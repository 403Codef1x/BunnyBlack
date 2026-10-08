// language: C#, file: Forms/BrowserForm.cs
using System;
using System.Drawing;
using System.Windows.Forms;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class BrowserForm : UserControl
    {
        private bool isWinRE;
        private FlowLayoutPanel tabsPanel;
        private Panel contentPanel;
        private Button[] tabButtons;
        private UserControl currentTab;
        private Label titleLabel;

        private TaskManagerForm taskManager;
        private ShutdownForm shutdown;

        private int lastIndex = -1;

        public BrowserForm(bool winRE)
        {
            isWinRE = winRE;
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
            if (tabsPanel != null) tabsPanel.BackColor = ThemeManager.Background;
            if (contentPanel != null) contentPanel.BackColor = ThemeManager.Background;
            ApplyTabStyles();
            ThemeHelper.Apply(this);
            Invalidate(true);
        }

        public void ApplyLanguage()
        {
            if (InvokeRequired) { Invoke(new Action(ApplyLanguage)); return; }
            if (titleLabel != null) titleLabel.Text = Loc.T("builtin.title");
            if (tabButtons != null && tabButtons.Length >= 2)
            {
                tabButtons[0].Text = Loc.T("builtin.tasks");
                tabButtons[1].Text = Loc.T("builtin.power");
            }
            Invalidate(true);
        }

        private void ApplyTabStyles()
        {
            if (tabButtons == null) return;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                bool sel = (i == lastIndex);
                tabButtons[i].BackColor = sel ? ThemeManager.Accent : ThemeManager.PanelAlt;
                tabButtons[i].ForeColor = sel ? ThemeManager.AccentText : ThemeManager.Foreground;
                if (tabButtons[i].FlatAppearance != null)
                    tabButtons[i].FlatAppearance.BorderColor = sel ? ThemeManager.Accent : ThemeManager.Border;
            }
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
                BackColor = ThemeManager.Background,
                Margin = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var titlePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Background,
                Height = 50,
                Margin = new Padding(0),
                Padding = new Padding(28, 24, 28, 0)
            };
            titleLabel = new Label
            {
                Text = Loc.T("builtin.title"),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            titlePanel.Controls.Add(titleLabel);
            layout.Controls.Add(titlePanel, 0, 0);

            tabsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = ThemeManager.Background,
                Padding = new Padding(28, 8, 0, 0),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true
            };

            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Background,
                Padding = new Padding(28, 0, 28, 20)
            };

            var tabs = new (string text, int index)[]
            {
                (Loc.T("builtin.tasks"), 0),
                (Loc.T("builtin.power"), 1),
            };

            tabButtons = new Button[tabs.Length];
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = tabs[i].index;
                var btn = new Button
                {
                    Text = tabs[i].text,
                    Tag = index,
                    Width = 200,
                    Height = 34,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ThemeManager.PanelAlt,
                    ForeColor = ThemeManager.Foreground,
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Margin = new Padding(0, 0, 8, 0),
                    UseVisualStyleBackColor = false,
                    FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Border }
                };
                btn.Click += (s, e) => SelectTab(index);
                tabButtons[i] = btn;
                tabsPanel.Controls.Add(btn);
            }

            var contentWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Background,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            contentWrapper.Controls.Add(contentPanel);
            contentWrapper.Controls.Add(tabsPanel);

            layout.Controls.Add(contentWrapper, 0, 1);
            this.Controls.Add(layout);

            SelectTab(0);
        }

        private void SelectTab(int index)
        {
            if (lastIndex == index && currentTab != null) return;
            lastIndex = index;

            ApplyTabStyles();

            contentPanel.Controls.Clear();

            switch (index)
            {
                case 0:
                    if (taskManager == null)
                    {
                        taskManager = new TaskManagerForm(isWinRE);
                        taskManager.Dock = DockStyle.Fill;
                    }
                    currentTab = taskManager;
                    break;
                case 1:
                    if (shutdown == null)
                    {
                        shutdown = new ShutdownForm(isWinRE);
                        shutdown.Dock = DockStyle.Fill;
                    }
                    currentTab = shutdown;
                    break;
            }

            if (currentTab != null)
            {
                var wrapper = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = ThemeManager.Background,
                    Padding = new Padding(0),
                    Margin = new Padding(0)
                };
                wrapper.Controls.Add(currentTab);
                contentPanel.Controls.Add(wrapper);
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