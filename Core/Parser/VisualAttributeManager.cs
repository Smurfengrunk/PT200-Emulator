using PT200Emulator.Core.Emulator;
using PT200Emulator.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PT200Emulator.Core.Emulator.ScreenBuffer;

namespace PT200Emulator.Core.Parser
{
    public class VisualAttributeManager
    {
        // Här kan du hålla nuvarande attribut för hela displayen
        public VisualAttributes CurrentAttributes { get; private set; } = new();

        public void ChangeDisplayAttributes(int scope, string[] parameters, IScreenBuffer buffer, TerminalState terminal)
        {
            // Uppdatera aktuell stil
            HandleSGR(parameters, buffer, terminal);

            // Måla om området med den nya stilen
            ApplyStyleToScope(scope, buffer);
        }

        public void ApplyStyleToScope(int scope, IScreenBuffer buffer)
        {
            var style = buffer.CurrentStyle;

            int startRow, startCol, endRow, endCol;
            switch (scope)
            {
                case 0: // från cursor till slutet
                    startRow = buffer.CursorRow;
                    startCol = buffer.CursorCol;
                    endRow = buffer.Rows - 1;
                    endCol = buffer.Cols - 1;
                    break;
                case 1: // från början till cursor
                    startRow = 0;
                    startCol = 0;
                    endRow = buffer.CursorRow;
                    endCol = buffer.CursorCol;
                    break;
                case 2: // hela skärmen
                default:
                    startRow = 0;
                    startCol = 0;
                    endRow = buffer.Rows - 1;
                    endCol = buffer.Cols - 1;
                    break;
            }

            using (buffer.BeginUpdate())
            {
                for (int row = startRow; row <= endRow; row++)
                {
                    int colStart = (row == startRow) ? startCol : 0;
                    int colEnd = (row == endRow) ? endCol : buffer.Cols - 1;

                    for (int col = colStart; col <= colEnd; col++)
                    {
                        // Gör FG/BG-swap om ReverseVideo är aktiv
                        var fg = style.Foreground;
                        var bg = style.Background;
                        if (style.ReverseVideo)
                            (fg, bg) = (bg, fg);

                        // Klona stilen och sätt rätt färger
                        var newStyle = style.Clone();
                        newStyle.Foreground = fg;
                        newStyle.Background = bg;

                        // Uppdatera cellen i _mainBuffer
                        var cell = buffer.GetCell(row, col);
                        cell.Style = newStyle;
                        buffer.SetCell(row, col, cell);

                        // Uppdatera även _styles[,] så RenderFromBuffer ser ändringen
                        buffer.SetStyle(row, col, newStyle);
                    }
                }
            }

            buffer.MarkDirty(); // triggar BufferUpdated → RenderFromBuffer
        }

        public void HandleSGR(string[] parameters, IScreenBuffer buffer, TerminalState terminal)
        {
            if (parameters.Length == 0)
            {
                buffer.CurrentStyle.Reset();
                terminal.GlyphMode = GlyphMode.Normal;
                return;
            }

            foreach (var p in parameters)
            {
                switch (p)
                {
                    case "0":
                        this.LogDebug($"Normal Video, ESC [{p}m");
                        buffer.CurrentStyle.Reset();
                        terminal.GlyphMode = GlyphMode.Normal;
                        this.LogDebug($"LowIntensity flag = {buffer.CurrentStyle.LowIntensity}");
                        break;
                    case "2":
                        this.LogDebug($"Low Intensity Video, ESC [{p}m");
                        buffer.CurrentStyle.LowIntensity = true; // Lägg till flagga i CurrentStyle
                        this.LogDebug($"LowIntensity flag = {buffer.CurrentStyle.LowIntensity}");
                        break;
                    case "4":
                        this.LogDebug($"Underline, ESC [{p}m");
                        buffer.CurrentStyle.Underline = true;
                        this.LogDebug($"LowIntensity flag = {buffer.CurrentStyle.LowIntensity}");
                        break;
                    case "5":
                        this.LogDebug($"Blink, ESC [{p}m");
                        buffer.CurrentStyle.Blink = true;
                        this.LogDebug($"LowIntensity flag = {buffer.CurrentStyle.LowIntensity}");
                        break;
                    case "7":
                        this.LogDebug($"Reverse Video, ESC [{p}m");
                        buffer.CurrentStyle.ReverseVideo = true;
                        this.LogDebug($"LowIntensity flag = {buffer.CurrentStyle.LowIntensity}");
                        break;
                    case ">1":
                        this.LogDebug($"StrikeThrough, ESC [{p}m");
                        buffer.CurrentStyle.StrikeThrough = true;
                        this.LogDebug($"LowIntensity flag = {buffer.CurrentStyle.LowIntensity}");
                        break;
                    case ">2":
                        this.LogDebug($"Invisible Video, ESC [{p}m");
                        buffer.CurrentStyle.Foreground = buffer.CurrentStyle.Background;
                        this.LogDebug($"LowIntensity flag = {buffer.CurrentStyle.LowIntensity}");
                        break;
                    case ">3":
                        this.LogDebug($"Line Drawing Graphics, ESC [{p}m");
                        terminal.GlyphMode = GlyphMode.LineDrawing;
                        this.LogDebug($"LowIntensity flag = {buffer.CurrentStyle.LowIntensity}");
                        break;
                    case ">4":
                        this.LogDebug($"Block Drawing Graphics, ESC [{p}m");
                        terminal.GlyphMode = GlyphMode.BlockDrawing;
                        this.LogDebug($"LowIntensity flag = {buffer.CurrentStyle.LowIntensity}");
                        break;
                }
            }
        }

        public enum GlyphMode
        {
            Normal,
            LineDrawing,
            BlockDrawing
        }

        public class GlyphModeInterpreter
        {
            public void Apply(string[] parameters, TerminalState terminal)
            {
                foreach (var p in parameters)
                {
                    switch (p)
                    {
                        case ">3":
                            this.LogDebug($"Glyphmode {p} => Line Drawing");
                            terminal.GlyphMode = GlyphMode.LineDrawing;
                            break;
                        case ">4":
                            this.LogDebug($"Glyphmode {p} => Block Drawing");
                            terminal.GlyphMode = GlyphMode.BlockDrawing;
                            break;
                        case "0":
                            this.LogDebug($"Glyphmode {p} => Normal");
                            terminal.GlyphMode = GlyphMode.Normal;
                            break;
                            // Lägg till fler om PT200 har fler glyphlägen
                    }
                }
            }
        }
    }

    public class VisualAttributes
    {
        public bool Bold { get; set; }
        public bool Underline { get; set; }
        public bool ReverseVideo { get; set; }
        public ConsoleColor Foreground { get; set; }
        public ConsoleColor Background { get; set; }

        public void Reset()
        {
            Bold = false;
            Underline = false;
            ReverseVideo = false;
            Foreground = ConsoleColor.Gray;
            Background = ConsoleColor.Black;
        }
    }
}
