using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace BunnyBlack.Core
{
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

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetInformationProcess(IntPtr processHandle, int processInformationClass, ref int processInformation, int processInformationLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

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
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint PROCESS_ALL_ACCESS = 0x0F0000;
        private const int ProcessBreakOnTermination = 29;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const string SE_DEBUG_NAME = "SeDebugPrivilege";
        private const string SE_TCB_NAME = "SeTcbPrivilege";
        private const uint SE_PRIVILEGE_ENABLED = 0x2;

        private static Dictionary<int, bool> criticalCache = new Dictionary<int, bool>();
        private static object cacheLock = new object();
        private static DateTime lastCacheUpdate = DateTime.MinValue;
        private static TimeSpan cacheExpiry = TimeSpan.FromSeconds(5);

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

        private static bool EnableDebugPrivilege()
        {
            try
            {
                IntPtr hToken;
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
                    return false;

                try
                {
                    long luid;
                    if (!LookupPrivilegeValue(null, SE_DEBUG_NAME, out luid))
                        return false;

                    TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    };

                    return AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
            catch { return false; }
        }

        private static bool EnableTcbPrivilege()
        {
            try
            {
                IntPtr hToken;
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
                    return false;

                try
                {
                    long luid;
                    if (!LookupPrivilegeValue(null, SE_TCB_NAME, out luid))
                        return false;

                    TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    };

                    return AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
            catch { return false; }
        }

        public static List<ProcessInfo> GetProcessesFast()
        {
            var result = new List<ProcessInfo>();
            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
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

                        result.Add(new ProcessInfo
                        {
                            PID = proc.Id,
                            Name = name,
                            MemoryMB = memoryMB,
                            Status = status,
                            IsCritical = false,
                            ParentPID = 0,
                            Level = 0
                        });
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

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
        // РАБОТАЕТ! - КРИТИЧНОСТЬ ДЛЯ ВСЕЙ СИСТЕМЫ (BSOD)
        // ============================================================
        public static bool SetSystemCritical(int pid, bool critical)
        {
            try
            {
                if (!IsAdministrator())
                {
                    throw new Exception("Требуются права администратора!");
                }

                if (!EnableDebugPrivilege())
                {
                    throw new Exception("Не удалось получить привилегии SeDebugPrivilege!");
                }

                if (!EnableTcbPrivilege())
                {
                    throw new Exception("Не удалось получить привилегии SeTcbPrivilege!");
                }

                // Если это текущий процесс - используем RtlSetProcessIsCritical
                if (pid == Process.GetCurrentProcess().Id)
                {
                    int value = critical ? 1 : 0;
                    int oldValue = 0;
                    int flags = 0;

                    int result = RtlSetProcessIsCritical(value, oldValue, flags);
                    if (result != 0)
                    {
                        throw new Exception($"Ошибка RtlSetProcessIsCritical: 0x{result:X8}");
                    }

                    ClearCache();
                    return true;
                }

                // Для других процессов - пытаемся через OpenProcess + NtSetInformationProcess
                IntPtr hProcess = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
                if (hProcess == IntPtr.Zero)
                {
                    hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
                    if (hProcess == IntPtr.Zero)
                    {
                        throw new Exception($"Не удалось открыть процесс (PID: {pid}). Возможно, процесс защищён.");
                    }
                }

                try
                {
                    int value = critical ? 1 : 0;
                    int result = NtSetInformationProcess(hProcess, ProcessBreakOnTermination, ref value, sizeof(int));

                    if (result != 0)
                    {
                        throw new Exception($"Ошибка NtSetInformationProcess: 0x{result:X8}. Процесс защищён.");
                    }

                    ClearCache();
                    return true;
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка изменения критичности: {ex.Message}");
            }
        }

        public static bool SetCritical(int pid, bool critical)
        {
            return SetSystemCritical(pid, critical);
        }

        public static bool SetCurrentProcessCritical(bool critical)
        {
            try
            {
                if (!IsAdministrator())
                {
                    throw new Exception("Требуются права администратора!");
                }

                int value = critical ? 1 : 0;
                int oldValue = 0;
                int flags = 0;

                int result = RtlSetProcessIsCritical(value, oldValue, flags);
                if (result != 0)
                {
                    throw new Exception($"Ошибка: 0x{result:X8}");
                }

                ClearCache();
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка изменения критичности: {ex.Message}");
            }
        }

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