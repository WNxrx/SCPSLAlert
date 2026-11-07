using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 用鼠标在屏幕上直接框出一块 OCR 区域。
    ///
    /// 做法：铺一层半透明的全屏遮罩（整个虚拟桌面，多显示器也盖得住），
    /// 按住左键拖出一块，松开就确认；Esc 或右键取消。
    /// 返回的是**屏幕物理像素**坐标，和 OCR 截图用的坐标系一致。
    ///
    /// 注意：如果游戏是"独占全屏"，这层遮罩会被游戏盖住看不见也点不到，
    /// 只能先把游戏改成"无边框窗口"再框。
    /// </summary>
    internal class RegionPickerForm : Form
    {
        /// <summary>小于这个尺寸视为误触，不采纳。</summary>
        private const int MinSize = 24;

        private readonly string _hint;
        private Point _start;
        private bool _dragging;
        private Rectangle _rect;
        private Rectangle _screenOffset;

        /// <summary>框出来的区域（屏幕坐标）。取消时是空矩形。</summary>
        public Rectangle SelectedRect
        {
            get { return _rect; }
        }

        public RegionPickerForm(string hint)
        {
            _hint = hint;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            BackColor = Color.Black;
            Opacity = 0.35;          // 压暗整个屏幕，明确告诉用户"现在在框选"
            Text = "框选 OCR 区域";

            _screenOffset = SystemInformation.VirtualScreen;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { Activate(); Focus(); } catch { }
        }

        private Rectangle Normalize(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            int w = Math.Abs(a.X - b.X);
            int h = Math.Abs(a.Y - b.Y);
            return new Rectangle(x, y, w, h);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Right) { Cancel(); return; }
            if (e.Button != MouseButtons.Left) return;

            _start = e.Location;
            _dragging = true;
            _rect = new Rectangle(e.Location, Size.Empty);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            _rect = Normalize(_start, e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false;

            if (_rect.Width < MinSize || _rect.Height < MinSize)
            {
                _rect = Rectangle.Empty;
                Invalidate();
                return;      // 太小了，当误触，让用户重来
            }

            // 客户区坐标 → 屏幕坐标
            _rect = new Rectangle(_rect.X + _screenOffset.Left, _rect.Y + _screenOffset.Top,
                                  _rect.Width, _rect.Height);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Cancel();
        }

        private void Cancel()
        {
            _rect = Rectangle.Empty;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

            // 顶部提示条
            string hint = _hint == null ? "" : _hint;
            string line = "按住鼠标左键框选 OCR 区域，松开确认；Esc / 右键取消    —— " + hint;
            using (Font font = new Font("微软雅黑", 12f, FontStyle.Bold))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(230, 20, 24, 32)))
            using (SolidBrush fg = new SolidBrush(Color.White))
            {
                SizeF sz = g.MeasureString(line, font);
                RectangleF box = new RectangleF((ClientSize.Width - sz.Width) / 2f - 18f, 26f,
                                                sz.Width + 36f, sz.Height + 18f);
                g.FillRectangle(bg, box);
                g.DrawString(line, font, fg, box.X + 18f, box.Y + 9f);
            }

            if (_rect.Width <= 0 || _rect.Height <= 0) return;

            // 选区边框（画在客户区坐标上）
            Rectangle local = new Rectangle(_rect.X - _screenOffset.Left, _rect.Y - _screenOffset.Top,
                                            _rect.Width, _rect.Height);
            using (Pen outer = new Pen(Color.FromArgb(255, 60, 180, 255), 4f))
            using (Pen inner = new Pen(Color.White, 1f))
            {
                g.DrawRectangle(outer, local);
                g.DrawRectangle(inner, local);
            }

            // 尺寸标签
            string size = _rect.Width + " × " + _rect.Height;
            using (Font font = new Font("Consolas", 10f, FontStyle.Bold))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(230, 20, 24, 32)))
            using (SolidBrush fg = new SolidBrush(Color.FromArgb(255, 120, 210, 255)))
            {
                SizeF sz = g.MeasureString(size, font);
                float lx = local.X;
                float ly = local.Y - sz.Height - 6f;
                if (ly < 0f) ly = local.Y + 6f;
                g.FillRectangle(bg, lx, ly, sz.Width + 12f, sz.Height + 4f);
                g.DrawString(size, font, fg, lx + 6f, ly + 2f);
            }
        }
    }
}
