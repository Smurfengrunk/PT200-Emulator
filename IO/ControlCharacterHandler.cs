using System;
using System.Collections.Generic;

namespace PT200Emulator.IO
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
            Null
        }
        public byte[] LastRawBytes { get; private set; }

        public ControlCharacterResult Handle(char ch)
        {
            LastRawBytes = new byte[] { (byte)ch };

            return ch switch
            {
                (char)0x07 => ControlCharacterResult.Bell,
                (char)0x10 => ControlCharacterResult.Break,
                (char)0x03 => ControlCharacterResult.Abort,
                (char)0x0C => ControlCharacterResult.FormFeed,
                (char)0x00 => ControlCharacterResult.Null,
                _ => ControlCharacterResult.NotHandled
            };
        }
    }
}