using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskPad
{
    /// A pane with its own tab strip and editor. Windows hold one or more groups in a split layout.
    public sealed class EditorGroup : DockPanel
    {
        public TaskWindow Owner;
        public readonly List<TabView> Tabs = new List<TabView>();
        public readonly StackPanel Strip = new StackPanel { Orientation = Orientation.Horizontal };
        public readonly Border Bar;
        public readonly Border Body = new Border { Background = Theme.Bg };
        public TabView Active;

        public EditorGroup(TaskWindow owner)
        {
            Owner = owner;
            var bar = new Grid { Height = 36, Background = Theme.Chrome };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 5; i++) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var scroller = new ScrollViewer
            {
                Content = Strip,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent,
            };
            scroller.PreviewMouseWheel += (s, e) => { scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta / 2.0); e.Handled = true; };
            scroller.MouseDoubleClick += (s, e) =>
            {
                if (e.OriginalSource == scroller || e.OriginalSource == Strip || e.OriginalSource is Border b && b.Child == Strip) Owner.NewTab(this);
            };
            bar.Children.Add(scroller);

            AddButton(bar, 1, "+", "New tab (Ctrl+N)", b => Owner.NewTab(this));
            AddButton(bar, 2, "◫", "Split right (Ctrl+\\)", b => Owner.SplitActive(this, Dock.Right));
            AddButton(bar, 3, "✦", "Keywords (Ctrl+K)", b => KeywordsPopup.Show(b, () => Active?.Editor, () => Owner.ShowCheatSheet()));
            AddButton(bar, 4, "↺", "Recently closed (Ctrl+Shift+T reopens last)", b => Recent.ShowMenu(Owner, b));
            AddButton(bar, 5, "⋯", "Menu", b => Owner.ShowMenu(b, this));

            Bar = new Border { Child = bar, BorderBrush = Theme.ChromeBorder, BorderThickness = new Thickness(0, 0, 0, 1) };
            SetDock(Bar, Dock.Top);
            Children.Add(Bar);
            Children.Add(Body);

            PreviewGotKeyboardFocus += (s, e) => Owner.SetActiveGroup(this);
            PreviewMouseDown += (s, e) => Owner.SetActiveGroup(this);
        }

        static void AddButton(Grid bar, int col, string glyph, string tip, Action<FrameworkElement> click)
        {
            var tb = new TextBlock
            {
                Text = glyph,
                Foreground = Theme.FgDim,
                FontSize = glyph == "◫" ? 14 : 17,
                FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
            };
            var b = new Border { Width = 34, Background = Brushes.Transparent, Child = tb, ToolTip = tip, Cursor = Cursors.Hand };
            b.MouseEnter += (s, e) => tb.Foreground = Theme.Fg;
            b.MouseLeave += (s, e) => tb.Foreground = Theme.FgDim;
            b.MouseLeftButtonUp += (s, e) => click(b);
            Grid.SetColumn(b, col);
            bar.Children.Add(b);
        }

        public void Insert(TabView t, int index, bool activate = true)
        {
            t.Group = this;
            index = Math.Max(0, Math.Min(index, Tabs.Count));
            Tabs.Insert(index, t);
            Strip.Children.Insert(index, t.Header);
            t.Shift.X = 0;
            Session.MarkDirty();
            if (activate) Activate(t);
            else t.Refresh();
        }

        /// Removes the tab's UI from this group. Caller decides what happens to an empty group.
        public void Remove(TabView t)
        {
            int i = Tabs.IndexOf(t);
            if (i < 0) return;
            Tabs.RemoveAt(i);
            Strip.Children.Remove(t.Header);
            if (Body.Child == t.Root) Body.Child = null;
            t.Group = null;
            Session.MarkDirty();
            if (Active == t)
            {
                Active = null;
                if (Tabs.Count > 0) Activate(Tabs[Math.Min(i, Tabs.Count - 1)], focus: false);
            }
        }

        public void Activate(TabView t, bool focus = true)
        {
            if (t == null) return;
            var prev = Active;
            Active = t;
            if (t.Root.Parent is Border old && old != Body) old.Child = null;
            Body.Child = t.Root;
            prev?.Refresh();
            t.Refresh();
            t.Header.BringIntoView();
            if (focus)
            {
                Owner.SetActiveGroup(this);
                Dispatcher.BeginInvoke(new Action(() => t.Editor.TextArea.Focus()), DispatcherPriority.Input);
            }
            Owner.UpdateStatus();
            Owner.UpdateStats();
        }

        /// Keeps Tabs in the same order as the header elements (after a drag reorder).
        public void SyncOrderFromStrip()
        {
            Tabs.Clear();
            foreach (FrameworkElement h in Strip.Children) Tabs.Add((TabView)h.Tag);
        }

        public void RefreshHeaders()
        {
            foreach (var t in Tabs) t.Refresh();
        }
    }
}
