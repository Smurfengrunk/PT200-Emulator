using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PT200Emulator.Util
{
    public static class Logger
    {
        private static readonly object _lock = new();
        private static readonly string logFilePath;
        private static readonly HashSet<string> _logOnceKeys = new();
        public static bool SilentMode = false;

        public enum LogLevel
        {
            Error = 0,
            Warning = 1,
            Info = 2,
            Debug = 3,
            Trace = 4
        }

        public enum LogProfile
        {
            Silent,
            Normal,
            Info,
            Debug,
            Trace
        }

        private static LogLevel CurrentLevel = LogLevel.Warning;
        public static LogLevel CurrentLogLevel => CurrentLevel;
        private static LogProfile _currentProfile = LogProfile.Normal;
        public static LogProfile CurrentProfile => _currentProfile;



        static Logger()
        {
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            logFilePath = Path.Combine(logDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        }

        public static bool IsEnabled(LogLevel level)
        {
            return level <= CurrentLevel;
        }

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
            //Console.WriteLine($"[LOG CHECK] level={level}, CurrentLevel={CurrentLevel}, IsEnabled={level >= CurrentLevel}");
            //Console.WriteLine($"[ENUM] level={(int)level}, CurrentLevel={(int)CurrentLevel}");

            if (SilentMode || !IsEnabled(level))
            {
                //Console.WriteLine(IsEnabled(level) ? message : $"[LOG {level} SKIPPED]");
                return;
            }

            if (level == LogLevel.Info && message.StartsWith("🛠️"))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(message);
                Console.ResetColor();
            }
            //Console.WriteLine($"[LOG] level={level}, CurrentLevel={CurrentLevel}, IsEnabled={IsEnabled(level)}");
            lock (_lock)
            {
                using var writer = new StreamWriter(logFilePath, append: true, Encoding.UTF8);
                writer.WriteLine(line);
            }

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

        public static void LogOnce(string key, string message, LogLevel level = LogLevel.Info)
        {
            lock (_lock)
            {
                if (_logOnceKeys.Contains(key)) return;
                _logOnceKeys.Add(key);
            }
            Log(message, level);
        }

        public static void LogRaw(string line)
        {
            lock (_lock)
            {
                File.AppendAllText(logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        public static void LogHex(byte[] data, int length, string direction = "RX")
        {
            if (length > 512 && CurrentLevel < LogLevel.Trace) return;
            var hex = BitConverter.ToString(data, 0, length);
            var ascii = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                byte b = data[i];
                ascii.Append((b >= 32 && b <= 126) ? (char)b : '.');
            }

            Log($"{direction} ({length} bytes): {hex}  ASCII: {ascii}", LogLevel.Debug);
        }

        public static void LogBufferState(ScreenBuffer buffer)
        {
            Log("=== ScreenBuffer Dump ===", LogLevel.Debug);
            for (int row = 0; row < buffer.Rows; row++)
            {
                var line = new StringBuilder();
                for (int col = 0; col < buffer.Cols; col++)
                {
                    var cell = buffer.GetCell(row, col);
                    char ch = cell.Character;
                    line.Append(ch == '\0' ? ' ' : ch);
                }
                Log($"{row:D2}: {line}", LogLevel.Debug);
            }
        }

        public static void SetProfile(LogProfile profile)
        {
            _currentProfile = profile;
            //Logger.Log($"[Logger] SetProfile({profile})", LogLevel.Info);
            SilentMode = profile == LogProfile.Silent;
            CurrentLevel = profile switch
            {
                LogProfile.Silent => LogLevel.Error,
                LogProfile.Normal => LogLevel.Warning,
                LogProfile.Info => LogLevel.Info,
                LogProfile.Debug => LogLevel.Debug,
                LogProfile.Trace => LogLevel.Trace,
                _ => LogLevel.Warning
            };
            //Log($"[Logger] CurrentLevel satt till {CurrentLevel}", LogLevel.Info);
            Log($"🛠️ Loggprofil satt till {profile}", LogLevel.Info);
        }
    }
}