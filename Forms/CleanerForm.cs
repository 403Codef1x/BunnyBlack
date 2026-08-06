using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BunnyBlack.Forms
{
    public partial class CleanerForm : UserControl
    {
        private RichTextBox logBox;
        private CheckBox chkTemp;
        private CheckBox chkRecycle;
        private CheckBox chkBrowsers;
        private CheckBox chkRegistry;
        private CheckBox chkLogs;
        private CheckBox chkPrefetch;
        private CheckBox chkSystem; // Новая галочка "Системный кэш"
        private Button btnAnalyze;
        private Button btnClean;

        private bool isAnalyzing = false;

        private long tempSize = 0;
        private long recycleSize = 0;
        private long browserSize = 0;
        private long logsSize = 0;
        private long prefetchSize = 0;
        private long systemSize = 0;
        private int registryProblems = 0;

        public CleanerForm(bool winRE)
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
            AppendLog("Готов к работе. Нажмите 'Анализ'.", Color.Gray);
        }

        private void InitializeComponent()
        {
            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(20),
                BackColor = Color.FromArgb(13, 13, 13)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));

            logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10),
                ReadOnly = true,
                WordWrap = true
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
            mainLayout.Controls.Add(logPanel, 0, 0);

            // ============================================================
            // Область очистки (Группа 1)
            // ============================================================
            GroupBox optionsGroup = new GroupBox
            {
                Text = "Область очистки",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(240, 240, 240),
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(22, 22, 22)
            };
            FlowLayoutPanel optionsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(10, 8, 10, 8),
                BackColor = Color.Transparent
            };

            chkTemp = CreateCheckBox("Временные файлы (Temp)");
            chkRecycle = CreateCheckBox("Корзина");
            chkBrowsers = CreateCheckBox("Кэш браузеров");
            chkRegistry = CreateCheckBox("Реестр");
            chkLogs = CreateCheckBox("Логи Windows");
            chkPrefetch = CreateCheckBox("Prefetch и миниатюры");
            chkSystem = CreateCheckBox("Системный кэш (Windows/Обновления)"); // НОВОЕ

            FlowLayoutPanel row1 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, BackColor = Color.Transparent, WrapContents = true };
            row1.Controls.Add(chkTemp);
            row1.Controls.Add(chkRecycle);

            FlowLayoutPanel row2 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, BackColor = Color.Transparent, WrapContents = true };
            row2.Controls.Add(chkBrowsers);
            row2.Controls.Add(chkRegistry);

            FlowLayoutPanel row3 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, BackColor = Color.Transparent, WrapContents = true };
            row3.Controls.Add(chkLogs);
            row3.Controls.Add(chkPrefetch);

            FlowLayoutPanel row4 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, BackColor = Color.Transparent, WrapContents = true };
            row4.Controls.Add(chkSystem);

            optionsFlow.Controls.Add(row1);
            optionsFlow.Controls.Add(row2);
            optionsFlow.Controls.Add(row3);
            optionsFlow.Controls.Add(row4);
            optionsGroup.Controls.Add(optionsFlow);
            mainLayout.Controls.Add(optionsGroup, 0, 1);

            // ============================================================
            // Действия (Кнопки)
            // ============================================================
            GroupBox actionsGroup = new GroupBox
            {
                Text = "Действия",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(240, 240, 240),
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(22, 22, 22)
            };
            FlowLayoutPanel actionsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(10, 8, 10, 8),
                BackColor = Color.Transparent
            };

            btnAnalyze = CreateActionButton("Анализ", Color.FromArgb(0, 160, 160));
            btnAnalyze.Click += BtnAnalyze_Click;

            btnClean = CreateActionButton("Очистить", Color.FromArgb(0, 160, 160));
            btnClean.Click += BtnClean_Click;
            btnClean.Enabled = false;

            actionsFlow.Controls.Add(btnAnalyze);
            actionsFlow.Controls.Add(btnClean);
            actionsGroup.Controls.Add(actionsFlow);
            mainLayout.Controls.Add(actionsGroup, 0, 2);

            this.Controls.Add(mainLayout);
        }

        private CheckBox CreateCheckBox(string text)
        {
            return new CheckBox
            {
                Text = text,
                ForeColor = Color.FromArgb(216, 216, 216),
                Font = new Font("Segoe UI", 10),
                AutoSize = true,
                Checked = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 15, 0)
            };
        }

        private Button CreateActionButton(string text, Color color)
        {
            return new Button
            {
                Text = text,
                Height = 40,
                Width = 180,
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.FromArgb(240, 240, 240),
                FlatAppearance = { BorderSize = 0 },
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
        }

        private void AppendLog(string text, Color? color = null)
        {
            if (logBox.InvokeRequired)
            {
                logBox.Invoke(new Action(() => AppendLog(text, color)));
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionLength = 0;
            logBox.SelectionColor = color ?? Color.FromArgb(200, 200, 200);
            logBox.AppendText($"{timestamp} - {text}{Environment.NewLine}");
            logBox.ScrollToCaret();
        }

        private string FormatSize(long bytes)
        {
            if (bytes == 0) return "0 Б";
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        // ============================================================
        // АНАЛИЗ
        // ============================================================
        private async void BtnAnalyze_Click(object sender, EventArgs e)
        {
            if (isAnalyzing) return;

            isAnalyzing = true;
            btnAnalyze.Enabled = false;
            btnClean.Enabled = false;
            logBox.Clear();
            tempSize = recycleSize = browserSize = logsSize = prefetchSize = systemSize = 0;
            registryProblems = 0;

            AppendLog("Анализ временных файлов...", Color.Cyan);
            await Task.Run(() => AnalyzeTemp());

            AppendLog("Анализ корзины...", Color.Cyan);
            await Task.Run(() => AnalyzeRecycleBin());

            AppendLog("Анализ кэша браузеров...", Color.Cyan);
            await Task.Run(() => AnalyzeBrowsers());

            AppendLog("Анализ логов Windows...", Color.Cyan);
            await Task.Run(() => AnalyzeLogs());

            AppendLog("Анализ Prefetch и миниатюр...", Color.Cyan);
            await Task.Run(() => AnalyzePrefetch());

            AppendLog("Анализ системного кэша...", Color.Cyan);
            await Task.Run(() => AnalyzeSystemCache());

            AppendLog("Анализ реестра...", Color.Cyan);
            await Task.Run(() => AnalyzeRegistry());

            AppendLog("---", Color.Gray);
            if (tempSize > 0) AppendLog($"Временные файлы: {FormatSize(tempSize)}", Color.LightGreen);
            if (recycleSize > 0) AppendLog($"Корзина: {FormatSize(recycleSize)}", Color.LightGreen);
            if (browserSize > 0) AppendLog($"Кэш браузеров: {FormatSize(browserSize)}", Color.LightGreen);
            if (logsSize > 0) AppendLog($"Логи Windows: {FormatSize(logsSize)}", Color.LightGreen);
            if (prefetchSize > 0) AppendLog($"Prefetch/миниатюры: {FormatSize(prefetchSize)}", Color.LightGreen);
            if (systemSize > 0) AppendLog($"Системный кэш: {FormatSize(systemSize)}", Color.LightGreen);
            if (registryProblems > 0) AppendLog($"Найдено проблем в реестре: {registryProblems}", Color.Orange);

            long total = tempSize + recycleSize + browserSize + logsSize + prefetchSize + systemSize;
            AppendLog($"Можно освободить: {FormatSize(total)}", Color.LightGreen);

            btnAnalyze.Enabled = true;
            btnClean.Enabled = total > 0 || registryProblems > 0;
            isAnalyzing = false;
        }

        private void AnalyzeTemp()
        {
            string tempPath = Path.GetTempPath();
            tempSize = GetDirectorySize(tempPath);
        }

        private void AnalyzeRecycleBin()
        {
            try
            {
                string recyclePath = @"C:\$Recycle.Bin";
                if (Directory.Exists(recyclePath))
                {
                    recycleSize = GetDirectorySize(recyclePath);
                }
            }
            catch { recycleSize = 0; }
        }

        private void AnalyzeBrowsers()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] browserPaths = new[]
            {
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache\Cache_Data"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache\Cache_Data"),
                Path.Combine(localAppData, @"Opera Software\Opera Stable\Cache"),
                Path.Combine(localAppData, @"Yandex\YandexBrowser\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Mozilla\Firefox\Profiles")
            };

            browserSize = 0;
            foreach (string path in browserPaths)
            {
                if (Directory.Exists(path))
                {
                    if (path.Contains("Firefox"))
                    {
                        string[] profiles = Directory.GetDirectories(path);
                        foreach (string prof in profiles)
                        {
                            string cachePath = Path.Combine(prof, "cache2");
                            if (Directory.Exists(cachePath))
                                browserSize += GetDirectorySize(cachePath);
                        }
                    }
                    else
                    {
                        browserSize += GetDirectorySize(path);
                    }
                }
            }
        }

        private void AnalyzeLogs()
        {
            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
            string logPath = Path.Combine(systemRoot, @"System32\LogFiles");
            logsSize = GetDirectorySize(logPath);
        }

        private void AnalyzePrefetch()
        {
            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
            string prefetchPath = Path.Combine(systemRoot, "Prefetch");
            prefetchSize = GetDirectorySize(prefetchPath);

            string thumbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
            if (Directory.Exists(thumbPath))
            {
                prefetchSize += GetDirectorySize(thumbPath);
            }
        }

        private void AnalyzeSystemCache()
        {
            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
            systemSize = 0;

            // 1. Папка с обновлениями Windows (самая жирная)
            string updatePath = Path.Combine(systemRoot, @"SoftwareDistribution\Download");
            if (Directory.Exists(updatePath))
            {
                systemSize += GetDirectorySize(updatePath);
            }

            // 2. Папка с отчётами об ошибках (WER)
            string werPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER");
            if (Directory.Exists(werPath))
            {
                systemSize += GetDirectorySize(werPath);
            }

            // 3. Кэш иконок и эскизов (дополнительно)
            string iconCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"IconCache.db");
            if (File.Exists(iconCachePath))
            {
                try { systemSize += new FileInfo(iconCachePath).Length; } catch { }
            }
        }

        private void AnalyzeRegistry()
        {
            registryProblems = 0;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key != null)
                    {
                        foreach (string val in key.GetValueNames())
                        {
                            string valData = key.GetValue(val)?.ToString() ?? "";
                            if (valData.Contains("temp") || valData.Contains("TEMP"))
                            {
                                registryProblems++;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            long size = 0;
            try
            {
                var dirInfo = new DirectoryInfo(path);
                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { size += file.Length; }
                    catch { }
                }
            }
            catch { }
            return size;
        }

        // ============================================================
        // ОЧИСТКА
        // ============================================================
        private async void BtnClean_Click(object sender, EventArgs e)
        {
            if (btnClean.Enabled == false) return;

            if (MessageBox.Show("Вы уверены, что хотите удалить выбранные данные?", "Подтверждение очистки",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;

            btnAnalyze.Enabled = false;
            btnClean.Enabled = false;
            AppendLog("Начало очистки...", Color.Cyan);

            if (chkTemp.Checked)
            {
                AppendLog("Очистка временных файлов...", Color.Cyan);
                await Task.Run(CleanTemp);
            }

            if (chkRecycle.Checked)
            {
                AppendLog("Очистка корзины...", Color.Cyan);
                await Task.Run(CleanRecycleBin);
            }

            if (chkBrowsers.Checked)
            {
                AppendLog("Очистка кэша браузеров...", Color.Cyan);
                await Task.Run(CleanBrowsers);
            }

            if (chkLogs.Checked)
            {
                AppendLog("Очистка логов Windows...", Color.Cyan);
                await Task.Run(CleanLogs);
            }

            if (chkPrefetch.Checked)
            {
                AppendLog("Очистка Prefetch и миниатюр...", Color.Cyan);
                await Task.Run(CleanPrefetch);
            }

            if (chkSystem.Checked)
            {
                AppendLog("Очистка системного кэша...", Color.Cyan);
                await Task.Run(CleanSystemCache);
            }

            if (chkRegistry.Checked)
            {
                AppendLog("Очистка реестра...", Color.Cyan);
                await Task.Run(CleanRegistry);
            }

            AppendLog("Очистка завершена!", Color.LightGreen);
            btnAnalyze.Enabled = true;
            btnClean.Enabled = false;
        }

        private void CleanTemp()
        {
            string tempPath = Path.GetTempPath();
            try
            {
                foreach (var file in Directory.EnumerateFiles(tempPath))
                {
                    try { File.Delete(file); } catch { }
                }
                foreach (var dir in Directory.EnumerateDirectories(tempPath))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            catch { }
        }

        private void CleanRecycleBin()
        {
            try
            {
                Process.Start("cmd.exe", "/c rd /s /q C:\\$Recycle.Bin");
            }
            catch { }
        }

        private void CleanBrowsers()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] browserPaths = new[]
            {
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache\Cache_Data"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache\Cache_Data"),
                Path.Combine(localAppData, @"Opera Software\Opera Stable\Cache"),
                Path.Combine(localAppData, @"Yandex\YandexBrowser\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Mozilla\Firefox\Profiles")
            };

            foreach (string path in browserPaths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        if (path.Contains("Firefox"))
                        {
                            string[] profiles = Directory.GetDirectories(path);
                            foreach (string prof in profiles)
                            {
                                string cachePath = Path.Combine(prof, "cache2");
                                if (Directory.Exists(cachePath))
                                {
                                    try { Directory.Delete(cachePath, true); } catch { }
                                }
                            }
                        }
                        else
                        {
                            try { Directory.Delete(path, true); } catch { }
                        }
                    }
                }
                catch { }
            }
        }

        private void CleanLogs()
        {
            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
            string logPath = Path.Combine(systemRoot, @"System32\LogFiles");
            try
            {
                if (Directory.Exists(logPath))
                {
                    foreach (var file in Directory.EnumerateFiles(logPath))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    foreach (var dir in Directory.EnumerateDirectories(logPath))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            catch { }
        }

        private void CleanPrefetch()
        {
            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
            string prefetchPath = Path.Combine(systemRoot, "Prefetch");
            try
            {
                if (Directory.Exists(prefetchPath))
                {
                    foreach (var file in Directory.EnumerateFiles(prefetchPath))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }

            string thumbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
            try
            {
                if (Directory.Exists(thumbPath))
                {
                    foreach (var file in Directory.EnumerateFiles(thumbPath, "*.db"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        private void CleanSystemCache()
        {
            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";

            // 1. Очистка папки обновлений
            string updatePath = Path.Combine(systemRoot, @"SoftwareDistribution\Download");
            try
            {
                if (Directory.Exists(updatePath))
                {
                    foreach (var file in Directory.EnumerateFiles(updatePath))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    foreach (var dir in Directory.EnumerateDirectories(updatePath))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            catch { }

            // 2. Очистка отчётов об ошибках
            string werPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER");
            try
            {
                if (Directory.Exists(werPath))
                {
                    foreach (var file in Directory.EnumerateFiles(werPath))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }

            // 3. Удаление кэша иконок
            string iconCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"IconCache.db");
            try
            {
                if (File.Exists(iconCachePath))
                {
                    try { File.Delete(iconCachePath); } catch { }
                }
            }
            catch { }
        }

        private void CleanRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        List<string> toDelete = new List<string>();
                        foreach (string val in key.GetValueNames())
                        {
                            string valData = key.GetValue(val)?.ToString() ?? "";
                            if (valData.Contains("temp") || valData.Contains("TEMP"))
                            {
                                toDelete.Add(val);
                            }
                        }
                        foreach (string v in toDelete)
                        {
                            try { key.DeleteValue(v); } catch { }
                        }
                    }
                }
            }
            catch { }
        }
    }
}