using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace SCPSLAlert
{
    internal static class WpfScroll
    {
        public static string LastError = "";

        public static void Apply(FrameworkElement root)
        {
            Apply(root, !ModernTheme.IsDark);
        }

        public static void Apply(FrameworkElement root, bool light)
        {
            if (root == null) return;
            try
            {
                UiColors palette = light ? UiColors.Light() : UiColors.Dark();
                Color thumb = ToMedia(palette.Border);
                Color hover = ToMedia(palette.Accent);

                Style style = new Style(typeof(ScrollBar));
                style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 8.0));
                style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0.0));
                style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
                style.Setters.Add(new Setter(Control.TemplateProperty, BuildTemplate(false, thumb, hover)));

                Trigger horizontal = new Trigger();
                horizontal.Property = ScrollBar.OrientationProperty;
                horizontal.Value = Orientation.Horizontal;
                horizontal.Setters.Add(new Setter(Control.TemplateProperty, BuildTemplate(true, thumb, hover)));
                horizontal.Setters.Add(new Setter(FrameworkElement.HeightProperty, 8.0));
                horizontal.Setters.Add(new Setter(FrameworkElement.WidthProperty, double.NaN));
                style.Triggers.Add(horizontal);

                root.Resources[typeof(ScrollBar)] = style;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private static Color ToMedia(System.Drawing.Color color)
        {
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        private static string Hex(Color color)
        {
            return "#" + color.A.ToString("X2") + color.R.ToString("X2")
                + color.G.ToString("X2") + color.B.ToString("X2");
        }

        private static ControlTemplate BuildTemplate(bool horizontal, Color thumb, Color hover)
        {
            string up = horizontal ? "PageLeftCommand" : "PageUpCommand";
            string down = horizontal ? "PageRightCommand" : "PageDownCommand";
            string xaml = "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\""
                + " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\""
                + " xmlns:primitives=\"clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework\""
                + " TargetType=\"{x:Type ScrollBar}\">"
                + "<Grid Background=\"Transparent\">"
                + "<primitives:Track x:Name=\"PART_Track\""
                + (horizontal ? " Orientation=\"Horizontal\"" : "")
                + (horizontal ? " IsDirectionReversed=\"False\"" : " IsDirectionReversed=\"True\"")
                + ">"
                + "<primitives:Track.DecreaseRepeatButton>"
                + "<RepeatButton Command=\"{x:Static ScrollBar." + up + "}\" Focusable=\"False\" IsTabStop=\"False\">"
                + "<RepeatButton.Template><ControlTemplate TargetType=\"{x:Type RepeatButton}\">"
                + "<Border Background=\"Transparent\"/>"
                + "</ControlTemplate></RepeatButton.Template>"
                + "</RepeatButton>"
                + "</primitives:Track.DecreaseRepeatButton>"
                + "<primitives:Track.Thumb>"
                + "<Thumb>"
                + "<Thumb.Template><ControlTemplate TargetType=\"{x:Type Thumb}\">"
                + "<Border x:Name=\"Bd\" CornerRadius=\"4\" Margin=\"1\" Background=\"" + Hex(thumb) + "\"/>"
                + "<ControlTemplate.Triggers>"
                + "<Trigger Property=\"IsMouseOver\" Value=\"True\">"
                + "<Setter TargetName=\"Bd\" Property=\"Background\" Value=\"" + Hex(hover) + "\"/>"
                + "</Trigger>"
                + "<Trigger Property=\"IsDragging\" Value=\"True\">"
                + "<Setter TargetName=\"Bd\" Property=\"Background\" Value=\"" + Hex(hover) + "\"/>"
                + "</Trigger>"
                + "</ControlTemplate.Triggers>"
                + "</ControlTemplate></Thumb.Template>"
                + "</Thumb>"
                + "</primitives:Track.Thumb>"
                + "<primitives:Track.IncreaseRepeatButton>"
                + "<RepeatButton Command=\"{x:Static ScrollBar." + down + "}\" Focusable=\"False\" IsTabStop=\"False\">"
                + "<RepeatButton.Template><ControlTemplate TargetType=\"{x:Type RepeatButton}\">"
                + "<Border Background=\"Transparent\"/>"
                + "</ControlTemplate></RepeatButton.Template>"
                + "</RepeatButton>"
                + "</primitives:Track.IncreaseRepeatButton>"
                + "</primitives:Track>"
                + "</Grid>"
                + "</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }
    }
}
