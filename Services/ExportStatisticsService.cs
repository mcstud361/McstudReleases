#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Tracks import AND export statistics over time
    /// - Import = data read from clipboard (what came INTO McStud)
    /// - Export = data sent to CCC/Mitchell (what went OUT of McStud)
    /// This allows verification that imports match exports for accuracy tracking
    /// </summary>
    public class ExportStatisticsService
    {
        private const string StatsFilePath = "export_stats.json";
        private StatsData _data;
        private readonly object _lock = new object();

        public ExportStatisticsService()
        {
            _data = LoadStats();
        }

        /// <summary>
        /// Record an IMPORT (clipboard read from Excel)
        /// </summary>
        public void RecordImport(List<ParsedOperation> operations)
        {
            lock (_lock)
            {
                var record = new TransactionRecord
                {
                    Timestamp = DateTime.Now,
                    Type = TransactionType.Import,
                    UserId = GetCurrentUserId(),
                    Source = "Excel",
                    OperationCount = operations.Count,
                    TotalPrice = ParseDecimalSum(operations.Select(o => o.Price)),
                    TotalLabor = ParseDecimalSum(operations.Select(o => o.Labor)),
                    TotalPaint = ParseDecimalSum(operations.Select(o => o.Paint)),
                    Operations = operations.Select(o => new OperationRecord
                    {
                        Type = o.Operation,
                        Description = o.Description,
                        Price = ParseDecimal(o.Price),
                        Labor = ParseDecimal(o.Labor),
                        Paint = ParseDecimal(o.Paint)
                    }).ToList()
                };

                _data.Transactions.Add(record);
                SaveStats();
            }
        }

        /// <summary>
        /// Record an EXPORT (sent to CCC or Mitchell)
        /// </summary>
        public void RecordExport(List<ParsedOperation> operations, string target)
        {
            lock (_lock)
            {
                var record = new TransactionRecord
                {
                    Timestamp = DateTime.Now,
                    Type = TransactionType.Export,
                    UserId = GetCurrentUserId(),
                    Target = target,
                    OperationCount = operations.Count,
                    TotalPrice = ParseDecimalSum(operations.Select(o => o.Price)),
                    TotalLabor = ParseDecimalSum(operations.Select(o => o.Labor)),
                    TotalPaint = ParseDecimalSum(operations.Select(o => o.Paint)),
                    Operations = operations.Select(o => new OperationRecord
                    {
                        Type = o.Operation,
                        Description = o.Description,
                        Price = ParseDecimal(o.Price),
                        Labor = ParseDecimal(o.Labor),
                        Paint = ParseDecimal(o.Paint)
                    }).ToList()
                };

                _data.Transactions.Add(record);
                SaveStats();
            }
        }

        /// <summary>
        /// Record a LEARN event (estimate trained/learned from Import tab).
        /// Tracks a fingerprint so we know how many times the same estimate was learned.
        /// Returns the occurrence number (1 = first time, 2+ = repeat).
        /// </summary>
        public int RecordLearn(int partsLearned, int manualOpsLearned, decimal estimateValue, string? fingerprint = null)
        {
            lock (_lock)
            {
                var source = string.IsNullOrEmpty(fingerprint) ? "Import" : $"Import|{fingerprint}";
                int occurrence = 1;

                // Count how many times this exact estimate was already learned
                if (!string.IsNullOrEmpty(fingerprint))
                {
                    occurrence = _data.Transactions.Count(t => t.Type == TransactionType.Learn && t.Source == source) + 1;
                }

                var record = new TransactionRecord
                {
                    Timestamp = DateTime.Now,
                    Type = TransactionType.Learn,
                    UserId = GetCurrentUserId(),
                    Source = source,
                    OperationCount = partsLearned + manualOpsLearned,
                    TotalPrice = estimateValue,
                    TotalLabor = 0,
                    TotalPaint = 0
                };

                _data.Transactions.Add(record);
                SaveStats();
                return occurrence;
            }
        }

        /// <summary>
        /// Get IMPORT statistics for a period
        /// </summary>
        public PeriodStats GetImportStats(StatsPeriod period)
        {
            var records = FilterByPeriod(_data.Transactions.Where(t => t.Type == TransactionType.Import), period);
            return BuildPeriodStats(records, period.ToString(), "Imported");
        }

        /// <summary>
        /// Get EXPORT statistics for a period
        /// </summary>
        public PeriodStats GetExportStats(StatsPeriod period)
        {
            var records = FilterByPeriod(_data.Transactions.Where(t => t.Type == TransactionType.Export), period);
            return BuildPeriodStats(records, period.ToString(), "Exported");
        }

        /// <summary>
        /// Get COMBINED statistics (imports + exports) for comparison
        /// </summary>
        public CombinedStats GetCombinedStats(StatsPeriod period, string? targetFilter = null)
        {
            var imports = FilterByPeriod(_data.Transactions.Where(t => t.Type == TransactionType.Import), period).ToList();
            var exports = FilterByPeriod(_data.Transactions.Where(t => t.Type == TransactionType.Export), period);

            // Apply target filter if specified (not "All")
            if (!string.IsNullOrEmpty(targetFilter) && targetFilter != "All")
            {
                exports = exports.Where(e => e.Target == targetFilter);
            }

            var exportsList = exports.ToList();
            var learns = FilterByPeriod(_data.Transactions.Where(t => t.Type == TransactionType.Learn), period).ToList();

            return new CombinedStats
            {
                PeriodName = period.ToString(),
                TargetFilter = targetFilter ?? "All",

                // Import totals
                ImportCount = imports.Count,
                ImportOperations = imports.Sum(r => r.OperationCount),
                ImportPrice = imports.Sum(r => r.TotalPrice),
                ImportLabor = imports.Sum(r => r.TotalLabor),
                ImportPaint = imports.Sum(r => r.TotalPaint),

                // Export totals (filtered by target if specified)
                ExportCount = exportsList.Count,
                ExportOperations = exportsList.Sum(r => r.OperationCount),
                ExportPrice = exportsList.Sum(r => r.TotalPrice),
                ExportLabor = exportsList.Sum(r => r.TotalLabor),
                ExportPaint = exportsList.Sum(r => r.TotalPaint),

                // Learn totals
                LearnCount = learns.Count,
                LearnOperations = learns.Sum(r => r.OperationCount),

                // Target breakdown (always show all targets)
                CCCDesktopOps = FilterByPeriod(_data.Transactions.Where(t => t.Type == TransactionType.Export && t.Target == "CCC Desktop"), period).Sum(r => r.OperationCount),
                CCCWebOps = FilterByPeriod(_data.Transactions.Where(t => t.Type == TransactionType.Export && t.Target == "CCC Web"), period).Sum(r => r.OperationCount),
                MitchellOps = FilterByPeriod(_data.Transactions.Where(t => t.Type == TransactionType.Export && t.Target == "Mitchell"), period).Sum(r => r.OperationCount)
            };
        }

        /// <summary>
        /// Get today's combined stats (shortcut)
        /// </summary>
        public CombinedStats GetTodayStats() => GetCombinedStats(StatsPeriod.Today);

        /// <summary>
        /// Get this week's combined stats
        /// </summary>
        public CombinedStats GetWeekStats() => GetCombinedStats(StatsPeriod.ThisWeek);

        /// <summary>
        /// Get this month's combined stats
        /// </summary>
        public CombinedStats GetMonthStats() => GetCombinedStats(StatsPeriod.ThisMonth);

        /// <summary>
        /// Get this year's combined stats
        /// </summary>
        public CombinedStats GetYearStats() => GetCombinedStats(StatsPeriod.ThisYear);

        /// <summary>
        /// Get all-time combined stats
        /// </summary>
        public CombinedStats GetAllTimeStats() => GetCombinedStats(StatsPeriod.AllTime);

        /// <summary>
        /// Get recent transactions (both imports and exports)
        /// </summary>
        public List<TransactionRecord> GetRecentTransactions(int count = 20)
        {
            return _data.Transactions
                .OrderByDescending(r => r.Timestamp)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get daily breakdown for the current month
        /// </summary>
        public List<DailyStats> GetDailyBreakdown()
        {
            var today = DateTime.Today;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            var transactions = _data.Transactions.Where(t => t.Timestamp.Date >= startOfMonth);

            return transactions
                .GroupBy(t => t.Timestamp.Date)
                .Select(g => new DailyStats
                {
                    Date = g.Key,
                    ImportCount = g.Count(t => t.Type == TransactionType.Import),
                    ExportCount = g.Count(t => t.Type == TransactionType.Export),
                    ImportOperations = g.Where(t => t.Type == TransactionType.Import).Sum(t => t.OperationCount),
                    ExportOperations = g.Where(t => t.Type == TransactionType.Export).Sum(t => t.OperationCount),
                    ImportPrice = g.Where(t => t.Type == TransactionType.Import).Sum(t => t.TotalPrice),
                    ExportPrice = g.Where(t => t.Type == TransactionType.Export).Sum(t => t.TotalPrice),
                    ImportLabor = g.Where(t => t.Type == TransactionType.Import).Sum(t => t.TotalLabor),
                    ExportLabor = g.Where(t => t.Type == TransactionType.Export).Sum(t => t.TotalLabor),
                    ImportPaint = g.Where(t => t.Type == TransactionType.Import).Sum(t => t.TotalPaint),
                    ExportPaint = g.Where(t => t.Type == TransactionType.Export).Sum(t => t.TotalPaint),
                    LearnCount = g.Count(t => t.Type == TransactionType.Learn),
                    LearnOperations = g.Where(t => t.Type == TransactionType.Learn).Sum(t => t.OperationCount)
                })
                .OrderByDescending(d => d.Date)
                .ToList();
        }

        private IEnumerable<TransactionRecord> FilterByPeriod(IEnumerable<TransactionRecord> records, StatsPeriod period)
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

        private PeriodStats BuildPeriodStats(IEnumerable<TransactionRecord> records, string periodName, string typeLabel)
        {
            var list = records.ToList();
            return new PeriodStats
            {
                PeriodName = periodName,
                TypeLabel = typeLabel,
                TransactionCount = list.Count,
                OperationCount = list.Sum(r => r.OperationCount),
                TotalPrice = list.Sum(r => r.TotalPrice),
                TotalLabor = list.Sum(r =>
                    r.Operations.Count > 0
                        ? r.Operations.Sum(o => Math.Min(o.Labor, 50m))
                        : Math.Min(r.TotalLabor, r.OperationCount * 50m)),
                TotalPaint = list.Sum(r => r.TotalPaint),
                CCCCount = list.Count(r => r.Target == "CCC"),
                MitchellCount = list.Count(r => r.Target == "Mitchell")
            };
        }

        private decimal ParseDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Replace("$", "").Replace(",", "").Trim();
            return decimal.TryParse(value, out var result) ? result : 0;
        }

        private decimal ParseDecimalSum(IEnumerable<string> values)
        {
            return values.Sum(v => ParseDecimal(v));
        }

        /// <summary>
        /// Get current Windows user ID
        /// </summary>
        public static string GetCurrentUserId()
        {
            try { return Environment.UserName; }
            catch { return "unknown"; }
        }

        /// <summary>
        /// Get all unique user IDs in the stats
        /// </summary>
        public List<string> GetAllUserIds()
        {
            return _data.Transactions
                .Select(t => string.IsNullOrEmpty(t.UserId) ? "unknown" : t.UserId)
                .Distinct()
                .OrderBy(u => u)
                .ToList();
        }

        /// <summary>
        /// Get combined stats filtered by user
        /// </summary>
        public CombinedStats GetCombinedStatsByUser(StatsPeriod period, string userId, string? targetFilter = null)
        {
            var userTransactions = _data.Transactions.Where(t =>
                (string.IsNullOrEmpty(t.UserId) ? "unknown" : t.UserId) == userId);

            return GetCombinedStatsFromTransactions(userTransactions, period, targetFilter);
        }

        /// <summary>
        /// Get daily breakdown filtered by user
        /// </summary>
        public List<DailyStats> GetDailyBreakdownByUser(string userId)
        {
            var today = DateTime.Today;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            var transactions = _data.Transactions
                .Where(t => t.Timestamp.Date >= startOfMonth)
                .Where(t => (string.IsNullOrEmpty(t.UserId) ? "unknown" : t.UserId) == userId);

            return transactions
                .GroupBy(t => t.Timestamp.Date)
                .Select(g => new DailyStats
                {
                    Date = g.Key,
                    ImportCount = g.Count(t => t.Type == TransactionType.Import),
                    ExportCount = g.Count(t => t.Type == TransactionType.Export),
                    ImportOperations = g.Where(t => t.Type == TransactionType.Import).Sum(t => t.OperationCount),
                    ExportOperations = g.Where(t => t.Type == TransactionType.Export).Sum(t => t.OperationCount),
                    ImportPrice = g.Where(t => t.Type == TransactionType.Import).Sum(t => t.TotalPrice),
                    ExportPrice = g.Where(t => t.Type == TransactionType.Export).Sum(t => t.TotalPrice),
                    ImportLabor = g.Where(t => t.Type == TransactionType.Import).Sum(t => t.TotalLabor),
                    ExportLabor = g.Where(t => t.Type == TransactionType.Export).Sum(t => t.TotalLabor),
                    ImportPaint = g.Where(t => t.Type == TransactionType.Import).Sum(t => t.TotalPaint),
                    ExportPaint = g.Where(t => t.Type == TransactionType.Export).Sum(t => t.TotalPaint),
                    LearnCount = g.Count(t => t.Type == TransactionType.Learn),
                    LearnOperations = g.Where(t => t.Type == TransactionType.Learn).Sum(t => t.OperationCount)
                })
                .OrderByDescending(d => d.Date)
                .ToList();
        }

        /// <summary>
        /// Get recent transactions filtered by user
        /// </summary>
        public List<TransactionRecord> GetRecentTransactionsByUser(string userId, int count = 20)
        {
            return _data.Transactions
                .Where(t => (string.IsNullOrEmpty(t.UserId) ? "unknown" : t.UserId) == userId)
                .OrderByDescending(r => r.Timestamp)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get most used operation types by user
        /// </summary>
        public List<OperationTypeStats> GetMostUsedOperationsByUser(string userId, StatsPeriod period, int topCount = 10)
        {
            var userTransactions = FilterByPeriod(
                _data.Transactions.Where(t => (string.IsNullOrEmpty(t.UserId) ? "unknown" : t.UserId) == userId),
                period);

            var allOperations = userTransactions
                .SelectMany(t => t.Operations)
                .GroupBy(o => o.Type.ToUpperInvariant())
                .Select(g => new OperationTypeStats
                {
                    OperationType = g.Key,
                    Count = g.Count(),
                    TotalPrice = g.Sum(o => o.Price),
                    TotalLabor = g.Sum(o => o.Labor),
                    TotalPaint = g.Sum(o => o.Paint)
                })
                .OrderByDescending(s => s.Count)
                .Take(topCount)
                .ToList();

            return allOperations;
        }

        /// <summary>
        /// Get user ranking (leaderboard)
        /// </summary>
        public List<UserStats> GetUserRanking(StatsPeriod period)
        {
            var transactions = FilterByPeriod(_data.Transactions, period);

            return transactions
                .GroupBy(t => string.IsNullOrEmpty(t.UserId) ? "unknown" : t.UserId)
                .Select(g => new UserStats
                {
                    UserId = g.Key,
                    TotalTransactions = g.Count(),
                    TotalOperations = g.Sum(t => t.OperationCount),
                    TotalPrice = g.Sum(t => t.TotalPrice),
                    TotalLabor = g.Sum(t => t.TotalLabor),
                    ImportCount = g.Count(t => t.Type == TransactionType.Import),
                    ExportCount = g.Count(t => t.Type == TransactionType.Export)
                })
                .OrderByDescending(u => u.TotalOperations)
                .ToList();
        }

        /// <summary>
        /// Get shop-wide aggregate stats across ALL users
        /// </summary>
        public ShopWideStats GetShopWideStats(StatsPeriod period)
        {
            var transactions = FilterByPeriod(_data.Transactions, period).ToList();
            var exports = transactions.Where(t => t.Type == TransactionType.Export).ToList();
            var users = transactions.Select(t => t.UserId).Distinct().Where(u => !string.IsNullOrEmpty(u)).ToList();

            var stats = new ShopWideStats
            {
                TotalUsers = users.Count,
                TotalExports = exports.Count,
                TotalOperations = exports.Sum(t => t.OperationCount),
                TotalValue = exports.Sum(t => t.TotalPrice),
                TotalLaborHours = exports.Sum(t =>
                    t.Operations.Count > 0
                        ? t.Operations.Sum(o => Math.Min(o.Labor, 50m))
                        : Math.Min(t.TotalLabor, t.OperationCount * 50m)),
                TotalRefinishHours = exports.Sum(t => t.TotalPaint)
            };

            // Calculate averages
            if (stats.TotalUsers > 0)
            {
                stats.AvgOperationsPerUser = stats.TotalOperations / (decimal)stats.TotalUsers;
                stats.AvgValuePerUser = stats.TotalValue / stats.TotalUsers;
                stats.AvgLaborPerUser = stats.TotalLaborHours / stats.TotalUsers;
            }

            if (stats.TotalExports > 0)
            {
                stats.AvgOperationsPerExport = stats.TotalOperations / (decimal)stats.TotalExports;
                stats.AvgValuePerExport = stats.TotalValue / stats.TotalExports;
            }

            if (stats.TotalOperations > 0)
            {
                stats.AvgValuePerOperation = stats.TotalValue / stats.TotalOperations;
            }

            return stats;
        }

        private CombinedStats GetCombinedStatsFromTransactions(IEnumerable<TransactionRecord> baseTransactions, StatsPeriod period, string? targetFilter = null)
        {
            var imports = FilterByPeriod(baseTransactions.Where(t => t.Type == TransactionType.Import), period).ToList();
            var exports = FilterByPeriod(baseTransactions.Where(t => t.Type == TransactionType.Export), period);

            if (!string.IsNullOrEmpty(targetFilter) && targetFilter != "All")
            {
                exports = exports.Where(e => e.Target == targetFilter);
            }

            var exportsList = exports.ToList();
            var learns = FilterByPeriod(baseTransactions.Where(t => t.Type == TransactionType.Learn), period).ToList();

            return new CombinedStats
            {
                PeriodName = period.ToString(),
                TargetFilter = targetFilter ?? "All",

                ImportCount = imports.Count,
                ImportOperations = imports.Sum(r => r.OperationCount),
                ImportPrice = imports.Sum(r => r.TotalPrice),
                ImportLabor = imports.Sum(r => r.TotalLabor),
                ImportPaint = imports.Sum(r => r.TotalPaint),

                ExportCount = exportsList.Count,
                ExportOperations = exportsList.Sum(r => r.OperationCount),
                ExportPrice = exportsList.Sum(r => r.TotalPrice),
                ExportLabor = exportsList.Sum(r => r.TotalLabor),
                ExportPaint = exportsList.Sum(r => r.TotalPaint),

                LearnCount = learns.Count,
                LearnOperations = learns.Sum(r => r.OperationCount),

                CCCDesktopOps = FilterByPeriod(baseTransactions.Where(t => t.Type == TransactionType.Export && t.Target == "CCC Desktop"), period).Sum(r => r.OperationCount),
                CCCWebOps = FilterByPeriod(baseTransactions.Where(t => t.Type == TransactionType.Export && t.Target == "CCC Web"), period).Sum(r => r.OperationCount),
                MitchellOps = FilterByPeriod(baseTransactions.Where(t => t.Type == TransactionType.Export && t.Target == "Mitchell"), period).Sum(r => r.OperationCount)
            };
        }

        private StatsData LoadStats()
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
                    return JsonSerializer.Deserialize<StatsData>(json) ?? new StatsData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading stats: {ex.Message}");
            }
            return new StatsData();
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
                System.Diagnostics.Debug.WriteLine($"Error saving stats: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all statistics (use with caution!)
        /// </summary>
        public void ClearAllStats()
        {
            lock (_lock)
            {
                _data = new StatsData();
                SaveStats();
            }
        }

        #region PDF Export Tracking

        /// <summary>
        /// Record a PDF export
        /// </summary>
        public void RecordPdfExport(string fileName, string exportType, int pageCount = 1, long fileSizeBytes = 0)
        {
            lock (_lock)
            {
                var record = new PdfExportRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    FileName = fileName,
                    ExportType = exportType,
                    PageCount = pageCount,
                    FileSizeBytes = fileSizeBytes
                };

                _data.PdfExports.Add(record);
                SaveStats();
            }
        }

        /// <summary>
        /// Get PDF export stats by user
        /// </summary>
        public PdfExportStats GetPdfStatsByUser(string userId, StatsPeriod period)
        {
            var records = FilterPdfByPeriod(
                _data.PdfExports.Where(p => (string.IsNullOrEmpty(p.UserId) ? "unknown" : p.UserId) == userId),
                period).ToList();

            return new PdfExportStats
            {
                TotalExports = records.Count,
                TotalPages = records.Sum(r => r.PageCount),
                TotalSizeBytes = records.Sum(r => r.FileSizeBytes),
                ByType = records.GroupBy(r => r.ExportType)
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        /// <summary>
        /// Get all PDF exports by user
        /// </summary>
        public List<PdfExportRecord> GetRecentPdfExportsByUser(string userId, int count = 20)
        {
            return _data.PdfExports
                .Where(p => (string.IsNullOrEmpty(p.UserId) ? "unknown" : p.UserId) == userId)
                .OrderByDescending(p => p.Timestamp)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get PDF export daily breakdown by user
        /// </summary>
        public List<DailyPdfStats> GetDailyPdfBreakdownByUser(string userId)
        {
            var today = DateTime.Today;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            return _data.PdfExports
                .Where(p => p.Timestamp.Date >= startOfMonth)
                .Where(p => (string.IsNullOrEmpty(p.UserId) ? "unknown" : p.UserId) == userId)
                .GroupBy(p => p.Timestamp.Date)
                .Select(g => new DailyPdfStats
                {
                    Date = g.Key,
                    ExportCount = g.Count(),
                    TotalPages = g.Sum(p => p.PageCount),
                    TotalSizeBytes = g.Sum(p => p.FileSizeBytes)
                })
                .OrderByDescending(d => d.Date)
                .ToList();
        }

        private IEnumerable<PdfExportRecord> FilterPdfByPeriod(IEnumerable<PdfExportRecord> records, StatsPeriod period)
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

        #endregion

        #region Session Tracking

        private string? _currentSessionId;

        /// <summary>
        /// Start a new work session
        /// </summary>
        public string StartSession()
        {
            lock (_lock)
            {
                // End any existing active session
                EndCurrentSession();

                var session = new SessionRecord
                {
                    UserId = GetCurrentUserId(),
                    StartTime = DateTime.Now
                };

                _data.Sessions.Add(session);
                _currentSessionId = session.SessionId;
                SaveStats();

                return session.SessionId;
            }
        }

        /// <summary>
        /// End the current session
        /// </summary>
        public void EndCurrentSession()
        {
            lock (_lock)
            {
                var activeSession = _data.Sessions.FirstOrDefault(s => s.IsActive && s.UserId == GetCurrentUserId());
                if (activeSession != null)
                {
                    activeSession.EndTime = DateTime.Now;
                    SaveStats();
                }
                _currentSessionId = null;
            }
        }

        /// <summary>
        /// Get or create an active session (auto-start if none exists)
        /// </summary>
        public SessionRecord GetOrCreateSession()
        {
            lock (_lock)
            {
                var userId = GetCurrentUserId();
                var activeSession = _data.Sessions.FirstOrDefault(s => s.IsActive && s.UserId == userId);

                // If session is older than 2 hours, consider it stale and start new one
                if (activeSession != null && activeSession.Duration.TotalHours > 2)
                {
                    activeSession.EndTime = DateTime.Now;
                    activeSession = null;
                }

                if (activeSession == null)
                {
                    activeSession = new SessionRecord
                    {
                        UserId = userId,
                        StartTime = DateTime.Now
                    };
                    _data.Sessions.Add(activeSession);
                    SaveStats();
                }

                return activeSession;
            }
        }

        /// <summary>
        /// Update current session with export activity
        /// </summary>
        public void RecordSessionActivity(int operationCount, decimal value)
        {
            lock (_lock)
            {
                var session = GetOrCreateSession();
                session.EstimatesCompleted++;
                session.OperationsExported += operationCount;
                session.TotalValue += value;
                SaveStats();
            }
        }

        /// <summary>
        /// Get session statistics for a user
        /// </summary>
        public List<SessionRecord> GetSessionsByUser(string userId, StatsPeriod period)
        {
            var sessions = _data.Sessions.Where(s => s.UserId == userId);
            return FilterSessionsByPeriod(sessions, period).OrderByDescending(s => s.StartTime).ToList();
        }

        private IEnumerable<SessionRecord> FilterSessionsByPeriod(IEnumerable<SessionRecord> sessions, StatsPeriod period)
        {
            var today = DateTime.Today;
            return period switch
            {
                StatsPeriod.Today => sessions.Where(s => s.StartTime.Date == today),
                StatsPeriod.ThisWeek => sessions.Where(s => s.StartTime.Date >= today.AddDays(-(int)today.DayOfWeek)),
                StatsPeriod.ThisMonth => sessions.Where(s => s.StartTime.Date >= new DateTime(today.Year, today.Month, 1)),
                StatsPeriod.ThisYear => sessions.Where(s => s.StartTime.Date >= new DateTime(today.Year, 1, 1)),
                StatsPeriod.AllTime => sessions,
                _ => sessions
            };
        }

        #endregion

        #region Enhanced Stats

        /// <summary>
        /// Get enhanced statistics with trends and averages
        /// </summary>
        public EnhancedStats GetEnhancedStats(string userId, StatsPeriod period)
        {
            var transactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                period).ToList();

            var sessions = FilterSessionsByPeriod(
                _data.Sessions.Where(s => s.UserId == userId),
                period).ToList();

            // Calculate streaks
            var (currentStreak, longestStreak) = CalculateStreaks(userId);

            // Calculate trends (compare to previous period)
            var (estimatesTrend, operationsTrend, valueTrend) = CalculateTrends(userId, period);

            var totalEstimates = transactions.Count;
            var totalOperations = transactions.Sum(t => t.OperationCount);
            var totalValue = transactions.Sum(t => t.TotalPrice);
            // Cap per-operation labor at 50h to fix historical data where dollar amounts
            // were incorrectly parsed as labor hours (e.g. $250 → 250 hours)
            var totalLabor = transactions.Sum(t =>
                t.Operations.Count > 0
                    ? t.Operations.Sum(o => Math.Min(o.Labor, 50m))
                    : Math.Min(t.TotalLabor, t.OperationCount * 50m));

            var totalTime = TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks));

            return new EnhancedStats
            {
                TotalEstimates = totalEstimates,
                TotalOperations = totalOperations,
                TotalValue = totalValue,
                TotalLaborHours = totalLabor,

                AvgOperationsPerEstimate = totalEstimates > 0 ? (double)totalOperations / totalEstimates : 0,
                AvgValuePerEstimate = totalEstimates > 0 ? totalValue / totalEstimates : 0,
                AvgValuePerOperation = totalOperations > 0 ? totalValue / totalOperations : 0,

                TotalSessions = sessions.Count,
                TotalTimeSpent = totalTime,
                AvgEstimatesPerSession = sessions.Count > 0 ? (double)totalEstimates / sessions.Count : 0,
                AvgSessionDuration = sessions.Count > 0 ? TimeSpan.FromTicks(totalTime.Ticks / sessions.Count) : TimeSpan.Zero,

                CurrentStreak = currentStreak,
                LongestStreak = longestStreak,

                EstimatesTrend = estimatesTrend,
                OperationsTrend = operationsTrend,
                ValueTrend = valueTrend
            };
        }

        private (int currentStreak, int longestStreak) CalculateStreaks(string userId)
        {
            var dates = _data.Transactions
                .Where(t => t.UserId == userId && t.Type == TransactionType.Export)
                .Select(t => t.Timestamp.Date)
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();

            if (dates.Count == 0) return (0, 0);

            int currentStreak = 0;
            int longestStreak = 0;
            int tempStreak = 1;

            // Calculate current streak (from today backwards)
            var today = DateTime.Today;
            if (dates.Contains(today) || dates.Contains(today.AddDays(-1)))
            {
                var startDate = dates.Contains(today) ? today : today.AddDays(-1);
                currentStreak = 1;
                for (int i = 1; i < dates.Count; i++)
                {
                    if (dates[i] == startDate.AddDays(-i))
                        currentStreak++;
                    else
                        break;
                }
            }

            // Calculate longest streak
            for (int i = 1; i < dates.Count; i++)
            {
                if ((dates[i - 1] - dates[i]).Days == 1)
                    tempStreak++;
                else
                {
                    longestStreak = Math.Max(longestStreak, tempStreak);
                    tempStreak = 1;
                }
            }
            longestStreak = Math.Max(longestStreak, tempStreak);

            return (currentStreak, longestStreak);
        }

        private (double estimatesTrend, double operationsTrend, double valueTrend) CalculateTrends(string userId, StatsPeriod period)
        {
            var currentPeriodData = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                period).ToList();

            var previousPeriodData = GetPreviousPeriodData(userId, period).ToList();

            if (previousPeriodData.Count == 0) return (0, 0, 0);

            var currentEstimates = currentPeriodData.Count;
            var previousEstimates = previousPeriodData.Count;
            var estimatesTrend = previousEstimates > 0 ? ((double)currentEstimates - previousEstimates) / previousEstimates * 100 : 0;

            var currentOps = currentPeriodData.Sum(t => t.OperationCount);
            var previousOps = previousPeriodData.Sum(t => t.OperationCount);
            var opsTrend = previousOps > 0 ? ((double)currentOps - previousOps) / previousOps * 100 : 0;

            var currentValue = currentPeriodData.Sum(t => t.TotalPrice);
            var previousValue = previousPeriodData.Sum(t => t.TotalPrice);
            var valueTrend = previousValue > 0 ? (double)((currentValue - previousValue) / previousValue) * 100 : 0;

            return (estimatesTrend, opsTrend, valueTrend);
        }

        private IEnumerable<TransactionRecord> GetPreviousPeriodData(string userId, StatsPeriod period)
        {
            var today = DateTime.Today;
            var exports = _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export);

            return period switch
            {
                StatsPeriod.Today => exports.Where(t => t.Timestamp.Date == today.AddDays(-1)),
                StatsPeriod.ThisWeek => exports.Where(t =>
                    t.Timestamp.Date >= today.AddDays(-(int)today.DayOfWeek - 7) &&
                    t.Timestamp.Date < today.AddDays(-(int)today.DayOfWeek)),
                StatsPeriod.ThisMonth => exports.Where(t =>
                    t.Timestamp.Month == (today.Month == 1 ? 12 : today.Month - 1) &&
                    t.Timestamp.Year == (today.Month == 1 ? today.Year - 1 : today.Year)),
                _ => Enumerable.Empty<TransactionRecord>()
            };
        }

        /// <summary>
        /// Get part type breakdown (which parts are most common)
        /// </summary>
        public List<PartTypeStats> GetPartTypeBreakdown(string userId, StatsPeriod period)
        {
            var transactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                period).ToList();

            var allOperations = transactions.SelectMany(t => t.Operations).ToList();
            var totalCount = allOperations.Count;

            if (totalCount == 0) return new List<PartTypeStats>();

            // Group by normalized part type
            var grouped = allOperations
                .GroupBy(o => NormalizePartType(o.Description))
                .Select(g => new PartTypeStats
                {
                    PartType = g.Key,
                    Count = g.Count(),
                    TotalLabor = g.Sum(o => o.Labor),
                    TotalValue = g.Sum(o => o.Price),
                    Percentage = (double)g.Count() / totalCount * 100
                })
                .OrderByDescending(p => p.Count)
                .Take(10)
                .ToList();

            return grouped;
        }

        private string NormalizePartType(string description)
        {
            var desc = description.ToLowerInvariant();

            // Common part types
            if (desc.Contains("bumper")) return "Bumper";
            if (desc.Contains("fender")) return "Fender";
            if (desc.Contains("quarter")) return "Quarter Panel";
            if (desc.Contains("door")) return "Door";
            if (desc.Contains("hood")) return "Hood";
            if (desc.Contains("decklid") || desc.Contains("trunk")) return "Decklid/Trunk";
            if (desc.Contains("rocker")) return "Rocker";
            if (desc.Contains("headl")) return "Headlamp";
            if (desc.Contains("taill")) return "Taillamp";
            if (desc.Contains("mirror")) return "Mirror";
            if (desc.Contains("grille") || desc.Contains("grill")) return "Grille";
            if (desc.Contains("pillar")) return "Pillar";
            if (desc.Contains("rail")) return "Rail";
            if (desc.Contains("wheel")) return "Wheel";
            if (desc.Contains("glass") || desc.Contains("windshield")) return "Glass";
            if (desc.Contains("molding") || desc.Contains("moulding")) return "Molding";
            if (desc.Contains("cover")) return "Cover/Panel";

            return "Other";
        }

        /// <summary>
        /// Get operation type breakdown (Replace, Repair, R&I)
        /// </summary>
        public List<OperationTypeStats> GetOperationTypeBreakdown(string userId, StatsPeriod period)
        {
            var transactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                period).ToList();

            var allOperations = transactions.SelectMany(t => t.Operations).ToList();

            if (allOperations.Count == 0) return new List<OperationTypeStats>();

            return allOperations
                .GroupBy(o => NormalizeOperationType(o.Type))
                .Select(g => new OperationTypeStats
                {
                    OperationType = g.Key,
                    Count = g.Count(),
                    TotalPrice = g.Sum(o => o.Price),
                    TotalLabor = g.Sum(o => o.Labor),
                    TotalPaint = g.Sum(o => o.Paint)
                })
                .OrderByDescending(o => o.Count)
                .ToList();
        }

        private string NormalizeOperationType(string opType)
        {
            var op = opType.ToUpperInvariant();
            if (op.Contains("REPLACE") || op.Contains("REPL")) return "Replace";
            if (op.Contains("REPAIR") || op.Contains("RPR")) return "Repair";
            if (op.Contains("R&I") || op.Contains("R/I") || op.Contains("REMOVE")) return "R&I";
            if (op.Contains("BLEND")) return "Blend";
            if (op.Contains("REFINISH") || op.Contains("PAINT")) return "Refinish";
            if (op.Contains("OVERHAUL") || op.Contains("O/H")) return "Overhaul";
            return opType.Length > 0 ? opType : "Other";
        }

        /// <summary>
        /// Get hourly activity breakdown
        /// </summary>
        public List<HourlyActivity> GetHourlyActivity(string userId, StatsPeriod period)
        {
            var exportTransactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                period).ToList();
            var learnTransactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Learn),
                period).ToList();

            var hourlyGroups = Enumerable.Range(0, 24)
                .Select(hour =>
                {
                    var hourExports = exportTransactions.Where(t => t.Timestamp.Hour == hour).ToList();
                    var hourLearns = learnTransactions.Where(t => t.Timestamp.Hour == hour).ToList();
                    return new HourlyActivity
                    {
                        Hour = hour,
                        ExportCount = hourExports.Count,
                        OperationCount = hourExports.Sum(t => t.OperationCount),
                        LearnCount = hourLearns.Sum(t => t.OperationCount)
                    };
                })
                .ToList();

            return hourlyGroups;
        }

        /// <summary>
        /// Get performance ranking (percentile among all users)
        /// </summary>
        public PerformanceRanking GetPerformanceRanking(string userId, StatsPeriod period)
        {
            var allUsers = GetUserRanking(period);
            var totalUsers = allUsers.Count;

            if (totalUsers == 0)
            {
                return new PerformanceRanking
                {
                    UserId = userId,
                    Rank = 1,
                    TotalUsers = 1,
                    Percentile = 50
                };
            }

            // Find user's rank (1-based)
            var userRank = allUsers.FindIndex(u => u.UserId == userId) + 1;
            if (userRank == 0) userRank = totalUsers; // Not found, put at bottom

            // Calculate percentile (higher rank = higher percentile)
            var percentile = totalUsers > 1
                ? (double)(totalUsers - userRank) / (totalUsers - 1) * 100
                : 50;

            var userStats = allUsers.FirstOrDefault(u => u.UserId == userId);
            var topUser = allUsers.FirstOrDefault();

            // Calculate avg ops per estimate
            var userTransactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                period).ToList();
            var avgOps = userTransactions.Count > 0
                ? (double)userTransactions.Sum(t => t.OperationCount) / userTransactions.Count
                : 0;

            // Top user avg ops
            var topUserTransactions = topUser != null
                ? FilterByPeriod(
                    _data.Transactions.Where(t => t.UserId == topUser.UserId && t.Type == TransactionType.Export),
                    period).ToList()
                : new List<TransactionRecord>();
            var topUserAvgOps = topUserTransactions.Count > 0
                ? (double)topUserTransactions.Sum(t => t.OperationCount) / topUserTransactions.Count
                : 0;

            return new PerformanceRanking
            {
                UserId = userId,
                Rank = userRank,
                TotalUsers = totalUsers,
                Percentile = percentile,
                UserOperations = userStats?.TotalOperations ?? 0,
                UserValue = userStats?.TotalPrice ?? 0,
                AvgOpsPerEstimate = avgOps,
                TopUserAvgOps = topUserAvgOps
            };
        }

        /// <summary>
        /// Get category breakdown (Labor vs Refinish vs Materials)
        /// </summary>
        public CategoryBreakdown GetCategoryBreakdown(string userId, StatsPeriod period)
        {
            var transactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                period).ToList();

            var allOperations = transactions.SelectMany(t => t.Operations).ToList();

            decimal laborDollars = 0;
            decimal refinishDollars = 0;
            decimal materialsDollars = 0;
            decimal otherDollars = 0;

            foreach (var op in allOperations)
            {
                var opType = op.Type.ToUpperInvariant();
                var desc = op.Description.ToUpperInvariant();

                // Categorize based on operation type and description
                if (opType.Contains("REFIN") || opType.Contains("PAINT") || opType.Contains("BLEND") ||
                    desc.Contains("REFINISH") || desc.Contains("PAINT") || desc.Contains("BLEND"))
                {
                    refinishDollars += op.Price;
                }
                else if (opType.Contains("MATERIAL") || opType.Contains("MAT") ||
                         desc.Contains("MATERIAL") || desc.Contains("ADHESIVE") ||
                         desc.Contains("SEALER") || desc.Contains("PRIMER"))
                {
                    materialsDollars += op.Price;
                }
                else if (opType.Contains("LABOR") || opType.Contains("BODY") ||
                         opType.Contains("REPLACE") || opType.Contains("REPAIR") ||
                         opType.Contains("R&I") || opType.Contains("R/I"))
                {
                    laborDollars += op.Price;
                }
                else
                {
                    otherDollars += op.Price;
                }
            }

            return new CategoryBreakdown
            {
                LaborDollars = laborDollars,
                RefinishDollars = refinishDollars,
                MaterialsDollars = materialsDollars,
                OtherDollars = otherDollars,
                TotalDollars = laborDollars + refinishDollars + materialsDollars + otherDollars
            };
        }

        /// <summary>
        /// Get ROI statistics
        /// </summary>
        public ROIStats GetROIStats(string userId, StatsPeriod period)
        {
            var transactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                period).ToList();

            var totalValue = transactions.Sum(t => t.TotalPrice);
            var totalOps = transactions.Sum(t => t.OperationCount);
            var estimateCount = transactions.Count;

            // Estimate what would have been missed without the tool
            // Industry average is ~30% of operations are commonly missed
            var estimatedMissed = totalValue * 0.30m;

            // Estimate time saved (assume 2 minutes per operation lookup)
            var timeSavedMinutes = totalOps * 2;

            return new ROIStats
            {
                TotalValueCaptured = totalValue,
                TotalOperationsAdded = totalOps,
                EstimatesProcessed = estimateCount,
                AvgValuePerEstimate = estimateCount > 0 ? totalValue / estimateCount : 0,
                EstimatedMissedWithoutTool = estimatedMissed,
                TimeSaved = TimeSpan.FromMinutes(timeSavedMinutes)
            };
        }

        /// <summary>
        /// Get user goals with current progress
        /// </summary>
        public UserGoals GetUserGoals(string userId)
        {
            // Load saved goals or use defaults
            var goals = _data.UserGoals.FirstOrDefault(g => g.UserId == userId) ?? new UserGoals { UserId = userId };

            // Calculate today's progress
            var todayTransactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                StatsPeriod.Today).ToList();

            goals.CurrentDayOperations = todayTransactions.Sum(t => t.OperationCount);
            goals.CurrentDayEstimates = todayTransactions.Count;
            goals.CurrentDayValue = todayTransactions.Sum(t => t.TotalPrice);

            return goals;
        }

        /// <summary>
        /// Save user goals
        /// </summary>
        public void SaveUserGoals(UserGoals goals)
        {
            lock (_lock)
            {
                var existing = _data.UserGoals.FirstOrDefault(g => g.UserId == goals.UserId);
                if (existing != null)
                {
                    existing.DailyOperationsTarget = goals.DailyOperationsTarget;
                    existing.DailyEstimatesTarget = goals.DailyEstimatesTarget;
                    existing.DailyValueTarget = goals.DailyValueTarget;
                    existing.TargetOpsPerEstimate = goals.TargetOpsPerEstimate;
                }
                else
                {
                    _data.UserGoals.Add(goals);
                }
                SaveStats();
            }
        }

        /// <summary>
        /// Get individual estimate details for drill-down
        /// </summary>
        public List<EstimateDetail> GetEstimateDetails(string userId, StatsPeriod period, int limit = 50)
        {
            var transactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                period)
                .OrderByDescending(t => t.Timestamp)
                .Take(limit)
                .ToList();

            return transactions.Select(t => new EstimateDetail
            {
                Timestamp = t.Timestamp,
                Target = t.Target,
                OperationCount = t.OperationCount,
                TotalValue = t.TotalPrice,
                LaborHours = t.TotalLabor,
                RefinishHours = t.TotalPaint,
                Operations = t.Operations
            }).ToList();
        }

        /// <summary>
        /// Get most commonly added operations (what users add most)
        /// </summary>
        public List<MissedOperationStats> GetTopAddedOperations(string userId, StatsPeriod period, int limit = 10)
        {
            var transactions = FilterByPeriod(
                _data.Transactions.Where(t => t.UserId == userId && t.Type == TransactionType.Export),
                period).ToList();

            var allOperations = transactions.SelectMany(t => t.Operations).ToList();

            return allOperations
                .GroupBy(o => o.Type.ToUpperInvariant())
                .Select(g => new MissedOperationStats
                {
                    OperationType = g.Key,
                    Description = g.First().Description,
                    TimesAdded = g.Count(),
                    TimesSuggested = g.Count(), // Assume all added were suggested
                    AvgValue = g.Average(o => o.Price)
                })
                .OrderByDescending(m => m.TimesAdded)
                .Take(limit)
                .ToList();
        }

        /// <summary>
        /// Get user achievements (gamification)
        /// </summary>
        public List<Achievement> GetAchievements(string userId)
        {
            var allTimeStats = GetEnhancedStats(userId, StatsPeriod.AllTime);
            var achievements = new List<Achievement>();

            // Pull data from other services
            var usageSummary = DocumentUsageTrackingService.Instance.GetUsageSummary(StatsPeriod.AllTime, userId);
            var tabVisits = DocumentUsageTrackingService.Instance.GetTabVisitStats();
            var presets = EstimatePresetService.Instance.GetAllPresets();
            var accuracy = EstimateAccuracyService.Instance.GetAccuracyAnalysis();
            var feedbackCount = EstimateAccuracyService.Instance.GetFeedbackCount();
            var learningStats = EstimateLearningService.Instance.GetStatistics();
            int healthScore = 0;
            try { healthScore = LearningHealthService.Instance.GetLearningHealthScore(); } catch { }

            int totalInvoices = usageSummary.TotalInvoices;
            int totalPdfs = usageSummary.TotalPdfExports;
            int distinctTabs = tabVisits.Count;
            int presetCount = presets.Count;
            int totalPresetUses = presets.Sum(p => p.TimesUsed);
            int accurateCount = accuracy.AccurateCount;
            int totalPatterns = learningStats.TotalPatterns;
            int estimatesImported = learningStats.EstimatesImported;
            int totalSessions = allTimeStats.TotalSessions;
            double totalHours = allTimeStats.TotalTimeSpent.TotalHours;

            // === Export Stats (7) ===
            achievements.Add(new Achievement
            {
                Id = "ops_100", Name = "Getting Started", Description = "Export 100 operations",
                Icon = "\uE8B8", Target = 100, Progress = Math.Min(100, allTimeStats.TotalOperations),
                IsUnlocked = allTimeStats.TotalOperations >= 100, Tier = "Bronze", Category = "Export Stats"
            });
            achievements.Add(new Achievement
            {
                Id = "ops_500", Name = "Operation Expert", Description = "Export 500 operations",
                Icon = "\uE8B8", Target = 500, Progress = Math.Min(500, allTimeStats.TotalOperations),
                IsUnlocked = allTimeStats.TotalOperations >= 500, Tier = "Silver", Category = "Export Stats"
            });
            achievements.Add(new Achievement
            {
                Id = "ops_1000", Name = "Operation Master", Description = "Export 1,000 operations",
                Icon = "\uE8B8", Target = 1000, Progress = Math.Min(1000, allTimeStats.TotalOperations),
                IsUnlocked = allTimeStats.TotalOperations >= 1000, Tier = "Gold", Category = "Export Stats"
            });
            achievements.Add(new Achievement
            {
                Id = "ops_5000", Name = "Operation Legend", Description = "Export 5,000 operations",
                Icon = "\uE734", Target = 5000, Progress = Math.Min(5000, allTimeStats.TotalOperations),
                IsUnlocked = allTimeStats.TotalOperations >= 5000, Tier = "Platinum", Category = "Export Stats"
            });
            achievements.Add(new Achievement
            {
                Id = "est_25", Name = "First Fleet", Description = "Export 25 estimates",
                Icon = "\uE8B8", Target = 25, Progress = Math.Min(25, allTimeStats.TotalEstimates),
                IsUnlocked = allTimeStats.TotalEstimates >= 25, Tier = "Bronze", Category = "Export Stats"
            });
            achievements.Add(new Achievement
            {
                Id = "est_100", Name = "Centurion", Description = "Export 100 estimates",
                Icon = "\uE8B8", Target = 100, Progress = Math.Min(100, allTimeStats.TotalEstimates),
                IsUnlocked = allTimeStats.TotalEstimates >= 100, Tier = "Silver", Category = "Export Stats"
            });
            achievements.Add(new Achievement
            {
                Id = "est_500", Name = "Estimate Titan", Description = "Export 500 estimates",
                Icon = "\uE8B8", Target = 500, Progress = Math.Min(500, allTimeStats.TotalEstimates),
                IsUnlocked = allTimeStats.TotalEstimates >= 500, Tier = "Gold", Category = "Export Stats"
            });

            // === Value (3) ===
            achievements.Add(new Achievement
            {
                Id = "value_10k", Name = "Value Hunter", Description = "Capture $10,000 in value",
                Icon = "\uE8C8", Target = 10000, Progress = Math.Min(10000, (int)allTimeStats.TotalValue),
                IsUnlocked = allTimeStats.TotalValue >= 10000, Tier = "Bronze", Category = "Value"
            });
            achievements.Add(new Achievement
            {
                Id = "value_50k", Name = "Value Champion", Description = "Capture $50,000 in value",
                Icon = "\uE8C8", Target = 50000, Progress = Math.Min(50000, (int)allTimeStats.TotalValue),
                IsUnlocked = allTimeStats.TotalValue >= 50000, Tier = "Silver", Category = "Value"
            });
            achievements.Add(new Achievement
            {
                Id = "value_100k", Name = "Six Figure Club", Description = "Capture $100,000 in value",
                Icon = "\uE8C8", Target = 100000, Progress = Math.Min(100000, (int)allTimeStats.TotalValue),
                IsUnlocked = allTimeStats.TotalValue >= 100000, Tier = "Gold", Category = "Value"
            });

            // === Streaks (6) ===
            achievements.Add(new Achievement
            {
                Id = "streak_3", Name = "Hat Trick", Description = "3-day streak",
                Icon = "\uE735", Target = 3, Progress = Math.Min(3, allTimeStats.LongestStreak),
                IsUnlocked = allTimeStats.LongestStreak >= 3, Tier = "Bronze", Category = "Streaks"
            });
            achievements.Add(new Achievement
            {
                Id = "streak_7", Name = "Week Warrior", Description = "7-day streak",
                Icon = "\uE735", Target = 7, Progress = Math.Min(7, allTimeStats.LongestStreak),
                IsUnlocked = allTimeStats.LongestStreak >= 7, Tier = "Bronze", Category = "Streaks"
            });
            achievements.Add(new Achievement
            {
                Id = "streak_14", Name = "Fortnight Force", Description = "14-day streak",
                Icon = "\uE735", Target = 14, Progress = Math.Min(14, allTimeStats.LongestStreak),
                IsUnlocked = allTimeStats.LongestStreak >= 14, Tier = "Silver", Category = "Streaks"
            });
            achievements.Add(new Achievement
            {
                Id = "streak_30", Name = "Monthly Machine", Description = "30-day streak",
                Icon = "\uE735", Target = 30, Progress = Math.Min(30, allTimeStats.LongestStreak),
                IsUnlocked = allTimeStats.LongestStreak >= 30, Tier = "Gold", Category = "Streaks"
            });
            achievements.Add(new Achievement
            {
                Id = "streak_60", Name = "Iron Will", Description = "60-day streak",
                Icon = "\uE735", Target = 60, Progress = Math.Min(60, allTimeStats.LongestStreak),
                IsUnlocked = allTimeStats.LongestStreak >= 60, Tier = "Gold", Category = "Streaks"
            });
            achievements.Add(new Achievement
            {
                Id = "streak_90", Name = "Unstoppable", Description = "90-day streak",
                Icon = "\uE735", Target = 90, Progress = Math.Min(90, allTimeStats.LongestStreak),
                IsUnlocked = allTimeStats.LongestStreak >= 90, Tier = "Platinum", Category = "Streaks"
            });

            // === Time Use (5) ===
            achievements.Add(new Achievement
            {
                Id = "sessions_10", Name = "Regular", Description = "Log 10 sessions",
                Icon = "\uE823", Target = 10, Progress = Math.Min(10, totalSessions),
                IsUnlocked = totalSessions >= 10, Tier = "Bronze", Category = "Time Use"
            });
            achievements.Add(new Achievement
            {
                Id = "sessions_50", Name = "Creature of Habit", Description = "Log 50 sessions",
                Icon = "\uE823", Target = 50, Progress = Math.Min(50, totalSessions),
                IsUnlocked = totalSessions >= 50, Tier = "Silver", Category = "Time Use"
            });
            achievements.Add(new Achievement
            {
                Id = "sessions_200", Name = "Power User", Description = "Log 200 sessions",
                Icon = "\uE823", Target = 200, Progress = Math.Min(200, totalSessions),
                IsUnlocked = totalSessions >= 200, Tier = "Gold", Category = "Time Use"
            });
            achievements.Add(new Achievement
            {
                Id = "hours_10", Name = "Putting in Time", Description = "Spend 10 hours in app",
                Icon = "\uE823", Target = 10, Progress = Math.Min(10, (int)totalHours),
                IsUnlocked = totalHours >= 10, Tier = "Bronze", Category = "Time Use"
            });
            achievements.Add(new Achievement
            {
                Id = "hours_50", Name = "Time Well Spent", Description = "Spend 50 hours in app",
                Icon = "\uE823", Target = 50, Progress = Math.Min(50, (int)totalHours),
                IsUnlocked = totalHours >= 50, Tier = "Silver", Category = "Time Use"
            });

            // === Usage (6) ===
            achievements.Add(new Achievement
            {
                Id = "invoices_10", Name = "Paper Pusher", Description = "Create 10 invoices",
                Icon = "\uE8A5", Target = 10, Progress = Math.Min(10, totalInvoices),
                IsUnlocked = totalInvoices >= 10, Tier = "Bronze", Category = "Usage"
            });
            achievements.Add(new Achievement
            {
                Id = "invoices_50", Name = "Invoice Machine", Description = "Create 50 invoices",
                Icon = "\uE8A5", Target = 50, Progress = Math.Min(50, totalInvoices),
                IsUnlocked = totalInvoices >= 50, Tier = "Silver", Category = "Usage"
            });
            achievements.Add(new Achievement
            {
                Id = "pdfs_25", Name = "PDF Pro", Description = "Export 25 PDFs",
                Icon = "\uE8A5", Target = 25, Progress = Math.Min(25, totalPdfs),
                IsUnlocked = totalPdfs >= 25, Tier = "Bronze", Category = "Usage"
            });
            achievements.Add(new Achievement
            {
                Id = "pdfs_100", Name = "Document Factory", Description = "Export 100 PDFs",
                Icon = "\uE8A5", Target = 100, Progress = Math.Min(100, totalPdfs),
                IsUnlocked = totalPdfs >= 100, Tier = "Silver", Category = "Usage"
            });
            achievements.Add(new Achievement
            {
                Id = "feedback_5", Name = "Helpful Critic", Description = "Submit 5 feedback entries",
                Icon = "\uE8A5", Target = 5, Progress = Math.Min(5, feedbackCount),
                IsUnlocked = feedbackCount >= 5, Tier = "Bronze", Category = "Usage"
            });
            achievements.Add(new Achievement
            {
                Id = "feedback_25", Name = "Quality Champion", Description = "Submit 25 feedback entries",
                Icon = "\uE8A5", Target = 25, Progress = Math.Min(25, feedbackCount),
                IsUnlocked = feedbackCount >= 25, Tier = "Silver", Category = "Usage"
            });

            // === Customization (4) ===
            achievements.Add(new Achievement
            {
                Id = "presets_3", Name = "Template Tinkerer", Description = "Create 3 presets",
                Icon = "\uE771", Target = 3, Progress = Math.Min(3, presetCount),
                IsUnlocked = presetCount >= 3, Tier = "Bronze", Category = "Customization"
            });
            achievements.Add(new Achievement
            {
                Id = "presets_10", Name = "Preset Architect", Description = "Create 10 presets",
                Icon = "\uE771", Target = 10, Progress = Math.Min(10, presetCount),
                IsUnlocked = presetCount >= 10, Tier = "Silver", Category = "Customization"
            });
            achievements.Add(new Achievement
            {
                Id = "preset_uses_20", Name = "Shortcut Master", Description = "Use presets 20 times",
                Icon = "\uE771", Target = 20, Progress = Math.Min(20, totalPresetUses),
                IsUnlocked = totalPresetUses >= 20, Tier = "Silver", Category = "Customization"
            });
            achievements.Add(new Achievement
            {
                Id = "tabs_8", Name = "Explorer", Description = "Visit 8 different tabs",
                Icon = "\uE771", Target = 8, Progress = Math.Min(8, distinctTabs),
                IsUnlocked = distinctTabs >= 8, Tier = "Bronze", Category = "Customization"
            });

            // === Learning (4) ===
            achievements.Add(new Achievement
            {
                Id = "patterns_50", Name = "Pattern Spotter", Description = "Learn 50 patterns",
                Icon = "\uE82D", Target = 50, Progress = Math.Min(50, totalPatterns),
                IsUnlocked = totalPatterns >= 50, Tier = "Bronze", Category = "Learning"
            });
            achievements.Add(new Achievement
            {
                Id = "patterns_200", Name = "Pattern Master", Description = "Learn 200 patterns",
                Icon = "\uE82D", Target = 200, Progress = Math.Min(200, totalPatterns),
                IsUnlocked = totalPatterns >= 200, Tier = "Silver", Category = "Learning"
            });
            achievements.Add(new Achievement
            {
                Id = "imports_10", Name = "Data Feeder", Description = "Import 10 estimates",
                Icon = "\uE82D", Target = 10, Progress = Math.Min(10, estimatesImported),
                IsUnlocked = estimatesImported >= 10, Tier = "Bronze", Category = "Learning"
            });
            achievements.Add(new Achievement
            {
                Id = "health_80", Name = "Brain Trust", Description = "Reach learning health score of 80",
                Icon = "\uE82D", Target = 80, Progress = Math.Min(80, healthScore),
                IsUnlocked = healthScore >= 80, Tier = "Gold", Category = "Learning"
            });

            // === Accuracy (4) ===
            var avgOps = allTimeStats.TotalEstimates > 0
                ? (double)allTimeStats.TotalOperations / allTimeStats.TotalEstimates : 0;
            achievements.Add(new Achievement
            {
                Id = "efficiency_5", Name = "Efficient Estimator", Description = "Average 5+ ops per estimate",
                Icon = "\uE9D9", Target = 5, Progress = Math.Min(5, (int)avgOps),
                IsUnlocked = avgOps >= 5, Tier = "Silver", Category = "Accuracy"
            });
            achievements.Add(new Achievement
            {
                Id = "efficiency_8", Name = "Thoroughness Pro", Description = "Average 8+ ops per estimate",
                Icon = "\uE9D9", Target = 8, Progress = Math.Min(8, (int)avgOps),
                IsUnlocked = avgOps >= 8, Tier = "Gold", Category = "Accuracy"
            });
            achievements.Add(new Achievement
            {
                Id = "accurate_10", Name = "Sharpshooter", Description = "Get 10 accurate estimates",
                Icon = "\uE9D9", Target = 10, Progress = Math.Min(10, accurateCount),
                IsUnlocked = accurateCount >= 10, Tier = "Bronze", Category = "Accuracy"
            });
            achievements.Add(new Achievement
            {
                Id = "accurate_50", Name = "Bullseye", Description = "Get 50 accurate estimates",
                Icon = "\uE9D9", Target = 50, Progress = Math.Min(50, accurateCount),
                IsUnlocked = accurateCount >= 50, Tier = "Gold", Category = "Accuracy"
            });

            return achievements;
        }

        /// <summary>
        /// Get smart insights based on user patterns
        /// </summary>
        public List<SmartInsight> GetSmartInsights(string userId)
        {
            var insights = new List<SmartInsight>();
            var todayStats = GetEnhancedStats(userId, StatsPeriod.Today);
            var weekStats = GetEnhancedStats(userId, StatsPeriod.ThisWeek);
            var monthStats = GetEnhancedStats(userId, StatsPeriod.ThisMonth);
            var goals = GetUserGoals(userId);
            var weekComparison = GetWeeklyComparison(userId);

            // Goal progress insights
            if (goals.OperationsProgress >= 100)
            {
                insights.Add(new SmartInsight
                {
                    Type = "Achievement", Title = "Daily Goal Crushed!",
                    Message = $"You hit {goals.CurrentDayOperations} operations today!",
                    Icon = "\uE735", Color = "#00B050"
                });
            }
            else if (goals.OperationsProgress >= 75)
            {
                insights.Add(new SmartInsight
                {
                    Type = "Tip", Title = "Almost There!",
                    Message = $"Just {goals.DailyOperationsTarget - goals.CurrentDayOperations} more operations to hit your daily goal.",
                    Icon = "\uE9D5", Color = "#FFC000"
                });
            }

            // Week-over-week insights
            if (weekComparison.IsImproving)
            {
                insights.Add(new SmartInsight
                {
                    Type = "Milestone", Title = "On Fire This Week!",
                    Message = $"You're up {weekComparison.OperationsChange:F0}% in operations vs last week.",
                    Icon = "\uE735", Color = "#00B050"
                });
            }
            else if (weekComparison.OperationsChange < -20)
            {
                insights.Add(new SmartInsight
                {
                    Type = "Alert", Title = "Slower Week",
                    Message = "Operations are down from last week. Need a boost?",
                    Icon = "\uE7BA", Color = "#FF6B6B"
                });
            }

            // Productivity insights
            var hourlyActivity = GetHourlyActivity(userId, StatsPeriod.ThisMonth);
            var peakHour = hourlyActivity.OrderByDescending(h => h.OperationCount).FirstOrDefault();
            if (peakHour != null && peakHour.OperationCount > 10)
            {
                insights.Add(new SmartInsight
                {
                    Type = "Tip", Title = "Peak Performance Time",
                    Message = $"You're most productive at {peakHour.FormattedHour}. Schedule important estimates then!",
                    Icon = "\uE823", Color = "#0078D4"
                });
            }

            // Value insights
            if (monthStats.AvgValuePerEstimate > 500)
            {
                insights.Add(new SmartInsight
                {
                    Type = "Milestone", Title = "High-Value Estimator",
                    Message = $"Your average estimate value is {monthStats.AvgValuePerEstimate:C0}. Keep it up!",
                    Icon = "\uE8C8", Color = "#00B050"
                });
            }

            return insights.Take(4).ToList(); // Limit to top 4 insights
        }

        /// <summary>
        /// Get week-over-week comparison
        /// </summary>
        public WeeklyComparison GetWeeklyComparison(string userId)
        {
            var today = DateTime.Today;
            var thisWeekStart = today.AddDays(-(int)today.DayOfWeek);
            var lastWeekStart = thisWeekStart.AddDays(-7);
            var lastWeekEnd = thisWeekStart.AddDays(-1);

            var thisWeekData = _data.Transactions
                .Where(t => t.UserId == userId && t.Type == TransactionType.Export)
                .Where(t => t.Timestamp.Date >= thisWeekStart)
                .ToList();

            var lastWeekData = _data.Transactions
                .Where(t => t.UserId == userId && t.Type == TransactionType.Export)
                .Where(t => t.Timestamp.Date >= lastWeekStart && t.Timestamp.Date <= lastWeekEnd)
                .ToList();

            return new WeeklyComparison
            {
                ThisWeekOperations = thisWeekData.Sum(t => t.OperationCount),
                LastWeekOperations = lastWeekData.Sum(t => t.OperationCount),
                ThisWeekValue = thisWeekData.Sum(t => t.TotalPrice),
                LastWeekValue = lastWeekData.Sum(t => t.TotalPrice),
                ThisWeekEstimates = thisWeekData.Count,
                LastWeekEstimates = lastWeekData.Count
            };
        }

        #endregion
    }

    #region Enums

    public enum TransactionType
    {
        Import,  // Data read from clipboard (into McStud)
        Export,  // Data sent to CCC/Mitchell (out of McStud)
        Learn    // Estimate learned/trained from Import tab
    }

    public enum StatsPeriod
    {
        Today,
        ThisWeek,
        ThisMonth,
        ThisYear,
        AllTime
    }

    #endregion

    #region Data Models

    public class StatsData
    {
        public List<TransactionRecord> Transactions { get; set; } = new List<TransactionRecord>();
        public List<PdfExportRecord> PdfExports { get; set; } = new List<PdfExportRecord>();
        public List<SessionRecord> Sessions { get; set; } = new List<SessionRecord>();
        public List<UserGoals> UserGoals { get; set; } = new List<UserGoals>();
    }

    public class TransactionRecord
    {
        public DateTime Timestamp { get; set; }
        public TransactionType Type { get; set; }
        public string UserId { get; set; } = "";   // Windows username who performed the operation
        public string Source { get; set; } = "";   // For imports: "Excel"
        public string Target { get; set; } = "";   // For exports: "CCC" or "Mitchell"
        public int OperationCount { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal TotalLabor { get; set; }
        public decimal TotalPaint { get; set; }
        public List<OperationRecord> Operations { get; set; } = new List<OperationRecord>();
    }

    public class OperationRecord
    {
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Price { get; set; }
        public decimal Labor { get; set; }
        public decimal Paint { get; set; }
    }

    public class PeriodStats
    {
        public string PeriodName { get; set; } = "";
        public string TypeLabel { get; set; } = "";
        public int TransactionCount { get; set; }
        public int OperationCount { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal TotalLabor { get; set; }
        public decimal TotalPaint { get; set; }
        public int CCCCount { get; set; }
        public int MitchellCount { get; set; }

        public string FormattedPrice => TotalPrice.ToString("C0");
        public string FormattedLabor => TotalLabor.ToString("F1") + " hrs";
        public string FormattedPaint => TotalPaint.ToString("F1") + " hrs";
    }

    /// <summary>
    /// Combined stats showing both imports and exports for comparison
    /// </summary>
    public class CombinedStats
    {
        public string PeriodName { get; set; } = "";
        public string TargetFilter { get; set; } = "All";

        // Import totals (what came IN)
        public int ImportCount { get; set; }
        public int ImportOperations { get; set; }
        public decimal ImportPrice { get; set; }
        public decimal ImportLabor { get; set; }
        public decimal ImportPaint { get; set; }

        // Export totals (what went OUT) - may be filtered by target
        public int ExportCount { get; set; }
        public int ExportOperations { get; set; }
        public decimal ExportPrice { get; set; }
        public decimal ExportLabor { get; set; }
        public decimal ExportPaint { get; set; }

        // Learn totals (estimates learned/trained)
        public int LearnCount { get; set; }
        public int LearnOperations { get; set; }

        // Target breakdown (operations by target)
        public int CCCDesktopOps { get; set; }
        public int CCCWebOps { get; set; }
        public int MitchellOps { get; set; }

        // Formatted values
        public string FormattedImportPrice => ImportPrice.ToString("C0");
        public string FormattedExportPrice => ExportPrice.ToString("C0");
        public string FormattedImportLabor => ImportLabor.ToString("F1") + " hrs";
        public string FormattedExportLabor => ExportLabor.ToString("F1") + " hrs";
        public string FormattedImportPaint => ImportPaint.ToString("F1") + " hrs";
        public string FormattedExportPaint => ExportPaint.ToString("F1") + " hrs";

        // Accuracy check - do imports match exports?
        public bool OperationsMatch => ImportOperations == ExportOperations;
        public bool PriceMatches => ImportPrice == ExportPrice;
        public bool LaborMatches => ImportLabor == ExportLabor;
        public bool PaintMatches => ImportPaint == ExportPaint;
        public bool AllMatch => OperationsMatch && PriceMatches && LaborMatches && PaintMatches;

        // Differences (positive = more imported than exported, negative = more exported)
        public int OperationsDiff => ImportOperations - ExportOperations;
        public decimal PriceDiff => ImportPrice - ExportPrice;
        public decimal LaborDiff => ImportLabor - ExportLabor;
        public decimal PaintDiff => ImportPaint - ExportPaint;

        // Target breakdown summary
        public string TargetBreakdown => $"CCC: {CCCDesktopOps} | Web: {CCCWebOps} | Mitch: {MitchellOps}";
    }

    public class DailyStats
    {
        public DateTime Date { get; set; }

        // Import totals
        public int ImportCount { get; set; }
        public int ImportOperations { get; set; }
        public decimal ImportPrice { get; set; }
        public decimal ImportLabor { get; set; }
        public decimal ImportPaint { get; set; }

        // Export totals
        public int ExportCount { get; set; }
        public int ExportOperations { get; set; }
        public decimal ExportPrice { get; set; }
        public decimal ExportLabor { get; set; }
        public decimal ExportPaint { get; set; }

        // Learn totals
        public int LearnCount { get; set; }
        public int LearnOperations { get; set; }

        public string FormattedDate => Date.ToString("MMM dd");
        public string DayOfWeek => Date.ToString("ddd");

        // Quick accuracy check
        public bool OperationsMatch => ImportOperations == ExportOperations;
    }

    /// <summary>
    /// Statistics for a specific operation type
    /// </summary>
    public class OperationTypeStats
    {
        public string OperationType { get; set; } = "";
        public int Count { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal TotalLabor { get; set; }
        public decimal TotalPaint { get; set; }

        public string FormattedPrice => TotalPrice.ToString("C0");
        public string FormattedLabor => TotalLabor.ToString("F1") + " hrs";
    }

    /// <summary>
    /// Per-user statistics for leaderboard/comparison
    /// </summary>
    public class UserStats
    {
        public string UserId { get; set; } = "";
        public int TotalTransactions { get; set; }
        public int TotalOperations { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal TotalLabor { get; set; }
        public int ImportCount { get; set; }
        public int ExportCount { get; set; }

        public string FormattedPrice => TotalPrice.ToString("C0");
        public string FormattedLabor => TotalLabor.ToString("F1") + " hrs";
    }

    /// <summary>
    /// Shop-wide aggregate statistics across all users
    /// </summary>
    public class ShopWideStats
    {
        public int TotalUsers { get; set; }
        public int TotalExports { get; set; }
        public int TotalOperations { get; set; }
        public decimal TotalValue { get; set; }
        public decimal TotalLaborHours { get; set; }
        public decimal TotalRefinishHours { get; set; }

        // Averages
        public decimal AvgOperationsPerUser { get; set; }
        public decimal AvgValuePerUser { get; set; }
        public decimal AvgLaborPerUser { get; set; }
        public decimal AvgOperationsPerExport { get; set; }
        public decimal AvgValuePerExport { get; set; }
        public decimal AvgValuePerOperation { get; set; }

        // Formatted values
        public string FormattedTotalValue => TotalValue.ToString("C0");
        public string FormattedAvgValuePerUser => AvgValuePerUser.ToString("C0");
        public string FormattedAvgValuePerExport => AvgValuePerExport.ToString("C0");
        public string FormattedAvgValuePerOp => AvgValuePerOperation.ToString("C2");
    }

    /// <summary>
    /// PDF export tracking record
    /// </summary>
    public class PdfExportRecord
    {
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ExportType { get; set; } = "";  // "Estimate", "Supplement", "Report"
        public int PageCount { get; set; }
        public long FileSizeBytes { get; set; }
    }

    /// <summary>
    /// Aggregated PDF export statistics
    /// </summary>
    public class PdfExportStats
    {
        public int TotalExports { get; set; }
        public int TotalPages { get; set; }
        public long TotalSizeBytes { get; set; }
        public Dictionary<string, int> ByType { get; set; } = new Dictionary<string, int>();

        public string FormattedSize => FormatBytes(TotalSizeBytes);

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }

    /// <summary>
    /// Daily PDF export statistics
    /// </summary>
    public class DailyPdfStats
    {
        public DateTime Date { get; set; }
        public int ExportCount { get; set; }
        public int TotalPages { get; set; }
        public long TotalSizeBytes { get; set; }

        public string FormattedDate => Date.ToString("MMM dd");
    }

    /// <summary>
    /// Session tracking - tracks work sessions
    /// </summary>
    public class SessionRecord
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int EstimatesCompleted { get; set; }
        public int OperationsExported { get; set; }
        public decimal TotalValue { get; set; }

        public TimeSpan Duration => (EndTime ?? DateTime.Now) - StartTime;
        public string FormattedDuration
        {
            get
            {
                var d = Duration;
                if (d.TotalMinutes < 60) return $"{d.TotalMinutes:F0}m";
                return $"{d.Hours}h {d.Minutes}m";
            }
        }
        public bool IsActive => EndTime == null;
    }

    /// <summary>
    /// Enhanced statistics with trends and averages
    /// </summary>
    public class EnhancedStats
    {
        // Current period stats
        public int TotalEstimates { get; set; }
        public int TotalOperations { get; set; }
        public decimal TotalValue { get; set; }
        public decimal TotalLaborHours { get; set; }

        // Averages
        public double AvgOperationsPerEstimate { get; set; }
        public decimal AvgValuePerEstimate { get; set; }
        public decimal AvgValuePerOperation { get; set; }

        // Session stats
        public int TotalSessions { get; set; }
        public TimeSpan TotalTimeSpent { get; set; }
        public double AvgEstimatesPerSession { get; set; }
        public TimeSpan AvgSessionDuration { get; set; }

        // Streaks
        public int CurrentStreak { get; set; }  // Consecutive days with activity
        public int LongestStreak { get; set; }

        // Trends (compared to previous period)
        public double EstimatesTrend { get; set; }  // % change
        public double OperationsTrend { get; set; }
        public double ValueTrend { get; set; }

        public string FormattedValue => TotalValue.ToString("C0");
        public string FormattedAvgValue => AvgValuePerEstimate.ToString("C0");
        public string FormattedTimeSpent
        {
            get
            {
                if (TotalTimeSpent.TotalMinutes < 60) return $"{TotalTimeSpent.TotalMinutes:F0}m";
                if (TotalTimeSpent.TotalHours < 24) return $"{TotalTimeSpent.Hours}h {TotalTimeSpent.Minutes}m";
                return $"{TotalTimeSpent.TotalHours:F0}h";
            }
        }
    }

    /// <summary>
    /// Part/operation type breakdown
    /// </summary>
    public class PartTypeStats
    {
        public string PartType { get; set; } = "";  // Bumper, Fender, Quarter Panel, etc.
        public int Count { get; set; }
        public decimal TotalLabor { get; set; }
        public decimal TotalValue { get; set; }
        public double Percentage { get; set; }

        public string FormattedLabor => TotalLabor.ToString("F1") + "h";
    }

    /// <summary>
    /// Hourly activity breakdown
    /// </summary>
    public class HourlyActivity
    {
        public int Hour { get; set; }  // 0-23
        public int ExportCount { get; set; }
        public int OperationCount { get; set; }
        public int LearnCount { get; set; }

        public string FormattedHour => Hour switch
        {
            0 => "12 AM",
            12 => "12 PM",
            < 12 => $"{Hour} AM",
            _ => $"{Hour - 12} PM"
        };
    }

    /// <summary>
    /// Performance ranking/percentile data
    /// </summary>
    public class PerformanceRanking
    {
        public string UserId { get; set; } = "";
        public int Rank { get; set; }
        public int TotalUsers { get; set; }
        public double Percentile { get; set; }  // 0-100, higher is better
        public int UserOperations { get; set; }
        public decimal UserValue { get; set; }
        public double AvgOpsPerEstimate { get; set; }
        public double TopUserAvgOps { get; set; }  // Best performer's avg

        public string PercentileLabel => Percentile >= 90 ? "Top 10%" :
                                         Percentile >= 75 ? "Top 25%" :
                                         Percentile >= 50 ? "Top 50%" :
                                         "Below Average";
        public bool IsTopPerformer => Percentile >= 75;
    }

    /// <summary>
    /// Category breakdown (Labor vs Refinish vs Materials)
    /// </summary>
    public class CategoryBreakdown
    {
        public decimal LaborDollars { get; set; }
        public decimal RefinishDollars { get; set; }
        public decimal MaterialsDollars { get; set; }
        public decimal OtherDollars { get; set; }
        public decimal TotalDollars { get; set; }

        public double LaborPercent => TotalDollars > 0 ? (double)(LaborDollars / TotalDollars * 100) : 0;
        public double RefinishPercent => TotalDollars > 0 ? (double)(RefinishDollars / TotalDollars * 100) : 0;
        public double MaterialsPercent => TotalDollars > 0 ? (double)(MaterialsDollars / TotalDollars * 100) : 0;
        public double OtherPercent => TotalDollars > 0 ? (double)(OtherDollars / TotalDollars * 100) : 0;

        public string FormattedLabor => LaborDollars.ToString("C0");
        public string FormattedRefinish => RefinishDollars.ToString("C0");
        public string FormattedMaterials => MaterialsDollars.ToString("C0");
    }

    /// <summary>
    /// ROI (Return on Investment) calculation
    /// </summary>
    public class ROIStats
    {
        public decimal TotalValueCaptured { get; set; }
        public int TotalOperationsAdded { get; set; }
        public int EstimatesProcessed { get; set; }
        public decimal AvgValuePerEstimate { get; set; }
        public decimal EstimatedMissedWithoutTool { get; set; }  // What would have been missed
        public TimeSpan TimeSaved { get; set; }  // Estimated time saved

        public string FormattedValueCaptured => TotalValueCaptured.ToString("C0");
        public string FormattedAvgPerEstimate => AvgValuePerEstimate.ToString("C0");
        public string FormattedMissed => EstimatedMissedWithoutTool.ToString("C0");
        public string FormattedTimeSaved => TimeSaved.TotalHours >= 1
            ? $"{TimeSaved.TotalHours:F1} hrs"
            : $"{TimeSaved.TotalMinutes:F0} min";
    }

    /// <summary>
    /// User goals and targets
    /// </summary>
    public class UserGoals
    {
        public string UserId { get; set; } = "";
        public int DailyOperationsTarget { get; set; } = 50;
        public int DailyEstimatesTarget { get; set; } = 10;
        public decimal DailyValueTarget { get; set; } = 5000;
        public double TargetOpsPerEstimate { get; set; } = 5.0;

        // Progress tracking
        public int CurrentDayOperations { get; set; }
        public int CurrentDayEstimates { get; set; }
        public decimal CurrentDayValue { get; set; }

        public double OperationsProgress => DailyOperationsTarget > 0 ? (double)CurrentDayOperations / DailyOperationsTarget * 100 : 0;
        public double EstimatesProgress => DailyEstimatesTarget > 0 ? (double)CurrentDayEstimates / DailyEstimatesTarget * 100 : 0;
        public double ValueProgress => DailyValueTarget > 0 ? (double)(CurrentDayValue / DailyValueTarget * 100) : 0;

        public bool OperationsGoalMet => CurrentDayOperations >= DailyOperationsTarget;
        public bool EstimatesGoalMet => CurrentDayEstimates >= DailyEstimatesTarget;
        public bool ValueGoalMet => CurrentDayValue >= DailyValueTarget;
    }

    /// <summary>
    /// Individual estimate detail for drill-down
    /// </summary>
    public class EstimateDetail
    {
        public DateTime Timestamp { get; set; }
        public string Target { get; set; } = "";
        public int OperationCount { get; set; }
        public decimal TotalValue { get; set; }
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public List<OperationRecord> Operations { get; set; } = new();

        public string FormattedTime => Timestamp.ToString("h:mm tt");
        public string FormattedDate => Timestamp.ToString("MMM d");
        public string FormattedValue => TotalValue.ToString("C0");
    }

    /// <summary>
    /// Most commonly missed operations
    /// </summary>
    public class MissedOperationStats
    {
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public int TimesAdded { get; set; }
        public int TimesSuggested { get; set; }
        public decimal AvgValue { get; set; }
        public double AcceptanceRate => TimesSuggested > 0 ? (double)TimesAdded / TimesSuggested * 100 : 0;

        public string FormattedAvgValue => AvgValue.ToString("C0");
    }

    /// <summary>
    /// Achievement/Badge data - gamification element
    /// </summary>
    public class Achievement
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "\uE735"; // Default star
        public bool IsUnlocked { get; set; }
        public DateTime? UnlockedAt { get; set; }
        public int Progress { get; set; }  // 0-100
        public int Target { get; set; }
        public string Tier { get; set; } = "Bronze"; // Bronze, Silver, Gold, Platinum
        public string Category { get; set; } = "General";

        public string TierColor => Tier switch
        {
            "Platinum" => "#E5E4E2",
            "Gold" => "#FFD700",
            "Silver" => "#C0C0C0",
            _ => "#CD7F32"
        };

        public static string CategoryIcon(string category) => category switch
        {
            "Export Stats" => "\uE8B8",   // Clipboard
            "Value" => "\uE8C8",          // Money
            "Streaks" => "\uE735",        // Star
            "Time Use" => "\uE823",       // Clock
            "Usage" => "\uE8A5",          // Document
            "Customization" => "\uE771",  // Settings
            "Learning" => "\uE82D",       // Education
            "Accuracy" => "\uE9D9",       // Target
            _ => "\uE734"                 // Star outline
        };
    }

    /// <summary>
    /// Smart insight/tip based on user patterns
    /// </summary>
    public class SmartInsight
    {
        public string Type { get; set; } = "";  // Tip, Alert, Achievement, Milestone
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Icon { get; set; } = "\uE82F";
        public string Color { get; set; } = "#0078D4";
        public bool IsActionable { get; set; }
        public string ActionText { get; set; } = "";
    }

    /// <summary>
    /// Weekly comparison data
    /// </summary>
    public class WeeklyComparison
    {
        public int ThisWeekOperations { get; set; }
        public int LastWeekOperations { get; set; }
        public decimal ThisWeekValue { get; set; }
        public decimal LastWeekValue { get; set; }
        public int ThisWeekEstimates { get; set; }
        public int LastWeekEstimates { get; set; }

        public double OperationsChange => LastWeekOperations > 0
            ? (double)(ThisWeekOperations - LastWeekOperations) / LastWeekOperations * 100 : 0;
        public double ValueChange => LastWeekValue > 0
            ? (double)((ThisWeekValue - LastWeekValue) / LastWeekValue * 100) : 0;
        public bool IsImproving => OperationsChange > 0 && ValueChange > 0;
    }

    #endregion
}
