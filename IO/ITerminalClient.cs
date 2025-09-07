using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PT200Emulator.IO
{
    public interface ITerminalClient
    {
        Task SendAsync(byte[] buffer);
        Task SendAsync(char ch);
        Task SendAsync(string text);
        event Action<string> DataReceived;
        bool Connected { get; }
    }
}