// language: C#, file: Core/ProcessHelper.cs
// ФИКС: ProcessBreakOnTermination принимает ULONG (4 байта), а не ULONG_PTR.
// На x64 передаём sizeof(int) = 4. Это устраняет STATUS_INFO_LENGTH_MISMATCH.
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
        // ============================================================
        // P/INVOKE
        // ============================================================
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSuspendProcess(IntPtr h);
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtResumeProcess(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr h, uint code);

        // ============================================================
        // NtSetInformationProcess — ФИКС: ref int (4 байта), а не ref IntPtr
        // ============================================================
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref int processInformation,
            int processInformationLength);

        [DllImport("ntdll.dll")]
        private static extern int RtlSetProcessIsCritical(int value, int old, int flags);
        [DllImport("ntdll.dll")]
        private static extern int RtlGetProcessIsCritical(int pid, out int isCritical);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
            ref TOKEN_PRIVILEGES state, int len, IntPtr prev, IntPtr ret);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        // ============================================================
        // Константы
        // ============================================================
        private const uint PROCESS_TERMINATE = 0x0001;
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint PROCESS_SET_LIMITED_INFORMATION = 0x2000;

        private const int ProcessBreakOnTermination = 29;

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const string SE_DEBUG_NAME = "SeDebugPrivilege";
        private const string SE_TCB_NAME = "SeTcbPrivilege";
        private const uint SE_PRIVILEGE_ENABLED = 0x2;

        private const int STATUS_SUCCESS = 0x00000000;
        private const int STATUS_ACCESS_DENIED = unchecked((int)0xC0000022);
        private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
        private const int STATUS_INVALID_INFO_CLASS = unchecked((int)0xC0000003);
        private const int STATUS_PRIVILEGE_NOT_HELD = unchecked((int)0xC0000061);

        // ============================================================
        // BASIC
        // ============================================================
        public static bool IsAdministrator()
        {
            try
            {
                var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
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
                using (var s = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId FROM Win32_Process"))
                    foreach (var o in s.Get())
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
            catch { }

            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        string name = proc.ProcessName;
                        if (string.IsNullOrEmpty(name)) continue;

                        float mb = 0;
                        try { mb = proc.WorkingSet64 / 1024f / 1024f; } catch { }

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
                            MemoryMB = mb,
                            Status = status,
                            ParentPID = ppid
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
                return RtlGetProcessIsCritical(pid, out isCritical) == 0 && isCritical != 0;
            }
            catch { return false; }
        }

        // ============================================================
        // SET CRITICAL — ФИКС РАЗМЕРА
        // ============================================================
        public static ProcessOpResult SetSystemCritical(int pid, bool critical, out string detail)
        {
            detail = null;

            if (!IsAdministrator())
            {
                detail = "Требуются права администратора.";
                return ProcessOpResult.NotAdmin;
            }

            if (!EnableDebugPrivilege())
            {
                detail = "SeDebugPrivilege недоступен.";
                return ProcessOpResult.DebugPrivilegeFailed;
            }

            EnableTcbPrivilege();

            // Текущий — прямой вызов
            if (pid == Process.GetCurrentProcess().Id)
            {
                int r = RtlSetProcessIsCritical(critical ? 1 : 0, 0, 0);
                if (r != 0)
                {
                    detail = $"RtlSetProcessIsCritical: 0x{r:X8}";
                    return ProcessOpResult.RtlSetCriticalFailed;
                }
                return ProcessOpResult.Success;
            }

            uint accessNeeded = PROCESS_SET_INFORMATION | PROCESS_SET_LIMITED_INFORMATION;

            IntPtr hProcess = OpenProcess(accessNeeded, false, pid);
            if (hProcess == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                detail = $"OpenProcess ошибка {err}.";
                return ProcessOpResult.OpenProcessFailed;
            }

            try
            {
                // ============================================================
                // ФИКС: int + sizeof(int) = 4 байта
                // ============================================================
                int value = critical ? 1 : 0;
                int length = sizeof(int);

                int result = NtSetInformationProcess(hProcess, ProcessBreakOnTermination,
                    ref value, length);

                if (result == STATUS_SUCCESS)
                    return ProcessOpResult.Success;

                switch (result)
                {
                    case STATUS_ACCESS_DENIED:
                        detail = "STATUS_ACCESS_DENIED. Процесс защищён (PPL).";
                        return ProcessOpResult.ProtectedProcess;

                    case STATUS_INFO_LENGTH_MISMATCH:
                        detail = "STATUS_INFO_LENGTH_MISMATCH — даже с 4 байтами. " +
                                 "Возможно, ядро требует 8. Проверь версию Windows.";
                        return ProcessOpResult.NtSetInfoFailed;

                    case STATUS_INVALID_INFO_CLASS:
                        detail = "STATUS_INVALID_INFO_CLASS. Ядро не поддерживает ProcessBreakOnTermination.";
                        return ProcessOpResult.NtSetInfoFailed;

                    case STATUS_PRIVILEGE_NOT_HELD:
                        detail = "STATUS_PRIVILEGE_NOT_HELD. Нужен SeTcbPrivilege.";
                        return ProcessOpResult.TcbPrivilegeFailed;

                    default:
                        detail = $"NtSetInformationProcess: NTSTATUS=0x{result:X8}";
                        return ProcessOpResult.NtSetInfoFailed;
                }
            }
            finally { CloseHandle(hProcess); }
        }

        public static bool SetSystemCritical(int pid, bool critical)
        {
            string detail;
            var r = SetSystemCritical(pid, critical, out detail);
            if (r == ProcessOpResult.Success) return true;
            throw new Exception($"Ошибка: {detail}");
        }

        public static bool SetCritical(int pid, bool critical) => SetSystemCritical(pid, critical);

        public static bool SetCurrentProcessCritical(bool critical)
        {
            try
            {
                if (!IsAdministrator()) throw new Exception("Нужны права администратора.");
                int r = RtlSetProcessIsCritical(critical ? 1 : 0, 0, 0);
                if (r != 0) throw new Exception($"Ошибка: 0x{r:X8}");
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
                IntPtr h = OpenProcess(PROCESS_TERMINATE, false, pid);
                if (h == IntPtr.Zero) return false;
                try { return TerminateProcess(h, 0); }
                finally { CloseHandle(h); }
            }
            catch { return false; }
        }

        public static bool FreezeProcess(int pid)
        {
            try
            {
                EnableDebugPrivilege();
                IntPtr h = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                if (h == IntPtr.Zero) return false;
                try { return NtSuspendProcess(h) == 0; }
                finally { CloseHandle(h); }
            }
            catch { return false; }
        }

        public static bool UnfreezeProcess(int pid)
        {
            try
            {
                EnableDebugPrivilege();
                IntPtr h = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                if (h == IntPtr.Zero) return false;
                try { return NtResumeProcess(h) == 0; }
                finally { CloseHandle(h); }
            }
            catch { return false; }
        }

        public static void ClearCache() { }
    }
}