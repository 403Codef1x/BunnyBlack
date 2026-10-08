// language: C#, file: Core/ThemeManager.cs
// Управление темой: Dark / Light. Все цвета через свойства.
// Событие ThemeChanged для перекраски форм.
using System;
using System.Drawing;
using Microsoft.Win32;

namespace BunnyBlack.Core
{
    public enum AppTheme { Dark, Light }

    public static class ThemeManager
    {
        private static AppTheme _current = AppTheme.Dark;

        public static event Action ThemeChanged;

        public static AppTheme Current
        {
            get => _current;
            set
            {
                if (_current == value) return;
                _current = value;
                Save();
                ThemeChanged?.Invoke();
            }
        }

        public static bool IsDark => _current == AppTheme.Dark;

        // ============================================================
        // БАЗОВЫЕ ЦВЕТА
        // ============================================================
        public static Color Background => IsDark ? Color.FromArgb(13, 13, 13) : Color.FromArgb(245, 245, 245);
        public static Color Foreground => IsDark ? Color.FromArgb(216, 216, 216) : Color.FromArgb(30, 30, 30);
        public static Color Panel => IsDark ? Color.FromArgb(18, 18, 18) : Color.FromArgb(235, 235, 235);
        public static Color PanelAlt => IsDark ? Color.FromArgb(22, 22, 22) : Color.FromArgb(228, 228, 228);
        public static Color GroupBack => IsDark ? Color.FromArgb(22, 22, 22) : Color.FromArgb(232, 232, 232);
        public static Color Header => IsDark ? Color.FromArgb(25, 25, 25) : Color.FromArgb(220, 220, 220);
        public static Color Row => IsDark ? Color.FromArgb(18, 18, 18) : Color.FromArgb(250, 250, 250);
        public static Color RowAlt => IsDark ? Color.FromArgb(14, 14, 14) : Color.FromArgb(240, 240, 240);
        public static Color Border => IsDark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(200, 200, 200);
        public static Color BorderLight => IsDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(180, 180, 180);
        public static Color Input => IsDark ? Color.FromArgb(24, 24, 24) : Color.FromArgb(255, 255, 255);
        public static Color Selection => IsDark ? Color.FromArgb(40, 50, 60) : Color.FromArgb(180, 210, 240);
        public static Color SelectionText => IsDark ? Color.FromArgb(255, 255, 255) : Color.FromArgb(0, 0, 0);
        public static Color Muted => IsDark ? Color.FromArgb(120, 120, 120) : Color.FromArgb(130, 130, 130);

        // ============================================================
        // АКЦЕНТЫ
        // ============================================================
        public static Color Accent => Color.FromArgb(35, 55, 75);
        public static Color AccentText => Color.FromArgb(240, 240, 240);

        // ============================================================
        // СЕМАНТИЧЕСКИЕ — текст
        // ============================================================
        public static Color Success => Color.FromArgb(136, 221, 170);
        public static Color Warning => Color.FromArgb(255, 200, 120);
        public static Color Danger => Color.FromArgb(255, 150, 150);

        // ============================================================
        // СЕМАНТИЧЕСКИЕ — фон (для кнопок, groupbox, подсветки)
        // ============================================================
        public static Color SuccessBack => IsDark ? Color.FromArgb(15, 40, 25) : Color.FromArgb(210, 240, 220);
        public static Color WarningBack => IsDark ? Color.FromArgb(60, 40, 15) : Color.FromArgb(250, 235, 200);
        public static Color DangerBack => IsDark ? Color.FromArgb(50, 20, 20) : Color.FromArgb(250, 220, 220);

        // ============================================================
        // СОХРАНЕНИЕ / ЗАГРУЗКА
        // ============================================================
        public static void Save()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                    k?.SetValue("Theme", _current == AppTheme.Dark ? "dark" : "light");
            }
            catch { }
        }

        public static void Load()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                {
                    if (k == null) return;
                    string s = k.GetValue("Theme")?.ToString() ?? "dark";
                    _current = (s == "light") ? AppTheme.Light : AppTheme.Dark;
                }
            }
            catch { }
        }
    }
}