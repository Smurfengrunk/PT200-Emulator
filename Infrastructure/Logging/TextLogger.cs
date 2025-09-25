using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PT200Emulator.Infrastructure.Logging
{
    public class TextLogger // Testklass under utvecklingen för att dumpa kompletta rader, främst från Emacs
    {
        private readonly Dictionary<int, List<(int col, char ch)>> _lines = new();
        private readonly ILogger _logger;

        public TextLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void LogChar(int row, int col, char ch)
        {
            if (!_lines.ContainsKey(row))
                _lines[row] = new List<(int, char)>();

            _lines[row].Add((col, ch));
        }

        public void FlushLine(int row)
        {
            if (!_lines.TryGetValue(row, out var chars)) return;

            var ordered = chars.OrderBy(c => c.col).Select(c => c.ch).ToArray();
            var text = new string(ordered);

            this.LogDebug($"[TEXT] Row {row}: \"{new string(ordered)}\"");

            _lines.Remove(row);
        }

        public void FlushAll()
        {
            this.LogDebug($"[TextLogger] Rader att flusha: {string.Join(", ", _lines.Keys)}");
            foreach (var row in _lines.Keys.ToList())
                FlushLine(row);
        }
    }
}
