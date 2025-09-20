using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PT200Emulator.Core.Parser;
using PT200Emulator.Core.Emulator;
using PT200Emulator.Core.Input;
using PT200Emulator.Core.Rendering;
using PT200Emulator.Infrastructure.Networking;

namespace PT200Emulator.DummyImplementations
{
    internal class Parser : ITerminalParser
    {
        public event Action<byte[]> OnDcsResponse;
        public event Action<IReadOnlyList<TerminalAction>> ActionsReady = delegate { };
        public void Feed(ReadOnlySpan<byte> data) { /* gör inget */ }
        public IScreenBuffer screenBuffer { get; private set; }
        public void TriggerDummy()
        {
            ActionsReady?.Invoke(Array.Empty<TerminalAction>());
        }
        public InputController _controller {  get; set; }

        // Dummy för att undvika idiotiska varningar
        private void SuppressEventWarnings()
        {
            _ = OnDcsResponse;
            _ = ActionsReady;
        }
    }


    internal class Renderer : IRenderer
    {
        public void Render(IReadOnlyList<RenderDiff> diffs) { }
    }
}