// language: C#, file: Program.cs
// Полная замена.
// п.1  — WinRE-детект расширен: MiniNT + windir X:\ + SystemRoot X:\
// п.18 — обёртка Application.Run в try/catch, крэш-лог в %TEMP%\bunny_crash.log
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

namespace BunnyBlack
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Глобальный перехватчик — чтобы не тихо умирало
            Application.ThreadException += (s, e) => LogCrash(e.Exception, "ThreadException");
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                LogCrash(e.ExceptionObject as Exception, "UnhandledException");

            if (!IsAdministrator())
            {
                RestartAsAdmin();
                return;
            }

            // ============================================================
            // ОТПРАВКА ЛОГА В DISCORD
            // ============================================================
            try
            {
                BunnyBlack.Core.DiscordLogger.SendStartupLog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DiscordLogger] {ex.Message}");
            }

            string randomWindowTitle = GenerateRandomString(8);
            bool isWinRE = IsWinREEnvironment();

            try
            {
                Application.Run(new MainForm(isWinRE, randomWindowTitle));
            }
            catch (Exception ex)
            {
                LogCrash(ex, "MainForm.Run");
                MessageBox.Show(
                    $"Критическая ошибка при запуске:\n{ex.Message}\n\nЛог: %TEMP%\\bunny_crash.log",
                    "BunnyBlack", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // п.18 — крэш-лог
        // ============================================================
        private static void LogCrash(Exception ex, string source)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "bunny_crash.log");
                string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]{Environment.NewLine}" +
                              $"{ex?.ToString() ?? "(null exception)"}{Environment.NewLine}" +
                              new string('-', 60) + Environment.NewLine;
                File.AppendAllText(path, text);
            }
            catch { }
        }

        private static string GenerateRandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            char[] stringChars = new char[length];
            for (int i = 0; i < stringChars.Length; i++)
                stringChars[i] = chars[random.Next(chars.Length)];
            return new string(stringChars);
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static void RestartAsAdmin()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось запустить программу с правами администратора:\n{ex.Message}",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // п.1 — расширенный WinRE-детект
        // ============================================================
        private static bool IsWinREEnvironment()
        {
            // 1. MiniNT-ключ
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\MiniNT"))
                {
                    if (key != null) return true;
                }
            }
            catch { }

            // 2. windir == X:\
            try
            {
                string windir = Environment.GetEnvironmentVariable("windir") ?? "";
                if (windir.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }

            // 3. SystemRoot == X:\
            try
            {
                string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "";
                if (systemRoot.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }

            // 4. Специфичные WinRE-процессы
            try
            {
                string peImg = Environment.GetEnvironmentVariable("PE_IMAGE") ?? "";
                if (!string.IsNullOrEmpty(peImg)) return true;
            }
            catch { }

            return false;
        }
    }
}