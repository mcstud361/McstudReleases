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
        public EstimateScoringResult ScoreEstimate(List<ParsedEstimateLine> lines, string? vehicleInfo = null)
        {
            // Pre-process: join continuation lines (e.g., "Rub-Out &" + "& Buff" → "Rub-Out & Buff")
            // OCR and PDF parsers sometimes split wrapped descriptions across multiple lines.
            lines = JoinContinuationLines(lines);

            // Filter out vehicle description lines that leaked through PDF parsing
            // (e.g., "2023 Tesla Model 3") — these trigger false positives on EV/make checks.
            lines = lines.Where(l =>
                !IsVehicleInfoLine(l.PartName ?? "") || !string.IsNullOrEmpty(l.OperationType))
                .ToList();

            var result = new EstimateScoringResult
            {
                VehicleInfo = vehicleInfo,
                TotalLineItems = lines.Count,
                AssessedAt = DateTime.Now
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
            RunCheck(() => CheckMustHaves(lines, result), "MustHaves");

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

            foreach (var line in lines.Where(l => !l.IsManualLine && !string.IsNullOrEmpty(l.PartName)))
            {
                var partLower = line.PartName.ToLowerInvariant();
                var opLower = line.OperationType?.ToLowerInvariant() ?? "";

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
                            // Check if this item is already in the estimate (word-boundary match)
                            bool isPresent = lines.Any(l =>
                                IsWordMatch(l.PartName ?? "", missedItem.Item) ||
                                IsWordMatch(l.Description ?? "", missedItem.Item));

                            if (!isPresent)
                            {
                                var issue = new ScoringIssue
                                {
                                    Category = GetIssueCategory(missedItem.Category ?? "Other"),
                                    Severity = GetSeverity(missedItem.Priority ?? "medium"),
                                    Title = missedItem.Item,
                                    Description = missedItem.Description ?? "",
                                    WhyNeeded = missedItem.WhyNeeded ?? "",
                                    TriggeredBy = line.PartName,
                                    SuggestedFix = new SuggestedFix
                                    {
                                        OperationType = GetOperationType(missedItem.Category ?? "Labor"),
                                        Description = missedItem.Item,
                                        LaborHours = missedItem.LaborHours,
                                        EstimatedCost = missedItem.TypicalCost,
                                        DegReference = missedItem.DegReference
                                    },
                                    PointDeduction = GetPointDeduction(missedItem.Priority ?? "medium")
                                };

                                // Avoid duplicates (title-only dedup - prevents synonym panels
                                // like "Quarter Panel" and "L Quarter" from both triggering the same issue)
                                if (!result.Issues.Any(i => i.Title == issue.Title))
                                {
                                    result.Issues.Add(issue);
                                }
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
            var refinishPanels = lines.Where(l =>
                !l.IsManualLine &&
                (l.OperationType?.ToLowerInvariant().Contains("repl") == true ||
                 l.OperationType?.ToLowerInvariant().Contains("rfn") == true ||
                 l.OperationType?.ToLowerInvariant().Contains("refinish") == true ||
                 l.RefinishHours > 0)).ToList();

            foreach (var panel in refinishPanels)
            {
                var partLower = panel.PartName?.ToLowerInvariant() ?? "";

                foreach (var rule in _blendRules)
                {
                    if (partLower.Contains(rule.Key))
                    {
                        foreach (var blendRule in rule.Value)
                        {
                            // Check if blend panel is already in estimate
                            bool hasBlend = lines.Any(l =>
                                l.PartName?.ToLowerInvariant().Contains(blendRule.AdjacentPanel.ToLowerInvariant()) == true &&
                                (l.OperationType?.ToLowerInvariant().Contains("blend") == true ||
                                 l.Description?.ToLowerInvariant().Contains("blend") == true));

                            if (!hasBlend)
                            {
                                var issue = new ScoringIssue
                                {
                                    Category = IssueCategoryType.Blend,
                                    Severity = blendRule.IsRequired ? IssueSeverity.High : IssueSeverity.Medium,
                                    Title = $"Blend {blendRule.AdjacentPanel}",
                                    Description = $"Adjacent panel blend for color match",
                                    WhyNeeded = $"New paint on {panel.PartName} won't match aged paint on {blendRule.AdjacentPanel}",
                                    TriggeredBy = panel.PartName ?? "",
                                    SuggestedFix = new SuggestedFix
                                    {
                                        OperationType = "Blend",
                                        Description = $"Blend {blendRule.AdjacentPanel}",
                                        LaborHours = blendRule.TypicalHours
                                    },
                                    PointDeduction = blendRule.IsRequired ? 5 : 3
                                };

                                if (!result.Issues.Any(i => i.Title == issue.Title))
                                {
                                    result.Issues.Add(issue);
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Material Checks

        private void CheckMaterialOperations(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            var existingTitles = new HashSet<string>(
                result.Issues.Select(i => i.Title.ToLowerInvariant()));

            foreach (var line in lines.Where(l => !l.IsManualLine && !string.IsNullOrEmpty(l.PartName)))
            {
                var opType = line.OperationType ?? "";

                // Skip lines that don't involve paint or structural work
                bool involvesPaint = OperationRulesEngine.Instance.InvolvesPaint(opType) || line.RefinishHours > 0;
                bool isReplace = opType.ToLowerInvariant().Contains("repl") ||
                                 opType.ToLowerInvariant().Contains("r&r") ||
                                 opType.ToLowerInvariant().Contains("section");
                if (!involvesPaint && !isReplace) continue;

                var suggestions = OperationRulesEngine.Instance.GetSuggestedOperations(line.PartName!, opType);

                foreach (var suggestion in suggestions)
                {
                    // Skip if already flagged
                    if (existingTitles.Contains(suggestion.Name.ToLowerInvariant())) continue;

                    // Check if the operation already exists in the estimate
                    var nameLower = suggestion.Name.ToLowerInvariant();
                    bool isPresent = lines.Any(l =>
                        l.PartName?.ToLowerInvariant().Contains(nameLower) == true ||
                        l.Description?.ToLowerInvariant().Contains(nameLower) == true);

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
                        TriggeredBy = line.PartName ?? "",
                        SuggestedFix = new SuggestedFix
                        {
                            Description = suggestion.Description,
                            LaborHours = suggestion.DefaultHours,
                            OperationType = suggestion.OperationType,
                            DegReference = degRef
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

            foreach (var line in lines.Where(l => !l.IsManualLine && !string.IsNullOrEmpty(l.PartName)))
            {
                var partLower = line.PartName.ToLowerInvariant();
                var opLower = line.OperationType?.ToLowerInvariant() ?? "";

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

                        // Search all estimate lines for this operation (word-boundary match)
                        bool isPresent = lines.Any(l =>
                            IsWordMatch(l.PartName ?? "", operationName) ||
                            IsWordMatch(l.Description ?? "", operationName));

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
                            TriggeredBy = line.PartName ?? "",
                            SourceDetail = line.PartName ?? "",
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
            // Check bumper work for R&I items — split front vs rear
            var bumperWork = lines.Where(l =>
                !l.IsManualLine &&
                (l.PartName?.ToLowerInvariant().Contains("bumper") == true ||
                 l.PartName?.ToLowerInvariant().Contains("fascia") == true)).ToList();

            if (bumperWork.Count > 0)
            {
                var frontBumperWork = bumperWork.Where(l =>
                {
                    var name = l.PartName?.ToLowerInvariant() ?? "";
                    return name.Contains("front") || name.Contains("frt") ||
                           (!name.Contains("rear") && !name.Contains("rr ") && !name.Contains("r bumper") && !name.Contains("r fascia"));
                }).ToList();

                var rearBumperWork = bumperWork.Where(l =>
                {
                    var name = l.PartName?.ToLowerInvariant() ?? "";
                    return name.Contains("rear") || name.Contains("rr ") || name.Contains("r bumper") || name.Contains("r fascia");
                }).ToList();

                if (frontBumperWork.Count > 0)
                {
                    var trigger = frontBumperWork.First().PartName ?? "Front Bumper";
                    CheckForMissingRI(lines, result, trigger, new[] {
                        ("R&I Fog Lamps", 0.3m, new[] { "fog" }),
                        ("R&I Parking Sensors", 0.2m, new[] { "sensor", "park" }),
                        ("R&I Front Camera", 0.3m, new[] { "camera", "front camera" })
                    });
                }

                if (rearBumperWork.Count > 0)
                {
                    var trigger = rearBumperWork.First().PartName ?? "Rear Bumper";
                    CheckForMissingRI(lines, result, trigger, new[] {
                        ("R&I Parking Sensors", 0.2m, new[] { "sensor", "park" }),
                        ("R&I Rear Camera", 0.3m, new[] { "camera", "rear camera", "backup camera" })
                    });
                }
            }

            // Check door work for R&I items
            var doorWork = lines.Where(l =>
                !l.IsManualLine &&
                l.PartName?.ToLowerInvariant().Contains("door") == true &&
                !l.PartName.ToLowerInvariant().Contains("fuel")).ToList();

            if (doorWork.Count > 0)
            {
                var trigger = doorWork.First().PartName ?? "Door";
                CheckForMissingRI(lines, result, trigger, new[] {
                    ("R&I Mirror", 0.3m, new[] { "mirror" }),
                    ("R&I Door Handle", 0.3m, new[] { "handle" }),
                    ("R&I Door Trim Panel", 0.3m, new[] { "trim", "molding" })
                });
            }

            // Check quarter panel for R&I items
            var quarterWork = lines.Where(l =>
                !l.IsManualLine &&
                (l.PartName?.ToLowerInvariant().Contains("quarter") == true ||
                 l.PartName?.ToLowerInvariant().Contains("qtr") == true)).ToList();

            if (quarterWork.Count > 0)
            {
                var trigger = quarterWork.First().PartName ?? "Quarter Panel";
                CheckForMissingRI(lines, result, trigger, new[] {
                    ("R&I Tail Light", 0.3m, new[] { "tail" }),
                    ("R&I Fuel Door", 0.2m, new[] { "fuel door", "fuel" }),
                    ("R&I Quarter Moldings", 0.2m, new[] { "molding", "moulding" })
                });
            }

            // Check hood for R&I items
            var hoodWork = lines.Where(l =>
                !l.IsManualLine &&
                l.PartName?.ToLowerInvariant().Contains("hood") == true).ToList();

            if (hoodWork.Count > 0)
            {
                CheckForMissingRI(lines, result, "Hood", new[] {
                    ("R&I Hood Insulator", 0.3m, new[] { "insulator", "insulation" }),
                    ("R&I Hood Struts", 0.2m, new[] { "strut" })
                });
            }

            // Check fender for R&I items
            var fenderWork = lines.Where(l =>
                !l.IsManualLine &&
                l.PartName?.ToLowerInvariant().Contains("fender") == true).ToList();

            if (fenderWork.Count > 0)
            {
                var trigger = fenderWork.First().PartName ?? "Fender";
                CheckForMissingRI(lines, result, trigger, new[] {
                    ("R&I Fender Liner", 0.3m, new[] { "liner", "splash" }),
                    ("R&I Tire/Wheel", 0.2m, new[] { "wheel", "tire" })
                });
            }
        }

        private void CheckForMissingRI(List<ParsedEstimateLine> lines, EstimateScoringResult result,
            string triggeredBy, (string Name, decimal Hours, string[] Keywords)[] riItems)
        {
            foreach (var item in riItems)
            {
                // Only suggest R&I if the component is actually referenced in the estimate
                bool componentMentioned = lines.Any(l =>
                    item.Keywords.Any(k =>
                        l.Description?.ToLowerInvariant().Contains(k) == true ||
                        l.PartName?.ToLowerInvariant().Contains(k) == true));

                if (!componentMentioned) continue;

                // Check if the R&I itself is already on the estimate
                var itemLower = item.Name.ToLowerInvariant();
                bool hasRI = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains(itemLower) == true ||
                    l.PartName?.ToLowerInvariant().Contains(itemLower) == true);

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
                                LaborHours = item.Hours
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
            // Any body panel work should have pre/post scans per OEM position statements
            bool needsScans = result.EstimateTotal > 2500 ||
                lines.Any(l =>
                    l.PartName?.ToLowerInvariant().Contains("bumper") == true ||
                    l.PartName?.ToLowerInvariant().Contains("fender") == true ||
                    l.PartName?.ToLowerInvariant().Contains("hood") == true ||
                    l.PartName?.ToLowerInvariant().Contains("door") == true ||
                    l.PartName?.ToLowerInvariant().Contains("quarter") == true ||
                    l.PartName?.ToLowerInvariant().Contains("roof") == true ||
                    l.PartName?.ToLowerInvariant().Contains("windshield") == true ||
                    l.PartName?.ToLowerInvariant().Contains("frame") == true ||
                    l.PartName?.ToLowerInvariant().Contains("rail") == true ||
                    l.PartName?.ToLowerInvariant().Contains("strut tower") == true ||
                    l.PartName?.ToLowerInvariant().Contains("structural") == true ||
                    l.PartName?.ToLowerInvariant().Contains("airbag") == true ||
                    l.PartName?.ToLowerInvariant().Contains("srs") == true);

            if (needsScans)
            {
                // Check both Description AND PartName — CCC estimates often put scan text in PartName
                bool hasPreScan = lines.Any(l =>
                {
                    var desc = l.Description?.ToLowerInvariant() ?? "";
                    var part = l.PartName?.ToLowerInvariant() ?? "";
                    var combined = $"{desc} {part}";
                    return (combined.Contains("pre") || combined.Contains("before")) &&
                           combined.Contains("scan");
                });

                bool hasPostScan = lines.Any(l =>
                {
                    var desc = l.Description?.ToLowerInvariant() ?? "";
                    var part = l.PartName?.ToLowerInvariant() ?? "";
                    var combined = $"{desc} {part}";
                    return (combined.Contains("post") || combined.Contains("after")) &&
                           combined.Contains("scan");
                });

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
                            LaborHours = 0.5m
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
                            LaborHours = 0.5m
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
            // Check windshield work
            bool hasWindshield = lines.Any(l =>
                l.PartName?.ToLowerInvariant().Contains("windshield") == true ||
                l.PartName?.ToLowerInvariant().Contains("w/s") == true);

            if (hasWindshield)
            {
                bool hasCalibration = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("calibrat") == true ||
                    l.PartName?.ToLowerInvariant().Contains("calibrat") == true ||
                    l.Description?.ToLowerInvariant().Contains("adas") == true);

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
                            EstimatedCost = 350
                        },
                        PointDeduction = 10
                    });
                }
            }

            // Check for alignment with ADAS implications
            bool hasAlignment = lines.Any(l =>
                l.Description?.ToLowerInvariant().Contains("align") == true);

            bool hasSuspensionWork = lines.Any(l =>
                l.PartName?.ToLowerInvariant().Contains("strut") == true ||
                l.PartName?.ToLowerInvariant().Contains("control arm") == true ||
                l.PartName?.ToLowerInvariant().Contains("tie rod") == true ||
                l.PartName?.ToLowerInvariant().Contains("suspension") == true);

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
                        LaborHours = 1.0m
                    },
                    PointDeduction = 5
                });
            }

            // Check radar/sensor work
            bool hasRadarSensor = lines.Any(l =>
                l.PartName?.ToLowerInvariant().Contains("radar") == true ||
                l.PartName?.ToLowerInvariant().Contains("sensor") == true ||
                l.PartName?.ToLowerInvariant().Contains("camera") == true);

            if (hasRadarSensor)
            {
                bool hasSensorCalibration = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("calibrat") == true);

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
                            EstimatedCost = 250
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
                bool hasClearCoat = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("clear") == true ||
                    l.PartName?.ToLowerInvariant().Contains("clear") == true);

                // Clear coat is usually included, but check for 2-stage/3-stage
                bool hasTriCoat = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("tri-coat") == true ||
                    l.Description?.ToLowerInvariant().Contains("3-stage") == true ||
                    l.Description?.ToLowerInvariant().Contains("three stage") == true ||
                    l.Description?.ToLowerInvariant().Contains("pearl") == true);

                if (hasTriCoat)
                {
                    bool hasTriCoatTime = lines.Any(l =>
                        l.Description?.ToLowerInvariant().Contains("tri-coat") == true ||
                        l.Description?.ToLowerInvariant().Contains("additional") == true);

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
                                LaborHours = 0.5m
                            },
                            PointDeduction = 4
                        });
                    }
                }
            }

            // Check for battery disconnect on structural work
            bool hasStructuralWelding = lines.Any(l =>
                (l.PartName?.ToLowerInvariant().Contains("quarter") == true ||
                 l.PartName?.ToLowerInvariant().Contains("rocker") == true ||
                 l.PartName?.ToLowerInvariant().Contains("rail") == true ||
                 l.PartName?.ToLowerInvariant().Contains("pillar") == true ||
                 l.PartName?.ToLowerInvariant().Contains("roof") == true) &&
                l.OperationType?.ToLowerInvariant().Contains("repl") == true);

            if (hasStructuralWelding)
            {
                bool hasBatteryDisconnect = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("battery") == true &&
                    l.Description?.ToLowerInvariant().Contains("disconnect") == true);

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
                            LaborHours = 0.6m
                        },
                        PointDeduction = 4
                    });
                }
            }
        }

        #endregion

        #region Must-Haves Check

        private void CheckMustHaves(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            var mustHaves = GhostConfigService.Instance.GetMustHaves();
            if (mustHaves == null || mustHaves.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[MustHaves] No must-haves configured — skipping");
                return;
            }
            System.Diagnostics.Debug.WriteLine($"[MustHaves] Checking {mustHaves.Count} must-haves against {lines.Count} parsed lines");

            // Build combined text blob for condition evaluation
            var combinedTextLower = string.Join(" ",
                lines.Select(l => $"{l.Description} {l.PartName} {l.OperationType}")).ToLowerInvariant();

            foreach (var mh in mustHaves)
            {
                if (!mh.Enabled || string.IsNullOrWhiteSpace(mh.Description)) continue;

                // Skip must-haves whose condition is not met by the estimate context
                if (!EstimateConditionEvaluator.Evaluate(mh.Conditions, combinedTextLower))
                {
                    System.Diagnostics.Debug.WriteLine($"[MustHaves] '{mh.Description}': condition '{mh.Conditions}' not met — skipping");
                    continue;
                }

                // Skip junk entries (e.g., "Description", "Setup" from header rows that leaked into config)
                var descLower = mh.Description.ToLowerInvariant().Trim();
                if (descLower is "description" or "category" or "operation" or "setup" or "")
                    continue;

                // Count how many lines match this must-have using fuzzy word-based matching.
                // This handles truncated names ("Disconnect and" matches "Disconnect and Reconnect Battery"),
                // hyphen/space differences ("Pre-Scan" matches "Pre-repair scan"),
                // and parenthetical suffixes ("Color Tint (2-Stage)" matches "Color Tint").
                int matchCount = lines.Count(l => MustHaveFuzzyMatch(descLower, l));

                System.Diagnostics.Debug.WriteLine($"[MustHaves] '{mh.Description}': {matchCount}/{mh.MinCount} matches → {(matchCount >= mh.MinCount ? "PRESENT" : "MISSING")}");

                if (matchCount < mh.MinCount)
                {
                    var missing = mh.MinCount - matchCount;
                    var title = matchCount == 0
                        ? $"Missing: {mh.Description}"
                        : $"Need {missing} more: {mh.Description}";

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
                            Description = mh.Description
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
                        DegReference = s.DegReference
                    }
                });
            }
            return issues;
        }

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
                        EstimatedCost = op.Price
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
                            EstimatedCost = manual.AvgPrice
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

            // Calculate total deductions (cap at 100)
            int totalDeductions = Math.Min(100, result.Issues.Sum(i => i.PointDeduction));

            result.OverallScore = Math.Max(0, baseScore - totalDeductions);

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

            // Set grade
            result.Grade = result.OverallScore switch
            {
                >= 95 => "A+",
                >= 90 => "A",
                >= 85 => "B+",
                >= 80 => "B",
                >= 75 => "C+",
                >= 70 => "C",
                >= 60 => "D",
                _ => "F"
            };

            // Count issues by severity
            result.CriticalCount = result.Issues.Count(i => i.Severity == IssueSeverity.Critical);
            result.HighCount = result.Issues.Count(i => i.Severity == IssueSeverity.High);
            result.MediumCount = result.Issues.Count(i => i.Severity == IssueSeverity.Medium);
            result.LowCount = result.Issues.Count(i => i.Severity == IssueSeverity.Low);

            // Calculate potential recovery
            result.PotentialLaborRecovery = result.Issues.Sum(i => i.SuggestedFix?.LaborHours ?? 0);
            result.PotentialCostRecovery = result.Issues.Sum(i => i.SuggestedFix?.EstimatedCost ?? 0);

            // Classify action types
            foreach (var issue in result.Issues)
            {
                issue.ActionType = ClassifyActionType(issue);
            }

            // Generate summary
            if (result.OverallScore >= 90)
            {
                result.Summary = "Excellent! This estimate is very complete.";
            }
            else if (result.OverallScore >= 75)
            {
                result.Summary = $"Good estimate. Found {result.Issues.Count} potential improvements.";
            }
            else if (result.OverallScore >= 60)
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
            return new Dictionary<string, List<BlendRule>>
            {
                ["hood"] = new() { new("Fender", 0.5m, true), new("Fender", 0.5m, true) },
                ["fender"] = new() { new("Hood", 0.5m, true), new("Door", 0.5m, true) },
                ["door"] = new() { new("Fender", 0.5m, false), new("Quarter Panel", 0.5m, false) },
                ["quarter"] = new() { new("Door", 0.5m, true), new("Bumper", 0.3m, false) },
                ["bumper"] = new() { new("Fender", 0.3m, false) },
                ["roof"] = new() { new("Quarter Panel", 0.5m, false) },
                ["deck"] = new() { new("Quarter Panel", 0.5m, true) },
                ["trunk"] = new() { new("Quarter Panel", 0.5m, true) }
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

    #endregion
}
