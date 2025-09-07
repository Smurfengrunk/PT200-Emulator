using System.Windows.Media;

public static class ColorThemeManager
{
    private static readonly Dictionary<int, Brush> ansiForeground = new()
    {
        [30] = Brushes.Black,
        [31] = Brushes.Red,
        [32] = Brushes.Green,
        [33] = Brushes.Yellow,
        [34] = Brushes.Blue,
        [35] = Brushes.Magenta,
        [36] = Brushes.Cyan,
        [37] = Brushes.White,
        [90] = Brushes.DarkGray,
        [91] = Brushes.OrangeRed,
        [92] = Brushes.LightGreen,
        [93] = Brushes.LightYellow,
        [94] = Brushes.LightBlue,
        [95] = Brushes.Plum,
        [96] = Brushes.LightCyan,
        [97] = Brushes.LightGray
    };

    private static readonly Dictionary<int, Brush> ansiBackground = new()
    {
        [40] = Brushes.Black,
        [41] = Brushes.DarkRed,
        [42] = Brushes.DarkGreen,
        [43] = Brushes.Olive,
        [44] = Brushes.DarkBlue,
        [45] = Brushes.DarkMagenta,
        [46] = Brushes.DarkCyan,
        [47] = Brushes.Gray
    };

    public static Brush GetForeground(int code) => ansiForeground.TryGetValue(code, out var brush) ? brush : Brushes.White;
    public static Brush GetBackground(int code) => ansiBackground.TryGetValue(code, out var brush) ? brush : Brushes.Black;
}