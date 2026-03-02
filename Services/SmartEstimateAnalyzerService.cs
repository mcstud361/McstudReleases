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

            // Detect ADAS-related work (bumper, windshield, mirrors, cameras, sensors)
            bool hasADASComponents = lines.Any(l =>
            {
                var desc = l.Description?.ToLowerInvariant() ?? "";
                var part = l.PartName?.ToLowerInvariant() ?? "";
                return desc.Contains("bumper") || desc.Contains("windshield") || desc.Contains("mirror") ||
                       desc.Contains("camera") || desc.Contains("sensor") || desc.Contains("radar") ||
                       desc.Contains("grille") || desc.Contains("fascia") ||
                       part.Contains("bumper") || part.Contains("windshield") || part.Contains("mirror");
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

            // Analyze each line against knowledge bases
            foreach (var line in lines.Where(l => !l.IsManualLine))
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

                // Check ADAS Requirements
                if (_adasRequirements?.CalibrationTriggers != null)
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
                                if (!suggestedItems.Contains(itemKey))
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
        /// Check if a line matches trigger keywords and operations
        /// </summary>
        private bool MatchesTrigger(string lineText, string opType, List<string>? keywords, List<string>? operations)
        {
            if (keywords == null || keywords.Count == 0)
                return false;

            // Check if any keyword matches
            bool keywordMatch = keywords.Any(k => lineText.Contains(k.ToLowerInvariant()));
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
            return keywords.Any(k => lineText.Contains(k.ToLowerInvariant()));
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
            { "flex additive", new[] { "flex", "flexitive", "flex add" } },
            { "pre-repair scan", new[] { "pre scan", "prescan", "pre-scan", "diagnostic scan" } },
            { "post-repair scan", new[] { "post scan", "postscan", "post-scan" } },
            { "color tint", new[] { "tint", "color match", "tinting" } },
            { "blend", new[] { "blnd", "blending" } },
            { "corrosion protection", new[] { "corrosion", "cavity wax", "rust protection", "anti-corrosion" } },
            { "seam sealer", new[] { "seam seal", "sealer" } },
            { "weld-thru primer", new[] { "weld thru", "weld through", "welding primer" } },
            { "denib", new[] { "de-nib", "de nib", "nib sand" } },
            { "clear coat", new[] { "clearcoat", "clear" } },
            { "battery disconnect", new[] { "disconnect battery", "battery" } },
            { "4-wheel alignment", new[] { "alignment", "4 wheel", "four wheel", "wheel align" } },
            { "feather, prime & block", new[] { "feather", "prime", "block", "fpb" } },
            { "r&i", new[] { "remove", "install", "r+i", "r/i" } },
            { "transfer", new[] { "xfer" } },
            { "calibration", new[] { "calibrate", "calib" } },
            { "headlamp", new[] { "headlight", "head lamp", "head light" } }
        };

        /// <summary>
        /// Check if an item is already on the estimate using fuzzy matching
        /// </summary>
        private bool IsAlreadyOnEstimateFuzzy(List<ParsedEstimateLine> lines, string item)
        {
            var itemLower = item.ToLowerInvariant();

            // Get all keywords to search for
            var searchTerms = new List<string> { itemLower };

            // Add fuzzy match alternatives
            foreach (var kvp in _fuzzyMatchKeywords)
            {
                if (itemLower.Contains(kvp.Key.ToLowerInvariant()))
                {
                    searchTerms.AddRange(kvp.Value);
                }
            }

            // Also check if item words appear in the fuzzy keywords
            var itemWords = itemLower.Split(' ', '-', '/', '&').Where(w => w.Length > 2).ToList();
            foreach (var word in itemWords)
            {
                foreach (var kvp in _fuzzyMatchKeywords)
                {
                    if (kvp.Key.ToLowerInvariant().Contains(word) || kvp.Value.Any(v => v.Contains(word)))
                    {
                        searchTerms.Add(kvp.Key.ToLowerInvariant());
                        searchTerms.AddRange(kvp.Value);
                    }
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
    }

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
