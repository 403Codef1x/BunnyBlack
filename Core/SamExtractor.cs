// language: C#, file: Core/SamExtractor.cs
// Извлечение хэшей LM/NT из оффлайн SAM. Работает через уже загруженные
// оффлайн-кусты (RegistryHelper.LoadOfflineHives должен быть вызван до).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace BunnyBlack.Core
{
    public static class SamExtractor
    {
        // Класс перестановки boot key
        private static readonly int[] Permutation = { 8, 5, 4, 2, 11, 9, 13, 3, 0, 6, 1, 12, 14, 10, 15, 7 };

        public static string DumpHashes()
        {
            try
            {
                byte[] bootKey = GetBootKeyFromSystem();
                if (bootKey == null) return "Failed to obtain BootKey.\n" +
                    "Убедись, что оффлайн-кусты SYSTEM загружены (RegistryHelper.IsWinReEnvironment + LoadOfflineHives).";

                var sb = new StringBuilder();
                sb.AppendLine("======== SAM Hash Dump ========");
                sb.AppendLine($"BootKey: {BitConverter.ToString(bootKey).Replace("-", "").ToLowerInvariant()}");
                sb.AppendLine();

                var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using (var sam = hklm.OpenSubKey(@"BunnyBlack_Offline_SAM\SAM\Domains\Account\Users"))
                {
                    if (sam == null) return "No SAM hive loaded (BunnyBlack_Offline_SAM).";

                    foreach (string ridHex in sam.GetSubKeyNames())
                    {
                        if (ridHex.Length < 8) continue;
                        try
                        {
                            using (var userKey = sam.OpenSubKey(ridHex))
                            {
                                if (userKey == null) continue;
                                byte[] v = userKey.GetValue("V") as byte[];
                                byte[] f = userKey.GetValue("F") as byte[];
                                if (v == null) continue;

                                string name = ExtractNameFromF(f) ?? ridHex;
                                byte[] hash = ExtractNtHash(v, ridHex);
                                if (hash == null) continue;

                                string lm = hash.Length >= 16
                                    ? BitConverter.ToString(hash, 0, 16).Replace("-", "").ToLowerInvariant()
                                    : new string('0', 32);
                                string nt = hash.Length >= 32
                                    ? BitConverter.ToString(hash, 16, 16).Replace("-", "").ToLowerInvariant()
                                    : new string('0', 32);

                                sb.AppendLine($"{name}:{ridHex.Substring(ridHex.Length - 4)}:{lm}:{nt}:::");
                            }
                        }
                        catch (Exception ex) { sb.AppendLine($"[!] {ridHex}: {ex.Message}"); }
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex) { return "SamExtractor error: " + ex.Message; }
        }

        private static string ExtractNameFromF(byte[] f)
        {
            if (f == null) return null;
            try
            {
                int off = 0x0C, len = 0x14 * 2;
                if (f.Length < off + len) return null;
                string s = Encoding.Unicode.GetString(f, off, len).TrimEnd('\0');
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            catch { return null; }
        }

        private static byte[] GetBootKeyFromSystem()
        {
            try
            {
                var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                var sys = hklm.OpenSubKey(@"BunnyBlack_Offline_SYSTEM\ControlSet001\Control\Lsa")
                       ?? hklm.OpenSubKey(@"BunnyBlack_Offline_SYSTEM\CurrentControlSet\Control\Lsa")
                       ?? hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
                if (sys == null) return null;

                byte[] jd = sys.GetValue("JD") as byte[];
                byte[] sk = sys.GetValue("Skew1") as byte[];
                byte[] gb = sys.GetValue("GBG") as byte[];
                byte[] dt = sys.GetValue("Data") as byte[];
                if (jd == null || sk == null || gb == null || dt == null) return null;

                var combined = new List<byte>();
                combined.AddRange(jd); combined.AddRange(sk);
                combined.AddRange(gb); combined.AddRange(dt);
                if (combined.Count != 32) return null;

                string hex = Encoding.ASCII.GetString(combined.ToArray());
                if (hex.Length != 32) return null;

                byte[] scrambled = new byte[16];
                for (int i = 0; i < 16; i++)
                    scrambled[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

                var bootKey = new byte[16];
                for (int i = 0; i < 16; i++)
                    bootKey[i] = scrambled[Permutation[i]];
                return bootKey;
            }
            catch (Exception ex) { Debug.WriteLine($"[BootKey] {ex.Message}"); return null; }
        }

        // Упрощённое извлечение — для полного дампа с AES разблокировкой
        // нужна отдельная реализация. Здесь возвращаем сырой blob по offset.
        private static byte[] ExtractNtHash(byte[] v, string ridHex)
        {
            try
            {
                // offset хэша в структуре V — 0x9C, длина LM (16) + NT (16)
                int off = 0x9C;
                if (v.Length < off + 32) return null;
                byte[] hash = new byte[32];
                Buffer.BlockCopy(v, off, hash, 0, 32);
                return hash;
            }
            catch { return null; }
        }
    }
}