using PT200Emulator.Interfaces;
using PT200Emulator.Util;
using System.Windows.Media;

public class ScreenBuffer : IScreenBuffer
{
    public class Cell
    {
        public char Character { get; set; } = ' ';
        public Brush Foreground { get; set; } = Brushes.White;
        public Brush Background { get; set; } = Brushes.Black;
        public bool Blink { get; set; } = false;
    }

    private readonly int rows;
    private readonly int cols;
    private readonly Cell[,] buffer;

    public int CursorRow { get; set; }
    public int CursorCol { get; set; }

    // Aktuella attribut
    private bool reverseVideo = false;
    private Brush currentForeground = Brushes.White;
    private Brush currentBackground = Brushes.Black;

    public int Rows => rows;
    public int Cols => cols;

    public ScreenBuffer(int cols, int rows)
    {
        this.rows = rows;
        this.cols = cols;
        buffer = new Cell[rows, cols];

        // Initiera bufferten med tomma celler
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                buffer[r, c] = new Cell();
            }
        }
    }

    // Återställ attribut till standard
    public void ResetAttributes()
    {
        reverseVideo = false;
        currentForeground = Brushes.White;
        currentBackground = Brushes.Black;
    }

    // Sätt invertering på/av
    public void SetReverse(bool enable)
    {
        reverseVideo = enable;
    }

    public void SetForeground(Brush brush)
    {
        currentForeground = brush;
    }

    public void SetBackground(Brush brush)
    {
        currentBackground = brush;
    }

    // Skriv ett tecken på aktuell position
    public void WriteChar(int row, int col, char ch, bool blink = false)
    {
        Logger.Log($"[Buffer] ({row},{col}) = '{ch}'", Logger.LogLevel.Debug);
        var fg = reverseVideo ? currentBackground : currentForeground;
        var bg = reverseVideo ? currentForeground : currentBackground;

        buffer[row, col] = new Cell
        {
            Character = ch,
            Foreground = fg,
            Background = bg,
            Blink = blink
        };
    }

    public void WriteChar(int row, int col, char ch, Brush fg, Brush bg, bool blink)
    {
        Logger.Log($"[Buffer] ({row},{col}) = '{ch}'", Logger.LogLevel.Debug);
        buffer[row, col] = new Cell
        {
            Character = ch,
            Foreground = fg,
            Background = bg,
            Blink = blink
        };
    }

    public void WriteChar(int row, int col, char ch)
    {
        Logger.Log($"[Buffer] ({row},{col}) = '{ch}'", Logger.LogLevel.Debug);

        var fg = reverseVideo ? currentBackground : currentForeground;
        var bg = reverseVideo ? currentForeground : currentBackground;

        buffer[row, col] = new Cell
        {
            Character = ch,
            Foreground = fg,
            Background = bg,
            Blink = false
        };
    }


    public void WriteChar(char ch)
    {
        Logger.Log($"[Buffer] ({CursorRow},{CursorCol}) = '{ch}'", Logger.LogLevel.Debug);

        var fg = reverseVideo ? currentBackground : currentForeground;
        var bg = reverseVideo ? currentForeground : currentBackground;

        buffer[CursorRow, CursorCol] = new Cell
        {
            Character = ch,
            Foreground = fg,
            Background = bg,
            Blink = false
        };

        AdvanceCursor();
    }
    private bool IsStatusRow => CursorRow == Rows - 1;

    private void AdvanceCursor()
    {
        CursorCol++;
        if (CursorCol >= Cols)
        {
            CursorCol = 0;
            CursorRow++;
            if (CursorRow >= Rows - 1) // håll sista raden orörd
            {
                ScrollUp();
            }
        }
    }

    public void MoveCursorHome()
    {
        CursorRow = 0;
        CursorCol = 0;
    }

    public string[] GetAllLines()
    {
        var lines = new string[rows];
        for (int r = 0; r < rows; r++)
        {
            char[] chars = new char[cols];
            for (int c = 0; c < cols; c++)
                chars[c] = buffer[r, c].Character;
            lines[r] = new string(chars);
        }
        return lines;
    }

    // Rensa hela skärmen
    public void Clear()
    {
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                buffer[r, c] = new Cell();
            }
        }
        CursorRow = 0;
        CursorCol = 0;
    }

    public void Clear(int mode)
    {
        if (mode == 0)
        {
            // från cursor till slut
            for (int r = CursorRow; r < rows; r++)
            {
                int startCol = (r == CursorRow) ? CursorCol : 0;
                for (int c = startCol; c < cols; c++)
                    buffer[r, c] = new Cell();
            }
        }
        else if (mode == 1)
        {
            // från början till cursor
            for (int r = 0; r <= CursorRow; r++)
            {
                int endCol = (r == CursorRow) ? CursorCol : cols - 1;
                for (int c = 0; c <= endCol; c++)
                    buffer[r, c] = new Cell();
            }
        }
        else if (mode == 2)
        {
            // hela skärmen
            Clear();
        }
    }

    // Rensa från cursor till radslut
    public void ClearLineFromCursor()
    {
        for (int c = CursorCol; c < cols; c++)
        {
            buffer[CursorRow, c] = new Cell();
        }
    }

    // Rensa hela raden
    public void ClearLine()
    {
        for (int c = 0; c < cols; c++)
        {
            buffer[CursorRow, c] = new Cell();
        }
    }

    public void ClearLine(int mode)
    {
        // mode: 0 = från cursor till radslut, 1 = från radbörjan till cursor, 2 = hela raden
        if (mode == 0)
        {
            for (int c = CursorCol; c < cols; c++)
                buffer[CursorRow, c] = new Cell();
        }
        else if (mode == 1)
        {
            for (int c = 0; c <= CursorCol; c++)
                buffer[CursorRow, c] = new Cell();
        }
        else if (mode == 2)
        {
            for (int c = 0; c < cols; c++)
                buffer[CursorRow, c] = new Cell();
        }
    }


    // Flytta cursor
    public void SetCursorPosition(int row, int col)
    {
        CursorRow = Math.Clamp(row, 0, rows - 1);
        CursorCol = Math.Clamp(col, 0, cols - 1);
    }

    // Scrolla upp en rad
    public void ScrollUp()
    {
        for (int r = 1; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                buffer[r - 1, c] = buffer[r, c];
            }
        }
        // Rensa sista raden
        for (int c = 0; c < cols; c++)
        {
            buffer[rows - 1, c] = new Cell();
        }
    }

    // Hämta en cell (för rendering)
    public Cell GetCell(int row, int col)
    {
        return buffer[row, col];
    }

    public string GetLine(int row)
    {
        if (row < 0 || row >= rows) return string.Empty;

        var chars = new char[cols];
        for (int c = 0; c < cols; c++)
            chars[c] = buffer[row, c].Character == '\0' ? ' ' : buffer[row, c].Character;

        return new string(chars);
    }

    // Antag att du har: int rows, cols; Cell[,] buffer;
    // samt currentForeground/currentBackground och reverseVideo.

    private void ClearRow(int row)
    {
        var fg = reverseVideo ? currentBackground : currentForeground;
        var bg = reverseVideo ? currentForeground : currentBackground;

        for (int c = 0; c < cols; c++)
        {
            buffer[row, c] = new Cell
            {
                Character = ' ',
                Foreground = fg,
                Background = bg,
                Blink = false
            };
        }

        Logger.Log($"[Scroll] Cleared row {row}", Logger.LogLevel.Debug);
    }
}