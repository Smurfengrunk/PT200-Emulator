using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PT200Emulator.Parser.EscapeSequenceParser;

namespace PT200Emulator.Core
{
    public interface IScreenBuffer
    {
        int Rows { get; }
        int Cols { get; }
        public TextAttributeState CurrentStyle { get; set; }

        void WriteChar(char ch);
        //void WriteChar(int row, int col, char ch, bool blink = false);
        void WriteChar(char ch, TextAttributeState style);
        void WriteChar(int row, int col, char ch);
        void WriteString(string str, TextAttributeState style);
        void SetCursorPosition(int row, int col);
        void Clear();
        void ClearLine();
        void ResetAttributes();
        void AddChar(char ch);
        void AddChar(string ch);
        string GetText();
    }
}