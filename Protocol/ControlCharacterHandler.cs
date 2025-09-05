using PT200Emulator.Util;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using static PT200Emulator.Util.Logger;

namespace PT200Emulator.Protocol
{
    public class ControlCharacterHandler
    {
        public event Action<byte[]> RawOutput;
        public event Action BreakReceived;
        public event Action BellReceived;

        public enum ControlCharacterResult
        {
            NotHandled,
            RawOutput,
            Break,
            Bell,
            Abort,
            FormFeed,
            Null,
            LineFeed,
            CarriageReturn
        }
        public byte[] LastRawBytes { get; private set; }

        public ControlCharacterResult Handle(char ch)
        {
            Log($"[TX ControlCharacterResult] Skickar input till värddatorn: 0x{(int)ch:X2}", LogLevel.Info);
            LastRawBytes = new byte[] { (byte)ch };

            return ch switch
            {
                (char)0x07 => ControlCharacterResult.Bell,
                (char)0x10 => ControlCharacterResult.Break,
                (char)0x03 => ControlCharacterResult.Abort,
                (char)0x0C => ControlCharacterResult.FormFeed,
                (char)0x00 => ControlCharacterResult.Null,
                (char)0x0A => ControlCharacterResult.LineFeed,     // LF
                (char)0x0D => ControlCharacterResult.CarriageReturn, // CR
                _ => ControlCharacterResult.NotHandled
            };
        }

        // För att undvika varningar om oanvända händelser
        private void TouchEvents()
        {
            _ = RawOutput;
            _ = BreakReceived;
            _ = BellReceived;
        }
    }
}