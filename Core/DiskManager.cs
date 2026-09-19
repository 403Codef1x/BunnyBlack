// language: C#, file: Core/DiskManager.cs
// Список дисков, разделов, свободное место, назначение букв.
// Через WMI + mountvol.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;

namespace BunnyBlack.Core
{
    public class DiskInfo
    {
        public string DeviceId { get; set; }
        public string Model { get; set; }
        public long SizeBytes { get; set; }
        public string InterfaceType { get; set; }
        public int Partitions { get; set; }
        public bool IsSystem { get; set; }
    }

    public class PartitionInfo
    {
        public string DeviceId { get; set; }
        public string DriveLetter { get; set; }
        public string Label { get; set; }
        public string FileSystem { get; set; }
        public long SizeBytes { get; set; }
        public long FreeBytes { get; set; }
        public string Type { get; set; }
        public bool Bootable { get; set; }
        public bool IsSystem { get; set; }
        public bool IsWindows { get; set; }   // есть папка Windows
    }

    public static class DiskManager
    {
        // ============================================================
        // СПИСОК ФИЗИЧЕСКИХ ДИСКОВ
        // ============================================================
        public static List<DiskInfo> GetDisks()
        {
            var list = new List<DiskInfo>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        try
                        {
                            list.Add(new DiskInfo
                            {
                                DeviceId = o["DeviceID"]?.ToString() ?? "",
                                Model = o["Model"]?.ToString() ?? "",
                                SizeBytes = Convert.ToInt64(o["Size"] ?? 0L),
                                InterfaceType = o["InterfaceType"]?.ToString() ?? "",
                                Partitions = Convert.ToInt32(o["Partitions"] ?? 0),
                                IsSystem = (o["DeviceID"]?.ToString() ?? "").Contains("PHYSICALDRIVE0")
                            });
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[GetDisks] " + ex.Message); }
            return list;
        }

        // ============================================================
        // СПИСОК РАЗДЕЛОВ / ЛОГИЧЕСКИХ ДИСКОВ
        // ============================================================
        public static List<PartitionInfo> GetPartitions()
        {
            var list = new List<PartitionInfo>();

            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady && d.DriveType != DriveType.Fixed) continue;

                        var pi = new PartitionInfo
                        {
                            DriveLetter = d.Name.TrimEnd('\\'),
                            Label = d.IsReady ? d.VolumeLabel : "",
                            FileSystem = d.IsReady ? d.DriveFormat : "",
                            SizeBytes = d.IsReady ? d.TotalSize : 0,
                            FreeBytes = d.IsReady ? d.TotalFreeSpace : 0,
                            Type = d.DriveType.ToString()
                        };

                        // маркеры системного
                        string windows = Path.Combine(d.Name, "Windows", "System32");
                        pi.IsWindows = Directory.Exists(windows);
                        pi.Bootable = File.Exists(Path.Combine(d.Name, "bootmgr")) ||
                                      Directory.Exists(Path.Combine(d.Name, "Boot"));
                        pi.IsSystem = pi.IsWindows;

                        list.Add(pi);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[GetPartitions] " + ex.Message); }

            return list;
        }

        // ============================================================
        // СМЕНИТЬ БУКВУ
        // ============================================================
        public static bool SetDriveLetter(string currentLetter, char newLetter, out string error)
        {
            error = null;
            try
            {
                string script =
                    $"select volume {currentLetter.TrimEnd(':')}\r\n" +
                    $"assign letter={newLetter}\r\n";

                string scriptPath = Path.Combine(Path.GetTempPath(), "bb_diskpart.txt");
                File.WriteAllText(scriptPath, script);

                var si = new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = $"/s \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var p = Process.Start(si))
                {
                    p?.WaitForExit(15000);
                }

                try { File.Delete(scriptPath); } catch { }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ============================================================
        // МОНТИРОВАТЬ / РАЗМОНТИРОВАТЬ (mountvol)
        // ============================================================
        public static bool MountVolume(string volumeGuid, string mountPoint, out string error)
        {
            error = null;
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = "mountvol.exe",
                    Arguments = $"\"{mountPoint}\" {volumeGuid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(si))
                {
                    p?.WaitForExit(8000);
                    if (p != null && p.ExitCode != 0)
                    {
                        error = p.StandardError.ReadToEnd();
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool UnmountVolume(string mountPoint, out string error)
        {
            error = null;
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = "mountvol.exe",
                    Arguments = $"\"{mountPoint}\" /d",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(si))
                {
                    p?.WaitForExit(8000);
                    if (p != null && p.ExitCode != 0)
                    {
                        error = p.StandardError.ReadToEnd();
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ============================================================
        // ФОРМАТ
        // ============================================================
        public static bool FormatVolume(string driveLetter, string fs, string label, out string error)
        {
            error = null;
            try
            {
                string letter = driveLetter.TrimEnd(':');
                string script =
                    $"select volume {letter}\r\n" +
                    $"format fs={fs} label=\"{label}\" quick\r\n";

                string scriptPath = Path.Combine(Path.GetTempPath(), "bb_format.txt");
                File.WriteAllText(scriptPath, script);

                var si = new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = $"/s \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(si)) p?.WaitForExit(60000);

                try { File.Delete(scriptPath); } catch { }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ============================================================
        // ХЕЛПЕР
        // ============================================================
        public static string FormatBytes(long b)
        {
            if (b <= 0) return "—";
            string[] s = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = b; int i = 0;
            while (len >= 1024 && i < s.Length - 1) { i++; len /= 1024; }
            return $"{len:0.##} {s[i]}";
        }
    }
}