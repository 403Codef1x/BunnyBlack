using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace BunnyBlack.Core
{
    public class ServiceInfo
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Status { get; set; }
        public string StartType { get; set; }
        public string Path { get; set; }
    }

    public class ServiceHelper
    {
        public static List<ServiceInfo> GetServices()
        {
            var services = new List<ServiceInfo>();
            try
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                {
                    if (key != null)
                    {
                        foreach (var name in key.GetSubKeyNames())
                        {
                            try
                            {
                                using (var sub = key.OpenSubKey(name))
                                {
                                    if (sub != null)
                                    {
                                        var start = sub.GetValue("Start") as int?;
                                        var display = sub.GetValue("DisplayName")?.ToString() ?? "";
                                        var path = sub.GetValue("ImagePath")?.ToString() ?? "";

                                        string startType = "Неизвестно";
                                        if (start.HasValue)
                                        {
                                            if (start.Value == 0) startType = "Загрузочная";
                                            else if (start.Value == 1) startType = "Системная";
                                            else if (start.Value == 2) startType = "Авто";
                                            else if (start.Value == 3) startType = "Вручную";
                                            else if (start.Value == 4) startType = "Отключена";
                                        }

                                        var skip = new[] { "Microsoft", "Windows", ".NET", "Framework", "Defender" };
                                        var combined = (name + display).ToLower();
                                        bool shouldSkip = false;
                                        foreach (var s in skip)
                                        {
                                            if (combined.Contains(s.ToLower()))
                                            {
                                                shouldSkip = true;
                                                break;
                                            }
                                        }

                                        if (!shouldSkip)
                                        {
                                            services.Add(new ServiceInfo
                                            {
                                                Name = name,
                                                DisplayName = string.IsNullOrEmpty(display) ? name : display,
                                                Status = "Активна",
                                                StartType = startType,
                                                Path = path
                                            });
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            return services;
        }

        public static bool DisableService(string name)
        {
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"config \"{name}\" start= disabled",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(si))
                {
                    if (p != null) p.WaitForExit();
                }

                si.Arguments = $"stop \"{name}\"";
                using (var p = Process.Start(si))
                {
                    if (p != null) p.WaitForExit();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool EnableService(string name)
        {
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"config \"{name}\" start= auto",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(si))
                {
                    if (p != null) p.WaitForExit();
                }

                si.Arguments = $"start \"{name}\"";
                using (var p = Process.Start(si))
                {
                    if (p != null) p.WaitForExit();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}