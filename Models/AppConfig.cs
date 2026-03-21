using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilverWolfLauncher.Models
{
    public class AppConfig
    {
        [JsonPropertyName("gamePath")]
        public string GamePath { get; set; } = "";

        [JsonPropertyName("minimizeToTray")]
        public bool MinimizeToTray { get; set; } = true;

        [JsonPropertyName("dontAskOnClose")]
        public bool DontAskOnClose { get; set; } = false;

        [JsonPropertyName("autoServices")]
        public bool AutoServices { get; set; } = true;

        [JsonPropertyName("launcherVersion")]
        public string LauncherVersion { get; set; } = "1.1.0";

        [JsonPropertyName("serverVersion")]
        public string ServerVersion { get; set; } = "0.0.0";

        [JsonPropertyName("proxyVersion")]
        public string ProxyVersion { get; set; } = "0.0.0";

        [JsonPropertyName("skipUpdatePrompt")]
        public bool SkipUpdatePrompt { get; set; } = false;

        [JsonPropertyName("skipInstallPrompt")]
        public bool SkipInstallPrompt { get; set; } = false;

        // --- Manager Logic ---
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new AppConfig();
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch { return new AppConfig(); }
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
