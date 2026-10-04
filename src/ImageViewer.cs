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

        readonly string _path;
        readonly BitmapSource _bmp;
        readonly Image _img = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        readonly Canvas _host = new Canvas { ClipToBounds = true };
        readonly ScaleTransform _scale = new ScaleTransform();
        readonly TranslateTransform _move = new TranslateTransform();
        readonly TextBlock _zoomText = Label(""), _pixelText = Label("");
        readonly Border _pinButton;
        byte[] _pixels;
        int _stride;
        double _zoom = 1;
        bool _fitted = true;
        Point? _dragFrom;
        Point _dragOrigin;

        public static void Show(string path)
        {
            if (OpenViewers.TryGetValue(path, out var v))
            {
                if (v.WindowState == WindowState.Minimized) v.WindowState = WindowState.Normal;
                v.Activate();
                return;
            }
            try
            {
                v = new ImageViewer(path);
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

        ImageViewer(string path)
        {
            _path = path;
            _bmp = Images.Load(path);
            Title = Path.GetFileName(path) + " — TaskPad";
            Background = Theme.Chrome;
            UseLayoutRounding = true;
            MinWidth = 320;
            MinHeight = 240;
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/TaskPad;component/assets/taskpad.ico")); } catch { }

            // size to the image, within 80% of the work area
            var wa = SystemParameters.WorkArea;
            double w = Math.Max(MinWidth, Math.Min(_bmp.PixelWidth + 40, wa.Width * 0.8));
            double h = Math.Max(MinHeight, Math.Min(_bmp.PixelHeight + 110, wa.Height * 0.8));
            Width = w;
            Height = h;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _img.Source = _bmp;
            _img.Width = _bmp.PixelWidth;
            _img.Height = _bmp.PixelHeight;
            _img.RenderTransform = new TransformGroup { Children = { _scale, _move } };
            _host.Children.Add(_img);
            _host.Background = Checkerboard();

            _pinButton = ToolButton("📌", "Pin on top — keep as reference (P)", TogglePin);
            var bar = new StackPanel { Orientation = Orientation.Horizontal };
            bar.Children.Add(ToolButton("−", "Zoom out (−)", () => ZoomBy(1 / 1.25, Center())));
            bar.Children.Add(new Border { Width = 58, Child = _zoomText });
            bar.Children.Add(ToolButton("+", "Zoom in (+)", () => ZoomBy(1.25, Center())));
            bar.Children.Add(Sep());
            bar.Children.Add(ToolButton("Fit", "Fit to window (0)", Fit));
            bar.Children.Add(ToolButton("1:1", "Actual size (1)", () => SetZoom(1, Center())));
            bar.Children.Add(Sep());
            bar.Children.Add(_pinButton);
            bar.Children.Add(ToolButton("⧉", "Copy image (Ctrl+C)", () => Clipboard.SetImage(_bmp)));
            bar.Children.Add(ToolButton("↗", "Open in default app", () => Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true })));
            bar.Children.Add(ToolButton("📁", "Show in Explorer", () => Process.Start("explorer.exe", $"/select,\"{_path}\"")));

            var info = new TextBlock
            {
                Text = $"{_bmp.PixelWidth} × {_bmp.PixelHeight}   ·   {new FileInfo(path).Length / 1024.0:0.#} KB",
                Foreground = Theme.FgDim, FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            };
            _pixelText.Margin = new Thickness(0, 0, 12, 0);
            _pixelText.HorizontalAlignment = HorizontalAlignment.Right;

            var bottom = new Grid { Height = 38, Background = Theme.Chrome };
            bottom.Children.Add(info);
            bottom.Children.Add(new Border { Child = bar, HorizontalAlignment = HorizontalAlignment.Center });
            bottom.Children.Add(_pixelText);
            var bottomBorder = new Border { Child = bottom, BorderBrush = Theme.ChromeBorder, BorderThickness = new Thickness(0, 1, 0, 0) };

            var root = new DockPanel();
            DockPanel.SetDock(bottomBorder, Dock.Bottom);
            root.Children.Add(bottomBorder);
            root.Children.Add(_host);
            Content = root;

            _host.SizeChanged += (s, e) => { if (_fitted) Fit(); };
            _host.MouseWheel += (s, e) => ZoomBy(e.Delta > 0 ? 1.2 : 1 / 1.2, e.GetPosition(_host));
            _host.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    if (_fitted || Math.Abs(_zoom - FitZoom()) < 0.001) SetZoom(1, e.GetPosition(_host)); else Fit();
                    return;
                }
                _dragFrom = e.GetPosition(_host);
                _dragOrigin = new Point(_move.X, _move.Y);
                _host.CaptureMouse();
                _host.Cursor = Cursors.SizeAll;
            };
            _host.MouseMove += (s, e) =>
            {
                var p = e.GetPosition(_host);
                if (_dragFrom is Point from && e.LeftButton == MouseButtonState.Pressed)
                {
                    _fitted = false;
                    _move.X = _dragOrigin.X + p.X - from.X;
                    _move.Y = _dragOrigin.Y + p.Y - from.Y;
                }
                UpdatePixel(p);
            };
            _host.MouseLeftButtonUp += (s, e) => { _dragFrom = null; _host.ReleaseMouseCapture(); _host.Cursor = Cursors.Arrow; };
            _host.MouseLeave += (s, e) => _pixelText.Text = "";

            PreviewKeyDown += (s, e) =>
            {
                var k = e.Key;
                if (k == Key.Escape) Close();
                else if (k == Key.OemPlus || k == Key.Add) ZoomBy(1.25, Center());
                else if (k == Key.OemMinus || k == Key.Subtract) ZoomBy(1 / 1.25, Center());
                else if (k == Key.D0 || k == Key.NumPad0 || k == Key.F) Fit();
                else if (k == Key.D1 || k == Key.NumPad1) SetZoom(1, Center());
                else if (k == Key.P) TogglePin();
                else if (k == Key.C && Keyboard.Modifiers == ModifierKeys.Control) Clipboard.SetImage(_bmp);
                else return;
                e.Handled = true;
            };
            SourceInitialized += (s, e) => TaskWindow.ApplyDarkTitleBar(this);
            Closed += (s, e) => OpenViewers.Remove(_path);
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
