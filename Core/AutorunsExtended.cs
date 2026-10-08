// language: C#, file: Core/AutorunsExtended.cs
// Полная замена.
// п.4  — добавлены поля: FileExists, Signed, CreatedAt, ModifiedAt, StatusReason.
// п.12 — эвристика свежести (Created < 7 дней).
// п.20 — расширенный Winlogon (Shell, Userinit, Taskman, System, AppSetup, GinaDLL, UIHost, VmApplet).
// п.28 — BootExecute, KnownDLLs, AppCertDlls, LSA Notification Packages.
// IFEO — только Debugger.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace BunnyBlack.Core
{
    public class AutorunEntry
    {
        public string Category { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Hive { get; set; }
        public string KeyPath { get; set; }
        public string ValueName { get; set; }
        public bool FileExists { get; set; }
        public bool Suspicious { get; set; }
        public string SuspicionReason { get; set; }

        // п.4 — новые поля
        public bool Signed { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }

        // п.12 — вычисляется в MarkSuspicious
        public bool FreshFile { get; set; }
        public int AgeDays { get; set; }
    }

    public static class AutorunsExtended
    {
        private static readonly string[] SuspiciousPathParts =
        {
            @"\AppData\Local\Temp\", @"\Windows\Temp\", @"\Temp\",
            @"\Downloads\", @"\Desktop\", @"\ProgramData\",
            @"\Users\Public\", @"\Recycle"
        };

        // ============================================================
        // СВОД
        // ============================================================
        public static List<AutorunEntry> Collect(bool offline = false)
        {
            var list = new List<AutorunEntry>();

            try { CollectRunKeys(list, offline); } catch (Exception e) { BbLog.Error("[CollectRunKeys]", e); }
            try { CollectWinlogonExtended(list, offline); } catch (Exception e) { BbLog.Error("[Winlogon]", e); }
            try { CollectComClsid(list, offline); } catch (Exception e) { BbLog.Error("[ComClsid]", e); }
            try { CollectLsaProviders(list, offline); } catch (Exception e) { BbLog.Error("[Lsa]", e); }
            try { CollectPrintMonitors(list, offline); } catch (Exception e) { BbLog.Error("[Print]", e); }
            try { CollectBootExecute(list, offline); } catch (Exception e) { BbLog.Error("[BootExec]", e); }
            try { CollectKnownDlls(list, offline); } catch (Exception e) { BbLog.Error("[KnownDLLs]", e); }
            try { CollectAppCertDlls(list, offline); } catch (Exception e) { BbLog.Error("[AppCert]", e); }
            try { CollectLsaNotificationPackages(list, offline); } catch (Exception e) { BbLog.Error("[LsaNotify]", e); }
            try { CollectShellServiceObject(list, offline); } catch (Exception e) { BbLog.Error("[ShellService]", e); }
            try { CollectSharedTaskScheduler(list, offline); } catch (Exception e) { BbLog.Error("[SharedTask]", e); }
            try { CollectAppPaths(list, offline); } catch (Exception e) { BbLog.Error("[AppPaths]", e); }
            try { CollectExplorerExtensions(list, offline); } catch (Exception e) { BbLog.Error("[ShellHooks]", e); }
            try { CollectWshHooks(list, offline); } catch (Exception e) { BbLog.Error("[WSH]", e); }
            try { CollectBrowserHelpers(list, offline); } catch (Exception e) { BbLog.Error("[BHO]", e); }
            try { CollectIfeo(list, offline); } catch (Exception e) { BbLog.Error("[IFEO]", e); }

            // эвристика
            foreach (var e in list) MarkSuspicious(e);
            return list;
        }

        // ============================================================
        // RUN / RUNONCE
        // ============================================================
        private static void CollectRunKeys(List<AutorunEntry> list, bool offline)
        {
            string[] subs =
            {
                @"Microsoft\Windows\CurrentVersion\Run",
                @"Microsoft\Windows\CurrentVersion\RunOnce",
                @"Microsoft\Windows\CurrentVersion\RunServices",
                @"Microsoft\Windows\CurrentVersion\RunServicesOnce",
                @"Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
                @"Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
            };

            foreach (var sub in subs)
                ScanHive(list, offline, "SOFTWARE", sub, "Run");
        }

        // ============================================================
        // п.20 — WINLOGON расширенный
        // ============================================================
        private static void CollectWinlogonExtended(List<AutorunEntry> list, bool offline)
        {
            const string sub = @"Microsoft\Windows NT\CurrentVersion\Winlogon";

            string[] values =
            {
                "Shell", "Userinit", "Taskman", "System",
                "AppSetup", "VmApplet", "GinaDLL", "UIHost",
                "AlternateShell", "AutoAdminLogon", "DefaultUserName"
            };

            ScanHive(list, offline, "SOFTWARE", sub, "Winlogon", specificValues: values);

            // Winlogon\Notify
            ScanHive(list, offline, "SOFTWARE",
                @"Microsoft\Windows NT\CurrentVersion\Winlogon\Notify", "WinlogonNotify");
        }

        // ============================================================
        // IFEO — Image File Execution Options. Только Debugger.
        // ============================================================
        private static void CollectIfeo(List<AutorunEntry> list, bool offline)
        {
            const string sub = @"Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

            try
            {
                using (var key = OpenHive(offline, "SOFTWARE", sub))
                {
                    if (key == null) return;

                    foreach (var subName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var subKey = key.OpenSubKey(subName))
                            {
                                if (subKey == null) continue;
                                string debugger = subKey.GetValue("Debugger")?.ToString() ?? "";
                                if (string.IsNullOrWhiteSpace(debugger)) continue;

                                list.Add(MakeEntry(
                                    "IFEO Debugger",
                                    subName + " → Debugger",
                                    debugger,
                                    offline, "SOFTWARE",
                                    sub + "\\" + subName,
                                    "Debugger"));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[CollectIfeo]", ex); }
        }

        // ============================================================
        // п.28 — BOOT EXECUTE
        // ============================================================
        private static void CollectBootExecute(List<AutorunEntry> list, bool offline)
        {
            const string sub = @"ControlSet001\Control\Session Manager";

            try
            {
                using (var key = OpenHive(offline, "SYSTEM", sub))
                {
                    if (key == null) return;

                    object boot = key.GetValue("BootExecute");
                    if (boot is string[] arr)
                    {
                        foreach (var s in arr)
                        {
                            if (string.IsNullOrWhiteSpace(s)) continue;
                            list.Add(MakeEntry("BootExecute", "BootExecute", s, offline, "SYSTEM", sub, "BootExecute"));
                        }
                    }
                    else if (boot is string str && !string.IsNullOrWhiteSpace(str))
                    {
                        list.Add(MakeEntry("BootExecute", "BootExecute", str, offline, "SYSTEM", sub, "BootExecute"));
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[CollectBootExecute]", ex); }
        }

        // ============================================================
        // п.28 — KNOWN DLLS
        // ============================================================
        private static void CollectKnownDlls(List<AutorunEntry> list, bool offline)
        {
            const string sub = @"ControlSet001\Control\Session Manager\KnownDLLs";

            try
            {
                using (var key = OpenHive(offline, "SYSTEM", sub))
                {
                    if (key == null) return;

                    foreach (var name in key.GetValueNames())
                    {
                        string val = key.GetValue(name)?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(val)) continue;

                        bool unusual = val.Contains("\\") || val.Contains("/") || val.Contains(":");

                        var e = MakeEntry("KnownDLL", name, val, offline, "SYSTEM", sub, name);
                        if (unusual)
                        {
                            e.Suspicious = true;
                            e.SuspicionReason = "KnownDLL содержит путь вместо имени — подмена системной DLL";
                        }
                        list.Add(e);
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[CollectKnownDlls]", ex); }
        }

        // ============================================================
        // п.28 — APP CERT DLLS
        // ============================================================
        private static void CollectAppCertDlls(List<AutorunEntry> list, bool offline)
        {
            const string sub = @"ControlSet001\Control\Session Manager\AppCertDlls";

            try
            {
                using (var key = OpenHive(offline, "SYSTEM", sub))
                {
                    if (key == null) return;

                    foreach (var name in key.GetValueNames())
                    {
                        string val = key.GetValue(name)?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(val)) continue;
                        list.Add(MakeEntry("AppCertDll", name, val, offline, "SYSTEM", sub, name));
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[CollectAppCertDlls]", ex); }
        }

        // ============================================================
        // п.28 — LSA NOTIFICATION PACKAGES
        // ============================================================
        private static void CollectLsaNotificationPackages(List<AutorunEntry> list, bool offline)
        {
            const string sub = @"ControlSet001\Control\Lsa";

            try
            {
                using (var key = OpenHive(offline, "SYSTEM", sub))
                {
                    if (key == null) return;

                    string[] pkgs = { "Notification Packages", "Authentication Packages", "Security Packages" };

                    foreach (var pkgName in pkgs)
                    {
                        object val = key.GetValue(pkgName);
                        if (val is string[] arr)
                        {
                            foreach (var s in arr)
                            {
                                if (string.IsNullOrWhiteSpace(s)) continue;
                                list.Add(MakeEntry("LSA Package", pkgName, s, offline, "SYSTEM", sub, pkgName));
                            }
                        }
                        else if (val is string str && !string.IsNullOrWhiteSpace(str))
                        {
                            list.Add(MakeEntry("LSA Package", pkgName, str, offline, "SYSTEM", sub, pkgName));
                        }
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[CollectLsaNotificationPackages]", ex); }
        }

        // ============================================================
        // COM CLSID
        // ============================================================
        private static void CollectComClsid(List<AutorunEntry> list, bool offline)
        {
            string sub = @"Classes\CLSID";
            try
            {
                using (var key = OpenHive(offline, "SOFTWARE", sub))
                {
                    if (key == null) return;

                    foreach (var clsid in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var inproc = key.OpenSubKey(clsid + @"\InprocServer32"))
                            {
                                if (inproc == null) continue;
                                string def = inproc.GetValue("")?.ToString() ?? "";
                                if (string.IsNullOrWhiteSpace(def)) continue;

                                string lower = def.ToLowerInvariant();
                                if (lower.Contains(@"\windows\system32\")) continue;
                                if (lower.Contains(@"\windows\syswow64\")) continue;
                                if (lower.Contains(@"\windows\winsxs\")) continue;

                                string shortClsid = clsid.Length > 38 ? clsid.Substring(0, 38) : clsid;
                                list.Add(MakeEntry("COM CLSID", shortClsid, def, offline, "SOFTWARE",
                                    sub + "\\" + clsid + "\\InprocServer32", ""));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[CollectComClsid]", ex); }
        }

        // ============================================================
        // LSA PACKAGES (старый метод — оставлен для совместимости)
        // ============================================================
        private static void CollectLsaProviders(List<AutorunEntry> list, bool offline)
        {
            // теперь реализовано в CollectLsaNotificationPackages
        }

        // ============================================================
        // PRINT MONITORS
        // ============================================================
        private static void CollectPrintMonitors(List<AutorunEntry> list, bool offline)
        {
            string sub = @"ControlSet001\Control\Print\Monitors";
            try
            {
                using (var key = OpenHive(offline, "SYSTEM", sub))
                {
                    if (key == null) return;
                    foreach (var monitor in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var m = key.OpenSubKey(monitor))
                            {
                                string driver = m?.GetValue("Driver")?.ToString() ?? "";
                                if (!string.IsNullOrEmpty(driver))
                                    list.Add(MakeEntry("Print Monitor", monitor, driver, offline, "SYSTEM",
                                        sub + "\\" + monitor, "Driver"));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[CollectPrintMonitors]", ex); }
        }

        // ============================================================
        // SHELL SERVICE OBJECT DELAY LOAD / SHARED TASK / SHELL EXEC HOOKS
        // ============================================================
        private static void CollectShellServiceObject(List<AutorunEntry> list, bool offline)
        {
            string sub = @"Microsoft\Windows\CurrentVersion\ShellServiceObjectDelayLoad";
            try
            {
                using (var key = OpenHive(offline, "SOFTWARE", sub))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames())
                    {
                        string val = key.GetValue(name)?.ToString() ?? "";
                        list.Add(MakeEntry("ShellServiceObject", name, val, offline, "SOFTWARE", sub, name));
                    }
                }
            }
            catch { }
        }

        private static void CollectSharedTaskScheduler(List<AutorunEntry> list, bool offline)
        {
            string sub = @"Microsoft\Windows\CurrentVersion\Explorer\SharedTaskScheduler";
            try
            {
                using (var key = OpenHive(offline, "SOFTWARE", sub))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames())
                    {
                        string val = key.GetValue(name)?.ToString() ?? "";
                        list.Add(MakeEntry("SharedTaskScheduler", name, val, offline, "SOFTWARE", sub, name));
                    }
                }
            }
            catch { }
        }

        private static void CollectExplorerExtensions(List<AutorunEntry> list, bool offline)
        {
            string sub = @"Microsoft\Windows\CurrentVersion\Explorer\ShellExecuteHooks";
            try
            {
                using (var key = OpenHive(offline, "SOFTWARE", sub))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames())
                    {
                        string val = key.GetValue(name)?.ToString() ?? "";
                        list.Add(MakeEntry("ShellExecuteHook", name, val, offline, "SOFTWARE", sub, name));
                    }
                }
            }
            catch { }
        }

        // ============================================================
        // APP PATHS
        // ============================================================
        private static void CollectAppPaths(List<AutorunEntry> list, bool offline)
        {
            string sub = @"Microsoft\Windows\CurrentVersion\App Paths";
            try
            {
                using (var key = OpenHive(offline, "SOFTWARE", sub))
                {
                    if (key == null) return;
                    foreach (var exe in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var ek = key.OpenSubKey(exe))
                            {
                                string def = ek?.GetValue("")?.ToString() ?? "";
                                if (!string.IsNullOrEmpty(def))
                                {
                                    string lower = def.ToLowerInvariant();
                                    if (lower.Contains(@"\program files\common files\system\")) continue;
                                    if (lower.Contains(@"\windows\system32\")) continue;
                                    if (lower.Contains(@"\windows\syswow64\")) continue;

                                    list.Add(MakeEntry("AppPath", exe, def, offline, "SOFTWARE",
                                        sub + "\\" + exe, ""));
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // ============================================================
        // WSH / BHO
        // ============================================================
        private static void CollectWshHooks(List<AutorunEntry> list, bool offline)
        {
            string[] subs =
            {
                @"Microsoft\Windows Script Host\Settings",
                @"Classes\CLSID\{72C24DD5-D70A-438B-8A42-98424B88AFB8}\InprocServer32",
                @"Classes\CLSID\{F935DC22-1CF0-11D0-ADB9-00C04FD58A0B}\InprocServer32"
            };

            foreach (var sub in subs)
            {
                try
                {
                    using (var key = OpenHive(offline, "SOFTWARE", sub))
                    {
                        if (key == null) continue;
                        foreach (var name in key.GetValueNames())
                        {
                            string val = key.GetValue(name)?.ToString() ?? "";
                            list.Add(MakeEntry("WSH", name, val, offline, "SOFTWARE", sub, name));
                        }
                    }
                }
                catch { }
            }
        }

        private static void CollectBrowserHelpers(List<AutorunEntry> list, bool offline)
        {
            string sub = @"Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects";
            try
            {
                using (var key = OpenHive(offline, "SOFTWARE", sub))
                {
                    if (key == null) return;
                    foreach (var name in key.GetSubKeyNames())
                        list.Add(MakeEntry("BHO", name, "", offline, "SOFTWARE", sub + "\\" + name, ""));
                }
            }
            catch { }
        }

        // ============================================================
        // ВНУТРЕННИЕ ХЕЛПЕРЫ
        // ============================================================
        private static AutorunEntry MakeEntry(string category, string name, string path,
            bool offline, string hive, string keyPath, string valueName)
        {
            string cleanPath = ExtractPath(path);
            string normalizedPath = NormalizeToLive(cleanPath);

            var e = new AutorunEntry
            {
                Category = category,
                Name = name,
                Path = path,
                Hive = hive,
                KeyPath = keyPath,
                ValueName = valueName,
                FileExists = !string.IsNullOrEmpty(normalizedPath) && File.Exists(normalizedPath)
            };

            if (e.FileExists)
            {
                try
                {
                    e.Signed = Heuristics.IsSigned(normalizedPath);

                    var fi = new FileInfo(normalizedPath);
                    e.CreatedAt = fi.CreationTime;
                    e.ModifiedAt = fi.LastWriteTime;

                    double ageDays = (DateTime.Now - fi.CreationTime).TotalDays;
                    e.AgeDays = (int)ageDays;
                    e.FreshFile = ageDays < 7;
                }
                catch { }
            }

            return e;
        }

        private static string ExtractPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim();
            int q1 = s.IndexOf('"');
            if (q1 >= 0)
            {
                int q2 = s.IndexOf('"', q1 + 1);
                if (q2 > q1) return s.Substring(q1 + 1, q2 - q1 - 1);
            }
            int space = s.IndexOf(' ');
            if (space > 0 && s.Contains(".exe")) return s.Substring(0, space);
            return s;
        }

        private static string NormalizeToLive(string path)
        {
            try
            {
                if (RegistryHelper.IsWinReEnvironment() && path.Length > 1 && path[1] == ':')
                {
                    char drive = path[0];
                    if (char.ToUpper(drive) != 'X')
                    {
                        string sysDrive = RegistryHelper.GetSystemDrive();
                        return sysDrive + path.Substring(2);
                    }
                }
            }
            catch { }
            return path;
        }

        // ============================================================
        // ЭВРИСТИКА
        // ============================================================
        private static void MarkSuspicious(AutorunEntry e)
        {
            if (string.IsNullOrEmpty(e.Path)) return;
            string lower = e.Path.ToLowerInvariant();

            foreach (var frag in SuspiciousPathParts)
            {
                if (lower.Contains(frag.ToLowerInvariant()))
                {
                    e.Suspicious = true;
                    e.SuspicionReason = $"Путь в {frag}";
                    return;
                }
            }

            if (lower.Contains(".tmp") || lower.Contains(".scr") || lower.Contains(".pif"))
            {
                e.Suspicious = true;
                e.SuspicionReason = "Подозрительное расширение";
                return;
            }

            if (e.Path.StartsWith("cmd ") || e.Path.StartsWith("powershell") ||
                e.Path.StartsWith("wscript") || e.Path.StartsWith("cscript") ||
                e.Path.StartsWith("mshta") || e.Path.StartsWith("rundll32"))
            {
                e.Suspicious = true;
                e.SuspicionReason = "LOLBin-запуск (" + e.Path.Split(' ')[0] + ")";
                return;
            }

            if (!e.FileExists && !string.IsNullOrEmpty(e.Path) && e.Path.Contains(".exe"))
            {
                e.Suspicious = true;
                e.SuspicionReason = "Файла нет — запись-призрак";
                return;
            }

            if (e.Category == "Run" && e.FileExists && !e.Signed)
            {
                e.Suspicious = true;
                e.SuspicionReason = "Автозапуск без цифровой подписи";
                return;
            }

            if (e.Category == "Run" && e.FreshFile && !e.Suspicious)
            {
                e.Suspicious = true;
                e.SuspicionReason = $"Файл свежий ({e.AgeDays} дней)";
            }
        }

        // ============================================================
        // ОТКРЫТИЕ КЛЮЧА
        // ============================================================
        private static RegistryKey OpenHive(bool offline, string hive, string subPath)
        {
            try
            {
                if (!offline)
                {
                    if (hive == "SOFTWARE")
                        return Registry.LocalMachine.OpenSubKey(@"SOFTWARE\" + subPath);
                    if (hive == "SYSTEM")
                        return Registry.LocalMachine.OpenSubKey(@"SYSTEM\" + subPath);
                    return null;
                }

                if (!RegistryHelper.IsWinReEnvironment()) return null;
                string _;
                RegistryHelper.EnsureOfflineHives(out _);

                string mount;
                if (hive == "SOFTWARE") mount = "BB_Offline_SOFTWARE";
                else if (hive == "SYSTEM") mount = "BB_Offline_SYSTEM";
                else mount = "BB_Offline_SOFTWARE";

                return Registry.LocalMachine.OpenSubKey(mount + "\\" + subPath);
            }
            catch (Exception ex)
            {
                BbLog.Error("[AutorunsExtended.OpenHive]", ex);
                return null;
            }
        }

        private static void ScanHive(List<AutorunEntry> list, bool offline, string hive, string subPath,
            string category, string[] specificValues = null)
        {
            try
            {
                using (var key = OpenHive(offline, hive, subPath))
                {
                    if (key == null) return;

                    if (specificValues != null)
                    {
                        foreach (var val in specificValues)
                        {
                            string v = key.GetValue(val)?.ToString() ?? "";
                            if (!string.IsNullOrWhiteSpace(v))
                                list.Add(MakeEntry(category, val, v, offline, hive, subPath, val));
                        }
                    }
                    else
                    {
                        foreach (var name in key.GetValueNames())
                        {
                            string v = key.GetValue(name)?.ToString() ?? "";
                            list.Add(MakeEntry(category, name, v, offline, hive, subPath, name));
                        }

                        foreach (var subName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using (var sub = key.OpenSubKey(subName))
                                {
                                    string v = sub?.GetValue("")?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(v))
                                        list.Add(MakeEntry(category, subName, v, offline, hive,
                                            subPath + "\\" + subName, ""));
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { BbLog.Error($"[ScanHive/{category}]", ex); }
        }
    }
}