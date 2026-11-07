using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace SCPSLAlert
{
    /// <summary>
    /// 新版 A.R.C.T 提示的“卡片”绘制。它只负责画，不依赖窗口：
    /// 屏幕上的提示和设置页里的预览共用这一份，保证看到什么就是什么。
    ///
    /// 设计语言（对齐新界面的 A.R.C.T 终端）：
    ///   · 近黑底 + 切角 + 发丝线 + 扫描线，不用圆角卡片；
    ///   · 左侧一条信号色竖线，状态色只出现在“信号”和“计时”上；
    ///   · 标题用中文黑体，编号/计时用等宽，形成“工业终端”的字号对比；
    ///   · 倒计时从正文里拆出来，单独放大成读数；正文只留事件名。
    /// </summary>
    internal sealed class WpfPromptCard : FrameworkElement
    {
        private const double Outer = 3;       // 外圈留给阴影 / 发光
        private const double Cut = 9;         // 切角
        private const double PadX = 14;
        private const double PadTop = 4;

        private static readonly FontFamily UiFont =
            UiFonts.Body;
        private static readonly FontFamily DisplayFont =
            UiFonts.Title;
        private static readonly FontFamily MonoFont =
            UiFonts.Mono;

        private static readonly Regex ClockTail = new Regex(
            @"(?<clock>\d{1,3}:\d{2}(?::\d{2})?)\s*$", RegexOptions.Compiled);

        private AlertConfig _alert;
        private string _text = "";
        private bool _highlight;
        private bool _edit;
        private double _phase;
        private double _entrance = 1.0;
        private Size _size = new Size(320, 74);

        private string _header = "SYS // ALERT";
        private string _title = "";
        private string _clock = "";
        private string _clockTag = "";
        private bool _countdown;
        private double _timerRemain;
        private double _timerTotal;
        private Color _accent = Color.FromRgb(198, 255, 61);
        private bool _isDanger;

        public WpfPromptCard()
        {
            SnapsToDevicePixels = true;
            IsHitTestVisible = false;
        }

        public AlertConfig Alert
        {
            get { return _alert; }
            set { _alert = value; Refresh(); }
        }

        public string Text
        {
            get { return _text; }
            set { _text = value == null ? "" : value; Refresh(); }
        }

        public bool Highlight
        {
            get { return _highlight; }
            set
            {
                if (_highlight == value) return;
                _highlight = value;
                Recolor();
                InvalidateVisual();
            }
        }

        /// <summary>
        /// 入场进度：0 = 还没出现，1 = 完全停稳。
        /// 由 WpfAlertOverlay 用 WinForms 计时器推进，宿主没有 WPF 动画时钟也不会卡住。
        /// </summary>
        public double Entrance
        {
            get { return _entrance; }
            set
            {
                double v = value;
                if (v < 0) v = 0;
                if (v > 1) v = 1;
                if (Math.Abs(v - _entrance) < 0.001) return;
                _entrance = v;
                InvalidateVisual();
            }
        }

        public bool EditMode
        {
            get { return _edit; }
            set { _edit = value; InvalidateVisual(); }
        }

        /// <summary>0~1 的扫描相位（只用于微动效，不参与排版）。</summary>
        public double Phase
        {
            get { return _phase; }
            set
            {
                double v = value - Math.Floor(value);
                if (Math.Abs(v - _phase) < 0.002) return;
                _phase = v;
                InvalidateVisual();
            }
        }

        public Size CardSize { get { return _size; } }

        public void SetContent(AlertConfig alert, string text, bool highlight)
        {
            bool needRecolor = _alert != alert || _highlight != highlight;
            _alert = alert;
            _text = text == null ? "" : text;
            _highlight = highlight;
            _timerRemain = 0;
            _timerTotal = 0;
            if (needRecolor) Recolor(); else Parse();
            InvalidateMeasure();
            InvalidateVisual();
        }

        /// <summary>倒计时进度。total ≤ 0 时保持“扫描线”动效，不假装知道总量。</summary>
        public void SetTimer(double remainSeconds, double totalSeconds)
        {
            _timerRemain = remainSeconds;
            _timerTotal = totalSeconds;
            InvalidateVisual();
        }

        private void Refresh()
        {
            Recolor();
            InvalidateMeasure();
            InvalidateVisual();
        }

        #region 语义色 / 文案

        private void Recolor()
        {
            _accent = AccentFor(_alert);
            _isDanger = IsDanger(_alert);
            Parse();
        }

        private static Color AccentFor(AlertConfig a)
        {
            string s = a == null ? "" : (a.Section ?? "");
            if (s == "Warhead" || s == "DeadManAlert" || s == "LocalDeathAlert")
                return Color.FromRgb(255, 77, 77);
            if (s == "Decontamination" || s == "ScpDeath" || s == "ScpKilled"
                || s == "TokenAlert" || s == "RespawnAlert")
                return Color.FromRgb(255, 138, 42);
            if (s == "RoundTimer" || s == "ServerJoin" || s == "GameLaunch"
                || s == "SupportEstimate" || s == "WaveAlert" || s == "TicketAlert")
                return Color.FromRgb(56, 214, 255);
            if (s == "SelfRespawnAlert")
                return Color.FromRgb(107, 231, 154);
            return Color.FromRgb(198, 255, 61);
        }

        private static bool IsDanger(AlertConfig a)
        {
            string s = a == null ? "" : (a.Section ?? "");
            return s == "Warhead" || s == "DeadmanAlert" || s == "DeadManAlert"
                || s == "LocalDeathAlert" || s == "Decontamination";
        }

        private static string CodeFor(AlertConfig a)
        {
            string s = a == null ? "" : (a.Section ?? "");
            if (s == "RoundStart") return "MISSION START";
            if (s == "RoundTimer") return "ROUND CLOCK";
            if (s == "ServerJoin") return "LINK ESTABLISHED";
            if (s == "GameLaunch") return "CLIENT ONLINE";
            if (s == "Warhead") return "WARHEAD";
            if (s == "Decontamination") return "DECON";
            if (s == "ScpDeath") return "SCP LOST";
            if (s == "SupportEstimate") return "REINFORCEMENT";
            if (s == "GeneratorAlert") return "GENERATOR";
            if (s == "GenActiveAlert") return "GENERATOR ONLINE";
            if (s == "TokenAlert") return "TOKEN";
            if (s == "RoleAlert") return "ROLE ASSIGN";
            if (s == "TicketAlert") return "TICKETS";
            if (s == "WaveAlert") return "WAVE";
            if (s == "ScpKilled") return "SCP KILLED";
            if (s == "DeadManAlert") return "DEADMAN";
            if (s == "RespawnAlert") return "RESPAWN";
            if (s == "LocalDeathAlert") return "VITALS LOST";
            if (s == "SelfRespawnAlert") return "VITALS RESTORED";
            if (s == "StartupAlert") return "SYSTEM";
            return string.IsNullOrEmpty(s) ? "ALERT" : s.ToUpperInvariant();
        }

        private void Parse()
        {
            string t = _text == null ? "" : _text.Trim();
            _clock = "";
            _clockTag = "";

            Match m = ClockTail.Match(t);
            if (m.Success)
            {
                _clock = m.Groups["clock"].Value;
                t = t.Substring(0, m.Index).Trim();
                _countdown = true;
            }
            else
            {
                _countdown = false;
            }

            if (t.EndsWith("T-", StringComparison.Ordinal))
            {
                _clockTag = "T-";
                t = t.Substring(0, t.Length - 2).Trim();
            }
            else if (t.EndsWith("约", StringComparison.Ordinal))
            {
                _clockTag = "~";
                t = t.Substring(0, t.Length - 1).Trim();
            }
            else if (t.EndsWith("约 ", StringComparison.Ordinal))
            {
                _clockTag = "~";
                t = t.Substring(0, t.Length - 2).Trim();
            }

            // 去掉尾部“：”和“-”，标题只留事件本身
            t = t.TrimEnd('：', ':', '-', '—', ' ');
            if (t.Length == 0) t = _alert == null ? "提示" : _alert.DisplayName;
            _title = t;
            _header = "SYS // " + CodeFor(_alert);
            if (_highlight) _header = "ALERT // " + CodeFor(_alert);
        }

        #endregion

        #region 排版

        protected override Size MeasureOverride(Size availableSize)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double clockW = 0, tagW = 0;
            double clockSize = _clock.Length >= 8 ? 18 : 22;
            if (_countdown)
            {
                FormattedText c = MakeText(_clock, MonoFont, FontWeights.Bold, clockSize,
                    Brushes.White, 200, dpi);
                clockW = c.WidthIncludingTrailingWhitespace;
                if (_clockTag.Length > 0)
                {
                    FormattedText tag = MakeText(_clockTag, MonoFont, FontWeights.Normal, 8.5,
                        Brushes.White, 40, dpi);
                    tagW = tag.WidthIncludingTrailingWhitespace + 4;
                }
            }

            FormattedText header = MakeText(_header, DisplayFont, FontWeights.SemiBold, 9,
                Brushes.White, 600, dpi);
            FormattedText rawTitle = MakeText(_title, UiFont, FontWeights.Bold, _countdown ? 12.5 : 14,
                Brushes.White, 2000, dpi);

            double iconW = 26;
            double rightW = _countdown ? tagW + clockW + 14 : 0;
            double natural = PadX * 2 + iconW + rawTitle.WidthIncludingTrailingWhitespace + rightW + 10;
            double w = Math.Max(280, Math.Min(450, natural));
            w = Math.Max(w, header.WidthIncludingTrailingWhitespace + PadX * 2 + 58);

            double titleMax = Math.Max(80, w - (Outer + PadX + 26) - Outer - PadX - rightW - 6);
            FormattedText title = MakeText(_title, UiFont, FontWeights.Bold,
                _countdown ? 12.5 : 14, Brushes.White, titleMax, dpi);

            double mainTop = Outer + PadTop + 11;
            double mainH = Math.Max(title.Height, _countdown ? clockSize * 1.18 : 0);
            double barY = mainTop + mainH + 3;
            double h = barY + 5 + Outer;

            _size = new Size(Math.Ceiling(w + Outer * 2), Math.Ceiling(h));
            return _size;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            Clip = BuildShape(ActualWidth, ActualHeight, Cut, Outer);
        }

        #endregion

        #region 绘制

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 8 || h < 8) return;

            double progress = _entrance;
            if (progress <= 0.001) return;

            // 入场：面板从左往右“拉”出来，内容稍晚于边框出现。
            // 用自定义进度（不是 WPF 时间线），所以 WinForms 主循环里也能稳定播。
            double reveal = EaseInOutCubic(Math.Min(1.0, progress / 0.66));
            double content = SmoothStep(0.26, 0.90, progress);
            bool revealClip = reveal < 0.999;
            if (revealClip)
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(0.8, w * reveal), h)));

            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            Color accent = _highlight ? Color.FromRgb(255, 77, 77) : _accent;
            if (_highlight && !_isDanger) accent = Color.FromRgb(255, 138, 42);
            Brush accentBrush = Solid(accent);
            Brush ink = Solid(_highlight ? Color.FromRgb(255, 248, 244) : Color.FromRgb(242, 255, 249));
            Brush sub = Solid(_highlight ? Color.FromRgb(255, 196, 178) : Color.FromRgb(143, 174, 166));
            Brush muted = Solid(Color.FromRgb(82, 110, 103));

            Geometry shape = BuildShape(w, h, Cut, Outer);

            // 外圈阴影 / 高亮发光（画在切角外形外侧，不被裁掉）
            for (int i = 4; i >= 1; i--)
            {
                Geometry glow = BuildShape(w - i * 2, h - i * 2, Cut, Outer + i);
                byte a = (byte)(_highlight ? 22 : 10);
                dc.DrawGeometry(Solid(Color.FromArgb(a, 0, 0, 0)), null, glow);
            }
            if (_highlight)
            {
                Geometry aura = BuildShape(w, h, Cut, Outer);
                dc.DrawGeometry(Solid(Color.FromArgb(30, accent.R, accent.G, accent.B)), null, aura);
            }

            dc.PushClip(shape);

            // 底：近黑 + 极轻的顶亮下暗，避免“数字塑料感”
            LinearGradientBrush bg = new LinearGradientBrush(
                Color.FromArgb(246, 11, 17, 15),
                Color.FromArgb(246, 5, 8, 7), 90);
            bg.Freeze();
            dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

            // 扫描线：2~3% 透明度，横向 1px / 4px
            for (double y = Outer; y < h - Outer; y += 4)
                dc.DrawRectangle(Solid(Color.FromArgb(9, 198, 255, 61)), null,
                    new Rect(Outer, y, w - Outer * 2, 1));

            // 右上角斜切装饰线
            Pen hatch = new Pen(Solid(Color.FromArgb(26, accent.R, accent.G, accent.B)), 1);
            hatch.Freeze();
            for (int i = 0; i < 5; i++)
            {
                double x = w - Outer - 8 - i * 9;
                dc.DrawLine(hatch, new Point(x, Outer + 1),
                    new Point(x + 18, Outer + 19));
            }

            // 入场瞬间的“信号噪声”横条：只在拉出阶段出现，很快消失
            if (progress < 0.72)
            {
                double noise = (1 - progress / 0.72);
                dc.PushOpacity(noise * 0.75);
                for (int i = 0; i < 5; i++)
                {
                    double ny = Outer + 8 + ((i * 13 + (int)(progress * 40)) % Math.Max(1, (int)(h - Outer * 2 - 16)));
                    double nx = Outer + 4 + ((i * 37 + 11) % Math.Max(1, (int)(w - Outer * 2 - 60)));
                    double nw = 14 + (i * 17) % 46;
                    dc.DrawRectangle(Solid(Color.FromArgb(150, accent.R, accent.G, accent.B)), null,
                        new Rect(nx, ny, Math.Min(nw, w - Outer - nx), 1));
                }
                dc.Pop();
            }

            // 入场时的一次性提亮：擦除完成的一瞬间“亮一下”，随后落回正常
            if (progress > 0.44 && progress < 0.76)
            {
                double flash = (1 - (progress - 0.44) / 0.32) * 42;
                if (flash > 0)
                    dc.DrawGeometry(Solid(Color.FromArgb((byte)flash, accent.R, accent.G, accent.B)),
                        null, shape);
            }

            // 左侧信号竖线：状态只占这一小条，大面积保持中性
            dc.DrawRectangle(accentBrush, null,
                new Rect(Outer, Outer + 2, 3, h - Outer * 2 - 4));

            // 顶部微型标题
            dc.PushOpacity(0.12 + content * 0.88);
            double headerX = Outer + PadX + 6;
            double titleX = Outer + PadX + 26;
            FormattedText header = MakeText(_header, DisplayFont, FontWeights.SemiBold, 9,
                _highlight ? accentBrush : sub, w - headerX - Outer - PadX - 92, dpi);
            DrawText(dc, header, headerX, Outer + PadTop);

            // 右上角：状态 + 本地时间
            string state = _highlight ? "ALERT" : (_edit ? "DRAG"
                : (progress < 0.66 ? "SYNC" : "LIVE"));
            FormattedText stateFt = MakeText(state, DisplayFont, FontWeights.SemiBold, 8,
                _highlight ? accentBrush : muted, 60, dpi);
            FormattedText timeFt = MakeText(DateTime.Now.ToString("HH:mm:ss"), MonoFont,
                FontWeights.Normal, 7.2, muted, 70, dpi);
            double rightX = w - Outer - PadX;
            DrawText(dc, timeFt, rightX - timeFt.WidthIncludingTrailingWhitespace, Outer + PadTop + 1);
            DrawText(dc, stateFt,
                rightX - timeFt.WidthIncludingTrailingWhitespace - 7 - stateFt.WidthIncludingTrailingWhitespace,
                Outer + PadTop);

            // 主行：图标 + 标题 + 倒计时
            double mainTop = Outer + PadTop + 11;
            double clockSize = _clock.Length >= 8 ? 18 : 22;
            double contentShift = (1 - content) * 9;
            FormattedText title = MakeText(_title, UiFont, FontWeights.Bold,
                _countdown ? 12.5 : 14, ink,
                w - titleX - Outer - PadX - (_countdown ? 100 : 0), dpi);
            double titleH = title.Height;
            double mainH = Math.Max(titleH, _countdown ? clockSize * 1.18 : 0);
            double iconY = mainTop + (mainH - 18) / 2;
            DrawIcon(dc, new Rect(Outer + PadX + 2 - contentShift, iconY, 18, 18),
                _alert == null ? AlertIcon.None : _alert.Style.Icon, accentBrush);

            double textY = mainTop + (mainH - titleH) / 2 - 1;
            DrawText(dc, title, titleX - contentShift, textY);

            if (_countdown)
            {
                double drawClock = clockSize * (0.92 + 0.08 * content);
                FormattedText clock = MakeText(_clock, MonoFont, FontWeights.Bold, drawClock,
                    _highlight ? accentBrush : ink, 220, dpi);
                double clockX = w - Outer - PadX - clock.WidthIncludingTrailingWhitespace + contentShift;
                double clockY = mainTop + (mainH - clock.Height) / 2 - 1;
                DrawText(dc, clock, clockX, clockY);

                if (_clockTag.Length > 0)
                {
                    FormattedText tag = MakeText(_clockTag, MonoFont, FontWeights.Normal, 8.5,
                        _highlight ? accentBrush : sub, 40, dpi);
                    DrawText(dc, tag,
                        clockX - tag.WidthIncludingTrailingWhitespace - 4,
                        clockY + clock.Height - tag.Height - 3);
                }
            }

            // 底部读数条：刻度 + 扫描段（不再单独占一行页脚，保证默认 56px 槽位不叠）
            double barY = mainTop + mainH + 3;
            double bx = Outer + PadX;
            double bw = w - Outer * 2 - PadX * 2;
            dc.DrawRectangle(Solid(Color.FromArgb(80, 42, 65, 57)), null,
                new Rect(bx, barY, bw, 1));

            if (_countdown)
            {
                double frac = -1;
                if (_timerTotal > 0.5)
                {
                    frac = _timerRemain / _timerTotal;
                    if (frac < 0) frac = 0;
                    if (frac > 1) frac = 1;
                    dc.DrawRectangle(Solid(Color.FromArgb(170, accent.R, accent.G, accent.B)), null,
                        new Rect(bx, barY + 1, bw * frac, 2));
                }

                for (double x = bx; x < bx + bw; x += 8)
                {
                    bool major = ((int)Math.Round((x - bx) / 8)) % 5 == 0;
                    dc.DrawRectangle(Solid(major ? Color.FromArgb(120, accent.R, accent.G, accent.B)
                                                 : Color.FromArgb(45, 150, 190, 170)),
                        null, new Rect(x, barY + 2, 1, major ? 3 : 2));
                }

                if (frac < 0)
                {
                    double sweep = bw * _phase;
                    double sw = Math.Min(42, bw);
                    LinearGradientBrush g = new LinearGradientBrush(
                        Color.FromArgb(0, accent.R, accent.G, accent.B),
                        Color.FromArgb(190, accent.R, accent.G, accent.B), 0);
                    g.Freeze();
                    dc.DrawRectangle(g, null, new Rect(bx + Math.Max(0, sweep - sw), barY + 2,
                        Math.Min(sw, bw - Math.Max(0, sweep - sw)), 2));
                }
            }
            else
            {
                dc.DrawRectangle(accentBrush, null, new Rect(bx, barY + 2, 30, 2));
                for (int i = 0; i < 3; i++)
                    dc.DrawRectangle(Solid(Color.FromArgb((byte)(110 - i * 28), accent.R, accent.G, accent.B)),
                        null, new Rect(bx + 34 + i * 5, barY + 2, 2, 2));
            }

            // 编辑模式：虚线框 + 四角夹点
            dc.Pop();   // 结束内容透明度
            if (_edit)
            {
                Pen dash = new Pen(Solid(Color.FromRgb(56, 214, 255)), 1);
                dash.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
                dash.Freeze();
                dc.DrawRectangle(null, dash, new Rect(Outer + 1, Outer + 1, w - Outer * 2 - 2, h - Outer * 2 - 2));
                Brush handle = Solid(Color.FromRgb(56, 214, 255));
                dc.DrawRectangle(handle, null, new Rect(Outer - 1, Outer - 1, 5, 5));
                dc.DrawRectangle(handle, null, new Rect(w - Outer - 4, Outer - 1, 5, 5));
                dc.DrawRectangle(handle, null, new Rect(Outer - 1, h - Outer - 4, 5, 5));
                dc.DrawRectangle(handle, null, new Rect(w - Outer - 4, h - Outer - 4, 5, 5));
            }

            dc.Pop();

            // 边框：普通 1px 发丝线，高亮时 2px 信号色
            Pen border = new Pen(_highlight ? accentBrush : Solid(Color.FromArgb(118, 52, 80, 70)),
                _highlight ? 2 : 1);
            border.Freeze();
            dc.DrawGeometry(null, border, shape);

            // 左上 / 右下角括号，最便宜的“工业终端”味
            Pen bracket = new Pen(Solid(Color.FromArgb(150, accent.R, accent.G, accent.B)), 1);
            bracket.Freeze();
            double q = Outer + 7;
            dc.DrawLine(bracket, new Point(q, Outer + 1), new Point(q + 12, Outer + 1));
            dc.DrawLine(bracket, new Point(Outer + 1, q), new Point(Outer + 1, q + 12));
            dc.DrawLine(bracket, new Point(w - Outer - 1, h - q), new Point(w - Outer - 1, h - q - 12));
            dc.DrawLine(bracket, new Point(w - q, h - Outer - 1), new Point(w - q - 12, h - Outer - 1));

            if (revealClip)
            {
                dc.Pop();   // 结束“从左往右拉出”的裁剪
                DrawRevealEdge(dc, w, h, reveal, accent);
            }
            else if (progress > 0.58 && progress < 0.96)
            {
                // 擦除完成后的二次扫描线：从左边再扫到右边，扫完就停。
                double sweep = (progress - 0.58) / 0.38;
                double sx = Outer + Math.Max(0, Math.Min(1, sweep)) * (w - Outer * 2);
                double alpha = (1 - sweep) * 110;
                if (alpha > 0)
                {
                    LinearGradientBrush trail = new LinearGradientBrush(
                        Color.FromArgb(0, accent.R, accent.G, accent.B),
                        Color.FromArgb((byte)alpha, accent.R, accent.G, accent.B), 0);
                    trail.Freeze();
                    dc.DrawRectangle(trail, null, new Rect(Math.Max(Outer, sx - 26), Outer + 1,
                        Math.Min(26, sx - Outer), h - Outer * 2 - 2));
                    Pen scan = new Pen(Solid(Color.FromArgb((byte)alpha, accent.R, accent.G, accent.B)), 1);
                    scan.Freeze();
                    dc.DrawLine(scan, new Point(sx, Outer + 1), new Point(sx, h - Outer - 1));
                }
            }
        }

        /// <summary>拉出阶段的引导边：一条亮线 + 向后拖的渐变 + 两枚小刻度。</summary>
        private static void DrawRevealEdge(DrawingContext dc, double w, double h,
            double reveal, Color accent)
        {
            double x = Math.Max(Outer + 1, Math.Min(w - Outer - 1, w * reveal));
            LinearGradientBrush trail = new LinearGradientBrush(
                Color.FromArgb(0, accent.R, accent.G, accent.B),
                Color.FromArgb(115, accent.R, accent.G, accent.B), 0);
            trail.Freeze();
            dc.DrawRectangle(trail, null, new Rect(Math.Max(Outer, x - 22), Outer + 1,
                Math.Min(22, x - Outer), h - Outer * 2 - 2));

            Pen edge = new Pen(Solid(Color.FromArgb(245, accent.R, accent.G, accent.B)), 1.4);
            edge.Freeze();
            dc.DrawLine(edge, new Point(x, Outer + 1), new Point(x, h - Outer - 1));

            Brush tick = Solid(Color.FromArgb(220, accent.R, accent.G, accent.B));
            dc.DrawRectangle(tick, null, new Rect(x + 3, Outer + 3, 5, 1));
            dc.DrawRectangle(tick, null, new Rect(x + 3, h - Outer - 4, 5, 1));
        }

        private static double EaseOutCubic(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double u = 1 - t;
            return 1 - u * u * u;
        }

        private static double EaseInOutCubic(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            if (t < 0.5) return 4 * t * t * t;
            double u = -2 * t + 2;
            return 1 - (u * u * u) / 2;
        }

        private static double SmoothStep(double a, double b, double x)
        {
            if (x <= a) return 0;
            if (x >= b) return 1;
            double t = (x - a) / (b - a);
            return t * t * (3 - 2 * t);
        }

        private static void DrawText(DrawingContext dc, FormattedText ft, double x, double y)
        {
            dc.DrawText(ft, new Point(x, y));
        }

        private static FormattedText MakeText(string text, FontFamily family, FontWeight weight,
            double size, Brush brush, double maxWidth, double dpi)
        {
            if (text == null) text = "";
            FormattedText ft = new FormattedText(text, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal),
                size, brush, dpi);
            if (maxWidth > 8)
            {
                ft.MaxTextWidth = maxWidth;
                ft.Trimming = TextTrimming.CharacterEllipsis;
            }
            return ft;
        }

        private static void DrawIcon(DrawingContext dc, Rect r, AlertIcon kind, Brush brush)
        {
            Pen pen = new Pen(brush, 1.6);
            pen.Freeze();
            double cx = r.X + r.Width / 2;
            double cy = r.Y + r.Height / 2;
            switch (kind)
            {
                case AlertIcon.Play:
                    {
                        StreamGeometry g = new StreamGeometry();
                        using (StreamGeometryContext c = g.Open())
                        {
                            c.BeginFigure(new Point(r.X + 4, r.Y + 3), true, true);
                            c.LineTo(new Point(r.Right - 3, cy), true, false);
                            c.LineTo(new Point(r.X + 4, r.Bottom - 3), true, false);
                        }
                        g.Freeze();
                        dc.DrawGeometry(brush, null, g);
                        break;
                    }
                case AlertIcon.Clock:
                    dc.DrawEllipse(null, pen, new Point(cx, cy), 8, 8);
                    dc.DrawLine(pen, new Point(cx, cy), new Point(cx, cy - 5));
                    dc.DrawLine(pen, new Point(cx, cy), new Point(cx + 4, cy));
                    break;
                case AlertIcon.Arrow:
                    dc.DrawLine(pen, new Point(r.X + 2, cy), new Point(r.Right - 2, cy));
                    dc.DrawLine(pen, new Point(r.Right - 6, cy - 4), new Point(r.Right - 2, cy));
                    dc.DrawLine(pen, new Point(r.Right - 6, cy + 4), new Point(r.Right - 2, cy));
                    break;
                case AlertIcon.Screen:
                    dc.DrawRectangle(null, pen, new Rect(r.X + 2, r.Y + 3, r.Width - 4, r.Height - 7));
                    dc.DrawLine(pen, new Point(r.X + 5, r.Bottom - 2), new Point(r.Right - 5, r.Bottom - 2));
                    break;
                case AlertIcon.Warning:
                    {
                        StreamGeometry g = new StreamGeometry();
                        using (StreamGeometryContext c = g.Open())
                        {
                            c.BeginFigure(new Point(cx, r.Y + 2), true, true);
                            c.LineTo(new Point(r.Right - 2, r.Bottom - 3), true, false);
                            c.LineTo(new Point(r.X + 2, r.Bottom - 3), true, false);
                        }
                        g.Freeze();
                        dc.DrawGeometry(null, pen, g);
                        dc.DrawLine(pen, new Point(cx, r.Y + 8), new Point(cx, r.Y + 13));
                        dc.DrawRectangle(brush, null, new Rect(cx - 0.8, r.Y + 15, 1.6, 1.6));
                        break;
                    }
                case AlertIcon.Ring:
                    dc.DrawEllipse(null, pen, new Point(cx, cy), 7, 7);
                    dc.DrawEllipse(brush, null, new Point(cx, cy), 2.5, 2.5);
                    break;
                case AlertIcon.Diamond:
                    {
                        StreamGeometry g = new StreamGeometry();
                        using (StreamGeometryContext c = g.Open())
                        {
                            c.BeginFigure(new Point(cx, r.Y + 2), true, true);
                            c.LineTo(new Point(r.Right - 2, cy), true, false);
                            c.LineTo(new Point(cx, r.Bottom - 2), true, false);
                            c.LineTo(new Point(r.X + 2, cy), true, false);
                        }
                        g.Freeze();
                        dc.DrawGeometry(brush, null, g);
                        break;
                    }
                default:
                    dc.DrawRectangle(brush, null, new Rect(r.X + 8, r.Y + 8, 4, 4));
                    break;
            }
        }

        private static Geometry BuildShape(double w, double h, double cut, double inset)
        {
            if (w <= 0 || h <= 0) return Geometry.Empty;
            double l = inset, t = inset, r = w - inset, b = h - inset;
            if (r - l < cut * 2 || b - t < cut * 2) cut = Math.Min((r - l) / 2, (b - t) / 2);
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(l, t), true, true);
                c.LineTo(new Point(r - cut, t), true, false);
                c.LineTo(new Point(r, t + cut), true, false);
                c.LineTo(new Point(r, b), true, false);
                c.LineTo(new Point(l + cut, b), true, false);
                c.LineTo(new Point(l, b - cut), true, false);
            }
            g.Freeze();
            return g;
        }

        private static SolidColorBrush Solid(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        #endregion
    }
}
