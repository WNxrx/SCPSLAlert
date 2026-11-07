using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SCPSLAlert
{
    /// <summary>游戏运行期间的一秒采样。FPS 由 PresentMon 提供，其余来自 SystemTelemetry。</summary>
    internal sealed class GamePerfSample
    {
        public DateTime At;
        public bool HasFps;
        public double Fps;
        public double LowFps;
        public double Cpu;
        public double GameCpu;
        public double Gpu;
        public double GameGpu;
        public bool GpuAvailable;
        public double MemPct;
        public double GameMemMb;
        public double Disk;
        public double Net;
        public double Temp;
        public bool TempAvailable;
    }

    /// <summary>游戏里被软件识别到/记录到的事件（画在报告时间轴上）。</summary>
    internal sealed class GamePerfEvent
    {
        public DateTime At;
        public DateTime EndAt;
        public string Kind = "";       // wave / death / respawn / warhead / deadman
        public string Label = "";
        public bool HasEnd { get { return EndAt != DateTime.MinValue && EndAt > At; } }
    }

    /// <summary>一局游戏的性能记录（从检测到 SCPSL 进程开始，到进程退出为止）。</summary>
    internal sealed class GamePerfSession
    {
        public DateTime Start = DateTime.Now;
        public DateTime End;
        public int Pid;
        public string GameTitle = "";
        public bool EnteredServer;
        public readonly List<GamePerfSample> Samples = new List<GamePerfSample>(4096);
        public readonly List<GamePerfEvent> Events = new List<GamePerfEvent>(32);

        public double DurationSeconds
        {
            get
            {
                DateTime end = End == DateTime.MinValue ? DateTime.Now : End;
                double d = (end - Start).TotalSeconds;
                return d < 0 ? 0 : d;
            }
        }

        public GamePerfSession Clone()
        {
            GamePerfSession c = new GamePerfSession();
            c.Start = Start;
            c.End = End == DateTime.MinValue ? DateTime.Now : End;
            c.Pid = Pid;
            c.GameTitle = GameTitle;
            c.EnteredServer = EnteredServer;
            for (int i = 0; i < Samples.Count; i++)
            {
                GamePerfSample s = Samples[i];
                GamePerfSample d = new GamePerfSample();
                d.At = s.At; d.HasFps = s.HasFps; d.Fps = s.Fps; d.LowFps = s.LowFps;
                d.Cpu = s.Cpu; d.GameCpu = s.GameCpu; d.Gpu = s.Gpu; d.GameGpu = s.GameGpu;
                d.GpuAvailable = s.GpuAvailable; d.MemPct = s.MemPct; d.GameMemMb = s.GameMemMb;
                d.Disk = s.Disk; d.Net = s.Net; d.Temp = s.Temp; d.TempAvailable = s.TempAvailable;
                c.Samples.Add(d);
            }
            for (int i = 0; i < Events.Count; i++)
            {
                GamePerfEvent e = Events[i];
                GamePerfEvent f = new GamePerfEvent();
                f.At = e.At; f.EndAt = e.EndAt; f.Kind = e.Kind; f.Label = e.Label;
                c.Events.Add(f);
            }
            return c;
        }
    }

    internal sealed class GamePerfSummary
    {
        public int Count;
        public int FpsCount;
        public double DurationSeconds;
        public double AvgFps, MinFps, AvgLowFps, AvgFrameMs;
        public double AvgCpu, MaxCpu, AvgGameCpu, MaxGameCpu;
        public double AvgGpu, MaxGpu, AvgGameGpu, MaxGameGpu;
        public double AvgMemPct, MaxMemPct, AvgGameMem, MaxGameMem;
        public double AvgDisk, MaxDisk, AvgNet, MaxNet;
        public double AvgTemp, MaxTemp;
        public bool HasGpu, HasTemp;
    }

    /// <summary>报告配色，直接取自主界面的当前主题。</summary>
    internal sealed class PerfReportPalette
    {
        public Brush Bg;
        public Brush Panel;
        public Brush PanelAlt;
        public Brush Line;
        public Brush Ink;
        public Brush Sub;
        public Brush Muted;
        public Brush Accent;
        public Brush Accent2;
        public Brush Danger;
        public Brush Gpu;
        public Brush Net;
        public Brush Disk;
    }

    /// <summary>
    /// 把一局游戏的性能曲线渲染成 PNG。纯 WPF 绘制，和主界面同一套配色 / 字体。
    /// 包含 FPS、CPU/GPU、内存、磁盘/网络、温度，以及趋势建议。
    /// </summary>
    internal static class GamePerformanceReport
    {
        private const double W = 1600;
        private const double Margin = 60;
        private static readonly Typeface UiFace = new Typeface(new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface UiBoldFace = new Typeface(new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Typeface MonoFace = new Typeface(new FontFamily("Consolas"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface DisplayFace = new Typeface(
            new FontFamily("Bahnschrift SemiCondensed, Bahnschrift, Segoe UI"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private sealed class Series
        {
            public string Name;
            public Brush Color;
            public Func<GamePerfSample, bool> Valid;
            public Func<GamePerfSample, double> Value;
            public double Min;
            public double Max;
            public bool Secondary;
        }

        public static void Save(GamePerfSession session, string path, PerfReportPalette p, int refreshRate)
        {
            BitmapSource bmp = Render(session, p, refreshRate);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream fs = File.Create(path)) enc.Save(fs);
        }

        /// <summary>
        /// 与 PNG 同内容的文本版：KPI、事件、建议，以及每秒全部采样。
        /// 自动记录 / 手动导出都会把这份 .txt 和 .png 放在一起。
        /// </summary>
        public static string RenderText(GamePerfSession s, int refreshRate)
        {
            GamePerfSummary x = Summarize(s);
            List<string> advice = BuildAdvice(s, x);
            StringBuilder sb = new StringBuilder(32768);
            sb.AppendLine("SCPSLAlert // A.R.C.T  —  游戏性能曲线（文本版）");
            sb.AppendLine("================================================");
            sb.AppendLine("开始时间 : " + s.Start.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("结束时间 : " + (s.End == DateTime.MinValue ? "（进行中）" : s.End.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine("游戏时长 : " + Clock(x.DurationSeconds) + "（" + x.DurationSeconds.ToString("0") + " 秒）");
            sb.AppendLine("进程 PID : " + (s.Pid > 0 ? s.Pid.ToString() : "--"));
            sb.AppendLine("进入服务器: " + (s.EnteredServer ? "是" : "否"));
            sb.AppendLine("采样点数 : " + x.Count);
            sb.AppendLine("显示刷新 : " + (refreshRate > 0 ? refreshRate + " Hz" : "--"));
            sb.AppendLine();
            sb.AppendLine("[KPI]");
            sb.AppendLine("平均帧率 : " + (x.FpsCount > 0 ? x.AvgFps.ToString("0.0") + " FPS" : "--"));
            sb.AppendLine("最低帧率 : " + (x.FpsCount > 0 ? x.MinFps.ToString("0.0") + " FPS" : "--"));
            sb.AppendLine("1% low   : " + (x.FpsCount > 0 ? x.AvgLowFps.ToString("0.0") + " FPS" : "--"));
            sb.AppendLine("平均帧间隔: " + (x.FpsCount > 0 ? x.AvgFrameMs.ToString("0.00") + " ms" : "--"));
            sb.AppendLine("CPU 平均 / 峰值: " + x.AvgCpu.ToString("0.0") + "% / " + x.MaxCpu.ToString("0.0") + "%");
            sb.AppendLine("游戏 CPU 平均 / 峰值: " + x.AvgGameCpu.ToString("0.0") + "% / " + x.MaxGameCpu.ToString("0.0") + "%");
            if (x.HasGpu)
            {
                sb.AppendLine("GPU 平均 / 峰值: " + x.AvgGpu.ToString("0.0") + "% / " + x.MaxGpu.ToString("0.0") + "%");
                sb.AppendLine("游戏 GPU 平均 / 峰值: " + x.AvgGameGpu.ToString("0.0") + "% / " + x.MaxGameGpu.ToString("0.0") + "%");
            }
            sb.AppendLine("系统内存 平均 / 峰值: " + x.AvgMemPct.ToString("0.0") + "% / " + x.MaxMemPct.ToString("0.0") + "%");
            sb.AppendLine("游戏内存 平均 / 峰值: " + (x.AvgGameMem / 1024.0).ToString("0.00") + " / " + (x.MaxGameMem / 1024.0).ToString("0.00") + " GB");
            sb.AppendLine("磁盘繁忙 平均 / 峰值: " + x.AvgDisk.ToString("0.0") + "% / " + x.MaxDisk.ToString("0.0") + "%");
            sb.AppendLine("网络 平均 / 峰值: " + x.AvgNet.ToString("0.00") + " / " + x.MaxNet.ToString("0.00") + " Mbps");
            if (x.HasTemp) sb.AppendLine("温度 平均 / 峰值: " + x.AvgTemp.ToString("0.0") + " / " + x.MaxTemp.ToString("0.0") + " °C");
            sb.AppendLine();
            sb.AppendLine("[游戏事件]");
            if (s.Events.Count == 0) sb.AppendLine("本局没有记录到可标注的事件。");
            for (int i = 0; i < s.Events.Count; i++)
            {
                GamePerfEvent e = s.Events[i];
                sb.Append(e.At.ToString("HH:mm:ss")).Append("  ").Append(e.Kind).Append("  ").Append(e.Label);
                if (e.HasEnd) sb.Append("  持续到 ").Append(e.EndAt.ToString("HH:mm:ss"));
                sb.AppendLine();
            }
            sb.AppendLine();
            sb.AppendLine("[趋势与建议]");
            for (int i = 0; i < advice.Count; i++) sb.AppendLine("· " + advice[i]);
            sb.AppendLine();
            sb.AppendLine("[每秒采样]");
            sb.AppendLine("时间,帧率,1%low,CPU%,游戏CPU%,GPU%,游戏GPU%,内存%,游戏内存MB,磁盘%,网络Mbps,温度C");
            for (int i = 0; i < s.Samples.Count; i++)
            {
                GamePerfSample q = s.Samples[i];
                sb.Append(q.At.ToString("HH:mm:ss")).Append(',');
                sb.Append(q.HasFps ? q.Fps.ToString("0.0", CultureInfo.InvariantCulture) : "").Append(',');
                sb.Append(q.HasFps ? q.LowFps.ToString("0.0", CultureInfo.InvariantCulture) : "").Append(',');
                sb.Append(q.Cpu.ToString("0.0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(q.GameCpu.ToString("0.0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(q.GpuAvailable ? q.Gpu.ToString("0.0", CultureInfo.InvariantCulture) : "").Append(',');
                sb.Append(q.GpuAvailable ? q.GameGpu.ToString("0.0", CultureInfo.InvariantCulture) : "").Append(',');
                sb.Append(q.MemPct.ToString("0.0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(q.GameMemMb.ToString("0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(q.Disk.ToString("0.0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(q.Net.ToString("0.00", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(q.TempAvailable ? q.Temp.ToString("0.0", CultureInfo.InvariantCulture) : "").AppendLine();
            }
            return sb.ToString();
        }

        public static BitmapSource Render(GamePerfSession session, PerfReportPalette p, int refreshRate)
        {
            GamePerfSummary sum = Summarize(session);
            List<string> advice = BuildAdvice(session, sum);
            bool hasTemp = sum.HasTemp;
            double contentBottom = 34 + 112 + 112 + 96 + 300 + 250 + 236 + (hasTemp ? 210 : 0) + 250;
            double H = contentBottom + 70;
            double width = W;

            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(p.Bg, null, new Rect(0, 0, width, H));
                double y = 34;

                // ---------- 页头 ----------
                Text(dc, "SCPSLAlert  //  A.R.C.T", 10, p.Accent, Margin, y, MonoFace);
                Text(dc, "游戏性能曲线", 40, p.Ink, Margin, y + 16, DisplayFace);
                Text(dc, "PERFORMANCE REPORT  /  SCPSL.EXE", 9, p.Sub, width - Margin - 360, y + 12, MonoFace);

                string when = session.Start.ToString("yyyy-MM-dd  HH:mm:ss");
                string dur = Clock(sum.DurationSeconds);
                Text(dc, when + "  ·  时长 " + dur + "  ·  PID " + (session.Pid > 0 ? session.Pid.ToString() : "--")
                    + "  ·  采样 " + sum.Count + " 点", 11, p.Sub, Margin, y + 68, UiFace);
                Line(dc, p.Line, Margin, y + 96, width - Margin, y + 96, 1);
                y += 112;

                // ---------- KPI ----------
                double gap = 18;
                double kw = (width - Margin * 2 - gap * 3) / 4;
                Kpi(dc, p, Margin, y, kw, "平均帧率", sum.FpsCount > 0 ? sum.AvgFps.ToString("0") : "--", "FPS",
                    sum.FpsCount > 0 ? "最低 " + sum.MinFps.ToString("0") + " FPS" : "未采集到 FPS", p.Accent2);
                Kpi(dc, p, Margin + (kw + gap), y, kw, "1% LOW", sum.FpsCount > 0 ? sum.AvgLowFps.ToString("0") : "--", "FPS",
                    sum.FpsCount > 0 ? "卡顿参考" : "需要 PresentMon", p.Gpu);
                Kpi(dc, p, Margin + (kw + gap) * 2, y, kw, "平均帧间隔", sum.FpsCount > 0 ? sum.AvgFrameMs.ToString("0.0") : "--", "MS",
                    sum.FpsCount > 0 ? "越低越顺" : "等待游戏帧", p.Accent);
                Kpi(dc, p, Margin + (kw + gap) * 3, y, kw, "游戏时长", dur, "",
                    sum.Count > 0 ? "本局采样完整保留" : "未检测到游戏", p.Ink);
                y += 112;

                // ---------- 事件时间轴 ----------
                EventStrip(dc, p, new Rect(Margin, y, width - Margin * 2, 96), session);
                y += 96;

                // ---------- FPS ----------
                double fpsMax = 120;
                for (int i = 0; i < session.Samples.Count; i++)
                {
                    GamePerfSample s = session.Samples[i];
                    if (s.HasFps && s.Fps > fpsMax) fpsMax = s.Fps;
                }
                if (refreshRate > 0 && refreshRate * 1.05 > fpsMax) fpsMax = refreshRate * 1.05;
                fpsMax = Math.Ceiling(fpsMax * 1.12 / 10) * 10;
                if (fpsMax < 60) fpsMax = 60;
                List<Series> fps = new List<Series>();
                fps.Add(new Series { Name = "平均 FPS", Color = p.Accent2, Min = 0, Max = fpsMax,
                    Valid = delegate(GamePerfSample s) { return s.HasFps; }, Value = delegate(GamePerfSample s) { return s.Fps; } });
                fps.Add(new Series { Name = "1% low", Color = p.Gpu, Min = 0, Max = fpsMax,
                    Valid = delegate(GamePerfSample s) { return s.HasFps && s.LowFps > 0; }, Value = delegate(GamePerfSample s) { return s.LowFps; } });
                Chart(dc, p, new Rect(Margin, y, width - Margin * 2, 300), "FPS  /  帧率", "目标刷新率参考线",
                    session, fps, "FPS", true, 0, fpsMax, refreshRate);
                y += 300;

                // ---------- CPU / GPU ----------
                List<Series> cg = new List<Series>();
                cg.Add(new Series { Name = "CPU 系统", Color = p.Accent, Min = 0, Max = 100,
                    Valid = delegate(GamePerfSample s) { return true; }, Value = delegate(GamePerfSample s) { return s.Cpu; } });
                cg.Add(new Series { Name = "CPU 游戏", Color = p.Accent2, Min = 0, Max = 100,
                    Valid = delegate(GamePerfSample s) { return true; }, Value = delegate(GamePerfSample s) { return s.GameCpu; } });
                if (sum.HasGpu)
                {
                    cg.Add(new Series { Name = "GPU 系统", Color = p.Gpu, Min = 0, Max = 100,
                        Valid = delegate(GamePerfSample s) { return s.GpuAvailable; }, Value = delegate(GamePerfSample s) { return s.Gpu; } });
                    cg.Add(new Series { Name = "GPU 游戏", Color = p.Net, Min = 0, Max = 100,
                        Valid = delegate(GamePerfSample s) { return s.GpuAvailable; }, Value = delegate(GamePerfSample s) { return s.GameGpu; } });
                }
                Chart(dc, p, new Rect(Margin, y, (width - Margin * 2 - 26) / 2, 250), "CPU / GPU  /  占用率",
                    "100% 为满负载", session, cg, "%", true, 0, 100, refreshRate);
                Chart(dc, p, new Rect(Margin + (width - Margin * 2 - 26) / 2 + 26, y,
                    (width - Margin * 2 - 26) / 2, 250), "MEMORY  /  内存", "系统 % · 游戏 GB",
                    session, MemorySeries(p, sum), "%", true, 0, 100, refreshRate);
                y += 250;

                // ---------- 磁盘 / 网络 ----------
                List<Series> dn = new List<Series>();
                dn.Add(new Series { Name = "磁盘繁忙 %", Color = p.Disk, Min = 0, Max = 100,
                    Valid = delegate(GamePerfSample s) { return true; }, Value = delegate(GamePerfSample s) { return s.Disk; } });
                double netMax = 1;
                for (int i = 0; i < session.Samples.Count; i++) if (session.Samples[i].Net > netMax) netMax = session.Samples[i].Net;
                netMax = Math.Ceiling(netMax * 1.2);
                dn.Add(new Series { Name = "网络 Mbps", Color = p.Net, Min = 0, Max = netMax, Secondary = true,
                    Valid = delegate(GamePerfSample s) { return true; }, Value = delegate(GamePerfSample s) { return s.Net; } });
                Chart(dc, p, new Rect(Margin, y, width - Margin * 2, 236), "STORAGE / NETWORK  /  磁盘与网络",
                    "磁盘 % · 网络 Mbps", session, dn, "%", true, 0, 100, refreshRate);
                y += 236;

                // ---------- 温度 ----------
                if (hasTemp)
                {
                    double tmax = 80;
                    for (int i = 0; i < session.Samples.Count; i++)
                        if (session.Samples[i].TempAvailable && session.Samples[i].Temp > tmax)
                            tmax = session.Samples[i].Temp;
                    tmax = Math.Ceiling(tmax * 1.1 / 5) * 5;
                    if (tmax < 60) tmax = 60;
                    List<Series> temp = new List<Series>();
                    temp.Add(new Series { Name = "温度 °C", Color = p.Danger, Min = 20, Max = tmax,
                        Valid = delegate(GamePerfSample s) { return s.TempAvailable; }, Value = delegate(GamePerfSample s) { return s.Temp; } });
                    Chart(dc, p, new Rect(Margin, y, width - Margin * 2, 210), "TEMPERATURE  /  温度",
                        "有传感器时记录", session, temp, "°C", true, 20, tmax, refreshRate);
                    y += 210;
                }

                // ---------- 建议 ----------
                Advice(dc, p, new Rect(Margin, y, width - Margin * 2, 250), advice);
                y += 250;

                Text(dc, "SCPSLAlert // A.R.C.T  ·  游戏性能曲线  ·  生成于 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    8.5, p.Muted, Margin, H - 30, MonoFace);
                Text(dc, "数据来源：PresentMon + 本工具系统采样", 8.5, p.Muted,
                    width - Margin - 300, H - 30, MonoFace);
            }

            RenderTargetBitmap bmp = new RenderTargetBitmap((int)width, (int)H, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        private static List<Series> MemorySeries(PerfReportPalette p, GamePerfSummary sum)
        {
            List<Series> list = new List<Series>();
            list.Add(new Series { Name = "系统内存 %", Color = p.Accent, Min = 0, Max = 100,
                Valid = delegate(GamePerfSample s) { return true; }, Value = delegate(GamePerfSample s) { return s.MemPct; } });
            double maxGb = 4;
            if (sum.MaxGameMem > 0) maxGb = Math.Max(4, Math.Ceiling(sum.MaxGameMem / 1024.0 * 1.25));
            // 游戏内存用右侧轴；图例里会写实际范围。
            list.Add(new Series { Name = "游戏内存 GB", Color = p.Accent2, Min = 0,
                Max = maxGb, Secondary = true,
                Valid = delegate(GamePerfSample s) { return true; }, Value = delegate(GamePerfSample s) { return s.GameMemMb / 1024.0; } });
            return list;
        }

        private static GamePerfSummary Summarize(GamePerfSession s)
        {
            GamePerfSummary x = new GamePerfSummary();
            x.Count = s.Samples.Count;
            x.DurationSeconds = s.DurationSeconds;
            double fps = 0, low = 0, cpu = 0, gcpu = 0, gpu = 0, ggpu = 0, mem = 0, gmem = 0, disk = 0, net = 0, temp = 0;
            int nFps = 0, nGpu = 0, nTemp = 0;
            x.MinFps = double.MaxValue;
            for (int i = 0; i < s.Samples.Count; i++)
            {
                GamePerfSample q = s.Samples[i];
                if (q.HasFps && q.Fps > 0)
                {
                    fps += q.Fps; low += q.LowFps; nFps++;
                    if (q.Fps < x.MinFps) x.MinFps = q.Fps;
                }
                cpu += q.Cpu; gcpu += q.GameCpu; mem += q.MemPct; gmem += q.GameMemMb; disk += q.Disk; net += q.Net;
                if (q.Cpu > x.MaxCpu) x.MaxCpu = q.Cpu;
                if (q.GameCpu > x.MaxGameCpu) x.MaxGameCpu = q.GameCpu;
                if (q.MemPct > x.MaxMemPct) x.MaxMemPct = q.MemPct;
                if (q.GameMemMb > x.MaxGameMem) x.MaxGameMem = q.GameMemMb;
                if (q.Disk > x.MaxDisk) x.MaxDisk = q.Disk;
                if (q.Net > x.MaxNet) x.MaxNet = q.Net;
                if (q.GpuAvailable)
                {
                    gpu += q.Gpu; ggpu += q.GameGpu; nGpu++;
                    if (q.Gpu > x.MaxGpu) x.MaxGpu = q.Gpu;
                    if (q.GameGpu > x.MaxGameGpu) x.MaxGameGpu = q.GameGpu;
                }
                if (q.TempAvailable)
                {
                    temp += q.Temp; nTemp++;
                    if (q.Temp > x.MaxTemp) x.MaxTemp = q.Temp;
                }
            }
            if (x.Count > 0)
            {
                x.AvgCpu = cpu / x.Count; x.AvgGameCpu = gcpu / x.Count;
                x.AvgMemPct = mem / x.Count; x.AvgGameMem = gmem / x.Count;
                x.AvgDisk = disk / x.Count; x.AvgNet = net / x.Count;
            }
            if (nFps > 0)
            {
                x.FpsCount = nFps;
                x.AvgFps = fps / nFps;
                x.AvgLowFps = low / nFps;
                x.AvgFrameMs = x.AvgFps > 0 ? 1000.0 / x.AvgFps : 0;
                if (x.MinFps == double.MaxValue) x.MinFps = 0;
            }
            if (nGpu > 0) { x.HasGpu = true; x.AvgGpu = gpu / nGpu; x.AvgGameGpu = ggpu / nGpu; }
            if (nTemp > 0) { x.HasTemp = true; x.AvgTemp = temp / nTemp; }
            return x;
        }

        private static List<string> BuildAdvice(GamePerfSession s, GamePerfSummary x)
        {
            List<string> tips = new List<string>();
            if (x.FpsCount == 0)
            {
                tips.Add("没有采集到游戏帧率：确认 PresentMon.exe 与主程序同目录，并以管理员身份运行。");
            }
            else
            {
                double? fpsTrend = Trend(s, delegate(GamePerfSample q) { return q.HasFps; }, delegate(GamePerfSample q) { return q.Fps; });
                if (fpsTrend.HasValue && fpsTrend.Value <= -12)
                    tips.Add("帧率后段下降 " + Math.Abs(fpsTrend.Value).ToString("0") + "%：可能是温度上升或后台占用增加，先看温度 / CPU 曲线。");
                if (x.AvgFps < 45)
                    tips.Add("平均 " + x.AvgFps.ToString("0") + " FPS 偏低：优先降低分辨率 / 画质，并关闭不必要的后台程序。");
                else if (x.AvgFps < 60)
                    tips.Add("平均 " + x.AvgFps.ToString("0") + " FPS 还没到 60：降低阴影 / 后处理，或把帧率上限设在能稳定的档位。");

                if (x.AvgLowFps > 0 && x.AvgLowFps < x.AvgFps * 0.55)
                    tips.Add("1% low 只有平均帧率的 " + (x.AvgLowFps / x.AvgFps * 100).ToString("0")
                        + "%：偶发卡顿明显，尝试限制最高帧率、关闭录屏 / 浏览器硬件加速。");
            }

            if (x.HasGpu && x.AvgGpu >= 90)
                tips.Add("GPU 平均 " + x.AvgGpu.ToString("0") + "% 接近满载：降低分辨率、阴影和抗锯齿，先给 GPU 留余量。");
            if (x.AvgCpu >= 85 || x.MaxCpu >= 96)
                tips.Add("CPU 平均 " + x.AvgCpu.ToString("0") + "%、峰值 " + x.MaxCpu.ToString("0")
                    + "%：CPU 压力偏高，关闭后台下载 / 录屏，或限制游戏帧率。");
            if (x.MaxMemPct >= 90)
                tips.Add("系统内存峰值 " + x.MaxMemPct.ToString("0") + "%：内存接近吃满，建议关掉浏览器 / 其他游戏。");
            if (x.MaxDisk >= 75)
                tips.Add("磁盘繁忙峰值 " + x.MaxDisk.ToString("0") + "%：加载 / 读图时容易卡顿，尽量把游戏放在 SSD 上。");
            if (x.HasTemp && x.MaxTemp >= 85)
                tips.Add("温度峰值 " + x.MaxTemp.ToString("0") + "°C：注意散热，温度过高会触发降频，表现为后段掉帧。");

            double? cpuTrend = Trend(s, delegate(GamePerfSample q) { return true; }, delegate(GamePerfSample q) { return q.Cpu; });
            if (cpuTrend.HasValue)
            {
                double pts = CpuPointDelta(s);
                if (pts >= 10)
                    tips.Add("CPU 后段比前段高 " + pts.ToString("0") + " 个百分点：检查后台进程是否在增长。");
            }
            double? memTrend = Trend(s, delegate(GamePerfSample q) { return true; }, delegate(GamePerfSample q) { return q.GameMemMb; });
            if (memTrend.HasValue && memTrend.Value >= 20)
                tips.Add("游戏内存后段增长 " + memTrend.Value.ToString("0") + "%：长时间游玩可考虑重开一局释放内存。");

            if (tips.Count == 0)
                tips.Add("整体稳定：当前画质与帧率设置可以保持；如果仍觉得卡，优先看 1% low 和温度曲线。");

            // 控制在一页内；严重问题优先保留前面的条目。
            while (tips.Count > 6) tips.RemoveAt(tips.Count - 1);
            return tips;
        }

        private static double? Trend(GamePerfSession s, Func<GamePerfSample, bool> ok, Func<GamePerfSample, double> value)
        {
            List<double> vals = new List<double>();
            for (int i = 0; i < s.Samples.Count; i++)
                if (ok(s.Samples[i])) vals.Add(value(s.Samples[i]));
            if (vals.Count < 12) return null;
            int k = Math.Max(1, vals.Count / 4);
            double a = 0, b = 0;
            for (int i = 0; i < k; i++) a += vals[i];
            for (int i = vals.Count - k; i < vals.Count; i++) b += vals[i];
            a /= k; b /= k;
            if (a <= 0) return null;
            return (b - a) / a * 100.0;
        }

        private static double CpuPointDelta(GamePerfSession s)
        {
            if (s.Samples.Count < 12) return 0;
            int k = Math.Max(1, s.Samples.Count / 4);
            double a = 0, b = 0;
            for (int i = 0; i < k; i++) a += s.Samples[i].Cpu;
            for (int i = s.Samples.Count - k; i < s.Samples.Count; i++) b += s.Samples[i].Cpu;
            return b / k - a / k;
        }

        // ---------------- 事件时间轴 ----------------

        private static void EventStrip(DrawingContext dc, PerfReportPalette p, Rect r, GamePerfSession s)
        {
            dc.DrawRoundedRectangle(p.Panel, new Pen(p.Line, 1), r, 6, 6);
            Text(dc, "GAME EVENTS  /  游戏事件", 10, p.Accent, r.X + 16, r.Y + 10, MonoFace);
            Text(dc, "竖线 = 事件发生；色带 = 核弹 / 终焉持续段", 8, p.Muted, r.X + 180, r.Y + 11, UiFace);
            Rect plot = new Rect(r.X + 54, r.Y + 34, r.Width - 78, r.Height - 54);
            double duration = Math.Max(1, s.DurationSeconds);
            dc.DrawLine(new Pen(p.Line, 0.7), new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
            for (int i = 0; i <= 4; i++)
            {
                double xx = plot.Left + plot.Width * i / 4.0;
                dc.DrawLine(new Pen(p.Line, 0.5), new Point(xx, plot.Bottom), new Point(xx, plot.Bottom + 4));
                Text(dc, Clock(duration * i / 4.0), 7, p.Muted, xx - 13, plot.Bottom + 6, MonoFace);
            }

            // 先画持续事件色带（核弹 / 终焉）
            for (int i = 0; i < s.Events.Count; i++)
            {
                GamePerfEvent e = s.Events[i];
                if (!e.HasEnd) continue;
                double x1 = XOf(e.At, s, plot, duration);
                double x2 = XOf(e.EndAt, s, plot, duration);
                if (x2 < x1) { double t = x1; x1 = x2; x2 = t; }
                Brush b = e.Kind == "warhead" ? p.Gpu : p.Danger;
                dc.DrawRectangle(WithAlpha(b, 32), null, new Rect(x1, plot.Top, Math.Max(2, x2 - x1), plot.Height));
                Text(dc, e.Label, 7.5, b, Math.Min(x1 + 3, plot.Right - 70), plot.Top + 2, UiFace);
            }

            // 再画事件竖线 + 标签（上下交错，避免挤在一起）
            int row = 0;
            for (int i = 0; i < s.Events.Count; i++)
            {
                GamePerfEvent e = s.Events[i];
                double x = XOf(e.At, s, plot, duration);
                Brush b = KindColor(e.Kind, p);
                dc.DrawLine(new Pen(b, 1.4), new Point(x, plot.Top + 10), new Point(x, plot.Bottom));
                if (!e.HasEnd)
                {
                    double labelY = plot.Top + 10 + (row % 2) * 15;
                    Text(dc, e.Label, 7, b, x + 3, labelY, UiFace);
                }
                row++;
            }
            if (s.Events.Count == 0)
                Text(dc, "本局没有记录到可标注的事件。", 8.5, p.Muted, plot.Left, plot.Top + 18, UiFace);
        }

        private static void ChartEvents(DrawingContext dc, PerfReportPalette p, Rect plot,
            GamePerfSession s, double duration)
        {
            for (int i = 0; i < s.Events.Count; i++)
            {
                GamePerfEvent e = s.Events[i];
                Brush b = KindColor(e.Kind, p);
                if (e.HasEnd)
                {
                    double x1 = XOf(e.At, s, plot, duration);
                    double x2 = XOf(e.EndAt, s, plot, duration);
                    if (x2 < x1) { double t = x1; x1 = x2; x2 = t; }
                    dc.DrawRectangle(WithAlpha(b, 14), null, new Rect(x1, plot.Top, Math.Max(1, x2 - x1), plot.Height));
                }
                double x = XOf(e.At, s, plot, duration);
                dc.DrawLine(new Pen(WithAlpha(b, 70), 0.8), new Point(x, plot.Top), new Point(x, plot.Bottom));
            }
        }

        private static double XOf(DateTime at, GamePerfSession s, Rect plot, double duration)
        {
            double nx = (at - s.Start).TotalSeconds / duration;
            if (nx < 0) nx = 0;
            if (nx > 1) nx = 1;
            return plot.Left + nx * plot.Width;
        }

        private static Brush KindColor(string kind, PerfReportPalette p)
        {
            if (kind == "wave") return p.Net;
            if (kind == "death") return p.Danger;
            if (kind == "respawn") return p.Accent;
            if (kind == "warhead") return p.Gpu;
            if (kind == "deadman") return p.Danger;
            return p.Sub;
        }

        // ---------------- 绘制小工具 ----------------

        private static void Kpi(DrawingContext dc, PerfReportPalette p, double x, double y, double w,
            string label, string value, string unit, string foot, Brush color)
        {
            Rect r = new Rect(x, y, w, 94);
            dc.DrawRoundedRectangle(p.Panel, new Pen(p.Line, 1), r, 6, 6);
            dc.DrawRectangle(color, null, new Rect(x, y, 3, 94));
            Text(dc, label, 9, p.Muted, x + 16, y + 12, MonoFace);
            Text(dc, value, 28, color, x + 16, y + 26, DisplayFace);
            if (!string.IsNullOrEmpty(unit))
                Text(dc, unit, 10, p.Muted, x + 16 + Measure(value, 28, DisplayFace).Width + 8, y + 48, MonoFace);
            Text(dc, foot, 8.5, p.Sub, x + 16, y + 70, UiFace);
        }

        private static void Chart(DrawingContext dc, PerfReportPalette p, Rect r, string title, string sub,
            GamePerfSession session, List<Series> series, string unit, bool axis,
            double yMin, double yMax, double targetFps)
        {
            dc.DrawRoundedRectangle(p.Panel, new Pen(p.Line, 1), r, 6, 6);
            Text(dc, title, 10, p.Accent, r.X + 16, r.Y + 12, MonoFace);
            Text(dc, sub, 8.5, p.Muted, r.X + 16, r.Y + 28, UiFace);

            double legendX = r.X + 16;
            for (int i = 0; i < series.Count; i++)
            {
                Series s = series[i];
                double ly = r.Y + 48;
                dc.DrawRectangle(s.Color, null, new Rect(legendX, ly + 4, 12, 2));
                string label = s.Name;
                if (s.Secondary) label += "  " + s.Min.ToString("0") + "–" + s.Max.ToString("0");
                Text(dc, label, 8, p.Sub, legendX + 17, ly, UiFace);
                legendX += Measure(label, 8, UiFace).Width + 52;
                if (legendX > r.Right - 220 && i < series.Count - 1)
                {
                    legendX = r.X + 16;
                }
            }

            Rect plot = new Rect(r.X + 54, r.Y + 76, r.Width - 88, r.Height - 104);
            double duration = Math.Max(1, session.DurationSeconds);
            ChartEvents(dc, p, plot, session, duration);
            // 网格 + 左轴
            for (int i = 0; i <= 4; i++)
            {
                double yy = plot.Top + plot.Height * i / 4.0;
                dc.DrawLine(new Pen(p.Line, 0.7), new Point(plot.Left, yy), new Point(plot.Right, yy));
                double v = yMax - (yMax - yMin) * i / 4.0;
                Text(dc, v.ToString("0"), 7.5, p.Muted, r.X + 10, yy - 7, MonoFace);
            }
            Text(dc, unit, 7.5, p.Muted, r.X + 10, plot.Top - 12, MonoFace);

            // 时间轴
            for (int i = 0; i <= 4; i++)
            {
                double xx = plot.Left + plot.Width * i / 4.0;
                double t = duration * i / 4.0;
                Text(dc, Clock(t), 7.5, p.Muted, xx - 14, plot.Bottom + 8, MonoFace);
            }

            // 目标线
            if (axis && title.StartsWith("FPS", StringComparison.Ordinal) && targetFps > 0 && yMax > yMin)
            {
                double targetY = plot.Bottom - (targetFps - yMin) / (yMax - yMin) * plot.Height;
                if (targetY >= plot.Top && targetY <= plot.Bottom)
                {
                    Pen dash = new Pen(WithAlpha(p.Muted, 120), 1);
                    dash.DashStyle = new DashStyle(new double[] { 4, 4 }, 0);
                    dc.DrawLine(dash, new Point(plot.Left, targetY), new Point(plot.Right, targetY));
                }
            }

            for (int si = 0; si < series.Count; si++)
            {
                Series s = series[si];
                Pen pen = new Pen(s.Color, si == 0 ? 1.8 : 1.3);
                StreamGeometry geo = new StreamGeometry();
                bool open = false;
                using (StreamGeometryContext ctx = geo.Open())
                {
                    for (int i = 0; i < session.Samples.Count; i++)
                    {
                        GamePerfSample q = session.Samples[i];
                        if (!s.Valid(q)) { open = false; continue; }
                        double v = s.Value(q);
                        if (double.IsNaN(v) || double.IsInfinity(v)) { open = false; continue; }
                        double nx = (q.At - session.Start).TotalSeconds / duration;
                        if (nx < 0) nx = 0;
                        if (nx > 1) nx = 1;
                        double ny = (v - s.Min) / Math.Max(0.0001, s.Max - s.Min);
                        if (ny < 0) ny = 0;
                        if (ny > 1) ny = 1;
                        Point pt = new Point(plot.Left + nx * plot.Width, plot.Bottom - ny * plot.Height);
                        if (!open) { ctx.BeginFigure(pt, false, false); open = true; }
                        else ctx.LineTo(pt, true, false);
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }
        }

        private static void Advice(DrawingContext dc, PerfReportPalette p, Rect r, List<string> lines)
        {
            dc.DrawRoundedRectangle(p.Panel, new Pen(p.Line, 1), r, 6, 6);
            Text(dc, "TREND & ADVICE  /  趋势与建议", 10, p.Accent, r.X + 16, r.Y + 12, MonoFace);
            double y = r.Y + 42;
            for (int i = 0; i < lines.Count; i++)
            {
                dc.DrawEllipse(p.Accent, null, new Point(r.X + 22, y + 7), 2.5, 2.5);
                FormattedText ft = new FormattedText(lines[i], CultureInfo.GetCultureInfo("zh-CN"),
                    FlowDirection.LeftToRight, UiFace, 11, p.Sub, 1.0);
                ft.MaxTextWidth = r.Width - 52;
                dc.DrawText(ft, new Point(r.X + 34, y));
                y += Math.Max(24, ft.Height + 8);
            }
            Text(dc, "建议按优先级排列；只根据本局实际采样给出，不猜测未记录的数据。", 8, p.Muted,
                r.X + 16, r.Bottom - 22, UiFace);
        }

        private static void Text(DrawingContext dc, string text, double size, Brush brush,
            double x, double y, Typeface face)
        {
            FormattedText ft = new FormattedText(text ?? "", CultureInfo.GetCultureInfo("zh-CN"),
                FlowDirection.LeftToRight, face, size, brush, 1.0);
            dc.DrawText(ft, new Point(x, y));
        }

        private static Size Measure(string text, double size, Typeface face)
        {
            FormattedText ft = new FormattedText(text ?? "", CultureInfo.GetCultureInfo("zh-CN"),
                FlowDirection.LeftToRight, face, size, Brushes.White, 1.0);
            return new Size(ft.Width, ft.Height);
        }

        private static void Line(DrawingContext dc, Brush b, double x1, double y1, double x2, double y2, double w)
        {
            dc.DrawLine(new Pen(b, w), new Point(x1, y1), new Point(x2, y2));
        }

        private static Brush WithAlpha(Brush b, byte a)
        {
            SolidColorBrush s = b as SolidColorBrush;
            if (s == null) return b;
            Color c = s.Color;
            return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        }

        private static string Clock(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int s = (int)Math.Round(seconds);
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }
    }
}
