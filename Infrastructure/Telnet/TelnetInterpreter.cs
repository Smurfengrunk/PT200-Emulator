using PT200Emulator.Infrastructure.Logging;
using System;
using System.Collections.Generic;

namespace PT200Emulator.Protocol
{
    public class TelnetInterpreter
    {
        private bool _inSubNegotiation = false;
        private readonly List<byte> _subBuffer = new();

        public event Action<byte[]> OnDataBytes;      // Rena data till parsern
        public event Action<byte[]> OnSendToServer;   // Telnet-svar till servern
        public event Action<string> OnTelnetCommand;  // Logg/debug

        public void Feed(byte[] buffer, int length)
        {
            var cleanData = new List<byte>(); // Ny buffert för ren data
            int i = 0;

            while (i < length)
            {
                byte b = buffer[i];

                if (b == 0xFF) // IAC
                {
                    if (i + 1 >= length) break;
                    byte command = buffer[++i];

                    if (command == 0xFF) // Escaped 0xFF
                    {
                        cleanData.Add(0xFF);
                        i++;
                        continue;
                    }

                    if (command == 0xFA) // SB
                    {
                        _inSubNegotiation = true;
                        _subBuffer.Clear();
                        i++;
                        continue;
                    }

                    if (command == 0xF0 && _inSubNegotiation) // SE
                    {
                        _inSubNegotiation = false;
                        OnTelnetCommand?.Invoke($"SB: {BitConverter.ToString(_subBuffer.ToArray())}");
                        _subBuffer.Clear();
                        i++;
                        continue;
                    }

                    if (i + 1 < length)
                    {
                        byte option = buffer[++i];
                        OnTelnetCommand?.Invoke($"IAC {command:X2} {option:X2}");

                        switch (command)
                        {
                            case 0xFD: // DO
                                if (option == 0x01 /* ECHO */ || option == 0x03 /* SUPPRESS GO AHEAD */)
                                    SendTelnetResponse(0xFB, option); // WILL
                                else
                                    SendTelnetResponse(0xFC, option); // WONT
                                break;

                            case 0xFB: // WILL
                                if (option == 0x01 /* ECHO */ || option == 0x03 /* SUPPRESS GO AHEAD */)
                                    SendTelnetResponse(0xFD, option); // DO
                                else
                                    SendTelnetResponse(0xFE, option); // DONT
                                break;

                            case 0xFE: // DONT
                            case 0xFC: // WONT
                                       // Ignorera
                                break;
                        }
                        i++;
                        continue;
                    }
                    i++;
                }
                else if (_inSubNegotiation)
                {
                    _subBuffer.Add(b);
                    i++;
                }
                else
                {
                    // Lägg till ren data i blockbufferten
                    cleanData.Add(b);
                    i++;
                }
            }

            // Skicka hela blocket på en gång
            if (cleanData.Count > 0)
            {
                OnDataBytes?.Invoke(cleanData.ToArray());
            }
        }

        private void SendTelnetResponse(byte command, byte option)
        {
            byte[] response = { 0xFF, command, option };
            OnSendToServer?.Invoke(response);
        }

        public void ClearHandlers()
        {
            OnDataBytes = null;
            OnSendToServer = null;
            OnTelnetCommand = null;
        }

        public Task SendToServer(byte[] bytes)
        {
            this.LogTrace($"[SEND TO SERVER] {BitConverter.ToString(bytes)}");
            OnSendToServer?.Invoke(bytes);
            return Task.CompletedTask;
        }
    }
}