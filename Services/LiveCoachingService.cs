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
        private readonly McStudDesktop.Services.GhostConfigService _ghostConfig;
        private readonly McStudDesktop.Services.ExcelGhostDataProvider _excelProvider;
        private readonly McStudDesktop.Services.OperationKnowledgeService _knowledgeService;

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
        private string? _trackedVin;
        private int _accumulatedOpsOrder = 0;
        private readonly Dictionary<string, int> _opInsertionOrder = new(StringComparer.OrdinalIgnoreCase);
        // Freshness tracking: how many consecutive captures each operation has been absent
        private readonly Dictionary<string, int> _opMissCount = new(StringComparer.OrdinalIgnoreCase);
        private const int MaxMissesBeforeRemoval = 12; // Remove after 12 consecutive absences (allows scrolling through long estimates)

        private const int DebounceMs = 500;

        public event EventHandler<CoachingSnapshot>? SuggestionsUpdated;
        public event EventHandler<bool>? CoachingStateChanged;

        public bool IsRunning => _isRunning;
        public CoachingSnapshot? LatestSnapshot { get; private set; }

        private LiveCoachingService()
        {
            _screenMonitor = ScreenMonitorService.Instance;
            _scoringService = McStudDesktop.Services.EstimateScoringService.Instance;
            _analyzerService = McStudDesktop.Services.SmartEstimateAnalyzerService.Instance;
            _ghostConfig = McStudDesktop.Services.GhostConfigService.Instance;
            _excelProvider = McStudDesktop.Services.ExcelGhostDataProvider.Instance;
            _knowledgeService = McStudDesktop.Services.OperationKnowledgeService.Instance;
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
            _lastSourceWindow = "";
            ResetAccumulatedState();
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

        public IReadOnlyList<ParsedEstimateLine> AccumulatedOperations =>
            _accumulatedOps
                .OrderBy(kvp => _opInsertionOrder.GetValueOrDefault(kvp.Key, int.MaxValue))
                .Select(kvp => kvp.Value)
                .ToList();

        public FocusedPartContext? DetectFocusedPart(ScreenOcrResult result)
        {
            var topPart = result.DetectedOperations
                .Where(op => !string.IsNullOrWhiteSpace(op.PartName))
                .GroupBy(op => op.PartName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (topPart == null) return null;
            return new FocusedPartContext { PartName = topPart.Key, OperationType = topPart.First().OperationType };
        }

        public void ResetVehicle()
        {
            ResetAccumulatedState();
            lock (_lock) { _dismissedIds.Clear(); }
        }

        private void ResetAccumulatedState()
        {
            _accumulatedOps.Clear();
            _accumulatedRawText.Clear();
            _opInsertionOrder.Clear();
            _opMissCount.Clear();
            _accumulatedOpsOrder = 0;
            _vehicleInfo = null;
            _customerName = null;
            _trackedVin = null;
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

        private async void ProcessOcrResult(ScreenOcrResult result)
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
                    ResetAccumulatedState();
                }
            }
            _lastSourceWindow = sourceWindow;

            // VIN-based vehicle change detection (fuzzy: allow up to 2 OCR character differences)
            if (!string.IsNullOrEmpty(result.DetectedVin))
            {
                if (_trackedVin != null && !VinsAreSimilar(_trackedVin, result.DetectedVin))
                {
                    Debug.WriteLine($"[LiveCoaching] VIN changed from \"{_trackedVin}\" to \"{result.DetectedVin}\" — resetting accumulated data");
                    ResetAccumulatedState();
                }
                // Only update tracked VIN if we don't have one yet (first detection wins, avoids OCR drift)
                if (_trackedVin == null)
                    _trackedVin = result.DetectedVin;
            }

            // Accumulate structured operations from this capture
            // Skip garbage: must have a valid operation type and a meaningful part name
            var currentCaptureKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (result.DetectedOperations != null)
            {
                foreach (var op in result.DetectedOperations)
                {
                    // Allow known body panels even without an explicit operation type
                    var hasValidOpType = !string.IsNullOrWhiteSpace(op.OperationType) &&
                                         _validOpTypes.Contains(op.OperationType.Trim());
                    var isKnownPanel = !string.IsNullOrWhiteSpace(op.PartName) &&
                                       _knownBodyPanels.Contains(op.PartName.Trim());
                    if (!hasValidOpType && !isKnownPanel) continue;
                    if (string.IsNullOrWhiteSpace(op.PartName) || op.PartName.Length < 3) continue;
                    // Skip garbage that has no letters (dots, bullets, symbols like "• •")
                    if (!Regex.IsMatch(op.PartName, @"[a-zA-Z]{2,}")) continue;
                    // Skip part numbers (numeric-leading entries from CCC parts catalog)
                    if (Regex.IsMatch(op.PartName.Trim(), @"^\d{2,}")) continue;
                    // Skip single-word hardware sub-parts (bolt, rivet, nut, etc.)
                    var trimmedPart = op.PartName.Trim();
                    if (!trimmedPart.Contains(' ') && _hardwareSubParts.Contains(trimmedPart)) continue;

                    var key = $"{op.Description}|{op.PartName}|{op.OperationType}".ToLowerInvariant();
                    currentCaptureKeys.Add(key);
                    if (!_opInsertionOrder.ContainsKey(key))
                        _opInsertionOrder[key] = _accumulatedOpsOrder++;
                    _opMissCount[key] = 0; // Reset miss count — we just saw it
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

            // Deletion detection: increment miss count for operations not seen in this capture,
            // remove if they've been absent for too many consecutive captures.
            if (currentCaptureKeys.Count > 0)
            {
                var keysToRemove = new List<string>();
                foreach (var existingKey in _accumulatedOps.Keys.ToList())
                {
                    if (!currentCaptureKeys.Contains(existingKey))
                    {
                        _opMissCount.TryGetValue(existingKey, out var misses);
                        misses++;
                        _opMissCount[existingKey] = misses;
                        if (misses >= MaxMissesBeforeRemoval)
                            keysToRemove.Add(existingKey);
                    }
                }
                foreach (var key in keysToRemove)
                {
                    _accumulatedOps.Remove(key);
                    _opInsertionOrder.Remove(key);
                    _opMissCount.Remove(key);
                    Debug.WriteLine($"[LiveCoaching] Removed stale operation: {key}");
                }
            }

            // Accumulate raw text for vehicle/customer info only — no part scanning.
            // Strip CCC parts catalog sidebar text (after "Add to Estimate") to avoid
            // false "confirmed on estimate" matches from catalog items.
            if (!string.IsNullOrWhiteSpace(result.RawText))
            {
                var cleanedRaw = result.RawText;
                var catalogIdx = cleanedRaw.IndexOf("Add to Estimate", StringComparison.OrdinalIgnoreCase);
                if (catalogIdx > 50)
                    cleanedRaw = cleanedRaw.Substring(0, catalogIdx);
                _accumulatedRawText.AppendLine(cleanedRaw);
                if (_vehicleInfo == null)
                    _vehicleInfo = ExtractVehicleInfo(cleanedRaw);
                if (_customerName == null)
                    _customerName = ExtractCustomerName(cleanedRaw);
            }

            // Build parsed lines from ONLY structured operations
            var parsedLines = new List<ParsedEstimateLine>(_accumulatedOps.Values);

            Debug.WriteLine($"[LiveCoaching] Processing {parsedLines.Count} structured operations");

            // --- New suggestion pipeline ---
            // 1. SOP List baseline suggestions (context-filtered)
            var sopSuggestions = GetSOPListSuggestions(parsedLines, _accumulatedRawText.ToString());

            // 2. Excel Tool suggestions (from shop's estimating tool data)
            var excelSuggestions = GetExcelToolSuggestions(parsedLines);

            // 2.5 Rules Engine suggestions (material + op-type aware)
            var rulesSuggestions = GetRulesEngineSuggestions(parsedLines);

            // 3. Knowledge Base suggestions (missing ops detection)
            var kbSuggestions = GetKnowledgeBaseSuggestions(parsedLines);

            // 4. Learned pattern suggestions (from past uploaded estimates)
            var learnedSuggestions = GetLearnedPatternSuggestions(parsedLines);

            // 5. Run scoring/analysis engines on structured ops only
            var scoringResult = parsedLines.Count > 0
                ? _scoringService.ScoreEstimate(parsedLines)
                : new McStudDesktop.Services.EstimateScoringResult { OverallScore = 0, Grade = "--" };
            var analysisResult = parsedLines.Count > 0
                ? _analyzerService.AnalyzeEstimate(parsedLines)
                : new McStudDesktop.Services.AnalysisResult();

            // Merge all suggestions: SOP → Excel → KB → Learned → Scoring → Analyzer
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

            // 2. Excel Tool suggestions (High — from shop's estimating reference)
            foreach (var excel in excelSuggestions)
            {
                var key = NormalizeKey(excel.Category, excel.Title);
                if (seenKeys.Contains(key)) continue;
                if (IsFuzzyDuplicate(key, seenKeys)) continue;
                seenKeys.Add(key);
                excel.Id = key;
                suggestions.Add(excel);
            }

            // 2.5. Rules Engine suggestions (High — material + operation-type aware)
            foreach (var rule in rulesSuggestions)
            {
                var key = NormalizeKey(rule.Category, rule.Title);
                if (seenKeys.Contains(key)) continue;
                if (IsFuzzyDuplicate(key, seenKeys)) continue;
                seenKeys.Add(key);
                rule.Id = key;
                suggestions.Add(rule);
            }

            // 3. Knowledge Base suggestions (High — structural/procedural requirements)
            foreach (var kb in kbSuggestions)
            {
                var key = NormalizeKey(kb.Category, kb.Title);
                if (seenKeys.Contains(key)) continue;
                if (IsFuzzyDuplicate(key, seenKeys)) continue;
                seenKeys.Add(key);
                kb.Id = key;
                suggestions.Add(kb);
            }

            // 4. Learned pattern suggestions (High/Medium — from real past estimates)
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
                    EstimatedCost = 0, // No hardcoded prices — only real data from Excel/uploaded estimates
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
                    EstimatedCost = 0, // TypicalCost is hardcoded — only show real data
                    LaborHours = suggestion.LaborHours,
                    Source = "Analyzer"
                });
            }

            // AI-powered deduplication for borderline pairs (once per cycle, not hot-path)
            suggestions = await TryAiDedupAsync(suggestions);

            // Cross-check suggestions against structured ops AND accumulated raw text.
            // Structured ops alone miss many SOP/misc items (pre-wash, clean for delivery, etc.)
            // that appear in OCR text but don't parse as structured operations.
            var structuredOpsText = string.Join(" | ", _accumulatedOps.Values
                .Select(op => $"{op.Description} {op.PartName} {op.OperationType} {op.RawLine}"))
                .ToLowerInvariant();
            var rawText = _accumulatedRawText.ToString().ToLowerInvariant();
            // Apply OCR corrections so "Batten,'" → "battery" etc. for better on-estimate matching
            var combinedSeenText = ScreenOcrService.ApplyOcrCorrections(structuredOpsText + " | " + rawText);

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

            // B2: Suggestion cap — keep all Critical/High, cap Medium so total <=20, Low only if <15
            var criticalHigh = suggestions.Where(s => s.Severity == CoachingSeverity.Critical || s.Severity == CoachingSeverity.High).ToList();
            var mediums = suggestions.Where(s => s.Severity == CoachingSeverity.Medium).ToList();
            var lows = suggestions.Where(s => s.Severity == CoachingSeverity.Low).ToList();
            var capped = new List<CoachingSuggestion>(criticalHigh);
            var mediumSlots = Math.Max(0, 20 - capped.Count);
            capped.AddRange(mediums.Take(mediumSlots));
            if (capped.Count < 15)
                capped.AddRange(lows.Take(15 - capped.Count));
            suggestions = capped;

            var confirmedCount = suggestions.Count(s => s.IsConfirmedOnEstimate);
            var focusedPart = DetectFocusedPart(result);
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
                VIN = _trackedVin,
                FocusedPart = focusedPart,
                Timestamp = DateTime.Now
            };

            LatestSnapshot = snapshot;
            Debug.WriteLine($"[LiveCoaching] Snapshot: score={snapshot.Score}, grade={snapshot.Grade}, suggestions={suggestions.Count} (SOP={sopSuggestions.Count}, Excel={excelSuggestions.Count}, KB={kbSuggestions.Count}, learned={learnedSuggestions.Count})");
            SuggestionsUpdated?.Invoke(this, snapshot);
        }

        // SOP items shown on every collision job (Critical)
        private static readonly HashSet<string> _alwaysShowSop = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pre-Scan", "Post Scan", "Disconnect/Reconnect Battery", "Disconnect / Reconnect Battery",
            "Electronic Reset", "OEM Research", "Refinish Material Invoice",
            "Pre-Wash", "Clean for Delivery", "Parts Disposal"
        };

        // SOP items only relevant when refinish work is detected
        private static readonly HashSet<string> _refinishOnlySop = new(StringComparer.OrdinalIgnoreCase)
        {
            "Color Tint", "Spray Out Cards", "Static Gun", "Touch Up Painted Bolts",
            "Monitor Flash and Cure Time", "Cover Car for Overspray", "Cover for Edging",
            "Mask for Buffing", "Cover Interior and Jambs for Refinish", "Glass Cleaner",
            "Mask and Protect", "Cover Engine Compartment"
        };

        // SOP items only relevant when ADAS-triggering parts are detected
        private static readonly HashSet<string> _adasOnlySop = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADAS Diagnostic Report", "Setup ADAS Equipment",
            "Charge and Maintain Battery during ADAS", "Simulate Full Fluids",
            "Check and Adjust Tire Pressure"
        };

        // SOP items only shown when estimate has >=3 operations (not a tiny sublet/supplement)
        private static readonly HashSet<string> _multiOpOnlySop = new(StringComparer.OrdinalIgnoreCase)
        {
            "Battery Support", "Test Battery Condition", "Cover and Protect Electrical",
            "Setup Scan Tool", "Dynamic Systems Verification", "In-Process Scan",
            "Steering Wheel Cover", "Seat Cover", "Floor Mat",
            "Hazardous Waste", "Misc Hardware"
        };

        // Keywords that indicate refinish work
        private static readonly string[] _refinishKeywords = { "refinish", "blend", "paint", "clear coat", "refn", "blnd" };

        // Keywords that indicate ADAS-triggering parts
        private static readonly string[] _adasKeywords = { "windshield", "camera", "radar", "sensor", "calibrat" };

        /// <summary>
        /// Returns context-filtered SOP List baseline items.
        /// Items are gated by what's actually on the estimate (refinish, ADAS, operation count).
        /// </summary>
        private List<CoachingSuggestion> GetSOPListSuggestions(List<ParsedEstimateLine> structuredOps, string rawText)
        {
            var enabledItems = SOPBaselineService.Instance.GetEnabledItems();

            // Build context signals
            var lowerRaw = rawText.ToLowerInvariant();
            var opsText = string.Join(" ", structuredOps.Select(o =>
                $"{o.Description} {o.PartName} {o.OperationType}")).ToLowerInvariant();
            var combined = lowerRaw + " " + opsText;

            var hasRefinish = _refinishKeywords.Any(kw => combined.Contains(kw)) ||
                              structuredOps.Any(o => o.RefinishHours > 0) ||
                              structuredOps.Any(o => OperationRulesEngine.Instance.InvolvesPaint(o.OperationType));
            var hasAdas = _adasKeywords.Any(kw => combined.Contains(kw));
            var hasEnoughOps = structuredOps.Count >= 3;

            return enabledItems
                .Where(item =>
                {
                    var name = item.Name;
                    // Always-show items pass unconditionally
                    if (_alwaysShowSop.Contains(name)) return true;
                    // Refinish-only items gated on refinish detection
                    if (_refinishOnlySop.Contains(name)) return hasRefinish;
                    // ADAS-only items gated on ADAS part detection
                    if (_adasOnlySop.Contains(name)) return hasAdas;
                    // Multi-op items gated on operation count
                    if (_multiOpOnlySop.Contains(name)) return hasEnoughOps;
                    // Uncategorized SOP items — show if estimate has any operations
                    return structuredOps.Count > 0;
                })
                .Select(item =>
                {
                    var cost = 0m; // No hardcoded SOP prices — only real data from Excel/uploaded estimates

                    return new CoachingSuggestion
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
                        EstimatedCost = cost,
                        LaborHours = item.LaborHours
                    };
                }).ToList();
        }

        /// <summary>
        /// Queries learned patterns for operations that commonly co-occur with the
        /// structured operations detected on the current estimate.
        /// Computes costs using GhostConfigService rates for consistency with Ghost Estimate.
        /// </summary>
        private List<CoachingSuggestion> GetLearnedPatternSuggestions(List<ParsedEstimateLine> structuredOps)
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

                        var totalHours = manualLine.LaborUnits + manualLine.RefinishUnits;
                        var manualCost = ComputeCostFromHours(totalHours, manualLine.Description, op.OperationType);
                        // If configured rate gives $0, fall back to learned price
                        if (manualCost == 0 && (manualLine.AvgPrice > 0 || manualLine.Price > 0))
                            manualCost = manualLine.AvgPrice > 0 ? manualLine.AvgPrice : manualLine.Price;

                        suggestions.Add(new CoachingSuggestion
                        {
                            Title = manualLine.Description,
                            Description = !string.IsNullOrEmpty(hoursInfo) ? hoursInfo : $"Manual line for {op.PartName}.",
                            WhyNeeded = $"Added {manualLine.TimesUsed}x in past estimates with {manualPattern.ParentOperationType} {manualPattern.ParentPartName}.",
                            Category = "Learned Patterns",
                            Severity = manualLine.TimesUsed >= 3 ? CoachingSeverity.High : CoachingSeverity.Medium,
                            TriggeredBy = op.PartName,
                            EstimatedCost = manualCost,
                            LaborHours = totalHours,
                            Source = "Learned Patterns"
                        });
                    }
                }

                // Query direct operations for this part — only keep operations
                // that are directly related to the triggering part (same part or sub-operations).
                // Do NOT suggest Replace/Repl of completely different parts (e.g., "Repl Roof" from a hood job).
                var queryResult = learningService.QueryOperationsForPart(op.PartName, op.OperationType);
                foreach (var suggested in queryResult.SuggestedOperations)
                {
                    if (suggested.Confidence < 0.2 || suggested.TimesUsed < 1) continue;
                    if (string.IsNullOrWhiteSpace(suggested.OperationType)) continue;

                    // FILTER: Only suggest operations on the SAME part or sub-components of that part.
                    // Don't suggest "Replace Roof" just because past estimates with a hood also had a roof.
                    if (!IsRelatedToTriggerPart(op.PartName, suggested.PartName, suggested.OperationType))
                        continue;

                    var key = $"{suggested.PartName}|{suggested.OperationType}".ToLowerInvariant();
                    if (!seenKeys.Add(key)) continue;

                    var sugCost = ComputeCostFromHours(suggested.TypicalLaborHours, suggested.OperationType ?? "", suggested.OperationType);
                    if (sugCost == 0 && suggested.TypicalPrice > 0) sugCost = suggested.TypicalPrice;

                    suggestions.Add(new CoachingSuggestion
                    {
                        Title = $"{suggested.OperationType} {suggested.PartName}".Trim(),
                        Description = !string.IsNullOrEmpty(suggested.Description) ? suggested.Description : $"Commonly done with {op.OperationType} {op.PartName}.",
                        WhyNeeded = $"Found in {suggested.TimesUsed} past estimates ({suggested.Confidence:P0} of the time) when {op.OperationType} {op.PartName} is present.",
                        Category = !string.IsNullOrEmpty(suggested.Category) ? suggested.Category : "Learned Patterns",
                        Severity = suggested.Confidence >= 0.85 ? CoachingSeverity.Critical
                                 : suggested.Confidence >= 0.5 ? CoachingSeverity.High
                                 : CoachingSeverity.Medium,
                        TriggeredBy = op.PartName,
                        EstimatedCost = sugCost,
                        LaborHours = suggested.TypicalLaborHours,
                        Source = "Learned Patterns"
                    });
                }

                // Query co-occurring operations — only keep those related to the same part area
                var related = learningService.GetRelatedOperations(op.PartName, op.OperationType ?? "Replace");
                foreach (var coOp in related)
                {
                    if (coOp.TimesSeenTogether < 1) continue;
                    if (string.IsNullOrWhiteSpace(coOp.OperationType)) continue;
                    if (!_validOpTypes.Contains(coOp.OperationType.Trim())) continue;

                    // FILTER: Only suggest co-occurring ops on the same part or sub-components.
                    if (!IsRelatedToTriggerPart(op.PartName, coOp.PartName, coOp.OperationType))
                        continue;

                    var key = $"{coOp.PartName}|{coOp.OperationType}".ToLowerInvariant();
                    if (!seenKeys.Add(key)) continue;

                    var coOpCost = ComputeCostFromHours(coOp.AvgLaborHours, coOp.OperationType ?? "", coOp.OperationType);
                    if (coOpCost == 0 && coOp.AvgPrice > 0) coOpCost = coOp.AvgPrice;

                    suggestions.Add(new CoachingSuggestion
                    {
                        Title = $"{coOp.OperationType} {coOp.PartName}".Trim(),
                        Description = $"Co-occurs with {op.OperationType} {op.PartName} in past estimates.",
                        WhyNeeded = $"Present in {coOp.CoOccurrenceRate:P0} of estimates with {op.OperationType} {op.PartName}.",
                        Category = "Learned Patterns",
                        Severity = coOp.CoOccurrenceRate >= 0.85 ? CoachingSeverity.Critical
                                 : coOp.CoOccurrenceRate >= 0.5 ? CoachingSeverity.High
                                 : CoachingSeverity.Medium,
                        TriggeredBy = op.PartName,
                        EstimatedCost = coOpCost,
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
        /// Queries Excel Tool operations relevant to the detected parts on screen.
        /// Uses the same ExcelGhostDataProvider as Ghost Estimate for consistent data.
        /// </summary>
        private List<CoachingSuggestion> GetExcelToolSuggestions(List<ParsedEstimateLine> structuredOps)
        {
            var suggestions = new List<CoachingSuggestion>();
            if (structuredOps.Count == 0) return suggestions;

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var op in structuredOps)
            {
                if (string.IsNullOrWhiteSpace(op.PartName)) continue;

                // Look up this part+operation in the Excel tool
                var lookup = _excelProvider.LookupForGhost(op.PartName, op.OperationType ?? "");
                if (!lookup.Found) continue;

                var key = $"excel|{op.PartName}|{op.OperationType}".ToLowerInvariant();
                if (!seenKeys.Add(key)) continue;

                // Skip if disabled by user override
                var desc = $"{op.OperationType} {op.PartName}".Trim();
                if (_excelProvider.IsOperationDisabled(desc)) continue;

                var laborHours = lookup.LaborHours + lookup.RefinishHours;
                var cost = ComputeCostFromHours(laborHours, desc, op.OperationType);
                if (cost == 0 && lookup.Price > 0) cost = lookup.Price;

                suggestions.Add(new CoachingSuggestion
                {
                    Title = desc,
                    Description = $"From Excel tool ({lookup.SheetName}): {lookup.LaborHours:G}h labor, {lookup.RefinishHours:G}h refinish.",
                    WhyNeeded = $"Excel estimating tool reference for {op.PartName}.",
                    Category = lookup.SheetName,
                    Severity = CoachingSeverity.High,
                    TriggeredBy = op.PartName,
                    EstimatedCost = cost,
                    LaborHours = laborHours,
                    Source = "Excel Tool"
                });
            }

            return suggestions;
        }

        /// <summary>
        /// Queries the OperationRulesEngine for material-aware operation suggestions.
        /// Classifies each part by material type and suggests operations based on
        /// material + operation type (e.g., plastic → adhesion promoter, welded → e-coat).
        /// </summary>
        private List<CoachingSuggestion> GetRulesEngineSuggestions(List<ParsedEstimateLine> structuredOps)
        {
            var engine = OperationRulesEngine.Instance;
            var learningService = EstimateLearningService.Instance;
            var suggestions = new List<CoachingSuggestion>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var op in structuredOps)
            {
                if (string.IsNullOrWhiteSpace(op.PartName)) continue;

                var suggested = engine.GetSuggestedOperations(op.PartName, op.OperationType);
                foreach (var s in suggested)
                {
                    if (!seenNames.Add(s.Name)) continue;

                    // Check if this operation is already on the estimate
                    var alreadyOnEstimate = structuredOps.Any(e =>
                        e.PartName != null && e.PartName.Contains(s.Name, StringComparison.OrdinalIgnoreCase));
                    if (alreadyOnEstimate) continue;

                    // Use learned data from uploaded estimates when available
                    var learnedStats = learningService.GetOperationStats(s.Name);
                    var hasLearned = learnedStats.TotalOccurrences > 0 && learnedStats.AvgLaborHours > 0;
                    var hours = hasLearned ? (decimal)learnedStats.AvgLaborHours : s.DefaultHours;
                    var dataSource = "Rules Engine";

                    if (hasLearned)
                    {
                        // Sanity cap: learned hours can be wildly wrong if the learning data
                        // accumulated totals instead of per-operation hours.
                        var nameLower = s.Name.ToLowerInvariant();
                        var isMaterial = s.Category?.ToLowerInvariant() is "materials" or "add" ||
                            nameLower.Contains("flex") || nameLower.Contains("adhesion") ||
                            nameLower.Contains("clear coat") || nameLower.Contains("denib") ||
                            nameLower.Contains("color tint") || nameLower.Contains("spray") ||
                            nameLower.Contains("promoter") || nameLower.Contains("additive");
                        var maxHours = isMaterial ? 0.5m : 5.0m;
                        if (hours > maxHours)
                        {
                            Debug.WriteLine($"[LiveCoaching] Capped learned hours for '{s.Name}': {hours:F1}h → {s.DefaultHours}h (max {maxHours})");
                            hours = s.DefaultHours;
                            // Also cap the fallback default
                            if (hours > maxHours)
                                hours = isMaterial ? 0.2m : 1.0m;
                        }

                        dataSource = $"Rules Engine + Learned ({learnedStats.TotalOccurrences} estimates)";
                    }
                    else
                    {
                        // Fall back to Excel tool data if no learned data
                        var excelLookup = _excelProvider.LookupForGhost(s.Name, s.OperationType);
                        if (excelLookup.Found)
                        {
                            var excelHours = excelLookup.LaborHours + excelLookup.RefinishHours;
                            if (excelHours > 0)
                                hours = excelHours;
                            dataSource = $"Rules Engine + Excel Tool ({excelLookup.SheetName})";
                        }
                    }

                    var cost = ComputeCostFromHours(hours, s.Name, s.OperationType);

                    suggestions.Add(new CoachingSuggestion
                    {
                        Title = s.Name,
                        Description = s.Description + " — " + s.WhyNeeded,
                        WhyNeeded = s.WhyNeeded,
                        Category = s.Category,
                        Severity = CoachingSeverity.High,
                        TriggeredBy = op.PartName,
                        EstimatedCost = cost,
                        LaborHours = hours,
                        Source = dataSource
                    });
                }
            }

            // Second pass: percentage-based refinish operations (Buff, DE-NIB, Feather Edge)
            bool isFirstPanel = true;
            var addedRefinishOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var op in structuredOps)
            {
                if (string.IsNullOrWhiteSpace(op.PartName)) continue;
                if (!engine.InvolvesPaint(op.OperationType)) continue;

                var refinishSuggestions = engine.GetRefinishSuggestedOperations(
                    op.PartName, op.OperationType, op.RefinishHours, op.LaborHours, isFirstPanel);

                foreach (var s in refinishSuggestions)
                {
                    if (!addedRefinishOps.Add(s.Name)) continue; // show once

                    // Skip if already on estimate
                    if (structuredOps.Any(e => e.PartName != null &&
                        e.PartName.Contains(s.Name, StringComparison.OrdinalIgnoreCase))) continue;

                    // Use learned data if available, else formula hours
                    var learnedStats = learningService.GetOperationStats(s.Name);
                    var hasLearned = learnedStats.TotalOccurrences > 0 && learnedStats.AvgLaborHours > 0;
                    var hours = hasLearned ? (decimal)learnedStats.AvgLaborHours : s.DefaultHours;
                    var dataSource = hasLearned
                        ? $"Rules Engine + Learned ({learnedStats.TotalOccurrences} estimates)"
                        : "Rules Engine (percentage formula)";

                    // Sanity cap for refinish materials
                    if (hours > 1.0m)
                    {
                        var rnLower = s.Name.ToLowerInvariant();
                        if (rnLower.Contains("denib") || rnLower.Contains("buff") ||
                            rnLower.Contains("feather") || rnLower.Contains("wet sand") ||
                            rnLower.Contains("block sand"))
                        {
                            hours = Math.Min(hours, 1.0m);
                        }
                    }

                    var cost = ComputeCostFromHours(hours, s.Name, s.OperationType);

                    suggestions.Add(new CoachingSuggestion
                    {
                        Title = s.Name,
                        Description = s.Description + " — " + s.WhyNeeded,
                        WhyNeeded = s.WhyNeeded,
                        Category = s.Category,
                        Severity = CoachingSeverity.High,
                        TriggeredBy = op.PartName,
                        EstimatedCost = cost,
                        LaborHours = hours,
                        Source = dataSource
                    });
                }
                isFirstPanel = false;
            }

            return suggestions;
        }

        /// <summary>
        /// Queries the Knowledge Base (570+ ops) for missing operations based on
        /// what's detected on the estimate. Uses same KB as Ghost Estimate.
        /// </summary>
        private List<CoachingSuggestion> GetKnowledgeBaseSuggestions(List<ParsedEstimateLine> structuredOps)
        {
            var suggestions = new List<CoachingSuggestion>();
            if (structuredOps.Count == 0) return suggestions;

            // Build estimate lines for the KB missing-op detector
            var estimateLines = structuredOps
                .Select(op => $"{op.OperationType} {op.PartName} {op.Description}".Trim())
                .ToList();

            var missingOps = _knowledgeService.DetectMissingOperations(estimateLines);
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var missing in missingOps)
            {
                var key = $"kb|{missing.Operation}".ToLowerInvariant();
                if (!seenKeys.Add(key)) continue;

                // Check if the user has disabled this category
                if (!_ghostConfig.IsCategoryEnabled(missing.Operation)) continue;

                // Look up labor hours from the KB justification or Excel tool
                var laborHours = 0m;
                var cost = 0m;

                // Try Excel tool first for consistent pricing
                var excelMatches = _excelProvider.FindOperation(missing.Operation);
                if (excelMatches.Count > 0 && excelMatches[0].RelevanceScore >= 50)
                {
                    laborHours = excelMatches[0].Operation.LaborHours + excelMatches[0].Operation.RefinishHours;
                    cost = ComputeCostFromHours(laborHours, missing.Operation, null);
                    if (cost == 0) cost = excelMatches[0].Operation.Price;
                }

                // Fall back: look up from KB part operations
                if (laborHours == 0)
                {
                    // Try to find from part operations in the KB
                    foreach (var op in structuredOps)
                    {
                        if (string.IsNullOrWhiteSpace(op.PartName)) continue;
                        var partOps = _knowledgeService.GetOperationsForPart(op.PartName);
                        var match = partOps.Operations.FirstOrDefault(o =>
                            o.Name.Contains(missing.Operation, StringComparison.OrdinalIgnoreCase) ||
                            missing.Operation.Contains(o.Name, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            laborHours = match.LaborHours;
                            cost = 0; // KB TypicalCost is hardcoded — only use Excel tool prices
                            break;
                        }
                    }
                }

                suggestions.Add(new CoachingSuggestion
                {
                    Title = missing.Operation,
                    Description = missing.Reason,
                    WhyNeeded = missing.Justification?.WhyNeeded ?? missing.Reason,
                    DegReference = missing.Justification?.DEGReference,
                    Category = "Knowledge Base",
                    Severity = missing.Priority.ToLowerInvariant() switch
                    {
                        "high" => CoachingSeverity.High,
                        "critical" => CoachingSeverity.Critical,
                        _ => CoachingSeverity.Medium
                    },
                    EstimatedCost = cost,
                    LaborHours = laborHours,
                    Source = "Knowledge Base"
                });
            }

            return suggestions;
        }

        /// <summary>
        /// Computes dollar cost from labor hours using GhostConfigService rates.
        /// Uses the same rates as Ghost Estimate for consistency.
        // Known body panels that should accumulate even without an explicit operation type.
        // These are real estimate parts, not catalog garbage.
        private static readonly HashSet<string> _knownBodyPanels = new(StringComparer.OrdinalIgnoreCase)
        {
            "hood", "fender", "front fender", "rear fender", "bumper", "bumper cover",
            "front bumper", "front bumper cover", "rear bumper", "rear bumper cover",
            "front door", "rear door", "door", "trunk lid", "decklid", "liftgate", "tailgate",
            "roof", "roof panel", "quarter panel", "rocker panel", "a pillar", "b pillar",
            "radiator support", "headlamp", "tail lamp", "windshield", "back glass",
            "fender liner", "door shell", "mirror", "grille", "header panel",
            "bumper reinforcement", "bumper absorber", "hood hinge", "door hinge"
        };

        /// <summary>
        /// No longer computes dollar costs from labor rates — the estimating system (CCC/Mitchell)
        /// handles pricing. We only show labor hours from our data sources (Excel tool, learned patterns).
        /// Returns 0 so no fake dollar amounts are displayed.
        /// </summary>
        private decimal ComputeCostFromHours(decimal laborHours, string operationName, string? operationType)
        {
            // No hardcoded prices — only show prices from Excel tool or uploaded estimates.
            // The estimating system (CCC/Mitchell) handles pricing.
            return 0;
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
            "mask", "cover", "tint", "prime", "block", "sand",
            "add", "additional", "cost", "adjust", "aim"
        };

        // Single-word hardware sub-parts from CCC parts catalog — NOT estimate line items
        private static readonly HashSet<string> _hardwareSubParts = new(StringComparer.OrdinalIgnoreCase)
        {
            "bolt", "nut", "rivet", "plug", "screw", "clip", "retainer", "bracket",
            "fastener", "washer", "spacer", "stud", "pin", "grommet"
        };

        /// <summary>
        /// Determines if a suggested operation is related to the triggering part.
        /// Prevents suggesting "Replace Roof" or "Replace Fender" just because a hood
        /// appeared on the same past estimates. Only allows:
        /// - Operations on the same part (hood → Refinish hood, backtape jambs for hood)
        /// - Sub-component operations (hood → hood hinge, hood strut, hood insulator)
        /// - Non-Replace operations that are generic labor (blend, refinish, R&I, repair)
        /// </summary>
        // Maps a part keyword to its CCC area — parts in the same area are related
        private static readonly Dictionary<string, string> _partAreaMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Front bumper area
            ["bumper cover"] = "front_bumper", ["bumper assy"] = "front_bumper", ["bumper absorber"] = "front_bumper",
            ["bumper reinforcement"] = "front_bumper", ["grille"] = "front_bumper", ["grill"] = "front_bumper",
            ["park sensor"] = "front_bumper", ["fog lamp"] = "front_bumper", ["fog light"] = "front_bumper",
            ["valance"] = "front_bumper", ["license plate"] = "front_bumper",
            // Rear bumper area
            ["rear bumper"] = "rear_bumper", ["step pad"] = "rear_bumper",
            // Front lamps
            ["headlamp"] = "front_lamps", ["headlight"] = "front_lamps",
            // Rear lamps
            ["tail lamp"] = "rear_lamps", ["taillight"] = "rear_lamps", ["tail light"] = "rear_lamps",
            // Hood area
            ["hood"] = "hood", ["hood hinge"] = "hood", ["hood strut"] = "hood", ["hood insulator"] = "hood",
            ["hood latch"] = "hood",
            // Fender area
            ["fender"] = "fender", ["fender liner"] = "fender", ["inner fender"] = "fender",
            ["wheel opening"] = "fender", ["splash shield"] = "fender",
            // Front door area
            ["front door"] = "front_door", ["door shell"] = "front_door", ["door trim"] = "front_door",
            ["door handle"] = "door", ["door check"] = "door", ["door hinge"] = "door",
            ["belt molding"] = "door", ["belt w'strip"] = "door", ["door w'strip"] = "door",
            ["surround w'strip"] = "door", ["water deflector"] = "door", ["door lock"] = "door",
            ["door edge"] = "door", ["door mirror"] = "door", ["mirror assy"] = "door",
            ["applique"] = "door", ["window molding"] = "door", ["corner molding"] = "door",
            // Rear door area
            ["rear door"] = "rear_door",
            // Quarter panel
            ["quarter panel"] = "quarter", ["quarter"] = "quarter",
            // Roof
            ["roof"] = "roof", ["roof panel"] = "roof", ["sunroof"] = "roof",
            // Trunk / liftgate
            ["trunk"] = "trunk", ["decklid"] = "trunk", ["liftgate"] = "trunk", ["tailgate"] = "trunk",
            // Rocker / pillars
            ["rocker"] = "rocker", ["a pillar"] = "rocker", ["b pillar"] = "rocker", ["c pillar"] = "rocker",
            ["rocker molding"] = "rocker",
        };

        private static string? GetPartArea(string partName)
        {
            var lower = partName.ToLowerInvariant();
            // Check longer keys first for specificity (e.g., "rear door" before "door")
            foreach (var kvp in _partAreaMap.OrderByDescending(k => k.Key.Length))
            {
                if (lower.Contains(kvp.Key))
                    return kvp.Value;
            }
            return null;
        }

        private static bool IsRelatedToTriggerPart(string triggerPart, string suggestedPart, string suggestedOpType)
        {
            var trigger = triggerPart.ToLowerInvariant().Trim();
            var suggested = suggestedPart.ToLowerInvariant().Trim();
            var opType = (suggestedOpType ?? "").ToLowerInvariant().Trim();

            // Same part or one contains the other
            if (suggested.Contains(trigger) || trigger.Contains(suggested))
                return true;

            // Check if they're in the same CCC part area
            var triggerArea = GetPartArea(trigger);
            var suggestedArea = GetPartArea(suggested);
            if (triggerArea != null && suggestedArea != null)
            {
                // Same area → related (e.g., rear door → door handle, belt molding, door trim)
                if (triggerArea == suggestedArea) return true;

                // "door" area items relate to both front_door and rear_door
                if (triggerArea is "front_door" or "rear_door" && suggestedArea == "door") return true;
                if (triggerArea == "door" && suggestedArea is "front_door" or "rear_door") return true;
            }

            // Generic labor descriptions apply to any triggering part
            var genericLabor = new[] {
                "backtape", "de-nib", "denib", "wet/dry", "rub-out", "buff",
                "edging", "cover car", "cover engine", "cover interior",
                "cavity wax", "seam sealer", "adhesive", "trial fit",
                "stage and secure", "mask", "sand", "prime", "clean",
                "three stage", "two tone", "add for", "edge guard",
                "sound deadener", "foam pad", "black out tape",
                "protector", "crash pad"
            };
            if (genericLabor.Any(g => suggested.Contains(g)))
                return true;

            // Replace/Repl of a DIFFERENT major part area = NOT related
            if (opType is "repl" or "replace" or "new")
                return false; // Contains check above already caught same-part replaces

            // Non-replace ops (R&I, Blend, Refinish, Repair) on unrelated parts → not related
            return false;
        }

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
                keywords.AddRange(new[] { "flex additive", "flex add", "flexible additive", "flex agent" });

            // Adhesion promoter
            if (titleLower.Contains("adhesion") || titleLower.Contains("promoter"))
                keywords.AddRange(new[] { "adhesion promoter", "adhesion", "ad pro", "adpro", "promoter" });

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

        // Common vehicle make names for validation
        private static readonly HashSet<string> _vehicleMakes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Acura", "Alfa", "Aston", "Audi", "BMW", "Bentley", "Buick", "Cadillac",
            "Chevrolet", "Chevy", "Chrysler", "Dodge", "Ferrari", "Fiat", "Ford",
            "Genesis", "GMC", "Honda", "Hyundai", "Infiniti", "Jaguar", "Jeep",
            "Kia", "Lamborghini", "Land", "Lexus", "Lincoln", "Lucid", "Maserati",
            "Mazda", "McLaren", "Mercedes", "Mercury", "Mini", "Mitsubishi",
            "Nissan", "Polestar", "Pontiac", "Porsche", "Ram", "Rivian", "Rolls",
            "Saturn", "Scion", "Smart", "Subaru", "Suzuki", "Tesla", "Toyota",
            "Volkswagen", "Volvo", "VW"
        };

        private static string? ExtractVehicleInfo(string rawText)
        {
            // Match patterns like "2024 Toyota Camry" or "2023 Honda Civic"
            // Validate that the second word is a known vehicle make to avoid false positives
            var matches = System.Text.RegularExpressions.Regex.Matches(
                rawText,
                @"((?:19|20)\d{2})\s+([A-Za-z]{3,})\s+([A-Za-z]{2,})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var make = match.Groups[2].Value;
                if (_vehicleMakes.Contains(make))
                    return match.Value.Trim();
            }
            return null;
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

            // CCC ONE new workfiles have title "Untitled" — treat as CCC
            // Also "New Workfile" patterns
            if (title == "untitled" || title.Contains("workfile") || title.Contains("new estimate"))
                return "CCC";

            // For non-estimating apps, use the first word as the app identity
            var firstWord = windowTitle.Split(new[] { ' ', '-', '–', '—', '|' }, StringSplitOptions.RemoveEmptyEntries);
            return firstWord.Length > 0 ? firstWord[0] : windowTitle;
        }

        /// <summary>
        /// Fuzzy VIN comparison — allows up to 2 character differences to handle OCR errors (O/0, I/1, etc.)
        /// </summary>
        private static bool VinsAreSimilar(string vin1, string vin2)
        {
            if (string.Equals(vin1, vin2, StringComparison.OrdinalIgnoreCase))
                return true;

            if (vin1.Length != vin2.Length)
                return false;

            int differences = 0;
            for (int i = 0; i < vin1.Length; i++)
            {
                if (!char.ToUpperInvariant(vin1[i]).Equals(char.ToUpperInvariant(vin2[i])))
                {
                    differences++;
                    if (differences > 2)
                        return false;
                }
            }
            return true;
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
            var titleLower = title.ToLowerInvariant();
            var catLower = category.ToLowerInvariant();

            // Normalize scan aliases so "Pre-Scan", "Pre-Repair Scan", "Pre Scan" all dedup
            // regardless of source category (SOP, KB, Scoring)
            if (titleLower.Contains("pre") && titleLower.Contains("scan"))
            { titleLower = "pre_scan"; catLower = "diagnostic"; }
            else if (titleLower.Contains("post") && titleLower.Contains("scan"))
            { titleLower = "post_scan"; catLower = "diagnostic"; }
            else if (titleLower.Contains("in-process") && titleLower.Contains("scan"))
            { titleLower = "in_process_scan"; catLower = "diagnostic"; }

            // Normalize battery disconnect/reconnect variants
            // "Disconnect/Reconnect Battery", "Battery Disconnect", "Battery Reconnect & Initialize" → same key
            if (titleLower.Contains("battery") && (titleLower.Contains("disconnect") || titleLower.Contains("reconnect")))
            { titleLower = "battery_disconnect_reconnect"; catLower = "electrical"; }

            // Normalize adhesion promoter variants
            if (titleLower.Contains("adhesion") && titleLower.Contains("promoter"))
            { titleLower = "adhesion_promoter"; catLower = "materials"; }

            // Normalize flex additive variants
            if (titleLower.Contains("flex") && (titleLower.Contains("additive") || titleLower.Contains("add")))
            { titleLower = "flex_additive"; catLower = "materials"; }

            // Use _ instead of stripping spaces — preserves word boundaries for IsFuzzyDuplicate
            var normalized = (catLower + "|" + titleLower)
                .Replace(" ", "_")
                .Replace("-", "_");
            return normalized;
        }

        private static bool IsFuzzyDuplicate(string key, HashSet<string> existingKeys)
        {
            // Extract title from key (category|title format)
            var keyParts = key.Split('|');
            var keyTitle = keyParts.Length >= 2 ? keyParts[1] : key;
            var keyWords = keyTitle.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 1).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var existing in existingKeys)
            {
                if (existing.Length == 0 || key.Length == 0) continue;

                var existingParts = existing.Split('|');
                if (existingParts.Length < 2) continue;

                var existingTitle = existingParts[1];

                // Same-category substring check (original logic)
                if (keyParts.Length >= 2 && keyParts[0] == existingParts[0])
                {
                    if (keyTitle.Contains(existingTitle) || existingTitle.Contains(keyTitle))
                        return true;

                    var maxLen = Math.Max(keyTitle.Length, existingTitle.Length);
                    if (maxLen > 3)
                    {
                        var commonChars = keyTitle.Intersect(existingTitle).Count();
                        if ((double)commonChars / maxLen >= 0.8)
                            return true;
                    }
                }

                // B3: Cross-category word overlap check (>70% word overlap = duplicate)
                if (keyWords.Count >= 2)
                {
                    var existingWords = existingTitle.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length > 1).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (existingWords.Count >= 2)
                    {
                        var overlap = keyWords.Count(w => existingWords.Contains(w));
                        var smaller = Math.Min(keyWords.Count, existingWords.Count);
                        if (smaller > 0 && (double)overlap / smaller > 0.7)
                            return true;
                    }
                }
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

        /// <summary>
        /// Use AI to detect and remove duplicate suggestions that string matching missed.
        /// Called once per coaching cycle, not in the hot-path IsFuzzyDuplicate.
        /// Returns the filtered list on success, original list on failure.
        /// </summary>
        private async Task<List<CoachingSuggestion>> TryAiDedupAsync(List<CoachingSuggestion> suggestions)
        {
            try
            {
                if (suggestions.Count < 3) return suggestions;

                var apiService = McStudDesktop.Services.ClaudeApiService.Instance;
                if (!McStudDesktop.Services.AiConfigService.Instance.IsFeatureEnabled(McStudDesktop.Services.AiFeature.SuggestionDedup))
                    return suggestions;

                // Find borderline pairs (similar titles that string matching kept)
                var pairs = new List<string>();
                for (int i = 0; i < suggestions.Count && pairs.Count < 20; i++)
                {
                    for (int j = i + 1; j < suggestions.Count && pairs.Count < 20; j++)
                    {
                        var a = suggestions[i].Title;
                        var b = suggestions[j].Title;
                        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) continue;
                        // Only send pairs with some word overlap (potential duplicates)
                        var wordsA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLower()).ToHashSet();
                        var wordsB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLower()).ToHashSet();
                        var overlap = wordsA.Count(w => wordsB.Contains(w));
                        if (overlap >= 1)
                            pairs.Add($"{i}:{j} | \"{a}\" vs \"{b}\"");
                    }
                }

                if (pairs.Count == 0) return suggestions;

                var systemPrompt = @"You are a collision repair coaching assistant. Identify which suggestion pairs are duplicates (same underlying advice, different wording).

