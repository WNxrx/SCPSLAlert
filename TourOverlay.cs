using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>教程的一步。</summary>
    internal class TourStep
    {
        /// <summary>属于哪个板块（显示在卡片上方）。</summary>
        public string Section = "";
        public string Title = "";
        public string Body = "";

        /// <summary>要高亮的控件；null = 只压暗、不高亮。</summary>
        public Control Target;

        /// <summary>进入这一步之前要做的事（例如先切到某一页、或者弹出演示用的提示）。</summary>
        public Action OnEnter;

        /// <summary>离开这一步时要收拾的东西（把演示用的提示 / 面板关掉）。</summary>
        public Action OnExit;

        /// <summary>
        /// 迷你模式：不铺满屏幕、不压暗，只在右下角放一张小卡片。
        /// 这时用户能照常操作界面 —— 用来“让他自己试试看”，但又不会把人挡住。
        /// </summary>
        public bool Mini;

        /// <summary>
        /// 卡片上的第三个按钮（可选）。
        /// 演示弹面板那几步会用到 —— 用户可能正好想先去把按键改成自己顺手的。
        /// </summary>
        public string ExtraLabel = "";
        public Action ExtraAction;
    }

    /// <summary>
    /// 新手教程：把整个界面走一遍。
    ///
    /// 做法是一层铺满屏幕的分层窗口：整体压暗，在目标控件的位置“挖一个洞”，
    /// 洞的边上画一圈强调色，再在旁边放一张说明卡片。
    ///
    /// 随时可以退出：按 Esc、点「跳过」都行，不强制走完。
    /// </summary>
    internal class TourOverlay : Form
    {
        private readonly List<TourStep> _steps;
        private readonly Timer _refresh;   // 目标位置可能因为切页 / 布局变化，定期重画

        private int _index;
        private Bitmap _surface;

        // 卡片上的按钮命中区
        private Rectangle _hitNext, _hitPrev, _hitSkip, _hitExtra;

        private bool _closing;

        /// <summary>用户走完了全程。</summary>
        public event Action Completed;

        /// <summary>用户中途退出。</summary>
        public event Action Exited;

        private const int CardW = 470;
        private const int Pad = 14;

        private static readonly Color Dim = Color.FromArgb(168, 6, 8, 12);
        private static readonly Color CardBg = Color.FromArgb(250, 26, 30, 38);
        private static readonly Color TextColor = Color.FromArgb(238, 241, 246);
        private static readonly Color DimText = Color.FromArgb(148, 156, 170);
        private static readonly Color Accent = Color.FromArgb(90, 190, 255);
        private static readonly Color BtnBg = Color.FromArgb(255, 52, 60, 74);

        public TourOverlay(List<TourStep> steps)
        {
            _steps = steps;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            _refresh = new Timer();
            _refresh.Interval = 200;
            _refresh.Tick += delegate { Redraw(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        /// <summary>开始教程。</summary>
        public void StartTour()
        {
            _index = 0;

            Rectangle b = Screen.PrimaryScreen.Bounds;
            Bounds = b;
            if (!Visible) Show();
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

            NativeMethods.ForceFocus(Handle);
            ApplyStep();
            _refresh.Start();
        }

        private void ApplyStep()
        {
            if (_index < 0 || _index >= _steps.Count) return;
            TourStep s = _steps[_index];
            if (s.OnEnter != null)
            {
                try { s.OnEnter(); } catch { }
            }
            Redraw();
        }

        /// <summary>离开当前这一步：把演示用的东西收掉。</summary>
        private void LeaveStep()
        {
            if (_index < 0 || _index >= _steps.Count) return;
            TourStep s = _steps[_index];
            if (s.OnExit != null)
            {
                try { s.OnExit(); } catch { }
            }
        }

        private void Next()
        {
            if (_index >= _steps.Count - 1) { Finish(true); return; }
            LeaveStep();
            _index++;
            ApplyStep();
        }

        private void Prev()
        {
            if (_index <= 0) return;
            LeaveStep();
            _index--;
            ApplyStep();
        }

        private void Finish(bool completed)
        {
            if (_closing) return;
            LeaveStep();          // 演示用的提示 / 面板要收干净
            _closing = true;
            _refresh.Stop();
            if (Visible) Hide();

            // 焦点还给设置窗口，别让后面点不动
            try { NativeMethods.ForceFocus(Owner != null ? Owner.Handle : IntPtr.Zero); } catch { }

            if (completed) { if (Completed != null) Completed(); }
            else { if (Exited != null) Exited(); }

            try { Dispose(); } catch { }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Finish(false); return; }
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Right || e.KeyCode == Keys.Space)
            {
                Next(); return;
            }
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Back) { Prev(); return; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_hitNext.Contains(e.Location)) { Next(); return; }
            if (_hitPrev.Contains(e.Location)) { Prev(); return; }
            if (_hitSkip.Contains(e.Location)) { Finish(false); return; }
            if (!_hitExtra.IsEmpty && _hitExtra.Contains(e.Location))
            {
                TourStep s = _steps[_index];
                if (s.ExtraAction != null) { try { s.ExtraAction(); } catch { } }
                Redraw();
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_closing) { e.Cancel = true; Finish(false); }
            else base.OnFormClosing(e);
        }

        #region 绘制

        private void Redraw()
        {
            if (_closing || !IsHandleCreated) return;
            if (_index < 0 || _index >= _steps.Count) return;

            TourStep s = _steps[_index];
            Rectangle screen = Screen.PrimaryScreen.Bounds;

            // ---- 迷你模式：只有一张小卡片，右下角，不压暗、不挡操作 ----
            if (s.Mini)
            {
                Size mini = MeasureCard(s, screen.Width - 60);
                Point at = new Point(screen.Right - mini.Width - 24, screen.Bottom - mini.Height - 24);

                Bitmap mb = new Bitmap(mini.Width, mini.Height, PixelFormat.Format32bppArgb);
                using (Graphics mg = Graphics.FromImage(mb))
                {
                    mg.SmoothingMode = SmoothingMode.AntiAlias;
                    mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    mg.Clear(Color.Transparent);
                    DrawCard(mg, s, new Rectangle(0, 0, mini.Width, mini.Height));
                }

                Bounds = new Rectangle(at, mini);   // 窗口缩到只有卡片
                if (_surface != null) _surface.Dispose();
                _surface = mb;
                PushToScreen(mb, at);
                return;
            }

            // ---- 普通模式：铺满屏幕 + 压暗 + 挖洞 ----
            Bounds = screen;

            Rectangle hole = Rectangle.Empty;
            bool hasHole = false;
            if (s.Target != null && s.Target.IsHandleCreated && s.Target.Visible)
            {
                try
                {
                    Rectangle r = s.Target.RectangleToScreen(s.Target.ClientRectangle);
                    hole = Rectangle.Inflate(r, 6, 6);
                    hasHole = hole.Width > 4 && hole.Height > 4;
                }
                catch { hasHole = false; }
            }

            Size cardSize = MeasureCard(s, screen.Width - 60);
            Point cardAt = PlaceCard(cardSize, hole, screen);

            Bitmap bmp = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                // 整屏压暗
                using (SolidBrush b = new SolidBrush(Dim))
                    g.FillRectangle(b, 0, 0, screen.Width, screen.Height);

                // 在目标处挖一个洞（SourceCopy 直接写像素，才能真的擦掉）
                if (hasHole)
                {
                    CompositingMode old = g.CompositingMode;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    using (GraphicsPath hp = AlertRenderer.RoundedRect(hole, 10))
                    using (SolidBrush b = new SolidBrush(Color.Transparent))
                        g.FillPath(b, hp);
                    g.CompositingMode = old;

                    using (GraphicsPath hp = AlertRenderer.RoundedRect(hole, 10))
                    using (Pen p = new Pen(Accent, 2f))
                        g.DrawPath(p, hp);
                }

                DrawCard(g, s, new Rectangle(cardAt, cardSize));
            }

            if (_surface != null) _surface.Dispose();
            _surface = bmp;
            PushToScreen(bmp, new Point(screen.Left, screen.Top));
        }

        private Size MeasureCard(TourStep s, int maxWidth)
        {
            int w = Math.Min(CardW, maxWidth);
            int textW = w - Pad * 2;

            int h = Pad + 18;                       // 板块行
            h += 26;                                 // 标题
            using (Graphics g = Graphics.FromImage(new Bitmap(1, 1)))
            {
                SizeF sz = g.MeasureString(s.Body, BodyFont, textW);
                h += (int)Math.Ceiling(sz.Height) + 6;
            }
            h += 44 + Pad;                           // 按钮行
            return new Size(w, h);
        }

        private Point PlaceCard(Size card, Rectangle hole, Rectangle screen)
        {
            int x, y;

            if (hole.IsEmpty)
            {
                x = screen.Left + (screen.Width - card.Width) / 2;
                y = screen.Top + (screen.Height - card.Height) / 2;
                return new Point(x, y);
            }

            // 优先放目标下方
            y = hole.Bottom + 14;
            if (y + card.Height > screen.Bottom - 20)
            {
                // 下方放不下就放上方
                y = hole.Top - 14 - card.Height;
            }
            if (y < screen.Top + 20) y = screen.Bottom - card.Height - 20;

            // 水平尽量跟目标对齐，但不越界
            x = hole.Left;
            if (x + card.Width > screen.Right - 20) x = screen.Right - card.Width - 20;
            if (x < screen.Left + 20) x = screen.Left + 20;

            return new Point(x, y);
        }

        private void DrawCard(Graphics g, TourStep s, Rectangle card)
        {
            // 外阴影
            for (int i = 6; i >= 1; i--)
                using (GraphicsPath gp = AlertRenderer.RoundedRect(Rectangle.Inflate(card, i, i), 14 + i))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(16, Color.Black)))
                    g.FillPath(b, gp);

            using (GraphicsPath gp = AlertRenderer.RoundedRect(card, 14))
            {
                using (SolidBrush b = new SolidBrush(CardBg))
                    g.FillPath(b, gp);
                using (Pen p = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
                    g.DrawPath(p, gp);
            }

            // 左边一条强调色，和悬浮面板一致
            using (SolidBrush b = new SolidBrush(Accent))
                g.FillRectangle(b, card.X, card.Y + 1, 4, card.Height - 2);

            int x = card.X + Pad + 6;
            int y = card.Y + Pad;
            int textW = card.Width - Pad * 2 - 6;

            // 板块 + 进度
            string head = (s.Section.Length > 0 ? s.Section + "  ·  " : "")
                        + "第 " + (_index + 1) + " / " + _steps.Count + " 步";
            using (SolidBrush b = new SolidBrush(Accent))
                g.DrawString(head, SmallFont, b, x, y);
            y += 18;

            // 标题
            using (SolidBrush b = new SolidBrush(TextColor))
                g.DrawString(s.Title, TitleFont, b, x, y);
            y += 26;

            // 正文（自动换行）
            using (SolidBrush b = new SolidBrush(Color.FromArgb(214, 219, 228)))
                g.DrawString(s.Body, BodyFont, b, new RectangleF(x, y, textW, 400));

            // ---- 按钮 ----
            int by = card.Bottom - Pad - 32;
            int bh = 32;

            _hitSkip = new Rectangle(x, by, 76, bh);
            DrawButton(g, _hitSkip, "跳过", false);

            _hitNext = new Rectangle(card.Right - Pad - 6 - 92, by, 92, bh);
            DrawButton(g, _hitNext, _index >= _steps.Count - 1 ? "完成" : "下一步", true);

            _hitPrev = new Rectangle(_hitNext.Left - 8 - 76, by, 76, bh);
            if (_index > 0) DrawButton(g, _hitPrev, "上一步", false);
            else _hitPrev = Rectangle.Empty;

            // 可选的第三个按钮（“去改按键”这类），放在「跳过」右边
            if (!string.IsNullOrEmpty(s.ExtraLabel))
            {
                int w = 110;
                _hitExtra = new Rectangle(_hitSkip.Right + 8, by, w, bh);
                DrawButton(g, _hitExtra, s.ExtraLabel, false);
            }
            else _hitExtra = Rectangle.Empty;
        }

        private void DrawButton(Graphics g, Rectangle r, string text, bool primary)
        {
            using (GraphicsPath gp = AlertRenderer.RoundedRect(r, 7))
            {
                using (SolidBrush b = new SolidBrush(primary ? Accent : BtnBg))
                    g.FillPath(b, gp);
                if (!primary)
                    using (Pen p = new Pen(Color.FromArgb(80, 255, 255, 255), 1f))
                        g.DrawPath(p, gp);
            }
            SizeF sz = g.MeasureString(text, BodyFont);
            using (SolidBrush b = new SolidBrush(primary ? Color.FromArgb(12, 16, 22) : TextColor))
                g.DrawString(text, BodyFont, b,
                    r.X + (r.Width - sz.Width) / 2f,
                    r.Y + (r.Height - sz.Height) / 2f);
        }

        private void PushToScreen(Bitmap surface, Point origin)
        {
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = surface.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                NativeMethods.SIZE size = new NativeMethods.SIZE();
                size.cx = surface.Width;
                size.cy = surface.Height;

                NativeMethods.POINT src = new NativeMethods.POINT();
                NativeMethods.POINT dst = new NativeMethods.POINT();
                dst.X = origin.X;
                dst.Y = origin.Y;

                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                blend.BlendOp = NativeMethods.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;

                NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size,
                    memDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldBitmap);
                    NativeMethods.DeleteObject(hBitmap);
                }
                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static Font SmallFont
        {
            get { return _small ?? (_small = MakeFont(9f, false)); }
        }

        private static Font BodyFont
        {
            get { return _body ?? (_body = MakeFont(10f, false)); }
        }

        private static Font TitleFont
        {
            get { return _title ?? (_title = MakeFont(13f, true)); }
        }

        private static Font _small, _body, _title;

        private static Font MakeFont(float size, bool bold)
        {
            string face = OverlayForm.ResolveFontFamily("微软雅黑");
            try { return new Font(face, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
            catch { return SystemFonts.DefaultFont; }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refresh.Stop();
                _refresh.Tick -= delegate { };
                if (_surface != null) { _surface.Dispose(); _surface = null; }
            }
            base.Dispose(disposing);
        }
    }
}
