using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class TaskManagerForm : UserControl
    {
        private DataGridView taskGrid;
        private CheckBox autoRefresh;
        private ComboBox refreshCombo;
        private Label statsLabel;
        private TextBox searchBox;
        private System.Windows.Forms.Timer refreshTimer;
        private bool isWinRE;
        private HashSet<int> frozenPids = new HashSet<int>();
        private bool isLoading = false;
        private int savedFirstDisplayedRow = 0;
        private int savedSelectedRowIndex = -1;

        private PerformanceCounter cpuCounter;
        private PerformanceCounter ramCounter;

        private ServiceController serviceController = new ServiceController();
        private DataGridView servicesGrid;
        private TabControl taskTabControl;

        private List<ProcessInfo> cachedProcesses = new List<ProcessInfo>();

        public TaskManagerForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);

            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                cpuCounter.NextValue();
                ramCounter.NextValue();
            }
            catch { }

            InitializeComponent();
            LoadTasksAsync();

            this.KeyDown += OnKeyDown;
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = "Диспетчер задач",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(title, 0, 0);

            var ctrlPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Height = 30
            };

            autoRefresh = new CheckBox
            {
                Text = "Авто-обновление:",
                ForeColor = Color.FromArgb(216, 216, 216),
                Font = new Font("Segoe UI", 11),
                AutoSize = true,
                BackColor = Color.Transparent,
                Checked = true
            };
            autoRefresh.CheckedChanged += (s, e) => ToggleAutoRefresh();
            ctrlPanel.Controls.Add(autoRefresh);

            refreshCombo = new ComboBox
            {
                Items = { "2 сек", "5 сек", "10 сек", "30 сек" },
                SelectedIndex = 1,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 75,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.FromArgb(216, 216, 216),
                FlatStyle = FlatStyle.Flat
            };
            refreshCombo.SelectedIndexChanged += (s, e) => UpdateTimer();
            ctrlPanel.Controls.Add(refreshCombo);

            statsLabel = new Label
            {
                Text = "Загрузка...",
                ForeColor = Color.FromArgb(102, 102, 102),
                Font = new Font("Segoe UI", 11),
                AutoSize = true,
                Margin = new Padding(20, 0, 0, 0),
                BackColor = Color.Transparent
            };
            ctrlPanel.Controls.Add(statsLabel);

            layout.Controls.Add(ctrlPanel, 0, 1);

            searchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 11)
            };
            searchBox.Text = "Поиск по имени процесса...";
            searchBox.ForeColor = Color.Gray;
            searchBox.GotFocus += (s, e) => { if (searchBox.Text == "Поиск по имени процесса...") { searchBox.Text = ""; searchBox.ForeColor = Color.FromArgb(216, 216, 216); } };
            searchBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(searchBox.Text)) { searchBox.Text = "Поиск по имени процесса..."; searchBox.ForeColor = Color.Gray; } };
            searchBox.TextChanged += (s, e) => FilterTasks();
            layout.Controls.Add(searchBox, 0, 2);

            taskTabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216)
            };

            taskGrid = CreateProcessGrid();
            var procTab = new TabPage("Процессы");
            procTab.BackColor = Color.FromArgb(13, 13, 13);
            procTab.ForeColor = Color.FromArgb(216, 216, 216);
            procTab.Controls.Add(taskGrid);
            taskTabControl.TabPages.Add(procTab);

            servicesGrid = CreateServicesGrid();
            var svcTab = new TabPage("Службы");
            svcTab.BackColor = Color.FromArgb(13, 13, 13);
            svcTab.ForeColor = Color.FromArgb(216, 216, 216);
            svcTab.Controls.Add(servicesGrid);
            taskTabControl.TabPages.Add(svcTab);

            layout.Controls.Add(taskTabControl, 0, 3);

            this.Controls.Add(layout);

            ToggleAutoRefresh();
            LoadServices();
        }

        private DataGridView CreateProcessGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(22, 22, 22),
                ForeColor = Color.FromArgb(216, 216, 216),
                BackgroundColor = Color.FromArgb(22, 22, 22),
                GridColor = Color.FromArgb(45, 45, 45),
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 30,
                RowTemplate = { Height = 25 }
            };

            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null, grid, new object[] { true });

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 30, 30);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 200, 200);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);

            grid.DefaultCellStyle.BackColor = Color.FromArgb(22, 22, 22);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 50, 60);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(255, 255, 255);
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9);

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(17, 17, 17);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);

            grid.Columns.Add("PID", "PID");
            grid.Columns.Add("Name", "Процесс");
            grid.Columns.Add("Memory", "Память МБ");
            grid.Columns.Add("CPU", "CPU %");
            grid.Columns.Add("Status", "Статус");
            grid.Columns.Add("Critical", "Критичность");
            grid.Columns[0].Width = 55;
            grid.Columns[2].Width = 85;
            grid.Columns[3].Width = 65;
            grid.Columns[4].Width = 85;
            grid.Columns[5].Width = 110;

            grid.ContextMenuStrip = CreateContextMenu();
            return grid;
        }

        private DataGridView CreateServicesGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(22, 22, 22),
                ForeColor = Color.FromArgb(216, 216, 216),
                BackgroundColor = Color.FromArgb(22, 22, 22),
                GridColor = Color.FromArgb(45, 45, 45),
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 30,
                RowTemplate = { Height = 25 }
            };

            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null, grid, new object[] { true });

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 30, 30);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 200, 200);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);

            grid.DefaultCellStyle.BackColor = Color.FromArgb(22, 22, 22);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 50, 60);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(255, 255, 255);
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9);

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(17, 17, 17);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);

            grid.Columns.Add("Name", "Имя");
            grid.Columns.Add("Display", "Отображаемое имя");
            grid.Columns.Add("Status", "Статус");
            grid.Columns.Add("Start", "Тип запуска");

            grid.ContextMenuStrip = CreateServiceContextMenu();
            return grid;
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(17, 17, 17);
            menu.ForeColor = Color.FromArgb(216, 216, 216);

            var killItem = new ToolStripMenuItem("Завершить (Delete)");
            killItem.Click += (s, e) => KillSelected();
            menu.Items.Add(killItem);

            var locItem = new ToolStripMenuItem("Открыть расположение");
            locItem.Click += (s, e) => OpenLocation();
            menu.Items.Add(locItem);

            var propsItem = new ToolStripMenuItem("Свойства");
            propsItem.Click += (s, e) => ShowProperties();
            menu.Items.Add(propsItem);

            menu.Items.Add(new ToolStripSeparator());

            var freezeItem = new ToolStripMenuItem("Заморозить (F)");
            freezeItem.Click += (s, e) => FreezeSelected();
            menu.Items.Add(freezeItem);

            var unfreezeItem = new ToolStripMenuItem("Разморозить (F)");
            unfreezeItem.Click += (s, e) => UnfreezeSelected();
            menu.Items.Add(unfreezeItem);

            menu.Items.Add(new ToolStripSeparator());

            var critItem = new ToolStripMenuItem("Сделать критичным");
            critItem.Click += (s, e) => SetCriticalSelected(true);
            menu.Items.Add(critItem);

            var uncritItem = new ToolStripMenuItem("Снять критичность");
            uncritItem.Click += (s, e) => SetCriticalSelected(false);
            menu.Items.Add(uncritItem);

            return menu;
        }

        private ContextMenuStrip CreateServiceContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(17, 17, 17);
            menu.ForeColor = Color.FromArgb(216, 216, 216);

            var startItem = new ToolStripMenuItem("Запустить");
            startItem.Click += (s, e) => StartSelectedService();
            menu.Items.Add(startItem);

            var stopItem = new ToolStripMenuItem("Остановить");
            stopItem.Click += (s, e) => StopSelectedService();
            menu.Items.Add(stopItem);

            menu.Items.Add(new ToolStripSeparator());

            var deleteItem = new ToolStripMenuItem("Удалить");
            deleteItem.ForeColor = Color.FromArgb(255, 150, 150);
            deleteItem.Click += (s, e) => DeleteSelectedService();
            menu.Items.Add(deleteItem);

            return menu;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (taskGrid.SelectedRows.Count == 0) return;

            switch (e.KeyCode)
            {
                case Keys.Delete:
                    KillSelected();
                    e.Handled = true;
                    break;
                case Keys.F:
                    int pid = GetSelectedPID();
                    if (pid >= 0)
                    {
                        if (frozenPids.Contains(pid))
                            UnfreezeSelected();
                        else
                            FreezeSelected();
                        e.Handled = true;
                    }
                    break;
            }
        }

        private int GetSelectedPID()
        {
            if (taskGrid.SelectedRows.Count == 0) return -1;
            var val = taskGrid.SelectedRows[0].Cells[0].Value;
            return val != null ? int.Parse(val.ToString()) : -1;
        }

        private string GetSelectedName()
        {
            if (taskGrid.SelectedRows.Count == 0) return "";
            return taskGrid.SelectedRows[0].Cells[1].Value?.ToString() ?? "";
        }

        private async void LoadTasksAsync()
        {
            if (isLoading) return;
            isLoading = true;

            try
            {
                if (taskGrid.FirstDisplayedScrollingRowIndex >= 0)
                {
                    savedFirstDisplayedRow = taskGrid.FirstDisplayedScrollingRowIndex;
                }

                savedSelectedRowIndex = -1;
                if (taskGrid.SelectedRows.Count > 0)
                {
                    savedSelectedRowIndex = taskGrid.SelectedRows[0].Index;
                }

                var processes = await Task.Run(() => ProcessHelper.GetProcessesFast());
                cachedProcesses = processes;

                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                    {
                        UpdateProcessGrid(processes);
                        FilterTasks();

                        if (savedFirstDisplayedRow > 0 && savedFirstDisplayedRow < taskGrid.Rows.Count)
                        {
                            taskGrid.FirstDisplayedScrollingRowIndex = savedFirstDisplayedRow;
                        }

                        if (savedSelectedRowIndex >= 0 && savedSelectedRowIndex < taskGrid.Rows.Count)
                        {
                            taskGrid.Rows[savedSelectedRowIndex].Selected = true;
                        }
                    }));
                }
                else
                {
                    UpdateProcessGrid(processes);
                    FilterTasks();

                    if (savedFirstDisplayedRow > 0 && savedFirstDisplayedRow < taskGrid.Rows.Count)
                    {
                        taskGrid.FirstDisplayedScrollingRowIndex = savedFirstDisplayedRow;
                    }

                    if (savedSelectedRowIndex >= 0 && savedSelectedRowIndex < taskGrid.Rows.Count)
                    {
                        taskGrid.Rows[savedSelectedRowIndex].Selected = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки: {ex.Message}");
            }
            finally
            {
                isLoading = false;
            }
        }

        private void UpdateProcessGrid(List<ProcessInfo> processes)
        {
            taskGrid.Rows.Clear();

            var criticalKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "csrss", "smss", "wininit", "services", "lsass", "lsm",
                "winlogon", "system", "system idle process", "registry"
            };

            foreach (var proc in processes)
            {
                try
                {
                    bool isFrz = frozenPids.Contains(proc.PID);

                    bool isCritical = false;
                    string nameLower = proc.Name.ToLower();
                    foreach (var kw in criticalKeywords)
                    {
                        if (nameLower == kw || nameLower.Contains(kw))
                        {
                            isCritical = true;
                            break;
                        }
                    }

                    var row = new DataGridViewRow();
                    row.CreateCells(taskGrid, proc.PID, proc.Name, proc.MemoryMB, 0, proc.Status);

                    var critCell = new DataGridViewTextBoxCell();
                    critCell.Value = isCritical ? "КРИТИЧНЫЙ" : "Обычный";

                    if (isCritical)
                    {
                        critCell.Style.BackColor = Color.FromArgb(30, 15, 15);
                        critCell.Style.ForeColor = Color.FromArgb(255, 80, 80);
                        critCell.Style.Font = new Font("Segoe UI", 9, FontStyle.Bold);
                        for (int i = 0; i < 5; i++)
                        {
                            row.Cells[i].Style.BackColor = Color.FromArgb(30, 15, 15);
                            row.Cells[i].Style.ForeColor = Color.FromArgb(255, 170, 170);
                        }
                    }
                    else
                    {
                        critCell.Style.BackColor = Color.FromArgb(15, 30, 20);
                        critCell.Style.ForeColor = Color.FromArgb(153, 221, 187);
                    }
                    critCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    row.Cells[5] = critCell;

                    if (isFrz)
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            row.Cells[i].Style.BackColor = Color.FromArgb(10, 26, 42);
                            row.Cells[i].Style.ForeColor = Color.FromArgb(68, 136, 255);
                        }
                    }

                    if (isCritical && isFrz)
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            row.Cells[i].Style.BackColor = Color.FromArgb(30, 15, 15);
                            row.Cells[i].Style.ForeColor = Color.FromArgb(255, 170, 170);
                        }
                    }

                    taskGrid.Rows.Add(row);
                }
                catch { }
            }

            UpdateStats(processes);
        }

        private void UpdateStats(List<ProcessInfo> processes)
        {
            int total = processes.Count;
            int crit = 0, frz = 0;

            var criticalKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "csrss", "smss", "wininit", "services", "lsass", "lsm",
                "winlogon", "system", "system idle process", "registry"
            };

            foreach (var proc in processes)
            {
                if (frozenPids.Contains(proc.PID)) frz++;

                bool isCritical = false;
                string nameLower = proc.Name.ToLower();
                foreach (var kw in criticalKeywords)
                {
                    if (nameLower == kw || nameLower.Contains(kw))
                    {
                        isCritical = true;
                        break;
                    }
                }
                if (isCritical) crit++;
            }

            try
            {
                float cpu = cpuCounter?.NextValue() ?? 0;
                float ram = ramCounter?.NextValue() ?? 0;
                statsLabel.Text = $"Процессов: {total}   Критичных: {crit}   Заморожено: {frz}   CPU: {cpu:F1}%   RAM: {ram:F0} MB";
            }
            catch
            {
                statsLabel.Text = $"Процессов: {total}   Критичных: {crit}   Заморожено: {frz}";
            }
        }

        private void FilterTasks()
        {
            var text = searchBox.Text?.ToLower() ?? "";
            if (text == "поиск по имени процесса..." || string.IsNullOrEmpty(text))
            {
                foreach (DataGridViewRow row in taskGrid.Rows)
                    row.Visible = true;
                return;
            }

            foreach (DataGridViewRow row in taskGrid.Rows)
            {
                if (row.Cells[1].Value == null) continue;
                var name = row.Cells[1].Value.ToString().ToLower();
                row.Visible = name.Contains(text);
            }
        }

        private void ToggleAutoRefresh()
        {
            if (autoRefresh.Checked)
            {
                if (refreshTimer == null)
                {
                    refreshTimer = new System.Windows.Forms.Timer();
                    refreshTimer.Tick += (s, e) => LoadTasksAsync();
                }
                refreshTimer.Interval = GetInterval();
                refreshTimer.Start();
            }
            else
            {
                refreshTimer?.Stop();
            }
        }

        private void UpdateTimer()
        {
            if (refreshTimer != null && refreshTimer.Enabled)
            {
                refreshTimer.Interval = GetInterval();
            }
        }

        private int GetInterval()
        {
            int idx = refreshCombo.SelectedIndex;
            if (idx == 0) return 2000;
            if (idx == 1) return 5000;
            if (idx == 2) return 10000;
            if (idx == 3) return 30000;
            return 5000;
        }

        private void KillSelected()
        {
            var pid = GetSelectedPID();
            if (pid < 0) return;
            var name = GetSelectedName();

            if (MessageBox.Show($"Завершить процесс '{name}' (PID {pid})?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try
                {
                    ProcessHelper.KillProcess(pid);
                    frozenPids.Remove(pid);
                    LoadTasksAsync();
                    MessageBox.Show($"Процесс {name} завершён", "Успешно",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось завершить процесс: {ex.Message}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenLocation()
        {
            var pid = GetSelectedPID();
            if (pid < 0) return;

            try
            {
                var proc = Process.GetProcessById(pid);
                var path = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                {
                    var folder = System.IO.Path.GetDirectoryName(path);
                    if (System.IO.Directory.Exists(folder))
                        Process.Start(folder);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowProperties()
        {
            var pid = GetSelectedPID();
            if (pid < 0) return;

            try
            {
                var proc = Process.GetProcessById(pid);
                var path = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void FreezeSelected()
        {
            var pid = GetSelectedPID();
            if (pid < 0) return;
            var name = GetSelectedName();

            ProcessHelper.FreezeProcess(pid);
            frozenPids.Add(pid);
            LoadTasksAsync();
            MessageBox.Show($"Процесс {name} заморожен", "Заморожен",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UnfreezeSelected()
        {
            var pid = GetSelectedPID();
            if (pid < 0) return;
            var name = GetSelectedName();

            ProcessHelper.UnfreezeProcess(pid);
            frozenPids.Remove(pid);
            LoadTasksAsync();
            MessageBox.Show($"Процесс {name} разморожен", "Разморожен",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ============================================================
        // УСТАНОВКА КРИТИЧНОСТИ
        // ============================================================
        private void SetCriticalSelected(bool critical)
        {
            var pid = GetSelectedPID();
            if (pid < 0) return;
            var name = GetSelectedName();

            if (!ProcessHelper.IsAdministrator())
            {
                MessageBox.Show(
                    "Для изменения критичности процесса требуются права администратора!\n\n" +
                    "Перезапустите программу от имени администратора.",
                    "Требуются права", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string warning = critical ?
                "⚠️ ПРОЦЕСС СТАНЕТ КРИТИЧНЫМ ДЛЯ ВСЕЙ СИСТЕМЫ!\n" +
                "ЕГО ЗАВЕРШЕНИЕ ВЫЗОВЕТ BSOD (синий экран смерти)!" :
                "Критичность будет снята.";

            if (pid == Process.GetCurrentProcess().Id)
            {
                warning += "\n\nВНИМАНИЕ: Вы изменяете критичность ТЕКУЩЕЙ ПРОГРАММЫ!";
            }

            DialogResult confirm = MessageBox.Show(
                $"{(critical ? "Установить" : "Снять")} критичность с '{name}' (PID: {pid})?\n\n" + warning,
                "Подтверждение",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            try
            {
                bool result = ProcessHelper.SetSystemCritical(pid, critical);

                if (result)
                {
                    ProcessHelper.ClearCache();
                    LoadTasksAsync();
                    MessageBox.Show($"{(critical ? "Критичность установлена" : "Критичность снята")} для {name}",
                        "Успешно", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadServices()
        {
            if (servicesGrid == null) return;

            servicesGrid.Rows.Clear();
            try
            {
                foreach (var service in ServiceController.GetServices())
                {
                    try
                    {
                        servicesGrid.Rows.Add(
                            service.ServiceName,
                            service.DisplayName,
                            service.Status.ToString(),
                            GetServiceStartType(service.ServiceName)
                        );
                    }
                    catch { }
                }
            }
            catch { }
        }

        private string GetServiceStartType(string name)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Services\\{name}"))
                {
                    if (key != null)
                    {
                        var start = key.GetValue("Start") as int?;
                        if (start.HasValue)
                        {
                            if (start.Value == 0) return "Загрузочная";
                            if (start.Value == 1) return "Системная";
                            if (start.Value == 2) return "Авто";
                            if (start.Value == 3) return "Вручную";
                            if (start.Value == 4) return "Отключена";
                        }
                    }
                }
            }
            catch { }
            return "Неизвестно";
        }

        private void StartSelectedService()
        {
            if (servicesGrid.SelectedRows.Count == 0) return;
            var name = servicesGrid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return;

            try
            {
                serviceController.ServiceName = name;
                serviceController.Start();
                serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                LoadServices();
                MessageBox.Show($"Служба {name} запущена", "Успешно",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска службы: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopSelectedService()
        {
            if (servicesGrid.SelectedRows.Count == 0) return;
            var name = servicesGrid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return;

            try
            {
                serviceController.ServiceName = name;
                serviceController.Stop();
                serviceController.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                LoadServices();
                MessageBox.Show($"Служба {name} остановлена", "Успешно",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка остановки службы: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteSelectedService()
        {
            if (servicesGrid.SelectedRows.Count == 0) return;
            var name = servicesGrid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return;

            if (MessageBox.Show($"Удалить службу '{name}'?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try
                {
                    try
                    {
                        serviceController.ServiceName = name;
                        serviceController.Stop();
                        serviceController.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                    catch { }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = $"delete \"{name}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    using (var process = Process.Start(startInfo))
                    {
                        process.WaitForExit();
                    }

                    LoadServices();
                    MessageBox.Show($"Служба {name} удалена", "Успешно",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления службы: {ex.Message}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public new void Dispose()
        {
            try
            {
                cpuCounter?.Dispose();
                ramCounter?.Dispose();
                refreshTimer?.Stop();
                refreshTimer?.Dispose();
                ProcessHelper.ClearCache();
            }
            catch { }
            base.Dispose();
        }
    }
}