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

        readonly TextBlock _title, _close;

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

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(_title);
            sp.Children.Add(_close);
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
            TabDrag.Attach(this, _close);

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

        public void Refresh()
        {
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
                Background = Theme.Bg,
                Foreground = Theme.Fg,
                WordWrap = st.WordWrap,
                ShowLineNumbers = true,
                LineNumbersForeground = Theme.LineNo,
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
            ta.SelectionBrush = Theme.Selection;
            ta.SelectionForeground = null;
            ta.SelectionBorder = null;
            ta.SelectionCornerRadius = 3;
            ta.Caret.CaretBrush = Theme.Accent;
            ta.TextView.CurrentLineBackground = Theme.CurrentLine;
            ta.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
            ta.TextView.LinkTextForegroundBrush = Theme.Link;
            ta.TextView.LinkTextUnderline = true;

            // softer gutter: hide the dotted separator, add breathing room
            foreach (var m in ta.LeftMargins.OfType<Line>()) m.Stroke = Brushes.Transparent;
            ta.LeftMargins.Add(new Rectangle { Width = st.LeftMargin, Fill = Brushes.Transparent });

            ta.TextView.LineTransformers.Add(new TaskColorizer());
            ta.TextView.ElementGenerators.Add(new MarkerGenerator());
            ta.TextView.ElementGenerators.Add(new ImageGenerator(doc));
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
