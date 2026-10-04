using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace TaskPad
{
    /// Image inspector: wheel to zoom at the cursor, drag to pan, double-click to toggle fit / 100%,
    /// pin to keep it on top as a reference, pixel position + colour readout.
    public sealed class ImageViewer : Window
    {
        static readonly Dictionary<string, ImageViewer> OpenViewers = new Dictionary<string, ImageViewer>(StringComparer.OrdinalIgnoreCase);

        string _path;
        BitmapSource _bmp, _original;        // _original = as loaded from disk (for undo / "unsaved" state)
        List<string> _list;                  // images to step through with ◀ ▶
        int _index;
        bool _edited;
        readonly Image _img = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        readonly Canvas _host = new Canvas { ClipToBounds = true };
        readonly ScaleTransform _scale = new ScaleTransform();
        readonly TranslateTransform _move = new TranslateTransform();
        readonly TextBlock _zoomText = Label(""), _pixelText = Label(""), _info = Label(""), _counter = Label("");
        readonly Border _pinButton, _prevButton, _nextButton, _cropButton, _saveButton;
        readonly StackPanel _normalBar = new StackPanel { Orientation = Orientation.Horizontal };
        readonly StackPanel _cropBar = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        // crop overlay
        readonly System.Windows.Shapes.Path _mask = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly System.Windows.Shapes.Rectangle _selRect = new System.Windows.Shapes.Rectangle { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly TextBlock _selText = new TextBlock { Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), Padding = new Thickness(5, 1, 5, 2), FontFamily = new FontFamily("Segoe UI"), FontSize = 11, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        bool _cropMode;
        Rect? _sel;                          // selection in image pixels
        Point? _selFrom;
        byte[] _pixels;
        int _stride;
        double _zoom = 1;
        bool _fitted = true;
        Point? _dragFrom;
        Point _dragOrigin;

        /// Opens (or focuses) a viewer. `siblings` = images to step through (note order or folder order).
        public static void Show(string path, IList<string> siblings = null)
        {
            if (OpenViewers.TryGetValue(path, out var v))
            {
                if (siblings != null && siblings.Count > 1) v.SetList(siblings, path);
                if (v.WindowState == WindowState.Minimized) v.WindowState = WindowState.Normal;
                v.Activate();
                return;
            }
            try
            {
                v = new ImageViewer(path, siblings);
                OpenViewers[path] = v;
                v.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot open image: " + ex.Message, "TaskPad", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public static void CloseAll()
        {
            foreach (var v in OpenViewers.Values.ToList()) v.Close();
        }

        ImageViewer(string path, IList<string> siblings)
        {
            Background = Theme.Chrome;
            UseLayoutRounding = true;
            MinWidth = 420;
            MinHeight = 260;
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/TaskPad;component/assets/taskpad.ico")); } catch { }

            _img.RenderTransform = new TransformGroup { Children = { _scale, _move } };
            _host.Children.Add(_img);
            _host.Children.Add(_mask);
            _host.Children.Add(_selRect);
            _host.Children.Add(_selText);
            _host.Background = Checkerboard();

            _prevButton = ToolButton("◀", "Previous image (←)", () => Step(-1));
            _nextButton = ToolButton("▶", "Next image (→)", () => Step(+1));
            _pinButton = ToolButton("📌", "Pin on top — keep as reference (P)", TogglePin);
            _cropButton = ToolButton("✂", "Crop (C)", () => SetCropMode(true));
            _saveButton = ToolButton("💾", "Save (Ctrl+S)  ·  Save as copy (Ctrl+Shift+S)", () => Save(false));
            _normalBar.Children.Add(_prevButton);
            _normalBar.Children.Add(new Border { MinWidth = 44, Child = _counter });
            _normalBar.Children.Add(_nextButton);
            _normalBar.Children.Add(Sep());
            _normalBar.Children.Add(ToolButton("−", "Zoom out (−)", () => ZoomBy(1 / 1.25, Center())));
            _normalBar.Children.Add(new Border { Width = 52, Child = _zoomText });
            _normalBar.Children.Add(ToolButton("+", "Zoom in (+)", () => ZoomBy(1.25, Center())));
            _normalBar.Children.Add(ToolButton("Fit", "Fit to window (0)", Fit));
            _normalBar.Children.Add(ToolButton("1:1", "Actual size (1)", () => SetZoom(1, Center())));
            _normalBar.Children.Add(Sep());
            _normalBar.Children.Add(_cropButton);
            _normalBar.Children.Add(_saveButton);
            _normalBar.Children.Add(ToolButton("⤓", "Save as copy… (Ctrl+Shift+S)", () => Save(true)));
            _normalBar.Children.Add(Sep());
            _normalBar.Children.Add(_pinButton);
            _normalBar.Children.Add(ToolButton("⧉", "Copy image (Ctrl+C)", () => Clipboard.SetImage(_bmp)));
            _normalBar.Children.Add(ToolButton("↗", "Open in default app", () => Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true })));
            _normalBar.Children.Add(ToolButton("📁", "Show in Explorer", () => Process.Start("explorer.exe", $"/select,\"{_path}\"")));

            _cropBar.Children.Add(new Border { Child = Label("Drag to select the area to keep"), Margin = new Thickness(0, 0, 10, 0) });
            _cropBar.Children.Add(ToolButton("Apply crop", "Enter", ApplyCrop));
            _cropBar.Children.Add(ToolButton("Cancel", "Esc", () => SetCropMode(false)));

            _info.Margin = new Thickness(12, 0, 0, 0);
            _info.HorizontalAlignment = HorizontalAlignment.Left;
            _pixelText.Margin = new Thickness(0, 0, 12, 0);
            _pixelText.HorizontalAlignment = HorizontalAlignment.Right;

            var bottom = new Grid { Height = 40, Background = Theme.Chrome };
            bottom.Children.Add(_info);
            var center = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            center.Children.Add(_normalBar);
            center.Children.Add(_cropBar);
            bottom.Children.Add(center);
            bottom.Children.Add(_pixelText);
            var bottomBorder = new Border { Child = bottom, BorderBrush = Theme.ChromeBorder, BorderThickness = new Thickness(0, 1, 0, 0) };

            var root = new DockPanel();
            DockPanel.SetDock(bottomBorder, Dock.Bottom);
            root.Children.Add(bottomBorder);
            root.Children.Add(_host);
            Content = root;

            _list = siblings != null && siblings.Count > 0 ? siblings.ToList() : new List<string> { path };
            Load(path);

            // size to the image, within 80% of the work area
            var wa = SystemParameters.WorkArea;
            Width = Math.Max(MinWidth, Math.Min(_bmp.PixelWidth + 40, wa.Width * 0.8));
            Height = Math.Max(MinHeight, Math.Min(_bmp.PixelHeight + 110, wa.Height * 0.8));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _host.SizeChanged += (s, e) => { if (_fitted) Fit(); else UpdateOverlay(); };
            _host.MouseWheel += (s, e) => ZoomBy(e.Delta > 0 ? 1.2 : 1 / 1.2, e.GetPosition(_host));
            _host.MouseLeftButtonDown += (s, e) =>
            {
                var p = e.GetPosition(_host);
                if (_cropMode)
                {
                    _selFrom = ToImage(p, clamp: true);
                    _sel = new Rect(_selFrom.Value, _selFrom.Value);
                    _host.CaptureMouse();
                    UpdateOverlay();
                    return;
                }
                if (e.ClickCount == 2)
                {
                    if (_fitted || Math.Abs(_zoom - FitZoom()) < 0.001) SetZoom(1, p); else Fit();
                    return;
                }
                _dragFrom = p;
                _dragOrigin = new Point(_move.X, _move.Y);
                _host.CaptureMouse();
                _host.Cursor = Cursors.SizeAll;
            };
            _host.MouseMove += (s, e) =>
            {
                var p = e.GetPosition(_host);
                if (_cropMode && _selFrom is Point a && e.LeftButton == MouseButtonState.Pressed)
                {
                    _sel = new Rect(a, ToImage(p, clamp: true));
                    UpdateOverlay();
                }
                else if (_dragFrom is Point from && e.LeftButton == MouseButtonState.Pressed)
                {
                    _fitted = false;
                    _move.X = _dragOrigin.X + p.X - from.X;
                    _move.Y = _dragOrigin.Y + p.Y - from.Y;
                    UpdateOverlay();
                }
                UpdatePixel(p);
            };
            _host.MouseLeftButtonUp += (s, e) =>
            {
                _dragFrom = null;
                _selFrom = null;
                _host.ReleaseMouseCapture();
                _host.Cursor = _cropMode ? Cursors.Cross : Cursors.Arrow;
            };
            _host.MouseLeave += (s, e) => _pixelText.Text = "";

            PreviewKeyDown += (s, e) =>
            {
                var k = e.Key;
                var mods = Keyboard.Modifiers;
                if (_cropMode && k == Key.Escape) SetCropMode(false);
                else if (_cropMode && k == Key.Enter) ApplyCrop();
                else if (k == Key.Escape) Close();
                else if (k == Key.Left || k == Key.PageUp) Step(-1);
                else if (k == Key.Right || k == Key.PageDown || k == Key.Space) Step(+1);
                else if (k == Key.Home) GoTo(0);
                else if (k == Key.End) GoTo(_list.Count - 1);
                else if (k == Key.OemPlus || k == Key.Add) ZoomBy(1.25, Center());
                else if (k == Key.OemMinus || k == Key.Subtract) ZoomBy(1 / 1.25, Center());
                else if (k == Key.D0 || k == Key.NumPad0 || k == Key.F) Fit();
                else if (k == Key.D1 || k == Key.NumPad1) SetZoom(1, Center());
                else if (k == Key.P) TogglePin();
                else if (k == Key.C && mods == ModifierKeys.None) SetCropMode(true);
                else if (k == Key.C && mods == ModifierKeys.Control) Clipboard.SetImage(_bmp);
                else if (k == Key.S && mods == ModifierKeys.Control) Save(false);
                else if (k == Key.S && mods == (ModifierKeys.Control | ModifierKeys.Shift)) Save(true);
                else if (k == Key.Z && mods == ModifierKeys.Control) Revert();
                else return;
                e.Handled = true;
            };
            SourceInitialized += (s, e) => TaskWindow.ApplyDarkTitleBar(this);
            Closing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
            Closed += (s, e) => OpenViewers.Remove(_path);
        }

        // ---------------- navigation ----------------

        void SetList(IList<string> list, string current)
        {
            _list = list.ToList();
            _index = Math.Max(0, _list.FindIndex(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase)));
            UpdateNav();
        }

        void Step(int dir)
        {
            if (_list.Count < 2) return;
            GoTo((_index + dir + _list.Count) % _list.Count);
        }

        void GoTo(int i)
        {
            if (i < 0 || i >= _list.Count || i == _index && _bmp != null) return;
            if (!ConfirmDiscard()) return;
            var path = _list[i];
            if (!File.Exists(path)) { _list.RemoveAt(i); UpdateNav(); return; }
            try { Load(path); Fit(); }
            catch (Exception ex) { MessageBox.Show(this, "Cannot open image: " + ex.Message, "TaskPad"); }
        }

        void Load(string path)
        {
            OpenViewers.Remove(_path ?? "");
            _path = path;
            if (!OpenViewers.ContainsKey(path)) OpenViewers[path] = this;
            _original = _bmp = Images.Load(path);
            _edited = false;
            _pixels = null;
            _index = Math.Max(0, _list.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
            if (_index < 0 || _list.Count == 0) { _list.Insert(0, path); _index = 0; }
            ShowBitmap();
            UpdateNav();
        }

        void ShowBitmap()
        {
            _img.Source = _bmp;
            _img.Width = _bmp.PixelWidth;
            _img.Height = _bmp.PixelHeight;
            _pixels = null;
            long bytes = File.Exists(_path) ? new FileInfo(_path).Length : 0;
            _info.Text = $"{_bmp.PixelWidth} × {_bmp.PixelHeight}   ·   {bytes / 1024.0:0.#} KB" + (_edited ? "   ·   edited (Ctrl+S to save)" : "");
            Title = (_edited ? "● " : "") + Path.GetFileName(_path) + " — TaskPad";
        }

        void UpdateNav()
        {
            bool many = _list.Count > 1;
            _prevButton.Visibility = _nextButton.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
            _counter.Text = many ? $"{_index + 1} / {_list.Count}" : "";
            ((FrameworkElement)_counter.Parent).Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------- crop ----------------

        void SetCropMode(bool on)
        {
            _cropMode = on;
            _sel = null;
            _normalBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            _cropBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            _host.Cursor = on ? Cursors.Cross : Cursors.Arrow;
            UpdateOverlay();
        }

        Point ToImage(Point screen, bool clamp)
        {
            double x = (screen.X - _move.X) / _zoom, y = (screen.Y - _move.Y) / _zoom;
            if (clamp) { x = Math.Max(0, Math.Min(_bmp.PixelWidth, x)); y = Math.Max(0, Math.Min(_bmp.PixelHeight, y)); }
            return new Point(Math.Round(x), Math.Round(y));
        }

        /// Draws the dimmed area outside the selection and the dashed selection frame.
        void UpdateOverlay()
        {
            bool show = _cropMode && _sel is Rect r && r.Width >= 1 && r.Height >= 1;
            _mask.Visibility = _selRect.Visibility = _selText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            var s = _sel.Value;
            var screen = new Rect(s.X * _zoom + _move.X, s.Y * _zoom + _move.Y, s.Width * _zoom, s.Height * _zoom);
            var image = new Rect(_move.X, _move.Y, _bmp.PixelWidth * _zoom, _bmp.PixelHeight * _zoom);
            _mask.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(image), new RectangleGeometry(screen));
            Canvas.SetLeft(_selRect, screen.X); Canvas.SetTop(_selRect, screen.Y);
            _selRect.Width = screen.Width; _selRect.Height = screen.Height;
            _selText.Text = $"{(int)s.Width} × {(int)s.Height}";
            Canvas.SetLeft(_selText, screen.X); Canvas.SetTop(_selText, Math.Max(0, screen.Y - 20));
        }

        void ApplyCrop()
        {
            if (!(_sel is Rect s) || s.Width < 2 || s.Height < 2) { SetCropMode(false); return; }
            var rect = new Int32Rect((int)s.X, (int)s.Y, (int)Math.Min(s.Width, _bmp.PixelWidth - s.X), (int)Math.Min(s.Height, _bmp.PixelHeight - s.Y));
            var cropped = new CroppedBitmap(_bmp, rect);
            cropped.Freeze();
            _bmp = cropped;
            _edited = true;
            SetCropMode(false);
            ShowBitmap();
            Fit();
        }

        void Revert()
        {
            if (!_edited) return;
            _bmp = _original;
            _edited = false;
            ShowBitmap();
            Fit();
        }

        // ---------------- save ----------------

        bool ConfirmDiscard()
        {
            if (!_edited) return true;
            var r = MessageBox.Show(this, $"Save changes to {Path.GetFileName(_path)}?", "TaskPad", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Save(false);
            _edited = false;
            return true;
        }

        bool Save(bool asCopy)
        {
            var target = _path;
            if (asCopy)
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp",
                    FileName = Path.GetFileNameWithoutExtension(_path) + "-edited.png",
                    InitialDirectory = Path.GetDirectoryName(_path).Contains(Path.Combine(Path.GetTempPath(), "TaskPad"))
                        ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : Path.GetDirectoryName(_path),
                };
                if (dlg.ShowDialog(this) != true) return false;
                target = dlg.FileName;
            }
            else if (!_edited) return true;
            try
            {
                BitmapEncoder enc;
                switch (Path.GetExtension(target).ToLowerInvariant())
                {
                    case ".jpg": case ".jpeg": enc = new JpegBitmapEncoder { QualityLevel = 92 }; break;
                    case ".bmp": enc = new BmpBitmapEncoder(); break;
                    case ".gif": enc = new GifBitmapEncoder(); break;
                    case ".tif": case ".tiff": enc = new TiffBitmapEncoder(); break;
                    default: enc = new PngBitmapEncoder(); break;
                }
                enc.Frames.Add(BitmapFrame.Create(_bmp));
                var tmp = target + ".saving";
                using (var fs = File.Create(tmp)) enc.Save(fs);
                if (File.Exists(target)) File.Replace(tmp, target, null); else File.Move(tmp, target);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot save image: " + ex.Message, "TaskPad", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!asCopy)
            {
                _original = _bmp;
                _edited = false;
                ShowBitmap();
                // the image may belong to a .task note: pack it again, and refresh thumbnails everywhere
                foreach (var d in Workspace.Docs.Where(d => d.AssetDir != null && _path.StartsWith(d.AssetDir, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    d.Dirty = true;
                    if (Workspace.Settings.AutoSave) d.Save(null, false); else d.Raise();
                }
                foreach (var v in Workspace.AllViews) v.Editor.TextArea.TextView.Redraw();
            }
            Workspace.LastActive?.Toast("Saved " + Path.GetFileName(target));
            return true;
        }

        // ---------------- zoom / pan ----------------

        Point Center() => new Point(_host.ActualWidth / 2, _host.ActualHeight / 2);

        double FitZoom()
        {
            if (_host.ActualWidth <= 0 || _host.ActualHeight <= 0) return 1;
            return Math.Min(1, Math.Min((_host.ActualWidth - 24) / _bmp.PixelWidth, (_host.ActualHeight - 24) / _bmp.PixelHeight));
        }

        void Fit()
        {
            _fitted = true;
            _zoom = FitZoom();
            Apply();
            _move.X = Math.Round((_host.ActualWidth - _bmp.PixelWidth * _zoom) / 2);
            _move.Y = Math.Round((_host.ActualHeight - _bmp.PixelHeight * _zoom) / 2);
            UpdateOverlay();
        }

        void ZoomBy(double factor, Point at) => SetZoom(_zoom * factor, at);

        void SetZoom(double z, Point at)
        {
            _fitted = false;
            z = Math.Max(0.02, Math.Min(64, z));
            // keep the image point under `at` fixed
            double ix = (at.X - _move.X) / _zoom, iy = (at.Y - _move.Y) / _zoom;
            _zoom = z;
            Apply();
            _move.X = at.X - ix * z;
            _move.Y = at.Y - iy * z;
            UpdateOverlay();
        }

        void Apply()
        {
            _scale.ScaleX = _scale.ScaleY = _zoom;
            // crisp pixels when zoomed in for inspection, smooth when zoomed out
            RenderOptions.SetBitmapScalingMode(_img, _zoom >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
            _zoomText.Text = Math.Round(_zoom * 100) + "%";
        }

        void UpdatePixel(Point p)
        {
            int x = (int)Math.Floor((p.X - _move.X) / _zoom), y = (int)Math.Floor((p.Y - _move.Y) / _zoom);
            if (x < 0 || y < 0 || x >= _bmp.PixelWidth || y >= _bmp.PixelHeight) { _pixelText.Text = ""; return; }
            if (_pixels == null)
            {
                var conv = new FormatConvertedBitmap(_bmp, PixelFormats.Bgra32, null, 0);
                _stride = conv.PixelWidth * 4;
                _pixels = new byte[_stride * conv.PixelHeight];
                conv.CopyPixels(_pixels, _stride, 0);
            }
            int i = y * _stride + x * 4;
            byte b = _pixels[i], g = _pixels[i + 1], r = _pixels[i + 2], a = _pixels[i + 3];
            _pixelText.Inlines.Clear();
            _pixelText.Inlines.Add(new System.Windows.Documents.Run($"{x}, {y}   "));
            _pixelText.Inlines.Add(new System.Windows.Documents.Run("■ ") { Foreground = new SolidColorBrush(Color.FromArgb(255, r, g, b)) });
            _pixelText.Inlines.Add(new System.Windows.Documents.Run(a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}"));
        }

        void TogglePin()
        {
            Topmost = !Topmost;
            _pinButton.Background = Topmost ? Theme.AccentSoft : Brushes.Transparent;
            _pinButton.ToolTip = Topmost ? "Pinned on top (P to unpin)" : "Pin on top — keep as reference (P)";
        }

        // ---------------- ui bits ----------------

        static TextBlock Label(string s) => new TextBlock
        {
            Text = s, Foreground = Theme.FgDim, FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };

        static UIElement Sep() => new Border { Width = 1, Height = 18, Background = Theme.ChromeBorder, Margin = new Thickness(6, 0, 6, 0) };

        Border ToolButton(string glyph, string tip, Action click)
        {
            var tb = new TextBlock
            {
                Text = glyph, Foreground = Theme.Fg, FontSize = glyph.Length > 1 && glyph != "1:1" && glyph != "Fit" ? 13 : 13.5,
                FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var b = new Border
            {
                MinWidth = 30, Height = 28, Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent, Child = tb, ToolTip = tip, Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var hover = Theme.Hover;
            b.MouseEnter += (s, e) => { if (b != _pinButton || !Topmost) b.Background = hover; };
            b.MouseLeave += (s, e) => { if (b != _pinButton || !Topmost) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        static Brush Checkerboard()
        {
            var a = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x16));
            var b = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1E));
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(a, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            g.Children.Add(new GeometryDrawing(b, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            g.Children.Add(new GeometryDrawing(b, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
            return new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
        }
    }
}
