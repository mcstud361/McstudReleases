#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Calculates and maintains learning system health metrics.
    /// Provides overall health score, recommendations, and alerts.
    ///
    /// Health Score Components:
    /// - Pattern quality (confidence distribution)
    /// - Acceptance rates (feedback)
    /// - Freshness (stale patterns)
    /// - Coverage (patterns vs. common operations)
    /// </summary>
    public class LearningHealthService
    {
        private static LearningHealthService? _instance;
        public static LearningHealthService Instance => _instance ??= new LearningHealthService();

        private readonly EstimateLearningService _learningService;
        private readonly LearningFeedbackService _feedbackService;
        private readonly PatternIntelligenceService _intelligenceService;
        private readonly string _healthCachePath;

        private LearningHealthMetrics? _cachedMetrics;
        private DateTime _lastCalculation = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        public LearningHealthService()
        {
            _learningService = EstimateLearningService.Instance;
            _feedbackService = LearningFeedbackService.Instance;
            _intelligenceService = PatternIntelligenceService.Instance;

            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(appDataPath);
            _healthCachePath = Path.Combine(appDataPath, "learning_health.json");

            LoadCachedMetrics();
        }

        #region Health Metrics

        /// <summary>
        /// Calculate comprehensive health metrics for the learning system.
        /// </summary>
        public LearningHealthMetrics CalculateHealthMetrics()
        {
            // Use cache if recent
            if (_cachedMetrics != null && DateTime.Now - _lastCalculation < _cacheExpiry)
            {
                return _cachedMetrics;
            }

            var metrics = new LearningHealthMetrics
            {
                LastCalculated = DateTime.Now
            };

            var stats = _learningService.GetStatistics();
            var feedbackSummary = _feedbackService.GetSummary();

            // Pattern counts
            metrics.TotalPatterns = stats.TotalPatterns;

            // Confidence distribution (would need pattern access)
            // For now, estimate from stats
            metrics.HighConfidencePatterns = (int)(stats.TotalPatterns * stats.AverageConfidence);
            metrics.MediumConfidencePatterns = (int)(stats.TotalPatterns * (1 - stats.AverageConfidence) * 0.6);
            metrics.LowConfidencePatterns = stats.TotalPatterns - metrics.HighConfidencePatterns - metrics.MediumConfidencePatterns;

            // Stale patterns (would need iteration)
            metrics.StalePatterns = 0;  // TODO: Calculate from pattern database

            // Feedback rates
            metrics.OverallAcceptanceRate = feedbackSummary.OverallAcceptanceRate;
            metrics.OverallRejectionRate = feedbackSummary.OverallRejectionRate;

            // Top patterns
            var topRejected = _feedbackService.GetMostRejectedPatterns(10);
            metrics.TopRejectedPatterns = topRejected.Select(f => f.PatternKey).ToList();

            var topAccepted = _feedbackService.GetMostAcceptedPatterns(10);
            metrics.MostUsedPatterns = topAccepted.Select(f => f.PatternKey).ToList();

            // Stale patterns list
            metrics.StalePatternKeys = _intelligenceService.GetStalePatterns();

            // Vehicle type distribution (would need pattern access)
            metrics.PatternsByVehicleType = new Dictionary<string, int>
            {
                { "car", 0 },
                { "truck", 0 },
                { "suv", 0 },
                { "unknown", stats.TotalPatterns }
            };

            // Conflicts (would need detection)
            metrics.ConflictingPatternsCount = 0;

            // Calculate overall health score
            metrics.HealthScore = CalculateHealthScore(metrics, stats);

            // Generate recommendations
            metrics.Recommendations = GenerateRecommendations(metrics, stats);

            // Cache and save
            _cachedMetrics = metrics;
            _lastCalculation = DateTime.Now;
            SaveCachedMetrics();

            System.Diagnostics.Debug.WriteLine($"[Health] Calculated health score: {metrics.HealthScore}/100");

            return metrics;
        }

        /// <summary>
        /// Calculate overall health score (0-100).
        /// </summary>
        public int CalculateHealthScore(LearningHealthMetrics metrics, LearningStatistics stats)
        {
            int score = 0;

            // Pattern quality (0-30 points)
            if (metrics.TotalPatterns > 0)
            {
                double highRatio = (double)metrics.HighConfidencePatterns / metrics.TotalPatterns;
                score += (int)(highRatio * 30);
            }

            // Acceptance rate (0-25 points)
            score += (int)(metrics.OverallAcceptanceRate * 25);

            // Freshness (0-20 points)
            if (metrics.TotalPatterns > 0)
            {
                double freshRatio = 1.0 - ((double)metrics.StalePatterns / metrics.TotalPatterns);
                score += (int)(freshRatio * 20);
            }
            else
            {
                score += 20;  // No patterns = fresh start
            }

            // Training volume (0-15 points)
            // More estimates = more reliable patterns
            int estimateScore = Math.Min(15, stats.EstimatesImported / 5);
            score += estimateScore;

            // Pattern coverage (0-10 points)
            // Having patterns for different vehicle types
            int vehicleTypes = metrics.PatternsByVehicleType.Count(kvp => kvp.Value > 0);
            score += Math.Min(10, vehicleTypes * 3);

            // Penalties
            // Rejection rate penalty (up to -15)
            if (metrics.OverallRejectionRate > 0.3)
            {
                score -= (int)((metrics.OverallRejectionRate - 0.3) * 30);
            }

            // Conflict penalty (up to -10)
            if (metrics.ConflictingPatternsCount > 5)
            {
                score -= Math.Min(10, metrics.ConflictingPatternsCount);
            }

            return Math.Max(0, Math.Min(100, score));
        }

        /// <summary>
        /// Get the current health score (uses cache).
        /// </summary>
        public int GetLearningHealthScore()
        {
            return CalculateHealthMetrics().HealthScore;
        }

        /// <summary>
        /// Quick check if system is healthy (score >= 60).
        /// </summary>
        public bool IsSystemHealthy()
        {
            return GetLearningHealthScore() >= 60;
        }

        /// <summary>
        /// Get health grade based on score.
        /// </summary>
        public string GetHealthGrade()
        {
            var score = GetLearningHealthScore();
            return score switch
            {
                >= 90 => "Excellent",
                >= 75 => "Good",
                >= 60 => "Fair",
                >= 40 => "Needs Attention",
                _ => "Critical"
            };
        }

        /// <summary>
        /// Get a color indicator for health status.
        /// </summary>
        public string GetHealthColor()
        {
            var score = GetLearningHealthScore();
            return score switch
            {
                >= 75 => "#4CAF50",  // Green
                >= 60 => "#8BC34A",  // Light green
                >= 40 => "#FFC107",  // Yellow
                >= 20 => "#FF9800",  // Orange
                _ => "#F44336"       // Red
            };
        }

        #endregion

        #region Recommendations

        /// <summary>
        /// Generate actionable recommendations to improve health.
        /// </summary>
        public List<string> GenerateRecommendations(LearningHealthMetrics metrics, LearningStatistics stats)
        {
            var recommendations = new List<string>();

            // Bootstrap mode
            if (stats.EstimatesImported < 20)
            {
                int remaining = 20 - stats.EstimatesImported;
                recommendations.Add($"Bootstrap mode active. Import {remaining} more estimates to enable quality checks.");
            }

            // Low pattern count
            if (metrics.TotalPatterns < 10)
            {
                recommendations.Add("Import more estimates to build pattern library. Target: 50+ patterns for good coverage.");
            }

            // High rejection rate
            if (metrics.OverallRejectionRate > 0.3)
            {
                recommendations.Add($"High rejection rate ({metrics.OverallRejectionRate:P0}). Review top rejected patterns and consider removing or retraining them.");
            }

            // Stale patterns
            if (metrics.StalePatterns > 5)
            {
                recommendations.Add($"{metrics.StalePatterns} patterns are stale (180+ days old). Import fresh estimates to refresh them.");
            }

            // Low confidence patterns
            if (metrics.LowConfidencePatterns > metrics.TotalPatterns * 0.3)
            {
                recommendations.Add($"{metrics.LowConfidencePatterns} patterns have low confidence. Add more training examples for these patterns.");
            }

            // Conflicts
            if (metrics.ConflictingPatternsCount > 0)
            {
                recommendations.Add($"{metrics.ConflictingPatternsCount} pattern conflicts detected. Review and resolve in Pattern Explorer.");
            }

            // Top rejected patterns
            if (metrics.TopRejectedPatterns.Count > 0)
            {
                var topRejected = metrics.TopRejectedPatterns.Take(3);
                recommendations.Add($"Most rejected patterns: {string.Join(", ", topRejected)}. Consider reviewing or removing.");
            }

            // Vehicle type coverage
            var emptyVehicleTypes = metrics.PatternsByVehicleType
                .Where(kvp => kvp.Value == 0 && kvp.Key != "unknown")
                .Select(kvp => kvp.Key)
                .ToList();

            if (emptyVehicleTypes.Count > 0)
            {
                recommendations.Add($"No patterns for: {string.Join(", ", emptyVehicleTypes)}. Import estimates from these vehicle types.");
            }

            // Success message if healthy
            if (recommendations.Count == 0 && metrics.HealthScore >= 75)
            {
                recommendations.Add("Learning system is healthy! Keep importing quality estimates to maintain performance.");
            }

            return recommendations;
        }

        /// <summary>
        /// Get the current recommendations.
        /// </summary>
        public List<string> GetHealthRecommendations()
        {
            return CalculateHealthMetrics().Recommendations;
        }

        #endregion

        #region Update & Refresh

        /// <summary>
        /// Force recalculation of health metrics.
        /// </summary>
        public void RefreshHealthMetrics()
        {
            _cachedMetrics = null;
            _lastCalculation = DateTime.MinValue;
            CalculateHealthMetrics();
        }

        /// <summary>
        /// Update health metrics after training.
        /// Called by learning service after importing estimates.
        /// </summary>
        public void OnTrainingCompleted(int patternsLearned, int estimatesImported)
        {
            // Invalidate cache to force recalculation
            _cachedMetrics = null;
            System.Diagnostics.Debug.WriteLine($"[Health] Training completed: {patternsLearned} patterns, {estimatesImported} estimates");
        }

        /// <summary>
        /// Update health metrics after feedback recorded.
        /// </summary>
        public void OnFeedbackRecorded()
        {
            // Could throttle this to avoid too many recalculations
            // For now, just invalidate cache
            if (_cachedMetrics != null && DateTime.Now - _lastCalculation > TimeSpan.FromSeconds(30))
            {
                _cachedMetrics = null;
            }
        }

        #endregion

        #region Alerts

        /// <summary>
        /// Get any critical alerts that need immediate attention.
        /// </summary>
        public List<HealthAlert> GetCriticalAlerts()
        {
            var alerts = new List<HealthAlert>();
            var metrics = CalculateHealthMetrics();

            // Critical rejection rate
            if (metrics.OverallRejectionRate > 0.5)
            {
                alerts.Add(new HealthAlert
                {
                    Severity = AlertSeverity.Critical,
                    Title = "Very High Rejection Rate",
                    Message = $"Over 50% of suggestions are being rejected. Learning system may be producing poor results.",
                    Action = "Review and retrain patterns"
                });
            }

            // Critical health score
            if (metrics.HealthScore < 30)
            {
                alerts.Add(new HealthAlert
                {
                    Severity = AlertSeverity.Critical,
                    Title = "Learning System Health Critical",
                    Message = $"Health score is {metrics.HealthScore}/100. System needs immediate attention.",
                    Action = "Import quality estimates and review patterns"
                });
            }

            // Too many stale patterns
            if (metrics.StalePatterns > metrics.TotalPatterns * 0.5 && metrics.TotalPatterns > 10)
            {
                alerts.Add(new HealthAlert
                {
                    Severity = AlertSeverity.Warning,
                    Title = "Many Stale Patterns",
                    Message = $"Over half of patterns are stale. Accuracy may be degraded.",
                    Action = "Import fresh estimates to update patterns"
                });
            }

            return alerts;
        }

        /// <summary>
        /// Check if there are any critical alerts.
        /// </summary>
        public bool HasCriticalAlerts()
        {
            return GetCriticalAlerts().Any(a => a.Severity == AlertSeverity.Critical);
        }

        #endregion

        #region Summary

        /// <summary>
        /// Get a brief summary of learning system status.
        /// </summary>
        public HealthSummary GetSummary()
        {
            var metrics = CalculateHealthMetrics();
            var stats = _learningService.GetStatistics();

            return new HealthSummary
            {
                HealthScore = metrics.HealthScore,
                HealthGrade = GetHealthGrade(),
                HealthColor = GetHealthColor(),
                TotalPatterns = metrics.TotalPatterns,
                EstimatesImported = stats.EstimatesImported,
                AcceptanceRate = metrics.OverallAcceptanceRate,
                IsBootstrapMode = stats.EstimatesImported < 20,
                BootstrapProgress = Math.Min(stats.EstimatesImported, 20),
                HasCriticalAlerts = HasCriticalAlerts(),
                TopRecommendation = metrics.Recommendations.FirstOrDefault() ?? "System healthy"
            };
        }

        #endregion

        #region Persistence

        private void SaveCachedMetrics()
        {
            try
            {
                if (_cachedMetrics != null)
                {
                    var json = JsonSerializer.Serialize(_cachedMetrics, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(_healthCachePath, json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Health] Error saving cache: {ex.Message}");
            }
        }

        private void LoadCachedMetrics()
        {
            try
            {
                if (File.Exists(_healthCachePath))
                {
                    var json = File.ReadAllText(_healthCachePath);
                    _cachedMetrics = JsonSerializer.Deserialize<LearningHealthMetrics>(json);

                    if (_cachedMetrics != null)
                    {
                        _lastCalculation = _cachedMetrics.LastCalculated;

                        // If cache is old, invalidate it
                        if (DateTime.Now - _lastCalculation > TimeSpan.FromHours(1))
                        {
                            _cachedMetrics = null;
                            _lastCalculation = DateTime.MinValue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Health] Error loading cache: {ex.Message}");
            }
        }

        #endregion
    }

    #region Alert & Summary Classes

    /// <summary>
    /// Health alert requiring attention.
    /// </summary>
    public class HealthAlert
    {
        public AlertSeverity Severity { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
    }

    /// <summary>
    /// Alert severity levels.
    /// </summary>
    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }

    /// <summary>
    /// Brief health summary for UI display.
    /// </summary>
    public class HealthSummary
    {
        public int HealthScore { get; set; }
        public string HealthGrade { get; set; } = "";
        public string HealthColor { get; set; } = "";
        public int TotalPatterns { get; set; }
        public int EstimatesImported { get; set; }
        public double AcceptanceRate { get; set; }
        public bool IsBootstrapMode { get; set; }
        public int BootstrapProgress { get; set; }
        public bool HasCriticalAlerts { get; set; }
        public string TopRecommendation { get; set; } = "";
    }

    #endregion
}
