// language: C#, file: Forms/ScanForm.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class ScanForm : UserControl
    {
        private DataGridView grid;
        private RichTextBox logBox;
        private CheckBox autoFixCheck;
        private Button scanButton;
        private Button fixButton;
        private ProgressBar scanProgress;
        private Label titleLabel;
        private bool isScanning = false;
        private bool isScanCompleted = false;

        public bool IsScanCompleted => isScanCompleted;

        private class Threat
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Risk { get; set; }
            public string Hive { get; set; }
            public string Path { get; set; }
            public string ValueName { get; set; }
            public string RepairMethod { get; set; }
        }

        private List<Threat> foundThreats = new List<Threat>();

        public ScanForm(bool winRE)
        {
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
            if (titleLabel != null) titleLabel.Text = Loc.T("scan.title");
            if (autoFixCheck != null) autoFixCheck.Text = Loc.T("scan.autofix");
            if (scanButton != null && !isScanning) scanButton.Text = Loc.T("scan.start");
            if (fixButton != null) fixButton.Text = Loc.T("scan.fixall");
            if (grid != null && grid.Columns.Count >= 3)
            {
                grid.Columns[0].HeaderText = "Угроза";
                grid.Columns[1].HeaderText = "Описание";
                grid.Columns[2].HeaderText = "Риск";
            }
            Invalidate(true);
        }

        public void BeginAutoScan()
        {
            if (isScanCompleted || isScanning) return;
            if (!this.IsHandleCreated)
            {
                this.HandleCreated += (s, e) => BeginAutoScan();
                return;
            }
            Task.Run(() => PerformScan());
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(20),
                BackColor = ThemeManager.Background
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));

            titleLabel = new Label
            {
                Text = Loc.T("scan.title"),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            mainLayout.Controls.Add(titleLabel, 0, 0);

            autoFixCheck = new CheckBox
            {
                Text = Loc.T("scan.autofix"),
                ForeColor = ThemeManager.Foreground,
                Font = new Font("Segoe UI", 10),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Checked = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            mainLayout.Controls.Add(autoFixCheck, 0, 1);

            scanProgress = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Visible = false
            };
            mainLayout.Controls.Add(scanProgress, 0, 2);

            grid = CreateDarkGrid();
            grid.Columns.Clear();
            grid.Columns.Add("Name", "Угроза");
            grid.Columns.Add("Desc", "Описание");
            grid.Columns.Add("Risk", "Риск");
            grid.Columns[0].Width = 220;
            grid.Columns[1].Width = 400;
            grid.Columns[2].Width = 100;
            grid.AllowUserToAddRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            var gridPanel = new Panel { Dock = DockStyle.Fill, BackColor = ThemeManager.Background };
            gridPanel.Controls.Add(grid);
            mainLayout.Controls.Add(gridPanel, 0, 3);

            logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Background,
                ForeColor = ThemeManager.Foreground,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10),
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            var logPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Background,
                Padding = new Padding(0, 5, 0, 0)
            };
            logPanel.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, logPanel.ClientRectangle,
                    ThemeManager.Border, 1, ButtonBorderStyle.Solid,
                    ThemeManager.Border, 1, ButtonBorderStyle.Solid,
                    ThemeManager.Border, 1, ButtonBorderStyle.Solid,
                    ThemeManager.Border, 1, ButtonBorderStyle.Solid);
            };
            logPanel.Controls.Add(logBox);
            mainLayout.Controls.Add(logPanel, 0, 4);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 10, 0, 0),
                AutoSize = true
            };

            scanButton = new Button
            {
                Text = Loc.T("scan.start"),
                Width = 180,
                Height = 35,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.PanelAlt,
                ForeColor = ThemeManager.Foreground,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Border }
            };
            scanButton.Click += ScanButton_Click;

            fixButton = new Button
            {
                Text = Loc.T("scan.fixall"),
                Width = 150,
                Height = 35,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.SuccessBack,
                ForeColor = ThemeManager.Success,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                FlatAppearance = { BorderSize = 0 },
                Enabled = false
            };
            fixButton.Click += FixButton_Click;

            buttonPanel.Controls.Add(scanButton);
            buttonPanel.Controls.Add(fixButton);
            mainLayout.Controls.Add(buttonPanel, 0, 5);

            this.Controls.Add(mainLayout);
        }

        private DataGridView CreateDarkGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Background,
                ForeColor = ThemeManager.Foreground,
                BackgroundColor = ThemeManager.Background,
                GridColor = ThemeManager.Border,
                BorderStyle = BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 32
            };
            grid.RowTemplate.Height = 28;
            grid.ColumnHeadersDefaultCellStyle.BackColor = ThemeManager.Header;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ThemeManager.Foreground;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grid.DefaultCellStyle.BackColor = ThemeManager.Row;
            grid.DefaultCellStyle.ForeColor = ThemeManager.Foreground;
            grid.DefaultCellStyle.SelectionBackColor = ThemeManager.Selection;
            grid.DefaultCellStyle.SelectionForeColor = ThemeManager.SelectionText;
            grid.AlternatingRowsDefaultCellStyle.BackColor = ThemeManager.RowAlt;
            return grid;
        }

        private async void ScanButton_Click(object sender, EventArgs e)
        {
            if (isScanning) return;
            await Task.Run(() => PerformScan());
        }

        private void PerformScan()
        {
            if (isScanning) return;
            isScanning = true;
            isScanCompleted = false;

            UpdateUI(() =>
            {
                scanButton.Enabled = false;
                scanButton.Text = "Scanning...";
                fixButton.Enabled = false;
                foundThreats.Clear();
                grid.Rows.Clear();
                logBox.Clear();
                scanProgress.Visible = true;
                AppendLog("▶ Scan started", ThemeManager.Success);
            });

            try
            {
                var batch = new List<Threat>();
                Action flush = () =>
                {
                    if (batch.Count == 0) return;
                    var snapshot = new List<Threat>(batch);
                    batch.Clear();
                    UpdateUI(() =>
                    {
                        foreach (var t in snapshot)
                        {
                            int idx = grid.Rows.Add(t.Name, t.Description, t.Risk);
                            if (t.Risk == "Критический" || t.Risk == "Critical")
                                grid.Rows[idx].Cells[2].Style.ForeColor = ThemeManager.Danger;
                            else if (t.Risk == "Высокий" || t.Risk == "High")
                                grid.Rows[idx].Cells[2].Style.ForeColor = ThemeManager.Warning;
                        }
                    });
                };

                ScanRegistryRestrictions(batch, flush);
                ScanScancodeMap(batch, flush);
                ScanDebuggers(batch, flush);
                ScanDisallowRun(batch, flush);
                ScanHostsFile(batch, flush);
                ScanAppInitDlls(batch, flush);
                ScanWinlogonHijack(batch, flush);
                ScanServicesCritical(batch, flush);
                ScanBootExecute(batch, flush);
                ScanWmiSubscriptions(batch, flush);
                flush();
            }
            catch (Exception ex) { BbLog.Error("[PerformScan]", ex); }

            UpdateUI(() =>
            {
                AppendLog(Loc.T("scan.done"), ThemeManager.Success);
                scanButton.Text = Loc.T("scan.start");
                scanButton.Enabled = true;
                fixButton.Enabled = foundThreats.Count > 0;
                scanProgress.Visible = false;
                isScanning = false;
                isScanCompleted = true;
            });
        }

        // ===== Проверки =====
        private void ScanRegistryRestrictions(List<Threat> batch, Action flush)
        {
            var checks = new (string name, string desc, string risk, string hive, string path, string val, object expected)[]
            {
                ("DisableTaskMgr", "Block Task Manager", "High", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr", 1),
                ("DisableRegistryTools", "Block regedit", "High", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools", 1),
                ("DisableCMD", "Block CMD", "High", "HKCU",
                    @"Software\Policies\Microsoft\Windows\System", "DisableCMD", 2),
                ("NoControlPanel", "Hide Control Panel", "Medium", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoControlPanel", 1),
                ("NoRun", "Block Run dialog", "Medium", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRun", 1),
                ("NoWinKeys", "Disable Win hotkeys", "Medium", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWinKeys", 1),
                ("DisableLockWorkstation", "Block Win+L", "Medium", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation", 1),
                ("DisableChangePassword", "Block password change", "Medium", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableChangePassword", 1),
                ("HKLM:DisableTaskMgr", "Task Mgr (HKLM)", "High", "HKLM",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr", 1),
                ("HKLM:DisableRegistryTools", "Regedit (HKLM)", "High", "HKLM",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools", 1),
                ("HKLM:DisableCMD", "CMD (HKLM)", "High", "HKLM",
                    @"SOFTWARE\Policies\Microsoft\Windows\System", "DisableCMD", 2),
            };

            foreach (var check in checks)
            {
                bool found = CheckRegistryValue(check.hive, check.path, check.val, check.expected);
                if (found)
                {
                    var t = new Threat
                    {
                        Name = check.name,
                        Description = check.desc,
                        Risk = check.risk,
                        Hive = check.hive,
                        Path = check.path,
                        ValueName = check.val,
                        RepairMethod = "DeleteValue"
                    };
                    foundThreats.Add(t);
                    batch.Add(t);
                    if (batch.Count >= 20) flush();
                    UpdateUI(() => AppendLog($"  [!] {check.name}", ThemeManager.Warning));
                }
                else UpdateUI(() => AppendLog($"  [✓] {check.name}", ThemeManager.Muted));
            }
        }

        private void ScanScancodeMap(List<Threat> batch, Action flush)
        {
            string path = @"SYSTEM\CurrentControlSet\Control\Keyboard Layout";
            bool found = CheckRegistryValue("HKLM", path, "ScancodeMap", null);
            if (found)
            {
                var t = new Threat
                {
                    Name = "ScancodeMap",
                    Description = "Keyboard remap",
                    Risk = "Critical",
                    Hive = "HKLM",
                    Path = path,
                    ValueName = "ScancodeMap",
                    RepairMethod = "DeleteValue"
                };
                foundThreats.Add(t);
                batch.Add(t);
                if (batch.Count >= 20) flush();
                UpdateUI(() => AppendLog("  [!] ScancodeMap", ThemeManager.Danger));
            }
            else UpdateUI(() => AppendLog("  [✓] ScancodeMap", ThemeManager.Muted));
        }

        private void ScanDebuggers(List<Threat> batch, Action flush)
        {
            string basePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
            try
            {
                using (var key = OpenHiveKey("HKLM", basePath))
                {
                    if (key == null) return;
                    int found = 0;
                    foreach (string subName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subName))
                        {
                            if (subKey != null && subKey.GetValue("Debugger") != null)
                            {
                                found++;
                                var t = new Threat
                                {
                                    Name = $"Debugger: {subName}",
                                    Description = "IFEO hijack",
                                    Risk = "Critical",
                                    Hive = "HKLM",
                                    Path = basePath + "\\" + subName,
                                    ValueName = "Debugger",
                                    RepairMethod = "DeleteValue"
                                };
                                foundThreats.Add(t);
                                batch.Add(t);
                                if (batch.Count >= 20) flush();
                                UpdateUI(() => AppendLog($"  [!] Debugger {subName}", ThemeManager.Danger));
                            }
                        }
                    }
                    if (found == 0) UpdateUI(() => AppendLog("  [✓] IFEO", ThemeManager.Muted));
                }
            }
            catch { }
        }

        private void ScanDisallowRun(List<Threat> batch, Action flush)
        {
            string path = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
            bool found = CheckRegistryValue("HKCU", path, "DisallowRun", 1);
            if (found)
            {
                var t = new Threat
                {
                    Name = "DisallowRun",
                    Description = "Block list",
                    Risk = "High",
                    Hive = "HKCU",
                    Path = path,
                    ValueName = "DisallowRun",
                    RepairMethod = "DeleteValue"
                };
                foundThreats.Add(t);
                batch.Add(t);
                if (batch.Count >= 20) flush();
                UpdateUI(() => AppendLog("  [!] DisallowRun", ThemeManager.Warning));
            }
            else UpdateUI(() => AppendLog("  [✓] DisallowRun", ThemeManager.Muted));
        }

        private void ScanHostsFile(List<Threat> batch, Action flush)
        {
            string sysDrive = RegistryHelper.GetSystemDrive();
            string hostsPath = Path.Combine(sysDrive, @"Windows\System32\drivers\etc\hosts");
            try
            {
                if (!File.Exists(hostsPath))
                {
                    UpdateUI(() => AppendLog("  [✓] hosts", ThemeManager.Muted));
                    return;
                }

                var lines = File.ReadAllLines(hostsPath, Encoding.UTF8);
                var suspicious = new List<string>();
                string[] domains = { "google.com", "youtube.com", "facebook.com", "vk.com", "ok.ru", "mail.ru", "yandex.ru", "github.com" };

                foreach (var line in lines)
                {
                    string t = line.Trim();
                    if (t.StartsWith("#") || string.IsNullOrEmpty(t)) continue;
                    if (t.StartsWith("127.0.0.1") || t.StartsWith("0.0.0.0"))
                        foreach (var d in domains)
                            if (t.Contains(d)) suspicious.Add(t);
                }

                if (suspicious.Count > 0)
                {
                    var t = new Threat
                    {
                        Name = "HostsFile",
                        Description = $"Phishing: {string.Join("; ", suspicious)}",
                        Risk = "Critical",
                        RepairMethod = "FixHosts"
                    };
                    foundThreats.Add(t);
                    batch.Add(t);
                    if (batch.Count >= 20) flush();
                    UpdateUI(() => AppendLog("  [!] hosts phishing", ThemeManager.Danger));
                }
                else UpdateUI(() => AppendLog("  [✓] HostsFile", ThemeManager.Muted));
            }
            catch { }
        }

        private void ScanAppInitDlls(List<Threat> batch, Action flush)
        {
            const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows";
            try
            {
                using (var key = OpenHiveKey("HKLM", path))
                {
                    if (key == null) { UpdateUI(() => AppendLog("  [✓] AppInit", ThemeManager.Muted)); return; }

                    string dlls = key.GetValue("AppInit_DLLs")?.ToString() ?? "";
                    int load = 0;
                    try { load = Convert.ToInt32(key.GetValue("LoadAppInit_DLLs") ?? 0); } catch { }

                    if (load != 0 && !string.IsNullOrWhiteSpace(dlls))
                    {
                        var t = new Threat
                        {
                            Name = "AppInit_DLLs",
                            Description = $"Inject: {dlls}",
                            Risk = "Critical",
                            Hive = "HKLM",
                            Path = path,
                            ValueName = "AppInit_DLLs",
                            RepairMethod = "DisableAppInit"
                        };
                        foundThreats.Add(t);
                        batch.Add(t);
                        if (batch.Count >= 20) flush();
                        UpdateUI(() => AppendLog($"  [!] AppInit_DLLs: {dlls}", ThemeManager.Danger));
                    }
                    else UpdateUI(() => AppendLog("  [✓] AppInit_DLLs", ThemeManager.Muted));
                }
            }
            catch { }
        }

        private void ScanWinlogonHijack(List<Threat> batch, Action flush)
        {
            const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            try
            {
                using (var key = OpenHiveKey("HKLM", path))
                {
                    if (key == null) { UpdateUI(() => AppendLog("  [✓] Winlogon", ThemeManager.Muted)); return; }

                    string shell = key.GetValue("Shell")?.ToString() ?? "explorer.exe";
                    string userinit = key.GetValue("Userinit")?.ToString() ?? "";

                    bool shellNormal = shell.Trim().Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
                    bool userinitNormal = string.IsNullOrEmpty(userinit) ||
                        userinit.ToLowerInvariant().Contains(@"c:\windows\system32\userinit.exe") ||
                        userinit.ToLowerInvariant().Contains(@"%systemroot%\system32\userinit.exe");

                    if (!shellNormal)
                    {
                        var t = new Threat { Name = "Winlogon:Shell", Description = $"Shell: {shell}", Risk = "Critical", Hive = "HKLM", Path = path, ValueName = "Shell", RepairMethod = "RestoreWinlogonShell" };
                        foundThreats.Add(t); batch.Add(t); if (batch.Count >= 20) flush();
                        UpdateUI(() => AppendLog($"  [!] Shell: {shell}", ThemeManager.Danger));
                    }
                    else UpdateUI(() => AppendLog("  [✓] Winlogon Shell", ThemeManager.Muted));

                    if (!userinitNormal)
                    {
                        var t = new Threat { Name = "Winlogon:Userinit", Description = $"Userinit: {userinit}", Risk = "Critical", Hive = "HKLM", Path = path, ValueName = "Userinit", RepairMethod = "RestoreWinlogonUserinit" };
                        foundThreats.Add(t); batch.Add(t); if (batch.Count >= 20) flush();
                        UpdateUI(() => AppendLog($"  [!] Userinit: {userinit}", ThemeManager.Danger));
                    }
                    else UpdateUI(() => AppendLog("  [✓] Winlogon Userinit", ThemeManager.Muted));
                }
            }
            catch { }
        }

        private void ScanServicesCritical(List<Threat> batch, Action flush)
        {
            const string path = @"SYSTEM\CurrentControlSet\Services";
            try
            {
                using (var key = OpenHiveKey("HKLM", path))
                {
                    if (key == null) return;
                    int found = 0;

                    foreach (var svc in key.GetSubKeyNames())
                    {
                        using (var sub = key.OpenSubKey(svc))
                        {
                            if (sub == null) continue;
                            int start = -1;
                            try { start = Convert.ToInt32(sub.GetValue("Start") ?? -1); } catch { }
                            if (start != 0 && start != 1) continue;

                            string image = sub.GetValue("ImagePath")?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(image)) continue;

                            string lower = image.ToLowerInvariant();
                            bool sus =
                                (lower.Contains(@"\temp\") || lower.Contains(@"\appdata\") ||
                                 lower.Contains(@"\users\public\") || lower.Contains(@"\programdata\")) &&
                                !lower.Contains(@"\windows\");

                            if (sus)
                            {
                                found++;
                                var t = new Threat { Name = $"Service: {svc}", Description = $"Start={start} → {image}", Risk = start == 0 ? "Critical" : "High", Hive = "HKLM", Path = path + "\\" + svc, ValueName = "ImagePath", RepairMethod = "DeleteValue" };
                                foundThreats.Add(t); batch.Add(t); if (batch.Count >= 20) flush();
                                UpdateUI(() => AppendLog($"  [!] Service {svc}", ThemeManager.Danger));
                            }
                        }
                    }
                    if (found == 0) UpdateUI(() => AppendLog("  [✓] Services", ThemeManager.Muted));
                }
            }
            catch { }
        }

        private void ScanBootExecute(List<Threat> batch, Action flush)
        {
            const string path = @"SYSTEM\CurrentControlSet\Control\Session Manager";
            try
            {
                using (var key = OpenHiveKey("HKLM", path))
                {
                    if (key == null) return;
                    object val = key.GetValue("BootExecute");
                    string[] arr = val as string[] ?? (val is string s ? new[] { s } : null);
                    if (arr == null || arr.Length == 0)
                    {
                        UpdateUI(() => AppendLog("  [✓] BootExecute", ThemeManager.Muted));
                        return;
                    }

                    bool bad = false;
                    foreach (var entry in arr)
                    {
                        string norm = (entry ?? "").Trim().ToLowerInvariant();
                        bool ok = norm == "autocheck autochk *" || norm.StartsWith("autocheck autochk");
                        if (!ok) bad = true;
                    }

                    if (bad)
                    {
                        var t = new Threat { Name = "BootExecute", Description = string.Join(" | ", arr), Risk = "Critical", Hive = "HKLM", Path = path, ValueName = "BootExecute", RepairMethod = "ReportOnly" };
                        foundThreats.Add(t); batch.Add(t); if (batch.Count >= 20) flush();
                        UpdateUI(() => AppendLog("  [!] BootExecute", ThemeManager.Danger));
                    }
                    else UpdateUI(() => AppendLog("  [✓] BootExecute", ThemeManager.Muted));
                }
            }
            catch { }
        }

        private void ScanWmiSubscriptions(List<Threat> batch, Action flush)
        {
            try
            {
                var subs = WmiSubscriptionScanner.Scan();
                if (subs == null || subs.Count == 0)
                {
                    UpdateUI(() => AppendLog("  [✓] WMI", ThemeManager.Muted));
                    return;
                }

                int found = 0;
                foreach (var ws in subs)
                {
                    if (!ws.Suspicious) continue;
                    found++;
                    var t = new Threat { Name = "WMI Subscription", Description = $"{ws.BindingName}: {ws.SuspicionReason}", Risk = "Critical", RepairMethod = "ReportOnly" };
                    foundThreats.Add(t); batch.Add(t); if (batch.Count >= 20) flush();
                    UpdateUI(() => AppendLog($"  [!] WMI: {ws.BindingName}", ThemeManager.Danger));
                }
                if (found == 0) UpdateUI(() => AppendLog("  [✓] WMI Subscriptions", ThemeManager.Muted));
            }
            catch { }
        }

        private void FixButton_Click(object sender, EventArgs e)
        {
            if (foundThreats.Count == 0) return;

            if (MessageBox.Show($"Fix {foundThreats.Count}?", Loc.T("warn.confirm"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;

            int fixedCount = 0;
            foreach (var threat in foundThreats)
            {
                if (threat.RepairMethod == "DeleteValue")
                {
                    if (DeleteRegistryValue(threat.Hive, threat.Path, threat.ValueName)) fixedCount++;
                }
                else if (threat.RepairMethod == "FixHosts")
                {
                    if (FixHostsFile()) fixedCount++;
                }
                else if (threat.RepairMethod == "DisableAppInit")
                {
                    if (DisableAppInit()) fixedCount++;
                }
                else if (threat.RepairMethod == "RestoreWinlogonShell")
                {
                    if (RestoreWinlogonValue("Shell", "explorer.exe")) fixedCount++;
                }
                else if (threat.RepairMethod == "RestoreWinlogonUserinit")
                {
                    if (RestoreWinlogonValue("Userinit", @"C:\Windows\system32\userinit.exe,")) fixedCount++;
                }
            }

            UpdateUI(() =>
            {
                AppendLog($"Fixed: {fixedCount}/{foundThreats.Count}", ThemeManager.Success);
                foundThreats.Clear();
                grid.Rows.Clear();
                fixButton.Enabled = false;
            });
        }

        private RegistryKey OpenHiveKey(string hive, string path, bool writable = false)
        {
            try
            {
                if (!RegistryHelper.IsWinReEnvironment())
                {
                    var baseKey = hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                    return baseKey.OpenSubKey(path, writable);
                }

                if (hive == "HKLM")
                {
                    if (path.StartsWith("SYSTEM\\", StringComparison.OrdinalIgnoreCase))
                    {
                        RegistryHelper.LoadOfflineHives();
                        return Registry.LocalMachine.OpenSubKey("BB_Offline_SYSTEM\\" + path.Substring(7), writable);
                    }
                    if (path.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase))
                    {
                        RegistryHelper.LoadOfflineHives();
                        return Registry.LocalMachine.OpenSubKey("BB_Offline_SOFTWARE\\" + path.Substring(9), writable);
                    }
                    return Registry.LocalMachine.OpenSubKey(path, writable);
                }

                RegistryHelper.LoadHkcu();
                return Registry.Users.OpenSubKey("BB_Offline_HKCU\\" + path, writable);
            }
            catch (Exception ex) { BbLog.Error("[OpenHiveKey]", ex); return null; }
        }

        private bool CheckRegistryValue(string hive, string path, string valueName, object expected)
        {
            try
            {
                using (var key = OpenHiveKey(hive, path))
                {
                    if (key == null) return false;
                    object val = key.GetValue(valueName);
                    if (val == null) return false;
                    if (expected == null) return true;
                    if (val is int iVal && expected is int iExp) return iVal == iExp;
                    return false;
                }
            }
            catch { return false; }
        }

        private bool DeleteRegistryValue(string hive, string path, string valueName)
        {
            try
            {
                using (var key = OpenHiveKey(hive, path, true))
                {
                    if (key != null && key.GetValue(valueName) != null)
                    {
                        key.DeleteValue(valueName);
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        private bool DisableAppInit()
        {
            try
            {
                using (var key = OpenHiveKey("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", true))
                {
                    if (key == null) return false;
                    key.SetValue("AppInit_DLLs", "");
                    key.SetValue("LoadAppInit_DLLs", 0, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch { return false; }
        }

        private bool RestoreWinlogonValue(string name, string value)
        {
            try
            {
                using (var key = OpenHiveKey("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true))
                {
                    if (key == null) return false;
                    key.SetValue(name, value);
                    return true;
                }
            }
            catch { return false; }
        }

        private bool FixHostsFile()
        {
            try
            {
                string sysDrive = RegistryHelper.GetSystemDrive();
                string hostsPath = Path.Combine(sysDrive, @"Windows\System32\drivers\etc\hosts");
                if (!File.Exists(hostsPath)) return false;

                var lines = File.ReadAllLines(hostsPath, Encoding.UTF8);
                var clean = new List<string>();
                string[] domains = { "google.com", "youtube.com", "facebook.com", "vk.com", "ok.ru", "mail.ru", "yandex.ru", "github.com" };

                foreach (var line in lines)
                {
                    string t = line.Trim();
                    if (t.StartsWith("#") || string.IsNullOrEmpty(t)) { clean.Add(line); continue; }
                    bool bad = false;
                    if (t.StartsWith("127.0.0.1") || t.StartsWith("0.0.0.0"))
                        foreach (var d in domains)
                            if (t.Contains(d)) { bad = true; break; }
                    if (!bad) clean.Add(line);
                }

                File.WriteAllLines(hostsPath, clean, Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }

        private void UpdateUI(Action action)
        {
            if (this.InvokeRequired) this.Invoke(action);
            else action();
        }

        private void AppendLog(string text, Color? color = null)
        {
            if (logBox.InvokeRequired)
            {
                logBox.Invoke(new Action(() => AppendLog(text, color)));
                return;
            }
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionLength = 0;
            logBox.SelectionColor = color ?? ThemeManager.Foreground;
            logBox.AppendText(text + Environment.NewLine);
            logBox.ScrollToCaret();
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
}