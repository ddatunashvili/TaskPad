using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TaskPad
{
    /// Colours. Brushes are shared instances whose Color is swapped in place, so switching
    /// dark / light updates every open window live.
    public static class Theme
    {
        public static bool IsLight { get; private set; }

        public static readonly SolidColorBrush
            Bg = new SolidColorBrush(), Chrome = new SolidColorBrush(), ChromeBorder = new SolidColorBrush(),
            Fg = new SolidColorBrush(), FgDim = new SolidColorBrush(), FgFaint = new SolidColorBrush(),
            LineNo = new SolidColorBrush(), CurrentLine = new SolidColorBrush(), Selection = new SolidColorBrush(),
            Accent = new SolidColorBrush(), Heading = new SolidColorBrush(), Section = new SolidColorBrush(), Done = new SolidColorBrush(),
            Popup = new SolidColorBrush(), Hover = new SolidColorBrush(), Input = new SolidColorBrush(), AccentSoft = new SolidColorBrush(),

            // better-comments palette
            Bang = new SolidColorBrush(), Slash = new SolidColorBrush(), Star = new SolidColorBrush(), Question = new SolidColorBrush(),
            Todo = new SolidColorBrush(), Arrow = new SolidColorBrush(), Back = new SolidColorBrush(), Dash = new SolidColorBrush(),

            // inline tokens
            Mention = new SolidColorBrush(), HashTag = new SolidColorBrush(), Date = new SolidColorBrush(), Code = new SolidColorBrush(), Link = new SolidColorBrush(),

            // checkbox states
            BoxOpen = new SolidColorBrush(), BoxDone = new SolidColorBrush(), BoxDoing = new SolidColorBrush(), BoxCancel = new SolidColorBrush();

        static Theme() => Apply(false);

        public static void Apply(bool light)
        {
            IsLight = light;
            if (!light)
            {
                Set(Bg, "#0E0E11"); Set(Chrome, "#09090B"); Set(ChromeBorder, "#1C1C22");
                Set(Fg, "#D4D4D8"); Set(FgDim, "#71717A"); Set(FgFaint, "#3F3F46");
                Set(LineNo, "#3F3F4A"); Set(CurrentLine, "#16161C"); Set(Selection, "#4522C55E");
                Set(Accent, "#22C55E"); Set(Heading, "#F4F4F5"); Set(Section, "#E4E4E7"); Set(Done, "#5B5B66");
                Set(Popup, "#17171D"); Set(Hover, "#26262F"); Set(Input, "#0E0E11"); Set(AccentSoft, "#3322C55E");
                Set(Bang, "#FF5C5C"); Set(Slash, "#4ADE80"); Set(Star, "#34D399"); Set(Question, "#A78BFA");
                Set(Todo, "#FBBF24"); Set(Arrow, "#C084FC"); Set(Back, "#22D3EE"); Set(Dash, "#94A3B8");
                Set(Mention, "#FBBF24"); Set(HashTag, "#22D3EE"); Set(Date, "#60A5FA"); Set(Code, "#FDBA74"); Set(Link, "#60A5FA");
                Set(BoxOpen, "#6B6B78"); Set(BoxDone, "#22C55E"); Set(BoxDoing, "#FBBF24"); Set(BoxCancel, "#EF4444");
            }
            else
            {
                Set(Bg, "#FFFFFF"); Set(Chrome, "#F3F3F6"); Set(ChromeBorder, "#E2E2E8");
                Set(Fg, "#1F2328"); Set(FgDim, "#6E6E7A"); Set(FgFaint, "#B4B4BF");
                Set(LineNo, "#A3A3AE"); Set(CurrentLine, "#F4F4F8"); Set(Selection, "#3516A34A");
                Set(Accent, "#16A34A"); Set(Heading, "#14241A"); Set(Section, "#26262E"); Set(Done, "#9B9BA6");
                Set(Popup, "#FFFFFF"); Set(Hover, "#EDEDF3"); Set(Input, "#FAFAFC"); Set(AccentSoft, "#2816A34A");
                Set(Bang, "#D92D33"); Set(Slash, "#16A34A"); Set(Star, "#059669"); Set(Question, "#7C3AED");
                Set(Todo, "#C2860A"); Set(Arrow, "#9333EA"); Set(Back, "#0891B2"); Set(Dash, "#64748B");
                Set(Mention, "#B7791F"); Set(HashTag, "#0E7490"); Set(Date, "#2563EB"); Set(Code, "#C2410C"); Set(Link, "#2563EB");
                Set(BoxOpen, "#9A9AA6"); Set(BoxDone, "#16A34A"); Set(BoxDoing, "#D99A00"); Set(BoxCancel, "#DC2626");
            }
        }

        static readonly System.Collections.Generic.Dictionary<Color, SolidColorBrush> Frozen = new System.Collections.Generic.Dictionary<Color, SolidColorBrush>();

        /// Frozen copy for the text editor (AvalonEdit freezes brushes it is given, which would lock the shared ones).
        public static Brush F(Brush b)
        {
            if (!(b is SolidColorBrush s) || s.IsFrozen && !ReferenceEquals(s, Bg)) return b;
            if (!Frozen.TryGetValue(s.Color, out var f)) { f = new SolidColorBrush(s.Color); f.Freeze(); Frozen[s.Color] = f; }
            return f;
        }

        static void Set(SolidColorBrush b, string hex) => b.Color = (Color)ColorConverter.ConvertFromString(hex);

        /// Switches theme for the whole app: brushes, menu/scrollbar styles, title bars and editors.
        public static void Switch(bool light)
        {
            Apply(light);
            Workspace.Settings.Theme = light ? "light" : "dark";
            Workspace.Settings.Save();
            if (Application.Current != null) Styles.Install(Application.Current.Resources);
            TaskPad.Code.Refresh();
            foreach (Window w in Application.Current.Windows) TaskWindow.ApplyDarkTitleBar(w);
            foreach (var v in Workspace.AllViews)
            {
                v.ApplyTheme();
                v.Refresh();
            }
            foreach (var w in Workspace.Windows) { foreach (var g in w.Groups) { g.RefreshHeaders(); g.UpdateToggles(); } w.Explorer.ApplyItemStyle(); w.RefreshWelcome(); }
        }
    }
}
