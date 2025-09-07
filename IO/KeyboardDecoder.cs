using System.Windows.Input;

namespace PT200Emulator.IO
{
    public class KeyboardDecoder
    {
        public byte[] DecodeKey(Key key, ModifierKeys modifiers)
        {
            if ((modifiers & ModifierKeys.Control) != 0)
            {
                if (key >= Key.A && key <= Key.Z)
                    return new[] { (byte)(key - Key.A + 1) };

                if (key == Key.Oem4) return new[] { (byte)0x1B }; // Ctrl+[
                if (key == Key.D2) return new[] { (byte)0x00 };  // Ctrl+@
                if (key == Key.P) return new[] { (byte)0x10 };   // Ctrl+P
            }

            switch (key)
            {
                case Key.Enter: return new[] { (byte)'\r' };
                case Key.Tab: return new[] { (byte)'\t' };
                case Key.Escape: return new[] { (byte)0x1B };
                case Key.Back: return new[] { (byte)0x08 };

                case Key.Up: return Escape("[A");
                case Key.Down: return Escape("[B");
                case Key.Right: return Escape("[C");
                case Key.Left: return Escape("[D");

                case Key.Home: return Escape("[H");
                case Key.End: return Escape("[F");
                case Key.PageUp: return Escape("[5~");
                case Key.PageDown: return Escape("[6~");

                case Key.F1: return Escape("OP");
                case Key.F2: return Escape("OQ");
                case Key.F3: return Escape("OR");
                case Key.F4: return Escape("OS");
                case Key.F5: return Escape("[15~");
                case Key.F6: return Escape("[17~");
                case Key.F7: return Escape("[18~");
                case Key.F8: return Escape("[19~");
                case Key.F9: return Escape("[20~");
                case Key.F10: return Escape("[21~");
                case Key.F11: return Escape("[23~");
                case Key.F12: return Escape("[24~");
            }

            return Array.Empty<byte>();
        }

        private byte[] Escape(string sequence) => System.Text.Encoding.ASCII.GetBytes("\x1B" + sequence);

        public ControlCharacterResult HandleControlCharacter(char ch)
        {
            return ch switch
            {
                '\x03' => new(ControlCharacterType.Break),
                '\x07' => new(ControlCharacterType.Bell),
                '\x1B' => new(ControlCharacterType.Escape),
                '\x00' => new(ControlCharacterType.Null),
                _ => new(ControlCharacterType.RawOutput, ch)
            };
        }

        public bool IsControlCharacter(char ch) => ch < 0x20 || ch == 0x7F;

        public string Describe(Key key, ModifierKeys modifiers) => $"{modifiers}+{key}";
    }

    public enum ControlCharacterType
    {
        RawOutput,
        Break,
        Bell,
        Escape,
        Null
    }

    public class ControlCharacterResult
    {
        public ControlCharacterType Type { get; }
        public char? Character { get; }

        public ControlCharacterResult(ControlCharacterType type, char? character = null)
        {
            Type = type;
            Character = character;
        }
    }
}