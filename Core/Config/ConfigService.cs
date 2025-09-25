using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using PT200Emulator.Core.Parser;
using Microsoft.Win32;

namespace PT200Emulator.Core.Config
{
    public class ConfigService
    {
        private readonly string _configFolder;

        public ConfigService(string configFolder)
        {
            _configFolder = configFolder;
            Directory.CreateDirectory(_configFolder);
        }

        public TransportConfig LoadTransportConfig() =>
            TransportConfig.Load(_configFolder);

        public UiConfig LoadUiConfig() =>
            UiConfig.Load(_configFolder);

        public void SaveTransportConfig(TransportConfig cfg) =>
            cfg.Save(_configFolder);

        public void SaveUiConfig(UiConfig cfg) =>
            cfg.Save(_configFolder);
    }

    public class TransportConfig
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 2323;

        public static TransportConfig Load(string configFolder)
        {
            var filePath = Path.Combine(configFolder, "transportConfig.json");
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var config = JsonSerializer.Deserialize<TransportConfig>(json);
                    if (config != null)
                        return config;
                }
            }
            catch (Exception ex)
            {
                // Här kan du logga felet om du vill
                Console.WriteLine($"Kunde inte läsa transportkonfiguration: {ex.Message}");
            }

            // Fallback till defaultvärden
            return new TransportConfig();
        }

        public void Save(string configFolder)
        {
            var filePath = Path.Combine(configFolder, "transportConfig.json");
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
    }

    public class UiConfig
    {
        public LogLevel DefaultLogLevel { get; set; } = LogLevel.Debug;
        public TerminalState.ScreenFormat ScreenFormat { get; set; } = TerminalState.ScreenFormat.S80x24;
        public TerminalState.DisplayType DisplayTheme { get; set; } = TerminalState.DisplayType.Green;
        public enum CaretStyle
        {
            VerticalBar,
            Underscore,
            Block
        }

        public CaretStyle CaretStylePreference { get; set; }

        public static UiConfig Load(string configFolder)
        {
            var filePath = Path.Combine(configFolder, "uiConfig.json");
            if (!File.Exists(filePath))
                return new UiConfig();

            try
            {
                var json = File.ReadAllText(filePath);
                var jsonSD = JsonSerializer.Deserialize<UiConfig>(json) ?? new UiConfig();
                return jsonSD;
            }
            catch
            {
                return new UiConfig();
            }
        }

        public void Save(string configFolder)
        {
            var filePath = Path.Combine(configFolder, "uiConfig.json");
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
    }

    public static class ConfigManager
    {
        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public static UiConfig Load()
        {
            if (!File.Exists(ConfigPath))
                return new UiConfig(); // defaultvärden

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<UiConfig>(json) ?? new UiConfig();
            
        }

        public static void Save(UiConfig config)
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(ConfigPath, json);
        }
    }
}