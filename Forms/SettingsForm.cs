using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;

namespace BunnyBlack.Forms
{
    public partial class SettingsForm : UserControl
    {
        private CheckBox topCheck;
        private CheckBox trayCheck;
        private bool isWinRE;
        private Form parentForm;

        public SettingsForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);

            parentForm = this.FindForm();

            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(28, 24, 28, 20),
                BackColor = Color.FromArgb(13, 13, 13)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Строка для кнопки сайта
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = "Настройки",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(title, 0, 0);

            // Окно
            var winGroup = new GroupBox
            {
                Text = "Окно",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(240, 240, 240),
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(22, 22, 22)
            };
            var winLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10, 8, 10, 8),
                BackColor = Color.Transparent
            };

            topCheck = new CheckBox
            {
                Text = "Держать окно поверх всех окон",
                ForeColor = Color.FromArgb(216, 216, 216),
                Font = new Font("Segoe UI", 11),
                AutoSize = true,
                Checked = true,
                BackColor = Color.Transparent
            };
            topCheck.CheckedChanged += (s, e) => ApplyTopMost();
            winLayout.Controls.Add(topCheck, 0, 0);

            trayCheck = new CheckBox
            {
                Text = "Сворачивать в системный трей (возле часов)",
                ForeColor = Color.FromArgb(216, 216, 216),
                Font = new Font("Segoe UI", 11),
                AutoSize = true,
                Checked = true,
                BackColor = Color.Transparent
            };
            winLayout.Controls.Add(trayCheck, 0, 1);

            winGroup.Controls.Add(winLayout);
            layout.Controls.Add(winGroup, 0, 1);

            // Автозагрузка
            var startupGroup = new GroupBox
            {
                Text = "Автозагрузка программы",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(240, 240, 240),
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(22, 22, 22)
            };
            var startupLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(10, 8, 10, 8),
                BackColor = Color.Transparent
            };

            var addBtn = new Button
            {
                Text = "Добавить в автозагрузку",
                Height = 35,
                Width = 200,
                BackColor = Color.FromArgb(15, 34, 24),
                ForeColor = Color.FromArgb(136, 221, 170),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(51, 102, 68) },
                Font = new Font("Segoe UI", 11),
                Cursor = Cursors.Hand
            };
            addBtn.Click += (s, e) => AddToStartup();
            startupLayout.Controls.Add(addBtn);

            var remBtn = new Button
            {
                Text = "Убрать из автозагрузки",
                Height = 35,
                Width = 200,
                BackColor = Color.FromArgb(42, 16, 16),
                ForeColor = Color.FromArgb(255, 136, 136),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(102, 51, 51) },
                Font = new Font("Segoe UI", 11),
                Cursor = Cursors.Hand
            };
            remBtn.Click += (s, e) => RemoveFromStartup();
            startupLayout.Controls.Add(remBtn);

            startupGroup.Controls.Add(startupLayout);
            layout.Controls.Add(startupGroup, 0, 2);

            // ============================================================
            // КНОПКА ОТКРЫТЬ САЙТ
            // ============================================================
            var siteBtn = new Button
            {
                Text = "Открыть сайт программы",
                Height = 35,
                Width = 220,
                BackColor = Color.FromArgb(20, 30, 50), // Темно-синий стиль
                ForeColor = Color.FromArgb(150, 200, 255),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 80, 120) },
                Font = new Font("Segoe UI", 11),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            siteBtn.Click += (s, e) => OpenWebsite();
            layout.Controls.Add(siteBtn, 0, 3);

            this.Controls.Add(layout);
        }

        // ============================================================
        // ЗАГРУЗКА НАСТРОЕК ИЗ РЕЕСТРА
        // ============================================================
        private void LoadSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                {
                    if (key != null)
                    {
                        object topValue = key.GetValue("TopMost");
                        if (topValue != null)
                            topCheck.Checked = topValue.ToString() == "1";
                        else
                            topCheck.Checked = true;

                        object trayValue = key.GetValue("TrayEnabled");
                        if (trayValue != null)
                            trayCheck.Checked = trayValue.ToString() == "1";
                        else
                            trayCheck.Checked = true;
                    }
                    else
                    {
                        topCheck.Checked = true;
                        trayCheck.Checked = true;
                    }
                }

                ApplyTopMost();
                ApplyTrayBehavior();
            }
            catch { }
        }

        // ============================================================
        // ПРИМЕНЕНИЕ TOPMOST
        // ============================================================
        private void ApplyTopMost()
        {
            try
            {
                if (parentForm != null && !parentForm.IsDisposed)
                {
                    parentForm.TopMost = topCheck.Checked;
                }
            }
            catch { }
        }

        // ============================================================
        // ПРИМЕНЕНИЕ ПОВЕДЕНИЯ ТРЕЯ
        // ============================================================
        private void ApplyTrayBehavior()
        {
            try
            {
                if (parentForm != null && !parentForm.IsDisposed && parentForm is MainForm mainForm)
                {
                    mainForm.SetTrayMode(trayCheck.Checked);
                }
            }
            catch { }
        }

        // ============================================================
        // ОТКРЫТИЕ САЙТА (ИСПРАВЛЕНА ОШИБКА .NET 8)
        // ============================================================
        private void OpenWebsite()
        {
            try
            {
                string url = "https://bunnyblack.ct.ws/";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true // ВАЖНО: Без этого .NET 8 ищет файл, а не открывает ссылку
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть ссылку: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // СОХРАНЕНИЕ НАСТРОЕК
        // ============================================================
        private void SaveSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                {
                    if (key != null)
                    {
                        key.SetValue("TopMost", topCheck.Checked ? "1" : "0");
                        key.SetValue("TrayEnabled", trayCheck.Checked ? "1" : "0");
                    }
                }

                ApplyTopMost();
                ApplyTrayBehavior();

                MessageBox.Show("Настройки сохранены", "Успешно",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // НОРМАЛИЗАЦИЯ ПУТИ (ДЛЯ WINRE)
        // ============================================================
        private string NormalizeStartupPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            if (isWinRE && path.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase))
            {
                return @"C:\" + path.Substring(3);
            }

            return path;
        }

        // ============================================================
        // ДОБАВЛЕНИЕ В АВТОЗАГРУЗКУ
        // ============================================================
        private void AddToStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        string exePath = NormalizeStartupPath(Process.GetCurrentProcess().MainModule.FileName);
                        key.SetValue("BunnyBlack", exePath);
                        MessageBox.Show("Добавлено в автозагрузку", "Успешно",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // УДАЛЕНИЕ ИЗ АВТОЗАГРУЗКИ
        // ============================================================
        private void RemoveFromStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null && key.GetValue("BunnyBlack") != null)
                    {
                        key.DeleteValue("BunnyBlack");
                        MessageBox.Show("Убрано из автозагрузки", "Успешно",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}