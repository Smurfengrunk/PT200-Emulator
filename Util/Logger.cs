using System;
using System.IO;
using System.Text;

namespace PT200Emulator.Util
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string logFilePath;

        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        public static LogLevel CurrentLevel = LogLevel.Warning;
        public static LogLevel ErrorLevel = LogLevel.Error;
        public static LogLevel WarningLevel = LogLevel.Warning;
        public static LogLevel InfoLevel = LogLevel.Info;

        static Logger()
        {
            // Skapa loggmapp bredvid exe
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);

            // Filnamn med tidsstämpel
            logFilePath = Path.Combine(logDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        }

        /// <summary>
        /// Loggar en vanlig textsträng med tidsstämpel.
        /// </summary>
        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (level < CurrentLevel) return;

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

            lock (_lock)
            {
                File.AppendAllText(logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }

            // Skriv bara till konsolen om den är tillgänglig och inte omdirigerad
            if (Environment.UserInteractive && !Console.IsOutputRedirected)
            {
                try
                {
                    Console.WriteLine(line);
                }
                catch (IOException)
                {
                    // Ignorera om konsolen inte finns
                }
            }
        }

        /// <summary>
        /// Loggar en hexdump av data med både hex och ASCII.
        /// </summary>
        public static void LogHex(byte[] data, int length, string direction = "RX")
        {
            var hex = BitConverter.ToString(data, 0, length);
            var ascii = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                byte b = data[i];
                ascii.Append((b >= 32 && b <= 126) ? (char)b : '.');
            }

            Log($"{direction} ({length} bytes): {hex}  ASCII: {ascii}");
        }

        public static void LogBufferState(ScreenBuffer buffer)
        {
            Logger.Log("=== ScreenBuffer Dump ===", LogLevel.Debug);
            for (int row = 0; row < buffer.Rows; row++)
            {
                var line = new StringBuilder();
                for (int col = 0; col < buffer.Cols; col++)
                {
                    var cell = buffer.GetCell(row, col);
                    char ch = cell.Character;
                    line.Append(ch == '\0' ? ' ' : ch);
                }
                Logger.Log($"{row:D2}: {line}", LogLevel.Debug);
            }
        }
    }
}