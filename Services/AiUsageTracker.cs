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
    /// Tracks AI API usage, enforces budget/call limits, and provides usage statistics.
    /// Persists to %LocalAppData%/McStudDesktop/ai_usage.json.
    /// Thread-safe with lock.
    /// </summary>
    public class AiUsageTracker
    {
        private static AiUsageTracker? _instance;
        public static AiUsageTracker Instance => _instance ??= new AiUsageTracker();

        private readonly string _dataFilePath;
        private AiUsageData _data;
        private readonly object _lock = new();

        // Pricing constants per token (model-specific, see CalculateCost)
        private static readonly Dictionary<string, (decimal Input, decimal Output)> ModelPricing = new()
        {
            { "claude-3-haiku-20240307", (0.00000025m, 0.00000125m) },   // $0.25/$1.25 per million
            { "claude-haiku-4-5-20251001", (0.000001m, 0.000005m) },     // $1.00/$5.00 per million
            { "claude-sonnet-4-20250514", (0.000003m, 0.000015m) },      // $3.00/$15.00 per million
        };
        private const decimal DEFAULT_INPUT_COST = 0.000001m;   // $1.00 per million (Haiku 4.5 default)
        private const decimal DEFAULT_OUTPUT_COST = 0.000005m;   // $5.00 per million

        public AiUsageTracker()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(appDataPath);
            _dataFilePath = Path.Combine(appDataPath, "ai_usage.json");
            _data = LoadData();
            PurgeOldRecords();
        }

        /// <summary>
        /// Record a completed API call with token usage
        /// </summary>
        public void RecordCall(string feature, string model, int inputTokens, int outputTokens, double latencyMs, bool success, string? error = null)
        {
            lock (_lock)
            {
                var cost = CalculateCost(inputTokens, outputTokens, model);
                var record = new AiUsageRecord
                {
                    Timestamp = DateTime.Now,
                    Feature = feature,
                    Model = model,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    Cost = cost,
                    LatencyMs = latencyMs,
                    Success = success,
                    Error = error
                };
                _data.Records.Add(record);

                // Update daily stats
                var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
                if (!_data.DailyStats.TryGetValue(today, out var stats))
                {
                    stats = new AiDailyStats();
                    _data.DailyStats[today] = stats;
                }
                stats.TotalCalls++;
                stats.TotalCost += cost;
                stats.TotalInputTokens += inputTokens;
                stats.TotalOutputTokens += outputTokens;
                if (success) stats.SuccessfulCalls++;
                else stats.FailedCalls++;

                SaveData();
            }
        }

        /// <summary>
        /// Check if a call can be made (within budget and call limits)
        /// </summary>
        public bool CanMakeCall()
        {
            lock (_lock)
            {
                var config = AiConfigService.Instance;
                if (!config.IsEnabled) return false;

                var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
                if (!_data.DailyStats.TryGetValue(today, out var stats))
                    return true; // No calls today yet

                // Check daily call limit
                if (stats.TotalCalls >= config.GetMaxCallsPerDay())
                {
                    System.Diagnostics.Debug.WriteLine($"[AiUsage] Daily call limit reached: {stats.TotalCalls}/{config.GetMaxCallsPerDay()}");
                    return false;
                }

                // Check daily budget
                if (stats.TotalCost >= config.GetDailyBudgetCap())
                {
                    System.Diagnostics.Debug.WriteLine($"[AiUsage] Daily budget cap reached: ${stats.TotalCost:F4}/${config.GetDailyBudgetCap():F2}");
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Get usage summary for a time period
        /// </summary>
        public AiUsageSummary GetSummary(AiUsagePeriod period)
        {
            lock (_lock)
            {
                var cutoff = period switch
                {
                    AiUsagePeriod.Today => DateTime.Now.Date,
                    AiUsagePeriod.Week => DateTime.Now.Date.AddDays(-7),
                    AiUsagePeriod.Month => DateTime.Now.Date.AddDays(-30),
                    AiUsagePeriod.AllTime => DateTime.MinValue,
                    _ => DateTime.Now.Date
                };

                var records = _data.Records.Where(r => r.Timestamp >= cutoff).ToList();

                var summary = new AiUsageSummary
                {
                    Period = period,
                    TotalCalls = records.Count,
                    SuccessfulCalls = records.Count(r => r.Success),
                    FailedCalls = records.Count(r => !r.Success),
                    TotalCost = records.Sum(r => r.Cost),
                    TotalInputTokens = records.Sum(r => r.InputTokens),
                    TotalOutputTokens = records.Sum(r => r.OutputTokens),
                    AverageLatencyMs = records.Count > 0 ? records.Average(r => r.LatencyMs) : 0
                };

                // Per-feature breakdown
                foreach (var group in records.GroupBy(r => r.Feature))
                {
                    summary.PerFeature[group.Key] = new AiFeatureStats
                    {
                        Calls = group.Count(),
                        Cost = group.Sum(r => r.Cost),
                        SuccessRate = group.Count() > 0 ? (double)group.Count(r => r.Success) / group.Count() : 0
                    };
                }

                return summary;
            }
        }

        /// <summary>
        /// Get today's stats for the dashboard
        /// </summary>
        public AiDailyStats GetTodayStats()
        {
            lock (_lock)
            {
                var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
                return _data.DailyStats.TryGetValue(today, out var stats)
                    ? stats
                    : new AiDailyStats();
            }
        }

        /// <summary>
        /// Calculate cost for a given token count, using model-specific pricing
        /// </summary>
        public static decimal CalculateCost(int inputTokens, int outputTokens, string? model = null)
        {
            var inputRate = DEFAULT_INPUT_COST;
            var outputRate = DEFAULT_OUTPUT_COST;

            if (!string.IsNullOrEmpty(model) && ModelPricing.TryGetValue(model, out var pricing))
            {
                inputRate = pricing.Input;
                outputRate = pricing.Output;
            }

            return (inputTokens * inputRate) + (outputTokens * outputRate);
        }

        #region Private

        private void PurgeOldRecords()
        {
            lock (_lock)
            {
                var cutoff = DateTime.Now.AddDays(-90);
                var before = _data.Records.Count;
                _data.Records.RemoveAll(r => r.Timestamp < cutoff);

                // Also purge old daily stats
                var keysToRemove = _data.DailyStats.Keys
                    .Where(k => DateTime.TryParse(k, out var d) && d < cutoff)
                    .ToList();
                foreach (var key in keysToRemove)
                    _data.DailyStats.Remove(key);

                if (_data.Records.Count < before)
                {
                    System.Diagnostics.Debug.WriteLine($"[AiUsage] Purged {before - _data.Records.Count} records older than 90 days");
                    SaveData();
                }
            }
        }

        private AiUsageData LoadData()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<AiUsageData>(json, options) ?? new AiUsageData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiUsage] Load error: {ex.Message}");
            }
            return new AiUsageData();
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
                System.Diagnostics.Debug.WriteLine($"[AiUsage] Save error: {ex.Message}");
            }
        }

        #endregion
    }

    #region Usage Models

    public class AiUsageData
    {
        public List<AiUsageRecord> Records { get; set; } = new();
        public Dictionary<string, AiDailyStats> DailyStats { get; set; } = new();
    }

    public class AiUsageRecord
    {
        public DateTime Timestamp { get; set; }
        public string Feature { get; set; } = "";
        public string Model { get; set; } = "";
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public decimal Cost { get; set; }
        public double LatencyMs { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public class AiDailyStats
    {
        public int TotalCalls { get; set; }
        public int SuccessfulCalls { get; set; }
        public int FailedCalls { get; set; }
        public decimal TotalCost { get; set; }
        public long TotalInputTokens { get; set; }
        public long TotalOutputTokens { get; set; }
    }

    public class AiUsageSummary
    {
        public AiUsagePeriod Period { get; set; }
        public int TotalCalls { get; set; }
        public int SuccessfulCalls { get; set; }
        public int FailedCalls { get; set; }
        public decimal TotalCost { get; set; }
        public long TotalInputTokens { get; set; }
        public long TotalOutputTokens { get; set; }
        public double AverageLatencyMs { get; set; }
        public Dictionary<string, AiFeatureStats> PerFeature { get; set; } = new();
    }

    public class AiFeatureStats
    {
        public int Calls { get; set; }
        public decimal Cost { get; set; }
        public double SuccessRate { get; set; }
    }

    public enum AiUsagePeriod
    {
        Today,
        Week,
        Month,
        AllTime
    }

    #endregion
}
