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
    /// Supplement Detector Service - Identifies commonly missed items based on operations
    ///
    /// Analyzes an estimate and suggests supplements that are often overlooked:
    /// - When bumper cover repair → Check flex additive, adhesion promoter, R&I items
    /// - When quarter panel replace → Check corrosion protection, seam sealer, weld-thru primer
    /// - When any refinish → Check 3-stage, clear coat, denib
    /// - Global checks based on estimate total
    /// </summary>
    public class SupplementDetectorService
    {
        private readonly string _dataFilePath;
        private CommonlyMissedItemsData? _missedItemsData;

        private static SupplementDetectorService? _instance;
        public static SupplementDetectorService Instance => _instance ??= new SupplementDetectorService();

        public SupplementDetectorService()
        {
            _dataFilePath = Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "CommonlyMissedItems.json"
            );
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _missedItemsData = JsonSerializer.Deserialize<CommonlyMissedItemsData>(json, options);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SupplementDetector] Load error: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyze an estimate and return suggested supplements
        /// </summary>
        public SupplementAnalysisResult AnalyzeEstimate(EstimateAnalysisInput input)
        {
            var result = new SupplementAnalysisResult();

            if (_missedItemsData == null)
            {
                result.Warnings.Add("Could not load commonly missed items database");
                return result;
            }

            // Check each operation in the estimate
            foreach (var operation in input.Operations)
            {
                var suggestions = CheckOperation(operation, input.Operations);
                result.Suggestions.AddRange(suggestions);
            }

            // Global checks based on estimate total
            if (input.EstimateTotal >= 2500)
            {
                var globalSuggestions = CheckGlobalThresholds(input);
                result.Suggestions.AddRange(globalSuggestions);
            }

            // Check for any refinish operations
            if (input.Operations.Any(o => IsRefinishOperation(o)))
            {
                var refinishGlobals = CheckRefinishGlobals(input);
                result.Suggestions.AddRange(refinishGlobals);
            }

            // Check for any welding operations
            if (input.Operations.Any(o => IsWeldingOperation(o)))
            {
                var weldingGlobals = CheckWeldingGlobals(input);
                result.Suggestions.AddRange(weldingGlobals);
            }

            // Remove duplicates and already-included items
            result.Suggestions = DeduplicateSuggestions(result.Suggestions, input.Operations);

            // Calculate totals
            result.TotalSuggestedLabor = result.Suggestions.Sum(s => s.LaborHours);
            result.TotalSuggestedMaterials = result.Suggestions.Sum(s => s.MaterialCost);
            result.TotalSuggestedValue = result.Suggestions.Sum(s => s.EstimatedValue);

            // Categorize by priority
            result.CriticalItems = result.Suggestions.Where(s => s.Priority == "critical").ToList();
            result.HighPriorityItems = result.Suggestions.Where(s => s.Priority == "high").ToList();
            result.MediumPriorityItems = result.Suggestions.Where(s => s.Priority == "medium").ToList();
            result.LowPriorityItems = result.Suggestions.Where(s => s.Priority == "low").ToList();

            return result;
        }

        /// <summary>
        /// Check a single operation for missing items
        /// </summary>
        private List<SupplementSuggestion> CheckOperation(EstimateOperation operation, List<EstimateOperation> allOperations)
        {
            var suggestions = new List<SupplementSuggestion>();

            if (_missedItemsData?.OperationChecks == null) return suggestions;

            foreach (var checkKvp in _missedItemsData.OperationChecks)
            {
                var check = checkKvp.Value;

                // Check if this operation matches the trigger
                if (MatchesTrigger(operation, check))
                {
                    foreach (var missedItem in check.MissedItems)
                    {
                        // Check if this item is already in the estimate
                        if (!IsItemAlreadyIncluded(missedItem.Item, allOperations))
                        {
                            var suggestion = new SupplementSuggestion
                            {
                                Item = missedItem.Item,
                                Category = missedItem.Category,
                                Description = missedItem.Description,
                                WhyNeeded = missedItem.WhyNeeded,
                                DegReference = missedItem.DegReference,
                                LaborHours = missedItem.LaborHours,
                                MaterialCost = missedItem.TypicalCost,
                                Priority = missedItem.Priority,
                                TriggerOperation = $"{operation.OperationType} {operation.PartName}",
                                IsConditional = !string.IsNullOrEmpty(missedItem.Conditional)
                            };

                            // Calculate estimated value (labor @ $60/hr + materials)
                            suggestion.EstimatedValue = (suggestion.LaborHours * 60m) + suggestion.MaterialCost;

                            suggestions.Add(suggestion);
                        }
                    }
                }
            }

            return suggestions;
        }

        /// <summary>
        /// Check if operation matches a trigger definition
        /// </summary>
        private bool MatchesTrigger(EstimateOperation operation, OperationCheck check)
        {
            var partNameLower = operation.PartName.ToLowerInvariant();
            var opTypeLower = operation.OperationType.ToLowerInvariant();
            var descLower = operation.Description?.ToLowerInvariant() ?? "";

            // Check if part name matches any trigger keyword
            bool partMatches = check.TriggerKeywords.Any(kw =>
                partNameLower.Contains(kw.ToLower()) || descLower.Contains(kw.ToLower()));

            if (!partMatches) return false;

            // Check if operation type matches
            bool opMatches = check.TriggerOperations.Any(op =>
                opTypeLower.Contains(op.ToLower()) || opTypeLower.StartsWith(op.ToLower()));

            return opMatches;
        }

        /// <summary>
        /// Check if an item is already in the estimate
        /// </summary>
        private bool IsItemAlreadyIncluded(string item, List<EstimateOperation> operations)
        {
            var itemLower = item.ToLowerInvariant();

            return operations.Any(op =>
            {
                var descLower = op.Description?.ToLowerInvariant() ?? "";
                var partLower = op.PartName.ToLowerInvariant();

                // Exact or partial match
                return descLower.Contains(itemLower) ||
                       partLower.Contains(itemLower) ||
                       // Check common abbreviations
                       (itemLower.Contains("flex") && descLower.Contains("flex")) ||
                       (itemLower.Contains("adhesion") && descLower.Contains("adhesion")) ||
                       (itemLower.Contains("corrosion") && descLower.Contains("corrosion")) ||
                       (itemLower.Contains("weld-thru") && (descLower.Contains("weld") && descLower.Contains("primer"))) ||
                       (itemLower.Contains("scan") && descLower.Contains("scan"));
            });
        }

        /// <summary>
        /// Check global thresholds (estimate over $2500)
        /// </summary>
        private List<SupplementSuggestion> CheckGlobalThresholds(EstimateAnalysisInput input)
        {
            var suggestions = new List<SupplementSuggestion>();

            if (_missedItemsData?.GlobalChecks?.EstimateOver2500 == null) return suggestions;

            var threshold = _missedItemsData.GlobalChecks.EstimateOver2500;

            if (input.EstimateTotal >= threshold.Threshold)
            {
                foreach (var check in threshold.Checks)
                {
                    if (!IsItemAlreadyIncluded(check.Item, input.Operations))
                    {
                        suggestions.Add(new SupplementSuggestion
                        {
                            Item = check.Item,
                            Category = check.Category,
                            Description = check.Description,
                            WhyNeeded = check.WhyNeeded,
                            DegReference = check.DegReference,
                            LaborHours = check.LaborHours,
                            Priority = check.Priority,
                            TriggerOperation = $"Estimate total over ${threshold.Threshold:N0}",
                            EstimatedValue = check.LaborHours * 60m
                        });
                    }
                }
            }

            return suggestions;
        }

        /// <summary>
        /// Check refinish global items
        /// </summary>
        private List<SupplementSuggestion> CheckRefinishGlobals(EstimateAnalysisInput input)
        {
            var suggestions = new List<SupplementSuggestion>();

            if (_missedItemsData?.GlobalChecks?.AnyRefinish == null) return suggestions;

            foreach (var check in _missedItemsData.GlobalChecks.AnyRefinish.Checks)
            {
                if (!IsItemAlreadyIncluded(check.Item, input.Operations))
                {
                    suggestions.Add(new SupplementSuggestion
                    {
                        Item = check.Item,
                        Category = check.Category,
                        Description = check.Description,
                        WhyNeeded = check.WhyNeeded,
                        MaterialCost = check.TypicalCost,
                        Priority = check.Priority,
                        TriggerOperation = "Refinish operations present",
                        EstimatedValue = check.TypicalCost
                    });
                }
            }

            return suggestions;
        }

        /// <summary>
        /// Check welding global items
        /// </summary>
        private List<SupplementSuggestion> CheckWeldingGlobals(EstimateAnalysisInput input)
        {
            var suggestions = new List<SupplementSuggestion>();

            if (_missedItemsData?.GlobalChecks?.AnyWelding == null) return suggestions;

            foreach (var check in _missedItemsData.GlobalChecks.AnyWelding.Checks)
            {
                if (!IsItemAlreadyIncluded(check.Item, input.Operations))
                {
                    suggestions.Add(new SupplementSuggestion
                    {
                        Item = check.Item,
                        Category = check.Category,
                        Description = check.Description,
                        WhyNeeded = check.WhyNeeded,
                        LaborHours = check.LaborHours,
                        Priority = check.Priority,
                        TriggerOperation = "Welding operations present",
                        EstimatedValue = check.LaborHours * 60m
                    });
                }
            }

            return suggestions;
        }

        private bool IsRefinishOperation(EstimateOperation op)
        {
            var opLower = op.OperationType.ToLowerInvariant();
            return opLower.Contains("refinish") || opLower.Contains("paint") ||
                   opLower.Contains("rfn") || opLower.Contains("blend");
        }

        private bool IsWeldingOperation(EstimateOperation op)
        {
            var opLower = op.OperationType.ToLowerInvariant();
            var partLower = op.PartName.ToLowerInvariant();

            // Structural panels that require welding
            var weldedParts = new[] { "quarter panel", "roof", "rocker", "pillar", "rail", "floor" };

            return (opLower.Contains("replace") || opLower.Contains("section")) &&
                   weldedParts.Any(wp => partLower.Contains(wp));
        }

        /// <summary>
        /// Remove duplicate suggestions
        /// </summary>
        private List<SupplementSuggestion> DeduplicateSuggestions(
            List<SupplementSuggestion> suggestions,
            List<EstimateOperation> existingOps)
        {
            return suggestions
                .GroupBy(s => s.Item.ToLowerInvariant())
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>
        /// Get a quick checklist for a specific operation type
        /// </summary>
        public List<string> GetQuickChecklist(string operationType, string partName)
        {
            var checklist = new List<string>();

            if (_missedItemsData?.OperationChecks == null) return checklist;

            foreach (var checkKvp in _missedItemsData.OperationChecks)
            {
                var check = checkKvp.Value;
                var partLower = partName.ToLowerInvariant();
                var opLower = operationType.ToLowerInvariant();

                bool partMatches = check.TriggerKeywords.Any(kw => partLower.Contains(kw.ToLower()));
                bool opMatches = check.TriggerOperations.Any(op => opLower.Contains(op.ToLower()));

                if (partMatches && opMatches)
                {
                    foreach (var item in check.MissedItems)
                    {
                        checklist.Add($"[ ] {item.Item} - {item.Description}");
                    }
                }
            }

            return checklist;
        }
    }

    #region Data Models

    public class CommonlyMissedItemsData
    {
        public string Version { get; set; } = "";
        public string LastUpdated { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, OperationCheck> OperationChecks { get; set; } = new();
        public GlobalChecksData? GlobalChecks { get; set; }
    }

    public class OperationCheck
    {
        public string Operation { get; set; } = "";
        public string[] TriggerKeywords { get; set; } = Array.Empty<string>();
        public string[] TriggerOperations { get; set; } = Array.Empty<string>();
        public List<MissedItem> MissedItems { get; set; } = new();
    }

    public class MissedItem
    {
        public string Item { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string WhyNeeded { get; set; } = "";
        public string? DegReference { get; set; }
        public decimal TypicalCost { get; set; }
        public decimal LaborHours { get; set; }
        public decimal LaborHoursMultiplier { get; set; }
        public string Priority { get; set; } = "medium";
        public string? Conditional { get; set; }
    }

    public class GlobalChecksData
    {
        public ThresholdCheck? EstimateOver2500 { get; set; }
        public ConditionalCheck? AnyRefinish { get; set; }
        public ConditionalCheck? AnyWelding { get; set; }
    }

    public class ThresholdCheck
    {
        public decimal Threshold { get; set; }
        public List<MissedItem> Checks { get; set; } = new();
    }

    public class ConditionalCheck
    {
        public string Condition { get; set; } = "";
        public List<MissedItem> Checks { get; set; } = new();
    }

    // Input/Output models
    public class EstimateAnalysisInput
    {
        public List<EstimateOperation> Operations { get; set; } = new();
        public decimal EstimateTotal { get; set; }
        public bool Is3StagePaint { get; set; }
        public string? VehicleMake { get; set; }
        public string? VehicleModel { get; set; }
        public int? VehicleYear { get; set; }
    }

    public class EstimateOperation
    {
        public string OperationType { get; set; } = "";
        public string PartName { get; set; } = "";
        public string? Description { get; set; }
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
    }

    public class SupplementAnalysisResult
    {
        public List<SupplementSuggestion> Suggestions { get; set; } = new();
        public List<SupplementSuggestion> CriticalItems { get; set; } = new();
        public List<SupplementSuggestion> HighPriorityItems { get; set; } = new();
        public List<SupplementSuggestion> MediumPriorityItems { get; set; } = new();
        public List<SupplementSuggestion> LowPriorityItems { get; set; } = new();

        public decimal TotalSuggestedLabor { get; set; }
        public decimal TotalSuggestedMaterials { get; set; }
        public decimal TotalSuggestedValue { get; set; }

        public List<string> Warnings { get; set; } = new();
    }

    public class SupplementSuggestion
    {
        public string Item { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string WhyNeeded { get; set; } = "";
        public string? DegReference { get; set; }
        public decimal LaborHours { get; set; }
        public decimal MaterialCost { get; set; }
        public decimal EstimatedValue { get; set; }
        public string Priority { get; set; } = "medium";
        public string TriggerOperation { get; set; } = "";
        public bool IsConditional { get; set; }
    }

    #endregion
}
