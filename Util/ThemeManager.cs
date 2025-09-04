using PT200Emulator.Core;
using PT200Emulator.Util;
using System.Windows.Controls;
using static PT200Emulator.Core.PT200State;

public class ThemeManager
{
    private readonly AttributeTracker attr;
    private readonly Canvas canvas;
    private readonly TextBlock statusText;
    private readonly TextBlock clockText;
    private DisplayType currentType;

    public ThemeManager(AttributeTracker attr, Canvas canvas, TextBlock statusText, TextBlock clockText)
    {
        this.attr = attr;
        this.canvas = canvas;
        this.statusText = statusText;
        this.clockText = clockText;
    }

    public void Apply(DisplayType type)
    {
        currentType = type;

        var fg = DisplayTheme.GetForeground(type);
        var bg = DisplayTheme.GetBackground(type);
        var invFg = DisplayTheme.GetInvertedForeground(type);
        var invBg = DisplayTheme.GetInvertedBackground(type);

        attr.SetForeground(fg);
        attr.SetBackground(bg);

        canvas.Background = bg;
        statusText.Foreground = invFg;
        statusText.Background = invBg;
        clockText.Foreground = invFg;
        clockText.Background = invBg;

        Logger.Log($"[ThemeManager] FG={DisplayTheme.GetForeground(type)}, BG={DisplayTheme.GetBackground(type)}", Logger.LogLevel.Info);
    }

    public DisplayType Current => currentType;
}