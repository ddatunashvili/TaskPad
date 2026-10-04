using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TaskPad
{
    /// Recently closed files (newest first), kept in recent.txt next to the settings.
    public static class Recent
    {
        const int Max = 20;
        static List<string> _items;

        static string FilePath => Path.Combine(Workspace.Settings.Dir ?? Workspace.ExeDir, "recent.txt");

        static List<string> Items
        {
            get
            {
                if (_items == null)
                {
                    try { _items = File.Exists(FilePath) ? File.ReadAllLines(FilePath).Where(l => l.Length > 0).ToList() : new List<string>(); }
                    catch { _items = new List<string>(); }
                }
                return _items;
            }
        }

        public static List<string> List() => Closed.ToList();

        public static void Add(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Items.Insert(0, path);
            if (Items.Count > Max) Items.RemoveRange(Max, Items.Count - Max);
            Persist();
        }

        static void Remove(string path)
        {
            Items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Persist();
        }

        static void Persist()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllLines(FilePath, Items); } catch { }
        }

        /// Paths that are closed right now (open ones are skipped).
        static IEnumerable<string> Closed => Items.Where(p => Workspace.FindByPath(p) == null);

        /// Ctrl+Shift+T
        public static void ReopenLast(TaskWindow w)
        {
            var p = Closed.FirstOrDefault(File.Exists);
            if (p == null) { w.Toast("No recently closed files"); return; }
            Reopen(w, p);
        }

        static void Reopen(TaskWindow w, string path)
        {
            if (!File.Exists(path)) { Remove(path); w.Toast("File no longer exists: " + Path.GetFileName(path)); return; }
            Remove(path);
            w.OpenFile(path);
        }

        public static void ShowMenu(TaskWindow w, FrameworkElement anchor)
        {
            var m = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            var list = Closed.ToList();
            if (list.Count == 0) m.Items.Add(new MenuItem { Header = "No recently closed files", IsEnabled = false });
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                bool exists = File.Exists(p);
                var mi = new MenuItem
                {
                    Header = Path.GetFileName(p) + (exists ? "" : "  (missing)"),
                    InputGestureText = i == 0 ? "Ctrl+Shift+T" : ShortDir(p),
                    ToolTip = p,
                    IsEnabled = exists,
                };
                mi.Click += (s, e) => Reopen(w, p);
                m.Items.Add(mi);
            }
            if (list.Count > 0)
            {
                m.Items.Add(new Separator());
                var all = new MenuItem { Header = "Reopen all" };
                all.Click += (s, e) => { foreach (var p in list.Where(File.Exists)) Reopen(w, p); };
                m.Items.Add(all);
                var clear = new MenuItem { Header = "Clear list" };
                clear.Click += (s, e) => { Items.Clear(); Persist(); };
                m.Items.Add(clear);
            }
            m.IsOpen = true;
        }

        static string ShortDir(string p)
        {
            var d = Path.GetDirectoryName(p) ?? "";
            return d.Length > 38 ? "…" + d.Substring(d.Length - 37) : d;
        }
    }
}
