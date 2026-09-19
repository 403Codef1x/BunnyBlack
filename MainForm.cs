// language: C#, file: MainForm.cs
// Полная замена. «Файлы» и «Питание» перенесены внутрь «Встроенные программы».
// Навигация: Автозагрузка, Сканер, Пользователи, Доп.Возможности,
// Встроенные программы, Кликер, Настройки.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using BunnyBlack.Forms;
using BunnyBlack.Core;

namespace BunnyBlack
{
    public class MainForm : Form
    {
        private Panel sidebar;
        private Panel contentPanel;
        private Panel modeBanner;
        private Label modeBannerLabel;
        private Button[] navButtons;
        private Label statusLabel;
        private bool isWinRE;
        private bool isDarkTheme = true;

        private NotifyIcon trayIcon;
        private EventHandler trayResizeHandler;

        private readonly Dictionary<int, Func<UserControl>> pageFactories;
        private readonly Dictionary<int, UserControl> loadedPages = new Dictionary<int, UserControl>();

        public MainForm(bool winRE, string windowTitle)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);

            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            InitializeComponent();
            this.Text = windowTitle;

            UpdateModeBanner();
            if (winRE) this.Opacity = 0.98;

            pageFactories = new Dictionary<int, Func<UserControl>>
            {
                [0] = () => new AutostartForm(isWinRE),
                [1] = () => new ScanForm(isWinRE),
                [2] = () => new UsersForm(isWinRE),
                [3] = () => new ToolsForm(isWinRE),
                [4] = () => new BrowserForm(isWinRE),
                [5] = () => new ClickerForm(isWinRE),
                [6] = () => new SettingsForm(isWinRE),
            };

            this.TopMost = true;

