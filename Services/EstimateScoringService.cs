#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Comprehensive Estimate Scoring Service
    /// Analyzes estimates for completeness and identifies missing operations.
    ///
    /// Scoring Categories:
    /// - Operation Chains: Missing related operations (e.g., bumper replace missing flex additive)
    /// - Blend Operations: Missing blend on adjacent panels
    /// - Materials: Missing corrosion protection, seam sealer, etc.
    /// - R&I Operations: Missing R&I for access
    /// - Diagnostics: Missing pre/post scans for ADAS vehicles
    /// - Calibrations: Missing ADAS calibrations after glass/sensor work
    /// </summary>
    public class EstimateScoringService
    {
        private static EstimateScoringService? _instance;
        public static EstimateScoringService Instance => _instance ??= new EstimateScoringService();

        private readonly CommonlyMissedData? _missedItemsData;
        private readonly IncludedNotIncludedData? _pPageData;
        private readonly Dictionary<string, List<BlendRule>> _blendRules;
        private readonly Dictionary<string, List<string>> _operationChains;

        // Pre-computed line index for current scoring run (avoids thousands of repeated ToLowerInvariant + regex calls)
        private List<string> _idxPartsLower = new();
        private List<string> _idxDescsLower = new();
        private List<string> _idxOpsLower = new();
        private string _idxAllText = "";  // all parts+descs concatenated for quick global search

        public EstimateScoringService()
        {
            _missedItemsData = LoadCommonlyMissedItems();
            _pPageData = LoadIncludedNotIncludedData();
            _blendRules = InitializeBlendRules();
            _operationChains = InitializeOperationChains();
        }

        #region Word Boundary Helpers

        private static readonly Regex _vehicleInfoPattern = new(@"(19|20)\d{2}\s+\w+\s+\w+", RegexOptions.Compiled);
        private static readonly HashSet<string> _fuzzyStopWords = new() { "and", "for", "the", "during", "with", "from" };

        // Cache compiled word-boundary regexes to avoid re-creating them in hot loops
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> _wordBoundaryCache = new();

        /// <summary>
        /// Synonym groups: if the scoring checks for any of these items, it should also
        /// accept any of the other items in the same group as a match.
        /// Exposed publicly for synonym-aware dedup in learned pattern filtering.
        /// </summary>
        public static string[][] SynonymGroups => _synonymGroups;
        private static readonly string[][] _synonymGroups = new[]
        {
            new[] { "corrosion protection", "cavity wax", "anti-corrosion", "rust protection", "corrosion resist", "e-coat", "ecoat" },
            new[] { "battery disconnect", "disconnect battery", "disconnect and reconnect battery", "d&r battery", "battery reconnect" },
            new[] { "seam sealer", "seam seal", "sealer application" },
            new[] { "weld-thru primer", "weld thru primer", "weld through primer", "welding primer" },
            new[] { "adhesion promoter", "ad pro", "adpro" },
            new[] { "flex additive", "flex add", "flexitive" },
            new[] { "pre-repair scan", "pre scan", "pre-scan", "prescan" },
            new[] { "post-repair scan", "post scan", "post-scan", "postscan", "post repair scan" },
            new[] { "clean for delivery", "final clean", "detail clean", "clean delivery" },
            new[] { "hazardous waste", "haz waste", "hazmat disposal", "waste disposal" },
            new[] { "srs", "airbag", "air bag", "supplemental restraint", "restraint system" },
            new[] { "seatbelt", "seat belt", "safety belt" },
            new[] { "disable and enable srs", "srs disable", "disable srs", "air bag system diagnosis" },
            new[] { "cover car", "cover vehicle", "mask and protect", "cover for overspray", "cover interior" },
            new[] { "tire pressure", "tpms", "tire monitor" },
            new[] { "customer belongings", "personal items", "remove belongings" },
            new[] { "restraint control module", "rcm program", "seat weight sensor", "srs module" },
            new[] { "torque wheels", "torque lug nuts", "spec torque", "torque to spec", "torque wheel", "torque wheels to spec" },
            new[] { "dynamic systems verification", "collision diagnosis", "post repair verification", "systems verification" },
        };

        private static readonly HashSet<string> _dedupStopWords = new()
            { "and", "for", "the", "during", "with", "from", "all", "after", "before", "required" };

        private static string NormalizeDedupKey(string title)
        {
            var t = title.ToLowerInvariant().Trim();
            if (t.StartsWith("missing: ")) t = t.Substring(9);
            else if (t.StartsWith("need "))
            {
                var ci = t.IndexOf(':');
                if (ci > 0 && ci < t.Length - 1) t = t.Substring(ci + 1).TrimStart();
            }
            var words = t.Split(new[] { ' ', '-', '/', '&', '(', ')', ',', '.' },
                StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !_dedupStopWords.Contains(w))
                .OrderBy(w => w).ToArray();
            return words.Length > 0 ? string.Join("|", words) : t;
        }

        /// <summary>
        /// Collapse synonym-equivalent scoring issues into one per synonym group.
        /// Keeps the highest-severity / highest-deduction issue and removes the rest.
        /// Example: "Battery Disconnect/Reconnect" + "Battery Disconnect" + "Battery Reconnect &amp; Initialize"
        ///          → keeps the one with highest severity.
        /// </summary>
        private static void DeduplicateSynonymIssues(EstimateScoringResult result)
        {
            var toRemove = new HashSet<int>();
            for (int g = 0; g < _synonymGroups.Length; g++)
            {
                var group = _synonymGroups[g];
                // Find all issues that match this synonym group
                var matchingIndices = new List<int>();
                for (int i = 0; i < result.Issues.Count; i++)
                {
                    if (toRemove.Contains(i)) continue;
                    var titleLower = result.Issues[i].Title?.ToLowerInvariant() ?? "";
                    if (group.Any(s => titleLower.Contains(s)))
                        matchingIndices.Add(i);
                }
                if (matchingIndices.Count <= 1) continue;

                // Keep the best one (highest severity, then highest point deduction)
                int bestIdx = matchingIndices[0];
                for (int j = 1; j < matchingIndices.Count; j++)
                {
                    var curr = result.Issues[matchingIndices[j]];
                    var best = result.Issues[bestIdx];
                    if (curr.Severity > best.Severity ||
                        (curr.Severity == best.Severity && curr.PointDeduction > best.PointDeduction))
                        bestIdx = matchingIndices[j];
                }
                foreach (var idx in matchingIndices)
                    if (idx != bestIdx) toRemove.Add(idx);
            }

            if (toRemove.Count > 0)
                result.Issues = result.Issues.Where((_, i) => !toRemove.Contains(i)).ToList();
        }

        /// <summary>
        /// Word-boundary-aware keyword match.
        /// Keywords under 3 chars → exact token match (split on whitespace/punctuation).
        /// Keywords 3+ chars → \b word-boundary regex (compiled + cached).
        /// </summary>
        public static bool IsWordMatch(string text, string keyword)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword)) return false;

            var kw = keyword.ToLowerInvariant();
            var txt = text.ToLowerInvariant();

            if (kw.Length < 3)
            {
                // Exact token match for very short keywords
                var tokens = txt.Split(new[] { ' ', ',', ';', '/', '-', '(', ')', '&', '.', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                return tokens.Any(t => t == kw);
            }

            // Word-boundary regex for 3+ char keywords — cached and compiled
            var regex = _wordBoundaryCache.GetOrAdd(kw, k =>
                new Regex(@"\b" + Regex.Escape(k) + @"\b", RegexOptions.Compiled));
            return regex.IsMatch(txt);
        }

        /// <summary>
        /// Detects vehicle description lines (e.g., "2023 Tesla Model 3") that
        /// leak through PDF parsing and should not trigger part-based checks.
        /// </summary>
        public static bool IsVehicleInfoLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return _vehicleInfoPattern.IsMatch(text);
        }

        /// <summary>
        /// Build a pre-computed index of all line text (lowercase) for the current scoring run.
        /// Called once at the start of ScoreEstimate — all check methods use the index instead of
        /// repeatedly calling ToLowerInvariant() and scanning all lines.
        /// </summary>
        private void BuildLineIndex(List<ParsedEstimateLine> lines)
        {
            _idxPartsLower = new List<string>(lines.Count);
            _idxDescsLower = new List<string>(lines.Count);
            _idxOpsLower = new List<string>(lines.Count);
            var sb = new System.Text.StringBuilder(lines.Count * 80);
            for (int i = 0; i < lines.Count; i++)
            {
                var p = lines[i].PartName?.ToLowerInvariant() ?? "";
                var d = lines[i].Description?.ToLowerInvariant() ?? "";
                var o = lines[i].OperationType?.ToLowerInvariant() ?? "";
                _idxPartsLower.Add(p);
                _idxDescsLower.Add(d);
                _idxOpsLower.Add(o);
                sb.Append(p).Append(' ').Append(d).Append(' ');
            }
            _idxAllText = sb.ToString();
        }

        /// <summary>
        /// Fast check: is this keyword present anywhere in the estimate?
        /// Uses the pre-computed concatenated text instead of scanning all lines with regex.
        /// </summary>
        private bool IsItemInEstimate(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return false;
            var kw = keyword.ToLowerInvariant();

            // Quick pre-check: if substring is present, confirm with regex
            if (_idxAllText.Contains(kw))
            {
                if (kw.Length < 3)
                {
                    var tokens = _idxAllText.Split(new[] { ' ', ',', ';', '/', '-', '(', ')', '&', '.', '\t' },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Any(t => t == kw)) return true;
                }
                else
                {
                    var regex = _wordBoundaryCache.GetOrAdd(kw, k =>
                        new Regex(@"\b" + Regex.Escape(k) + @"\b", RegexOptions.Compiled));
                    if (regex.IsMatch(_idxAllText)) return true;
                }
            }

            // Check synonyms: if the keyword is part of a synonym group,
            // check if any alternative appears as a coherent phrase on a single line
            // (NOT against concatenated text — that causes false matches across unrelated lines)
            foreach (var group in _synonymGroups)
            {
                bool keywordInGroup = false;
                for (int i = 0; i < group.Length; i++)
                {
                    if (kw.Contains(group[i]) || group[i].Contains(kw))
                    { keywordInGroup = true; break; }
                }
                if (keywordInGroup)
                {
                    for (int i = 0; i < group.Length; i++)
                    {
                        var alt = group[i];
                        for (int j = 0; j < _idxPartsLower.Count; j++)
                        {
                            if (_idxPartsLower[j].Contains(alt) || _idxDescsLower[j].Contains(alt))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Check if any line's part name or description contains the given substring (pre-lowered).
        /// Uses indexed lowercase text.
        /// </summary>
        private bool AnyLineContains(string substringLower)
        {
            for (int i = 0; i < _idxPartsLower.Count; i++)
            {
                if (_idxPartsLower[i].Contains(substringLower) || _idxDescsLower[i].Contains(substringLower))
                    return true;
            }
            return false;
        }

        #endregion

        #region Main Scoring Method

        private static void RunCheck(Action check, string name)
        {
            try { check(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScoringService] {name} check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Score an estimate for completeness.
        /// Returns a detailed scoring result with suggestions.
        /// </summary>
        public EstimateScoringResult ScoreEstimate(List<ParsedEstimateLine> lines, string? vehicleInfo = null,
            decimal estimateBodyRate = 0, decimal estimatePaintRate = 0, decimal estimateMechRate = 0,
            string? insuranceCompany = null)
        {
            // Pre-process: join continuation lines (e.g., "Rub-Out &" + "& Buff" → "Rub-Out & Buff")
            // OCR and PDF parsers sometimes split wrapped descriptions across multiple lines.
            lines = JoinContinuationLines(lines);

            // Filter out vehicle description lines that leaked through PDF parsing
            // (e.g., "2023 Tesla Model 3") — these trigger false positives on EV/make checks.
            lines = lines.Where(l =>
                !IsVehicleInfoLine(l.PartName ?? "") || !string.IsNullOrEmpty(l.OperationType))
                .ToList();

            // Pre-compute lowercase index for all lines (avoids thousands of repeated ToLowerInvariant calls)
            BuildLineIndex(lines);

            var result = new EstimateScoringResult
            {
                VehicleInfo = vehicleInfo,
                TotalLineItems = lines.Count,
                AssessedAt = DateTime.Now,
                EstimateBodyRate = estimateBodyRate,
                EstimatePaintRate = estimatePaintRate,
                EstimateMechRate = estimateMechRate
            };

            if (lines.Count == 0)
            {
                result.OverallScore = 0;
                result.Grade = "N/A";
                result.Summary = "No operations to score";
                return result;
            }

            // Calculate estimate total
            result.EstimateTotal = lines.Sum(l => l.Price);

            // Categorize estimate lines for the report card
            result.CategorizedLines = CategorizeEstimateLines(lines);

            // Run all scoring checks — each wrapped so one failure doesn't block the rest
            RunCheck(() => CheckOperationChains(lines, result), "OperationChains");
            RunCheck(() => CheckBlendOperations(lines, result), "BlendOperations");
            RunCheck(() => CheckMaterialOperations(lines, result), "MaterialOperations");
            RunCheck(() => CheckNotIncludedOperations(lines, result), "NotIncludedOperations");
            RunCheck(() => CheckRIOperations(lines, result), "RIOperations");
            RunCheck(() => CheckDiagnosticScans(lines, result), "DiagnosticScans");
            RunCheck(() => CheckADASCalibrations(lines, result), "ADASCalibrations");
            RunCheck(() => CheckGlobalRules(lines, result), "GlobalRules");
            RunCheck(() => CheckMustHaves(lines, result, insuranceCompany), "MustHaves");

            // Collapse synonym-equivalent issues (e.g., "Battery Disconnect/Reconnect" + "Battery Disconnect"
            // + "Battery Reconnect & Initialize" → keep highest-severity only)
            DeduplicateSynonymIssues(result);

            // Calculate final score
            CalculateFinalScore(result);

            // Safety cap — if something still produces a runaway, truncate to 500 most severe
            const int MaxIssues = 500;
            if (result.Issues.Count > MaxIssues)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ScoringService] WARNING: {result.Issues.Count} issues exceeded cap of {MaxIssues}, truncating.");
                result.Issues = result.Issues
                    .OrderByDescending(i => i.Severity)
                    .ThenByDescending(i => i.PointDeduction)
                    .Take(MaxIssues).ToList();
            }

            return result;
        }

        #endregion

        #region Operation Chain Checks

        private void CheckOperationChains(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            if (_missedItemsData?.OperationChecks == null) return;

            var issueTitles = new HashSet<string>();

            for (int li = 0; li < lines.Count; li++)
            {
                if (lines[li].IsManualLine || string.IsNullOrEmpty(lines[li].PartName)) continue;

                var partLower = _idxPartsLower[li];
                var opLower = _idxOpsLower[li];

                foreach (var check in _missedItemsData.OperationChecks)
                {
                    var checkData = check.Value;

                    // Check if this line triggers this check (word-boundary match)
                    bool matchesPart = checkData.TriggerKeywords?.Any(k =>
                        IsWordMatch(partLower, k)) == true;

                    bool matchesOp = checkData.TriggerOperations == null ||
                                     checkData.TriggerOperations.Count == 0 ||
                                     checkData.TriggerOperations.Any(o =>
                                         opLower.Contains(o.ToLowerInvariant()));

                    if (matchesPart && matchesOp && checkData.MissedItems != null)
                    {
                        foreach (var missedItem in checkData.MissedItems)
                        {
                            if (issueTitles.Contains(missedItem.Item)) continue;

                            // Use pre-computed index instead of scanning all lines
                            if (!IsItemInEstimate(missedItem.Item))
                            {
                                issueTitles.Add(missedItem.Item);
                                result.Issues.Add(new ScoringIssue
                                {
                                    Category = GetIssueCategory(missedItem.Category ?? "Other"),
                                    Severity = GetSeverity(missedItem.Priority ?? "medium"),
                                    Title = missedItem.Item,
                                    Description = missedItem.Description ?? "",
                                    WhyNeeded = missedItem.WhyNeeded ?? "",
                                    TriggeredBy = lines[li].PartName,
                                    SuggestedFix = new SuggestedFix
                                    {
                                        OperationType = GetOperationType(missedItem.Category ?? "Labor"),
                                        Description = missedItem.Item,
                                        LaborHours = missedItem.LaborHours,
                                        EstimatedCost = missedItem.TypicalCost,
                                        DegReference = missedItem.DegReference,
                                        LaborCategory = MapLaborCategory(missedItem.Category)
                                    },
                                    PointDeduction = GetPointDeduction(missedItem.Priority ?? "medium")
                                });
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Blend Operation Checks

        private void CheckBlendOperations(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            var refinishPanels = new List<int>();
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].IsManualLine &&
                    (_idxOpsLower[i].Contains("repl") || _idxOpsLower[i].Contains("rfn") ||
                     _idxOpsLower[i].Contains("refinish") || lines[i].RefinishHours > 0))
                    refinishPanels.Add(i);
            }

            foreach (var li in refinishPanels)
            {
                var partLower = _idxPartsLower[li];

                // --- Missing blend on adjacent panels ---
                foreach (var rule in _blendRules)
                {
                    if (partLower.Contains(rule.Key))
                    {
                        foreach (var blendRule in rule.Value)
                        {
                            var adjLower = blendRule.AdjacentPanel.ToLowerInvariant();
                            bool hasBlend = false;
                            for (int j = 0; j < lines.Count; j++)
                            {
                                if (_idxPartsLower[j].Contains(adjLower) &&
                                    (_idxOpsLower[j].Contains("blend") || _idxOpsLower[j].Contains("blnd") ||
                                     _idxDescsLower[j].Contains("blend") || _idxDescsLower[j].Contains("blnd")))
                                { hasBlend = true; break; }
                            }

                            if (!hasBlend)
                            {
                                // Try to get average blend hours from learned estimates
                                decimal learnedHours = 0m;
                                int learnedCount = 0;
                                string learnedSource = "";
                                try
                                {
                                    var learnedPattern = EstimateLearningService.Instance
                                        .GetManualLinesForPart(blendRule.AdjacentPanel, "Blend");
                                    if (learnedPattern != null)
                                    {
                                        var blendEntries = learnedPattern.ManualLines
                                            .Where(m => m.RefinishUnits > 0 || m.LaborUnits > 0)
                                            .ToList();
                                        if (blendEntries.Count > 0)
                                        {
                                            learnedHours = blendEntries.Average(m => m.RefinishUnits > 0 ? m.RefinishUnits : m.LaborUnits);
                                            learnedCount = blendEntries.Sum(m => m.TimesUsed);
                                            learnedSource = $"avg from {learnedCount} learned estimate(s)";
                                        }
                                    }
                                }
                                catch { /* learning service may not have data — that's fine */ }

                                var desc = learnedHours > 0
                                    ? $"Adjacent panel blend for color match — {learnedSource}"
                                    : "Adjacent panel blend for color match";

                                var issue = new ScoringIssue
                                {
                                    Category = IssueCategoryType.Blend,
                                    Severity = IssueSeverity.Low,
                                    Title = $"Blend {blendRule.AdjacentPanel}",
                                    Description = desc,
                                    WhyNeeded = $"New paint on {lines[li].PartName} won't match aged paint on {blendRule.AdjacentPanel}",
                                    TriggeredBy = lines[li].PartName ?? "",
                                    SuggestedFix = new SuggestedFix
                                    {
                                        OperationType = "Blend",
                                        Description = $"Blend {blendRule.AdjacentPanel}",
                                        LaborHours = learnedHours,
                                        LaborCategory = "Paint"
                                    },
                                    Source = learnedHours > 0 ? "Learned" : null,
                                    SourceDetail = learnedHours > 0 ? $"{learnedHours:F1} hrs avg" : null,
                                    PointDeduction = 0
                                };

                                if (!result.Issues.Any(i => i.Title == issue.Title))
                                    result.Issues.Add(issue);
                            }
                        }
                    }
                }

                // --- Missing refinish-related operations from learned data ---
                // Check what refinish ops (buff, de-nib, wet/dry sand, edging, backtape, etc.)
                // your prior estimates had for this panel and flag any that are missing here.
                CheckLearnedRefinishOps(lines, li, result);
            }
        }

        /// <summary>
        /// For a refinished panel, check learned estimates for commonly-seen refinish operations
        /// (buff, de-nib, wet/dry sand, edging, backtape, etc.) and flag any missing from this estimate.
        /// </summary>
        private void CheckLearnedRefinishOps(List<ParsedEstimateLine> lines, int panelIndex, EstimateScoringResult result)
        {
            var partName = lines[panelIndex].PartName ?? "";
            if (string.IsNullOrWhiteSpace(partName)) return;

            // Refinish-related keywords to look for in learned manual lines
            var refinishKeywords = new[]
            {
                "buff", "de-nib", "denib", "wet/dry", "wet dry", "rub-out", "rub out",
                "sand", "edging", "backtape", "back tape", "jamb", "clear coat",
                "blend edge", "color sand", "cut and buff", "cut & buff",
                "stage and secure", "stage & secure", "mask", "cover for",
                "spray card", "tint", "let down panel"
            };

            try
            {
                // Query learned data for this panel + Refinish/Replace operations
                ManualLinePattern? learnedPattern = null;
                foreach (var opType in new[] { "Refinish", "Replace", null })
                {
                    learnedPattern = EstimateLearningService.Instance.GetManualLinesForPart(partName, opType);
                    if (learnedPattern != null && learnedPattern.ManualLines.Count > 0)
                        break;
                }

                if (learnedPattern == null || learnedPattern.ManualLines.Count == 0)
                    return;

                // Filter to refinish-related manual lines seen in 2+ estimates
                var refinishOps = learnedPattern.ManualLines
                    .Where(m => m.TimesUsed >= 2)
                    .Where(m =>
                    {
                        var descLower = (m.Description ?? m.ManualLineType ?? "").ToLowerInvariant();
                        return refinishKeywords.Any(kw => descLower.Contains(kw))
                            || m.LaborType.Equals("Refinish", StringComparison.OrdinalIgnoreCase)
                            || m.RefinishUnits > 0;
                    })
                    .OrderByDescending(m => m.TimesUsed)
                    .Take(8) // Cap to avoid flooding
                    .ToList();

                foreach (var learnedOp in refinishOps)
                {
                    var opDesc = !string.IsNullOrEmpty(learnedOp.ManualLineType)
                        ? learnedOp.ManualLineType : learnedOp.Description;
                    if (string.IsNullOrWhiteSpace(opDesc) || opDesc.Length < 4) continue;

                    var opDescLower = opDesc.ToLowerInvariant();

                    // Check if this operation already exists on the estimate for this panel
                    bool alreadyExists = false;
                    for (int j = 0; j < lines.Count; j++)
                    {
                        var lineDesc = _idxDescsLower[j] + " " + _idxPartsLower[j];
                        if (lineDesc.Contains(opDescLower) ||
                            opDescLower.Split(' ').Where(w => w.Length >= 4).All(w => lineDesc.Contains(w)))
                        {
                            alreadyExists = true;
                            break;
                        }
                    }

                    if (alreadyExists) continue;

                    var hours = learnedOp.RefinishUnits > 0 ? learnedOp.RefinishUnits : learnedOp.LaborUnits;
                    var frequency = learnedPattern.ExampleCount > 0
                        ? (double)learnedOp.TimesUsed / learnedPattern.ExampleCount : 0;
                    var pctText = frequency > 0 ? $"{frequency:P0}" : $"{learnedOp.TimesUsed}x";

                    var title = $"{opDesc} — {partName}";
                    if (result.Issues.Any(i => i.Title == title)) continue;

                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Refinish,
                        Severity = frequency >= 0.7 ? IssueSeverity.Medium : IssueSeverity.Low,
                        ActionType = frequency >= 0.5 ? IssueActionType.AddToEstimate : IssueActionType.VerifyOptional,
                        Title = title,
                        Description = $"Seen in {pctText} of your estimates with {partName}",
                        WhyNeeded = $"Your learned estimates commonly include this refinish operation",
                        TriggeredBy = partName,
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Refinish",
                            Description = opDesc,
                            LaborHours = hours,
                            LaborCategory = "Paint"
                        },
                        Source = "Learned",
                        SourceDetail = $"{hours:F1} hrs avg, {pctText} frequency",
                        PointDeduction = 0 // Don't penalize — learned suggestions are advisory
                    });
                }
            }
            catch { /* learning service unavailable — skip gracefully */ }
        }

        #endregion

        #region Material Checks

        private void CheckMaterialOperations(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            var existingTitles = new HashSet<string>(
                result.Issues.Select(i => i.Title.ToLowerInvariant()));

            for (int li = 0; li < lines.Count; li++)
            {
                if (lines[li].IsManualLine || string.IsNullOrEmpty(lines[li].PartName)) continue;

                var opType = lines[li].OperationType ?? "";
                var opLower = _idxOpsLower[li];

                // Skip lines that don't involve paint or structural work
                bool involvesPaint = OperationRulesEngine.Instance.InvolvesPaint(opType) || lines[li].RefinishHours > 0;
                bool isReplace = opLower.Contains("repl") || opLower.Contains("r&r") || opLower.Contains("section");
                if (!involvesPaint && !isReplace) continue;

                var suggestions = OperationRulesEngine.Instance.GetSuggestedOperations(lines[li].PartName!, opType);

                foreach (var suggestion in suggestions)
                {
                    // Skip if already flagged
                    if (existingTitles.Contains(suggestion.Name.ToLowerInvariant())) continue;

                    // Use pre-computed index for presence check
                    var nameLower = suggestion.Name.ToLowerInvariant();
                    bool isPresent = AnyLineContains(nameLower);

                    if (isPresent) continue;

                    // Severity: Critical for replace-only structural ops, High for others
                    var severity = suggestion.ReplaceOnly ? IssueSeverity.Critical : IssueSeverity.High;

                    // Parse DEG reference from source if present
                    string? degRef = null;
                    if (suggestion.Source.Contains("DEG"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(suggestion.Source, @"\d{4,}");
                        if (match.Success)
                            degRef = match.Value;
                    }

                    var issue = new ScoringIssue
                    {
                        Source = "Scoring",
                        Category = IssueCategoryType.Materials,
                        Severity = severity,
                        Title = suggestion.Name,
                        Description = suggestion.Description,
                        WhyNeeded = suggestion.WhyNeeded,
                        TriggeredBy = lines[li].PartName ?? "",
                        SuggestedFix = new SuggestedFix
                        {
                            Description = suggestion.Description,
                            LaborHours = suggestion.DefaultHours,
                            OperationType = suggestion.OperationType,
                            DegReference = degRef,
                            LaborCategory = suggestion.DefaultHours > 0 ? "Paint" : "Parts"
                        },
                        PointDeduction = GetPointDeduction(severity == IssueSeverity.Critical ? "critical" : "high")
                    };

                    result.Issues.Add(issue);
                    existingTitles.Add(suggestion.Name.ToLowerInvariant());
                }
            }
        }

        #endregion

        #region Not-Included Operations (P-Pages/CEG/DEG)

        private void CheckNotIncludedOperations(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            if (_pPageData?.Operations == null || _pPageData.Operations.Count == 0) return;

            var dedupKeys = new HashSet<string>(
                result.Issues.Select(i => $"{i.Title.ToLowerInvariant()}|{i.Category}"));

            for (int li = 0; li < lines.Count; li++)
            {
                if (lines[li].IsManualLine || string.IsNullOrEmpty(lines[li].PartName)) continue;

                var partLower = _idxPartsLower[li];
                var opLower = _idxOpsLower[li];

                foreach (var pPageOp in _pPageData.Operations)
                {
                    // Match against aliases (word-boundary match)
                    bool matchesPart = pPageOp.Aliases?.Any(a => IsWordMatch(partLower, a)) == true;
                    if (!matchesPart) continue;

                    // Also match on operationType when available
                    if (!string.IsNullOrEmpty(pPageOp.OperationType))
                    {
                        var pPageOpType = pPageOp.OperationType.ToLowerInvariant();
                        // Only enforce op-type match if the estimate line has an operation type
                        if (!string.IsNullOrEmpty(opLower) && pPageOpType == "replace" &&
                            !opLower.Contains("repl") && !opLower.Contains("r&r") && !opLower.Contains("section"))
                            continue;
                    }

                    if (pPageOp.NotIncluded == null) continue;

                    var sourceCitation = BuildSourceCitation(pPageOp.SourceRefs);

                    foreach (var notIncludedText in pPageOp.NotIncluded)
                    {
                        // Extract the key operation name (text before the parenthesized source citation)
                        var operationName = notIncludedText;
                        var parenIdx = notIncludedText.IndexOf('(');
                        if (parenIdx > 0)
                            operationName = notIncludedText.Substring(0, parenIdx).Trim();

                        // Use pre-computed index instead of scanning all lines
                        bool isPresent = IsItemInEstimate(operationName);

                        if (isPresent) continue;

                        var category = MapNotIncludedToCategory(operationName);
                        var dedupKey = $"{operationName.ToLowerInvariant()}|{category}";
                        if (dedupKeys.Contains(dedupKey)) continue;
                        dedupKeys.Add(dedupKey);

                        // Look for matching metOperations entry for suggested fix
                        SuggestedFix? suggestedFix = null;
                        var opNameLower = operationName.ToLowerInvariant();
                        var matchingMet = pPageOp.MetOperations?.FirstOrDefault(m =>
                            opNameLower.Contains(m.Description?.ToLowerInvariant() ?? "~") ||
                            m.Description?.ToLowerInvariant().Contains(opNameLower) == true);

                        if (matchingMet != null)
                        {
                            suggestedFix = new SuggestedFix
                            {
                                Description = matchingMet.Description ?? operationName,
                                LaborHours = matchingMet.Hours,
                                OperationType = matchingMet.LaborType ?? "Add"
                            };
                        }

                        result.Issues.Add(new ScoringIssue
                        {
                            Source = "PPage",
                            Category = category,
                            Severity = IssueSeverity.Medium,
                            PointDeduction = GetPointDeduction("medium"),
                            Title = operationName,
                            Description = notIncludedText,
                            WhyNeeded = !string.IsNullOrEmpty(sourceCitation) ? $"Per {sourceCitation}" : "",
                            TriggeredBy = lines[li].PartName ?? "",
                            SourceDetail = lines[li].PartName ?? "",
                            SuggestedFix = suggestedFix
                        });
                    }
                }
            }
        }

        #endregion

        #region R&I Checks

        private void CheckRIOperations(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            // Collect bumper/door/quarter/hood/fender work using index
            string? frontBumperTrigger = null, rearBumperTrigger = null, doorTrigger = null;
            string? quarterTrigger = null, hoodTrigger = null, fenderTrigger = null;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].IsManualLine) continue;
                var p = _idxPartsLower[i];

                if (p.Contains("bumper") || p.Contains("fascia"))
                {
                    if (p.Contains("rear") || p.Contains("rr ") || p.Contains("r bumper") || p.Contains("r fascia"))
                        rearBumperTrigger ??= lines[i].PartName ?? "Rear Bumper";
                    else
                        frontBumperTrigger ??= lines[i].PartName ?? "Front Bumper";
                }
                if ((p.Contains("door") && !p.Contains("fuel")) && doorTrigger == null)
                    doorTrigger = lines[i].PartName ?? "Door";
                if ((p.Contains("quarter") || p.Contains("qtr")) && quarterTrigger == null)
                    quarterTrigger = lines[i].PartName ?? "Quarter Panel";
                if (p.Contains("hood") && hoodTrigger == null)
                    hoodTrigger = "Hood";
                if (p.Contains("fender") && fenderTrigger == null)
                    fenderTrigger = lines[i].PartName ?? "Fender";
            }

            if (frontBumperTrigger != null)
                CheckForMissingRI(result, frontBumperTrigger, new[] {
                    ("R&I Fog Lamps", 0.3m, new[] { "fog" }),
                    ("R&I Parking Sensors", 0.2m, new[] { "sensor", "park" }),
                    ("R&I Front Camera", 0.3m, new[] { "camera", "front camera" })
                });
            if (rearBumperTrigger != null)
                CheckForMissingRI(result, rearBumperTrigger, new[] {
                    ("R&I Parking Sensors", 0.2m, new[] { "sensor", "park" }),
                    ("R&I Rear Camera", 0.3m, new[] { "camera", "rear camera", "backup camera" })
                });
            if (doorTrigger != null)
                CheckForMissingRI(result, doorTrigger, new[] {
                    ("R&I Mirror", 0.3m, new[] { "mirror" }),
                    ("R&I Door Handle", 0.3m, new[] { "handle" }),
                    ("R&I Door Trim Panel", 0.3m, new[] { "trim", "molding" })
                });
            if (quarterTrigger != null)
                CheckForMissingRI(result, quarterTrigger, new[] {
                    ("R&I Tail Light", 0.3m, new[] { "tail" }),
                    ("R&I Fuel Door", 0.2m, new[] { "fuel door", "fuel" }),
                    ("R&I Quarter Moldings", 0.2m, new[] { "molding", "moulding" })
                });
            if (hoodTrigger != null)
                CheckForMissingRI(result, hoodTrigger, new[] {
                    ("R&I Hood Insulator", 0.3m, new[] { "insulator", "insulation" }),
                    ("R&I Hood Struts", 0.2m, new[] { "strut" })
                });
            if (fenderTrigger != null)
                CheckForMissingRI(result, fenderTrigger, new[] {
                    ("R&I Fender Liner", 0.3m, new[] { "liner", "splash" }),
                    ("R&I Tire/Wheel", 0.2m, new[] { "wheel", "tire" })
                });
        }

        private void CheckForMissingRI(EstimateScoringResult result,
            string triggeredBy, (string Name, decimal Hours, string[] Keywords)[] riItems)
        {
            foreach (var item in riItems)
            {
                // Only suggest R&I if the component is actually referenced in the estimate
                bool componentMentioned = item.Keywords.Any(k => AnyLineContains(k));

                if (!componentMentioned) continue;

                // Check if the R&I itself is already on the estimate
                // Use keyword-based matching: look for any line with R&I/R&R/Repl/Incl operation
                // that also contains one of the component keywords
                bool hasRI = false;
                for (int i = 0; i < _idxPartsLower.Count; i++)
                {
                    var combined = _idxPartsLower[i] + " " + _idxDescsLower[i];
                    bool isRiOrReplOp = _idxOpsLower[i].Contains("r&i") || _idxOpsLower[i].Contains("r&r") ||
                                  _idxOpsLower[i].Contains("repl") || _idxOpsLower[i].Contains("rpr") ||
                                  combined.Contains("r&i") || combined.Contains("r&r") ||
                                  combined.Contains("remove") || combined.Contains("aim") ||
                                  _idxOpsLower[i] == "incl.";
                    if (isRiOrReplOp && item.Keywords.Any(k => combined.Contains(k)))
                    { hasRI = true; break; }
                }
                // Also check via the full substring as fallback
                if (!hasRI)
                    hasRI = AnyLineContains(item.Name.ToLowerInvariant());

                if (!hasRI)
                {
                    var existing = result.Issues.FirstOrDefault(i => i.Title == item.Name);
                    if (existing == null)
                    {
                        result.Issues.Add(new ScoringIssue
                        {
                            Category = IssueCategoryType.RandI,
                            Severity = IssueSeverity.Medium,
                            Title = item.Name,
                            Description = $"Remove and install {item.Name.Replace("R&I ", "")}",
                            WhyNeeded = $"Required for access to {triggeredBy}",
                            TriggeredBy = triggeredBy,
                            SuggestedFix = new SuggestedFix
                            {
                                OperationType = "R&I",
                                Description = item.Name,
                                LaborHours = item.Hours,
                                LaborCategory = "Body"
                            },
                            PointDeduction = 2
                        });
                    }
                }
            }
        }

        #endregion

        #region Diagnostic Scan Checks

        private void CheckDiagnosticScans(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            // Use pre-computed index for all checks
            bool needsScans = result.EstimateTotal > 2500 ||
                _idxPartsLower.Any(p =>
                    p.Contains("bumper") || p.Contains("fender") || p.Contains("hood") ||
                    p.Contains("door") || p.Contains("quarter") || p.Contains("roof") ||
                    p.Contains("windshield") || p.Contains("frame") || p.Contains("rail") ||
                    p.Contains("strut tower") || p.Contains("structural") ||
                    p.Contains("airbag") || p.Contains("srs"));

            if (needsScans)
            {
                bool hasPreScan = false, hasPostScan = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    var combined = _idxDescsLower[i] + " " + _idxPartsLower[i];
                    if (!hasPreScan && (combined.Contains("pre") || combined.Contains("before")) && combined.Contains("scan"))
                        hasPreScan = true;
                    if (!hasPostScan && (combined.Contains("post") || combined.Contains("after")) && combined.Contains("scan"))
                        hasPostScan = true;
                    if (hasPreScan && hasPostScan) break;
                }

                // Also check for generic scan operations
                bool hasAnyScan = lines.Any(l =>
                {
                    var desc = l.Description?.ToLowerInvariant() ?? "";
                    var part = l.PartName?.ToLowerInvariant() ?? "";
                    var combined = $"{desc} {part}";
                    return combined.Contains("diagnostic") || combined.Contains("scan");
                });

                if (!hasPreScan && !hasAnyScan)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Diagnostic,
                        Severity = IssueSeverity.Critical,
                        Title = "Pre-Repair Scan",
                        Description = "Diagnostic scan before any work",
                        WhyNeeded = "Document existing codes and system status - OEM requirement",
                        TriggeredBy = result.EstimateTotal > 2500 ? $"Estimate over $2,500" : "Structural work",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Pre-Repair Diagnostic Scan",
                            LaborHours = 0.5m,
                            LaborCategory = "Mech"
                        },
                        PointDeduction = 8
                    });
                }

                if (!hasPostScan && !hasAnyScan)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Diagnostic,
                        Severity = IssueSeverity.Critical,
                        Title = "Post-Repair Scan",
                        Description = "Diagnostic scan after all work complete",
                        WhyNeeded = "Verify no new codes, clear repair-related DTCs",
                        TriggeredBy = result.EstimateTotal > 2500 ? $"Estimate over $2,500" : "Structural work",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Post-Repair Diagnostic Scan",
                            LaborHours = 0.5m,
                            LaborCategory = "Mech"
                        },
                        PointDeduction = 8
                    });
                }
            }
        }

        #endregion

        #region ADAS Calibration Checks

        private void CheckADASCalibrations(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            // Use pre-computed index
            bool hasWindshield = _idxPartsLower.Any(p => p.Contains("windshield") || p.Contains("w/s"));

            if (hasWindshield)
            {
                bool hasCalibration = AnyLineContains("calibrat") || AnyLineContains("adas");

                if (!hasCalibration)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Calibration,
                        Severity = IssueSeverity.Critical,
                        Title = "ADAS Camera Calibration",
                        Description = "Recalibrate forward-facing camera",
                        WhyNeeded = "Camera mounted to windshield - OEM requires calibration after R&R",
                        TriggeredBy = "Windshield",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Sublet",
                            Description = "ADAS Camera Calibration",
                            EstimatedCost = 350,
                            LaborCategory = "Parts"
                        },
                        PointDeduction = 10
                    });
                }
            }

            // Check for alignment with ADAS implications
            bool hasAlignment = _idxDescsLower.Any(d => d.Contains("align"));

            bool hasSuspensionWork = _idxPartsLower.Any(p =>
                p.Contains("strut") || p.Contains("control arm") ||
                p.Contains("tie rod") || p.Contains("suspension"));

            if (hasSuspensionWork && !hasAlignment)
            {
                result.Issues.Add(new ScoringIssue
                {
                    Category = IssueCategoryType.Calibration,
                    Severity = IssueSeverity.High,
                    Title = "4-Wheel Alignment",
                    Description = "Check/adjust wheel alignment",
                    WhyNeeded = "Suspension work affects alignment settings",
                    TriggeredBy = "Suspension components",
                    SuggestedFix = new SuggestedFix
                    {
                        OperationType = "Add",
                        Description = "4-Wheel Alignment",
                        LaborHours = 1.0m,
                        LaborCategory = "Mech"
                    },
                    PointDeduction = 5
                });
            }

            // Check radar/sensor work
            bool hasRadarSensor = _idxPartsLower.Any(p =>
                p.Contains("radar") || p.Contains("sensor") || p.Contains("camera"));

            if (hasRadarSensor)
            {
                bool hasSensorCalibration = _idxDescsLower.Any(d => d.Contains("calibrat"));

                if (!hasSensorCalibration)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Calibration,
                        Severity = IssueSeverity.High,
                        Title = "Sensor Calibration",
                        Description = "Calibrate radar/camera sensor after R&R",
                        WhyNeeded = "ADAS sensors require calibration after removal",
                        TriggeredBy = "Radar/Camera Sensor",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Sublet",
                            Description = "ADAS Sensor Calibration",
                            EstimatedCost = 250,
                            LaborCategory = "Parts"
                        },
                        PointDeduction = 6
                    });
                }
            }
        }

        #endregion

        #region Global Rules

        private void CheckGlobalRules(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            // Check for clear coat on refinish operations
            bool hasRefinish = lines.Any(l => l.RefinishHours > 0);
            if (hasRefinish)
            {
                bool hasClearCoat = AnyLineContains("clear");

                // Clear coat is usually included, but check for 2-stage/3-stage
                bool hasTriCoat = _idxDescsLower.Any(d =>
                    d.Contains("tri-coat") || d.Contains("3-stage") ||
                    d.Contains("three stage") || d.Contains("pearl"));

                if (hasTriCoat)
                {
                    bool hasTriCoatTime = _idxDescsLower.Any(d =>
                        d.Contains("tri-coat") || d.Contains("tricoat") ||
                        d.Contains("three stage") || d.Contains("3-stage") || d.Contains("3 stage") ||
                        d.Contains("additional") || d.Contains("add for three") ||
                        d.Contains("add for 3"));

                    if (!hasTriCoatTime)
                    {
                        result.Issues.Add(new ScoringIssue
                        {
                            Category = IssueCategoryType.Refinish,
                            Severity = IssueSeverity.High,
                            Title = "3-Stage/Tri-Coat Time",
                            Description = "Additional refinish time for 3-stage paint",
                            WhyNeeded = "3-stage paint requires extra color application steps",
                            TriggeredBy = "Tri-coat/Pearl paint",
                            SuggestedFix = new SuggestedFix
                            {
                                OperationType = "Add",
                                Description = "3-Stage Paint Additional Time",
                                LaborHours = 0.5m,
                                LaborCategory = "Paint"
                            },
                            PointDeduction = 4
                        });
                    }
                }
            }

            // --- Spray-Out / Let-Down Panel ---
            // Any refinish job should have spray-out cards for color matching verification.
            if (hasRefinish)
            {
                bool hasSprayOut = AnyLineContains("spray out") || AnyLineContains("spray card") ||
                                   AnyLineContains("sprayout") || AnyLineContains("let down") ||
                                   AnyLineContains("letdown") || AnyLineContains("let-down");
                if (!hasSprayOut)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Refinish,
                        Severity = IssueSeverity.Medium,
                        Title = "Spray-Out / Let-Down Panel",
                        Description = "Spray-out cards verify color match before painting the vehicle",
                        WhyNeeded = "Required to confirm color accuracy and avoid costly re-sprays",
                        TriggeredBy = "Refinish operations present",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Refn",
                            Description = "Spray Out Cards (2-Stage)",
                            LaborHours = 0.5m,
                            LaborCategory = "Paint"
                        },
                        PointDeduction = 2
                    });
                }
            }

            // --- Color Sand & Buff on Metallic/Pearl ---
            // Metallic and pearl paints require color sanding and buffing for proper finish.
            if (hasRefinish)
            {
                bool hasMetallicOrPearl = _idxDescsLower.Any(d =>
                    d.Contains("metallic") || d.Contains("pearl") || d.Contains("mica") ||
                    d.Contains("tri-coat") || d.Contains("3-stage") || d.Contains("three stage"));

                // Also check raw text / part names for paint type indicators
                if (!hasMetallicOrPearl)
                    hasMetallicOrPearl = _idxAllText.Contains("metallic") || _idxAllText.Contains("pearl") ||
                                         _idxAllText.Contains("mica");

                if (hasMetallicOrPearl)
                {
                    bool hasColorSand = AnyLineContains("color sand") || AnyLineContains("colorsand") ||
                                        AnyLineContains("colour sand");
                    if (!hasColorSand)
                    {
                        result.Issues.Add(new ScoringIssue
                        {
                            Category = IssueCategoryType.Refinish,
                            Severity = IssueSeverity.Medium,
                            Title = "Color Sand — Metallic/Pearl",
                            Description = "Color sanding required for metallic and pearl finishes",
                            WhyNeeded = "Metallic/pearl paints need color sanding to eliminate orange peel and achieve OEM-quality finish",
                            TriggeredBy = "Metallic/Pearl/Mica paint detected",
                            SuggestedFix = new SuggestedFix
                            {
                                OperationType = "Refn",
                                Description = "Color Sand",
                                LaborHours = 0.5m,
                                LaborCategory = "Paint"
                            },
                            PointDeduction = 2
                        });
                    }
                }
            }

            // --- Wet/Dry Sand (De-Nib) on Repair Panels ---
            // Panels being repaired (not replaced) need sanding for proper adhesion & finish.
            {
                bool hasRepairWithRefinish = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (_idxOpsLower[i].Contains("rep") && !_idxOpsLower[i].Contains("repl") && lines[i].RefinishHours > 0)
                    { hasRepairWithRefinish = true; break; }
                }

                if (hasRepairWithRefinish)
                {
                    bool hasWetDrySand = AnyLineContains("wet") || AnyLineContains("de-nib") ||
                                         AnyLineContains("denib") || AnyLineContains("de nib");
                    if (!hasWetDrySand)
                    {
                        result.Issues.Add(new ScoringIssue
                        {
                            Category = IssueCategoryType.Refinish,
                            Severity = IssueSeverity.Medium,
                            Title = "Wet/Dry Sand (De-Nib)",
                            Description = "Wet/dry sanding needed on repaired panels for smooth finish",
                            WhyNeeded = "Repair panels require sanding between primer and paint coats to achieve proper finish",
                            TriggeredBy = "Panel repair with refinish",
                            SuggestedFix = new SuggestedFix
                            {
                                OperationType = "Refn",
                                Description = "Wet/Dry Sand & De-Nib",
                                LaborHours = 0.3m,
                                LaborCategory = "Paint"
                            },
                            PointDeduction = 2
                        });
                    }
                }
            }

            // --- Color Tint on Multi-Stage / Tri-Coat ---
            // Multi-stage paints often require tinting the intermediate coat for proper color depth.
            if (hasRefinish)
            {
                bool hasMultiStage = _idxDescsLower.Any(d =>
                    d.Contains("tri-coat") || d.Contains("3-stage") || d.Contains("three stage") ||
                    d.Contains("pearl") || d.Contains("mica") || d.Contains("multi-stage") ||
                    d.Contains("2-tone") || d.Contains("two-tone") || d.Contains("two tone"));

                if (hasMultiStage)
                {
                    bool hasTint = AnyLineContains("tint") || AnyLineContains("color tint") ||
                                   AnyLineContains("colour tint") || AnyLineContains("intermediate");
                    if (!hasTint)
                    {
                        result.Issues.Add(new ScoringIssue
                        {
                            Category = IssueCategoryType.Refinish,
                            Severity = IssueSeverity.Medium,
                            Title = "Color Tint — Multi-Stage Paint",
                            Description = "Color tint for intermediate coat on multi-stage / tri-coat paint",
                            WhyNeeded = "Multi-stage paints need tinted midcoats for proper color depth and match",
                            TriggeredBy = "Multi-stage/Tri-coat paint detected",
                            SuggestedFix = new SuggestedFix
                            {
                                OperationType = "Refn",
                                Description = "Color Tint (2-Stage)",
                                LaborHours = 0.5m,
                                LaborCategory = "Paint"
                            },
                            PointDeduction = 3
                        });
                    }
                }
            }

            // --- Buff / Cut & Buff when refinishing ---
            // Refinished panels need buffing for final finish quality.
            if (hasRefinish)
            {
                bool hasBuff = AnyLineContains("buff") || AnyLineContains("polish") ||
                               AnyLineContains("cut and buff") || AnyLineContains("cut & buff") ||
                               AnyLineContains("rub out") || AnyLineContains("rub-out");
                if (!hasBuff)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Refinish,
                        Severity = IssueSeverity.Medium,
                        Title = "Buff / Cut & Buff",
                        Description = "Final buffing/polishing needed after refinish for OEM-quality finish",
                        WhyNeeded = "All refinished panels require buffing to remove imperfections and match factory finish",
                        TriggeredBy = "Refinish operations present",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Refn",
                            Description = "Buff & Polish",
                            LaborHours = 0.3m,
                            LaborCategory = "Paint"
                        },
                        PointDeduction = 2
                    });
                }
            }

            // Check for battery disconnect on structural work
            bool hasStructuralWelding = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if ((_idxPartsLower[i].Contains("quarter") || _idxPartsLower[i].Contains("rocker") ||
                     _idxPartsLower[i].Contains("rail") || _idxPartsLower[i].Contains("pillar") ||
                     _idxPartsLower[i].Contains("roof")) && _idxOpsLower[i].Contains("repl"))
                { hasStructuralWelding = true; break; }
            }

            if (hasStructuralWelding)
            {
                bool hasBatteryDisconnect = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    var combined = _idxPartsLower[i] + " " + _idxDescsLower[i];
                    if ((combined.Contains("battery") && combined.Contains("disconnect")) ||
                        (combined.Contains("battery") && combined.Contains("d&r")) ||
                        combined.Contains("battery disconnect") || combined.Contains("disconnect battery") ||
                        combined.Contains("disconnect and reconnect"))
                    { hasBatteryDisconnect = true; break; }
                }

                if (!hasBatteryDisconnect)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Electrical,
                        Severity = IssueSeverity.High,
                        Title = "Battery Disconnect/Reconnect",
                        Description = "Disconnect battery before welding, reconnect and initialize after",
                        WhyNeeded = "Protect vehicle electronics from welding current",
                        TriggeredBy = "Structural panel replacement",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Battery Disconnect/Reconnect & Initialize",
                            LaborHours = 0.6m,
                            LaborCategory = "Mech"
                        },
                        PointDeduction = 4
                    });
                }
            }
        }

        #endregion

        #region Must-Haves Check

        private void CheckMustHaves(List<ParsedEstimateLine> lines, EstimateScoringResult result, string? insuranceCompany = null)
        {
            // Detect vehicle fuel type from vehicle info for context-aware filtering
            var vehicleFuelType = PatternIntelligenceService.Instance.ClassifyVehicleFuelType(result.VehicleInfo);
            var mustHaves = GhostConfigService.Instance.GetMustHavesForContext(insuranceCompany, vehicleFuelType);
            if (mustHaves == null || mustHaves.Count == 0) return;

            // Build combined text blob for condition evaluation (reuse indexed data)
            var combinedTextLower = _idxAllText + " " + string.Join(" ", _idxOpsLower);

            // Pre-normalize all lines ONCE for must-have matching (avoids re-normalizing per must-have)
            var preNormalizedLines = new List<string>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                var combined = GhostConfigService.NormalizeMustHaveDesc(
                    (lines[i].PartName ?? "") + " " + (lines[i].Description ?? ""));
                preNormalizedLines.Add(combined);
            }

            // Build dedup key set from scoring issues already found — must-haves that duplicate
            // an existing scoring issue (e.g., "Battery Disconnect/Reconnect") are suppressed.
            var existingKeys = new HashSet<string>(result.Issues.Select(i => NormalizeDedupKey(i.Title)));

            foreach (var mh in mustHaves)
            {
                if (!mh.Enabled || string.IsNullOrWhiteSpace(mh.Description)) continue;

                // Skip must-haves whose condition is not met by the estimate context
                if (!EstimateConditionEvaluator.Evaluate(mh.Conditions, combinedTextLower))
                    continue;

                // Skip junk entries
                var descLower = mh.Description.ToLowerInvariant().Trim();
                if (descLower is "description" or "category" or "operation" or "setup" or "")
                    continue;

                // Pre-normalize the must-have ONCE (not per-line)
                var mhNorm = GhostConfigService.NormalizeMustHaveDesc(mh.Description);
                var mhWords = GhostConfigService.ExtractSignificantWords(mhNorm);

                // Count matches using pre-normalized data
                int matchCount = 0;
                for (int i = 0; i < preNormalizedLines.Count; i++)
                {
                    if (GhostConfigService.MatchesMustHave(preNormalizedLines[i], mhNorm, mhWords))
                        matchCount++;
                }

                // Synonym fallback: if no direct matches, check if a synonym is on the estimate
                if (matchCount == 0 && IsItemInEstimate(mh.Description))
                    matchCount = mh.MinCount; // treat as found via synonym

                if (matchCount < mh.MinCount)
                {
                    var missing = mh.MinCount - matchCount;
                    var title = matchCount == 0
                        ? $"Missing: {mh.Description}"
                        : $"Need {missing} more: {mh.Description}";

                    // Cross-source dedup: skip if scoring already flagged an equivalent issue
                    var mhKey = NormalizeDedupKey(title);
                    if (existingKeys.Contains(mhKey)) continue;

                    // Also check synonym groups — e.g., "battery disconnect" vs "disconnect and reconnect battery"
                    bool synonymMatch = false;
                    var mhDescLower = mh.Description.ToLowerInvariant();
                    foreach (var group in _synonymGroups)
                    {
                        if (group.Any(s => mhDescLower.Contains(s)) &&
                            result.Issues.Any(existing => group.Any(s => existing.Title.ToLowerInvariant().Contains(s))))
                        { synonymMatch = true; break; }
                    }
                    if (synonymMatch) continue;

                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Other,
                        Severity = mh.PointDeduction >= 8 ? IssueSeverity.Critical :
                                   mh.PointDeduction >= 5 ? IssueSeverity.High : IssueSeverity.Medium,
                        Title = title,
                        Description = $"Your must-have requires at least {mh.MinCount} \"{mh.Description}\" — found {matchCount}",
                        WhyNeeded = "Required by your shop's must-have settings",
                        TriggeredBy = "Must-Have Rule",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = mh.Description,
                            LaborHours = mh.ExpectedHours + mh.RefinishHours,
                            EstimatedCost = mh.ExpectedPrice,
                            LaborCategory = MapOpTypeToLaborCategory(mh.OpType)
                        },
                        PointDeduction = mh.PointDeduction * missing
                    });
                }
            }
        }

        /// <summary>
        /// Fuzzy matching for must-have operations against estimate lines.
        /// Handles truncated descriptions, hyphen/space variations, and abbreviations.
        /// </summary>
        internal static bool MustHaveFuzzyMatch(string mustHaveLower, ParsedEstimateLine line)
        {
            var partLower = line.PartName?.ToLowerInvariant() ?? "";
            var descLower = line.Description?.ToLowerInvariant() ?? "";
            var combined = partLower + " " + descLower;

            // Normalize hyphens to spaces for comparison
            var normalizedMH = mustHaveLower.Replace("-", " ").Replace("/", " ");
            var normalizedLine = combined.Replace("-", " ").Replace("/", " ");

            // 1. Direct substring match: line contains must-have (NOT the reverse — a short line
            //    like "scan" must not match "pre-repair scan diagnostic" via reverse-contains)
            if (normalizedLine.Contains(normalizedMH) && normalizedMH.Length >= 3)
                return true;

            // 2. Word-based matching: extract significant words (3+ chars) and check overlap
            var mhWords = normalizedMH.Split(new[] { ' ', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !_fuzzyStopWords.Contains(w)).ToList();
            var lineWords = normalizedLine.Split(new[] { ' ', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !_fuzzyStopWords.Contains(w)).ToHashSet();

            if (mhWords.Count == 0) return false;

            // Count how many must-have words appear in the line (prefix match for truncated words)
            // Only forward direction: line-word starts with must-have prefix (not the reverse)
            int matched = mhWords.Count(mw => lineWords.Any(lw =>
                lw == mw ||
                (mw.Length >= 5 && lw.Length >= 5 && lw.StartsWith(mw.Substring(0, 5)))));

            // If 2+ words match, or if it's a short must-have (1-2 words) and all match
            if (mhWords.Count <= 2)
                return matched == mhWords.Count;
            return matched >= 2 && (double)matched / mhWords.Count >= 0.5;
        }

        #endregion

        #region Score Calculation

        /// <summary>
        /// Convert smart suggestions to scoring issues so they can be merged into the unified panel.
        /// Point deductions are halved since the scoring engine already catches the important ones at full weight.
        /// </summary>
        public static List<ScoringIssue> ConvertFromSuggestions(List<SmartSuggestedOperation> suggestions)
        {
            var issues = new List<ScoringIssue>();
            foreach (var s in suggestions)
            {
                var category = s.Category switch
                {
                    SuggestionCategory.Calibration => IssueCategoryType.Calibration,
                    SuggestionCategory.Diagnostic => IssueCategoryType.Diagnostic,
                    SuggestionCategory.Electrical => IssueCategoryType.Electrical,
                    SuggestionCategory.Materials => IssueCategoryType.Materials,
                    SuggestionCategory.RAndI => IssueCategoryType.RandI,
                    SuggestionCategory.Labor => IssueCategoryType.Labor,
                    SuggestionCategory.Refinish => IssueCategoryType.Refinish,
                    SuggestionCategory.Mechanical => IssueCategoryType.Mechanical,
                    _ => IssueCategoryType.Other
                };

                var severity = s.Priority?.ToLowerInvariant() switch
                {
                    "critical" => IssueSeverity.Critical,
                    "high" => IssueSeverity.High,
                    "medium" => IssueSeverity.Medium,
                    _ => IssueSeverity.Low
                };

                // Halved point deductions for smart-sourced items
                var baseDeduction = severity switch
                {
                    IssueSeverity.Critical => 5,
                    IssueSeverity.High => 3,
                    IssueSeverity.Medium => 1,
                    _ => 0
                };

                issues.Add(new ScoringIssue
                {
                    Category = category,
                    Severity = severity,
                    Title = s.Item,
                    Description = s.Description ?? s.Item,
                    WhyNeeded = s.WhyNeeded ?? "",
                    TriggeredBy = s.SourcePart,
                    PointDeduction = baseDeduction,
                    Source = "Smart",
                    SourceDetail = s.SourcePart,
                    SuggestedFix = new SuggestedFix
                    {
                        OperationType = "Add",
                        Description = s.Description ?? s.Item,
                        LaborHours = s.LaborHours,
                        EstimatedCost = s.TypicalCost,
                        DegReference = s.DegReference,
                        LaborCategory = MapIssueCategoryToLaborCategory(category)
                    }
                });
            }
            return issues;
        }

        private static readonly string[] _partNameKeywords =
            { "panel", "bumper", "fender", "door", "hood", "trunk", "liftgate", "roof", "rail", "pillar", "rocker" };
        private static readonly string[] _operationVerbs =
            { "r&i", "r and i", "blend", "refinish", "corrosion", "sealer", "prime", "mask", "calibrat",
              "scan", "disconnect", "clean", "cover", "protect", "weld", "adhesion", "flex", "denib", "sand" };

        /// <summary>
        /// Convert learned operations (from EstimateLearningService) to scoring issues
        /// so they can be merged into the unified scoring panel.
        /// Learned patterns are suggestions — low deduction weight (1 pt each).
        /// </summary>
        public static List<ScoringIssue> ConvertFromLearnedOperations(
            List<GeneratedOperation> operations,
            ManualLinePattern? manualPattern,
            string sourcePartName)
        {
            var issues = new List<ScoringIssue>();

            foreach (var op in operations)
            {
                // Skip items seen only once — too low confidence to suggest
                if (op.TimesUsed < 2)
                    continue;

                // Skip items with no actionable data
                if (op.LaborHours <= 0 && op.RepairHours <= 0 && op.Price <= 0)
                    continue;

                // Skip garbage descriptions
                var opDesc = op.Description?.Trim() ?? "";
                if (opDesc.Length < 5 || opDesc.Length > 100)
                    continue;

                // Skip truncated descriptions (ends with "&" or "and", or starts with "for ")
                if (opDesc.EndsWith(" &") || opDesc.EndsWith(" and") || opDesc.StartsWith("for "))
                    continue;

                // Skip suspiciously high hours — legitimate missing ops rarely exceed 4 hrs
                if (op.LaborHours + op.RepairHours > 4.0m)
                    continue;

                // Skip bare part names without an operation verb (e.g., "Rear body panel | 8.0 hr")
                var opDescLower = opDesc.ToLowerInvariant();
                bool looksLikePartName = _partNameKeywords.Any(k => opDescLower.Contains(k));
                bool hasOperationVerb = _operationVerbs.Any(v => opDescLower.Contains(v));
                if (looksLikePartName && !hasOperationVerb)
                    continue;

                var category = op.Category?.ToLowerInvariant() switch
                {
                    "calibration" => IssueCategoryType.Calibration,
                    "diagnostic" => IssueCategoryType.Diagnostic,
                    "electrical" => IssueCategoryType.Electrical,
                    "materials" or "material" => IssueCategoryType.Materials,
                    "r&i" or "ri" or "r and i" => IssueCategoryType.RandI,
                    "labor" => IssueCategoryType.Labor,
                    "refinish" or "rfn" => IssueCategoryType.Refinish,
                    "mechanical" or "mech" => IssueCategoryType.Mechanical,
                    _ => IssueCategoryType.Other
                };

                issues.Add(new ScoringIssue
                {
                    Category = category,
                    Severity = IssueSeverity.Low,
                    Title = op.Description,
                    Description = $"{op.OperationType}: {op.Description}",
                    WhyNeeded = $"Seen in {op.TimesUsed} previous estimates",
                    TriggeredBy = sourcePartName,
                    PointDeduction = 1,
                    Source = "Learned",
                    SourceDetail = sourcePartName,
                    SuggestedFix = new SuggestedFix
                    {
                        OperationType = op.OperationType,
                        Description = op.Description,
                        LaborHours = op.LaborHours + op.RepairHours,
                        EstimatedCost = op.Price,
                        LaborCategory = MapIssueCategoryToLaborCategory(category)
                    }
                });
            }

            if (manualPattern != null)
            {
                foreach (var manual in manualPattern.ManualLines)
                {
                    // Skip items seen only once — too low confidence to suggest
                    if (manual.TimesUsed < 2)
                        continue;

                    // Skip items with zero labor AND zero price — no actionable value
                    if (manual.LaborUnits <= 0 && manual.RefinishUnits <= 0 && manual.AvgPrice <= 0 && manual.Price <= 0)
                        continue;

                    // Skip descriptions that are too short or too long (fragments/boilerplate)
                    var desc = manual.Description?.Trim() ?? "";
                    if (desc.Length < 5 || desc.Length > 100)
                        continue;

                    // Skip truncated descriptions
                    if (desc.EndsWith(" &") || desc.EndsWith(" and") || desc.StartsWith("for "))
                        continue;

                    // Skip suspiciously high hours
                    if (manual.LaborUnits + manual.RefinishUnits > 4.0m)
                        continue;

                    // Skip bare part names without an operation verb
                    var descLower = desc.ToLowerInvariant();
                    bool looksLikePartName2 = _partNameKeywords.Any(k => descLower.Contains(k));
                    bool hasOperationVerb2 = _operationVerbs.Any(v => descLower.Contains(v));
                    if (looksLikePartName2 && !hasOperationVerb2)
                        continue;

                    issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Other,
                        Severity = IssueSeverity.Low,
                        Title = manual.ManualLineType,
                        Description = manual.Description,
                        WhyNeeded = $"Seen in {manual.TimesUsed} previous estimates",
                        TriggeredBy = sourcePartName,
                        PointDeduction = 1,
                        Source = "Learned",
                        SourceDetail = sourcePartName,
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Manual",
                            Description = manual.ManualLineType,
                            LaborHours = manual.LaborUnits + manual.RefinishUnits,
                            EstimatedCost = manual.AvgPrice,
                            LaborCategory = manual.RefinishUnits > 0 ? "Paint" : "Body"
                        }
                    });
                }
            }

            return issues;
        }

        /// <summary>
        /// Recalculate score/grade/counts after issues have been modified (e.g., after merging smart suggestions).
        /// </summary>
        public void RecalculateScore(EstimateScoringResult result) => CalculateFinalScore(result);

        private void CalculateFinalScore(EstimateScoringResult result)
        {
            // Start at 100
            int baseScore = 100;

            // Apply diminishing returns: sort deductions descending, each successive one decays
            const double decayFactor = 0.92;
            const int maxTotalDeduction = 60; // Minimum possible score = 40

            var deductions = result.Issues
                .Where(i => i.Category != IssueCategoryType.Blend) // Exclude blend considerations from score
                .Select(i =>
                {
                    // Cap Low severity items at 1 point
                    int pts = i.PointDeduction;
                    if (i.Severity == IssueSeverity.Low && pts > 1) pts = 1;
                    return pts;
                })
                .OrderByDescending(d => d)
                .ToList();

            double totalDeduction = 0;
            double multiplier = 1.0;
            foreach (var d in deductions)
            {
                totalDeduction += d * multiplier;
                multiplier *= decayFactor;
            }

            int cappedDeduction = Math.Min(maxTotalDeduction, (int)Math.Round(totalDeduction));
            result.OverallScore = Math.Max(baseScore - maxTotalDeduction, baseScore - cappedDeduction);

            // Calculate category breakdown
            result.CategoryScores = new Dictionary<string, int>
            {
                ["Materials"] = CalculateCategoryScore(result.Issues, IssueCategoryType.Materials),
                ["Blend"] = CalculateCategoryScore(result.Issues, IssueCategoryType.Blend),
                ["R&I"] = CalculateCategoryScore(result.Issues, IssueCategoryType.RandI),
                ["Diagnostics"] = CalculateCategoryScore(result.Issues, IssueCategoryType.Diagnostic),
                ["Calibrations"] = CalculateCategoryScore(result.Issues, IssueCategoryType.Calibration),
                ["Refinish"] = CalculateCategoryScore(result.Issues, IssueCategoryType.Refinish)
            };

            // Set grade (softer thresholds)
            result.Grade = result.OverallScore switch
            {
                >= 92 => "A+",
                >= 85 => "A",
                >= 78 => "B+",
                >= 70 => "B",
                >= 62 => "C+",
                >= 55 => "C",
                >= 40 => "D",
                _ => "F"
            };

            // Count issues by severity
            result.CriticalCount = result.Issues.Count(i => i.Severity == IssueSeverity.Critical);
            result.HighCount = result.Issues.Count(i => i.Severity == IssueSeverity.High);
            result.MediumCount = result.Issues.Count(i => i.Severity == IssueSeverity.Medium);
            result.LowCount = result.Issues.Count(i => i.Severity == IssueSeverity.Low);

            // Calculate potential recovery per category using actual labor rates
            // If "Use estimate rates" is enabled and the estimate had rates, use those; otherwise config defaults
            var ghostConfig = GhostConfigService.Instance;
            bool useEstRates = ghostConfig.Config.LaborRates.UseEstimateRates;
            decimal bodyRate = (useEstRates && result.EstimateBodyRate > 0) ? result.EstimateBodyRate : ghostConfig.GetEffectiveBodyRate();
            decimal paintRate = (useEstRates && result.EstimatePaintRate > 0) ? result.EstimatePaintRate : ghostConfig.GetEffectivePaintRate();
            decimal mechRate = (useEstRates && result.EstimateMechRate > 0) ? result.EstimateMechRate : ghostConfig.GetEffectiveMechRate();

            decimal bodyLaborHrs = 0, paintLaborHrs = 0, mechLaborHrs = 0, partsRecovery = 0;

            foreach (var issue in result.Issues.Where(i => i.Category != IssueCategoryType.Blend))
            {
                var fix = issue.SuggestedFix;
                if (fix == null) continue;

                var cat = fix.LaborCategory ?? "";
                switch (cat.ToLowerInvariant())
                {
                    case "paint":
                    case "refinish":
                    case "rfn":
                        paintLaborHrs += fix.LaborHours;
                        partsRecovery += fix.EstimatedCost;
                        break;
                    case "mechanical":
                    case "mech":
                        mechLaborHrs += fix.LaborHours;
                        partsRecovery += fix.EstimatedCost;
                        break;
                    case "parts":
                        partsRecovery += fix.EstimatedCost;
                        break;
                    default: // Body or unset
                        bodyLaborHrs += fix.LaborHours;
                        partsRecovery += fix.EstimatedCost;
                        break;
                }
            }

            result.BodyLaborRecovery = bodyLaborHrs;
            result.PaintLaborRecovery = paintLaborHrs;
            result.MechLaborRecovery = mechLaborHrs;
            result.PartsRecovery = partsRecovery;
            result.BodyRate = bodyRate;
            result.PaintRate = paintRate;
            result.MechRate = mechRate;

            result.PotentialLaborRecovery = bodyLaborHrs + paintLaborHrs + mechLaborHrs;
            result.PotentialCostRecovery = (bodyLaborHrs * bodyRate) + (paintLaborHrs * paintRate) + (mechLaborHrs * mechRate) + partsRecovery;

            // Classify action types
            foreach (var issue in result.Issues)
            {
                issue.ActionType = ClassifyActionType(issue);
            }

            // Generate summary
            if (result.OverallScore >= 85)
            {
                result.Summary = "Excellent! This estimate is very complete.";
            }
            else if (result.OverallScore >= 70)
            {
                result.Summary = $"Good estimate. Found {result.Issues.Count} potential improvements.";
            }
            else if (result.OverallScore >= 55)
            {
                result.Summary = $"Fair estimate. Missing {result.CriticalCount + result.HighCount} important items.";
            }
            else
            {
                result.Summary = $"Needs work. {result.CriticalCount} critical items missing.";
            }
        }

        private static IssueActionType ClassifyActionType(ScoringIssue issue)
        {
            // Low severity → verify / optional
            if (issue.Severity == IssueSeverity.Low)
                return IssueActionType.VerifyOptional;

            // ADAS calibrations and items with DEG references → check with OEM
            if (issue.Category == IssueCategoryType.Calibration)
                return IssueActionType.CheckWithOEM;

            if (!string.IsNullOrEmpty(issue.SuggestedFix?.DegReference))
                return IssueActionType.CheckWithOEM;

            var titleLower = issue.Title.ToLowerInvariant();
            if (titleLower.Contains("adas") || titleLower.Contains("calibrat"))
                return IssueActionType.CheckWithOEM;

            // Everything else → add to estimate
            return IssueActionType.AddToEstimate;
        }

        private int CalculateCategoryScore(List<ScoringIssue> issues, IssueCategoryType category)
        {
            var categoryIssues = issues.Where(i => i.Category == category).ToList();
            if (categoryIssues.Count == 0) return 100;

            int deductions = categoryIssues.Sum(i => i.PointDeduction);
            return Math.Max(0, 100 - (deductions * 3)); // Amplify for category score
        }

        #endregion

        #region Pre-processing

        /// <summary>
        /// Join continuation lines where wrapped descriptions got split across lines.
        /// e.g., a line with "Rub-Out &amp;" followed by a bare "Buff" line → merge into previous line's description.
        /// Also handles lines starting with "&amp;" like "&amp; Buff", "Block Sand", etc. that are clearly
        /// continuations of the previous operation.
        /// </summary>
        private static List<ParsedEstimateLine> JoinContinuationLines(List<ParsedEstimateLine> lines)
        {
            var result = new List<ParsedEstimateLine>(lines.Count);

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var partName = (line.PartName ?? "").Trim();
                var opType = (line.OperationType ?? "").Trim();

                // Detect continuation line: no operation type, and either:
                // - starts with "&" or "and" (e.g., "& Buff", "and Buff")
                // - previous line's PartName ends with "&" or "and"
                // - is a known refinish sub-operation (Rub-Out, Block Sand, etc.)
                bool isContinuation = string.IsNullOrEmpty(opType) && result.Count > 0 &&
                    (partName.StartsWith("&") || partName.StartsWith("and ", StringComparison.OrdinalIgnoreCase) ||
                     IsRefinishContinuation(partName));

                // Also check: previous line ends with "&"
                if (!isContinuation && result.Count > 0 && string.IsNullOrEmpty(opType))
                {
                    var prevPart = (result[^1].PartName ?? "").Trim();
                    if (prevPart.EndsWith("&") || prevPart.EndsWith("and", StringComparison.OrdinalIgnoreCase))
                        isContinuation = true;
                }

                if (isContinuation)
                {
                    // Merge into previous line
                    var prev = result[^1];
                    var separator = (prev.PartName ?? "").EndsWith("&") || partName.StartsWith("&") ? " " : " ";
                    prev.PartName = ((prev.PartName ?? "") + separator + partName).Trim();
                    if (!string.IsNullOrEmpty(line.Description))
                        prev.Description = ((prev.Description ?? "") + " " + line.Description).Trim();
                    // Accumulate hours/price from continuation
                    if (line.LaborHours > 0 && prev.LaborHours == 0) prev.LaborHours = line.LaborHours;
                    if (line.RefinishHours > 0 && prev.RefinishHours == 0) prev.RefinishHours = line.RefinishHours;
                    if (line.Price > 0 && prev.Price == 0) prev.Price = line.Price;
                }
                else
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private static bool IsRefinishContinuation(string partName)
        {
            var lower = partName.ToLowerInvariant();
            return lower.StartsWith("rub-out") || lower.StartsWith("rub out") || lower.StartsWith("rubout") ||
                   lower.StartsWith("block sand") || lower.StartsWith("buff") ||
                   lower.StartsWith("wet sand") || lower.StartsWith("wet/dry") ||
                   lower.StartsWith("sand, rub") || lower.StartsWith("de-nib") || lower.StartsWith("denib");
        }

        #endregion

        #region Estimate Line Categorization

        private List<CategorizedEstimateLine> CategorizeEstimateLines(List<ParsedEstimateLine> lines)
        {
            var categorized = new List<CategorizedEstimateLine>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.PartName) && string.IsNullOrWhiteSpace(line.Description))
                    continue;

                var partLower = (line.PartName ?? "").ToLowerInvariant();
                var descLower = (line.Description ?? "").ToLowerInvariant();
                var opLower = (line.OperationType ?? "").ToLowerInvariant();
                var combined = $"{partLower} {descLower}";

                string category = ClassifyLineCategory(combined, opLower, line);

                categorized.Add(new CategorizedEstimateLine
                {
                    PartName = line.PartName ?? "",
                    OperationType = line.OperationType ?? "",
                    Description = line.Description ?? "",
                    LaborHours = line.LaborHours,
                    RefinishHours = line.RefinishHours,
                    Price = line.Price,
                    Category = category
                });
            }

            return categorized;
        }

        private string ClassifyLineCategory(string combined, string opLower, ParsedEstimateLine line)
        {
            // Diagnostics
            if (combined.Contains("scan") || combined.Contains("diagnostic"))
                return "Diagnostics";

            // Calibrations
            if (combined.Contains("calibrat") || combined.Contains("adas") || combined.Contains("alignment"))
                return "Calibrations";

            // Blend
            if (opLower.Contains("blend") || combined.Contains("blend"))
                return "Blend";

            // R&I
            if (opLower.Contains("r&i") || opLower.Contains("r & i") || combined.Contains("remove and install") ||
                combined.Contains("r&i") || combined.Contains("remove & install"))
                return "R&I";

            // Materials
            if (combined.Contains("flex additive") || combined.Contains("adhesion promoter") ||
                combined.Contains("corrosion") || combined.Contains("seam seal") ||
                combined.Contains("weld-thru") || combined.Contains("weld thru") ||
                combined.Contains("cavity wax") || combined.Contains("primer") ||
                combined.Contains("material") || combined.Contains("paint supplies") ||
                combined.Contains("masking") || combined.Contains("sandpaper") ||
                combined.Contains("tack cloth"))
                return "Materials";

            // Refinish
            if (line.RefinishHours > 0 || opLower.Contains("rfn") || opLower.Contains("refinish") ||
                combined.Contains("denib") || combined.Contains("de-nib") ||
                combined.Contains("buff") || combined.Contains("rub-out") ||
                combined.Contains("feather") || combined.Contains("block sand") ||
                combined.Contains("clear coat") || combined.Contains("tri-coat") ||
                combined.Contains("3-stage") || combined.Contains("color sand"))
                return "Refinish";

            // Electrical
            if (combined.Contains("battery") || combined.Contains("wiring") ||
                combined.Contains("electrical") || combined.Contains("module") ||
                combined.Contains("sensor") && !combined.Contains("calibrat"))
                return "Electrical";

            // Mechanical / Sublet
            if (combined.Contains("alignment") || combined.Contains("suspension") ||
                combined.Contains("strut") || combined.Contains("control arm") ||
                combined.Contains("sublet"))
                return "Mechanical";

            // Body/Structural (default for repair/replace operations)
            if (opLower.Contains("rpr") || opLower.Contains("repair") ||
                opLower.Contains("rpl") || opLower.Contains("replace") ||
                opLower.Contains("new") || opLower.Contains("overhaul") ||
                line.LaborHours > 0)
                return "Body/Structural";

            return "Other";
        }

        #endregion

        #region Helper Methods

        private CommonlyMissedData? LoadCommonlyMissedItems()
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = Path.Combine(appDir, "Data", "CommonlyMissedItems.json");

                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<CommonlyMissedData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scoring] Error loading commonly missed items: {ex.Message}");
            }
            return null;
        }

        private IncludedNotIncludedData? LoadIncludedNotIncludedData()
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = Path.Combine(appDir, "Data", "IncludedNotIncluded.json");

                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<IncludedNotIncludedData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scoring] Error loading P-Page data: {ex.Message}");
            }
            return null;
        }

        private Dictionary<string, List<BlendRule>> InitializeBlendRules()
        {
            // Hours set to 0 — actual blend time comes from the estimating system,
            // not from us. We only flag that a blend is needed.
            return new Dictionary<string, List<BlendRule>>
            {
                ["hood"] = new() { new("Fender", 0m, true), new("Fender", 0m, true) },
                ["fender"] = new() { new("Hood", 0m, true), new("Door", 0m, true) },
                ["door"] = new() { new("Fender", 0m, false), new("Quarter Panel", 0m, false) },
                ["quarter"] = new() { new("Door", 0m, true), new("Bumper", 0m, false) },
                ["bumper"] = new() { new("Fender", 0m, false) },
                ["roof"] = new() { new("Quarter Panel", 0m, false) },
                ["deck"] = new() { new("Quarter Panel", 0m, true) },
                ["trunk"] = new() { new("Quarter Panel", 0m, true) }
            };
        }

        private Dictionary<string, List<string>> InitializeOperationChains()
        {
            return new Dictionary<string, List<string>>
            {
                ["bumper_replace"] = new() { "Flex Additive", "Adhesion Promoter", "Transfer Parts" },
                ["quarter_replace"] = new() { "Corrosion Protection", "Weld-Thru Primer", "Seam Sealer", "Adjacent Blend" },
                ["door_replace"] = new() { "Transfer Hardware", "Seam Sealer", "Adjacent Blend" },
                ["hood_replace"] = new() { "Transfer Insulator", "Blend Fenders" },
                ["windshield_replace"] = new() { "ADAS Calibration", "Transfer Sensors" }
            };
        }

        private IssueCategoryType GetIssueCategory(string category)
        {
            return category.ToLowerInvariant() switch
            {
                "materials" => IssueCategoryType.Materials,
                "refinish" => IssueCategoryType.Refinish,
                "labor" => IssueCategoryType.Labor,
                "r&i" or "r & i" or "randi" => IssueCategoryType.RandI,
                "electrical" => IssueCategoryType.Electrical,
                "diagnostic" => IssueCategoryType.Diagnostic,
                "calibration" => IssueCategoryType.Calibration,
                "mechanical" => IssueCategoryType.Mechanical,
                _ => IssueCategoryType.Other
            };
        }

        private IssueSeverity GetSeverity(string priority)
        {
            return priority.ToLowerInvariant() switch
            {
                "critical" => IssueSeverity.Critical,
                "high" => IssueSeverity.High,
                "medium" => IssueSeverity.Medium,
                "low" => IssueSeverity.Low,
                _ => IssueSeverity.Medium
            };
        }

        private string GetOperationType(string category)
        {
            return category.ToLowerInvariant() switch
            {
                "materials" => "Add",
                "refinish" => "Refinish",
                "labor" => "Add",
                "r&i" => "R&I",
                "electrical" => "Add",
                "diagnostic" => "Add",
                "calibration" => "Sublet",
                "mechanical" => "Add",
                _ => "Add"
            };
        }

        private int GetPointDeduction(string priority)
        {
            var weights = GhostConfigService.Instance.Config.ScoringWeights;
            return priority.ToLowerInvariant() switch
            {
                "critical" => weights.CriticalPoints,
                "high" => weights.HighPoints,
                "medium" => weights.MediumPoints,
                "low" => weights.LowPoints,
                _ => 2
            };
        }

        /// <summary>
        /// Map a commonly-missed item's category string to a labor category for rate-based recovery.
        /// </summary>
        private static string MapLaborCategory(string? category)
        {
            if (string.IsNullOrEmpty(category)) return "Body";
            var c = category.ToLowerInvariant();
            if (c.Contains("refinish") || c.Contains("paint") || c.Contains("rfn") || c.Contains("blend"))
                return "Paint";
            if (c.Contains("mech") || c.Contains("electrical") || c.Contains("diagnostic") || c.Contains("scan"))
                return "Mech";
            if (c.Contains("calibrat") || c.Contains("sublet"))
                return "Parts";
            return "Body";
        }

        /// <summary>
        /// Map a must-have OpType (Body, Rfn, Mech, Sublet) to a labor category.
        /// </summary>
        private static string MapOpTypeToLaborCategory(string? opType)
        {
            return (opType?.ToLowerInvariant()) switch
            {
                "rfn" or "refinish" or "paint" => "Paint",
                "mech" or "mechanical" => "Mech",
                "sublet" => "Parts",
                _ => "Body"
            };
        }

        /// <summary>
        /// Map an IssueCategoryType enum to a labor category string.
        /// </summary>
        private static string MapIssueCategoryToLaborCategory(IssueCategoryType cat)
        {
            return cat switch
            {
                IssueCategoryType.Refinish or IssueCategoryType.Blend => "Paint",
                IssueCategoryType.Mechanical or IssueCategoryType.Electrical or IssueCategoryType.Diagnostic => "Mech",
                IssueCategoryType.Calibration => "Parts",
                IssueCategoryType.Materials => "Parts",
                _ => "Body"
            };
        }

        private IssueCategoryType MapNotIncludedToCategory(string operationText)
        {
            var lower = operationText.ToLowerInvariant();
            if (lower.Contains("corrosion") || lower.Contains("primer") || lower.Contains("sealer") ||
                lower.Contains("e-coat") || lower.Contains("cavity wax") || lower.Contains("adhesion") ||
                lower.Contains("flex additive"))
                return IssueCategoryType.Materials;
            if (lower.Contains("r&i") || lower.Contains("remove") || lower.Contains("transfer"))
                return IssueCategoryType.RandI;
            if (lower.Contains("scan") || lower.Contains("diagnostic"))
                return IssueCategoryType.Diagnostic;
            if (lower.Contains("adas") || lower.Contains("calibrat"))
                return IssueCategoryType.Calibration;
            if (lower.Contains("blend") || lower.Contains("refinish") || lower.Contains("feather") ||
                lower.Contains("prime") || lower.Contains("denib") || lower.Contains("buff"))
                return IssueCategoryType.Refinish;
            return IssueCategoryType.Other;
        }

        private string BuildSourceCitation(SourceReferences? sourceRefs)
        {
            if (sourceRefs == null) return "";
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(sourceRefs.CccMotor))
                parts.Add($"CCC/MOTOR {sourceRefs.CccMotor}");
            if (!string.IsNullOrEmpty(sourceRefs.Mitchell))
                parts.Add($"Mitchell {sourceRefs.Mitchell}");
            if (sourceRefs.DegInquiries?.Count > 0)
                parts.Add("DEG #" + string.Join(", #", sourceRefs.DegInquiries));
            return string.Join(" | ", parts);
        }

        #endregion
    }

    #region Data Models

    public class EstimateScoringResult
    {
        public int OverallScore { get; set; }
        public string Grade { get; set; } = "";
        public string Summary { get; set; } = "";
        public string? VehicleInfo { get; set; }
        public int TotalLineItems { get; set; }
        public decimal EstimateTotal { get; set; }
        public DateTime AssessedAt { get; set; }

        public List<ScoringIssue> Issues { get; set; } = new();
        public Dictionary<string, int> CategoryScores { get; set; } = new();
        public List<CategorizedEstimateLine> CategorizedLines { get; set; } = new();

        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }

        public decimal PotentialLaborRecovery { get; set; }
        public decimal PotentialCostRecovery { get; set; }

        // Per-category recovery breakdown
        public decimal BodyLaborRecovery { get; set; }
        public decimal PaintLaborRecovery { get; set; }
        public decimal MechLaborRecovery { get; set; }
        public decimal PartsRecovery { get; set; }
        public decimal BodyRate { get; set; }
        public decimal PaintRate { get; set; }
        public decimal MechRate { get; set; }

        // Rate overrides detected from the estimate itself (0 = use config default)
        internal decimal EstimateBodyRate { get; set; }
        internal decimal EstimatePaintRate { get; set; }
        internal decimal EstimateMechRate { get; set; }

        public BenchmarkResult? Benchmark { get; set; }
    }

    public class CategorizedEstimateLine
    {
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public string Category { get; set; } = "";
    }

    public class ScoringIssue
    {
        public IssueCategoryType Category { get; set; }
        public IssueSeverity Severity { get; set; }
        public IssueActionType ActionType { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string WhyNeeded { get; set; } = "";
        public string TriggeredBy { get; set; } = "";
        public SuggestedFix? SuggestedFix { get; set; }
        public int PointDeduction { get; set; }
        public bool IsSelected { get; set; } = false;
        public string? Source { get; set; }        // "Scoring" or "Smart"
        public string? SourceDetail { get; set; }  // e.g. "Front Bumper" (the triggering part)
    }

    public class SuggestedFix
    {
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal EstimatedCost { get; set; }
        public string? DegReference { get; set; }
        /// <summary>
        /// Labor category for per-rate recovery: Body, Paint/Rfn, Mech, Parts
        /// </summary>
        public string? LaborCategory { get; set; }
    }

    public enum IssueCategoryType
    {
        Materials,
        Blend,
        RandI,
        Diagnostic,
        Calibration,
        Refinish,
        Labor,
        Electrical,
        Mechanical,
        Other
    }

    public enum IssueSeverity
    {
        Critical,
        High,
        Medium,
        Low
    }

    public enum IssueActionType
    {
        AddToEstimate,
        CheckWithOEM,
        VerifyOptional
    }

    internal class BlendRule
    {
        public string AdjacentPanel { get; }
        public decimal TypicalHours { get; }
        public bool IsRequired { get; }

        public BlendRule(string panel, decimal hours, bool required)
        {
            AdjacentPanel = panel;
            TypicalHours = hours;
            IsRequired = required;
        }
    }

    internal class CommonlyMissedData
    {
        public string? Version { get; set; }
        public Dictionary<string, OperationCheckData>? OperationChecks { get; set; }
        public Dictionary<string, GlobalCheckData>? GlobalChecks { get; set; }
    }

    internal class OperationCheckData
    {
        public string? Operation { get; set; }
        public List<string>? TriggerKeywords { get; set; }
        public List<string>? TriggerOperations { get; set; }
        public List<MissedItemData>? MissedItems { get; set; }
    }

    internal class MissedItemData
    {
        public string Item { get; set; } = "";
        public string? Category { get; set; }
        public string? Description { get; set; }
        public string? WhyNeeded { get; set; }
        public string? DegReference { get; set; }
        public decimal TypicalCost { get; set; }
        public decimal LaborHours { get; set; }
        public string? Priority { get; set; }
    }

    internal class GlobalCheckData
    {
        public decimal? Threshold { get; set; }
        public string? Condition { get; set; }
        public string? Description { get; set; }
        public List<MissedItemData>? Checks { get; set; }
    }

    public class BenchmarkResult
    {
        public bool HasEnoughData { get; set; }
        public int SimilarEstimateCount { get; set; }
        public decimal CurrentTotal { get; set; }
        public decimal AverageTotal { get; set; }
        public decimal MedianTotal { get; set; }
        public decimal DollarDifference { get; set; }   // negative = below avg
        public double PercentDifference { get; set; }    // negative = below avg
        public decimal HighestSimilarTotal { get; set; }
        public decimal LowestSimilarTotal { get; set; }
        public List<string> CommonDamageZones { get; set; } = new();
    }

    #endregion
}
