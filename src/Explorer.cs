using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskPad
{
    /// VS Code-style folder explorer in the left sidebar: lazy tree, file-type icons, auto refresh,
    /// and New File / New Folder / Rename / Delete / Reveal / Copy Path on right-click.
    public sealed class ExplorerPanel : DockPanel
    {
        public sealed class Node
        {
            public string Path { get; set; }
            public string Name { get; set; }
            public bool IsDir { get; set; }
            public int Depth { get; set; }
            public bool Expanded { get; set; }
            public Thickness Indent => new Thickness(6 + Depth * 14, 0, 0, 0);
            public string Chevron => IsDir ? (Expanded ? "" : "") : "";
            public string Icon { get; set; }
            public Brush IconBrush { get; set; }
            public FontFamily IconFont { get; set; }
            public double IconSize { get; set; }
            public FontWeight IconWeight { get; set; }
        }

        readonly TaskWindow _owner;
        AgendaPanel _agenda;
        Grid _filesView;
        FrameworkElement _actions;
        TextBlock _tabFiles, _tabAgenda;
        public bool AgendaShown => _agenda != null && _agenda.Visibility == Visibility.Visible;
        readonly ObservableCollection<Node> _items = new ObservableCollection<Node>();
        readonly ListBox _list;
        readonly TextBlock _title = new TextBlock();
        readonly StackPanel _empty;
        readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FileSystemWatcher _watcher;
        readonly DispatcherTimer _refresh;
        string _root;
        Border _closeButton;

        /// "Close Folder": back to the empty state; nothing on disk changes.
        public void CloseFolder()
        {
            Workspace.Settings.ExplorerFolder = "";
            Workspace.Settings.Save();
            Open(null);
        }

        static readonly FontFamily Mdl2 = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public string Root => _root;

        public ExplorerPanel(TaskWindow owner)
        {
            _owner = owner;
            Background = Theme.Chrome;
            Width = Math.Max(160, Workspace.Settings.ExplorerWidth);

            // header: folder name + actions
            var head = new DockPanel { Height = 36, Margin = new Thickness(12, 0, 4, 0), LastChildFill = true };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(HeadButton("", "New file", () => NewItem(false, Target())));
            actions.Children.Add(HeadButton("", "New folder", () => NewItem(true, Target())));
            actions.Children.Add(HeadButton("", "Refresh", Refresh));
            actions.Children.Add(HeadButton("", "Collapse all", () => { _expanded.Clear(); Rebuild(); }));
            actions.Children.Add(HeadButton("", "Open another folder…", PickFolder));
            _closeButton = HeadButton("", "Close folder", CloseFolder);
            actions.Children.Add(_closeButton);
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
            _title.Foreground = Theme.FgDim;
            _title.FontFamily = new FontFamily("Segoe UI");
            _title.FontSize = 11;
            _title.FontWeight = FontWeights.SemiBold;
            _title.VerticalAlignment = VerticalAlignment.Center;
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            head.Children.Add(_title);
            DockPanel.SetDock(head, Dock.Top);
            _actions = head;   // the whole header row hides in agenda mode

            // Files | Agenda switch
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 6, 0, 0) };
            _tabFiles = TabLabel("FILES", () => ShowAgenda(false));
            _tabAgenda = TabLabel("AGENDA", () => ShowAgenda(true));
            tabs.Children.Add(_tabFiles);
            tabs.Children.Add(_tabAgenda);
            DockPanel.SetDock(tabs, Dock.Top);
            Children.Add(tabs);
            Children.Add(head);

            // empty state
            _empty = new StackPanel { Margin = new Thickness(14, 10, 14, 0) };
            _empty.Children.Add(new TextBlock { Text = "No folder open", Foreground = Theme.FgDim, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5, Margin = new Thickness(0, 0, 0, 10) });
            var open = new Border
            {
                Background = Theme.Accent, CornerRadius = new CornerRadius(5), Padding = new Thickness(12, 6, 12, 7), Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = "Open Folder", Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5 },
            };
            open.MouseLeftButtonUp += (s, e) => PickFolder();
            _empty.Children.Add(open);

            _list = new ListBox
            {
                ItemsSource = _items,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplate),
                FocusVisualStyle = null,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            ApplyItemStyle();
            _list.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (ItemAt(e.OriginalSource as DependencyObject) is Node n) Activate(n, Keyboard.Modifiers == ModifierKeys.Control);
            };
            _list.PreviewMouseRightButtonDown += (s, e) =>
            {
                var n = ItemAt(e.OriginalSource as DependencyObject);
                if (n != null) _list.SelectedItem = n;
            };
            _list.MouseRightButtonUp += (s, e) => { ShowMenu(ItemAt(e.OriginalSource as DependencyObject)); e.Handled = true; };
            _list.KeyDown += OnKey;
            _filesView = new Grid { Children = { _list, _empty } };
            _agenda = new AgendaPanel(_owner) { Visibility = Visibility.Collapsed };
            Children.Add(new Grid { Children = { _filesView, _agenda } });
            UpdateTabs();

            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _refresh.Tick += (s, e) => { _refresh.Stop(); Refresh(); };

            Open(Workspace.Settings.ExplorerFolder);
        }

        const string RowTemplate = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
  <StackPanel Orientation='Horizontal' Height='24' Margin='{Binding Indent}' Background='Transparent'>
    <TextBlock Text='{Binding Chevron}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='9' Width='14' VerticalAlignment='Center' Opacity='0.7'/>
    <TextBlock Text='{Binding Icon}' Foreground='{Binding IconBrush}' FontFamily='{Binding IconFont}' FontSize='{Binding IconSize}' FontWeight='{Binding IconWeight}'
               Width='20' TextAlignment='Center' VerticalAlignment='Center' Margin='0,0,6,0'/>
    <TextBlock Text='{Binding Name}' FontFamily='Segoe UI' FontSize='13' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/>
  </StackPanel>
