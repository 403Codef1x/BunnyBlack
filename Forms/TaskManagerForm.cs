// language: C#, file: Forms/TaskManagerForm.cs
// Полная замена. Отображение как в Process Explorer / RKit:
//   дерево процессов с отступами ├─/└─, колонки Name, PID, Critical,
//   Company Name, CommandLine.
// Контекстное меню: Kill, Freeze, Critical, Location, Properties.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class TaskManagerForm : UserControl
    {
        private DataGridView grid;
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

        private List<ProcessInfo> cachedProcesses = new List<ProcessInfo>();

        private readonly HashSet<string> criticalKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "smss", "csrss", "wininit", "services", "lsass", "lsm",
            "winlogon", "registry", "memory compression", "system idle process"
        };

        // Кэш Company Name для путей
        private readonly Dictionary<string, string> companyCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                Text = "Поиск по имени процесса..."
            };
            searchBox.ForeColor = Color.Gray;
            searchBox.GotFocus += (s, e) => { if (searchBox.Text == "Поиск по имени процесса...") { searchBox.Text = ""; searchBox.ForeColor = Color.FromArgb(216, 216, 216); } };
            searchBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(searchBox.Text)) { searchBox.Text = "Поиск по имени процесса..."; searchBox.ForeColor = Color.Gray; } };
            searchBox.TextChanged += (s, e) => FilterTree();
            layout.Controls.Add(searchBox, 0, 1);

            grid = new DataGridView
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
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);

            grid.Columns.Add("Name", "Process Name");
            grid.Columns.Add("PID", "PID");
            grid.Columns.Add("Critical", "Critical");
            grid.Columns.Add("Company", "Company Name");
            grid.Columns.Add("CommandLine", "CommandLine");

            grid.Columns[0].Width = 300;
            grid.Columns[1].Width = 70;
            grid.Columns[2].Width = 80;
            grid.Columns[3].Width = 200;
            grid.Columns[4].Width = 640;

            grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            grid.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns[1].SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns[2].SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns[3].SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns[4].SortMode = DataGridViewColumnSortMode.NotSortable;

            grid.ContextMenuStrip = CreateContextMenu();
            layout.Controls.Add(grid, 0, 2);

            this.Controls.Add(layout);

            ToggleAutoRefresh();
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

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (grid.SelectedRows.Count == 0) return;

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
            if (grid.SelectedRows.Count == 0) return -1;
            var val = grid.SelectedRows[0].Cells[1].Value;
            if (val == null) return -1;
            return int.TryParse(val.ToString(), out int p) ? p : -1;
        }

        private string GetSelectedName()
        {
            if (grid.SelectedRows.Count == 0) return "";
            string raw = grid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            // снять префикс дерева
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
                try
                {
                    if (grid.FirstDisplayedScrollingRowIndex >= 0)
                        savedFirstDisplayedRow = grid.FirstDisplayedScrollingRowIndex;
                }
                catch { }
                savedSelectedRowIndex = -1;
                if (grid.SelectedRows.Count > 0)
                    savedSelectedRowIndex = grid.SelectedRows[0].Index;

                var processes = await Task.Run(() => ProcessHelper.GetProcessesFast());
                cachedProcesses = processes;

                // предзагрузка Company Name в фоне
                await Task.Run(() =>
                {
                    foreach (var p in processes)
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(p.Name)) continue;
                            string path = GetProcessPath(p.PID);
                            if (string.IsNullOrEmpty(path)) continue;
                            if (companyCache.ContainsKey(path)) continue;
                            var vi = FileVersionInfo.GetVersionInfo(path);
                            companyCache[path] = vi.CompanyName ?? "";
                        }
                        catch { }
                    }
                });

                if (this.InvokeRequired)
                    this.Invoke(new Action(() =>
                    {
                        RebuildTree(processes);
                        FilterTree();
                        RestoreScrollAndSelection();
                    }));
                else
                {
                    RebuildTree(processes);
                    FilterTree();
                    RestoreScrollAndSelection();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LoadTasks] {ex.Message}");
            }
            finally { isLoading = false; }
        }

        private void RestoreScrollAndSelection()
        {
            try
            {
                if (savedFirstDisplayedRow > 0 && savedFirstDisplayedRow < grid.Rows.Count)
                    grid.FirstDisplayedScrollingRowIndex = savedFirstDisplayedRow;

                if (savedSelectedRowIndex >= 0 && savedSelectedRowIndex < grid.Rows.Count)
                    grid.Rows[savedSelectedRowIndex].Selected = true;
            }
            catch { }
        }

        // ============================================================
        // ДЕРЕВО
        // ============================================================
        private void RebuildTree(List<ProcessInfo> processes)
        {
            grid.Rows.Clear();

            // карта: ppid → список процессов
            var childrenOf = new Dictionary<int, List<ProcessInfo>>();
            var byPid = new Dictionary<int, ProcessInfo>();
            var hasParent = new HashSet<int>();

            foreach (var p in processes)
            {
                byPid[p.PID] = p;
                if (!childrenOf.ContainsKey(p.ParentPID))
                    childrenOf[p.ParentPID] = new List<ProcessInfo>();
                childrenOf[p.ParentPID].Add(p);
            }

            foreach (var p in processes)
                if (p.ParentPID != 0 && byPid.ContainsKey(p.ParentPID))
                    hasParent.Add(p.PID);

            // корни — процессы, чьи родители не в списке или ppid = 0 / 4 (System)
            var roots = new List<ProcessInfo>();
            foreach (var p in processes)
                if (p.ParentPID == 0 || !hasParent.Contains(p.PID) || !byPid.ContainsKey(p.ParentPID))
                    roots.Add(p);

            // сортировка корней — System Idle первый, дальше System, потом остальные
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

            // обход рекурсивно
            var visited = new HashSet<int>();
            foreach (var root in roots)
                AddProcessRow(root, "", true, childrenOf, byPid, visited);

            // кто остался непосещённым (циклы?) — добавим в корень
            foreach (var p in processes)
                if (!visited.Contains(p.PID))
                    AddProcessRow(p, "", true, childrenOf, byPid, visited);

            UpdateStats(processes);
        }

        private void AddProcessRow(ProcessInfo p, string prefix, bool isLast,
            Dictionary<int, List<ProcessInfo>> childrenOf,
            Dictionary<int, ProcessInfo> byPid,
            HashSet<int> visited)
        {
            if (visited.Contains(p.PID)) return;
            visited.Add(p.PID);

            // определяем критикал
            string lower = p.Name.ToLowerInvariant();
            bool isCritical = criticalKeywords.Contains(lower);

            // company name
            string company = "";
            try
            {
                string path = GetProcessPath(p.PID);
                if (!string.IsNullOrEmpty(path) && companyCache.TryGetValue(path, out var c))
                    company = c;
            }
            catch { }
            if (string.IsNullOrEmpty(company) && isCritical) company = "Microsoft Corporation";

            // command line
            string cmd = "";
            try
            {
                string path = GetProcessPath(p.PID);
                cmd = path ?? "";
            }
            catch { }

            // имя с префиксом дерева
            string display = prefix + p.Name;

            int idx = grid.Rows.Add(display, p.PID, isCritical ? "Да" : "", company, cmd);
            var row = grid.Rows[idx];
            row.Tag = p;

            // подсветка по типу
            if (isCritical)
            {
                // как в Process Explorer — светло-красный фон для системных
                row.Cells[0].Style.BackColor = Color.FromArgb(60, 20, 20);
                row.Cells[1].Style.BackColor = Color.FromArgb(60, 20, 20);
                row.Cells[2].Style.BackColor = Color.FromArgb(60, 20, 20);
                row.Cells[3].Style.BackColor = Color.FromArgb(60, 20, 20);
                row.Cells[4].Style.BackColor = Color.FromArgb(60, 20, 20);

                row.Cells[0].Style.ForeColor = Color.FromArgb(255, 180, 180);
                row.Cells[1].Style.ForeColor = Color.FromArgb(255, 180, 180);
                row.Cells[2].Style.ForeColor = Color.FromArgb(255, 100, 100);
                row.Cells[3].Style.ForeColor = Color.FromArgb(255, 180, 180);
                row.Cells[4].Style.ForeColor = Color.FromArgb(255, 180, 180);

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

            // frozen — синий
            if (frozenPids.Contains(p.PID))
            {
                for (int c = 0; c < 5; c++)
                {
                    row.Cells[c].Style.BackColor = Color.FromArgb(10, 26, 42);
                    row.Cells[c].Style.ForeColor = Color.FromArgb(100, 180, 255);
                }
            }

            // дети
            if (childrenOf.TryGetValue(p.PID, out var children))
            {
                // убираем дубли (себя)
                children.RemoveAll(c => c.PID == p.PID);
                children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                for (int i = 0; i < children.Count; i++)
                {
                    bool last = (i == children.Count - 1);
                    string childPrefix = prefix.Replace("├─ ", "│  ").Replace("└─ ", "   ") +
                                         (last ? "└─ " : "├─ ");
                    AddProcessRow(children[i], childPrefix, last, childrenOf, byPid, visited);
                }
            }
        }

        // ============================================================
        // Путь процесса
        // ============================================================
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

        // ============================================================
        // СТАТИСТИКА
        // ============================================================
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

        // ============================================================
        // ФИЛЬТР
        // ============================================================
        private void FilterTree()
        {
            string text = searchBox.Text?.ToLowerInvariant() ?? "";
            if (text == "поиск по имени процесса..." || string.IsNullOrEmpty(text))
            {
                foreach (DataGridViewRow row in grid.Rows) row.Visible = true;
                return;
            }

            foreach (DataGridViewRow row in grid.Rows)
            {
                string name = row.Cells[0].Value?.ToString() ?? "";
                string cmd = row.Cells[4].Value?.ToString() ?? "";
                row.Visible = name.ToLowerInvariant().Contains(text) ||
                              cmd.ToLowerInvariant().Contains(text);
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
        // ДЕЙСТВИЯ
        // ============================================================
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
                string path = GetProcessPath(pid);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowProperties()
        {
            OpenLocation();
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

        private void SetCriticalSelected(bool critical)
        {
            var pid = GetSelectedPID();
            if (pid < 0) return;
            var name = GetSelectedName();

            if (!ProcessHelper.IsAdministrator())
            {
                MessageBox.Show(
                    "Для изменения критичности процесса требуются права администратора!",
                    "Требуются права", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool wasCritical = false;
            try { wasCritical = ProcessHelper.IsProcessCritical(pid); } catch { }
            string currentState = wasCritical ? "СЕЙЧАС КРИТИЧНЫЙ" : "сейчас обычный";

            string warning = critical
                ? "⚠️ ПРОЦЕСС СТАНЕТ КРИТИЧНЫМ ДЛЯ ВСЕЙ СИСТЕМЫ!\n" +
                  "ЕГО ЗАВЕРШЕНИЕ ВЫЗОВЕТ BSOD!"
                : "Критичность будет снята.";

            if (pid == Process.GetCurrentProcess().Id)
                warning += "\n\nВНИМАНИЕ: Вы изменяете критичность ТЕКУЩЕЙ ПРОГРАММЫ!";

            DialogResult confirm = MessageBox.Show(
                $"{(critical ? "Установить" : "Снять")} критичность с '{name}' (PID: {pid})?\n" +
                $"Текущий статус: {currentState}\n\n" + warning,
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            try
            {
                string detail;
                var result = ProcessHelper.SetSystemCritical(pid, critical, out detail);

                if (result == ProcessOpResult.Success)
                {
                    ProcessHelper.ClearCache();
                    LoadTasksAsync();
                    MessageBox.Show($"{(critical ? "Критичность установлена" : "Критичность снята")} для {name}",
                        "Успешно", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show($"Ошибка ({result}): {detail}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
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