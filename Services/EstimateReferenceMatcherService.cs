#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using McstudDesktop.Models;
using McStudDesktop.Views;

namespace McStudDesktop.Services;

/// <summary>
/// Auto-matches estimate operations against reference data (Definitions, P-Pages, DEG, Procedures, Included/Not Included)
/// and adds matched items to the PDF Export Queue.
/// </summary>
public sealed class EstimateReferenceMatcherService
{
    private static EstimateReferenceMatcherService? _instance;
    public static EstimateReferenceMatcherService Instance => _instance ??= new EstimateReferenceMatcherService();

    // Reference data
    private List<RefDefinition>? _definitions;
    private List<RefPPage>? _ppages;
    private List<RefDEGInquiry>? _degInquiries;
    private List<RefProcedure>? _procedures;
    private List<RefIncludedNotIncluded>? _includedNotIncluded;
    private bool _dataLoaded;

    // Stop words to filter out noise
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "labor", "total", "qty", "quantity", "subtotal", "tax", "amount", "price",
        "hours", "hour", "rate", "each", "per", "net", "gross", "discount",
        "deductible", "supplement", "estimate", "page", "line", "item",
        "left", "right", "front", "rear", "upper", "lower", "inner", "outer",
        "lt", "rt", "lf", "rf", "lr", "rr", "fr", "assy", "assembly",
        "w/", "w/o", "with", "without", "the", "and", "for", "from", "into"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private EstimateReferenceMatcherService()
    {
    }

    /// <summary>
    /// Match parsed estimate lines against reference data and add to PDF queue.
    /// Returns the number of items added.
    /// </summary>
    public async Task<MatchResult> MatchEstimateLinesAsync(List<ParsedEstimateLine> lines)
    {
        EnsureDataLoaded();
        var terms = ExtractSearchTermsFromEstimate(lines);
        return await Task.Run(() => MatchTermsAndQueue(terms));
    }

    /// <summary>
    /// Match OCR result operations against reference data and add to PDF queue.
    /// Returns the number of items added.
    /// </summary>
    public async Task<MatchResult> MatchOcrResultAsync(ScreenOcrResult ocrResult)
    {
        EnsureDataLoaded();
        var terms = ExtractSearchTermsFromOcr(ocrResult);
        return await Task.Run(() => MatchTermsAndQueue(terms));
    }

