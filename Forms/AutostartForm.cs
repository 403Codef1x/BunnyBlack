// language: C#, file: Forms/AutostartForm.cs
// Полная замена. Только ключевые автозапуски:
//   Run / RunOnce (HKLM + HKCU)
//   Winlogon
//   SafeBoot
//   Session Manager (BootExecute)
//   System\Setup (CmdLine, SetupType, EnableCursorSuppression)
//   Windows (AppInit_DLLs)
// Группы + отступы └─, короткий префикс HKLM\Software.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using BunnyBlack.Core;
using Microsoft.Win32;

namespace BunnyBlack.Forms
{
    public partial class AutostartForm : UserControl
    {
        private DataGridView grid;
        private FlowLayoutPanel topBar;
        private TextBox filterBox;
        private Button refreshBtn;
        private Label statusLabel;
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
            public bool Suspicious;
        }

        private readonly List<Entry> entries = new List<Entry>();

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

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(28, 20, 28, 20),
                BackColor = Color.FromArgb(13, 13, 13)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = "Автозагрузка",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(title, 0, 0);

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
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10),
                Margin = new Padding(0, 2, 8, 0)
            };
            filterBox.TextChanged += (s, e) => ApplyFilter();
            topBar.Controls.Add(filterBox);

            refreshBtn = new Button
            {
                Text = "Обновить",
                Width = 110,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(240, 240, 240),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9),
                Margin = new Padding(0, 2, 12, 0)
            };
            refreshBtn.Click += (s, e) => LoadData();
            topBar.Controls.Add(refreshBtn);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(120, 120, 120),
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
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BackgroundColor = Color.FromArgb(13, 13, 13),
                GridColor = Color.FromArgb(45, 45, 45),
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

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(25, 25, 25);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 200, 200);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(25, 25, 25);

            grid.DefaultCellStyle.BackColor = Color.FromArgb(18, 18, 18);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 50, 60);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(255, 255, 255);
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9);

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(14, 14, 14);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);

            grid.Columns.Add("Name", "Name");
            grid.Columns.Add("Value", "Value");
            grid.Columns[0].Width = 700;
            grid.Columns[1].Width = 620;
            grid.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns[1].SortMode = DataGridViewColumnSortMode.NotSortable;

            grid.ContextMenuStrip = BuildContextMenu();
            grid.CellDoubleClick += (s, e) => EditValueInline(e.RowIndex);

            layout.Controls.Add(grid, 0, 2);
            this.Controls.Add(layout);
        }

        // ============================================================
        // СБОР ДАННЫХ — только 7 групп
        // ============================================================
        private void LoadData()
        {
            entries.Clear();
            grid.Rows.Clear();
            statusLabel.Text = "Загрузка…";

            try
            {
                CollectRun();
                CollectWinlogon();
                CollectSafeBoot();
                CollectBootExecute();
                CollectCmdLineAndAppInit();

                statusLabel.Text = $"Записей: {entries.Count}";
                ApplyFilter();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Ошибка: " + ex.Message;
            }
        }

        // ------------------------------------------------------------
        // RUN / RUNONCE
        // ------------------------------------------------------------
        private void CollectRun()
        {
            // HKLM\Run
            CollectRunGroup(
                @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run",
                RegistryHelper.GetRunItems(),
                it => IsHklm(it.Type) && !it.Type.Contains("RunOnce"),
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                "HKLM");

            // HKCU\Run
            CollectRunGroup(
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                RegistryHelper.GetRunItems(),
                it => IsHkcu(it.Type) && !it.Type.Contains("RunOnce"),
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                "HKCU");

            // HKLM\RunOnce
            CollectRunGroup(
                @"HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce",
                RegistryHelper.GetRunOnceItems(),
                it => IsHklm(it.Type),
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                "HKLM");

            // HKCU\RunOnce
            CollectRunGroup(
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce",
                RegistryHelper.GetRunOnceItems(),
                it => IsHkcu(it.Type),
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                "HKCU");
        }

        private bool IsHklm(string t) => !string.IsNullOrEmpty(t) && t.Contains("HKLM");
        private bool IsHkcu(string t) => !string.IsNullOrEmpty(t) && t.Contains("HKCU");

        private void CollectRunGroup(string title, List<AutostartItem> items,
            Func<AutostartItem, bool> filter, string keyPath, string hive)
        {
            var matching = new List<AutostartItem>();
            foreach (var it in items)
                if (filter(it)) matching.Add(it);

            if (matching.Count == 0) return;

            AddGroup(title);
            foreach (var it in matching)
                AddValue(title, it.Name, it.Path, hive, keyPath, it.Name, it.Exists, Suspicious(it.Path));
        }

        // ------------------------------------------------------------
        // WINLOGON
        // ------------------------------------------------------------
        private void CollectWinlogon()
        {
            const string title = @"HKLM\Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
            const string keyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

            var items = RegistryHelper.GetWinlogonItems();
            if (items.Count == 0) return;

            AddGroup(title);
            foreach (var it in items)
                AddValue(title, it.Name, it.Path, "HKLM", keyPath, it.Name, true, false);
        }

        // ------------------------------------------------------------
        // SAFEBOOT
        // ------------------------------------------------------------
        private void CollectSafeBoot()
        {
            string[] groups = {
                @"HKLM\System\CurrentControlSet\Control\SafeBoot",
                @"HKLM\System\CurrentControlSet\Control\SafeBoot\Minimal",
                @"HKLM\System\CurrentControlSet\Control\SafeBoot\Network"
            };
            string[] interesting = { "AlternateShell", "Default", "Option" };

            foreach (var g in groups)
            {
                string keyPath = g.Replace(@"HKLM\System\", @"SYSTEM\");

                RegistryKey root = null;
                try
                {
                    root = isWinRE
                        ? Registry.LocalMachine.OpenSubKey("BunnyBlack_Offline_SYSTEM\\" + keyPath.Substring(7))
                        : Registry.LocalMachine.OpenSubKey(keyPath);
                    if (root == null) continue;

                    var found = new List<(string name, string value)>();
                    foreach (var name in interesting)
                    {
                        string v = root.GetValue(name)?.ToString();
                        if (!string.IsNullOrEmpty(v)) found.Add((name, v));
                    }

                    if (found.Count == 0) continue;

                    AddGroup(g);
                    foreach (var f in found)
                        AddValue(g, f.name, f.value, "HKLM", keyPath, f.name, true, false);
                }
                catch { }
                finally { root?.Dispose(); }
            }
        }

        // ------------------------------------------------------------
        // BOOT EXECUTE
        // ------------------------------------------------------------
        private void CollectBootExecute()
        {
            const string title = @"HKLM\System\CurrentControlSet\Control\Session Manager";
            const string keyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager";

            try
            {
                RegistryKey root = isWinRE
                    ? Registry.LocalMachine.OpenSubKey("BunnyBlack_Offline_SYSTEM\\CurrentControlSet\\Control\\Session Manager")
                    : Registry.LocalMachine.OpenSubKey(keyPath);
                if (root == null) return;

                var boot = root.GetValue("BootExecute");
                root.Dispose();

                if (boot is string[] arr && arr.Length > 0)
                {
                    AddGroup(title);
                    foreach (var s in arr)
                        AddValue(title, "BootExecute", s, "HKLM", keyPath, "BootExecute", true, false);
                }
                else if (boot is string str && !string.IsNullOrEmpty(str))
                {
                    AddGroup(title);
                    AddValue(title, "BootExecute", str, "HKLM", keyPath, "BootExecute", true, false);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------
        // SYSTEM\SETUP (CmdLine, SetupType, EnableCursorSuppression)
        // + APPINIT_DLLS
        // ------------------------------------------------------------
        private void CollectCmdLineAndAppInit()
        {
            const string setupTitle = @"HKLM\System\Setup";
            const string setupKeyPath = @"SYSTEM\Setup";
            var setupItems = RegistryHelper.GetCmdLineAutoRunItems();
            if (setupItems.Count > 0)
            {
                AddGroup(setupTitle);
                foreach (var it in setupItems)
                    AddValue(setupTitle, it.Name, it.Path, "HKLM", setupKeyPath, it.Name, true, false);
            }

            const string winTitle = @"HKLM\Software\Microsoft\Windows NT\CurrentVersion\Windows";
            const string winKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows";
            var appinit = RegistryHelper.GetAppInitDllsItems();

            var realAppInit = new List<AppInitItem>();
            foreach (var it in appinit)
            {
                if (it.Name == "LoadAppInit_DLLs" || it.Name == "RequireSignedAppInit_DLLs") continue;
                realAppInit.Add(it);
            }

            if (realAppInit.Count == 0) return;

            AddGroup(winTitle);
            foreach (var it in realAppInit)
                AddValue(winTitle, it.Name, it.Path, "HKLM", winKeyPath, it.Name, true, false);
        }

        // ============================================================
        // ДОБАВЛЕНИЕ
        // ============================================================
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

            int idx = grid.Rows.Add(title, "");
            var row = grid.Rows[idx];
            row.Tag = e;
            row.DefaultCellStyle.BackColor = Color.FromArgb(28, 28, 28);
            row.DefaultCellStyle.ForeColor = Color.FromArgb(200, 200, 200);
            row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 50, 60);
            row.DefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        }

        private void AddValue(string groupTitle, string valueName, string value,
            string hive, string keyPath, string realValueName,
            bool exists, bool suspicious)
        {
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
                Value = value ?? "",
                Hive = hive,
                KeyPath = keyPath,
                ValueName = realValueName,
                FilePathHint = value,
                IsGroup = false,
                Exists = exists,
                Suspicious = suspicious
            };
            entries.Add(e);

            int idx = grid.Rows.Add(shortName, e.Value);
            var row = grid.Rows[idx];
            row.Tag = e;

            if (suspicious)
            {
                row.Cells[0].Style.ForeColor = Color.FromArgb(255, 120, 120);
                row.Cells[1].Style.ForeColor = Color.FromArgb(255, 200, 120);
            }
            else
            {
                row.Cells[0].Style.ForeColor = Color.FromArgb(230, 230, 230);
                row.Cells[1].Style.ForeColor = Color.FromArgb(180, 220, 180);
            }

            if (!exists && !string.IsNullOrEmpty(value) &&
                (value.Contains(".exe") || value.Contains(".dll") || value.Contains(":\\")))
            {
                row.Cells[1].Style.ForeColor = Color.FromArgb(120, 120, 120);
                row.Cells[1].Style.Font = new Font("Segoe UI", 9, FontStyle.Italic);
            }
        }

        private bool Suspicious(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string lower = path.ToLowerInvariant();
            return lower.Contains(@"\temp\") || lower.Contains(@"\appdata\local\temp") ||
                   lower.Contains(@"\downloads\") || lower.Contains(@"\users\public\") ||
                   (lower.Contains(@"\programdata\") && lower.Contains(".exe")) ||
                   lower.Contains("powershell ") || lower.Contains("cmd.exe /c") ||
                   lower.Contains("wscript") || lower.Contains("mshta") ||
                   lower.Contains("rundll32");
        }

        // ============================================================
        // ФИЛЬТР
        // ============================================================
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
                    (e.Value?.ToLowerInvariant().Contains(f) ?? false);
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

        // ============================================================
        // КОНТЕКСТНОЕ МЕНЮ
        // ============================================================
        private ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip { BackColor = Color.FromArgb(17, 17, 17), ForeColor = Color.FromArgb(216, 216, 216) };

            var edit = new ToolStripMenuItem("Изменить значение");
            edit.Click += (s, e) => EditSelectedValue();
            m.Items.Add(edit);

            var del = new ToolStripMenuItem("Удалить");
            del.ForeColor = Color.FromArgb(255, 150, 150);
            del.Click += (s, e) => DeleteSelected();
            m.Items.Add(del);

            m.Items.Add(new ToolStripSeparator());

            var openLoc = new ToolStripMenuItem("Открыть расположение файла");
            openLoc.Click += (s, e) => OpenFileLocation();
            m.Items.Add(openLoc);

            var openReg = new ToolStripMenuItem("Открыть в редакторе реестра");
            openReg.Click += (s, e) => OpenInRegedit();
            m.Items.Add(openReg);

            var copyName = new ToolStripMenuItem("Копировать полный путь");
            copyName.Click += (s, e) => { var x = GetSelected(); if (x != null) Clipboard.SetText(x.FullName ?? ""); };
            m.Items.Add(copyName);

            var copyVal = new ToolStripMenuItem("Копировать Value");
            copyVal.Click += (s, e) => { var x = GetSelected(); if (x != null) Clipboard.SetText(x.Value ?? ""); };
            m.Items.Add(copyVal);

            m.Items.Add(new ToolStripSeparator());

            var quar = new ToolStripMenuItem("В карантин (если файл)");
            quar.ForeColor = Color.FromArgb(255, 200, 120);
            quar.Click += (s, e) => SendSelectedToQuarantine();
            m.Items.Add(quar);

            return m;
        }

        private Entry GetSelected()
        {
            if (grid.SelectedRows.Count == 0) return null;
            return grid.SelectedRows[0].Tag as Entry;
        }

        // ============================================================
        // ДЕЙСТВИЯ
        // ============================================================
        private void EditValueInline(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            var e = grid.Rows[rowIndex].Tag as Entry;
            if (e == null || e.IsGroup) return;
            if (string.IsNullOrEmpty(e.ValueName)) return;

            using (var dlg = new Form
            {
                Text = "Изменить значение: " + e.ValueName,
                Size = new Size(700, 160),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
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
                    BackColor = Color.FromArgb(24, 24, 24),
                    ForeColor = Color.FromArgb(216, 216, 216),
                    BorderStyle = BorderStyle.FixedSingle
                };
                var ok = new Button { Text = "OK", Location = new Point(560, 60), Size = new Size(110, 32), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 55, 75), ForeColor = Color.FromArgb(240, 240, 240) };
                var cancel = new Button { Text = "Отмена", Location = new Point(440, 60), Size = new Size(110, 32), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(170, 170, 170) };
                dlg.Controls.Add(tb); dlg.Controls.Add(ok); dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok; dlg.CancelButton = cancel;

                if (dlg.ShowDialog(FindOwner()) == DialogResult.OK)
                {
                    if (WriteRegistryValue(e, tb.Text))
                    {
                        LoadData();
                        NedoMessageBox.Show("Изменено.");
                    }
                    else NedoMessageBox.Show("Не удалось изменить.", isError: true);
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
                RegistryKey key = null;
                if (e.Hive == "HKLM")
                {
                    string path = e.KeyPath.StartsWith("SOFTWARE\\") || e.KeyPath.StartsWith("SYSTEM\\")
                        ? e.KeyPath : "SOFTWARE\\" + e.KeyPath;
                    if (isWinRE)
                    {
                        string prefix = path.StartsWith("SYSTEM\\") ? "BunnyBlack_Offline_SYSTEM\\" + path.Substring(7)
                                     : path.StartsWith("SOFTWARE\\") ? "BunnyBlack_Offline_SOFTWARE\\" + path.Substring(9)
                                     : path;
                        key = Registry.LocalMachine.OpenSubKey(prefix, true);
                    }
                    else key = Registry.LocalMachine.OpenSubKey(path, true);
                }
                else if (e.Hive == "HKCU")
                {
                    key = Registry.CurrentUser.OpenSubKey(e.KeyPath, true);
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

            if (MessageBoxHelper.Show($"Удалить '{e.FullName}'?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                if (string.IsNullOrEmpty(e.ValueName)) return;

                RegistryKey key = null;
                if (e.Hive == "HKLM")
                {
                    string path = e.KeyPath.StartsWith("SOFTWARE\\") || e.KeyPath.StartsWith("SYSTEM\\")
                        ? e.KeyPath : "SOFTWARE\\" + e.KeyPath;
                    if (isWinRE)
                    {
                        string prefix = path.StartsWith("SYSTEM\\") ? "BunnyBlack_Offline_SYSTEM\\" + path.Substring(7)
                                     : path.StartsWith("SOFTWARE\\") ? "BunnyBlack_Offline_SOFTWARE\\" + path.Substring(9)
                                     : path;
                        key = Registry.LocalMachine.OpenSubKey(prefix, true);
                    }
                    else key = Registry.LocalMachine.OpenSubKey(path, true);
                }
                else if (e.Hive == "HKCU")
                {
                    key = Registry.CurrentUser.OpenSubKey(e.KeyPath, true);
                }

                if (key != null && key.GetValue(e.ValueName) != null)
                {
                    key.DeleteValue(e.ValueName);
                    key.Dispose();
                    LoadData();
                    NedoMessageBox.Show("Удалено.");
                }
                else
                {
                    key?.Dispose();
                    NedoMessageBox.Show("Значение не найдено или нет прав.", isError: true);
                }
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true);
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
                        NedoMessageBox.Show("Не найдено: " + path, isError: true);
                }
            }
            catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
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
                NedoMessageBox.Show("Файл не найден: " + path, isError: true);
                return;
            }

            string err;
            if (Quarantine.Add(path, "Из автозагрузки: " + e.FullName, out err))
            {
                LoadData();
                NedoMessageBox.Show("Файл в карантине.");
            }
            else NedoMessageBox.Show("Ошибка: " + err, isError: true);
        }

        // ============================================================
        // ХЕЛПЕРЫ
        // ============================================================
        private Form FindOwner()
        {
            foreach (Form f in Application.OpenForms)
                if (f is MainForm) return f;
            return Form.ActiveForm;
        }
    }
}