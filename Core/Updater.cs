// language: C#, file: Core/Updater.cs
// Только проверка обновлений через GitHub Releases API.
// Без скачивания, без установки — просто узнаём, есть ли новая версия.
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BunnyBlack.Core
{
    public class UpdateInfo
    {
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseNotes { get; set; }
        public string ReleasePageUrl { get; set; }
        public string AssetName { get; set; }
        public string DownloadUrl { get; set; }
        public long DownloadSize { get; set; }
        public string Error { get; set; }
    }

    public static class Updater
    {
        private const string GithubOwner = "Gamer31YT";
        private const string GithubRepo = "BunnyBlack-main";

        public static string LatestReleaseUrl =>
            $"https://github.com/{GithubOwner}/{GithubRepo}/releases/latest";

        public static string CurrentVersion
        {
            get
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var ver = asm.GetName().Version;
                    if (ver != null) return $"{ver.Major}.{ver.Minor}.{ver.Build}";
                }
                catch { }
                return "1.0.0";
            }
        }

        // ============================================================
        // ПРОВЕРКА
        // ============================================================
        public static async Task<UpdateInfo> CheckAsync()
        {
            var info = new UpdateInfo
            {
                CurrentVersion = CurrentVersion,
                HasUpdate = false
            };

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.UserAgent.Add(
                        new ProductInfoHeaderValue("BunnyBlack", CurrentVersion));
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                    string url = $"https://api.github.com/repos/{GithubOwner}/{GithubRepo}/releases/latest";

                    using (var resp = await client.GetAsync(url))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            info.Error = $"GitHub вернул HTTP {(int)resp.StatusCode}. Проверь, есть ли опубликованные релизы.";
                            return info;
                        }

                        string json = await resp.Content.ReadAsStringAsync();
                        var root = JObject.Parse(json);

                        string tag = root["tag_name"]?.ToString() ?? "";
                        string latest = tag.TrimStart('v', 'V');
                        info.LatestVersion = latest;
                        info.ReleaseNotes = root["body"]?.ToString() ?? "";
                        info.ReleasePageUrl = root["html_url"]?.ToString() ?? LatestReleaseUrl;

                        if (CompareVersions(latest, CurrentVersion) > 0)
                            info.HasUpdate = true;

                        // ассет — только для информации (размер)
                        var assets = root["assets"] as JArray;
                        if (assets != null)
                        {
                            foreach (var a in assets)
                            {
                                string name = a["name"]?.ToString() ?? "";
                                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    info.AssetName = name;
                                    info.DownloadUrl = a["browser_download_url"]?.ToString() ?? "";
                                    if (long.TryParse(a["size"]?.ToString(), out long sz))
                                        info.DownloadSize = sz;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                info.Error = "Ошибка проверки: " + ex.Message;
            }

            return info;
        }

        private static int CompareVersions(string a, string b)
        {
            try
            {
                var pa = a.Split('.');
                var pb = b.Split('.');
                int len = Math.Max(pa.Length, pb.Length);
                for (int i = 0; i < len; i++)
                {
                    int va = i < pa.Length && int.TryParse(pa[i], out int x) ? x : 0;
                    int vb = i < pb.Length && int.TryParse(pb[i], out int y) ? y : 0;
                    if (va != vb) return va.CompareTo(vb);
                }
                return 0;
            }
            catch { return 0; }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "—";
            string[] units = { "Б", "КБ", "МБ", "ГБ" };
            double len = bytes;
            int i = 0;
            while (len >= 1024 && i < units.Length - 1) { i++; len /= 1024; }
            return $"{len:0.##} {units[i]}";
        }
    }
}