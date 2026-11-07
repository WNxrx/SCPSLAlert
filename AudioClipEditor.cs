using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 音频裁剪编辑窗口。
    ///
    /// 【这一版的新交互（操作者要求）】
    ///   · 波形下面有一条"片段条"：这个文件**已保存的所有片段**都画在上面，
    ///     重合的片段会上下叠着画（后加的在上面）。
    ///   · 单击某个片段 = 选中它；再单击**重合处** = 换选更下面那个（早一些添加的）。
    ///   · 选中后：按住片段中间拖 = 整体平移；按住左右边缘（±6px）拖 = 伸长 / 缩短。
    ///   · 在空白处（波形或片段条的空位）按住往右拖 = 画一个**新片段**。
    ///   · 「保存」把这个文件的所有片段写回配置，其它文件的片段原样保留。
    ///
    /// 【为什么是"片段"而不是改文件】原文件一个字节都不动：片段只是
    /// "文件名 + 起止秒"，播放时用 MCI 播那一段（`play ... from ... to ...`）。
    /// 起止按**毫秒**传给 MCI，头尾不会再差半秒。
    /// </summary>
    internal class AudioClipEditor : Form
    {
        private readonly string _file;
        private readonly string _fullPath;

        private float[] _peaks;                     // 波形（null = 画时间轴）
        private double _total;                      // 总时长（秒）；0 = 问不出来

        // 这个文件的所有片段（编辑的就是这份内存副本）
        private List<AudioLibrary.Segment> _segs = new List<AudioLibrary.Segment>();
        private int _sel = -1;                      // 选中的片段下标；-1 = 没选

        // 拖动状态：-1 没在拖 / 0 画新片段 / 1 整体平移 / 2 拖左缘 / 3 拖右缘
        private int _dragKind = -1;
        private double _dragAnchorSec;
        private double _dragFrom0, _dragTo0;
        private int _dragIdx = -1;

        // 重合循环点选：记住上一次点击的位置/时刻，短时间内在同一处再点才轮换
        private double _lastClickSec = -1;
        private DateTime _lastClickAt = DateTime.MinValue;

        private Canvas _canvas;
        private TextBox _txtName;
        private Label _lblSel;
        private Label _lblHint;
        private NumericUpDown _numFadeIn;
        private NumericUpDown _numFadeOut;
        private Button _btnDelete;
        private Timer _tick;

        /// <summary>保存下来的片段名（给调用方在列表里选中用；没有新建/改名就是空串）。</summary>
        public string SavedName = "";

        /// <summary>片段表 / 音量表（由「音频」页在打开时塞进来；保存时写回 _segmentsSpec）。</summary>
        public string _segmentsSpec = "";
        public string _volumesSpec = "";

        public AudioClipEditor(string fileName, string fullPath, double knownSeconds)
        {
            _file = fileName;
            _fullPath = fullPath;
            _total = knownSeconds;

            Text = "编辑音频 —— " + fileName;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(860, 620);
            MinimumSize = new Size(660, 520);
            BackColor = UiKit.PageBg;
            Font = UiKit.Font;
            MinimizeBox = false;

            TableLayoutPanel stack = UiKit.Stack();

            TableLayoutPanel gWave;
            UiKit.AddGroup(stack, UiKit.Group("片段条（上）+ 波形（下）", 3, out gWave));
            BuildCanvas(gWave);

            TableLayoutPanel g;
            UiKit.AddGroup(stack, UiKit.Group("选中的片段", 5, out g));
            _lblSel = UiKit.Label("");
            UiKit.Row(g, "范围", _lblSel, "", true);

            _txtName = UiKit.Text(240);
            _txtName.TextChanged += delegate
            {
                if (_sel >= 0 && _sel < _segs.Count) _segs[_sel].Name = _txtName.Text.Trim();
            };
            UiKit.Row(g, "名字", _txtName, "保存后在「音频」页按这个名字用", false);

            _numFadeIn = new NumericUpDown();
            _numFadeIn.DecimalPlaces = 1;
            _numFadeIn.Increment = 0.5m;
            _numFadeIn.Maximum = 30;
            _numFadeIn.Width = 70;
            _numFadeIn.ValueChanged += delegate
            {
                if (_sel >= 0 && _sel < _segs.Count)
                    _segs[_sel].FadeIn = (double)_numFadeIn.Value;
            };

            _numFadeOut = new NumericUpDown();
            _numFadeOut.DecimalPlaces = 1;
            _numFadeOut.Increment = 0.5m;
            _numFadeOut.Maximum = 30;
            _numFadeOut.Width = 70;
            _numFadeOut.ValueChanged += delegate
            {
                if (_sel >= 0 && _sel < _segs.Count)
                    _segs[_sel].FadeOut = (double)_numFadeOut.Value;
            };
            UiKit.Row2(g, "渐入 / 渐出", _numFadeIn, 70, _numFadeOut, "秒（0 = 不渐变）");

            Button btnPlay = UiKit.Button("试听选中", 110);
            btnPlay.Click += delegate { PlaySelected(); };
            Button btnStop = UiKit.Button("停止", 80);
            btnStop.Click += delegate { AudioLibrary.StopCurrent(); };
            _btnDelete = UiKit.Button("删除选中片段", 130);
            _btnDelete.Click += delegate { DeleteSelected(); };
            Button btnSave = UiKit.Button("保存全部片段", 130);
            btnSave.Click += delegate { SaveAll(); };
            Button btnCancel = UiKit.Button("取消", 80);
            btnCancel.Click += delegate { Close(); };

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.WrapContents = false;
            bar.Height = 30;
            bar.Margin = new Padding(0);
            bar.MinimumSize = new Size(10, 30);
            bar.Controls.Add(btnPlay);
            bar.Controls.Add(btnStop);
            bar.Controls.Add(_btnDelete);
            bar.Controls.Add(btnSave);
            bar.Controls.Add(btnCancel);

            // 按钮行横跨「控件 + 说明」两列（否则最后一颗会被说明文字压住，显示不全）
            UiKit.Row(g, "", bar, "", true);
            g.SetColumnSpan(bar, 2);
            bar.BringToFront();          // 说明列那个空标签在 z 序上会更晚加进来，别挡住按钮

            _lblHint = UiKit.Label("");
            _lblHint.ForeColor = UiKit.SubColor;
            _lblHint.Font = UiKit.FontSmall;
            _lblHint.MinimumSize = new Size(10, 34);
            UiKit.Row(g, "", _lblHint, "", true);
            g.SetColumnSpan(_lblHint, 2);

            Controls.Add(stack);

            Load += delegate { LoadAudio(); };
            FormClosed += delegate { AudioLibrary.StopCurrent(); };
        }

        /// <summary>带双缓冲的画布（自己画波形，不双缓冲会闪）。</summary>
        private class Canvas : Panel
        {
            public Canvas() { DoubleBuffered = true; }
        }

        private void BuildCanvas(TableLayoutPanel parent)
        {
            _canvas = new Canvas();
            _canvas.Dock = DockStyle.Fill;
            // 【必须给 MinimumSize】UiKit.Row 的行高 = max(34, MinimumSize.Height + 6)，
            // 不给的话这一行只有 34px 高，画布太矮会把片段条挤到画布外面（画到框外）。
            _canvas.MinimumSize = new Size(200, 230);
            _canvas.BackColor = Color.White;
            _canvas.Cursor = Cursors.Cross;
            _canvas.MouseDown += OnCanvasMouseDown;
            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseUp += OnCanvasMouseUp;
            _canvas.Paint += OnCanvasPaint;
            _canvas.Resize += delegate { _layoutDone = false; _canvas.Invalidate(); };
            UiKit.Row(parent, "", _canvas, "", true);
        }

        // ============================================================
        // 读音频：总长度（必须拿到！）+ 波形 + 已有片段
        //   wav 直接读采样点，很快；mp3 要先解码（约 0.6~1 秒），
        //   所以丢后台线程算，算完再刷画布 —— 不能卡住窗口。
        // ============================================================

        private bool _peaksBusy;

        private void LoadAudio()
        {
            // 先把这个文件已有的片段读出来（操作者要求：显示全部片段）
            List<AudioLibrary.Segment> all = AudioLibrary.ParseSegments(_segmentsSpec);
            _segs = new List<AudioLibrary.Segment>();
            for (int i = 0; i < all.Count; i++)
                if (string.Equals(all[i].File, _file, StringComparison.OrdinalIgnoreCase))
                    _segs.Add(all[i]);

            double sec = AudioLibrary.DurationSeconds(_fullPath);
            if (sec > 0) _total = sec;

            if (_total <= 0)
            {
                _lblHint.Text = "读不出这段音频的长度（这个格式打不开）—— "
                    + "请换 wav / mp3 再试，或者用别的软件先转一下格式。";
                _lblSel.Text = "（这段音频裁不了）";
                _canvas.Invalidate();
                return;
            }

            // 先给个能用的提示（波形可能还在算）
            _lblHint.Text = "共 " + _total.ToString("0.0") + " 秒，已有 " + _segs.Count + " 个片段。"
                + "拖空白 = 新建；点片段 = 选中（重合处再点换下面那个）；选中后拖中间 = 平移，拖两端 = 改边界。";
            SyncSelectionUi();
            _canvas.Invalidate();

            StartPeaksTask();
        }

        /// <summary>后台算波形包络，算完回 UI 线程画。</summary>
        private void StartPeaksTask()
        {
            if (_peaksBusy) return;
            _peaksBusy = true;

            string path = _fullPath;
            int buckets = Math.Max(200, _canvas.ClientSize.Width);
            bool slow = !string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);
            if (slow)
            {
                _lblHint.Text = "正在解码波形…（这个格式要先解码才能画音量）";
                _canvas.Invalidate();
            }

            Action a = delegate
            {
                double sec2 = 0;
                string why = "";
                float[] pk = null;
                try { pk = AudioLibrary.WaveformPeaks(path, buckets, out sec2, out why); }
                catch (Exception ex) { why = ex.Message; }

                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate { ApplyPeaks(pk, sec2, why); });
                }
                catch { }
            };

            if (slow) System.Threading.Tasks.Task.Run(a);
            else a();      // wav 很快，直接算，省得界面闪一下
        }

        private void ApplyPeaks(float[] pk, double sec2, string why)
        {
            _peaksBusy = false;
            if (IsDisposed) return;

            _peaks = pk;
            if (pk != null && sec2 > 0) _total = sec2;

            _lblHint.Text = (pk != null
                    ? "波形已读出：共 " + _total.ToString("0.0") + " 秒，已有 " + _segs.Count + " 个片段。"
                    : "画不出波形（" + why + "），共 " + _total.ToString("0.0") + " 秒，已有 " + _segs.Count + " 个片段。")
                + "拖空白 = 新建；点片段 = 选中（重合处再点换下面那个）；选中后拖中间 = 平移，拖两端 = 改边界。";

            _canvas.Invalidate();
            SyncSelectionUi();
        }

        private int XOf(double s)
        {
            if (_total <= 0) return 0;
            return (int)Math.Round(_canvas.ClientSize.Width * (s / _total));
        }

        private double SecOf(int x)
        {
            if (_total <= 0) return 0;
            int w = Math.Max(1, _canvas.ClientSize.Width);
            if (x < 0) x = 0;
            if (x > w) x = w;
            return _total * x / w;
        }

        // ============================================================
        // 片段条布局：贪心分道，重合的片段各占一条道（后加的道号小 = 画在上面）
        // ============================================================

        private const int RulerH = 18;      // 时间刻度
        private const int KnobH = 18;       // 底部轴圆点余量
        private const int LaneGap = 3;

        private List<int> _lanes;           // 每个片段的道号（与 _segs 同下标）
        private int _laneH = 18;            // 道高（自适应）
        private int _lanesShown = 1;        // 实际画得下的道数
        private int _laneStripH = 21;
        private bool _layoutDone;

        private void AssignLanes()
        {
            if (_lanes == null || _lanes.Count != _segs.Count) _lanes = new List<int>(new int[_segs.Count]);
            List<int> order = new List<int>();
            for (int i = 0; i < _segs.Count; i++) order.Add(i);
            order.Sort(delegate (int a, int b)
            {
                int c = _segs[a].From.CompareTo(_segs[b].From);
                return c != 0 ? c : a.CompareTo(b);
            });

            List<double> laneEnd = new List<double>();
            foreach (int i in order)
            {
                double from = _segs[i].From, to = _segs[i].To > 0 ? _segs[i].To : _total;
                int lane = 0;
                while (lane < laneEnd.Count && from < laneEnd[lane] - 0.01) lane++;
                if (lane == laneEnd.Count) laneEnd.Add(0);
                laneEnd[lane] = to;
                _lanes[i] = lane;
            }
            _layoutDone = false;
        }

        private int LaneCount()
        {
            int max = 0;
            if (_lanes != null) for (int i = 0; i < _lanes.Count; i++) if (_lanes[i] + 1 > max) max = _lanes[i] + 1;
            return Math.Max(1, max);
        }

        /// <summary>
        /// 片段条在**最上面**、波形在下面，道高按画布高度自适应。
        /// 以前是从下往上堆、高度固定，道一多就把片段条顶到画布外面被切掉。
        /// </summary>
        private void EnsureLayout()
        {
            if (_layoutDone && _lanes != null && _lanes.Count == _segs.Count) return;
            AssignLanes();

            int h = Math.Max(90, _canvas.ClientSize.Height);
            int need = Math.Max(1, LaneCount());
            int avail = h - RulerH - KnobH - 46;        // 给波形至少留 46px
            if (avail < 22) avail = 22;

            int lh = avail / need - LaneGap;
            if (lh > 20) lh = 20;
            if (lh < 8) lh = 8;
            _laneH = lh;

            int shown = avail / (lh + LaneGap);
            if (shown < 1) shown = 1;
            if (shown > need) shown = need;
            _lanesShown = shown;
            _laneStripH = shown * (lh + LaneGap);
            _layoutDone = true;
        }

        private int LaneStripH { get { EnsureLayout(); return _laneStripH; } }
        private int WaveTop { get { EnsureLayout(); return _laneStripH + RulerH; } }
        private int WaveBottom
        {
            get
            {
                int b = _canvas.ClientSize.Height - KnobH;
                int t = WaveTop;
                return b < t + 20 ? t + 20 : b;
            }
        }

        /// <summary>某条道的矩形。道号小的画在条带**最下面**（后添加的在上）。</summary>
        private Rectangle LaneRect(int lane)
        {
            EnsureLayout();
            int idx = _lanesShown - 1 - lane;
            if (idx < 0) idx = 0;
            if (idx >= _lanesShown) idx = _lanesShown - 1;
            return new Rectangle(0, idx * (_laneH + LaneGap), _canvas.ClientSize.Width, _laneH);
        }

        private bool InLaneArea(int y) { return y >= 0 && y < LaneStripH; }

        /// <summary>没画出来的道数（片段太多、画布放不下时）。</summary>
        private int HiddenLanes()
        {
            EnsureLayout();
            return Math.Max(0, LaneCount() - _lanesShown);
        }

        /// <summary>x 处压着的片段，后添加的排在前面。</summary>
        private List<int> SegmentsAt(double sec)
        {
            List<int> hit = new List<int>();
            for (int i = 0; i < _segs.Count; i++)
            {
                double from = _segs[i].From, to = _segs[i].To > 0 ? _segs[i].To : _total;
                if (sec >= from && sec <= to) hit.Add(i);
            }
            hit.Sort(delegate (int a, int b) { return b.CompareTo(a); });
            return hit;
        }

        // ============================================================
        // 鼠标
        // ============================================================

        private void OnCanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _total <= 0) return;

            double sec = SecOf(e.X);

            // ---- 片段条区域：点选 / 拖动 ----
            if (InLaneArea(e.Y))
            {
                List<int> hit = SegmentsAt(sec);
                if (hit.Count == 0)
                {
                    // 空位 → 画新片段
                    StartNewBox(sec);
                }
                else
                {
                    // 重合循环点选：快速连点同一处 → 依次换选更早添加的
                    bool sameSpot = _sel >= 0 && hit.Contains(_sel)
                        && Math.Abs(sec - _lastClickSec) < 0.15
                        && (DateTime.Now - _lastClickAt).TotalMilliseconds < 1200;

                    _sel = sameSpot ? hit[(hit.IndexOf(_sel) + 1) % hit.Count] : hit[0];

                    _lastClickSec = sec;
                    _lastClickAt = DateTime.Now;

                    // 选中即可拖：按按下位置决定平移还是改边界
                    AudioLibrary.Segment s = _segs[_sel];
                    double from = s.From, to = s.To > 0 ? s.To : _total;
                    int x1 = XOf(from), x2 = XOf(to);
                    _dragIdx = _sel;
                    if (Math.Abs(e.X - x1) <= 6) { _dragKind = 2; _dragFrom0 = from; _dragTo0 = to; }
                    else if (Math.Abs(e.X - x2) <= 6) { _dragKind = 3; _dragFrom0 = from; _dragTo0 = to; }
                    else { _dragKind = 1; _dragAnchorSec = sec; _dragFrom0 = from; _dragTo0 = to; }
                }

                SyncSelectionUi();
                _canvas.Invalidate();
                return;
            }

            // ---- 波形区域：一律画新片段（操作者要求：按住从左往右滑 = 增加新的）----
            StartNewBox(sec);
            _canvas.Invalidate();
        }

        private void StartNewBox(double sec)
        {
            _dragKind = 0;
            _dragAnchorSec = sec;
            // 【必须在这里初始化 ghost 的范围】以前只在 MouseMove 里赋值，
            // 于是"按一下不拖"（单纯点一下空白）会用**上一次拖动**留下的
            // _dragFrom0/_dragTo0 建出一个位置莫名其妙的片段。
            _dragFrom0 = sec;
            _dragTo0 = sec;
            _dragIdx = -1;
            _sel = -1;                    // 画新的过程中不选中旧的
            SyncSelectionUi();
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (_total <= 0) return;

            if (_dragKind < 0)
            {
                // 只是移动鼠标 → 变个光标提示"这里能拖"
                Cursor cur;
                if (InLaneArea(e.Y))
                {
                    List<int> hit = SegmentsAt(SecOf(e.X));
                    if (hit.Count == 0) cur = Cursors.Cross;
                    else
                    {
                        AudioLibrary.Segment s = _segs[hit[0]];
                        double from = s.From, to = s.To > 0 ? s.To : _total;
                        int x1 = XOf(from), x2 = XOf(to);
                        cur = (Math.Abs(e.X - x1) <= 6 || Math.Abs(e.X - x2) <= 6)
                            ? Cursors.SizeWE : Cursors.Hand;
                    }
                }
                else cur = Cursors.Cross;
                if (_canvas.Cursor != cur) _canvas.Cursor = cur;
                return;
            }

            double at = SecOf(e.X);
            const double minLen = 0.05;

            if (_dragKind == 0)
            {
                // 画新片段：实时反映在画布上（用一个临时的"ghost"选中段）
                double f = Math.Min(_dragAnchorSec, at);
                double t = Math.Max(_dragAnchorSec, at);
                if (t - f < minLen) t = Math.Min(_total, f + minLen);
                // ghost 存进 drag 专用字段，鼠标抬起才真正加进 _segs
                _dragFrom0 = f; _dragTo0 = t;
            }
            else if (_dragIdx >= 0 && _dragIdx < _segs.Count)
            {
                AudioLibrary.Segment s = _segs[_dragIdx];
                if (_dragKind == 1)
                {
                    double delta = at - _dragAnchorSec;
                    double len = _dragTo0 - _dragFrom0;
                    double f = _dragFrom0 + delta;
                    if (f < 0) f = 0;
                    if (f + len > _total) f = _total - len;
                    if (f < 0) f = 0;
                    s.From = f;
                    s.To = f + len;
                }
                else if (_dragKind == 2)
                {
                    double f = Math.Max(0, Math.Min(at, _dragTo0 - minLen));
                    s.From = f;
                }
                else if (_dragKind == 3)
                {
                    double t = Math.Min(_total, Math.Max(at, s.From + minLen));
                    s.To = t;
                }
            }

            UpdateSelectionLabel();
            _canvas.Invalidate();
        }

        private void OnCanvasMouseUp(object sender, MouseEventArgs e)
        {
            if (_dragKind < 0) return;

            if (_dragKind == 0)
            {
                // 新片段落子：太短的就给个 0.5 秒的默认长度
                double f = _dragFrom0, t = _dragTo0;
                if (t - f < 0.05) t = Math.Min(_total, f + 0.5);

                AudioLibrary.Segment s = new AudioLibrary.Segment();
                s.File = _file;
                s.From = Math.Round(f, 2);
                s.To = Math.Round(Math.Min(t, _total), 2);
                s.Name = NewSegmentName();
                _segs.Add(s);
                _sel = _segs.Count - 1;
                SavedName = s.Name;
                _txtName.Text = s.Name;      // 触发 TextChanged 也没关系（同一值）
                _txtName.Focus();
                _txtName.SelectAll();
            }
            else if (_dragIdx >= 0 && _dragIdx < _segs.Count)
            {
                AudioLibrary.Segment s = _segs[_dragIdx];
                s.From = Math.Round(s.From, 2);
                s.To = Math.Round(Math.Min(s.To, _total), 2);
                if (s.To - s.From < 0.05) s.To = Math.Min(_total, s.From + 0.05);
                _sel = _dragIdx;
                SavedName = s.Name;
            }

            _dragKind = -1;
            _dragIdx = -1;
            SyncSelectionUi();
            _canvas.Invalidate();
        }

        private string NewSegmentName()
        {
            int n = 1;
            while (true)
            {
                string cand = "片段" + n;
                bool dup = false;
                for (int i = 0; i < _segs.Count; i++)
                    if (string.Equals(_segs[i].Name, cand, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                if (!dup) return cand;
                n++;
            }
        }

        private void DeleteSelected()
        {
            if (_sel < 0 || _sel >= _segs.Count) return;
            string name = _segs[_sel].Name;
            if (MessageBox.Show("确定删掉片段「" + name + "」吗？（只删片段，不动原文件）", "音频",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _segs.RemoveAt(_sel);
            _sel = -1;
            SyncSelectionUi();
            _canvas.Invalidate();
        }

        /// <summary>把选中片段的资料刷到下面的输入框 / 标签上。</summary>
        private void SyncSelectionUi()
        {
            bool has = _sel >= 0 && _sel < _segs.Count;
            _btnDelete.Enabled = has;

            if (_txtName.Text != (has ? _segs[_sel].Name : ""))
            {
                // 更新名字框（注意别在用户打字时被刷回去：只有值不一样才动）
                _txtName.Text = has ? _segs[_sel].Name : "";
            }
            if (has)
            {
                SetNum(_numFadeIn, (decimal)_segs[_sel].FadeIn);
                SetNum(_numFadeOut, (decimal)_segs[_sel].FadeOut);
            }
            else
            {
                SetNum(_numFadeIn, 0);
                SetNum(_numFadeOut, 0);
            }
            UpdateSelectionLabel();
        }

        private static void SetNum(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) v = n.Minimum;
            if (v > n.Maximum) v = n.Maximum;
            if (n.Value != v) n.Value = v;
        }

        private void UpdateSelectionLabel()
        {
            if (_lblSel == null || _total <= 0) return;

            if (_dragKind == 0)
            {
                _lblSel.Text = "（正在画新片段：" + _dragFrom0.ToString("0.00") + " ~ "
                    + _dragTo0.ToString("0.00") + " 秒，松手完成）";
                return;
            }
            if (_sel < 0 || _sel >= _segs.Count)
            {
                _lblSel.Text = "（没有选中：点片段条上的一个片段，或在空白处拖一个新片段）";
                return;
            }

            AudioLibrary.Segment s = _segs[_sel];
            _lblSel.Text = s.From.ToString("0.00") + " 秒 ～ "
                + (s.To > 0 ? s.To.ToString("0.00") : _total.ToString("0.00"))
                + " 秒（共 " + ((s.To > 0 ? s.To : _total) - s.From).ToString("0.00") + " 秒）";
        }

        // ============================================================
        // 画
        // ============================================================

        private void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _canvas.ClientSize.Width;
            int h = _canvas.ClientSize.Height;

            EnsureLayout();
            int stripH = _laneStripH;
            int rulerTop = stripH;
            int waveTop = stripH + RulerH;
            int waveBottom = WaveBottom;
            int mid = (waveTop + waveBottom) / 2;

            using (Brush bg = new SolidBrush(Color.White)) g.FillRectangle(bg, 0, 0, w, h);

            if (_total <= 0)
            {
                using (Brush b = new SolidBrush(Color.FromArgb(120, 120, 120)))
                    g.DrawString("（读不出这段音频的长度）", UiKit.Font, b, 8, mid - 8);
                return;
            }

            // ---- 片段条（最上面）----
            for (int lane = 0; lane < _lanesShown; lane++)
            {
                Rectangle r = LaneRect(lane);
                using (Brush b = new SolidBrush(Color.FromArgb(246, 248, 251)))
                    g.FillRectangle(b, r);
                using (Pen p = new Pen(Color.FromArgb(228, 232, 238)))
                    g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }

            // ---- 时间刻度 ----
            double step = StepFor(_total);
            using (Pen tick = new Pen(Color.FromArgb(210, 215, 222)))
            using (Brush tb = new SolidBrush(Color.FromArgb(90, 95, 105)))
            {
                for (double s = 0; s <= _total + 0.001; s += step)
                {
                    int x = XOf(s);
                    g.DrawLine(tick, x, rulerTop + 4, x, waveBottom);
                    g.DrawString(Clock(s), UiKit.FontSmall, tb, Math.Min(x + 3, w - 32), rulerTop + 1);
                }
            }

            // ---- 波形 ----
            if (_peaks != null && _peaks.Length > 0)
            {
                bool selSpan = _sel >= 0 && _sel < _segs.Count;
                double sf = selSpan ? _segs[_sel].From : -1;
                double st = selSpan ? (_segs[_sel].To > 0 ? _segs[_sel].To : _total) : -1;

                for (int x = 0; x < w; x++)
                {
                    int idx = (int)((long)x * _peaks.Length / Math.Max(1, w));
                    if (idx < 0 || idx >= _peaks.Length) continue;
                    int amp = (int)Math.Round(_peaks[idx] * ((waveBottom - waveTop) / 2 - 4));
                    if (amp < 1) amp = 1;

                    bool inside = selSpan && x >= XOf(sf) && x <= XOf(st);
                    using (Brush b = new SolidBrush(inside
                        ? Color.FromArgb(90, 140, 250)
                        : Color.FromArgb(190, 195, 205)))
                        g.FillRectangle(b, x, mid - amp, 1, amp * 2);
                }
            }
            else
            {
                using (Pen p = new Pen(Color.FromArgb(225, 228, 235)))
                    g.DrawLine(p, 0, mid, w, mid);
            }

            // ---- 选中片段在波形上的底色 ----
            if (_sel >= 0 && _sel < _segs.Count)
            {
                AudioLibrary.Segment s = _segs[_sel];
                int x1 = XOf(s.From), x2 = XOf(s.To > 0 ? s.To : _total);
                using (Brush sel = new SolidBrush(Color.FromArgb(26, 90, 140, 250)))
                    g.FillRectangle(sel, x1, waveTop, Math.Max(1, x2 - x1), waveBottom - waveTop);
            }

            // ---- 片段条上的圆角条 ----
            for (int i = 0; i < _segs.Count; i++)
            {
                if (_lanes == null || i >= _lanes.Count) break;
                if (_lanes[i] >= _lanesShown) continue;
                DrawSegmentBar(g, i, _lanes[i]);
            }

            // 放不下的道数提示
            int hidden = HiddenLanes();
            if (hidden > 0)
            {
                string tip = "还有 " + hidden + " 条重叠片段没画出来";
                SizeF sz = g.MeasureString(tip, UiKit.FontSmall);
                using (Brush b = new SolidBrush(Color.FromArgb(160, 120, 128, 142)))
                    g.DrawString(tip, UiKit.FontSmall, b, w - sz.Width - 6, stripH - sz.Height - 2);
            }

            // ---- 正在画的新片段（ghost）----
            if (_dragKind == 0)
            {
                int x1 = XOf(_dragFrom0), x2 = XOf(Math.Max(_dragTo0, _dragFrom0 + 0.01));
                Rectangle r = LaneRect(0);
                using (Brush b = new SolidBrush(Color.FromArgb(70, 90, 140, 250)))
                    g.FillRectangle(b, x1, r.Y, Math.Max(2, x2 - x1), r.Height);
                using (Pen p = new Pen(Color.FromArgb(70, 130, 250), 2))
                    g.DrawRectangle(p, x1, r.Y, Math.Max(2, x2 - x1), r.Height - 1);
            }

            // ---- 选中片段的左右轴 ----
            if (_sel >= 0 && _sel < _segs.Count)
            {
                AudioLibrary.Segment s = _segs[_sel];
                DrawHandle(g, XOf(s.From), waveTop, waveBottom);
                DrawHandle(g, XOf(s.To > 0 ? s.To : _total), waveTop, waveBottom);
            }

            // ---- 播放位置 ----
            double pos = AudioLibrary.CurrentPositionSeconds();
            if (pos > 0 && pos <= _total)
            {
                int x = XOf(pos);
                using (Pen p = new Pen(Color.FromArgb(60, 190, 120), 1))
                    g.DrawLine(p, x, waveTop, x, waveBottom);
            }
        }

        private void DrawHandle(Graphics g, int x, int top, int bottom)
        {
            Color c = Color.FromArgb(70, 130, 250);
            using (Pen p = new Pen(c, 2))
                g.DrawLine(p, x, top, x, bottom);
            int r = 5;
            using (Brush b = new SolidBrush(c))
                g.FillEllipse(b, x - r, bottom - r, r * 2, r * 2);
            using (Pen p = new Pen(Color.White, 2))
                g.DrawEllipse(p, x - r, bottom - r, r * 2, r * 2);
        }

        private void DrawSegmentBar(Graphics g, int idx, int lane)
        {
            AudioLibrary.Segment s = _segs[idx];
            int x1 = XOf(s.From);
            int x2 = XOf(s.To > 0 ? s.To : _total);
            if (x2 - x1 < 3) x2 = x1 + 3;

            Rectangle r = LaneRect(lane);
            Rectangle bar = new Rectangle(x1, r.Y + LaneGap / 2, x2 - x1, r.Height - LaneGap / 2 - 1);
            if (bar.Width < 2) bar.Width = 2;

            bool sel = idx == _sel;
            using (GraphicsPath path = Rounded(bar, 4))
            using (Brush b = new SolidBrush(sel ? Color.FromArgb(70, 130, 250) : Color.FromArgb(150, 185, 250)))
                g.FillPath(b, path);
            using (GraphicsPath path = Rounded(bar, 4))
            using (Pen p = new Pen(sel ? Color.FromArgb(40, 90, 210) : Color.FromArgb(110, 150, 230)))
                g.DrawPath(p, path);

            // 名字（放得下才画）
            string label = s.Name;
            if (!string.IsNullOrEmpty(label) && bar.Width > 30)
            {
                SizeF sz = g.MeasureString(label, UiKit.FontSmall);
                if (sz.Width < bar.Width - 6)
                    g.DrawString(label, UiKit.FontSmall, Brushes.White, bar.X + 4,
                        bar.Y + (bar.Height - sz.Height) / 2);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (rad > r.Width / 2) rad = Math.Max(1, r.Width / 2);
            if (rad > r.Height / 2) rad = Math.Max(1, r.Height / 2);
            p.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
            p.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
            p.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
            p.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>刻度间隔：让刻度别太密也别太稀。</summary>
        private static double StepFor(double total)
        {
            if (total <= 15) return 1;
            if (total <= 60) return 5;
            if (total <= 180) return 20;
            if (total <= 600) return 60;
            return 120;
        }

        private static string Clock(double sec)
        {
            int s = (int)Math.Round(sec);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // ============================================================
        // 播放 / 保存
        // ============================================================

        private void PlaySelected()
        {
            if (_sel < 0 || _sel >= _segs.Count)
            {
                MessageBox.Show("先点片段条上的一个片段选中它（或在空白处拖一个新片段）。", "音频",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AudioLibrary.Segment s = _segs[_sel];
            AudioLibrary.SegmentsSpec = _segmentsSpec;
            AudioLibrary.VolumesSpec = _volumesSpec;
            int vol = AudioLibrary.VolumeOf(_volumesSpec, AudioLibrary.SegmentPrefix + s.Name);
            if (!AudioLibrary.PlayMs(s.File, "编辑窗口试听",
                    vol,
                    (int)Math.Round(s.From * 1000),
                    s.To > 0 ? (int)Math.Round(s.To * 1000) : 0,
                    s.FadeIn, s.FadeOut))
            {
                if (AudioLibrary.LastError.Length > 0 && AudioLibrary.LastError.IndexOf("找不到") < 0)
                    _lblHint.Text = "播不出来：" + AudioLibrary.LastError;
            }

            // 播放位置那根绿线要动起来
            if (_tick == null)
            {
                _tick = new Timer();
                _tick.Interval = 120;
                _tick.Tick += delegate { if (!IsDisposed && _canvas != null) _canvas.Invalidate(); };
            }
            _tick.Start();
        }

        /// <summary>
        /// 保存：本文件的片段整体替换回 spec，其它文件的片段原样保留。
        /// </summary>
        private void SaveAll()
        {
            // 名字查重（同一文件里不许重名，否则分配时分不清）
            for (int i = 0; i < _segs.Count; i++)
                for (int j = i + 1; j < _segs.Count; j++)
                    if (string.Equals(_segs[i].Name.Trim(), _segs[j].Name.Trim(),
                            StringComparison.OrdinalIgnoreCase)
                        && _segs[i].Name.Trim().Length > 0)
                    {
                        MessageBox.Show("有两个片段都叫「" + _segs[i].Name.Trim() + "」，改一下名字再保存。", "音频",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
            for (int i = 0; i < _segs.Count; i++)
                if (_segs[i].Name.Trim().Length == 0)
                {
                    MessageBox.Show("有一个片段还没起名字（在「片段名字」里填一下）。", "音频",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

            List<AudioLibrary.Segment> all = AudioLibrary.ParseSegments(_segmentsSpec);
            all.RemoveAll(delegate (AudioLibrary.Segment x)
            {
                return string.Equals(x.File, _file, StringComparison.OrdinalIgnoreCase);
            });
            all.AddRange(_segs);

            _segmentsSpec = AudioLibrary.JoinSegments(all);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_tick != null) { try { _tick.Stop(); } catch { } }
            AudioLibrary.StopCurrent();
            base.OnFormClosing(e);
        }
    }
}
