using PT200Emulator.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PT200Emulator.Protocol
{
    public class TelnetInterpreter
    {
        private bool inSubNegotiation = false;
        private List<byte> subBuffer = new();

        public event Action<byte> OnDataByte;
        public event Action<string> OnTelnetCommand;
        public event Action<byte[]> OnSendBytes;

        public void Feed(byte[] buffer, int length)
        {
            Logger.LogHex(buffer, length, "FEED");
            Logger.Log($"FEED ASCII: \"{Encoding.ASCII.GetString(buffer, 0, length)}\"");
            OnTelnetCommand += cmd => Logger.Log($"TELNET CMD: {cmd}", Logger.LogLevel.Info);
            OnDataByte += b => Logger.Log($"CHAR: {(char)b} (0x{b:X2})", Logger.LogLevel.Info);
            int i = 0;
            while (i < length)
            {
                byte b = buffer[i];
                Logger.Log($"[TELNET FEED] Byte: 0x{b:X2} '{(char)b}'");

                if (b == 0xFF) // IAC
                {
                    if (i + 1 >= length) break;
                    byte command = buffer[++i];

                    if (command == 0xFF) // IAC IAC (escaped 0xFF)
                    {
                        OnDataByte?.Invoke(0xFF);
                        i++;
                        continue;
                    }

                    if (command == 0xFA) // SB
                    {
                        inSubNegotiation = true;
                        subBuffer.Clear();
                        i++;
                        continue;
                    }

                    if (command == 0xF0 && inSubNegotiation) // SE
                    {
                        inSubNegotiation = false;
                        OnTelnetCommand?.Invoke($"SB: {BitConverter.ToString(subBuffer.ToArray())}");
                        subBuffer.Clear();
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
                                SendTelnetResponse(0xFB, option); // WILL
                                break;
                            case 0xFB: // WILL
                                SendTelnetResponse(0xFD, option); // DO
                                break;
                            case 0xFE: // DONT
                                SendTelnetResponse(0xFC, option); // WONT
                                break;
                            case 0xFC: // WONT
                                SendTelnetResponse(0xFE, option); // DONT
                                break;
                                // Lägg till fler om du vill hantera andra kommandon
                        }

                        i++;
                        continue;
                    }
                    i++;
                }
                else if (inSubNegotiation)
                {
                    subBuffer.Add(b);
                    i++;
                }
                else
                {
                    OnDataByte?.Invoke(b);
                    i++;
                }
            }
        }

        private void SendTelnetResponse(byte command, byte option)
        {
            byte[] response = new byte[] { 0xFF, command, option };
            OnSendBytes?.Invoke(response); // eller direkt till stream
        }
    }
}