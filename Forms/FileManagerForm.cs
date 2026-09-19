// language: C#, file: Forms/FileManagerForm.cs
// Проводник: дерево дисков + список файлов + операции.
// Правый клик: открыть, переименовать, удалить, свойства, хэш,
// в карантин, вставить путь в CmdLine / AppInit, заменить на Sethc/Utilman.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class FileManagerForm : UserControl
    {
        private TreeView treeView;
        private ListView listView;
        private Panel topBar;
        private TextBox pathBox;
        private Button backBtn;
        private Button upBtn;
        private Button refreshBtn;
        private Label statusLabel;
        private bool isWinRE;

        private string currentDir;

        public FileManagerForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
            LoadDrives();
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            // ============================================================
            // Верхняя панель
            // ============================================================
            topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(18, 18, 18),
                Padding = new Padding(8, 6, 8, 6)
            };

            backBtn = MakeNavBtn("◀", 0);
            backBtn.Click += (s, e) => NavigateUp();
            topBar.Controls.Add(backBtn);

            upBtn = MakeNavBtn("▲", 40);
            upBtn.Click += (s, e) => NavigateUp();
            topBar.Controls.Add(upBtn);

            refreshBtn = MakeNavBtn("⟳", 80);
            refreshBtn.Click += (s, e) => LoadDirectory(currentDir);
            topBar.Controls.Add(refreshBtn);

            pathBox = new TextBox
            {
                Location = new Point(124, 8),
                Width = 700,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10)
            };
            pathBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    string p = pathBox.Text.Trim();
                    if (Directory.Exists(p)) LoadDirectory(p);
                }
            };
            topBar.Controls.Add(pathBox);

            // ============================================================
            // Дерево дисков
            // ============================================================
            treeView = new TreeView
            {
                Dock = DockStyle.Left,
                Width = 240,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10),
                ShowLines = false,
                FullRowSelect = true,
                ItemHeight = 26
            };
            treeView.AfterSelect += (s, e) =>
            {
                if (e.Node?.Tag is string path && Directory.Exists(path))
                    LoadDirectory(path);
            };
            treeView.BeforeExpand += (s, e) =>
            {
                if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text == "…")
                {
                    e.Node.Nodes.Clear();
                    FillNodeChildren(e.Node);
                }
            };

            // ============================================================
            // Список файлов
            // ============================================================
            listView = new ListView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                BorderStyle = BorderStyle.None,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                Font = new Font("Segoe UI", 10),
                OwnerDraw = false
            };
            listView.Columns.Add("Имя", 320);
            listView.Columns.Add("Размер", 100);
            listView.Columns.Add("Изменён", 140);
            listView.Columns.Add("Атрибуты", 160);
            listView.DoubleClick += (s, e) => OpenSelected();
            listView.ContextMenuStrip = BuildContextMenu();

            // ============================================================
            // Статус-строка
            // ============================================================
            statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                ForeColor = Color.FromArgb(150, 150, 150),
                BackColor = Color.FromArgb(18, 18, 18),
                Font = new Font("Segoe UI", 9)
            };

            this.Controls.Add(listView);
            this.Controls.Add(treeView);
            this.Controls.Add(topBar);
            this.Controls.Add(statusLabel);
        }

        private Button MakeNavBtn(string text, int x)
        {
            return new Button
            {
                Text = text,
                Width = 32,
                Height = 26,
                Location = new Point(x + 8, 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10)
            };
        }

        // ============================================================
        // ДЕРЕВО
        // ============================================================
        private void LoadDrives()
        {
            treeView.Nodes.Clear();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;

                    string name = $"{d.Name.TrimEnd('\\')} {d.VolumeLabel}";
                    var node = new TreeNode(name) { Tag = d.RootDirectory.FullName };
                    node.Nodes.Add("…");
                    treeView.Nodes.Add(node);
                }
                catch { }
            }
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
                        child.Nodes.Add("…");
                        parent.Nodes.Add(child);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ============================================================
        // СПИСОК
        // ============================================================
        private void LoadDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            currentDir = dir;
            pathBox.Text = dir;
            listView.Items.Clear();

            try
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    try
                    {
                        var di = new DirectoryInfo(d);
                        var item = new ListViewItem(di.Name);
                        item.SubItems.Add("<DIR>");
                        item.SubItems.Add(di.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                        item.SubItems.Add(di.Attributes.ToString());
                        item.Tag = d;
                        item.ForeColor = Color.FromArgb(136, 221, 170);
                        listView.Items.Add(item);
                    }
                    catch { }
                }

                foreach (var f in Directory.GetFiles(dir))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        var item = new ListViewItem(fi.Name);
                        item.SubItems.Add(FormatBytes(fi.Length));
                        item.SubItems.Add(fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                        item.SubItems.Add(fi.Attributes.ToString());
                        item.Tag = f;
                        item.ForeColor = Color.FromArgb(216, 216, 216);
                        listView.Items.Add(item);
                    }
                    catch { }
                }

                statusLabel.Text = $"{dir}   |   Папок: {Directory.GetDirectories(dir).Length}   |   Файлов: {Directory.GetFiles(dir).Length}";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Ошибка: " + ex.Message;
            }
        }

        private void NavigateUp()
        {
            if (string.IsNullOrEmpty(currentDir)) return;
            try
            {
                var parent = Directory.GetParent(currentDir);
                if (parent != null) LoadDirectory(parent.FullName);
            }
            catch { }
        }

        private string GetSelectedPath()
        {
            if (listView.SelectedItems.Count == 0) return null;
            return listView.SelectedItems[0].Tag as string;
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
                if (Directory.Exists(path)) LoadDirectory(path);
                else Process.Start(path);
            }
            catch (Exception ex) { NedoMessageBox.Show("Ошибка открытия: " + ex.Message, isError: true); }
        }

        private void DeleteSelected()
        {
            string path = GetSelectedPath();
            if (path == null) return;

            if (MessageBoxHelper.Show($"Удалить '{Path.GetFileName(path)}'?",
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            string err;
            if (UnlockHelper.ForceDelete(path, out err))
                LoadDirectory(currentDir);
            else
                NedoMessageBox.Show("Ошибка: " + err, isError: true);
        }

        private void SendToQuarantine()
        {
            string path = GetSelectedPath();
            if (path == null || Directory.Exists(path)) return;

            string err;
            if (Quarantine.Add(path, "Ручной карантин", out err))
            {
                NedoMessageBox.Show("Файл отправлен в карантин.");
                LoadDirectory(currentDir);
            }
            else NedoMessageBox.Show("Ошибка: " + err, isError: true);
        }

        private void RenameSelected()
        {
            string path = GetSelectedPath();
            if (path == null) return;

            using (var dlg = new Form
            {
                Text = "Переименовать",
                Size = new Size(450, 140),
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
                    Text = Path.GetFileName(path),
                    Location = new Point(12, 12),
                    Width = 400,
                    BackColor = Color.FromArgb(24, 24, 24),
                    ForeColor = Color.FromArgb(216, 216, 216),
                    BorderStyle = BorderStyle.FixedSingle
                };
                var ok = new Button { Text = "OK", Location = new Point(320, 55), Size = new Size(80, 30), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 55, 75), ForeColor = Color.FromArgb(240, 240, 240) };
                var cancel = new Button { Text = "Отмена", Location = new Point(230, 55), Size = new Size(80, 30), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.FromArgb(170, 170, 170) };
                dlg.Controls.Add(tb); dlg.Controls.Add(ok); dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok; dlg.CancelButton = cancel;

                if (dlg.ShowDialog(FindOwner()) == DialogResult.OK)
                {
                    try
                    {
                        string newPath = Path.Combine(Path.GetDirectoryName(path), tb.Text);
                        if (Directory.Exists(path)) Directory.Move(path, newPath);
                        else File.Move(path, newPath);
                        LoadDirectory(currentDir);
                    }
                    catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
                }
            }
        }

        private void ShowProperties()
        {
            string path = GetSelectedPath();
            if (path == null) return;

            try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
            catch { }
        }

        private void ComputeHash()
        {
            string path = GetSelectedPath();
            if (path == null || Directory.Exists(path)) return;

            try
            {
                string hash = Heuristics.Sha256(path);
                NedoMessageBox.Show($"SHA256:\n{hash}\n\nПуть:\n{path}");
            }
            catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
        }

        // ============================================================
        // ВСТАВКА В CMDLINE / APPINIT / SETHC
        // ============================================================
        private void InsertToCmdLine()
        {
            string path = GetSelectedPath();
            if (path == null) return;

            if (RegistryHelper.SetCmdLineValue("CmdLine", path))
                NedoMessageBox.Show("Путь прописан в CmdLine.\nПрименится при следующей загрузке.");
            else
                NedoMessageBox.Show("Не удалось прописать CmdLine.", isError: true);
        }

        private void InsertToAppInit()
        {
            string path = GetSelectedPath();
            if (path == null) return;

            if (RegistryHelper.SetAppInitDllsValue("AppInit_DLLs", path))
            {
                RegistryHelper.SetAppInitDllsValue("LoadAppInit_DLLs", "1");
                NedoMessageBox.Show("Путь прописан в AppInit_DLLs.\nПрименится при следующей загрузке.");
            }
            else NedoMessageBox.Show("Не удалось прописать AppInit_DLLs.", isError: true);
        }

        private void ReplaceSethcWithSelected()
        {
            if (!isWinRE)
            {
                NedoMessageBox.Show("Замена Sethc/Utilman доступна только в WinRE.", isError: true);
                return;
            }

            string path = GetSelectedPath();
            if (path == null || !File.Exists(path)) return;

            if (MessageBoxHelper.Show(
                $"Заменить sethc.exe и utilman.exe на:\n{path}?",
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                string sysDrive = RegistryHelper.GetSystemDrive();
                string sethc = Path.Combine(sysDrive, @"Windows\System32\sethc.exe");
                string utilman = Path.Combine(sysDrive, @"Windows\System32\utilman.exe");

                if (!File.Exists(sethc + ".bak")) File.Copy(sethc, sethc + ".bak", true);
                if (!File.Exists(utilman + ".bak")) File.Copy(utilman, utilman + ".bak", true);

                File.Copy(path, sethc, true);
                File.Copy(path, utilman, true);

                NedoMessageBox.Show("Заменено.");
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true);
            }
        }

        // ============================================================
        // CONTEXT MENU
        // ============================================================
        private ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip { BackColor = Color.FromArgb(17, 17, 17), ForeColor = Color.FromArgb(216, 216, 216) };

            var open = new ToolStripMenuItem("Открыть");
            open.Click += (s, e) => OpenSelected();
            m.Items.Add(open);

            var rename = new ToolStripMenuItem("Переименовать");
            rename.Click += (s, e) => RenameSelected();
            m.Items.Add(rename);

            var hash = new ToolStripMenuItem("SHA256");
            hash.Click += (s, e) => ComputeHash();
            m.Items.Add(hash);

            var props = new ToolStripMenuItem("Свойства");
            props.Click += (s, e) => ShowProperties();
            m.Items.Add(props);

            m.Items.Add(new ToolStripSeparator());

            var quar = new ToolStripMenuItem("В карантин");
            quar.ForeColor = Color.FromArgb(255, 200, 120);
            quar.Click += (s, e) => SendToQuarantine();
            m.Items.Add(quar);

            var del = new ToolStripMenuItem("Удалить");
            del.ForeColor = Color.FromArgb(255, 150, 150);
            del.Click += (s, e) => DeleteSelected();
            m.Items.Add(del);

            m.Items.Add(new ToolStripSeparator());

            var cmd = new ToolStripMenuItem("Прописать в CmdLine");
            cmd.Click += (s, e) => InsertToCmdLine();
            m.Items.Add(cmd);

            var appinit = new ToolStripMenuItem("Прописать в AppInit_DLLs");
            appinit.Click += (s, e) => InsertToAppInit();
            m.Items.Add(appinit);

            if (isWinRE)
            {
                var sethc = new ToolStripMenuItem("Заменить sethc/utilman этим файлом");
                sethc.ForeColor = Color.FromArgb(255, 200, 120);
                sethc.Click += (s, e) => ReplaceSethcWithSelected();
                m.Items.Add(sethc);
            }

            return m;
        }

        // ============================================================
        // ХЕЛПЕРЫ
        // ============================================================
        private string FormatBytes(long bytes)
        {
            string[] s = { "Б", "КБ", "МБ", "ГБ" };
            double len = bytes; int i = 0;
            while (len >= 1024 && i < s.Length - 1) { i++; len /= 1024; }
            return $"{len:0.##} {s[i]}";
        }

        private Form FindOwner()
        {
            foreach (Form f in Application.OpenForms)
                if (f is MainForm) return f;
            return Form.ActiveForm;
        }
    }
}