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
    /// Shop Analytics Service - Tracks performance metrics over time
    ///
    /// Tracks:
    /// - Average supplement amount per claim
    /// - Most commonly missed operations
    /// - Cycle time accuracy
    /// - Labor efficiency ratios
    /// - By estimator performance
    /// - Trends over time
    /// </summary>
    public class ShopAnalyticsService
    {
        private readonly string _dataFilePath;
        private ShopAnalyticsData _analyticsData;

        private static ShopAnalyticsService? _instance;
        public static ShopAnalyticsService Instance => _instance ??= new ShopAnalyticsService();

        public ShopAnalyticsService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(appDataPath);
            _dataFilePath = Path.Combine(appDataPath, "shop_analytics.json");

            _analyticsData = LoadData();
        }

        private ShopAnalyticsData LoadData()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var data = JsonSerializer.Deserialize<ShopAnalyticsData>(json, options);
                    if (data != null) return data;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Analytics] Load error: {ex.Message}");
            }

            return new ShopAnalyticsData();
        }

        public void SaveData()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_analyticsData, options);
                File.WriteAllText(_dataFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Analytics] Save error: {ex.Message}");
            }
        }

        #region Record Estimate Data

        /// <summary>
        /// Record a completed estimate for analytics
        /// </summary>
        public void RecordEstimate(EstimateRecord record)
        {
            record.RecordedDate = DateTime.Now;
            record.Id = Guid.NewGuid().ToString();

            _analyticsData.Estimates.Add(record);

            // Update running totals
            UpdateRunningTotals(record);

            // Update missed items tracking
            if (record.SupplementItems?.Any() == true)
            {
                foreach (var item in record.SupplementItems)
                {
                    TrackMissedItem(item);
                }
            }

            // Update estimator stats
            if (!string.IsNullOrEmpty(record.Estimator))
            {
                UpdateEstimatorStats(record);
            }

            SaveData();
        }

        /// <summary>
        /// Record a supplement addition
        /// </summary>
        public void RecordSupplement(string estimateId, decimal originalTotal, decimal supplementAmount, List<string> addedItems)
        {
            var supplement = new SupplementRecord
            {
                EstimateId = estimateId,
                OriginalTotal = originalTotal,
                SupplementAmount = supplementAmount,
                SupplementPercentage = originalTotal > 0 ? (supplementAmount / originalTotal) * 100 : 0,
                AddedItems = addedItems,
                RecordedDate = DateTime.Now
            };

            _analyticsData.Supplements.Add(supplement);

            // Track commonly added supplement items
            foreach (var item in addedItems)
            {
                TrackSupplementItem(item);
            }

            SaveData();
        }

        /// <summary>
        /// Record cycle time for an estimate
        /// </summary>
        public void RecordCycleTime(string estimateId, int estimatedDays, int actualDays)
        {
            var cycleRecord = new CycleTimeRecord
            {
                EstimateId = estimateId,
                EstimatedDays = estimatedDays,
                ActualDays = actualDays,
                Variance = actualDays - estimatedDays,
                RecordedDate = DateTime.Now
            };

            _analyticsData.CycleTimes.Add(cycleRecord);
            SaveData();
        }

        #endregion

        #region Analytics Queries

        /// <summary>
        /// Get overall shop performance summary
        /// </summary>
        public ShopPerformanceSummary GetPerformanceSummary(int? lastDays = null)
        {
            var estimates = FilterByDate(_analyticsData.Estimates, lastDays);
            var supplements = FilterByDate(_analyticsData.Supplements, lastDays);
            var cycleTimes = FilterByDate(_analyticsData.CycleTimes, lastDays);

            var summary = new ShopPerformanceSummary
            {
                TotalEstimates = estimates.Count,
                TotalSupplements = supplements.Count,
                DateRange = lastDays.HasValue ? $"Last {lastDays} days" : "All time"
            };

            if (estimates.Any())
            {
                summary.AverageEstimateTotal = estimates.Average(e => e.TotalAmount);
                summary.AverageLaborHours = estimates.Average(e => e.TotalLaborHours);
                summary.AverageRefinishHours = estimates.Average(e => e.TotalRefinishHours);
            }

            if (supplements.Any())
            {
                summary.AverageSupplementAmount = supplements.Average(s => s.SupplementAmount);
                summary.AverageSupplementPercentage = supplements.Average(s => s.SupplementPercentage);
                summary.TotalSupplementDollars = supplements.Sum(s => s.SupplementAmount);
            }

            if (cycleTimes.Any())
            {
                summary.AverageCycleTimeVariance = cycleTimes.Average(c => c.Variance);
                summary.OnTimePerecentage = cycleTimes.Count(c => c.Variance <= 0) * 100.0 / cycleTimes.Count;
            }

            return summary;
        }

        /// <summary>
        /// Get most commonly missed items
        /// </summary>
        public List<MissedItemStats> GetMostCommonlyMissedItems(int topN = 10)
        {
            return _analyticsData.MissedItemCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(topN)
                .Select(kvp => new MissedItemStats
                {
                    ItemName = kvp.Key,
                    TimesAdded = kvp.Value,
                    Percentage = _analyticsData.TotalEstimatesAnalyzed > 0
                        ? (kvp.Value * 100.0 / _analyticsData.TotalEstimatesAnalyzed)
                        : 0
                })
                .ToList();
        }

        /// <summary>
        /// Get estimator performance comparison
        /// </summary>
        public List<EstimatorPerformance> GetEstimatorPerformance()
        {
            return _analyticsData.EstimatorStats.Values
                .OrderByDescending(e => e.AverageSupplementCaptured)
                .ToList();
        }

        /// <summary>
        /// Get supplement trends over time
        /// </summary>
        public List<MonthlyTrend> GetSupplementTrends(int months = 12)
        {
            var startDate = DateTime.Now.AddMonths(-months);

            return _analyticsData.Supplements
                .Where(s => s.RecordedDate >= startDate)
                .GroupBy(s => new { s.RecordedDate.Year, s.RecordedDate.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new MonthlyTrend
                {
                    Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                    AverageSupplementAmount = g.Average(s => s.SupplementAmount),
                    TotalSupplements = g.Count(),
                    TotalDollars = g.Sum(s => s.SupplementAmount)
                })
                .ToList();
        }

        /// <summary>
        /// Get operations that capture the most supplement dollars
        /// </summary>
        public List<SupplementItemStats> GetTopSupplementItems(int topN = 10)
        {
            return _analyticsData.SupplementItemCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(topN)
                .Select(kvp => new SupplementItemStats
                {
                    ItemName = kvp.Key,
                    TimesAdded = kvp.Value
                })
                .ToList();
        }

        /// <summary>
        /// Get cycle time analysis
        /// </summary>
        public CycleTimeAnalysis GetCycleTimeAnalysis()
        {
            var analysis = new CycleTimeAnalysis();

            if (!_analyticsData.CycleTimes.Any())
                return analysis;

            var records = _analyticsData.CycleTimes;

            analysis.TotalRecords = records.Count;
            analysis.AverageEstimatedDays = records.Average(c => c.EstimatedDays);
            analysis.AverageActualDays = records.Average(c => c.ActualDays);
            analysis.AverageVariance = records.Average(c => c.Variance);

            analysis.OnTimeCount = records.Count(c => c.Variance <= 0);
            analysis.LateCount = records.Count(c => c.Variance > 0);
            analysis.OnTimePercentage = analysis.TotalRecords > 0
                ? (analysis.OnTimeCount * 100.0 / analysis.TotalRecords)
                : 0;

            // Distribution of variance
            analysis.Within1Day = records.Count(c => Math.Abs(c.Variance) <= 1);
            analysis.Within3Days = records.Count(c => Math.Abs(c.Variance) <= 3);
            analysis.MoreThan3Days = records.Count(c => Math.Abs(c.Variance) > 3);

            return analysis;
        }

        /// <summary>
        /// Get performance by vehicle make
        /// </summary>
        public List<MakePerformance> GetPerformanceByMake()
        {
            return _analyticsData.Estimates
                .Where(e => !string.IsNullOrEmpty(e.VehicleMake))
                .GroupBy(e => e.VehicleMake!)
                .Select(g => new MakePerformance
                {
                    Make = g.Key,
                    EstimateCount = g.Count(),
                    AverageTotal = g.Average(e => e.TotalAmount),
                    AverageLaborHours = g.Average(e => e.TotalLaborHours)
                })
                .OrderByDescending(m => m.EstimateCount)
                .Take(15)
                .ToList();
        }

        #endregion

        #region Private Helpers

        private void UpdateRunningTotals(EstimateRecord record)
        {
            _analyticsData.TotalEstimatesAnalyzed++;
            _analyticsData.TotalDollarsEstimated += record.TotalAmount;
            _analyticsData.TotalLaborHours += record.TotalLaborHours;
            _analyticsData.TotalRefinishHours += record.TotalRefinishHours;
            _analyticsData.LastUpdated = DateTime.Now;
        }

        private void TrackMissedItem(string itemName)
        {
            var key = itemName.ToLowerInvariant();
            if (_analyticsData.MissedItemCounts.ContainsKey(key))
            {
                _analyticsData.MissedItemCounts[key]++;
            }
            else
            {
                _analyticsData.MissedItemCounts[key] = 1;
            }
        }

        private void TrackSupplementItem(string itemName)
        {
            var key = itemName.ToLowerInvariant();
            if (_analyticsData.SupplementItemCounts.ContainsKey(key))
            {
                _analyticsData.SupplementItemCounts[key]++;
            }
            else
            {
                _analyticsData.SupplementItemCounts[key] = 1;
            }
        }

        private void UpdateEstimatorStats(EstimateRecord record)
        {
            var name = record.Estimator!;

            if (!_analyticsData.EstimatorStats.ContainsKey(name))
            {
                _analyticsData.EstimatorStats[name] = new EstimatorPerformance { Name = name };
            }

            var stats = _analyticsData.EstimatorStats[name];
            stats.TotalEstimates++;
            stats.TotalDollars += record.TotalAmount;

            // Recalculate averages
            stats.AverageEstimateTotal = stats.TotalDollars / stats.TotalEstimates;

            if (record.SupplementAmount > 0)
            {
                stats.TotalSupplementsCaptured++;
                stats.TotalSupplementDollars += record.SupplementAmount;
                stats.AverageSupplementCaptured = stats.TotalSupplementDollars / stats.TotalSupplementsCaptured;
            }
        }

        private List<T> FilterByDate<T>(List<T> items, int? lastDays) where T : IHasRecordedDate
        {
            if (!lastDays.HasValue)
                return items;

            var cutoff = DateTime.Now.AddDays(-lastDays.Value);
            return items.Where(i => i.RecordedDate >= cutoff).ToList();
        }

        #endregion

        #region Export / Import

        /// <summary>
        /// Export analytics data to JSON
        /// </summary>
        public string ExportToJson()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(_analyticsData, options);
        }

        /// <summary>
        /// Get a printable performance report
        /// </summary>
        public string GeneratePerformanceReport(int? lastDays = 30)
        {
            var summary = GetPerformanceSummary(lastDays);
            var missed = GetMostCommonlyMissedItems(5);
            var estimators = GetEstimatorPerformance();

            var report = new System.Text.StringBuilder();

            report.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            report.AppendLine("║           SHOP PERFORMANCE REPORT                            ║");
            report.AppendLine($"║           {summary.DateRange,-42}     ║");
            report.AppendLine("╠══════════════════════════════════════════════════════════════╣");
            report.AppendLine("║ ESTIMATES                                                    ║");
            report.AppendLine($"║   Total Estimates:        {summary.TotalEstimates,8}                        ║");
            report.AppendLine($"║   Average Total:         ${summary.AverageEstimateTotal,10:N2}                     ║");
            report.AppendLine($"║   Avg Labor Hours:       {summary.AverageLaborHours,10:N1}                      ║");
            report.AppendLine($"║   Avg Refinish Hours:    {summary.AverageRefinishHours,10:N1}                      ║");
            report.AppendLine("╠══════════════════════════════════════════════════════════════╣");
            report.AppendLine("║ SUPPLEMENTS                                                  ║");
            report.AppendLine($"║   Total Supplements:      {summary.TotalSupplements,8}                        ║");
            report.AppendLine($"║   Average Amount:        ${summary.AverageSupplementAmount,10:N2}                     ║");
            report.AppendLine($"║   Average Percentage:    {summary.AverageSupplementPercentage,10:N1}%                     ║");
            report.AppendLine($"║   Total Captured:       ${summary.TotalSupplementDollars,11:N2}                    ║");
            report.AppendLine("╠══════════════════════════════════════════════════════════════╣");
            report.AppendLine("║ TOP MISSED ITEMS                                             ║");

            foreach (var item in missed)
            {
                var itemName = item.ItemName.Length > 30 ? item.ItemName.Substring(0, 30) : item.ItemName;
                report.AppendLine($"║   {itemName,-32} {item.TimesAdded,5}x ({item.Percentage:N1}%) ║");
            }

            report.AppendLine("╠══════════════════════════════════════════════════════════════╣");
            report.AppendLine("║ ESTIMATOR PERFORMANCE                                        ║");

            foreach (var est in estimators.Take(5))
            {
                var name = est.Name.Length > 20 ? est.Name.Substring(0, 20) : est.Name;
                report.AppendLine($"║   {name,-20} {est.TotalEstimates,4} est  ${est.AverageSupplementCaptured,8:N0} avg supp ║");
            }

            report.AppendLine("╚══════════════════════════════════════════════════════════════╝");

            return report.ToString();
        }

        /// <summary>
        /// Reset all analytics data
        /// </summary>
        public void ResetAllData()
        {
            _analyticsData = new ShopAnalyticsData();
            SaveData();
        }

        #endregion
    }

    #region Analytics Data Models

    public interface IHasRecordedDate
    {
        DateTime RecordedDate { get; }
    }

    public class ShopAnalyticsData
    {
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public int TotalEstimatesAnalyzed { get; set; }
        public decimal TotalDollarsEstimated { get; set; }
        public decimal TotalLaborHours { get; set; }
        public decimal TotalRefinishHours { get; set; }

        public List<EstimateRecord> Estimates { get; set; } = new();
        public List<SupplementRecord> Supplements { get; set; } = new();
        public List<CycleTimeRecord> CycleTimes { get; set; } = new();

        public Dictionary<string, int> MissedItemCounts { get; set; } = new();
        public Dictionary<string, int> SupplementItemCounts { get; set; } = new();
        public Dictionary<string, EstimatorPerformance> EstimatorStats { get; set; } = new();
    }

    public class EstimateRecord : IHasRecordedDate
    {
        public string Id { get; set; } = "";
        public DateTime RecordedDate { get; set; }
        public string? Estimator { get; set; }
        public string? VehicleMake { get; set; }
        public string? VehicleModel { get; set; }
        public int? VehicleYear { get; set; }

        public decimal TotalAmount { get; set; }
        public decimal TotalLaborHours { get; set; }
        public decimal TotalRefinishHours { get; set; }
        public int OperationCount { get; set; }

        public decimal SupplementAmount { get; set; }
        public List<string>? SupplementItems { get; set; }
    }

    public class SupplementRecord : IHasRecordedDate
    {
        public string EstimateId { get; set; } = "";
        public DateTime RecordedDate { get; set; }
        public decimal OriginalTotal { get; set; }
        public decimal SupplementAmount { get; set; }
        public decimal SupplementPercentage { get; set; }
        public List<string> AddedItems { get; set; } = new();
    }

    public class CycleTimeRecord : IHasRecordedDate
    {
        public string EstimateId { get; set; } = "";
        public DateTime RecordedDate { get; set; }
        public int EstimatedDays { get; set; }
        public int ActualDays { get; set; }
        public int Variance { get; set; }
    }

    public class EstimatorPerformance
    {
        public string Name { get; set; } = "";
        public int TotalEstimates { get; set; }
        public decimal TotalDollars { get; set; }
        public decimal AverageEstimateTotal { get; set; }
        public int TotalSupplementsCaptured { get; set; }
        public decimal TotalSupplementDollars { get; set; }
        public decimal AverageSupplementCaptured { get; set; }
    }

    // Output models
    public class ShopPerformanceSummary
    {
        public string DateRange { get; set; } = "";
        public int TotalEstimates { get; set; }
        public int TotalSupplements { get; set; }
        public decimal AverageEstimateTotal { get; set; }
        public decimal AverageLaborHours { get; set; }
        public decimal AverageRefinishHours { get; set; }
        public decimal AverageSupplementAmount { get; set; }
        public decimal AverageSupplementPercentage { get; set; }
        public decimal TotalSupplementDollars { get; set; }
        public double AverageCycleTimeVariance { get; set; }
        public double OnTimePerecentage { get; set; }
    }

    public class MissedItemStats
    {
        public string ItemName { get; set; } = "";
        public int TimesAdded { get; set; }
        public double Percentage { get; set; }
    }

    public class SupplementItemStats
    {
        public string ItemName { get; set; } = "";
        public int TimesAdded { get; set; }
    }

    public class MonthlyTrend
    {
        public string Month { get; set; } = "";
        public decimal AverageSupplementAmount { get; set; }
        public int TotalSupplements { get; set; }
        public decimal TotalDollars { get; set; }
    }

    public class CycleTimeAnalysis
    {
        public int TotalRecords { get; set; }
        public double AverageEstimatedDays { get; set; }
        public double AverageActualDays { get; set; }
        public double AverageVariance { get; set; }
        public int OnTimeCount { get; set; }
        public int LateCount { get; set; }
        public double OnTimePercentage { get; set; }
        public int Within1Day { get; set; }
        public int Within3Days { get; set; }
        public int MoreThan3Days { get; set; }
    }

    public class MakePerformance
    {
        public string Make { get; set; } = "";
        public int EstimateCount { get; set; }
        public decimal AverageTotal { get; set; }
        public decimal AverageLaborHours { get; set; }
    }

    #endregion
}
