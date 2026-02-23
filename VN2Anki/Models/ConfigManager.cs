using System;
using System.IO;
using System.Text.Json;

namespace VN2Anki.Models
{
    public static class ConfigManager
    {
        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VN2Anki");
        private static readonly string ConfigFilePath = Path.Combine(AppDataFolder, "config.json");

        public static AppConfig Load()
        {
            if (!File.Exists(ConfigFilePath))
                return new AppConfig();

            try
            {
                string json = File.ReadAllText(ConfigFilePath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public static void Save(AppConfig config)
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not save the settings: {ex.Message}");
            }
        }

        public static string GetConfigPath() => ConfigFilePath;
    }
}