#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using McstudDesktop.Models;
using McStudDesktop.Services;

namespace McstudDesktop.Services
{
    /// <summary>
    /// Connects screen monitoring to estimate scoring engines and produces
    /// real-time coaching suggestions displayed inline in the Screen Monitor panel.
    /// </summary>
    public class LiveCoachingService
    {
        private static LiveCoachingService? _instance;
        public static LiveCoachingService Instance => _instance ??= new LiveCoachingService();

        private readonly ScreenMonitorService _screenMonitor;
        private readonly McStudDesktop.Services.EstimateScoringService _scoringService;
        private readonly McStudDesktop.Services.SmartEstimateAnalyzerService _analyzerService;

        private CancellationTokenSource? _debounceCts;
        private string _lastContentHash = "";
        private readonly HashSet<string> _dismissedIds = new();
        private readonly object _lock = new();
        private bool _isRunning;

        // Accumulated view of the entire estimate across scrolling
        private readonly Dictionary<string, ParsedEstimateLine> _accumulatedOps = new(StringComparer.OrdinalIgnoreCase);
        private readonly StringBuilder _accumulatedRawText = new();
        private string _lastSourceWindow = "";
        private string? _vehicleInfo;
        private string? _customerName;

        private const int DebounceMs = 500;

        public event EventHandler<CoachingSnapshot>? SuggestionsUpdated;
        public event EventHandler<bool>? CoachingStateChanged;

        public bool IsRunning => _isRunning;

        private LiveCoachingService()
        {
            _screenMonitor = ScreenMonitorService.Instance;
            _scoringService = McStudDesktop.Services.EstimateScoringService.Instance;
            _analyzerService = McStudDesktop.Services.SmartEstimateAnalyzerService.Instance;
        }

        public void Start()
        {
            if (_isRunning) return;

            // Auto-start screen monitoring if not already running
            if (!_screenMonitor.IsMonitoring)
            {
                _screenMonitor.Start();
                Debug.WriteLine("[LiveCoaching] Auto-started screen monitoring");
            }

            _screenMonitor.OcrResultReady += OnOcrResultReady;
            _isRunning = true;
            CoachingStateChanged?.Invoke(this, true);
            Debug.WriteLine("[LiveCoaching] Started");
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _screenMonitor.OcrResultReady -= OnOcrResultReady;
            _debounceCts?.Cancel();
            _debounceCts = null;
            _isRunning = false;
            _lastContentHash = "";
            _accumulatedOps.Clear();
            _accumulatedRawText.Clear();
            _lastSourceWindow = "";
            _vehicleInfo = null;
            _customerName = null;
            CoachingStateChanged?.Invoke(this, false);
            Debug.WriteLine("[LiveCoaching] Stopped");
        }

        public void DismissSuggestion(string id)
        {
            lock (_lock) { _dismissedIds.Add(id); }
        }

        public void ResetDismissals()
        {
            lock (_lock) { _dismissedIds.Clear(); }
        }

        private void OnOcrResultReady(object? sender, ScreenOcrResult result)
        {
            // Accept results with either structured operations OR raw text
            var hasOps = result.DetectedOperations != null && result.DetectedOperations.Count > 0;
            var hasText = !string.IsNullOrWhiteSpace(result.RawText);

            if (!hasOps && !hasText) return;

            // Content-hash check — skip if content unchanged
            var hash = ComputeContentHash(result);
            if (hash == _lastContentHash) return;
            _lastContentHash = hash;

            // Cancel any pending debounce
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceMs, token);
                    if (token.IsCancellationRequested) return;

