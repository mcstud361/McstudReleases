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
            SeedDefaultMustHaves();
            MigrateExistingConfig();
        }

        /// <summary>
        /// Seeds default must-have operations if the list is empty.
        /// These are standard SOP items that should be checked on every uploaded estimate.
        /// </summary>
        private void SeedDefaultMustHaves()
        {
            if (_config.MustHaves.Count > 0) return;

            foreach (var d in GetCanonicalMustHaves())
            {
                _config.MustHaves.Add(new MustHaveOperation
                {
                    Description = d.Desc,
                    Section = d.Section,
                    OpType = d.OpType,
                    Category = d.Category,
                    ExpectedPrice = d.Price,
                    ExpectedHours = d.Hours,
                    RefinishHours = d.RefinishHours,
                    PointDeduction = d.Points,
                    Enabled = true
                });
            }
            SaveConfig();
        }

        /// <summary>
        /// Canonical list of all must-have operations — single source of truth.
        /// Used for seeding new configs and migrating existing ones.
        /// </summary>
        private static List<(string Desc, string Section, string OpType, string Category, decimal Hours, decimal RefinishHours, decimal Price, int Points)> GetCanonicalMustHaves()
        {
            return new()
            {
                // === MISCELLANEOUS OPERATIONS ===
                ("Clean for Delivery",                              "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     1.0m, 0m,   0m,      3),
                ("Glass Cleaner",                                   "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0m,   0m,   2.00m,   2),
                ("Mask and Protect Removed Components",             "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0m,   0.5m, 10.00m,  3),
                ("Parts Disposal",                                  "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0m,   0m,   25.00m,  3),
                ("Hazardous Waste Disposal",                        "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0m,   0m,   7.50m,   3),
                ("Misc Hardware",                                   "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0m,   0m,   15.00m,  2),
                ("Steering Wheel Cover, Seat Cover, and Floor Mat", "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0m,   0.2m, 5.00m,   2),
                ("Refinish Material Invoice",                       "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0m,   0m,   1.00m,   2),
                ("Color Tint (2-Stage)",                            "MISCELLANEOUS OPERATIONS", "Rfn",    "Refinish Operations", 0m,   0.5m, 0m,      3),
                ("Spray Out Cards (2-Stage)",                       "MISCELLANEOUS OPERATIONS", "Rfn",    "Refinish Operations", 0m,   0.5m, 0m,      3),
                ("Cover Car for Overspray",                         "MISCELLANEOUS OPERATIONS", "Rfn",    "Refinish Operations", 0m,   0.2m, 0m,      2),
                ("Cover for Edging",                                "MISCELLANEOUS OPERATIONS", "Rfn",    "Refinish Operations", 0m,   0.3m, 0m,      2),
                ("Mask for Buffing",                                "MISCELLANEOUS OPERATIONS", "Rfn",    "Refinish Operations", 0m,   0.5m, 0m,      2),
                ("Cover Engine Compartment",                        "MISCELLANEOUS OPERATIONS", "Rfn",    "Refinish Operations", 0m,   0.2m, 0m,      2),
                ("Cover Interior and Jambs for Refinish",           "MISCELLANEOUS OPERATIONS", "Rfn",    "Refinish Operations", 0m,   0.2m, 0m,      2),
                ("Clean and Cover Car for Primer",                  "MISCELLANEOUS OPERATIONS", "Rfn",    "Refinish Operations", 0m,   0.3m, 0m,      2),
                ("Cover Interior and Jambs for Repairs",            "MISCELLANEOUS OPERATIONS", "Rfn",    "Refinish Operations", 0m,   0.2m, 0m,      2),

                // === VEHICLE DIAGNOSTICS ===
                ("Pre-Scan",                                        "VEHICLE DIAGNOSTICS", "Sublet", "Scanning",              0m,   0m,   150.00m, 5),
                ("In-Process Scan",                                 "VEHICLE DIAGNOSTICS", "Sublet", "Scanning",              0m,   0m,   150.00m, 5),
                ("Post Scan",                                       "VEHICLE DIAGNOSTICS", "Sublet", "Scanning",              0m,   0m,   150.00m, 5),
                ("Setup Scan Tool",                                 "VEHICLE DIAGNOSTICS", "Mech",   "Scanning",              0.2m, 0m,   0m,      2),
                ("Dynamic Systems Verification",                    "VEHICLE DIAGNOSTICS", "Mech",   "Scanning",              1.0m, 0m,   0m,      3),
                ("OEM Research",                                    "VEHICLE DIAGNOSTICS", "Mech",   "Scanning",              1.0m, 0m,   50.00m,  3),
                ("ADAS Diagnostic Report",                          "VEHICLE DIAGNOSTICS", "Body",   "Calibration",           0m,   0m,   25.00m,  3),
                ("Simulate Full Fluids for ADAS Calibrations",      "VEHICLE DIAGNOSTICS", "Mech",   "Calibration",           0.2m, 0m,   0m,      2),
                ("Check and Adjust Tire Pressure for ADAS",         "VEHICLE DIAGNOSTICS", "Mech",   "Calibration",           0.2m, 0m,   0m,      2),
                ("Remove Customer Belongings for ADAS",             "VEHICLE DIAGNOSTICS", "Body",   "Calibration",           0.2m, 0m,   0m,      2),

                // === ELECTRICAL ===
                ("Disconnect and Reconnect Battery",                "ELECTRICAL", "Mech", "Mechanical Operations",            0.4m, 0m,   0m,      3),
                ("Test Battery Condition",                          "ELECTRICAL", "Mech", "Mechanical Operations",            0.2m, 0m,   0m,      2),
                ("Electronic Reset",                                "ELECTRICAL", "Mech", "Mechanical Operations",            0.5m, 0m,   0m,      3),
                ("Cover and Protect Electrical Connections",        "ELECTRICAL", "Mech", "Mechanical Operations",            0.3m, 0m,   5.00m,   2),
                ("Battery Support",                                 "ELECTRICAL", "Mech", "Mechanical Operations",            0.2m, 0m,   0m,      2),
                ("Charge and Maintain Battery during ADAS",         "ELECTRICAL", "Mech", "Mechanical Operations",            0.6m, 0m,   0m,      2),
            };
        }

        /// <summary>
        /// For existing users who already have a GhostConfig.json: fill in new fields
        /// (OpType, Category, RefinishHours) on existing items and add any missing items.
        /// </summary>
        private void MigrateExistingConfig()
        {
            var canonical = GetCanonicalMustHaves();
            bool changed = false;

            // Build lookup from canonical list by lowercase description
            var canonicalByDesc = new Dictionary<string, (string Desc, string Section, string OpType, string Category, decimal Hours, decimal RefinishHours, decimal Price, int Points)>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in canonical)
                canonicalByDesc[c.Desc] = c;

            // Update existing items: fill in new fields if they're still at defaults
            foreach (var mh in _config.MustHaves)
            {
                // Try exact match first, then fuzzy (existing configs may have shorter names)
                if (canonicalByDesc.TryGetValue(mh.Description, out var match) ||
                    TryFuzzyMatchCanonical(mh.Description, canonicalByDesc, out match))
                {
                    if (string.IsNullOrEmpty(mh.OpType) || mh.OpType == "Body")
                    {
                        // Only overwrite if OpType was never set (default "Body") and canonical differs
                        if (match.OpType != "Body" || string.IsNullOrEmpty(mh.Category))
                        {
                            mh.OpType = match.OpType;
                            changed = true;
                        }
                    }
                    if (string.IsNullOrEmpty(mh.Category))
                    {
                        mh.Category = match.Category;
                        changed = true;
                    }
                    if (mh.RefinishHours == 0 && match.RefinishHours != 0)
                    {
                        mh.RefinishHours = match.RefinishHours;
                        changed = true;
                    }
                    // Update Section to canonical form (e.g. "Misc" → "MISCELLANEOUS OPERATIONS")
                    if (mh.Section != match.Section)
                    {
                        mh.Section = match.Section;
                        changed = true;
                    }
                    // Update Description to canonical form (e.g. "Color Tint" → "Color Tint (2-Stage)")
                    if (mh.Description != match.Desc)
                    {
                        mh.Description = match.Desc;
                        changed = true;
                    }
                }
            }

            // Add missing items that don't exist in the current config
            var existingDescs = new HashSet<string>(
                _config.MustHaves.Select(m => m.Description),
                StringComparer.OrdinalIgnoreCase);

            foreach (var c in canonical)
            {
                if (existingDescs.Contains(c.Desc))
                    continue;

                _config.MustHaves.Add(new MustHaveOperation
                {
                    Description = c.Desc,
                    Section = c.Section,
                    OpType = c.OpType,
                    Category = c.Category,
                    ExpectedPrice = c.Price,
                    ExpectedHours = c.Hours,
                    RefinishHours = c.RefinishHours,
                    PointDeduction = c.Points,
                    Enabled = true
                });
                changed = true;
            }

            if (changed)
                SaveConfig();
        }

        /// <summary>
        /// Fuzzy match: old config may have "Color Tint" but canonical is "Color Tint (2-Stage)".
        /// Match if the canonical description starts with or contains the existing description.
        /// </summary>
        private static bool TryFuzzyMatchCanonical(
            string description,
            Dictionary<string, (string Desc, string Section, string OpType, string Category, decimal Hours, decimal RefinishHours, decimal Price, int Points)> canonical,
            out (string Desc, string Section, string OpType, string Category, decimal Hours, decimal RefinishHours, decimal Price, int Points) match)
        {
            match = default;
            if (string.IsNullOrWhiteSpace(description)) return false;

            var descLower = description.ToLowerInvariant().Trim();
            foreach (var kvp in canonical)
            {
                var canonLower = kvp.Key.ToLowerInvariant();
                if (canonLower.Contains(descLower) || descLower.Contains(canonLower))
                {
                    match = kvp.Value;
                    return true;
                }
            }
            return false;
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

        #region Custom Operations

        public List<GhostCustomOperation> GetCustomOperations() => _config.CustomOperations;

        public void AddCustomOperation(GhostCustomOperation op)
        {
            _config.CustomOperations.Add(op);
            SaveConfig();
        }

        public void UpdateCustomOperation(GhostCustomOperation op)
        {
            var idx = _config.CustomOperations.FindIndex(o => o.Id == op.Id);
            if (idx >= 0) _config.CustomOperations[idx] = op;
            SaveConfig();
        }

        public void RemoveCustomOperation(string id)
        {
            _config.CustomOperations.RemoveAll(o => o.Id == id);
            SaveConfig();
        }

        #endregion

        #region Must-Haves

        public List<MustHaveOperation> GetMustHaves() => _config.MustHaves;

        public void AddMustHave(MustHaveOperation mh)
        {
            _config.MustHaves.Add(mh);
            SaveConfig();
        }

        public void UpdateMustHave(MustHaveOperation mh)
        {
            var idx = _config.MustHaves.FindIndex(m => m.Id == mh.Id);
            if (idx >= 0) _config.MustHaves[idx] = mh;
            SaveConfig();
        }

        public void RemoveMustHave(string id)
        {
            _config.MustHaves.RemoveAll(m => m.Id == id);
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
        public List<GhostCustomOperation> CustomOperations { get; set; } = new();
        public List<MustHaveOperation> MustHaves { get; set; } = new();
        public ScoringWeights ScoringWeights { get; set; } = new();
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

    public class GhostCustomOperation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Description { get; set; } = "";
        public string Category { get; set; } = "B";
        public string OperationType { get; set; } = "Body";
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class ScoringWeights
    {
        public int CriticalPoints { get; set; } = 8;
        public int HighPoints { get; set; } = 5;
        public int MediumPoints { get; set; } = 3;
        public int LowPoints { get; set; } = 1;
    }

    /// <summary>
    /// A must-have operation that should always be present on every estimate.
    /// Used by scoring to flag missing must-haves as issues.
    /// </summary>
    public class MustHaveOperation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Description { get; set; } = "";
        public string Section { get; set; } = "";  // Electrical, Vehicle Diagnostics, Misc, Refinish
        public string OpType { get; set; } = "Body";  // Body, Rfn, Mech, Sublet
        public string Category { get; set; } = "";  // Body Operations, Refinish Operations, Scanning, Calibration, Mechanical Operations
        public int MinCount { get; set; } = 1;
        public int PointDeduction { get; set; } = 5;
        public decimal ExpectedPrice { get; set; }
        public decimal ExpectedHours { get; set; }
        public decimal RefinishHours { get; set; }
        public bool Enabled { get; set; } = true;
    }

    #endregion
}
