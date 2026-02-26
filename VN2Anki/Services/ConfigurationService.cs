using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VN2Anki.Models;

namespace VN2Anki.Services
{
    public interface IConfigurationService
    {
        AppConfig CurrentConfig { get; }
        void Save();
        void Load();
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;
        private readonly string _appDataFolder;
        private readonly string _configFilePath;

        public AppConfig CurrentConfig { get; private set; }

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger;
            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VN2Anki");
            _configFilePath = Path.Combine(_appDataFolder, "config.json");

            Load(); // Automatically loads on instantiation
        }

        public void Load()
        {
            if (!File.Exists(_configFilePath))
            {
                _logger.LogInformation("Configuration file not found. Creating default configuration.");
                CurrentConfig = new AppConfig();
                return;
            }

            try
            {
                string json = File.ReadAllText(_configFilePath);
                CurrentConfig = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                _logger.LogInformation("Configuration loaded successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration. Falling back to defaults.");
                CurrentConfig = new AppConfig();
            }
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(_appDataFolder))
                {
                    Directory.CreateDirectory(_appDataFolder);
                }

                string json = JsonSerializer.Serialize(CurrentConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
                _logger.LogInformation("Configuration saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration.");
            }
        }
    }
}