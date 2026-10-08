// language: C#, file: Core/TelegramNotifier.cs
// Полная замена.
// Токен и chat_id в открытом виде.
// Отправка через api.telegram.org, с fallback с Markdown на plain.
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BunnyBlack.Core
{
    public static class TelegramNotifier
    {
        // ============================================================
        // НАСТРОЙКА
        // ============================================================
        private const string BotToken = "8832885464:AAHdP2lAlZ-kqQoKgg7DDGHdphz9aKnsiI0";
        private const string ChatId = "-5389684165";

        private const int HttpTimeoutSec = 10;

        public static bool IsConfigured()
        {
            return !string.IsNullOrEmpty(BotToken) && !string.IsNullOrEmpty(ChatId);
        }

        // ============================================================
        // ОТПРАВКА
        // ============================================================
        public static async Task<bool> SendAsync(string message)
        {
            if (!IsConfigured()) return false;
            if (string.IsNullOrEmpty(message)) return false;

            // Telegram режет сообщения по 4096 символов. Дробим.
            const int maxLen = 4000;
            bool ok = true;

            int pos = 0;
            while (pos < message.Length)
            {
                int len = Math.Min(maxLen, message.Length - pos);
                if (pos + len < message.Length)
                {
                    int lastNl = message.LastIndexOf('\n', pos + len - 1, len);
                    if (lastNl > pos) len = lastNl - pos + 1;
                }

                string chunk = message.Substring(pos, len);
                pos += len;

                // пробуем с Markdown
                if (!await SendChunkAsync(chunk, useMarkdown: true))
                {
                    // fallback — plain
                    if (!await SendChunkAsync(chunk, useMarkdown: false))
                        ok = false;
                }

                await Task.Delay(300);
            }
            return ok;
        }

        private static async Task<bool> SendChunkAsync(string text, bool useMarkdown)
        {
            try
            {
                string url = $"https://api.telegram.org/bot{BotToken}/sendMessage";

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSec);

                    var payload = new
                    {
                        chat_id = ChatId,
                        text = text,
                        parse_mode = useMarkdown ? "Markdown" : null,
                        disable_web_page_preview = true
                    };

                    string json = JsonConvert.SerializeObject(payload,
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                    using (var body = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        var resp = await client.PostAsync(url, body);
                        string respText = "";
                        try { respText = await resp.Content.ReadAsStringAsync(); } catch { }

                        if (resp.IsSuccessStatusCode) return true;

                        BbLog.Warn($"[TelegramNotifier] HTTP {(int)resp.StatusCode}: {respText}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                BbLog.Error("[TelegramNotifier.SendChunkAsync]", ex);
                return false;
            }
        }

        public static void SendSync(string message)
        {
            try { SendAsync(message).GetAwaiter().GetResult(); }
            catch (Exception ex) { BbLog.Error("[TelegramNotifier.SendSync]", ex); }
        }

        // ============================================================
        // ТЕСТ
        // ============================================================
        public static void TestSend()
        {
            try
            {
                bool ok = SendAsync("**BunnyBlack** — тестовое сообщение ✓").GetAwaiter().GetResult();
                System.Windows.Forms.MessageBox.Show(
                    ok ? "Отправлено успешно!" : "Не удалось отправить. Смотри debug.log",
                    "Telegram Test");
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Ошибка: " + ex.Message, "Telegram Test");
            }
        }
    }
}