using Microsoft.Extensions.Logging;
using System;

namespace PT200Emulator.Infrastructure.Logging
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using PT200Emulator.UI;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO.Packaging;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Windows;

    public static class LogHelper
    {
        private static readonly Dictionary<string, ILogger> _loggers = new();
        private static ILoggerFactory _factory;
        private static int _logCountSinceChange;
        private static LocalizationProvider _localization;

        // Callback som UI kan prenumerera på
        public static Action<int, LogLevel> OnLogCountChanged;

        public static void Initialize(ILoggerFactory factory, string languageCode = "sv", params string[] categories)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));

            var primaryLocalization = new LocalizationProvider(languageCode);
            var fallbackLocalization = new LocalizationProvider("en");

            _localization = primaryLocalization;

            // Initiera formattern med både primär och fallback
            LogFormatter.Initialize(primaryLocalization, fallbackLocalization);

            foreach (var category in categories) _loggers[category] = _factory.CreateLogger(category);
            // Initiera formattern med både primär och fallback
            LogFormatter.Initialize(primaryLocalization, fallbackLocalization);

            // Logga en uppstartsruta
            var startupLogger = factory.CreateLogger("Startup");
            var minLevel = LoggerFactoryProvider.GetMinimumLevel();
            var lang = languageCode;
            var fallbackLang = "en";

            startupLogger.LogInformation($"Loggsystem initierat. Miniminivå: {minLevel}, Språk: {lang}, Fallback: {fallbackLang}");
        }

        internal static void IncrementFromProvider()
        {
            _logCountSinceChange++;
            OnLogCountChanged?.Invoke(_logCountSinceChange, LoggerFactoryProvider.GetMinimumLevel());
        }

        public static void ResetLogCount()
        {
            _logCountSinceChange = 0;
            OnLogCountChanged?.Invoke(_logCountSinceChange, LoggerFactoryProvider.GetMinimumLevel());
        }


        private static void IncrementAndNotify()
        {
            _logCountSinceChange++;
            NotifyUI();
        }

        private static void NotifyUI()
        {
            OnLogCountChanged?.Invoke(_logCountSinceChange, LoggerFactoryProvider.GetMinimumLevel());
        }

        public static ILogger GetLogger(string category)
        {
            ILogger primary = new NullLogger(category);
            //if (_factory == null) primary = new NullLogger(category);

            if (!_loggers.TryGetValue(category, out var logger))
            {
                primary = _factory.CreateLogger(category);
                //logger = new CompositeLogger(primary, category);
                _loggers[category] = primary;
            }

            return primary;
        }

        public static void LogDebug(string category, string messageKey)
        {
            IncrementAndNotify();
            string message;
            var localized = _localization?.Get(messageKey);

            if (!string.IsNullOrEmpty(localized))
            {
                message = localized;
            }
            else if (!string.IsNullOrEmpty(messageKey) && !messageKey.StartsWith("["))
            {
                message = $"[{messageKey}]";
            }
            else
            {
                message = messageKey; // redan färdig text, lämna som den är
            }
            GetLogger(category).LogDebug(message);
        }

        public static void LogInformation(string category, string messageKey)
        {
            IncrementAndNotify();
            string message;
            var localized = _localization?.Get(messageKey);

            if (!string.IsNullOrEmpty(localized))
            {
                message = localized;
            }
            else if (!string.IsNullOrEmpty(messageKey) && !messageKey.StartsWith("["))
            {
                message = $"[{messageKey}]";
            }
            else
            {
                message = messageKey; // redan färdig text, lämna som den är
            }
            GetLogger(category).LogInformation(message);
        }

        public static void LogWarning(string category, string messageKey)
        {
            IncrementAndNotify();
            string message;
            var localized = _localization?.Get(messageKey);

            if (!string.IsNullOrEmpty(localized))
            {
                message = localized;
            }
            else if (!string.IsNullOrEmpty(messageKey) && !messageKey.StartsWith("["))
            {
                message = $"[{messageKey}]";
            }
            else
            {
                message = messageKey; // redan färdig text, lämna som den är
            }
            GetLogger(category).LogWarning(message);
        }

        public static void LogError(string category, string messageKey)
        {
            IncrementAndNotify();
            string message;
            var localized = _localization?.Get(messageKey);

            if (!string.IsNullOrEmpty(localized))
            {
                message = localized;
            }
            else if (!string.IsNullOrEmpty(messageKey) && !messageKey.StartsWith("["))
            {
                message = $"[{messageKey}]";
            }
            else
            {
                message = messageKey; // redan färdig text, lämna som den är
            }
            GetLogger(category).LogError(message);
        }

        public static void LogCritical(string category, string message)
        {
            IncrementAndNotify();
            GetLogger(category).LogCritical(message);
        }

        public static void LogTrace(string category, string message)
        {
            IncrementAndNotify();
            GetLogger(category).LogTrace(message);
        }

        private class NullLogger : ILogger
        {
            private readonly string _category;
            public NullLogger(string category) => _category = category;

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            { }

            private class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new NullScope();
                public void Dispose() { }
            }
        }
    }

    public static class LogHelperExtensions
    {
        [ThreadStatic]
        private static int _logDepth;

        private static bool IsRecursing => _logDepth > 10;

        public static void LogDebug(this object caller, string message, params object[] args) =>
            Log(caller, LogLevel.Debug, message, args);

        public static void LogInformation(this object caller, string message, params object[] args) =>
            Log(caller, LogLevel.Information, message, args);

        public static void LogWarning(this object caller, string message, params object[] args) =>
            Log(caller, LogLevel.Warning, message, args);

        public static void LogError(this object caller, string message, params object[] args) =>
            Log(caller, LogLevel.Error, message, args);

        public static void LogCritical(this object caller, string message, params object[] args) =>
            Log(caller, LogLevel.Critical, message, args);

        public static void LogTrace(this object caller, string message, params object[] args) =>
            Log(caller, LogLevel.Trace, message, args);

        public static void LogStackTrace(this object caller, string message = "Stacktrace", bool includeDotNet = false, int maxFrames = 20)
        {
            var category = caller.GetType().Name;
            var stack = CallOriginTracker.GetCallStack(includeDotNet, maxFrames);
            var logger = LogHelper.GetLogger(category);
            logger.LogTrace($"{message}:\n{stack}");
        }
        private static void Log(object caller, LogLevel level, string message, params object[] args)
        {
            if (level < LoggerFactoryProvider.GetMinimumLevel()) return;

            if (IsRecursing) return;

            try
            {
                _logDepth++;
                var category = caller.GetType().Name;
                var formatted = args != null && args.Length > 0
                    ? string.Format(message, args)
                    : message;

                var logger = LogHelper.GetLogger(category);
                logger.Log(level, formatted);
            }
            finally
            {
                _logDepth--;
            }
        }
    }

    public class ConsoleFallbackLogger : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LoggerFactoryProvider.GetMinimumLevel();
        private class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }

        private readonly string _category;
        public ConsoleFallbackLogger(string category)
        {
            _category = category;
        }
        IDisposable ILogger.BeginScope<TState>(TState state)
        {
            return new NoopDisposable();
        }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter == null)
                return;

            var logMessage = formatter(state, exception);
            var originalColor = Console.ForegroundColor;

            Console.ForegroundColor = LogFormatter.GetColor(logLevel);
            Console.WriteLine(LogFormatter.Format(_category, logLevel, logMessage));
            Console.ForegroundColor = originalColor;

            if (exception != null)
                System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    public static class CallOriginTracker
    {
        public static string GetCallStack(bool includeDotNet = false, int maxFrames = 20)
        {
            var trace = new StackTrace(true);
            var sb = new StringBuilder();
            int count = 0;

            foreach (var frame in trace.GetFrames())
            {
                var method = frame.GetMethod();
                var type = method.DeclaringType?.FullName ?? "<global>";
                var file = frame.GetFileName();
                var line = frame.GetFileLineNumber();

                if (!includeDotNet && type.StartsWith("System."))
                    continue;

                sb.AppendLine($"↪ {type}.{method.Name} @ {file}:{line}");

                if (++count >= maxFrames)
                    break;
            }

            return sb.ToString();
        }
    }

    public static class LogFormatter
    {
        private static LocalizationProvider _localization;
        private static LocalizationProvider _fallbackLocalization;

        public static void Initialize(LocalizationProvider localization, LocalizationProvider fallbackLocalization)
        {
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
            _fallbackLocalization = fallbackLocalization ?? throw new ArgumentNullException(nameof(fallbackLocalization));
        }

        public static string Format(string category, LogLevel level, string messageKey, params object[] args)
        {
            // Försök slå upp i primärt språk
            var localized = _localization?.Get(messageKey, args);

            // Om det saknas, försök med fallback (engelska)
            if (string.IsNullOrEmpty(localized))
                localized = _fallbackLocalization?.Get(messageKey, args);

            // Om det fortfarande saknas, visa nyckeln
            if (string.IsNullOrEmpty(localized))
            {
                if (!string.IsNullOrEmpty(messageKey))
                {
                    localized = messageKey.StartsWith("[")
                        ? messageKey
                        : $"[{messageKey}]";
                }
                else
                {
                    localized = "<no message>";
                }
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            return $"[{timestamp}] {level.ToString().ToLower(),5}: {category,-20} {localized}";
        }

        public static ConsoleColor GetColor(LogLevel level) => level switch
        {
            LogLevel.Trace => ConsoleColor.Gray,
            LogLevel.Debug => ConsoleColor.Cyan,
            LogLevel.Information => ConsoleColor.Green,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Critical => ConsoleColor.Magenta,
            _ => ConsoleColor.White
        };
    }

    public class CompositeLogger : ILogger
    {
        private readonly ILogger _primary;
        private readonly ConsoleFallbackLogger _console;

        public CompositeLogger(ILogger primary, string category)
        {
            _primary = primary;
            _console = new ConsoleFallbackLogger(category);
        }

        public IDisposable BeginScope<TState>(TState state) => _primary.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel)
            => logLevel >= LoggerFactoryProvider.GetMinimumLevel();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            _primary.Log(logLevel, eventId, state, exception, formatter);
            _console.Log(logLevel, eventId, state, exception, formatter);
        }

    }

    public class ColorConsoleLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ColorConsoleLogger(categoryName);
        public void Dispose() { }
    }

    public class ColorConsoleLogger : ILogger
    {
        private readonly string _category;
        private static LocalizationProvider _localization;
        private static LocalizationProvider _fallbackLocalization;


        public ColorConsoleLogger(string category)
        {
            _category = category;
            _localization = new LocalizationProvider();
            _fallbackLocalization = new LocalizationProvider();
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel)
            => logLevel >= LoggerFactoryProvider.GetMinimumLevel();
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            if (formatter == null) return;

            // Hämta själva meddelandet från formattern
            var message = formatter(state, exception) ?? string.Empty;

            // Om vi använder lokalisering: försök slå upp
            string localized = null;
            if (!string.IsNullOrEmpty(message) && !message.StartsWith("["))
            {
                // Tolka som nyckel och slå upp
                localized = _localization?.Get(message) ?? _fallbackLocalization?.Get(message);
            }

            // Om lokalisering misslyckas och det är en nyckel, kapsla in i []
            if (string.IsNullOrEmpty(localized))
            {
                if (!string.IsNullOrEmpty(message) && !message.StartsWith("["))
                    localized = $"[{message}]";
                else
                    localized = message; // redan färdig text, lämna som den är
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var formatted = $"[{timestamp}] {logLevel.ToString().ToLower(),5}: {_category,-20} {localized}";

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = LogFormatter.GetColor(logLevel);
            Console.WriteLine(formatted);
            Console.ForegroundColor = originalColor;

            System.Diagnostics.Debug.WriteLine(formatted);

            if (exception != null)
                System.Diagnostics.Debug.WriteLine(exception);
        }



        private class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            public void Dispose() { }
        }
    }
}