#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Tracks statistics for estimate imports
    /// - Learn mode: estimates used for training the system
    /// - Build mode: estimates used to generate/recommend operations
    /// </summary>
    public class EstimateStatisticsService
    {
        private const string StatsFilePath = "estimate_stats.json";
        private EstimateStatsData _data;
        private readonly object _lock = new object();

        private static EstimateStatisticsService? _instance;
        public static EstimateStatisticsService Instance => _instance ??= new EstimateStatisticsService();

        private EstimateStatisticsService()
        {
            _data = LoadStats();
        }

        /// <summary>
        /// Record an estimate import for LEARNING (training the AI)
        /// </summary>
        public void RecordLearnImport(string vehicleInfo, int patternsLearned, int lineItemsProcessed)
        {
            lock (_lock)
            {
                var record = new EstimateImportRecord
                {
                    Timestamp = DateTime.Now,
                    Mode = EstimateImportMode.Learn,
                    UserId = GetCurrentUserId(),
                    VehicleInfo = vehicleInfo,
                    PatternsLearned = patternsLearned,
                    LineItemsProcessed = lineItemsProcessed
                };

                _data.Imports.Add(record);
                SaveStats();
            }
        }

        /// <summary>
        /// Record an estimate import for BUILDING (generating operations)
        /// </summary>
        public void RecordBuildImport(string vehicleInfo, int operationsGenerated, int suggestionsApplied)
        {
            lock (_lock)
            {
                var record = new EstimateImportRecord
                {
                    Timestamp = DateTime.Now,
                    Mode = EstimateImportMode.Build,
                    UserId = GetCurrentUserId(),
                    VehicleInfo = vehicleInfo,
                    OperationsGenerated = operationsGenerated,
                    SuggestionsApplied = suggestionsApplied
                };

                _data.Imports.Add(record);
                SaveStats();
            }
        }

        /// <summary>
        /// Get combined statistics for a period
        /// </summary>
        public EstimateStatsResult GetStats(StatsPeriod period)
        {
            var records = FilterByPeriod(_data.Imports, period).ToList();

            var learnRecords = records.Where(r => r.Mode == EstimateImportMode.Learn).ToList();
            var buildRecords = records.Where(r => r.Mode == EstimateImportMode.Build).ToList();

            return new EstimateStatsResult
            {
                PeriodName = period.ToString(),

                // Learn stats
                LearnImports = learnRecords.Count,
                TotalPatternsLearned = learnRecords.Sum(r => r.PatternsLearned),
                TotalLineItemsProcessed = learnRecords.Sum(r => r.LineItemsProcessed),

                // Build stats
                BuildImports = buildRecords.Count,
                TotalOperationsGenerated = buildRecords.Sum(r => r.OperationsGenerated),
                TotalSuggestionsApplied = buildRecords.Sum(r => r.SuggestionsApplied)
            };
        }

        public EstimateStatsResult GetTodayStats() => GetStats(StatsPeriod.Today);
        public EstimateStatsResult GetWeekStats() => GetStats(StatsPeriod.ThisWeek);
        public EstimateStatsResult GetMonthStats() => GetStats(StatsPeriod.ThisMonth);
        public EstimateStatsResult GetAllTimeStats() => GetStats(StatsPeriod.AllTime);

        /// <summary>
        /// Get recent import records
        /// </summary>
        public List<EstimateImportRecord> GetRecentImports(int count = 10)
        {
            return _data.Imports
                .OrderByDescending(r => r.Timestamp)
                .Take(count)
                .ToList();
        }

        private IEnumerable<EstimateImportRecord> FilterByPeriod(IEnumerable<EstimateImportRecord> records, StatsPeriod period)
        {
            var today = DateTime.Today;
            return period switch
            {
                StatsPeriod.Today => records.Where(r => r.Timestamp.Date == today),
                StatsPeriod.ThisWeek => records.Where(r => r.Timestamp.Date >= today.AddDays(-(int)today.DayOfWeek)),
                StatsPeriod.ThisMonth => records.Where(r => r.Timestamp.Date >= new DateTime(today.Year, today.Month, 1)),
                StatsPeriod.ThisYear => records.Where(r => r.Timestamp.Date >= new DateTime(today.Year, 1, 1)),
                StatsPeriod.AllTime => records,
                _ => records
            };
        }

        private EstimateStatsData LoadStats()
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "McStudDesktop"
                );
                Directory.CreateDirectory(appDataPath);
                var filePath = Path.Combine(appDataPath, StatsFilePath);

                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<EstimateStatsData>(json) ?? new EstimateStatsData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading estimate stats: {ex.Message}");
            }
            return new EstimateStatsData();
        }

        private void SaveStats()
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "McStudDesktop"
                );
                Directory.CreateDirectory(appDataPath);
                var filePath = Path.Combine(appDataPath, StatsFilePath);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_data, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving estimate stats: {ex.Message}");
            }
        }

        public void ClearAllStats()
        {
            lock (_lock)
            {
                _data = new EstimateStatsData();
                SaveStats();
            }
        }

        /// <summary>
        /// Get current Windows user ID
        /// </summary>
        private static string GetCurrentUserId()
        {
            try { return Environment.UserName; }
            catch { return "unknown"; }
        }

        /// <summary>
        /// Get all unique user IDs in the stats
        /// </summary>
        public List<string> GetAllUserIds()
        {
            return _data.Imports
                .Select(i => string.IsNullOrEmpty(i.UserId) ? "unknown" : i.UserId)
                .Distinct()
                .OrderBy(u => u)
                .ToList();
        }

        /// <summary>
        /// Get stats filtered by user
        /// </summary>
        public EstimateStatsResult GetStatsByUser(StatsPeriod period, string userId)
        {
            var records = FilterByPeriod(
                _data.Imports.Where(i => (string.IsNullOrEmpty(i.UserId) ? "unknown" : i.UserId) == userId),
                period).ToList();

            var learnRecords = records.Where(r => r.Mode == EstimateImportMode.Learn).ToList();
            var buildRecords = records.Where(r => r.Mode == EstimateImportMode.Build).ToList();

            return new EstimateStatsResult
            {
                PeriodName = period.ToString(),
                LearnImports = learnRecords.Count,
                TotalPatternsLearned = learnRecords.Sum(r => r.PatternsLearned),
                TotalLineItemsProcessed = learnRecords.Sum(r => r.LineItemsProcessed),
                BuildImports = buildRecords.Count,
                TotalOperationsGenerated = buildRecords.Sum(r => r.OperationsGenerated),
                TotalSuggestionsApplied = buildRecords.Sum(r => r.SuggestionsApplied)
            };
        }

        /// <summary>
        /// Get recent imports filtered by user
        /// </summary>
        public List<EstimateImportRecord> GetRecentImportsByUser(string userId, int count = 10)
        {
            return _data.Imports
                .Where(i => (string.IsNullOrEmpty(i.UserId) ? "unknown" : i.UserId) == userId)
                .OrderByDescending(r => r.Timestamp)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get user ranking for estimate imports
        /// </summary>
        public List<EstimateUserStats> GetUserRanking(StatsPeriod period)
        {
            var records = FilterByPeriod(_data.Imports, period);

            return records
                .GroupBy(r => string.IsNullOrEmpty(r.UserId) ? "unknown" : r.UserId)
                .Select(g => new EstimateUserStats
                {
                    UserId = g.Key,
                    TotalImports = g.Count(),
                    LearnImports = g.Count(r => r.Mode == EstimateImportMode.Learn),
                    BuildImports = g.Count(r => r.Mode == EstimateImportMode.Build),
                    TotalPatternsLearned = g.Where(r => r.Mode == EstimateImportMode.Learn).Sum(r => r.PatternsLearned),
                    TotalOperationsGenerated = g.Where(r => r.Mode == EstimateImportMode.Build).Sum(r => r.OperationsGenerated)
                })
                .OrderByDescending(u => u.TotalImports)
                .ToList();
        }
    }

    #region Enums

    public enum EstimateImportMode
    {
        Learn,  // Training the AI with examples
        Build   // Generating operations from estimate
    }

    #endregion

    #region Data Models

    public class EstimateStatsData
    {
        public List<EstimateImportRecord> Imports { get; set; } = new List<EstimateImportRecord>();
    }

    public class EstimateImportRecord
    {
        public DateTime Timestamp { get; set; }
        public EstimateImportMode Mode { get; set; }
        public string UserId { get; set; } = "";  // Windows username
        public string VehicleInfo { get; set; } = "";

        // For Learn mode
        public int PatternsLearned { get; set; }
        public int LineItemsProcessed { get; set; }

        // For Build mode
        public int OperationsGenerated { get; set; }
        public int SuggestionsApplied { get; set; }
    }

    public class EstimateStatsResult
    {
        public string PeriodName { get; set; } = "";

        // Learn mode totals
        public int LearnImports { get; set; }
        public int TotalPatternsLearned { get; set; }
        public int TotalLineItemsProcessed { get; set; }

        // Build mode totals
        public int BuildImports { get; set; }
        public int TotalOperationsGenerated { get; set; }
        public int TotalSuggestionsApplied { get; set; }

        // Computed
        public int TotalImports => LearnImports + BuildImports;
    }

    /// <summary>
    /// Per-user estimate statistics for leaderboard
    /// </summary>
    public class EstimateUserStats
    {
        public string UserId { get; set; } = "";
        public int TotalImports { get; set; }
        public int LearnImports { get; set; }
        public int BuildImports { get; set; }
        public int TotalPatternsLearned { get; set; }
        public int TotalOperationsGenerated { get; set; }
    }

    #endregion
}
