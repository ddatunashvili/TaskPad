using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace TaskPad
{
    /// Tab dragging: animated reorder inside a strip, drop into another group, drop on an
    /// editor edge to split, or drop outside every TaskPad window to tear the tab out.
    public static class TabDrag
    {
        enum Kind { None, Strip, Split, Center, NewWindow }

        sealed class Target
        {
            public Kind Kind;
            public TaskWindow Window;
            public EditorGroup Group;
            public int Index;
            public Dock Side;
        }

        static TabView _tab, _pending;
        static Point _downPx;
        static double _grabX;
        static bool _floating;
        static Window _ghost;
        static TextBlock _ghostHint;
        static Target _target;
        static TaskWindow _overlayWindow;

        const double Threshold = 6, StripSlack = 26;
        static readonly Duration Anim = new Duration(TimeSpan.FromMilliseconds(170));
        static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        public static void Attach(TabView tab, UIElement closeButton)
        {
            var h = tab.Header;
            h.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (IsInside(e.OriginalSource as DependencyObject, closeButton)) return;
                tab.Group?.Activate(tab);
                _pending = tab;
                _downPx = CursorPx();
                _grabX = e.GetPosition(h).X;
                h.CaptureMouse();
                e.Handled = true;
            };
            h.MouseMove += (s, e) =>
            {
                if (_pending != tab && _tab != tab) return;
                if (e.LeftButton != MouseButtonState.Pressed) { Finish(false); return; }
                var p = CursorPx();
                if (_tab == null)
                {
                    if (Math.Abs(p.X - _downPx.X) < Threshold && Math.Abs(p.Y - _downPx.Y) < Threshold) return;
                    Begin(tab);
                }
                Update(p);
            };
            h.MouseLeftButtonUp += (s, e) =>
            {
                if (_tab == tab) Finish(true);
                _pending = null;
                if (h.IsMouseCaptured) h.ReleaseMouseCapture();
            };
            h.LostMouseCapture += (s, e) =>
            {
                if (_tab == tab && !_dropping) Finish(false);
            };
        }

        static bool _dropping;

        static bool IsInside(DependencyObject d, UIElement ancestor)
        {
            for (; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d == ancestor) return true;
            return false;
        }

        static void Begin(TabView tab)
        {
            _tab = tab;
            _pending = null;
            _floating = false;
            Panel.SetZIndex(tab.Header, 100);
        }

        // ---------------- move ----------------

        static void Update(Point px)
        {
            var g = _tab.Group;
            var strip = g.Strip;
            var sp = strip.PointFromScreen(px);
            bool overOwnStrip = sp.Y > -StripSlack && sp.Y < strip.ActualHeight + StripSlack && sp.X > -StripSlack && sp.X < g.Bar.ActualWidth;
            if (overOwnStrip)
            {
                if (_floating) Land();
                Reorder(sp.X - _grabX);
                return;
            }
            if (!_floating) Lift();
            MoveGhost(px);
            _target = FindTarget(px);
            ShowTarget(_target);
        }

        /// Live reorder: dragged header follows the mouse, neighbours slide out of the way.
        static void Reorder(double desiredLeft)
        {
            var strip = _tab.Group.Strip;
            var h = _tab.Header;
            double total = strip.Children.Cast<FrameworkElement>().Sum(c => c.ActualWidth);
            desiredLeft = Math.Max(0, Math.Min(desiredLeft, total - h.ActualWidth));

            while (true)
            {
                int i = strip.Children.IndexOf(h);
                double layoutLeft = 0;
                for (int k = 0; k < i; k++) layoutLeft += ((FrameworkElement)strip.Children[k]).ActualWidth;
                double shift = desiredLeft - layoutLeft;
                _tab.Shift.BeginAnimation(TranslateTransform.XProperty, null);
                _tab.Shift.X = shift;

                if (i + 1 < strip.Children.Count && shift > ((FrameworkElement)strip.Children[i + 1]).ActualWidth / 2)
                {
                    var next = (FrameworkElement)strip.Children[i + 1];
                    strip.Children.RemoveAt(i + 1);
                    strip.Children.Insert(i, next);
                    Slide(next, h.ActualWidth);
                    strip.UpdateLayout();
                    continue;
                }
                if (i > 0 && shift < -((FrameworkElement)strip.Children[i - 1]).ActualWidth / 2)
                {
                    var prev = (FrameworkElement)strip.Children[i - 1];
                    strip.Children.RemoveAt(i - 1);
                    strip.Children.Insert(i, prev);
                    Slide(prev, -h.ActualWidth);
                    strip.UpdateLayout();
                    continue;
                }
                break;
            }
        }

        /// Element jumped by `from` px in layout; animate it from its old spot to the new one.
        static void Slide(FrameworkElement e, double from)
        {
            var tv = (TabView)e.Tag;
            double current = tv.Shift.X;
            tv.Shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(current + from, 0, Anim) { EasingFunction = Ease });
        }

        static void Lift()
        {
            _floating = true;
            var h = _tab.Header;
            _tab.Shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Anim) { EasingFunction = Ease });
            h.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.35, Anim));
            ShowGhost();
        }

        static void Land()
        {
            _floating = false;
            _tab.Header.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, Anim));
            HideGhost();
            ClearOverlay();
            _target = null;
        }

        // ---------------- targets ----------------

        static Target FindTarget(Point px)
        {
            var w = WindowAt(px);
            if (w == null) return new Target { Kind = Kind.NewWindow };
            var p = w.OverlayPointFromScreen(px);
            foreach (var g in w.Groups)
            {
                var bar = w.BoundsOf(g.Bar);
                if (bar.Contains(p))
                    return new Target { Kind = Kind.Strip, Window = w, Group = g, Index = InsertIndex(g, px) };
                var body = w.BoundsOf(g.Body);
                if (!body.Contains(p)) continue;

                double rx = (p.X - body.X) / body.Width, ry = (p.Y - body.Y) / body.Height;
                bool soleTab = g == _tab.Group && g.Tabs.Count == 1;
                Kind kind = Kind.Split;
                Dock side = Dock.Right;
                if (rx < 0.28) side = Dock.Left;
                else if (rx > 0.72) side = Dock.Right;
                else if (ry < 0.28) side = Dock.Top;
                else if (ry > 0.72) side = Dock.Bottom;
                else kind = Kind.Center;
                if (soleTab || (kind == Kind.Center && g == _tab.Group)) return new Target { Kind = Kind.None };
                return new Target { Kind = kind, Window = w, Group = g, Side = side, Index = g.Tabs.Count };
            }
            return new Target { Kind = Kind.None };
        }

        static int InsertIndex(EditorGroup g, Point px)
        {
            var sp = g.Strip.PointFromScreen(px);
            double x = 0;
            int i = 0;
            foreach (FrameworkElement c in g.Strip.Children)
            {
                if (sp.X < x + c.ActualWidth / 2) return i;
                x += c.ActualWidth;
                i++;
            }
            return i;
        }

        static void ShowTarget(Target t)
        {
            var w = t.Window;
            if (_overlayWindow != null && _overlayWindow != w) _overlayWindow.HideDropRect();
            _overlayWindow = w;
            _ghostHint.Text = t.Kind == Kind.NewWindow ? "↗  Drop to open in a new window"
                            : t.Kind == Kind.Split ? "◫  Split " + t.Side.ToString().ToLowerInvariant()
                            : t.Kind == Kind.Strip || t.Kind == Kind.Center ? "→  Move here" : "";
            _ghostHint.Visibility = _ghostHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (w == null) return;

            Rect r;
            switch (t.Kind)
            {
                case Kind.Strip:
                {
                    var bar = w.BoundsOf(t.Group.Bar);
                    double x = 0;
                    for (int i = 0; i < t.Index && i < t.Group.Strip.Children.Count; i++) x += ((FrameworkElement)t.Group.Strip.Children[i]).ActualWidth;
                    var stripOrigin = w.BoundsOf(t.Group.Strip);
                    r = new Rect(stripOrigin.X + x - 1, bar.Y + 4, 3, bar.Height - 8);
                    break;
                }
                case Kind.Split:
                {
                    var b = w.BoundsOf(t.Group.Body);
                    switch (t.Side)
                    {
                        case Dock.Left: r = new Rect(b.X, b.Y, b.Width / 2, b.Height); break;
                        case Dock.Right: r = new Rect(b.X + b.Width / 2, b.Y, b.Width / 2, b.Height); break;
                        case Dock.Top: r = new Rect(b.X, b.Y, b.Width, b.Height / 2); break;
                        default: r = new Rect(b.X, b.Y + b.Height / 2, b.Width, b.Height / 2); break;
                    }
                    r.Inflate(-4, -4);
                    break;
                }
                case Kind.Center:
                    r = w.BoundsOf(t.Group.Body);
                    r.Inflate(-4, -4);
                    break;
                default:
                    w.HideDropRect();
                    return;
            }
            w.ShowDropRect(r);
        }

        static void ClearOverlay()
        {
            _overlayWindow?.HideDropRect();
            _overlayWindow = null;
        }

        // ---------------- drop ----------------

        static void Finish(bool drop)
        {
            if (_tab == null) { _pending = null; return; }
            var tab = _tab;
            var target = _target;
            bool floating = _floating;
            _tab = null;
            _target = null;
            _floating = false;
            HideGhost();
            ClearOverlay();
            Panel.SetZIndex(tab.Header, 0);
            tab.Header.BeginAnimation(UIElement.OpacityProperty, null);
            tab.Header.Opacity = 1;

            if (!floating)
            {
                // settle the dragged header into its slot
                tab.Shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Anim) { EasingFunction = Ease });
                tab.Group.SyncOrderFromStrip();
                return;
            }
            tab.Group.SyncOrderFromStrip();
            if (!drop || target == null) return;

            _dropping = true;
            try
            {
                switch (target.Kind)
                {
                    case Kind.Strip:
                    case Kind.Center:
                        TaskWindow.MoveTab(tab, target.Group, target.Index);
                        break;
                    case Kind.Split:
                        var ng = target.Window.Split(target.Group, target.Side);
                        TaskWindow.MoveTab(tab, ng, 0);
                        break;
                    case Kind.NewWindow:
                        TearOut(tab);
                        break;
                }
            }
            finally { _dropping = false; }
        }

        static void TearOut(TabView tab)
        {
            var src = tab.Group.Owner;
            var px = CursorPx();
            var dip = src.PresentationSourceTransformFromDevice(px);
            if (src.TabCount == 1)
            {
                // lone tab: just move its window to the cursor
                if (src.WindowState == WindowState.Maximized) src.WindowState = WindowState.Normal;
                src.Left = dip.X - 80;
                src.Top = dip.Y - 18;
                return;
            }
            var w = new TaskWindow(false)
            {
                Left = dip.X - 80,
                Top = dip.Y - 18,
                Width = Math.Min(src.ActualWidth, 900),
                Height = Math.Min(src.ActualHeight, 640),
            };
            w.Show();
            TaskWindow.MoveTab(tab, w.ActiveGroup, 0);
        }

        // ---------------- ghost ----------------

        static void ShowGhost()
        {
            var tab = _tab;
            ImageSource snap = null;
            try
            {
                var ed = tab.Editor;
                if (ed.ActualWidth > 0 && ed.ActualHeight > 0)
                {
                    int w = (int)Math.Min(ed.ActualWidth, 900), h = (int)Math.Min(ed.ActualHeight, 600);
                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(ed);
                    rtb.Freeze();
                    snap = rtb;
                }
            }
            catch { }

            var title = new TextBlock
            {
                Text = tab.Doc.Name,
                Foreground = Theme.Fg,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(10, 6, 10, 6),
            };
            _ghostHint = new TextBlock
            {
                Foreground = Theme.Accent,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11.5,
                Margin = new Thickness(10, 0, 10, 8),
            };
            var stack = new StackPanel();
            stack.Children.Add(title);
            if (snap != null)
                stack.Children.Add(new Border
                {
                    Margin = new Thickness(6, 0, 6, 6),
                    CornerRadius = new CornerRadius(4),
                    ClipToBounds = true,
                    Child = new Image { Source = snap, Width = 260, Stretch = Stretch.Uniform, Opacity = 0.9 },
                });
            stack.Children.Add(_ghostHint);

            _ghost = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                IsHitTestVisible = false,
                Content = new Border
                {
                    Background = Theme.Popup,
                    BorderBrush = Theme.Accent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Child = stack,
                    Width = 274,
                },
                Opacity = 0,
            };
            MoveGhost(CursorPx());
            _ghost.Show();
            _ghost.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, Anim));
        }

        static void MoveGhost(Point px)
        {
            if (_ghost == null || _tab == null) return;
            var dip = _tab.Group.Owner.PresentationSourceTransformFromDevice(px);
            _ghost.Left = dip.X + 16;
            _ghost.Top = dip.Y + 16;
        }

        static void HideGhost()
        {
            _ghost?.Close();
            _ghost = null;
        }

        // ---------------- win32 ----------------

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);

        static Point CursorPx()
        {
            GetCursorPos(out var p);
            return new Point(p.X, p.Y);
        }

        static TaskWindow WindowAt(Point px)
        {
            var hwnd = GetAncestor(WindowFromPoint(new POINT { X = (int)px.X, Y = (int)px.Y }), 2 /* GA_ROOT */);
            return Workspace.Windows.FirstOrDefault(w => new WindowInteropHelper(w).Handle == hwnd);
        }
    }

    static class WindowDpi
    {
        /// Converts a device-pixel screen point to WPF device-independent units.
        public static Point PresentationSourceTransformFromDevice(this Window w, Point px)
        {
            var src = PresentationSource.FromVisual(w);
            return src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(px) : px;
        }
    }
}
