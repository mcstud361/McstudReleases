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
    /// LEARNED KNOWLEDGE BASE - The brain of the system
    ///
    /// This stores EVERYTHING learned from uploaded estimates:
    /// - Part name variations and canonical forms
    /// - Operation statistics (labor hours, prices, refinish)
    /// - Co-occurrence patterns (what parts appear together)
    /// - Calculation formulas (blend %, overlap deductions)
    /// - Vehicle-specific patterns
    ///
    /// CONTINUOUS LEARNING: Every new estimate makes this smarter.
    /// </summary>
    public class LearnedKnowledgeBase
    {
        #region Singleton

        private static LearnedKnowledgeBase? _instance;
        private static readonly object _lock = new();

        public static LearnedKnowledgeBase Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LearnedKnowledgeBase();
                    }
                }
                return _instance;
            }
        }

        #endregion

        // File paths
        private readonly string _knowledgeFilePath;
        private readonly string _backupPath;

        // The actual knowledge store
        private KnowledgeStore _store;

        // Learning statistics
        public int TotalEstimatesAnalyzed => _store.Metadata.TotalEstimatesAnalyzed;
        public int TotalPartsLearned => _store.Parts.Count;
        public int TotalPatternsLearned => _store.CoOccurrencePatterns.Count;
        public DateTime LastUpdated => _store.Metadata.LastUpdated;

        public LearnedKnowledgeBase()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(appDataPath);

            _knowledgeFilePath = Path.Combine(appDataPath, "learned_knowledge.json");
            _backupPath = Path.Combine(appDataPath, "learned_knowledge.backup.json");

            _store = LoadKnowledge();

            System.Diagnostics.Debug.WriteLine($"[Knowledge] Loaded: {TotalPartsLearned} parts, {TotalPatternsLearned} patterns from {TotalEstimatesAnalyzed} estimates");
        }

        #region Part Knowledge

        /// <summary>
        /// Get or create knowledge entry for a part
        /// </summary>
        public PartKnowledge GetOrCreatePart(string canonicalName)
        {
            var key = canonicalName.ToLowerInvariant().Trim();
            if (!_store.Parts.TryGetValue(key, out var part))
            {
                part = new PartKnowledge
                {
                    CanonicalName = key,
                    CreatedAt = DateTime.Now
                };
                _store.Parts[key] = part;
            }
            return part;
        }

        /// <summary>
        /// Record a part alias (variation of a part name)
        /// </summary>
        public void RecordPartAlias(string canonicalName, string alias)
        {
            var part = GetOrCreatePart(canonicalName);
            var normalizedAlias = alias.ToLowerInvariant().Trim();

            if (!part.KnownAliases.Contains(normalizedAlias))
            {
                part.KnownAliases.Add(normalizedAlias);
                part.LastUpdated = DateTime.Now;
            }

            // Also add to global alias lookup
            _store.AliasToCanonical[normalizedAlias] = canonicalName.ToLowerInvariant();
        }

        /// <summary>
        /// Try to resolve an alias to its canonical part name
        /// </summary>
        public string? ResolveAlias(string possibleAlias)
        {
            var normalized = possibleAlias.ToLowerInvariant().Trim();

            // Direct lookup
            if (_store.AliasToCanonical.TryGetValue(normalized, out var canonical))
                return canonical;

            // Check if it's already a canonical name
            if (_store.Parts.ContainsKey(normalized))
                return normalized;

            return null;
        }

        /// <summary>
        /// Get all known parts
        /// </summary>
        public IReadOnlyDictionary<string, PartKnowledge> GetAllParts() => _store.Parts;

        #endregion

        #region Operation Statistics

        /// <summary>
        /// Record operation statistics for a part
        /// </summary>
        public void RecordOperation(
            string canonicalPart,
            string operationType,
            decimal laborHours,
            decimal refinishHours,
            decimal price,
            string? vehicleType = null)
        {
            var part = GetOrCreatePart(canonicalPart);
            var opKey = operationType.ToLowerInvariant().Trim();

            if (!part.OperationStats.TryGetValue(opKey, out var stats))
            {
                stats = new OperationStatistics { OperationType = opKey };
                part.OperationStats[opKey] = stats;
            }

            // Record values
            if (laborHours > 0)
            {
                stats.LaborHoursValues.Add(laborHours);
                stats.RecalculateLaborStats();
            }

            if (refinishHours > 0)
            {
                stats.RefinishHoursValues.Add(refinishHours);
                stats.RecalculateRefinishStats();
            }

            if (price > 0)
            {
                stats.PriceValues.Add(price);
                stats.RecalculatePriceStats();
            }

            stats.SampleCount++;
            stats.LastSeen = DateTime.Now;
            part.TimesSeenInEstimates++;
            part.LastUpdated = DateTime.Now;

            // Track vehicle-specific stats if provided
            if (!string.IsNullOrEmpty(vehicleType))
            {
                RecordVehicleSpecificStats(canonicalPart, operationType, vehicleType, laborHours, refinishHours);
            }
        }

        /// <summary>
        /// Get operation statistics for a part
        /// </summary>
        public OperationStatistics? GetOperationStats(string canonicalPart, string operationType)
        {
            var partKey = canonicalPart.ToLowerInvariant().Trim();
            var opKey = operationType.ToLowerInvariant().Trim();

            if (_store.Parts.TryGetValue(partKey, out var part))
            {
                if (part.OperationStats.TryGetValue(opKey, out var stats))
                {
                    return stats;
                }
            }
            return null;
        }

        #endregion

        #region Co-occurrence Patterns

        /// <summary>
        /// Record that two parts appeared together in an estimate
        /// </summary>
        public void RecordCoOccurrence(
            string part1Canonical,
            string part1Operation,
            string part2Canonical,
            string part2Operation)
        {
            // Create bidirectional pattern key (sorted for consistency)
            var key1 = $"{part1Canonical}|{part1Operation}";
            var key2 = $"{part2Canonical}|{part2Operation}";
            var patternKey = string.Compare(key1, key2) < 0 ? $"{key1}::{key2}" : $"{key2}::{key1}";

            if (!_store.CoOccurrencePatterns.TryGetValue(patternKey, out var pattern))
            {
                pattern = new CoOccurrencePattern
                {
                    Part1 = part1Canonical,
                    Operation1 = part1Operation,
                    Part2 = part2Canonical,
                    Operation2 = part2Operation,
                    PatternKey = patternKey
                };
                _store.CoOccurrencePatterns[patternKey] = pattern;
            }

            pattern.TimesSeenTogether++;
            pattern.LastSeen = DateTime.Now;

            // Update the part's co-occurrence list
            var part1 = GetOrCreatePart(part1Canonical);
            var existing = part1.CoOccurrences.FirstOrDefault(c =>
                c.AssociatedPart == part2Canonical && c.AssociatedOperation == part2Operation);

            if (existing != null)
            {
                existing.TimesSeenTogether++;
            }
            else
            {
                part1.CoOccurrences.Add(new PartAssociation
                {
                    AssociatedPart = part2Canonical,
                    AssociatedOperation = part2Operation,
                    TimesSeenTogether = 1
                });
            }
        }

        /// <summary>
        /// Get parts that commonly appear with a given part
        /// </summary>
        public List<PartAssociation> GetCoOccurrences(string canonicalPart, double minCoOccurrenceRate = 0.3)
        {
            var part = GetOrCreatePart(canonicalPart);

            // Calculate co-occurrence rates
            foreach (var assoc in part.CoOccurrences)
            {
                assoc.CoOccurrenceRate = part.TimesSeenInEstimates > 0
                    ? (double)assoc.TimesSeenTogether / part.TimesSeenInEstimates
                    : 0;
            }

            return part.CoOccurrences
                .Where(c => c.CoOccurrenceRate >= minCoOccurrenceRate)
                .OrderByDescending(c => c.CoOccurrenceRate)
                .ToList();
        }

        #endregion

        #region Adjacent Panels (for blending)

        /// <summary>
        /// Record that two panels were blended together (indicates adjacency)
        /// </summary>
        public void RecordBlendAdjacency(string refinishedPanel, string blendedPanel)
        {
            var part = GetOrCreatePart(refinishedPanel);
            var blendKey = blendedPanel.ToLowerInvariant().Trim();

            if (!part.LearnedAdjacentPanels.Contains(blendKey))
            {
                part.LearnedAdjacentPanels.Add(blendKey);
            }

            // Track blend frequency
            if (!part.BlendFrequency.ContainsKey(blendKey))
            {
                part.BlendFrequency[blendKey] = 0;
            }
            part.BlendFrequency[blendKey]++;
        }

        /// <summary>
        /// Get learned adjacent panels for blending
        /// </summary>
        public List<string> GetAdjacentPanels(string canonicalPart)
        {
            var part = GetOrCreatePart(canonicalPart);
            return part.LearnedAdjacentPanels
                .OrderByDescending(p => part.BlendFrequency.GetValueOrDefault(p, 0))
                .ToList();
        }

        #endregion

        #region Learned Formulas

        /// <summary>
        /// Record a calculation observation (for formula learning)
        /// </summary>
        public void RecordCalculation(string formulaType, string context, decimal inputValue, decimal outputValue)
        {
            if (!_store.LearnedFormulas.TryGetValue(formulaType, out var formula))
            {
                formula = new LearnedFormula { FormulaType = formulaType };
                _store.LearnedFormulas[formulaType] = formula;
            }

            formula.Observations.Add(new FormulaObservation
            {
                Context = context,
                InputValue = inputValue,
                OutputValue = outputValue,
                ObservedAt = DateTime.Now
            });

            // Try to derive formula from observations
            formula.DeriveFormula();
        }

        /// <summary>
        /// Get a learned formula
        /// </summary>
        public LearnedFormula? GetFormula(string formulaType)
        {
            return _store.LearnedFormulas.GetValueOrDefault(formulaType);
        }

        #endregion

        #region Vehicle-Specific Learning

        private void RecordVehicleSpecificStats(
            string canonicalPart,
            string operationType,
            string vehicleType,
            decimal laborHours,
            decimal refinishHours)
        {
            var vehKey = vehicleType.ToLowerInvariant();
            if (!_store.VehicleSpecificPatterns.TryGetValue(vehKey, out var patterns))
            {
                patterns = new VehiclePatterns { VehicleType = vehKey };
                _store.VehicleSpecificPatterns[vehKey] = patterns;
            }

            var opKey = $"{canonicalPart}|{operationType}";
            if (!patterns.OperationAverages.TryGetValue(opKey, out var avg))
            {
                avg = new VehicleOperationAverage();
                patterns.OperationAverages[opKey] = avg;
            }

            avg.LaborValues.Add(laborHours);
            avg.RefinishValues.Add(refinishHours);
            avg.SampleCount++;

            patterns.TotalOperationsRecorded++;
        }

        /// <summary>
        /// Get vehicle-specific labor time if available
        /// </summary>
        public decimal? GetVehicleSpecificLaborTime(string vehicleType, string canonicalPart, string operationType)
        {
            var vehKey = vehicleType.ToLowerInvariant();
            if (_store.VehicleSpecificPatterns.TryGetValue(vehKey, out var patterns))
            {
                var opKey = $"{canonicalPart}|{operationType}";
                if (patterns.OperationAverages.TryGetValue(opKey, out var avg) && avg.SampleCount >= 1)
                {
                    return avg.LaborValues.Average();
                }
            }
            return null;
        }

        #endregion

        #region Estimate Tracking

        /// <summary>
        /// Record that an estimate was analyzed
        /// </summary>
        public void RecordEstimateAnalyzed(string estimateId, int lineCount, decimal totalValue)
        {
            _store.Metadata.TotalEstimatesAnalyzed++;
            _store.Metadata.TotalLinesAnalyzed += lineCount;
            _store.Metadata.TotalValueAnalyzed += totalValue;
            _store.Metadata.LastUpdated = DateTime.Now;

            _store.AnalyzedEstimateIds.Add(estimateId);
        }

        /// <summary>
        /// Check if an estimate was already analyzed
        /// </summary>
        public bool WasEstimateAnalyzed(string estimateId)
        {
            return _store.AnalyzedEstimateIds.Contains(estimateId);
        }

        #endregion

        #region Persistence

        private KnowledgeStore LoadKnowledge()
        {
            try
            {
                if (File.Exists(_knowledgeFilePath))
                {
                    var json = File.ReadAllText(_knowledgeFilePath);
                    if (!string.IsNullOrWhiteSpace(json) && json != "null")
                    {
                        var store = JsonSerializer.Deserialize<KnowledgeStore>(json);
                        if (store != null) return store;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Knowledge] Error loading: {ex.Message}");

                // Try backup
                try
                {
                    if (File.Exists(_backupPath))
                    {
                        var json = File.ReadAllText(_backupPath);
                        var store = JsonSerializer.Deserialize<KnowledgeStore>(json);
                        if (store != null) return store;
                    }
                }
                catch { }
            }

            return new KnowledgeStore();
        }

        public void Save()
        {
            // Skip saving in Shop mode — baseline data is read-only
            if (LearningModeService.Instance.CurrentMode == LearningMode.Shop)
            {
                System.Diagnostics.Debug.WriteLine("[Knowledge] SHOP mode — skipping save (baseline is read-only)");
                return;
            }

            try
            {
                // Backup existing
                if (File.Exists(_knowledgeFilePath))
                {
                    File.Copy(_knowledgeFilePath, _backupPath, true);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var json = JsonSerializer.Serialize(_store, options);
                File.WriteAllText(_knowledgeFilePath, json);

                System.Diagnostics.Debug.WriteLine($"[Knowledge] Saved: {TotalPartsLearned} parts, {TotalPatternsLearned} patterns");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Knowledge] Error saving: {ex.Message}");
            }
        }

        /// <summary>
        /// Get learning statistics
        /// </summary>
        public KnowledgeStatistics GetStatistics()
        {
            return new KnowledgeStatistics
            {
                TotalEstimatesAnalyzed = _store.Metadata.TotalEstimatesAnalyzed,
                TotalLinesAnalyzed = _store.Metadata.TotalLinesAnalyzed,
                TotalPartsLearned = _store.Parts.Count,
                TotalAliasesLearned = _store.AliasToCanonical.Count,
                TotalCoOccurrencePatterns = _store.CoOccurrencePatterns.Count,
                TotalFormulasLearned = _store.LearnedFormulas.Count,
                TotalValueAnalyzed = _store.Metadata.TotalValueAnalyzed,
                LastUpdated = _store.Metadata.LastUpdated,
                TopParts = _store.Parts.Values
                    .OrderByDescending(p => p.TimesSeenInEstimates)
                    .Take(20)
                    .Select(p => new PartSummary
                    {
                        Name = p.CanonicalName,
                        TimesSeen = p.TimesSeenInEstimates,
                        AliasCount = p.KnownAliases.Count
                    })
                    .ToList()
            };
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Root storage container
    /// </summary>
    public class KnowledgeStore
    {
        public KnowledgeMetadata Metadata { get; set; } = new();
        public Dictionary<string, PartKnowledge> Parts { get; set; } = new();
        public Dictionary<string, string> AliasToCanonical { get; set; } = new();
        public Dictionary<string, CoOccurrencePattern> CoOccurrencePatterns { get; set; } = new();
        public Dictionary<string, LearnedFormula> LearnedFormulas { get; set; } = new();
        public Dictionary<string, VehiclePatterns> VehicleSpecificPatterns { get; set; } = new();
        public HashSet<string> AnalyzedEstimateIds { get; set; } = new();
    }

    public class KnowledgeMetadata
    {
        public int Version { get; set; } = 1;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public int TotalEstimatesAnalyzed { get; set; }
        public int TotalLinesAnalyzed { get; set; }
        public decimal TotalValueAnalyzed { get; set; }
    }

    /// <summary>
    /// Everything known about a part
    /// </summary>
    public class PartKnowledge
    {
        public string CanonicalName { get; set; } = "";
        public List<string> KnownAliases { get; set; } = new();
        public Dictionary<string, OperationStatistics> OperationStats { get; set; } = new();
        public List<PartAssociation> CoOccurrences { get; set; } = new();
        public List<string> LearnedAdjacentPanels { get; set; } = new();
        public Dictionary<string, int> BlendFrequency { get; set; } = new();
        public int TimesSeenInEstimates { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdated { get; set; }

        // Computed properties
        [JsonIgnore]
        public string MostCommonOperation => OperationStats
            .OrderByDescending(o => o.Value.SampleCount)
            .FirstOrDefault().Key ?? "unknown";
    }

    /// <summary>
    /// Statistics for a specific operation on a part
    /// </summary>
    public class OperationStatistics
    {
        public string OperationType { get; set; } = "";
        public int SampleCount { get; set; }
        public DateTime LastSeen { get; set; }

        // Labor hours
        public decimal MeanLaborHours { get; set; }
        public decimal StdDevLaborHours { get; set; }
        public decimal MinLaborHours { get; set; }
        public decimal MaxLaborHours { get; set; }
        public decimal MedianLaborHours { get; set; }
        public List<decimal> LaborHoursValues { get; set; } = new();

        // Refinish hours
        public decimal MeanRefinishHours { get; set; }
        public decimal StdDevRefinishHours { get; set; }
        public decimal MinRefinishHours { get; set; }
        public decimal MaxRefinishHours { get; set; }
        public List<decimal> RefinishHoursValues { get; set; } = new();

        // Price
        public decimal MeanPrice { get; set; }
        public decimal StdDevPrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public List<decimal> PriceValues { get; set; } = new();

        public void RecalculateLaborStats()
        {
            if (LaborHoursValues.Count == 0) return;

            MeanLaborHours = LaborHoursValues.Average();
            MinLaborHours = LaborHoursValues.Min();
            MaxLaborHours = LaborHoursValues.Max();
            MedianLaborHours = GetMedian(LaborHoursValues);
            StdDevLaborHours = CalculateStdDev(LaborHoursValues);
        }

        public void RecalculateRefinishStats()
        {
            if (RefinishHoursValues.Count == 0) return;

            MeanRefinishHours = RefinishHoursValues.Average();
            MinRefinishHours = RefinishHoursValues.Min();
            MaxRefinishHours = RefinishHoursValues.Max();
            StdDevRefinishHours = CalculateStdDev(RefinishHoursValues);
        }

        public void RecalculatePriceStats()
        {
            if (PriceValues.Count == 0) return;

            MeanPrice = PriceValues.Average();
            MinPrice = PriceValues.Min();
            MaxPrice = PriceValues.Max();
            StdDevPrice = CalculateStdDev(PriceValues);
        }

        private static decimal GetMedian(List<decimal> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[mid - 1] + sorted[mid]) / 2
                : sorted[mid];
        }

        private static decimal CalculateStdDev(List<decimal> values)
        {
            if (values.Count < 2) return 0;
            var avg = values.Average();
            var sumOfSquares = values.Sum(v => (v - avg) * (v - avg));
            return (decimal)Math.Sqrt((double)(sumOfSquares / (values.Count - 1)));
        }
    }

    /// <summary>
    /// Association between parts (co-occurrence)
    /// </summary>
    public class PartAssociation
    {
        public string AssociatedPart { get; set; } = "";
        public string AssociatedOperation { get; set; } = "";
        public int TimesSeenTogether { get; set; }
        public double CoOccurrenceRate { get; set; }
    }

    /// <summary>
    /// Pattern of parts appearing together
    /// </summary>
    public class CoOccurrencePattern
    {
        public string PatternKey { get; set; } = "";
        public string Part1 { get; set; } = "";
        public string Operation1 { get; set; } = "";
        public string Part2 { get; set; } = "";
        public string Operation2 { get; set; } = "";
        public int TimesSeenTogether { get; set; }
        public DateTime LastSeen { get; set; }
    }

    /// <summary>
    /// A formula learned from observations
    /// </summary>
    public class LearnedFormula
    {
        public string FormulaType { get; set; } = "";
        public string? DerivedFormula { get; set; }
        public double? DerivedPercentage { get; set; }
        public double? DerivedFlatRate { get; set; }
        public double Confidence { get; set; }
        public int ObservationCount => Observations.Count;
        public List<FormulaObservation> Observations { get; set; } = new();

        public void DeriveFormula()
        {
            if (Observations.Count < 3) return;

            // Try to detect if it's a percentage or flat rate
            var ratios = Observations
                .Where(o => o.InputValue > 0)
                .Select(o => (double)(o.OutputValue / o.InputValue))
                .ToList();

            if (ratios.Count < 3) return;

            var avgRatio = ratios.Average();
            var stdDev = Math.Sqrt(ratios.Sum(r => Math.Pow(r - avgRatio, 2)) / ratios.Count);

            // Low std dev means consistent percentage
            if (stdDev < 0.1)
            {
                DerivedPercentage = avgRatio;
                DerivedFormula = $"{avgRatio:P0} of input";
                Confidence = 1.0 - stdDev;
            }
            else
            {
                // Check for flat rate
                var outputs = Observations.Select(o => (double)o.OutputValue).ToList();
                var outputStdDev = Math.Sqrt(outputs.Sum(o => Math.Pow(o - outputs.Average(), 2)) / outputs.Count);

                if (outputStdDev < 0.3)
                {
                    DerivedFlatRate = outputs.Average();
                    DerivedFormula = $"Flat rate: {outputs.Average():F2}";
                    Confidence = 1.0 - outputStdDev;
                }
            }
        }
    }

    public class FormulaObservation
    {
        public string Context { get; set; } = "";
        public decimal InputValue { get; set; }
        public decimal OutputValue { get; set; }
        public DateTime ObservedAt { get; set; }
    }

    /// <summary>
    /// Vehicle-specific patterns
    /// </summary>
    public class VehiclePatterns
    {
        public string VehicleType { get; set; } = "";
        public Dictionary<string, VehicleOperationAverage> OperationAverages { get; set; } = new();
        public int TotalOperationsRecorded { get; set; }
    }

    public class VehicleOperationAverage
    {
        public List<decimal> LaborValues { get; set; } = new();
        public List<decimal> RefinishValues { get; set; } = new();
        public int SampleCount { get; set; }
    }

    /// <summary>
    /// Statistics summary
    /// </summary>
    public class KnowledgeStatistics
    {
        public int TotalEstimatesAnalyzed { get; set; }
        public int TotalLinesAnalyzed { get; set; }
        public int TotalPartsLearned { get; set; }
        public int TotalAliasesLearned { get; set; }
        public int TotalCoOccurrencePatterns { get; set; }
        public int TotalFormulasLearned { get; set; }
        public decimal TotalValueAnalyzed { get; set; }
        public DateTime LastUpdated { get; set; }
        public List<PartSummary> TopParts { get; set; } = new();
    }

    public class PartSummary
    {
        public string Name { get; set; } = "";
        public int TimesSeen { get; set; }
        public int AliasCount { get; set; }
    }

    #endregion
}
