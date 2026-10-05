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

        double _lineH, _baseline, _charW, _em, _gap;
        static readonly double[] HeadScale = { 1, 1.6, 1.4, 1.25, 1.12, 1.04, 0.96 };   // same as TaskColorizer
        static readonly double[] HeadTop = { 0, 0.9, 0.7, 0.55, 0.45, 0.35, 0.3 };     // gap above, in em
        readonly Func<int> _caretLine;

        /// `caretLine`: heading markers ("## ") stay visible on the caret's line so the level can be edited.
        readonly Func<int> _revealLine;
        readonly Action<int> _reveal;
        static DateTime _lastToggle;
        static ICSharpCode.AvalonEdit.Document.TextDocument _lastToggleDoc;

        /// `revealLine` / `reveal`: double-clicking an icon shows that line's raw code ("[ ]", "!", …) for editing.
        public MarkerGenerator(Func<int> caretLine = null, Func<int> revealLine = null, Action<int> reveal = null)
        {
            _caretLine = caretLine;
            _revealLine = revealLine;
            _reveal = reveal;
        }

        bool Skip(Tok t, ICSharpCode.AvalonEdit.Document.DocumentLine line) =>
            _revealLine != null && _revealLine() == line.LineNumber;

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var line = CurrentContext.Document.GetLineByOffset(startOffset);
            if (Code.IsCodeLine(CurrentContext.Document, line.LineNumber)) return -1;
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
            _gap = Math.Max(0, Workspace.Settings.MarkerSpacing);

            foreach (var t in info.Tokens)
            {
                if (line.Offset + t.Start != offset || Skip(t, line)) continue;
                FrameworkElement el;
                switch (t.Kind)
                {
                    case TokKind.Rule: el = Rule(doc.GetCharAt(offset), tv.ActualWidth - info.Indent * _charW - 40); break;
                    case TokKind.HiddenBullet: el = Host(new Canvas(), 0); break;
                    case TokKind.HeadingMark: el = HeadingSpacer(info.Heading, t.Length, _caretLine != null && _caretLine() == line.LineNumber); break;
                    case TokKind.Checkbox: el = Checkbox(doc, offset, info.Check, info.Indent > 0); break;
                    default: el = TagGlyph(info.Tag, t.Length); break;
                }
                if (t.Kind == TokKind.Checkbox || t.Kind == TokKind.Tag || t.Kind == TokKind.HeadingMark)
                {
                    var lineAnchor = doc.CreateAnchor(line.Offset);
                    if (t.Kind != TokKind.HeadingMark) KeywordMenu.Attach(el, tv, lineAnchor);
                    el.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount < 2 || _reveal == null || lineAnchor.IsDeleted) return;
                        e.Handled = true;
                        // the first click of a double-click toggled the box: take that back
                        if (_lastToggleDoc == doc && (DateTime.Now - _lastToggle).TotalMilliseconds < 700 && doc.UndoStack.CanUndo)
                            doc.UndoStack.Undo();
                        _lastToggleDoc = null;
                        _reveal(doc.GetLineByOffset(lineAnchor.Offset).LineNumber);
                    };
                }
                return new InlineObjectElement(t.Length, el);
            }
            return null;
        }

        /// Wraps a marker visual. The host is a bit taller than a text line (markerSpacing, half above /
        /// half below) so task and icon lines get breathing room; the line grows to fit it.
        /// Wraps a marker visual in the shared inline frame (see Align): centred on the capitals,
        /// with markerSpacing of breathing room so task and icon lines get a little more height.
        FrameworkElement Host(FrameworkElement child, double width) => Align.Host(child, CurrentContext, width, centreX: true);

        /// Hidden "## " of a heading, sized to add space above (more for h1) and a little below.
        /// On the caret's line it shows the hashes as dim text so the level stays visible while editing.
        FrameworkElement HeadingSpacer(int level, int len, bool showHashes)
        {
            double scale = HeadScale[Math.Max(1, Math.Min(6, level))];
            double top = Math.Round(_em * HeadTop[Math.Max(1, Math.Min(6, level))] * (_gap > 0 ? 1 : 0));
            double bottom = Math.Round(_em * 0.25 * (_gap > 0 ? 1 : 0));
            double baseline = _baseline * scale + top;
            double h = baseline + (_lineH - _baseline) * scale + bottom;
            var c = new Canvas { Height = h, Background = Brushes.Transparent };
            if (showHashes)
            {
                var tb = new TextBlock { Text = new string('#', level), Foreground = Theme.FgFaint, FontSize = _em, FontFamily = CurrentContext.GlobalTextRunProperties.Typeface.FontFamily };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                c.Width = Math.Max(len * _charW, tb.DesiredSize.Width + _charW);
                Canvas.SetTop(tb, baseline - _baseline);
                c.Children.Add(tb);
            }
            else c.Width = 0;
            TextBlock.SetBaselineOffset(c, baseline);
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
                _lastToggle = DateTime.Now;
                _lastToggleDoc = doc;
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
