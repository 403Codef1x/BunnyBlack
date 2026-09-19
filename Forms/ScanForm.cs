// language: C#, file: Forms/ScanForm.cs
// Полная замена.
// ФИКС: в WinRE проверка/удаление политик идёт через оффлайн-кусты (HKLM + HKCU).
// - HKCU в WinRE читаем из BunnyBlack_Offline_HKCU (NTUSER.DAT), fallback на SOFTWARE.
// - HKLM в WinRE читаем из BunnyBlack_Offline_SOFTWARE.
// - Дублируем проверки в machine-wide Policies (HKLM), которые выставляются через оффлайн.
// - п.2  — handle guard в BeginAutoScan.
// - п.6  — батч-обновление грида.
// - п.16 — ProgressBar.
// - п.20 — hosts через system drive.
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private bool isScanning = false;
        private bool isScanCompleted = false;

        public bool IsScanCompleted => isScanCompleted;

        private class Threat
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Risk { get; set; }
            public string Hive { get; set; }         // "HKLM" | "HKCU"
            public string Path { get; set; }
            public string ValueName { get; set; }
            public object ValueData { get; set; }
            public string RepairMethod { get; set; }
        }

        private List<Threat> foundThreats = new List<Threat>();

        public ScanForm(bool winRE)
        {
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
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
            this.BackColor = Color.FromArgb(13, 13, 13);

            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(20),
                BackColor = Color.FromArgb(13, 13, 13)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));

            Label title = new Label
            {
                Text = "Сканирование системы",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            mainLayout.Controls.Add(title, 0, 0);

            autoFixCheck = new CheckBox
            {
                Text = "Автоматически исправлять найденные нарушения",
                ForeColor = Color.FromArgb(216, 216, 216),
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

            Panel gridPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 13, 13) };
            gridPanel.Controls.Add(grid);
            mainLayout.Controls.Add(gridPanel, 0, 3);

            logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(200, 200, 200),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10),
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            Panel logPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Padding = new Padding(0, 5, 0, 0)
            };
            logPanel.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, logPanel.ClientRectangle,
                    Color.FromArgb(60, 60, 60), 1, ButtonBorderStyle.Solid,
                    Color.FromArgb(60, 60, 60), 1, ButtonBorderStyle.Solid,
                    Color.FromArgb(60, 60, 60), 1, ButtonBorderStyle.Solid,
                    Color.FromArgb(60, 60, 60), 1, ButtonBorderStyle.Solid);
            };
            logPanel.Controls.Add(logBox);
            mainLayout.Controls.Add(logPanel, 0, 4);

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 10, 0, 0),
                AutoSize = true
            };

            scanButton = new Button
            {
                Text = "Начать сканирование",
                Width = 180,
                Height = 35,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(240, 240, 240),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) }
            };
            scanButton.Click += ScanButton_Click;

            fixButton = new Button
            {
                Text = "Исправить всё",
                Width = 150,
                Height = 35,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(25, 60, 40),
                ForeColor = Color.FromArgb(240, 240, 240),
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
            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BackgroundColor = Color.FromArgb(13, 13, 13),
                GridColor = Color.FromArgb(45, 45, 45),
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

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(25, 25, 25);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 200, 200);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);

            grid.DefaultCellStyle.BackColor = Color.FromArgb(18, 18, 18);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 50, 60);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(255, 255, 255);

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(14, 14, 14);

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
                scanButton.Text = "Сканирование...";
                fixButton.Enabled = false;
                foundThreats.Clear();
                grid.Rows.Clear();
                logBox.Clear();
                if (scanProgress != null) scanProgress.Visible = true;
                AppendLog("▶ Запуск глубокого сканирования системы...", Color.Cyan);
                AppendLog("═══════════════════════════════════════════");

                // диагностика
                if (RegistryHelper.IsWinReEnvironment())
                {
                    AppendLog($"  [i] WinRE: оффлайн-кусты", Color.LightBlue);
                    string status;
                    RegistryHelper.LoadOfflineHives(out status);
                    AppendLog($"  [i] {status}", Color.LightBlue);
                }
            });

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
                        if (t.Risk == "Критический") grid.Rows[idx].Cells[2].Style.ForeColor = Color.Red;
                        else if (t.Risk == "Высокий") grid.Rows[idx].Cells[2].Style.ForeColor = Color.OrangeRed;
                    }
                });
            };

            ScanRegistryRestrictions(batch, flush);
            ScanScancodeMap(batch, flush);
            ScanDebuggers(batch, flush);
            ScanDisallowRun(batch, flush);
            ScanHostsFile(batch, flush);
            flush();

            UpdateUI(() =>
            {
                AppendLog("═══════════════════════════════════════════");
                AppendLog($"✅ Сканирование завершено. Найдено угроз: {foundThreats.Count}", Color.LightGreen);

                scanButton.Text = "Начать сканирование";
                scanButton.Enabled = true;
                fixButton.Enabled = foundThreats.Count > 0;
                if (scanProgress != null) scanProgress.Visible = false;
                isScanning = false;
                isScanCompleted = true;
            });
        }

        // ============================================================
        // ФИКС — проверка политик: теперь работает и в WinRE,
        // через оффлайн-кусты. Каждая политика проверяется во всех
        // возможных местах: HKLM Policy + HKCU Policy + оффлайн HKCU.
        // ============================================================
        private void ScanRegistryRestrictions(List<Threat> batch, Action flush)
        {
            // (name, desc, risk, hive, path, val, expected)
            var checks = new (string name, string desc, string risk, string hive, string path, string val, object expected)[]
            {
                // ============================================================
                // User-policy — HKCU\...\Policies
                // ============================================================
                ("DisableTaskMgr", "Блокировка диспетчера задач", "Высокий", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr", 1),
                ("DisableRegistryTools", "Блокировка редактора реестра", "Высокий", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools", 1),
                ("DisableCMD", "Блокировка командной строки (CMD)", "Высокий", "HKCU",
                    @"Software\Policies\Microsoft\Windows\System", "DisableCMD", 2),
                ("NoControlPanel", "Скрытие Панели управления", "Средний", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoControlPanel", 1),
                ("NoRun", "Блокировка меню 'Выполнить'", "Средний", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRun", 1),
                ("NoWinKeys", "Отключение горячих клавиш Win", "Средний", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWinKeys", 1),
                ("DisableLockWorkstation", "Блокировка блокировки ПК (Win+L)", "Средний", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation", 1),
                ("DisableChangePassword", "Блокировка смены пароля", "Средний", "HKCU",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableChangePassword", 1),

                // ============================================================
                // Machine-wide policy — HKLM
                // ============================================================
                ("HKLM:DisableTaskMgr", "Блокировка диспетчера задач (HKLM)", "Высокий", "HKLM",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr", 1),
                ("HKLM:DisableRegistryTools", "Блокировка редактора реестра (HKLM)", "Высокий", "HKLM",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools", 1),
                ("HKLM:DisableCMD", "Блокировка CMD (HKLM)", "Высокий", "HKLM",
                    @"SOFTWARE\Policies\Microsoft\Windows\System", "DisableCMD", 2),
                ("HKLM:NoControlPanel", "Скрытие Панели управления (HKLM)", "Средний", "HKLM",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoControlPanel", 1),
                ("HKLM:NoRun", "Блокировка 'Выполнить' (HKLM)", "Средний", "HKLM",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRun", 1),
                ("HKLM:NoViewOnDrive", "Ограничение доступа к дискам (HKLM)", "Средний", "HKLM",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoViewOnDrive", null),
                ("HKLM:DisableLockWorkstation", "Блокировка Win+L (HKLM)", "Средний", "HKLM",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation", 1),
                ("HKLM:DisableChangePassword", "Блокировка смены пароля (HKLM)", "Средний", "HKLM",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableChangePassword", 1),
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
                    UpdateUI(() => AppendLog($"  [!] Обнаружено: {check.name} ({check.risk})", Color.OrangeRed));
                }
                else
                {
                    UpdateUI(() => AppendLog($"  [✓] Безопасно: {check.name}", Color.Gray));
                }
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
                    Description = "Переназначение или блокировка клавиш клавиатуры",
                    Risk = "Критический",
                    Hive = "HKLM",
                    Path = path,
                    ValueName = "ScancodeMap",
                    RepairMethod = "DeleteValue"
                };
                foundThreats.Add(t);
                batch.Add(t);
                if (batch.Count >= 20) flush();
                UpdateUI(() => AppendLog("  [!] Обнаружено: ScancodeMap (Критический)", Color.Red));
            }
            else
            {
                UpdateUI(() => AppendLog("  [✓] Безопасно: ScancodeMap", Color.Gray));
            }
        }

        private void ScanDebuggers(List<Threat> batch, Action flush)
        {
            string basePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
            try
            {
                using (var key = OpenHiveKey("HKLM", basePath))
                {
                    if (key == null)
                    {
                        UpdateUI(() => AppendLog("  [!] Debuggers: ключ не открылся", Color.Orange));
                        return;
                    }

                    int foundCount = 0;
                    foreach (string subName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subName))
                        {
                            if (subKey != null && subKey.GetValue("Debugger") != null)
                            {
                                foundCount++;
                                string capturedName = subName;
                                var t = new Threat
                                {
                                    Name = $"Debugger: {capturedName}",
                                    Description = "Подмена запуска через отладчик",
                                    Risk = "Критический",
                                    Hive = "HKLM",
                                    Path = basePath + "\\" + capturedName,
                                    ValueName = "Debugger",
                                    RepairMethod = "DeleteValue"
                                };
                                foundThreats.Add(t);
                                batch.Add(t);
                                if (batch.Count >= 20) flush();
                                UpdateUI(() => AppendLog($"  [!] Обнаружено: Debugger в '{capturedName}' (Критический)", Color.Red));
                            }
                        }
                    }

                    if (foundCount == 0)
                        UpdateUI(() => AppendLog("  [✓] Безопасно: Debuggers", Color.Gray));
                }
            }
            catch (Exception ex) { UpdateUI(() => AppendLog($"  [!] Ошибка Debuggers: {ex.Message}", Color.Red)); }
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
                    Description = "Чёрный список запрещённых к запуску программ",
                    Risk = "Высокий",
                    Hive = "HKCU",
                    Path = path,
                    ValueName = "DisallowRun",
                    RepairMethod = "DeleteValue"
                };
                foundThreats.Add(t);
                batch.Add(t);
                if (batch.Count >= 20) flush();
                UpdateUI(() => AppendLog("  [!] Обнаружено: DisallowRun (Высокий)", Color.OrangeRed));
            }
            else
            {
                UpdateUI(() => AppendLog("  [✓] Безопасно: DisallowRun", Color.Gray));
            }
        }

        private void ScanHostsFile(List<Threat> batch, Action flush)
        {
            string sysDrive = RegistryHelper.GetSystemDrive();
            string hostsPath = Path.Combine(sysDrive, @"Windows\System32\drivers\etc\hosts");
            try
            {
                if (!File.Exists(hostsPath))
                {
                    UpdateUI(() => AppendLog($"  [✓] Hosts не найден ({hostsPath})", Color.Gray));
                    return;
                }

                string[] lines = File.ReadAllLines(hostsPath, Encoding.UTF8);
                List<string> suspiciousEntries = new List<string>();

                string[] protectedDomains = { "google.com", "youtube.com", "facebook.com", "vk.com", "ok.ru", "mail.ru", "yandex.ru", "github.com" };

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || string.IsNullOrEmpty(trimmed)) continue;

                    if (trimmed.StartsWith("127.0.0.1") || trimmed.StartsWith("0.0.0.0"))
                        foreach (string domain in protectedDomains)
                            if (trimmed.Contains(domain))
                                suspiciousEntries.Add(trimmed);
                }

                if (suspiciousEntries.Count > 0)
                {
                    var t = new Threat
                    {
                        Name = "HostsFile",
                        Description = $"Фишинговые перенаправления: {string.Join("; ", suspiciousEntries)}",
                        Risk = "Критический",
                        RepairMethod = "FixHosts"
                    };
                    foundThreats.Add(t);
                    batch.Add(t);
                    if (batch.Count >= 20) flush();
                    UpdateUI(() =>
                    {
                        AppendLog("  [!] Обнаружено: Фишинг в hosts (Критический)", Color.Red);
                        foreach (var entry in suspiciousEntries) AppendLog($"    → {entry}", Color.Red);
                    });
                }
                else
                {
                    UpdateUI(() => AppendLog("  [✓] Безопасно: HostsFile", Color.Gray));
                }
            }
            catch (Exception ex)
            {
                UpdateUI(() => AppendLog($"  [!] Ошибка чтения hosts: {ex.Message}", Color.Red));
            }
        }

        // ============================================================
        // ИСПРАВЛЕНИЕ
        // ============================================================
        private void FixButton_Click(object sender, EventArgs e)
        {
            if (foundThreats.Count == 0) return;

            if (MessageBox.Show($"Вы уверены, что хотите исправить {foundThreats.Count} угроз?\nЭто действие необратимо.",
                "Подтверждение исправления", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;

            int fixedCount = 0;

            foreach (var threat in foundThreats)
            {
                if (threat.RepairMethod == "DeleteValue")
                {
                    if (DeleteRegistryValue(threat.Hive, threat.Path, threat.ValueName))
                    {
                        fixedCount++;
                        UpdateUI(() => AppendLog($"  [+] Исправлено: {threat.Name}", Color.LightGreen));
                    }
                    else
                    {
                        UpdateUI(() => AppendLog($"  [-] Ошибка исправления: {threat.Name}", Color.Red));
                    }
                }
                else if (threat.RepairMethod == "FixHosts")
                {
                    if (FixHostsFile())
                    {
                        fixedCount++;
                        UpdateUI(() => AppendLog("  [+] Исправлено: HostsFile", Color.LightGreen));
                    }
                    else
                    {
                        UpdateUI(() => AppendLog("  [-] Ошибка исправления: HostsFile", Color.Red));
                    }
                }
            }

            UpdateUI(() =>
            {
                AppendLog($"\n✅ Исправление завершено. Исправлено: {fixedCount} из {foundThreats.Count}", Color.LightGreen);
                foundThreats.Clear();
                grid.Rows.Clear();
                fixButton.Enabled = false;
            });
        }

        // ============================================================
        // ФИКС: OpenHiveKey — явно работает с оффлайн-кустами
        // HKLM → BunnyBlack_Offline_SOFTWARE (или _SYSTEM)
        // HKCU → BunnyBlack_Offline_HKCU, fallback на SOFTWARE
        // ============================================================
        private RegistryKey OpenHiveKey(string hive, string path, bool writable = false)
        {
            try
            {
                if (!RegistryHelper.IsWinReEnvironment())
                {
                    RegistryKey baseKey = hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                    return baseKey.OpenSubKey(path, writable);
                }

                // WinRE — грузим кусты
                RegistryHelper.LoadOfflineHives();

                string fullPath;

                if (hive == "HKLM")
                {
                    if (path.StartsWith("SYSTEM\\", StringComparison.OrdinalIgnoreCase))
                        fullPath = "BunnyBlack_Offline_SYSTEM\\" + path.Substring(7);
                    else if (path.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase))
                        fullPath = "BunnyBlack_Offline_SOFTWARE\\" + path.Substring(9);
                    else
                        fullPath = "BunnyBlack_Offline_SOFTWARE\\" + path;
                }
                else // HKCU
                {
                    // Сначала пробуем оффлайн-HKCU
                    string hkcu = "BunnyBlack_Offline_HKCU\\" + path;
                    var test = Registry.LocalMachine.OpenSubKey(hkcu, writable);
                    if (test != null) return test;

                    // Fallback: machine-wide policy из SOFTWARE
                    string sw = "BunnyBlack_Offline_SOFTWARE\\" + path;
                    var test2 = Registry.LocalMachine.OpenSubKey(sw, writable);
                    if (test2 != null) return test2;

                    return null;
                }

                Debug.WriteLine($"[OpenHiveKey] {hive}\\{path} → {fullPath}");
                var key = Registry.LocalMachine.OpenSubKey(fullPath, writable);
                if (key == null) Debug.WriteLine($"[OpenHiveKey] NULL for {fullPath}");
                return key;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpenHiveKey] {ex.Message}");
                return null;
            }
        }

        private bool CheckRegistryValue(string hive, string path, string valueName, object expectedValue)
        {
            try
            {
                using (var key = OpenHiveKey(hive, path))
                {
                    if (key == null) return false;
                    object val = key.GetValue(valueName);
                    if (val == null) return false;

                    if (expectedValue == null) return true;
                    if (val is int iVal && expectedValue is int iExp) return iVal == iExp;
                    if (val is string sVal && expectedValue is string sExp) return sVal == sExp;
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
            catch (Exception ex)
            {
                Debug.WriteLine($"[DeleteRegistryValue] {ex.Message}");
                return false;
            }
        }

        private bool FixHostsFile()
        {
            try
            {
                string sysDrive = RegistryHelper.GetSystemDrive();
                string hostsPath = Path.Combine(sysDrive, @"Windows\System32\drivers\etc\hosts");
                if (!File.Exists(hostsPath)) return false;

                string[] lines = File.ReadAllLines(hostsPath, Encoding.UTF8);
                List<string> cleanLines = new List<string>();

                string[] protectedDomains = { "google.com", "youtube.com", "facebook.com", "vk.com", "ok.ru", "mail.ru", "yandex.ru", "github.com" };

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || string.IsNullOrEmpty(trimmed))
                    {
                        cleanLines.Add(line);
                        continue;
                    }

                    bool malicious = false;
                    if (trimmed.StartsWith("127.0.0.1") || trimmed.StartsWith("0.0.0.0"))
                        foreach (string domain in protectedDomains)
                            if (trimmed.Contains(domain)) { malicious = true; break; }

                    if (!malicious) cleanLines.Add(line);
                }

                File.WriteAllLines(hostsPath, cleanLines, Encoding.UTF8);
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
            logBox.SelectionColor = color ?? Color.FromArgb(200, 200, 200);
            logBox.AppendText(text + Environment.NewLine);
            logBox.ScrollToCaret();
        }
    }
}