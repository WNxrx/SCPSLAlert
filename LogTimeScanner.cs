using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SCPSLAlert
{
    /// <summary>
    /// 「时间」页的日志扫描器：读游戏的服务端日志（ServerLogs\*\Round*.txt），
    /// 把关键事件**成对**找出来（开始 → 结束），算出**现实持续了多少秒**；
    /// 同时给出单个时刻（用来校准"此刻还剩多少"）。
    ///
    /// 一个 Round*.txt 就是完整的一局，所以配对在文件内部做。
    /// 只读文件、不动正在写的 LogMonitor。
    /// </summary>
    internal static class LogTimeScanner
    {
        /// <summary>一对「开始 → 结束」。</summary>
        public class Interval
        {
            public string Kind = "";        // decon / warhead / warheadCancel / support / chaos / round
            public string Label = "";       // 「回合开始 → 净化开始」这种写法
            public DateTime From, To;
            public double Seconds;
            public string Note = "";        // 备注（取消 / 只显示不填 等）
            public string Samples = "";     // 直接校准项用：近几局的样本
        }

        /// <summary>一个单独的时刻。</summary>
        public class Moment
        {
            public string Kind = "";        // round / decon / warhead / detonated / roundend
            public DateTime Time;
            public string Label = "";

            public string KindName
            {
                get
                {
                    switch (Kind)
                    {
                        case "round": return "回合开始";
                        case "decon": return "倒计时结束（开始杀人）";
                        case "warhead": return "核弹倒计时开始";
                        case "detonated": return "核弹引爆";
                        case "roundend": return "回合结束";
                        default: return Kind;
                    }
                }
            }
        }

        public class Report
        {
            public List<Interval> Intervals = new List<Interval>();
            public List<Moment> Moments = new List<Moment>();
        }

        private static readonly Regex HeadRegex = new Regex(
            @"^(?<y>\d{4})-(?<mo>\d{2})-(?<d>\d{2}) (?<h>\d{2}):(?<mi>\d{2}):(?<s>\d{2})(?<ms>\.\d+)?\s*(?<tz>[+-]\d{2}:\d{2})?\s*\|",
            RegexOptions.Compiled);

        /// <summary>最近的几局（新的在前），每种事件各留最近 3 局。</summary>
        public static Report Scan()
        {
            return Scan(new ScanOptions());
        }

        /// <summary>按给定的当前设置扫描（算建议值要用到"回合后多久净化"等）。</summary>
        public static Report Scan(ScanOptions opt)
        {
            if (opt == null) opt = new ScanOptions();

            Report rep = new Report();
            List<string> files = FindLogs();
            List<DateTime> deconTimes = new List<DateTime>();

            for (int i = 0; i < files.Count; i++)
            {
                List<Ev> evs;
                try { evs = ScanFile(files[i]); }
                catch { continue; }
                BuildIntervals(evs, rep.Intervals, deconTimes);
                AddMoments(evs, rep.Moments);
            }

            // 应用端日志里的「净化倒计时开始」+ 服务端的「开始杀人」→ 真正的倒计时时长
            AddCountdownIntervals(deconTimes, rep.Intervals);

            // 应用端日志里的刷新事件 → 大波间隔 / 小波间隔（都在程序自己日志里）
            AddWaveIntervals(rep.Intervals);

            // 直接校准项：把量到的值归成「选一下就能填」的入口
            BuildDirectRows(rep, opt);

            rep.Intervals = KeepNewest(rep.Intervals);
            rep.Intervals.Sort(delegate (Interval a, Interval b) { return b.To.CompareTo(a.To); });
            rep.Moments.Sort(delegate (Moment a, Moment b) { return b.Time.CompareTo(a.Time); });
            if (rep.Moments.Count > 8) rep.Moments.RemoveRange(8, rep.Moments.Count - 8);
            return rep;
        }

        /// <summary>
        /// 每种事件只留最近几条：能填时长的留 3 局方便对比；
        /// 只显示的（核弹取消 / 混沌 / 回合时长）留 1 条就够，免得把有用的挤下去。
        /// </summary>
        private static List<Interval> KeepNewest(List<Interval> all)
        {
            Dictionary<string, int> quota = new Dictionary<string, int>();
            quota["deconCountdown"] = 3;
            quota["deconEnd"] = 3;
            quota["warhead"] = 3;
            quota["support"] = 3;
            quota["warheadCancel"] = 1;
            quota["chaos"] = 1;
            quota["round"] = 1;
            quota["waveBigGap"] = 3;
            quota["waveAfterSmallGap"] = 3;
            quota["waveSmallGap"] = 3;

            Dictionary<string, int> used = new Dictionary<string, int>();
            List<Interval> sorted = new List<Interval>(all);
            sorted.Sort(delegate (Interval a, Interval b) { return b.To.CompareTo(a.To); });

            List<Interval> keep = new List<Interval>();
            for (int i = 0; i < sorted.Count; i++)
            {
                int cap;
                if (!quota.TryGetValue(sorted[i].Kind, out cap)) cap = 2;
                int n;
                used.TryGetValue(sorted[i].Kind, out n);
                if (n >= cap) continue;
                used[sorted[i].Kind] = n + 1;
                keep.Add(sorted[i]);
            }
            return keep;
        }

        // ============================================================
        // 直接校准项（操作者要求）：不要每次都自己去间隔行里挑，
        // 直接给「轻收净化时间（多少秒）」「核弹倒计时（多少秒）」
        // 「支援刷新时间（多少秒）」这种选项，勾上就填对应的设置。
        // 建议值取**最近那一局**量到/算出来的数，旁边把近几局样本一起列出来。
        // ============================================================

        /// <summary>
        /// 扫描要用到的"当前设置值"（算建议值时要用）。
        /// </summary>
        public class ScanOptions
        {
            /// <summary>用户填的「回合后多久净化」，用来把净化总时长劈开。</summary>
            public double DeconOffsetSeconds;

            /// <summary>软件估的大波典型人数（默认 16）。</summary>
            public int BigWavePlayers = 16;

            /// <summary>每人加时秒数（默认 10）。</summary>
            public int PerPlayerSeconds = 10;

            /// <summary>小波进场延迟（默认 60 秒）。</summary>
            public int MiniEntryDelay = 60;

            /// <summary>软件估的小波典型人数（默认 6）。</summary>
            public int SmallWavePlayers = 6;
        }

        private static void BuildDirectRows(Report rep, ScanOptions opt)
        {
            double deconOffsetSeconds = opt.DeconOffsetSeconds;

            // 净化：优先用"倒计时开始 → 开始杀人"（就是倒计时时长本身）
            Interval cd = NewestOf(rep.Intervals, "deconCountdown");
            Interval de = NewestOf(rep.Intervals, "deconEnd");
            int deconVal = 0;
            string deconSrc = "";
            if (cd != null)
            {
                deconVal = (int)Math.Round(cd.Seconds);
                deconSrc = "倒计时实测";
            }
            else if (de != null)
            {
                deconVal = (int)Math.Round(de.Seconds) - (int)deconOffsetSeconds;
                deconSrc = "总时长 " + (int)Math.Round(de.Seconds) + " − 偏移 " + (int)deconOffsetSeconds;
            }
            if (deconVal > 0)
            {
                rep.Intervals.Add(Direct("directDecon", "轻收净化时间（倒计时多少秒）",
                    deconVal, Samples(rep.Intervals, cd != null ? "deconCountdown" : "deconEnd"), ""));
            }

            Interval wh = NewestOf(rep.Intervals, "warhead");
            if (wh != null)
                rep.Intervals.Add(Direct("directWarhead", "核弹倒计时（多少秒）",
                    (int)Math.Round(wh.Seconds), Samples(rep.Intervals, "warhead"), ""));

            Interval sp = NewestOf(rep.Intervals, "support");
            if (sp != null)
                rep.Intervals.Add(Direct("directSupport", "支援刷新时间（多少秒）",
                    (int)Math.Round(sp.Seconds), Samples(rep.Intervals, "support"),
                    "各局差别可能较大，数字受对局情况影响"));

            // ---- 大波 / 小波（操作者要求：大波取平均，小波固定）----
            // 软件预测时会自己加"上一波人数 × 每人加时"，所以校准值要把这部分减掉；
            // 小波同理：MiniWaveDelay 只是"最早"，真正进场还要加上进场延迟。
            //
            // 【优先用小波 → 下一次大波】大波 → 大波的间隔里可能夹了一波小波，
            // 那种情况下计时其实是从小波重新起算的，拿它减"大波人数"会算大。
            // 小波 → 大波的间隔只受小波人数影响，换算出来的基准值最干净。
            int afterSmallAvg = Average(rep.Intervals, "waveAfterSmallGap", 2, MaxWaveGapSeconds);
            int bigAvg = Average(rep.Intervals, "waveBigGap", 2, MaxWaveGapSeconds);
            if (afterSmallAvg > 0 || bigAvg > 0)
            {
                int bonus;
                int basis;
                string src;
                string smp;
                if (afterSmallAvg > 0)
                {
                    bonus = opt.SmallWavePlayers * opt.PerPlayerSeconds;
                    basis = afterSmallAvg;
                    src = "小波 → 下一次大波 实测平均 " + afterSmallAvg + " 秒";
                    smp = Samples(rep.Intervals, "waveAfterSmallGap");
                }
                else
                {
                    bonus = opt.BigWavePlayers * opt.PerPlayerSeconds;
                    basis = bigAvg;
                    src = "大波 → 大波 实测平均 " + bigAvg + " 秒";
                    smp = Samples(rep.Intervals, "waveBigGap");
                }
                int val = basis - bonus;
                if (val < 30) val = 30;
                rep.Intervals.Add(Direct("directWaveBig", "大波基准间隔（多少秒）", val,
                    src + (smp.Length > 0 ? "，" + smp : ""),
                    "预测时软件还会自己加 人数×每人加时（按 "
                    + (afterSmallAvg > 0 ? opt.SmallWavePlayers : opt.BigWavePlayers) + "×"
                    + opt.PerPlayerSeconds + " = " + bonus + " 秒估），所以这里减掉"
                    + (afterSmallAvg > 0 ? "；大波 → 大波中间可能夹着小波，优先采用小波之后的那段" : "")));
            }

            int smallAvg = Average(rep.Intervals, "waveSmallGap", 2, 900);
            if (smallAvg > 0)
            {
                int val = smallAvg - opt.MiniEntryDelay;
                if (val < 30) val = 30;
                string smp = Samples(rep.Intervals, "waveSmallGap");
                rep.Intervals.Add(Direct("directWaveSmall", "小波最早延迟（多少秒）", val,
                    "实测平均 " + smallAvg + " 秒" + (smp.Length > 0 ? "，" + smp : ""),
                    "小波真正进场 = 它 + 进场延迟 " + opt.MiniEntryDelay + " 秒，所以这里减掉"));
            }
        }

        /// <summary>
        /// 某一类间隔的平均值（四舍五入）；样本不够（少于 minCount 个）返回 0 ——
        /// 刷新间隔本来就随机，拿**一个**样本去校准会把设置带偏（实测单样本 275 秒，
        /// 而 6 个样本的平均只有 208 秒），所以宁可先不给建议。
        /// maxSeconds 用来挡掉"跨回合 / 跨天"的超大间隔：以前把 10-17 17:32 到
        /// 10-24 16:38 这种六天的空档也算成大波间隔，平均出 37579 秒（再被配置上限夹成 3600），
        /// 填进去之后所有刷新推算都变成 99:59。
        /// </summary>
        private static int Average(List<Interval> list, string kind, int minCount, double maxSeconds = 1800)
        {
            double sum = 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Kind != kind) continue;
                if (list[i].Seconds < 20) continue;      // 掉线/重复读到的短间隔不算
                if (list[i].Seconds > maxSeconds) continue;
                sum += list[i].Seconds;
                n++;
            }
            if (n < minCount) return 0;
            return (int)Math.Round(sum / n);
        }

        /// <summary>刷新间隔的合理上限（秒）：超过半小时的肯定是跨回合/跨天，不是同一局。</summary>
        private const double MaxWaveGapSeconds = 1800;

        private static Interval Direct(string kind, string label, int seconds, string samples, string note)
        {
            Interval t = new Interval();
            t.Kind = kind;
            t.Label = label;
            t.Seconds = seconds;
            t.Samples = samples;
            t.Note = note;
            t.To = DateTime.Now;
            return t;
        }

        private static Interval NewestOf(List<Interval> list, string kind)
        {
            Interval best = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Kind != kind) continue;
                if (list[i].Seconds < 3) continue;        // 管理员瞬间引爆这种 0 秒的不算样本
                if (best == null || list[i].To > best.To) best = list[i];
            }
            return best;
        }

        /// <summary>把某一类量到的近几局样本串起来（"655 / 655 / 655"）。</summary>
        private static string Samples(List<Interval> list, string kind)
        {
            List<Interval> hits = new List<Interval>();
            for (int i = 0; i < list.Count; i++)
                if (list[i].Kind == kind && list[i].Seconds >= 3) hits.Add(list[i]);
            if (hits.Count <= 1) return "";
            hits.Sort(delegate (Interval a, Interval b) { return b.To.CompareTo(a.To); });
            if (hits.Count > 3) hits.RemoveRange(3, hits.Count - 3);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hits.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append((int)Math.Round(hits[i].Seconds));
            }
            return "近 " + hits.Count + " 局：" + sb;
        }

        private class Ev
        {
            public string Kind = "";
            public DateTime Time;
            public bool WarheadDetonate;
            public bool WarheadCancel;
        }

        // ============================================================
        // 跨两个日志配对：应用端「净化倒计时开始」→ 服务端「开始杀人」
        //
        // 应用端日志（程序自己的 SCPSLAlert.log）里，OCR 认出 CASSIE 那句
        // 「净化程序将在倒计时 X 分钟后开始」时会记一行带毫秒时间戳的
        // 「[播报] 触发 轻收容净化开始计时」—— 那一刻就是倒计时开始。
        // 服务端的 `Decontamination started.` 就是倒计时归零、开始杀人。
        // 两条一减 = 这一局**真实**的净化倒计时时长。
        //
        // 用户常常半途进/出服务器，应用端不一定有记录，所以这是"有就用"。
        // ============================================================

        private static readonly Regex AppDeconRegex = new Regex(
            @"^(?<y>\d{4})-(?<mo>\d{2})-(?<d>\d{2}) (?<h>\d{2}):(?<mi>\d{2}):(?<s>\d{2})(?<ms>\.\d+)?\s+\[播报\]\s*触发\s*轻收容净化开始计时",
            RegexOptions.Compiled);

        private static void AddCountdownIntervals(List<DateTime> deconTimes, List<Interval> into)
        {
            if (deconTimes.Count == 0) return;
            List<DateTime> starts = ScanAppDeconStarts();
            if (starts.Count == 0) return;

            List<DateTime> used = new List<DateTime>();
            for (int i = 0; i < starts.Count; i++)
            {
                DateTime a = starts[i];
                DateTime best = DateTime.MinValue;
                double bestGap = double.MaxValue;
                for (int k = 0; k < deconTimes.Count; k++)
                {
                    double gap = (deconTimes[k] - a).TotalSeconds;
                    if (gap < 0 || gap > 1800) continue;         // 只看之后 30 分钟内的
                    if (used.Contains(deconTimes[k])) continue;
                    if (gap < bestGap) { bestGap = gap; best = deconTimes[k]; }
                }
                if (best == DateTime.MinValue) continue;
                used.Add(best);
                into.Add(MakeInterval("deconCountdown", "净化倒计时开始 → 开始杀人（倒计时时长）",
                    a, best, ""));
            }
        }

        // ============================================================
        // 刷新（大波 / 小波）间隔：从**程序自己的日志**里量
        //
        // 服务端日志只写「WaveSpawner spawned N players as FoundationForces」，
        // 分不出是大波还是小波；程序日志里认到播报时会分类，所以从这边读：
        //   · 「识别到 MTF 进入设施（小波 / 增援部队）…」 → 小波
        //   · 「识别到 Chaos 进入设施（大波）」           → 大波
        //   · 「大波刷新：Chaos，刷新计时已校准到现在」   → 大波
        //   · 「手动校准大波：MTF」/「手动校准小波：MTF」 → 按日志文字分大小波
        //     （用户按 F11 / 面板「刚刷了一波」时，程序会按上一波自动顺延）
        // ============================================================

        private class WaveEv
        {
            public DateTime Time;
            public bool Big;
        }

        private static readonly Regex AppTimeRegex = new Regex(
            @"^(?<y>\d{4})-(?<mo>\d{2})-(?<d>\d{2}) (?<h>\d{2}):(?<mi>\d{2}):(?<s>\d{2})(?<ms>\.\d+)?",
            RegexOptions.Compiled);

        private static readonly Regex AppWaveEnterRegex = new Regex(
            @"进入设施（(?<k>[^）]*)）", RegexOptions.Compiled);

        private static readonly Regex AppWaveFillRegex = new Regex(
            @"(?<k>大波|小波)刷新：", RegexOptions.Compiled);

        private static List<WaveEv> ScanAppWaves()
        {
            // 【必须先收集再排序】两份日志是"主日志在前、时间日志在后"直接拼起来的，
            // 顺序不是时间顺序。以前边读边去重（比较上一条），遇到时间更早的行
            // 差值变成负数、被当成"60 秒内的重复"丢掉，于是大量刷新事件凭空消失。
            List<WaveEv> raw = new List<WaveEv>();
            string[] lines = ReadAppLogLines();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.IndexOf("进入设施") < 0 && line.IndexOf("刷新：") < 0
                    && line.IndexOf("手动校准") < 0) continue;

                bool big;
                Match e = AppWaveEnterRegex.Match(line);
                if (e.Success)
                {
                    big = e.Groups["k"].Value.IndexOf("小波") < 0;
                }
                else
                {
                    Match f = AppWaveFillRegex.Match(line);
                    if (f.Success) big = f.Groups["k"].Value == "大波";
                    else if (line.IndexOf("手动校准小波") >= 0) big = false;
                    else if (line.IndexOf("手动校准大波") >= 0) big = true;
                    else continue;
                }

                Match tm = AppTimeRegex.Match(line);
                if (!tm.Success) continue;
                DateTime t;
                if (!TryParseAppTime(tm, out t)) continue;

                WaveEv w = new WaveEv();
                w.Time = t;
                w.Big = big;
                raw.Add(w);
            }

            // 按时间排好，再合并同一波在几秒内被记的好几行（播报 + 战术自动填写 + 手动校准）
            raw.Sort(delegate (WaveEv a, WaveEv b) { return a.Time.CompareTo(b.Time); });

            List<WaveEv> list = new List<WaveEv>();
            for (int i = 0; i < raw.Count; i++)
            {
                if (list.Count > 0)
                {
                    WaveEv last = list[list.Count - 1];
                    if (last.Big == raw[i].Big
                        && Math.Abs((raw[i].Time - last.Time).TotalSeconds) < 60) continue;
                }
                list.Add(raw[i]);
            }
            return list;
        }

        private static void AddWaveIntervals(List<Interval> into)
        {
            List<WaveEv> ws = ScanAppWaves();
            if (ws.Count < 2) return;

            // 大波 → 下一次大波（操作者说这个比较随机，所以后面取平均）
            // 同时记下"小波 → 下一次大波"：中间夹了小波时，计时是从小波重新起算的，
            // 用它换算基准值更准（见 BuildDirectRows）。
            DateTime? prevBig = null;
            DateTime? lastSmall = null;
            for (int i = 0; i < ws.Count; i++)
            {
                double gap;
                if (ws[i].Big)
                {
                    if (prevBig.HasValue)
                    {
                        gap = (ws[i].Time - prevBig.Value).TotalSeconds;
                        // 【跨回合保护】超过 30 分钟的空档不是同一局的刷新间隔
                        if (gap >= 60 && gap <= MaxWaveGapSeconds)
                            into.Add(MakeInterval("waveBigGap", "大波 → 下一次大波",
                                prevBig.Value, ws[i].Time, "只显示"));
                    }
                    if (lastSmall.HasValue)
                    {
                        gap = (ws[i].Time - lastSmall.Value).TotalSeconds;
                        if (gap >= 60 && gap <= MaxWaveGapSeconds)
                            into.Add(MakeInterval("waveAfterSmallGap", "小波 → 下一次大波",
                                lastSmall.Value, ws[i].Time, ""));
                    }
                    prevBig = ws[i].Time;
                    lastSmall = null;
                    continue;
                }

                lastSmall = ws[i].Time;
                if (!prevBig.HasValue) continue;

                // 大波 → 紧跟的那一波小波（每个大波只配一次）
                gap = (ws[i].Time - prevBig.Value).TotalSeconds;
                if (gap <= 20 || gap > 900) continue;      // 超过 15 分钟的不算"这一波的小波"
                into.Add(MakeInterval("waveSmallGap", "大波 → 小波",
                    prevBig.Value, ws[i].Time, ""));
                prevBig = null;                            // 一个主波只配一个小波
            }
        }

        /// <summary>
        /// 读程序自己日志的尾部（各 1MB）并切成行；读不到就返回空数组。
        ///
        /// 【两份都读】时间日志（SCPSLAlert.时间.log）只收时间相关的行、上限也只有 2MB，
        /// 所以同样的体积能留下**长得多**的历史；主日志 5MB 轮转时会把刷新数据一起裁掉
        /// （实测发生过：14:29~15:02 的刷新记录被整段删掉）。两份合起来读，重复的行
        /// 由后面的去重规则（净化 120 秒 / 刷新 60 秒）合并，不影响结果。
        /// </summary>
        private static string[] ReadAppLogLines()
        {
            List<string> all = new List<string>();
            List<string> paths = new List<string>();

            string main = AppLogPath();
            if (!string.IsNullOrEmpty(main)) paths.Add(main);

            string timeLog = "";
            try { timeLog = Logger.TimePath; }
            catch { }
            if (string.IsNullOrEmpty(timeLog) && !string.IsNullOrEmpty(main))
            {
                try
                {
                    string dir = Path.GetDirectoryName(main);
                    if (!string.IsNullOrEmpty(dir))
                        timeLog = Path.Combine(dir, "SCPSLAlert.时间.log");
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(timeLog) && !paths.Contains(timeLog)) paths.Add(timeLog);

            for (int i = 0; i < paths.Count; i++)
            {
                string[] part = ReadTailLines(paths[i]);
                if (part.Length > 0) all.AddRange(part);
            }
            return all.ToArray();
        }

        private static string[] ReadTailLines(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new string[0];
            try
            {
                byte[] bytes;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    long skip = Math.Max(0, len - 1024 * 1024);
                    fs.Seek(skip, SeekOrigin.Begin);
                    bytes = new byte[len - skip];
                    if (bytes.Length == 0) return new string[0];
                    fs.Read(bytes, 0, bytes.Length);
                }
                return Encoding.UTF8.GetString(bytes).Split('\n');
            }
            catch { return new string[0]; }
        }

        /// <summary>从程序自己的日志里取「净化开始计时」的时刻（同一句播报会记好几行，去重）。</summary>
        private static List<DateTime> ScanAppDeconStarts()
        {
            // 同样：两份日志拼接后不是时间顺序，必须先收集再排序再去重（见 ScanAppWaves）
            List<DateTime> raw = new List<DateTime>();
            string[] lines = ReadAppLogLines();

            for (int i = 0; i < lines.Length; i++)
            {
                Match m = AppDeconRegex.Match(lines[i].TrimEnd('\r'));
                if (!m.Success) continue;
                DateTime t;
                if (!TryParseAppTime(m, out t)) continue;
                raw.Add(t);
            }

            raw.Sort();
            List<DateTime> list = new List<DateTime>();
            for (int i = 0; i < raw.Count; i++)
            {
                // 同一句播报会在几秒内被认到好几次，只留第一下
                if (list.Count > 0 && (raw[i] - list[list.Count - 1]).TotalSeconds < 120) continue;
                list.Add(raw[i]);
            }
            return list;
        }

        private static bool TryParseAppTime(Match m, out DateTime t)
        {
            t = DateTime.MinValue;
            try
            {
                string s = m.Groups["y"].Value + "-" + m.Groups["mo"].Value + "-" + m.Groups["d"].Value
                    + " " + m.Groups["h"].Value + ":" + m.Groups["mi"].Value + ":" + m.Groups["s"].Value
                    + (m.Groups["ms"].Success ? m.Groups["ms"].Value : "");
                t = DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss.FFF", CultureInfo.InvariantCulture);
                return true;
            }
            catch { return false; }
        }

        private static string AppLogPath()
        {
            try { if (!string.IsNullOrEmpty(Logger.Path)) return Logger.Path; }
            catch { }
            try
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string dir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir)) return Path.Combine(dir, "SCPSLAlert.log");
            }
            catch { }
            return "";
        }

        private static void BuildIntervals(List<Ev> evs, List<Interval> into, List<DateTime> deconTimes)
        {
            DateTime? roundStart = null, roundEnd = null, decon = null, mtf = null, chaos = null;
            DateTime? armed = null;               // 核弹倒计时开始

            for (int i = 0; i < evs.Count; i++)
            {
                Ev e = evs[i];
                switch (e.Kind)
                {
                    case "round": if (!roundStart.HasValue) roundStart = e.Time; break;
                    case "roundend": if (!roundEnd.HasValue) roundEnd = e.Time; break;
                    case "decon": if (!decon.HasValue) decon = e.Time; break;
                    case "mtf": if (!mtf.HasValue) mtf = e.Time; break;
                    case "chaos": if (!chaos.HasValue) chaos = e.Time; break;
                }

                // 核弹：先看这一条是不是"引爆/取消"（它自己不是开始时刻），再决定要不要记开始
                if (e.Kind == "warhead")
                {
                    if (e.WarheadDetonate)
                    {
                        if (armed.HasValue)
                            into.Add(MakeInterval("warhead", "核弹倒计时开始 → 引爆", armed.Value, e.Time, ""));
                        armed = null;
                    }
                    else if (e.WarheadCancel)
                    {
                        if (armed.HasValue)
                            into.Add(MakeInterval("warheadCancel", "核弹倒计时开始 → 取消", armed.Value, e.Time, "取消"));
                        armed = null;
                    }
                    else
                    {
                        armed = e.Time;      // Countdown started（重新起表就覆盖）
                    }
                }
            }

            // ---- 净化：日志里那行 Decontamination started = 倒计时归零、开始杀人 ----
            // 【不要拿"被腐蚀物融化"的死亡当标志】那是(可能别人)在轻收容里死掉，
            // 而且没人在轻收容死就一条都没有 —— 只能证明"正在杀人"，不能当时刻用。
            if (roundStart.HasValue && decon.HasValue && decon.Value > roundStart.Value)
                into.Add(MakeInterval("deconEnd", "回合开始 → 倒计时结束（开始杀人）",
                    roundStart.Value, decon.Value, ""));
            if (decon.HasValue) deconTimes.Add(decon.Value);

            if (roundStart.HasValue && mtf.HasValue && mtf.Value > roundStart.Value)
                into.Add(MakeInterval("support", "回合开始 → 第一次支援刷新", roundStart.Value, mtf.Value, ""));
            if (roundStart.HasValue && chaos.HasValue && chaos.Value > roundStart.Value)
                into.Add(MakeInterval("chaos", "回合开始 → 第一次混沌刷新", roundStart.Value, chaos.Value, "只显示"));
            if (roundStart.HasValue && roundEnd.HasValue && roundEnd.Value > roundStart.Value)
                into.Add(MakeInterval("round", "回合开始 → 回合结束", roundStart.Value, roundEnd.Value, "只显示"));
        }

        private static Interval MakeInterval(string kind, string label,
            DateTime from, DateTime to, string note)
        {
            Interval t = new Interval();
            t.Kind = kind;
            t.Label = label;
            t.From = from;
            t.To = to;
            t.Seconds = Math.Round((to - from).TotalSeconds, 1);
            t.Note = note;
            return t;
        }

        private static void AddMoments(List<Ev> evs, List<Moment> into)
        {
            for (int i = 0; i < evs.Count; i++)
            {
                Ev e = evs[i];
                string kind = e.Kind;
                if (kind == "warhead" && e.WarheadDetonate) kind = "detonated";
                if (kind == "mtf" || kind == "chaos" || kind == "warhead") continue;

                Moment m = new Moment();
                m.Kind = kind;
                m.Time = e.Time;
                into.Add(m);
            }
        }

        /// <summary>日志文件：ServerLogs\*\Round*.txt，取最近修改的 6 个。</summary>
        private static List<string> FindLogs()
        {
            List<string> files = new List<string>();
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SCP Secret Laboratory", "ServerLogs");
                if (!Directory.Exists(root)) return files;
                foreach (string d in Directory.GetDirectories(root))
                {
                    try
                    {
                        foreach (string f in Directory.GetFiles(d, "Round*.txt"))
                            files.Add(f);
                    }
                    catch { }
                }
                files.Sort(delegate (string a, string b)
                {
                    return File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a));
                });
                if (files.Count > 30) files.RemoveRange(30, files.Count - 30);
            }
            catch { }
            return files;
        }

        private static List<Ev> ScanFile(string path)
        {
            List<Ev> list = new List<Ev>();

            byte[] bytes;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                long len = fs.Length;
                long skip = Math.Max(0, len - 1024 * 1024);
                fs.Seek(skip, SeekOrigin.Begin);
                bytes = new byte[len - skip];
                if (bytes.Length == 0) return list;
                fs.Read(bytes, 0, bytes.Length);
            }

            string text = Encoding.UTF8.GetString(bytes);
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length < 24) continue;
                Match m = HeadRegex.Match(line);
                if (!m.Success) continue;

                string body = line.Substring(m.Length).TrimStart();
                DateTime t;
                if (!TryParseHead(m, out t)) continue;

                Ev e = Classify(body, t);
                if (e != null) list.Add(e);
            }
            return list;
        }

        private static Ev Classify(string body, DateTime t)
        {
            string sub = "";
            int p1 = body.IndexOf('|');
            if (p1 > 0)
            {
                int p2 = body.IndexOf('|', p1 + 1);
                if (p2 > p1) sub = body.Substring(p1 + 1, p2 - p1 - 1).Trim();
            }
            bool warheadSub = string.Equals(sub, "Warhead", StringComparison.OrdinalIgnoreCase);

            Ev e = new Ev();
            e.Time = t;

            if (warheadSub && Has(body, "Countdown started")) { e.Kind = "warhead"; return e; }
            if (warheadSub && Has(body, "Warhead detonated"))
            {
                e.Kind = "warhead";
                e.WarheadDetonate = true;
                return e;
            }
            if (warheadSub && Has(body, "Detonation cancelled"))
            {
                e.Kind = "warhead";
                e.WarheadCancel = true;
                return e;
            }

            if (Has(body, "Round has been started")) { e.Kind = "round"; return e; }
            if (Has(body, "Round finished") || Has(body, "Round has been ended")
                || Has(body, "Round has ended")) { e.Kind = "roundend"; return e; }
            if (Has(body, "Decontamination started") || Has(body, "Decontamination has begun"))
            { e.Kind = "decon"; return e; }

            if (Has(body, "WaveSpawner has successfully spawned"))
            {
                if (Has(body, "FoundationForces")) { e.Kind = "mtf"; return e; }
                if (Has(body, "ChaosInsurgency")) { e.Kind = "chaos"; return e; }
            }
            return null;
        }

        private static bool Has(string s, string sub)
        {
            return s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryParseHead(Match m, out DateTime t)
        {
            t = DateTime.MinValue;
            try
            {
                string s = m.Groups["y"].Value + "-" + m.Groups["mo"].Value + "-" + m.Groups["d"].Value
                    + " " + m.Groups["h"].Value + ":" + m.Groups["mi"].Value + ":" + m.Groups["s"].Value
                    + (m.Groups["ms"].Success ? m.Groups["ms"].Value : "");
                if (m.Groups["tz"].Success)
                {
                    DateTimeOffset dto = DateTimeOffset.ParseExact(
                        s + " " + m.Groups["tz"].Value,
                        "yyyy-MM-dd HH:mm:ss.FFF zzz", CultureInfo.InvariantCulture);
                    t = dto.LocalDateTime;
                }
                else
                {
                    t = DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss.FFF", CultureInfo.InvariantCulture);
                }
                return true;
            }
            catch { return false; }
        }
    }
}
