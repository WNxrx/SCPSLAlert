using System;
using System.Drawing;
using System.Globalization;

namespace SCPSLAlert
{
    /// <summary>一套主题色板。深色/浅色各一份。</summary>
    internal sealed class UiPalette
    {
        public Color PageBg;
        public Color Panel;
        public Color PanelAlt;
        public Color Line;
        public Color Text;
        public Color Sub;
        public Color Muted;
        public Color Accent;
        public Color Warn;
        public Color Danger;
        public Color Success;
        public Color Info;

        public Color[] ToArray()
        {
            return new Color[] { PageBg, Panel, PanelAlt, Line, Text, Sub, Muted, Accent, Warn, Danger, Success, Info };
        }

        public void FromArray(Color[] c)
        {
            if (c == null || c.Length < 12) return;
            PageBg = c[0]; Panel = c[1]; PanelAlt = c[2]; Line = c[3];
            Text = c[4]; Sub = c[5]; Muted = c[6]; Accent = c[7];
            Warn = c[8]; Danger = c[9]; Success = c[10]; Info = c[11];
        }
    }

    /// <summary>
    /// 全软件主题令牌的唯一来源。
    /// 深/浅两套色板都可独立编辑；强调色变化时触发 Changed。
    /// </summary>
    internal static class ThemeTokens
    {
        public const string DefaultAccent = "#38D6FF";

        private static UiPalette _dark = DefaultDark();
        private static UiPalette _light = DefaultLight();

        public static event Action Changed;

        public static UiPalette Dark { get { return _dark; } }
        public static UiPalette Light { get { return _light; } }

        public static UiPalette Get(bool light)
        {
            return light ? _light : _dark;
        }

        public static void SetPalette(UiPalette dark, UiPalette light)
        {
            if (dark == null || light == null) return;
            _dark = dark;
            _light = light;
            if (Changed != null)
            {
                try { Changed(); } catch { }
            }
        }

        /// <summary>从配置读两套色板；缺失/非法由 Config.Load 先回落。</summary>
        public static void Configure(Config cfg)
        {
            if (cfg == null) return;
            UiPalette d = DefaultDark();
            UiPalette l = DefaultLight();
            Color[] da = d.ToArray();
            Color[] la = l.ToArray();
            da[0] = ParseHex(cfg.PageBgHex, da[0]);
            da[1] = ParseHex(cfg.PanelHex, da[1]);
            da[2] = ParseHex(cfg.PanelAltHex, da[2]);
            da[3] = ParseHex(cfg.LineHex, da[3]);
            da[4] = ParseHex(cfg.TextHex, da[4]);
            da[5] = ParseHex(cfg.SubTextHex, da[5]);
            da[6] = ParseHex(cfg.MutedHex, da[6]);
            da[7] = ParseHex(cfg.AccentHex, da[7]);
            da[8] = ParseHex(cfg.WarnHex, da[8]);
            da[9] = ParseHex(cfg.DangerHex, da[9]);
            da[10] = ParseHex(cfg.SuccessHex, da[10]);
            da[11] = ParseHex(cfg.InfoHex, da[11]);

            la[0] = ParseHex(cfg.LightPageBgHex, la[0]);
            la[1] = ParseHex(cfg.LightPanelHex, la[1]);
            la[2] = ParseHex(cfg.LightPanelAltHex, la[2]);
            la[3] = ParseHex(cfg.LightLineHex, la[3]);
            la[4] = ParseHex(cfg.LightTextHex, la[4]);
            la[5] = ParseHex(cfg.LightSubTextHex, la[5]);
            la[6] = ParseHex(cfg.LightMutedHex, la[6]);
            la[7] = ParseHex(cfg.LightAccentHex, la[7]);
            la[8] = ParseHex(cfg.LightWarnHex, la[8]);
            la[9] = ParseHex(cfg.LightDangerHex, la[9]);
            la[10] = ParseHex(cfg.LightSuccessHex, la[10]);
            la[11] = ParseHex(cfg.LightInfoHex, la[11]);

            d.FromArray(da);
            l.FromArray(la);
            SetPalette(d, l);
        }

        /// <summary>浅色强调色：深色色相不变，压深到白底可读。</summary>
        public static Color DeriveLightAccent(Color darkAccent)
        {
            return EnsureContrast(darkAccent, Color.White, 4.5, false);
        }

        public static Color For(bool light)
        {
            UiPalette p = Get(light);
            Color bg = p.PageBg;
            return EnsureContrast(p.Accent, bg, light ? 4.5 : 3.0, !light);
        }

        public static Color Soft(bool light)
        {
            UiPalette p = Get(light);
            Color c = For(light);
            return Blend(c, p.Panel, 0.82f);
        }

