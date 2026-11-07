using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 把 OCR 区域**画在屏幕上**给用户看。
    ///
    /// 两个用途：
    ///   1. 区域列表窗口打开时自动显示一次：一眼看清现在这些区域都在哪、
    ///      有没有互相重叠、有没有框偏（以前只能看数字，完全没有空间感）；
    ///   2. 在列表里右键某个区域 →「在屏幕上高亮显示」：只重点高亮那一个。
    ///
    /// 它是全屏透明遮罩，画的框**鼠标完全穿透**，不会拦住任何点击；
    /// 点一下或者按 Esc 就消失。
    /// </summary>
    internal class RegionOverlayForm : Form
    {
        private readonly List<OcrRegion> _regions = new List<OcrRegion>();
        private string _focusName = "";
        private bool _persistent;

        private static readonly Color NormalColor = Color.FromArgb(255, 190, 90);
        private static readonly Color FocusColor = Color.FromArgb(120, 230, 255);
        private static readonly Font LabelFont =
            new Font("微软雅黑", 9f, FontStyle.Bold, GraphicsUnit.Point);

        public RegionOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Opacity = 0.30;
            DoubleBuffered = true;
            KeyPreview = true;
        }

        /// <summary>
        /// 持久显示模式：**不再加那层黑色遮罩**，只画彩色的框。
        ///
        /// 【为什么两种模式要分开】
        /// 一闪而过的时候，那层 30% 黑遮罩很有用 —— 它把游戏画面压暗，
        /// 框的走向一眼就能看清。但"一直显示"就不行了：
        ///   · 整屏压暗会严重影响正常用（用户还要在区域列表窗口上点按钮）；
        ///   · 而且会让用户误以为游戏画面变暗了。
        /// 所以持久模式改成"纯描边"：靠 TransparencyKey 把黑色背景**完全抠透明**，
        /// 只留下框本身，既不遮画面也不挡鼠标。
        ///
        /// 【为什么纯描边模式下必须关掉抗锯齿】抗锯齿会把边框边缘和黑色背景
        /// 混成中间色，那些像素**不是纯黑**，于是不会被抠掉，
        /// 看上去就是每个框外面糊了一圈黑边。关掉之后边框像素颜色统一，
        /// 要么全在要么全走，干干净净。
        /// </summary>
        public bool Persistent
        {
            get { return _persistent; }
            set
            {
                if (_persistent == value) return;
                _persistent = value;
                ApplyMode();
                Invalidate();
            }
        }

        /// <summary>
        /// 强制指定"要不要那层黑色遮罩"。null = 按模式自动决定。
        ///
        /// 【为什么需要这个】看单个框的时候**不该压暗屏幕** ——
        /// 整屏变暗很打扰（用户可能正在对着游戏调框），
        /// 而且只高亮一个框本来也不需要靠压暗来突出。
        /// 所以"在屏幕上高亮显示这个区域"走的是不压暗的那条路。
        /// </summary>
        public bool? MaskOverride;

        private void ApplyMode()
        {
            // 不压暗的条件：持久显示，或者被显式要求不要遮罩
            bool noMask = _persistent || (MaskOverride.HasValue && !MaskOverride.Value);
            _noMask = noMask;

            if (noMask)
            {
                Opacity = 1.0;                      // 不再靠整窗透明度压暗
                TransparencyKey = Color.Black;      // 黑色背景 = 完全透明
            }
            else
            {
                TransparencyKey = Color.Empty;
                Opacity = 0.30;
            }
        }

        private bool _noMask;


        /// <summary>鼠标穿透：这是"看"用的，绝不能挡住任何操作。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020;   // WS_EX_TRANSPARENT
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        /// <summary>把要显示的框换算成屏幕坐标（处理"从底边算起"）。</summary>
        public void SetRegions(IEnumerable<OcrRegion> regions, string focusName)
        {
            _regions.Clear();
            if (regions != null)
            {
                foreach (OcrRegion r in regions)
                {
                    if (r != null && r.Enabled) _regions.Add(r.Clone());
                }
            }
            _focusName = focusName == null ? "" : focusName;

            // 覆盖整个虚拟桌面（多显示器也能看全）
            Rectangle vs = SystemInformation.VirtualScreen;
            Bounds = vs;

            // MaskOverride 可能是在 Show() 之前设的，这里再同步一次，
            // 保证 TransparencyKey / Opacity 跟当前模式一致。
            ApplyMode();
            Invalidate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Invalidate();
        }

        /// <summary>
        /// 先把整块画面画进一张缓冲位图，再一次性贴到窗口上（不闪烁）。
        ///
        /// 【两条经验，都是踩出来的】
        /// 1. 只允许在 OnPaint 里画。用 CreateGraphics() 往窗口上直接画的话，
        ///    画出来的东西**不受 Form.Opacity 约束**（透明度是窗口合成阶段才施加的），
        ///    于是框会立刻以全不透明显示，而 Invalidates 触发重绘时又画一遍 ——
        ///    观感就是"框先闪一下、遮罩随后才淡入"。
        /// 2. g.Clear(Color.Black) 是透明底：配合 Opacity = 0.3，
        ///    整体就是一层 30% 的黑色遮罩，而框自身仍然保持鲜艳。
        /// </summary>
        private void DrawScene(Graphics target)
        {
            // 不压暗模式走"纯描边"：背景保持纯黑（会被 TransparencyKey 抠成透明），
            // 而且**必须关掉抗锯齿** —— 否则边框边缘和黑底混出来的中间色
            // 不是纯黑、抠不掉，每个框外面会糊一圈黑边（实测踩过）。
            if (_noMask)
            {
                target.SmoothingMode = SmoothingMode.None;
                target.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                target.Clear(Color.Black);

                foreach (OcrRegion r in _regions)
                {
                    Rectangle rc = ToClient(r);
                    bool focus = _focusName.Length > 0 && r.Name == _focusName;
                    Color c = focus ? FocusColor : NormalColor;

                    // 不用半透明填充：透明填充和黑底混出来的也不是纯黑，
                    // 会在框内留下一层灰蒙蒙的底。持久模式只要描边。
                    using (Pen p = new Pen(c, focus ? 4f : 2f))
                        target.DrawRectangle(p, rc);

                    // 名称牌也只描边，不填底色
                    string tag = r.Name + "  " + rc.Width + "×" + rc.Height;
                    SizeF sz = target.MeasureString(tag, LabelFont);
                    RectangleF plate = new RectangleF(
                        rc.X, Math.Max(0, rc.Y - sz.Height - 4), sz.Width + 12, sz.Height + 4);

                    using (SolidBrush tick = new SolidBrush(c))
                    {
                        using (Pen bp = new Pen(c, 1f))
                            target.DrawRectangle(bp, plate.X, plate.Y, plate.Width, plate.Height);
                        target.DrawString(tag, LabelFont, tick, plate.X + 5, plate.Y + 1);
                    }
                }
                return;
            }

            target.SmoothingMode = SmoothingMode.AntiAlias;
            target.Clear(Color.Black);

            foreach (OcrRegion r in _regions)
            {
                Rectangle rc = ToClient(r);
                bool focus = _focusName.Length > 0 && r.Name == _focusName;
                Color c = focus ? FocusColor : NormalColor;

                // 填充淡一点、描边实一点
                using (SolidBrush b = new SolidBrush(Color.FromArgb(focus ? 46 : 26, c)))
                    target.FillRectangle(b, rc);

                using (Pen p = new Pen(c, focus ? 3f : 2f))
                    target.DrawRectangle(p, rc);

                // 左上角挂一个名字牌
                string tag = r.Name + "  " + rc.Width + "×" + rc.Height;
                SizeF sz = target.MeasureString(tag, LabelFont);
                RectangleF plate = new RectangleF(
                    rc.X, Math.Max(0, rc.Y - sz.Height - 2), sz.Width + 10, sz.Height + 2);

                using (SolidBrush b = new SolidBrush(Color.FromArgb(210, 20, 24, 32)))
                    target.FillRectangle(b, plate);
                using (SolidBrush b = new SolidBrush(c))
                    target.DrawString(tag, LabelFont, b, plate.X + 5, plate.Y + 1);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 什么都不做：底色由 OnPaint 里的缓冲位图负责，
            // 否则会先刷一遍纯黑再被覆盖，白闪一下。
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;

            if (_buf == null || _buf.Width != Width || _buf.Height != Height)
            {
                if (_buf != null) _buf.Dispose();
                _buf = new Bitmap(Width, Height);
            }

            using (Graphics g = Graphics.FromImage(_buf))
                DrawScene(g);

            e.Graphics.DrawImageUnscaled(_buf, 0, 0);
        }

        private Bitmap _buf;

        /// <summary>
        /// 淡出：Opacity 降到 0 之后再关窗。
        ///
        /// 【为什么不能直接 Close】窗口一旦销毁，屏幕上那层黑色遮罩会"啪"地消失；
        /// 而框是画在遮罩上的，两者同时消失才自然。这里让它们一起淡出。
        /// </summary>
        private void StartFadeOutCore()
        {
            if (_fading) return;
            _fading = true;

            _fadeTimer = new Timer();
            _fadeTimer.Interval = 16;
            _fadeTimer.Tick += delegate
            {
                double o = Opacity - 0.08;
                if (o <= 0)
                {
                    _fadeTimer.Stop();
                    _fadeTimer.Dispose();
                    _fadeTimer = null;
                    Close();
                    return;
                }
                Opacity = o;
            };
            _fadeTimer.Start();
        }

        private bool _fading;
        private Timer _fadeTimer;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_buf != null) { _buf.Dispose(); _buf = null; }
                if (_fadeTimer != null) { _fadeTimer.Stop(); _fadeTimer.Dispose(); _fadeTimer = null; }
                if (LabelFont != null) { }   // 静态字体，不在这里释放
            }
            base.Dispose(disposing);
        }

        private Rectangle ToClient(OcrRegion r)
        {
            Rectangle abs = r.Rect();
            return new Rectangle(abs.X - Left, abs.Y - Top, abs.Width, abs.Height);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // 持久模式下**绝对不能自己关掉** —— 用户正在区域列表窗口上点按钮，
            // 遮罩是穿透的、本来就不该收到点击；万一收到了也不能把框收走。
            if (_persistent) return;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !_persistent) { Close(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        /// <summary>
        /// 持久模式下要忽略自动淡出 / 自动关闭。
        /// 区域列表窗口传来的一切"几秒后消失"都要在这里被挡住。
        /// </summary>
        public void StartFadeOut()
        {
            if (_persistent) return;      // 一直显示，不淡出
            StartFadeOutCore();
        }

    }
}
