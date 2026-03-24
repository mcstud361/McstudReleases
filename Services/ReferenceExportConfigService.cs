#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace McStudDesktop.Services
{
    public class ReferenceExportConfig
    {
        public string HeaderTitle { get; set; } = "MET Reference Guide";
        public string HeaderSubtitle { get; set; } = "Collision Estimating Reference Documentation";
        public bool ShowDate { get; set; } = true;
        public string DateFormat { get; set; } = "MMMM dd, yyyy 'at' h:mm tt";
        public string FooterText { get; set; } = "MET Reference Guide";
        public bool ShowTableOfContents { get; set; } = true;
        public bool ShowPageNumbers { get; set; } = true;
    }

    public class ReferenceExportConfigService
    {
        private static ReferenceExportConfigService? _instance;
        public static ReferenceExportConfigService Instance => _instance ??= new ReferenceExportConfigService();

        private readonly string _configPath;
        public ReferenceExportConfig Config { get; private set; } = new();

        public event Action? ConfigChanged;

        private ReferenceExportConfigService()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop");
            Directory.CreateDirectory(appDataDir);
            _configPath = Path.Combine(appDataDir, "ReferenceExportConfig.json");
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<ReferenceExportConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (config != null)
                        Config = config;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReferenceExportConfigService] Error loading config: {ex.Message}");
            }
        }

        public void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_configPath, json);
                ConfigChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReferenceExportConfigService] Error saving config: {ex.Message}");
            }
        }

        public void ResetToDefaults()
        {
            Config = new ReferenceExportConfig();
            SaveConfig();
        }
    }
}
