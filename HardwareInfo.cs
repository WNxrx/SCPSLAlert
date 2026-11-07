using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.IO;
using System.Text;

namespace SCPSLAlert
{
    /// <summary>
    /// 崩溃报告用的硬件配置表（WMI 读取，逐项容错；读不到就写“未知”）。
    /// 只在崩溃时调用一次，慢一点没关系。
    /// </summary>
    internal static class HardwareInfo
    {
        public static string Collect()
        {
            StringBuilder sb = new StringBuilder(4096);
            sb.AppendLine("==== 硬件配置 ====");

            Append(sb, "CPU", Cpu());
            Append(sb, "主板", Board());
            Append(sb, "内存", Memory());
            Append(sb, "显卡", Gpu());
            Append(sb, "系统", Os());
            Append(sb, "硬盘", Disks());
            Append(sb, "显示", Display());
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string name, string value)
        {
            sb.AppendLine(name.PadRight(6) + ": " + (string.IsNullOrEmpty(value) ? "未知" : value));
        }

        private static string Cpu()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    string nm = k != null ? (k.GetValue("ProcessorNameString") as string) : null;
                    if (!string.IsNullOrEmpty(nm)) return nm.Trim() + "  (" + Environment.ProcessorCount + " 线程)";
                }
            }
            catch { }
            try
            {
                foreach (ManagementObject mo in Query("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor"))
                {
                    string name = Str(mo, "Name");
                    string cores = Str(mo, "NumberOfCores");
                    string threads = Str(mo, "NumberOfLogicalProcessors");
                    string mhz = Str(mo, "MaxClockSpeed");
                    return name + "  (" + cores + " 核 / " + threads + " 线程"
                        + (mhz.Length > 0 ? " / " + mhz + " MHz" : "") + ")";
                }
            }
            catch { }
            return "";
        }

        private static string Board()
        {
            try
            {
                foreach (ManagementObject mo in Query("SELECT Manufacturer,Product FROM Win32_BaseBoard"))
                    return Str(mo, "Manufacturer") + " " + Str(mo, "Product");
            }
            catch { }
            return "";
        }

        private static string Memory()
        {
            try
            {
                double total = 0;
                foreach (ManagementObject mo in Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    double v;
                    if (double.TryParse(Str(mo, "TotalPhysicalMemory"), out v)) total = v;
                }
                List<string> sticks = new List<string>();
                foreach (ManagementObject mo in Query("SELECT Capacity,Speed,Manufacturer,PartNumber FROM Win32_PhysicalMemory"))
                {
                    double cap;
                    double.TryParse(Str(mo, "Capacity"), out cap);
                    string speed = Str(mo, "Speed");
                    sticks.Add((cap / 1073741824.0).ToString("0") + "GB"
                        + (speed.Length > 0 ? "@" + speed : "")
                        + (Str(mo, "PartNumber").Length > 0 ? " " + Str(mo, "PartNumber") : ""));
                }
                string line = (total / 1073741824.0).ToString("0.0") + " GB";
                if (sticks.Count > 0) line += "（" + string.Join(" + ", sticks.ToArray()) + "）";
                return line;
            }
            catch { }
            return FallbackMemory();
        }

        private static string FallbackMemory()
        {
            try
            {
                MEMORYSTATUSEX st = new MEMORYSTATUSEX();
                st.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref st))
                    return (st.ullTotalPhys / 1073741824.0).ToString("0.0") + " GB";
            }
            catch { }
            return "";
        }

        private static string Gpu()
        {
            try
            {
                List<string> list = new List<string>();
                foreach (ManagementObject mo in Query("SELECT Name,AdapterRAM,DriverVersion FROM Win32_VideoController"))
                {
                    double ram;
                    double.TryParse(Str(mo, "AdapterRAM"), out ram);
                    string line = Str(mo, "Name");
                    if (ram > 0) line += "  " + (ram / 1073741824.0).ToString("0.0") + "GB";
                    line += "  驱动 " + Str(mo, "DriverVersion");
                    list.Add(line);
                }
                return string.Join("  /  ", list.ToArray());
            }
            catch { }
            return "";
        }

        private static string Os()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        string nm = k.GetValue("ProductName") as string;
                        string bd = k.GetValue("CurrentBuild") as string;
                        string dv = k.GetValue("DisplayVersion") as string;
                        if (!string.IsNullOrEmpty(nm))
                            return nm + "  " + dv + "  Build " + bd + (Environment.Is64BitOperatingSystem ? "  64 位" : "  32 位");
                    }
                }
            }
            catch { }
            try
            {
                foreach (ManagementObject mo in Query("SELECT Caption,Version,BuildNumber,OSArchitecture FROM Win32_OperatingSystem"))
                    return Str(mo, "Caption") + "  " + Str(mo, "Version") + "  Build " + Str(mo, "BuildNumber")
                        + "  " + Str(mo, "OSArchitecture");
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        private static string Disks()
        {
            try
            {
                List<string> dl = new List<string>();
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    dl.Add(d.Name.TrimEnd('\\') + " " + (d.TotalSize / 1073741824.0).ToString("0") + "GB"
                        + "（剩 " + (d.AvailableFreeSpace / 1073741824.0).ToString("0") + "GB）");
                }
                if (dl.Count > 0) return string.Join("  /  ", dl.ToArray());
            }
            catch { }
            try
            {
                List<string> list = new List<string>();
                foreach (ManagementObject mo in Query("SELECT Model,Size,MediaType FROM Win32_DiskDrive"))
                {
                    double size;
                    double.TryParse(Str(mo, "Size"), out size);
                    string line = Str(mo, "Model");
                    if (size > 0) line += "  " + (size / 1073741824.0).ToString("0") + "GB";
                    list.Add(line);
                }
                return string.Join("  /  ", list.ToArray());
            }
            catch { }
            return "";
        }

        private static string Display()
        {
            try
            {
                foreach (ManagementObject mo in Query("SELECT CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate FROM Win32_VideoController"))
                {
                    string w = Str(mo, "CurrentHorizontalResolution");
                    string h = Str(mo, "CurrentVerticalResolution");
                    string hz = Str(mo, "CurrentRefreshRate");
                    if (w.Length > 0) return w + "×" + h + (hz.Length > 0 ? " @ " + hz + "Hz" : "");
                }
            }
            catch { }
            return "";
        }

        private static IEnumerable<ManagementObject> Query(string wql)
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(wql);
            foreach (ManagementObject mo in searcher.Get())
                yield return mo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        private static string Str(ManagementObject mo, string name)
        {
            try
            {
                object v = mo[name];
                return v == null ? "" : v.ToString().Trim();
            }
            catch { return ""; }
        }
    }
}
