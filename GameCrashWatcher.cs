using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SCPSLAlert
{
    internal static class GameCrashWatcher
    {
        public static event Action<CrashReport> ReportReady;

        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int SYNCHRONIZE = 0x00100000;
        private const uint WAIT_OBJECT_0 = 0;
        private const int AltF4WindowMs = 20000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private static readonly object _sync = new object();
        private static Thread _thread;
        private static volatile bool _run;
        private static Config _cfg;

        public static bool Running
        {
            get { return _run; }
        }

        public static void Apply(Config cfg)
        {
            _cfg = cfg;
            if (cfg != null && cfg.MonitorGameCrash) Start();
            else Stop();
        }

        public static void Start()
        {
            lock (_sync)
            {
                if (_run) return;
                _run = true;
                _thread = new Thread(Loop);
                _thread.IsBackground = true;
                _thread.Name = "GameCrashWatcher";
                _thread.Start();
            }
        }

        public static void Stop()
        {
            lock (_sync)
            {
                _run = false;
                _thread = null;
            }
        }

        private static void Loop()
        {
            while (_run)
            {
                Process game = FindGame();
                if (game == null)
                {
                    Thread.Sleep(900);
                    continue;
                }

                int pid = 0;
                DateTime started = DateTime.Now;
                IntPtr handle = IntPtr.Zero;
                try
                {
                    pid = game.Id;
                    try { started = game.StartTime; } catch { }
                    handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, false, pid);
                }
                catch { }

                if (handle == IntPtr.Zero)
                {
                    try { game.Dispose(); } catch { }
                    Thread.Sleep(1200);
                    continue;
                }

                try
                {
                    while (_run)
                    {
                        uint wait = WaitForSingleObject(handle, 500);
                        if (wait == WAIT_OBJECT_0) break;
                        bool exited;
                        try
                        {
                            exited = game.HasExited;
                        }
                        catch
                        {
                            exited = true;
                        }
                        if (exited) break;
                    }

                    if (!_run) return;

                    uint code = 0;
                    GetExitCodeProcess(handle, out code);
                    // 259 = STILL_ACTIVE：等待信号和查询之间进程还没真正结束。
                    // 以前这里直接 return，会把整个监控线程停掉；现在回到外层重新找进程。
                    if (code == 259) continue;
                    Thread.Sleep(1600);
                    if (!_run) return;
                    HandleExit(pid, (int)code, started);
                }
                catch (Exception ex)
                {
                    try { Logger.Err("游戏", "监控游戏退出失败", ex); } catch { }
                }
                finally
                {
                    if (handle != IntPtr.Zero) CloseHandle(handle);
                    try { game.Dispose(); } catch { }
                }
            }
        }

        private static Process FindGame()
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
                    if (string.Equals(name, "SCPSL", StringComparison.OrdinalIgnoreCase))
                    {
                        Process found = all[i];
                        all[i] = null;
                        return found;
                    }
                }
            }
            catch { }
            finally
            {
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null) continue;
                        try { all[i].Dispose(); } catch { }
                    }
                }
            }
            return null;
        }

        private static void HandleExit(int pid, int exitCode, DateTime started)
        {
            string player = PlayerLogPath("Player.log");
            string previous = PlayerLogPath("Player-prev.log");

            string[] tail = ReadTail(player, 600);
            string text = Join(tail);
            TimeSpan duration = DateTime.Now - started;

            // 退出前用户是否对游戏窗口按过 Alt+F4。这是区分
            // "我自己关的" 和 "它自己死掉的" 最可靠的动作信号。
            // 但"按过"不等于"关掉了"：按下时已经卡死、按完很久才退出、
            // 或者系统在按键之前就记过卡死，都不算主动关闭。
            bool altF4Hung;
            DateTime altF4At;
            bool altF4 = GameCloseTracker.WasAltF4(pid, DateTime.Now, AltF4WindowMs,
                out altF4Hung, out altF4At);
            GameCloseTracker.Clear();

            string module = "";
            string code = "";
            string offset = "";
            DateTime hangAt;
            bool eventFound = QueryErrorEvent(pid, started, out module, out code, out offset, out hangAt);
            bool hang = hangAt != DateTime.MinValue;

            WerReport wer = WerReport.FindLatest(started);
            string dumpSource = FindDumpSource(started);

            DateTime exitAt = DateTime.Now;
            bool altF4Close = GameExitRules.AltF4Counts(altF4, altF4Hung, altF4At, exitAt, hangAt);

            GameExitKind kind = GameExitRules.Classify(exitCode,
                eventFound || wer != null, dumpSource.Length > 0, altF4Close, hang, text);

            if (kind == GameExitKind.Normal)
            {
                try
                {
                    string how = altF4Close ? "Alt+F4"
                        : GameExitRules.IsCleanLog(text) ? "控制台 quit"
                        : "退出码 0";
                    Logger.On("游戏", "游戏正常退出（" + how + "，"
                        + CrashAnalyzer.Hex(exitCode) + "，运行 "
                        + CrashAnalyzer.HumanDuration(duration) + "）");
                }
                catch { }
                return;
            }

            string dir = CrashPaths.NewDir(CrashPaths.GameCrash, "game");
            string dump = dumpSource.Length > 0 ? CrashPaths.Copy(dumpSource, dir, "game.dmp") : "";

            string saved = CrashPaths.Copy(player, dir, "Player.log");
            CrashPaths.Copy(previous, dir, "Player-prev.log");
            if (saved.Length == 0) tail = new string[0];

            List<string> modules = null;
            if (wer != null)
            {
                if (string.IsNullOrEmpty(module))
                {
                    module = wer.Module;
                    code = wer.Code;
                    offset = wer.Offset;
                }
                CrashPaths.Copy(wer.FilePath, dir, "wer.txt");
                modules = wer.Modules;
            }

            List<string> stackModules = null;
            List<string> stackLines = null;
            if (dump.Length > 0)
            {
                MiniDump parsed = MiniDump.Load(dump);
                if (parsed != null && parsed.Frames.Count > 0)
                {
                    stackModules = new List<string>();
                    stackLines = new List<string>();
                    for (int i = 0; i < parsed.Frames.Count; i++)
                    {
                        stackModules.Add(parsed.Frames[i].Module);
                        stackLines.Add(parsed.Frames[i].Text);
                    }
                }
            }

            CrashReport report = CrashAnalyzer.Analyze(exitCode, module, code, offset, tail, duration, dir,
                modules, stackModules, stackLines);

            if (kind == GameExitKind.Abnormal)
            {
                report.ConfirmedCrash = false;
                string title = hang ? "游戏卡死，没有正常退出" : "游戏没有正常退出";
                string detail = hang
                    ? "Windows 记录到游戏无响应（AppHang），随后进程结束；没有崩溃转储。"
                    : "退出码 " + CrashAnalyzer.Hex(exitCode)
                      + "，但没有 WER / 转储 / 崩溃日志，也不是 Alt+F4 或控制台 quit 的收尾。";
                if (altF4 && !altF4Close)
                {
                    detail += altF4Hung
                        ? " 按下 Alt+F4 时窗口已经没有响应，这次按键是在关一个已经卡死的游戏。"
                        : " 虽然按过 Alt+F4，但退出明显滞后于按键（或卡死发生在按键之前），不算正常关闭。";
                }

                bool exists = false;
                for (int i = 0; i < report.Causes.Count; i++)
                {
                    if (string.Equals(report.Causes[i].Title, title, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    CrashCause cause = new CrashCause();
                    cause.Level = hang ? "高" : "中";
                    cause.Title = title;
                    cause.Detail = detail;
                    report.Causes.Insert(0, cause);
                }
                if (report.Causes.Count > 3)
                    report.Causes.RemoveRange(3, report.Causes.Count - 3);
                report.Summary = title + "（" + CrashAnalyzer.Hex(exitCode) + "）";
                report.Suggestions.Add("如果是你自己用任务管理器 / Steam 结束的，这条记录可以忽略");
            }

            report.DumpPath = dump;
            report.LogPath = Path.Combine(dir, "summary.txt");
            if (dump.Length > 0) report.Meta.Add("转储  " + Path.GetFileName(dump));
            report.Meta.Add(kind == GameExitKind.Crash ? "判定  已确认崩溃" : "判定  没有崩溃证据");

            try
            {
                string hw = HardwareInfo.Collect() + "\r\n" + CrashTelemetry.Report();
                File.WriteAllText(Path.Combine(dir, "硬件与遥测.txt"), hw, new UTF8Encoding(false));
                report.Meta.Add("硬件  硬件与遥测.txt");
            }
            catch { }
            report.SaveText();

            try
            {
                Logger.On("游戏", (kind == GameExitKind.Crash ? "检测到游戏崩溃：" : "检测到游戏非正常退出：")
                    + report.Summary + "；记录 " + dir);
            }
            catch { }

            CrashPaths.PruneFolders(CrashPaths.GameCrash, 20, 300L * 1024 * 1024);

            if (_cfg != null && !_cfg.CrashPopup) return;
            Action<CrashReport> ready = ReportReady;
            if (ready != null)
            {
                try { ready(report); } catch { }
            }
        }

        private static bool QueryErrorEvent(int pid, DateTime started,
            out string module, out string code, out string offset, out DateTime hangAt)
        {
            module = "";
            code = "";
            offset = "";
            hangAt = DateTime.MinValue;
            bool found = false;
            try
            {
                using (EventLog log = new EventLog("Application"))
                {
                    int count = log.Entries.Count;
                    int from = Math.Max(0, count - 400);
                    for (int i = count - 1; i >= from; i--)
                    {
                        EventLogEntry entry;
                        try { entry = log.Entries[i]; }
                        catch { continue; }
                        if (entry.TimeGenerated < started.AddSeconds(-5)) break;

                        // 卡死：Application Hang（1002），或 WER 的 AppHangTransient（1001）。
                        // 这两种不会产生 AppCrash 报告，但同样属于"没有正常退出"。
                        if (entry.InstanceId == 1001 || entry.InstanceId == 1002)
                        {
                            try
                            {
                                string message = entry.Message;
                                if (message != null
                                    && message.IndexOf("SCPSL.exe", StringComparison.OrdinalIgnoreCase) >= 0
                                    && (entry.InstanceId == 1002
                                        || message.IndexOf("AppHang", StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    // 记最早的一条：它代表"键盘按下之前就已经卡了"。
                                    if (hangAt == DateTime.MinValue || entry.TimeGenerated < hangAt)
                                        hangAt = entry.TimeGenerated;
                                }
                            }
                            catch { }
                            continue;
                        }

                        if (entry.InstanceId != 1000) continue;
                        string[] s = entry.ReplacementStrings;
                        if (s == null || s.Length < 8) continue;
                        if (s[0].IndexOf("SCPSL.exe", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (!found)
                        {
                            module = s[3];
                            code = s[6];
                            offset = s[7];
                            found = true;
                        }
                    }
                }
            }
            catch { }
            return found;
        }

        private static string FindDumpSource(DateTime started)
        {
            try
            {
                string best = "";
                DateTime bestTime = DateTime.MinValue;
                string[] roots = new string[] { Path.GetTempPath(), GameDumpSettings.DumpFolder };
                for (int r = 0; r < roots.Length; r++)
                {
                    string[] files;
                    try
                    {
                        if (!Directory.Exists(roots[r])) continue;
                        files = Directory.GetFiles(roots[r], "SCPSL*.dmp");
                    }
                    catch { continue; }
                    for (int i = 0; i < files.Length; i++)
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(files[i]);
                            // 只认本次进程启动之后写出的转储。以前放宽 10 秒，
                            // 上一局崩溃后马上重开游戏时会把旧转储算到新一局头上。
                            if (fi.LastWriteTime < started.AddSeconds(-2)) continue;                            if (fi.LastWriteTime > bestTime)
                            {
                                bestTime = fi.LastWriteTime;
                                best = fi.FullName;
                            }
                        }
                        catch { }
                    }
                }
                if (best.Length == 0) return "";
                return best;
            }
            catch { return ""; }
        }

        private static string PlayerLogPath(string name)
        {
            try
            {
                string home = Environment.GetEnvironmentVariable("USERPROFILE");
                if (string.IsNullOrEmpty(home))
                    home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "AppData", "LocalLow", "Northwood", "SCPSL", name);
            }
            catch { return ""; }
        }

        private static string[] ReadTail(string path, int count)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new string[0];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
                {
                    string text = reader.ReadToEnd();
                    string[] lines = text.Replace("\r", "").Split('\n');
                    int start = Math.Max(0, lines.Length - count);
                    string[] tail = new string[lines.Length - start];
                    Array.Copy(lines, start, tail, 0, tail.Length);
                    return tail;
                }
            }
            catch { return new string[0]; }
        }

        private static string Join(string[] lines)
        {
            if (lines == null || lines.Length == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++) sb.AppendLine(lines[i]);
            return sb.ToString();
        }

        public static bool DeleteSession(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                string root = Path.GetFullPath(CrashPaths.GameCrash).TrimEnd('\\') + "\\";
                string full = Path.GetFullPath(dir);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
                if (!File.Exists(Path.Combine(full, "summary.txt"))) return false;
                Directory.Delete(full, true);
                return true;
            }
            catch { return false; }
        }

        public static void DeleteAll()
        {
            try
            {
                List<string> dirs = CrashPaths.Folders(CrashPaths.GameCrash);
                for (int i = 0; i < dirs.Count; i++)
                {
                    try { Directory.Delete(dirs[i], true); } catch { }
                }
            }
            catch { }
        }

        public static void DeleteDumps()
        {
            try
            {
                List<string> dirs = CrashPaths.Folders(CrashPaths.GameCrash);
                for (int i = 0; i < dirs.Count; i++)
                {
                    try
                    {
                        string[] files = Directory.GetFiles(dirs[i], "*.dmp");
                        for (int k = 0; k < files.Length; k++)
                        {
                            try { File.Delete(files[k]); } catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static long DataSize()
        {
            return CrashPaths.TotalSize(CrashPaths.GameCrash);
        }

        public static List<string> Sessions()
        {
            List<string> all = CrashPaths.Folders(CrashPaths.GameCrash);
            List<string> list = new List<string>();
            for (int i = 0; i < all.Count; i++)
            {
                try
                {
                    if (File.Exists(Path.Combine(all[i], "summary.txt"))) list.Add(all[i]);
                }
                catch { }
            }
            return list;
        }

        public static int SessionCount()
        {
            return Sessions().Count;
        }

        public static CrashReport LastReport()
        {
            try
            {
                List<string> dirs = Sessions();
                if (dirs.Count == 0) return null;
                string summary = Path.Combine(dirs[0], "summary.txt");
                if (!File.Exists(summary)) return null;
                CrashReport r = new CrashReport();
                r.Kind = "游戏";
                r.Dir = dirs[0];
                r.Time = Directory.GetLastWriteTime(dirs[0]);
                r.LogPath = summary;
                string text = File.ReadAllText(summary);
                string[] lines = text.Replace("\r", "").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("摘要：")) r.Summary = lines[i].Substring(3).Trim();
                    if (lines[i].StartsWith("标题：")) r.Title = lines[i].Substring(3).Trim();
                }
                string dump = Path.Combine(dirs[0], "game.dmp");
                if (File.Exists(dump)) r.DumpPath = dump;
                return r;
            }
            catch { return null; }
        }
    }
}

