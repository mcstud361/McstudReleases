#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Centralized configuration for Ghost Estimate labor rates, scanning, and operation overrides.
    /// Persists to %LocalAppData%/McStudDesktop/GhostConfig.json.
    /// </summary>
    public class GhostConfigService
    {
        private static GhostConfigService? _instance;
        public static GhostConfigService Instance => _instance ??= new GhostConfigService();

        private GhostConfig _config;
        private readonly string _configPath;

        // Default labor rates
        public const decimal DEFAULT_BODY_RATE = 55.00m;
        public const decimal DEFAULT_PAINT_RATE = 55.00m;
        public const decimal DEFAULT_MECH_RATE = 95.00m;
        public const decimal DEFAULT_FRAME_RATE = 75.00m;
        public const decimal DEFAULT_GLASS_RATE = 55.00m;

        // Default scanning
        public const decimal DEFAULT_SCAN_FLAT_RATE = 150.00m;
        public const decimal DEFAULT_SCAN_LABOR_HOURS = 0.5m;

        public event EventHandler? ConfigChanged;

        public GhostConfigService()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(localAppData, "McStudDesktop");
            Directory.CreateDirectory(appDir);
            _configPath = Path.Combine(appDir, "GhostConfig.json");
            _config = LoadConfig();
        }

        public GhostConfig Config => _config;

        #region Effective Rate Getters

        public decimal GetEffectiveBodyRate() => _config.LaborRates.BodyRate ?? DEFAULT_BODY_RATE;
        public decimal GetEffectivePaintRate() => _config.LaborRates.PaintRate ?? DEFAULT_PAINT_RATE;
        public decimal GetEffectiveMechRate() => _config.LaborRates.MechRate ?? DEFAULT_MECH_RATE;
        public decimal GetEffectiveFrameRate() => _config.LaborRates.FrameRate ?? DEFAULT_FRAME_RATE;
        public decimal GetEffectiveGlassRate() => _config.LaborRates.GlassRate ?? DEFAULT_GLASS_RATE;

        public ScanningConfig GetScanningConfig() => _config.Scanning;

        /// <summary>
        /// Get the scanning labor hours and price based on billing method.
        /// FlatRate: LaborHours=0, Price=FlatRateAmount.
        /// LaborHours: LaborHours=configured hours, Price=0.
        /// </summary>
        public (decimal LaborHours, decimal Price) GetEffectiveScanning()
        {
            return _config.Scanning.BillingMethod switch
            {
                ScanBillingMethod.FlatRate => (0m, _config.Scanning.FlatRateAmount),
                ScanBillingMethod.LaborHours => (_config.Scanning.LaborHours, 0m),
                _ => (0m, DEFAULT_SCAN_FLAT_RATE)
            };
        }

        /// <summary>
        /// Get per-operation override if one exists.
        /// </summary>
        public OperationOverride? GetOperationOverride(string operationKey)
        {
            operationKey = operationKey.ToLowerInvariant();
            return _config.OperationOverrides.GetValueOrDefault(operationKey);
        }

        /// <summary>
        /// Check if a category is enabled (not disabled by the user).
        /// </summary>
        public bool IsCategoryEnabled(string category)
        {
            return !_config.DisabledCategories.Contains(category);
        }

        #endregion

        #region Config Management

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
                System.Diagnostics.Debug.WriteLine("[GhostConfig] Config saved");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GhostConfig] Error saving: {ex.Message}");
            }
        }

        public void ResetToDefaults()
        {
            _config = new GhostConfig();
            SaveConfig();
        }

        public void SetLaborRate(string rateType, decimal? value)
        {
            switch (rateType.ToLowerInvariant())
            {
                case "body": _config.LaborRates.BodyRate = value; break;
                case "paint": _config.LaborRates.PaintRate = value; break;
                case "mech": _config.LaborRates.MechRate = value; break;
                case "frame": _config.LaborRates.FrameRate = value; break;
                case "glass": _config.LaborRates.GlassRate = value; break;
            }
            SaveConfig();
        }

        public void SetScanningConfig(ScanBillingMethod method, decimal? flatRate = null, decimal? laborHours = null)
        {
            _config.Scanning.BillingMethod = method;
            if (flatRate.HasValue) _config.Scanning.FlatRateAmount = flatRate.Value;
            if (laborHours.HasValue) _config.Scanning.LaborHours = laborHours.Value;
            SaveConfig();
        }

        public void SetOperationOverride(string operationKey, OperationOverride? over)
        {
            operationKey = operationKey.ToLowerInvariant();
            if (over == null)
                _config.OperationOverrides.Remove(operationKey);
            else
                _config.OperationOverrides[operationKey] = over;
            SaveConfig();
        }

        public void SetCategoryEnabled(string category, bool enabled)
        {
            if (enabled)
                _config.DisabledCategories.Remove(category);
            else if (!_config.DisabledCategories.Contains(category))
                _config.DisabledCategories.Add(category);
            SaveConfig();
        }

        #endregion

        #region Load/Save

        private GhostConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<GhostConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new JsonStringEnumConverter() }
                    });
                    if (config != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[GhostConfig] Config loaded from file");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GhostConfig] Error loading, using defaults: {ex.Message}");
            }
            return new GhostConfig();
        }

        #endregion
    }

    #region Config Models

    public class GhostConfig
    {
        public GhostLaborRates LaborRates { get; set; } = new();
        public ScanningConfig Scanning { get; set; } = new();
        public Dictionary<string, OperationOverride> OperationOverrides { get; set; } = new();
        public List<string> DisabledCategories { get; set; } = new();
    }

    public class GhostLaborRates
    {
        public decimal? BodyRate { get; set; }
        public decimal? PaintRate { get; set; }
        public decimal? MechRate { get; set; }
        public decimal? FrameRate { get; set; }
        public decimal? GlassRate { get; set; }
    }

    public class ScanningConfig
    {
        public ScanBillingMethod BillingMethod { get; set; } = ScanBillingMethod.FlatRate;
        public decimal FlatRateAmount { get; set; } = GhostConfigService.DEFAULT_SCAN_FLAT_RATE;
        public decimal LaborHours { get; set; } = GhostConfigService.DEFAULT_SCAN_LABOR_HOURS;
    }

    public enum ScanBillingMethod
    {
        FlatRate,
        LaborHours
    }

    public class OperationOverride
    {
        public decimal? LaborHours { get; set; }
        public decimal? RefinishHours { get; set; }
        public decimal? Price { get; set; }
        public bool Disabled { get; set; }
    }

    #endregion
}
