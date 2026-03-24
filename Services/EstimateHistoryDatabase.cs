#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services;

/// <summary>
/// Estimate DNA / History Database
/// Stores parsed estimates for pattern analysis, insurance payment tracking, and similarity matching.
///
/// CAPABILITIES:
/// - Track which insurers pay for specific operations
/// - Find similar estimates based on vehicle, damage type, complexity
/// - Generate "Estimate DNA" fingerprints for pattern matching
/// - Answer queries like "How many times did Allstate pay for corrosion protection?"
/// </summary>
public class EstimateHistoryDatabase
{
    private static EstimateHistoryDatabase? _instance;
    public static EstimateHistoryDatabase Instance => _instance ??= new EstimateHistoryDatabase();

    private readonly string _databasePath;
    private EstimateHistoryData _data = new();
    private bool _isLoaded;

    public bool IsLoaded => _isLoaded;
    public int EstimateCount => _data.Estimates.Count;
    public List<string> KnownInsurers => _data.Estimates
        .Select(e => e.InsuranceCompany)
        .Where(i => !string.IsNullOrWhiteSpace(i))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(i => i)
        .ToList();

    private EstimateHistoryDatabase()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McStudDesktop"
        );
        Directory.CreateDirectory(appDataPath);
        _databasePath = Path.Combine(appDataPath, "estimate_history.json");
        LoadDatabase();
    }

    #region Database Operations

    private void LoadDatabase()
    {
        try
        {
            if (File.Exists(_databasePath))
            {
                var json = File.ReadAllText(_databasePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                _data = JsonSerializer.Deserialize<EstimateHistoryData>(json, options) ?? new EstimateHistoryData();
                _isLoaded = true;
                System.Diagnostics.Debug.WriteLine($"[EstimateHistory] Loaded {_data.Estimates.Count} estimates from history");
            }
            else
            {
                _data = new EstimateHistoryData();
                _isLoaded = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EstimateHistory] Failed to load: {ex.Message}");
            _data = new EstimateHistoryData();
            _isLoaded = true;
        }
    }

    public void SaveDatabase()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_data, options);
            File.WriteAllText(_databasePath, json);
            System.Diagnostics.Debug.WriteLine($"[EstimateHistory] Saved {_data.Estimates.Count} estimates");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EstimateHistory] Failed to save: {ex.Message}");
        }
    }

    #endregion

    #region Add/Import Estimates

    /// <summary>
    /// Add a parsed estimate to the history database
    /// </summary>
    public string AddEstimate(ParsedEstimate parsed, string? insuranceCompany = null, string? roNumber = null)
    {
        var estimate = new StoredEstimate
        {
            Id = Guid.NewGuid().ToString(),
            ImportedDate = DateTime.Now,
            SourceFile = parsed.SourceFile,
            EstimateSource = parsed.Source, // CCC, Mitchell, Audatex
            InsuranceCompany = insuranceCompany ?? ExtractInsuranceCompany(parsed.RawText),
            RONumber = roNumber ?? ExtractRONumber(parsed.RawText),
            VehicleInfo = parsed.VehicleInfo,
            VIN = parsed.VIN,
            GrandTotal = parsed.Totals.GrandTotal,
            PartsTotal = parsed.Totals.PartsTotal,
            LaborTotal = parsed.Totals.LaborTotal,
            PaintTotal = parsed.Totals.RefinishTotal + parsed.Totals.PaintMaterial
        };

        // Convert line items to stored format with operation tracking
        foreach (var item in parsed.LineItems)
        {
            var storedItem = new StoredLineItem
            {
                Description = item.Description,
                PartName = item.PartName,
                OperationType = item.OperationType,
                Section = item.Section,
                PartNumber = item.PartNumber,
                LaborHours = item.LaborHours,
                RefinishHours = item.RefinishHours,
                Price = item.Price,
                Quantity = item.Quantity,
                LaborType = item.LaborType,
                IsManualLine = item.IsManualMarker,
                IsAdditionalOperation = item.IsAdditionalOperation,
                ParentPartName = item.ParentPartName,
                // Track payment status (default to paid since it's on the estimate)
                WasPaid = true
            };
            estimate.LineItems.Add(storedItem);
        }

        // Generate DNA fingerprint
        estimate.DNA = GenerateEstimateDNA(estimate);

        // Grade the estimate quality
        try
        {
            var qualityLines = estimate.LineItems.Select(li => new ParsedEstimateLine
            {
                PartName = li.PartName,
                OperationType = li.OperationType,
                LaborHours = li.LaborHours,
                RefinishHours = li.RefinishHours,
                Price = li.Price,
                Description = li.Description,
                IsManualLine = li.IsManualLine
            }).ToList();
            var quality = EstimateQualityService.Instance.AssessQuality(qualityLines, estimate.VehicleInfo);
            estimate.QualityScore = quality.QualityScore;
            estimate.QualityGrade = quality.Grade.ToString();
            System.Diagnostics.Debug.WriteLine($"[History] Graded estimate: {estimate.QualityScore}/100 ({estimate.QualityGrade})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[History] Error grading estimate: {ex.Message}");
            estimate.QualityScore = 70; // Default to Fair if grading fails
            estimate.QualityGrade = "Good";
        }

        // Add to database
        _data.Estimates.Add(estimate);

        // Update operation payment index for fast lookups
        IndexEstimateOperations(estimate);

        SaveDatabase();

        System.Diagnostics.Debug.WriteLine($"[EstimateHistory] Added estimate {estimate.Id}: {estimate.VehicleInfo} - {estimate.InsuranceCompany}");
        return estimate.Id;
    }

    /// <summary>
    /// Extract insurance company from raw estimate text
    /// </summary>
    private string ExtractInsuranceCompany(string rawText)
    {
        var knownInsurers = new[]
        {
            "Allstate", "State Farm", "GEICO", "Progressive", "Nationwide",
            "Liberty Mutual", "Farmers", "USAA", "Travelers", "American Family",
            "Erie Insurance", "Hartford", "Amica", "MetLife", "Safeco",
            "Mercury", "Infinity", "Kemper", "Plymouth Rock", "NJM",
            "AAA", "Chubb", "AIG", "Cincinnati", "Hanover"
        };

        var upper = rawText.ToUpperInvariant();
        foreach (var insurer in knownInsurers)
        {
            if (upper.Contains(insurer.ToUpperInvariant()))
                return insurer;
        }

        // Try to extract from "Insurance:" or "Carrier:" labels
        var patterns = new[]
        {
            @"Insurance[:\s]+([A-Za-z\s]+?)(?:\r?\n|$)",
            @"Carrier[:\s]+([A-Za-z\s]+?)(?:\r?\n|$)",
            @"Insurance Company[:\s]+([A-Za-z\s]+?)(?:\r?\n|$)"
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(rawText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var extracted = match.Groups[1].Value.Trim();
                if (extracted.Length > 3 && extracted.Length < 50)
                    return extracted;
            }
        }

        return "Unknown";
    }

    /// <summary>
    /// Extract RO/Claim number from raw estimate text
    /// </summary>
    private string ExtractRONumber(string rawText)
    {
        var patterns = new[]
        {
            @"RO[#:\s]+([A-Z0-9-]+)",
            @"Repair Order[#:\s]+([A-Z0-9-]+)",
            @"Claim[#:\s]+([A-Z0-9-]+)",
            @"Workfile[#:\s]+([A-Z0-9-]+)"
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(rawText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return "";
    }

    #endregion

    #region Estimate DNA Fingerprint

    /// <summary>
    /// Generate an "Estimate DNA" fingerprint for pattern matching
    /// </summary>
    private EstimateDNA GenerateEstimateDNA(StoredEstimate estimate)
    {
        var dna = new EstimateDNA();

        // Calculate complexity score based on line items
        dna.TotalLineCount = estimate.LineItems.Count;
        dna.ManualLineCount = estimate.LineItems.Count(i => i.IsManualLine);
        dna.AdditionalOpCount = estimate.LineItems.Count(i => i.IsAdditionalOperation);

        // Categorize operations
        var sections = estimate.LineItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Section))
            .GroupBy(i => i.Section)
            .ToDictionary(g => g.Key, g => g.Count());
        dna.SectionBreakdown = sections;

        // Damage zones (which parts are involved)
        dna.DamageZones = estimate.LineItems
            .Where(i => !string.IsNullOrWhiteSpace(i.PartName))
            .Select(i => NormalizeDamageZone(i.PartName))
            .Where(z => !string.IsNullOrWhiteSpace(z))
            .Distinct()
            .ToList();

        // Operation types distribution
        dna.OperationTypes = estimate.LineItems
            .Where(i => !string.IsNullOrWhiteSpace(i.OperationType))
            .GroupBy(i => i.OperationType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Calculate complexity score (0-100)
        dna.ComplexityScore = CalculateComplexityScore(estimate);

        // Calculate profit indicators
        dna.ManualLineRatio = dna.TotalLineCount > 0
            ? (double)dna.ManualLineCount / dna.TotalLineCount
            : 0;

        // Risk flags
        dna.RiskFlags = IdentifyRiskFlags(estimate);

        // Keywords for similarity matching
        dna.Keywords = ExtractKeywords(estimate);

        return dna;
    }

    private string NormalizeDamageZone(string partName)
    {
        var lower = partName.ToLowerInvariant();

        // Normalize to damage zones
        if (lower.Contains("bumper") && lower.Contains("front")) return "Front Bumper";
        if (lower.Contains("bumper") && lower.Contains("rear")) return "Rear Bumper";
        if (lower.Contains("fender") && (lower.Contains("front") || lower.Contains("lf") || lower.Contains("rf"))) return "Front Fender";
        if (lower.Contains("fender")) return "Fender";
        if (lower.Contains("hood")) return "Hood";
        if (lower.Contains("door") && lower.Contains("front")) return "Front Door";
        if (lower.Contains("door") && lower.Contains("rear")) return "Rear Door";
        if (lower.Contains("door")) return "Door";
        if (lower.Contains("quarter")) return "Quarter Panel";
        if (lower.Contains("roof")) return "Roof";
        if (lower.Contains("trunk") || lower.Contains("decklid")) return "Trunk/Decklid";
        if (lower.Contains("liftgate") || lower.Contains("tailgate")) return "Liftgate";
        if (lower.Contains("headl")) return "Headlamp";
        if (lower.Contains("taill")) return "Taillamp";
        if (lower.Contains("mirror")) return "Mirror";
        if (lower.Contains("windshield")) return "Windshield";
        if (lower.Contains("pillar")) return "Pillar";
        if (lower.Contains("rocker")) return "Rocker Panel";
        if (lower.Contains("radiator")) return "Radiator Support";
        if (lower.Contains("frame")) return "Frame";

        return partName;
    }

    private int CalculateComplexityScore(StoredEstimate estimate)
    {
        int score = 0;

        // Base score from line count
        score += Math.Min(estimate.LineItems.Count * 2, 30);

        // Manual lines indicate complexity (estimator had to add them)
        score += estimate.LineItems.Count(i => i.IsManualLine) * 3;

        // Multiple sections = more damage areas
        var sectionCount = estimate.LineItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Section))
            .Select(i => i.Section)
            .Distinct()
            .Count();
        score += sectionCount * 5;

        // Structural/frame work is complex
        if (estimate.LineItems.Any(i =>
            i.Section?.Contains("FRAME", StringComparison.OrdinalIgnoreCase) == true ||
            i.Section?.Contains("STRUCTURAL", StringComparison.OrdinalIgnoreCase) == true ||
            i.PartName?.Contains("frame", StringComparison.OrdinalIgnoreCase) == true))
        {
            score += 20;
        }

        // ADAS/calibration adds complexity
        if (estimate.LineItems.Any(i =>
            i.Description?.Contains("calibrat", StringComparison.OrdinalIgnoreCase) == true ||
            i.Description?.Contains("ADAS", StringComparison.OrdinalIgnoreCase) == true ||
            i.Description?.Contains("scan", StringComparison.OrdinalIgnoreCase) == true))
        {
            score += 15;
        }

        // High total indicates complexity
        if (estimate.GrandTotal > 10000) score += 10;
        if (estimate.GrandTotal > 20000) score += 10;

        return Math.Min(score, 100);
    }

    private List<string> IdentifyRiskFlags(StoredEstimate estimate)
    {
        var flags = new List<string>();

        // Low manual line ratio might mean missed operations
        var manualRatio = estimate.LineItems.Count > 0
            ? (double)estimate.LineItems.Count(i => i.IsManualLine) / estimate.LineItems.Count
            : 0;
        if (manualRatio < 0.15 && estimate.LineItems.Count > 10)
            flags.Add("Low manual line ratio - potential missed operations");

        // Structural work without scanning
        bool hasStructural = estimate.LineItems.Any(i =>
            i.Section?.Contains("STRUCTURAL", StringComparison.OrdinalIgnoreCase) == true ||
            i.PartName?.Contains("frame", StringComparison.OrdinalIgnoreCase) == true);
        bool hasScan = estimate.LineItems.Any(i =>
            i.Description?.Contains("scan", StringComparison.OrdinalIgnoreCase) == true);
        if (hasStructural && !hasScan)
            flags.Add("Structural work without scanning noted");

        // Bumper replacement without sensor check
        bool hasBumperReplace = estimate.LineItems.Any(i =>
            i.OperationType?.Contains("Repl", StringComparison.OrdinalIgnoreCase) == true &&
            i.PartName?.Contains("bumper", StringComparison.OrdinalIgnoreCase) == true);
        bool hasSensorCheck = estimate.LineItems.Any(i =>
            i.Description?.Contains("sensor", StringComparison.OrdinalIgnoreCase) == true ||
            i.Description?.Contains("park assist", StringComparison.OrdinalIgnoreCase) == true);
        if (hasBumperReplace && !hasSensorCheck)
            flags.Add("Bumper replacement - check for sensors/park assist");

        // Glass work without calibration
        bool hasGlass = estimate.LineItems.Any(i =>
            i.PartName?.Contains("windshield", StringComparison.OrdinalIgnoreCase) == true ||
            i.PartName?.Contains("glass", StringComparison.OrdinalIgnoreCase) == true);
        bool hasCalibration = estimate.LineItems.Any(i =>
            i.Description?.Contains("calibrat", StringComparison.OrdinalIgnoreCase) == true);
        if (hasGlass && !hasCalibration)
            flags.Add("Glass work without calibration - check ADAS requirements");

        return flags;
    }

    private List<string> ExtractKeywords(StoredEstimate estimate)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add part names
        foreach (var item in estimate.LineItems.Where(i => !string.IsNullOrWhiteSpace(i.PartName)))
        {
            keywords.Add(item.PartName);
        }

        // Add operation types
        foreach (var item in estimate.LineItems.Where(i => !string.IsNullOrWhiteSpace(i.OperationType)))
        {
            keywords.Add(item.OperationType);
        }

        // Add sections
        foreach (var item in estimate.LineItems.Where(i => !string.IsNullOrWhiteSpace(i.Section)))
        {
            keywords.Add(item.Section);
        }

        // Extract key terms from descriptions
        var keyTerms = new[] { "calibration", "scan", "ADAS", "blend", "refinish", "structural", "frame", "airbag", "sensor" };
        foreach (var item in estimate.LineItems)
        {
            foreach (var term in keyTerms)
            {
                if (item.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
                    keywords.Add(term);
            }
        }

        return keywords.ToList();
    }

    #endregion

    #region Operation Payment Index

    /// <summary>
    /// Index estimate operations for fast payment lookups
    /// </summary>
    private void IndexEstimateOperations(StoredEstimate estimate)
    {
        if (string.IsNullOrWhiteSpace(estimate.InsuranceCompany) || estimate.InsuranceCompany == "Unknown")
            return;

        var insurerKey = estimate.InsuranceCompany.ToLowerInvariant();

        if (!_data.OperationPaymentIndex.ContainsKey(insurerKey))
            _data.OperationPaymentIndex[insurerKey] = new Dictionary<string, OperationPaymentStats>();

        foreach (var item in estimate.LineItems.Where(i => i.WasPaid))
        {
            // Index by description (normalized)
            var opKey = NormalizeOperationKey(item.Description);
            if (string.IsNullOrWhiteSpace(opKey)) continue;

            if (!_data.OperationPaymentIndex[insurerKey].ContainsKey(opKey))
            {
                _data.OperationPaymentIndex[insurerKey][opKey] = new OperationPaymentStats
                {
                    OperationDescription = item.Description,
                    NormalizedKey = opKey
                };
            }

            var stats = _data.OperationPaymentIndex[insurerKey][opKey];
            stats.TimesPaid++;
            stats.TotalHoursPaid += item.LaborHours + item.RefinishHours;
            stats.TotalAmountPaid += item.Price;
            stats.EstimateIds.Add(estimate.Id);

            // Track average hours
            if (stats.TimesPaid > 0)
                stats.AverageHours = stats.TotalHoursPaid / stats.TimesPaid;
        }
    }

    private string NormalizeOperationKey(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";

        var lower = description.ToLowerInvariant();

        // Normalize common operation terms
        var normalizations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "corrosion protection", "corrosion_protection" },
            { "corrosion protect", "corrosion_protection" },
            { "cavity wax", "corrosion_protection" },
            { "anti-corrosion", "corrosion_protection" },
            { "seam sealer", "seam_sealer" },
            { "seam seal", "seam_sealer" },
            { "adhesion promoter", "adhesion_promoter" },
            { "flex additive", "flex_additive" },
            { "flex agent", "flex_additive" },
            { "de-nib", "denib" },
            { "denib", "denib" },
            { "color sand", "color_sand" },
            { "wet sand", "wet_sand" },
            { "pre-scan", "pre_scan" },
            { "pre scan", "pre_scan" },
            { "post-scan", "post_scan" },
            { "post scan", "post_scan" },
            { "diagnostic scan", "diagnostic_scan" },
            { "calibration", "calibration" },
            { "blend", "blend" },
            { "clear coat", "clear_coat" },
            { "clearcoat", "clear_coat" },
            { "prime", "prime" },
            { "primer", "prime" },
            { "mask", "masking" },
            { "backtape", "backtape" },
            { "back tape", "backtape" },
            { "cover car", "cover_car" },
            { "tri-coat", "tricoat" },
            { "three stage", "tricoat" }
        };

        foreach (var kvp in normalizations)
        {
            if (lower.Contains(kvp.Key))
                return kvp.Value;
        }

        // Return cleaned version if no specific match
        return System.Text.RegularExpressions.Regex.Replace(lower, @"[^a-z0-9\s]", "")
            .Replace(" ", "_")
            .Trim('_');
    }

    #endregion

    #region Query Methods

    /// <summary>
    /// Find estimates where a specific insurer paid for an operation
    /// Example: "Show estimates where Allstate paid for corrosion protection"
    /// </summary>
    public List<EstimateSearchResult> FindEstimatesWithOperation(string insuranceCompany, string operationKeyword)
    {
        var results = new List<EstimateSearchResult>();
        var insurerLower = insuranceCompany.ToLowerInvariant();
        var opLower = operationKeyword.ToLowerInvariant();

        foreach (var estimate in _data.Estimates)
        {
            if (!estimate.InsuranceCompany.Contains(insurerLower, StringComparison.OrdinalIgnoreCase))
                continue;

            var matchingItems = estimate.LineItems
                .Where(i => i.Description?.Contains(opLower, StringComparison.OrdinalIgnoreCase) == true ||
                           i.PartName?.Contains(opLower, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (matchingItems.Any())
            {
                results.Add(new EstimateSearchResult
                {
                    Estimate = estimate,
                    MatchingLineItems = matchingItems,
                    MatchReason = $"Contains '{operationKeyword}' paid by {insuranceCompany}"
                });
            }
        }

        return results.OrderByDescending(r => r.Estimate.ImportedDate).ToList();
    }

    /// <summary>
    /// Get payment statistics for an operation by insurer
    /// Example: "How many times did Allstate pay for corrosion protection?"
    /// </summary>
    public OperationPaymentSummary GetOperationPaymentStats(string insuranceCompany, string operationKeyword)
    {
        var summary = new OperationPaymentSummary
        {
            InsuranceCompany = insuranceCompany,
            OperationKeyword = operationKeyword
        };

        var insurerLower = insuranceCompany.ToLowerInvariant();
        var normalizedOp = NormalizeOperationKey(operationKeyword);

        // Check indexed stats first
        if (_data.OperationPaymentIndex.TryGetValue(insurerLower, out var insurerOps))
        {
            if (insurerOps.TryGetValue(normalizedOp, out var stats))
            {
                summary.TimesPaid = stats.TimesPaid;
                summary.TotalAmountPaid = stats.TotalAmountPaid;
                summary.AverageHours = stats.AverageHours;
                summary.EstimateIds = stats.EstimateIds.ToList();
            }
        }

        // Also search by keyword match for variations
        var opLower = operationKeyword.ToLowerInvariant();
        foreach (var estimate in _data.Estimates)
        {
            if (!estimate.InsuranceCompany.Contains(insurerLower, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var item in estimate.LineItems)
            {
                if (item.Description?.Contains(opLower, StringComparison.OrdinalIgnoreCase) == true && item.WasPaid)
                {
                    if (!summary.EstimateIds.Contains(estimate.Id))
                    {
                        summary.TimesPaid++;
                        summary.TotalAmountPaid += item.Price;
                        summary.TotalHoursPaid += item.LaborHours + item.RefinishHours;
                        summary.EstimateIds.Add(estimate.Id);
                    }
                }
            }
        }

        if (summary.TimesPaid > 0)
            summary.AverageHours = summary.TotalHoursPaid / summary.TimesPaid;

        return summary;
    }

    /// <summary>
    /// Get all payment stats for a specific insurer
    /// Example: "What does State Farm typically pay for?"
    /// </summary>
    public Dictionary<string, OperationPaymentStats> GetInsurerPaymentProfile(string insuranceCompany)
    {
        var insurerLower = insuranceCompany.ToLowerInvariant();

        if (_data.OperationPaymentIndex.TryGetValue(insurerLower, out var profile))
            return profile;

        return new Dictionary<string, OperationPaymentStats>();
    }

    /// <summary>
    /// Find similar estimates based on DNA fingerprint
    /// Example: "This looks like RO #4521 - that one supplemented 3 times"
    /// </summary>
    public List<SimilarEstimateMatch> FindSimilarEstimates(StoredEstimate target, int maxResults = 5)
    {
        var matches = new List<SimilarEstimateMatch>();

        foreach (var estimate in _data.Estimates.Where(e => e.Id != target.Id))
        {
            var similarity = CalculateSimilarity(target, estimate);
            if (similarity > 0.3) // 30% similarity threshold
            {
                matches.Add(new SimilarEstimateMatch
                {
                    Estimate = estimate,
                    SimilarityScore = similarity,
                    CommonDamageZones = target.DNA.DamageZones
                        .Intersect(estimate.DNA.DamageZones, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    ComplexityDifference = Math.Abs(target.DNA.ComplexityScore - estimate.DNA.ComplexityScore)
                });
            }
        }

        return matches
            .OrderByDescending(m => m.SimilarityScore)
            .Take(maxResults)
            .ToList();
    }

    private double CalculateSimilarity(StoredEstimate a, StoredEstimate b)
    {
        double score = 0;
        double maxScore = 0;

        // Same insurance company (weight: 10%)
        maxScore += 10;
        if (a.InsuranceCompany.Equals(b.InsuranceCompany, StringComparison.OrdinalIgnoreCase))
            score += 10;

        // Similar vehicle (weight: 15%)
        maxScore += 15;
        if (!string.IsNullOrWhiteSpace(a.VehicleInfo) && !string.IsNullOrWhiteSpace(b.VehicleInfo))
        {
            var aWords = a.VehicleInfo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bWords = b.VehicleInfo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var commonWords = aWords.Intersect(bWords, StringComparer.OrdinalIgnoreCase).Count();
            score += 15 * (double)commonWords / Math.Max(aWords.Length, bWords.Length);
        }

        // Damage zones overlap (weight: 40%)
        maxScore += 40;
        if (a.DNA.DamageZones.Count > 0 && b.DNA.DamageZones.Count > 0)
        {
            var overlap = a.DNA.DamageZones
                .Intersect(b.DNA.DamageZones, StringComparer.OrdinalIgnoreCase)
                .Count();
            var total = a.DNA.DamageZones.Union(b.DNA.DamageZones, StringComparer.OrdinalIgnoreCase).Count();
            score += 40 * (double)overlap / total;
        }

        // Complexity similarity (weight: 20%)
        maxScore += 20;
        var complexityDiff = Math.Abs(a.DNA.ComplexityScore - b.DNA.ComplexityScore);
        score += 20 * (1 - complexityDiff / 100.0);

        // Similar line count (weight: 15%)
        maxScore += 15;
        var lineDiff = Math.Abs(a.DNA.TotalLineCount - b.DNA.TotalLineCount);
        var maxLines = Math.Max(a.DNA.TotalLineCount, b.DNA.TotalLineCount);
        if (maxLines > 0)
            score += 15 * (1 - (double)lineDiff / maxLines);

        return maxScore > 0 ? score / maxScore : 0;
    }

    /// <summary>
    /// Get aggregate statistics across all estimates
    /// </summary>
    public HistoryStatistics GetOverallStatistics()
    {
        var stats = new HistoryStatistics
        {
            TotalEstimates = _data.Estimates.Count,
            TotalLineItems = _data.Estimates.Sum(e => e.LineItems.Count),
            TotalManualLines = _data.Estimates.Sum(e => e.LineItems.Count(i => i.IsManualLine)),
            TotalValue = _data.Estimates.Sum(e => e.GrandTotal),
            UniqueInsurers = _data.Estimates
                .Select(e => e.InsuranceCompany)
                .Where(i => !string.IsNullOrWhiteSpace(i) && i != "Unknown")
                .Distinct()
                .Count()
        };

        // Top insurers by volume
        stats.TopInsurers = _data.Estimates
            .Where(e => !string.IsNullOrWhiteSpace(e.InsuranceCompany) && e.InsuranceCompany != "Unknown")
            .GroupBy(e => e.InsuranceCompany)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        // Most common operations
        stats.TopOperations = _data.Estimates
            .SelectMany(e => e.LineItems)
            .Where(i => i.IsManualLine || i.IsAdditionalOperation)
            .GroupBy(i => NormalizeOperationKey(i.Description))
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderByDescending(g => g.Count())
            .Take(20)
            .ToDictionary(g => g.Key, g => g.Count());

        // Average complexity
        if (_data.Estimates.Count > 0)
            stats.AverageComplexity = _data.Estimates.Average(e => e.DNA.ComplexityScore);

        return stats;
    }

    /// <summary>
    /// Search estimates by various criteria
    /// </summary>
    public List<StoredEstimate> SearchEstimates(EstimateSearchCriteria criteria)
    {
        var query = _data.Estimates.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(criteria.InsuranceCompany))
            query = query.Where(e => e.InsuranceCompany.Contains(criteria.InsuranceCompany, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(criteria.VehicleKeyword))
            query = query.Where(e => e.VehicleInfo.Contains(criteria.VehicleKeyword, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(criteria.OperationKeyword))
            query = query.Where(e => e.LineItems.Any(i =>
                i.Description?.Contains(criteria.OperationKeyword, StringComparison.OrdinalIgnoreCase) == true));

        if (criteria.MinTotal.HasValue)
            query = query.Where(e => e.GrandTotal >= criteria.MinTotal.Value);

        if (criteria.MaxTotal.HasValue)
            query = query.Where(e => e.GrandTotal <= criteria.MaxTotal.Value);

        if (criteria.FromDate.HasValue)
            query = query.Where(e => e.ImportedDate >= criteria.FromDate.Value);

        if (criteria.ToDate.HasValue)
            query = query.Where(e => e.ImportedDate <= criteria.ToDate.Value);

        if (criteria.MinComplexity.HasValue)
            query = query.Where(e => e.DNA.ComplexityScore >= criteria.MinComplexity.Value);

        return query.OrderByDescending(e => e.ImportedDate).ToList();
    }

    #region Baseline Merge

    /// <summary>
    /// Merge baseline estimate history data into this database.
    /// Uses a signature (vehicle type + insurer + line count + grand total) to detect duplicates.
    /// For OperationPaymentIndex, keeps whichever entry has higher TimesPaid.
    /// </summary>
    public void MergeBaseline(EstimateHistoryData baseline)
    {
        if (baseline == null) return;

        var existingSignatures = _data.Estimates
            .Select(e => GenerateEstimateSignature(e))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var estimate in baseline.Estimates)
        {
            var sig = GenerateEstimateSignature(estimate);
            if (!existingSignatures.Contains(sig))
            {
                _data.Estimates.Add(estimate);
                existingSignatures.Add(sig);
                added++;
            }
        }

        // Merge OperationPaymentIndex
        foreach (var (insurerKey, baselineOps) in baseline.OperationPaymentIndex)
        {
            if (!_data.OperationPaymentIndex.ContainsKey(insurerKey))
            {
                _data.OperationPaymentIndex[insurerKey] = new Dictionary<string, OperationPaymentStats>();
            }

            var existingOps = _data.OperationPaymentIndex[insurerKey];
            foreach (var (opKey, baselineStats) in baselineOps)
            {
                if (!existingOps.ContainsKey(opKey))
                {
                    existingOps[opKey] = baselineStats;
                }
                else if (baselineStats.TimesPaid > existingOps[opKey].TimesPaid)
                {
                    existingOps[opKey] = baselineStats;
                }
            }
        }

        if (added > 0)
        {
            SaveDatabase();
            System.Diagnostics.Debug.WriteLine($"[EstimateHistory] Merged baseline: added {added} estimates");
        }
    }

    /// <summary>
    /// Generate a signature for duplicate detection during baseline merge.
    /// Uses vehicle type + insurer + line count + grand total (rounded).
    /// </summary>
    private string GenerateEstimateSignature(StoredEstimate estimate)
    {
        var vehicle = (estimate.VehicleInfo ?? "").Trim().ToLowerInvariant();
        var insurer = (estimate.InsuranceCompany ?? "").Trim().ToLowerInvariant();
        var lineCount = estimate.LineItems.Count;
        var total = Math.Round(estimate.GrandTotal, 0);
        return $"{vehicle}|{insurer}|{lineCount}|{total}";
    }

    /// <summary>
    /// Get the raw history data (for baseline export)
    /// </summary>
    public EstimateHistoryData GetRawData() => _data;

    #endregion

    /// <summary>
    /// Get all estimates in the database (for mining/analysis)
    /// </summary>
    public List<StoredEstimate> GetAllEstimates()
    {
        return _data.Estimates.ToList();
    }

    /// <summary>
    /// Get estimate by ID
    /// </summary>
    public StoredEstimate? GetEstimateById(string id)
    {
        return _data.Estimates.FirstOrDefault(e => e.Id == id);
    }

    #endregion
}

#region Data Models

public class EstimateHistoryData
{
    public List<StoredEstimate> Estimates { get; set; } = new();
    public Dictionary<string, Dictionary<string, OperationPaymentStats>> OperationPaymentIndex { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}

public class StoredEstimate
{
    public string Id { get; set; } = "";
    public DateTime ImportedDate { get; set; }
    public string SourceFile { get; set; } = "";
    public string EstimateSource { get; set; } = ""; // CCC, Mitchell, Audatex
    public string InsuranceCompany { get; set; } = "";
    public string RONumber { get; set; } = "";
    public string ClaimNumber { get; set; } = "";
    public string VehicleInfo { get; set; } = "";
    public string VehicleType { get; set; } = ""; // sedan, truck, suv, etc. (for pattern matching)
    public string VIN { get; set; } = "";
    public decimal GrandTotal { get; set; }
    public decimal PartsTotal { get; set; }
    public decimal LaborTotal { get; set; }
    public decimal PaintTotal { get; set; }
    public decimal TotalAmount => GrandTotal; // Alias for compatibility
    public List<StoredLineItem> LineItems { get; set; } = new();
    public EstimateDNA DNA { get; set; } = new();
    public int SupplementCount { get; set; }
    public string? Notes { get; set; }
    public int QualityScore { get; set; }
    public string QualityGrade { get; set; } = "";
}

public class StoredLineItem
{
    public string Description { get; set; } = "";
    public string PartName { get; set; } = "";
    public string OperationType { get; set; } = "";
    public string Operation { get => OperationType; set => OperationType = value; } // Alias for compatibility
    public string Section { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public decimal LaborHours { get; set; }
    public decimal RefinishHours { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
    public string LaborType { get; set; } = "";
    public bool IsManualLine { get; set; }
    public bool IsAdditionalOperation { get; set; }
    public string? ParentPartName { get; set; }
    public bool WasPaid { get; set; } = true;
    public bool WasDenied { get; set; }
    public string? DenialReason { get; set; }
}

public class EstimateDNA
{
    public int TotalLineCount { get; set; }
    public int ManualLineCount { get; set; }
    public int AdditionalOpCount { get; set; }
    public int ComplexityScore { get; set; }
    public double ManualLineRatio { get; set; }
    public List<string> DamageZones { get; set; } = new();
    public Dictionary<string, int> SectionBreakdown { get; set; } = new();
    public Dictionary<string, int> OperationTypes { get; set; } = new();
    public List<string> RiskFlags { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
}

public class OperationPaymentStats
{
    public string OperationDescription { get; set; } = "";
    public string NormalizedKey { get; set; } = "";
    public int TimesPaid { get; set; }
    public decimal TotalHoursPaid { get; set; }
    public decimal TotalAmountPaid { get; set; }
    public decimal AverageHours { get; set; }
    public List<string> EstimateIds { get; set; } = new();
}

public class OperationPaymentSummary
{
    public string InsuranceCompany { get; set; } = "";
    public string OperationKeyword { get; set; } = "";
    public int TimesPaid { get; set; }
    public decimal TotalAmountPaid { get; set; }
    public decimal TotalHoursPaid { get; set; }
    public decimal AverageHours { get; set; }
    public List<string> EstimateIds { get; set; } = new();
}

public class EstimateSearchResult
{
    public StoredEstimate Estimate { get; set; } = null!;
    public List<StoredLineItem> MatchingLineItems { get; set; } = new();
    public string MatchReason { get; set; } = "";
}

public class SimilarEstimateMatch
{
    public StoredEstimate Estimate { get; set; } = null!;
    public double SimilarityScore { get; set; }
    public List<string> CommonDamageZones { get; set; } = new();
    public int ComplexityDifference { get; set; }
}

public class HistoryStatistics
{
    public int TotalEstimates { get; set; }
    public int TotalLineItems { get; set; }
    public int TotalManualLines { get; set; }
    public decimal TotalValue { get; set; }
    public int UniqueInsurers { get; set; }
    public double AverageComplexity { get; set; }
    public Dictionary<string, int> TopInsurers { get; set; } = new();
    public Dictionary<string, int> TopOperations { get; set; } = new();
}

public class EstimateSearchCriteria
{
    public string? InsuranceCompany { get; set; }
    public string? VehicleKeyword { get; set; }
    public string? OperationKeyword { get; set; }
    public decimal? MinTotal { get; set; }
    public decimal? MaxTotal { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? MinComplexity { get; set; }
}

#endregion
