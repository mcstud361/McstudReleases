#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using McstudDesktop.Models;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Orchestrator for AI-driven estimating automation.
    /// Connects AI intent parsing → adapter execution → OCR verification.
    /// </summary>
    public class AiAutomationService
    {
        private static AiAutomationService? _instance;
        public static AiAutomationService Instance => _instance ??= new AiAutomationService();

        private readonly List<IEstimatingSystemAdapter> _adapters = new();
        private IEstimatingSystemAdapter? _currentAdapter;

        private static readonly string[] KnownOperations = new[]
        {
            "Replace", "Repair", "R&I", "Refinish", "Blend",
            "Add", "O/H", "Sublet", "Mech", "Body"
        };

        public IEstimatingSystemAdapter? CurrentAdapter => _currentAdapter;

        public AiAutomationService()
        {
            // Register adapters in priority order
            _adapters.Add(new CccDesktopAdapter());
            _adapters.Add(new CccWebAdapter());
            _adapters.Add(new MitchellAdapter());
        }

        /// <summary>
        /// Detect which estimating system is running and connect to it.
        /// Returns the connected adapter, or null if none found.
        /// </summary>
        public async Task<IEstimatingSystemAdapter?> DetectSystemAsync(CancellationToken ct = default)
        {
            foreach (var adapter in _adapters)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    if (await adapter.ConnectAsync(ct))
                    {
                        _currentAdapter = adapter;
                        Debug.WriteLine($"[AiAutomation] Connected to {adapter.SystemName}");
                        return adapter;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AiAutomation] Error trying {adapter.SystemName}: {ex.Message}");
                }
            }

            _currentAdapter = null;
            return null;
        }

        /// <summary>
        /// Parse a natural language command into a structured AutomationIntent using Claude AI.
        /// Falls back to rule-based parsing if AI is unavailable.
        /// </summary>
        public async Task<AutomationIntent?> ParseIntentAsync(string command, CancellationToken ct = default)
        {
            var intent = await TryAiIntentParsingAsync(command, ct);

            // Fall back to rule-based parsing
            if (intent == null)
                intent = ParseIntentRuleBased(command);

            if (intent != null)
                intent.RawCommand = command;

            return intent;
        }

        /// <summary>
        /// Build a deterministic execution plan from an intent.
        /// </summary>
        public AutomationPlan PlanActions(AutomationIntent intent, IEstimatingSystemAdapter adapter)
        {
            var plan = new AutomationPlan
            {
                Intent = intent,
                SystemName = adapter.SystemName,
            };

            int stepNum = 1;

            // Step 1: Focus the estimating window
            plan.Steps.Add(new AutomationStep
            {
                StepNumber = stepNum++,
                Action = "focus",
                Description = $"Focus {adapter.SystemName} window"
            });

            foreach (var op in intent.Operations)
            {
                // Insert new line
                plan.Steps.Add(new AutomationStep
                {
                    StepNumber = stepNum++,
                    Action = "insert_line",
                    Description = "Insert new line"
                });

                // Type part name
                plan.Steps.Add(new AutomationStep
                {
                    StepNumber = stepNum++,
                    Action = "type_field",
                    Description = $"Type part: {op.Part}",
                    Value = op.Part
                });

                // Tab to operation type
                plan.Steps.Add(new AutomationStep
                {
                    StepNumber = stepNum++,
                    Action = "tab",
                    Description = "Tab to operation field"
                });

                // Type operation type
                plan.Steps.Add(new AutomationStep
                {
                    StepNumber = stepNum++,
                    Action = "type_field",
                    Description = $"Type operation: {op.OperationType}",
                    Value = op.OperationType
                });

                // If hours specified, tab and type hours
                if (op.Hours.HasValue)
                {
                    plan.Steps.Add(new AutomationStep
                    {
                        StepNumber = stepNum++,
                        Action = "tab",
                        Description = "Tab to hours field"
                    });

                    plan.Steps.Add(new AutomationStep
                    {
                        StepNumber = stepNum++,
                        Action = "type_field",
                        Description = $"Type hours: {op.Hours.Value}",
                        Value = op.Hours.Value.ToString("0.0")
                    });
                }

                // Confirm the line
                plan.Steps.Add(new AutomationStep
                {
                    StepNumber = stepNum++,
                    Action = "enter",
                    Description = "Confirm line entry"
                });
            }

            // OCR verification step
            plan.Steps.Add(new AutomationStep
            {
                StepNumber = stepNum++,
                Action = "verify",
                Description = "OCR verify operations were added"
            });

            // Build summary
            var opSummaries = intent.Operations.Select(o =>
            {
                var hrs = o.Hours.HasValue ? $" ({o.Hours.Value}h)" : "";
                return $"{o.OperationType} {o.Part}{hrs}";
            });
            plan.Summary = $"Add {string.Join(", ", opSummaries)} in {adapter.SystemName}";

            return plan;
        }

        /// <summary>
        /// Execute an automation plan step by step.
        /// Reports progress and supports cancellation.
        /// </summary>
        public async Task<AutomationResult> ExecuteAsync(
            AutomationPlan plan, IEstimatingSystemAdapter adapter,
            IProgress<AutomationProgress>? progress = null,
            CancellationToken ct = default)
        {
            var result = new AutomationResult { TotalSteps = plan.Steps.Count };
            int delayBetweenSteps = 100; // ms

            for (int i = 0; i < plan.Steps.Count; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    result.Message = $"Cancelled at step {i + 1} of {plan.Steps.Count}";
                    return result;
                }

                var step = plan.Steps[i];
                progress?.Report(new AutomationProgress
                {
                    CurrentStep = i + 1,
                    TotalSteps = plan.Steps.Count,
                    Description = step.Description
                });

                bool stepSuccess = false;

                try
                {
                    stepSuccess = step.Action switch
                    {
                        "focus" => await adapter.FocusEstimatingWindowAsync(ct),
                        "insert_line" => await adapter.InsertNewLineAsync(ct),
                        "type_field" => await adapter.TypeInFieldAsync(step.Value ?? "", ct),
                        "tab" => await adapter.TabToNextFieldAsync(ct),
                        "enter" => await adapter.PressEnterAsync(ct),
                        "escape" => await adapter.PressEscapeAsync(ct),
                        "click" => await adapter.ClickElementAsync(step.Value ?? "", ct),
                        "verify" => await VerifyWithOcrAsync(plan, adapter, result, ct),
                        _ => false
                    };
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AiAutomation] Step {i + 1} error: {ex.Message}");
                    stepSuccess = false;
                }

                if (!stepSuccess && step.Action != "verify")
                {
                    result.Message = $"Failed at step {i + 1}: {step.Description}";
                    result.StepsCompleted = i;
                    return result;
                }

                result.StepsCompleted = i + 1;

                // Small delay between steps for UI responsiveness
                if (step.Action != "verify" && i < plan.Steps.Count - 1)
                    await Task.Delay(delayBetweenSteps, ct);
            }

            result.Success = true;
            if (string.IsNullOrEmpty(result.Message))
                result.Message = $"Successfully completed: {plan.Summary}";

            return result;
        }

        /// <summary>
        /// Agent loop for complex multi-step commands (Phase 2 — stubbed).
        /// </summary>
        public Task<AutomationResult> RunAgentLoopAsync(
            string goal, IEstimatingSystemAdapter adapter,
            int maxSteps = 10, CancellationToken ct = default)
        {
            // Phase 2: multi-turn AI loop with OCR feedback
            return Task.FromResult(new AutomationResult
            {
                Success = false,
                Message = "Agent loop not yet implemented. Use simple commands like 'replace the hood'."
            });
        }

        #region Private Methods

        private async Task<AutomationIntent?> TryAiIntentParsingAsync(string command, CancellationToken ct)
        {
            try
            {
                if (!AiConfigService.Instance.IsFeatureEnabled(AiFeature.Automation))
                    return null;

                var sanitized = ClaudeApiService.SanitizeSensitiveData(command);

                var systemPrompt = @"You are an auto body estimating assistant. Parse the user's command into a JSON object describing what operations to add to the estimate.

Known operation types: Replace, Repair, R&I, Refinish, Blend, Add, O/H, Sublet, Mech, Body

Respond ONLY with valid JSON, no explanation:
{
  ""intentType"": ""add_operation"",
  ""operations"": [
    { ""part"": ""hood"", ""operationType"": ""Replace"", ""hours"": null }
  ],
  ""confidence"": 0.95,
  ""explanation"": ""Add Replace operation for hood""
}

Rules:
- ""part"" should be the common part name (e.g., ""hood"", ""fender"", ""bumper cover"", ""door"")
- ""operationType"" must be one of the known types above
- ""hours"" is null unless the user specifies hours (e.g., ""repair the hood 2.5 hours"")
- ""confidence"" is 0.0-1.0 indicating how confident you are in the parsing
- If the user mentions multiple parts, return multiple operations
- For ""R&I"" accept variations like ""remove"", ""r and i"", ""remove and install""";

                var response = await ClaudeApiService.Instance.SendAsync(
                    systemPrompt, sanitized, AiFeature.Automation, 512, ct);

                if (response == null) return null;

                var json = ClaudeApiService.StripCodeFences(response.Text);
                var parsed = JsonSerializer.Deserialize<AiIntentResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed == null) return null;

                var intent = new AutomationIntent
                {
                    IntentType = parsed.IntentType ?? "add_operation",
                    Confidence = parsed.Confidence,
                    Explanation = parsed.Explanation ?? ""
                };

                foreach (var op in parsed.Operations ?? new())
                {
                    intent.Operations.Add(new AutomationOperation
                    {
                        Part = op.Part ?? "",
                        OperationType = NormalizeOperationType(op.OperationType ?? ""),
                        Hours = op.Hours
                    });
                }

                if (intent.Operations.Count == 0) return null;

                Debug.WriteLine($"[AiAutomation] AI parsed intent: {intent.Explanation} (confidence: {intent.Confidence})");
                return intent;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiAutomation] AI intent parsing error: {ex.Message}");
                return null;
            }
        }

        private AutomationIntent? ParseIntentRuleBased(string command)
        {
            var lower = command.Trim().ToLowerInvariant();
            var intent = new AutomationIntent { IntentType = "add_operation" };

            // Try to match "<operation> the <part>" or "<operation> <part>"
            foreach (var opType in KnownOperations)
            {
                var opLower = opType.ToLowerInvariant();
                var patterns = new[]
                {
                    $"{opLower} the ",
                    $"{opLower} ",
                    $"add {opLower} ",
                    $"enter {opLower} ",
                    $"insert {opLower} "
                };

                foreach (var pattern in patterns)
                {
                    var idx = lower.IndexOf(pattern, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        var remaining = command.Substring(idx + pattern.Length).Trim();

                        // Extract hours if present (e.g., "hood 2.5 hours" or "hood 2.5h")
                        decimal? hours = null;
                        var hoursMatch = System.Text.RegularExpressions.Regex.Match(
                            remaining, @"(\d+\.?\d*)\s*(hours?|hrs?|h)\b",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        string partName;
                        if (hoursMatch.Success)
                        {
                            hours = decimal.Parse(hoursMatch.Groups[1].Value);
                            partName = remaining.Substring(0, hoursMatch.Index).Trim();
                        }
                        else
                        {
                            partName = remaining.Trim();
                        }

                        // Clean up part name
                        partName = partName.TrimEnd(',', '.', '!');

                        if (!string.IsNullOrWhiteSpace(partName))
                        {
                            intent.Operations.Add(new AutomationOperation
                            {
                                Part = partName,
                                OperationType = opType,
                                Hours = hours
                            });
                            intent.Confidence = 0.7; // Rule-based is lower confidence
                            intent.Explanation = $"Add {opType} for {partName}";
                            return intent;
                        }
                    }
                }
            }

            // Handle "r and i" / "remove" variations
            if (lower.Contains("r and i ") || lower.Contains("remove and install ") || lower.StartsWith("remove the "))
            {
                var part = ExtractPartAfterKeyword(lower, command, new[] { "r and i the ", "r and i ", "remove and install the ", "remove and install ", "remove the " });
                if (!string.IsNullOrWhiteSpace(part))
                {
                    intent.Operations.Add(new AutomationOperation
                    {
                        Part = part,
                        OperationType = "R&I"
                    });
                    intent.Confidence = 0.7;
                    intent.Explanation = $"Add R&I for {part}";
                    return intent;
                }
            }

            return null;
        }

        private string? ExtractPartAfterKeyword(string lower, string original, string[] keywords)
        {
            foreach (var kw in keywords)
            {
                var idx = lower.IndexOf(kw, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var part = original.Substring(idx + kw.Length).Trim().TrimEnd(',', '.', '!');
                    if (!string.IsNullOrWhiteSpace(part)) return part;
                }
            }
            return null;
        }

        private string NormalizeOperationType(string opType)
        {
            var lower = opType.ToLowerInvariant().Trim();
            return lower switch
            {
                "replace" => "Replace",
                "repair" => "Repair",
                "r&i" or "r and i" or "remove" or "remove and install" => "R&I",
                "refinish" => "Refinish",
                "blend" => "Blend",
                "add" => "Add",
                "o/h" or "overhaul" => "O/H",
                "sublet" => "Sublet",
                "mech" or "mechanical" => "Mech",
                "body" => "Body",
                _ => opType // Return as-is if not recognized
            };
        }

        private async Task<bool> VerifyWithOcrAsync(
            AutomationPlan plan, IEstimatingSystemAdapter adapter,
            AutomationResult result, CancellationToken ct)
        {
            try
            {
                // Brief delay for UI to update
                await Task.Delay(500, ct);

                var ocrResult = await adapter.ReadCurrentScreenAsync(ct);
                if (ocrResult == null)
                {
                    result.OcrVerified = false;
                    result.OcrVerificationDetail = "Could not capture screen for verification";
                    return true; // Don't fail the whole plan for verification issues
                }

                // Check if any of the planned operations appear in the OCR text
                var rawText = ocrResult.RawText.ToLowerInvariant();
                int verified = 0;

                foreach (var op in plan.Intent.Operations)
                {
                    if (rawText.Contains(op.Part.ToLowerInvariant()))
                        verified++;
                }

                result.OcrVerified = verified > 0;
                result.OcrVerificationDetail = verified == plan.Intent.Operations.Count
                    ? $"All {verified} operation(s) verified on screen"
                    : verified > 0
                        ? $"{verified} of {plan.Intent.Operations.Count} operation(s) found on screen"
                        : "Operations not yet visible on screen (may need to scroll)";

                return true; // Verification is informational, never fails the plan
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiAutomation] OCR verify error: {ex.Message}");
                result.OcrVerified = false;
                result.OcrVerificationDetail = "Verification error";
                return true;
            }
        }

        #endregion

        #region AI Response Model

        private class AiIntentResponse
        {
            public string? IntentType { get; set; }
            public List<AiOperationResponse>? Operations { get; set; }
            public double Confidence { get; set; }
            public string? Explanation { get; set; }
        }

        private class AiOperationResponse
        {
            public string? Part { get; set; }
            public string? OperationType { get; set; }
            public decimal? Hours { get; set; }
        }

        #endregion
    }
}
