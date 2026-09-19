// language: C#, file: Core/Quarantine.cs
// Карантин: AES-256-CBC + HMAC-SHA256, манифест JSON.
// Файлы не удаляются, а переносятся в защищённое хранилище с метаданными.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace BunnyBlack.Core
{
    public class QuarantineEntry
    {
        public string Id { get; set; }              // GUID
        public string OriginalPath { get; set; }
        public string FileName { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
        public DateTime QuarantinedAt { get; set; }
        public string Reason { get; set; }
        public string StoredName { get; set; }      // имя внутри карантина
    }

    public class QuarantineManifest
    {
        public List<QuarantineEntry> Entries { get; set; } = new List<QuarantineEntry>();
    }

    public static class Quarantine
    {
        private static readonly string BaseDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "BunnyBlack", "quarantine");

        private static readonly string FilesDir = Path.Combine(BaseDir, "files");
        private static readonly string ManifestPath = Path.Combine(BaseDir, "manifest.json");

        // ключ выводится из MachineGuid + имени пользователя, чтобы карантин нельзя
        // было просто так прочитать на другой машине
        private static byte[] DeriveKey()
        {
            string seed = "";
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography"))
                    seed = k?.GetValue("MachineGuid")?.ToString() ?? "";
            }
            catch { }
            seed += "|" + Environment.MachineName + "|" + Environment.UserName + "|BB_Q1";
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
        }

        public static void EnsureDirs()
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                Directory.CreateDirectory(FilesDir);
            }
            catch { }
        }

        // ============================================================
        // ОТПРАВИТЬ В КАРАНТИН
        // ============================================================
        public static bool Add(string sourcePath, string reason, out string error)
        {
            error = null;
            EnsureDirs();

            try
            {
                if (!File.Exists(sourcePath)) { error = "Файл не найден."; return false; }

                var fi = new FileInfo(sourcePath);
                if (fi.Length > 2L * 1024 * 1024 * 1024) { error = "Файл > 2 ГБ, пропущен."; return false; }

                string id = Guid.NewGuid().ToString("N");
                string storedName = id + ".qtn";
                string dst = Path.Combine(FilesDir, storedName);

                string hash = Sha256(sourcePath);
                byte[] key = DeriveKey();

                byte[] iv = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(iv);

                byte[] plain = File.ReadAllBytes(sourcePath);

                byte[] cipher;
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (var enc = aes.CreateEncryptor())
                        cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                }

                byte[] hmac;
                using (var hm = new HMACSHA256(key))
                {
                    byte[] data = new byte[iv.Length + cipher.Length];
                    Buffer.BlockCopy(iv, 0, data, 0, iv.Length);
                    Buffer.BlockCopy(cipher, 0, data, iv.Length, cipher.Length);
                    hmac = hm.ComputeHash(data);
                }

                using (var fs = new FileStream(dst, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(iv, 0, iv.Length);
                    fs.Write(hmac, 0, hmac.Length);
                    fs.Write(cipher, 0, cipher.Length);
                }

                var manifest = LoadManifest();
                manifest.Entries.Add(new QuarantineEntry
                {
                    Id = id,
                    OriginalPath = sourcePath,
                    FileName = fi.Name,
                    Size = fi.Length,
                    Sha256 = hash,
                    QuarantinedAt = DateTime.Now,
                    Reason = reason,
                    StoredName = storedName
                });
                SaveManifest(manifest);

                // удаляем оригинал только после успешной записи в карантин
                try { File.Delete(sourcePath); }
                catch (Exception ex) { error = "Оригинал не удалён: " + ex.Message; }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ============================================================
        // ВОССТАНОВИТЬ ИЗ КАРАНТИНА
        // ============================================================
        public static bool Restore(QuarantineEntry entry, string targetPath, out string error)
        {
            error = null;
            try
            {
                string src = Path.Combine(FilesDir, entry.StoredName);
                if (!File.Exists(src)) { error = "Файл карантина не найден."; return false; }

                byte[] all = File.ReadAllBytes(src);
                if (all.Length < 16 + 32) { error = "Повреждённый файл карантина."; return false; }

                byte[] iv = new byte[16];
                byte[] hmac = new byte[32];
                byte[] cipher = new byte[all.Length - 48];
                Buffer.BlockCopy(all, 0, iv, 0, 16);
                Buffer.BlockCopy(all, 16, hmac, 0, 32);
                Buffer.BlockCopy(all, 48, cipher, 0, cipher.Length);

                byte[] key = DeriveKey();

                using (var hm = new HMACSHA256(key))
                {
                    byte[] data = new byte[iv.Length + cipher.Length];
                    Buffer.BlockCopy(iv, 0, data, 0, iv.Length);
                    Buffer.BlockCopy(cipher, 0, data, iv.Length, cipher.Length);
                    byte[] computed = hm.ComputeHash(data);
                    if (!ConstTimeEq(hmac, computed)) { error = "HMAC не совпадает (повреждён или подменён)."; return false; }
                }

                byte[] plain;
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (var dec = aes.CreateDecryptor())
                        plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                }

                string outPath = string.IsNullOrEmpty(targetPath) ? entry.OriginalPath : targetPath;
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, plain);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Remove(QuarantineEntry entry, out string error)
        {
            error = null;
            try
            {
                string src = Path.Combine(FilesDir, entry.StoredName);
                if (File.Exists(src)) File.Delete(src);

                var manifest = LoadManifest();
                manifest.Entries.RemoveAll(e => e.Id == entry.Id);
                SaveManifest(manifest);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ============================================================
        // СПИСОК / ОЧИСТКА
        // ============================================================
        public static List<QuarantineEntry> List()
        {
            return LoadManifest().Entries;
        }

        public static void WipeAll()
        {
            try
            {
                if (Directory.Exists(FilesDir))
                    Directory.Delete(FilesDir, true);
                if (File.Exists(ManifestPath))
                    File.Delete(ManifestPath);
            }
            catch { }
        }

        public static long TotalSize()
        {
            long total = 0;
            foreach (var e in List()) total += e.Size;
            return total;
        }

        // ============================================================
        // ВНУТРЕННИЕ
        // ============================================================
        private static QuarantineManifest LoadManifest()
        {
            try
            {
                if (!File.Exists(ManifestPath)) return new QuarantineManifest();
                string json = File.ReadAllText(ManifestPath);
                return JsonConvert.DeserializeObject<QuarantineManifest>(json) ?? new QuarantineManifest();
            }
            catch { return new QuarantineManifest(); }
        }

        private static void SaveManifest(QuarantineManifest manifest)
        {
            try
            {
                string json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                File.WriteAllText(ManifestPath, json);
            }
            catch (Exception ex) { Debug.WriteLine("[SaveManifest] " + ex.Message); }
        }

        private static string Sha256(string filePath)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(filePath))
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            }
            catch { return ""; }
        }

        private static bool ConstTimeEq(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}