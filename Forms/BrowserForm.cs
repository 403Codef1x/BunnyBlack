// language: C#, file: Forms/BrowserForm.cs
// Полная замена. «Встроенные программы» теперь контейнер с 3 вкладками:
//   1. Диспетчер задач (TaskManagerForm)
//   2. Проводник (FileManagerForm)
//   3. Питание (ShutdownForm)
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BunnyBlack.Forms
{
    public partial class BrowserForm : UserControl
    {
        private bool isWinRE;
        private FlowLayoutPanel tabsPanel;
        private Panel contentPanel;
        private Button[] tabButtons;
        private UserControl currentTab;

        private TaskManagerForm taskManager;
        private FileManagerForm fileManager;
        private ShutdownForm shutdown;

        private int lastIndex = -1;

        public BrowserForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(13, 13, 13);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
                BackColor = Color.FromArgb(13, 13, 13),
                Margin = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Заголовок
            var titlePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Height = 50,
                Margin = new Padding(0),
                Padding = new Padding(28, 24, 28, 0)
            };
            var title = new Label
            {
                Text = "Встроенные программы",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            titlePanel.Controls.Add(title);
            layout.Controls.Add(titlePanel, 0, 0);

            // Панель вкладок
            tabsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = Color.FromArgb(13, 13, 13),
                Padding = new Padding(28, 8, 0, 0),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true
            };

            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Padding = new Padding(28, 0, 28, 20)
            };

            var tabs = new (string text, int index)[]
            {
                ("Диспетчер задач", 0),
                ("Проводник", 1),
                ("Питание", 2),
            };

            tabButtons = new Button[tabs.Length];
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = tabs[i].Item2;
                var btn = new Button
                {
                    Text = tabs[i].Item1,
                    Tag = index,
                    Width = 200,
                    Height = 34,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(22, 22, 22),
                    ForeColor = Color.FromArgb(170, 170, 170),
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Margin = new Padding(0, 0, 8, 0),
                    UseVisualStyleBackColor = false,
                    FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(45, 45, 45) }
                };
                btn.Click += (s, e) => SelectTab(index);
                tabButtons[i] = btn;
                tabsPanel.Controls.Add(btn);
            }

            // Сборка
            var contentWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
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

            for (int i = 0; i < tabButtons.Length; i++)
            {
                bool selected = i == index;
                tabButtons[i].BackColor = selected ? Color.FromArgb(35, 55, 75) : Color.FromArgb(22, 22, 22);
                tabButtons[i].ForeColor = selected ? Color.FromArgb(240, 240, 240) : Color.FromArgb(170, 170, 170);
                tabButtons[i].FlatAppearance.BorderColor = selected ? Color.FromArgb(70, 110, 150) : Color.FromArgb(45, 45, 45);
            }

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
                    if (fileManager == null)
                    {
                        fileManager = new FileManagerForm(isWinRE);
                        fileManager.Dock = DockStyle.Fill;
                    }
                    currentTab = fileManager;
                    break;
                case 2:
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
                    BackColor = Color.FromArgb(13, 13, 13),
                    Padding = new Padding(0),
                    Margin = new Padding(0)
                };
                wrapper.Controls.Add(currentTab);
                contentPanel.Controls.Add(wrapper);
            }
        }
    }
}