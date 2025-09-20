using Microsoft.Extensions.Logging;
using PT200Emulator.Core.Input;
using PT200Emulator.Infrastructure.Networking;
using System.Text;
using System.Windows.Input;
using PT200Emulator.Infrastructure.Logging;

namespace PT200Emulator.Core.Input
{
    public class InputController
    {
        private readonly IInputMapper _mapper;
        public Func<byte[], Task> SendBytes { get; set; }
        public InputController(IInputMapper mapper, Func<byte[], Task> sendBytes)
        {
            _mapper = mapper;
            SendBytes = sendBytes;
        }

        public async Task SendRawAsync(byte[] data)
        {
            if (data != null && data.Length > 0)
            {
                await SendBytes(data);
            }
        }

        public async Task SendBreakAsync()
        {
            byte breakByte = 0x10;
            await SendRawAsync(new byte[] { breakByte });
        }

        internal async Task<bool> DetectSpecialKey(KeyEventArgs e)
        {
            // Ctrl + P → SendBreak
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.P)
            {
                await SendBreakAsync();
                return true;
            }

            // Tab → ASCII 0x09
            if (e.Key == Key.Tab)
            {
                await SendRawAsync(new byte[] { 0x09 });
                return true;
            }

            // Ctrl + A–Z → ASCII 0x01–0x1A
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                char c = e.Key.ToString().ToUpperInvariant()[0];
                if (c >= 'A' && c <= 'Z')
                {
                    byte ctrlCode = (byte)(c - '@'); // t.ex. 'A' - '@' = 0x01
                    await SendRawAsync(new byte[] { ctrlCode });
                    return true;
                }
            }

            return false; // ingen specialhantering
        }
    }
}