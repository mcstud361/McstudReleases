#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services;

/// <summary>
/// One-shot cleanup service that classifies training examples,
/// removes junk, rebuilds patterns with running averages,
/// and populates EstimateHistoryDatabase from batched imports.
/// </summary>
public class DatabaseCleanupService
{
    public class CleanupResult
    {
        public int TotalExamples { get; set; }
        public int OperationExamplesKept { get; set; }
        public int MetadataLinesFound { get; set; }
        public int JunkLinesRemoved { get; set; }
        public int PatternsRebuilt { get; set; }
        public int BatchesFound { get; set; }
        public int HistoryEntriesCreated { get; set; }
        public int CoOccurrenceRecordsBuilt { get; set; }
        public string BackupPath { get; set; } = "";
    }

    public class BatchMetadata
    {
        public string InsuranceCompany { get; set; } = "";
        public string ClaimNumber { get; set; } = "";
        public string VIN { get; set; } = "";
        public string VehicleInfo { get; set; } = "";
        public string RONumber { get; set; } = "";
        public string Source { get; set; } = "";
    }

    private enum LineClassification
    {
        Metadata,
        Junk,
        Operation
    }

    // Known insurers (mirrors EstimateHistoryDatabase list)
    private static readonly string[] KnownInsurers =
    {
        "Allstate", "State Farm", "GEICO", "Progressive", "Nationwide",
        "Liberty Mutual", "Farmers", "USAA", "Travelers", "American Family",
        "Erie Insurance", "Hartford", "Amica", "MetLife", "Safeco",
        "Mercury", "Infinity", "Kemper", "Plymouth Rock", "NJM",
        "AAA", "Chubb", "AIG", "Cincinnati", "Hanover"
    };

