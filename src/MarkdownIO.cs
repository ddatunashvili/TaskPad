using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TaskPad
{
    /// Import Markdown into TaskPad syntax, export to standard Markdown or clean plain text.
    public static class MarkdownIO
    {
        // ---------------- import ----------------

        public static void Import(TaskWindow w)
        {
            var dlg = new OpenFileDialog { Filter = "Markdown (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*" };
            if (dlg.ShowDialog(w) != true) return;
            string md;
            try { md = Doc.ReadText(dlg.FileName, out _); }
            catch (Exception ex) { w.Toast("Cannot read file: " + ex.Message); return; }
            var t = w.NewTab(null, FromMarkdown(md));
            t.Doc.UntitledName = Path.GetFileNameWithoutExtension(dlg.FileName);
            t.Doc.Raise();
            w.Toast("Imported " + Path.GetFileName(dlg.FileName) + " — save it with Ctrl+S");
        }

        static readonly Regex MdTask = new Regex(@"^(\s*)[-*+]\s+\[([ xX])\]\s?", RegexOptions.Compiled);
        static readonly Regex MdBullet = new Regex(@"^(\s*)[*+]\s+", RegexOptions.Compiled);

        public static string FromMarkdown(string md)
        {
            var sb = new StringBuilder();
            bool fence = false;
            foreach (var raw in md.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.TrimStart().StartsWith("```")) { fence = !fence; sb.AppendLine(line); continue; }
                if (fence) { sb.AppendLine(line); continue; }
                var m = MdTask.Match(line);
                if (m.Success) line = m.Groups[1].Value + "[" + (m.Groups[2].Value == " " ? " " : "x") + "] " + line.Substring(m.Length);
                else if ((m = MdBullet.Match(line)).Success) line = m.Groups[1].Value + "- " + line.Substring(m.Length); // "*" is a star in TaskPad
                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        // ---------------- export ----------------

        public static void ExportMarkdown(TaskWindow w, Doc doc) => Export(w, doc, "md", "Markdown (*.md)|*.md", ToMarkdown);
        public static void ExportText(TaskWindow w, Doc doc) => Export(w, doc, "txt", "Text (*.txt)|*.txt", ToPlainText);

        static void Export(TaskWindow w, Doc doc, string ext, string filter, Func<Doc, string> convert)
        {
            if (doc == null) return;
            var dlg = new SaveFileDialog
            {
                Filter = filter,
                FileName = Path.GetFileNameWithoutExtension(doc.Name) + (ext == "txt" ? " (plain)" : "") + "." + ext,
                InitialDirectory = doc.Path != null ? Path.GetDirectoryName(doc.Path) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            };
            if (dlg.ShowDialog(w) != true) return;
            if (doc.Path != null && string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(doc.Path), StringComparison.OrdinalIgnoreCase))
            { w.Toast("Pick a different file name — that's the note itself"); return; }
            try
            {
                File.WriteAllText(dlg.FileName, convert(doc), new UTF8Encoding(false));
                var path = dlg.FileName;
                w.Toast("Exported " + Path.GetFileName(path), "Open", () => w.OpenFile(path), seconds: 6);
            }
            catch (Exception ex) { w.Toast("Export failed: " + ex.Message); }
        }

        static readonly Regex Comment = new Regex(@"\{==(?<t>.+?)==\}\{>>(?<c>.*?)<<\}|\{>>(?<c>.*?)<<\}", RegexOptions.Compiled);
        static readonly Regex Image = new Regex(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);

        public static string ToMarkdown(Doc doc)
        {
            var sb = new StringBuilder();
            var notes = new List<string>();
            string prev = null;
            foreach (var line in doc.Document.Lines)
            {
                var text = doc.Document.GetText(line);
                var info = LineParser.Parse(text);
                string ind = text.Substring(0, info.Indent);
                string content = info.ContentStart < text.Length ? text.Substring(info.ContentStart) : "";
                content = Comment.Replace(content, m =>
                {
                    var thread = CommentThread.Parse(m.Groups["c"].Value);
                    var parts = new List<string>();
                    foreach (var e in thread) parts.Add((e.Author != null ? "**" + e.Author + "**: " : "") + e.Text.Replace("\n", " "));
                    notes.Add(string.Join(" — ", parts));
                    return (m.Groups["t"].Success ? m.Groups["t"].Value : "") + $"[^{notes.Count}]";
                });
                // keep inline image width hints out of standard markdown
                content = Image.Replace(content, m => $"![{m.Groups[1].Value.Split('|')[0]}]({m.Groups[2].Value})");

                string outLine;
                if (text.Trim().Length == 0) outLine = "";
                else if (info.IsRule) outLine = "---";
                else if (info.Heading > 0) outLine = new string('#', info.Heading) + " " + content;
                else if (info.Check != Check.None)
                {
                    switch (info.Check)
                    {
                        case Check.Done: outLine = ind + "- [x] " + content; break;
                        case Check.Doing: outLine = ind + "- [ ] " + content + " *(in progress)*"; break;
                        case Check.Cancelled: outLine = ind + "- [ ] ~~" + content + "~~"; break;
                        default: outLine = ind + "- [ ] " + content; break;
                    }
                }
                else if (info.Tag != Tag.None)
                {
                    switch (info.Tag)
                    {
                        case Tag.Bang: outLine = ind + "- **⚠ " + content + "**"; break;
                        case Tag.Question: outLine = ind + "- *❓ " + content + "*"; break;
                        case Tag.Todo: outLine = ind + "- **TODO:** " + content; break;
                        case Tag.Star: outLine = ind + "- ⭐ " + content; break;
                        case Tag.Arrow: outLine = ind + "- ➜ " + content; break;
                        case Tag.Back: outLine = ind + "- ⏳ " + content; break;
                        case Tag.Slash: outLine = ind + "- ✅ " + content; break;
                        default: outLine = ind + "- " + content; break;
                    }
                }
                else if (info.NumberStart >= 0) outLine = ind + text.Substring(info.NumberStart, info.NumberLen) + " " + content;
                else if (info.Section) outLine = "**" + text.Trim() + "**";
                else outLine = ind + content;

                // consecutive plain lines would merge into one paragraph in markdown: force line breaks
                bool plain = outLine.Length > 0 && !outLine.TrimStart().StartsWith("-") && !outLine.StartsWith("#") && !Regex.IsMatch(outLine.TrimStart(), @"^\d+[.)] ");
                if (plain && prev != null && prev.Length > 0) sb.Length -= Environment.NewLine.Length; // remove newline, add hard break
                if (plain && prev != null && prev.Length > 0) sb.Append("  " + Environment.NewLine);
                sb.AppendLine(outLine);
                prev = plain ? outLine : "";
            }
            if (notes.Count > 0)
            {
                sb.AppendLine();
                for (int i = 0; i < notes.Count; i++) sb.AppendLine($"[^{i + 1}]: {notes[i]}");
            }
            return sb.ToString();
        }

        public static string ToPlainText(Doc doc)
        {
            var sb = new StringBuilder();
            foreach (var line in doc.Document.Lines)
            {
                var text = doc.Document.GetText(line);
                var info = LineParser.Parse(text);
                string ind = text.Substring(0, info.Indent);
                string content = info.ContentStart < text.Length ? text.Substring(info.ContentStart) : "";
                content = Comment.Replace(content, m => m.Groups["t"].Success ? m.Groups["t"].Value : "");
                content = Image.Replace(content, m => "[image: " + Path.GetFileName(m.Groups[2].Value) + "]");

                if (text.Trim().Length == 0) { sb.AppendLine(); continue; }
                if (info.IsRule) { sb.AppendLine(new string(text.Trim()[0] == '=' ? '=' : '-', 40)); continue; }
                if (info.Heading > 0)
                {
                    sb.AppendLine(info.Heading <= 2 ? content.ToUpperInvariant() : content);
                    if (info.Heading <= 2) sb.AppendLine(new string(info.Heading == 1 ? '=' : '-', Math.Max(3, content.Length)));
                    continue;
                }
                string lead = "";
                if (info.Check != Check.None)
                    lead = info.Check == Check.Done ? "☑ " : info.Check == Check.Doing ? "◐ " : info.Check == Check.Cancelled ? "☒ " : "☐ ";
                switch (info.Tag)
                {
                    case Tag.Bang: lead += "(!) "; break;
                    case Tag.Question: lead += "(?) "; break;
                    case Tag.Todo: lead += "TODO: "; break;
                    case Tag.Star: lead += "★ "; break;
                    case Tag.Arrow: lead += "→ "; break;
                    case Tag.Back: lead += "← "; break;
                    case Tag.Slash: lead += "✓ "; break;
                    case Tag.Dash: lead += "• "; break;
                }
                if (info.NumberStart >= 0) lead += text.Substring(info.NumberStart, info.NumberLen) + " ";
                sb.AppendLine(ind + lead + (info.Section ? text.Trim() : content));
            }
            return sb.ToString();
        }
    }
}
