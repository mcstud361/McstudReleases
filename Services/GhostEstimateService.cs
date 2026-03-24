#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
    public partial class GhostEstimateService
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
        private readonly List<string> _missingDataWarnings = new();

        // Major panels that should never be added automatically by mining — only sub-components are OK
        private static readonly HashSet<string> MajorPanels = new(StringComparer.OrdinalIgnoreCase)
        {
            "front bumper cover", "rear bumper cover", "hood", "fender", "front door",
            "rear door", "quarter panel", "trunk lid", "roof", "rocker panel",
            "a-pillar", "b-pillar", "c-pillar", "liftgate", "tailgate", "decklid"
        };

        // Operations that are hardware, parts, logistics — never belong on an estimate
        private static readonly string[] JunkOperationKeywords = new[]
        {
            "bolt", "nut ", "nut\t", "clip ", "grommet", "rivet", "screw", "fastener",
            "adhesive", "genuine adhesive", "toyota bond", "wurth", "inventory",
            "tow to", "tow from", "dealer", "dealership",
            "control arm", "cntrl arm", "trailing arm", "seat nut",
            "touch up painted bolt", "monitor flash",
            "filler", "lower grille retainer", "molding clip",
            "pre wash", "prewash",
            "felt strip", "emblem"
        };

        // Must-have operations are now read from GhostConfigService (single source of truth)

        // Operations that must ONLY appear in MISCELLANEOUS OPERATIONS (handled by AddMustHaveOperations).
        // Any per-panel pipeline that tries to add these should skip them to prevent duplication.
        private static readonly HashSet<string> MiscOnlyOperations = new(StringComparer.OrdinalIgnoreCase)
        {
            "cover car", "cover vehicle", "cover for edging", "mask for buffing",
            "cover engine compartment", "cover interior", "clean and cover car",
            "color tint", "spray out card", "clean for delivery", "glass cleaner",
            "parts disposal", "hazardous waste", "misc hardware", "steering wheel cover",
            "seat cover", "floor mat", "refinish material invoice",
            "mask and protect removed", "cover interior and jambs for repairs",
            "cover interior and jambs for refinish",
            "weld-through primer", "weld through primer"
        };

        private static bool IsMiscOnlyOperation(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return false;
            var lower = description.ToLowerInvariant();
            return MiscOnlyOperations.Any(kw => lower.Contains(kw));
        }

        private bool IsMustHaveDescription(string descLower)
        {
            return _ghostConfig.GetMustHaves()
                .Where(m => m.Enabled)
                .Any(m => descLower.Contains(m.Description.ToLowerInvariant()));
        }

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
                else
                {
                    _missingDataWarnings.Add("IncludedNotIncluded.json not found — MET labor times and operation data unavailable. Ghost estimates will use fallback times.");
                    System.Diagnostics.Debug.WriteLine("[Ghost] WARNING: IncludedNotIncluded.json not found");
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
                else
                {
                    _missingDataWarnings.Add("PartOperationsFormulas.json not found — blend formulas and labor calculations will use defaults.");
                    System.Diagnostics.Debug.WriteLine("[Ghost] WARNING: PartOperationsFormulas.json not found");
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

            // Surface any missing data warnings so user knows why features may be limited
            foreach (var warning in _missingDataWarnings)
                result.Notes.Add(warning);

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
            FilterGhostOperations(result);

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
                        Section = !string.IsNullOrWhiteSpace(op.Section) ? op.Section : MapToCCCSection(op.PartName, op.Category),
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
                        // Skip misc-only ops — they belong in MISCELLANEOUS via AddMustHaveOperations
                        if (IsMiscOnlyOperation(rec.Name)) continue;

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
                        // Sanity check: skip learned operations with absurd hours (likely mislearned prices)
                        if (sug.LaborHours > 5.0m && sug.OperationType != "Replace" && sug.OperationType != "Repl")
                            continue;

                        // Skip misc-only ops — they belong in MISCELLANEOUS via AddMustHaveOperations
                        if (IsMiscOnlyOperation(sug.Description)) continue;

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

            // Add must-have operations that aren't already present
            AddMustHaveOperations(result);

            // Final filter: remove operations whose section maps to a panel the user didn't request.
            // Allow global sections (diagnostics, restraints, misc) to pass through.
            var globalSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "MISCELLANEOUS OPERATIONS", "VEHICLE DIAGNOSTICS", "RESTRAINT SYSTEMS",
                "ELECTRICAL", "MECHANICAL", "Custom Operations"
            };
            var userPanelSections = new HashSet<string>(
                affectedPanels.Select(p => MapToCCCSection(p.Name, "")),
                StringComparer.OrdinalIgnoreCase);
            // Also add blend-adjacent sections (one hop away)
            foreach (var panel in affectedPanels)
            {
                var adjacent = GetAdjacentPanels(panel.Name);
                foreach (var adj in adjacent)
                    userPanelSections.Add(MapToCCCSection(adj, ""));
            }

            result.GuidanceOperations = result.GuidanceOperations
                .Where(op =>
                {
                    // Filter by section relevance
                    if (!string.IsNullOrWhiteSpace(op.Section) &&
                        !globalSections.Contains(op.Section) &&
                        !userPanelSections.Contains(op.Section))
                        return false;

                    // Filter out hardware, parts, logistics, and consumable junk
                    var descLower = (op.Description ?? "").ToLowerInvariant();
                    if (JunkOperationKeywords.Any(kw => descLower.Contains(kw)))
                        return false;

                    // Filter operations with part numbers (7+ digits = OEM part number)
                    if (Regex.IsMatch(descLower, @"\d{7,}"))
                        return false;

                    // Filter truncated junk, raw estimate data, all-caps junk
                    if (descLower.Contains(" rmc ") || descLower.Contains(" rmc$") || descLower.EndsWith(" rmc"))
                        return false;
                    if (descLower.Contains("two tone calculation") || descLower.Contains("clear coat and two tone"))
                        return false;
                    if (descLower.Contains("feather edge & block"))
                        return false;
                    if (Regex.IsMatch(descLower, @"^\d{2,3}\s+\d{5,}"))
                        return false;
                    if (descLower.Trim() == "buff only")
                        return false;
                    if (Regex.IsMatch(descLower, @"\$\s*$"))
                        return false;

                    // Misc-only ops must only appear in MISCELLANEOUS
                    if (IsMiscOnlyOperation(descLower) && op.Section != "MISCELLANEOUS OPERATIONS")
                        return false;

                    // Scans belong in VEHICLE DIAGNOSTICS only
                    if ((descLower.Contains("pre-repair scan") || descLower.Contains("post-repair scan") ||
                         descLower.Contains("pre-scan") || descLower.Contains("post-scan")) &&
                        op.Section != "VEHICLE DIAGNOSTICS")
                        return false;

                    // Strip actual parts/R&I lines from MISCELLANEOUS — they don't belong there
                    if (op.Section == "MISCELLANEOUS OPERATIONS")
                    {
                        var d = descLower;
                        if ((d.Contains("r&i") || d.Contains("remove and install")) && !IsMustHaveDescription(d))
                            return false;
                        if (MajorPanels.Any(p => d.Contains(p)))
                            return false;
                        if (d.Contains("clear coat application") || d.Contains("three stage paint") || d.Contains("two tone"))
                            return false;
                        if (d.Contains("side marker") || d.Contains("fog lamp") || d.Contains("parking sensor"))
                            return false;
                        if (d.Contains("door trim panel") || d.Contains("door handle") || d.Contains("door shell"))
                            return false;
                        if (d.Contains("upper molding") || d.Contains("lower molding"))
                            return false;
                        if (d.Contains("stage and secure") && !IsMustHaveDescription(d))
                            return false;
                        if (d.Contains("cover and protect electrical") || d.Contains("cover vehicle"))
                            return false;
                    }

                    return true;
                })
                .ToList();

            // Final dedup pass — catch R&I prefix/suffix swaps and near-duplicate descriptions
            var dedupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            result.GuidanceOperations = result.GuidanceOperations
                .Where(op =>
                {
                    var key = NormalizeDescriptionForDedup(op.Description, op.Section);
                    return dedupKeys.Add(key);
                })
                .ToList();

            // Copy operations list for backward compat
            result.Operations = result.GuidanceOperations.Cast<GhostOperation>().ToList();

            return result;
        }

        /// <summary>
        /// Add must-have operations from the shop's standard list.
        /// These always appear on every ghost estimate with correct hours/prices.
        /// Skips any that are already present (by normalized description match).
        /// </summary>
        private void AddMustHaveOperations(GuidanceEstimateResult result)
        {
            var existingDescs = new HashSet<string>(
                result.GuidanceOperations.Select(o => (o.Description ?? "").ToLowerInvariant().Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var mh in _ghostConfig.GetMustHaves().Where(m => m.Enabled))
            {
                // Skip if already present (fuzzy match on description)
                if (existingDescs.Any(d => d.Contains(mh.Description.ToLowerInvariant())))
                    continue;

                result.GuidanceOperations.Add(new GuidanceOperation
                {
                    OperationType = mh.OpType,
                    PartName = mh.Description.ToLower(),
                    Description = mh.Description,
                    Category = mh.Category,
                    Section = mh.Section,
                    LaborHours = mh.ExpectedHours,
                    RefinishHours = mh.RefinishHours,
                    Price = mh.ExpectedPrice,
                    Confidence = 1.0,
                    Source = "Shop standard operations",
                    DataSource = "Database",
                    ConfidenceLabel = "High",
                    IsRequired = true
                });
            }
        }

        /// <summary>
        /// Normalize a description for dedup — strips R&amp;I prefix/suffix, side codes, articles.
        /// </summary>
        private static string NormalizeDescriptionForDedup(string? description, string? section)
        {
            if (string.IsNullOrWhiteSpace(description)) return "";
            var d = description.ToLowerInvariant().Trim();

            // Strip side prefixes
            d = System.Text.RegularExpressions.Regex.Replace(d, @"^(lt |rt |left |right )", "");

            // Normalize R&I variants — treat "R&I Door Trim Panel" same as "Door Trim Panel R&I"
            d = System.Text.RegularExpressions.Regex.Replace(d, @"^r&i\s+", "");
            d = System.Text.RegularExpressions.Regex.Replace(d, @"\s+r&i$", "");
            d = System.Text.RegularExpressions.Regex.Replace(d, @"^r ?& ?i\s+", "");
            d = System.Text.RegularExpressions.Regex.Replace(d, @"\s+r ?& ?i$", "");
            d = System.Text.RegularExpressions.Regex.Replace(d, @"^remove and install\s+", "");
            d = System.Text.RegularExpressions.Regex.Replace(d, @"\s+remove and install$", "");

            // Normalize "stage and secure for refinish" variations
            d = System.Text.RegularExpressions.Regex.Replace(d, @"stage and secure.*$", "stage and secure");

            // Remove articles
            d = d.Replace("the ", "").Replace("a ", "");

            // Combine with section to scope dedup per section
            return $"{section?.ToLower() ?? ""}|{d.Trim()}";
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

            // SRS only for structural/airbag-deployment panels — not simple door repairs
            var hasAirbagComponents = panelNames.Any(p =>
                p.Contains("pillar") || p.Contains("dash") || p.Contains("steering") ||
                p.Contains("seat") || p.Contains("roof"));

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
                ("SOP Operations", hasWeldedPanels || hasRefinish, "SOP Operations"), // SOPs when refinish or welded work
            };

            foreach (var (sheetName, isRelevant, displayCategory) in sheetConfig)
            {
                if (!isRelevant) continue;
                if (!_ghostConfig.IsCategoryEnabled(displayCategory)) continue;

                var sheetOps = _excelProvider.GetSheetOperations(sheetName);
                foreach (var excelOp in sheetOps)
                {
                    if (string.IsNullOrWhiteSpace(excelOp.Description)) continue;
                    // Skip raw tab-delimited data rows (unparsed summary rows from Excel)
                    if (excelOp.Description.Contains('\t') || excelOp.Description.Contains('\n'))
                        continue;
                    // Skip Excel UI junk: navigation links, stat bars, emoji rows
                    if (excelOp.Description.Contains("\U0001f517") || excelOp.Description.Contains("\U0001f4ca") ||
                        excelOp.Description.Contains("\U0001f4b2") || excelOp.Description.Contains("\U0001f6e0") ||
                        excelOp.Description.Contains("\U0001f3a8") ||
                        excelOp.Description.StartsWith("Back to top", StringComparison.OrdinalIgnoreCase))
                        continue;
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

            // Include user's custom operations
            foreach (var customOp in _ghostConfig.GetCustomOperations())
            {
                if (!customOp.Enabled || string.IsNullOrWhiteSpace(customOp.Description)) continue;

                var normalizedKey = NormalizeOperationKey(customOp.Description.ToLower(), customOp.OperationType);
                if (!seenOperations.Add(normalizedKey)) continue;

                result.GuidanceOperations.Add(new GuidanceOperation
                {
                    OperationType = customOp.OperationType,
                    PartName = customOp.Description.ToLower(),
                    Description = customOp.Description,
                    Category = "Custom",
                    Section = "Custom Operations",
                    LaborHours = customOp.LaborHours,
                    RefinishHours = customOp.RefinishHours,
                    Price = customOp.Price,
                    Confidence = 0.90,
                    Source = "Your Custom Operation",
                    DataSource = "Custom",
                    ConfidenceLabel = "High",
                    IsRequired = false,
                    Justification = "Custom operation added by user"
                });
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

            // Two-pass approach: detect unambiguous panels first, then resolve ambiguous ones using context
            var ambiguousPanels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bumper" };
            var deferredMentions = new List<(string panel, string line)>();

            // Pass 1: detect all non-ambiguous panels
            foreach (var line in lines)
            {
                foreach (var panel in directPanels)
                {
                    if (!line.Contains(panel))
                        continue;

                    if (ambiguousPanels.Contains(panel) && !line.Contains("front") && !line.Contains("rear"))
                    {
                        // Defer ambiguous panels for pass 2
                        deferredMentions.Add((panel, line));
                        continue;
                    }

                    AddPanelFromLine(panel, line, panels, addedPanelKeys, refinishOnlyKeywords, replaceKeywords, riKeywords);
                }
            }

            // Pass 2: resolve ambiguous panels using already-detected siblings as context
            foreach (var (panel, line) in deferredMentions)
            {
                AddPanelFromLine(panel, line, panels, addedPanelKeys, refinishOnlyKeywords, replaceKeywords, riKeywords, siblingPanels: panels);
            }

            return panels;
        }

        private void AddPanelFromLine(string panel, string line, List<AffectedPanel> panels,
            HashSet<string> addedPanelKeys, string[] refinishOnlyKeywords, string[] replaceKeywords, string[] riKeywords,
            List<AffectedPanel>? siblingPanels = null)
        {
            var fullName = GetFullPanelName(panel, line, siblingPanels);

            var lineSide = DetectSideFromDescription(line);
            var dedupKey = $"{fullName}|{lineSide}";

            if (!addedPanelKeys.Add(dedupKey))
                return;

            if (panels.Any(p => p.Name == fullName && p.Side == lineSide))
                return;

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

        private string GetFullPanelName(string shortName, string context, List<AffectedPanel>? siblingPanels = null)
        {
            // Add position context — context should be the individual line, not full description
            var isRear = context.Contains("rear");
            var isFront = context.Contains("front");

            return shortName switch
            {
                "bumper" => InferBumperPosition(context, siblingPanels),
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

        private string InferBumperPosition(string lineContext, List<AffectedPanel>? siblings)
        {
            if (lineContext.Contains("rear")) return "rear bumper cover";
            if (lineContext.Contains("front")) return "front bumper cover";

            // Infer from sibling panels already detected
            if (siblings != null && siblings.Count > 0)
            {
                var rearIndicators = new[] { "trunk", "quarter", "tail light", "rear door", "decklid", "liftgate" };
                var frontIndicators = new[] { "hood", "fender", "grille", "headlight", "radiator", "front door" };

                bool hasRear = siblings.Any(p => rearIndicators.Any(r => p.Name.Contains(r, StringComparison.OrdinalIgnoreCase)));
                bool hasFront = siblings.Any(p => frontIndicators.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)));

                if (hasRear && !hasFront) return "rear bumper cover";
            }

            return "front bumper cover"; // default
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

            // Check for a learned operation profile for this panel + operation
            var profileKey = $"{(_knowledgeBase.ResolveAlias(panel.Name.ToLower()) ?? panel.Name.ToLower())}|{primaryOp.ToLower()}";
            var profile = _knowledgeBase.GetOperationProfile(profileKey);

            // Use learned description for primary operation
            var primaryDescription = ResolveLearnedDescription(panel.Name, primaryOp, sideCode);
            var primaryCategory = ResolveLearnedLaborType(panel.Name, primaryOp);

            // Primary operation - format like real CCC estimate line
            operations.Add(new GhostOperation
            {
                OperationType = opCode,
                PartName = panel.Name,
                Description = primaryDescription,
                Category = primaryCategory,
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

            // If a learned profile exists with enough data, use it for sub-operations
            if (profile != null && profile.ExampleCount >= 3)
            {
                var qualifiedSubs = profile.SubOperations
                    .Where(s => s.AppearanceRate >= 0.6 && !string.IsNullOrEmpty(s.PartName))
                    .OrderByDescending(s => s.AppearanceRate)
                    .ToList();

                if (qualifiedSubs.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Ghost] Using learned profile for {profileKey}: {qualifiedSubs.Count} sub-operations from {profile.ExampleCount} examples");

                    foreach (var sub in qualifiedSubs)
                    {
                        var subDescription = !string.IsNullOrWhiteSpace(sub.Description)
                            ? sub.Description
                            : ResolveLearnedDescription(sub.PartName!, sub.OperationType ?? "", sideCode);

                        // Skip misc-only ops — they belong in MISCELLANEOUS via AddMustHaveOperations
                        if (IsMiscOnlyOperation(subDescription)) continue;

                        var subOpCode = (sub.OperationType ?? "").ToLower() switch
                        {
                            "replace" => "Repl",
                            "repair" => "Rpr",
                            "r&i" => "R&I",
                            "refinish" or "rfn" => "Rfn",
                            "blend" => "Blend",
                            _ => sub.OperationType ?? ""
                        };

                        var subCategory = !string.IsNullOrWhiteSpace(sub.LaborType)
                            ? sub.LaborType
                            : DetermineCategory(sub.PartName!);

                        var subResolution = ResolveLaborTime(sub.PartName!, sub.OperationType ?? "", vehicleType);
                        var subHours = subResolution.HasLearnedData ? subResolution.Hours : sub.AverageHours;

                        var isRefinishOp = subOpCode == "Rfn" || subOpCode == "Blend";

                        operations.Add(new GhostOperation
                        {
                            OperationType = subOpCode,
                            PartName = sub.PartName!,
                            Description = subDescription,
                            Category = subCategory,
                            LaborHours = isRefinishOp ? 0m : subHours,
                            RefinishHours = isRefinishOp ? subHours : 0m,
                            MinLaborHours = subResolution.HasLearnedData ? subResolution.MinHours : subHours,
                            MaxLaborHours = subResolution.HasLearnedData ? subResolution.MaxHours : subHours,
                            SampleCount = subResolution.SampleCount,
                            LaborSource = subResolution.Source,
                            Confidence = Math.Min(0.95, sub.AppearanceRate),
                            Source = $"Learned profile ({sub.AppearanceRate:P0} appearance in {profile.ExampleCount} estimates)",
                            Side = panel.Side
                        });
                    }

                    return operations;
                }
            }

            // Fallback: existing hardcoded R&I logic when no learned profile
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
                        Description = ResolveLearnedDescription(riPart, "R&I", sideCode),
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
            var opCounts = new Dictionary<string, (string PartName, string OpType, string Description, string LaborType, decimal AvgLabor, decimal AvgRefinish, decimal AvgPrice, int Count)>();

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
                            !string.IsNullOrWhiteSpace(li.Description) ? li.Description : existing.Description,
                            !string.IsNullOrWhiteSpace(li.LaborType) ? li.LaborType : existing.LaborType,
                            (existing.AvgLabor * existing.Count + li.LaborHours) / newCount,
                            (existing.AvgRefinish * existing.Count + li.RefinishHours) / newCount,
                            (existing.AvgPrice * existing.Count + li.Price) / newCount,
                            newCount
                        );
                    }
                    else
                    {
                        opCounts[opKey] = (li.PartName, li.OperationType, li.Description ?? "", li.LaborType ?? "", li.LaborHours, li.RefinishHours, li.Price, 1);
                    }
                }
            }

            // Add operations that appear in >= 50% of similar estimates
            var threshold = Math.Max(2, similarEstimates.Count / 2);
            var addedCount = 0;

            foreach (var (opKey, data) in opCounts.Where(kv => kv.Value.Count >= threshold).OrderByDescending(kv => kv.Value.Count))
            {
                // Don't add new major panels from similar estimates
                if (MajorPanels.Contains(data.PartName) &&
                    !panels.Any(p => p.Name.Equals(data.PartName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var confidence = (double)data.Count / similarEstimates.Count;

                // Use actual description from stored estimates, fall back to learned or generic
                var desc = !string.IsNullOrWhiteSpace(data.Description)
                    ? data.Description
                    : ResolveLearnedDescription(data.PartName, data.OpType);

                // Skip misc-only ops — they belong in MISCELLANEOUS via AddMustHaveOperations
                if (IsMiscOnlyOperation(desc)) continue;

                var category = !string.IsNullOrWhiteSpace(data.LaborType)
                    ? data.LaborType
                    : ResolveLearnedLaborType(data.PartName, data.OpType);

                result.Operations.Add(new GhostOperation
                {
                    OperationType = data.OpType,
                    PartName = data.PartName,
                    Description = desc,
                    Category = category,
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

                // Determine which CCC section the parent panel belongs to
                var parentSection = MapToCCCSection(panel.Name, "");
                // Build set of all sections the user's panels map to
                var userSections = new HashSet<string>(
                    panels.Select(p => MapToCCCSection(p.Name, "")),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var assoc in coOccurrences)
                {
                    var opKey = $"{assoc.AssociatedPart}|{assoc.AssociatedOperation}";
                    if (existingOps.Contains(opKey))
                        continue; // Already in the estimate

                    // Don't add entirely new major panels — only sub-components of existing panels
                    if (MajorPanels.Contains(assoc.AssociatedPart) &&
                        !panels.Any(p => p.Name.Equals(assoc.AssociatedPart, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Skip R&I parts — those are handled by GetRAndIParts
                    if (assoc.AssociatedOperation.Equals("r&i", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Filter out co-occurrence ops that map to a completely different panel section.
                    // E.g., "bumper cover grommet" defaulting to FRONT BUMPER when parent is rear bumper.
                    var opSection = MapToCCCSection(assoc.AssociatedPart, "");
                    if (!userSections.Contains(opSection) && opSection != parentSection &&
                        opSection != "MISCELLANEOUS OPERATIONS" && opSection != "VEHICLE DIAGNOSTICS")
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

                    var resolvedDesc = ResolveLearnedDescription(assoc.AssociatedPart, assoc.AssociatedOperation, sideCode);

                    // Skip misc-only ops — they belong in MISCELLANEOUS via AddMustHaveOperations
                    if (IsMiscOnlyOperation(resolvedDesc)) continue;

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
                        Description = resolvedDesc,
                        Category = ResolveLearnedLaborType(assoc.AssociatedPart, assoc.AssociatedOperation),
                        // Use parent panel's section so ops stay in the correct CCC section
                        Section = parentSection,
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
            var engine = OperationRulesEngine.Instance;

            // Build estimate context from the engine for structural/ADAS checks
            var opTuples = result.Operations
                .Where(o => !string.IsNullOrWhiteSpace(o.PartName))
                .Select(o => (o.PartName, o.OperationType))
                .ToList();
            var estimateContext = engine.AnalyzeRulesEstimateContext(opTuples);

            var hasStructural = estimateContext.HasStructural;

            // ADAS calibration only for components with forward-facing sensors
            var hasADASComponents = result.Operations.Any(o =>
                o.PartName.Contains("windshield") ||
                o.PartName.Contains("front bumper") ||
                o.PartName.Contains("grille") ||
                o.PartName.Contains("radar"));

            // Scans and diagnostics are now handled by MustHaveOperations

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

            // Add material-aware operations via OperationRulesEngine
            // This replaces the old hardcoded hasWeldedPanels/hasPlasticParts blocks
            // and fixes the Cover Car bug (now triggers for ANY paint work, not just welded panels)
            var addedOpNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Pre-populate with operations already on the estimate to avoid duplicates
            foreach (var existingOp in result.Operations)
            {
                if (!string.IsNullOrWhiteSpace(existingOp.PartName))
                    addedOpNames.Add(existingOp.PartName);
            }

            foreach (var op in result.Operations.ToList())
            {
                if (string.IsNullOrWhiteSpace(op.PartName)) continue;

                var suggestions = engine.GetSuggestedOperations(op.PartName, op.OperationType);
                foreach (var suggestion in suggestions)
                {
                    // Dedup: skip if this operation name was already added
                    if (!addedOpNames.Add(suggestion.Name)) continue;

                    // Skip misc-only ops — they belong in MISCELLANEOUS via AddMustHaveOperations
                    if (IsMiscOnlyOperation(suggestion.Description)) continue;

                    // Use learned data first, fall back to engine defaults
                    var isPaintOp = suggestion.OperationType == "Paint";
                    var resolved = ResolveLaborTime(suggestion.Name, suggestion.OperationType, result.VehicleType);
                    var hours = resolved.HasLearnedData ? resolved.Hours : suggestion.DefaultHours;

                    result.Operations.Add(new GhostOperation
                    {
                        OperationType = suggestion.OperationType,
                        PartName = suggestion.Name,
                        Description = suggestion.Description,
                        Category = suggestion.Category,
                        LaborHours = isPaintOp ? 0m : hours,
                        RefinishHours = isPaintOp ? hours : 0m,
                        MinLaborHours = resolved.HasLearnedData ? resolved.MinHours : (isPaintOp ? 0m : hours),
                        MaxLaborHours = resolved.HasLearnedData ? resolved.MaxHours : (isPaintOp ? 0m : hours),
                        SampleCount = resolved.SampleCount,
                        LaborSource = resolved.Source,
                        Confidence = resolved.HasLearnedData ? resolved.Confidence : 0.90,
                        Source = resolved.HasLearnedData
                            ? $"Learned from {resolved.SampleCount} estimates"
                            : suggestion.Source
                    });
                }
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
                // Determine the primary operation type for this panel (Replace or Repair) for operation-type-aware refinish
                var panelPrimaryOp = result.Operations
                    .FirstOrDefault(o => o.PartName?.Equals(panel, StringComparison.OrdinalIgnoreCase) == true &&
                                         (o.OperationType == "Repl" || o.OperationType == "Replace" || o.OperationType == "Rpr" || o.OperationType == "Repair"))
                    ?.OperationType;
                var rfnOpType = panelPrimaryOp switch
                {
                    "Repl" or "Replace" => "replace",
                    "Rpr" or "Repair" => "repair",
                    _ => null
                };
                var rfnResolution = ResolveRefinishTime(panel, rfnOpType);

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

            // Per-panel refinish operations (Buff, DE-NIB, Feather Edge) using actual hours
            {
                var engine = OperationRulesEngine.Instance;
                var addedRefinishOps = new Dictionary<string, GhostOperation>(StringComparer.OrdinalIgnoreCase);
                bool isFirstPanel = true;

                foreach (var panel in panelsNeedingRefinish)
                {
                    // Get this panel's actual refinish and repair hours from already-generated operations
                    var panelOps = result.Operations.Where(o =>
                        o.PartName.Equals(panel, StringComparison.OrdinalIgnoreCase)).ToList();
                    var panelRefinishHours = panelOps
                        .Where(o => o.OperationType == "Rfn" || o.OperationType == "Refinish" || o.OperationType == "Blend")
                        .Sum(o => o.RefinishHours);
                    var panelRepairHours = panelOps
                        .Where(o => o.OperationType == "Repair")
                        .Sum(o => o.LaborHours);
                    var panelOpType = panelOps.FirstOrDefault()?.OperationType ?? "Replace";

                    var refinishSuggestions = engine.GetRefinishSuggestedOperations(
                        panel, panelOpType, panelRefinishHours, panelRepairHours, isFirstPanel);

                    foreach (var suggestion in refinishSuggestions)
                    {
                        var key = suggestion.Name;

                        // Learned data check: if learned data exists for this op, use it instead
                        var resolved = ResolveLaborTime(key, "Paint", result.VehicleType);
                        if (resolved.HasLearnedData)
                        {
                            // Only add once with learned hours (not per-panel)
                            if (addedRefinishOps.ContainsKey(key)) continue;
                            var op = new GhostOperation
                            {
                                OperationType = "Paint",
                                PartName = key,
                                Description = suggestion.Description,
                                Category = "Refinish Operations",
                                RefinishHours = resolved.Hours,
                                MinRefinishHours = resolved.MinHours,
                                MaxRefinishHours = resolved.MaxHours,
                                SampleCount = resolved.SampleCount,
                                LaborSource = resolved.Source,
                                Confidence = 0.90,
                                Source = $"Learned from {resolved.SampleCount} estimates"
                            };
                            addedRefinishOps[key] = op;
                            result.Operations.Add(op);
                        }
                        else
                        {
                            // Use calculated percentage hours — accumulate per panel
                            if (addedRefinishOps.TryGetValue(key, out var existing))
                            {
                                existing.RefinishHours += suggestion.DefaultHours;
                                existing.MinRefinishHours += suggestion.DefaultHours;
                                existing.MaxRefinishHours += suggestion.DefaultHours;
                            }
                            else
                            {
                                var op = new GhostOperation
                                {
                                    OperationType = "Paint",
                                    PartName = key,
                                    Description = suggestion.Description,
                                    Category = "Refinish Operations",
                                    RefinishHours = suggestion.DefaultHours,
                                    MinRefinishHours = suggestion.DefaultHours,
                                    MaxRefinishHours = suggestion.DefaultHours,
                                    SampleCount = 0,
                                    LaborSource = "fallback",
                                    Confidence = 0.85,
                                    Source = suggestion.Source
                                };
                                addedRefinishOps[key] = op;
                                result.Operations.Add(op);
                            }
                        }
                    }
                    isFirstPanel = false;
                }
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
            var canonicalName = _knowledgeBase.ResolveAlias(partLower) ?? partLower;

            // PRIORITY 1: Learned data with 3+ samples (high confidence, outlier-filtered)
            var learnedStats = _knowledgeBase.GetOperationStats(canonicalName, operationType);

            if (learnedStats != null && learnedStats.SampleCount >= 3 && learnedStats.MeanLaborHours > 0)
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
                    Confidence = 0.95,
                    LearnedDollarAmount = learnedStats.PriceValues.Count > 0 ? (decimal?)learnedStats.MeanPrice : null,
                    MinDollarAmount = learnedStats.PriceValues.Count > 0 ? (decimal?)learnedStats.MinPrice : null,
                    MaxDollarAmount = learnedStats.PriceValues.Count > 0 ? (decimal?)learnedStats.MaxPrice : null,
                    LearnedDescription = learnedStats.MostCommonDescription,
                    LearnedLaborType = learnedStats.MostCommonLaborType
                };
            }

            // PRIORITY 2: Excel estimating tool (curated real data from user's tool)
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
                    Confidence = 0.85
                };
            }

            // PRIORITY 3: Learned data with 1-2 samples (lower confidence, blend with other sources)
            if (learnedStats != null && learnedStats.SampleCount >= 1 && learnedStats.MeanLaborHours > 0)
            {
                var hours = learnedStats.MedianLaborHours > 0 ? learnedStats.MedianLaborHours : learnedStats.MeanLaborHours;

                // For 1-2 samples, try to blend with other data sources for reliability
                if (learnedStats.SampleCount <= 2)
                {
                    decimal? blendHours = null;
                    int blendSamples = 0;

                    var blendPatterns = _learningService.SearchPatterns(partName, 5);
                    var blendMatchPattern = blendPatterns.FirstOrDefault(p =>
                        p.PartName.Equals(partName, StringComparison.OrdinalIgnoreCase) &&
                        p.OperationType.Equals(operationType, StringComparison.OrdinalIgnoreCase));
                    if (blendMatchPattern?.Operations != null)
                    {
                        var patternOps = blendMatchPattern.Operations.Where(o => o.LaborHours > 0).ToList();
                        if (patternOps.Any())
                        {
                            blendHours = patternOps.Average(o => o.LaborHours);
                            blendSamples = blendMatchPattern.ExampleCount;
                        }
                    }

                    if (blendHours == null)
                    {
                        var blendHistEstimates = _historyDb.GetAllEstimates();
                        var matchingLines = blendHistEstimates
                            .SelectMany(e => e.LineItems)
                            .Where(li => li.LaborHours > 0 &&
                                         !string.IsNullOrEmpty(li.PartName) &&
                                         li.PartName.ToLower().Contains(partLower) &&
                                         li.OperationType.Equals(operationType, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        if (matchingLines.Count >= 1)
                        {
                            blendHours = matchingLines.Average(li => li.LaborHours);
                            blendSamples = matchingLines.Count;
                        }
                    }

                    if (blendHours.HasValue && blendSamples > 0)
                    {
                        var learnedWeight = Math.Min(0.5m, 1.0m / (1 + blendSamples));
                        var histWeight = 1.0m - learnedWeight;
                        var blended = (hours * learnedWeight) + (blendHours.Value * histWeight);
                        System.Diagnostics.Debug.WriteLine($"[Ghost] Weighted blend for {partName} {operationType}: learned={hours}h({learnedStats.SampleCount}) + hist={blendHours.Value}h({blendSamples}) → {blended:F2}h");
                        hours = blended;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[Ghost] LEARNED data for {partName} {operationType}: {hours}h (from {learnedStats.SampleCount} estimates)");
                return new LaborResolution
                {
                    Hours = hours,
                    Source = "learned",
                    SampleCount = learnedStats.SampleCount,
                    MinHours = learnedStats.MinLaborHours,
                    MaxHours = learnedStats.MaxLaborHours,
                    MedianHours = learnedStats.MedianLaborHours,
                    Confidence = learnedStats.SampleCount == 2 ? 0.80 : 0.60,
                    LearnedDollarAmount = learnedStats.PriceValues.Count > 0 ? (decimal?)learnedStats.MeanPrice : null,
                    MinDollarAmount = learnedStats.PriceValues.Count > 0 ? (decimal?)learnedStats.MinPrice : null,
                    MaxDollarAmount = learnedStats.PriceValues.Count > 0 ? (decimal?)learnedStats.MaxPrice : null,
                    LearnedDescription = learnedStats.MostCommonDescription,
                    LearnedLaborType = learnedStats.MostCommonLaborType
                };
            }

            // PRIORITY 4: Vehicle-specific learned data (truck vs SUV vs car)
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

            // PRIORITY 5: Learned patterns from EstimateLearningService
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

            // PRIORITY 6: Estimate history database — average across all stored estimates
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

        private LaborResolution ResolveRefinishTime(string partName, string? operationType = null)
        {
            var partLower = partName.ToLower();

            // PRIORITY 1: Real learned refinish data from uploaded estimates
            var canonicalName = _knowledgeBase.ResolveAlias(partLower) ?? partLower;

            // If a specific operation type is provided, check it first for operation-type-aware refinish
            if (!string.IsNullOrEmpty(operationType))
            {
                var specificStats = _knowledgeBase.GetOperationStats(canonicalName, operationType);
                if (specificStats != null && specificStats.RefinishHoursValues.Count >= 1 && specificStats.MeanRefinishHours > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Ghost] LEARNED refinish for {partName} ({operationType}): {specificStats.MeanRefinishHours}h (from {specificStats.RefinishHoursValues.Count} samples)");
                    return new LaborResolution
                    {
                        Hours = specificStats.MeanRefinishHours,
                        Source = "learned",
                        SampleCount = specificStats.RefinishHoursValues.Count,
                        MinHours = specificStats.MinRefinishHours,
                        MaxHours = specificStats.MaxRefinishHours,
                        MedianHours = specificStats.MeanRefinishHours,
                        Confidence = specificStats.RefinishHoursValues.Count >= 3 ? 0.95 : specificStats.RefinishHoursValues.Count == 2 ? 0.85 : 0.5
                    };
                }
            }

            // Fall back to checking all operation types
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
        /// Resolve the most common real CCC description for a part + operation from learned data.
        /// Falls back to generic "{sideCode}{TitleCase(partName)} {opCode}" format.
        /// </summary>
        private string ResolveLearnedDescription(string partName, string operationType, string sideCode = "")
        {
            var canonicalName = _knowledgeBase.ResolveAlias(partName.ToLower()) ?? partName.ToLower();
            var stats = _knowledgeBase.GetOperationStats(canonicalName, operationType);
            var learned = stats?.MostCommonDescription;
            if (!string.IsNullOrWhiteSpace(learned))
            {
                // If the learned description already has a side prefix, use as-is; otherwise prepend sideCode
                if (!string.IsNullOrEmpty(sideCode) &&
                    !learned.StartsWith("LT ", StringComparison.OrdinalIgnoreCase) &&
                    !learned.StartsWith("RT ", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{sideCode}{learned}";
                }
                return learned;
            }

            // Fallback to generic format
            var opCode = operationType.ToLower() switch
            {
                "replace" => "Repl",
                "repair" => "Rpr",
                "r&i" => "R&I",
                "refinish" or "rfn" => "Rfn",
                "blend" => "Blend",
                _ => operationType
            };
            return $"{sideCode}{ToTitleCase(partName)} {opCode}";
        }

        /// <summary>
        /// Resolve the most common labor type for a part + operation from learned data.
        /// Falls back to DetermineCategory() heuristic.
        /// </summary>
        private string ResolveLearnedLaborType(string partName, string operationType)
        {
            var canonicalName = _knowledgeBase.ResolveAlias(partName.ToLower()) ?? partName.ToLower();
            var stats = _knowledgeBase.GetOperationStats(canonicalName, operationType);
            var learned = stats?.MostCommonLaborType;
            if (!string.IsNullOrWhiteSpace(learned))
                return learned;

            return DetermineCategory(partName);
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
                return "MISCELLANEOUS OPERATIONS"; // Don't default generic door to front

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

            // Bumper fallback (generic "bumper" without front/rear) — default to MISCELLANEOUS
            // to avoid wrongly classifying rear bumper sub-ops under FRONT BUMPER
            if (lower.Contains("bumper"))
                return "MISCELLANEOUS OPERATIONS";

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
            var subPartKeywords = new[] { "grommet", "clip", "retainer", "rivet", "fastener", "screw",
                "bolt", "nut", "pin", "stud", "plug", "washer", "bushing" };
            var trimKeywords = new[] { "molding", "moulding", "emblem", "nameplate", "badge",
                "decal", "stripe", "adhesive", "sealer", "sealant" };

            foreach (var op in result.Operations)
            {
                var partLower = op.PartName?.ToLower() ?? "";
                var descLower = op.Description?.ToLower() ?? "";
                var opTypeLower = op.OperationType?.ToLower() ?? "";
                var combined = $"{partLower} {descLower}";

                // Sub-components: grommets, clips, retainers, etc. — max 1.0h
                if (subPartKeywords.Any(k => combined.Contains(k)))
                {
                    if (op.LaborHours > 1.0m) op.LaborHours = 1.0m;
                    if (op.RefinishHours > 0.5m) op.RefinishHours = 0.5m;
                }
                // Small trim/molding parts — max 2.0h
                else if (trimKeywords.Any(k => combined.Contains(k)))
                {
                    if (op.LaborHours > 2.0m) op.LaborHours = 2.0m;
                    if (op.RefinishHours > 2.0m) op.RefinishHours = 2.0m;
                }
                // SOP/misc operations: max 1.5h
                else if (sopMiscPatterns.Any(p => combined.Contains(p)))
                {
                    if (op.LaborHours > 1.5m) op.LaborHours = 1.5m;
                    if (op.RefinishHours > 1.5m) op.RefinishHours = 1.5m;
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

        /// <summary>
        /// Filter ghost estimate operations: remove junk, part numbers, misc-only ops in wrong sections,
        /// and parts that don't belong in MISCELLANEOUS.
        /// </summary>
        private void FilterGhostOperations(GhostEstimateResult result)
        {
            // Step 1: Ensure every op has a Section so filtering works correctly.
            // GenerateOperationsForPanel doesn't set Section — the UI falls back to Category.
            foreach (var op in result.Operations)
            {
                if (string.IsNullOrWhiteSpace(op.Section))
                    op.Section = MapToCCCSection(op.PartName ?? "", op.Category ?? "");
            }

            result.Operations = result.Operations
                .Where(op =>
                {
                    var descLower = (op.Description ?? "").ToLowerInvariant();
                    var partLower = (op.PartName ?? "").ToLowerInvariant();
                    var section = op.Section ?? "";

                    // --- Global junk filters (apply to ALL sections) ---

                    // Filter out hardware, parts, logistics, and consumable junk
                    if (JunkOperationKeywords.Any(kw => descLower.Contains(kw)))
                        return false;

                    // Filter operations with part numbers (7+ digits = OEM part number)
                    if (Regex.IsMatch(descLower, @"\d{7,}"))
                        return false;

                    // Filter truncated junk lines — raw RMC/estimate data that leaked through
                    if (descLower.Contains("refinish material invoice") && section != "MISCELLANEOUS OPERATIONS")
                        return false;
                    if (descLower.Contains(" rmc ") || descLower.Contains(" rmc$") || descLower.EndsWith(" rmc"))
                        return false;

                    // Filter all-caps junk lines (FEATHER EDGE, TT Included, etc.)
                    if (descLower.Contains("two tone calculation") || descLower.Contains("clear coat and two tone"))
                        return false;
                    if (descLower.Contains("feather edge & block"))
                        return false;

                    // Filter lines with leading estimate line numbers (e.g. "135 900500 Refinish...")
                    if (Regex.IsMatch(descLower, @"^\d{2,3}\s+\d{5,}"))
                        return false;

                    // Filter truncated/fragment descriptions that start with "for " or "add for"
                    // but keep legitimate "Add for Edging", "Add for Underside"
                    if (Regex.IsMatch(descLower, @"^for\s+\w") && descLower.Length < 25)
                        return false;
                    if (Regex.IsMatch(descLower, @"^add for\s+\w") && descLower.Contains("  "))
                        return false;

                    // Filter "Buff Only" — not a standalone operation
                    if (descLower.Trim() == "buff only")
                        return false;

                    // Filter items with dollar amounts embedded in the description (price data, not ops)
                    if (Regex.IsMatch(descLower, @"\$\s*$"))
                        return false;

                    // Filter operations that are actually parts with prices (clips, moldings with high $)
                    if (partLower.Contains("clip") && op.Price > 50)
                        return false;

                    // --- Misc-only ops: must ONLY appear in MISCELLANEOUS ---
                    if (IsMiscOnlyOperation(descLower) && section != "MISCELLANEOUS OPERATIONS")
                        return false;

                    // --- Scans belong in VEHICLE DIAGNOSTICS only ---
                    if ((descLower.Contains("pre-repair scan") || descLower.Contains("post-repair scan") ||
                         descLower.Contains("pre-scan") || descLower.Contains("post-scan") ||
                         descLower.Contains("pre scan") || descLower.Contains("post scan")) &&
                        section != "VEHICLE DIAGNOSTICS")
                        return false;

                    // --- MECHANICAL section cleanup ---
                    if (section == "MECHANICAL")
                    {
                        // Remove test speaker, wheel repl, and other non-body ops
                        if (descLower.Contains("test speaker"))
                            return false;
                        if (descLower.Contains("wheel") && descLower.Contains("repl"))
                            return false;
                    }

                    // --- MISCELLANEOUS section cleanup ---
                    if (section == "MISCELLANEOUS OPERATIONS")
                    {
                        var d = descLower;
                        // R&I parts don't belong in misc (side marker, bumper cover, door trim, door handle, etc.)
                        if ((d.Contains("r&i") || d.Contains("remove and install")) && !IsMustHaveDescription(d))
                            return false;
                        // Major panel names in misc = wrong section
                        if (MajorPanels.Any(p => d.Contains(p)))
                            return false;
                        // Specific parts that end up in misc
                        if (d.Contains("side marker") || d.Contains("fog lamp") || d.Contains("parking sensor"))
                            return false;
                        if (d.Contains("door trim panel") || d.Contains("door handle") || d.Contains("door shell"))
                            return false;
                        if (d.Contains("upper molding") || d.Contains("lower molding"))
                            return false;
                        if (d.Contains("clear coat application") || d.Contains("three stage paint") || d.Contains("two tone"))
                            return false;
                        // Generic "stage and secure" without must-have context
                        if (d.Contains("stage and secure") && !IsMustHaveDescription(d))
                            return false;
                        // Duplicate cover/protect ops that aren't from must-haves list
                        // (Must-have versions have specific descriptions like "Cover Car for Overspray")
                        if (d.Contains("cover and protect electrical") || d.Contains("cover vehicle"))
                            return false;
                        // Flex additive with absurd hours in misc — the per-panel version is correct
                        if (d.Contains("flex additive") && op.LaborHours + op.RefinishHours > 1.0m)
                            return false;
                    }

                    return true;
                })
                .ToList();
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

        /// <summary>
        /// AI-generated plain-English explanation of the estimate (null if AI unavailable)
        /// </summary>
        public string? AiExplanation { get; set; }
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
        public string? LearnedDescription { get; set; }
        public string? LearnedLaborType { get; set; }
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

    #region AI-Powered Methods (GhostEstimateService partial)

    public partial class GhostEstimateService
    {
        /// <summary>
        /// Async version of GenerateGhostEstimate that uses AI for panel detection when available.
        /// Falls back to keyword-based parsing when AI is unavailable.
        /// </summary>
        public async Task<GhostEstimateResult> GenerateGhostEstimateAsync(GhostEstimateInput input)
        {
            var result = new GhostEstimateResult
            {
                VehicleInfo = input.VehicleInfo,
                DamageDescription = input.DamageDescription,
                GeneratedAt = DateTime.Now
            };

            foreach (var warning in _missingDataWarnings)
                result.Notes.Add(warning);

            var vehicleType = _patternIntelligence.ClassifyVehicleType(input.VehicleInfo);
            result.VehicleType = vehicleType;

            // Step 1: Try AI panel detection first, fall back to keyword parser
            var affectedPanels = await GetAffectedPanelsAsync(input);

            // Apply per-panel severity overrides
            if (input.PanelSeverities.Count > 0)
            {
                foreach (var panel in affectedPanels)
                {
                    if (input.PanelSeverities.TryGetValue(panel.Name, out var panelSeverity))
                        panel.Severity = panelSeverity;
                }
            }

            // Steps 2-8 same as synchronous version
            foreach (var panel in affectedPanels)
            {
                var operations = GenerateOperationsForPanel(panel, panel.Severity, vehicleType);
                result.Operations.AddRange(operations);
            }

            AddLearnedCoOccurrenceOperations(result, affectedPanels, vehicleType);
            AddOperationsFromSimilarEstimates(result, affectedPanels, vehicleType);
            AddRelatedOperations(result, input);
            AddRefinishOperations(result);

            foreach (var op in result.Operations)
            {
                op.Description = CleanOperationDescription(op.Description);
                op.PartName = CleanPartName(op.PartName);
            }
            SanitizeHours(result);
            DeduplicateOperations(result);
            FilterGhostOperations(result);
            CalculateTotals(result);
            AddConfidenceNotes(result);

            return result;
        }

        /// <summary>
        /// Async version of GenerateGuidanceEstimate that leverages AI capabilities.
        /// Uses sync GenerateGuidanceEstimate for the core logic, then cleans up operations
        /// and adds AI explanation.
        /// </summary>
        public async Task<GuidanceEstimateResult> GenerateGuidanceEstimateAsync(GhostEstimateInput input)
        {
            // Use sync version for core logic (all inline operations)
            var result = GenerateGuidanceEstimate(input);

            // AI cleanup pass: fix truncated descriptions, remove junk line numbers/codes,
            // drop empty/garbage operations, and standardize formatting
            await TryAiOperationCleanupAsync(result);

            // AI explanation removed — not useful for estimators and costs tokens

            return result;
        }

        /// <summary>
        /// Use AI to clean up generated ghost estimate operations:
        /// - Fix truncated descriptions (e.g. "Door shell (HSS) Stage and" → "Door shell (HSS) Stage and Secure")
        /// - Remove junk line numbers/codes from descriptions (e.g. "30 Repl Lower grille retainer 4 11.92")
        /// - Drop empty/meaningless operations
        /// - Fix items in wrong sections
        /// - Standardize operation descriptions to professional estimating language
        /// </summary>
        private async Task TryAiOperationCleanupAsync(GuidanceEstimateResult result)
        {
            try
            {
                var apiService = ClaudeApiService.Instance;
                if (!AiConfigService.Instance.IsFeatureEnabled(AiFeature.GhostPanelDetection))
                    return;

                if (result.GuidanceOperations.Count == 0) return;

                var systemPrompt = @"You are a collision repair estimating expert. Clean up these estimate operation descriptions.

For each operation, return the CLEANED version. Fix these common problems:
1. Truncated descriptions — complete them (e.g. ""Stage and"" → ""Stage and Secure"", ""Cover Interior and Jambs for"" → ""Cover Interior and Jambs for Refinish"", ""Lift gate Stage and Secure for"" → ""Liftgate Stage and Secure for Refinish"")
2. Leading line numbers or junk codes — remove them (e.g. ""30 Repl Lower grille retainer 4 11.92"" → ""Lower Grille Retainer"", ""62 AUTO L Add For Pillar Refinish C"" → ""Add for Pillar Refinish"")
3. Fragment descriptions that don't make sense alone — fix them (e.g. ""for Clear Coat"" → ""Clear Coat Application"")
4. Inconsistent capitalization — use Title Case for part names
5. Remove operations that are pure garbage/unreadable
6. ""Adhesion Promoter Only"" with no hours/price = remove it (duplicate of one with values)

Return a JSON array of objects with:
- ""index"": the 0-based index of the operation
- ""description"": cleaned description (or null to remove the operation)
- ""partName"": cleaned part name (or null to keep original)
- ""section"": corrected section if the item is in the wrong section (or null to keep)

Only include operations that need changes or removal. If an operation is fine, don't include it.
Return ONLY the JSON array, no markdown.";

                // Build operation list for AI
                var opLines = new List<string>();
                for (int i = 0; i < result.GuidanceOperations.Count && i < 80; i++)
                {
                    var op = result.GuidanceOperations[i];
                    opLines.Add($"[{i}] Section: {op.Section ?? op.Category} | Desc: \"{op.Description}\" | Part: \"{op.PartName}\" | Type: {op.OperationType} | {op.LaborHours}h {(op.RefinishHours > 0 ? $"+ {op.RefinishHours}h rfn " : "")}${op.Price}");
                }

                var userMessage = string.Join("\n", opLines);
                if (userMessage.Length > 4000)
                    userMessage = userMessage.Substring(0, 4000);

                var response = await apiService.SendAsync(systemPrompt, userMessage, AiFeature.GhostExplanation, 2048);
                if (response == null) return;

                var text = ClaudeApiService.StripCodeFences(response.Text);

                var fixes = System.Text.Json.JsonSerializer.Deserialize<List<AiOperationFix>>(text, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (fixes == null || fixes.Count == 0) return;

                // Apply fixes in reverse order so removals don't shift indices
                var toRemove = new HashSet<int>();
                foreach (var fix in fixes)
                {
                    if (fix.Index < 0 || fix.Index >= result.GuidanceOperations.Count) continue;

                    if (fix.Description == null)
                    {
                        // null description = remove operation
                        toRemove.Add(fix.Index);
                        continue;
                    }

                    var op = result.GuidanceOperations[fix.Index];
                    if (!string.IsNullOrWhiteSpace(fix.Description))
                        op.Description = fix.Description;
                    if (!string.IsNullOrWhiteSpace(fix.PartName))
                        op.PartName = fix.PartName;
                    if (!string.IsNullOrWhiteSpace(fix.Section))
                        op.Section = fix.Section;
                }

                if (toRemove.Count > 0)
                {
                    result.GuidanceOperations = result.GuidanceOperations
                        .Where((op, i) => !toRemove.Contains(i))
                        .ToList();
                    // Also update the base Operations list
                    result.Operations = result.GuidanceOperations.Cast<GhostOperation>().ToList();
                    System.Diagnostics.Debug.WriteLine($"[Ghost] AI cleanup removed {toRemove.Count} junk operations");
                }

                System.Diagnostics.Debug.WriteLine($"[Ghost] AI cleanup applied {fixes.Count} fixes to operations");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] AI operation cleanup failed: {ex.Message}");
            }
        }

        private class AiOperationFix
        {
            public int Index { get; set; }
            public string? Description { get; set; }
            public string? PartName { get; set; }
            public string? Section { get; set; }
        }

        /// <summary>
        /// Try AI-powered panel detection from damage description.
        /// Falls back to keyword-based detection on failure.
        /// </summary>
        private async Task<List<AffectedPanel>> GetAffectedPanelsAsync(GhostEstimateInput input)
        {
            // Try AI first
            var aiPanels = await TryAiPanelDetectionAsync(input.DamageDescription, input.Severity);
            if (aiPanels != null && aiPanels.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] AI detected {aiPanels.Count} panels");

                // Merge with explicit impact zone panels
                var merged = new List<AffectedPanel>(aiPanels);
                var addedNames = new HashSet<string>(aiPanels.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

                foreach (var zone in input.ImpactZones)
                {
                    if (ImpactZonePanels.TryGetValue(zone.ToLower().Replace(" ", "_"), out var zonePanels))
                    {
                        foreach (var panelName in zonePanels)
                        {
                            if (addedNames.Add(panelName))
                            {
                                merged.Add(new AffectedPanel
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

                return merged;
            }

            // Fall back to keyword-based parsing
            return GetAffectedPanels(input);
        }

        /// <summary>
        /// Use AI to parse a natural-language damage description into structured panel data.
        /// Returns null on failure.
        /// </summary>
        private async Task<List<AffectedPanel>?> TryAiPanelDetectionAsync(string damageDescription, string defaultSeverity)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(damageDescription) || damageDescription.Length < 5)
                    return null;

                var apiService = ClaudeApiService.Instance;
                var systemPrompt = @"You are a collision repair damage analyst. Parse the damage description into affected vehicle panels.

Return a JSON array of panels. Each panel object has:
- ""name"": lowercase panel name (e.g. ""front bumper cover"", ""hood"", ""fender"", ""front door"", ""rear door"", ""quarter panel"", ""roof"", ""trunk lid"", ""grille"", ""headlight"", ""tail light"", ""rocker panel"", ""a-pillar"", ""b-pillar"", ""radiator support"", ""rear bumper cover"", ""mirror"")
- ""side"": ""left"", ""right"", ""both"", or """" (empty for center panels)
- ""severity"": ""light"", ""moderate"", ""heavy"", or ""severe""

Rules:
- Use standard collision repair panel names
- Infer related panels (e.g. front end hit implies bumper, hood, grille, possibly fender)
- Be conservative — only include panels clearly indicated or strongly implied
- Return ONLY the JSON array, no markdown or explanation";

                var response = await apiService.SendAsync(systemPrompt, damageDescription, AiFeature.GhostPanelDetection, 512);
                if (response == null) return null;

                var text = ClaudeApiService.StripCodeFences(response.Text);

                var aiPanels = JsonSerializer.Deserialize<List<AiPanelResult>>(text, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (aiPanels == null || aiPanels.Count == 0) return null;

                var panels = new List<AffectedPanel>();
                foreach (var p in aiPanels)
                {
                    if (string.IsNullOrWhiteSpace(p.Name)) continue;
                    panels.Add(new AffectedPanel
                    {
                        Name = p.Name.ToLowerInvariant().Trim(),
                        Side = p.Side ?? "",
                        Severity = string.IsNullOrWhiteSpace(p.Severity) ? defaultSeverity : p.Severity,
                        ImpactZone = "ai_detected"
                    });
                }

                return panels.Count > 0 ? panels : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] AI panel detection failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Generate a plain-English explanation of a ghost/guidance estimate using AI.
        /// Returns null if AI is unavailable.
        /// </summary>
        public async Task<string?> GenerateExplanationAsync(GuidanceEstimateResult result)
        {
            try
            {
                var apiService = ClaudeApiService.Instance;
                var systemPrompt = @"You are a collision repair estimating assistant. Summarize the estimate in 2-4 sentences of plain English that a body shop estimator would understand. Mention key panels, total hours, and notable operations. Be concise and professional.";

                var opSummary = string.Join("\n", result.GuidanceOperations
                    .Take(50)
                    .Select(o => $"- {o.Description} ({o.OperationType}) {o.LaborHours}h {(o.RefinishHours > 0 ? $"+ {o.RefinishHours}h refn" : "")}"));

                var userMessage = $"Vehicle: {result.VehicleInfo}\nDamage: {result.DamageDescription}\nBody Hours: {result.TotalBodyHours:F1}, Refinish Hours: {result.TotalRefinishHours:F1}\nTotal: ${result.GrandTotalLaborDollars:F2}\n\nOperations:\n{opSummary}";

                var response = await apiService.SendAsync(systemPrompt, userMessage, AiFeature.GhostExplanation, 256);
                return response?.Text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Ghost] AI explanation failed: {ex.Message}");
                return null;
            }
        }

        private class AiPanelResult
        {
            public string? Name { get; set; }
            public string? Side { get; set; }
            public string? Severity { get; set; }
        }
    }

    #endregion
}
