using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Colors whole lines (tags, headings, done tasks) and inline tokens (@mention, #tag, dates, `code`).
    public sealed class TaskColorizer : DocumentColorizingTransformer
    {
        static readonly Regex Inline = new Regex(
            @"(?<m>(?<![\w@])@[\w.\-]+)|(?<h>(?<![\w#&])#(?!(?:[0-9A-Fa-f]{3,4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\b)[A-Za-z][\w\-/]*)|(?<d>\b\d{4}-\d{2}-\d{2}(?:[ T]\d{1,2}:\d{2})?\b|\b\d{1,2}:\d{2}\b)|(?<c>`[^`]+`)",
            RegexOptions.Compiled);

        // h1 … h6
        static readonly double[] HeadingScale = { 1, 1.6, 1.4, 1.25, 1.12, 1.04, 0.96 };

        protected override void ColorizeLine(DocumentLine line)
        {
            if (line.Length == 0 || Code.IsCodeLine(CurrentContext.Document, line.LineNumber)) return;
            var text = CurrentContext.Document.GetText(line);
            var info = LineParser.Parse(text);
            int o = line.Offset, end = line.EndOffset;

            if (info.IsRule)
            {
                Paint(o, end, Theme.FgFaint);
                return;
            }

            if (info.Heading > 0)
            {
                double scale = HeadingScale[info.Heading];
                var headBrush = info.Heading <= 2 ? Theme.Heading : info.Heading <= 4 ? Theme.Section : Theme.Fg;
                ChangeLinePart(o + info.Indent, end, el =>
                {
                    el.TextRunProperties.SetForegroundBrush(Theme.F(headBrush));
                    el.TextRunProperties.SetFontRenderingEmSize(el.TextRunProperties.FontRenderingEmSize * scale);
                    SetStyle(el, FontWeights.Bold, FontStyles.Normal);
                });
                Paint(o + info.Indent, o + info.Indent + info.Heading, Theme.FgFaint);
                InlineTokens(text, o, info.ContentStart);
                return;
            }

            if (info.CommentLen > 0)
                Paint(o + info.Indent, o + info.Indent + info.CommentLen, Theme.FgFaint);

            if (info.NumberStart >= 0)
                ChangeLinePart(o + info.NumberStart, o + info.NumberStart + info.NumberLen, el =>
                {
                    el.TextRunProperties.SetForegroundBrush(Theme.F(Theme.Accent));
                    SetStyle(el, FontWeights.Bold, FontStyles.Normal);
                });

            if (info.Section)
                ChangeLinePart(o + info.Indent, end, el =>
                {
                    el.TextRunProperties.SetForegroundBrush(Theme.F(Theme.Section));
                    SetStyle(el, FontWeights.Bold, FontStyles.Normal);
                });

            if (info.Tag != Tag.None && info.Tag != Tag.Dash && info.ContentStart < text.Length)
            {
                var brush = TagBrush(info.Tag);
                var weight = info.Tag == Tag.Bang || info.Tag == Tag.Slash || info.Tag == Tag.Todo || info.Tag == Tag.Arrow
                    ? FontWeights.Bold : FontWeights.Normal;
                var style = info.Tag == Tag.Question ? FontStyles.Italic : FontStyles.Normal;
                ChangeLinePart(o + info.ContentStart, end, el =>
                {
                    el.TextRunProperties.SetForegroundBrush(Theme.F(brush));
                    SetStyle(el, weight, style);
                });
            }

            if (info.IsDone)
            {
                if (info.ContentStart < text.Length)
                    ChangeLinePart(o + info.ContentStart, end, el =>
                    {
                        el.TextRunProperties.SetForegroundBrush(Theme.F(Theme.Done));
                        el.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
                    });
                return;
            }

            InlineTokens(text, o, info.ContentStart);
        }

        void InlineTokens(string text, int lineOffset, int from)
        {
            if (from >= text.Length) return;
            for (var m = Inline.Match(text, from); m.Success; m = m.NextMatch())
            {
                Brush b = m.Groups["m"].Success ? Theme.Mention
                        : m.Groups["h"].Success ? Theme.HashTag
                        : m.Groups["d"].Success ? Theme.Date
                        : Theme.Code;
                Paint(lineOffset + m.Index, lineOffset + m.Index + m.Length, b);
            }
        }

        void Paint(int start, int end, Brush b)
        {
            if (end > start) ChangeLinePart(start, end, el => el.TextRunProperties.SetForegroundBrush(Theme.F(b)));
        }

        static void SetStyle(VisualLineElement el, FontWeight w, FontStyle s)
        {
            var tf = el.TextRunProperties.Typeface;
            el.TextRunProperties.SetTypeface(new Typeface(tf.FontFamily, s, w, tf.Stretch));
        }

        public static Brush TagBrush(Tag t)
        {
            switch (t)
            {
                case Tag.Bang: return Theme.Bang;
                case Tag.Question: return Theme.Question;
                case Tag.Star: return Theme.Star;
                case Tag.Arrow: return Theme.Arrow;
                case Tag.Back: return Theme.Back;
                case Tag.Dash: return Theme.Dash;
                case Tag.Slash: return Theme.Slash;
                case Tag.Todo: return Theme.Todo;
                default: return Theme.Fg;
            }
        }
    }
}
