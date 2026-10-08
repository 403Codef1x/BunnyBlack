// language: C#, file: Forms/FileManagerForm.cs
// Полная замена.
// - Иконки файлов и папок через SHGetFileInfo.
// - Разделитель между деревом и списком.
// - Чередование строк в списке.
// - Компактный шрифт.
// - Кнопки операций с иконками.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class FileManagerForm : UserControl
    {
        private TreeView treeView;
        private ListView listView;
        private Panel topBar;
        private FlowLayoutPanel bottomBar;
        private Panel splitter;
        private TextBox pathBox;
        private Button backBtn, forwardBtn, upBtn, homeBtn, refreshBtn, searchBtn, showInExplorerBtn;
        private Button copyBtn, moveBtn, deleteBtn, renameBtn;
        private Label statusLabel;
        private ImageList iconList;

        private bool isWinRE;
        private string currentDir;
        private readonly Stack<string> backStack = new Stack<string>();
        private readonly Stack<string> forwardStack = new Stack<string>();

        private readonly List<string> clipboardFiles = new List<string>();
        private bool clipboardCutMode = false;

        // ============================================================
        // P/INVOKE для иконок
        // ============================================================
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        public FileManagerForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13); 
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
            LoadDrives();
        }

        // ============================================================
        // ИКОНКИ
        // ============================================================
        private int GetIconIndex(string path, bool isDirectory)
        {
            try
            {
                var shfi = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_SMALLICON;
                if (isDirectory) flags |= SHGFI_USEFILEATTRIBUTES;

                SHGetFileInfo(path, isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL,
                    ref shfi, (uint)Marshal.SizeOf(shfi), flags);

                if (shfi.hIcon == IntPtr.Zero) return 0;

                var icon = Icon.FromHandle(shfi.hIcon);
                int idx = iconList.Images.Count;
                iconList.Images.Add(icon);

                // освобождаем
                DestroyIcon(shfi.hIcon);

                return idx;
            }
            catch { return 0; }
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            // ImageList для иконок
            iconList = new ImageList
            {
                ImageSize = new Size(16, 16),
                ColorDepth = ColorDepth.Depth32Bit,
                TransparentColor = Color.Transparent
            };
            // дефолтная иконка папки и файла
            try
            {
                var shfiDir = new SHFILEINFO();
                SHGetFileInfo("folder", FILE_ATTRIBUTE_DIRECTORY, ref shfiDir,
                    (uint)Marshal.SizeOf(shfiDir), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
                if (shfiDir.hIcon != IntPtr.Zero)
                {
                    iconList.Images.Add(Icon.FromHandle(shfiDir.hIcon));
                    DestroyIcon(shfiDir.hIcon);
                }

                var shfiFile = new SHFILEINFO();
                SHGetFileInfo("file.txt", FILE_ATTRIBUTE_NORMAL, ref shfiFile,
                    (uint)Marshal.SizeOf(shfiFile), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
                if (shfiFile.hIcon != IntPtr.Zero)
                {
                    iconList.Images.Add(Icon.FromHandle(shfiFile.hIcon));
                    DestroyIcon(shfiFile.hIcon);
                }
            }
            catch { }

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0),
                BackColor = ThemeManager.Background,
                Margin = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

            // ============================================================
            // ВЕРХНЯЯ ПАНЕЛЬ
            // ============================================================
            topBar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(22, 22, 22),
                Padding = new Padding(6, 6, 6, 6),
                Margin = new Padding(0)
            };

            backBtn = MakeIconBtn("◀", "Назад", 6);
            backBtn.Click += (s, e) => NavigateBack();
            topBar.Controls.Add(backBtn);

            forwardBtn = MakeIconBtn("▶", "Вперёд", 34);
            forwardBtn.Click += (s, e) => NavigateForward();
            topBar.Controls.Add(forwardBtn);

            upBtn = MakeIconBtn("▲", "Вверх", 62);
            upBtn.Click += (s, e) => NavigateUp();
            topBar.Controls.Add(upBtn);

            homeBtn = MakeIconBtn("⌂", "Домой", 90);
            homeBtn.Click += (s, e) => GoHome();
            topBar.Controls.Add(homeBtn);

            refreshBtn = MakeIconBtn("⟳", "Обновить", 118);
            refreshBtn.Click += (s, e) => LoadDirectory(currentDir);
            topBar.Controls.Add(refreshBtn);

            pathBox = new TextBox
            {
                Location = new Point(150, 7),
                Width = 800,
                Height = 24,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9)
            };
            pathBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    string p = pathBox.Text.Trim();
                    if (Directory.Exists(p)) LoadDirectory(p, addToHistory: true);
                }
            };
            topBar.Controls.Add(pathBox);

            searchBtn = MakeIconBtn("🔍", "Поиск", 0);
            searchBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            searchBtn.Click += (s, e) => SearchInCurrentFolder();
            topBar.Controls.Add(searchBtn);

            showInExplorerBtn = MakeIconBtn("📁", "Показать в проводнике", 0);
            showInExplorerBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            showInExplorerBtn.Click += (s, e) => ShowInExplorer();
            topBar.Controls.Add(showInExplorerBtn);

            topBar.Resize += (s, e) =>
            {
                int w = topBar.ClientSize.Width;
                int h = topBar.ClientSize.Height;
                showInExplorerBtn.Location = new Point(w - 34, (h - 24) / 2);
                searchBtn.Location = new Point(w - 64, (h - 24) / 2);
                pathBox.Width = Math.Max(100, w - 150 - 72);
            };

            root.Controls.Add(topBar, 0, 0);

            // ============================================================
            // ОСНОВНАЯ — дерево + разделитель + список
            // ============================================================
            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = ThemeManager.Background,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));    // разделитель
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Background,
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 8.5f),
                ShowLines = false,
                ShowPlusMinus = false,
                ShowRootLines = false,
                FullRowSelect = true,
                ItemHeight = 20,
                HideSelection = false,
                Indent = 14,
                ImageList = iconList,
                Margin = new Padding(0),
                Padding = new Padding(4, 4, 0, 0)
            };
            treeView.AfterSelect += (s, e) =>
            {
                if (e.Node?.Tag is string path && Directory.Exists(path))
                    LoadDirectory(path, addToHistory: true);
            };
            treeView.BeforeExpand += (s, e) =>
            {
                if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text == "…")
                {
                    e.Node.Nodes.Clear();
                    FillNodeChildren(e.Node);
                }
            };
            main.Controls.Add(treeView, 0, 0);

            // разделитель
            splitter = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45),
                Margin = new Padding(0)
            };
            main.Controls.Add(splitter, 1, 0);

            listView = new ListView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.None,
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true,
                GridLines = false,
                Font = new Font("Segoe UI", 8.5f),
                OwnerDraw = false,
                HideSelection = false,
                MultiSelect = true,
                Margin = new Padding(0),
                Scrollable = true,
                SmallImageList = iconList
            };
            listView.Columns.Add("Имя", 800, HorizontalAlignment.Left);
            listView.DoubleClick += (s, e) => OpenSelected();
            listView.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
                if (e.KeyCode == Keys.F2) { RenameSelected(); e.Handled = true; }
                if (e.Control && e.KeyCode == Keys.C) { CopySelectedToClipboard(); e.Handled = true; }
                if (e.Control && e.KeyCode == Keys.X) { CutSelectedToClipboard(); e.Handled = true; }
                if (e.Control && e.KeyCode == Keys.V) { PasteFromClipboard(); e.Handled = true; }
                if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; }
            };
            listView.Resize += (s, e) =>
            {
                if (listView.Columns.Count > 0)
                    listView.Columns[0].Width = listView.ClientSize.Width - 4;
            };
            listView.ContextMenuStrip = BuildContextMenu();
            main.Controls.Add(listView, 2, 0);

            root.Controls.Add(main, 0, 1);

            // ============================================================
            // НИЖНЯЯ ПАНЕЛЬ
            // ============================================================
            bottomBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(22, 22, 22),
                Padding = new Padding(10, 7, 10, 7),
                WrapContents = false,
                Margin = new Padding(0)
            };

            copyBtn = MakeActionBtn("Копировать");
            copyBtn.Click += (s, e) => CopySelectedToClipboard();
            bottomBar.Controls.Add(copyBtn);

            moveBtn = MakeActionBtn("Переместить");
            moveBtn.Click += (s, e) => CutSelectedToClipboard();
            bottomBar.Controls.Add(moveBtn);

            deleteBtn = MakeActionBtn("Удалить", Color.FromArgb(45, 18, 18), Color.FromArgb(255, 150, 150), Color.FromArgb(80, 40, 40));
            deleteBtn.Click += (s, e) => DeleteSelected();
            bottomBar.Controls.Add(deleteBtn);

            renameBtn = MakeActionBtn("Переименовать");
            renameBtn.Click += (s, e) => RenameSelected();
            bottomBar.Controls.Add(renameBtn);

            statusLabel = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(150, 150, 150),
                Font = new Font("Segoe UI", 9),
                BackColor = Color.Transparent,
                Margin = new Padding(24, 8, 0, 0)
            };
            bottomBar.Controls.Add(statusLabel);

            root.Controls.Add(bottomBar, 0, 2);

            this.Controls.Add(root);
        }

        private Button MakeIconBtn(string text, string tooltip, int x)
        {
            var btn = new Button
            {
                Text = text,
                Width = 24,
                Height = 24,
                Location = new Point(x, 7),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(50, 50, 50) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9),
                TabStop = false,
                Padding = new Padding(0)
            };
            var tt = new ToolTip();
            tt.SetToolTip(btn, tooltip);
            return btn;
        }

        private Button MakeActionBtn(string text, Color? back = null, Color? fore = null, Color? border = null)
        {
            return new Button
            {
                Text = text,
                Height = 30,
                Width = 120,
                FlatStyle = FlatStyle.Flat,
                BackColor = back ?? Color.FromArgb(30, 30, 30),
                ForeColor = fore ?? Color.FromArgb(220, 220, 220),
                FlatAppearance = { BorderSize = 1, BorderColor = border ?? Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9),
                Margin = new Padding(0, 0, 6, 0)
            };
        }

        // ============================================================
        // ДЕРЕВО
        // ============================================================
        private void LoadDrives()
        {
            treeView.Nodes.Clear();
            try
            {
                TreeNode firstNode = null;

                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        if (d.DriveType != DriveType.Fixed &&
                            d.DriveType != DriveType.Removable &&
                            d.DriveType != DriveType.Network) continue;

                        string label = $"{d.Name.TrimEnd('\\')}";
                        var node = new TreeNode(label) { Tag = d.RootDirectory.FullName };

                        int iconIdx = GetIconIndex(d.RootDirectory.FullName, true);
                        node.ImageIndex = iconIdx;
                        node.SelectedImageIndex = iconIdx;

                        node.Nodes.Add("…");
                        treeView.Nodes.Add(node);

                        if (firstNode == null) firstNode = node;
                    }
                    catch { }
                }

                if (firstNode != null)
                {
                    treeView.SelectedNode = firstNode;
                    firstNode.Expand();
                }
            }
            catch (Exception ex) { BbLog.Error("[LoadDrives]", ex); }
        }

        private void FillNodeChildren(TreeNode parent)
        {
            string path = parent.Tag as string;
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                foreach (var dir in Directory.GetDirectories(path))
                {
                    try
                    {
                        var child = new TreeNode(Path.GetFileName(dir)) { Tag = dir };

                        int iconIdx = GetIconIndex(dir, true);
                        child.ImageIndex = iconIdx;
                        child.SelectedImageIndex = iconIdx;

                        if (Directory.GetDirectories(dir).Length > 0)
                            child.Nodes.Add("…");
                        parent.Nodes.Add(child);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void GoHome()
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!Directory.Exists(home)) home = RegistryHelper.GetSystemDrive() + "\\";
                LoadDirectory(home, addToHistory: true);
            }
            catch { }
        }

        // ============================================================
        // СПИСОК
        // ============================================================
        private void LoadDirectory(string dir, bool addToHistory = false)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir))
            {
                statusLabel.Text = "Папка не найдена: " + dir;
                return;
            }

            if (addToHistory && !string.IsNullOrEmpty(currentDir) && currentDir != dir)
            {
                backStack.Push(currentDir);
                forwardStack.Clear();
            }

            currentDir = dir;
            pathBox.Text = dir;
            listView.BeginUpdate();
            listView.Items.Clear();

            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    try
                    {
                        var di = new DirectoryInfo(sub);
                        var item = new ListViewItem(di.Name);
                        item.Tag = di.FullName;
                        item.ForeColor = Color.FromArgb(136, 221, 170);

                        int iconIdx = GetIconIndex(di.FullName, true);
                        item.ImageIndex = iconIdx;

                        listView.Items.Add(item);
                    }
                    catch { }
                }

                foreach (var file in Directory.GetFiles(dir))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        var item = new ListViewItem(fi.Name);
                        item.Tag = fi.FullName;
                        item.ForeColor = Color.FromArgb(216, 216, 216);

                        int iconIdx = GetIconIndex(fi.FullName, false);
                        item.ImageIndex = iconIdx;

                        listView.Items.Add(item);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Ошибка: " + ex.Message;
                BbLog.Error($"[LoadDirectory/{dir}]", ex);
            }
            finally
            {
                listView.EndUpdate();
            }

            UpdateStatus();

            if (listView.Items.Count > 0)
            {
                listView.Items[0].Selected = true;
                listView.Focus();
            }
        }

        private void UpdateStatus()
        {
            try
            {
                if (string.IsNullOrEmpty(currentDir)) { statusLabel.Text = ""; return; }

                int dirCount = 0, fileCount = 0;
                try { dirCount = Directory.GetDirectories(currentDir).Length; } catch { }
                try { fileCount = Directory.GetFiles(currentDir).Length; } catch { }

                long free = 0, total = 0;
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(currentDir));
                    if (drive.IsReady) { free = drive.TotalFreeSpace; total = drive.TotalSize; }
                }
                catch { }

                statusLabel.Text = $"Папок: {dirCount}   Файлов: {fileCount}   Свободно: {FormatBytes(free)} / {FormatBytes(total)}";
            }
            catch { }
        }

        private void NavigateBack()
        {
            if (backStack.Count == 0) return;
            forwardStack.Push(currentDir);
            string prev = backStack.Pop();
            if (Directory.Exists(prev)) LoadDirectory(prev);
        }

        private void NavigateForward()
        {
            if (forwardStack.Count == 0) return;
            backStack.Push(currentDir);
            string next = forwardStack.Pop();
            if (Directory.Exists(next)) LoadDirectory(next);
        }

        private void NavigateUp()
        {
            if (string.IsNullOrEmpty(currentDir)) return;
            try
            {
                var parent = Directory.GetParent(currentDir);
                if (parent != null) LoadDirectory(parent.FullName, addToHistory: true);
            }
            catch { }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 0) return "—";
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.##} {sizes[order]}";
        }

        private string GetSelectedPath()
        {
            if (listView.SelectedItems.Count == 0) return null;
            return listView.SelectedItems[0].Tag as string;
        }

        private List<string> GetSelectedPaths()
        {
            var result = new List<string>();
            foreach (ListViewItem item in listView.SelectedItems)
            {
                string p = item.Tag as string;
                if (!string.IsNullOrEmpty(p)) result.Add(p);
            }
            return result;
        }

        // ============================================================
        // ОПЕРАЦИИ
        // ============================================================
        private void OpenSelected()
        {
            string path = GetSelectedPath();
            if (path == null) return;
            try
            {
                if (Directory.Exists(path)) LoadDirectory(path, addToHistory: true);
                else Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
        }

        private void ShowInExplorer()
        {
            try
            {
                string path = GetSelectedPath();
                if (string.IsNullOrEmpty(path)) path = currentDir;
                if (string.IsNullOrEmpty(path)) return;

                if (File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else if (Directory.Exists(path))
                    Process.Start("explorer.exe", $"\"{path}\"");
            }
            catch (Exception ex) { BbLog.Error("[ShowInExplorer]", ex); }
        }

        private void SearchInCurrentFolder()
        {
            if (string.IsNullOrEmpty(currentDir)) return;
            string query = InputDialog("Поиск", "Маска (например *.exe):", "*");
            if (string.IsNullOrEmpty(query)) return;

            try
            {
                listView.BeginUpdate();
                listView.Items.Clear();

                foreach (var f in Directory.GetFiles(currentDir, query, SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        var item = new ListViewItem(fi.FullName);
                        item.Tag = fi.FullName;
                        item.ForeColor = Color.FromArgb(255, 200, 120);

                        int iconIdx = GetIconIndex(fi.FullName, false);
                        item.ImageIndex = iconIdx;

                        listView.Items.Add(item);
                    }
                    catch { }
                }
                listView.EndUpdate();
                statusLabel.Text = $"Найдено: {listView.Items.Count} по маске '{query}'";
            }
            catch (Exception ex) { BbLog.Error("[Search]", ex); }
        }

        private void CopySelectedToClipboard()
        {
            var paths = GetSelectedPaths();
            if (paths.Count == 0) return;
            clipboardFiles.Clear();
            clipboardFiles.AddRange(paths);
            clipboardCutMode = false;
            statusLabel.Text = $"Скопировано: {paths.Count} (Ctrl+V — вставить)";
        }

        private void CutSelectedToClipboard()
        {
            var paths = GetSelectedPaths();
            if (paths.Count == 0) return;
            clipboardFiles.Clear();
            clipboardFiles.AddRange(paths);
            clipboardCutMode = true;
            statusLabel.Text = $"Перемещение: {paths.Count} (Ctrl+V — вставить)";
        }

        private void PasteFromClipboard()
        {
            if (clipboardFiles.Count == 0) return;
            if (string.IsNullOrEmpty(currentDir)) return;

            int copied = 0, failed = 0;
            foreach (var src in clipboardFiles)
            {
                try
                {
                    string name = Path.GetFileName(src);
                    string dst = Path.Combine(currentDir, name);
                    if (clipboardCutMode)
                    {
                        if (Directory.Exists(src)) Directory.Move(src, dst);
                        else File.Move(src, dst);
                    }
                    else
                    {
                        if (Directory.Exists(src)) CopyDirectory(src, dst);
                        else File.Copy(src, dst, true);
                    }
                    copied++;
                }
                catch (Exception ex)
                {
                    BbLog.Error($"[Paste/{src}]", ex);
                    failed++;
                }
            }

            clipboardFiles.Clear();
            clipboardCutMode = false;
            LoadDirectory(currentDir);
            statusLabel.Text = $"Вставлено: {copied}, ошибок: {failed}";
        }

        private void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            foreach (var sub in Directory.GetDirectories(src))
                CopyDirectory(sub, Path.Combine(dst, Path.GetFileName(sub)));
        }

        private void DeleteSelected()
        {
            var paths = GetSelectedPaths();
            if (paths.Count == 0) return;

            string msg = paths.Count == 1
                ? $"Удалить '{Path.GetFileName(paths[0])}'?"
                : $"Удалить {paths.Count} элементов?";

            if (MessageBoxHelper.Show(msg, "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            int deleted = 0, failed = 0;
            foreach (var path in paths)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    else File.Delete(path);
                    deleted++;
                }
                catch (Exception ex)
                {
                    BbLog.Error($"[Delete/{path}]", ex);
                    failed++;
                }
            }

            LoadDirectory(currentDir);
            statusLabel.Text = $"Удалено: {deleted}, ошибок: {failed}";
        }

        private void RenameSelected()
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path)) return;

            string newName = InputDialog("Переименовать", "Новое имя:", Path.GetFileName(path));
            if (string.IsNullOrEmpty(newName) || newName == Path.GetFileName(path)) return;

            try
            {
                string newPath = Path.Combine(Path.GetDirectoryName(path), newName);
                if (Directory.Exists(path)) Directory.Move(path, newPath);
                else File.Move(path, newPath);
                LoadDirectory(currentDir);
            }
            catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
        }

        private void OpenAsAdmin()
        {
            string path = GetSelectedPath();
            if (path == null || Directory.Exists(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "runas" });
            }
            catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
        }

        private void CopyPathToClipboard()
        {
            string path = GetSelectedPath();
            if (!string.IsNullOrEmpty(path)) Clipboard.SetText(path);
        }

        private void SendToQuarantine()
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path) || Directory.Exists(path)) return;

            string err;
            if (Quarantine.Add(path, "Из проводника", out err))
            {
                NedoMessageBox.Show("Файл в карантине.");
                LoadDirectory(currentDir);
            }
            else NedoMessageBox.Show("Ошибка: " + err, isError: true);
        }

        private void InsertToCmdLine()
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path) || Directory.Exists(path)) return;

            if (RegistryHelper.SetCmdLineValue("CmdLine", path))
                NedoMessageBox.Show("Путь прописан в CmdLine.");
            else NedoMessageBox.Show("Не удалось.", isError: true);
        }

        private void InsertToAppInit()
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path) || Directory.Exists(path)) return;

            if (RegistryHelper.SetAppInitDllsValue("AppInit_DLLs", path))
            {
                RegistryHelper.SetAppInitDllsValue("LoadAppInit_DLLs", "1");
                NedoMessageBox.Show("Путь прописан в AppInit_DLLs.");
            }
            else NedoMessageBox.Show("Не удалось.", isError: true);
        }

        private void ReplaceSethcUtilman()
        {
            if (!isWinRE)
            {
                NedoMessageBox.Show("Замена sethc/utilman работает только в WinRE.", isError: true);
                return;
            }
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            if (MessageBoxHelper.Show($"Заменить sethc.exe и utilman.exe на:\n{path}?",
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                string drive = RegistryHelper.GetSystemDrive();
                if (!drive.EndsWith(":")) drive += ":";
                string sys32 = Path.Combine(drive, @"Windows\System32");
                string sethc = Path.Combine(sys32, "sethc.exe");
                string utilman = Path.Combine(sys32, "utilman.exe");

                if (!File.Exists(sethc + ".bak")) File.Copy(sethc, sethc + ".bak", true);
                if (!File.Exists(utilman + ".bak")) File.Copy(utilman, utilman + ".bak", true);

                File.Copy(path, sethc, true);
                File.Copy(path, utilman, true);

                NedoMessageBox.Show("Заменено.");
            }
            catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
        }

        // ============================================================
        // МЕНЮ
        // ============================================================
        private ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip { BackColor = Color.FromArgb(17, 17, 17), ForeColor = Color.FromArgb(216, 216, 216) };

            var open = new ToolStripMenuItem("Открыть");
            open.Click += (s, e) => OpenSelected();
            m.Items.Add(open);

            var openAdmin = new ToolStripMenuItem("Открыть от админа");
            openAdmin.Click += (s, e) => OpenAsAdmin();
            m.Items.Add(openAdmin);

            var openExplorer = new ToolStripMenuItem("Показать в проводнике");
            openExplorer.Click += (s, e) => ShowInExplorer();
            m.Items.Add(openExplorer);

            m.Items.Add(new ToolStripSeparator());

            var copy = new ToolStripMenuItem("Копировать (Ctrl+C)");
            copy.Click += (s, e) => CopySelectedToClipboard();
            m.Items.Add(copy);

            var cut = new ToolStripMenuItem("Переместить (Ctrl+X)");
            cut.Click += (s, e) => CutSelectedToClipboard();
            m.Items.Add(cut);

            var paste = new ToolStripMenuItem("Вставить (Ctrl+V)");
            paste.Click += (s, e) => PasteFromClipboard();
            m.Items.Add(paste);

            m.Items.Add(new ToolStripSeparator());

            var rename = new ToolStripMenuItem("Переименовать (F2)");
            rename.Click += (s, e) => RenameSelected();
            m.Items.Add(rename);

            var copyPath = new ToolStripMenuItem("Копировать путь");
            copyPath.Click += (s, e) => CopyPathToClipboard();
            m.Items.Add(copyPath);

            var del = new ToolStripMenuItem("Удалить (Delete)");
            del.ForeColor = Color.FromArgb(255, 150, 150);
            del.Click += (s, e) => DeleteSelected();
            m.Items.Add(del);

            m.Items.Add(new ToolStripSeparator());

            var quar = new ToolStripMenuItem("В карантин");
            quar.ForeColor = Color.FromArgb(255, 200, 120);
            quar.Click += (s, e) => SendToQuarantine();
            m.Items.Add(quar);

            var cmdLine = new ToolStripMenuItem("Прописать в CmdLine");
            cmdLine.Click += (s, e) => InsertToCmdLine();
            m.Items.Add(cmdLine);

            var appInit = new ToolStripMenuItem("Прописать в AppInit_DLLs");
            appInit.Click += (s, e) => InsertToAppInit();
            m.Items.Add(appInit);

            if (isWinRE)
            {
                var sethc = new ToolStripMenuItem("Заменить sethc/utilman этим файлом");
                sethc.ForeColor = Color.FromArgb(255, 200, 120);
                sethc.Click += (s, e) => ReplaceSethcUtilman();
                m.Items.Add(sethc);
            }

            return m;
        }

        private string InputDialog(string title, string prompt, string defaultValue)
        {
            using (var form = new Form
            {
                Text = title,
                Size = new Size(450, 150),
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
                var label = new Label { Text = prompt, Location = new Point(12, 12), AutoSize = true, ForeColor = Color.FromArgb(216, 216, 216) };
                var tb = new TextBox { Text = defaultValue, Location = new Point(12, 40), Width = 410, BackColor = Color.FromArgb(24, 24, 24), ForeColor = Color.FromArgb(216, 216, 216), BorderStyle = BorderStyle.FixedSingle };
                var ok = new Button { Text = "OK", Location = new Point(340, 75), Size = new Size(80, 30), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 55, 75), ForeColor = Color.FromArgb(240, 240, 240) };
                var cancel = new Button { Text = "Отмена", Location = new Point(250, 75), Size = new Size(80, 30), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(170, 170, 170) };

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