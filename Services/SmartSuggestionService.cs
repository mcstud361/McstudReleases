#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using McStudDesktop.Services;

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

                    foreach (var manual in pattern.ManualLines
                        .Where(m => m.TimesUsed >= 2) // Only suggest items seen in 2+ estimates
                        .Where(m => m.LaborUnits > 0 || m.RefinishUnits > 0 || m.AvgPrice > 0 || m.Price > 0) // Must have actionable data
                        .Where(m => (m.Description?.Length ?? 0) >= 5 && (m.Description?.Length ?? 0) <= 100) // Not fragments or boilerplate
                        .OrderByDescending(m => m.TimesUsed).Take(10))
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

            // Strategy 3: Category pooling — when per-part data is thin
            if (suggestions.ManualOperations.Count < 3)
            {
                var rulesEngine = OperationRulesEngine.Instance;
                var category = rulesEngine.GetPartOperationCategory(partName, operationType);

                if (category != PartOperationCategory.Unknown)
                {
                    var categoryPattern = _learningService.GetManualLinesForCategory(category, partName);
                    if (categoryPattern != null && categoryPattern.ManualLines.Count > 0)
                    {
                        var partsCount = _learningService.GetCategoryPartsCount(category);
                        var categoryName = OperationRulesEngine.GetCategoryDisplayName(category);
                        suggestions.CategoryDescription = $"From {categoryPattern.ExampleCount} similar {categoryName} estimates across {partsCount} parts";

                        // Existing per-part descriptions for dedup
                        var existingDescs = suggestions.ManualOperations
                            .Select(m => m.Description.ToLowerInvariant())
                            .ToHashSet();

                        foreach (var manual in categoryPattern.ManualLines
                            .Where(m => m.TimesUsed >= 2)
                            .Where(m => m.LaborUnits > 0 || m.RefinishUnits > 0 || m.AvgPrice > 0 || m.Price > 0)
                            .Where(m => (m.Description?.Length ?? 0) >= 5 && (m.Description?.Length ?? 0) <= 100)
                            .OrderByDescending(m => m.TimesUsed).Take(10))
                        {
                            var desc = !string.IsNullOrEmpty(manual.ManualLineType) ? manual.ManualLineType : manual.Description;
                            if (existingDescs.Contains(desc.ToLowerInvariant()))
                                continue;

                            existingDescs.Add(desc.ToLowerInvariant());

                            suggestions.CategoryOperations.Add(new SmartSuggestionOp
                            {
                                Description = desc,
                                OperationType = !string.IsNullOrEmpty(manual.LaborType) ? manual.LaborType : "Add",
                                LaborHours = manual.LaborUnits,
                                RefinishHours = manual.RefinishUnits,
                                Price = manual.AvgPrice > 0 ? manual.AvgPrice : manual.Price,
                                Confidence = categoryPattern.Confidence * 0.8,
                                TimesUsed = manual.TimesUsed,
                                Source = "Category",
                                CategoryLabel = categoryName,
                                Reason = $"Used {manual.TimesUsed}x across {partsCount} {categoryName} parts",
                                Priority = manual.TimesUsed
                            });
                        }
                    }
                }
            }

            // Strategy 4: Baseline fallback from rules engine when no learned data exists
            if (suggestions.ManualOperations.Count == 0 && suggestions.CategoryOperations.Count == 0)
            {
                var rulesEngine = OperationRulesEngine.Instance;
                var baselineOps = rulesEngine.GetSuggestedOperations(partName, operationType);
                if (baselineOps.Count > 0)
                {
                    suggestions.MatchType = "Baseline";
                    suggestions.MatchDescription = "Common operations for this part type (no learned data yet)";

                    foreach (var baseOp in baselineOps)
                    {
                        suggestions.CategoryOperations.Add(new SmartSuggestionOp
                        {
                            Description = baseOp.Description,
                            OperationType = baseOp.OperationType,
                            LaborHours = baseOp.DefaultHours,
                            Source = "Rules Engine",
                            CategoryLabel = "Common Operations",
                            Reason = baseOp.WhyNeeded,
                            Priority = 0
                        });
                    }
                }
            }

            // If still no data at all, return empty with message
            if (suggestions.ManualOperations.Count == 0 && suggestions.CategoryOperations.Count == 0)
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

        /// <summary>
        /// Get suggestions with AI-powered dedup. Falls back to standard string dedup if AI is unavailable.
        /// </summary>
        public async Task<PartSuggestions> GetSuggestionsForPartAsync(string partName, string operationType, string? vehicleInfo = null)
        {
            var suggestions = GetSuggestionsForPart(partName, operationType, vehicleInfo);

            // Only use AI dedup if we have enough suggestions to warrant it
            var allOps = suggestions.ManualOperations.Concat(suggestions.CategoryOperations).ToList();
            if (allOps.Count >= 3)
            {
                var deduped = await TryAiDedupAsync(allOps);
                if (deduped != null)
                {
                    // Separate back into manual vs category
                    var dedupedDescs = new HashSet<string>(deduped.Select(d => d.Description), StringComparer.OrdinalIgnoreCase);
                    suggestions.ManualOperations.RemoveAll(o => !dedupedDescs.Contains(o.Description));
                    suggestions.CategoryOperations.RemoveAll(o => !dedupedDescs.Contains(o.Description));
                    suggestions.CalculateTotals();
                }
            }

            return suggestions;
        }

        /// <summary>
        /// Use AI to detect semantically duplicate suggestions that string matching would miss.
        /// Returns the deduplicated list, or null on failure.
        /// </summary>
        private async Task<List<SmartSuggestionOp>?> TryAiDedupAsync(List<SmartSuggestionOp> operations)
        {
            try
            {
                var apiService = ClaudeApiService.Instance;
                var systemPrompt = @"You are a collision repair estimating assistant. Given a list of suggested operations, identify duplicates — operations that describe the same work even if worded differently.

Examples of duplicates:
- ""Blend adjacent panel"" and ""Blend"" (same operation)
- ""R&I headlamp"" and ""Remove & install headlight"" (same work)
- ""Corrosion protection"" and ""Anti-corrosion treatment"" (same work)

Return a JSON array of the UNIQUE operation descriptions to keep (remove the duplicates, keeping the more descriptive version). Return ONLY the JSON array of strings.";

                var opList = string.Join("\n", operations.Select((o, i) => $"{i + 1}. {o.Description} ({o.OperationType})"));
                var response = await apiService.SendAsync(systemPrompt, opList, AiFeature.SuggestionDedup, 512);
                if (response == null) return null;

                var text = ClaudeApiService.StripCodeFences(response.Text);
                var keepDescs = JsonSerializer.Deserialize<List<string>>(text, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (keepDescs == null || keepDescs.Count == 0) return null;

                // Map back to the original operations by matching descriptions
                var keepSet = new HashSet<string>(keepDescs, StringComparer.OrdinalIgnoreCase);
                var result = operations.Where(o => keepSet.Contains(o.Description)).ToList();

                // If AI removed too many (>50%), something went wrong — return null to use fallback
                if (result.Count < operations.Count * 0.5)
                    return null;

                var removed = operations.Count - result.Count;
                if (removed > 0)
                    System.Diagnostics.Debug.WriteLine($"[SmartSuggestion] AI dedup removed {removed} duplicates from {operations.Count} suggestions");

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartSuggestion] AI dedup failed: {ex.Message}");
                return null;
            }
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

        // Category-pooled suggestions (from similar part types)
        public List<SmartSuggestionOp> CategoryOperations { get; set; } = new();
        public string CategoryDescription { get; set; } = "";

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

        public bool HasData => ManualOperations.Count > 0 || CategoryOperations.Count > 0;
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
        public string Source { get; set; } = "Learned"; // "Learned", "Category", or "Rules Engine"
        public string CategoryLabel { get; set; } = "";
        public string Reason { get; set; } = "";
        public int Priority { get; set; }
    }

    #endregion
}
