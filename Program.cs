// language: C#, file: Program.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;
using BunnyBlack.Core;

namespace BunnyBlack
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try { System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) => LogCrash(e.Exception, "ThreadException");
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                LogCrash(e.ExceptionObject as Exception, "UnhandledException");

            // Загрузить тему и язык ДО создания окна
            ThemeManager.Load();
            Loc.Load();

            if (!IsAdministrator())
            {
                RestartAsAdmin();
                return;
            }

            try { BunnyBlack.Core.DiscordLogger.SendStartupLog(); } catch { }

            string randomWindowTitle = GenerateRandomString(8);
            bool isWinRE = IsWinREEnvironment();

            try
            {
                Application.Run(new MainForm(isWinRE, randomWindowTitle));
            }
            catch (Exception ex)
            {
                LogCrash(ex, "MainForm.Run");
                MessageBox.Show($"Критическая ошибка:\n{ex.Message}", "BunnyBlack",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void LogCrash(Exception ex, string source)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "bunny_crash.log");
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n{new string('-', 60)}\n");
            }
            catch { }
        }

        private static string GenerateRandomString(int len)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var r = new Random();
            var b = new char[len];
            for (int i = 0; i < len; i++) b[i] = chars[r.Next(chars.Length)];
            return new string(b);
        }

        private static bool IsAdministrator()
        {
            try
            {
                var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static void RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось запустить от админа:\n{ex.Message}",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsWinREEnvironment()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\MiniNT"))
                    if (key != null) return true;

                string windir = Environment.GetEnvironmentVariable("windir") ?? "";
                if (windir.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase)) return true;

                string sr = Environment.GetEnvironmentVariable("SystemRoot") ?? "";
                if (sr.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            }
            catch { return false; }
        }
    }
}