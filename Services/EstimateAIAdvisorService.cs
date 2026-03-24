#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services;

/// <summary>
/// Unified AI Advisor that orchestrates all existing AI/learning services
/// to provide composite intelligence: research prior estimates, learn and adapt,
/// and proactively suggest/recommend based on real data.
/// </summary>
public class EstimateAIAdvisorService
{
    private static EstimateAIAdvisorService? _instance;
    public static EstimateAIAdvisorService Instance => _instance ??= new EstimateAIAdvisorService();

    private readonly EstimateHistoryDatabase _historyDb;
    private readonly EstimateLearningService _learningService;
    private readonly SmartSuggestionService _suggestionService;
    private readonly EstimateScoringService _scoringService;
    private readonly EstimateAccuracyService _accuracyService;
    private readonly EstimateQueryService _queryService;
    private readonly PatternIntelligenceService _intelligenceService;
    private readonly LearningFeedbackService _feedbackService;

    private AdvisorSessionContext _session = new();

    public EstimateAIAdvisorService()
    {
        _historyDb = EstimateHistoryDatabase.Instance;
        _learningService = EstimateLearningService.Instance;
        _suggestionService = SmartSuggestionService.Instance;
        _scoringService = EstimateScoringService.Instance;
        _accuracyService = EstimateAccuracyService.Instance;
        _queryService = EstimateQueryService.Instance;
        _intelligenceService = PatternIntelligenceService.Instance;
        _feedbackService = LearningFeedbackService.Instance;
    }

    #region Session Context

    public void SetSessionContext(string? vehicleInfo, string? insuranceCompany)
    {
        _session.VehicleInfo = vehicleInfo;
        _session.InsuranceCompany = insuranceCompany;
        if (!string.IsNullOrEmpty(vehicleInfo))
            _session.VehicleType = _intelligenceService.ClassifyVehicleType(vehicleInfo);
    }

    public void ClearSession()
    {
        _session = new AdvisorSessionContext();
    }

    /// <summary>
    /// Returns a snapshot of the current session context for use by EstimateContextService
    /// </summary>
    public AdvisorSessionContext GetSessionSnapshot()
    {
        return _session;
    }

    public void TrackEnteredOperation(string part, string operation, decimal hours)
    {
        _session.EnteredOperations.Add(new EnteredOp { Part = part, Operation = operation, Hours = hours });
    }

    #endregion

    #region Intent Classification & Routing

    /// <summary>
    /// Main router. Classifies user intent and delegates to the appropriate advisor pipeline.
    /// Returns null if the input is not an advisor query.
    /// </summary>
    public AdvisorResponse? ProcessAdvisorQuery(string userInput)
    {
        var intent = ClassifyIntent(userInput);
        if (intent == AdvisorIntent.None)
            return null;

        return intent switch
        {
            AdvisorIntent.WhatAmIMissing => HandleWhatAmIMissing(userInput),
            AdvisorIntent.ResearchPriorEstimates => HandleResearchPrior(userInput),
            AdvisorIntent.InsurerIntelligence => HandleInsurerIntelligence(userInput),
            AdvisorIntent.PersonalizedTips => HandlePersonalizedTips(userInput),
            AdvisorIntent.PartRecommendation => HandlePartRecommendation(userInput),
            AdvisorIntent.EstimateReview => HandleEstimateReview(userInput),
            _ => null
        };
    }

    private AdvisorIntent ClassifyIntent(string input)
    {
        var lower = input.ToLowerInvariant();

        // EstimateReview
        if (Regex.IsMatch(lower, @"\b(review\s+(my\s+)?estimate|score\s+this|how\s+complete)\b"))
            return AdvisorIntent.EstimateReview;

        // WhatAmIMissing
        if (Regex.IsMatch(lower, @"\b(what\s+am\s+i\s+missing|what\s+else\s+(should|do\s+i)|am\s+i\s+forgetting|what\s+did\s+i\s+miss)\b"))
            return AdvisorIntent.WhatAmIMissing;

        // InsurerIntelligence
        if (Regex.IsMatch(lower, @"\b(what\s+does\s+\w+\s+pay|insurer|insurance\s+(company|pays|profile)|who\s+pays\s+for)\b"))
            return AdvisorIntent.InsurerIntelligence;

        // ResearchPriorEstimates
        if (Regex.IsMatch(lower, @"\b(similar\s+estimates?|prior\s+estimates?|past\s+estimates?|show\s+(me\s+)?similar|research\s+(my\s+)?estimates?|find\s+estimates?)\b"))
            return AdvisorIntent.ResearchPriorEstimates;

        // PersonalizedTips
        if (Regex.IsMatch(lower, @"\b(what\s+should\s+i\s+watch|tips?\s+for|watch\s+out|common\s+mistakes?|where\s+do\s+i\s+(mess|slip))\b"))
            return AdvisorIntent.PersonalizedTips;

        // PartRecommendation
        if (Regex.IsMatch(lower, @"\b(what\s+do\s+i\s+need\s+for|what\s+operations?\s+for|recommend.*(for|on)|what\s+goes\s+with)\b"))
            return AdvisorIntent.PartRecommendation;

        return AdvisorIntent.None;
    }

