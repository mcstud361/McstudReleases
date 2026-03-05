#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Tracks estimate accuracy over time by monitoring supplement patterns.
    /// Builds a personalized "heatmap" of where the estimator consistently
    /// under or over estimates, enabling data-driven improvement.
    /// </summary>
    public class EstimateAccuracyService
    {
        private const string DataFileName = "estimate_accuracy.json";
        private AccuracyData _data;
        private readonly object _lock = new object();
        private readonly string _dataFilePath;

        private static EstimateAccuracyService? _instance;
        public static EstimateAccuracyService Instance => _instance ??= new EstimateAccuracyService();

        public EstimateAccuracyService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(appDataPath);
            _dataFilePath = Path.Combine(appDataPath, DataFileName);
            _data = LoadData();
        }

        #region Recording Methods

        /// <summary>
        /// Record feedback for an estimate - was it accurate or supplemented?
        /// </summary>
        public void RecordEstimateFeedback(EstimateFeedback feedback)
        {
            lock (_lock)
            {
                feedback.Timestamp = DateTime.Now;
                feedback.UserId = GetCurrentUserId();
                _data.Feedbacks.Add(feedback);
                SaveData();
            }
        }

        /// <summary>
        /// Quick record - estimate was accurate, no supplement needed
        /// </summary>
        public void RecordAccurate(string roNumber, string? vehicleInfo = null)
        {
            RecordEstimateFeedback(new EstimateFeedback
            {
                RONumber = roNumber,
                VehicleInfo = vehicleInfo ?? "",
                WasAccurate = true,
                SupplementItems = new List<SupplementedItem>()
            });
        }

        /// <summary>
        /// Record that specific items were supplemented
        /// </summary>
        public void RecordSupplement(string roNumber, List<SupplementedItem> items, string? vehicleInfo = null)
        {
            RecordEstimateFeedback(new EstimateFeedback
            {
                RONumber = roNumber,
                VehicleInfo = vehicleInfo ?? "",
                WasAccurate = false,
                SupplementItems = items
            });
        }

        /// <summary>
        /// Quick supplement recording by category
        /// </summary>
        public void RecordSupplementByCategory(string roNumber, string partCategory,
            decimal hoursAdded, decimal materialsAdded = 0, string? notes = null)
        {
            var item = new SupplementedItem
            {
                PartCategory = partCategory,
                HoursAdded = hoursAdded,
                MaterialsAdded = materialsAdded,
                Notes = notes ?? ""
            };

            RecordEstimateFeedback(new EstimateFeedback
            {
                RONumber = roNumber,
                WasAccurate = false,
                SupplementItems = new List<SupplementedItem> { item }
            });
        }

        #endregion

        #region Analysis Methods

        /// <summary>
        /// Get accuracy summary for chatbot response
        /// </summary>
        public AccuracyAnalysis GetAccuracyAnalysis(int? lastNEstimates = null)
        {
            var feedbacks = _data.Feedbacks
                .Where(f => string.IsNullOrEmpty(f.UserId) || f.UserId == GetCurrentUserId())
                .OrderByDescending(f => f.Timestamp)
                .ToList();

            if (lastNEstimates.HasValue)
                feedbacks = feedbacks.Take(lastNEstimates.Value).ToList();

            var analysis = new AccuracyAnalysis
            {
                TotalEstimates = feedbacks.Count,
                AccurateCount = feedbacks.Count(f => f.WasAccurate),
                SupplementedCount = feedbacks.Count(f => !f.WasAccurate)
            };

            if (analysis.TotalEstimates == 0)
                return analysis;

            analysis.OverallAccuracyRate = (double)analysis.AccurateCount / analysis.TotalEstimates;

            // Analyze by part category
            var supplementedItems = feedbacks
                .Where(f => !f.WasAccurate)
                .SelectMany(f => f.SupplementItems)
                .ToList();

            var byCategory = supplementedItems
                .GroupBy(s => NormalizeCategory(s.PartCategory))
                .Select(g => new CategoryAccuracy
                {
                    Category = g.Key,
                    SupplementCount = g.Count(),
                    TotalHoursAdded = g.Sum(s => s.HoursAdded),
                    TotalMaterialsAdded = g.Sum(s => s.MaterialsAdded),
                    AverageHoursAdded = g.Average(s => s.HoursAdded),
                    CommonMissedItems = g
                        .Where(s => !string.IsNullOrEmpty(s.Notes))
                        .GroupBy(s => s.Notes)
                        .OrderByDescending(ng => ng.Count())
                        .Take(3)
                        .Select(ng => ng.Key!)
                        .ToList()
                })
                .OrderByDescending(c => c.SupplementCount)
                .ToList();

            analysis.ByCategory = byCategory;

            // Identify problem areas (supplemented > 50% of the time for that category)
            analysis.ProblemAreas = byCategory
                .Where(c => c.SupplementCount >= 3) // Need at least 3 data points
                .Take(5)
                .ToList();

            // Identify strong areas (rarely supplemented)
            var allCategories = feedbacks
                .SelectMany(f => f.SupplementItems.Select(s => NormalizeCategory(s.PartCategory)))
                .Concat(feedbacks.Where(f => f.WasAccurate && !string.IsNullOrEmpty(f.PrimaryCategory))
                    .Select(f => NormalizeCategory(f.PrimaryCategory!)))
                .Distinct()
                .ToList();

            // For now, strong areas are those not in problem areas
            analysis.StrongAreas = byCategory
                .Where(c => c.SupplementCount <= 2)
                .Take(5)
                .ToList();

            return analysis;
        }

        /// <summary>
        /// Get accuracy for a specific part category
        /// </summary>
        public CategoryAccuracy? GetCategoryAccuracy(string category)
        {
            var normalized = NormalizeCategory(category);
            var analysis = GetAccuracyAnalysis();
            return analysis.ByCategory.FirstOrDefault(c =>
                c.Category.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get supplement rate for a category (0-1)
        /// </summary>
        public double GetSupplementRate(string category)
        {
            var normalized = NormalizeCategory(category);
            var feedbacks = _data.Feedbacks
                .Where(f => string.IsNullOrEmpty(f.UserId) || f.UserId == GetCurrentUserId())
                .ToList();

            var relevantFeedbacks = feedbacks
                .Where(f => f.SupplementItems.Any(s =>
                    NormalizeCategory(s.PartCategory).Equals(normalized, StringComparison.OrdinalIgnoreCase)) ||
                    (f.WasAccurate && f.PrimaryCategory != null &&
                     NormalizeCategory(f.PrimaryCategory).Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (relevantFeedbacks.Count == 0) return 0;

            var supplemented = relevantFeedbacks.Count(f => !f.WasAccurate);
            return (double)supplemented / relevantFeedbacks.Count;
        }

        /// <summary>
        /// Get personalized tips based on accuracy patterns
        /// </summary>
        public List<AccuracyTip> GetPersonalizedTips()
        {
            var tips = new List<AccuracyTip>();
            var analysis = GetAccuracyAnalysis(50); // Last 50 estimates

            if (analysis.TotalEstimates < 5)
            {
                tips.Add(new AccuracyTip
                {
                    Category = "General",
                    Tip = "Keep logging estimate feedback to build your accuracy profile!",
                    Priority = "info"
                });
                return tips;
            }

            foreach (var problem in analysis.ProblemAreas.Take(3))
            {
                var tip = new AccuracyTip
                {
                    Category = problem.Category,
                    Priority = problem.SupplementCount >= 5 ? "high" : "medium"
                };

                if (problem.AverageHoursAdded > 0)
                {
                    tip.Tip = $"Consider adding {problem.AverageHoursAdded:F1} hrs to {problem.Category} estimates. " +
                              $"You've supplemented this {problem.SupplementCount} times.";
                }
                else if (problem.TotalMaterialsAdded > 0)
                {
                    var avgMaterials = problem.TotalMaterialsAdded / problem.SupplementCount;
                    tip.Tip = $"You're typically missing ~${avgMaterials:F0} in materials on {problem.Category}.";
                }
                else
                {
                    tip.Tip = $"{problem.Category} has been supplemented {problem.SupplementCount} times. " +
                              "Run the Supplement Checker before finalizing.";
                }

                if (problem.CommonMissedItems.Any())
                {
                    tip.Tip += $" Common misses: {string.Join(", ", problem.CommonMissedItems)}";
                }

                tips.Add(tip);
            }

            // Add positive reinforcement for strong areas
            if (analysis.StrongAreas.Any())
            {
                var strong = analysis.StrongAreas.First();
                tips.Add(new AccuracyTip
                {
                    Category = strong.Category,
                    Tip = $"Great accuracy on {strong.Category}! Keep it up.",
                    Priority = "positive"
                });
            }

            return tips;
        }

        /// <summary>
        /// Format accuracy analysis for chatbot display
        /// </summary>
        public string FormatForChat()
        {
            var analysis = GetAccuracyAnalysis(50);

            if (analysis.TotalEstimates == 0)
            {
                return "📊 **No accuracy data yet.**\n\n" +
                       "Start logging estimate outcomes to build your accuracy profile!\n\n" +
                       "After completing an estimate, use the feedback prompt to record:\n" +
                       "• Was the estimate accurate?\n" +
                       "• What items were supplemented?\n\n" +
                       "Over time, I'll show you patterns in where you're leaving money on the table.";
            }

            if (analysis.TotalEstimates < 5)
            {
                return $"📊 **Building Your Accuracy Profile**\n\n" +
                       $"Estimates tracked: {analysis.TotalEstimates}\n" +
                       $"Need at least 5 estimates for meaningful insights.\n\n" +
                       "Keep logging feedback after each job!";
            }

            var response = $"📊 **Your Estimate Accuracy** (Last {analysis.TotalEstimates} estimates)\n\n";
            response += $"Overall: **{analysis.OverallAccuracyRate:P0}** accurate ({analysis.AccurateCount}/{analysis.TotalEstimates})\n\n";

            if (analysis.ProblemAreas.Any())
            {
                response += "🔴 **FREQUENTLY SUPPLEMENTED:**\n";
                foreach (var problem in analysis.ProblemAreas.Take(3))
                {
                    response += $"• **{problem.Category}**: Supplemented {problem.SupplementCount}x\n";
                    if (problem.AverageHoursAdded > 0)
                        response += $"  → Avg: +{problem.AverageHoursAdded:F1} hrs\n";
                    if (problem.TotalMaterialsAdded > 0)
                        response += $"  → Materials: +${problem.TotalMaterialsAdded:F0} total\n";
                    if (problem.CommonMissedItems.Any())
                        response += $"  → Common: {string.Join(", ", problem.CommonMissedItems.Take(2))}\n";
                }
                response += "\n";
            }

            if (analysis.StrongAreas.Any())
            {
                response += "🟢 **ACCURATE AREAS:**\n";
                foreach (var strong in analysis.StrongAreas.Take(3))
                {
                    response += $"• {strong.Category}";
                    if (strong.SupplementCount > 0)
                        response += $" (only {strong.SupplementCount} supplements)";
                    response += "\n";
                }
                response += "\n";
            }

            // Add top tip
            var tips = GetPersonalizedTips();
            var topTip = tips.FirstOrDefault(t => t.Priority == "high" || t.Priority == "medium");
            if (topTip != null)
            {
                response += $"💡 **TIP:** {topTip.Tip}";
            }

            return response;
        }

        /// <summary>
        /// Format response for "where am I leaving money" query
        /// </summary>
        public string FormatMoneyLeftOnTable()
        {
            var analysis = GetAccuracyAnalysis(50);

            if (analysis.TotalEstimates < 5)
            {
                return "💰 **Not enough data yet.**\n\n" +
                       "Log at least 5 estimate outcomes to see where you're leaving money on the table.";
            }

            var totalHoursLeft = analysis.ByCategory.Sum(c => c.TotalHoursAdded);
            var totalMaterialsLeft = analysis.ByCategory.Sum(c => c.TotalMaterialsAdded);
            var estimatedValue = (totalHoursLeft * 50m) + totalMaterialsLeft; // Rough $50/hr estimate

            var response = "💰 **Where You're Leaving Money**\n\n";

            if (estimatedValue > 0)
            {
                response += $"Estimated missed revenue (last {analysis.TotalEstimates} jobs): **${estimatedValue:N0}**\n";
                response += $"• Labor: {totalHoursLeft:F1} hrs (~${totalHoursLeft * 50:N0})\n";
                response += $"• Materials: ${totalMaterialsLeft:N0}\n\n";
            }

            response += "**By Category:**\n";
            foreach (var cat in analysis.ByCategory.Where(c => c.TotalHoursAdded > 0 || c.TotalMaterialsAdded > 0).Take(5))
            {
                var catValue = (cat.TotalHoursAdded * 50m) + cat.TotalMaterialsAdded;
                response += $"• {cat.Category}: ${catValue:N0}";
                if (cat.TotalHoursAdded > 0)
                    response += $" ({cat.TotalHoursAdded:F1} hrs)";
                response += "\n";
            }

            return response;
        }

        /// <summary>
        /// "Retirement Fund Finder" - Scans IMPORTED estimates and finds money left on the table.
        /// Compares actual estimates against P-Page "Not Included" operations to find patterns
        /// of commonly missed billable operations.
        /// </summary>
        public RetirementFundAnalysis GetRetirementFundAnalysis(int? monthsBack = 6, int estimatesPerMonth = 40)
        {
            var analysis = new RetirementFundAnalysis
            {
                MonthsAnalyzed = monthsBack ?? 6,
                EstimatesPerMonthAssumption = estimatesPerMonth
            };

            try
            {
                // Get REAL imported estimate data from the learning service
                var learningService = EstimateLearningService.Instance;
                var cutoffDate = DateTime.Now.AddMonths(-(monthsBack ?? 6));

                var trainingExamples = learningService.GetTrainingExamples()
                    .Where(e => e.DateAdded >= cutoffDate)
                    .ToList();

                analysis.TotalEstimatesAnalyzed = trainingExamples.Count;

                if (trainingExamples.Count < 3)
                    return analysis;

                // Load the IncludedNotIncluded data to know what SHOULD be on estimates
                var includedNotIncludedData = LoadIncludedNotIncludedData();
                if (includedNotIncludedData?.Operations == null || includedNotIncludedData.Operations.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[RetirementFund] No IncludedNotIncluded data available");
                    return analysis;
                }

                // Track missed operations across all estimates
                var missedOperationTracker = new Dictionary<string, MissedOperationTracker>();

                foreach (var example in trainingExamples)
                {
                    // Find matching P-Page operation for this estimate line
                    var matchingPPage = FindMatchingPPageOperation(
                        example.PartName,
                        example.OperationType,
                        includedNotIncludedData.Operations);

                    if (matchingPPage == null || matchingPPage.NotIncluded == null)
                        continue;

                    // Get the operations that were actually on this estimate
                    var estimateOperationDescriptions = example.GeneratedOperations
                        .Select(op => op.Description?.ToLowerInvariant() ?? "")
                        .ToHashSet();

                    // Check each "Not Included" operation - these are billable if added
                    foreach (var notIncludedOp in matchingPPage.NotIncluded)
                    {
                        var notIncludedLower = notIncludedOp.ToLowerInvariant();

                        // Check if this operation appears in the estimate
                        var wasIncluded = estimateOperationDescriptions.Any(desc =>
                            OperationMatches(desc, notIncludedLower));

                        if (!wasIncluded)
                        {
                            // This is money left on the table!
                            var key = $"{matchingPPage.PartName}|{ExtractOperationName(notIncludedOp)}";

                            if (!missedOperationTracker.ContainsKey(key))
                            {
                                // Find the hours for this operation from metOperations
                                var metOp = matchingPPage.MetOperations?.FirstOrDefault(m =>
                                    OperationMatches(m.Description?.ToLowerInvariant() ?? "", notIncludedLower));

                                missedOperationTracker[key] = new MissedOperationTracker
                                {
                                    PartName = matchingPPage.PartName ?? "Unknown",
                                    OperationDescription = ExtractOperationName(notIncludedOp),
                                    FullDescription = notIncludedOp,
                                    EstimatedHours = metOp?.Hours ?? 0.3m, // Default 0.3 hrs if unknown
                                    LaborType = metOp?.LaborType ?? "B"
                                };
                            }

                            missedOperationTracker[key].TimesOccurred++;
                            missedOperationTracker[key].AffectedEstimates.Add(example.NormalizedKey);
                        }
                    }
                }

                // Calculate missed revenue
                const decimal laborRate = 55m; // Average body labor rate
                var significantMisses = missedOperationTracker.Values
                    .Where(m => m.TimesOccurred >= 2) // Must be missed at least twice to be a pattern
                    .OrderByDescending(m => m.TimesOccurred * m.EstimatedHours)
                    .ToList();

                if (!significantMisses.Any())
                {
                    // No patterns found - estimates look complete!
                    analysis.AverageMissedPerJob = 0;
                    return analysis;
                }

                // Calculate totals
                var totalMissedHours = significantMisses.Sum(m => m.TimesOccurred * m.EstimatedHours);
                var totalMissedValue = totalMissedHours * laborRate;
                var uniqueEstimatesAffected = significantMisses
                    .SelectMany(m => m.AffectedEstimates)
                    .Distinct()
                    .Count();

                analysis.AverageHoursPerJob = uniqueEstimatesAffected > 0
                    ? totalMissedHours / uniqueEstimatesAffected
                    : 0;
                analysis.AverageMissedPerJob = analysis.AverageHoursPerJob * laborRate;

                // Project to yearly
                var missRate = (double)uniqueEstimatesAffected / trainingExamples.Count;
                var yearlyEstimatesAffected = (int)(estimatesPerMonth * 12 * missRate);
                analysis.ProjectedYearlyGain = analysis.AverageMissedPerJob * yearlyEstimatesAffected;

                // Group by part category for top opportunities
                var byCategory = significantMisses
                    .GroupBy(m => NormalizeCategory(m.PartName))
                    .Select(g => new MissedRevenueCategory
                    {
                        Category = g.Key,
                        TotalHours = g.Sum(m => m.TimesOccurred * m.EstimatedHours),
                        TotalMaterials = 0, // Could add materials tracking later
                        Occurrences = g.Sum(m => m.TimesOccurred),
                        CommonOperations = g
                            .OrderByDescending(m => m.TimesOccurred)
                            .Take(3)
                            .Select(m => new MissedOperation
                            {
                                Description = m.OperationDescription,
                                TimesOccurred = m.TimesOccurred,
                                AverageHours = m.EstimatedHours,
                                AverageMaterials = 0
                            })
                            .ToList()
                    })
                    .OrderByDescending(c => c.TotalHours * laborRate)
                    .ToList();

                analysis.TopOpportunities = byCategory.Take(5).ToList();
                analysis.ActionItems = GenerateActionItems(byCategory, estimatesPerMonth);

                System.Diagnostics.Debug.WriteLine($"[RetirementFund] Analyzed {trainingExamples.Count} estimates, found {significantMisses.Count} missed operation patterns");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RetirementFund] Error: {ex.Message}");
            }

            return analysis;
        }

        /// <summary>
        /// Helper class to track missed operations
        /// </summary>
        private class MissedOperationTracker
        {
            public string PartName { get; set; } = "";
            public string OperationDescription { get; set; } = "";
            public string FullDescription { get; set; } = "";
            public decimal EstimatedHours { get; set; }
            public string LaborType { get; set; } = "B";
            public int TimesOccurred { get; set; }
            public HashSet<string> AffectedEstimates { get; set; } = new();
        }

        /// <summary>
        /// Load IncludedNotIncluded data from JSON
        /// </summary>
        private IncludedNotIncludedDataLocal? LoadIncludedNotIncludedData()
        {
            try
            {
                var basePath = AppContext.BaseDirectory;
                var path = Path.Combine(basePath, "Data", "IncludedNotIncluded.json");

                if (!File.Exists(path))
                    path = Path.Combine(Directory.GetCurrentDirectory(), "Data", "IncludedNotIncluded.json");

                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<IncludedNotIncludedDataLocal>(json, options);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RetirementFund] Failed to load IncludedNotIncluded: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Find matching P-Page operation based on part name and operation type
        /// </summary>
        private IncludedNotIncludedOperationLocal? FindMatchingPPageOperation(
            string partName,
            string operationType,
            List<IncludedNotIncludedOperationLocal> operations)
        {
            if (string.IsNullOrEmpty(partName)) return null;

            var partLower = partName.ToLowerInvariant();
            var opTypeLower = operationType?.ToLowerInvariant() ?? "";

            // Try exact match first
            var match = operations.FirstOrDefault(op =>
                (op.PartName?.ToLowerInvariant().Contains(partLower) == true ||
                 op.Aliases?.Any(a => a.ToLowerInvariant().Contains(partLower)) == true) &&
                (string.IsNullOrEmpty(opTypeLower) ||
                 op.OperationType?.ToLowerInvariant().Contains(opTypeLower) == true));

            // Try fuzzy match
            if (match == null)
            {
                match = operations.FirstOrDefault(op =>
                    op.Aliases?.Any(a => partLower.Contains(a.ToLowerInvariant())) == true);
            }

            return match;
        }

        /// <summary>
        /// Check if an operation description matches a "not included" item
        /// </summary>
        private bool OperationMatches(string estimateOp, string notIncludedOp)
        {
            if (string.IsNullOrEmpty(estimateOp) || string.IsNullOrEmpty(notIncludedOp))
                return false;

            // Extract key terms from the not-included operation
            var keyTerms = new[] { "weld-through", "primer", "e-coat", "cavity wax", "corrosion",
                "seam sealer", "scan", "calibration", "blend", "feather", "prime", "block",
                "denib", "polish", "buff", "masking", "fog lamp", "cover vehicle", "sound deadener" };

            foreach (var term in keyTerms)
            {
                if (notIncludedOp.Contains(term) && estimateOp.Contains(term))
                    return true;
            }

            // Check for partial match (at least 3 words in common)
            var notIncludedWords = notIncludedOp.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .ToHashSet();
            var estimateWords = estimateOp.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .ToHashSet();

            var commonWords = notIncludedWords.Intersect(estimateWords).Count();
            return commonWords >= 2;
        }

        /// <summary>
        /// Extract a clean operation name from the full P-Page description
        /// </summary>
        private string ExtractOperationName(string fullDescription)
        {
            // Remove source citations like "(CCC/MOTOR)" or "(DEG Inquiry 12345)"
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                fullDescription,
                @"\s*\([^)]*\)\s*",
                "");

            // Truncate if too long
            if (cleaned.Length > 50)
                cleaned = cleaned.Substring(0, 47) + "...";

            return cleaned.Trim();
        }

        private List<RetirementFundActionItem> GenerateActionItems(List<MissedRevenueCategory> categories, int estimatesPerMonth)
        {
            var items = new List<RetirementFundActionItem>();

            foreach (var cat in categories.Take(5))
            {
                var value = (cat.TotalHours * 50m) + cat.TotalMaterials;
                if (value <= 0) continue;

                // Project this specific category to yearly
                var yearlyProjection = value * (12.0m / Math.Max(1, cat.Occurrences)) * estimatesPerMonth / 12;

                var item = new RetirementFundActionItem
                {
                    Category = cat.Category,
                    Description = GetActionDescription(cat),
                    HistoricalMissed = value,
                    ProjectedYearlyGain = yearlyProjection,
                    Priority = cat.Occurrences >= 5 ? "HIGH" : cat.Occurrences >= 3 ? "MEDIUM" : "LOW"
                };

                items.Add(item);
            }

            return items;
        }

        private string GetActionDescription(MissedRevenueCategory cat)
        {
            if (cat.CommonOperations.Any())
            {
                var topOp = cat.CommonOperations.First();
                return $"Add \"{topOp.Description}\" to your standard {cat.Category.ToLower()} process";
            }

            if (cat.TotalHours > cat.TotalMaterials / 50m)
            {
                return $"Add {cat.TotalHours / Math.Max(1, cat.Occurrences):F1} hrs avg to {cat.Category.ToLower()} estimates";
            }

            return $"Review {cat.Category.ToLower()} materials - missing ~${cat.TotalMaterials / Math.Max(1, cat.Occurrences):N0}/job";
        }

        /// <summary>
        /// Format the Retirement Fund Finder analysis for chat display
        /// </summary>
        public string FormatRetirementFundFinder(int estimatesPerMonth = 40)
        {
            var analysis = GetRetirementFundAnalysis(6, estimatesPerMonth);

            if (analysis.TotalEstimatesAnalyzed < 3)
            {
                return "🏦 **Retirement Fund Finder**\n\n" +
                       "Not enough data yet! Import at least 3 estimates to analyze your patterns.\n\n" +
                       "**How to use:**\n" +
                       "1. Go to the **Import** tab\n" +
                       "2. Upload estimate PDFs from CCC or Mitchell\n" +
                       "3. Come back here to see:\n" +
                       "   • Average $ left on table per job\n" +
                       "   • Projected yearly revenue gain\n" +
                       "   • Specific operations you're missing\n" +
                       "   • Your ROI from adding these operations";
            }

            // Check if estimates look complete (no significant misses)
            if (analysis.AverageMissedPerJob == 0 && analysis.TotalEstimatesAnalyzed >= 3)
            {
                return "🏦 **Retirement Fund Finder**\n\n" +
                       $"✅ **Looking good!** Analyzed {analysis.TotalEstimatesAnalyzed} estimates.\n\n" +
                       "Your estimates appear to include the standard P-Page operations.\n" +
                       "No significant patterns of missed operations detected.\n\n" +
                       "**Keep it up!** Import more estimates over time to track your consistency.";
            }

            var response = "🏦 **Retirement Fund Finder**\n";
            response += $"*Analyzing last {analysis.MonthsAnalyzed} months ({analysis.TotalEstimatesAnalyzed} estimates)*\n\n";

            // Headline numbers
            if (analysis.AverageMissedPerJob > 0)
            {
                response += $"💵 **You're leaving ~${analysis.AverageMissedPerJob:N0}/job on the table**\n\n";

                if (analysis.ProjectedYearlyGain > 0)
                {
                    response += $"📈 **Projected yearly gain: ${analysis.ProjectedYearlyGain:N0}**\n";
                    response += $"   *(based on {analysis.EstimatesPerMonthAssumption} estimates/month)*\n\n";
                }
            }

            // Top opportunities
            if (analysis.TopOpportunities.Any())
            {
                response += "🎯 **Top Missed Operations:**\n";
                foreach (var opp in analysis.TopOpportunities.Take(4))
                {
                    var value = opp.TotalHours * 55m; // $55/hr labor rate
                    response += $"• **{opp.Category}**: ${value:N0} missed";
                    if (opp.Occurrences > 1)
                        response += $" ({opp.Occurrences}x)";
                    response += "\n";

                    if (opp.CommonOperations.Any())
                    {
                        var top = opp.CommonOperations.First();
                        response += $"  └ \"{top.Description}\" ({top.TimesOccurred}x @ {top.AverageHours:F1}hr)\n";
                    }
                }
                response += "\n";
            }

            // Action items with ROI
            if (analysis.ActionItems.Any())
            {
                response += "✅ **ACTION ITEMS:**\n";
                var highPriorityItems = analysis.ActionItems.Where(a => a.Priority == "HIGH" || a.Priority == "MEDIUM").Take(3);
                foreach (var action in highPriorityItems)
                {
                    var icon = action.Priority == "HIGH" ? "🔴" : "🟡";
                    response += $"{icon} {action.Description}\n";
                    response += $"   → **+${action.ProjectedYearlyGain:N0}/year**\n";
                }
                response += "\n";
            }

            // Calculate total ROI
            var totalPotential = analysis.ActionItems.Sum(a => a.ProjectedYearlyGain);
            if (totalPotential > 0)
            {
                response += $"💰 **Total potential: +${totalPotential:N0}/year**\n";
                response += "   by consistently applying these operations";
            }

            return response;
        }

        #endregion

        #region Baseline Merge

        /// <summary>
        /// Merge baseline accuracy feedbacks into this service's data.
        /// Detects duplicates by category + operation type combo (not RO number since those are stripped).
        /// User's personal accuracy data is always preserved.
        /// </summary>
        public void MergeBaseline(List<EstimateFeedback> baselineFeedbacks)
        {
            if (baselineFeedbacks == null || baselineFeedbacks.Count == 0) return;

            lock (_lock)
            {
                // Build signature set from existing feedbacks for duplicate detection
                var existingSignatures = _data.Feedbacks
                    .Select(f => GenerateFeedbackSignature(f))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                int added = 0;
                foreach (var feedback in baselineFeedbacks)
                {
                    var sig = GenerateFeedbackSignature(feedback);
                    if (!existingSignatures.Contains(sig))
                    {
                        _data.Feedbacks.Add(feedback);
                        existingSignatures.Add(sig);
                        added++;
                    }
                }

                if (added > 0)
                {
                    SaveData();
                    System.Diagnostics.Debug.WriteLine($"[AccuracyService] Merged baseline: added {added} feedback entries");
                }
            }
        }

        /// <summary>
        /// Generate a signature for duplicate detection during baseline merge.
        /// Uses category + operation types + was accurate flag + supplement count.
        /// </summary>
        private string GenerateFeedbackSignature(EstimateFeedback feedback)
        {
            var category = NormalizeCategory(feedback.PrimaryCategory ?? "");
            var ops = string.Join(",", feedback.SupplementItems
                .Select(s => $"{NormalizeCategory(s.PartCategory)}:{s.OperationType}:{s.HoursAdded:F1}")
                .OrderBy(s => s));
            return $"{category}|{feedback.WasAccurate}|{ops}";
        }

        /// <summary>
        /// Get raw accuracy data (for baseline export)
        /// </summary>
        public AccuracyData GetRawData() => _data;

        #endregion

        #region Helper Methods

        private string NormalizeCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return "Other";

            var lower = category.ToLowerInvariant().Trim();

            // Normalize common variations
            if (lower.Contains("door")) return "Doors";
            if (lower.Contains("fender")) return "Fenders";
            if (lower.Contains("bumper")) return "Bumpers";
            if (lower.Contains("quarter") || lower.Contains("qtr")) return "Quarter Panels";
            if (lower.Contains("hood")) return "Hood";
            if (lower.Contains("deck") || lower.Contains("trunk")) return "Decklid/Trunk";
            if (lower.Contains("roof")) return "Roof";
            if (lower.Contains("rocker")) return "Rockers";
            if (lower.Contains("pillar")) return "Pillars";
            if (lower.Contains("rail")) return "Rails";
            if (lower.Contains("blend")) return "Blends";
            if (lower.Contains("refinish") || lower.Contains("paint")) return "Refinish";
            if (lower.Contains("scan")) return "Scanning/ADAS";
            if (lower.Contains("adas") || lower.Contains("calibrat")) return "Scanning/ADAS";
            if (lower.Contains("structural")) return "Structural";
            if (lower.Contains("frame")) return "Frame";
            if (lower.Contains("glass") || lower.Contains("windshield")) return "Glass";
            if (lower.Contains("mirror")) return "Mirrors";
            if (lower.Contains("headl") || lower.Contains("head lamp")) return "Headlamps";
            if (lower.Contains("taill") || lower.Contains("tail lamp")) return "Taillamps";
            if (lower.Contains("wheel") || lower.Contains("rim")) return "Wheels";
            if (lower.Contains("suspension")) return "Suspension";
            if (lower.Contains("material")) return "Materials";
            if (lower.Contains("labor")) return "Labor Time";

            // Capitalize first letter
            return char.ToUpper(category[0]) + category.Substring(1).ToLower();
        }

        private string GetCurrentUserId()
        {
            try { return Environment.UserName; }
            catch { return "unknown"; }
        }

        private AccuracyData LoadData()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<AccuracyData>(json, options) ?? new AccuracyData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccuracyService] Load error: {ex.Message}");
            }
            return new AccuracyData();
        }

        private void SaveData()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_data, options);
                File.WriteAllText(_dataFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccuracyService] Save error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all accuracy data (for testing)
        /// </summary>
        public void ClearAllData()
        {
            lock (_lock)
            {
                _data = new AccuracyData();
                SaveData();
            }
        }

        /// <summary>
        /// Get raw feedback count for status display
        /// </summary>
        public int GetFeedbackCount() => _data.Feedbacks.Count;

        #endregion
    }

    #region Data Models

    public class AccuracyData
    {
        public List<EstimateFeedback> Feedbacks { get; set; } = new();
    }

    public class EstimateFeedback
    {
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = "";
        public string RONumber { get; set; } = "";
        public string VehicleInfo { get; set; } = "";
        public string? PrimaryCategory { get; set; } // Main part category for accurate estimates
        public bool WasAccurate { get; set; }
        public List<SupplementedItem> SupplementItems { get; set; } = new();
    }

    public class SupplementedItem
    {
        public string PartCategory { get; set; } = "";
        public string OperationType { get; set; } = ""; // Repair, Replace, R&I
        public decimal HoursAdded { get; set; }
        public decimal MaterialsAdded { get; set; }
        public string Notes { get; set; } = ""; // What was missed (e.g., "blend time", "R&I moldings")
    }

    public class AccuracyAnalysis
    {
        public int TotalEstimates { get; set; }
        public int AccurateCount { get; set; }
        public int SupplementedCount { get; set; }
        public double OverallAccuracyRate { get; set; }
        public List<CategoryAccuracy> ByCategory { get; set; } = new();
        public List<CategoryAccuracy> ProblemAreas { get; set; } = new();
        public List<CategoryAccuracy> StrongAreas { get; set; } = new();
    }

    public class CategoryAccuracy
    {
        public string Category { get; set; } = "";
        public int SupplementCount { get; set; }
        public decimal TotalHoursAdded { get; set; }
        public decimal TotalMaterialsAdded { get; set; }
        public decimal AverageHoursAdded { get; set; }
        public List<string> CommonMissedItems { get; set; } = new();
    }

    public class AccuracyTip
    {
        public string Category { get; set; } = "";
        public string Tip { get; set; } = "";
        public string Priority { get; set; } = "medium"; // high, medium, low, positive, info
    }

    // Retirement Fund Finder Models
    public class RetirementFundAnalysis
    {
        public int MonthsAnalyzed { get; set; }
        public int TotalEstimatesAnalyzed { get; set; }
        public int EstimatesPerMonthAssumption { get; set; }
        public decimal AverageHoursPerJob { get; set; }
        public decimal AverageMaterialsPerJob { get; set; }
        public decimal AverageMissedPerJob { get; set; }
        public decimal ProjectedYearlyGain { get; set; }
        public List<MissedRevenueCategory> TopOpportunities { get; set; } = new();
        public List<RetirementFundActionItem> ActionItems { get; set; } = new();
    }

    public class MissedRevenueCategory
    {
        public string Category { get; set; } = "";
        public decimal TotalHours { get; set; }
        public decimal TotalMaterials { get; set; }
        public int Occurrences { get; set; }
        public List<MissedOperation> CommonOperations { get; set; } = new();
    }

    public class MissedOperation
    {
        public string Description { get; set; } = "";
        public int TimesOccurred { get; set; }
        public decimal AverageHours { get; set; }
        public decimal AverageMaterials { get; set; }
    }

    public class RetirementFundActionItem
    {
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal HistoricalMissed { get; set; }
        public decimal ProjectedYearlyGain { get; set; }
        public string Priority { get; set; } = "MEDIUM"; // HIGH, MEDIUM, LOW
    }

    // Local IncludedNotIncluded data classes (to avoid circular dependency with ChatbotService)
    internal class IncludedNotIncludedDataLocal
    {
        public string? Version { get; set; }
        public List<IncludedNotIncludedOperationLocal>? Operations { get; set; }
    }

    internal class IncludedNotIncludedOperationLocal
    {
        public string? Id { get; set; }
        public string? PartName { get; set; }
        public string? OperationType { get; set; }
        public string? AttachmentType { get; set; }
        public List<string>? Aliases { get; set; }
        public List<string>? Included { get; set; }
        public List<string>? NotIncluded { get; set; }
        public List<MetOperationLocal>? MetOperations { get; set; }
    }

    internal class MetOperationLocal
    {
        public string? Description { get; set; }
        public decimal Hours { get; set; }
        public string? LaborType { get; set; }
    }

    #endregion
}
