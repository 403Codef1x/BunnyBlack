// language: C#, file: Forms/ToolsForm.cs
// Полная замена.
// Убраны пункты: «Снять блокировки», «Открыть карантин», «Разблокировать и удалить файл».
// Остались: sethc/utilman, sfc, UAC, WinRE.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class ToolsForm : UserControl
    {
        private bool isWinRE;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public ToolsForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(28, 24, 28, 20),
                BackColor = Color.FromArgb(13, 13, 13)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = "Дополнительные возможности" + (isWinRE ? " (WinRE)" : ""),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(title, 0, 0);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Transparent };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 10)
            };

            var tools = new (string name, string desc, Action action)[]
            {
                ("Заменить sethc и utilman", "Заменяет sethc.exe и utilman.exe на Bunny Black (только WinRE)", ReplaceSethcUtilman),
                ("sfc /scannow", "Проверяет целостность системных файлов (диск определится сам)", RunSfcScannow),
                ("Включить UAC", "Включает контроль учётных записей", EnableUAC),
                ("Войти в WinRE", "Перезагружает компьютер в среду восстановления", EnterWinRE),
            };

            foreach (var tool in tools)
            {
                var panel = CreateToolPanel(tool.name, tool.desc, tool.action);
                flow.Controls.Add(panel);
            }

            scroll.Controls.Add(flow);
            layout.Controls.Add(scroll, 0, 1);
            this.Controls.Add(layout);
        }

        private Panel CreateToolPanel(string name, string desc, Action action)
        {
            var panel = new Panel
            {
                Height = 65,
                Width = 850,
                BackColor = Color.FromArgb(22, 22, 22),
                Margin = new Padding(0, 0, 0, 8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(16, 8, 16, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

            var info = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

            info.Controls.Add(new Label
            {
                Text = name,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            }, 0, 0);

            info.Controls.Add(new Label
            {
                Text = desc,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(102, 102, 102),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            }, 0, 1);

            layout.Controls.Add(info, 0, 0);

            var btnContainer = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 130,
                Height = 38,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent
            };

            var btn = new Button
            {
                Text = "Выполнить",
                Height = 38,
                Width = 130,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(240, 240, 240),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11),
                UseVisualStyleBackColor = false
            };
            btn.Click += (s, e) =>
            {
                try { action(); }
                catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
            };

            btnContainer.Controls.Add(btn);
            layout.Controls.Add(btnContainer, 1, 0);

            panel.Controls.Add(layout);
            return panel;
        }

        // ============================================================
        // sethc / utilman
        // ============================================================
        private void ReplaceSethcUtilman()
        {
            try
            {
                if (!isWinRE)
                    throw new Exception("Эта функция работает только в WinRE.");

                if (!IsAdministrator())
                    throw new Exception("Нужны права администратора.");

                string windowsPath = FindWindowsDirectoryInWinRE();
                if (string.IsNullOrEmpty(windowsPath))
                    throw new Exception("Не удалось найти Windows.");

                string system32 = Path.Combine(windowsPath, "System32");
                string exePath = Application.ExecutablePath;

                string sethcPath = Path.Combine(system32, "sethc.exe");
                string utilmanPath = Path.Combine(system32, "utilman.exe");
                string sethcBak = Path.Combine(system32, "sethc.exe.bak");
                string utilmanBak = Path.Combine(system32, "utilman.exe.bak");
                string tempSethc = Path.Combine(Path.GetTempPath(), "sethc_temp.exe");
                string tempUtilman = Path.Combine(Path.GetTempPath(), "utilman_temp.exe");

                File.Copy(exePath, tempSethc, true);
                File.Copy(exePath, tempUtilman, true);

                string batPath = Path.Combine(Path.GetTempPath(), "replace_sethc.bat");
                File.WriteAllText(batPath, $@"
@echo off
title Замена sethc.exe и utilman.exe
takeown /f ""{sethcPath}"" >nul 2>&1
icacls ""{sethcPath}"" /grant Administrators:F >nul 2>&1
takeown /f ""{utilmanPath}"" >nul 2>&1
icacls ""{utilmanPath}"" /grant Administrators:F >nul 2>&1
attrib -r -s -h ""{sethcPath}"" >nul 2>&1
attrib -r -s -h ""{utilmanPath}"" >nul 2>&1
if not exist ""{sethcBak}"" copy ""{sethcPath}"" ""{sethcBak}""
if not exist ""{utilmanBak}"" copy ""{utilmanPath}"" ""{utilmanBak}""
copy ""{tempSethc}"" ""{sethcPath}"" /y
copy ""{tempUtilman}"" ""{utilmanPath}"" /y
timeout /t 2 /nobreak >nul
");

                var si = new ProcessStartInfo
                {
                    FileName = batPath,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                var p = Process.Start(si);
                p?.WaitForExit();

                SecureDelete(batPath);
                SecureDelete(tempSethc);
                SecureDelete(tempUtilman);
            }
            catch (Exception ex)
            {
                throw new Exception($"Не удалось заменить файлы: {ex.Message}");
            }
        }

        private void SecureDelete(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                long len = new FileInfo(path).Length;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var zeros = new byte[Math.Min(len > 0 ? len : 1, 65536)];
                    long written = 0;
                    while (written < len)
                    {
                        int chunk = (int)Math.Min(zeros.Length, len - written);
                        fs.Write(zeros, 0, chunk);
                        written += chunk;
                    }
                    fs.Flush(true);
                }
                File.Delete(path);
            }
            catch { }
        }

        // ============================================================
        // SFC
        // ============================================================
        private void RunSfcScannow()
        {
            string command;
            if (isWinRE)
            {
                string drive = DetectWindowsDrive();
                if (string.IsNullOrEmpty(drive))
                {
                    NedoMessageBox.Show("Не удалось найти диск с Windows.", isError: true);
                    return;
                }
                if (!drive.EndsWith(":")) drive += ":";
                string winDir = drive + @"\Windows";
                string winSxsTemp = Path.Combine(winDir, @"WinSxS\Temp");

                try
                {
                    Directory.CreateDirectory(Path.Combine(winSxsTemp, "PendingDeletes"));
                    Directory.CreateDirectory(Path.Combine(winSxsTemp, "PendingRenames"));
                }
                catch { }

                string pendingXml = Path.Combine(winDir, @"WinSxS\pending.xml");
                if (File.Exists(pendingXml))
                {
                    try
                    {
                        string bak = pendingXml + ".old";
                        if (File.Exists(bak)) File.Delete(bak);
                        File.Move(pendingXml, bak);
                    }
                    catch { }
                }

                command = $"sfc /scannow /offbootdir={drive}\\ /offwindir={winDir}";
            }
            else command = "sfc /scannow";

            RunCmdVisible(command);
        }

        // ============================================================
        // UAC / WinRE
        // ============================================================
        private void EnableUAC()
        {
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"EnableLUA\" /t REG_DWORD /d 1 /f");
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"ConsentPromptBehaviorAdmin\" /t REG_DWORD /d 5 /f");
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"PromptOnSecureDesktop\" /t REG_DWORD /d 1 /f");
            NedoMessageBox.Show("UAC включён. Требуется перезагрузка.");
        }

        private void EnterWinRE()
        {
            if (MessageBoxHelper.Show(
                "Перезагрузить в среду восстановления (WinRE)?",
                "WinRE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RunCmd("shutdown /r /o /t 0");
        }

        // ============================================================
        // ХЕЛПЕРЫ
        // ============================================================
        private void RunCmd(string command)
        {
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + command,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(si)) p?.WaitForExit();
            }
            catch { }
        }

        private void RunCmdVisible(string command)
        {
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/k " + command,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                var p = Process.Start(si);

                if (p != null)
                {
                    var timer = new Timer { Interval = 300 };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        timer.Dispose();
                        try
                        {
                            p.Refresh();
                            IntPtr h = p.MainWindowHandle;
                            if (h != IntPtr.Zero)
                            {
                                ShowWindow(h, 9);
                                SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW);
                                SetForegroundWindow(h);
                            }
                        }
                        catch { }
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show("Ошибка запуска: " + ex.Message, isError: true);
            }
        }

        private string DetectWindowsDrive()
        {
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                        if (!d.IsReady) continue;
                        if (d.Name.StartsWith("X:", StringComparison.OrdinalIgnoreCase)) continue;

                        string root = d.RootDirectory.FullName.TrimEnd('\\');
                        if (!File.Exists(Path.Combine(root, @"Windows\System32\sfc.exe"))) continue;
                        if (!Directory.Exists(Path.Combine(root, "Windows", "WinSxS"))) continue;

                        return root;
                    }
                    catch { }
                }
            }
            catch { }

            try
            {
                string fb = RegistryHelper.GetSystemDrive();
                if (!string.IsNullOrEmpty(fb))
                {
                    if (!fb.EndsWith(":")) fb += ":";
                    return fb;
                }
            }
            catch { }

            return null;
        }

        private string FindWindowsDirectoryInWinRE()
        {
            string detected = DetectWindowsDrive();
            if (!string.IsNullOrEmpty(detected))
            {
                string win = Path.Combine(detected, "Windows");
                if (Directory.Exists(win)) return win;
            }

            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        if (d.Name.StartsWith("X:", StringComparison.OrdinalIgnoreCase)) continue;
                        if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                        string testPath = Path.Combine(d.RootDirectory.FullName, "Windows", "System32");
                        if (Directory.Exists(testPath)) return Path.Combine(d.RootDirectory.FullName, "Windows");
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private bool IsAdministrator()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var pr = new System.Security.Principal.WindowsPrincipal(id);
                return pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private Form FindOwner()
        {
            foreach (Form f in Application.OpenForms)
                if (f is MainForm) return f;
            return Form.ActiveForm;
        }
    }
}