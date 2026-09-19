// language: C#, file: Core/PolicyResetHelper.cs
// Массовый сброс политик: TaskMgr, RegistryTools, CMD, UAC, PowerButton,
// Control Panel, Switch User, Cursor Suppression, Explorer-ограничения.
// Работает и в обычной системе, и в WinRE (через оффлайн-кусты).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace BunnyBlack.Core
{
    public class PolicyResetResult
    {
        public string Name { get; set; }
        public string Hive { get; set; }
        public string Path { get; set; }
        public string ValueName { get; set; }
        public bool WasSet { get; set; }
        public bool Removed { get; set; }
        public string Error { get; set; }
    }

    public static class PolicyResetHelper
    {
        // (Hive, Path, ValueName)
        private static readonly (string hive, string path, string name)[] Policies =
        {
            // Task Manager / Regedit / CMD
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools"),
            ("HKCU", @"Software\Policies\Microsoft\Windows\System",             "DisableCMD"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools"),
            ("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System",             "DisableCMD"),

            // Explorer
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoControlPanel"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRun"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWinKeys"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoViewOnDrive"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDrives"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFolderOptions"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDesktop"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFind"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLogoff"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoClose"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoControlPanel"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRun"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoViewOnDrive"),

            // Lock / password / UAC
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableChangePassword"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableChangePassword"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "FilterAdministratorToken"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ValidateAdminCodeSignatures"),

            // Power Button / Switch User / cursor
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "HideFastUserSwitching"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "shutdownwithoutlogon"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "EnableCursorSuppression"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableCursorSuppression"),
            ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLogoff"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "HideFastUserSwitching"),

            // DisallowRun / DisallowRun keys
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "DisallowRun"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "RestrictRun"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoTrayContextMenu"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFileMenu"),
        };

        // ============================================================
        // СБРОС ВСЕХ
        // ============================================================
        public static List<PolicyResetResult> ResetAll()
        {
            var results = new List<PolicyResetResult>();
            bool winRE = RegistryHelper.IsWinReEnvironment();

            if (winRE)
            {
                string _;
                RegistryHelper.EnsureOfflineHives(out _);
            }

            foreach (var p in Policies)
            {
                var r = new PolicyResetResult
                {
                    Name = $"{p.hive}\\{p.path}\\{p.name}",
                    Hive = p.hive,
                    Path = p.path,
                    ValueName = p.name
                };

                try
                {
                    RegistryKey root = null;
                    string mappedPath = p.path;

                    if (!winRE)
                    {
                        root = p.hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                    }
                    else
                    {
                        // оффлайн
                        if (p.hive == "HKLM")
                        {
                            if (p.path.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase))
                                mappedPath = "BunnyBlack_Offline_SOFTWARE\\" + p.path.Substring(9);
                            else if (p.path.StartsWith("SYSTEM\\", StringComparison.OrdinalIgnoreCase))
                                mappedPath = "BunnyBlack_Offline_SYSTEM\\" + p.path.Substring(7);
                            root = Registry.LocalMachine;
                        }
                        else
                        {
                            // HKCU → NTUSER
                            try
                            {
                                using (var test = Registry.Users.OpenSubKey("BunnyBlack_Offline_HKCU"))
                                {
                                    if (test != null)
                                    {
                                        root = Registry.Users;
                                        mappedPath = "BunnyBlack_Offline_HKCU\\" + p.path;
                                    }
                                    else
                                    {
                                        // fallback — HKLM-дубликат
                                        mappedPath = "BunnyBlack_Offline_SOFTWARE\\" + p.path;
                                        root = Registry.LocalMachine;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (root == null) { r.Error = "Корень не доступен"; results.Add(r); continue; }

                    using (var key = root.OpenSubKey(mappedPath, true))
                    {
                        if (key == null) { results.Add(r); continue; }   // нет ключа — уже сброшено

                        if (key.GetValue(p.name) != null)
                        {
                            r.WasSet = true;
                            try
                            {
                                key.DeleteValue(p.name, false);
                                r.Removed = true;
                            }
                            catch (Exception ex) { r.Error = ex.Message; }
                        }
                    }
                }
                catch (Exception ex) { r.Error = ex.Message; }

                results.Add(r);
            }

            return results;
        }
    }
}