        public static UiPalette DefaultDark()
        {
            UiPalette p = new UiPalette();
            p.PageBg = ParseHex("#070B10", Color.Black);
            p.Panel = ParseHex("#0C1219", Color.Black);
            p.PanelAlt = ParseHex("#101A24", Color.Black);
            p.Line = ParseHex("#1B2B36", Color.Black);
            p.Text = ParseHex("#E6F4F8", Color.White);
            p.Sub = ParseHex("#9AB0BC", Color.Gray);
            p.Muted = ParseHex("#5C7480", Color.Gray);
            p.Accent = ParseHex(DefaultAccent, Color.Cyan);
            p.Warn = ParseHex("#FFB03A", Color.Orange);
            p.Danger = ParseHex("#FF5C5C", Color.Red);
            p.Success = ParseHex("#5BE38C", Color.Green);
            p.Info = ParseHex("#2F7BFF", Color.Blue);
            return p;
        }

        public static UiPalette DefaultLight()
        {
            UiPalette p = new UiPalette();
            p.PageBg = ParseHex("#EBF1F7", Color.White);
            p.Panel = ParseHex("#FFFFFF", Color.White);
            p.PanelAlt = ParseHex("#F2F6FA", Color.White);
            p.Line = ParseHex("#CCD8E3", Color.Gray);
            p.Text = ParseHex("#0A1720", Color.Black);
            p.Sub = ParseHex("#4A5D6C", Color.Gray);
            p.Muted = ParseHex("#8496A4", Color.Gray);
            p.Accent = ParseHex("#006B84", Color.Teal);
            p.Warn = ParseHex("#B26A00", Color.Orange);
            p.Danger = ParseHex("#C83232", Color.Red);
            p.Success = ParseHex("#1F7A4D", Color.Green);
            p.Info = ParseHex("#0B63CE", Color.Blue);
            return p;
        }

        public static string NormalizeHex(string value, string fallback)
        {
            string s = (value ?? "").Trim();
            if (s.Length == 0) return fallback;
            if (!s.StartsWith("#", StringComparison.Ordinal)) s = "#" + s;
            if (s.Length == 4)
            {
                s = "#" + s[1] + s[1] + s[2] + s[2] + s[3] + s[3];
            }
            if (s.Length != 7) return fallback;
            for (int i = 1; i < s.Length; i++)
            {
                if (!Uri.IsHexDigit(s[i])) return fallback;
            }
            return s.ToUpperInvariant();
        }

        public static Color ParseHex(string value, Color fallback)
        {
            string s = NormalizeHex(value, "");
            if (s.Length != 7) return fallback;
            int r, g, b;
            if (!int.TryParse(s.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)) return fallback;
            if (!int.TryParse(s.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)) return fallback;
            if (!int.TryParse(s.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b)) return fallback;
            return Color.FromArgb(255, r, g, b);
        }

        public static string ToHex(Color c)
        {
            return "#" + c.R.ToString("X2", CultureInfo.InvariantCulture)
                       + c.G.ToString("X2", CultureInfo.InvariantCulture)
                       + c.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        public static double Contrast(Color a, Color b)
        {
            double la = Luminance(a);
            double lb = Luminance(b);
            if (la < lb) { double t = la; la = lb; lb = t; }
            return (la + 0.05) / (lb + 0.05);
        }

        private static double Luminance(Color c)
        {
            double r = Channel(c.R);
            double g = Channel(c.G);
            double b = Channel(c.B);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static double Channel(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        private static Color EnsureContrast(Color c, Color background, double target, bool lighten)
        {
            Color current = c;
            for (int i = 0; i < 24 && Contrast(current, background) < target; i++)
            {
                current = lighten ? Lighten(current, 0.06f) : Darken(current, 0.06f);
            }
            return current;
        }

        public static Color Lighten(Color c, float amount)
        {
            return Color.FromArgb(
                c.A,
                (int)Math.Min(255, c.R + (255 - c.R) * amount),
                (int)Math.Min(255, c.G + (255 - c.G) * amount),
                (int)Math.Min(255, c.B + (255 - c.B) * amount));
        }

        public static Color Darken(Color c, float amount)
        {
            return Color.FromArgb(
                c.A,
                (int)Math.Max(0, c.R * (1 - amount)),
                (int)Math.Max(0, c.G * (1 - amount)),
                (int)Math.Max(0, c.B * (1 - amount)));
        }

        public static Color Blend(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static readonly string[] PresetNames = { "信号青", "警戒琥珀", "冰蓝", "冷白" };
        public static readonly string[] PresetHex = { "#38D6FF", "#D8C24A", "#6EA8FF", "#DCE7EE" };
    }
}
