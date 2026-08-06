using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.ServiceProcess;
using System.Windows.Forms;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class AutostartForm : UserControl
    {
        private FlowLayoutPanel tabsPanel;
        private Panel contentPanel;
        private Button[] tabButtons;
        private DataGridView combinedRunGrid; // Объединенный грид для Run + RunOnce
        private DataGridView wlGrid;
        private DataGridView folderGrid;
        private DataGridView systemParamsGrid; // Объединенный грид
        private DataGridView taskGrid;
        private DataGridView servicesGrid;
        private DataGridView currentGrid;
        private bool isWinRE;

        public AutostartForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
            LoadData();
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
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
                Text = "Автозагрузка",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
            titlePanel.Controls.Add(title);
            layout.Controls.Add(titlePanel, 0, 0);

            // ПАНЕЛЬ ВКЛАДОК
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
            // Вкладки (Run и RunOnce объединены в одну)
            // ============================================================
            (string, int)[] tabs = new[]
            {
                ("Run / RunOnce", 0),   // ОБЪЕДИНЕННАЯ ВКЛАДКА
                ("Winlogon", 1),
                ("StartUp", 2),
                ("CmdLine и AppInit_DLLs", 3),
                ("Планировщик задач", 4),
                ("Службы", 5)
            };

            tabButtons = new Button[tabs.Length];
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = tabs[i].Item2;
                Button button = new Button
                {
                    Text = tabs[i].Item1,
                    Tag = index,
                    Width = 195,
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
            // ОБЪЕДИНЕННЫЙ ГРИД ДЛЯ RUN И RUNONCE
            // ============================================================
            combinedRunGrid = CreateDarkGrid();
            combinedRunGrid.Columns.Clear();
            combinedRunGrid.Columns.Add("Name", "Название");
            combinedRunGrid.Columns.Add("Path", "Путь");
            combinedRunGrid.Columns.Add("Type", "Тип");
            combinedRunGrid.Columns.Add("Exists", "Файл есть");
            combinedRunGrid.Columns[0].Width = 200;
            combinedRunGrid.Columns[1].Width = 500;
            combinedRunGrid.Columns[2].Width = 120;
            combinedRunGrid.Columns[3].Width = 80;

            ContextMenuStrip combinedMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };

            ToolStripMenuItem deleteItem = new ToolStripMenuItem("Удалить / Сбросить");
            deleteItem.ForeColor = Color.FromArgb(255, 150, 150);
            deleteItem.Click += (s, e) => DeleteSelected(combinedRunGrid);
            combinedMenu.Items.Add(deleteItem);

            combinedRunGrid.ContextMenuStrip = combinedMenu;

            // ============================================================
            // ГРИД WINLOGON (с отдельным меню)
            // ============================================================
            wlGrid = CreateDarkGrid();
            wlGrid.Columns.Clear();
            wlGrid.Columns.Add("Param", "Параметр");
            wlGrid.Columns.Add("Value", "Значение");
            wlGrid.Columns[0].Width = 150;
            wlGrid.Columns[1].Width = 650;

            ContextMenuStrip wlMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };

            ToolStripMenuItem editWlItem = new ToolStripMenuItem("Изменить значение");
            editWlItem.Click += (s, e) => EditValue(wlGrid);
            wlMenu.Items.Add(editWlItem);

            ToolStripMenuItem resetWlItem = new ToolStripMenuItem("Сбросить / Восстановить");
            resetWlItem.ForeColor = Color.FromArgb(255, 150, 150);
            resetWlItem.Click += (s, e) => DeleteSelected(wlGrid);
            wlMenu.Items.Add(resetWlItem);

            wlGrid.ContextMenuStrip = wlMenu;

            // ============================================================
            // ГРИД STARTUP (Папка автозагрузки)
            // ============================================================
            folderGrid = CreateDarkGrid();
            folderGrid.Columns.Clear();
            folderGrid.Columns.Add("Name", "Файл");
            folderGrid.Columns.Add("Path", "Путь");
            folderGrid.Columns.Add("Exists", "Файл есть");
            folderGrid.Columns[0].Width = 200;
            folderGrid.Columns[1].Width = 600;

            ContextMenuStrip folderMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };

            ToolStripMenuItem openFolderItem = new ToolStripMenuItem("Открыть папку");
            openFolderItem.Click += (s, e) => OpenFolder(folderGrid);
            folderMenu.Items.Add(openFolderItem);

            ToolStripMenuItem deleteFolderItem = new ToolStripMenuItem("Удалить файл");
            deleteFolderItem.ForeColor = Color.FromArgb(255, 150, 150);
            deleteFolderItem.Click += (s, e) => DeleteSelected(folderGrid);
            folderMenu.Items.Add(deleteFolderItem);

            folderGrid.ContextMenuStrip = folderMenu;

            // ============================================================
            // ГРИД CMDLINE И APPINIT_DLLS (объединенный)
            // ============================================================
            systemParamsGrid = CreateDarkGrid();
            systemParamsGrid.Columns.Clear();
            systemParamsGrid.Columns.Add("Parameter", "Параметр");
            systemParamsGrid.Columns.Add("Value", "Значение");
            systemParamsGrid.Columns.Add("Source", "Источник");
            systemParamsGrid.Columns[0].Width = 220;
            systemParamsGrid.Columns[1].Width = 500;
            systemParamsGrid.Columns[2].Width = 80;

            ContextMenuStrip systemMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };

            ToolStripMenuItem editSystemItem = new ToolStripMenuItem("Изменить значение");
            editSystemItem.Click += (s, e) => EditValue(systemParamsGrid);
            systemMenu.Items.Add(editSystemItem);

            ToolStripMenuItem resetSystemItem = new ToolStripMenuItem("Сбросить");
            resetSystemItem.ForeColor = Color.FromArgb(255, 150, 150);
            resetSystemItem.Click += (s, e) => DeleteSelected(systemParamsGrid);
            systemMenu.Items.Add(resetSystemItem);

            systemParamsGrid.ContextMenuStrip = systemMenu;

            // ============================================================
            // ГРИД ПЛАНИРОВЩИКА ЗАДАЧ
            // ============================================================
            taskGrid = CreateDarkGrid();
            taskGrid.Columns.Clear();
            taskGrid.Columns.Add("TaskName", "Имя задачи");
            taskGrid.Columns.Add("Action", "Действие/Путь");
            taskGrid.Columns.Add("State", "Состояние");
            taskGrid.Columns.Add("Triggers", "Триггеры");
            taskGrid.Columns[0].Width = 200;
            taskGrid.Columns[1].Width = 300;
            taskGrid.Columns[2].Width = 100;
            taskGrid.Columns[3].Width = 200;

            // ============================================================
            // ГРИД СЛУЖБ
            // ============================================================
            servicesGrid = CreateDarkGrid();
            servicesGrid.Columns.Clear();
            servicesGrid.Columns.Add("ServiceName", "Имя службы");
            servicesGrid.Columns.Add("DisplayName", "Отображаемое имя");
            servicesGrid.Columns.Add("Status", "Статус");
            servicesGrid.Columns.Add("StartType", "Тип запуска");
            servicesGrid.Columns[0].Width = 200;
            servicesGrid.Columns[1].Width = 250;
            servicesGrid.Columns[2].Width = 100;
            servicesGrid.Columns[3].Width = 120;

            ContextMenuStrip servicesMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };
            ToolStripMenuItem startService = new ToolStripMenuItem("Запустить");
            startService.Click += (s, e) => ChangeServiceState(ServiceControllerStatus.Running);
            servicesMenu.Items.Add(startService);

            ToolStripMenuItem stopService = new ToolStripMenuItem("Остановить");
            stopService.Click += (s, e) => ChangeServiceState(ServiceControllerStatus.Stopped);
            servicesMenu.Items.Add(stopService);

            ToolStripMenuItem restartService = new ToolStripMenuItem("Перезапустить");
            restartService.Click += (s, e) => ChangeServiceState(ServiceControllerStatus.Running, true);
            servicesMenu.Items.Add(restartService);

            servicesGrid.ContextMenuStrip = servicesMenu;

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

            SelectTab(0);
        }

        private DataGridView CreateDarkGrid()
        {
            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BackgroundColor = Color.FromArgb(13, 13, 13),
                GridColor = Color.FromArgb(45, 45, 45),
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 35,
                Margin = new Padding(0),
                Padding = new Padding(0),
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None
            };
            grid.RowTemplate.Height = 30;

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(25, 25, 25);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 200, 200);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(25, 25, 25);
            grid.EnableHeadersVisualStyles = false;

            grid.DefaultCellStyle.BackColor = Color.FromArgb(18, 18, 18);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 50, 60);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(255, 255, 255);

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(14, 14, 14);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);

            grid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(13, 13, 13);
            grid.RowHeadersDefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            grid.TopLeftHeaderCell = new DataGridViewHeaderCell();
            grid.TopLeftHeaderCell.Style.BackColor = Color.FromArgb(13, 13, 13);

            return grid;
        }

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

            contentPanel.Controls.Clear();

            switch (index)
            {
                case 0: currentGrid = combinedRunGrid; break;
                case 1: currentGrid = wlGrid; break;
                case 2: currentGrid = folderGrid; break;
                case 3: currentGrid = systemParamsGrid; break;
                case 4: currentGrid = taskGrid; break;
                case 5: currentGrid = servicesGrid; break;
                default: currentGrid = null; break;
            }

            if (currentGrid != null)
            {
                Panel wrapper = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(13, 13, 13),
                    Padding = new Padding(0),
                    Margin = new Padding(0)
                };
                currentGrid.Dock = DockStyle.Fill;
                currentGrid.BackgroundColor = Color.FromArgb(13, 13, 13);
                wrapper.Controls.Add(currentGrid);
                contentPanel.Controls.Add(wrapper);
            }
        }

        private string FormatDisplayValue(string value)
        {
            return string.IsNullOrEmpty(value) ? "(Пустое значение)" : value;
        }

        private void LoadData()
        {
            // ============================================================
            // ОБЪЕДИНЕННАЯ ЗАГРУЗКА RUN + RUNONCE
            // ============================================================
            combinedRunGrid.Rows.Clear();
            foreach (AutostartItem item in RegistryHelper.GetRunItems())
                combinedRunGrid.Rows.Add(item.Name, item.Path, "Run", item.Exists ? "Да" : "Нет");
            foreach (AutostartItem item in RegistryHelper.GetRunOnceItems())
                combinedRunGrid.Rows.Add(item.Name, item.Path, "RunOnce", item.Exists ? "Да" : "Нет");

            wlGrid.Rows.Clear();
            foreach (AutostartItem item in RegistryHelper.GetWinlogonItems())
                wlGrid.Rows.Add(item.Name, FormatDisplayValue(item.Path));

            // ============================================================
            // ЗАГРУЗКА STARTUP
            // ============================================================
            folderGrid.Rows.Clear();
            foreach (AutostartItem item in RegistryHelper.GetStartupFolderItems())
                folderGrid.Rows.Add(item.Name, item.Path, item.Exists ? "Да" : "Нет");

            // ============================================================
            // ЗАГРУЗКА CMDLINE И APPINIT (СКРЫТЫ 2 ПАРАМЕТРА)
            // ============================================================
            systemParamsGrid.Rows.Clear();
            foreach (CmdLineItem item in RegistryHelper.GetCmdLineAutoRunItems())
                systemParamsGrid.Rows.Add(item.Name, FormatDisplayValue(item.Path), "CmdLine");

            foreach (AppInitItem item in RegistryHelper.GetAppInitDllsItems())
            {
                // Скрываем LoadAppInit_DLLs и RequireSignedAppInit_DLLs
                if (item.Name == "LoadAppInit_DLLs" || item.Name == "RequireSignedAppInit_DLLs")
                    continue;

                systemParamsGrid.Rows.Add(item.Name, FormatDisplayValue(item.Path), "AppInit");
            }

            taskGrid.Rows.Clear();
            foreach (TaskItem item in RegistryHelper.GetTaskSchedulerItems())
                taskGrid.Rows.Add(item.TaskName, item.Action, item.State, item.Triggers);

            servicesGrid.Rows.Clear();
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                foreach (ServiceController svc in services)
                {
                    try
                    {
                        string status = svc.Status == ServiceControllerStatus.Running ? "Запущена" :
                                        svc.Status == ServiceControllerStatus.Stopped ? "Остановлена" :
                                        svc.Status == ServiceControllerStatus.Paused ? "Приостановлена" : "Неизвестно";
                        string startType = "Неизвестно";
                        try
                        {
                            using (ManagementObject obj = new ManagementObject($"Win32_Service.Name='{svc.ServiceName}'"))
                            {
                                obj.Get();
                                object val = obj["StartMode"];
                                if (val != null)
                                {
                                    switch (val.ToString())
                                    {
                                        case "Auto": startType = "Автоматически"; break;
                                        case "Manual": startType = "Вручную"; break;
                                        case "Disabled": startType = "Отключена"; break;
                                        default: startType = val.ToString(); break;
                                    }
                                }
                            }
                        }
                        catch { }

                        servicesGrid.Rows.Add(svc.ServiceName, svc.DisplayName, status, startType);
                    }
                    catch { }
                }
            }
            catch { MessageBox.Show("Не удалось загрузить список служб.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void OpenFolder(DataGridView grid)
        {
            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("Выберите запись", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (grid != folderGrid)
            {
                MessageBox.Show("Открытие папки недоступно для этой вкладки.", "Инфо", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                string path = grid.SelectedRows[0].Cells[1].Value?.ToString() ?? "";
                if (!string.IsNullOrEmpty(path) && path != "(Пустое значение)")
                {
                    string folder = System.IO.Path.GetDirectoryName(path.Replace("\"", ""));
                    if (System.IO.Directory.Exists(folder))
                        System.Diagnostics.Process.Start(folder);
                    else
                        MessageBox.Show("Папка не найдена", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch { }
        }

        private void EditValue(DataGridView grid)
        {
            if (grid != wlGrid && grid != systemParamsGrid)
                return;

            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("Выберите запись", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DataGridViewRow row = grid.SelectedRows[0];
            if (row.Cells.Count < 2) return;

            string paramName = row.Cells[0].Value?.ToString() ?? "";
            string currentValue = row.Cells[1].Value?.ToString() ?? "";
            string source = row.Cells.Count > 2 ? row.Cells[2].Value?.ToString() : "";

            if (currentValue == "(Пустое значение)")
                currentValue = "";

            using (Form inputDialog = new Form())
            {
                inputDialog.Text = $"Изменить значение: {paramName}";
                inputDialog.Size = new Size(600, 150);
                inputDialog.StartPosition = FormStartPosition.CenterParent;
                inputDialog.BackColor = Color.FromArgb(13, 13, 13);
                inputDialog.ForeColor = Color.FromArgb(216, 216, 216);

                TextBox textBox = new TextBox
                {
                    Text = currentValue,
                    Location = new Point(10, 10),
                    Size = new Size(560, 30),
                    BackColor = Color.FromArgb(22, 22, 22),
                    ForeColor = Color.FromArgb(216, 216, 216),
                    BorderStyle = BorderStyle.FixedSingle
                };

                Button okButton = new Button
                {
                    Text = "OK",
                    Location = new Point(490, 60),
                    Size = new Size(80, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(35, 55, 75),
                    ForeColor = Color.FromArgb(240, 240, 240),
                    DialogResult = DialogResult.OK
                };

                Button cancelButton = new Button
                {
                    Text = "Отмена",
                    Location = new Point(400, 60),
                    Size = new Size(80, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(22, 22, 22),
                    ForeColor = Color.FromArgb(170, 170, 170),
                    DialogResult = DialogResult.Cancel
                };

                inputDialog.Controls.AddRange(new Control[] { textBox, okButton, cancelButton });
                inputDialog.AcceptButton = okButton;
                inputDialog.CancelButton = cancelButton;

                if (inputDialog.ShowDialog() == DialogResult.OK)
                {
                    string newValue = textBox.Text;
                    bool result = false;

                    try
                    {
                        if (grid == wlGrid)
                            result = RegistryHelper.SetWinlogonValue(paramName, newValue);
                        else if (grid == systemParamsGrid)
                        {
                            if (source == "CmdLine")
                                result = RegistryHelper.SetCmdLineValue(paramName, newValue);
                            else if (source == "AppInit")
                                result = RegistryHelper.SetAppInitDllsValue(paramName, newValue);
                        }

                        if (result)
                        {
                            LoadData();
                            MessageBox.Show("Значение успешно изменено", "Успешно", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("Не удалось изменить значение", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void DeleteSelected(DataGridView grid)
        {
            if (grid == null || grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("Выберите запись", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DataGridViewRow row = grid.SelectedRows[0];
            if (row.Cells.Count < 1) return;

            string name = row.Cells[0].Value?.ToString() ?? "";
            string path = row.Cells.Count > 1 ? row.Cells[1].Value?.ToString() ?? "" : "";
            string source = row.Cells.Count > 2 ? row.Cells[2].Value?.ToString() : "";

            if (path == "(Пустое значение)")
                path = "";

            if (MessageBox.Show($"Удалить / Сбросить '{name}'?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;

            bool isAdmin = IsAdministrator();
            if (!isAdmin)
            {
                MessageBox.Show("Для этого действия требуются права администратора.\nПрограмма будет перезапущена с правами администратора.",
                    "Недостаточно прав", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                RestartAsAdmin();
                return;
            }

            bool result = false;

            try
            {
                if (grid == combinedRunGrid)
                {
                    string type = row.Cells[2].Value?.ToString() ?? "Run";
                    if (type == "Run")
                        result = RegistryHelper.DeleteRunItem(name, "Run");
                    else
                        result = RegistryHelper.DeleteRunItem(name, "RunOnce");
                }
                else if (grid == folderGrid)
                {
                    result = RegistryHelper.DeleteFileItem(path);
                }
                else if (grid == wlGrid)
                {
                    if (name == "Shell")
                        result = RegistryHelper.SetWinlogonValue(name, "explorer.exe");
                    else if (name == "Userinit")
                        result = RegistryHelper.SetWinlogonValue(name, "C:\\Windows\\system32\\userinit.exe,");
                    else
                        result = RegistryHelper.SetWinlogonValue(name, "");
                }
                else if (grid == systemParamsGrid)
                {
                    if (source == "CmdLine")
                        result = RegistryHelper.SetCmdLineValue(name, "");
                    else if (source == "AppInit")
                        result = RegistryHelper.SetAppInitDllsValue(name, "");
                }
                else if (grid == taskGrid)
                    result = RegistryHelper.DeleteTaskSchedulerTask(name);
                else if (grid == servicesGrid)
                {
                    result = ChangeServiceStateByName(name, ServiceControllerStatus.Stopped);
                }

                if (result)
                {
                    LoadData();
                    MessageBox.Show($"Запись '{name}' успешно удалена/сброшена.", "Успешно", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("Ошибка удаления/сброса.\nВозможно, запись уже была удалена, или у вас недостаточно прав.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ СЛУЖБ
        // ============================================================
        private void ChangeServiceState(ServiceControllerStatus targetState, bool restart = false)
        {
            if (servicesGrid.SelectedRows.Count == 0) return;

            string serviceName = servicesGrid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(serviceName)) return;

            if (restart)
            {
                if (MessageBox.Show($"Перезапустить службу '{serviceName}'?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;
            }
            else
            {
                string action = targetState == ServiceControllerStatus.Running ? "запустить" : "остановить";
                if (MessageBox.Show($"Вы уверены, что хотите {action} службу '{serviceName}'?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;
            }

            try
            {
                using (ServiceController svc = new ServiceController(serviceName))
                {
                    if (restart && svc.Status == ServiceControllerStatus.Running)
                    {
                        svc.Stop();
                        svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        svc.Start();
                    }
                    else if (targetState == ServiceControllerStatus.Running && svc.Status != ServiceControllerStatus.Running)
                    {
                        svc.Start();
                    }
                    else if (targetState == ServiceControllerStatus.Stopped && svc.Status != ServiceControllerStatus.Stopped)
                    {
                        svc.Stop();
                    }
                    svc.WaitForStatus(targetState, TimeSpan.FromSeconds(10));
                    LoadData();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка управления службой: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ChangeServiceStateByName(string serviceName, ServiceControllerStatus targetState)
        {
            try
            {
                using (ServiceController svc = new ServiceController(serviceName))
                {
                    if (targetState == ServiceControllerStatus.Stopped && svc.Status != ServiceControllerStatus.Stopped)
                    {
                        svc.Stop();
                        svc.WaitForStatus(targetState, TimeSpan.FromSeconds(10));
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        private bool IsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private void RestartAsAdmin()
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                System.Diagnostics.Process.Start(startInfo);
                Application.Exit();
            }
            catch
            {
                MessageBox.Show("Пользователь отклонил запрос на повышение прав. Действие невозможно.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}