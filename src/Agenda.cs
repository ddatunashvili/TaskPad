using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskPad
{
    /// Every task with a deadline, from open notes, the open folder(s) and the Inbox.
    public static class Agenda
    {
        public sealed class Entry
        {
            public string Path, Name, Text;
            public int Line;              // 1-based
            public DateTime When;
            public bool HasTime, Done, IsTask;
        }

        static readonly string[] Exts = { ".txt", ".task", ".md", ".todo" };
        static readonly Dictionary<string, (DateTime Stamp, string Text)> FileCache = new Dictionary<string, (DateTime, string)>(StringComparer.OrdinalIgnoreCase);

        public static string InboxPath
        {
            get
            {
                var p = Workspace.Settings.InboxPath;
                if (string.IsNullOrWhiteSpace(p))
                    p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TaskPad", "Inbox" + TaskFile.Ext);
                return p;
            }
        }

        /// Folders to look in: every window's explorer folder plus the remembered one.
        static IEnumerable<string> Folders()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in Workspace.Windows) if (w.Explorer?.Root != null) set.Add(w.Explorer.Root);
            if (!string.IsNullOrEmpty(Workspace.Settings.ExplorerFolder) && Directory.Exists(Workspace.Settings.ExplorerFolder)) set.Add(Workspace.Settings.ExplorerFolder);
            return set;
        }

        /// Note text for a file on disk (cached by write time; .task notes read from inside the archive).
        static string ReadNote(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > 4_000_000) return null;
                if (FileCache.TryGetValue(path, out var c) && c.Stamp == fi.LastWriteTimeUtc) return c.Text;
                string text;
                if (TaskFile.Is(path))
                {
                    if (fi.Length == 0) text = "";
                    else using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
                    {
                        var e = zip.GetEntry("note.txt");
                        if (e == null) return null;
                        using (var r = new StreamReader(e.Open(), Encoding.UTF8)) text = r.ReadToEnd();
                    }
                }
                else text = Doc.ReadText(path, out _);
                FileCache[path] = (fi.LastWriteTimeUtc, text);
                return text;
            }
            catch { return null; }
        }

        static IEnumerable<string> FolderFiles(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            int count = 0;
            while (stack.Count > 0 && count < 3000)
            {
                var dir = stack.Pop();
                string[] files = new string[0], dirs = new string[0];
                try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); } catch { }
                foreach (var f in files)
                    if (Exts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())) { count++; yield return f; }
                foreach (var d in dirs)
                {
                    var n = System.IO.Path.GetFileName(d);
                    if (n.StartsWith(".") || n == "node_modules" || n == "bin" || n == "obj") continue;
                    stack.Push(d);
                }
            }
        }

        public static List<Entry> Collect()
        {
            var sources = new Dictionary<string, (string Name, string Text)>(StringComparer.OrdinalIgnoreCase);
            // open notes first: they may have unsaved edits
            foreach (var d in Workspace.Docs)
            {
                if (Code.IsCodeFile(d.Path)) continue;
                sources[d.Path ?? ("untitled:" + d.Name)] = (d.Name, d.Document.Text);
            }
            foreach (var root in Folders())
                foreach (var f in FolderFiles(root))
                    if (!sources.ContainsKey(f) && ReadNote(f) is string t) sources[f] = (System.IO.Path.GetFileName(f), t);
            if (File.Exists(InboxPath) && !sources.ContainsKey(InboxPath) && ReadNote(InboxPath) is string inbox)
                sources[InboxPath] = (System.IO.Path.GetFileName(InboxPath), inbox);

            var list = new List<Entry>();
            foreach (var kv in sources)
            {
                var lines = kv.Value.Text.Replace("\r\n", "\n").Split('\n');
                bool fence = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) { fence = !fence; continue; }
                    if (fence || line.IndexOf("due", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var m = Due.Token.Match(line);
                    if (!m.Success || !Due.TryParse(m.Groups["v"].Value, out var when, out bool hasTime)) continue;
                    var info = LineParser.Parse(line);
                    var label = Due.Token.Replace(line.Substring(Math.Min(info.ContentStart, line.Length)), "");
                    label = Comments.Markup.Replace(label, x => x.Groups["t"].Success ? x.Groups["t"].Value : "").Trim();
                    list.Add(new Entry
                    {
                        Path = kv.Key.StartsWith("untitled:") ? null : kv.Key, Name = kv.Value.Name, Text = label.Length > 0 ? label : "(no text)",
                        Line = i + 1, When = when, HasTime = hasTime, Done = info.IsDone, IsTask = info.Check != Check.None,
                    });
                }
            }
            return list.OrderBy(e => e.When).ToList();
        }

        /// Opens the note and puts the cursor on the task.
        public static void Reveal(TaskWindow w, Entry e)
        {
            if (w == null) return;
            if (e.Path != null) w.OpenFile(e.Path);
            var doc = e.Path != null ? Workspace.FindByPath(e.Path) : Workspace.Docs.FirstOrDefault(d => d.Name == e.Name);
            var tab = doc?.Views.FirstOrDefault(v => v.Group?.Owner == w) ?? doc?.Views.FirstOrDefault();
            if (tab == null) return;
            tab.Group.Activate(tab);
            if (e.Line <= tab.Doc.Document.LineCount)
            {
                var line = tab.Doc.Document.GetLineByNumber(e.Line);
                tab.Editor.CaretOffset = line.EndOffset;
                tab.Editor.ScrollToLine(e.Line);
            }
            w.Activate();
        }

        /// Ticks / unticks the task in its note (opening it so autosave writes the change).
        public static void Toggle(TaskWindow w, Entry e)
        {
            Doc doc = e.Path != null ? Workspace.FindByPath(e.Path) : Workspace.Docs.FirstOrDefault(d => d.Name == e.Name);
            if (doc == null && e.Path != null) { w.OpenFile(e.Path); doc = Workspace.FindByPath(e.Path); }
            if (doc == null || e.Line > doc.Document.LineCount) return;
            var line = doc.Document.GetLineByNumber(e.Line);
            var info = LineParser.Parse(doc.Document.GetText(line));
            if (info.Check == Check.None) Markers.SetLine(doc.Document, e.Line, "[x] ");
            else SmartEditing.SetCheck(doc.Document, line.Offset + info.CheckStart, info.Check == Check.Done ? Check.Open : Check.Done);
        }
    }

    /// The "Agenda" tab of the sidebar.
    public sealed class AgendaPanel : Border
    {
        readonly TaskWindow _w;
        readonly StackPanel _list = new StackPanel { Margin = new Thickness(6, 4, 6, 12) };
        readonly DispatcherTimer _timer, _debounce;
        bool _showDone;

        public AgendaPanel(TaskWindow w)
        {
            _w = w;
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _timer.Tick += (s, e) => Refresh();
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Refresh(); };
            IsVisibleChanged += (s, e) => { if (IsVisible) { Refresh(); _timer.Start(); } else _timer.Stop(); };
        }

        /// Called when any note changes.
        public void Poke()
        {
            if (!IsVisible) return;
            _debounce.Stop();
            _debounce.Start();
        }

        public void Refresh()
        {
            if (!IsVisible) return;
            _list.Children.Clear();
            var items = Agenda.Collect().Where(e => _showDone || !e.Done).ToList();
            var now = DateTime.Now;
            var today = DateTime.Today;
            var weekEnd = today.AddDays(7);
            var groups = new (string Title, Func<Agenda.Entry, bool> Test, Brush Color)[]
            {
                ("Overdue", e => !e.Done && e.When < now, Theme.Bang),
                ("Today", e => (e.Done || e.When >= now) && e.When.Date == today, Theme.Todo),
                ("Tomorrow", e => e.When.Date == today.AddDays(1), Theme.Accent),
                ("This week", e => e.When.Date > today.AddDays(1) && e.When.Date < weekEnd, Theme.Fg),
                ("Later", e => e.When.Date >= weekEnd, Theme.FgDim),
                ("Done", e => e.Done && e.When < now && e.When.Date != today, Theme.FgDim),
            };
            var shown = new HashSet<Agenda.Entry>();
            foreach (var g in groups)
            {
                var rows = items.Where(e => !shown.Contains(e) && g.Test(e)).ToList();
                if (rows.Count == 0) continue;
                _list.Children.Add(new TextBlock
                {
                    Text = $"{g.Title.ToUpperInvariant()}  {rows.Count}", Foreground = g.Color, FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 12, 0, 4),
                });
                foreach (var e in rows) { shown.Add(e); _list.Children.Add(Row(e)); }
            }
            if (_list.Children.Count == 0)
                _list.Children.Add(new TextBlock
                {
                    Text = "No deadlines yet.\n\nAdd due: to any task — e.g.\n[ ] send report due:+2h\nTasks from the open folder, open notes and your Inbox appear here.",
                    Foreground = Theme.FgDim, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 14, 10, 0),
                });
            var footer = new TextBlock
            {
                Text = _showDone ? "Hide completed" : "Show completed", Foreground = Theme.FgDim, FontFamily = new FontFamily("Segoe UI"), FontSize = 11.5,
                Margin = new Thickness(10, 16, 0, 0), Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline,
            };
            footer.MouseLeftButtonUp += (s, e) => { _showDone = !_showDone; Refresh(); };
            _list.Children.Add(footer);
        }

        UIElement Row(Agenda.Entry e)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var box = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(3.5), BorderThickness = new Thickness(1.4),
                BorderBrush = e.Done ? Theme.BoxDone : Theme.BoxOpen, Background = e.Done ? Theme.BoxDone : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 3, 0, 0), Cursor = Cursors.Hand,
                Child = e.Done ? new TextBlock { Text = "✓", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) } : null,
                ToolTip = e.Done ? "Mark as not done" : "Mark as done",
            };
            box.MouseLeftButtonDown += (s, a) => a.Handled = true;
            box.MouseLeftButtonUp += (s, a) => { a.Handled = true; Agenda.Toggle(_w, e); Poke(); };
            g.Children.Add(box);

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = e.Text, Foreground = e.Done ? Theme.Done : Theme.Fg, FontFamily = new FontFamily("Segoe UI"), FontSize = 13,
                TextWrapping = TextWrapping.Wrap, TextDecorations = e.Done ? TextDecorations.Strikethrough : null,
            });
            var meta = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            meta.Inlines.Add(new System.Windows.Documents.Run(e.Done ? "done" : Due.Countdown(e.When, DateTime.Now)) { Foreground = Due.StateBrush(e.When, e.Done), FontWeight = FontWeights.SemiBold });
            meta.Inlines.Add(new System.Windows.Documents.Run("  ·  " + Due.Pretty(e.When, e.HasTime) + "  ·  " + e.Name) { Foreground = Theme.FgDim });
            text.Children.Add(meta);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var row = new Border { Child = g, Padding = new Thickness(2, 5, 6, 5), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = e.Path ?? e.Name };
            row.MouseEnter += (s, a) => row.Background = Theme.Hover;
            row.MouseLeave += (s, a) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (s, a) => Agenda.Reveal(_w, e);
            return row;
        }
    }
}
