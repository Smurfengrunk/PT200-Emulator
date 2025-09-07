using PT200Emulator.UI;
using PT200Emulator.Util;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using static PT200Emulator.Util.Logger;

namespace PT200Emulator.Core
{
    public class TerminalRenderer
    {
        private readonly double cellWidth = 8;
        private readonly double cellHeight = 16;
        private Brush foreground, background;
        private readonly Typeface typeface = new Typeface("Consolas");

        public void Render(Canvas canvas, ScreenBuffer buffer, PT200State state)
        {
            Logger.Log($"🎨 Render startar – bufferstorlek: {buffer.Rows}x{buffer.Cols}", LogLevel.Trace);
            canvas.Children.Clear();

            for (int row = 0; row < buffer.Rows; row++)
            {
                for (int col = 0; col < buffer.Cols; col++)
                {
                    var cell = buffer.GetCell(row, col);
                    foreground = buffer.GetBrushForColor(cell.ForegroundColor);
                    background = buffer.GetBrushForColor(cell.BackgroundColor);
                    if (cell == null || cell.Character == '\0') continue;
                    Logger.Log($"🔍 Cell[{row},{col}] = '{cell?.Character}'", LogLevel.Trace);

                    var text = new FormattedText(
                        cell.Character.ToString(),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        typeface,
                        14,
                        foreground,
                        1.0);

                    var geometry = text.BuildGeometry(new System.Windows.Point(0, 0));
                    /*var path = new Path
                    {
                        Data = geometry,
                        Fill = foregroundBrush
                    };

                    Canvas.SetLeft(path, col * cellWidth);
                    Canvas.SetTop(path, row * cellHeight);
                    canvas.Children.Add(path);*/
                    var textBlock = new TextBlock
                    {
                        Text = cell.Character.ToString(),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 14,
                        Foreground = foreground,
                        Background = background,
                        Width = cellWidth,
                        Height = cellHeight,
                        TextAlignment = System.Windows.TextAlignment.Center
                    };
                    Canvas.SetLeft(textBlock, col * cellWidth);
                    Canvas.SetTop(textBlock, row * cellHeight);
                    canvas.Children.Add(textBlock);
                }
            }

            // Cursor
            if (state.cursorVisible)
            {
                var cursorRect = new Rectangle
                {
                    Width = cellWidth,
                    Height = cellHeight,
                    Fill = Brushes.Gray,
                    Opacity = 0.5
                };
                Canvas.SetLeft(cursorRect, buffer.CursorCol * cellWidth);
                Canvas.SetTop(cursorRect, buffer.CursorRow * cellHeight);
                canvas.Children.Add(cursorRect);
            }

        }
    }

    public class ControlCharacterHandler
    {
        public event Action<byte[]> RawOutput;
        public event Action BreakReceived;
        public event Action BellReceived;

        public enum ControlCharacterResult
        {
            NotHandled,
            RawOutput,
            Break,
            Bell,
            Abort,
            FormFeed,
            Null,
            LineFeed,
            CarriageReturn
        }
        public byte[] LastRawBytes { get; private set; }

        public ControlCharacterResult Handle(char ch)
        {
            LastRawBytes = new byte[] { (byte)ch };

            return ch switch
            {
                (char)0x07 => ControlCharacterResult.Bell,
                (char)0x10 => ControlCharacterResult.Break,
                (char)0x03 => ControlCharacterResult.Abort,
                (char)0x0C => ControlCharacterResult.FormFeed,
                (char)0x00 => ControlCharacterResult.Null,
                (char)0x0A => ControlCharacterResult.LineFeed,     // LF
                (char)0x0D => ControlCharacterResult.CarriageReturn, // CR
                _ => ControlCharacterResult.NotHandled
            };
        }
        // För att undvika varningar om oanvända händelser
        private void TouchEvents()
        {
            _ = RawOutput;
            _ = BreakReceived;
            _ = BellReceived;
        }

    }
}
