// language: C#, file: Core/BbLog.cs
// Логирование в файл вместо Debug.WriteLine.
// Пишет в %APPDATA%\BunnyBlack\logs\debug.log с ротацией по 2 МБ.
using System;
using System.Diagnostics;
using System.IO;

namespace BunnyBlack.Core
{
    public static class BbLog
    {
        private static readonly object _lock = new object();
        private static readonly string LogDir;
        private static readonly string LogFile;
        private const long MaxBytes = 2 * 1024 * 1024;   // 2 МБ

        static BbLog()
        {
            try
            {
                LogDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BunnyBlack", "logs");
                Directory.CreateDirectory(LogDir);
                LogFile = Path.Combine(LogDir, "debug.log");
            }
            catch { }
        }

        public static void Info(string msg) => Write("INFO", msg, null);
        public static void Warn(string msg) => Write("WARN", msg, null);
        public static void Error(string msg) => Write("ERR ", msg, null);
        public static void Error(string msg, Exception ex) => Write("ERR ", msg, ex);

        private static void Write(string level, string msg, Exception ex)
        {
            lock (_lock)
            {
                try
                {
                    // ротация
                    if (File.Exists(LogFile))
                    {
                        var fi = new FileInfo(LogFile);
                        if (fi.Length > MaxBytes)
                        {
                            string old = LogFile + ".old";
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(LogFile, old);
                        }
                    }

                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}";
                    if (ex != null) line += Environment.NewLine + "  " + ex;

                    File.AppendAllText(LogFile, line + Environment.NewLine);

                    // дублируем в Debug Output для отладки
                    Debug.WriteLine(line);
                }
                catch { }
            }
        }

        public static string GetLogPath() => LogFile;
        public static string GetLogDir() => LogDir;
    }
}