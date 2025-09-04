using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PT200Emulator.IO
{
    public interface ITerminalClient
    {
        Task SendAsync(string data);
    }
}