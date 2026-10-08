// language: C#, file: Forms/SettingsForm.cs
// Добавлена группа «Тема и язык». Остальное как было.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using BunnyBlack.Core;
using Microsoft.Win32;

namespace BunnyBlack.Forms
{
    public partial class SettingsForm : UserControl
    {
        private CheckBox trayCheck;
        private CheckBox topMostCheck;
        private ComboBox themeCombo;
        private ComboBox langCombo;
        private Label titleLabel;
        private GroupBox winGroup;
        private GroupBox themeGroup;
        private GroupBox startupGroup;
        private GroupBox updateGroup;
        private Label currentVersionLabel;
        private Label updateStatusLabel;
        private Button checkUpdateBtn;
        private LinkLabel releaseNotesLink;
        private LinkLabel releasePageLink;
        private Button saveBtn;
        private bool isWinRE;

        public SettingsForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;

            InitializeComponent();
            LoadSettings();

            ThemeManager.ThemeChanged += ApplyTheme;
            Loc.LanguageChanged += ApplyLanguage;
        }

        private void ApplyTheme()
        {
            if (InvokeRequired) { Invoke(new Action(ApplyTheme)); return; }
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;
            ApplyThemeRecursive(this);
            Invalidate(true);
        }

        private void ApplyThemeRecursive(Control c)
        {
            if (c is GroupBox gb)
            {
                gb.BackColor = ThemeManager.GroupBack;
                gb.ForeColor = ThemeManager.Foreground;
            }
            else if (c is TextBox tb)
            {
                tb.BackColor = ThemeManager.Input;
                tb.ForeColor = ThemeManager.Foreground;
            }
            else if (c is Button btn)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = ThemeManager.PanelAlt;
                btn.ForeColor = ThemeManager.Foreground;
                if (btn.FlatAppearance != null)
                    btn.FlatAppearance.BorderColor = ThemeManager.Border;
            }
            else if (c is Label lbl)
            {
                lbl.BackColor = Color.Transparent;
                lbl.ForeColor = ThemeManager.Foreground;
            }
            else if (c is CheckBox cb)
            {
                cb.BackColor = Color.Transparent;
                cb.ForeColor = ThemeManager.Foreground;
            }
            else if (c is ComboBox combo)
            {
                combo.BackColor = ThemeManager.Input;
                combo.ForeColor = ThemeManager.Foreground;
            }

