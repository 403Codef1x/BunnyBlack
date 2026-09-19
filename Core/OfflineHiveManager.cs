// language: C#, file: Core/OfflineHiveManager.cs
// Управление оффлайн-кустами: загрузка, работа с любым HIV/DAT/REG, выгрузка.
// Используется в WinRE для чтения/записи целевой системы.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BunnyBlack.Core
{
    public class MountedHive
    {
        public string MountName { get; set; }        // временное имя в HKLM
        public string SourceFile { get; set; }       // путь к .hiv/.dat
        public string Label { get; set; }            // человекочитаемое имя
        public bool IsWritable { get; set; }
        public DateTime MountedAt { get; set; }
    }

    public static class OfflineHiveManager
    {
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegLoadKey(IntPtr hKey, string lpSubKey, string lpFile);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegUnLoadKey(IntPtr hKey, string lpSubKey);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegSaveKey(IntPtr hKey, string lpFile, IntPtr lpSecurityAttributes);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegRestoreKey(IntPtr hKey, string lpFile, int dwFlags);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        private static readonly uint HKEY_LOCAL_MACHINE = 0x80000002;
        private static readonly uint HKEY_USERS = 0x80000003;

        // Временные имена, в которых монтируем кусты. Не торчат наружу.
        private static readonly string TmpPrefix = "BB_Mount_";

        private static readonly List<MountedHive> _mounted = new List<MountedHive>();
        private static int _counter = 0;

        public static List<MountedHive> Mounted => new List<MountedHive>(_mounted);

        // ============================================================
        // СМОНТИРОВАТЬ ЛЮБОЙ ФАЙЛ-КУСТ
        // ============================================================
        public static bool Mount(string filePath, out MountedHive hive, out string error)
        {
            hive = null;
            error = null;

            try
            {
                if (!File.Exists(filePath))
                {
                    error = "Файл не найден: " + filePath;
                    return false;
                }

                _counter++;
                string mountName = TmpPrefix + _counter.ToString("D3");
                string fileName = Path.GetFileNameWithoutExtension(filePath);

                // отдельно для .dat (NTUSER) — монтируем в HKU
                bool isDat = filePath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
                IntPtr root = isDat ? (IntPtr)HKEY_USERS : (IntPtr)HKEY_LOCAL_MACHINE;

                int r = RegLoadKey(root, mountName, filePath);
                if (r != 0)
                {
                    error = DecodeLoadError(r, filePath);
                    return false;
                }

                hive = new MountedHive
                {
                    MountName = mountName,
                    SourceFile = filePath,
                    Label = fileName,
                    IsWritable = true,
                    MountedAt = DateTime.Now
                };
                _mounted.Add(hive);
                Debug.WriteLine($"[OfflineHiveManager] mounted {mountName} <- {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ============================================================
        // РАЗМОНТИРОВАТЬ
        // ============================================================
        public static bool Unmount(MountedHive hive, out string error)
        {
            error = null;
            try
            {
                if (hive == null) { error = "hive = null"; return false; }

                bool isDat = hive.SourceFile.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
                IntPtr root = isDat ? (IntPtr)HKEY_USERS : (IntPtr)HKEY_LOCAL_MACHINE;

                int r = RegUnLoadKey(root, hive.MountName);
                if (r != 0)
                {
                    error = $"RegUnLoadKey код {r}";
                    return false;
                }
                _mounted.Remove(hive);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void UnmountAll()
        {
            foreach (var h in new List<MountedHive>(_mounted))
            {
                string _;
                Unmount(h, out _);
            }
        }

        // ============================================================
        // ПОЛУЧИТЬ КЛЮЧ ИЗ СМОНТИРОВАННОГО КУСТА
        // ============================================================
        public static RegistryKey OpenSubKey(MountedHive hive, string subPath, bool writable = false)
        {
            try
            {
                if (hive == null) return null;
                bool isDat = hive.SourceFile.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
                string root = isDat ? "HKEY_USERS" : "HKEY_LOCAL_MACHINE";
                string full = $"{root}\\{hive.MountName}";
                if (!string.IsNullOrEmpty(subPath))
                    full += "\\" + subPath.TrimStart('\\');
                return RegistryKey.OpenBaseKey(
                    isDat ? RegistryHive.Users : RegistryHive.LocalMachine,
                    RegistryView.Default
                ).OpenSubKey(hive.MountName + (string.IsNullOrEmpty(subPath) ? "" : "\\" + subPath.TrimStart('\\')), writable);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpenSubKey] {ex.Message}");
                return null;
            }
        }

        // ============================================================
        // ЭКСПОРТ В .REG
        // ============================================================
        public static bool ExportToReg(MountedHive hive, string subPath, string outFile, out string error)
        {
            error = null;
            try
            {
                using (var key = OpenSubKey(hive, subPath))
                {
                    if (key == null) { error = "Ключ не найден"; return false; }
                    string regPath = $"HKLM\\{hive.MountName}" +
                                     (string.IsNullOrEmpty(subPath) ? "" : "\\" + subPath.TrimStart('\\'));

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Windows Registry Editor Version 5.00");
                    sb.AppendLine();
                    DumpKey(key, regPath, sb);
                    File.WriteAllText(outFile, sb.ToString(), System.Text.Encoding.Unicode);
                    return true;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static void DumpKey(RegistryKey key, string path, System.Text.StringBuilder sb)
        {
            sb.AppendLine($"[{path}]");
            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(name);
                var kind = key.GetValueKind(name);
                string namePart = string.IsNullOrEmpty(name) ? "@" : "\"" + name + "\"";
                sb.AppendLine($"{namePart}={FormatRegValue(value, kind)}");
            }
            sb.AppendLine();
            foreach (var sub in key.GetSubKeyNames())
            {
                try
                {
                    using (var child = key.OpenSubKey(sub))
                        if (child != null) DumpKey(child, path + "\\" + sub, sb);
                }
                catch { }
            }
        }

        private static string FormatRegValue(object value, RegistryValueKind kind)
        {
            if (value == null) return "\"\"";
            switch (kind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    return "\"" + value.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                case RegistryValueKind.DWord:
                    return "dword:" + ((int)value).ToString("x8");
                case RegistryValueKind.QWord:
                    return "hex(b):" + BitConverter.ToString(BitConverter.GetBytes((long)value)).Replace("-", ",").ToLowerInvariant();
                case RegistryValueKind.MultiString:
                    var arr = (string[])value;
                    return "hex(7):" + BitConverter.ToString(
                        System.Text.Encoding.Unicode.GetBytes(string.Join("\0", arr) + "\0\0")
                    ).Replace("-", ",").ToLowerInvariant();
                case RegistryValueKind.Binary:
                    return "hex:" + BitConverter.ToString((byte[])value).Replace("-", ",").ToLowerInvariant();
                default:
                    return "\"" + value.ToString() + "\"";
            }
        }

        // ============================================================
        // ИМПОРТ .REG
        // ============================================================
        public static bool ImportFromReg(MountedHive hive, string regFile, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(regFile)) { error = "Файл не найден"; return false; }
                string content = File.ReadAllText(regFile, System.Text.Encoding.Unicode);
                if (!content.StartsWith("Windows Registry Editor"))
                    content = File.ReadAllText(regFile); // попытка как ANSI

                // Простейший парсер — только для строк/двордов
                string currentPath = null;
                foreach (var rawLine in content.Split('\n'))
                {
                    string line = rawLine.Trim('\r', ' ', '\t');
                    if (string.IsNullOrEmpty(line) || line.StartsWith(";")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        string p = line.Trim('[', ']');
                        // мапим HKLM\<mount> → <subPath>
                        string prefix = $"HKLM\\{hive.MountName}";
                        if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            currentPath = p.Substring(prefix.Length).TrimStart('\\');
                        else
                            currentPath = null;
                        continue;
                    }

                    if (currentPath == null) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;

                    string name = line.Substring(0, eq).Trim('"');
                    if (name == "@") name = "";
                    string val = line.Substring(eq + 1).Trim();

                    using (var key = OpenSubKey(hive, currentPath, true))
                    {
                        if (key == null) continue;

                        if (val.StartsWith("\""))
                        {
                            string s = val.Trim('"').Replace("\\\\", "\\").Replace("\\\"", "\"");
                            key.SetValue(name, s, RegistryValueKind.String);
                        }
                        else if (val.StartsWith("dword:"))
                        {
                            int dw = Convert.ToInt32(val.Substring(6), 16);
                            key.SetValue(name, dw, RegistryValueKind.DWord);
                        }
                        // Остальные типы пропускаем для краткости
                    }
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static string DecodeLoadError(int code, string file)
        {
            switch (code)
            {
                case 0: return null;
                case 5: return $"Доступ запрещён (5): {file}\nЗапусти от админа.";
                case 32: return $"Файл занят (32): {file}\nЗакрой редактор реестра системы или перезагрузись в WinRE.";
                case 87: return $"Неверный параметр (87): {file}";
                case 2: return $"Файл не найден (2): {file}";
                default: return $"RegLoadKey ошибка {code}: {file}";
            }
        }
    }
}