// language: C#, file: MainForm.cs
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
        private Button[] navButtons;
        private Label statusLabel;
        private bool isWinRE;
        private Label brandTitle;
        private Label brandSubtitle;

        private NotifyIcon trayIcon;
        private EventHandler trayResizeHandler;

        private readonly Dictionary<int, Func<UserControl>> pageFactories;
        private readonly Dictionary<int, UserControl> loadedPages = new Dictionary<int, UserControl>();

        public MainForm(bool winRE, string windowTitle)
        {
            isWinRE = winRE;

            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;

            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            InitializeComponent();

            if (winRE)
            {
                this.SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
                this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
                this.SetStyle(ControlStyles.UserPaint, true);
                this.SetStyle(ControlStyles.ResizeRedraw, true);
                this.DoubleBuffered = false;
            }

            this.Text = windowTitle;
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

            ThemeManager.ThemeChanged += OnThemeChanged;
            Loc.LanguageChanged += OnLanguageChanged;

            ShowPage(0);

            this.BringToFront();
            this.Activate();

            if (winRE)
            {
                Application.DoEvents();
                this.Invalidate(true);
                this.Update();
            }
        }

        private void OnThemeChanged()
        {
            if (InvokeRequired) { Invoke(new Action(OnThemeChanged)); return; }
            ApplyCurrentTheme();
        }

        private void OnLanguageChanged()
        {
            if (InvokeRequired) { Invoke(new Action(OnLanguageChanged)); return; }
            ApplyCurrentLanguage();
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

            sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 200,
                BackColor = ThemeManager.Panel
            };
            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Background
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                ForeColor = ThemeManager.Muted,
                BackColor = ThemeManager.Panel
            };

            Controls.Add(contentPanel);
            Controls.Add(sidebar);
            Controls.Add(statusLabel);

            BuildSidebar();
        }

        private void BuildSidebar()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            int y = 16;

            brandTitle = new Label
            {
                Text = Loc.T("app.title"),
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                TextAlign = ContentAlignment.MiddleCenter,
                Width = 184,
                Height = 45,
                BackColor = Color.Transparent,
                Location = new Point(8, y)
            };
            panel.Controls.Add(brandTitle);
            y += 55;

            brandSubtitle = new Label
            {
                Text = Loc.T("app.subtitle"),
                Font = new Font("Segoe UI", 10),
                ForeColor = ThemeManager.Muted,
                TextAlign = ContentAlignment.MiddleCenter,
                Width = 184,
                Height = 25,
                BackColor = Color.Transparent,
                Location = new Point(8, y)
            };
            panel.Controls.Add(brandSubtitle);
            y += 35;

            panel.Controls.Add(new Panel
            {
                Height = 1,
                BackColor = ThemeManager.Border,
                Location = new Point(8, y),
                Width = 184
            });
            y += 21;

            string[] navKeys =
            {
                "nav.autostart", "nav.scanner", "nav.users",
                "nav.tools", "nav.builtin", "nav.clicker", "nav.settings"
            };

            navButtons = new Button[navKeys.Length];
            for (int i = 0; i < navKeys.Length; i++)
            {
                var btn = new Button
                {
                    Text = Loc.T(navKeys[i]),
                    Tag = navKeys[i],
                    FlatStyle = FlatStyle.Flat,
                    FlatAppearance = { BorderSize = 0 },
                    ForeColor = ThemeManager.Muted,
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

        private void ApplyCurrentTheme()
        {
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;
            sidebar.BackColor = ThemeManager.Panel;
            contentPanel.BackColor = ThemeManager.Background;
            statusLabel.BackColor = ThemeManager.Panel;
            statusLabel.ForeColor = ThemeManager.Muted;

            if (brandTitle != null) brandTitle.ForeColor = ThemeManager.Foreground;
            if (brandSubtitle != null) brandSubtitle.ForeColor = ThemeManager.Muted;

            ThemeHelper.Apply(this);

            foreach (var kv in loadedPages)
            {
                var page = kv.Value;
                var m = page.GetType().GetMethod("ApplyTheme",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                try { m?.Invoke(page, null); } catch { }
            }

            Invalidate(true);
        }

        private void ApplyCurrentLanguage()
        {
            if (brandTitle != null) brandTitle.Text = Loc.T("app.title");
            if (brandSubtitle != null) brandSubtitle.Text = Loc.T("app.subtitle");

            if (navButtons != null)
            {
                string[] navKeys =
                {
                    "nav.autostart", "nav.scanner", "nav.users",
                    "nav.tools", "nav.builtin", "nav.clicker", "nav.settings"
                };
                for (int i = 0; i < navButtons.Length && i < navKeys.Length; i++)
                {
                    navButtons[i].Tag = navKeys[i];
                    navButtons[i].Text = Loc.T(navKeys[i]);
                }
            }

            foreach (var kv in loadedPages)
            {
                var page = kv.Value;
                var m = page.GetType().GetMethod("ApplyLanguage",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                try { m?.Invoke(page, null); } catch { }
            }

            Invalidate(true);
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
                    ? ThemeManager.Foreground
                    : ThemeManager.Muted;
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

            SetStatus(Loc.T("status.page", navButtons[index].Text));

            if (isWinRE)
            {
                Application.DoEvents();
                contentPanel.Invalidate(true);
                contentPanel.Update();
                this.Invalidate(true);
                this.Update();
                Application.DoEvents();
            }
        }

        public void SetStatus(string text)
        {
            if (statusLabel.InvokeRequired)
                statusLabel.Invoke(new Action(() => statusLabel.Text = text));
            else
                statusLabel.Text = text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.ThemeChanged -= OnThemeChanged;
                Loc.LanguageChanged -= OnLanguageChanged;
            }
            base.Dispose(disposing);
        }
    }
}