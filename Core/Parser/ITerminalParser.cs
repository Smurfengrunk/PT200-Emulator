using PT200Emulator.Core.Emulator;
using PT200Emulator.Core.Input;
using System;
using System.Collections.Generic;

namespace PT200Emulator.Core.Parser
{
    public interface ITerminalParser
    {
        void Feed(ReadOnlySpan<byte> data);
        event Action<IReadOnlyList<TerminalAction>> ActionsReady;
        event Action<byte[]> OnDcsResponse;
        IScreenBuffer screenBuffer { get; }

    }

    // Placeholder – flyttas eller byggs ut senare
    public record TerminalAction(string Command, object Parameter = null);
}