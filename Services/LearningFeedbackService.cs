#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Tracks user acceptance/rejection of generated operations and suggestions.
    /// Uses feedback to adjust pattern confidence scores over time.
    ///
    /// Feedback-Adjusted Confidence:
    /// - 80%+ acceptance: boost confidence up to 20%
    /// - 50-80% acceptance: no change
    /// - 20-50% acceptance: reduce up to 30%
    /// - less than 20% acceptance: reduce up to 50%
    /// </summary>
    public class LearningFeedbackService
    {
        private static LearningFeedbackService? _instance;
        public static LearningFeedbackService Instance => _instance ??= new LearningFeedbackService();

        private readonly string _feedbackPath;
        private Dictionary<string, PatternFeedback> _feedbacks = new();
        private const int MaxRecentEvents = 50;

        public LearningFeedbackService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(appDataPath);
            _feedbackPath = Path.Combine(appDataPath, "learning_feedback.json");

            LoadFeedback();
        }

        #region Recording Feedback

        /// <summary>
        /// Record that a pattern was used to generate suggestions.
        /// </summary>
        public void RecordOperationGenerated(string patternKey, string? context = null)
        {
            var feedback = GetOrCreateFeedback(patternKey);
            feedback.TimesGenerated++;
            feedback.LastUsed = DateTime.Now;

            AddEvent(feedback, FeedbackAction.Generated, context);
            SaveFeedback();

            System.Diagnostics.Debug.WriteLine($"[Feedback] Generated: {patternKey} (total: {feedback.TimesGenerated})");
        }

        /// <summary>
        /// Record that a user accepted a generated operation.
        /// </summary>
        public void RecordOperationAccepted(string patternKey, string? context = null)
        {
            var feedback = GetOrCreateFeedback(patternKey);
            feedback.TimesAccepted++;
            feedback.LastUsed = DateTime.Now;

            AddEvent(feedback, FeedbackAction.Accepted, context);
            SaveFeedback();

            System.Diagnostics.Debug.WriteLine($"[Feedback] Accepted: {patternKey} (rate: {feedback.AcceptanceRate:P0})");
        }

        /// <summary>
        /// Record that a user rejected a generated operation.
        /// </summary>
        public void RecordOperationRejected(string patternKey, string? context = null)
        {
            var feedback = GetOrCreateFeedback(patternKey);
            feedback.TimesRejected++;
            feedback.LastUsed = DateTime.Now;

            AddEvent(feedback, FeedbackAction.Rejected, context);
            SaveFeedback();

            System.Diagnostics.Debug.WriteLine($"[Feedback] Rejected: {patternKey} (rate: {feedback.RejectionRate:P0})");
        }

        /// <summary>
        /// Record that a user accepted but modified a generated operation.
        /// </summary>
        public void RecordOperationModified(string patternKey, string? modification = null, string? context = null)
        {
            var feedback = GetOrCreateFeedback(patternKey);
            feedback.TimesModified++;
            feedback.LastUsed = DateTime.Now;

            var evt = new FeedbackEvent
            {
                Timestamp = DateTime.Now,
                Action = FeedbackAction.Modified,
                Context = context,
                ModificationDetails = modification
            };
            AddEvent(feedback, evt);
            SaveFeedback();

            System.Diagnostics.Debug.WriteLine($"[Feedback] Modified: {patternKey} - {modification}");
        }

        /// <summary>
        /// Record that a smart suggestion was accepted.
        /// </summary>
        public void RecordSuggestionAccepted(string suggestionId, string? context = null)
        {
            RecordOperationAccepted($"suggestion:{suggestionId}", context);
        }

        /// <summary>
        /// Record that a smart suggestion was rejected.
        /// </summary>
        public void RecordSuggestionRejected(string suggestionId, string? context = null)
        {
            RecordOperationRejected($"suggestion:{suggestionId}", context);
        }

        /// <summary>
        /// Record batch feedback for multiple patterns at once.
        /// </summary>
        public void RecordBatchAccepted(IEnumerable<string> patternKeys, string? context = null)
        {
            foreach (var key in patternKeys)
            {
                RecordOperationAccepted(key, context);
            }
        }

        /// <summary>
        /// Record batch rejection for multiple patterns at once.
        /// </summary>
        public void RecordBatchRejected(IEnumerable<string> patternKeys, string? context = null)
        {
            foreach (var key in patternKeys)
            {
                RecordOperationRejected(key, context);
            }
        }

        #endregion

        #region Confidence Adjustment

        /// <summary>
        /// Adjust a pattern's confidence based on user feedback.
        /// Called when generating suggestions to get effective confidence.
        /// </summary>
        public double AdjustConfidenceFromFeedback(string patternKey, double baseConfidence)
        {
            if (!_feedbacks.TryGetValue(patternKey, out var feedback))
                return baseConfidence;

            // Need at least 5 data points for meaningful adjustment
            if (feedback.TimesGenerated < 5)
                return baseConfidence;

            double acceptanceRate = feedback.AcceptanceRate;

            // Sliding scale adjustment:
            // 80%+ acceptance: boost confidence by up to 20%
            // 50-80% acceptance: no change
            // 20-50% acceptance: reduce confidence by up to 30%
            // <20% acceptance: reduce confidence by up to 50%

            double adjustment;
            if (acceptanceRate >= 0.8)
            {
                // 0.8 → 1.0, 1.0 → 1.2
                adjustment = 1.0 + (acceptanceRate - 0.8) * 1.0;
            }
            else if (acceptanceRate >= 0.5)
            {
                // No change
                adjustment = 1.0;
            }
            else if (acceptanceRate >= 0.2)
            {
                // 0.2 → 0.7, 0.5 → 1.0
                adjustment = 0.7 + (acceptanceRate - 0.2) * 1.0;
            }
            else
            {
                // 0 → 0.5, 0.2 → 0.7
                adjustment = 0.5 + acceptanceRate * 1.0;
            }

            var adjusted = Math.Max(0.1, Math.Min(1.0, baseConfidence * adjustment));

            System.Diagnostics.Debug.WriteLine($"[Feedback] Adjusted confidence for {patternKey}: " +
                $"{baseConfidence:F2} → {adjusted:F2} (acceptance: {acceptanceRate:P0})");

            return adjusted;
        }

        /// <summary>
        /// Get the effective confidence for a pattern (base * feedback adjustment * decay).
        /// </summary>
        public double GetEffectiveConfidence(string patternKey, double baseConfidence, double decayedConfidence)
        {
            // Start with decayed confidence (already accounts for staleness)
            var feedbackAdjusted = AdjustConfidenceFromFeedback(patternKey, decayedConfidence);
            return feedbackAdjusted;
        }

        #endregion

        #region Analytics

        /// <summary>
        /// Get feedback record for a specific pattern.
        /// </summary>
        public PatternFeedback? GetFeedbackForPattern(string patternKey)
        {
            return _feedbacks.TryGetValue(patternKey, out var feedback) ? feedback : null;
        }

        /// <summary>
        /// Get patterns with highest rejection rates.
        /// These patterns may need review or removal.
        /// </summary>
        public List<PatternFeedback> GetMostRejectedPatterns(int count = 10)
        {
            return _feedbacks.Values
                .Where(f => f.TimesGenerated >= 5)  // Minimum samples
                .OrderByDescending(f => f.RejectionRate)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get patterns with highest acceptance rates.
        /// These are the most reliable patterns.
        /// </summary>
        public List<PatternFeedback> GetMostAcceptedPatterns(int count = 10)
        {
            return _feedbacks.Values
                .Where(f => f.TimesGenerated >= 5)
                .OrderByDescending(f => f.AcceptanceRate)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get patterns that are improving (acceptance trending up).
        /// </summary>
        public List<string> GetImprovingPatterns(int count = 10)
        {
            return _feedbacks.Values
                .Where(f => f.RecentEvents.Count >= 10)
                .Where(f => CalculateTrend(f) > 0.1)  // Positive trend
                .OrderByDescending(f => CalculateTrend(f))
                .Take(count)
                .Select(f => f.PatternKey)
                .ToList();
        }

        /// <summary>
        /// Get patterns that are declining (acceptance trending down).
        /// </summary>
        public List<string> GetDecliningPatterns(int count = 10)
        {
            return _feedbacks.Values
                .Where(f => f.RecentEvents.Count >= 10)
                .Where(f => CalculateTrend(f) < -0.1)  // Negative trend
                .OrderBy(f => CalculateTrend(f))
                .Take(count)
                .Select(f => f.PatternKey)
                .ToList();
        }

        /// <summary>
        /// Generate effectiveness report for a time period.
        /// </summary>
        public LearningEffectivenessReport GetEffectivenessReport(DateTime periodStart, DateTime periodEnd)
        {
            var report = new LearningEffectivenessReport
            {
                PeriodStart = periodStart,
                PeriodEnd = periodEnd
            };

            var eventsInPeriod = _feedbacks.Values
                .SelectMany(f => f.RecentEvents)
                .Where(e => e.Timestamp >= periodStart && e.Timestamp <= periodEnd)
                .ToList();

            report.TotalSuggestionsMade = eventsInPeriod.Count(e => e.Action == FeedbackAction.Generated);
            report.TotalAccepted = eventsInPeriod.Count(e => e.Action == FeedbackAction.Accepted);
            report.TotalRejected = eventsInPeriod.Count(e => e.Action == FeedbackAction.Rejected);
            report.TotalModified = eventsInPeriod.Count(e => e.Action == FeedbackAction.Modified);

            if (report.TotalSuggestionsMade > 0)
            {
                report.OverallAcceptanceRate = (double)report.TotalAccepted / report.TotalSuggestionsMade;
            }

            // Compare to previous period
            var previousStart = periodStart - (periodEnd - periodStart);
            var previousEvents = _feedbacks.Values
                .SelectMany(f => f.RecentEvents)
                .Where(e => e.Timestamp >= previousStart && e.Timestamp < periodStart)
                .ToList();

            int previousSuggestions = previousEvents.Count(e => e.Action == FeedbackAction.Generated);
            int previousAccepted = previousEvents.Count(e => e.Action == FeedbackAction.Accepted);

            if (previousSuggestions > 0)
            {
                double previousRate = (double)previousAccepted / previousSuggestions;
                report.TrendVsPreviousPeriod = report.OverallAcceptanceRate - previousRate;
            }

            report.ImprovingPatterns = GetImprovingPatterns(5);
            report.DecliningPatterns = GetDecliningPatterns(5);

            return report;
        }

        /// <summary>
        /// Get overall acceptance rate across all patterns.
        /// </summary>
        public double GetOverallAcceptanceRate()
        {
            var totalGenerated = _feedbacks.Values.Sum(f => f.TimesGenerated);
            var totalAccepted = _feedbacks.Values.Sum(f => f.TimesAccepted);

            return totalGenerated > 0 ? (double)totalAccepted / totalGenerated : 0;
        }

        /// <summary>
        /// Get overall rejection rate across all patterns.
        /// </summary>
        public double GetOverallRejectionRate()
        {
            var totalGenerated = _feedbacks.Values.Sum(f => f.TimesGenerated);
            var totalRejected = _feedbacks.Values.Sum(f => f.TimesRejected);

            return totalGenerated > 0 ? (double)totalRejected / totalGenerated : 0;
        }

        /// <summary>
        /// Get summary statistics for the feedback system.
        /// </summary>
        public FeedbackSummary GetSummary()
        {
            return new FeedbackSummary
            {
                TotalPatterns = _feedbacks.Count,
                TotalSuggestions = _feedbacks.Values.Sum(f => f.TimesGenerated),
                TotalAccepted = _feedbacks.Values.Sum(f => f.TimesAccepted),
                TotalRejected = _feedbacks.Values.Sum(f => f.TimesRejected),
                TotalModified = _feedbacks.Values.Sum(f => f.TimesModified),
                OverallAcceptanceRate = GetOverallAcceptanceRate(),
                OverallRejectionRate = GetOverallRejectionRate(),
                PatternsWithFeedback = _feedbacks.Values.Count(f => f.TimesGenerated >= 5),
                HighPerformingPatterns = _feedbacks.Values.Count(f => f.AcceptanceRate >= 0.8 && f.TimesGenerated >= 5),
                LowPerformingPatterns = _feedbacks.Values.Count(f => f.AcceptanceRate < 0.3 && f.TimesGenerated >= 5)
            };
        }

        #endregion

        #region Helpers

        private PatternFeedback GetOrCreateFeedback(string patternKey)
        {
            if (!_feedbacks.TryGetValue(patternKey, out var feedback))
            {
                feedback = new PatternFeedback
                {
                    PatternKey = patternKey,
                    FirstUsed = DateTime.Now
                };
                _feedbacks[patternKey] = feedback;
            }
            return feedback;
        }

        private void AddEvent(PatternFeedback feedback, FeedbackAction action, string? context)
        {
            var evt = new FeedbackEvent
            {
                Timestamp = DateTime.Now,
                Action = action,
                Context = context
            };
            AddEvent(feedback, evt);
        }

        private void AddEvent(PatternFeedback feedback, FeedbackEvent evt)
        {
            feedback.RecentEvents.Add(evt);

            // Keep only last N events
            while (feedback.RecentEvents.Count > MaxRecentEvents)
            {
                feedback.RecentEvents.RemoveAt(0);
            }
        }

        /// <summary>
        /// Calculate acceptance trend from recent events.
        /// Positive = improving, negative = declining.
        /// </summary>
        private double CalculateTrend(PatternFeedback feedback)
        {
            var events = feedback.RecentEvents;
            if (events.Count < 10) return 0;

            // Split into halves
            int mid = events.Count / 2;
            var firstHalf = events.Take(mid).ToList();
            var secondHalf = events.Skip(mid).ToList();

            double firstRate = CalculateRateFromEvents(firstHalf);
            double secondRate = CalculateRateFromEvents(secondHalf);

            return secondRate - firstRate;
        }

        private double CalculateRateFromEvents(List<FeedbackEvent> events)
        {
            int generated = events.Count(e => e.Action == FeedbackAction.Generated);
            int accepted = events.Count(e => e.Action == FeedbackAction.Accepted);
            return generated > 0 ? (double)accepted / generated : 0;
        }

        #endregion

        #region Baseline Merge

        /// <summary>
        /// Merge baseline feedback data. User's personal feedback always takes precedence.
        /// Only adds baseline entries for patterns the user has no feedback on.
        /// </summary>
        public void MergeBaseline(Dictionary<string, PatternFeedback> baseline)
        {
            if (baseline == null) return;

            int added = 0;
            foreach (var (key, baselineFeedback) in baseline)
            {
                if (!_feedbacks.ContainsKey(key))
                {
                    _feedbacks[key] = baselineFeedback;
                    added++;
                }
                // If user has feedback for this key, keep user's data (personal experience takes precedence)
            }

            if (added > 0)
            {
                SaveFeedback();
                System.Diagnostics.Debug.WriteLine($"[Feedback] Merged baseline: added {added} feedback entries");
            }
        }

        /// <summary>
        /// Get all feedback data (for baseline export)
        /// </summary>
        public Dictionary<string, PatternFeedback> GetAllFeedback() => _feedbacks;

        #endregion

        #region Persistence

        public void SaveFeedback()
        {
            try
            {
                var json = JsonSerializer.Serialize(_feedbacks, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_feedbackPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Feedback] Error saving: {ex.Message}");
            }
        }

        public void LoadFeedback()
        {
            try
            {
                if (File.Exists(_feedbackPath))
                {
                    var json = File.ReadAllText(_feedbackPath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, PatternFeedback>>(json);
                    if (loaded != null)
                    {
                        _feedbacks = loaded;
                        System.Diagnostics.Debug.WriteLine($"[Feedback] Loaded {_feedbacks.Count} pattern feedbacks");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Feedback] Error loading: {ex.Message}");
                _feedbacks = new Dictionary<string, PatternFeedback>();
            }
        }

        /// <summary>
        /// Clear all feedback data (for testing/reset).
        /// </summary>
        public void ClearAllFeedback()
        {
            _feedbacks.Clear();
            SaveFeedback();
            System.Diagnostics.Debug.WriteLine("[Feedback] Cleared all feedback data");
        }

        #endregion
    }

    #region Summary Classes

    /// <summary>
    /// Summary of feedback statistics.
    /// </summary>
    public class FeedbackSummary
    {
        public int TotalPatterns { get; set; }
        public int TotalSuggestions { get; set; }
        public int TotalAccepted { get; set; }
        public int TotalRejected { get; set; }
        public int TotalModified { get; set; }
        public double OverallAcceptanceRate { get; set; }
        public double OverallRejectionRate { get; set; }
        public int PatternsWithFeedback { get; set; }
        public int HighPerformingPatterns { get; set; }
        public int LowPerformingPatterns { get; set; }
    }

    #endregion
}
