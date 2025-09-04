using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PT200Emulator.Interfaces
{
    public interface ITerminalClient
    {
        Task SendAsync(string data);
    }
}