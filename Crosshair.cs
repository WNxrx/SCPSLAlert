using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 准星覆盖层控制器。
    ///
    /// 组成：
    ///   · KeyboardHook：缩圈 / 回弹（可选，按配置里的键）；
    ///   · 左键轮询（GetAsyncKeyState）→ 开火扩散；
    ///   · 一个「游戏在前台才显示」的轮询定时器，负责定位与显隐；
    ///   · 真正的绘制在 CrosshairOverlayForm 的独立渲染线程里跑（默认 240 fps）。
    /// 所有动画都是按时间戳算的闭式解，帧率变化不会让动画跑偏。
    /// </summary>
    internal sealed class CrosshairController : IDisposable
    {
        private readonly Config _config;
        private readonly KeyboardHook _keys = new KeyboardHook();
        private readonly CrosshairOverlayForm _overlay;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly System.Windows.Forms.Timer _poll;
        private int _lastVk;
        private int _lastTick;

        // 左键状态只用轮询读：游戏里低级鼠标钩子会被反作弊/输入层拦掉，
        // 表现为“键盘能收到、鼠标收不到”。GetAsyncKeyState 直接读物理键状态，不经过任何注入。
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_LBUTTON = 0x01;
        private bool _pollLeftDown;


        public CrosshairController(Config config)
        {
            _config = config;
            _overlay = new CrosshairOverlayForm(config);

            _keys.Combo += OnKey;
            _keys.Install();

            // 4ms 轮询左键状态（约 250Hz，比显示器刷新还快，足够跟手）
            _poll = new System.Windows.Forms.Timer();
            _poll.Interval = 4;
            _poll.Tick += delegate { PollMouse(); };
            _poll.Start();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 200;
            _timer.Tick += delegate { UpdateOverlay(); };
            _timer.Start();
        }

        /// <summary>
        /// 左键状态轮询（唯一来源）。
        ///
        /// 为什么不用低级鼠标钩子：游戏里的钩子会被反作弊/输入层拦截，
        /// 表现为“键盘能收到、鼠标收不到”。GetAsyncKeyState 直接读物理键状态，
        /// 不经过任何注入，游戏里同样有效。
        /// </summary>
        private void PollMouse()
        {
            bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            if (down == _pollLeftDown) return;
            _pollLeftDown = down;
            DispatchFire(down);
        }

        private void DispatchFire(bool down)
        {
            if (!_config.DisplayAssistEnabled || !_config.CrosshairEnabled || KeyboardHook.Suspended) return;
            if (!_config.CrosshairDynamic || !_config.CrosshairFireSpread) return;
            if (!IsGameForeground()) return;
            if (down) _overlay.BeginFire();
            else _overlay.EndFire();
        }

        private void OnKey(int vk, bool ctrl, bool alt, bool shift, bool win)
        {
            if (!_config.DisplayAssistEnabled || !_config.CrosshairEnabled || KeyboardHook.Suspended || !IsGameForeground()) return;
            int now = Environment.TickCount;
            if (vk == _lastVk && unchecked(now - _lastTick) < 120) return;
            _lastVk = vk; _lastTick = now;

            HotkeySpec ring = HotkeySpec.Parse(_config.CrosshairRingKey);
            HotkeySpec expand = HotkeySpec.Parse(_config.CrosshairExpandKey);
            if (Matches(ring, vk, ctrl, alt, shift, win)) _overlay.BeginRing();
            if (Matches(expand, vk, ctrl, alt, shift, win)) _overlay.BeginExpand();
        }

        private static bool Matches(HotkeySpec h, int vk, bool ctrl, bool alt, bool shift, bool win)
        { return h != null && h.IsValid && h.Vk == vk && h.Ctrl == ctrl && h.Alt == alt && h.Shift == shift && h.Win == win; }

        private void UpdateOverlay()
        {
            bool show = _config.DisplayAssistEnabled && _config.CrosshairEnabled && IsGameForeground();
            if (!show)
            {
                if (_overlay.Visible) _overlay.HideOverlay();
                return;
            }
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            // 基准改为游戏客户区（不含标题栏/边框），而不是整个窗口矩形。
            // 这样全屏/无边框/窗口化三种模式下准星都落在画面中心；
            // 偏移仍按物理像素存，开了“按 DPI 缩放”时一起缩放，保持观感。
            Rectangle client = BrightnessSampler.ClientBounds(hwnd);
            if (client.IsEmpty)
            {
                NativeMethods.RECT r;
                if (!NativeMethods.GetWindowRect(hwnd, out r)) return;
                client = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            if (client.Width <= 0 || client.Height <= 0) return;
            double scale = ScreenGeometry.DpiScaleForPoint(new Point(client.Left + client.Width / 2, client.Top + client.Height / 2));
            double k = _config.CrosshairDpiScale ? Math.Max(0.5, scale) : 1.0;
            int cx = client.Left + client.Width / 2 + (int)Math.Round(_config.CrosshairOffsetX * k);
            int cy = client.Top + client.Height / 2 + (int)Math.Round(_config.CrosshairOffsetY * k);
            _overlay.ShowAt(cx, cy);
        }

        private static bool IsGameForeground()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            uint pid; NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return false;
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                    return p.ProcessName.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.ProcessName.IndexOf("SCP Secret Laboratory", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            _poll.Stop();
            _poll.Dispose();
            _keys.Dispose();
            _overlay.Close();
            _overlay.Dispose();
        }
    }

    /// <summary>
    /// 分层窗口（per-pixel alpha）+ 独立渲染线程。
    ///
    /// 为什么不用原来的 TransparencyKey 方案：
    ///   · TransparencyKey 只有 1-bit 透明，抗锯齿边缘会发黑/发白；
    ///   · 每帧 Invalidate 走的是 UI 线程消息泵，帧率被钉在 ~60；
    ///   · 这里用 UpdateLayeredWindow 直接推一张 32 位位图，渲染线程想跑多快跑多快。
    /// 另外：静止不动时不做任何重绘（空闲 0 开销），只有扩散/缩圈/回弹/命中时才逐帧画。
    /// </summary>
    internal sealed class CrosshairOverlayForm : Form
    {
        private const int Canvas = 340;
        private const int UlwAlpha = 0x00000002;
        private const int DibRgbColors = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public int bmiColors;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, int usage,
            out IntPtr bits, IntPtr section, int offset);

        private readonly Config _config;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private Thread _thread;
        private volatile bool _running;
        private volatile bool _shown;
        private volatile int _left;
        private volatile int _top;

        // 直接画进 DIB：这样 UpdateLayeredWindow 拿到的永远是最新像素
        private IntPtr _memDc = IntPtr.Zero;
        private IntPtr _hDib = IntPtr.Zero;
        private IntPtr _oldObj = IntPtr.Zero;
        private Graphics _gfx;
        private readonly object _gdiLock = new object();

        private volatile bool _holding;
        private double _holdAt;
        private double _releaseAt = -100;
        private double _spreadAtRelease;
        private readonly System.Collections.Generic.List<double> _ringQueue = new System.Collections.Generic.List<double>();
        private readonly object _ringLock = new object();
        private double _expandAt = -100;
        private double _hitAt = -100;
        private volatile float _scale = 1f;
        private string _lastSignature = "";

        public int MeasuredFps { get; private set; }
        public long RenderCount;
        public string LastError = "";

        public CrosshairOverlayForm(Config config)
        {
            _config = config;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Width = Canvas;
            Height = Canvas;
            BackColor = Color.Black;
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                             | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            InitDib();
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    _scale = Math.Max(1f, g.DpiX / 96f);
            }
            catch { _scale = 1f; }
            StartRenderThread();
        }

        private void InitDib()
        {
            lock (_gdiLock)
            {
                if (_hDib != IntPtr.Zero) return;
                BITMAPINFO bmi = new BITMAPINFO();
                bmi.bmiHeader = new BITMAPINFOHEADER();
                bmi.bmiHeader.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = Canvas;
                bmi.bmiHeader.biHeight = -Canvas;      // 负数 = 自上而下
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;       // BI_RGB
                IntPtr bits;
                _hDib = CreateDIBSection(IntPtr.Zero, ref bmi, DibRgbColors, out bits, IntPtr.Zero, 0);
                if (_hDib == IntPtr.Zero)
                {
                    LastError = "CreateDIBSection 失败 " + Marshal.GetLastWin32Error();
                    return;
                }
                _memDc = CreateCompatibleDC(IntPtr.Zero);
                _oldObj = SelectObject(_memDc, _hDib);
                _gfx = Graphics.FromHdc(_memDc);
                _gfx.SmoothingMode = SmoothingMode.AntiAlias;
                _gfx.PixelOffsetMode = PixelOffsetMode.HighQuality;
                _gfx.CompositingMode = CompositingMode.SourceOver;
            }
        }

        // ---------------- 外部调用（UI 线程） ----------------
        public void ShowAt(int centerX, int centerY)
        {
            _left = centerX - Canvas / 2;
            _top = centerY - Canvas / 2;
            if (!Visible) Show();
            // 目标显示器可能和主屏 DPI 不同；Show() 之后再按准星中心点取一次，
            // 避免 OnHandleCreated 用主屏 DPI 把这里刚设好的值覆盖掉。
            try { _scale = (float)ScreenGeometry.DpiScaleForPoint(new Point(centerX, centerY)); }
            catch { }
            _shown = true;
        }

        public void HideOverlay()
        {
            _shown = false;
            if (Visible) Hide();
        }

        public void BeginFire()
        {
            _holding = true;
            _holdAt = Now;
            _hitAt = Now;
        }

        public void EndFire()
        {
            if (!_holding) return;
            _spreadAtRelease = CurrentSpread();
            _releaseAt = Now;
            _holding = false;
        }

        public void BeginRing() { lock (_ringLock) { _ringQueue.Add(Now); if (_ringQueue.Count > 10) _ringQueue.RemoveAt(0); } }
        public void BeginExpand() { _expandAt = Now; }

        // ---------------- 渲染线程 ----------------
        private double Now { get { return _clock.Elapsed.TotalSeconds; } }

        private void StartRenderThread()
        {
            if (_thread != null) return;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Priority = ThreadPriority.AboveNormal;
            _thread.Name = "CrosshairRender";
            _thread.Start();
        }

        private void Loop()
        {
            try { timeBeginPeriod(1); }
            catch { }

            Stopwatch sw = Stopwatch.StartNew();
            double next = 0;
            int frames = 0;
            double fpsAt = 0;

            while (_running)
            {
                double now = sw.Elapsed.TotalSeconds;
                int target = Math.Max(60, Math.Min(500, _config.CrosshairRenderFps));
                double period = 1.0 / target;

                if (now >= next)
                {
                    next += period;
                    if (next < now) next = now + period;

                    if (_shown && IsHandleCreated)
                    {
                        try { RenderFrame(); }
                        catch (Exception ex) { LastError = ex.Message; }
                        frames++;
                        if (now - fpsAt >= 1.0)
                        {
                            MeasuredFps = frames;
                            frames = 0;
                            fpsAt = now;
                        }
                    }
                }
                else
                {
                    double wait = next - now;
                    if (wait > 0.003) Thread.Sleep((int)(wait * 1000) - 1);
                    else Thread.SpinWait(80);
                }
            }
            try { timeEndPeriod(1); }
            catch { }
        }

        private double CurrentSpread()
        {
            if (!_config.CrosshairDynamic || !_config.CrosshairFireSpread) return 0;
            double amount = _config.CrosshairFireAmount;
            double cap = amount * Math.Max(1f, _config.CrosshairFireMaxMul);
            double now = Now;
            if (_holding)
            {
                double ramp = Math.Min(1.0, (now - _holdAt) / 0.055);
                return Math.Min(cap, amount * (1 - Math.Pow(1 - ramp, 3)));
            }
            double dur = Math.Max(0.02, _config.CrosshairFireDurationMs / 1000.0);
            double since = now - _releaseAt;
            if (since < 0 || since >= dur) return 0;
            double u = since / dur;
            double s = _spreadAtRelease;
            if (_config.CrosshairFireCurve == 1) return s * (1 - u);
            if (_config.CrosshairFireCurve == 2)
            {
                if (u < 0.35) return s;
                double v = (u - 0.35) / 0.65;
                return s * (1 - v * v);
            }
            return s * Math.Exp(-3.0 * u);
        }

        /// <summary>还没走完的圈（0..1）。每按一次就多一个，旧圈继续走自己的进度。</summary>
        private double[] ActiveRings()
        {
            if (!_config.CrosshairDynamic) return null;
            double dur = Math.Max(0.06, _config.CrosshairRingDurationMs / 1000.0);
            double now = Now;
            System.Collections.Generic.List<double> keep = new System.Collections.Generic.List<double>();
            lock (_ringLock)
            {
                for (int i = _ringQueue.Count - 1; i >= 0; i--)
                {
                    double t = (now - _ringQueue[i]) / dur;
                    if (t >= 1) { _ringQueue.RemoveAt(i); continue; }
                    keep.Add(t);
                }
            }
            return keep.Count == 0 ? null : keep.ToArray();
        }

        private double ExpandPulse()
        {
            if (!_config.CrosshairDynamic) return 0;
            double t = (Now - _expandAt) / Math.Max(0.06, _config.CrosshairExpandDurationMs / 1000.0);
            if (t < 0 || t >= 1) return 0;
            return Math.Sin(Math.PI * t) * _config.CrosshairExpandAmount;
        }

        private double HitAlpha()
        {
            if (!_config.CrosshairDynamic || !_config.CrosshairHitFlash) return 0;
            double t = (Now - _hitAt) / 0.18;
            return t >= 0 && t < 1 ? (1 - t) * 0.7 : 0;
        }

        private void RenderFrame()
        {
            double spread = CurrentSpread();
            double[] rings = ActiveRings();
            double pulse = ExpandPulse();
            double hit = HitAlpha();
            bool animating = _holding || spread > 0.02 || (rings != null && rings.Length > 0) || pulse != 0 || hit > 0;

            string sig = Signature(spread, rings, pulse, hit);
            if (!animating && sig == _lastSignature) return;
            _lastSignature = sig;

            lock (_gdiLock)
            {
                if (_gfx == null || _memDc == IntPtr.Zero) return;
                _gfx.Clear(Color.Transparent);
                PointF center = new PointF(Canvas / 2f, Canvas / 2f);
                CrosshairPaint.Draw(_gfx, _config, _scale, spread, pulse, hit, center);
                if (rings != null)
                {
                    for (int i = 0; i < rings.Length; i++)
                        CrosshairPaint.DrawRing(_gfx, _config, _scale, center, rings[i]);
                }
            }

            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                POINT dst = new POINT();
                dst.X = _left;
                dst.Y = _top;
                SIZE size = new SIZE();
                size.cx = Canvas;
                size.cy = Canvas;
                POINT src = new POINT();
                src.X = 0;
                src.Y = 0;
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = 0;             // AC_SRC_OVER
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = 1;         // AC_SRC_ALPHA
                bool ok;
                lock (_gdiLock)
                    ok = UpdateLayeredWindow(Handle, screen, ref dst, ref size, _memDc, ref src, 0, ref blend, UlwAlpha);
                if (!ok) LastError = "UpdateLayeredWindow 失败 " + Marshal.GetLastWin32Error();
                else RenderCount++;
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
        }

        private string Signature(double spread, double[] rings, double pulse, double hit)
        {
            string ringSig = rings == null || rings.Length == 0 ? "0"
                : rings.Length + ":" + rings[0].ToString("F3") + ":" + rings[rings.Length - 1].ToString("F3");
            return string.Concat(
                _left, ",", _top, "|",
                _config.CrosshairType, ",", _config.CrosshairTShape, ",",
                _config.CrosshairLength.ToString("F1"), ",",
                _config.CrosshairLineGap.ToString("F1"), ",",
                _config.CrosshairLineThickness.ToString("F1"), ",",
                _config.CrosshairDotSize.ToString("F1"), ",",
                _config.CrosshairRotation.ToString("F0"), ",",
                _config.CrosshairRingRadiusF.ToString("F0"), ",",
                _config.CrosshairOuterEnabled, ",",
                _config.CrosshairOuterLength.ToString("F1"), ",",
                _config.CrosshairOuterThickness.ToString("F1"), ",",
                _config.CrosshairOuterGap.ToString("F1"), ",",
                _config.CrosshairColor, ",", _config.CrosshairOpacity, ",",
                _config.CrosshairOutline, ",", _config.CrosshairOutlineColor, ",",
                _config.CrosshairOutlineOpacity, ",", _config.CrosshairOutlineWidth.ToString("F2"), ",",
                spread.ToString("F2"), ",", ringSig, ",",
                pulse.ToString("F2"), ",", hit.ToString("F3"));
        }

        [DllImport("winmm.dll")]
        private static extern int timeBeginPeriod(int ms);

        [DllImport("winmm.dll")]
        private static extern int timeEndPeriod(int ms);

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _running = false;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            _running = false;
            Thread t = _thread;
            _thread = null;
            if (t != null) { try { t.Join(300); } catch { } }

            lock (_gdiLock)
            {
                if (_gfx != null) { _gfx.Dispose(); _gfx = null; }
                if (_memDc != IntPtr.Zero)
                {
                    if (_oldObj != IntPtr.Zero) SelectObject(_memDc, _oldObj);
                    DeleteDC(_memDc);
                    _memDc = IntPtr.Zero;
                }
                if (_hDib != IntPtr.Zero) { DeleteObject(_hDib); _hDib = IntPtr.Zero; }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>缩圈缓动：把 0..1 的进度映射成 0..1 的收缩进度。</summary>
    internal static class CrosshairEase
    {
        public static readonly string[] Names = { "线性", "先快后慢", "先慢后快", "平滑", "回弹" };

        public static double Ring(double t, int mode)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            switch (mode)
            {
                case 0: return t;                                   // 线性
                case 1: return 1 - Math.Pow(1 - t, 3);              // 先快后慢（ease-out）
                case 2: return t * t * t;                           // 先慢后快（ease-in）
                case 4:                                             // 回弹（末端轻微过冲）
                    {
                        double c = 1.70158;
                        double u = t - 1;
                        return 1 + (c + 1) * u * u * u + c * u * u;
                    }
                default: return t * t * (3 - 2 * t);                // 平滑（smoothstep）
            }
        }
    }

    /// <summary>准星绘制（GDI+）。参数直接读 Config，改设置立即反映到下一次渲染。</summary>
    internal static class CrosshairPaint
    {
        public static void Draw(Graphics g, Config c, float scale, double spread,
                                double expandPulse, double hit, PointF center)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float k = c.CrosshairDpiScale ? Math.Max(0.5f, scale) : 1f;
            float cx = center.X;
            float cy = center.Y;

            Color main = Color.FromArgb(Percent(c.CrosshairOpacity), Color.FromArgb(c.CrosshairColor));
            Color outl = Color.FromArgb(Percent(c.CrosshairOutlineOpacity), Color.FromArgb(c.CrosshairOutlineColor));
            bool outline = c.CrosshairOutline && c.CrosshairOutlineOpacity > 0 && c.CrosshairOutlineWidth > 0.01f;

            float th = Math.Max(0.7f, c.CrosshairLineThickness * k);
            float len = Math.Max(0f, c.CrosshairLength * k);
            float gap = Math.Max(0.5f, (float)(c.CrosshairLineGap + spread + expandPulse) * k);
            float ow = c.CrosshairOutlineWidth * k;

            int type = c.CrosshairType;
            if (type == 1)
            {
                float r = (c.CrosshairRingRadiusF + (float)spread) * k;
                if (r > 1f) Ring(g, cx, cy, r, th, main, outl, outline, ow);
            }
            else if (type == 6)
            {
                Arms(g, cx, cy, gap, len, th, c.CrosshairRotation, main, outl, outline, ow, true);
                if (c.CrosshairOuterEnabled && c.CrosshairOuterLength > 0f)
                {
                    float ogap = gap + len + c.CrosshairOuterGap * k;
                    Arms(g, cx, cy, ogap, c.CrosshairOuterLength * k,
                        Math.Max(0.7f, c.CrosshairOuterThickness * k), c.CrosshairRotation,
                        main, outl, outline, ow, true);
                }
            }
            else if (type == 3) Poly(g, cx, cy, len + gap, 4, -Math.PI / 2, c.CrosshairRotation, main, outl, outline, ow);
            else if (type == 4) Poly(g, cx, cy, len + gap, 4, Math.PI / 4, c.CrosshairRotation, main, outl, outline, ow);
            else if (type == 5) Poly(g, cx, cy, len + gap, 3, -Math.PI / 2, c.CrosshairRotation, main, outl, outline, ow);
            else if (type != 2) Arms(g, cx, cy, gap, len, th, c.CrosshairRotation, main, outl, outline, ow, !c.CrosshairTShape);

            if (c.CrosshairDotSize > 0.01f)
            {
                float d = c.CrosshairDotSize * k;
                using (SolidBrush b = new SolidBrush(main))
                    g.FillEllipse(b, cx - d / 2f, cy - d / 2f, d, d);
            }


            // 命中闪光
            if (hit > 0)
            {
                using (Pen p = new Pen(Color.FromArgb((int)(255 * hit), 67, 207, 187), 2f))
                    g.DrawEllipse(p, cx - 10, cy - 10, 20, 20);
            }
        }

        /// <summary>画一个缩圈：t 是这一圈自己的进度（0..1）。</summary>
        public static void DrawRing(Graphics g, Config c, float scale, PointF center, double t)
        {
            if (t < 0 || t >= 1 || c.CrosshairRingRadius <= 0) return;
            float k = c.CrosshairDpiScale ? Math.Max(0.5f, scale) : 1f;
            Color main = Color.FromArgb(Percent(c.CrosshairOpacity), Color.FromArgb(c.CrosshairColor));
            double e = CrosshairEase.Ring(t, c.CrosshairRingEase);
            float r = c.CrosshairRingRadius * k * (float)(1 - e);
            int a = (int)(main.A * Math.Pow(1 - t, 1.25));
            if (a <= 4 || r <= 1f) return;
            using (Pen p = new Pen(Color.FromArgb(a, main), Math.Max(1f, c.CrosshairLineThickness * k)))
                g.DrawEllipse(p, center.X - r, center.Y - r, r * 2f, r * 2f);
        }

        private static int Percent(int p)
        {
            int a = (int)Math.Round(p * 2.55);
            return a < 0 ? 0 : (a > 255 ? 255 : a);
        }

        private static void Arms(Graphics g, float cx, float cy, float gap, float len, float th,
                                 float rotation, Color main, Color outl, bool outline, float ow, bool withTop)
        {
            if (len <= 0.2f) return;
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(rotation);
            float inner = Math.Max(0.6f, gap);
            float outer = inner + len;
            Line(g, inner, 0, outer, 0, th, main, outl, outline, ow);
            Line(g, 0, inner, 0, outer, th, main, outl, outline, ow);
            Line(g, -inner, 0, -outer, 0, th, main, outl, outline, ow);
            if (withTop) Line(g, 0, -inner, 0, -outer, th, main, outl, outline, ow);
            g.Restore(st);
        }

        private static void Line(Graphics g, float x1, float y1, float x2, float y2, float th,
                                 Color main, Color outl, bool outline, float ow)
        {
            if (outline)
            {
                using (Pen p = new Pen(outl, th + ow * 2f))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawLine(p, x1, y1, x2, y2);
                }
            }
            using (Pen p = new Pen(main, th))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawLine(p, x1, y1, x2, y2);
            }
        }

        private static void Ring(Graphics g, float cx, float cy, float r, float th,
                                 Color main, Color outl, bool outline, float ow)
        {
            if (outline)
            {
                using (Pen p = new Pen(outl, th + ow * 2f))
                    g.DrawEllipse(p, cx - r, cy - r, r * 2f, r * 2f);
            }
            using (Pen p = new Pen(main, th))
                g.DrawEllipse(p, cx - r, cy - r, r * 2f, r * 2f);
        }

        private static void Poly(Graphics g, float cx, float cy, float r, int sides, double start,
                                 float rotation, Color main, Color outl, bool outline, float ow)
        {
            if (r <= 0.5f) return;
            PointF[] pts = new PointF[sides];
            double rot = start + rotation * Math.PI / 180.0;
            for (int i = 0; i < sides; i++)
            {
                double a = rot + i * 2.0 * Math.PI / sides;
                pts[i] = new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r));
            }
            if (outline)
            {
                using (Pen p = new Pen(outl, ow * 2f + 1.4f))
                {
                    p.LineJoin = LineJoin.Round;
                    g.DrawPolygon(p, pts);
                }
            }
            using (Pen p = new Pen(main, 1.6f))
            {
                p.LineJoin = LineJoin.Round;
                g.DrawPolygon(p, pts);
            }
        }
    }
}














