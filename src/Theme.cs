using System.Windows.Media;

namespace TaskPad
{
    public static class Theme
    {
        public static readonly SolidColorBrush
            Bg = B("#0E0E11"),
            Chrome = B("#09090B"),
            ChromeBorder = B("#1C1C22"),
            Fg = B("#D4D4D8"),
            FgDim = B("#71717A"),
            FgFaint = B("#3F3F46"),
            LineNo = B("#3F3F4A"),
            CurrentLine = B("#16161C"),
            Selection = B("#553F3FA8"),
            Accent = B("#7C6CF6"),
            Heading = B("#EDE9FE"),
            Section = B("#E4E4E7"),
            Done = B("#5B5B66"),

            // better-comments palette
            Bang = B("#FF5C5C"),
            Slash = B("#4ADE80"),
            Star = B("#34D399"),
            Question = B("#A78BFA"),
            Todo = B("#FBBF24"),
            Arrow = B("#C084FC"),
            Back = B("#22D3EE"),
            Dash = B("#94A3B8"),

            // inline tokens
            Mention = B("#FBBF24"),
            HashTag = B("#22D3EE"),
            Date = B("#60A5FA"),
            Code = B("#FDBA74"),
            Link = B("#60A5FA"),

            // checkbox states
            BoxOpen = B("#6B6B78"),
            BoxDone = B("#7C6CF6"),
            BoxDoing = B("#FBBF24"),
            BoxCancel = B("#EF4444");

        static SolidColorBrush B(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }
}
