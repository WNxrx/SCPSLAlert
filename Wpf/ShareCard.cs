using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SCPSLAlert
{
    /// <summary>名片可用的软件数据（不出现拿不到的 KD 之类）。</summary>
    internal static class ShareCardData
    {
        public static string Session
        {
            get
            {
                try
                {
                    TimeSpan t = DateTime.Now - Process.GetCurrentProcess().StartTime;
                    return ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
                }
                catch { return "--:--:--"; }
            }
        }

        private static string _matches;
        private static DateTime _matchesAt;

        public static string Matches
        {
            get
            {
                if (_matches == null || (DateTime.Now - _matchesAt).TotalSeconds > 30)
                {
                    try { _matches = Math.Max(0, MatchStore.ListRecent(500).Count) + " 局"; }
                    catch { _matches = "-- 局"; }
                    _matchesAt = DateTime.Now;
                }
                return _matches;
            }
        }
    }

    /// <summary>
    /// 可自定义的分享名片：6 种预设 + 标题 / 副标题 / 状态 / 玩家 / 三组数据 /
    /// 主色 / 字号 / 网格 / 六边形 / 边框 / 实时数据开关。
    /// 画布按 1306×196 的设计基准等比绘制，预览和导出不会变形。
    /// </summary>
    internal sealed class ShareCardCanvas : FrameworkElement
    {
        public static readonly string[] PresetNames =
        {
            "终端主卡", "收容失效", "战术数据", "基金会档案", "干员证", "信号脉冲"
        };

        private const double BaseW = 1306;
        private const double BaseH = 196;

        private sealed class Model
        {
            public string Title, Subtitle, Tag, Player, PlayerId, Line1, Line2;
            public string[] StatLabel = new string[3];
            public string[] StatValue = new string[3];
            public Color Accent, Panel, Ink, Sub, Line, Danger, Warn, Success;
            public double TextScale;
            public bool ShowGrid, ShowHex, ShowFrame, ShowStats, ShowPlayer, ShowLive;
        }

        private readonly Brush _accent, _line, _sub, _panel, _ink;
        private readonly Config _cfg;

        public ShareCardCanvas(Brush accent, Brush line, Brush sub, Brush panel, Brush ink, Config cfg)
        {
            _accent = accent;
            _line = line;
            _sub = sub;
            _panel = panel;
            _ink = ink;
            _cfg = cfg;
            IsHitTestVisible = false;
        }

        private static Color ToWpf(System.Drawing.Color c)
        {
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        private Color AccentColor
        {
            get
            {
                Color fallback = _accent is SolidColorBrush ? ((SolidColorBrush)_accent).Color : Color.FromRgb(56, 214, 255);
                if (_cfg == null || string.IsNullOrEmpty(_cfg.CardAccentHex)) return fallback;
                return ToWpf(ThemeTokens.ParseHex(_cfg.CardAccentHex, System.Drawing.Color.FromArgb(
                    fallback.A, fallback.R, fallback.G, fallback.B)));
            }
        }

        private Model BuildModel()
        {
            Model m = new Model();
            m.Title = string.IsNullOrEmpty(_cfg.CardTitle) ? "A.R.C.T" : _cfg.CardTitle;
            m.Subtitle = string.IsNullOrEmpty(_cfg.CardSubtitle) ? "SCPSL ALERT" : _cfg.CardSubtitle;
            m.Tag = _cfg.CardTag ?? "";
            m.Player = _cfg.CardPlayer ?? "";
            m.PlayerId = _cfg.CardPlayerId ?? "";
            m.Line1 = _cfg.CardLine1 ?? "";
            m.Line2 = _cfg.CardLine2 ?? "";
            m.StatLabel[0] = _cfg.CardStat1Label ?? "";
            m.StatValue[0] = _cfg.CardStat1Value ?? "";
            m.StatLabel[1] = _cfg.CardStat2Label ?? "";
            m.StatValue[1] = _cfg.CardStat2Value ?? "";
            m.StatLabel[2] = _cfg.CardStat3Label ?? "";
            m.StatValue[2] = _cfg.CardStat3Value ?? "";

            m.Accent = AccentColor;
            m.Panel = _panel is SolidColorBrush ? ((SolidColorBrush)_panel).Color : Color.FromRgb(10, 18, 26);
            m.Ink = _ink is SolidColorBrush ? ((SolidColorBrush)_ink).Color : Color.FromRgb(235, 245, 250);
            m.Sub = _sub is SolidColorBrush ? ((SolidColorBrush)_sub).Color : Color.FromRgb(150, 175, 190);
            m.Line = _line is SolidColorBrush ? ((SolidColorBrush)_line).Color : Color.FromRgb(40, 60, 80);
            m.Danger = Color.FromRgb(255, 77, 77);
            m.Warn = Color.FromRgb(242, 178, 58);
            m.Success = Color.FromRgb(61, 224, 138);
            m.TextScale = Math.Max(0.75, Math.Min(1.45, (_cfg.CardTextScale <= 0 ? 100 : _cfg.CardTextScale) / 100.0));
            m.ShowGrid = _cfg.CardShowGrid;
            m.ShowHex = _cfg.CardShowHex;
            m.ShowFrame = _cfg.CardShowFrame;
            m.ShowStats = _cfg.CardShowStats;
            m.ShowPlayer = _cfg.CardShowPlayer;
            m.ShowLive = _cfg.CardShowLive;
            return m;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 4 || h <= 4) return;

            Model m = BuildModel();
            int preset = Math.Max(0, Math.Min(PresetNames.Length - 1, _cfg.CardPreset));
            // 统一在 1306×196 设计坐标里画，再整体缩放到实际大小；
            // 这样预览、底部装饰条和导出的 PNG 永远不会变形。
            double s = 1;
            dc.PushTransform(new ScaleTransform(w / BaseW, h / BaseH));

            switch (preset)
            {
                case 1: DrawBreach(dc, BaseW, BaseH, s, m); break;
                case 2: DrawTactical(dc, BaseW, BaseH, s, m); break;
                case 3: DrawArchive(dc, BaseW, BaseH, s, m); break;
                case 4: DrawIdCard(dc, BaseW, BaseH, s, m); break;
                case 5: DrawPulse(dc, BaseW, BaseH, s, m); break;
                default: DrawTerminal(dc, BaseW, BaseH, s, m); break;
            }

            DrawGlassOverlay(dc, BaseW, BaseH, s, m);
            DrawTexts(dc, BaseW, BaseH, s, m, preset);
            dc.Pop();
        }

        private void DrawTexts(DrawingContext dc, double w, double h, double s, Model m, int preset)
        {
            double pad = 30 * s;
            double titleSize = (preset == 1 ? 38 : (preset == 3 ? 32 : 36)) * s * m.TextScale;
            Brush ink = preset == 1 ? Solid(m.Danger) : (preset == 3 ? Solid(m.Warn) : Solid(m.Ink));
            Brush accent = Solid(m.Accent);

            if (preset == 1)
            {
                // 收容失效：左边让出危险条纹，标题整体右移。
                pad = 92 * s;
                Draw(dc, m.Title, new Point(pad, 42 * s), titleSize, ink, true, w - pad - 320 * s);
                Draw(dc, m.Subtitle, new Point(pad, 92 * s), 11 * s * m.TextScale, Solid(Color.FromArgb(235, 255, 150, 150)), false, w * 0.42);
                if (m.Tag.Length > 0)
                    Tag(dc, m.Tag, new Point(pad, 120 * s), 10 * s * m.TextScale, m.Danger, w);
            }
            else if (preset == 2)
            {
                // 战术数据：左侧信息块。
                Draw(dc, "SCPSL ALERT / A.R.C.T", new Point(pad, 30 * s), 9.5 * s * m.TextScale, Solid(m.Sub), false, w * 0.3);
                Draw(dc, m.Title, new Point(pad, 52 * s), titleSize, ink, true, w * 0.32);
                Draw(dc, m.Subtitle, new Point(pad, 96 * s), 10 * s * m.TextScale, Solid(m.Sub), false, w * 0.32);
                if (m.Tag.Length > 0)
                    Draw(dc, m.Tag, new Point(pad, 118 * s), 9 * s * m.TextScale, accent, false, w * 0.32);
            }
            else if (preset == 3)
            {
                // 基金会档案：左上角小标题，主要信息走档案行。
                Draw(dc, "FOUNDATION ARCHIVE / 机密", new Point(96 * s, 26 * s), 9 * s * m.TextScale, Solid(Color.FromArgb(200, m.Warn.R, m.Warn.G, m.Warn.B)), false, w * 0.42);
                Draw(dc, m.Title, new Point(96 * s, 42 * s), titleSize, ink, true, w * 0.42);
                Draw(dc, m.Subtitle, new Point(96 * s, 82 * s), 9.5 * s * m.TextScale, Solid(Color.FromArgb(200, m.Warn.R, m.Warn.G, m.Warn.B)), false, w * 0.42);
                DrawArchiveRows(dc, w, h, s, m);
            }
            else if (preset == 4)
            {
                // 干员证：左侧照片占位，右侧身份信息。
                Draw(dc, "OPERATOR / 观察员", new Point(210 * s, 38 * s), 9.5 * s * m.TextScale, Solid(m.Sub), false, w * 0.38);
                Draw(dc, m.Title, new Point(210 * s, 56 * s), titleSize, ink, true, w * 0.42);
                Draw(dc, m.Subtitle, new Point(210 * s, 94 * s), 10 * s * m.TextScale, Solid(m.Sub), false, w * 0.42);
                if (m.Tag.Length > 0)
                    Tag(dc, m.Tag, new Point(210 * s, 118 * s), 10 * s * m.TextScale, m.Accent, w);
                DrawIdRight(dc, w, h, s, m);
            }
            else if (preset == 5)
            {
                // 信号脉冲：主标题在左，右侧是实时状态。
                Draw(dc, "SIGNAL LOCKED / 信号锁定", new Point(pad, 34 * s), 9 * s * m.TextScale, accent, false, w * 0.42);
                Draw(dc, m.Title, new Point(pad, 50 * s), titleSize, ink, true, w * 0.42);
                Draw(dc, m.Subtitle, new Point(pad, 92 * s), 10 * s * m.TextScale, Solid(m.Sub), false, w * 0.42);
                DrawPulseRight(dc, w, h, s, m);
            }
            else
            {
                // 终端主卡：标题 / 玩家 / 三组数据 / 六边形。
                Draw(dc, m.Title, new Point(pad, 46 * s), titleSize, ink, true, w * 0.32);
                Draw(dc, m.Subtitle, new Point(pad, 94 * s), 10.5 * s * m.TextScale, Solid(m.Sub), false, w * 0.32);
                if (m.Tag.Length > 0)
                    Tag(dc, m.Tag, new Point(pad, 116 * s), 10 * s * m.TextScale, m.Accent, w);
                DrawTerminalRight(dc, w, h, s, m);
            }

            double lineX = pad;
            double lineY1 = h - 46 * s;
            double lineY2 = h - 26 * s;
            double lineW = w - pad * 2;
            if (preset == 3)
            {
                // 基金会档案的中部是档案行，底部小字挪到右侧空白处。
                lineX = w * 0.58;
                lineY1 = h - 34 * s;
                lineY2 = h - 18 * s;
                lineW = w * 0.36;
            }
            else if (preset == 4)
            {
                // 干员证左下角被照片占住，小字从照片右侧开始。
                lineX = 210 * s;
                lineW = w - lineX - 30 * s;
            }
            if (!string.IsNullOrEmpty(m.Line1))
                Draw(dc, m.Line1, new Point(lineX, lineY1), 10 * s * m.TextScale,
                    preset == 1 ? Solid(m.Danger) : accent, false, lineW);
            if (!string.IsNullOrEmpty(m.Line2))
                Draw(dc, m.Line2, new Point(lineX, lineY2), 9 * s * m.TextScale, Solid(m.Sub), false, lineW);
        }

        private void DrawTerminalRight(DrawingContext dc, double w, double h, double s, Model m)
        {
            double divX = w * 0.36;
            if (m.ShowFrame)
                dc.DrawLine(new Pen(Solid(Color.FromArgb(150, m.Accent.R, m.Accent.G, m.Accent.B)), 1.4 * s),
                    new Point(divX, 24 * s), new Point(divX, h - 24 * s));

            if (m.ShowPlayer && (m.Player.Length > 0 || m.PlayerId.Length > 0))
            {
                Draw(dc, "玩家 / OPERATOR", new Point(divX + 24 * s, 42 * s), 8 * s * m.TextScale, Solid(m.Sub), false, 230 * s);
                Draw(dc, m.Player, new Point(divX + 24 * s, 56 * s), 23 * s * m.TextScale, Solid(m.Ink), true, 240 * s);
                Draw(dc, m.PlayerId, new Point(divX + 24 * s, 88 * s), 8 * s * m.TextScale, Solid(m.Sub), false, 240 * s);
            }

            if (m.ShowStats)
            {
                double x = w * 0.635;
                for (int i = 0; i < 3; i++)
                    Stat(dc, m.StatValue[i], m.StatLabel[i], x + i * w * 0.08, 50 * s, 18 * s * m.TextScale, 8 * s * m.TextScale, m.Ink, m.Sub, 150 * s);
            }
            if (m.ShowHex)
                DrawHex(dc, new Point(w - 36 * s, h * 0.5), 26 * s, new Pen(Solid(m.Accent), 1.1 * s), false);
        }

        private void DrawArchiveRows(DrawingContext dc, double w, double h, double s, Model m)
        {
            double x = 96 * s, y = 104 * s, rowH = 20 * s;
            string[] labels = { "代号", "权限", "档案", "状态" };
            string[] values =
            {
                m.Player,
                m.StatValue[0],
                m.PlayerId,
                m.Tag
            };
            for (int i = 0; i < labels.Length; i++)
            {
                Draw(dc, labels[i], new Point(x, y), 8.5 * s * m.TextScale, Solid(Color.FromArgb(180, m.Warn.R, m.Warn.G, m.Warn.B)), false, 56 * s);
                Draw(dc, values[i] ?? "", new Point(x + 58 * s, y), 9.5 * s * m.TextScale, Solid(Color.FromRgb(205, 187, 138)), false, 300 * s);
                y += rowH;
            }

            if (m.ShowStats)
            {
                double sx = w * 0.58;
                Draw(dc, m.StatValue[1], new Point(sx, 42 * s), 20 * s * m.TextScale, Solid(m.Warn), true, 220 * s);
                Draw(dc, m.StatLabel[1], new Point(sx, 70 * s), 8 * s * m.TextScale, Solid(Color.FromArgb(190, m.Warn.R, m.Warn.G, m.Warn.B)), false, 210 * s);
                Draw(dc, m.StatValue[2], new Point(sx, 92 * s), 20 * s * m.TextScale, Solid(m.Warn), true, 220 * s);
                Draw(dc, m.StatLabel[2], new Point(sx, 120 * s), 8 * s * m.TextScale, Solid(Color.FromArgb(190, m.Warn.R, m.Warn.G, m.Warn.B)), false, 210 * s);
            }

            // 右侧条形码 + 盖章。
            if (m.ShowFrame)
            {
                double bx = w - 170 * s, by = h * 0.28, bw = 115 * s, bh = 56 * s;
                for (double x2 = 0; x2 < bw; x2 += 4 * s)
                {
                    int kind = 1 + ((int)(x2 / Math.Max(0.1, s)) % 3);
                    dc.DrawRectangle(Solid(m.Warn), null, new Rect(bx + x2, by, kind * s, bh));
                }
                Draw(dc, "SCP FOUNDATION · 机密", new Point(bx, by + bh + 8 * s), 7.5 * s * m.TextScale, Solid(Color.FromArgb(170, m.Warn.R, m.Warn.G, m.Warn.B)), false, 220 * s);
            }
        }

        private void DrawIdRight(DrawingContext dc, double w, double h, double s, Model m)
        {
            if (m.ShowStats)
            {
                double x = w * 0.62;
                Draw(dc, m.StatLabel[0], new Point(x, 38 * s), 8.5 * s * m.TextScale, Solid(m.Sub), false, 120 * s);
                Draw(dc, m.StatValue[0], new Point(x, 50 * s), 28 * s * m.TextScale, Solid(m.Accent), true, 120 * s);
                Draw(dc, m.StatValue[1], new Point(x + 86 * s, 40 * s), 18 * s * m.TextScale, Solid(m.Ink), true, 100 * s);
                Draw(dc, m.StatLabel[1], new Point(x + 86 * s, 64 * s), 8 * s * m.TextScale, Solid(m.Sub), false, 100 * s);
                Draw(dc, m.StatValue[2], new Point(x + 86 * s, 82 * s), 18 * s * m.TextScale, Solid(m.Ink), true, 100 * s);
                Draw(dc, m.StatLabel[2], new Point(x + 86 * s, 106 * s), 8 * s * m.TextScale, Solid(m.Sub), false, 100 * s);
            }

            // 右侧斜切面板 + 六边形照片位。
            if (m.ShowHex)
            {
                DrawHex(dc, new Point(w - 74 * s, h * 0.5), 40 * s, new Pen(Solid(m.Accent), 1.4 * s), true);
                DrawHex(dc, new Point(w - 74 * s, h * 0.5), 27 * s, new Pen(Solid(Color.FromArgb(130, m.Accent.R, m.Accent.G, m.Accent.B)), 1 * s), false);
            }

            if (m.ShowLive)
            {
                Draw(dc, "LEVEL", new Point(w - 104 * s, 30 * s), 7.5 * s * m.TextScale, Solid(m.Sub), false, 80 * s);
                Draw(dc, m.StatValue[1], new Point(w - 104 * s, 38 * s), 26 * s * m.TextScale, Solid(m.Accent), true, 80 * s);
            }
        }

        private void DrawPulseRight(DrawingContext dc, double w, double h, double s, Model m)
        {
            double x = w * 0.58;
            if (m.ShowPlayer && m.Player.Length > 0)
            {
                Draw(dc, m.Player, new Point(x, 40 * s), 17 * s * m.TextScale, Solid(m.Ink), true, w - x - 30 * s);
                Draw(dc, m.PlayerId, new Point(x, 66 * s), 8 * s * m.TextScale, Solid(m.Sub), false, w - x - 30 * s);
            }
            if (m.ShowStats)
            {
                Stat(dc, m.StatValue[0], m.StatLabel[0], x + 8 * s, 92 * s, 18 * s * m.TextScale, 8 * s * m.TextScale, m.Ink, m.Sub, w);
                Stat(dc, m.StatValue[1], m.StatLabel[1], x + 120 * s, 92 * s, 18 * s * m.TextScale, 8 * s * m.TextScale, m.Ink, m.Sub, w);
                Stat(dc, m.StatValue[2], m.StatLabel[2], x + 232 * s, 92 * s, 18 * s * m.TextScale, 8 * s * m.TextScale, m.Ink, m.Sub, w);
            }
            if (m.ShowLive)
            {
                Draw(dc, "● LIVE  " + ShareCardData.Session, new Point(x, 142 * s), 8.5 * s * m.TextScale, Solid(m.Success), false, 260 * s);
                Draw(dc, "MATCHES  " + ShareCardData.Matches, new Point(x, 158 * s), 8 * s * m.TextScale, Solid(m.Sub), false, 260 * s);
            }
        }

        private void DrawTerminal(DrawingContext dc, double w, double h, double s, Model m)
        {
            dc.DrawRectangle(Solid(BaseFill(m.Panel)), null, new Rect(0, 0, w, h));
            if (m.ShowGrid) DrawGrid(dc, w, h, m.Accent, 0.06, s);
            if (m.ShowFrame) dc.DrawRectangle(null, new Pen(Solid(m.Accent), 1.2 * s), new Rect(0.5 * s, 0.5 * s, w - s, h - s));
            if (m.ShowHex) DrawHex(dc, new Point(w * 0.86, h * 0.5), Math.Min(w, h) * 0.30, new Pen(Solid(m.Accent), 1.2 * s), false);
            if (m.ShowFrame) dc.DrawLine(new Pen(Solid(m.Accent), 1.4 * s), new Point(w * 0.62, 24 * s), new Point(w * 0.62, h - 24 * s));
        }

        private void DrawBreach(DrawingContext dc, double w, double h, double s, Model m)
        {
            dc.DrawRectangle(Solid(BaseFill(Color.FromRgb(12, 6, 7))), null, new Rect(0, 0, w, h));
            if (m.ShowGrid) DrawGrid(dc, w, h, m.Danger, 0.07, s);
            dc.DrawLine(new Pen(Solid(m.Danger), 2.4 * s), new Point(0, 2 * s), new Point(w, 2 * s));
            dc.DrawLine(new Pen(Solid(m.Danger), 2.4 * s), new Point(0, h - 2 * s), new Point(w, h - 2 * s));

            // 左侧危险条纹。
            double stripeW = 54 * s;
            Pen hazardPen = new Pen(Solid(Color.FromArgb(160, 255, 110, 110)), 5 * s);
            for (double y = -stripeW; y < h + stripeW; y += 18 * s)
                dc.DrawLine(hazardPen, new Point(0, y), new Point(stripeW, y + stripeW));
            dc.DrawRectangle(Solid(Color.FromArgb(90, 20, 5, 8)), null, new Rect(0, 0, stripeW, h));

            if (m.ShowFrame)
            {
                dc.DrawLine(new Pen(Solid(m.Danger), 1.2 * s), new Point(w - 30 * s, 20 * s), new Point(w - 30 * s, h - 20 * s));
                for (int i = 0; i < 7; i++)
                    dc.DrawRectangle(Solid(i < 5 ? m.Danger : Color.FromRgb(58, 20, 24)), null,
                        new Rect(w - 22 * s, 32 * s + i * 18 * s, 9 * s, 12 * s));
            }

            if (m.ShowStats)
            {
                Draw(dc, m.StatLabel[0], new Point(w * 0.66, 50 * s), 8 * s * m.TextScale, Solid(Color.FromArgb(200, 255, 141, 141)), false, 160 * s);
                Draw(dc, m.StatValue[0], new Point(w * 0.66, 62 * s), 18 * s * m.TextScale, Solid(m.Danger), true, 160 * s);
                Draw(dc, m.StatLabel[1], new Point(w * 0.66, 96 * s), 8 * s * m.TextScale, Solid(Color.FromArgb(200, 255, 141, 141)), false, 160 * s);
                Draw(dc, m.StatValue[1], new Point(w * 0.66, 108 * s), 18 * s * m.TextScale, Solid(m.Danger), true, 160 * s);
                Draw(dc, m.StatValue[2], new Point(w * 0.82, 62 * s), 30 * s * m.TextScale, Solid(m.Danger), true, 180 * s);
                Draw(dc, m.StatLabel[2], new Point(w * 0.82, 96 * s), 8 * s * m.TextScale, Solid(Color.FromArgb(200, 255, 141, 141)), false, 180 * s);
            }
        }

        private void DrawTactical(DrawingContext dc, double w, double h, double s, Model m)
        {
            dc.DrawRectangle(Solid(BaseFill(m.Panel)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Solid(Color.FromArgb(30, m.Accent.R, m.Accent.G, m.Accent.B)), null,
                new Rect(0, 0, w * 0.38, h));
            if (m.ShowGrid) DrawGrid(dc, w, h, m.Accent, 0.05, s);
            if (m.ShowFrame)
            {
                dc.DrawLine(new Pen(Solid(Color.FromArgb(120, m.Accent.R, m.Accent.G, m.Accent.B)), 1 * s),
                    new Point(w * 0.38, 0), new Point(w * 0.38, h));
                dc.DrawRectangle(null, new Pen(Solid(Color.FromArgb(120, m.Line.R, m.Line.G, m.Line.B)), 1 * s),
                    new Rect(0.5 * s, 0.5 * s, w - s, h - s));
            }

            if (m.ShowStats)
            {
                double[] xs = { w * 0.42, w * 0.55, w * 0.68 };
                for (int i = 0; i < 3; i++)
                {
                    dc.DrawRectangle(Solid(Color.FromArgb(25, m.Accent.R, m.Accent.G, m.Accent.B)), null,
                        new Rect(xs[i], 38 * s, 98 * s, 72 * s));
                    dc.DrawLine(new Pen(Solid(m.Accent), 2 * s), new Point(xs[i], 38 * s), new Point(xs[i], 110 * s));
                    Draw(dc, m.StatValue[i], new Point(xs[i] + 12 * s, 48 * s), 23 * s * m.TextScale, Solid(m.Ink), true, 90 * s);
                    Draw(dc, m.StatLabel[i], new Point(xs[i] + 12 * s, 82 * s), 8 * s * m.TextScale, Solid(m.Sub), false, 90 * s);
                }
            }

            // 右侧柱状图。
            if (m.ShowFrame)
            {
                double baseY = h - 34 * s;
                for (int i = 0; i < 7; i++)
                {
                    double bh = (18 + (i * 37 % 54)) * s;
                    Color c = i == 4 ? m.Success : m.Accent;
                    dc.DrawRectangle(Solid(Color.FromArgb(200, c.R, c.G, c.B)), null,
                        new Rect(w * 0.83 + i * 13 * s, baseY - bh, 8 * s, bh));
                }
                dc.DrawLine(new Pen(Solid(Color.FromArgb(150, m.Accent.R, m.Accent.G, m.Accent.B)), 1 * s),
                    new Point(w * 0.82, baseY), new Point(w - 24 * s, baseY));
            }
        }

        private void DrawArchive(DrawingContext dc, double w, double h, double s, Model m)
        {
            dc.DrawRectangle(Solid(BaseFill(Color.FromRgb(13, 11, 7))), null, new Rect(0, 0, w, h));
            for (double y = 0; y < h; y += 5 * s)
                dc.DrawLine(new Pen(Solid(Color.FromArgb(14, m.Warn.R, m.Warn.G, m.Warn.B)), 0.6 * s), new Point(0, y), new Point(w, y));
            if (m.ShowFrame)
            {
                dc.DrawRectangle(null, new Pen(Solid(Color.FromArgb(150, m.Warn.R, m.Warn.G, m.Warn.B)), 1 * s),
                    new Rect(0.5 * s, 0.5 * s, w - s, h - s));

                // 左侧 KETER 盖章。
                Rect stamp = new Rect(24 * s, 58 * s, 54 * s, 58 * s);
                dc.PushTransform(new RotateTransform(-6, stamp.Left + stamp.Width / 2, stamp.Top + stamp.Height / 2));
                dc.DrawRectangle(null, new Pen(Solid(m.Warn), 2 * s), stamp);
                Draw(dc, "KETER", new Point(stamp.Left + 7 * s, stamp.Top + 20 * s), 9 * s * m.TextScale, Solid(m.Warn), true, 50 * s);
                dc.Pop();
            }
        }

        private void DrawIdCard(DrawingContext dc, double w, double h, double s, Model m)
        {
            dc.DrawRectangle(Solid(BaseFill(m.Panel)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Solid(Color.FromArgb(40, m.Accent.R, m.Accent.G, m.Accent.B)), null,
                new Rect(w * 0.72, 0, w * 0.28, h));
            if (m.ShowGrid) DrawGrid(dc, w, h, m.Accent, 0.045, s);
            if (m.ShowFrame) dc.DrawLine(new Pen(Solid(m.Accent), 2 * s), new Point(0, h - 4 * s), new Point(w, h - 4 * s));

            // 照片位。
            if (m.ShowHex)
            {
                Rect photo = new Rect(30 * s, 26 * s, 150 * s, 144 * s);
                dc.DrawRectangle(Solid(Color.FromArgb(45, m.Accent.R, m.Accent.G, m.Accent.B)), null, photo);
                for (double x = photo.Left; x < photo.Right; x += 14 * s)
                    dc.DrawLine(new Pen(Solid(Color.FromArgb(70, m.Accent.R, m.Accent.G, m.Accent.B)), 0.8 * s),
                        new Point(x, photo.Bottom), new Point(x + 60 * s, photo.Top));
                dc.DrawRectangle(null, new Pen(Solid(Color.FromArgb(170, m.Accent.R, m.Accent.G, m.Accent.B)), 1 * s), photo);
                DrawHex(dc, new Point(photo.Left + photo.Width / 2, photo.Top + photo.Height / 2), 42 * s,
                    new Pen(Solid(Color.FromArgb(150, m.Accent.R, m.Accent.G, m.Accent.B)), 1 * s), false);
            }
        }

        private void DrawPulse(DrawingContext dc, double w, double h, double s, Model m)
        {
            dc.DrawRectangle(Solid(BaseFill(m.Panel)), null, new Rect(0, 0, w, h));
            if (m.ShowGrid) DrawGrid(dc, w, h, m.Accent, 0.045, s);

            Pen wave = new Pen(Solid(Color.FromArgb(120, m.Accent.R, m.Accent.G, m.Accent.B)), 1.4 * s);
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                bool open = false;
                for (double x = 0; x < w; x += 6 * s)
                {
                    double y = h * 0.5 + Math.Sin(x / (26 * s)) * 8 * s + (x % (90 * s) < 3 * s ? -18 * s : 0);
                    if (!open) { ctx.BeginFigure(new Point(x, y), false, false); open = true; }
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, wave, g);
            if (m.ShowFrame) dc.DrawLine(new Pen(Solid(m.Line), 1 * s), new Point(0, h * 0.5), new Point(w, h * 0.5));

            // 扫描线。
            if (m.ShowFrame)
                dc.DrawRectangle(Solid(Color.FromArgb(150, m.Accent.R, m.Accent.G, m.Accent.B)), null,
                    new Rect(w * 0.56, 0, 1.4 * s, h));

            if (m.ShowPlayer && m.Player.Length > 0)
            {
                // 右上角胶囊。
                double pillW = Math.Min(210 * s, 20 * s + m.Player.Length * 8 * s);
                Rect pill = new Rect(w - pillW - 30 * s, 28 * s, pillW, 26 * s);
                dc.DrawRoundedRectangle(Solid(Color.FromArgb(180, m.Panel.R, m.Panel.G, m.Panel.B)), new Pen(Solid(m.Accent), 1 * s), pill, 4 * s, 4 * s);
                Draw(dc, m.Player, new Point(pill.Left + 10 * s, pill.Top + 7 * s), 9 * s * m.TextScale, Solid(m.Accent), false, pillW - 20 * s);
            }
            if (m.ShowLive)
            {
                for (int i = 0; i < 9; i++)
                    dc.DrawEllipse(Solid(Color.FromArgb(90, m.Success.R, m.Success.G, m.Success.B)), null,
                        new Point(24 * s + i * 26 * s, 20 * s), 2.2 * s, 2.2 * s);
            }
        }

        private void DrawGlassOverlay(DrawingContext dc, double w, double h, double s, Model m)
        {
            if (!WpfCardGlass.Enabled || WpfCardGlass.Strength <= 0) return;
            RectangleGeometry shape = new RectangleGeometry(new Rect(0, 0, w, h));
            WpfCardGlass.DrawOverlay(dc, shape, w, h);
            if (m.ShowFrame)
            {
                Color tint = WpfCardGlass.TintColor;
                dc.DrawRectangle(null, new Pen(Solid(Color.FromArgb(92, tint.R, tint.G, tint.B)), 1 * s),
                    new Rect(0.5 * s, 0.5 * s, w - s, h - s));
            }
        }

        private void Stat(DrawingContext dc, string value, string label, double x, double y,
            double valueSize, double labelSize, Color ink, Color sub, double w)
        {
            Draw(dc, string.IsNullOrEmpty(value) ? "—" : value, new Point(x, y), valueSize, Solid(ink), true, 160);
            Draw(dc, label ?? "", new Point(x, y + valueSize + 4), labelSize, Solid(sub), false, 160);
        }

        private void Tag(DrawingContext dc, string text, Point at, double size, Color color, double w)
        {
            FormattedText ft = Format(text, size, Solid(color), false, w - at.X - 20);
            Rect box = new Rect(at.X, at.Y, ft.Width + 16, ft.Height + 6);
            dc.DrawRectangle(null, new Pen(Solid(Color.FromArgb(180, color.R, color.G, color.B)), 1), box);
            dc.DrawText(ft, new Point(at.X + 8, at.Y + 3));
        }

        private void DrawGrid(DrawingContext dc, double w, double h, Color color, double alpha, double s)
        {
            Pen p = new Pen(Solid(Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B)), 0.55 * s);
            for (double x = 0; x < w; x += 26 * s) dc.DrawLine(p, new Point(x, 0), new Point(x, h));
            for (double y = 0; y < h; y += 26 * s) dc.DrawLine(p, new Point(0, y), new Point(w, y));
        }

        private void DrawHex(DrawingContext dc, Point c, double r, Pen pen, bool diamond)
        {
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                for (int i = 0; i < (diamond ? 4 : 6); i++)
                {
                    double a = Math.PI * 2 / (diamond ? 4 : 6) * i - Math.PI / 2;
                    Point p = new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
                    if (i == 0) ctx.BeginFigure(p, false, true); else ctx.LineTo(p, true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        private FormattedText Format(string text, double size, Brush brush, bool bold, double maxWidth)
        {
            string value = text ?? "";
            FormattedText ft = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(UiFonts.Title, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                Math.Max(1, size), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double width = Math.Max(30, maxWidth);
            if (ft.Width > width) ft.MaxTextWidth = width;
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            return ft;
        }

        private void Draw(DrawingContext dc, string text, Point at, double size, Brush brush, bool bold, double maxWidth)
        {
            dc.DrawText(Format(text, size, brush, bold, maxWidth), at);
        }

        private static SolidColorBrush Solid(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>名片底色：开启卡片玻璃时留一点透明度，让主界面背景透进来。</summary>
        private static Color BaseFill(Color c)
        {
            if (!WpfCardGlass.Enabled || WpfCardGlass.Strength <= 0) return c;
            int a = (int)Math.Max(105, 255 - WpfCardGlass.Opacity * 1.55);
            return Color.FromArgb((byte)a, c.R, c.G, c.B);
        }

        /// <summary>把名片渲染成 PNG。导出用实色底，避免分享图带透明背景。</summary>
        public void SavePng(string path, double width, double height)
        {
            double oldW = Width, oldH = Height;
            bool glass = WpfCardGlass.Enabled;
            WpfCardGlass.Enabled = false;
            try
            {
                Width = width; Height = height;
                Measure(new Size(width, height));
                Arrange(new Rect(0, 0, width, height));
                UpdateLayout();
                RenderTargetBitmap bmp = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(this);
                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (System.IO.FileStream fs = System.IO.File.Create(path)) enc.Save(fs);
            }
            finally
            {
                WpfCardGlass.Enabled = glass;
                Width = oldW; Height = oldH;
            }
        }
    }
}
