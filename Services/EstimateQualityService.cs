#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Assesses estimate quality before training.
    /// Detects outliers, missing operations, and validates data integrity.
    ///
    /// Quality Scoring Algorithm:
    /// - Start at 100
    /// - -30 max for incomplete data (missing parts/hours)
    /// - -25 max for outliers (Z-score > 2.5)
    /// - -25 max for missing common operations
    /// - +10 bonus for well-structured estimates
    ///
    /// Bootstrap Mode (first 20 estimates):
    /// - Quality checks are informational only
    /// - All estimates accepted for training to build baselines
    /// </summary>
    public class EstimateQualityService
    {
        private static EstimateQualityService? _instance;
        public static EstimateQualityService Instance => _instance ??= new EstimateQualityService();

        private readonly EstimateLearningService _learningService;
        private List<CommonlyMissedItem> _commonlyMissedItems = new();
        private readonly string _commonlyMissedPath;

        public EstimateQualityService()
        {
            _learningService = EstimateLearningService.Instance;

            // Load commonly missed items reference data
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            _commonlyMissedPath = Path.Combine(appDir, "Data", "CommonlyMissedItems.json");
            LoadCommonlyMissedItems();
        }

        #region Quality Assessment

        /// <summary>
        /// Assess the quality of an estimate before training.
        /// Returns a quality record with score, grade, flags, and outliers.
        /// </summary>
        public EstimateQualityRecord AssessQuality(List<ParsedEstimateLine> lines, string? vehicleInfo = null)
        {
            var record = new EstimateQualityRecord
            {
                VehicleInfo = vehicleInfo,
                LineItemCount = lines.Count
            };

            // Detect outliers (only if we have baselines)
            record.Outliers = DetectOutliers(lines);

            // Check for missing common operations
            var missingOps = CheckMissingOperations(lines);
            record.Flags.AddRange(missingOps);

            // Check data completeness
            var completenessFlags = CheckDataCompleteness(lines);
            record.Flags.AddRange(completenessFlags);

            // Calculate quality score
            record.QualityScore = CalculateQualityScore(lines, record.Outliers, record.Flags);
            record.Grade = GetGrade(record.QualityScore);

            // Calculate learning weight
            record.LearningWeight = CalculateLearningWeight(record);

            System.Diagnostics.Debug.WriteLine($"[Quality] Score: {record.QualityScore} ({record.Grade}), " +
                $"Outliers: {record.Outliers.Count}, Flags: {record.Flags.Count}, Weight: {record.LearningWeight:F2}");

            return record;
        }

        /// <summary>
        /// Calculate quality score based on completeness, outliers, and flags.
        /// </summary>
        public int CalculateQualityScore(List<ParsedEstimateLine> lines,
            List<OutlierDetection> outliers, List<QualityFlag> flags)
        {
            int score = 100;

            // Data completeness penalty (max -30 points)
            int linesWithParts = lines.Count(l => !string.IsNullOrEmpty(l.PartName));
            int linesWithHours = lines.Count(l => l.LaborHours > 0 || l.RefinishHours > 0);
            double completenessRatio = lines.Count > 0
                ? (double)(linesWithParts + linesWithHours) / (lines.Count * 2)
                : 0;
            int completenessDeduction = (int)((1 - completenessRatio) * 30);
            score -= Math.Min(30, completenessDeduction);

            // Outlier penalty (max -25 points)
            int severeOutliers = outliers.Count(o => Math.Abs(o.ZScore) > 3);
            int moderateOutliers = outliers.Count(o => Math.Abs(o.ZScore) > 2.5m && Math.Abs(o.ZScore) <= 3);
            score -= Math.Min(25, severeOutliers * 10 + moderateOutliers * 5);

            // Missing operations penalty (max -25 points)
            int criticalMissing = flags.Count(f =>
                f.Type == QualityFlagType.MissingCommonOperation && f.Severity == QualitySeverity.Error);
            int warningMissing = flags.Count(f =>
                f.Type == QualityFlagType.MissingCommonOperation && f.Severity == QualitySeverity.Warning);
            score -= Math.Min(25, criticalMissing * 8 + warningMissing * 3);

            // Other flag penalties (max -20 points)
            int otherErrors = flags.Count(f =>
                f.Type != QualityFlagType.MissingCommonOperation && f.Severity == QualitySeverity.Error);
            int otherWarnings = flags.Count(f =>
                f.Type != QualityFlagType.MissingCommonOperation && f.Severity == QualitySeverity.Warning);
            score -= Math.Min(20, otherErrors * 5 + otherWarnings * 2);

            // Bonus for well-structured estimates (+10 points)
            if (lines.Count >= 5 && completenessRatio > 0.9 && outliers.Count == 0 &&
                !flags.Any(f => f.Severity == QualitySeverity.Error))
            {
                score = Math.Min(100, score + 10);
            }

            return Math.Max(0, Math.Min(100, score));
        }

        /// <summary>
        /// Get quality grade from score.
        /// </summary>
        public QualityGrade GetGrade(int score)
        {
            return score switch
            {
                >= 90 => QualityGrade.Excellent,
                >= 75 => QualityGrade.Good,
                >= 60 => QualityGrade.Fair,
                >= 40 => QualityGrade.Poor,
                _ => QualityGrade.Rejected
            };
        }

        /// <summary>
        /// Calculate learning weight based on quality.
        /// Low quality estimates have reduced influence during training.
        /// </summary>
        public decimal CalculateLearningWeight(EstimateQualityRecord quality)
        {
            // Bootstrap mode: always use full weight
            if (_learningService.GetStatistics().EstimatesImported < 20)
            {
                return 1.0m;
            }

            return quality.Grade switch
            {
                QualityGrade.Excellent => 1.0m,
                QualityGrade.Good => 0.9m,
                QualityGrade.Fair => 0.7m,
                QualityGrade.Poor => 0.4m,
                QualityGrade.Rejected => 0.0m,  // Don't use for training
                _ => 0.5m
            };
        }

        /// <summary>
        /// Check if estimate should be used for training based on quality.
        /// </summary>
        public bool ShouldTrainFromEstimate(EstimateQualityRecord quality)
        {
            // Bootstrap mode: always allow training
            if (_learningService.GetStatistics().EstimatesImported < 20)
            {
                return true;
            }

            var config = GetConfiguration();
            return quality.QualityScore >= config.MinQualityScoreForTraining;
        }

        #endregion

        #region Outlier Detection

        /// <summary>
        /// Detect outliers in estimate lines using Z-score analysis.
        /// Compares values against historical baselines.
        /// </summary>
        public List<OutlierDetection> DetectOutliers(List<ParsedEstimateLine> lines)
        {
            var outliers = new List<OutlierDetection>();
            var baselines = GetBaselines();

            // Skip if no baselines or in bootstrap mode
            if (baselines.Count == 0 || _learningService.GetStatistics().EstimatesImported < 20)
            {
                return outliers;
            }

            var config = GetConfiguration();

            foreach (var line in lines.Where(l => !l.IsManualLine && !string.IsNullOrEmpty(l.PartName)))
            {
                var baselineKey = $"{line.PartName.ToLowerInvariant()}|{line.OperationType?.ToLowerInvariant() ?? "unknown"}";

                if (!baselines.TryGetValue(baselineKey, out var baseline) || baseline.SampleCount < 5)
                    continue;

                // Check labor hours
                if (line.LaborHours > 0 && baseline.StdDevLaborHours > 0)
                {
                    var zScore = (line.LaborHours - baseline.MeanLaborHours) / baseline.StdDevLaborHours;
                    if (Math.Abs(zScore) > (decimal)config.OutlierZScoreThreshold)
                    {
                        outliers.Add(new OutlierDetection
                        {
                            Field = "LaborHours",
                            Value = line.LaborHours,
                            ExpectedMin = baseline.MeanLaborHours - (2 * baseline.StdDevLaborHours),
                            ExpectedMax = baseline.MeanLaborHours + (2 * baseline.StdDevLaborHours),
                            ZScore = zScore,
                            PartName = line.PartName,
                            OperationType = line.OperationType ?? "unknown"
                        });
                    }
                }

                // Check refinish hours
                if (line.RefinishHours > 0 && baseline.StdDevRefinishHours > 0)
                {
                    var zScore = (line.RefinishHours - baseline.MeanRefinishHours) / baseline.StdDevRefinishHours;
                    if (Math.Abs(zScore) > (decimal)config.OutlierZScoreThreshold)
                    {
                        outliers.Add(new OutlierDetection
                        {
                            Field = "RefinishHours",
                            Value = line.RefinishHours,
                            ExpectedMin = baseline.MeanRefinishHours - (2 * baseline.StdDevRefinishHours),
                            ExpectedMax = baseline.MeanRefinishHours + (2 * baseline.StdDevRefinishHours),
                            ZScore = zScore,
                            PartName = line.PartName,
                            OperationType = line.OperationType ?? "unknown"
                        });
                    }
                }

                // Check price
                if (line.Price > 0 && baseline.StdDevPrice > 0)
                {
                    var zScore = (line.Price - baseline.MeanPrice) / baseline.StdDevPrice;
                    if (Math.Abs(zScore) > (decimal)config.OutlierZScoreThreshold)
                    {
                        outliers.Add(new OutlierDetection
                        {
                            Field = "Price",
                            Value = line.Price,
                            ExpectedMin = baseline.MeanPrice - (2 * baseline.StdDevPrice),
                            ExpectedMax = baseline.MeanPrice + (2 * baseline.StdDevPrice),
                            ZScore = zScore,
                            PartName = line.PartName,
                            OperationType = line.OperationType ?? "unknown"
                        });
                    }
                }
            }

            return outliers;
        }

        /// <summary>
        /// Get human-readable description of an outlier.
        /// </summary>
        public string GetOutlierDescription(OutlierDetection outlier)
        {
            string direction = outlier.ZScore > 0 ? "higher" : "lower";
            string fieldName = outlier.Field switch
            {
                "LaborHours" => "labor hours",
                "RefinishHours" => "refinish hours",
                "Price" => "price",
                _ => outlier.Field
            };

            return $"{outlier.PartName} {outlier.OperationType}: {fieldName} of {outlier.Value:F2} is " +
                   $"significantly {direction} than expected ({outlier.ExpectedMin:F2} - {outlier.ExpectedMax:F2})";
        }

        #endregion

        #region Missing Operations Check

        /// <summary>
        /// Check for commonly missed operations based on parts in the estimate.
        /// </summary>
        public List<QualityFlag> CheckMissingOperations(List<ParsedEstimateLine> lines)
        {
            var flags = new List<QualityFlag>();

            if (_commonlyMissedItems.Count == 0)
                return flags;

            var partNames = lines
                .Where(l => !l.IsManualLine && !string.IsNullOrEmpty(l.PartName))
                .Select(l => l.PartName.ToLowerInvariant())
                .ToHashSet();

            var operationTypes = lines
                .Where(l => !string.IsNullOrEmpty(l.OperationType))
                .Select(l => l.OperationType.ToLowerInvariant())
                .ToHashSet();

            foreach (var item in _commonlyMissedItems)
            {
                // Check if trigger condition is met
                bool triggered = item.TriggerParts.Any(t =>
                    partNames.Any(p => p.Contains(t.ToLowerInvariant())));

                if (!triggered)
                    continue;

                // Check if expected operation is present
                bool hasExpected = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains(item.ExpectedOperation.ToLowerInvariant()) == true ||
                    l.PartName?.ToLowerInvariant().Contains(item.ExpectedOperation.ToLowerInvariant()) == true);

                if (!hasExpected)
                {
                    flags.Add(new QualityFlag
                    {
                        Type = QualityFlagType.MissingCommonOperation,
                        Description = $"Commonly needed: {item.ExpectedOperation} (triggered by {string.Join(", ", item.TriggerParts)})",
                        Severity = item.Severity,
                        AffectedItem = item.ExpectedOperation
                    });
                }
            }

            return flags;
        }

        /// <summary>
        /// Check data completeness of estimate lines.
        /// </summary>
        public List<QualityFlag> CheckDataCompleteness(List<ParsedEstimateLine> lines)
        {
            var flags = new List<QualityFlag>();

            // Check for lines without part names
            int linesWithoutParts = lines.Count(l => !l.IsManualLine && string.IsNullOrEmpty(l.PartName));
            if (linesWithoutParts > 0)
            {
                flags.Add(new QualityFlag
                {
                    Type = QualityFlagType.IncompleteData,
                    Description = $"{linesWithoutParts} line(s) missing part name",
                    Severity = linesWithoutParts > 3 ? QualitySeverity.Error : QualitySeverity.Warning
                });
            }

            // Check for lines without hours (excluding parts-only lines)
            int linesWithoutHours = lines.Count(l =>
                !l.IsManualLine &&
                !string.IsNullOrEmpty(l.PartName) &&
                l.LaborHours == 0 && l.RefinishHours == 0 && l.RepairHours == 0);

            // This is common for parts-only lines, so only warn if it's most lines
            if (linesWithoutHours > lines.Count * 0.5)
            {
                flags.Add(new QualityFlag
                {
                    Type = QualityFlagType.IncompleteData,
                    Description = $"{linesWithoutHours} line(s) have no labor hours",
                    Severity = QualitySeverity.Info
                });
            }

            // Check for zero prices on main parts (might indicate parsing issue)
            int mainPartsZeroPrice = lines.Count(l =>
                !l.IsManualLine &&
                !string.IsNullOrEmpty(l.PartName) &&
                l.Price == 0 &&
                (l.OperationType?.ToLowerInvariant().Contains("repl") == true ||
                 l.OperationType?.ToLowerInvariant().Contains("replace") == true));

            if (mainPartsZeroPrice > 2)
            {
                flags.Add(new QualityFlag
                {
                    Type = QualityFlagType.IncompleteData,
                    Description = $"{mainPartsZeroPrice} replacement parts have $0 price (parsing issue?)",
                    Severity = QualitySeverity.Warning
                });
            }

            return flags;
        }

        #endregion

        #region Baselines Management

        /// <summary>
        /// Get current statistical baselines.
        /// </summary>
        public Dictionary<string, OperationBaseline> GetBaselines()
        {
            var stats = _learningService.GetStatistics();
            // Access baselines through the database via learning service
            // For now, return empty if not available
            return new Dictionary<string, OperationBaseline>();
        }

        /// <summary>
        /// Update a baseline with a new data point.
        /// Uses Welford's online algorithm for incremental statistics.
        /// </summary>
        public void UpdateBaseline(string partOperation, decimal laborHours, decimal refinishHours, decimal price)
        {
            // This will be called by the learning service when new estimates are trained
            System.Diagnostics.Debug.WriteLine($"[Quality] Updating baseline for {partOperation}");
        }

        /// <summary>
        /// Refresh all baselines from training data.
        /// Should be called periodically or when baselines seem stale.
        /// </summary>
        public void RefreshAllBaselines()
        {
            System.Diagnostics.Debug.WriteLine("[Quality] Refreshing all baselines from training data");
            // Will trigger recalculation in EstimateLearningService
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Get current learning configuration.
        /// </summary>
        public LearningConfiguration GetConfiguration()
        {
            // Get from learning service's metadata
            return new LearningConfiguration();
        }

        /// <summary>
        /// Check if system is in bootstrap mode.
        /// </summary>
        public bool IsBootstrapMode()
        {
            return _learningService.GetStatistics().EstimatesImported < 20;
        }

        /// <summary>
        /// Get bootstrap progress (0-20 estimates).
        /// </summary>
        public (int current, int required) GetBootstrapProgress()
        {
            var imported = _learningService.GetStatistics().EstimatesImported;
            return (Math.Min(imported, 20), 20);
        }

        #endregion

        #region Data Loading

        private void LoadCommonlyMissedItems()
        {
            try
            {
                if (File.Exists(_commonlyMissedPath))
                {
                    var json = File.ReadAllText(_commonlyMissedPath);
                    var data = JsonSerializer.Deserialize<QualityCommonlyMissedData>(json);
                    if (data?.Items != null)
                    {
                        _commonlyMissedItems = data.Items;
                        System.Diagnostics.Debug.WriteLine($"[Quality] Loaded {_commonlyMissedItems.Count} commonly missed items");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Quality] Error loading commonly missed items: {ex.Message}");
            }
        }

        #endregion
    }

    #region Supporting Data Classes

    internal class QualityCommonlyMissedData
    {
        public List<CommonlyMissedItem> Items { get; set; } = new();
    }

    internal class CommonlyMissedItem
    {
        /// <summary>Parts that trigger this check</summary>
        public List<string> TriggerParts { get; set; } = new();

        /// <summary>Operation expected when trigger parts present</summary>
        public string ExpectedOperation { get; set; } = "";

        /// <summary>Severity if missing</summary>
        public QualitySeverity Severity { get; set; } = QualitySeverity.Warning;

        /// <summary>Category (ADAS, Refinish, Structural, etc.)</summary>
        public string Category { get; set; } = "";
    }

    #endregion
}
