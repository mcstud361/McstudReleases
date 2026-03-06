#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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

            // Step 3: Add related operations (ADAS, calibrations, etc.)
            AddRelatedOperations(result, input);

            // Step 4: Add refinish operations
            AddRefinishOperations(result);

            // Step 5: Calculate totals
            CalculateTotals(result);

            // Step 6: Add confidence notes
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

            // Find operations ghost has that user doesn't
            foreach (var ghostOp in ghostEstimate.Operations)
            {
                var userHas = userOperations.Any(u =>
                    OperationsMatch(u, ghostOp));

                if (!userHas)
                {
                    result.GhostFoundMissing.Add(new MissingOperation
                    {
                        Operation = ghostOp,
                        Reason = ghostOp.Source ?? "AI pattern match",
                        Confidence = ghostOp.Confidence
                    });
                }
                else
                {
                    result.BothHave.Add(ghostOp);
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
                                Category = rec.Category == "Diagnostic" ? "Scanning" :
                                           rec.Category == "Refinish" ? "Refinish Operations" :
                                           "Body Operations",
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
                            result.GuidanceOperations.Add(new GuidanceOperation
                            {
                                OperationType = sug.OperationType,
                                PartName = panel.Name,
                                Description = sug.Description,
                                Category = sug.OperationType == "Rfn" || sug.OperationType == "Refinish" || sug.OperationType == "Blend"
                                    ? "Refinish Operations" : "Body Operations",
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

            // Add warnings
            if (result.LearnedCount == 0)
                result.Warnings.Add("No learned patterns available. Upload estimates to improve suggestions.");

            if (result.KnowledgeBaseCount == 0)
                result.Warnings.Add("No knowledge base operations matched. Try a more specific damage description.");

            var stats = _smartEngine.GetStats();
            if (stats.TotalEstimatesLearned < 10)
                result.ProTips.Add("Upload more estimates to unlock learned pattern suggestions.");

            if (result.GuidanceOperations.Any(o => o.Category == "Scanning"))
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

            // Normalize scan variants to canonical keys
            if (part.Contains("pre") && (part.Contains("scan") || op.Contains("scan")))
                return "pre scan|scan";
            if (part.Contains("post") && (part.Contains("scan") || op.Contains("scan")))
                return "post scan|scan";
            if ((part.Contains("diagnostic scan") || op.Contains("diagnostic scan")) && !part.Contains("post"))
                return "pre scan|scan";

            // Normalize ADAS calibration variants
            if (part.Contains("adas") || part.Contains("calibrat") || op.Contains("calibrat"))
                return "adas calibration|calibration";

            return $"{part}|{op}";
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

            // Common damage keywords and their associated panels
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
                        if (!panels.Any(p => p.Name == panelName))
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

            // Direct panel mentions
            var directPanels = new[]
            {
                "bumper", "fender", "hood", "door", "quarter", "roof", "trunk",
                "liftgate", "tailgate", "grille", "headlight", "tail light", "mirror"
            };

            // Refinish-only keywords that indicate no body repair needed
            var refinishOnlyKeywords = new[] { "blend", "refinish", "paint", "clear coat", "clearcoat", "color match", "base coat", "basecoat" };
            var replaceKeywords = new[] { "replace", "new", "order" };

            foreach (var panel in directPanels)
            {
                if (description.Contains(panel) && !panels.Any(p => p.Name.Contains(panel)))
                {
                    var fullName = GetFullPanelName(panel, description);

                    // Check context around this panel mention to determine operation type
                    var isRefinishOnly = false;
                    string? explicitOp = null;

                    // Look for operation keywords near the panel name
                    // e.g. "fender blend", "hood paint", "door replace"
                    foreach (var rk in refinishOnlyKeywords)
                    {
                        if (description.Contains($"{panel} {rk}") || description.Contains($"{rk} {panel}"))
                        {
                            isRefinishOnly = true;
                            explicitOp = rk == "blend" ? "Blend" : "Refinish";
                            break;
                        }
                    }

                    foreach (var rk in replaceKeywords)
                    {
                        if (description.Contains($"{panel} {rk}") || description.Contains($"{rk} {panel}"))
                        {
                            explicitOp = "Replace";
                            break;
                        }
                    }

                    panels.Add(new AffectedPanel
                    {
                        Name = fullName,
                        ImpactZone = "direct mention",
                        Severity = explicitOp == "Replace" ? "heavy" : DetectSeverityFromDescription(description),
                        Side = DetectSideFromDescription(description),
                        RefinishOnly = isRefinishOnly,
                        ExplicitOperation = explicitOp
                    });
                }
            }

            return panels;
        }

        private string GetFullPanelName(string shortName, string context)
        {
            // Add position context
            var isRear = context.Contains("rear");
            var isFront = context.Contains("front");

            return shortName switch
            {
                "bumper" => isRear ? "rear bumper cover" : "front bumper cover",
                "door" when context.Contains("rear door") => "rear door",
                "door" => "front door",
                "light" when isRear => "tail light",
                "light" => "headlight",
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
                var refinishHours = GetLearnedRefinishTime(panel.Name);
                if (blendOp == "Blend")
                    refinishHours = Math.Max(refinishHours * 0.5m, 1.0m); // Blend is ~50% of full refinish

                operations.Add(new GhostOperation
                {
                    OperationType = blendOp,
                    PartName = panel.Name,
                    Description = $"{sideCode}{ToTitleCase(panel.Name)} {blendLabel}",
                    Category = "Refinish Operations",
                    RefinishHours = refinishHours,
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
            var bodyHours = GetLearnedLaborTime(panel.Name, primaryOp, vehicleType) * severityMultiplier;

            // Primary operation - format like real CCC estimate line
            operations.Add(new GhostOperation
            {
                OperationType = opCode,
                PartName = panel.Name,
                Description = $"{sideCode}{ToTitleCase(panel.Name)} {opCode}",
                Category = DetermineCategory(panel.Name),
                LaborHours = bodyHours,
                Confidence = 0.95,
                Source = "CCC/MOTOR database",
                Side = panel.Side
            });

            // Add R&I operations for panels that need removal - CCC Pathways
            if (RequiresRAndI(panel.Name, primaryOp))
            {
                var riParts = GetRAndIParts(panel.Name);
                foreach (var riPart in riParts)
                {
                    var riHours = GetLearnedLaborTime(riPart, "R&I", vehicleType);
                    operations.Add(new GhostOperation
                    {
                        OperationType = "R&I",
                        PartName = riPart,
                        Description = $"{sideCode}{ToTitleCase(riPart)} R&I",
                        Category = "Part Operations",
                        LaborHours = riHours,
                        Confidence = 0.85,
                        Source = $"CCC Pathways - NOT INCLUDED in {panel.Name} time",
                        Side = panel.Side
                    });
                }
            }

            return operations;
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

            // Add scan operations - ACTUAL PRICING from real estimates
            if (result.Operations.Count > 0)
            {
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Mech",
                    PartName = "pre-repair scan",
                    Description = "Pre-Repair Diagnostic Scan",
                    Category = "Scanning",
                    LaborHours = 0.5m,  // Per IncludedNotIncluded.json
                    Confidence = 0.95,
                    Source = "CCC/MOTOR - scan tool diagnostics NOT INCLUDED"
                });

                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Mech",
                    PartName = "post-repair scan",
                    Description = "Post-Repair Diagnostic Scan",
                    Category = "Scanning",
                    LaborHours = 0.5m,  // Per IncludedNotIncluded.json
                    Confidence = 0.95,
                    Source = "CCC/MOTOR - scan tool diagnostics NOT INCLUDED"
                });
            }

            // Add ADAS calibrations if needed - ACTUAL PRICING
            if (hasADASComponents)
            {
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Sublet",
                    PartName = "adas calibration",
                    Description = "ADAS Calibration - Forward Camera/Radar",
                    Category = "Calibration",
                    Price = 350.00m,  // Typical shop rate
                    Confidence = 0.80,
                    Source = "CCC/MOTOR G33 - ADAS calibration triggers"
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

            // Add WELDED PANEL operations from ACTUAL MET times
            if (hasWeldedPanels)
            {
                var weldedCount = result.Operations.Count(o =>
                    o.OperationType == "Replace" && (
                    o.PartName.Contains("quarter") ||
                    o.PartName.Contains("rocker") ||
                    o.PartName.Contains("pillar")));

                // Remove Factory E-Coat - 0.3h per MET
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body",
                    PartName = "e-coat removal",
                    Description = "Remove Factory E-Coat at Weld Flanges",
                    Category = "Body Operations",
                    LaborHours = 0.3m * Math.Max(1, weldedCount),  // MET: BodyOp_A29
                    Confidence = 0.90,
                    Source = "MET: Body Operations - DEG: protective coatings NOT INCLUDED"
                });

                // Weld-Through Primer - 0.2h per MET
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body",
                    PartName = "weld-through primer",
                    Description = "Weld-Through Primer Application",
                    Category = "Body Operations",
                    LaborHours = 0.2m * Math.Max(1, weldedCount),  // MET: BodyOp_B29
                    Confidence = 0.90,
                    Source = "MET: Body Operations - Mitchell CEG: NOT INCLUDED"
                });

                // Cover Vehicle from Weld Sparks - 0.5h per MET
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body",
                    PartName = "cover car",
                    Description = "Cover Vehicle from Weld Spark Damage",
                    Category = "Body Operations",
                    LaborHours = 0.5m,  // MET: Cover Car Operations
                    Confidence = 0.85,
                    Source = "DEG Inquiry 13434 - NOT INCLUDED"
                });

                // Seam Sealer - 0.3h per application
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body",
                    PartName = "seam sealer",
                    Description = "Seam Sealer Application (Beyond Attachment Points)",
                    Category = "Body Operations",
                    LaborHours = 0.3m * Math.Max(1, weldedCount),  // MET: BodyOp_C29
                    Confidence = 0.85,
                    Source = "DEG Inquiry 23010 - additional seam sealer NOT INCLUDED"
                });

                // Cavity Wax / Anti-Corrosion - 0.3-0.5h per MET
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body",
                    PartName = "cavity wax",
                    Description = "Cavity Wax / Anti-Corrosion Treatment",
                    Category = "Body Operations",
                    LaborHours = 0.5m * Math.Max(1, weldedCount),  // MET: BodyOp_D29
                    Confidence = 0.90,
                    Source = "Mitchell CEG G31 - corrosion protection NOT INCLUDED"
                });
            }

            // Add PLASTIC PART operations from ACTUAL formulas
            if (hasPlasticParts)
            {
                var plasticPanels = result.Operations.Where(o =>
                    (o.OperationType == "Replace" || o.OperationType == "Refinish") &&
                    (o.PartName.Contains("bumper") || o.PartName.Contains("fascia"))).ToList();

                if (plasticPanels.Any())
                {
                    // Calculate refinish hours for plastic panels
                    var totalRefinishHours = plasticPanels.Sum(p => p.RefinishHours > 0 ? p.RefinishHours : 2.5m);

                    // Adhesion Promoter - 10% of refinish, min 0.2, max 0.5 per PartOperationsFormulas.json
                    var adhesionHours = Math.Min(0.5m, Math.Max(0.2m, totalRefinishHours * 0.10m));
                    result.Operations.Add(new GhostOperation
                    {
                        OperationType = "Paint",
                        PartName = "adhesion promoter",
                        Description = "Adhesion Promoter (Plastic Parts)",
                        Category = "Refinish Operations",
                        RefinishHours = adhesionHours,
                        Confidence = 0.95,
                        Source = "Mitchell CEG Section 22 - plastic NOT INCLUDED"
                    });

                    // Flex Additive - 0.2h for first large plastic part per formula
                    result.Operations.Add(new GhostOperation
                    {
                        OperationType = "Paint",
                        PartName = "flex additive",
                        Description = "Flex Additive (First Large Plastic Part)",
                        Category = "Refinish Operations",
                        RefinishHours = 0.2m,
                        Confidence = 0.95,
                        Source = "MET: Part Operations - flexible paint NOT INCLUDED"
                    });
                }
            }

            // Add corrosion protection for all replaced panels
            var replacedPanels = result.Operations.Where(o => o.OperationType == "Replace").ToList();
            if (replacedPanels.Any() && !hasWeldedPanels)
            {
                // Hem flange corrosion protection for bolted panels
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Body",
                    PartName = "corrosion protection",
                    Description = "Corrosion Protection - Hem Flanges",
                    Category = "Body Operations",
                    LaborHours = 0.2m * replacedPanels.Count,  // MET: 0.2h per panel
                    Confidence = 0.90,
                    Source = "Mitchell CEG G31 - hem flange protection NOT INCLUDED"
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
                var refinishHours = GetLearnedRefinishTime(panel);

                // Add refinish for the panel - use proper CCC terminology
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Rfn",  // CCC terminology
                    PartName = panel,
                    Description = $"Refinish {ToTitleCase(panel)}",
                    Category = "Refinish Operations",
                    RefinishHours = refinishHours,
                    Confidence = 0.95,
                    Source = "Mitchell CEG - separate operation"
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

            // Add blend operations - ACTUAL FORMULA: up to 20% of base refinish time first panel, 10% each additional
            var blendPanelList = panelsToBlend.ToList();
            for (int i = 0; i < blendPanelList.Count; i++)
            {
                var blendPanel = blendPanelList[i];
                var baseRefinish = GetLearnedRefinishTime(blendPanel);
                // First blend panel: 20% of base, additional: 10% per IncludedNotIncluded.json blend formulas
                var blendPercent = i == 0 ? 0.20m : 0.10m;

                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Blend",
                    PartName = blendPanel,
                    Description = $"Blend {ToTitleCase(blendPanel)} (Color Match)",
                    Category = "Refinish Operations",
                    RefinishHours = baseRefinish * blendPercent,
                    Confidence = 0.80,
                    Source = "Mitchell CEG Section 28 - blend NOT INCLUDED"
                });
            }

            // Add Feather, Prime & Block for REPAIR panels - ACTUAL operation from estimates
            if (hasRepairPanels)
            {
                var repairPanelCount = result.Operations.Count(o => o.OperationType == "Repair");
                // Per IncludedNotIncluded.json: "Feather, prime & block" typical 0.5-3.0 hours
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Paint",
                    PartName = "feather prime block",
                    Description = "Feather, Prime & Block (Repair Panels)",
                    Category = "Refinish Operations",
                    RefinishHours = 0.5m * repairPanelCount,  // MET: PartOp_A33 base
                    Confidence = 0.90,
                    Source = "Mitchell CEG Section 28 - NOT INCLUDED"
                });
            }

            // Add DE-NIB if refinishing - ACTUAL operation from PartOperationsFormulas.json
            if (panelsNeedingRefinish.Any())
            {
                // Per formula: 0.2h flat rate when refinishing (if refinishHours >= 0.5)
                result.Operations.Add(new GhostOperation
                {
                    OperationType = "Paint",
                    PartName = "denib",
                    Description = "DE-NIB & Polish",
                    Category = "Refinish Operations",
                    RefinishHours = 0.3m,  // Per formula: 0.2-0.3h typical
                    Confidence = 0.85,
                    Source = "MET: Part Operations - denib NOT INCLUDED"
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
            // Typical shop labor rates (can be customized)
            const decimal BODY_LABOR_RATE = 55.00m;     // $55/hr body labor (typical)
            const decimal PAINT_LABOR_RATE = 55.00m;    // $55/hr refinish labor
            const decimal MECH_LABOR_RATE = 95.00m;     // $95/hr mechanical (scanning, etc.)
            const decimal FRAME_LABOR_RATE = 75.00m;    // $75/hr frame labor

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
        }

        private void AddConfidenceNotes(GhostEstimateResult result)
        {
            var stats = _smartEngine.GetStats();

            if (stats.TotalEstimatesLearned < 10)
            {
                result.Notes.Add("Low training data - estimates based primarily on base knowledge. Upload more estimates to improve accuracy.");
            }
            else if (stats.TotalEstimatesLearned < 50)
            {
                result.Notes.Add($"Moderate training ({stats.TotalEstimatesLearned} estimates learned). Accuracy improves with more data.");
            }
            else
            {
                result.Notes.Add($"Well-trained model ({stats.TotalEstimatesLearned} estimates). High confidence in suggestions.");
            }

            // Add notes for low-confidence operations
            var lowConfidence = result.Operations.Where(o => o.Confidence < 0.7).ToList();
            if (lowConfidence.Any())
            {
                result.Notes.Add($"{lowConfidence.Count} operations have lower confidence - verify these carefully.");
            }
        }

        private decimal GetLearnedLaborTime(string partName, string operationType, string vehicleType)
        {
            // Try to get from loaded operations data (real MET times)
            if (_operationsData?.Operations != null)
            {
                var matchingOp = _operationsData.Operations.FirstOrDefault(op =>
                    op.Aliases?.Any(a => partName.ToLower().Contains(a.ToLower())) == true ||
                    partName.ToLower().Contains(op.PartName?.ToLower() ?? ""));

                if (matchingOp?.MetOperations != null)
                {
                    var totalHours = matchingOp.MetOperations.Sum(m => m.Hours);
                    if (totalHours > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Ghost] Using MET data for {partName}: {totalHours}h total");
                        return (decimal)totalHours;
                    }
                }
            }

            // Fall back to database-representative defaults from REAL ESTIMATES
            // These are typical CCC/MOTOR database times
            return (partName, operationType) switch
            {
                // REPLACE operations - typical database times
                (var p, "Replace") when p.Contains("bumper cover") || p.Contains("fascia") => 1.5m,
                (var p, "Replace") when p.Contains("front bumper") => 1.5m,
                (var p, "Replace") when p.Contains("rear bumper") => 1.2m,
                (var p, "Replace") when p.Contains("fender") => 2.0m,
                (var p, "Replace") when p.Contains("hood") => 1.0m,
                (var p, "Replace") when p.Contains("front door") => 3.5m,
                (var p, "Replace") when p.Contains("rear door") => 3.2m,
                (var p, "Replace") when p.Contains("quarter") => 8.0m,  // Welded - high labor
                (var p, "Replace") when p.Contains("rocker") => 6.0m,   // Welded structural
                (var p, "Replace") when p.Contains("roof") => 10.0m,    // Major structural
                (var p, "Replace") when p.Contains("trunk") || p.Contains("decklid") => 1.5m,
                (var p, "Replace") when p.Contains("liftgate") || p.Contains("tailgate") => 2.0m,

                // REPAIR operations - typical database times
                (var p, "Repair") when p.Contains("bumper") => 2.0m,
                (var p, "Repair") when p.Contains("fender") => 3.0m,
                (var p, "Repair") when p.Contains("door") => 2.5m,
                (var p, "Repair") when p.Contains("hood") => 2.5m,
                (var p, "Repair") when p.Contains("quarter") => 4.0m,

                // R&I operations - from MET database
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
        }

        private decimal GetLearnedRefinishTime(string partName)
        {
            // ACTUAL refinish times from real estimates (CCC database typical)
            return partName switch
            {
                // Large panels - typically higher refinish time
                var p when p.Contains("hood") => 3.5m,
                var p when p.Contains("roof") => 4.0m,
                var p when p.Contains("trunk") || p.Contains("decklid") => 3.0m,

                // Standard panels
                var p when p.Contains("fender") => 3.0m,
                var p when p.Contains("door") => 3.0m,
                var p when p.Contains("quarter") => 4.0m,

                // Bumpers - plastic, includes flex additive consideration
                var p when p.Contains("bumper") || p.Contains("fascia") => 2.5m,
                var p when p.Contains("valance") => 1.5m,
                var p when p.Contains("spoiler") => 2.0m,

                // Smaller parts
                var p when p.Contains("mirror") => 0.8m,
                var p when p.Contains("molding") => 0.5m,

                _ => 2.5m
            };
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

        public List<string> Notes { get; set; } = new();
    }

    public class GhostOperation
    {
        public string OperationType { get; set; } = "";
        public string PartName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string Side { get; set; } = "";

        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }

        public double Confidence { get; set; } = 1.0;
        public string? Source { get; set; }
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

        public double MatchPercentage { get; set; }
        public string Summary { get; set; } = "";
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
    }

    public class GuidanceEstimateResult : GhostEstimateResult
    {
        public List<GuidanceOperation> GuidanceOperations { get; set; } = new();

        // Source breakdown counts
        public int DatabaseCount { get; set; }
        public int KnowledgeBaseCount { get; set; }
        public int LearnedCount { get; set; }

        public List<string> Warnings { get; set; } = new();
        public List<string> ProTips { get; set; } = new();
    }

    #endregion

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
