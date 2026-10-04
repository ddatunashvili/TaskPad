using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;

namespace TaskPad
{
    /// One open file (or untitled buffer). Several tabs can show the same Doc (split view).
    public sealed class Doc
    {
        public readonly TextDocument Document = new TextDocument();
        public readonly List<TabView> Views = new List<TabView>();
        public string Path, UntitledName;
        public string AssetDir;          // folder holding this note's images (.task notes)
        public bool Dirty;
        public Encoding Encoding = new UTF8Encoding(false);
        public DateTime LastWriteUtc;
        public event Action Changed;

        readonly DispatcherTimer _autoSave;

        public string Name => Path != null ? System.IO.Path.GetFileName(Path) : UntitledName;

        public Doc(string text, string path)
        {
            Document.Text = text ?? "";
            Document.UndoStack.ClearAll();
            Path = path;
            Document.FileName = path;
            if (path == null) UntitledName = "Untitled-" + (++Workspace.UntitledCounter);

            _autoSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(200, Workspace.Settings.AutoSaveDelayMs)) };
            _autoSave.Tick += (s, e) =>
            {
                _autoSave.Stop();
                if (Dirty && Path != null && Workspace.Settings.AutoSave) Save(null, false);
            };
            Document.TextChanged += (s, e) =>
            {
                if (!Dirty) { Dirty = true; Raise(); }
                Session.MarkDirty();
                _autoSave.Stop();
                _autoSave.Start();
            };
            Workspace.Docs.Add(this);
        }

        public void Raise() => Changed?.Invoke();

        public static Doc Open(string path)
        {
            if (TaskFile.Is(path))
            {
                var t = TaskFile.Load(path, out var assets);
                return new Doc(t, path) { AssetDir = assets, LastWriteUtc = File.GetLastWriteTimeUtc(path) };
            }
            var text = ReadText(path, out var enc);
            return new Doc(text, path) { Encoding = enc, LastWriteUtc = File.GetLastWriteTimeUtc(path) };
        }

        /// Called when the last tab showing this doc closes.
        public void Dispose()
        {
            _autoSave.Stop();
            Workspace.Docs.Remove(this);
        }

        public bool Save(Window owner, bool saveAs)
        {
            if (Path == null || saveAs)
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|TaskPad note with images (*.task)|*.task|Markdown (*.md)|*.md|Todo (*.todo)|*.todo|All files (*.*)|*.*",
                    FilterIndex = Path == null || TaskFile.Is(Path) ? 2 : 1,   // new notes default to .task
                    FileName = Path != null ? System.IO.Path.GetFileName(Path) : "tasks" + TaskFile.Ext,
                    InitialDirectory = Path != null ? System.IO.Path.GetDirectoryName(Path) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                };
                if (dlg.ShowDialog(owner) != true) return false;
                Path = dlg.FileName;
                Document.FileName = Path;
                if (TaskFile.Is(Path)) AssetDir = TaskFile.CacheDir(Path);
            }
            try
            {
                if (TaskFile.Is(Path))
                {
                    TaskFile.Save(this);
                    LastWriteUtc = File.GetLastWriteTimeUtc(Path);
                    Dirty = false;
                    Raise();
                    return true;
                }
                var bytes = Encoding.GetBytes(Document.Text);
                var pre = Encoding.GetPreamble();
                using (var fs = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    fs.Write(pre, 0, pre.Length);
                    fs.Write(bytes, 0, bytes.Length);
                }
                LastWriteUtc = File.GetLastWriteTimeUtc(Path);
                Dirty = false;
                Raise();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot save {Path}\n\n{ex.Message}", "TaskPad", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        /// Saves file-backed docs silently; asks only for untitled docs with text.
        public bool ConfirmClose(Window owner)
        {
            if (!Dirty) return true;
            if (Path != null) return Save(owner, false);
            if (Document.TextLength == 0) return true;
            var r = MessageBox.Show(owner, $"Save changes to {Name}?", "TaskPad", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            return r == MessageBoxResult.No || Save(owner, true);
        }

        public void ReloadIfChanged()
        {
            if (Path == null || Dirty || !File.Exists(Path)) return;
            var w = File.GetLastWriteTimeUtc(Path);
            if (w == LastWriteUtc) return;
            try
            {
                Encoding enc = Encoding;
                var text = TaskFile.Is(Path) ? TaskFile.Load(Path, out AssetDir) : ReadText(Path, out enc);
                var carets = new List<int>();
                foreach (var v in Views) carets.Add(v.Editor.CaretOffset);
                Document.Text = text;
                for (int i = 0; i < Views.Count; i++) Views[i].Editor.CaretOffset = Math.Min(carets[i], text.Length);
                Encoding = enc;
                LastWriteUtc = w;
                Dirty = false;
                _autoSave.Stop();
                Raise();
            }
            catch { }
        }

        /// True for files that are clearly not text (NUL bytes in the first 8 KB, no UTF-16 BOM).
        public static bool LooksBinary(string path)
        {
            try
            {
                var buf = new byte[8192];
                int n;
                using (var fs = File.OpenRead(path)) n = fs.Read(buf, 0, buf.Length);
                if (n >= 2 && ((buf[0] == 0xFF && buf[1] == 0xFE) || (buf[0] == 0xFE && buf[1] == 0xFF))) return false;
                for (int i = 0; i < n; i++) if (buf[i] == 0) return true;
                return false;
            }
            catch { return false; }
        }

        public static string ReadText(string path, out Encoding enc)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { enc = new UTF8Encoding(true); return enc.GetString(bytes, 3, bytes.Length - 3); }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { enc = Encoding.Unicode; return enc.GetString(bytes, 2, bytes.Length - 2); }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { enc = Encoding.BigEndianUnicode; return enc.GetString(bytes, 2, bytes.Length - 2); }
            try
            {
                var s = new UTF8Encoding(false, true).GetString(bytes);
                enc = new UTF8Encoding(false);
                return s;
            }
            catch (DecoderFallbackException)
            {
                enc = Encoding.Default;
                return enc.GetString(bytes);
            }
        }
    }
}
