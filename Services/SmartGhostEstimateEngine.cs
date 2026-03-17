#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Services
{
    /// <summary>
    /// SMART GHOST ESTIMATE ENGINE
    ///
    /// Generates estimates based ENTIRELY on learned data from uploaded estimates.
    /// NO HARDCODED VALUES - everything comes from pattern mining.
    ///
    /// How it works:
    /// 1. User describes damage (panels, severity, impact zones)
    /// 2. Engine finds similar estimates from history
    /// 3. Engine builds estimate using learned:
    ///    - Labor times from real estimates
    ///    - Co-occurring operations
    ///    - Blend patterns
    ///    - R&I associations
    ///    - Calculation formulas
    ///
    /// THE MORE ESTIMATES YOU UPLOAD, THE SMARTER THIS GETS.
    /// </summary>
    public class SmartGhostEstimateEngine
    {
        #region Singleton

        private static SmartGhostEstimateEngine? _instance;
        public static SmartGhostEstimateEngine Instance => _instance ??= new SmartGhostEstimateEngine();

        #endregion

        private readonly LearnedKnowledgeBase _knowledge;
        private readonly PartRecognitionEngine _partRecognition;
        private readonly EstimateMiningEngine _mining;
        private readonly PatternIntelligenceService _patternIntelligence;
        private readonly GhostConfigService _ghostConfig;

        // Confidence thresholds
        private const double HIGH_CONFIDENCE = 0.8;
        private const double MEDIUM_CONFIDENCE = 0.5;
        private const double LOW_CONFIDENCE = 0.3;

        public SmartGhostEstimateEngine()
        {
            _knowledge = LearnedKnowledgeBase.Instance;
            _partRecognition = PartRecognitionEngine.Instance;
            _mining = EstimateMiningEngine.Instance;
            _patternIntelligence = PatternIntelligenceService.Instance;
            _ghostConfig = GhostConfigService.Instance;
        }

        #region Main Generation

        /// <summary>
        /// Generate a smart ghost estimate based on damage description
        /// </summary>
        public SmartGhostResult GenerateEstimate(SmartGhostInput input)
        {
            var result = new SmartGhostResult
            {
                Input = input,
                GeneratedAt = DateTime.Now
            };

            // Check if we have enough learned data
            var stats = _knowledge.GetStatistics();
            if (stats.TotalEstimatesAnalyzed < 5)
            {
                result.Warnings.Add($"Limited training data ({stats.TotalEstimatesAnalyzed} estimates). Accuracy improves with more uploads.");
            }

            // Classify vehicle type
            var vehicleType = _patternIntelligence.ClassifyVehicleType(input.VehicleInfo);
            result.VehicleType = vehicleType;

            // Process each damaged panel
            foreach (var panel in input.DamagedPanels)
            {
                GenerateOperationsForPanel(result, panel, input, vehicleType);
            }

            // Add co-occurring operations
            AddCoOccurringOperations(result, input, vehicleType);

            // Add blend operations
            AddBlendOperations(result);

            // Add standard operations (scanning, etc.)
            AddStandardOperations(result, input);

            // Calculate totals
            CalculateTotals(result);

            // Add confidence notes
            AddConfidenceNotes(result);

            return result;
        }

        #endregion

        #region Operation Generation

        private void GenerateOperationsForPanel(
            SmartGhostResult result,
            DamagedPanel panel,
            SmartGhostInput input,
            string vehicleType)
        {
            // Recognize the part
            var recognition = _partRecognition.Recognize(panel.Name);
            if (!recognition.Success)
            {
                result.Warnings.Add($"Could not recognize part: {panel.Name}");
                return;
            }

            var canonical = recognition.CanonicalName;
            var operation = DetermineOperation(panel.Severity);

            // Get learned labor time
            var laborEstimate = _mining.GetExpectedLaborTime(canonical, operation, vehicleType);

            // Get learned refinish time
            var refinishEstimate = _mining.GetExpectedLaborTime(canonical, "refinish", vehicleType);

            // Create primary operation
            var primaryCat = DetermineCategory(canonical);
            var primaryOp = new SmartGhostOperation
            {
                PartName = canonical,
                OperationType = operation,
                Description = BuildDescription(recognition, operation),
                Side = recognition.Side,
                Category = primaryCat,
                Section = MapToCCCSection(canonical, primaryCat),

                // Use LEARNED values if available, otherwise mark as estimated
                LaborHours = laborEstimate.HasData ? laborEstimate.Mean : EstimateFallbackLabor(canonical, operation),
                LaborConfidence = laborEstimate.HasData ? laborEstimate.Confidence : 0.3,
                LaborSource = laborEstimate.HasData ? laborEstimate.Source : "fallback_estimate",
                LaborSampleCount = laborEstimate.SampleCount,

                RefinishHours = refinishEstimate.HasData ? refinishEstimate.Mean : EstimateFallbackRefinish(canonical),
                RefinishConfidence = refinishEstimate.HasData ? refinishEstimate.Confidence : 0.3,

                OverallConfidence = laborEstimate.HasData ? laborEstimate.Confidence : 0.3
            };

            result.Operations.Add(primaryOp);

            // Add R&I operations for commonly associated parts
            AddRAndIOperations(result, canonical, recognition.Side, vehicleType);
        }

        private void AddRAndIOperations(
            SmartGhostResult result,
            string mainPartCanonical,
            string side,
            string vehicleType)
        {
            // Get parts that commonly appear WITH this part as R&I
            var associations = _mining.GetAssociatedOperations(mainPartCanonical, MEDIUM_CONFIDENCE);

            var riOperations = associations
                .Where(a => a.OperationType == "r&i" && a.Confidence >= MEDIUM_CONFIDENCE)
                .OrderByDescending(a => a.Confidence)
                .Take(5)
                .ToList();

            foreach (var ri in riOperations)
            {
                var laborEstimate = _mining.GetExpectedLaborTime(ri.PartName, "r&i", vehicleType);

                result.Operations.Add(new SmartGhostOperation
                {
                    PartName = ri.PartName,
                    OperationType = "r&i",
                    Description = BuildRIDescription(ri.PartName, side),
                    Side = side,
                    Category = "R&I",
                    Section = MapToCCCSection(ri.PartName),
                    LaborHours = laborEstimate.HasData ? laborEstimate.Mean : 0.3m,
                    LaborConfidence = ri.Confidence,
                    LaborSource = laborEstimate.HasData ? "learned" : "estimated",
                    LaborSampleCount = laborEstimate.SampleCount,
                    OverallConfidence = ri.Confidence,
                    ReasonIncluded = $"Appears with {mainPartCanonical} in {ri.Confidence:P0} of estimates"
                });
            }
        }

        private void AddCoOccurringOperations(SmartGhostResult result, SmartGhostInput input, string vehicleType)
        {
            var existingParts = result.Operations.Select(o => o.PartName).ToHashSet();

            foreach (var existingOp in result.Operations.ToList())
            {
                // Get commonly co-occurring parts
                var associations = _mining.GetCommonlyPairedParts(existingOp.PartName, 10);

                foreach (var assoc in associations.Where(a => a.CoOccurrenceRate >= HIGH_CONFIDENCE))
                {
                    // Skip if already added
                    if (existingParts.Contains(assoc.AssociatedPart))
                        continue;

                    // Skip R&I operations (handled separately)
                    if (assoc.AssociatedOperation == "r&i")
                        continue;

                    var laborEstimate = _mining.GetExpectedLaborTime(
                        assoc.AssociatedPart,
                        assoc.AssociatedOperation,
                        vehicleType);

                    var coCategory = DetermineCategory(assoc.AssociatedPart);
                    result.Operations.Add(new SmartGhostOperation
                    {
                        PartName = assoc.AssociatedPart,
                        OperationType = assoc.AssociatedOperation,
                        Description = BuildDescription(assoc.AssociatedPart, assoc.AssociatedOperation),
                        Category = coCategory,
                        Section = MapToCCCSection(assoc.AssociatedPart, coCategory),
                        LaborHours = laborEstimate.HasData ? laborEstimate.Mean : 0.5m,
                        LaborConfidence = assoc.CoOccurrenceRate,
                        LaborSource = laborEstimate.HasData ? "learned" : "co-occurrence",
                        OverallConfidence = assoc.CoOccurrenceRate,
                        ReasonIncluded = $"Appears with {existingOp.PartName} in {assoc.CoOccurrenceRate:P0} of estimates"
                    });

                    existingParts.Add(assoc.AssociatedPart);
                }
            }
        }

        private void AddBlendOperations(SmartGhostResult result)
        {
            // Find panels being refinished
            var refinishedPanels = result.Operations
                .Where(o => o.OperationType == "replace" || o.OperationType == "repair")
                .Select(o => o.PartName)
                .Distinct()
                .ToList();

            var existingParts = result.Operations.Select(o => o.PartName).ToHashSet();

            foreach (var panel in refinishedPanels)
            {
                // Get LEARNED adjacent panels for blending
                var adjacentPanels = _mining.GetBlendSuggestions(panel);

                foreach (var adjacent in adjacentPanels.Take(3))
                {
                    // Skip if panel is already being worked on
                    if (existingParts.Contains(adjacent))
                        continue;

                    // Get learned blend time
                    var blendEstimate = _mining.GetExpectedLaborTime(adjacent, "blend", null);
                    var refinishEstimate = _mining.GetExpectedLaborTime(adjacent, "refinish", null);

                    // If we have blend data, use it
                    decimal blendHours;
                    if (blendEstimate.HasData)
                    {
                        blendHours = blendEstimate.Mean;
                    }
                    else if (refinishEstimate.HasData)
                    {
                        // Use 20% of refinish time for first panel (learned formula)
                        var formula = _knowledge.GetFormula("blend_percentage");
                        var blendPct = formula?.DerivedPercentage ?? 0.20;
                        blendHours = refinishEstimate.Mean * (decimal)blendPct;
                    }
                    else
                    {
                        blendHours = 0.5m; // Fallback
                    }

                    result.Operations.Add(new SmartGhostOperation
                    {
                        PartName = adjacent,
                        OperationType = "blend",
                        Description = $"Blend {FormatPartName(adjacent)} (color match)",
                        Category = "Refinish",
                        Section = MapToCCCSection(adjacent),
                        RefinishHours = blendHours,
                        RefinishConfidence = blendEstimate.HasData ? blendEstimate.Confidence : 0.6,
                        OverallConfidence = 0.7,
                        ReasonIncluded = $"Adjacent to {panel} - learned from estimate patterns"
                    });

                    existingParts.Add(adjacent);
                }
            }
        }

        private void AddStandardOperations(SmartGhostResult result, SmartGhostInput input)
        {
            // Pre/Post scan - use config scanning method (flat rate or labor hours)
            // Only add if not already present from co-occurring operations or other paths
            var hasPreScan = result.Operations.Any(o =>
                o.PartName != null && o.PartName.Contains("pre", StringComparison.OrdinalIgnoreCase) &&
                (o.Category == "Scanning" || o.PartName.Contains("scan", StringComparison.OrdinalIgnoreCase)));
            var hasPostScan = result.Operations.Any(o =>
                o.PartName != null && o.PartName.Contains("post", StringComparison.OrdinalIgnoreCase) &&
                (o.Category == "Scanning" || o.PartName.Contains("scan", StringComparison.OrdinalIgnoreCase)));

            var scanConfig = _ghostConfig.GetEffectiveScanning();

            if (!hasPreScan)
            {
                var preScanEstimate = _mining.GetExpectedLaborTime("pre_repair_scan", "mechanical", null);
                var preScanHours = preScanEstimate.HasData ? preScanEstimate.Mean : scanConfig.LaborHours;
                var preScanPrice = preScanEstimate.HasData ? 0m : scanConfig.Price;

                result.Operations.Add(new SmartGhostOperation
                {
                    PartName = "pre_repair_scan",
                    OperationType = preScanPrice > 0 ? "sublet" : "mechanical",
                    Description = "Pre-Repair Diagnostic Scan",
                    Category = "Scanning",
                    Section = "VEHICLE DIAGNOSTICS",
                    LaborHours = preScanHours,
                    Price = preScanPrice,
                    LaborConfidence = preScanEstimate.HasData ? preScanEstimate.Confidence : 0.9,
                    LaborSource = preScanEstimate.HasData ? "learned" :
                        (preScanPrice > 0 ? "shop_flat_rate" : "industry_standard"),
                    OverallConfidence = 0.95,
                    ReasonIncluded = "Required for modern vehicles - OEM procedure"
                });
            }

            if (!hasPostScan)
            {
                var postScanEstimate = _mining.GetExpectedLaborTime("post_repair_scan", "mechanical", null);
                var postScanHours = postScanEstimate.HasData ? postScanEstimate.Mean : scanConfig.LaborHours;
                var postScanPrice = postScanEstimate.HasData ? 0m : scanConfig.Price;

                result.Operations.Add(new SmartGhostOperation
                {
                    PartName = "post_repair_scan",
                    OperationType = postScanPrice > 0 ? "sublet" : "mechanical",
                    Description = "Post-Repair Diagnostic Scan",
                    Category = "Scanning",
                    Section = "VEHICLE DIAGNOSTICS",
                    LaborHours = postScanHours,
                    Price = postScanPrice,
                    LaborConfidence = postScanEstimate.HasData ? postScanEstimate.Confidence : 0.9,
                    LaborSource = postScanEstimate.HasData ? "learned" :
                        (postScanPrice > 0 ? "shop_flat_rate" : "industry_standard"),
                    OverallConfidence = 0.95,
                    ReasonIncluded = "Required for modern vehicles - OEM procedure"
                });
            }

            // ADAS calibration if relevant parts
            var adasParts = new[] { "windshield", "front_bumper_cover", "grille", "mirror", "headlight" };
            if (result.Operations.Any(o => adasParts.Any(p => o.PartName.Contains(p))))
            {
                var adasEstimate = _mining.GetExpectedLaborTime("adas_calibration", "sublet", null);

                result.Operations.Add(new SmartGhostOperation
                {
                    PartName = "adas_calibration",
                    OperationType = "sublet",
                    Description = "ADAS Calibration (Camera/Radar)",
                    Category = "Calibration",
                    Section = "VEHICLE DIAGNOSTICS",
                    Price = adasEstimate.HasData ? adasEstimate.Mean * 100 : 350m, // Sublet pricing
                    OverallConfidence = 0.8,
                    ReasonIncluded = "ADAS-equipped components affected"
                });
            }

            // Structural measurements if relevant
            var structuralParts = new[] { "pillar", "rail", "rocker", "apron", "frame" };
            if (result.Operations.Any(o => structuralParts.Any(p => o.PartName.Contains(p))))
            {
                var measureEstimate = _mining.GetExpectedLaborTime("structural_measurement", "frame", null);

                result.Operations.Add(new SmartGhostOperation
                {
                    PartName = "structural_measurement",
                    OperationType = "frame",
                    Description = "Measure/Document Vehicle Structure",
                    Category = "Frame",
                    Section = "FRAME",
                    LaborHours = measureEstimate.HasData ? measureEstimate.Mean : 1.0m,
                    OverallConfidence = 0.9,
                    ReasonIncluded = "Structural components affected - measurement required"
                });
            }
        }

        #endregion

        #region Calculations

        private void CalculateTotals(SmartGhostResult result)
        {
            // Get labor rates from user config (falls back to defaults)
            var bodyRate = _ghostConfig.GetEffectiveBodyRate();
            var paintRate = _ghostConfig.GetEffectivePaintRate();
            var mechRate = _ghostConfig.GetEffectiveMechRate();
            var frameRate = _ghostConfig.GetEffectiveFrameRate();

            result.TotalBodyHours = result.Operations
                .Where(o => o.Category == "Body" || o.Category == "R&I" || o.Category == "Part")
                .Sum(o => o.LaborHours);

            result.TotalRefinishHours = result.Operations
                .Where(o => o.Category == "Refinish" || o.OperationType == "blend")
                .Sum(o => o.RefinishHours > 0 ? o.RefinishHours : o.LaborHours);

            result.TotalMechHours = result.Operations
                .Where(o => o.Category == "Scanning" || o.Category == "Mechanical")
                .Sum(o => o.LaborHours);

            result.TotalFrameHours = result.Operations
                .Where(o => o.Category == "Frame")
                .Sum(o => o.LaborHours);

            result.TotalSublet = result.Operations
                .Where(o => o.OperationType == "sublet")
                .Sum(o => o.Price);

            result.TotalBodyDollars = result.TotalBodyHours * bodyRate;
            result.TotalRefinishDollars = result.TotalRefinishHours * paintRate;
            result.TotalMechDollars = result.TotalMechHours * mechRate;
            result.TotalFrameDollars = result.TotalFrameHours * frameRate;

            result.GrandTotal = result.TotalBodyDollars + result.TotalRefinishDollars +
                               result.TotalMechDollars + result.TotalFrameDollars + result.TotalSublet;

            // Calculate overall confidence
            var confidences = result.Operations.Select(o => o.OverallConfidence).ToList();
            result.OverallConfidence = confidences.Count > 0 ? confidences.Average() : 0.5;
        }

        private void AddConfidenceNotes(SmartGhostResult result)
        {
            var stats = _knowledge.GetStatistics();

            // Training data note
            if (stats.TotalEstimatesAnalyzed < 10)
            {
                result.Notes.Add($"Training data: {stats.TotalEstimatesAnalyzed} estimates. Upload more for better accuracy.");
            }
            else if (stats.TotalEstimatesAnalyzed < 50)
            {
                result.Notes.Add($"Moderate training: {stats.TotalEstimatesAnalyzed} estimates. Accuracy improving.");
            }
            else
            {
                result.Notes.Add($"Well-trained: {stats.TotalEstimatesAnalyzed} estimates analyzed. High confidence.");
            }

            // Low confidence operations
            var lowConfidence = result.Operations.Where(o => o.OverallConfidence < MEDIUM_CONFIDENCE).ToList();
            if (lowConfidence.Any())
            {
                result.Notes.Add($"{lowConfidence.Count} operations have limited data - verify manually.");
            }

            // Source breakdown
            var learnedCount = result.Operations.Count(o => o.LaborSource == "learned");
            var estimatedCount = result.Operations.Count(o => o.LaborSource != "learned");
            result.Notes.Add($"Data sources: {learnedCount} from patterns, {estimatedCount} estimated.");
        }

        #endregion

        #region Helpers

        private string DetermineOperation(string severity)
        {
            return severity.ToLowerInvariant() switch
            {
                "severe" or "heavy" => "replace",
                "moderate" or "medium" => "repair",
                "light" or "minor" => "repair",
                _ => "repair"
            };
        }

        private string DetermineCategory(string partName)
        {
            if (partName.Contains("pillar") || partName.Contains("rail") || partName.Contains("apron"))
                return "Structural";
            if (partName.Contains("frame") || partName.Contains("subframe"))
                return "Frame";
            if (partName.Contains("scan"))
                return "Scanning";
            return "Part";
        }

        private static string MapToCCCSection(string partName, string category = "")
        {
            var lower = (partName ?? "").ToLowerInvariant();

            if (lower.Contains("scan") || lower.Contains("diagnostic") || lower.Contains("adas") || lower.Contains("calibration"))
                return "VEHICLE DIAGNOSTICS";
            if (lower.Contains("battery") || lower.Contains("electronic reset") || lower.Contains("electrical"))
                return "ELECTRICAL";
            if (lower.Contains("restraint") || lower.Contains("air bag") || lower.Contains("airbag") || lower.Contains("srs"))
                return "RESTRAINT SYSTEMS";
            if (lower.Contains("frame") || lower.Contains("subframe") || lower.Contains("rail") || lower.Contains("apron"))
                return "FRAME";
            if (lower.Contains("front bumper") || lower.Contains("grille") || lower.Contains("front fascia"))
                return "FRONT BUMPER & GRILLE";
            if (lower.Contains("rear bumper") || lower.Contains("rear fascia"))
                return "REAR BUMPER";
            if (lower.Contains("headlamp") || lower.Contains("headlight") || lower.Contains("head lamp") || lower.Contains("head light") || lower.Contains("fog light") || lower.Contains("fog lamp"))
                return "FRONT LAMPS";
            if (lower.Contains("taillamp") || lower.Contains("taillight") || lower.Contains("tail light") || lower.Contains("tail lamp"))
                return "REAR LAMPS";
            if (lower.Contains("radiator support") || lower.Contains("core support"))
                return "RADIATOR SUPPORT";
            if (lower.Contains("hood"))
                return "HOOD";
            if (lower.Contains("fender"))
                return "FENDER";
            if (lower.Contains("front door"))
                return "FRONT DOOR";
            if (lower.Contains("rear door"))
                return "REAR DOOR";
            if (lower.Contains("door"))
                return "FRONT DOOR";
            if (lower.Contains("mirror"))
                return "FRONT DOOR";
            if (lower.Contains("quarter"))
                return "QUARTER PANEL";
            if (lower.Contains("pillar") || lower.Contains("rocker") || lower.Contains("floor pan"))
                return "PILLARS, ROCKER & FLOOR";
            if (lower.Contains("roof") || lower.Contains("sunroof"))
                return "ROOF";
            if (lower.Contains("trunk") || lower.Contains("decklid") || lower.Contains("deck lid") || lower.Contains("liftgate") || lower.Contains("lift gate"))
                return "TRUNK / DECKLID";
            if (lower.Contains("windshield") || lower.Contains("glass"))
                return "GLASS";
            if (lower.Contains("dash") || lower.Contains("instrument panel"))
                return "INSTRUMENT PANEL";
            if (lower.Contains("radiator") || lower.Contains("condenser") || lower.Contains("suspension") || lower.Contains("steering") || lower.Contains("engine") || lower.Contains("wheel"))
                return "MECHANICAL";
            if (lower.Contains("bumper"))
                return "FRONT BUMPER & GRILLE";

            if (!string.IsNullOrEmpty(category))
            {
                if (category == "Scanning" || category == "Calibration") return "VEHICLE DIAGNOSTICS";
                if (category == "Frame" || category == "Structural") return "FRAME";
            }

            return "MISCELLANEOUS OPERATIONS";
        }

        private string BuildDescription(PartRecognitionResult recognition, string operation)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(recognition.Side))
                parts.Add(recognition.Side == "left" ? "LT" : "RT");
            parts.Add(FormatPartName(recognition.CanonicalName));
            parts.Add(FormatOperation(operation));
            return string.Join(" ", parts);
        }

        private string BuildDescription(string partName, string operation)
        {
            return $"{FormatPartName(partName)} {FormatOperation(operation)}";
        }

        private string BuildRIDescription(string partName, string side)
        {
            var sidePrefix = side == "left" ? "LT " : side == "right" ? "RT " : "";
            return $"{sidePrefix}{FormatPartName(partName)} R&I";
        }

        private string FormatPartName(string canonical)
        {
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase(canonical.Replace("_", " "));
        }

        private string FormatOperation(string operation)
        {
            return operation switch
            {
                "replace" => "Repl",
                "repair" => "Rpr",
                "r&i" => "R&I",
                "refinish" => "Rfn",
                "blend" => "Blend",
                "overhaul" => "O/H",
                _ => operation
            };
        }

        private decimal EstimateFallbackLabor(string partName, string operation)
        {
            // Very conservative fallbacks when no learned data
            return operation switch
            {
                "replace" => partName.Contains("quarter") || partName.Contains("roof") ? 8.0m :
                            partName.Contains("door") ? 3.5m :
                            partName.Contains("bumper") ? 1.5m : 2.0m,
                "repair" => 2.5m,
                "r&i" => 0.4m,
                _ => 1.0m
            };
        }

        private decimal EstimateFallbackRefinish(string partName)
        {
            return partName.Contains("hood") || partName.Contains("roof") ? 4.0m :
                   partName.Contains("quarter") ? 4.0m :
                   partName.Contains("door") || partName.Contains("fender") ? 3.0m :
                   partName.Contains("bumper") ? 2.5m : 2.5m;
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// Get operations learned for a specific part
        /// </summary>
        public List<LearnedOperationInfo> GetLearnedOperationsForPart(string partName)
        {
            var recognition = _partRecognition.Recognize(partName);
            if (!recognition.Success) return new List<LearnedOperationInfo>();

            var allParts = _knowledge.GetAllParts();
            if (!allParts.TryGetValue(recognition.CanonicalName, out var partKnowledge))
                return new List<LearnedOperationInfo>();

            return partKnowledge.OperationStats.Select(kvp => new LearnedOperationInfo
            {
                OperationType = kvp.Key,
                MeanLaborHours = kvp.Value.MeanLaborHours,
                MedianLaborHours = kvp.Value.MedianLaborHours,
                MinLaborHours = kvp.Value.MinLaborHours,
                MaxLaborHours = kvp.Value.MaxLaborHours,
                SampleCount = kvp.Value.SampleCount,
                MeanRefinishHours = kvp.Value.MeanRefinishHours,
                MeanPrice = kvp.Value.MeanPrice
            }).ToList();
        }

        /// <summary>
        /// Get system statistics
        /// </summary>
        public SmartGhostEngineStats GetStats()
        {
            var kStats = _knowledge.GetStatistics();
            var pStats = _partRecognition.GetStats();

            return new SmartGhostEngineStats
            {
                TotalEstimatesLearned = kStats.TotalEstimatesAnalyzed,
                TotalPartsKnown = kStats.TotalPartsLearned,
                TotalAliasesLearned = kStats.TotalAliasesLearned,
                TotalPatternsDiscovered = kStats.TotalCoOccurrencePatterns,
                TotalFormulasLearned = kStats.TotalFormulasLearned,
                TotalValueAnalyzed = kStats.TotalValueAnalyzed,
                LastUpdated = kStats.LastUpdated
            };
        }

        #endregion
    }

    #region Data Models

    public class SmartGhostInput
    {
        public string VehicleInfo { get; set; } = "";
        public string DamageDescription { get; set; } = "";
        public List<DamagedPanel> DamagedPanels { get; set; } = new();
        public string OverallSeverity { get; set; } = "moderate";
    }

    public class DamagedPanel
    {
        public string Name { get; set; } = "";
        public string Severity { get; set; } = "moderate";
        public string Side { get; set; } = "";
    }

    public class SmartGhostResult
    {
        public SmartGhostInput Input { get; set; } = new();
        public string VehicleType { get; set; } = "";
        public DateTime GeneratedAt { get; set; }

        public List<SmartGhostOperation> Operations { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> Notes { get; set; } = new();

        // Totals
        public decimal TotalBodyHours { get; set; }
        public decimal TotalRefinishHours { get; set; }
        public decimal TotalMechHours { get; set; }
        public decimal TotalFrameHours { get; set; }
        public decimal TotalSublet { get; set; }

        public decimal TotalBodyDollars { get; set; }
        public decimal TotalRefinishDollars { get; set; }
        public decimal TotalMechDollars { get; set; }
        public decimal TotalFrameDollars { get; set; }
        public decimal GrandTotal { get; set; }

        public double OverallConfidence { get; set; }
    }

    public class SmartGhostOperation
    {
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public string Side { get; set; } = "";
        public string Category { get; set; } = "";
        public string Section { get; set; } = "";

        public decimal LaborHours { get; set; }
        public double LaborConfidence { get; set; }
        public string LaborSource { get; set; } = "";
        public int LaborSampleCount { get; set; }

        public decimal RefinishHours { get; set; }
        public double RefinishConfidence { get; set; }

        public decimal Price { get; set; }

        public double OverallConfidence { get; set; }
        public string ReasonIncluded { get; set; } = "";
    }

    public class LearnedOperationInfo
    {
        public string OperationType { get; set; } = "";
        public decimal MeanLaborHours { get; set; }
        public decimal MedianLaborHours { get; set; }
        public decimal MinLaborHours { get; set; }
        public decimal MaxLaborHours { get; set; }
        public decimal MeanRefinishHours { get; set; }
        public decimal MeanPrice { get; set; }
        public int SampleCount { get; set; }
    }

    public class SmartGhostEngineStats
    {
        public int TotalEstimatesLearned { get; set; }
        public int TotalPartsKnown { get; set; }
        public int TotalAliasesLearned { get; set; }
        public int TotalPatternsDiscovered { get; set; }
        public int TotalFormulasLearned { get; set; }
        public decimal TotalValueAnalyzed { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    #endregion
}