            foreach (Control child in c.Controls) ApplyThemeRecursive(child);
        }

        private void ApplyLanguage()
        {
            if (InvokeRequired) { Invoke(new Action(ApplyLanguage)); return; }
            if (titleLabel != null) titleLabel.Text = Loc.T("settings.title");
            if (winGroup != null) winGroup.Text = Loc.T("settings.group.window");
            if (themeGroup != null) themeGroup.Text = Loc.T("settings.group.theme");
            if (startupGroup != null) startupGroup.Text = Loc.T("settings.group.startup");
            if (updateGroup != null) updateGroup.Text = Loc.T("settings.group.updates");
            if (trayCheck != null) trayCheck.Text = Loc.T("settings.tray");
            if (topMostCheck != null) topMostCheck.Text = Loc.T("settings.topmost");
            if (saveBtn != null) saveBtn.Text = Loc.T("settings.save");
            if (checkUpdateBtn != null) checkUpdateBtn.Text = Loc.T("settings.updates.check");
            if (currentVersionLabel != null)
                currentVersionLabel.Text = $"Версия: {Updater.CurrentVersion}";
            if (themeCombo != null && themeCombo.Items.Count >= 2)
            {
                int idx = themeCombo.SelectedIndex;
                themeCombo.Items[0] = Loc.T("settings.theme.dark");
                themeCombo.Items[1] = Loc.T("settings.theme.light");
                themeCombo.SelectedIndex = idx;
            }
            if (langCombo != null && langCombo.Items.Count >= 2)
            {
                int idx = langCombo.SelectedIndex;
                langCombo.Items[0] = Loc.T("settings.lang.ru");
                langCombo.Items[1] = Loc.T("settings.lang.en");
                langCombo.SelectedIndex = idx;
            }
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(28, 24, 28, 20),
                BackColor = ThemeManager.Background,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            layout.RowStyles.Clear();
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            titleLabel = new Label
            {
                Text = Loc.T("settings.title"),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(titleLabel, 0, 0);

            // ===== ОКНО =====
            winGroup = new GroupBox
            {
                Text = Loc.T("settings.group.window"),
                Dock = DockStyle.Fill,
                ForeColor = ThemeManager.Foreground,
                Font = new Font("Segoe UI", 11),
                BackColor = ThemeManager.GroupBack
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
                Text = Loc.T("settings.tray"),
                ForeColor = ThemeManager.Foreground,
                Font = new Font("Segoe UI", 11),
                AutoSize = true,
                Checked = true,
                BackColor = Color.Transparent
            };
            winLayout.Controls.Add(trayCheck);

            topMostCheck = new CheckBox
            {
                Text = Loc.T("settings.topmost"),
                ForeColor = ThemeManager.Foreground,
                Font = new Font("Segoe UI", 11),
                AutoSize = true,
                Checked = true,
                BackColor = Color.Transparent
            };
            winLayout.Controls.Add(topMostCheck);
            winGroup.Controls.Add(winLayout);
            layout.Controls.Add(winGroup, 0, 1);

            // ===== ТЕМА И ЯЗЫК =====
            themeGroup = new GroupBox
            {
                Text = Loc.T("settings.group.theme"),
                Dock = DockStyle.Fill,
                ForeColor = ThemeManager.Foreground,
                Font = new Font("Segoe UI", 11),
                BackColor = ThemeManager.GroupBack
            };
            var themeLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = Color.Transparent
            };
            themeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            themeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            themeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            themeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            themeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            themeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            themeLayout.Controls.Add(new Label
            {
                Text = Loc.T("settings.theme"),
                ForeColor = ThemeManager.Foreground,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            }, 0, 0);

            themeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.Input,
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill
            };
            themeCombo.Items.Add(Loc.T("settings.theme.dark"));
            themeCombo.Items.Add(Loc.T("settings.theme.light"));
            themeCombo.SelectedIndex = ThemeManager.IsDark ? 0 : 1;
            themeCombo.SelectedIndexChanged += (s, e) =>
            {
                ThemeManager.Current = themeCombo.SelectedIndex == 0 ? AppTheme.Dark : AppTheme.Light;
            };
            themeLayout.Controls.Add(themeCombo, 1, 0);

            themeLayout.Controls.Add(new Label
            {
                Text = Loc.T("settings.lang"),
                ForeColor = ThemeManager.Foreground,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            }, 2, 0);

            langCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.Input,
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill
            };
            langCombo.Items.Add(Loc.T("settings.lang.ru"));
            langCombo.Items.Add(Loc.T("settings.lang.en"));
            langCombo.SelectedIndex = Loc.Current == AppLang.RU ? 0 : 1;
            langCombo.SelectedIndexChanged += (s, e) =>
            {
                Loc.Current = langCombo.SelectedIndex == 0 ? AppLang.RU : AppLang.EN;
            };
            themeLayout.Controls.Add(langCombo, 3, 0);

            themeGroup.Controls.Add(themeLayout);
            layout.Controls.Add(themeGroup, 0, 2);

            // ===== АВТОЗАГРУЗКА =====
            startupGroup = new GroupBox
            {
                Text = Loc.T("settings.group.startup"),
                Dock = DockStyle.Fill,
                ForeColor = ThemeManager.Foreground,
                Font = new Font("Segoe UI", 11),
                BackColor = ThemeManager.GroupBack
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
                Text = Loc.T("settings.startup.add"),
                Height = 35,
                Width = 220,
                BackColor = ThemeManager.SuccessBack,
                ForeColor = ThemeManager.Success,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Success },
                Font = new Font("Segoe UI", 11),
                Cursor = Cursors.Hand
            };
            addBtn.Click += (s, e) => AddToStartup();
            startupLayout.Controls.Add(addBtn);

            var remBtn = new Button
            {
                Text = Loc.T("settings.startup.remove"),
                Height = 35,
                Width = 220,
                BackColor = ThemeManager.DangerBack,
                ForeColor = ThemeManager.Danger,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Danger },
                Font = new Font("Segoe UI", 11),
                Cursor = Cursors.Hand
            };
            remBtn.Click += (s, e) => RemoveFromStartup();
            startupLayout.Controls.Add(remBtn);

            startupGroup.Controls.Add(startupLayout);
            layout.Controls.Add(startupGroup, 0, 3);

            // ===== ОБНОВЛЕНИЯ =====
            updateGroup = new GroupBox
            {
                Text = Loc.T("settings.group.updates"),
                Dock = DockStyle.Fill,
                ForeColor = ThemeManager.Foreground,
                Font = new Font("Segoe UI", 11),
                BackColor = ThemeManager.GroupBack
            };

            var updateLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = Color.Transparent
            };
            updateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            updateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            updateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            updateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            currentVersionLabel = new Label
            {
                Text = $"Версия: {Updater.CurrentVersion}",
                ForeColor = ThemeManager.Foreground,
                Font = new Font("Segoe UI", 10),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            updateLayout.Controls.Add(currentVersionLabel, 0, 0);

            updateStatusLabel = new Label
            {
                Text = "Нажми «Проверить обновления»",
                ForeColor = ThemeManager.Muted,
                Font = new Font("Segoe UI", 10),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            updateLayout.Controls.Add(updateStatusLabel, 0, 1);

            releaseNotesLink = new LinkLabel
            {
                Text = "",
                ForeColor = ThemeManager.Success,
                ActiveLinkColor = ThemeManager.Success,
                LinkColor = ThemeManager.Success,
                Font = new Font("Segoe UI", 9),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Visible = false
            };
            releaseNotesLink.LinkClicked += (s, e) => OpenUrl(Updater.LatestReleaseUrl);
            updateLayout.Controls.Add(releaseNotesLink, 0, 2);

            var updateBtnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                WrapContents = false
            };

            checkUpdateBtn = new Button
            {
                Text = Loc.T("Проверить обновления"),
                Height = 28,
                Width = 200,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.PanelAlt,
                ForeColor = ThemeManager.Foreground,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Border },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9)
            };
            checkUpdateBtn.Click += (s, e) =>
                System.Threading.Tasks.Task.Run(() => CheckForUpdatesAsync());
            updateBtnPanel.Controls.Add(checkUpdateBtn);

            updateLayout.Controls.Add(updateBtnPanel, 0, 3);
            updateGroup.Controls.Add(updateLayout);
            layout.Controls.Add(updateGroup, 0, 4);

            // ===== СОХРАНИТЬ =====
            saveBtn = new Button
            {
                Text = Loc.T("settings.save"),
                Height = 40,
                Width = 220,
                BackColor = ThemeManager.SuccessBack,
                ForeColor = ThemeManager.Success,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Success },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            saveBtn.Click += (s, e) => SaveSettings();
            layout.Controls.Add(saveBtn, 0, 5);

            this.Controls.Add(layout);
        }

        private void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                NedoMessageBox.Show("Ссылка пустая.", isError: true);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return;
            }
            catch (Exception ex) { Debug.WriteLine("[OpenUrl/1] " + ex.Message); }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = url,
                    UseShellExecute = true
                });
                return;
            }
            catch (Exception ex) { Debug.WriteLine("[OpenUrl/2] " + ex.Message); }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"url.dll,FileProtocolHandler {url}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return;
            }
            catch (Exception ex) { Debug.WriteLine("[OpenUrl/3] " + ex.Message); }

            NedoMessageBox.Show(
                "Не удалось открыть браузер.\n\nОткрой вручную:\n" + url,
                isError: true);
        }

        private async System.Threading.Tasks.Task CheckForUpdatesAsync()
        {
            try
            {
                this.Invoke((Action)(() =>
                {
                    updateStatusLabel.Text = "Проверка...";
                    updateStatusLabel.ForeColor = ThemeManager.Muted;
                    checkUpdateBtn.Enabled = false;
                    releaseNotesLink.Visible = false;
                }));

                var info = await Updater.CheckAsync();

                this.Invoke((Action)(() =>
                {
                    checkUpdateBtn.Enabled = true;

                    if (!string.IsNullOrEmpty(info.Error))
                    {
                        updateStatusLabel.Text = info.Error;
                        updateStatusLabel.ForeColor = ThemeManager.Danger;
                        return;
                    }

                    if (info.HasUpdate)
                    {
                        string size = info.DownloadSize > 0
                            ? $" (размер exe: {Updater.FormatSize(info.DownloadSize)})"
                            : "";
                        updateStatusLabel.Text = $"Доступна новая версия: {info.LatestVersion}{size}";
                        updateStatusLabel.ForeColor = ThemeManager.Warning;

                        releaseNotesLink.Text = "Открыть страницу релиза →";
                        releaseNotesLink.Visible = true;
                    }
                    else
                    {
                        updateStatusLabel.Text = $"Установлена последняя версия ({info.CurrentVersion})";
                        updateStatusLabel.ForeColor = ThemeManager.Success;
                    }
                }));
            }
            catch (Exception ex)
            {
                this.Invoke((Action)(() =>
                {
                    checkUpdateBtn.Enabled = true;
                    updateStatusLabel.Text = "Ошибка: " + ex.Message;
                    updateStatusLabel.ForeColor = ThemeManager.Danger;
                }));
            }
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
                        trayCheck.Checked = true;
                        topMostCheck.Checked = true;

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

                NedoMessageBox.Show(Loc.T("settings.saved"));
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