    private List<string> ExtractSearchTermsFromEstimate(List<ParsedEstimateLine> lines)
    {
        var terms = new List<string>();
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line.PartName))
            {
                var cleaned = CleanTerm(line.PartName);
                if (!string.IsNullOrWhiteSpace(cleaned))
                    terms.Add(cleaned);
            }

            if (!string.IsNullOrWhiteSpace(line.OperationType))
            {
                // Combine part name + operation type for better matching
                var combined = $"{line.PartName} {line.OperationType}".Trim();
                if (!string.IsNullOrWhiteSpace(combined))
                    terms.Add(CleanTerm(combined));
            }

            if (!string.IsNullOrWhiteSpace(line.Description))
            {
                var cleaned = CleanTerm(line.Description);
                if (!string.IsNullOrWhiteSpace(cleaned) && cleaned != CleanTerm(line.PartName))
                    terms.Add(cleaned);
            }
        }

        return terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<string> ExtractSearchTermsFromOcr(ScreenOcrResult result)
    {
        var terms = new List<string>();
        foreach (var op in result.DetectedOperations)
        {
            if (!string.IsNullOrWhiteSpace(op.PartName))
            {
                var cleaned = CleanTerm(op.PartName);
                if (!string.IsNullOrWhiteSpace(cleaned))
                    terms.Add(cleaned);
            }

            if (!string.IsNullOrWhiteSpace(op.OperationType) && !string.IsNullOrWhiteSpace(op.PartName))
            {
                var combined = $"{op.PartName} {op.OperationType}".Trim();
                terms.Add(CleanTerm(combined));
            }

            if (!string.IsNullOrWhiteSpace(op.Description))
            {
                var cleaned = CleanTerm(op.Description);
                if (!string.IsNullOrWhiteSpace(cleaned) && cleaned != CleanTerm(op.PartName))
                    terms.Add(cleaned);
            }
        }

        return terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string CleanTerm(string term)
    {
        // Remove side/position prefixes, numbers, and common noise
        var cleaned = term.Trim();

        // Strip leading digits and whitespace (OCR noise like "4 Front Bumper" → "Front Bumper")
        cleaned = Regex.Replace(cleaned, @"^\d+\s+", "");
        // Strip trailing standalone digits
        cleaned = Regex.Replace(cleaned, @"\s+\d+\s*$", "");
        // Strip stray asterisks from OCR
        cleaned = cleaned.Replace("*", "").Trim();

        // Remove leading side designations
        var prefixes = new[] { "LT ", "RT ", "LF ", "RF ", "LR ", "RR ", "FR ", "L ", "R " };
        foreach (var prefix in prefixes)
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[prefix.Length..].Trim();
                break;
            }
        }

        return cleaned;
    }

    private bool IsStopWordOnly(string term)
    {
        var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.All(w => StopWords.Contains(w) || w.Length <= 2);
    }

    private MatchResult MatchTermsAndQueue(List<string> terms)
    {
        var result = new MatchResult();
        var itemsToAdd = new List<PdfQueueItem>();
        var crossRefPPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceMap = new Dictionary<string, string>(); // Id -> MatchSource

        foreach (var term in terms)
        {
            if (IsStopWordOnly(term)) continue;

            // 1. IncludedNotIncluded — best for part names
            MatchIncludedNotIncluded(term, itemsToAdd, crossRefPPages, result);

            // 2. Definitions
            MatchDefinitions(term, itemsToAdd, crossRefPPages, result);

            // 3. P-Pages
            MatchPPages(term, itemsToAdd, result);

            // 4. DEG Inquiries
            MatchDEGInquiries(term, itemsToAdd, crossRefPPages, result);

            // 5. Procedures
            MatchProcedures(term, itemsToAdd, crossRefPPages, result);
        }

        // Track sources for items added so far (before cross-refs)
        foreach (var item in itemsToAdd)
            sourceMap[item.Id] = InferMatchSource(item.Id);

        // 6. Cross-references: add P-Pages referenced by other matches
        var preCount = itemsToAdd.Count;
        ResolveCrossReferences(crossRefPPages, itemsToAdd, result);

        // Tag cross-ref items
        for (int i = preCount; i < itemsToAdd.Count; i++)
            sourceMap[itemsToAdd[i].Id] = "Cross-Reference";

        // Build staged items instead of adding directly to queue
        foreach (var item in itemsToAdd)
        {
            sourceMap.TryGetValue(item.Id, out var source);
            result.Items.Add(new StagedReferenceItem
            {
                QueueItem = item,
                MatchSource = source ?? "Reference",
                IsSelected = true
            });
        }

        Debug.WriteLine($"[ReferenceMatcher] Matched {result.MatchedTerms.Count} terms, staged {result.Items.Count} items for review");
        return result;
    }

    private static string InferMatchSource(string id)
    {
        if (id.StartsWith("inclnotincl-")) return "Included/Not Included";
        if (id.StartsWith("def-")) return "Definition";
        if (id.StartsWith("ppage-")) return "P-Page";
        if (id.StartsWith("deg-")) return "DEG Inquiry";
        if (id.StartsWith("proc-")) return "Procedure";
        return "Reference";
    }

    // === MATCHING METHODS ===

    private void MatchIncludedNotIncluded(string term, List<PdfQueueItem> items, HashSet<string> crossRefPPages, MatchResult result)
    {
        if (_includedNotIncluded == null) return;

        foreach (var op in _includedNotIncluded)
        {
            bool matched = TermMatchesText(term, op.PartName)
                || (op.Aliases != null && op.Aliases.Any(a => TermMatchesText(term, a)));

            if (matched)
            {
                var id = $"inclnotincl-{op.Id}";
                if (items.Any(i => i.Id == id)) continue;

                // Build definition text from included/not included lists
                var defParts = new List<string>();
                if (op.Included != null && op.Included.Count > 0)
                    defParts.Add("INCLUDED:\n" + string.Join("\n", op.Included.Select(i => $"  + {i}")));
                if (op.NotIncluded != null && op.NotIncluded.Count > 0)
                    defParts.Add("NOT INCLUDED:\n" + string.Join("\n", op.NotIncluded.Select(i => $"  - {i}")));

                items.Add(new PdfQueueItem
                {
                    Id = id,
                    Term = $"{op.PartName} ({op.OperationType})",
                    Category = "Included/Not Included",
                    Definition = string.Join("\n\n", defParts),
                    Details = op.ScrsNotes,
                    PPageRef = op.SourceRefs?.CccMotor,
                    Status = "Operations Reference"
                });

                result.MatchedTerms.Add(op.PartName ?? term);

                // Collect P-Page cross-references
                if (!string.IsNullOrEmpty(op.SourceRefs?.CccMotor))
                {
                    foreach (var pref in op.SourceRefs.CccMotor.Split(',', StringSplitOptions.TrimEntries))
                        crossRefPPages.Add(pref);
                }
            }
        }
    }

    private void MatchDefinitions(string term, List<PdfQueueItem> items, HashSet<string> crossRefPPages, MatchResult result)
    {
        if (_definitions == null) return;

        foreach (var def in _definitions)
        {
            if (TermMatchesText(term, def.Term))
            {
                var id = $"def-{def.Id}";
                if (items.Any(i => i.Id == id)) continue;

                items.Add(new PdfQueueItem
                {
                    Id = id,
                    Term = def.Term ?? "Unknown",
                    Category = def.Category ?? "General",
                    Definition = def.Definition ?? "",
                    Details = def.Details,
                    PPageRef = def.PPageRef,
                    PPageLocation = def.PPageLocation,
                    DegInquiry = def.DegInquiry,
                    DegResponse = def.DegResponse,
                    Status = def.Status
                });

                result.MatchedTerms.Add(def.Term ?? term);

                if (!string.IsNullOrEmpty(def.PPageRef))
                    crossRefPPages.Add(def.PPageRef);
            }
        }
    }

    private void MatchPPages(string term, List<PdfQueueItem> items, MatchResult result)
    {
        if (_ppages == null) return;

        foreach (var pp in _ppages)
        {
            bool matched = TermMatchesText(term, pp.Title)
                || (pp.Tags != null && pp.Tags.Any(t => TermMatchesText(term, t)));

            if (matched)
            {
                var id = $"ppage-{pp.Id}";
                if (items.Any(i => i.Id == id)) continue;

                var defParts = new List<string>();
                if (!string.IsNullOrEmpty(pp.Summary))
                    defParts.Add(pp.Summary);
                if (pp.Included != null && pp.Included.Count > 0)
                    defParts.Add("INCLUDED:\n" + string.Join("\n", pp.Included.Select(i => $"  + {i}")));
                if (pp.NotIncluded != null && pp.NotIncluded.Count > 0)
                    defParts.Add("NOT INCLUDED:\n" + string.Join("\n", pp.NotIncluded.Select(i => $"  - {i}")));

                items.Add(new PdfQueueItem
                {
                    Id = id,
                    Term = $"P-Page {pp.Section}: {pp.Title}",
                    Category = pp.Category ?? "P-Pages",
                    Definition = string.Join("\n\n", defParts),
                    Details = pp.Notes,
                    PPageRef = pp.Section,
                    Status = "P-Page Reference"
                });

                result.MatchedTerms.Add(pp.Title ?? term);
            }
        }
    }

    private void MatchDEGInquiries(string term, List<PdfQueueItem> items, HashSet<string> crossRefPPages, MatchResult result)
    {
        if (_degInquiries == null) return;

        foreach (var deg in _degInquiries)
        {
            bool matched = TermMatchesText(term, deg.Title)
                || (deg.Tags != null && deg.Tags.Any(t => TermMatchesText(term, t)));

            if (matched)
            {
                var id = $"deg-{deg.Id}";
                if (items.Any(i => i.Id == id)) continue;

                items.Add(new PdfQueueItem
                {
                    Id = id,
                    Term = $"DEG #{deg.InquiryNumber}: {deg.Title}",
                    Category = deg.Category ?? "DEG Inquiry",
                    Definition = deg.Response ?? "",
                    Details = deg.KeyPoints != null ? string.Join("\n", deg.KeyPoints.Select(k => $"- {k}")) : null,
                    DegInquiry = deg.InquiryNumber,
                    PPageRef = deg.PPageRef,
                    Status = "DEG Inquiry"
                });

                result.MatchedTerms.Add(deg.Title ?? term);

                if (!string.IsNullOrEmpty(deg.PPageRef))
                    crossRefPPages.Add(deg.PPageRef);
            }
        }
    }

    private void MatchProcedures(string term, List<PdfQueueItem> items, HashSet<string> crossRefPPages, MatchResult result)
    {
        if (_procedures == null) return;

        foreach (var proc in _procedures)
        {
            bool matched = TermMatchesText(term, proc.Name)
                || (proc.Tags != null && proc.Tags.Any(t => TermMatchesText(term, t)));

            if (matched)
            {
                var id = $"proc-{proc.Id}";
                if (items.Any(i => i.Id == id)) continue;

                var defParts = new List<string>();
                if (!string.IsNullOrEmpty(proc.Description))
                    defParts.Add(proc.Description);
                if (proc.Steps != null && proc.Steps.Count > 0)
                    defParts.Add("STEPS:\n" + string.Join("\n", proc.Steps.Select((s, idx) => $"  {idx + 1}. {s}")));
                if (!string.IsNullOrEmpty(proc.TimeGuideline))
                    defParts.Add($"Time: {proc.TimeGuideline}");

                items.Add(new PdfQueueItem
                {
                    Id = id,
                    Term = proc.Name ?? "Procedure",
                    Category = proc.Category ?? "Procedures",
                    Definition = string.Join("\n\n", defParts),
                    Details = proc.Notes,
                    PPageRef = proc.PPageRef,
                    Status = "Procedure"
                });

                result.MatchedTerms.Add(proc.Name ?? term);

                if (!string.IsNullOrEmpty(proc.PPageRef))
                    crossRefPPages.Add(proc.PPageRef);
            }
        }
    }

    private void ResolveCrossReferences(HashSet<string> pPageRefs, List<PdfQueueItem> items, MatchResult result)
    {
        if (_ppages == null || pPageRefs.Count == 0) return;

        foreach (var pref in pPageRefs)
        {
            var pp = _ppages.FirstOrDefault(p =>
                string.Equals(p.Section, pref, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Id, pref, StringComparison.OrdinalIgnoreCase));

            if (pp == null) continue;

            var id = $"ppage-{pp.Id}";
            if (items.Any(i => i.Id == id)) continue;

            var defParts = new List<string>();
            if (!string.IsNullOrEmpty(pp.Summary))
                defParts.Add(pp.Summary);
            if (pp.Included != null && pp.Included.Count > 0)
                defParts.Add("INCLUDED:\n" + string.Join("\n", pp.Included.Select(i => $"  + {i}")));
            if (pp.NotIncluded != null && pp.NotIncluded.Count > 0)
                defParts.Add("NOT INCLUDED:\n" + string.Join("\n", pp.NotIncluded.Select(i => $"  - {i}")));

            items.Add(new PdfQueueItem
            {
                Id = id,
                Term = $"P-Page {pp.Section}: {pp.Title}",
                Category = pp.Category ?? "P-Pages",
                Definition = string.Join("\n\n", defParts),
                Details = pp.Notes,
                PPageRef = pp.Section,
                Status = "P-Page (Cross-Reference)"
            });

            result.CrossRefCount++;
        }
    }

    // === TERM MATCHING ===

    private static bool TermMatchesText(string searchTerm, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(searchTerm))
            return false;

        // Exact contains match (case-insensitive)
        if (text.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            return true;

        if (searchTerm.Contains(text, StringComparison.OrdinalIgnoreCase))
            return true;

        // Word-level matching: if 2+ significant words from the search term appear in the text
        var searchWords = searchTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !StopWords.Contains(w) && w.Length > 2)
            .ToList();

        if (searchWords.Count >= 2)
        {
            var matchCount = searchWords.Count(w =>
                text.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (matchCount >= Math.Min(2, searchWords.Count))
                return true;
        }

        return false;
    }

    // === DATA LOADING ===

    private void EnsureDataLoaded()
    {
        if (_dataLoaded) return;

        try
        {
            var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");

            _definitions = LoadDefinitions(Path.Combine(dataDir, "Definitions.json"));
            _ppages = LoadPPages(Path.Combine(dataDir, "PPages.json"));
            _degInquiries = LoadDEGInquiries(Path.Combine(dataDir, "DEGInquiries.json"));
            _procedures = LoadProcedures(Path.Combine(dataDir, "Procedures.json"));
            _includedNotIncluded = LoadIncludedNotIncluded(Path.Combine(dataDir, "IncludedNotIncluded.json"));

            _dataLoaded = true;
            Debug.WriteLine($"[ReferenceMatcher] Loaded: {_definitions?.Count ?? 0} defs, {_ppages?.Count ?? 0} ppages, " +
                $"{_degInquiries?.Count ?? 0} DEGs, {_procedures?.Count ?? 0} procs, {_includedNotIncluded?.Count ?? 0} incl/not incl");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReferenceMatcher] Data load error: {ex.Message}");
        }
    }

    private List<RefDefinition>? LoadDefinitions(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<DefinitionsData>(json, JsonOptions);
        return data?.Definitions;
    }

    private List<RefPPage>? LoadPPages(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<PPagesData>(json, JsonOptions);
        return data?.Sections;
    }

    private List<RefDEGInquiry>? LoadDEGInquiries(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<DEGData>(json, JsonOptions);
        return data?.Inquiries;
    }

    private List<RefProcedure>? LoadProcedures(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<ProceduresData>(json, JsonOptions);
        return data?.Procedures;
    }

    private List<RefIncludedNotIncluded>? LoadIncludedNotIncluded(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<IncludedNotIncludedData>(json, JsonOptions);
        return data?.Operations;
    }

    // === JSON DATA MODELS (private, for deserialization only) ===

    private class DefinitionsData
    {
        public List<RefDefinition>? Definitions { get; set; }
    }

    private class PPagesData
    {
        public List<RefPPage>? Sections { get; set; }
    }

    private class DEGData
    {
        public List<RefDEGInquiry>? Inquiries { get; set; }
    }

    private class ProceduresData
    {
        public List<RefProcedure>? Procedures { get; set; }
    }

    private class IncludedNotIncludedData
    {
        public List<RefIncludedNotIncluded>? Operations { get; set; }
    }
}

