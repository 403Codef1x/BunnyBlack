// language: C#, file: Forms/UsersForm.cs
// Полная замена.
// - OEM-866 встроена прямо в класс (GetOemEncoding) — внешний EncodingHelper не нужен.
// - ExecuteNetCommand не падает на GetEncoding(866).
// - Удаление, создание, смена пароля, вкл/выкл — работают.
// - Колонки: Имя, Статус.
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
        private bool isWinRE;

        private readonly List<RegistryHelper.UserEntry> users = new List<RegistryHelper.UserEntry>();

        private static readonly Regex InvalidNameChars =
            new Regex(@"[\s\\/:*?""<>|]", RegexOptions.Compiled);

        private static readonly HashSet<string> SkipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "administrator", "guest", "defaultaccount", "wdagutilityaccount",
            "defaultuser0", "defaultuser1",
            "администратор", "гость",
            "default", "default user", "all users",
            "все пользователи", "всё пользователи", "общие",
            "public", "$recycle.bin", "defaultapppool",
            "local service", "network service", "system",
            "codexsandbox", "codexsandboxoffline", "codexsandboxonline",
            "codexsandboxadmin", "codexsandboxuser",
        };

        private static readonly HashSet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "administrator", "guest",
            "администратор", "гость"
        };

        public UsersForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
            LoadUsersAsync();
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
                BackColor = Color.FromArgb(13, 13, 13)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = "Пользователи" + (isWinRE ? " (WinRE)" : ""),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(title, 0, 0);

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
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10),
                Margin = new Padding(0, 4, 8, 0)
            };
            filterBox.TextChanged += (s, e) => ApplyFilter();
            topBar.Controls.Add(filterBox);

            var refreshBtn = MakeButton("Обновить", 100, Color.FromArgb(30, 30, 30), Color.FromArgb(220, 220, 220));
            refreshBtn.Click += (s, e) => LoadUsersAsync();
            topBar.Controls.Add(refreshBtn);

            var createBtn = MakeButton("Создать", 100, Color.FromArgb(30, 55, 40), Color.FromArgb(136, 221, 170));
            createBtn.Click += (s, e) => CreateUser();
            topBar.Controls.Add(createBtn);

            var delBtn = MakeButton("Удалить", 100, Color.FromArgb(50, 20, 20), Color.FromArgb(255, 150, 150));
            delBtn.Click += (s, e) => DeleteSelected();
            topBar.Controls.Add(delBtn);

            var passBtn = MakeButton("Сменить пароль", 150, Color.FromArgb(30, 30, 30), Color.FromArgb(220, 220, 220));
            passBtn.Click += (s, e) => ChangePassword();
            topBar.Controls.Add(passBtn);

            var toggleBtn = MakeButton("Вкл/Выкл", 110, Color.FromArgb(40, 40, 60), Color.FromArgb(170, 180, 255));
            toggleBtn.Click += (s, e) => ToggleSelected();
            topBar.Controls.Add(toggleBtn);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(120, 120, 120),
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

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(25, 25, 25);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 200, 200);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grid.DefaultCellStyle.BackColor = Color.FromArgb(18, 18, 18);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 50, 60);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(255, 255, 255);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(14, 14, 14);

            grid.Columns.Add("Name", "Имя");
            grid.Columns.Add("Status", "Статус");
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
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9),
                Margin = new Padding(0, 2, 8, 0)
            };
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip { BackColor = Color.FromArgb(17, 17, 17), ForeColor = Color.FromArgb(216, 216, 216) };

            var create = new ToolStripMenuItem("Создать пользователя");
            create.Click += (s, e) => CreateUser();
            m.Items.Add(create);

            var del = new ToolStripMenuItem("Удалить");
            del.ForeColor = Color.FromArgb(255, 150, 150);
            del.Click += (s, e) => DeleteSelected();
            m.Items.Add(del);

            var pass = new ToolStripMenuItem("Сменить пароль");
            pass.Click += (s, e) => ChangePassword();
            m.Items.Add(pass);

            m.Items.Add(new ToolStripSeparator());

            var en = new ToolStripMenuItem("Включить");
            en.ForeColor = Color.FromArgb(136, 221, 170);
            en.Click += (s, e) => SetActive(true);
            m.Items.Add(en);

            var dis = new ToolStripMenuItem("Отключить");
            dis.ForeColor = Color.FromArgb(255, 150, 150);
            dis.Click += (s, e) => SetActive(false);
            m.Items.Add(dis);

            return m;
        }

        private async void LoadUsersAsync()
        {
            statusLabel.Text = "Загрузка…";
            grid.Rows.Clear();
            users.Clear();

            try
            {
                var list = await Task.Run(() => RegistryHelper.GetUserEntries());

                this.Invoke((Action)(() =>
                {
                    int shown = 0;
                    foreach (var u in list)
                    {
                        if (SkipNames.Contains(u.Name)) continue;
                        if (string.IsNullOrWhiteSpace(u.Name)) continue;

                        users.Add(u);
                        AddRow(u);
                        shown++;
                    }
                    statusLabel.Text = $"Пользователей: {shown}";
                    ApplyFilter();
                }));
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Ошибка: " + ex.Message;
            }
        }

        private void AddRow(RegistryHelper.UserEntry u)
        {
            int idx = grid.Rows.Add(u.Name, u.Disabled ? "Отключён" : "Включён");
            var row = grid.Rows[idx];
            row.Tag = u;

            if (u.Disabled)
            {
                row.Cells[1].Style.ForeColor = Color.FromArgb(255, 150, 150);
                row.Cells[1].Style.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            }
            else row.Cells[1].Style.ForeColor = Color.FromArgb(136, 221, 170);

            if (u.RID == 500 || u.IsAdmin)
            {
                row.Cells[0].Style.ForeColor = Color.FromArgb(255, 200, 120);
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
            var name = ShowInputDialog("Создать пользователя", "Имя пользователя:");
            if (string.IsNullOrEmpty(name)) return;

            string err;
            if (!IsValidUsername(name, out err)) { NedoMessageBox.Show(err, isError: true); return; }
            if (ProtectedNames.Contains(name)) { NedoMessageBox.Show("Защищённое имя.", isError: true); return; }

            var pass = ShowInputDialog("Создать пользователя", "Пароль:", true);
            if (string.IsNullOrEmpty(pass)) { NedoMessageBox.Show("Пароль не может быть пустым.", isError: true); return; }

            string cmdErr;
            if (ExecuteNetCommand($"user \"{name}\" \"{pass}\" /add", out cmdErr))
            {
                ExecuteNetCommand($"user \"{name}\" \"{pass}\"", out _);
                NedoMessageBox.Show($"Пользователь '{name}' создан.");
                LoadUsersAsync();
            }
            else
            {
                NedoMessageBox.Show("Ошибка создания: " + cmdErr, isError: true);
            }
        }

        private void DeleteSelected()
        {
            var u = GetSelected();
            if (u == null)
            {
                NedoMessageBox.Show("Выбери пользователя в списке.", isError: true);
                return;
            }

            if (ProtectedNames.Contains(u.Name) || u.RID == 500)
            {
                NedoMessageBox.Show($"'{u.Name}' — защищённый аккаунт, удалить нельзя.", isError: true);
                return;
            }

            if (MessageBoxHelper.Show($"Удалить пользователя '{u.Name}'?",
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            string err;
            if (!ExecuteNetCommand($"user \"{u.Name}\" /delete", out err))
            {
                NedoMessageBox.Show("Не удалось удалить:\n" + err, isError: true);
                return;
            }

            try
            {
                string drive = RegistryHelper.GetSystemDrive();
                string profile = Path.Combine(drive, "Users", u.Name);
                if (Directory.Exists(profile) &&
                    profile.IndexOf("systemprofile", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (MessageBoxHelper.Show($"Удалить папку профиля?\n{profile}",
                        "Профиль", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        try { Directory.Delete(profile, true); } catch { }
                    }
                }
            }
            catch { }

            NedoMessageBox.Show($"Пользователь '{u.Name}' удалён.");
            LoadUsersAsync();
        }

        private void ChangePassword()
        {
            var u = GetSelected();
            if (u == null) { NedoMessageBox.Show("Выбери пользователя.", isError: true); return; }

            var pass = ShowInputDialog("Смена пароля", $"Новый пароль для '{u.Name}':", true);
            if (string.IsNullOrEmpty(pass)) return;

            string err;
            if (ExecuteNetCommand($"user \"{u.Name}\" \"{pass}\"", out err))
                NedoMessageBox.Show("Пароль изменён.");
            else
                NedoMessageBox.Show("Ошибка смены пароля:\n" + err, isError: true);
        }

        private void ToggleSelected()
        {
            var u = GetSelected();
            if (u == null) { NedoMessageBox.Show("Выбери пользователя.", isError: true); return; }
            SetActive(u.Disabled);
        }

        private void SetActive(bool enable)
        {
            var u = GetSelected();
            if (u == null) return;

            string err;
            if (ExecuteNetCommand($"user \"{u.Name}\" /active:{(enable ? "yes" : "no")}", out err))
            {
                NedoMessageBox.Show($"{(enable ? "Включён" : "Отключён")}: {u.Name}");
                LoadUsersAsync();
            }
            else NedoMessageBox.Show("Ошибка:\n" + err, isError: true);
        }

        private RegistryHelper.UserEntry GetSelected()
        {
            if (grid.SelectedRows.Count == 0) return null;
            return grid.SelectedRows[0].Tag as RegistryHelper.UserEntry;
        }

        // ============================================================
        // EXECUTE NET — с безопасной кодировкой
        // ============================================================
        private bool ExecuteNetCommand(string args, out string error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "net.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                Encoding enc = GetOemEncoding();
                if (enc != null)
                {
                    psi.StandardOutputEncoding = enc;
                    psi.StandardErrorEncoding = enc;
                }

                using (var p = Process.Start(psi))
                {
                    if (p == null) { error = "net.exe не запустился"; return false; }

                    string stdout = "", stderr = "";
                    try { stdout = p.StandardOutput.ReadToEnd(); } catch { }
                    try { stderr = p.StandardError.ReadToEnd(); } catch { }

                    if (!p.WaitForExit(8000))
                    {
                        try { p.Kill(); } catch { }
                        error = "Таймаут net.exe";
                        return false;
                    }

                    string combined = (stdout + "\n" + stderr).Trim();

                    if (p.ExitCode != 0)
                    {
                        error = CleanNetError(combined);
                        return false;
                    }

                    string lower = combined.ToLowerInvariant();
                    if (lower.Contains("отказано") || lower.Contains("denied") ||
                        lower.Contains("системная ошибка") || lower.Contains("system error") ||
                        lower.Contains("не найден") || lower.Contains("not found") ||
                        lower.Contains("уже существует") || lower.Contains("already exists") ||
                        lower.Contains("не соответствует") || lower.Contains("does not meet") ||
                        lower.Contains("ошибка") || lower.Contains("error"))
                    {
                        error = CleanNetError(combined);
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ============================================================
        // OEM-кодировка с безопасным фоллбэком — без внешнего хелпера
        // ============================================================
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
            if (string.IsNullOrEmpty(raw))
            {
                int code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                if (code == 5) return "Отказано в доступе. Запусти программу от имени администратора.";
                if (code == 2) return "Пользователь не найден.";
                return "неизвестная ошибка (net.exe вернул пустой ответ)";
            }

            string s = raw.Replace("\r", "").Replace("\n", " ").Trim();
            return s.Length > 500 ? s.Substring(0, 500) + "…" : s;
        }

        private bool IsValidUsername(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name)) { error = "Имя не может быть пустым."; return false; }
            if (name.Length > 20) { error = "Имя длиннее 20 символов."; return false; }
            if (InvalidNameChars.IsMatch(name))
            {
                error = "Имя не может содержать пробелы и символы: \\ / : * ? \" < > |";
                return false;
            }
            if (name.EndsWith(".")) { error = "Имя не может заканчиваться точкой."; return false; }
            return true;
        }

        private string ShowInputDialog(string title, string message, bool password = false)
        {
            using (var form = new Form
            {
                Text = title,
                Size = new Size(420, 160),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
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
                    ForeColor = Color.FromArgb(216, 216, 216),
                    BackColor = Color.Transparent
                };

                var tb = new TextBox
                {
                    Left = 20,
                    Top = 50,
                    Width = 370,
                    BackColor = Color.FromArgb(24, 24, 24),
                    ForeColor = Color.FromArgb(216, 216, 216),
                    BorderStyle = BorderStyle.FixedSingle
                };
                if (password) tb.PasswordChar = '•';

                var ok = new Button
                {
                    Text = "OK",
                    Left = 220,
                    Top = 85,
                    Width = 80,
                    Height = 30,
                    DialogResult = DialogResult.OK,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(35, 55, 75),
                    ForeColor = Color.FromArgb(240, 240, 240)
                };

                var cancel = new Button
                {
                    Text = "Отмена",
                    Left = 310,
                    Top = 85,
                    Width = 80,
                    Height = 30,
                    DialogResult = DialogResult.Cancel,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(22, 22, 22),
                    ForeColor = Color.FromArgb(170, 170, 170)
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
    }
}