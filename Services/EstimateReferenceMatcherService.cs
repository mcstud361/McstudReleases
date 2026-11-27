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
        "w/", "w/o", "with", "without", "the", "and", "for", "from", "into",
        "after", "before", "during"
    };

    // Single-word terms that are too generic to trigger Level 2 (reverse) matching.
    // Group 1: operational verbs — appear on nearly every estimate line.
    // Group 2: common part / location nouns — shared across unrelated reference topics.
    //          e.g. "panel" as a tag on a welding DEG must NOT pull that DEG into a
    //          bumper-only estimate just because "panel" appears in "Bumper Panel".
    // Specific process terms (weld, blend, scan, calibrate, etc.) are intentionally
    // left out — if the estimate explicitly says "weld" or "blend", that IS relevant.
    private static readonly HashSet<string> GenericSingleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Operational verbs
        "repair", "replace", "remove", "install", "paint", "clean", "test", "check",
        // Generic part / location nouns
        "panel", "cover", "bumper", "fender", "door", "hood", "roof", "trunk", "lid",
        "frame", "rail", "glass", "trim", "wire", "wiring", "body", "structural",
        "bracket", "support", "reinforcement", "bar", "beam", "shield", "liner",
        "molding", "moulding", "lamp", "light", "mirror", "handle", "hinge",
        "assembly", "component", "section", "area", "side", "part", "piece",
        // Weak qualifier words — modifiers, not topics. These are common tags on
        // reference entries (e.g. "dynamic"/"static" on ADAS calibration) and would
        // otherwise pull a whole reference in on a loose single-word hit. The poster
        // child: the routine estimate line "Dynamic Systems Verification" (a post-scan
        // step on nearly every estimate) matched the "dynamic" tag and dragged in full
        // ADAS calibration procedures the estimate never called for.
        "dynamic", "static", "combined", "standard", "universal", "manual"
    };

    // Words that are real content but too ubiquitous or ambiguous to ANCHOR a suggestion on their
    // own. They describe the kind of work nearly every estimate involves ("refinish"/"paint") or
    // are category/qualifier labels ("structural"/"stage") — so a reference item whose name only
    // overlaps the estimate on one of these is NOT specific enough to suggest. A job isn't a reason
    // to suggest every refinish procedure in the library; the item has to name something real on
    // the estimate. (These still activate work CONTEXTS via ContextTriggers — they just can't, by
    // themselves, pull a checklist item into the "items you can add" list.)
    private static readonly HashSet<string> BroadAnchorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "refinish", "refinishing", "refinished", "refn", "repaint", "repainting",
        "paint", "painting", "painted", "blend", "blending", "clearcoat", "basecoat",
        "calibration", "calibrate", "calibrating", "recalibration", "scan", "prescan",
        "postscan", "diagnostic", "diagnostics", "structural", "electrical", "mechanical",
        "stage", "coat", "coating", "operation", "operations", "procedure", "procedures",
        "vehicle", "surface", "setup",

        // Ambiguous / cross-sense words — real words that recur across the estimate in a
        // DIFFERENT meaning than the reference item intends, so a lone match on one of them is a
        // false positive (observed on real estimates): "Color Tint" (paint) vs "Window Tint"
        // (film); "Steering Wheel Cover" vs "Steering Angle Sensor"; "Aim radar" vs "Headlamp
        // Aiming"; "T-Stud" fastener vs "Stud Puller"; "high voltage system" vs "Seatbelt System";
        // "Adhesive Cleanup" vs "Airbag Residue Cleanup"; "Cure/Mixing Time"; "Pressure vent" vs
        // "Tire Pressures"; generic "Mask/Prep/Access/Collision/Disposal".
        "time", "system", "systems", "tint", "steering", "aim", "aiming", "stud",
        "cleanup", "collision", "access", "disposal", "pressure", "pressures",
        "mask", "prep", "condition",
        // Second pass on real estimates: bare "sensor" pulled "Steering Angle Sensor" onto jobs
        // whose only sensor was a vent/park sensor; "equipment" pulled "Scaffolding Equipment" off
        // "Setup ADAS Equipment"; "backside" pulled "Mask Backside of Bumper for Radar" off a vent
        // panel's "Refinish backside". (A real radar-behind-bumper job still matches via "radar".)
        "sensor", "equipment", "backside",
        // "oem" recurs everywhere ("OEM parts", "OEM Appearance", "OEM Research") — on its own it
        // falsely confirmed "Structural Sectioning per OEM Procedure".
        "oem",
        // Third pass: "window" matched power windows / a window regulator (→ "Window Tint Removal");
        // "airbag(s)" matched the vehicle's safety-equipment list and ADAS scan lines, not a deployed
        // airbag (→ "Airbag Residue Cleanup" / "SRS Disposal"). Both are the wrong sense.
        // "seatbelt(s)" is the same story — a "Seatbelt System" check line is not SRS disposal.
        "window", "windows", "airbag", "airbags", "seatbelt", "seatbelts"
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
    public async Task<MatchResult> MatchEstimateLinesAsync(List<ParsedEstimateLine> lines, string? estimateSource = null)
    {
        EnsureDataLoaded();
        var terms = ExtractSearchTermsFromEstimate(lines, out var termLines, out var termOps);
        return await Task.Run(() => MatchTermsAndQueue(terms, termLines, termOps, estimateSource));
    }

    /// <summary>
    /// Match OCR result operations against reference data and add to PDF queue.
    /// Returns the number of items added.
    /// </summary>
    public async Task<MatchResult> MatchOcrResultAsync(ScreenOcrResult ocrResult)
    {
        EnsureDataLoaded();
        var terms = ExtractSearchTermsFromOcr(ocrResult);
        // OCR has no reliable line numbers / op codes — pass empty maps (no line tags, no op gating).
        return await Task.Run(() => MatchTermsAndQueue(terms,
            new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase), null));
    }

    private List<string> ExtractSearchTermsFromEstimate(List<ParsedEstimateLine> lines, out Dictionary<string, SortedSet<int>> termLines, out Dictionary<string, HashSet<string>> termOps)
    {
        var terms = new List<string>();
        var lineMap = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
        var opMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        termLines = lineMap;
        termOps = opMap;

        void Add(string? cleaned, int lineNo, string? op)
        {
            if (string.IsNullOrWhiteSpace(cleaned)) return;
            terms.Add(cleaned);
            if (lineNo > 0)
            {
                if (!lineMap.TryGetValue(cleaned, out var set)) { set = new SortedSet<int>(); lineMap[cleaned] = set; }
                set.Add(lineNo);
            }
            if (!string.IsNullOrWhiteSpace(op))
            {
                if (!opMap.TryGetValue(cleaned, out var ops)) { ops = new HashSet<string>(StringComparer.OrdinalIgnoreCase); opMap[cleaned] = ops; }
                ops.Add(op);
            }
        }

        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line.PartName))
                Add(CleanTerm(line.PartName), line.LineNumber, line.OperationType);

            if (!string.IsNullOrWhiteSpace(line.OperationType))
                Add(CleanTerm($"{line.PartName} {line.OperationType}".Trim()), line.LineNumber, line.OperationType);

            if (!string.IsNullOrWhiteSpace(line.Description))
            {
                var cleaned = CleanTerm(line.Description);
                if (!string.IsNullOrWhiteSpace(cleaned) && cleaned != CleanTerm(line.PartName))
                    Add(cleaned, line.LineNumber, line.OperationType);
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

    private MatchResult MatchTermsAndQueue(List<string> terms, Dictionary<string, SortedSet<int>> termLines, Dictionary<string, HashSet<string>> termOps, string? estimateSource)
    {
        var result = new MatchResult();
        var itemsToAdd = new List<PdfQueueItem>();
        var crossRefPPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceMap = new Dictionary<string, string>(); // Id -> MatchSource
        var matchedInclOps = new List<RefIncludedNotIncluded>(); // for NotIncluded gap analysis
        var opLines = new Dictionary<string, SortedSet<int>>(); // op.Id -> estimate line numbers that matched it

        foreach (var term in terms)
        {
            if (IsStopWordOnly(term)) continue;

            // 1. IncludedNotIncluded — primary anchor: matches specific part+operation combos
            //    and collects the P-Page cross-refs that are valid for this operation type.
            MatchIncludedNotIncluded(term, itemsToAdd, crossRefPPages, result, matchedInclOps, termLines, termOps, opLines);

            // 2. Definitions — specific multi-word technical terms (adhesion promoter, blend, etc.)
            MatchDefinitions(term, itemsToAdd, crossRefPPages, result);

            // 3. DEG Inquiries — specific inquiry topics
            MatchDEGInquiries(term, itemsToAdd, crossRefPPages, result);

            // 4. Procedures — specific shop procedure names
            MatchProcedures(term, itemsToAdd, crossRefPPages, result);

            // NOTE: P-Pages are intentionally NOT directly matched here.
            // P-Pages are reference chapters (G4, G5, G33, etc.) that apply to specific
            // operations — not to loose keyword matches. They are added exclusively via
            // cross-references earned by IncludedNotIncluded / Definition / DEG matches above.
            // This prevents G9 (Diagnostics), G10 (Structural), G20 (Wiring) etc. from
            // appearing on every estimate just because a tag loosely overlaps.
        }

        // Track sources for items added so far (before cross-refs)
        foreach (var item in itemsToAdd)
            sourceMap[item.Id] = InferMatchSource(item.Id);

        // 5. Cross-references: resolve the P-Pages that were earned by matched operations
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

        // Requirement 2: build the "items you can add" list, driven entirely by the curated
        // reference libraries (no learned-pattern setup). Two sources, shared dedup:
        //   1. NotIncluded gaps — for each matched operation, what that labor does NOT cover.
        //   2. Full reference checklist — every reference-tab item (De-Nib, etc.) not already on
        //      the estimate. If an item is already present, it's skipped.
        // Suggestions come from the curated Not-Included data (accurate, operation-anchored, cited).
        // The checklist now only confirms what's ALREADY on the estimate (the Present list) — it no
        // longer emits keyword-anchored suggestions, which were the whack-a-mole false-positive
        // source. Separate budgets so the (large) curated suggestion set can't starve the Present list.
        const int MaxSuggestions = 40;
        const int MaxPresent = 80;
        var (blob, words) = BuildEstimateCorpus(terms);
        var activeContexts = DetectContexts(NormalizeForContext(blob));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AnalyzeNotIncludedGaps(matchedInclOps, blob, words, result, seen, MaxSuggestions, opLines, estimateSource);
        AnalyzeReferenceChecklist(blob, words, activeContexts, result, seen, MaxPresent);

        // Order both lists so related items group together: by source/category, then name.
        Comparison<SuggestedMissingItem> byCategoryThenName = (a, b) =>
        {
            int c = string.Compare(a.SourceOperation, b.SourceOperation, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Item, b.Item, StringComparison.OrdinalIgnoreCase);
        };
        result.Suggestions.Sort(byCategoryThenName);
        result.Present.Sort(byCategoryThenName);

        Debug.WriteLine($"[ReferenceMatcher] Matched {result.MatchedTerms.Count} terms, staged {result.Items.Count} items, {result.Suggestions.Count} suggested, {result.Present.Count} present");
        return result;
    }

    /// <summary>
    /// Build a lowercase text blob + significant-word set representing everything on the estimate,
    /// used to test whether a "not included" item is already present.
    /// </summary>
    private static (string Blob, HashSet<string> Words) BuildEstimateCorpus(List<string> terms)
    {
        var blob = string.Join(" | ", terms).ToLowerInvariant();
        var words = blob
            .Split(new[] { ' ', '|', '/', '-', ',', '(', ')', '.', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Select(NormalizeWord)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (blob, words);
    }

    /// <summary>
    /// Is a "not included" phrase already represented on the estimate? True (present → don't suggest)
    /// when the phrase is a direct substring, when it has no specific words to look for (all generic
    /// filler), or when every one of its specific words appears on the estimate.
    /// </summary>
    private static bool IsNotIncludedPresent(string phrase, string blob, HashSet<string> words)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return true;
        var p = phrase.ToLowerInvariant().Trim();

        // Direct substring hit (e.g. estimate literally contains "clear coat")
        if (p.Length >= 4 && blob.Contains(p)) return true;

        // Specific words = length ≥ 4, not a stop word, not a generic part/verb token.
        var specific = p
            .Split(new[] { ' ', '/', '-', ',', '(', ')', '.', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeWord)
            .Where(w => w.Length >= 4 && !StopWords.Contains(w) && !GenericSingleWords.Contains(w))
            .ToList();

        // No specific words to anchor on → too generic to suggest; treat as present.
        if (specific.Count == 0) return true;

        // Present only if EVERY specific word is on the estimate.
        return specific.All(words.Contains);
    }

    /// <summary>
    /// Produce "suggested addition" items from the NotIncluded lists of the matched operations.
    /// Capped per-operation and overall to avoid flooding the scrubber.
    /// </summary>
    private void AnalyzeNotIncludedGaps(List<RefIncludedNotIncluded> matchedOps, string blob, HashSet<string> words, MatchResult result, HashSet<string> seen, int maxTotal, Dictionary<string, SortedSet<int>> opLines, string? estimateSource)
    {
        const int MaxPerOp = 5;
        bool isCcc = string.Equals(estimateSource, "CCC", StringComparison.OrdinalIgnoreCase);
        bool isMitchell = string.Equals(estimateSource, "Mitchell", StringComparison.OrdinalIgnoreCase);

        // "line 46" / "lines 25-28, 30, 35-37" — every matched line, consecutive runs collapsed to ranges.
        string LineTag(RefIncludedNotIncluded op)
        {
            if (op.Id == null || !opLines.TryGetValue(op.Id, out var lns) || lns.Count == 0) return "";
            var nums = lns.ToList();
            var parts = new List<string>();
            int start = nums[0], prev = nums[0];
            for (int i = 1; i <= nums.Count; i++)
            {
                if (i < nums.Count && nums[i] == prev + 1) { prev = nums[i]; continue; }
                parts.Add(start == prev ? $"{start}" : $"{start}-{prev}");
                if (i < nums.Count) { start = prev = nums[i]; }
            }
            return parts.Count == 1 && !parts[0].Contains('-')
                ? $"  ·  line {parts[0]}"
                : $"  ·  lines {string.Join(", ", parts)}";
        }

        // Build each matched operation's eligible not-included items: skip anything already on the
        // estimate, and dedup globally on the CORE phrase (parenthetical citations + trailing filler
        // stripped) so "Weld-through primer (Mitchell CEG)" and "Weld-through primer application (DEG…)"
        // don't repeat across every welded panel.
        var queues = new List<(RefIncludedNotIncluded Op, List<string> Items)>();
        foreach (var op in matchedOps)
        {
            if (op.NotIncluded == null) continue;
            var eligible = new List<string>();
            foreach (var ni in op.NotIncluded)
            {
                if (eligible.Count >= MaxPerOp) break;
                if (string.IsNullOrWhiteSpace(ni)) continue;
                if (!AppliesToSource(ni, isCcc, isMitchell)) continue; // don't suggest Mitchell items on a CCC estimate (or vice-versa)
                if (IsNotIncludedPresent(ni, blob, words)) continue;
                if (!seen.Add(NormalizeNotIncludedKey(ni))) continue;
                eligible.Add(ni.Trim());
            }
            if (eligible.Count > 0) queues.Add((op, eligible));
        }

        // Round-robin across operations so EVERY matched operation contributes before any gets a
        // second item — otherwise an early-matched op (e.g. Battery) eats the budget and a key later
        // op (Quarter Panel welding, Windshield urethane) gets nothing.
        for (int round = 0; round < MaxPerOp; round++)
        {
            foreach (var (op, items) in queues)
            {
                if (result.Suggestions.Count >= maxTotal) return;
                if (round >= items.Count) continue;
                var opName = !string.IsNullOrWhiteSpace(op.OperationType)
                    && op.PartName?.IndexOf(op.OperationType!, StringComparison.OrdinalIgnoreCase) < 0
                    ? $"{op.PartName} ({op.OperationType})" : op.PartName;
                var opLabel = $"{opName}{LineTag(op)}";
                result.Suggestions.Add(new SuggestedMissingItem
                {
                    Item = items[round],
                    SourceOperation = opLabel,
                    Reason = $"Not included in {opName} — consider adding as a separate line"
                });
            }
        }
    }

    /// <summary>
    /// Does a not-included item apply to an estimate from this system? The items are sourced from
    /// CCC/MOTOR, Mitchell CEG, or the (cross-platform) DEG. A CCC estimate shouldn't be handed a
    /// Mitchell-only item (e.g. "Transfer of fog lights (Mitchell …)"), and vice-versa. Items citing
    /// DEG or nothing are system-neutral and always apply.
    /// </summary>
    /// <summary>
    /// Some "not included" entries are not concrete operations to add — they are conditional
    /// judgment notes ("requires evaluation", "by agreement among parties", "evaluate on-the-spot").
    /// Suggesting them as add-a-line items is noise: there is nothing specific to bill. These still
    /// live in the reference data (for the detail panel), they just don't seed the suggestion list.
    /// </summary>
    private static readonly string[] JudgmentOnlyMarkers =
    {
        "requires evaluation", "requires agreement", "by agreement", "among parties",
        "on-the-spot", "on the spot", "evaluate on", "requires negotiation", "if agreed",
        "case-by-case", "case by case"
    };

    private static bool IsJudgmentOnlyItem(string ni)
    {
        var t = ni.ToLowerInvariant();
        return JudgmentOnlyMarkers.Any(m => t.Contains(m));
    }

    private static bool AppliesToSource(string ni, bool isCcc, bool isMitchell)
    {
        if (!isCcc && !isMitchell) return true; // unknown/Audatex/Generic → no filtering
        var t = ni.ToLowerInvariant();
        bool mentionsMitchell = t.Contains("mitchell") || t.Contains("ceg");
        // NOTE: don't use bare "motor" — it hits part names ("mirror motor"). And a "GNN" ref only
        // means CCC/MOTOR when it ISN'T a Mitchell CEG citation (Mitchell CEG also numbers sections GNN).
        bool mentionsCcc = t.Contains("ccc") || t.Contains("pathways")
            || (Regex.IsMatch(t, @"\bg\d+\b") && !mentionsMitchell);
        if (isCcc && mentionsMitchell && !mentionsCcc) return false;      // Mitchell-only on a CCC estimate
        if (isMitchell && mentionsCcc && !mentionsMitchell) return false; // CCC-only on a Mitchell estimate
        return true;
    }

    /// <summary>Core phrase of a "not included" item for dedup: drop "(citations)" and trailing
    /// filler ("application", "usage", "procedure", "requirements") so near-identical variants merge.</summary>
    private static string NormalizeNotIncludedKey(string phrase)
    {
        var p = Regex.Replace(phrase.ToLowerInvariant(), @"\([^)]*\)", " ");   // strip parentheticals
        p = Regex.Replace(p, @"\b(application|applications|usage|procedure|procedures|requirements|beyond\b.*)$", " ");
        p = Regex.Replace(p, @"[^a-z0-9]+", " ").Trim();                        // normalize punctuation
        return p;
    }

    // Reference categories that are not separately-billable operations — never suggested as "add" items.
    private static readonly HashSet<string> NonAddableCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Industry Studies", "Special Precautions"
    };

    // Specific reference terms that are concepts/billing notes, not operations to add to an estimate.
    private static readonly HashSet<string> NonAddableTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "sublet", "overlap", "r&i overlap and included operations", "judgment item", "included operations"
    };

    // Work "contexts" — the kinds of operations a job may involve. Each has a set of DISTINCTIVE
    // trigger phrases (deliberately specific, not generic part/filler words). A context is "active"
    // for an estimate if any trigger appears on it; a reference item "belongs to" a context if any
    // trigger appears in its text. An item is only suggested when it shares an ACTIVE context with
    // the estimate — so carbon-fiber/welding/glass items never appear on a job that has none.
    private static readonly Dictionary<string, string[]> ContextTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["refinish"]   = new[]{ "refinish", "refn", "repaint", "clearcoat", "clear coat", "basecoat", "base coat", "blend", "blnd", "de nib", "denib", "color tint", "overspray", "wet dry sand", "rub out", "edging", "spray out", "flash and cure", "two stage", "three stage", "feather edge", "buff" },
        ["welding"]    = new[]{ "weld", "welding", "welded", "destructive weld", "weld through", "spot weld" },
        ["structural"] = new[]{ "structural", "sectioning", "pinchweld", "pinch weld", "frame rail", "hydroformed", "boron", "anchor pot" },
        ["composite"]  = new[]{ "composite", "carbon fiber", "cfrp", "fiber reinforced", "sheet molded", " smc ", " frp " },
        ["glass"]      = new[]{ "windshield", "urethane", "nags", "glass primer", "back glass" },
        ["electrical"] = new[]{ "battery", "voltage", "wiring harness", "electronic reset", "module programming", "alternator" },
        ["adas"]       = new[]{ "adas", "calibration", "calibrate", "radar", "lidar", "blind spot", "lane departure", "aiming", "headlamp aim" },
        ["scan"]       = new[]{ "pre scan", "prescan", "post scan", "postscan", "in process scan", "diagnostic scan", "dynamic systems" },
        ["corrosion"]  = new[]{ "corrosion", "seam sealer", "cavity wax", "undercoating" },
        ["suspension"] = new[]{ "suspension", "wheel alignment", "strut", "control arm", "ball joint" },
        ["airbag"]     = new[]{ "airbag", "air bag", " srs ", "seatbelt", "seat belt", "restraint", "pretensioner", "clockspring" },
        // NOTE: "tire pressure" is intentionally NOT a trigger — it appears in ADAS prep lines
        // ("Check and Adjust Tire Pressure for ADAS Calibrations") and would falsely activate tire/wheel work.
        ["tirewheel"]  = new[]{ "tpms", "mount and balance", "wheel balance", "road force", "tire disposal", "tire rotation", "tire replacement" },
        ["acclimate"]  = new[]{ "refrigerant", "r1234", "evacuate and recharge", "ac condenser", "a c recharge" },
        ["aluminum"]   = new[]{ "aluminum" },
        ["ev"]         = new[]{ " ev ", "hybrid", "high voltage", "electric vehicle" },
    };

    // "Exclusive" work types: if an item's TITLE names one of these and that work ISN'T on the
    // estimate, the item is dropped outright — regardless of what its description text mentions.
    // (A composite item whose blurb says "corrosion" must not ride in on a corrosion-only job.)
    // The remaining contexts (refinish, electrical, adas, scan, corrosion, airbag) are "additive" —
    // they apply alongside other work, so they're not used for exclusion.
    private static readonly HashSet<string> ExclusiveContexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "welding", "structural", "composite", "glass", "suspension", "tirewheel", "acclimate", "aluminum", "ev"
    };

    /// <summary>
    /// Exclude an item when its TITLE names an exclusive work type (welding, composite, glass, etc.)
    /// that is not active on the estimate. Title-based so a stray word in the description can't let it in.
    /// </summary>
    private static bool IsExcludedByWorkType(string? term, HashSet<string> activeContexts)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;
        var norm = NormalizeForContext(term);
        foreach (var ctx in ExclusiveContexts)
        {
            if (activeContexts.Contains(ctx)) continue;
            foreach (var trig in ContextTriggers[ctx])
                if (norm.Contains(trig)) return true;
        }
        return false;
    }

    /// <summary>Normalize text for context trigger matching: lowercase, non-alphanumerics → spaces, padded.</summary>
    private static string NormalizeForContext(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return " ";
        var cleaned = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ");
        return " " + Regex.Replace(cleaned, @"\s+", " ").Trim() + " ";
    }

    /// <summary>Which work contexts are present on the estimate (from its normalized text blob).</summary>
    private static HashSet<string> DetectContexts(string normBlob)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in ContextTriggers)
            foreach (var trig in kv.Value)
                if (normBlob.Contains(trig)) { active.Add(kv.Key); break; }
        return active;
    }

    /// <summary>
    /// Relevance-filtered reference checklist: a reference-tab item (Definition or Procedure) is
    /// suggested only when it actually relates to this estimate — it shares a distinctive
    /// operation/material keyword with the estimate, or it's P-Page-linked to a matched operation.
    /// Items already on the estimate, or that are included in other labor, are skipped. This cuts
    /// the full-library dump down to what applies to the job in front of the estimator.
    /// </summary>
    private void AnalyzeReferenceChecklist(string blob, HashSet<string> words, HashSet<string> activeContexts, MatchResult result, HashSet<string> seen, int maxPresent)
    {
        if (activeContexts.Count == 0) return;

        // Stemmed estimate words (aiming→aim, blocking→block) for order-independent present detection.
        var stemmed = words.Select(StemWord).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Consecutive significant-word pairs on each estimate line — used to confirm an item is
        // really present (its words sit adjacent on a line) vs. merely scattered across the estimate.
        var estimateBigrams = BuildEstimateBigrams(blob);

        // Definitions — the named operations/items curated on the Reference tab (De-Nib, etc.).
        if (_definitions != null)
        {
            foreach (var def in _definitions)
            {
                if (result.Present.Count >= maxPresent) return;

                // Skip items that are INCLUDED in other labor (not separately addable).
                var status = def.Status ?? "";
                bool includedOnly = status.Contains("INCLUDED", StringComparison.OrdinalIgnoreCase)
                    && !status.Contains("NOT INCLUDED", StringComparison.OrdinalIgnoreCase);
                if (includedOnly) continue;
                if (def.Category != null && NonAddableCategories.Contains(def.Category)) continue;

                bool defExcluded = IsExcludedByWorkType(def.Term, activeContexts);
                AddChecklistItem(def.Term, def.Category ?? "Reference", defExcluded, blob, stemmed, estimateBigrams, result, seen, maxPresent);
            }
        }

        // Procedures — billable shop procedures from the Reference tab.
        if (_procedures != null)
        {
            foreach (var proc in _procedures)
            {
                if (result.Present.Count >= maxPresent) return;
                bool procExcluded = IsExcludedByWorkType(proc.Name, activeContexts);
                AddChecklistItem(proc.Name, proc.Category ?? "Procedure", procExcluded, blob, stemmed, estimateBigrams, result, seen, maxPresent);
            }
        }
    }

    /// <summary>
    /// Add a reference item to the Present list IF it's detected on the estimate. This is now the
    /// checklist's only job — confirm what's already there (for the reference document). Suggestions
    /// come exclusively from the curated Not-Included data, so there's no keyword-guessing here.
    /// Work-type exclusion still applies: a "Carbon Fiber" item can't be present on a non-composite job.
    /// </summary>
    private void AddChecklistItem(string? term, string category, bool excluded, string blob, HashSet<string> stemmedWords, HashSet<string> estimateBigrams, MatchResult result, HashSet<string> seen, int maxPresent)
    {
        if (string.IsNullOrWhiteSpace(term)) return;
        var t = term.Trim();
        var key = t.ToLowerInvariant();
        if (NonAddableTerms.Contains(key)) return; // concept/billing note, not an addable operation
        if (!seen.Add(key)) return;                // dedup across NotIncluded gaps + checklist
        if (excluded) return;                      // work type not on the job → can't be present
        if (result.Present.Count >= maxPresent) return;

        if (IsChecklistItemPresent(t, blob, stemmedWords, estimateBigrams))
        {
            result.Present.Add(new SuggestedMissingItem
            {
                Item = t,
                SourceOperation = category,
                Reason = $"Already added on estimate ({category})"
            });
        }
    }

    private static readonly char[] PresentWordSplit =
        { ' ', '|', '/', '-', ',', '(', ')', '.', ':', ';', '&', '\'', '"' };

    /// <summary>Ordered significant (len≥3, non-stopword, stemmed) words of a single text run.</summary>
    private static List<string> SignificantStemmedSeq(string text) => text.ToLowerInvariant()
        .Split(PresentWordSplit, StringSplitOptions.RemoveEmptyEntries)
        .Select(NormalizeWord)
        .Where(w => w.Length >= 3 && !StopWords.Contains(w))
        .Select(StemWord)
        .ToList();

    /// <summary>
    /// Consecutive significant-word pairs within each estimate line (terms are joined by " | " in
    /// the blob, so pairs never cross a line boundary). This captures real adjacency — "stage secure"
    /// from the line "Stage and secure" — which is what tells a genuine presence apart from the same
    /// words scattered across unrelated lines.
    /// </summary>
    private static HashSet<string> BuildEstimateBigrams(string blob)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in blob.Split('|'))
        {
            var seq = SignificantStemmedSeq(term);
            for (int i = 0; i + 1 < seq.Count; i++)
                set.Add(seq[i] + " " + seq[i + 1]);
        }
        return set;
    }

    /// <summary>
    /// Is a reference-checklist item already on the estimate? True on a direct substring, or when a
    /// consecutive 2-word run of the item sits adjacent on an estimate line (stopwords ignored on
    /// both sides) — so "Stage and Secure for Refinish" matches the line "Stage and secure", while
    /// "Glass Primer" does NOT match an estimate that merely has "Glass Cleaner" and "…for Primer"
    /// in separate places. The fallback (for items with no 2-word run) requires at least TWO
    /// distinctive words present, so a lone common word ("battery") can't confirm "Battery Support".
    /// </summary>
    private static bool IsChecklistItemPresent(string item, string blob, HashSet<string> stemmedWords, HashSet<string> estimateBigrams)
    {
        var p = item.ToLowerInvariant().Trim();
        if (p.Length >= 4 && blob.Contains(p)) return true; // literal substring

        // Adjacency: a significant 2-word run of the item appears consecutively on an estimate line.
        var seq = SignificantStemmedSeq(p);
        for (int i = 0; i + 1 < seq.Count; i++)
            if (estimateBigrams.Contains(seq[i] + " " + seq[i + 1]))
                return true;

        // Fallback (single-significant-word items with no adjacency): require ≥2 DISTINCTIVE words
        // (not generic part words, not broad catch-alls) present. A lone shared common word is not
        // enough to claim a specific item is on the estimate — that's what wrongly put "Battery
        // Support" / "Test Battery Condition" / "Glass Primer" into the "already on estimate" list.
        var distinctive = p.Split(PresentWordSplit, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeWord)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w)
                        && !GenericSingleWords.Contains(w) && !BroadAnchorWords.Contains(w))
            .Select(StemWord)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctive.Count == 0) return false;
        int present = distinctive.Count(stemmedWords.Contains);
        return present >= 2 && (double)present / distinctive.Count >= 0.6;
    }

    /// <summary>Light stemming for present-matching: strip common verb suffixes (aiming→aim, blocking→block).</summary>
    private static string StemWord(string w)
    {
        foreach (var suf in new[] { "ing", "ed" })
            if (w.Length > suf.Length + 2 && w.EndsWith(suf, StringComparison.Ordinal))
                return w.Substring(0, w.Length - suf.Length);
        return w;
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

    /// <summary>Does the estimate term contain the FULL alias phrase (word-boundary)? Directional on
    /// purpose: "right a-pillar" ⊇ "a pillar" ✓, but "windshield" ⊉ "windshield pillar" ✗ and
    /// "c-pillar" ⊉ "a pillar" ✗ — a short term can't trigger a longer, more specific alias.</summary>
    private static bool TermContainsAliasPhrase(string term, string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return false;
        string Norm(string s) => " " + Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim() + " ";
        return Norm(term).Contains(Norm(alias));
    }

    /// <summary>A curated op whose type is a physical part operation (Replace/Repair/R&I) must be
    /// matched to a real line of that operation on the estimate. Process ops (Calibration, Scan,
    /// Electrical, Refinish, etc.) aren't gated — they apply from the kind of work present.</summary>
    private static bool RequiresLineOp(string? curatedOpType)
    {
        if (string.IsNullOrWhiteSpace(curatedOpType)) return false;
        var o = curatedOpType.ToLowerInvariant();
        return o.Contains("replace") || o.Contains("repair") || o == "r&i" || o == "r & i";
    }

    /// <summary>Does the estimate's set of operation codes for a part satisfy a curated op's type?
    /// Replace←Repl/New, Repair←Rpr, R&I←R&I/O&H. "Replace/Repair" accepts either.</summary>
    private static bool LineOpSatisfies(string curatedOpType, HashSet<string> estimateOps)
    {
        bool has(params string[] codes) => estimateOps.Any(e => codes.Any(c => string.Equals(e, c, StringComparison.OrdinalIgnoreCase)));
        var o = curatedOpType.ToLowerInvariant();
        bool ok = false;
        if (o.Contains("replace")) ok |= has("Repl", "New", "R&R");
        if (o.Contains("repair")) ok |= has("Rpr");
        if (o == "r&i" || o == "r & i") ok |= has("R&I", "O/H");
        return ok;
    }

    // === MATCHING METHODS ===

    private void MatchIncludedNotIncluded(string term, List<PdfQueueItem> items, HashSet<string> crossRefPPages, MatchResult result, List<RefIncludedNotIncluded> matchedOps, Dictionary<string, SortedSet<int>> termLines, Dictionary<string, HashSet<string>> termOps, Dictionary<string, SortedSet<int>> opLines)
    {
        if (_includedNotIncluded == null) return;

        foreach (var op in _includedNotIncluded)
        {
            // PartName uses the general matcher; ALIASES require the full alias phrase to appear in
            // the term (term ⊇ alias), NOT the reverse. Otherwise a lone "windshield" matched the
            // A-Pillar alias "windshield pillar", and "center"/"c-pillar" matched "center pillar" —
            // a short/generic term must not light up a longer, more specific alias.
            bool matched = TermMatchesText(term, op.PartName)
                || (op.Aliases != null && op.Aliases.Any(a => TermContainsAliasPhrase(term, a)));

            if (matched)
            {
                // Operation-type gate: a part-level op (Replace/Repair/R&I) must correspond to a
                // COMPATIBLE operation actually on the estimate for this part. Otherwise a "Quarter
                // Panel Replace" op matches a repaired quarter, and "Door Shell Replace" matches a
                // refinished fuel door. Process ops (Calibration, Scan, Electrical…) aren't gated.
                if (RequiresLineOp(op.OperationType))
                {
                    termOps.TryGetValue(term, out var estOps);
                    if (estOps == null || !LineOpSatisfies(op.OperationType!, estOps)) continue;
                }

                // Record for NotIncluded gap analysis (dedup by reference — an op may match many terms).
                if (!matchedOps.Contains(op)) matchedOps.Add(op);

                // Record which estimate line(s) this term came from, so suggestions can point back
                // to the exact line on the estimate.
                if (op.Id != null && termLines.TryGetValue(term, out var lns) && lns.Count > 0)
                {
                    if (!opLines.TryGetValue(op.Id, out var set)) { set = new SortedSet<int>(); opLines[op.Id] = set; }
                    set.UnionWith(lns);
                }

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

                // Format the title based on whether inquiryNumber is a real DEG number
                // (numeric) vs a GTE / P-Page / MOTOR reference label.
                var inqNum = deg.InquiryNumber ?? "";
                var isRealDegNumber = inqNum.Any(char.IsDigit) &&
                                      !inqNum.Contains("GTE", StringComparison.OrdinalIgnoreCase) &&
                                      !inqNum.Contains("P-Page", StringComparison.OrdinalIgnoreCase) &&
                                      !inqNum.Contains("MOTOR", StringComparison.OrdinalIgnoreCase) &&
                                      !inqNum.Contains("CEG", StringComparison.OrdinalIgnoreCase) &&
                                      !inqNum.Contains("Multiple", StringComparison.OrdinalIgnoreCase) &&
                                      !inqNum.Contains("NAGS", StringComparison.OrdinalIgnoreCase);
                var termText = isRealDegNumber
                    ? $"DEG #{inqNum}: {deg.Title}"
                    : (string.IsNullOrWhiteSpace(inqNum) ? deg.Title : $"{deg.Title} ({inqNum})");
                var statusText = isRealDegNumber ? "DEG Inquiry" : "P-Page / GTE Reference";

                items.Add(new PdfQueueItem
                {
                    Id = id,
                    Term = termText,
                    Category = deg.Category ?? (isRealDegNumber ? "DEG Inquiry" : "P-Page / GTE Reference"),
                    Definition = deg.Response ?? "",
                    Details = deg.KeyPoints != null ? string.Join("\n", deg.KeyPoints.Select(k => $"- {k}")) : null,
                    DegInquiry = deg.InquiryNumber,
                    PPageRef = deg.PPageRef,
                    Status = statusText
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

        searchTerm = searchTerm.Trim();
        var refText = text.Trim();

        // Ignore ultra-short reference tokens (e.g. "ac", "ws"). A 2-char substring match hits far
        // too many unrelated words ("br-ac-ket", "wa-sh-er", "tr-ac-k") and produces bogus references
        // (this is why an A/C / refrigerant doc was matching a plain bumper estimate).
        if (refText.Length < 3)
            return false;

        // Level 1: Exact substring — reference text contains the full search term
        // (e.g. reference "Quarter Panel" contains search term "Quarter Panel")
        if (searchTerm.Length >= 3 && refText.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
        {
            // A single-word search term must land on a whole-word boundary so short/common
            // tokens ("bar", "cap", "seal") don't hit unrelated words ("crossbar", "capacity",
            // "sealant"). Multi-word phrases are specific enough to trust as a raw substring.
            bool searchIsSingleWord = !searchTerm.Contains(' ');
            if (!searchIsSingleWord ||
                Regex.IsMatch(refText, $@"\b{Regex.Escape(searchTerm)}\b", RegexOptions.IgnoreCase))
                return true;
        }

        // Extract significant words for fuzzy matching
        var textWords = refText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !StopWords.Contains(w) && w.Length > 2)
            .ToList();
        var searchWords = searchTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !StopWords.Contains(w) && w.Length > 2)
            .ToList();

        // Level 2: Reverse match — search term contains the reference text.
        if (searchTerm.Contains(refText, StringComparison.OrdinalIgnoreCase))
        {
            // Multi-word reference text (2+ significant words) is specific enough to trust.
            // e.g. "Quarter Panel" on a reference → "Quarter Panel Replace" on estimate → good match.
            if (textWords.Count >= 2)
                return true;

            // Single-word reference text: block generic terms entirely (verbs + common nouns),
            // and require a WHOLE-WORD match so "cap" doesn't hit "capacity" and "weld" doesn't
            // catch a stray substring. Allow only when the search term is short (≤3 words) so the
            // word is a substantial part of the concept.
            var singleWord = textWords.FirstOrDefault() ?? refText;
            if (!GenericSingleWords.Contains(singleWord) && searchWords.Count <= 3 &&
                Regex.IsMatch(searchTerm, $@"\b{Regex.Escape(singleWord)}\b", RegexOptions.IgnoreCase))
                return true;
        }

        // Level 3 — word-level overlap: require ≥60% overlap of the shorter side's
        // significant words, with a minimum of 2.
        //
        // Two rules keep this from firing on unrelated references:
        //   1. WHOLE-WORD matching (normalized for simple plurals) — NOT loose substring.
        //      Substring counting was the top false-positive source: "Protect" matched
        //      "Protection", "align" matched "Alignment", "coat" matched "overcoat", so a
        //      "Cover and Protect Electrical" line pulled in masking/overspray defs.
        //   2. At least one matched word must be NON-generic. A match can no longer ride
        //      purely on shared filler ("cover", "panel", "bumper", "trim"): e.g. a
        //      "Cover Vehicle for Overspray Protection" def must share a *specific* word,
        //      not just the word "cover", to attach to an estimate line.
        if (searchWords.Count >= 2 && textWords.Count >= 2)
        {
            var refWordSet = textWords
                .Select(NormalizeWord)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var matched = searchWords
                .Where(w => refWordSet.Contains(NormalizeWord(w)))
                .ToList();
            bool hasSpecificWord = matched.Any(w => !GenericSingleWords.Contains(NormalizeWord(w)));
            var shorterCount = Math.Min(searchWords.Count, textWords.Count);
            var minRequired = Math.Max(2, (int)Math.Ceiling(shorterCount * 0.6));
            if (hasSpecificWord && matched.Count >= minRequired)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Normalize a word for whole-word comparison: strip a simple trailing plural "s"
    /// (e.g. "bumpers" → "bumper") so singular/plural forms still match, without the
    /// loose substring behavior that caused false positives ("protect" ⊂ "protection").
    /// </summary>
    private static string NormalizeWord(string w)
    {
        if (w.Length > 3
            && w.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            && !w.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
            return w[..^1];
        return w;
    }

    // === DATA LOADING ===

    private void EnsureDataLoaded()
    {
        if (_dataLoaded) return;

        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");

        // Each reference file loads INDEPENDENTLY. Previously all five shared one try/catch, so a
        // single malformed file (Procedures.json) threw and aborted the rest — silently nulling
        // _procedures AND _includedNotIncluded, which killed the entire curated "Not Included"
        // suggestion path on every estimate. One bad file must never take the others down.
        _definitions = LoadSafely("Definitions.json", LoadDefinitions);
        _ppages = LoadSafely("PPages.json", LoadPPages);
        _degInquiries = LoadSafely("DEGInquiries.json", LoadDEGInquiries);
        _procedures = LoadSafely("Procedures.json", LoadProcedures);
        _includedNotIncluded = LoadSafely("IncludedNotIncluded.json", LoadIncludedNotIncluded);

        _dataLoaded = true;
        Debug.WriteLine($"[ReferenceMatcher] Loaded: {_definitions?.Count ?? 0} defs, {_ppages?.Count ?? 0} ppages, " +
            $"{_degInquiries?.Count ?? 0} DEGs, {_procedures?.Count ?? 0} procs, {_includedNotIncluded?.Count ?? 0} incl/not incl");

        List<T>? LoadSafely<T>(string fileName, Func<string, List<T>?> loader)
        {
            try { return loader(Path.Combine(dataDir, fileName)); }
            catch (Exception ex) { Debug.WriteLine($"[ReferenceMatcher] Failed to load {fileName}: {ex.Message}"); return null; }
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
/// An operation the reference data says is NOT included in a matched operation's labor, and
/// which isn't already on the estimate — surfaced as a "consider adding" suggestion.
/// </summary>
public class SuggestedMissingItem
{
    /// <summary>The not-included operation/item (e.g. "Clear Coat", "Blend adjacent panels").</summary>
    public string Item { get; set; } = "";
    /// <summary>The matched estimate operation this suggestion came from (e.g. "Bumper Cover (Replace)").</summary>
    public string SourceOperation { get; set; } = "";
    /// <summary>Human-readable explanation shown to the estimator.</summary>
    public string Reason { get; set; } = "";
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

    /// <summary>Operations not included in matched labor and not already on the estimate.</summary>
    public List<SuggestedMissingItem> Suggestions { get; set; } = new();

    /// <summary>Relevant reference-tab items that ARE already on the estimate (shown as confirmation).</summary>
    public List<SuggestedMissingItem> Present { get; set; } = new();

    public string Summary => Items.Count > 0 || Suggestions.Count > 0 || Present.Count > 0
        ? $"Found {Items.Count} reference matches, {Suggestions.Count} suggested additions, {Present.Count} already present ({MatchedTerms.Count} terms matched, {CrossRefCount} cross-refs)"
        : "No matching reference items found";
}
