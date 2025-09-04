using System;
using System.Collections.Generic;
using PT200Emulator.Core;
using PT200Emulator.Util;

namespace PT200Emulator.Parser
{
    public class CsiCommandSet
    {
        private readonly IScreenBuffer screenBuffer;

        public Dictionary<char, Action<string>> Commands { get; } = new();

        public CsiCommandSet(IScreenBuffer buffer)
        {
            screenBuffer = buffer;
            Init();
        }

        private void Init()
        {
            Commands['H'] = HandleCursorPosition;
            Commands['J'] = _ => screenBuffer.Clear();
            Commands['K'] = _ => screenBuffer.ClearLine();
            Commands['m'] = HandleSgr;
            Commands['?'] = param => Logger.Log($"[CSI] Frågeteckenkommandot med param: {param}", Logger.LogLevel.Info);
            // Lägg till fler CSI-kommandon här
        }

        private void HandleCursorPosition(string param)
        {
            var parts = param.Split(';');
            int row = parts.Length > 0 && int.TryParse(parts[0], out var r) ? r - 1 : 0;
            int col = parts.Length > 1 && int.TryParse(parts[1], out var c) ? c - 1 : 0;
            screenBuffer.SetCursorPosition(row, col);
        }

        private void HandleSgr(string param)
        {
            var codes = param.Split(';');
            foreach (var code in codes)
            {
                switch (code)
                {
                    case "0":
                        screenBuffer.ResetAttributes();
                        break;
                    case "7":
                        // Reverse video – implementera om du vill
                        break;
                        // Lägg till fler SGR-koder
                }
            }
        }
    }
}