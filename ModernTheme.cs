using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SCPSLAlert
{
    internal sealed class UiColors
    {
        public Color PageBg, CardBg, CardBorder, MutedBg, NavBg, NavHover, NavSelected, NavText;
        public Color HeaderBg, HeaderText, HeaderSub, FooterBg, Text, SubText, Border;
        public Color Accent, AccentHover, AccentSoft, InputBg, InputBorder, TrackOff, Knob, Danger;

        public static UiColors Light()
        {
            UiColors c = new UiColors();
            c.PageBg = Color.FromArgb(238, 243, 244);
            c.CardBg = Color.FromArgb(250, 252, 251);
            c.CardBorder = Color.FromArgb(211, 223, 222);
            c.MutedBg = Color.FromArgb(242, 247, 246);
            c.NavBg = Color.FromArgb(231, 238, 237);
            c.NavHover = Color.FromArgb(220, 232, 230);
            c.NavSelected = Color.FromArgb(207, 233, 229);
            c.NavText = Color.FromArgb(57, 78, 78);
            c.HeaderBg = Color.FromArgb(250, 252, 251);
            c.HeaderText = Color.FromArgb(27, 43, 46);
            c.HeaderSub = Color.FromArgb(101, 123, 124);
            c.FooterBg = Color.FromArgb(246, 249, 248);
            c.Text = Color.FromArgb(28, 43, 46);
            c.SubText = Color.FromArgb(101, 123, 124);
            c.Border = Color.FromArgb(205, 218, 217);
            c.Accent = ThemeTokens.For(true);
            c.AccentHover = ThemeTokens.Lighten(c.Accent, 0.08f);
            c.AccentSoft = ThemeTokens.Blend(c.Accent, Color.White, 0.85f);
            c.InputBg = Color.FromArgb(255, 255, 255);
            c.InputBorder = Color.FromArgb(193, 211, 209);
            c.TrackOff = Color.FromArgb(173, 193, 191);
            c.Knob = Color.FromArgb(255, 255, 255);
            c.Danger = Color.FromArgb(190, 74, 67);
            return c;
        }

        public static UiColors Dark()
        {
            UiColors c = new UiColors();
            c.PageBg = Color.FromArgb(15, 22, 26);
            c.CardBg = Color.FromArgb(24, 34, 38);
            c.CardBorder = Color.FromArgb(43, 61, 64);
            c.MutedBg = Color.FromArgb(20, 30, 33);
            c.NavBg = Color.FromArgb(17, 25, 28);
            c.NavHover = Color.FromArgb(29, 43, 45);
            c.NavSelected = Color.FromArgb(26, 58, 56);
            c.NavText = Color.FromArgb(174, 194, 192);
            c.HeaderBg = Color.FromArgb(21, 31, 34);
            c.HeaderText = Color.FromArgb(235, 244, 241);
            c.HeaderSub = Color.FromArgb(139, 164, 161);
            c.FooterBg = Color.FromArgb(18, 27, 30);
            c.Text = Color.FromArgb(229, 239, 236);
            c.SubText = Color.FromArgb(143, 166, 163);
            c.Border = Color.FromArgb(43, 60, 63);
            c.Accent = ThemeTokens.For(false);
            c.AccentHover = ThemeTokens.Lighten(c.Accent, 0.10f);
            c.AccentSoft = ThemeTokens.Blend(c.Accent, Color.FromArgb(20, 30, 33), 0.85f);
            c.InputBg = Color.FromArgb(27, 40, 43);
            c.InputBorder = Color.FromArgb(53, 75, 76);
            c.TrackOff = Color.FromArgb(62, 83, 83);
            c.Knob = Color.FromArgb(237, 247, 243);
            c.Danger = Color.FromArgb(240, 111, 101);
            return c;
        }
    }

    internal static class ModernTheme
    {
        public static bool Active;
        public static bool? ForcedDark;
        public static bool AnimationsEnabled = true;

        private static UiColors _palette = UiColors.Light();
        private static bool _dark;
        private static Timer _watch;
        private static bool _raised;
        private static readonly HashSet<Form> ModernForms = new HashSet<Form>();

        static ModernTheme()
        {
            // 强调色改了：重建调色板并让 WPF 侧跟着刷新。
            ThemeTokens.Changed += delegate
            {
                _raised = false;
                Refresh();
            };
        }

        public static event Action Changed;

        /// <summary>主题选择：-1 跟随系统，0 浅色，1 深色。</summary>
        public static int Mode = -1;

        public static bool IsDark { get { return _dark; } }
        public static UiColors P { get { return _palette; } }

        public static bool IsModernHost(Control c)
        {
            Control p = c;
            while (p != null)
            {
                Form form = p as Form;
                if (form != null && ModernForms.Contains(form)) return true;
                if (p is ModernSettingsForm || p is ModernStartView) return true;
                p = p.Parent;
            }
            return false;
        }

        public static void MarkModern(Form form)
        {
            if (form == null) return;
            ModernForms.Add(form);
        }

        public static void UnmarkModern(Form form)
        {
            if (form == null) return;
            ModernForms.Remove(form);
        }

        public static int FrameInterval
        {
            get
            {
                return 33;
            }
        }

        public static readonly Font InputFont =
            new Font("微软雅黑", 10f, FontStyle.Regular, GraphicsUnit.Point);

        public static UiColors For(Control c)
        {
            return IsModernHost(c) ? _palette : UiColors.Light();
        }

        public static Color ContentBack(Control c)
        {
            UiColors p = For(c);
            Control parent = c == null ? null : c.Parent;
            while (parent != null)
            {
                if (parent is ModernExpander) return p.CardBg;
                if (parent is ModernSettingsForm || parent is ModernStartView) break;
                Color back = parent.BackColor;
                if (back.A != 0 && back != Color.Transparent) return back;
                parent = parent.Parent;
            }
            return p.PageBg;
        }

        public static void Start()
        {
            Refresh();
            if (_watch != null) return;
            _watch = new Timer();
            _watch.Interval = 1200;
            _watch.Tick += delegate { Refresh(); };
            _watch.Start();
        }

        public static void Stop()
        {
            if (_watch == null) return;
            _watch.Stop();
            _watch.Dispose();
            _watch = null;
        }

        public static void Refresh()
        {
            bool dark = ForcedDark.HasValue ? ForcedDark.Value
                : (Mode >= 0 ? Mode == 1 : DetectDark());
            if (dark == _dark && _raised) return;
            _dark = dark;
            _palette = dark ? UiColors.Dark() : UiColors.Light();
            _raised = true;
            if (Changed != null) Changed();
        }

        public static void SetMode(int mode)
        {
            Mode = mode < -1 ? -1 : (mode > 1 ? 1 : mode);
            Refresh();
        }

        public static void RaiseChanged()
        {
            if (Changed != null) Changed();
        }

        public static bool DetectDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return false;
                    object v = key.GetValue("AppsUseLightTheme");
                    if (v == null) return false;
                    return Convert.ToInt32(v) == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static Color Mix(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c);
        }

        public static double Luma(Color c)
        {
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        }

        public static bool IsNear(Color a, Color b, int tolerance)
        {
            return Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance
                && Math.Abs(a.B - b.B) <= tolerance;
        }

        public static Color SurfaceFor(Color original)
        {
            UiColors p = _palette;
            if (original.A == 0) return original;
            double l = Luma(original);
            if (!_dark)
            {
                if (l >= 0.985) return p.CardBg;
                if (IsNear(original, UiKit.PageBg, 4)) return p.PageBg;
                if (IsNear(original, UiKit.NavBg, 4)) return p.NavBg;
                if (IsNear(original, UiKit.FooterBg, 4)) return p.FooterBg;
                if (IsNear(original, UiKit.MutedBg, 4)) return p.MutedBg;
                return original;
            }
            if (l >= 0.86) return p.CardBg;
            if (l >= 0.72) return p.MutedBg;
            if (l >= 0.45) return p.PageBg;
            return original;
        }

        public static Color ForeFor(Color original, Color background)
        {
            UiColors p = _palette;
            if (original.A == 0) return original;
            if (original.B > original.R + 30 && original.B > 120 && original.R < 120) return p.Accent;
            if (!_dark) return original;
            double l = Luma(original);
            double sat = Saturation(original);
            if (l >= 0.86) return p.Text;
            if (sat > 0.4 && l < 0.78) return Mix(original, Color.White, 0.18f);
            if (sat < 0.35 && l > 0.28) return p.SubText;
            if (l < 0.72) return p.Text;
            return p.SubText;
        }

        private static double Saturation(Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max == 0) return 0;
            return (max - min) / (double)max;
        }
    }

    internal static class ModernDialogSkin
    {
        public static void Apply(Form form)
        {
            if (!ModernTheme.Active || form == null) return;
            ModernTheme.MarkModern(form);
            form.FormClosed += delegate { ModernTheme.UnmarkModern(form); };
            UiColors p = ModernTheme.P;
            form.BackColor = p.PageBg;
            form.ForeColor = p.Text;
            Theme(form, p);
            form.Invalidate(true);
        }

        private static void Theme(Control parent, UiColors p)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                if (c is Label)
                {
                    c.BackColor = Color.Transparent;
                    Color fore = ModernTheme.ForeFor(c.ForeColor, p.PageBg);
                    if (c.ForeColor != fore) c.ForeColor = fore;
                }
                else if (c is TextBox)
                {
                    c.BackColor = p.InputBg;
                    c.ForeColor = p.Text;
                }
                else if (c is NumericUpDown)
                {
                    c.BackColor = p.InputBg;
                    c.ForeColor = p.Text;
                }
                else if (c is ComboBox)
                {
                    c.BackColor = p.InputBg;
                    c.ForeColor = p.Text;
                }
                else if (c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel || c is GroupBox)
                {
                    if (c.BackColor != Color.Transparent) c.BackColor = p.PageBg;
                }
                else if (c is Button && !(c is ModernButton))
                {
                    c.BackColor = p.CardBg;
                    c.ForeColor = p.Text;
                }
                Theme(c, p);
            }
        }
    }
}
