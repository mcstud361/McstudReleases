#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Smart Suggestion Service - Pattern-based suggestions from learned estimates ONLY
    ///
    /// NO hardcoded knowledge - everything comes from uploaded estimates.
    /// When you input a part + operation (like "Bumper Cover Replace"), this service:
    /// 1. Finds similar estimates by Year/Make/Model first
    /// 2. Shows what manual lines those estimates had with frequency
    /// 3. Falls back to any estimate with same operation if no YMM match
    /// </summary>
    public class SmartSuggestionService
    {
        private static SmartSuggestionService? _instance;
        public static SmartSuggestionService Instance => _instance ??= new SmartSuggestionService();

        private readonly EstimateLearningService _learningService;

        public SmartSuggestionService()
        {
            _learningService = EstimateLearningService.Instance;
        }

        /// <summary>
        /// Get suggestions for a part + operation based ONLY on learned data
        /// </summary>
        public PartSuggestions GetSuggestionsForPart(string partName, string operationType, string? vehicleInfo = null)
        {
            var suggestions = new PartSuggestions
            {
                PartName = partName,
                OperationType = operationType,
                VehicleInfo = vehicleInfo ?? ""
            };

            // Strategy 1: If vehicle info provided, try YMM-based matching first
            if (!string.IsNullOrEmpty(vehicleInfo))
            {
                var ymmResult = _learningService.FindSimilarEstimates(vehicleInfo, partName, operationType);
                if (ymmResult.HasData)
                {
                    suggestions.MatchType = ymmResult.MatchType;
                    suggestions.MatchCount = ymmResult.TotalEstimatesFound;
                    suggestions.MatchDescription = $"Based on {ymmResult.TotalEstimatesFound} {ymmResult.MatchType} estimate(s)";

                    // Add aggregated manual lines with frequency
                    foreach (var agg in ymmResult.AggregatedManualLines)
                    {
                        suggestions.ManualOperations.Add(new SmartSuggestionOp
                        {
                            Description = agg.Description,
                            OperationType = "Add",
                            LaborHours = agg.AvgLaborHours,
                            RefinishHours = agg.AvgRefinishHours,
                            Price = agg.AvgPrice,
                            Confidence = agg.Frequency,
                            TimesUsed = agg.Count,
                            Source = "Learned",
                            Reason = $"{agg.FrequencyDisplay} estimates had this",
                            Priority = (int)(agg.Frequency * 100)
                        });
                    }
                }
            }

            // Strategy 2: Fall back to general manual line patterns (no YMM filter)
            if (suggestions.ManualOperations.Count == 0)
            {
                var pattern = _learningService.GetManualLinesForPart(partName, operationType);
                if (pattern != null && pattern.ManualLines.Count > 0)
                {
                    suggestions.MatchType = "Pattern Match";
                    suggestions.MatchCount = pattern.ExampleCount;
                    suggestions.MatchDescription = $"Based on {pattern.ExampleCount} learned pattern(s)";

                    foreach (var manual in pattern.ManualLines.OrderByDescending(m => m.TimesUsed).Take(15))
                    {
                        // Don't duplicate
                        if (suggestions.ManualOperations.Any(m =>
                            m.Description.Equals(manual.Description, StringComparison.OrdinalIgnoreCase) ||
                            m.Description.Equals(manual.ManualLineType, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        suggestions.ManualOperations.Add(new SmartSuggestionOp
                        {
                            Description = !string.IsNullOrEmpty(manual.ManualLineType) ? manual.ManualLineType : manual.Description,
                            OperationType = !string.IsNullOrEmpty(manual.LaborType) ? manual.LaborType : "Add",
                            LaborHours = manual.LaborUnits,
                            RefinishHours = manual.RefinishUnits,
                            Price = manual.AvgPrice,
                            Confidence = pattern.Confidence,
                            TimesUsed = manual.TimesUsed,
                            Source = "Learned",
                            Reason = $"Used {manual.TimesUsed}x across {pattern.ExampleCount} estimate(s)",
                            Priority = manual.TimesUsed
                        });
                    }
                }
            }

            // If still no data, return empty with message
            if (suggestions.ManualOperations.Count == 0)
            {
                suggestions.MatchType = "No Data";
                suggestions.MatchCount = 0;
                suggestions.MatchDescription = "No learned data for this combination yet. Upload estimates to learn patterns.";
            }

            // Calculate totals
            suggestions.CalculateTotals();

            return suggestions;
        }

        /// <summary>
        /// Get suggestions with current vehicle context
        /// </summary>
        public PartSuggestions GetSuggestionsForCurrentVehicle(string partName, string operationType)
        {
            // Try to get current vehicle from most recent estimate
            // For now, just call without vehicle info
            return GetSuggestionsForPart(partName, operationType, null);
        }
    }

    #region Data Models

    public class PartSuggestions
    {
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string VehicleInfo { get; set; } = "";

        // Match info
        public string MatchType { get; set; } = ""; // "Exact YMM", "Similar Vehicle", "Pattern Match", "No Data"
        public int MatchCount { get; set; }
        public string MatchDescription { get; set; } = "";

        // Suggestions from learned data
        public List<SmartSuggestionOp> ManualOperations { get; set; } = new();

        // Note: RelatedParts and CommonlyMissed removed - we only show what was actually learned
        // These could be added back if we track which parts appear together in estimates

        // Totals
        public decimal TotalLaborHours { get; set; }
        public decimal TotalRefinishHours { get; set; }
        public decimal EstimatedValue { get; set; }

        public void CalculateTotals()
        {
            TotalLaborHours = ManualOperations.Sum(o => o.LaborHours);
            TotalRefinishHours = ManualOperations.Sum(o => o.RefinishHours);
            EstimatedValue = ManualOperations.Sum(o => o.Price);
        }

        public bool HasData => ManualOperations.Count > 0;
    }

    public class SmartSuggestionOp
    {
        public string Description { get; set; } = "";
        public string OperationType { get; set; } = "Add"; // Add, Rpr, Refn, etc.
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public double Confidence { get; set; }
        public int TimesUsed { get; set; }
        public string Source { get; set; } = "Learned"; // Always "Learned" now
        public string Reason { get; set; } = "";
        public int Priority { get; set; }
    }

    #endregion
}
