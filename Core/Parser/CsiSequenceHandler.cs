using PT200Emulator.Infrastructure.Logging;
using PT200Emulator.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PT200Emulator.Core.Parser
{
    public class CsiSequenceHandler
    {
        private readonly CsiCommandTable table;

        public CsiSequenceHandler(CsiCommandTable table)
        {
            this.table = table;
        }

        public void Handle(string sequence)
        {
            this.LogDebug($"[CSI] Sekvens mottagen: ESC[{sequence}");
            // Exempel: "12;24H"
            var match = Regex.Match(sequence, @"^([\d;]*)([A-Za-z])$");
            if (!match.Success) return;

            var paramStr = match.Groups[1].Value;
            var command = match.Groups[2].Value;

            if (table.TryGet(command, out var def))
            {
                var parameters = paramStr.Split(';').Select(p => p.Trim()).ToArray();
                if (def != null) this.LogDebug($"[CSI] {def.Description} → {string.Join(", ", parameters)}"); else this.LogDebug("[CSI] CsiCommandTable not initialized");
                // Här kan du trigga en TerminalAction eller uppdatera TerminalState
            }
            else
            {
                this.LogDebug($"[CSI] Okänd sekvens: ESC[{sequence}");
            }
        }
    }
    public class CsiCommandTable
    {
        private TerminalControl terminalControl;
        private readonly Dictionary<string, CsiCommandDefinition> commands;

        public CsiCommandTable(string jsonPath)
        {
            terminalControl = new TerminalControl();
            terminalControl.UpdateStatus("CSI-sekvens tolkas", Brushes.SteelBlue);
            var json = File.ReadAllText(jsonPath);
            var root = JsonSerializer.Deserialize<CsiCommandRoot>(json);
            commands = root?.CSI.ToDictionary(cmd => cmd.Command) ?? new();
            //foreach (var kv in commands) this.LogDebug($"[CSI] {kv.Key} → {kv.Value.Name} ({kv.Value.Params})");
        }

        public bool TryGet(string command, out CsiCommandDefinition def) =>
            commands.TryGetValue(command, out def);
    }

    public class CsiCommandDefinition
    {
        public string Command { get; set; } = "";
        public string Name { get; set; } = "";
        public string Params { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class CsiCommandRoot
    {
        public List<CsiCommandDefinition> CSI { get; set; } = new();
    }
}
