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
                if (Dirty && Path != null) Save(null, false);
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
                    Filter = "Text files (*.txt)|*.txt|Markdown (*.md)|*.md|Todo (*.todo)|*.todo|All files (*.*)|*.*",
                    FileName = Path != null ? System.IO.Path.GetFileName(Path) : "tasks.txt",
                    InitialDirectory = Path != null ? System.IO.Path.GetDirectoryName(Path) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                };
                if (dlg.ShowDialog(owner) != true) return false;
                Path = dlg.FileName;
                Document.FileName = Path;
            }
            try
            {
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
                var text = ReadText(Path, out var enc);
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
