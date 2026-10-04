using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Image support: pasted/dropped images are saved next to the file in "images/" and referenced
    /// with markdown  ![](images/img-….png)  so the document stays plain text.
    public static class Images
    {
        public static readonly Regex Ref = new Regex(@"!\[([^\]\r\n]*)\]\(([^)\r\n]+)\)", RegexOptions.Compiled);
        static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico" };

        public static bool IsImageFile(string path) => Exts.Contains(Path.GetExtension(path).ToLowerInvariant());

        /// Folder where new images for this doc go.
        /// .task notes keep images inside the note; everything else uses one app folder
        /// (%LOCALAPPDATA%\TaskPad\images) instead of creating "images" folders next to notes.
        static string StoreDir(Doc doc)
        {
            if (doc.AssetDir != null) return Path.Combine(doc.AssetDir, "images");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskPad", "images");
        }

        /// Text to put in the document for an image file (relative when possible).
        static string RefFor(Doc doc, string file)
        {
            string p = file;
            if (doc.AssetDir != null || doc.Path != null)
            {
                var baseDir = (doc.AssetDir ?? Path.GetDirectoryName(doc.Path)).TrimEnd('\\') + "\\";
                if (file.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) p = file.Substring(baseDir.Length).Replace('\\', '/');
            }
            return $"![]({p})";
        }

        public static string Resolve(Doc doc, string reference)
        {
            reference = reference.Trim().Trim('<', '>');
            if (reference.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) reference = new Uri(reference).LocalPath;
            if (Path.IsPathRooted(reference)) return reference;
            if (doc.AssetDir != null)
            {
                var inside = Path.Combine(doc.AssetDir, reference.Replace('/', '\\'));
                if (File.Exists(inside) || doc.Path == null) return inside;
            }
            if (doc.Path == null) return null;
            try { return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc.Path), reference.Replace('/', '\\'))); }
            catch { return null; }
        }

        /// Where an image reference points, ignoring the .task cache (used when packing a note).
        public static string ResolveForAdopt(Doc doc, string reference)
        {
            reference = reference.Trim().Trim('<', '>');
            if (reference.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) reference = new Uri(reference).LocalPath;
            if (Path.IsPathRooted(reference)) return reference;
            if (doc.Path == null) return null;
            try { return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc.Path), reference.Replace('/', '\\'))); }
            catch { return null; }
        }

        /// Every image the note references, in order (for ◀ ▶ in the viewer).
        public static List<string> InNote(Doc doc)
        {
            var list = new List<string>();
            foreach (Match m in Ref.Matches(doc.Document.Text))
            {
                var p = Resolve(doc, m.Groups[2].Value);
                if (p != null && File.Exists(p) && !list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
            }
            return list;
        }

        static string UniquePath(string dir, string name, string ext)
        {
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, name + ext);
            for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, $"{name}-{i}{ext}");
            return p;
        }

        // ---------------- paste / drop ----------------

        /// Handles Ctrl+V when the clipboard holds an image or image files. Returns false to let text paste run.
        public static bool TryPaste(TextEditor ed, Doc doc)
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList().Cast<string>().Where(IsImageFile).ToList();
                    if (files.Count == 0) return false;
                    InsertFiles(ed, doc, files);
                    return true;
                }
                if (!Clipboard.ContainsImage() && !Clipboard.ContainsData("PNG")) return false;

                BitmapSource img = null;
                if (Clipboard.GetData("PNG") is MemoryStream png)
                    img = BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (img == null) img = Clipboard.GetImage();
                if (img == null) return false;

                var path = UniquePath(StoreDir(doc), "img-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), ".png");
                using (var fs = File.Create(path))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(img));
                    enc.Save(fs);
                }
                Insert(ed, RefFor(doc, path));
                PackNow(doc);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not paste image: " + ex.Message, "TaskPad", MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }
        }

        /// Copies dropped/pasted image files into the doc's images folder and references them.
        public static void InsertFiles(TextEditor ed, Doc doc, IEnumerable<string> files)
        {
            var refs = new List<string>();
            foreach (var f in files)
            {
                string target = f;
                var dir = StoreDir(doc);
                if (!string.Equals(Path.GetDirectoryName(f), dir, StringComparison.OrdinalIgnoreCase))
                {
                    var name = Regex.Replace(Path.GetFileNameWithoutExtension(f), @"[^\w\-.]+", "-");
                    target = UniquePath(dir, name, Path.GetExtension(f).ToLowerInvariant());
                    File.Copy(f, target);
                }
                refs.Add(RefFor(doc, target));
            }
            if (refs.Count == 0) return;
            var nl = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(ed.Document, ed.TextArea.Caret.Line);
            Insert(ed, string.Join(nl, refs));
            PackNow(doc);
        }

        /// .task notes: write the image into the file immediately (when auto save is on).
        static void PackNow(Doc doc)
        {
            if (doc.Path != null && TaskFile.Is(doc.Path) && Workspace.Settings.AutoSave) doc.Save(null, false);
        }

        static void Insert(TextEditor ed, string text)
        {
            var doc = ed.Document;
            int caret = ed.CaretOffset;
            var line = doc.GetLineByOffset(caret);
            bool needSpace = caret > line.Offset && !char.IsWhiteSpace(doc.GetCharAt(caret - 1));
            ed.TextArea.Selection.ReplaceSelectionWithText((needSpace ? " " : "") + text);
        }

        // ---------------- bitmap cache ----------------

        static readonly Dictionary<string, (DateTime Stamp, BitmapSource Bmp)> Thumbs = new Dictionary<string, (DateTime, BitmapSource)>(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource LoadThumb(string path, int decodeHeight)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var stamp = File.GetLastWriteTimeUtc(path);
                var key = path + "|" + decodeHeight;
                if (Thumbs.TryGetValue(key, out var c) && c.Stamp == stamp) return c.Bmp;
                var b = Load(path, decodeHeight);
                Thumbs[key] = (stamp, b);
                return b;
            }
            catch { return null; }
        }

        public static BitmapSource Load(string path, int decodeHeight = 0)
        {
            // read into memory so the file is never locked
            var bytes = File.ReadAllBytes(path);
            if (decodeHeight > 0)
            {
                var f = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                if (f.PixelHeight <= decodeHeight) decodeHeight = 0; // never upscale
            }
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = new MemoryStream(bytes);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeHeight > 0) bi.DecodePixelHeight = decodeHeight;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
    }

    /// Renders ![](path) references as a clickable thumbnail (or a chip when previews are off).
    public sealed class ImageGenerator : VisualLineElementGenerator
    {
        readonly Doc _doc;

        public ImageGenerator(Doc doc) { _doc = doc; }

        Match FindMatch(int startOffset, out int lineOffset)
        {
            var line = CurrentContext.Document.GetLineByOffset(startOffset);
            lineOffset = line.Offset;
            if (Code.IsCodeLine(CurrentContext.Document, line.LineNumber)) return null;
            var text = CurrentContext.Document.GetText(line);
            int rel = startOffset - line.Offset;
            if (text.IndexOf("![", rel, StringComparison.Ordinal) < 0) return null;
            var m = Images.Ref.Match(text, rel);
            return m.Success ? m : null;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var m = FindMatch(startOffset, out int lo);
            return m == null ? -1 : lo + m.Index;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            var m = FindMatch(offset, out int lo);
            if (m == null || lo + m.Index != offset) return null;
            var path = Images.Resolve(_doc, m.Groups[2].Value);
            var tv = CurrentContext.TextView;
            var anchor = CurrentContext.Document.CreateAnchor(offset);
            return new InlineObjectElement(m.Length, Build(path, m.Groups[1].Value, tv, anchor));
        }

        static readonly Regex AltWidth = new Regex(@"^(?<alt>.*?)\|(?<w>\d{2,4})$");

        /// Rewrites the reference's alt text (e.g. to store a display width as "alt|320").
        void UpdateRef(ICSharpCode.AvalonEdit.Document.TextAnchor anchor, Func<string, string> newAlt, bool remove = false)
        {
            if (anchor.IsDeleted) return;
            var doc = CurrentDoc;
            var line = doc.GetLineByOffset(anchor.Offset);
            var m = Images.Ref.Match(doc.GetText(line), anchor.Offset - line.Offset);
            if (!m.Success || line.Offset + m.Index != anchor.Offset) return;
            if (remove)
            {
                int start = line.Offset + m.Index, len = m.Length;
                if (start + len < line.EndOffset && doc.GetCharAt(start + len) == ' ') len++;
                doc.Remove(start, len);
                return;
            }
            var g = m.Groups[1];
            doc.Replace(line.Offset + g.Index, g.Length, newAlt(g.Value));
        }

        ICSharpCode.AvalonEdit.Document.TextDocument CurrentDoc => _doc.Document;

        FrameworkElement Build(string path, string alt, TextView tv, ICSharpCode.AvalonEdit.Document.TextAnchor anchor)
        {
            double previewH = Workspace.Settings.ImagePreviewHeight;
            double lineH = tv.DefaultLineHeight;
            var aw = AltWidth.Match(alt);
            double customW = aw.Success ? double.Parse(aw.Groups["w"].Value) : 0;
            string baseAlt = aw.Success ? aw.Groups["alt"].Value : alt;
            string name = path != null ? Path.GetFileName(path) : alt;
            var bmp = path != null && (previewH > 0 || customW > 0) && File.Exists(path) ? Images.LoadThumb(path, (int)Math.Max(previewH * 2, customW > 0 ? 900 : 0)) : null;
            bool missing = path == null || !File.Exists(path);

            FrameworkElement el;
            if (bmp != null)
            {
                double ratio = bmp.PixelHeight / Math.Max(1.0, bmp.PixelWidth);
                double h = Math.Min(previewH, bmp.PixelHeight);
                double w = h / ratio;
                double maxW = Math.Max(120, Math.Min(480, tv.ActualWidth - 60));
                if (w > maxW) { h *= maxW / w; w = maxW; }
                if (customW > 0) { w = customW; h = w * ratio; }
                var img = new Image { Source = bmp, Width = w, Height = h, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var caption = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x0E, 0x0E, 0x11)),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(0, 5, 0, 5),
                    Child = new TextBlock
                    {
                        Text = "🔍 " + name,
                        Foreground = Theme.Fg,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 11,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = w - 12,
                    },
                    Opacity = 0,
                };
                // hover tools: remove (top-right) and resize grip (bottom-right)
                var remove = new Border
                {
                    Width = 22, Height = 22, Margin = new Thickness(0, 5, 5, 0),
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x0E, 0x0E, 0x11)),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Cursor = Cursors.Hand, Opacity = 0, ToolTip = "Remove from note (file is kept)",
                    Child = new TextBlock { Text = "✕", Foreground = Theme.Fg, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                remove.MouseLeftButtonDown += (s, e) => e.Handled = true;
                remove.MouseLeftButtonUp += (s, e) => { e.Handled = true; UpdateRef(anchor, null, remove: true); };
                var grip = new System.Windows.Controls.Primitives.Thumb
                {
                    Width = 16, Height = 16, Cursor = Cursors.SizeNWSE, Opacity = 0,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    ToolTip = "Drag to resize · double-click to reset",
                    Template = GripTemplate(),
                };
                double startW = w;
                grip.DragStarted += (s, e) => startW = img.Width;
                grip.DragDelta += (s, e) =>
                {
                    double nw = Math.Max(48, Math.Min(1600, img.Width + e.HorizontalChange));
                    img.Width = nw;
                    img.Height = nw * ratio;
                };
                grip.DragCompleted += (s, e) =>
                {
                    int nw = (int)Math.Round(img.Width);
                    if (Math.Abs(nw - startW) >= 2) UpdateRef(anchor, a => baseAlt + "|" + nw);
                };
                grip.MouseDoubleClick += (s, e) => { e.Handled = true; UpdateRef(anchor, a => baseAlt); };

                var grid = new Grid();
                grid.Children.Add(img);
                grid.Children.Add(caption);
                grid.Children.Add(remove);
                grid.Children.Add(grip);
                var frame = new Border
                {
                    Child = grid,
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = Theme.ChromeBorder,
                    BorderThickness = new Thickness(1),
                    Background = Theme.Chrome,
                    Margin = new Thickness(0, 4, 0, 4),
                    ClipToBounds = true,
                };
                frame.MouseEnter += (s, e) => { caption.Opacity = remove.Opacity = grip.Opacity = 1; frame.BorderBrush = Theme.Accent; };
                frame.MouseLeave += (s, e) => { caption.Opacity = remove.Opacity = grip.Opacity = 0; frame.BorderBrush = Theme.ChromeBorder; };
                el = frame;
            }
            else
            {
                // compact chip: [image icon] name — click opens the viewer, hover shows a preview
                string label = baseAlt.Length > 0 ? baseAlt : name;
                double fs = Math.Max(10, lineH * 0.56);
                var icon = new Border
                {
                    Width = fs + 4, Height = fs + 2, CornerRadius = new CornerRadius(3),
                    Background = missing ? Theme.BoxCancel : Theme.Accent,
                    Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = missing
                        ? (UIElement)new TextBlock { Text = "!", Foreground = Brushes.White, FontSize = fs * 0.7, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                        : new System.Windows.Shapes.Path   // tiny "picture" glyph: mountains + sun
                        {
                            Data = Geometry.Parse("M0,10 L3.6,5 L6.2,8 L8.2,6 L12,10 Z M9,3 A1.5,1.5 0 1 1 9,2.99 Z"),
                            Fill = Brushes.White, Stretch = Stretch.Uniform, Margin = new Thickness(2.5, 3, 2.5, 3),
                        },
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(icon);
                sp.Children.Add(new TextBlock
                {
                    Text = missing ? "missing: " + label : label,
                    Foreground = missing ? Theme.Bang : Theme.Fg,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = fs,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = 260,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                var chip = new Border
                {
                    Background = missing ? new SolidColorBrush(Color.FromArgb(0x30, 0xEF, 0x44, 0x44)) : Theme.Hover,
                    BorderBrush = missing ? Theme.BoxCancel : Theme.ChromeBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(3, 0, 8, 0),
                    Height = Math.Round(lineH - 2),
                    Child = sp,
                };
                var normal = chip.BorderBrush;
                chip.MouseEnter += (s, e) => { if (!missing) chip.BorderBrush = Theme.Accent; };
                chip.MouseLeave += (s, e) => chip.BorderBrush = normal;
                TextBlock.SetBaselineOffset(chip, tv.DefaultBaseline - 1);
                el = chip;
                if (!missing)
                {
                    // hover preview
                    var thumb = Images.LoadThumb(path, 360);
                    var tip = new StackPanel();
                    if (thumb != null)
                        tip.Children.Add(new Image { Source = thumb, MaxWidth = 320, MaxHeight = 220, Stretch = Stretch.Uniform, Margin = new Thickness(0, 2, 0, 6) });
                    tip.Children.Add(new TextBlock { Text = "Click to open  ·  " + name, Foreground = Theme.FgDim, FontSize = 11 });
                    chip.ToolTip = tip;
                    ToolTipService.SetInitialShowDelay(chip, 250);
                }
            }

            if (!missing)
            {
                el.Cursor = Cursors.Hand;
                if (el.ToolTip == null)
                {
                    el.ToolTip = "Click to inspect   ·   " + path;
                    ToolTipService.SetInitialShowDelay(el, 700);
                }
                el.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    ImageViewer.Show(path, Images.InNote(_doc));
                };
            }
            return el;
        }
    
        static ControlTemplate GripTemplate()
        {
            var t = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb));
            var f = new FrameworkElementFactory(typeof(Border));
            f.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xD0, 0x7C, 0x6C, 0xF6)));
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(6, 0, 5, 0));
            t.VisualTree = f;
            return t;
        }
    }
}
