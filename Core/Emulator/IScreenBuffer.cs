using static PT200Emulator.Core.Emulator.ScreenBuffer;

namespace PT200Emulator.Core.Emulator
{
    public interface IScreenBuffer
    {
        // Size
        int Rows { get; }
        int Cols { get; }

        // Cursor
        int CursorRow { get; }
        int CursorCol { get; }

        // Events
        event Action BufferUpdated;

        // Access
        char GetChar(int row, int col);

        // Mutations
        void WriteChar(char ch);
        void SetCursorPosition(int row, int col);

        // Control chars
        void CarriageReturn(); // CR
        void LineFeed();       // LF (with scroll)
        void Backspace();      // BS
        void Tab();            // next tab stop (every 8 cols by default)

        // Clearing
        void ClearScreen();
        void ClearLine(int row);
        StyleInfo GetStyle(int row, int col);
        public int GetBufferUpdatedHandlerCount();
        public IDisposable BeginUpdate();
        public event Action<int, int> CursorMoved;
        public bool GetDirty();
        public void MarkDirty();
        public void ClearDirty();
        public StyleInfo CurrentStyle { get; set; }
        public void SetScrollRegion(int top, int bottom);
        public void ResetScrollRegion();
        public bool InSystemLine();
        public RowLockManager RowLocks { get; }
        ScreenCell this[int row, int col] { get; set; }
        ScreenCell GetCell(int row, int col);
        void SetCell(int row, int col, ScreenCell cell);
        void SetStyle(int row, int col,  StyleInfo style);

        // Optional: full buffer resize (if you need it later)
        // void Resize(int rows, int cols);
    }
}