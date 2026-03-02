#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services;

/// <summary>
/// Natural language query processor for the Estimate History Database.
/// Interprets questions like:
/// - "What estimate did Allstate pay corrosion protection?"
/// - "How many times did State Farm pay for pre-scan?"
/// - "Show me estimates similar to this one"
/// - "Which insurers pay for cavity wax?"
/// </summary>
public class EstimateQueryService
{
    private static EstimateQueryService? _instance;
    public static EstimateQueryService Instance => _instance ??= new EstimateQueryService();

    private readonly EstimateHistoryDatabase _historyDb;

    // Known insurance company names for extraction
    private static readonly string[] _knownInsurers = new[]
    {
        "Allstate", "State Farm", "GEICO", "Progressive", "Nationwide",
        "Liberty Mutual", "Farmers", "USAA", "Travelers", "American Family",
        "Erie", "Hartford", "Amica", "MetLife", "Safeco",
        "Mercury", "Infinity", "Kemper", "Plymouth Rock", "NJM",
        "AAA", "Chubb", "AIG", "Cincinnati", "Hanover"
    };

    // Known operation keywords for extraction
    private static readonly string[] _operationKeywords = new[]
    {
        "corrosion protection", "corrosion", "cavity wax", "seam sealer", "seam seal",
        "adhesion promoter", "flex additive", "flex agent",
        "de-nib", "denib", "color sand", "wet sand", "block sand",
        "pre-scan", "pre scan", "post-scan", "post scan", "diagnostic scan", "scan",
        "calibration", "calibrate", "ADAS", "camera calibration",
        "blend", "clearcoat", "clear coat", "basecoat", "base coat",
        "tricoat", "tri-coat", "three stage", "pearl",
        "prime", "primer", "mask", "masking", "backtape", "back tape",
        "cover car", "feather edge", "spot repair",
        "weld", "plug weld", "spot weld", "mig weld",
        "R&I", "remove and install", "replace", "repair", "refinish"
    };

    private EstimateQueryService()
    {
        _historyDb = EstimateHistoryDatabase.Instance;
    }

    /// <summary>
    /// Process a natural language query about estimate history
    /// Returns null if the query doesn't match estimate history patterns
    /// </summary>
    public EstimateQueryResult? ProcessQuery(string userInput)
    {
        var input = userInput.Trim().ToLowerInvariant();

        // Check if this is an estimate history query
        if (!IsEstimateHistoryQuery(input))
            return null;

        // Parse the query type and extract parameters
        var queryType = DetermineQueryType(input);

        return queryType switch
        {
            QueryType.InsurerOperationPayment => HandleInsurerOperationQuery(input),
            QueryType.OperationPaymentCount => HandlePaymentCountQuery(input),
            QueryType.InsurerProfile => HandleInsurerProfileQuery(input),
            QueryType.SimilarEstimates => HandleSimilarEstimatesQuery(input),
            QueryType.OverallStats => HandleStatsQuery(input),
            QueryType.EstimateSearch => HandleSearchQuery(input),
            QueryType.WhoPaysPor => HandleWhoPaysPorQuery(input),
            _ => null
        };
    }

