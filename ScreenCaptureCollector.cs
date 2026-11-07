using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 第 6 批：截屏采集。
    ///
    /// 规则（用户 2026-11-07 确认）：
    ///   · 一次采集固定产出 21 张，存同一个文件夹，文件夹上限 21 张；
    ///   · 先采 7 张基础图 → 提示"基础的图片已采集完成" → 继续采到 21 张自动停；
    ///   · 界面只展示 7 张：21 张按平均亮度排序后取 0/3/7/10/13/17/20 七个分位，
    ///     保证亮度跨度最大且分布均匀；
    ///   · 下一次采集整体覆盖上一轮：先写临时目录，21 张齐了再替换；
    ///   · 采集中死亡 → 挂鼠标钩子等一次点击（观战）后继续；30 秒没点转"用户暂停"；
    ///   · 采集中复活 → 自动暂停，点继续恢复。
    ///
    /// 提示走独立通道（CaptureHudForm），默认不参与悬浮窗避让。
    /// </summary>
    internal enum CaptureState
    {
        Idle = 0,
        Capturing = 1,
        PausedByDeath = 2,
        PausedByRespawn = 3,
        PausedByUser = 4
    }

    internal sealed class CaptureShot
    {
        public string TempPath;
        public int Luma;
        public DateTime At;
    }

    internal sealed class ScreenCaptureCollector : IDisposable
    {
        private readonly Config _cfg;
        private readonly System.Threading.Timer _timer;
        private readonly List<CaptureShot> _shots = new List<CaptureShot>();
        private MouseHook _mouse;
        private DateTime _deadAt = DateTime.MinValue;
        private int _busy;

        private CaptureState _state = CaptureState.Idle;
        private string _tempDir;
        private bool _missedSpectate;

        /// <summary>状态变化（后台线程回调，UI 自己切线程）。</summary>
        public Action<CaptureState> StateChanged;
        public Action<int, int> Progress;          // 已采 / 上限
        public Action<string, bool> Notice;        // 文字, 是不是轻提示
        public Action<string> Completed;           // 最终目录
        public Action SaveRequested;               // 让外层保存 config.ini

        public ScreenCaptureCollector(Config cfg)
        {
            _cfg = cfg;
            _timer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public CaptureState State { get { return _state; } }
        public int Count { get { return _shots.Count; } }
        public int MaxCount { get { return Math.Max(7, Math.Min(21, _cfg.CaptureMaxCount)); } }
        public int BaseCount { get { return Math.Max(1, Math.Min(MaxCount, _cfg.CaptureMinCount)); } }

        public string FinalDir
        {
            get
            {
                string dir = string.IsNullOrEmpty(_cfg.CaptureDir) ? "采集" : _cfg.CaptureDir;
                if (!Path.IsPathRooted(dir)) dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir);
                return dir;
            }
        }

        public void Start()
        {
            if (_state != CaptureState.Idle) return;
            _shots.Clear();
            _deadAt = DateTime.MinValue;
            try
            {
                // 先写临时目录，采满再整体替换，避免中途失败把上一轮的图弄丢
                string root = FinalDir;
                Directory.CreateDirectory(root);
                _tempDir = Path.Combine(root, ".tmp-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
                Directory.CreateDirectory(_tempDir);
            }
            catch (Exception ex)
            {
                Logger.Err("采集", "创建采集目录失败", ex);
                Notice("创建采集目录失败：" + ex.Message, true);
                return;
            }

            SetState(CaptureState.Capturing);
            // 需求：点开始后把游戏切到前台一次（之后软件可以在后台继续截）
            try
            {
                IntPtr game = ScreenGeometry.FindGameWindow();
                if (game != IntPtr.Zero) NativeMethods.SetForegroundWindow(game);
            }
            catch { }
            Notice("开始采集：先采 " + BaseCount + " 张基础图", false);
            _timer.Change(250, 500);
        }

        public void Pause(string reason, bool user)
        {
            if (_state != CaptureState.Capturing) return;
            SetState(user ? CaptureState.PausedByUser : CaptureState.PausedByDeath);
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            Notice(reason, true);
        }

        public void Resume()
        {
            if (_state == CaptureState.Capturing || _state == CaptureState.Idle) return;
            bool missed = _missedSpectate;
            _missedSpectate = false;
            SetState(CaptureState.Capturing);
            _timer.Change(250, 500);
            // 需求二.5：30 秒没点到观战视角 → 用户暂停后手动继续时提示
            Notice(missed ? "未检测到观战视角，可能采集到死亡画面" : "已继续采集，请注意帧率波动", true);
        }

        /// <summary>停止：数据保留，落到最终目录并提示路径（不删除）。</summary>
        public void Stop()
        {
            if (_state == CaptureState.Idle) return;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            Finish(true);
        }

        /// <summary>只删当前采集目录里的图片（界面上的二次确认由调用方做）。</summary>
        public string DeleteAll()
        {
            try
            {
                string dir = FinalDir;
                if (!Directory.Exists(dir)) return "目录不存在：" + dir;
                int n = 0;
                foreach (string f in Directory.GetFiles(dir))
                {
                    if (!f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(f); n++; } catch { }
                }
                _shots.Clear();
                if (_state != CaptureState.Idle) { _timer.Change(Timeout.Infinite, Timeout.Infinite); SetState(CaptureState.Idle); }
                return "已删除 " + n + " 张图片";
            }
            catch (Exception ex) { return "删除失败：" + ex.Message; }
        }

        // ---------------- 死亡 / 复活 ----------------

        public void OnSelfDeath()
        {
            if (_state == CaptureState.Capturing)
            {
                // 死亡界面不算有效样本：先停，挂鼠标钩子等玩家点到观战视角
                _deadAt = DateTime.Now;
                SetState(CaptureState.PausedByDeath);
                _timer.Change(1000, 1000);   // 保留看门狗：30 秒没点转用户暂停
                InstallMouseHook();
                Notice("检测到死亡：点一下游戏画面（观战视角）即可继续采集", false);
            }
            // 暂停期间死亡：保持暂停，不挂钩子，不自动恢复
        }

        public void OnSelfRespawn()
        {
            RemoveMouseHook();
            if (_state == CaptureState.Capturing)
            {
                SetState(CaptureState.PausedByRespawn);
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                Notice("已暂停：检测到复活", false);
            }
            // 其他暂停状态保持不变
        }

        private void InstallMouseHook()
        {
            try
            {
                if (_mouse == null)
                {
                    _mouse = new MouseHook();
                    _mouse.ButtonChanged += OnMouseButton;
                }
                if (!_mouse.Installed) _mouse.Install();
            }
            catch { }
        }

        private void RemoveMouseHook()
        {
            try
            {
                if (_mouse != null)
                {
                    _mouse.ButtonChanged -= OnMouseButton;
                    _mouse.Dispose();
                    _mouse = null;
                }
            }
            catch { }
        }

        /// <summary>死亡后的一次点击：只要点在游戏窗口里，就恢复采集（观战视角）。</summary>
        private void OnMouseButton(int button, bool down)
        {
            if (!down || _state != CaptureState.PausedByDeath) return;
            if (button != 0 && button != 1) return;   // 只统计左右键
            bool inGame = false;
            try
            {
                Point p = Cursor.Position;
                IntPtr hwnd = ScreenGeometry.FindGameWindow();
                if (hwnd != IntPtr.Zero)
                {
                    Rectangle client = BrightnessSampler.ClientBounds(hwnd);
                    inGame = !client.IsEmpty && client.Contains(p);
                }
            }
            catch { }
            if (!inGame)
            {
                // 软件窗口里的点击不算
                return;
            }

            RemoveMouseHook();
            SetState(CaptureState.Capturing);
            _timer.Change(250, 500);
            Notice("已继续采集（观战视角）", true);
        }

        // ---------------- 采集循环 ----------------

        private void OnTick(object state)
        {
            // 死亡后等点击的 30 秒看门狗（这段时间不采集，但计时器仍在跑）
            if (_state == CaptureState.PausedByDeath)
            {
                if (_deadAt != DateTime.MinValue && (DateTime.Now - _deadAt).TotalSeconds >= 30)
                {
                    RemoveMouseHook();
                    _missedSpectate = true;
                    SetState(CaptureState.PausedByUser);
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    Notice("30 秒未点击，已转为暂停", false);
                }
                return;
            }
            if (_state != CaptureState.Capturing) return;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try { CaptureOne(); }
            catch (Exception ex) { Logger.Err("采集", "截屏失败", ex); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        private void CaptureOne()
        {
            if (_shots.Count >= MaxCount) { Finish(false); return; }

            IntPtr hwnd = ScreenGeometry.FindGameWindow();
            if (hwnd == IntPtr.Zero) return;
            Rectangle client = BrightnessSampler.ClientBounds(hwnd);
            if (client.Width < 64 || client.Height < 64) return;

            Bitmap bmp = CaptureClient(client);
            if (bmp == null) return;
            Bitmap small = null;
            try
            {
                small = Scale(bmp, 192);
                int luma = small != null ? BrightnessSampler.Measure(small) : 0;
                // 亮度几乎一样的画面不重复收：21 张要尽量覆盖不同亮度
                if (_shots.Count > 0)
                {
                    CaptureShot last = _shots[_shots.Count - 1];
                    if (Math.Abs(last.Luma - luma) < 2) return;
                }
                string path = Path.Combine(_tempDir, "capture" + (_shots.Count + 1).ToString("00") + ".png");
                bmp.Save(path, ImageFormat.Png);
                CaptureShot shot = new CaptureShot();
                shot.TempPath = path;
                shot.Luma = luma;
                shot.At = DateTime.Now;
                _shots.Add(shot);

                int total = MaxCount;
                if (Progress != null) Progress(_shots.Count, total);
                else Notice("当前已采集 " + _shots.Count + "/" + total + " 张图片", true);

                if (_shots.Count == BaseCount)
                    Notice("基础的图片已采集完成（" + BaseCount + "/" + total + "），继续采到 " + total + " 张", false);
                if (_shots.Count >= MaxCount) Finish(false);
            }
            finally
            {
                bmp.Dispose();
                if (small != null) small.Dispose();
            }
        }

        private static Bitmap CaptureClient(Rectangle client)
        {
            try
            {
                Bitmap bmp = new Bitmap(client.Width, client.Height, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(client.Left, client.Top, 0, 0, client.Size, CopyPixelOperation.SourceCopy);
                return bmp;
            }
            catch { return null; }
        }

        private static Bitmap Scale(Bitmap src, int width)
        {
            try
            {
                if (src.Width <= width) return (Bitmap)src.Clone();
                int height = Math.Max(9, (int)Math.Round(width * (double)src.Height / src.Width));
                Bitmap dst = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(dst))
                    g.DrawImage(src, 0, 0, width, height);
                return dst;
            }
            catch { return null; }
        }

        // ---------------- 收尾 / 挑 7 张 ----------------

        /// <summary>挑 7 张：按亮度升序取等分索引 0/3/7/10/13/17/20。</summary>
        public static List<int> PickEvenByLuma(List<CaptureShot> shots, int pick)
        {
            List<int> result = new List<int>();
            if (shots == null || shots.Count == 0) return result;
            List<int> order = new List<int>();
            for (int i = 0; i < shots.Count; i++) order.Add(i);
            order.Sort(delegate(int a, int b)
            {
                int c = shots[a].Luma.CompareTo(shots[b].Luma);
                return c != 0 ? c : a.CompareTo(b);
            });
            if (pick >= order.Count)
            {
                result.AddRange(order);
                return result;
            }
            for (int k = 0; k < pick; k++)
            {
                int idx = (int)Math.Round(k * (order.Count - 1) / (double)(pick - 1));
                if (idx < 0) idx = 0;
                if (idx >= order.Count) idx = order.Count - 1;
                if (!result.Contains(order[idx])) result.Add(order[idx]);
            }
            return result;
        }

        private void Finish(bool stopped)
        {
            if (_state == CaptureState.Idle) return;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            RemoveMouseHook();

            if (_shots.Count == 0)
            {
                SetState(CaptureState.Idle);
                Notice("没有采到图片", true);
                return;
            }

            List<int> picked = PickEvenByLuma(_shots, 7);
            string dir = FinalDir;
            try
            {
                Directory.CreateDirectory(dir);
                // 覆盖：先删旧的 png，再把临时文件搬过来
                foreach (string f in Directory.GetFiles(dir, "*.png"))
                {
                    try { File.Delete(f); } catch { }
                }
                List<int> order = new List<int>();
                for (int i = 0; i < _shots.Count; i++) order.Add(i);
                order.Sort(delegate(int a, int b) { return _shots[a].Luma.CompareTo(_shots[b].Luma); });

                string[] names = new string[_shots.Count];
                int n = 0;
                for (int k = 0; k < order.Count; k++)
                {
                    int srcIndex = order[k];
                    string target = Path.Combine(dir, "capture" + (n + 1).ToString("00") + ".png");
                    try { File.Move(_shots[srcIndex].TempPath, target); } catch { }
                    names[srcIndex] = Path.GetFileName(target);
                    n++;
                }
                _cfg.CaptureCount = _shots.Count;
                _cfg.CaptureSelected = JoinIndices(picked, order);
                _cfg.CaptureSetAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (SaveRequested != null) { try { SaveRequested(); } catch { } }
            }
            catch (Exception ex)
            {
                Logger.Err("采集", "保存采集结果失败", ex);
                Notice("保存失败：" + ex.Message, false);
            }
            finally
            {
                try { if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
            }

            SetState(CaptureState.Idle);
            Notice("采集完成：" + _shots.Count + " 张（已挑 7 张）", false);
            if (Completed != null) Completed(dir);
        }

        private static string JoinIndices(List<int> picked, List<int> sortedOrder)
        {
            if (picked == null || picked.Count == 0) return "";
            List<string> parts = new List<string>();
            for (int i = 0; i < picked.Count; i++)
            {
                int sortedPos = sortedOrder.IndexOf(picked[i]) + 1;
                parts.Add(sortedPos.ToString());
            }
            return string.Join(",", parts.ToArray());
        }

        private void SetState(CaptureState s)
        {
            if (_state == s) return;
            _state = s;
            Logger.On("采集", "状态 → " + s);
            if (StateChanged != null) { try { StateChanged(s); } catch { } }
        }

        public void Dispose()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _timer.Dispose();
            RemoveMouseHook();
        }
    }
}