    /// <summary>
    /// Run the full cleanup: classify, batch, rebuild patterns, populate history.
    /// </summary>
    public CleanupResult CleanupAndRebuild()
    {
        var result = new CleanupResult();
        var learningService = EstimateLearningService.Instance;
        var oldDb = learningService.CurrentDatabase;

        result.TotalExamples = oldDb.TrainingExamples.Count;

        // Step 1: Classify each training example
        var classified = new List<(TrainingExample example, LineClassification classification)>();
        foreach (var ex in oldDb.TrainingExamples)
        {
            var classification = ClassifyLine(ex);
            classified.Add((ex, classification));
        }

        var operationExamples = classified
            .Where(c => c.classification == LineClassification.Operation)
            .Select(c => c.example)
            .ToList();
        var metadataExamples = classified
            .Where(c => c.classification == LineClassification.Metadata)
            .Select(c => c.example)
            .ToList();
        var junkExamples = classified
            .Where(c => c.classification == LineClassification.Junk)
            .Select(c => c.example)
            .ToList();

        result.OperationExamplesKept = operationExamples.Count;
        result.MetadataLinesFound = metadataExamples.Count;
        result.JunkLinesRemoved = junkExamples.Count;

        // Step 2: Group into batches by DateAdded proximity (< 5 seconds = same batch)
        var allExamples = oldDb.TrainingExamples.OrderBy(e => e.DateAdded).ToList();
        var batches = GroupIntoBatches(allExamples, TimeSpan.FromSeconds(5));
        result.BatchesFound = batches.Count;

        // Step 3: Extract metadata per batch
        var batchMetadata = new Dictionary<int, BatchMetadata>();
        for (int i = 0; i < batches.Count; i++)
        {
            batchMetadata[i] = ExtractBatchMetadata(batches[i], classified);
        }

        // Step 4: Rebuild clean database
        var newDb = new LearnedPatternDatabase
        {
            TrainingExamples = operationExamples,
            Version = oldDb.Version,
            SmartMetadata = oldDb.SmartMetadata,
            PatternFeedbacks = oldDb.PatternFeedbacks,
            LastUpdated = DateTime.Now,
            EstimatesImported = oldDb.EstimatesImported,
            TotalEstimateValue = oldDb.TotalEstimateValue
        };

        // Rebuild patterns from clean operation examples with running averages
        foreach (var ex in operationExamples)
        {
            var patternKey = GeneratePatternKey(ex);
            if (string.IsNullOrWhiteSpace(patternKey))
                continue;

            if (newDb.Patterns.TryGetValue(patternKey, out var existing))
            {
                existing.ExampleCount++;
                existing.LastUpdated = DateTime.Now;

                foreach (var op in ex.GeneratedOperations)
                {
                    var existingOp = existing.Operations.FirstOrDefault(o =>
                        o.Description.Equals(op.Description, StringComparison.OrdinalIgnoreCase));

                    if (existingOp != null)
                    {
                        existingOp.TimesUsed++;
                        if (op.LaborHours > 0)
                            existingOp.LaborHours = ((existingOp.LaborHours * (existingOp.TimesUsed - 1)) + op.LaborHours) / existingOp.TimesUsed;
                        if (op.RepairHours > 0)
                            existingOp.RepairHours = ((existingOp.RepairHours * (existingOp.TimesUsed - 1)) + op.RepairHours) / existingOp.TimesUsed;
                        if (op.RefinishHours > 0)
                            existingOp.RefinishHours = ((existingOp.RefinishHours * (existingOp.TimesUsed - 1)) + op.RefinishHours) / existingOp.TimesUsed;
                        if (op.Price > 0)
                            existingOp.Price = ((existingOp.Price * (existingOp.TimesUsed - 1)) + op.Price) / existingOp.TimesUsed;
                    }
                    else
                    {
                        var newOp = new GeneratedOperation
                        {
                            OperationType = op.OperationType,
                            Description = op.Description,
                            Category = op.Category,
                            LaborHours = op.LaborHours,
                            RepairHours = op.RepairHours,
                            RefinishHours = op.RefinishHours,
                            Price = op.Price,
                            Quantity = op.Quantity,
                            Confidence = op.Confidence,
                            Source = op.Source,
                            TimesUsed = 1
                        };
                        existing.Operations.Add(newOp);
                    }
                }

                existing.Confidence = CalculateConfidence(existing.ExampleCount);
            }
            else
            {
                var newPattern = new LearnedPattern
                {
                    PatternKey = patternKey,
                    PartName = ex.PartName,
                    OperationType = ex.OperationType,
                    Operations = ex.GeneratedOperations.Select(op => new GeneratedOperation
                    {
                        OperationType = op.OperationType,
                        Description = op.Description,
                        Category = op.Category,
                        LaborHours = op.LaborHours,
                        RepairHours = op.RepairHours,
                        RefinishHours = op.RefinishHours,
                        Price = op.Price,
                        Quantity = op.Quantity,
                        Confidence = op.Confidence,
                        Source = op.Source,
                        TimesUsed = 1
                    }).ToList(),
                    ExampleCount = 1,
                    DateCreated = DateTime.Now,
                    LastUpdated = DateTime.Now,
                    Confidence = 0.5
                };
                newDb.Patterns[patternKey] = newPattern;
            }
        }

        result.PatternsRebuilt = newDb.Patterns.Count;

        // Step 4b: Build co-occurrence data from batches
        newDb.CoOccurrences = new Dictionary<string, CoOccurrenceRecord>();
        foreach (var batch in batches)
        {
            var batchOps = batch
                .Where(ex => ClassifyLine(ex) == LineClassification.Operation &&
                             !string.IsNullOrWhiteSpace(ex.PartName))
                .Select(ex =>
                {
                    var normPart = ex.PartName.ToLowerInvariant().Replace(" ", "_").Replace("-", "_").Replace("cover", "").Replace("panel", "").Trim('_');
                    var normOp = ex.OperationType?.ToLowerInvariant() ?? "";
                    if (normOp.Contains("repl")) normOp = "replace";
                    else if (normOp.Contains("rpr") || normOp.Contains("repair")) normOp = "repair";
                    else if (normOp.Contains("r&i") || normOp.Contains("r/i")) normOp = "ri";
                    else if (normOp.Contains("refn") || normOp.Contains("refinish")) normOp = "refinish";
                    else normOp = normOp.Replace(" ", "_");
                    var patternKey = string.IsNullOrEmpty(normOp) ? normPart : $"{normPart}|{normOp}";
                    return (PatternKey: patternKey, PartName: ex.PartName, OperationType: ex.OperationType ?? "",
                            LaborHours: ex.RepairHours, RefinishHours: ex.RefinishHours, Price: ex.Price);
                })
                .ToList();

            if (batchOps.Count < 2) continue;

            foreach (var op in batchOps)
            {
                if (!newDb.CoOccurrences.TryGetValue(op.PatternKey, out var record))
                {
                    record = new CoOccurrenceRecord
                    {
                        PatternKey = op.PatternKey,
                        PartName = op.PartName,
                        OperationType = op.OperationType
                    };
                    newDb.CoOccurrences[op.PatternKey] = record;
                }
                record.TotalEstimateCount++;

                foreach (var other in batchOps)
                {
                    if (other.PatternKey == op.PatternKey) continue;
                    if (!record.CoOccurringOperations.TryGetValue(other.PatternKey, out var entry))
                    {
                        entry = new CoOccurrenceEntry
                        {
                            PatternKey = other.PatternKey,
                            PartName = other.PartName,
                            OperationType = other.OperationType
                        };
                        record.CoOccurringOperations[other.PatternKey] = entry;
                    }
                    int n = entry.TimesSeenTogether;
                    entry.AvgLaborHours = (entry.AvgLaborHours * n + other.LaborHours) / (n + 1);
                    entry.AvgRefinishHours = (entry.AvgRefinishHours * n + other.RefinishHours) / (n + 1);
                    entry.AvgPrice = (entry.AvgPrice * n + other.Price) / (n + 1);
                    entry.TimesSeenTogether++;
                }
            }
        }
        result.CoOccurrenceRecordsBuilt = newDb.CoOccurrences.Count;

        // Step 5: Populate EstimateHistoryDatabase from batches
        var historyDb = EstimateHistoryDatabase.Instance;
        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            var meta = batchMetadata[i];

            // Get operation examples from this batch
            var batchOpExamples = batch
                .Where(ex => ClassifyLine(ex) == LineClassification.Operation)
                .ToList();

            if (batchOpExamples.Count == 0)
                continue;

            // Build ParsedEstimate from batch
            var parsed = new ParsedEstimate
            {
                Source = !string.IsNullOrEmpty(meta.Source) ? meta.Source : "CCC",
                VehicleInfo = meta.VehicleInfo,
                VIN = meta.VIN,
                SourceFile = "cleanup_rebuild",
                ParsedDate = batch.First().DateAdded,
                LineItems = batchOpExamples.Select(ex => new PdfEstimateLineItem
                {
                    RawLine = ex.EstimateLine,
                    Description = ex.EstimateLine,
                    PartName = ex.PartName,
                    OperationType = ex.OperationType,
                    LaborHours = ex.RepairHours,
                    RefinishHours = ex.RefinishHours,
                    Price = ex.Price
                }).ToList()
            };

            // Compute totals from line items
            parsed.Totals = new EstimateTotals
            {
                LaborTotal = batchOpExamples.Sum(e => e.RepairHours * 50m), // Approximate at $50/hr
                PartsTotal = batchOpExamples.Sum(e => e.Price),
                RefinishTotal = batchOpExamples.Sum(e => e.RefinishHours * 50m),
                GrandTotal = batchOpExamples.Sum(e => e.Price) +
                             batchOpExamples.Sum(e => (e.RepairHours + e.RefinishHours) * 50m)
            };

            try
            {
                historyDb.AddEstimate(parsed, meta.InsuranceCompany);
                result.HistoryEntriesCreated++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cleanup] Error adding batch {i} to history: {ex.Message}");
            }
        }

        // Step 6: Replace the database
        learningService.ReplaceDatabase(newDb);

        System.Diagnostics.Debug.WriteLine(
            $"[Cleanup] Done: {result.OperationExamplesKept} kept, {result.JunkLinesRemoved} junk removed, " +
            $"{result.PatternsRebuilt} patterns, {result.CoOccurrenceRecordsBuilt} co-occurrence records, " +
            $"{result.HistoryEntriesCreated} history entries");

        return result;
    }

    private LineClassification ClassifyLine(TrainingExample example)
    {
        var line = example.EstimateLine ?? "";
        var upper = line.Trim().ToUpperInvariant();

        // Check for metadata first (these are valuable but not operations)
        if (IsMetadataLine(upper, line))
            return LineClassification.Metadata;

        // Check junk via the strong filter (but only if not metadata)
        if (!string.IsNullOrWhiteSpace(line) && EstimatePdfParser.IsHeaderOrFooter(line))
            return LineClassification.Junk;

        // Operation: has at least one of: non-empty OperationType, RepairHours > 0, RefinishHours > 0, Price > 10
        bool hasOperationType = !string.IsNullOrWhiteSpace(example.OperationType);
        bool hasHours = example.RepairHours > 0 || example.RefinishHours > 0;
        bool hasPrice = example.Price > 10;

        if (hasOperationType || hasHours || hasPrice)
            return LineClassification.Operation;

        // No meaningful data — classify as junk
        return LineClassification.Junk;
    }

    private bool IsMetadataLine(string upper, string rawLine)
    {
        // Insurance company patterns
        if (upper.Contains("INSURANCE COMPANY:") || upper.Contains("CARRIER:"))
            return true;
        foreach (var insurer in KnownInsurers)
        {
            if (upper.Contains(insurer.ToUpperInvariant()))
                return true;
        }

        // Claim number
        if (upper.Contains("CLAIM #:") || upper.Contains("CLAIM NUMBER:") || upper.Contains("CLAIM NUMBER"))
            return true;

        // VIN (17 chars, no I/O/Q)
        if (Regex.IsMatch(rawLine, @"\b[A-HJ-NPR-Z0-9]{17}\b"))
            return true;

        // Vehicle info: year make model (e.g. "2019 Honda Accord")
        if (Regex.IsMatch(rawLine, @"\b20\d{2}\s+[A-Za-z][\w-]+\s+[A-Za-z][\w-]+", RegexOptions.IgnoreCase))
            return true;

        // RO / Workfile ID
        if (upper.Contains("WORKFILE ID:") || upper.Contains("RO:") || upper.Contains("RO #:"))
            return true;

        return false;
    }

    private List<List<TrainingExample>> GroupIntoBatches(List<TrainingExample> examples, TimeSpan gap)
    {
        var batches = new List<List<TrainingExample>>();
        if (examples.Count == 0) return batches;

        var currentBatch = new List<TrainingExample> { examples[0] };
        for (int i = 1; i < examples.Count; i++)
        {
            if ((examples[i].DateAdded - examples[i - 1].DateAdded) <= gap)
            {
                currentBatch.Add(examples[i]);
            }
            else
            {
                batches.Add(currentBatch);
                currentBatch = new List<TrainingExample> { examples[i] };
            }
        }
        batches.Add(currentBatch);
        return batches;
    }

    private BatchMetadata ExtractBatchMetadata(
        List<TrainingExample> batch,
        List<(TrainingExample example, LineClassification classification)> classified)
    {
        var meta = new BatchMetadata();

        // Find metadata lines in this batch
        var metadataLines = batch
            .Where(ex => classified.Any(c => c.example == ex && c.classification == LineClassification.Metadata))
            .ToList();

        foreach (var ex in metadataLines)
        {
            var line = ex.EstimateLine ?? "";
            var upper = line.Trim().ToUpperInvariant();

            // Insurance
            if (string.IsNullOrEmpty(meta.InsuranceCompany))
            {
                foreach (var insurer in KnownInsurers)
                {
                    if (upper.Contains(insurer.ToUpperInvariant()))
                    {
                        meta.InsuranceCompany = insurer;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(meta.InsuranceCompany))
                {
                    var insurMatch = Regex.Match(line, @"(?:Insurance Company:|Carrier:)\s*(.+)", RegexOptions.IgnoreCase);
                    if (insurMatch.Success)
                        meta.InsuranceCompany = insurMatch.Groups[1].Value.Trim();
                }
            }

            // Claim #
            if (string.IsNullOrEmpty(meta.ClaimNumber))
            {
                var claimMatch = Regex.Match(line, @"(?:Claim\s*#?:?\s*|Claim\s+Number:?\s*)([A-Z0-9-]+)", RegexOptions.IgnoreCase);
                if (claimMatch.Success)
                    meta.ClaimNumber = claimMatch.Groups[1].Value.Trim();
            }

            // VIN
            if (string.IsNullOrEmpty(meta.VIN))
            {
                var vinMatch = Regex.Match(line, @"\b([A-HJ-NPR-Z0-9]{17})\b");
                if (vinMatch.Success)
                    meta.VIN = vinMatch.Groups[1].Value;
            }

            // Vehicle info
            if (string.IsNullOrEmpty(meta.VehicleInfo))
            {
                var vehicleMatch = Regex.Match(line, @"\b(20\d{2}\s+[\w-]+\s+[\w-]+)", RegexOptions.IgnoreCase);
                if (vehicleMatch.Success)
                    meta.VehicleInfo = vehicleMatch.Value.Trim();
            }

            // RO Number
            if (string.IsNullOrEmpty(meta.RONumber))
            {
                var roMatch = Regex.Match(line, @"(?:Workfile ID:|RO:?|RO #:?)\s*(\S+)", RegexOptions.IgnoreCase);
                if (roMatch.Success)
                    meta.RONumber = roMatch.Groups[1].Value.Trim();
            }
        }

        // Also pull source/vehicle info from training examples themselves
        if (string.IsNullOrEmpty(meta.Source))
        {
            var firstWithSource = batch.FirstOrDefault(e => !string.IsNullOrEmpty(e.Source));
            if (firstWithSource != null)
                meta.Source = firstWithSource.Source;
        }
        if (string.IsNullOrEmpty(meta.VehicleInfo))
        {
            var firstWithVehicle = batch.FirstOrDefault(e => !string.IsNullOrEmpty(e.VehicleInfo));
            if (firstWithVehicle != null)
                meta.VehicleInfo = firstWithVehicle.VehicleInfo ?? "";
        }

        return meta;
    }

    private string GeneratePatternKey(TrainingExample example)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(example.PartName))
            parts.Add(example.PartName.ToLowerInvariant().Replace(" ", "_"));

        if (!string.IsNullOrEmpty(example.OperationType))
            parts.Add(example.OperationType.ToLowerInvariant());

        return string.Join("|", parts);
    }

    private double CalculateConfidence(int exampleCount)
    {
        return 1.0 - (1.0 / (1.0 + exampleCount * 0.5));
    }
}
