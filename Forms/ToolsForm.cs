// language: C#, file: Forms/ToolsForm.cs
// SFC: подготовка в фоновом потоке, прогресс-диалог без крестика,
// программное закрытие не триггерит отмену.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class ToolsForm : UserControl
    {
        private bool isWinRE;
        private Label titleLabel;

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
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;
            InitializeComponent();

            ThemeManager.ThemeChanged += ApplyTheme;
            Loc.LanguageChanged += ApplyLanguage;
        }

        public void ApplyTheme()
        {
            if (InvokeRequired) { Invoke(new Action(ApplyTheme)); return; }
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;
            ThemeHelper.Apply(this);
            Invalidate(true);
        }

        public void ApplyLanguage()
        {
            if (InvokeRequired) { Invoke(new Action(ApplyLanguage)); return; }
            if (titleLabel != null)
                titleLabel.Text = "Дополнительные возможности" + (isWinRE ? " (WinRE)" : "");
            Invalidate(true);
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
                BackColor = ThemeManager.Background
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            titleLabel = new Label
            {
                Text = "Дополнительные возможности" + (isWinRE ? " (WinRE)" : ""),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(titleLabel, 0, 0);

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
                ("Разрешить sfc.exe (WinRE)", "Снять ASR + добавить exclusion (только WinRE)", AllowSfcInWinRE),
                ("Заменить sethc и utilman", "Заменяет sethc.exe и utilman.exe на Bunny Black (только WinRE)", ReplaceSethcUtilman),
                ("sfc /scannow", "Проверяет целостность системных файлов (диск определится сам)", () => RunSfcScannow()),
                ("Включить UAC", "Включает контроль учётных записей", EnableUAC),
                ("Войти в WinRE", "Перезагружает компьютер в среду восстановления", EnterWinRE),

                ("Восстановление загрузчика", "", null),
                ("bootrec /fixmbr", "Восстановить главную загрузочную запись", () => RunAndShow("fixmbr", BootHelper.FixMbr)),
                ("bootrec /fixboot", "Восстановить загрузочный сектор активного раздела", () => RunAndShow("fixboot", BootHelper.FixBoot)),
                ("bootrec /rebuildbcd", "Пересобрать хранилище BCD", () => RunAndShow("rebuildbcd", BootHelper.RebuildBcd)),
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
            if (action == null)
            {
                var headPanel = new Panel
                {
                    Height = 32,
                    Width = 850,
                    BackColor = ThemeManager.Background,
                    Margin = new Padding(0, 12, 0, 8)
                };
                headPanel.Controls.Add(new Label
                {
                    Text = name,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    ForeColor = ThemeManager.Success,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(8, 0, 0, 0),
                    BackColor = Color.Transparent
                });
                return headPanel;
            }

            var panel = new Panel
            {
                Height = 65,
                Width = 850,
                BackColor = ThemeManager.PanelAlt,
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
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            }, 0, 0);

            info.Controls.Add(new Label
            {
                Text = desc,
                Font = new Font("Segoe UI", 10),
                ForeColor = ThemeManager.Muted,
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
                Text = Loc.T("btn.run"),
                Height = 38,
                Width = 130,
                BackColor = ThemeManager.Panel,
                ForeColor = ThemeManager.Foreground,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Border },
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
        // BOOTREC
        // ============================================================
        private void RunAndShow(string title, Func<string> action)
        {
            try
            {
                if (MessageBoxHelper.Show($"Выполнить bootrec /{title}?", "Подтверждение",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

                string result = action();
                if (string.IsNullOrWhiteSpace(result)) result = "(команда выполнена, вывода нет)";
                NedoMessageBox.Show($"bootrec /{title}:\n\n{result}");
            }
            catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
        }

        // ============================================================
        // РАЗРЕШИТЬ sfc.exe
        // ============================================================
        private void AllowSfcInWinRE()
        {
            if (!isWinRE)
            {
                NedoMessageBox.Show("Только для WinRE.", isError: true);
                return;
            }

            string drive = DetectRealWindowsDrive();
            if (string.IsNullOrEmpty(drive))
            {
                NedoMessageBox.Show("Windows не найдена.", isError: true);
                return;
            }
            if (!drive.EndsWith(":")) drive += ":";
            string sfcTarget = drive + @"\Windows\System32\sfc.exe";

            if (!File.Exists(sfcTarget))
            {
                NedoMessageBox.Show($"Не найден:\n{sfcTarget}", isError: true);
                return;
            }

            DisableAsrRule("26190899-1602-49e8-8b27-eb1d0a1ce869");
            AddDefenderExclusion(sfcTarget);
            AddDefenderExclusion(Path.GetDirectoryName(sfcTarget));

            NedoMessageBox.Show(
                "ASR-правило снято.\n" +
                "Defender exclusion добавлен:\n\n" + sfcTarget);
        }

        // ============================================================
        // SFC — асинхронная подготовка + запуск
        // ============================================================
        private async void RunSfcScannow()
        {
            if (!isWinRE)
            {
                RunCmdVisible("sfc /scannow");
                return;
            }

            string drive = DetectRealWindowsDrive();
            if (string.IsNullOrEmpty(drive))
            {
                NedoMessageBox.Show(
                    "Не удалось найти диск с Windows.\n\n" +
                    "Программа перебрала все разделы и ни на одном\n" +
                    "не нашла одновременно:\n" +
                    "  • Windows\\System32\\sfc.exe\n" +
                    "  • Windows\\System32\\config\\SOFTWARE\n" +
                    "  • Windows\\WinSxS",
                    isError: true);
                return;
            }
            if (!drive.EndsWith(":")) drive += ":";
            string winDir = drive + @"\Windows";
            string system32 = winDir + @"\System32";
            string winSxs = winDir + @"\WinSxS";
            string sfcTarget = system32 + @"\sfc.exe";

            if (!File.Exists(sfcTarget))
            {
                NedoMessageBox.Show($"Не найден sfc.exe:\n{sfcTarget}", isError: true);
                return;
            }

            var progress = new SfcProgressForm(drive);
            var cts = progress.CancellationSource;

            // Показываем немодально
            progress.Show(this);
            Application.DoEvents();

            bool prepared = await Task.Run(() =>
            {
                try
                {
                    progress.SetStatus("Снятие ASR-правила...");
                    if (cts.IsCancellationRequested) return false;
                    DisableAsrRule("26190899-1602-49e8-8b27-eb1d0a1ce869");

                    if (cts.IsCancellationRequested) return false;
                    progress.SetStatus("Defender exclusion...");
                    AddDefenderExclusion(sfcTarget);
                    AddDefenderExclusion(system32);
                    AddDefenderExclusion(winSxs);

                    if (cts.IsCancellationRequested) return false;
                    progress.SetStatus("Починка ACL на WinSxS\\Temp...");
                    FixAclQuick(winSxs + @"\Temp");
                    FixAclQuick(winSxs + @"\ManifestCache");

                    if (cts.IsCancellationRequested) return false;
                    progress.SetStatus("Обработка pending.xml...");
                    string pendingXml = winSxs + @"\pending.xml";
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

                    if (cts.IsCancellationRequested) return false;
                    progress.SetStatus("Создание WinSxS\\Temp...");
                    try
                    {
                        Directory.CreateDirectory(winSxs + @"\Temp\PendingDeletes");
                        Directory.CreateDirectory(winSxs + @"\Temp\PendingRenames");
                    }
                    catch { }

                    return true;
                }
                catch (Exception ex)
                {
                    BbLog.Error("[SFC prepare]", ex);
                    return false;
                }
            });

            // Помечаем программное закрытие — FormClosing не отменит задачу
            progress.MarkAsCompleted();

            if (!progress.IsDisposed)
                progress.Close();

            if (!prepared)
            {
                NedoMessageBox.Show("Подготовка прервана. Смотри debug.log.", isError: true);
                return;
            }

            string command = $"\"{sfcTarget}\" /scannow /offbootdir={drive}\\ /offwindir={winDir}";
            BbLog.Info("[SFC] " + command);
            RunCmdVisible(command);
        }

        // ============================================================
        // Быстрая починка ACL — только папка, без /r
        // ============================================================
        private void FixAclQuick(string path)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                RunCmdWait($"takeown /f \"{path}\" /d y", 30000);
                RunCmdWait($"icacls \"{path}\" /grant \"SYSTEM\":(F) /q", 15000);
                RunCmdWait($"icacls \"{path}\" /grant \"Administrators\":(F) /q", 15000);
                RunCmdWait($"icacls \"{path}\" /grant \"NT SERVICE\\TrustedInstaller\":(F) /q", 15000);
            }
            catch (Exception ex)
            {
                BbLog.Error("[FixAclQuick] " + path, ex);
            }
        }

        private void RunCmdWait(string command, int timeoutMs = 60000)
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
                using (var p = Process.Start(si))
                {
                    if (p == null) return;
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();

                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        BbLog.Warn($"[RunCmdWait] timeout: {command}");
                    }
                }
            }
            catch (Exception ex)
            {
                BbLog.Error("[RunCmdWait] " + command, ex);
            }
        }

        // ============================================================
        // АВТООПРЕДЕЛЕНИЕ ДИСКА
        // ============================================================
        private string DetectRealWindowsDrive()
        {
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;

                        string root = d.RootDirectory.FullName.TrimEnd('\\').ToUpperInvariant();
                        if (root.StartsWith("X:")) continue;

                        string sfcPath = root + @"\WINDOWS\SYSTEM32\SFC.EXE";
                        string configSw = root + @"\WINDOWS\SYSTEM32\CONFIG\SOFTWARE";
                        string winSxs = root + @"\WINDOWS\WINSXS";

                        if (File.Exists(sfcPath) &&
                            File.Exists(configSw) &&
                            Directory.Exists(winSxs))
                        {
                            BbLog.Info($"[DetectRealWindowsDrive] найден {root}");
                            return root;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { BbLog.Error("[DetectRealWindowsDrive/1]", ex); }

            try
            {
                var psi = new ProcessStartInfo("bcdedit.exe", "/enum {default}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(5000);

                        foreach (var line in output.Split('\n'))
                        {
                            string t = line.Trim();
                            if (t.StartsWith("osdevice", StringComparison.OrdinalIgnoreCase))
                            {
                                int idx = t.IndexOf("partition=", StringComparison.OrdinalIgnoreCase);
                                if (idx >= 0)
                                {
                                    string letter = t.Substring(idx + "partition=".Length).Trim();
                                    if (letter.Length >= 2 && letter[1] == ':')
                                    {
                                        string root = letter.Substring(0, 2).ToUpperInvariant();

                                        string sfcPath = root + @"\Windows\System32\sfc.exe";
                                        string configSw = root + @"\Windows\System32\config\SOFTWARE";
                                        if (File.Exists(sfcPath) && File.Exists(configSw))
                                        {
                                            BbLog.Info($"[DetectRealWindowsDrive/bcdedit] {root}");
                                            return root;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[DetectRealWindowsDrive/2]", ex); }

            try
            {
                string fb = RegistryHelper.GetSystemDrive();
                if (!string.IsNullOrEmpty(fb))
                {
                    if (!fb.EndsWith(":")) fb += ":";
                    if (!fb.ToUpperInvariant().StartsWith("X:"))
                    {
                        string sfcPath = fb + @"\Windows\System32\sfc.exe";
                        string configSw = fb + @"\Windows\System32\config\SOFTWARE";
                        if (File.Exists(sfcPath) && File.Exists(configSw))
                        {
                            BbLog.Info($"[DetectRealWindowsDrive/RegistryHelper] {fb}");
                            return fb;
                        }
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[DetectRealWindowsDrive/3]", ex); }

            BbLog.Warn("[DetectRealWindowsDrive] Windows не найдена");
            return null;
        }

        // ============================================================
        // ASR / Defender
        // ============================================================
        private void DisableAsrRule(string ruleGuid)
        {
            try
            {
                string ps =
                    "$ErrorActionPreference='SilentlyContinue';" +
                    $"Add-MpPreference -AttackSurfaceReductionRules_Ids '{ruleGuid}' " +
                    "-AttackSurfaceReductionRules_Actions Disabled";

                byte[] b = System.Text.Encoding.Unicode.GetBytes(ps);
                string enc = Convert.ToBase64String(b);

                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + enc)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(psi)?.WaitForExit(10000);
            }
            catch (Exception ex) { BbLog.Error("[DisableAsrRule]", ex); }
        }

        private void AddDefenderExclusion(string path)
        {
            try
            {
                string ps =
                    "$ErrorActionPreference='SilentlyContinue';" +
                    $"Add-MpPreference -ExclusionPath '{path}'";

                byte[] b = System.Text.Encoding.Unicode.GetBytes(ps);
                string enc = Convert.ToBase64String(b);

                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + enc)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(psi)?.WaitForExit(8000);
            }
            catch { }
        }

        // ============================================================
        // sethc / utilman
        // ============================================================
        private void ReplaceSethcUtilman()
        {
            try
            {
                if (!isWinRE) throw new Exception("Только для WinRE.");
                if (!IsAdministrator()) throw new Exception("Нужны права администратора.");

                string windowsPath = FindWindowsDirectoryInWinRE();
                if (string.IsNullOrEmpty(windowsPath)) throw new Exception("Windows не найдена.");

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

                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    UseShellExecute = true,
                    CreateNoWindow = false
                });
                p?.WaitForExit();

                SecureDelete(batPath);
                SecureDelete(tempSethc);
                SecureDelete(tempUtilman);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка: {ex.Message}");
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
        // UAC / WinRE
        // ============================================================
        private void EnableUAC()
        {
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"EnableLUA\" /t REG_DWORD /d 1 /f");
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"ConsentPromptBehaviorAdmin\" /t REG_DWORD /d 5 /f");
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"PromptOnSecureDesktop\" /t REG_DWORD /d 1 /f");
            NedoMessageBox.Show("UAC включён.");
        }

        private void EnterWinRE()
        {
            if (MessageBoxHelper.Show("Перезагрузить в WinRE?",
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
            catch (Exception ex) { BbLog.Error("[RunCmd]", ex); }
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
                    var timer = new System.Windows.Forms.Timer { Interval = 300 };
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

        private string FindWindowsDirectoryInWinRE()
        {
            string detected = DetectRealWindowsDrive();
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.ThemeChanged -= ApplyTheme;
                Loc.LanguageChanged -= ApplyLanguage;
            }
            base.Dispose(disposing);
        }
    }

    // ============================================================
    // Прогресс-форма для подготовки sfc
    // ============================================================
    public class SfcProgressForm : Form
    {
        private Label statusLabel;
        private ProgressBar bar;
        private Button cancelBtn;

        private bool _completedProgrammatically = false;

        public CancellationTokenSource CancellationSource { get; private set; }
            = new CancellationTokenSource();

        public SfcProgressForm(string drive)
        {
            this.Text = "Подготовка sfc";
            this.Size = new Size(480, 180);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ControlBox = false;   // никакого крестика — только кнопка "Отмена"
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;

            var title = new Label
            {
                Text = $"Подготовка sfc /scannow на диске {drive}",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                BackColor = Color.Transparent,
                Location = new Point(16, 16),
                Size = new Size(440, 24)
            };
            Controls.Add(title);

            statusLabel = new Label
            {
                Text = "Запуск...",
                Font = new Font("Segoe UI", 9),
                ForeColor = ThemeManager.Muted,
                BackColor = Color.Transparent,
                Location = new Point(16, 48),
                Size = new Size(440, 20)
            };
            Controls.Add(statusLabel);

            bar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Location = new Point(16, 76),
                Size = new Size(440, 20)
            };
            Controls.Add(bar);

            cancelBtn = new Button
            {
                Text = "Отмена",
                Location = new Point(360, 110),
                Size = new Size(96, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.PanelAlt,
                ForeColor = ThemeManager.Foreground,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Border }
            };
            cancelBtn.Click += (s, e) =>
            {
                CancellationSource.Cancel();
                this.Close();
            };
            Controls.Add(cancelBtn);

            this.FormClosing += (s, e) =>
            {
                if (_completedProgrammatically) return;
                if (!CancellationSource.IsCancellationRequested)
                    CancellationSource.Cancel();
            };
        }

        public void MarkAsCompleted()
        {
            _completedProgrammatically = true;
        }

        public void SetStatus(string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => SetStatus(text)));
                return;
            }
            statusLabel.Text = text;
        }
    }
}