using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 一帧游戏画面的统计结果（由 <see cref="GameFpsMonitor"/> 在后台线程算好）。
    /// </summary>
    internal sealed class GameFpsStats
    {
        public bool HasData;
        public double AverageFps;
        public double LowFps;
        public double FrameMs;
        public double JitterMs;
        public int Version;
        public string Status = "";
    }

    /// <summary>
    /// 用 PresentMon 读 **游戏进程** 的呈现事件，算平均帧率 / 1% low / 帧时间。
    ///
    /// 【为什么不用 CompositionTarget.Rendering】
    /// 那个只能测到本工具自己的 WPF 窗口，关掉/最小化就掉到 0，和游戏无关。
    /// PresentMon 走 ETW，只观察 SCPSL.exe 的 Present()，不注入、不 Hook D3D。
    ///
    /// 【开销】
    /// 只监听一个进程 + 关掉 GPU / 输入 / 显示跟踪（--no_track_*），
    /// 后台线程按秒聚合，不逐帧回调 UI；正常打开性能页时开销可以忽略。
    /// </summary>
    internal sealed class GameFpsMonitor : IDisposable
    {
        private readonly object _sync = new object();

        // 最近 60 秒的每帧间隔（毫秒）。最多留 20000 帧，240fps 也够 60 秒。
        private readonly List<double> _recent = new List<double>(20000);
        // 当前这一秒的帧间隔，用于算 1 秒平均 FPS。
        private readonly List<double> _second = new List<double>(512);
        // 最近 60 个“每秒平均 FPS”，给曲线用。
        private readonly List<double> _history = new List<double>(64);

        private Process _proc;
        private Thread _outThread;
        private Thread _errThread;
        private volatile bool _wanted;
        private volatile bool _alive;
        private volatile bool _fatal;
        private volatile int _generation;
        private bool _stopping;

        private DateTime _lastFrameAt = DateTime.MinValue;
        private DateTime _lastComputeAt = DateTime.MinValue;
        private DateTime _lastAttemptAt = DateTime.MinValue;
        private DateTime _startedAt = DateTime.MinValue;
        private int _gamePid;
        private int _restarts;
        private volatile bool _prestart;
        private static bool _legacySessionCleaned;
        private readonly EtwFpsMonitor _etw = new EtwFpsMonitor();
        private bool _etwActive;
        private int _etwPid;
        private DateTime _etwStartedAt = DateTime.MinValue;

        private double _avgFps;
        private double _lowFps;
        private double _frameMs;
        private double _jitterMs;
        private int _version;
        private string _status = "未启动";

        /// <summary>
        /// 程序启动时先让 PresentMon 按进程名等着。很多游戏是在 PresentMon
        /// 启动之前就已经开始呈现了，后挂上去可能一直抓不到；先等着最稳。
        /// </summary>
        public void StartWaitingForGame()
        {
            if (_alive || _fatal) return;
            _prestart = true;
            _wanted = false;
            _lastAttemptAt = DateTime.MinValue;
            TryStart();
        }

        /// <summary>游戏在不在跑。true 时启动 PresentMon，false 时停掉。</summary>
        public void SetGameRunning(bool running)
        {
            SetGameRunning(running, 0);
        }

        /// <summary>
        /// 游戏在不在跑。pid 是游戏进程 ID；有 PID 时优先按 PID 定向，
        /// 比 --process_name 更可靠（尤其是 PresentMon 启动时游戏已经在跑）。
        /// </summary>
        public void SetGameRunning(bool running, int pid)
        {
            if (running)
            {
                _wanted = true;

                // 首选自己的 ETW 消费者：零注入，也不依赖 PresentMon.exe。
                // 拿到游戏 PID 后直接订阅 DXGI，按主交换链数 Present。
                if (pid > 0 && pid != _etwPid)
                {
                    _etwPid = pid;
                    if (_etw.Start(pid))
                    {
                        _etwActive = true;
                        _etwStartedAt = DateTime.Now;
                        _prestart = false;
                        if (_alive) Stop("ETW 已接管");
                        Logger.On("性能", "ETW DXGI 已接管，游戏 PID=" + pid);
                        return;
                    }

                    _etwActive = false;
                    Logger.On("性能", "ETW 启动失败：" + _etw.Snapshot().Status);
                }

                if (_etwActive && pid == _etwPid)
                {
                    if (_etw.Snapshot().HasData) return;

                    // ETW 会话起来了但 6 秒还没有帧：可能交换链选错/游戏没写
                    // DXGI 事件。别一直卡着，回退 PresentMon。
                    if (_etwStartedAt != DateTime.MinValue
                        && (DateTime.Now - _etwStartedAt).TotalSeconds >= 6)
                    {
                        Logger.On("性能", "ETW 6 秒没有帧，回退 PresentMon");
                        _etwActive = false;
                        _etw.Stop();
                    }
                    else
                    {
                        return;
                    }
                }

                // 预启动的 PresentMon 正在按名字等；游戏刚起来就直接复用它。
                // 如果游戏在预启动之前就已经在跑，则改成按 PID 重开。
                if (_prestart && _alive && _gamePid == 0)
                {
                    bool gameWasAlreadyRunning = false;
                    try
                    {
                        if (pid > 0)
                            gameWasAlreadyRunning = Process.GetProcessById(pid).StartTime < _startedAt;
                    }
                    catch { }

                    _gamePid = pid > 0 ? pid : 0;
                    _prestart = false;
                    _restarts = 0;
                    if (gameWasAlreadyRunning)
                    {
                        Stop("改按 PID 定向");
                        _lastAttemptAt = DateTime.MinValue;
                        TryStart();
                    }
                    return;
                }

                // 换了一局 / 游戏重启：旧的 PresentMon 还在盯旧 PID，必须重开。
                if (pid > 0 && pid != _gamePid)
                {
                    bool wasAlive = _alive;
                    _gamePid = pid;
                    _restarts = 0;
                    _fatal = false;
                    _lastAttemptAt = DateTime.MinValue;
                    if (wasAlive) Stop("切换游戏进程");
                }
                else if (pid > 0)
                {
                    _gamePid = pid;
                }

                if (_alive || _fatal)
                {
                    // PresentMon 有时会启动成功但一直不输出表头（没抓到目标进程）。
                    // 10 秒还没有表头就重开一次，并把 stderr 留在日志里。
                    if (_alive && _startedAt != DateTime.MinValue
                        && (DateTime.Now - _startedAt).TotalSeconds >= 10
                        && IsStartingStatus())
                    {
                        if (_restarts < 1)
                        {
                            _restarts++;
                            Logger.On("性能", "PresentMon 10 秒没有输出表头，改用进程 ID 重试");
                            Stop("重新启动 PresentMon");
                            _lastAttemptAt = DateTime.MinValue;
                            TryStart();
                        }
                        else if (IsStartingStatus())
                        {
                            SetStatus("PresentMon 无输出，请查看日志");
                        }
                    }
                    return;
                }

                // 启动失败（比如没管理员权限）不要每秒重试，30 秒后再试。
                if ((DateTime.Now - _lastAttemptAt).TotalSeconds < 30) return;
                _lastAttemptAt = DateTime.Now;
                TryStart();
            }
            else
            {
                _wanted = false;
                _fatal = false;
                _gamePid = 0;
                _restarts = 0;
                _etwActive = false;
                _etwPid = 0;
                _etwStartedAt = DateTime.MinValue;
                _etw.Stop();

                // 游戏退出后不要彻底停掉：重新按进程名等下一局，
                // 这样下一局开始时 PresentMon 已经在跑了。
                if (!_prestart)
                {
                    _prestart = true;
                    if (_alive) Stop("等待下一局");
                    _lastAttemptAt = DateTime.MinValue;
                    TryStart();
                }
            }
        }

        private bool IsStartingStatus()
        {
            lock (_sync)
            {
                return !string.IsNullOrEmpty(_status)
                    && _status.StartsWith("正在启动", StringComparison.Ordinal);
            }
        }

        private void TryStart()
        {
            string exe = FindPresentMon();
            if (exe == null)
            {
                SetStatus("未找到 PresentMon.exe");
                return;
            }

            CleanupLegacySession();

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                // 有 PID 就按 PID 定向：游戏通常比 PresentMon 早启动，
                // --process_name 在“先有游戏、后开 PresentMon”时可能一直抓不到。
                string target = _gamePid > 0
                    ? "--process_id " + _gamePid.ToString(CultureInfo.InvariantCulture)
                    : "--process_name SCPSL.exe";
                // 每次都用新的 ETW 会话名。固定名字一旦上次 PresentMon 被强杀，
                // 会话会残留在系统里，下一次启动直接 0xC0000035（名字冲突），
                // 表现为进程活着但永远不输出 CSV。
                string session = "SCPSLAlertFps_"
                    + (_gamePid > 0 ? _gamePid.ToString(CultureInfo.InvariantCulture) : "0")
                    + "_" + unchecked((uint)Environment.TickCount).ToString("X8", CultureInfo.InvariantCulture);
                // v1 输出更简单、兼容性最好；不去关 GPU/输入/显示跟踪，
                // 避免某些 2.6 版本把呈现事件一起关掉。
                psi.Arguments = target + " --output_stdout --no_console_stats --v1_metrics"
                    + " --session_name " + session;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                // PresentMon 的控制台输出是 UTF-16LE（带 BOM），不设这个会读成乱码。
                psi.StandardOutputEncoding = Encoding.Unicode;
                psi.StandardErrorEncoding = Encoding.Unicode;

                Process p = Process.Start(psi);
                if (p == null)
                {
                    SetStatus("PresentMon 启动失败");
                    return;
                }

                _proc = p;
                _alive = true;
                _stopping = false;
                _startedAt = DateTime.Now;
                int gen;
                lock (_sync) { _generation++; gen = _generation; }
                SetStatus("正在启动 PresentMon…");
                Logger.On("性能", "PresentMon 启动：" + exe + " " + psi.Arguments);

                _outThread = new Thread(new ThreadStart(delegate { ReadStdout(gen); }));
                _outThread.IsBackground = true;
                _outThread.Name = "GameFps-Out";
                _outThread.Start();

                _errThread = new Thread(new ThreadStart(delegate { ReadStderr(gen); }));
                _errThread.IsBackground = true;
                _errThread.Name = "GameFps-Err";
                _errThread.Start();
            }
            catch (Exception ex)
            {
                SetStatus("PresentMon 启动失败：" + Short(ex.Message));
            }
        }

        private static string FindPresentMon()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string exact = Path.Combine(dir, "PresentMon.exe");
                if (File.Exists(exact)) return exact;

                // 兼容直接把带版本号的文件丢进来。
                string[] cands = Directory.GetFiles(dir, "PresentMon-*.exe");
                if (cands != null && cands.Length > 0)
                {
                    Array.Sort(cands, StringComparer.OrdinalIgnoreCase);
                    return cands[cands.Length - 1];
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 旧版本固定用 SCPSLAlertGameFps 这个 ETW 会话名；如果上次被强杀，
        /// 会话会残留在系统里，之后每次启动都会 0xC0000035（名字冲突）。
        /// 这里只清理这一个旧名字，新会话用唯一名，不会互相影响。
        /// </summary>
        private static void CleanupLegacySession()
        {
            if (_legacySessionCleaned) return;
            _legacySessionCleaned = true;
            try
            {
                string logman = Path.Combine(Environment.SystemDirectory, "logman.exe");
                if (!File.Exists(logman)) return;
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = logman;
                psi.Arguments = "stop SCPSLAlertGameFps -ets";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process p = Process.Start(psi);
                if (p == null) return;
                if (!p.WaitForExit(3000))
                {
                    try { p.Kill(); } catch { }
                }
                else if (p.ExitCode == 0)
                {
                    Logger.On("性能", "已清理残留的旧 ETW 会话 SCPSLAlertGameFps");
                }
                try { p.Dispose(); } catch { }
            }
            catch { }
        }

        private void ReadStdout(int gen)
        {
            Process proc = _proc;
            int msCol = -1;
            bool sawHeader = false;
            try
            {
                StreamReader r = proc.StandardOutput;
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    if (line[0] == '\uFEFF') line = line.Substring(1);

                    if (!sawHeader)
                    {
                        // PresentMon 2.x 默认输出 v2 表头，帧间隔列叫 FrameTime；
                        // 只有 --v1_metrics 才是旧的 MsBetweenPresents。两套都要认，
                        // 否则 2.6 会一直卡在“正在启动 PresentMon…”。
                        if (line.IndexOf("Application", StringComparison.OrdinalIgnoreCase) >= 0
                            && (line.IndexOf("MsBetweenPresents", StringComparison.OrdinalIgnoreCase) >= 0
                                || line.IndexOf("FrameTime", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            string[] h = SplitCsv(line);
                            int frameTimeCol = -1;
                            for (int i = 0; i < h.Length; i++)
                            {
                                string name = h[i].Trim();
                                if (name.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("MsBetweenDisplayChange", StringComparison.OrdinalIgnoreCase))
                                {
                                    msCol = i;
                                    break;
                                }
                                if (name.Equals("FrameTime", StringComparison.OrdinalIgnoreCase))
                                    frameTimeCol = i;
                            }
                            if (msCol < 0) msCol = frameTimeCol;
                            if (msCol >= 0)
                            {
                                sawHeader = true;
                                SetStatus("采集中");
                                Logger.On("性能", "PresentMon 表头已识别："
                                    + (frameTimeCol == msCol ? "v2 FrameTime" : "v1 MsBetweenPresents")
                                    + "（第 " + (msCol + 1) + " 列）");
                            }
                        }
                        continue;
                    }

                    string[] c = SplitCsv(line);
                    if (msCol >= c.Length) continue;
                    double ms;
                    if (!double.TryParse(c[msCol].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out ms)) continue;
                    if (ms <= 0.01 || ms > 1000) continue;

                    DateTime now = DateTime.Now;
                    lock (_sync)
                    {
                        _recent.Add(ms);
                        if (_recent.Count > 20000) _recent.RemoveAt(0);
                        _second.Add(ms);
                        _lastFrameAt = now;

                        if (_lastComputeAt == DateTime.MinValue) _lastComputeAt = now;
                        if ((now - _lastComputeAt).TotalSeconds >= 1.0)
                        {
                            ComputeLocked(now);
                            _lastComputeAt = now;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_stopping)
                {
                    SetStatus("PresentMon 读取失败：" + Short(ex.Message));
                    Logger.On("性能", "PresentMon 读取失败：" + Short(ex.Message));
                }
            }
            finally
            {
                if (gen == _generation)
                {
                    _alive = false;
                    int code = -1;
                    try { if (proc.HasExited) code = proc.ExitCode; }
                    catch { }
                    if (code == 6)
                    {
                        // PresentMon 在缺少管理员权限 / Performance Log Users 时退出码 6。
                        // 不要每 30 秒反复拉起一个注定失败的进程；下一次检测到游戏重新开始时再试。
                        _fatal = true;
                        SetStatus("需要管理员权限：请重启 SCPSLAlert");
                        Logger.On("性能", "PresentMon 退出：需要管理员权限，请重启 SCPSLAlert");
                    }
                    else if (_wanted && !_stopping)
                    {
                        Logger.On("性能", "PresentMon 已退出：代码 " + code);
                        // 只在"还在正常启动/采集"时改状态；不要盖掉 stderr 给出的
                        // "需要管理员权限"这种明确错误。
                        lock (_sync)
                        {
                            if (_status == "采集中" || _status.StartsWith("正在启动", StringComparison.Ordinal))
                                _status = code > 0
                                    ? "PresentMon 已退出（代码 " + code + "）"
                                    : "PresentMon 已退出（等待重试）";
                        }
                    }
                }
            }
        }

        private void ReadStderr(int gen)
        {
            try
            {
                Process proc = _proc;
                if (proc == null) return;
                StreamReader r = proc.StandardError;
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    string s = line.Trim();
                    if (s.Length == 0) continue;
                    if (gen == _generation)
                        Logger.On("性能", "PresentMon stderr：" + Short(s));
                    if (s.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("administrative", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("elevated", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("权限", StringComparison.Ordinal) >= 0)
                    {
                        if (gen == _generation) SetStatus("需要管理员权限");
                    }
                    else if (s.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (gen == _generation) SetStatus("PresentMon：" + Short(s));
                    }
                }
            }
            catch { }
        }

        private void ComputeLocked(DateTime now)
        {
            if (_second.Count == 0) return;

            double sum = 0;
            for (int i = 0; i < _second.Count; i++) sum += _second[i];
            double avgMs = sum / _second.Count;
            _second.Clear();

            if (avgMs > 0)
            {
                _avgFps = 1000.0 / avgMs;
                _history.Add(_avgFps);
                while (_history.Count > 60) _history.RemoveAt(0);
                _version++;
            }

            // 最近 60 秒的 1% low / 平均帧时间 / 抖动
            if (_recent.Count > 0)
            {
                List<double> sorted = new List<double>(_recent);
                sorted.Sort();
                int idx = (int)(sorted.Count * 0.99);
                if (idx >= sorted.Count) idx = sorted.Count - 1;
                double p99 = sorted[idx];
                _lowFps = p99 > 0 ? 1000.0 / p99 : _avgFps;

                double all = 0;
                for (int i = 0; i < _recent.Count; i++) all += _recent[i];
                _frameMs = all / _recent.Count;

                double var = 0;
                for (int i = 0; i < _recent.Count; i++)
                {
                    double d = _recent[i] - _frameMs;
                    var += d * d;
                }
                _jitterMs = Math.Sqrt(var / _recent.Count);
            }

            _lastFrameAt = now;
        }

        private void SetStatus(string s)
        {
            lock (_sync) _status = s == null ? "" : s;
        }

        private void Stop(string status)
        {
            _stopping = true;
            lock (_sync) { _generation++; }
            Process p;
            lock (_sync) { p = _proc; _proc = null; }
            if (p != null)
            {
                try { if (!p.HasExited) p.Kill(); } catch { }
                try { p.Dispose(); } catch { }
            }
            _alive = false;
            lock (_sync)
            {
                _recent.Clear();
                _second.Clear();
                _history.Clear();
                _avgFps = 0;
                _lowFps = 0;
                _frameMs = 0;
                _jitterMs = 0;
                _lastFrameAt = DateTime.MinValue;
                _lastComputeAt = DateTime.MinValue;
                _version++;
                _status = status == null ? "" : status;
            }
            _stopping = false;
        }

        /// <summary>取一份当前统计，WPF 每秒读一次。</summary>
        public GameFpsStats Snapshot()
        {
            if (_etwActive)
            {
                GameFpsStats e = _etw.Snapshot();
                if (e.HasData)
                {
                    lock (_sync)
                    {
                        _avgFps = e.AverageFps;
                        _lowFps = e.LowFps;
                        _frameMs = e.FrameMs;
                        _jitterMs = 0;
                        _lastFrameAt = DateTime.Now;
                        _history.Add(e.AverageFps);
                        while (_history.Count > 60) _history.RemoveAt(0);
                        _version++;
                    }
                }
                return e;
            }

            GameFpsStats s = new GameFpsStats();
            lock (_sync)
            {
                bool fresh = _lastFrameAt != DateTime.MinValue
                    && (DateTime.Now - _lastFrameAt).TotalSeconds <= 2.5;
                s.HasData = fresh && _avgFps > 0;
                s.AverageFps = fresh ? _avgFps : 0;
                s.LowFps = fresh ? _lowFps : 0;
                s.FrameMs = fresh ? _frameMs : 0;
                s.JitterMs = fresh ? _jitterMs : 0;
                s.Version = _version;
                s.Status = _status;
            }

            // PresentMon 没数据时，退回到 RTSS 共享内存。
            // RTSS 自己注入游戏，我们只读它写好的共享内存，不碰游戏进程。
            if (!s.HasData)
            {
                RtssFpsStats rt = RtssFpsReader.Read(_gamePid);
                if (rt.HasData)
                {
                    s.HasData = true;
                    s.AverageFps = rt.Fps;
                    s.LowFps = rt.LowFps;
                    s.FrameMs = rt.FrameMs;
                    s.JitterMs = 0;
                    s.Status = rt.Status;

                    lock (_sync)
                    {
                        _avgFps = rt.Fps;
                        _lowFps = rt.LowFps;
                        _frameMs = rt.FrameMs;
                        _jitterMs = 0;
                        _lastFrameAt = DateTime.Now;
                        _history.Add(rt.Fps);
                        while (_history.Count > 60) _history.RemoveAt(0);
                        _version++;
                    }
                }
                else if (string.IsNullOrEmpty(s.Status)
                    || s.Status.StartsWith("正在启动", StringComparison.Ordinal))
                {
                    s.Status = rt.Status;
                }
            }
            return s;
        }

        /// <summary>最近 60 个每秒平均 FPS（旧 → 新）。</summary>
        public double[] HistorySnapshot()
        {
            lock (_sync) return _history.ToArray();
        }

        public void Dispose()
        {
            _wanted = false;
            _etwActive = false;
            _etwPid = 0;
            _etwStartedAt = DateTime.MinValue;
            _etw.Dispose();
            Stop("未启动");
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= 80 ? s : s.Substring(0, 80) + "…";
        }

        private static string[] SplitCsv(string line)
        {
            List<string> parts = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool quote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else quote = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') quote = true;
                    else if (c == ',')
                    {
                        parts.Add(sb.ToString());
                        sb.Length = 0;
                    }
                    else sb.Append(c);
                }
            }
            parts.Add(sb.ToString());
            return parts.ToArray();
        }
    }
}
