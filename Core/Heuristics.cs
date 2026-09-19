// language: C#, file: Core/Heuristics.cs
// 5+ эвристик подозрительности: свежесть, путь, отсутствие подписи,
// левенштейн-похожесть на системное имя, малый размер, необычная директория.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace BunnyBlack.Core
{
    public class HeuristicResult
    {
        public bool Suspicious { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
        public string Summary => string.Join("; ", Reasons);
    }

    public static class Heuristics
    {
        [DllImport("wintrust.dll", PreserveSig = true, SetLastError = false)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        // ============================================================
        // ПРОВЕРКА ПОДПИСИ
        // ============================================================
        public static bool IsSigned(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                var fileInfo = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                    pcwszFilePath = filePath,
                };
                IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
                Marshal.StructureToPtr(fileInfo, pFile, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = 2,          // WTD_UI_NONE
                    fdwRevocationChecks = 0,
                    dwUnionChoice = 1,        // WTD_CHOICE_FILE
                    pFile = pFile,
                    dwStateAction = 0,
                    dwProvFlags = 0x1000,     // WTD_CACHE_ONLY_URL_RETRIEVAL
                };
                IntPtr pData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
                Marshal.StructureToPtr(data, pData, false);

                var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                uint result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

                Marshal.FreeHGlobal(pFile);
                Marshal.FreeHGlobal(pData);

                return result == 0;
            }
            catch { return false; }
        }

        // ============================================================
        // ЭВРИСТИКА ФАЙЛА
        // ============================================================
        public static HeuristicResult CheckFile(string filePath)
        {
            var r = new HeuristicResult();
            if (string.IsNullOrEmpty(filePath)) return r;

            try
            {
                if (!File.Exists(filePath))
                {
                    r.Suspicious = true;
                    r.Reasons.Add("Файл не существует");
                    return r;
                }

                var fi = new FileInfo(filePath);
                string lower = filePath.ToLowerInvariant();

                // 1. свежесть
                double ageDays = (DateTime.Now - fi.CreationTime).TotalDays;
                if (ageDays < 7 && !lower.Contains(@"\windows\"))
                {
                    r.Reasons.Add($"Файл создан {ageDays:F1} дн. назад");
                    r.Suspicious = true;
                }

                // 2. путь
                string[] badPaths = {
                    @"\appdata\local\temp\", @"\windows\temp\", @"\temp\",
                    @"\downloads\", @"\programdata\", @"\users\public\"
                };
                foreach (var p in badPaths)
                {
                    if (lower.Contains(p))
                    {
                        r.Reasons.Add("Путь в " + p.Trim('\\'));
                        r.Suspicious = true;
                        break;
                    }
                }

                // 3. подпись
                if (!IsSigned(filePath))
                {
                    r.Reasons.Add("Нет цифровой подписи");
                    r.Suspicious = true;
                }

                // 4. малый размер
                if (fi.Length < 8192 && lower.EndsWith(".exe"))
                {
                    r.Reasons.Add("Очень маленький .exe");
                    r.Suspicious = true;
                }

                // 5. LOLBin-совпадение в имени
                string name = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
                string[] lolbins = { "svchost", "lsass", "csrss", "winlogon", "explorer", "services", "taskhost", "dllhost" };
                foreach (var l in lolbins)
                {
                    if (name == l && !lower.Contains(@"\windows\system32"))
                    {
                        r.Reasons.Add($"Имитация системного имени: {l}");
                        r.Suspicious = true;
                        break;
                    }
                    int dist = Levenshtein(name, l);
                    if (dist > 0 && dist <= 2 && name.Length >= 5)
                    {
                        r.Reasons.Add($"Похоже на '{l}' (Лев. {dist})");
                        r.Suspicious = true;
                    }
                }

                // 6. hash — не считаем здесь, это делает HashScanner
            }
            catch (Exception ex) { Debug.WriteLine("[CheckFile] " + ex.Message); }

            return r;
        }

        // ============================================================
        // ЭВРИСТИКА ПРОЦЕССА
        // ============================================================
        public static HeuristicResult CheckProcess(int pid, string name, string path)
        {
            var r = new HeuristicResult();

            try
            {
                // файловые эвристики
                if (!string.IsNullOrEmpty(path))
                {
                    var fr = CheckFile(path);
                    if (fr.Suspicious)
                    {
                        r.Suspicious = true;
                        r.Reasons.AddRange(fr.Reasons);
                    }
                }

                // имя без пути
                if (string.IsNullOrEmpty(path))
                {
                    r.Reasons.Add("Нет доступа к пути");
                    r.Suspicious = true;
                }
            }
            catch { }

            return r;
        }

        // ============================================================
        // SHA256
        // ============================================================
        public static string Sha256(string filePath)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(filePath))
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            }
            catch { return null; }
        }

        // ============================================================
        // ЛЕВЕНШТЕЙН
        // ============================================================
        public static int Levenshtein(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;

            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            return d[a.Length, b.Length];
        }
    }
}