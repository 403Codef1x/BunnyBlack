using System.Windows.Forms;

namespace BunnyBlack.Core
{
    public static class MessageBoxHelper
    {
        public static DialogResult Show(
            string text,
            string caption = "Bunny Black",
            MessageBoxButtons buttons = MessageBoxButtons.OK,
            MessageBoxIcon icon = MessageBoxIcon.Information)
        {
            Form owner = null;
            foreach (Form f in Application.OpenForms)
            {
                if (f is MainForm) { owner = f; break; }
            }
            if (owner == null) owner = Form.ActiveForm;

            if (owner != null)
                return MessageBox.Show(owner, text, caption, buttons, icon);
            else
                return MessageBox.Show(text, caption, buttons, icon);
        }
    }
}