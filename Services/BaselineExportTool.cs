#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Static utility to export and sanitize your data for distribution as baseline.
    /// Run this on YOUR machine, then ship the sanitized files with the app.
    ///
    /// Sanitization rules:
    /// - Strip: RO numbers, claim numbers, VINs, source file paths, user IDs
    /// - Regenerate: Estimate IDs (new GUIDs)
    /// - Keep: Patterns, hours, operation types, insurer names, vehicle make/model/year,
    ///         confidence scores, feedback ratios, part names, categories
    /// </summary>
    public static class BaselineExportTool
    {
        /// <summary>
        /// Export and sanitize all learning data to the Data/ folder for distribution.
        /// Returns a summary of what was exported.
        /// </summary>
        public static BaselineExportResult ExportAndSanitize()
        {
            var result = new BaselineExportResult();
            var outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            Directory.CreateDirectory(outputPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            try
            {
                // 1. Export Estimate History (sanitized)
                var historyResult = ExportEstimateHistory(outputPath, options);
                result.EstimateHistoryCount = historyResult.count;
                result.EstimateHistorySize = historyResult.size;

                // 2. Export Feedback (sanitized)
                var feedbackResult = ExportFeedback(outputPath, options);
                result.FeedbackCount = feedbackResult.count;
                result.FeedbackSize = feedbackResult.size;

                // 3. Export Accuracy (sanitized)
                var accuracyResult = ExportAccuracy(outputPath, options);
                result.AccuracyCount = accuracyResult.count;
                result.AccuracySize = accuracyResult.size;

                // 4. Learned Patterns — already published via EstimateLearningService.PublishLearning()
                //    Just note it in the result
                var patternsPath = Path.Combine(outputPath, "LearnedPatterns.json");
                if (File.Exists(patternsPath))
                {
                    result.PatternsSize = new FileInfo(patternsPath).Length;
                    result.PatternsExist = true;
                }

                // Auto-bump baseline version
                var versionPath = Path.Combine(outputPath, "baseline_version.txt");
                int currentVersion = 0;
                if (File.Exists(versionPath))
                {
                    int.TryParse(File.ReadAllText(versionPath).Trim(), out currentVersion);
                }
                int newVersion = currentVersion + 1;
                File.WriteAllText(versionPath, newVersion.ToString());
                result.NewVersion = newVersion;

                result.Success = true;
                result.OutputPath = outputPath;
                result.Message = $"Baseline v{newVersion} exported: {result.EstimateHistoryCount} estimates, " +
                                 $"{result.FeedbackCount} feedback entries, " +
                                 $"{result.AccuracyCount} accuracy records";

                System.Diagnostics.Debug.WriteLine($"[BaselineExport] {result.Message}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Export failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[BaselineExport] Error: {ex.Message}");
            }

            return result;
        }

        private static (int count, long size) ExportEstimateHistory(string outputPath, JsonSerializerOptions options)
        {
            var rawData = EstimateHistoryDatabase.Instance.GetRawData();
            if (rawData.Estimates.Count == 0)
                return (0, 0);

            // Deep clone and sanitize
            var sanitized = new EstimateHistoryData
            {
                LastUpdated = rawData.LastUpdated
            };

            // Map old IDs to new IDs for consistent references
            var idMap = new Dictionary<string, string>();

            foreach (var estimate in rawData.Estimates)
            {
                var newId = Guid.NewGuid().ToString();
                idMap[estimate.Id] = newId;

                var clean = new StoredEstimate
                {
                    Id = newId,
                    ImportedDate = estimate.ImportedDate,
                    SourceFile = "", // Strip file path
                    EstimateSource = estimate.EstimateSource,
                    InsuranceCompany = estimate.InsuranceCompany,
                    RONumber = "", // Strip RO number
                    ClaimNumber = "", // Strip claim number
                    VehicleInfo = SanitizeVehicleInfo(estimate.VehicleInfo),
                    VehicleType = estimate.VehicleType,
                    VIN = "", // Strip VIN
                    GrandTotal = estimate.GrandTotal,
                    PartsTotal = estimate.PartsTotal,
                    LaborTotal = estimate.LaborTotal,
                    PaintTotal = estimate.PaintTotal,
                    SupplementCount = estimate.SupplementCount,
                    Notes = "", // Strip notes (may contain PII)
                    DNA = estimate.DNA // DNA is already normalized/aggregated
                };

                // Sanitize line items
                foreach (var item in estimate.LineItems)
                {
                    clean.LineItems.Add(new StoredLineItem
                    {
                        Description = item.Description,
                        PartName = item.PartName,
                        OperationType = item.OperationType,
                        Section = item.Section,
                        PartNumber = "", // Strip part numbers (can be traced)
                        LaborHours = item.LaborHours,
                        RefinishHours = item.RefinishHours,
                        Price = item.Price,
                        Quantity = item.Quantity,
                        LaborType = item.LaborType,
                        IsManualLine = item.IsManualLine,
                        IsAdditionalOperation = item.IsAdditionalOperation,
                        ParentPartName = item.ParentPartName,
                        WasPaid = item.WasPaid,
                        WasDenied = item.WasDenied,
                        DenialReason = item.DenialReason
                    });
                }

                sanitized.Estimates.Add(clean);
            }

            // Sanitize OperationPaymentIndex (remap estimate IDs)
            foreach (var (insurerKey, ops) in rawData.OperationPaymentIndex)
            {
                sanitized.OperationPaymentIndex[insurerKey] = new Dictionary<string, OperationPaymentStats>();
                foreach (var (opKey, stats) in ops)
                {
                    var cleanStats = new OperationPaymentStats
                    {
                        OperationDescription = stats.OperationDescription,
                        NormalizedKey = stats.NormalizedKey,
                        TimesPaid = stats.TimesPaid,
                        TotalHoursPaid = stats.TotalHoursPaid,
                        TotalAmountPaid = stats.TotalAmountPaid,
                        AverageHours = stats.AverageHours,
                        EstimateIds = stats.EstimateIds
                            .Where(id => idMap.ContainsKey(id))
                            .Select(id => idMap[id])
                            .ToList()
                    };
                    sanitized.OperationPaymentIndex[insurerKey][opKey] = cleanStats;
                }
            }

            var filePath = Path.Combine(outputPath, "BaselineEstimateHistory.json");
            var json = JsonSerializer.Serialize(sanitized, options);
            File.WriteAllText(filePath, json);

            return (sanitized.Estimates.Count, new FileInfo(filePath).Length);
        }

        private static (int count, long size) ExportFeedback(string outputPath, JsonSerializerOptions options)
        {
            var feedbacks = LearningFeedbackService.Instance.GetAllFeedback();
            if (feedbacks.Count == 0)
                return (0, 0);

            // Feedback data is already pattern-key-based with no PII
            // Just strip any context strings that might contain file paths
            var sanitized = new Dictionary<string, PatternFeedback>();

            foreach (var (key, feedback) in feedbacks)
            {
                var clean = new PatternFeedback
                {
                    PatternKey = feedback.PatternKey,
                    TimesGenerated = feedback.TimesGenerated,
                    TimesAccepted = feedback.TimesAccepted,
                    TimesRejected = feedback.TimesRejected,
                    TimesModified = feedback.TimesModified,
                    FirstUsed = feedback.FirstUsed,
                    LastUsed = feedback.LastUsed,
                    // Strip recent events — they may contain context with PII
                    RecentEvents = new List<FeedbackEvent>()
                };

                sanitized[key] = clean;
            }

            var filePath = Path.Combine(outputPath, "BaselineFeedback.json");
            var json = JsonSerializer.Serialize(sanitized, options);
            File.WriteAllText(filePath, json);

            return (sanitized.Count, new FileInfo(filePath).Length);
        }

        private static (int count, long size) ExportAccuracy(string outputPath, JsonSerializerOptions options)
        {
            var rawData = EstimateAccuracyService.Instance.GetRawData();
            if (rawData.Feedbacks.Count == 0)
                return (0, 0);

            var sanitized = new List<EstimateFeedback>();

            foreach (var feedback in rawData.Feedbacks)
            {
                sanitized.Add(new EstimateFeedback
                {
                    Timestamp = feedback.Timestamp,
                    UserId = "", // Strip user ID
                    RONumber = "", // Strip RO number
                    VehicleInfo = SanitizeVehicleInfo(feedback.VehicleInfo),
                    PrimaryCategory = feedback.PrimaryCategory,
                    WasAccurate = feedback.WasAccurate,
                    SupplementItems = feedback.SupplementItems.Select(s => new SupplementedItem
                    {
                        PartCategory = s.PartCategory,
                        OperationType = s.OperationType,
                        HoursAdded = s.HoursAdded,
                        MaterialsAdded = s.MaterialsAdded,
                        Notes = SanitizeNotes(s.Notes) // Keep generic notes, strip specific identifiers
                    }).ToList()
                });
            }

            var filePath = Path.Combine(outputPath, "BaselineAccuracy.json");
            var json = JsonSerializer.Serialize(sanitized, options);
            File.WriteAllText(filePath, json);

            return (sanitized.Count, new FileInfo(filePath).Length);
        }

        /// <summary>
        /// Sanitize vehicle info: keep make/model/year, strip VIN-like patterns.
        /// </summary>
        private static string SanitizeVehicleInfo(string vehicleInfo)
        {
            if (string.IsNullOrWhiteSpace(vehicleInfo)) return "";

            // Strip anything that looks like a VIN (17 alphanumeric chars)
            var cleaned = Regex.Replace(vehicleInfo, @"\b[A-HJ-NPR-Z0-9]{17}\b", "");

            // Strip claim/RO numbers embedded in vehicle info
            cleaned = Regex.Replace(cleaned, @"\b(RO|Claim|File)[\s#:]*[A-Z0-9-]+\b", "", RegexOptions.IgnoreCase);

            return cleaned.Trim();
        }

        /// <summary>
        /// Sanitize notes: keep generic operational notes, strip identifiers.
        /// </summary>
        private static string SanitizeNotes(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) return "";

            // Strip RO numbers, claim numbers, file paths
            var cleaned = Regex.Replace(notes, @"\b(RO|Claim|File)[\s#:]*[A-Z0-9-]+\b", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"[A-Z]:\\[^\s]+", "", RegexOptions.IgnoreCase); // File paths
            cleaned = Regex.Replace(cleaned, @"\b[A-HJ-NPR-Z0-9]{17}\b", ""); // VINs

            return cleaned.Trim();
        }
    }

    /// <summary>
    /// Result of a baseline export operation
    /// </summary>
    public class BaselineExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string OutputPath { get; set; } = "";

        public int EstimateHistoryCount { get; set; }
        public long EstimateHistorySize { get; set; }

        public int FeedbackCount { get; set; }
        public long FeedbackSize { get; set; }

        public int AccuracyCount { get; set; }
        public long AccuracySize { get; set; }

        public bool PatternsExist { get; set; }
        public long PatternsSize { get; set; }

        public int NewVersion { get; set; }

        public string FormattedSummary => $"Estimates: {EstimateHistoryCount} ({FormatSize(EstimateHistorySize)})\n" +
                                          $"Feedback: {FeedbackCount} ({FormatSize(FeedbackSize)})\n" +
                                          $"Accuracy: {AccuracyCount} ({FormatSize(AccuracySize)})\n" +
                                          $"Patterns: {(PatternsExist ? FormatSize(PatternsSize) : "Not published yet")}";

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
