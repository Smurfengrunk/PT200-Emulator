using PT200Emulator.Util;
using System.Windows.Media;
using static ScreenBuffer;

public class AttributeTracker
{
    public void SetForeground(Brush brush) => foreground = brush;
    public void SetBackground(Brush brush) => background = brush;
    public void SetReverse(bool enable) => reverseVideo = enable;
    public void SetBlink(bool enable) => blink = enable;

    public Brush CurrentForeground => reverseVideo ? background : foreground;
    public Brush CurrentBackground => reverseVideo ? foreground : background;
    public bool CurrentBlink => blink;

    private Brush foreground = Brushes.White;
    private Brush background = Brushes.Black;
    private string foregroundName = "White";
    private string backgroundName = "Black";
    private bool reverseVideo = false;
    private bool blink = false;

    public void ApplyForeground(Brush brush)
    {
        var foregroundBrush = brush;
        if (brush is SolidColorBrush solid)
            foregroundName = solid.Color.ToString();
        else
            foregroundName = brush?.ToString() ?? "null";
    }

    public void ApplyBackground(Brush brush)
    {
        var backgroundBrush = brush;
        if (brush is SolidColorBrush solid)
            backgroundName = solid.Color.ToString();
        else
            backgroundName = brush?.ToString() ?? "null";
    }

    public void EnableReverseVideo(bool enable) => reverseVideo = enable;
    public void EnableBlink(bool enable) => blink = enable;

    public Cell CreateCell(char ch)
    {
        return new Cell
        {
            Character = ch,
            Foreground = CurrentForeground,
            Background = CurrentBackground,
            Blink = CurrentBlink
        };
    }

    public void LogAttributes(int row, int col, char ch)
    {
        string fg = reverseVideo ? backgroundName : foregroundName;
        string bg = reverseVideo ? foregroundName : backgroundName;

        Logger.Log(
            $"[Attr] ({row},{col}) = '{ch}' | FG={fg}, BG={bg}, Blink={blink}, Reverse={reverseVideo}",
            Logger.LogLevel.Debug
        );
    }

}