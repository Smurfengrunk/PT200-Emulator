using System;
using PT200Emulator.Core.Emulator;
using PT200Emulator.Core.Parser;

namespace PT200Emulator.Core.Parser
{
    /// <summary>
    /// Tolkar ESC-sekvenser som påverkar teckentabellerna (G0/G1).
    /// </summary>
    public class EscapeSequenceHandler
    {
        private readonly CharTableManager charTables;

        public EscapeSequenceHandler(CharTableManager charTables)
        {
            this.charTables = charTables ?? throw new ArgumentNullException(nameof(charTables));
        }

        /// <summary>
        /// Tar emot en ESC-sekvens (utan själva ESC-tecknet) och utför rätt åtgärd.
        /// </summary>
        public void Handle(string sequence)
        {
            // G0-växlingar
            if (sequence == "(B") // ASCII i G0
                charTables.SelectG0();
            else if (sequence == "(0") // DEC Special Graphics i G0
                charTables.SelectG0(); // Här kan du ladda annan tabell om du vill

            // G1-växlingar
            else if (sequence == ")B") // ASCII i G1
                charTables.SelectG1();
            else if (sequence == ")0") // DEC Special Graphics i G1
                charTables.SelectG1();

            // Här kan du lägga till fler ESC-sekvenser vid behov
            if (sequence.Substring(0) == "$")
            {
                switch (sequence.Substring(1))
                {
                    case "V":
                        //Set status bra
                        break;
                    case "U":
                        // Display status bar
                        break;
                    case "T":
                        // Reset Status bar
                        break;
                }
            }
        }
    }
}