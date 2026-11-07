using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>一个 OCR 区域是拿来干嘛的。决定扫描时用哪一套识别规则。</summary>
    internal enum OcrRegionKind
    {
        /// <summary>游戏播报（CASSIE / 广播）。默认区域，定时轮询。</summary>
        Broadcast = 0,

        /// <summary>血量类（SCP 血量面板 / 记分板）。</summary>
        Health = 1,

        /// <summary>发电机状态。</summary>
        Generator = 2,

        /// <summary>通用：所有规则都试一遍。</summary>
        Generic = 3,

        /// <summary>
        /// 第 4 批已删除的"屏幕中心角色"框。
        /// 只保留枚举值用于**识别旧配置并忽略它**（数值也不能改，否则后面几种用途会错位）。
        /// 不会再创建、不会再扫描。
        /// </summary>
        Role = 4,

        /// <summary>
        /// 死亡界面上方那组"票数 / 影响力"（需求八）。
        /// 平时不扫，玩家一死才启用；复活或回合结束就停。
        /// 哪个阵营、是大数字还是小数字，看 OcrRegion.Tag。
        /// </summary>
        Ticket = 5,

        /// <summary>右下角三个"静音"开关（死亡时才有，用来判断复活）。</summary>
        Silence = 6,

        /// <summary>左侧玩家/实体列表（死亡时读这一局有哪些 SCP）。</summary>
        ScpList = 7,

        /// <summary>
        /// SCP 血量表（画面中偏右那块 "HP:当前/上限 HS:护盾" 的清单）。
        ///
        /// 【为什么单独一个用途】这块是**白字**，直接普通识别就行，
        /// 不需要票数那套颜色掩膜；而且它有固定格式 "HP:a/b HS:c"，
        /// 用专门的正则解析比通用规则准得多。
        /// Tag 里写要盯哪个 SCP（"scphp_173"），留空就从文字里自己找编号。
        /// </summary>
        ScpHp = 8
    }

    /// <summary>
    /// 一组 OCR 监控区域。
    ///
    /// 不同服务器的 UI 布局不一样：播报固定在左下角，但血量、发电机状态
    /// 可能出现在屏幕任何地方。所以这里做成一个列表：
    ///   · Poll = true 的区域（默认只有播报区）按 OcrPollInterval 定时轮询；
    ///   · 其它区域一律不轮询，只在按“立即扫描”快捷键时截一次。
    ///
    /// Y 有两种含义，用 FromBottom 区分：
    ///   · FromBottom = true  → Y 是“距屏幕底部的边距”（播报区默认就是这种）
    ///   · FromBottom = false → Y 就是屏幕上的绝对纵坐标
    /// </summary>
    internal class OcrRegion
    {
        public string Name = "新区域";

        public int X;
        public int Y = 400;
        public bool FromBottom;
        public int Width = 1000;
        public int Height = 400;

        // ---- 第 3 批：游戏客户区归一化坐标 ----
        // 0..1，基准 = 游戏客户区物理像素（左上角原点，X 向右、Y 向下）。
        // 旧 X/Y/W/H 仍保留为屏幕物理像素，给旧界面和回滚用。
        public double XPercent = double.NaN;
        public double YPercent = double.NaN;
        public double WPercent = double.NaN;
        public double HPercent = double.NaN;

        public bool HasNormalized
        {
            get
            {
                return !double.IsNaN(XPercent) && !double.IsNaN(YPercent)
                    && !double.IsNaN(WPercent) && !double.IsNaN(HPercent)
                    && XPercent >= 0 && XPercent <= 1 && YPercent >= 0 && YPercent <= 1
                    && WPercent > 0 && WPercent <= 1 && HPercent > 0 && HPercent <= 1;
            }
        }

        public bool Enabled = true;

        /// <summary>是否定时轮询。默认只有播报区是 true，其它区域按需扫描。</summary>
        public bool Poll;

        public OcrRegionKind Kind = OcrRegionKind.Generic;

        /// <summary>
        /// 细分用途标记（需求八）：
        ///   "mtf_tokens"      左侧大数字 = MTF 当前可用令牌数
        ///   "mtf_influence"   左侧小数字 = MTF 影响力 / 下一阈值（如 49/80）
        ///   "chaos_tokens"    右侧大数字 = 混沌当前可用令牌数
        ///   "chaos_influence" 右侧小数字 = 混沌影响力 / 下一阈值
        /// 其它区域留空即可。
        /// </summary>
        public string Tag = "";

        public OcrRegion Clone()
        {
            OcrRegion r = new OcrRegion();
            r.Name = Name;
            r.X = X;
            r.Y = Y;
            r.FromBottom = FromBottom;
            r.Width = Width;
            r.Height = Height;
            r.XPercent = XPercent;
            r.YPercent = YPercent;
            r.WPercent = WPercent;
            r.HPercent = HPercent;
            r.Enabled = Enabled;
            r.Poll = Poll;
            r.Kind = Kind;
            r.Tag = Tag;
            return r;
        }

        /// <summary>默认的播报区域：屏幕左下角 1000×400。</summary>
        /// <summary>
        /// 按**区域名字**猜用途。
        ///
        /// 【为什么需要】用户在界面上改坐标时很容易忘了改"用途"，
        /// 结果一堆框全是"通用" —— 角色框不做角色识别、票数框不做票数解析，
        /// 现象就是"专有功能全都不工作"，而且很难看出原因。
        /// 名字里带了关键词就自动纠正（只在用途还是"通用"时才动，不覆盖用户明确选的）。
        /// </summary>
        /// <summary>用途的名字（给扫描报告显示）。</summary>
        public string KindName()
        {
            return OcrRegion.KindName(Kind);
        }

        public static OcrRegionKind GuessKindFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return OcrRegionKind.Generic;
            string n = name;

            if (n.IndexOf("令牌") >= 0 || n.IndexOf("影响力") >= 0 || n.IndexOf("票数") >= 0)
                return OcrRegionKind.Ticket;
            if (n.IndexOf("静音") >= 0) return OcrRegionKind.Silence;
            if (n.IndexOf("SCP 列表") >= 0 || n.IndexOf("SCP列表") >= 0) return OcrRegionKind.ScpList;
            if (n.IndexOf("播报") >= 0) return OcrRegionKind.Broadcast;
            if (n.IndexOf("发电机") >= 0 || n.IndexOf("发电站") >= 0) return OcrRegionKind.Generator;
            if (n.IndexOf("血量") >= 0) return OcrRegionKind.Health;
            return OcrRegionKind.Generic;
        }

        /// <summary>
        /// 右下角那三个"静音"开关（静音存活玩家 / 静音观察者 / 静音音效）。
        /// 玩家死亡时会显示，复活就没有了 —— 所以"三个都认不出来"= 已经复活。
        /// 坐标按用户 1920×1080 实测。
        /// </summary>
        public static OcrRegion NewSilence()
        {
            OcrRegion r = new OcrRegion();
            r.Name = "右下角静音开关";
            r.Tag = "silence";
            r.X = 1640;
            r.Y = 911;
            r.FromBottom = false;
            r.Width = 279;
            r.Height = 167;
            r.Enabled = true;
            r.Poll = false;
            r.Kind = OcrRegionKind.Silence;
            return r;
        }

        /// <summary>
        /// 画面中偏右那块 SCP 血量表（"HP:当前/上限 HS:护盾"）。
        /// 坐标按用户 1920×1080 的实测：列表横向 1030~1480、纵向约 450~520。
        /// 不同服务器这块的位置/行数可能不同 —— 用户自己框一次即可。
        /// </summary>
        public static OcrRegion NewScpHp()
        {
            OcrRegion r = new OcrRegion();
            r.Name = "SCP 血量表";
            r.Tag = "";                  // 留空 = 自动认文字里的 SCP 编号
            r.X = 1030;
            r.Y = 448;
            r.FromBottom = false;
            r.Width = 450;
            r.Height = 78;
            r.Enabled = true;
            // 要定时轮询：掉血是"变化量"，只扫一次算不出伤害。
            // 具体间隔由 OcrAlertWatcher 里的血量表专用节流控制（比播报慢）。
            r.Poll = true;
            r.Kind = OcrRegionKind.ScpHp;
            return r;
        }

        /// <summary>
        /// 屏幕**左侧**的玩家/实体列表（死亡时能看到这一局有哪些 SCP，还能顺带读血量）。
        /// 坐标按用户 1920×1080 实测。
        /// </summary>
        public static OcrRegion NewScpList()
        {
            OcrRegion r = new OcrRegion();
            r.Name = "左侧 SCP 列表";
            r.Tag = "scplist";
            r.X = 14;
            r.Y = 36;
            r.FromBottom = false;
            r.Width = 286;
            r.Height = 1000;
            r.Enabled = true;
            r.Poll = false;
            r.Kind = OcrRegionKind.ScpList;
            return r;
        }

        /// <summary>
        /// 死亡界面那 4 个"票数 / 影响力"框（需求八）。
        /// 坐标是 1920×1080 下的实测值；换分辨率请自己框一次。
        /// </summary>
        public static OcrRegion NewTicket(string name, string tag, int x, int y, int w, int h)
        {
            OcrRegion r = new OcrRegion();
            r.Name = name;
            r.Tag = tag;
            r.X = x;
            r.Y = y;
            r.FromBottom = false;
            r.Width = w;
            r.Height = h;
            r.Enabled = true;
            r.Poll = false;              // 只在玩家死亡期间被拉起来扫
            r.Kind = OcrRegionKind.Ticket;
            return r;
        }

        /// <summary>
        /// 第 4 批已删除：屏幕中心角色框。
        /// 保留这个方法只为兼容旧测试代码，返回的东西不会进入任何配置列表。
        /// </summary>
        public static OcrRegion NewRole()
        {
            OcrRegion r = new OcrRegion();
            r.Name = "屏幕中心角色（已删除）";
            r.Kind = OcrRegionKind.Role;
            r.Enabled = false;
            return r;
        }

        /// <summary>名字/坐标都按屏幕尺寸缩放一份（换分辨率时初始化用）。</summary>
        public static OcrRegion NewBroadcast()
        {
            OcrRegion r = new OcrRegion();
            r.Name = "左下角播报";
            r.X = 0;
            r.Y = 400;
            r.FromBottom = true;
            r.Width = 1000;
            r.Height = 400;
            r.Enabled = true;
            r.Poll = true;
            r.Kind = OcrRegionKind.Broadcast;
            return r;
        }

        /// <summary>
        /// 第 3 批：把旧物理像素折算成游戏客户区 0..1。
        /// 只在配置第一次升级时调用；之后以归一化坐标为准。
        /// </summary>
        public void EnsureNormalized(Rectangle basis)
        {
            if (HasNormalized || basis.Width <= 0 || basis.Height <= 0) return;
            int top = FromBottom ? (basis.Bottom - Y) : Y;
            XPercent = ScreenGeometry.Clamp01((X - basis.Left) / (double)basis.Width);
            YPercent = ScreenGeometry.Clamp01((top - basis.Top) / (double)basis.Height);
            WPercent = ScreenGeometry.Clamp01(Width / (double)basis.Width);
            HPercent = ScreenGeometry.Clamp01(Height / (double)basis.Height);
            if (WPercent <= 0) WPercent = 0.01;
            if (HPercent <= 0) HPercent = 0.01;
        }

        /// <summary>用户用旧界面/框选改了 X/Y/W/H 后清掉归一化，下次保存重新折算。</summary>
        public void ClearNormalized()
        {
            XPercent = double.NaN;
            YPercent = double.NaN;
            WPercent = double.NaN;
            HPercent = double.NaN;
        }

        /// <summary>保存前把归一化坐标回写成当前物理像素，旧界面仍能读到。</summary>
        public void SyncLegacy(Rectangle basis)
        {
            if (!HasNormalized || basis.Width <= 0 || basis.Height <= 0) return;
            Rectangle r = Rect();
            X = r.X;
            Width = r.Width;
            Height = r.Height;
            Y = FromBottom ? Math.Max(0, basis.Bottom - r.Bottom) : r.Y;
        }

        /// <summary>
        /// 换算成屏幕物理像素矩形。
        /// 有归一化值时基准 = 游戏客户区；旧配置没有归一化值时沿用原来的
        /// “主屏绝对像素”，避免升级后旧区域突然跳位。
        /// </summary>
        public Rectangle Rect()
        {
            if (HasNormalized)
            {
                Rectangle basis = ScreenGeometry.GameClientOrPrimary();
                Rectangle r = ScreenGeometry.DenormalizeRect(
                    new NormalizedPoint(XPercent, YPercent), WPercent, HPercent, basis);
                return ClampToBasis(r, basis);
            }
            return RectLegacy();
        }

        private Rectangle RectLegacy()
        {
            Rectangle scr = Screen.PrimaryScreen.Bounds;

            int w = Width;
            int h = Height;
            int x = X;
            int y = FromBottom ? (scr.Height - Y) : Y;

            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (w < 10) w = 10;
            if (h < 10) h = 10;
            if (x > scr.Width - 10) x = Math.Max(0, scr.Width - 10);
            if (y > scr.Height - 10) y = Math.Max(0, scr.Height - 10);
            if (x + w > scr.Width) w = scr.Width - x;    // 别越界
            if (y + h > scr.Height) h = scr.Height - y;

            return new Rectangle(x, y, w, h);
        }

        private static Rectangle ClampToBasis(Rectangle r, Rectangle basis)
        {
            if (basis.Width <= 0 || basis.Height <= 0) return r;
            int x = Math.Max(basis.Left, Math.Min(r.Left, basis.Right - 1));
            int y = Math.Max(basis.Top, Math.Min(r.Top, basis.Bottom - 1));
            int right = Math.Max(x + 1, Math.Min(r.Right, basis.Right));
            int bottom = Math.Max(y + 1, Math.Min(r.Bottom, basis.Bottom));
            return new Rectangle(x, y, right - x, bottom - y);
        }

        /// <summary>给界面 / 日志用的一行描述。</summary>
        public string Describe()
        {
            string anchor = FromBottom ? "Y=底-" + Y.ToString(CultureInfo.InvariantCulture)
                                       : "Y=" + Y.ToString(CultureInfo.InvariantCulture);
            return Name + "   " + X + "," + anchor + "  " + Width + "×" + Height
                 + "  [" + KindName(Kind) + "]"
                 + (Poll ? " 定时" : " 按需")
                 + (Enabled ? "" : " (已停用)");
        }

        public static string KindName(OcrRegionKind k)
        {
            switch (k)
            {
                case OcrRegionKind.Broadcast: return "播报";
                case OcrRegionKind.Health: return "血量";
                case OcrRegionKind.Generator: return "发电机";
                case OcrRegionKind.Role: return "角色（已删除）";
                case OcrRegionKind.Ticket: return "票数";
                case OcrRegionKind.Silence: return "静音开关(复活判断)";
                case OcrRegionKind.ScpList: return "SCP 列表";
                case OcrRegionKind.ScpHp: return "SCP 血量表";
                default: return "通用";
            }
        }

        public static OcrRegionKind ParseKind(string s)
        {
            if (string.IsNullOrEmpty(s)) return OcrRegionKind.Generic;
            switch (s.Trim().ToLowerInvariant())
            {
                case "broadcast": return OcrRegionKind.Broadcast;
                case "health": return OcrRegionKind.Health;
                case "generator": return OcrRegionKind.Generator;
                case "role": return OcrRegionKind.Role;
                case "ticket": return OcrRegionKind.Ticket;
                case "silence": return OcrRegionKind.Silence;
                case "scplist": return OcrRegionKind.ScpList;
                case "scphp": return OcrRegionKind.ScpHp;
                default: return OcrRegionKind.Generic;
            }
        }

        public static string KindKey(OcrRegionKind k)
        {
            switch (k)
            {
                case OcrRegionKind.Broadcast: return "broadcast";
                case OcrRegionKind.Health: return "health";
                case OcrRegionKind.Generator: return "generator";
                case OcrRegionKind.ScpHp: return "scphp";
                default: return "generic";
            }
        }
    }
}
