using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using PT200Emulator.Core.Parser;
using PT200Emulator.Infrastructure.Logging;
using PT200Emulator.UI;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace PT200Emulator.Core.Emulator
{
    public class ScreenBuffer : IScreenBuffer
    {
        private const int TabSize = 8;

        private char[,] _chars;

        public event Action BufferUpdated;
        public event Action<int, int> CursorMoved;
        internal int _updateDepth;
        private bool _dirty;
        private bool _hasFlushed = false;
        private bool _inSystemLine = false;
        private CancellationTokenSource _idleCts;
        private readonly TimeSpan _idleDelay = TimeSpan.FromMilliseconds(8);
        private readonly CharTableManager charTableManager;
        private readonly TextLogger textLogger;
        private FlushTrigger flushTrigger;
        public int Rows => _chars.GetLength(0);
        public int Cols => _chars.GetLength(1);
        private int ScrollTop, ScrollBottom;
        public int CursorRow { get; private set; }
        public int CursorCol { get; private set; }
        public RowLockManager RowLocks { get; } = new();

        public StyleInfo CurrentStyle { get; set; } = new StyleInfo();
        private CancellationTokenSource _burstCts;
        private IDisposable _burstScope;

        public IDisposable BeginUpdate() => new UpdateScope(this);

        private void EnterUpdate() => Interlocked.Increment(ref _updateDepth);

        public int GetBufferUpdatedHandlerCount()
        {
            return BufferUpdated?.GetInvocationList().Length ?? 0;
        }



        private readonly struct UpdateScope : IDisposable
        {
            private readonly ScreenBuffer _b;
            public UpdateScope(ScreenBuffer b) { _b = b; _b.EnterUpdate(); }
            public void Dispose() => _b.LeaveUpdate();
        }

        // Fält
        public struct ScreenCell
        {
            public char Char;
            public Brush Foreground;
            public Brush Background;
            public StyleInfo Style;
        }
        private readonly ScreenCell[,] _mainBuffer;
        private readonly ScreenCell[] _systemLineBuffer;
        public ScreenCell this[int row, int col]
        {
            get => _mainBuffer[row, col];
            set => _mainBuffer[row, col] = value;
        }

        public ScreenCell[,] AllCells()
        {
            return _mainBuffer;
        }

        public bool InSystemLine()
        {
            if (_inSystemLine) return true; else return false;
        }

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

            //_burstActive = false;

            _dirty = true;
        }

        private void ScheduleIdleFlush()
        {
            _idleCts?.Cancel();
            _idleCts = new CancellationTokenSource();
            var token = _idleCts.Token;

            Task.Delay(_idleDelay, token).ContinueWith(_ =>
            {
                this.LogTrace($"[SCHEDULEDIDLEFLUSH] BufferUpdated invoked – handlers: {BufferUpdated?.GetInvocationList().Length ?? 0}, hash: {this.GetHashCode()}");
                if (!token.IsCancellationRequested && _dirty && _updateDepth == 0)
                {
                    this.LogTrace($"[SCHEDULEIDLEFLUSH] Dags för en uppdatering");
                    Application.Current.Dispatcher.Invoke(
                        () => BufferUpdated?.Invoke(),
                        DispatcherPriority.Render
                    );
                }
            }, TaskScheduler.Default);
            flushTrigger.ForceFlush("ScheduledIdFlush");
            _hasFlushed = true;
         }

        public ScreenBuffer(int rows, int cols, Brush defaultFg, Brush defaultBg, string basePath)
        {
            _mainBuffer = new ScreenCell[rows, cols];
            _systemLineBuffer = new ScreenCell[cols];
            if (rows <= 0 || cols <= 0) throw new ArgumentOutOfRangeException();
            _chars = new char[rows, cols];
            var g0path = Path.Combine(basePath, "data", "chartables", "g0.json");
            var g1path = Path.Combine(basePath, "data", "chartables", "g1.json");
            charTableManager = new CharTableManager(g0path, g1path);
            textLogger = new TextLogger(MainWindow.Logger);
            flushTrigger = new FlushTrigger(textLogger);
            // Initiera med blanktecken och standardfärger
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    _mainBuffer[r, c] = new ScreenCell { Char = ' ', Foreground = defaultFg, Background = defaultBg, Style = new StyleInfo() };
                    //this.LogDebug($"LowIntensity flag @({r}, {c}) = {_mainBuffer[r, c].Style.LowIntensity}");
                }

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
                        newStyles[r, c] = _mainBuffer[r, c].Style;
                    }
                // Kopiera så mycket som får plats från gamla bufferten
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        if (newStyles[r, c] == null)
                            _mainBuffer[r, c].Style = new StyleInfo();
                    }
                
                _chars = newChars;

                CursorRow = Math.Min(CursorRow, rows - 1);
                CursorCol = Math.Min(CursorCol, cols - 1);

                _dirty = true;
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
            return _mainBuffer[row, col].Style;
        }

        public void WriteChar(char ch)
        {
            if (!_hasFlushed)
                flushTrigger.ForceFlush("Första teckeninmatning – inittext börjar");
            var glyph = charTableManager.Translate((byte)ch);
            textLogger.LogChar(CursorRow, CursorCol, glyph);
            //this.LogTrace($"[CHAR] Pos=({CursorRow},{CursorCol}), Byte=0x{(byte)ch:X2}, Glyph='{glyph}', Attr=FG:{CurrentStyle.Foreground}, BG:{CurrentStyle.Background}");
            flushTrigger.OnCharWritten();
            Mutate(() =>
            {
                if (ch == '\x1B') return; // ESC ska inte ritas

                if (_inSystemLine)
                {
                    if ((uint)CursorCol >= (uint)Cols) return;

                    _systemLineBuffer[CursorCol] = new ScreenCell
                    {
                        Char = ch,
                        Foreground = CurrentStyle.Background,
                        Background = CurrentStyle.Foreground
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
                        Foreground = CurrentStyle.ReverseVideo ? CurrentStyle.Background : CurrentStyle.Foreground,
                        Background = CurrentStyle.ReverseVideo ? CurrentStyle.Foreground : CurrentStyle.Background
                    };

                    _chars[CursorRow, CursorCol] = ch;
                    _mainBuffer[CursorRow, CursorCol].Style = CurrentStyle.Clone();

                    AdvanceCursor();
                }
            });
        }


        public void SetCursorPosition(int row, int col)
        {
            this.LogTrace($"[CURSOR] Pos=({CursorRow},{CursorCol})");
            CursorRow = Math.Clamp(row, 0, Rows - 1);
            CursorCol = Math.Clamp(col, 0, Cols - 1);
            CursorMoved?.Invoke(CursorRow, CursorCol);
            MarkDirty();
            if (!_hasFlushed && row == 22 && col == 0)
            {
                flushTrigger.ForceFlush("Initsekvens avslutad");
                _hasFlushed = true;
            }
            else flushTrigger.OnCursorMoved(CursorRow, CursorCol);
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
                    _mainBuffer[r, c].Style = new StyleInfo();
                }

            CursorRow = 0;
            CursorCol = 0;
            MarkDirty();
        }

        public void ClearLine(int row)
        {
            if ((uint)row >= (uint)Rows) return;
            for (int c = 0; c < Cols; c++)
            {
                _chars[row, c] = ' ';
                _mainBuffer[row, c].Style = new StyleInfo();
            }
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
                    _mainBuffer[r - 1, c].Style = _mainBuffer[r, c].Style;
                }

            for (int c = 0; c < Cols; c++)
            {
                _chars[Rows - 1, c] = ' ';
                _mainBuffer[Rows - 1, c].Style = new StyleInfo();
            }
            MarkDirty();
        }

        public void SetScrollRegion(int top, int bottom)
        {
            // Sätt övre och nedre gräns för scrollområdet
            ScrollTop = top;
            ScrollBottom = bottom;
            // Validera att top < bottom och inom skärmens höjd
        }

        public void ResetScrollRegion()
        {
            ScrollTop = 0;
            ScrollBottom = Rows -1;
        }

        public ScreenCell GetCell(int row, int col)
        {
            return _mainBuffer[row, col];
        }

        public void SetCell(int row, int col, ScreenCell cell)
        {
            _mainBuffer[row, col] = cell;
        }

        public void SetStyle(int row, int col,  StyleInfo style)
        {
            _mainBuffer[row, col].Style = style;
        }
    }

    public class StyleInfo
    {
        public Brush Foreground { get; set; } = Brushes.LimeGreen;
        public Brush Background { get; set; } = Brushes.Black;

        public bool Blink { get; set; } = false;
        public bool Bold { get; set; } = false;
        public bool Underline { get; set; } = false;
        public bool ReverseVideo { get; set; } = false;
        public bool LowIntensity { get; set; } = false;
        public bool StrikeThrough { get; set; } = false;

        // PT200-specifika
        public bool Transparent { get; set; } = false;
        public bool VisualAttributeLock { get; set; } = false;

        public void Reset()
        {
            Foreground = Brushes.LimeGreen;
            Background = Brushes.Black;
            Blink = false;
            Bold = false;
            Underline = false;
            ReverseVideo = false;
            LowIntensity = false;
            Transparent = false;
            VisualAttributeLock = false;
        }

        public StyleInfo Clone()
        {
            return (StyleInfo)MemberwiseClone();
        }
    }

    public class RowLockManager
    {
        private readonly HashSet<int> _lockedRows = new();
        private bool _ignoreLocksTemporarily = false;
        public IEnumerable<int> GetLockedRows() => _lockedRows.OrderBy(r => r);

        public void Lock(int row) => _lockedRows.Add(row);
        public void Unlock(int row) => _lockedRows.Remove(row);
        
        public void LockSystemLines(int top, int bottom)
        {
            for (int i = top; i <= bottom; i++)
                Lock(i);
        }

        public bool IsLocked(int row)
        {
            if (_ignoreLocksTemporarily) return false;
            return _lockedRows.Contains(row);
        }

        public void IgnoreLocksTemporarily()
        {
            _ignoreLocksTemporarily = true;
        }

        public void RestoreLockEnforcement()
        {
            _ignoreLocksTemporarily = false;
        }

        public void LogLockedRows(ILogger logger)
        {
            var locked = GetLockedRows().ToList();
            if (locked.Count == 0)
                logger.LogDebug("[RowLockManager] Inga låsta rader");
            else
                logger.LogDebug($"[RowLockManager] Låsta rader: {string.Join(", ", locked)}");
        }
    }
}