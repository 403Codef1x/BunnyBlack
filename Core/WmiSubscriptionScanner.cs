// language: C#, file: Core/WmiSubscriptionScanner.cs
// Сканер постоянных WMI-подписок: __EventFilter, __EventConsumer,
// __FilterToConsumerBinding. Это самый скрытный тип персистентности —
// обычный Autoruns его почти не видит.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace BunnyBlack.Core
{
    public class WmiSubscription
    {
        public string FilterName { get; set; }
        public string FilterQuery { get; set; }
        public string ConsumerName { get; set; }
        public string ConsumerType { get; set; }
        public string ConsumerCommand { get; set; }
        public string BindingName { get; set; }
        public bool Suspicious { get; set; }
        public string SuspicionReason { get; set; }
    }

    public static class WmiSubscriptionScanner
    {
        public static List<WmiSubscription> Scan()
        {
            var list = new List<WmiSubscription>();

            // 1. фильтры
            var filters = new Dictionary<string, (string query, string name)>();
            try
            {
                var q = new WqlObjectQuery("SELECT * FROM __EventFilter");
                using (var s = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\subscription"), q))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = mo["Name"]?.ToString() ?? "";
                        string query = mo["Query"]?.ToString() ?? "";
                        filters[name] = (query, name);
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[WMI filters] " + ex.Message); }

            // 2. консьюмеры
            var consumers = new Dictionary<string, WmiSubscription>();
            string[] consumerClasses = {
                "CommandLineEventConsumer",
                "ActiveScriptEventConsumer",
                "LogFileEventConsumer",
                "NTEventLogEventConsumer",
                "SMTPEventConsumer"
            };

            foreach (var cls in consumerClasses)
            {
                try
                {
                    var q = new WqlObjectQuery($"SELECT * FROM {cls}");
                    using (var s = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\subscription"), q))
                    {
                        foreach (ManagementObject mo in s.Get())
                        {
                            string name = mo["Name"]?.ToString() ?? "";
                            string cmd = "";
                            if (cls == "CommandLineEventConsumer")
                                cmd = mo["CommandLineTemplate"]?.ToString() ?? "";
                            else if (cls == "ActiveScriptEventConsumer")
                                cmd = mo["ScriptText"]?.ToString() ?? "";

                            consumers[name] = new WmiSubscription
                            {
                                ConsumerName = name,
                                ConsumerType = cls,
                                ConsumerCommand = cmd
                            };
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[WMI {cls}] {ex.Message}"); }
            }

            // 3. связки
            try
            {
                var q = new WqlObjectQuery("SELECT * FROM __FilterToConsumerBinding");
                using (var s = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\subscription"), q))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string filterPath = mo["Filter"]?.ToString() ?? "";
                        string consumerPath = mo["Consumer"]?.ToString() ?? "";

                        string filterName = ExtractName(filterPath);
                        string consumerName = ExtractName(consumerPath);

                        var ws = new WmiSubscription
                        {
                            BindingName = $"{filterName} -> {consumerName}",
                            FilterName = filterName,
                            FilterQuery = filters.TryGetValue(filterName, out var f) ? f.query : ""
                        };

                        if (consumers.TryGetValue(consumerName, out var c))
                        {
                            ws.ConsumerName = c.ConsumerName;
                            ws.ConsumerType = c.ConsumerType;
                            ws.ConsumerCommand = c.ConsumerCommand;
                        }

                        MarkSuspicious(ws);
                        list.Add(ws);
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[WMI bindings] " + ex.Message); }

            return list;
        }

        private static string ExtractName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int i = path.IndexOf("Name=");
            if (i < 0) return path;
            string n = path.Substring(i + 5).TrimEnd('"');
            return n.Trim('"');
        }

        private static void MarkSuspicious(WmiSubscription ws)
        {
            string all = (ws.FilterQuery + " " + ws.ConsumerCommand).ToLowerInvariant();

            // подозрительные триггеры
            if (all.Contains("win32_logonsession") || all.Contains("win32_processstarttrace"))
            {
                ws.Suspicious = true;
                ws.SuspicionReason = "Триггер на вход/запуск процесса";
                return;
            }

            // LOLBins
            string[] lolbins = { "powershell", "cmd.exe", "wscript", "cscript", "mshta", "rundll32", "regsvr32" };
            foreach (var b in lolbins)
            {
                if (all.Contains(b))
                {
                    ws.Suspicious = true;
                    ws.SuspicionReason = "Команда через " + b;
                    return;
                }
            }

            // base64
            if (all.Contains("-enc") || all.Contains("-encodedcommand"))
            {
                ws.Suspicious = true;
                ws.SuspicionReason = "Base64-payload";
                return;
            }
        }

        // ============================================================
        // УДАЛЕНИЕ ПОДПИСКИ
        // ============================================================
        public static bool RemoveSubscription(WmiSubscription ws, out string error)
        {
            error = null;
            try
            {
                var scope = new ManagementScope(@"\\.\root\subscription");

                // удалить binding
                var qb = new WqlObjectQuery("SELECT * FROM __FilterToConsumerBinding");
                using (var s = new ManagementObjectSearcher(scope, qb))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string fp = mo["Filter"]?.ToString() ?? "";
                        string cp = mo["Consumer"]?.ToString() ?? "";
                        if (ExtractName(fp) == ws.FilterName && ExtractName(cp) == ws.ConsumerName)
                            mo.Delete();
                    }

                // удалить filter
                var qf = new WqlObjectQuery($"SELECT * FROM __EventFilter WHERE Name='{ws.FilterName}'");
                using (var s = new ManagementObjectSearcher(scope, qf))
                    foreach (ManagementObject mo in s.Get()) mo.Delete();

                // удалить consumer
                if (!string.IsNullOrEmpty(ws.ConsumerType))
                {
                    var qc = new WqlObjectQuery($"SELECT * FROM {ws.ConsumerType} WHERE Name='{ws.ConsumerName}'");
                    using (var s = new ManagementObjectSearcher(scope, qc))
                        foreach (ManagementObject mo in s.Get()) mo.Delete();
                }

                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
    }
}