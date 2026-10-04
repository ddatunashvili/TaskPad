using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TaskPad
{
    /// Tiny key=value settings. Stored next to the exe (portable) or in %APPDATA%\TaskPad if that folder is read-only.
    public class Settings
    {
        public string FontFamily = "Comic Mono, Cascadia Mono, Consolas";
        public double FontSize = 16;
        public bool WordWrap = true;
        public int AutoSaveDelayMs = 1000;
        public int IndentSize = 2;
        public double LeftMargin = 28;
        public double ImagePreviewHeight = 0;   // 0 = compact chip, >0 = inline thumbnail height
        public double CommentWidth = 360, CommentHeight = 200;
        public double Left = double.NaN, Top = double.NaN, Width = 980, Height = 680;
        public bool Maximized;

        string _path;

        public static Settings Load()
        {
            var s = new Settings();
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var local = Path.Combine(exeDir, "TaskPad.ini");
            s._path = File.Exists(local) || CanWrite(exeDir)
                ? local
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskPad", "TaskPad.ini");
            if (!File.Exists(s._path)) return s;
            try
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(s._path))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && !line.StartsWith(";")) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                string v;
                if (map.TryGetValue("font", out v) && v.Length > 0) s.FontFamily = v;
                if (map.TryGetValue("fontSize", out v)) s.FontSize = D(v, s.FontSize);
                if (map.TryGetValue("wordWrap", out v)) s.WordWrap = v == "true";
                if (map.TryGetValue("autoSaveDelayMs", out v)) s.AutoSaveDelayMs = (int)D(v, s.AutoSaveDelayMs);
                if (map.TryGetValue("imagePreviewHeight", out v)) s.ImagePreviewHeight = Math.Max(0, D(v, s.ImagePreviewHeight));
                if (map.TryGetValue("commentWidth", out v)) s.CommentWidth = D(v, s.CommentWidth);
                if (map.TryGetValue("commentHeight", out v)) s.CommentHeight = D(v, s.CommentHeight);
                if (map.TryGetValue("leftMargin", out v)) s.LeftMargin = Math.Max(0, D(v, s.LeftMargin));
                if (map.TryGetValue("indentSize", out v)) s.IndentSize = Math.Max(1, (int)D(v, s.IndentSize));
                if (map.TryGetValue("left", out v)) s.Left = D(v, double.NaN);
                if (map.TryGetValue("top", out v)) s.Top = D(v, double.NaN);
                if (map.TryGetValue("width", out v)) s.Width = D(v, s.Width);
                if (map.TryGetValue("height", out v)) s.Height = D(v, s.Height);
                if (map.TryGetValue("maximized", out v)) s.Maximized = v == "true";
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var ci = CultureInfo.InvariantCulture;
                File.WriteAllLines(_path, new[]
                {
                    "; TaskPad settings",
                    "font=" + FontFamily,
                    "fontSize=" + FontSize.ToString(ci),
                    "wordWrap=" + (WordWrap ? "true" : "false"),
                    "autoSaveDelayMs=" + AutoSaveDelayMs,
                    "indentSize=" + IndentSize,
                    "leftMargin=" + LeftMargin.ToString(ci),
                    "imagePreviewHeight=" + ImagePreviewHeight.ToString(ci),
                    "commentWidth=" + CommentWidth.ToString(ci),
                    "commentHeight=" + CommentHeight.ToString(ci),
                    "left=" + Left.ToString(ci),
                    "top=" + Top.ToString(ci),
                    "width=" + Width.ToString(ci),
                    "height=" + Height.ToString(ci),
                    "maximized=" + (Maximized ? "true" : "false"),
                });
            }
            catch { }
        }

        static double D(string v, double def) =>
            double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;

        static bool CanWrite(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".taskpad-probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }
    }
}
