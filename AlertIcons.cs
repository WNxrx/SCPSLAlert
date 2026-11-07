using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SCPSLAlert
{
    /// <summary>每种提示左边的小标识（用矢量画出来，不依赖字体里有没有这个符号）。</summary>
    internal enum AlertIcon
    {
        None,
        Play,      // 播放三角：回合开始
        Clock,     // 时钟：回合计时 / 支援估算
        Arrow,     // 箭头：进入服务器
        Screen,    // 屏幕：游戏启动
        Warning,   // 警告三角：核弹
        Ring,      // 圆环：净化
        Diamond    // 菱形：SCP 阵亡
    }

    internal static class AlertIcons
    {
        public static void Draw(Graphics g, Rectangle r, AlertIcon kind, Color color)
        {
            if (kind == AlertIcon.None) return;
            if (r.Width < 5 || r.Height < 5) return;

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float cx = r.Left + r.Width / 2f;
            float cy = r.Top + r.Height / 2f;

            using (SolidBrush brush = new SolidBrush(color))
            using (Pen pen = new Pen(color, Math.Max(1.6f, r.Width / 7f)))
            {
                switch (kind)
                {
                    case AlertIcon.Play:
                        g.FillPolygon(brush, new PointF[]
                        {
                            new PointF(r.Left + r.Width * 0.18f, r.Top + r.Height * 0.10f),
                            new PointF(r.Right - r.Width * 0.10f, cy),
                            new PointF(r.Left + r.Width * 0.18f, r.Bottom - r.Height * 0.10f)
                        });
                        break;

                    case AlertIcon.Clock:
                        {
                            int t = Math.Max(2, r.Width / 6);
                            g.DrawEllipse(pen, r.Left + t / 2f, r.Top + t / 2f, r.Width - t, r.Height - t);
                            g.DrawLine(pen, cx, cy, cx, cy - r.Height * 0.24f);
                            g.DrawLine(pen, cx, cy, cx + r.Width * 0.20f, cy);
                            break;
                        }

                    case AlertIcon.Arrow:
                        {
                            float shaft = r.Height * 0.24f;
                            float head = r.Width * 0.44f;
                            g.FillPolygon(brush, new PointF[]
                            {
                                new PointF(r.Left, cy - shaft / 2f),
                                new PointF(r.Right - head, cy - shaft / 2f),
                                new PointF(r.Right - head, r.Top),
                                new PointF(r.Right, cy),
                                new PointF(r.Right - head, r.Bottom),
                                new PointF(r.Right - head, cy + shaft / 2f),
                                new PointF(r.Left, cy + shaft / 2f)
                            });
                            break;
                        }

                    case AlertIcon.Screen:
                        {
                            int m = Math.Max(2, r.Width / 5);
                            using (GraphicsPath gp = new GraphicsPath(FillMode.Alternate))
                            {
                                using (GraphicsPath outer = AlertRenderer.RoundedRect(r, Math.Max(2, r.Width / 5)))
                                    gp.AddPath(outer, false);
                                using (GraphicsPath inner = AlertRenderer.RoundedRect(
                                           new Rectangle(r.Left + m, r.Top + m, r.Width - 2 * m, r.Height - 2 * m),
                                           Math.Max(1, r.Width / 5 - m / 2)))
                                    gp.AddPath(inner, false);
                                g.FillPath(brush, gp);
                            }
                            break;
                        }

                    case AlertIcon.Warning:
                        {
                            using (GraphicsPath gp = new GraphicsPath(FillMode.Alternate))
                            {
                                gp.AddPolygon(new PointF[]
                                {
                                    new PointF(cx, r.Top),
                                    new PointF(r.Right, r.Bottom),
                                    new PointF(r.Left, r.Bottom)
                                });
                                float m = r.Width * 0.30f;
                                gp.AddPolygon(new PointF[]
                                {
                                    new PointF(cx, r.Top + m * 1.7f),
                                    new PointF(r.Right - m, r.Bottom - m * 0.75f),
                                    new PointF(r.Left + m, r.Bottom - m * 0.75f)
                                });
                                g.FillPath(brush, gp);
                            }
                            break;
                        }

                    case AlertIcon.Ring:
                        {
                            int t = Math.Max(2, r.Width / 5);
                            using (GraphicsPath gp = new GraphicsPath(FillMode.Alternate))
                            {
                                gp.AddEllipse(r);
                                gp.AddEllipse(new Rectangle(r.Left + t, r.Top + t, r.Width - 2 * t, r.Height - 2 * t));
                                g.FillPath(brush, gp);
                            }
                            g.FillEllipse(brush, cx - r.Width * 0.12f, cy - r.Height * 0.12f,
                                          r.Width * 0.24f, r.Height * 0.24f);
                            break;
                        }

                    case AlertIcon.Diamond:
                        g.FillPolygon(brush, new PointF[]
                        {
                            new PointF(cx, r.Top),
                            new PointF(r.Right, cy),
                            new PointF(cx, r.Bottom),
                            new PointF(r.Left, cy)
                        });
                        break;
                }
            }

            g.SmoothingMode = old;
        }
    }
}
