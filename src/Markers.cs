using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit;

namespace TaskPad
{
    /// Applies a line marker ("[ ] ", "! ", "1. " …) to the current line or every selected line.
    public static class Markers
    {
        /// Sets `syntax` as the marker of each line in the selection (or the caret line).
        /// If every line already has that marker it is removed instead (toggle).
        /// `syntax == null` clears markers.
        public static void Apply(TextEditor ed, string syntax)
        {
            var doc = ed.Document;
            SmartEditing.GetLineRange(ed, out int first, out int last);
            bool multi = last > first;
            bool numbered = syntax != null && Regex.IsMatch(syntax, @"^\d+[.)] $");

            bool allHave = syntax != null;
            for (int n = first; n <= last && allHave; n++)
            {
                var text = doc.GetText(doc.GetLineByNumber(n));
                if (multi && text.Trim().Length == 0) continue;
                allHave = Same(MarkerOf(text, LineParser.Parse(text)), syntax);
            }
            if (allHave) syntax = null;

            int counter = 1;
            using (doc.RunUpdate())
            {
                for (int n = first; n <= last; n++)
                {
                    var line = doc.GetLineByNumber(n);
                    var text = doc.GetText(line);
                    if (multi && text.Trim().Length == 0) continue;
                    var info = LineParser.Parse(text);
                    int start = info.Indent + info.CommentLen;
                    int end = HasMarker(info) ? info.ContentStart : start;
                    if (info.IsRule) end = text.Length;
                    string s = syntax == null ? "" : numbered ? (counter++) + ". " : syntax;
                    doc.Replace(line.Offset + start, end - start, s);
                }
            }

            var a = doc.GetLineByNumber(first);
            var b = doc.GetLineByNumber(last);
            if (multi) ed.Select(a.Offset, b.EndOffset - a.Offset);
            else ed.CaretOffset = b.EndOffset;
        }

        static bool HasMarker(LineInfo i) =>
            i.Check != Check.None || i.Tag != Tag.None || i.NumberStart >= 0 || i.Heading > 0;

        static string MarkerOf(string text, LineInfo i) =>
            HasMarker(i) ? text.Substring(i.Indent + i.CommentLen, i.ContentStart - i.Indent - i.CommentLen) : "";

        static bool Same(string marker, string syntax)
        {
            string a = marker.Trim(), b = syntax.Trim();
            if (Regex.IsMatch(b, @"^\d+[.)]$")) return Regex.IsMatch(a, @"^\d+[.)]$");
            return string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
