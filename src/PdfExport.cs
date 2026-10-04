using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace TaskPad
{
    /// Export: renders the note to styled HTML and prints it to PDF with Edge (or Chrome) headless,
    /// both of which ship with / are common on Windows. Falls back to opening the HTML for manual printing.
    public static class PdfExport
    {
        public static void Run(TaskWindow owner, Doc doc)
        {
            if (doc == null) return;
            var dlg = new SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = Path.GetFileNameWithoutExtension(doc.Name) + ".pdf",
                InitialDirectory = doc.Path != null ? Path.GetDirectoryName(doc.Path) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            };
            if (dlg.ShowDialog(owner) != true) return;
            var pdf = dlg.FileName;

            var html = Path.Combine(Path.GetTempPath(), "TaskPad-export-" + Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(html, ToHtml(doc), new UTF8Encoding(false));
            var browser = FindBrowser();
            if (browser == null)
            {
                Process.Start(new ProcessStartInfo(html) { UseShellExecute = true });
                owner.Toast("No Edge/Chrome found — opened HTML, use Print → Save as PDF");
                return;
            }

            owner.Toast("Exporting PDF…", seconds: 10);
            Task.Run(() =>
            {
                try
                {
                    if (File.Exists(pdf)) File.Delete(pdf);
                    var profile = Path.Combine(Path.GetTempPath(), "TaskPad-pdf-profile");
                    var psi = new ProcessStartInfo(browser,
                        $"--headless=new --disable-gpu --no-first-run --user-data-dir=\"{profile}\" --no-pdf-header-footer --print-to-pdf-no-header --print-to-pdf=\"{pdf}\" \"{new Uri(html).AbsoluteUri}\"")
                    { UseShellExecute = false, CreateNoWindow = true };
                    using (var p = Process.Start(psi)) p.WaitForExit(45000);
                    try { File.Delete(html); } catch { }
                    return File.Exists(pdf);
                }
                catch { return false; }
            }).ContinueWith(t => owner.Dispatcher.Invoke(() =>
            {
                if (t.Result)
                    owner.Toast("Exported " + Path.GetFileName(pdf), "Open", () => Process.Start(new ProcessStartInfo(pdf) { UseShellExecute = true }), seconds: 6);
                else
                    owner.Toast("PDF export failed", "Open HTML", () => Process.Start(new ProcessStartInfo(html) { UseShellExecute = true }), seconds: 6);
            }));
        }

        static string FindBrowser()
        {
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var p in new[]
            {
                Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
            })
                if (File.Exists(p)) return p;
            return null;
        }

        // ---------------- HTML ----------------

        public static string ToHtml(Doc doc)
        {
            var notes = new List<string>();
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>").Append(Esc(doc.Name)).Append("</title><style>").Append(Css).Append("</style></head><body><main>");
            foreach (var line in doc.Document.Lines)
            {
                var text = doc.Document.GetText(line);
                var info = LineParser.Parse(text);
                string indent = info.Indent > 0 ? $" style='margin-left:{info.Indent * 0.55:0.##}em'" : "";
                string content = info.ContentStart < text.Length ? Inline(text.Substring(info.ContentStart), doc, notes) : "";

                if (text.Trim().Length == 0) { sb.Append("<div class='gap'></div>"); continue; }
                if (info.IsRule) { sb.Append(text.Trim()[0] == '=' ? "<hr class='accent'>" : "<hr>"); continue; }
                if (info.Heading > 0) { sb.Append($"<h{info.Heading}>{content}</h{info.Heading}>"); continue; }

                string cls = "line", lead = "";
                if (info.Check != Check.None)
                {
                    var st = info.Check == Check.Done ? "done" : info.Check == Check.Doing ? "doing" : info.Check == Check.Cancelled ? "cancel" : "open";
                    var mark = info.Check == Check.Done ? "✓" : info.Check == Check.Cancelled ? "✕" : info.Check == Check.Doing ? "◐" : "";
                    cls += " task " + st + (info.Indent > 0 ? " sub" : "");
                    lead = $"<span class='box'>{mark}</span>";
                }
                if (info.Tag != Tag.None)
                {
                    var t = info.Tag.ToString().ToLowerInvariant();
                    cls += " tag-" + t;
                    lead += info.Tag == Tag.Bang ? "<span class='badge'>!</span>"
                          : info.Tag == Tag.Question ? "<span class='badge'>?</span>"
                          : info.Tag == Tag.Todo ? "<span class='pill'>TODO</span>"
                          : $"<span class='glyph'>{Glyph(info.Tag)}</span>";
                }
                if (info.NumberStart >= 0) lead += $"<span class='num'>{Esc(text.Substring(info.NumberStart, info.NumberLen))}</span>";
                if (info.Section) cls += " section";
                sb.Append($"<div class='{cls}'{indent}>{lead}<span class='txt'>{content}</span></div>");
            }
            if (notes.Count > 0)
            {
                sb.Append("<section class='notes'><h4>Comments</h4><ol>");
                foreach (var n in notes) sb.Append("<li>").Append(n).Append("</li>");
                sb.Append("</ol></section>");
            }
            sb.Append("</main></body></html>");
            return sb.ToString();
        }

        static string Glyph(Tag t)
        {
            switch (t)
            {
                case Tag.Star: return "★";
                case Tag.Arrow: return "❯";
                case Tag.Back: return "❮";
                case Tag.Slash: return "✓";
                default: return "•";
            }
        }

        static readonly Regex InlineRx = new Regex(
            @"(?<img>!\[(?<alt>[^\]]*)\]\((?<src>[^)]+)\))|(?<cm>\{==(?<ct>.+?)==\}\{>>(?<cc>.*?)<<\}|\{>>(?<cc2>.*?)<<\})|(?<url>\b(?:https?://|www\.)[^\s<>""'`)\]]+)|(?<hex>(?<![\w&#])#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3,4})\b)|(?<code>`[^`]+`)|(?<m>(?<![\w@])@[\w.\-]+)|(?<tag>(?<![\w#&])#[A-Za-z][\w\-/]*)|(?<d>\b\d{4}-\d{2}-\d{2}(?:[ T]\d{1,2}:\d{2})?\b)",
            RegexOptions.Compiled);

        static string Inline(string s, Doc doc, List<string> notes)
        {
            var sb = new StringBuilder();
            int last = 0;
            foreach (Match m in InlineRx.Matches(s))
            {
                sb.Append(Esc(s.Substring(last, m.Index - last)));
                last = m.Index + m.Length;
                if (m.Groups["img"].Success)
                {
                    var path = Images.Resolve(doc, m.Groups["src"].Value);
                    if (path != null && File.Exists(path))
                    {
                        var mime = Path.GetExtension(path).ToLowerInvariant() == ".jpg" || Path.GetExtension(path).ToLowerInvariant() == ".jpeg" ? "image/jpeg" : "image/" + Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                        sb.Append($"<img src='data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}'>");
                    }
                    else sb.Append("<span class='missing'>[image missing]</span>");
                }
                else if (m.Groups["cm"].Success)
                {
                    var raw = m.Groups["cc"].Success ? m.Groups["cc"].Value : m.Groups["cc2"].Value;
                    var thread = CommentThread.Parse(raw);
                    var items = new StringBuilder();
                    foreach (var e in thread) items.Append(e.Author != null ? $"<b>{Esc(e.Author)}</b> " : "").Append(Esc(e.Text)).Append("<br>");
                    notes.Add(items.ToString());
                    if (m.Groups["ct"].Success) sb.Append("<mark>").Append(Esc(m.Groups["ct"].Value)).Append("</mark>");
                    sb.Append($"<sup class='ref'>{notes.Count}</sup>");
                }
                else if (m.Groups["url"].Success)
                {
                    var u = m.Value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + m.Value : m.Value;
                    sb.Append($"<a href='{Esc(u)}'>{Esc(m.Value)}</a>");
                }
                else if (m.Groups["hex"].Success) sb.Append($"<span class='sw' style='background:{m.Value}'></span><code>{Esc(m.Value)}</code>");
                else if (m.Groups["code"].Success) sb.Append("<code>").Append(Esc(m.Value.Trim('`'))).Append("</code>");
                else if (m.Groups["m"].Success) sb.Append("<span class='mention'>").Append(Esc(m.Value)).Append("</span>");
                else if (m.Groups["tag"].Success) sb.Append("<span class='hashtag'>").Append(Esc(m.Value)).Append("</span>");
                else sb.Append("<span class='date'>").Append(Esc(m.Value)).Append("</span>");
            }
            sb.Append(Esc(s.Substring(last)));
            return sb.ToString();
        }

        static string Esc(string s) => WebUtility.HtmlEncode(s);

        const string Css = @"
@page { margin: 18mm 16mm; }
body { font: 11pt/1.55 'Segoe UI', system-ui, sans-serif; color: #1f2329; }
main { max-width: 760px; margin: 0 auto; }
h1,h2,h3,h4,h5,h6 { margin: .9em 0 .35em; line-height: 1.25; color: #111; }
h1 { font-size: 2em; } h2 { font-size: 1.6em; } h3 { font-size: 1.3em; } h4 { font-size: 1.12em; } h5 { font-size: 1em; } h6 { font-size: .9em; color: #555; }
.gap { height: .6em; }
hr { border: 0; border-top: 1px solid #ddd; margin: .8em 0; } hr.accent { border-top: 2px solid #16a34a; }
.line { display: flex; gap: .5em; align-items: baseline; margin: .12em 0; break-inside: avoid; }
.section .txt { font-weight: 700; }
.box { flex: none; width: .95em; height: .95em; border: 1.5px solid #8a8a96; border-radius: 3px; display: inline-flex; align-items: center; justify-content: center; font-size: .8em; color: #fff; transform: translateY(.12em); }
.sub .box { width: .8em; height: .8em; }
.done .box { background: #16a34a; border-color: #16a34a; } .done .txt, .cancel .txt { color: #888; text-decoration: line-through; }
.doing .box { border-color: #d99a00; color: #d99a00; } .cancel .box { border-color: #e04444; color: #e04444; }
.badge { flex: none; width: 1.1em; height: 1.1em; border-radius: 50%; color: #fff; font-weight: 700; font-size: .8em; display: inline-flex; align-items: center; justify-content: center; }
.tag-bang .badge { background: #e5484d; } .tag-bang .txt { color: #c62f35; font-weight: 700; }
.tag-question .badge { background: #8b6cf0; } .tag-question .txt { color: #6d4fd6; font-style: italic; }
.pill { flex: none; background: #f5b70a; color: #1a1408; font-size: .65em; font-weight: 800; padding: .1em .6em; border-radius: 1em; }
.tag-todo .txt { color: #9a6b00; font-weight: 700; }
.glyph { flex: none; width: 1em; text-align: center; }
.tag-star .glyph, .tag-star .txt { color: #12936a; } .tag-arrow .glyph, .tag-arrow .txt { color: #8b3fd1; font-weight: 600; }
.tag-back .glyph, .tag-back .txt { color: #0b8aa3; } .tag-slash .glyph, .tag-slash .txt { color: #1f9d55; font-weight: 600; } .tag-dash .glyph { color: #888; }
.num { color: #16a34a; font-weight: 700; }
code { font-family: 'Cascadia Mono', Consolas, monospace; background: #f3f2f8; padding: 0 .3em; border-radius: 3px; font-size: .9em; color: #b45309; }
.mention { color: #b7791f; font-weight: 600; } .hashtag { color: #0e7490; } .date { color: #2563eb; }
a { color: #2563eb; }
.sw { display: inline-block; width: .85em; height: .85em; border-radius: 3px; border: 1px solid #0002; margin-right: .25em; vertical-align: -.1em; }
mark { background: #fff1b8; border-bottom: 1.5px dotted #f5b70a; }
sup.ref { color: #b7791f; font-weight: 700; margin-left: .1em; }
img { display: block; max-width: 100%; max-height: 520px; margin: .5em 0; border-radius: 6px; border: 1px solid #e3e3ea; }
.notes { margin-top: 2em; border-top: 1px solid #ddd; font-size: .9em; color: #444; } .notes li { margin: .3em 0; }
.missing { color: #c62f35; }
";
    }
}
