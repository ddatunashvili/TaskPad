using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace TaskPad
{
    /// Dropdown listing every keyword with a preview; clicking one applies it to the current line.
    public static class KeywordsPopup
    {
        enum Mode { LinePrefix, Inline, Rule }

        sealed class Kw
        {
            public string Preview, Syntax, Desc;
            public Brush Color;
            public bool Badge, Bold, Italic, Strike;
            public Mode Mode;
            public Kw(string preview, string syntax, string desc, Brush color, Mode mode = Mode.LinePrefix)
            { Preview = preview; Syntax = syntax; Desc = desc; Color = color; Mode = mode; }
        }

        static readonly object[] Items =
        {
            "Tasks",
            new Kw("☐", "[ ] ", "Open task — type [] for one", Theme.Fg),
            new Kw("☑", "[x] ", "Done task", Theme.Done) { Strike = true },
            new Kw("◧", "[/] ", "In progress  (Ctrl+click box)", Theme.BoxDoing),
            new Kw("☒", "[-] ", "Cancelled  (Shift+click box)", Theme.BoxCancel) { Strike = true },
            "Markers",
            new Kw("!", "! ", "Urgent / warning", Theme.Bang) { Badge = true, Bold = true },
            new Kw("?", "? ", "Question / idea", Theme.Question) { Badge = true, Italic = true },
            new Kw("TODO", "TODO: ", "Something to do", Theme.Todo) { Badge = true, Bold = true },
            new Kw("★", "* ", "Starred / important", Theme.Star),
            new Kw("❯", "> ", "Next up / forwarded", Theme.Arrow) { Bold = true },
            new Kw("❮", "< ", "Waiting on someone", Theme.Back),
            new Kw("✓", "/ ", "Finished note", Theme.Slash) { Bold = true },
            new Kw("•", "- ", "Bullet", Theme.Dash),
            new Kw("1.", "1. ", "Numbered list", Theme.Accent) { Bold = true },
            "Structure",
            new Kw("H", "# ", "Heading  (## … ###### smaller)", Theme.Heading) { Bold = true },
            new Kw("—", "---", "Horizontal line  (=== accent)", Theme.Dash, Mode.Rule),
            "Inline",
            new Kw("@", "@name", "Person", Theme.Mention, Mode.Inline),
            new Kw("#", "#tag", "Tag", Theme.HashTag, Mode.Inline),
            new Kw("📅", "{date}", "Date  (Ctrl+;)", Theme.Date, Mode.Inline),
            new Kw("⏰", "{due}", "Deadline with countdown — also due:+3d, due:friday", Theme.Todo, Mode.Inline),
            new Kw("`", "`code`", "Code", Theme.Code, Mode.Inline),
        };

        public static void Show(FrameworkElement anchor, Func<TextEditor> editor, Action cheatSheet)
        {
            var popup = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
            };

            var list = new StackPanel { Margin = new Thickness(4) };
            foreach (var o in Items)
            {
                if (o is string header) { list.Children.Add(Header(header)); continue; }
                var kw = (Kw)o;
                var row = Row(kw);
                row.MouseLeftButtonUp += (s, e) =>
                {
                    popup.IsOpen = false;
                    var ed = editor();
                    if (ed == null) return;
                    Apply(ed, kw);
                    ed.TextArea.Focus();
                };
                list.Children.Add(row);
            }

            var footer = new TextBlock
            {
                Text = "Click to apply to the line or all selected lines  ·  F1 cheat sheet",
                Foreground = Theme.FgDim,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11.5,
                Margin = new Thickness(12, 8, 12, 6),
                Cursor = Cursors.Hand,
            };
            footer.MouseLeftButtonUp += (s, e) => { popup.IsOpen = false; cheatSheet(); };
            list.Children.Add(new Border { Height = 1, Background = Theme.ChromeBorder, Margin = new Thickness(8, 6, 8, 0) });
            list.Children.Add(footer);

            const double width = 400;
            popup.Child = new Border
            {
                Width = width,
                Background = Theme.Popup,
                BorderBrush = Theme.ChromeBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = new ScrollViewer
                {
                    Content = list,
                    MaxHeight = 560,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
            };
            popup.HorizontalOffset = anchor.ActualWidth - width;
            popup.VerticalOffset = 2;
            popup.IsOpen = true;
        }

        static UIElement Header(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            Foreground = Theme.FgDim,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 10, 0, 4),
        };

        static Border Row(Kw kw)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            FrameworkElement preview;
            if (kw.Badge)
            {
                preview = new Border
                {
                    Background = kw.Color,
                    CornerRadius = new CornerRadius(8),
                    Height = 16,
                    MinWidth = 16,
                    Padding = new Thickness(4, 0, 4, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = kw.Preview,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontWeight = FontWeights.Bold,
                        FontSize = kw.Preview.Length > 1 ? 9 : 11,
                        Foreground = kw.Color == Theme.Todo ? new SolidColorBrush(Color.FromRgb(24, 20, 8)) : Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
            }
            else
            {
                preview = new TextBlock
                {
                    Text = kw.Preview,
                    Foreground = kw.Color,
                    FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
                    FontSize = 15,
                    FontWeight = kw.Bold ? FontWeights.Bold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            preview.Margin = new Thickness(8, 0, 0, 0);
            g.Children.Add(preview);

            var syntax = new TextBlock
            {
                Text = kw.Syntax.Replace("{date}", "2026-10-04").Replace("{due}", "due:…").TrimEnd(),
                Foreground = Theme.Fg,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var chip = new Border
            {
                Child = syntax,
                Background = Theme.Hover,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(chip, 1);
            g.Children.Add(chip);

            var desc = new TextBlock
            {
                Text = kw.Desc,
                Foreground = kw.Color,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12.5,
                FontWeight = kw.Bold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStyle = kw.Italic ? FontStyles.Italic : FontStyles.Normal,
                TextDecorations = kw.Strike ? TextDecorations.Strikethrough : null,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(desc, 2);
            g.Children.Add(desc);

            var row = new Border
            {
                Child = g,
                Height = 30,
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = kw.Mode == Mode.Inline ? "Insert at cursor" : "Apply to current line",
            };
            ToolTipService.SetInitialShowDelay(row, 800);
            var hover = Theme.Hover;
            row.MouseEnter += (s, e) => row.Background = hover;
            row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
            return row;
        }

        static void Apply(TextEditor ed, Kw kw)
        {
            var doc = ed.Document;
            var line = doc.GetLineByOffset(ed.CaretOffset);
            var text = doc.GetText(line);

            if (kw.Mode == Mode.Inline)
            {
                var s = kw.Syntax.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"))
                                 .Replace("{due}", "due:" + DateTime.Now.AddDays(1).ToString("yyyy-MM-dd") + " 18:00");
                int caret = ed.CaretOffset;
                bool needSpace = caret > line.Offset && !char.IsWhiteSpace(doc.GetCharAt(caret - 1));
                ed.TextArea.Selection.ReplaceSelectionWithText((needSpace ? " " : "") + s);
                return;
            }

            if (kw.Mode == Mode.Rule)
            {
                if (text.Trim().Length == 0) doc.Replace(line.Offset, line.Length, kw.Syntax);
                else
                {
                    var nl = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(doc, line.LineNumber);
                    doc.Insert(line.EndOffset, nl + kw.Syntax);
                }
                ed.CaretOffset = doc.GetLineByOffset(ed.CaretOffset).EndOffset;
                return;
            }

            // Line prefix: applies to the caret line or every selected line (toggles off if already set).
            Markers.Apply(ed, kw.Syntax);
        }
    }
}
