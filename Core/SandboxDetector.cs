// language: C#, file: Core/SandboxDetector.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace BunnyBlack.Core
{
    public static class SandboxDetector
    {
        private static readonly string[] VmMacPrefixes =
        {
            "080027",  // VirtualBox
            "000C29",  // VMware
            "005056",  // VMware ESX
            "000569",  // VMware
            "001C14",  // VMware
            "525400",  // QEMU/KVM
            "00155D",  // Hyper-V
            "00163E"   // Xen
        };

        private static readonly string[] VmProcesses =
        {
            "vboxservice", "vboxtray", "vmtoolsd", "vmwaretray", "vmwareuser",
            "qemu-ga", "xenservice", "vmsrvc", "vmusrvc"
        };

        private static readonly string[] VmRegistryMarkers =
        {
            @"SYSTEM\CurrentControlSet\Services\VBoxGuest",
            @"SYSTEM\CurrentControlSet\Services\VBoxMouse",
            @"SYSTEM\CurrentControlSet\Services\vmci",
            @"SYSTEM\CurrentControlSet\Services\vmhgfs",
            @"SYSTEM\CurrentControlSet\Services\vmx86",
            @"SYSTEM\CurrentControlSet\Services\qemu-ga",
            @"SYSTEM\CurrentControlSet\Services\vmicheartbeat",
            @"SYSTEM\CurrentControlSet\Enum\SCSI\Disk&Ven_VMware",
            @"SYSTEM\CurrentControlSet\Enum\SCSI\Disk&Ven_VBOX",
            @"SYSTEM\CurrentControlSet\Enum\SCSI\Disk&Ven_QEMU"
        };

        private static string _cache = null;

        public static string Analyze()
        {
            if (_cache != null) return _cache;
            var reasons = new List<string>();

            try { if (HasVmMac()) reasons.Add("mac"); } catch { }
            try { if (HasVmProcess()) reasons.Add("proc"); } catch { }
            try { if (HasVmRegistry()) reasons.Add("reg"); } catch { }
            try { if (HasVmWmi()) reasons.Add("wmi"); } catch { }

            _cache = reasons.Count == 0 ? "clean" : "vm:" + string.Join("+", reasons);
            return _cache;
        }

        public static bool IsVirtualized() => Analyze() != "clean";

        private static bool HasVmMac()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                string mac = ni.GetPhysicalAddress()?.ToString() ?? "";
                if (mac.Length < 8) continue;
                string prefix = mac.Substring(0, 6).ToUpperInvariant();
                foreach (var p in VmMacPrefixes)
                    if (prefix == p) return true;
            }
            return false;
        }

        private static bool HasVmProcess()
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string n = p.ProcessName.ToLowerInvariant();
                    foreach (var v in VmProcesses)
                        if (n == v) return true;
                }
                catch { }
            }
            return false;
        }

        private static bool HasVmRegistry()
        {
            foreach (var path in VmRegistryMarkers)
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(path))
                        if (k != null) return true;
                }
                catch { }
            }
            return false;
        }

        private static bool HasVmWmi()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Model, Manufacturer FROM Win32_ComputerSystem"))
                    foreach (var o in s.Get())
                    {
                        string model = (o["Model"]?.ToString() ?? "").ToLowerInvariant();
                        string man = (o["Manufacturer"]?.ToString() ?? "").ToLowerInvariant();
                        string combo = model + " " + man;
                        if (combo.Contains("vmware") || combo.Contains("virtualbox") ||
                            combo.Contains("qemu") || combo.Contains("xen") ||
                            combo.Contains("hyper-v") || combo.Contains("kvm") ||
                            combo.Contains("parallels")) return true;
                    }
            }
            catch { }
            return false;
        }
    }
}