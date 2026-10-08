// language: C#, file: Core/RegistryHelper.cs
// Полная замена.
// ФИКС: GetUserEntries теперь использует WMI Win32_UserAccount как основной источник.
// Это работает под админом в живой системе без SYSTEM-прав.
// Fallback: SAM (WinRE) → папки C:\Users\*.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
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

    public static class RegistryHelper
    {
        private const string HIVE_SOFTWARE = @"BB_Offline_SOFTWARE";
        private const string HIVE_SYSTEM = @"BB_Offline_SYSTEM";
        private const string HIVE_SAM = @"BB_Offline_SAM";
        private const string HIVE_SECURITY = @"BB_Offline_SECURITY";
        private const string HIVE_HKCU = @"BB_Offline_HKCU";

        private static bool? _isWinRe = null;
        private static string _systemDrive = null;
        private static bool _hivesLoaded = false;
        private static bool _hkcuLoaded = false;
        private static readonly object _lock = new object();

        // ============================================================
        // P/INVOKE
        // ============================================================
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegLoadKey(IntPtr hKey, string lpSubKey, string lpFile);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegUnLoadKey(IntPtr hKey, string lpSubKey);

        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));
        private static readonly IntPtr HKEY_USERS = new IntPtr(unchecked((int)0x80000003));

        // ============================================================
        // NetAPI
        // ============================================================
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

        private const int UF_SCRIPT = 0x0001;
        private const int UF_ACCOUNTDISABLE = 0x0002;
        private const int UF_DONT_EXPIRE_PASSWD = 0x10000;

        // ============================================================
        // WinRE DETECT
        // ============================================================
        public static bool IsWinReEnvironment()
        {
            if (_isWinRe.HasValue) return _isWinRe.Value;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT"))
                    if (k != null) { _isWinRe = true; return true; }

                string windir = Environment.GetEnvironmentVariable("windir") ?? "";
                if (windir.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase)) { _isWinRe = true; return true; }

                string sr = Environment.GetEnvironmentVariable("SystemRoot") ?? "";
                if (sr.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase)) { _isWinRe = true; return true; }

                _isWinRe = false;
                return false;
            }
            catch { _isWinRe = false; return false; }
        }

        // ============================================================
        // SYSTEM DRIVE
        // ============================================================
        public static string GetSystemDrive()
        {
            if (!string.IsNullOrEmpty(_systemDrive)) return _systemDrive;

            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;

                        string root = d.RootDirectory.FullName.TrimEnd('\\');
                        if (root.StartsWith("X:", StringComparison.OrdinalIgnoreCase)) continue;

                        string configSW = Path.Combine(root, @"Windows\System32\Config\SOFTWARE");
                        if (File.Exists(configSW)) { _systemDrive = root; return root; }

                        string usersDir = Path.Combine(root, "Users");
                        if (Directory.Exists(usersDir)) { _systemDrive = root; return root; }
                    }
                    catch { }
                }
            }
            catch { }

            _systemDrive = "C:";
            return "C:";
        }

        // ============================================================
        // HIVE LOAD
        // ============================================================
        public static bool LoadOfflineHives() { string _; return LoadOfflineHives(out _); }

        public static bool LoadOfflineHives(out string status)
        {
            lock (_lock)
            {
                status = null;
                if (!IsWinReEnvironment()) { status = "Не WinRE"; return true; }
                if (_hivesLoaded) { status = "Уже загружены"; return true; }

                string drive = GetSystemDrive();
                if (!drive.EndsWith(":")) drive += ":";

                string cfg = Path.Combine(drive, @"Windows\System32\Config");
                if (!Directory.Exists(cfg))
                {
                    status = $"Не найдена папка {cfg}";
                    return false;
                }

                int loaded = 0;
                var errors = new List<string>();

                loaded += TryLoadHive(HIVE_SOFTWARE, Path.Combine(cfg, "SOFTWARE"), errors) ? 1 : 0;
                loaded += TryLoadHive(HIVE_SYSTEM, Path.Combine(cfg, "SYSTEM"), errors) ? 1 : 0;
                loaded += TryLoadHive(HIVE_SAM, Path.Combine(cfg, "SAM"), errors) ? 1 : 0;
                loaded += TryLoadHive(HIVE_SECURITY, Path.Combine(cfg, "SECURITY"), errors) ? 1 : 0;

                _hivesLoaded = loaded > 0;
                status = $"Загружено {loaded}/4";
                if (errors.Count > 0) status += ". Проблемы: " + string.Join("; ", errors);
                return loaded > 0;
            }
        }

        private static bool TryLoadHive(string hiveName, string file, List<string> errors)
        {
            try
            {
                if (!File.Exists(file)) { errors.Add($"{hiveName}: нет файла"); return false; }

                try
                {
                    using (var test = Registry.LocalMachine.OpenSubKey(hiveName))
                        if (test != null) return true;
                }
                catch { }

                int r = RegLoadKey(HKEY_LOCAL_MACHINE, hiveName, file);
                if (r == 0) return true;

                string output;
                int ec = RunReg($"load HKLM\\{hiveName} \"{file}\"", out output);
                if (ec == 0) return true;

                errors.Add($"{hiveName}: {output.Trim()}");
                return false;
            }
            catch (Exception ex)
            {
                errors.Add($"{hiveName}: {ex.Message}");
                return false;
            }
        }

        private static int RunReg(string args, out string output)
        {
            output = "";
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(si))
                {
                    if (p == null) return -1;
                    output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return -2; }
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { output = ex.Message; return -3; }
        }

        public static void UnloadOfflineHives()
        {
            lock (_lock)
            {
                if (!IsWinReEnvironment()) return;
                UnloadHive(HIVE_SOFTWARE);
                UnloadHive(HIVE_SYSTEM);
                UnloadHive(HIVE_SAM);
                UnloadHive(HIVE_SECURITY);
                UnloadHkcu();
                _hivesLoaded = false;
            }
        }

        private static void UnloadHive(string name)
        {
            try { RegUnLoadKey(HKEY_LOCAL_MACHINE, name); } catch { }
            try { string _; RunReg($"unload HKLM\\{name}", out _); } catch { }
        }

        public static bool EnsureOfflineHives() { return LoadOfflineHives(); }
        public static bool EnsureOfflineHives(out string status) { return LoadOfflineHives(out status); }

        // ============================================================
        // HKCU
        // ============================================================
        public static bool LoadHkcu()
        {
            if (!IsWinReEnvironment()) return true;
            if (_hkcuLoaded) return true;

            try
            {
                using (var k = Registry.Users.OpenSubKey(HIVE_HKCU))
                    if (k != null) { _hkcuLoaded = true; return true; }

                string drive = GetSystemDrive();
                if (!drive.EndsWith(":")) drive += ":";
                string usersDir = Path.Combine(drive, "Users");
                if (!Directory.Exists(usersDir)) return false;

                string bestUser = null;
                DateTime bestTime = DateTime.MinValue;
                foreach (var dir in Directory.GetDirectories(usersDir))
                {
                    string name = Path.GetFileName(dir);
                    if (name.Equals("Public", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("Default", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("All Users", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.StartsWith(".")) continue;

                    string ntuser = Path.Combine(dir, "NTUSER.DAT");
                    if (!File.Exists(ntuser)) continue;
                    DateTime t = File.GetLastWriteTime(ntuser);
                    if (t > bestTime) { bestTime = t; bestUser = dir; }
                }

                if (bestUser == null) return false;

                string path = Path.Combine(bestUser, "NTUSER.DAT");

                int r = RegLoadKey(HKEY_USERS, HIVE_HKCU, path);
                if (r == 0) { _hkcuLoaded = true; return true; }

                string output;
                int ec = RunReg($"load HKU\\{HIVE_HKCU} \"{path}\"", out output);
                if (ec == 0) { _hkcuLoaded = true; return true; }

                return false;
            }
            catch (Exception ex) { BbLog.Error("[LoadHkcu]", ex); return false; }
        }

        public static void UnloadHkcu()
        {
            if (!_hkcuLoaded) return;
            try { RegUnLoadKey(HKEY_USERS, HIVE_HKCU); } catch { }
            try { string _; RunReg($"unload HKU\\{HIVE_HKCU}", out _); } catch { }
            _hkcuLoaded = false;
        }

        // ============================================================
        // OPEN SUBKEY
        // ============================================================
        private static RegistryKey OpenHiveSubKey(string hive, string path, bool writable = false)
        {
            try
            {
                string full = hive + "\\" + path.TrimStart('\\');
                return Registry.LocalMachine.OpenSubKey(full, writable);
            }
            catch { return null; }
        }

        private static RegistryKey OpenSoftwareSubKey(string path, bool writable = false)
        {
            if (!IsWinReEnvironment())
                return Registry.LocalMachine.OpenSubKey(path, writable);

            LoadOfflineHives();
            return OpenHiveSubKey(HIVE_SOFTWARE, path, writable);
        }

        private static RegistryKey OpenSystemSubKey(string path, bool writable = false)
        {
            if (!IsWinReEnvironment())
                return Registry.LocalMachine.OpenSubKey(path, writable);

            LoadOfflineHives();
            return OpenHiveSubKey(HIVE_SYSTEM, path, writable);
        }

        private static RegistryKey OpenSamSubKey(string path, bool writable = false)
        {
            if (!IsWinReEnvironment())
                return Registry.LocalMachine.OpenSubKey(path, writable);

            LoadOfflineHives();
            return OpenHiveSubKey(HIVE_SAM, path, writable);
        }

        private static RegistryKey OpenUserSubKey(string path, bool writable = false)
        {
            if (!IsWinReEnvironment())
                return Registry.CurrentUser.OpenSubKey(path, writable);

            if (!LoadHkcu()) return null;
            try { return Registry.Users.OpenSubKey(HIVE_HKCU + "\\" + path.TrimStart('\\'), writable); }
            catch { return null; }
        }

        // ============================================================
        // NORMALIZE
        // ============================================================
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            string drive = GetSystemDrive();
            if (!drive.EndsWith(":")) drive += ":";

            if (path.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase))
                return drive + "\\" + path.Substring(3);

            if (IsWinReEnvironment() && path.Length > 2 && path[1] == ':' && char.IsLetter(path[0]))
            {
                if (!path.StartsWith(drive, StringComparison.OrdinalIgnoreCase))
                    return drive + path.Substring(2);
            }
            return path;
        }

        public static bool IsFirstRunWinRE()
        {
            try
            {
                using (var k = OpenSoftwareSubKey(@"BunnyBlack", true))
                {
                    if (k == null) return true;
                    if (k.GetValue("FirstRun") == null) { k.SetValue("FirstRun", "done"); return true; }
                    return false;
                }
            }
            catch { return false; }
        }

        // ============================================================
        // ПОЛЬЗОВАТЕЛИ — WMI → SAM → папки
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

            // ============================================================
            // 1) WMI Win32_UserAccount — живая система
            // ============================================================
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Name, SID, Disabled, LocalAccount FROM Win32_UserAccount"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            string name = obj["Name"]?.ToString() ?? "";
                            if (string.IsNullOrEmpty(name)) continue;
                            if (seen.Contains(name)) continue;

                            bool local = false;
                            try { local = Convert.ToBoolean(obj["LocalAccount"] ?? false); }
                            catch { }
                            if (!local) continue;

                            string sid = obj["SID"]?.ToString() ?? "";
                            bool disabled = false;
                            try { disabled = Convert.ToBoolean(obj["Disabled"] ?? false); }
                            catch { }

                            int rid = 0;
                            int lastDash = sid.LastIndexOf('-');
                            if (lastDash > 0) int.TryParse(sid.Substring(lastDash + 1), out rid);

                            result.Add(new UserEntry
                            {
                                Name = name,
                                SID = sid,
                                RID = rid,
                                Disabled = disabled,
                                IsAdmin = (rid == 500),
                                ProfilePath = GetUserProfilePath(name),
                                Source = "WMI"
                            });
                            seen.Add(name);
                        }
                        catch (Exception ex) { BbLog.Error("[GetUserEntries/WMI-item]", ex); }
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[GetUserEntries/WMI]", ex); }

            // ============================================================
            // 2) SAM — fallback (WinRE)
            // ============================================================
            if (result.Count == 0)
            {
                try
                {
                    using (var namesKey = OpenSamSubKey(@"SAM\Domains\Account\Users\Names"))
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
                                        using (var fk = OpenSamSubKey(@"SAM\Domains\Account\Users\" + hexRid))
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
                catch (Exception ex) { BbLog.Error("[GetUserEntries/SAM]", ex); }
            }

            // ============================================================
            // 3) Folder fallback
            // ============================================================
            if (result.Count == 0)
            {
                try
                {
                    string drive = GetSystemDrive();
                    if (!drive.EndsWith(":")) drive += ":";
                    string usersDir = Path.Combine(drive, "Users");

                    if (Directory.Exists(usersDir))
                    {
                        foreach (var dir in Directory.GetDirectories(usersDir))
                        {
                            string name = Path.GetFileName(dir);
                            if (seen.Contains(name)) continue;

                            result.Add(new UserEntry
                            {
                                Name = name,
                                SID = "",
                                RID = 0,
                                Disabled = false,
                                IsAdmin = false,
                                ProfilePath = dir,
                                Source = "Folder"
                            });
                            seen.Add(name);
                        }
                    }
                }
                catch (Exception ex) { BbLog.Error("[GetUserEntries/Folder]", ex); }
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            string src = result.Count > 0 ? result[0].Source : "нет";
            BbLog.Info($"[GetUserEntries] итого {result.Count} (источник: {src})");
            return result;
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
                using (var key = OpenSoftwareSubKey(@"Microsoft\Windows NT\CurrentVersion\ProfileList"))
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
                                        return NormalizePath(profilePath);
                                }
                            }
                        }
                    }
                }
                return "";
            }
            catch { return ""; }
        }

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
        // NetAPI CRUD
        // ============================================================
        public static bool CreateUserNetApi(string username, string password, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                errorMessage = "Пустое имя/пароль.";
                return false;
            }
            try
            {
                USER_INFO_1 ui = new USER_INFO_1
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
                int pe;
                int r = NetUserAdd(null, 1, ref ui, out pe);
                if (r != 0) { errorMessage = $"NetUserAdd код {r}"; return false; }

                USER_INFO_1003 pw = new USER_INFO_1003 { usri1003_password = password };
                int pe2;
                int r2 = NetUserSetInfo(null, username, 1003, ref pw, out pe2);
                if (r2 != 0) { errorMessage = $"Пароль не установлен, код {r2}"; return false; }
                return true;
            }
            catch (Exception ex) { errorMessage = ex.Message; return false; }
        }

        public static bool CreateUserNetApi(string username, string password)
            => CreateUserNetApi(username, password, out _);

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

                USER_INFO_1008 upd = new USER_INFO_1008 { usri1008_flags = info.usri1008_flags };
                int pe;
                return NetUserSetInfo(null, username, 1008, ref upd, out pe) == 0;
            }
            catch { return false; }
        }

        // ============================================================
        // RUN / RUNONCE
        // ============================================================
        public static List<AutostartItem> GetRunItems()
        {
            var items = new List<AutostartItem>();

            void Add(RegistryKey key, string type)
            {
                if (key == null) return;
                try
                {
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
                catch { }
            }

            if (IsWinReEnvironment())
            {
                LoadOfflineHives();
                Add(OpenHiveSubKey(HIVE_SOFTWARE, @"Microsoft\Windows\CurrentVersion\Run"), "HKLM\\Run");
                Add(OpenHiveSubKey(HIVE_SOFTWARE, @"Wow6432Node\Microsoft\Windows\CurrentVersion\Run"), "HKLM\\Run (32)");
                Add(OpenUserSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"), "HKCU\\Run");
                return items;
            }

            Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "HKLM\\Run (64bit)");
            Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "HKLM\\Run (32bit)");
            Add(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "HKCU\\Run");

            return items;
        }

        public static List<AutostartItem> GetRunOnceItems()
        {
            var items = new List<AutostartItem>();

            void Add(RegistryKey key, string type)
            {
                if (key == null) return;
                try
                {
                    foreach (var name in key.GetValueNames())
                    {
                        var value = key.GetValue(name)?.ToString() ?? "";
                        bool dup = false;
                        foreach (var it in items) if (it.Name == name && it.Path == value && it.Type == type) { dup = true; break; }
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
                catch { }
            }

            if (IsWinReEnvironment())
            {
                LoadOfflineHives();
                Add(OpenHiveSubKey(HIVE_SOFTWARE, @"Microsoft\Windows\CurrentVersion\RunOnce"), "HKLM\\RunOnce");
                Add(OpenUserSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce"), "HKCU\\RunOnce");
                return items;
            }

            Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), "HKLM\\RunOnce (64bit)");
            Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), "HKLM\\RunOnce (32bit)");
            Add(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), "HKCU\\RunOnce");

            return items;
        }

        public static List<AutostartItem> GetWinlogonItems()
        {
            var items = new List<AutostartItem>();
            string[] params_ = { "Shell", "Userinit", "Taskman", "System" };

            try
            {
                using (var key = OpenSoftwareSubKey(@"Microsoft\Windows NT\CurrentVersion\Winlogon"))
                {
                    if (key != null)
                    {
                        foreach (var param in params_)
                        {
                            var value = key.GetValue(param)?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(value))
                                items.Add(new AutostartItem
                                {
                                    Name = param,
                                    Path = NormalizePath(value),
                                    Type = "Winlogon",
                                    Exists = true
                                });
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
                string drive = GetSystemDrive();
                if (!drive.EndsWith(":")) drive += ":";
                string[] folders;

                if (IsWinReEnvironment())
                {
                    string usersPath = Path.Combine(drive, "Users");
                    var startupPaths = new List<string>();
                    if (Directory.Exists(usersPath))
                    {
                        foreach (string userDir in Directory.GetDirectories(usersPath))
                        {
                            string sp = Path.Combine(userDir, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
                            if (Directory.Exists(sp)) startupPaths.Add(sp);
                        }
                    }
                    string commonStartup = Path.Combine(drive, @"ProgramData\Microsoft\Windows\Start Menu\Programs\Startup");
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
                            items.Add(new AutostartItem
                            {
                                Name = name,
                                Path = file,
                                Type = "Папка автозагрузки",
                                Exists = true
                            });
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
                bool hkcu = type.Contains("HKCU");
                bool runonce = type.Contains("RunOnce");
                bool is32 = type.Contains("(32)");

                RegistryKey key = null;
                if (hkcu)
                    key = OpenUserSubKey(@"Software\Microsoft\Windows\CurrentVersion\" + (runonce ? "RunOnce" : "Run"), true);
                else if (is32)
                    key = OpenSoftwareSubKey(@"Wow6432Node\Microsoft\Windows\CurrentVersion\" + (runonce ? "RunOnce" : "Run"), true);
                else
                    key = OpenSoftwareSubKey(@"Microsoft\Windows\CurrentVersion\" + (runonce ? "RunOnce" : "Run"), true);

                using (key)
                {
                    if (key != null && key.GetValue(name) != null) { key.DeleteValue(name); return true; }
                }
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
            catch (UnauthorizedAccessException ex) { error = "Отказано: " + ex.Message; return false; }
            catch (IOException ex) { error = "IO: " + ex.Message; return false; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool DeleteFileItem(string path) => DeleteFileItem(path, out _);

        public static bool SetWinlogonValue(string name, string value)
        {
            try
            {
                using (var key = OpenSoftwareSubKey(@"Microsoft\Windows NT\CurrentVersion\Winlogon", true))
                    if (key != null) { key.SetValue(name, NormalizePath(value)); return true; }
                return false;
            }
            catch { return false; }
        }

        public static List<AppInitItem> GetAppInitDllsItems()
        {
            var list = new List<AppInitItem>();
            try
            {
                using (var key = OpenSoftwareSubKey(@"Microsoft\Windows NT\CurrentVersion\Windows"))
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
                using (var key = OpenSoftwareSubKey(@"Microsoft\Windows NT\CurrentVersion\Windows", true))
                    if (key != null) { key.SetValue(name, value); return true; }
                return false;
            }
            catch { return false; }
        }

        public static bool DeleteAppInitDllsValue(string name)
        {
            try
            {
                using (var key = OpenSoftwareSubKey(@"Microsoft\Windows NT\CurrentVersion\Windows", true))
                    if (key != null && key.GetValue(name) != null)
                    {
                        key.SetValue(name, name == "AppInit_DLLs" ? "" : (object)0);
                        return true;
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
                using (var setupKey = OpenSystemSubKey(@"Setup"))
                {
                    if (setupKey != null)
                    {
                        string cmdLine = setupKey.GetValue("CmdLine")?.ToString() ?? "";
                        list.Add(new CmdLineItem { Name = "CmdLine", Path = string.IsNullOrEmpty(cmdLine) ? "(пусто)" : cmdLine });

                        string setupType = setupKey.GetValue("SetupType")?.ToString() ?? "";
                        list.Add(new CmdLineItem { Name = "SetupType", Path = string.IsNullOrEmpty(setupType) ? "(пусто)" : setupType });
                    }
                }

                using (var polKey = OpenSoftwareSubKey(@"Microsoft\Windows\CurrentVersion\Policies\System"))
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
                    using (var key = OpenSystemSubKey(@"Setup", true))
                        if (key != null) { key.SetValue(name, value); return true; }
                    return false;
                }
                if (name == "EnableCursorSuppression")
                {
                    using (var key = OpenSoftwareSubKey(@"Microsoft\Windows\CurrentVersion\Policies\System", true))
                        if (key != null) { key.SetValue(name, value); return true; }
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
                    using (var key = OpenSystemSubKey(@"Setup", true))
                        if (key != null && key.GetValue(name) != null) { key.SetValue(name, ""); return true; }
                    return false;
                }
                if (name == "EnableCursorSuppression")
                {
                    using (var key = OpenSoftwareSubKey(@"Microsoft\Windows\CurrentVersion\Policies\System", true))
                        if (key != null && key.GetValue(name) != null) { key.SetValue(name, ""); return true; }
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
            catch { try { return GetOfflineTaskItems(); } catch { } }
            return list;
        }

        private static List<TaskItem> GetOfflineTaskItems()
        {
            var tasks = new List<TaskItem>();
            string drive = GetSystemDrive();
            if (!drive.EndsWith(":")) drive += ":";
            string tasksPath = Path.Combine(drive, @"Windows\System32\Tasks");
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
                    string drive = GetSystemDrive();
                    if (!drive.EndsWith(":")) drive += ":";
                    string tasksDir = Path.Combine(drive, @"Windows\System32\Tasks");

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