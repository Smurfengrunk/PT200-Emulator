using PT200Emulator.Util;
using System.Windows.Media;
using static ScreenBuffer;

public class AttributeTracker
{
    private Brush foreground = Brushes.White;
    private Brush background = Brushes.Black;
    private bool reverseVideo = false;
    private bool blink = false;

    public void SetForeground(Brush brush) => foreground = brush;
    public void SetBackground(Brush brush) => background = brush;
    public void SetReverse(bool enable) => reverseVideo = enable;
    public void SetBlink(bool enable) => blink = enable;

    public Brush CurrentForeground => reverseVideo ? background : foreground;
    public Brush CurrentBackground => reverseVideo ? foreground : background;
    public bool CurrentBlink => blink;

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
        Logger.Log($"[Attr] ({row},{col}) = '{ch}' | FG={CurrentForeground}, BG={CurrentBackground}, Blink={blink}, Reverse={reverseVideo}", Logger.LogLevel.Debug);
    }
}