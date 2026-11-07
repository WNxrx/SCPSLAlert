using System;
using System.Windows.Media;

namespace SCPSLAlert
{
    internal static class WpfTheme
    {
        public static event Action Changed;

        public static readonly FontFamily UiFont = UiFonts.Body;
        public static readonly FontFamily IconFont = new FontFamily("Segoe MDL2 Assets");
        public static readonly FontFamily BrandFont = UiFonts.Body;

        public static Brush PageBg;
        public static Brush CardBg;
        public static Brush CardBorder;
        public static Brush Text;
        public static Brush SubText;
        public static Brush Accent;
        public static Brush AccentSoft;
        public static Brush InputBg;
        public static Brush InputBorder;
        public static Brush Border;
        public static Brush MutedBg;
        public static Brush NavBg;
        public static Brush NavHover;
        public static Brush NavSelected;
        public static Brush HeaderBg;
        public static Brush FooterBg;
        public static Brush TrackOff;
        public static Brush Knob;
        public static Brush Danger;
        public static Brush Shadow;

        static WpfTheme()
        {
            Build();
            ModernTheme.Changed += delegate
            {
                Build();
                if (Changed != null) Changed();
            };
        }

        public static bool IsDark { get { return ModernTheme.IsDark; } }

        public static void Start()
        {
            ModernTheme.Active = true;
            ModernTheme.Start();
        }

        public static void Build()
        {
            UiColors p = ModernTheme.P;
            PageBg = Solid(p.PageBg);
            CardBg = Solid(p.CardBg);
            CardBorder = Solid(p.CardBorder);
            Text = Solid(p.Text);
            SubText = Solid(p.SubText);
            Accent = Solid(p.Accent);
            AccentSoft = Solid(p.AccentSoft);
            InputBg = Solid(p.InputBg);
            InputBorder = Solid(p.InputBorder);
            Border = Solid(p.Border);
            MutedBg = Solid(p.MutedBg);
            NavBg = Solid(p.NavBg);
            NavHover = Solid(p.NavHover);
            NavSelected = Solid(p.NavSelected);
            HeaderBg = Solid(p.HeaderBg);
            FooterBg = Solid(p.FooterBg);
            TrackOff = Solid(p.TrackOff);
            Knob = Solid(p.Knob);
            Danger = Solid(p.Danger);
            Shadow = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0));
            ((SolidColorBrush)Shadow).Freeze();
        }

        public static SolidColorBrush Solid(System.Drawing.Color c)
        {
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        public static SolidColorBrush Fresh(System.Drawing.Color c)
        {
            return new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        }

        public static SolidColorBrush Blend(Brush a, Brush b, double t)
        {
            Color ca = ((SolidColorBrush)a).Color;
            Color cb = ((SolidColorBrush)b).Color;
            return new SolidColorBrush(Color.FromArgb(
                (byte)(ca.A + (cb.A - ca.A) * t),
                (byte)(ca.R + (cb.R - ca.R) * t),
                (byte)(ca.G + (cb.G - ca.G) * t),
                (byte)(ca.B + (cb.B - ca.B) * t)));
        }
    }
}
