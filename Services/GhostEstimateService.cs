#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Ghost Estimate Service - Generates AI estimates for comparison with user estimates.
    ///
    /// The "Ghost" writes an estimate independently based on:
    /// 1. Damage description/location
    /// 2. Vehicle info
    /// 3. Learned patterns from uploaded estimates
    /// 4. Part relationship knowledge
    /// 5. ACTUAL labor times, pricing, and terminology from real estimates
    ///
    /// User compares their estimate against the ghost to find:
    /// - Operations they missed
    /// - Operations ghost missed (learning opportunity for the AI)
    /// </summary>
    public class GhostEstimateService
    {
        private static GhostEstimateService? _instance;
        public static GhostEstimateService Instance => _instance ??= new GhostEstimateService();

        private readonly SmartEstimateEngine _smartEngine;
        private readonly EstimateLearningService _learningService;
        private readonly PatternIntelligenceService _patternIntelligence;
        private readonly OperationKnowledgeService _knowledgeService;
        private readonly SmartSuggestionService _suggestionService;
        private readonly LearnedKnowledgeBase _knowledgeBase;
        private readonly EstimateHistoryDatabase _historyDb;
        private readonly GhostConfigService _ghostConfig;
        private readonly ExcelGhostDataProvider _excelProvider;

        // Loaded knowledge from real estimates
        private GhostIncludedNotIncludedData? _operationsData;
        private GhostPartOperationsFormulas? _formulasData;

        // Damage severity multipliers for labor time estimation
        private static readonly Dictionary<string, decimal> SeverityMultipliers = new()
        {
            ["light"] = 0.7m,    // Light damage - minor repair
            ["moderate"] = 1.0m, // Moderate - standard repair
            ["heavy"] = 1.5m,    // Heavy - major repair or replace
            ["severe"] = 2.0m    // Severe - structural/total section
        };

        // Impact zone to affected panels mapping
        private static readonly Dictionary<string, string[]> ImpactZonePanels = new()
        {
            ["front_center"] = new[] { "front bumper cover", "grille", "hood", "radiator support", "condenser", "radiator" },
            ["front_left"] = new[] { "front bumper cover", "fender", "headlight", "fog light", "fender liner", "apron" },
            ["front_right"] = new[] { "front bumper cover", "fender", "headlight", "fog light", "fender liner", "apron" },
            ["side_left_front"] = new[] { "fender", "front door", "mirror", "rocker panel", "a-pillar" },
            ["side_left_rear"] = new[] { "rear door", "quarter panel", "rocker panel", "c-pillar" },
            ["side_right_front"] = new[] { "fender", "front door", "mirror", "rocker panel", "a-pillar" },
            ["side_right_rear"] = new[] { "rear door", "quarter panel", "rocker panel", "c-pillar" },
            ["rear_center"] = new[] { "rear bumper cover", "trunk lid", "tail light", "rear body panel" },
            ["rear_left"] = new[] { "rear bumper cover", "quarter panel", "tail light" },
            ["rear_right"] = new[] { "rear bumper cover", "quarter panel", "tail light" },
            ["top"] = new[] { "roof", "sunroof", "headliner" },
            ["undercarriage"] = new[] { "subframe", "suspension", "exhaust", "fuel tank" }
        };

        public GhostEstimateService()
        {
            _smartEngine = SmartEstimateEngine.Instance;
            _learningService = EstimateLearningService.Instance;
            _patternIntelligence = PatternIntelligenceService.Instance;
            _knowledgeService = OperationKnowledgeService.Instance;
            _suggestionService = SmartSuggestionService.Instance;
            _knowledgeBase = LearnedKnowledgeBase.Instance;
            _historyDb = EstimateHistoryDatabase.Instance;
            _ghostConfig = GhostConfigService.Instance;
            _excelProvider = ExcelGhostDataProvider.Instance;
            LoadRealEstimateData();
        }

        /// <summary>
        /// Load actual estimate data from JSON knowledge files
        /// </summary>
        private void LoadRealEstimateData()
        {
            try
            {
                var basePath = AppContext.BaseDirectory;

                // Load IncludedNotIncluded.json - contains real MET times and operations
                var iniPath = Path.Combine(basePath, "Data", "IncludedNotIncluded.json");
                if (!File.Exists(iniPath))
                    iniPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "IncludedNotIncluded.json");

                if (File.Exists(iniPath))
                {
                    var json = File.ReadAllText(iniPath);
                    _operationsData = JsonSerializer.Deserialize<GhostIncludedNotIncludedData>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    System.Diagnostics.Debug.WriteLine($"[Ghost] Loaded {_operationsData?.Operations?.Count ?? 0} operations from IncludedNotIncluded.json");
                }

                // Load PartOperationsFormulas.json - contains formulas and labor calculations
                var formulasPath = Path.Combine(basePath, "Data", "PartOperationsFormulas.json");
                if (!File.Exists(formulasPath))
                    formulasPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "PartOperationsFormulas.json");

                if (File.Exists(formulasPath))
                {
                    var json = File.ReadAllText(formulasPath);
                    _formulasData = JsonSerializer.Deserialize<GhostPartOperationsFormulas>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    System.Diagnostics.Debug.WriteLine("[Ghost] Loaded PartOperationsFormulas.json");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] Error loading estimate data: {ex.Message}");
            }
        }

        /// <summary>
        /// Detect affected panels from input without generating the full estimate.
        /// Used by the UI to show per-panel severity selection before generation.
        /// </summary>
        public List<AffectedPanel> DetectAffectedPanels(GhostEstimateInput input)
        {
            return GetAffectedPanels(input);
        }

        /// <summary>
        /// Generate a ghost estimate based on damage input
        /// </summary>
        public GhostEstimateResult GenerateGhostEstimate(GhostEstimateInput input)
        {
            var result = new GhostEstimateResult
            {
                VehicleInfo = input.VehicleInfo,
                DamageDescription = input.DamageDescription,
                GeneratedAt = DateTime.Now
            };

            // Classify vehicle type for pattern matching
            var vehicleType = _patternIntelligence.ClassifyVehicleType(input.VehicleInfo);
            result.VehicleType = vehicleType;

            // Step 1: Determine affected panels from impact zones
            var affectedPanels = GetAffectedPanels(input);

            // Apply per-panel severity overrides
            if (input.PanelSeverities.Count > 0)
            {
                foreach (var panel in affectedPanels)
                {
                    if (input.PanelSeverities.TryGetValue(panel.Name, out var panelSeverity))
                    {
                        panel.Severity = panelSeverity;
                    }
                }
            }

            // Step 2: Generate operations for each panel (using per-panel severity)
            foreach (var panel in affectedPanels)
            {
                var operations = GenerateOperationsForPanel(panel, panel.Severity, vehicleType);
                result.Operations.AddRange(operations);
            }

            // Step 3: Add co-occurrence operations from real uploaded estimates
            AddLearnedCoOccurrenceOperations(result, affectedPanels, vehicleType);

            // Step 4: Mine similar real estimates for operations we may have missed
            AddOperationsFromSimilarEstimates(result, affectedPanels, vehicleType);

            // Step 5: Add related operations (ADAS, calibrations, etc.)
            AddRelatedOperations(result, input);

            // Step 6: Add refinish operations
            AddRefinishOperations(result);

            // Step 6.5: Clean descriptions, sanitize hours, and global deduplication
            foreach (var op in result.Operations)
            {
                op.Description = CleanOperationDescription(op.Description);
                op.PartName = CleanPartName(op.PartName);
            }
            SanitizeHours(result);
            DeduplicateOperations(result);

            // Step 7: Calculate totals
            CalculateTotals(result);

            // Step 8: Add confidence notes
            AddConfidenceNotes(result);

            return result;
        }

        /// <summary>
        /// Compare user estimate against ghost estimate
        /// </summary>
        public GhostComparisonResult CompareEstimates(
            List<GhostOperation> userOperations,
            GhostEstimateResult ghostEstimate)
        {
            var result = new GhostComparisonResult
            {
                GhostEstimate = ghostEstimate,
                UserOperations = userOperations
            };

            // Find operations ghost has that user doesn't, and detect value differences
            foreach (var ghostOp in ghostEstimate.Operations)
            {
                var matchingUserOp = userOperations.FirstOrDefault(u => OperationsMatch(u, ghostOp));

                if (matchingUserOp == null)
                {
                    result.GhostFoundMissing.Add(new MissingOperation
                    {
                        Operation = ghostOp,
                        Reason = ghostOp.Source ?? "AI pattern match",
                        Confidence = ghostOp.Confidence
                    });

                    // Track category missing counts
                    var cat = ghostOp.Category ?? "Other";
                    result.CategoryMissingCounts[cat] = result.CategoryMissingCounts.GetValueOrDefault(cat) + 1;
                }
                else
                {
                    result.BothHave.Add(ghostOp);

                    // Track category match counts
                    var cat = ghostOp.Category ?? "Other";
                    result.CategoryMatchCounts[cat] = result.CategoryMatchCounts.GetValueOrDefault(cat) + 1;

                    // Check for value differences
                    var laborDiff = ghostOp.LaborHours - matchingUserOp.LaborHours;
                    var refinishDiff = ghostOp.RefinishHours - matchingUserOp.RefinishHours;
                    var priceDiff = ghostOp.Price - matchingUserOp.Price;

                    if (Math.Abs(laborDiff) > 0.1m || Math.Abs(refinishDiff) > 0.1m || Math.Abs(priceDiff) > 1m)
                    {
                        result.ValueDifferences.Add(new ValueDifference
                        {
                            GhostOp = ghostOp,
                            UserOp = matchingUserOp,
                            LaborHoursDiff = laborDiff,
                            RefinishHoursDiff = refinishDiff,
                            PriceDiff = priceDiff
                        });
                    }
                }
            }

            // Find operations user has that ghost doesn't
            foreach (var userOp in userOperations)
            {
                var ghostHas = ghostEstimate.Operations.Any(g =>
                    OperationsMatch(userOp, g));

                if (!ghostHas)
                {
                    result.UserFoundExtra.Add(new ExtraOperation
                    {
                        Operation = userOp,
                        LearnFromThis = true // Ghost should learn from user
                    });
                }
            }

            // Calculate labor dollar difference
            var ghostConfig = GhostConfigService.Instance;
            var ghostTotalLabor = ghostEstimate.GrandTotalLaborDollars;
            var userTotalLabor = userOperations.Sum(o => o.LaborHours) * ghostConfig.GetEffectiveBodyRate() +
                                 userOperations.Sum(o => o.RefinishHours) * ghostConfig.GetEffectivePaintRate() +
                                 userOperations.Sum(o => o.Price);
            result.LaborDollarDifference = ghostTotalLabor - userTotalLabor;

            // Detect coverage gaps
            var ghostCategories = ghostEstimate.Operations.Select(o => o.Category).Distinct().ToHashSet();
            var userCategories = userOperations.Select(o => o.Category).Distinct().ToHashSet();
            foreach (var cat in ghostCategories.Where(c => !userCategories.Contains(c)))
            {
                result.CoverageGaps.Add(cat);
            }

            // Calculate summary
            result.MatchPercentage = result.BothHave.Count > 0
                ? (double)result.BothHave.Count / Math.Max(ghostEstimate.Operations.Count, userOperations.Count) * 100
                : 0;

            result.Summary = GenerateComparisonSummary(result);

            return result;
        }

        /// <summary>
        /// Learn from comparison - user accepted some ghost suggestions, rejected others
        /// </summary>
        public void LearnFromComparison(GhostComparisonResult comparison,
            List<GhostOperation> acceptedSuggestions,
            List<GhostOperation> rejectedSuggestions)
        {
            var feedbackService = LearningFeedbackService.Instance;
            var intelligenceService = PatternIntelligenceService.Instance;
            var learningService = EstimateLearningService.Instance;

            // Learn from user's extra operations (things AI missed)
            // These are operations the user wrote that the ghost didn't suggest
            foreach (var extra in comparison.UserFoundExtra)
            {
                var patternKey = $"{extra.Operation.PartName}|{extra.Operation.OperationType}".ToLower();
                feedbackService.RecordOperationAccepted(patternKey, "ghost_user_extra");

                // Find corresponding learned pattern and snapshot before modification
                var patterns = learningService.SearchPatterns(extra.Operation.PartName ?? "", 5);
                var match = patterns.FirstOrDefault(p =>
                    p.PartName.Equals(extra.Operation.PartName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    intelligenceService.CreatePatternSnapshot(match, "ghost_learned_from_user");
                }

                System.Diagnostics.Debug.WriteLine($"[Ghost] Learned from user extra: {extra.Operation.Description}");
            }

            // Learn from rejected suggestions (AI was wrong)
            foreach (var rejected in rejectedSuggestions)
            {
                var patternKey = $"{rejected.PartName}|{rejected.OperationType}".ToLower();
                feedbackService.RecordOperationRejected(patternKey, "ghost_rejected");
                System.Diagnostics.Debug.WriteLine($"[Ghost] Pattern rejected, recorded: {rejected.Description}");
            }

            // Reinforce accepted suggestions (AI was right)
            foreach (var accepted in acceptedSuggestions)
            {
                var patternKey = $"{accepted.PartName}|{accepted.OperationType}".ToLower();
                feedbackService.RecordOperationAccepted(patternKey, "ghost_accepted");
                System.Diagnostics.Debug.WriteLine($"[Ghost] Pattern confirmed, recorded: {accepted.Description}");
            }

            // Save feedback
            feedbackService.SaveFeedback();
        }

        /// <summary>
        /// Generate a guidance estimate that merges operations from all data sources:
        /// 1. MET/CCC database (via GenerateGhostEstimate)
        /// 2. Operation Knowledge Base (570+ operations with justifications)
        /// 3. Learned patterns from uploaded estimates
        /// </summary>
        public GuidanceEstimateResult GenerateGuidanceEstimate(GhostEstimateInput input)
        {
            // Step 1: Get base MET/CCC operations from existing ghost estimate logic
            var baseEstimate = GenerateGhostEstimate(input);

            var result = new GuidanceEstimateResult
            {
                VehicleInfo = baseEstimate.VehicleInfo,
                VehicleType = baseEstimate.VehicleType,
                DamageDescription = baseEstimate.DamageDescription,
                GeneratedAt = baseEstimate.GeneratedAt,
                TotalBodyHours = baseEstimate.TotalBodyHours,
                TotalRefinishHours = baseEstimate.TotalRefinishHours,
                TotalSubletAmount = baseEstimate.TotalSubletAmount,
                RefinishPanelCount = baseEstimate.RefinishPanelCount,
                BlendPanelCount = baseEstimate.BlendPanelCount,
                TotalBodyLaborDollars = baseEstimate.TotalBodyLaborDollars,
                TotalRefinishLaborDollars = baseEstimate.TotalRefinishLaborDollars,
                TotalMechLaborDollars = baseEstimate.TotalMechLaborDollars,
                TotalFrameLaborDollars = baseEstimate.TotalFrameLaborDollars,
                GrandTotalLaborDollars = baseEstimate.GrandTotalLaborDollars,
                Notes = baseEstimate.Notes
            };

            // Track operations by normalized key for deduplication
            var seenOperations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add base database operations
            foreach (var op in baseEstimate.Operations)
            {
                var normalizedKey = NormalizeOperationKey(op.PartName, op.OperationType);
                if (seenOperations.Add(normalizedKey))
                {
                    result.GuidanceOperations.Add(new GuidanceOperation
                    {
                        OperationType = op.OperationType,
                        PartName = op.PartName,
                        Description = op.Description,
                        Category = op.Category,
                        Section = MapToCCCSection(op.PartName, op.Category),
                        Side = op.Side,
                        LaborHours = op.LaborHours,
                        RefinishHours = op.RefinishHours,
                        Price = op.Price,
                        Confidence = op.Confidence,
                        Source = op.Source,
                        DataSource = "Database",
                        ConfidenceLabel = op.Confidence >= 0.9 ? "High" : (op.Confidence >= 0.7 ? "Medium" : "Low"),
                        IsRequired = op.Confidence >= 0.9
                    });
                    result.DatabaseCount++;
                }
            }

            // Step 2: Query Operation Knowledge Base for each affected panel
            var affectedPanels = GetAffectedPanels(input);

            // Apply per-panel severity overrides
            if (input.PanelSeverities.Count > 0)
            {
                foreach (var panel in affectedPanels)
                {
                    if (input.PanelSeverities.TryGetValue(panel.Name, out var panelSeverity))
                    {
                        panel.Severity = panelSeverity;
                    }
                }
            }

            foreach (var panel in affectedPanels)
            {
                var partOps = _knowledgeService.GetOperationsForPart(panel.Name);
                if (partOps?.Operations != null)
                {
                    foreach (var rec in partOps.Operations)
                    {
                        var normalizedKey = NormalizeOperationKey(panel.Name, rec.Name);
                        if (seenOperations.Add(normalizedKey))
                        {
                            var justification = _knowledgeService.GetJustification(rec.Name);
                            var sideCode = panel.Side switch
                            {
                                "Left" => "LT ",
                                "Right" => "RT ",
                                _ => ""
                            };

                            var kbCategory = rec.Category == "Diagnostic" ? "Scanning" :
                                           rec.Category == "Refinish" ? "Refinish Operations" :
                                           "Body Operations";

                            result.GuidanceOperations.Add(new GuidanceOperation
                            {
                                OperationType = rec.Category switch
                                {
                                    "Diagnostic" => "Mech",
                                    "Refinish" => "Rfn",
                                    "Body" => "Body",
                                    _ => "Body"
                                },
                                PartName = rec.Name.ToLower(),
                                Description = $"{sideCode}{rec.Name}",
                                Category = kbCategory,
                                Section = MapToCCCSection(panel.Name, kbCategory),
                                Side = panel.Side,
                                LaborHours = rec.LaborHours,
                                Price = rec.IsMaterial ? rec.TypicalCost : 0,
                                Confidence = rec.IsCommon ? 0.90 : 0.70,
                                Source = rec.Notes,
                                DataSource = "Knowledge Base",
                                Justification = justification?.WhyNeeded ?? rec.Notes,
                                PPageReference = justification?.PPageReference ?? "",
                                DEGReference = justification?.DEGReference ?? "",
                                ConfidenceLabel = rec.IsCommon ? "High" : "Medium",
                                IsRequired = rec.IsCommon
                            });
                            result.KnowledgeBaseCount++;
                        }
                    }
                }
            }

            // Step 3: Query Smart Suggestion Service for learned patterns
            foreach (var panel in affectedPanels)
            {
                var primaryOp = panel.RefinishOnly ? (panel.ExplicitOperation ?? "Refinish") :
                    panel.ExplicitOperation ?? (panel.Severity.ToLower() switch
                    {
                        "severe" or "heavy" => "Replace",
                        _ => "Repair"
                    });

                var suggestions = _suggestionService.GetSuggestionsForPart(
                    panel.Name, primaryOp, input.VehicleInfo);

                if (suggestions.HasData)
                {
                    foreach (var sug in suggestions.ManualOperations)
                    {
                        var normalizedKey = NormalizeOperationKey(panel.Name, sug.Description);
                        if (seenOperations.Add(normalizedKey))
                        {
                            var learnedCategory = sug.OperationType == "Rfn" || sug.OperationType == "Refinish" || sug.OperationType == "Blend"
                                    ? "Refinish Operations" : "Body Operations";

                            result.GuidanceOperations.Add(new GuidanceOperation
                            {
                                OperationType = sug.OperationType,
                                PartName = panel.Name,
                                Description = sug.Description,
                                Category = learnedCategory,
                                Section = MapToCCCSection(panel.Name, learnedCategory),
                                Side = panel.Side,
                                LaborHours = sug.LaborHours,
                                RefinishHours = sug.RefinishHours,
                                Price = sug.Price,
                                Confidence = sug.Confidence,
                                Source = sug.Reason,
                                DataSource = "Learned",
                                Justification = sug.Reason,
                                LearnedFrequency = sug.TimesUsed,
                                ConfidenceLabel = sug.Confidence >= 0.8 ? "High" :
                                                  (sug.Confidence >= 0.5 ? "Medium" : "Low"),
                                IsRequired = false
                            });
                            result.LearnedCount++;
                        }
                    }
                }
            }

            // Step 4: Pull operations from Excel estimating tool sheets
            // Gate by relevance: SRS ops when airbag components involved, Cover Car when welding present, etc.
            AddExcelSheetOperations(result, affectedPanels, seenOperations);

            // Add warnings
            if (result.LearnedCount == 0)
                result.Warnings.Add("No learned patterns available. Upload estimates to improve suggestions.");

            if (result.KnowledgeBaseCount == 0)
                result.Warnings.Add("No knowledge base operations matched. Try a more specific damage description.");

            var stats = _smartEngine.GetStats();
            if (stats.TotalEstimatesLearned < 10)
                result.ProTips.Add("Upload more estimates to unlock learned pattern suggestions.");

            if (result.GuidanceOperations.Any(o => o.Category == "Scanning" || o.Section == "VEHICLE DIAGNOSTICS"))
                result.ProTips.Add("Pre/Post scans are required on most modern vehicles. Don't skip them.");

            if (result.GuidanceOperations.Any(o => o.PartName.Contains("quarter") || o.PartName.Contains("rocker")))
                result.ProTips.Add("Welded panels require destructive weld tests, corrosion protection, and cavity wax.");

            // Copy operations list for backward compat
            result.Operations = result.GuidanceOperations.Cast<GhostOperation>().ToList();

            return result;
        }

        private string NormalizeOperationKey(string partName, string operationName)
        {
            var part = partName.ToLower().Trim().Replace("-", " ").Replace("  ", " ");
            var op = operationName.ToLower().Trim().Replace("-", " ").Replace("  ", " ");
            var combined = $"{part} {op}";

            // Normalize scan variants to canonical keys — check both part and op fields
            if (combined.Contains("pre") && combined.Contains("scan"))
                return "pre scan|scan";
            if (combined.Contains("post") && combined.Contains("scan"))
                return "post scan|scan";
            if (combined.Contains("diagnostic scan"))
                return "pre scan|scan";

            // Normalize ADAS calibration variants
            if (combined.Contains("adas") || combined.Contains("calibrat"))
                return "adas calibration|calibration";

            return $"{part}|{op}";
        }

        /// <summary>
        /// Pull relevant operations from Excel estimating tool sheets that aren't covered by existing logic.
        /// Gates operations by relevance to the damage at hand.
        /// </summary>
        private void AddExcelSheetOperations(GuidanceEstimateResult result, List<AffectedPanel> affectedPanels, HashSet<string> seenOperations)
        {
            if (!_ghostConfig.IsCategoryEnabled("Excel Tool")) return;

            var panelNames = affectedPanels.Select(p => p.Name.ToLower()).ToHashSet();
            var existingOps = result.GuidanceOperations;

            var hasWeldedPanels = existingOps.Any(o =>
                o.PartName.Contains("quarter") || o.PartName.Contains("rocker") ||
                o.PartName.Contains("pillar") || o.PartName.Contains("roof"));

            var hasAirbagComponents = panelNames.Any(p =>
                p.Contains("pillar") || p.Contains("dash") || p.Contains("steering") ||
                p.Contains("door") || p.Contains("seat") || p.Contains("roof"));

            var hasRefinish = existingOps.Any(o =>
                o.Category == "Refinish Operations" || o.OperationType == "Rfn" || o.OperationType == "Blend");

            var hasMechanical = existingOps.Any(o =>
                o.PartName.Contains("suspension") || o.PartName.Contains("subframe") ||
                o.PartName.Contains("steering") || o.PartName.Contains("engine") ||
                o.PartName.Contains("radiator") || o.PartName.Contains("condenser"));

            // Map sheet names to their relevance conditions and display categories
            var sheetConfig = new (string SheetName, bool IsRelevant, string DisplayCategory)[]
            {
                ("SRS Operations", hasAirbagComponents, "SRS Operations"),
                ("Cover Car Operations", hasWeldedPanels || hasRefinish, "Cover Car Operations"),
                ("Mechanical Operations", hasMechanical, "Mechanical Operations"),
                ("SOP Operations", existingOps.Count > 0, "SOP Operations"), // SOPs are always potentially relevant
            };

            foreach (var (sheetName, isRelevant, displayCategory) in sheetConfig)
            {
                if (!isRelevant) continue;
                if (!_ghostConfig.IsCategoryEnabled(displayCategory)) continue;

                var sheetOps = _excelProvider.GetSheetOperations(sheetName);
                foreach (var excelOp in sheetOps)
                {
                    if (string.IsNullOrWhiteSpace(excelOp.Description)) continue;
                    if (_excelProvider.IsOperationDisabled(excelOp.Description)) continue;

                    var normalizedKey = NormalizeOperationKey(excelOp.Description.ToLower(), excelOp.OperationType ?? "Body");
                    if (!seenOperations.Add(normalizedKey)) continue;

                    var laborHours = _excelProvider.GetEffectiveLaborHours(excelOp.Description);
                    var refinishHours = _excelProvider.GetEffectiveRefinishHours(excelOp.Description);
                    var price = _excelProvider.GetEffectivePrice(excelOp.Description);

                    // Determine operation type code
                    var opType = (excelOp.Category ?? "B") switch
                    {
                        "M" => "Mech",
                        "B" => "Body",
                        "R" => "Rfn",
                        "F" => "Frame",
                        "D" => "Mech",
                        _ => "Body"
                    };

                    result.GuidanceOperations.Add(new GuidanceOperation
                    {
                        OperationType = opType,
                        PartName = excelOp.Description.ToLower(),
                        Description = excelOp.Description,
                        Category = displayCategory,
                        Section = MapExcelCategoryToSection(displayCategory),
                        LaborHours = laborHours,
                        RefinishHours = refinishHours,
                        Price = price,
                        Confidence = 0.85,
                        Source = $"Excel Tool: {sheetName}",
                        DataSource = "Excel Tool",
                        ConfidenceLabel = "High",
                        IsRequired = false,
                        Justification = $"From estimating tool ({sheetName})"
                    });
                    result.ExcelToolCount++;
                }
            }
        }

        #region Private Methods

        private List<AffectedPanel> GetAffectedPanels(GhostEstimateInput input)
        {
            var panels = new List<AffectedPanel>();
            var addedPanels = new HashSet<string>();

            // From explicit impact zones
            foreach (var zone in input.ImpactZones)
            {
                if (ImpactZonePanels.TryGetValue(zone.ToLower().Replace(" ", "_"), out var zonePanels))
                {
                    foreach (var panelName in zonePanels)
                    {
                        if (!addedPanels.Contains(panelName))
                        {
                            addedPanels.Add(panelName);
                            panels.Add(new AffectedPanel
                            {
                                Name = panelName,
                                ImpactZone = zone,
                                Severity = input.Severity,
                                Side = DetermineSide(zone)
                            });
                        }
                    }
                }
            }

            // From damage description parsing
            var descriptionLower = input.DamageDescription.ToLower();
            var detectedPanels = DetectPanelsFromDescription(descriptionLower);

            foreach (var detected in detectedPanels)
            {
                if (!addedPanels.Contains(detected.Name))
                {
                    addedPanels.Add(detected.Name);
                    panels.Add(detected);
                }
            }

            return panels;
        }

        private List<AffectedPanel> DetectPanelsFromDescription(string description)
        {
            var panels = new List<AffectedPanel>();
            var addedPanelKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Holistic damage patterns — match against full description since these are multi-panel events
            var damagePatterns = new Dictionary<string[], string[]>
            {
                { new[] { "front end", "front hit", "front collision" }, new[] { "front bumper cover", "hood", "grille", "radiator support" } },
                { new[] { "rear end", "rear hit", "rear collision", "backed into" }, new[] { "rear bumper cover", "trunk lid", "tail light" } },
                { new[] { "t-bone", "side impact", "side hit" }, new[] { "front door", "rear door", "rocker panel" } },
                { new[] { "fender bender", "corner hit" }, new[] { "fender", "front bumper cover", "headlight" } },
                { new[] { "door ding", "door dent" }, new[] { "front door", "rear door" } },
                { new[] { "hail", "hail damage" }, new[] { "hood", "roof", "trunk lid", "fender" } },
                { new[] { "rolled", "rollover" }, new[] { "roof", "a-pillar", "b-pillar", "c-pillar", "quarter panel" } }
            };

            foreach (var pattern in damagePatterns)
            {
                if (pattern.Key.Any(k => description.Contains(k)))
                {
                    foreach (var panelName in pattern.Value)
                    {
                        if (addedPanelKeys.Add(panelName))
                        {
                            panels.Add(new AffectedPanel
                            {
                                Name = panelName,
                                ImpactZone = "description",
                                Severity = DetectSeverityFromDescription(description),
                                Side = DetectSideFromDescription(description)
                            });
                        }
                    }
                }
            }

            // Direct panel mentions — parse PER LINE to avoid cross-contamination
            // e.g. "repair front bumper, repair left rear door" won't confuse front/rear
            var directPanels = new[]
            {
                "bumper", "fender", "hood", "door", "quarter", "roof", "trunk",
                "liftgate", "tailgate", "grille", "headlight", "tail light", "mirror",
                "wheel", "rocker", "pillar"
            };

            var refinishOnlyKeywords = new[] { "blend", "refinish", "paint", "clear coat", "clearcoat", "color match", "base coat", "basecoat" };
            var replaceKeywords = new[] { "replace", "new", "order" };
            var riKeywords = new[] { "r&i", "r and i", "remove and install", "remove & install" };

            // Split input into individual lines by comma, newline, semicolon
            var lines = description.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();

            // If no delimiters found, treat whole description as one line
            if (lines.Length == 0)
                lines = new[] { description };

            foreach (var line in lines)
            {
                foreach (var panel in directPanels)
                {
                    if (!line.Contains(panel))
                        continue;

                    // Use THIS LINE's context for GetFullPanelName (not the full description)
                    var fullName = GetFullPanelName(panel, line);

                    // Build a dedup key that includes side context from this line
                    var lineSide = DetectSideFromDescription(line);
                    var dedupKey = $"{fullName}|{lineSide}";

                    if (!addedPanelKeys.Add(dedupKey))
                        continue; // Already added this exact panel+side

                    // Also skip if we already have this panel name without side distinction
                    if (panels.Any(p => p.Name == fullName && p.Side == lineSide))
                        continue;

                    // Check context around this panel mention to determine operation type
                    var isRefinishOnly = false;
                    string? explicitOp = null;

                    foreach (var rk in refinishOnlyKeywords)
                    {
                        if (line.Contains($"{panel} {rk}") || line.Contains($"{rk} {panel}") || line.Contains(rk))
                        {
                            isRefinishOnly = true;
                            explicitOp = rk == "blend" ? "Blend" : "Refinish";
                            break;
                        }
                    }

                    if (!isRefinishOnly)
                    {
                        foreach (var rk in replaceKeywords)
                        {
                            if (line.Contains($"{panel} {rk}") || line.Contains($"{rk} {panel}") || line.Contains(rk))
                            {
                                explicitOp = "Replace";
                                break;
                            }
                        }
                    }

                    // Check for R&I operation
                    if (explicitOp == null && !isRefinishOnly)
                    {
                        foreach (var rk in riKeywords)
                        {
                            if (line.Contains(rk))
                            {
                                explicitOp = "R&I";
                                break;
                            }
                        }
                    }

                    panels.Add(new AffectedPanel
                    {
                        Name = fullName,
                        ImpactZone = "direct mention",
                        Severity = explicitOp == "Replace" ? "heavy" : DetectSeverityFromDescription(line),
                        Side = lineSide,
                        RefinishOnly = isRefinishOnly,
                        ExplicitOperation = explicitOp
                    });
                }
            }

            return panels;
        }

        private string GetFullPanelName(string shortName, string context)
        {
            // Add position context — context should be the individual line, not full description
            var isRear = context.Contains("rear");
            var isFront = context.Contains("front");

            return shortName switch
            {
                "bumper" => isRear ? "rear bumper cover" : "front bumper cover",
                "door" when context.Contains("rear door") => "rear door",
                "door" when isRear => "rear door",
                "door" => "front door",
                "light" when isRear => "tail light",
                "light" => "headlight",
                "wheel" when isRear => "rear wheel",
                "wheel" => isFront ? "front wheel" : "wheel",
                "rocker" => "rocker panel",
                "pillar" when context.Contains("a-pillar") || context.Contains("a pillar") => "a-pillar",
                "pillar" when context.Contains("b-pillar") || context.Contains("b pillar") => "b-pillar",
                "pillar" when context.Contains("c-pillar") || context.Contains("c pillar") => "c-pillar",
                "pillar" => "a-pillar",
                _ => shortName
            };
        }

        private string DetectSeverityFromDescription(string description)
        {
            if (description.Contains("totaled") || description.Contains("destroyed") || description.Contains("crushed"))
                return "severe";
            if (description.Contains("major") || description.Contains("heavy") || description.Contains("buckled") || description.Contains("kinked"))
                return "heavy";
            if (description.Contains("minor") || description.Contains("light") || description.Contains("scratch") || description.Contains("scuff"))
                return "light";
            return "moderate";
        }

        private string DetectSideFromDescription(string description)
        {
            if (description.Contains("left") || description.Contains("driver"))
                return "Left";
            if (description.Contains("right") || description.Contains("passenger"))
                return "Right";
            if (description.Contains("both"))
                return "Both";
            return "";
        }

        private string DetermineSide(string zone)
        {
            if (zone.Contains("left")) return "Left";
            if (zone.Contains("right")) return "Right";
            return "";
        }

        private List<GhostOperation> GenerateOperationsForPanel(AffectedPanel panel, string severity, string vehicleType)
        {
            var operations = new List<GhostOperation>();

            // Build position prefix using CCC side designation
            var sideCode = panel.Side switch
            {
                "Left" => "LT ",
                "Right" => "RT ",
                _ => ""
            };

            // If panel is refinish-only (e.g. "fender blend"), skip body repair operations
            if (panel.RefinishOnly)
            {
                var blendOp = panel.ExplicitOperation == "Blend" ? "Blend" : "Rfn";
                var blendLabel = panel.ExplicitOperation == "Blend" ? "Blend" : "Refinish";
                var rfnResolution = ResolveRefinishTime(panel.Name);
                var refinishHours = rfnResolution.Hours;
                if (blendOp == "Blend")
                    refinishHours = Math.Max(refinishHours * 0.5m, 1.0m);

                operations.Add(new GhostOperation
                {
                    OperationType = blendOp,
                    PartName = panel.Name,
                    Description = $"{sideCode}{ToTitleCase(panel.Name)} {blendLabel}",
                    Category = "Refinish Operations",
                    RefinishHours = refinishHours,
                    MinRefinishHours = rfnResolution.MinHours,
                    MaxRefinishHours = rfnResolution.MaxHours,
                    SampleCount = rfnResolution.SampleCount,
                    LaborSource = rfnResolution.Source,
                    Confidence = 0.90,
                    Source = "User-specified refinish operation",
                    Side = panel.Side
                });

                return operations;
            }

            var severityMultiplier = SeverityMultipliers.GetValueOrDefault(severity.ToLower(), 1.0m);

            // Determine primary operation based on severity or explicit user input
            string primaryOp, opCode;
            if (panel.ExplicitOperation == "Replace")
            {
                primaryOp = "Replace";
                opCode = "Repl";
            }
            else
            {
                (primaryOp, opCode) = severity.ToLower() switch
                {
                    "severe" or "heavy" => ("Replace", "Repl"),
                    "moderate" => ("Repair", "Rpr"),
                    "light" => ("Repair", "Rpr"),
                    _ => ("Repair", "Rpr")
                };
            }

            // Get learned labor times or use defaults
            var laborResolution = ResolveLaborTime(panel.Name, primaryOp, vehicleType);
            var bodyHours = laborResolution.Hours * severityMultiplier;

            var sourceLabel = laborResolution.HasLearnedData
                ? $"Learned from {laborResolution.SampleCount} uploaded estimates"
                : "CCC/MOTOR database";

            // Primary operation - format like real CCC estimate line
            operations.Add(new GhostOperation
            {
                OperationType = opCode,
                PartName = panel.Name,
                Description = $"{sideCode}{ToTitleCase(panel.Name)} {opCode}",
                Category = DetermineCategory(panel.Name),
                LaborHours = bodyHours,
                MinLaborHours = laborResolution.MinHours * severityMultiplier,
                MaxLaborHours = laborResolution.MaxHours * severityMultiplier,
                SampleCount = laborResolution.SampleCount,
                LaborSource = laborResolution.Source,
                LearnedDollarAmount = laborResolution.LearnedDollarAmount,
                MinDollarAmount = laborResolution.MinDollarAmount,
                MaxDollarAmount = laborResolution.MaxDollarAmount,
                Confidence = laborResolution.HasLearnedData ? 0.98 : 0.95,
                Source = sourceLabel,
                Side = panel.Side
            });

            // Add R&I operations for panels that need removal - CCC Pathways
            if (RequiresRAndI(panel.Name, primaryOp))
            {
                var riParts = GetRAndIParts(panel.Name);
                foreach (var riPart in riParts)
                {
                    var riResolution = ResolveLaborTime(riPart, "R&I", vehicleType);
                    operations.Add(new GhostOperation
                    {
                        OperationType = "R&I",
                        PartName = riPart,
                        Description = $"{sideCode}{ToTitleCase(riPart)} R&I",
                        Category = "Part Operations",
                        LaborHours = riResolution.Hours,
                        MinLaborHours = riResolution.MinHours,
                        MaxLaborHours = riResolution.MaxHours,
                        SampleCount = riResolution.SampleCount,
                        LaborSource = riResolution.Source,
                        Confidence = 0.85,
                        Source = $"CCC Pathways - NOT INCLUDED in {panel.Name} time",
                        Side = panel.Side
                    });
                }
            }

            return operations;
        }

        /// <summary>
        /// Find real estimates with similar damage zones and pull operations we haven't added yet.
        /// This catches operations that the hardcoded logic and co-occurrence patterns miss.
        /// </summary>
        private void AddOperationsFromSimilarEstimates(GhostEstimateResult result, List<AffectedPanel> panels, string vehicleType)
        {
            var allEstimates = _historyDb.GetAllEstimates();
            if (allEstimates.Count < 3) return; // Need enough data to be meaningful

            var affectedPartNames = panels.Select(p => p.Name.ToLower()).ToHashSet();

            var existingOps = new HashSet<string>(
                result.Operations.Select(o => $"{o.PartName?.ToLower()}|{o.OperationType?.ToLower()}"),
                StringComparer.OrdinalIgnoreCase);

            // Find estimates that share at least 2 affected panels with our damage
            var similarEstimates = allEstimates
                .Where(e => e.LineItems != null && e.LineItems.Count > 0)
                .Select(e => new
                {
                    Estimate = e,
                    MatchingPanels = e.LineItems
                        .Where(li => !string.IsNullOrEmpty(li.PartName))
                        .Select(li => li.PartName.ToLower())
                        .Distinct()
                        .Count(p => affectedPartNames.Any(ap => p.Contains(ap) || ap.Contains(p)))
                })
                .Where(x => x.MatchingPanels >= 2)
                .OrderByDescending(x => x.MatchingPanels)
                .Take(10)
                .ToList();

            if (similarEstimates.Count == 0) return;

            // Count how often each operation appears across similar estimates
            var opCounts = new Dictionary<string, (string PartName, string OpType, decimal AvgLabor, decimal AvgRefinish, decimal AvgPrice, int Count)>();

            foreach (var sim in similarEstimates)
            {
                foreach (var li in sim.Estimate.LineItems)
                {
                    if (string.IsNullOrEmpty(li.PartName) || string.IsNullOrEmpty(li.OperationType))
                        continue;

                    var opKey = $"{li.PartName.ToLower()}|{li.OperationType.ToLower()}";
                    if (existingOps.Contains(opKey))
                        continue; // Already in our estimate

                    if (opCounts.TryGetValue(opKey, out var existing))
                    {
                        var newCount = existing.Count + 1;
                        opCounts[opKey] = (
                            li.PartName,
                            li.OperationType,
                            (existing.AvgLabor * existing.Count + li.LaborHours) / newCount,
                            (existing.AvgRefinish * existing.Count + li.RefinishHours) / newCount,
                            (existing.AvgPrice * existing.Count + li.Price) / newCount,
                            newCount
                        );
                    }
                    else
                    {
                        opCounts[opKey] = (li.PartName, li.OperationType, li.LaborHours, li.RefinishHours, li.Price, 1);
                    }
                }
            }

            // Add operations that appear in >= 50% of similar estimates
            var threshold = Math.Max(2, similarEstimates.Count / 2);
            var addedCount = 0;

            foreach (var (opKey, data) in opCounts.Where(kv => kv.Value.Count >= threshold).OrderByDescending(kv => kv.Value.Count))
            {
                var confidence = (double)data.Count / similarEstimates.Count;

                result.Operations.Add(new GhostOperation
                {
                    OperationType = data.OpType,
                    PartName = data.PartName,
                    Description = $"{ToTitleCase(data.PartName)} {data.OpType}",
                    Category = DetermineCategory(data.PartName),
                    LaborHours = data.AvgLabor,
                    RefinishHours = data.AvgRefinish,
                    Price = data.AvgPrice,
                    Confidence = Math.Min(0.90, confidence),
                    Source = $"Found in {data.Count}/{similarEstimates.Count} similar real estimates"
                });

                existingOps.Add(opKey);
                addedCount++;

                if (addedCount >= 15) break; // Cap additions from similar estimates
            }

            if (addedCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] Added {addedCount} operations from {similarEstimates.Count} similar real estimates");
            }
        }

        /// <summary>
        /// Add operations that commonly co-occur with the affected panels, learned from real uploaded estimates.
        /// E.g., if "front bumper cover Replace" appears with "bumper absorber R&I" in 80% of real estimates, add it.
        /// </summary>
        private void AddLearnedCoOccurrenceOperations(GhostEstimateResult result, List<AffectedPanel> panels, string vehicleType)
        {
            var existingOps = new HashSet<string>(
                result.Operations.Select(o => $"{o.PartName?.ToLower()}|{o.OperationType?.ToLower()}"),
                StringComparer.OrdinalIgnoreCase);

            var addedOps = new List<GhostOperation>();

            foreach (var panel in panels)
            {
                var canonicalName = _knowledgeBase.ResolveAlias(panel.Name.ToLower()) ?? panel.Name.ToLower();

                // Get all co-occurring operations with >= 40% co-occurrence rate
                var coOccurrences = _knowledgeBase.GetCoOccurrences(canonicalName, 0.4);

                foreach (var assoc in coOccurrences)
                {
                    var opKey = $"{assoc.AssociatedPart}|{assoc.AssociatedOperation}";
                    if (existingOps.Contains(opKey))
                        continue; // Already in the estimate

                    // Skip R&I parts — those are handled by GetRAndIParts
                    if (assoc.AssociatedOperation.Equals("r&i", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Get learned labor time for this co-occurring operation
                    var coResolution = ResolveLaborTime(assoc.AssociatedPart, assoc.AssociatedOperation, vehicleType);
                    var laborHours = coResolution.Hours;
                    var refinishHours = 0m;
                    if (assoc.AssociatedOperation.Equals("refinish", StringComparison.OrdinalIgnoreCase) ||
                        assoc.AssociatedOperation.Equals("rfn", StringComparison.OrdinalIgnoreCase) ||
                        assoc.AssociatedOperation.Equals("blend", StringComparison.OrdinalIgnoreCase))
                    {
                        var coRfnResolution = ResolveRefinishTime(assoc.AssociatedPart);
                        refinishHours = coRfnResolution.Hours;
                        laborHours = 0m;
                    }

                    var sideCode = panel.Side switch
                    {
                        "Left" => "LT ",
                        "Right" => "RT ",
                        _ => ""
                    };

                    var opCodeShort = assoc.AssociatedOperation.ToLower() switch
                    {
                        "replace" => "Repl",
                        "repair" => "Rpr",
                        "refinish" or "rfn" => "Rfn",
                        "blend" => "Blend",
                        _ => assoc.AssociatedOperation
                    };

                    addedOps.Add(new GhostOperation
                    {
                        OperationType = opCodeShort,
                        PartName = assoc.AssociatedPart,
                        Description = $"{sideCode}{ToTitleCase(assoc.AssociatedPart)} {opCodeShort}",
                        Category = DetermineCategory(assoc.AssociatedPart),
                        LaborHours = laborHours,
                        RefinishHours = refinishHours,
                        Confidence = Math.Min(0.95, assoc.CoOccurrenceRate),
                        Source = $"Learned co-occurrence ({assoc.CoOccurrenceRate:P0} of estimates with {panel.Name})",
                        Side = panel.Side
                    });

                    existingOps.Add(opKey);
                }
            }

            if (addedOps.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] Added {addedOps.Count} operations from learned co-occurrence patterns");
                result.Operations.AddRange(addedOps);
            }
        }

        private void AddRelatedOperations(GhostEstimateResult result, GhostEstimateInput input)
        {
            var hasStructural = result.Operations.Any(o =>
                o.PartName.Contains("pillar") ||
                o.PartName.Contains("rail") ||
                o.PartName.Contains("rocker") ||
                o.PartName.Contains("apron"));

            var hasWeldedPanels = result.Operations.Any(o =>
                o.PartName.Contains("quarter") ||
                o.PartName.Contains("rocker") ||
                o.PartName.Contains("pillar") ||
                o.PartName.Contains("roof"));

            var hasADASComponents = result.Operations.Any(o =>
                o.PartName.Contains("bumper") ||
                o.PartName.Contains("grille") ||
                o.PartName.Contains("windshield") ||
                o.PartName.Contains("mirror"));

            var hasPlasticParts = result.Operations.Any(o =>
                o.PartName.Contains("bumper") ||
                o.PartName.Contains("fascia") ||
                o.PartName.Contains("valance") ||
                o.PartName.Contains("spoiler"));

            // Add scan operations — use config scanning method (flat rate or labor hours)
            // Only add if not already present from other paths (learned co-occurrence, similar estimates, etc.)
            if (result.Operations.Count > 0)
            {
                var hasPreScan = result.Operations.Any(o =>
                    o.PartName != null && o.PartName.Contains("pre", StringComparison.OrdinalIgnoreCase) &&
                    (o.Category == "Scanning" || o.PartName.Contains("scan", StringComparison.OrdinalIgnoreCase)));
                var hasPostScan = result.Operations.Any(o =>
                    o.PartName != null && o.PartName.Contains("post", StringComparison.OrdinalIgnoreCase) &&
                    (o.Category == "Scanning" || o.PartName.Contains("scan", StringComparison.OrdinalIgnoreCase)));

                var scanConfig = _ghostConfig.GetEffectiveScanning();

                if (!hasPreScan)
                {
                    var learnedPreScan = GetLearnedSubletOrLaborTime("pre-repair scan", "Mech", 0m);
                    var preScanHours = learnedPreScan > 0 ? learnedPreScan : scanConfig.LaborHours;
                    var preScanPrice = learnedPreScan > 0 ? 0m : scanConfig.Price;
                    var preScanSource = learnedPreScan > 0 ? "Learned from uploaded estimates" :
                        (scanConfig.Price > 0 ? $"Shop rate: ${scanConfig.Price:F0} flat rate" : "CCC/MOTOR - scan tool diagnostics NOT INCLUDED");

                    result.Operations.Add(new GhostOperation
                    {
                        OperationType = preScanPrice > 0 ? "Sublet" : "Mech",
                        PartName = "pre-repair scan",
                        Description = "Pre-Repair Diagnostic Scan",
                        Category = "Scanning",
                        LaborHours = preScanHours,
                        Price = preScanPrice,
                        Confidence = 0.95,
                        Source = preScanSource
                    });
                }

                if (!hasPostScan)
                {
                    var learnedPostScan = GetLearnedSubletOrLaborTime("post-repair scan", "Mech", 0m);
                    var postScanHours = learnedPostScan > 0 ? learnedPostScan : scanConfig.LaborHours;
                    var postScanPrice = learnedPostScan > 0 ? 0m : scanConfig.Price;
                    var postScanSource = learnedPostScan > 0 ? "Learned from uploaded estimates" :
                        (scanConfig.Price > 0 ? $"Shop rate: ${scanConfig.Price:F0} flat rate" : "CCC/MOTOR - scan tool diagnostics NOT INCLUDED");

                    result.Operations.Add(new GhostOperation
                    {
                        OperationType = postScanPrice > 0 ? "Sublet" : "Mech",
                        PartName = "post-repair scan",
                        Description = "Post-Repair Diagnostic Scan",
                        Category = "Scanning",
                        LaborHours = postScanHours,
                        Price = postScanPrice,
                        Confidence = 0.95,
                        Source = postScanSource
                    });
                }
            }

            // Add ADAS calibrations if needed — use learned pricing if available
            if (hasADASComponents)
            {
                var adasResolved = ResolveSubletPrice("adas calibration", 350.00m);
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Sublet",
                    PartName = "adas calibration",
                    Description = "ADAS Calibration - Forward Camera/Radar",
                    Category = "Calibration",
                    Price = adasResolved.price,
                    SampleCount = adasResolved.sampleCount,
                    LearnedDollarAmount = adasResolved.sampleCount > 0 ? adasResolved.price : null,
                    MinDollarAmount = adasResolved.sampleCount > 0 ? adasResolved.minPrice : null,
                    MaxDollarAmount = adasResolved.sampleCount > 0 ? adasResolved.maxPrice : null,
                    LaborSource = adasResolved.sampleCount > 0 ? "learned" : "fallback",
                    Confidence = 0.80,
                    Source = adasResolved.sampleCount > 0
                        ? $"Learned from {adasResolved.sampleCount} uploaded estimates (avg ${adasResolved.price:F2})"
                        : "CCC/MOTOR G33 - ADAS calibration triggers"
                });
            }

            // Add structural measurements if structural damage
            if (hasStructural)
            {
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Frame",
                    PartName = "structural measurement",
                    Description = "Measure/Document Vehicle Structure",
                    Category = "Frame Operations",
                    LaborHours = 1.0m,
                    Confidence = 0.90,
                    Source = "Mitchell CEG - NOT INCLUDED in repair times"
                });
            }

            // Add WELDED PANEL operations — learned-first, fallback to MET times
            if (hasWeldedPanels)
            {
                var weldedCount = result.Operations.Count(o =>
                    o.OperationType == "Replace" && (
                    o.PartName.Contains("quarter") ||
                    o.PartName.Contains("rocker") ||
                    o.PartName.Contains("pillar")));

                // E-Coat Removal
                var ecoatRes = ResolveLaborTime("e-coat removal", "Body", result.VehicleType);
                var ecoatHours = ecoatRes.HasLearnedData ? ecoatRes.Hours : 0.3m * Math.Max(1, weldedCount);
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body", PartName = "e-coat removal",
                    Description = "Remove Factory E-Coat at Weld Flanges",
                    Category = "Body Operations", LaborHours = ecoatHours,
                    MinLaborHours = ecoatRes.HasLearnedData ? ecoatRes.MinHours : ecoatHours,
                    MaxLaborHours = ecoatRes.HasLearnedData ? ecoatRes.MaxHours : ecoatHours,
                    SampleCount = ecoatRes.SampleCount, LaborSource = ecoatRes.Source,
                    Confidence = 0.90,
                    Source = ecoatRes.HasLearnedData ? $"Learned from {ecoatRes.SampleCount} estimates" : "MET: Body Operations - DEG: protective coatings NOT INCLUDED"
                });

                // Weld-Through Primer
                var wtpRes = ResolveLaborTime("weld-through primer", "Body", result.VehicleType);
                var wtpHours = wtpRes.HasLearnedData ? wtpRes.Hours : 0.2m * Math.Max(1, weldedCount);
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body", PartName = "weld-through primer",
                    Description = "Weld-Through Primer Application",
                    Category = "Body Operations", LaborHours = wtpHours,
                    MinLaborHours = wtpRes.HasLearnedData ? wtpRes.MinHours : wtpHours,
                    MaxLaborHours = wtpRes.HasLearnedData ? wtpRes.MaxHours : wtpHours,
                    SampleCount = wtpRes.SampleCount, LaborSource = wtpRes.Source,
                    Confidence = 0.90,
                    Source = wtpRes.HasLearnedData ? $"Learned from {wtpRes.SampleCount} estimates" : "MET: Body Operations - Mitchell CEG: NOT INCLUDED"
                });

                // Cover Vehicle from Weld Sparks
                var coverRes = ResolveLaborTime("cover car", "Body", result.VehicleType);
                var coverHours = coverRes.HasLearnedData ? coverRes.Hours : 0.5m;
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body", PartName = "cover car",
                    Description = "Cover Vehicle from Weld Spark Damage",
                    Category = "Body Operations", LaborHours = coverHours,
                    MinLaborHours = coverRes.HasLearnedData ? coverRes.MinHours : coverHours,
                    MaxLaborHours = coverRes.HasLearnedData ? coverRes.MaxHours : coverHours,
                    SampleCount = coverRes.SampleCount, LaborSource = coverRes.Source,
                    Confidence = 0.85,
                    Source = coverRes.HasLearnedData ? $"Learned from {coverRes.SampleCount} estimates" : "DEG Inquiry 13434 - NOT INCLUDED"
                });

                // Seam Sealer
                var sealerRes = ResolveLaborTime("seam sealer", "Body", result.VehicleType);
                var sealerHours = sealerRes.HasLearnedData ? sealerRes.Hours : 0.3m * Math.Max(1, weldedCount);
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body", PartName = "seam sealer",
                    Description = "Seam Sealer Application (Beyond Attachment Points)",
                    Category = "Body Operations", LaborHours = sealerHours,
                    MinLaborHours = sealerRes.HasLearnedData ? sealerRes.MinHours : sealerHours,
                    MaxLaborHours = sealerRes.HasLearnedData ? sealerRes.MaxHours : sealerHours,
                    SampleCount = sealerRes.SampleCount, LaborSource = sealerRes.Source,
                    Confidence = 0.85,
                    Source = sealerRes.HasLearnedData ? $"Learned from {sealerRes.SampleCount} estimates" : "DEG Inquiry 23010 - additional seam sealer NOT INCLUDED"
                });

                // Cavity Wax / Anti-Corrosion
                var cavityRes = ResolveLaborTime("cavity wax", "Body", result.VehicleType);
                var cavityHours = cavityRes.HasLearnedData ? cavityRes.Hours : 0.5m * Math.Max(1, weldedCount);
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body", PartName = "cavity wax",
                    Description = "Cavity Wax / Anti-Corrosion Treatment",
                    Category = "Body Operations", LaborHours = cavityHours,
                    MinLaborHours = cavityRes.HasLearnedData ? cavityRes.MinHours : cavityHours,
                    MaxLaborHours = cavityRes.HasLearnedData ? cavityRes.MaxHours : cavityHours,
                    SampleCount = cavityRes.SampleCount, LaborSource = cavityRes.Source,
                    Confidence = 0.90,
                    Source = cavityRes.HasLearnedData ? $"Learned from {cavityRes.SampleCount} estimates" : "Mitchell CEG G31 - corrosion protection NOT INCLUDED"
                });
            }

            // Add PLASTIC PART operations — learned-first, fallback to formulas
            if (hasPlasticParts)
            {
                var plasticPanels = result.Operations.Where(o =>
                    (o.OperationType == "Replace" || o.OperationType == "Refinish") &&
                    (o.PartName.Contains("bumper") || o.PartName.Contains("fascia"))).ToList();

                if (plasticPanels.Any())
                {
                    var totalRefinishHours = plasticPanels.Sum(p => p.RefinishHours > 0 ? p.RefinishHours : 2.5m);

                    // Adhesion Promoter
                    var adhRes = ResolveLaborTime("adhesion promoter", "Paint", result.VehicleType);
                    var adhesionHours = adhRes.HasLearnedData ? adhRes.Hours : Math.Min(0.5m, Math.Max(0.2m, totalRefinishHours * 0.10m));
                    result.Operations.Add(new GhostOperation
                    {
                        OperationType = "Paint", PartName = "adhesion promoter",
                        Description = "Adhesion Promoter (Plastic Parts)",
                        Category = "Refinish Operations", RefinishHours = adhesionHours,
                        SampleCount = adhRes.SampleCount, LaborSource = adhRes.Source,
                        Confidence = 0.95,
                        Source = adhRes.HasLearnedData ? $"Learned from {adhRes.SampleCount} estimates" : "Mitchell CEG Section 22 - plastic NOT INCLUDED"
                    });

                    // Flex Additive
                    var flexRes = ResolveLaborTime("flex additive", "Paint", result.VehicleType);
                    var flexHours = flexRes.HasLearnedData ? flexRes.Hours : 0.2m;
                    result.Operations.Add(new GhostOperation
                    {
                        OperationType = "Paint", PartName = "flex additive",
                        Description = "Flex Additive (First Large Plastic Part)",
                        Category = "Refinish Operations", RefinishHours = flexHours,
                        SampleCount = flexRes.SampleCount, LaborSource = flexRes.Source,
                        Confidence = 0.95,
                        Source = flexRes.HasLearnedData ? $"Learned from {flexRes.SampleCount} estimates" : "MET: Part Operations - flexible paint NOT INCLUDED"
                    });
                }
            }

            // Add corrosion protection for all replaced panels
            var replacedPanels = result.Operations.Where(o => o.OperationType == "Replace").ToList();
            if (replacedPanels.Any() && !hasWeldedPanels)
            {
                var corrRes = ResolveLaborTime("corrosion protection", "Body", result.VehicleType);
                var corrHours = corrRes.HasLearnedData ? corrRes.Hours : 0.2m * replacedPanels.Count;
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body", PartName = "corrosion protection",
                    Description = "Corrosion Protection - Hem Flanges",
                    Category = "Body Operations", LaborHours = corrHours,
                    MinLaborHours = corrRes.HasLearnedData ? corrRes.MinHours : corrHours,
                    MaxLaborHours = corrRes.HasLearnedData ? corrRes.MaxHours : corrHours,
                    SampleCount = corrRes.SampleCount, LaborSource = corrRes.Source,
                    Confidence = 0.90,
                    Source = corrRes.HasLearnedData ? $"Learned from {corrRes.SampleCount} estimates" : "Mitchell CEG G31 - hem flange protection NOT INCLUDED"
                });
            }
        }

        private void AddRefinishOperations(GhostEstimateResult result)
        {
            var panelsNeedingRefinish = result.Operations
                .Where(o => o.OperationType == "Replace" || o.OperationType == "Repair")
                .Select(o => o.PartName)
                .Distinct()
                .ToList();

            var hasRepairPanels = result.Operations.Any(o => o.OperationType == "Repair");

            // Track adjacent panels for blending
            var panelsToBlend = new HashSet<string>();

            foreach (var panel in panelsNeedingRefinish)
            {
                var rfnResolution = ResolveRefinishTime(panel);

                // Add refinish for the panel - use proper CCC terminology
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Rfn",  // CCC terminology
                    PartName = panel,
                    Description = $"Refinish {ToTitleCase(panel)}",
                    Category = "Refinish Operations",
                    RefinishHours = rfnResolution.Hours,
                    MinRefinishHours = rfnResolution.MinHours,
                    MaxRefinishHours = rfnResolution.MaxHours,
                    SampleCount = rfnResolution.SampleCount,
                    LaborSource = rfnResolution.Source,
                    Confidence = 0.95,
                    Source = rfnResolution.HasLearnedData
                        ? $"Learned from {rfnResolution.SampleCount} uploaded estimates"
                        : "Mitchell CEG - separate operation"
                });

                // Get adjacent panels for blending
                var adjacent = GetAdjacentPanels(panel);
                foreach (var adj in adjacent)
                {
                    if (!panelsNeedingRefinish.Contains(adj))
                    {
                        panelsToBlend.Add(adj);
                    }
                }
            }

            // Add blend operations — check for learned blend hours first, then formula
            var blendPanelList = panelsToBlend.ToList();
            for (int i = 0; i < blendPanelList.Count; i++)
            {
                var blendPanel = blendPanelList[i];

                // First: check if we have direct learned blend hours
                var blendResolution = ResolveLaborTime(blendPanel, "blend", "");
                decimal blendHours;
                string blendSource;
                int blendSamples;
                decimal blendMin, blendMax;

                if (blendResolution.HasLearnedData)
                {
                    // Use direct learned blend hours (halved for additional panels)
                    blendHours = i == 0 ? blendResolution.Hours : blendResolution.Hours * 0.5m;
                    blendSource = $"Learned blend from {blendResolution.SampleCount} estimates";
                    blendSamples = blendResolution.SampleCount;
                    blendMin = i == 0 ? blendResolution.MinHours : blendResolution.MinHours * 0.5m;
                    blendMax = i == 0 ? blendResolution.MaxHours : blendResolution.MaxHours * 0.5m;
                }
                else
                {
                    // Check for learned blend percentage formula
                    var blendFormula = _knowledgeBase.GetFormula("blend_percentage");
                    decimal blendPercent;
                    if (blendFormula?.DerivedPercentage != null && blendFormula.DerivedPercentage > 0)
                    {
                        blendPercent = (decimal)blendFormula.DerivedPercentage.Value;
                        if (i > 0) blendPercent *= 0.5m; // Halved for additional panels
                    }
                    else
                    {
                        blendPercent = i == 0 ? 0.20m : 0.10m; // Hardcoded fallback
                    }

                    var baseRfnResolution = ResolveRefinishTime(blendPanel);
                    blendHours = baseRfnResolution.Hours * blendPercent;
                    blendSource = "Mitchell CEG Section 28 - blend NOT INCLUDED";
                    blendSamples = 0;
                    blendMin = baseRfnResolution.MinHours * blendPercent;
                    blendMax = baseRfnResolution.MaxHours * blendPercent;
                }

                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Blend",
                    PartName = blendPanel,
                    Description = $"Blend {ToTitleCase(blendPanel)} (Color Match)",
                    Category = "Refinish Operations",
                    RefinishHours = blendHours,
                    MinRefinishHours = blendMin,
                    MaxRefinishHours = blendMax,
                    SampleCount = blendSamples,
                    LaborSource = blendResolution.HasLearnedData ? blendResolution.Source : "fallback",
                    Confidence = blendResolution.HasLearnedData ? 0.90 : 0.80,
                    Source = blendSource
                });
            }

            // Feather, Prime & Block for REPAIR panels
            if (hasRepairPanels)
            {
                var repairPanelCount = result.Operations.Count(o => o.OperationType == "Repair");
                var fpbRes = ResolveLaborTime("feather prime block", "Paint", "");
                var fpbHours = fpbRes.HasLearnedData ? fpbRes.Hours : 0.5m * repairPanelCount;
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Paint", PartName = "feather prime block",
                    Description = "Feather, Prime & Block (Repair Panels)",
                    Category = "Refinish Operations", RefinishHours = fpbHours,
                    MinRefinishHours = fpbRes.HasLearnedData ? fpbRes.MinHours : fpbHours,
                    MaxRefinishHours = fpbRes.HasLearnedData ? fpbRes.MaxHours : fpbHours,
                    SampleCount = fpbRes.SampleCount, LaborSource = fpbRes.Source,
                    Confidence = 0.90,
                    Source = fpbRes.HasLearnedData ? $"Learned from {fpbRes.SampleCount} estimates" : "Mitchell CEG Section 28 - NOT INCLUDED"
                });
            }

            // DE-NIB if refinishing
            if (panelsNeedingRefinish.Any())
            {
                var denibRes = ResolveLaborTime("denib", "Paint", "");
                var denibHours = denibRes.HasLearnedData ? denibRes.Hours : 0.3m;
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Paint", PartName = "denib",
                    Description = "DE-NIB & Polish",
                    Category = "Refinish Operations", RefinishHours = denibHours,
                    MinRefinishHours = denibRes.HasLearnedData ? denibRes.MinHours : denibHours,
                    MaxRefinishHours = denibRes.HasLearnedData ? denibRes.MaxHours : denibHours,
                    SampleCount = denibRes.SampleCount, LaborSource = denibRes.Source,
                    Confidence = 0.85,
                    Source = denibRes.HasLearnedData ? $"Learned from {denibRes.SampleCount} estimates" : "MET: Part Operations - denib NOT INCLUDED"
                });
            }

            // Add overlap deduction note for multiple adjacent panels
            if (panelsNeedingRefinish.Count > 1)
            {
                // Per IncludedNotIncluded.json: Major adjacent panel deduction -0.4 hours
                result.Notes.Add($"Overlap deduction may apply: -0.4h for major adjacent panels (fender AND door refinished together)");
            }
        }

        private void CalculateTotals(GhostEstimateResult result)
        {
            // Labor rates from user config (falls back to defaults if not set)
            var BODY_LABOR_RATE = _ghostConfig.GetEffectiveBodyRate();
            var PAINT_LABOR_RATE = _ghostConfig.GetEffectivePaintRate();
            var MECH_LABOR_RATE = _ghostConfig.GetEffectiveMechRate();
            var FRAME_LABOR_RATE = _ghostConfig.GetEffectiveFrameRate();

            // Calculate hours by labor type
            result.TotalBodyHours = result.Operations
                .Where(o => o.Category == "Body Operations" || o.Category == "Part Operations" || o.Category == "Structural")
                .Sum(o => o.LaborHours);

            result.TotalRefinishHours = result.Operations
                .Where(o => o.Category == "Refinish Operations")
                .Sum(o => o.RefinishHours);

            var mechHours = result.Operations
                .Where(o => o.Category == "Scanning" || o.OperationType == "Mech")
                .Sum(o => o.LaborHours);

            var frameHours = result.Operations
                .Where(o => o.Category == "Frame Operations")
                .Sum(o => o.LaborHours);

            result.TotalSubletAmount = result.Operations
                .Where(o => o.OperationType == "Sublet")
                .Sum(o => o.Price);

            // Calculate dollar totals
            result.TotalBodyLaborDollars = result.TotalBodyHours * BODY_LABOR_RATE;
            result.TotalRefinishLaborDollars = result.TotalRefinishHours * PAINT_LABOR_RATE;
            result.TotalMechLaborDollars = mechHours * MECH_LABOR_RATE;
            result.TotalFrameLaborDollars = frameHours * FRAME_LABOR_RATE;

            result.GrandTotalLaborDollars =
                result.TotalBodyLaborDollars +
                result.TotalRefinishLaborDollars +
                result.TotalMechLaborDollars +
                result.TotalFrameLaborDollars +
                result.TotalSubletAmount;

            // Count refinish panels
            result.RefinishPanelCount = result.Operations.Count(o =>
                (o.OperationType == "Rfn" || o.OperationType == "Refinish") && o.PartName != "clear coat");
            result.BlendPanelCount = result.Operations.Count(o => o.OperationType == "Blend");

            // Calculate learned dollar totals from operations that have learned dollar amounts
            var opsWithDollars = result.Operations.Where(o => o.LearnedDollarAmount.HasValue && o.LearnedDollarAmount > 0).ToList();
            if (opsWithDollars.Count > 0)
            {
                result.LearnedDollarTotal = opsWithDollars.Sum(o => o.LearnedDollarAmount!.Value);
                result.MinDollarTotal = opsWithDollars.Sum(o => o.MinDollarAmount ?? o.LearnedDollarAmount!.Value);
                result.MaxDollarTotal = opsWithDollars.Sum(o => o.MaxDollarAmount ?? o.LearnedDollarAmount!.Value);
            }
        }

        private void AddConfidenceNotes(GhostEstimateResult result)
        {
            var stats = _smartEngine.GetStats();
            var kbStats = _knowledgeBase.GetStatistics();
            var historyCount = _historyDb.GetAllEstimates().Count;

            // Show real learning stats
            var totalDataSources = stats.TotalEstimatesLearned + historyCount;

            if (totalDataSources < 10)
            {
                result.Notes.Add($"Low training data ({totalDataSources} estimates, {kbStats.TotalPartsLearned} parts learned). Upload more estimates to improve accuracy.");
            }
            else if (totalDataSources < 50)
            {
                result.Notes.Add($"Moderate training ({totalDataSources} estimates, {kbStats.TotalPartsLearned} parts, {kbStats.TotalCoOccurrencePatterns} patterns). Accuracy improves with more data.");
            }
            else
            {
                result.Notes.Add($"Well-trained ({totalDataSources} estimates, {kbStats.TotalPartsLearned} parts, {kbStats.TotalCoOccurrencePatterns} patterns). High confidence in labor times.");
            }

            // Count how many operations used real learned data vs fallback
            var learnedCount = result.Operations.Count(o => o.Source != null && (o.Source.StartsWith("Learned") || o.Source.Contains("uploaded estimates")));
            var fallbackCount = result.Operations.Count(o => o.Source != null && o.Source.Contains("CCC/MOTOR"));

            if (learnedCount > 0)
            {
                result.Notes.Add($"{learnedCount} operations used real learned labor times from your uploaded estimates.");
            }
            if (fallbackCount > 0 && totalDataSources > 0)
            {
                result.Notes.Add($"{fallbackCount} operations used default times — upload more estimates with these parts to improve.");
            }

            // Add notes for low-confidence operations
            var lowConfidence = result.Operations.Where(o => o.Confidence < 0.7).ToList();
            if (lowConfidence.Any())
            {
                result.Notes.Add($"{lowConfidence.Count} operations have lower confidence - verify these carefully.");
            }
        }

        private LaborResolution ResolveLaborTime(string partName, string operationType, string vehicleType)
        {
            var partLower = partName.ToLower();

            // PRIORITY 1: Real learned data from uploaded estimates (LearnedKnowledgeBase)
            var canonicalName = _knowledgeBase.ResolveAlias(partLower) ?? partLower;
            var learnedStats = _knowledgeBase.GetOperationStats(canonicalName, operationType);

            if (learnedStats != null && learnedStats.SampleCount >= 1 && learnedStats.MeanLaborHours > 0)
            {
                var hours = learnedStats.MedianLaborHours > 0 ? learnedStats.MedianLaborHours : learnedStats.MeanLaborHours;
                System.Diagnostics.Debug.WriteLine($"[Ghost] LEARNED data for {partName} {operationType}: {hours}h (from {learnedStats.SampleCount} estimates)");
                return new LaborResolution
                {
                    Hours = hours,
                    Source = "learned",
                    SampleCount = learnedStats.SampleCount,
                    MinHours = learnedStats.MinLaborHours,
                    MaxHours = learnedStats.MaxLaborHours,
                    MedianHours = learnedStats.MedianLaborHours,
                    Confidence = learnedStats.SampleCount >= 3 ? 0.95 : learnedStats.SampleCount == 2 ? 0.85 : 0.5,
                    LearnedDollarAmount = learnedStats.PriceValues.Count > 0 ? (decimal?)learnedStats.MeanPrice : null,
                    MinDollarAmount = learnedStats.PriceValues.Count > 0 ? (decimal?)learnedStats.MinPrice : null,
                    MaxDollarAmount = learnedStats.PriceValues.Count > 0 ? (decimal?)learnedStats.MaxPrice : null
                };
            }

            // PRIORITY 2: Vehicle-specific learned data (truck vs SUV vs car)
            if (!string.IsNullOrEmpty(vehicleType))
            {
                var vehSpecific = _knowledgeBase.GetVehicleSpecificLaborTime(vehicleType, canonicalName, operationType);
                if (vehSpecific.HasValue && vehSpecific.Value > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Ghost] Vehicle-specific data for {vehicleType} {partName} {operationType}: {vehSpecific.Value}h");
                    return new LaborResolution
                    {
                        Hours = vehSpecific.Value,
                        Source = "vehicle_specific",
                        SampleCount = 1,
                        MinHours = vehSpecific.Value,
                        MaxHours = vehSpecific.Value,
                        MedianHours = vehSpecific.Value,
                        Confidence = 0.7
                    };
                }
            }

            // PRIORITY 3: Learned patterns from EstimateLearningService
            var patterns = _learningService.SearchPatterns(partName, 5);
            var matchingPattern = patterns.FirstOrDefault(p =>
                p.PartName.Equals(partName, StringComparison.OrdinalIgnoreCase) &&
                p.OperationType.Equals(operationType, StringComparison.OrdinalIgnoreCase));

            if (matchingPattern != null && matchingPattern.ExampleCount >= 1)
            {
                var patternOps = matchingPattern.Operations?
                    .Where(o => o.LaborHours > 0)
                    .ToList();
                if (patternOps != null && patternOps.Any())
                {
                    var avgHours = patternOps.Average(o => o.LaborHours);
                    if (avgHours > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Ghost] Pattern data for {partName} {operationType}: {avgHours}h (from {matchingPattern.ExampleCount} examples)");
                        return new LaborResolution
                        {
                            Hours = avgHours,
                            Source = "pattern",
                            SampleCount = matchingPattern.ExampleCount,
                            MinHours = patternOps.Min(o => o.LaborHours),
                            MaxHours = patternOps.Max(o => o.LaborHours),
                            MedianHours = avgHours,
                            Confidence = matchingPattern.ExampleCount >= 3 ? 0.85 : matchingPattern.ExampleCount == 2 ? 0.75 : 0.5
                        };
                    }
                }
            }

            // PRIORITY 4: Estimate history database — average across all stored estimates
            var historyEstimates = _historyDb.GetAllEstimates();
            if (historyEstimates.Count > 0)
            {
                var matchingLines = historyEstimates
                    .SelectMany(e => e.LineItems)
                    .Where(li => li.LaborHours > 0 &&
                                 !string.IsNullOrEmpty(li.PartName) &&
                                 li.PartName.ToLower().Contains(partLower) &&
                                 li.OperationType.Equals(operationType, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingLines.Count >= 1)
                {
                    var avgHours = matchingLines.Average(li => li.LaborHours);
                    // Also compute dollar amounts from matching line items
                    var priceLines = matchingLines.Where(li => li.Price > 0).ToList();
                    decimal? avgPrice = priceLines.Count > 0 ? priceLines.Average(li => li.Price) : null;
                    decimal? minPrice = priceLines.Count > 0 ? priceLines.Min(li => li.Price) : null;
                    decimal? maxPrice = priceLines.Count > 0 ? priceLines.Max(li => li.Price) : null;

                    System.Diagnostics.Debug.WriteLine($"[Ghost] History DB for {partName} {operationType}: {avgHours:F2}h (from {matchingLines.Count} line items)");
                    return new LaborResolution
                    {
                        Hours = avgHours,
                        Source = "history_db",
                        SampleCount = matchingLines.Count,
                        MinHours = matchingLines.Min(li => li.LaborHours),
                        MaxHours = matchingLines.Max(li => li.LaborHours),
                        MedianHours = avgHours,
                        Confidence = matchingLines.Count >= 3 ? 0.85 : matchingLines.Count == 2 ? 0.75 : 0.5,
                        LearnedDollarAmount = avgPrice,
                        MinDollarAmount = minPrice,
                        MaxDollarAmount = maxPrice
                    };
                }
            }

            // PRIORITY 5: Excel estimating tool data (ExcelOperationsDB.json)
            var excelLookup = _excelProvider.LookupForGhost(partName, operationType);
            if (excelLookup.Found && excelLookup.LaborHours > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] Excel tool data for {partName} {operationType}: {excelLookup.LaborHours}h (sheet: {excelLookup.SheetName})");
                return new LaborResolution
                {
                    Hours = excelLookup.LaborHours,
                    Source = "excel_tool",
                    SampleCount = 1,
                    MinHours = excelLookup.LaborHours,
                    MaxHours = excelLookup.LaborHours,
                    MedianHours = excelLookup.LaborHours,
                    Confidence = 0.7
                };
            }

            // PRIORITY 6: MET data from IncludedNotIncluded.json
            if (_operationsData?.Operations != null)
            {
                var matchingOp = _operationsData.Operations.FirstOrDefault(op =>
                    op.Aliases?.Any(a => partLower.Contains(a.ToLower())) == true ||
                    partLower.Contains(op.PartName?.ToLower() ?? ""));

                if (matchingOp?.MetOperations != null)
                {
                    var totalHours = matchingOp.MetOperations.Sum(m => m.Hours);
                    if (totalHours > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Ghost] MET data for {partName}: {totalHours}h total");
                        return new LaborResolution
                        {
                            Hours = (decimal)totalHours,
                            Source = "met_data",
                            SampleCount = 0,
                            MinHours = (decimal)totalHours,
                            MaxHours = (decimal)totalHours,
                            MedianHours = (decimal)totalHours,
                            Confidence = 0.8
                        };
                    }
                }
            }

            // PRIORITY 7: Hardcoded fallback (last resort)
            System.Diagnostics.Debug.WriteLine($"[Ghost] FALLBACK for {partName} {operationType} — no learned data available");
            var fallbackHours = (partName, operationType) switch
            {
                (var p, "Replace") when p.Contains("bumper cover") || p.Contains("fascia") => 1.5m,
                (var p, "Replace") when p.Contains("front bumper") => 1.5m,
                (var p, "Replace") when p.Contains("rear bumper") => 1.2m,
                (var p, "Replace") when p.Contains("fender") => 2.0m,
                (var p, "Replace") when p.Contains("hood") => 1.0m,
                (var p, "Replace") when p.Contains("front door") => 3.5m,
                (var p, "Replace") when p.Contains("rear door") => 3.2m,
                (var p, "Replace") when p.Contains("quarter") => 8.0m,
                (var p, "Replace") when p.Contains("rocker") => 6.0m,
                (var p, "Replace") when p.Contains("roof") => 10.0m,
                (var p, "Replace") when p.Contains("trunk") || p.Contains("decklid") => 1.5m,
                (var p, "Replace") when p.Contains("liftgate") || p.Contains("tailgate") => 2.0m,
                (var p, "Repair") when p.Contains("bumper") => 2.0m,
                (var p, "Repair") when p.Contains("fender") => 3.0m,
                (var p, "Repair") when p.Contains("door") => 2.5m,
                (var p, "Repair") when p.Contains("hood") => 2.5m,
                (var p, "Repair") when p.Contains("quarter") => 4.0m,
                (var p, "R&I") when p.Contains("headlight") || p.Contains("head light") => 0.5m,
                (var p, "R&I") when p.Contains("tail light") || p.Contains("taillight") => 0.4m,
                (var p, "R&I") when p.Contains("fog light") || p.Contains("fog lamp") => 0.3m,
                (var p, "R&I") when p.Contains("grille") => 0.3m,
                (var p, "R&I") when p.Contains("liner") || p.Contains("fender liner") => 0.3m,
                (var p, "R&I") when p.Contains("mirror") => 0.5m,
                (var p, "R&I") when p.Contains("door trim") || p.Contains("trim panel") => 0.4m,
                (var p, "R&I") when p.Contains("molding") => 0.2m,
                (var p, "R&I") when p.Contains("emblem") || p.Contains("nameplate") => 0.1m,
                (var p, "R&I") when p.Contains("sensor") || p.Contains("park sensor") => 0.15m,
                (var p, "R&I") when p.Contains("reinforcement") || p.Contains("rebar") => 0.3m,
                _ => 1.0m
            };

            return new LaborResolution
            {
                Hours = fallbackHours,
                Source = "fallback",
                SampleCount = 0,
                MinHours = fallbackHours,
                MaxHours = fallbackHours,
                MedianHours = fallbackHours,
                Confidence = 0.5
            };
        }

        private LaborResolution ResolveRefinishTime(string partName)
        {
            var partLower = partName.ToLower();

            // PRIORITY 1: Real learned refinish data from uploaded estimates
            var canonicalName = _knowledgeBase.ResolveAlias(partLower) ?? partLower;

            foreach (var opType in new[] { "refinish", "rfn", "replace", "repair" })
            {
                var stats = _knowledgeBase.GetOperationStats(canonicalName, opType);
                if (stats != null && stats.RefinishHoursValues.Count >= 1 && stats.MeanRefinishHours > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Ghost] LEARNED refinish for {partName}: {stats.MeanRefinishHours}h (from {stats.RefinishHoursValues.Count} samples)");
                    return new LaborResolution
                    {
                        Hours = stats.MeanRefinishHours,
                        Source = "learned",
                        SampleCount = stats.RefinishHoursValues.Count,
                        MinHours = stats.MinRefinishHours,
                        MaxHours = stats.MaxRefinishHours,
                        MedianHours = stats.MeanRefinishHours,
                        Confidence = stats.RefinishHoursValues.Count >= 3 ? 0.95 : stats.RefinishHoursValues.Count == 2 ? 0.85 : 0.5
                    };
                }
            }

            // PRIORITY 2: Estimate history database — average refinish hours
            var historyEstimates = _historyDb.GetAllEstimates();
            if (historyEstimates.Count > 0)
            {
                var matchingLines = historyEstimates
                    .SelectMany(e => e.LineItems)
                    .Where(li => li.RefinishHours > 0 &&
                                 !string.IsNullOrEmpty(li.PartName) &&
                                 li.PartName.ToLower().Contains(partLower))
                    .ToList();

                if (matchingLines.Count >= 1)
                {
                    var avgHours = matchingLines.Average(li => li.RefinishHours);
                    System.Diagnostics.Debug.WriteLine($"[Ghost] History DB refinish for {partName}: {avgHours:F2}h (from {matchingLines.Count} lines)");
                    return new LaborResolution
                    {
                        Hours = avgHours,
                        Source = "history_db",
                        SampleCount = matchingLines.Count,
                        MinHours = matchingLines.Min(li => li.RefinishHours),
                        MaxHours = matchingLines.Max(li => li.RefinishHours),
                        MedianHours = avgHours,
                        Confidence = matchingLines.Count >= 3 ? 0.85 : matchingLines.Count == 2 ? 0.75 : 0.5
                    };
                }
            }

            // PRIORITY 3: Excel estimating tool data
            var excelRfn = _excelProvider.LookupForGhost(partName, "refinish");
            if (excelRfn.Found && excelRfn.RefinishHours > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] Excel refinish for {partName}: {excelRfn.RefinishHours}h (sheet: {excelRfn.SheetName})");
                return new LaborResolution
                {
                    Hours = excelRfn.RefinishHours,
                    Source = "excel_tool",
                    SampleCount = 1,
                    MinHours = excelRfn.RefinishHours,
                    MaxHours = excelRfn.RefinishHours,
                    MedianHours = excelRfn.RefinishHours,
                    Confidence = 0.7
                };
            }

            // PRIORITY 4: Hardcoded fallback
            System.Diagnostics.Debug.WriteLine($"[Ghost] FALLBACK refinish for {partName} — no learned data");
            var fallbackHours = partName switch
            {
                var p when p.Contains("hood") => 3.5m,
                var p when p.Contains("roof") => 4.0m,
                var p when p.Contains("trunk") || p.Contains("decklid") => 3.0m,
                var p when p.Contains("fender") => 3.0m,
                var p when p.Contains("door") => 3.0m,
                var p when p.Contains("quarter") => 4.0m,
                var p when p.Contains("bumper") || p.Contains("fascia") => 2.5m,
                var p when p.Contains("valance") => 1.5m,
                var p when p.Contains("spoiler") => 2.0m,
                var p when p.Contains("mirror") => 0.8m,
                var p when p.Contains("molding") => 0.5m,
                _ => 2.5m
            };

            return new LaborResolution
            {
                Hours = fallbackHours,
                Source = "fallback",
                SampleCount = 0,
                MinHours = fallbackHours,
                MaxHours = fallbackHours,
                MedianHours = fallbackHours,
                Confidence = 0.5
            };
        }

        /// <summary>
        /// Check if the labor time for this part+operation came from real learned data (not hardcoded fallback)
        /// </summary>
        private bool IsFromLearnedData(string partName, string operationType, string vehicleType)
        {
            return ResolveLaborTime(partName, operationType, vehicleType).HasLearnedData;
        }

        /// <summary>
        /// Get learned labor/sublet time for miscellaneous operations (scans, calibrations, etc.)
        /// </summary>
        private decimal GetLearnedSubletOrLaborTime(string partName, string operationType, decimal fallback)
        {
            var canonicalName = _knowledgeBase.ResolveAlias(partName.ToLower()) ?? partName.ToLower();
            var stats = _knowledgeBase.GetOperationStats(canonicalName, operationType.ToLower());
            if (stats != null && stats.SampleCount >= 1 && stats.MeanLaborHours > 0)
                return stats.MeanLaborHours;

            // Check history DB
            var historyEstimates = _historyDb.GetAllEstimates();
            if (historyEstimates.Count > 0)
            {
                var matchingLines = historyEstimates
                    .SelectMany(e => e.LineItems)
                    .Where(li => li.LaborHours > 0 &&
                                 !string.IsNullOrEmpty(li.Description) &&
                                 li.Description.ToLower().Contains(partName.ToLower()))
                    .ToList();

                if (matchingLines.Count >= 1)
                    return matchingLines.Average(li => li.LaborHours);
            }

            return fallback;
        }

        /// <summary>
        /// Get learned sublet price from real estimates
        /// </summary>
        private decimal GetLearnedSubletPrice(string partName, decimal fallback)
        {
            return ResolveSubletPrice(partName, fallback).price;
        }

        private (decimal price, int sampleCount, decimal minPrice, decimal maxPrice) ResolveSubletPrice(string partName, decimal fallback)
        {
            var canonicalName = _knowledgeBase.ResolveAlias(partName.ToLower()) ?? partName.ToLower();

            // Check for price data in knowledge base
            foreach (var opType in new[] { "sublet", "calibration", "mech" })
            {
                var stats = _knowledgeBase.GetOperationStats(canonicalName, opType);
                if (stats != null && stats.PriceValues.Count >= 1 && stats.MeanPrice > 0)
                    return (stats.MeanPrice, stats.PriceValues.Count, stats.MinPrice, stats.MaxPrice);
            }

            // Check history DB for sublet lines matching this description
            var historyEstimates = _historyDb.GetAllEstimates();
            if (historyEstimates.Count > 0)
            {
                var matchingLines = historyEstimates
                    .SelectMany(e => e.LineItems)
                    .Where(li => li.Price > 0 &&
                                 !string.IsNullOrEmpty(li.Description) &&
                                 li.Description.ToLower().Contains(partName.ToLower()))
                    .ToList();

                if (matchingLines.Count >= 1)
                    return (matchingLines.Average(li => li.Price), matchingLines.Count, matchingLines.Min(li => li.Price), matchingLines.Max(li => li.Price));
            }

            // Check history DB via GetOperationPaymentStats pattern
            if (historyEstimates.Count > 0)
            {
                var paymentLines = historyEstimates
                    .SelectMany(e => e.LineItems)
                    .Where(li => li.Price > 0 &&
                                 !string.IsNullOrEmpty(li.PartName) &&
                                 li.PartName.ToLower().Contains(partName.ToLower()))
                    .ToList();

                if (paymentLines.Count >= 1)
                    return (paymentLines.Average(li => li.Price), paymentLines.Count, paymentLines.Min(li => li.Price), paymentLines.Max(li => li.Price));
            }

            return (fallback, 0, fallback, fallback);
        }

        private bool RequiresRAndI(string panelName, string operation)
        {
            if (operation == "R&I") return false;

            // Panels that typically have attached components
            var panelsWithComponents = new[]
            {
                "bumper", "fender", "hood", "door", "trunk", "liftgate", "grille"
            };

            return panelsWithComponents.Any(p => panelName.Contains(p));
        }

        private string[] GetRAndIParts(string panelName)
        {
            // PRIORITY 1: Learn R&I parts from real estimates using co-occurrence data
            var canonicalName = _knowledgeBase.ResolveAlias(panelName.ToLower()) ?? panelName.ToLower();
            var coOccurrences = _knowledgeBase.GetCoOccurrences(canonicalName, 0.3);

            // Filter to R&I operations that commonly appear with this panel
            var learnedRiParts = coOccurrences
                .Where(c => c.AssociatedOperation.Equals("r&i", StringComparison.OrdinalIgnoreCase) ||
                            c.AssociatedOperation.Equals("R&I", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.AssociatedPart)
                .ToArray();

            if (learnedRiParts.Length >= 1)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] LEARNED R&I parts for {panelName}: {string.Join(", ", learnedRiParts)}");
                return learnedRiParts;
            }

            // PRIORITY 2: Hardcoded fallback
            return panelName switch
            {
                var p when p.Contains("front bumper") => new[] { "grille", "fog light", "headlight", "front bumper reinforcement" },
                var p when p.Contains("rear bumper") => new[] { "tail light", "backup sensor", "rear bumper reinforcement" },
                var p when p.Contains("fender") => new[] { "headlight", "fender liner", "side marker" },
                var p when p.Contains("hood") => new[] { "hood insulator", "hood emblem" },
                var p when p.Contains("front door") => new[] { "door trim panel", "mirror", "door handle" },
                var p when p.Contains("rear door") => new[] { "door trim panel", "door handle" },
                var p when p.Contains("trunk") => new[] { "trunk trim", "license plate lights" },
                _ => Array.Empty<string>()
            };
        }

        private string[] GetAdjacentPanels(string panelName)
        {
            // PRIORITY 1: Learned adjacent panels from real uploaded estimates
            var canonicalName = _knowledgeBase.ResolveAlias(panelName.ToLower()) ?? panelName.ToLower();
            var learnedAdjacent = _knowledgeBase.GetAdjacentPanels(canonicalName);
            if (learnedAdjacent.Count >= 1)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] LEARNED adjacent for {panelName}: {string.Join(", ", learnedAdjacent)}");
                return learnedAdjacent.ToArray();
            }

            // PRIORITY 2: Hardcoded fallback
            return panelName switch
            {
                var p when p.Contains("front bumper") => new[] { "hood", "fender" },
                var p when p.Contains("rear bumper") => new[] { "quarter panel", "trunk lid" },
                var p when p.Contains("fender") => new[] { "hood", "front door", "front bumper cover" },
                var p when p.Contains("hood") => new[] { "fender" },
                var p when p.Contains("front door") => new[] { "fender", "rear door" },
                var p when p.Contains("rear door") => new[] { "front door", "quarter panel" },
                var p when p.Contains("quarter") => new[] { "rear door", "rear bumper cover", "trunk lid" },
                var p when p.Contains("trunk") || p.Contains("decklid") => new[] { "quarter panel" },
                var p when p.Contains("roof") => new[] { "quarter panel" },
                _ => Array.Empty<string>()
            };
        }

        private string DetermineCategory(string panelName)
        {
            if (panelName.Contains("pillar") || panelName.Contains("rail") || panelName.Contains("apron"))
                return "Structural";
            if (panelName.Contains("frame") || panelName.Contains("subframe"))
                return "Frame Operations";
            return "Part Operations";
        }

        /// <summary>
        /// Maps a part name (and optional category) to a CCC estimate section name.
        /// </summary>
        private static string MapToCCCSection(string partName, string category = "")
        {
            var lower = (partName ?? "").ToLowerInvariant();

            // Scan/diagnostic operations
            if (lower.Contains("scan") || lower.Contains("diagnostic") || lower.Contains("oem research") ||
                lower.Contains("adas") || lower.Contains("calibration"))
                return "VEHICLE DIAGNOSTICS";

            // Electrical
            if (lower.Contains("battery") || lower.Contains("electronic reset") || lower.Contains("electrical"))
                return "ELECTRICAL";

            // Restraint systems
            if (lower.Contains("restraint") || lower.Contains("air bag") || lower.Contains("airbag") ||
                lower.Contains("srs"))
                return "RESTRAINT SYSTEMS";

            // Frame/structural
            if (lower.Contains("frame") || lower.Contains("subframe") || lower.Contains("rail") ||
                lower.Contains("apron"))
                return "FRAME";

            // Front bumper / grille
            if (lower.Contains("front bumper") || lower.Contains("grille") || lower.Contains("front fascia"))
                return "FRONT BUMPER & GRILLE";

            // Rear bumper
            if (lower.Contains("rear bumper") || lower.Contains("rear fascia"))
                return "REAR BUMPER";

            // Lamps
            if (lower.Contains("headlamp") || lower.Contains("headlight") || lower.Contains("head lamp") ||
                lower.Contains("head light") || lower.Contains("fog light") || lower.Contains("fog lamp"))
                return "FRONT LAMPS";
            if (lower.Contains("taillamp") || lower.Contains("taillight") || lower.Contains("tail light") ||
                lower.Contains("tail lamp"))
                return "REAR LAMPS";

            // Radiator support
            if (lower.Contains("radiator support") || lower.Contains("core support"))
                return "RADIATOR SUPPORT";

            // Hood
            if (lower.Contains("hood"))
                return "HOOD";

            // Fender
            if (lower.Contains("fender"))
                return "FENDER";

            // Doors — check front/rear before generic door
            if (lower.Contains("front door"))
                return "FRONT DOOR";
            if (lower.Contains("rear door"))
                return "REAR DOOR";
            if (lower.Contains("door"))
                return "FRONT DOOR"; // Default generic door to front

            // Mirror → door section
            if (lower.Contains("mirror"))
                return "FRONT DOOR";

            // Quarter panel
            if (lower.Contains("quarter"))
                return "QUARTER PANEL";

            // Pillars, rocker, floor
            if (lower.Contains("pillar") || lower.Contains("rocker") || lower.Contains("floor pan"))
                return "PILLARS, ROCKER & FLOOR";

            // Roof
            if (lower.Contains("roof") || lower.Contains("sunroof"))
                return "ROOF";

            // Trunk / decklid / liftgate
            if (lower.Contains("trunk") || lower.Contains("decklid") || lower.Contains("deck lid") ||
                lower.Contains("liftgate") || lower.Contains("lift gate"))
                return "TRUNK / DECKLID";

            // Glass
            if (lower.Contains("windshield") || lower.Contains("glass") || lower.Contains("back glass"))
                return "GLASS";

            // Instrument panel / dash
            if (lower.Contains("dash") || lower.Contains("instrument panel"))
                return "INSTRUMENT PANEL";

            // Mechanical parts
            if (lower.Contains("radiator") || lower.Contains("condenser") || lower.Contains("suspension") ||
                lower.Contains("steering") || lower.Contains("engine") || lower.Contains("wheel"))
                return "MECHANICAL";

            // Bumper fallback (generic "bumper" without front/rear)
            if (lower.Contains("bumper"))
                return "FRONT BUMPER & GRILLE";

            // Category-based fallbacks
            if (!string.IsNullOrEmpty(category))
            {
                if (category == "Scanning" || category == "Calibration")
                    return "VEHICLE DIAGNOSTICS";
                if (category == "SRS Operations")
                    return "RESTRAINT SYSTEMS";
                if (category == "Cover Car Operations" || category == "SOP Operations")
                    return "MISCELLANEOUS OPERATIONS";
                if (category == "Mechanical Operations")
                    return "MECHANICAL";
                if (category == "Frame Operations" || category == "Structural")
                    return "FRAME";
            }

            return "MISCELLANEOUS OPERATIONS";
        }

        /// <summary>
        /// Maps an Excel sheet display category directly to its CCC section.
        /// </summary>
        private static string MapExcelCategoryToSection(string displayCategory)
        {
            return displayCategory switch
            {
                "SRS Operations" => "RESTRAINT SYSTEMS",
                "Cover Car Operations" => "MISCELLANEOUS OPERATIONS",
                "SOP Operations" => "MISCELLANEOUS OPERATIONS",
                "Mechanical Operations" => "MECHANICAL",
                _ => "MISCELLANEOUS OPERATIONS"
            };
        }

        private bool OperationsMatch(GhostOperation op1, GhostOperation op2)
        {
            // Fuzzy match on part name and operation type
            var part1 = op1.PartName.ToLower().Trim();
            var part2 = op2.PartName.ToLower().Trim();
            var type1 = op1.OperationType.ToLower();
            var type2 = op2.OperationType.ToLower();

            // Exact match
            if (part1 == part2 && type1 == type2)
                return true;

            // Partial match (one contains the other)
            if ((part1.Contains(part2) || part2.Contains(part1)) && type1 == type2)
                return true;

            return false;
        }

        private string GenerateComparisonSummary(GhostComparisonResult result)
        {
            var lines = new List<string>();

            if (result.GhostFoundMissing.Count > 0)
            {
                lines.Add($"AI found {result.GhostFoundMissing.Count} operations you might have missed");
            }

            if (result.UserFoundExtra.Count > 0)
            {
                lines.Add($"You caught {result.UserFoundExtra.Count} things AI didn't include");
            }

            if (result.BothHave.Count > 0)
            {
                lines.Add($"{result.BothHave.Count} operations match ({result.MatchPercentage:F0}% agreement)");
            }

            return string.Join("\n", lines);
        }

        private string ToTitleCase(string input)
        {
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
        }

        /// <summary>
        /// Clean operation descriptions: strip CCC line prefixes, part numbers, trailing metadata.
        /// </summary>
        private string CleanOperationDescription(string desc)
        {
            if (string.IsNullOrWhiteSpace(desc)) return desc;

            // Strip CCC line prefixes (5+ digit codes like "900500")
            desc = Regex.Replace(desc, @"^\d{5,}\s*", "");

            // Strip side codes at start (L, R, LT, RT) followed by space — but only if standalone
            // Don't strip if it's part of a word like "Left"
            desc = Regex.Replace(desc, @"^(LT|RT|L|R)\s+", "", RegexOptions.IgnoreCase);

            // Strip part numbers (6+ mixed alphanumeric like "0008388910" or "AB12CD34")
            desc = Regex.Replace(desc, @"\b\d{6,}\b", "");
            desc = Regex.Replace(desc, @"\b[A-Z0-9]{8,}\b", "");

            // Strip trailing CCC metadata markers: "Refinish*", "0.3*", "Existing", "New", "Yes"
            desc = Regex.Replace(desc, @"\s+\d+\.?\d*\*", "");  // "0.3*"
            desc = Regex.Replace(desc, @"\s*\*\s*", " ");        // standalone "*"
            desc = Regex.Replace(desc, @"\s+(Existing|New|Yes)\s*$", "", RegexOptions.IgnoreCase);

            // Clean up extra whitespace
            desc = Regex.Replace(desc, @"\s{2,}", " ").Trim();

            return desc;
        }

        /// <summary>
        /// Clean part name: strip part numbers and CCC codes but preserve meaningful text.
        /// </summary>
        private string CleanPartName(string partName)
        {
            if (string.IsNullOrWhiteSpace(partName)) return partName;

            // Strip numeric part numbers (6+ digits)
            partName = Regex.Replace(partName, @"\b\d{6,}\b", "");
            // Strip mixed alphanumeric codes (8+ chars)
            partName = Regex.Replace(partName, @"\b[A-Z0-9]{8,}\b", "");
            // Clean whitespace
            partName = Regex.Replace(partName, @"\s{2,}", " ").Trim();

            return partName;
        }

        /// <summary>
        /// Sanitize hours to prevent absurd learned values.
        /// Caps hours by operation category to reasonable maximums.
        /// </summary>
        private void SanitizeHours(GhostEstimateResult result)
        {
            var sopMiscPatterns = new[] { "hazmat", "hazardous", "disposal", "mask", "clean", "glass cleaner", "protect", "cover car", "tape" };

            foreach (var op in result.Operations)
            {
                var partLower = op.PartName?.ToLower() ?? "";
                var descLower = op.Description?.ToLower() ?? "";
                var opTypeLower = op.OperationType?.ToLower() ?? "";
                var combined = $"{partLower} {descLower}";

                // SOP/misc operations: max 1.5h
                if (sopMiscPatterns.Any(p => combined.Contains(p)))
                {
                    if (op.LaborHours > 1.5m) op.LaborHours = 1.5m;
                    if (op.RefinishHours > 1.5m) op.RefinishHours = 1.5m;
                    // Cap SOP prices at $50 (unless it's a part/material sublet)
                    if (op.Price > 50m && opTypeLower != "sublet") op.Price = 50m;
                }
                // R&I operations: max 5.0h
                else if (opTypeLower == "r&i")
                {
                    if (op.LaborHours > 5.0m) op.LaborHours = 5.0m;
                }
                // Blend operations: max 4.0h
                else if (opTypeLower == "blend")
                {
                    if (op.RefinishHours > 4.0m) op.RefinishHours = 4.0m;
                }
                // Refinish operations: max 8.0h
                else if (opTypeLower == "rfn" || opTypeLower == "refinish" || opTypeLower == "paint")
                {
                    if (op.RefinishHours > 8.0m) op.RefinishHours = 8.0m;
                }
                // Repair body operations: max 15.0h
                else if (opTypeLower == "rpr" || opTypeLower == "repair")
                {
                    if (op.LaborHours > 15.0m) op.LaborHours = 15.0m;
                }

                // Absolute cap: no single operation > 20h
                if (op.LaborHours > 20.0m) op.LaborHours = 20.0m;
                if (op.RefinishHours > 20.0m) op.RefinishHours = 20.0m;
            }
        }

        /// <summary>
        /// Global deduplication across all operation-adding paths.
        /// Normalizes by PartName + OperationType + Side and keeps highest confidence on collision.
        /// Also caps scans to max 1 Pre-Scan + 1 Post-Scan.
        /// </summary>
        private void DeduplicateOperations(GhostEstimateResult result)
        {
            var dedupMap = new Dictionary<string, GhostOperation>(StringComparer.OrdinalIgnoreCase);
            var dedupedList = new List<GhostOperation>();

            foreach (var op in result.Operations)
            {
                var normPart = NormalizePart(op.PartName);
                var normOp = NormalizeOp(op.OperationType);
                var key = $"{normPart}|{normOp}|{op.Side}";

                if (dedupMap.TryGetValue(key, out var existing))
                {
                    // Keep the one with higher confidence
                    if (op.Confidence > existing.Confidence)
                    {
                        dedupedList.Remove(existing);
                        dedupMap[key] = op;
                        dedupedList.Add(op);
                    }
                    // else skip the duplicate
                }
                else
                {
                    dedupMap[key] = op;
                    dedupedList.Add(op);
                }
            }

            // Cap scans: max 1 Pre-Scan + 1 Post-Scan
            var preScans = dedupedList.Where(o => IsScanOp(o, "pre")).ToList();
            var postScans = dedupedList.Where(o => IsScanOp(o, "post")).ToList();

            if (preScans.Count > 1)
            {
                var keep = preScans.OrderByDescending(o => o.Confidence).First();
                foreach (var dup in preScans.Where(o => o != keep))
                    dedupedList.Remove(dup);
            }
            if (postScans.Count > 1)
            {
                var keep = postScans.OrderByDescending(o => o.Confidence).First();
                foreach (var dup in postScans.Where(o => o != keep))
                    dedupedList.Remove(dup);
            }

            result.Operations = dedupedList;
        }

        private static string NormalizePart(string? partName)
        {
            if (string.IsNullOrWhiteSpace(partName)) return "";
            return partName.ToLower().Trim()
                .Replace("-", " ").Replace("_", " ")
                .Replace("  ", " ");
        }

        private static string NormalizeOp(string? opType)
        {
            if (string.IsNullOrWhiteSpace(opType)) return "";
            var op = opType.ToLower().Trim();
            return op switch
            {
                "repl" => "replace",
                "rpr" => "repair",
                "rfn" or "refinish" => "refinish",
                _ => op
            };
        }

        private static bool IsScanOp(GhostOperation op, string preOrPost)
        {
            var partLower = op.PartName?.ToLower() ?? "";
            var descLower = op.Description?.ToLower() ?? "";
            var isScan = op.Category == "Scanning" ||
                         partLower.Contains("scan") ||
                         descLower.Contains("scan");
            if (!isScan) return false;
            return partLower.Contains(preOrPost) || descLower.Contains(preOrPost);
        }

        #endregion
    }

    #region Data Models

    public class GhostEstimateInput
    {
        public string VehicleInfo { get; set; } = "";
        public string DamageDescription { get; set; } = "";
        public List<string> ImpactZones { get; set; } = new();
        public string Severity { get; set; } = "moderate";
        public List<string> SelectedPanels { get; set; } = new(); // From damage zone selector

        /// <summary>
        /// Per-panel severity overrides. Key = panel name, Value = severity string (light/moderate/heavy/severe).
        /// When set, these override the blanket Severity for individual panels.
        /// </summary>
        public Dictionary<string, string> PanelSeverities { get; set; } = new();
    }

    public class GhostEstimateResult
    {
        public string VehicleInfo { get; set; } = "";
        public string VehicleType { get; set; } = "";
        public string DamageDescription { get; set; } = "";
        public DateTime GeneratedAt { get; set; }

        public List<GhostOperation> Operations { get; set; } = new();

        // Labor hours by type
        public decimal TotalBodyHours { get; set; }
        public decimal TotalRefinishHours { get; set; }
        public decimal TotalSubletAmount { get; set; }
        public int RefinishPanelCount { get; set; }
        public int BlendPanelCount { get; set; }

        // ACTUAL DOLLAR TOTALS (using typical labor rates)
        public decimal TotalBodyLaborDollars { get; set; }
        public decimal TotalRefinishLaborDollars { get; set; }
        public decimal TotalMechLaborDollars { get; set; }
        public decimal TotalFrameLaborDollars { get; set; }
        public decimal GrandTotalLaborDollars { get; set; }

        // Learned dollar totals from uploaded estimates
        public decimal? LearnedDollarTotal { get; set; }
        public decimal? MinDollarTotal { get; set; }
        public decimal? MaxDollarTotal { get; set; }

        public List<string> Notes { get; set; } = new();
    }

    public class GhostOperation
    {
        public string OperationType { get; set; } = "";
        public string PartName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string Section { get; set; } = "";
        public string Side { get; set; } = "";

        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }

        public double Confidence { get; set; } = 1.0;
        public string? Source { get; set; }

        // Learned data range fields
        public int SampleCount { get; set; }
        public decimal MinLaborHours { get; set; }
        public decimal MaxLaborHours { get; set; }
        public decimal MinRefinishHours { get; set; }
        public decimal MaxRefinishHours { get; set; }
        public decimal? LearnedDollarAmount { get; set; }
        public decimal? MinDollarAmount { get; set; }
        public decimal? MaxDollarAmount { get; set; }
        public string LaborSource { get; set; } = "fallback";
    }

    public class AffectedPanel
    {
        public string Name { get; set; } = "";
        public string ImpactZone { get; set; } = "";
        public string Severity { get; set; } = "moderate";
        public string Side { get; set; } = "";
        public bool RefinishOnly { get; set; }
        public string? ExplicitOperation { get; set; }
    }

    public class GhostComparisonResult
    {
        public GhostEstimateResult GhostEstimate { get; set; } = new();
        public List<GhostOperation> UserOperations { get; set; } = new();

        public List<MissingOperation> GhostFoundMissing { get; set; } = new();
        public List<ExtraOperation> UserFoundExtra { get; set; } = new();
        public List<GhostOperation> BothHave { get; set; } = new();

        // Enhanced comparison data
        public List<ValueDifference> ValueDifferences { get; set; } = new();
        public List<string> CoverageGaps { get; set; } = new();
        public Dictionary<string, int> CategoryMatchCounts { get; set; } = new();
        public Dictionary<string, int> CategoryMissingCounts { get; set; } = new();
        public decimal LaborDollarDifference { get; set; }

        public double MatchPercentage { get; set; }
        public string Summary { get; set; } = "";
    }

    public class ValueDifference
    {
        public GhostOperation GhostOp { get; set; } = new();
        public GhostOperation UserOp { get; set; } = new();
        public decimal LaborHoursDiff { get; set; }
        public decimal RefinishHoursDiff { get; set; }
        public decimal PriceDiff { get; set; }
    }

    public class MissingOperation
    {
        public GhostOperation Operation { get; set; } = new();
        public string Reason { get; set; } = "";
        public double Confidence { get; set; }
    }

    public class ExtraOperation
    {
        public GhostOperation Operation { get; set; } = new();
        public bool LearnFromThis { get; set; }
    }

    public class GuidanceOperation : GhostOperation
    {
        public string DataSource { get; set; } = "Database"; // "Database", "Knowledge Base", "Learned"
        public string Justification { get; set; } = "";
        public string PPageReference { get; set; } = "";
        public string DEGReference { get; set; } = "";
        public bool IsRequired { get; set; }
        public int LearnedFrequency { get; set; }
        public string ConfidenceLabel { get; set; } = "Medium"; // "High", "Medium", "Low"
        public string RangeDisplayText { get; set; } = "";
    }

    public class GuidanceEstimateResult : GhostEstimateResult
    {
        public List<GuidanceOperation> GuidanceOperations { get; set; } = new();

        // Source breakdown counts
        public int DatabaseCount { get; set; }
        public int KnowledgeBaseCount { get; set; }
        public int LearnedCount { get; set; }
        public int ExcelToolCount { get; set; }

        public List<string> Warnings { get; set; } = new();
        public List<string> ProTips { get; set; } = new();
    }

    #endregion

    public class LaborResolution
    {
        public decimal Hours { get; set; }
        public string Source { get; set; } = "fallback";
        public int SampleCount { get; set; }
        public decimal MinHours { get; set; }
        public decimal MaxHours { get; set; }
        public decimal MedianHours { get; set; }
        public double Confidence { get; set; } = 0.5;
        public decimal? LearnedDollarAmount { get; set; }
        public decimal? MinDollarAmount { get; set; }
        public decimal? MaxDollarAmount { get; set; }
        public bool HasLearnedData => Source != "fallback" && Source != "met_data";
    }

    #region Knowledge Base Data Models

    /// <summary>
    /// Data from IncludedNotIncluded.json - real estimate operations and MET times
    /// </summary>
    internal class GhostIncludedNotIncludedData
    {
        public string? Version { get; set; }
        public string? LastUpdated { get; set; }
        public GhostTerminologyData? Terminology { get; set; }
        public GhostCalculationsData? Calculations { get; set; }
        public List<GhostOperationDefinition>? Operations { get; set; }
    }

    internal class GhostTerminologyData
    {
        public Dictionary<string, string>? OperationTypes { get; set; }
        public Dictionary<string, string>? SideDesignations { get; set; }
        public Dictionary<string, string>? LaborTypes { get; set; }
        public Dictionary<string, string>? CommonAbbreviations { get; set; }
    }

    internal class GhostCalculationsData
    {
        public GhostBlendFormulasData? BlendFormulas { get; set; }
        public GhostOverlapDeductionsData? OverlapDeductions { get; set; }
        public GhostMaskingFormulasData? MaskingFormulas { get; set; }
        public GhostManualEntryOperationsData? ManualEntryOperations { get; set; }
        public GhostTriCoatAdditiveData? TriCoatAdditive { get; set; }
    }

    internal class GhostBlendFormulasData
    {
        public string? Description { get; set; }
        public GhostPanelFormula? LargePanels { get; set; }
        public GhostPanelFormula? StandardPanels { get; set; }
        public GhostPanelFormula? BumperCover { get; set; }
    }

    internal class GhostPanelFormula
    {
        public List<string>? Panels { get; set; }
        public string? FirstPanel { get; set; }
        public string? AdditionalPanels { get; set; }
        public string? Notes { get; set; }
    }

    internal class GhostOverlapDeductionsData
    {
        public string? Description { get; set; }
        public GhostOverlapDeduction? MajorAdjacentPanel { get; set; }
    }

    internal class GhostOverlapDeduction
    {
        public decimal Deduction { get; set; }
        public string? Unit { get; set; }
        public string? Notes { get; set; }
    }

    internal class GhostMaskingFormulasData
    {
        public string? Description { get; set; }
        public List<GhostMaskingOpening>? Openings { get; set; }
    }

    internal class GhostMaskingOpening
    {
        public string? Area { get; set; }
        public double Hours { get; set; }
        public string? TypicalCost { get; set; }
    }

    internal class GhostManualEntryOperationsData
    {
        public string? Description { get; set; }
        public string? Source { get; set; }
        public List<GhostManualOperation>? RefinishOperations { get; set; }
        public List<GhostManualOperation>? CorrosionOperations { get; set; }
        public List<GhostManualOperation>? AdditionalOperations { get; set; }
    }

    internal class GhostManualOperation
    {
        public string? Operation { get; set; }
        public string? TypicalHours { get; set; }
        public string? LaborType { get; set; }
        public string? When { get; set; }
    }

    internal class GhostTriCoatAdditiveData
    {
        public string? Description { get; set; }
        public string? Formula { get; set; }
        public string? Example { get; set; }
    }

    internal class GhostOperationDefinition
    {
        public string? Id { get; set; }
        public string? PartName { get; set; }
        public string? OperationType { get; set; }
        public string? AttachmentType { get; set; }
        public List<string>? Aliases { get; set; }
        public List<string>? Included { get; set; }
        public List<string>? NotIncluded { get; set; }
        public List<GhostMetOperation>? MetOperations { get; set; }
        public string? ScrsNotes { get; set; }
    }

    internal class GhostMetOperation
    {
        public string? Description { get; set; }
        public double Hours { get; set; }
        public string? LaborType { get; set; }
        public string? Source { get; set; }
        public string? MetRef { get; set; }
    }

    /// <summary>
    /// Data from PartOperationsFormulas.json - formulas for labor calculations
    /// </summary>
    internal class GhostPartOperationsFormulas
    {
        public string? Version { get; set; }
        public string? Source { get; set; }
        public Dictionary<string, GhostPartTypeInfo>? PartTypes { get; set; }
        public Dictionary<string, GhostOperationCategoryInfo>? OperationCategories { get; set; }
    }

    internal class GhostPartTypeInfo
    {
        public string? Name { get; set; }
        public List<string>? Aliases { get; set; }
        public bool RequiresAdhesionPromoter { get; set; }
        public bool RequiresFlexAdditive { get; set; }
        public bool RequiresCorrosionProtection { get; set; }
        public bool RequiresWeldThroughPrimer { get; set; }
        public bool RequiresSeamSealer { get; set; }
    }

    internal class GhostOperationCategoryInfo
    {
        public string? Name { get; set; }
        public List<GhostFormulaOperation>? Operations { get; set; }
    }

    internal class GhostFormulaOperation
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public List<string>? AppliesTo { get; set; }
        public GhostFormulaDefinition? Formula { get; set; }
    }

    internal class GhostFormulaDefinition
    {
        public string? Type { get; set; }
        public string? Condition { get; set; }
        public double Value { get; set; }
        public string? BaseOn { get; set; }
        public double Percentage { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double ValuePerItem { get; set; }
        public string? Notes { get; set; }
    }

    #endregion
}
