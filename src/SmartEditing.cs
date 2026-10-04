using System;
using System.Text.RegularExpressions;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace TaskPad
{
    /// List-aware editing: continue tasks/bullets on Enter, [] -> [ ], Ctrl+Enter toggles,
    /// Tab indents list items, VS Code style line moves.
    public static class SmartEditing
    {
        static readonly Regex ListPrefix = new Regex(@"^(\s*)((?:[-*+]\s+)?\[[ xX/-]\] ?|[-*+] +|(\d{1,3})([.)]) +)", RegexOptions.Compiled);

        public static Func<int> IndentSize = () => 2;

        public static void Attach(TextEditor ed)
        {
            ed.TextArea.PreviewKeyDown += (s, e) => OnKey(ed, e);
            ed.TextArea.TextEntered += (s, e) =>
            {
                if (e.Text == "]") ExpandEmptyBox(ed);
            };
        }

        static void OnKey(TextEditor ed, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool handled = true;

            if (key == Key.Enter && mods == ModifierKeys.None) handled = ContinueList(ed);
            else if (key == Key.Enter && mods == ModifierKeys.Control) ToggleLines(ed);
            else if (key == Key.Tab && mods == ModifierKeys.None) handled = IndentListLine(ed, +1);
            else if (key == Key.Tab && mods == ModifierKeys.Shift) handled = IndentListLine(ed, -1);
            else if (key == Key.Up && mods == ModifierKeys.Alt) MoveLines(ed, -1);
            else if (key == Key.Down && mods == ModifierKeys.Alt) MoveLines(ed, +1);
            else if (key == Key.Up && mods == (ModifierKeys.Alt | ModifierKeys.Shift)) DuplicateLines(ed, false);
            else if (key == Key.Down && mods == (ModifierKeys.Alt | ModifierKeys.Shift)) DuplicateLines(ed, true);
            else if (key == Key.K && mods == (ModifierKeys.Control | ModifierKeys.Shift)) DeleteLines(ed);
            else if (key == Key.F5 && mods == ModifierKeys.None) ed.TextArea.Selection.ReplaceSelectionWithText(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            else if (key == Key.OemSemicolon && mods == ModifierKeys.Control) ed.TextArea.Selection.ReplaceSelectionWithText(DateTime.Now.ToString("yyyy-MM-dd"));
            else handled = false;

            if (handled) e.Handled = true;
        }

        // ---------- checkbox state ----------

        public static void SetCheck(TextDocument doc, int boxOffset, Check state)
        {
            if (boxOffset + 2 >= doc.TextLength || doc.GetCharAt(boxOffset) != '[') return;
            char c = state == Check.Done ? 'x' : state == Check.Doing ? '/' : state == Check.Cancelled ? '-' : ' ';
            doc.Replace(boxOffset + 1, 1, c.ToString());
        }

        static void ToggleLines(TextEditor ed)
        {
            var doc = ed.Document;
            GetLineRange(ed, out int first, out int last);
            bool anyOpen = false, allPlain = true;
            for (int n = first; n <= last; n++)
            {
                var info = LineParser.Parse(doc.GetText(doc.GetLineByNumber(n)));
                if (info.Check != Check.None) allPlain = false;
                if (info.Check == Check.Open || info.Check == Check.Doing) anyOpen = true;
            }
            using (doc.RunUpdate())
            {
                for (int n = first; n <= last; n++)
                {
                    var line = doc.GetLineByNumber(n);
                    var text = doc.GetText(line);
                    var info = LineParser.Parse(text);
                    if (info.Check != Check.None)
                        SetCheck(doc, line.Offset + info.CheckStart, anyOpen ? Check.Done : Check.Open);
                    else if (allPlain && (text.Trim().Length > 0 || first == last))
                    {
                        // turn "- foo" into "[ ] foo", plain "foo" into "[ ] foo"
                        int at = info.Indent, remove = 0;
                        if (info.Tag == Tag.Dash || info.Tag == Tag.Star) remove = info.ContentStart - info.TagStart;
                        doc.Replace(line.Offset + at, remove, "[ ] ");
                    }
                }
            }
        }

        static void ExpandEmptyBox(TextEditor ed)
        {
            var doc = ed.Document;
            int caret = ed.CaretOffset;
            if (caret < 2 || doc.GetText(caret - 2, 2) != "[]") return;
            var line = doc.GetLineByOffset(caret);
            var before = doc.GetText(line.Offset, caret - 2 - line.Offset).Trim();
            if (before.Length != 0 && before != "-" && before != "*" && before != "+" && before != "//") return;
            bool spaceAfter = caret < line.EndOffset && doc.GetCharAt(caret) == ' ';
            doc.Replace(caret - 2, 2, spaceAfter ? "[ ]" : "[ ] ");
            ed.CaretOffset = caret - 2 + 4;
        }

        // ---------- lists ----------

        static bool ContinueList(TextEditor ed)
        {
            if (!ed.TextArea.Selection.IsEmpty) return false;
            var doc = ed.Document;
            int caret = ed.CaretOffset;
            var line = doc.GetLineByOffset(caret);
            var text = doc.GetText(line);
            var m = ListPrefix.Match(text);
            if (!m.Success || caret - line.Offset < m.Length) return false;

            string indent = m.Groups[1].Value, marker = m.Groups[2].Value;
            if (text.Substring(m.Length).Trim().Length == 0)
            {
                // Enter on an empty item: outdent, or end the list.
                if (indent.Length > 0)
                {
                    int cut = Math.Min(indent.Length, IndentSize());
                    doc.Remove(line.Offset, cut);
                }
                else doc.Replace(line.Offset, line.Length, "");
                return true;
            }

            string next;
            if (m.Groups[3].Success)
                next = (int.Parse(m.Groups[3].Value) + 1) + m.Groups[4].Value + " ";
            else
                next = Regex.Replace(marker, @"\[[^\]]\] ?", "[ ] ");
            if (!next.EndsWith(" ")) next += " ";

            var nl = TextUtilities.GetNewLineFromDocument(doc, line.LineNumber);
            ed.TextArea.Selection.ReplaceSelectionWithText(nl + indent + next);
            ed.TextArea.Caret.BringCaretToView();
            return true;
        }

        /// Tab on a plain line right under a task turns it into a subtask: "  [ ] text".
        static bool MakeSubtask(TextEditor ed, int lineNumber)
        {
            var doc = ed.Document;
            LineInfo parent = null;
            for (int n = lineNumber - 1; n >= 1; n--)
            {
                var t = doc.GetText(doc.GetLineByNumber(n));
                if (t.Trim().Length == 0) continue;
                parent = LineParser.Parse(t);
                break;
            }
            if (parent == null || parent.Check == Check.None) return false;

            var line = doc.GetLineByNumber(lineNumber);
            var info = LineParser.Parse(doc.GetText(line));
            string indent = new string(' ', parent.Indent + IndentSize());
            doc.Replace(line.Offset, info.Indent, indent + "[ ] ");
            ed.CaretOffset = Math.Max(ed.CaretOffset, line.Offset + indent.Length + 4);
            return true;
        }

        static bool IndentListLine(TextEditor ed, int dir)
        {
            var doc = ed.Document;
            GetLineRange(ed, out int first, out int last);
            if (first == last)
            {
                var info = LineParser.Parse(doc.GetText(doc.GetLineByNumber(first)));
                bool isList = info.Check != Check.None || info.Tag != Tag.None || info.NumberStart >= 0;
                if (!isList && dir > 0 && MakeSubtask(ed, first)) return true;
                if (!isList) return false;
            }
            else if (dir > 0) return false; // let AvalonEdit indent multi-line selections

            int size = IndentSize();
            using (doc.RunUpdate())
            {
                for (int n = first; n <= last; n++)
                {
                    var line = doc.GetLineByNumber(n);
                    if (dir > 0) doc.Insert(line.Offset, new string(' ', size));
                    else
                    {
                        int k = 0;
                        while (k < size && k < line.Length && doc.GetCharAt(line.Offset + k) == ' ') k++;
                        if (k == 0 && line.Length > 0 && doc.GetCharAt(line.Offset) == '\t') k = 1;
                        if (k > 0) doc.Remove(line.Offset, k);
                    }
                }
            }
            return true;
        }

        // ---------- VS Code style line ops ----------

        public static void GetLineRange(TextEditor ed, out int first, out int last)
        {
            var sel = ed.TextArea.Selection;
            if (sel.IsEmpty)
            {
                first = last = ed.TextArea.Caret.Line;
                return;
            }
            var seg = sel.SurroundingSegment;
            first = ed.Document.GetLineByOffset(seg.Offset).LineNumber;
            var lastLine = ed.Document.GetLineByOffset(seg.EndOffset);
            last = lastLine.LineNumber;
            if (seg.EndOffset == lastLine.Offset && last > first) last--;
        }

        static void MoveLines(TextEditor ed, int dir)
        {
            var doc = ed.Document;
            GetLineRange(ed, out int first, out int last);
            if (dir < 0 && first == 1 || dir > 0 && last == doc.LineCount) return;

            var caret = ed.TextArea.Caret;
            int caretLine = caret.Line, caretCol = caret.Column;
            bool hadSel = !ed.TextArea.Selection.IsEmpty;
            var a = doc.GetLineByNumber(first);
            var b = doc.GetLineByNumber(last);
            string block = doc.GetText(a.Offset, b.Length + b.Offset - a.Offset);

            using (doc.RunUpdate())
            {
                if (dir < 0)
                {
                    var prev = doc.GetLineByNumber(first - 1);
                    string nl = doc.GetText(prev.EndOffset, prev.DelimiterLength);
                    doc.Replace(prev.Offset, b.EndOffset - prev.Offset, block + nl + doc.GetText(prev));
                }
                else
                {
                    var next = doc.GetLineByNumber(last + 1);
                    string nl = doc.GetText(b.EndOffset, b.DelimiterLength);
                    doc.Replace(a.Offset, next.EndOffset - a.Offset, doc.GetText(next) + nl + block);
                }
            }
            first += dir; last += dir;
            caret.Position = new ICSharpCode.AvalonEdit.TextViewPosition(caretLine + dir, caretCol);
            if (hadSel)
            {
                var s = doc.GetLineByNumber(first).Offset;
                var e = doc.GetLineByNumber(last).EndOffset;
                ed.Select(s, e - s);
            }
            caret.BringCaretToView();
        }

        static void DuplicateLines(TextEditor ed, bool down)
        {
            var doc = ed.Document;
            GetLineRange(ed, out int first, out int last);
            var a = doc.GetLineByNumber(first);
            var b = doc.GetLineByNumber(last);
            string block = doc.GetText(a.Offset, b.EndOffset - a.Offset);
            var nl = TextUtilities.GetNewLineFromDocument(doc, first);
            int caretLine = ed.TextArea.Caret.Line, caretCol = ed.TextArea.Caret.Column;
            doc.Insert(b.EndOffset, nl + block);
            if (down)
                ed.TextArea.Caret.Position = new ICSharpCode.AvalonEdit.TextViewPosition(caretLine + (last - first + 1), caretCol);
            ed.TextArea.Caret.BringCaretToView();
        }

        static void DeleteLines(TextEditor ed)
        {
            var doc = ed.Document;
            GetLineRange(ed, out int first, out int last);
            var a = doc.GetLineByNumber(first);
            var b = doc.GetLineByNumber(last);
            int start = a.Offset, end = b.EndOffset + b.DelimiterLength;
            if (b.DelimiterLength == 0 && first > 1)
            {
                var prev = doc.GetLineByNumber(first - 1);
                start = prev.EndOffset;
            }
            doc.Remove(start, end - start);
        }
    }
}
