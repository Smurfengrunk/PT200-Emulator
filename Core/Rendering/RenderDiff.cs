namespace PT200Emulator.Core.Rendering
{
    public enum RenderOp
    {
        PutChar,        // Sätt en enstaka cell
        MoveCursor,     // Flytta markören (om renderer använder det)
        EraseLine,      // Rensa aktuell rad (helt eller delvis)
        EraseDisplay,   // Rensa hela skärmen
        InvalidateAll   // Tvinga full omritning
    }

    public sealed class RenderDiff
    {
        public RenderOp Op { get; }
        public int Row { get; }
        public int Col { get; }
        public char? Ch { get; }
        public TextAttributes Attr { get; }

        private RenderDiff(RenderOp op, int row, int col, char? ch, TextAttributes attr)
        {
            Op = op;
            Row = row;
            Col = col;
            Ch = ch;
            Attr = attr;
        }

        // Fabriksmetoder för vanligaste fallen
        public static RenderDiff PutChar(int row, int col, char ch, TextAttributes attr = null)
            => new(RenderOp.PutChar, row, col, ch, attr);

        public static RenderDiff MoveCursor(int row, int col)
            => new(RenderOp.MoveCursor, row, col, null, null);

        public static RenderDiff EraseLine()
            => new(RenderOp.EraseLine, 0, 0, null, null);

        public static RenderDiff EraseDisplay()
            => new(RenderOp.EraseDisplay, 0, 0, null, null);

        public static RenderDiff InvalidateAll()
            => new(RenderOp.InvalidateAll, 0, 0, null, null);
    }

    // Kan byggas ut med färger, intensitet, blink, etc.
    public sealed class TextAttributes
    {
        public bool Bold { get; init; }
        public bool Underline { get; init; }
        public bool Inverse { get; init; }
        public ConsoleColor? Foreground { get; init; }
        public ConsoleColor? Background { get; init; }

        public static readonly TextAttributes Default = new();
    }
}