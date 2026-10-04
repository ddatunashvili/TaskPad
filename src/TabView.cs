using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Search;

namespace TaskPad
{
    /// A tab: one editor view of a Doc, plus its header in a group's tab strip.
    public sealed class TabView
    {
        public readonly Doc Doc;
        public readonly TextEditor Editor;
        public readonly Grid Root = new Grid();
        public CommentMargin Margin;
        public readonly Border Header;
        public readonly TranslateTransform Shift = new TranslateTransform();
        public EditorGroup Group;

        readonly TextBlock _title, _close, _folder;
        readonly UIElement _buttons;

        public TabView(Doc doc)
        {
            Doc = doc;
            doc.Views.Add(this);
            Editor = CreateEditor(doc);
            var ui = CommentUi.Attach(Editor);
            Margin = new CommentMargin(Editor);
            ui.Margin = Margin;
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(Margin, 1);
            Root.Children.Add(Editor);
            Root.Children.Add(Margin);

            _title = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            _close = new TextBlock
            {
                Width = 18,
                Height = 18,
                TextAlignment = TextAlignment.Center,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = Theme.FgDim,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
            };
            _close.MouseLeftButtonUp += (s, e) => { e.Handled = true; Group?.Owner.CloseTab(this); };
            _close.MouseLeftButtonDown += (s, e) => e.Handled = true;

            _folder = new TextBlock
            {
                Text = "\uE838", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11,
                Width = 18, Height = 18, TextAlignment = TextAlignment.Center, Padding = new Thickness(0, 3, 0, 0),
                Foreground = Theme.FgDim, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                ToolTip = "Open containing folder",
            };
            _folder.MouseLeftButtonDown += (s, e) => e.Handled = true;
            _folder.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (Doc.Path != null) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + Doc.Path + "\"");
            };
            _folder.MouseEnter += (s, e) => _folder.Foreground = Theme.Fg;
            _folder.MouseLeave += (s, e) => _folder.Foreground = Theme.FgDim;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(_folder);
            buttons.Children.Add(_close);
            _buttons = buttons;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(_title);
            sp.Children.Add(buttons);
            Header = new Border
            {
                Child = sp,
                Padding = new Thickness(14, 0, 8, 0),
                BorderThickness = new Thickness(0, 2, 1, 0),
                RenderTransform = Shift,
                Tag = this,
            };
            Header.MouseUp += (s, e) => { if (e.ChangedButton == MouseButton.Middle) Group?.Owner.CloseTab(this); };
            Header.MouseEnter += (s, e) => Refresh();
            Header.MouseLeave += (s, e) => Refresh();
            TabDrag.Attach(this, _buttons);

