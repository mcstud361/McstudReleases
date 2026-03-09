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
        private readonly HashSet<string> _accumulatedParts = new(StringComparer.OrdinalIgnoreCase);
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
            _accumulatedParts.Clear();
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
            // If the source window changed, reset accumulated state (different estimate)
            var sourceWindow = result.SourceWindow ?? "";
            if (!string.IsNullOrEmpty(sourceWindow) && !sourceWindow.Equals(_lastSourceWindow, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(_lastSourceWindow))
                {
                    Debug.WriteLine($"[LiveCoaching] Window changed from \"{_lastSourceWindow}\" to \"{sourceWindow}\" — resetting accumulated data");
                    _accumulatedOps.Clear();
                    _accumulatedParts.Clear();
                    _accumulatedRawText.Clear();
                    _vehicleInfo = null;
                    _customerName = null;
                }
                _lastSourceWindow = sourceWindow;
            }

            // Accumulate structured operations from this capture
            if (result.DetectedOperations != null)
            {
                foreach (var op in result.DetectedOperations)
                {
                    // Key by description+partName+opType to avoid duplicates
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

            // Accumulate raw text and part names
            if (!string.IsNullOrWhiteSpace(result.RawText))
            {
                _accumulatedRawText.AppendLine(result.RawText);

                var detectedParts = ScanPartsFromText(result.RawText);
                foreach (var partName in detectedParts)
                    _accumulatedParts.Add(partName);

                // Extract vehicle and customer info if not found yet
                if (_vehicleInfo == null)
                    _vehicleInfo = ExtractVehicleInfo(result.RawText);
                if (_customerName == null)
                    _customerName = ExtractCustomerName(result.RawText);
            }

            // Build the full picture from accumulated data
            var parsedLines = new List<ParsedEstimateLine>(_accumulatedOps.Values);

            // Add synthetic lines for parts found in text that aren't in structured ops
            var structuredPartNames = new HashSet<string>(
                parsedLines.Select(p => p.PartName),
                StringComparer.OrdinalIgnoreCase);

            foreach (var partName in _accumulatedParts)
            {
                if (structuredPartNames.Contains(partName)) continue;
                parsedLines.Add(new ParsedEstimateLine
                {
                    Description = partName,
                    PartName = partName,
                    OperationType = "Replace",
                    RawLine = partName
                });
            }

            if (parsedLines.Count == 0)
            {
                Debug.WriteLine("[LiveCoaching] No operations or parts accumulated yet");
                return;
            }

            Debug.WriteLine($"[LiveCoaching] Scoring with {parsedLines.Count} accumulated lines ({_accumulatedOps.Count} structured + {_accumulatedParts.Count} text-scanned parts)");

            // Run both scoring engines against the FULL accumulated picture
            var scoringResult = _scoringService.ScoreEstimate(parsedLines);
            var analysisResult = _analyzerService.AnalyzeEstimate(parsedLines);

            // Merge + deduplicate into CoachingSuggestions
            var suggestions = new List<CoachingSuggestion>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Process scoring issues
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

            // Process smart analyzer suggestions
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

            // Cross-check suggestions against accumulated raw text —
            // if the suggestion title/keywords are found in what we've already seen, it's confirmed on the estimate
            var allSeenText = _accumulatedRawText.ToString().ToLowerInvariant();
            foreach (var s in suggestions)
            {
                s.IsConfirmedOnEstimate = IsSuggestionOnEstimate(s, allSeenText);
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

            Debug.WriteLine($"[LiveCoaching] Snapshot: score={snapshot.Score}, grade={snapshot.Grade}, suggestions={suggestions.Count}");
            SuggestionsUpdated?.Invoke(this, snapshot);
        }

        /// <summary>
        /// Scans raw OCR text for known part names (same patterns as ScreenMonitorPanel).
        /// </summary>
        private static List<string> ScanPartsFromText(string rawText)
        {
            var lowerText = rawText.ToLowerInvariant();
            var parts = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (pattern, canonical) in _scanParts)
            {
                if (seen.Contains(canonical)) continue;

                var idx = lowerText.IndexOf(pattern);
                if (idx < 0) continue;

                // Word boundary check
                if (idx > 0 && char.IsLetter(lowerText[idx - 1])) continue;
                var endIdx = idx + pattern.Length;
                if (endIdx < lowerText.Length && char.IsLetter(lowerText[endIdx])) continue;

                seen.Add(canonical);
                parts.Add(canonical);
            }

            return parts;
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
                keywords.AddRange(new[] { "memory saver", "ks-100", "battery support" });

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

        // Same part patterns as ScreenMonitorPanel — longer/more-specific first
        private static readonly (string Pattern, string CanonicalName)[] _scanParts = new[]
        {
            ("front bumper", "Front Bumper"), ("rear bumper", "Rear Bumper"),
            ("bumper reinforcement", "Bumper Reinforcement"), ("bumper absorber", "Bumper Absorber"),
            ("bumper fascia", "Bumper Cover"), ("bumper cover", "Bumper Cover"),
            ("fender liner", "Fender Liner"), ("inner fender", "Fender Liner"),
            ("front fender", "Fender"), ("fender", "Fender"),
            ("front door", "Front Door"), ("rear door", "Rear Door"),
            ("door handle", "Door Handle"), ("door trim", "Door Trim Panel"),
            ("quarter panel", "Quarter Panel"), ("qtr panel", "Quarter Panel"),
            ("rocker panel", "Rocker Panel"), ("side panel", "Side Panel"),
            ("roof panel", "Roof"), ("trunk lid", "Trunk Lid"), ("decklid", "Trunk Lid"),
            ("liftgate", "Liftgate"), ("tailgate", "Tailgate"),
            ("hood", "Hood"),
            ("headlamp", "Headlight"), ("headlight", "Headlight"), ("head lamp", "Headlight"),
            ("tail lamp", "Tail Light"), ("taillight", "Tail Light"), ("tail light", "Tail Light"),
            ("fog lamp", "Fog Light"), ("fog light", "Fog Light"),
            ("grille", "Grille"), ("grill", "Grille"),
            ("radiator support", "Radiator Support"), ("rad support", "Radiator Support"),
            ("energy absorber", "Bumper Absorber"),
            ("windshield", "Windshield"), ("back glass", "Rear Glass"),
            ("side mirror", "Mirror"), ("outside mirror", "Mirror"),
            ("parking sensor", "Parking Sensor"), ("backup camera", "Backup Camera"),
            ("splash shield", "Splash Shield"), ("wheel opening", "Wheel Opening Molding"),
            ("molding", "Molding"), ("spoiler", "Spoiler"), ("valance", "Valance"),
            ("a pillar", "A-Pillar"), ("b pillar", "B-Pillar"), ("c pillar", "C-Pillar"),
            ("radiator", "Radiator"), ("condenser", "Condenser"),
            ("control arm", "Control Arm"), ("strut", "Strut"), ("suspension", "Suspension"),
            ("wheel", "Wheel"), ("tire", "Tire"),
            ("adas", "ADAS Calibration"), ("calibration", "Calibration"),
            ("pre scan", "Pre-Repair Scan"), ("post scan", "Post-Repair Scan"),
            ("diagnostic", "Diagnostic Scan")
        };

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