    /// <summary>
    /// Check if input looks like an estimate history query
    /// </summary>
    private bool IsEstimateHistoryQuery(string input)
    {
        var historyIndicators = new[]
        {
            "did .+ pay",
            "does .+ pay",
            "how many times",
            "which estimate",
            "show .+ estimate",
            "find .+ estimate",
            "similar estimate",
            "estimate like",
            "insurance .+ pay",
            "insurer .+ pay",
            "who pays for",
            "which insurers",
            "my estimate",
            "history",
            "past estimate",
            "allstate", "state farm", "geico", "progressive", "nationwide",
            "liberty mutual", "farmers", "usaa"
        };

        foreach (var pattern in historyIndicators)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    private enum QueryType
    {
        Unknown,
        InsurerOperationPayment,  // "What estimate did Allstate pay corrosion protection?"
        OperationPaymentCount,    // "How many times did State Farm pay for pre-scan?"
        InsurerProfile,           // "What does Allstate typically pay for?"
        SimilarEstimates,         // "Find estimates similar to this one"
        OverallStats,             // "Show my estimate statistics"
        EstimateSearch,           // "Find estimates with bumper damage"
        WhoPaysPor               // "Who pays for corrosion protection?"
    }

    private QueryType DetermineQueryType(string input)
    {
        // "How many times did X pay for Y" - count query
        if (Regex.IsMatch(input, @"how many times.+(pay|paid)", RegexOptions.IgnoreCase))
            return QueryType.OperationPaymentCount;

        // "Who pays for X" or "Which insurers pay for X"
        if (Regex.IsMatch(input, @"(who|which insurer).+(pay|paid)", RegexOptions.IgnoreCase))
            return QueryType.WhoPaysPor;

        // "What does X pay for" or "X's payment profile"
        if (Regex.IsMatch(input, @"what (does|did) .+ (typically |usually )?(pay|cover)", RegexOptions.IgnoreCase))
            return QueryType.InsurerProfile;

        // "Similar estimates" or "estimates like this"
        if (Regex.IsMatch(input, @"similar|like this|looks like", RegexOptions.IgnoreCase))
            return QueryType.SimilarEstimates;

        // "Statistics" or "overview" or "summary"
        if (Regex.IsMatch(input, @"statistic|overview|summary|how many estimate", RegexOptions.IgnoreCase))
            return QueryType.OverallStats;

        // "Find/show estimates with/where X"
        if (Regex.IsMatch(input, @"(find|show|search).+estimate", RegexOptions.IgnoreCase))
            return QueryType.EstimateSearch;

        // Default: insurer + operation query
        var insurer = ExtractInsurer(input);
        var operation = ExtractOperation(input);
        if (!string.IsNullOrEmpty(insurer) && !string.IsNullOrEmpty(operation))
            return QueryType.InsurerOperationPayment;

        if (!string.IsNullOrEmpty(insurer))
            return QueryType.InsurerProfile;

        return QueryType.Unknown;
    }

    #region Query Handlers

    /// <summary>
    /// Handle: "What estimate did Allstate pay corrosion protection?"
    /// </summary>
    private EstimateQueryResult HandleInsurerOperationQuery(string input)
    {
        var insurer = ExtractInsurer(input);
        var operation = ExtractOperation(input);

        if (string.IsNullOrEmpty(insurer) || string.IsNullOrEmpty(operation))
        {
            return new EstimateQueryResult
            {
                Success = false,
                Message = "I couldn't identify the insurance company or operation. Try: \"Show estimates where Allstate paid corrosion protection\""
            };
        }

        var results = _historyDb.FindEstimatesWithOperation(insurer, operation);

        if (results.Count == 0)
        {
            return new EstimateQueryResult
            {
                Success = true,
                Message = $"No estimates found where **{insurer}** paid for **{operation}**.\n\n" +
                         $"You have {_historyDb.EstimateCount} estimates in your history.\n" +
                         "Upload more estimates to build your insurance payment database!"
            };
        }

        var response = $"**{insurer}** paid for **{operation}** in {results.Count} estimate(s):\n\n";

        foreach (var result in results.Take(5))
        {
            response += $"**{result.Estimate.VehicleInfo}** ({result.Estimate.ImportedDate:MM/dd/yyyy})\n";
            response += $"RO: {result.Estimate.RONumber} | Total: ${result.Estimate.GrandTotal:N0}\n";

            foreach (var item in result.MatchingLineItems.Take(3))
            {
                response += $"  • {item.Description}";
                if (item.LaborHours > 0) response += $" - {item.LaborHours}hrs";
                if (item.Price > 0) response += $" ${item.Price:N2}";
                response += "\n";
            }
            response += "\n";
        }

        if (results.Count > 5)
            response += $"... and {results.Count - 5} more estimates\n";

        return new EstimateQueryResult
        {
            Success = true,
            Message = response,
            Estimates = results.Select(r => r.Estimate).ToList()
        };
    }

    /// <summary>
    /// Handle: "How many times did State Farm pay for pre-scan?"
    /// </summary>
    private EstimateQueryResult HandlePaymentCountQuery(string input)
    {
        var insurer = ExtractInsurer(input);
        var operation = ExtractOperation(input);

        if (string.IsNullOrEmpty(insurer) || string.IsNullOrEmpty(operation))
        {
            return new EstimateQueryResult
            {
                Success = false,
                Message = "I couldn't identify the insurance company or operation. Try: \"How many times did Allstate pay for corrosion protection?\""
            };
        }

        var stats = _historyDb.GetOperationPaymentStats(insurer, operation);

        if (stats.TimesPaid == 0)
        {
            return new EstimateQueryResult
            {
                Success = true,
                Message = $"**{insurer}** has not paid for **{operation}** in any of your {_historyDb.EstimateCount} stored estimates.\n\n" +
                         "This doesn't mean they won't pay - you may not have that data yet!\n" +
                         "Upload more estimates to build your payment history."
            };
        }

        var response = $"**{insurer}** has paid for **{operation}**:\n\n";
        response += $"• **{stats.TimesPaid} time(s)** across your estimates\n";

        if (stats.AverageHours > 0)
            response += $"• Average hours: **{stats.AverageHours:N1}**\n";

        if (stats.TotalAmountPaid > 0)
            response += $"• Total paid: **${stats.TotalAmountPaid:N2}**\n";

        response += $"\n*Based on {_historyDb.EstimateCount} estimates in your history*";

        return new EstimateQueryResult
        {
            Success = true,
            Message = response,
            PaymentStats = stats
        };
    }

    /// <summary>
    /// Handle: "What does Allstate typically pay for?"
    /// </summary>
    private EstimateQueryResult HandleInsurerProfileQuery(string input)
    {
        var insurer = ExtractInsurer(input);

        if (string.IsNullOrEmpty(insurer))
        {
            return new EstimateQueryResult
            {
                Success = false,
                Message = "Which insurance company? Try: \"What does Allstate typically pay for?\""
            };
        }

        var profile = _historyDb.GetInsurerPaymentProfile(insurer);

        if (profile.Count == 0)
        {
            // Check if we have any estimates for this insurer
            var estimates = _historyDb.SearchEstimates(new EstimateSearchCriteria { InsuranceCompany = insurer });

            if (estimates.Count == 0)
            {
                return new EstimateQueryResult
                {
                    Success = true,
                    Message = $"No estimates found for **{insurer}** in your history.\n\n" +
                             $"Known insurers in your database: {string.Join(", ", _historyDb.KnownInsurers.Take(10))}"
                };
            }

            return new EstimateQueryResult
            {
                Success = true,
                Message = $"Found {estimates.Count} estimate(s) for **{insurer}** but no payment patterns indexed yet.\n" +
                         "The more estimates you upload, the better the payment tracking becomes!"
            };
        }

        var response = $"**{insurer}** Payment Profile:\n\n";

        // Sort by times paid
        var topOps = profile.OrderByDescending(kvp => kvp.Value.TimesPaid).Take(15);

        foreach (var kvp in topOps)
        {
            var stats = kvp.Value;
            response += $"• **{stats.OperationDescription}**: {stats.TimesPaid}x";
            if (stats.AverageHours > 0) response += $" (avg {stats.AverageHours:N1}hrs)";
            response += "\n";
        }

        var estimateCount = _historyDb.SearchEstimates(new EstimateSearchCriteria { InsuranceCompany = insurer }).Count;
        response += $"\n*Based on {estimateCount} estimates from {insurer}*";

        return new EstimateQueryResult
        {
            Success = true,
            Message = response
        };
    }

    /// <summary>
    /// Handle: "Who pays for corrosion protection?"
    /// </summary>
    private EstimateQueryResult HandleWhoPaysPorQuery(string input)
    {
        var operation = ExtractOperation(input);

        if (string.IsNullOrEmpty(operation))
        {
            return new EstimateQueryResult
            {
                Success = false,
                Message = "What operation? Try: \"Who pays for corrosion protection?\" or \"Which insurers pay for pre-scan?\""
            };
        }

        var insurerStats = new Dictionary<string, int>();

        foreach (var insurer in _historyDb.KnownInsurers)
        {
            var stats = _historyDb.GetOperationPaymentStats(insurer, operation);
            if (stats.TimesPaid > 0)
                insurerStats[insurer] = stats.TimesPaid;
        }

        if (insurerStats.Count == 0)
        {
            return new EstimateQueryResult
            {
                Success = true,
                Message = $"No insurers have paid for **{operation}** in your estimate history.\n\n" +
                         "This doesn't mean they won't - you may just not have enough data yet!\n" +
                         $"Upload more estimates to build your payment database."
            };
        }

        var response = $"**Insurers that have paid for \"{operation}\":**\n\n";

        foreach (var kvp in insurerStats.OrderByDescending(k => k.Value))
        {
            response += $"• **{kvp.Key}**: {kvp.Value} time(s)\n";
        }

        response += $"\n*Based on {_historyDb.EstimateCount} estimates in your history*";

        return new EstimateQueryResult
        {
            Success = true,
            Message = response
        };
    }

    /// <summary>
    /// Handle: "Show my estimate statistics"
    /// </summary>
    private EstimateQueryResult HandleStatsQuery(string input)
    {
        var stats = _historyDb.GetOverallStatistics();

        if (stats.TotalEstimates == 0)
        {
            return new EstimateQueryResult
            {
                Success = true,
                Message = "**Estimate History Database**\n\n" +
                         "No estimates stored yet!\n\n" +
                         "**How to build your database:**\n" +
                         "1. Import estimates from the Upload tab\n" +
                         "2. Each parsed estimate is automatically saved\n" +
                         "3. Ask questions like \"Did Allstate pay for corrosion protection?\"\n\n" +
                         "The more estimates you upload, the smarter this becomes!"
            };
        }

        var response = "**Your Estimate History Database**\n\n";
        response += $"• **{stats.TotalEstimates}** estimates stored\n";
        response += $"• **{stats.TotalLineItems}** total line items\n";
        response += $"• **{stats.TotalManualLines}** manual/# lines ({(stats.TotalLineItems > 0 ? (100.0 * stats.TotalManualLines / stats.TotalLineItems) : 0):N1}%)\n";
        response += $"• **${stats.TotalValue:N0}** total estimate value\n";
        response += $"• **{stats.UniqueInsurers}** insurance companies\n";
        response += $"• Average complexity: **{stats.AverageComplexity:N0}**/100\n\n";

        if (stats.TopInsurers.Count > 0)
        {
            response += "**Top Insurers:**\n";
            foreach (var kvp in stats.TopInsurers.Take(5))
            {
                response += $"• {kvp.Key}: {kvp.Value} estimates\n";
            }
            response += "\n";
        }

        if (stats.TopOperations.Count > 0)
        {
            response += "**Most Common Manual Operations:**\n";
            foreach (var kvp in stats.TopOperations.Take(5))
            {
                response += $"• {kvp.Key}: {kvp.Value}x\n";
            }
        }

        return new EstimateQueryResult
        {
            Success = true,
            Message = response,
            Statistics = stats
        };
    }

    /// <summary>
    /// Handle: "Find estimates with bumper damage"
    /// </summary>
    private EstimateQueryResult HandleSearchQuery(string input)
    {
        var criteria = new EstimateSearchCriteria();

        // Extract search criteria
        var insurer = ExtractInsurer(input);
        if (!string.IsNullOrEmpty(insurer))
            criteria.InsuranceCompany = insurer;

        var operation = ExtractOperation(input);
        if (!string.IsNullOrEmpty(operation))
            criteria.OperationKeyword = operation;

        // Extract vehicle keywords
        var vehiclePatterns = new[] { "honda", "toyota", "ford", "chevy", "chevrolet", "bmw", "mercedes",
            "lexus", "acura", "nissan", "hyundai", "kia", "mazda", "subaru" };
        foreach (var pattern in vehiclePatterns)
        {
            if (input.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                criteria.VehicleKeyword = pattern;
                break;
            }
        }

        // Extract damage zone keywords
        var damageZones = new[] { "bumper", "fender", "hood", "door", "quarter", "roof", "trunk",
            "headlamp", "taillight", "mirror", "windshield" };
        foreach (var zone in damageZones)
        {
            if (input.Contains(zone, StringComparison.OrdinalIgnoreCase))
            {
                criteria.OperationKeyword ??= zone;
                break;
            }
        }

        var results = _historyDb.SearchEstimates(criteria);

        if (results.Count == 0)
        {
            return new EstimateQueryResult
            {
                Success = true,
                Message = "No estimates found matching your criteria.\n\n" +
                         $"Your database has {_historyDb.EstimateCount} estimates.\n" +
                         "Try different keywords or upload more estimates!"
            };
        }

        var response = $"**Found {results.Count} estimate(s):**\n\n";

        foreach (var estimate in results.Take(5))
        {
            response += $"**{estimate.VehicleInfo}**\n";
            response += $"• {estimate.InsuranceCompany} | {estimate.ImportedDate:MM/dd/yyyy}\n";
            response += $"• Total: ${estimate.GrandTotal:N0} | Complexity: {estimate.DNA.ComplexityScore}/100\n";

            if (estimate.DNA.DamageZones.Count > 0)
                response += $"• Damage: {string.Join(", ", estimate.DNA.DamageZones.Take(4))}\n";

            response += "\n";
        }

        if (results.Count > 5)
            response += $"*... and {results.Count - 5} more estimates*";

        return new EstimateQueryResult
        {
            Success = true,
            Message = response,
            Estimates = results
        };
    }

    /// <summary>
    /// Handle: "Find estimates similar to this one"
    /// </summary>
    private EstimateQueryResult HandleSimilarEstimatesQuery(string input)
    {
        // For now, return guidance on how to use similar estimates
        // In the future, this could work with the currently loaded estimate

        return new EstimateQueryResult
        {
            Success = true,
            Message = "**Similar Estimate Finder**\n\n" +
                     "To find similar estimates, upload an estimate first, then ask:\n" +
                     "• \"Find estimates like this one\"\n" +
                     "• \"Show similar past estimates\"\n\n" +
                     "The system will match based on:\n" +
                     "• Same insurance company\n" +
                     "• Similar vehicle type\n" +
                     "• Same damage zones\n" +
                     "• Similar complexity score\n\n" +
                     $"Your database has {_historyDb.EstimateCount} estimates to compare against."
        };
    }

    #endregion

    #region Extraction Helpers

    private string ExtractInsurer(string input)
    {
        foreach (var insurer in _knownInsurers)
        {
            if (input.Contains(insurer, StringComparison.OrdinalIgnoreCase))
                return insurer;
        }

        // Also check partial matches
        if (input.Contains("state farm", StringComparison.OrdinalIgnoreCase))
            return "State Farm";
        if (input.Contains("liberty", StringComparison.OrdinalIgnoreCase))
            return "Liberty Mutual";
        if (input.Contains("american family", StringComparison.OrdinalIgnoreCase))
            return "American Family";

        return "";
    }

    private string ExtractOperation(string input)
    {
        // Sort by length descending to match longer phrases first
        foreach (var op in _operationKeywords.OrderByDescending(o => o.Length))
        {
            if (input.Contains(op, StringComparison.OrdinalIgnoreCase))
                return op;
        }

        return "";
    }

    #endregion
}

#region Result Models

public class EstimateQueryResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<StoredEstimate>? Estimates { get; set; }
    public OperationPaymentSummary? PaymentStats { get; set; }
    public HistoryStatistics? Statistics { get; set; }
}

#endregion
