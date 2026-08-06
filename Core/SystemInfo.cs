using System;
using System.Collections.Generic;
using System.Management;
using System.Security.Principal;

namespace BunnyBlack.Core
{
    public class SystemInfo
    {
        public static Dictionary<string, string> GetAll()
        {
            var info = new Dictionary<string, string>();

            try
            {
                info["OS"] = Environment.OSVersion.VersionString;
                info["OSVersion"] = Environment.OSVersion.Version.ToString();
                info["MachineName"] = Environment.MachineName;
                info["UserName"] = Environment.UserName;
                info["ProcessorCount"] = Environment.ProcessorCount.ToString();
                info["Is64Bit"] = Environment.Is64BitOperatingSystem ? "Да" : "Нет";
                info["IsAdmin"] = IsAdmin() ? "Да" : "Нет";

                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            info["OSName"] = obj["Caption"]?.ToString() ?? "N/A";
                            info["OSBuild"] = obj["BuildNumber"]?.ToString() ?? "N/A";
                            info["InstallDate"] = obj["InstallDate"]?.ToString() ?? "N/A";
                            info["LastBootUpTime"] = obj["LastBootUpTime"]?.ToString() ?? "N/A";
                            if (obj["TotalVisibleMemorySize"] != null)
                            {
                                info["TotalMemory"] = (Convert.ToUInt64(obj["TotalVisibleMemorySize"]) / 1024 / 1024).ToString() + " GB";
                            }
                            if (obj["FreePhysicalMemory"] != null)
                            {
                                info["FreeMemory"] = (Convert.ToUInt64(obj["FreePhysicalMemory"]) / 1024 / 1024).ToString() + " GB";
                            }
                            break;
                        }
                    }
                }
                catch { }

                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            info["CPUName"] = obj["Name"]?.ToString() ?? "N/A";
                            info["CPUCores"] = obj["NumberOfCores"]?.ToString() ?? "N/A";
                            info["CPULogical"] = obj["NumberOfLogicalProcessors"]?.ToString() ?? "N/A";
                            break;
                        }
                    }
                }
                catch { }

                try
                {
                    var disks = new List<string>();
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk WHERE DriveType=3"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            var drive = obj["DeviceID"]?.ToString() ?? "N/A";
                            ulong size = 0, free = 0;
                            if (obj["Size"] != null) ulong.TryParse(obj["Size"].ToString(), out size);
                            if (obj["FreeSpace"] != null) ulong.TryParse(obj["FreeSpace"].ToString(), out free);
                            var sizeGB = size / 1024 / 1024 / 1024;
                            var freeGB = free / 1024 / 1024 / 1024;
                            disks.Add($"{drive} ({freeGB}/{sizeGB} GB)");
                        }
                    }
                    info["Disks"] = string.Join(", ", disks);
                }
                catch { }
            }
            catch (Exception ex)
            {
                info["Error"] = ex.Message;
            }

            return info;
        }

        public static bool IsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static List<string> GetUsers()
        {
            var users = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_UserAccount WHERE Status='OK' AND Disabled='FALSE'"))
                {
                    var systemUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "ADMINISTRATOR",
                        "GUEST",
                        "DEFAULTACCOUNT",
                        "SYSTEM",
                        "LOCAL SERVICE",
                        "NETWORK SERVICE",
                        "WDAGUTILITYACCOUNT",
                        "CODESANDBOXOFFLINE",
                        "CODESANDBOXONLINE",
                        "CODESANDBOX",
                        "DWM-1",
                        "DWM-2",
                        "DWM-3",
                        "DWM-4",
                        "DWM-5",
                        "DWM-6",
                        "DWM-7",
                        "DWM-8",
                        "ANONYMOUS LOGON",
                        "AUTHENTICATED USERS",
                        "BATCH",
                        "CONSOLE",
                        "CREATOR GROUP",
                        "CREATOR OWNER",
                        "DIALUP",
                        "DIGEST AUTHENTICATION",
                        "ENTERPRISE DOMAIN CONTROLLERS",
                        "EVERYONE",
                        "INTERACTIVE",
                        "IUSR",
                        "LOCAL",
                        "LOCAL SYSTEM",
                        "NETWORK",
                        "NTLM AUTHENTICATION",
                        "OWNER RIGHTS",
                        "REMOTE",
                        "REMOTE INTERACTIVE",
                        "RESTRICTED",
                        "SELF",
                        "SERVICE",
                        "SYSTEM",
                        "TERMINAL SERVER USER",
                        "THIS ORGANIZATION",
                        "USERS",
                        "УСЛУГИ",
                        "ПАРАМЕТРЫ"
                    };

                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var name = obj["Name"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(name) &&
                            !name.StartsWith("$") &&
                            !name.StartsWith("DWM-") &&
                            !name.StartsWith("S-1-") &&
                            !systemUsers.Contains(name))
                        {
                            users.Add(name);
                        }
                    }
                }
            }
            catch { }
            return users;
        }
    }
}