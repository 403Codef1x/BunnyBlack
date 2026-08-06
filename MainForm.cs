using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using BunnyBlack.Forms;

namespace BunnyBlack
{
    public class MainForm : Form
    {
        private Panel sidebar;
        private Panel contentPanel;
        private Button[] navButtons;
        private Label statusLabel;
        private bool isWinRE;
        private bool isDarkTheme = true;

        private NotifyIcon trayIcon;

        // КЭШ ВСЕХ ФОРМ (создаются 1 раз при старте)
        private AutostartForm cachedAutostart;
        private ScanForm cachedScan;
        private UsersForm cachedUsers;
        private ToolsForm cachedTools;
        private BrowserForm cachedBrowser;
        private SettingsForm cachedSettings;

        public MainForm(bool winRE, string windowTitle)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);

            InitializeComponent();

            this.Text = windowTitle;

            // Загрузка настроек
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                {
                    if (key != null)
                    {
                        object topVal = key.GetValue("TopMost");
                        if (topVal != null) this.TopMost = topVal.ToString() == "1";
                        else this.TopMost = true;

                        object trayVal = key.GetValue("TrayEnabled");
                        if (trayVal != null) SetTrayMode(trayVal.ToString() == "1");
                        else SetTrayMode(true);
                    }
                    else
                    {
                        this.TopMost = true;
                        SetTrayMode(true);
                    }
                }
            }
            catch
            {
                this.TopMost = true;
                SetTrayMode(true);
            }

            // ============================================================
            // ПРЕДВАРИТЕЛЬНОЕ СОЗДАНИЕ ВСЕХ ВКЛАДОК (КЭШИРОВАНИЕ)
            // ============================================================
            cachedAutostart = new AutostartForm(isWinRE);
            cachedScan = new ScanForm(isWinRE);
            cachedUsers = new UsersForm(isWinRE);
            cachedTools = new ToolsForm(isWinRE);
            cachedBrowser = new BrowserForm(isWinRE);
            cachedSettings = new SettingsForm(isWinRE);

            // Добавляем их в панель, но скрываем все, кроме первой
            contentPanel.Controls.Add(cachedAutostart);
            contentPanel.Controls.Add(cachedScan);
            contentPanel.Controls.Add(cachedUsers);
            contentPanel.Controls.Add(cachedTools);
            contentPanel.Controls.Add(cachedBrowser);
            contentPanel.Controls.Add(cachedSettings);

            cachedAutostart.Dock = DockStyle.Fill;
            cachedScan.Dock = DockStyle.Fill;
            cachedUsers.Dock = DockStyle.Fill;
            cachedTools.Dock = DockStyle.Fill;
            cachedBrowser.Dock = DockStyle.Fill;
            cachedSettings.Dock = DockStyle.Fill;

            // Скрываем всё, кроме первой
            cachedScan.Visible = false;
            cachedUsers.Visible = false;
            cachedTools.Visible = false;
            cachedBrowser.Visible = false;
            cachedSettings.Visible = false;

            ShowPage(0);
        }

        public void SetFullScreenMode(bool enable)
        {
            if (enable)
            {
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
                this.TopMost = true;
                this.BringToFront();
            }
            else
            {
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Normal;
                this.TopMost = false;
            }
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.SuspendLayout();
            this.ClientSize = new System.Drawing.Size(1479, 825);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "MainForm";
            this.StartPosition = FormStartPosition.CenterScreen;

            sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 200,
                BackColor = Color.FromArgb(18, 18, 18)
            };
            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13)
            };
            statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                ForeColor = Color.FromArgb(170, 170, 170),
                BackColor = Color.FromArgb(18, 18, 18)
            };

            Controls.Add(contentPanel);
            Controls.Add(sidebar);
            Controls.Add(statusLabel);

            BuildSidebar();
            this.ResumeLayout(false);
        }

        private void BuildSidebar()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                AutoScroll = false
            };

            int y = 16;

            var logo = new Label
            {
                Text = "Bunny Black",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                TextAlign = ContentAlignment.MiddleCenter,
                Width = 184,
                Height = 45,
                BackColor = Color.Transparent,
                Location = new Point(8, y)
            };
            panel.Controls.Add(logo);
            y += 55;

            var sub = new Label
            {
                Text = "System Recovery",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(102, 102, 102),
                TextAlign = ContentAlignment.MiddleCenter,
                Width = 184,
                Height = 25,
                BackColor = Color.Transparent,
                Location = new Point(8, y)
            };
            panel.Controls.Add(sub);
            y += 35;

            var sep2 = new Panel
            {
                Height = 1,
                BackColor = Color.FromArgb(37, 37, 37),
                Location = new Point(8, y),
                Width = 184
            };
            panel.Controls.Add(sep2);
            y += 11;

            var sep3 = new Panel
            {
                Height = 10,
                BackColor = Color.Transparent,
                Location = new Point(8, y),
                Width = 184
            };
            panel.Controls.Add(sep3);
            y += 10;

            var pages = new (string text, int index)[]
            {
                ("Автозагрузка", 0),
                ("Сканирование", 1),
                ("Пользователи", 2),
                ("Доп.Возможности", 3),
                ("Встроенные программы", 4),
                ("Настройки", 5),
            };

            navButtons = new Button[pages.Length];

            for (int i = 0; i < pages.Length; i++)
            {
                var btn = new Button
                {
                    Text = pages[i].text,
                    Tag = pages[i].index,
                    FlatStyle = FlatStyle.Flat,
                    FlatAppearance = { BorderSize = 0 },
                    ForeColor = Color.FromArgb(140, 140, 140),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(14, 8, 8, 8),
                    Font = new Font("Segoe UI", 13),
                    Height = 40,
                    Width = 184,
                    Cursor = Cursors.Hand,
                    BackColor = Color.Transparent,
                    AutoEllipsis = false,
                    UseCompatibleTextRendering = true,
                    Location = new Point(8, y)
                };

                int idx = i;
                btn.Click += (s, e) => ShowPage(idx);

                panel.Controls.Add(btn);
                navButtons[i] = btn;
                y += 46;
            }

            sidebar.Controls.Add(panel);
        }

        public void ApplyTheme(bool useDark)
        {
            isDarkTheme = useDark;

            Color bgColor = useDark ? Color.FromArgb(13, 13, 13) : Color.FromArgb(235, 235, 235);
            Color fgColor = useDark ? Color.FromArgb(216, 216, 216) : Color.FromArgb(30, 30, 30);
            Color sidebarColor = useDark ? Color.FromArgb(18, 18, 18) : Color.FromArgb(230, 230, 230);
            Color logoColor = useDark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(20, 20, 20);
            Color subColor = useDark ? Color.FromArgb(102, 102, 102) : Color.FromArgb(80, 80, 80);
            Color sepColor = useDark ? Color.FromArgb(37, 37, 37) : Color.FromArgb(180, 180, 180);

            this.BackColor = bgColor;
            this.ForeColor = fgColor;
            contentPanel.BackColor = bgColor;
            sidebar.BackColor = sidebarColor;
            statusLabel.BackColor = sidebarColor;
            statusLabel.ForeColor = useDark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(70, 70, 70);

            foreach (Control ctrl in sidebar.Controls)
            {
                if (ctrl is Panel p)
                {
                    foreach (Control child in p.Controls)
                    {
                        if (child is Label lbl)
                        {
                            if (lbl.Text == "Bunny Black")
                                lbl.ForeColor = logoColor;
                            if (lbl.Text == "System Recovery")
                                lbl.ForeColor = subColor;
                        }
                        if (child is Panel sep && sep.Height == 1)
                            sep.BackColor = sepColor;
                    }
                }
            }

            foreach (var btn in navButtons)
            {
                if (btn.BackColor == Color.Transparent)
                {
                    btn.ForeColor = useDark ? Color.FromArgb(140, 140, 140) : Color.FromArgb(60, 60, 60);
                }
                else
                {
                    btn.ForeColor = useDark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(20, 20, 20);
                }
            }

            this.Invalidate();
            this.Update();
        }

        public void SetTrayMode(bool enabled)
        {
            if (enabled)
            {
                if (trayIcon == null)
                {
                    trayIcon = new NotifyIcon();
                    trayIcon.Icon = this.Icon;
                    trayIcon.Text = "Bunny Black";
                    trayIcon.Visible = true;

                    ContextMenuStrip trayMenu = new ContextMenuStrip();
                    trayMenu.Items.Add("Открыть", null, (s, e) =>
                    {
                        this.Show();
                        this.WindowState = FormWindowState.Normal;
                        this.BringToFront();
                    });
                    trayMenu.Items.Add("Выход", null, (s, e) => Application.Exit());
                    trayIcon.ContextMenuStrip = trayMenu;

                    trayIcon.DoubleClick += (s, e) =>
                    {
                        this.Show();
                        this.WindowState = FormWindowState.Normal;
                        this.BringToFront();
                    };
                }

                trayIcon.Visible = true;

                this.Resize += (s, e) =>
                {
                    if (this.WindowState == FormWindowState.Minimized && trayIcon.Visible)
                    {
                        this.Hide();
                    }
                };
            }
            else
            {
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    trayIcon = null;
                }
                this.WindowState = FormWindowState.Normal;
            }
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < navButtons.Length; i++)
            {
                if (i == index)
                {
                    navButtons[i].ForeColor = isDarkTheme ? Color.FromArgb(240, 240, 240) : Color.FromArgb(20, 20, 20);
                    navButtons[i].BackColor = Color.Transparent;
                }
                else
                {
                    navButtons[i].ForeColor = isDarkTheme ? Color.FromArgb(140, 140, 140) : Color.FromArgb(60, 60, 60);
                    navButtons[i].BackColor = Color.Transparent;
                }
            }

            // МГНОВЕННОЕ ПЕРЕКЛЮЧЕНИЕ: Меняем Visible, а не пересоздаём
            cachedAutostart.Visible = (index == 0);
            cachedScan.Visible = (index == 1);
            cachedUsers.Visible = (index == 2);
            cachedTools.Visible = (index == 3);
            cachedBrowser.Visible = (index == 4);
            cachedSettings.Visible = (index == 5);

            // Если пользователь впервые открыл Сканирование, запускаем авто-скан в фоне
            if (index == 1 && !cachedScan.IsScanCompleted)
            {
                // Запускаем сканирование асинхронно, не блокируя интерфейс
                cachedScan.BeginAutoScan();
            }

            SetStatus($"Страница: {navButtons[index].Text}");
        }

        public void SetStatus(string text)
        {
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action(() => statusLabel.Text = text));
            }
            else
            {
                statusLabel.Text = text;
            }
        }
    }
}