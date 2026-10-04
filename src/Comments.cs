using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Comments on text, stored as CriticMarkup so the file stays plain text:
    ///   {==highlighted text==}{>>@ana 2026-10-04 12:30: first note || @ben: a reply<<}
    /// A bare note without highlighted text is  {>>note<<}.
    public static class Comments
    {
        public static readonly Regex Markup = new Regex(@"\{==(?<t>.+?)==\}\{>>(?<c>.*?)<<\}|\{>>(?<c>.*?)<<\}", RegexOptions.Compiled);

        public static bool MarginMode => Workspace.Settings.CommentsMode == "margin";

        /// Finds the comment markup containing `offset`, if any.
        public static bool Find(TextDocument doc, int offset, out int start, out Match match)
        {
            var line = doc.GetLineByOffset(Math.Max(0, Math.Min(offset, doc.TextLength)));
            var text = doc.GetText(line);
            for (var m = Markup.Match(text); m.Success; m = m.NextMatch())
            {
                int s = line.Offset + m.Index;
                if (offset >= s && offset <= s + m.Length)
                {
                    start = s;
                    match = m;
                    return true;
                }
            }
            start = -1;
            match = null;
            return false;
        }

        /// Rewrites the thread of the comment at `markupStart`. An empty result removes the comment (text is kept).
        public static void Update(TextDocument doc, int markupStart, Func<List<CommentEntry>, List<CommentEntry>> change)
        {
            if (!Find(doc, markupStart, out int start, out var m)) return;
            var thread = change(CommentThread.Parse(m.Groups["c"].Value));
            if (thread == null || thread.Count == 0)
            {
                doc.Replace(start, m.Length, m.Groups["t"].Success ? m.Groups["t"].Value : "");
                return;
            }
            var g = m.Groups["c"];
            doc.Replace(start + g.Index - m.Index, g.Length, CommentThread.Serialize(thread));
        }

        /// All comments in the document: (markup start, match).
        public static IEnumerable<(int Start, Match M)> All(TextDocument doc)
        {
            foreach (var line in doc.Lines)
            {
                if (line.Length < 5) continue;
                var text = doc.GetText(line);
                if (text.IndexOf("{>>", StringComparison.Ordinal) < 0 && text.IndexOf("{==", StringComparison.Ordinal) < 0) continue;
                for (var m = Markup.Match(text); m.Success; m = m.NextMatch()) yield return (line.Offset + m.Index, m);
            }
        }
    }

    public sealed class CommentEntry
    {
        public string Author, Date, Text;
    }

    /// Thread format inside {>> <<}:  "@author date: text || @author: reply"  (author/date optional).
    public static class CommentThread
    {
        static readonly Regex Head = new Regex(@"^@(?<a>[^\s:]+)(?: (?<d>\d{4}-\d{2}-\d{2}(?: \d{1,2}:\d{2})?))?: (?<t>.*)$", RegexOptions.Singleline);

        public static List<CommentEntry> Parse(string raw)
        {
            var list = new List<CommentEntry>();
            foreach (var part in raw.Split(new[] { " || " }, StringSplitOptions.None))
            {
                var p = part.Trim();
                if (p.Length == 0) continue;
                var m = Head.Match(p);
                list.Add(m.Success
                    ? new CommentEntry { Author = m.Groups["a"].Value, Date = m.Groups["d"].Success ? m.Groups["d"].Value : null, Text = Decode(m.Groups["t"].Value) }
                    : new CommentEntry { Text = Decode(p) });
            }
            return list;
        }

        public static string Serialize(IEnumerable<CommentEntry> entries) =>
            string.Join(" || ", entries.Select(e =>
                (e.Author != null ? "@" + e.Author + (e.Date != null ? " " + e.Date : "") + ": " : "") + Encode(e.Text)));

        public static CommentEntry New(string text) => new CommentEntry
        {
            Author = Author,
            Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Text = text,
        };

        public static string Author
        {
            get
            {
                var a = Workspace.Settings.Author;
                if (string.IsNullOrWhiteSpace(a)) a = Environment.UserName;
                return Regex.Replace(a.Trim(), @"[\s:]+", "_");
            }
        }

        static string Decode(string c) => c.Replace("\\n", "\n");
        static string Encode(string c) =>
            c.Replace("\r\n", "\n").Replace("\n", "\\n").Replace("<<}", "<< }").Replace("||", "| |").Trim();
    }

    /// Highlights commented text.
    public sealed class CommentColorizer : DocumentColorizingTransformer
    {
        static readonly Brush Highlight = Frozen(Color.FromArgb(0x2E, 0xFB, 0xBF, 0x24));
        static readonly TextDecorationCollection Underline = MakeUnderline();

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            if (text.IndexOf("{==", StringComparison.Ordinal) < 0 || Code.IsCodeLine(CurrentContext.Document, line.LineNumber)) return;
            for (var m = Comments.Markup.Match(text); m.Success; m = m.NextMatch())
            {
                var t = m.Groups["t"];
                if (!t.Success) continue;
                ChangeLinePart(line.Offset + t.Index, line.Offset + t.Index + t.Length, el =>
                {
                    el.TextRunProperties.SetBackgroundBrush(Highlight);
                    el.TextRunProperties.SetTextDecorations(Underline);
                });
            }
        }

        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        static TextDecorationCollection MakeUnderline()
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0xB7, 0x0A)), 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
            pen.Freeze();
            var c = new TextDecorationCollection { new TextDecoration(TextDecorationLocation.Underline, pen, 1, TextDecorationUnit.FontRecommended, TextDecorationUnit.Pixel) };
            c.Freeze();
            return c;
        }
    }

    /// Hides the {== ==}{>> <<} syntax and shows a small bubble (hover mode) or nothing (margin mode).
    public sealed class CommentGenerator : VisualLineElementGenerator
    {
        readonly CommentUi _ui;
        public CommentGenerator(CommentUi ui) { _ui = ui; }

        bool Next(int startOffset, out int offset, out int length, out bool bubble, out int markupStart, out int replies)
        {
            var line = CurrentContext.Document.GetLineByOffset(startOffset);
            var text = CurrentContext.Document.GetText(line);
            offset = length = markupStart = -1;
            bubble = false;
            replies = 0;
            if (text.IndexOf("{", StringComparison.Ordinal) < 0 || Code.IsCodeLine(CurrentContext.Document, line.LineNumber)) return false;
            for (var m = Comments.Markup.Match(text); m.Success; m = m.NextMatch())
            {
                int s = line.Offset + m.Index, e = s + m.Length;
                if (e <= startOffset) continue;
                markupStart = s;
                replies = CommentThread.Parse(m.Groups["c"].Value).Count;
                if (m.Groups["t"].Success)
                {
                    int tailStart = s + 3 + m.Groups["t"].Length;
                    if (s >= startOffset) { offset = s; length = 3; return true; }
                    if (tailStart >= startOffset) { offset = tailStart; length = e - tailStart; bubble = true; return true; }
                }
                else if (s >= startOffset) { offset = s; length = m.Length; bubble = true; return true; }
            }
            return false;
        }

        public override int GetFirstInterestedOffset(int startOffset) =>
            Next(startOffset, out int o, out _, out _, out _, out _) ? o : -1;

        public override VisualLineElement ConstructElement(int offset)
        {
            if (!Next(offset, out int o, out int len, out bool bubble, out int markupStart, out int count) || o != offset) return null;
            if (!bubble) return new InlineObjectElement(len, new Canvas { Width = 0 });

            var tv = CurrentContext.TextView;
            var doc = CurrentContext.Document;
            bool bare = doc.GetCharAt(markupStart + 1) == '>'; // {>>note<<} without highlighted text
            // margin mode: the margin card is the UI; keep only a tiny dot for bare notes
            double size = Math.Round(tv.DefaultLineHeight * 0.62);
            var icon = new Border
            {
                Height = size,
                MinWidth = size + 6,
                Padding = new Thickness(3, 0, 3, 0),
                Margin = new Thickness(3, 0, 1, 0),
                CornerRadius = new CornerRadius(size / 2.4, size / 2.4, size / 2.4, 1),
                Background = Theme.Todo,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = count > 1 ? count.ToString() : "…",
                    FontSize = size * (count > 1 ? 0.7 : 0.75),
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(Color.FromRgb(24, 20, 8)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, count > 1 ? 0 : -size * 0.35, 0, 0),
                },
            };
            if (Comments.MarginMode && !bare) return new InlineObjectElement(len, new Canvas { Width = 0 });
            TextBlock.SetBaselineOffset(icon, size - 1);
            var anchor = doc.CreateAnchor(markupStart);
            icon.MouseEnter += (s, e) => { if (!anchor.IsDeleted) _ui.ShowCard(anchor.Offset, icon); };
            icon.MouseLeave += (s, e) => _ui.HideCardSoon();
            icon.MouseLeftButtonDown += (s, e) => e.Handled = true;
            // open on mouse-up: a StaysOpen=false popup opened on mouse-down closes again on the release
            icon.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (!anchor.IsDeleted) _ui.Edit(anchor.Offset);
            };
            return new InlineObjectElement(len, icon);
        }
    }

    /// One comment thread: quote, entries (edit / delete each), reply box, resolve.
    /// Used in the hover popup and as a card in the right margin.
    public sealed class ThreadCard : Border
    {
        readonly TextEditor _ed;
        readonly TextAnchor _anchor;
        readonly StackPanel _stack = new StackPanel();
        TextBox _reply;
        public event Action Done;

        public int Start => _anchor.IsDeleted ? -1 : _anchor.Offset;
        public bool IsEditing => IsKeyboardFocusWithin;

        public ThreadCard(TextEditor ed, int markupStart)
        {
            _ed = ed;
            _anchor = ed.Document.CreateAnchor(markupStart);
            Background = Theme.Popup;
            BorderThickness = new Thickness(1);
            BorderBrush = Theme.ChromeBorder;
            CornerRadius = new CornerRadius(7);
            Padding = new Thickness(12, 9, 12, 10);
            Child = _stack;
            Cursor = Cursors.Arrow;
            Build();
            MouseEnter += (s, e) => SetActive(true);
            MouseLeave += (s, e) => SetActive(IsKeyboardFocusWithin);
            IsKeyboardFocusWithinChanged += (s, e) => SetActive(IsKeyboardFocusWithin || IsMouseOver);
            MouseLeftButtonUp += (s, e) =>
            {
                if (e.OriginalSource is TextBox) return;
                SelectInEditor();
            };
        }

        void SetActive(bool on) => BorderBrush = on ? Theme.Todo : Theme.ChromeBorder;

        public void SelectInEditor()
        {
            if (!Comments.Find(_ed.Document, Start, out int start, out var m)) return;
            var t = m.Groups["t"];
            if (t.Success) _ed.Select(start + 3, t.Length); else _ed.CaretOffset = start;
            _ed.TextArea.Caret.BringCaretToView();
        }

        public void FocusReply()
        {
            Dispatcher.BeginInvoke(new Action(() => { _reply?.Focus(); Keyboard.Focus(_reply); }), DispatcherPriority.Input);
        }

        void Build()
        {
            _stack.Children.Clear();
            if (!Comments.Find(_ed.Document, Start, out _, out var m)) return;
            var thread = CommentThread.Parse(m.Groups["c"].Value);

            if (m.Groups["t"].Success)
                _stack.Children.Add(new Border
                {
                    BorderBrush = Theme.Todo,
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    Padding = new Thickness(7, 0, 0, 0),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = new TextBlock
                    {
                        Text = m.Groups["t"].Value,
                        Foreground = Theme.FgDim,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontStyle = FontStyles.Italic,
                        FontSize = 12,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                });

            for (int i = 0; i < thread.Count; i++) _stack.Children.Add(Entry(thread[i], i));

            _reply = Box(thread.Count == 0 ? "Comment…" : "Reply…");
            _reply.Margin = new Thickness(0, 8, 0, 0);
            _reply.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
                {
                    e.Handled = true;
                    var text = _reply.Text.Trim();
                    if (text.Length == 0) return;
                    Comments.Update(_ed.Document, Start, t => { t.Add(CommentThread.New(text)); return t; });
                    _reply.Text = "";
                    Build();
                    FocusReply();
                }
                else if (e.Key == Key.Escape) { e.Handled = true; _ed.TextArea.Focus(); Done?.Invoke(); }
            };
            _stack.Children.Add(_reply);

            var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
            footer.Children.Add(new TextBlock { Text = "Enter to send · Shift+Enter new line", Foreground = Theme.FgFaint, FontFamily = new FontFamily("Segoe UI"), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center });
            var resolve = Link("✓ Resolve", "Remove the whole thread (the text stays)", () =>
            {
                Comments.Update(_ed.Document, Start, t => null);
                Done?.Invoke();
            });
            DockPanel.SetDock(resolve, Dock.Right);
            footer.Children.Add(resolve);
            _stack.Children.Add(footer);
        }

        UIElement Entry(CommentEntry e, int index)
        {
            var head = new DockPanel { LastChildFill = false };
            var name = new TextBlock
            {
                Text = e.Author ?? "note",
                Foreground = e.Author != null ? AuthorBrush(e.Author) : Theme.FgDim,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
            };
            head.Children.Add(name);
            if (e.Date != null)
                head.Children.Add(new TextBlock { Text = "  " + e.Date, Foreground = Theme.FgFaint, FontFamily = new FontFamily("Segoe UI"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0 };
            DockPanel.SetDock(tools, Dock.Right);
            head.Children.Add(tools);

            var body = new TextBlock
            {
                Text = e.Text,
                Foreground = Theme.Fg,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            };
            var holder = new StackPanel { Margin = new Thickness(0, index == 0 ? 0 : 8, 0, 0), Background = Brushes.Transparent };
            holder.Children.Add(head);
            holder.Children.Add(body);
            holder.MouseEnter += (s, a) => tools.Opacity = 1;
            holder.MouseLeave += (s, a) => tools.Opacity = 0;

            tools.Children.Add(Link("✎", "Edit", () =>
            {
                var box = Box("");
                box.Text = e.Text;
                int i = holder.Children.IndexOf(body);
                holder.Children[i] = box;
                box.PreviewKeyDown += (s, a) =>
                {
                    if (a.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
                    {
                        a.Handled = true;
                        var text = box.Text.Trim();
                        Comments.Update(_ed.Document, Start, t =>
                        {
                            if (text.Length == 0) t.RemoveAt(index); else t[index].Text = text;
                            return t;
                        });
                        Build();
                    }
                    else if (a.Key == Key.Escape) { a.Handled = true; Build(); }
                };
                Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.CaretIndex = box.Text.Length; }), DispatcherPriority.Input);
            }));
            tools.Children.Add(Link("✕", "Delete this message", () =>
            {
                Comments.Update(_ed.Document, Start, t => { t.RemoveAt(index); return t; });
                if (Start < 0 || !Comments.Find(_ed.Document, Start, out _, out _)) Done?.Invoke(); else Build();
            }));
            return holder;
        }

        static readonly Color[] Palette =
        {
            Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0x22, 0xD3, 0xEE), Color.FromRgb(0xA7, 0x8B, 0xFA),
            Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0xF4, 0x72, 0xB6), Color.FromRgb(0x60, 0xA5, 0xFA),
        };

        static Brush AuthorBrush(string a)
        {
            int h = 0;
            foreach (var ch in a) h = h * 31 + ch;
            return new SolidColorBrush(Palette[Math.Abs(h) % Palette.Length]);
        }

        static TextBox Box(string placeholder)
        {
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 160,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Theme.Input,
                Foreground = Theme.Fg,
                CaretBrush = Theme.Accent,
                SelectionBrush = Theme.Accent,
                BorderBrush = Theme.ChromeBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
            };
            if (placeholder.Length > 0)
            {
                var hint = new VisualBrush(new TextBlock { Text = placeholder, Foreground = Theme.FgFaint, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Margin = new Thickness(8, 4, 0, 0) })
                { AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, Stretch = Stretch.None };
                var bg = box.Background;
                void Upd() => box.Background = box.Text.Length == 0 && !box.IsKeyboardFocused ? (Brush)hint : bg;
                box.TextChanged += (s, e) => Upd();
                box.GotKeyboardFocus += (s, e) => Upd();
                box.LostKeyboardFocus += (s, e) => Upd();
                box.Loaded += (s, e) => Upd();
            }
            return box;
        }

        static Border Link(string text, string tip, Action click)
        {
            var b = new Border
            {
                Padding = new Thickness(6, 1, 6, 2),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = new TextBlock { Text = text, Foreground = Theme.FgDim, FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"), FontSize = 11.5 },
            };
            var hover = Theme.Hover;
            b.MouseEnter += (s, e) => b.Background = hover;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            return b;
        }
    }

    /// Hover card, new-comment box and thread popup for one editor.
    public sealed class CommentUi
    {
        readonly TextEditor _ed;
        readonly Popup _card, _editor, _thread;
        readonly StackPanel _cardStack = new StackPanel();
        readonly TextBox _box;
        readonly Border _editFrame;
        readonly DispatcherTimer _hideTimer;
        (int Start, int Length)? _newRange;
        int _cardStart = -1;

        public CommentMargin Margin;

        public static CommentUi Attach(TextEditor ed)
        {
            var ui = new CommentUi(ed);
            ed.TextArea.TextView.ElementGenerators.Add(new CommentGenerator(ui));
            ed.TextArea.TextView.LineTransformers.Add(new CommentColorizer());
            return ui;
        }

        CommentUi(TextEditor ed)
        {
            _ed = ed;
            var tv = ed.TextArea.TextView;

            // ---- hover card (read-only)
            var cardBorder = Frame(new Border { Child = _cardStack, Padding = new Thickness(12, 9, 12, 9) });
            cardBorder.MaxWidth = 420;
            _card = new Popup
            {
                AllowsTransparency = true,
                Placement = PlacementMode.Relative,
                PlacementTarget = tv,
                StaysOpen = true,
                Focusable = false,
                PopupAnimation = PopupAnimation.Fade,
                Child = cardBorder,
            };
            cardBorder.MouseEnter += (s, e) => _hideTimer.Stop();
            cardBorder.MouseLeave += (s, e) => HideCardSoon();
            cardBorder.MouseLeftButtonUp += (s, e) => { if (_cardStart >= 0) Edit(_cardStart); };
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _hideTimer.Tick += (s, e) => { _hideTimer.Stop(); _card.IsOpen = false; };

            // ---- new comment box (resizable)
            _box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Theme.Input,
                Foreground = Theme.Fg,
                CaretBrush = Theme.Accent,
                SelectionBrush = Theme.Accent,
                BorderBrush = Theme.ChromeBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 5, 6, 5),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
            };
            var save = Btn("Comment", "Ctrl+Enter", Theme.Accent, Commit);
            var cancel = Btn("Cancel", "Esc", Theme.FgDim, () => _editor.IsOpen = false);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 14, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            var title = new TextBlock { Text = "New comment  ·  " + CommentThread.Author, Foreground = Theme.Todo, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
            var grip = new Thumb { Width = 14, Height = 14, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -8, -6), Template = GripTemplate() };
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(title, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            dock.Children.Add(title);
            dock.Children.Add(buttons);
            dock.Children.Add(_box);
            var gridRoot = new Grid();
            gridRoot.Children.Add(dock);
            gridRoot.Children.Add(grip);
            _editFrame = Frame(new Border { Child = gridRoot, Padding = new Thickness(12, 10, 12, 10) });
            _editFrame.Width = Workspace.Settings.CommentWidth;
            _editFrame.Height = Workspace.Settings.CommentHeight;
            grip.DragDelta += (s, e) =>
            {
                _editFrame.Width = Math.Max(240, Math.Min(900, _editFrame.Width + e.HorizontalChange));
                _editFrame.Height = Math.Max(140, Math.Min(700, _editFrame.Height + e.VerticalChange));
            };
            grip.DragCompleted += (s, e) =>
            {
                Workspace.Settings.CommentWidth = _editFrame.Width;
                Workspace.Settings.CommentHeight = _editFrame.Height;
            };
            _editor = MakePopup(_editFrame);
            _editor.Closed += (s, e) => { _newRange = null; _ed.TextArea.Focus(); };
            _box.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { Commit(); e.Handled = true; }
                else if (e.Key == Key.Escape) { _editor.IsOpen = false; e.Handled = true; }
            };

            // ---- thread popup (hover mode: click the bubble)
            _thread = MakePopup(null);

            // hover over commented text
            tv.MouseHover += (s, e) =>
            {
                var pos = tv.GetPositionFloor(e.GetPosition(tv) + tv.ScrollOffset);
                if (pos == null) return;
                int off = _ed.Document.GetOffset(pos.Value.Location);
                if (!Comments.Find(_ed.Document, off, out int start, out _)) return;
                if (Comments.MarginMode) Margin?.Highlight(start);
                else ShowCard(start, null, e.GetPosition(tv));
            };
            tv.MouseHoverStopped += (s, e) => { HideCardSoon(); Margin?.Highlight(-1); };

            ed.TextArea.PreviewKeyDown += (s, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                var mods = Keyboard.Modifiers;
                if (key == Key.M && (mods == ModifierKeys.Control || mods == (ModifierKeys.Control | ModifierKeys.Alt))) { AddFromSelection(); e.Handled = true; }
            };
            SelectionToolbar.CommentRequested += editor => { if (editor == _ed) AddFromSelection(); };
        }

        Popup MakePopup(UIElement child) => new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = _ed.TextArea.TextView,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
            Child = child,
        };

        // ---------------- hover card ----------------

        public void ShowCard(int markupStart, FrameworkElement near, Point? at = null)
        {
            if (_editor.IsOpen || _thread.IsOpen || Comments.MarginMode) return;
            if (!Comments.Find(_ed.Document, markupStart, out int start, out var m)) return;
            _hideTimer.Stop();
            _cardStart = start;
            _cardStack.Children.Clear();
            var thread = CommentThread.Parse(m.Groups["c"].Value);
            foreach (var e in thread)
            {
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Foreground = Theme.Fg, Margin = new Thickness(0, _cardStack.Children.Count == 0 ? 0 : 6, 0, 0) };
                if (e.Author != null) tb.Inlines.Add(new System.Windows.Documents.Run(e.Author + "  ") { Foreground = Theme.Todo, FontWeight = FontWeights.SemiBold, FontSize = 12 });
                tb.Inlines.Add(new System.Windows.Documents.Run(e.Text));
                _cardStack.Children.Add(tb);
            }
            if (thread.Count == 0) _cardStack.Children.Add(new TextBlock { Text = "(empty comment)", Foreground = Theme.FgDim });
            _cardStack.Children.Add(new TextBlock { Text = thread.Count > 1 ? $"{thread.Count} messages · click to reply" : "click to reply or edit", Foreground = Theme.FgDim, FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
            var tv = _ed.TextArea.TextView;
            Point p = at ?? near.TranslatePoint(new Point(0, near.ActualHeight), tv);
            _card.HorizontalOffset = p.X + 4;
            _card.VerticalOffset = p.Y + 14;
            _card.IsOpen = true;
        }

        public void HideCardSoon()
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        // ---------------- new / edit ----------------

        void AddFromSelection()
        {
            var sel = _ed.TextArea.Selection;
            var doc = _ed.Document;
            if (!sel.IsEmpty)
            {
                var seg = sel.SurroundingSegment;
                if (doc.GetLineByOffset(seg.Offset).LineNumber != doc.GetLineByOffset(seg.EndOffset).LineNumber) return; // one line only
                _newRange = (seg.Offset, seg.Length);
            }
            else _newRange = (_ed.CaretOffset, 0);
            _box.Text = "";
            PlaceAt(_editor, _newRange.Value.Start);
            _card.IsOpen = false;
            _editor.IsOpen = true;
            // focus once the popup's window exists, otherwise keys keep going to the editor
            _box.Dispatcher.BeginInvoke(new Action(() => { _box.Focus(); Keyboard.Focus(_box); }), DispatcherPriority.Input);
        }

        /// Opens the thread for replies / edits: margin card in margin mode, popup otherwise.
        public void Edit(int markupStart)
        {
            if (!Comments.Find(_ed.Document, markupStart, out int start, out _)) return;
            _card.IsOpen = false;
            if (Comments.MarginMode && Margin != null && Margin.Focus(start)) return;
            var card = new ThreadCard(_ed, start) { Width = Workspace.Settings.CommentWidth, BorderBrush = Theme.Todo };
            card.Done += () => _thread.IsOpen = false;
            _thread.Child = card;
            PlaceAt(_thread, start);
            _thread.IsOpen = true;
            card.FocusReply();
        }

        void PlaceAt(Popup p, int offset)
        {
            var tv = _ed.TextArea.TextView;
            var loc = _ed.Document.GetLocation(offset);
            var vp = tv.GetVisualPosition(new TextViewPosition(loc), VisualYPosition.LineBottom) - tv.ScrollOffset;
            p.HorizontalOffset = Math.Max(8, vp.X - 20);
            p.VerticalOffset = vp.Y + 6;
        }

        void Commit()
        {
            var doc = _ed.Document;
            var text = _box.Text.Trim();
            if (_newRange.HasValue && text.Length > 0)
            {
                var (s, len) = _newRange.Value;
                string thread = CommentThread.Serialize(new[] { CommentThread.New(text) });
                string sel = doc.GetText(s, len);
                string markup = len > 0 ? "{==" + sel + "==}{>>" + thread + "<<}" : "{>>" + thread + "<<}";
                doc.Replace(s, len, markup);
                _ed.TextArea.ClearSelection();
                _ed.CaretOffset = s + markup.Length;
            }
            _editor.IsOpen = false;
        }

        // ---------------- ui helpers ----------------

        static Border Frame(Border inner)
        {
            var b = new Border
            {
                Background = Theme.Popup,
                BorderThickness = new Thickness(3, 1, 1, 1),
                CornerRadius = new CornerRadius(6),
                Child = inner,
            };
            b.BorderBrush = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Theme.Todo.Color, 0), new GradientStop(Theme.Todo.Color, 0.01),
                new GradientStop(Theme.ChromeBorder.Color, 0.01), new GradientStop(Theme.ChromeBorder.Color, 1),
            }, 0);
            return b;
        }

        static Border Btn(string text, string tip, Brush fg, Action click)
        {
            var b = new Border
            {
                Padding = new Thickness(12, 4, 12, 5),
                Margin = new Thickness(6, 0, 0, 0),
                CornerRadius = new CornerRadius(5),
                Background = text == "Comment" ? Theme.AccentSoft : Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = new TextBlock { Text = text, Foreground = fg, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5 },
            };
            var bg = b.Background;
            var hover = Theme.Hover;
            b.MouseEnter += (s, e) => b.Background = hover;
            b.MouseLeave += (s, e) => b.Background = bg;
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        static ControlTemplate GripTemplate()
        {
            var t = new ControlTemplate(typeof(Thumb));
            var f = new FrameworkElementFactory(typeof(TextBlock));
            f.SetValue(TextBlock.TextProperty, "◢");
            f.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Theme.FgDim.Color)); // templates freeze their values: use a copy
            f.SetValue(TextBlock.FontSizeProperty, 11.0);
            f.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
            t.VisualTree = f;
            return t;
        }
    }

    /// Google Docs-style comment column on the right: one card per thread, aligned to its line.
    public sealed class CommentMargin : Border
    {
        readonly TextEditor _ed;
        readonly Canvas _canvas = new Canvas { ClipToBounds = true };
        readonly List<ThreadCard> _cards = new List<ThreadCard>();
        readonly DispatcherTimer _rebuild;
        bool _dirty = true;

        public CommentMargin(TextEditor ed)
        {
            _ed = ed;
            Width = Workspace.Settings.CommentMarginWidth;
            Background = Theme.Chrome;
            BorderBrush = Theme.ChromeBorder;
            BorderThickness = new Thickness(1, 0, 0, 0);
            Child = _canvas;

            _rebuild = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _rebuild.Tick += (s, e) => { _rebuild.Stop(); Rebuild(); };
            ed.Document.TextChanged += (s, e) => { _dirty = true; _rebuild.Stop(); _rebuild.Start(); };
            var tv = ed.TextArea.TextView;
            tv.ScrollOffsetChanged += (s, e) => Layout();
            tv.VisualLinesChanged += (s, e) => Layout();
            SizeChanged += (s, e) => Layout();
            IsVisibleChanged += (s, e) => { if (IsVisible) Rebuild(); };
            _canvas.MouseWheel += (s, e) => ed.ScrollToVerticalOffset(ed.VerticalOffset - e.Delta); // scroll the note from the margin
            ApplyMode();
        }

        public void ApplyMode()
        {
            Visibility = Comments.MarginMode ? Visibility.Visible : Visibility.Collapsed;
            if (IsVisible) Rebuild();
        }

        void Rebuild()
        {
            if (!IsVisible) return;
            if (_cards.Any(c => c.IsEditing)) { _rebuild.Start(); return; } // don't yank a box you're typing in
            _cards.Clear();
            _canvas.Children.Clear();
            foreach (var (start, _) in Comments.All(_ed.Document))
            {
                var card = new ThreadCard(_ed, start) { Width = Width - 22 };
                _cards.Add(card);
                _canvas.Children.Add(card);
            }
            _dirty = false;
            Layout();
        }

        void Layout()
        {
            if (!IsVisible || _ed.Document == null) return;
            if (_dirty && !_rebuild.IsEnabled) { Rebuild(); return; }
            var tv = _ed.TextArea.TextView;
            double prevBottom = 4;
            foreach (var c in _cards)
            {
                if (c.Start < 0 || c.Start > _ed.Document.TextLength) { c.Visibility = Visibility.Collapsed; continue; }
                var line = _ed.Document.GetLineByOffset(c.Start).LineNumber;
                double y = tv.GetVisualTopByDocumentLine(line) - tv.ScrollOffset.Y + 6; // + editor padding
                c.Measure(new Size(c.Width, double.PositiveInfinity));
                y = Math.Max(y, prevBottom + 8);
                Canvas.SetLeft(c, 11);
                Canvas.SetTop(c, y);
                prevBottom = y + c.DesiredSize.Height;
            }
        }

        public void Highlight(int start)
        {
            foreach (var c in _cards)
                if (!c.IsEditing) c.BorderBrush = c.Start == start ? Theme.Todo : Theme.ChromeBorder;
        }

        public bool Focus(int start)
        {
            if (!IsVisible) return false;
            if (_dirty) Rebuild();
            var card = _cards.FirstOrDefault(c => c.Start == start);
            if (card == null) return false;
            card.FocusReply();
            return true;
        }
    }
}
