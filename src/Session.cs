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
                    if (!w.IsVisible || groups.All(g => g.Tabs.Count == 0)) continue;
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
        /// A window is only created when at least one of its tabs can actually be restored; anything that
        /// goes wrong is logged and never leaves an invisible window behind.
        public static bool Restore()
        {
            if (!Enabled || !File.Exists(File_)) return false;
            string[] lines;
            try { lines = File.ReadAllLines(File_); } catch { return false; }
            if (lines.Length == 0 || lines[0] != Version) return false;

            _restoring = true;
            var docs = new Dictionary<int, Doc>();
            TaskWindow focus = null;
            try
            {
                var rows = lines.Skip(1).Select(l => l.Split('\t').Select(Uri.UnescapeDataString).ToArray()).ToList();
                foreach (var p in rows.Where(r => r[0] == "doc"))
                {
                    var doc = LoadDoc(p);
                    if (doc != null) docs[int.Parse(p[1])] = doc;
                }

                // window blocks: a "win" row followed by its "grp" rows
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i][0] != "win") continue;
                    var w = rows[i];
                    var grps = new List<string[]>();
                    for (int j = i + 1; j < rows.Count && rows[j][0] == "grp"; j++) grps.Add(rows[j]);
                    var groupTabs = grps.Select(g => TabsOf(g, docs)).ToList();
                    if (groupTabs.All(t => t.Count == 0)) continue;   // nothing to show: don't create a window at all

                    var win = RestoreWindow(w, grps, groupTabs);
                    if (win != null && w.Length > 8 && w[8] == "1") focus = win;
                }
            }
            catch (Exception ex) { Log(ex); }
            finally { _restoring = false; }

            // never keep windows that didn't make it on screen
            foreach (var w in Workspace.Windows.Where(w => !w.IsVisible).ToList()) Workspace.Windows.Remove(w);
            foreach (var d in docs.Values.Where(d => d.Views.Count == 0).ToList()) d.Dispose();
            if (Workspace.Windows.Count == 0) return false;
            Workspace.LastActive = focus != null && focus.IsVisible ? focus : Workspace.Windows.Last();
            return true;
        }

        static List<(Doc Doc, int Caret, double Scroll)> TabsOf(string[] grp, Dictionary<int, Doc> docs)
        {
            var list = new List<(Doc, int, double)>();
            foreach (var entry in (grp.Length > 2 ? grp[2] : "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = entry.Split(',');
                if (int.TryParse(f[0], out int id) && docs.TryGetValue(id, out var doc))
                    list.Add((doc, (int)D(f.Length > 1 ? f[1] : "0", 0), f.Length > 2 ? D(f[2], 0) : 0));
            }
            return list;
        }

        static TaskWindow RestoreWindow(string[] w, List<string[]> grps, List<List<(Doc Doc, int Caret, double Scroll)>> groupTabs)
        {
            var st = Workspace.Settings;
            double left = D(w[1], double.NaN), top = D(w[2], double.NaN), width = D(w[3], 0), height = D(w[4], 0);
            if (width < 360 || height < 240) { width = st.Width; height = st.Height; }
            var win = new TaskWindow(false) { Width = width, Height = height };
            if (double.IsNaN(left) || double.IsNaN(top) || !OnScreen(left, top)) win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            else { win.Left = left; win.Top = top; }
            try
            {
                // only keep the layout if every group has tabs; otherwise fall back to one group
                bool allGroups = groupTabs.All(t => t.Count > 0);
                var groups = allGroups ? win.ApplyLayout(w[6]) : win.Groups.ToList();
                if (!allGroups) groupTabs = new List<List<(Doc, int, double)>> { groupTabs.SelectMany(t => t).ToList() };
                for (int g = 0; g < groups.Count && g < groupTabs.Count; g++)
                {
                    int active = allGroups ? (int)D(grps[g][1], 0) : 0;
                    foreach (var (doc, caret, scroll) in groupTabs[g])
                    {
                        var tab = new TabView(doc);
                        groups[g].Insert(tab, groups[g].Tabs.Count, activate: false);
                        tab.Editor.CaretOffset = Math.Min(caret, doc.Document.TextLength);
                        var ed = tab.Editor;
                        ed.Loaded += (s, e) => ed.ScrollToVerticalOffset(scroll);
                    }
                    if (groups[g].Tabs.Count > 0) groups[g].Activate(groups[g].Tabs[Math.Min(active, groups[g].Tabs.Count - 1)], focus: false);
                }
                var live = win.Groups.ToList();
                win.SetActiveGroup(live[Math.Max(0, Math.Min((int)D(w[7], 0), live.Count - 1))]);
                if (w[5] == "1") win.WindowState = WindowState.Maximized;
                win.Show();
                return win;
            }
            catch (Exception ex)
            {
                Log(ex);
                foreach (var t in win.Groups.SelectMany(g => g.Tabs).ToList()) t.Detach();
                Workspace.Windows.Remove(win);
                return null;
            }
        }

        static bool OnScreen(double left, double top) =>
            left >= SystemParameters.VirtualScreenLeft - 50 && top >= SystemParameters.VirtualScreenTop - 50 &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

        static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "TaskPad-error.log"), $"{DateTime.Now} session restore{Environment.NewLine}{ex}{Environment.NewLine}"); } catch { }
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
