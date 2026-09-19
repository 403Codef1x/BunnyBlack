// language: C#, file: Forms/ShutdownForm.cs
// Полная замена. Только 3 действия: WinRE, BIOS, выход из Safe Mode.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using BunnyBlack.Core;

namespace BunnyBlack.Forms
{
    public partial class ShutdownForm : UserControl
    {
        private bool isWinRE;

        public ShutdownForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(28, 24, 28, 20),
                BackColor = Color.FromArgb(13, 13, 13)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = "Питание" + (isWinRE ? " (WinRE)" : ""),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(title, 0, 0);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Transparent };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 10)
            };

            var items = new (string name, string desc, Action action)[]
            {
                ("Перезагрузить в WinRE", "Перезагрузка в среду восстановления (shutdown /r /o)", RebootToWinRE),
                ("Перезагрузить в BIOS", "Перезагрузка в прошивку (shutdown /r /fw)", RebootToBIOS),
                ("Обычный режим", "Снять флаг Safe Mode и перезагрузиться", RebootToNormal),
            };

            foreach (var it in items)
            {
                var panel = CreateToolPanel(it.name, it.desc, it.action);
                flow.Controls.Add(panel);
            }

            scroll.Controls.Add(flow);
            layout.Controls.Add(scroll, 0, 1);
            this.Controls.Add(layout);
        }

        private Panel CreateToolPanel(string name, string desc, Action action)
        {
            var panel = new Panel
            {
                Height = 65,
                Width = 850,
                BackColor = Color.FromArgb(22, 22, 22),
                Margin = new Padding(0, 0, 0, 8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(16, 8, 16, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

            var info = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

            info.Controls.Add(new Label
            {
                Text = name,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            }, 0, 0);

            info.Controls.Add(new Label
            {
                Text = desc,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(102, 102, 102),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            }, 0, 1);

            layout.Controls.Add(info, 0, 0);

            var btnContainer = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 130,
                Height = 38,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent
            };

            var btn = new Button
            {
                Text = "Выполнить",
                Height = 38,
                Width = 130,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(240, 240, 240),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11)
            };
            btn.Click += (s, e) =>
            {
                try { action(); }
                catch (Exception ex) { NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true); }
            };

            btnContainer.Controls.Add(btn);
            layout.Controls.Add(btnContainer, 1, 0);

            panel.Controls.Add(layout);
            return panel;
        }

        // ============================================================
        // ДЕЙСТВИЯ
        // ============================================================
        private void RebootToWinRE()
        {
            if (MessageBoxHelper.Show(
                "Перезагрузить в среду восстановления (WinRE)?",
                "Перезагрузка в WinRE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            RunCmd("shutdown /r /o /t 0");
        }

        private void RebootToBIOS()
        {
            if (MessageBoxHelper.Show(
                "Перезагрузить в прошивку (BIOS/UEFI)?\nТребуется поддержка UEFI-прошивкой.",
                "Перезагрузка в BIOS", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            RunCmd("shutdown /r /fw /t 0");
        }

        private void RebootToNormal()
        {
            if (MessageBoxHelper.Show(
                "Снять флаг Safe Mode и перезагрузиться?",
                "Обычный режим", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            RunCmd("bcdedit /deletevalue {current} safeboot & shutdown /r /t 5");
        }

        private void RunCmd(string args)
        {
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(si);
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show("Ошибка: " + ex.Message, isError: true);
            }
        }
    }
}