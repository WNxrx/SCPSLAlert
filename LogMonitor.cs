using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 日志定位 + 实时增量读取（同时盯住多个文件）。
    ///
    /// 为什么要同时盯多个：
    ///   · 加入别人的服务器时，本地根本没有服务端日志，
    ///     唯一有内容的是客户端自己的 Player.log；
    ///   · 自己开专用服务器时，细节最全的反而是 ServerLogs\&lt;端口&gt;\Round *.txt。
    ///
    /// 两边的行格式不一样，所以本类只负责“把新出现的整行原样吐出来”，
    /// 怎么解释交给 LogParser（它会自己判断是哪一种）。
    ///
    /// 全程只读（FileShare.ReadWrite | FileShare.Delete），
    /// 不占用游戏文件，也绝不写回任何游戏目录。
    /// </summary>
    internal class LogMonitor : IDisposable
    {
        private const int MaxReadPerPoll = 4 * 1024 * 1024;
        private const int MaxPendingChars = 1024 * 1024;

        /// <summary>一个被盯住的文件的读取状态。</summary>
        private class Feed
        {
            public string Path;
            public long Position;
            public Decoder Decoder;
            public StringBuilder Pending;
        }

        private readonly string _configuredDir;
        private readonly Timer _timer;
        private readonly Dictionary<string, Feed> _feeds =
            new Dictionary<string, Feed>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _activeFiles = new List<string>();
        private bool _firstScan = true;

        /// <summary>每读到一整行就触发一次（两种日志格式混在一起）。</summary>
        public event Action<string> LineReceived;

        /// <summary>状态变化（用于托盘提示 / 设置窗口里的“当前状态”）。</summary>
        public event Action<string> StatusChanged;

        public LogMonitor(string configuredDir, int pollIntervalMs)
        {
            _configuredDir = configuredDir;

            if (pollIntervalMs < 100) pollIntervalMs = 100;
            if (pollIntervalMs > 10000) pollIntervalMs = 10000;
            _timer = new Timer();
            _timer.Interval = pollIntervalMs;
            _timer.Tick += OnTick;
        }

        /// <summary>当前实际在盯的文件（可能多个）。</summary>
        public string[] ActiveFiles
        {
            get { lock (_activeFiles) { return _activeFiles.ToArray(); } }
        }

        /// <summary>自动定位到的客户端日志路径。</summary>
        public static string PlayerLogPath()
        {
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(up)) return null;
            string p = Path.Combine(up, "AppData", "LocalLow", "Northwood", "SCPSL", "Player.log");
            return File.Exists(p) ? p : null;
        }

        /// <summary>
        /// 客户端日志的候选位置（按优先级）。
        /// “Northwood\SCPSL\Player.log” 是当前版本用的；
        /// 后面几个是老版本 / 老 Unity 命名，留着以防万一。
        /// </summary>
        private static List<string> PlayerLogCandidates()
        {
            List<string> list = new List<string>();
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string low = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (!string.IsNullOrEmpty(up))
            {
                list.Add(Path.Combine(up, "AppData", "LocalLow", "Northwood", "SCPSL", "Player.log"));
                list.Add(Path.Combine(up, "AppData", "LocalLow", "Northwood Studios",
                                      "SCP Secret Laboratory", "Player.log"));
                list.Add(Path.Combine(up, "AppData", "LocalLow", "Northwood", "SCPSL", "output_log.txt"));
            }
            if (!string.IsNullOrEmpty(low))
            {
                list.Add(Path.Combine(low, "SCP Secret Laboratory", "Player.log"));
            }
            return list;
        }

        /// <summary>自己开服时的日志根目录。</summary>
        public string RootDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(_configuredDir)) return _configuredDir;
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SCP Secret Laboratory", "ServerLogs");
            }
        }

        /// <summary>
        /// 找出所有要盯的文件：
        ///   1) 客户端 Player.log（加入服务器时唯一有内容的）
        ///   2) ServerLogs 下每个端口目录里最新的 Round*.txt（自己开服时最详细）
        ///   3) 用户在设置里手动指定的目录（额外补充，不影响上面两条）
        /// </summary>
        private List<string> Discover()
        {
            List<string> found = new List<string>();

            // 1) 客户端日志
            List<string> players = PlayerLogCandidates();
            for (int i = 0; i < players.Count; i++)
                if (File.Exists(players[i])) { found.Add(players[i]); break; }

            // 2) / 3) 服务端回合日志
            List<string> roots = new List<string>();
            roots.Add(RootDirectory);
            if (!string.IsNullOrEmpty(_configuredDir))
            {
                string def = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SCP Secret Laboratory", "ServerLogs");
                if (!string.Equals(def, _configuredDir, StringComparison.OrdinalIgnoreCase))
                    roots.Add(def);
            }

            for (int r = 0; r < roots.Count; r++)
            {
                string root = roots[r];
                if (!Directory.Exists(root)) continue;

                // 用户手动指定的目录里如果直接放着 Player.log，
                // 也要盯上 —— 只认 Round*.txt 的话手填就没意义了。
                AddIfExists(found, Path.Combine(root, "Player.log"));
                AddIfExists(found, Path.Combine(root, "output_log.txt"));

                // 根目录直接放的 Round*.txt
                AddNewest(found, root);

                // 每个端口子目录各取最新一份
                try
                {
                    string[] dirs = Directory.GetDirectories(root);
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        AddIfExists(found, Path.Combine(dirs[i], "Player.log"));
                        AddNewest(found, dirs[i]);
                    }
                }
                catch { }
            }

            return found;
        }

        private static void AddIfExists(List<string> found, string path)
        {
            try
            {
                if (File.Exists(path) && !found.Contains(path)) found.Add(path);
            }
            catch { }
        }

        /// <summary>把 dir 里最后修改的那个 Round*.txt 加进列表。</summary>
        private static void AddNewest(List<string> found, string dir)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, "Round*.txt");
                string best = null;
                DateTime bestTime = DateTime.MinValue;
                for (int i = 0; i < files.Length; i++)
                {
                    DateTime t;
                    try { t = File.GetLastWriteTime(files[i]); }
                    catch { continue; }
                    if (t > bestTime) { bestTime = t; best = files[i]; }
                }
                if (best != null && !found.Contains(best)) found.Add(best);
            }
            catch { }
        }

        public void Start()
        {
            _timer.Start();
            Poll();
        }

        /// <summary>
        /// 启动时只读客户端日志，查最近一次场景切换，以判断游戏是否仍在设施场景。
        /// 不把历史日志重新送入事件处理，避免重放上一局的提示和计时。
        /// </summary>
        public bool TryGetCurrentClientFacility(out bool inFacility)
        {
            inFacility = false;
            try
            {
                List<string> candidates = PlayerLogCandidates();
                string newest = null;
                DateTime newestWrite = DateTime.MinValue;
                for (int i = 0; i < candidates.Count; i++)
                {
                    string path = candidates[i];
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    DateTime written = File.GetLastWriteTimeUtc(path);
                    if (newest == null || written > newestWrite)
                    {
                        newest = path;
                        newestWrite = written;
                    }
                }
                if (newest == null) return false;

                bool sawScene = false;
                using (FileStream fs = new FileStream(newest, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.IndexOf("SceneLoaded", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        if (line.IndexOf("Facility", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            inFacility = true;
                            sawScene = true;
                        }
                        else if (line.IndexOf("NewMainMenu", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            inFacility = false;
                            sawScene = true;
                        }
                    }
                }
                return sawScene;
            }
            catch (Exception ex)
            {
                Logger.On("启动", "读取客户端当前场景失败（将等待新的进服证据）: " + ex.Message);
                return false;
            }
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void OnTick(object sender, EventArgs e)
        {
            Poll();
        }

        private void SetStatus(string s)
        {
            if (StatusChanged != null) StatusChanged(s);
        }

        private void Poll()
        {
            try
            {
                List<string> paths = Discover();

                // 新出现的文件挂上。首次扫描时已经有内容的文件先跳到末尾，
                // 免得把上一次游戏留下的旧事件一口气全弹出来。
                for (int i = 0; i < paths.Count; i++)
                {
                    string p = paths[i];
                    if (_feeds.ContainsKey(p)) continue;

                    Feed f = new Feed();
                    f.Path = p;
                    f.Decoder = Encoding.UTF8.GetDecoder();
                    f.Pending = new StringBuilder();
                    f.Position = _firstScan ? SafeLength(p) : 0;
                    _feeds[p] = f;
                }
                _firstScan = false;

                // 逐个读取
                List<string> keys = new List<string>(_feeds.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    Feed f = _feeds[keys[i]];
                    try
                    {
                        if (!File.Exists(f.Path))
                        {
                            // 文件被删 / 正在轮转：先留着，等它回来再接着读
                            continue;
                        }
                        ReadNewContent(f);
                    }
                    catch
                    {
                        // 单个文件读失败不影响别的
                    }
                }

                lock (_activeFiles)
                {
                    _activeFiles.Clear();
                    for (int i = 0; i < keys.Count; i++)
                        if (File.Exists(keys[i])) _activeFiles.Add(keys[i]);
                }

                if (_activeFiles.Count == 0)
                {
                    SetStatus("未找到日志：既没有 Player.log，也没有 ServerLogs\\*\\Round*.txt");
                }
                else
                {
                    // 只显示文件名，路径太长会把状态栏撑爆
                    StringBuilder names = new StringBuilder();
                    for (int i = 0; i < _activeFiles.Count; i++)
                    {
                        if (i > 0) names.Append("、");
                        try { names.Append(Path.GetFileName(_activeFiles[i])); }
                        catch { names.Append(_activeFiles[i]); }
                    }
                    SetStatus("监控中（" + _activeFiles.Count + " 个日志）：" + names);
                }
            }
            catch (Exception ex)
            {
                SetStatus("读取失败：" + ex.Message);
            }
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        private void ReadNewContent(Feed f)
        {
            using (FileStream fs = new FileStream(f.Path, FileMode.Open, FileAccess.Read,
                                                  FileShare.ReadWrite | FileShare.Delete))
            {
                if (f.Position > fs.Length)
                {
                    // 文件被重建（例如游戏重启后 Player.log 重新开始写）：从头读
                    f.Position = 0;
                    f.Pending.Length = 0;
                    f.Decoder.Reset();
                }

                fs.Seek(f.Position, SeekOrigin.Begin);
                long remaining = fs.Length - f.Position;
                if (remaining <= 0) return;
                if (remaining > MaxReadPerPoll) remaining = MaxReadPerPoll;

                byte[] buffer = new byte[(int)remaining];
                int read = fs.Read(buffer, 0, buffer.Length);
                if (read <= 0) return;
                f.Position += read;

                // 有状态的 Decoder：避免一次读取正好切断多字节 UTF-8 汉字
                char[] chars = new char[Encoding.UTF8.GetMaxCharCount(read)];
                int n = f.Decoder.GetChars(buffer, 0, read, chars, 0, false);
                f.Pending.Append(chars, 0, n);
            }

            string all = f.Pending.ToString();
            int start = 0;
            int idx;
            while ((idx = all.IndexOf('\n', start)) >= 0)
            {
                string line = all.Substring(start, idx - start).TrimEnd('\r', ' ', '\t');
                start = idx + 1;
                if (line.Length > 0 && LineReceived != null)
                {
                    try { LineReceived(line); }
                    catch { }
                }
            }

            f.Pending.Length = 0;
            int tailLen = all.Length - start;
            if (tailLen > 0)
            {
                if (tailLen > MaxPendingChars) start = all.Length - MaxPendingChars;
                f.Pending.Append(all, start, all.Length - start);
            }
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTick;
                _timer.Dispose();
            }
        }
    }
}
