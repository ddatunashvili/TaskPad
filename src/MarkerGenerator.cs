using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Replaces line markers with nicer visuals: [ ] -> clickable checkbox, * -> star, ! -> badge, --- -> rule, etc.
    /// The file text is never changed by rendering; only by clicking a checkbox.
    public sealed class MarkerGenerator : VisualLineElementGenerator
    {
        static readonly FontFamily SymbolFont = new FontFamily("Segoe UI Symbol, Segoe UI");
        static readonly Geometry CheckGeo = Geometry.Parse("M 0,5 L 3.5,8.5 L 10,1.5");
        static readonly Geometry CrossGeo = Geometry.Parse("M 0,0 L 8,8 M 8,0 L 0,8");

        double _lineH, _baseline, _charW, _em;
        readonly Func<int> _caretLine;

        /// `caretLine`: heading markers ("## ") stay visible on the caret's line so the level can be edited.
        public MarkerGenerator(Func<int> caretLine = null) { _caretLine = caretLine; }

        bool Skip(Tok t, ICSharpCode.AvalonEdit.Document.DocumentLine line) =>
            t.Kind == TokKind.HeadingMark && _caretLine != null && _caretLine() == line.LineNumber;

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var line = CurrentContext.Document.GetLineByOffset(startOffset);
            var info = LineParser.Parse(CurrentContext.Document.GetText(line));
            foreach (var t in info.Tokens)
            {
                if (Skip(t, line)) continue;
                int abs = line.Offset + t.Start;
                if (abs >= startOffset) return abs;
            }
            return -1;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            var doc = CurrentContext.Document;
            var line = doc.GetLineByOffset(offset);
            var info = LineParser.Parse(doc.GetText(line));
            var tv = CurrentContext.TextView;
            _em = CurrentContext.GlobalTextRunProperties.FontRenderingEmSize;
            _lineH = tv.DefaultLineHeight;
            _baseline = tv.DefaultBaseline;
            _charW = tv.WideSpaceWidth;

            foreach (var t in info.Tokens)
            {
                if (line.Offset + t.Start != offset || Skip(t, line)) continue;
                FrameworkElement el;
                switch (t.Kind)
                {
                    case TokKind.Rule: el = Rule(doc.GetCharAt(offset), tv.ActualWidth - info.Indent * _charW - 40); break;
                    case TokKind.HiddenBullet:
                    case TokKind.HeadingMark: el = Host(new Canvas(), 0); break;
                    case TokKind.Checkbox: el = Checkbox(doc, offset, info.Check, info.Indent > 0); break;
                    default: el = TagGlyph(info.Tag, t.Length); break;
                }
                return new InlineObjectElement(t.Length, el);
            }
            return null;
        }

        FrameworkElement Host(FrameworkElement child, double width)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var c = new Canvas { Width = width, Height = _lineH, Background = Brushes.Transparent };
            // wide glyphs (badges, ★) overflow to the right into the following space, never left where they get clipped
            Canvas.SetLeft(child, Math.Max(1, Math.Round((width - child.DesiredSize.Width) / 2)));
            Canvas.SetTop(child, Math.Round((_lineH - child.DesiredSize.Height) / 2));
            c.Children.Add(child);
            TextBlock.SetBaselineOffset(c, _baseline);
            return c;
        }

        FrameworkElement Rule(char c, double width)
        {
            width = Math.Max(40, double.IsNaN(width) || width <= 0 ? 600 : width);
            var r = new Rectangle
            {
                Width = width,
                Height = c == '=' ? 2 : 1,
                Fill = c == '=' ? Theme.Accent : c == '*' ? Theme.Dash : Theme.ChromeBorder,
                Opacity = c == '=' ? 0.6 : 1,
            };
            if (c == '_') { r.Fill = Brushes.Transparent; r.Stroke = Theme.FgFaint; r.StrokeDashArray = new DoubleCollection { 2, 3 }; r.Height = 1; }
            return Host(r, width);
        }

        FrameworkElement Checkbox(TextDocument doc, int offset, Check state, bool subtask)
        {
            // subtasks (indented) get a smaller box
            double box = Math.Round(_em * (subtask ? 0.78 : 1.0));
            var b = new Border
            {
                Width = box,
                Height = box,
                CornerRadius = new CornerRadius(Math.Round(box * 0.24)),
                BorderThickness = new Thickness(1.5),
                Background = Brushes.Transparent,
                SnapsToDevicePixels = true,
            };
            switch (state)
            {
                case Check.Done:
                    b.Background = Theme.BoxDone;
                    b.BorderBrush = Theme.BoxDone;
                    b.Child = Mark(CheckGeo, Brushes.White, box * 0.12, 2.2);
                    break;
                case Check.Doing:
                    b.BorderBrush = Theme.BoxDoing;
                    b.Child = new Border
                    {
                        Background = Theme.BoxDoing,
                        CornerRadius = new CornerRadius(2),
                        Margin = new Thickness(box * 0.16),
                    };
                    break;
                case Check.Cancelled:
                    b.BorderBrush = Theme.BoxCancel;
                    b.Opacity = 0.75;
                    b.Child = Mark(CrossGeo, Theme.BoxCancel, box * 0.18, 1.8);
                    break;
                default:
                    b.BorderBrush = Theme.BoxOpen;
                    break;
            }

            var host = Host(b, _charW * 3);
            host.Cursor = Cursors.Hand;
            host.ToolTip = "Click: done/open   Ctrl+click: in progress   Shift+click: cancelled";
            ToolTipService.SetInitialShowDelay(host, 900);
            var anchor = doc.CreateAnchor(offset);
            host.MouseEnter += (s, e) => { if (state == Check.Open) b.BorderBrush = Theme.Accent; };
            host.MouseLeave += (s, e) => { if (state == Check.Open) b.BorderBrush = Theme.BoxOpen; };
            host.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (anchor.IsDeleted) return;
                var mods = Keyboard.Modifiers;
                Check target = (mods & ModifierKeys.Control) != 0 ? Check.Doing
                             : (mods & ModifierKeys.Shift) != 0 ? Check.Cancelled
                             : state == Check.Open ? Check.Done : Check.Open;
                if (target == state && state != Check.Open) target = Check.Open;
                SmartEditing.SetCheck(doc, anchor.Offset, target);
            };
            return host;
        }

        static Path Mark(Geometry g, Brush stroke, double margin, double thickness) => new Path
        {
            Data = g,
            Stretch = Stretch.Uniform,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Margin = new Thickness(margin),
        };

        FrameworkElement TagGlyph(Tag tag, int len)
        {
            double w = _charW * len;
            switch (tag)
            {
                case Tag.Bang: return Host(Pill("!", Theme.Bang, Brushes.White, _em * 0.95), w);
                case Tag.Question: return Host(Pill("?", Theme.Question, Brushes.White, _em * 0.95), w);
                case Tag.Todo: return Host(Pill("TODO", Theme.Todo, new SolidColorBrush(Color.FromRgb(24, 20, 8)), Math.Max(w - 2, _em * 2.2)), w);
                case Tag.Dash: return Host(Glyph("•", Theme.Dash, 1.15, FontWeights.Normal), w);
                case Tag.Star: return Host(Glyph("★", Theme.Star, 0.95, FontWeights.Normal), w);
                case Tag.Arrow: return Host(Glyph("❯", Theme.Arrow, 0.9, FontWeights.Bold), w);
                case Tag.Back: return Host(Glyph("❮", Theme.Back, 0.9, FontWeights.Bold), w);
                case Tag.Slash: return Host(Glyph("✓", Theme.Slash, 0.95, FontWeights.Bold), w);
                default: return Host(new Canvas(), w);
            }
        }

        TextBlock Glyph(string s, Brush fg, double scale, FontWeight weight) => new TextBlock
        {
            Text = s,
            Foreground = fg,
            FontFamily = SymbolFont,
            FontSize = _em * scale,
            FontWeight = weight,
        };

        Border Pill(string s, Brush bg, Brush fg, double width)
        {
            double h = Math.Round(_em * 0.95);
            return new Border
            {
                Background = bg,
                Width = Math.Round(width),
                Height = h,
                CornerRadius = new CornerRadius(h / 2),
                Child = new TextBlock
                {
                    Text = s,
                    Foreground = fg,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontWeight = FontWeights.Bold,
                    FontSize = _em * (s.Length > 1 ? 0.62 : 0.72),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }
    }
}
