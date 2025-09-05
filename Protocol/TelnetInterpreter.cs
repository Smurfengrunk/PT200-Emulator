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

        public void Feed(byte[] buffer, int length)
        {
            int i = 0;
            while (i < length)
            {
                byte b = buffer[i];

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
    }
}