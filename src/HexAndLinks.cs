using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Colour codes (#RGB, #RRGGBB, #RRGGBBAA) get a swatch; Ctrl+click copies a colour code or a link.
    public static class HexAndLinks
    {
        public static readonly Regex Hex = new Regex(@"(?<![\w&#])#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3,4})\b", RegexOptions.Compiled);
        public static readonly Regex Url = new Regex(@"\b(?:https?://|www\.)[^\s<>""'`)\]]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool TryParse(string hex, out Color c)
        {
            c = Colors.Transparent;
            var h = hex.TrimStart('#');
            if (h.Length == 3 || h.Length == 4) h = string.Concat(Array.ConvertAll(h.ToCharArray(), ch => new string(ch, 2)));
            if (h.Length != 6 && h.Length != 8) return false;
            uint v = uint.Parse(h, NumberStyles.HexNumber);
            if (h.Length == 6) c = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            else c = Color.FromArgb((byte)v, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8)); // CSS order RRGGBBAA
            return true;
        }

        public static void Attach(TextEditor ed)
        {
            ed.TextArea.TextView.ElementGenerators.Add(new HexGenerator());
            ed.TextArea.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                var pos = ed.GetPositionFromPoint(e.GetPosition(ed));
                if (pos == null) return;
                var doc = ed.Document;
                var line = doc.GetLineByNumber(pos.Value.Line);
                var text = doc.GetText(line);
                int col = pos.Value.Column - 1;
                foreach (var rx in new[] { Hex, Url })
                    for (var m = rx.Match(text); m.Success; m = m.NextMatch())
                    {
                        if (col < m.Index || col > m.Index + m.Length) continue;
                        e.Handled = true;
                        Copy(ed, m.Value, rx == Url);
                        return;
                    }
            };
        }

        public static void Copy(FrameworkElement from, string value, bool isLink)
        {
            try { Clipboard.SetText(value); } catch { }
            var url = value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + value : value;
            TaskWindow.ToastFrom(from, "Copied " + (value.Length > 60 ? value.Substring(0, 57) + "…" : value),
                isLink ? "Open ↗" : null,
                isLink ? () => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } } : (Action)null,
                isLink ? (Color?)null : (TryParse(value, out var c) ? c : (Color?)null));
        }
    }

    /// Puts a small colour swatch in front of each colour code (replaces the '#').
    public sealed class HexGenerator : VisualLineElementGenerator
    {
        Match Next(int startOffset, out int lineOffset)
        {
            var line = CurrentContext.Document.GetLineByOffset(startOffset);
            lineOffset = line.Offset;
            var text = CurrentContext.Document.GetText(line);
            int rel = startOffset - line.Offset;
            if (text.IndexOf('#', rel) < 0) return null;
            for (var m = HexAndLinks.Hex.Match(text, rel); m.Success; m = m.NextMatch())
            {
                // skip "# heading" markers and "#tag" style words (handled elsewhere)
                if (m.Index == 0 && text.Length > 1 && text[1] == ' ') continue;
                return m;
            }
            return null;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var m = Next(startOffset, out int lo);
            return m == null ? -1 : lo + m.Index;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            var m = Next(offset, out int lo);
            if (m == null || lo + m.Index != offset || !HexAndLinks.TryParse(m.Value, out var color)) return null;
            var tv = CurrentContext.TextView;
            var props = CurrentContext.GlobalTextRunProperties;
            double em = props.FontRenderingEmSize, box = Math.Round(em * 0.8);

            var swatch = new Border
            {
                Width = box,
                Height = box,
                CornerRadius = new CornerRadius(3),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(color),
                Margin = new Thickness(1, Math.Max(0, tv.DefaultBaseline - Align.CapMiddle(CurrentContext) - box / 2), 3, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
            };
            var hash = new TextBlock
            {
                Text = "#",
                FontFamily = props.Typeface.FontFamily,
                FontSize = em,
                Foreground = Theme.FgDim,
                VerticalAlignment = VerticalAlignment.Top,
            };
            // shared inline frame: swatch centred on the capitals, "#" on the text baseline
            Align.Frame(CurrentContext, out double fa, out double fh);
            swatch.Margin = new Thickness(0);
            swatch.VerticalAlignment = VerticalAlignment.Top;
            hash.VerticalAlignment = VerticalAlignment.Top;
            hash.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sp = new Canvas { Width = 1 + box + 3 + hash.DesiredSize.Width, Height = fh, Background = Brushes.Transparent };
            Canvas.SetLeft(swatch, 1);
            Canvas.SetTop(swatch, Math.Round(fa - Align.CapMiddle(CurrentContext) - box / 2));
            Canvas.SetLeft(hash, 1 + box + 3);
            Canvas.SetTop(hash, Align.TextTop(CurrentContext));
            sp.Children.Add(swatch);
            sp.Children.Add(hash);
            TextBlock.SetBaselineOffset(sp, fa);
            string value = m.Value;
            swatch.ToolTip = $"{value.ToUpperInvariant()}   rgb({color.R}, {color.G}, {color.B}{(color.A < 255 ? ", " + Math.Round(color.A / 255.0, 2).ToString(CultureInfo.InvariantCulture) : "")})\nclick to copy";
            swatch.MouseLeftButtonDown += (s, e) => { e.Handled = true; HexAndLinks.Copy(swatch, value, false); };
            return new InlineObjectElement(1, sp);
        }
    }
}
