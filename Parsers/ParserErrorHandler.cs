using PT200Emulator.Util;
using System;
using static PT200Emulator.Util.Logger;

namespace PT200Emulator.Parser
{
    public class ParserErrorHandler
    {
        public void Handle(Exception ex, string context = null)
        {
            string message = $"[PARSER-ERROR] {ex.GetType().Name}: {ex.Message}";
            if (!string.IsNullOrEmpty(context))
                message += $" | Kontext: {context}";

            Log(message, LogLevel.Error);
        }

        public void Handle(string message)
        {
            Log($"[PARSER-WARNING] {message}", LogLevel.Warning);
        }
    }
}