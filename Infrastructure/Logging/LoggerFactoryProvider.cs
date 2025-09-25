using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PT200Emulator.Infrastructure.Logging
{
    public static class LoggerFactoryProvider
    {
        private static ILoggerFactory _instance;
        private static LogLevel _currentLevel = LogLevel.Debug;

        public static ILoggerFactory Instance => _instance ??= CreateFactory();

        private static ILoggerFactory CreateFactory()
        {
            var factory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new CountingLoggerProvider()); // först!
                //builder.AddProvider(new ColorConsoleLoggerProvider()); // vår färgade konsol
                builder.AddDebug(); // VS Debug Output
                builder.AddFilter((category, level) => level >= _currentLevel);
            });

            return factory;
        }


        public static void SetMinimumLevel(LogLevel level)
        {
            _currentLevel = level;
            LogHelper.ResetLogCount(); // Nollställ räknaren vid nivåändring
        }

        public static LogLevel GetMinimumLevel() => _currentLevel;
    }

    public class CountingLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CountingLogger(categoryName);
        public void Dispose() { }

        private class CountingLogger : ILogger
        {
            private readonly string _category;
            public CountingLogger(string category) => _category = category;

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                // Öka räknaren även för loggar som inte går via LogHelper
                LogHelper.IncrementFromProvider();
            }

            private class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new NullScope();
                public void Dispose() { }
            }
        }
    }

}