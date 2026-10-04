using System;
using System.Globalization;
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
    /// Date + time picker for deadlines. Writes "due:yyyy-MM-dd[ HH:mm]" into the line.
    public sealed class DuePicker
    {
        static Popup _open;
        public static TextDocument JustSetDoc;
        public static int JustSetCaret = -1;

        /// True while the caret hasn't moved since the picker wrote the deadline.
        public static bool JustSet(TextEditor ed) => JustSetDoc == ed.Document && JustSetCaret == ed.CaretOffset;

        readonly TextEditor _ed;
        readonly TextAnchor _lineAnchor;
        DateTime _month, _date;
        string _time;            // "HH:mm" or null
        readonly bool _editing;
        readonly Popup _popup;
        readonly UniformGrid _days = new UniformGrid { Columns = 7, Rows = 6 };
        readonly TextBlock _monthLabel = new TextBlock(), _summary = new TextBlock();
        readonly TextBox _timeBox;
        readonly WrapPanel _timeChips = new WrapPanel();

        /// Opens the picker for the deadline on `lineNumber` (editing it if one exists), anchored at `offset`.
        public static void Show(TextEditor ed, int lineNumber, int offset, bool takeFocus = true)
        {
            Close();
            new DuePicker(ed, lineNumber, offset, takeFocus);
        }

        public static void Close()
        {
            if (_open != null) _open.IsOpen = false;
            _open = null;
        }

        public static bool IsOpen => _open != null && _open.IsOpen;

        DuePicker(TextEditor ed, int lineNumber, int offset, bool takeFocus)
        {
            _ed = ed;
            var doc = ed.Document;
            var line = doc.GetLineByNumber(lineNumber);
            _lineAnchor = doc.CreateAnchor(line.Offset);

            // start from the line's current deadline, or tomorrow
            var m = Due.Token.Match(doc.GetText(line));
            if (m.Success && Due.TryParse(m.Groups["v"].Value, out var cur, out bool hasTime))
            {
                _editing = true;
                _date = cur.Date;
                _time = hasTime ? cur.ToString("HH:mm") : null;
            }
            else
            {
                _date = DateTime.Today.AddDays(1);
                _time = null;
            }
            _month = new DateTime(_date.Year, _date.Month, 1);

            _timeBox = new TextBox
            {
                Width = 62, Text = _time ?? "", Background = Theme.Input, Foreground = Theme.Fg, CaretBrush = Theme.Accent,
                BorderBrush = Theme.ChromeBorder, BorderThickness = new Thickness(1), Padding = new Thickness(5, 2, 5, 3),
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Time, e.g. 18:30",
            };
            _timeBox.TextChanged += (s, e) =>
            {
                var t = _timeBox.Text.Trim();
                if (t.Length == 0) { _time = null; Refresh(); return; }
                if (DateTime.TryParseExact(t, new[] { "H:mm", "HH:mm", "H", "HHmm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tt))
                { _time = tt.ToString("HH:mm"); Refresh(); }
            };

            _popup = new Popup
            {
                AllowsTransparency = true, StaysOpen = false, PopupAnimation = PopupAnimation.Fade,
                PlacementTarget = ed.TextArea.TextView, Placement = PlacementMode.Relative,
                Child = Build(),
            };
            var tv = ed.TextArea.TextView;
            var vp = tv.GetVisualPosition(new TextViewPosition(doc.GetLocation(Math.Min(offset, doc.TextLength))), VisualYPosition.LineBottom) - tv.ScrollOffset;
            _popup.HorizontalOffset = Math.Max(8, Math.Min(vp.X - 20, tv.ActualWidth - 300));
            _popup.VerticalOffset = vp.Y + 4;
            _popup.Closed += (s, e) => { if (_open == _popup) _open = null; };
            Refresh();
            _open = _popup;
            _popup.IsOpen = true;
            if (takeFocus) _popup.Dispatcher.BeginInvoke(new Action(() => _popup.Child.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        UIElement Build()
        {
            var ui = new FontFamily("Segoe UI");
            var stack = new StackPanel { Width = 280 };

            _summary.FontFamily = ui; _summary.FontSize = 13; _summary.FontWeight = FontWeights.SemiBold; _summary.Foreground = Theme.Fg;
            var title = new DockPanel { Margin = new Thickness(2, 0, 0, 8) };
            title.Children.Add(new TextBlock { Text = "⏰  ", FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(_summary);
            stack.Children.Add(title);

            // quick picks
            var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            quick.Children.Add(Chip("Today", () => Pick(DateTime.Today)));
            quick.Children.Add(Chip("Tomorrow", () => Pick(DateTime.Today.AddDays(1))));
            quick.Children.Add(Chip("+3 days", () => Pick(DateTime.Today.AddDays(3))));
            quick.Children.Add(Chip("Next week", () => Pick(DateTime.Today.AddDays(7))));
            quick.Children.Add(Chip("Next month", () => Pick(DateTime.Today.AddMonths(1))));
            stack.Children.Add(quick);

            // month header
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var prev = NavButton("‹", () => { _month = _month.AddMonths(-1); Refresh(); });
            var next = NavButton("›", () => { _month = _month.AddMonths(1); Refresh(); });
            DockPanel.SetDock(prev, Dock.Left);
            DockPanel.SetDock(next, Dock.Right);
            head.Children.Add(prev);
            head.Children.Add(next);
            _monthLabel.FontFamily = ui; _monthLabel.FontSize = 13; _monthLabel.Foreground = Theme.Fg; _monthLabel.FontWeight = FontWeights.SemiBold;
            _monthLabel.HorizontalAlignment = HorizontalAlignment.Center; _monthLabel.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(_monthLabel);
            stack.Children.Add(head);

            var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 2, 0, 2) };
            foreach (var n in new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" })
                names.Children.Add(new TextBlock { Text = n, FontFamily = ui, FontSize = 11, Foreground = Theme.FgDim, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(names);
            stack.Children.Add(_days);

            // time
            stack.Children.Add(new TextBlock { Text = "TIME", FontFamily = ui, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.FgDim, Margin = new Thickness(2, 10, 0, 4) });
            var timeRow = new WrapPanel();
            foreach (var t in new[] { null, "09:00", "12:00", "15:00", "18:00", "21:00" })
            {
                var tt = t;
                var chip = Chip(t ?? "No time", () => { _time = tt; _timeBox.Text = tt ?? ""; Refresh(); });
                chip.Tag = t ?? "";
                _timeChips.Children.Add(chip);
            }
            timeRow.Children.Add(_timeChips);
            stack.Children.Add(timeRow);
            var custom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            custom.Children.Add(new TextBlock { Text = "or type", FontFamily = ui, FontSize = 12, Foreground = Theme.FgDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) });
            custom.Children.Add(_timeBox);
            stack.Children.Add(custom);

            // buttons
            var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            if (_editing)
            {
                var remove = Button("Remove", false, Remove);
                DockPanel.SetDock(remove, Dock.Left);
                buttons.Children.Add(remove);
            }
            var set = Button("Set deadline", true, Commit);
            DockPanel.SetDock(set, Dock.Right);
            buttons.Children.Add(set);
            var cancel = Button("Cancel", false, () => _popup.IsOpen = false);
            DockPanel.SetDock(cancel, Dock.Right);
            buttons.Children.Add(cancel);
            stack.Children.Add(buttons);

            var frame = new Border
            {
                Background = Theme.Popup, BorderBrush = Theme.ChromeBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = stack, Focusable = true,
            };
            frame.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { _popup.IsOpen = false; e.Handled = true; }
                else if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            };
            return frame;
        }

        void Pick(DateTime d)
        {
            _date = d.Date;
            _month = new DateTime(d.Year, d.Month, 1);
            Refresh();
        }

        void Refresh()
        {
            _monthLabel.Text = _month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            _summary.Text = Due.Pretty(_time != null ? _date.Add(TimeSpan.Parse(_time)) : _date, _time != null)
                          + "  ·  " + Due.Countdown(_time != null ? _date.Add(TimeSpan.Parse(_time)) : _date.AddDays(1).AddSeconds(-1), DateTime.Now);
            _days.Children.Clear();
            int lead = ((int)_month.DayOfWeek + 6) % 7;     // Monday first
            var first = _month.AddDays(-lead);
            for (int i = 0; i < 42; i++)
            {
                var d = first.AddDays(i);
                bool inMonth = d.Month == _month.Month, sel = d == _date, today = d == DateTime.Today, past = d < DateTime.Today;
                var tb = new TextBlock
                {
                    Text = d.Day.ToString(), FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5,
                    Foreground = sel ? Brushes.White : !inMonth || past ? Theme.FgFaint : Theme.Fg,
                    FontWeight = sel || today ? FontWeights.SemiBold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                var cell = new Border
                {
                    Height = 30, Margin = new Thickness(1), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Child = tb,
                    Background = sel ? Theme.Accent : Brushes.Transparent,
                    BorderBrush = today && !sel ? Theme.Accent : Brushes.Transparent, BorderThickness = new Thickness(1),
                };
                var day = d;
                cell.MouseEnter += (s, e) => { if (!sel) cell.Background = Theme.Hover; };
                cell.MouseLeave += (s, e) => { if (!sel) cell.Background = Brushes.Transparent; };
                cell.MouseLeftButtonUp += (s, e) => { _date = day; if (day.Month != _month.Month) _month = new DateTime(day.Year, day.Month, 1); Refresh(); };
                cell.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { _date = day; Commit(); } };
                _days.Children.Add(cell);
            }
            foreach (Border chip in _timeChips.Children)
            {
                bool on = (string)chip.Tag == (_time ?? "");
                chip.Background = on ? Theme.AccentSoft : Brushes.Transparent;
                chip.BorderBrush = on ? Theme.Accent : Theme.ChromeBorder;
            }
        }

        // ---------------- write back ----------------

        void Commit()
        {
            var value = "due:" + _date.ToString("yyyy-MM-dd") + (_time != null ? " " + _time : "");
            Apply(value);
            _popup.IsOpen = false;
        }

        void Remove()
        {
            Apply(null);
            _popup.IsOpen = false;
        }

        /// Replaces the line's deadline, or appends one (dropping a dangling "due:" that was just typed).
        void Apply(string token)
        {
            if (_lineAnchor.IsDeleted) return;
            var doc = _ed.Document;
            var line = doc.GetLineByOffset(_lineAnchor.Offset);
            var text = doc.GetText(line);
            var m = Due.Token.Match(text);
            if (m.Success)
            {
                int start = m.Index, len = m.Length;
                if (token == null && start > 0 && text[start - 1] == ' ') { start--; len++; }
                doc.Replace(line.Offset + start, len, token ?? "");
            }
            else if (token != null)
            {
                var bare = System.Text.RegularExpressions.Regex.Match(text, @"(?<![\w@])due:\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (bare.Success) doc.Replace(line.Offset + bare.Index, bare.Length, token);
                else doc.Insert(line.EndOffset, (text.Length > 0 && !text.EndsWith(" ") ? " " : "") + token);
            }
            line = doc.GetLineByOffset(_lineAnchor.Offset);
            _ed.CaretOffset = line.EndOffset;
            // show the pill right away (normally the raw text shows while the caret is on the line)
            JustSetDoc = doc;
            JustSetCaret = _ed.CaretOffset;
            _ed.TextArea.TextView.Redraw(line);
            _ed.TextArea.Focus();
        }

        // ---------------- ui bits ----------------

        static Border Chip(string text, Action click)
        {
            var b = new Border
            {
                Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, 5, 5), CornerRadius = new CornerRadius(5),
                BorderBrush = Theme.ChromeBorder, BorderThickness = new Thickness(1), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, Foreground = Theme.Fg, FontFamily = new FontFamily("Segoe UI"), FontSize = 12 },
            };
            b.MouseEnter += (s, e) => { if (b.Background == Brushes.Transparent) b.Background = Theme.Hover; };
            b.MouseLeave += (s, e) => { if (b.Background == Theme.Hover) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        static Border NavButton(string glyph, Action click)
        {
            var b = new Border
            {
                Width = 28, Height = 26, CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new TextBlock { Text = glyph, Foreground = Theme.Fg, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) },
            };
            b.MouseEnter += (s, e) => b.Background = Theme.Hover;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        static Border Button(string text, bool primary, Action click)
        {
            var b = new Border
            {
                Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(6, 0, 0, 0), CornerRadius = new CornerRadius(5), Cursor = Cursors.Hand,
                Background = primary ? Theme.Accent : Brushes.Transparent,
                Child = new TextBlock { Text = text, Foreground = primary ? Brushes.White : text == "Remove" ? Theme.Bang : Theme.FgDim, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal },
            };
            b.MouseEnter += (s, e) => b.Opacity = 0.85;
            b.MouseLeave += (s, e) => b.Opacity = 1;
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }
    }
}
