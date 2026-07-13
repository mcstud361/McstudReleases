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

        // Cached read-only Standard (rebuilt on demand, invalidated on SaveConfig) — avoids
        // regenerating + re-normalizing the canonical list on every GetMustHaves() call.
        private List<MustHaveOperation>? _standardCache;
        private HashSet<string>? _standardNormCache;

        // Default labor rates
        public const decimal DEFAULT_BODY_RATE = 55.00m;
        public const decimal DEFAULT_PAINT_RATE = 55.00m;
        public const decimal DEFAULT_MECH_RATE = 95.00m;
        public const decimal DEFAULT_FRAME_RATE = 75.00m;
        public const decimal DEFAULT_GLASS_RATE = 55.00m;
        public const decimal DEFAULT_MATERIAL_RATE = 32.00m;  // Paint materials per refinish hour
        public const string DEFAULT_ESTIMATE_HEADING = "Estimate Total";

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
            SeedMustHaveGroups();
            MigrateBuiltInGroupOrder();
            SetBaseGroupFlags();
            SplitStandardFromShopMustHaves();
            DeduplicateMustHaves();
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
                    Conditions = d.Conditions,
                    Enabled = true
                });
            }
            SaveConfig();
        }

        /// <summary>
        /// Canonical list of all must-have operations — single source of truth.
        /// Used for seeding new configs and migrating existing ones.
        /// </summary>
        private static List<(string Desc, string Section, string OpType, string Category, decimal Hours, decimal RefinishHours, decimal Price, int Points, string Conditions)> GetCanonicalMustHaves()
        {
            return new()
            {
                // === MISCELLANEOUS OPERATIONS (standard order — these 10 first) ===
                ("Clean for Delivery",                              "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     1.0m, 0m,   0m,      3, "always"),
                ("Glass Cleaner",                                   "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0m,   0m,   2.00m,   2, "always"),
                ("Mask and Protect Removed Components",             "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0.5m, 0m,   10.00m,  3, "always"),
                ("N.J.S.A 13:1E-126 et seq. (A-901 Hauler Compliance Fee)", "MISCELLANEOUS OPERATIONS", "Body", "Body Operations", 0m, 0m, 7.50m,  2, "always"),
                ("N.J.A.C 7:26 (Solid Waste Disposal Fee)",                 "MISCELLANEOUS OPERATIONS", "Body", "Body Operations", 0m, 0m, 25.00m, 2, "always"),
                ("N.J.A.C 7:26G (Hazardous Waste Disposal Fee)",            "MISCELLANEOUS OPERATIONS", "Body", "Body Operations", 0m, 0m, 7.50m,  2, "always"),
                ("N.J.S.A 13:1E-99.11 et seq. (Recycling Fee)",             "MISCELLANEOUS OPERATIONS", "Body", "Body Operations", 0m, 0m, 20.00m, 2, "always"),
                ("Misc Hardware",                                   "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0m,   0m,   15.00m,  2, "always"),
                ("Steering Wheel Cover, Seat Cover, and Floor Mat", "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0.2m, 0m,   5.00m,   2, "always"),
                ("IPA Wipe 1x",                                     "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0.1m, 0m,   2.00m,   2, "when refinish"),  // confirm condition (paint prep?)
                ("Collision Wrap",                                  "MISCELLANEOUS OPERATIONS", "Body",   "Body Operations",     0.3m, 0m,   25.00m,  2, "always"),  // optional, default-off; count Input ($25/0.3 per area)

                // === VEHICLE DIAGNOSTICS ===
                ("Pre-Scan",                                        "VEHICLE DIAGNOSTICS", "Sublet", "Scanning",              0m,   0m,   150.00m, 5, "always"),
                ("In-Process Scan",                                 "VEHICLE DIAGNOSTICS", "Sublet", "Scanning",              0m,   0m,   150.00m, 5, "always"),
                ("Post Scan",                                       "VEHICLE DIAGNOSTICS", "Sublet", "Scanning",              0m,   0m,   150.00m, 5, "always"),
                ("Setup Scan Tool",                                 "VEHICLE DIAGNOSTICS", "Mech",   "Scanning",              0.2m, 0m,   0m,      2, "always"),
                ("Dynamic Systems Verification",                    "VEHICLE DIAGNOSTICS", "Mech",   "Scanning",              1.0m, 0m,   0m,      3, "always"),
                ("OEM Research",                                    "VEHICLE DIAGNOSTICS", "Mech",   "Scanning",              1.0m, 0m,   50.00m,  3, "always"),
                ("ADAS Diagnostic Report",                          "VEHICLE DIAGNOSTICS", "Body",   "Calibration",           0m,   0m,   25.00m,  3, "when adas"),
                ("Simulate Full Fluids for ADAS Calibrations",      "VEHICLE DIAGNOSTICS", "Mech",   "Calibration",           0.2m, 0m,   0m,      2, "when adas"),

                // === ELECTRICAL ===
                ("Disconnect and Reconnect Battery",                "ELECTRICAL", "Mech", "Mechanical Operations",            0.4m, 0m,   0m,      3, "always"),
                ("Test Battery Condition",                          "ELECTRICAL", "Mech", "Mechanical Operations",            0.2m, 0m,   0m,      2, "always"),
                ("Electronic Reset",                                "ELECTRICAL", "Mech", "Mechanical Operations",            0.5m, 0m,   0m,      3, "always"),
                ("Cover and Protect Electrical Connections",        "ELECTRICAL", "Mech", "Mechanical Operations",            0.3m, 0m,   5.00m,   2, "always"),
                ("Battery Support",                                 "ELECTRICAL", "Mech", "Mechanical Operations",            0.2m, 0m,   0m,      2, "always"),
                ("Charge and Maintain Battery",                     "ELECTRICAL", "Mech", "Mechanical Operations",            0.6m, 0m,   0m,      2, "when adas"),        // renamed from "...during ADAS" — confirm condition
                ("Mobile Cart for EV",                              "ELECTRICAL", "Mech", "Mechanical Operations",            0.5m, 0m,   50.00m,  2, "when high voltage"),  // labor type (M/F/0) still pending CCC field-wiring
                ("Verify No High Voltage",                         "ELECTRICAL", "Mech", "Mechanical Operations",            0.2m, 0m,   0m,      3, "when high voltage"),
                ("Service Mode",                                    "ELECTRICAL", "Mech", "Mechanical Operations",            0m,   0m,   0m,      2, "when high voltage"),  // TODO: price/hrs/labor/condition

                // === WELDING OPERATIONS ===
                ("Weld Blankets",                                  "WELDING OPERATIONS", "Body", "Body Operations",          0m,   0m,   25.00m,  3, "when welding"),
                ("Welder Setup",                                   "WELDING OPERATIONS", "Body", "Body Operations",          0.3m, 0m,   0m,      3, "when welding"),
                ("Welding Consumables",                            "WELDING OPERATIONS", "Body", "Body Operations",          0m,   0m,   35.00m,  3, "when welding"),
                ("Weld Through Primer",                            "WELDING OPERATIONS", "Body", "Body Operations",          0m,   0m,   15.00m,  3, "when welding"),
                ("Destructive Test Weld",                          "WELDING OPERATIONS", "Body", "Body Operations",          0.3m, 0m,   0m,      3, "when welding"),
                ("Remove and Cleanup Debris from Welding",         "WELDING OPERATIONS", "Body", "Body Operations",          0.5m, 0m,   0m,      2, "when welding"),
                ("R&I Electronics Within Weld Zones",              "WELDING OPERATIONS", "Body", "Body Operations",          0.3m, 0m,   0m,      3, "when welding"),

                // === MEASUREMENT ===
                ("Setup for Measurement",                          "MEASUREMENT", "Body", "Body Operations",                 0.5m, 0m,   0m,      3, "when structural"),
                ("Pre and Post Repair Measurements",               "MEASUREMENT", "Body", "Body Operations",                 1.0m, 0m,   0m,      3, "when structural"),

                // === AC AND CLIMATE ===
                ("R1234yf Refrigerant and Refrigerant Oil",        "AC AND CLIMATE", "Mech", "Mechanical Operations",        0m,   0m,   75.00m,  3, "when ac"),
                ("Cover and Protect AC Lines",                     "AC AND CLIMATE", "Mech", "Mechanical Operations",        0.2m, 0m,   0m,      2, "when ac"),
                ("Climate Control System Check",                   "AC AND CLIMATE", "Mech", "Mechanical Operations",        0.3m, 0m,   0m,      2, "when ac"),
                ("Bring Vehicle to Operating Temperatures",        "AC AND CLIMATE", "Mech", "Mechanical Operations",        0.3m, 0m,   0m,      2, "when ac"),
                ("Coolant",                                        "AC AND CLIMATE", "Mech", "Mechanical Operations",        0m,   0m,   25.00m,  2, "when ac"),

                // === WHEEL AND TIRE ===
                ("Torque Wheels to Spec",                          "WHEEL AND TIRE", "Mech", "Mechanical Operations",        0.2m, 0m,   0m,      3, "when wheels"),
                ("Mount and Balance Tires",                        "WHEEL AND TIRE", "Mech", "Mechanical Operations",        0.3m, 0m,   0m,      2, "when wheels"),
                ("Wheel Weights",                                  "WHEEL AND TIRE", "Mech", "Mechanical Operations",        0m,   0m,   10.00m,  2, "when wheels"),
                ("Tire Mounting Paste",                            "WHEEL AND TIRE", "Mech", "Mechanical Operations",        0m,   0m,   5.00m,   1, "when wheels"),
                ("Tire Disposal Fee",                              "WHEEL AND TIRE", "Mech", "Mechanical Operations",        0m,   0m,   5.00m,   1, "when wheels"),

                // === SRS AND RESTRAINTS ===
                ("Disable and Enable SRS",                         "SRS AND RESTRAINTS", "Mech", "Mechanical Operations",    0.3m, 0m,   0m,      3, "when srs"),
                ("SRS Safety Inspections",                         "SRS AND RESTRAINTS", "Mech", "Mechanical Operations",    0.5m, 0m,   0m,      3, "when srs"),
                ("Seatbelt and Steering Column Inspection",        "SRS AND RESTRAINTS", "Mech", "Mechanical Operations",    0.5m, 0m,   0m,      3, "when srs"),
                ("Airbag Residue Cleanup",                         "SRS AND RESTRAINTS", "Body", "Body Operations",          0.3m, 0m,   0m,      2, "when srs"),
                ("Restraint Control Module Program",               "SRS AND RESTRAINTS", "Sublet", "Mechanical Operations",  0m,   0m,   100.00m, 3, "when srs"),

                // === BODY ON FRAME ===
                ("Cut Up Shipping Crate",                          "BODY ON FRAME", "Body", "Body Operations",              0.5m, 0m,   0m,      2, "when body on frame"),
                ("Drain and Replace Fuel",                         "BODY ON FRAME", "Mech", "Mechanical Operations",        0.5m, 0m,   0m,      2, "when body on frame"),
                ("Depressurize Fuel Tank",                         "BODY ON FRAME", "Mech", "Mechanical Operations",        0.2m, 0m,   0m,      2, "when body on frame"),

                // === TOTAL LOSS ===
                ("Administration Fee",                             "TOTAL LOSS", "Body", "Body Operations",                  0m,   0m,   150.00m, 2, "when total loss"),
                ("Yard Fee",                                       "TOTAL LOSS", "Body", "Body Operations",                  0m,   0m,   50.00m,  2, "when total loss"),

                // === STOLEN RECOVERY ===
                ("Inspect Vehicle",                                "STOLEN RECOVERY", "Body", "Body Operations",             1.0m, 0m,   0m,      2, "when stolen recovery"),
                ("Fingerprint Powder Cleanup",                     "STOLEN RECOVERY", "Body", "Body Operations",             0.5m, 0m,   0m,      2, "when stolen recovery"),
                ("Wheel Lock Set",                                 "STOLEN RECOVERY", "Body", "Body Operations",             0m,   0m,   35.00m,  2, "when stolen recovery"),
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
            var canonicalByDesc = new Dictionary<string, (string Desc, string Section, string OpType, string Category, decimal Hours, decimal RefinishHours, decimal Price, int Points, string Conditions)>(StringComparer.OrdinalIgnoreCase);
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
                    // Migrate Conditions: update if still "always" but canonical says otherwise
                    if (mh.Conditions == "always" && match.Conditions != "always")
                    {
                        mh.Conditions = match.Conditions;
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
                    Conditions = c.Conditions,
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
            Dictionary<string, (string Desc, string Section, string OpType, string Category, decimal Hours, decimal RefinishHours, decimal Price, int Points, string Conditions)> canonical,
            out (string Desc, string Section, string OpType, string Category, decimal Hours, decimal RefinishHours, decimal Price, int Points, string Conditions) match)
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
        public decimal GetEffectiveMaterialRate() => _config.LaborRates.MaterialRate ?? DEFAULT_MATERIAL_RATE;
        public string GetEffectiveEstimateHeading() =>
            string.IsNullOrWhiteSpace(_config.EstimateHeading) ? DEFAULT_ESTIMATE_HEADING : _config.EstimateHeading;
        public void SetEstimateHeading(string heading)
        {
            _config.EstimateHeading = heading ?? DEFAULT_ESTIMATE_HEADING;
            SaveConfig();
        }

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
            _standardCache = null;       // invalidate cached Standard (groups/config may have changed)
            _standardNormCache = null;
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
                case "material": _config.LaborRates.MaterialRate = value; break;
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

        #region Must-Have Groups

        /// <summary>
        /// Canonical order of built-in must-have groups — single source of truth.
        /// Used both to seed new configs and to migrate existing ones to the standard order.
        /// To change the shipped section order, reorder this array and bump
        /// CURRENT_GROUP_ORDER_VERSION below.
        /// </summary>
        private static readonly (string Name, string Color)[] CanonicalGroupOrder = new[]
        {
            ("ELECTRICAL",               "#64B4FF"),
            ("VEHICLE DIAGNOSTICS",      "#50C8DC"),
            ("MISCELLANEOUS OPERATIONS", "#C8B464"),
            ("WELDING OPERATIONS",       "#FF9664"),
            ("MEASUREMENT",              "#C8B4FF"),
            ("AC AND CLIMATE",           "#64DCDC"),
            ("WHEEL AND TIRE",           "#64C896"),
            ("SRS AND RESTRAINTS",       "#FF6464"),
            ("BODY ON FRAME",            "#C89664"),
            ("TOTAL LOSS",               "#DC6464"),
            ("STOLEN RECOVERY",          "#9664C8"),
        };

        // Bump this whenever CanonicalGroupOrder changes to push the new order to existing shops once.
        private const int CURRENT_GROUP_ORDER_VERSION = 1;

        // The three locked base categories that make up the read-only Standard (always present, can't be removed).
        private static readonly HashSet<string> BaseGroupNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "ELECTRICAL", "VEHICLE DIAGNOSTICS", "MISCELLANEOUS OPERATIONS"
        };

        // Bump when the legacy split logic changes; the one-time split runs once per version.
        private const int CURRENT_SPLIT_VERSION = 1;

        // Standard operations that stay read-only but the shop can still toggle on/off (checkbox
        // enabled). Stored in NormalizeMustHaveDesc form.
        private static readonly HashSet<string> ToggleableStandardDescriptions = new(StringComparer.Ordinal)
        {
            "mobile cart for ev",
            "verify no high voltage",
            "service mode",
            "collision wrap",
            "oem research"
        };

        // Optional base ops that start OFF (unchecked) — the shop enables them when relevant.
        private static readonly HashSet<string> DefaultOffStandardDescriptions = new(StringComparer.Ordinal)
        {
            "collision wrap"
        };

        // Base ops whose price + labor the shop may type its own value for (floored at the canonical minimum).
        private static readonly HashSet<string> EditableValueStandardDescriptions = new(StringComparer.Ordinal)
        {
            "oem research"
        };

        // Founder-defined Inputs for base SOP ops (keyed by NormalizeMustHaveDesc). Each op can
        // switch between these states; the chosen state's values become the op's effective values.
        private static readonly Dictionary<string, List<MustHaveInputState>> StandardInputStates = new(StringComparer.Ordinal)
        {
            ["pre scan"] = ScanInputStates(),
            ["in process scan"] = ScanInputStates(),
            ["post scan"] = ScanInputStates(),
            ["disconnect and reconnect battery"] = new()
            {
                new MustHaveInputState { Label = "Single", HoverText = "Single battery — 0.4 hr Mechanical", Hours = 0.4m, BodyLaborCategory = "Mechanical", CccOperationType = "Rpr" },
                new MustHaveInputState { Label = "Dual",   HoverText = "Dual battery — 0.8 hr Mechanical",   Hours = 0.8m, BodyLaborCategory = "Mechanical", CccOperationType = "Rpr" },
            },
        };

        private static List<MustHaveInputState> ScanInputStates() => new()
        {
            new MustHaveInputState { Label = "$",     HoverText = "$150 flat (Sublet)",       Price = 150.00m, CccOperationType = "Repl" },
            new MustHaveInputState { Label = "Labor", HoverText = "1.0 hr Mechanical labor",   Hours = 1.0m, BodyLaborCategory = "Mechanical", CccOperationType = "Rpr" },
        };

        // Base SOP ops with a typed count Input (keyed by NormalizeMustHaveDesc → the count's label).
        // The op's per-unit price/hours scale by the typed count (e.g. 2 wipes = 2× price & labor).
        private static readonly Dictionary<string, string> StandardCountInputs = new(StringComparer.Ordinal)
        {
            ["ipa wipe 1x"] = "Wipes",
            ["collision wrap"] = "Areas",
        };

        // CCC op-type + labor category per base op (from the shop's CCC data). Keyed by the exact
        // canonical description. Op-type must be a price-bearing type (Repl / R&I) for lines that
        // carry a dollar amount — CCC voids a manual price on a labor-only "Rpr". Labor category
        // drives the M/F/B letter on labor lines. (Scans + Disconnect Battery come from InputStates.)
        private static readonly Dictionary<string, (string CccOp, string LaborCat)> StandardOpMeta =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // Misc
            ["Clean for Delivery"] = ("Rpr", "Body"),
            ["Glass Cleaner"] = ("Repl", "Body"),
            ["Mask and Protect Removed Components"] = ("Repl", "Body"),
            ["N.J.S.A 13:1E-126 et seq. (A-901 Hauler Compliance Fee)"] = ("Repl", "Body"),
            ["N.J.A.C 7:26 (Solid Waste Disposal Fee)"] = ("Repl", "Body"),
            ["N.J.A.C 7:26G (Hazardous Waste Disposal Fee)"] = ("Repl", "Body"),
            ["N.J.S.A 13:1E-99.11 et seq. (Recycling Fee)"] = ("Repl", "Body"),
            ["Misc Hardware"] = ("Repl", "Body"),
            ["Steering Wheel Cover, Seat Cover, and Floor Mat"] = ("Repl", "Body"),
            ["IPA Wipe 1x"] = ("Repl", "Body"),
            ["Collision Wrap"] = ("Repl", "Body"),
            // Vehicle Diagnostics
            ["Setup Scan Tool"] = ("R&I", "Mechanical"),
            ["Dynamic Systems Verification"] = ("Rpr", "Mechanical"),
            ["OEM Research"] = ("Repl", "Mechanical"),
            ["ADAS Diagnostic Report"] = ("Repl", "Mechanical"),
            ["Simulate Full Fluids for ADAS Calibrations"] = ("R&I", "Mechanical"),
            // Electrical
            ["Test Battery Condition"] = ("Rpr", "Mechanical"),
            ["Electronic Reset"] = ("Rpr", "Mechanical"),
            ["Cover and Protect Electrical Connections"] = ("Repl", "Mechanical"),
            ["Battery Support"] = ("Rpr", "Mechanical"),
            ["Charge and Maintain Battery"] = ("Rpr", "Mechanical"),
            ["Verify No High Voltage"] = ("Rpr", "Mechanical"),
        };

        /// <summary>
        /// Seed built-in must-have groups from the canonical order if none exist.
        /// </summary>
        private void SeedMustHaveGroups()
        {
            if (_config.MustHaveGroups.Count > 0) return;

            for (int i = 0; i < CanonicalGroupOrder.Length; i++)
            {
                _config.MustHaveGroups.Add(new MustHaveGroup
                {
                    Name = CanonicalGroupOrder[i].Name,
                    AccentColor = CanonicalGroupOrder[i].Color,
                    SortOrder = i,
                    IsBuiltIn = true,
                    IsBase = BaseGroupNames.Contains(CanonicalGroupOrder[i].Name)
                });
            }
            _config.BuiltInGroupOrderVersion = CURRENT_GROUP_ORDER_VERSION;
            SaveConfig();
        }

        /// <summary>
        /// One-time migration: re-order an existing shop's built-in groups to match the
        /// current canonical standard. Runs once per version bump (guarded by
        /// BuiltInGroupOrderVersion), so shops can still reorder afterward without it resetting.
        /// </summary>
        private void MigrateBuiltInGroupOrder()
        {
            if (_config.BuiltInGroupOrderVersion >= CURRENT_GROUP_ORDER_VERSION) return;

            for (int i = 0; i < CanonicalGroupOrder.Length; i++)
            {
                var grp = _config.MustHaveGroups.FirstOrDefault(g =>
                    g.IsBuiltIn && g.Name.Equals(CanonicalGroupOrder[i].Name, StringComparison.OrdinalIgnoreCase));
                if (grp != null)
                    grp.SortOrder = i;
            }

            _config.BuiltInGroupOrderVersion = CURRENT_GROUP_ORDER_VERSION;
            SaveConfig();
        }

        public List<MustHaveGroup> GetMustHaveGroups() =>
            _config.MustHaveGroups.OrderBy(g => g.SortOrder).ToList();

        public void AddMustHaveGroup(MustHaveGroup group)
        {
            if (_config.MustHaveGroups.Count > 0)
                group.SortOrder = _config.MustHaveGroups.Max(g => g.SortOrder) + 1;
            _config.MustHaveGroups.Add(group);
            SaveConfig();
        }

        public void UpdateMustHaveGroup(MustHaveGroup group)
        {
            var idx = _config.MustHaveGroups.FindIndex(g => g.Id == group.Id);
            if (idx >= 0) _config.MustHaveGroups[idx] = group;
            SaveConfig();
        }

        public void DeleteMustHaveGroup(string groupId)
        {
            // Unassign operations back to Section-based grouping
            foreach (var mh in _config.MustHaves.Where(m => m.GroupId == groupId))
                mh.GroupId = null;
            _config.MustHaveGroups.RemoveAll(g => g.Id == groupId);
            SaveConfig();
        }

        public void MoveOperationToGroup(string operationId, string? groupId)
        {
            var mh = _config.MustHaves.FirstOrDefault(m => m.Id == operationId);
            if (mh != null)
            {
                mh.GroupId = groupId;
                SaveConfig();
            }
        }

        #endregion

        #region Must-Haves

        public static readonly string[] KnownVehicleFuelTypes = { "Gas Vehicle", "Electric Vehicle", "Hybrid Vehicle" };

        /// <summary>
        /// The read-only MET Standard must-haves, generated fresh from code each call.
        /// Never written to the shop's config — always reflects the latest shipped standard.
        /// Stable IDs ("std:" + normalized description) so templates can reference them and
        /// they stay consistent across launches.
        /// </summary>
        public List<MustHaveOperation> GetStandardMustHaves()
        {
            if (_standardCache != null) return _standardCache;

            var groupIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in _config.MustHaveGroups)
                if (!groupIdByName.ContainsKey(g.Name)) groupIdByName[g.Name] = g.Id;

            var disabled = new HashSet<string>(_config.DisabledStandardOps.Select(NormalizeMustHaveDesc));
            var list = new List<MustHaveOperation>();
            foreach (var d in GetCanonicalMustHaves())
            {
                var norm = NormalizeMustHaveDesc(d.Desc);
                // Toggleable if it's a named toggleable op, OR it lives in a non-base (optional) category
                bool optional = ToggleableStandardDescriptions.Contains(norm) || !BaseGroupNames.Contains(d.Section);
                // Default-off ops start unchecked (presence in DisabledStandardOps = ON for them);
                // normal optional ops start on (presence = OFF); locked base ops are always on.
                bool enabled = !optional ? true
                    : (DefaultOffStandardDescriptions.Contains(norm) ? disabled.Contains(norm) : !disabled.Contains(norm));
                var op = new MustHaveOperation
                {
                    Id = "std:" + norm,
                    Description = d.Desc,
                    Section = d.Section,
                    GroupId = groupIdByName.TryGetValue(d.Section, out var gid) ? gid : null,
                    OpType = d.OpType,
                    Category = d.Category,
                    ExpectedPrice = d.Price,
                    ExpectedHours = d.Hours,
                    RefinishHours = d.RefinishHours,
                    PointDeduction = d.Points,
                    Conditions = d.Conditions,
                    Enabled = enabled,
                    Optional = optional,
                    IsStandard = true
                };

                // CCC op-type + labor category. Op-type: a price-bearing type (Replace) so CCC keeps the
                // dollar amount. Labor category drives the labor-type letter on paste ("0" for Body/regular,
                // "M" for Mechanical) matching the shop's CCC export format.
                if (StandardOpMeta.TryGetValue(d.Desc, out var meta))
                {
                    op.CccOperationType = meta.CccOp;
                    op.BodyLaborCategory = meta.LaborCat;
                }

                // Attach founder-defined Inputs and apply the chosen state's values
                if (StandardInputStates.TryGetValue(norm, out var states) && states.Count > 0)
                {
                    op.InputStates = states;
                    var selLabel = _config.InputSelections.TryGetValue(norm, out var sl) ? sl : states[0].Label;
                    var sel = states.FirstOrDefault(s => s.Label == selLabel) ?? states[0];
                    op.ExpectedPrice = sel.Price;
                    op.ExpectedHours = sel.Hours;
                    op.RefinishHours = sel.RefinishHours;
                    if (!string.IsNullOrEmpty(sel.BodyLaborCategory)) op.BodyLaborCategory = sel.BodyLaborCategory;
                    if (!string.IsNullOrEmpty(sel.CccOperationType)) op.CccOperationType = sel.CccOperationType;
                    if (sel.Quantity > 0) op.Quantity = sel.Quantity;
                }

                // Attach a typed count Input if this op has one (values stay per-unit; count applied at point of use)
                if (StandardCountInputs.TryGetValue(norm, out var countLabel))
                    op.CountInputLabel = countLabel;

                // Editable-value op: the shop may type its own price/labor, floored at the canonical minimum
                if (EditableValueStandardDescriptions.Contains(norm))
                {
                    op.EditableValue = true;
                    if (_config.ValueOverrides.TryGetValue(norm, out var ov))
                    {
                        op.ExpectedPrice = Math.Max(ov.Price, d.Price);
                        op.ExpectedHours = Math.Max(ov.Hours, d.Hours);
                    }
                }

                list.Add(op);
            }

            _standardCache = list;
            _standardNormCache = new HashSet<string>(list.Select(s => NormalizeMustHaveDesc(s.Description)));
            return list;
        }

        /// <summary>
        /// Effective must-haves = the read-only Standard (always shown as-is, never editable in
        /// place) PLUS the shop's own items that are NOT part of the Standard (custom ops, etc.).
        /// There are NO in-place overrides of the Standard — a shop customizes by copying to a
        /// template. Any leftover shop item matching a Standard description is suppressed here so
        /// the Standard is the single source of truth for its operations.
        /// </summary>
        public List<MustHaveOperation> GetMustHaves()
        {
            var standard = GetStandardMustHaves();
            var stdNorms = _standardNormCache!; // populated by GetStandardMustHaves above

            var result = new List<MustHaveOperation>(standard);
            foreach (var m in _config.MustHaves)
                if (!stdNorms.Contains(NormalizeMustHaveDesc(m.Description)))
                    result.Add(m);
            return result;
        }

        /// <summary>The shop's own must-haves only (custom ops + overrides), excluding the Standard.</summary>
        public List<MustHaveOperation> GetShopMustHaves() => _config.MustHaves;

        /// <summary>Current per-op toggles turned OFF (normalized descriptions) — used when saving a template.</summary>
        public List<string> GetDisabledStandardOps() => new List<string>(_config.DisabledStandardOps);

        /// <summary>The shop's chosen Input state label for an op (null = use the op's first/default state).</summary>
        public string? GetInputSelection(string description)
        {
            var norm = NormalizeMustHaveDesc(description);
            return _config.InputSelections.TryGetValue(norm, out var label) ? label : null;
        }

        /// <summary>Set the shop's default Input state for an op (e.g. scans → "Labor"). Persisted.</summary>
        public void SetInputSelection(string description, string stateLabel)
        {
            _config.InputSelections[NormalizeMustHaveDesc(description)] = stateLabel;
            SaveConfig();
        }

        /// <summary>Current Input selections (copy) — used when saving a template.</summary>
        public Dictionary<string, string> GetInputSelections() => new Dictionary<string, string>(_config.InputSelections);

        /// <summary>The shop's typed price/labor override for an editable-value op (null = use the Standard default).</summary>
        public MustHaveValueOverride? GetValueOverride(string description)
        {
            var norm = NormalizeMustHaveDesc(description);
            return _config.ValueOverrides.TryGetValue(norm, out var ov) ? ov : null;
        }

        /// <summary>Set the shop's typed price/labor for an editable-value op (e.g. OEM Research). Persisted; floored at the minimum on read.</summary>
        public void SetValueOverride(string description, decimal price, decimal hours)
        {
            _config.ValueOverrides[NormalizeMustHaveDesc(description)] = new MustHaveValueOverride { Price = price, Hours = hours };
            SaveConfig();
        }

        /// <summary>Current editable-value overrides (copy) — used when saving a template.</summary>
        public Dictionary<string, MustHaveValueOverride> GetValueOverrides()
            => _config.ValueOverrides.ToDictionary(kv => kv.Key, kv => new MustHaveValueOverride { Price = kv.Value.Price, Hours = kv.Value.Hours });

        /// <summary>The shop's default count for a count-Input op (min 1).</summary>
        public int GetCountSelection(string description)
        {
            var norm = NormalizeMustHaveDesc(description);
            return _config.CountSelections.TryGetValue(norm, out var c) && c > 0 ? c : 1;
        }

        /// <summary>Set the shop's default count for a count-Input op. Persisted.</summary>
        public void SetCountSelection(string description, int count)
        {
            if (count < 1) count = 1;
            _config.CountSelections[NormalizeMustHaveDesc(description)] = count;
            SaveConfig();
        }

        /// <summary>Current count defaults (copy) — used when saving a template.</summary>
        public Dictionary<string, int> GetCountSelections() => new Dictionary<string, int>(_config.CountSelections);

        /// <summary>Toggle a (toggleable) Standard operation on/off for this shop. Persisted in DisabledStandardOps.</summary>
        public void SetStandardOpEnabled(string description, bool enabled)
        {
            var norm = NormalizeMustHaveDesc(description);
            _config.DisabledStandardOps.RemoveAll(d => NormalizeMustHaveDesc(d) == norm);
            // Default-off ops store presence = ON; normal ops store presence = OFF.
            bool listed = DefaultOffStandardDescriptions.Contains(norm) ? enabled : !enabled;
            if (listed) _config.DisabledStandardOps.Add(norm);
            SaveConfig();
        }

        /// <summary>
        /// Get must-haves filtered by insurance company and vehicle fuel type context.
        /// Returns enabled must-haves that are universal (no tags) OR match the given context.
        /// </summary>
        public List<MustHaveOperation> GetMustHavesForContext(string? insuranceCompany, string? vehicleFuelType)
        {
            return GetMustHaves().Where(m => m.Enabled && IsOpCategoryActive(m) && MatchesContext(m, insuranceCompany, vehicleFuelType)).ToList();
        }

        /// <summary>
        /// Is the operation's category active? Base categories (Electrical/VD/Misc) are always
        /// active; optional categories are active only when their Included flag is on.
        /// </summary>
        private bool IsOpCategoryActive(MustHaveOperation mh)
        {
            var grp = _config.MustHaveGroups.FirstOrDefault(g => g.Id == mh.GroupId)
                   ?? _config.MustHaveGroups.FirstOrDefault(g => g.Name.Equals(mh.Section, StringComparison.OrdinalIgnoreCase));
            if (grp == null) return true;          // ungrouped → always active
            return grp.IsBase || grp.Included;     // base always; optional only when included
        }

        /// <summary>Set the IsBase flag on built-in base categories (idempotent).</summary>
        private void SetBaseGroupFlags()
        {
            bool changed = false;
            foreach (var g in _config.MustHaveGroups)
            {
                bool shouldBeBase = g.IsBuiltIn && BaseGroupNames.Contains(g.Name);
                if (g.IsBase != shouldBeBase) { g.IsBase = shouldBeBase; changed = true; }
            }
            if (changed) SaveConfig();
        }

        /// <summary>
        /// One-time migration to the read-only-Standard model: removes items from the shop's
        /// list that are unmodified duplicates of the current Standard (stale auto-seeds), and
        /// keeps everything else (shop edits = overrides, custom ops, retired-standard leftovers).
        /// Non-destructive to genuine shop data; writes a one-time backup first. Version-guarded.
        /// </summary>
        private void SplitStandardFromShopMustHaves()
        {
            if (_config.MustHavesSplitVersion >= CURRENT_SPLIT_VERSION) return;

            try { if (File.Exists(_configPath)) File.Copy(_configPath, _configPath + ".pre-standard-split.bak", true); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[GhostConfig] split backup failed: {ex.Message}"); }

            var standard = GetStandardMustHaves();
            var stdByNorm = new Dictionary<string, MustHaveOperation>();
            foreach (var s in standard)
                stdByNorm[NormalizeMustHaveDesc(s.Description)] = s;

            var kept = new List<MustHaveOperation>();
            foreach (var m in _config.MustHaves)
            {
                var norm = NormalizeMustHaveDesc(m.Description);
                if (stdByNorm.TryGetValue(norm, out var std) && StandardValuesMatch(m, std))
                    continue; // unmodified copy of the current standard → drop (served read-only now)
                m.IsStandard = false;
                kept.Add(m);
            }
            int removed = _config.MustHaves.Count - kept.Count;
            _config.MustHaves = kept;
            _config.MustHavesSplitVersion = CURRENT_SPLIT_VERSION;
            SaveConfig();
            System.Diagnostics.Debug.WriteLine($"[GhostConfig] Standard split: dropped {removed} stale seeds, kept {kept.Count} shop items");
        }

        private static bool StandardValuesMatch(MustHaveOperation a, MustHaveOperation b)
        {
            return a.ExpectedPrice == b.ExpectedPrice
                && a.ExpectedHours == b.ExpectedHours
                && a.RefinishHours == b.RefinishHours
                && a.PointDeduction == b.PointDeduction
                && string.Equals(a.Conditions ?? "", b.Conditions ?? "", StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.OpType ?? "", b.OpType ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesContext(MustHaveOperation mh, string? insuranceCompany, string? vehicleFuelType)
        {
            // Insurance filter: empty list = universal, otherwise must match
            bool insuranceOk = mh.InsuranceCompanies.Count == 0 ||
                (!string.IsNullOrEmpty(insuranceCompany) &&
                 mh.InsuranceCompanies.Any(ic => ic.Equals(insuranceCompany, StringComparison.OrdinalIgnoreCase)));

            // Vehicle type filter: empty list = universal, otherwise must match
            bool vehicleOk = mh.VehicleTypes.Count == 0 ||
                (!string.IsNullOrEmpty(vehicleFuelType) &&
                 mh.VehicleTypes.Any(vt => vt.Equals(vehicleFuelType, StringComparison.OrdinalIgnoreCase)));

            return insuranceOk && vehicleOk;
        }

        public void AddMustHave(MustHaveOperation mh)
        {
            // Dedup: skip if a must-have with the same description already exists (fuzzy match)
            var normNew = NormalizeMustHaveDesc(mh.Description);
            if (_config.MustHaves.Any(existing =>
                NormalizeMustHaveDesc(existing.Description) == normNew))
                return;

            _config.MustHaves.Add(mh);
            SaveConfig();
        }

        /// <summary>
        /// Normalize text for must-have matching: lowercase, replace &amp;/hyphens/slashes,
        /// collapse whitespace. Shared across Screen OCR, Import Scrubber, and Ghost Estimate.
        /// </summary>
        public static string NormalizeMustHaveDesc(string desc)
        {
            if (string.IsNullOrWhiteSpace(desc)) return "";
            return System.Text.RegularExpressions.Regex.Replace(
                desc.ToLowerInvariant()
                    .Replace("&", " and ")
                    .Replace("/", " ")
                    .Replace("-", " ")
                    // Strip common OCR punctuation artifacts (commas, apostrophes, periods, colons, semicolons)
                    .Replace(",", "")
                    .Replace("'", "")
                    .Replace("'", "")
                    .Replace(".", "")
                    .Replace(":", "")
                    .Replace(";", ""),
                @"\s+", " ").Trim();
        }

        /// <summary>
        /// Extract significant words (> 3 chars) from normalized text for word-overlap matching.
        /// </summary>
        public static string[] ExtractSignificantWords(string normalizedText)
        {
            return normalizedText.Split(' ')
                .Where(w => w.Length > 3)
                .Distinct()
                .ToArray();
        }

        /// <summary>
        /// Unified must-have matching: checks if a detected text matches a must-have operation.
        /// Shared across Screen OCR, Import Scrubber, and Ghost Estimate for consistent behavior.
        /// When strictMode is true (for raw OCR text blobs), skips loose substring checks and
        /// requires ALL significant words to match to avoid false positives from CCC section headers.
        /// </summary>
        public static bool MatchesMustHave(string detectedNorm, string mustHaveNorm, string[] mustHaveWords, bool strictMode = false)
        {
            // Check 1: Exact match
            if (detectedNorm == mustHaveNorm) return true;

            if (!strictMode)
            {
                // Check 2: Detected text contains full must-have
                if (detectedNorm.Contains(mustHaveNorm)) return true;

                // Check 3: Must-have contains detected text (if detected is >= 45% length)
                if (mustHaveNorm.Contains(detectedNorm) && detectedNorm.Length >= mustHaveNorm.Length * 0.45)
                    return true;
            }

            // Check 4: Proportional word overlap with fuzzy matching for OCR errors
            if (mustHaveWords.Length > 0)
            {
                var matchCount = mustHaveWords.Count(w => FuzzyContainsWord(detectedNorm, w));
                // Strict mode: 80% of words must match (allows 1 OCR misread per 5 words,
                //   but still prevents false positives from section headers like "ELECTRICAL").
                //   Short must-haves (1-2 words) require all words even in strict mode.
                // Normal mode: short must-haves require all, longer require 60%
                int threshold = strictMode
                    ? (mustHaveWords.Length <= 2
                        ? mustHaveWords.Length
                        : (int)Math.Ceiling(mustHaveWords.Length * 0.8))
                    : mustHaveWords.Length <= 2
                        ? mustHaveWords.Length
                        : (int)Math.Ceiling(mustHaveWords.Length * 0.6);

                if (matchCount >= threshold)
                    return true;
            }

            if (!strictMode)
            {
                // Check 5: PDF truncation — if detected is a single significant word (4+ chars)
                // and it matches the first significant word of the must-have, treat as truncated match.
                // PDF parsing often keeps only the first word (e.g., "Clean" from "Clean for Delivery").
                if (mustHaveWords.Length >= 2)
                {
                    var detectedWords = detectedNorm.Split(' ')
                        .Where(w => w.Length >= 4).ToArray();
                    if (detectedWords.Length == 1 && mustHaveWords.Length > 0 &&
                        FuzzyContainsWord(detectedWords[0], mustHaveWords[0]))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if text contains a word, with fuzzy matching for OCR misreads.
        /// Exact match first, then Levenshtein distance for words > 3 chars.
        /// </summary>
        public static bool FuzzyContainsWord(string text, string word)
        {
            // Exact substring match first (fast path)
            if (text.Contains(word)) return true;

            // Fuzzy: only for words longer than 3 chars (short words are too ambiguous)
            if (word.Length <= 3) return false;

            // Max edit distance: 1 for 4-5 char words, 2 for 6+ char words
            int maxDistance = word.Length <= 5 ? 1 : 2;

            // Split text into words and check each against the target word
            var textWords = text.Split(' ');
            foreach (var tw in textWords)
            {
                if (tw.Length < word.Length - maxDistance || tw.Length > word.Length + maxDistance)
                    continue; // Length difference too large, skip

                if (LevenshteinDistance(tw, word) <= maxDistance)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Compute Levenshtein edit distance between two strings.
        /// </summary>
        public static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;

            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++)
                prev[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }

            return prev[b.Length];
        }

        /// <summary>
        /// Convenience: check if a ParsedEstimateLine matches a must-have description.
        /// Normalizes PartName + Description and checks via MatchesMustHave.
        /// </summary>
        public static bool LineMatchesMustHave(string mustHaveDesc, ParsedEstimateLine line)
        {
            var mhNorm = NormalizeMustHaveDesc(mustHaveDesc);
            var mhWords = ExtractSignificantWords(mhNorm);

            var partNorm = NormalizeMustHaveDesc(line.PartName);
            var descNorm = NormalizeMustHaveDesc(line.Description);
            var combined = NormalizeMustHaveDesc(line.PartName + " " + line.Description);

            return MatchesMustHave(combined, mhNorm, mhWords)
                || (!string.IsNullOrEmpty(partNorm) && MatchesMustHave(partNorm, mhNorm, mhWords))
                || (!string.IsNullOrEmpty(descNorm) && MatchesMustHave(descNorm, mhNorm, mhWords));
        }

        /// <summary>
        /// Remove duplicate must-haves (same normalized description). Keeps the first occurrence.
        /// </summary>
        public void DeduplicateMustHaves()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toRemove = new List<string>();
            foreach (var mh in _config.MustHaves)
            {
                var norm = NormalizeMustHaveDesc(mh.Description);
                if (!seen.Add(norm))
                    toRemove.Add(mh.Id);
            }
            if (toRemove.Count > 0)
            {
                _config.MustHaves.RemoveAll(m => toRemove.Contains(m.Id));
                System.Diagnostics.Debug.WriteLine($"[GhostConfig] Removed {toRemove.Count} duplicate must-haves");
                SaveConfig();
            }
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

        /// <summary>
        /// Swap the list positions of two must-haves. Display order within a section is the
        /// position in the MustHaves list, so this is how manual up/down reordering persists.
        /// No-op if either id isn't found (e.g. a not-yet-saved pending add).
        /// </summary>
        public void SwapMustHaveOrder(string idA, string idB)
        {
            int ia = _config.MustHaves.FindIndex(m => m.Id == idA);
            int ib = _config.MustHaves.FindIndex(m => m.Id == idB);
            if (ia >= 0 && ib >= 0 && ia != ib)
            {
                (_config.MustHaves[ia], _config.MustHaves[ib]) = (_config.MustHaves[ib], _config.MustHaves[ia]);
                SaveConfig();
            }
        }

        // --- Must-Have Templates ---

        public List<MustHaveTemplate> GetMustHaveTemplates() => _config.MustHaveTemplates;

        public void SaveMustHaveTemplate(MustHaveTemplate template)
        {
            // Replace if same name exists
            _config.MustHaveTemplates.RemoveAll(t => t.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase));
            _config.MustHaveTemplates.Add(template);
            SaveConfig();
        }

        public void DeleteMustHaveTemplate(string id)
        {
            _config.MustHaveTemplates.RemoveAll(t => t.Id == id);
            SaveConfig();
        }

        /// <summary>
        /// Apply a category-set template: include exactly the optional categories the template
        /// names (the base 3 categories stay always-on). Persists.
        /// </summary>
        public void ApplyCategoryTemplate(MustHaveTemplate t)
        {
            var wanted = new HashSet<string>(t.IncludedCategories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var g in _config.MustHaveGroups)
            {
                if (g.IsBase) continue;
                g.Included = wanted.Contains(g.Name);
            }
            _config.DisabledStandardOps = new List<string>(t.DisabledOps ?? new List<string>());
            _config.InputSelections = new Dictionary<string, string>(t.InputSelections ?? new Dictionary<string, string>());
            _config.ValueOverrides = (t.ValueOverrides ?? new Dictionary<string, MustHaveValueOverride>())
                .ToDictionary(kv => kv.Key, kv => new MustHaveValueOverride { Price = kv.Value.Price, Hours = kv.Value.Hours });
            _config.CountSelections = new Dictionary<string, int>(t.CountSelections ?? new Dictionary<string, int>());
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
        public List<MustHaveTemplate> MustHaveTemplates { get; set; } = new();
        public List<MustHaveGroup> MustHaveGroups { get; set; } = new();
        public ScoringWeights ScoringWeights { get; set; } = new();
        public string EstimateHeading { get; set; } = GhostConfigService.DEFAULT_ESTIMATE_HEADING;
        /// <summary>
        /// Bumped when the canonical built-in group order changes, so a one-time
        /// migration can re-order existing shops' built-in groups to the new standard.
        /// </summary>
        public int BuiltInGroupOrderVersion { get; set; } = 0;
        /// <summary>Bumped when the legacy merged-list → read-only-Standard split runs (once).</summary>
        public int MustHavesSplitVersion { get; set; } = 0;
        /// <summary>Normalized descriptions of toggleable Standard ops the shop has turned OFF.</summary>
        public List<string> DisabledStandardOps { get; set; } = new();
        /// <summary>Chosen Input state per op: normalized description → selected state label.</summary>
        public Dictionary<string, string> InputSelections { get; set; } = new();
        /// <summary>Shop-typed price/labor overrides for editable-value ops: normalized description → override.</summary>
        public Dictionary<string, MustHaveValueOverride> ValueOverrides { get; set; } = new();
        /// <summary>Default count per count-Input op: normalized description → count.</summary>
        public Dictionary<string, int> CountSelections { get; set; } = new();
    }

    public class GhostLaborRates
    {
        public decimal? BodyRate { get; set; }
        public decimal? PaintRate { get; set; }
        public decimal? MechRate { get; set; }
        public decimal? FrameRate { get; set; }
        public decimal? GlassRate { get; set; }
        public decimal? MaterialRate { get; set; }
        /// <summary>
        /// When true, the scrubber uses rates detected from the estimate itself
        /// instead of the shop rates configured above. Defaults to true.
        /// </summary>
        public bool UseEstimateRates { get; set; } = true;
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
    /// One selectable state of an operation "Input" — e.g. "$" vs "Labor", or "Single" vs "Dual".
    /// The user picks a state (default in Must-Haves, or live on the screen-read line) and its
    /// values become the operation's effective values (what the scrubber checks / what inserts to CCC).
    /// </summary>
    public class MustHaveInputState
    {
        public string Label { get; set; } = "";        // chip text, e.g. "$", "Labor", "Single", "Dual"
        public string HoverText { get; set; } = "";     // preview shown on hover, e.g. "1.0 hr Mechanical"
        public decimal Price { get; set; }
        public decimal Hours { get; set; }              // body labor hours
        public decimal RefinishHours { get; set; }
        public string BodyLaborCategory { get; set; } = "";
        public string CccOperationType { get; set; } = "";
        public int Quantity { get; set; } = 1;
    }

    /// <summary>The shop's typed price/labor override for an editable-value op (e.g. OEM Research).</summary>
    public class MustHaveValueOverride
    {
        public decimal Price { get; set; }
        public decimal Hours { get; set; }
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
        public string? GroupId { get; set; }  // null = groups by Section (backward compat)
        public string OpType { get; set; } = "Body";  // Body, Rfn, Mech, Sublet (legacy labor type)
        public string Category { get; set; } = "";  // Body Operations, Refinish Operations, Scanning, Calibration, Mechanical Operations
        public string CccOperationType { get; set; } = "";  // Repl, Rpr, Refn, R&I, Sect, Algn, Subl, Blnd
        public string BodyLaborCategory { get; set; } = "";  // Body, Mechanical, Frame, Structural, Diagnostic, Electrical, Glass, PDR, User Defined 1-4
        public int Quantity { get; set; } = 1;
        public int MinCount { get; set; } = 1;
        public int PointDeduction { get; set; } = 5;
        public decimal ExpectedPrice { get; set; }
        public decimal ExpectedHours { get; set; }
        public decimal RefinishHours { get; set; }
        public string Conditions { get; set; } = "always";
        public bool Enabled { get; set; } = true;
        /// <summary>True = part of the read-only MET Standard (generated from code, not editable in place). Not persisted on shop items.</summary>
        public bool IsStandard { get; set; }
        /// <summary>Standard op the shop may still toggle on/off (checkbox enabled) even though it's read-only.</summary>
        public bool Optional { get; set; }
        /// <summary>Selectable Input states (e.g. $ ⇄ Labor). Empty = no Inputs. The chosen state's values are applied to this op.</summary>
        public List<MustHaveInputState> InputStates { get; set; } = new();
        /// <summary>If set (e.g. "Wipes"), this op has a typed count Input — its per-unit price/hours scale by the count.</summary>
        public string CountInputLabel { get; set; } = "";
        /// <summary>True = the shop may type its own price + labor on this op (floored at the Standard minimum).</summary>
        public bool EditableValue { get; set; }
        public List<string> InsuranceCompanies { get; set; } = new(); // empty = applies to all
        public List<string> VehicleTypes { get; set; } = new();       // empty = applies to all
    }

    /// <summary>
    /// User-saved must-have template — a named snapshot of checked operation descriptions.
    /// </summary>
    public class MustHaveTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public List<string> Descriptions { get; set; } = new();  // (legacy) operation descriptions
        /// <summary>Optional category names folded in on top of the base 3 — the template model.</summary>
        public List<string> IncludedCategories { get; set; } = new();
        /// <summary>Normalized descriptions of toggleable ops turned OFF in this template.</summary>
        public List<string> DisabledOps { get; set; } = new();
        /// <summary>Chosen Input states in this template: normalized description → selected state label.</summary>
        public Dictionary<string, string> InputSelections { get; set; } = new();
        /// <summary>Editable-value overrides in this template: normalized description → price/labor.</summary>
        public Dictionary<string, MustHaveValueOverride> ValueOverrides { get; set; } = new();
        /// <summary>Default counts in this template: normalized description → count.</summary>
        public Dictionary<string, int> CountSelections { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// A named group for organizing must-have operations in the UI.
    /// Built-in groups correspond to canonical sections; custom groups are user-created.
    /// </summary>
    public class MustHaveGroup
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string AccentColor { get; set; } = "#64B4FF";
        public int SortOrder { get; set; }
        public bool IsBuiltIn { get; set; }
        /// <summary>True = locked base category (Electrical / Vehicle Diagnostics / Misc) — always present, can't be removed.</summary>
        public bool IsBase { get; set; }
        /// <summary>Whether this (optional) category is folded into the active scrubbing set. Base categories are always included regardless.</summary>
        public bool Included { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    #endregion
}
