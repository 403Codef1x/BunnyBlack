// language: C#, file: Forms/SettingsForm.cs
// Полная замена.
// TopMost по умолчанию включён. Если ключа нет — ставим 1.
// Сохранены: LoadSettings, SaveSettings, NormalizeStartupPath, AddToStartup, RemoveFromStartup.
using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class SettingsForm : UserControl
    {
        private CheckBox trayCheck;
        private CheckBox topMostCheck;
        private bool isWinRE;

        public SettingsForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);

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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // заголовок
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));   // окно
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));   // автозагрузка
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // кнопка
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // пусто

            var title = new Label
            {
                Text = "Настройки",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(title, 0, 0);

            // ===== ОКНО =====
            var winGroup = new GroupBox
            {
                Text = "Окно",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(240, 240, 240),
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(22, 22, 22)
            };
            var winLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(10, 8, 10, 8),
                BackColor = Color.Transparent,
                WrapContents = false
            };

            trayCheck = new CheckBox
            {
                Text = "Сворачивать в системный трей (возле часов)",
                ForeColor = Color.FromArgb(216, 216, 216),
                Font = new Font("Segoe UI", 11),
                AutoSize = true,
                Checked = true,
                BackColor = Color.Transparent
            };
            winLayout.Controls.Add(trayCheck);

            topMostCheck = new CheckBox
            {
                Text = "Поверх всех окон",
                ForeColor = Color.FromArgb(216, 216, 216),
                Font = new Font("Segoe UI", 11),
                AutoSize = true,
                Checked = true,     // дефолт включён
                BackColor = Color.Transparent
            };
            winLayout.Controls.Add(topMostCheck);

            winGroup.Controls.Add(winLayout);
            layout.Controls.Add(winGroup, 0, 1);

            // ===== АВТОЗАГРУЗКА =====
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

            // ===== СОХРАНИТЬ =====
            var saveBtn = new Button
            {
                Text = "Сохранить настройки",
                Height = 40,
                Width = 220,
                BackColor = Color.FromArgb(15, 34, 24),
                ForeColor = Color.FromArgb(136, 221, 170),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(51, 102, 68) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            saveBtn.Click += (s, e) => SaveSettings();
            layout.Controls.Add(saveBtn, 0, 3);

            this.Controls.Add(layout);
        }

        private void LoadSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                {
                    if (key != null)
                    {
                        object trayValue = key.GetValue("TrayEnabled");
                        trayCheck.Checked = trayValue == null || trayValue.ToString() == "1";

                        object tmValue = key.GetValue("TopMost");
                        topMostCheck.Checked = tmValue == null || tmValue.ToString() == "1";
                    }
                    else
                    {
                        // Первый запуск — дефолт оба включены
                        trayCheck.Checked = true;
                        topMostCheck.Checked = true;

                        // сразу пишем в реестр, чтобы MainForm читал согласованное значение
                        try
                        {
                            using (var writeKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                            {
                                if (writeKey != null)
                                {
                                    writeKey.SetValue("TrayEnabled", "1");
                                    writeKey.SetValue("TopMost", "1");
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                {
                    if (key != null)
                    {
                        key.SetValue("TrayEnabled", trayCheck.Checked ? "1" : "0");
                        key.SetValue("TopMost", topMostCheck.Checked ? "1" : "0");
                    }
                }

                MainForm mainForm = null;
                foreach (Form f in Application.OpenForms)
                {
                    if (f is MainForm mf) { mainForm = mf; break; }
                }

                if (mainForm != null)
                {
                    mainForm.SetTrayMode(trayCheck.Checked);
                    mainForm.SetTopMostMode(topMostCheck.Checked);
                }

                NedoMessageBox.Show("Настройки сохранены");
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show($"Ошибка сохранения: {ex.Message}", isError: true);
            }
        }

        private string NormalizeStartupPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            if (isWinRE && path.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase))
                return @"C:\" + path.Substring(3);

            return path;
        }

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
                        NedoMessageBox.Show("Добавлено в автозагрузку");
                    }
                }
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show($"Ошибка: {ex.Message}", isError: true);
            }
        }

        private void RemoveFromStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null && key.GetValue("BunnyBlack") != null)
                    {
                        key.DeleteValue("BunnyBlack");
                        NedoMessageBox.Show("Убрано из автозагрузки");
                    }
                }
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show($"Ошибка: {ex.Message}", isError: true);
            }
        }
    }
}