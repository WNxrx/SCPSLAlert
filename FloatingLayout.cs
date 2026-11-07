using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 第 8 批：普通提示的防重叠布局。
    ///
    /// 只管理带框的“普通提示”（IAlertSurface）；触发点的图片/无框文字和
    /// CaptureHudForm 独立通道不注册。
    ///
    /// 规则：
    ///   · 只同屏计算；用表面实际尺寸/锚点，不用估算值；
    ///   · 默认向下：新提示与旧提示重叠时，优先插进离默认位置最近的可用间隙，
    ///     没有间隙就放到整组下方；旧提示会被后续项依次向下推；
    ///   · 向下会把任何一条推出工作区 → 整组改算向上（镜像算法）；
    ///   · 移动前一次性算完所有目标，再用一个共享计时器缓动，不逐窗瞬移；
    ///   · 上下都放不下：新提示优先，允许与最旧一条重叠并写日志，不删除窗口；
    ///   · 位移只作用于“显示位置”，不写回 config；用户拖拽的锚点不被覆盖。
    /// </summary>
    internal sealed class FloatingLayoutManager : IDisposable
    {
        private sealed class Item
        {
            public IAlertSurface Surface;
            public Rectangle Anchor;
            public Size Size;
            public int ShowOrder;
        }

        private sealed class Anim
        {
            public int FromX;
            public int FromY;
            public int ToX;
            public int ToY;
        }

        private readonly Config _cfg;
        private IAlertSurface[] _surfaces;
        private readonly Timer _timer;
        private readonly Dictionary<IAlertSurface, Anim> _anims =
            new Dictionary<IAlertSurface, Anim>();
        private DateTime _animStart;
        private int _animMs;
        private bool _suspended;
        private bool _disposed;
        private bool _overflowLogged;
        private readonly Dictionary<IAlertSurface, int> _showOrder =
            new Dictionary<IAlertSurface, int>();
        private int _nextShowOrder = 1;

        public FloatingLayoutManager(Config cfg, IAlertSurface[] surfaces)
        {
            _cfg = cfg;
            _surfaces = surfaces ?? new IAlertSurface[0];
            _timer = new Timer();
            _timer.Interval = 15;
            _timer.Tick += OnTick;
        }

        public bool Suspended
        {
            get { return _suspended; }
            set
            {
                if (_suspended == value) return;
                _suspended = value;
                if (value) StopAnimation();
            }
        }

        public void SetSurfaces(IAlertSurface[] surfaces)
        {
            StopAnimation();
            _anims.Clear();
            _surfaces = surfaces ?? new IAlertSurface[0];
            _overflowLogged = false;
            _showOrder.Clear();
            _nextShowOrder = 1;
        }

        /// <summary>每次 ApplyAll 后调用；只移动位置变了且仍在屏幕上的提示。</summary>
        public void Relayout(bool immediate)
        {
            if (_disposed || _cfg == null || _suspended) return;
            if (!_cfg.AlertAvoidEnabled)
            {
                // 关掉避让后，已在屏幕上的提示要回到配置锚点，不能停在旧偏移。
                ResetToAnchors();
                return;
            }
            if (_surfaces == null || _surfaces.Length == 0) return;

            List<Item> items = new List<Item>();
            HashSet<IAlertSurface> seen = new HashSet<IAlertSurface>();
            for (int i = 0; i < _surfaces.Length; i++)
            {
                IAlertSurface s = _surfaces[i];
                if (s == null || !s.IsPopped) continue;
                Size size;
                Point anchor;
                try
                {
                    size = s.SurfaceSize;
                    anchor = s.RestLocation;
                }
                catch { continue; }
                if (size.Width <= 0 || size.Height <= 0) continue;

                int order;
                if (!_showOrder.TryGetValue(s, out order))
                {
                    order = _nextShowOrder++;
                    _showOrder[s] = order;
                }
                seen.Add(s);
                items.Add(new Item
                {
                    Surface = s,
                    Anchor = new Rectangle(anchor, size),
                    Size = size,
                    ShowOrder = order
                });
            }
            // 已经收起的提示下次重新出现时算新提示，排到下方。
            if (_showOrder.Count > seen.Count)
            {
                List<IAlertSurface> stale = new List<IAlertSurface>();
                foreach (KeyValuePair<IAlertSurface, int> p in _showOrder)
                    if (!seen.Contains(p.Key)) stale.Add(p.Key);
                for (int i = 0; i < stale.Count; i++) _showOrder.Remove(stale[i]);
            }
            if (items.Count == 0)
            {
                StopAnimation();
                return;
            }

            Dictionary<IAlertSurface, Rectangle> targets = Compute(items);
            if (targets == null || targets.Count == 0) return;
            Apply(targets, immediate);
        }

        private void ResetToAnchors()
        {
            StopAnimation();
            if (_surfaces == null) return;
            for (int i = 0; i < _surfaces.Length; i++)
            {
                IAlertSurface s = _surfaces[i];
                if (s == null || !s.IsPopped) continue;
                try
                {
                    Point p = s.RestLocation;
                    s.SetDisplayPosition(p.X, p.Y);
                }
                catch { }
            }
        }

        private Dictionary<IAlertSurface, Rectangle> Compute(List<Item> items)
        {
            Dictionary<IAlertSurface, Rectangle> result =
                new Dictionary<IAlertSurface, Rectangle>();
            int gap = Math.Max(0, Math.Min(32, _cfg.AlertAvoidGapPx));
            int margin = Math.Max(0, Math.Min(64, _cfg.AlertAvoidMarginPx));
            bool overflowed = false;

            for (int si = 0; si < Screen.AllScreens.Length; si++)
            {
                Screen screen = Screen.AllScreens[si];
                List<Item> group = new List<Item>();
                for (int i = 0; i < items.Count; i++)
                {
                    try
                    {
                        Screen owner = Screen.FromPoint(new Point(items[i].Anchor.X, items[i].Anchor.Y));
                        if (string.Equals(owner.DeviceName, screen.DeviceName,
                            StringComparison.OrdinalIgnoreCase))
                            group.Add(items[i]);
                    }
                    catch { }
                }
                if (group.Count == 0) continue;

                Rectangle work = screen.WorkingArea;
                Dictionary<IAlertSurface, Rectangle> down;
                if (TryLayout(group, work, gap, margin, true, out down))
                {
                    foreach (KeyValuePair<IAlertSurface, Rectangle> p in down)
                        result[p.Key] = p.Value;
                    continue;
                }

                Dictionary<IAlertSurface, Rectangle> up;
                if (TryLayout(group, work, gap, margin, false, out up))
                {
                    foreach (KeyValuePair<IAlertSurface, Rectangle> p in up)
                        result[p.Key] = p.Value;
                    continue;
                }

                // 上下都不够：新提示优先，允许重叠，不删除任何窗口。
                Dictionary<IAlertSurface, Rectangle> overflow = OverflowLayout(group, work, gap, margin);
                foreach (KeyValuePair<IAlertSurface, Rectangle> p in overflow)
                    result[p.Key] = p.Value;
                overflowed = true;
                if (!_overflowLogged)
                {
                    _overflowLogged = true;
                    Logger.On("提示", "防重叠：上下都放不下 " + group.Count
                        + " 条提示，已按新提示优先处理（允许重叠，未删除窗口）");
                }
            }

            if (!overflowed) _overflowLogged = false;
            return result;
        }

        /// <summary>向下/向上镜像布局；全部放得下才返回 true。</summary>
        private static bool TryLayout(List<Item> group, Rectangle work, int gap, int margin,
            bool down, out Dictionary<IAlertSurface, Rectangle> result)
        {
            result = new Dictionary<IAlertSurface, Rectangle>();
            int top = work.Top + margin;
            int bottom = work.Bottom - margin;
            int left = work.Left + margin;
            int right = work.Right - margin;
            if (right - left < 8 || bottom - top < 8) return false;

            List<Item> ordered = new List<Item>(group);
            if (down)
                ordered.Sort(delegate(Item a, Item b)
                {
                    int c = a.Anchor.Y.CompareTo(b.Anchor.Y);
                    if (c != 0) return c;
                    c = a.ShowOrder.CompareTo(b.ShowOrder);
                    return c != 0 ? c : a.Anchor.X.CompareTo(b.Anchor.X);
                });
            else
                ordered.Sort(delegate(Item a, Item b)
                {
                    int c = b.Anchor.Y.CompareTo(a.Anchor.Y);
                    if (c != 0) return c;
                    c = b.ShowOrder.CompareTo(a.ShowOrder);
                    return c != 0 ? c : b.Anchor.X.CompareTo(a.Anchor.X);
                });

            List<Rectangle> placed = new List<Rectangle>();
            for (int i = 0; i < ordered.Count; i++)
            {
                Item it = ordered[i];
                int w = it.Size.Width;
                int h = it.Size.Height;
                if (w > right - left) w = right - left;
                if (h > bottom - top) h = bottom - top;
                if (w < 1 || h < 1) return false;

                int x = Clamp(it.Anchor.X, left, right - w);
                int y = it.Anchor.Y;
                if (down)
                {
                    if (y < top) y = top;
                    if ((y + h > bottom) || !Fits(placed, x, y, w, h, gap))
                    {
                        int gapY = FindGapDown(placed, x, w, h, top, bottom, gap, it.Anchor.Y);
                        if (gapY != int.MinValue) y = gapY;
                        else
                        {
                            y = it.Anchor.Y;
                            for (int j = 0; j < placed.Count; j++)
                            {
                                Rectangle r = placed[j];
                                if (x < r.Right + gap && x + w + gap > r.Left)
                                    y = Math.Max(y, r.Bottom + gap);
                            }
                        }
                    }
                }
                else
                {
                    if (y + h > bottom) y = bottom - h;
                    if ((y < top) || !Fits(placed, x, y, w, h, gap))
                    {
                        int gapY = FindGapUp(placed, x, w, h, top, bottom, gap, it.Anchor.Y);
                        if (gapY != int.MinValue) y = gapY;
                        else
                        {
                            y = it.Anchor.Y;
                            for (int j = 0; j < placed.Count; j++)
                            {
                                Rectangle r = placed[j];
                                if (x < r.Right + gap && x + w + gap > r.Left)
                                    y = Math.Min(y, r.Top - gap - h);
                            }
                        }
                    }
                }

                if (y < top || y + h > bottom) return false;
                if (!Fits(placed, x, y, w, h, gap)) return false;
                placed.Add(new Rectangle(x, y, w, h));
                result[it.Surface] = new Rectangle(x, y, w, h);
            }
            return true;
        }

        private static bool Fits(List<Rectangle> placed, int x, int y, int w, int h, int gap)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                Rectangle r = placed[i];
                if (x < r.Right + gap && x + w + gap > r.Left
                    && y < r.Bottom + gap && y + h + gap > r.Top)
                    return false;
            }
            return true;
        }

        private static int FindGapDown(List<Rectangle> placed, int x, int w, int h,
            int top, int bottom, int gap, int desired)
        {
            List<Rectangle> sorted = new List<Rectangle>(placed);
            sorted.RemoveAll(delegate(Rectangle r)
            {
                return !(x < r.Right + gap && x + w + gap > r.Left);
            });
            sorted.Sort(delegate(Rectangle a, Rectangle b) { return a.Top.CompareTo(b.Top); });
            int best = int.MinValue;
            int bestDist = int.MaxValue;
            int gapStart = top;
            for (int i = 0; i <= sorted.Count; i++)
            {
                int limit = i < sorted.Count ? sorted[i].Top : bottom;
                if (limit - gapStart >= h + gap)
                {
                    int cand = gapStart;
                    if (cand >= top && cand + h <= bottom)
                    {
                        int dist = Math.Abs(cand - desired);
                        if (dist < bestDist) { best = cand; bestDist = dist; }
                    }
                }
                if (i < sorted.Count) gapStart = Math.Max(gapStart, sorted[i].Bottom + gap);
            }
            return best;
        }

        private static int FindGapUp(List<Rectangle> placed, int x, int w, int h,
            int top, int bottom, int gap, int desired)
        {
            List<Rectangle> sorted = new List<Rectangle>(placed);
            sorted.RemoveAll(delegate(Rectangle r)
            {
                return !(x < r.Right + gap && x + w + gap > r.Left);
            });
            sorted.Sort(delegate(Rectangle a, Rectangle b) { return b.Bottom.CompareTo(a.Bottom); });
            int best = int.MinValue;
            int bestDist = int.MaxValue;
            int gapEnd = bottom;
            for (int i = 0; i <= sorted.Count; i++)
            {
                int limit = i < sorted.Count ? sorted[i].Bottom : top;
                if (gapEnd - limit >= h + gap)
                {
                    int cand = gapEnd - h;
                    if (cand >= top && cand + h <= bottom)
                    {
                        int dist = Math.Abs(cand - desired);
                        if (dist < bestDist) { best = cand; bestDist = dist; }
                    }
                }
                if (i < sorted.Count) gapEnd = Math.Min(gapEnd, sorted[i].Top - gap);
            }
            return best;
        }

        /// <summary>兜底：仍保证每条都在屏幕内；允许重叠，不丢窗口。</summary>
        private static Dictionary<IAlertSurface, Rectangle> OverflowLayout(
            List<Item> group, Rectangle work, int gap, int margin)
        {
            Dictionary<IAlertSurface, Rectangle> result =
                new Dictionary<IAlertSurface, Rectangle>();
            int top = work.Top + margin;
            int bottom = work.Bottom - margin;
            int left = work.Left + margin;
            int right = work.Right - margin;
            List<Item> ordered = new List<Item>(group);
            ordered.Sort(delegate(Item a, Item b)
            {
                int c = a.Anchor.Y.CompareTo(b.Anchor.Y);
                if (c != 0) return c;
                c = a.ShowOrder.CompareTo(b.ShowOrder);
                return c != 0 ? c : a.Anchor.X.CompareTo(b.Anchor.X);
            });
            int lastBottom = top;
            for (int i = 0; i < ordered.Count; i++)
            {
                Item it = ordered[i];
                int w = Math.Min(it.Size.Width, right - left);
                int h = Math.Min(it.Size.Height, bottom - top);
                int x = Clamp(it.Anchor.X, left, right - w);
                int y = Math.Max(it.Anchor.Y, lastBottom);
                if (y + h > bottom) y = bottom - h;
                if (y < top) y = top;
                result[it.Surface] = new Rectangle(x, y, w, h);
                lastBottom = Math.Max(lastBottom, y + h + gap);
            }
            return result;
        }

        private void Apply(Dictionary<IAlertSurface, Rectangle> targets, bool immediate)
        {
            int ms = Math.Max(0, Math.Min(500, _cfg.AlertAvoidAnimMs));
            bool instant = immediate || ms <= 0 || !_cfg.UiAnimationsEnabled || UiAnim.Off;

            if (instant)
            {
                StopAnimation();
                foreach (KeyValuePair<IAlertSurface, Rectangle> p in targets)
                {
                    try { p.Key.SetDisplayPosition(p.Value.X, p.Value.Y); }
                    catch { }
                }
                return;
            }

            _anims.Clear();
            foreach (KeyValuePair<IAlertSurface, Rectangle> p in targets)
            {
                try
                {
                    Rectangle cur = p.Key.DisplayBounds;
                    if (cur.X == p.Value.X && cur.Y == p.Value.Y) continue;
                    _anims[p.Key] = new Anim
                    {
                        FromX = cur.X, FromY = cur.Y,
                        ToX = p.Value.X, ToY = p.Value.Y
                    };
                }
                catch { }
            }
            if (_anims.Count == 0)
            {
                StopAnimation();
                return;
            }
            _animMs = ms;
            _animStart = DateTime.UtcNow;
            if (!_timer.Enabled) _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            double t = _animMs <= 0 ? 1 : (DateTime.UtcNow - _animStart).TotalMilliseconds / _animMs;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            double eased = 1 - Math.Pow(1 - t, 3);   // EaseOutCubic
            foreach (KeyValuePair<IAlertSurface, Anim> p in _anims)
            {
                try
                {
                    if (!p.Key.IsPopped) continue;
                    int x = (int)Math.Round(p.Value.FromX + (p.Value.ToX - p.Value.FromX) * eased);
                    int y = (int)Math.Round(p.Value.FromY + (p.Value.ToY - p.Value.FromY) * eased);
                    p.Key.SetDisplayPosition(x, y);
                }
                catch { }
            }
            if (t >= 1) StopAnimation();
        }

        private void StopAnimation()
        {
            _timer.Stop();
            _anims.Clear();
        }

        private static int Clamp(int v, int min, int max)
        {
            if (max < min) return min;
            return v < min ? min : (v > max ? max : v);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAnimation();
            _timer.Dispose();
        }
    }
}
