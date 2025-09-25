using PT200Emulator.Core.Emulator;
using PT200Emulator.Core.Parser;
using PT200Emulator.Infrastructure.Logging;
using System;
using System.Text;

namespace PT200Emulator.Protocol
{
    public class TelnetSessionBridge : IDisposable
    {
        private readonly TelnetInterpreter _interpreter;
        private readonly ITerminalParser _parser;
        public ITerminalParser Parser { get { return _parser; } }
        private readonly Func<byte[], Task> _sendToServerAsync;

        private readonly Action<string> _log;

        // Behöver spara referenser för att kunna avregistrera events
        private Action<byte[]> _sendHandler;

        public TelnetSessionBridge(
            TelnetInterpreter interpreter,
            ITerminalParser parser,
            Func<byte[], Task> sendToServerAsync,
            Action<string> log)
        {
            _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _sendToServerAsync = sendToServerAsync ?? throw new ArgumentNullException(nameof(sendToServerAsync));
            _log = log ?? (_ => { });

            HookEvents();
        }

        public IScreenBuffer ScreenBuffer
        {
            get
            {
                // förutsätter att parsern är av en typ som har ScreenBuffer-property
                return (_parser as TerminalParser)?.screenBuffer;
            }
        }

        internal void HookEvents()
        {
            // Data från servern → parsern
            _interpreter.OnDataBytes += HandleIncomingBytes;

            // Data från klienten → servern
            _sendHandler = bytes =>
            {
                _log?.Invoke($"[BRIDGE] Sending to server: {BitConverter.ToString(bytes)}");
                _sendToServerAsync(bytes);
            };
            _interpreter.OnSendToServer += _sendHandler;
        }

        private void HandleIncomingBytes(byte[] bytes)
        {
            _log?.Invoke($"[BRIDGE] Feeding {bytes.Length} bytes to parser");
            this.LogDebug($"[RAW INPUT] Bytes mottagna: {BitConverter.ToString(bytes)}");
            _parser.Feed(bytes);
        }

        /// <summary>
        /// Anropas av MainWindow när transporten tar emot data.
        /// </summary>
        public void Feed(byte[] buffer, int length)
        {
            _log?.Invoke($"[BRIDGE] Feed called - {length} bytes");
            _interpreter.Feed(buffer, length);
        }

        /// <summary>
        /// Skickar data från klienten till servern.
        /// </summary>
        public Task SendFromClient(byte[] bytes)
        {
            this.LogTrace($"[SENDFROMCLIENT] bytes = {Encoding.ASCII.GetString(bytes)}");
            return _sendToServerAsync(bytes);
        }

        public void Dispose()
        {
            _interpreter.OnDataBytes -= HandleIncomingBytes;

            if (_sendHandler != null)
                _interpreter.OnSendToServer -= _sendHandler;
        }
    }
}