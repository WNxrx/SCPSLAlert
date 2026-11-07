using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal sealed class BrightnessPoint
    {
        public int Luma;
        public double Gamma;
        public double Saturation = 1.07;
        public string File;
    }

    internal static class GameBrightnessProfile
    {
        /// <summary>
        /// 伽马 → 自动补偿后的饱和度。
        ///
        /// 【需求一.4】现有补偿系数 = 0.35（`1.0 + (1-γ)×0.35`）。
        /// 新公式：现有补偿系数 ×（1 + CompensationBoost）。
        /// boost 默认 0.10、范围 0–0.50，所以默认比旧版补偿略强 10%。
        /// </summary>
        public const double AutoSaturationCoefficient = 0.35;

        public static double AutoSaturation(double gamma, double boost)
        {
            if (boost < 0) boost = 0;
            if (boost > 0.50) boost = 0.50;
            double k = AutoSaturationCoefficient * (1.0 + boost);
            double sat = 1.0 + Math.Max(0, 1.0 - gamma) * k;
            return Math.Min(1.25, Math.Max(0.90, sat));
        }

        public static List<BrightnessPoint> Parse(string text)
        {
            List<BrightnessPoint> list = new List<BrightnessPoint>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            string[] rows = text.Split(';');
            foreach (string row in rows)
            {
                string[] p = row.Split(':');
                int l; double g;
                if (p.Length == 2 && int.TryParse(p[0].Trim(), out l)
                    && double.TryParse(p[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out g))
                    list.Add(new BrightnessPoint { Luma = Math.Max(0, Math.Min(255, l)), Gamma = Math.Max(0.20, Math.Min(1.6, g)) });
            }
            return list.OrderBy(x => x.Luma).ToList();
        }

        public static string Serialize(IEnumerable<BrightnessPoint> points)
        {
            return string.Join(";", points.OrderBy(x => x.Luma).Select(x => x.Luma.ToString() + ":" + x.Gamma.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        }

        public static int Count(string text) { return Parse(text).Count; }

        // Smoothstep per segment: continuous, monotone between calibration points, and curved.
        public static double Evaluate(string profile, int luma)
        {
            List<BrightnessPoint> p = Parse(profile);
            if (p.Count == 0) return 1.0;
            if (luma <= p[0].Luma) return p[0].Gamma;
            if (luma >= p[p.Count - 1].Luma) return p[p.Count - 1].Gamma;
            for (int i = 0; i < p.Count - 1; i++)
            {
                if (luma < p[i + 1].Luma)
                {
                    double t = (double)(luma - p[i].Luma) / Math.Max(1, p[i + 1].Luma - p[i].Luma);
                    t = t * t * (3.0 - 2.0 * t);
                    return p[i].Gamma + (p[i + 1].Gamma - p[i].Gamma) * t;
                }
            }
            return p[p.Count - 1].Gamma;
        }

        /// <summary>
        /// 运行时用的曲线：校准区间内沿用用户曲线，另加两个保护：
        ///  · 亮部防白屏：只要曲线在该亮度仍给出 &lt; 1 的伽马（继续提亮），
        ///    就随亮度逐渐把它拉向 1.05 —— 校准表的亮端经常是一条 0.80 的平线，
        ///    照搬会把已经很亮的画面继续往白里推；
        ///  · 比最暗样本还黑时，允许再往下压一点（最多半个区间），避免“游戏里已经
        ///    很黑但伽马停在最暗样本的值上不动”。
        /// 曲线编辑器显示的仍是用户保存的原始节点，这些保护只发生在运行时。
        /// </summary>
        public static double EvaluateForRuntime(string profile, int luma)
        {
            List<BrightnessPoint> p = Parse(profile);
            if (p.Count == 0) return 1.0;
            double gamma;
            if (luma >= p[p.Count - 1].Luma)
            {
                BrightnessPoint top = p[p.Count - 1];
                double span = Math.Max(24.0, 255.0 - top.Luma);
                double t = Math.Min(1.0, (luma - top.Luma) / span);
                t = t * t * (3.0 - 2.0 * t);
                double target = Math.Max(1.05, top.Gamma);
                gamma = top.Gamma + (target - top.Gamma) * t;
            }
            else if (luma <= p[0].Luma)
            {
                BrightnessPoint bottom = p[0];
                double t = Math.Min(1.0, (bottom.Luma - luma) / Math.Max(24.0, bottom.Luma));
                double target = Math.Max(0.20, Math.Min(bottom.Gamma, 0.62));
                gamma = bottom.Gamma + (target - bottom.Gamma) * t * 0.5;
            }
            else gamma = Evaluate(profile, luma);

            // 亮部防白屏：luma 100 以上、曲线还想提亮时，逐步收向中性偏暗。
            if (gamma < 1.05 && luma > 100)
            {
                double t = Math.Min(1.0, (luma - 100.0) / 155.0);
                t = t * t * (3.0 - 2.0 * t);
                gamma += (1.05 - gamma) * t;
            }
            return gamma;
        }
    }

    /// <summary>
    /// 亮度采样共用逻辑：控制器和校准窗口必须用同一套“区域 + 度量”，
    /// 否则校准出来的曲线和运行时对不上。
    /// 区域 = 游戏客户区整块（不是中心小方块），缩到 192 像素宽再统计。
    /// 度量 = 平均亮度占 3/4、20% 暗部分位占 1/4，暗场景里零星的灯/天空不会把读数拉高。
    /// </summary>
    internal static class BrightnessSampler
    {
        public const int SampleWidth = 192;

        /// <summary>游戏客户区在屏幕上的矩形（不含标题栏和边框）。</summary>
        public static Rectangle ClientBounds(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return Rectangle.Empty;
            try
            {
                NativeMethods.RECT cr;
                if (!NativeMethods.GetClientRect(hwnd, out cr)) return Rectangle.Empty;
                int width = cr.Right - cr.Left, height = cr.Bottom - cr.Top;
                if (width <= 0 || height <= 0) return Rectangle.Empty;
                NativeMethods.POINT origin = new NativeMethods.POINT();
                if (!NativeMethods.ClientToScreen(hwnd, ref origin)) return Rectangle.Empty;
                return new Rectangle(origin.X, origin.Y, width, height);
            }
            catch { return Rectangle.Empty; }
        }

        /// <summary>
        /// 把一个屏幕区域直接缩略到 maxWidth 宽。用 StretchBlt 而不是
        /// CopyFromScreen + 缩放，整屏采样从约 20ms 降到 2–3ms。
        /// 失败返回 null（独占全屏、UIPI、桌面切换时可能失败）。
        /// </summary>
        public static Bitmap CaptureScaled(Rectangle source, int maxWidth)
        {
            if (source.Width < 16 || source.Height < 16) return null;
            int width = Math.Max(16, Math.Min(maxWidth, source.Width));
            int height = Math.Max(9, (int)Math.Round(width * (double)source.Height / source.Width));
            IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return null;
            IntPtr mem = IntPtr.Zero, hbmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                mem = NativeMethods.CreateCompatibleDC(screen);
                if (mem == IntPtr.Zero) return null;
                hbmp = NativeMethods.CreateCompatibleBitmap(screen, width, height);
                if (hbmp == IntPtr.Zero) return null;
                old = NativeMethods.SelectObject(mem, hbmp);
                NativeMethods.SetStretchBltMode(mem, NativeMethods.HALFTONE);
                if (!NativeMethods.StretchBlt(mem, 0, 0, width, height, screen,
                        source.Left, source.Top, source.Width, source.Height, NativeMethods.SRCCOPY))
                    return null;
                Bitmap result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(result))
                {
                    IntPtr dest = g.GetHdc();
                    try { NativeMethods.BitBlt(dest, 0, 0, width, height, mem, 0, 0, NativeMethods.SRCCOPY); }
                    finally { g.ReleaseHdc(dest); }
                }
                return result;
            }
            catch { return null; }
            finally
            {
                if (mem != IntPtr.Zero && old != IntPtr.Zero) NativeMethods.SelectObject(mem, old);
                if (hbmp != IntPtr.Zero) NativeMethods.DeleteObject(hbmp);
                if (mem != IntPtr.Zero) NativeMethods.DeleteDC(mem);
                NativeMethods.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        /// <summary>0–255 的场景亮度。对 24bpp 图做直方图，一次遍历得出均值和 20% 分位。</summary>
        public static unsafe int Measure(Bitmap b)
        {
            if (b == null) return 0;
            try
            {
                BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int[] hist = new int[256];
                    long sum = 0; int count = 0;
                    byte* p = (byte*)d.Scan0.ToPointer();
                    for (int y = 0; y < b.Height; y++)
                    {
                        byte* row = p + y * d.Stride;
                        for (int x = 0; x < b.Width; x++)
                        {
                            byte* q = row + x * 3;
                            int v = (299 * q[2] + 587 * q[1] + 114 * q[0]) / 1000;
                            hist[v]++; sum += v; count++;
                        }
                    }
                    int mean = (int)(sum / Math.Max(1, count));
                    int target = Math.Max(1, count / 5), acc = 0, p20 = mean;
                    for (int i = 0; i < 256; i++)
                    {
                        acc += hist[i];
                        if (acc >= target) { p20 = i; break; }
                    }
                    return Math.Max(0, Math.Min(255, (mean * 3 + p20) / 4));
                }
                finally { b.UnlockBits(d); }
            }
            catch { return 0; }
        }
    }

    /// <summary>按游戏前台画面亮度套用校准曲线；gamma ramp 退出前会还原。</summary>
    internal sealed class GameBrightnessController : IDisposable
    {
        private readonly Config _cfg;
        private readonly System.Threading.Timer _timer;
        private readonly System.Threading.Timer _fadeTimer;
        private readonly object _gate = new object();
        private IntPtr _dc;
        private bool _dcFromGetDc;
        private ushort[] _original;
        private ushort[] _currentRamp;
        private ushort[] _fadeStartRamp;
        private ushort[] _fadeTargetRamp;
        private DateTime _fadeStarted;
        private int _fadeDurationMs = 360;
        private bool _restoring;
        private DateTime _lastProbe = DateTime.MinValue;
        private DateTime _lastAdjust = DateTime.MinValue;
        private int _lastGoodLuma = 128;
        private int _sampleFailures;
        private double _appliedGamma = double.NaN;
        private double _appliedSaturation = double.NaN;
        private bool _disposed;
        private volatile bool _suspended;

        // 采样周期。提亮（画面变亮 -> 需要压低伽马防止白屏）走快速通道，
        // 压暗（画面变黑 -> 抬伽马）才按用户配置的检测间隔节流。
        private const int ProbeIntervalMs = 150;
        private const int FastAdjustCooldownMs = 80;

        public GameBrightnessController(Config cfg)
        {
            _cfg = cfg;
            _timer = new System.Threading.Timer(Tick, null, 400, ProbeIntervalMs);
            _fadeTimer = new System.Threading.Timer(FadeTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void SetSuspended(bool suspended)
        {
            _suspended = suspended;
            if (suspended) Restore();
        }

        private void Tick(object state)
        {
            if (_disposed || _suspended) { Restore(); return; }
            lock (_gate) try
            {
                if (_disposed || _suspended) { Restore(); return; }
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                bool gameForeground = IsGameWindow(hwnd);
                bool forceGlobal = _cfg.GameBrightnessForceGlobal;
                if (!gameForeground && !forceGlobal) { Restore(); return; }
                bool hold = _cfg.GameBrightnessHoldEnabled;
                if (!_cfg.DisplayAssistEnabled || !_cfg.GameBrightnessEnabled || (!hold && GameBrightnessProfile.Count(_cfg.GameBrightnessProfile) < 2)) { Restore(); return; }

                DateTime now = DateTime.UtcNow;
                if (!hold && (now - _lastProbe).TotalMilliseconds >= ProbeIntervalMs)
                {
                    _lastProbe = now;
                    int measured;
                    if (TrySample(hwnd, !gameForeground && forceGlobal, out measured) && measured > 3)
                    {
                        _lastGoodLuma = measured;
                        _sampleFailures = 0;
                    }
                    else if (_sampleFailures < 1000) _sampleFailures++;
                    // 截图失败/全黑（独占全屏、UAC 弹窗、切桌面）时沿用上一次有效亮度并保留
                    // 当前伽马，不要像以前那样 Restore() —— 那会让画面突然变回刺眼的原色。
                }
                int luma = _lastGoodLuma;

                if (_original == null && !BeginSession(gameForeground ? hwnd : IntPtr.Zero)) return;
                double gamma, saturation;
                if (hold)
                {
                    gamma = Math.Max(0.20, Math.Min(1.6, _cfg.GameBrightnessHoldGamma / 100.0));
                    saturation = Math.Max(0.20, Math.Min(1.6, _cfg.GameBrightnessHoldSaturation / 100.0));
                }
                else
                {
                    gamma = GameBrightnessProfile.EvaluateForRuntime(_cfg.GameBrightnessProfile, luma);
                    saturation = GameBrightnessProfile.Count(_cfg.GameBrightnessSaturationProfile) >= 2
                        ? GameBrightnessProfile.Evaluate(_cfg.GameBrightnessSaturationProfile, luma) : 1.0;
                }

                // 目标伽马比当前小 = 画面变亮了，需要更快压暗，否则高光会先白一阵。
                bool screenGotBrighter = !double.IsNaN(_appliedGamma) && gamma < _appliedGamma - 0.004;
                double threshold = screenGotBrighter ? 0.004 : 0.009;
                if (!double.IsNaN(_appliedGamma) &&
                    Math.Abs(gamma - _appliedGamma) < threshold &&
                    !double.IsNaN(_appliedSaturation) && Math.Abs(saturation - _appliedSaturation) < 0.015) return;

                int cooldown = screenGotBrighter
                    ? FastAdjustCooldownMs
                    : Math.Min(650, Math.Max(150, _cfg.GameBrightnessIntervalMs));
                if ((now - _lastAdjust).TotalMilliseconds < cooldown) return;
                ushort[] ramp = BuildRamp(_original, gamma, saturation);
                if (_dc != IntPtr.Zero && StartTransition(ramp, screenGotBrighter ? 90 : 300))
                {
                    _lastAdjust = DateTime.UtcNow;
                    _appliedGamma = gamma;
                    _appliedSaturation = saturation;
                }
            }
            catch { Restore(); }
        }

        private bool BeginSession(IntPtr hwnd)
        {
            try
            {
                string device = hwnd == IntPtr.Zero ? Screen.PrimaryScreen.DeviceName : Screen.FromHandle(hwnd).DeviceName;
                _dc = NativeMethods.CreateDC("DISPLAY", device, null, IntPtr.Zero);
                if (_dc == IntPtr.Zero) { _dc = NativeMethods.GetDC(IntPtr.Zero); _dcFromGetDc = _dc != IntPtr.Zero; }
                if (_dc == IntPtr.Zero) return false;
                _original = new ushort[768];
                if (!NativeMethods.GetDeviceGammaRamp(_dc, _original)) { CloseDc(); _original = null; return false; }
                _currentRamp = (ushort[])_original.Clone();
                return true;
            }
            catch { CloseDc(); _original = null; return false; }
        }

        private void Restore()
        {
            lock (_gate)
            {
                if (_original == null || _dc == IntPtr.Zero) return;
                if (_restoring && RampsEqual(_fadeTargetRamp, _original)) return;
                if (_currentRamp == null || RampsEqual(_currentRamp, _original))
                {
                    FinishRestore();
                    return;
                }
                _appliedGamma = double.NaN;
                StartTransition(_original, 260);
                _restoring = true;
            }
        }

        private void RestoreImmediately()
        {
            lock (_gate)
            {
                try { _fadeTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                if (_original != null && _dc != IntPtr.Zero)
                {
                    try { NativeMethods.SetDeviceGammaRamp(_dc, _original); } catch { }
                }
                FinishRestore();
            }
        }

        private void FinishRestore()
        {
            try { _fadeTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            _original = null;
            _currentRamp = null;
            _fadeStartRamp = null;
            _fadeTargetRamp = null;
            _restoring = false;
            CloseDc();
            _lastAdjust = DateTime.MinValue;
            _appliedGamma = double.NaN;
            _appliedSaturation = double.NaN;
        }

        private static bool RampsEqual(ushort[] a, ushort[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private bool StartTransition(ushort[] target)
        {
            return StartTransition(target, 300);
        }

        private bool StartTransition(ushort[] target, int durationMs)
        {
            if (_currentRamp == null || target == null || _dc == IntPtr.Zero) return false;
            _fadeTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _fadeStartRamp = (ushort[])_currentRamp.Clone();
            _fadeTargetRamp = target;
            _fadeDurationMs = Math.Max(40, durationMs);
            _restoring = false;
            _fadeStarted = DateTime.UtcNow;
            _fadeTimer.Change(0, 16);
            return true;
        }

        private void FadeTick(object state)
        {
            lock (_gate)
            {
                if (_disposed || _dc == IntPtr.Zero || _fadeTargetRamp == null) return;
                double t = (DateTime.UtcNow - _fadeStarted).TotalMilliseconds / _fadeDurationMs;
                if (t >= 1.0) t = 1.0;
                double eased = t * t * (3.0 - 2.0 * t);
                ushort[] ramp = new ushort[768];
                for (int i = 0; i < ramp.Length; i++)
                    ramp[i] = (ushort)Math.Max(0, Math.Min(65535, Math.Round(_fadeStartRamp[i] + (_fadeTargetRamp[i] - _fadeStartRamp[i]) * eased)));
                if (!NativeMethods.SetDeviceGammaRamp(_dc, ramp))
                {
                    _fadeTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    _appliedGamma = double.NaN;
                    if (_restoring) FinishRestore(); else _fadeTargetRamp = null;
                    return;
                }
                _currentRamp = ramp;
                if (t >= 1.0)
                {
                    if (_restoring) FinishRestore();
                    else
                    {
                        _fadeTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        _fadeTargetRamp = null;
                        _fadeStartRamp = null;
                    }
                }
            }
        }

        private void CloseDc()
        {
            if (_dc == IntPtr.Zero) return;
            try { if (_dcFromGetDc) NativeMethods.ReleaseDC(IntPtr.Zero, _dc); else NativeMethods.DeleteDC(_dc); } catch { }
            _dcFromGetDc = false;
            _dc = IntPtr.Zero;
        }

        private static ushort[] BuildRamp(ushort[] baseline, double exponent, double saturation)
        {
            ushort[] r = new ushort[768];
            exponent = Math.Max(0.20, Math.Min(1.6, exponent));
            saturation = Math.Max(0.20, Math.Min(1.6, saturation));
            for (int c = 0; c < 3; c++) for (int i = 0; i < 256; i++)
            {
                double v = baseline[c * 256 + i] / 65535.0;
                double corrected = Math.Pow(v, exponent);
                corrected = 0.5 + (corrected - 0.5) * saturation;
                r[c * 256 + i] = (ushort)Math.Max(0, Math.Min(65535, Math.Round(corrected * 65535.0)));
            }
            return r;
        }

        private static IntPtr _lastGameHwnd;
        private static bool _lastGameResult;
        private static DateTime _lastGameCheck = DateTime.MinValue;

        private static bool IsGameWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            DateTime now = DateTime.UtcNow;
            if (hwnd == _lastGameHwnd && (now - _lastGameCheck).TotalMilliseconds < 2000) return _lastGameResult;
            bool result = false;
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            if (pid != 0)
            {
                try
                {
                    using (Process p = Process.GetProcessById((int)pid))
                    {
                        string n = p.ProcessName;
                        result = !n.StartsWith("SCPSLAlert", StringComparison.OrdinalIgnoreCase) &&
                            (n.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("SCP Secret Laboratory", StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                }
                catch { result = false; }
            }
            _lastGameHwnd = hwnd;
            _lastGameResult = result;
            _lastGameCheck = now;
            return result;
        }

        private static bool TrySample(IntPtr hwnd, bool fullDesktop, out int luma)
        {
            luma = 0;
            try
            {
                Rectangle r = fullDesktop ? SystemInformation.VirtualScreen : BrightnessSampler.ClientBounds(hwnd);
                if (r.Width < 320 || r.Height < 180) return false;
                using (Bitmap b = BrightnessSampler.CaptureScaled(r, BrightnessSampler.SampleWidth))
                {
                    if (b == null) return false;
                    luma = BrightnessSampler.Measure(b);
                    return true;
                }
            }
            catch { return false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            _fadeTimer.Dispose();
            RestoreImmediately();
        }
    }

    internal sealed class GameBrightnessAssistantForm : Form
    {
        private readonly Config _cfg;
        private readonly Action _save;
        private readonly Action _closed;
        private readonly List<BrightnessPoint> _points = new List<BrightnessPoint>();
        private readonly ListBox _list = new ListBox();
        private readonly PictureBox _before = new PictureBox();
        private readonly PictureBox _after = new PictureBox();
        private readonly TrackBar _gamma = new TrackBar();
        private readonly TrackBar _saturation = new TrackBar();
        private readonly CheckBox _autoSaturation = new CheckBox();
        private readonly FlowLayoutPanel _gallery = new FlowLayoutPanel();
        private readonly Label _value = new Label();
        private readonly Label _status = new Label();
        private readonly Button _continueCapture = new Button();
        private readonly System.Windows.Forms.Timer _captureTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _previewTimer = new System.Windows.Forms.Timer();
        private readonly Random _random = new Random();
        private Bitmap _previewSource;
        private string _galleryKey;
        private DateTime _nextCapture;
        private int _captureCount;
        private DateTime _captureStarted;
        private byte[] _previousSignature;
        private bool _syncingSliders;
        private string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCPSLAlert", "GameBrightness"); } }

        public GameBrightnessAssistantForm(Config cfg, Action save, Action closed)
        {
            _cfg = cfg; _save = save; _closed = closed;
            Text = "游戏亮度校准"; StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(1240, 820); MinimumSize = new Size(1020, 680);
            BackColor = Color.FromArgb(28, 31, 39); ForeColor = Color.White;
            BuildUi(); LoadExisting();
            _captureTimer.Interval = 400; _captureTimer.Tick += CaptureTick;
            _previewTimer.Interval = 40; _previewTimer.Tick += delegate { _previewTimer.Stop(); UpdatePreviewNow(); };
            FormClosed += delegate { _captureTimer.Stop(); _previewTimer.Stop(); if (_closed != null) _closed(); };
            FormClosed += delegate
            {
                if (_before.Image != null) { _before.Image.Dispose(); _before.Image = null; }
                if (_after.Image != null) { _after.Image.Dispose(); _after.Image = null; }
                if (_previewSource != null) { _previewSource.Dispose(); _previewSource = null; }
                foreach (Control c in _gallery.Controls) { PictureBox pb = c as PictureBox; if (pb != null && pb.Image != null) { pb.Image.Dispose(); pb.Image = null; } }
            };
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(16), BackColor = BackColor };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(root);
            FlowLayoutPanel left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            root.Controls.Add(left, 0, 0);
            Label title = new Label { Text = "均匀分布的亮度样本（7 张）", AutoSize = true, Font = new Font("微软雅黑", 13, FontStyle.Bold), Margin = new Padding(4, 4, 4, 12) }; left.Controls.Add(title);
            _list.Width = 230; _list.Height = 300; _list.BackColor = Color.FromArgb(39, 43, 53); _list.ForeColor = Color.White; _list.SelectedIndexChanged += SelectionChanged; left.Controls.Add(_list);
            Button collect = new Button { Text = "重新采集样本", Width = 230, Height = 36 }; collect.Click += StartCapture; left.Controls.Add(collect);
            _continueCapture.Text = "继续自动补采 / 均衡"; _continueCapture.Width = 230; _continueCapture.Height = 34; _continueCapture.Enabled = false; _continueCapture.Click += ContinueCapture; left.Controls.Add(_continueCapture);
            Button open = new Button { Text = "打开样本文件夹", Width = 230, Height = 32 }; open.Click += delegate { try { Directory.CreateDirectory(Folder); Process.Start("explorer.exe", Folder); } catch { } }; left.Controls.Add(open);
            _status.AutoSize = true; _status.MaximumSize = new Size(232, 0); _status.Text = "采样区域是游戏客户区整块画面（不是中心小块），并按暗部亮度加权；程序持续采集并挑选 7 个间隔较均匀的亮度点。"; left.Controls.Add(_status);

            TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = BackColor, Padding = new Padding(8, 0, 0, 0) };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 112)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 170)); root.Controls.Add(right, 1, 0);
            right.Controls.Add(new Label { Text = "原始画面", AutoSize = true }, 0, 0); right.Controls.Add(new Label { Text = "伽马 + 饱和度预览", AutoSize = true }, 1, 0);
            _gallery.Dock = DockStyle.Fill; _gallery.WrapContents = false; _gallery.AutoScroll = true; _gallery.FlowDirection = FlowDirection.LeftToRight; _gallery.Padding = new Padding(2); right.Controls.Add(_gallery, 0, 1); right.SetColumnSpan(_gallery, 2);
            _before.Dock = DockStyle.Fill; _before.SizeMode = PictureBoxSizeMode.Zoom; _before.BackColor = Color.Black; right.Controls.Add(_before, 0, 2);
            _after.Dock = DockStyle.Fill; _after.SizeMode = PictureBoxSizeMode.Zoom; _after.BackColor = Color.Black; right.Controls.Add(_after, 1, 2);
            FlowLayoutPanel controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            _gamma.Minimum = 20; _gamma.Maximum = 160; _gamma.TickFrequency = 10; _gamma.Width = 620; _gamma.ValueChanged += GammaChanged; controls.Controls.Add(_gamma);
            _saturation.Minimum = 20; _saturation.Maximum = 160; _saturation.TickFrequency = 10; _saturation.Width = 620; _saturation.ValueChanged += SaturationChanged; controls.Controls.Add(_saturation);
            _autoSaturation.Text = "调伽马时自动补偿色彩变淡（可再手动微调）"; _autoSaturation.AutoSize = true; _autoSaturation.Checked = true; controls.Controls.Add(_autoSaturation);
            _value.AutoSize = true; controls.Controls.Add(_value);
            controls.Controls.Add(new Label { Text = "说明：滑杆只改右侧预览图和保存到曲线的数值；关闭本窗口回到游戏后才会实时生效。", AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Color.FromArgb(168, 178, 194) });
            Button curve = new Button { Text = "打开手动曲线编辑器…", Width = 190, Height = 32 }; curve.Click += OpenCurveEditor; controls.Controls.Add(curve);
            Button save = new Button { Text = "保存校准", Width = 120, Height = 34 }; save.Click += SaveProfile; controls.Controls.Add(save);
            right.Controls.Add(controls, 0, 3); right.SetColumnSpan(controls, 2);
        }

        private void LoadExisting()
        {
            List<BrightnessPoint> saved = GameBrightnessProfile.Parse(_cfg.GameBrightnessProfile);
            List<BrightnessPoint> savedSat = GameBrightnessProfile.Parse(_cfg.GameBrightnessSaturationProfile);
            int maxSamples = 7;
            for (int i = 0; i < maxSamples; i++)
            {
                string f = Path.Combine(Folder, "sample" + i + ".png");
                if (File.Exists(f))
                {
                    int luma = ImageLuma(f);
                    BrightnessPoint nearest = saved.OrderBy(x => Math.Abs(x.Luma - luma)).FirstOrDefault();
                    BrightnessPoint nearestSat = savedSat.OrderBy(x => Math.Abs(x.Luma - luma)).FirstOrDefault();
                    _points.Add(new BrightnessPoint { Luma = luma, Gamma = nearest == null ? 0.8 : nearest.Gamma, Saturation = nearestSat == null ? 1.07 : nearestSat.Gamma, File = f });
                }
            }
            if (_points.Count == 0 && saved.Count > 0)
            {
                foreach (BrightnessPoint p in saved)
                {
                    BrightnessPoint nearest = savedSat.OrderBy(x => Math.Abs(x.Luma - p.Luma)).FirstOrDefault();
                    _points.Add(new BrightnessPoint { Luma = p.Luma, Gamma = p.Gamma, Saturation = nearest == null ? 1.07 : nearest.Gamma });
                }
            }
            // 第一次使用、还没有任何样本时也给一条可拖动的默认曲线，
            // 否则列表为空、滑杆怎么拖都没反应（看起来像界面坏了）。
            if (_points.Count == 0)
            {
                for (int i = 0; i < 5; i++) _points.Add(new BrightnessPoint { Luma = i * 255 / 4, Gamma = 0.86, Saturation = 1.05 });
                _status.Text = "还没有样本，当前是默认曲线（可以先拖动滑杆，或点“重新采集样本”）。";
            }
            RefreshList(true);
            _continueCapture.Enabled = _points.Count >= 3;
            if (_points.Count > 0) _list.SelectedIndex = 0;
        }

        private void StartCapture(object sender, EventArgs e)
        {
            _points.Clear(); _list.Items.Clear(); _captureCount = 0; _captureStarted = DateTime.UtcNow;
            _previousSignature = null;
            ClearPreviewImages();
            Directory.CreateDirectory(Folder);
            for (int i = 0; i < 7; i++)
            {
                try { string oldSample = Path.Combine(Folder, "sample" + i + ".png"); if (File.Exists(oldSample)) File.Delete(oldSample); } catch { }
            }
            Hide();
            _continueCapture.Enabled = false;
            _status.Text = "请回到游戏正常游玩。程序会持续寻找覆盖更均匀的 7 个亮度点；到时限后返回调整。";
            _nextCapture = DateTime.UtcNow.AddSeconds(_random.Next(2, 6)); _captureTimer.Start();
            MessageBox.Show("设置窗口已隐藏。请切回 SCP:SL 正常游玩。程序会持续采集并挑选 7 个亮度分布尽量均匀的样本；发现更暗、更亮或能填补亮度间隔的画面时会更新样本。", "亮度样本采集", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void BeginAutoCapture()
        {
            StartCapture(this, EventArgs.Empty);
        }

        private void CaptureTick(object sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _captureStarted).TotalSeconds > 90)
            {
                _captureTimer.Stop(); Show(); Activate();
                _continueCapture.Enabled = _points.Count >= 3;
                RefreshList(true);
                if (_points.Count > 0) _list.SelectedIndex = 0;
                _status.Text = "本轮结束：" + _points.Count + " / 7 张。" + SampleRangeText() + (_points.Count < 7 ? "可以继续补采或先调整已有样本。" : "可调整曲线；也可以继续补采以扩展亮度范围、改善分布。");
                return;
            }
            if (DateTime.UtcNow < _nextCapture) return;
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (!IsGame(hwnd)) { _nextCapture = DateTime.UtcNow.AddSeconds(1); return; }
            byte[] signature = null;
            if (_cfg.GameBrightnessCaptureMotionOnly)
            {
                int motionLuma;
                if (!CaptureMotionSignature(hwnd, out motionLuma, out signature))
                { _nextCapture = DateTime.UtcNow.AddSeconds(1); return; }
                if (motionLuma <= 3) _previousSignature = signature;
                else if (_previousSignature == null)
                {
                    _previousSignature = signature;
                    _status.Text = "正在等画面出现明显变化，再收取样本…";
                    _nextCapture = DateTime.UtcNow.AddSeconds(_random.Next(2, 6)); return;
                }
                else
                {
                    double change = SignatureChange(_previousSignature, signature);
                    _previousSignature = signature;
                    if (change < 8.0)
                    {
                        _status.Text = "画面变化还不明显，继续等待…";
                        _nextCapture = DateTime.UtcNow.AddSeconds(_random.Next(2, 6)); return;
                    }
                }
            }
            Bitmap b; int luma;
            if (!CaptureSample(hwnd, out b, out luma)) { _nextCapture = DateTime.UtcNow.AddSeconds(1); return; }
            if (_points.Any(p => Math.Abs(p.Luma - luma) < 2)) { b.Dispose(); _nextCapture = DateTime.UtcNow.AddSeconds(_random.Next(2, 6)); return; }
            BrightnessPoint candidate = new BrightnessPoint { Luma = luma, Gamma = 0.8, Saturation = 1.07 };
            if (_points.Count < 7)
            {
                candidate.File = Path.Combine(Folder, "sample" + _captureCount + ".png");
                using (b) b.Save(candidate.File, ImageFormat.Png);
                _points.Add(candidate); _captureCount++;
            }
            else
            {
                List<BrightnessPoint> pool = new List<BrightnessPoint>(_points);
                pool.Add(candidate);
                List<BrightnessPoint> chosen = ChooseEvenlySpaced(pool, 7);
                if (!chosen.Contains(candidate))
                {
                    b.Dispose(); _nextCapture = DateTime.UtcNow.AddSeconds(_random.Next(2, 6)); return;
                }
                BrightnessPoint replace = _points.FirstOrDefault(p => !chosen.Contains(p));
                if (replace == null) { b.Dispose(); _nextCapture = DateTime.UtcNow.AddSeconds(_random.Next(2, 6)); return; }
                candidate.File = replace.File;
                using (b) b.Save(candidate.File, ImageFormat.Png);
                _points.Remove(replace); _points.Add(candidate);
            }
            RefreshList();
            if (Visible)
                _list.SelectedIndex = _points.OrderBy(x => x.Luma).ToList().IndexOf(candidate);
            _status.Text = "正在均衡采集（" + _points.Count + " / 7）。" + SampleRangeText();
            _nextCapture = DateTime.UtcNow.AddSeconds(_random.Next(2, 6));
        }

        private void ContinueCapture(object sender, EventArgs e)
        {
            if (_points.Count < 3) return;
            _captureCount = _points.Count;
            _captureStarted = DateTime.UtcNow;
            _nextCapture = DateTime.UtcNow.AddSeconds(_random.Next(1, 3));
            ClearPreviewImages();
            _continueCapture.Enabled = false;
            _status.Text = "继续观察新画面；超出当前范围或能改善亮度分布的样本会替换较近的点。";
            Hide(); _captureTimer.Start();
            MessageBox.Show("请切回 SCP:SL 正常游玩。程序会继续寻找更暗、更亮或能填补最大亮度间隔的画面，并更新 7 个样本。", "继续采集", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearPreviewImages()
        {
            _previewTimer.Stop();
            Image oldB = _before.Image; _before.Image = null; if (oldB != null) oldB.Dispose();
            Image oldA = _after.Image; _after.Image = null; if (oldA != null) oldA.Dispose();
            if (_previewSource != null) { _previewSource.Dispose(); _previewSource = null; }
        }

        private static List<BrightnessPoint> ChooseEvenlySpaced(List<BrightnessPoint> points, int count)
        {
            List<BrightnessPoint> sorted = points.OrderBy(p => p.Luma).ToList();
            List<BrightnessPoint> selected = new List<BrightnessPoint>();
            if (sorted.Count <= count) return sorted;
            selected.Add(sorted[0]);
            if (count > 1) selected.Add(sorted[sorted.Count - 1]);
            while (selected.Count < count)
            {
                BrightnessPoint best = null; int bestDistance = -1;
                foreach (BrightnessPoint p in sorted)
                {
                    if (selected.Contains(p)) continue;
                    int nearest = int.MaxValue;
                    foreach (BrightnessPoint s in selected) nearest = Math.Min(nearest, Math.Abs(p.Luma - s.Luma));
                    if (nearest > bestDistance) { bestDistance = nearest; best = p; }
                }
                if (best == null) break;
                selected.Add(best);
            }
            return selected;
        }

        private string SampleRangeText()
        {
            if (_points.Count == 0) return "";
            List<BrightnessPoint> p = _points.OrderBy(x => x.Luma).ToList();
            int largestGap = 0;
            for (int i = 1; i < p.Count; i++) largestGap = Math.Max(largestGap, p[i].Luma - p[i - 1].Luma);
            return "亮度范围 " + p[0].Luma + "–" + p[p.Count - 1].Luma + "，最大间隔 " + largestGap + "。";
        }

        private static bool IsGame(IntPtr hwnd)
        {
            uint pid; if (hwnd == IntPtr.Zero) return false; NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            try { using (Process p = Process.GetProcessById((int)pid)) return !p.ProcessName.StartsWith("SCPSLAlert", StringComparison.OrdinalIgnoreCase) && (p.ProcessName.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0 || p.ProcessName.IndexOf("SCP Secret Laboratory", StringComparison.OrdinalIgnoreCase) >= 0); } catch { return false; }
        }

        private static bool CaptureSample(IntPtr hwnd, out Bitmap bmp, out int luma)
        {
            bmp = null; luma = 0;
            try
            {
                Rectangle r = BrightnessSampler.ClientBounds(hwnd);
                if (r.Width < 640 || r.Height < 360) return false;
                bmp = BrightnessSampler.CaptureScaled(r, 720);
                if (bmp == null) return false;
                luma = BrightnessSampler.Measure(bmp);
                return true;
            }
            catch
            {
                if (bmp != null) { bmp.Dispose(); bmp = null; }
                return false;
            }
        }

        private static bool CaptureMotionSignature(IntPtr hwnd, out int luma, out byte[] signature)
        {
            luma = 0; signature = null;
            try
            {
                Rectangle r = BrightnessSampler.ClientBounds(hwnd);
                if (r.Width < 320 || r.Height < 180) return false;
                using (Bitmap sample = BrightnessSampler.CaptureScaled(r, 320))
                {
                    if (sample == null) return false;
                    luma = BrightnessSampler.Measure(sample);
                    signature = MakeSignature(sample);
                    return true;
                }
            }
            catch { return false; }
        }

        private static unsafe byte[] MakeSignature(Bitmap b)
        {
            int step = Math.Max(2, Math.Min(b.Width / 80, b.Height / 45));
            int sw = (b.Width + step - 1) / step, sh = (b.Height + step - 1) / step;
            byte[] result = new byte[sw * sh];
            BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                byte* p = (byte*)d.Scan0.ToPointer(); int n = 0;
                for (int y = 0; y < b.Height; y += step) for (int x = 0; x < b.Width; x += step)
                { byte* q = p + y * d.Stride + x * 3; result[n++] = (byte)((299 * q[2] + 587 * q[1] + 114 * q[0]) / 1000); }
                return result;
            }
            finally { b.UnlockBits(d); }
        }

        private static double SignatureChange(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 255.0;
            long sum = 0;
            for (int i = 0; i < a.Length; i++) sum += Math.Abs((int)a[i] - (int)b[i]);
            return (double)sum / a.Length;
        }

        private void RefreshList(bool forceGallery = false)
        {
            _list.Items.Clear();
            foreach (BrightnessPoint p in _points.OrderBy(x => x.Luma)) _list.Items.Add("亮度 " + p.Luma + "  ·  γ " + p.Gamma.ToString("0.00") + "  ·  色彩 " + p.Saturation.ToString("0.00"));
            RefreshGallery(forceGallery);
        }

        private void RefreshGallery(bool force = false)
        {
            List<BrightnessPoint> ordered = _points.OrderBy(x => x.Luma).ToList();
            string key = string.Join("|", ordered.Select(p => (p.File ?? "-") + "@" + p.Luma).ToArray());
            // 采集时窗口是隐藏的：画廊缩略图没必要每收一张就重建一遍，等回到窗口再统一刷新。
            if (!force && (!Visible || key == _galleryKey)) { if (!Visible) _galleryKey = null; return; }
            _galleryKey = key;
            _gallery.SuspendLayout();
            try
            {
                foreach (Control c in _gallery.Controls)
                {
                    PictureBox pb = c as PictureBox;
                    if (pb != null && pb.Image != null) pb.Image.Dispose();
                    c.Dispose();
                }
                _gallery.Controls.Clear();
                for (int i = 0; i < ordered.Count; i++)
                {
                    BrightnessPoint point = ordered[i];
                    PictureBox pb = new PictureBox { Width = 118, Height = 82, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(12, 14, 18), BorderStyle = BorderStyle.FixedSingle, Tag = i, Cursor = Cursors.Hand, Margin = new Padding(3) };
                    if (!string.IsNullOrEmpty(point.File) && File.Exists(point.File))
                    {
                        try
                        {
                            using (Bitmap source = new Bitmap(point.File))
                                pb.Image = Downscale(source, 236);
                        }
                        catch { }
                    }
                    pb.Click += delegate(object sender, EventArgs e) { int index = (int)((Control)sender).Tag; if (index >= 0 && index < _list.Items.Count) _list.SelectedIndex = index; };
                    _gallery.Controls.Add(pb);
                }
            }
            finally { _gallery.ResumeLayout(); }
        }

        private static Bitmap Downscale(Bitmap source, int maxWidth)
        {
            int width = Math.Max(1, Math.Min(maxWidth, source.Width));
            int height = Math.Max(1, (int)Math.Round(source.Height * (double)width / source.Width));
            Bitmap result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, 0, 0, width, height);
            }
            return result;
        }

        private void SelectionChanged(object sender, EventArgs e)
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _points.Count) { ClearPreviewImages(); return; }
            BrightnessPoint p = _points.OrderBy(x => x.Luma).ElementAt(i);
            LoadBeforeImage(p.File);
            _syncingSliders = true;
            _gamma.Value = Math.Max(_gamma.Minimum, Math.Min(_gamma.Maximum, (int)Math.Round(p.Gamma * 100)));
            _saturation.Value = Math.Max(_saturation.Minimum, Math.Min(_saturation.Maximum, (int)Math.Round(p.Saturation * 100)));
            _syncingSliders = false;
            UpdatePreviewNow();
        }

        private void LoadBeforeImage(string file)
        {
            _previewTimer.Stop();
            Image oldBefore = _before.Image; _before.Image = null; if (oldBefore != null) oldBefore.Dispose();
            if (_previewSource != null) { _previewSource.Dispose(); _previewSource = null; }
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
            try
            {
                using (Bitmap full = new Bitmap(file))
                {
                    _previewSource = Downscale(full, 720);
                    _before.Image = new Bitmap(_previewSource);
                }
            }
            catch { }
        }

        private void GammaChanged(object sender, EventArgs e)
        {
            int i = _list.SelectedIndex; if (i < 0 || _syncingSliders) return;
            BrightnessPoint p = _points.OrderBy(x => x.Luma).ElementAt(i); p.Gamma = _gamma.Value / 100.0;
            if (_autoSaturation.Checked) p.Saturation = GameBrightnessProfile.AutoSaturation(p.Gamma, _cfg.GameBrightnessCompensationBoost);
            _syncingSliders = true; _saturation.Value = Math.Max(_saturation.Minimum, Math.Min(_saturation.Maximum, (int)Math.Round(p.Saturation * 100))); _syncingSliders = false;
            if (i < _list.Items.Count) _list.Items[i] = "亮度 " + p.Luma + "  ·  γ " + p.Gamma.ToString("0.00") + "  ·  色彩 " + p.Saturation.ToString("0.00");
            UpdatePreview();
        }

        private void SaturationChanged(object sender, EventArgs e)
        {
            int i = _list.SelectedIndex; if (i < 0 || _syncingSliders) return;
            BrightnessPoint p = _points.OrderBy(x => x.Luma).ElementAt(i); p.Saturation = _saturation.Value / 100.0;
            if (i < _list.Items.Count) _list.Items[i] = "亮度 " + p.Luma + "  ·  γ " + p.Gamma.ToString("0.00") + "  ·  色彩 " + p.Saturation.ToString("0.00");
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            // 拖动滑杆时不要每一格都跑一遍逐像素幂运算：先更新文字，
            // 停手 40ms 后再用降采样后的图（≤720 宽）算一次预览。
            if (_previewSource == null) return;
            int i = _list.SelectedIndex; if (i < 0 || i >= _points.Count) return;
            BrightnessPoint p = _points.OrderBy(x => x.Luma).ElementAt(i);
            _value.Text = "伽马 " + p.Gamma.ToString("0.00") + "（<1 提亮）  ·  饱和度近似 " + p.Saturation.ToString("0.00") + "（1.00 中性）";
            _previewTimer.Stop(); _previewTimer.Start();
        }

        private void UpdatePreviewNow()
        {
            if (_previewSource == null) return;
            int i = _list.SelectedIndex; if (i < 0 || i >= _points.Count) return;
            BrightnessPoint p = _points.OrderBy(x => x.Luma).ElementAt(i);
            _value.Text = "伽马 " + p.Gamma.ToString("0.00") + "（<1 提亮）  ·  饱和度近似 " + p.Saturation.ToString("0.00") + "（1.00 中性）";
            Bitmap preview = new Bitmap(_previewSource.Width, _previewSource.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(preview)) g.DrawImageUnscaled(_previewSource, 0, 0);
            Rectangle rect = new Rectangle(0, 0, preview.Width, preview.Height); BitmapData d = preview.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)d.Scan0.ToPointer();
                    for (int y = 0; y < preview.Height; y++) for (int x = 0; x < preview.Width; x++)
                    {
                        byte* px = ptr + y * d.Stride + x * 4;
                        for (int c = 0; c < 3; c++)
                        {
                            double linear = Math.Pow(px[c] / 255.0, p.Gamma);
                            px[c] = (byte)Math.Max(0, Math.Min(255, Math.Round((0.5 + (linear - 0.5) * p.Saturation) * 255)));
                        }
                    }
                }
            }
            finally { preview.UnlockBits(d); }
            Image old = _after.Image; _after.Image = preview; if (old != null) old.Dispose();
        }

        private void OpenCurveEditor(object sender, EventArgs e)
        {
            if (_points.Count == 0)
                for (int i = 0; i < 5; i++) _points.Add(new BrightnessPoint { Luma = i * 255 / 4, Gamma = 0.80, Saturation = 1.07 });
            using (GameBrightnessCurveEditorForm editor = new GameBrightnessCurveEditorForm(_cfg, _points, _save))
                editor.ShowDialog(this);
            _points.Sort(delegate(BrightnessPoint a, BrightnessPoint b) { return a.Luma.CompareTo(b.Luma); });
            RefreshList(true);
            if (_list.Items.Count > 0 && _list.SelectedIndex < 0) _list.SelectedIndex = 0;
        }

        private static int ImageLuma(string file)
        {
            try
            {
                using (Bitmap full = new Bitmap(file))
                using (Bitmap small = Downscale(full, BrightnessSampler.SampleWidth))
                    return BrightnessSampler.Measure(small);
            }
            catch { return 0; }
        }

        private void SaveProfile(object sender, EventArgs e)
        {
            if (_points.Count < 3) { MessageBox.Show("至少需要 3 张亮度不同的样本才能保存曲线。", "亮度校准"); return; }
            SaveCurrentProfile();
            _status.Text = "校准已保存。返回“辅助”页即可开启自动适配。";
            MessageBox.Show(_points.Count + " 个亮度节点的伽马与饱和度曲线已保存。", "亮度校准", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SaveCurrentProfile()
        {
            if (_points.Count < 3) return;
            _cfg.GameBrightnessProfile = GameBrightnessProfile.Serialize(_points);
            _cfg.GameBrightnessSaturationProfile = GameBrightnessProfile.Serialize(_points.Select(p => new BrightnessPoint { Luma = p.Luma, Gamma = p.Saturation }));
            if (_save != null) _save();
        }
    }

    internal sealed class GameBrightnessPreset
    {
        public string Name;
        public string Gamma;
        public string Saturation;
    }

    internal static class GameBrightnessPresetStore
    {
        public static List<GameBrightnessPreset> Parse(string text)
        {
            List<GameBrightnessPreset> result = new List<GameBrightnessPreset>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (string row in text.Split(';'))
            {
                string[] p = row.Split(',');
                if (p.Length != 3) continue;
                try { result.Add(new GameBrightnessPreset { Name = Decode(p[0]), Gamma = Decode(p[1]), Saturation = Decode(p[2]) }); } catch { }
            }
            return result;
        }
        private static string Encode(string s) { return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s ?? "")); }
        private static string Decode(string s) { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        public static string Serialize(IEnumerable<GameBrightnessPreset> rows)
        { return string.Join(";", rows.Select(p => Encode(p.Name) + "," + Encode(p.Gamma) + "," + Encode(p.Saturation)).ToArray()); }
        public static void Put(Config cfg, string name, string gamma, string saturation)
        {
            List<GameBrightnessPreset> rows = Parse(cfg.GameBrightnessPresets);
            GameBrightnessPreset old = rows.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (old != null) rows.Remove(old);
            rows.Add(new GameBrightnessPreset { Name = name, Gamma = gamma, Saturation = saturation });
            cfg.GameBrightnessPresets = Serialize(rows);
        }
    }

    /// <summary>可拖拽的双曲线编辑器；点位横轴是场景亮度，纵轴是伽马/饱和度比例。</summary>
    internal sealed class BrightnessCurveGraph : Control
    {
        private readonly List<BrightnessPoint> _points;
        private BrightnessPoint _dragged;
        public bool EditSaturation;
        public bool AutoCompensateSaturation = true;
        /// <summary>需求一.4：饱和度补偿增强（0–0.50）。</summary>
        public double CompensationBoost = 0.10;
        public BrightnessPoint Selected;
        public event Action Changed;
        public event Action SelectionChanged;

        public BrightnessCurveGraph(List<BrightnessPoint> points)
        {
            _points = points; DoubleBuffered = true; BackColor = Color.FromArgb(22, 25, 32); ForeColor = Color.White;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        }
        private Rectangle Plot { get { return new Rectangle(56, 18, Math.Max(20, Width - 78), Math.Max(20, Height - 58)); } }
        private float X(int l) { Rectangle r = Plot; return r.Left + r.Width * l / 255f; }
        private float Y(double v) { Rectangle r = Plot; return r.Bottom - r.Height * (float)((Math.Max(.2, Math.Min(1.6, v)) - .2) / 1.4); }
        private static double Val(BrightnessPoint p, bool sat) { return sat ? p.Saturation : p.Gamma; }
        public double ValueAt(int luma, bool sat)
        {
            List<BrightnessPoint> ordered = _points.OrderBy(p => p.Luma).ToList();
            if (ordered.Count == 0) return sat ? 1.07 : .8;
            if (luma <= ordered[0].Luma) return Val(ordered[0], sat);
            if (luma >= ordered[ordered.Count - 1].Luma) return Val(ordered[ordered.Count - 1], sat);
            for (int i = 0; i < ordered.Count - 1; i++) if (luma < ordered[i + 1].Luma)
            {
                double t = (double)(luma - ordered[i].Luma) / Math.Max(1, ordered[i + 1].Luma - ordered[i].Luma); t = t * t * (3 - 2 * t);
                return Val(ordered[i], sat) + (Val(ordered[i + 1], sat) - Val(ordered[i], sat)) * t;
            }
            return Val(ordered[ordered.Count - 1], sat);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = Plot;
            using (Pen grid = new Pen(Color.FromArgb(52, 60, 72)))
            using (Pen faint = new Pen(Color.FromArgb(37, 44, 55)))
            using (Brush text = new SolidBrush(Color.FromArgb(174, 185, 199)))
            using (Pen gammaPen = new Pen(Color.FromArgb(60, 210, 245), 2.5f))
            using (Pen satPen = new Pen(Color.FromArgb(255, 173, 74), 2.5f))
            using (Pen neutral = new Pen(Color.FromArgb(90, 145, 155, 165), 1f))
            using (Brush gammaBrush = new SolidBrush(Color.FromArgb(60, 210, 245)))
            using (Brush satBrush = new SolidBrush(Color.FromArgb(255, 173, 74)))
            using (Brush selectedBrush = new SolidBrush(Color.White))
            using (Font f = new Font("Segoe UI", 8f))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int l = i * 255 / 4; float x = X(l); g.DrawLine(i == 0 || i == 4 ? grid : faint, x, r.Top, x, r.Bottom); g.DrawString(l.ToString(), f, text, x - 8, r.Bottom + 5);
                    double v = .2 + i * .35; float y = Y(v); g.DrawLine(i == 0 || i == 4 ? grid : faint, r.Left, y, r.Right, y); g.DrawString(v.ToString("0.00"), f, text, 4, y - 8);
                }
                g.DrawLine(neutral, r.Left, Y(1), r.Right, Y(1));
                DrawCurve(g, gammaPen, false); DrawCurve(g, satPen, true);
                foreach (BrightnessPoint p in _points)
                {
                    DrawNode(g, gammaBrush, selectedBrush, p, p.Gamma, p == Selected && !EditSaturation);
                    DrawNode(g, satBrush, selectedBrush, p, p.Saturation, p == Selected && EditSaturation);
                }
                g.DrawString("亮度", f, text, r.Right - 25, r.Bottom + 23);
                g.DrawString("伽马", f, gammaBrush, r.Left + 8, r.Top + 4);
                g.DrawString("饱和度", f, satBrush, r.Left + 54, r.Top + 4);
                g.DrawString("双击空白处添加节点；拖动节点调整当前选中的曲线", f, text, r.Right - 280, r.Top + 4);
            }
        }
        private void DrawCurve(Graphics g, Pen pen, bool sat)
        { PointF[] path = new PointF[256]; for (int i = 0; i < path.Length; i++) path[i] = new PointF(X(i), Y(ValueAt(i, sat))); g.DrawLines(pen, path); }
        private void DrawNode(Graphics g, Brush normal, Brush selected, BrightnessPoint p, double val, bool isSelected)
        { float x = X(p.Luma), y = Y(val); RectangleF b = new RectangleF(x - (isSelected ? 6 : 4), y - (isSelected ? 6 : 4), isSelected ? 12 : 8, isSelected ? 12 : 8); g.FillEllipse(isSelected ? selected : normal, b); if (isSelected) using (Pen outline = new Pen(Color.Black, 1.2f)) g.DrawEllipse(outline, b); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            _dragged = _points.OrderBy(p => Distance(e.Location, new PointF(X(p.Luma), Y(Val(p, EditSaturation))))).FirstOrDefault();
            if (_dragged != null && Distance(e.Location, new PointF(X(_dragged.Luma), Y(Val(_dragged, EditSaturation)))) < 18)
            { Selected = _dragged; Capture = true; if (SelectionChanged != null) SelectionChanged(); Invalidate(); }
            else _dragged = null;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (_dragged == null || !Capture) return;
            int index = _points.OrderBy(p => p.Luma).ToList().IndexOf(_dragged); int luma = (int)Math.Round((e.X - Plot.Left) * 255.0 / Plot.Width);
            List<BrightnessPoint> sorted = _points.OrderBy(p => p.Luma).ToList(); int lo = index > 0 ? sorted[index - 1].Luma + 2 : 0; int hi = index < sorted.Count - 1 ? sorted[index + 1].Luma - 2 : 255;
            _dragged.Luma = Math.Max(lo, Math.Min(hi, luma)); double value = .2 + (Plot.Bottom - e.Y) * 1.4 / Plot.Height; value = Math.Max(.2, Math.Min(1.6, value));
            if (EditSaturation) _dragged.Saturation = value;
            else { _dragged.Gamma = value; if (AutoCompensateSaturation) _dragged.Saturation = GameBrightnessProfile.AutoSaturation(value, CompensationBoost); }
            Invalidate(); if (Changed != null) Changed();
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragged = null; Capture = false; }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e); if (e.Button != MouseButtons.Left) return;
            int luma = Math.Max(0, Math.Min(255, (int)Math.Round((e.X - Plot.Left) * 255.0 / Plot.Width)));
            if (_points.Any(p => Math.Abs(p.Luma - luma) < 12)) return;
            BrightnessPoint node = new BrightnessPoint { Luma = luma, Gamma = ValueAt(luma, false), Saturation = ValueAt(luma, true) };
            _points.Add(node); _points.Sort(delegate(BrightnessPoint a, BrightnessPoint b) { return a.Luma.CompareTo(b.Luma); }); Selected = node;
            if (Changed != null) Changed(); if (SelectionChanged != null) SelectionChanged(); Invalidate();
        }
        private static double Distance(Point a, PointF b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }
    }

    internal sealed class GameBrightnessCurveEditorForm : Form
    {
        private readonly Config _cfg;
        private readonly List<BrightnessPoint> _points;
        private readonly Action _save;
        private readonly BrightnessCurveGraph _graph;
        private readonly TrackBar _gamma = new TrackBar { Minimum = 20, Maximum = 160, TickFrequency = 10 };
        private readonly TrackBar _saturation = new TrackBar { Minimum = 20, Maximum = 160, TickFrequency = 10 };
        private readonly ComboBox _curveMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 178 };
        private readonly CheckBox _autoSaturation = new CheckBox { Text = "自动补色", AutoSize = true, Checked = true };
        private readonly PictureBox _original = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Dock = DockStyle.Fill };
        private readonly PictureBox _preview = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Dock = DockStyle.Fill };
        private readonly FlowLayoutPanel _gallery = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        private readonly ComboBox _presets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly TextBox _presetName = new TextBox { Width = 190 };
        private bool _loading;
        private Bitmap _reference;

        public GameBrightnessCurveEditorForm(Config cfg, List<BrightnessPoint> points, Action save)
        {
            _cfg = cfg; _points = points; _save = save; Text = "伽马与饱和度曲线"; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(1180, 760); MinimumSize = new Size(980, 650); BackColor = Color.FromArgb(28, 31, 39); ForeColor = Color.White;
            _graph = new BrightnessCurveGraph(_points); _graph.Dock = DockStyle.Fill; _graph.Selected = _points.OrderBy(p => p.Luma).ElementAtOrDefault(_points.Count / 2); _graph.CompensationBoost = _cfg.GameBrightnessCompensationBoost; _graph.Changed += GraphChanged; _graph.SelectionChanged += GraphSelectionChanged;
            FormClosed += delegate { if (_reference != null) { _reference.Dispose(); _reference = null; } if (_original.Image != null) { _original.Image.Dispose(); _original.Image = null; } if (_preview.Image != null) { _preview.Image.Dispose(); _preview.Image = null; } foreach (Control c in _gallery.Controls) { PictureBox pb = c as PictureBox; if (pb != null && pb.Image != null) { pb.Image.Dispose(); pb.Image = null; } } };
            BuildUi(); RefreshGallery(); RefreshPointControls(); UpdateReference();
        }
        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12), BackColor = BackColor };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118)); Controls.Add(root);
            root.Controls.Add(_graph, 0, 0);
            TableLayoutPanel side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(14, 0, 0, 0), BackColor = BackColor };
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 27)); side.RowStyles.Add(new RowStyle(SizeType.Percent, 27)); side.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); side.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); side.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); side.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); side.RowStyles.Add(new RowStyle(SizeType.Percent, 46)); root.Controls.Add(side, 1, 0);
            TableLayoutPanel pics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = BackColor };
            pics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); pics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); pics.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); pics.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pics.Controls.Add(new Label { Text = "参考原图", AutoSize = true }, 0, 0); pics.Controls.Add(new Label { Text = "当前曲线预览", AutoSize = true }, 1, 0); pics.Controls.Add(_original, 0, 1); pics.Controls.Add(_preview, 1, 1); side.Controls.Add(pics, 0, 0); side.SetRowSpan(pics, 2);
            _curveMode.Items.AddRange(new object[] { "编辑伽马（蓝）", "编辑饱和度（橙）" }); _curveMode.SelectedIndex = 0; _curveMode.SelectedIndexChanged += delegate { _graph.EditSaturation = _curveMode.SelectedIndex == 1; _graph.Invalidate(); };
            FlowLayoutPanel modeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) }; _autoSaturation.CheckedChanged += delegate { _graph.AutoCompensateSaturation = _autoSaturation.Checked; }; modeRow.Controls.Add(_curveMode); modeRow.Controls.Add(_autoSaturation); side.Controls.Add(modeRow, 0, 2);
            FlowLayoutPanel gammaRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) }; gammaRow.Controls.Add(new Label { Text = "伽马", Width = 42, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false });
            _gamma.Width = 290; _gamma.ValueChanged += delegate { if (!_loading && _graph.Selected != null) { _graph.Selected.Gamma = _gamma.Value / 100.0; if (_autoSaturation.Checked) _graph.Selected.Saturation = GameBrightnessProfile.AutoSaturation(_graph.Selected.Gamma, _cfg.GameBrightnessCompensationBoost); RefreshPointControls(); _graph.Invalidate(); UpdateReference(); } }; gammaRow.Controls.Add(_gamma); side.Controls.Add(gammaRow, 0, 3);
            FlowLayoutPanel satRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) }; satRow.Controls.Add(new Label { Text = "饱和度", Width = 42, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false });
            _saturation.Width = 290; _saturation.ValueChanged += delegate { if (!_loading && _graph.Selected != null) { _graph.Selected.Saturation = _saturation.Value / 100.0; _graph.Invalidate(); UpdateReference(); } }; satRow.Controls.Add(_saturation); side.Controls.Add(satRow, 0, 4);
            FlowLayoutPanel nodeBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            Button add = new Button { Text = "添加曲线点", Width = 112 }; add.Click += AddPoint; nodeBtns.Controls.Add(add);
            Button remove = new Button { Text = "删除选中点", Width = 112 }; remove.Click += RemovePoint; nodeBtns.Controls.Add(remove); side.Controls.Add(nodeBtns, 0, 5);
            FlowLayoutPanel preset = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            preset.Controls.Add(new Label { Text = "预设（同时保存两条曲线）", AutoSize = true });
            FlowLayoutPanel presetRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; presetRow.Controls.Add(_presets); Button load = new Button { Text = "载入", Width = 55 }; load.Click += LoadPreset; presetRow.Controls.Add(load); Button del = new Button { Text = "删除", Width = 55 }; del.Click += DeletePreset; presetRow.Controls.Add(del); preset.Controls.Add(presetRow);
            FlowLayoutPanel saveRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; saveRow.Controls.Add(_presetName); Button savePreset = new Button { Text = "保存预设", Width = 85 }; savePreset.Click += SavePreset; saveRow.Controls.Add(savePreset); preset.Controls.Add(saveRow); side.Controls.Add(preset, 0, 6);
            _gallery.Height = 100; root.Controls.Add(_gallery, 0, 1); root.SetColumnSpan(_gallery, 2);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button close = new Button { Text = "关闭", Width = 90 }; close.Click += delegate { Close(); }; footer.Controls.Add(close);
            Button apply = new Button { Text = "应用并保存两条曲线", Width = 170 }; apply.Click += ApplyAndSave; footer.Controls.Add(apply); Controls.Add(footer);
            _presets.Items.Clear(); foreach (GameBrightnessPreset p in GameBrightnessPresetStore.Parse(_cfg.GameBrightnessPresets)) _presets.Items.Add(p.Name);
            if (_presets.Items.Count > 0) _presets.SelectedIndex = 0;
        }
        private void RefreshGallery()
        {
            foreach (Control c in _gallery.Controls) { PictureBox pb = c as PictureBox; if (pb != null && pb.Image != null) pb.Image.Dispose(); c.Dispose(); }
            _gallery.Controls.Clear();
            foreach (BrightnessPoint p in _points.OrderBy(x => x.Luma)) if (!string.IsNullOrEmpty(p.File) && File.Exists(p.File))
            {
                PictureBox pb = new PictureBox { Width = 118, Height = 86, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black, Tag = p, Cursor = Cursors.Hand, Margin = new Padding(3) };
                try { using (Image im = Image.FromFile(p.File)) pb.Image = new Bitmap(im); } catch { }
                pb.Click += delegate(object sender, EventArgs e) { _graph.Selected = (BrightnessPoint)((Control)sender).Tag; UpdateReference(); RefreshPointControls(); _graph.Invalidate(); };
                _gallery.Controls.Add(pb);
            }
            if (_gallery.Controls.Count == 0) _gallery.Controls.Add(new Label { Text = "没有截图样本：双击曲线可添加节点；右侧提供中性彩色参考图。", AutoSize = true, ForeColor = Color.LightGray, Padding = new Padding(10) });
        }
        private void GraphChanged() { RefreshPointControls(); UpdateReference(); }
        private void GraphSelectionChanged() { RefreshPointControls(); UpdateReference(); }
        private void RefreshPointControls()
        {
            _loading = true; BrightnessPoint p = _graph.Selected;
            _gamma.Enabled = _saturation.Enabled = p != null;
            if (p != null) { _gamma.Value = Math.Max(20, Math.Min(160, (int)Math.Round(p.Gamma * 100))); _saturation.Value = Math.Max(20, Math.Min(160, (int)Math.Round(p.Saturation * 100))); }
            _loading = false; _graph.Invalidate();
        }
        private void AddPoint(object sender, EventArgs e)
        {
            List<BrightnessPoint> sorted = _points.OrderBy(p => p.Luma).ToList(); int bestLuma = 128, bestGap = -1;
            for (int i = 1; i < sorted.Count; i++) { int gap = sorted[i].Luma - sorted[i - 1].Luma; if (gap > bestGap) { bestGap = gap; bestLuma = (sorted[i].Luma + sorted[i - 1].Luma) / 2; } }
            if (sorted.Count == 1) bestLuma = sorted[0].Luma < 128 ? 192 : 64;
            if (sorted.Any(p => Math.Abs(p.Luma - bestLuma) < 3)) return;
            BrightnessPoint point = new BrightnessPoint { Luma = bestLuma, Gamma = _graph.ValueAt(bestLuma, false), Saturation = _graph.ValueAt(bestLuma, true) }; _points.Add(point); _points.Sort(delegate(BrightnessPoint a, BrightnessPoint b) { return a.Luma.CompareTo(b.Luma); }); _graph.Selected = point; _graph.Invalidate(); RefreshGallery(); RefreshPointControls();
        }
        private void RemovePoint(object sender, EventArgs e)
        { if (_points.Count <= 3 || _graph.Selected == null) return; _points.Remove(_graph.Selected); _graph.Selected = _points.OrderBy(p => p.Luma).ElementAt(_points.Count / 2); _graph.Invalidate(); RefreshGallery(); RefreshPointControls(); }
        private string GammaText() { return GameBrightnessProfile.Serialize(_points); }
        private string SaturationText() { return GameBrightnessProfile.Serialize(_points.Select(p => new BrightnessPoint { Luma = p.Luma, Gamma = p.Saturation })); }
        private void ApplyAndSave(object sender, EventArgs e)
        { if (_points.Count < 3) { MessageBox.Show("至少保留 3 个曲线点。", "曲线编辑"); return; } _cfg.GameBrightnessProfile = GammaText(); _cfg.GameBrightnessSaturationProfile = SaturationText(); if (_save != null) _save(); MessageBox.Show("伽马与饱和度曲线已保存。", "曲线编辑", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        private void SavePreset(object sender, EventArgs e)
        {
            string name = _presetName.Text.Trim(); if (name.Length == 0) { MessageBox.Show("请先输入预设名称。", "保存预设"); return; }
            GameBrightnessPresetStore.Put(_cfg, name, GammaText(), SaturationText()); if (_save != null) _save();
            int old = _presets.SelectedIndex; _presets.Items.Clear(); foreach (GameBrightnessPreset p in GameBrightnessPresetStore.Parse(_cfg.GameBrightnessPresets)) _presets.Items.Add(p.Name); _presets.SelectedIndex = Math.Max(0, Math.Min(old, _presets.Items.Count - 1));
        }
        private void LoadPreset(object sender, EventArgs e)
        {
            if (_presets.SelectedItem == null) return; GameBrightnessPreset p = GameBrightnessPresetStore.Parse(_cfg.GameBrightnessPresets).FirstOrDefault(x => x.Name == _presets.SelectedItem.ToString()); if (p == null) return;
            List<BrightnessPoint> oldPoints = new List<BrightnessPoint>(_points);
            List<BrightnessPoint> gamma = GameBrightnessProfile.Parse(p.Gamma), sat = GameBrightnessProfile.Parse(p.Saturation); _points.Clear();
            foreach (BrightnessPoint g in gamma) { BrightnessPoint s = sat.OrderBy(x => Math.Abs(x.Luma - g.Luma)).FirstOrDefault(); BrightnessPoint img = oldPoints.Where(x => !string.IsNullOrEmpty(x.File)).OrderBy(x => Math.Abs(x.Luma - g.Luma)).FirstOrDefault(); _points.Add(new BrightnessPoint { Luma = g.Luma, Gamma = g.Gamma, Saturation = s == null ? 1.07 : s.Gamma, File = img == null ? null : img.File }); }
            _graph.Selected = _points.OrderBy(x => x.Luma).ElementAtOrDefault(_points.Count / 2); RefreshGallery(); RefreshPointControls(); UpdateReference();
        }
        private void DeletePreset(object sender, EventArgs e)
        {
            if (_presets.SelectedItem == null) return; string name = _presets.SelectedItem.ToString(); List<GameBrightnessPreset> rows = GameBrightnessPresetStore.Parse(_cfg.GameBrightnessPresets); rows.RemoveAll(x => x.Name == name); _cfg.GameBrightnessPresets = GameBrightnessPresetStore.Serialize(rows); if (_save != null) _save();
            _presets.Items.Clear(); foreach (GameBrightnessPreset p in rows) _presets.Items.Add(p.Name); if (_presets.Items.Count > 0) _presets.SelectedIndex = 0;
        }
        private void UpdateReference()
        {
            BrightnessPoint point = _graph.Selected; if (point == null) return;
            BrightnessPoint imagePoint = !string.IsNullOrEmpty(point.File) && File.Exists(point.File) ? point : _points.Where(p => !string.IsNullOrEmpty(p.File) && File.Exists(p.File)).OrderBy(p => Math.Abs(p.Luma - point.Luma)).FirstOrDefault();
            Bitmap source = null;
            if (imagePoint != null) { try { using (Image im = Image.FromFile(imagePoint.File)) { source = new Bitmap(320, 190, PixelFormat.Format32bppArgb); using (Graphics g = Graphics.FromImage(source)) { g.Clear(Color.Black); g.DrawImage(im, new Rectangle(0, 0, source.Width, source.Height)); } } } catch { } }
            if (source == null) source = CreateReference();
            if (_reference != null) _reference.Dispose(); _reference = source;
            Image oldOriginal = _original.Image, oldPreview = _preview.Image;
            _original.Image = (Bitmap)_reference.Clone(); _preview.Image = Transform(_reference, point.Gamma, point.Saturation);
            if (oldOriginal != null) oldOriginal.Dispose(); if (oldPreview != null) oldPreview.Dispose();
        }
        private static Bitmap CreateReference()
        {
            Bitmap b = new Bitmap(320, 190); using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(32, 36, 45));
                for (int x = 0; x < 320; x++) { int v = x * 255 / 319; using (Pen p = new Pen(Color.FromArgb(v, v, v))) g.DrawLine(p, x, 0, x, 189); }
                using (Brush red = new SolidBrush(Color.FromArgb(230, 40, 55))) using (Brush green = new SolidBrush(Color.FromArgb(30, 210, 100))) using (Brush blue = new SolidBrush(Color.FromArgb(40, 110, 245)))
                { g.FillEllipse(red, 42, 62, 62, 62); g.FillRectangle(green, 128, 62, 62, 62); g.FillPolygon(blue, new Point[] { new Point(235, 60), new Point(275, 120), new Point(205, 120) }); }
            }
            return b;
        }
        private static Bitmap Transform(Bitmap input, double gamma, double saturation)
        {
            Bitmap b = new Bitmap(input.Width, input.Height, PixelFormat.Format32bppArgb); using (Graphics g = Graphics.FromImage(b)) g.DrawImage(input, 0, 0);
            BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try { unsafe { byte* p = (byte*)d.Scan0.ToPointer(); for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++) { byte* q = p + y * d.Stride + x * 4; for (int c = 0; c < 3; c++) { double v = Math.Pow(q[c] / 255.0, gamma); q[c] = (byte)Math.Max(0, Math.Min(255, Math.Round((.5 + (v - .5) * saturation) * 255))); } } } }
            finally { b.UnlockBits(d); } return b;
        }
    }
}
