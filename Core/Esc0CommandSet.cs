using System;
using System.Collections.Generic;
using PT200Emulator.Interfaces;

namespace PT200Emulator.Core.Terminal
{
    public class Esc0CommandSet
    {
        private readonly IScreenBuffer screenBuffer;

        public Dictionary<string, Action> Commands { get; } = new();
        public Dictionary<string, Action<string>> HexCommands { get; } = new();

        public Esc0CommandSet(IScreenBuffer buffer)
        {
            screenBuffer = buffer;
            Init();
        }

        private void Init()
        {
            // ────── Linjer och hörn
            Commands["#!"] = () => Draw('│');
            Commands["$!"] = () => Draw('─');
            Commands["%!"] = () => Draw('┌');
            Commands["&!"] = () => Draw('┐');
            Commands["'!"] = () => Draw('└');
            Commands["(!"] = () => Draw('┘');
            Commands[")!"] = () => Draw('├');
            Commands["*!"] = () => Draw('┤');
            Commands["+!"] = () => Draw('┬');
            Commands[",!"] = () => Draw('┴');
            Commands["-!"] = () => Draw('┼');

            // ────── Symboler
            Commands["A!"] = () => Draw('★');
            Commands["B!"] = () => Draw('☆');
            Commands["C!"] = () => Draw('●');
            Commands["D!"] = () => Draw('○');
            Commands["E!"] = () => Draw('◆');
            Commands["F!"] = () => Draw('◇');
            Commands["G!"] = () => Draw('▲');
            Commands["H!"] = () => Draw('▼');
            Commands["I!"] = () => Draw('►');
            Commands["J!"] = () => Draw('◄');

            // ────── Statusfält
            Commands["6!"] = () => screenBuffer.SetCursorPosition(screenBuffer.Rows - 1, 0);
            Commands["!!"] = () => {
                int lastRow = screenBuffer.Rows - 1;
                for (int c = 0; c < screenBuffer.Cols; c++)
                    screenBuffer.WriteChar(lastRow, c, ' ');
            };
            Commands["\"!"] = () => screenBuffer.ResetAttributes();

            // ────── Hexgrafik
            HexCommands["69"] = _ => Draw('█');
            HexCommands["6A"] = _ => Draw('▄');
            HexCommands["6B"] = _ => Draw('▀');
            HexCommands["6C"] = _ => Draw('▒');
            HexCommands["6D"] = _ => Draw('░');
            HexCommands["6E"] = _ => Draw('▓');
        }

        private void Draw(char ch)
        {
            screenBuffer.WriteChar(ch);
        }
    }
}