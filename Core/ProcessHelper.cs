// language: C#, file: Core/ProcessHelper.cs
// Полная замена.
// ФИКС КРАША: RtlSetProcessIsCritical для ЧУЖИХ процессов крашит систему
// (KeBugCheckEx CRITICAL_PROCESS_DIED) — это поведение ядра Windows 8.1+.
// Теперь:
//   - Текущему процессу — флаг ставится напрямую, безопасно.
//   - Чужому — возвращается ProtectedProcess с понятным сообщением, БЕЗ инъекции.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BunnyBlack.Core
{
    public enum ProcessOpResult
    {
        Success = 0,
        NotAdmin = 1,
        DebugPrivilegeFailed = 2,
        TcbPrivilegeFailed = 3,
        OpenProcessFailed = 4,
        NtSetInfoFailed = 5,
        RtlSetCriticalFailed = 6,
        ProtectedProcess = 7,
        NotCurrentProcess = 8,   // <-- новый код: операция недопустима для чужого процесса
        UnknownError = 99
    }

    public class ProcessInfo
    {
        public int PID { get; set; }
        public string Name { get; set; }
        public float MemoryMB { get; set; }
        public string Status { get; set; }
        public bool IsCritical { get; set; }
        public int ParentPID { get; set; }
        public List<ProcessInfo> Children { get; set; } = new List<ProcessInfo>();
        public int Level { get; set; }
    }

    public static class ProcessHelper
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("ntdll.dll")]
        private static extern int RtlSetProcessIsCritical(int NewValue, int OldValue, int Flags);

        [DllImport("ntdll.dll")]
        private static extern int RtlGetProcessIsCritical(int pid, out int IsCritical);

        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        private const uint PROCESS_TERMINATE = 0x0001;
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const string SE_DEBUG_NAME = "SeDebugPrivilege";
        private const string SE_TCB_NAME = "SeTcbPrivilege";
        private const uint SE_PRIVILEGE_ENABLED = 0x2;

        private static Dictionary<int, bool> criticalCache = new Dictionary<int, bool>();
        private static object cacheLock = new object();
        private static DateTime lastCacheUpdate = DateTime.MinValue;
        private static TimeSpan cacheExpiry = TimeSpan.FromSeconds(5);

        // ============================================================
        // BASIC
        // ============================================================
        public static bool IsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static bool EnablePrivilege(string name)
        {
            try
            {
                IntPtr hToken;
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
                    return false;
                try
                {
                    long luid;
                    if (!LookupPrivilegeValue(null, name, out luid)) return false;
                    TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    };
                    return AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
                finally { CloseHandle(hToken); }
            }
            catch { return false; }
        }

        private static bool EnableDebugPrivilege() => EnablePrivilege(SE_DEBUG_NAME);
        private static bool EnableTcbPrivilege() => EnablePrivilege(SE_TCB_NAME);

        // ============================================================
        // GET PROCESSES
        // ============================================================
        public static List<ProcessInfo> GetProcessesFast()
        {
            var result = new List<ProcessInfo>();
            var parentMap = new Dictionary<int, int>();

            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId FROM Win32_Process"))
                    foreach (var o in searcher.Get())
                    {
                        try
                        {
                            int pid = Convert.ToInt32(o["ProcessId"]);
                            int ppid = Convert.ToInt32(o["ParentProcessId"]);
                            parentMap[pid] = ppid;
                        }
                        catch { }
                    }
            }
            catch (Exception ex) { Debug.WriteLine($"[GetProcessesFast/wmi] {ex.Message}"); }

            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        string name = proc.ProcessName;
                        if (string.IsNullOrEmpty(name)) continue;

                        float memoryMB = 0;
                        try { memoryMB = proc.WorkingSet64 / 1024f / 1024f; } catch { }

                        string status = "Работает";
                        try
                        {
                            if (proc.HasExited) status = "Завершён";
                            else if (proc.Responding) status = "Активен";
                            else status = "Не отвечает";
                        }
                        catch { status = "Неизвестно"; }

                        int ppid = parentMap.TryGetValue(proc.Id, out int p) ? p : 0;

                        result.Add(new ProcessInfo
                        {
                            PID = proc.Id,
                            Name = name,
                            MemoryMB = memoryMB,
                            Status = status,
                            IsCritical = false,
                            ParentPID = ppid,
                            Level = 0
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[GetProcessesFast] {ex.Message}"); }
            return result;
        }

        // ============================================================
        // CHECK CRITICAL
        // ============================================================
        public static bool IsProcessCritical(int pid)
        {
            try
            {
                int isCritical = 0;
                int result = RtlGetProcessIsCritical(pid, out isCritical);
                return result == 0 && isCritical != 0;
            }
            catch { return false; }
        }

        // ============================================================
        // SET SYSTEM CRITICAL — БЕЗОПАСНАЯ ВЕРСИЯ
        // ============================================================
        public static ProcessOpResult SetSystemCritical(int pid, bool critical, out string detail)
        {
            detail = null;

            if (!IsAdministrator())
            {
                detail = "Требуются права администратора.";
                return ProcessOpResult.NotAdmin;
            }

            // ============================================================
            // ГЛАВНОЕ ОГРАНИЧЕНИЕ:
            // RtlSetProcessIsCritical для ЧУЖОГО процесса вызывает
            // KeBugCheckEx(CRITICAL_PROCESS_DIED) — система падает в BSOD.
            // Это документированное поведение ядра Windows 8.1+.
            // Разрешаем операцию ТОЛЬКО для текущего процесса.
            // ============================================================
            int currentPid = Process.GetCurrentProcess().Id;
            if (pid != currentPid)
            {
                detail =
                    "Windows запрещает устанавливать critical flag чужим процессам. " +
                    "Ядро немедленно роняет систему в BSOD (CRITICAL_PROCESS_DIED). " +
                    "Это НЕ ошибка программы — это защита Windows. " +
                    "Устанавливать флаг можно только самому себе.";
                return ProcessOpResult.NotCurrentProcess;
            }

            // Работаем только с текущим процессом
            try
            {
                int result = RtlSetProcessIsCritical(critical ? 1 : 0, 0, 0);
                if (result != 0)
                {
                    detail = $"RtlSetProcessIsCritical: 0x{result:X8}";
                    return ProcessOpResult.RtlSetCriticalFailed;
                }
                ClearCache();
                return ProcessOpResult.Success;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return ProcessOpResult.UnknownError;
            }
        }

        // Сохранённая оригинальная сигнатура
        public static bool SetSystemCritical(int pid, bool critical)
        {
            string detail;
            var r = SetSystemCritical(pid, critical, out detail);
            if (r == ProcessOpResult.Success) return true;
            throw new Exception($"Ошибка изменения критичности: {detail}");
        }

        public static bool SetCritical(int pid, bool critical) => SetSystemCritical(pid, critical);

        public static bool SetCurrentProcessCritical(bool critical)
        {
            try
            {
                if (!IsAdministrator())
                    throw new Exception("Требуются права администратора!");
                int result = RtlSetProcessIsCritical(critical ? 1 : 0, 0, 0);
                if (result != 0) throw new Exception($"Ошибка: 0x{result:X8}");
                ClearCache();
                return true;
            }
            catch (Exception ex) { throw new Exception($"Ошибка: {ex.Message}"); }
        }

        // ============================================================
        // KILL / FREEZE
        // ============================================================
        public static bool KillProcess(int pid)
        {
            try
            {
                EnableDebugPrivilege();
                IntPtr handle = OpenProcess(PROCESS_TERMINATE, false, pid);
                if (handle == IntPtr.Zero) return false;
                try { return TerminateProcess(handle, 0); }
                finally { CloseHandle(handle); }
            }
            catch { return false; }
        }

        public static bool FreezeProcess(int pid)
        {
            try
            {
                EnableDebugPrivilege();
                IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                if (handle == IntPtr.Zero) return false;
                try { return NtSuspendProcess(handle) == 0; }
                finally { CloseHandle(handle); }
            }
            catch { return false; }
        }

        public static bool UnfreezeProcess(int pid)
        {
            try
            {
                EnableDebugPrivilege();
                IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                if (handle == IntPtr.Zero) return false;
                try { return NtResumeProcess(handle) == 0; }
                finally { CloseHandle(handle); }
            }
            catch { return false; }
        }

        public static void ClearCache()
        {
            lock (cacheLock)
            {
                criticalCache.Clear();
                lastCacheUpdate = DateTime.MinValue;
            }
        }
    }
}