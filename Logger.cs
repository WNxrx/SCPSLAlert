using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 程序自己的运行日志（排障用）。
    ///
    /// 【为什么必须是异步的】
    /// 热键走的是低级键盘钩子（WH_KEYBOARD_LL）。Windows 对这个回调有硬性
    /// 超时（默认 300ms），一旦超时就把它**静默摘掉** —— 表现就是
    /// “所有快捷键突然全部失效，只能重启程序”。所以钩子回调所在的线程
    /// 绝对不能碰磁盘。这里把日志丢进队列，由后台线程慢慢写。
    ///
    /// 其它特性：
    ///   · 完整日志保存在 SCPSLAlert.log，并按 OCR / 票数 / 程序操作分类副本；
    ///   · 日志超过上限时自动删掉最旧的一半（保留最新内容，绝不无限占硬盘）；
    ///   · 任何情况下都不抛异常 —— 日志写不进去也绝不能拖垮主功能；
    ///   · 队列有上限，极端情况下宁可丢日志也不拖慢程序。
    /// </summary>
    internal static class Logger
    {
        /// <summary>单个日志文件的上限：5MB。</summary>
        public const long MaxBytes = 5L * 1024 * 1024;

        /// <summary>
        /// 时间日志（只记回合/刷新/校准/倒计时这些时间相关的行）单个文件上限：2MB。
        /// 它比主日志小得多、又只收时间行，所以同样的体积能留下长得多的时间记录 ——
        /// 主日志 5MB 轮转时会把刷新数据一起裁掉（实测就发生过）。
        /// </summary>
        public const long TimeMaxBytes = 2L * 1024 * 1024;

        /// <summary>待写队列上限，超了就丢最旧的。</summary>
        private const int MaxQueue = 4000;

        /// <summary>时间日志的队列上限。</summary>
        private const int MaxTimeQueue = 2000;

        private static readonly object _lock = new object();
        private static readonly object _fileLock = new object();
        private static readonly Queue<string> _queue = new Queue<string>();
        private static readonly Queue<string> _timeQueue = new Queue<string>();
        private static readonly Queue<string> _ocrQueue = new Queue<string>();
        private static readonly Queue<string> _ticketQueue = new Queue<string>();
        private static readonly Queue<string> _opsQueue = new Queue<string>();
        private static readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private static readonly UTF8Encoding _utf8 = new UTF8Encoding(false);

        private static string _path = "";
        private static string _timePath = "";
        private static string _ocrPath = "";
        private static string _ticketPath = "";
        private static string _opsPath = "";
        private static bool _enabled;
        private static long _written;
        private static long _timeWritten;
        private static long _ocrWritten, _ticketWritten, _opsWritten;
        private static int _dropped;
        private static int _timeDropped;
        private static int _ocrDropped, _ticketDropped, _opsDropped;
        private static Thread _worker;

        /// <summary>
        /// 已经入队、但还没真正写进文件的行数。
        /// Flush 必须等它归零 —— 队列被取走（清空）不等于已经落盘。
        /// </summary>
        private static int _pending;
        private static int _timePending;
        private static int _ocrPending, _ticketPending, _opsPending;

        /// <summary>当前日志文件路径（设置窗口里“打开日志”用）。</summary>
        public static string Path
        {
            get { lock (_lock) { return _path; } }
        }

        /// <summary>时间日志的路径（只记时间相关的行）。</summary>
        public static string TimePath
        {
            get { lock (_lock) { return _timePath; } }
        }

        public static string OcrPath { get { lock (_lock) { return _ocrPath; } } }
        public static string TicketPath { get { lock (_lock) { return _ticketPath; } } }
        public static string OperationsPath { get { lock (_lock) { return _opsPath; } } }

        public static bool Enabled
        {
            get { lock (_lock) { return _enabled; } }
        }

        /// <summary>被丢掉的行数（队列溢出时）。</summary>
        public static int Dropped
        {
            get { lock (_lock) { return _dropped; } }
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 启动时调一次。enabled 来自 config.ini 的 EnableLog。
        /// 文件已经超过 5MB 就直接删掉重开。
        /// </summary>
        public static void Init(string path, bool enabled)
        {
            try
            {
                lock (_lock)
                {
                    _path = path;
                    _enabled = enabled;
                    _written = 0;
                    _timeWritten = 0;

                    // 时间日志跟主日志同目录、同前缀，一眼能看出是一家
                    _timePath = "";
                    _ocrPath = _ticketPath = _opsPath = "";
                    try
                    {
                        string dir = System.IO.Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            _timePath = System.IO.Path.Combine(dir, "SCPSLAlert.时间.log");
                            _ocrPath = System.IO.Path.Combine(dir, "SCPSLAlert.OCR.log");
                            _ticketPath = System.IO.Path.Combine(dir, "SCPSLAlert.票数.log");
                            _opsPath = System.IO.Path.Combine(dir, "SCPSLAlert.程序操作.log");
                        }
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(path);
                            if (fi.Length > MaxBytes) _written = TrimOldest(path, MaxBytes);
                            else _written = fi.Length;
                        }
                        catch { }
                    }

                    _ocrWritten = ExistingLength(_ocrPath, MaxBytes);
                    _ticketWritten = ExistingLength(_ticketPath, TimeMaxBytes);
                    _opsWritten = ExistingLength(_opsPath, MaxBytes);

                    if (!string.IsNullOrEmpty(_timePath) && File.Exists(_timePath))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(_timePath);
                            if (fi.Length > TimeMaxBytes) _timeWritten = TrimOldest(_timePath, TimeMaxBytes);
                            else _timeWritten = fi.Length;
                        }
                        catch { }
                    }
                }
                EnsureWorker();
            }
            catch { }
        }

        private static void EnsureWorker()
        {
            lock (_lock)
            {
                if (_worker != null) return;
                _worker = new Thread(Worker);
                _worker.IsBackground = true;      // 进程退出就跟着结束，不挡着关程序
                _worker.Name = "SCPSLAlert.Logger";
                try { _worker.Start(); } catch { _worker = null; }
            }
        }

        /// <summary>运行中切换开关（设置窗口里勾选时调用）。</summary>
        public static void SetEnabled(bool enabled)
        {
            lock (_lock) { _enabled = enabled; }
        }

        /// <summary>记一条普通日志（时间相关的行会同时进时间日志）。</summary>
        public static void On(string tag, string message)
        {
            string body = "[" + tag + "] " + message;
            Write(body);
            if (tag == "OCR" || tag == "播报" || tag == "复活" || tag == "预览")
                WriteSplit(_ocrQueue, ref _ocrPending, ref _ocrDropped, body);
            else if (tag == "票数")
                WriteSplit(_ticketQueue, ref _ticketPending, ref _ticketDropped, body);
            else
                WriteSplit(_opsQueue, ref _opsPending, ref _opsDropped, body);
            if (IsTimeLine(tag, message)) WriteTime("[" + tag + "] " + message);
        }

        private static long ExistingLength(string path, long maxBytes)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
                FileInfo fi = new FileInfo(path);
                return fi.Length > maxBytes ? TrimOldest(path, maxBytes) : fi.Length;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 哪些行算"时间相关" —— 时间日志只收这些。
        /// 先按标签认（回合 / 校准 / 波次 / 核弹 / 净化）；再按内容兜底，
        /// 因为「战术」「播报」这两路里也混着真正的计时行
        /// （自动填写刷新计时、校准回合时间、净化倒计时开始…）。
        ///
        /// 【别用单个大词当关键词】播种时踩过：光看"核弹""净化""倒计时"会把
        ///   [音频] 触发点已保存：「核弹」…        ← 只是个音频名字
        ///   [OCR] …净化 计时 长 度 655…          ← 只是识别原文
        /// 这类行也收进来，时间日志就不干净了。所以关键词都带上下文。
        /// </summary>
        private static bool IsTimeLine(string tag, string message)
        {
            if (tag == "回合" || tag == "校准" || tag == "波次" || tag == "核弹" || tag == "净化")
                return true;
            if (string.IsNullOrEmpty(message)) return false;

            return message.IndexOf("刷新计时") >= 0
                || message.IndexOf("大波刷新") >= 0
                || message.IndexOf("小波刷新") >= 0
                || message.IndexOf("校准回合时间") >= 0
                || message.IndexOf("轻收容净化开始计时") >= 0
                || message.IndexOf("净化开始计时") >= 0
                || message.IndexOf("净化倒计时") >= 0
                || message.IndexOf("开始杀人") >= 0
                || message.IndexOf("清除生物") >= 0
                || message.IndexOf("亡者协议倒计时") >= 0
                || message.IndexOf("核弹倒计时") >= 0
                || message.IndexOf("回合开始") >= 0;
        }

        /// <summary>记一条异常。</summary>
        public static void Err(string tag, string message, Exception ex)
        {
            string detail = ex == null ? "" : (ex.GetType().Name + "：" + ex.Message);
            string body = "[" + tag + "] !! " + message + "  " + detail;
            Write(body);
            WriteSplit(_opsQueue, ref _opsPending, ref _opsDropped, body);
        }

        /// <summary>记一条异常（不带额外说明）。</summary>
        public static void Err(string tag, Exception ex)
        {
            Err(tag, "异常", ex);
        }

        /// <summary>把还没写完的日志尽量写出去（退出前调一下，最多等 ms 毫秒）。</summary>
        public static void Flush(int ms)
        {
            try
            {
                DateTime end = DateTime.Now.AddMilliseconds(ms);
                while (DateTime.Now < end)
                {
                    // 队列空 + 没有正在写的东西，才算真的落盘了
                    lock (_lock)
                    {
                        if (_queue.Count == 0 && _pending <= 0
                            && _timeQueue.Count == 0 && _timePending <= 0
                            && _ocrQueue.Count == 0 && _ocrPending <= 0
                            && _ticketQueue.Count == 0 && _ticketPending <= 0
                            && _opsQueue.Count == 0 && _opsPending <= 0) return;
                    }
                    _signal.Set();
                    Thread.Sleep(20);
                }
            }
            catch { }
        }

        /// <summary>只入队，不写盘 —— 任何线程（包括钩子回调）都能安全、快速地调用。</summary>
        private static void Write(string body)
        {
            try
            {
                string line = Stamp() + "  " + body;

                lock (_lock)
                {
                    if (!_enabled) return;

                    // 队列满了就丢最旧的，绝不无限膨胀
                    while (_queue.Count >= MaxQueue) { _queue.Dequeue(); _dropped++; _pending--; }
                    _queue.Enqueue(line);
                    _pending++;
                }
                _signal.Set();
            }
            catch
            {
                // 日志永远不许把主流程带崩
            }
        }

        private static void WriteSplit(Queue<string> q, ref int pending, ref int dropped, string body)
        {
            try
            {
                string line = Stamp() + "  " + body;
                lock (_lock)
                {
                    if (!_enabled) return;
                    while (q.Count >= MaxQueue) { q.Dequeue(); dropped++; pending--; }
                    q.Enqueue(line);
                    pending++;
                }
                _signal.Set();
            }
            catch { }
        }

        /// <summary>时间日志入队（规则和主日志一样：只入队、不写盘）。</summary>
        private static void WriteTime(string body)
        {
            try
            {
                string line = Stamp() + "  " + body;

                lock (_lock)
                {
                    if (!_enabled || string.IsNullOrEmpty(_timePath)) return;

                    while (_timeQueue.Count >= MaxTimeQueue)
                    {
                        _timeQueue.Dequeue();
                        _timeDropped++;
                        _timePending--;
                    }
                    _timeQueue.Enqueue(line);
                    _timePending++;
                }
                _signal.Set();
            }
            catch { }
        }

        /// <summary>取走整个队列；丢过行就在最前面插一条说明。调用方必须持有 _lock。</summary>
        private static string[] TakeQueue(Queue<string> q, ref int dropped)
        {
            if (q.Count == 0) return null;
            string[] batch = q.ToArray();
            q.Clear();

            if (dropped > 0)
            {
                string note = Stamp() + "  [日志] 队列溢出，丢了 " + dropped + " 行";
                string[] bigger = new string[batch.Length + 1];
                bigger[0] = note;
                Array.Copy(batch, 0, bigger, 1, batch.Length);
                batch = bigger;
                dropped = 0;
            }
            return batch;
        }

        private static void Worker()
        {
            while (true)
            {
                try { _signal.WaitOne(1000); }
                catch { }

                string[] batch = null, timeBatch = null, ocrBatch = null, ticketBatch = null, opsBatch = null;
                string path = "", timePath = "", ocrPath = "", ticketPath = "", opsPath = "";
                bool on = false;
                int took = 0, tookTime = 0, tookOcr = 0, tookTicket = 0, tookOps = 0;

                try
                {
                    lock (_lock)
                    {
                        batch = TakeQueue(_queue, ref _dropped);
                        timeBatch = TakeQueue(_timeQueue, ref _timeDropped);
                        ocrBatch = TakeQueue(_ocrQueue, ref _ocrDropped);
                        ticketBatch = TakeQueue(_ticketQueue, ref _ticketDropped);
                        opsBatch = TakeQueue(_opsQueue, ref _opsDropped);
                        took = batch == null ? 0 : batch.Length;
                        tookTime = timeBatch == null ? 0 : timeBatch.Length;
                        tookOcr = ocrBatch == null ? 0 : ocrBatch.Length;
                        tookTicket = ticketBatch == null ? 0 : ticketBatch.Length;
                        tookOps = opsBatch == null ? 0 : opsBatch.Length;
                        path = _path;
                        timePath = _timePath;
                        ocrPath = _ocrPath;
                        ticketPath = _ticketPath;
                        opsPath = _opsPath;
                        on = _enabled;
                    }

                    // 写盘在锁外面做：Write() 的调用方（包括钩子回调）一行都不会等
                    if (on && batch != null && !string.IsNullOrEmpty(path))
                        WriteBatch(path, batch, ref _written, MaxBytes);

                    if (on && timeBatch != null && !string.IsNullOrEmpty(timePath))
                        WriteBatch(timePath, timeBatch, ref _timeWritten, TimeMaxBytes);

                    if (on && ocrBatch != null && !string.IsNullOrEmpty(ocrPath))
                        WriteBatch(ocrPath, ocrBatch, ref _ocrWritten, MaxBytes);
                    if (on && ticketBatch != null && !string.IsNullOrEmpty(ticketPath))
                        WriteBatch(ticketPath, ticketBatch, ref _ticketWritten, TimeMaxBytes);
                    if (on && opsBatch != null && !string.IsNullOrEmpty(opsPath))
                        WriteBatch(opsPath, opsBatch, ref _opsWritten, MaxBytes);
                }
                catch { }
                finally
                {
                    if (took > 0 || tookTime > 0 || tookOcr > 0 || tookTicket > 0 || tookOps > 0)
                        lock (_lock)
                        {
                            _pending = Math.Max(0, _pending - took);
                            _timePending = Math.Max(0, _timePending - tookTime);
                            _ocrPending = Math.Max(0, _ocrPending - tookOcr);
                            _ticketPending = Math.Max(0, _ticketPending - tookTicket);
                            _opsPending = Math.Max(0, _opsPending - tookOps);
                        }
                }
            }
        }

        private static void WriteBatch(string path, string[] lines, ref long written, long maxBytes)
        {
            lock (_fileLock)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    long add = 0;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        sb.Append(lines[i]).Append("\r\n");
                        add += _utf8.GetByteCount(lines[i]) + 2;
                    }

                    // 超过上限：**只删最旧的一半**（操作者要求：不再整文件清空，
                    // 免得刚出的排障信息跟着老日志一起没了）
                    if (written + add > maxBytes)
                        written = TrimOldest(path, maxBytes);

                    File.AppendAllText(path, sb.ToString(), _utf8);
                    written += add;
                }
                catch
                {
                    // 日志永远不许把主流程带崩
                }
            }
        }

        /// <summary>
        /// 把日志文件最旧的一半删掉，保留最新的一半。
        /// 返回修剪后的文件大小（字节）；失败返回 -1（那就只能整文件清空兜底）。
        /// </summary>
        private static long TrimOldest(string path, long maxBytes)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 4)
                {
                    File.Delete(path);
                    return 0;
                }

                // 保留最新的一半，但至少留 500 行、从整行边界切
                int keep = lines.Length / 2;
                if (lines.Length - keep < 500) keep = Math.Max(0, lines.Length - 500);
                if (keep <= 0) { File.Delete(path); return 0; }

                StringBuilder sb = new StringBuilder();
                sb.Append(Stamp()).Append("  [日志] 日志超过 ").Append(maxBytes / 1024 / 1024)
                  .Append("MB，已删掉最旧的一半（保留最新 ")
                  .Append(lines.Length - keep).Append(" 行）\r\n");
                for (int i = keep; i < lines.Length; i++)
                    sb.Append(lines[i]).Append("\r\n");

                File.WriteAllText(path, sb.ToString(), _utf8);
                return _utf8.GetByteCount(sb.ToString());
            }
            catch
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                return 0;
            }
        }
    }
}
