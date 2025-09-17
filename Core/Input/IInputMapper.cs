using System.Windows.Input;
using PT200Emulator.Core.Emulator;

namespace PT200Emulator.Core.Input
{
    public interface IInputMapper
    {
        public byte[] MapKey(Key key, ModifierKeys modifiers);
        public byte[] MapText(string text);

    }

    // Placeholder – flyttas eller byggs ut senare
    public enum TerminalModes
    {
        Default
    }
}