            doc.Changed += Refresh;
            Editor.TextArea.Caret.PositionChanged += (s, e) => { if (IsFocusedTab) Group.Owner.UpdateStatus(); };
            Editor.Document.TextChanged += (s, e) => { if (IsFocusedTab) Group.Owner.ScheduleStats(); };
        }

        bool IsFocusedTab => Group != null && Group.Active == this && Group.Owner.ActiveGroup == Group;

        public void Detach()
        {
            Doc.Changed -= Refresh;
            Doc.Views.Remove(this);
            if (Doc.Views.Count == 0) Doc.Dispose();
        }

        string _codePath = "\u0000";

        /// Code files (.py, .js, …) get language highlighting; notes get none (their ``` blocks are coloured separately).
        public void ApplyCodeMode()
        {
            _codePath = Doc.Path;
            Editor.SyntaxHighlighting = Code.IsCodeFile(Doc.Path) ? Code.ForFile(Doc.Path) : null;
            Editor.TextArea.TextView.Redraw();
        }

        /// Re-applies editor colours after a theme switch.
        public void ApplyTheme()
        {
            var ta = Editor.TextArea;
            Editor.Background = Theme.F(Theme.Bg);
            Editor.Foreground = Theme.F(Theme.Fg);
            Editor.LineNumbersForeground = Theme.F(Theme.LineNo);
            ta.SelectionBrush = Theme.F(Theme.Selection);
            ta.Caret.CaretBrush = Theme.F(Theme.Accent);
            ta.TextView.CurrentLineBackground = Theme.F(Theme.CurrentLine);
            ta.TextView.LinkTextForegroundBrush = Theme.F(Theme.Link);
            ta.TextView.Redraw();
        }

        public void Refresh()
        {
            if (_codePath != Doc.Path) ApplyCodeMode();   // e.g. after Save As with another extension
            bool active = Group != null && Group.Active == this;
            bool focusedGroup = Group != null && Group.Owner.ActiveGroup == Group;
            _title.Text = Doc.Name;
            _title.Foreground = active ? Theme.Fg : Theme.FgDim;
            Header.Background = active ? Theme.Bg : Theme.Chrome;
            var top = active ? (focusedGroup ? Theme.Accent : Theme.FgFaint) : Theme.ChromeBorder;
            Header.BorderBrush = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(top.Color, 0), new GradientStop(top.Color, 0.06), new GradientStop(Theme.ChromeBorder.Color, 0.06),
            }, 90);
            Header.ToolTip = Doc.Path ?? Doc.Name;
            bool hover = Header.IsMouseOver;
            _close.Text = Doc.Dirty && !hover ? "●" : (hover || active ? "✕" : "");
            _folder.Visibility = Doc.Path != null && (hover || active) ? Visibility.Visible : Visibility.Collapsed;
            _close.FontSize = Doc.Dirty && !hover ? 10 : 12;
            if (active && focusedGroup) Group.Owner.UpdateTitle();
        }

        static TextEditor CreateEditor(Doc doc)
        {
            var st = Workspace.Settings;
            var ed = new TextEditor
            {
                Document = doc.Document,
                FontFamily = new FontFamily(st.FontFamily),
                FontSize = st.FontSize,
                Background = Theme.F(Theme.Bg),
                Foreground = Theme.F(Theme.Fg),
                WordWrap = st.WordWrap,
                ShowLineNumbers = true,
                LineNumbersForeground = Theme.F(Theme.LineNo),
                Padding = new Thickness(6, 8, 6, 0),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
            };
            var o = ed.Options;
            o.ConvertTabsToSpaces = true;
            o.IndentationSize = st.IndentSize;
            o.HighlightCurrentLine = true;
            o.EnableHyperlinks = true;
            o.EnableEmailHyperlinks = false;
            o.RequireControlModifierForHyperlinkClick = true;
            o.AllowScrollBelowDocument = true;
            o.CutCopyWholeLine = true;
            o.EnableRectangularSelection = true;
            o.InheritWordWrapIndentation = true;
            o.WordWrapIndentation = 0;

            var ta = ed.TextArea;
            ta.SelectionBrush = Theme.F(Theme.Selection);
            ta.SelectionForeground = null;
            ta.SelectionBorder = null;
            ta.SelectionCornerRadius = 3;
            ta.Caret.CaretBrush = Theme.F(Theme.Accent);
            ta.TextView.CurrentLineBackground = Theme.F(Theme.CurrentLine);
            ta.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
            ta.TextView.LinkTextForegroundBrush = Theme.F(Theme.Link);
            ta.TextView.LinkTextUnderline = true;

            // softer gutter: hide the dotted separator, add breathing room
            foreach (var m in ta.LeftMargins.OfType<Line>()) m.Stroke = Brushes.Transparent;
            ta.LeftMargins.Add(new Rectangle { Width = st.LeftMargin, Fill = Brushes.Transparent });

            ta.TextView.LineTransformers.Add(new TaskColorizer());
            // double-click an icon: show that line's raw code until the caret leaves it
            int revealLine = -1;
            void Reveal(int lineNo)
            {
                revealLine = lineNo;
                ta.TextView.Redraw(ed.Document.GetLineByNumber(lineNo));
                var line = ed.Document.GetLineByNumber(lineNo);
                var info = LineParser.Parse(ed.Document.GetText(line));
                if (info.Check != Check.None) ed.Select(line.Offset + info.CheckStart + 1, 1);   // "[ ]" -> select the state char
                else
                {
                    var marker = Markers.Current(ed.Document, lineNo, out int off, out int len);
                    int trimmed = marker.TrimEnd().Length;
                    if (trimmed > 0) ed.Select(off, trimmed); else ed.CaretOffset = off;
                }
                ta.Focus();
            }
            ta.TextView.ElementGenerators.Add(new MarkerGenerator(() => ta.Caret.Line, () => revealLine, Reveal));
            // re-render the old and new caret lines so heading "#" markers show only while editing that line
            int lastCaretLine = 1;
            ta.Caret.PositionChanged += (s, e) =>
            {
                int now = ta.Caret.Line;
                if (now == lastCaretLine) return;
                var doc = ed.Document;
                if (revealLine > 0 && revealLine != now)
                {
                    int r = revealLine;
                    revealLine = -1;
                    if (r <= doc.LineCount) ta.TextView.Redraw(doc.GetLineByNumber(r));
                }
                if (lastCaretLine <= doc.LineCount) ta.TextView.Redraw(doc.GetLineByNumber(lastCaretLine));
                ta.TextView.Redraw(doc.GetLineByNumber(now));
                lastCaretLine = now;
            };
            ta.TextView.ElementGenerators.Add(new ImageGenerator(doc));
            ta.TextView.ElementGenerators.Add(new DueGenerator(ed, () => ta.Caret.Line));
            ta.TextView.LineTransformers.Add(new Code.FenceColorizer());
            SmartEditing.Attach(ed);
            SelectionToolbar.Attach(ed);
            HexAndLinks.Attach(ed);

            // Ctrl+V / Shift+Insert with an image (or image files) on the clipboard
            ta.PreviewKeyDown += (s, e) =>
            {
                bool paste = e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control ||
                             e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.Shift;
                if (paste && Images.TryPaste(ed, doc)) e.Handled = true;
            };

            var search = SearchPanel.Install(ta);
            search.MarkerBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFB, 0xBF, 0x24));

            // horizontal rules size to the view width; redraw once it is known / changes
            ta.TextView.SizeChanged += (s, e) => { if (e.WidthChanged) ta.TextView.Redraw(); };

            ed.PreviewMouseWheel += (s, e) =>
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                Workspace.Zoom(e.Delta > 0 ? +1 : -1);
                e.Handled = true;
            };
            return ed;
        }
    }
}
