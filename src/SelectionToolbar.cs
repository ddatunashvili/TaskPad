using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Floating bar shown above a multi-line selection: one click turns every selected line
    /// into tasks, bullets, badges, a numbered list… (click again to remove).
    public sealed class SelectionToolbar
    {
        static readonly (string Glyph, string Syntax, string Tip, Brush Color, bool Badge)[] Buttons =
        {
            ("☐", "[ ] ", "Tasks", null, false),
            ("☑", "[x] ", "Done tasks", null, false),
            ("!", "! ", "Urgent", null, true),
            ("?", "? ", "Questions", null, true),
            ("★", "* ", "Starred", null, false),
            ("❯", "> ", "Next up", null, false),
            ("❮", "< ", "Waiting", null, false),
            ("✓", "/ ", "Finished", null, false),
            ("•", "- ", "Bullets", null, false),
            ("1.", "1. ", "Numbered list", null, false),
            ("H", "## ", "Headings", null, false),
            ("✕", null, "Clear markers", null, false),
        };

        public static event Action<TextEditor> CommentRequested;

        readonly TextEditor _ed;
        readonly StackPanel _markers = new StackPanel { Orientation = Orientation.Horizontal };
        FrameworkElement _commentBtn;
        readonly Popup _popup;
        readonly DispatcherTimer _debounce;

        public static void Attach(TextEditor ed) => new SelectionToolbar(ed);

        SelectionToolbar(TextEditor ed)
        {
            _ed = ed;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            _commentBtn = CommentButton();
            row.Children.Add(_commentBtn);
            foreach (var b in Buttons) _markers.Children.Add(Button(b.Glyph, b.Syntax, b.Tip));
            row.Children.Add(_markers);

            _popup = new Popup
            {
                AllowsTransparency = true,
                PlacementTarget = ed.TextArea.TextView,
                Placement = PlacementMode.Relative,
                StaysOpen = true,
                Focusable = false,
                PopupAnimation = PopupAnimation.Fade,
                Child = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1D)),
                    BorderBrush = Theme.ChromeBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Child = row,
                },
            };

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Evaluate(); };

            var ta = ed.TextArea;
            ta.SelectionChanged += (s, e) => { Hide(); _debounce.Stop(); _debounce.Start(); };
            ta.TextView.ScrollOffsetChanged += (s, e) => { if (_popup.IsOpen) Evaluate(); };
            ta.LostKeyboardFocus += (s, e) => Hide();
            ta.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && _popup.IsOpen) { Hide(); e.Handled = true; } };
            ed.Unloaded += (s, e) => Hide();
        }

        void Hide()
        {
            if (_popup.IsOpen) _popup.IsOpen = false;
        }

        void Evaluate()
        {
            var ta = _ed.TextArea;
            if (ta.Selection.IsEmpty || !ta.IsKeyboardFocusWithin || Mouse.LeftButton == MouseButtonState.Pressed)
            {
                if (Mouse.LeftButton == MouseButtonState.Pressed && !ta.Selection.IsEmpty) _debounce.Start(); // wait for mouse-up
                Hide();
                return;
            }
            SmartEditing.GetLineRange(_ed, out int first, out int last);
            bool multi = last > first;
            // one line: offer a comment on the selected words; several lines: line markers
            _markers.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
            _commentBtn.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;

            var tv = ta.TextView;
            var doc = _ed.Document;
            var top = tv.GetVisualPosition(new TextViewPosition(first, 1), VisualYPosition.LineTop);
            var bottom = tv.GetVisualPosition(new TextViewPosition(last, 1), VisualYPosition.LineBottom);
            double y = top.Y - tv.ScrollOffset.Y - 44;
            if (y < 4) y = bottom.Y - tv.ScrollOffset.Y + 6;
            y = Math.Max(4, Math.Min(y, tv.ActualHeight - 44));
            var selStart = tv.GetVisualPosition(new TextViewPosition(doc.GetLocation(ta.Selection.SurroundingSegment.Offset)), VisualYPosition.LineTop);
            _popup.HorizontalOffset = multi ? 24 : Math.Max(8, selStart.X - tv.ScrollOffset.X - 12);
            _popup.VerticalOffset = y;
            _popup.IsOpen = true;
        }

        FrameworkElement Button(string glyph, string syntax, string tip)
        {
            FrameworkElement content;
            Brush color = syntax == null ? Theme.FgDim : ColorFor(syntax);
            if (glyph == "!" || glyph == "?")
            {
                content = new Border
                {
                    Background = color,
                    Width = 16, Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Child = new TextBlock
                    {
                        Text = glyph, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11,
                        FontFamily = new FontFamily("Segoe UI"),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
            }
            else
            {
                content = new TextBlock
                {
                    Text = glyph,
                    Foreground = color,
                    FontSize = glyph.Length > 1 ? 13 : 15,
                    FontWeight = glyph == "H" || glyph == "1." ? FontWeights.Bold : FontWeights.Normal,
                    FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
                };
            }
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;

            var hover = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x30));
            var b = new Border
            {
                Width = 32, Height = 30,
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Child = content,
                Cursor = Cursors.Hand,
                ToolTip = syntax == null ? tip : $"{tip}   ({syntax.Trim()})",
            };
            ToolTipService.SetInitialShowDelay(b, 400);
            b.MouseEnter += (s, e) => b.Background = hover;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            b.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Markers.Apply(_ed, syntax);
                _ed.TextArea.Focus();
                // little pulse so the click is felt
                b.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(220)));
            };
            return b;
        }

        FrameworkElement CommentButton()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };
            sp.Children.Add(new TextBlock { Text = "💬", FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            sp.Children.Add(new TextBlock { Text = "Comment", Foreground = Theme.Todo, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            var hover = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x30));
            var b = new Border { Height = 30, CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Child = sp, Cursor = Cursors.Hand, ToolTip = "Add a comment to the selection (Ctrl+M)" };
            b.MouseEnter += (s, e) => b.Background = hover;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; Hide(); CommentRequested?.Invoke(_ed); };
            return b;
        }

        static Brush ColorFor(string syntax)
        {
            switch (syntax.Trim())
            {
                case "[ ]": return Theme.Fg;
                case "[x]": return Theme.BoxDone;
                case "!": return Theme.Bang;
                case "?": return Theme.Question;
                case "*": return Theme.Star;
                case ">": return Theme.Arrow;
                case "<": return Theme.Back;
                case "/": return Theme.Slash;
                case "-": return Theme.Dash;
                case "1.": return Theme.Accent;
                default: return Theme.Heading;
            }
        }
    }
}
