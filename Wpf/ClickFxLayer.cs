using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SCPSLAlert
{
    /// <summary>
    /// 新界面点击特效。
    /// 单击：四条线从点击处向外，近端留一个缺口（Gap0），缺口闭合后立刻消失。
    /// 动画没播完就点别处：不新建线，整组用 0.75s 先快后慢移过去，移动中缺口慢慢张开，
    /// 到达后在新位置重新播放单击效果；移动过程不淡出。
    /// 长按：四条线一直显示并跟随鼠标，松手才淡出；淡出期间的新单击会新建线，不复用旧线。
    /// </summary>
    internal sealed class ClickFxLayer : FrameworkElement
    {
        private enum Mode { Opening, Moving, Held, Fading }

        private sealed class Burst
        {
            public Mode Mode;
            public DateTime T0 = DateTime.UtcNow;
            public Point From;
            public Point To;
            public Point Center;
            public double GapStart;
            public double Gap;
            public double Far;
            public bool Pressed;              // 鼠标还按着
            public bool Rotated;
            public double Angle;
            public double Angle0;
            public DateTime AngleAt = DateTime.MinValue;
            public DateTime FadeAt;
            public double FadeTotal = HoldFadeMs;
        }

        /// <summary>起始缺口（像素），由外观页配置。</summary>
        public double GapSize = 84;
        private const double CloseMs = 260;    // 缺口闭合
        private const double FarMs = 220;      // 远端伸到窗口边
        private const double FadeMs = 70;      // 闭合后立刻消失
        private const double MoveMs = 450;     // 改点移动（更快）
        private const double HoldFadeMs = 220; // 长按松手淡出
        private const double RotateMs = 420;   // 45° 旋转时长
        private const double RotateHoldMs = 200; // 旋转后停留

        private readonly List<Burst> _bursts = new List<Burst>();
        private readonly Color _color;
        private DateTime _lastFrame = DateTime.MinValue;
        private bool _hooked;

        public bool Enabled = true;

        public ClickFxLayer(Color color)
        {
            _color = color;
            IsHitTestVisible = false;
            ClipToBounds = true;
        }

        public void Press(Point p, bool doubleClick)
        {
            if (!Enabled) return;
            Burst latest = Newest();

            if (doubleClick && latest != null && !latest.Rotated)
            {
                if (latest.Mode == Mode.Moving)
                {
                    // 旋转后不再跟随/移动：钉在当前位置
                    latest.To = latest.Center;
                    latest.Mode = Mode.Opening;
                    latest.T0 = DateTime.UtcNow;
                    latest.GapStart = latest.Gap;
                }
                latest.Rotated = true;
                latest.Angle0 = latest.Angle;
                latest.Angle = Math.PI / 4;
                latest.AngleAt = DateTime.UtcNow;
                if (latest.Mode == Mode.Fading)
                {
                    latest.FadeAt = DateTime.UtcNow.AddMilliseconds(RotateMs + RotateHoldMs);
                    latest.FadeTotal = 260;
                    latest.Mode = Mode.Fading;
                }
                Hook();
                InvalidateVisual();
                return;
            }

            // 动画未播完就点别处：把现有这组挪过去，不新建
            if (latest != null && !latest.Rotated
                && (latest.Mode == Mode.Opening || latest.Mode == Mode.Moving))
            {
                latest.From = latest.Center;
                latest.To = p;
                latest.GapStart = latest.Gap;
                latest.T0 = DateTime.UtcNow;
                latest.Mode = Mode.Moving;
                latest.Pressed = true;
                Hook();
                InvalidateVisual();
                return;
            }

            Burst b = new Burst();
            b.T0 = DateTime.UtcNow;
            b.From = p;
            b.To = p;
            b.Center = p;
            b.Mode = Mode.Opening;
            b.GapStart = GapSize;
            b.Gap = GapSize;
            b.Pressed = true;
            _bursts.Add(b);
            if (_bursts.Count > 6) _bursts.RemoveAt(0);
            Hook();
            InvalidateVisual();
        }

        public void Move(Point p)
        {
            Burst b = Newest();
            if (b == null) return;
            if (b.Mode == Mode.Held || (b.Mode == Mode.Opening && b.Pressed))
            {
                b.To = p;
                b.Center = p;
            }
        }

        public void Release()
        {
            Burst b = Newest();
            if (b == null) return;
            b.Pressed = false;
            if (b.Mode == Mode.Held)
            {
                b.Mode = Mode.Fading;
                b.FadeAt = DateTime.UtcNow;
                b.FadeTotal = HoldFadeMs;
            }
        }

        private Burst Newest()
        {
            return _bursts.Count > 0 ? _bursts[_bursts.Count - 1] : null;
        }

        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            CompositionTarget.Rendering += OnFrame;
        }

        private void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        private void OnFrame(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if (_lastFrame != DateTime.MinValue && (now - _lastFrame).TotalMilliseconds < 8) return;
            _lastFrame = now;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_bursts.Count == 0) { Unhook(); return; }
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 2 || h < 2) return;

            DateTime now = DateTime.UtcNow;
            double maxLen = Math.Max(w, h) * 1.2;

            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                Burst b = _bursts[i];
                Advance(b, now, maxLen);
                if (b.Mode == Mode.Fading && (now - b.FadeAt).TotalMilliseconds >= b.FadeTotal)
                {
                    _bursts.RemoveAt(i);
                    continue;
                }
                DrawOne(dc, b, now, maxLen);
            }
            if (_bursts.Count == 0) Unhook();
        }

        private void Advance(Burst b, DateTime now, double maxLen)
        {
            double ms;
            switch (b.Mode)
            {
                case Mode.Opening:
                    ms = (now - b.T0).TotalMilliseconds;
                    {
                        double k = Math.Min(1, ms / CloseMs);
                        b.Gap = b.GapStart * (1 - EaseOut(k));
                        double fk = Math.Min(1, ms / FarMs);
                        b.Far = maxLen * EaseOut(fk);
                        b.Center = b.To;
                        if (k >= 1)
                        {
                            b.Gap = 0;
                            if (b.Pressed)
                            {
                                b.Mode = Mode.Held;     // 按住不放：保持四条线
                            }
                            else if (b.Rotated && b.AngleAt != DateTime.MinValue)
                            {
                                b.Mode = Mode.Fading;
                                b.FadeAt = b.AngleAt.AddMilliseconds(RotateMs + RotateHoldMs);
                                b.FadeTotal = 260;
                                if (b.FadeAt < now) b.FadeAt = now;
                            }
                            else
                            {
                                b.Mode = Mode.Fading;
                                b.FadeAt = now;
                                b.FadeTotal = FadeMs;
                            }
                        }
                    }
                    break;

                case Mode.Held:
                    b.Gap = 0;
                    b.Far = maxLen;
                    b.Center = b.To;
                    break;

                case Mode.Moving:
                    ms = (now - b.T0).TotalMilliseconds;
                    {
                        double k = Math.Min(1, ms / MoveMs);
                        double e = EaseOut(k);
                        b.Center = new Point(b.From.X + (b.To.X - b.From.X) * e,
                                             b.From.Y + (b.To.Y - b.From.Y) * e);
                        b.Gap = b.GapStart + (GapSize - b.GapStart) * EaseInOut(k);
                        b.Far = maxLen;
                        if (k >= 1)
                        {
                            b.Mode = Mode.Opening;
                            b.T0 = now;
                            b.GapStart = GapSize;
                            b.Gap = GapSize;
                        }
                    }
                    break;

                case Mode.Fading:
                    b.Gap = 0;
                    b.Center = b.To;
                    break;
            }
        }

        private void DrawOne(DrawingContext dc, Burst b, DateTime now, double maxLen)
        {
            double alpha = 1;
            if (b.Mode == Mode.Fading)
            {
                double fms = (now - b.FadeAt).TotalMilliseconds;
                alpha = Math.Max(0, Math.Min(1, 1 - fms / b.FadeTotal));
            }
            if (alpha <= 0) return;

            double angle = b.Angle;
            if (b.Rotated && b.AngleAt != DateTime.MinValue)
            {
                double ak = Math.Min(1, (now - b.AngleAt).TotalMilliseconds / RotateMs);
                angle = b.Angle0 + (b.Angle - b.Angle0) * EaseOut(ak);
            }

            Pen pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 235), _color.R, _color.G, _color.B)),
                b.Rotated ? 1.4 : 1.2);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;

            for (int d = 0; d < 4; d++)
            {
                double a = angle + d * Math.PI / 2;
                double dx = Math.Cos(a);
                double dy = Math.Sin(a);
                Point nearPt = new Point(b.Center.X + dx * b.Gap, b.Center.Y + dy * b.Gap);
                Point farPt = new Point(b.Center.X + dx * Math.Max(b.Gap + 1, b.Far),
                    b.Center.Y + dy * Math.Max(b.Gap + 1, b.Far));
                dc.DrawLine(pen, nearPt, farPt);
            }
        }

        private static double EaseOut(double k)
        {
            double p = 1 - k;
            return 1 - p * p * p;
        }

        private static double EaseInOut(double k)
        {
            return k < 0.5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2;
        }
    }
}
