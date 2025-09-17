using PT200Emulator.Core.Parser;
using PT200Emulator.Core.Input;
using PT200Emulator.Infrastructure.Logging;
using System;
using System.Collections.Generic;

namespace PT200Emulator.Core.Routing
{
    public class CommandRouter
    {
        private readonly InputController controller;
        private readonly TerminalState state;

        public CommandRouter(InputController controller, TerminalState state)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public async void Route(IEnumerable<TerminalAction> actions)
        {
            foreach (var action in actions)
            {
                switch (action.Command.ToUpperInvariant())
                {
                    case "DCS":
                        HandleDcs(action.Parameter?.ToString());
                        break;

                    case "PING":
                        await controller.SendRawAsync(System.Text.Encoding.ASCII.GetBytes("PONG\n"));
                        break;

                    case "SETMODE":
                        if (action.Parameter is string modeStr)
                        {
                            if (modeStr.Equals("BLOCK", StringComparison.OrdinalIgnoreCase))
                            {
                                state.IsBlockMode = true;
                                this.LogInformation("[Router] Kommunikationsläge satt till BLOCK");
                            }
                            else if (modeStr.Equals("LINE", StringComparison.OrdinalIgnoreCase))
                            {
                                state.IsBlockMode = false;
                                this.LogInformation("[Router] Kommunikationsläge satt till LINE");
                            }
                            else
                            {
                                this.LogInformation($"[Router] Okänt SETMODE-värde: {modeStr}");
                            }
                        }
                        break;

                    default:
                        this.LogInformation($"[Router] Okänt kommando: {action.Command}");
                        break;
                }
            }
        }

        private void HandleDcs(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                this.LogInformation("[Router] Tom DCS – ignoreras.");
                return;
            }

            this.LogInformation($"[Router] Hanterar DCS: {content}");
            state.ReadDcs("Data/DcsBitGroups.json", content);
        }
    }
}