#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Configuration service for Claude AI integration.
    /// Persists to %LocalAppData%/McStudDesktop/AiConfig.json.
    /// </summary>
    public class AiConfigService
    {
        private static AiConfigService? _instance;
        public static AiConfigService Instance => _instance ??= new AiConfigService();

        private AiConfig _config;
        private readonly string _configPath;

        // Obfuscated default key parts — assembled at runtime, not stored as single plain string
        // The actual key is set by admin or embedded here in parts
        private static readonly byte[] _defaultKeyData = Array.Empty<byte>();

        public event EventHandler? ConfigChanged;

        public AiConfigService()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(localAppData, "McStudDesktop");
            Directory.CreateDirectory(appDir);
            _configPath = Path.Combine(appDir, "AiConfig.json");
            _config = LoadConfig();
        }

        public AiConfig Config => _config;

        /// <summary>
        /// Whether AI features are globally enabled
        /// </summary>
        public bool IsEnabled => _config.Enabled && !string.IsNullOrWhiteSpace(GetApiKey());

        /// <summary>
        /// Check if a specific AI feature is enabled
        /// </summary>
        public bool IsFeatureEnabled(AiFeature feature)
        {
            if (!IsEnabled) return false;
            return feature switch
            {
                AiFeature.OcrCleanup => _config.FeatureToggles.OcrCleanup,
                AiFeature.GhostPanelDetection => _config.FeatureToggles.GhostPanelDetection,
                AiFeature.PartMatching => _config.FeatureToggles.PartMatching,
                AiFeature.SuggestionDedup => _config.FeatureToggles.SuggestionDedup,
                AiFeature.GhostExplanation => _config.FeatureToggles.GhostExplanation,
                AiFeature.Chat => _config.FeatureToggles.ChatAssistant,
                AiFeature.EstimateReview => _config.FeatureToggles.EstimateReview,
                AiFeature.Automation => _config.FeatureToggles.Automation,
                _ => false
            };
        }

        /// <summary>
        /// Get the active API key (admin override takes precedence over embedded default)
        /// </summary>
        public string GetApiKey()
        {
            if (!string.IsNullOrWhiteSpace(_config.ApiKeyOverride))
                return _config.ApiKeyOverride;

            return GetEmbeddedKey();
        }

        /// <summary>
        /// Get the model name to use for API calls
        /// </summary>
        public string GetModelName() => _config.ModelName;

        /// <summary>
        /// Get daily budget cap in dollars
        /// </summary>
        public decimal GetDailyBudgetCap() => _config.DailyBudgetCap;

        /// <summary>
        /// Get max API calls per day
        /// </summary>
        public int GetMaxCallsPerDay() => _config.MaxCallsPerDay;

        #region Config Management

        public void SetEnabled(bool enabled)
        {
            _config.Enabled = enabled;
            SaveConfig();
        }

        public void SetApiKeyOverride(string? key)
        {
            _config.ApiKeyOverride = key?.Trim();
            SaveConfig();
        }

        public void SetModelName(string model)
        {
            _config.ModelName = model;
            SaveConfig();
        }

        public void SetDailyBudgetCap(decimal cap)
        {
            _config.DailyBudgetCap = Math.Max(0.01m, cap);
            SaveConfig();
        }

        public void SetMaxCallsPerDay(int max)
        {
            _config.MaxCallsPerDay = Math.Max(1, max);
            SaveConfig();
        }

        public void SetFeatureToggle(AiFeature feature, bool enabled)
        {
            switch (feature)
            {
                case AiFeature.OcrCleanup: _config.FeatureToggles.OcrCleanup = enabled; break;
                case AiFeature.GhostPanelDetection: _config.FeatureToggles.GhostPanelDetection = enabled; break;
                case AiFeature.PartMatching: _config.FeatureToggles.PartMatching = enabled; break;
                case AiFeature.SuggestionDedup: _config.FeatureToggles.SuggestionDedup = enabled; break;
                case AiFeature.GhostExplanation: _config.FeatureToggles.GhostExplanation = enabled; break;
                case AiFeature.Chat: _config.FeatureToggles.ChatAssistant = enabled; break;
                case AiFeature.EstimateReview: _config.FeatureToggles.EstimateReview = enabled; break;
                case AiFeature.Automation: _config.FeatureToggles.Automation = enabled; break;
            }
            SaveConfig();
        }

        public void ResetToDefaults()
        {
            _config = new AiConfig();
            SaveConfig();
        }

        #endregion

        #region Admin PIN

        /// <summary>
        /// Whether an admin PIN has been set to protect API key settings
        /// </summary>
        public bool HasAdminPin => !string.IsNullOrEmpty(_config.AdminPinHash);

        /// <summary>
        /// Set or change the admin PIN. Pass null to remove PIN protection.
        /// </summary>
        public void SetAdminPin(string? pin)
        {
            _config.AdminPinHash = string.IsNullOrWhiteSpace(pin) ? null : HashPin(pin);
            SaveConfig();
        }

        /// <summary>
        /// Verify a PIN against the stored hash
        /// </summary>
        public bool VerifyAdminPin(string pin)
        {
            if (!HasAdminPin) return true;
            return _config.AdminPinHash == HashPin(pin);
        }

        private static string HashPin(string pin)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("McStud_PIN_" + pin.Trim()));
            return Convert.ToBase64String(bytes);
        }

        #endregion

        public void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                });
                File.WriteAllText(_configPath, json);
                ConfigChanged?.Invoke(this, EventArgs.Empty);
                System.Diagnostics.Debug.WriteLine("[AiConfig] Config saved");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiConfig] Error saving: {ex.Message}");
            }
        }

        #region Private

        private AiConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<AiConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new JsonStringEnumConverter() }
                    });
                    if (config != null)
                    {
                        // Migrate deprecated model names to current
                        if (config.ModelName.Contains("claude-3-haiku") || config.ModelName.Contains("claude-3.5-haiku"))
                            config.ModelName = "claude-haiku-4-5-20251001";
                        System.Diagnostics.Debug.WriteLine("[AiConfig] Config loaded from file");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiConfig] Error loading, using defaults: {ex.Message}");
            }
            return new AiConfig();
        }

        /// <summary>
        /// Retrieve the embedded default API key (obfuscated in binary).
        /// Returns empty string if no embedded key is configured.
        /// Admin should set their key via the settings panel.
        /// </summary>
        private static string GetEmbeddedKey()
        {
            if (_defaultKeyData.Length == 0) return "";
            try
            {
                // Simple XOR deobfuscation with machine-specific entropy
                var machineKey = Encoding.UTF8.GetBytes(Environment.MachineName.PadRight(32, 'X'));
                var decoded = new byte[_defaultKeyData.Length];
                for (int i = 0; i < _defaultKeyData.Length; i++)
                    decoded[i] = (byte)(_defaultKeyData[i] ^ machineKey[i % machineKey.Length]);
                return Encoding.UTF8.GetString(decoded);
            }
            catch
            {
                return "";
            }
        }

        #endregion
    }

    #region Config Models

    public class AiConfig
    {
        public bool Enabled { get; set; } = false;
        public string? ApiKeyOverride { get; set; }
        public string ModelName { get; set; } = "claude-haiku-4-5-20251001";
        public decimal DailyBudgetCap { get; set; } = 5.00m;
        public int MaxCallsPerDay { get; set; } = 500;
        public AiFeatureToggles FeatureToggles { get; set; } = new();
        public string? AdminPinHash { get; set; }
    }

    public class AiFeatureToggles
    {
        public bool OcrCleanup { get; set; } = true;
        public bool GhostPanelDetection { get; set; } = true;
        public bool PartMatching { get; set; } = true;
        public bool SuggestionDedup { get; set; } = true;
        public bool GhostExplanation { get; set; } = true;
        public bool ChatAssistant { get; set; } = true;
        public bool EstimateReview { get; set; } = true;
        public bool Automation { get; set; } = true;
    }

    public enum AiFeature
    {
        OcrCleanup,
        GhostPanelDetection,
        PartMatching,
        SuggestionDedup,
        GhostExplanation,
        Chat,
        EstimateReview,
        Automation
    }

    #endregion
}
