// language: C#, file: Core/UnlockHelper.cs
// Разблокировка файлов: Restart Manager API, снятие атрибутов, takeown/icacls,
// MoveFileEx с флагом отложенного удаления (для системных заблокированных).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BunnyBlack.Core
{
    public static class UnlockHelper
    {
        // ============================================================
        // RESTART MANAGER — узнать, какие процессы держат файл
        // ============================================================
        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        private const int RmRebootReasonNone = 0;
        private const int CCH_RM_MAX_APP_NAME = 255;
        private const int CCH_RM_MAX_SVC_NAME = 63;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle,
            uint nFiles, string[] rgsFilenames,
            uint nApplications, RM_UNIQUE_PROCESS[] rgApplications,
            uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle,
            out uint pnProcInfoNeeded, ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
            ref uint lpdwRebootReasons);

        public static List<int> GetLockingProcesses(string filePath)
        {
            var result = new List<int>();
            uint handle;
            string key = Guid.NewGuid().ToString();
            int rv = RmStartSession(out handle, 0, key);
            if (rv != 0) return result;

            try
            {
                string[] resources = { filePath };
                rv = RmRegisterResources(handle, 1, resources, 0, null, 0, null);
                if (rv != 0) return result;

                uint pnProcInfoNeeded = 0, pnProcInfo = 0, lpdwRebootReasons = RmRebootReasonNone;
                rv = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);

                if (rv == 234) // ERROR_MORE_DATA
                {
                    var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;
                    rv = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                    if (rv == 0)
                        for (int i = 0; i < pnProcInfo; i++)
                            result.Add(processInfo[i].Process.dwProcessId);
                }
            }
            finally
            {
                RmEndSession(handle);
            }

            return result;
        }

        // ============================================================
        // MOVE FILE EX — отложенное удаление/переименование
        // ============================================================
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);

        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
        private const int MOVEFILE_REPLACE_EXISTING = 0x1;

        public static bool ScheduleDeleteOnReboot(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                return MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }
            catch { return false; }
        }

        // ============================================================
        // TAKEOWN + ICACLS — оверрайд прав
        // ============================================================
        public static bool TakeOwnership(string path, out string error)
        {
            error = null;
            try
            {
                RunCmd($"takeown /f \"{path}\" /a");
                RunCmd($"icacls \"{path}\" /grant Administrators:F /t /c");

                // снимаем read-only / system / hidden
                try
                {
                    var attr = File.GetAttributes(path);
                    attr &= ~(FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden);
                    File.SetAttributes(path, attr);
                }
                catch { }

                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ============================================================
        // УДАЛЕНИЕ С РАЗБЛОКИРОВКОЙ
        // ============================================================
        public static bool ForceDelete(string path, out string error, bool killLockers = false)
        {
            error = null;
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) { error = "Не найдено."; return false; }

                // 1. найти блокирующие процессы
                var lockers = GetLockingProcesses(path);
                if (lockers.Count > 0 && killLockers)
                {
                    foreach (var pid in lockers)
                    {
                        try
                        {
                            using (var p = Process.GetProcessById(pid)) p.Kill();
                        }
                        catch { }
                    }
                    System.Threading.Thread.Sleep(300);
                }

                // 2. взять ownership
                string ownErr;
                TakeOwnership(path, out ownErr);

                // 3. попытаться удалить сейчас
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    else if (Directory.Exists(path)) Directory.Delete(path, true);
                    return true;
                }
                catch (Exception ex)
                {
                    // 4. fallback — отложить до перезагрузки
                    if (File.Exists(path) && ScheduleDeleteOnReboot(path))
                    {
                        error = "Файл заблокирован. Удаление отложено до перезагрузки (" + ex.Message + ")";
                        return true;
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void RunCmd(string arguments)
        {
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(si)) p?.WaitForExit(10000);
            }
            catch { }
        }
    }
}