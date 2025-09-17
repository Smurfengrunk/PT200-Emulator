using PT200Emulator.Core.Parser;
using PT200Emulator.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PT200Emulator.Protocol
{
    public class TelnetSessionBridge
    {
        private readonly TelnetInterpreter _interpreter;
        private readonly Action<byte[]> _sendToServer;
        //private readonly Action<byte[]> _onDataBytes;
        private readonly Action<string> _log;

        private readonly List<byte> _rxBuffer = new();
        private DateTime _lastByteTime = DateTime.UtcNow;
        private readonly TimeSpan _flushTimeout = TimeSpan.FromMilliseconds(10);
        private readonly object _lock = new();
        private ITerminalParser _parser;

        public TelnetSessionBridge(
            TelnetInterpreter telnet,
            Action<byte[]> sendBytes,
            Action<string> log)
        {
            _interpreter = telnet;
            _sendToServer = sendBytes;
            _log = log;
            this.LogDebug($"[TelnetSessionBridge] Telnet interpreter hash: {_interpreter.GetHashCode()}");
        }

        public void HookEvents(TelnetInterpreter telnet, ITerminalParser parser)
        {
            _parser = parser;
            telnet.OnDataBytes += bytes =>
            {
                //this.LogDebug($"[BRIDGE ONDATABYTES] {BitConverter.ToString(bytes)}");
                HandleIncomingBytes(bytes);
            };
            /*telnet.OnSendToServer += bytes =>
                {
                    this.LogDebug($"[BRIDGE SEND TO SERVER] {BitConverter.ToString(bytes)}");
                    _sendToServer(bytes);
                };*/

            // Timer-loop för flush
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(5);
                    if (_rxBuffer.Count > 0 && DateTime.UtcNow - _lastByteTime > _flushTimeout)
                    {
                        Flush(parser);
                    }
                }
            });
        }

        private void HandleIncomingBytes(byte[] buffer)
        {
            lock (_lock)
            {
                _rxBuffer.AddRange(buffer);
                _lastByteTime = DateTime.UtcNow;
            }
        }

        private void Flush(ITerminalParser parser)
        {
            byte[] data;
            lock (_lock)
            {
                if (_rxBuffer.Count == 0) return;
                data = _rxBuffer.ToArray();
                _rxBuffer.Clear();
            }

            parser.Feed(data);
            this.LogDebug($"[FLUSH TO PARSER] {BitConverter.ToString(data)}");
        }

        public void Feed(byte[] buffer, int length)
        {
            //this.LogDebug($"[BRIDGE FEED] len={length} data={BitConverter.ToString(buffer, 0, length)}");
            _interpreter.Feed(buffer, length);
        }

        public Task SendFromClient(byte[] bytes)
        {
            _sendToServer(bytes);
            return Task.CompletedTask;
        }
    }
}