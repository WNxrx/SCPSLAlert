using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SCPSLAlert
{
    /// <summary>
    /// 高级页入口卡片的动态线稿。
    ///   display：整张卡片是坐标系，曲线按“小球在曲线上滚”的速度左右移动（下坡快、上坡慢）；
    ///   recognition：雷达固定，扫描线旋转；
    ///   device：沿卡片自带横线滚动的心率曲线。
    /// 只画线、不画球；卡片本身不动。受 UiAnim 档位与休眠控制。
    /// </summary>
    internal sealed class CardLineArt : FrameworkElement
    {
        private readonly string _kind;
        private readonly Color _tone;
        private readonly bool _light;
        private double _phase;
        private double _sweep;
        private DateTime _last = DateTime.MinValue;
        private bool _hooked;

        public CardLineArt(string kind, Color tone, bool light)
        {
            _kind = kind ?? "";
            _tone = tone;
            _light = light;
            IsHitTestVisible = false;
            Loaded += delegate { Hook(); };
            Unloaded += delegate { Unhook(); };
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
            if (!IsVisible || SCPSLAlert.UiAnim.Off || SCPSLAlert.UiAnim.Sleep)
            {
                _last = DateTime.MinValue;
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (_last == DateTime.MinValue) { _last = now; return; }
            double dt = (now - _last).TotalSeconds;
            if (dt < 1.0 / 30.0) return;
            _last = now;
            if (dt > 0.2) dt = 0.2;
            Step(dt);
            InvalidateVisual();
        }

        /// <summary>推进动画（测试也调这个）。</summary>
        internal void Step(double dt)
        {
            double w = Math.Max(40, ActualWidth);
            if (_kind == "recognition")
            {
                _sweep += dt * (Math.PI * 2 / 18.0);   // 18 秒一圈
            }
            else if (_kind == "device")
            {
                _phase += dt * 90;                     // 90 px/s
            }
            else
            {
                double speed = w / 9.0;                // 基准：9 秒滚过一整张卡
                speed *= SlopeSpeed(w);
                _phase += dt * speed;
            }
            if (_phase > 1e7) _phase %= 1e7;
        }

        /// <summary>小球在曲线上的速度：下坡（屏幕 y 增大）加快，上坡减慢。</summary>
        private double SlopeSpeed(double w)
        {
            double xb = w * 0.34;
            double y1 = CurveY(xb + 1.5, w);
            double y2 = CurveY(xb - 1.5, w);
            double slope = (y1 - y2) / 3.0;            // px / px
            double k = 1.0 + slope * 5.0;
            if (k < 0.35) k = 0.35;
            if (k > 2.2) k = 2.2;
            return k;
        }

        private double CurveY(double x, double w)
        {
            double h = Math.Max(40, ActualHeight);
            double u = (x + _phase) / w * Math.PI * 2;
            double v = 0.50
                + 0.20 * Math.Sin(u)
                + 0.05 * Math.Sin(u * 2 + 0.7);
            return h * v;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 20 || h < 20) return;

            byte a = (byte)(_light ? 34 : 30);
            byte faint = (byte)(_light ? 18 : 16);
            Pen pen = new Pen(new SolidColorBrush(Color.FromArgb(a, _tone.R, _tone.G, _tone.B)), 1.1);
            Pen grid = new Pen(new SolidColorBrush(Color.FromArgb(faint, _tone.R, _tone.G, _tone.B)), 0.8);

            if (_kind == "recognition") DrawRadar(dc, w, h, pen, grid, a);
            else if (_kind == "device") DrawEcg(dc, w, h, pen, grid, a);
            else DrawCurve(dc, w, h, pen, grid);
        }

        // ---------------- 显示：整卡坐标系 + 滚动曲线 ----------------
        private void DrawCurve(DrawingContext dc, double w, double h, Pen pen, Pen grid)
        {
            // 整张卡片都是坐标系
            for (int i = 1; i < 8; i++)
            {
                double x = w * i / 8.0;
                dc.DrawLine(grid, new Point(x, 0), new Point(x, h));
            }
            for (int i = 1; i < 6; i++)
            {
                double y = h * i / 6.0;
                dc.DrawLine(grid, new Point(0, y), new Point(w, y));
            }

            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, CurveY(0, w)), false, false);
                for (double x = 2; x <= w; x += 2)
                    ctx.LineTo(new Point(x, CurveY(x, w)), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        // ---------------- 识别：固定雷达 + 旋转扫描线 ----------------
        private void DrawRadar(DrawingContext dc, double w, double h, Pen pen, Pen grid, byte a)
        {
            double cx = w * 0.55;
            double cy = h * 0.40;
            double r = Math.Min(w, h) * 0.33;
            for (int i = 1; i <= 4; i++)
                dc.DrawEllipse(null, i == 4 ? pen : grid, new Point(cx, cy), r * i / 4, r * i / 4);

            for (int i = 0; i < 36; i++)
            {
                double ang = i * Math.PI / 18;
                double r1 = r * (i % 3 == 0 ? 0.93 : 0.965);
                dc.DrawLine(grid,
                    new Point(cx + Math.Cos(ang) * r1, cy + Math.Sin(ang) * r1),
                    new Point(cx + Math.Cos(ang) * r, cy + Math.Sin(ang) * r));
            }

            double sweep = -Math.PI / 2 + _sweep;
            for (int i = 0; i < 14; i++)
            {
                double ang = sweep - i * 0.045;
                byte al = (byte)Math.Max(6, a - i * 3);
                Pen tail = new Pen(new SolidColorBrush(Color.FromArgb(al, _tone.R, _tone.G, _tone.B)), 1.1);
                dc.DrawLine(tail, new Point(cx, cy), new Point(cx + Math.Cos(ang) * r, cy + Math.Sin(ang) * r));
            }
            Pen lead = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Min(255, a + 60), _tone.R, _tone.G, _tone.B)), 1.4);
            dc.DrawLine(lead, new Point(cx, cy), new Point(cx + Math.Cos(sweep) * r, cy + Math.Sin(sweep) * r));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)Math.Min(255, a + 40), _tone.R, _tone.G, _tone.B)),
                null, new Point(cx, cy), 2.4, 2.4);
        }

        // ---------------- 设备：沿卡片横线滚动的心率 ----------------
        private void DrawEcg(DrawingContext dc, double w, double h, Pen pen, Pen grid, byte a)
        {
            double baseY = h * 0.56;
            dc.DrawLine(grid, new Point(0, baseY), new Point(w, baseY));

            // 心电波形（相位滚动）
            double period = 200;
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, baseY), false, false);
                for (double x = 2; x <= w; x += 2)
                {
                    double t = ((x - _phase) % period + period) % period / period;
                    double v = Beat(t);
                    ctx.LineTo(new Point(x, baseY - v * h * 0.20), true, false);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        /// <summary>一个心跳周期（0..1），返回 -1..1 的幅度。</summary>
        private static double Beat(double t)
        {
            double v = 0;
            v += 0.10 * Gauss(t, 0.16, 0.035);   // P
            v += -0.12 * Gauss(t, 0.30, 0.018);  // Q
            v += 1.00 * Gauss(t, 0.36, 0.022);   // R
            v += -0.30 * Gauss(t, 0.42, 0.020);  // S
            v += 0.22 * Gauss(t, 0.62, 0.055);   // T
            return v;
        }

        private static double Gauss(double t, double mu, double sigma)
        {
            double d = (t - mu) / sigma;
            return Math.Exp(-0.5 * d * d);
        }
    }
}
