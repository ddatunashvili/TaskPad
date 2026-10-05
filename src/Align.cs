using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Vertical alignment of inline icons (checkboxes, badges, pills, bubbles, swatches, chips).
    /// AvalonEdit places inline elements at the TOP of the text line, and a line's top depends on its tallest
    /// element. So every inline element uses the same frame: identical baseline anchor and height, with its
    /// content centred on the middle of the capital letters. Mixed icons on one line then all line up.
    public static class Align
    {
        /// Distance from the baseline up to the middle of the capitals, in pixels.
        public static double CapMiddle(ITextRunConstructionContext ctx)
        {
            var props = ctx.GlobalTextRunProperties;
            double em = props.FontRenderingEmSize;
            double caps = 0.7;
            foreach (var family in props.Typeface.FontFamily.Source.Split(','))
            {
                var ff = new FontFamily(family.Trim());
                // skip families that aren't installed (WPF would silently hand back a fallback font)
                if (!ff.FamilyNames.Values.Any()) continue;
                var tf = new Typeface(ff, props.Typeface.Style, props.Typeface.Weight, props.Typeface.Stretch);
                if (tf.TryGetGlyphTypeface(out var gt)) { caps = gt.CapsHeight; break; }
            }
            return em * caps / 2;
        }

        /// The shared frame: A = baseline offset (distance from the frame top to the baseline), H = frame height.
        public static void Frame(ITextRunConstructionContext ctx, out double a, out double h)
        {
            var tv = ctx.TextView;
            double lineH = tv.DefaultLineHeight, b = tv.DefaultBaseline;
            double gap = Math.Max(0, Workspace.Settings?.MarkerSpacing ?? 0);
            double cap = CapMiddle(ctx);
            double box = lineH + gap;
            a = Math.Max(b, box / 2 + cap);
            h = a + Math.Max(lineH - b, box / 2 - cap);
        }

        /// Wraps `content` in the shared frame, centred on the capitals; `width` = frame width
        /// (null = content width + margins). `centreX` centres it horizontally inside `width`.
        public static Canvas Host(FrameworkElement content, ITextRunConstructionContext ctx, double? width = null, bool centreX = false, double marginLeft = 0, double marginRight = 0, double? centreSpan = null)
        {
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Frame(ctx, out double a, out double h);
            double cap = CapMiddle(ctx);
            double w = width ?? content.DesiredSize.Width + marginLeft + marginRight;
            var c = new Canvas { Width = w, Height = h, Background = Brushes.Transparent };
            double left = centreX ? Math.Max(1, Math.Round(((centreSpan ?? w) - content.DesiredSize.Width) / 2)) : marginLeft;
            Canvas.SetLeft(content, left);
            Canvas.SetTop(content, Math.Round(a - cap - content.DesiredSize.Height / 2));
            c.Children.Add(content);
            TextBlock.SetBaselineOffset(c, a);
            return c;
        }

        /// Top offset inside the frame at which a text block of the editor font sits on the baseline.
        public static double TextTop(ITextRunConstructionContext ctx)
        {
            Frame(ctx, out double a, out _);
            return a - ctx.TextView.DefaultBaseline;
        }
    }
}
