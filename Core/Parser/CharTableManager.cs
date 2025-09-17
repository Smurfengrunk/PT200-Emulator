using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PT200Emulator.Core.Parser
{
    /// <summary>
    /// Hanterar teckentabellerna för G0 och G1 samt växling mellan dem.
    /// </summary>
    public class CharTableManager
    {
        private readonly Dictionary<byte, char> G0Table;
        private readonly Dictionary<byte, char> G1Table;
        public void SwitchToG0() { /* ... */ }
        public void SwitchToG1() { /* ... */ }

        // 0 = G0 aktiv för GL, 1 = G1 aktiv för GL
        private int activeGL = 0;

        public CharTableManager(string g0Path, string g1Path)
        {
            G0Table = LoadTable(g0Path);
            G1Table = LoadTable(g1Path);
        }

        private Dictionary<byte, char> LoadTable(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Teckentabell saknas: {path}");

            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (dict == null)
                throw new InvalidOperationException($"Kunde inte läsa teckentabell: {path}");

            return dict.ToDictionary(
                kvp => Convert.ToByte(kvp.Key, 16),
                kvp => kvp.Value[0]
            );
        }

        /// <summary>
        /// Växla GL till G0.
        /// </summary>
        public void SelectG0() => activeGL = 0;

        /// <summary>
        /// Växla GL till G1.
        /// </summary>
        public void SelectG1() => activeGL = 1;

        /// <summary>
        /// Returnerar rätt tecken beroende på aktiv tabell.
        /// </summary>
        public char Translate(byte code)
        {
            return activeGL == 0
                ? G0Table.GetValueOrDefault(code, '?')
                : G1Table.GetValueOrDefault(code, '?');
        }
    }
}