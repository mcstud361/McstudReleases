#nullable enable
using System;
using System.Collections.Generic;

namespace McstudDesktop.Models
{
    public class SOPBaselineItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "";
        public string Section { get; set; } = "";        // "Electrical", "Vehicle Diagnostics", "Miscellaneous"
        public string Description { get; set; } = "";
        public string WhyNeeded { get; set; } = "";
        public decimal EstimatedCost { get; set; }
        public decimal LaborHours { get; set; }
        public string Severity { get; set; } = "Medium";
        public bool IsEnabled { get; set; } = true;
        public bool IsCustom { get; set; }
    }

    public class SOPBaselineSettings
    {
        public List<SOPBaselineItem> Items { get; set; } = new();
        public int Version { get; set; } = 1;
        public DateTime LastModified { get; set; } = DateTime.Now;
    }
}
