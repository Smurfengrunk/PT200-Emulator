using System;
using System.Text;

namespace PT200Emulator.Protocol
{
    public class TelnetSessionBridge
    {
        private readonly TelnetInterpreter _interpreter;
        private readonly Action<byte[]> _sendToServer;
        private readonly Action<char> _renderChar;
        private readonly Action<string> _log;

        public TelnetSessionBridge(TelnetInterpreter interpreter,
                                   Action<byte[]> sendToServer,
                                   Action<char> renderChar,
                                   Action<string> log)
        {
            _interpreter = interpreter;
            _sendToServer = sendToServer;
            _renderChar = renderChar;
            _log = log;

            HookEvents();
        }

        private void HookEvents()
        {
            _interpreter.OnDataByte += b =>
            {
                char ch = (char)b;
                _renderChar(ch);
                _log($"📥 CHAR: '{ch}' (0x{b:X2})");
            };

            _interpreter.OnSendBytes += bytes =>
            {
                _sendToServer(bytes);
                string ascii = Encoding.ASCII.GetString(bytes).Replace("\r", "\\r").Replace("\n", "\\n");
                _log($"📤 TELNET SEND: {BitConverter.ToString(bytes)}  ASCII: \"{ascii}\"");
            };

            _interpreter.OnTelnetCommand += cmd =>
            {
                _log($"🛠 TELNET CMD: {cmd}");
            };
            var test = Encoding.ASCII.GetBytes("Hello\r\n");
            _interpreter.Feed(test, test.Length);
        }

        public void Feed(byte[] buffer, int length)
        {
            _interpreter.Feed(buffer, length);
        }
    }
}