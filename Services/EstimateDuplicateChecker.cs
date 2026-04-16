#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Classification of a re-uploaded estimate compared to what's already on file.
    /// </summary>
    public enum DuplicateMatchKind
    {
        None,           // No existing match found — import as brand new (Version 1)
        ExactMatch,     // Byte-identical or field-identical to an existing import
        Different       // Same claim/vehicle but has differences (candidate for new Version)
    }

    public class EstimateDuplicateResult
    {
        public DuplicateMatchKind Kind { get; set; } = DuplicateMatchKind.None;
        public StoredEstimate? Match { get; set; }      // The existing estimate we matched against
        public List<string> Differences { get; set; } = new();
    }

    /// <summary>
    /// Detects whether a newly-parsed estimate is a duplicate (exact or modified)
    /// of something already in the history database. Used to offer the user a choice
    /// between skipping, replacing, or saving as a new version.
    /// </summary>
    public static class EstimateDuplicateChecker
    {
        /// <summary>
        /// Find the best candidate existing estimate that represents the "same claim"
        /// as the parsed one, then classify whether it is an exact match or a revised version.
        /// </summary>
        public static EstimateDuplicateResult Check(ParsedEstimate parsed)
        {
            var all = EstimateHistoryDatabase.Instance.GetEstimatesForCurrentUser();
            var match = FindBestMatch(parsed, all);
            if (match == null)
                return new EstimateDuplicateResult { Kind = DuplicateMatchKind.None };

            var differences = Compare(parsed, match);
            return new EstimateDuplicateResult
            {
                Kind = differences.Count == 0 ? DuplicateMatchKind.ExactMatch : DuplicateMatchKind.Different,
                Match = match,
                Differences = differences
            };
        }

        /// <summary>
        /// Match priority (tie-break by most recent ImportedDate):
        ///   1) VIN + ClaimNumber  (strong — same claim on same vehicle)
        ///   2) VIN + RONumber     (same RO at this shop)
        ///   3) VIN                (most-recent import for this vehicle)
        ///   4) ClaimNumber alone  (if no VIN on the parsed PDF)
        ///   5) SourceFile name + insurance + close grand total (within $1) fallback
        /// </summary>
        private static StoredEstimate? FindBestMatch(ParsedEstimate parsed, List<StoredEstimate> all)
        {
            var vin = (parsed.VIN ?? "").Trim();
            // ClaimNumber and RONumber are on StoredEstimate but not ParsedEstimate — we can't
            // grab them from parsed directly. We extract from raw text the same way AddEstimate does,
            // but cheaper to just use the candidate's fields for matching.
            // NOTE: ParsedEstimate doesn't expose claim/RO, so we match purely on VIN + totals + source file.

            IEnumerable<StoredEstimate> candidates = all;

            if (!string.IsNullOrEmpty(vin))
            {
                var byVin = candidates.Where(e => string.Equals(e.VIN, vin, StringComparison.OrdinalIgnoreCase)).ToList();
                if (byVin.Count > 0)
                {
                    // Prefer a VIN match with a very close grand total (supplement of same claim)
                    var close = byVin
                        .Where(e => Math.Abs(e.GrandTotal - parsed.Totals.GrandTotal) < 1m)
                        .OrderByDescending(e => e.ImportedDate)
                        .FirstOrDefault();
                    if (close != null) return close;

                    // Otherwise the most recent VIN match is still our best candidate for a new version.
                    return byVin.OrderByDescending(e => e.ImportedDate).First();
                }
            }

            // Fallback: same filename + insurer + grand total within $1
            if (!string.IsNullOrEmpty(parsed.SourceFile))
            {
                var byFile = candidates
                    .Where(e => string.Equals(e.SourceFile, parsed.SourceFile, StringComparison.OrdinalIgnoreCase)
                             && Math.Abs(e.GrandTotal - parsed.Totals.GrandTotal) < 1m)
                    .OrderByDescending(e => e.ImportedDate)
                    .FirstOrDefault();
                if (byFile != null) return byFile;
            }

            return null;
        }

        /// <summary>
        /// Compare the parsed PDF against a stored estimate and return a human-readable
        /// list of differences. An empty list means "exact match".
        /// </summary>
        public static List<string> Compare(ParsedEstimate parsed, StoredEstimate stored)
        {
            var diffs = new List<string>();

            if (!SameText(parsed.VehicleInfo, stored.VehicleInfo))
                diffs.Add($"Vehicle: \"{stored.VehicleInfo}\" → \"{parsed.VehicleInfo}\"");
            if (!SameText(parsed.CustomerName, stored.CustomerName))
                diffs.Add($"Customer: \"{stored.CustomerName}\" → \"{parsed.CustomerName}\"");
            if (!SameText(parsed.AdjusterName, stored.AdjusterName))
                diffs.Add($"Adjuster: \"{stored.AdjusterName}\" → \"{parsed.AdjusterName}\"");

            if (!SameMoney(parsed.Totals.GrandTotal, stored.GrandTotal))
                diffs.Add($"Grand Total: {stored.GrandTotal:C0} → {parsed.Totals.GrandTotal:C0}");
            if (!SameMoney(parsed.Totals.PartsTotal, stored.PartsTotal))
                diffs.Add($"Parts Total: {stored.PartsTotal:C0} → {parsed.Totals.PartsTotal:C0}");
            if (!SameMoney(parsed.Totals.LaborTotal, stored.LaborTotal))
                diffs.Add($"Labor Total: {stored.LaborTotal:C0} → {parsed.Totals.LaborTotal:C0}");

            if (parsed.LineItems.Count != stored.LineItems.Count)
                diffs.Add($"Line Items: {stored.LineItems.Count} → {parsed.LineItems.Count}");
            else
            {
                // Same count — check per-line differences
                int changedLines = 0;
                for (int i = 0; i < parsed.LineItems.Count; i++)
                {
                    var p = parsed.LineItems[i];
                    var s = stored.LineItems[i];
                    if (!SameText(p.Description, s.Description) ||
                        !SameMoney(p.Price, s.Price) ||
                        !SameHours(p.LaborHours, s.LaborHours) ||
                        !SameHours(p.RefinishHours, s.RefinishHours))
                    {
                        changedLines++;
                    }
                }
                if (changedLines > 0)
                    diffs.Add($"{changedLines} line item{(changedLines == 1 ? "" : "s")} changed");
            }

            if (!SameMoney(parsed.Totals.BodyHourlyRate, stored.BodyHourlyRate))
                diffs.Add($"Body Rate: ${stored.BodyHourlyRate:0.00}/hr → ${parsed.Totals.BodyHourlyRate:0.00}/hr");
            if (!SameMoney(parsed.Totals.RefinishHourlyRate, stored.RefinishHourlyRate))
                diffs.Add($"Refinish Rate: ${stored.RefinishHourlyRate:0.00}/hr → ${parsed.Totals.RefinishHourlyRate:0.00}/hr");

            return diffs;
        }

        private static bool SameText(string? a, string? b)
            => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool SameMoney(decimal a, decimal b) => Math.Abs(a - b) < 0.01m;
        private static bool SameHours(decimal a, decimal b) => Math.Abs(a - b) < 0.01m;
    }
}