Return a JSON array of index pairs to remove (the second item in each pair). Format: [3, 7, 12] — these are the 0-based indices of suggestions to remove.
If no duplicates, return an empty array: []
Return ONLY the JSON array.";

                var userMessage = $"Suggestions:\n{string.Join("\n", suggestions.Select((s, i) => $"[{i}] {s.Title}: {s.Description}"))}\n\nPotential duplicate pairs:\n{string.Join("\n", pairs)}";

                if (userMessage.Length > 3000)
                    userMessage = userMessage.Substring(0, 3000);

                var response = await apiService.SendAsync(systemPrompt, userMessage, McStudDesktop.Services.AiFeature.SuggestionDedup, 256);
                if (response == null) return suggestions;

                var text = McStudDesktop.Services.ClaudeApiService.StripCodeFences(response.Text);

                var indicesToRemove = System.Text.Json.JsonSerializer.Deserialize<List<int>>(text);
                if (indicesToRemove == null || indicesToRemove.Count == 0) return suggestions;

                var removeSet = new HashSet<int>(indicesToRemove);
                var filtered = suggestions.Where((s, i) => !removeSet.Contains(i)).ToList();

                Debug.WriteLine($"[LiveCoaching] AI dedup removed {suggestions.Count - filtered.Count} duplicates");
                return filtered;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiveCoaching] AI dedup failed (using fallback): {ex.Message}");
                return suggestions;
            }
        }
    }
}
