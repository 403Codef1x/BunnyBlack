using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class UsersForm : UserControl
    {
        private DataGridView usersGrid;
        private bool isWinRE;
        private List<string> currentUsers = new List<string>();

        public UsersForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
            LoadUsers();
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
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));

            var title = new Label
            {
                Text = "Пользователи" + (isWinRE ? " (WinRE)" : ""),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(title, 0, 0);

            usersGrid = new DataGridView
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
                ColumnHeadersHeight = 35,
                RowTemplate = { Height = 30 }
            };

            usersGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 30, 30);
            usersGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 200, 200);
            usersGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);

            usersGrid.DefaultCellStyle.BackColor = Color.FromArgb(22, 22, 22);
            usersGrid.DefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            usersGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 50, 60);
            usersGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(255, 255, 255);

            usersGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(17, 17, 17);
            usersGrid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);

            usersGrid.Columns.Add("Name", "Имя");
            usersGrid.Columns[0].Width = 300;

            // ============================================================
            // КОНТЕКСТНОЕ МЕНЮ
            // ============================================================
            var contextMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };

            var refreshItem = new ToolStripMenuItem("Обновить список");
            refreshItem.Click += (s, e) => LoadUsers();
            contextMenu.Items.Add(refreshItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var createItem = new ToolStripMenuItem("Создать пользователя");
            createItem.Click += (s, e) => CreateUser();
            contextMenu.Items.Add(createItem);

            var deleteItem = new ToolStripMenuItem("Удалить");
            deleteItem.ForeColor = Color.FromArgb(255, 150, 150);
            deleteItem.Click += (s, e) => DeleteUser();
            contextMenu.Items.Add(deleteItem);

            var passItem = new ToolStripMenuItem("Сменить пароль");
            passItem.Click += (s, e) => ChangePassword();
            contextMenu.Items.Add(passItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var enableItem = new ToolStripMenuItem("Включить");
            enableItem.Click += (s, e) => ToggleUser(true);
            contextMenu.Items.Add(enableItem);

            var disableItem = new ToolStripMenuItem("Отключить");
            disableItem.Click += (s, e) => ToggleUser(false);
            contextMenu.Items.Add(disableItem);

            usersGrid.ContextMenuStrip = contextMenu;

            layout.Controls.Add(usersGrid, 0, 1);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 5, 0, 0)
            };

            var refreshBtn = new Button
            {
                Text = "Обновить",
                Height = 36,
                Width = 120,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(240, 240, 240),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11)
            };
            refreshBtn.Click += (s, e) => LoadUsers();
            btnPanel.Controls.Add(refreshBtn);

            var createBtn = new Button
            {
                Text = "Создать",
                Height = 36,
                Width = 120,
                BackColor = Color.FromArgb(15, 34, 24),
                ForeColor = Color.FromArgb(136, 221, 170),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(51, 102, 68) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11)
            };
            createBtn.Click += (s, e) => CreateUser();
            btnPanel.Controls.Add(createBtn);

            var delBtn = new Button
            {
                Text = "Удалить",
                Height = 36,
                Width = 120,
                BackColor = Color.FromArgb(50, 20, 20),
                ForeColor = Color.FromArgb(255, 150, 150),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(80, 40, 40) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11)
            };
            delBtn.Click += (s, e) => DeleteUser();
            btnPanel.Controls.Add(delBtn);

            var passBtn = new Button
            {
                Text = "Сменить пароль",
                Height = 36,
                Width = 160,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(240, 240, 240),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11)
            };
            passBtn.Click += (s, e) => ChangePassword();
            btnPanel.Controls.Add(passBtn);

            var enableBtn = new Button
            {
                Text = "Включить",
                Height = 36,
                Width = 120,
                BackColor = Color.FromArgb(15, 34, 24),
                ForeColor = Color.FromArgb(136, 221, 170),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(51, 102, 68) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11)
            };
            enableBtn.Click += (s, e) => ToggleUser(true);
            btnPanel.Controls.Add(enableBtn);

            var disableBtn = new Button
            {
                Text = "Отключить",
                Height = 36,
                Width = 120,
                BackColor = Color.FromArgb(50, 20, 20),
                ForeColor = Color.FromArgb(255, 150, 150),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(80, 40, 40) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11)
            };
            disableBtn.Click += (s, e) => ToggleUser(false);
            btnPanel.Controls.Add(disableBtn);

            layout.Controls.Add(btnPanel, 0, 2);
            this.Controls.Add(layout);
        }

        // ============================================================
        // ЗАГРУЗКА ПОЛЬЗОВАТЕЛЕЙ (WMI - САМЫЙ НАДЁЖНЫЙ)
        // ============================================================
        private void LoadUsers()
        {
            usersGrid.Rows.Clear();
            currentUsers.Clear();

            try
            {
                List<string> users = new List<string>();

                if (!isWinRE)
                {
                    // ИСПОЛЬЗУЕМ WMI (РАБОТАЕТ БЕЗ КОНСОЛЬНЫХ КОСЯКОВ)
                    users = GetUsersFromWmi();
                }

                // РЕЗЕРВНЫЙ МЕТОД (ЕСЛИ WMI НЕ СРАБОТАЛ ИЛИ МЫ В WINRE)
                if (users.Count == 0)
                {
                    users = GetUsersFromProfileList();
                }

                foreach (var user in users)
                {
                    usersGrid.Rows.Add(user);
                    currentUsers.Add(user);
                }

                if (users.Count == 0)
                {
                    usersGrid.Rows.Add("Нет пользователей");
                }
            }
            catch (Exception ex)
            {
                usersGrid.Rows.Add("Ошибка загрузки: " + ex.Message);
            }
        }

        // ============================================================
        // WMI ЗАПРОС - НАДЁЖНОЕ ЧТЕНИЕ СПИСКА
        // ============================================================
        private List<string> GetUsersFromWmi()
        {
            var users = new List<string>();

            try
            {
                string query = "SELECT Name FROM Win32_UserAccount WHERE LocalAccount = True AND Disabled = False";

                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString();
                        if (!string.IsNullOrEmpty(name) && !users.Contains(name))
                        {
                            if (!IsSystemUser(name))
                            {
                                users.Add(name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                usersGrid.Rows.Add("Ошибка WMI: " + ex.Message);
            }

            return users;
        }

        // ============================================================
        // ПОЛУЧЕНИЕ ПОЛЬЗОВАТЕЛЕЙ ИЗ PROFILELIST (РЕЗЕРВНЫЙ МЕТОД)
        // ============================================================
        private List<string> GetUsersFromProfileList()
        {
            var users = new List<string>();
            var processedSids = new HashSet<string>();

            try
            {
                string profileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
                RegistryKey baseKey = isWinRE ? Registry.LocalMachine : Registry.LocalMachine;

                using (var key = baseKey.OpenSubKey(profileListPath))
                {
                    if (key == null) return users;

                    foreach (string sid in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var subKey = key.OpenSubKey(sid))
                            {
                                if (subKey == null) continue;

                                // ОТСЕИВАЕМ СИСТЕМНЫЕ СЛУЖЕБНЫЕ SID
                                if (sid.StartsWith("S-1-5-18") ||  // LocalSystem
                                    sid.StartsWith("S-1-5-19") ||  // LocalService
                                    sid.StartsWith("S-1-5-20") ||  // NetworkService
                                    sid.StartsWith("S-1-5-21-") == false) // Не пользовательский SID
                                {
                                    continue;
                                }

                                string profilePath = subKey.GetValue("ProfileImagePath")?.ToString() ?? "";
                                if (string.IsNullOrEmpty(profilePath)) continue;

                                string userName = Path.GetFileName(profilePath);
                                if (string.IsNullOrEmpty(userName)) continue;

                                // ПРОВЕРКА: СКРЫТ В СПЕЦИАЛЬНЫХ АККАУНТАХ?
                                bool isHidden = IsUserHiddenInRegistry(userName);
                                if (isHidden) continue;

                                // ПРОВЕРКА: СИСТЕМНОЕ ИМЯ?
                                if (IsSystemUser(userName)) continue;

                                // Убираем дубликаты
                                if (!processedSids.Contains(sid) && !users.Contains(userName))
                                {
                                    users.Add(userName);
                                    processedSids.Add(sid);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            users.Sort();
            return users;
        }

        // ============================================================
        // ПРОВЕРКА СКРЫТОСТИ ЧЕРЕЗ РЕЕСТР
        // ============================================================
        private bool IsUserHiddenInRegistry(string username)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(username);
                        if (value != null)
                        {
                            if (value is int intVal && intVal == 0)
                                return true;
                            if (value is byte[] bytes && bytes.Length > 0 && bytes[0] == 0)
                                return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        // ============================================================
        // ПРОВЕРКА - СИСТЕМНЫЙ ЛИ ПОЛЬЗОВАТЕЛЬ
        // ============================================================
        private bool IsSystemUser(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;

            var systemUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // СИСТЕМНЫЕ
                "ADMINISTRATOR", "GUEST", "DEFAULTACCOUNT", "WDAGUTILITYACCOUNT",
                // DWM
                "DWM-1", "DWM-2", "DWM-3", "DWM-4", "DWM-5", "DWM-6", "DWM-7", "DWM-8",
                // ДРУГИЕ СКРЫТЫЕ
                "DEFAULTUSER0", "DEFAULTUSER1", "IUSR", "IWAM", "ASPNET", "SUPPORT_388945a0",
                // Codex Sandbox
                "CODESANDBOXOFFLINE", "CODESANDBOXONLINE", "CODESANDBOX",
                "CODESANDBOXADMIN", "CODESANDBOXUSER",
                // ПАПКИ
                "Public", "Default", "All Users", "Default User"
            };

            if (systemUsers.Contains(name)) return true;
            if (name.StartsWith("$")) return true;
            if (name.StartsWith("DWM-", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("CodexSandbox", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        // ============================================================
        // ПОЛУЧЕНИЕ ВЫБРАННОГО ИМЕНИ
        // ============================================================
        private string GetSelectedName()
        {
            if (usersGrid.SelectedRows.Count == 0) return null;
            var name = usersGrid.SelectedRows[0].Cells[0].Value?.ToString();
            if (string.IsNullOrEmpty(name)) return null;
            if (name == "Нет пользователей") return null;
            if (name.StartsWith("Ошибка загрузки:")) return null;
            return name;
        }

        // ============================================================
        // СОЗДАНИЕ ПОЛЬЗОВАТЕЛЯ (ИСПРАВЛЕНА ЛОГИКА)
        // ============================================================
        private void CreateUser()
        {
            var name = ShowInputDialog("Создать пользователя", "Имя пользователя:");
            if (string.IsNullOrEmpty(name)) return;

            if (IsSystemUser(name))
            {
                MessageBox.Show("Это имя зарезервировано для системных пользователей", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var pass = ShowInputDialog("Создать пользователя", "Пароль:", true);
            if (string.IsNullOrEmpty(pass)) return;

            bool result;
            if (isWinRE)
            {
                result = RegistryHelper.CreateUserNetApi(name, pass);
            }
            else
            {
                // ИСПОЛЬЗУЕМ ФАЙЛОВЫЙ МЕТОД ДЛЯ ВЫПОЛНЕНИЯ
                string tempFile = Path.Combine(Path.GetTempPath(), "user_create.txt");
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c net user \"{name}\" \"{pass}\" /add > \"{tempFile}\" 2>&1",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false
                    };

                    using (Process process = Process.Start(psi))
                    {
                        if (process == null) result = false;
                        else
                        {
                            process.WaitForExit();
                            result = process.ExitCode == 0;
                        }
                    }
                }
                catch
                {
                    result = false;
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }

            if (result)
            {
                MessageBox.Show($"Пользователь {name} создан", "Успешно",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadUsers();
            }
            else
            {
                MessageBox.Show($"Ошибка при создании пользователя {name}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // УДАЛЕНИЕ ПОЛЬЗОВАТЕЛЯ
        // ============================================================
        private void DeleteUser()
        {
            var name = GetSelectedName();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Выберите пользователя", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (IsSystemUser(name))
            {
                MessageBox.Show("Нельзя удалить системного пользователя", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show($"Удалить пользователя '{name}'?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                bool result;
                if (isWinRE)
                    result = RegistryHelper.DeleteUserNetApi(name);
                else
                    result = ExecuteNetCommand($"user \"{name}\" /delete");

                if (result)
                {
                    MessageBox.Show($"Пользователь {name} удален", "Успешно",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    LoadUsers();
                }
                else
                {
                    MessageBox.Show($"Ошибка при удалении пользователя {name}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ============================================================
        // СМЕНА ПАРОЛЯ
        // ============================================================
        private void ChangePassword()
        {
            var name = GetSelectedName();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Выберите пользователя", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (IsSystemUser(name))
            {
                MessageBox.Show("Нельзя менять пароль системного пользователя", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var pass = ShowInputDialog("Сменить пароль", $"Новый пароль для {name}:", true);
            if (string.IsNullOrEmpty(pass)) return;

            bool result;
            if (isWinRE)
                result = RegistryHelper.SetUserPasswordNetApi(name, pass);
            else
                result = ExecuteNetCommand($"user \"{name}\" \"{pass}\"");

            if (result)
            {
                MessageBox.Show($"Пароль для {name} изменен", "Успешно",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show($"Ошибка при смене пароля для {name}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // ВКЛЮЧЕНИЕ/ОТКЛЮЧЕНИЕ ПОЛЬЗОВАТЕЛЯ
        // ============================================================
        private void ToggleUser(bool enable)
        {
            var name = GetSelectedName();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Выберите пользователя", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (IsSystemUser(name))
            {
                MessageBox.Show($"Нельзя {(enable ? "включить" : "отключить")} системного пользователя", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string action = enable ? "включен" : "отключен";
            bool result;
            if (isWinRE)
                result = enable ? RegistryHelper.EnableUserNetApi(name) : RegistryHelper.DisableUserNetApi(name);
            else
                result = ExecuteNetCommand($"user \"{name}\" /active:{(enable ? "yes" : "no")}");

            if (result)
            {
                MessageBox.Show($"Пользователь {name} {action}", "Успешно",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadUsers();
            }
            else
            {
                MessageBox.Show($"Ошибка при {action} пользователя {name}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // ВЫПОЛНЕНИЕ КОМАНДЫ NET
        // ============================================================
        private bool ExecuteNetCommand(string arguments)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "net",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process == null) return false;
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        // ============================================================
        // ДИАЛОГ ВВОДА
        // ============================================================
        private string ShowInputDialog(string title, string message, bool password = false)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.Size = new Size(400, 150);
                form.StartPosition = FormStartPosition.CenterParent;
                form.BackColor = Color.FromArgb(17, 17, 17);
                form.ForeColor = Color.FromArgb(216, 216, 216);

                var label = new Label
                {
                    Text = message,
                    Left = 20,
                    Top = 20,
                    Width = 350,
                    ForeColor = Color.FromArgb(216, 216, 216),
                    BackColor = Color.Transparent
                };

                var textBox = new TextBox
                {
                    Left = 20,
                    Top = 50,
                    Width = 350,
                    BackColor = Color.FromArgb(24, 24, 24),
                    ForeColor = Color.FromArgb(216, 216, 216),
                    BorderStyle = BorderStyle.FixedSingle
                };
                if (password) textBox.PasswordChar = '*';

                var okBtn = new Button
                {
                    Text = "OK",
                    Left = 220,
                    Top = 80,
                    Width = 80,
                    DialogResult = DialogResult.OK,
                    BackColor = Color.FromArgb(30, 30, 30),
                    ForeColor = Color.FromArgb(240, 240, 240),
                    FlatStyle = FlatStyle.Flat,
                    FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) }
                };

                var cancelBtn = new Button
                {
                    Text = "Отмена",
                    Left = 310,
                    Top = 80,
                    Width = 80,
                    DialogResult = DialogResult.Cancel,
                    BackColor = Color.FromArgb(30, 30, 30),
                    ForeColor = Color.FromArgb(240, 240, 240),
                    FlatStyle = FlatStyle.Flat,
                    FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) }
                };

                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(okBtn);
                form.Controls.Add(cancelBtn);
                form.AcceptButton = okBtn;
                form.CancelButton = cancelBtn;

                return form.ShowDialog() == DialogResult.OK ? textBox.Text : "";
            }
        }
    }
}