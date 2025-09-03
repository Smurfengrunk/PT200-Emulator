using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PT200Emulator.Core;
using PT200Emulator.Util; // för Logger

namespace PT200Emulator.Util
{
    public class TcpTerminalClient
    {
        private readonly EscapeSequenceParser parser;
        private TcpClient client;
        private NetworkStream stream;

        public event Action DataReceived;

        // Mode‑indikator
        private bool termMode = false;
        private Timer modeTimer;
        private readonly TimeSpan termTimeout = TimeSpan.FromSeconds(3);

        public TcpTerminalClient(EscapeSequenceParser parser)
        {
            this.parser = parser;
        }

        public async Task ConnectAsync(string host, int port)
        {
            client = new TcpClient();
            await client.ConnectAsync(host, port);
            stream = client.GetStream();

            _ = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                int bytesRead;

                bool inTelnetCommand = false;
                bool inSubNegotiation = false;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    Logger.LogHex(buffer, bytesRead); // logga rådata

                    bool escSeen = false;

                    for (int i = 0; i < bytesRead; i++)
                    {
                        byte b = buffer[i];

                        if (b == 0x1B) escSeen = true; // ESC hittad

                        if (inTelnetCommand)
                        {
                            if (inSubNegotiation)
                            {
                                if (b == 0xFF)
                                {
                                    inTelnetCommand = true;
                                    inSubNegotiation = false;
                                }
                                continue;
                            }
                            else
                            {
                                if (b == 0xFA) // SB
                                {
                                    Logger.Log("[TELNET] SB (Subnegotiation Begin)");
                                    inSubNegotiation = true;
                                    continue;
                                }
                                else if (b == 0xF0) // SE
                                {
                                    Logger.Log("[TELNET] SE (Subnegotiation End)");
                                    inTelnetCommand = false;
                                    continue;
                                }
                                else
                                {
                                    string cmdName = b switch
                                    {
                                        0xFB => "WILL",
                                        0xFC => "WONT",
                                        0xFD => "DO",
                                        0xFE => "DONT",
                                        _ => $"CMD {b:X2}"
                                    };
                                    byte option = buffer[i + 1];
                                    Logger.Log($"[TELNET] {cmdName} (option {option:X2})");
                                    i++; // hoppa över option
                                    inTelnetCommand = false;
                                    continue;
                                }
                            }
                        }

                        if (b == 0xFF) // IAC
                        {
                            inTelnetCommand = true;
                            continue;
                        }

                        // Vanlig data → mata parsern
                        parser.Feed((char)b);
                    }

                    // Mode‑detektering med timer
                    if (escSeen)
                    {
                        if (!termMode)
                        {
                            termMode = true;
                            Logger.Log("[MODE] Switched to TERM mode (ESC sequences detected)");
                        }
                        ResetModeTimer();
                    }

                    DataReceived?.Invoke();
                }
            });
        }

        public async Task SendAsync(string text)
        {
            if (stream == null) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            Logger.LogHex(bytes, bytes.Length, "TX");
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        public async Task SendAsync(byte[] bytes)
        {
            if (stream == null || bytes == null || bytes.Length == 0) return;
            Logger.LogHex(bytes, bytes.Length, "TX");
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        private void ResetModeTimer()
        {
            modeTimer?.Dispose();
            modeTimer = new Timer(_ =>
            {
                if (termMode)
                {
                    termMode = false;
                    Logger.Log("[MODE] Switched to TEXT mode (no ESC sequences for timeout period)");
                }
            }, null, termTimeout, Timeout.InfiniteTimeSpan);
        }
    }
}