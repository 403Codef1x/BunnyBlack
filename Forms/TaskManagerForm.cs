// language: C#, file: Forms/TaskManagerForm.cs
// Полная замена.
// - Критичность работает на всех обычных процессах, кроме системных PPL.
// - Список PPL-процессов пропускается сразу, без попыток установки.
// - Ячейка Critical обновляется мгновенно, не ждёт таймера.
// - Дерево процессов, CommandLine, Company Name — как было.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Management;
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
        private int _lastSelectedPid = -1;

        private ServiceController serviceController = new ServiceController();
        private DataGridView servicesGrid;
        private TabControl taskTabControl;

        private readonly Dictionary<int, DataGridViewRow> rowByPid = new Dictionary<int, DataGridViewRow>();
        private readonly HashSet<int> realCriticalPids = new HashSet<int>();

        private readonly Dictionary<int, string> pathCache = new Dictionary<int, string>();
        private readonly Dictionary<int, string> cmdCache = new Dictionary<int, string>();
        private readonly Dictionary<int, string> companyCache = new Dictionary<int, string>();

        private readonly HashSet<string> criticalKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "smss", "wininit", "services", "lsass", "lsm",
            "winlogon", "system", "system idle process", "registry", "memory compression"
        };

        // PPL-процессы — критичность на них не работает
        private readonly HashSet<string> protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "system idle process", "registry", "memory compression",
            "smss", "csrss", "wininit", "services", "lsass", "lsaiso",
            "fontdrvhost", "secure system", "svchost"
        };

        public TaskManagerForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);

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
                RowCount = 3,
                Padding = new Padding(20, 18, 20, 16),
                BackColor = Color.FromArgb(13, 13, 13)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var ctrlPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                WrapContents = false
            };

            autoRefresh = new CheckBox
            {
                Text = "Авто-обновление:",
                ForeColor = Color.FromArgb(216, 216, 216),
                Font = new Font("Segoe UI", 10),
                AutoSize = true,
                BackColor = Color.Transparent,
                Checked = true,
                Margin = new Padding(0, 6, 6, 0)
            };
            autoRefresh.CheckedChanged += (s, e) => ToggleAutoRefresh();
            ctrlPanel.Controls.Add(autoRefresh);

            refreshCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 75,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.FromArgb(216, 216, 216),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9),
                Margin = new Padding(0, 4, 16, 0)
            };
            refreshCombo.Items.AddRange(new object[] { "2 сек", "5 сек", "10 сек", "30 сек" });
            refreshCombo.SelectedIndex = 1;
            refreshCombo.SelectedIndexChanged += (s, e) => UpdateTimer();
            ctrlPanel.Controls.Add(refreshCombo);

            statsLabel = new Label
            {
                Text = "Загрузка...",
                ForeColor = Color.FromArgb(140, 140, 140),
                Font = new Font("Segoe UI", 10),
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 6, 0, 0)
            };
            ctrlPanel.Controls.Add(statsLabel);
            layout.Controls.Add(ctrlPanel, 0, 0);

            searchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10),
                Text = "Поиск по имени/пути/командной строке..."
            };
            searchBox.ForeColor = Color.Gray;
            searchBox.GotFocus += (s, e) => { if (searchBox.Text.StartsWith("Поиск")) { searchBox.Text = ""; searchBox.ForeColor = Color.FromArgb(216, 216, 216); } };
            searchBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(searchBox.Text)) { searchBox.Text = "Поиск по имени/пути/командной строке..."; searchBox.ForeColor = Color.Gray; } };
            searchBox.TextChanged += (s, e) => FilterTasks();
            layout.Controls.Add(searchBox, 0, 1);

            taskTabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216)
            };

            taskGrid = CreateProcessGrid();
            var procTab = new TabPage("Процессы")
            {
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216)
            };
            procTab.Controls.Add(taskGrid);
            taskTabControl.TabPages.Add(procTab);

            servicesGrid = CreateServicesGrid();
            var svcTab = new TabPage("Службы")
            {
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216)
            };
            svcTab.Controls.Add(servicesGrid);
            taskTabControl.TabPages.Add(svcTab);

            layout.Controls.Add(taskTabControl, 0, 2);
            this.Controls.Add(layout);

            ToggleAutoRefresh();
            LoadServices();
        }

        private DataGridView CreateProcessGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BackgroundColor = Color.FromArgb(13, 13, 13),
                GridColor = Color.FromArgb(40, 40, 40),
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 34,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None
            };
            grid.RowTemplate.Height = 26;

            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null, grid, new object[] { true });

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(25, 25, 25);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 200, 200);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(25, 25, 25);

            grid.DefaultCellStyle.BackColor = Color.FromArgb(13, 13, 13);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 50, 60);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(255, 255, 255);
            grid.DefaultCellStyle.Font = new Font("Consolas", 9);

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(16, 16, 16);

            grid.Columns.Add("Name", "Process Name");
            grid.Columns.Add("PID", "PID");
            grid.Columns.Add("Critical", "Critical");
            grid.Columns.Add("Company", "Company Name");
            grid.Columns.Add("CommandLine", "CommandLine");

            grid.Columns[0].Width = 380;
            grid.Columns[1].Width = 80;
            grid.Columns[2].Width = 100;
            grid.Columns[3].Width = 200;
            grid.Columns[4].Width = 640;

            grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            grid.ContextMenuStrip = CreateContextMenu();
            return grid;
        }

        private DataGridView CreateServicesGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BackgroundColor = Color.FromArgb(13, 13, 13),
                GridColor = Color.FromArgb(40, 40, 40),
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 30
            };
            grid.RowTemplate.Height = 25;

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

            grid.Columns.Add("Name", "Имя");
            grid.Columns.Add("Display", "Отображаемое имя");
            grid.Columns.Add("Status", "Статус");
            grid.Columns.Add("Start", "Тип запуска");

            grid.ContextMenuStrip = CreateServiceContextMenu();
            return grid;
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };

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
            var menu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };

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
                        if (frozenPids.Contains(pid)) UnfreezeSelected();
                        else FreezeSelected();
                        e.Handled = true;
                    }
                    break;
            }
        }

        private int GetSelectedPID()
        {
            if (taskGrid.SelectedRows.Count == 0) return -1;
            var val = taskGrid.SelectedRows[0].Cells[1].Value;
            if (val == null) return -1;
            return int.TryParse(val.ToString(), out int p) ? p : -1;
        }

        private string GetSelectedName()
        {
            if (taskGrid.SelectedRows.Count == 0) return "";
            string raw = taskGrid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            raw = raw.TrimStart(' ', '│', '├', '└', '─', ' ').Trim();
            return raw;
        }

        // ============================================================
        // ЗАГРУЗКА
        // ============================================================
        private async void LoadTasksAsync()
        {
            if (isLoading) return;
            isLoading = true;

            try
            {
                _lastSelectedPid = GetSelectedPID();

                try
                {
                    if (taskGrid.FirstDisplayedScrollingRowIndex >= 0)
                        savedFirstDisplayedRow = taskGrid.FirstDisplayedScrollingRowIndex;
                }
                catch { }

                List<ProcessInfo> processes;
                try
                {
                    processes = await Task.Run(() => ProcessHelper.GetProcessesFast());
                }
                catch (Exception ex)
                {
                    BbLog.Error("[LoadTasks/GetProcesses]", ex);
                    processes = new List<ProcessInfo>();
                }

                if (processes == null) processes = new List<ProcessInfo>();

                if (this.InvokeRequired)
                    this.Invoke(new Action(() =>
                    {
                        RebuildTree(processes);
                        FilterTasks();
                        RestoreScrollAndSelection();
                    }));
                else
                {
                    RebuildTree(processes);
                    FilterTasks();
                    RestoreScrollAndSelection();
                }

                RefreshRealCriticalAsync();
                _ = Task.Run(() => EnrichRowsAsync(processes));
            }
            catch (Exception ex)
            {
                BbLog.Error("[LoadTasks]", ex);
                if (this.InvokeRequired)
                    this.Invoke(new Action(() => statsLabel.Text = "Ошибка: " + ex.Message));
                else
                    statsLabel.Text = "Ошибка: " + ex.Message;
            }
            finally { isLoading = false; }
        }

        private void EnrichRowsAsync(List<ProcessInfo> processes)
        {
            try
            {
                foreach (var p in processes)
                {
                    if (!pathCache.ContainsKey(p.PID))
                        pathCache[p.PID] = GetProcessPath(p.PID) ?? "";

                    if (!companyCache.ContainsKey(p.PID))
                    {
                        string company = "";
                        try
                        {
                            string path = pathCache[p.PID];
                            if (!string.IsNullOrEmpty(path))
                            {
                                var vi = FileVersionInfo.GetVersionInfo(path);
                                company = vi.CompanyName ?? "";
                            }
                        }
                        catch { }
                        companyCache[p.PID] = company;
                    }

                    if (!cmdCache.ContainsKey(p.PID))
                        cmdCache[p.PID] = GetCommandLine(p.PID) ?? "";

                    int pidLocal = p.PID;
                    string compLocal = companyCache[pidLocal];
                    string cmdLocal = cmdCache[pidLocal];

                    try
                    {
                        if (this.IsHandleCreated)
                            this.BeginInvoke(new Action(() =>
                            {
                                if (rowByPid.TryGetValue(pidLocal, out var row))
                                {
                                    try
                                    {
                                        if (row.Cells.Count >= 5)
                                        {
                                            if (string.IsNullOrEmpty(row.Cells[3].Value?.ToString()))
                                                row.Cells[3].Value = compLocal;
                                            if (string.IsNullOrEmpty(row.Cells[4].Value?.ToString()))
                                                row.Cells[4].Value = cmdLocal;
                                        }
                                    }
                                    catch { }
                                }
                            }));
                    }
                    catch { }
                }
            }
            catch (Exception ex) { BbLog.Error("[EnrichRows]", ex); }
        }

        private void RestoreScrollAndSelection()
        {
            try
            {
                if (savedFirstDisplayedRow > 0 && savedFirstDisplayedRow < taskGrid.Rows.Count)
                    taskGrid.FirstDisplayedScrollingRowIndex = savedFirstDisplayedRow;

                if (_lastSelectedPid > 0)
                {
                    foreach (DataGridViewRow row in taskGrid.Rows)
                    {
                        if (row.Cells[1].Value != null &&
                            int.TryParse(row.Cells[1].Value.ToString(), out int pid) &&
                            pid == _lastSelectedPid)
                        {
                            row.Selected = true;
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private void RebuildTree(List<ProcessInfo> processes)
        {
            taskGrid.Rows.Clear();
            rowByPid.Clear();

            if (processes == null || processes.Count == 0)
            {
                statsLabel.Text = "Процессов: 0";
                return;
            }

            var childrenOf = new Dictionary<int, List<ProcessInfo>>();
            var byPid = new Dictionary<int, ProcessInfo>();

            foreach (var p in processes)
            {
                byPid[p.PID] = p;
                if (!childrenOf.ContainsKey(p.ParentPID))
                    childrenOf[p.ParentPID] = new List<ProcessInfo>();
                childrenOf[p.ParentPID].Add(p);
            }

            var hasParent = new HashSet<int>();
            foreach (var p in processes)
                if (p.ParentPID != 0 && byPid.ContainsKey(p.ParentPID))
                    hasParent.Add(p.PID);

            var roots = new List<ProcessInfo>();
            foreach (var p in processes)
                if (p.ParentPID == 0 || !hasParent.Contains(p.PID) || !byPid.ContainsKey(p.ParentPID))
                    roots.Add(p);

            roots.Sort((a, b) =>
            {
                int Rank(ProcessInfo p)
                {
                    string n = p.Name.ToLowerInvariant();
                    if (n == "system idle process" || n == "idle") return 0;
                    if (n == "system") return 1;
                    if (n == "smss") return 2;
                    if (n == "csrss") return 3;
                    if (n == "wininit") return 4;
                    if (n == "services") return 5;
                    if (n == "lsass") return 6;
                    if (n == "winlogon") return 7;
                    if (n == "explorer") return 8;
                    return 100;
                }
                int ra = Rank(a), rb = Rank(b);
                if (ra != rb) return ra.CompareTo(rb);
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            var visited = new HashSet<int>();
            foreach (var root in roots)
                AddProcessRow(root, "", childrenOf, visited);

            foreach (var p in processes)
                if (!visited.Contains(p.PID))
                    AddProcessRow(p, "", childrenOf, visited);

            UpdateStats(processes);
        }

        private void AddProcessRow(ProcessInfo p, string prefix,
            Dictionary<int, List<ProcessInfo>> childrenOf, HashSet<int> visited)
        {
            if (visited.Contains(p.PID)) return;
            visited.Add(p.PID);

            string lower = p.Name.ToLowerInvariant();
            bool isCritical = criticalKeywords.Contains(lower);

            string company = companyCache.TryGetValue(p.PID, out var c) ? c : "";
            string cmd = cmdCache.TryGetValue(p.PID, out var cl) ? cl : "";

            string display = prefix + p.Name;

            int idx = taskGrid.Rows.Add(display, p.PID, isCritical ? "Да" : "", company, cmd);
            var row = taskGrid.Rows[idx];
            row.Tag = p;
            rowByPid[p.PID] = row;

            if (isCritical)
            {
                for (int c2 = 0; c2 < 5; c2++)
                {
                    row.Cells[c2].Style.BackColor = Color.FromArgb(60, 20, 20);
                    row.Cells[c2].Style.ForeColor = Color.FromArgb(255, 180, 180);
                }
                row.Cells[2].Style.ForeColor = Color.FromArgb(255, 80, 80);
                row.Cells[2].Style.Font = new Font("Consolas", 9, FontStyle.Bold);
            }
            else
            {
                row.Cells[0].Style.ForeColor = Color.FromArgb(216, 216, 216);
                row.Cells[1].Style.ForeColor = Color.FromArgb(200, 200, 200);
                row.Cells[2].Style.ForeColor = Color.FromArgb(120, 120, 120);
                row.Cells[3].Style.ForeColor = Color.FromArgb(180, 180, 180);
                row.Cells[4].Style.ForeColor = Color.FromArgb(180, 200, 180);
            }

            if (frozenPids.Contains(p.PID))
            {
                for (int c2 = 0; c2 < 5; c2++)
                {
                    row.Cells[c2].Style.BackColor = Color.FromArgb(10, 26, 42);
                    row.Cells[c2].Style.ForeColor = Color.FromArgb(100, 180, 255);
                }
            }

            if (childrenOf.TryGetValue(p.PID, out var children))
            {
                children.RemoveAll(x => x.PID == p.PID);
                children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                for (int i = 0; i < children.Count; i++)
                {
                    bool last = (i == children.Count - 1);
                    string childPrefix = prefix.Replace("├─ ", "│  ").Replace("└─ ", "   ") +
                                         (last ? "└─ " : "├─ ");
                    AddProcessRow(children[i], childPrefix, childrenOf, visited);
                }
            }
        }

        private string GetProcessPath(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    try { return p.MainModule?.FileName ?? ""; }
                    catch { return ""; }
                }
            }
            catch { return ""; }
        }

        private string GetCommandLine(int pid)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}"))
                {
                    foreach (var o in s.Get())
                    {
                        try
                        {
                            string cl = o["CommandLine"]?.ToString();
                            if (!string.IsNullOrEmpty(cl)) return cl;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return "";
        }

        private void UpdateStats(List<ProcessInfo> processes)
        {
            int total = processes.Count;
            int crit = 0, frz = 0;

            foreach (var proc in processes)
            {
                if (frozenPids.Contains(proc.PID)) frz++;
                if (criticalKeywords.Contains(proc.Name.ToLowerInvariant())) crit++;
            }

            statsLabel.Text = $"Процессов: {total}   Критичных: {crit}   Заморожено: {frz}";
        }

        private void FilterTasks()
        {
            string text = searchBox.Text?.ToLowerInvariant() ?? "";
            if (text.StartsWith("поиск") || string.IsNullOrEmpty(text))
            {
                foreach (DataGridViewRow row in taskGrid.Rows) row.Visible = true;
                return;
            }

            foreach (DataGridViewRow row in taskGrid.Rows)
            {
                string name = row.Cells[0].Value?.ToString() ?? "";
                string cmd = row.Cells[4].Value?.ToString() ?? "";
                string comp = row.Cells[3].Value?.ToString() ?? "";
                row.Visible = name.ToLowerInvariant().Contains(text) ||
                              cmd.ToLowerInvariant().Contains(text) ||
                              comp.ToLowerInvariant().Contains(text);
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
            else refreshTimer?.Stop();
        }

        private void UpdateTimer()
        {
            if (refreshTimer != null && refreshTimer.Enabled)
                refreshTimer.Interval = GetInterval();
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

        // ============================================================
        // КРИТИЧНОСТЬ
        // ============================================================
        private void RefreshRealCriticalAsync()
        {
            Task.Run(() =>
            {
                var newSet = new HashSet<int>();
                List<int> snapshot;
                lock (rowByPid) snapshot = new List<int>(rowByPid.Keys);

                foreach (var pid in snapshot)
                {
                    try
                    {
                        if (ProcessHelper.IsProcessCritical(pid))
                            newSet.Add(pid);
                    }
                    catch { }
                }

                try
                {
                    if (this.IsHandleCreated)
                        this.Invoke(new Action(() =>
                        {
                            realCriticalPids.Clear();
                            foreach (var pid in newSet) realCriticalPids.Add(pid);
                            UpdateCriticalColumn();
                        }));
                }
                catch { }
            });
        }

        private void UpdateCriticalColumn()
        {
            foreach (var kv in rowByPid)
            {
                try
                {
                    var row = kv.Value;
                    if (row.Cells.Count < 3) continue;

                    string nameLower = (row.Cells[0].Value?.ToString() ?? "").TrimStart(' ', '│', '├', '└', '─').Trim().ToLowerInvariant();
                    bool byName = criticalKeywords.Contains(nameLower);
                    bool isReal = realCriticalPids.Contains(kv.Key);

                    if (isReal)
                    {
                        row.Cells[2].Value = "Да (real)";
                        row.Cells[2].Style.ForeColor = Color.FromArgb(255, 80, 80);
                        row.Cells[2].Style.Font = new Font("Consolas", 9, FontStyle.Bold);
                    }
                    else if (byName)
                    {
                        row.Cells[2].Value = "Да";
                        row.Cells[2].Style.ForeColor = Color.FromArgb(255, 80, 80);
                        row.Cells[2].Style.Font = new Font("Consolas", 9, FontStyle.Bold);
                    }
                    else
                    {
                        row.Cells[2].Value = "";
                        row.Cells[2].Style.ForeColor = Color.FromArgb(120, 120, 120);
                        row.Cells[2].Style.Font = new Font("Consolas", 9, FontStyle.Regular);
                    }
                }
                catch { }
            }
        }

        private void SetCriticalSelected(bool critical)
        {
            var pid = GetSelectedPID();
            if (pid <= 0)
            {
                MessageBox.Show($"Не удалось определить PID выбранного процесса (получено: {pid}).",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var name = GetSelectedName();
            string nameLower = name.ToLowerInvariant();

            if (!ProcessHelper.IsAdministrator())
            {
                MessageBox.Show("Требуются права администратора!",
                    "Требуются права", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ============================================================
            // PPL-ПРОЦЕССЫ — не трогаем
            // ============================================================
            if (protectedNames.Contains(nameLower))
            {
                MessageBox.Show(
                    $"'{name}' — системный защищённый процесс (PPL).\n\n" +
                    "Windows блокирует установку critical flag таким процессам на уровне ядра.\n\n" +
                    "Выбери обычное приложение — notepad, браузер, Яндекс Музыку — на них критичность работает.",
                    "Системный процесс", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (critical)
            {
                string warn =
                    "⚠ ВНИМАНИЕ  ⚠\n\n" +
                    $"Устанавливается critical flag на '{name}' (PID: {pid}).\n\n" +
                    "• Если процесс завершится — система уйдёт в BSOD.\n" +
                    "• Флаг снимается при перезагрузке.\n\n" +
                    "Продолжить?";

                if (MessageBox.Show(warn, "КРИТИЧНЫЙ ПРОЦЕСС",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            try
            {
                string detail;
                var result = ProcessHelper.SetSystemCritical(pid, critical, out detail);

                if (result == ProcessOpResult.Success)
                {
                    ProcessHelper.ClearCache();

                    // обновляем ячейку сразу
                    if (rowByPid.TryGetValue(pid, out var row))
                    {
                        try
                        {
                            if (critical)
                            {
                                realCriticalPids.Add(pid);
                                row.Cells[2].Value = "Да (real)";
                                row.Cells[2].Style.ForeColor = Color.FromArgb(255, 80, 80);
                                row.Cells[2].Style.Font = new Font("Consolas", 9, FontStyle.Bold);
                            }
                            else
                            {
                                realCriticalPids.Remove(pid);
                                bool byName = criticalKeywords.Contains(nameLower);
                                row.Cells[2].Value = byName ? "Да" : "";
                                row.Cells[2].Style.ForeColor = byName
                                    ? Color.FromArgb(255, 80, 80)
                                    : Color.FromArgb(120, 120, 120);
                                row.Cells[2].Style.Font = new Font("Consolas", 9, byName ? FontStyle.Bold : FontStyle.Regular);
                            }
                        }
                        catch { }
                    }

                    RefreshRealCriticalAsync();

                    MessageBox.Show(
                        $"Критичность {(critical ? "установлена" : "снята")} для {name}" +
                        (critical ? "\n\n⚠ НЕ УБИВАЙ его — BSOD." : ""),
                        "Успешно", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (result == ProcessOpResult.ProtectedProcess)
                {
                    MessageBox.Show(
                        $"'{name}' защищён.\n\n{detail}\n\n" +
                        "Попробуй на обычном приложении — блокнот, браузер, Яндекс Музыка.",
                        "Защищённый процесс", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show($"Ошибка ({result}):\n\n{detail}",
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // KILL / FREEZE
        // ============================================================
        private void KillSelected()
        {
            var pid = GetSelectedPID();
            if (pid <= 0) return;
            var name = GetSelectedName();

            if (realCriticalPids.Contains(pid))
            {
                if (MessageBox.Show(
                    $"⚠ '{name}' КРИТИЧНЫЙ. Завершение вызовет BSOD.\n\nВсё равно?",
                    "Критичный процесс!", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }
            else
            {
                if (MessageBox.Show($"Завершить '{name}' (PID {pid})?", "Подтверждение",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            try
            {
                ProcessHelper.KillProcess(pid);
                frozenPids.Remove(pid);
                pathCache.Remove(pid);
                cmdCache.Remove(pid);
                companyCache.Remove(pid);
                LoadTasksAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenLocation()
        {
            var pid = GetSelectedPID();
            if (pid <= 0) return;
            try
            {
                string path = pathCache.TryGetValue(pid, out var p) ? p : GetProcessPath(pid);
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowProperties() => OpenLocation();

        private void FreezeSelected()
        {
            var pid = GetSelectedPID();
            if (pid <= 0) return;
            ProcessHelper.FreezeProcess(pid);
            frozenPids.Add(pid);
            LoadTasksAsync();
        }

        private void UnfreezeSelected()
        {
            var pid = GetSelectedPID();
            if (pid <= 0) return;
            ProcessHelper.UnfreezeProcess(pid);
            frozenPids.Remove(pid);
            LoadTasksAsync();
        }

        // ============================================================
        // Службы
        // ============================================================
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
                            GetServiceStartType(service.ServiceName));
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
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

                    var si = new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = $"delete \"{name}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    using (var p = Process.Start(si)) p?.WaitForExit();

                    LoadServices();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public new void Dispose()
        {
            try
            {
                refreshTimer?.Stop();
                refreshTimer?.Dispose();
                ProcessHelper.ClearCache();
            }
            catch { }
            base.Dispose();
        }
    }
}