using System.Collections.Generic;

namespace TaskPad
{
    public enum Tag { None, Bang, Question, Star, Arrow, Back, Dash, Slash, Todo }
    public enum Check { None, Open, Done, Doing, Cancelled }
    public enum TokKind { Rule, HiddenBullet, Checkbox, Tag }

    public struct Tok
    {
        public TokKind Kind;
        public int Start, Length;
        public Tok(TokKind k, int s, int l) { Kind = k; Start = s; Length = l; }
    }

    /// Recognizes the "markup" at the start of a line. Offsets are relative to the line start.
    public sealed class LineInfo
    {
        public int Indent;
        public bool IsRule;
        public int Heading;            // 1..3 for #, ##, ###
        public int CommentLen;         // optional leading "// "
        public Check Check;
        public int CheckStart = -1;
        public Tag Tag;
        public int TagStart = -1, TagLen;
        public int NumberStart = -1, NumberLen;
        public int ContentStart;
        public bool Section;           // "Something:"
        public readonly List<Tok> Tokens = new List<Tok>(3);

        public bool IsDone => Check == Check.Done || Check == Check.Cancelled;
    }

    public static class LineParser
    {
        public static LineInfo Parse(string t)
        {
            var info = new LineInfo();
            int n = t.Length, i = 0;
            while (i < n && (t[i] == ' ' || t[i] == '\t')) i++;
            info.Indent = i;
            info.ContentStart = i;
            if (i == n) return info;

            // --- / === / ___ / ***  horizontal rule
            int end = n;
            while (end > i && t[end - 1] == ' ') end--;
            if (end - i >= 3 && "-=_*".IndexOf(t[i]) >= 0)
            {
                bool same = true;
                for (int k = i + 1; k < end; k++) if (t[k] != t[i]) { same = false; break; }
                if (same)
                {
                    info.IsRule = true;
                    info.Tokens.Add(new Tok(TokKind.Rule, i, end - i));
                    return info;
                }
            }

            // # Heading
            if (t[i] == '#')
            {
                int h = 0;
                while (i + h < n && t[i + h] == '#' && h < 4) h++;
                if (h <= 3 && i + h < n && t[i + h] == ' ')
                {
                    info.Heading = h;
                    info.ContentStart = Skip(t, i + h);
                    return info;
                }
            }

            // optional "//" prefix, better-comments style
            if (i + 1 < n && t[i] == '/' && t[i + 1] == '/')
            {
                info.CommentLen = Skip(t, i + 2) - i;
                i += info.CommentLen;
                info.ContentStart = i;
            }

            // "- [ ]" / "* [x]" / "[ ]"
            int j = i;
            if (j + 1 < n && "-*+".IndexOf(t[j]) >= 0 && t[j + 1] == ' ')
            {
                int k = Skip(t, j + 1);
                if (IsBox(t, k))
                {
                    info.Tokens.Add(new Tok(TokKind.HiddenBullet, j, k - j));
                    j = k;
                }
            }
            if (IsBox(t, j))
            {
                info.CheckStart = j;
                info.Check = StateOf(t[j + 1]);
                info.Tokens.Add(new Tok(TokKind.Checkbox, j, 3));
                i = Skip(t, j + 3);
                info.ContentStart = i;
            }
            else if (info.Tokens.Count > 0)
            {
                info.Tokens.Clear();
            }

            // 1. / 2)
            if (info.Check == Check.None)
            {
                int d = i;
                while (d < n && d - i < 3 && char.IsDigit(t[d])) d++;
                if (d > i && d + 1 < n && (t[d] == '.' || t[d] == ')') && t[d + 1] == ' ')
                {
                    info.NumberStart = i;
                    info.NumberLen = d + 1 - i;
                    info.ContentStart = Skip(t, d + 1);
                    return info;
                }
            }

            // tag markers: ! ? * > < - / todo
            if (i < n)
            {
                Tag tag = Tag.None;
                int len = 1;
                if (n - i >= 4 && string.Compare(t, i, "todo", 0, 4, true) == 0 &&
                    (i + 4 == n || t[i + 4] == ':' || t[i + 4] == ' '))
                {
                    tag = Tag.Todo;
                    len = i + 4 < n && t[i + 4] == ':' ? 5 : 4;
                }
                else if (i + 1 == n || t[i + 1] == ' ')
                {
                    switch (t[i])
                    {
                        case '!': tag = Tag.Bang; break;
                        case '?': tag = Tag.Question; break;
                        case '*': tag = Tag.Star; break;
                        case '>': tag = Tag.Arrow; break;
                        case '<': tag = Tag.Back; break;
                        case '-': tag = Tag.Dash; break;
                        case '/': tag = Tag.Slash; break;
                    }
                }
                if (tag != Tag.None)
                {
                    info.Tag = tag;
                    info.TagStart = i;
                    info.TagLen = len;
                    info.Tokens.Add(new Tok(TokKind.Tag, i, len));
                    info.ContentStart = Skip(t, i + len);
                    return info;
                }
            }

            // "Project name:"  -> section
            if (info.Check == Check.None && info.CommentLen == 0 && end - i <= 60 && end > i + 1 &&
                t[end - 1] == ':' && t.IndexOf("://", i) < 0)
                info.Section = true;

            return info;
        }

        static bool IsBox(string t, int k) =>
            k + 2 < t.Length && t[k] == '[' && t[k + 2] == ']' && " xX/-".IndexOf(t[k + 1]) >= 0 &&
            (k + 3 == t.Length || t[k + 3] == ' ');

        static Check StateOf(char c)
        {
            switch (c)
            {
                case 'x': case 'X': return Check.Done;
                case '/': return Check.Doing;
                case '-': return Check.Cancelled;
                default: return Check.Open;
            }
        }

        static int Skip(string t, int i)
        {
            while (i < t.Length && t[i] == ' ') i++;
            return i;
        }
    }
}
