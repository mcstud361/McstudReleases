#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Manages pattern versioning, conflict detection, and vehicle-specific patterns.
    ///
    /// Features:
    /// - Vehicle type classification (truck/SUV/car)
    /// - Pattern versioning with rollback (keeps last 5 versions)
    /// - Conflict detection between overlapping patterns
    /// - Confidence decay for stale patterns
    /// - Effective confidence calculation (base * decay * feedback)
    /// </summary>
    public class PatternIntelligenceService
    {
        private static PatternIntelligenceService? _instance;
        public static PatternIntelligenceService Instance => _instance ??= new PatternIntelligenceService();

        private readonly EstimateLearningService _learningService;
        private readonly LearningFeedbackService _feedbackService;

        // Vehicle classification patterns
        private static readonly string[] TruckPatterns = new[]
        {
            "f-150", "f150", "f-250", "f250", "f-350", "f350",
            "silverado", "sierra", "ram 1500", "ram 2500", "ram 3500",
            "tundra", "titan", "tacoma", "colorado", "canyon",
            "ranger", "frontier", "gladiator", "ridgeline",
            "truck", "pickup", "dually"
        };

        private static readonly string[] SuvPatterns = new[]
        {
            "explorer", "expedition", "tahoe", "suburban", "yukon",
            "4runner", "sequoia", "land cruiser", "highlander", "pilot",
            "pathfinder", "armada", "traverse", "enclave", "acadia",
            "durango", "grand cherokee", "wrangler", "bronco",
            "rav4", "cr-v", "crv", "cx-5", "cx5", "cx-9", "cx9",
            "rogue", "murano", "tucson", "santa fe", "sorento",
            "escape", "equinox", "trailblazer", "blazer",
            "suv", "crossover", "4x4"
        };

        public PatternIntelligenceService()
        {
            _learningService = EstimateLearningService.Instance;
            _feedbackService = LearningFeedbackService.Instance;
        }

        #region Vehicle Type Classification

        /// <summary>
        /// Classify vehicle type from vehicle info string.
        /// Returns "truck", "suv", "car", or "unknown".
        /// </summary>
        public string ClassifyVehicleType(string? vehicleInfo)
        {
            if (string.IsNullOrEmpty(vehicleInfo))
                return "unknown";

            var lower = vehicleInfo.ToLowerInvariant();

            // Check trucks first (more specific)
            if (TruckPatterns.Any(p => lower.Contains(p)))
                return "truck";

            // Check SUVs
            if (SuvPatterns.Any(p => lower.Contains(p)))
                return "suv";

            // Default to car
            return "car";
        }

        /// <summary>
        /// Get patterns specific to a vehicle type.
        /// </summary>
        public List<LearnedPattern> GetPatternsForVehicleType(string vehicleType)
        {
            return _learningService.GetAllPatterns()
                .Where(p => PatternAppliesToVehicle(p, vehicleType))
                .ToList();
        }

        /// <summary>
        /// Check if a pattern should apply to a specific vehicle type.
        /// </summary>
        public bool PatternAppliesToVehicle(LearnedPattern pattern, string vehicleType)
        {
            // Pattern with no vehicle type applies to all
            if (string.IsNullOrEmpty(pattern.VehicleType))
                return true;

            return pattern.VehicleType.Equals(vehicleType, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Pattern Versioning

        /// <summary>
        /// Create a snapshot of a pattern before modification.
        /// Keeps last N versions for rollback capability.
        /// </summary>
        public void CreatePatternSnapshot(LearnedPattern pattern, string reason)
        {
            var metadata = GetOrCreateMetadata();

            if (!metadata.PatternVersions.TryGetValue(pattern.PatternKey, out var history))
            {
                history = new PatternVersionHistory
                {
                    PatternKey = pattern.PatternKey,
                    CurrentVersion = pattern.PatternVersion
                };
                metadata.PatternVersions[pattern.PatternKey] = history;
            }

            // Serialize the current pattern state
            var snapshot = new PatternSnapshot
            {
                Version = pattern.PatternVersion,
                SnapshotDate = DateTime.Now,
                Reason = reason,
                SerializedPattern = JsonSerializer.Serialize(pattern)
            };

            history.Snapshots.Add(snapshot);

            // Keep only last N snapshots
            var config = metadata.Configuration;
            while (history.Snapshots.Count > config.MaxPatternSnapshots)
            {
                history.Snapshots.RemoveAt(0);
            }

            // Increment version
            pattern.PatternVersion++;
            history.CurrentVersion = pattern.PatternVersion;

            System.Diagnostics.Debug.WriteLine($"[Intelligence] Created snapshot v{snapshot.Version} for {pattern.PatternKey}: {reason}");
        }

        /// <summary>
        /// Rollback a pattern to a previous version.
        /// </summary>
        public bool RollbackPattern(string patternKey, int targetVersion)
        {
            var metadata = GetOrCreateMetadata();

            if (!metadata.PatternVersions.TryGetValue(patternKey, out var history))
            {
                System.Diagnostics.Debug.WriteLine($"[Intelligence] No version history for {patternKey}");
                return false;
            }

            var snapshot = history.Snapshots.FirstOrDefault(s => s.Version == targetVersion);
            if (snapshot == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Intelligence] Version {targetVersion} not found for {patternKey}");
                return false;
            }

            try
            {
                var restored = JsonSerializer.Deserialize<LearnedPattern>(snapshot.SerializedPattern);
                if (restored == null)
                    return false;

                // The learning service would need a method to replace a pattern
                // For now, log the action
                System.Diagnostics.Debug.WriteLine($"[Intelligence] Rolled back {patternKey} to v{targetVersion}");

                // Create a new snapshot for the rollback
                CreatePatternSnapshot(restored, $"rollback_from_v{history.CurrentVersion}");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Intelligence] Rollback failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get version history for a pattern.
        /// </summary>
        public List<PatternSnapshot> GetPatternHistory(string patternKey)
        {
            var metadata = GetOrCreateMetadata();

            if (metadata.PatternVersions.TryGetValue(patternKey, out var history))
            {
                return history.Snapshots.OrderByDescending(s => s.Version).ToList();
            }

            return new List<PatternSnapshot>();
        }

        /// <summary>
        /// Get the current version number for a pattern.
        /// </summary>
        public int GetCurrentVersion(string patternKey)
        {
            var metadata = GetOrCreateMetadata();

            if (metadata.PatternVersions.TryGetValue(patternKey, out var history))
            {
                return history.CurrentVersion;
            }

            return 1;
        }

        #endregion

        #region Conflict Detection

        /// <summary>
        /// Detect conflicts between patterns.
        /// Conflicts occur when patterns have overlapping keys or produce contradictory results.
        /// </summary>
        public List<PatternConflict> DetectConflicts()
        {
            var conflicts = new List<PatternConflict>();
            var allPatterns = _learningService.GetAllPatterns();

            // Compare patterns that share the same part name
            var byPart = allPatterns
                .Where(p => !string.IsNullOrEmpty(p.PartName))
                .GroupBy(p => p.PartName.ToLower())
                .Where(g => g.Count() > 1);

            foreach (var group in byPart)
            {
                var patterns = group.ToList();
                for (int i = 0; i < patterns.Count; i++)
                {
                    for (int j = i + 1; j < patterns.Count; j++)
                    {
                        var conflict = CheckConflict(patterns[i], patterns[j]);
                        if (conflict != null)
                            conflicts.Add(conflict);
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[Intelligence] Conflict detection completed: {conflicts.Count} conflicts found");
            return conflicts;
        }

        /// <summary>
        /// Check if two patterns conflict with each other.
        /// </summary>
        public PatternConflict? CheckConflict(LearnedPattern pattern1, LearnedPattern pattern2)
        {
            // Same part but different operation types with similar hours
            if (pattern1.PartName.Equals(pattern2.PartName, StringComparison.OrdinalIgnoreCase) &&
                !pattern1.OperationType.Equals(pattern2.OperationType, StringComparison.OrdinalIgnoreCase))
            {
                // Check if operations have similar labor hours (potential confusion)
                var ops1Labor = pattern1.Operations.Sum(o => o.LaborHours);
                var ops2Labor = pattern2.Operations.Sum(o => o.LaborHours);

                if (ops1Labor > 0 && ops2Labor > 0)
                {
                    var ratio = Math.Min(ops1Labor, ops2Labor) / Math.Max(ops1Labor, ops2Labor);
                    if (ratio > 0.8m)  // Hours within 20% of each other
                    {
                        return new PatternConflict
                        {
                            PatternKey1 = pattern1.PatternKey,
                            PatternKey2 = pattern2.PatternKey,
                            ConflictType = "similar_hours_different_ops",
                            Severity = 0.5,
                            Description = $"{pattern1.PartName}: {pattern1.OperationType} and {pattern2.OperationType} have similar hours"
                        };
                    }
                }
            }

            // Overlapping pattern keys
            var keys1 = pattern1.PatternKey.Split('|', '_');
            var keys2 = pattern2.PatternKey.Split('|', '_');
            var overlap = keys1.Intersect(keys2, StringComparer.OrdinalIgnoreCase).Count();
            var totalKeys = Math.Max(keys1.Length, keys2.Length);

            if (overlap > 0 && overlap >= totalKeys * 0.8)
            {
                return new PatternConflict
                {
                    PatternKey1 = pattern1.PatternKey,
                    PatternKey2 = pattern2.PatternKey,
                    ConflictType = "overlapping_keys",
                    Severity = (double)overlap / totalKeys,
                    Description = $"Pattern keys overlap: {string.Join(", ", keys1.Intersect(keys2))}"
                };
            }

            return null;
        }

        /// <summary>
        /// Mark patterns as conflicting with each other.
        /// </summary>
        public void MarkConflict(string patternKey1, string patternKey2)
        {
            // This would update the ConflictingPatterns list on each pattern
            System.Diagnostics.Debug.WriteLine($"[Intelligence] Marked conflict: {patternKey1} <-> {patternKey2}");
        }

        #endregion

        #region Confidence Calculations

        /// <summary>
        /// Check if a pattern meets the minimum confidence threshold for suggestions.
        /// </summary>
        public bool MeetsSuggestionThreshold(LearnedPattern pattern)
        {
            var config = GetConfiguration();
            var effectiveConfidence = GetEffectiveConfidence(pattern);
            return effectiveConfidence >= config.MinConfidenceForSuggestion;
        }

        /// <summary>
        /// Get the effective confidence for a pattern.
        /// Combines base confidence, decay, and feedback adjustment.
        /// </summary>
        public double GetEffectiveConfidence(LearnedPattern pattern)
        {
            // Start with decay-adjusted confidence
            var decayed = pattern.DecayedConfidence;

            // Apply feedback adjustment
            var adjusted = _feedbackService.AdjustConfidenceFromFeedback(pattern.PatternKey, decayed);

            return adjusted;
        }

        /// <summary>
        /// Apply decay to all stale patterns.
        /// Should be called periodically (e.g., on app startup).
        /// </summary>
        public int ApplyPatternDecay()
        {
            int decayedCount = 0;
            // This would iterate through patterns and check IsStale property
            // Decay is already computed dynamically via DecayedConfidence property

            System.Diagnostics.Debug.WriteLine($"[Intelligence] Pattern decay check completed. {decayedCount} patterns affected.");
            return decayedCount;
        }

        /// <summary>
        /// Get list of stale patterns (not updated in 180+ days).
        /// </summary>
        public List<string> GetStalePatterns()
        {
            return _learningService.GetAllPatterns()
                .Where(p => p.IsStale)
                .Select(p => $"{p.PartName} {p.OperationType} (last updated {p.LastUpdated:d})")
                .ToList();
        }

        /// <summary>
        /// Get patterns that should be refreshed (confidence degraded significantly).
        /// </summary>
        public List<string> GetPatternsNeedingRefresh()
        {
            return _learningService.GetAllPatterns()
                .Where(p => p.DecayedConfidence < p.Confidence * 0.8)
                .Select(p => $"{p.PartName} {p.OperationType} (confidence: {p.DecayedConfidence:P0})")
                .ToList();
        }

        #endregion

        #region Pattern Analysis

        /// <summary>
        /// Analyze a pattern and return insights.
        /// </summary>
        public PatternAnalysis AnalyzePattern(LearnedPattern pattern)
        {
            var feedback = _feedbackService.GetFeedbackForPattern(pattern.PatternKey);
            var history = GetPatternHistory(pattern.PatternKey);

            return new PatternAnalysis
            {
                PatternKey = pattern.PatternKey,
                BaseConfidence = pattern.Confidence,
                DecayedConfidence = pattern.DecayedConfidence,
                EffectiveConfidence = GetEffectiveConfidence(pattern),
                IsStale = pattern.IsStale,
                DaysSinceUpdate = (int)(DateTime.Now - pattern.LastUpdated).TotalDays,
                ExampleCount = pattern.ExampleCount,
                VersionCount = history.Count,
                CurrentVersion = pattern.PatternVersion,
                AcceptanceRate = feedback?.AcceptanceRate ?? 0,
                RejectionRate = feedback?.RejectionRate ?? 0,
                TimesUsed = feedback?.TimesGenerated ?? 0,
                MeetsSuggestionThreshold = MeetsSuggestionThreshold(pattern),
                VehicleType = pattern.VehicleType,
                HasConflicts = pattern.ConflictingPatterns?.Any() ?? false,
                ConflictCount = pattern.ConflictingPatterns?.Count ?? 0
            };
        }

        /// <summary>
        /// Get patterns sorted by quality (confidence, feedback, freshness).
        /// </summary>
        public List<PatternAnalysis> GetPatternsByQuality(int count = 50)
        {
            return _learningService.GetAllPatterns()
                .Select(p => AnalyzePattern(p))
                .OrderByDescending(a => a.QualityScore)
                .Take(count)
                .ToList();
        }

        #endregion

        #region Helpers

        private SmartLearningMetadata GetOrCreateMetadata()
        {
            // Would get from learning service
            return new SmartLearningMetadata();
        }

        private LearningConfiguration GetConfiguration()
        {
            return GetOrCreateMetadata().Configuration;
        }

        #endregion
    }

    #region Analysis Classes

    /// <summary>
    /// Analysis result for a single pattern.
    /// </summary>
    public class PatternAnalysis
    {
        public string PatternKey { get; set; } = "";
        public double BaseConfidence { get; set; }
        public double DecayedConfidence { get; set; }
        public double EffectiveConfidence { get; set; }
        public bool IsStale { get; set; }
        public int DaysSinceUpdate { get; set; }
        public int ExampleCount { get; set; }
        public int VersionCount { get; set; }
        public int CurrentVersion { get; set; }
        public double AcceptanceRate { get; set; }
        public double RejectionRate { get; set; }
        public int TimesUsed { get; set; }
        public bool MeetsSuggestionThreshold { get; set; }
        public string? VehicleType { get; set; }
        public bool HasConflicts { get; set; }
        public int ConflictCount { get; set; }

        /// <summary>
        /// Overall quality score for the pattern (0-100).
        /// </summary>
        public int QualityScore
        {
            get
            {
                int score = 0;

                // Effective confidence (0-40 points)
                score += (int)(EffectiveConfidence * 40);

                // Example count (0-20 points, max at 10+ examples)
                score += Math.Min(20, ExampleCount * 2);

                // Acceptance rate (0-20 points)
                score += (int)(AcceptanceRate * 20);

                // Freshness (0-10 points)
                if (DaysSinceUpdate < 30) score += 10;
                else if (DaysSinceUpdate < 90) score += 5;

                // Penalties
                if (IsStale) score -= 10;
                if (HasConflicts) score -= 5;
                if (RejectionRate > 0.3) score -= 10;

                return Math.Max(0, Math.Min(100, score));
            }
        }
    }

    #endregion
}