</DataTemplate>";

        /// Row style with theme colours (rebuilt when the theme changes).
        public void ApplyItemStyle()
        {
            string H(SolidColorBrush b) => b.Color.ToString();
            _list.ItemContainerStyle = (Style)XamlReader.Parse($@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'>
  <Setter Property='Foreground' Value='{H(Theme.Fg)}'/>
  <Setter Property='Padding' Value='0'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='ListBoxItem'>
      <Border x:Name='Bd' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='Transparent' Margin='4,0,4,0' CornerRadius='4'>
        <ContentPresenter/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{H(Theme.Hover)}'/></Trigger>
        <Trigger Property='IsSelected' Value='True'><Setter TargetName='Bd' Property='Background' Value='{H(Theme.AccentSoft)}'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");
            _title.Foreground = Theme.FgDim;
            if (_tabFiles != null && _filesView != null) UpdateTabs();
            Rebuild();
        }

        TextBlock TabLabel(string text, Action click)
        {
            var t = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 16, 0), Cursor = Cursors.Hand, Padding = new Thickness(0, 2, 0, 4) };
            t.MouseLeftButtonUp += (s, e) => click();
            return t;
        }

        void UpdateTabs()
        {
            bool a = AgendaShown;
            _tabFiles.Foreground = a ? Theme.FgDim : Theme.Fg;
            _tabAgenda.Foreground = a ? Theme.Fg : Theme.FgDim;
            _tabFiles.TextDecorations = a ? null : TextDecorations.Underline;
            _tabAgenda.TextDecorations = a ? TextDecorations.Underline : null;
            _filesView.Visibility = a ? Visibility.Collapsed : Visibility.Visible;
            _actions.Visibility = a ? Visibility.Collapsed : Visibility.Visible;
            _title.Visibility = a ? Visibility.Collapsed : Visibility.Visible;
        }

        public void ShowAgenda(bool on)
        {
            _agenda.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            UpdateTabs();
            if (on) _agenda.Refresh();
        }

        public void PokeAgenda() => _agenda?.Poke();

        static Node ItemAt(DependencyObject d)
        {
            for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is ListBoxItem li) return li.DataContext as Node;
            return null;
        }

        static Border HeadButton(string glyph, string tip, Action click)
        {
            var tb = new TextBlock { Text = glyph, FontFamily = Mdl2, FontSize = 13, Foreground = Theme.FgDim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Child = tb, ToolTip = tip, Cursor = Cursors.Hand };
            b.MouseEnter += (s, e) => { b.Background = Theme.Hover; tb.Foreground = Theme.Fg; };
            b.MouseLeave += (s, e) => { b.Background = Brushes.Transparent; tb.Foreground = Theme.FgDim; };
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        // ---------------- folder ----------------

        public void PickFolder()
        {
            // folder picker via the file dialog trick (works on .NET Framework without WinForms)
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open Folder — pick any file, or type a folder path and press Open",
                CheckFileExists = false,
                ValidateNames = false,
                FileName = "Select this folder",
                InitialDirectory = _root ?? (_owner.ActiveTab?.Doc.Path != null ? Path.GetDirectoryName(_owner.ActiveTab.Doc.Path) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            };
            if (dlg.ShowDialog(_owner) != true) return;
            var dir = Directory.Exists(dlg.FileName) ? dlg.FileName : Path.GetDirectoryName(dlg.FileName);
            Open(dir);
        }

        public void Open(string dir)
        {
            _watcher?.Dispose();
            _watcher = null;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                _root = null;
                _items.Clear();
                _title.Text = "EXPLORER";
                _title.ToolTip = null;
                _empty.Visibility = Visibility.Visible;
                if (_closeButton != null) _closeButton.Visibility = Visibility.Collapsed;
                return;
            }
            _root = Path.GetFullPath(dir).TrimEnd('\\');
            if (_root.EndsWith(":")) _root += "\\";
            Workspace.Settings.ExplorerFolder = _root;
            Workspace.Settings.Save();
            _title.Text = (Path.GetFileName(_root.TrimEnd('\\')) is string n && n.Length > 0 ? n : _root).ToUpperInvariant();
            _title.ToolTip = _root;
            _empty.Visibility = Visibility.Collapsed;
            if (_closeButton != null) _closeButton.Visibility = Visibility.Visible;
            _expanded.Clear();
            Rebuild();
            try
            {
                _watcher = new FileSystemWatcher(_root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                FileSystemEventHandler h = (s, e) => Dispatcher.BeginInvoke(new Action(() => { _refresh.Stop(); _refresh.Start(); PokeAgenda(); }));
                _watcher.Created += h;
                _watcher.Deleted += h;
                _watcher.Renamed += (s, e) => Dispatcher.BeginInvoke(new Action(() => { _refresh.Stop(); _refresh.Start(); }));
                _watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        public void Refresh() => Rebuild();

        void Rebuild()
        {
            if (_root == null) return;
            var selected = (_list.SelectedItem as Node)?.Path;
            _items.Clear();
            AddChildren(_root, 0);
            if (selected != null) _list.SelectedItem = _items.FirstOrDefault(n => string.Equals(n.Path, selected, StringComparison.OrdinalIgnoreCase));
        }

        void AddChildren(string dir, int depth)
        {
            IEnumerable<string> dirs, files;
            try
            {
                dirs = Directory.GetDirectories(dir).Where(Visible).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
                files = Directory.GetFiles(dir).Where(Visible).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { return; }
            foreach (var d in dirs)
            {
                bool open = _expanded.Contains(d);
                _items.Add(MakeNode(d, true, depth, open));
                if (open) AddChildren(d, depth + 1);
            }
            foreach (var f in files) _items.Add(MakeNode(f, false, depth, false));
        }

        static bool Visible(string p)
        {
            var name = Path.GetFileName(p);
            if (name.StartsWith(".") && name != ".gitignore") return false;
            try { return (File.GetAttributes(p) & (FileAttributes.Hidden | FileAttributes.System)) == 0; } catch { return false; }
        }

        void Toggle(Node n)
        {
            if (!n.IsDir) return;
            if (!_expanded.Remove(n.Path)) _expanded.Add(n.Path);
            Rebuild();
        }

        void Activate(Node n, bool ctrl)
        {
            if (n.IsDir) { Toggle(n); return; }
            OpenFile(n.Path);
        }

        void OpenFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (Images.IsImageFile(path))
            {
                var folder = Directory.GetFiles(Path.GetDirectoryName(path)).Where(Images.IsImageFile).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
                ImageViewer.Show(path, folder);
                return;
            }
            if (ext == ".pdf" || ext == ".exe" || ext == ".lnk" || (Doc.LooksBinary(path) && !TaskFile.Is(path)))
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { _owner.Toast(ex.Message); }
                return;
            }
            _owner.OpenFile(path);
        }

        /// Folder for "new" actions: the selected folder, the selected file's folder, or the root.
        string Target()
        {
            var n = _list.SelectedItem as Node;
            if (n == null) return _root;
            return n.IsDir ? n.Path : Path.GetDirectoryName(n.Path);
        }

        // ---------------- right-click ----------------

        void ShowMenu(Node n)
        {
            if (_root == null) { PickFolder(); return; }
            var m = new ContextMenu { PlacementTarget = _list };
            MenuItem Item(string header, string gesture, Action a)
            {
                var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
                mi.Click += (s, e) => a();
                m.Items.Add(mi);
                return mi;
            }
            string folder = n == null ? _root : n.IsDir ? n.Path : Path.GetDirectoryName(n.Path);
            Item("New File…", null, () => NewItem(false, folder));
            Item("New Folder…", null, () => NewItem(true, folder));
            if (n != null)
            {
                m.Items.Add(new Separator());
                if (!n.IsDir) Item("Open", "Enter", () => OpenFile(n.Path));
                Item("Reveal in File Explorer", null, () => Process.Start("explorer.exe", $"/select,\"{n.Path}\""));
                Item("Copy Path", null, () => Clipboard.SetText(n.Path));
                Item("Copy Relative Path", null, () => Clipboard.SetText(Relative(n.Path)));
                m.Items.Add(new Separator());
                Item("Rename…", "F2", () => Rename(n));
                Item("Delete", "Del", () => Delete(n));
            }
            m.Items.Add(new Separator());
            Item("Refresh", null, Refresh);
            Item("Open Folder…", null, PickFolder);
            Item("Close Folder", null, CloseFolder);
            m.IsOpen = true;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            var n = _list.SelectedItem as Node;
            if (n == null) return;
            if (e.Key == Key.F2) { Rename(n); e.Handled = true; }
            else if (e.Key == Key.Delete) { Delete(n); e.Handled = true; }
            else if (e.Key == Key.Enter) { Activate(n, false); e.Handled = true; }
            else if (e.Key == Key.Right && n.IsDir && !n.Expanded) { Toggle(n); e.Handled = true; }
            else if (e.Key == Key.Left && n.IsDir && n.Expanded) { Toggle(n); e.Handled = true; }
        }

        string Relative(string p) => p.StartsWith(_root, StringComparison.OrdinalIgnoreCase) ? p.Substring(_root.Length).TrimStart('\\') : p;

        void NewItem(bool folder, string dir)
        {
            if (dir == null) return;
            Prompt(folder ? "New folder name" : "New file name  (no extension = .task)", "", name =>
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException("A file or folder with that name already exists.");
                if (folder) Directory.CreateDirectory(path);
                else
                {
                    if (Path.GetExtension(path).Length == 0) path += TaskFile.Ext;
                    if (TaskFile.Is(path)) TaskFile.CreateNew(path, "");
                    else File.WriteAllText(path, "");
                }
                _expanded.Add(dir);
                Rebuild();
                _list.SelectedItem = _items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
                if (!folder) OpenFile(path);
            });
        }

        void Rename(Node n)
        {
            Prompt("Rename", n.Name, name =>
            {
                if (name == n.Name) return;
                var target = Path.Combine(Path.GetDirectoryName(n.Path), name);
                if (n.IsDir) Directory.Move(n.Path, target);
                else File.Move(n.Path, target);
                // keep open tabs pointing at the renamed file
                foreach (var d in Workspace.Docs.Where(d => d.Path != null))
                {
                    if (string.Equals(d.Path, n.Path, StringComparison.OrdinalIgnoreCase)) d.Path = target;
                    else if (n.IsDir && d.Path.StartsWith(n.Path + "\\", StringComparison.OrdinalIgnoreCase)) d.Path = target + d.Path.Substring(n.Path.Length);
                    else continue;
                    d.Document.FileName = d.Path;
                    d.Raise();
                }
                if (_expanded.Remove(n.Path)) _expanded.Add(target);
                Rebuild();
            });
        }

        void Delete(Node n)
        {
            var open = Workspace.Docs.Any(d => d.Path != null && (string.Equals(d.Path, n.Path, StringComparison.OrdinalIgnoreCase) || n.IsDir && d.Path.StartsWith(n.Path + "\\", StringComparison.OrdinalIgnoreCase)));
            if (MessageBox.Show(_owner, $"Move \"{n.Name}\" to the Recycle Bin?" + (open ? "\n\nIt is open in TaskPad; the tab stays open until you close it." : ""),
                    "Delete", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                if (n.IsDir) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(n.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(n.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex) { _owner.Toast("Delete failed: " + ex.Message); }
            Rebuild();
        }

        /// Small inline input box under the header (VS Code-like).
        void Prompt(string title, string initial, Action<string> commit)
        {
            var box = new TextBox
            {
                Text = initial, Background = Theme.Input, Foreground = Theme.Fg, CaretBrush = Theme.Accent, SelectionBrush = Theme.Accent,
                BorderBrush = Theme.Accent, BorderThickness = new Thickness(1), Padding = new Thickness(4, 3, 4, 3),
                FontFamily = new FontFamily("Segoe UI"), FontSize = 13,
            };
            var hint = new TextBlock { Text = title + "  ·  Enter / Esc", Foreground = Theme.FgDim, FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
            var err = new TextBlock { Foreground = Theme.Bang, FontFamily = new FontFamily("Segoe UI"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            var stack = new StackPanel();
            stack.Children.Add(hint);
            stack.Children.Add(box);
            stack.Children.Add(err);
            var popup = new Popup
            {
                PlacementTarget = this, Placement = PlacementMode.Relative, HorizontalOffset = 6, VerticalOffset = 38,
                StaysOpen = false, AllowsTransparency = true,
                Child = new Border { Width = ActualWidth - 12, Background = Theme.Popup, BorderBrush = Theme.ChromeBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Child = stack },
            };
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    var name = box.Text.Trim();
                    if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    { err.Text = "That name isn't allowed."; err.Visibility = Visibility.Visible; return; }
                    try { commit(name); popup.IsOpen = false; }
                    catch (Exception ex) { err.Text = ex.Message; err.Visibility = Visibility.Visible; }
                }
            };
            popup.IsOpen = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                box.Focus();
                Keyboard.Focus(box);
                // select the name without its extension, like VS Code
                int dot = box.Text.LastIndexOf('.');
                if (dot > 0) box.Select(0, dot); else box.SelectAll();
            }), DispatcherPriority.Input);
        }

        // ---------------- icons ----------------

        Node MakeNode(string path, bool dir, int depth, bool expanded)
        {
            var n = new Node { Path = path, Name = Path.GetFileName(path), IsDir = dir, Depth = depth, Expanded = expanded, IconFont = Mdl2, IconSize = 14, IconWeight = FontWeights.Normal };
            if (dir)
            {
                n.Icon = expanded ? "" : "";
                n.IconBrush = Brush("#DCB67A");
                return n;
            }
            var ext = Path.GetExtension(path).ToLowerInvariant();
            void Badge(string text, string color, double size = 9.5) { n.Icon = text; n.IconBrush = Brush(color); n.IconFont = new FontFamily("Segoe UI"); n.IconSize = size; n.IconWeight = FontWeights.Bold; }
            void Glyph(string g, string color) { n.Icon = g; n.IconBrush = Brush(color); }
            switch (ext)
            {
                case ".task": n.Icon = ""; n.IconBrush = Theme.Accent; break;              // checkbox
                case ".txt": case ".log": case ".todo": Glyph("", Theme.IsLight ? "#6E6E7A" : "#A0A0AA"); break;
                case ".md": case ".markdown": Badge("M↓", "#519ABA", 9); break;
                case ".pdf": Badge("PDF", "#E5484D", 8); break;
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp": case ".webp": case ".ico": case ".svg": Glyph("", "#A074C4"); break;
                case ".js": case ".mjs": case ".cjs": Badge("JS", "#E8C34A"); break;
                case ".jsx": case ".tsx": Badge("⚛", "#4FC1E9", 12); break;
                case ".ts": Badge("TS", "#3B82F6"); break;
                case ".cs": Badge("C#", "#68A063"); break;
                case ".py": Badge("PY", "#4B8BBE"); break;
                case ".java": Badge("JV", "#E76F00"); break;
                case ".go": Badge("GO", "#29BEB0"); break;
                case ".rs": Badge("RS", "#DEA584"); break;
                case ".cpp": case ".cc": case ".c": case ".h": case ".hpp": Badge("C", "#659AD2"); break;
                case ".json": Badge("{ }", "#E8C34A", 9); break;
                case ".html": case ".htm": Badge("<>", "#E34C26", 10); break;
                case ".xml": case ".xaml": case ".csproj": case ".config": Badge("<>", "#F1A33F", 10); break;
                case ".css": case ".scss": case ".less": Badge("#", "#42A5F5", 12); break;
                case ".yml": case ".yaml": case ".toml": case ".ini": Glyph("", "#9AA0A6"); break;   // settings gear
                case ".sh": case ".bat": case ".cmd": case ".ps1": Badge(">_", "#7CB342", 9); break;
                case ".zip": case ".7z": case ".rar": case ".tar": case ".gz": Glyph("", "#D7A54B"); break;
                case ".exe": case ".msi": case ".dll": Glyph("", "#9AA0A6"); break;
                case ".mp3": case ".wav": case ".flac": Glyph("", "#E91E63"); break;
                case ".mp4": case ".mov": case ".mkv": case ".avi": Glyph("", "#FF7043"); break;
                case ".docx": case ".doc": Badge("W", "#2B579A", 11); break;
                case ".xlsx": case ".xls": case ".csv": Badge("X", "#217346", 11); break;
                case ".pptx": Badge("P", "#D24726", 11); break;
                default: Glyph("", Theme.IsLight ? "#8A8A96" : "#7A7A86"); break;      // generic page
            }
            return n;
        }

        static readonly Dictionary<string, SolidColorBrush> BrushCache = new Dictionary<string, SolidColorBrush>();
        static Brush Brush(string hex)
        {
            if (!BrushCache.TryGetValue(hex, out var b)) { b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); BrushCache[hex] = b; }
            return b;
        }
    }
}
