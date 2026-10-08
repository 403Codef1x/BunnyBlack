// language: C#, file: Core/BootHelper.cs
// Работа с загрузчиком через bootrec.
// Backup/restore MBR и BCD удалены.
using System;
using System.Diagnostics;

namespace BunnyBlack.Core
{
    public static class BootHelper
    {
        // ============================================================
        // BOOTREC — восстановление загрузчика
        // ============================================================
        public static string RunBootrec(string args)
        {
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = "bootrec.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var p = Process.Start(si))
                {
                    if (p == null) return "bootrec.exe не запустился";

                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(60000);   // bootrec иногда долгий

                    BbLog.Info($"[bootrec {args}] exit={p.ExitCode}");
                    return string.IsNullOrWhiteSpace(output) ? "(вывода нет)" : output.Trim();
                }
            }
            catch (Exception ex)
            {
                BbLog.Error($"[bootrec {args}]", ex);
                return "Ошибка: " + ex.Message;
            }
        }

        public static string FixMbr() => RunBootrec("/fixmbr");
        public static string FixBoot() => RunBootrec("/fixboot");
        public static string RebuildBcd() => RunBootrec("/rebuildbcd");
    }
}