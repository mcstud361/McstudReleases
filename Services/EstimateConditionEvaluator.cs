#nullable enable
using System;
using System.Collections.Generic;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Single source of truth for evaluating must-have conditions against estimate text.
    /// Shared by EstimateScoringService, ScreenMonitorPanel, and EstimateContextService.
    /// </summary>
    public static class EstimateConditionEvaluator
    {
        /// <summary>
        /// All known condition presets for the UI ComboBox.
        /// </summary>
        public static readonly string[] AllConditions = new[]
        {
            "always",
            "when refinish",
            "when welding",
            "when adas",
            "when ac",
            "when wheels",
            "when srs",
            "when structural",
            "when high voltage",
            "when body on frame",
            "when total loss",
            "when stolen recovery"
        };

        // Keyword triggers per condition, checked against the combined lowercase estimate text.
        private static readonly Dictionary<string, string[]> ConditionKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["when refinish"] = new[] { "refinish", "blend", "paint", "clear coat", "refn", "blnd", "color tint", "spray out" },
            ["when welding"] = new[] { "weld", "quarter panel replace", "roof panel replace", "rocker replace", "pillar replace", "quarter replace", "roof replace" },
            ["when adas"] = new[] { "camera", "radar", "sensor", "calibrat", "adas", "windshield" },
            ["when ac"] = new[] { "a/c", "ac compressor", "refrigerant", "condenser", "evaporator", "climate" },
            ["when wheels"] = new[] { "wheel r&i", "wheel r and i", "tire", "mount and balance", "lug", "torque wheel" },
            ["when srs"] = new[] { "airbag", "air bag", "seatbelt", "seat belt", "restraint", "srs" },
            ["when structural"] = new[] { "measurement", "frame", "structural" },
            ["when high voltage"] = new[] { "hybrid", "high voltage", "ev battery" },
            ["when body on frame"] = new[] { "body on frame", "bed side", "shipping crate", "cab to frame" },
            ["when total loss"] = new[] { "total loss" },
            ["when stolen recovery"] = new[] { "stolen", "recovery" },
        };

        /// <summary>
        /// Evaluate whether a must-have condition is triggered by the estimate text.
        /// </summary>
        /// <param name="condition">The condition string (e.g. "always", "when welding"). Null/empty defaults to true.</param>
        /// <param name="combinedTextLower">All estimate line text concatenated and lowercased.</param>
        /// <returns>True if the condition is met (must-have should be checked), false if it should be skipped.</returns>
        public static bool Evaluate(string? condition, string combinedTextLower)
        {
            if (string.IsNullOrWhiteSpace(condition) || condition == "always")
                return true;

            if (ConditionKeywords.TryGetValue(condition, out var keywords))
            {
                foreach (var kw in keywords)
                {
                    if (combinedTextLower.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            // Unknown condition — safe default: include it
            return true;
        }
    }
}
