// language: C#, file: Core/NedoMessageBox.cs
// Полная замена. Авторазмер + выделяемый текст (п.12).
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BunnyBlack.Core
{
    public static class NedoMessageBox
    {
        public static void Show(string text, bool isError = false)
        {
            const int maxWidth = 480;
            const int minWidth = 360;
            const int minHeight = 150;

            var messageFont = new Font("Segoe UI", 13, FontStyle.Bold);
            var fgColor = isError ? Color.FromArgb(255, 100, 100) : Color.FromArgb(136, 221, 170);

            Size textSize;
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
                textSize = g.MeasureString(text ?? "", messageFont, maxWidth - 60).ToSize();

            int formW = Math.Max(minWidth, Math.Min(maxWidth, textSize.Width + 80));
            int formH = Math.Max(minHeight, textSize.Height + 110);

            Form form = new Form
            {
                Text = "nedohackers",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = Color.FromArgb(13, 13, 13),
                ForeColor = Color.FromArgb(216, 216, 216),
                ClientSize = new Size(formW, formH),
                Font = new Font("Segoe UI", 10)
            };

            form.Icon = null;

            // п.12 — TextBox вместо Label: можно выделить и Ctrl+C
            TextBox messageBox = new TextBox
            {
                Text = text ?? "",
                Font = messageFont,
                ForeColor = fgColor,
                BackColor = Color.FromArgb(13, 13, 13),
                BorderStyle = BorderStyle.None,
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.None,
                Dock = DockStyle.Fill,
                TabStop = false,
                TextAlign = HorizontalAlignment.Center
            };
            messageBox.SelectionStart = 0;
            messageBox.SelectionLength = 0;

            // контейнер с отступами
            Panel container = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 13, 13),
                Padding = new Padding(20, 20, 20, 60)
            };
            container.Controls.Add(messageBox);
            form.Controls.Add(container);

            Button okButton = new Button
            {
                Text = "ОК",
                Width = 110,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = isError ? Color.FromArgb(60, 25, 25) : Color.FromArgb(30, 55, 40),
                ForeColor = isError ? Color.FromArgb(255, 136, 136) : Color.FromArgb(136, 221, 170),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                FlatAppearance =
                {
                    BorderSize = 1,
                    BorderColor = isError ? Color.FromArgb(120, 40, 40) : Color.FromArgb(51, 102, 68)
                },
                DialogResult = DialogResult.OK
            };

            okButton.Location = new Point(
                (form.ClientSize.Width - okButton.Width) / 2,
                form.ClientSize.Height - okButton.Height - 15
            );

            form.Controls.Add(okButton);
            okButton.BringToFront();
            form.AcceptButton = okButton;

            Form owner = FindOwner();
            if (owner != null) form.ShowDialog(owner);
            else form.ShowDialog();
        }

        private static Form FindOwner()
        {
            foreach (Form f in Application.OpenForms)
                if (f is MainForm) return f;
            return Form.ActiveForm;
        }
    }
}