// === Reference data models ===

public class RefDefinition
{
    public string? Id { get; set; }
    public string? Term { get; set; }
    public string? Category { get; set; }
    public string? Status { get; set; }
    public string? PPageLocation { get; set; }
    public string? PPageRef { get; set; }
    public string? Definition { get; set; }
    public string? Details { get; set; }
    public string? DegInquiry { get; set; }
    public string? DegResponse { get; set; }
}

public class RefPPage
{
    public string? Id { get; set; }
    public string? Section { get; set; }
    public string? PPageRef { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public string? Summary { get; set; }
    public List<string>? Included { get; set; }
    public List<string>? NotIncluded { get; set; }
    public string? Notes { get; set; }
    public List<string>? Tags { get; set; }
}

public class RefDEGInquiry
{
    public string? Id { get; set; }
    public string? InquiryNumber { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public string? Response { get; set; }
    public List<string>? KeyPoints { get; set; }
    public string? PPageRef { get; set; }
    public List<string>? Tags { get; set; }
}

public class RefProcedure
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? PPageRef { get; set; }
    public string? Description { get; set; }
    public List<string>? Steps { get; set; }
    public string? TimeGuideline { get; set; }
    public string? Notes { get; set; }
    public List<string>? Tags { get; set; }
}

public class RefIncludedNotIncluded
{
    public string? Id { get; set; }
    public string? PartName { get; set; }
    public string? OperationType { get; set; }
    public List<string>? Aliases { get; set; }
    public RefSourceRefs? SourceRefs { get; set; }
    public List<string>? Included { get; set; }
    public List<string>? NotIncluded { get; set; }
    public string? ScrsNotes { get; set; }
}

public class RefSourceRefs
{
    public string? CccMotor { get; set; }
    public string? Mitchell { get; set; }
    public List<string>? DegInquiries { get; set; }
}

/// <summary>
/// A reference item staged for user review before adding to PDF queue
/// </summary>
public class StagedReferenceItem
{
    public PdfQueueItem QueueItem { get; set; } = new();
    public string MatchSource { get; set; } = "Reference";
    public bool IsSelected { get; set; } = true;
}

/// <summary>
/// Result of a reference matching operation
/// </summary>
public class MatchResult
{
    public int AddedCount { get; set; }
    public int CrossRefCount { get; set; }
    public List<string> MatchedTerms { get; set; } = new();
    public List<StagedReferenceItem> Items { get; set; } = new();

    public string Summary => Items.Count > 0
        ? $"Found {Items.Count} reference matches ({MatchedTerms.Count} terms matched, {CrossRefCount} cross-refs)"
        : "No matching reference items found";
}
