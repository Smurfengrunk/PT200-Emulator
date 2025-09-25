using PT200Emulator.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PT200Emulator.Core.Parser
{
    /// <summary>
    /// PT200 har bara två teckentabeller:
    /// G0 = ASCII, G1 = Special Graphics.
    /// GL = G0 eller G1 beroende på activeGL, GR = alltid G1.
    /// </summary>
    public class CharTableManager
    {
        private readonly Dictionary<byte, char> _asciiTable;
        private readonly Dictionary<byte, char> _graphicsTable;

        // Aktuella tabeller för G0 och G1
        private Dictionary<byte, char> _g0;
        private Dictionary<byte, char> _g1;

        // 0 = G0 aktiv för GL, 1 = G1 aktiv för GL
        private int activeGL = 0;

        public CharTableManager(string g0Path, string g1Path)
        {
            _asciiTable = LoadTable(g0Path);
            _graphicsTable = LoadTable(g1Path);

            // PT200 startar alltid med ASCII i G0 och grafik i G1
            _g0 = _asciiTable;
            _g1 = _graphicsTable;
        }

        private Dictionary<byte, char> LoadTable(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Teckentabell saknas: {path}");

            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? throw new InvalidOperationException($"Kunde inte läsa teckentabell: {path}");

            return dict.ToDictionary(
                kvp => Convert.ToByte(kvp.Key, 16),
                kvp => kvp.Value[0]
            );
        }

        /// <summary>
        /// Växla GL till G0 (LS0)
        /// </summary>
        public void SelectG0()
        {
            if (activeGL != 0)
            {
                activeGL = 0;
                this.LogDebug("[CharTableManager] GL -> G0 (ASCII)");
            }
        }

        /// <summary>
        /// Växla GL till G1 (LS1)
        /// </summary>
        public void SelectG1()
        {
            if (activeGL != 1)
            {
                activeGL = 1;
                this.LogDebug("[CharTableManager] GL -> G1 (Graphics)");
            }
        }

        /// <summary>
        /// Översätt en byte till rätt tecken beroende på GL/GR och aktiv tabell.
        /// </summary>
        public char Translate(byte code)
        {
            //this.LogDebug($"SPACE: activeGL={activeGL}, table={(activeGL == 0 ? "G0" : "G1")}");
            //this.LogDebug($"G0[0x20] = '{_g0[0x20]}' (U+{(int)_g0[0x20]:X4})");
            // GL-området (0x21–0x7E)
            if (code >= 0x20 && code <= 0x7E)
                return (activeGL == 0 ? _g0 : _g1).GetValueOrDefault(code, '?');

            // GR-området (0xA1–0xFE) – alltid G1
            if (code >= 0xA1 && code <= 0xFE)
                return _g1.GetValueOrDefault((byte)(code & 0x7F), '?');

            // Kontrolltecken eller okänt
            return '?';
        }

        public char GetGlyphFromTable(string tableName, byte code)
        {
            if (!(_g0.ContainsKey(code) || _g1.ContainsKey(code)))
                this.LogDebug($"[CharTableManager] Okänt glyph: 0x{code:X2} i {tableName}");

            return tableName switch
            {
                "G0" => _g0.GetValueOrDefault(code, '?'),
                "G1" => _g1.GetValueOrDefault(code, '?'),
                _ => '?'
            };
        }

        public void LoadG0Ascii() => _g0 = _asciiTable;
        public void LoadG0Graphics() => _g0 = _graphicsTable;
        public void LoadG1Ascii() => _g1 = _asciiTable;
        public void LoadG1Graphics() => _g1 = _graphicsTable;
    }
}