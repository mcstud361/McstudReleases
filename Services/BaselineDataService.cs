#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Startup service that seeds/updates baseline data across all 4 learning services.
    ///
    /// Flow:
    /// 1. Reads Data/baseline_version.txt (ships with app)
    /// 2. Reads %AppData%/McStudDesktop/baseline_applied_version.txt (tracks last merged version)
    /// 3. If versions differ → runs merge for all 4 services
    /// 4. Writes new applied version after successful merge
    ///
    /// Called from App.cs on startup, before any singleton services are accessed.
    /// </summary>
    public class BaselineDataService
    {
        private static BaselineDataService? _instance;
        public static BaselineDataService Instance => _instance ??= new BaselineDataService();

        private readonly string _appDataPath;
        private readonly string _baselinePath;
        private readonly string _appliedVersionPath;

        private BaselineDataService()
        {
            _baselinePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(_appDataPath);
            _appliedVersionPath = Path.Combine(_appDataPath, "baseline_applied_version.txt");
        }

        /// <summary>
        /// Check if baseline needs to be applied, and apply it if so.
        /// Safe to call on every startup — no-ops if versions match.
        /// </summary>
        public void ApplyBaselineIfNeeded()
        {
            try
            {
                var shippedVersion = ReadShippedVersion();
                var appliedVersion = ReadAppliedVersion();

                System.Diagnostics.Debug.WriteLine($"[Baseline] Shipped version: {shippedVersion}, Applied version: {appliedVersion}");

                if (shippedVersion == appliedVersion)
                {
                    System.Diagnostics.Debug.WriteLine("[Baseline] Versions match — skipping merge");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Baseline] Version mismatch — applying baseline v{shippedVersion}");

                bool success = true;

                // 1. Learned Patterns — already handled by EstimateLearningService's LoadDatabase()
                //    (it loads base + merges user on top). No extra work needed here.
                System.Diagnostics.Debug.WriteLine("[Baseline] Patterns: handled by EstimateLearningService natively");

                // 2. Estimate History
                success &= MergeEstimateHistory();

                // 3. Feedback
                success &= MergeFeedback();

                // 4. Accuracy
                success &= MergeAccuracy();

                if (success)
                {
                    WriteAppliedVersion(shippedVersion);
                    System.Diagnostics.Debug.WriteLine($"[Baseline] Successfully applied baseline v{shippedVersion}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Baseline] Some merges failed — will retry next launch");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Baseline] Error during baseline apply: {ex.Message}");
            }
        }

        private bool MergeEstimateHistory()
        {
            try
            {
                var path = Path.Combine(_baselinePath, "BaselineEstimateHistory.json");
                if (!File.Exists(path))
                {
                    System.Diagnostics.Debug.WriteLine("[Baseline] No BaselineEstimateHistory.json found — skipping");
                    return true; // Not an error, just no data to merge yet
                }

                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var baseline = JsonSerializer.Deserialize<EstimateHistoryData>(json, options);

                if (baseline != null)
                {
                    EstimateHistoryDatabase.Instance.MergeBaseline(baseline);
                    System.Diagnostics.Debug.WriteLine("[Baseline] Estimate history merged successfully");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Baseline] Error merging estimate history: {ex.Message}");
                return false;
            }
        }

        private bool MergeFeedback()
        {
            try
            {
                var path = Path.Combine(_baselinePath, "BaselineFeedback.json");
                if (!File.Exists(path))
                {
                    System.Diagnostics.Debug.WriteLine("[Baseline] No BaselineFeedback.json found — skipping");
                    return true;
                }

                var json = File.ReadAllText(path);
                var baseline = JsonSerializer.Deserialize<Dictionary<string, PatternFeedback>>(json);

                if (baseline != null)
                {
                    LearningFeedbackService.Instance.MergeBaseline(baseline);
                    System.Diagnostics.Debug.WriteLine("[Baseline] Feedback merged successfully");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Baseline] Error merging feedback: {ex.Message}");
                return false;
            }
        }

        private bool MergeAccuracy()
        {
            try
            {
                var path = Path.Combine(_baselinePath, "BaselineAccuracy.json");
                if (!File.Exists(path))
                {
                    System.Diagnostics.Debug.WriteLine("[Baseline] No BaselineAccuracy.json found — skipping");
                    return true;
                }

                var json = File.ReadAllText(path);
                var baseline = JsonSerializer.Deserialize<List<EstimateFeedback>>(json);

                if (baseline != null)
                {
                    EstimateAccuracyService.Instance.MergeBaseline(baseline);
                    System.Diagnostics.Debug.WriteLine("[Baseline] Accuracy data merged successfully");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Baseline] Error merging accuracy: {ex.Message}");
                return false;
            }
        }

        private string ReadShippedVersion()
        {
            var path = Path.Combine(_baselinePath, "baseline_version.txt");
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Trim();
            }
            return "0";
        }

        private string ReadAppliedVersion()
        {
            if (File.Exists(_appliedVersionPath))
            {
                return File.ReadAllText(_appliedVersionPath).Trim();
            }
            return "0";
        }

        private void WriteAppliedVersion(string version)
        {
            File.WriteAllText(_appliedVersionPath, version);
        }
    }
}
