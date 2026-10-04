using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TaskPad
{
    /// Process-wide state shared by every TaskPad window.
    public static class Workspace
    {
        public static Settings Settings;
        public static readonly List<TaskWindow> Windows = new List<TaskWindow>();
        public static readonly List<Doc> Docs = new List<Doc>();
        public static TaskWindow LastActive;
        public static int UntitledCounter;

        public static IEnumerable<TabView> AllViews => Docs.SelectMany(d => d.Views).ToList();

        public static Doc FindByPath(string path) =>
            Docs.FirstOrDefault(d => d.Path != null && string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));

        public static void Zoom(int dir)
        {
            double size = dir == 0 ? 16 : Math.Max(8, Math.Min(48, Settings.FontSize + dir));
            Settings.FontSize = size;
            foreach (var v in AllViews) v.Editor.FontSize = size;
            foreach (var w in Windows) w.UpdateStatus();
        }

        public static void ToggleWrap()
        {
            Settings.WordWrap = !Settings.WordWrap;
            foreach (var v in AllViews) v.Editor.WordWrap = Settings.WordWrap;
        }

        public static void SetCommentPanel(bool on)
        {
            Settings.CommentPanel = on;
            Settings.Save();
            foreach (var w in Windows) foreach (var g in w.Groups) g.UpdateToggles();
            foreach (var v in AllViews)
            {
                v.Margin.ApplyMode();
                v.Editor.TextArea.TextView.Redraw();
            }
        }

        public static void ReloadChangedFiles()
        {
            foreach (var d in Docs.ToList()) d.ReloadIfChanged();
            foreach (var w in Windows) w.UpdateStatus();
        }

        public static string ExeDir => AppDomain.CurrentDomain.BaseDirectory;
    }
}
