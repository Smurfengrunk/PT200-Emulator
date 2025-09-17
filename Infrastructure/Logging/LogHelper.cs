using Microsoft.Extensions.Logging;
using System;

namespace PT200Emulator.Infrastructure.Logging
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using System;
    using System.Collections.Generic;

    public static class LogHelper
    {
        private static readonly Dictionary<string, ILogger> _loggers = new();
        private static ILoggerFactory _factory;
        private static int _logCountSinceChange;

        // Callback som UI kan prenumerera på
        public static Action<int, LogLevel> OnLogCountChanged;

        public static void Initialize(ILoggerFactory factory, params string[] categories)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));

            foreach (var category in categories)
            {
                _loggers[category] = _factory.CreateLogger(category);
            }
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
            if (_factory == null) return new NullLogger(category);
            if (!_loggers.TryGetValue(category, out var logger))
            {
                logger = _factory.CreateLogger(category);
                _loggers[category] = logger;
            }

            return logger;
        }

        public static void LogDebug(string category, string message)
        {
            IncrementAndNotify();
            GetLogger(category).LogDebug(message);
        }

        public static void LogInformation(string category, string message)
        {
            IncrementAndNotify();
            GetLogger(category).LogInformation(message);
        }

        public static void LogWarning(string category, string message)
        {
            IncrementAndNotify();
            GetLogger(category).LogWarning(message);
        }

        public static void LogError(string category, string message)
        {
            IncrementAndNotify();
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

            private static void Log(object caller, LogLevel level, string message, params object[] args)
            {
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
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                var logMessage = formatter(state, exception);
                var originalColor = Console.ForegroundColor;

                Console.ForegroundColor = logLevel switch
                {
                    LogLevel.Debug => ConsoleColor.Cyan,
                    LogLevel.Information => ConsoleColor.Green,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Error => ConsoleColor.Red,
                    LogLevel.Critical => ConsoleColor.Magenta,
                    _ => ConsoleColor.White
                };


                if (exception != null)
                    Console.WriteLine(exception);

                Console.ForegroundColor = originalColor;
            }
        }
    }