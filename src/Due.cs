using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Deadlines: "due:2026-10-10", "due:2026-10-10 18:00", "due:18:00", "@due(…)".
    /// Rendered as a live countdown pill; "due:+3d", "due:tomorrow"… are turned into dates as you type.
    public static class Due
    {
        public static readonly Regex Token = new Regex(
            @"(?<![\w@])(?:due:(?<v>\d{4}-\d{2}-\d{2}(?:[ T]\d{1,2}:\d{2})?|\d{1,2}:\d{2})|@due\((?<v>[^)\r\n]*)\))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex Relative = new Regex(
            @"(?<![\w@])(?<k>due:|@due\()(?<v>\+\s*\d+\s*(?:min|mins|minutes?|h|hrs?|hours?|d|days?|w|wks?|weeks?|mo|mos|months?|y|yrs?|years?)|today|tonight|tomorrow|tmr|mon(?:day)?|tue(?:sday)?|wed(?:nesday)?|thu(?:rsday)?|fri(?:day)?|sat(?:urday)?|sun(?:day)?|next ?week|next ?month)\)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// Deadline for a token value; date-only means the end of that day.
        public static bool TryParse(string v, out DateTime due, out bool hasTime)
        {
            v = v.Trim();
            hasTime = false;
            var fmts = new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd" };
            if (DateTime.TryParseExact(v, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out due))
            {
                hasTime = v.Length > 10;
                if (!hasTime) due = due.Date.AddDays(1).AddSeconds(-1);
                return true;
            }
            if (DateTime.TryParseExact(v, new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            {
                due = DateTime.Today.Add(t.TimeOfDay);
                hasTime = true;
                return true;
            }
            return false;
        }

        /// "in 3d 4h", "in 42m", "2d overdue".
        public static string Countdown(DateTime due, DateTime now)
        {
            var span = due - now;
            bool late = span < TimeSpan.Zero;
            var a = late ? -span : span;
            string s;
            if (a.TotalDays >= 60) s = $"{(int)(a.TotalDays / 30.4)}mo";
            else if (a.TotalDays >= 14) s = $"{(int)(a.TotalDays / 7)}w {(int)a.TotalDays % 7}d".Replace(" 0d", "");
            else if (a.TotalDays >= 1) s = $"{(int)a.TotalDays}d {a.Hours}h".Replace(" 0h", "");
            else if (a.TotalHours >= 1) s = $"{(int)a.TotalHours}h {a.Minutes}m".Replace(" 0m", "");
            else if (a.TotalMinutes >= 1) s = $"{(int)a.TotalMinutes}m";
            else return late ? "due now" : "<1m left";
            return late ? s + " overdue" : "in " + s;
        }

        public static string Pretty(DateTime due, bool hasTime)
        {
            var d = due.Date == DateTime.Today ? "Today" : due.Date == DateTime.Today.AddDays(1) ? "Tomorrow"
                  : due.Year == DateTime.Today.Year ? due.ToString("ddd d MMM", CultureInfo.InvariantCulture) : due.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            return hasTime ? d + " " + due.ToString("HH:mm") : d;
        }

        public static Brush StateBrush(DateTime due, bool done)
        {
            if (done) return Theme.Done;
            var left = due - DateTime.Now;
            if (left < TimeSpan.Zero) return Theme.Bang;
            if (left.TotalHours < 24) return Theme.Todo;
            if (left.TotalDays < 7) return Theme.Accent;
            return Theme.FgDim;
        }

        // ---------------- relative -> absolute while typing ----------------

        /// After a space / Enter: turns "due:+3d", "due:friday"… right before the caret into a date.
        public static bool ExpandBeforeCaret(TextEditor ed)
        {
            var doc = ed.Document;
            int caret = ed.CaretOffset;
            var line = doc.GetLineByOffset(caret);
            var before = doc.GetText(line.Offset, caret - line.Offset).TrimEnd(' ');
            var m = Relative.Match(before);
            if (!m.Success) return false;
            var when = Resolve(m.Groups["v"].Value, out bool withTime);
            if (when == null) return false;
            var value = withTime ? when.Value.ToString("yyyy-MM-dd HH:mm") : when.Value.ToString("yyyy-MM-dd");
            var text = m.Groups["k"].Value.StartsWith("@") ? "@due(" + value + ")" : "due:" + value;
            doc.Replace(line.Offset + m.Index, m.Length, text);
            return true;
        }

        /// Turns every relative due token in a text into a date (used for the tour note).
        public static string ExpandText(string text) =>
            Regex.Replace(text, @"(?<![\w@])due:(?<v>\+\d+(?:min|h|d|w|mo)|tomorrow|today|friday)", m =>
            {
                var when = Resolve(m.Groups["v"].Value, out bool withTime);
                return when == null ? m.Value : "due:" + when.Value.ToString(withTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd");
            });

        static DateTime? Resolve(string v, out bool withTime)
        {
            v = v.Trim().ToLowerInvariant();
            withTime = false;
            var now = DateTime.Now;
            var rel = Regex.Match(v, @"^\+\s*(\d+)\s*([a-z]+)$");
            if (rel.Success)
            {
                int n = int.Parse(rel.Groups[1].Value);
                var u = rel.Groups[2].Value;
                if (u.StartsWith("min")) { withTime = true; return Round(now.AddMinutes(n)); }
                if (u.StartsWith("h")) { withTime = true; return Round(now.AddHours(n)); }
                if (u.StartsWith("d")) return now.Date.AddDays(n);
                if (u.StartsWith("w")) return now.Date.AddDays(7 * n);
                if (u.StartsWith("mo")) return now.Date.AddMonths(n);
                if (u.StartsWith("y")) return now.Date.AddYears(n);
                return null;
            }
            switch (v.Replace(" ", ""))
            {
                case "today": return now.Date;
                case "tonight": withTime = true; return now.Date.AddHours(20);
                case "tomorrow": case "tmr": return now.Date.AddDays(1);
                case "nextweek": return now.Date.AddDays(7);
                case "nextmonth": return now.Date.AddMonths(1);
            }
            var days = new[] { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };
            int idx = Array.FindIndex(days, d => v.StartsWith(d));
            if (idx >= 0)
            {
                int diff = (idx - (int)now.DayOfWeek + 7) % 7;
                return now.Date.AddDays(diff == 0 ? 7 : diff);
            }
            return null;
        }

        static DateTime Round(DateTime t) => new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0).AddMinutes(t.Second > 0 ? 1 : 0);

        // ---------------- scanning / ticking ----------------

        public sealed class Item { public DateTime When; public bool Done; public string Text; }

        public static IEnumerable<Item> Scan(TextDocument doc)
        {
            if (Code.IsCodeFile(doc.FileName)) yield break;
            foreach (var line in doc.Lines)
            {
                if (line.Length < 8) continue;
                var text = doc.GetText(line);
                if (text.IndexOf("due", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (Code.FenceAt(doc, line.LineNumber) != null) continue;
                var m = Token.Match(text);
                if (!m.Success || !TryParse(m.Groups["v"].Value, out var when, out _)) continue;
                var info = LineParser.Parse(text);
                var label = Token.Replace(text.Substring(Math.Min(info.ContentStart, text.Length)), "").Trim();
                yield return new Item { When = when, Done = info.IsDone, Text = label.Length > 60 ? label.Substring(0, 57) + "…" : label };
            }
        }

        static DispatcherTimer _timer;
        static DateTime _lastTick = DateTime.Now;

        /// Every 30 s: refresh countdowns and status bars, and toast deadlines that just arrived.
        public static void Start()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _timer.Tick += (s, e) =>
            {
                var now = DateTime.Now;
                foreach (var d in Workspace.Docs.ToList())
                    foreach (var it in Scan(d.Document))
                        if (!it.Done && it.When > _lastTick && it.When <= now)
                            Workspace.LastActive?.Toast("⏰ Due now: " + (it.Text.Length > 0 ? it.Text : d.Name), seconds: 12);
                _lastTick = now;
                foreach (var v in Workspace.AllViews) v.Editor.TextArea.TextView.Redraw();
                foreach (var w in Workspace.Windows) w.UpdateStats();
            };
            _timer.Start();
        }
    }

    /// Draws due tokens as a countdown pill (raw text while the caret is on that line).
    public sealed class DueGenerator : VisualLineElementGenerator
    {
        readonly Func<int> _caretLine;
        readonly TextEditor _ed;
        public DueGenerator(TextEditor ed, Func<int> caretLine) { _ed = ed; _caretLine = caretLine; }

        Match Next(int startOffset, out DocumentLine line)
        {
            line = CurrentContext.Document.GetLineByOffset(startOffset);
            if ((line.LineNumber == _caretLine() && !DuePicker.JustSet(_ed)) || Code.IsCodeLine(CurrentContext.Document, line.LineNumber)) return null;
            var text = CurrentContext.Document.GetText(line);
            int rel = startOffset - line.Offset;
            if (text.IndexOf("due", rel, StringComparison.OrdinalIgnoreCase) < 0) return null;
            for (var m = Due.Token.Match(text, rel); m.Success; m = m.NextMatch())
                if (Due.TryParse(m.Groups["v"].Value, out _, out _)) return m;
            return null;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var m = Next(startOffset, out var line);
            return m == null ? -1 : line.Offset + m.Index;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            var m = Next(offset, out var line);
            if (m == null || line.Offset + m.Index != offset) return null;
            Due.TryParse(m.Groups["v"].Value, out var when, out var hasTime);
            bool done = LineParser.Parse(CurrentContext.Document.GetText(line)).IsDone;
            var tv = CurrentContext.TextView;
            var brush = Due.StateBrush(when, done);
            double fs = Math.Max(10, tv.DefaultLineHeight * 0.55);

            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = "⏰", FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = fs * 0.95, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = Due.Pretty(when, hasTime), Foreground = Theme.Fg, FontFamily = new FontFamily("Segoe UI"), FontSize = fs, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock
            {
                Text = "  ·  " + (done ? "done" : Due.Countdown(when, DateTime.Now)),
                Foreground = brush, FontFamily = new FontFamily("Segoe UI"), FontSize = fs, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            });
            var pill = new Border
            {
                Child = sp,
                Height = Math.Round(tv.DefaultLineHeight - 2),
                Padding = new Thickness(7, 0, 9, 0),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = brush,
                Background = Theme.Hover,
                ToolTip = when.ToString(hasTime ? "dddd, d MMMM yyyy  HH:mm" : "dddd, d MMMM yyyy", CultureInfo.InvariantCulture) + (done ? "" : "  —  " + Due.Countdown(when, DateTime.Now)) + "\nclick to change",
                Opacity = done ? 0.7 : 1,
            };
            TextBlock.SetBaselineOffset(pill, tv.DefaultBaseline - 1);
            pill.Cursor = System.Windows.Input.Cursors.Hand;
            int lineNo = line.LineNumber;
            pill.MouseLeftButtonDown += (s, e) => e.Handled = true;
            pill.MouseLeftButtonUp += (s, e) => { e.Handled = true; DuePicker.Show(_ed, lineNo, offset); };
            return new InlineObjectElement(m.Length, pill);
        }
    }
}
