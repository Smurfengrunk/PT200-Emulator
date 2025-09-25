using PT200Emulator.Core.Emulator;
using PT200Emulator.Infrastructure.Logging;
using PT200Emulator.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
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
        private string paramStr, command;
        private string[] parameters;
        bool isPrivate;
        private ModeManager _modeManager;
        private TextLogger _textLogger;

        public CsiSequenceHandler(CsiCommandTable table, ModeManager modeManager)
        {
            this.table = table;
            _modeManager = modeManager;
            _textLogger = new TextLogger(MainWindow.Logger);
        }

        public void Handle(char finalChar, string sequence, TerminalState terminal, IScreenBuffer buffer, VisualAttributeManager visualAttributeManager)
        {
            this.LogTrace($"[CSI] Sekvens mottagen: ESC[{sequence}");
            _textLogger.LogDebug($"Esc[{sequence}");
            paramStr = string.Empty;
            parameters = new string[0];
            // Exempel: "12;24H"
            var match = Regex.Match(sequence, @"^([><\?\d;]*)([A-Za-z])$");
            if (!match.Success || match.Groups.Count < 2) return;

            paramStr = match.Groups[1].Value;
            command = match.Groups[2].Value;
            isPrivate = paramStr.StartsWith("?") || paramStr.StartsWith(">");
            if (table.TryHandle(finalChar, command, paramStr, out parameters, terminal, buffer, out var def))
            {
                if (def != null) this.LogDebug($"[CSI] {def.Description} → {string.Join(", ", parameters)}");
                else if (isPrivate) this.LogDebug("[CSI] Privat kommando");
                else this.LogDebug("[CSI] CsiCommandTable not initialized");
                // Här kan du trigga en TerminalAction eller uppdatera TerminalState
            }
            else
            {
                this.LogWarning($"[CsiSequenceHandler] Okänd sekvens ESC[{command}:{paramStr}");
            }
        }
    }

    public class CsiCommandTable
    {
        // Metadata från JSON – nyckel = kommando, värde = definition
        private readonly Dictionary<string, CsiCommandDefinition> _definitions;

        // Handlers för exekvering – nyckel = kommando, värde = Action
        private readonly Dictionary<string, Action<string[], TerminalState, IScreenBuffer>> _handlers;

        private readonly ModeManager _modeManager;
        private readonly VisualAttributeManager _visualAttributeManager;

        public CsiCommandTable(
            IEnumerable<CsiCommandDefinition> definitions,
            ModeManager modeManager,
            VisualAttributeManager visualAttributeManager,
            IScreenBuffer buffer,
            TerminalState terminal)
        {
            _modeManager = modeManager;
            _visualAttributeManager = visualAttributeManager;

            // Ladda metadata från JSON-listan
            _definitions = definitions.ToDictionary(
                d => $"{d.Command}:{d.Params}",   // unik nyckel
                d => d,
                StringComparer.Ordinal
            );
            // Bygg handler-tabellen
            _handlers = new(StringComparer.Ordinal)
            {
                ["H"] = (p, t, b) => b.SetCursorPosition(
                    ParseOrDefault(p.ElementAtOrDefault(0), 1),
                    ParseOrDefault(p.ElementAtOrDefault(1), 1)
                ),

                ["Y"] = (p, t, b) => b.SetCursorPosition(
                    b.CursorCol,
                    ParseOrDefault(p.ElementAtOrDefault(0), 1)
                ),

                ["h"] = (p, t, b) =>
                {
                    foreach (var mode in p.Where(x => x.StartsWith(">"))
                                          .Select(x => x.Substring(1))
                                          .Select(s => int.TryParse(s, out var m) ? m : (int?)null)
                                          .Where(m => m.HasValue))
                    {
                        _modeManager.Set(mode.Value);
                    }
                },

                ["l"] = (p, t, b) =>
                {
                    foreach (var mode in p.Where(x => x.StartsWith(">"))
                                          .Select(x => x.Substring(1))
                                          .Select(s => int.TryParse(s, out var m) ? m : (int?)null)
                                          .Where(m => m.HasValue))
                    {
                        _modeManager.Reset(mode.Value);
                    }
                },

                ["m"] = (p, t, b) => _visualAttributeManager.HandleSGR(p, b, terminal),

                ["r"] = (p, t, b) =>
                {
                    // PT200 CVD – Change Visual Attributes of Display
                    int scope = ParseOrDefault(p.ElementAtOrDefault(0), 2);
                    _visualAttributeManager.ChangeDisplayAttributes(scope, p.Skip(1).ToArray(), buffer, terminal);
                }
            };
        }

        /// <summary>
        /// Hämtar metadata för ett kommando om det finns.
        /// </summary>
        public bool TryGet(string command, string paramStr, out CsiCommandDefinition def)
        {
            // Försök med exakt nyckel först
            if (_definitions.TryGetValue($"{command}:{paramStr}", out def))
                return true;

            // Wildcard: matcha >nn eller >n
            if (paramStr.StartsWith(">"))
            {
                if (_definitions.TryGetValue($"{command}:>nn", out def) ||
                    _definitions.TryGetValue($"{command}:>n", out def))
                    return true;
            }

            // Fallback: matcha bara kommandot om det finns
            if (_definitions.TryGetValue(command, out def))
                return true;

            def = null;
            return false;
        }
        /// <summary>
        /// Försöker köra handlern för ett kommando.
        /// </summary>
        public bool TryHandle(char finalChar, string command, string paramStr, out string[] parameters, TerminalState terminal, IScreenBuffer buffer, out CsiCommandDefinition def)
        {
            def = null;
            var cmd = finalChar.ToString(); // t.ex. "h"
            parameters = string.IsNullOrEmpty(paramStr)
            ? Array.Empty<string>()
                : paramStr.Split(';').Select(p => p.Trim()).ToArray();
            string key = $"{command}:{paramStr}";
            if (IsRowColumn(paramStr) && _definitions.TryGetValue($"{command}:row;column", out def))
            {
                this.LogInformation($"[CSI] {def.Name} ␦ {paramStr} {def.Description}");
            }
            else if (IsNumericList(paramStr) && _definitions.TryGetValue($"{command}:n1;n2;...", out def))
            {
                this.LogInformation($"[CSI] {def.Name} ␦ {paramStr} {def.Description}");
            }
            else if (paramStr.All(char.IsDigit) && _definitions.TryGetValue($"{command}:>n", out def))
            {
                this.LogInformation($"[CSI] {def.Name} ␦ {paramStr} {def.Description}");
            }

            if (_handlers.TryGetValue(command, out var handler))
            {
                handler(parameters, terminal, buffer);
                return true;
            }
            else
            {
                this.LogDebug($"[CSI] Okänt kommando {cmd} med parametrar {paramStr}");
            }
            if (def == null && _definitions.TryGetValue(command, out var fallback))
                def = fallback;
            return false;
        }

        private static bool IsRowColumn(string paramStr)
        {
            var parts = paramStr.Split(';');
            return parts.Length == 2 &&
                   int.TryParse(parts[0], out _) &&
                   int.TryParse(parts[1], out _);
        }

        private static bool IsNumericList(string paramStr)
        {
            var parts = paramStr.Split(';');
            return parts.All(p => int.TryParse(p, out _));
        }

        private static int ParseOrDefault(string s, int def)
            => int.TryParse(s, out var val) ? val : def;
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
