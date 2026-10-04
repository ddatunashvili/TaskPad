using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskPad
{
    /// ".task" = one file holding a note and its images. It is a ZIP archive:
    ///   note.txt      the note (plain TaskPad text, images referenced as ![](images/name.png))
    ///   images/...    the pictures
    ///   taskpad       format marker ("1")
    /// While open, the archive is unpacked to %LOCALAPPDATA%\TaskPad\task-cache\<id>\; saving packs it again
    /// with only the images the note still references.
    public static class TaskFile
    {
        public const string Ext = ".task";
        const string NoteEntry = "note.txt";

        public static bool Is(string path) => path != null && path.EndsWith(Ext, StringComparison.OrdinalIgnoreCase);

        public static string CacheDir(string path)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()));
                var id = BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskPad", "task-cache", id);
            }
        }

        /// Reads the note text and unpacks the images into the cache folder.
        public static string Load(string path, out string assetDir)
        {
            assetDir = CacheDir(path);
            Directory.CreateDirectory(assetDir);
            if (new FileInfo(path).Length == 0) return "";            // brand-new empty file
            string text = "";
            using (var zip = ZipFile.OpenRead(path))
            {
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    if (e.FullName == NoteEntry)
                    {
                        using (var r = new StreamReader(e.Open(), new UTF8Encoding(false), true)) text = r.ReadToEnd();
                        continue;
                    }
                    if (!e.FullName.StartsWith("images/", StringComparison.Ordinal)) continue;
                    var target = Path.GetFullPath(Path.Combine(assetDir, e.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(assetDir, StringComparison.OrdinalIgnoreCase)) continue; // no path tricks
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                }
            }
            return text;
        }

        /// "Export as .task": writes a self-contained copy (note + every image it references);
        /// the open note itself is not changed.
        public static void ExportCopy(TaskWindow w, Doc doc)
        {
            if (doc == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "TaskPad note with images (*.task)|*.task",
                FileName = Path.GetFileNameWithoutExtension(doc.Name) + Ext,
                InitialDirectory = doc.Path != null ? Path.GetDirectoryName(doc.Path) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            };
            if (dlg.ShowDialog(w) != true) return;
            var target = dlg.FileName;
            if (doc.Path != null && string.Equals(Path.GetFullPath(target), Path.GetFullPath(doc.Path), StringComparison.OrdinalIgnoreCase))
            { w.Toast("That's the note itself — just save it"); return; }
            try
            {
                var text = doc.Document.Text;
                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // entry name -> source file
                var sb = new StringBuilder();
                int last = 0;
                foreach (Match m in Images.Ref.Matches(text))
                {
                    var src = Images.Resolve(doc, m.Groups[2].Value);
                    if (src == null || !File.Exists(src)) continue;
                    var name = "images/" + Path.GetFileName(src);
                    for (int i = 2; files.TryGetValue(name, out var existing) && !string.Equals(existing, src, StringComparison.OrdinalIgnoreCase); i++)
                        name = "images/" + Path.GetFileNameWithoutExtension(src) + "-" + i + Path.GetExtension(src);
                    files[name] = src;
                    var g = m.Groups[2];
                    sb.Append(text, last, g.Index - last).Append(name);
                    last = g.Index + g.Length;
                }
                sb.Append(text, last, text.Length - last);

                var tmp = target + ".saving";
                using (var fs = new FileStream(tmp, FileMode.Create))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    using (var sw = new StreamWriter(zip.CreateEntry("taskpad", CompressionLevel.NoCompression).Open())) sw.Write("1");
                    using (var sw = new StreamWriter(zip.CreateEntry(NoteEntry).Open(), new UTF8Encoding(false))) sw.Write(sb.ToString());
                    foreach (var f in files) zip.CreateEntryFromFile(f.Value, f.Key, CompressionLevel.NoCompression);
                }
                if (File.Exists(target)) File.Replace(tmp, target, null); else File.Move(tmp, target);
                w.Toast($"Exported {Path.GetFileName(target)} ({files.Count} image{(files.Count == 1 ? "" : "s")})", "Open", () => w.OpenFile(target), seconds: 6);
            }
            catch (Exception ex) { w.Toast("Export failed: " + ex.Message); }
        }

        /// Writes a new .task file containing just `text`.
        public static void CreateNew(string path, string text)
        {
            using (var fs = new FileStream(path, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(zip.CreateEntry("taskpad", CompressionLevel.NoCompression).Open())) w.Write("1");
                using (var w = new StreamWriter(zip.CreateEntry(NoteEntry).Open(), new UTF8Encoding(false))) w.Write(text);
            }
        }

        /// Unpacks only the images (session restore of an unsaved .task note).
        public static void EnsureAssets(Doc doc)
        {
            if (!Is(doc.Path)) return;
            doc.AssetDir = CacheDir(doc.Path);
            if (Directory.Exists(Path.Combine(doc.AssetDir, "images")) || !File.Exists(doc.Path)) return;
            try { Load(doc.Path, out _); } catch { }
        }

        /// Copies images referenced from elsewhere (absolute paths, old images\ folders) into the note's
        /// own images and rewrites the references, so the .task file is self-contained.
        public static void Adopt(Doc doc)
        {
            var dir = Path.Combine(doc.AssetDir, "images");
            var text = doc.Document.Text;
            var changes = new List<(int Index, int Length, string Text)>();
            foreach (Match m in Images.Ref.Matches(text))
            {
                var src = m.Groups[2].Value;
                if (src.StartsWith("images/", StringComparison.Ordinal) && File.Exists(Path.Combine(doc.AssetDir, src.Replace('/', '\\')))) continue;
                var file = Images.ResolveForAdopt(doc, src);
                if (file == null || !File.Exists(file)) continue;
                Directory.CreateDirectory(dir);
                var name = Path.GetFileName(file);
                var target = Path.Combine(dir, name);
                for (int i = 2; File.Exists(target) && !SameFile(file, target); i++)
                    target = Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + "-" + i + Path.GetExtension(name));
                if (!File.Exists(target)) File.Copy(file, target);
                var g = m.Groups[2];
                changes.Add((g.Index, g.Length, "images/" + Path.GetFileName(target)));
            }
            if (changes.Count == 0) return;
            using (doc.Document.RunUpdate())
                foreach (var c in changes.OrderByDescending(c => c.Index))
                    doc.Document.Replace(c.Index, c.Length, c.Text);
        }

        static bool SameFile(string a, string b)
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            return fa.Length == fb.Length && File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b));
        }

        /// Packs note + referenced images into the .task file (written to a temp file, then swapped in).
        public static void Save(Doc doc)
        {
            if (doc.AssetDir == null) doc.AssetDir = CacheDir(doc.Path);
            Adopt(doc);
            var text = doc.Document.Text;
            var tmp = doc.Path + ".saving";
            using (var fs = new FileStream(tmp, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var mark = zip.CreateEntry("taskpad", CompressionLevel.NoCompression);
                using (var w = new StreamWriter(mark.Open())) w.Write("1");
                var note = zip.CreateEntry(NoteEntry, CompressionLevel.Optimal);
                using (var w = new StreamWriter(note.Open(), new UTF8Encoding(false))) w.Write(text);
                var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in Images.Ref.Matches(text))
                {
                    var rel = m.Groups[2].Value;
                    if (!rel.StartsWith("images/", StringComparison.Ordinal) || !added.Add(rel)) continue;
                    var file = Path.Combine(doc.AssetDir, rel.Replace('/', '\\'));
                    if (File.Exists(file)) zip.CreateEntryFromFile(file, rel, CompressionLevel.NoCompression); // images are already compressed
                }
            }
            if (File.Exists(doc.Path)) File.Replace(tmp, doc.Path, null);
            else File.Move(tmp, doc.Path);
        }
    }
}
