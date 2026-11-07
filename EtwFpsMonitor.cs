using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 自己消费 Microsoft-Windows-DXGI 的 ETW 事件，不注入游戏。
    ///
    /// 只统计目标进程的 Present Start 事件（DXGI 事件 ID 178，必要时回退 184），
    /// 用事件到达时间算每秒帧数。和 PresentMon 走的是同一套 ETW provider，
    /// 但进程过滤、会话生命周期都由我们控制，不依赖 PresentMon.exe。
    /// </summary>
    internal sealed unsafe class EtwFpsMonitor : IDisposable
    {
        private static readonly Guid DxgiProvider = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");

        private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        private const uint EVENT_CONTROL_CODE_DISABLE_PROVIDER = 0;
        private const uint EVENT_TRACE_CONTROL_STOP = 1;

        private readonly object _sync = new object();
        private readonly List<double> _recent = new List<double>(2048);

        private int _pid;
        private string _sessionName = "";
        private IntPtr _propsPtr = IntPtr.Zero;
        private ulong _traceHandle;
        private Thread _thread;
        private EventTraceEventCallback _callback;
        private EVENT_TRACE_LOGFILEW _logfile;
        private volatile bool _running;

        private long _secondStart;
        private int _frames;
        private double _fps;
        private double _lowFps;
        private double _frameMs;
        private long _lastEventTick;
        private long _lastTimeStamp;
        private string _status = "未启动";

        public GameFpsStats Snapshot()
        {
            GameFpsStats s = new GameFpsStats();
            lock (_sync)
            {
                bool fresh = _lastEventTick != 0
                    && (Stopwatch.GetTimestamp() - _lastEventTick) * 1000.0 / Stopwatch.Frequency <= 2500;
                s.HasData = fresh && _fps > 0;
                s.AverageFps = fresh ? _fps : 0;
                s.LowFps = fresh ? _lowFps : 0;
                s.FrameMs = fresh ? _frameMs : 0;
                s.JitterMs = 0;
                s.Status = _status;
            }
            return s;
        }

        public bool Start(int pid)
        {
            Stop();
            if (pid <= 0)
            {
                SetStatus("ETW 没有游戏 PID");
                return false;
            }

            _pid = pid;
            _sessionName = "SCPSLAlertEtw_" + pid.ToString() + "_"
                + unchecked((uint)Environment.TickCount).ToString("X8");
            _callback = new EventTraceEventCallback(OnEvent);
            _running = true;

            int propsSize = Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES));
            int nameBytes = (_sessionName.Length + 1) * 2;
            _propsPtr = Marshal.AllocHGlobal(propsSize + nameBytes);
            for (int i = 0; i < propsSize + nameBytes; i++) Marshal.WriteByte(_propsPtr, i, 0);

            EVENT_TRACE_PROPERTIES props = new EVENT_TRACE_PROPERTIES();
            props.Wnode.BufferSize = (uint)(propsSize + nameBytes);
            props.Wnode.Flags = WNODE_FLAG_TRACED_GUID;
            props.Wnode.ClientContext = 1; // QPC
            props.BufferSize = 64;
            props.MinimumBuffers = 4;
            props.MaximumBuffers = 16;
            props.LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
            props.FlushTimer = 1;
            props.LoggerNameOffset = (uint)propsSize;
            Marshal.StructureToPtr(props, _propsPtr, false);
            Marshal.Copy(_sessionName.ToCharArray(), 0, IntPtr.Add(_propsPtr, propsSize), _sessionName.Length);

            ulong sessionHandle;
            int err = StartTraceW(out sessionHandle, _sessionName, (EVENT_TRACE_PROPERTIES*)_propsPtr);
            if (err != 0)
            {
                // 5 = ERROR_ACCESS_DENIED。程序 manifest 默认要求管理员，
                // 正常不会出现；出现时重启 SCPSLAlert 即可。
                SetStatus(err == 5
                    ? "需要管理员权限：请重启 SCPSLAlert"
                    : "ETW 启动失败：" + err);
                Cleanup();
                return false;
            }

            ENABLE_TRACE_PARAMETERS enable = new ENABLE_TRACE_PARAMETERS();
            enable.Version = 1;
            Guid provider = DxgiProvider;
            err = EnableTraceEx2(sessionHandle, ref provider, EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                4, ulong.MaxValue, 0, 0, ref enable);
            if (err != 0)
            {
                SetStatus(err == 5
                    ? "需要管理员权限：请重启 SCPSLAlert"
                    : "ETW 启用 DXGI 失败：" + err);
                Cleanup();
                return false;
            }

            _logfile = new EVENT_TRACE_LOGFILEW();
            _logfile.LoggerName = _sessionName;
            _logfile.LogFileMode = EVENT_TRACE_REAL_TIME_MODE | PROCESS_TRACE_MODE_EVENT_RECORD;
            _logfile.EventCallback = _callback;
            _traceHandle = OpenTrace(ref _logfile);
            if (_traceHandle == ulong.MaxValue)
            {
                SetStatus("ETW 打开失败：" + Marshal.GetLastWin32Error());
                Cleanup();
                return false;
            }

            _thread = new Thread(ProcessLoop);
            _thread.IsBackground = true;
            _thread.Name = "EtwFps";
            _thread.Start();
            SetStatus("ETW 采集中");
            return true;
        }

        private void ProcessLoop()
        {
            try
            {
                ProcessTrace(new ulong[] { _traceHandle }, 1, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
            if (_running) SetStatus("ETW 已停止");
        }

        private unsafe void OnEvent(EVENT_RECORD* record)
        {
            if (record == null || !_running) return;
            EVENT_HEADER h = record->EventHeader;
            if (h.ProcessId != _pid) return;
            if (h.ProviderId != DxgiProvider) return;

            // 只数主交换链的 Present Start（178）。184 是同一帧的
            // multiplane overlay 事件，186 还会出现两次，混在一起会把 FPS 翻倍。
            if (h.Id != 178) return;

            // 事件 UserData 的第一个字段是交换链地址。一个进程可能同时有
            // 游戏主链和覆盖层链，固定跟第一个（通常是主链），避免重复计数。
            if (record->UserData != IntPtr.Zero)
            {
                ulong swapchain = *(ulong*)record->UserData;
                if (swapchain != 0)
                {
                    if (_swapchain == 0) _swapchain = swapchain;
                    else if (swapchain != _swapchain) return;
                }
            }

            long now = Stopwatch.GetTimestamp();
            long ts = h.TimeStamp;
            lock (_sync)
            {
                if (_secondStart == 0) _secondStart = now;
                _frames++;

                if (_lastTimeStamp != 0 && ts > _lastTimeStamp)
                {
                    double ms = (ts - _lastTimeStamp) * 1000.0 / Stopwatch.Frequency;
                    if (ms > 0.01 && ms < 1000)
                    {
                        _recent.Add(ms);
                        if (_recent.Count > 2048) _recent.RemoveAt(0);
                    }
                }
                _lastTimeStamp = ts;
                _lastEventTick = now;

                double elapsed = (now - _secondStart) * 1000.0 / Stopwatch.Frequency;
                if (elapsed >= 1000)
                {
                    _fps = _frames * 1000.0 / elapsed;
                    _frames = 0;
                    _secondStart = now;

                    if (_recent.Count > 0)
                    {
                        List<double> sorted = new List<double>(_recent);
                        sorted.Sort();
                        int idx = (int)(sorted.Count * 0.99);
                        if (idx >= sorted.Count) idx = sorted.Count - 1;
                        double p99 = sorted[idx];
                        _lowFps = p99 > 0 ? 1000.0 / p99 : _fps;

                        double sum = 0;
                        for (int i = 0; i < _recent.Count; i++) sum += _recent[i];
                        _frameMs = sum / _recent.Count;
                    }
                    else
                    {
                        _lowFps = _fps;
                        _frameMs = 1000.0 / _fps;
                    }
                    _status = "ETW DXGI";
                }
            }
        }

        private ulong _swapchain;

        private void SetStatus(string s)
        {
            lock (_sync) _status = s == null ? "" : s;
        }

        public void Stop()
        {
            _running = false;
            try
            {
                if (_traceHandle != 0 && _traceHandle != ulong.MaxValue)
                    CloseTrace(_traceHandle);
            }
            catch { }
            _traceHandle = 0;

            try
            {
                if (_thread != null) _thread.Join(1500);
            }
            catch { }
            _thread = null;

            Cleanup();
            lock (_sync)
            {
                _secondStart = 0;
                _frames = 0;
                _fps = 0;
                _lowFps = 0;
                _frameMs = 0;
                _lastEventTick = 0;
                _lastTimeStamp = 0;
                _recent.Clear();
                _swapchain = 0;
                _status = "未启动";
            }
        }

        private void Cleanup()
        {
            try
            {
                if (_propsPtr != IntPtr.Zero && !string.IsNullOrEmpty(_sessionName))
                {
                    ControlTrace(0, _sessionName, (EVENT_TRACE_PROPERTIES*)_propsPtr, EVENT_TRACE_CONTROL_STOP);
                }
            }
            catch { }
            try
            {
                if (_propsPtr != IntPtr.Zero) Marshal.FreeHGlobal(_propsPtr);
            }
            catch { }
            _propsPtr = IntPtr.Zero;
        }

        public void Dispose()
        {
            Stop();
        }

        #region ETW native

        private delegate bool EventTraceBufferCallback(IntPtr logfile);
        private delegate void EventTraceEventCallback(EVENT_RECORD* record);

        [StructLayout(LayoutKind.Sequential)]
        private struct WNODE_HEADER
        {
            public uint BufferSize;
            public uint ProviderId;
            public ulong HistoricalContext;
            public ulong TimeStamp;
            public Guid Guid;
            public uint ClientContext;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE_PROPERTIES
        {
            public WNODE_HEADER Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE_HEADER
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public byte Type;
            public byte Level;
            public ushort Version;
            public int ThreadId;
            public int ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public int KernelTime;
            public int UserTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ETW_BUFFER_CONTEXT
        {
            public byte ProcessorNumber;
            public byte Alignment;
            public ushort LoggerId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_HEADER
        {
            public ushort Size;
            public ushort HeaderType;
            public ushort Flags;
            public ushort EventProperty;
            public int ThreadId;
            public int ProcessId;
            public long TimeStamp;
            public Guid ProviderId;
            public ushort Id;
            public byte Version;
            public byte Channel;
            public byte Level;
            public byte Opcode;
            public ushort Task;
            public ulong Keyword;
            public int KernelTime;
            public int UserTime;
            public Guid ActivityId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_RECORD
        {
            public EVENT_HEADER EventHeader;
            public ETW_BUFFER_CONTEXT BufferContext;
            public ushort ExtendedDataCount;
            public ushort UserDataLength;
            public IntPtr ExtendedData;
            public IntPtr UserData;
            public IntPtr UserContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE
        {
            public EVENT_TRACE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public int MofLength;
            public ETW_BUFFER_CONTEXT BufferContext;
        }

        [StructLayout(LayoutKind.Sequential, Size = 0xac, CharSet = CharSet.Unicode)]
        private struct TIME_ZONE_INFORMATION
        {
            public uint Bias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string StandardName;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U2, SizeConst = 8)]
            public ushort[] StandardDate;
            public uint StandardBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DaylightName;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U2, SizeConst = 8)]
            public ushort[] DaylightDate;
            public uint DaylightBias;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public uint StartBuffers;
            public uint PointerSize;
            public uint EventsLost;
            public uint CpuSpeedInMHz;
            public IntPtr LoggerName;
            public IntPtr LogFileName;
            public TIME_ZONE_INFORMATION TimeZone;
            public long BootTime;
            public long PerfFreq;
            public long StartTime;
            public uint ReservedFlags;
            public uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct EVENT_TRACE_LOGFILEW
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string LogFileName;
            [MarshalAs(UnmanagedType.LPWStr)] public string LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public uint LogFileMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER LogfileHeader;
            public EventTraceBufferCallback BufferCallback;
            public int BufferSize;
            public int Filled;
            public int EventsLost;
            public EventTraceEventCallback EventCallback;
            public int IsKernelTrace;
            public IntPtr Context;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ENABLE_TRACE_PARAMETERS
        {
            public uint Version;
            public uint EnableProperty;
            public uint ControlFlags;
            public Guid SourceId;
            public IntPtr EnableFilterDesc;
            public int FilterDescCount;
        }

        [DllImport("advapi32.dll", EntryPoint = "OpenTraceW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ulong OpenTrace(ref EVENT_TRACE_LOGFILEW logfile);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int CloseTrace(ulong traceHandle);

        [DllImport("advapi32.dll", EntryPoint = "StartTraceW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int StartTraceW(out ulong sessionHandle, string sessionName,
            EVENT_TRACE_PROPERTIES* properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int EnableTraceEx2(ulong traceHandle, ref Guid providerId,
            uint controlCode, byte level, ulong matchAnyKeyword, ulong matchAllKeyword,
            int timeout, ref ENABLE_TRACE_PARAMETERS enableParameters);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int ControlTrace(ulong sessionHandle, string sessionName,
            EVENT_TRACE_PROPERTIES* properties, uint controlCode);

        #endregion
    }
}