                    ProcessOcrResult(result);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LiveCoaching] Error processing OCR: {ex.Message}");
                }
            }, token);
        }

        private void ProcessOcrResult(ScreenOcrResult result)
        {
            // Only reset accumulated state if the core application changed
            var sourceWindow = result.SourceWindow ?? "";
            var currentApp = GetCoreAppName(sourceWindow);
            var lastApp = GetCoreAppName(_lastSourceWindow);
            if (!string.IsNullOrEmpty(currentApp) && !currentApp.Equals(lastApp, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(lastApp))
                {
                    Debug.WriteLine($"[LiveCoaching] App changed from \"{lastApp}\" to \"{currentApp}\" — resetting accumulated data");
                    _accumulatedOps.Clear();
                    _accumulatedRawText.Clear();
                    _vehicleInfo = null;
                    _customerName = null;
                }
            }
            _lastSourceWindow = sourceWindow;

            // Accumulate structured operations from this capture
            if (result.DetectedOperations != null)
            {
                foreach (var op in result.DetectedOperations)
                {
                    var key = $"{op.Description}|{op.PartName}|{op.OperationType}".ToLowerInvariant();
                    _accumulatedOps[key] = new ParsedEstimateLine
                    {
                        RawLine = op.RawLine,
                        Description = op.Description,
                        PartName = op.PartName,
                        OperationType = op.OperationType,
                        LaborHours = op.LaborHours,
                        RefinishHours = op.RefinishHours,
                        Price = op.Price,
                        Quantity = op.Quantity
                    };
                }
            }

            // Accumulate raw text for vehicle/customer info only — no part scanning
            if (!string.IsNullOrWhiteSpace(result.RawText))
            {
                _accumulatedRawText.AppendLine(result.RawText);
                if (_vehicleInfo == null)
                    _vehicleInfo = ExtractVehicleInfo(result.RawText);
                if (_customerName == null)
                    _customerName = ExtractCustomerName(result.RawText);
            }

            // Build parsed lines from ONLY structured operations
            var parsedLines = new List<ParsedEstimateLine>(_accumulatedOps.Values);

            Debug.WriteLine($"[LiveCoaching] Processing {parsedLines.Count} structured operations");

            // --- New suggestion pipeline ---
            // 1. SOP List baseline suggestions (always present)
            var sopSuggestions = GetSOPListSuggestions();

            // 2. Learned pattern suggestions (from past uploaded estimates)
            var learnedSuggestions = GetLearnedPatternSuggestions(parsedLines);

            // 3. Run scoring/analysis engines on structured ops only
            var scoringResult = parsedLines.Count > 0
                ? _scoringService.ScoreEstimate(parsedLines)
                : new McStudDesktop.Services.EstimateScoringResult { OverallScore = 0, Grade = "--" };
            var analysisResult = parsedLines.Count > 0
                ? _analyzerService.AnalyzeEstimate(parsedLines)
                : new McStudDesktop.Services.AnalysisResult();

            // Merge all suggestions: SOP List → Learned → Scoring → Analyzer
            var suggestions = new List<CoachingSuggestion>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. SOP List suggestions (Critical/High — money left on the table)
            foreach (var sop in sopSuggestions)
            {
                var key = NormalizeKey(sop.Category, sop.Title);
                if (!seenKeys.Add(key)) continue;
                sop.Id = key;
                suggestions.Add(sop);
            }

            // 2. Learned pattern suggestions (High/Medium — from real past estimates)
            foreach (var learned in learnedSuggestions)
            {
                var key = NormalizeKey(learned.Category, learned.Title);
                if (seenKeys.Contains(key)) continue;
                if (IsFuzzyDuplicate(key, seenKeys)) continue;
                seenKeys.Add(key);
                learned.Id = key;
                suggestions.Add(learned);
            }

            // 3. Scoring engine issues
            foreach (var issue in scoringResult.Issues)
            {
                var key = NormalizeKey(issue.Category.ToString(), issue.Title);
                if (!seenKeys.Add(key)) continue;

                suggestions.Add(new CoachingSuggestion
                {
                    Id = key,
                    Title = issue.Title,
                    Description = issue.Description,
                    WhyNeeded = issue.WhyNeeded,
                    DegReference = issue.SuggestedFix?.DegReference,
                    Category = issue.Category.ToString(),
                    Severity = MapSeverity(issue.Severity),
                    TriggeredBy = issue.TriggeredBy,
                    EstimatedCost = issue.SuggestedFix?.EstimatedCost ?? 0,
                    LaborHours = issue.SuggestedFix?.LaborHours ?? 0,
                    Source = "Scoring"
                });
            }

            // 4. Smart analyzer suggestions
            foreach (var suggestion in analysisResult.Suggestions)
            {
                var key = NormalizeKey(suggestion.Category.ToString(), suggestion.Item);
                if (seenKeys.Contains(key))
                {
                    var existing = suggestions.FirstOrDefault(s => s.Id == key);
                    if (existing != null) existing.Source = "Both";
                    continue;
                }

                if (IsFuzzyDuplicate(key, seenKeys)) continue;

                seenKeys.Add(key);
                suggestions.Add(new CoachingSuggestion
                {
                    Id = key,
                    Title = suggestion.Item,
                    Description = suggestion.Description ?? "",
                    WhyNeeded = suggestion.WhyNeeded ?? "",
                    DegReference = suggestion.DegReference,
                    Category = suggestion.Category.ToString(),
                    Severity = MapPriority(suggestion.Priority),
                    TriggeredBy = suggestion.SourcePart,
                    EstimatedCost = suggestion.TypicalCost,
                    LaborHours = suggestion.LaborHours,
                    Source = "Analyzer"
                });
            }

            // Cross-check suggestions against structured ops AND accumulated raw text.
            // Structured ops alone miss many SOP/misc items (pre-wash, clean for delivery, etc.)
            // that appear in OCR text but don't parse as structured operations.
            var structuredOpsText = string.Join(" | ", _accumulatedOps.Values
                .Select(op => $"{op.Description} {op.PartName} {op.OperationType} {op.RawLine}"))
                .ToLowerInvariant();
            var rawText = _accumulatedRawText.ToString().ToLowerInvariant();
            var combinedSeenText = structuredOpsText + " | " + rawText;

            foreach (var s in suggestions)
            {
                s.IsConfirmedOnEstimate = IsSuggestionOnEstimate(s, combinedSeenText);
            }

            // Apply dismissals
            lock (_lock)
            {
                foreach (var s in suggestions)
                {
                    if (_dismissedIds.Contains(s.Id))
                        s.IsDismissed = true;
                }
            }

            var confirmedCount = suggestions.Count(s => s.IsConfirmedOnEstimate);
            var snapshot = new CoachingSnapshot
            {
                Suggestions = suggestions,
                TotalOperationsDetected = parsedLines.Count,
                ConfirmedCount = confirmedCount,
                Score = scoringResult.OverallScore,
                Grade = scoringResult.Grade,
                PotentialRecovery = scoringResult.PotentialCostRecovery + scoringResult.PotentialLaborRecovery,
                VehicleInfo = _vehicleInfo,
                CustomerName = _customerName,
                Timestamp = DateTime.Now
            };

            Debug.WriteLine($"[LiveCoaching] Snapshot: score={snapshot.Score}, grade={snapshot.Grade}, suggestions={suggestions.Count} (SOP={sopSuggestions.Count}, learned={learnedSuggestions.Count})");
            SuggestionsUpdated?.Invoke(this, snapshot);
        }

        /// <summary>
        /// Returns SOP List baseline items that should always be suggested on every estimate.
        /// Reads from SOPBaselineService so users can enable/disable and add custom items.
        /// </summary>
        private static List<CoachingSuggestion> GetSOPListSuggestions()
        {
            var enabledItems = SOPBaselineService.Instance.GetEnabledItems();
            return enabledItems.Select(item => new CoachingSuggestion
            {
                Title = item.Name,
                Description = item.Description,
                WhyNeeded = item.WhyNeeded,
                Category = $"SOP - {item.Section}",
                Severity = item.Severity.ToLowerInvariant() switch
                {
                    "critical" => CoachingSeverity.Critical,
                    "high" => CoachingSeverity.High,
                    "low" => CoachingSeverity.Low,
                    _ => CoachingSeverity.Medium
                },
                Source = "SOP List",
                EstimatedCost = item.EstimatedCost,
                LaborHours = item.LaborHours
            }).ToList();
        }

        /// <summary>
        /// Queries learned patterns for operations that commonly co-occur with the
        /// structured operations detected on the current estimate.
        /// </summary>
        private static List<CoachingSuggestion> GetLearnedPatternSuggestions(List<ParsedEstimateLine> structuredOps)
        {
            var suggestions = new List<CoachingSuggestion>();
            if (structuredOps.Count == 0) return suggestions;

            var learningService = EstimateLearningService.Instance;
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var op in structuredOps)
            {
                if (string.IsNullOrWhiteSpace(op.PartName)) continue;

                // Query manual line patterns — these are the # manual lines estimators
                // add for each part+operation (backtape jambs, de-nib, flex additive, etc.)
                var manualPattern = learningService.GetManualLinesForPart(op.PartName, op.OperationType);
                if (manualPattern != null)
                {
                    foreach (var manualLine in manualPattern.ManualLines)
                    {
                        if (manualLine.TimesUsed < 1) continue;
                        var key = $"manual|{manualLine.Description}".ToLowerInvariant();
                        if (!seenKeys.Add(key)) continue;

                        var laborDisplay = manualLine.LaborUnits > 0 ? $"{manualLine.LaborUnits:G} labor" : "";
                        var refinishDisplay = manualLine.RefinishUnits > 0 ? $"{manualLine.RefinishUnits:G} refinish" : "";
                        var hoursInfo = string.Join(", ", new[] { laborDisplay, refinishDisplay }.Where(s => !string.IsNullOrEmpty(s)));

                        suggestions.Add(new CoachingSuggestion
                        {
                            Title = manualLine.Description,
                            Description = !string.IsNullOrEmpty(hoursInfo) ? hoursInfo : $"Manual line for {op.PartName}.",
                            WhyNeeded = $"Added {manualLine.TimesUsed}x in past estimates with {manualPattern.ParentOperationType} {manualPattern.ParentPartName}.",
                            Category = "Learned Patterns",
                            Severity = manualLine.TimesUsed >= 3 ? CoachingSeverity.High : CoachingSeverity.Medium,
                            TriggeredBy = op.PartName,
                            EstimatedCost = manualLine.AvgPrice > 0 ? manualLine.AvgPrice : manualLine.Price,
                            LaborHours = manualLine.LaborUnits + manualLine.RefinishUnits,
                            Source = "Learned Patterns"
                        });
                    }
                }

                // Query direct operations for this part — skip entries with no valid op type
                var queryResult = learningService.QueryOperationsForPart(op.PartName, op.OperationType);
                foreach (var suggested in queryResult.SuggestedOperations)
                {
                    if (suggested.Confidence < 0.4 || suggested.TimesUsed < 2) continue;
                    if (string.IsNullOrWhiteSpace(suggested.OperationType)) continue;
                    var key = $"{suggested.PartName}|{suggested.OperationType}".ToLowerInvariant();
                    if (!seenKeys.Add(key)) continue;

                    suggestions.Add(new CoachingSuggestion
                    {
                        Title = $"{suggested.OperationType} {suggested.PartName}".Trim(),
                        Description = !string.IsNullOrEmpty(suggested.Description) ? suggested.Description : $"Commonly done with {op.OperationType} {op.PartName}.",
                        WhyNeeded = $"Found in {suggested.TimesUsed} past estimates ({suggested.Confidence:P0} of the time) when {op.OperationType} {op.PartName} is present.",
                        Category = !string.IsNullOrEmpty(suggested.Category) ? suggested.Category : "Learned Patterns",
                        Severity = suggested.Confidence >= 0.7 ? CoachingSeverity.High : CoachingSeverity.Medium,
                        TriggeredBy = op.PartName,
                        EstimatedCost = suggested.TypicalPrice,
                        LaborHours = suggested.TypicalLaborHours,
                        Source = "Learned Patterns"
                    });
                }

                // Query co-occurring operations — only keep those with a valid op type
                var related = learningService.GetRelatedOperations(op.PartName, op.OperationType ?? "Replace");
                foreach (var coOp in related)
                {
                    if (coOp.TimesSeenTogether < 2) continue;
                    // Skip co-occurrences with no operation type — those are parts, not labor
                    if (string.IsNullOrWhiteSpace(coOp.OperationType)) continue;
                    if (!_validOpTypes.Contains(coOp.OperationType.Trim())) continue;
                    var key = $"{coOp.PartName}|{coOp.OperationType}".ToLowerInvariant();
                    if (!seenKeys.Add(key)) continue;

                    suggestions.Add(new CoachingSuggestion
                    {
                        Title = $"{coOp.OperationType} {coOp.PartName}".Trim(),
                        Description = $"Co-occurs with {op.OperationType} {op.PartName} in past estimates.",
                        WhyNeeded = $"Seen together {coOp.TimesSeenTogether} times in uploaded estimates.",
                        Category = "Learned Patterns",
                        Severity = coOp.CoOccurrenceRate >= 0.7 ? CoachingSeverity.High : CoachingSeverity.Medium,
                        TriggeredBy = op.PartName,
                        EstimatedCost = coOp.AvgPrice,
                        LaborHours = coOp.AvgLaborHours,
                        Source = "Learned Patterns"
                    });
                }
            }

            // Filter out parts — only keep actual labor operations
            suggestions.RemoveAll(s => IsPartNotOperation(s.Title));

            return suggestions;
        }

        /// <summary>
        /// Valid collision-side operation types that indicate a labor operation, not a part.
        /// </summary>
        private static readonly HashSet<string> _validOpTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "replace", "repl", "repair", "rpr", "refinish", "refn", "blend", "blnd",
            "r&i", "r/i", "r+i", "ri", "overhaul", "o/h", "align", "algn",
            "sublet", "subl", "inspect", "scan", "calibrate", "calibration",
            "diagnostic", "setup", "test", "check", "verify", "clean", "wash",
            "mask", "cover", "tint", "prime", "block", "sand"
        };

        /// <summary>
        /// Returns true if the suggestion title looks like a physical part (emblem, clips,
        /// molding, etc.) rather than a labor operation. Parts should NOT be coaching suggestions.
        /// </summary>
        private static bool IsPartNotOperation(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return true;

            var lower = title.Trim().ToLowerInvariant();

            // If it starts with a valid operation type, it's a real operation
            foreach (var op in _validOpTypes)
            {
                if (lower.StartsWith(op + " ") || lower.StartsWith(op + "\t"))
                    return false;
            }

            // If it contains a part number pattern (6+ alphanumeric chars like ABC12345), it's a part
            if (Regex.IsMatch(lower, @"\b[a-z]*\d{3,}[a-z]*\b") && !Regex.IsMatch(lower, @"\b\d+\.?\d*\s*(hr|hour|labor|refin)"))
                return true;

            // Known part-only keywords — if the title is JUST a part name with no op type, filter it
            string[] partKeywords = {
                "emblem", "badge", "nameplate", "decal", "sticker", "logo",
                "clip", "clips", "fastener", "fasteners", "rivet", "rivets",
                "bolt", "bolts", "nut", "nuts", "screw", "screws",
                "bracket", "retainer", "grommet", "pin", "pins",
                "tape", "adhesive", "sealer", "sealant", "foam",
                "wire", "wiring", "harness", "connector", "pigtail",
                "weatherstrip", "seal", "gasket",
                "insulation", "dampener", "deadener",
                "filler", "putty", "primer"
            };

            // If the whole suggestion is just a part keyword or a part keyword with modifiers, skip it
            foreach (var pk in partKeywords)
            {
                if (lower == pk || lower.EndsWith(" " + pk) || lower.EndsWith(" " + pk + "s"))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a suggestion's operation is already present in the accumulated OCR text.
        /// Uses the suggestion title and common keywords/aliases to search.
        /// </summary>
        private static bool IsSuggestionOnEstimate(CoachingSuggestion suggestion, string allSeenTextLower)
        {
            // Build a set of keywords to search for from the suggestion
            var title = suggestion.Title.ToLowerInvariant();

            // Direct title match
            if (allSeenTextLower.Contains(title)) return true;

            // Check common aliases/keywords for known suggestion types
            var keywords = GetSuggestionKeywords(title, suggestion.Category.ToLowerInvariant());
            foreach (var keyword in keywords)
            {
                if (allSeenTextLower.Contains(keyword)) return true;
            }

            return false;
        }

        /// <summary>
        /// Returns search keywords/aliases for a suggestion so we can find it in OCR text
        /// even if it's worded slightly differently.
        /// </summary>
        private static List<string> GetSuggestionKeywords(string titleLower, string categoryLower)
        {
            var keywords = new List<string>();

            // === ELECTRICAL SOP ===

            // Battery disconnect/reconnect
            if (titleLower.Contains("battery") || titleLower.Contains("disconnect"))
                keywords.AddRange(new[] { "disconnect battery", "reconnect battery", "d/c battery",
                    "battery disconnect", "disconnect and reconnect", "disconnect & reconnect",
                    "disconnect/reconnect" });

            // Test battery condition
            if (titleLower.Contains("test battery"))
                keywords.AddRange(new[] { "test battery", "battery condition", "battery test" });

            // Electronic reset
            if (titleLower.Contains("electronic reset"))
                keywords.AddRange(new[] { "electronic reset", "module reset", "window relearn",
                    "idle relearn", "initialization" });

            // Cover/protect electrical
            if (titleLower.Contains("cover") && titleLower.Contains("electrical"))
                keywords.AddRange(new[] { "cover and protect electrical", "cover electrical",
                    "protect electrical", "cover & protect electrical" });

            // Battery support
            if (titleLower.Contains("battery support"))
                keywords.AddRange(new[] { "battery support", "memory saver", "ks-100",
                    "keep alive", "keep-alive", "battery charger" });

            // Charge/maintain battery during ADAS
            if (titleLower.Contains("charge") && titleLower.Contains("battery"))
                keywords.AddRange(new[] { "charge and maintain", "charge & maintain",
                    "maintain battery", "charge battery" });

            // === VEHICLE DIAGNOSTICS SOP ===

            // Pre-Scan
            if (titleLower.Contains("pre") && titleLower.Contains("scan"))
                keywords.AddRange(new[] { "pre-scan", "pre scan", "pre-repair scan",
                    "prescan", "pre repair scan" });

            // In-Process Scan
            if (titleLower.Contains("in-process") || titleLower.Contains("in process"))
                keywords.AddRange(new[] { "in-process scan", "in process scan" });

            // Post Scan
            if (titleLower.Contains("post") && titleLower.Contains("scan"))
                keywords.AddRange(new[] { "post-scan", "post scan", "post-repair scan",
                    "postscan", "post repair scan" });

            // Setup Scan Tool
            if (titleLower.Contains("scan tool") || titleLower.Contains("setup scan"))
                keywords.AddRange(new[] { "scan tool", "setup scan", "diagnostic scan",
                    "diagnostic" });

            // Dynamic Systems Verification
            if (titleLower.Contains("dynamic") || titleLower.Contains("systems verification"))
                keywords.AddRange(new[] { "dynamic systems", "systems verification",
                    "drive cycle", "test drive", "road test" });

            // OEM Research
            if (titleLower.Contains("oem"))
                keywords.AddRange(new[] { "oem research", "oem procedure", "oem position",
                    "repair procedure", "oem repair" });

            // ADAS Diagnostic Report
            if (titleLower.Contains("adas") && titleLower.Contains("report"))
                keywords.AddRange(new[] { "adas diagnostic", "adas report", "diagnostic report" });

            // Setup ADAS Equipment
            if (titleLower.Contains("adas") && titleLower.Contains("equipment"))
                keywords.AddRange(new[] { "adas equipment", "setup adas", "adas calibration",
                    "calibration equipment" });

            // Simulate Full Fluids
            if (titleLower.Contains("simulate") || titleLower.Contains("full fluids"))
                keywords.AddRange(new[] { "simulate full fluids", "simulate fluids",
                    "full fluids", "fluid levels" });

            // Check and Adjust Tire Pressure
            if (titleLower.Contains("tire pressure") || titleLower.Contains("adjust tire"))
                keywords.AddRange(new[] { "tire pressure", "adjust tire", "check tire",
                    "check and adjust tire" });

            // === MISCELLANEOUS SOP ===

            // Pre-Wash
            if (titleLower.Contains("pre-wash") || titleLower.Contains("pre wash"))
                keywords.AddRange(new[] { "pre-wash", "pre wash", "prewash" });

            // Clean for Delivery
            if (titleLower.Contains("clean") && titleLower.Contains("delivery"))
                keywords.AddRange(new[] { "clean for delivery", "final clean", "delivery clean" });

            // Glass Cleaner
            if (titleLower.Contains("glass clean"))
                keywords.AddRange(new[] { "glass cleaner", "clean glass" });

            // Mask and Protect
            if (titleLower.Contains("mask") && titleLower.Contains("protect"))
                keywords.AddRange(new[] { "mask and protect", "mask & protect", "mask/protect" });

            // Parts Disposal
            if (titleLower.Contains("parts disposal") || titleLower.Contains("disposal"))
                keywords.AddRange(new[] { "parts disposal", "dispose", "disposal" });

            // Hazardous Waste
            if (titleLower.Contains("hazardous") || titleLower.Contains("hazmat"))
                keywords.AddRange(new[] { "hazardous waste", "haz waste", "hazmat",
                    "hazardous material" });

            // Misc Hardware
            if (titleLower.Contains("misc hardware") || titleLower.Contains("miscellaneous hardware"))
                keywords.AddRange(new[] { "misc hardware", "miscellaneous hardware",
                    "misc. hardware", "clips", "fasteners" });

            // Steering Wheel Cover / Seat Cover / Floor Mat
            if (titleLower.Contains("seat cover") || titleLower.Contains("floor mat") || titleLower.Contains("steering wheel cover"))
                keywords.AddRange(new[] { "seat cover", "floor mat", "steering wheel cover",
                    "interior protection" });

            // Refinish Material Invoice
            if (titleLower.Contains("refinish material"))
                keywords.AddRange(new[] { "refinish material", "paint material", "rmc",
                    "material invoice" });

            // Color Tint
            if (titleLower.Contains("color tint") || titleLower.Contains("colour tint"))
                keywords.AddRange(new[] { "color tint", "colour tint", "tint" });

            // Spray Out Cards
            if (titleLower.Contains("spray out"))
                keywords.AddRange(new[] { "spray out", "sprayout", "spray-out", "test card" });

            // Static Gun
            if (titleLower.Contains("static gun"))
                keywords.AddRange(new[] { "static gun", "tack cloth", "dust removal" });

            // Touch Up Painted Bolts
            if (titleLower.Contains("touch up"))
                keywords.AddRange(new[] { "touch up", "touchup", "touch-up" });

            // Monitor Flash and Cure Time
            if (titleLower.Contains("flash") && titleLower.Contains("cure"))
                keywords.AddRange(new[] { "flash and cure", "flash & cure", "flash/cure",
                    "monitor flash", "cure time" });

            // Cover Car for Overspray
            if (titleLower.Contains("cover") && titleLower.Contains("overspray"))
                keywords.AddRange(new[] { "cover for overspray", "overspray", "cover car" });

            // Cover for Edging
            if (titleLower.Contains("cover") && titleLower.Contains("edging"))
                keywords.AddRange(new[] { "cover for edging", "edging" });

            // Mask for Buffing
            if (titleLower.Contains("mask") && titleLower.Contains("buff"))
                keywords.AddRange(new[] { "mask for buffing", "mask for buff", "buffing" });

            // Cover Engine Compartment
            if (titleLower.Contains("engine compartment"))
                keywords.AddRange(new[] { "cover engine", "engine compartment" });

            // Cover Interior and Jambs for Refinish
            if (titleLower.Contains("interior") && titleLower.Contains("jamb"))
                keywords.AddRange(new[] { "cover interior", "interior and jambs",
                    "interior & jambs", "jambs for refinish" });

            // === GENERIC MATCHES ===

            // Calibration
            if (titleLower.Contains("calibrat"))
                keywords.AddRange(new[] { "calibration", "calibrate", "adas calibration" });

            // Blend
            if (titleLower.Contains("blend"))
                keywords.AddRange(new[] { "blend", "blending", "blnd" });

            // Corrosion
            if (titleLower.Contains("corrosion"))
                keywords.AddRange(new[] { "corrosion", "anti-corrosion", "corrosion protection",
                    "cavity wax", "weld-thru primer", "weld thru primer" });

            // Flex additive
            if (titleLower.Contains("flex"))
                keywords.AddRange(new[] { "flex additive", "flex add", "flexible additive" });

            // R&I
            if (titleLower.Contains("r&i") || titleLower.Contains("remove") || titleLower.Contains("reinstall"))
                keywords.AddRange(new[] { "r&i", "r & i", "remove & install",
                    "remove and install", "remove/install" });

            // Module programming
            if (titleLower.Contains("module") || titleLower.Contains("programming"))
                keywords.AddRange(new[] { "module programming", "reprogram", "initialization" });

            // Seam sealer
            if (titleLower.Contains("seam seal"))
                keywords.AddRange(new[] { "seam sealer", "seam seal" });

            return keywords;
        }

        private static string? ExtractVehicleInfo(string rawText)
        {
            // Match patterns like "2024 Toyota Camry" or "2023 Honda Civic"
            var match = System.Text.RegularExpressions.Regex.Match(
                rawText,
                @"(20\d{2})\s+([A-Za-z]{3,})\s+([A-Za-z]{2,})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value.Trim() : null;
        }

        private static string? ExtractCustomerName(string rawText)
        {
            // Look for common CCC/Mitchell patterns:
            // "Owner: John Smith", "Customer: Jane Doe", "Insured: Bob Jones"
            var patterns = new[]
            {
                @"(?:Owner|Customer|Insured|Claimant)\s*[:\-]\s*([A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)",
                @"(?:OWNER|CUSTOMER|INSURED|CLAIMANT)\s*[:\-]\s*([A-Z][A-Za-z]+(?:\s+[A-Z][A-Za-z]+)+)"
            };

            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(rawText, pattern);
                if (match.Success && match.Groups.Count > 1)
                    return match.Groups[1].Value.Trim();
            }
            return null;
        }


        /// <summary>
        /// Extracts the core application name from a window title so we only reset
        /// when switching between entirely different apps (CCC → Mitchell), not when
        /// the title changes within the same app (CCC tab navigation, claim number, etc.)
        /// </summary>
        private static string GetCoreAppName(string windowTitle)
        {
            if (string.IsNullOrEmpty(windowTitle)) return "";

            var title = windowTitle.ToLowerInvariant();
            if (title.Contains("ccc") || title.Contains("caborneone")) return "CCC";
            if (title.Contains("mitchell")) return "Mitchell";
            if (title.Contains("audatex") || title.Contains("audaexplore")) return "Audatex";

            // For non-estimating apps, use the first word as the app identity
            var firstWord = windowTitle.Split(new[] { ' ', '-', '–', '—', '|' }, StringSplitOptions.RemoveEmptyEntries);
            return firstWord.Length > 0 ? firstWord[0] : windowTitle;
        }

        private static string ComputeContentHash(ScreenOcrResult result)
        {
            var sb = new StringBuilder();

            // Hash on structured operations if available
            if (result.DetectedOperations != null && result.DetectedOperations.Count > 0)
            {
                foreach (var op in result.DetectedOperations.OrderBy(o => o.Description))
                {
                    sb.Append(op.Description).Append('|')
                      .Append(op.OperationType).Append('|')
                      .Append(op.PartName).Append('|')
                      .Append(op.LaborHours).Append(';');
                }
            }
            else
            {
                // Fall back to raw text hash
                sb.Append(result.RawText ?? "");
            }

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(bytes);
        }

        private static string NormalizeKey(string category, string title)
        {
            var normalized = (category + "|" + title)
                .ToLowerInvariant()
                .Replace(" ", "")
                .Replace("-", "");
            return normalized;
        }

        private static bool IsFuzzyDuplicate(string key, HashSet<string> existingKeys)
        {
            foreach (var existing in existingKeys)
            {
                if (existing.Length == 0 || key.Length == 0) continue;

                var keyParts = key.Split('|');
                var existingParts = existing.Split('|');
                if (keyParts.Length < 2 || existingParts.Length < 2) continue;
                if (keyParts[0] != existingParts[0]) continue;

                var keyTitle = keyParts[1];
                var existingTitle = existingParts[1];

                if (keyTitle.Contains(existingTitle) || existingTitle.Contains(keyTitle))
                    return true;

                var maxLen = Math.Max(keyTitle.Length, existingTitle.Length);
                if (maxLen <= 3) continue;
                var commonChars = keyTitle.Intersect(existingTitle).Count();
                if ((double)commonChars / maxLen >= 0.8)
                    return true;
            }
            return false;
        }

        private static CoachingSeverity MapSeverity(McStudDesktop.Services.IssueSeverity severity)
        {
            return severity switch
            {
                McStudDesktop.Services.IssueSeverity.Critical => CoachingSeverity.Critical,
                McStudDesktop.Services.IssueSeverity.High => CoachingSeverity.High,
                McStudDesktop.Services.IssueSeverity.Medium => CoachingSeverity.Medium,
                McStudDesktop.Services.IssueSeverity.Low => CoachingSeverity.Low,
                _ => CoachingSeverity.Medium
            };
        }

        private static CoachingSeverity MapPriority(string priority)
        {
            return priority.ToLowerInvariant() switch
            {
                "critical" => CoachingSeverity.Critical,
                "high" => CoachingSeverity.High,
                "medium" => CoachingSeverity.Medium,
                "low" => CoachingSeverity.Low,
                _ => CoachingSeverity.Medium
            };
        }
    }
}
