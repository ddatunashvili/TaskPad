using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Comments on text, stored as CriticMarkup so the file stays plain text:
    ///   {==highlighted text==}{>>the comment<<}     or a bare note   {>>note<<}
    public static class Comments
    {
        public static readonly Regex Markup = new Regex(@"\{==(?<t>.+?)==\}\{>>(?<c>.*?)<<\}|\{>>(?<c>.*?)<<\}", RegexOptions.Compiled);

        public static string Decode(string c) => c.Replace("\\n", "\n");
        public static string Encode(string c) => c.Replace("\r\n", "\n").Replace("\n", "\\n").Replace("<<}", "<< }").Trim();

        /// Finds the comment markup containing `offset`, if any.
        public static bool Find(TextDocument doc, int offset, out int start, out Match match)
        {
            var line = doc.GetLineByOffset(Math.Min(offset, doc.TextLength));
            var text = doc.GetText(line);
            for (var m = Markup.Match(text); m.Success; m = m.NextMatch())
            {
                int s = line.Offset + m.Index;
                if (offset >= s && offset <= s + m.Length)
                {
                    start = s;
                    match = m;
                    return true;
                }
            }
            start = -1;
            match = null;
            return false;
        }
    }

    /// Highlights commented text.
    public sealed class CommentColorizer : DocumentColorizingTransformer
    {
        static readonly Brush Highlight = Frozen(Color.FromArgb(0x2E, 0xFB, 0xBF, 0x24));
        static readonly TextDecorationCollection Underline = MakeUnderline();

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            if (text.IndexOf("{==", StringComparison.Ordinal) < 0) return;
            for (var m = Comments.Markup.Match(text); m.Success; m = m.NextMatch())
            {
                var t = m.Groups["t"];
                if (!t.Success) continue;
                ChangeLinePart(line.Offset + t.Index, line.Offset + t.Index + t.Length, el =>
                {
                    el.TextRunProperties.SetBackgroundBrush(Highlight);
                    el.TextRunProperties.SetTextDecorations(Underline);
                });
            }
        }

        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        static TextDecorationCollection MakeUnderline()
        {
            var pen = new Pen(Theme.Todo, 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
            pen.Freeze();
            var c = new TextDecorationCollection { new TextDecoration(TextDecorationLocation.Underline, pen, 1, TextDecorationUnit.FontRecommended, TextDecorationUnit.Pixel) };
            c.Freeze();
            return c;
        }
    }

    /// Hides the {== ==}{>> <<} syntax and shows a small speech-bubble instead.
    public sealed class CommentGenerator : VisualLineElementGenerator
    {
        readonly CommentUi _ui;
        public CommentGenerator(CommentUi ui) { _ui = ui; }

        bool Next(int startOffset, out int offset, out int length, out bool bubble, out int markupStart)
        {
            var line = CurrentContext.Document.GetLineByOffset(startOffset);
            var text = CurrentContext.Document.GetText(line);
            int rel = startOffset - line.Offset;
            offset = length = markupStart = -1;
            bubble = false;
            if (text.IndexOf("{", StringComparison.Ordinal) < 0) return false;
            for (var m = Comments.Markup.Match(text); m.Success; m = m.NextMatch())
            {
                int s = line.Offset + m.Index, e = s + m.Length;
                if (e <= startOffset) continue;
                markupStart = s;
                if (m.Groups["t"].Success)
                {
                    int tailStart = s + 3 + m.Groups["t"].Length;
                    if (s >= startOffset) { offset = s; length = 3; return true; }              // "{=="  (hidden)
                    if (tailStart >= startOffset) { offset = tailStart; length = e - tailStart; bubble = true; return true; } // "==}{>>…<<}"
                }
                else if (s >= startOffset) { offset = s; length = m.Length; bubble = true; return true; }
            }
            return false;
        }

        public override int GetFirstInterestedOffset(int startOffset) =>
            Next(startOffset, out int o, out _, out _, out _) ? o : -1;

        public override VisualLineElement ConstructElement(int offset)
        {
            if (!Next(offset, out int o, out int len, out bool bubble, out int markupStart) || o != offset) return null;
            if (!bubble) return new InlineObjectElement(len, new Canvas { Width = 0 });

            var tv = CurrentContext.TextView;
            double size = Math.Round(tv.DefaultLineHeight * 0.62);
            var icon = new Border
            {
                Width = size + 6,
                Height = size,
                Margin = new Thickness(3, 0, 1, 0),
                CornerRadius = new CornerRadius(size / 2.4, size / 2.4, size / 2.4, 1),
                Background = Theme.Todo,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "…",
                    FontSize = size * 0.75,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(24, 20, 8)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -size * 0.35, 0, 0),
                },
            };
            TextBlock.SetBaselineOffset(icon, size - 1);
            var anchor = CurrentContext.Document.CreateAnchor(markupStart);
            icon.MouseEnter += (s, e) => { if (!anchor.IsDeleted) _ui.ShowCard(anchor.Offset, icon); };
            icon.MouseLeave += (s, e) => _ui.HideCardSoon();
            icon.MouseLeftButtonDown += (s, e) => e.Handled = true;
            // open on mouse-up: a StaysOpen=false popup opened on mouse-down closes again on the release
            icon.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (!anchor.IsDeleted) _ui.Edit(anchor.Offset);
            };
            return new InlineObjectElement(len, icon);
        }
    }

    /// Hover card + edit box for comments of one editor.
    public sealed class CommentUi
    {
        readonly TextEditor _ed;
        readonly Popup _card, _editor;
        readonly TextBlock _cardText;
        readonly TextBox _box;
        readonly Border _editFrame;
        readonly System.Windows.Threading.DispatcherTimer _hideTimer;
        int _editStart = -1;            // existing markup being edited
        (int Start, int Length)? _newRange; // selection being commented

        public static void Attach(TextEditor ed)
        {
            var ui = new CommentUi(ed);
            ed.TextArea.TextView.ElementGenerators.Add(new CommentGenerator(ui));
            ed.TextArea.TextView.LineTransformers.Add(new CommentColorizer());
        }

        CommentUi(TextEditor ed)
        {
            _ed = ed;
            var tv = ed.TextArea.TextView;

            // ---- hover card
            _cardText = new TextBlock
            {
                Foreground = Theme.Fg,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            };
            var hint = new TextBlock
            {
                Text = "click the bubble to edit",
                Foreground = Theme.FgDim,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
            };
            var cardStack = new StackPanel();
            cardStack.Children.Add(_cardText);
            cardStack.Children.Add(hint);
            var cardBorder = Frame(new Border { Child = cardStack, Padding = new Thickness(12, 9, 12, 9) });
            cardBorder.MaxWidth = 420;
            _card = new Popup
            {
                AllowsTransparency = true,
                Placement = PlacementMode.Relative,
                PlacementTarget = tv,
                StaysOpen = true,
                Focusable = false,
                PopupAnimation = PopupAnimation.Fade,
                Child = cardBorder,
            };
            cardBorder.MouseEnter += (s, e) => _hideTimer.Stop();
            cardBorder.MouseLeave += (s, e) => HideCardSoon();
            cardBorder.MouseLeftButtonUp += (s, e) => { if (_cardStart >= 0) Edit(_cardStart); };
            _hideTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _hideTimer.Tick += (s, e) => { _hideTimer.Stop(); _card.IsOpen = false; };

            // ---- edit box (resizable)
            _box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x11)),
                Foreground = Theme.Fg,
                CaretBrush = Theme.Accent,
                SelectionBrush = Theme.Accent,
                BorderBrush = Theme.ChromeBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 5, 6, 5),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
            };
            var save = Btn("Save", "Ctrl+Enter", Theme.Accent, () => Commit());
            var del = Btn("Delete", "Remove comment, keep text", Theme.Bang, Delete);
            var cancel = Btn("Cancel", "Esc", Theme.FgDim, () => _editor.IsOpen = false);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 14, 0) };
            buttons.Children.Add(del);
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            _deleteButton = del;

            var title = new TextBlock { Text = "Comment", Foreground = Theme.Todo, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
            var grip = new Thumb { Width = 14, Height = 14, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -8, -6) };
            grip.Template = GripTemplate();
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(title, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            dock.Children.Add(title);
            dock.Children.Add(buttons);
            dock.Children.Add(_box);
            var gridRoot = new Grid();
            gridRoot.Children.Add(dock);
            gridRoot.Children.Add(grip);
            _editFrame = Frame(new Border { Child = gridRoot, Padding = new Thickness(12, 10, 12, 10) });
            _editFrame.Width = Workspace.Settings.CommentWidth;
            _editFrame.Height = Workspace.Settings.CommentHeight;
            grip.DragDelta += (s, e) =>
            {
                _editFrame.Width = Math.Max(240, Math.Min(900, _editFrame.Width + e.HorizontalChange));
                _editFrame.Height = Math.Max(140, Math.Min(700, _editFrame.Height + e.VerticalChange));
            };
            grip.DragCompleted += (s, e) =>
            {
                Workspace.Settings.CommentWidth = _editFrame.Width;
                Workspace.Settings.CommentHeight = _editFrame.Height;
            };
            _editor = new Popup
            {
                AllowsTransparency = true,
                Placement = PlacementMode.Relative,
                PlacementTarget = tv,
                StaysOpen = false,
                PopupAnimation = PopupAnimation.Fade,
                Child = _editFrame,
            };
            _editor.Closed += (s, e) => { _editStart = -1; _newRange = null; _ed.TextArea.Focus(); };
            _box.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { Commit(); e.Handled = true; }
                else if (e.Key == Key.Escape) { _editor.IsOpen = false; e.Handled = true; }
            };

            // hover over commented text
            tv.MouseHover += (s, e) =>
            {
                var pos = tv.GetPositionFloor(e.GetPosition(tv) + tv.ScrollOffset);
                if (pos == null) return;
                int off = _ed.Document.GetOffset(pos.Value.Location);
                if (Comments.Find(_ed.Document, off, out int start, out _)) ShowCard(start, null, e.GetPosition(tv));
            };
            tv.MouseHoverStopped += (s, e) => HideCardSoon();

            ed.TextArea.PreviewKeyDown += (s, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                var mods = Keyboard.Modifiers;
                if (key == Key.M && (mods == ModifierKeys.Control || mods == (ModifierKeys.Control | ModifierKeys.Alt))) { AddFromSelection(); e.Handled = true; }
            };
            SelectionToolbar.CommentRequested += editor => { if (editor == _ed) AddFromSelection(); };
        }

        readonly Border _deleteButton;
        int _cardStart = -1;

        // ---------------- card ----------------

        public void ShowCard(int markupStart, FrameworkElement near, Point? at = null)
        {
            if (_editor.IsOpen) return;
            var doc = _ed.Document;
            if (!Comments.Find(doc, markupStart, out int start, out var m)) return;
            _hideTimer.Stop();
            _cardStart = start;
            _cardText.Text = Comments.Decode(m.Groups["c"].Value).Trim();
            if (_cardText.Text.Length == 0) _cardText.Text = "(empty comment)";
            var tv = _ed.TextArea.TextView;
            Point p = at ?? near.TranslatePoint(new Point(0, near.ActualHeight), tv);
            _card.HorizontalOffset = p.X + 4;
            _card.VerticalOffset = p.Y + 14;
            _card.IsOpen = true;
        }

        public void HideCardSoon()
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        // ---------------- edit ----------------

        void AddFromSelection()
        {
            var sel = _ed.TextArea.Selection;
            var doc = _ed.Document;
            if (!sel.IsEmpty)
            {
                var seg = sel.SurroundingSegment;
                if (doc.GetLineByOffset(seg.Offset).LineNumber != doc.GetLineByOffset(seg.EndOffset).LineNumber) return; // one line only
                _newRange = (seg.Offset, seg.Length);
            }
            else _newRange = (_ed.CaretOffset, 0);
            _editStart = -1;
            _box.Text = "";
            _deleteButton.Visibility = Visibility.Collapsed;
            OpenEditorAt(_newRange.Value.Start);
        }

        public void Edit(int markupStart)
        {
            if (!Comments.Find(_ed.Document, markupStart, out int start, out var m)) return;
            _card.IsOpen = false;
            _editStart = start;
            _newRange = null;
            _box.Text = Comments.Decode(m.Groups["c"].Value);
            _deleteButton.Visibility = Visibility.Visible;
            OpenEditorAt(start);
        }

        void OpenEditorAt(int offset)
        {
            var tv = _ed.TextArea.TextView;
            var loc = _ed.Document.GetLocation(offset);
            var vp = tv.GetVisualPosition(new TextViewPosition(loc), VisualYPosition.LineBottom) - tv.ScrollOffset;
            _editor.HorizontalOffset = Math.Max(8, vp.X - 20);
            _editor.VerticalOffset = vp.Y + 6;
            _card.IsOpen = false;
            _editor.IsOpen = true;
            // focus once the popup's window exists, otherwise keys keep going to the editor
            _box.Dispatcher.BeginInvoke(new Action(() => { _box.Focus(); Keyboard.Focus(_box); _box.SelectAll(); }),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        void Commit()
        {
            var doc = _ed.Document;
            string c = Comments.Encode(_box.Text);
            if (_newRange.HasValue)
            {
                var (s, len) = _newRange.Value;
                if (c.Length > 0)
                {
                    string text = doc.GetText(s, len);
                    string markup = len > 0 ? "{==" + text + "==}{>>" + c + "<<}" : "{>>" + c + "<<}";
                    doc.Replace(s, len, markup);
                    _ed.TextArea.ClearSelection();
                    _ed.CaretOffset = s + markup.Length;
                }
            }
            else if (_editStart >= 0 && Comments.Find(doc, _editStart, out int start, out var m))
            {
                if (c.Length == 0) { _editor.IsOpen = false; Delete(); return; }
                var g = m.Groups["c"];
                doc.Replace(start + g.Index - m.Index, g.Length, c);
            }
            _editor.IsOpen = false;
        }

        void Delete()
        {
            var doc = _ed.Document;
            int target = _editStart >= 0 ? _editStart : _cardStart;
            if (target >= 0 && Comments.Find(doc, target, out int start, out var m))
                doc.Replace(start, m.Length, m.Groups["t"].Success ? m.Groups["t"].Value : "");
            _editor.IsOpen = false;
        }

        // ---------------- ui helpers ----------------

        static Border Frame(Border inner) => new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1D)),
            BorderBrush = Theme.ChromeBorder,
            BorderThickness = new Thickness(3, 1, 1, 1),
            CornerRadius = new CornerRadius(6),
            Child = inner,
        }.Also(b => b.BorderBrush = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Theme.Todo.Color, 0), new GradientStop(Theme.Todo.Color, 0.01),
            new GradientStop(Theme.ChromeBorder.Color, 0.01), new GradientStop(Theme.ChromeBorder.Color, 1),
        }, 0));

        static Border Btn(string text, string tip, Brush fg, Action click)
        {
            var b = new Border
            {
                Padding = new Thickness(12, 4, 12, 5),
                Margin = new Thickness(6, 0, 0, 0),
                CornerRadius = new CornerRadius(5),
                Background = text == "Save" ? new SolidColorBrush(Color.FromArgb(0x40, 0x7C, 0x6C, 0xF6)) : Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = new TextBlock { Text = text, Foreground = fg, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5 },
            };
            var bg = b.Background;
            var hover = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x34));
            b.MouseEnter += (s, e) => b.Background = hover;
            b.MouseLeave += (s, e) => b.Background = bg;
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        static ControlTemplate GripTemplate()
        {
            var t = new ControlTemplate(typeof(Thumb));
            var f = new FrameworkElementFactory(typeof(TextBlock));
            f.SetValue(TextBlock.TextProperty, "◢");
            f.SetValue(TextBlock.ForegroundProperty, Theme.FgDim);
            f.SetValue(TextBlock.FontSizeProperty, 11.0);
            f.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
            t.VisualTree = f;
            return t;
        }
    }

    static class FluentExt
    {
        public static T Also<T>(this T x, Action<T> a) { a(x); return x; }
    }
}
