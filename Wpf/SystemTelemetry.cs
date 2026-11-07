using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SCPSLAlert
{
    internal sealed class SystemTelemetry
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            public uint Low;
            public uint High;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            public short SpecVersion;
            public short DriverVersion;
            public short Size;
            public short DriverExtra;
            public int Fields;
            public int PositionX;
            public int PositionY;
            public int DisplayOrientation;
            public int DisplayFixedOutput;
            public short Color;
            public short Duplex;
            public short YResolution;
            public short TTOption;
            public short Collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FormName;
            public short LogPixels;
            public int BitsPerPel;
            public int PelsWidth;
            public int PelsHeight;
            public int DisplayFlags;
            public int DisplayFrequency;
            public int IcmMethod;
            public int IcmIntent;
            public int MediaType;
            public int DitherType;
            public int Reserved1;
            public int Reserved2;
            public int PanningWidth;
            public int PanningHeight;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string device, int mode, ref DevMode devMode);

        public ulong TotalMemory;
        public ulong AvailableMemory;
        public double MemoryPercent;
        public double CpuPercent;
        public double ToolMemoryMb;
        public double ToolCpuPercent;
        public int ToolThreads;
        public double GameMemoryMb;
        public double GameCpuPercent;
        public int GameThreads;
        public bool GameRunning;
        public int GamePid;
        public string GameTitle = "";
        public bool GameForeground;
        public int ProcessCount;
        public string WindowsName = "Windows";
        public string Uptime = "";
        public double DiskFreePercent;
        public int RefreshRate = 60;

        // ---- GPU / 网络 / 游戏所在磁盘 -------------------------------------
        public bool GpuAvailable;
        public double GpuPercent;
        public double GameGpuPercent;
        public bool NetworkAvailable;
        public double NetworkMbps;
        public double NetworkDownMbps;
        public double NetworkUpMbps;
        public double NetworkLinkMbps;
        public bool DiskCounterAvailable;
        public double DiskBusyPercent;
        public double DiskReadMBps;
        public double DiskWriteMBps;
        public double DiskUsedPercent;
        public double DiskFreeGb;
        public double DiskTotalGb;
        public string DiskRoot = "C:\\";
        public string DiskLabel = "C:";

        // ---- 温度（ACPI 热区；有的机器没有，就保持不可用）-------------------
        public bool TemperatureAvailable;
        public double TemperatureCelsius;
        public string TemperatureSource = "ACPI";

        private ulong _lastIdle;
        private ulong _lastKernel;
        private ulong _lastUser;
        private DateTime _lastSample = DateTime.MinValue;
        private TimeSpan _lastToolCpu;
        private TimeSpan _lastGameCpu;
        private int _gamePid;
        private int _refreshRate;

        private PerformanceCounterCategory _gpuCategory;
        private string _gpuCounterName;
        private DateTime _gpuRetry = DateTime.MinValue;

        private PerformanceCounterCategory _diskCategory;
        private DateTime _diskRetry = DateTime.MinValue;
        private readonly Dictionary<string, CounterSample> _gpuSamples = new Dictionary<string, CounterSample>();
        private readonly Dictionary<string, CounterSample> _diskSamples = new Dictionary<string, CounterSample>();
        private PerformanceCounterCategory _thermalCategory;
        private DateTime _thermalRetry = DateTime.MinValue;

        private readonly List<NetworkInterface> _netInterfaces = new List<NetworkInterface>();
        private DateTime _netScanAt = DateTime.MinValue;
        private bool _netReady;
        private long _lastNetReceived;
        private long _lastNetSent;
        private DateTime _lastNetAt = DateTime.MinValue;

        private string _gameDiskRoot;

        public SystemTelemetry()
        {
            ReadWindowsName();
            ReadRefreshRate();
            Refresh(0);
        }

        public void Refresh(int elapsedMs)
        {
            MemoryStatus mem = new MemoryStatus();
            mem.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            if (GlobalMemoryStatusEx(ref mem))
            {
                TotalMemory = mem.TotalPhys;
                AvailableMemory = mem.AvailPhys;
                MemoryPercent = TotalMemory == 0 ? 0 : (1.0 - AvailableMemory / (double)TotalMemory) * 100.0;
            }

            FileTime idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                ulong i = ToU64(idle);
                ulong k = ToU64(kernel);
                ulong u = ToU64(user);
                if (_lastKernel != 0)
                {
                    ulong di = i - _lastIdle;
                    ulong dk = k - _lastKernel;
                    ulong du = u - _lastUser;
                    ulong total = dk + du;
                    if (total > 0) CpuPercent = Math.Max(0, Math.Min(100, (1.0 - di / (double)total) * 100.0));
                }
                _lastIdle = i;
                _lastKernel = k;
                _lastUser = u;
            }

            ProcessCount = 0;
            try { ProcessCount = Process.GetProcesses().Length; }
            catch { }

            RefreshTool();
            RefreshGame(elapsedMs);
            RefreshDisk();
            RefreshNetwork();
            RefreshGpu();
            RefreshTemperature();
            Uptime = FormatUptime(GetTickCount64());
            _lastSample = DateTime.Now;
        }

        private void RefreshTool()
        {
            try
            {
                Process p = Process.GetCurrentProcess();
                ToolMemoryMb = p.WorkingSet64 / 1048576.0;
                ToolThreads = p.Threads.Count;
                TimeSpan cpu = p.TotalProcessorTime;
                double elapsed = _lastSample == DateTime.MinValue ? 0 : (DateTime.Now - _lastSample).TotalSeconds;
                if (elapsed > 0.2 && _lastToolCpu != TimeSpan.Zero && _lastSample != DateTime.MinValue)
                {
                    double used = (cpu - _lastToolCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100.0;
                    ToolCpuPercent = Math.Max(0, Math.Min(100, used));
                }
                _lastToolCpu = cpu;
            }
            catch { }
        }

        private void RefreshGame(int elapsedMs)
        {
            GameRunning = false;
            GameForeground = false;
            GamePid = 0;
            GameMemoryMb = 0;
            GameCpuPercent = 0;
            GameThreads = 0;
            GameTitle = "";
            GameGpuPercent = 0;

            Process[] all = null;
            try
            {
                all = Process.GetProcesses();
                Process game = null;
                for (int i = 0; i < all.Length; i++)
                {
                    string name;
                    try { name = all[i].ProcessName; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.StartsWith("SCPSLAlert", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        game = all[i];
                        break;
                    }
                }
                if (game != null)
                {
                    GameRunning = true;
                    _gamePid = game.Id;
                    GamePid = game.Id;
                    GameThreads = game.Threads.Count;
                    GameMemoryMb = game.WorkingSet64 / 1048576.0;
                    try { GameTitle = game.MainWindowTitle ?? ""; }
                    catch { }
                    TimeSpan cpu = game.TotalProcessorTime;
                    double elapsed = _lastSample == DateTime.MinValue ? 0 : (DateTime.Now - _lastSample).TotalSeconds;
                    if (elapsed > 0.2 && _lastGameCpu != TimeSpan.Zero && _gamePid == GamePid)
                    {
                        double used = (cpu - _lastGameCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100.0;
                        GameCpuPercent = Math.Max(0, Math.Min(100, used));
                    }
                    _lastGameCpu = cpu;

                    try
                    {
                        string path = game.MainModule.FileName;
                        string root = string.IsNullOrEmpty(path) ? null : Path.GetPathRoot(path);
                        if (!string.IsNullOrEmpty(root)) _gameDiskRoot = root;
                    }
                    catch { }

                    IntPtr fg = GetForegroundWindow();
                    uint pid;
                    GetWindowThreadProcessId(fg, out pid);
                    GameForeground = pid == (uint)game.Id;
                }
            }
            catch { }
            finally
            {
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        try { all[i].Dispose(); } catch { }
                    }
                }
            }
        }

        private void RefreshDisk()
        {
            try
            {
                string root = _gameDiskRoot;
                if (string.IsNullOrEmpty(root)) root = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(root)) root = "C:\\";
                DiskRoot = root;
                string label = root.TrimEnd('\\');
                if (string.IsNullOrEmpty(label)) label = root;
                DiskLabel = label;

                try
                {
                    DriveInfo d = new DriveInfo(root);
                    if (d.IsReady && d.TotalSize > 0)
                    {
                        DiskTotalGb = d.TotalSize / 1073741824.0;
                        DiskFreeGb = d.AvailableFreeSpace / 1073741824.0;
                        DiskFreePercent = d.AvailableFreeSpace / (double)d.TotalSize * 100.0;
                        DiskUsedPercent = 100.0 - DiskFreePercent;
                    }
                }
                catch { }

                RefreshDiskCounters(label);
            }
            catch { }
        }

        private void RefreshDiskCounters(string instance)
        {
            try
            {
                if (_diskCategory == null)
                {
                    if ((DateTime.Now - _diskRetry).TotalSeconds < 30) return;
                    _diskRetry = DateTime.Now;
                    _diskCategory = FindCategory("LogicalDisk", "逻辑磁盘");
                    if (_diskCategory == null) return;
                }

                InstanceDataCollectionCollection data = _diskCategory.ReadCategory();
                double busy = SampleInstance(data, instance,
                    new string[] { "% Disk Time", "% 磁盘时间", "Disk Time" });
                double read = SampleInstance(data, instance,
                    new string[] { "Disk Read Bytes/sec", "磁盘读取字节/秒" });
                double write = SampleInstance(data, instance,
                    new string[] { "Disk Write Bytes/sec", "磁盘写入字节/秒" });

                DiskBusyPercent = Math.Max(0, Math.Min(100, busy));
                DiskReadMBps = Math.Max(0, read) / 1048576.0;
                DiskWriteMBps = Math.Max(0, write) / 1048576.0;
                DiskCounterAvailable = true;
            }
            catch
            {
                DiskCounterAvailable = false;
                _diskCategory = null;
            }
        }

        private void RefreshNetwork()
        {
            try
            {
                if (_netInterfaces.Count == 0 || (DateTime.Now - _netScanAt).TotalSeconds > 10)
                {
                    _netInterfaces.Clear();
                    NetworkInterface[] all = NetworkInterface.GetAllNetworkInterfaces();
                    for (int i = 0; i < all.Length; i++)
                    {
                        NetworkInterface ni = all[i];
                        if (ni.OperationalStatus != OperationalStatus.Up) continue;
                        NetworkInterfaceType t = ni.NetworkInterfaceType;
                        if (t == NetworkInterfaceType.Loopback || t == NetworkInterfaceType.Tunnel) continue;
                        string id = (ni.Name + " " + ni.Description).ToLowerInvariant();
                        if (id.IndexOf("vethernet") >= 0 || id.IndexOf("hyper-v") >= 0
                            || id.IndexOf("vmware") >= 0 || id.IndexOf("virtualbox") >= 0
                            || id.IndexOf("loopback") >= 0 || id.IndexOf("teredo") >= 0
                            || id.IndexOf("isatap") >= 0 || id.IndexOf("bluetooth") >= 0
                            || id.IndexOf("tap-") >= 0 || id.IndexOf("vpn") >= 0)
                            continue;
                        _netInterfaces.Add(ni);
                    }
                    _netScanAt = DateTime.Now;
                    _netReady = false;
                }

                if (_netInterfaces.Count == 0)
                {
                    NetworkAvailable = false;
                    NetworkMbps = 0;
                    NetworkDownMbps = 0;
                    NetworkUpMbps = 0;
                    NetworkLinkMbps = 0;
                    return;
                }

                long received = 0;
                long sent = 0;
                long link = 0;
                for (int i = 0; i < _netInterfaces.Count; i++)
                {
                    try
                    {
                        IPv4InterfaceStatistics st = _netInterfaces[i].GetIPv4Statistics();
                        received += st.BytesReceived;
                        sent += st.BytesSent;
                    }
                    catch { }
                    try
                    {
                        long speed = _netInterfaces[i].Speed;
                        if (speed > 0 && speed < 100000000000L) link += speed;
                    }
                    catch { }
                }
                NetworkLinkMbps = link / 1000000.0;
                NetworkAvailable = _netInterfaces.Count > 0;

                DateTime now = DateTime.Now;
                if (!_netReady)
                {
                    _lastNetReceived = received;
                    _lastNetSent = sent;
                    _lastNetAt = now;
                    _netReady = true;
                    return;
                }

                double seconds = (now - _lastNetAt).TotalSeconds;
                if (seconds < 0.2) return;
                double dr = received - _lastNetReceived;
                double ds = sent - _lastNetSent;
                if (dr < 0) dr = 0;
                if (ds < 0) ds = 0;
                NetworkDownMbps = dr * 8.0 / 1000000.0 / seconds;
                NetworkUpMbps = ds * 8.0 / 1000000.0 / seconds;
                NetworkMbps = NetworkDownMbps + NetworkUpMbps;
                _lastNetReceived = received;
                _lastNetSent = sent;
                _lastNetAt = now;
            }
            catch
            {
                NetworkAvailable = false;
            }
        }

        private void RefreshGpu()
        {
            try
            {
                if (_gpuCategory == null)
                {
                    if ((DateTime.Now - _gpuRetry).TotalSeconds < 30) return;
                    _gpuRetry = DateTime.Now;
                    _gpuCategory = FindCategory("GPU Engine", "GPU 引擎");
                    if (_gpuCategory == null)
                    {
                        GpuAvailable = false;
                        return;
                    }
                }

                InstanceDataCollectionCollection data = _gpuCategory.ReadCategory();
                if (string.IsNullOrEmpty(_gpuCounterName))
                {
                    _gpuCounterName = FindCounterKey(data,
                        new string[] { "Utilization Percentage", "利用率" });
                }
                if (string.IsNullOrEmpty(_gpuCounterName))
                {
                    GpuAvailable = false;
                    return;
                }

                InstanceDataCollection col = data[_gpuCounterName];
                if (col == null)
                {
                    GpuAvailable = false;
                    return;
                }

                double total = 0;
                double game = 0;
                foreach (InstanceData inst in col.Values)
                {
                    string name = inst.InstanceName;
                    if (name == null) continue;
                    if (name.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    CounterSample sample = inst.Sample;
                    CounterSample previous;
                    if (!_gpuSamples.TryGetValue(name, out previous))
                    {
                        _gpuSamples[name] = sample;
                        continue;
                    }
                    _gpuSamples[name] = sample;
                    double value = 0;
                    try { value = CounterSample.Calculate(previous, sample); }
                    catch { continue; }
                    if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) continue;
                    total += value;
                    if (GamePid > 0 && ParsePid(name) == GamePid) game += value;
                }
                if (_gpuSamples.Count > 4000) _gpuSamples.Clear();
                GpuPercent = Math.Max(0, Math.Min(100, total));
                GameGpuPercent = Math.Max(0, Math.Min(100, game));
                GpuAvailable = true;
            }
            catch
            {
                GpuAvailable = false;
                _gpuCategory = null;
            }
        }

        private void RefreshTemperature()
        {
            try
            {
                if (_thermalCategory == null)
                {
                    if ((DateTime.Now - _thermalRetry).TotalSeconds < 30) return;
                    _thermalRetry = DateTime.Now;
                    _thermalCategory = FindCategory("Thermal Zone Information", "热区信息");
                    if (_thermalCategory == null)
                    {
                        TemperatureAvailable = false;
                        return;
                    }
                }

                InstanceDataCollectionCollection data = _thermalCategory.ReadCategory();
                string key = FindCounterKey(data,
                    new string[] { "High Precision Temperature", "Temperature", "温度" });
                if (key == null)
                {
                    TemperatureAvailable = false;
                    return;
                }
                InstanceDataCollection col = data[key];
                if (col == null)
                {
                    TemperatureAvailable = false;
                    return;
                }

                bool precise = key.IndexOf("High Precision", StringComparison.OrdinalIgnoreCase) >= 0;
                double best = double.MinValue;
                foreach (InstanceData inst in col.Values)
                {
                    double raw = 0;
                    try { raw = CounterSample.Calculate(inst.Sample); }
                    catch { continue; }
                    if (double.IsNaN(raw) || double.IsInfinity(raw) || raw <= 0) continue;

                    double celsius;
                    if (precise)
                    {
                        if (raw > 1000) celsius = raw / 10.0 - 273.15;
                        else if (raw > 150) celsius = raw - 273.15;
                        else celsius = raw / 10.0;
                    }
                    else
                    {
                        if (raw > 150) celsius = raw - 273.15;
                        else celsius = raw;
                    }
                    if (celsius < -30 || celsius > 150) continue;
                    if (celsius > best) best = celsius;
                }

                if (best == double.MinValue)
                {
                    TemperatureAvailable = false;
                    return;
                }
                TemperatureCelsius = best;
                TemperatureAvailable = true;
                TemperatureSource = "ACPI";
            }
            catch
            {
                TemperatureAvailable = false;
                _thermalCategory = null;
            }
        }

        private static int ParsePid(string instance)
        {
            int i = instance.IndexOf("_pid_", StringComparison.OrdinalIgnoreCase);
            int start;
            if (i >= 0) start = i + 5;
            else if (instance.StartsWith("pid_", StringComparison.OrdinalIgnoreCase)) start = 4;
            else return 0;
            int end = start;
            while (end < instance.Length && instance[end] >= '0' && instance[end] <= '9') end++;
            int pid;
            if (end > start && int.TryParse(instance.Substring(start, end - start), out pid)) return pid;
            return 0;
        }

        private static PerformanceCounterCategory FindCategory(string exact, string friendly)
        {
            try { return new PerformanceCounterCategory(exact); }
            catch { }
            try
            {
                PerformanceCounterCategory[] all = PerformanceCounterCategory.GetCategories();
                for (int i = 0; i < all.Length; i++)
                {
                    string name = all[i].CategoryName;
                    if (name == null) continue;
                    if (string.Equals(name, friendly, StringComparison.OrdinalIgnoreCase)
                        || name.IndexOf("GPU 引擎", StringComparison.OrdinalIgnoreCase) >= 0)
                        return all[i];
                }
            }
            catch { }
            return null;
        }

        private static string FindCounterKey(InstanceDataCollectionCollection data, string[] needles)
        {
            foreach (string key in data.Keys)
            {
                for (int i = 0; i < needles.Length; i++)
                {
                    if (key.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0) return key;
                }
            }
            return null;
        }

        private double SampleInstance(InstanceDataCollectionCollection data, string instance, string[] needles)
        {
            string key = FindCounterKey(data, needles);
            if (key == null) return 0;
            InstanceDataCollection col = data[key];
            if (col == null) return 0;
            foreach (InstanceData inst in col.Values)
            {
                if (!string.Equals(inst.InstanceName, instance, StringComparison.OrdinalIgnoreCase)) continue;
                CounterSample sample = inst.Sample;
                string id = key + "|" + inst.InstanceName;
                CounterSample previous;
                if (!_diskSamples.TryGetValue(id, out previous))
                {
                    _diskSamples[id] = sample;
                    return 0;
                }
                _diskSamples[id] = sample;
                try
                {
                    double value = CounterSample.Calculate(previous, sample);
                    if (!double.IsNaN(value) && !double.IsInfinity(value)) return value;
                }
                catch { }
                return 0;
            }
            return 0;
        }

        private void ReadWindowsName()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        string product = Convert.ToString(k.GetValue("ProductName"));
                        string build = Convert.ToString(k.GetValue("CurrentBuildNumber"));
                        int b;
                        if (int.TryParse(build, out b) && b >= 22000) product = "Windows 11";
                        WindowsName = product;
                    }
                }
            }
            catch { }
        }

        private void ReadRefreshRate()
        {
            try
            {
                DevMode dm = new DevMode();
                dm.Size = (short)Marshal.SizeOf(typeof(DevMode));
                if (EnumDisplaySettings(null, -1, ref dm) && dm.DisplayFrequency > 0)
                    _refreshRate = dm.DisplayFrequency;
            }
            catch { }
            RefreshRate = _refreshRate > 0 ? _refreshRate : 60;
        }

        public bool KillGame()
        {
            Process[] all = null;
            try
            {
                all = Process.GetProcesses();
                for (int i = 0; i < all.Length; i++)
                {
                    string name;
                    try { name = all[i].ProcessName; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.StartsWith("SCPSLAlert", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { all[i].Kill(); } catch { }
                    }
                }
                return true;
            }
            catch { return false; }
            finally
            {
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        try { all[i].Dispose(); } catch { }
                    }
                }
            }
        }
        public static string FormatUptime(ulong ms)
        {
            TimeSpan t = TimeSpan.FromMilliseconds(ms);
            return (int)t.TotalDays + "d " + t.Hours.ToString("00") + "h " + t.Minutes.ToString("00") + "m";
        }

        private static ulong ToU64(FileTime ft)
        {
            return ((ulong)ft.High << 32) | ft.Low;
        }

        public string MemoryText(double mb)
        {
            if (mb >= 1024) return (mb / 1024.0).ToString("0.0") + " GB";
            return mb.ToString("0") + " MB";
        }
    }
}


