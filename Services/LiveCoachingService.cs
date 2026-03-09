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
            // Build parsed lines from structured operations
            var parsedLines = new List<ParsedEstimateLine>();

            if (result.DetectedOperations != null)
            {
                foreach (var op in result.DetectedOperations)
                {
                    parsedLines.Add(new ParsedEstimateLine
                    {
                        RawLine = op.RawLine,
                        Description = op.Description,
                        PartName = op.PartName,
                        OperationType = op.OperationType,
                        LaborHours = op.LaborHours,
                        RefinishHours = op.RefinishHours,
                        Price = op.Price,
                        Quantity = op.Quantity
                    });
                }
            }

            // If no structured ops but we have raw text, build synthetic lines
            // from part name scanning (same approach as ScreenMonitorPanel.DetectParts)
            if (parsedLines.Count == 0 && !string.IsNullOrWhiteSpace(result.RawText))
            {
                var detectedParts = ScanPartsFromText(result.RawText);
                foreach (var partName in detectedParts)
                {
                    parsedLines.Add(new ParsedEstimateLine
                    {
                        Description = partName,
                        PartName = partName,
                        OperationType = "Replace",
                        RawLine = partName
                    });
                }
                Debug.WriteLine($"[LiveCoaching] Built {parsedLines.Count} synthetic lines from raw text");
            }

            if (parsedLines.Count == 0)
            {
                Debug.WriteLine("[LiveCoaching] No operations or parts found in OCR result");
                return;
            }

            // Run both scoring engines
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

            // Apply dismissals
            lock (_lock)
            {
                foreach (var s in suggestions)
                {
                    if (_dismissedIds.Contains(s.Id))
                        s.IsDismissed = true;
                }
            }

            var opCount = result.DetectedOperations?.Count ?? 0;
            var snapshot = new CoachingSnapshot
            {
                Suggestions = suggestions,
                TotalOperationsDetected = opCount > 0 ? opCount : parsedLines.Count,
                Score = scoringResult.OverallScore,
                Grade = scoringResult.Grade,
                PotentialRecovery = scoringResult.PotentialCostRecovery + scoringResult.PotentialLaborRecovery,
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
