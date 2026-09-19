// language: C#, file: Forms/RegistryEditorForm.cs
// Полная замена.
// - Режим "Живой реестр" — как было.
// - Режим "Оффлайн-кусты" — в WinRE, автоматически подгружает SOFTWARE/SYSTEM/SAM/SECURITY
//   и NTUSER.DAT пользователя. Работает как со стандартными, так и с принесёнными файлами.
// - Импорт/экспорт .reg, монтирование любого .hiv/.dat.
// - Сохранены: AddKey, DeleteKey, AddValue, EditValue, DeleteValue, InputDialog,
//   AddValueDialog, EditValueDialog, context-menu.
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
    public partial class RegistryEditorForm : UserControl
    {
        private SplitContainer splitContainer;
        private TreeView treeView;
        private DataGridView grid;
        private Panel topBar;
        private ComboBox rootCombo;
        private Button btnMountFile;
        private Button btnUnmountAll;
        private Button btnImport;
        private Button btnExport;
        private Label statusLabel;

        private ContextMenuStrip treeContextMenu;
        private ContextMenuStrip gridContextMenu;

        private RegistryKey currentKey;
        private string currentPath = "";
        private string currentRoot = "HKEY_CURRENT_USER";
        private bool isWinRE;
        private bool useOfflineMode = false;

        // Виртуальные "корни" для дерева — живые и оффлайн
        private class RootNode
        {
            public string Key;       // HKLM, HKCU, OfflineSOFTWARE, ...
            public string Display;
            public bool IsOffline;
            public string MountName; // для оффлайн
            public bool IsUserHive;
        }

        private List<RootNode> activeRoots = new List<RootNode>();

        public RegistryEditorForm(bool winRE)
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(13, 13, 13);
            isWinRE = winRE;

            InitializeComponent();

            if (isWinRE)
            {
                // пробуем загрузить кусты
                EnableOfflineMode();
            }
            LoadRegistryTree();
        }

        private void InitializeComponent()
        {
            // Верхняя панель
            topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(18, 18, 18),
                Padding = new Padding(8, 5, 8, 5)
            };

            rootCombo = new ComboBox
            {
                Width = 300,
                Height = 26,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.FromArgb(216, 216, 216),
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9),
                Location = new Point(8, 5)
            };
            rootCombo.SelectedIndexChanged += (s, e) => SwitchRoot(rootCombo.SelectedIndex);
            topBar.Controls.Add(rootCombo);

            btnMountFile = new Button
            {
                Text = "Смонтировать файл...",
                Width = 160,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 9),
                Location = new Point(320, 5),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand
            };
            btnMountFile.Click += BtnMountFile_Click;
            topBar.Controls.Add(btnMountFile);

            btnUnmountAll = new Button
            {
                Text = "Размонтировать всё",
                Width = 150,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 9),
                Location = new Point(490, 5),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand
            };
            btnUnmountAll.Click += (s, e) =>
            {
                OfflineHiveManager.UnmountAll();
                LoadRegistryTree();
            };
            topBar.Controls.Add(btnUnmountAll);

            btnImport = new Button
            {
                Text = "Импорт .reg",
                Width = 110,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 9),
                Location = new Point(650, 5),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand
            };
            btnImport.Click += BtnImport_Click;
            topBar.Controls.Add(btnImport);

            btnExport = new Button
            {
                Text = "Экспорт .reg",
                Width = 110,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 9),
                Location = new Point(770, 5),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand
            };
            btnExport.Click += BtnExport_Click;
            topBar.Controls.Add(btnExport);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(900, 9)
            };
            topBar.Controls.Add(statusLabel);

            // Дерево
            treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10),
                ShowLines = false,
                ShowPlusMinus = true,
                ShowRootLines = false,
                FullRowSelect = true,
                ItemHeight = 28,
                HideSelection = false
            };
            treeView.BeforeExpand += TreeView_BeforeExpand;
            treeView.AfterSelect += TreeView_AfterSelect;
            treeView.MouseDown += TreeView_MouseDown;

            splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                SplitterWidth = 4,
                SplitterDistance = 320,
                IsSplitterFixed = false
            };
            splitContainer.Panel1.BackColor = Color.FromArgb(13, 13, 13);
            splitContainer.Panel2.BackColor = Color.FromArgb(13, 13, 13);
            splitContainer.Panel1.Controls.Add(treeView);

            // Грид
            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BackgroundColor = Color.FromArgb(13, 13, 13),
                GridColor = Color.FromArgb(50, 50, 50),
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
            grid.RowTemplate.Height = 26;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(13, 13, 13);
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

            Controls.Add(splitContainer);
            Controls.Add(topBar);

            InitializeContextMenus();
        }

        private void EnableOfflineMode()
        {
            useOfflineMode = true;

            // грузим системные кусты
            string _;
            RegistryHelper.LoadOfflineHives(out _);

            statusLabel.Text = "Оффлайн-режим";
            statusLabel.ForeColor = Color.FromArgb(255, 200, 120);
        }

        // ============================================================
        // СПИСОК КОРНЕЙ — живой + оффлайн
        // ============================================================
        private void BuildRoots()
        {
            activeRoots.Clear();

            if (isWinRE && useOfflineMode)
            {
                // SOFTWARE
                activeRoots.Add(new RootNode
                {
                    Key = "OfflineSOFTWARE",
                    Display = "SOFTWARE (оффлайн)",
                    IsOffline = true,
                    MountName = "BunnyBlack_Offline_SOFTWARE",
                    IsUserHive = false
                });
                // SYSTEM
                activeRoots.Add(new RootNode
                {
                    Key = "OfflineSYSTEM",
                    Display = "SYSTEM (оффлайн)",
                    IsOffline = true,
                    MountName = "BunnyBlack_Offline_SYSTEM",
                    IsUserHive = false
                });
                // SAM
                activeRoots.Add(new RootNode
                {
                    Key = "OfflineSAM",
                    Display = "SAM (оффлайн)",
                    IsOffline = true,
                    MountName = "BunnyBlack_Offline_SAM",
                    IsUserHive = false
                });
                // SECURITY
                activeRoots.Add(new RootNode
                {
                    Key = "OfflineSECURITY",
                    Display = "SECURITY (оффлайн)",
                    IsOffline = true,
                    MountName = "BunnyBlack_Offline_SECURITY",
                    IsUserHive = false
                });
                // HKCU
                activeRoots.Add(new RootNode
                {
                    Key = "OfflineHKCU",
                    Display = "Пользователь (оффлайн NTUSER)",
                    IsOffline = true,
                    MountName = "BunnyBlack_Offline_HKCU",
                    IsUserHive = true
                });

                // плюс живые, чтобы не терять доступ к текущей WinRE
                activeRoots.Add(new RootNode { Key = "HKCU", Display = "HKEY_CURRENT_USER (WinRE)", IsOffline = false });
                activeRoots.Add(new RootNode { Key = "HKLM", Display = "HKEY_LOCAL_MACHINE (WinRE)", IsOffline = false });
            }
            else
            {
                activeRoots.Add(new RootNode { Key = "HKCR", Display = "HKEY_CLASSES_ROOT", IsOffline = false });
                activeRoots.Add(new RootNode { Key = "HKCU", Display = "HKEY_CURRENT_USER", IsOffline = false });
                activeRoots.Add(new RootNode { Key = "HKLM", Display = "HKEY_LOCAL_MACHINE", IsOffline = false });
                activeRoots.Add(new RootNode { Key = "HKU", Display = "HKEY_USERS", IsOffline = false });
                activeRoots.Add(new RootNode { Key = "HKCC", Display = "HKEY_CURRENT_CONFIG", IsOffline = false });
            }

            rootCombo.Items.Clear();
            foreach (var r in activeRoots)
                rootCombo.Items.Add(r.Display);
            if (rootCombo.Items.Count > 0)
                rootCombo.SelectedIndex = 0;
        }

        private void SwitchRoot(int index)
        {
            if (index < 0 || index >= activeRoots.Count) return;
            currentRoot = activeRoots[index].Key;
            LoadRegistryTree();
        }

        // ============================================================
        // ДЕРЕВО
        // ============================================================
        private void LoadRegistryTree()
        {
            BuildRoots();
            treeView.Nodes.Clear();

            foreach (var r in activeRoots)
            {
                var node = new TreeNode(r.Display) { Tag = r.Key };
                node.Nodes.Add("Загрузка...");
                treeView.Nodes.Add(node);
            }

            if (treeView.Nodes.Count > 0)
                treeView.SelectedNode = treeView.Nodes[0];
        }

        private void TreeView_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            var node = e.Node;
            if (node.Nodes.Count == 1 && node.Nodes[0].Text == "Загрузка...")
            {
                node.Nodes.Clear();
                LoadChildNodes(node);
            }
        }

        private void LoadChildNodes(TreeNode parentNode)
        {
            string rootKey = GetRootKeyFromNode(parentNode);
            RegistryKey baseKey = GetBaseKey(rootKey);
            string path = GetFullPath(parentNode);

            try
            {
                using (RegistryKey key = baseKey?.OpenSubKey(path))
                {
                    if (key != null)
                    {
                        foreach (string subName in key.GetSubKeyNames())
                        {
                            var child = new TreeNode(subName);
                            string childPath = string.IsNullOrEmpty(path) ? subName : path + "\\" + subName;
                            child.Tag = childPath;
                            child.Nodes.Add("Загрузка...");
                            parentNode.Nodes.Add(child);
                        }
                    }
                }
            }
            catch { }
        }

        private string GetRootKeyFromNode(TreeNode node)
        {
            var cur = node;
            while (cur.Level > 0) cur = cur.Parent;
            return cur.Tag as string;
        }

        private RegistryKey GetBaseKey(string rootKey)
        {
            // оффлайн-корни
            if (rootKey != null && rootKey.StartsWith("Offline"))
            {
                string mountName = null;
                bool isUser = false;
                foreach (var r in activeRoots)
                    if (r.Key == rootKey) { mountName = r.MountName; isUser = r.IsUserHive; break; }

                if (mountName == null) return null;
                try
                {
                    if (isUser)
                        return Registry.Users.OpenSubKey(mountName);
                    return Registry.LocalMachine.OpenSubKey(mountName);
                }
                catch { return null; }
            }

            switch (rootKey)
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
            if (node == null) return "";
            if (node.Level == 0) return "";
            if (node.Tag is string tag && tag.Contains("\\")) return tag;

            string path = node.Text;
            var parent = node.Parent;
            while (parent != null && parent.Level > 0)
            {
                path = parent.Text + "\\" + path;
                parent = parent.Parent;
            }
            node.Tag = path;
            return path;
        }

        private RegistryKey GetKeyFromNode(TreeNode node)
        {
            if (node == null) return null;
            string rootKey = GetRootKeyFromNode(node);
            string path = GetFullPath(node);

            var baseKey = GetBaseKey(rootKey);
            if (baseKey == null) return null;

            try
            {
                if (string.IsNullOrEmpty(path)) return baseKey;
                return baseKey.OpenSubKey(path, true);
            }
            catch { return null; }
        }

        private void TreeView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var node = treeView.GetNodeAt(e.X, e.Y);
                if (node != null) treeView.SelectedNode = node;
            }
        }

        private void TreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node == null) return;
            currentKey = GetKeyFromNode(e.Node);
            currentPath = GetFullPath(e.Node);
            if (currentKey != null) LoadValues(currentKey);
            else grid.Rows.Clear();
        }

        private void LoadValues(RegistryKey key)
        {
            grid.Rows.Clear();
            if (key == null) return;

            object defValue = key.GetValue("");
            if (defValue != null)
            {
                var kind = key.GetValueKind("");
                grid.Rows.Add("(По умолчанию)", GetTypeName(kind), FormatValue(defValue, kind));
            }

            foreach (var valueName in key.GetValueNames())
            {
                if (valueName == "") continue;
                object value = key.GetValue(valueName);
                var kind = key.GetValueKind(valueName);
                grid.Rows.Add(valueName, GetTypeName(kind), FormatValue(value, kind));
            }
        }

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
                    int dw = Convert.ToInt32(value);
                    return $"{dw} (0x{dw:X8})";
                case RegistryValueKind.QWord:
                    long qw = Convert.ToInt64(value);
                    return $"{qw} (0x{qw:X16})";
                case RegistryValueKind.Binary:
                    var bytes = (byte[])value;
                    if (bytes.Length == 0) return "(пусто)";
                    if (bytes.Length <= 16) return BitConverter.ToString(bytes).Replace("-", " ");
                    return BitConverter.ToString(bytes, 0, 16).Replace("-", " ") + " ...";
                case RegistryValueKind.MultiString:
                    return string.Join("; ", (string[])value);
                default:
                    return value.ToString();
            }
        }

        private void InitializeContextMenus()
        {
            treeContextMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };
            var addKey = new ToolStripMenuItem("Создать раздел");
            addKey.Click += AddKey_Click;
            treeContextMenu.Items.Add(addKey);
            var deleteKey = new ToolStripMenuItem("Удалить раздел");
            deleteKey.ForeColor = Color.FromArgb(255, 150, 150);
            deleteKey.Click += DeleteKey_Click;
            treeContextMenu.Items.Add(deleteKey);
            treeView.ContextMenuStrip = treeContextMenu;

            gridContextMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(17, 17, 17),
                ForeColor = Color.FromArgb(216, 216, 216)
            };
            var addValue = new ToolStripMenuItem("Создать значение");
            addValue.Click += AddValue_Click;
            gridContextMenu.Items.Add(addValue);
            var editValue = new ToolStripMenuItem("Изменить значение");
            editValue.Click += EditValue_Click;
            gridContextMenu.Items.Add(editValue);
            var deleteValue = new ToolStripMenuItem("Удалить значение");
            deleteValue.ForeColor = Color.FromArgb(255, 150, 150);
            deleteValue.Click += DeleteValue_Click;
            gridContextMenu.Items.Add(deleteValue);
            grid.ContextMenuStrip = gridContextMenu;
        }

        // ============================================================
        // ДЕЙСТВИЯ
        // ============================================================
        private void BtnMountFile_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "Кусты реестра (*.hiv;*.dat)|*.hiv;*.dat|Все файлы|*.*",
                Title = "Выбери файл-куст"
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                MountedHive h;
                string err;
                if (OfflineHiveManager.Mount(ofd.FileName, out h, out err))
                {
                    // добавим как новый корень
                    activeRoots.Add(new RootNode
                    {
                        Key = "OfflineMount_" + h.MountName,
                        Display = $"{h.Label} [{h.MountName}]",
                        IsOffline = true,
                        MountName = h.MountName,
                        IsUserHive = h.SourceFile.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                    });
                    LoadRegistryTree();
                    NedoMessageBox.Show($"Смонтировано как {h.MountName}");
                }
                else
                {
                    NedoMessageBox.Show("Ошибка монтирования: " + err, isError: true);
                }
            }
        }

        private void BtnImport_Click(object sender, EventArgs e)
        {
            if (!(currentKey is RegistryKey) || treeView.SelectedNode == null)
            {
                NedoMessageBox.Show("Выбери раздел в дереве.", isError: true);
                return;
            }

            string rootKey = GetRootKeyFromNode(treeView.SelectedNode);
            if (!rootKey.StartsWith("Offline"))
            {
                NedoMessageBox.Show("Импорт .reg доступен только в оффлайн-режиме.", isError: true);
                return;
            }

            MountedHive hive = null;
            foreach (var h in OfflineHiveManager.Mounted)
            {
                if (rootKey.Contains(h.MountName) || rootKey == "Offline" + h.MountName.Replace("BunnyBlack_Offline_", ""))
                    hive = h;
            }
            if (hive == null)
            {
                // fallback — берём системный куст по префиксу
                string mountName = null;
                foreach (var r in activeRoots)
                    if (r.Key == rootKey) mountName = r.MountName;
                if (mountName != null)
                {
                    foreach (var h in OfflineHiveManager.Mounted)
                        if (h.MountName == mountName) hive = h;
                }
            }

            if (hive == null)
            {
                NedoMessageBox.Show("Не найден смонтированный куст.", isError: true);
                return;
            }

            using (var ofd = new OpenFileDialog { Filter = "REG-файлы (*.reg)|*.reg" })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                string err;
                if (OfflineHiveManager.ImportFromReg(hive, ofd.FileName, out err))
                {
                    NedoMessageBox.Show("Импорт завершён.");
                    TreeView_AfterSelect(this, new TreeViewEventArgs(treeView.SelectedNode));
                }
                else NedoMessageBox.Show("Ошибка импорта: " + err, isError: true);
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode == null) return;
            string rootKey = GetRootKeyFromNode(treeView.SelectedNode);
            if (!rootKey.StartsWith("Offline"))
            {
                NedoMessageBox.Show("Экспорт .reg доступен только в оффлайн-режиме.", isError: true);
                return;
            }

            MountedHive hive = null;
            string mountName = null;
            foreach (var r in activeRoots)
                if (r.Key == rootKey) mountName = r.MountName;
            foreach (var h in OfflineHiveManager.Mounted)
                if (h.MountName == mountName) hive = h;

            if (hive == null)
            {
                NedoMessageBox.Show("Куст не смонтирован.", isError: true);
                return;
            }

            using (var sfd = new SaveFileDialog
            {
                Filter = "REG-файлы (*.reg)|*.reg",
                FileName = "export.reg"
            })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;
                string err;
                if (OfflineHiveManager.ExportToReg(hive, currentPath, sfd.FileName, out err))
                    NedoMessageBox.Show("Экспортировано в " + sfd.FileName);
                else
                    NedoMessageBox.Show("Ошибка экспорта: " + err, isError: true);
            }
        }

        private void AddKey_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode == null) return;
            if (treeView.SelectedNode.Level == 0)
            {
                NedoMessageBox.Show("Нельзя создать раздел в корне.", isError: true);
                return;
            }

            string newName = InputDialog("Введите имя нового раздела:", "Создать раздел", "");
            if (string.IsNullOrWhiteSpace(newName)) return;
            if (newName.Contains("\\"))
            {
                NedoMessageBox.Show("Имя не может содержать обратный слэш (\\).", isError: true);
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
                        var newNode = new TreeNode(newName);
                        string parentPath = GetFullPath(treeView.SelectedNode);
                        newNode.Tag = string.IsNullOrEmpty(parentPath) ? newName : parentPath + "\\" + newName;
                        newNode.Nodes.Add("Загрузка...");
                        treeView.SelectedNode.Nodes.Add(newNode);
                        treeView.SelectedNode = newNode;
                        LoadValues(newKey);
                    }
                }
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show($"Ошибка создания раздела: {ex.Message}", isError: true);
            }
        }

        private void DeleteKey_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode == null || treeView.SelectedNode.Level == 0)
            {
                NedoMessageBox.Show("Нельзя удалить корневой раздел.", isError: true);
                return;
            }

            string keyName = treeView.SelectedNode.Text;
            if (MessageBoxHelper.Show($"Удалить раздел '{keyName}' и всё его содержимое?",
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;

            try
            {
                var parentNode = treeView.SelectedNode.Parent;
                var parentKey = GetKeyFromNode(parentNode);
                if (parentKey != null)
                {
                    parentKey.DeleteSubKeyTree(keyName, false);
                    treeView.SelectedNode.Remove();
                    treeView.SelectedNode = parentNode;
                    LoadValues(GetKeyFromNode(parentNode));
                }
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show($"Ошибка удаления: {ex.Message}", isError: true);
            }
        }

        private void AddValue_Click(object sender, EventArgs e)
        {
            if (currentKey == null) return;
            using (var dialog = new AddValueDialog())
            {
                Form owner = FindOwner();
                var dr = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
                if (dr == DialogResult.OK)
                {
                    try
                    {
                        switch (dialog.ValueType)
                        {
                            case "REG_DWORD":
                                currentKey.SetValue(dialog.ValueName, Convert.ToInt32(dialog.ValueData), RegistryValueKind.DWord);
                                break;
                            case "REG_QWORD":
                                currentKey.SetValue(dialog.ValueName, Convert.ToInt64(dialog.ValueData), RegistryValueKind.QWord);
                                break;
                            default:
                                currentKey.SetValue(dialog.ValueName, dialog.ValueData, RegistryValueKind.String);
                                break;
                        }
                        LoadValues(currentKey);
                    }
                    catch
                    {
                        NedoMessageBox.Show("Ошибка формата данных.", isError: true);
                    }
                }
            }
        }

        private void EditValue_Click(object sender, EventArgs e)
        {
            if (grid.SelectedRows.Count == 0 || currentKey == null) return;
            string name = grid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            if (name == "(По умолчанию)") name = "";

            object value = currentKey.GetValue(name);
            if (value == null) return;
            var kind = currentKey.GetValueKind(name);

            using (var dialog = new EditValueDialog(name, value.ToString(), kind))
            {
                Form owner = FindOwner();
                var dr = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
                if (dr == DialogResult.OK)
                {
                    try
                    {
                        switch (kind)
                        {
                            case RegistryValueKind.DWord:
                                currentKey.SetValue(name, Convert.ToInt32(dialog.ValueData), RegistryValueKind.DWord);
                                break;
                            case RegistryValueKind.QWord:
                                currentKey.SetValue(name, Convert.ToInt64(dialog.ValueData), RegistryValueKind.QWord);
                                break;
                            case RegistryValueKind.MultiString:
                                currentKey.SetValue(name, dialog.ValueData.Split(';'), RegistryValueKind.MultiString);
                                break;
                            default:
                                currentKey.SetValue(name, dialog.ValueData, RegistryValueKind.String);
                                break;
                        }
                        LoadValues(currentKey);
                    }
                    catch
                    {
                        NedoMessageBox.Show("Ошибка формата.", isError: true);
                    }
                }
            }
        }

        private void DeleteValue_Click(object sender, EventArgs e)
        {
            if (grid.SelectedRows.Count == 0 || currentKey == null) return;
            string name = grid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            if (name == "(По умолчанию)") return;

            if (MessageBoxHelper.Show($"Удалить значение '{name}'?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;
            try { currentKey.DeleteValue(name); LoadValues(currentKey); }
            catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
        }

        private void Grid_DoubleClick(object sender, EventArgs e) => EditValue_Click(sender, e);

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) DeleteValue_Click(sender, e);
            else if (e.KeyCode == Keys.Enter) EditValue_Click(sender, e);
        }

        private string InputDialog(string prompt, string title, string defaultValue)
        {
            using (var form = new Form
            {
                Width = 450,
                Height = 160,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                ShowInTaskbar = false
            })
            {
                var label = new Label { Left = 20, Top = 20, Text = prompt, AutoSize = true, ForeColor = Color.FromArgb(216, 216, 216) };
                var input = new TextBox { Left = 20, Top = 50, Width = 390, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(216, 216, 216), BorderStyle = BorderStyle.FixedSingle, Text = defaultValue };
                var ok = new Button { Text = "OK", Left = 280, Width = 100, Top = 85, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 55, 75), ForeColor = Color.FromArgb(240, 240, 240), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Отмена", Left = 170, Width = 100, Top = 85, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(170, 170, 170), DialogResult = DialogResult.Cancel };

                form.Controls.Add(label); form.Controls.Add(input);
                form.Controls.Add(ok); form.Controls.Add(cancel);
                form.AcceptButton = ok; form.CancelButton = cancel;

                Form owner = FindOwner();
                var dr = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
                return dr == DialogResult.OK ? input.Text : null;
            }
        }

        private Form FindOwner()
        {
            foreach (Form f in Application.OpenForms)
                if (f is MainForm) return f;
            return Form.ActiveForm;
        }

        public class AddValueDialog : Form
        {
            public string ValueName => txtName.Text;
            public string ValueData => txtData.Text;
            public string ValueType => comboType.SelectedItem?.ToString() ?? "REG_SZ";
            private TextBox txtName, txtData;
            private ComboBox comboType;

            public AddValueDialog()
            {
                Text = "Добавить значение";
                Size = new Size(450, 240);
                StartPosition = FormStartPosition.CenterParent;
                BackColor = Color.FromArgb(13, 13, 13);
                ForeColor = Color.FromArgb(216, 216, 216);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                TopMost = true; ShowInTaskbar = false;

                Controls.Add(new Label { Text = "Имя:", Location = new Point(12, 15), AutoSize = true, ForeColor = Color.FromArgb(216, 216, 216) });
                txtName = new TextBox { Location = new Point(12, 35), Width = 400, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(216, 216, 216), BorderStyle = BorderStyle.FixedSingle };
                Controls.Add(txtName);

                Controls.Add(new Label { Text = "Тип:", Location = new Point(12, 65), AutoSize = true, ForeColor = Color.FromArgb(216, 216, 216) });
                comboType = new ComboBox { Location = new Point(12, 85), Width = 400, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(216, 216, 216), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
                comboType.Items.AddRange(new object[] { "REG_SZ", "REG_DWORD", "REG_QWORD" });
                comboType.SelectedIndex = 0;
                Controls.Add(comboType);

                Controls.Add(new Label { Text = "Значение:", Location = new Point(12, 115), AutoSize = true, ForeColor = Color.FromArgb(216, 216, 216) });
                txtData = new TextBox { Location = new Point(12, 135), Width = 400, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(216, 216, 216), BorderStyle = BorderStyle.FixedSingle };
                Controls.Add(txtData);

                var btnOk = new Button { Text = "OK", Location = new Point(280, 185), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 55, 75), ForeColor = Color.FromArgb(240, 240, 240), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Отмена", Location = new Point(370, 185), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(170, 170, 170), DialogResult = DialogResult.Cancel };
                Controls.Add(btnOk); Controls.Add(btnCancel);
                AcceptButton = btnOk; CancelButton = btnCancel;
            }
        }

        public class EditValueDialog : Form
        {
            public string ValueData => txtData.Text;
            private TextBox txtData;

            public EditValueDialog(string name, string data, RegistryValueKind kind)
            {
                Text = $"Изменить: {name}";
                Size = new Size(500, 180);
                StartPosition = FormStartPosition.CenterParent;
                BackColor = Color.FromArgb(13, 13, 13);
                ForeColor = Color.FromArgb(216, 216, 216);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                TopMost = true; ShowInTaskbar = false;

                Controls.Add(new Label
                {
                    Text = $"Тип: {GetTypeNameStatic(kind)}. Введи новое значение:",
                    Location = new Point(12, 15),
                    AutoSize = true,
                    ForeColor = Color.FromArgb(170, 170, 170)
                });

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
                Controls.Add(txtData);

                var btnOk = new Button { Text = "OK", Location = new Point(320, 110), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 55, 75), ForeColor = Color.FromArgb(240, 240, 240), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Отмена", Location = new Point(410, 110), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(170, 170, 170), DialogResult = DialogResult.Cancel };
                Controls.Add(btnOk); Controls.Add(btnCancel);
                AcceptButton = btnOk; CancelButton = btnCancel;
            }

            private static string GetTypeNameStatic(RegistryValueKind kind)
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
        }
    }
}