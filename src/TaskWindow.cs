using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TaskPad
{
    public sealed class TaskWindow : Window
    {
        readonly Border _root = new Border { Background = Theme.Bg };
        readonly Canvas _overlay = new Canvas { IsHitTestVisible = false };
        readonly Border _dropRect = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xC5, 0x5E)),
            BorderBrush = Theme.Accent,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Visibility = Visibility.Collapsed,
        };
        readonly Border _toast = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 18),
            Padding = new Thickness(12, 7, 8, 7),
            CornerRadius = new CornerRadius(8),
            Background = Theme.Popup,
            BorderBrush = Theme.ChromeBorder,
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
        };
        DispatcherTimer _toastTimer;
        readonly TextBlock _stPos = StatusText(), _stTasks = StatusText(), _stInfo = StatusText();
        readonly Border _progressTrack = new Border { Width = 70, Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.FgFaint, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly Border _progressFill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        readonly DispatcherTimer _statsTimer;

        readonly Border _welcomeHost = new Border { Visibility = Visibility.Collapsed };
        public bool WelcomeVisible => _welcomeHost.Visibility == Visibility.Visible;

        public void ShowWelcome()
        {
            _welcomeHost.Child = new WelcomePage(this);
            _welcomeHost.Visibility = Visibility.Visible;
        }

        public void HideWelcome()
        {
            _welcomeHost.Visibility = Visibility.Collapsed;
            _welcomeHost.Child = null;
            ActiveTab?.Editor.TextArea.Focus();
        }

        public void RefreshWelcome() { if (WelcomeVisible) ShowWelcome(); }

        public void OpenFileDialog() => OpenDialog();

        public void ShowKeywords()
        {
            var g = ActiveGroup ?? Groups.First();
            if (g.Active == null) NewTab(g);
            KeywordsPopup.Show(g.Bar, () => g.Active?.Editor, ShowCheatSheet);
        }

        public void ShowCodeThemesAt(FrameworkElement anchor) => ShowCodeThemes(anchor);

        /// The interactive tour, as an untitled note (only saved if the user wants to).
        public void OpenTour()
        {
            var t = NewTab(null, Due.ExpandText(WelcomePage.TourText));
            t.Doc.UntitledName = "Tour";
            t.Doc.Dirty = false;
            t.Doc.Raise();
            UpdateStats();
        }

        public ExplorerPanel Explorer;
        readonly Grid _body = new Grid();
        FrameworkElement _explorerEdge;

        public bool ExplorerVisible => Explorer.Visibility == Visibility.Visible;

        public void SetExplorerVisible(bool on)
        {
            Explorer.Visibility = _explorerEdge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Workspace.Settings.ExplorerVisible = on;
            foreach (var g in Groups) g.UpdateToggles();
        }

        public void ToggleExplorer() => SetExplorerVisible(!ExplorerVisible);

        /// Opens a folder as the project in the sidebar (a new window if this one already shows another folder).
        public void OpenFolder(string dir)
        {
            dir = Path.GetFullPath(dir);
            if (Explorer.Root != null && !string.Equals(Explorer.Root.TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && TabCount > 0)
            {
                var w = new TaskWindow(false) { Left = Left + 40, Top = Top + 40, Width = ActualWidth, Height = ActualHeight };
                w.NewTab();
                w.Show();
                w.OpenFolder(dir);
                return;
            }
            Explorer.Open(dir);
            SetExplorerVisible(true);
            Activate();
        }

        public EditorGroup ActiveGroup { get; private set; }
        public TabView ActiveTab => ActiveGroup?.Active;
        public int TabCount => Groups.Sum(g => g.Tabs.Count);

        public TaskWindow(bool restoreBounds = true)
        {
            var st = Workspace.Settings;
            Title = "TaskPad";
            Background = Theme.Chrome;
            Width = st.Width;
            Height = st.Height;
            MinWidth = 360;
            MinHeight = 240;
            if (restoreBounds && !double.IsNaN(st.Left) && !double.IsNaN(st.Top) && OnScreen(st.Left, st.Top))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = st.Left;
                Top = st.Top;
                if (st.Maximized) WindowState = WindowState.Maximized;
            }
            else if (restoreBounds) WindowStartupLocation = WindowStartupLocation.CenterScreen;
            else WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            AllowDrop = true;
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/TaskPad;component/assets/taskpad.ico")); } catch { }

            _progressTrack.Child = _progressFill;
            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _statsTimer.Tick += (s, e) => { _statsTimer.Stop(); UpdateStats(); };

            var first = new EditorGroup(this);
            _root.Child = first;
            ActiveGroup = first;
            _overlay.Children.Add(_dropRect);
            Content = BuildLayout();

            PreviewKeyDown += OnWindowKey;
            PreviewDragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
            };
            PreviewDrop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    var images = files.Where(f => File.Exists(f) && Images.IsImageFile(f)).ToList();
                    var tab = TabUnder(e.OriginalSource as DependencyObject) ?? ActiveTab;
                    if (images.Count > 0 && tab != null)
                    {
                        var pos = tab.Editor.GetPositionFromPoint(e.GetPosition(tab.Editor));
                        if (pos.HasValue) tab.Editor.TextArea.Caret.Position = pos.Value;
                        Images.InsertFiles(tab.Editor, tab.Doc, images);
                        tab.Group.Activate(tab);
                    }
                    foreach (var f in files.Where(f => File.Exists(f) && !Images.IsImageFile(f)))
                    {
                        if (Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            try { Process.Start(new ProcessStartInfo(f) { UseShellExecute = true }); } catch { }
                            Toast("Opened " + Path.GetFileName(f) + " in your PDF viewer");
                        }
                        else if (Doc.LooksBinary(f) && !TaskFile.Is(f)) Toast("Can't open " + Path.GetFileName(f) + " — not a text file");
                        else OpenFile(f);
                    }
                    e.Handled = true;
                }
            };
            Activated += (s, e) => { Workspace.LastActive = this; Workspace.ReloadChangedFiles(); };
            SourceInitialized += (s, e) => DarkTitleBar();
            Closing += OnClosing;
            Closed += (s, e) =>
            {
                Workspace.Windows.Remove(this);
                Session.MarkDirty();
                if (Workspace.Windows.Count == 0) ImageViewer.CloseAll();
                if (Workspace.LastActive == this) Workspace.LastActive = Workspace.Windows.LastOrDefault();
            };
            Workspace.Windows.Add(this);
            Workspace.LastActive = this;
        }

        UIElement BuildLayout()
        {
            var root = new DockPanel();
            var status = new DockPanel { Height = 24, Background = Theme.Chrome, LastChildFill = false };
            var explorerToggle = new TextBlock
            {
                Text = "\uE8B7", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13,
                Foreground = Theme.FgDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
                Cursor = Cursors.Hand, ToolTip = "Explorer (Ctrl+B)",
            };
            explorerToggle.MouseLeftButtonUp += (s, e) => ToggleExplorer();
            explorerToggle.MouseEnter += (s, e) => explorerToggle.Foreground = Theme.Fg;
            explorerToggle.MouseLeave += (s, e) => explorerToggle.Foreground = Theme.FgDim;
            DockPanel.SetDock(explorerToggle, Dock.Left);
            status.Children.Add(explorerToggle);
            _stPos.Margin = new Thickness(12, 0, 16, 0);
            DockPanel.SetDock(_stPos, Dock.Left);
            status.Children.Add(_stPos);
            var tasks = new StackPanel { Orientation = Orientation.Horizontal };
            tasks.Children.Add(_progressTrack);
            tasks.Children.Add(_stTasks);
            DockPanel.SetDock(tasks, Dock.Left);
            status.Children.Add(tasks);
            _stInfo.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(_stInfo, Dock.Right);
            status.Children.Add(_stInfo);
            var statusBorder = new Border { Child = status, BorderBrush = Theme.ChromeBorder, BorderThickness = new Thickness(0, 1, 0, 0) };
            DockPanel.SetDock(statusBorder, Dock.Bottom);
            root.Children.Add(statusBorder);

            var layer = new Grid();
            layer.Children.Add(_root);
            layer.Children.Add(_welcomeHost);
            layer.Children.Add(_overlay);
            layer.Children.Add(_toast);

            // [explorer | splitter | editors]
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Explorer = new ExplorerPanel(this);
            var edge = new Border { Width = 1, Background = Theme.ChromeBorder, HorizontalAlignment = HorizontalAlignment.Left };
            var split = new System.Windows.Controls.Primitives.Thumb { Width = 5, Cursor = Cursors.SizeWE, Opacity = 0 };
            split.DragDelta += (s, e) =>
            {
                Explorer.Width = Math.Max(160, Math.Min(ActualWidth - 300, Explorer.Width + e.HorizontalChange));
                Workspace.Settings.ExplorerWidth = Explorer.Width;
            };
            var splitHost = new Grid { Width = 5 };
            splitHost.Children.Add(edge);
            splitHost.Children.Add(split);
            Grid.SetColumn(Explorer, 0);
            Grid.SetColumn(splitHost, 1);
            Grid.SetColumn(layer, 2);
            _body.Children.Add(Explorer);
            _body.Children.Add(splitHost);
            _body.Children.Add(layer);
            _explorerEdge = splitHost;
            SetExplorerVisible(Workspace.Settings.ExplorerVisible);
            root.Children.Add(_body);
            return root;
        }

        static TextBlock StatusText() => new TextBlock
        {
            Foreground = Theme.FgDim,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // ---------------- groups / split layout ----------------

        public IEnumerable<EditorGroup> Groups
        {
            get
            {
                var list = new List<EditorGroup>();
                void Walk(UIElement e)
                {
                    if (e is EditorGroup g) list.Add(g);
                    else if (e is Grid grid) foreach (UIElement c in grid.Children) Walk(c);
                }
                Walk(_root.Child);
                return list;
            }
        }

        TabView TabUnder(DependencyObject d)
        {
            for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is EditorGroup g) return g.Active;
            return null;
        }

        public void SetActiveGroup(EditorGroup g)
        {
            if (ActiveGroup == g || g == null) return;
            var old = ActiveGroup;
            ActiveGroup = g;
            old?.RefreshHeaders();
            g.RefreshHeaders();
            UpdateStatus();
            UpdateStats();
        }

        /// Splits `target` and returns the new empty group placed on `side`.
        public EditorGroup Split(EditorGroup target, Dock side)
        {
            var group = new EditorGroup(this);
            bool horizontal = side == Dock.Left || side == Dock.Right;
            bool newFirst = side == Dock.Left || side == Dock.Top;
            var placeholder = new Border();
            Replace(target, placeholder);
            var grid = SplitGrid(newFirst ? group : target, newFirst ? target : group, horizontal);
            Replace(placeholder, grid);
            return group;
        }

        static Grid SplitGrid(FrameworkElement a, FrameworkElement b, bool horizontal)
        {
            var grid = new Grid();
            var splitter = new GridSplitter
            {
                Background = Theme.ChromeBorder,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = false,
            };
            if (horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
                splitter.Cursor = Cursors.SizeWE;
                Grid.SetColumn(splitter, 1);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
                splitter.Cursor = Cursors.SizeNS;
                Grid.SetRow(splitter, 1);
            }

            Place(a, horizontal, 0);
            Place(b, horizontal, 2);
            grid.Children.Add(a);
            grid.Children.Add(splitter);
            grid.Children.Add(b);
            return grid;
        }

        /// Layout as text: groups are numbered in Groups order, e.g. "(H 0 (V 1 2))".
        public string LayoutSpec()
        {
            int n = 0;
            string Walk(UIElement e)
            {
                if (e is EditorGroup) return (n++).ToString();
                if (e is Grid g)
                {
                    var parts = g.Children.OfType<FrameworkElement>().Where(c => !(c is GridSplitter))
                        .OrderBy(c => Grid.GetColumn(c) + Grid.GetRow(c)).Select(c => Walk(c)).ToList();
                    return $"({(g.ColumnDefinitions.Count == 3 ? "H" : "V")} {string.Join(" ", parts)})";
                }
                return "0";
            }
            return Walk(_root.Child);
        }

        /// Rebuilds the split layout from LayoutSpec(); returns the new groups in order.
        public List<EditorGroup> ApplyLayout(string spec)
        {
            var groups = new List<EditorGroup>();
            int i = 0;
            FrameworkElement Parse()
            {
                while (i < spec.Length && spec[i] == ' ') i++;
                if (i < spec.Length && spec[i] == '(')
                {
                    i++;
                    bool h = spec[i] == 'H';
                    i++;
                    var a = Parse();
                    var b = Parse();
                    while (i < spec.Length && spec[i] != ')') i++;
                    i++;
                    return SplitGrid(a, b, h);
                }
                while (i < spec.Length && char.IsDigit(spec[i])) i++;
                var g = new EditorGroup(this);
                groups.Add(g);
                return g;
            }
            try
            {
                var tree = Parse();
                _root.Child = tree;
            }
            catch
            {
                groups.Clear();
                var g = new EditorGroup(this);
                groups.Add(g);
                _root.Child = g;
            }
            ActiveGroup = groups[0];
            return groups;
        }

        static void Place(UIElement e, bool horizontal, int index)
        {
            Grid.SetColumn(e, horizontal ? index : 0);
            Grid.SetRow(e, horizontal ? 0 : index);
        }

        /// Puts `neu` where `old` is in the layout tree.
        void Replace(FrameworkElement old, FrameworkElement neu)
        {
            var parent = old.Parent;
            if (parent == _root) { _root.Child = neu; return; }
            if (parent is Grid g)
            {
                int col = Grid.GetColumn(old), row = Grid.GetRow(old);
                g.Children.Remove(old);
                Grid.SetColumn(neu, col);
                Grid.SetRow(neu, row);
                g.Children.Add(neu);
            }
        }

        /// Called when a group has no tabs left: collapse its split, or close the window.
        public void RemoveGroup(EditorGroup g)
        {
            if (g.Parent == _root)
            {
                _root.Child = null;
                Dispatcher.BeginInvoke(new Action(Close));
                return;
            }
            if (g.Parent is Grid grid)
            {
                var sibling = grid.Children.OfType<FrameworkElement>().First(c => c != g && !(c is GridSplitter));
                grid.Children.Clear();
                Replace(grid, sibling);
            }
            if (ActiveGroup == g)
            {
                ActiveGroup = null;
                var next = Groups.FirstOrDefault();
                if (next != null) { SetActiveGroup(next); next.Active?.Editor.TextArea.Focus(); }
            }
        }

        public void SplitActive(EditorGroup g, Dock side)
        {
            var tab = g?.Active;
            if (tab == null) return;
            var ng = Split(g, side);
            ng.Insert(new TabView(tab.Doc), 0);
        }

        // ---------------- tabs ----------------

        public TabView NewTab(EditorGroup g = null, string text = "")
        {
            g = g ?? ActiveGroup ?? Groups.First();
            var t = new TabView(new Doc(text, null));
            g.Insert(t, g.Tabs.Count);
            return t;
        }

        public void OpenFile(string path)
        {
            path = Path.GetFullPath(path);
            if (Directory.Exists(path)) { OpenFolder(path); return; }
            var g = ActiveGroup ?? Groups.First();
            var existing = Groups.SelectMany(x => x.Tabs).FirstOrDefault(t => t.Doc.Path != null && string.Equals(t.Doc.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { existing.Group.Activate(existing); return; }

            Doc doc = Workspace.FindByPath(path);
            if (doc == null)
            {
                try { doc = Doc.Open(path); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Cannot open {path}\n\n{ex.Message}", "TaskPad", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Replace a pristine empty untitled tab instead of stacking next to it.
            var blank = g.Tabs.Count == 1 && g.Tabs[0].Doc.Path == null && !g.Tabs[0].Doc.Dirty && g.Tabs[0].Doc.Document.TextLength == 0 ? g.Tabs[0] : null;
            g.Insert(new TabView(doc), g.Tabs.Count);
            if (blank != null) { g.Remove(blank); blank.Detach(); }
        }

        public void ReceiveFromOtherInstance(string[] paths)
        {
            if (paths.Length == 0) NewTab();
            foreach (var p in paths) OpenFile(p);
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        public void CloseTab(TabView t)
        {
            if (t?.Group == null) return;
            if (t.Doc.Views.Count == 1)
            {
                t.Group.Activate(t, focus: false);
                if (!t.Doc.ConfirmClose(this)) return;
            }
            var g = t.Group;
            g.Remove(t);
            if (t.Doc.Views.Count == 1) Recent.Add(t.Doc.Path);
            t.Detach();
            if (g.Tabs.Count == 0) RemoveGroup(g);
        }

        /// Moves a tab into `target` at `index` (any window). Empty source groups collapse.
        public static void MoveTab(TabView t, EditorGroup target, int index)
        {
            var src = t.Group;
            if (src == target)
            {
                int cur = src.Tabs.IndexOf(t);
                if (index > cur) index--;
                src.Strip.Children.Remove(t.Header);
                src.Tabs.Remove(t);
                src.Insert(t, index);
                return;
            }
            src.Remove(t);
            target.Insert(t, index);
            target.Owner.SetActiveGroup(target);
            if (src.Tabs.Count == 0) src.Owner.RemoveGroup(src);
            if (target.Owner != src.Owner) target.Owner.Activate();
        }

        void CycleTab(int dir)
        {
            var g = ActiveGroup;
            if (g == null || g.Tabs.Count < 2) return;
            int i = (g.Tabs.IndexOf(g.Active) + dir + g.Tabs.Count) % g.Tabs.Count;
            g.Activate(g.Tabs[i]);
        }

        public void ShowCheatSheet()
        {
            var t = NewTab(null, CheatSheet);
            t.Doc.UntitledName = "Cheat sheet";
            t.Doc.Dirty = false;
            t.Doc.Raise();
            UpdateStats();
        }

        public void MoveToNewWindow(TabView t)
        {
            if (t == null) return;
            var w = new TaskWindow(false) { Left = Left + 40, Top = Top + 40, Width = ActualWidth, Height = ActualHeight };
            w.Show();
            MoveTab(t, w.ActiveGroup, 0);
        }

        // ---------------- toast ----------------

        public static void ToastFrom(DependencyObject from, string text, string actionText = null, Action action = null, Color? swatch = null)
        {
            var w = Window.GetWindow(from) as TaskWindow ?? Workspace.LastActive;
            w?.Toast(text, actionText, action, swatch);
        }

        public void Toast(string text, string actionText = null, Action action = null, Color? swatch = null, double seconds = 2.6)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (swatch.HasValue)
                sp.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(swatch.Value), BorderBrush = Brushes.White, BorderThickness = new Thickness(0.5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = text, Foreground = Theme.Fg, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            if (actionText != null)
            {
                var b = new Border
                {
                    Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(10, 3, 10, 4), CornerRadius = new CornerRadius(5),
                    Background = Theme.AccentSoft, Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = actionText, Foreground = Theme.Fg, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5 },
                };
                b.MouseLeftButtonUp += (s, e) => { _toast.Visibility = Visibility.Collapsed; action?.Invoke(); };
                sp.Children.Add(b);
            }
            _toast.Child = sp;
            _toast.Visibility = Visibility.Visible;
            _toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                if (_toast.IsMouseOver) { _toastTimer.Start(); return; }
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
                fade.Completed += (s2, e2) => _toast.Visibility = Visibility.Collapsed;
                _toast.BeginAnimation(OpacityProperty, fade);
            };
            _toastTimer.Start();
        }

        // ---------------- drag & drop overlay ----------------

        public void ShowDropRect(Rect r)
        {
            if (_dropRect.Visibility != Visibility.Visible)
            {
                Canvas.SetLeft(_dropRect, r.X); Canvas.SetTop(_dropRect, r.Y);
                _dropRect.Width = r.Width; _dropRect.Height = r.Height;
                _dropRect.Opacity = 0;
                _dropRect.Visibility = Visibility.Visible;
            }
            var d = new Duration(TimeSpan.FromMilliseconds(140));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _dropRect.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(r.X, d) { EasingFunction = ease });
            _dropRect.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(r.Y, d) { EasingFunction = ease });
            _dropRect.BeginAnimation(WidthProperty, new DoubleAnimation(Math.Max(2, r.Width), d) { EasingFunction = ease });
            _dropRect.BeginAnimation(HeightProperty, new DoubleAnimation(Math.Max(2, r.Height), d) { EasingFunction = ease });
            _dropRect.BeginAnimation(OpacityProperty, new DoubleAnimation(1, d));
        }

        public void HideDropRect()
        {
            if (_dropRect.Visibility != Visibility.Visible) return;
            _dropRect.BeginAnimation(Canvas.LeftProperty, null);
            _dropRect.BeginAnimation(Canvas.TopProperty, null);
            _dropRect.BeginAnimation(WidthProperty, null);
            _dropRect.BeginAnimation(HeightProperty, null);
            _dropRect.BeginAnimation(OpacityProperty, null);
            _dropRect.Visibility = Visibility.Collapsed;
        }

        /// Bounds of an element in overlay coordinates.
        public Rect BoundsOf(FrameworkElement e) =>
            e.TransformToVisual(_overlay).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

        public Point OverlayPointFromScreen(Point px) => _overlay.PointFromScreen(px);

        // ---------------- menu ----------------

        public void ShowMenu(FrameworkElement anchor, EditorGroup g)
        {
            SetActiveGroup(g);
            var m = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            var tab = g.Active;
            var st = Workspace.Settings;

            MenuItem Item(ItemsControl parent, string header, string gesture, Action a, bool? check = null)
            {
                var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
                if (check.HasValue) { mi.IsCheckable = true; mi.IsChecked = check.Value; }
                mi.Click += (s, e) => { e.Handled = true; a(); };
                parent.Items.Add(mi);
                return mi;
            }
            MenuItem Sub(string header)
            {
                var mi = new MenuItem { Header = header };
                m.Items.Add(mi);
                return mi;
            }

            // ---- file
            Item(m, "New tab", "Ctrl+N", () => NewTab(g));
            Item(m, "New window", "Ctrl+Shift+N", () => NewWindow());
            Item(m, "Open…", "Ctrl+O", OpenDialog);
            Item(m, "Open Folder…", "Ctrl+Shift+O", () => { SetExplorerVisible(true); Explorer.PickFolder(); });
            var recent = Sub("Recently closed");
            var closed = Recent.List();
            if (closed.Count == 0) recent.Items.Add(new MenuItem { Header = "Nothing closed yet", IsEnabled = false });
            foreach (var p in closed.Take(12))
            {
                var path = p;
                Item(recent, System.IO.Path.GetFileName(p), null, () => OpenFile(path)).ToolTip = p;
            }
            if (closed.Count > 0)
            {
                recent.Items.Add(new Separator());
                Item(recent, "Reopen last", "Ctrl+Shift+T", () => Recent.ReopenLast(this));
            }
            Item(m, "Save", "Ctrl+S", () => tab?.Doc.Save(this, false));
            Item(m, "Save as…", "Ctrl+Shift+S", () => tab?.Doc.Save(this, true));
            var io = Sub("Import / Export");
            Item(io, "Export to PDF…", "Ctrl+P", () => PdfExport.Run(this, tab?.Doc)).IsEnabled = tab != null;
            Item(io, "Export as .task (with images)…", null, () => TaskFile.ExportCopy(this, tab?.Doc)).IsEnabled = tab != null;
            Item(io, "Export as Markdown…", null, () => MarkdownIO.ExportMarkdown(this, tab?.Doc)).IsEnabled = tab != null;
            Item(io, "Export as plain text…", null, () => MarkdownIO.ExportText(this, tab?.Doc)).IsEnabled = tab != null;
            io.Items.Add(new Separator());
            Item(io, "Import Markdown…", null, () => MarkdownIO.Import(this));
            Item(m, "Close tab", "Ctrl+W", () => CloseTab(tab));
            m.Items.Add(new Separator());

            // ---- view
            var view = Sub("View");
            Item(view, "File explorer", "Ctrl+B", ToggleExplorer, ExplorerVisible);
            Item(view, "Comments panel", null, () => Workspace.SetCommentPanel(!Comments.MarginMode), Comments.MarginMode);
            Item(view, "Word wrap", "Alt+Z", Workspace.ToggleWrap, st.WordWrap);
            view.Items.Add(new Separator());
            Item(view, "Split right", "Ctrl+\\", () => SplitActive(g, Dock.Right));
            Item(view, "Split down", "Ctrl+Shift+\\", () => SplitActive(g, Dock.Bottom));
            Item(view, "Move tab to new window", "Ctrl+Shift+M", () => MoveToNewWindow(tab));
            view.Items.Add(new Separator());
            Item(view, "Zoom in", "Ctrl+=", () => Workspace.Zoom(+1));
            Item(view, "Zoom out", "Ctrl+-", () => Workspace.Zoom(-1));
            Item(view, "Reset zoom", "Ctrl+0", () => Workspace.Zoom(0));
            if (Explorer.Root != null) { view.Items.Add(new Separator()); Item(view, "Close folder", null, Explorer.CloseFolder); }

            // ---- appearance
            var look = Sub("Appearance");
            Item(look, "Dark theme", null, () => { if (Theme.IsLight) Theme.Switch(false); }, !Theme.IsLight);
            Item(look, "Light theme", null, () => { if (!Theme.IsLight) Theme.Switch(true); }, Theme.IsLight);
            look.Items.Add(new Separator());
            var code = new MenuItem { Header = "Code colours", InputGestureText = Code.Current.Name };
            look.Items.Add(code);
            var currentCode = st.CodeTheme ?? "auto";
            Item(code, "Auto (One Dark / GitHub Light)", null, () => Code.SetTheme("auto"), string.Equals(currentCode, "auto", StringComparison.OrdinalIgnoreCase));
            code.Items.Add(new Separator());
            foreach (var t in Code.Themes)
            {
                var name = t.Name;
                Item(code, name + (t.Light ? "  (light)" : ""), null, () => Code.SetTheme(name), string.Equals(currentCode, name, StringComparison.OrdinalIgnoreCase));
            }

            // ---- settings
            var set = Sub("Settings");
            Item(set, "Auto save", null, () =>
            {
                st.AutoSave = !st.AutoSave;
                st.Save();
                if (st.AutoSave) foreach (var d in Workspace.Docs.Where(d => d.Dirty && d.Path != null).ToList()) d.Save(this, false);
                Toast(st.AutoSave ? "Auto save on" : "Auto save off — Ctrl+S to save");
            }, st.AutoSave);
            Item(set, "Install updates automatically", null, () => { st.AutoUpdate = st.AutoUpdate == "auto" ? "ask" : "auto"; st.Save(); }, st.AutoUpdate == "auto");
            Item(set, "Explorer right-click menu", null, () => { if (Shell.IsRegistered) Shell.Unregister(true); else Shell.Register(true); }, Shell.IsRegistered);
            Item(set, "Show welcome page at startup", null, () => { st.ShowWelcome = !st.ShowWelcome; st.Save(); }, st.ShowWelcome);
            set.Items.Add(new Separator());
            Item(set, "Open settings file…", null, () =>
            {
                st.Save();
                OpenFile(System.IO.Path.Combine(st.Dir, "TaskPad.ini"));
            });
            Item(m, "Reveal file in Explorer", null, () => Process.Start("explorer.exe", $"/select,\"{tab.Doc.Path}\"")).IsEnabled = tab?.Doc.Path != null;
            m.Items.Add(new Separator());

            // ---- help
            var help = Sub("Help");
            Item(help, "Welcome", null, ShowWelcome);
            Item(help, "Take the tour", null, OpenTour);
            Item(help, "Keywords", "Ctrl+K", () => KeywordsPopup.Show(anchor, () => g.Active?.Editor, ShowCheatSheet));
            Item(help, "Cheat sheet", "F1", ShowCheatSheet);
            help.Items.Add(new Separator());
            Item(help, "Check for updates", "v" + Updater.Short(Updater.Current), () => Updater.Check(silent: false));
            Item(help, "TaskPad on GitHub", null, () => { try { Process.Start(new ProcessStartInfo("https://github.com/ddatunashvili/TaskPad") { UseShellExecute = true }); } catch { } });
            m.IsOpen = true;
        }

        void ShowCodeThemes(FrameworkElement anchor)
        {
            var m = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            var current = Workspace.Settings.CodeTheme ?? "auto";
            void Add(string label, string value)
            {
                var mi = new MenuItem { Header = label, IsCheckable = true, IsChecked = string.Equals(current, value, StringComparison.OrdinalIgnoreCase) };
                mi.Click += (s, e) => Code.SetTheme(value);
                m.Items.Add(mi);
            }
            Add("Auto (One Dark / GitHub Light)", "auto");
            m.Items.Add(new Separator());
            foreach (var t in Code.Themes) Add(t.Name + (t.Light ? "  (light)" : ""), t.Name);
            m.IsOpen = true;
        }

        void NewWindow()
        {
            var w = new TaskWindow(false) { Left = Left + 40, Top = Top + 40, Width = ActualWidth, Height = ActualHeight };
            w.NewTab();
            w.Show();
        }

        void OpenDialog()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Notes (*.txt;*.task;*.md;*.todo;*.log)|*.txt;*.task;*.md;*.todo;*.log|TaskPad notes with images (*.task)|*.task|All files (*.*)|*.*",
                Multiselect = true,
            };
            if (ActiveTab?.Doc.Path != null) dlg.InitialDirectory = Path.GetDirectoryName(ActiveTab.Doc.Path);
            if (dlg.ShowDialog(this) == true)
                foreach (var f in dlg.FileNames) OpenFile(f);
        }

        // ---------------- status ----------------

        public void UpdateTitle()
        {
            var t = ActiveTab;
            Title = t == null ? "TaskPad" : (t.Doc.Dirty ? "● " : "") + t.Doc.Name + " — TaskPad";
        }

        public void ScheduleStats()
        {
            _statsTimer.Stop();
            _statsTimer.Start();
        }

        public void UpdateStatus()
        {
            UpdateTitle();
            var t = ActiveTab;
            if (t == null) { _stPos.Text = _stInfo.Text = ""; return; }
            var c = t.Editor.TextArea.Caret;
            var sel = t.Editor.SelectionLength;
            _stPos.Text = $"Ln {c.Line}, Col {c.Column}" + (sel > 0 ? $"  ({sel} selected)" : "");
            var doc = t.Doc.Document;
            string eol = doc.LineCount > 1 ? (doc.GetLineByNumber(1).DelimiterLength == 2 ? "CRLF" : "LF") : "CRLF";
            string enc = t.Doc.Encoding is UTF8Encoding u ? (u.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8") : t.Doc.Encoding.WebName.ToUpperInvariant();
            _stInfo.Text = $"{enc}    {eol}    {Math.Round(Workspace.Settings.FontSize / 16 * 100)}%";
        }

        public void UpdateStats()
        {
            var t = ActiveTab;
            int total = 0, done = 0;
            if (t != null)
            {
                var doc = t.Doc.Document;
                foreach (var line in doc.Lines)
                {
                    if (line.Length < 3) continue;
                    var info = LineParser.Parse(doc.GetText(line));
                    if (info.Check == Check.None || info.Check == Check.Cancelled) continue;
                    total++;
                    if (info.Check == Check.Done) done++;
                }
            }
            _progressTrack.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
            _progressFill.Width = total > 0 ? 70.0 * done / total : 0;
            _stTasks.Text = total > 0 ? $"{done}/{total} done" + (done == total ? "  ✓" : "") : "";
            _stTasks.Foreground = total > 0 && done == total ? Theme.Slash : Theme.FgDim;
            int overdue = t == null ? 0 : Due.Scan(t.Doc.Document).Count(i => !i.Done && i.When < DateTime.Now);
            if (overdue > 0)
                _stTasks.Inlines.Add(new System.Windows.Documents.Run((total > 0 ? "   ·   " : "") + $"⏰ {overdue} overdue") { Foreground = Theme.Bang });
        }

        // ---------------- keys / lifetime ----------------

        void OnWindowKey(object sender, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool ctrl = mods == ModifierKeys.Control, ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);
            var g = ActiveGroup;
            bool handled = true;
            if (ctrl && key == Key.N) NewTab();
            else if (ctrlShift && key == Key.N) NewWindow();
            else if (ctrl && key == Key.O) OpenDialog();
            else if (ctrl && key == Key.S) ActiveTab?.Doc.Save(this, false);
            else if (ctrlShift && key == Key.S) ActiveTab?.Doc.Save(this, true);
            else if (ctrl && (key == Key.W || key == Key.F4)) CloseTab(ActiveTab);
            else if (ctrl && (key == Key.Tab || key == Key.PageDown)) CycleTab(+1);
            else if (ctrlShift && key == Key.Tab || ctrl && key == Key.PageUp) CycleTab(-1);
            else if (ctrl && key == Key.Oem5) SplitActive(g, Dock.Right);
            else if (ctrlShift && key == Key.Oem5) SplitActive(g, Dock.Bottom);
            else if (ctrlShift && key == Key.M) MoveToNewWindow(ActiveTab);
            else if (ctrlShift && key == Key.T) Recent.ReopenLast(this);
            else if (ctrl && key == Key.B) ToggleExplorer();
            else if (ctrlShift && key == Key.O) { SetExplorerVisible(true); Explorer.PickFolder(); }
            else if (ctrl && (key == Key.OemPlus || key == Key.Add)) Workspace.Zoom(+1);
            else if (ctrl && (key == Key.OemMinus || key == Key.Subtract)) Workspace.Zoom(-1);
            else if (ctrl && (key == Key.D0 || key == Key.NumPad0)) Workspace.Zoom(0);
            else if (mods == ModifierKeys.Alt && key == Key.Z) Workspace.ToggleWrap();
            else if (mods == ModifierKeys.None && key == Key.F1) ShowCheatSheet();
            else if (ctrl && key == Key.P) PdfExport.Run(this, ActiveTab?.Doc);
            else if (ctrl && key == Key.K && g != null) KeywordsPopup.Show(g.Bar, () => g.Active?.Editor, ShowCheatSheet);
            else if (ctrl && key >= Key.D1 && key <= Key.D9 && g != null && key - Key.D1 < g.Tabs.Count) g.Activate(g.Tabs[key - Key.D1]);
            else handled = false;
            if (!handled && key == Key.Escape && WelcomeVisible) { HideWelcome(); handled = true; }
            if (handled) e.Handled = true;
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var tabs = Groups.SelectMany(g => g.Tabs).ToList();
            // Quitting (last window, or an update restart): files are saved, unsaved/untitled text is kept
            // in the session backup and comes back next start — no prompts. Closing one of several windows still asks.
            bool hotExit = Session.Enabled && (Session.Quitting || Workspace.Windows.Count == 1);
            foreach (var t in tabs)
            {
                if (!t.Doc.Views.All(v => v.Group?.Owner == this)) continue;
                // with auto save off, unsaved edits stay unsaved (kept in the session backup)
                if (hotExit) { if (t.Doc.Path != null && t.Doc.Dirty && Workspace.Settings.AutoSave) t.Doc.Save(this, false); }
                else if (!t.Doc.ConfirmClose(this)) { e.Cancel = true; return; }
            }
            if (hotExit && !Session.Quitting && !Session.Restoring) Session.Save();
            if (!hotExit) foreach (var t in tabs) if (t.Doc.Views.Count == 1) Recent.Add(t.Doc.Path);
            foreach (var t in tabs) t.Detach();

            var st = Workspace.Settings;
            st.Maximized = WindowState == WindowState.Maximized;
            var rb = RestoreBounds;
            if (!rb.IsEmpty)
            {
                st.Left = rb.Left; st.Top = rb.Top;
                st.Width = rb.Width; st.Height = rb.Height;
            }
            st.Save();
        }

        static bool OnScreen(double left, double top) =>
            left >= SystemParameters.VirtualScreenLeft - 50 && top >= SystemParameters.VirtualScreenTop - 50 &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void DarkTitleBar() => ApplyDarkTitleBar(this);

        public static void ApplyDarkTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                int on = Theme.IsLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, 20, ref on, 4);          // immersive dark mode (off in light theme)
                var c = Theme.Chrome.Color;
                int bgr = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(hwnd, 35, ref bgr, 4);         // caption color (Win11)
                var b = Theme.ChromeBorder.Color;
                int border = b.R | (b.G << 8) | (b.B << 16);
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);      // border color (Win11)
            }
            catch { }
        }

        const string CheatSheet =
