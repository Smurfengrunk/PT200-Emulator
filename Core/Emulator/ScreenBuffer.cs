using PT200Emulator.Infrastructure.Logging;
using PT200Emulator.UI;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace PT200Emulator.Core.Emulator
{
    public class ScreenBuffer : IScreenBuffer
    {
        private char[,] _chars;
        private StyleInfo[,] _styles;

        public event Action BufferUpdated;
        public event Action<int, int> CursorMoved;

        public int GetBufferUpdatedHandlerCount()
        {
            return BufferUpdated?.GetInvocationList().Length ?? 0;
        }

        public int Rows => _chars.GetLength(0);
        public int Cols => _chars.GetLength(1);

        public int CursorRow { get; private set; }
        public int CursorCol { get; private set; }

        public StyleInfo CurrentStyle { get; set; } = new StyleInfo();

        private const int TabSize = 8;

        internal int _updateDepth;
        private bool _dirty;
        private CancellationTokenSource _idleCts;
        private readonly TimeSpan _idleDelay = TimeSpan.FromMilliseconds(8);

        public IDisposable BeginUpdate() => new UpdateScope(this);

        private void EnterUpdate() => Interlocked.Increment(ref _updateDepth);

        private readonly struct UpdateScope : IDisposable
        {
            private readonly ScreenBuffer _b;
            public UpdateScope(ScreenBuffer b) { _b = b; _b.EnterUpdate(); }
            public void Dispose() => _b.LeaveUpdate();
        }

        // Fält
        private CancellationTokenSource _burstCts;
        private IDisposable _burstScope;
        private bool _burstActive;

        public struct ScreenCell
        {
            public char Char;
            public Brush Foreground;
            public Brush Background;
        }
        private readonly ScreenCell[,] _mainBuffer;
        private readonly ScreenCell[] _systemLineBuffer;
        private bool _inSystemLine = false;

        public void MarkDirty()
        {
            _dirty = true;
        }

        public void ClearDirty()
        {
            _dirty = false;
        }

        public bool GetDirty()
        {
            return _dirty;
        }

        /// <summary>
        /// Används av alla muterande operationer i bufferten.
        /// </summary>
        private void Mutate(Action action)
        {
            action();
            _dirty = true;

            // Om vi inte är inne i ett Begin/EndUpdate-block → schemalägg idle-flush
            if (_updateDepth == 0)
                ScheduleIdleFlush();
        }

        private void LeaveUpdate()
        {
            if (Interlocked.Decrement(ref _updateDepth) == 0 && _dirty)
            {
                // Schemalägg flush istället för att göra den direkt
                ScheduleIdleFlush();
            }
        }

        public void ForceEndUpdate()
        {
            while (_updateDepth > 0)
            {
                _updateDepth--;
            }
            try
            {
                _burstCts?.Cancel();
            }
            catch
            {
            }
            _burstCts = null;

            _burstScope?.Dispose();
            _burstScope = null;

            _burstActive = false;

            _dirty = true;
        }

        private void ScheduleIdleFlush()
        {
            _idleCts?.Cancel();
            _idleCts = new CancellationTokenSource();
            var token = _idleCts.Token;

            Task.Delay(_idleDelay, token).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested && _dirty && _updateDepth == 0)
                {
                    Application.Current.Dispatcher.Invoke(
                        () => BufferUpdated?.Invoke(),
                        DispatcherPriority.Render
                    );
                }
            }, TaskScheduler.Default);
        }

        public ScreenBuffer(int rows, int cols, Brush defaultFg, Brush defaultBg)
        {
            _mainBuffer = new ScreenCell[rows, cols];
            _systemLineBuffer = new ScreenCell[cols];
            if (rows <= 0 || cols <= 0) throw new ArgumentOutOfRangeException();
            _chars = new char[rows, cols];
            _styles = new StyleInfo[rows, cols];
            // Initiera med blanktecken och standardfärger
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    _mainBuffer[r, c] = new ScreenCell { Char = ' ', Foreground = defaultFg, Background = defaultBg };

            for (int c = 0; c < cols; c++)
                _systemLineBuffer[c] = new ScreenCell { Char = ' ', Foreground = defaultFg, Background = defaultBg };

            ClearScreen();
            this.LogTrace($"[SCREENBUFFER] ScreenBuffer hash: {this.GetHashCode()}");
            this.LogStackTrace();
        }

        public void Resize(int rows, int cols)
        {
            ForceEndUpdate();

            Mutate(() =>
            {
                if (rows <= 0 || cols <= 0) throw new ArgumentOutOfRangeException();

                var newChars = new char[rows, cols];
                var newStyles = new StyleInfo[rows, cols];

                // Kopiera så mycket som får plats från gamla bufferten
                for (int r = 0; r < Math.Min(Rows, rows); r++)
                    for (int c = 0; c < Math.Min(Cols, cols); c++)
                    {
                        newChars[r, c] = _chars[r, c];
                        newStyles[r, c] = _styles[r, c];
                    }
                // Kopiera så mycket som får plats från gamla bufferten
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        if (newStyles[r, c] == null)
                            newStyles[r, c] = new StyleInfo();
                    }

                _chars = newChars;
                _styles = newStyles;

                CursorRow = Math.Min(CursorRow, rows - 1);
                CursorCol = Math.Min(CursorCol, cols - 1);

                _dirty = true;

                this.LogTrace($"[SCREENBUFFER] BufferUpdated invoked – handlers: {BufferUpdated?.GetInvocationList().Length ?? 0}, hash: {this.GetHashCode()}");
            });
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
            Mutate(() =>
            {
                if (ch == '\x1B') return; // ESC ska inte ritas

                if (_inSystemLine)
                {
                    if ((uint)CursorCol >= (uint)Cols) return;

                    _systemLineBuffer[CursorCol] = new ScreenCell
                    {
                        Char = ch,
                        Foreground = CurrentStyle.brBackGround,
                        Background = CurrentStyle.brForeGround
                    };

                    CursorCol++;
                    if (CursorCol >= Cols) CursorCol = Cols - 1;
                }
                else
                {
                    if ((uint)CursorRow >= (uint)Rows || (uint)CursorCol >= (uint)Cols)
                        return;

                    _mainBuffer[CursorRow, CursorCol] = new ScreenCell
                    {
                        Char = ch,
                        Foreground = CurrentStyle.brForeGround,
                        Background = CurrentStyle.brBackGround
                    };

                    _chars[CursorRow, CursorCol] = ch;
                    _styles[CursorRow, CursorCol] = CurrentStyle.Clone();

                    AdvanceCursor();
                }

                this.LogTrace($"[WriteChar] BufferUpdated fired from ScreenWriter {this.GetHashCode()}");
            });
        }


        public void SetCursorPosition(int row, int col)
        {
            this.LogTrace($"[CURSOR] Pos=({CursorRow},{CursorCol})");
            CursorRow = Math.Clamp(row, 0, Rows - 1);
            CursorCol = Math.Clamp(col, 0, Cols - 1);
            CursorMoved?.Invoke(CursorRow, CursorCol);
            MarkDirty();
        }

        public void CarriageReturn()
        {
            CursorCol = 0;
            MarkDirty();
        }

        public void LineFeed()
        {
            CursorRow++;
            if (CursorRow >= Rows)
            {
                ScrollUp();
                CursorRow = Rows - 1;
            }
            MarkDirty();
        }

        public void Backspace()
        {
            if (CursorCol > 0)
            {
                CursorCol--;
                MarkDirty();
            }
        }

        public void Tab()
        {
            int nextStop = ((CursorCol / TabSize) + 1) * TabSize;
            CursorCol = Math.Min(nextStop, Cols - 1);
            MarkDirty();
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
            MarkDirty();
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
            MarkDirty();
        }

        private void AdvanceCursor()
        {
            Mutate(() =>
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
            });
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
            MarkDirty();
        }
    }

    public class StyleInfo
    {
        public ConsoleColor Foreground { get; set; } = ConsoleColor.Gray;
        public ConsoleColor Background { get; set; } = ConsoleColor.Black;
        public Brush brForeGround => ConsoleColorToBrush(Foreground);
        public Brush brBackGround => ConsoleColorToBrush(Background);
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

        private Brush ConsoleColorToBrush(ConsoleColor color)
        {
            return color switch
            {
                ConsoleColor.Black => Brushes.Black,
                ConsoleColor.DarkBlue => Brushes.DarkBlue,
                ConsoleColor.DarkGreen => Brushes.DarkGreen,
                ConsoleColor.DarkCyan => Brushes.DarkCyan,
                ConsoleColor.DarkRed => Brushes.DarkRed,
                ConsoleColor.DarkMagenta => Brushes.DarkMagenta,
                ConsoleColor.DarkYellow => Brushes.Olive,
                ConsoleColor.Gray => Brushes.Gray,
                ConsoleColor.DarkGray => Brushes.DarkGray,
                ConsoleColor.Blue => Brushes.Blue,
                ConsoleColor.Green => Brushes.Green,
                ConsoleColor.Cyan => Brushes.Cyan,
                ConsoleColor.Red => Brushes.Red,
                ConsoleColor.Magenta => Brushes.Magenta,
                ConsoleColor.Yellow => Brushes.Yellow,
                ConsoleColor.White => Brushes.White,
                _ => Brushes.Transparent
            };
        }
    }
}