// language: C#, file: Core/ThemeHelper.cs
// Утилита для рекурсивной перекраски контролов.
// Panel "modeBanner" исключён — у него свой цвет (SuccessBack / WarningBack).
using System.Drawing;
using System.Windows.Forms;

namespace BunnyBlack.Core
{
    public static class ThemeHelper
    {
        public static void Apply(Control root)
        {
            if (root == null) return;

            switch (root)
            {
                case DataGridView grid:
                    grid.BackgroundColor = ThemeManager.Background;
                    grid.GridColor = ThemeManager.Border;
                    grid.BorderStyle = BorderStyle.None;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = ThemeManager.Header;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = ThemeManager.Foreground;
                    grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = ThemeManager.Header;
                    grid.DefaultCellStyle.BackColor = ThemeManager.Row;
                    grid.DefaultCellStyle.ForeColor = ThemeManager.Foreground;
                    grid.DefaultCellStyle.SelectionBackColor = ThemeManager.Selection;
                    grid.DefaultCellStyle.SelectionForeColor = ThemeManager.SelectionText;
                    grid.AlternatingRowsDefaultCellStyle.BackColor = ThemeManager.RowAlt;
                    grid.AlternatingRowsDefaultCellStyle.ForeColor = ThemeManager.Foreground;
                    break;

                case TextBox tb:
                    tb.BackColor = ThemeManager.Input;
                    tb.ForeColor = ThemeManager.Foreground;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case RichTextBox rtb:
                    rtb.BackColor = ThemeManager.Background;
                    rtb.ForeColor = ThemeManager.Foreground;
                    rtb.BorderStyle = BorderStyle.None;
                    break;

                case Button btn:
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.BackColor = ThemeManager.PanelAlt;
                    btn.ForeColor = ThemeManager.Foreground;
                    if (btn.FlatAppearance != null)
                        btn.FlatAppearance.BorderColor = ThemeManager.Border;
                    break;

                case Label lbl:
                    lbl.BackColor = Color.Transparent;
                    lbl.ForeColor = ThemeManager.Foreground;
                    break;

                case CheckBox cb:
                    cb.BackColor = Color.Transparent;
                    cb.ForeColor = ThemeManager.Foreground;
                    break;

                case RadioButton rb:
                    rb.BackColor = Color.Transparent;
                    rb.ForeColor = ThemeManager.Foreground;
                    break;

                case ComboBox combo:
                    combo.BackColor = ThemeManager.Input;
                    combo.ForeColor = ThemeManager.Foreground;
                    combo.FlatStyle = FlatStyle.Flat;
                    break;

                case TabControl tc:
                    tc.BackColor = ThemeManager.Background;
                    tc.ForeColor = ThemeManager.Foreground;
                    break;

                case TabPage tp:
                    tp.BackColor = ThemeManager.Background;
                    tp.ForeColor = ThemeManager.Foreground;
                    break;

                case GroupBox gb:
                    gb.BackColor = ThemeManager.GroupBack;
                    gb.ForeColor = ThemeManager.Foreground;
                    break;

                case Panel p:
                    // Баннер режима — свой цвет, не трогаем
                    if (p.Name == "modeBanner") break;
                    p.BackColor = ThemeManager.Background;
                    break;

                case ListView lv:
                    lv.BackColor = ThemeManager.Background;
                    lv.ForeColor = ThemeManager.Foreground;
                    break;

                case TreeView tv:
                    tv.BackColor = ThemeManager.Background;
                    tv.ForeColor = ThemeManager.Foreground;
                    tv.BorderStyle = BorderStyle.None;
                    break;

                case ProgressBar _:
                    break;

                case Form f:
                    f.BackColor = ThemeManager.Background;
                    f.ForeColor = ThemeManager.Foreground;
                    break;

                case UserControl uc:
                    uc.BackColor = ThemeManager.Background;
                    uc.ForeColor = ThemeManager.Foreground;
                    break;
            }

            foreach (Control child in root.Controls)
                Apply(child);
        }
    }
}