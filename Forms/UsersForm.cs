// language: C#, file: Forms/UsersForm.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class UsersForm : UserControl
    {
        private DataGridView grid;
        private FlowLayoutPanel topBar;
        private TextBox filterBox;
        private Label statusLabel;
        private Label titleLabel;
        private bool isWinRE;

        private readonly List<RegistryHelper.UserEntry> users = new List<RegistryHelper.UserEntry>();

        private static readonly Regex InvalidNameChars =
            new Regex(@"[\s\\/:*?""<>|]", RegexOptions.Compiled);

        private static readonly HashSet<string> SkipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "administrator", "guest", "defaultaccount", "wdagutilityaccount",
            "defaultuser0", "defaultuser1", "администратор", "гость",
            "default", "default user", "all users",
            "все пользователи", "всё пользователи", "общие",
            "public", "$recycle.bin", "defaultapppool",
            "local service", "network service", "system",
            "codexsandbox", "codexsandboxoffline", "codexsandboxonline",
            "codexsandboxadmin", "codexsandboxuser",
        };

        private static readonly HashSet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "administrator", "guest", "администратор", "гость"
        };

        public UsersForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;
            InitializeComponent();
            LoadUsersAsync();

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
            if (titleLabel != null) titleLabel.Text = Loc.T("users.title");
            if (grid != null && grid.Columns.Count >= 2)
            {
                grid.Columns[0].HeaderText = Loc.T("users.col.name");
                grid.Columns[1].HeaderText = Loc.T("users.col.status");
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
                Padding = new Padding(28, 24, 28, 20),
                BackColor = ThemeManager.Background
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            titleLabel = new Label
            {
                Text = Loc.T("users.title"),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(titleLabel, 0, 0);

            topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 4, 0, 0),
                WrapContents = false
            };

            filterBox = new TextBox
            {
                Width = 220,
                BackColor = ThemeManager.Input,
                ForeColor = ThemeManager.Foreground,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10),
                Margin = new Padding(0, 4, 8, 0)
            };
            filterBox.TextChanged += (s, e) => ApplyFilter();
            topBar.Controls.Add(filterBox);

            var refreshBtn = MakeButton(Loc.T("btn.refresh"), 100, ThemeManager.PanelAlt, ThemeManager.Foreground);
            refreshBtn.Click += (s, e) => LoadUsersAsync();
            topBar.Controls.Add(refreshBtn);

            var createBtn = MakeButton(Loc.T("btn.create"), 100, ThemeManager.SuccessBack, ThemeManager.Success);
            createBtn.Click += (s, e) => CreateUser();
            topBar.Controls.Add(createBtn);

            var delBtn = MakeButton(Loc.T("btn.delete"), 100, ThemeManager.DangerBack, ThemeManager.Danger);
            delBtn.Click += (s, e) => DeleteSelected();
            topBar.Controls.Add(delBtn);

            var passBtn = MakeButton(Loc.T("users.change"), 150, ThemeManager.PanelAlt, ThemeManager.Foreground);
            passBtn.Click += (s, e) => ChangePassword();
            topBar.Controls.Add(passBtn);

            var toggleBtn = MakeButton(Loc.T("users.toggle"), 110, ThemeManager.PanelAlt, ThemeManager.Foreground);
            toggleBtn.Click += (s, e) => ToggleSelected();
            topBar.Controls.Add(toggleBtn);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = ThemeManager.Muted,
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(16, 8, 0, 0)
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
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None
            };
            grid.RowTemplate.Height = 30;

            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null, grid, new object[] { true });

            grid.ColumnHeadersDefaultCellStyle.BackColor = ThemeManager.Header;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ThemeManager.Foreground;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grid.DefaultCellStyle.BackColor = ThemeManager.Row;
            grid.DefaultCellStyle.ForeColor = ThemeManager.Foreground;
            grid.DefaultCellStyle.SelectionBackColor = ThemeManager.Selection;
            grid.DefaultCellStyle.SelectionForeColor = ThemeManager.SelectionText;
            grid.AlternatingRowsDefaultCellStyle.BackColor = ThemeManager.RowAlt;

            grid.Columns.Add("Name", Loc.T("users.col.name"));
            grid.Columns.Add("Status", Loc.T("users.col.status"));
            grid.Columns[0].FillWeight = 70;
            grid.Columns[1].FillWeight = 30;
            grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ChangePassword(); };
            grid.ContextMenuStrip = BuildContextMenu();

            layout.Controls.Add(grid, 0, 2);
            this.Controls.Add(layout);
        }

        private Button MakeButton(string text, int width, Color back, Color fore)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Border },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9),
                Margin = new Padding(0, 2, 8, 0)
            };
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip
            {
                BackColor = ThemeManager.PanelAlt,
                ForeColor = ThemeManager.Foreground
            };

            var create = new ToolStripMenuItem(Loc.T("btn.create"));
            create.Click += (s, e) => CreateUser();
            m.Items.Add(create);

            var del = new ToolStripMenuItem(Loc.T("btn.delete"));
            del.ForeColor = ThemeManager.Danger;
            del.Click += (s, e) => DeleteSelected();
            m.Items.Add(del);

            var pass = new ToolStripMenuItem(Loc.T("users.change"));
            pass.Click += (s, e) => ChangePassword();
            m.Items.Add(pass);

            m.Items.Add(new ToolStripSeparator());

            var en = new ToolStripMenuItem("On");
            en.ForeColor = ThemeManager.Success;
            en.Click += (s, e) => SetActive(true);
            m.Items.Add(en);

            var dis = new ToolStripMenuItem("Off");
            dis.ForeColor = ThemeManager.Danger;
            dis.Click += (s, e) => SetActive(false);
            m.Items.Add(dis);

            return m;
        }

        private async void LoadUsersAsync()
        {
            statusLabel.Text = Loc.T("status.loading");
            grid.Rows.Clear();
            users.Clear();

            try
            {
                var list = await Task.Run(() => RegistryHelper.GetUserEntries());

                this.Invoke((Action)(() =>
                {
                    int total = list == null ? 0 : list.Count;
                    int skipped = 0;
                    int shown = 0;

                    if (list != null)
                    {
                        foreach (var u in list)
                        {
                            if (string.IsNullOrWhiteSpace(u.Name)) { skipped++; continue; }
                            if (SkipNames.Contains(u.Name.Trim())) { skipped++; continue; }

                            users.Add(u);
                            AddRow(u);
                            shown++;
                        }
                    }

                    statusLabel.Text = $"Shown: {shown} (total: {total}, skipped: {skipped})";
                    ApplyFilter();
                }));
            }
            catch (Exception ex)
            {
                BbLog.Error("[LoadUsersAsync]", ex);
                statusLabel.Text = "Error: " + ex.Message;
            }
        }

        private void AddRow(RegistryHelper.UserEntry u)
        {
            int idx = grid.Rows.Add(u.Name, u.Disabled ? Loc.T("users.disabled") : Loc.T("users.enabled"));
            var row = grid.Rows[idx];
            row.Tag = u;

            if (u.Disabled)
            {
                row.Cells[1].Style.ForeColor = ThemeManager.Danger;
                row.Cells[1].Style.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            }
            else row.Cells[1].Style.ForeColor = ThemeManager.Success;

            if (u.RID == 500 || u.IsAdmin)
            {
                row.Cells[0].Style.ForeColor = ThemeManager.Warning;
                row.Cells[0].Style.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            }
        }

        private void ApplyFilter()
        {
            string f = (filterBox.Text ?? "").Trim().ToLowerInvariant();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (string.IsNullOrEmpty(f)) { row.Visible = true; continue; }
                string name = row.Cells[0].Value?.ToString() ?? "";
                row.Visible = name.ToLowerInvariant().Contains(f);
            }
        }

        private void CreateUser()
        {
            var name = ShowInputDialog(Loc.T("btn.create"), "Name:");
            if (string.IsNullOrEmpty(name)) return;

            string err;
            if (!IsValidUsername(name, out err)) { NedoMessageBox.Show(err, isError: true); return; }
            if (ProtectedNames.Contains(name)) { NedoMessageBox.Show("Protected.", isError: true); return; }

            var pass = ShowInputDialog(Loc.T("btn.create"), "Password:", true);
            if (string.IsNullOrEmpty(pass)) { NedoMessageBox.Show("Empty password.", isError: true); return; }

            string cmdErr;
            if (ExecuteNetCommand($"user \"{name}\" \"{pass}\" /add", out cmdErr))
            {
                ExecuteNetCommand($"user \"{name}\" \"{pass}\"", out _);
                NedoMessageBox.Show($"Created: {name}");
                LoadUsersAsync();
            }
            else
            {
                NedoMessageBox.Show("Error: " + cmdErr, isError: true);
            }
        }

        private void DeleteSelected()
        {
            var u = GetSelected();
            if (u == null) return;
            if (ProtectedNames.Contains(u.Name) || u.RID == 500)
            {
                NedoMessageBox.Show($"'{u.Name}' protected.", isError: true);
                return;
            }

            if (MessageBoxHelper.Show($"Delete '{u.Name}'?", Loc.T("warn.confirm"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            string err;
            if (ExecuteNetCommand($"user \"{u.Name}\" /delete", out err))
            {
                NedoMessageBox.Show($"Deleted: {u.Name}");
                LoadUsersAsync();
            }
            else
            {
                NedoMessageBox.Show("Error: " + err, isError: true);
            }
        }

        private void ChangePassword()
        {
            var u = GetSelected();
            if (u == null) return;

            var pass = ShowInputDialog(Loc.T("users.change"), $"New password for '{u.Name}':", true);
            if (string.IsNullOrEmpty(pass)) return;

            string err;
            if (ExecuteNetCommand($"user \"{u.Name}\" \"{pass}\"", out err))
                NedoMessageBox.Show("Password changed.");
            else
                NedoMessageBox.Show("Error: " + err, isError: true);
        }

        private void ToggleSelected()
        {
            var u = GetSelected();
            if (u == null) return;
            SetActive(u.Disabled);
        }

        private void SetActive(bool enable)
        {
            var u = GetSelected();
            if (u == null) return;

            string err;
            if (ExecuteNetCommand($"user \"{u.Name}\" /active:{(enable ? "yes" : "no")}", out err))
            {
                NedoMessageBox.Show($"{(enable ? "Enabled" : "Disabled")}: {u.Name}");
                LoadUsersAsync();
            }
            else NedoMessageBox.Show("Error: " + err, isError: true);
        }

        private RegistryHelper.UserEntry GetSelected()
        {
            if (grid.SelectedRows.Count == 0) return null;
            return grid.SelectedRows[0].Tag as RegistryHelper.UserEntry;
        }

        private bool ExecuteNetCommand(string args, out string error)
        {
            error = null;
            try
            {
                Encoding enc = GetOemEncoding();

                var psi = new ProcessStartInfo
                {
                    FileName = "net.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                if (enc != null)
                {
                    psi.StandardOutputEncoding = enc;
                    psi.StandardErrorEncoding = enc;
                }

                using (var p = Process.Start(psi))
                {
                    if (p == null) { error = "net.exe not started"; return false; }

                    string stdout = "", stderr = "";
                    try { stdout = p.StandardOutput.ReadToEnd(); } catch { }
                    try { stderr = p.StandardError.ReadToEnd(); } catch { }

                    if (!p.WaitForExit(8000))
                    {
                        try { p.Kill(); } catch { }
                        error = "Timeout";
                        return false;
                    }

                    string combined = (stdout + "\n" + stderr).Trim();
                    if (p.ExitCode != 0) { error = CleanNetError(combined); return false; }

                    string lower = combined.ToLowerInvariant();
                    if (lower.Contains("отказано") || lower.Contains("denied") ||
                        lower.Contains("системная ошибка") || lower.Contains("system error") ||
                        lower.Contains("не найден") || lower.Contains("not found") ||
                        lower.Contains("уже существует") || lower.Contains("already exists") ||
                        lower.Contains("не соответствует") || lower.Contains("does not meet"))
                    {
                        error = CleanNetError(combined);
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static Encoding GetOemEncoding()
        {
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                return System.Text.Encoding.GetEncoding(866);
            }
            catch
            {
                try { return System.Text.Encoding.GetEncoding(0); }
                catch { return null; }
            }
        }

        private string CleanNetError(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "unknown error";
            string s = raw.Replace("\r", "").Replace("\n", " ").Trim();
            return s.Length > 400 ? s.Substring(0, 400) + "…" : s;
        }

        private bool IsValidUsername(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name)) { error = "Empty name."; return false; }
            if (name.Length > 20) { error = "Name > 20 chars."; return false; }
            if (InvalidNameChars.IsMatch(name))
            {
                error = "Invalid chars.";
                return false;
            }
            if (name.EndsWith(".")) { error = "Ends with dot."; return false; }
            return true;
        }

        private string ShowInputDialog(string title, string message, bool password = false)
        {
            using (var form = new Form
            {
                Text = title,
                Size = new Size(420, 160),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = ThemeManager.Background,
                ForeColor = ThemeManager.Foreground,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                ShowInTaskbar = false
            })
            {
                var label = new Label
                {
                    Text = message,
                    Left = 20,
                    Top = 20,
                    Width = 370,
                    ForeColor = ThemeManager.Foreground,
                    BackColor = Color.Transparent
                };

                var tb = new TextBox
                {
                    Left = 20,
                    Top = 50,
                    Width = 370,
                    BackColor = ThemeManager.Input,
                    ForeColor = ThemeManager.Foreground,
                    BorderStyle = BorderStyle.FixedSingle
                };
                if (password) tb.PasswordChar = '•';

                var ok = new Button
                {
                    Text = Loc.T("btn.ok"),
                    Left = 220,
                    Top = 85,
                    Width = 80,
                    Height = 30,
                    DialogResult = DialogResult.OK,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ThemeManager.Accent,
                    ForeColor = ThemeManager.AccentText
                };

                var cancel = new Button
                {
                    Text = Loc.T("btn.cancel"),
                    Left = 310,
                    Top = 85,
                    Width = 80,
                    Height = 30,
                    DialogResult = DialogResult.Cancel,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ThemeManager.PanelAlt,
                    ForeColor = ThemeManager.Foreground
                };

                form.Controls.Add(label);
                form.Controls.Add(tb);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                Form owner = FindOwner();
                var dr = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
                return dr == DialogResult.OK ? tb.Text.Trim() : "";
            }
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