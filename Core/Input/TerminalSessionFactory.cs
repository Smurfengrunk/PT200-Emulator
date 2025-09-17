using PT200Emulator.Core.Input;
using PT200Emulator.Core.Parser;
using System;

namespace PT200Emulator.Core.Input
{
    public static class TerminalSessionFactory
    {
        public static TerminalSession Create(
            InputController controller,
            IInputMapper mapper,
            string basePath, // <-- nytt krav
            TerminalState state,
            ITerminalParser parser,
            string terminalId = "PT200 #01",
            int baudRate = 9600)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                throw new ArgumentException("Base path is required.", nameof(basePath));

            return new TerminalSession(controller, mapper, basePath, state, parser)
            {
                TerminalId = terminalId,
                BaudRate = baudRate
            };
        }
    }
}