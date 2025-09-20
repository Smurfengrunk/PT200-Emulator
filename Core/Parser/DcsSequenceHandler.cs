using Microsoft.Extensions.Logging;
using PT200Emulator.Core.Input;
using PT200Emulator.Infrastructure.Logging;
using PT200Emulator.Infrastructure.Networking;
using PT200Emulator.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PT200Emulator.Core.Parser
{

    public class DcsSequenceHandler
    {
        public event Action<IReadOnlyList<TerminalAction>> ActionsReady;
        public event Action<byte[]> OnDcsResponse;
        public event Action<string> OnStatusUpdate;

        private readonly TerminalState state;
        private readonly string jsonPath;

        public DcsSequenceHandler(TerminalState state, string jsonPath)
        {
            this.state = state;
            this.jsonPath = jsonPath;
        }

        public void Handle(byte[] payload, InputController _controller)
        {
            RaiseStatus("🟡 Väntar på DCS");
            const string jsonPath = "Data/DcsBitGroups.json";
            if (payload.Length == 0)
            {
                this.LogDebug("[DCS] Tom DCS mottagen – statusförfrågan.");
                RaiseStatus("🟡 Väntar på DCS");
                var dcs = state.BuildDcs(jsonPath);
                SendDcsResponse(dcs, _controller);
                return;
            }

            var content = Encoding.ASCII.GetString(payload);
            this.LogDebug($"[DCS] Innehåll: {content}");
            this.LogDebug($"[DCS] Payload: {BitConverter.ToString(payload)}");
            this.LogDebug($"[DCS] Tolkat innehåll: {content}");

            state.ReadDcs(jsonPath, content);
            var actions = DcsSequenceHandler.Build(content);
            ActionsReady?.Invoke(actions);
        }

        private void SendDcsResponse(string dcs, InputController _controller)
        {
            this.LogDebug($"[DCS] OnDcsResponse is {(OnDcsResponse == null ? "null" : "set")}");
            this.LogDebug($"[DCS] Controller hash = {_controller.GetHashCode()}"); this.LogInformation($"[DCS Response] {dcs}, Längd på respons = {dcs.Length}");
            var hex = BitConverter.ToString(Encoding.ASCII.GetBytes(dcs));
            this.LogDebug($"[DCS Response HEX] {hex}, Längd = {dcs.Length}");
            var bytes = Encoding.ASCII.GetBytes(dcs);
            this.LogTrace($"[DCS] Using handler hash={this.GetHashCode()}");
            OnDcsResponse?.Invoke(bytes);
        }
        public static IReadOnlyList<TerminalAction> Build(string content)
        {
            var actions = new List<TerminalAction>();

            if (content.Contains("BLOCK", StringComparison.OrdinalIgnoreCase))
                actions.Add(new TerminalAction("SETMODE", "BLOCK"));
            else if (content.Contains("LINE", StringComparison.OrdinalIgnoreCase))
                actions.Add(new TerminalAction("SETMODE", "LINE"));

            actions.Add(new TerminalAction("DCS", content));
            return actions;
        }
        private void RaiseStatus(string message)
        {
            OnStatusUpdate?.Invoke(message);
        }
    }
}
