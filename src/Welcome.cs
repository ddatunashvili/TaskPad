using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskPad
{
    /// First-run welcome page (VS Code style): start actions, recent files, the tour, look & feel.
    /// Shown over the editor area; ⋯ → Welcome brings it back.
    public sealed class WelcomePage : Border
    {
        readonly TaskWindow _w;

        public WelcomePage(TaskWindow w)
        {
            _w = w;
            Build();
        }

        public void Build()
        {
            Background = Theme.Bg;
            var root = new Grid();
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Body() };
            root.Children.Add(scroll);

            var close = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 18, 0),
                ToolTip = "Close (Esc)",
                Child = new TextBlock { Text = "✕", Foreground = Theme.FgDim, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            close.MouseEnter += (s, e) => close.Background = Theme.Hover;
            close.MouseLeave += (s, e) => close.Background = Brushes.Transparent;
            close.MouseLeftButtonUp += (s, e) => _w.HideWelcome();
            root.Children.Add(close);
            Child = root;
        }

        UIElement Body()
        {
            var page = new StackPanel { MaxWidth = 900, Margin = new Thickness(48, 40, 48, 32), HorizontalAlignment = HorizontalAlignment.Center };

            // ---- header
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 30) };
            var logo = new Image { Width = 64, Height = 64, Margin = new Thickness(0, 0, 18, 0) };
            try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/TaskPad;component/assets/taskpad.png")); } catch { }
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            head.Children.Add(logo);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "Welcome to TaskPad", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Theme.Fg, FontFamily = Ui });
            titles.Children.Add(new TextBlock { Text = $"Plain-text task lists, the nice way  ·  v{Updater.Short(Updater.Current)}", FontSize = 14, Foreground = Theme.FgDim, FontFamily = Ui, Margin = new Thickness(2, 4, 0, 0) });
            head.Children.Add(titles);
            page.Children.Add(head);

            // ---- two columns
            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            left.Children.Add(Section("Start"));
            left.Children.Add(Action("", "New note", "A .task note — text and images in one file", () => { _w.HideWelcome(); _w.NewTab(); }));
            left.Children.Add(Action("", "Open file…", "Ctrl+O", () => { _w.HideWelcome(); _w.OpenFileDialog(); }));
            left.Children.Add(Action("", "Open folder…", "Browse a project in the sidebar (Ctrl+Shift+O)", () => { _w.HideWelcome(); _w.SetExplorerVisible(true); _w.Explorer.PickFolder(); }));
            if (!Shell.IsRegistered)
                left.Children.Add(Action("", "Add to Explorer right-click menu", "“Open with TaskPad” on text files and folders", () => { Shell.Register(true); Build(); }));

            var recent = Recent.List().Where(File.Exists).Take(6).ToList();
            if (recent.Count > 0)
            {
                left.Children.Add(Section("Recent"));
                foreach (var p in recent)
                {
                    var path = p;
                    left.Children.Add(Link(Path.GetFileName(p), Path.GetDirectoryName(p), () => { _w.HideWelcome(); _w.OpenFile(path); }));
                }
            }
            Grid.SetColumn(left, 0);
            cols.Children.Add(left);

            var right = new StackPanel();
            right.Children.Add(Section("Learn"));
            right.Children.Add(Action("", "Take the 2-minute tour", "An interactive note: tick boxes, try markers, code, comments", () => { _w.HideWelcome(); _w.OpenTour(); }));
            right.Children.Add(Action("", "Keywords", "Every marker you can type (Ctrl+K)", () => { _w.HideWelcome(); _w.ShowKeywords(); }));
            right.Children.Add(Action("", "Cheat sheet", "Syntax and shortcuts (F1)", () => { _w.HideWelcome(); _w.ShowCheatSheet(); }));

            right.Children.Add(Section("Handy keys"));
            foreach (var (k, d) in new[]
            {
                ("[]  then  Enter", "a task, and the next one"),
                ("Ctrl+Enter", "tick / untick the line"),
                ("Ctrl+M", "comment on selected words"),
                ("Ctrl+V", "paste a screenshot into the note"),
                ("Ctrl+B", "file explorer"),
                ("Ctrl+P", "export to PDF"),
            })
                right.Children.Add(KeyRow(k, d));

            right.Children.Add(Section("Look"));
            var look = new WrapPanel();
            look.Children.Add(Chip(Theme.IsLight ? "☾  Dark theme" : "☀  Light theme", () => { Theme.Switch(!Theme.IsLight); }));
            look.Children.Add(Chip("Code colours: " + Code.Current.Name, () => _w.ShowCodeThemesAt(look)));
            right.Children.Add(look);
            Grid.SetColumn(right, 2);
            cols.Children.Add(right);
            page.Children.Add(cols);

            // ---- footer
            var show = new CheckBox
            {
                Content = new TextBlock { Text = "Show this page when TaskPad starts", Foreground = Theme.FgDim, FontFamily = Ui, FontSize = 12.5 },
                IsChecked = Workspace.Settings.ShowWelcome,
                Margin = new Thickness(0, 34, 0, 0),
                Cursor = Cursors.Hand,
            };
            show.Checked += (s, e) => { Workspace.Settings.ShowWelcome = true; Workspace.Settings.Save(); };
            show.Unchecked += (s, e) => { Workspace.Settings.ShowWelcome = false; Workspace.Settings.Save(); };
            page.Children.Add(show);
            return page;
        }

        static readonly FontFamily Ui = new FontFamily("Segoe UI");
        static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        static UIElement Section(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(), Foreground = Theme.FgDim, FontFamily = Ui, FontSize = 11.5, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 22, 0, 8),
        };

        static Border Action(string glyph, string title, string sub, Action click)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 18, Foreground = Theme.Accent, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, Foreground = Theme.Fg, FontFamily = Ui, FontSize = 14.5 });
            text.Children.Add(new TextBlock { Text = sub, Foreground = Theme.FgDim, FontFamily = Ui, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            return Clickable(g, click, new Thickness(12, 9, 12, 9));
        }

        static Border Link(string title, string sub, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = title, Foreground = Theme.Accent, FontFamily = Ui, FontSize = 13.5 });
            sp.Children.Add(new TextBlock { Text = "   " + sub, Foreground = Theme.FgDim, FontFamily = Ui, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            return Clickable(sp, click, new Thickness(12, 5, 12, 5));
        }

        static UIElement KeyRow(string keys, string what)
        {
            var g = new Grid { Margin = new Thickness(12, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new Border
            {
                Background = Theme.Hover, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 3), HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = keys, Foreground = Theme.Fg, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12 },
            });
            var d = new TextBlock { Text = what, Foreground = Theme.FgDim, FontFamily = Ui, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(d, 1);
            g.Children.Add(d);
            return g;
        }

        static Border Chip(string text, Action click)
        {
            var b = Clickable(new TextBlock { Text = text, Foreground = Theme.Fg, FontFamily = Ui, FontSize = 12.5 }, click, new Thickness(12, 6, 12, 7));
            b.BorderBrush = Theme.ChromeBorder;
            b.BorderThickness = new Thickness(1);
            b.Margin = new Thickness(0, 0, 8, 8);
            return b;
        }

        static Border Clickable(UIElement child, Action click, Thickness padding)
        {
            var b = new Border { Child = child, Padding = padding, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            b.MouseEnter += (s, e) => b.Background = Theme.Hover;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        // ---------------- the tour ----------------

        public const string TourText =
@"# Welcome to TaskPad 👋
This is a normal note — edit anything. Nothing here is saved unless you press Ctrl+S.

## 1. Tasks
[ ] Click this box to tick it
[ ] Ctrl+click a box for “in progress”, Shift+click for “cancelled”
[ ] Put the cursor on this line and press Ctrl+Enter
  [ ] Indented tasks become subtasks (Tab under a task)
Type [] at the start of a new line and press Enter twice to see lists continue.

## 2. Markers
! Something urgent
? A question or idea
* Starred / important
> Next up
< Waiting on someone
TODO: book flights
Right-click or double-click any of these icons to change them.

## 3. Deadlines
[ ] Send the report due:+2h
[ ] Renew passport due:+3w
Type due: then +2h, +3d, +1w, +1mo, tomorrow or friday and press space — it becomes a live countdown.

## 4. Notes & sections
Shopping:
- milk
- coffee
Colours get a swatch: #22C55E  · links copy with Ctrl+click: https://github.com/ddatunashvili/TaskPad
---

## 5. Code
```python
def hello(name):
    return f""Hello, {name}!""  # real syntax colours
```

## 6. Comments
Select some words below and press Ctrl+M to comment on them:
The {==launch date==}{>>@taskpad: comments live in the panel on the right — reply there, double-click a message to edit it<<} is next Friday.

## 7. Images
Paste a screenshot with Ctrl+V — it is stored inside a .task file, click it to zoom, crop or flip through.

That's it. Ctrl+K lists every keyword, F1 opens the cheat sheet. Enjoy!
";
    }
}
