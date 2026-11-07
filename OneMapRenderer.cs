using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SCPSLAlert
{
    /// <summary>一图流的导出参数（黑/白主题、抽稀、标注上限、水印）。</summary>
    internal sealed class OneMapOptions
    {
        public bool Light;
        public double Width = 1600;
        public int Lttb = OneMapMath.DefaultLttb;
        public int Labels = OneMapMath.DefaultLabels;
        public bool ShowHeart = true;
        public bool ShowPlayerName;
        /// <summary>FPS 是否画平滑线（默认原始）。</summary>
        public bool SmoothFps;
        /// <summary>是否画净化/支援/发电机等其他持续事件（默认关）。</summary>
        public bool ShowExtraDurations;
        public string PlayerName = "";
        public DateTime ExportAt = DateTime.Now;
        public string Version = AppInfo.Version;
        public string Title = "一图流";
    }

    /// <summary>
    /// 把一局 MatchRecord 渲染成静态图（一图流）。
    /// 每个回合一块：SCP 阵容 → 性能轨 → 双时间轴（事件在轴上）→ 状态轨（079 电弧）→ 生理轨。
    /// 不依赖主界面；配色由 OneMapPalette 传入。
    /// </summary>
    internal static class OneMapRenderer
    {
        private const double Margin = 56;

        private static readonly Typeface Ui = new Typeface(new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface UiBold = new Typeface(new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Typeface Mono = new Typeface(new FontFamily("Consolas"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface MonoBold = new Typeface(new FontFamily("Consolas"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Typeface Display = new Typeface(
            new FontFamily("Bahnschrift SemiCondensed, Bahnschrift, Segoe UI"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private static readonly Dictionary<Color, SolidColorBrush> Brushes =
            new Dictionary<Color, SolidColorBrush>();

        public sealed class OneMapPalette
        {
            public Color Bg, Panel, PanelAlt, Line, Ink, Sub, Muted;
            public Color Accent, Accent2, Danger, Warn, Gpu;
            public Color Guard, Fox, DClass, Scp, Heart, Chaos, Scientist, Tutorial;

            public static OneMapPalette For(bool light)
            {
                OneMapPalette p = new OneMapPalette();
                if (!light)
                {
                    p.Bg = C(7, 11, 16); p.Panel = C(12, 18, 25); p.PanelAlt = C(16, 26, 36);
                    p.Line = C(27, 43, 54); p.Ink = C(230, 244, 248); p.Sub = C(154, 176, 188);
                    p.Muted = C(92, 116, 128); p.Accent = C(56, 214, 255); p.Accent2 = C(47, 123, 255);
                    p.Danger = C(255, 107, 107); p.Warn = C(255, 176, 58); p.Gpu = C(185, 140, 255);
                    p.Guard = C(111, 168, 200); p.Fox = C(47, 123, 255); p.DClass = C(214, 143, 74);
                    p.Scp = C(255, 107, 107); p.Heart = C(255, 107, 107);
                    p.Chaos = C(123, 216, 143); p.Scientist = C(212, 193, 90); p.Tutorial = C(226, 132, 196);
                }
                else
                {
                    p.Bg = C(242, 245, 247); p.Panel = C(255, 255, 255); p.PanelAlt = C(237, 242, 245);
                    p.Line = C(214, 223, 229); p.Ink = C(10, 23, 32); p.Sub = C(74, 93, 108);
                    p.Muted = C(132, 150, 164); p.Accent = C(0, 132, 163); p.Accent2 = C(11, 99, 206);
                    p.Danger = C(192, 57, 43); p.Warn = C(178, 106, 0); p.Gpu = C(110, 79, 196);
                    p.Guard = C(78, 127, 153); p.Fox = C(30, 99, 200); p.DClass = C(160, 108, 40);
                    p.Scp = C(192, 57, 43); p.Heart = C(192, 57, 43);
                    p.Chaos = C(38, 122, 63); p.Scientist = C(150, 122, 20); p.Tutorial = C(160, 62, 130);
                }
                return p;
            }
        }

        // ============================================================
        //  渲染
        // ============================================================
        public static BitmapSource Render(MatchRecord m, OneMapOptions o, OneMapPalette p)
        {
            if (o == null) o = new OneMapOptions();
            if (p == null) p = OneMapPalette.For(o.Light);
            if (m == null) m = new MatchRecord();

            List<RoundLayout> layout = Measure(m, o);
            double h = layout[layout.Count - 1].Bottom + 40;

            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(B(p.Bg), null, new Rect(0, 0, o.Width, h));
                DrawHeader(dc, m, o, p, h);

                for (int i = 0; i < layout.Count; i++)
                    DrawRound(dc, m, m.Rounds[i], layout[i], o, p);
            }

            RenderTargetBitmap bmp = new RenderTargetBitmap((int)o.Width, (int)Math.Ceiling(h),
                96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        private sealed class RoundLayout
        {
            public double Top;
            public double LineupTop, PerfTop, AxisY, StatusTop, ArcTop, PhysioTop;
            public double Bottom;
            public bool HasArc, HasHeart, Compact;
        }

        private static List<RoundLayout> Measure(MatchRecord m, OneMapOptions o)
        {
            List<RoundLayout> list = new List<RoundLayout>();
            double y = 128;   // 页头
            for (int i = 0; i < m.Rounds.Count; i++)
            {
                MatchRound r = m.Rounds[i];
                RoundLayout l = new RoundLayout();
                l.Top = y;
                l.LineupTop = y + 24;
                l.PerfTop = l.LineupTop + 52;
                l.AxisY = l.PerfTop + 360;
                l.StatusTop = l.AxisY + 44;
                l.Compact = r.Seconds < 30;
                if (l.Compact)
                {
                    l.Bottom = l.LineupTop + 52 + 26;
                    y = l.Bottom;
                    list.Add(l);
                    continue;
                }
                l.HasArc = ArcInfo(r, o) != null;
                l.ArcTop = l.StatusTop + 62;
                l.HasHeart = o.ShowHeart && HasHeart(r);
                l.PhysioTop = l.ArcTop + (l.HasArc ? 34 : 10);
                l.Bottom = l.PhysioTop + (l.HasHeart ? 84 : 0) + 28;
                y = l.Bottom;
                list.Add(l);
            }
            if (list.Count == 0)
            {
                RoundLayout l = new RoundLayout();
                l.Top = 128; l.LineupTop = 152; l.PerfTop = 204; l.AxisY = 400;
                l.StatusTop = 444; l.ArcTop = 506; l.PhysioTop = 528; l.Bottom = 620;
                list.Add(l);
            }
            return list;
        }

        private static void DrawHeader(DrawingContext dc, MatchRecord m, OneMapOptions o, OneMapPalette p, double h)
        {
            Text(dc, "SCPSLAlert  //  A.R.C.T", 9.5, B(p.Accent), Margin, 30, Mono);
            Text(dc, o.Title, 34, B(p.Ink), Margin + 28, 44, Display);
            Emblem(dc, Margin + 9, 58, 18, p.Accent, 235, true);

            TextRight(dc, "数据来源  SCPSLAlert · A.R.C.T", 8.5, B(p.Sub), o.Width - Margin, 30, Mono);
            string ver = "v" + (o.Version ?? AppInfo.Version) + "  ·  导出 " + o.ExportAt.ToString("yyyy-MM-dd HH:mm");
            if (o.ShowPlayerName && !string.IsNullOrEmpty(o.PlayerName)) ver = o.PlayerName + "  ·  " + ver;
            TextRight(dc, ver, 8.5, B(p.Muted), o.Width - Margin, 44, Mono);

            string meta = "对局 " + (string.IsNullOrEmpty(m.Id) ? "--" : m.Id)
                + "   ·   回合 " + (m.Rounds.Count > 0 ? m.Rounds[m.Rounds.Count - 1].Index : 0).ToString("00")
                + "   ·   本机视角   ·   LTTB " + o.Lttb
                + "   ·   标注上限 " + o.Labels;
            Text(dc, meta, 9.5, B(p.Sub), Margin, 88, Ui);
            Line(dc, p.Line, Margin, 106, o.Width - Margin, 106, 1);
        }

        private static void DrawRound(DrawingContext dc, MatchRecord m, MatchRound r, RoundLayout l,
            OneMapOptions o, OneMapPalette p)
        {
            double plotL = Margin + 44;
            double plotR = o.Width - Margin;
            double plotW = plotR - plotL;
            double dur = Math.Max(1, r.Seconds);

            // 回合标题
            Text(dc, "回合 " + r.Index.ToString("00"), 12, B(p.Ink), Margin, l.Top, UiBold);
            Text(dc, r.Start.ToString("HH:mm:ss") + " → " + r.EffectiveEnd.ToString("HH:mm:ss"),
                10, B(p.Sub), Margin + 76, l.Top + 1, Mono);
            TextRight(dc, "时长 " + Clock(dur), 10, B(p.Sub), o.Width - Margin, l.Top + 1, Mono);

            // SCP 阵容
            DrawLineup(dc, r, l.LineupTop, plotL, plotR, p);

            if (l.Compact)
            {
                Text(dc, "短回合 · 时长 " + Clock(dur) + " · 事件 " + r.Events.Count + " 个 · 角色 "
                    + (r.Roles.Count > 0 && !string.IsNullOrEmpty(r.Roles[0].Role) ? r.Roles[0].Role : "未识别"),
                    9, B(p.Muted), Margin, l.LineupTop + 50, Ui);
                return;
            }

            // 性能轨
            RectFill(dc, p.Panel, Margin, l.PerfTop, o.Width - Margin * 2, 304);
            dc.DrawRoundedRectangle(null, new Pen(B(p.Line), 1),
                new Rect(Margin, l.PerfTop, o.Width - Margin * 2, 304), 4, 4);
            Emblem(dc, 1330, l.PerfTop + 100, 172, p.Accent, o.Light ? (byte)8 : (byte)7, false);
            Text(dc, "PERF  /  性能轨", 8.5, B(p.Accent), Margin + 16, l.PerfTop + 12, Mono);
            // 三条轨共用外框和时间网格，但各有独立纵轴：看起来是一张大坐标系，实际互不压。
            double laneTop = l.PerfTop + 40;
            double fpsH = 96, cgH = 56, laneGap = 18;
            Rect fpsLane = new Rect(plotL, laneTop, plotW, fpsH);
            Rect cpuLane = new Rect(plotL, fpsLane.Bottom + laneGap, plotW, cgH);
            Rect gpuLane = new Rect(plotL, cpuLane.Bottom + laneGap, plotW, cgH);

            // 共享的时间竖线（贯穿三条轨）
            for (int i = 0; i <= 4; i++)
            {
                double xx = plotL + plotW * i / 4.0;
                Line(dc, Alpha(p.Line, 55), xx, laneTop - 4, xx, gpuLane.Bottom + 2, 0.7);
            }

            LaneFrame(dc, p, fpsLane, "FPS", 144, p.Accent);
            LaneFrame(dc, p, cpuLane, "CPU", 100, p.Warn);
            LaneFrame(dc, p, gpuLane, "GPU", 100, p.Gpu);

            DrawTrack(dc, r, o, p, fpsLane, 0, 144, p.Accent, delegate(MatchSample s) { return s.HasFps; },
                delegate(MatchSample s) { return s.Fps; }, "FPS", true);
            DrawTrack(dc, r, o, p, cpuLane, 0, 100, p.Warn, delegate(MatchSample s) { return true; },
                delegate(MatchSample s) { return s.Cpu; }, "CPU", false);
            DrawTrack(dc, r, o, p, gpuLane, 0, 100, p.Gpu, delegate(MatchSample s) { return true; },
                delegate(MatchSample s) { return s.Gpu; }, "GPU", false);

            // 时间轴
            double axisY = l.AxisY;
            Line(dc, Mix(p.Line, p.Sub, 0.35), plotL - 12, axisY, plotR, axisY, 1.4);
            for (int i = 0; i <= 4; i++)
            {
                double t = dur * i / 4.0;
                double xx = plotL + plotW * i / 4.0;
                Line(dc, Mix(p.Line, p.Sub, 0.35), xx, axisY - 4, xx, axisY + 4, 1.2);
                TextCenter(dc, Clock(t), 8.5, B(p.Sub), xx, axisY + 4, Mono);
                TextCenter(dc, r.Start.AddSeconds(t).ToString("HH:mm:ss"), 7.5, B(p.Muted), xx, axisY + 17, Mono);
            }
            Text(dc, "回合内", 7, B(p.Muted), 8, axisY + 4, Ui);
            Text(dc, "现实", 7, B(p.Muted), 8, axisY + 17, Ui);

            // 事件（轴上两行）
            for (int i = 0; i < r.Events.Count; i++)
            {
                MatchEvent e = r.Events[i];
                if (e.HasEnd) continue;   // 持续事件画在下面的持续线上
                double ex = X((e.At - r.Start).TotalSeconds, dur, plotL, plotW);
                if (ex < plotL - 2 || ex > plotR + 2) continue;
                Color ec = EventColor(e.Kind, p);
                RectFill(dc, ec, ex - 1, axisY - 5, 2, 10);
                double ey = axisY - 52 + (i % 2) * 14;
                Dash(dc, Alpha(p.Sub, 55), ex, axisY + 2, ex, l.PhysioTop + (l.HasHeart ? 70 : 0), 0.9, 3, 4);
                Line(dc, Alpha(ec, 80), ex, ey + 11, ex, axisY - 5, 0.7);
                string txt = e.Label + " " + Clock((e.At - r.Start).TotalSeconds);
                double tw = Measure(txt, 7.5, Ui);
                double tx = ex + 5;
                if (tx + tw > plotR) tx = ex - tw - 5;
                Text(dc, txt, 7.5, B(ec), tx, ey, Ui);
            }


            // 持续事件：从起点向右画一条持续线（终焉 / 核弹 / 净化）
            int durLane = 0;
            for (int i = 0; i < r.Events.Count; i++)
            {
                MatchEvent e = r.Events[i];
                if (!e.HasEnd) continue;
                double x1 = X((e.At - r.Start).TotalSeconds, dur, plotL, plotW);
                double x2 = X((e.EndAt - r.Start).TotalSeconds, dur, plotL, plotW);
                if (x2 < x1) { double tswap = x1; x1 = x2; x2 = tswap; }
                double dy = axisY - 10 - (durLane % 2) * 12;
                durLane++;
                Color c = EventColor(e.Kind, p);
                Line(dc, c, x1, dy, x2, dy, 2.2);
                Line(dc, c, x2, dy - 4, x2, dy + 4, 2.2);
                string lbl = string.IsNullOrEmpty(e.Label) ? e.Kind : e.Label;
                Text(dc, lbl, 7.5, B(c), Math.Min(x1 + 4, plotR - 90), dy - 24, Ui);
            }

            // 状态轨
            DrawStatus(dc, r, l, o, p, plotL, plotR, plotW, dur);

            if (l.HasHeart) DrawHeart(dc, r, l, o, p, plotL, plotW, dur);
        }

        // ---------------- 阵容 ----------------
        private static void DrawLineup(DrawingContext dc, MatchRound r, double top, double left, double right, OneMapPalette p)
        {
            RectFill(dc, p.Panel, Margin, top, right - Margin, 40);
            dc.DrawRoundedRectangle(null, new Pen(B(p.Line), 1), new Rect(Margin, top, right - Margin, 40), 4, 4);
            Text(dc, "SCP 阵容", 8.5, B(p.Accent), Margin + 16, top + 8, Mono);

            double cx = Margin + 110;
            for (int i = 0; i < r.Scps.Count; i++)
            {
                ScpPresence s = r.Scps[i];
                bool mine = s.WasPlayerRole;
                double cw = mine ? 74 : 58;
                if (cx + cw > right - 10) break;
                RectFill(dc, mine ? Mix(p.PanelAlt, p.Scp, 0.18) : Mix(p.PanelAlt, p.Accent2, 0.12),
                    cx, top + 6, cw, 26);
                dc.DrawRectangle(null, new Pen(B(mine ? p.Scp : Mix(p.Line, p.Accent2, 0.45)), mine ? 1.4 : 1),
                    new Rect(cx, top + 6, cw, 26));
                TextCenter(dc, "SCP-" + s.Code, 9.5, B(mine ? p.Scp : p.Sub), cx + cw / 2, top + 11, Mono);
                cx += cw + 8;
            }
            if (r.Scps.Count == 0)
                Text(dc, "本回合没有识别到 SCP", 8, B(p.Muted), Margin + 110, top + 14, Ui);
        }

        // ---------------- 折线 ----------------
        /// <summary>一条轨的边框 + 左轴刻度 + 轨名；三条轨共用时间网格。</summary>
        private static void LaneFrame(DrawingContext dc, OneMapPalette p, Rect r, string tag, double max, Color color)
        {
            dc.DrawRectangle(B(Alpha(color, 6)), null, r);
            Line(dc, Alpha(p.Line, 85), r.Left, r.Top, r.Right, r.Top, 0.7);
            Line(dc, Alpha(p.Line, 85), r.Left, r.Bottom, r.Right, r.Bottom, 0.7);

            TextRight(dc, max.ToString("0"), 7, B(p.Muted), r.Left - 6, r.Top + 1, Mono);
            TextRight(dc, "0", 7, B(p.Muted), r.Left - 6, r.Bottom - 9, Mono);
            TextRight(dc, tag, 7.5, B(Alpha(color, 235)), r.Right - 6, r.Top + 2, Mono);
        }
        private static void DrawTrack(DrawingContext dc, MatchRound r, OneMapOptions o, OneMapPalette p,
            Rect rect, double min, double max, Color color,
            Func<MatchSample, bool> ok, Func<MatchSample, double> value, string tag, bool label)
        {
            List<int> idx = OneMapMath.Lttb(r.Samples, value, ok, o.Lttb);
            if (idx.Count < 2) return;
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(Pt(r, idx[0], rect, min, max, value), false, false);
                for (int i = 1; i < idx.Count; i++)
                    ctx.LineTo(Pt(r, idx[i], rect, min, max, value), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(B(Alpha(color, 40)), 3.0), geo);
            dc.DrawGeometry(null, new Pen(B(color), 1.5), geo);

            if (!label) return;
            int minI, maxI;
            List<int> marks = OneMapMath.Labels(r.Samples, value, ok, o.Labels, out minI, out maxI);
            for (int i = 0; i < marks.Count; i++)
            {
                int at = marks[i];
                Point pt = Pt(r, at, rect, min, max, value);
                bool isMax = at == maxI;
                bool isMin = at == minI;
                if (isMax || isMin)
                    dc.DrawEllipse(B(color), null, pt, 2.4, 2.4);
                double ly = isMax ? pt.Y - 12 : pt.Y + 3;
                if (ly < rect.Top + 8) ly = rect.Top + 8;
                if (ly > rect.Bottom - 10) ly = rect.Bottom - 10;
                string v = value(r.Samples[at]).ToString("0");
                Text(dc, v, 7.5, B(Alpha(color, 235)), Math.Min(pt.X + 4, rect.Right - 26), ly, Mono);
            }
        }

        private static Point Pt(MatchRound r, int index, Rect rect, double min, double max,
            Func<MatchSample, double> value)
        {
            MatchSample s = r.Samples[index];
            double t = r.Seconds <= 0 ? 0 : (s.At - r.Start).TotalSeconds / r.Seconds;
            double v = value(s);
            double ny = (v - min) / Math.Max(0.0001, max - min);
            if (ny < 0) ny = 0;
            if (ny > 1) ny = 1;
            return new Point(rect.Left + t * rect.Width, rect.Bottom - ny * rect.Height);
        }

        // ---------------- 状态轨 + 079 ----------------
        private static void DrawStatus(DrawingContext dc, MatchRound r, RoundLayout l, OneMapOptions o,
            OneMapPalette p, double plotL, double plotR, double plotW, double dur)
        {
            double top = l.StatusTop;
            Text(dc, "STATE  /  状态轨", 8.5, B(p.Accent), Margin + 16, top - 13, Mono);
            RectFill(dc, p.Panel, plotL - 12, top, plotW + 12, 56);

            List<MatchRoleSpan> spans = RoleSpans(r);
            for (int i = 0; i < spans.Count; i++)
            {
                MatchRoleSpan s = spans[i];
                double t0 = Math.Max(0, (s.Start - r.Start).TotalSeconds);
                double t1 = s.End == DateTime.MaxValue ? dur : (s.End - r.Start).TotalSeconds;
                if (t1 > dur) t1 = dur;
                if (t1 <= t0) continue;
                double x0 = plotL + t0 / dur * plotW;
                double x1 = plotL + t1 / dur * plotW;

                if (string.Equals(s.State, "dead", StringComparison.OrdinalIgnoreCase))
                {
                    DeathBlock(dc, new Rect(x0, top, x1 - x0, 54), p);
                    continue;
                }
                Color c = RoleColor(s.Role, p);
                bool unknownRole = string.IsNullOrEmpty(s.Role);
                string label = unknownRole ? "未识别" : s.Role;
                if (unknownRole)
                {
                    RectFill(dc, Mix(p.Panel, p.Muted, 0.35), x0, top + 20, x1 - x0, 14);
                    if (x1 - x0 > 40) Text(dc, label, 8, B(p.Muted), x0 + 7, top + 19, Ui);
                }
                else
                {
                    RectFill(dc, Mix(p.Panel, c, 0.74), x0, top, x1 - x0, 54);
                    RectFill(dc, c, x0, top, 2, 54);
                    if (x1 - x0 > 46) Text(dc, label, 8.5, B(p.Ink), x0 + 7, top + 20, Ui);
                }

                if (!string.IsNullOrEmpty(s.Role) && label.IndexOf("079", StringComparison.Ordinal) >= 0)
                {
                    // 本机扮演 079：这块用四边电弧包起来
                    ArcLine(dc, x0, top - 3, x0, top + 57, 4.5, 2, p.Accent, 1, true);
                    ArcLine(dc, x0, top, x1, top, 3.5, 2, p.Accent, 1, true);
                    ArcLine(dc, x1, top, x1, top + 54, 3.5, 2, p.Accent, 1, true);
                    ArcLine(dc, x0, top + 54, x1, top + 54, 3.5, 2, p.Accent, 1, true);
                }
            }

            // 非本机 079：在时间轴上画存活区间电弧
            ArcWindow w = ArcInfo(r, o);
            if (w != null)
            {
                double x0 = plotL + Math.Max(0, (w.Start - r.Start).TotalSeconds) / dur * plotW;
                double x1 = plotL + Math.Min(dur, (w.End - r.Start).TotalSeconds) / dur * plotW;
                if (x1 < x0) x1 = x0;
                double ay = l.ArcTop;
                Text(dc, "SCP-079 存活区间", 7.5, B(p.Accent), Margin + 16, ay - 2, Ui);
                ArcLine(dc, x0, ay + 6, x1, ay + 6, 4.2, 2, p.Accent, 1, true);
            }
        }

        private static void DrawHeart(DrawingContext dc, MatchRound r, RoundLayout l, OneMapOptions o,
            OneMapPalette p, double plotL, double plotW, double dur)
        {
            double top = l.PhysioTop;
            Text(dc, "PHYSIO  /  生理轨", 8.5, B(p.Accent), Margin + 16, top - 13, Mono);
            RectFill(dc, p.Panel, plotL - 12, top, plotW + 12, 78);
            Rect rect = new Rect(plotL, top, plotW, 78);
            for (int i = 0; i <= 3; i++)
            {
                double yy = rect.Top + rect.Height * i / 3.0;
                Line(dc, Alpha(p.Line, 90), rect.Left, yy, rect.Right, yy, 0.7);
                TextRight(dc, (130 - 30 * i).ToString("0"), 7.5, B(p.Muted), rect.Left - 8, yy - 6, Mono);
            }
            Text(dc, "BPM", 7.5, B(p.Muted), rect.Right - 6, rect.Top - 12, Mono);
            DrawTrack(dc, r, o, p, rect, 40, 130, p.Heart,
                delegate(MatchSample s) { return s.Bpm > 0; },
                delegate(MatchSample s) { return s.Bpm; }, "BPM", true);
        }
        private sealed class ArcWindow
        {
            public DateTime Start, End;
        }

        private static ArcWindow ArcInfo(MatchRound r, OneMapOptions o)
        {
            DateTime s, e;
            bool fromScan;
            if (!Scp079Window.TryGet(r, out s, out e, out fromScan)) return null;
            ScpPresence p = Scp079Window.Find(r);
            if (p != null && p.WasPlayerRole) return null;   // 本机扮演时走包边，不再画区间
            ArcWindow w = new ArcWindow();
            w.Start = s;
            w.End = e;
            return w;
        }

        private static List<MatchRoleSpan> RoleSpans(MatchRound r)
        {
            List<MatchRoleSpan> list = new List<MatchRoleSpan>();
            foreach (MatchRoleSpan s in r.Roles) list.Add(s);
            if (list.Count == 0)
            {
                // 没有角色段时，用采样的 role/state 兜底
                MatchRoleSpan cur = null;
                for (int i = 0; i < r.Samples.Count; i++)
                {
                    MatchSample s = r.Samples[i];
                    if (cur == null || cur.Role != s.Role || cur.State != s.State)
                    {
                        if (cur != null) cur.End = s.At;
                        cur = new MatchRoleSpan();
                        cur.Start = s.At;
                        cur.Role = s.Role;
                        cur.State = s.State;
                        list.Add(cur);
                    }
                }
                if (cur != null) cur.End = r.EffectiveEnd;
            }
            return list;
        }

        private static Color RoleColor(string role, OneMapPalette p)
        {
            string s = role ?? "";
            if (s.IndexOf("079", StringComparison.Ordinal) >= 0) return p.Scp;
            if (s.IndexOf("九尾", StringComparison.Ordinal) >= 0) return p.Fox;
            if (s.IndexOf("混沌", StringComparison.Ordinal) >= 0) return p.Chaos;
            if (s.IndexOf("科学", StringComparison.Ordinal) >= 0) return p.Scientist;
            if (s.IndexOf("教程", StringComparison.Ordinal) >= 0) return p.Tutorial;
            if (s.IndexOf("警卫", StringComparison.Ordinal) >= 0) return p.Guard;
            if (s.IndexOf("D", StringComparison.OrdinalIgnoreCase) >= 0) return p.DClass;
            if (s.IndexOf("SCP", StringComparison.OrdinalIgnoreCase) >= 0) return p.Scp;
            if (s.Length == 0) return p.Muted;
            return p.Accent2;
        }

        private static void DeathBlock(DrawingContext dc, Rect r, OneMapPalette p)
        {
            RectFill(dc, Mix(p.Bg, p.Danger, 0.10), r.X, r.Y, r.Width, r.Height);
            dc.PushClip(new RectangleGeometry(r));
            for (double px = r.X - r.Height; px < r.Right + r.Height; px += 8)
                Line(dc, Alpha(p.Danger, 42), px, r.Bottom, px + r.Height, r.Top, 1);
            dc.Pop();
            if (r.Width > 18) Text(dc, "死亡", 8, B(Alpha(p.Danger, 235)), r.X + 4, r.Y + 20, Ui);
        }

        // ---------------- 电弧 ----------------
        private static void ArcLine(DrawingContext dc, double x1, double y1, double x2, double y2,
            double amp, int density, Color color, int lines, bool glow)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 6) return;
            double ux = dx / len, uy = dy / len;
            double nx = -uy, ny = ux;
            double step = density <= 0 ? 12 : (density == 1 ? 8 : (density == 2 ? 5.5 : 4));
            int count = Math.Max(3, (int)(len / step));
            Random rnd = new Random(1200 + density * 71 + lines * 13 + (int)len);

            for (int line = 0; line < Math.Max(1, lines); line++)
            {
                double baseOff = (line - (lines - 1) / 2.0) * 2.8;
                List<Point> pts = new List<Point>(count + 1);
                double walk = 0;
                for (int i = 0; i <= count; i++)
                {
                    double t = (double)i / count;
                    walk += (rnd.NextDouble() - 0.5) * amp * 0.9;
                    if (rnd.NextDouble() < 0.16) walk += (rnd.NextDouble() - 0.5) * amp * 1.8;
                    if (walk > amp * 1.5) walk = amp * 1.5;
                    if (walk < -amp * 1.5) walk = -amp * 1.5;
                    double off = baseOff + walk;
                    pts.Add(new Point(x1 + ux * len * t + nx * off, y1 + uy * len * t + ny * off));
                }
                if (glow && line == 0)
                    dc.DrawGeometry(null, new Pen(B(Alpha(color, 30)), 3.4), Poly(pts));
                Pen main = new Pen(B(Alpha(color, line == 0 ? (byte)255 : (byte)195)), line == 0 ? 1.15 : 0.9);
                main.StartLineCap = PenLineCap.Round;
                main.EndLineCap = PenLineCap.Round;
                dc.DrawGeometry(null, main, Poly(pts));

                double gap = density <= 0 ? 9999 : (density == 1 ? 70 : density == 2 ? 40 : 24);
                int branches = (int)(len / gap);
                for (int b = 0; b < branches; b++)
                {
                    int idx = 2 + rnd.Next(Math.Max(1, pts.Count - 4));
                    if (idx >= pts.Count) idx = pts.Count - 1;
                    Point p0 = pts[idx];
                    double dir = rnd.NextDouble() < 0.5 ? -1 : 1;
                    double blen = 5 + rnd.NextDouble() * 5;
                    Point p1 = new Point(p0.X + nx * dir * blen * 0.55 + ux * blen * 0.45,
                        p0.Y + ny * dir * blen * 0.55 + uy * blen * 0.45);
                    Point p2 = new Point(p0.X + nx * dir * blen + ux * blen * 0.2,
                        p0.Y + ny * dir * blen + uy * blen * 0.2);
                    StreamGeometry g = new StreamGeometry();
                    using (StreamGeometryContext ctx = g.Open())
                    {
                        ctx.BeginFigure(p0, false, false);
                        ctx.LineTo(p1, true, false);
                        ctx.LineTo(p2, true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(null, new Pen(B(Alpha(color, 150)), 0.85), g);
                }
            }
        }

        private static StreamGeometry Poly(List<Point> pts)
        {
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                for (int i = 1; i < pts.Count; i++) ctx.LineTo(pts[i], true, false);
            }
            g.Freeze();
            return g;
        }

        // ---------------- 小工具 ----------------
        private static bool HasHeart(MatchRound r)
        {
            for (int i = 0; i < r.Samples.Count; i++) if (r.Samples[i].Bpm > 0) return true;
            return false;
        }

        private static Color EventColor(string kind, OneMapPalette p)
        {
            switch (kind)
            {
                case "gen": return p.Warn;
                case "warhead": return p.Accent2;
                case "deadman": return p.Danger;
                case "death": return p.Danger;
                case "respawn": return p.Guard;
                case "decon": return p.Gpu;
                case "scp": return p.Danger;
                default: return p.Accent2;
            }
        }

        private static void Emblem(DrawingContext dc, double cx, double cy, double size, Color color, byte alpha, bool mark)
        {
            double maxR = size / 2 - 2;
            double r = maxR * 0.82;
            Pen pen = new Pen(B(Alpha(color, alpha)), Math.Max(0.7, size / 90.0));
            dc.DrawGeometry(null, pen, Polygon(Hex(cx, cy, r, -Math.PI / 2), true));
            Point[] d = new Point[]
            {
                new Point(cx, cy - r * 0.30), new Point(cx + r * 0.30, cy),
                new Point(cx, cy + r * 0.30), new Point(cx - r * 0.30, cy)
            };
            dc.DrawGeometry(null, pen, Polygon(d, true));
            if (mark) dc.DrawEllipse(B(Alpha(color, alpha)), null, new Point(cx, cy),
                Math.Max(1.2, size * 0.035), Math.Max(1.2, size * 0.035));
        }

        private static Point[] Hex(double cx, double cy, double r, double start)
        {
            Point[] p = new Point[6];
            for (int i = 0; i < 6; i++)
            {
                double a = start + i * Math.PI / 3;
                p[i] = new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
            }
            return p;
        }

        private static StreamGeometry Polygon(Point[] points, bool closed)
        {
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(points[0], true, closed);
                for (int i = 1; i < points.Length; i++) ctx.LineTo(points[i], true, false);
            }
            g.Freeze();
            return g;
        }

        private static void Text(DrawingContext dc, string s, double size, Brush b, double x, double y, Typeface f)
        {
            dc.DrawText(Ft(s, size, b, f), new Point(x, y));
        }

        private static void TextRight(DrawingContext dc, string s, double size, Brush b, double right, double y, Typeface f)
        {
            FormattedText ft = Ft(s, size, b, f);
            dc.DrawText(ft, new Point(right - ft.Width, y));
        }

        private static void TextCenter(DrawingContext dc, string s, double size, Brush b, double cx, double y, Typeface f)
        {
            FormattedText ft = Ft(s, size, b, f);
            dc.DrawText(ft, new Point(cx - ft.Width / 2, y));
        }

        private static FormattedText Ft(string s, double size, Brush b, Typeface f)
        {
            return new FormattedText(s ?? "", CultureInfo.GetCultureInfo("zh-CN"),
                FlowDirection.LeftToRight, f, size, b, 1.0);
        }

        private static double Measure(string s, double size, Typeface f)
        {
            return Ft(s, size, BrushesBlack(), f).Width;
        }

        private static Brush BrushesBlack()
        {
            return B(Colors.Black);
        }

        private static double Legend(DrawingContext dc, double x, double y, Color c, string s, Color text)
        {
            RectFill(dc, c, x, y + 5, 12, 2);
            Text(dc, s, 8.5, B(text), x + 17, y, Mono);
            return x + 17 + s.Length * 6.2 + 26;
        }

        private static void Line(DrawingContext dc, Color c, double x1, double y1, double x2, double y2, double w)
        {
            dc.DrawLine(new Pen(B(c), w), new Point(x1, y1), new Point(x2, y2));
        }

        private static void Dash(DrawingContext dc, Color c, double x1, double y1, double x2, double y2,
            double w, double dash, double gap)
        {
            Pen p = new Pen(B(c), w);
            p.DashStyle = new DashStyle(new double[] { dash, gap }, 0);
            dc.DrawLine(p, new Point(x1, y1), new Point(x2, y2));
        }

        private static void RectFill(DrawingContext dc, Color c, double x, double y, double w, double h)
        {
            dc.DrawRectangle(B(c), null, new Rect(x, y, w, h));
        }

        private static double X(double t, double dur, double left, double width)
        {
            double nx = t / Math.Max(1, dur);
            if (nx < 0) nx = 0;
            if (nx > 1) nx = 1;
            return left + nx * width;
        }

        private static string Clock(double t)
        {
            int s = (int)Math.Round(Math.Max(0, t));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        private static Color Alpha(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        private static Color C(byte r, byte g, byte b) { return Color.FromRgb(r, g, b); }

        private static SolidColorBrush B(Color c)
        {
            SolidColorBrush b;
            if (Brushes.TryGetValue(c, out b)) return b;
            b = new SolidColorBrush(c);
            b.Freeze();
            Brushes[c] = b;
            return b;
        }
    
        // ============================================================
        //  命中测试（预览窗口点图用）
        // ============================================================
        internal sealed class OneMapHit
        {
            public int Round = -1;
            public string Kind = "";     // event / role / scp
            public int Index = -1;
        }

        internal static OneMapHit HitTest(MatchRecord m, OneMapOptions o, double x, double y)
        {
            OneMapHit hit = new OneMapHit();
            if (m == null || m.Rounds.Count == 0) return hit;
            if (o == null) o = new OneMapOptions();
            List<RoundLayout> layout = Measure(m, o);
            int ri = -1;
            for (int i = 0; i < layout.Count; i++)
            {
                if (y >= layout[i].Top - 10 && y <= layout[i].Bottom + 10) { ri = i; break; }
            }
            if (ri < 0) return hit;
            RoundLayout l = layout[ri];
            MatchRound r = m.Rounds[ri];
            double dur = Math.Max(1, r.Seconds);
            double plotL = Margin + 44;
            double plotW = o.Width - Margin - plotL;
            double tt = (x - plotL) / Math.Max(1, plotW) * dur;
            hit.Round = ri;

            if (y >= l.LineupTop && y <= l.LineupTop + 40)
            {
                double cx = Margin + 110;
                for (int i = 0; i < r.Scps.Count; i++)
                {
                    double cw = r.Scps[i].WasPlayerRole ? 74 : 58;
                    if (x >= cx && x <= cx + cw) { hit.Kind = "scp"; hit.Index = i; return hit; }
                    cx += cw + 8;
                }
                return hit;
            }

            if (y >= l.AxisY - 44 && y <= l.AxisY + 6)
            {
                int best = -1;
                double bestD = 14;
                for (int i = 0; i < r.Events.Count; i++)
                {
                    double d = Math.Abs((r.Events[i].At - r.Start).TotalSeconds - tt);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0) { hit.Kind = "event"; hit.Index = best; }
                return hit;
            }

            if (y >= l.StatusTop && y <= l.StatusTop + 58)
            {
                for (int i = 0; i < r.Roles.Count; i++)
                {
                    MatchRoleSpan s = r.Roles[i];
                    double t0 = (s.Start - r.Start).TotalSeconds;
                    double t1 = s.End == DateTime.MaxValue ? dur : (s.End - r.Start).TotalSeconds;
                    if (tt >= t0 && tt <= t1) { hit.Kind = "role"; hit.Index = i; return hit; }
                }
            }
            return hit;
        }
}
}
