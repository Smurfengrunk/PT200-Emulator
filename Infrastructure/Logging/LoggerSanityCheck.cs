using Microsoft.Extensions.Logging;
using PT200Emulator.Infrastructure.Logging;

public class LoggerSanityCheck
{
    public static void Run()
    {
        var factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var logger = factory.CreateLogger("SanityCheck");
        logger.LogInformation("✅ Direkt loggning från LoggerFactory");

        LoggerFactoryProvider.SetMinimumLevel(LogLevel.Debug);
        LogHelper.Initialize(factory, "SanityCheck");

        LogHelper.LogInformation("SanityCheck", "✅ Loggning via LogHelper");

        var fallback = new ConsoleFallbackLogger("SanityCheck");
        fallback.Log(LogLevel.Information, new EventId(0), "✅ Loggning via fallback", null, (s, e) => s);
    }
}