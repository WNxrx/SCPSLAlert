using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 战术数据小面板（操作者要求）：贴在战术面板**正下方**、宽度一样的四个小框。
    /// 数据和战术面板同源（同一份 TacticalLog），让用户一眼看清 OCR 扫到了什么：
    ///
    ///   ① 扫描到的 SCP      ② 重生令牌      ③ SCP 血量      ④ 自定义框
    ///
    /// 第 4 个框显示什么由设置里选（击杀数 / 总影响力 / 发电机 / 总血量）。
    ///
    /// 跟随战术面板一起开关：大面板不在了它也自己收起来。
    /// 鼠标穿透 + 不抢焦点（纯展示，不挡游戏操作），并对截图不可见
    ///（免得被自己的 OCR 又认一遍）。
    /// </summary>
    internal class TacticalMiniPanel : Form
    {
        private readonly Config _cfg;
        private readonly TacticalLog _log;
        private TacticalPanel _big;

        private readonly Timer _tick;
        private Bitmap _buf;
        private string _lastMode = "";

        private static readonly Font TitleFont =
            new Font("微软雅黑", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
        private static readonly Font TextFont =
            new Font("微软雅黑", 9f, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly Font SmallFont =
            new Font("微软雅黑", 8f, FontStyle.Regular, GraphicsUnit.Point);

        private const int PanelH = 150;

        public TacticalMiniPanel(Config cfg, TacticalLog log)
        {
            _cfg = cfg;
            _log = log;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(20, 24, 32);
            DoubleBuffered = true;
            Size = new Size(1544, PanelH);

            _tick = new Timer();
            _tick.Interval = 500;
            _tick.Tick += delegate { OnTickUi(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020;   // WS_EX_TRANSPARENT  鼠标穿透
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE   点了也不激活
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW   不进 Alt+Tab
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.HideFromCapture(Handle, "战术数据小面板");
        }

        /// <summary>绑定大面板：跟它的位置/宽度/可见性走。</summary>
        public void Attach(TacticalPanel big)
        {
            _big = big;
            _tick.Start();
        }

        private void OnTickUi()
        {
            try
            {
                // 设置里关了 / 大面板没了 → 自己也收起来
                bool enabled = _cfg == null || _cfg.GetTactical("mini_enabled") != "0";
                bool bigVisible = _big != null && !_big.IsDisposed && _big.Visible;
                if (!enabled || !bigVisible)
                {
                    if (Visible) Hide();
                    return;
                }

                Reposition();
                string mode = _cfg == null ? "" : (_cfg.GetTactical("mini_box4") ?? "");
                if (!Visible) Show();
                BringToFront();
                if (mode != _lastMode || _buf == null || _buf.Width != Width)
                {
                    _lastMode = mode;
                    RebuildBuffer();
                }
                Redraw();
            }
            catch { }
        }

        /// <summary>摆到大面板正下方，宽度对齐。</summary>
        private void Reposition()
        {
            if (_big == null || _big.IsDisposed) return;
            int w = _big.Width;
            if (Width != w)
            {
                Width = w;
                RebuildBuffer();
            }
            Location = new Point(_big.Left, _big.Bottom + 6);
        }

        private void RebuildBuffer()
        {
            if (_buf != null) { try { _buf.Dispose(); } catch { } }
            _buf = new Bitmap(Math.Max(200, Width), PanelH);
        }

        private void Redraw()
        {
            if (_buf == null) RebuildBuffer();
            using (Graphics g = Graphics.FromImage(_buf))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.FromArgb(20, 24, 32));

                int w = _buf.Width;
                int h = PanelH;
                using (GraphicsPath gp = AlertRenderer.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), 12))
                {
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(240, 26, 30, 38)))
                        g.FillPath(sb, gp);
                    using (Pen p = new Pen(Color.FromArgb(64, 255, 255, 255), 1f))
                        g.DrawPath(p, gp);
                }
                using (SolidBrush accent = new SolidBrush(Color.FromArgb(90, 190, 255)))
                    g.FillRectangle(accent, 2, 2, 3, h - 4);

                // ---- 四个框：SCP 框宽一点（要放列表），其余均分 ----
                int pad = 12;
                int gap = 8;
                int inner = w - pad * 2;
                if (inner < 4 * 60 + gap * 3)
                {
                    // 面板被拖得很窄（或屏幕太小）：退化成四等分，并保证每格有正宽度，
                    // 免得算出来的宽度是 0 / 负数，GDI+ 画圆角矩形会报错。
                    gap = 4;
                    pad = 4;
                    inner = Math.Max(240, w - pad * 2);
                }
                int wScp = (int)(inner * 0.34);
                int wRest = Math.Max(48, (inner - wScp - gap * 3) / 3);
                int bx = pad;
                int by = 10;
                int bh = Math.Max(40, h - by - 10);

                DrawBox(g, bx, by, wScp, bh, "扫描到的 SCP", ScpText(), SmallFont);
                bx += wScp + gap;
                DrawBox(g, bx, by, wRest, bh, "重生令牌",
                    "MTF " + _log.Tickets.TokensOf(TacticalLog.MTF)
                    + "\n混沌 " + _log.Tickets.TokensOf(TacticalLog.CHAOS), TextFont);
                bx += wRest + gap;
                DrawBox(g, bx, by, wRest, bh, "SCP 血量", HpText(), SmallFont);
                bx += wRest + gap;
                DrawBox(g, bx, by, wRest, bh, Box4Title(), Box4Text(), SmallFont);
            }
            Invalidate();
        }

        private void DrawBox(Graphics g, int x, int y, int w, int h, string title, string text, Font f)
        {
            Rectangle r = new Rectangle(x, y, w, h);
            using (GraphicsPath gp = AlertRenderer.RoundedRect(r, 8))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 44, 50, 62)))
                    g.FillPath(b, gp);
                using (Pen p = new Pen(Color.FromArgb(50, 255, 255, 255), 1f))
                    g.DrawPath(p, gp);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 160, 175)))
                g.DrawString(title, TitleFont, b, x + 8, y + 5);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(228, 232, 240)))
            {
                RectangleF tr = new RectangleF(x + 8, y + 24, w - 14, h - 30);
                g.DrawString(string.IsNullOrEmpty(text) ? "（暂无数据）" : text, f, b, tr);
            }
        }

        /// <summary>框①：扫描到的 SCP（在场 + 阵亡标记）。</summary>
        private string ScpText()
        {
            try
            {
                string alive = "", dead = "";
                for (int i = 0; i < _log.Records.Count; i++)
                {
                    TacticalLog.ScpRecord r = _log.Records[i];
                    if (!r.Present) continue;
                    if (r.Killed) dead += (dead.Length > 0 ? "、" : "") + r.Code;
                    else alive += (alive.Length > 0 ? "、" : "") + r.Code;
                }
                if (alive.Length == 0 && dead.Length == 0) return "";
                return (alive.Length > 0 ? "存活：" + alive : "存活：无")
                    + (dead.Length > 0 ? "\n阵亡：" + dead : "");
            }
            catch { return ""; }
        }

        /// <summary>框③：在场 SCP 的血量（当前/上限）。</summary>
        private string HpText()
        {
            try
            {
                string s = "";
                for (int i = 0; i < _log.Records.Count; i++)
                {
                    TacticalLog.ScpRecord r = _log.Records[i];
                    if (!r.Present || r.Killed) continue;
                    int cur = r.CurrentHp >= 0 ? r.CurrentHp : r.TotalHp;
                    s += (s.Length > 0 ? "\n" : "") + r.Code + "  " + cur
                        + (r.TotalHp > 0 ? "/" + r.TotalHp : "");
                }
                return s;
            }
            catch { return ""; }
        }

        // ---- 框④：显示内容由设置里的下拉选（config [Tactical] mini_box4）----
        private string Box4Mode()
        {
            string m = _cfg == null ? null : _cfg.GetTactical("mini_box4");
            return string.IsNullOrEmpty(m) ? "kills" : m;
        }

        private string Box4Title()
        {
            switch (Box4Mode())
            {
                case "tickets": return "总影响力";
                case "gen": return "发电机";
                case "damage": return "已打掉血量";
                default: return "击杀数";
            }
        }

        private string Box4Text()
        {
            try
            {
                switch (Box4Mode())
                {
                    case "tickets":
                        return _log.TotalTickets.ToString() + " 票\n（MTF + 混沌合计）";
                    case "gen":
                        {
                            int done = _log.GeneratorsActive, run = _log.GeneratorsRunning;
                            return done + " / " + TacticalLog.GeneratorCount + " 已启动"
                                + (run > 0 ? "\n" + run + " 台在计时" : "");
                        }
                    case "damage":
                        return _log.TotalDamage.ToString() + " 点";
                    default:
                        return _log.TotalKills.ToString() + " 个 SCP 阵亡";
                }
            }
            catch { return ""; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_buf != null) e.Graphics.DrawImageUnscaled(_buf, 0, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _tick.Stop(); _tick.Dispose(); } catch { }
                if (_buf != null) { try { _buf.Dispose(); } catch { } }
            }
            base.Dispose(disposing);
        }
    }
}
