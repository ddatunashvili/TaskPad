using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Right-click on a checkbox or marker icon: switch the line to another keyword, remove it, or type your own.
    public static class KeywordMenu
    {
        static readonly (string Label, string Syntax)[] Items =
        {
            ("☐|Task", "[ ] "),
            ("☑|Done", "[x] "),
            ("◧|In progress", "[/] "),
            ("☒|Cancelled", "[-] "),
            (null, null),
            ("!|Urgent", "! "),
            ("?|Question", "? "),
            ("T|To do", "TODO: "),
            ("★|Starred", "* "),
            ("❯|Next up", "> "),
            ("❮|Waiting", "< "),
            ("✓|Finished note", "/ "),
            ("•|Bullet", "- "),
            ("1.|Numbered", "1. "),
        };

        static object Row(string label)
        {
            var parts = label.Split('|');
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = parts[0], Width = 24, FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"), Foreground = Theme.FgDim });
            sp.Children.Add(new TextBlock { Text = parts[1] });
            return sp;
        }

        public static void Attach(FrameworkElement el, TextView tv, TextAnchor lineAnchor)
        {
            el.MouseRightButtonDown += (s, e) => e.Handled = true;
            el.MouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (lineAnchor.IsDeleted) return;
                var doc = lineAnchor.Document;
                int lineNo = doc.GetLineByOffset(lineAnchor.Offset).LineNumber;
                var current = Markers.Current(doc, lineNo, out int mOff, out int mLen).Trim();

                var menu = new ContextMenu { PlacementTarget = el };
                foreach (var (label, syntax) in Items)
                {
                    if (label == null) { menu.Items.Add(new Separator()); continue; }
                    var mi = new MenuItem { Header = Row(label), InputGestureText = syntax.Trim() };
                    if (string.Equals(syntax.Trim(), current, System.StringComparison.OrdinalIgnoreCase)) { mi.IsCheckable = true; mi.IsChecked = true; }
                    var sx = syntax;
                    mi.Click += (a, b) => Markers.SetLine(doc, lineNo, sx);
                    menu.Items.Add(mi);
                }
                menu.Items.Add(new Separator());
                var remove = new MenuItem { Header = "Remove marker" };
                remove.Click += (a, b) => Markers.SetLine(doc, lineNo, null);
                menu.Items.Add(remove);
                var type = new MenuItem { Header = "Type a different keyword…", InputGestureText = "raw text" };
                type.Click += (a, b) =>
                {
                    // select the raw marker so typing replaces it
                    if (tv.GetService(typeof(TextArea)) is TextArea ta)
                    {
                        Markers.Current(doc, lineNo, out int o, out int l);
                        ta.Selection = Selection.Create(ta, o, o + l);
                        ta.Caret.Offset = o + l;
                        ta.Focus();
                    }
                };
                menu.Items.Add(type);
                menu.IsOpen = true;
            };
        }
    }
}
