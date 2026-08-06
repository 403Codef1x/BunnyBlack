using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BunnyBlack.Forms
{
    public partial class RegistryEditorForm : UserControl
    {
        private SplitContainer splitContainer;
        private TreeView treeView;
        private DataGridView grid;
        private ContextMenuStrip treeContextMenu;
        private ContextMenuStrip gridContextMenu;

        private RegistryKey currentKey;
        private string currentPath = "";
        private string currentRoot = "HKEY_CURRENT_USER";

        public RegistryEditorForm(bool winRE)
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(13, 13, 13);
            InitializeComponent();
            LoadRegistryTree();
        }

        private void InitializeComponent()
        {
            // НЕТ ЗАГОЛОВКА, НЕТ ТУЛБАРА. ТОЛЬКО РАЗДЕЛИТЕЛЬ.
            splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                SplitterWidth = 4,
                SplitterDistance = 300,
                IsSplitterFixed = false
            };
            splitContainer.Panel1.BackColor = Color.FromArgb(13, 13, 13);
            splitContainer.Panel2.BackColor = Color.FromArgb(13, 13, 13);

            // ============================================================
            // TREE VIEW (Слева - чистое дерево)
            // ============================================================
            treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10),
                ShowLines = false, // Чистое дерево без соединительных линий
                ShowPlusMinus = true,
                ShowRootLines = false, // Без корневых линий
                FullRowSelect = true,
                ItemHeight = 28,
                HideSelection = false
            };
            treeView.BeforeExpand += TreeView_BeforeExpand;
            treeView.AfterSelect += TreeView_AfterSelect;
            treeView.MouseDown += TreeView_MouseDown;
            splitContainer.Panel1.Controls.Add(treeView);

            // ============================================================
            // DATA GRID VIEW (Справа - как на скриншоте)
            // ============================================================
            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BackgroundColor = Color.FromArgb(13, 13, 13),
                GridColor = Color.FromArgb(50, 50, 50), // Четкая темная сетка
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 30,
                RowTemplate = { Height = 26 }
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(13, 13, 13); // Фон заголовка как у фона
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(240, 240, 240);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grid.DefaultCellStyle.BackColor = Color.FromArgb(13, 13, 13);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(216, 216, 216);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(35, 55, 75);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(255, 255, 255);
            grid.Columns.Clear();
            grid.Columns.Add("Name", "Имя");
            grid.Columns.Add("Type", "Тип");
            grid.Columns.Add("Value", "Значение");
            grid.Columns[0].Width = 200;
            grid.Columns[1].Width = 100;
            grid.Columns[2].Width = 400;
            grid.DoubleClick += Grid_DoubleClick;
            grid.KeyDown += Grid_KeyDown;
            splitContainer.Panel2.Controls.Add(grid);

            this.Controls.Add(splitContainer);

            // ============================================================
            // КОНТЕКСТНЫЕ МЕНЮ
            // ============================================================
            InitializeContextMenus();
        }

        private void InitializeContextMenus()
        {
            // Меню для дерева
            treeContextMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };
            ToolStripMenuItem addKey = new ToolStripMenuItem("Создать раздел");
            addKey.Click += AddKey_Click;
            treeContextMenu.Items.Add(addKey);

            ToolStripMenuItem deleteKey = new ToolStripMenuItem("Удалить раздел");
            deleteKey.ForeColor = Color.FromArgb(255, 150, 150);
            deleteKey.Click += DeleteKey_Click;
            treeContextMenu.Items.Add(deleteKey);

            treeView.ContextMenuStrip = treeContextMenu;

            // Меню для таблицы
            gridContextMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };
            ToolStripMenuItem addValue = new ToolStripMenuItem("Создать значение");
            addValue.Click += AddValue_Click;
            gridContextMenu.Items.Add(addValue);

            ToolStripMenuItem editValue = new ToolStripMenuItem("Изменить значение");
            editValue.Click += EditValue_Click;
            gridContextMenu.Items.Add(editValue);

            ToolStripMenuItem deleteValue = new ToolStripMenuItem("Удалить значение");
            deleteValue.ForeColor = Color.FromArgb(255, 150, 150);
            deleteValue.Click += DeleteValue_Click;
            gridContextMenu.Items.Add(deleteValue);

            grid.ContextMenuStrip = gridContextMenu;
        }

        // ============================================================
        // ЗАГРУЗКА ДЕРЕВА
        // ============================================================
        private void LoadRegistryTree()
        {
            treeView.Nodes.Clear();

            TreeNode hkcr = new TreeNode("HKEY_CLASSES_ROOT") { Tag = "HKCR" };
            TreeNode hkcu = new TreeNode("HKEY_CURRENT_USER") { Tag = "HKCU" };
            TreeNode hklm = new TreeNode("HKEY_LOCAL_MACHINE") { Tag = "HKLM" };
            TreeNode hku = new TreeNode("HKEY_USERS") { Tag = "HKU" };
            TreeNode hkcc = new TreeNode("HKEY_CURRENT_CONFIG") { Tag = "HKCC" };

            // Добавляем заглушки для раскрытия
            hkcr.Nodes.Add("Загрузка...");
            hkcu.Nodes.Add("Загрузка...");
            hklm.Nodes.Add("Загрузка...");
            hku.Nodes.Add("Загрузка...");
            hkcc.Nodes.Add("Загрузка...");

            treeView.Nodes.AddRange(new TreeNode[] { hkcr, hkcu, hklm, hku, hkcc });

            // По умолчанию выбираем HKEY_CURRENT_USER
            treeView.SelectedNode = hkcu;
        }

        private void TreeView_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            TreeNode node = e.Node;
            if (node.Nodes.Count == 1 && node.Nodes[0].Text == "Загрузка...")
            {
                node.Nodes.Clear();
                LoadChildNodes(node);
            }
        }

        private void LoadChildNodes(TreeNode parentNode)
        {
            string root = parentNode.Tag as string;
            if (root == null)
            {
                TreeNode current = parentNode;
                while (current.Level > 0) current = current.Parent;
                root = current.Tag as string;
            }

            RegistryKey baseKey = GetBaseKey(root);
            string path = GetFullPath(parentNode);

            try
            {
                using (RegistryKey key = baseKey.OpenSubKey(path))
                {
                    if (key != null)
                    {
                        foreach (string subName in key.GetSubKeyNames())
                        {
                            TreeNode child = new TreeNode(subName);
                            child.Tag = path + "\\" + subName;
                            child.Nodes.Add("Загрузка...");
                            parentNode.Nodes.Add(child);
                        }
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки доступа
            }
        }

        private RegistryKey GetBaseKey(string root)
        {
            switch (root)
            {
                case "HKCR": return Registry.ClassesRoot;
                case "HKCU": return Registry.CurrentUser;
                case "HKLM": return Registry.LocalMachine;
                case "HKU": return Registry.Users;
                case "HKCC": return Registry.CurrentConfig;
                default: return Registry.CurrentUser;
            }
        }

        private string GetFullPath(TreeNode node)
        {
            if (node.Level == 0) return "";
            string path = node.Text;
            TreeNode parent = node.Parent;
            while (parent != null && parent.Level > 0)
            {
                path = parent.Text + "\\" + path;
                parent = parent.Parent;
            }
            return path;
        }

        private RegistryKey GetKeyFromNode(TreeNode node)
        {
            if (node == null) return null;
            if (node.Level == 0)
            {
                currentRoot = node.Tag as string;
                return GetBaseKey(currentRoot);
            }

            string root = "";
            TreeNode current = node;
            while (current.Level > 0) current = current.Parent;
            root = current.Tag as string;

            RegistryKey baseKey = GetBaseKey(root);
            string path = GetFullPath(node);

            try
            {
                return baseKey.OpenSubKey(path, true);
            }
            catch
            {
                return null;
            }
        }

        private void TreeView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                TreeNode node = treeView.GetNodeAt(e.X, e.Y);
                if (node != null)
                {
                    treeView.SelectedNode = node;
                }
            }
        }

        // ============================================================
        // ВЫБОР ВЕТКИ
        // ============================================================
        private void TreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            TreeNode node = e.Node;
            if (node == null) return;

            currentKey = GetKeyFromNode(node);
            currentPath = GetFullPath(node);

            if (currentKey != null)
            {
                LoadValues(currentKey);
            }
            else
            {
                grid.Rows.Clear();
            }
        }

        // ============================================================
        // ЗАГРУЗКА ЗНАЧЕНИЙ
        // ============================================================
        private void LoadValues(RegistryKey key)
        {
            grid.Rows.Clear();
            if (key == null) return;

            // Значение по умолчанию
            object defValue = key.GetValue("");
            if (defValue != null)
            {
                RegistryValueKind defKind = key.GetValueKind("");
                grid.Rows.Add("(По умолчанию)", GetTypeName(defKind), FormatValue(defValue, defKind));
            }

            // Остальные значения
            foreach (string valueName in key.GetValueNames())
            {
                if (valueName == "") continue;
                object value = key.GetValue(valueName);
                RegistryValueKind kind = key.GetValueKind(valueName);
                grid.Rows.Add(valueName, GetTypeName(kind), FormatValue(value, kind));
            }
        }

        // ============================================================
        // ФОРМАТИРОВАНИЕ
        // ============================================================
        // ИСПРАВЛЕНИЕ ОШИБКИ CS0120: МЕТОД СДЕЛАН СТАТИЧЕСКИМ
        private static string GetTypeName(RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.String: return "REG_SZ";
                case RegistryValueKind.ExpandString: return "REG_EXPAND_SZ";
                case RegistryValueKind.Binary: return "REG_BINARY";
                case RegistryValueKind.DWord: return "REG_DWORD";
                case RegistryValueKind.MultiString: return "REG_MULTI_SZ";
                case RegistryValueKind.QWord: return "REG_QWORD";
                default: return "REG_NONE";
            }
        }

        private string FormatValue(object value, RegistryValueKind kind)
        {
            if (value == null) return "(null)";

            switch (kind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    return value.ToString();
                case RegistryValueKind.DWord:
                    int dword = Convert.ToInt32(value);
                    return $"{dword} (0x{dword:X8})";
                case RegistryValueKind.QWord:
                    long qword = Convert.ToInt64(value);
                    return $"{qword} (0x{qword:X16})";
                case RegistryValueKind.Binary:
                    byte[] bytes = (byte[])value;
                    if (bytes.Length == 0) return "(пусто)";
                    if (bytes.Length <= 16)
                        return BitConverter.ToString(bytes).Replace("-", " ");
                    return BitConverter.ToString(bytes, 0, 16).Replace("-", " ") + " ...";
                case RegistryValueKind.MultiString:
                    string[] strings = (string[])value;
                    return string.Join("; ", strings);
                default:
                    return value.ToString();
            }
        }

        // ============================================================
        // ДОБАВЛЕНИЕ / УДАЛЕНИЕ РАЗДЕЛОВ
        // ============================================================
        private void AddKey_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode == null) return;
            if (treeView.SelectedNode.Level == 0)
            {
                MessageBox.Show("Нельзя создать раздел в корне.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string newName = InputDialog("Введите имя нового раздела:", "Создать раздел", "");
            if (string.IsNullOrWhiteSpace(newName)) return;
            if (newName.Contains("\\"))
            {
                MessageBox.Show("Имя не может содержать обратный слэш (\\).", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                RegistryKey parentKey = GetKeyFromNode(treeView.SelectedNode);
                if (parentKey == null) throw new Exception("Не удалось открыть родительский раздел.");

                using (RegistryKey newKey = parentKey.CreateSubKey(newName))
                {
                    if (newKey != null)
                    {
                        TreeNode newNode = new TreeNode(newName);
                        newNode.Tag = GetFullPath(treeView.SelectedNode) + "\\" + newName;
                        newNode.Nodes.Add("Загрузка...");
                        treeView.SelectedNode.Nodes.Add(newNode);
                        treeView.SelectedNode = newNode;
                        LoadValues(newKey);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания раздела: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteKey_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode == null || treeView.SelectedNode.Level == 0)
            {
                MessageBox.Show("Нельзя удалить корневой раздел.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string keyName = treeView.SelectedNode.Text;
            if (MessageBox.Show($"Вы уверены, что хотите удалить раздел '{keyName}' и всё его содержимое?",
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;

            try
            {
                TreeNode parentNode = treeView.SelectedNode.Parent;
                RegistryKey parentKey = GetKeyFromNode(parentNode);
                if (parentKey != null)
                {
                    parentKey.DeleteSubKey(keyName);
                    treeView.SelectedNode.Remove();
                    treeView.SelectedNode = parentNode;
                    LoadValues(GetKeyFromNode(parentNode));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления раздела: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // ДОБАВЛЕНИЕ / РЕДАКТИРОВАНИЕ / УДАЛЕНИЕ ЗНАЧЕНИЙ
        // ============================================================
        private void AddValue_Click(object sender, EventArgs e)
        {
            if (currentKey == null) return;

            using (AddValueDialog dialog = new AddValueDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string name = dialog.ValueName;
                    string data = dialog.ValueData;
                    string type = dialog.ValueType;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        MessageBox.Show("Имя значения не может быть пустым.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (currentKey.GetValue(name) != null)
                    {
                        MessageBox.Show("Значение с таким именем уже существует.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    try
                    {
                        switch (type)
                        {
                            case "REG_SZ":
                                currentKey.SetValue(name, data);
                                break;
                            case "REG_DWORD":
                                currentKey.SetValue(name, Convert.ToInt32(data));
                                break;
                            case "REG_QWORD":
                                currentKey.SetValue(name, Convert.ToInt64(data));
                                break;
                            default:
                                currentKey.SetValue(name, data);
                                break;
                        }
                        LoadValues(currentKey);
                    }
                    catch
                    {
                        MessageBox.Show("Ошибка формата данных (ожидается число).", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void EditValue_Click(object sender, EventArgs e)
        {
            if (grid.SelectedRows.Count == 0) return;
            if (currentKey == null) return;

            string name = grid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            if (name == "(По умолчанию)") name = "";

            object value = currentKey.GetValue(name);
            if (value == null) return;

            RegistryValueKind kind = currentKey.GetValueKind(name);
            string currentData = value.ToString();

            using (EditValueDialog dialog = new EditValueDialog(name, currentData, kind))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string newData = dialog.ValueData;
                        switch (kind)
                        {
                            case RegistryValueKind.String:
                            case RegistryValueKind.ExpandString:
                                currentKey.SetValue(name, newData);
                                break;
                            case RegistryValueKind.DWord:
                                currentKey.SetValue(name, Convert.ToInt32(newData));
                                break;
                            case RegistryValueKind.QWord:
                                currentKey.SetValue(name, Convert.ToInt64(newData));
                                break;
                            case RegistryValueKind.MultiString:
                                currentKey.SetValue(name, newData.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                                break;
                            default:
                                currentKey.SetValue(name, newData);
                                break;
                        }
                        LoadValues(currentKey);
                    }
                    catch
                    {
                        MessageBox.Show("Ошибка формата данных (ожидается число).", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void DeleteValue_Click(object sender, EventArgs e)
        {
            if (grid.SelectedRows.Count == 0) return;
            if (currentKey == null) return;

            string name = grid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            if (name == "(По умолчанию)")
            {
                MessageBox.Show("Нельзя удалить значение по умолчанию.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show($"Удалить значение '{name}'?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;

            try
            {
                currentKey.DeleteValue(name);
                LoadValues(currentKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления значения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Grid_DoubleClick(object sender, EventArgs e)
        {
            EditValue_Click(sender, e);
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteValue_Click(sender, e);
            }
            else if (e.KeyCode == Keys.Enter)
            {
                EditValue_Click(sender, e);
            }
        }

        // ============================================================
        // ДИАЛОГОВОЕ ОКНО ВВОДА ТЕКСТА
        // ============================================================
        private string InputDialog(string prompt, string title, string defaultValue)
        {
            Form form = new Form()
            {
                Width = 450,
                Height = 160,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                MaximizeBox = false,
                MinimizeBox = false
            };

            Label label = new Label() { Left = 20, Top = 20, Text = prompt, AutoSize = true, ForeColor = Color.FromArgb(216, 216, 216) };
            TextBox input = new TextBox() { Left = 20, Top = 50, Width = 390, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(216, 216, 216), BorderStyle = BorderStyle.FixedSingle, Text = defaultValue };
            Button ok = new Button() { Text = "OK", Left = 280, Width = 100, Top = 85, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 55, 75), ForeColor = Color.FromArgb(240, 240, 240), DialogResult = DialogResult.OK };
            Button cancel = new Button() { Text = "Отмена", Left = 170, Width = 100, Top = 85, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(170, 170, 170), DialogResult = DialogResult.Cancel };

            form.Controls.Add(label);
            form.Controls.Add(input);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;

            return form.ShowDialog() == DialogResult.OK ? input.Text : null;
        }

        // ============================================================
        // ВСТРОЕННЫЕ ДИАЛОГИ (ОКНА)
        // ============================================================
        public class AddValueDialog : Form
        {
            public string ValueName => txtName.Text;
            public string ValueData => txtData.Text;
            public string ValueType => comboType.SelectedItem?.ToString() ?? "REG_SZ";

            private TextBox txtName;
            private TextBox txtData;
            private ComboBox comboType;
            private Button btnOk, btnCancel;

            public AddValueDialog()
            {
                this.Text = "Добавить значение";
                this.Size = new Size(450, 240);
                this.StartPosition = FormStartPosition.CenterParent;
                this.BackColor = Color.FromArgb(13, 13, 13);
                this.ForeColor = Color.FromArgb(216, 216, 216);
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox = this.MinimizeBox = false;

                Label lblName = new Label { Text = "Имя:", Location = new Point(12, 15), AutoSize = true, ForeColor = Color.FromArgb(216, 216, 216) };
                txtName = new TextBox { Location = new Point(12, 35), Width = 400, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(216, 216, 216), BorderStyle = BorderStyle.FixedSingle };

                Label lblType = new Label { Text = "Тип:", Location = new Point(12, 65), AutoSize = true, ForeColor = Color.FromArgb(216, 216, 216) };
                comboType = new ComboBox { Location = new Point(12, 85), Width = 400, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(216, 216, 216), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
                comboType.Items.AddRange(new object[] { "REG_SZ", "REG_DWORD", "REG_QWORD" });
                comboType.SelectedIndex = 0;

                Label lblData = new Label { Text = "Значение:", Location = new Point(12, 115), AutoSize = true, ForeColor = Color.FromArgb(216, 216, 216) };
                txtData = new TextBox { Location = new Point(12, 135), Width = 400, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(216, 216, 216), BorderStyle = BorderStyle.FixedSingle };

                btnOk = new Button { Text = "OK", Location = new Point(280, 185), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 55, 75), ForeColor = Color.FromArgb(240, 240, 240), DialogResult = DialogResult.OK };
                btnCancel = new Button { Text = "Отмена", Location = new Point(370, 185), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(170, 170, 170), DialogResult = DialogResult.Cancel };

                this.Controls.AddRange(new Control[] { lblName, txtName, lblType, comboType, lblData, txtData, btnOk, btnCancel });
                this.AcceptButton = btnOk;
                this.CancelButton = btnCancel;
            }
        }

        public class EditValueDialog : Form
        {
            public string ValueData => txtData.Text;
            private TextBox txtData;
            private Button btnOk, btnCancel;

            public EditValueDialog(string name, string data, RegistryValueKind kind)
            {
                this.Text = $"Изменить: {name}";
                this.Size = new Size(500, 180);
                this.StartPosition = FormStartPosition.CenterParent;
                this.BackColor = Color.FromArgb(13, 13, 13);
                this.ForeColor = Color.FromArgb(216, 216, 216);
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox = this.MinimizeBox = false;

                Label lblInfo = new Label
                {
                    Text = $"Тип: {GetTypeName(kind)}. Введите новое значение:",
                    Location = new Point(12, 15),
                    AutoSize = true,
                    ForeColor = Color.FromArgb(170, 170, 170)
                };

                txtData = new TextBox
                {
                    Location = new Point(12, 40),
                    Width = 450,
                    Multiline = true,
                    Height = 60,
                    BackColor = Color.FromArgb(22, 22, 22),
                    ForeColor = Color.FromArgb(216, 216, 216),
                    BorderStyle = BorderStyle.FixedSingle,
                    Text = data
                };

                btnOk = new Button { Text = "OK", Location = new Point(320, 110), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 55, 75), ForeColor = Color.FromArgb(240, 240, 240), DialogResult = DialogResult.OK };
                btnCancel = new Button { Text = "Отмена", Location = new Point(410, 110), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(170, 170, 170), DialogResult = DialogResult.Cancel };

                this.Controls.AddRange(new Control[] { lblInfo, txtData, btnOk, btnCancel });
                this.AcceptButton = btnOk;
                this.CancelButton = btnCancel;
            }
        }
    }
}