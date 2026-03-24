#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace McStudDesktop.Services
{
    public class LearningAnalysisReport
    {
        public DateTime GeneratedAt { get; set; }
        public int EstimatesAnalyzed { get; set; }
        public int PatternsAnalyzed { get; set; }
        public List<AnalysisRecommendation> Recommendations { get; set; } = new();
        public string Summary { get; set; } = "";
        public string RawReportText { get; set; } = "";
    }

    public class AnalysisRecommendation
    {
        [JsonPropertyName("category")]
        public string Category { get; set; } = "";
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";
        [JsonPropertyName("detail")]
        public string Detail { get; set; } = "";
        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
        [JsonPropertyName("sourceData")]
        public string SourceData { get; set; } = "";
    }

    public class AiLearningAnalysisService
    {
        private static AiLearningAnalysisService? _instance;
        public static AiLearningAnalysisService Instance => _instance ??= new AiLearningAnalysisService();

        private AiLearningAnalysisService() { }

        public async Task<LearningAnalysisReport?> AnalyzeAndRecommendAsync(CancellationToken ct = default)
        {
            if (!AiConfigService.Instance.IsFeatureEnabled(AiFeature.Automation))
                return null;

            // 1. Gather learning stats
            var learningStats = EstimateLearningService.Instance.GetStatistics();
            var knowledgeStats = LearnedKnowledgeBase.Instance.GetStatistics();
            var database = EstimateLearningService.Instance.CurrentDatabase;
            var allParts = LearnedKnowledgeBase.Instance.GetAllParts();

            // 2. Read current static data files
            var missedItemsJson = ReadStaticDataFile("CommonlyMissedItems.json");
            var mustHaveJson = ReadStaticDataFile("MustHaveOperations.json");

            // 3. Build sanitized summary for AI
            var dataSummary = BuildDataSummary(learningStats, knowledgeStats, database, allParts, missedItemsJson, mustHaveJson);

            // 4. Send to Claude
            var systemPrompt = BuildSystemPrompt();
            var response = await ClaudeApiService.Instance.SendAsync(
                systemPrompt, dataSummary, AiFeature.Automation, maxTokens: 4096, cancellationToken: ct);

            if (response == null)
                return null;

            // 5. Parse response
            return ParseResponse(response.Text, learningStats, knowledgeStats);
        }

        private string ReadStaticDataFile(string fileName)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Data", fileName);
                if (File.Exists(path))
                    return File.ReadAllText(path);
            }
            catch { }
            return "{}";
        }

        private string BuildDataSummary(
            LearningStatistics learningStats,
            KnowledgeStatistics knowledgeStats,
            LearnedPatternDatabase database,
            IReadOnlyDictionary<string, PartKnowledge> allParts,
            string missedItemsJson,
            string mustHaveJson)
        {
            var sb = new StringBuilder();

            // Learning overview
            sb.AppendLine("=== LEARNING DATABASE OVERVIEW ===");
            sb.AppendLine($"Total estimates imported: {learningStats.EstimatesImported}");
            sb.AppendLine($"Total patterns: {learningStats.TotalPatterns}");
            sb.AppendLine($"Total examples: {learningStats.TotalExamples}");
            sb.AppendLine($"Average confidence: {learningStats.AverageConfidence:F2}");
            sb.AppendLine($"Last updated: {learningStats.LastUpdated:yyyy-MM-dd}");
            sb.AppendLine();

            // Knowledge base overview
            sb.AppendLine("=== KNOWLEDGE BASE OVERVIEW ===");
            sb.AppendLine($"Parts learned: {knowledgeStats.TotalPartsLearned}");
            sb.AppendLine($"Aliases learned: {knowledgeStats.TotalAliasesLearned}");
            sb.AppendLine($"Co-occurrence patterns: {knowledgeStats.TotalCoOccurrencePatterns}");
            sb.AppendLine($"Estimates analyzed: {knowledgeStats.TotalEstimatesAnalyzed}");
            sb.AppendLine($"Total value analyzed: ${knowledgeStats.TotalValueAnalyzed:N2}");
            sb.AppendLine();

            // Top patterns with co-occurrences
            sb.AppendLine("=== TOP LEARNED PATTERNS (by example count) ===");
            var topPatterns = database.Patterns.Values
                .OrderByDescending(p => p.ExampleCount)
                .Take(50);
            foreach (var pattern in topPatterns)
            {
                sb.AppendLine($"- {pattern.PartName} | {pattern.OperationType} | examples: {pattern.ExampleCount} | confidence: {pattern.Confidence:F2} | stale: {pattern.IsStale}");
                if (pattern.Operations.Count > 0)
                {
                    foreach (var op in pattern.Operations.Take(3))
                        sb.AppendLine($"    op: {op.Description} | {op.OperationType} | labor: {op.LaborHours}h | refinish: {op.RefinishHours}h | price: ${op.Price}");
                }
            }
            sb.AppendLine();

            // Co-occurrence data
            if (database.CoOccurrences?.Count > 0)
            {
                sb.AppendLine("=== CO-OCCURRENCE PATTERNS (learned from real estimates) ===");
                var topCoOccurrences = database.CoOccurrences
                    .OrderByDescending(c => c.Value.TotalEstimateCount)
                    .Take(40);
                foreach (var kvp in topCoOccurrences)
                    sb.AppendLine($"- {kvp.Key}: seen {kvp.Value.TotalEstimateCount} times, co-ops: {kvp.Value.CoOccurringOperations.Count}");
                sb.AppendLine();
            }

            // Operation baselines
            if (database.Baselines?.Count > 0)
            {
                sb.AppendLine("=== OPERATION BASELINES (learned labor hours) ===");
                var topBaselines = database.Baselines
                    .OrderByDescending(b => b.Value.SampleCount)
                    .Take(40);
                foreach (var kvp in topBaselines)
                {
                    var b = kvp.Value;
                    sb.AppendLine($"- {kvp.Key}: mean labor {b.MeanLaborHours:F2}h | range {b.MinLaborHours:F2}-{b.MaxLaborHours:F2}h | samples: {b.SampleCount}");
                }
                sb.AppendLine();
            }

            // Part knowledge with operation stats
            sb.AppendLine("=== PART KNOWLEDGE (top parts by frequency) ===");
            var topParts = allParts.Values
                .OrderByDescending(p => p.TimesSeenInEstimates)
                .Take(40);
            foreach (var part in topParts)
            {
                sb.AppendLine($"- {part.CanonicalName}: seen {part.TimesSeenInEstimates}x");
                foreach (var opStat in part.OperationStats.Take(3))
                    sb.AppendLine($"    {opStat.Key}: mean labor {opStat.Value.MeanLaborHours:F2}h, median {opStat.Value.MedianLaborHours:F2}h, samples: {opStat.Value.SampleCount}");
                if (part.CoOccurrences.Count > 0)
                {
                    var coOccs = string.Join(", ", part.CoOccurrences.Take(5).Select(c => $"{c.AssociatedPart}({c.CoOccurrenceRate:F2})"));
                    sb.AppendLine($"    co-occurs with: {coOccs}");
                }
            }
            sb.AppendLine();

            // Stale patterns
            var stalePatterns = database.Patterns.Values.Where(p => p.IsStale).ToList();
            if (stalePatterns.Count > 0)
            {
                sb.AppendLine($"=== STALE PATTERNS ({stalePatterns.Count} total) ===");
                foreach (var sp in stalePatterns.Take(20))
                    sb.AppendLine($"- {sp.PatternKey}: confidence {sp.Confidence:F2} -> decayed {sp.DecayedConfidence:F2}, last updated {sp.LastUpdated:yyyy-MM-dd}");
                sb.AppendLine();
            }

            // Health metrics
            if (database.HealthMetrics != null)
            {
                var hm = database.HealthMetrics;
                sb.AppendLine("=== LEARNING HEALTH ===");
                sb.AppendLine($"Health score: {hm.HealthScore}/100");
                sb.AppendLine($"High confidence: {hm.HighConfidencePatterns} | Medium: {hm.MediumConfidencePatterns} | Low: {hm.LowConfidencePatterns}");
                sb.AppendLine($"Stale: {hm.StalePatterns} | Conflicting: {hm.ConflictingPatternsCount}");
                sb.AppendLine($"Acceptance rate: {hm.OverallAcceptanceRate:F2} | Rejection rate: {hm.OverallRejectionRate:F2}");
                if (hm.TopRejectedPatterns.Count > 0)
                    sb.AppendLine($"Top rejected: {string.Join(", ", hm.TopRejectedPatterns.Take(10))}");
                sb.AppendLine();
            }

            // Feedback data
            if (database.PatternFeedbacks?.Count > 0)
            {
                sb.AppendLine("=== PATTERN FEEDBACK (user accept/reject data) ===");
                var topFeedback = database.PatternFeedbacks
                    .Where(f => f.Value.TimesGenerated > 2)
                    .OrderByDescending(f => f.Value.TimesGenerated)
                    .Take(20);
                foreach (var kvp in topFeedback)
                    sb.AppendLine($"- {kvp.Key}: accepted {kvp.Value.TimesAccepted}, rejected {kvp.Value.TimesRejected}, rate {kvp.Value.AcceptanceRate:F2}");
                sb.AppendLine();
            }

            // Current static data (abbreviated)
            sb.AppendLine("=== CURRENT CommonlyMissedItems.json (abbreviated) ===");
            sb.AppendLine(TruncateJson(missedItemsJson, 3000));
            sb.AppendLine();
            sb.AppendLine("=== CURRENT MustHaveOperations.json (abbreviated) ===");
            sb.AppendLine(TruncateJson(mustHaveJson, 2000));

            return sb.ToString();
        }

        private string TruncateJson(string json, int maxLength)
        {
            if (json.Length <= maxLength) return json;
            return json[..maxLength] + "\n... (truncated)";
        }

        private string BuildSystemPrompt()
        {
            return @"You are an expert collision repair estimating analyst. You are analyzing learned patterns from real collision repair estimates to improve two static reference data files:

1. CommonlyMissedItems.json - Contains operation checks with trigger keywords and commonly missed supplement items
2. MustHaveOperations.json - Contains must-have operations organized by category with labor hours and conditions

Your task is to compare the LEARNED DATA (from real uploaded estimates) against the CURRENT STATIC DATA and produce actionable recommendations.

Respond with a JSON object in this exact format:
{
  ""summary"": ""A 2-3 sentence plain-English overview of findings"",
  ""recommendations"": [
    {
      ""category"": ""missing_item|must_have|labor_update|new_rule|stale_pattern"",
      ""title"": ""Short title"",
      ""detail"": ""Detailed explanation of what should change and why"",
      ""confidence"": 0.85,
      ""sourceData"": ""What learned data supports this recommendation""
    }
  ]
}

Categories:
- missing_item: A co-occurrence pattern from real data that should be added to CommonlyMissedItems.json
- must_have: An operation that should be added to MustHaveOperations.json
- labor_update: A labor hour value in the static data that differs significantly from learned real-world data
- new_rule: A high-confidence learned pattern that should become a static rule
- stale_pattern: A learned pattern with low/decayed confidence that may need review or removal

Focus on:
1. Co-occurrence patterns that appear frequently in real estimates but are NOT in CommonlyMissedItems.json
2. Labor hour discrepancies between learned baselines and MustHaveOperations.json values
3. High-confidence patterns (>0.80) with many examples that could become static rules
4. Stale or low-confidence patterns that may be misleading the system
5. Parts that frequently appear together but aren't tracked as missed items

Be specific and actionable. Include exact values from the data. Order recommendations by confidence (highest first).";
        }

        private LearningAnalysisReport? ParseResponse(string responseText, LearningStatistics learningStats, KnowledgeStatistics knowledgeStats)
        {
            try
            {
                var cleaned = ClaudeApiService.StripCodeFences(responseText);
                var parsed = JsonSerializer.Deserialize<AiAnalysisResponse>(cleaned, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed == null)
                    return BuildFallbackReport(responseText, learningStats, knowledgeStats);

                var report = new LearningAnalysisReport
                {
                    GeneratedAt = DateTime.Now,
                    EstimatesAnalyzed = learningStats.EstimatesImported,
                    PatternsAnalyzed = learningStats.TotalPatterns,
                    Summary = parsed.Summary ?? "",
                    Recommendations = parsed.Recommendations ?? new()
                };

                report.RawReportText = FormatReportText(report);
                return report;
            }
            catch
            {
                return BuildFallbackReport(responseText, learningStats, knowledgeStats);
            }
        }

        private LearningAnalysisReport BuildFallbackReport(string responseText, LearningStatistics learningStats, KnowledgeStatistics knowledgeStats)
        {
            var report = new LearningAnalysisReport
            {
                GeneratedAt = DateTime.Now,
                EstimatesAnalyzed = learningStats.EstimatesImported,
                PatternsAnalyzed = learningStats.TotalPatterns,
                Summary = "AI returned a non-structured response. Raw text is included below.",
                RawReportText = $"McStud Learning Analysis Report\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm}\nEstimates Analyzed: {learningStats.EstimatesImported}\nPatterns Analyzed: {learningStats.TotalPatterns}\n\n--- AI Response ---\n{responseText}"
            };
            return report;
        }

        private string FormatReportText(LearningAnalysisReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("McStud Learning Analysis Report");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Estimates Analyzed: {report.EstimatesAnalyzed}");
            sb.AppendLine($"Patterns Analyzed: {report.PatternsAnalyzed}");
            sb.AppendLine($"Recommendations: {report.Recommendations.Count}");
            sb.AppendLine();

            sb.AppendLine("SUMMARY");
            sb.AppendLine(new string('-', 50));
            sb.AppendLine(report.Summary);
            sb.AppendLine();

            // Group recommendations by category
            var grouped = report.Recommendations
                .GroupBy(r => r.Category)
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                var categoryLabel = group.Key switch
                {
                    "missing_item" => "MISSING ITEMS (add to CommonlyMissedItems.json)",
                    "must_have" => "MUST-HAVE OPERATIONS (add to MustHaveOperations.json)",
                    "labor_update" => "LABOR HOUR UPDATES",
                    "new_rule" => "NEW RULES (high-confidence patterns)",
                    "stale_pattern" => "STALE PATTERNS (review/remove)",
                    _ => group.Key.ToUpperInvariant()
                };

                sb.AppendLine(categoryLabel);
                sb.AppendLine(new string('-', 50));

                int i = 1;
                foreach (var rec in group.OrderByDescending(r => r.Confidence))
                {
                    sb.AppendLine($"{i}. {rec.Title} (confidence: {rec.Confidence:F0}%)");
                    sb.AppendLine($"   {rec.Detail}");
                    if (!string.IsNullOrWhiteSpace(rec.SourceData))
                        sb.AppendLine($"   Source: {rec.SourceData}");
                    sb.AppendLine();
                    i++;
                }
            }

            return sb.ToString();
        }

        private class AiAnalysisResponse
        {
            [JsonPropertyName("summary")]
            public string? Summary { get; set; }
            [JsonPropertyName("recommendations")]
            public List<AnalysisRecommendation>? Recommendations { get; set; }
        }
    }
}
