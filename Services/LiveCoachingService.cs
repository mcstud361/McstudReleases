#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

            // Cross-check suggestions against ONLY structured ops — NOT raw OCR text.
            // Raw text includes sidebar navigation and diagram labels that cause false confirmations,
            // then items flip back to "missing" when user scrolls and the sidebar text changes.
            var structuredOpsText = string.Join(" | ", _accumulatedOps.Values
                .Select(op => $"{op.Description} {op.PartName} {op.OperationType} {op.RawLine}"))
                .ToLowerInvariant();
            var combinedSeenText = structuredOpsText;

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
        /// </summary>
        private static List<CoachingSuggestion> GetSOPListSuggestions()
        {
            return new List<CoachingSuggestion>
            {
                new() { Title = "Battery Disconnect/Reconnect", Description = "Disconnect and reconnect battery for safe electrical work.", WhyNeeded = "Required for any repair involving electrical components or welding.", Category = "SOP List", Severity = CoachingSeverity.High, Source = "SOP List", EstimatedCost = 18m },
                new() { Title = "Pre-Repair Scan", Description = "Diagnostic scan before repairs begin.", WhyNeeded = "Documents pre-existing DTCs and establishes baseline for post-scan comparison.", Category = "SOP List", Severity = CoachingSeverity.Critical, Source = "SOP List", EstimatedCost = 40m },
                new() { Title = "Post-Repair Scan", Description = "Diagnostic scan after all repairs completed.", WhyNeeded = "Verifies all DTCs are cleared and no new codes introduced by repair.", Category = "SOP List", Severity = CoachingSeverity.Critical, Source = "SOP List", EstimatedCost = 40m },
                new() { Title = "Setup Scan Tool", Description = "Setup and configure diagnostic scan tool.", WhyNeeded = "Scan tool setup time is a billable operation per DEG guidelines.", Category = "SOP List", Severity = CoachingSeverity.Medium, Source = "SOP List", EstimatedCost = 25m },
                new() { Title = "ADAS Diagnostics", Description = "Include ADAS systems in diagnostic scan.", WhyNeeded = "ADAS-equipped vehicles require separate diagnostic procedures.", Category = "SOP List", Severity = CoachingSeverity.High, Source = "SOP List", EstimatedCost = 50m },
                new() { Title = "Simulate Full Fluids", Description = "Simulate full fluid levels for accurate diagnostics.", WhyNeeded = "Some sensors require proper fluid levels to report correctly during scan.", Category = "SOP List", Severity = CoachingSeverity.Medium, Source = "SOP List", EstimatedCost = 15m },
                new() { Title = "Adjust Tire Pressure", Description = "Adjust tire pressure for diagnostics and ADAS calibration.", WhyNeeded = "Incorrect tire pressure can affect TPMS readings and ADAS calibration accuracy.", Category = "SOP List", Severity = CoachingSeverity.Medium, Source = "SOP List", EstimatedCost = 12m },
                new() { Title = "Drive Cycle", Description = "Perform drive cycle after repairs.", WhyNeeded = "Required to verify repair completion and clear certain adaptive DTCs.", Category = "SOP List", Severity = CoachingSeverity.Medium, Source = "SOP List", EstimatedCost = 30m },
                new() { Title = "Pre-Wash", Description = "Pre-wash vehicle before repair work.", WhyNeeded = "Ensures clean work surface and prevents contamination of repair areas.", Category = "SOP List", Severity = CoachingSeverity.Medium, Source = "SOP List", EstimatedCost = 20m },
                new() { Title = "Clean for Delivery", Description = "Final cleaning of vehicle before customer delivery.", WhyNeeded = "Professional delivery standard — removes dust, overspray, and repair debris.", Category = "SOP List", Severity = CoachingSeverity.Medium, Source = "SOP List", EstimatedCost = 25m },
            };
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

                // Query direct operations for this part
                var queryResult = learningService.QueryOperationsForPart(op.PartName, op.OperationType);
                foreach (var suggested in queryResult.SuggestedOperations)
                {
                    if (suggested.Confidence < 0.4 || suggested.TimesUsed < 2) continue;
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

                // Query co-occurring operations
                var related = learningService.GetRelatedOperations(op.PartName, op.OperationType ?? "Replace");
                foreach (var coOp in related)
                {
                    if (coOp.TimesSeenTogether < 2) continue;
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

            return suggestions;
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

            // Scan-related
            if (titleLower.Contains("pre") && (titleLower.Contains("scan") || titleLower.Contains("repair")))
                keywords.AddRange(new[] { "pre-scan", "pre scan", "pre-repair scan", "prescan", "pre repair scan" });
            if (titleLower.Contains("post") && (titleLower.Contains("scan") || titleLower.Contains("repair")))
                keywords.AddRange(new[] { "post-scan", "post scan", "post-repair scan", "postscan", "post repair scan" });

            // Calibration
            if (titleLower.Contains("calibrat") || categoryLower == "calibration")
                keywords.AddRange(new[] { "calibration", "calibrate", "adas calibration", "adas calib" });
            if (titleLower.Contains("static calibration"))
                keywords.Add("static calibration");
            if (titleLower.Contains("dynamic calibration"))
                keywords.Add("dynamic calibration");

            // Blend
            if (titleLower.Contains("blend"))
                keywords.AddRange(new[] { "blend", "blending" });

            // Corrosion
            if (titleLower.Contains("corrosion"))
                keywords.AddRange(new[] { "corrosion", "anti-corrosion", "corrosion protection", "cavity wax", "weld-thru primer", "weld thru primer" });

            // Flex additive
            if (titleLower.Contains("flex"))
                keywords.AddRange(new[] { "flex additive", "flex add", "flexible additive" });

            // Battery
            if (titleLower.Contains("battery") || titleLower.Contains("disconnect"))
                keywords.AddRange(new[] { "disconnect battery", "reconnect battery", "d/c battery", "battery disconnect" });

            // R&I
            if (titleLower.Contains("r&i") || titleLower.Contains("remove") || titleLower.Contains("reinstall"))
                keywords.AddRange(new[] { "r&i", "r & i", "remove & install", "remove and install", "remove/install" });

            // Diagnostic / scan tool
            if (titleLower.Contains("diagnostic") || titleLower.Contains("scan tool"))
                keywords.AddRange(new[] { "diagnostic", "scan tool", "diagnostic scan" });

            // Drive cycle
            if (titleLower.Contains("drive cycle"))
                keywords.AddRange(new[] { "drive cycle", "test drive" });

            // OEM
            if (titleLower.Contains("oem"))
                keywords.AddRange(new[] { "oem research", "oem procedure", "oem position" });

            // Clean for delivery
            if (titleLower.Contains("clean") && titleLower.Contains("delivery"))
                keywords.AddRange(new[] { "clean for delivery", "final clean" });

            // Hazardous waste
            if (titleLower.Contains("hazardous") || titleLower.Contains("hazmat"))
                keywords.AddRange(new[] { "hazardous waste", "haz waste", "hazmat" });

            // Memory saver
            if (titleLower.Contains("memory saver") || titleLower.Contains("ks-100"))
                keywords.AddRange(new[] { "memory saver", "ks-100", "battery support", "keep alive", "keep-alive" });

            // Module programming
            if (titleLower.Contains("module") || titleLower.Contains("programming") || titleLower.Contains("initialization"))
                keywords.AddRange(new[] { "module programming", "reprogram", "initialization", "initialize", "relearn", "idle relearn", "window relearn" });

            // Drive cycle (broader matching for the new item title)
            if (titleLower.Contains("drive") || titleLower.Contains("test drive"))
                keywords.AddRange(new[] { "drive cycle", "test drive", "road test" });

            // OEM research (broader matching)
            if (titleLower.Contains("oem") || titleLower.Contains("repair procedure"))
                keywords.AddRange(new[] { "oem research", "oem procedure", "oem position", "repair procedure", "oem repair" });

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
