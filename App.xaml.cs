using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PT200Emulator.Infrastructure.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace PT200Emulator
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Debug.WriteLine($"[GLOBAL] {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}");
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Debug.WriteLine($"[TASK] {e.Exception.GetType().Name}: {e.Exception.Message}\n{e.Exception.StackTrace}");
                e.SetObserved();
            };

            AllocConsole();

            var loggerFactory = LoggerFactoryProvider.Instance;

            LogHelper.Initialize(loggerFactory,
                "sv",
                "MainWindow",
                "TerminalControl",
                "TcpClientTransport",
                "InputController",
                "TerminalParser"
            );
            LogHelper.ResetLogCount();

            //loggerFactory.CreateLogger("Startup")
            //    .LogInformation("🚀 Applikationen startar med fungerande loggning");

            var services = new ServiceCollection();

            // Registrera factory och logging
            services.AddSingleton(loggerFactory);
            services.AddLogging(); // Registrerar LoggerFilterOptions och IOptionsMonitor

            ServiceProvider = services.BuildServiceProvider();

            base.OnStartup(e);
        }
    }
}