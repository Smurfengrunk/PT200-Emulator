using PT200Emulator.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PT200Emulator.Core.Emulator
{
    public class FlushTrigger
    {
        private readonly TextLogger _textLogger;
        private bool _hasFlushed = false;
        private int _charCount = 0;
        private int _cursorMoves = 0;

        public FlushTrigger(TextLogger textLogger)
        {
            _textLogger = textLogger;
        }

        public void OnCharWritten()
        {
            _charCount++;

            // Om många tecken skrivs utan flush – trigga
            if (!_hasFlushed && _charCount > 50)
                Flush("Många tecken skrivna utan cursorflytt");
        }

        public void OnCursorMoved(int row, int col)
        {
            _cursorMoves++;

            // Om cursor flyttas flera gånger – trigga
            if (!_hasFlushed && _cursorMoves > 5)
                Flush($"Cursor flyttad {_cursorMoves} gånger");
        }

        public void ForceFlush(string reason = "Manuell flush")
        {
            if (!_hasFlushed)
                Flush(reason);
        }

        private void Flush(string reason)
        {
            _textLogger.FlushAll();
            _hasFlushed = true;
            this.LogDebug($"[FlushTrigger] FlushAll triggat – {reason}");
        }
    }
}
