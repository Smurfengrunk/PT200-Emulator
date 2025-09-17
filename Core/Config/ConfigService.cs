using System;
using System.IO;
using System.Text.Json;

namespace PT200Emulator.Core.Config
{
    public class TransportConfig
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 2323;

        public class ConfigService
        {
            public TransportConfig LoadTransportConfig()
            {
                // Just nu: dummy som returnerar default
                return TransportConfig.Load();
            }
        }


        public static TransportConfig Load(string filePath = "transportsettings.json")
        {
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

        public void Save(string filePath = "transportsettings.json")
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
    }
}