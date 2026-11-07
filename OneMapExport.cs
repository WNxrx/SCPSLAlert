using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SCPSLAlert
{
    /// <summary>导出请求。普通图 / 一图流 / 两者、分割、txt 都在这里。</summary>
    internal sealed class OneMapExportRequest
    {
        public bool OneMap = true;
        public bool NormalChart;
        public bool Txt;
        public bool SplitByMatch = true;
        public int PartMaxPx = 10000;
        public string Dir = "";
        public string FileBase = "";
        public string PlayerName = "";
    }

    /// <summary>
    /// 一图流导出：渲染 → 超长切 _part01 → 可选普通性能图 → 可选 txt。
    /// 只写文件，不弹窗；窗口层负责选路径。
    /// </summary>
    internal static class OneMapExport
    {
        public static string DefaultDir(string configured)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    Directory.CreateDirectory(configured);
                    return configured;
                }
                string d = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "导出");
                Directory.CreateDirectory(d);
                return d;
            }
            catch { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string BaseName(MatchRecord m)
        {
            DateTime at = m != null ? m.Start : DateTime.Now;
            return "一图流_" + at.ToString("yyyyMMdd_HHmmss");
        }

        public static List<string> Export(MatchRecord m, OneMapOptions map, OneMapRenderer.OneMapPalette palette,
            OneMapExportRequest req, int refreshRate)
        {
            List<string> files = new List<string>();
            if (m == null || req == null) return files;

            string dir = DefaultDir(req.Dir);
            string baseName = string.IsNullOrEmpty(req.FileBase) ? BaseName(m) : req.FileBase;

            if (req.OneMap)
            {
                BitmapSource bmp = OneMapRenderer.Render(m, map, palette);
                List<string> parts = SaveSliced(bmp, dir, baseName, Math.Max(1000, req.PartMaxPx));
                for (int i = 0; i < parts.Count; i++) files.Add(parts[i]);
            }

            if (req.NormalChart)
            {
                try
                {
                    GamePerfSession session = ToPerfSession(m);
                    string path = UniquePath(Path.Combine(dir, baseName + "_性能.png"));
                    GamePerformanceReport.Save(session, path, ToPerfPalette(palette),
                        refreshRate > 0 ? refreshRate : 60);
                    files.Add(path);
                }
                catch { }
            }

            if (req.Txt)
            {
                try
                {
                    string path = UniquePath(Path.Combine(dir, baseName + "_解读.txt"));
                    File.WriteAllText(path, Text(m), new UTF8Encoding(false));
                    files.Add(path);
                }
                catch { }
            }
            return files;
        }

        /// <summary>
        /// 多局导出：按对局分割 = 每局一套；否则纵向拼成一张长图再切。
        /// </summary>
        public static List<string> ExportMany(List<MatchRecord> matches, OneMapOptions map,
            OneMapRenderer.OneMapPalette palette, OneMapExportRequest req, int refreshRate)
        {
            List<string> files = new List<string>();
            if (matches == null || matches.Count == 0 || req == null) return files;

            if (req.SplitByMatch)
            {
                for (int i = 0; i < matches.Count; i++)
                {
                    OneMapExportRequest one = new OneMapExportRequest();
                    one.OneMap = req.OneMap;
                    one.NormalChart = req.NormalChart;
                    one.Txt = req.Txt;
                    one.PartMaxPx = req.PartMaxPx;
                    one.Dir = req.Dir;
                    one.PlayerName = req.PlayerName;
                    one.FileBase = BaseName(matches[i]);
                    files.AddRange(Export(matches[i], map, palette, one, refreshRate));
                }
                return files;
            }

            if (!req.OneMap) return Export(matches[0], map, palette, req, refreshRate);

            List<BitmapSource> blocks = new List<BitmapSource>();
            for (int i = 0; i < matches.Count; i++)
                blocks.Add(OneMapRenderer.Render(matches[i], map, palette));
            BitmapSource combined = Stack(blocks, 40, palette.Bg);
            string dir = DefaultDir(req.Dir);
            string baseName = string.IsNullOrEmpty(req.FileBase)
                ? "一图流_合并_" + matches[0].Start.ToString("yyyyMMdd_HHmmss") : req.FileBase;
            files.AddRange(SaveSliced(combined, dir, baseName, Math.Max(1000, req.PartMaxPx)));
            return files;
        }

        public static List<string> SaveSliced(BitmapSource full, string dir, string baseName, int partMaxPx)
        {
            List<string> files = new List<string>();
            if (full == null) return files;
            int h = full.PixelHeight;
            int w = full.PixelWidth;
            int parts = h <= partMaxPx ? 1 : (int)Math.Ceiling(h / (double)partMaxPx);

            for (int i = 0; i < parts; i++)
            {
                int y = i * partMaxPx;
                int ph = Math.Min(partMaxPx, h - y);
                BitmapSource slice = full;
                if (parts > 1)
                    slice = new CroppedBitmap(full, new Int32Rect(0, y, w, ph));

                string name = parts == 1
                    ? baseName + ".png"
                    : baseName + "_part" + (i + 1).ToString("00") + ".png";
                string path = UniquePath(Path.Combine(dir, name));
                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(slice));
                using (FileStream fs = File.Create(path)) enc.Save(fs);
                files.Add(path);
            }
            return files;
        }

        public static BitmapSource Stack(List<BitmapSource> blocks, int gap, Color bg)
        {
            int w = 0;
            int h = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                w = Math.Max(w, blocks[i].PixelWidth);
                h += blocks[i].PixelHeight + (i > 0 ? gap : 0);
            }
            DrawingVisual v = new DrawingVisual();
            using (DrawingContext dc = v.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, w, h));
                double y = 0;
                for (int i = 0; i < blocks.Count; i++)
                {
                    dc.DrawImage(blocks[i], new Rect(0, y, blocks[i].PixelWidth, blocks[i].PixelHeight));
                    y += blocks[i].PixelHeight + (i < blocks.Count - 1 ? gap : 0);
                }
            }
            RenderTargetBitmap bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(v);
            bmp.Freeze();
            return bmp;
        }

        // ---------------- 文本解读 ----------------
        public static string Text(MatchRecord m)
        {
            StringBuilder sb = new StringBuilder(8192);
            sb.AppendLine("SCPSLAlert // A.R.C.T  —  一图流事件解读");
            sb.AppendLine("================================================");
            sb.AppendLine("对局     : " + (m.Id ?? "--"));
            sb.AppendLine("开始     : " + m.Start.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("结束     : " + (m.End == DateTime.MinValue ? "（进行中）" : m.End.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine("时长     : " + Clock(m.Seconds) + "（" + m.Seconds.ToString("0") + " 秒）");
            sb.AppendLine("回合数   : " + m.Rounds.Count);
            sb.AppendLine("采样点数 : " + m.TotalSamples);
            sb.AppendLine("版本     : " + (m.ExeVersion ?? AppInfo.Version));
            sb.AppendLine();

            for (int r = 0; r < m.Rounds.Count; r++)
            {
                MatchRound round = m.Rounds[r];
                sb.AppendLine("[回合 " + round.Index + "] " + round.Start.ToString("HH:mm:ss")
                    + " → " + round.EffectiveEnd.ToString("HH:mm:ss") + "  " + Clock(round.Seconds));
                sb.AppendLine("SCP：" + ScpLine(round));
                sb.AppendLine("角色：");
                for (int i = 0; i < round.Roles.Count; i++)
                {
                    MatchRoleSpan s = round.Roles[i];
                    sb.Append("  ").Append(s.Start.ToString("HH:mm:ss")).Append("  ")
                        .Append(string.IsNullOrEmpty(s.Role) ? "（未识别）" : s.Role)
                        .Append("  ").Append(s.State);
                    if (!string.IsNullOrEmpty(s.Note)) sb.Append("  ").Append(s.Note);
                    sb.AppendLine();
                }
                sb.AppendLine("事件：");
                for (int i = 0; i < round.Events.Count; i++)
                {
                    MatchEvent e = round.Events[i];
                    sb.Append("  ").Append(Clock((e.At - round.Start).TotalSeconds))
                        .Append("  ").Append(e.At.ToString("HH:mm:ss"))
                        .Append("  ").Append(e.Kind).Append("  ").Append(e.Label)
                        .Append("  [").Append(e.Source).Append("]");
                    if (e.HasEnd) sb.Append("  持续到 ").Append(e.EndAt.ToString("HH:mm:ss"));
                    sb.AppendLine();
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string ScpLine(MatchRound r)
        {
            if (r.Scps.Count == 0) return "（未识别）";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < r.Scps.Count; i++)
            {
                ScpPresence s = r.Scps[i];
                if (i > 0) sb.Append("、");
                sb.Append("SCP-").Append(s.Code);
                if (s.WasPlayerRole) sb.Append("（本机）");
                if (s.ContainedAt != DateTime.MinValue) sb.Append("（已收容 ").Append(s.ContainedAt.ToString("HH:mm:ss")).Append("）");
            }
            return sb.ToString();
        }

        // ---------------- 普通性能图 ----------------
        internal static GamePerfSession ToPerfSession(MatchRecord m)
        {
            GamePerfSession s = new GamePerfSession();
            s.Start = m.Start;
            s.End = m.End;
            s.Pid = m.Pid;
            s.GameTitle = m.GameTitle;
            s.EnteredServer = true;
            for (int r = 0; r < m.Rounds.Count; r++)
            {
                MatchRound round = m.Rounds[r];
                for (int i = 0; i < round.Samples.Count; i++)
                {
                    MatchSample q = round.Samples[i];
                    GamePerfSample d = new GamePerfSample();
                    d.At = q.At;
                    d.HasFps = q.HasFps;
                    d.Fps = q.Fps;
                    d.Cpu = q.Cpu;
                    d.Gpu = q.Gpu;
                    d.GpuAvailable = q.Gpu > 0;
                    d.Temp = q.Temp;
                    d.TempAvailable = q.HasTemp;
                    s.Samples.Add(d);
                }
                for (int i = 0; i < round.Events.Count; i++)
                {
                    MatchEvent e = round.Events[i];
                    GamePerfEvent g = new GamePerfEvent();
                    g.At = e.At;
                    g.EndAt = e.EndAt;
                    g.Kind = e.Kind;
                    g.Label = e.Label;
                    s.Events.Add(g);
                }
            }
            return s;
        }

        internal static PerfReportPalette ToPerfPalette(OneMapRenderer.OneMapPalette p)
        {
            PerfReportPalette o = new PerfReportPalette();
            o.Bg = B(p.Bg); o.Panel = B(p.Panel); o.PanelAlt = B(p.PanelAlt); o.Line = B(p.Line);
            o.Ink = B(p.Ink); o.Sub = B(p.Sub); o.Muted = B(p.Muted);
            o.Accent = B(p.Accent); o.Accent2 = B(p.Accent2); o.Danger = B(p.Danger);
            o.Gpu = B(p.Gpu); o.Net = B(p.Accent2); o.Disk = B(p.Warn);
            return o;
        }

        private static Brush B(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; i < 1000; i++)
            {
                string p = Path.Combine(dir, name + "-" + i + ext);
                if (!File.Exists(p)) return p;
            }
            return path;
        }

        private static string Clock(double t)
        {
            int s = (int)Math.Round(Math.Max(0, t));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }
    }
}
