using PT200Emulator.Infrastructure.Logging;
using System.Reflection;
using System.Windows.Navigation;

namespace PT200Emulator.Core.Emulator
{
    public class ScreenBuffer : IScreenBuffer
    {
        private readonly char[,] _chars;
        private readonly StyleInfo[,] _styles;

        public event Action BufferUpdated;

        public int Rows => _chars.GetLength(0);
        public int Cols => _chars.GetLength(1);

        public int CursorRow { get; private set; }
        public int CursorCol { get; private set; }

        public StyleInfo CurrentStyle { get; set; } = new StyleInfo();

        private const int TabSize = 8;

        public ScreenBuffer(int rows, int cols)
        {
            if (rows <= 0 || cols <= 0) throw new ArgumentOutOfRangeException();
            _chars = new char[rows, cols];
            _styles = new StyleInfo[rows, cols];
            ClearScreen();
        }

        public char GetChar(int row, int col)
        {
            if ((uint)row >= (uint)Rows || (uint)col >= (uint)Cols) return ' ';
            return _chars[row, col];
        }

        public StyleInfo GetStyle(int row, int col)
        {
            if ((uint)row >= (uint)Rows || (uint)col >= (uint)Cols) return new StyleInfo();
            return _styles[row, col];
        }

        public void WriteChar(char ch)
        {
            if (ch == '\x1B') return; // ESC ska inte ritas

            if ((uint)CursorRow >= (uint)Rows || (uint)CursorCol >= (uint)Cols)
                return;

            _chars[CursorRow, CursorCol] = ch;
            _styles[CursorRow, CursorCol] = CurrentStyle.Clone();
            AdvanceCursor();
            this.LogTrace("[WriteChar] BufferUpdated fired");
            BufferUpdated?.Invoke();
        }

        public void SetCursorPosition(int row, int col)
        {
            CursorRow = Math.Clamp(row, 0, Rows - 1);
            CursorCol = Math.Clamp(col, 0, Cols - 1);
            this.LogTrace("[SetCursorPosition] BufferUpdated fired");
            BufferUpdated?.Invoke();
        }

        public void CarriageReturn()
        {
            CursorCol = 0;
            this.LogTrace("[CarriageReturn] BufferUpdated fired");
            BufferUpdated?.Invoke();
        }

        public void LineFeed()
        {
            CursorRow++;
            if (CursorRow >= Rows)
            {
                ScrollUp();
                CursorRow = Rows - 1;
            }
            this.LogTrace("[LineFeed] BufferUpdated fired");
            BufferUpdated?.Invoke();
        }

        public void Backspace()
        {
            if (CursorCol > 0)
            {
                CursorCol--;
                this.LogTrace("[Backspace] BufferUpdated fired");
                BufferUpdated?.Invoke();
            }
        }

        public void Tab()
        {
            int nextStop = ((CursorCol / TabSize) + 1) * TabSize;
            CursorCol = Math.Min(nextStop, Cols - 1);
            this.LogTrace("[Tab] BufferUpdated fired");
            BufferUpdated?.Invoke();
        }

        public void ClearScreen()
        {
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    _chars[r, c] = ' ';
                    _styles[r, c] = new StyleInfo();
                }

            CursorRow = 0;
            CursorCol = 0;
            this.LogTrace("[ClearScreen] BufferUpdated fired");
            BufferUpdated?.Invoke();
        }

        public void ClearLine(int row)
        {
            if ((uint)row >= (uint)Rows) return;
            for (int c = 0; c < Cols; c++)
            {
                _chars[row, c] = ' ';
                _styles[row, c] = new StyleInfo();
            }
            this.LogTrace("[ClearLine] BufferUpdated fired");
            BufferUpdated?.Invoke();
        }

        private void AdvanceCursor()
        {
            CursorCol++;
            if (CursorCol >= Cols)
            {
                CursorCol = 0;
                CursorRow++;
                if (CursorRow >= Rows)
                {
                    ScrollUp();
                    CursorRow = Rows - 1;
                }
            }
            this.LogTrace("[AdvanceCursor] BufferUpdated fired");
            BufferUpdated?.Invoke();
        }

        private void ScrollUp()
        {
            for (int r = 1; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    _chars[r - 1, c] = _chars[r, c];
                    _styles[r - 1, c] = _styles[r, c];
                }

            for (int c = 0; c < Cols; c++)
            {
                _chars[Rows - 1, c] = ' ';
                _styles[Rows - 1, c] = new StyleInfo();
            }
            this.LogTrace("[ScrollUp] BufferUpdated fired");
            BufferUpdated?.Invoke();
        }
    }

    public class StyleInfo
    {
        public ConsoleColor Foreground { get; set; } = ConsoleColor.Gray;
        public ConsoleColor Background { get; set; } = ConsoleColor.Black;
        public bool Blink { get; set; } = false;
        public bool Bold { get; set; } = false;

        public void Reset()
        {
            Foreground = ConsoleColor.Gray;
            Background = ConsoleColor.Black;
            Blink = false;
            Bold = false;
        }

        public StyleInfo Clone()
        {
            return new StyleInfo
            {
                Foreground = this.Foreground,
                Background = this.Background,
                Blink = this.Blink,
                Bold = this.Bold
            };
        }
    }
}