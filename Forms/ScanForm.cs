using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BunnyBlack.Forms
{
    public partial class ScanForm : UserControl
    {
        private DataGridView grid;
        private RichTextBox logBox;
        private CheckBox autoFixCheck;
        private Button scanButton;
        private Button fixButton;
        private bool isScanning = false;
        private bool isScanCompleted = false;

        // Публичное свойство для проверки из MainForm
        public bool IsScanCompleted => isScanCompleted;

        // Вспомогательный класс для хранения найденной угрозы
        private class Threat
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Risk { get; set; }
            public string Hive { get; set; }
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
            if (!isScanCompleted && !isScanning)
            {
                // Запускаем сканирование в фоновом потоке
                Task.Run(() => PerformScan());
            }
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(13, 13, 13);

            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(20),
                BackColor = Color.FromArgb(13, 13, 13)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
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

            grid = CreateDarkGrid();
            grid.Columns.Clear();
            grid.Columns.Add("Name", "Угроза");
            grid.Columns.Add("Desc", "Описание");
            grid.Columns.Add("Risk", "Риск");
            grid.Columns[0].Width = 200;
            grid.Columns[1].Width = 400;
            grid.Columns[2].Width = 100;
            grid.AllowUserToAddRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            Panel gridPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 13, 13) };
            gridPanel.Controls.Add(grid);
            mainLayout.Controls.Add(gridPanel, 0, 2);

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
            mainLayout.Controls.Add(logPanel, 0, 3);

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
            mainLayout.Controls.Add(buttonPanel, 0, 4);

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

        // ============================================================
        // ГЛАВНОЕ СКАНИРОВАНИЕ (РЕАЛЬНОЕ)
        // ============================================================

        private async void ScanButton_Click(object sender, EventArgs e)
        {
            if (isScanning) return;
            await Task.Run(() => PerformScan());
        }

        // Вынесено в отдельный метод для вызова из MainForm без блокировки
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
                AppendLog("▶ Запуск глубокого сканирования системы...", Color.Cyan);
                AppendLog("═══════════════════════════════════════════");
            });

            // Блок сканирования 1: Реестр (Ограничения)
            ScanRegistryRestrictions();

            // Блок сканирования 2: ScancodeMap (Клавиатура)
            ScanScancodeMap();

            // Блок сканирования 3: Debuggers (Подмена антивирусов)
            ScanDebuggers();

            // Блок сканирования 4: DisallowRun (Чёрные списки)
            ScanDisallowRun();

            // Блок сканирования 5: Hosts File (Фишинг)
            ScanHostsFile();

            UpdateUI(() =>
            {
                AppendLog("═══════════════════════════════════════════");
                AppendLog($"✅ Сканирование завершено. Найдено угроз: {foundThreats.Count}", Color.LightGreen);

                scanButton.Text = "Начать сканирование";
                scanButton.Enabled = true;
                fixButton.Enabled = foundThreats.Count > 0;
                isScanning = false;
                isScanCompleted = true;
            });
        }

        // ============================================================
        // РЕАЛЬНЫЕ МЕТОДЫ ПРОВЕРКИ
        // ============================================================

        private void ScanRegistryRestrictions()
        {
            var checks = new (string name, string desc, string risk, string hive, string path, string val, object expected)[]
            {
                ("DisableTaskMgr", "Блокировка диспетчера задач", "Высокий", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr", 1),
                ("DisableRegistryTools", "Блокировка редактора реестра", "Высокий", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools", 1),
                ("DisableCMD", "Блокировка командной строки (CMD)", "Высокий", "HKCU", @"Software\Policies\Microsoft\Windows\System", "DisableCMD", 2),
                ("NoControlPanel", "Скрытие Панели управления", "Средний", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoControlPanel", 1),
                ("NoRun", "Блокировка меню 'Выполнить'", "Средний", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRun", 1),
                ("NoWinKeys", "Отключение горячих клавиш Win", "Средний", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWinKeys", 1),
                ("DisableLockWorkstation", "Блокировка блокировки ПК (Win+L)", "Средний", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation", 1),
                ("DisableChangePassword", "Блокировка смены пароля", "Средний", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableChangePassword", 1),
                ("NoControlPanel", "Скрытие панели управления", "Средний", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoControlPanel", 1)
            };

            foreach (var check in checks)
            {
                bool found = CheckRegistryValue(check.hive, check.path, check.val, check.expected);
                if (found)
                {
                    foundThreats.Add(new Threat
                    {
                        Name = check.name,
                        Description = check.desc,
                        Risk = check.risk,
                        Hive = check.hive,
                        Path = check.path,
                        ValueName = check.val,
                        RepairMethod = "DeleteValue"
                    });
                    UpdateUI(() => AppendLog($"  [!] Обнаружено: {check.name} ({check.risk})", Color.OrangeRed));
                }
                else
                {
                    UpdateUI(() => AppendLog($"  [✓] Безопасно: {check.name}", Color.Gray));
                }
            }
        }

        private void ScanScancodeMap()
        {
            string path = @"SYSTEM\CurrentControlSet\Control\Keyboard Layout";
            bool found = CheckRegistryValue("HKLM", path, "ScancodeMap", null);
            if (found)
            {
                foundThreats.Add(new Threat
                {
                    Name = "ScancodeMap",
                    Description = "Переназначение или блокировка клавиш клавиатуры",
                    Risk = "Критический",
                    Hive = "HKLM",
                    Path = path,
                    ValueName = "ScancodeMap",
                    RepairMethod = "DeleteValue"
                });
                UpdateUI(() => AppendLog($"  [!] Обнаружено: ScancodeMap (Критический) — клавиатура заблокирована!", Color.Red));
            }
            else
            {
                UpdateUI(() => AppendLog($"  [✓] Безопасно: ScancodeMap", Color.Gray));
            }
        }

        private void ScanDebuggers()
        {
            string basePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(basePath))
                {
                    if (key == null) return;

                    int foundCount = 0;
                    foreach (string subName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subName))
                        {
                            if (subKey != null && subKey.GetValue("Debugger") != null)
                            {
                                foundCount++;
                                foundThreats.Add(new Threat
                                {
                                    Name = $"Debugger: {subName}",
                                    Description = "Подмена запуска через отладчик (блокировка антивирусов)",
                                    Risk = "Критический",
                                    Hive = "HKLM",
                                    Path = basePath + "\\" + subName,
                                    ValueName = "Debugger",
                                    RepairMethod = "DeleteValue"
                                });
                                UpdateUI(() => AppendLog($"  [!] Обнаружено: Debugger в '{subName}' (Критический)", Color.Red));
                            }
                        }
                    }

                    if (foundCount == 0)
                        UpdateUI(() => AppendLog($"  [✓] Безопасно: Debuggers", Color.Gray));
                }
            }
            catch { UpdateUI(() => AppendLog($"  [!] Ошибка доступа к Debuggers", Color.Red)); }
        }

        private void ScanDisallowRun()
        {
            string path = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
            bool found = CheckRegistryValue("HKCU", path, "DisallowRun", 1);
            if (found)
            {
                foundThreats.Add(new Threat
                {
                    Name = "DisallowRun",
                    Description = "Чёрный список запрещённых к запуску программ",
                    Risk = "Высокий",
                    Hive = "HKCU",
                    Path = path,
                    ValueName = "DisallowRun",
                    RepairMethod = "DeleteValue"
                });
                UpdateUI(() => AppendLog($"  [!] Обнаружено: DisallowRun (Высокий)", Color.OrangeRed));
            }
            else
            {
                UpdateUI(() => AppendLog($"  [✓] Безопасно: DisallowRun", Color.Gray));
            }
        }

        private void ScanHostsFile()
        {
            string hostsPath = @"C:\Windows\System32\drivers\etc\hosts";
            try
            {
                if (!File.Exists(hostsPath))
                {
                    UpdateUI(() => AppendLog($"  [✓] Файл hosts не найден", Color.Gray));
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
                    {
                        foreach (string domain in protectedDomains)
                        {
                            if (trimmed.Contains(domain))
                            {
                                suspiciousEntries.Add(trimmed);
                            }
                        }
                    }
                }

                if (suspiciousEntries.Count > 0)
                {
                    foundThreats.Add(new Threat
                    {
                        Name = "HostsFile",
                        Description = $"Фишинговые перенаправления: {string.Join("; ", suspiciousEntries)}",
                        Risk = "Критический",
                        RepairMethod = "FixHosts"
                    });
                    UpdateUI(() => AppendLog($"  [!] Обнаружено: Фишинг в hosts (Критический)", Color.Red));
                    foreach (var entry in suspiciousEntries)
                        UpdateUI(() => AppendLog($"    → {entry}", Color.Red));
                }
                else
                {
                    UpdateUI(() => AppendLog($"  [✓] Безопасно: HostsFile", Color.Gray));
                }
            }
            catch (Exception ex)
            {
                UpdateUI(() => AppendLog($"  [!] Ошибка чтения hosts: {ex.Message}", Color.Red));
            }
        }

        // ============================================================
        // МЕТОДЫ ИСПРАВЛЕНИЯ
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
                        UpdateUI(() => AppendLog($"  [+] Исправлено: HostsFile", Color.LightGreen));
                    }
                    else
                    {
                        UpdateUI(() => AppendLog($"  [-] Ошибка исправления: HostsFile", Color.Red));
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
        // ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ
        // ============================================================

        private bool CheckRegistryValue(string hive, string path, string valueName, object expectedValue)
        {
            try
            {
                RegistryKey baseKey = hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                using (var key = baseKey.OpenSubKey(path))
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
                RegistryKey baseKey = hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                using (var key = baseKey.OpenSubKey(path, true))
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

        private bool FixHostsFile()
        {
            try
            {
                string hostsPath = @"C:\Windows\System32\drivers\etc\hosts";
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
                    {
                        foreach (string domain in protectedDomains)
                        {
                            if (trimmed.Contains(domain))
                            {
                                malicious = true;
                                break;
                            }
                        }
                    }

                    if (!malicious) cleanLines.Add(line);
                }

                File.WriteAllLines(hostsPath, cleanLines, Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }

        // Утилита для безопасного обновления UI из фонового потока
        private void UpdateUI(Action action)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(action);
            }
            else
            {
                action();
            }
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