@"# TaskPad cheat sheet
Type these at the start of a line:

[ ] open task  (type [] for one, click the box to toggle)
[x] done task
[/] in progress  (Ctrl+click a box)
[-] cancelled  (Shift+click a box)
- [ ] markdown style task works too

- bullet
* starred / important
> next up / forwarded
< waiting on someone
! urgent / warning
? question / idea
/ finished note
TODO: something to do
// ! better-comments style prefix works too

## Heading 2
### Heading 3
Section name:
---
===

Inline  @person  #tag  2026-10-04 14:30  `code`  https://example.com (Ctrl+click)

## Keys
1. Enter            continue task / bullet / numbered list
2. Enter twice      end the list
3. Ctrl+Enter       toggle task on line(s), or make the line a task
4. Tab / Shift+Tab  indent / outdent list item
5. Alt+Up/Down      move line
6. Shift+Alt+Down   duplicate line
7. Ctrl+Shift+K     delete line
8. Ctrl+F / Ctrl+H  find / replace
9. F5 / Ctrl+;      insert date-time / date
10. Ctrl+K          keywords picker
11. Ctrl+\          split right (drag a tab to an edge to split)
12. Ctrl+Shift+M    move tab to new window (or drag it out)
13. Ctrl+wheel      zoom
14. Ctrl+M          comment on selected words (hover to read, click bubble to edit)
15. Ctrl+V          paste an image (saved to images/ next to the file)
16. Ctrl+P          export to PDF
17. due:+3d         deadline with live countdown (also due:2026-10-10 18:00, due:friday, due:+2h, due:+1w, due:+1mo)
18. Ctrl+click      copy a colour code like #22C55E or a link

# Heading 1
## Heading 2
### Heading 3
#### Heading 4
##### Heading 5
###### Heading 6

## Subtasks
[ ] Parent task
  [ ] subtask gets a smaller box (Tab on a line under a task)

## Select several lines
Select lines and a toolbar appears: tasks, !, ?, *, >, <, /, -, 1., headings or clear.

## Comments
Highlight {==this text==}{>>comments are stored as CriticMarkup<<} and keep the file plain.
";
    }
}
