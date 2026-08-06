using System;
using System.Collections.Generic;
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

        private static bool? _isWinRe = null;
        private static bool? _hivesLoaded = null;
        private static string _systemDrive = null;

        // ============================================================
        // P/INVOKE для работы с реестром напрямую
        // ============================================================
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegLoadKey(IntPtr hKey, string lpSubKey, string lpFile);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegUnLoadKey(IntPtr hKey, string lpSubKey);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        private const int KEY_READ = 0x20019;
        private const int KEY_WRITE = 0x20006;
        private const uint HKEY_LOCAL_MACHINE = 0x80000002;

        // ============================================================
        // P/INVOKE для работы с пользователями (NetAPI)
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
        private struct USER_INFO_1003
        {
            public string usri1003_password;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct USER_INFO_1008
        {
            public int usri1008_flags;
        }

        private const int UF_ACCOUNTDISABLE = 0x00000001;
        private const int UF_PASSWD_NOTREQD = 0x00000020;
        private const int UF_DONT_EXPIRE_PASSWD = 0x00010000;

        // ============================================================
        // ПРОВЕРКА WINRE
        // ============================================================
        public static bool IsWinReEnvironment()
        {
            if (_isWinRe.HasValue) return _isWinRe.Value;

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT"))
                {
                    if (key != null)
                    {
                        _isWinRe = true;
                        return true;
                    }
                }

                string windir = Environment.GetEnvironmentVariable("windir") ?? "";
                if (windir.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase))
                {
                    _isWinRe = true;
                    return true;
                }

                string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "";
                if (systemRoot.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase))
                {
                    _isWinRe = true;
                    return true;
                }

                _isWinRe = false;
                return false;
            }
            catch
            {
                _isWinRe = false;
                return false;
            }
        }

        // ============================================================
        // ЗАГРУЗКА ОФФЛАЙН-КУСТОВ (через P/Invoke - быстрее)
        // ============================================================
        private static bool LoadOfflineHives()
        {
            if (_hivesLoaded.HasValue && _hivesLoaded.Value) return true;
            if (!IsWinReEnvironment()) return true;

            try
            {
                string systemDrive = GetSystemDrive();

                LoadOfflineHivePInvoke(OfflineSoftwareHive, Path.Combine(systemDrive, @"Windows\System32\Config\SOFTWARE"));
                LoadOfflineHivePInvoke(OfflineSystemHive, Path.Combine(systemDrive, @"Windows\System32\Config\SYSTEM"));
                LoadOfflineHivePInvoke(OfflineSamHive, Path.Combine(systemDrive, @"Windows\System32\Config\SAM"));
                LoadOfflineHivePInvoke(OfflineSecurityHive, Path.Combine(systemDrive, @"Windows\System32\Config\SECURITY"));

                _hivesLoaded = true;
                return true;
            }
            catch
            {
                _hivesLoaded = false;
                return false;
            }
        }

        private static void LoadOfflineHivePInvoke(string hiveName, string sourceFile)
        {
            try
            {
                if (!File.Exists(sourceFile)) return;
                int result = RegLoadKey((IntPtr)HKEY_LOCAL_MACHINE, hiveName, sourceFile);
                if (result != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"RegLoadKey failed: {result} for {sourceFile}");
                }
            }
            catch { }
        }

        public static void UnloadOfflineHives()
        {
            try
            {
                RegUnLoadKey((IntPtr)HKEY_LOCAL_MACHINE, OfflineSoftwareHive);
                RegUnLoadKey((IntPtr)HKEY_LOCAL_MACHINE, OfflineSystemHive);
                RegUnLoadKey((IntPtr)HKEY_LOCAL_MACHINE, OfflineSamHive);
                RegUnLoadKey((IntPtr)HKEY_LOCAL_MACHINE, OfflineSecurityHive);
                _hivesLoaded = false;
            }
            catch { }
        }

        // ============================================================
        // ОПРЕДЕЛЕНИЕ СИСТЕМНОГО ДИСКА
        // ============================================================
        public static string GetSystemDrive()
        {
            if (!string.IsNullOrEmpty(_systemDrive)) return _systemDrive;

            try
            {
                string[] drives = { "C:", "D:", "E:", "F:", "G:", "H:" };
                foreach (string drive in drives)
                {
                    string windowsPath = drive + @"\Windows\System32\Config\SOFTWARE";
                    if (File.Exists(windowsPath))
                    {
                        _systemDrive = drive;
                        return drive;
                    }
                }

                foreach (string drive in drives)
                {
                    string usersPath = drive + @"\Users";
                    if (Directory.Exists(usersPath))
                    {
                        _systemDrive = drive;
                        return drive;
                    }
                }

                _systemDrive = "C:";
                return "C:";
            }
            catch
            {
                _systemDrive = "C:";
                return "C:";
            }
        }

        // ============================================================
        // ОТКРЫТИЕ КЛЮЧА РЕЕСТРА (С ПОДДЕРЖКОЙ WINRE)
        // ============================================================
        private static RegistryKey OpenWindowsSubKey(string path, bool writable = false)
        {
            if (!IsWinReEnvironment())
            {
                return Registry.LocalMachine.OpenSubKey(path, writable);
            }

            LoadOfflineHives();

            string fullPath = path;

            if (path.StartsWith("SYSTEM\\", StringComparison.OrdinalIgnoreCase))
                fullPath = OfflineSystemHive + "\\" + path.Substring(7);
            else if (path.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase))
                fullPath = OfflineSoftwareHive + "\\" + path.Substring(9);
            else if (path.StartsWith("SAM\\", StringComparison.OrdinalIgnoreCase))
                fullPath = OfflineSamHive + "\\" + path.Substring(4);
            else if (path.StartsWith("SECURITY\\", StringComparison.OrdinalIgnoreCase))
                fullPath = OfflineSecurityHive + "\\" + path.Substring(9);

            return Registry.LocalMachine.OpenSubKey(fullPath, writable);
        }

        // ============================================================
        // НОРМАЛИЗАЦИЯ ПУТИ
        // ============================================================
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            if (path.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase))
                return GetSystemDrive() + "\\" + path.Substring(3);

            if (IsWinReEnvironment())
            {
                string systemDrive = GetSystemDrive();
                if (path.Length > 2 && path[1] == ':' && char.IsLetter(path[0]))
                {
                    if (!path.StartsWith(systemDrive, StringComparison.OrdinalIgnoreCase))
                    {
                        return systemDrive + path.Substring(2);
                    }
                }
            }

            return path;
        }

        // ============================================================
        // ПОЛУЧЕНИЕ ПОЛЬЗОВАТЕЛЕЙ
        // ============================================================
        public static List<UserInfo> GetUsers()
        {
            var users = new List<UserInfo>();

            try
            {
                using (var key = OpenWindowsSubKey(@"SAM\SAM\Domains\Account\Users\Names"))
                {
                    if (key != null)
                    {
                        foreach (string userName in key.GetSubKeyNames())
                        {
                            try
                            {
                                var userInfo = new UserInfo
                                {
                                    Name = userName,
                                    SID = GetUserSID(userName),
                                    ProfilePath = GetUserProfilePath(userName),
                                    IsAdmin = IsUserAdmin(userName),
                                    IsActive = true
                                };
                                users.Add(userInfo);
                            }
                            catch { }
                        }
                    }
                }

                if (users.Count == 0)
                {
                    string systemDrive = GetSystemDrive();
                    string usersPath = Path.Combine(systemDrive, @"Users");

                    if (Directory.Exists(usersPath))
                    {
                        foreach (string dir in Directory.GetDirectories(usersPath))
                        {
                            string name = Path.GetFileName(dir);
                            if (name != "Public" && name != "Default" && name != "All Users" && !name.StartsWith("."))
                            {
                                users.Add(new UserInfo
                                {
                                    Name = name,
                                    SID = "",
                                    ProfilePath = dir,
                                    IsAdmin = false,
                                    IsActive = true
                                });
                            }
                        }
                    }
                }
            }
            catch { }

            return users;
        }

        private static string GetUserSID(string userName)
        {
            try
            {
                using (var key = OpenWindowsSubKey($@"SAM\SAM\Domains\Account\Users\Names\{userName}"))
                {
                    if (key != null)
                    {
                        var sidBytes = key.GetValue("") as byte[];
                        if (sidBytes != null)
                            return Convert.ToBase64String(sidBytes);
                    }
                }
                return "";
            }
            catch { return ""; }
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

        private static bool IsUserAdmin(string userName)
        {
            try
            {
                using (var key = OpenWindowsSubKey(@"SAM\SAM\Domains\Account\Groups\Builtin\Administrators"))
                {
                    if (key != null)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        // ============================================================
        // РАБОТА С ПОЛЬЗОВАТЕЛЯМИ ЧЕРЕЗ NETAPI
        // ============================================================
        public static bool CreateUserNetApi(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return false;

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
                    usri1_flags = UF_DONT_EXPIRE_PASSWD,
                    usri1_script_path = null
                };

                int parm_err;
                int result = NetUserAdd(null, 1, ref userInfo, out parm_err);
                return result == 0;
            }
            catch { return false; }
        }

        public static bool DeleteUserNetApi(string username)
        {
            if (string.IsNullOrEmpty(username)) return false;
            try
            {
                int result = NetUserDel(null, username);
                return result == 0;
            }
            catch { return false; }
        }

        public static bool SetUserPasswordNetApi(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return false;
            try
            {
                USER_INFO_1003 userInfo = new USER_INFO_1003
                {
                    usri1003_password = password
                };
                int parm_err;
                int result = NetUserSetInfo(null, username, 1003, ref userInfo, out parm_err);
                return result == 0;
            }
            catch { return false; }
        }

        public static bool EnableUserNetApi(string username)
        {
            return SetUserActiveStatus(username, true);
        }

        public static bool DisableUserNetApi(string username)
        {
            return SetUserActiveStatus(username, false);
        }

        private static bool SetUserActiveStatus(string username, bool enable)
        {
            if (string.IsNullOrEmpty(username)) return false;
            try
            {
                IntPtr bufPtr;
                int result = NetUserGetInfo(null, username, 1008, out bufPtr);
                if (result != 0) return false;

                var userInfo1008 = (USER_INFO_1008)Marshal.PtrToStructure(bufPtr, typeof(USER_INFO_1008));
                NetApiBufferFree(bufPtr);

                if (enable)
                    userInfo1008.usri1008_flags &= ~UF_ACCOUNTDISABLE;
                else
                    userInfo1008.usri1008_flags |= UF_ACCOUNTDISABLE;

                USER_INFO_1008 updatedInfo = new USER_INFO_1008
                {
                    usri1008_flags = userInfo1008.usri1008_flags
                };

                int parm_err;
                result = NetUserSetInfo(null, username, 1008, ref updatedInfo, out parm_err);
                return result == 0;
            }
            catch { return false; }
        }

        // ============================================================
        // МЕТОДЫ АВТОЗАГРУЗКИ - ВСЕГДА ЧИТАЕМ ВСЕ РАЗРЯДНОСТИ
        // ============================================================

        public static List<AutostartItem> GetRunItems()
        {
            var items = new List<AutostartItem>();

            if (IsWinReEnvironment())
            {
                try
                {
                    using (var key = OpenWindowsSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        if (key != null)
                        {
                            foreach (var name in key.GetValueNames())
                            {
                                var value = key.GetValue(name)?.ToString() ?? "";
                                items.Add(new AutostartItem
                                {
                                    Name = name,
                                    Path = value,
                                    Type = "HKLM\\Run (WinRE)",
                                    Exists = File.Exists(NormalizePath(value?.Split(' ')[0]?.Trim('"') ?? ""))
                                });
                            }
                        }
                    }
                }
                catch { }
                return items;
            }

            void AddRunItems(RegistryKey key, string type)
            {
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name)?.ToString() ?? "";
                    bool duplicate = false;
                    foreach (var item in items)
                    {
                        if (item.Name == name && item.Path == value && item.Type == type)
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate)
                    {
                        items.Add(new AutostartItem
                        {
                            Name = name,
                            Path = value,
                            Type = type,
                            Exists = File.Exists(NormalizePath(value?.Split(' ')[0]?.Trim('"') ?? ""))
                        });
                    }
                }
            }

            try
            {
                AddRunItems(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "HKLM\\Run (64bit)");
            }
            catch { }

            try
            {
                AddRunItems(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "HKLM\\Run (32bit)");
            }
            catch { }

            try
            {
                AddRunItems(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "HKCU\\Run");
            }
            catch { }

            return items;
        }

        public static List<AutostartItem> GetRunOnceItems()
        {
            var items = new List<AutostartItem>();

            if (IsWinReEnvironment())
            {
                try
                {
                    using (var key = OpenWindowsSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"))
                    {
                        if (key != null)
                        {
                            foreach (var name in key.GetValueNames())
                            {
                                var value = key.GetValue(name)?.ToString() ?? "";
                                items.Add(new AutostartItem
                                {
                                    Name = name,
                                    Path = value,
                                    Type = "HKLM\\RunOnce (WinRE)",
                                    Exists = File.Exists(NormalizePath(value?.Split(' ')[0]?.Trim('"') ?? ""))
                                });
                            }
                        }
                    }
                }
                catch { }
                return items;
            }

            void AddRunOnceItems(RegistryKey key, string type)
            {
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name)?.ToString() ?? "";
                    bool duplicate = false;
                    foreach (var item in items)
                    {
                        if (item.Name == name && item.Path == value && item.Type == type)
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate)
                    {
                        items.Add(new AutostartItem
                        {
                            Name = name,
                            Path = value,
                            Type = type,
                            Exists = File.Exists(NormalizePath(value?.Split(' ')[0]?.Trim('"') ?? ""))
                        });
                    }
                }
            }

            try
            {
                AddRunOnceItems(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), "HKLM\\RunOnce (64bit)");
            }
            catch { }

            try
            {
                AddRunOnceItems(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), "HKLM\\RunOnce (32bit)");
            }
            catch { }

            try
            {
                AddRunOnceItems(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), "HKCU\\RunOnce");
            }
            catch { }

            return items;
        }

        public static List<AutostartItem> GetWinlogonItems()
        {
            var items = new List<AutostartItem>();
            string[] params_ = { "Shell", "Userinit", "Taskman", "System" };

            try
            {
                using (var key = OpenWindowsSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"))
                {
                    if (key != null)
                    {
                        foreach (var param in params_)
                        {
                            var value = key.GetValue(param)?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(value))
                            {
                                items.Add(new AutostartItem
                                {
                                    Name = param,
                                    Path = NormalizePath(value),
                                    Type = "Winlogon (HKLM)",
                                    Exists = true
                                });
                            }
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
                            string startupPath = Path.Combine(userDir, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
                            if (Directory.Exists(startupPath))
                                startupPaths.Add(startupPath);
                        }
                    }
                    string commonStartup = Path.Combine(systemDrive, @"ProgramData\Microsoft\Windows\Start Menu\Programs\Startup");
                    if (Directory.Exists(commonStartup))
                        startupPaths.Add(commonStartup);

                    folders = startupPaths.ToArray();
                }
                else
                {
                    string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                    string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    folders = new string[] { commonStartup, userStartup };
                }

                foreach (var folder in folders)
                {
                    if (Directory.Exists(folder))
                    {
                        foreach (var file in Directory.GetFiles(folder))
                        {
                            var name = Path.GetFileName(file);
                            if (!name.StartsWith("~") && !name.StartsWith("."))
                            {
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
                }
            }
            catch { }

            return items;
        }

        // ============================================================
        // ============== ИСПРАВЛЕННОЕ И ДОБАВЛЕННОЕ УДАЛЕНИЕ ==============
        // ============================================================

        public static bool DeleteRunItem(string name, string type)
        {
            try
            {
                // Если это WinRE, удаляем через оффлайн-куст
                if (IsWinReEnvironment())
                {
                    string path = type.Contains("RunOnce") ?
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" :
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

                    using (var key = OpenWindowsSubKey(path, true))
                    {
                        if (key != null && key.GetValue(name) != null)
                        {
                            key.DeleteValue(name);
                            return true;
                        }
                    }
                    return false;
                }

                // Обычная Windows: перебираем все возможные места, где может быть запись
                // 1. HKLM 64-bit
                using (var key64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\" + (type.Contains("RunOnce") ? "RunOnce" : "Run"), true))
                {
                    if (key64 != null && key64.GetValue(name) != null)
                    {
                        key64.DeleteValue(name);
                        return true;
                    }
                }

                // 2. HKLM 32-bit (WOW6432Node)
                using (var key32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\" + (type.Contains("RunOnce") ? "RunOnce" : "Run"), true))
                {
                    if (key32 != null && key32.GetValue(name) != null)
                    {
                        key32.DeleteValue(name);
                        return true;
                    }
                }

                // 3. HKCU (Текущий пользователь)
                using (var keyUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\" + (type.Contains("RunOnce") ? "RunOnce" : "Run"), true))
                {
                    if (keyUser != null && keyUser.GetValue(name) != null)
                    {
                        keyUser.DeleteValue(name);
                        return true;
                    }
                }

                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // Если не хватает прав - пробрасываем выше, чтобы программа перезапустилась с админом
                throw;
            }
            catch
            {
                return false;
            }
        }

        public static bool DeleteFileItem(string path)
        {
            try
            {
                string normalizedPath = NormalizePath(path);
                if (File.Exists(normalizedPath))
                {
                    File.Delete(normalizedPath);
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        public static bool SetWinlogonValue(string name, string value)
        {
            try
            {
                using (var key = OpenWindowsSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true))
                {
                    if (key != null)
                    {
                        key.SetValue(name, NormalizePath(value));
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        // ============================================================
        // APPINIT_DLLS
        // ============================================================

        public static List<AppInitItem> GetAppInitDllsItems()
        {
            var list = new List<AppInitItem>();
            try
            {
                using (var key = OpenWindowsSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows"))
                {
                    if (key != null)
                    {
                        string dlls = key.GetValue("AppInit_DLLs")?.ToString() ?? "";
                        string load = key.GetValue("LoadAppInit_DLLs")?.ToString() ?? "0";
                        string require = key.GetValue("RequireSignedAppInit_DLLs")?.ToString() ?? "0";
                        list.Add(new AppInitItem { Name = "AppInit_DLLs", Path = dlls, Type = "HKLM" });
                        list.Add(new AppInitItem { Name = "LoadAppInit_DLLs", Path = load, Type = "HKLM" });
                        list.Add(new AppInitItem { Name = "RequireSignedAppInit_DLLs", Path = require, Type = "HKLM" });
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
                using (var key = OpenWindowsSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", true))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value);
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        public static bool DeleteAppInitDllsValue(string name)
        {
            try
            {
                using (var key = OpenWindowsSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", true))
                {
                    if (key != null && key.GetValue(name) != null)
                    {
                        if (name == "AppInit_DLLs")
                            key.SetValue(name, "");
                        else
                            key.SetValue(name, 0);
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        // ============================================================
        // CMDLINE
        // ============================================================

        public static List<CmdLineItem> GetCmdLineAutoRunItems()
        {
            var list = new List<CmdLineItem>();

            try
            {
                using (var key = OpenWindowsSubKey(@"SYSTEM\Setup"))
                {
                    if (key != null)
                    {
                        string cmdLine = key.GetValue("CmdLine")?.ToString() ?? "";
                        list.Add(new CmdLineItem
                        {
                            Name = "CmdLine",
                            Path = string.IsNullOrEmpty(cmdLine) ? "(пусто)" : cmdLine
                        });

                        string setupType = key.GetValue("SetupType")?.ToString() ?? "";
                        list.Add(new CmdLineItem
                        {
                            Name = "SetupType",
                            Path = string.IsNullOrEmpty(setupType) ? "(пусто)" : setupType
                        });
                    }
                }

                using (var key = OpenWindowsSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"))
                {
                    if (key != null)
                    {
                        string cursorSuppression = key.GetValue("EnableCursorSuppression")?.ToString() ?? "";
                        list.Add(new CmdLineItem
                        {
                            Name = "EnableCursorSuppression",
                            Path = string.IsNullOrEmpty(cursorSuppression) ? "(пусто)" : cursorSuppression
                        });
                    }
                }
            }
            catch { }

            return list;
        }

        private static string GetRegistryPathForParameter(string name)
        {
            if (name == "CmdLine" || name == "SetupType")
                return @"SYSTEM\Setup";
            else if (name == "EnableCursorSuppression")
                return @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
            else
                return @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows";
        }

        public static bool SetCmdLineValue(string name, string value)
        {
            try
            {
                string registryPath = GetRegistryPathForParameter(name);
                using (var key = OpenWindowsSubKey(registryPath, true))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value);
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        public static bool DeleteCmdLineAutoRun(string name)
        {
            try
            {
                string registryPath = GetRegistryPathForParameter(name);
                using (var key = OpenWindowsSubKey(registryPath, true))
                {
                    if (key != null && key.GetValue(name) != null)
                    {
                        key.SetValue(name, "");
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        // ============================================================
        // ПЛАНИРОВЩИК ЗАДАЧ
        // ============================================================

        public static List<TaskItem> GetTaskSchedulerItems()
        {
            var list = new List<TaskItem>();

            if (IsWinReEnvironment())
            {
                return GetOfflineTaskItems();
            }

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
                        foreach (var t in task.Definition.Triggers)
                        {
                            triggers += t.ToString() + "; ";
                        }
                        if (triggers.Length > 2)
                            triggers = triggers.Substring(0, triggers.Length - 2);

                        list.Add(new TaskItem
                        {
                            TaskName = task.Name,
                            Action = actions,
                            State = task.State.ToString(),
                            Triggers = triggers
                        });
                    }
                }
            }
            catch { }

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
                foreach (string file in Directory.GetFiles(tasksPath, "*.xml", SearchOption.AllDirectories))
                {
                    try
                    {
                        string taskName = Path.GetFileNameWithoutExtension(file);
                        string action = "Неизвестно";
                        string triggers = "Неизвестно";

                        string xmlContent = File.ReadAllText(file);
                        XmlDocument doc = new XmlDocument();
                        doc.LoadXml(xmlContent);

                        XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
                        ns.AddNamespace("ns", "http://schemas.microsoft.com/windows/2004/02/mit/task");

                        XmlNode execNode = doc.SelectSingleNode("//ns:Exec", ns);
                        if (execNode != null)
                        {
                            string command = execNode.SelectSingleNode("ns:Command", ns)?.InnerText ?? "";
                            string args = execNode.SelectSingleNode("ns:Arguments", ns)?.InnerText ?? "";
                            action = command + (!string.IsNullOrEmpty(args) ? " " + args : "");
                        }

                        XmlNode triggersNode = doc.SelectSingleNode("//ns:Triggers", ns);
                        if (triggersNode != null)
                        {
                            var triggerList = new List<string>();
                            foreach (XmlNode child in triggersNode.ChildNodes)
                            {
                                if (child.Name.EndsWith("Trigger"))
                                {
                                    string triggerName = child.Name.Replace("ns:", "").Replace("Trigger", "");
                                    triggerList.Add(triggerName);
                                }
                            }
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
                    string taskPath = Path.Combine(systemDrive, @"Windows\System32\Tasks", taskName + ".xml");
                    if (File.Exists(taskPath))
                    {
                        File.Delete(taskPath);
                        return true;
                    }
                    string tasksDir = Path.Combine(systemDrive, @"Windows\System32\Tasks");
                    foreach (string file in Directory.GetFiles(tasksDir, "*.xml", SearchOption.AllDirectories))
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

        // ============================================================
        // БЭКАП РЕЕСТРА
        // ============================================================

        public static bool BackupRegistry()
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), $"registry_backup_{DateTime.Now:yyyyMMdd_HHmmss}.reg");
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "reg",
                    Arguments = $"export HKLM \"{path.Replace(".reg", "_HKLM.reg")}\" /y",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (var process = System.Diagnostics.Process.Start(startInfo)) { if (process != null) process.WaitForExit(); }
                startInfo.Arguments = $"export HKCU \"{path.Replace(".reg", "_HKCU.reg")}\" /y";
                using (var process = System.Diagnostics.Process.Start(startInfo)) { if (process != null) process.WaitForExit(); }
                return true;
            }
            catch { return false; }
        }
    }
}