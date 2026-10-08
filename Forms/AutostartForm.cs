// language: C#, file: Forms/AutostartForm.cs
// Полная замена. Источник данных — AutorunsExtended.Collect().
// Группы: Run, Winlogon, BootExecute, IFEO Debugger.
// Тема + локализация.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class AutostartForm : UserControl
    {
        private DataGridView grid;
        private FlowLayoutPanel topBar;
        private TextBox filterBox;
        private Button refreshBtn;
        private Label statusLabel;
        private Label titleLabel;
        private bool isWinRE;

        private class Entry
        {
            public string ShortName;
            public string FullName;
            public string Value;
            public string Hive;
            public string KeyPath;
            public string ValueName;
            public string FilePathHint;
            public bool IsGroup;
            public bool Exists;
            public bool Signed;
            public DateTime? CreatedAt;
            public DateTime? ModifiedAt;
            public int AgeDays;
            public bool Fresh;
            public bool Suspicious;
            public string Status;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public AutostartForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;
            InitializeComponent();
            LoadData();

            ThemeManager.ThemeChanged += ApplyTheme;
            Loc.LanguageChanged += ApplyLanguage;
        }

        public void ApplyTheme()
        {
            if (InvokeRequired) { Invoke(new Action(ApplyTheme)); return; }
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;
            ThemeHelper.Apply(this);
            Invalidate(true);
        }

        public void ApplyLanguage()
        {
            if (InvokeRequired) { Invoke(new Action(ApplyLanguage)); return; }
            if (titleLabel != null) titleLabel.Text = Loc.T("autostart.title");
            if (filterBox != null) filterBox.PlaceholderText = Loc.T("autostart.filter");
            if (refreshBtn != null) refreshBtn.Text = Loc.T("btn.refresh");

            if (grid != null && grid.Columns.Count >= 8)
            {
                grid.Columns[0].HeaderText = Loc.T("autostart.col.name");
                grid.Columns[1].HeaderText = Loc.T("autostart.col.value");
                grid.Columns[2].HeaderText = Loc.T("autostart.col.exists");
                grid.Columns[3].HeaderText = Loc.T("autostart.col.signed");
                grid.Columns[4].HeaderText = Loc.T("autostart.col.created");
                grid.Columns[5].HeaderText = Loc.T("autostart.col.modified");
                grid.Columns[6].HeaderText = Loc.T("autostart.col.age");
                grid.Columns[7].HeaderText = Loc.T("autostart.col.status");
            }

            if (grid?.ContextMenuStrip != null)
            {
                var items = grid.ContextMenuStrip.Items;
                if (items.Count >= 7)
                {
                    items[0].Text = Loc.T("autostart.menu.edit");
                    items[1].Text = Loc.T("autostart.menu.delete");
                    if (items.Count > 3) items[3].Text = Loc.T("autostart.menu.location");
                    if (items.Count > 4) items[4].Text = Loc.T("autostart.menu.regedit");
                    if (items.Count > 5) items[5].Text = Loc.T("autostart.menu.copyfull");
                    if (items.Count > 6) items[6].Text = Loc.T("autostart.menu.copyval");
                    if (items.Count > 8) items[8].Text = Loc.T("autostart.menu.quar");
                }
            }

            Invalidate(true);
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(28, 20, 28, 20),
                BackColor = ThemeManager.Background
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            titleLabel = new Label
            {
                Text = Loc.T("autostart.title"),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(titleLabel, 0, 0);

            topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 4, 0, 0)
            };

            filterBox = new TextBox
            {
                Width = 340,
                BackColor = ThemeManager.Input,
                ForeColor = ThemeManager.Foreground,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10),
                Margin = new Padding(0, 2, 8, 0)
            };
            try { filterBox.PlaceholderText = Loc.T("autostart.filter"); } catch { }
            filterBox.TextChanged += (s, e) => ApplyFilter();
            topBar.Controls.Add(filterBox);

            refreshBtn = new Button
            {
                Text = Loc.T("btn.refresh"),
                Width = 110,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.PanelAlt,
                ForeColor = ThemeManager.Foreground,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Border },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9),
                Margin = new Padding(0, 2, 12, 0)
            };
            refreshBtn.Click += (s, e) => LoadData();
            topBar.Controls.Add(refreshBtn);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = ThemeManager.Muted,
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 8, 0, 0)
            };
            topBar.Controls.Add(statusLabel);

            layout.Controls.Add(topBar, 0, 1);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Background,
                ForeColor = ThemeManager.Foreground,
                BackgroundColor = ThemeManager.Background,
                GridColor = ThemeManager.Border,
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
            grid.RowTemplate.Height = 28;

            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null, grid, new object[] { true });

            grid.ColumnHeadersDefaultCellStyle.BackColor = ThemeManager.Header;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ThemeManager.Foreground;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = ThemeManager.Header;

            grid.DefaultCellStyle.BackColor = ThemeManager.Row;
            grid.DefaultCellStyle.ForeColor = ThemeManager.Foreground;
            grid.DefaultCellStyle.SelectionBackColor = ThemeManager.Selection;
            grid.DefaultCellStyle.SelectionForeColor = ThemeManager.SelectionText;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9);

            grid.AlternatingRowsDefaultCellStyle.BackColor = ThemeManager.RowAlt;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = ThemeManager.Foreground;

            grid.Columns.Add("Name", Loc.T("autostart.col.name"));
            grid.Columns.Add("Value", Loc.T("autostart.col.value"));
            grid.Columns.Add("Exists", Loc.T("autostart.col.exists"));
            grid.Columns.Add("Signed", Loc.T("autostart.col.signed"));
            grid.Columns.Add("Created", Loc.T("autostart.col.created"));
            grid.Columns.Add("Modified", Loc.T("autostart.col.modified"));
            grid.Columns.Add("Age", Loc.T("autostart.col.age"));
            grid.Columns.Add("Status", Loc.T("autostart.col.status"));

            grid.Columns[0].Width = 480;
            grid.Columns[1].Width = 520;
            grid.Columns[2].Width = 70;
            grid.Columns[3].Width = 80;
            grid.Columns[4].Width = 130;
            grid.Columns[5].Width = 130;
            grid.Columns[6].Width = 80;
            grid.Columns[7].Width = 260;

            for (int i = 0; i < grid.Columns.Count; i++)
                grid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;

            grid.ContextMenuStrip = BuildContextMenu();
            grid.CellDoubleClick += (s, e) => EditValueInline(e.RowIndex);

            layout.Controls.Add(grid, 0, 2);
            this.Controls.Add(layout);
        }

        private void LoadData()
        {
            entries.Clear();
            grid.Rows.Clear();
            statusLabel.Text = Loc.T("status.loading");

            try
            {
                var collected = AutorunsExtended.Collect(isWinRE);

                var order = new (string category, string title)[]
                {
                    ("Run",             @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run"),
                    ("Winlogon",        @"HKLM\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"),
                    ("BootExecute",     @"HKLM\System\CurrentControlSet\Control\Session Manager"),
                    ("IFEO Debugger",   @"HKLM\Software\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"),
                };

                var byCategory = collected
                    .GroupBy(e => e.Category ?? "Other")
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var (cat, title) in order)
                {
                    if (!byCategory.TryGetValue(cat, out var items) || items.Count == 0)
                        continue;

                    AddGroup(title);
                    foreach (var it in items)
                        AddValue(title, it);
                }

                statusLabel.Text = $"Записей: {entries.Count(e => !e.IsGroup)}";
                ApplyFilter();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Ошибка: " + ex.Message;
                BbLog.Error("[AutostartForm.LoadData]", ex);
            }
        }

        private void AddGroup(string title)
        {
            var e = new Entry
            {
                ShortName = title,
                FullName = title,
                Value = "",
                IsGroup = true
            };
            entries.Add(e);

            int idx = grid.Rows.Add(title, "", "", "", "", "", "", "");
            var row = grid.Rows[idx];
            row.Tag = e;
            row.DefaultCellStyle.BackColor = ThemeManager.PanelAlt;
            row.DefaultCellStyle.ForeColor = ThemeManager.Foreground;
            row.DefaultCellStyle.SelectionBackColor = ThemeManager.Selection;
            row.DefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        }

        private void AddValue(string groupTitle, AutorunEntry it)
        {
            string valueName = it.Name;
            string shortName = string.IsNullOrEmpty(valueName)
                ? "  └─ (default)"
                : "  └─ " + valueName;

            string fullName = string.IsNullOrEmpty(valueName)
                ? groupTitle
                : groupTitle + "\\" + valueName;

            var e = new Entry
            {
                ShortName = shortName,
                FullName = fullName,
                Value = it.Path ?? "",
                Hive = it.Hive,
                KeyPath = it.KeyPath,
                ValueName = it.ValueName,
                FilePathHint = it.Path,
                IsGroup = false,
                Exists = it.FileExists,
                Signed = it.Signed,
                CreatedAt = it.CreatedAt,
                ModifiedAt = it.ModifiedAt,
                AgeDays = it.AgeDays,
                Fresh = it.FreshFile,
                Suspicious = it.Suspicious,
                Status = it.SuspicionReason
            };
            entries.Add(e);

            string existsStr = it.FileExists ? "Да" : "Нет";
            string signedStr = it.FileExists ? (it.Signed ? "Да" : "Нет") : "—";
            string createdStr = it.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—";
            string modifiedStr = it.ModifiedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—";
            string ageStr = it.FileExists ? $"{it.AgeDays} дн." : "—";
            string statusStr = it.Suspicious ? (it.SuspicionReason ?? "Подозрительно") : "";

            int idx = grid.Rows.Add(shortName, e.Value, existsStr, signedStr,
                createdStr, modifiedStr, ageStr, statusStr);
            var row = grid.Rows[idx];
            row.Tag = e;

            if (it.FreshFile)
            {
                row.Cells[0].Style.ForeColor = ThemeManager.Warning;
                row.Cells[1].Style.ForeColor = ThemeManager.Warning;
                row.Cells[6].Style.ForeColor = ThemeManager.Warning;
                row.Cells[6].Style.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            }
            else if (it.Suspicious)
            {
                row.Cells[0].Style.ForeColor = ThemeManager.Danger;
                row.Cells[1].Style.ForeColor = ThemeManager.Warning;
                row.Cells[7].Style.ForeColor = ThemeManager.Danger;
            }
            else
            {
                row.Cells[0].Style.ForeColor = ThemeManager.Foreground;
                row.Cells[1].Style.ForeColor = ThemeManager.Success;
            }

            if (it.FileExists && !it.Signed)
                row.Cells[3].Style.ForeColor = ThemeManager.Danger;
            else if (it.FileExists && it.Signed)
                row.Cells[3].Style.ForeColor = ThemeManager.Success;

            if (!it.FileExists)
            {
                row.Cells[2].Style.ForeColor = ThemeManager.Muted;
                row.Cells[1].Style.ForeColor = ThemeManager.Muted;
                row.Cells[1].Style.Font = new Font("Segoe UI", 9, FontStyle.Italic);
            }
        }

        private void ApplyFilter()
        {
            string f = (filterBox.Text ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(f))
            {
                foreach (DataGridViewRow row in grid.Rows) row.Visible = true;
                return;
            }

            for (int i = 0; i < grid.Rows.Count; i++)
            {
                var e = grid.Rows[i].Tag as Entry;
                if (e == null || e.IsGroup) continue;
                bool match =
                    (e.FullName?.ToLowerInvariant().Contains(f) ?? false) ||
                    (e.Value?.ToLowerInvariant().Contains(f) ?? false) ||
                    (e.Status?.ToLowerInvariant().Contains(f) ?? false);
                grid.Rows[i].Visible = match;
            }

            for (int i = grid.Rows.Count - 1; i >= 0; i--)
            {
                var e = grid.Rows[i].Tag as Entry;
                if (e == null || !e.IsGroup) continue;

                bool anyVisible = false;
                for (int j = i + 1; j < grid.Rows.Count; j++)
                {
                    var e2 = grid.Rows[j].Tag as Entry;
                    if (e2 == null) break;
                    if (e2.IsGroup) break;
                    if (grid.Rows[j].Visible) { anyVisible = true; break; }
                }
                grid.Rows[i].Visible = anyVisible;
            }
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip
            {
                BackColor = ThemeManager.PanelAlt,
                ForeColor = ThemeManager.Foreground
            };

            var edit = new ToolStripMenuItem(Loc.T("autostart.menu.edit"));
            edit.Click += (s, e) => EditSelectedValue();
            m.Items.Add(edit);

            var del = new ToolStripMenuItem(Loc.T("autostart.menu.delete"));
            del.ForeColor = ThemeManager.Danger;
            del.Click += (s, e) => DeleteSelected();
            m.Items.Add(del);

            m.Items.Add(new ToolStripSeparator());

            var openLoc = new ToolStripMenuItem(Loc.T("autostart.menu.location"));
            openLoc.Click += (s, e) => OpenFileLocation();
            m.Items.Add(openLoc);

            var openReg = new ToolStripMenuItem(Loc.T("autostart.menu.regedit"));
            openReg.Click += (s, e) => OpenInRegedit();
            m.Items.Add(openReg);

            var copyName = new ToolStripMenuItem(Loc.T("autostart.menu.copyfull"));
            copyName.Click += (s, e) => { var x = GetSelected(); if (x != null) Clipboard.SetText(x.FullName ?? ""); };
            m.Items.Add(copyName);

            var copyVal = new ToolStripMenuItem(Loc.T("autostart.menu.copyval"));
            copyVal.Click += (s, e) => { var x = GetSelected(); if (x != null) Clipboard.SetText(x.Value ?? ""); };
            m.Items.Add(copyVal);

            m.Items.Add(new ToolStripSeparator());

            var quar = new ToolStripMenuItem(Loc.T("autostart.menu.quar"));
            quar.ForeColor = ThemeManager.Warning;
            quar.Click += (s, e) => SendSelectedToQuarantine();
            m.Items.Add(quar);

            return m;
        }

        private Entry GetSelected()
        {
            if (grid.SelectedRows.Count == 0) return null;
            return grid.SelectedRows[0].Tag as Entry;
        }

        private void EditValueInline(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            var e = grid.Rows[rowIndex].Tag as Entry;
            if (e == null || e.IsGroup) return;
            if (string.IsNullOrEmpty(e.ValueName)) return;

            using (var dlg = new Form
            {
                Text = Loc.T("autostart.menu.edit") + ": " + e.ValueName,
                Size = new Size(700, 160),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = ThemeManager.Background,
                ForeColor = ThemeManager.Foreground,
                TopMost = true,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            })
            {
                var tb = new TextBox
                {
                    Text = e.Value ?? "",
                    Location = new Point(12, 12),
                    Width = 660,
                    BackColor = ThemeManager.Input,
                    ForeColor = ThemeManager.Foreground,
                    BorderStyle = BorderStyle.FixedSingle
                };
                var ok = new Button
                {
                    Text = Loc.T("btn.ok"),
                    Location = new Point(560, 60),
                    Size = new Size(110, 32),
                    DialogResult = DialogResult.OK,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ThemeManager.Accent,
                    ForeColor = ThemeManager.AccentText
                };
                var cancel = new Button
                {
                    Text = Loc.T("btn.cancel"),
                    Location = new Point(440, 60),
                    Size = new Size(110, 32),
                    DialogResult = DialogResult.Cancel,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ThemeManager.PanelAlt,
                    ForeColor = ThemeManager.Foreground
                };
                dlg.Controls.Add(tb);
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;

                if (dlg.ShowDialog(FindOwner()) == DialogResult.OK)
                {
                    if (WriteRegistryValue(e, tb.Text))
                    {
                        LoadData();
                        NedoMessageBox.Show("OK");
                    }
                    else NedoMessageBox.Show("Error", isError: true);
                }
            }
        }

        private void EditSelectedValue()
        {
            if (grid.SelectedRows.Count == 0) return;
            EditValueInline(grid.SelectedRows[0].Index);
        }

        private bool WriteRegistryValue(Entry e, string newValue)
        {
            try
            {
                Microsoft.Win32.RegistryKey key = null;
                string path = e.KeyPath ?? "";
                if (e.Hive == "HKLM")
                {
                    if (isWinRE)
                    {
                        string prefix =
                            path.StartsWith("SYSTEM\\", StringComparison.OrdinalIgnoreCase)
                                ? "BunnyBlack_Offline_SYSTEM\\" + path.Substring(7)
                                : path.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase)
                                    ? "BunnyBlack_Offline_SOFTWARE\\" + path.Substring(9)
                                    : "BunnyBlack_Offline_SOFTWARE\\" + path;
                        key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(prefix, true);
                    }
                    else key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path, true);
                }
                else if (e.Hive == "HKCU")
                {
                    key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path, true);
                }

                if (key == null) return false;
                key.SetValue(e.ValueName, newValue);
                key.Dispose();
                return true;
            }
            catch { return false; }
        }

        private void DeleteSelected()
        {
            var e = GetSelected();
            if (e == null || e.IsGroup) return;

            if (MessageBoxHelper.Show($"Delete '{e.FullName}'?", Loc.T("warn.confirm"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                if (string.IsNullOrEmpty(e.ValueName)) return;

                Microsoft.Win32.RegistryKey key = null;
                string path = e.KeyPath ?? "";
                if (e.Hive == "HKLM")
                {
                    if (isWinRE)
                    {
                        string prefix =
                            path.StartsWith("SYSTEM\\", StringComparison.OrdinalIgnoreCase)
                                ? "BunnyBlack_Offline_SYSTEM\\" + path.Substring(7)
                                : path.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase)
                                    ? "BunnyBlack_Offline_SOFTWARE\\" + path.Substring(9)
                                    : "BunnyBlack_Offline_SOFTWARE\\" + path;
                        key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(prefix, true);
                    }
                    else key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path, true);
                }
                else if (e.Hive == "HKCU")
                {
                    key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path, true);
                }

                if (key != null && key.GetValue(e.ValueName) != null)
                {
                    key.DeleteValue(e.ValueName);
                    key.Dispose();
                    LoadData();
                    NedoMessageBox.Show(Loc.T("btn.delete"));
                }
                else
                {
                    key?.Dispose();
                    NedoMessageBox.Show("Not found", isError: true);
                }
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show("Error: " + ex.Message, isError: true);
            }
        }

        private void OpenFileLocation()
        {
            var e = GetSelected();
            if (e == null || e.IsGroup || string.IsNullOrEmpty(e.FilePathHint)) return;

            string path = e.FilePathHint.Trim('"');
            int spaceIdx = path.IndexOf(' ');
            if (spaceIdx > 0 && path.Contains(".exe"))
                path = path.Substring(0, spaceIdx);

            try
            {
                if (File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Process.Start(dir);
                    else
                        NedoMessageBox.Show("Not found: " + path, isError: true);
                }
            }
            catch (Exception ex) { NedoMessageBox.Show("Error: " + ex.Message, isError: true); }
        }

        private void OpenInRegedit()
        {
            try { Process.Start("regedit.exe"); }
            catch { }
        }

        private void SendSelectedToQuarantine()
        {
            var e = GetSelected();
            if (e == null || e.IsGroup || string.IsNullOrEmpty(e.FilePathHint)) return;

            string path = e.FilePathHint.Trim('"');
            if (!File.Exists(path))
            {
                NedoMessageBox.Show("Not found: " + path, isError: true);
                return;
            }

            string err;
            if (Quarantine.Add(path, "Из автозагрузки: " + e.FullName, out err))
            {
                LoadData();
                NedoMessageBox.Show("Quarantined");
            }
            else NedoMessageBox.Show("Error: " + err, isError: true);
        }

        private Form FindOwner()
        {
            foreach (Form f in Application.OpenForms)
                if (f is MainForm) return f;
            return Form.ActiveForm;
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