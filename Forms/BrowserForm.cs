using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BunnyBlack.Forms
{
    public partial class BrowserForm : UserControl
    {
        private FlowLayoutPanel tabsPanel;
        private Panel contentPanel;
        private Button[] tabButtons;

        // Контейнеры для каждой вкладки
        private Panel browserPanel;
        private Panel taskManagerPanel;
        private Panel disksPanel;
        private Panel registryEditorPanel;
        private Panel cleanerPanel;

        // Компоненты браузера
        private WebView2 webView;
        private TextBox addressBar;
        private Button goButton;
        private Button backButton;
        private Button forwardButton;
        private Button refreshButton;

        // Компоненты управления дисками
        private DataGridView disksGrid;
        private Button refreshDisksButton;
        private ContextMenuStrip disksContextMenu;

        private bool isWinRE;

        public BrowserForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
            SelectTab(0); // По умолчанию открываем Браузер
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.Padding = new Padding(0);
            this.Margin = new Padding(0);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
                BackColor = Color.FromArgb(13, 13, 13),
                Margin = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Высота заголовка и вкладок
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ЗАГОЛОВОК
            Panel titlePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Height = 50,
                Margin = new Padding(0),
                Padding = new Padding(28, 24, 28, 0)
            };

            Label title = new Label
            {
                Text = "Встроенные программы",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
            titlePanel.Controls.Add(title);
            layout.Controls.Add(titlePanel, 0, 0);

            // ПАНЕЛЬ ВКЛАДОК (как в Автозагрузке)
            tabsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = Color.FromArgb(13, 13, 13),
                Padding = new Padding(28, 8, 0, 0),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = true,
                AutoSize = false,
                Margin = new Padding(0)
            };

            // ПАНЕЛЬ КОНТЕНТА
            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Padding = new Padding(28, 0, 28, 20),
                Margin = new Padding(0)
            };

            // ============================================================
            // ВКЛАДКИ (5 штук)
            // ============================================================
            (string, int)[] tabs = new[]
            {
                ("Браузер", 0),
                ("Диспетчер задач", 1),
                ("Управление дисками", 2),
                ("Редактор реестра", 3),
                ("Очистка системы", 4)
            };

            tabButtons = new Button[tabs.Length];
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = tabs[i].Item2;
                Button button = new Button
                {
                    Text = tabs[i].Item1,
                    Tag = index,
                    Width = 175,
                    Height = 34,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(22, 22, 22),
                    ForeColor = Color.FromArgb(170, 170, 170),
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Margin = new Padding(0, 0, 8, 0),
                    UseVisualStyleBackColor = false,
                    FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(45, 45, 45) },
                    AutoEllipsis = true
                };
                button.Click += (s, e) => SelectTab(index);
                tabButtons[i] = button;
                tabsPanel.Controls.Add(button);
            }

            // ============================================================
            // ПОДГОТОВКА КОНТЕНТА
            // ============================================================
            PrepareBrowserContent();
            PrepareTaskManagerContent();
            PrepareDisksContent();
            PrepareRegistryEditorContent();
            PrepareCleanerContent();

            // СБОРКА
            Panel contentWrapper = new Panel
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
        }

        // ============================================================
        // 1. БРАУЗЕР
        // ============================================================
        private void PrepareBrowserContent()
        {
            browserPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Visible = false
            };

            Panel topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 45,
                BackColor = Color.FromArgb(18, 18, 18),
                Padding = new Padding(8)
            };

            backButton = CreateNavButton("◀");
            backButton.Click += (s, e) => webView?.CoreWebView2?.GoBack();
            topPanel.Controls.Add(backButton);

            forwardButton = CreateNavButton("▶");
            forwardButton.Click += (s, e) => webView?.CoreWebView2?.GoForward();
            topPanel.Controls.Add(forwardButton);

            refreshButton = CreateNavButton("⟳");
            refreshButton.Click += (s, e) => webView?.CoreWebView2?.Reload();
            topPanel.Controls.Add(refreshButton);

            addressBar = new TextBox
            {
                Width = 600,
                Height = 30,
                BackColor = Color.FromArgb(22, 22, 22),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10),
                Location = new Point(130, 7)
            };
            addressBar.KeyPress += (s, e) =>
            {
                if (e.KeyChar == (char)Keys.Enter)
                {
                    NavigateTo(addressBar.Text);
                    e.Handled = true;
                }
            };
            topPanel.Controls.Add(addressBar);

            goButton = new Button
            {
                Text = "Перейти",
                Width = 80,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(216, 216, 216),
                Location = new Point(740, 7),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) }
            };
            goButton.Click += (s, e) => NavigateTo(addressBar.Text);
            topPanel.Controls.Add(goButton);

            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(13, 13, 13)
            };

            browserPanel.Controls.Add(webView);
            browserPanel.Controls.Add(topPanel);
            contentPanel.Controls.Add(browserPanel);

            _ = InitializeWebViewAsync();
        }

        private async System.Threading.Tasks.Task InitializeWebViewAsync()
        {
            try
            {
                await webView.EnsureCoreWebView2Async(null);
                webView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    string url = webView.CoreWebView2.Source;
                    if (addressBar.InvokeRequired)
                        addressBar.Invoke(new Action(() => addressBar.Text = url));
                    else
                        addressBar.Text = url;
                };
                webView.CoreWebView2.Navigate("https://www.yandex.ru");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка браузера:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Button CreateNavButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = 34,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 22, 22),
                ForeColor = Color.FromArgb(200, 200, 200),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Location = new Point(8, 7),
                Margin = new Padding(2)
            };
        }

        private void NavigateTo(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (webView?.CoreWebView2 == null) return;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;
            try { webView.CoreWebView2.Navigate(url); } catch { }
        }

        // ============================================================
        // 2. ДИСПЕТЧЕР ЗАДАЧ
        // ============================================================
        private void PrepareTaskManagerContent()
        {
            taskManagerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Visible = false
            };

            TaskManagerForm taskManager = new TaskManagerForm(isWinRE);
            taskManager.Dock = DockStyle.Fill;
            taskManagerPanel.Controls.Add(taskManager);
            contentPanel.Controls.Add(taskManagerPanel);
        }

        // ============================================================
        // 3. УПРАВЛЕНИЕ ДИСКАМИ
        // ============================================================
        private void PrepareDisksContent()
        {
            disksPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Visible = false
            };

            Panel topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(18, 18, 18),
                Padding = new Padding(8)
            };

            refreshDisksButton = new Button
            {
                Text = "Обновить",
                Width = 100,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(216, 216, 216),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Location = new Point(8, 4)
            };
            refreshDisksButton.Click += (s, e) => LoadDisks();

            Label infoLabel = new Label
            {
                Text = "Список подключенных дисков",
                ForeColor = Color.FromArgb(170, 170, 170),
                Font = new Font("Segoe UI", 10),
                Location = new Point(120, 7),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            topPanel.Controls.Add(refreshDisksButton);
            topPanel.Controls.Add(infoLabel);

            // ============================================================
            // КОНТЕКСТНОЕ МЕНЮ ДЛЯ ДИСКОВ
            // ============================================================
            disksContextMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };
            ToolStripMenuItem ejectItem = new ToolStripMenuItem("Удалить / Извлечь диск (Eject)");
            ejectItem.ForeColor = Color.FromArgb(255, 150, 150);
            ejectItem.Click += EjectDisk_Click;
            disksContextMenu.Items.Add(ejectItem);

            // ============================================================
            // ГРИД
            // ============================================================
            disksGrid = new DataGridView
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
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 30,
                ContextMenuStrip = disksContextMenu
            };
            disksGrid.RowTemplate.Height = 26;
            disksGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(25, 25, 25);
            disksGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 200, 200);
            disksGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            disksGrid.DefaultCellStyle.BackColor = Color.FromArgb(18, 18, 18);
            disksGrid.DefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            disksGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 50, 60);
            disksGrid.Columns.Clear();
            disksGrid.Columns.Add("Drive", "Диск");
            disksGrid.Columns.Add("Format", "Файловая система");
            disksGrid.Columns.Add("Total", "Общий размер");
            disksGrid.Columns.Add("Free", "Свободно");
            disksGrid.Columns.Add("Used", "Занято");
            disksGrid.Columns.Add("Status", "Статус");

            disksPanel.Controls.Add(disksGrid);
            disksPanel.Controls.Add(topPanel);
            contentPanel.Controls.Add(disksPanel);

            LoadDisks();
        }

        private void LoadDisks()
        {
            if (disksGrid == null) return;
            disksGrid.Rows.Clear();

            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                foreach (DriveInfo drive in drives)
                {
                    // Исключаем CD/DVD (дисководы) и сетевые диски
                    if (drive.DriveType == DriveType.CDRom || drive.DriveType == DriveType.Network)
                        continue;

                    string driveName = drive.Name.Replace("\\", "");
                    string format = drive.IsReady ? drive.DriveFormat : "Н/Д";
                    string totalSize = drive.IsReady ? FormatBytes(drive.TotalSize) : "Н/Д";
                    string freeSpace = drive.IsReady ? FormatBytes(drive.AvailableFreeSpace) : "Н/Д";
                    string usedSpace = drive.IsReady ? FormatBytes(drive.TotalSize - drive.AvailableFreeSpace) : "Н/Д";
                    string status = drive.IsReady ? "Готов" : "Не готов";

                    disksGrid.Rows.Add(driveName, format, totalSize, freeSpace, usedSpace, status);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки дисков: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void EjectDisk_Click(object sender, EventArgs e)
        {
            if (disksGrid.SelectedRows.Count == 0)
            {
                MessageBox.Show("Выберите диск в таблице.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string driveLetter = disksGrid.SelectedRows[0].Cells[0].Value?.ToString();
            if (string.IsNullOrEmpty(driveLetter)) return;

            if (MessageBox.Show($"Вы уверены, что хотите извлечь диск {driveLetter}:?",
                "Подтверждение удаления", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                return;

            try
            {
                // Используем стандартную команду Windows для извлечения диска
                string drivePath = driveLetter + @":\";
                System.Diagnostics.Process.Start("cmd.exe", $"/c powershell -Command \"(New-Object -ComObject Shell.Application).Namespace(17).ParseName('{drivePath}').InvokeVerb('Eject')\"");

                MessageBox.Show($"Команда на извлечение диска {driveLetter}: отправлена.", "Успешно", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Обновляем таблицу через пару секунд
                Timer t = new Timer { Interval = 3000 };
                t.Tick += (s, ev) => { LoadDisks(); t.Stop(); t.Dispose(); };
                t.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при извлечении диска: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 Б";
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
        // 4. РЕДАКТОР РЕЕСТРА
        // ============================================================
        private void PrepareRegistryEditorContent()
        {
            registryEditorPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Visible = false
            };

            RegistryEditorForm regEditor = new RegistryEditorForm(isWinRE);
            regEditor.Dock = DockStyle.Fill;
            registryEditorPanel.Controls.Add(regEditor);
            contentPanel.Controls.Add(registryEditorPanel);
        }

        // ============================================================
        // 5. ОЧИСТКА СИСТЕМЫ
        // ============================================================
        private void PrepareCleanerContent()
        {
            cleanerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Visible = false
            };

            CleanerForm cleaner = new CleanerForm(isWinRE);
            cleaner.Dock = DockStyle.Fill;
            cleanerPanel.Controls.Add(cleaner);
            contentPanel.Controls.Add(cleanerPanel);
        }

        // ============================================================
        // ПЕРЕКЛЮЧЕНИЕ ВКЛАДОК
        // ============================================================
        private void SelectTab(int index)
        {
            if (tabButtons == null) return;

            for (int i = 0; i < tabButtons.Length; i++)
            {
                bool selected = i == index;
                tabButtons[i].BackColor = selected ? Color.FromArgb(35, 55, 75) : Color.FromArgb(22, 22, 22);
                tabButtons[i].ForeColor = selected ? Color.FromArgb(240, 240, 240) : Color.FromArgb(170, 170, 170);
                tabButtons[i].FlatAppearance.BorderColor = selected ? Color.FromArgb(70, 110, 150) : Color.FromArgb(45, 45, 45);
            }

            browserPanel.Visible = (index == 0);
            taskManagerPanel.Visible = (index == 1);
            disksPanel.Visible = (index == 2);
            registryEditorPanel.Visible = (index == 3);
            cleanerPanel.Visible = (index == 4);
        }
    }
}