#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Smart Estimate Analyzer - Analyzes uploaded estimates and suggests commonly missed items,
    /// ADAS calibrations, diagnostics, and other operations based on industry knowledge bases.
    /// </summary>
    public class SmartEstimateAnalyzerService
    {
        private static SmartEstimateAnalyzerService? _instance;
        public static SmartEstimateAnalyzerService Instance => _instance ??= new SmartEstimateAnalyzerService();

        private SmartCommonlyMissedData? _commonlyMissed;
        private SmartADASRequirementsData? _adasRequirements;
        private bool _isLoaded = false;
        private readonly EstimateLearningService _learningService;

        #region Panel Adjacency Data

        private static readonly Dictionary<string, PanelAdjacencyInfo> _panelAdjacencyMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["front bumper"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Front Bumper", DefaultBlendHours = 1.0m,
                AdjacentPanels = new()
                {
                    new() { Name = "Hood", SideRelationship = SideRelationship.Center, Priority = "high" },
                    new() { Name = "Fender", SideRelationship = SideRelationship.BothSides, Priority = "high" }
                }
            },
            ["hood"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Hood", DefaultBlendHours = 1.5m,
                AdjacentPanels = new()
                {
                    new() { Name = "Fender", SideRelationship = SideRelationship.BothSides, Priority = "high" },
                    new() { Name = "Front Bumper", SideRelationship = SideRelationship.Center, Priority = "high" }
                }
            },
            ["fender"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Fender", DefaultBlendHours = 1.0m,
                AdjacentPanels = new()
                {
                    new() { Name = "Hood", SideRelationship = SideRelationship.Center, Priority = "high" },
                    new() { Name = "Front Door", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Front Bumper", SideRelationship = SideRelationship.Center, Priority = "high" },
                    new() { Name = "A-Pillar", SideRelationship = SideRelationship.SameSide, Priority = "medium" }
                }
            },
            ["front door"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Front Door", DefaultBlendHours = 1.0m,
                AdjacentPanels = new()
                {
                    new() { Name = "Fender", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Rear Door", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "A-Pillar", SideRelationship = SideRelationship.SameSide, Priority = "medium" },
                    new() { Name = "B-Pillar", SideRelationship = SideRelationship.SameSide, Priority = "medium" },
                    new() { Name = "Rocker Panel", SideRelationship = SideRelationship.SameSide, Priority = "medium" }
                }
            },
            ["rear door"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Rear Door", DefaultBlendHours = 1.0m,
                AdjacentPanels = new()
                {
                    new() { Name = "Front Door", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Quarter Panel", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "B-Pillar", SideRelationship = SideRelationship.SameSide, Priority = "medium" },
                    new() { Name = "C-Pillar", SideRelationship = SideRelationship.SameSide, Priority = "medium" },
                    new() { Name = "Rocker Panel", SideRelationship = SideRelationship.SameSide, Priority = "medium" }
                }
            },
            ["quarter panel"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Quarter Panel", DefaultBlendHours = 1.5m,
                AdjacentPanels = new()
                {
                    new() { Name = "Rear Door", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Rear Bumper", SideRelationship = SideRelationship.Center, Priority = "high" },
                    new() { Name = "Trunk Lid", SideRelationship = SideRelationship.Center, Priority = "high" },
                    new() { Name = "C-Pillar", SideRelationship = SideRelationship.SameSide, Priority = "medium" },
                    new() { Name = "Roof", SideRelationship = SideRelationship.Center, Priority = "medium" }
                }
            },
            ["rear bumper"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Rear Bumper", DefaultBlendHours = 1.0m,
                AdjacentPanels = new()
                {
                    new() { Name = "Quarter Panel", SideRelationship = SideRelationship.BothSides, Priority = "high" },
                    new() { Name = "Trunk Lid", SideRelationship = SideRelationship.Center, Priority = "high" }
                }
            },
            ["trunk lid"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Trunk Lid", DefaultBlendHours = 1.5m,
                AdjacentPanels = new()
                {
                    new() { Name = "Quarter Panel", SideRelationship = SideRelationship.BothSides, Priority = "high" },
                    new() { Name = "Rear Bumper", SideRelationship = SideRelationship.Center, Priority = "high" }
                }
            },
            ["liftgate"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Liftgate", DefaultBlendHours = 1.5m,
                AdjacentPanels = new()
                {
                    new() { Name = "Quarter Panel", SideRelationship = SideRelationship.BothSides, Priority = "high" },
                    new() { Name = "Rear Bumper", SideRelationship = SideRelationship.Center, Priority = "high" }
                }
            },
            ["roof"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Roof", DefaultBlendHours = 2.0m,
                AdjacentPanels = new()
                {
                    new() { Name = "A-Pillar", SideRelationship = SideRelationship.BothSides, Priority = "medium" },
                    new() { Name = "B-Pillar", SideRelationship = SideRelationship.BothSides, Priority = "medium" },
                    new() { Name = "C-Pillar", SideRelationship = SideRelationship.BothSides, Priority = "medium" },
                    new() { Name = "Quarter Panel", SideRelationship = SideRelationship.BothSides, Priority = "medium" }
                }
            },
            ["a-pillar"] = new PanelAdjacencyInfo
            {
                CanonicalName = "A-Pillar", DefaultBlendHours = 0.5m,
                AdjacentPanels = new()
                {
                    new() { Name = "Fender", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Front Door", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Roof", SideRelationship = SideRelationship.Center, Priority = "medium" }
                }
            },
            ["b-pillar"] = new PanelAdjacencyInfo
            {
                CanonicalName = "B-Pillar", DefaultBlendHours = 0.5m,
                AdjacentPanels = new()
                {
                    new() { Name = "Front Door", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Rear Door", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Roof", SideRelationship = SideRelationship.Center, Priority = "medium" }
                }
            },
            ["c-pillar"] = new PanelAdjacencyInfo
            {
                CanonicalName = "C-Pillar", DefaultBlendHours = 0.5m,
                AdjacentPanels = new()
                {
                    new() { Name = "Rear Door", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Quarter Panel", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Roof", SideRelationship = SideRelationship.Center, Priority = "medium" }
                }
            },
            ["rocker panel"] = new PanelAdjacencyInfo
            {
                CanonicalName = "Rocker Panel", DefaultBlendHours = 0.8m,
                AdjacentPanels = new()
                {
                    new() { Name = "Front Door", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Rear Door", SideRelationship = SideRelationship.SameSide, Priority = "high" },
                    new() { Name = "Quarter Panel", SideRelationship = SideRelationship.SameSide, Priority = "medium" }
                }
            }
        };

        private static readonly Dictionary<string, string> _panelNameAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            // Front Bumper
            { "fr bumper", "front bumper" },
            { "front bumper cover", "front bumper" },
            { "fr bumper cover", "front bumper" },
            { "front bumper fascia", "front bumper" },
            { "fr bumper assy", "front bumper" },
            { "bumper cover front", "front bumper" },
            { "front fascia", "front bumper" },
            // Rear Bumper
            { "rr bumper", "rear bumper" },
            { "rear bumper cover", "rear bumper" },
            { "rr bumper cover", "rear bumper" },
            { "rear bumper fascia", "rear bumper" },
            { "rr bumper assy", "rear bumper" },
            { "bumper cover rear", "rear bumper" },
            { "rear fascia", "rear bumper" },
            // Hood
            { "hood panel", "hood" },
            { "hood assy", "hood" },
            // Fender
            { "fdr", "fender" },
            { "fender panel", "fender" },
            { "front fender", "fender" },
            { "fr fender", "fender" },
            // Front Door
            { "fr door", "front door" },
            { "front door shell", "front door" },
            { "fr door shell", "front door" },
            { "front door skin", "front door" },
            { "fr door skin", "front door" },
            // Rear Door
            { "rr door", "rear door" },
            { "rear door shell", "rear door" },
            { "rr door shell", "rear door" },
            { "rear door skin", "rear door" },
            { "rr door skin", "rear door" },
            // Quarter Panel
            { "qtr panel", "quarter panel" },
            { "qtr pnl", "quarter panel" },
            { "quarter pnl", "quarter panel" },
            { "quarter", "quarter panel" },
            { "rear quarter", "quarter panel" },
            // Trunk / Liftgate
            { "trunk", "trunk lid" },
            { "trunk panel", "trunk lid" },
            { "decklid", "trunk lid" },
            { "deck lid", "trunk lid" },
            { "liftgate panel", "liftgate" },
            { "lift gate", "liftgate" },
            { "tailgate", "liftgate" },
            { "tail gate", "liftgate" },
            { "hatch", "liftgate" },
            // Roof
            { "roof panel", "roof" },
            { "roof skin", "roof" },
            // Pillars
            { "a pillar", "a-pillar" },
            { "a-post", "a-pillar" },
            { "b pillar", "b-pillar" },
            { "b-post", "b-pillar" },
            { "c pillar", "c-pillar" },
            { "c-post", "c-pillar" },
            // Rocker
            { "rocker", "rocker panel" },
            { "rocker pnl", "rocker panel" },
            { "rocker molding", "rocker panel" }
        };

        private static readonly HashSet<string> _centerPanels = new(StringComparer.OrdinalIgnoreCase)
        {
            "hood", "front bumper", "rear bumper", "trunk lid", "liftgate", "roof"
        };

        #endregion

        private SmartEstimateAnalyzerService()
        {
            _learningService = EstimateLearningService.Instance;
            LoadKnowledgeBases();
        }

        /// <summary>
        /// Try to get learned hours for an operation from the learning service
        /// </summary>
        private decimal GetLearnedHours(string partName, string operationType, decimal defaultHours)
        {
            try
            {
                var result = _learningService.QueryOperationsWithUnits(partName, operationType, null, null);
                if (result.Operations.Count > 0)
                {
                    // Find matching operation
                    var match = result.Operations.FirstOrDefault(o =>
                        o.Description?.Contains(operationType, StringComparison.OrdinalIgnoreCase) == true);

                    if (match != null)
                    {
                        var learnedHours = match.ScaledLaborHours + match.ScaledRefinishHours;
                        if (learnedHours > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SmartAnalyzer] Using learned hours for {partName}/{operationType}: {learnedHours} (default was {defaultHours})");
                            return learnedHours;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartAnalyzer] Error getting learned hours: {ex.Message}");
            }

            return defaultHours;
        }

        /// <summary>
        /// Load knowledge base JSON files
        /// </summary>
        private void LoadKnowledgeBases()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dataDir = Path.Combine(baseDir, "Data");

                // Load CommonlyMissedItems.json
                var commonlyMissedPath = Path.Combine(dataDir, "CommonlyMissedItems.json");
                if (File.Exists(commonlyMissedPath))
                {
                    var json = File.ReadAllText(commonlyMissedPath);
                    _commonlyMissed = JsonSerializer.Deserialize<SmartCommonlyMissedData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    System.Diagnostics.Debug.WriteLine($"[SmartAnalyzer] Loaded {_commonlyMissed?.OperationChecks?.Count ?? 0} operation checks");
                }

                // Load ADASRequirements.json
                var adasPath = Path.Combine(dataDir, "ADASRequirements.json");
                if (File.Exists(adasPath))
                {
                    var json = File.ReadAllText(adasPath);
                    _adasRequirements = JsonSerializer.Deserialize<SmartADASRequirementsData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    System.Diagnostics.Debug.WriteLine($"[SmartAnalyzer] Loaded {_adasRequirements?.CalibrationTriggers?.Count ?? 0} ADAS triggers");
                }

                _isLoaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartAnalyzer] Error loading knowledge bases: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyze an estimate and return suggestions for commonly missed items
        /// </summary>
        public AnalysisResult AnalyzeEstimate(List<ParsedEstimateLine> lines)
        {
            var result = new AnalysisResult();

            if (!_isLoaded || lines == null || lines.Count == 0)
                return result;

            // Calculate estimate total for threshold checks
            decimal estimateTotal = lines.Sum(l => l.Price);

            // Detect what types of work are on the estimate
            bool hasRefinishOperations = lines.Any(l =>
                l.OperationType?.Contains("Refn", StringComparison.OrdinalIgnoreCase) == true ||
                l.OperationType?.Contains("Paint", StringComparison.OrdinalIgnoreCase) == true ||
                l.OperationType?.Contains("Blnd", StringComparison.OrdinalIgnoreCase) == true ||
                l.RefinishHours > 0);

            bool hasWeldingOperations = lines.Any(l =>
                l.Description?.Contains("weld", StringComparison.OrdinalIgnoreCase) == true ||
                (l.OperationType?.Contains("Replace", StringComparison.OrdinalIgnoreCase) == true &&
                (l.Description?.Contains("quarter", StringComparison.OrdinalIgnoreCase) == true ||
                 l.Description?.Contains("roof", StringComparison.OrdinalIgnoreCase) == true ||
                 l.Description?.Contains("rocker", StringComparison.OrdinalIgnoreCase) == true ||
                 l.Description?.Contains("pillar", StringComparison.OrdinalIgnoreCase) == true ||
                 l.Description?.Contains("rail", StringComparison.OrdinalIgnoreCase) == true)));

            // Detect ADAS-related work — only front-facing components (windshield, front bumper,
            // mirrors, cameras, sensors, radar, grille). Rear bumper alone does NOT trigger ADAS.
            bool hasADASComponents = lines.Any(l =>
            {
                var desc = l.Description?.ToLowerInvariant() ?? "";
                var part = l.PartName?.ToLowerInvariant() ?? "";
                var combined = desc + " " + part;

                // These always trigger ADAS regardless of position
                if (combined.Contains("windshield") || combined.Contains("camera") ||
                    combined.Contains("sensor") || combined.Contains("radar") ||
                    combined.Contains("grille") || combined.Contains("mirror"))
                    return true;

                // Bumper/fascia only trigger if FRONT (rear bumper doesn't have ADAS sensors)
                if (combined.Contains("bumper") || combined.Contains("fascia"))
                {
                    // Exclude if explicitly rear
                    if (combined.Contains("rear") || combined.Contains("rr ") ||
                        combined.Contains("r bumper") || combined.Contains("r. bumper"))
                        return false;
                    // Front bumper or generic bumper → ADAS
                    return true;
                }

                return false;
            });

            // Detect electrical work (lights, sensors, wiring)
            bool hasElectricalComponents = lines.Any(l =>
            {
                var desc = l.Description?.ToLowerInvariant() ?? "";
                return desc.Contains("headl") || desc.Contains("tail") || desc.Contains("lamp") ||
                       desc.Contains("light") || desc.Contains("sensor") || desc.Contains("camera") ||
                       desc.Contains("wiring") || desc.Contains("harness") || desc.Contains("module") ||
                       desc.Contains("fog") || desc.Contains("turn signal");
            });

            // Detect any panel replacement (triggers battery disconnect for sensor-equipped panels)
            bool hasReplaceOperations = lines.Any(l =>
                l.OperationType?.Contains("Repl", StringComparison.OrdinalIgnoreCase) == true ||
                l.OperationType?.Contains("New", StringComparison.OrdinalIgnoreCase) == true);

            // Detect mechanical work (suspension, steering, alignment)
            bool hasMechanicalComponents = lines.Any(l =>
            {
                var desc = l.Description?.ToLowerInvariant() ?? "";
                return desc.Contains("strut") || desc.Contains("shock") || desc.Contains("control arm") ||
                       desc.Contains("suspension") || desc.Contains("tie rod") || desc.Contains("ball joint") ||
                       desc.Contains("wheel") || desc.Contains("steering") || desc.Contains("alignment") ||
                       desc.Contains("subframe") || desc.Contains("cradle");
            });

            // Track what we've already suggested to avoid duplicates
            var suggestedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Analyze each line against knowledge bases (skip vehicle description lines)
            foreach (var line in lines.Where(l => !l.IsManualLine &&
                !EstimateScoringService.IsVehicleInfoLine(l.PartName ?? "")))
            {
                var lineText = $"{line.Description} {line.PartName}".ToLowerInvariant();
                var opType = line.OperationType?.ToLowerInvariant() ?? "";

                // Check CommonlyMissedItems
                if (_commonlyMissed?.OperationChecks != null)
                {
                    foreach (var kvp in _commonlyMissed.OperationChecks)
                    {
                        var check = kvp.Value;
                        if (MatchesTrigger(lineText, opType, check.TriggerKeywords, check.TriggerOperations))
                        {
                            foreach (var item in check.MissedItems ?? new List<SmartMissedItem>())
                            {
                                // Skip static blend items — replaced by dynamic adjacency-based blend suggestions
                                if (IsStaticBlendSuggestion(item)) continue;

                                var itemKey = $"{check.Operation}|{item.Item}";
                                if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                                {
                                    suggestedItems.Add(itemKey);
                                    result.Suggestions.Add(new SmartSuggestedOperation
                                    {
                                        Category = GetSuggestionCategory(item.Category),
                                        SourcePart = line.PartName ?? line.Description ?? "",
                                        SourceOperation = line.OperationType ?? "",
                                        Item = item.Item,
                                        Description = item.Description,
                                        WhyNeeded = item.WhyNeeded,
                                        DegReference = item.DegReference,
                                        LaborHours = item.LaborHours,
                                        TypicalCost = item.TypicalCost,
                                        Priority = item.Priority ?? "medium",
                                        SuggestionType = "CommonlyMissed"
                                    });
                                }
                            }
                        }
                    }
                }

                // Check ADAS Requirements — skip lines that are battery/electrical SOP
                // (e.g., "Charge and Maintain Battery during ADAS" should NOT trigger calibration)
                var isElectricalLine = lineText.Contains("battery") || lineText.Contains("disconnect") ||
                    lineText.Contains("electronic") || lineText.Contains("electrical") ||
                    lineText.Contains("charge and maintain");
                if (!isElectricalLine && _adasRequirements?.CalibrationTriggers != null)
                {
                    foreach (var kvp in _adasRequirements.CalibrationTriggers)
                    {
                        var trigger = kvp.Value;
                        if (MatchesTriggerSimple(lineText, trigger.TriggerKeywords))
                        {
                            var calibRequired = trigger.CalibrationRequired?.ToString() ?? "false";
                            if (calibRequired == "true" || calibRequired == "True" || calibRequired == "verify" || calibRequired == "if_equipped")
                            {
                                var itemKey = $"ADAS|{trigger.Operation}";
                                var adasItem = $"{trigger.Operation} - ADAS Calibration";
                                if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, adasItem) && !IsAlreadyOnEstimateFuzzy(lines, trigger.Operation))
                                {
                                    suggestedItems.Add(itemKey);

                                    // Add calibration suggestion
                                    var sensors = string.Join(", ", trigger.AffectedSensors ?? new List<string>());
                                    result.Suggestions.Add(new SmartSuggestedOperation
                                    {
                                        Category = SuggestionCategory.Calibration,
                                        SourcePart = line.PartName ?? line.Description ?? "",
                                        SourceOperation = line.OperationType ?? "",
                                        Item = $"{trigger.Operation} - ADAS Calibration",
                                        Description = trigger.Explanation,
                                        WhyNeeded = trigger.OemNotes,
                                        LaborHours = trigger.TypicalTime,
                                        TypicalCost = trigger.TypicalCost,
                                        Priority = calibRequired == "true" || calibRequired == "True" ? "critical" : "high",
                                        SuggestionType = "ADAS",
                                        AffectedSensors = sensors,
                                        CalibrationType = trigger.CalibrationType
                                    });
                                }
                            }
                        }
                    }
                }
            }

            // Global checks
            if (_commonlyMissed?.GlobalChecks != null)
            {
                // Estimate over threshold
                if (_commonlyMissed.GlobalChecks.TryGetValue("estimateOver2500", out var overThreshold))
                {
                    if (estimateTotal >= (overThreshold.Threshold ?? 2500))
                    {
                        foreach (var item in overThreshold.Checks ?? new List<SmartMissedItem>())
                        {
                            var itemKey = $"Global|{item.Item}";
                            if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                            {
                                suggestedItems.Add(itemKey);
                                result.Suggestions.Add(new SmartSuggestedOperation
                                {
                                    Category = SuggestionCategory.Diagnostic,
                                    SourcePart = $"Estimate Total ${estimateTotal:N0}",
                                    SourceOperation = "",
                                    Item = item.Item,
                                    Description = item.Description,
                                    WhyNeeded = item.WhyNeeded,
                                    DegReference = item.DegReference,
                                    LaborHours = item.LaborHours,
                                    Priority = item.Priority ?? "high",
                                    SuggestionType = "GlobalCheck"
                                });
                            }
                        }
                    }
                }

                // Any refinish operations
                if (hasRefinishOperations && _commonlyMissed.GlobalChecks.TryGetValue("anyRefinish", out var refinishCheck))
                {
                    foreach (var item in refinishCheck.Checks ?? new List<SmartMissedItem>())
                    {
                        var itemKey = $"Refinish|{item.Item}";
                        if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                        {
                            suggestedItems.Add(itemKey);
                            result.Suggestions.Add(new SmartSuggestedOperation
                            {
                                Category = SuggestionCategory.Refinish,
                                SourcePart = "Refinish Operations",
                                SourceOperation = "",
                                Item = item.Item,
                                Description = item.Description,
                                WhyNeeded = item.WhyNeeded,
                                LaborHours = item.LaborHours,
                                TypicalCost = item.TypicalCost,
                                Priority = item.Priority ?? "medium",
                                SuggestionType = "GlobalCheck"
                            });
                        }
                    }
                }

                // Any welding operations
                if (hasWeldingOperations && _commonlyMissed.GlobalChecks.TryGetValue("anyWelding", out var weldingCheck))
                {
                    foreach (var item in weldingCheck.Checks ?? new List<SmartMissedItem>())
                    {
                        var itemKey = $"Welding|{item.Item}";
                        if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                        {
                            suggestedItems.Add(itemKey);
                            result.Suggestions.Add(new SmartSuggestedOperation
                            {
                                Category = SuggestionCategory.Electrical,
                                SourcePart = "Welding Operations",
                                SourceOperation = "",
                                Item = item.Item,
                                Description = item.Description,
                                WhyNeeded = item.WhyNeeded,
                                LaborHours = item.LaborHours,
                                Priority = item.Priority ?? "high",
                                SuggestionType = "GlobalCheck"
                            });
                        }
                    }
                }

                // ADAS-equipped vehicles
                if (hasADASComponents && _commonlyMissed.GlobalChecks.TryGetValue("anyADASVehicle", out var adasCheck))
                {
                    foreach (var item in adasCheck.Checks ?? new List<SmartMissedItem>())
                    {
                        var itemKey = $"ADAS|{item.Item}";
                        if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                        {
                            suggestedItems.Add(itemKey);
                            result.Suggestions.Add(new SmartSuggestedOperation
                            {
                                Category = GetSuggestionCategory(item.Category),
                                SourcePart = "ADAS Components Detected",
                                SourceOperation = "",
                                Item = item.Item,
                                Description = item.Description,
                                WhyNeeded = item.WhyNeeded,
                                DegReference = item.DegReference,
                                LaborHours = item.LaborHours,
                                Priority = item.Priority ?? "high",
                                SuggestionType = "GlobalCheck"
                            });
                        }
                    }
                }

                // Electrical components
                if (hasElectricalComponents && _commonlyMissed.GlobalChecks.TryGetValue("anyElectricalWork", out var elecCheck))
                {
                    foreach (var item in elecCheck.Checks ?? new List<SmartMissedItem>())
                    {
                        var itemKey = $"Electrical|{item.Item}";
                        if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                        {
                            suggestedItems.Add(itemKey);
                            result.Suggestions.Add(new SmartSuggestedOperation
                            {
                                Category = SuggestionCategory.Electrical,
                                SourcePart = "Electrical Components",
                                SourceOperation = "",
                                Item = item.Item,
                                Description = item.Description,
                                WhyNeeded = item.WhyNeeded,
                                LaborHours = item.LaborHours,
                                Priority = item.Priority ?? "medium",
                                SuggestionType = "GlobalCheck"
                            });
                        }
                    }
                }

                // Mechanical components
                if (hasMechanicalComponents && _commonlyMissed.GlobalChecks.TryGetValue("anyMechanicalWork", out var mechCheck))
                {
                    foreach (var item in mechCheck.Checks ?? new List<SmartMissedItem>())
                    {
                        var itemKey = $"Mechanical|{item.Item}";
                        if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                        {
                            suggestedItems.Add(itemKey);
                            result.Suggestions.Add(new SmartSuggestedOperation
                            {
                                Category = SuggestionCategory.Mechanical,
                                SourcePart = "Mechanical Work",
                                SourceOperation = "",
                                Item = item.Item,
                                Description = item.Description,
                                WhyNeeded = item.WhyNeeded,
                                LaborHours = item.LaborHours,
                                Priority = item.Priority ?? "medium",
                                SuggestionType = "GlobalCheck"
                            });
                        }
                    }
                }

                // Any replace operation (battery disconnect for sensor-equipped panels)
                if (hasReplaceOperations && _commonlyMissed.GlobalChecks.TryGetValue("anyReplace", out var replaceCheck))
                {
                    foreach (var item in replaceCheck.Checks ?? new List<SmartMissedItem>())
                    {
                        var itemKey = $"Replace|{item.Item}";
                        if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                        {
                            suggestedItems.Add(itemKey);
                            result.Suggestions.Add(new SmartSuggestedOperation
                            {
                                Category = SuggestionCategory.Electrical,
                                SourcePart = "Panel Replacement",
                                SourceOperation = "",
                                Item = item.Item,
                                Description = item.Description,
                                WhyNeeded = item.WhyNeeded,
                                LaborHours = item.LaborHours,
                                Priority = item.Priority ?? "high",
                                SuggestionType = "GlobalCheck"
                            });
                        }
                    }
                }

                // Miscellaneous (always applicable)
                if (_commonlyMissed.GlobalChecks.TryGetValue("miscellaneous", out var miscCheck))
                {
                    foreach (var item in miscCheck.Checks ?? new List<SmartMissedItem>())
                    {
                        var itemKey = $"Misc|{item.Item}";
                        if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                        {
                            suggestedItems.Add(itemKey);
                            result.Suggestions.Add(new SmartSuggestedOperation
                            {
                                Category = SuggestionCategory.Other,
                                SourcePart = "Standard Operations",
                                SourceOperation = "",
                                Item = item.Item,
                                Description = item.Description,
                                WhyNeeded = item.WhyNeeded,
                                LaborHours = item.LaborHours,
                                Priority = item.Priority ?? "low",
                                SuggestionType = "GlobalCheck"
                            });
                        }
                    }
                }
            }

            // Dynamic adjacency-based blend suggestions
            var blendSuggestions = GenerateAdjacentBlendSuggestions(lines);
            foreach (var blend in blendSuggestions)
            {
                var itemKey = $"DynamicBlend|{blend.Item}";
                if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, blend.Item))
                {
                    suggestedItems.Add(itemKey);
                    result.Suggestions.Add(blend);
                }
            }

            // Sort by priority
            result.Suggestions = result.Suggestions
                .OrderBy(s => GetPriorityOrder(s.Priority))
                .ThenBy(s => s.Category)
                .ToList();

            // Generate summary
            result.EstimateTotal = estimateTotal;
            result.LineCount = lines.Count;
            result.HasRefinish = hasRefinishOperations;
            result.HasWelding = hasWeldingOperations;
            result.CriticalCount = result.Suggestions.Count(s => s.Priority == "critical");
            result.HighPriorityCount = result.Suggestions.Count(s => s.Priority == "high");

            System.Diagnostics.Debug.WriteLine($"[SmartAnalyzer] Found {result.Suggestions.Count} suggestions ({result.CriticalCount} critical, {result.HighPriorityCount} high)");

            return result;
        }

        /// <summary>
        /// Find commonly missed items triggered by the given estimate lines.
        /// Returns items not already present on the estimate, with their trigger source.
        /// Used by the scrubber to produce the "Commonly Missed" clipboard section.
        /// </summary>
        public List<(string TriggerPart, SmartMissedItem Item, string Source)> FindMissedItemsForLines(List<ParsedEstimateLine> lines)
        {
            var results = new List<(string TriggerPart, SmartMissedItem Item, string Source)>();
            if (!_isLoaded || lines == null || lines.Count == 0)
                return results;

            var suggestedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Per-line operation checks
            if (_commonlyMissed?.OperationChecks != null)
            {
                foreach (var line in lines.Where(l => !l.IsManualLine))
                {
                    var lineText = $"{line.Description} {line.PartName}".ToLowerInvariant();
                    var opType = line.OperationType?.ToLowerInvariant() ?? "";

                    foreach (var kvp in _commonlyMissed.OperationChecks)
                    {
                        var check = kvp.Value;
                        if (MatchesTrigger(lineText, opType, check.TriggerKeywords, check.TriggerOperations))
                        {
                            foreach (var item in check.MissedItems ?? new List<SmartMissedItem>())
                            {
                                if (IsStaticBlendSuggestion(item)) continue;
                                var itemKey = $"{check.Operation}|{item.Item}";
                                if (!suggestedItems.Contains(itemKey) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                                {
                                    suggestedItems.Add(itemKey);
                                    var trigger = $"{line.PartName ?? line.Description ?? ""} {line.OperationType ?? ""}".Trim();
                                    results.Add((trigger, item, check.Operation ?? ""));
                                }
                            }
                        }
                    }
                }
            }

            // Global checks
            if (_commonlyMissed?.GlobalChecks != null)
            {
                decimal estimateTotal = lines.Sum(l => l.Price);
                bool hasRefinish = lines.Any(l =>
                    l.OperationType?.Contains("Refn", StringComparison.OrdinalIgnoreCase) == true ||
                    l.OperationType?.Contains("Paint", StringComparison.OrdinalIgnoreCase) == true ||
                    l.RefinishHours > 0);
                bool hasWelding = lines.Any(l =>
                    l.Description?.Contains("weld", StringComparison.OrdinalIgnoreCase) == true ||
                    (l.OperationType?.Contains("Replace", StringComparison.OrdinalIgnoreCase) == true &&
                    (l.Description?.Contains("quarter", StringComparison.OrdinalIgnoreCase) == true ||
                     l.Description?.Contains("roof", StringComparison.OrdinalIgnoreCase) == true ||
                     l.Description?.Contains("rocker", StringComparison.OrdinalIgnoreCase) == true ||
                     l.Description?.Contains("pillar", StringComparison.OrdinalIgnoreCase) == true ||
                     l.Description?.Contains("rail", StringComparison.OrdinalIgnoreCase) == true)));
                bool hasReplace = lines.Any(l =>
                    l.OperationType?.Contains("Repl", StringComparison.OrdinalIgnoreCase) == true ||
                    l.OperationType?.Contains("New", StringComparison.OrdinalIgnoreCase) == true);

                var globalConditions = new (string Key, bool Condition, string Source)[]
                {
                    ("anyRefinish", hasRefinish, "Refinish Operations"),
                    ("anyWelding", hasWelding, "Welding Operations"),
                    ("anyReplace", hasReplace, "Panel Replacement"),
                    ("miscellaneous", true, "Standard Operations"),
                };

                // Estimate over threshold
                if (_commonlyMissed.GlobalChecks.TryGetValue("estimateOver2500", out var overThreshold))
                {
                    if (estimateTotal >= (overThreshold.Threshold ?? 2500))
                        AddGlobalItems(overThreshold.Checks, $"Estimate ${estimateTotal:N0}", lines, suggestedItems, results);
                }

                foreach (var (key, condition, source) in globalConditions)
                {
                    if (condition && _commonlyMissed.GlobalChecks.TryGetValue(key, out var check))
                        AddGlobalItems(check.Checks, source, lines, suggestedItems, results);
                }
            }

            return results;
        }

        private void AddGlobalItems(List<SmartMissedItem>? items, string source,
            List<ParsedEstimateLine> lines, HashSet<string> suggested,
            List<(string TriggerPart, SmartMissedItem Item, string Source)> results)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                var key = $"Global|{item.Item}";
                if (!suggested.Contains(key) && !IsAlreadyOnEstimateFuzzy(lines, item.Item))
                {
                    suggested.Add(key);
                    results.Add((source, item, source));
                }
            }
        }

        /// <summary>
        /// Check if a line matches trigger keywords and operations
        /// </summary>
        private bool MatchesTrigger(string lineText, string opType, List<string>? keywords, List<string>? operations)
        {
            if (keywords == null || keywords.Count == 0)
                return false;

            // Check if any keyword matches (word-boundary aware)
            bool keywordMatch = keywords.Any(k => EstimateScoringService.IsWordMatch(lineText, k));
            if (!keywordMatch) return false;

            // If no operation filter, keyword match is enough
            if (operations == null || operations.Count == 0)
                return true;

            // Check if operation matches
            return operations.Any(op => opType.Contains(op.ToLowerInvariant()));
        }

        private bool MatchesTriggerSimple(string lineText, List<string>? keywords)
        {
            if (keywords == null || keywords.Count == 0)
                return false;
            return keywords.Any(k => EstimateScoringService.IsWordMatch(lineText, k));
        }

        /// <summary>
        /// Check if an item is already on the estimate (simple match)
        /// </summary>
        private bool IsAlreadyOnEstimate(List<ParsedEstimateLine> lines, string item)
        {
            return IsAlreadyOnEstimateFuzzy(lines, item);
        }

        /// <summary>
        /// Fuzzy matching keywords for common operations
        /// </summary>
        private static readonly Dictionary<string, string[]> _fuzzyMatchKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            { "adhesion promoter", new[] { "adhesion", "ad pro", "adpro", "promoter" } },
            { "flex additive", new[] { "flexitive", "flex add" } },
            { "pre-repair scan", new[] { "pre scan", "prescan", "pre-scan", "diagnostic scan" } },
            { "post-repair scan", new[] { "post scan", "postscan", "post-scan" } },
            { "color tint", new[] { "tint", "color match", "tinting" } },
            { "blend", new[] { "blnd", "blending" } },
            { "corrosion protection", new[] { "corrosion", "cavity wax", "rust protection", "anti-corrosion" } },
            { "seam sealer", new[] { "seam seal", "sealer" } },
            { "weld-thru primer", new[] { "weld thru", "weld through", "welding primer" } },
            { "denib", new[] { "de-nib", "de nib", "nib sand" } },
            { "clear coat", new[] { "clearcoat" } },
            { "battery disconnect", new[] { "disconnect battery" } },
            { "4-wheel alignment", new[] { "alignment", "4 wheel", "four wheel", "wheel align" } },
            { "feather, prime & block", new[] { "feather prime", "fpb" } },
            { "r&i", new[] { "r+i", "r/i" } },
            { "transfer", new[] { "xfer" } },
            { "calibration", new[] { "calibrate", "calib" } },
            { "headlamp", new[] { "headlight", "head lamp", "head light" } },
            { "memory saver", new[] { "ks-100", "keep alive", "keep-alive", "battery support" } },
            { "module programming", new[] { "reprogram", "initialization", "initialize", "relearn", "idle relearn" } },
            { "drive cycle", new[] { "test drive", "road test" } },
            { "oem repair procedures", new[] { "oem research", "oem procedure", "repair procedure" } },
            { "clean for delivery", new[] { "final clean", "detail clean" } },
            { "hazardous waste", new[] { "haz waste", "hazmat", "waste disposal" } },
            { "battery reconnect", new[] { "reconnect battery", "battery initialize", "battery init" } }
        };

        /// <summary>
        /// Check if an item is already on the estimate using fuzzy matching
        /// </summary>
        private bool IsAlreadyOnEstimateFuzzy(List<ParsedEstimateLine> lines, string item)
        {
            var itemLower = item.ToLowerInvariant();

            // Get all keywords to search for
            var searchTerms = new List<string> { itemLower };

            // Add fuzzy match alternatives (only direct key match — no cascading expansion)
            foreach (var kvp in _fuzzyMatchKeywords)
            {
                if (itemLower.Contains(kvp.Key.ToLowerInvariant()))
                {
                    searchTerms.AddRange(kvp.Value);
                }
            }

            // Search all lines for any matching terms
            foreach (var line in lines)
            {
                var desc = line.Description?.ToLowerInvariant() ?? "";
                var part = line.PartName?.ToLowerInvariant() ?? "";
                var combined = $"{desc} {part}";

                foreach (var term in searchTerms.Distinct())
                {
                    if (combined.Contains(term))
                    {
                        System.Diagnostics.Debug.WriteLine($"[FuzzyMatch] '{item}' matched by '{term}' in '{combined.Substring(0, Math.Min(50, combined.Length))}'");
                        return true;
                    }
                }
            }

            return false;
        }

        private SuggestionCategory GetSuggestionCategory(string? category)
        {
            return category?.ToLowerInvariant() switch
            {
                "materials" => SuggestionCategory.Materials,
                "r&i" => SuggestionCategory.RAndI,
                "labor" => SuggestionCategory.Labor,
                "refinish" => SuggestionCategory.Refinish,
                "diagnostic" => SuggestionCategory.Diagnostic,
                "calibration" => SuggestionCategory.Calibration,
                "mechanical" => SuggestionCategory.Mechanical,
                "electrical" => SuggestionCategory.Electrical,
                _ => SuggestionCategory.Other
            };
        }

        private int GetPriorityOrder(string? priority)
        {
            return priority?.ToLowerInvariant() switch
            {
                "critical" => 0,
                "high" => 1,
                "medium" => 2,
                "low" => 3,
                _ => 4
            };
        }

        #region Panel Adjacency Blend Logic

        /// <summary>
        /// Check if a static missed item from JSON is a generic blend suggestion (replaced by dynamic adjacency logic)
        /// </summary>
        private bool IsStaticBlendSuggestion(SmartMissedItem item)
        {
            if (string.IsNullOrEmpty(item.Item)) return false;
            var itemLower = item.Item.ToLowerInvariant();
            // Match generic static blend items like "Blend Adjacent Panels", "Adjacent Panel Blend", "Blend Fenders", "Blend Hood", "Blend Door"
            return (itemLower.Contains("blend") && (itemLower.Contains("adjacent") || itemLower.Contains("fender") || itemLower.Contains("hood") || itemLower.Contains("door")))
                || (itemLower.Contains("adjacent") && itemLower.Contains("blend"));
        }

        /// <summary>
        /// Normalize a parsed estimate line to a canonical panel name with side info
        /// </summary>
        private NormalizedPanel? NormalizePanel(ParsedEstimateLine line)
        {
            var desc = line.Description?.Trim() ?? "";
            var part = line.PartName?.Trim() ?? "";
            var combined = $"{part} {desc}".ToLowerInvariant();

            string? canonicalName = null;

            // Try direct alias match (longest match first for specificity)
            foreach (var alias in _panelNameAliases.OrderByDescending(a => a.Key.Length))
            {
                if (combined.Contains(alias.Key.ToLowerInvariant()))
                {
                    canonicalName = alias.Value;
                    break;
                }
            }

            // Try direct adjacency map match if no alias matched
            if (canonicalName == null)
            {
                foreach (var panel in _panelAdjacencyMap.Keys)
                {
                    if (combined.Contains(panel.ToLowerInvariant()))
                    {
                        canonicalName = panel;
                        break;
                    }
                }
            }

            if (canonicalName == null) return null;

            // Resolve side from line fields and text
            var side = ResolveSide(line, combined);

            // Handle ambiguous "door" — check for front/rear context
            if (canonicalName == "front door" || canonicalName == "rear door")
            {
                // Already specific, keep as-is
            }
            else if (combined.Contains("door") && !combined.Contains("front") && !combined.Contains("rear")
                     && !combined.Contains("fr ") && !combined.Contains("rr "))
            {
                // Ambiguous "door" — try position field
                var pos = line.Position?.ToLowerInvariant() ?? "";
                if (pos.Contains("front") || pos.Contains("fr"))
                    canonicalName = "front door";
                else if (pos.Contains("rear") || pos.Contains("rr"))
                    canonicalName = "rear door";
                // else remains whatever was matched
            }

            // Handle ambiguous "bumper"
            if (combined.Contains("bumper") && canonicalName != "front bumper" && canonicalName != "rear bumper")
            {
                if (combined.Contains("front") || combined.Contains("fr "))
                    canonicalName = "front bumper";
                else if (combined.Contains("rear") || combined.Contains("rr "))
                    canonicalName = "rear bumper";
            }

            return new NormalizedPanel
            {
                CanonicalName = canonicalName,
                Side = side,
                Position = line.Position ?? ""
            };
        }

        /// <summary>
        /// Resolve the side (Left/Right/"") from line fields and text context
        /// </summary>
        private string ResolveSide(ParsedEstimateLine line, string combinedLower)
        {
            // Check explicit Side field first
            var side = line.Side?.Trim() ?? "";
            if (!string.IsNullOrEmpty(side))
            {
                if (side.Contains("L", StringComparison.OrdinalIgnoreCase) || side.Contains("Left", StringComparison.OrdinalIgnoreCase))
                    return "Left";
                if (side.Contains("R", StringComparison.OrdinalIgnoreCase) || side.Contains("Right", StringComparison.OrdinalIgnoreCase))
                    return "Right";
            }

            // Check Position field
            var pos = line.Position?.ToLowerInvariant() ?? "";
            if (pos.Contains("left") || pos.Contains("lh") || pos.Contains("l/s") || pos.Contains("driver"))
                return "Left";
            if (pos.Contains("right") || pos.Contains("rh") || pos.Contains("r/s") || pos.Contains("passenger") || pos.Contains("pass"))
                return "Right";

            // Check description text
            if (combinedLower.Contains(" lh ") || combinedLower.Contains(" left ") || combinedLower.Contains("left ") || combinedLower.Contains(" l/s") || combinedLower.Contains("driver"))
                return "Left";
            if (combinedLower.Contains(" rh ") || combinedLower.Contains(" right ") || combinedLower.Contains("right ") || combinedLower.Contains(" r/s") || combinedLower.Contains("passenger") || combinedLower.Contains("pass"))
                return "Right";

            return "";
        }

        /// <summary>
        /// Classify an operation type to determine if it triggers blend on adjacent panels
        /// </summary>
        private BlendTriggerSeverity ClassifyBlendTrigger(string? operationType)
        {
            if (string.IsNullOrEmpty(operationType)) return BlendTriggerSeverity.NoBlendNeeded;

            var op = operationType.ToLowerInvariant();

            // Replace or Repair → adjacent panels need blend
            if (op.Contains("repl") || op.Contains("new") || op.Contains("repair") || op.Contains("rpr"))
                return BlendTriggerSeverity.TriggerBlend;

            // Blend → already blending, no cascade
            if (op.Contains("blend") || op.Contains("blnd"))
                return BlendTriggerSeverity.AlreadyBlending;

            // Refinish/Paint → full paint, no additional blend needed
            if (op.Contains("refn") || op.Contains("refinish") || op.Contains("paint"))
                return BlendTriggerSeverity.AlreadyRefinished;

            // R&I, Sublet, Other → no blend needed
            return BlendTriggerSeverity.NoBlendNeeded;
        }

        /// <summary>
        /// Generate dynamic blend suggestions based on panel adjacency relationships
        /// </summary>
        private List<SmartSuggestedOperation> GenerateAdjacentBlendSuggestions(List<ParsedEstimateLine> lines)
        {
            var suggestions = new List<SmartSuggestedOperation>();

            // Step 1: Normalize all lines to panels
            var panelLines = new List<(NormalizedPanel Panel, ParsedEstimateLine Line, BlendTriggerSeverity Severity)>();
            foreach (var line in lines.Where(l => !l.IsManualLine))
            {
                var normalized = NormalizePanel(line);
                if (normalized == null) continue;

                var severity = ClassifyBlendTrigger(line.OperationType);
                panelLines.Add((normalized, line, severity));
            }

            // Step 2: Build "covered" set — panels already getting paint work
            var coveredPanels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (panel, line, severity) in panelLines)
            {
                if (severity == BlendTriggerSeverity.TriggerBlend ||
                    severity == BlendTriggerSeverity.AlreadyBlending ||
                    severity == BlendTriggerSeverity.AlreadyRefinished)
                {
                    // Include side in key for sided panels
                    var key = FormatPanelKey(panel);
                    coveredPanels.Add(key);
                    System.Diagnostics.Debug.WriteLine($"[BlendAdj] Covered: {key} ({severity})");
                }
            }

            // Step 3: For each line that triggers blend, check adjacent panels
            var suggestedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (panel, line, severity) in panelLines)
            {
                if (severity != BlendTriggerSeverity.TriggerBlend) continue;

                if (!_panelAdjacencyMap.TryGetValue(panel.CanonicalName, out var adjacencyInfo)) continue;

                foreach (var adj in adjacencyInfo.AdjacentPanels)
                {
                    var adjSides = ResolveAdjacentSides(panel, adj);

                    foreach (var adjSide in adjSides)
                    {
                        var adjKey = _centerPanels.Contains(adj.Name)
                            ? adj.Name
                            : $"{adjSide} {adj.Name}".Trim();

                        // Skip if already covered by existing work
                        if (coveredPanels.Contains(adjKey)) continue;

                        // Skip if we already suggested this blend
                        if (suggestedKeys.Contains(adjKey)) continue;
                        suggestedKeys.Add(adjKey);

                        // Look up default hours for the adjacent panel
                        var adjHours = _panelAdjacencyMap.TryGetValue(adj.Name, out var adjInfo)
                            ? adjInfo.DefaultBlendHours
                            : 1.0m;

                        // Try learned hours
                        var blendItemName = $"Blend {adjKey}";
                        adjHours = GetLearnedHours(adj.Name, "Blend", adjHours);

                        var sourcePart = FormatSourceLabel(panel);

                        suggestions.Add(new SmartSuggestedOperation
                        {
                            Category = SuggestionCategory.Refinish,
                            SourcePart = sourcePart,
                            SourceOperation = line.OperationType ?? "",
                            Item = blendItemName,
                            Description = $"Blend {adjKey} for color match — adjacent to {sourcePart}",
                            WhyNeeded = "New or repaired paint won't match aged paint on adjacent panels without blending",
                            LaborHours = adjHours,
                            Priority = adj.Priority,
                            SuggestionType = "AdjacentBlend"
                        });

                        System.Diagnostics.Debug.WriteLine($"[BlendAdj] Suggest: {blendItemName} ({adjHours}h, {adj.Priority}) from {sourcePart}");
                    }
                }
            }

            return suggestions;
        }

        /// <summary>
        /// Determine which sides an adjacent panel needs blend for, based on source panel side and relationship
        /// </summary>
        private List<string> ResolveAdjacentSides(NormalizedPanel sourcePanel, AdjacentPanel adj)
        {
            // Center panels have no side
            if (_centerPanels.Contains(adj.Name))
                return new List<string> { "" };

            switch (adj.SideRelationship)
            {
                case SideRelationship.SameSide:
                    // Adjacent panel is on the same side as the source
                    return new List<string> { sourcePanel.Side };

                case SideRelationship.BothSides:
                    // Source triggers blend on both sides (e.g., hood → both fenders)
                    if (_centerPanels.Contains(sourcePanel.CanonicalName))
                        return new List<string> { "Left", "Right" };
                    // If source itself is sided, just same side
                    return new List<string> { sourcePanel.Side };

                case SideRelationship.Center:
                    return new List<string> { "" };

                default:
                    return new List<string> { sourcePanel.Side };
            }
        }

        private string FormatPanelKey(NormalizedPanel panel)
        {
            if (_centerPanels.Contains(panel.CanonicalName))
                return panel.CanonicalName;
            return string.IsNullOrEmpty(panel.Side)
                ? panel.CanonicalName
                : $"{panel.Side} {panel.CanonicalName}";
        }

        private string FormatSourceLabel(NormalizedPanel panel)
        {
            if (_centerPanels.Contains(panel.CanonicalName))
                return panel.CanonicalName;
            return string.IsNullOrEmpty(panel.Side)
                ? panel.CanonicalName
                : $"{panel.Side} {panel.CanonicalName}";
        }

        #endregion
    }

    #region Panel Adjacency Models

    public enum SideRelationship
    {
        SameSide,
        Center,
        BothSides
    }

    public enum BlendTriggerSeverity
    {
        TriggerBlend,
        AlreadyBlending,
        AlreadyRefinished,
        NoBlendNeeded
    }

    public class AdjacentPanel
    {
        public string Name { get; set; } = "";
        public SideRelationship SideRelationship { get; set; }
        public string Priority { get; set; } = "high";
    }

    public class PanelAdjacencyInfo
    {
        public string CanonicalName { get; set; } = "";
        public List<AdjacentPanel> AdjacentPanels { get; set; } = new();
        public decimal DefaultBlendHours { get; set; }
        public bool IsPaintable { get; set; } = true;
    }

    public class NormalizedPanel
    {
        public string CanonicalName { get; set; } = "";
        public string Side { get; set; } = "";
        public string Position { get; set; } = "";
    }

    #endregion

    #region Data Models

    public class AnalysisResult
    {
        public List<SmartSuggestedOperation> Suggestions { get; set; } = new();
        public decimal EstimateTotal { get; set; }
        public int LineCount { get; set; }
        public bool HasRefinish { get; set; }
        public bool HasWelding { get; set; }
        public int CriticalCount { get; set; }
        public int HighPriorityCount { get; set; }
    }

    public enum SuggestionCategory
    {
        Calibration,
        Diagnostic,
        Electrical,
        Materials,
        RAndI,
        Labor,
        Refinish,
        Mechanical,
        Other
    }

    public class SmartSuggestedOperation
    {
        public SuggestionCategory Category { get; set; }
        public string SourcePart { get; set; } = "";
        public string SourceOperation { get; set; } = "";
        public string Item { get; set; } = "";
        public string? Description { get; set; }
        public string? WhyNeeded { get; set; }
        public string? DegReference { get; set; }
        public decimal LaborHours { get; set; }
        public decimal TypicalCost { get; set; }
        public string Priority { get; set; } = "medium";
        public string SuggestionType { get; set; } = "";
        public string? AffectedSensors { get; set; }
        public string? CalibrationType { get; set; }
        public bool IsSelected { get; set; } = false;
    }

    // JSON Models for CommonlyMissedItems.json (prefixed to avoid conflicts)
    public class SmartCommonlyMissedData
    {
        public string? Version { get; set; }
        public Dictionary<string, SmartOperationCheck>? OperationChecks { get; set; }
        public Dictionary<string, SmartGlobalCheck>? GlobalChecks { get; set; }
    }

    public class SmartOperationCheck
    {
        public string? Operation { get; set; }
        public List<string>? TriggerKeywords { get; set; }
        public List<string>? TriggerOperations { get; set; }
        public List<SmartMissedItem>? MissedItems { get; set; }
    }

    public class SmartGlobalCheck
    {
        public decimal? Threshold { get; set; }
        public string? Condition { get; set; }
        public List<SmartMissedItem>? Checks { get; set; }
    }

    public class SmartMissedItem
    {
        public string Item { get; set; } = "";
        public string? Category { get; set; }
        public string? Description { get; set; }
        public string? WhyNeeded { get; set; }
        public string? DegReference { get; set; }
        public decimal TypicalCost { get; set; }
        public decimal LaborHours { get; set; }
        public decimal LaborHoursMultiplier { get; set; }
        public string? Priority { get; set; }
        public string? Conditional { get; set; }
    }

    // JSON Models for ADASRequirements.json (prefixed to avoid conflicts)
    public class SmartADASRequirementsData
    {
        public string? Version { get; set; }
        public Dictionary<string, SmartSensorType>? SensorTypes { get; set; }
        public Dictionary<string, SmartCalibrationTrigger>? CalibrationTriggers { get; set; }
    }

    public class SmartSensorType
    {
        public string? Name { get; set; }
        public string? Location { get; set; }
        public List<string>? CommonNames { get; set; }
        public string? CalibrationType { get; set; }
    }

    public class SmartCalibrationTrigger
    {
        public string? Operation { get; set; }
        public List<string>? TriggerKeywords { get; set; }
        public List<string>? AffectedSensors { get; set; }
        [JsonPropertyName("calibrationRequired")]
        public object? CalibrationRequired { get; set; }
        public string? CalibrationType { get; set; }
        public string? Explanation { get; set; }
        public string? OemNotes { get; set; }
        public decimal TypicalCost { get; set; }
        public decimal TypicalTime { get; set; }
    }

    #endregion
}