    /// <summary>
    /// Detects whether a user message is an advisor-type query (used by ChatbotView for routing).
    /// </summary>
    public bool IsAdvisorQuery(string userInput)
    {
        return ClassifyIntent(userInput) != AdvisorIntent.None;
    }

    #endregion

    #region Handler: What Am I Missing

    private AdvisorResponse HandleWhatAmIMissing(string input)
    {
        var response = new AdvisorResponse();
        var part = ExtractPartFromInput(input);
        var operation = ExtractOperationFromInput(input);

        // If no specific part in the query, use ALL parts from the session context
        // This is what happens when the screen monitor calls "what am I missing"
        var partsToCheck = new List<string>();
        if (!string.IsNullOrEmpty(part))
        {
            partsToCheck.Add(part);
        }
        else if (_session.EnteredOperations.Any())
        {
            // Pull all unique part names from what's on screen
            partsToCheck = _session.EnteredOperations
                .Select(op => op.Part)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Section 1: Learned pattern suggestions for each detected part
        var allPatternItems = new List<AdvisorItem>();
        foreach (var checkPart in partsToCheck)
        {
            var enteredOp = _session.EnteredOperations.FirstOrDefault(o =>
                o.Part.Equals(checkPart, StringComparison.OrdinalIgnoreCase));
            var opType = enteredOp?.Operation ?? operation ?? "Replace";

            var suggestions = _suggestionService.GetSuggestionsForPart(checkPart, opType, _session.VehicleInfo);
            if (suggestions.ManualOperations.Any())
            {
                // Filter out operations the estimator already has on screen
                var existingOps = _session.EnteredOperations
                    .Select(o => $"{o.Part}|{o.Operation}".ToLower())
                    .ToHashSet();

                var newSuggestions = suggestions.ManualOperations
                    .Where(op => op.Confidence >= 0.5 && op.TimesUsed >= 2)
                    .Where(op => !existingOps.Contains($"{op.Description}|{opType}".ToLower()))
                    .OrderByDescending(op => op.TimesUsed)
                    .Take(5)
                    .Select(op => new AdvisorItem
                    {
                        Description = op.Description,
                        Detail = $"With {checkPart} | {op.Source} | used {op.TimesUsed}x",
                        Hours = op.LaborHours + op.RefinishHours,
                        Source = "patterns",
                        Confidence = op.Confidence
                    });
                allPatternItems.AddRange(newSuggestions);
            }
        }
        if (allPatternItems.Any())
        {
            response.Sections.Add(new AdvisorSection
            {
                Title = "LEARNED PATTERNS — operations you may be missing",
                Icon = "\U0001f9e0",
                Items = allPatternItems.Take(12).ToList()
            });
        }

        // Section 2: History frequency — commonly included with these parts
        var allHistoryItems = new List<AdvisorItem>();
        foreach (var checkPart in partsToCheck)
        {
            var commonOps = FindCommonlyIncludedOperations(checkPart, operation);
            allHistoryItems.AddRange(commonOps.Take(4).Select(kv => new AdvisorItem
            {
                Description = kv.Key,
                Detail = $"With {checkPart} | found in {kv.Value} estimates",
                Source = "history"
            }));
        }
        if (allHistoryItems.Any())
        {
            response.Sections.Add(new AdvisorSection
            {
                Title = "COMMONLY INCLUDED (from prior estimates)",
                Icon = "\U0001f4ca",
                Items = allHistoryItems.DistinctBy(i => i.Description).Take(10).ToList()
            });
        }

        // Section 3: Accuracy warnings
        var tips = _accuracyService.GetPersonalizedTips();
        if (tips.Any())
        {
            var relevantTips = tips
                .Where(t => string.IsNullOrEmpty(part) || t.Tip.Contains(part, StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();

            if (relevantTips.Any())
            {
                var section = new AdvisorSection
                {
                    Title = "ACCURACY WARNINGS",
                    Icon = "\u26a0\ufe0f",
                    Items = relevantTips.Select(t => new AdvisorItem
                    {
                        Description = t.Tip,
                        Detail = t.Category,
                        Source = "accuracy"
                    }).ToList()
                };
                response.Sections.Add(section);
            }
        }

        // Section 4: Scoring check against entered operations
        if (_session.EnteredOperations.Any())
        {
            var parsedLines = _session.EnteredOperations.Select(op => new ParsedEstimateLine
            {
                PartName = op.Part,
                OperationType = op.Operation,
                LaborHours = op.Hours
            }).ToList();

            var scoreResult = _scoringService.ScoreEstimate(parsedLines, _session.VehicleInfo);
            if (scoreResult.Issues.Any())
            {
                var section = new AdvisorSection
                {
                    Title = $"COMPLETENESS SCORE: {scoreResult.OverallScore}/100 ({scoreResult.Grade})",
                    Icon = "\U0001f3af",
                    Items = scoreResult.Issues
                        .OrderByDescending(i => i.PointDeduction)
                        .Take(5)
                        .Select(i => new AdvisorItem
                        {
                            Description = i.Title,
                            Detail = i.Description,
                            Source = "scoring"
                        }).ToList()
                };
                response.Sections.Add(section);
            }
        }

        // Section 5: Must-have operations check
        try
        {
            var contextService = EstimateContextService.Instance;
            var mustHaveChecklist = contextService.GetContext().MustHaveChecklist;
            var missing = mustHaveChecklist.Where(m => !m.IsPresent).ToList();

            if (missing.Any())
            {
                var section = new AdvisorSection
                {
                    Title = $"MUST-HAVE OPERATIONS — {missing.Count} MISSING",
                    Icon = "\u2757",
                    Items = missing.Select(m =>
                    {
                        var detail = m.LaborHours > 0 ? $"{m.OperationType}, {m.LaborHours:N1}h" : $"{m.OperationType}, ${m.Price:N2}";
                        if (m.MaterialsCost > 0) detail += $", ${m.MaterialsCost:N2} materials";
                        return new AdvisorItem
                        {
                            Description = m.Description,
                            Detail = $"[{m.Category}] {detail} — {m.WhyNeeded}",
                            Hours = m.LaborHours,
                            Source = "must-haves"
                        };
                    }).ToList()
                };
                response.Sections.Add(section);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Advisor] Must-have check error: {ex.Message}");
        }

        response.Summary = response.Sections.Any()
            ? $"Found {response.Sections.Sum(s => s.Items.Count)} items across {response.Sections.Count} categories"
            : "No specific recommendations found. Keep building your estimate!";

        response.FollowUpQuestions = new List<string>
        {
            "Show similar estimates",
            "What does my insurer pay for?",
            "Review my estimate"
        };

        return response;
    }

    #endregion

    #region Handler: Research Prior Estimates

    private AdvisorResponse HandleResearchPrior(string input)
    {
        var response = new AdvisorResponse();

        // Try the estimate query service first
        var queryResult = _queryService.ProcessQuery(input);
        if (queryResult != null && queryResult.Success)
        {
            var section = new AdvisorSection
            {
                Title = "ESTIMATE HISTORY SEARCH",
                Icon = "\U0001f50d",
                Items = new List<AdvisorItem>()
            };

            if (queryResult.Estimates != null)
            {
                foreach (var est in queryResult.Estimates.Take(5))
                {
                    section.Items.Add(new AdvisorItem
                    {
                        Description = $"{est.VehicleInfo ?? "Vehicle"} - {est.InsuranceCompany ?? "Unknown insurer"}",
                        Detail = $"RO: {est.RONumber ?? "N/A"} | {est.LineItems.Count} lines | Total: {est.GrandTotal:C0}",
                        Source = "history"
                    });
                }
            }

            if (section.Items.Any())
                response.Sections.Add(section);

            response.Summary = queryResult.Message ?? "Search complete";
        }

        // Also try learning service similarity search
        var vehicleInfo = ExtractVehicleFromInput(input);
        var part = ExtractPartFromInput(input);
        if (!string.IsNullOrEmpty(vehicleInfo) || !string.IsNullOrEmpty(part))
        {
            var similarResult = _learningService.FindSimilarEstimates(
                vehicleInfo ?? _session.VehicleInfo ?? "",
                part ?? "",
                "");

            if (similarResult.MatchingEstimates.Any())
            {
                var section = new AdvisorSection
                {
                    Title = "SIMILAR PATTERNS (DNA match)",
                    Icon = "\U0001f9ec",
                    Items = similarResult.MatchingEstimates.Take(5).Select(m => new AdvisorItem
                    {
                        Description = $"{m.VehicleInfo} - {m.Operation.PartName} {m.Operation.OperationType}",
                        Detail = $"Imported: {m.DateImported:d}",
                        Source = "learning"
                    }).ToList()
                };
                response.Sections.Add(section);
            }
        }

        if (!response.Sections.Any())
        {
            response.Summary = "No matching estimates found in history. Import more estimates to build your database!";
        }

        response.FollowUpQuestions = new List<string>
        {
            "What am I missing?",
            "What does the insurer pay for?",
            "Show my estimate statistics"
        };

        return response;
    }

    #endregion

    #region Handler: Insurer Intelligence

    private AdvisorResponse HandleInsurerIntelligence(string input)
    {
        var response = new AdvisorResponse();
        var insurer = ExtractInsurerFromInput(input) ?? _session.InsuranceCompany;

        if (string.IsNullOrEmpty(insurer))
        {
            response.Summary = "Which insurance company? Try: 'What does State Farm pay for on bumper jobs?'";
            return response;
        }

        // Get payment profile
        var profile = _historyDb.GetInsurerPaymentProfile(insurer);
        if (profile.Any())
        {
            var section = new AdvisorSection
            {
                Title = $"{insurer.ToUpper()} PAYMENT PROFILE",
                Icon = "\U0001f4b0",
                Items = profile
                    .OrderByDescending(kv => kv.Value.TimesPaid)
                    .Take(10)
                    .Select(kv => new AdvisorItem
                    {
                        Description = kv.Key,
                        Detail = $"Paid {kv.Value.TimesPaid}x | Avg {kv.Value.AverageHours:N1} hr | ${kv.Value.TotalAmountPaid:N0} total",
                        Hours = kv.Value.AverageHours,
                        Source = "history"
                    }).ToList()
            };
            response.Sections.Add(section);
        }

        // Check for specific operation in the query
        var operation = ExtractOperationFromInput(input);
        var part = ExtractPartFromInput(input);
        if (!string.IsNullOrEmpty(operation) || !string.IsNullOrEmpty(part))
        {
            var keyword = part ?? operation ?? "";
            var paymentStats = _historyDb.GetOperationPaymentStats(insurer, keyword);
            if (paymentStats.TimesPaid > 0)
            {
                var section = new AdvisorSection
                {
                    Title = $"{keyword.ToUpper()} - {insurer.ToUpper()} HISTORY",
                    Icon = "\U0001f4cb",
                    Items = new List<AdvisorItem>
                    {
                        new() { Description = $"Times paid: {paymentStats.TimesPaid}", Source = "history" },
                        new() { Description = $"Average hours: {paymentStats.AverageHours:N1}", Source = "history" },
                        new() { Description = $"Total amount: {paymentStats.TotalAmountPaid:C0}", Source = "history" }
                    }
                };
                response.Sections.Add(section);
            }
        }

        // Cross-insurer comparison
        var allEstimates = _historyDb.GetAllEstimates();
        var insurers = allEstimates
            .Where(e => !string.IsNullOrEmpty(e.InsuranceCompany))
            .Select(e => e.InsuranceCompany!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (insurers.Count > 1)
        {
            var section = new AdvisorSection
            {
                Title = "OTHER INSURERS IN DATABASE",
                Icon = "\U0001f4ca",
                Items = insurers
                    .Where(i => !i.Equals(insurer, StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .Select(i =>
                    {
                        var count = allEstimates.Count(e => i.Equals(e.InsuranceCompany, StringComparison.OrdinalIgnoreCase));
                        return new AdvisorItem
                        {
                            Description = i,
                            Detail = $"{count} estimates on file",
                            Source = "history"
                        };
                    }).ToList()
            };
            if (section.Items.Any())
                response.Sections.Add(section);
        }

        response.Summary = response.Sections.Any()
            ? $"Found payment data for {insurer}"
            : $"No estimate history found for {insurer}. Import estimates with this insurer to build intelligence.";

        response.FollowUpQuestions = new List<string>
        {
            $"What does {insurer} pay for on front bumper?",
            "Compare with other insurers",
            "What am I missing?"
        };

        return response;
    }

    #endregion

    #region Handler: Personalized Tips

    private AdvisorResponse HandlePersonalizedTips(string input)
    {
        var response = new AdvisorResponse();

        // Accuracy analysis
        var analysis = _accuracyService.GetAccuracyAnalysis();
        if (analysis.TotalEstimates > 0)
        {
            var section = new AdvisorSection
            {
                Title = $"YOUR ACCURACY: {analysis.OverallAccuracyRate:P0}",
                Icon = "\U0001f3af",
                Items = new List<AdvisorItem>()
            };

            foreach (var problem in analysis.ProblemAreas.Take(3))
            {
                section.Items.Add(new AdvisorItem
                {
                    Description = $"{problem.Category}: supplemented {problem.SupplementCount}x",
                    Detail = $"Avg missed: {problem.AverageHoursAdded:N1} hr per supplement",
                    Source = "accuracy"
                });
            }

            if (section.Items.Any())
                response.Sections.Add(section);
        }

        // Feedback effectiveness
        var feedbackReport = _feedbackService.GetSummary();
        if (feedbackReport.TotalPatterns > 0)
        {
            var section = new AdvisorSection
            {
                Title = $"LEARNING EFFECTIVENESS: {feedbackReport.OverallAcceptanceRate:P0} acceptance",
                Icon = "\U0001f4c8",
                Items = new List<AdvisorItem>
                {
                    new() { Description = $"Patterns tracked: {feedbackReport.TotalPatterns}", Source = "feedback" },
                    new() { Description = $"Suggestions made: {feedbackReport.TotalSuggestions}", Source = "feedback" },
                    new() { Description = $"High-performing patterns: {feedbackReport.HighPerformingPatterns}", Source = "feedback" }
                }
            };
            response.Sections.Add(section);
        }

        // Stale patterns warning
        var stalePatterns = _intelligenceService.GetStalePatterns();
        if (stalePatterns.Any())
        {
            var section = new AdvisorSection
            {
                Title = $"STALE PATTERNS ({stalePatterns.Count})",
                Icon = "\u23f0",
                Items = stalePatterns.Take(5).Select(p => new AdvisorItem
                {
                    Description = p,
                    Detail = "Not updated in 180+ days - may need refresh",
                    Source = "intelligence"
                }).ToList()
            };
            response.Sections.Add(section);
        }

        // Personalized tips
        var tips = _accuracyService.GetPersonalizedTips();
        if (tips.Any())
        {
            var section = new AdvisorSection
            {
                Title = "PERSONALIZED TIPS",
                Icon = "\U0001f4a1",
                Items = tips.Take(5).Select(t => new AdvisorItem
                {
                    Description = t.Tip,
                    Detail = t.Category,
                    Source = "accuracy"
                }).ToList()
            };
            response.Sections.Add(section);
        }

        response.Summary = response.Sections.Any()
            ? "Here's your personalized performance analysis"
            : "Not enough data yet. Import estimates and record feedback to get personalized tips!";

        response.FollowUpQuestions = new List<string>
        {
            "Where am I leaving money?",
            "Show my supplement patterns",
            "What am I missing?"
        };

        return response;
    }

    #endregion

    #region Handler: Part Recommendation

    private AdvisorResponse HandlePartRecommendation(string input)
    {
        var response = new AdvisorResponse();
        var part = ExtractPartFromInput(input);
        var operation = ExtractOperationFromInput(input) ?? "Replace";

        if (string.IsNullOrEmpty(part))
        {
            response.Summary = "Which part? Try: 'What do I need for quarter panel replace?'";
            return response;
        }

        // Pattern-based suggestions
        var suggestions = _suggestionService.GetSuggestionsForPart(part, operation, _session.VehicleInfo);
        if (suggestions.ManualOperations.Any())
        {
            var section = new AdvisorSection
            {
                Title = $"OPERATIONS FOR {part.ToUpper()} {operation.ToUpper()}",
                Icon = "\U0001f527",
                Items = suggestions.ManualOperations
                    .OrderByDescending(op => op.TimesUsed)
                    .Take(10)
                    .Select(op => new AdvisorItem
                    {
                        Description = op.Description,
                        Detail = $"{op.OperationType} | {op.LaborHours + op.RefinishHours:N1} hr | {op.Source}",
                        Hours = op.LaborHours + op.RefinishHours,
                        Source = "patterns",
                        Confidence = op.Confidence
                    }).ToList()
            };
            response.Sections.Add(section);
        }

        // History-based common operations
        var commonOps = FindCommonlyIncludedOperations(part, operation);
        if (commonOps.Any())
        {
            var section = new AdvisorSection
            {
                Title = "COMMONLY PAIRED OPERATIONS",
                Icon = "\U0001f517",
                Items = commonOps.Take(6).Select(kv => new AdvisorItem
                {
                    Description = kv.Key,
                    Detail = $"Found in {kv.Value} prior estimates",
                    Source = "history"
                }).ToList()
            };
            response.Sections.Add(section);
        }

        // Scoring hints for this part
        var testLines = new List<ParsedEstimateLine>
        {
            new() { PartName = part, OperationType = operation, LaborHours = 1.0m }
        };
        var scoreResult = _scoringService.ScoreEstimate(testLines, _session.VehicleInfo);
        var relevantIssues = scoreResult.Issues.Where(i => i.Severity == IssueSeverity.Critical || i.Severity == IssueSeverity.High).ToList();
        if (relevantIssues.Any())
        {
            var section = new AdvisorSection
            {
                Title = "DON'T FORGET",
                Icon = "\u2757",
                Items = relevantIssues.Take(5).Select(i => new AdvisorItem
                {
                    Description = i.Title,
                    Detail = !string.IsNullOrEmpty(i.WhyNeeded) ? i.WhyNeeded : i.Description,
                    Source = "scoring"
                }).ToList()
            };
            response.Sections.Add(section);
        }

        response.Summary = response.Sections.Any()
            ? $"Recommendations for {part} {operation}"
            : $"No pattern data yet for {part} {operation}. Import estimates with this operation to build intelligence.";

        response.FollowUpQuestions = new List<string>
        {
            $"What am I missing on {part} {operation}?",
            $"What does my insurer pay for on {part}?",
            "Show similar estimates"
        };

        return response;
    }

    #endregion

    #region Handler: Estimate Review

    private AdvisorResponse HandleEstimateReview(string input)
    {
        var response = new AdvisorResponse();

        if (!_session.EnteredOperations.Any())
        {
            response.Summary = "No operations entered in this session yet. Enter some operations in chat first, then ask me to review!";
            return response;
        }

        var parsedLines = _session.EnteredOperations.Select(op => new ParsedEstimateLine
        {
            PartName = op.Part,
            OperationType = op.Operation,
            LaborHours = op.Hours
        }).ToList();

        var scoreResult = _scoringService.ScoreEstimate(parsedLines, _session.VehicleInfo);

        // Overall score
        var scoreSection = new AdvisorSection
        {
            Title = $"ESTIMATE SCORE: {scoreResult.OverallScore}/100 ({scoreResult.Grade})",
            Icon = "\U0001f3c6",
            Items = new List<AdvisorItem>
            {
                new() { Description = scoreResult.Summary, Source = "scoring" }
            }
        };
        response.Sections.Add(scoreSection);

        // Issues by severity
        if (scoreResult.Issues.Any())
        {
            var criticalHigh = scoreResult.Issues
                .Where(i => i.Severity == IssueSeverity.Critical || i.Severity == IssueSeverity.High)
                .ToList();

            if (criticalHigh.Any())
            {
                var section = new AdvisorSection
                {
                    Title = "CRITICAL ISSUES",
                    Icon = "\U0001f6a8",
                    Items = criticalHigh.Take(5).Select(i => new AdvisorItem
                    {
                        Description = i.Title,
                        Detail = i.Description,
                        Source = "scoring"
                    }).ToList()
                };
                response.Sections.Add(section);
            }

            var medium = scoreResult.Issues.Where(i => i.Severity == IssueSeverity.Medium).ToList();
            if (medium.Any())
            {
                var section = new AdvisorSection
                {
                    Title = "IMPROVEMENTS",
                    Icon = "\U0001f4dd",
                    Items = medium.Take(5).Select(i => new AdvisorItem
                    {
                        Description = i.Title,
                        Detail = i.Description,
                        Source = "scoring"
                    }).ToList()
                };
                response.Sections.Add(section);
            }
        }

        // Accuracy warning based on personal history
        var analysis = _accuracyService.GetAccuracyAnalysis();
        if (analysis.ProblemAreas.Any())
        {
            var enteredParts = _session.EnteredOperations.Select(o => o.Part.ToLower()).Distinct().ToList();
            var relevantProblems = analysis.ProblemAreas
                .Where(p => enteredParts.Any(ep => p.Category.Contains(ep, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (relevantProblems.Any())
            {
                var section = new AdvisorSection
                {
                    Title = "PERSONAL SUPPLEMENT RISK",
                    Icon = "\u26a0\ufe0f",
                    Items = relevantProblems.Take(3).Select(p => new AdvisorItem
                    {
                        Description = $"{p.Category}: supplemented {p.SupplementCount}x in your history",
                        Detail = $"You typically miss ~{p.AverageHoursAdded:N1} hr",
                        Source = "accuracy"
                    }).ToList()
                };
                response.Sections.Add(section);
            }
        }

        // Must-have operations check
        try
        {
            var contextService = EstimateContextService.Instance;
            var mustHaveChecklist = contextService.GetContext().MustHaveChecklist;
            var missing = mustHaveChecklist.Where(m => !m.IsPresent).ToList();

            if (missing.Any())
            {
                var section = new AdvisorSection
                {
                    Title = $"MUST-HAVE OPERATIONS — {missing.Count} MISSING",
                    Icon = "\u2757",
                    Items = missing.Take(10).Select(m =>
                    {
                        var detail = m.LaborHours > 0 ? $"{m.OperationType}, {m.LaborHours:N1}h" : $"{m.OperationType}, ${m.Price:N2}";
                        return new AdvisorItem
                        {
                            Description = m.Description,
                            Detail = $"[{m.Category}] {detail}",
                            Hours = m.LaborHours,
                            Source = "must-haves"
                        };
                    }).ToList()
                };
                response.Sections.Add(section);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Advisor] Must-have review error: {ex.Message}");
        }

        response.Summary = $"Score: {scoreResult.OverallScore}/100 | {_session.EnteredOperations.Count} operations reviewed";

        response.FollowUpQuestions = new List<string>
        {
            "What am I missing?",
            "Show similar estimates",
            "What does my insurer pay for?"
        };

        return response;
    }

    #endregion

    #region Proactive Suggestions

    /// <summary>
    /// Called automatically after user enters an operation in chat.
    /// Returns pattern-based missing ops, accuracy warnings, and insurer notes.
    /// Only returns when data is meaningful (confidence > 60%, timesUsed >= 3).
    /// </summary>
    public ProactiveSuggestion? GetProactiveSuggestions(string part, string operation, List<string> enteredOpTypes, string? vehicleInfo)
    {
        var result = new ProactiveSuggestion();
        bool hasMeaningfulData = false;

        // 1. Pattern-based suggestions for commonly missed operations
        var suggestions = _suggestionService.GetSuggestionsForPart(part, operation, vehicleInfo ?? _session.VehicleInfo);
        if (suggestions.ManualOperations.Any())
        {
            var enteredDescriptions = enteredOpTypes.Select(o => o.ToLower()).ToHashSet();

            var missing = suggestions.ManualOperations
                .Where(op => op.Confidence >= 0.6 && op.TimesUsed >= 3)
                .Where(op => !enteredDescriptions.Contains(op.OperationType.ToLower()))
                .Where(op => !enteredDescriptions.Any(e => op.Description.ToLower().Contains(e)))
                .OrderByDescending(op => op.TimesUsed)
                .Take(4)
                .ToList();

            if (missing.Any())
            {
                result.PatternSuggestions = missing.Select(op => new PatternSuggestionItem
                {
                    Description = op.Description,
                    OperationType = op.OperationType,
                    Hours = op.LaborHours + op.RefinishHours,
                    TimesUsed = op.TimesUsed,
                    Confidence = op.Confidence,
                    Source = op.Source
                }).ToList();
                hasMeaningfulData = true;
            }
        }

        // 2. Accuracy warning for this part category
        var tips = _accuracyService.GetPersonalizedTips();
        var partLower = part.ToLower();
        var relevantTip = tips.FirstOrDefault(t => t.Tip.Contains(partLower, StringComparison.OrdinalIgnoreCase));
        if (relevantTip != null)
        {
            result.AccuracyWarning = $"[{relevantTip.Category}] {relevantTip.Tip}";
            hasMeaningfulData = true;
        }

        // 3. Insurer-specific notes
        var insurer = _session.InsuranceCompany;
        if (!string.IsNullOrEmpty(insurer))
        {
            var paymentStats = _historyDb.GetOperationPaymentStats(insurer, part);
            if (paymentStats.TimesPaid > 0)
            {
                result.InsurerNote = $"{insurer} has paid for {part} operations {paymentStats.TimesPaid}x (avg {paymentStats.AverageHours:N1} hr)";
                hasMeaningfulData = true;
            }
        }

        return hasMeaningfulData ? result : null;
    }

    #endregion

    #region Helper: Find Commonly Included Operations

    /// <summary>
    /// Aggregates operations across historical estimates for a given part/operation.
    /// Returns frequency map of what's commonly found alongside this operation.
    /// </summary>
    private Dictionary<string, int> FindCommonlyIncludedOperations(string part, string? operation)
    {
        var allEstimates = _historyDb.GetAllEstimates();
        var partLower = part.ToLower();
        var frequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Find estimates containing this part
        var relevantEstimates = allEstimates
            .Where(e => e.LineItems.Any(li => li.PartName != null && li.PartName.ToLower().Contains(partLower)))
            .ToList();

        if (!relevantEstimates.Any())
            return frequency;

        // Count other operations in those estimates
        foreach (var estimate in relevantEstimates)
        {
            foreach (var item in estimate.LineItems)
            {
                if (item.PartName == null) continue;
                // Skip the queried part itself
                if (item.PartName.ToLower().Contains(partLower)) continue;

                var key = $"{item.PartName} - {item.OperationType}";
                frequency.TryGetValue(key, out var count);
                frequency[key] = count + 1;
            }
        }

        // Only return items found in multiple estimates
        return frequency
            .Where(kv => kv.Value >= 2)
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    #endregion

    #region Text Extraction Helpers

    private string? ExtractPartFromInput(string input)
    {
        var lower = input.ToLowerInvariant();
        var partPatterns = new[]
        {
            "bumper cover", "bumper", "front bumper", "rear bumper",
            "fender", "quarter panel", "quarter", "door", "door shell",
            "hood", "trunk lid", "trunk", "decklid", "roof", "roof panel",
            "windshield", "back glass", "door glass",
            "headlamp", "headlight", "tail lamp", "taillight",
            "grille", "radiator support", "core support",
            "rocker panel", "a-pillar", "b-pillar", "c-pillar",
            "mirror", "handle", "molding", "spoiler", "valance"
        };

        // Check for explicit part mentions
        foreach (var partPattern in partPatterns)
        {
            if (lower.Contains(partPattern))
                return partPattern;
        }

        // Try to extract from "on X" or "for X" patterns
        var match = Regex.Match(lower, @"(?:on|for|with)\s+(.+?)(?:\s+(?:replace|repair|refinish|r&i)|$|\?)");
        if (match.Success)
        {
            var extracted = match.Groups[1].Value.Trim();
            if (extracted.Length > 2 && extracted.Length < 40)
                return extracted;
        }

        return null;
    }

    private string? ExtractOperationFromInput(string input)
    {
        var lower = input.ToLowerInvariant();
        if (lower.Contains("replace") || lower.Contains("replacing"))
            return "Replace";
        if (lower.Contains("repair") || lower.Contains("repairing"))
            return "Repair";
        if (lower.Contains("refinish") || lower.Contains("paint"))
            return "Refinish";
        if (lower.Contains("r&i") || lower.Contains("remove and install"))
            return "R&I";
        if (lower.Contains("overhaul"))
            return "Overhaul";
        if (lower.Contains("blend"))
            return "Blend";
        return null;
    }

    private string? ExtractVehicleFromInput(string input)
    {
        // Match patterns like "2024 Civic", "Honda Accord", "2022 Toyota Camry"
        var match = Regex.Match(input, @"(\d{4}\s+)?(\w+\s+\w+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var candidate = match.Value.Trim();
            // Filter out common non-vehicle words
            var nonVehicle = new[] { "what am", "show me", "what does", "how do", "state farm", "what should" };
            if (!nonVehicle.Any(nv => candidate.ToLower().Contains(nv)))
                return candidate;
        }
        return null;
    }

    private string? ExtractInsurerFromInput(string input)
    {
        var lower = input.ToLowerInvariant();
        var insurers = new[]
        {
            "state farm", "allstate", "geico", "progressive", "usaa",
            "liberty mutual", "nationwide", "farmers", "travelers",
            "american family", "erie", "hartford", "safeco",
            "mercury", "encompass", "amica", "auto-owners"
        };

        foreach (var insurer in insurers)
        {
            if (lower.Contains(insurer))
                return insurer;
        }

        // Try "what does X pay" pattern
        var match = Regex.Match(lower, @"what\s+does\s+(\w+(?:\s+\w+)?)\s+pay");
        if (match.Success)
            return match.Groups[1].Value;

        return null;
    }

    #endregion
}

#region Data Models

public enum AdvisorIntent
{
    None,
    WhatAmIMissing,
    ResearchPriorEstimates,
    InsurerIntelligence,
    PersonalizedTips,
    PartRecommendation,
    EstimateReview
}

public class AdvisorResponse
{
    public List<AdvisorSection> Sections { get; set; } = new();
    public string Summary { get; set; } = "";
    public List<string> FollowUpQuestions { get; set; } = new();
}

public class AdvisorSection
{
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<AdvisorItem> Items { get; set; } = new();
}

public class AdvisorItem
{
    public string Description { get; set; } = "";
    public string Detail { get; set; } = "";
    public decimal Hours { get; set; }
    public string Source { get; set; } = "";
    public double Confidence { get; set; }
}

public class ProactiveSuggestion
{
    public List<PatternSuggestionItem> PatternSuggestions { get; set; } = new();
    public string? AccuracyWarning { get; set; }
    public string? InsurerNote { get; set; }
}

public class PatternSuggestionItem
{
    public string Description { get; set; } = "";
    public string OperationType { get; set; } = "";
    public decimal Hours { get; set; }
    public int TimesUsed { get; set; }
    public double Confidence { get; set; }
    public string Source { get; set; } = "";
}

public class AdvisorSessionContext
{
    public string? VehicleInfo { get; set; }
    public string? VehicleType { get; set; }
    public string? InsuranceCompany { get; set; }
    public List<EnteredOp> EnteredOperations { get; set; } = new();
}

public class EnteredOp
{
    public string Part { get; set; } = "";
    public string Operation { get; set; } = "";
    public decimal Hours { get; set; }
}

#endregion
