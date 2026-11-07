using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 第 4 批：阵营识别（替换屏幕中心角色 OCR）。
    ///
    /// 原理：游戏左下角血条的颜色由阵营决定。开局"角色分配完成"和每次复活后，
    /// 以 20 帧/秒截取血条最左 10% 的一个小条，多点平均取色，换算 HSV 后跟
    /// 映射表比对（HSV 容差 + 加权欧氏距离），连续 10 帧一致才算结果，
    /// 最多采 3 秒；连续 5 秒全黑/全透明判 SCP-079。
    ///
    /// 这里只负责"取色 + 判定 + 计时"，不负责弹提示，也不碰界面线程。
    /// </summary>
    internal enum FactionKind
    {
        Unknown = 0,
        DClass = 1,
        Scientist = 2,
        Mtf = 3,
        Chaos = 4,
        Guard = 5,
        Scp = 6,
        Tutorial = 7,
        Scp079 = 8
    }

    internal sealed class FactionEntry
    {
        public FactionKind Kind = FactionKind.Unknown;
        public string Name = "";
        public double Hue;
        public double Sat;
        public double Val;
        public double HueTol = 16;
        public double SatTol = 0.30;
        public double ValTol = 0.25;
        public bool Enabled = true;
    }

    internal sealed class FactionResult
    {
        public FactionKind Kind = FactionKind.Unknown;
        public string Name = "未知阵营";
        public Color Rgb;
        public double Hue, Sat, Val;
        public double Score = 999;
        public double RgbDistance;
        public bool IsBlack;
        public string Raw = "";
    }

    /// <summary>血条颜色 → 阵营 的映射表。默认值来自 阵营截图\ 的 14 张实测。</summary>
    internal static class FactionTable
    {
        public static List<FactionEntry> Defaults()
        {
            List<FactionEntry> list = new List<FactionEntry>();
            // 名称 | H | S | V | HueTol | SatTol | ValTol
            list.Add(Entry(FactionKind.DClass, "D 级人员", 30, 0.82, 0.37, 16, 0.30, 0.25));
            list.Add(Entry(FactionKind.Scientist, "科学家", 52, 0.55, 0.51, 14, 0.30, 0.25));
            list.Add(Entry(FactionKind.Chaos, "混沌分裂者", 95, 0.72, 0.24, 34, 0.40, 0.25));
            // 设施警卫是低饱和的灰蓝，必须排在九尾狐前面先比（否则会被蓝吃掉）
            list.Add(Entry(FactionKind.Guard, "设施警卫", 212, 0.28, 0.22, 12, 0.20, 0.20));
            list.Add(Entry(FactionKind.Mtf, "九尾狐", 210, 0.85, 0.40, 24, 0.42, 0.35));
            // 只有 SCP-173 的红色实测；其它 SCP 的血条颜色可能不同，留待补截图
            list.Add(Entry(FactionKind.Scp, "SCP", 2, 0.97, 0.45, 14, 0.30, 0.30));
            list.Add(Entry(FactionKind.Tutorial, "教程角色", 334, 0.47, 0.55, 18, 0.28, 0.25));
            return list;
        }

        private static FactionEntry Entry(FactionKind k, string name,
            double h, double s, double v, double ht, double st, double vt)
        {
            FactionEntry e = new FactionEntry();
            e.Kind = k; e.Name = name;
            e.Hue = h; e.Sat = s; e.Val = v;
            e.HueTol = ht; e.SatTol = st; e.ValTol = vt;
            return e;
        }

        /// <summary>一行一个：名称|H|S|V|HueTol|SatTol|ValTol。分号分隔多条。</summary>
        public static string Serialize(List<FactionEntry> list)
        {
            if (list == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                FactionEntry e = list[i];
                if (e == null) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(e.Name).Append('|')
                  .Append(e.Hue.ToString("0.#", CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.Sat.ToString("0.###", CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.Val.ToString("0.###", CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.HueTol.ToString("0.#", CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.SatTol.ToString("0.##", CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.ValTol.ToString("0.##", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static List<FactionEntry> Parse(string text)
        {
            List<FactionEntry> list = Defaults();
            if (string.IsNullOrEmpty(text)) return list;

            List<FactionEntry> parsed = new List<FactionEntry>();
            string[] lines = text.Split(';');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                string[] p = line.Split('|');
                if (p.Length < 4) continue;
                FactionEntry e = new FactionEntry();
                e.Name = p[0].Trim();
                if (e.Name.Length == 0) continue;
                e.Kind = GuessKind(e.Name);
                e.Hue = ReadDouble(p[1], 0);
                e.Sat = ReadDouble(p[2], 0.5);
                e.Val = ReadDouble(p[3], 0.5);
                if (p.Length > 4) e.HueTol = Math.Max(1, ReadDouble(p[4], 16));
                if (p.Length > 5) e.SatTol = Math.Max(0.01, ReadDouble(p[5], 0.30));
                if (p.Length > 6) e.ValTol = Math.Max(0.01, ReadDouble(p[6], 0.25));
                parsed.Add(e);
            }
            return parsed.Count > 0 ? parsed : list;
        }

        public static FactionKind GuessKind(string name)
        {
            if (string.IsNullOrEmpty(name)) return FactionKind.Unknown;
            if (name.IndexOf("079") >= 0) return FactionKind.Scp079;
            if (name.IndexOf("SCP", StringComparison.OrdinalIgnoreCase) >= 0) return FactionKind.Scp;
            if (name.IndexOf("九尾狐") >= 0 || name.IndexOf("MTF", StringComparison.OrdinalIgnoreCase) >= 0)
                return FactionKind.Mtf;
            if (name.IndexOf("混沌") >= 0) return FactionKind.Chaos;
            if (name.IndexOf("警卫") >= 0) return FactionKind.Guard;
            if (name.IndexOf("科学家") >= 0) return FactionKind.Scientist;
            if (name.IndexOf("D 级") >= 0 || name.IndexOf("D级") >= 0) return FactionKind.DClass;
            if (name.IndexOf("教程") >= 0) return FactionKind.Tutorial;
            return FactionKind.Unknown;
        }

        private static double ReadDouble(string s, double fallback)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        /// <summary>
        /// 判定一个颜色属于哪个阵营。
        /// 先按 HSV 做门限（色相环距离 / 饱和度 / 明度），再用加权欧氏距离做同分时的
        /// 次级比较 —— 单纯比 RGB 会被血条自带的明暗渐变带偏，所以权重给 HSV 更大。
        /// </summary>
        public static FactionResult Classify(Color c, List<FactionEntry> table, double rgbTol)
        {
            FactionResult r = new FactionResult();
            r.Rgb = c;
            double h, s, v;
            ToHsv(c, out h, out s, out v);
            r.Hue = h; r.Sat = s; r.Val = v;
            r.Raw = string.Format(CultureInfo.InvariantCulture,
                "H={0:0} S={1:0.00} V={2:0.00}", h, s, v);

            // 全黑 / 接近全透明（079 的屏幕没有血条）—— 单独交给调用方做 5 秒计时
            if (v <= 0.06 && s <= 0.55)
            {
                r.IsBlack = true;
                r.Score = 0;
                return r;
            }
            if (c.R < 12 && c.G < 12 && c.B < 14)
            {
                r.IsBlack = true;
                r.Score = 0;
                return r;
            }

            if (table == null) return r;
            FactionEntry best = null;
            double bestScore = double.MaxValue;
            for (int i = 0; i < table.Count; i++)
            {
                FactionEntry e = table[i];
                if (e == null || !e.Enabled) continue;

                double dh = Math.Abs(h - e.Hue);
                if (dh > 180) dh = 360 - dh;
                double ds = Math.Abs(s - e.Sat);
                double dv = Math.Abs(v - e.Val);

                // 任一项超出容差太多就直接排除，避免"最像的那个其实不像"也硬给结果
                if (dh > e.HueTol * 2.2) continue;
                if (ds > e.SatTol * 1.8 && s > 0.12) continue;

                double score = 0.70 * (dh / Math.Max(1, e.HueTol))
                             + 0.20 * (ds / Math.Max(0.01, e.SatTol))
                             + 0.10 * (dv / Math.Max(0.01, e.ValTol));
                if (score < bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }

            if (best == null || bestScore > 1.15) return r;

            r.Kind = best.Kind;
            r.Name = best.Name;
            r.Score = bestScore;
            r.RgbDistance = WeightedRgbDistance(c, HsvToRgb(best.Hue, best.Sat, best.Val));
            if (r.RgbDistance > rgbTol) return r;   // 颜色差太远：宁可"未知"也不乱报
            return r;
        }

        public static double WeightedRgbDistance(Color a, Color b)
        {
            double dr = (a.R - b.R) / 255.0;
            double dg = (a.G - b.G) / 255.0;
            double db = (a.B - b.B) / 255.0;
            return Math.Sqrt(0.30 * dr * dr + 0.59 * dg * dg + 0.11 * db * db);
        }

        public static Color HsvToRgb(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromArgb(
                (int)Math.Round((r + m) * 255),
                (int)Math.Round((g + m) * 255),
                (int)Math.Round((b + m) * 255));
        }

        public static void ToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            v = max;
            double d = max - min;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) { h = 0; return; }
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
            if (h < 0) h += 360;
        }
    }

    /// <summary>
    /// 采样器：在"游戏客户区"里按 1920×1080 实测的比例定位左下角血条，
    /// 只取最左 10% 的小条，多点平均。
    /// </summary>
    internal static class FactionSampler
    {
        // 1920×1080 实测：血条 x 50..410、y 1021..1055
        public const double BarX0 = 0.02604;
        public const double BarX1 = 0.21354;
        public const double BarY0 = 0.94537;
        public const double BarY1 = 0.97685;
        public const double LeftBand = 0.10;

        public static Rectangle BarRect(Rectangle client)
        {
            if (client.Width <= 0 || client.Height <= 0) return Rectangle.Empty;
            int x0 = client.Left + (int)Math.Round(client.Width * BarX0);
            int x1 = client.Left + (int)Math.Round(client.Width * BarX1);
            int y0 = client.Top + (int)Math.Round(client.Height * BarY0);
            int y1 = client.Top + (int)Math.Round(client.Height * BarY1);
            if (x1 - x0 < 4 || y1 - y0 < 3) return Rectangle.Empty;
            return new Rectangle(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>取血条最左 10% 的颜色（多点平均）。失败返回 null。</summary>
        public static Color? SampleLeft(Rectangle bar)
        {
            if (bar.Width < 4 || bar.Height < 3) return null;
            int bandWidth = Math.Max(3, (int)Math.Round(bar.Width * LeftBand));
            Rectangle sub = new Rectangle(bar.Left + 1, bar.Top, bandWidth, bar.Height);
            Bitmap bmp = null;
            try
            {
                bmp = BrightnessSampler.CaptureScaled(sub, sub.Width);
                if (bmp == null) return null;
                return Mean(bmp);
            }
            catch { return null; }
            finally { if (bmp != null) bmp.Dispose(); }
        }

        /// <summary>诊断用：抓一张完整血条区域的图（不缩放着色）。</summary>
        public static Bitmap CaptureDebug(Rectangle bar)
        {
            try
            {
                if (bar.Width < 4 || bar.Height < 3) return null;
                return BrightnessSampler.CaptureScaled(bar, bar.Width);
            }
            catch { return null; }
        }

        private static unsafe Color? Mean(Bitmap bmp)
        {
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                // 上下各让 25%：避开血条的高光边和阴影边
                int y0 = Math.Max(0, (int)(bmp.Height * 0.25));
                int y1 = Math.Min(bmp.Height - 1, (int)(bmp.Height * 0.75));
                if (y1 < y0) { y0 = 0; y1 = bmp.Height - 1; }
                long r = 0, g = 0, b = 0, n = 0;
                byte* p = (byte*)d.Scan0.ToPointer();
                for (int y = y0; y <= y1; y++)
                {
                    byte* row = p + y * d.Stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        byte* q = row + x * 3;
                        b += q[0]; g += q[1]; r += q[2];
                        n++;
                    }
                }
                if (n == 0) return null;
                return Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
            }
            finally { bmp.UnlockBits(d); }
        }
    }

    /// <summary>
    /// 一次识别（开局角色分配完成 / 复活）。后台线程跑，50ms 一帧。
    /// 成功后自动停；超过 MaxMs 或连续 5 秒全黑（→079）也会停。
    /// </summary>
    internal sealed class FactionRecognizer : IDisposable
    {
        public const int TickMs = 50;          // 20 帧/秒
        public const int BlackSeconds = 5;

        private readonly Config _cfg;
        private readonly Timer _timer;
        private int _busy;

        private volatile bool _active;
        private DateTime _startAt;
        private DateTime _deadline;
        private int _delayMs;
        private string _reason = "";

        private FactionKind _lastKind = FactionKind.Unknown;
        private int _sameCount;
        private DateTime _blackSince = DateTime.MinValue;
        private DateTime _startedAt;

        /// <summary>识别出结果（后台线程回调，调用方自己切 UI 线程）。</summary>
        public Action<FactionResult> Recognized;
        public Action<string> Note;
        public Action<string> GiveUp;

        public FactionRecognizer(Config cfg)
        {
            _cfg = cfg;
            _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool Active { get { return _active; } }
        public string Reason { get { return _reason; } }

        /// <summary>
        /// 开始一次识别。delayMs = 复活后延迟多久才认（需求：上限默认 3 秒）。
        /// maxMs 缺省按配置的 MaxSampleSeconds 算。
        /// </summary>
        public void Begin(string reason, int delayMs, int maxMs)
        {
            if (_cfg == null || !_cfg.FactionEnabled) return;
            _reason = reason;
            _delayMs = Math.Max(0, delayMs);
            _startAt = DateTime.UtcNow;
            _startedAt = _startAt;
            if (maxMs <= 0) maxMs = Math.Max(1, _cfg.FactionMaxSampleSeconds) * 1000;
            _deadline = _startAt.AddMilliseconds(_delayMs + maxMs);
            _lastKind = FactionKind.Unknown;
            _sameCount = 0;
            _blackSince = DateTime.MinValue;
            _active = true;
            _timer.Change(0, TickMs);
            Log("开始识别阵营（" + reason + "）"
                + (_delayMs > 0 ? "，延迟 " + (_delayMs / 1000.0).ToString("0.0") + " 秒" : "")
                + "，最多 " + (maxMs / 1000.0).ToString("0.0") + " 秒");
        }

        public void Stop()
        {
            _active = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void OnTick(object state)
        {
            if (!_active) return;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Log("阵营识别异常：" + ex.Message);
                Stop();
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        private void Tick()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _startAt).TotalMilliseconds < _delayMs) return;
            if (now > _deadline)
            {
                Stop();
                Log("超过采样时间还没认出阵营（" + _reason + "），本次按未知处理");
                SaveDebug("未识别", "超过采样时间");
                if (GiveUp != null) GiveUp(_reason);
                return;
            }

            IntPtr hwnd = ScreenGeometry.FindGameWindow();
            if (hwnd == IntPtr.Zero) return;
            Rectangle client = BrightnessSampler.ClientBounds(hwnd);
            Rectangle bar = FactionSampler.BarRect(client);
            if (bar.IsEmpty) return;

            Color? sample = FactionSampler.SampleLeft(bar);
            if (!sample.HasValue) return;

            FactionResult r = FactionTable.Classify(sample.Value, _cfg.FactionMap, _cfg.FactionRgbTolerance);
            if (r.IsBlack)
            {
                _sameCount = 0;
                _lastKind = FactionKind.Unknown;
                if (_blackSince == DateTime.MinValue) _blackSince = now;
                if ((now - _blackSince).TotalSeconds >= BlackSeconds)
                {
                    FactionResult dark = new FactionResult();
                    dark.Kind = FactionKind.Scp079;
                    dark.Name = "SCP-079";
                    dark.Rgb = sample.Value;
                    dark.IsBlack = true;
                    dark.Score = 0;
                    dark.Raw = "连续 " + BlackSeconds + " 秒全黑 / 无血条";
                    Stop();
                    Log("判定 SCP-079：血条区连续 " + BlackSeconds + " 秒全黑");
                    SaveDebug("SCP-079", "连续 5 秒全黑 / 无血条");
                    if (Recognized != null) Recognized(dark);
                }
                return;
            }

            _blackSince = DateTime.MinValue;
            if (r.Kind == FactionKind.Unknown)
            {
                _sameCount = 0;
                _lastKind = FactionKind.Unknown;
                return;
            }

            if (r.Kind == _lastKind) _sameCount++;
            else { _lastKind = r.Kind; _sameCount = 1; }

            if (_sameCount >= Math.Max(1, _cfg.FactionConfirmFrames))
            {
                Stop();
                Log("认出阵营：" + r.Name + "（" + r.Raw + "，连续 "
                    + _sameCount + " 帧，差异 " + r.Score.ToString("0.00") + "）");
                SaveDebug(r.Name, r.Raw + "，score " + r.Score.ToString("0.00"));
                if (Recognized != null) Recognized(r);
            }
        }

        /// <summary>每次识别结束都存一张血条截图 + txt，最多留 5 份，便于事后分析。</summary>
        private void SaveDebug(string result, string detail)
        {
            try
            {
                IntPtr hwnd = ScreenGeometry.FindGameWindow();
                if (hwnd == IntPtr.Zero) return;
                Rectangle client = BrightnessSampler.ClientBounds(hwnd);
                Rectangle bar = FactionSampler.BarRect(client);
                Bitmap bmp = FactionSampler.CaptureDebug(bar);
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "诊断", "阵营识别");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                string baseName = Path.Combine(dir, "阵营_" + stamp);
                if (bmp != null)
                {
                    bmp.Save(baseName + ".png", ImageFormat.Png);
                    bmp.Dispose();
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.AppendLine("原因: " + _reason);
                sb.AppendLine("延迟: " + _delayMs + " ms");
                sb.AppendLine("耗时: " + (DateTime.UtcNow - _startedAt).TotalMilliseconds.ToString("0") + " ms");
                sb.AppendLine("结果: " + result);
                sb.AppendLine("说明: " + detail);
                sb.AppendLine("血条矩形: " + bar);
                File.WriteAllText(baseName + ".txt", sb.ToString(), new UTF8Encoding(false));
                PruneDebug(dir, 5);
            }
            catch { }
        }

        private static void PruneDebug(string dir, int keep)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, "阵营_*.png");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                int remove = files.Length - keep;
                for (int i = 0; i < remove; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                    try { File.Delete(Path.ChangeExtension(files[i], ".txt")); } catch { }
                }
            }
            catch { }
        }

        private void Log(string msg)
        {
            Logger.On("阵营", msg);
            if (Note != null) { try { Note(msg); } catch { } }
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }

    /// <summary>
    /// 角色时间段：一局内"什么时间是什么角色 / 死没死"。
    /// </summary>
    internal sealed class RoleSegment
    {
        public DateTime Start;
        public DateTime End = DateTime.MaxValue;
        public string Role = "";
        public string State = "alive";     // alive / dead / spectator
        public string Note = "";

        public double Seconds(DateTime now)
        {
            DateTime e = End == DateTime.MaxValue ? now : End;
            return Math.Max(0, (e - Start).TotalSeconds);
        }

        public RoleSegment Clone()
        {
            RoleSegment s = new RoleSegment();
            s.Start = Start; s.End = End; s.Role = Role;
            s.State = State; s.Note = Note;
            return s;
        }
    }

    internal sealed class RoleTimeline : IDisposable
    {
        private readonly List<RoleSegment> _segments = new List<RoleSegment>();
        private readonly string _path;
        private int _round;
        private DateTime _roundStart = DateTime.MinValue;

        public RoleTimeline()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                _path = Path.Combine(dir, "对局记录");
                Directory.CreateDirectory(_path);
                _path = Path.Combine(_path, "角色时间段-"
                    + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
            }
            catch { _path = null; }
        }

        public string FilePath { get { return _path; } }

        public IList<RoleSegment> Segments { get { return _segments; } }

        public string CurrentRole
        {
            get
            {
                for (int i = _segments.Count - 1; i >= 0; i--)
                    if (!string.IsNullOrEmpty(_segments[i].Role)) return _segments[i].Role;
                return "";
            }
        }

        public string CurrentState
        {
            get
            {
                if (_segments.Count == 0) return "";
                return _segments[_segments.Count - 1].State;
            }
        }

        public void BeginRound(int round, DateTime now)
        {
            Close(now);
            _round = round;
            _roundStart = now;
            RoleSegment s = new RoleSegment();
            s.Start = now;
            s.Role = "";
            s.State = "alive";
            s.Note = "回合 " + round + " 开始";
            _segments.Add(s);
            Flush();
        }

        public void SetRole(string role, DateTime now)
        {
            if (string.IsNullOrEmpty(role)) return;
            if (string.Equals(CurrentRole, role, StringComparison.Ordinal)) return;
            RoleSegment last = _segments.Count > 0 ? _segments[_segments.Count - 1] : null;
            if (last != null && string.IsNullOrEmpty(last.Role))
            {
                last.Role = role;
                last.Note = (last.Note == null ? "" : last.Note + "；") + "认出角色";
                Flush();
                return;
            }
            Close(now);
            RoleSegment s = new RoleSegment();
            s.Start = now;
            s.Role = role;
            s.State = "alive";
            s.Note = "角色切换";
            _segments.Add(s);
            Flush();
        }

        public void SetState(string state, DateTime now)
        {
            SetState(state, now, false);
        }

        /// <summary>clearRole=true：复活时不知道新角色，先把角色清空，等识别结果再填。</summary>
        public void SetState(string state, DateTime now, bool clearRole)
        {
            if (string.IsNullOrEmpty(state)) return;
            if (_segments.Count == 0) return;
            RoleSegment last = _segments[_segments.Count - 1];
            if (last.State == state) return;
            string role = clearRole ? "" : last.Role;
            Close(now);
            RoleSegment s = new RoleSegment();
            s.Start = now;
            s.Role = role;
            s.State = state;
            s.Note = state == "dead" ? "阵亡 / 观战" : "复活";
            _segments.Add(s);
            Flush();
        }

        /// <summary>一局结束：把最后一段收尾。</summary>
        public void EndRound(DateTime now)
        {
            Close(now);
        }

        private void Close(DateTime now)
        {
            if (_segments.Count == 0) return;
            RoleSegment last = _segments[_segments.Count - 1];
            if (last.End == DateTime.MaxValue) last.End = now;
        }

        /// <summary>给界面用的一行摘要：D 级人员 01:12 → 九尾狐 03:40。</summary>
        public string Summary(int maxItems)
        {
            if (_segments.Count == 0) return "";
            DateTime now = DateTime.Now;
            StringBuilder sb = new StringBuilder();
            int shown = 0;
            for (int i = _segments.Count - 1; i >= 0 && shown < maxItems; i--)
            {
                RoleSegment s = _segments[i];
                string label = string.IsNullOrEmpty(s.Role) ? "（未识别）" : s.Role;
                string line = label + " " + Mmss(s.Seconds(now));
                if (sb.Length > 0) sb.Insert(0, " → ");
                sb.Insert(0, line);
                shown++;
            }
            return sb.ToString();
        }

        private static string Mmss(double seconds)
        {
            int s = (int)Math.Max(0, seconds);
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        public void Flush()
        {
            if (string.IsNullOrEmpty(_path)) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("回合,开始,结束,秒,阵营,状态,备注");
                DateTime now = DateTime.Now;
                for (int i = 0; i < _segments.Count; i++)
                {
                    RoleSegment s = _segments[i];
                    sb.Append(_round).Append(',')
                      .Append(s.Start.ToString("HH:mm:ss")).Append(',')
                      .Append(s.End == DateTime.MaxValue ? "" : s.End.ToString("HH:mm:ss")).Append(',')
                      .Append(((int)s.Seconds(now)).ToString()).Append(',')
                      .Append(Csv(s.Role)).Append(',')
                      .Append(Csv(s.State)).Append(',')
                      .AppendLine(Csv(s.Note));
                }
                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public void Dispose()
        {
            EndRound(DateTime.Now);
            Flush();
        }
    }
}
