#nullable enable
using System;
using System.Collections.Generic;

namespace McstudDesktop.Models
{
    public enum CoachingSeverity
    {
        Critical,
        High,
        Medium,
        Low
    }

    public class CoachingSuggestion
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string WhyNeeded { get; set; } = "";
        public string? DegReference { get; set; }
        public string Category { get; set; } = "";
        public CoachingSeverity Severity { get; set; } = CoachingSeverity.Medium;
        public string TriggeredBy { get; set; } = "";
        public decimal EstimatedCost { get; set; }
        public decimal LaborHours { get; set; }
        public string Source { get; set; } = "";
        public bool IsDismissed { get; set; }
        public DateTime FirstSeen { get; set; } = DateTime.Now;
    }

    public class CoachingSnapshot
    {
        public List<CoachingSuggestion> Suggestions { get; set; } = new();
        public int TotalOperationsDetected { get; set; }
        public int Score { get; set; }
        public string Grade { get; set; } = "";
        public decimal PotentialRecovery { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