            bool trayEnabled = true;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                {
                    if (key != null)
                    {
                        object te = key.GetValue("TrayEnabled");
                        if (te != null) trayEnabled = te.ToString() == "1";
                    }
                }
            }
            catch { }

            SetTrayMode(trayEnabled);
            ShowPage(0);

            this.BringToFront();
            this.Activate();
        }

        public void SetFullScreenMode(bool enable)
        {
            if (enable)
            {
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
                this.BringToFront();
            }
            else
            {
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Normal;
            }
        }

        private void InitializeComponent()
        {
            this.ClientSize = new Size(1479, 825);
            this.Name = "MainForm";
            this.StartPosition = FormStartPosition.CenterScreen;

            sidebar = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Color.FromArgb(18, 18, 18) };
            contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 13, 13) };

            modeBanner = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.FromArgb(22, 22, 22) };
            modeBannerLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 200),
                BackColor = Color.Transparent
            };
            modeBanner.Controls.Add(modeBannerLabel);

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
            Controls.Add(modeBanner);

            BuildSidebar();
        }

        private void UpdateModeBanner()
        {
            if (isWinRE)
            {
                modeBanner.BackColor = Color.FromArgb(60, 40, 15);
                modeBannerLabel.Text = "⚙  WinRE — оффлайн-режим (изменения применяются к целевой системе)";
                modeBannerLabel.ForeColor = Color.FromArgb(255, 200, 120);
            }
            else
            {
                modeBanner.BackColor = Color.FromArgb(15, 40, 25);
                modeBannerLabel.Text = "●  Online — работа с текущей системой";
                modeBannerLabel.ForeColor = Color.FromArgb(136, 221, 170);
            }
        }

        private void BuildSidebar()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            int y = 16;

            panel.Controls.Add(new Label
            {
                Text = "Bunny Black",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                TextAlign = ContentAlignment.MiddleCenter,
                Width = 184,
                Height = 45,
                BackColor = Color.Transparent,
                Location = new Point(8, y)
            });
            y += 55;

            panel.Controls.Add(new Label
            {
                Text = "System Recovery",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(102, 102, 102),
                TextAlign = ContentAlignment.MiddleCenter,
                Width = 184,
                Height = 25,
                BackColor = Color.Transparent,
                Location = new Point(8, y)
            });
            y += 35;

            panel.Controls.Add(new Panel { Height = 1, BackColor = Color.FromArgb(37, 37, 37), Location = new Point(8, y), Width = 184 });
            y += 11;
            panel.Controls.Add(new Panel { Height = 10, BackColor = Color.Transparent, Location = new Point(8, y), Width = 184 });
            y += 10;

            var pages = new (string text, int index)[]
            {
                ("Автозагрузка", 0),
                ("Сканер", 1),
                ("Пользователи", 2),
                ("Доп.Возможности", 3),
                ("Встроенные программы", 4),
                ("Кликер", 5),
                ("Настройки", 6),
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
                    Font = new Font("Segoe UI", 12),
                    Height = 40,
                    Width = 184,
                    Cursor = Cursors.Hand,
                    BackColor = Color.Transparent,
                    UseCompatibleTextRendering = true,
                    Location = new Point(8, y)
                };
                int idx = i;
                btn.Click += (s, e) => ShowPage(idx);
                panel.Controls.Add(btn);
                navButtons[i] = btn;
                y += 44;
            }

            sidebar.Controls.Add(panel);
        }

        public void ApplyTheme(bool useDark)
        {
            isDarkTheme = useDark;
            Color bgColor = useDark ? Color.FromArgb(13, 13, 13) : Color.FromArgb(235, 235, 235);
            Color fgColor = useDark ? Color.FromArgb(216, 216, 216) : Color.FromArgb(30, 30, 30);
            Color sidebarColor = useDark ? Color.FromArgb(18, 18, 18) : Color.FromArgb(230, 230, 230);

            this.BackColor = bgColor;
            this.ForeColor = fgColor;
            contentPanel.BackColor = bgColor;
            sidebar.BackColor = sidebarColor;
            statusLabel.BackColor = sidebarColor;

            this.Invalidate();
            this.Update();
        }

        public void SetTopMostMode(bool enabled)
        {
            this.TopMost = enabled;
            if (enabled) { this.BringToFront(); this.Activate(); }
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

                    var menu = new ContextMenuStrip();
                    menu.Items.Add("Открыть", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; BringToFront(); });
                    menu.Items.Add("Выход", null, (s, e) => Application.Exit());
                    trayIcon.ContextMenuStrip = menu;
                    trayIcon.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; BringToFront(); };
                }
                trayIcon.Visible = true;

                if (trayResizeHandler != null) { this.Resize -= trayResizeHandler; trayResizeHandler = null; }
                trayResizeHandler = (s, e) =>
                {
                    if (WindowState == FormWindowState.Minimized && trayIcon != null && trayIcon.Visible) Hide();
                };
                this.Resize += trayResizeHandler;
            }
            else
            {
                if (trayResizeHandler != null) { this.Resize -= trayResizeHandler; trayResizeHandler = null; }
                if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }
                WindowState = FormWindowState.Normal;
            }
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < navButtons.Length; i++)
            {
                navButtons[i].ForeColor = i == index
                    ? Color.FromArgb(240, 240, 240)
                    : Color.FromArgb(140, 140, 140);
                navButtons[i].BackColor = Color.Transparent;
            }

            if (!loadedPages.ContainsKey(index))
            {
                var ctrl = pageFactories[index]();
                ctrl.Dock = DockStyle.Fill;
                ctrl.Visible = false;
                contentPanel.Controls.Add(ctrl);
                loadedPages[index] = ctrl;
            }

            foreach (var kv in loadedPages)
                kv.Value.Visible = (kv.Key == index);

            if (index == 1 && loadedPages[1] is ScanForm sf && !sf.IsScanCompleted)
                sf.BeginAutoScan();

            SetStatus($"Страница: {navButtons[index].Text}");
        }

        public void SetStatus(string text)
        {
            if (statusLabel.InvokeRequired)
                statusLabel.Invoke(new Action(() => statusLabel.Text = text));
            else
                statusLabel.Text = text;
        }
    }
}