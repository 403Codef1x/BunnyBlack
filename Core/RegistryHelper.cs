// language: C#, file: Core/RegistryHelper.cs
// Полная замена.
// - GetUserEntries теперь использует NetUserEnum (WinAPI) — работает под обычным админом.
// - SAM-чтение и ProfileList — как fallback для WinRE.
// - Остальные методы без изменений.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace BunnyBlack.Core
{
    public class AutostartItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public bool Exists { get; set; }
    }

    public class AppInitItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
    }

    public class CmdLineItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public class TaskItem
    {
        public string TaskName { get; set; }
        public string Action { get; set; }
        public string State { get; set; }
        public string Triggers { get; set; }
    }

    public class UserInfo
    {
        public string Name { get; set; }
        public string SID { get; set; }
        public string ProfilePath { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsActive { get; set; }
    }

    public class RegistryHelper
    {
        private const string OfflineSoftwareHive = @"BunnyBlack_Offline_SOFTWARE";
        private const string OfflineSystemHive = @"BunnyBlack_Offline_SYSTEM";
        private const string OfflineSamHive = @"BunnyBlack_Offline_SAM";
        private const string OfflineSecurityHive = @"BunnyBlack_Offline_SECURITY";
        private const string OfflineHkcuHive = @"BunnyBlack_Offline_HKCU";

        private static bool? _isWinRe = null;
        private static bool? _hivesLoaded = null;
        private static string _systemDrive = null;

        // ============================================================
        // P/INVOKE — advapi32
        // ============================================================
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegLoadKey(IntPtr hKey, string lpSubKey, string lpFile);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegUnLoadKey(IntPtr hKey, string lpSubKey);

        private const uint HKEY_LOCAL_MACHINE = 0x80000002;
        private const uint HKEY_USERS = 0x80000003;

        // ============================================================
        // P/INVOKE — NetAPI (user add/del/setinfo)
        // ============================================================
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserAdd(string servername, int level, ref USER_INFO_1 buf, out int parm_err);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserDel(string servername, string username);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserSetInfo(string servername, string username, int level, ref USER_INFO_1003 buf, out int parm_err);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserSetInfo(string servername, string username, int level, ref USER_INFO_1008 buf, out int parm_err);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserGetInfo(string servername, string username, int level, out IntPtr bufPtr);

        [DllImport("netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        // ============================================================
        // P/INVOKE — NetUserEnum (список пользователей)
        // ============================================================
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserEnum(
            string servername, int level, int filter,
            out IntPtr bufptr, int prefmaxlen,
            out int entriesread, out int totalentries, out int resume_handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct USER_INFO_0
        {
            public string usri0_name;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct USER_INFO_1
        {
            public string usri1_name;
            public string usri1_password;
            public int usri1_password_age;
            public int usri1_priv;
            public string usri1_home_dir;
            public string usri1_comment;
            public int usri1_flags;
            public string usri1_script_path;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct USER_INFO_1003 { public string usri1003_password; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct USER_INFO_1008 { public int usri1008_flags; }

        private const int UF_SCRIPT = 0x0001;
        private const int UF_ACCOUNTDISABLE = 0x0002;
        private const int UF_PASSWD_NOTREQD = 0x0020;
        private const int UF_DONT_EXPIRE_PASSWD = 0x10000;

        private const int FILTER_NORMAL_ACCOUNT = 0x0002;
        private const int MAX_PREFERRED_LENGTH = -1;
        private const int NERR_Success = 0;
        private const int USER_PRIV_ADMIN = 2;

        // ============================================================
        // WINRE
        // ============================================================
        public static bool IsWinReEnvironment()
        {
            if (_isWinRe.HasValue) return _isWinRe.Value;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT"))
                    if (key != null) { _isWinRe = true; return true; }

                string windir = Environment.GetEnvironmentVariable("windir") ?? "";
                if (windir.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase)) { _isWinRe = true; return true; }

                string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "";
                if (systemRoot.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase)) { _isWinRe = true; return true; }

                _isWinRe = false;
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IsWinReEnvironment] {ex.Message}");
                _isWinRe = false;
                return false;
            }
        }

        // ============================================================
        // HIVE LOADED
        // ============================================================
        private static bool IsHiveLoaded(string hiveName)
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(hiveName)) return k != null; }
            catch { return false; }
        }

        private static bool IsUserHiveLoaded(string hiveName)
        {
            try { using (var k = Registry.Users.OpenSubKey(hiveName)) return k != null; }
            catch { return false; }
        }

        private static void CleanupStaleHives()
        {
            string[] names = { OfflineSoftwareHive, OfflineSystemHive, OfflineSamHive, OfflineSecurityHive };
            foreach (var n in names)
            {
                try
                {
                    if (IsHiveLoaded(n))
                    {
                        RegUnLoadKey((IntPtr)HKEY_LOCAL_MACHINE, n);
                        Debug.WriteLine($"[CleanupStale] unloaded {n}");
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[CleanupStale/{n}] {ex.Message}"); }
            }
            try
            {
                if (IsUserHiveLoaded(OfflineHkcuHive))
                    RegUnLoadKey((IntPtr)HKEY_USERS, OfflineHkcuHive);
            }
            catch { }
        }

        // ============================================================
        // LOAD OFFLINE HIVES
        // ============================================================
        public static bool LoadOfflineHives() { string _; return LoadOfflineHives(out _); }

        public static bool LoadOfflineHives(out string statusMessage)
        {
            statusMessage = null;
            if (!IsWinReEnvironment()) return true;

            bool swLoaded = IsHiveLoaded(OfflineSoftwareHive);
            bool sysLoaded = IsHiveLoaded(OfflineSystemHive);
            bool samLoaded = IsHiveLoaded(OfflineSamHive);

            if (swLoaded && sysLoaded && samLoaded)
            {
                _hivesLoaded = true;
                statusMessage = "Оффлайн-кусты уже загружены";
                return true;
            }

            CleanupStaleHives();

            try
            {
                string systemDrive = GetSystemDrive();
                int loaded = 0;
                var errors = new List<string>();

                void TryLoad(IntPtr root, string hive, string file)
                {
                    if (!File.Exists(file)) { errors.Add($"{hive}: нет файла"); return; }
                    int r = RegLoadKey(root, hive, file);
                    if (r == 0) loaded++;
                    else errors.Add($"{hive}: код {r}");
                }

                TryLoad((IntPtr)HKEY_LOCAL_MACHINE, OfflineSoftwareHive, Path.Combine(systemDrive, @"Windows\System32\Config\SOFTWARE"));
                TryLoad((IntPtr)HKEY_LOCAL_MACHINE, OfflineSystemHive, Path.Combine(systemDrive, @"Windows\System32\Config\SYSTEM"));
                TryLoad((IntPtr)HKEY_LOCAL_MACHINE, OfflineSamHive, Path.Combine(systemDrive, @"Windows\System32\Config\SAM"));
                TryLoad((IntPtr)HKEY_LOCAL_MACHINE, OfflineSecurityHive, Path.Combine(systemDrive, @"Windows\System32\Config\SECURITY"));

                try
                {
                    string usersDir = Path.Combine(systemDrive, "Users");
                    string bestUser = null;
                    DateTime bestTime = DateTime.MinValue;

                    if (Directory.Exists(usersDir))
                    {
                        foreach (var dir in Directory.GetDirectories(usersDir))
                        {
                            string name = Path.GetFileName(dir);
                            if (name.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("All Users", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith(".")) continue;

                            string ntuser = Path.Combine(dir, "NTUSER.DAT");
                            if (!File.Exists(ntuser)) continue;
                            DateTime t = File.GetLastWriteTime(ntuser);
                            if (t > bestTime) { bestTime = t; bestUser = dir; }
                        }
                    }
                    if (bestUser != null)
                        TryLoad((IntPtr)HKEY_USERS, OfflineHkcuHive, Path.Combine(bestUser, "NTUSER.DAT"));
                }
                catch { }

                if (errors.Count > 0 && loaded == 0)
                {
                    statusMessage = "Не удалось загрузить: " + string.Join("; ", errors);
                    _hivesLoaded = false;
                    return false;
                }

                statusMessage = $"Загружено {loaded} из 5";
                if (errors.Count > 0) statusMessage += ". Проблемы: " + string.Join("; ", errors);
                _hivesLoaded = loaded > 0;
                return loaded > 0;
            }
            catch (Exception ex)
            {
                statusMessage = "Ошибка: " + ex.Message;
                _hivesLoaded = false;
                return false;
            }
        }

        public static bool EnsureOfflineHives() { return LoadOfflineHives(); }
        public static bool EnsureOfflineHives(out string status) { return LoadOfflineHives(out status); }

        public static void UnloadOfflineHives()
        {
            try
            {
                if (IsHiveLoaded(OfflineSoftwareHive)) RegUnLoadKey((IntPtr)HKEY_LOCAL_MACHINE, OfflineSoftwareHive);
                if (IsHiveLoaded(OfflineSystemHive)) RegUnLoadKey((IntPtr)HKEY_LOCAL_MACHINE, OfflineSystemHive);
                if (IsHiveLoaded(OfflineSamHive)) RegUnLoadKey((IntPtr)HKEY_LOCAL_MACHINE, OfflineSamHive);
                if (IsHiveLoaded(OfflineSecurityHive)) RegUnLoadKey((IntPtr)HKEY_LOCAL_MACHINE, OfflineSecurityHive);
                if (IsUserHiveLoaded(OfflineHkcuHive)) RegUnLoadKey((IntPtr)HKEY_USERS, OfflineHkcuHive);
                _hivesLoaded = false;
            }
            catch { }
        }

        // ============================================================
        // SYSTEM DRIVE
        // ============================================================
        public static string GetSystemDrive()
        {
            if (!string.IsNullOrEmpty(_systemDrive)) return _systemDrive;
            try
            {
                string[] drives = { "C:", "D:", "E:", "F:", "G:", "H:" };
                foreach (string drive in drives)
                    if (File.Exists(drive + @"\Windows\System32\Config\SOFTWARE")) { _systemDrive = drive; return drive; }

                foreach (string drive in drives)
                    if (Directory.Exists(drive + @"\Users")) { _systemDrive = drive; return drive; }

                _systemDrive = "C:";
                return "C:";
            }
            catch { _systemDrive = "C:"; return "C:"; }
        }

        // ============================================================
        // OPEN SUBKEY
        // ============================================================
        private static RegistryKey OpenWindowsSubKey(string path, bool writable = false)
        {
            if (!IsWinReEnvironment())
                return Registry.LocalMachine.OpenSubKey(path, writable);

            LoadOfflineHives();

            string fullPath;
            if (path.StartsWith("SYSTEM\\", StringComparison.OrdinalIgnoreCase))
                fullPath = OfflineSystemHive + "\\" + path.Substring(7);
            else if (path.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase))
                fullPath = OfflineSoftwareHive + "\\" + path.Substring(9);
            else if (path.StartsWith("SAM\\", StringComparison.OrdinalIgnoreCase))
                fullPath = OfflineSamHive + "\\" + path.Substring(4);
            else if (path.StartsWith("SECURITY\\", StringComparison.OrdinalIgnoreCase))
                fullPath = OfflineSecurityHive + "\\" + path.Substring(9);
            else
                fullPath = OfflineSoftwareHive + "\\" + path;

            return Registry.LocalMachine.OpenSubKey(fullPath, writable);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (path.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase))
                return GetSystemDrive() + "\\" + path.Substring(3);

            if (IsWinReEnvironment())
            {
                string systemDrive = GetSystemDrive();
                if (path.Length > 2 && path[1] == ':' && char.IsLetter(path[0]))
                    if (!path.StartsWith(systemDrive, StringComparison.OrdinalIgnoreCase))
                        return systemDrive + path.Substring(2);
            }
            return path;
        }

        public static bool IsFirstRunWinRE()
        {
            try
            {
                using (var k = OpenWindowsSubKey(@"SOFTWARE\BunnyBlack", true))
                {
                    if (k == null) return true;
                    if (k.GetValue("FirstRun") == null) { k.SetValue("FirstRun", "done"); return true; }
                    return false;
                }
            }
            catch { return false; }
        }

        // ============================================================
        // РАСШИРЕННЫЙ СПИСОК ПОЛЬЗОВАТЕЛЕЙ
        // ============================================================
        public class UserEntry
        {
            public string Name { get; set; }
            public string SID { get; set; }
            public int RID { get; set; }
            public bool Disabled { get; set; }
            public bool IsAdmin { get; set; }
            public string ProfilePath { get; set; }
            public string Source { get; set; }
        }

        public static List<UserEntry> GetUserEntries()
        {
            var result = new List<UserEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ---------- 1) NetUserEnum ----------
            try
            {
                IntPtr buf = IntPtr.Zero;
                int read = 0, total = 0, resume = 0;

                int rc = NetUserEnum(null, 1, FILTER_NORMAL_ACCOUNT,
                    out buf, MAX_PREFERRED_LENGTH, out read, out total, out resume);

                if (rc == NERR_Success && buf != IntPtr.Zero)
                {
                    try
                    {
                        int structSize = Marshal.SizeOf<USER_INFO_1>();
                        IntPtr cur = buf;

                        for (int i = 0; i < read; i++)
                        {
                            var u = Marshal.PtrToStructure<USER_INFO_1>(cur);
                            cur = (IntPtr)((long)cur + structSize);

                            if (string.IsNullOrEmpty(u.usri1_name)) continue;
                            if (seen.Contains(u.usri1_name)) continue;

                            bool disabled = (u.usri1_flags & UF_ACCOUNTDISABLE) != 0;
                            bool isAdmin = (u.usri1_priv == USER_PRIV_ADMIN);

                            string sid; int rid;
                            TryGetSidAndRid(u.usri1_name, out sid, out rid);

                            result.Add(new UserEntry
                            {
                                Name = u.usri1_name,
                                SID = sid,
                                RID = rid,
                                Disabled = disabled,
                                IsAdmin = isAdmin,
                                ProfilePath = GetUserProfilePath(u.usri1_name),
                                Source = "NetAPI"
                            });
                            seen.Add(u.usri1_name);
                        }
                    }
                    finally { NetApiBufferFree(buf); }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[GetUserEntries/NetAPI] {ex.Message}"); }

            // ---------- 2) SAM (fallback для WinRE) ----------
            if (result.Count == 0)
            {
                try
                {
                    using (var namesKey = OpenWindowsSubKey(@"SAM\SAM\Domains\Account\Users\Names"))
                    {
                        if (namesKey != null)
                        {
                            foreach (var name in namesKey.GetSubKeyNames())
                            {
                                try
                                {
                                    if (seen.Contains(name)) continue;

                                    byte[] sidBytes = null;
                                    using (var nk = namesKey.OpenSubKey(name))
                                        sidBytes = nk?.GetValue("") as byte[];

                                    string sidStr = SidBytesToString(sidBytes);
                                    int rid = ExtractRid(sidBytes);

                                    bool disabled = false;
                                    if (rid > 0)
                                    {
                                        string hexRid = rid.ToString("X8");
                                        using (var fk = OpenWindowsSubKey(@"SAM\SAM\Domains\Account\Users\" + hexRid))
                                        {
                                            byte[] f = fk?.GetValue("F") as byte[];
                                            if (f != null && f.Length > 0x3C)
                                            {
                                                int flags = BitConverter.ToInt32(f, 0x38);
                                                disabled = (flags & UF_ACCOUNTDISABLE) != 0;
                                            }
                                        }
                                    }

                                    result.Add(new UserEntry
                                    {
                                        Name = name,
                                        SID = sidStr,
                                        RID = rid,
                                        Disabled = disabled,
                                        IsAdmin = (rid == 500),
                                        ProfilePath = GetUserProfilePath(name),
                                        Source = "SAM"
                                    });
                                    seen.Add(name);
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[GetUserEntries/SAM] {ex.Message}"); }
            }

            // ---------- 3) добить SID из ProfileList ----------
            foreach (var u in result)
            {
                if (!string.IsNullOrEmpty(u.SID)) continue;
                string sid; int rid;
                TryGetSidAndRid(u.Name, out sid, out rid);
                if (!string.IsNullOrEmpty(sid)) { u.SID = sid; u.RID = rid; }
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // ============================================================
        // SID из ProfileList
        // ============================================================
        private static void TryGetSidAndRid(string userName, out string sid, out int rid)
        {
            sid = ""; rid = 0;
            try
            {
                using (var key = OpenWindowsSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
                {
                    if (key == null) return;
                    foreach (var s in key.GetSubKeyNames())
                    {
                        using (var sub = key.OpenSubKey(s))
                        {
                            if (sub == null) continue;
                            string path = sub.GetValue("ProfileImagePath")?.ToString() ?? "";
                            if (path.EndsWith("\\" + userName, StringComparison.OrdinalIgnoreCase))
                            {
                                sid = s;
                                int lastDash = s.LastIndexOf('-');
                                if (lastDash > 0 && int.TryParse(s.Substring(lastDash + 1), out int r))
                                    rid = r;
                                return;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static string SidBytesToString(byte[] sid)
        {
            if (sid == null || sid.Length < 8) return "";
            try
            {
                byte revision = sid[0];
                byte subCount = sid[1];
                var authority = new byte[6];
                Array.Copy(sid, 2, authority, 0, 6);
                ulong authValue = 0;
                for (int i = 5; i >= 0; i--) authValue = (authValue << 8) | authority[i];

                var sb = new System.Text.StringBuilder();
                sb.Append("S-").Append(revision).Append('-').Append(authValue);

                int offset = 8;
                for (int i = 0; i < subCount && offset + 4 <= sid.Length; i++)
                {
                    uint sub = BitConverter.ToUInt32(sid, offset);
                    offset += 4;
                    sb.Append('-').Append(sub);
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        private static int ExtractRid(byte[] sid)
        {
            if (sid == null || sid.Length < 12) return 0;
            try
            {
                byte subCount = sid[1];
                int offset = 8 + (subCount - 1) * 4;
                if (offset + 4 > sid.Length) return 0;
                return (int)BitConverter.ToUInt32(sid, offset);
            }
            catch { return 0; }
        }

        private static string GetUserProfilePath(string userName)
        {
            try
            {
                using (var key = OpenWindowsSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
                {
                    if (key != null)
                    {
                        foreach (string sid in key.GetSubKeyNames())
                        {
                            using (var subKey = key.OpenSubKey(sid))
                            {
                                if (subKey != null)
                                {
                                    string profilePath = subKey.GetValue("ProfileImagePath")?.ToString() ?? "";
                                    if (profilePath.Contains(userName, StringComparison.OrdinalIgnoreCase))
                                        return profilePath;
                                }
                            }
                        }
                    }
                }
                return "";
            }
            catch { return ""; }
        }

        // ============================================================
        // GetUsers (legacy)
        // ============================================================
        public static List<UserInfo> GetUsers()
        {
            var list = new List<UserInfo>();
            foreach (var u in GetUserEntries())
                list.Add(new UserInfo
                {
                    Name = u.Name,
                    SID = u.SID,
                    ProfilePath = u.ProfilePath,
                    IsAdmin = u.IsAdmin,
                    IsActive = !u.Disabled
                });
            return list;
        }

        // ============================================================
        // CREATE USER
        // ============================================================
        public static bool CreateUserNetApi(string username, string password, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                errorMessage = "Имя и пароль не могут быть пустыми.";
                return false;
            }

            try
            {
                USER_INFO_1 userInfo = new USER_INFO_1
                {
                    usri1_name = username,
                    usri1_password = password,
                    usri1_password_age = 0,
                    usri1_priv = 1,
                    usri1_home_dir = null,
                    usri1_comment = null,
                    usri1_flags = UF_DONT_EXPIRE_PASSWD | UF_SCRIPT,
                    usri1_script_path = null
                };

                int parm_err;
                int result = NetUserAdd(null, 1, ref userInfo, out parm_err);
                if (result != 0)
                {
                    errorMessage = DecodeNetError(result);
                    return false;
                }

                USER_INFO_1003 pw = new USER_INFO_1003 { usri1003_password = password };
                int parm_err2;
                int setResult = NetUserSetInfo(null, username, 1003, ref pw, out parm_err2);
                if (setResult != 0)
                {
                    errorMessage = "Пользователь создан, но пароль не установлен: " + DecodeNetError(setResult);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Исключение: {ex.Message}";
                return false;
            }
        }

        public static bool CreateUserNetApi(string username, string password)
            => CreateUserNetApi(username, password, out _);

        private static string DecodeNetError(int code)
        {
            switch (code)
            {
                case 0: return "Успешно.";
                case 5: return "Отказано в доступе.";
                case 53: return "Сеть недоступна.";
                case 87: return "Неверный параметр.";
                case 2224: return "Пользователь уже существует.";
                case 2245: return "Пароль слишком короткий.";
                case 2246: return "Пароль слишком длинный.";
                case 2247: return "Пароль не соответствует требованиям.";
                case 2248: return "Пароль слишком новый.";
                case 2250: return "Имя пользователя не найдено.";
                case 2251: return "Недостаточно привилегий.";
                case 2252: return "Пользователь уже существует.";
                case 2253: return "Имя пользователя не соответствует требованиям.";
                default: return $"Ошибка NetAPI (код {code}).";
            }
        }

        public static bool DeleteUserNetApi(string username)
        {
            if (string.IsNullOrEmpty(username)) return false;
            try { return NetUserDel(null, username) == 0; } catch { return false; }
        }

        public static bool SetUserPasswordNetApi(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return false;
            try
            {
                USER_INFO_1003 ui = new USER_INFO_1003 { usri1003_password = password };
                int pe;
                return NetUserSetInfo(null, username, 1003, ref ui, out pe) == 0;
            }
            catch { return false; }
        }

        public static bool EnableUserNetApi(string username) => SetUserActiveStatus(username, true);
        public static bool DisableUserNetApi(string username) => SetUserActiveStatus(username, false);

        private static bool SetUserActiveStatus(string username, bool enable)
        {
            if (string.IsNullOrEmpty(username)) return false;
            try
            {
                IntPtr bufPtr;
                int result = NetUserGetInfo(null, username, 1008, out bufPtr);
                if (result != 0) return false;

                var info = (USER_INFO_1008)Marshal.PtrToStructure(bufPtr, typeof(USER_INFO_1008));
                NetApiBufferFree(bufPtr);

                if (enable) info.usri1008_flags &= ~UF_ACCOUNTDISABLE;
                else info.usri1008_flags |= UF_ACCOUNTDISABLE;

                USER_INFO_1008 updated = new USER_INFO_1008 { usri1008_flags = info.usri1008_flags };
                int pe;
                return NetUserSetInfo(null, username, 1008, ref updated, out pe) == 0;
            }
            catch { return false; }
        }

        // ============================================================
        // RUN / RUNONCE
        // ============================================================
        public static List<AutostartItem> GetRunItems()
        {
            var items = new List<AutostartItem>();

            if (IsWinReEnvironment())
            {
                LoadOfflineHives();
                AddRunFromHive(items, OfflineSoftwareHive, @"Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run (offline)");
                AddRunFromHive(items, OfflineSoftwareHive, @"Wow6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run (offline/32)");
                AddRunFromUserHive(items, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU\\Run (offline)");
                return items;
            }

            void Add(RegistryKey key, string type)
            {
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name)?.ToString() ?? "";
                    bool dup = false;
                    foreach (var item in items)
                        if (item.Name == name && item.Path == value && item.Type == type) { dup = true; break; }
                    if (!dup)
                        items.Add(new AutostartItem
                        {
                            Name = name,
                            Path = value,
                            Type = type,
                            Exists = File.Exists(NormalizePath(value?.Split(' ')[0]?.Trim('"') ?? ""))
                        });
                }
            }

            try { Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "HKLM\\Run (64bit)"); } catch { }
            try { Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "HKLM\\Run (32bit)"); } catch { }
            try { Add(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "HKCU\\Run"); } catch { }

            return items;
        }

        private static void AddRunFromHive(List<AutostartItem> items, string hiveName, string subPath, string type)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(hiveName + "\\" + subPath))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames())
                    {
                        var value = key.GetValue(name)?.ToString() ?? "";
                        bool dup = false;
                        foreach (var it in items)
                            if (it.Name == name && it.Path == value && it.Type == type) { dup = true; break; }
                        if (dup) continue;
                        items.Add(new AutostartItem { Name = name, Path = value, Type = type, Exists = File.Exists(NormalizePath(value?.Split(' ')[0]?.Trim('"') ?? "")) });
                    }
                }
            }
            catch { }
        }

        private static void AddRunFromUserHive(List<AutostartItem> items, string subPath, string type)
        {
            try
            {
                if (!IsUserHiveLoaded(OfflineHkcuHive)) return;
                using (var key = Registry.Users.OpenSubKey(OfflineHkcuHive + "\\" + subPath))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames())
                    {
                        var value = key.GetValue(name)?.ToString() ?? "";
                        bool dup = false;
                        foreach (var it in items)
                            if (it.Name == name && it.Path == value && it.Type == type) { dup = true; break; }
                        if (dup) continue;
                        items.Add(new AutostartItem { Name = name, Path = value, Type = type, Exists = File.Exists(NormalizePath(value?.Split(' ')[0]?.Trim('"') ?? "")) });
                    }
                }
            }
            catch { }
        }

        public static List<AutostartItem> GetRunOnceItems()
        {
            var items = new List<AutostartItem>();

            if (IsWinReEnvironment())
            {
                LoadOfflineHives();
                AddRunFromHive(items, OfflineSoftwareHive, @"Microsoft\Windows\CurrentVersion\RunOnce", "HKLM\\RunOnce (offline)");
                AddRunFromHive(items, OfflineSoftwareHive, @"Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM\\RunOnce (offline/32)");
                AddRunFromUserHive(items, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU\\RunOnce (offline)");
                return items;
            }

            void Add(RegistryKey key, string type)
            {
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name)?.ToString() ?? "";
                    bool dup = false;
                    foreach (var it in items) if (it.Name == name && it.Path == value && it.Type == type) { dup = true; break; }
                    if (!dup)
                        items.Add(new AutostartItem { Name = name, Path = value, Type = type, Exists = File.Exists(NormalizePath(value?.Split(' ')[0]?.Trim('"') ?? "")) });
                }
            }

            try { Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), "HKLM\\RunOnce (64bit)"); } catch { }
            try { Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), "HKLM\\RunOnce (32bit)"); } catch { }
            try { Add(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), "HKCU\\RunOnce"); } catch { }

            return items;
        }

        public static List<AutostartItem> GetWinlogonItems()
        {
            var items = new List<AutostartItem>();
            string[] params_ = { "Shell", "Userinit", "Taskman", "System" };

            try
            {
                RegistryKey key = IsWinReEnvironment()
                    ? (LoadOfflineHives() ? Registry.LocalMachine.OpenSubKey(OfflineSoftwareHive + @"\Microsoft\Windows NT\CurrentVersion\Winlogon") : null)
                    : Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");

                using (key)
                {
                    if (key != null)
                    {
                        foreach (var param in params_)
                        {
                            var value = key.GetValue(param)?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(value))
                                items.Add(new AutostartItem { Name = param, Path = NormalizePath(value), Type = "Winlogon (HKLM)", Exists = true });
                        }
                    }
                }
            }
            catch { }
            return items;
        }

        public static List<AutostartItem> GetStartupFolderItems()
        {
            var items = new List<AutostartItem>();
            try
            {
                string systemDrive = GetSystemDrive();
                string[] folders;

                if (IsWinReEnvironment())
                {
                    string usersPath = Path.Combine(systemDrive, @"Users");
                    var startupPaths = new List<string>();
                    if (Directory.Exists(usersPath))
                    {
                        foreach (string userDir in Directory.GetDirectories(usersPath))
                        {
                            string sp = Path.Combine(userDir, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
                            if (Directory.Exists(sp)) startupPaths.Add(sp);
                        }
                    }
                    string commonStartup = Path.Combine(systemDrive, @"ProgramData\Microsoft\Windows\Start Menu\Programs\Startup");
                    if (Directory.Exists(commonStartup)) startupPaths.Add(commonStartup);
                    folders = startupPaths.ToArray();
                }
                else
                {
                    folders = new string[] {
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                        Environment.GetFolderPath(Environment.SpecialFolder.Startup)
                    };
                }

                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (var file in Directory.GetFiles(folder))
                    {
                        var name = Path.GetFileName(file);
                        if (!name.StartsWith("~") && !name.StartsWith("."))
                            items.Add(new AutostartItem { Name = name, Path = file, Type = "Папка автозагрузки", Exists = true });
                    }
                }
            }
            catch { }
            return items;
        }

        public static bool DeleteRunItem(string name, string type)
        {
            try
            {
                if (IsWinReEnvironment())
                {
                    LoadOfflineHives();
                    string subPath = type.Contains("RunOnce")
                        ? @"Microsoft\Windows\CurrentVersion\RunOnce"
                        : @"Microsoft\Windows\CurrentVersion\Run";

                    if (type.StartsWith("HKCU"))
                    {
                        if (!IsUserHiveLoaded(OfflineHkcuHive)) return false;
                        using (var key = Registry.Users.OpenSubKey(OfflineHkcuHive + @"\Software\Microsoft\Windows\CurrentVersion\" + (type.Contains("RunOnce") ? "RunOnce" : "Run"), true))
                        {
                            if (key != null && key.GetValue(name) != null) { key.DeleteValue(name); return true; }
                        }
                        return false;
                    }

                    using (var key = Registry.LocalMachine.OpenSubKey(OfflineSoftwareHive + "\\" + subPath, true))
                    {
                        if (key != null && key.GetValue(name) != null) { key.DeleteValue(name); return true; }
                    }
                    return false;
                }

                using (var k64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\" + (type.Contains("RunOnce") ? "RunOnce" : "Run"), true))
                    if (k64 != null && k64.GetValue(name) != null) { k64.DeleteValue(name); return true; }

                using (var k32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\" + (type.Contains("RunOnce") ? "RunOnce" : "Run"), true))
                    if (k32 != null && k32.GetValue(name) != null) { k32.DeleteValue(name); return true; }

                using (var ku = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\" + (type.Contains("RunOnce") ? "RunOnce" : "Run"), true))
                    if (ku != null && ku.GetValue(name) != null) { ku.DeleteValue(name); return true; }

                return false;
            }
            catch (UnauthorizedAccessException) { throw; }
            catch { return false; }
        }

        public static bool DeleteFileItem(string path, out string error)
        {
            error = null;
            try
            {
                string p = NormalizePath(path);
                if (!File.Exists(p)) { error = "Файл не найден."; return false; }
                File.Delete(p);
                return true;
            }
            catch (UnauthorizedAccessException ex) { error = "Отказано в доступе: " + ex.Message; return false; }
            catch (IOException ex) { error = "Ошибка ввода-вывода: " + ex.Message; return false; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool DeleteFileItem(string path) => DeleteFileItem(path, out _);

        public static bool SetWinlogonValue(string name, string value)
        {
            try
            {
                if (IsWinReEnvironment())
                {
                    LoadOfflineHives();
                    using (var key = Registry.LocalMachine.OpenSubKey(OfflineSoftwareHive + @"\Microsoft\Windows NT\CurrentVersion\Winlogon", true))
                    {
                        if (key != null) { key.SetValue(name, NormalizePath(value)); return true; }
                    }
                    return false;
                }

                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true))
                {
                    if (key != null) { key.SetValue(name, NormalizePath(value)); return true; }
                }
                return false;
            }
            catch { return false; }
        }

        public static List<AppInitItem> GetAppInitDllsItems()
        {
            var list = new List<AppInitItem>();
            try
            {
                RegistryKey key = IsWinReEnvironment()
                    ? (LoadOfflineHives() ? Registry.LocalMachine.OpenSubKey(OfflineSoftwareHive + @"\Microsoft\Windows NT\CurrentVersion\Windows") : null)
                    : Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows");

                using (key)
                {
                    if (key != null)
                    {
                        list.Add(new AppInitItem { Name = "AppInit_DLLs", Path = key.GetValue("AppInit_DLLs")?.ToString() ?? "", Type = "HKLM" });
                        list.Add(new AppInitItem { Name = "LoadAppInit_DLLs", Path = key.GetValue("LoadAppInit_DLLs")?.ToString() ?? "0", Type = "HKLM" });
                        list.Add(new AppInitItem { Name = "RequireSignedAppInit_DLLs", Path = key.GetValue("RequireSignedAppInit_DLLs")?.ToString() ?? "0", Type = "HKLM" });
                    }
                }
            }
            catch { }
            return list;
        }

        public static bool SetAppInitDllsValue(string name, string value)
        {
            try
            {
                string path = IsWinReEnvironment()
                    ? OfflineSoftwareHive + @"\Microsoft\Windows NT\CurrentVersion\Windows"
                    : @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows";

                using (var key = Registry.LocalMachine.OpenSubKey(path, true))
                {
                    if (key != null) { key.SetValue(name, value); return true; }
                }
                return false;
            }
            catch { return false; }
        }

        public static bool DeleteAppInitDllsValue(string name)
        {
            try
            {
                string path = IsWinReEnvironment()
                    ? OfflineSoftwareHive + @"\Microsoft\Windows NT\CurrentVersion\Windows"
                    : @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows";

                using (var key = Registry.LocalMachine.OpenSubKey(path, true))
                {
                    if (key != null && key.GetValue(name) != null)
                    {
                        if (name == "AppInit_DLLs") key.SetValue(name, "");
                        else key.SetValue(name, 0);
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        public static List<CmdLineItem> GetCmdLineAutoRunItems()
        {
            var list = new List<CmdLineItem>();
            try
            {
                RegistryKey setupKey = IsWinReEnvironment()
                    ? (LoadOfflineHives() ? Registry.LocalMachine.OpenSubKey(OfflineSystemHive + @"\Setup") : null)
                    : Registry.LocalMachine.OpenSubKey(@"SYSTEM\Setup");

                using (setupKey)
                {
                    if (setupKey != null)
                    {
                        string cmdLine = setupKey.GetValue("CmdLine")?.ToString() ?? "";
                        list.Add(new CmdLineItem { Name = "CmdLine", Path = string.IsNullOrEmpty(cmdLine) ? "(пусто)" : cmdLine });
                        string setupType = setupKey.GetValue("SetupType")?.ToString() ?? "";
                        list.Add(new CmdLineItem { Name = "SetupType", Path = string.IsNullOrEmpty(setupType) ? "(пусто)" : setupType });
                    }
                }

                RegistryKey polKey = IsWinReEnvironment()
                    ? Registry.LocalMachine.OpenSubKey(OfflineSoftwareHive + @"\Microsoft\Windows\CurrentVersion\Policies\System")
                    : Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");

                using (polKey)
                {
                    if (polKey != null)
                    {
                        string cur = polKey.GetValue("EnableCursorSuppression")?.ToString() ?? "";
                        list.Add(new CmdLineItem { Name = "EnableCursorSuppression", Path = string.IsNullOrEmpty(cur) ? "(пусто)" : cur });
                    }
                }
            }
            catch { }
            return list;
        }

        public static bool SetCmdLineValue(string name, string value)
        {
            try
            {
                if (name == "CmdLine" || name == "SetupType")
                {
                    string path = IsWinReEnvironment() ? OfflineSystemHive + @"\Setup" : @"SYSTEM\Setup";
                    using (var key = Registry.LocalMachine.OpenSubKey(path, true))
                    {
                        if (key != null) { key.SetValue(name, value); return true; }
                    }
                    return false;
                }

                if (name == "EnableCursorSuppression")
                {
                    string path = IsWinReEnvironment()
                        ? OfflineSoftwareHive + @"\Microsoft\Windows\CurrentVersion\Policies\System"
                        : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
                    using (var key = Registry.LocalMachine.OpenSubKey(path, true))
                    {
                        if (key != null) { key.SetValue(name, value); return true; }
                    }
                    return false;
                }

                return SetAppInitDllsValue(name, value);
            }
            catch { return false; }
        }

        public static bool DeleteCmdLineAutoRun(string name)
        {
            try
            {
                if (name == "CmdLine" || name == "SetupType")
                {
                    string path = IsWinReEnvironment() ? OfflineSystemHive + @"\Setup" : @"SYSTEM\Setup";
                    using (var key = Registry.LocalMachine.OpenSubKey(path, true))
                    {
                        if (key != null && key.GetValue(name) != null) { key.SetValue(name, ""); return true; }
                    }
                    return false;
                }

                if (name == "EnableCursorSuppression")
                {
                    string path = IsWinReEnvironment()
                        ? OfflineSoftwareHive + @"\Microsoft\Windows\CurrentVersion\Policies\System"
                        : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
                    using (var key = Registry.LocalMachine.OpenSubKey(path, true))
                    {
                        if (key != null && key.GetValue(name) != null) { key.SetValue(name, ""); return true; }
                    }
                    return false;
                }

                return DeleteAppInitDllsValue(name);
            }
            catch { return false; }
        }

        public static List<TaskItem> GetTaskSchedulerItems()
        {
            var list = new List<TaskItem>();
            if (IsWinReEnvironment()) return GetOfflineTaskItems();

            try
            {
                using (var ts = new TaskService())
                {
                    foreach (var task in ts.FindAllTasks(t => true))
                    {
                        string actions = "";
                        if (task.Definition.Actions.Count > 0)
                        {
                            var action = task.Definition.Actions[0] as ExecAction;
                            if (action != null)
                                actions = action.Path + (!string.IsNullOrEmpty(action.Arguments) ? " " + action.Arguments : "");
                            else
                                actions = task.Definition.Actions[0].ToString();
                        }

                        string triggers = "";
                        foreach (var t in task.Definition.Triggers) triggers += t.ToString() + "; ";
                        if (triggers.Length > 2) triggers = triggers.Substring(0, triggers.Length - 2);

                        list.Add(new TaskItem
                        {
                            TaskName = task.Name,
                            Action = actions,
                            State = task.State.ToString(),
                            Triggers = triggers
                        });
                    }
                }
                return list;
            }
            catch
            {
                try { return GetOfflineTaskItems(); } catch { }
            }
            return list;
        }

        private static List<TaskItem> GetOfflineTaskItems()
        {
            var tasks = new List<TaskItem>();
            string systemDrive = GetSystemDrive();
            string tasksPath = Path.Combine(systemDrive, @"Windows\System32\Tasks");
            if (!Directory.Exists(tasksPath)) return tasks;

            try
            {
                foreach (string file in Directory.GetFiles(tasksPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        string content = null;
                        try { content = File.ReadAllText(file); } catch { continue; }
                        if (string.IsNullOrWhiteSpace(content) || !content.Contains("<Task")) continue;

                        string taskName = Path.GetFileNameWithoutExtension(file);
                        string action = "Неизвестно";
                        string triggers = "Неизвестно";

                        XmlDocument doc = new XmlDocument();
                        doc.LoadXml(content);
                        XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
                        ns.AddNamespace("ns", "http://schemas.microsoft.com/windows/2004/02/mit/task");

                        var execNode = doc.SelectSingleNode("//ns:Exec", ns);
                        if (execNode != null)
                        {
                            string command = execNode.SelectSingleNode("ns:Command", ns)?.InnerText ?? "";
                            string args = execNode.SelectSingleNode("ns:Arguments", ns)?.InnerText ?? "";
                            action = command + (!string.IsNullOrEmpty(args) ? " " + args : "");
                        }

                        var triggersNode = doc.SelectSingleNode("//ns:Triggers", ns);
                        if (triggersNode != null)
                        {
                            var triggerList = new List<string>();
                            foreach (XmlNode child in triggersNode.ChildNodes)
                                if (child.Name.EndsWith("Trigger"))
                                    triggerList.Add(child.Name.Replace("ns:", "").Replace("Trigger", ""));
                            triggers = string.Join("; ", triggerList);
                            if (string.IsNullOrEmpty(triggers)) triggers = "Без триггера";
                        }

                        tasks.Add(new TaskItem
                        {
                            TaskName = taskName,
                            Action = action,
                            State = "Offline (XML)",
                            Triggers = triggers
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return tasks;
        }

        public static bool DeleteTaskSchedulerTask(string taskName)
        {
            if (IsWinReEnvironment())
            {
                try
                {
                    string systemDrive = GetSystemDrive();
                    string tasksDir = Path.Combine(systemDrive, @"Windows\System32\Tasks");
                    foreach (string file in Directory.GetFiles(tasksDir, "*", SearchOption.AllDirectories))
                    {
                        if (Path.GetFileNameWithoutExtension(file).Equals(taskName, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                            return true;
                        }
                    }
                    return false;
                }
                catch { return false; }
            }

            try
            {
                using (var ts = new TaskService())
                {
                    ts.RootFolder.DeleteTask(taskName, false);
                    return true;
                }
            }
            catch { return false; }
        }

        public static bool BackupRegistry()
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), $"registry_backup_{DateTime.Now:yyyyMMdd_HHmmss}.reg");
                var si = new ProcessStartInfo
                {
                    FileName = "reg",
                    Arguments = $"export HKLM \"{path.Replace(".reg", "_HKLM.reg")}\" /y",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(si)) { if (p != null) p.WaitForExit(); }
                si.Arguments = $"export HKCU \"{path.Replace(".reg", "_HKCU.reg")}\" /y";
                using (var p = Process.Start(si)) { if (p != null) p.WaitForExit(); }
                return true;
            }
            catch { return false; }
        }
    }
}