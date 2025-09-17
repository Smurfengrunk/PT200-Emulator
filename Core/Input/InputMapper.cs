using Microsoft.Extensions.Logging;
using PT200Emulator.Core.Input;
using PT200Emulator.UI;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace PT200Emulator.Core.Input
{
    public class InputMapper : IInputMapper
    {
        public InputMapper()
        {
        }

        public byte[] MapKey(Key key, ModifierKeys modifiers)
        {
            // Special keys
            if (key == Key.Enter) return new byte[] { 0x0D, 0x0A };
            if (key == Key.Back) return new byte[] { 0x08 };
            if (key == Key.Tab) return new byte[] { 0x09 };
            if (key == Key.Escape) return new byte[] { 0x1B };
            if (key == Key.Space) return new byte[] { 0x20 };

            // Arrow keys
            if (key == Key.Up) return Encoding.ASCII.GetBytes("\x1B[A");
            if (key == Key.Down) return Encoding.ASCII.GetBytes("\x1B[B");
            if (key == Key.Right) return Encoding.ASCII.GetBytes("\x1B[C");
            if (key == Key.Left) return Encoding.ASCII.GetBytes("\x1B[D");

            // Digits
            if (key >= Key.D0 && key <= Key.D9)
            {
                if ((modifiers & ModifierKeys.Shift) != 0)
                {
                    // Shifted symbols on Swedish layout
                    return key switch
                    {
                        Key.D1 => new byte[] { (byte)'!' },
                        Key.D2 => new byte[] { (byte)'"' },
                        Key.D3 => new byte[] { (byte)'#' },
                        Key.D4 => new byte[] { (byte)'¤' },
                        Key.D5 => new byte[] { (byte)'%' },
                        Key.D6 => new byte[] { (byte)'&' },
                        Key.D7 => new byte[] { (byte)'/' },
                        Key.D8 => new byte[] { (byte)'(' },
                        Key.D9 => new byte[] { (byte)')' },
                        Key.D0 => new byte[] { (byte)'=' },
                        _ => null
                    };
                }
                else
                {
                    char c = (char)('0' + (key - Key.D0));
                    return new byte[] { (byte)c };
                }
            }

            // Letters A–Z
            if (key >= Key.A && key <= Key.Z)
            {
                char c = (char)('a' + (key - Key.A));
                if ((modifiers & ModifierKeys.Shift) != 0)
                    c = char.ToUpper(c);

                return new byte[] { (byte)c };
            }

            // Swedish letters
            if (key == Key.Oem3) return new byte[] { (byte)'å' }; // Often Oem3 or Oem102
            if (key == Key.Oem7) return new byte[] { (byte)'ä' };
            if (key == Key.Oem1) return new byte[] { (byte)'ö' };

            // Fallback
            return null;
        }
        public byte[] MapText(string text)
        {
            if (!string.IsNullOrEmpty(text))
                return Encoding.ASCII.GetBytes(text);

            return null;
        }
    }
}