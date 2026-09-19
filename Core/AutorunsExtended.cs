// language: C#, file: Core/AutorunsExtended.cs
// Расширенный сбор автозапусков: COM CLSID, LSA, Print Monitors, BootExecute,
// KnownDLLs, Winlogon Notify, ShellServiceObjectDelayLoad, SharedTaskScheduler,
// AppCertDlls, AppPaths, WSH-хуки, Explorer-расширения и т.д.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        // СВОД: пройти все ветки, вернуть единый список
        // ============================================================
        public static List<AutorunEntry> Collect(bool offline = false)
        {
            var list = new List<AutorunEntry>();

            try { CollectRunKeys(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectWinlogonExtra(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectComClsid(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectLsaProviders(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectPrintMonitors(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectBootExecute(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectKnownDlls(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectShellServiceObject(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectSharedTaskScheduler(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectAppCertDlls(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectAppPaths(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectExplorerExtensions(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectWshHooks(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectBrowserHelpers(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }
            try { CollectKnownSuspicious(list, offline); } catch (Exception e) { Debug.WriteLine(e.Message); }

            // эвристика
            foreach (var e in list) MarkSuspicious(e);
            return list;
        }

        // ============================================================
        // RUN / RUNONCE (сохранено как было + добавлено расширение)
        // ============================================================
        private static void CollectRunKeys(List<AutorunEntry> list, bool offline)
        {
            string[] subs = {
                @"Microsoft\Windows\CurrentVersion\Run",
                @"Microsoft\Windows\CurrentVersion\RunOnce",
                @"Microsoft\Windows\CurrentVersion\RunServices",
                @"Microsoft\Windows\CurrentVersion\RunServicesOnce",
                @"Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
                @"Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
            };

            foreach (var sub in subs)
            {
                ScanHive(list, offline, "SOFTWARE", sub, "Run");
            }
        }

        private static void CollectWinlogonExtra(List<AutorunEntry> list, bool offline)
        {
            string[] subs = {
                @"Microsoft\Windows NT\CurrentVersion\Winlogon"
            };

            foreach (var sub in subs)
            {
                ScanHive(list, offline, "SOFTWARE", sub, "Winlogon", specificValues: new[]
                {
                    "Shell", "Userinit", "Taskman", "System",
                    "AppSetup", "VmApplet", "GinaDLL", "UIHost"
                });
            }

            // Winlogon\Notify
            ScanHive(list, offline, "SOFTWARE",
                @"Microsoft\Windows NT\CurrentVersion\Winlogon\Notify", "WinlogonNotify");
        }

        // ============================================================
        // COM CLSID — заражение объектов
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

                                string shortClsid = clsid.Length > 38 ? clsid.Substring(0, 38) : clsid;
                                list.Add(MakeEntry("COM CLSID", shortClsid, def, offline, "SOFTWARE",
                                    sub + "\\" + clsid + "\\InprocServer32", ""));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[CollectComClsid] " + ex.Message); }
        }

        // ============================================================
        // LSA AUTHENTICATION PACKAGES
        // ============================================================
        private static void CollectLsaProviders(List<AutorunEntry> list, bool offline)
        {
            string[] subPaths = {
                @"ControlSet001\Control\Lsa\Authentication Packages",
                @"ControlSet001\Control\Lsa\Notification Packages",
                @"ControlSet001\Control\Lsa\Security Packages",
                @"ControlSet001\Control\Lsa\OSConfig"
            };

            foreach (var p in subPaths)
            {
                try
                {
                    using (var key = OpenHive(offline, "SYSTEM", p))
                    {
                        if (key == null) continue;
                        object def = key.GetValue("");
                        if (def is string[] arr)
                        {
                            foreach (var s in arr)
                                list.Add(MakeEntry("LSA Provider", p, s, offline, "SYSTEM", p, ""));
                        }
                        else if (def is string str)
                        {
                            list.Add(MakeEntry("LSA Provider", p, str, offline, "SYSTEM", p, ""));
                        }
                    }
                }
                catch { }
            }
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
            catch { }
        }

        // ============================================================
        // BOOT EXECUTE
        // ============================================================
        private static void CollectBootExecute(List<AutorunEntry> list, bool offline)
        {
            string sub = @"ControlSet001\Control\Session Manager";
            try
            {
                using (var key = OpenHive(offline, "SYSTEM", sub))
                {
                    if (key == null) return;
                    var boot = key.GetValue("BootExecute");
                    if (boot is string[] arr)
                        foreach (var s in arr)
                            list.Add(MakeEntry("BootExecute", "BootExecute", s, offline, "SYSTEM", sub, "BootExecute"));
                }
            }
            catch { }
        }

        // ============================================================
        // KNOWN DLLS
        // ============================================================
        private static void CollectKnownDlls(List<AutorunEntry> list, bool offline)
        {
            string sub = @"ControlSet001\Control\Session Manager\KnownDLLs";
            try
            {
                using (var key = OpenHive(offline, "SYSTEM", sub))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames())
                    {
                        string val = key.GetValue(name)?.ToString() ?? "";
                        if (val.Contains("\\") || val.Contains("/") || val.Contains(":"))
                            list.Add(MakeEntry("KnownDLL", name, val, offline, "SYSTEM", sub, name));
                    }
                }
            }
            catch { }
        }

        // ============================================================
        // SHELL SERVICE OBJECT DELAY LOAD
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

        // ============================================================
        // SHARED TASK SCHEDULER
        // ============================================================
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

        // ============================================================
        // APP CERT DLLS
        // ============================================================
        private static void CollectAppCertDlls(List<AutorunEntry> list, bool offline)
        {
            string sub = @"ControlSet001\Control\Session Manager\AppCertDlls";
            try
            {
                using (var key = OpenHive(offline, "SYSTEM", sub))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames())
                    {
                        string val = key.GetValue(name)?.ToString() ?? "";
                        list.Add(MakeEntry("AppCertDll", name, val, offline, "SYSTEM", sub, name));
                    }
                }
            }
            catch { }
        }

        // ============================================================
        // APP PATHS (подмена exe)
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
                                    list.Add(MakeEntry("AppPath", exe, def, offline, "SOFTWARE",
                                        sub + "\\" + exe, ""));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // ============================================================
        // EXPLORER EXTENSIONS / SHELL EXT
        // ============================================================
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
        // WSH — Windows Script Host
        // ============================================================
        private static void CollectWshHooks(List<AutorunEntry> list, bool offline)
        {
            string[] subs = {
                @"Microsoft\Windows Script Host\Settings",
                @"Classes\CLSID\{72C24DD5-D70A-438B-8A42-98424B88AFB8}\InprocServer32", // WScript.Shell
                @"Classes\CLSID\{F935DC22-1CF0-11D0-ADB9-00C04FD58A0B}\InprocServer32"  // WScript.Network
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

        // ============================================================
        // BROWSER HELPER OBJECTS
        // ============================================================
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
        // KNOWN SUSPICIOUS KEYS
        // ============================================================
        private static void CollectKnownSuspicious(List<AutorunEntry> list, bool offline)
        {
            // Office addins, Outlook, Exchange
            string[] subs = {
                @"Microsoft\Office\Outlook\Addins",
                @"Microsoft\Office\Excel\Addins",
                @"Microsoft\Office\Word\Addins"
            };

            foreach (var sub in subs)
            {
                try
                {
                    using (var key = OpenHive(offline, "SOFTWARE", sub))
                    {
                        if (key == null) continue;
                        foreach (var name in key.GetSubKeyNames())
                            list.Add(MakeEntry("Office Addin", name, "", offline, "SOFTWARE",
                                sub + "\\" + name, ""));
                    }
                }
                catch { }
            }
        }

        // ============================================================
        // ВНУТРЕННИЕ ХЕЛПЕРЫ
        // ============================================================
        private static AutorunEntry MakeEntry(string category, string name, string path,
            bool offline, string hive, string keyPath, string valueName)
        {
            string cleanPath = ExtractPath(path);
            return new AutorunEntry
            {
                Category = category,
                Name = name,
                Path = path,
                Hive = hive,
                KeyPath = keyPath,
                ValueName = valueName,
                FileExists = !string.IsNullOrEmpty(cleanPath) && File.Exists(NormalizeToLive(cleanPath)),
            };
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

            if (!string.IsNullOrEmpty(e.Path) && e.Path.StartsWith("cmd ") ||
                e.Path.StartsWith("powershell") || e.Path.StartsWith("wscript") ||
                e.Path.StartsWith("cscript") || e.Path.StartsWith("mshta") ||
                e.Path.StartsWith("rundll32"))
            {
                e.Suspicious = true;
                e.SuspicionReason = "LOLBin-запуск (" + e.Path.Split(' ')[0] + ")";
            }
        }

        // ============================================================
        // ОТКРЫТИЕ КЛЮЧА — живой или оффлайн
        // ============================================================
        private static RegistryKey OpenHive(bool offline, string hive, string subPath)
        {
            try
            {
                if (!offline)
                {
                    var root = hive == "SOFTWARE" ? Registry.LocalMachine : Registry.LocalMachine;
                    string prefix = hive == "SOFTWARE" ? "SOFTWARE\\" : "SYSTEM\\";
                    return root.OpenSubKey(prefix + subPath);
                }

                // оффлайн
                if (!RegistryHelper.IsWinReEnvironment()) return null;
                string _;
                RegistryHelper.EnsureOfflineHives(out _);

                string mount = hive == "SOFTWARE" ? "BunnyBlack_Offline_SOFTWARE"
                             : hive == "SYSTEM" ? "BunnyBlack_Offline_SYSTEM"
                             : hive == "SAM" ? "BunnyBlack_Offline_SAM"
                             : "BunnyBlack_Offline_SOFTWARE";

                return Registry.LocalMachine.OpenSubKey(mount + "\\" + subPath);
            }
            catch (Exception ex) { Debug.WriteLine("[OpenHive] " + ex.Message); return null; }
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
            catch { }
        }
    }
}