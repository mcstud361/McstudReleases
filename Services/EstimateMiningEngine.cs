#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace McStudDesktop.Services
{
    /// <summary>
    /// ESTIMATE MINING ENGINE
    ///
    /// The powerhouse that analyzes ALL uploaded estimates and extracts:
    /// - Part name variations (builds alias dictionary)
    /// - Operation statistics (labor times, prices, refinish)
    /// - Co-occurrence patterns (what parts appear together)
    /// - Calculation formulas (blend percentages, overlap deductions)
    /// - Vehicle-specific patterns
    ///
    /// RUN THIS ON:
    /// 1. Initial setup (analyze all existing estimates)
    /// 2. Every new estimate upload (incremental learning)
    /// 3. Periodic refresh (recalculate patterns)
    ///
    /// CONTINUOUS LEARNING: Every estimate makes the system smarter.
    /// </summary>
    public class EstimateMiningEngine
    {
        #region Singleton

        private static EstimateMiningEngine? _instance;
        public static EstimateMiningEngine Instance => _instance ??= new EstimateMiningEngine();

        #endregion

        private readonly LearnedKnowledgeBase _knowledge;
        private readonly PartRecognitionEngine _partRecognition;
        private readonly EstimateHistoryDatabase _historyDb;
        private readonly EstimateLearningService _legacyLearning;

        // Mining state
        private bool _isMining = false;
        private MiningProgress _progress = new();

        // Events
        public event EventHandler<MiningProgress>? ProgressChanged;
        public event EventHandler<MiningComplete>? MiningCompleted;

        public bool IsMining => _isMining;
        public MiningProgress Progress => _progress;

        public EstimateMiningEngine()
        {
            _knowledge = LearnedKnowledgeBase.Instance;
            _partRecognition = PartRecognitionEngine.Instance;
            _historyDb = EstimateHistoryDatabase.Instance;
            _legacyLearning = EstimateLearningService.Instance;
        }

        #region Full Mining Pass

        /// <summary>
        /// Run a full mining pass on ALL estimates in the database
        /// </summary>
        public async Task<MiningResult> RunFullMiningPassAsync(CancellationToken cancellationToken = default)
        {
            if (_isMining)
            {
                return new MiningResult { Success = false, Message = "Mining already in progress" };
            }

            _isMining = true;
            _progress = new MiningProgress { Phase = "Initializing", StartedAt = DateTime.Now };

            var result = new MiningResult { StartedAt = DateTime.Now };

            try
            {
                // Phase 0: Clear existing learned data for a clean rebuild with current filtering
                _progress.Phase = "Clearing old data";
                ProgressChanged?.Invoke(this, _progress);
                _knowledge.ClearAllLearnedData();
                System.Diagnostics.Debug.WriteLine("[Mining] Cleared old knowledge for clean rebuild with sanity filtering");

                // Phase 1: Get all estimates from history database
                _progress.Phase = "Loading estimates";
                ProgressChanged?.Invoke(this, _progress);

                var allEstimates = _historyDb.GetAllEstimates();
                _progress.TotalEstimates = allEstimates.Count;

                System.Diagnostics.Debug.WriteLine($"[Mining] Starting full pass on {allEstimates.Count} estimates");

                // Phase 2: Process each estimate
                _progress.Phase = "Mining patterns";
                int processed = 0;

                foreach (var estimate in allEstimates)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // Skip if already analyzed (unless we're doing a full refresh)
                    // For now, we'll re-analyze everything to ensure data quality

                    await Task.Run(() => MineEstimate(estimate), cancellationToken);

                    processed++;
                    _progress.ProcessedEstimates = processed;
                    _progress.CurrentEstimate = estimate.ClaimNumber ?? estimate.Id;

                    // Update progress every 10 estimates
                    if (processed % 10 == 0)
                    {
                        ProgressChanged?.Invoke(this, _progress);
                    }
                }

                // Phase 3: Calculate derived patterns
                _progress.Phase = "Calculating patterns";
                ProgressChanged?.Invoke(this, _progress);

                CalculateDerivedPatterns();

                // Phase 4: Save knowledge base
                _progress.Phase = "Saving knowledge";
                ProgressChanged?.Invoke(this, _progress);

                _knowledge.Save();

                // Complete
                result.Success = true;
                result.EstimatesProcessed = processed;
                result.PartsLearned = _knowledge.TotalPartsLearned;
                result.PatternsDiscovered = _knowledge.TotalPatternsLearned;
                result.CompletedAt = DateTime.Now;
                result.Message = $"Mined {processed} estimates, learned {result.PartsLearned} parts, discovered {result.PatternsDiscovered} patterns";

                _progress.Phase = "Complete";
                _progress.IsComplete = true;
                ProgressChanged?.Invoke(this, _progress);
                MiningCompleted?.Invoke(this, new MiningComplete { Result = result });

                System.Diagnostics.Debug.WriteLine($"[Mining] Complete: {result.Message}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Mining failed: {ex.Message}";
                result.CompletedAt = DateTime.Now;

                System.Diagnostics.Debug.WriteLine($"[Mining] Error: {ex.Message}");
            }
            finally
            {
                _isMining = false;
            }

            return result;
        }

        /// <summary>
        /// Clear all learned data and re-mine from scratch with improved filtering.
        /// Call this after upgrading the sanity caps in LearnedKnowledgeBase.
        /// </summary>
        public async Task<MiningResult> ClearAndRemineAsync(CancellationToken cancellationToken = default)
        {
            System.Diagnostics.Debug.WriteLine("[Mining] Clearing all learned data for clean re-mine...");
            _knowledge.ClearAllLearnedData();
            return await RunFullMiningPassAsync(cancellationToken);
        }

        #endregion

        #region Single Estimate Mining

        /// <summary>
        /// Mine a single estimate (for incremental learning)
        /// </summary>
        public void MineEstimate(StoredEstimate estimate)
        {
            if (estimate.LineItems == null || estimate.LineItems.Count == 0)
                return;

            // Grade the estimate if not already graded
            if (estimate.QualityScore == 0 && estimate.LineItems.Count > 0)
            {
                var parsedLines = estimate.LineItems.Select(li => new ParsedEstimateLine
                {
                    PartName = li.PartName,
                    OperationType = li.OperationType,
                    LaborHours = li.LaborHours,
                    RefinishHours = li.RefinishHours,
                    Price = li.Price,
                    Description = li.Description,
                    IsManualLine = li.IsManualLine
                }).ToList();

                var quality = EstimateQualityService.Instance.AssessQuality(parsedLines, estimate.VehicleInfo);
                estimate.QualityScore = quality.QualityScore;
                estimate.QualityGrade = quality.Grade.ToString();
            }

            // Skip rejected estimates entirely — they hurt learning
            if (estimate.QualityScore > 0 && estimate.QualityScore < 40)
            {
                System.Diagnostics.Debug.WriteLine($"[Mining] Skipping low-quality estimate {estimate.Id} (score: {estimate.QualityScore}, grade: {estimate.QualityGrade})");
                return;
            }

            var estimateId = estimate.Id ?? Guid.NewGuid().ToString();

            // Track parts in this estimate for co-occurrence
            var partsInEstimate = new List<(string canonical, string operation, decimal labor, decimal refinish, decimal price)>();

            // Process each line item
            foreach (var line in estimate.LineItems)
            {
                // Recognize the part
                var recognition = _partRecognition.Recognize(line.Description ?? "");
                if (!recognition.Success || string.IsNullOrEmpty(recognition.CanonicalName))
                    continue;

                var canonical = recognition.CanonicalName;
                var operation = NormalizeOperationType(line.Operation ?? line.OperationType ?? "");
                var laborHours = line.LaborHours;
                var refinishHours = line.RefinishHours;
                var price = line.Price;

                // Record operation statistics with description and labor type
                _knowledge.RecordOperationDetails(
                    canonical,
                    operation,
                    laborHours,
                    refinishHours,
                    price,
                    description: line.Description,
                    laborType: line.LaborType,
                    vehicleType: estimate.VehicleType
                );

                // Track for co-occurrence analysis
                partsInEstimate.Add((canonical, operation, laborHours, refinishHours, price));

                // Learn blend adjacency
                if (operation == "blend" || operation == "refinish")
                {
                    // Find what was refinished that this might be adjacent to
                    var refinishedParts = partsInEstimate
                        .Where(p => p.operation == "refinish" && p.canonical != canonical)
                        .ToList();

                    foreach (var refinished in refinishedParts)
                    {
                        if (operation == "blend")
                        {
                            _knowledge.RecordBlendAdjacency(refinished.canonical, canonical);
                        }
                    }
                }
            }

            // Record co-occurrences (all pairs of parts in this estimate)
            for (int i = 0; i < partsInEstimate.Count; i++)
            {
                for (int j = i + 1; j < partsInEstimate.Count; j++)
                {
                    var (part1, op1, _, _, _) = partsInEstimate[i];
                    var (part2, op2, _, _, _) = partsInEstimate[j];

                    _knowledge.RecordCoOccurrence(part1, op1, part2, op2);
                }
            }

            // Record operation profiles — group lines by section and learn sub-operation patterns
            RecordOperationProfiles(estimate);

            // Extract calculation patterns
            ExtractCalculationPatterns(estimate, partsInEstimate);

            // Mark estimate as analyzed
            _knowledge.RecordEstimateAnalyzed(
                estimateId,
                estimate.LineItems.Count,
                estimate.TotalAmount
            );
        }

        /// <summary>
        /// Mine a single estimate for incremental learning (public entry point)
        /// </summary>
        public void LearnFromEstimate(StoredEstimate estimate)
        {
            MineEstimate(estimate);
            _knowledge.Save();

            System.Diagnostics.Debug.WriteLine($"[Mining] Incremental learn from estimate: {estimate.ClaimNumber}");
        }

        #endregion

        #region Pattern Extraction

        /// <summary>
        /// Extract calculation patterns from an estimate
        /// </summary>
        private void ExtractCalculationPatterns(
            StoredEstimate estimate,
            List<(string canonical, string operation, decimal labor, decimal refinish, decimal price)> parts)
        {
            // Look for blend patterns
            var blends = parts.Where(p => p.operation == "blend").ToList();
            var refinishes = parts.Where(p => p.operation == "refinish").ToList();

            foreach (var blend in blends)
            {
                // Find likely source refinish panel
                var potentialSource = refinishes
                    .Where(r => r.refinish > 0)
                    .OrderByDescending(r => r.refinish)
                    .FirstOrDefault();

                if (potentialSource.canonical != null && potentialSource.refinish > 0 && blend.refinish > 0)
                {
                    // Record the ratio (blend hours / refinish hours)
                    _knowledge.RecordCalculation(
                        "blend_percentage",
                        $"{blend.canonical} blended with {potentialSource.canonical}",
                        potentialSource.refinish,
                        blend.refinish
                    );
                }
            }

            // Look for adhesion promoter patterns
            var adhesionPromoter = parts.FirstOrDefault(p =>
                p.canonical.Contains("adhesion") || p.canonical.Contains("promoter"));

            if (adhesionPromoter.canonical != null)
            {
                var totalPlasticRefinish = parts
                    .Where(p => IsPlasticPart(p.canonical) && p.refinish > 0)
                    .Sum(p => p.refinish);

                if (totalPlasticRefinish > 0)
                {
                    _knowledge.RecordCalculation(
                        "adhesion_promoter",
                        "plastic refinish",
                        totalPlasticRefinish,
                        adhesionPromoter.labor > 0 ? adhesionPromoter.labor : adhesionPromoter.refinish
                    );
                }
            }

            // Look for overlap deduction patterns
            // (This would require more detailed line-by-line analysis)
        }

        /// <summary>
        /// Calculate derived patterns after all estimates are processed
        /// </summary>
        private void CalculateDerivedPatterns()
        {
            var allParts = _knowledge.GetAllParts();

            foreach (var part in allParts.Values)
            {
                // Calculate co-occurrence rates
                foreach (var assoc in part.CoOccurrences)
                {
                    assoc.CoOccurrenceRate = part.TimesSeenInEstimates > 0
                        ? (double)assoc.TimesSeenTogether / part.TimesSeenInEstimates
                        : 0;
                }

                // Sort co-occurrences by rate
                part.CoOccurrences = part.CoOccurrences
                    .OrderByDescending(c => c.CoOccurrenceRate)
                    .ToList();

                // Sort adjacent panels by frequency
                part.LearnedAdjacentPanels = part.LearnedAdjacentPanels
                    .OrderByDescending(p => part.BlendFrequency.GetValueOrDefault(p, 0))
                    .ToList();
            }

            System.Diagnostics.Debug.WriteLine($"[Mining] Calculated derived patterns for {allParts.Count} parts");
        }

        #endregion

        #region Helpers

        private string NormalizeOperationType(string operation)
        {
            var lower = operation.ToLowerInvariant().Trim();

            return lower switch
            {
                "repl" or "replace" or "rpl" => "replace",
                "rpr" or "repair" or "rep" => "repair",
                "r&i" or "ri" or "r+i" or "r & i" => "r&i",
                "rfn" or "refinish" or "ref" or "refn" => "refinish",
                "blend" or "bld" => "blend",
                "o/h" or "oh" or "overhaul" => "overhaul",
                "sublet" or "sub" => "sublet",
                "add" or "manual" or "man" => "add",
                "body" or "bdy" => "body",
                "mech" or "mechanical" => "mechanical",
                "frame" or "frm" => "frame",
                _ => lower
            };
        }

        /// <summary>
        /// Group estimate lines by section and record operation profiles for primary operations.
        /// A "profile" captures what sub-operations consistently appear with a panel's primary operation.
        /// </summary>
        private void RecordOperationProfiles(StoredEstimate estimate)
        {
            if (estimate.LineItems == null || estimate.LineItems.Count == 0) return;

            // Group lines by section
            var sections = estimate.LineItems
                .Where(li => !string.IsNullOrEmpty(li.Section) && !string.IsNullOrEmpty(li.PartName))
                .GroupBy(li => li.Section, StringComparer.OrdinalIgnoreCase);

            foreach (var section in sections)
            {
                var lines = section.ToList();

                // Find primary operations (Replace/Repair) in this section
                var primaryLines = lines.Where(li =>
                {
                    var op = NormalizeOperationType(li.OperationType);
                    return op == "replace" || op == "repair";
                }).ToList();

                foreach (var primary in primaryLines)
                {
                    var primaryOp = NormalizeOperationType(primary.OperationType);
                    var panelKey = $"{primary.PartName.ToLower()}|{primaryOp}";

                    // All other lines in this section are sub-operations
                    var subOps = lines
                        .Where(li => li != primary)
                        .Select(li => new ProfileSubOperation
                        {
                            PartName = li.PartName,
                            OperationType = NormalizeOperationType(li.OperationType),
                            Description = li.Description,
                            LaborType = li.LaborType,
                            AverageHours = li.LaborHours > 0 ? li.LaborHours : li.RefinishHours
                        })
                        .Where(s => !string.IsNullOrEmpty(s.PartName))
                        .ToList();

                    if (subOps.Count > 0)
                    {
                        _knowledge.RecordOperationProfile(panelKey, subOps);
                    }
                }
            }
        }

        private bool IsPlasticPart(string canonicalPart)
        {
            var plasticParts = new[]
            {
                "bumper_cover", "fascia", "grille", "fender_liner", "valance",
                "spoiler", "air_dam", "splash_guard", "wheel_opening_molding",
                "rocker_molding", "body_side_molding"
            };

            return plasticParts.Any(p => canonicalPart.Contains(p));
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// Get operations typically associated with a part
        /// </summary>
        public List<AssociatedOperation> GetAssociatedOperations(string partName, double minConfidence = 0.3)
        {
            var recognition = _partRecognition.Recognize(partName);
            if (!recognition.Success) return new List<AssociatedOperation>();

            var coOccurrences = _knowledge.GetCoOccurrences(recognition.CanonicalName, minConfidence);

            return coOccurrences.Select(c => new AssociatedOperation
            {
                PartName = c.AssociatedPart,
                OperationType = c.AssociatedOperation,
                Confidence = c.CoOccurrenceRate,
                TimesSeenTogether = c.TimesSeenTogether,
                Stats = _knowledge.GetOperationStats(c.AssociatedPart, c.AssociatedOperation)
            }).ToList();
        }

        /// <summary>
        /// Get expected labor time for a part + operation
        /// </summary>
        public LaborTimeEstimate GetExpectedLaborTime(string partName, string operationType, string? vehicleType = null)
        {
            var recognition = _partRecognition.Recognize(partName);
            if (!recognition.Success)
            {
                return new LaborTimeEstimate { HasData = false };
            }

            // Try vehicle-specific first
            if (!string.IsNullOrEmpty(vehicleType))
            {
                var vehicleSpecific = _knowledge.GetVehicleSpecificLaborTime(vehicleType, recognition.CanonicalName, operationType);
                if (vehicleSpecific.HasValue)
                {
                    return new LaborTimeEstimate
                    {
                        HasData = true,
                        Mean = vehicleSpecific.Value,
                        Source = "vehicle_specific",
                        Confidence = 0.95
                    };
                }
            }

            // Fall back to general stats
            var stats = _knowledge.GetOperationStats(recognition.CanonicalName, operationType);
            if (stats != null && stats.SampleCount > 0)
            {
                return new LaborTimeEstimate
                {
                    HasData = true,
                    Mean = stats.MeanLaborHours,
                    StdDev = stats.StdDevLaborHours,
                    Min = stats.MinLaborHours,
                    Max = stats.MaxLaborHours,
                    Median = stats.MedianLaborHours,
                    SampleCount = stats.SampleCount,
                    Source = "learned",
                    Confidence = Math.Min(0.95, 0.5 + (stats.SampleCount * 0.05))
                };
            }

            return new LaborTimeEstimate { HasData = false };
        }

        /// <summary>
        /// Get panels to suggest for blending
        /// </summary>
        public List<string> GetBlendSuggestions(string refinishedPartName)
        {
            var recognition = _partRecognition.Recognize(refinishedPartName);
            if (!recognition.Success) return new List<string>();

            return _knowledge.GetAdjacentPanels(recognition.CanonicalName);
        }

        /// <summary>
        /// Get parts that commonly appear together with a given part
        /// </summary>
        public List<PartAssociation> GetCommonlyPairedParts(string partName, int topN = 10)
        {
            var recognition = _partRecognition.Recognize(partName);
            if (!recognition.Success) return new List<PartAssociation>();

            return _knowledge.GetCoOccurrences(recognition.CanonicalName)
                .Take(topN)
                .ToList();
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Get mining statistics
        /// </summary>
        public MiningStatistics GetStatistics()
        {
            var kStats = _knowledge.GetStatistics();

            return new MiningStatistics
            {
                TotalEstimatesMined = kStats.TotalEstimatesAnalyzed,
                TotalLinesMined = kStats.TotalLinesAnalyzed,
                TotalPartsLearned = kStats.TotalPartsLearned,
                TotalAliasesLearned = kStats.TotalAliasesLearned,
                TotalCoOccurrencePatterns = kStats.TotalCoOccurrencePatterns,
                TotalFormulasLearned = kStats.TotalFormulasLearned,
                TotalValueMined = kStats.TotalValueAnalyzed,
                LastMiningRun = kStats.LastUpdated,
                TopParts = kStats.TopParts
            };
        }

        #endregion
    }

    #region Data Models

    public class MiningProgress
    {
        public string Phase { get; set; } = "";
        public int TotalEstimates { get; set; }
        public int ProcessedEstimates { get; set; }
        public string CurrentEstimate { get; set; } = "";
        public bool IsComplete { get; set; }
        public DateTime StartedAt { get; set; }

        public double PercentComplete => TotalEstimates > 0
            ? (double)ProcessedEstimates / TotalEstimates * 100
            : 0;
    }

    public class MiningResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int EstimatesProcessed { get; set; }
        public int PartsLearned { get; set; }
        public int PatternsDiscovered { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }

        public TimeSpan Duration => CompletedAt - StartedAt;
    }

    public class MiningComplete
    {
        public MiningResult Result { get; set; } = new();
    }

    public class MiningStatistics
    {
        public int TotalEstimatesMined { get; set; }
        public int TotalLinesMined { get; set; }
        public int TotalPartsLearned { get; set; }
        public int TotalAliasesLearned { get; set; }
        public int TotalCoOccurrencePatterns { get; set; }
        public int TotalFormulasLearned { get; set; }
        public decimal TotalValueMined { get; set; }
        public DateTime LastMiningRun { get; set; }
        public List<PartSummary> TopParts { get; set; } = new();
    }

    public class AssociatedOperation
    {
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public double Confidence { get; set; }
        public int TimesSeenTogether { get; set; }
        public OperationStatistics? Stats { get; set; }
    }

    public class LaborTimeEstimate
    {
        public bool HasData { get; set; }
        public decimal Mean { get; set; }
        public decimal StdDev { get; set; }
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public decimal Median { get; set; }
        public int SampleCount { get; set; }
        public string Source { get; set; } = "";
        public double Confidence { get; set; }

        /// <summary>
        /// Get a "safe" estimate (mean + 1 stddev) to cover most cases
        /// </summary>
        public decimal SafeEstimate => Mean + StdDev;

        /// <summary>
        /// Get the likely range (mean ± 1 stddev)
        /// </summary>
        public (decimal low, decimal high) LikelyRange => (Mean - StdDev, Mean + StdDev);
    }

    #endregion
}
