using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace TaskPad
{
    /// "Hot exit": remembers every window, split layout, tab and cursor, and keeps unsaved / untitled text
    /// in session\backup-*.txt, so closing TaskPad never loses work and reopening brings it all back.
    public static class Session
    {
        const string Version = "taskpad-session 1";
        public static bool Enabled;     // false for --standalone and restoreSession=false
        public static bool Quitting;    // set while the app shuts everything down (update restart)
        static DispatcherTimer _timer;
        static bool _restoring;
        public static bool Restoring => _restoring;

        static string Dir => Path.Combine(Workspace.Settings.Dir ?? Workspace.ExeDir, "session");
        static string File_ => Path.Combine(Dir, "session.txt");

        /// Debounced save after edits / tab changes, so even a crash or power cut keeps the latest text.
        public static void MarkDirty()
        {
            if (!Enabled || _restoring || Quitting) return;
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _timer.Tick += (s, e) => { _timer.Stop(); if (Workspace.Windows.Count > 0) Save(); };
            }
            _timer.Stop();
            _timer.Start();
        }

        // ---------------- save ----------------

        public static void Save()
        {
            if (!Enabled) return;
            try
            {
                Directory.CreateDirectory(Dir);
                var ids = new Dictionary<Doc, int>();
                var sb = new StringBuilder(Version + "\n");
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var doc in Workspace.Docs.Where(d => d.Views.Count > 0))
                {
                    int id = ids.Count;
                    ids[doc] = id;
                    string backup = "";
                    if (doc.Path == null ? doc.Document.TextLength > 0 : doc.Dirty)
                    {
                        backup = $"backup-{id}.txt";
                        File.WriteAllText(Path.Combine(Dir, backup), doc.Document.Text, new UTF8Encoding(false));
                        keep.Add(backup);
                    }
                    sb.Append(Line("doc", id, doc.Path, doc.UntitledName, doc.Dirty ? 1 : 0, backup, doc.Encoding.WebName));
                }

                foreach (var w in Workspace.Windows)
                {
                    var groups = w.Groups.ToList();
                    if (groups.All(g => g.Tabs.Count == 0)) continue;
                    var rb = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight) : w.RestoreBounds;
                    sb.Append(Line("win", F(rb.Left), F(rb.Top), F(rb.Width), F(rb.Height), w.WindowState == WindowState.Maximized ? 1 : 0,
                        w.LayoutSpec(), groups.IndexOf(w.ActiveGroup), w == Workspace.LastActive ? 1 : 0));
                    foreach (var g in groups)
                    {
                        var tabs = string.Join(";", g.Tabs.Where(t => ids.ContainsKey(t.Doc))
                            .Select(t => ids[t.Doc] + "," + t.Editor.CaretOffset + "," + F(t.Editor.VerticalOffset)));
                        sb.Append(Line("grp", Math.Max(0, g.Tabs.IndexOf(g.Active)), tabs));
                    }
                }

                var tmp = File_ + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(File_)) File.Replace(tmp, File_, null); else File.Move(tmp, File_);
                foreach (var f in Directory.GetFiles(Dir, "backup-*.txt"))
                    if (!keep.Contains(Path.GetFileName(f))) try { File.Delete(f); } catch { }
            }
            catch { }
        }

        static string F(double d) => double.IsNaN(d) ? "" : d.ToString("0.##", CultureInfo.InvariantCulture);
        static string Line(params object[] parts) =>
            string.Join("\t", parts.Select(p => Uri.EscapeDataString(Convert.ToString(p, CultureInfo.InvariantCulture) ?? ""))) + "\n";

        // ---------------- restore ----------------

        /// Recreates the saved windows. Returns false if there was nothing to restore.
        public static bool Restore()
        {
            if (!Enabled || !File.Exists(File_)) return false;
            string[] lines;
            try { lines = File.ReadAllLines(File_); } catch { return false; }
            if (lines.Length == 0 || lines[0] != Version) return false;

            _restoring = true;
            try
            {
                var docs = new Dictionary<int, Doc>();
                TaskWindow win = null, focus = null;
                List<EditorGroup> groups = null;
                int groupIndex = 0, activeGroup = 0;

                foreach (var raw in lines.Skip(1))
                {
                    var p = raw.Split('\t').Select(Uri.UnescapeDataString).ToArray();
                    switch (p[0])
                    {
                        case "doc":
                        {
                            var doc = LoadDoc(p);
                            if (doc != null) docs[int.Parse(p[1])] = doc;
                            break;
                        }
                        case "win":
                        {
                            FinishWindow(win, groups, activeGroup);
                            win = new TaskWindow(false)
                            {
                                Left = D(p[1], 100), Top = D(p[2], 100),
                                Width = D(p[3], Workspace.Settings.Width), Height = D(p[4], Workspace.Settings.Height),
                            };
                            if (p[5] == "1") win.WindowState = WindowState.Maximized;
                            groups = win.ApplyLayout(p[6]);
                            activeGroup = (int)D(p[7], 0);
                            groupIndex = 0;
                            if (p.Length > 8 && p[8] == "1") focus = win;
                            break;
                        }
                        case "grp":
                        {
                            if (win == null || groupIndex >= groups.Count) break;
                            var g = groups[groupIndex++];
                            int active = (int)D(p[1], 0);
                            var restored = new List<(TabView Tab, int Caret, double Scroll)>();
                            foreach (var entry in (p.Length > 2 ? p[2] : "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                var f = entry.Split(',');
                                if (!docs.TryGetValue(int.Parse(f[0]), out var doc)) continue;
                                var tab = new TabView(doc);
                                g.Insert(tab, g.Tabs.Count, activate: false);
                                restored.Add((tab, (int)D(f[1], 0), f.Length > 2 ? D(f[2], 0) : 0));
                            }
                            if (g.Tabs.Count > 0) g.Activate(g.Tabs[Math.Min(active, g.Tabs.Count - 1)], focus: false);
                            foreach (var (tab, caret, scroll) in restored)
                            {
                                tab.Editor.CaretOffset = Math.Min(caret, tab.Doc.Document.TextLength);
                                var ed = tab.Editor;
                                ed.Loaded += (s, e) => ed.ScrollToVerticalOffset(scroll);
                            }
                            break;
                        }
                    }
                }
                FinishWindow(win, groups, activeGroup);

                // drop docs nobody shows (e.g. missing windows)
                foreach (var d in docs.Values.Where(d => d.Views.Count == 0).ToList()) d.Dispose();
                if (Workspace.Windows.Count == 0) return false;
                Workspace.LastActive = focus ?? Workspace.Windows.Last();
                return true;
            }
            catch
            {
                return Workspace.Windows.Count > 0;
            }
            finally { _restoring = false; }
        }

        static void FinishWindow(TaskWindow w, List<EditorGroup> groups, int active)
        {
            if (w == null) return;
            // remove groups that ended up empty (file deleted since last time)
            foreach (var g in groups.Where(g => g.Tabs.Count == 0).ToList())
                if (w.Groups.Count() > 1) w.RemoveGroup(g);
            if (w.TabCount == 0) { w.Close(); return; }
            var live = w.Groups.ToList();
            w.SetActiveGroup(live[Math.Max(0, Math.Min(active, live.Count - 1))]);
            w.Show();
        }

        static Doc LoadDoc(string[] p)
        {
            string path = p[2].Length > 0 ? p[2] : null, untitled = p[3], backup = p[5];
            bool dirty = p[4] == "1";
            string backupPath = backup.Length > 0 ? Path.Combine(Dir, backup) : null;
            try
            {
                Doc doc;
                if (backupPath != null && File.Exists(backupPath))
                {
                    var text = File.ReadAllText(backupPath, Encoding.UTF8);
                    doc = new Doc(text, path);
                    if (path == null)
                    {
                        doc.UntitledName = untitled;
                        var m = Regex.Match(untitled ?? "", @"^Untitled-(\d+)$");
                        if (m.Success) Workspace.UntitledCounter = Math.Max(Workspace.UntitledCounter, int.Parse(m.Groups[1].Value));
                    }
                    else if (File.Exists(path)) doc.LastWriteUtc = File.GetLastWriteTimeUtc(path);
                    try { doc.Encoding = Encoding.GetEncoding(p[6]); if (doc.Encoding is UTF8Encoding) doc.Encoding = new UTF8Encoding(false); } catch { }
                    doc.Dirty = dirty || path == null;
                    doc.Raise();
                    return doc;
                }
                if (path == null || !File.Exists(path)) return null;
                return Workspace.FindByPath(path) ?? Doc.Open(path);
            }
            catch { return null; }
        }

        static double D(string s, double def) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
    }
}
