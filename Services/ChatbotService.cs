using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services;

/// <summary>
/// Service for handling chatbot queries against the local knowledge base.
/// Uses keyword matching and fuzzy search to find relevant answers.
/// Now integrated with ExcelKnowledgeService for spreadsheet-aware help.
/// Also includes Definitions knowledge for P-page and operation lookups.
/// </summary>
public class ChatbotService
{
    private KnowledgeBase? _knowledgeBase;
    private DefinitionsData? _definitionsData;
    private OEMStatementsData? _oemStatementsData;
    private OperationsData? _operationsData;
    private IncludedNotIncludedData? _includedNotIncludedData;
    private List<EstimatingNote>? _estimatingNotes;
    private readonly ExcelKnowledgeService _excelKnowledge;
    private readonly EstimateLearningService _learningService;
    private readonly ScanningKnowledgeService _scanningKnowledge;
    private readonly EstimateAccuracyService _accuracyService;
    private readonly EstimateQueryService _estimateQueryService;
    private bool _isLoaded;

    public bool IsLoaded => _isLoaded;
    public List<string> QuickReplies => _knowledgeBase?.QuickReplies ?? new List<string>();

    public ChatbotService()
    {
        _excelKnowledge = new ExcelKnowledgeService();
        _learningService = EstimateLearningService.Instance;
        _scanningKnowledge = ScanningKnowledgeService.Instance;
        _accuracyService = EstimateAccuracyService.Instance;
        _estimateQueryService = EstimateQueryService.Instance;
        LoadKnowledgeBase();
        LoadDefinitions();
        LoadOEMStatements();
        LoadOperations();
        LoadIncludedNotIncluded();
        LoadEstimatingNotes();
    }

    private void LoadKnowledgeBase()
    {
        try
        {
            var basePath = AppContext.BaseDirectory;
            var kbPath = Path.Combine(basePath, "Data", "KnowledgeBase.json");

            if (!File.Exists(kbPath))
            {
                // Try development path
                kbPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "KnowledgeBase.json");
            }

            if (File.Exists(kbPath))
            {
                var json = File.ReadAllText(kbPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                _knowledgeBase = JsonSerializer.Deserialize<KnowledgeBase>(json, options);
                _isLoaded = _knowledgeBase != null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load knowledge base: {ex.Message}");
            _isLoaded = false;
        }
    }

    private void LoadDefinitions()
    {
        try
        {
            var basePath = AppContext.BaseDirectory;
            var defPath = Path.Combine(basePath, "Data", "Definitions.json");

            if (!File.Exists(defPath))
            {
                defPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Definitions.json");
            }

            if (File.Exists(defPath))
            {
                var json = File.ReadAllText(defPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                _definitionsData = JsonSerializer.Deserialize<DefinitionsData>(json, options);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load definitions: {ex.Message}");
        }
    }

    private void LoadOEMStatements()
    {
        try
        {
            var basePath = AppContext.BaseDirectory;
            var oemPath = Path.Combine(basePath, "Data", "OEMPositionStatements.json");

            if (!File.Exists(oemPath))
            {
                oemPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "OEMPositionStatements.json");
            }

            if (File.Exists(oemPath))
            {
                var json = File.ReadAllText(oemPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                _oemStatementsData = JsonSerializer.Deserialize<OEMStatementsData>(json, options);
                System.Diagnostics.Debug.WriteLine($"Loaded {_oemStatementsData?.Statements?.Count ?? 0} OEM position statements");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load OEM statements: {ex.Message}");
        }
    }

    private void LoadOperations()
    {
        try
        {
            var basePath = AppContext.BaseDirectory;
            var opsPath = Path.Combine(basePath, "Data", "Operations.json");

            if (!File.Exists(opsPath))
            {
                opsPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Operations.json");
            }

            if (File.Exists(opsPath))
            {
                var json = File.ReadAllText(opsPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                _operationsData = JsonSerializer.Deserialize<OperationsData>(json, options);
                System.Diagnostics.Debug.WriteLine($"Loaded {_operationsData?.Operations?.Count ?? 0} SCRS operations");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load SCRS operations: {ex.Message}");
        }
    }

    private void LoadIncludedNotIncluded()
    {
        try
        {
            var basePath = AppContext.BaseDirectory;
            var path = Path.Combine(basePath, "Data", "IncludedNotIncluded.json");

            if (!File.Exists(path))
            {
                path = Path.Combine(Directory.GetCurrentDirectory(), "Data", "IncludedNotIncluded.json");
            }

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                _includedNotIncludedData = JsonSerializer.Deserialize<IncludedNotIncludedData>(json, options);
                System.Diagnostics.Debug.WriteLine($"Loaded {_includedNotIncludedData?.Operations?.Count ?? 0} included/not-included operations");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load included/not-included data: {ex.Message}");
        }
    }

    private void LoadEstimatingNotes()
    {
        try
        {
            var basePath = AppContext.BaseDirectory;
            var notesPath = Path.Combine(basePath, "Data", "EstimatingNotes.json");

            if (!File.Exists(notesPath))
            {
                notesPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "EstimatingNotes.json");
            }

            if (File.Exists(notesPath))
            {
                var json = File.ReadAllText(notesPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                _estimatingNotes = JsonSerializer.Deserialize<List<EstimatingNote>>(json, options);
                System.Diagnostics.Debug.WriteLine($"Loaded {_estimatingNotes?.Count ?? 0} estimating notes");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load estimating notes: {ex.Message}");
        }
    }

    /// <summary>
    /// Search estimating notes knowledge base for general estimating questions
    /// </summary>
    private ChatResponse? SearchEstimatingNotes(string input)
    {
        if (_estimatingNotes == null || _estimatingNotes.Count == 0)
            return null;

        var inputLower = input.ToLowerInvariant();
        var words = inputLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Find best matching notes
        var matches = new List<(EstimatingNote note, int score)>();

        foreach (var note in _estimatingNotes)
        {
            var termLower = note.Term?.ToLowerInvariant() ?? "";
            var contentLower = note.Content?.ToLowerInvariant() ?? "";
            int score = 0;

            // Exact term match gets highest score
            if (inputLower.Contains(termLower) && termLower.Length > 3)
                score += 100;

            // Word matches in term
            foreach (var word in words)
            {
                if (word.Length < 3) continue;
                if (termLower.Contains(word))
                    score += 20;
                if (contentLower.Contains(word))
                    score += 5;
            }

            if (score > 30)
                matches.Add((note, score));
        }

        if (matches.Count == 0)
            return null;

        // Sort by score and take best matches
        var bestMatches = matches.OrderByDescending(m => m.score).Take(3).ToList();
        var best = bestMatches[0];

        // Build response
        var response = $"**{best.note.Term}**\n\n{best.note.Content}";

        // Add related topics if multiple matches
        if (bestMatches.Count > 1)
        {
            response += "\n\n**Related topics:**\n";
            foreach (var related in bestMatches.Skip(1))
            {
                response += $"• {related.note.Term}\n";
            }
        }

        return new ChatResponse
        {
            Message = response,
            Confidence = Math.Min(best.score / 100.0, 1.0),
            Category = "estimating-notes"
        };
    }

    /// <summary>
    /// Search for terminology, calculations, blend formulas, and order of operations queries
    /// </summary>
    private ChatResponse? SearchTerminologyAndCalculations(string input)
    {
        if (_includedNotIncludedData == null)
            return null;

        var inputLower = input.ToLowerInvariant();

        // Check for terminology queries
        if (inputLower.Contains("what does") || inputLower.Contains("what is") ||
            inputLower.Contains("meaning of") || inputLower.Contains("abbreviation") ||
            inputLower.Contains("terminology") || inputLower.Contains("r&i") ||
            inputLower.Contains("r&r") || inputLower.Contains("repl") ||
            inputLower.Contains("rfn") || inputLower.Contains("o/h"))
        {
            var terminology = _includedNotIncludedData.Terminology;
            if (terminology != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**Estimating Terminology:**\n");

                // Check operation types
                if (terminology.OperationTypes != null)
                {
                    bool foundMatch = false;
                    foreach (var kvp in terminology.OperationTypes)
                    {
                        if (inputLower.Contains(kvp.Key.ToLower()))
                        {
                            sb.AppendLine($"**{kvp.Key}**: {kvp.Value}");
                            foundMatch = true;
                        }
                    }

                    if (!foundMatch && (inputLower.Contains("operation type") || inputLower.Contains("terminology")))
                    {
                        sb.AppendLine("**Operation Types:**");
                        foreach (var kvp in terminology.OperationTypes)
                        {
                            sb.AppendLine($"• **{kvp.Key}**: {kvp.Value}");
                        }
                    }
                }

                // Check labor types
                if (terminology.LaborTypes != null && (inputLower.Contains("labor") || inputLower.Contains("type")))
                {
                    sb.AppendLine("\n**Labor Types:**");
                    foreach (var kvp in terminology.LaborTypes)
                    {
                        sb.AppendLine($"• **{kvp.Key}**: {kvp.Value}");
                    }
                }

                // Check side designations
                if (terminology.SideDesignations != null && (inputLower.Contains("side") || inputLower.Contains("lt") || inputLower.Contains("rt") || inputLower.Contains("rf") || inputLower.Contains("rr") || inputLower.Contains("lf") || inputLower.Contains("lr")))
                {
                    sb.AppendLine("\n**Side Designations:**");
                    foreach (var kvp in terminology.SideDesignations)
                    {
                        sb.AppendLine($"• **{kvp.Key}**: {kvp.Value}");
                    }
                }

                if (sb.Length > 30)
                {
                    return new ChatResponse
                    {
                        Message = sb.ToString(),
                        Confidence = 0.9,
                        Category = "terminology",
                        RelatedTopics = new List<string> { "blend formulas", "order of operations", "masking times" }
                    };
                }
            }
        }

        // Check for blend/overlap calculation queries
        if (inputLower.Contains("blend") || inputLower.Contains("overlap") ||
            inputLower.Contains("adjacent") || inputLower.Contains("how much to add"))
        {
            var calculations = _includedNotIncludedData.Calculations;
            if (calculations?.BlendFormulas != null || calculations?.OverlapDeductions != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**Blend & Overlap Calculations:**\n");

                if (calculations.BlendFormulas != null)
                {
                    sb.AppendLine($"{calculations.BlendFormulas.Description}\n");

                    if (calculations.BlendFormulas.LargePanels != null)
                    {
                        sb.AppendLine("**Large Panels** (Hood, Roof, Trunk, Spoiler):");
                        sb.AppendLine($"• First panel: {calculations.BlendFormulas.LargePanels.FirstPanel}");
                        sb.AppendLine($"• Each additional: {calculations.BlendFormulas.LargePanels.AdditionalPanels}\n");
                    }

                    if (calculations.BlendFormulas.StandardPanels != null)
                    {
                        sb.AppendLine("**Standard Panels** (Fender, Door, Quarter):");
                        sb.AppendLine($"• First panel: {calculations.BlendFormulas.StandardPanels.FirstPanel}");
                        sb.AppendLine($"• Each additional: {calculations.BlendFormulas.StandardPanels.AdditionalPanels}\n");
                    }

                    if (calculations.BlendFormulas.BumperCover != null)
                    {
                        sb.AppendLine("**Bumper Cover:**");
                        sb.AppendLine($"• First panel: {calculations.BlendFormulas.BumperCover.FirstPanel}");
                        if (!string.IsNullOrEmpty(calculations.BlendFormulas.BumperCover.Notes))
                            sb.AppendLine($"• Note: {calculations.BlendFormulas.BumperCover.Notes}");
                    }
                }

                if (calculations.OverlapDeductions?.MajorAdjacentPanel != null)
                {
                    sb.AppendLine($"\n**Overlap Deduction:**");
                    sb.AppendLine($"• Major adjacent panel: {calculations.OverlapDeductions.MajorAdjacentPanel.Deduction} {calculations.OverlapDeductions.MajorAdjacentPanel.Unit}");
                    sb.AppendLine($"• {calculations.OverlapDeductions.MajorAdjacentPanel.Notes}");
                }

                return new ChatResponse
                {
                    Message = sb.ToString(),
                    Confidence = 0.95,
                    Category = "calculations",
                    RelatedTopics = new List<string> { "masking times", "tri-coat additive", "labor times" }
                };
            }
        }

        // Check for masking time queries
        if (inputLower.Contains("mask") || inputLower.Contains("masking"))
        {
            var calculations = _includedNotIncludedData.Calculations;
            if (calculations?.MaskingFormulas?.Openings != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**Masking Time Formulas:**\n");
                sb.AppendLine($"{calculations.MaskingFormulas.Description}\n");

                foreach (var mask in calculations.MaskingFormulas.Openings)
                {
                    sb.AppendLine($"• **{mask.Area}**: {mask.Hours} hrs ({mask.TypicalCost})");
                }

                return new ChatResponse
                {
                    Message = sb.ToString(),
                    Confidence = 0.95,
                    Category = "calculations",
                    RelatedTopics = new List<string> { "blend formulas", "labor times" }
                };
            }
        }

        // Check for order of operations query
        if (inputLower.Contains("order of operation") || inputLower.Contains("order of repair") ||
            inputLower.Contains("repair order") || inputLower.Contains("what order") ||
            inputLower.Contains("sequence") || inputLower.Contains("phases"))
        {
            var order = _includedNotIncludedData.OrderOfOperations;
            if (order?.Phases != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**Order of Operations (Collision Repair):**\n");
                sb.AppendLine($"{order.Description}\n");

                foreach (var phase in order.Phases)
                {
                    sb.AppendLine($"**Phase {phase.Phase}: {phase.Name}**");
                    if (phase.Operations != null)
                    {
                        foreach (var op in phase.Operations)
                        {
                            sb.AppendLine($"  • {op}");
                        }
                    }
                    sb.AppendLine();
                }

                return new ChatResponse
                {
                    Message = sb.ToString(),
                    Confidence = 0.95,
                    Category = "order-of-operations",
                    RelatedTopics = new List<string> { "blend formulas", "terminology" }
                };
            }
        }

        // Check for tri-coat/three-stage query
        if (inputLower.Contains("tri-coat") || inputLower.Contains("tricoat") ||
            inputLower.Contains("three stage") || inputLower.Contains("3-stage") ||
            inputLower.Contains("three-stage") || inputLower.Contains("pearl"))
        {
            var calculations = _includedNotIncludedData.Calculations;
            if (calculations?.TriCoatAdditive != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**Tri-Coat / Three-Stage Paint:**\n");
                sb.AppendLine($"{calculations.TriCoatAdditive.Description}\n");
                sb.AppendLine($"**Formula:** {calculations.TriCoatAdditive.Formula}");
                sb.AppendLine($"**Example:** {calculations.TriCoatAdditive.Example}");

                return new ChatResponse
                {
                    Message = sb.ToString(),
                    Confidence = 0.95,
                    Category = "calculations",
                    RelatedTopics = new List<string> { "blend formulas", "color tinting" }
                };
            }
        }

        // Check for refrigerant/coolant queries
        if (inputLower.Contains("refrigerant") || inputLower.Contains("freon") ||
            inputLower.Contains("r-134") || inputLower.Contains("r134") ||
            inputLower.Contains("r-1234") || inputLower.Contains("r1234") ||
            inputLower.Contains("a/c") || inputLower.Contains(" ac ") ||
            inputLower.Contains("air conditioning"))
        {
            return GetRefrigerantResponse(inputLower);
        }

        // Check for coolant queries
        if (inputLower.Contains("coolant") || inputLower.Contains("antifreeze") ||
            inputLower.Contains("radiator fluid") || inputLower.Contains("engine coolant"))
        {
            return GetCoolantResponse();
        }

        // Check for ADAS queries
        if (inputLower.Contains("adas") || inputLower.Contains("advanced driver") ||
            inputLower.Contains("driver assist") ||
            (inputLower.Contains("what is") && (inputLower.Contains("calibration") || inputLower.Contains("radar") || inputLower.Contains("camera"))) ||
            (inputLower.Contains("blind spot") && !inputLower.Contains("operations")) ||
            (inputLower.Contains("lane") && (inputLower.Contains("departure") || inputLower.Contains("keep"))) ||
            inputLower.Contains("forward collision") || inputLower.Contains("automatic emergency") ||
            inputLower.Contains("adaptive cruise"))
        {
            return GetADASResponse(inputLower);
        }

        // Check for common labor times query
        if (inputLower.Contains("labor time") || inputLower.Contains("how many hours") ||
            inputLower.Contains("typical time") || inputLower.Contains("typical hours"))
        {
            var calculations = _includedNotIncludedData.Calculations;
            if (calculations?.CommonLaborTimes != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**Common Labor Times (from real estimates):**\n");
                sb.AppendLine($"{calculations.CommonLaborTimes.Description}\n");

                if (calculations.CommonLaborTimes.BodyOperations != null)
                {
                    sb.AppendLine("**Body Operations:**");
                    foreach (var op in calculations.CommonLaborTimes.BodyOperations)
                    {
                        if (op.Hours > 0)
                            sb.AppendLine($"• {op.Operation}: {op.Hours} hrs ({op.LaborType})");
                        else if (op.BodyHours > 0)
                            sb.AppendLine($"• {op.Operation}: {op.BodyHours} hrs Body + {op.RefinishHours} hrs Rfn");
                    }
                }

                if (calculations.CommonLaborTimes.RefinishOperations != null)
                {
                    sb.AppendLine("\n**Refinish Operations:**");
                    foreach (var op in calculations.CommonLaborTimes.RefinishOperations)
                    {
                        var cost = !string.IsNullOrEmpty(op.TypicalCost) ? $" ({op.TypicalCost})" : "";
                        var notes = !string.IsNullOrEmpty(op.Notes) ? $" - {op.Notes}" : "";
                        sb.AppendLine($"• {op.Operation}: {op.Hours} hrs{cost}{notes}");
                    }
                }

                return new ChatResponse
                {
                    Message = sb.ToString(),
                    Confidence = 0.9,
                    Category = "calculations",
                    RelatedTopics = new List<string> { "blend formulas", "masking times" }
                };
            }
        }

        // Check for manual entry / # operations query
        if (inputLower.Contains("manual entry") || inputLower.Contains("manual entries") ||
            inputLower.Contains("pound sign") || inputLower.Contains("hashtag") ||
            inputLower.Contains("# operation") || inputLower.Contains("what should i add") ||
            inputLower.Contains("add lines") || inputLower.Contains("add to estimate"))
        {
            var calculations = _includedNotIncludedData.Calculations;
            if (calculations?.ManualEntryOperations != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**Manual Entry Operations (#):**\n");
                sb.AppendLine("These are NOT INCLUDED in database times. You must add them as manual line items:\n");

                var manualOps = calculations.ManualEntryOperations;

                if (manualOps.RefinishOperations != null && manualOps.RefinishOperations.Count > 0)
                {
                    sb.AppendLine("**Refinish Manual Entries:**");
                    foreach (var op in manualOps.RefinishOperations)
                    {
                        var hours = op.TypicalHours ?? "";
                        var when = !string.IsNullOrEmpty(op.When) ? $" (When: {op.When})" : "";
                        sb.AppendLine($"• {op.Operation}: {hours} hrs{when}");
                    }
                    sb.AppendLine();
                }

                if (manualOps.CorrosionOperations != null && manualOps.CorrosionOperations.Count > 0)
                {
                    sb.AppendLine("**Corrosion Manual Entries:**");
                    foreach (var op in manualOps.CorrosionOperations)
                    {
                        var hours = op.TypicalHours ?? "";
                        var when = !string.IsNullOrEmpty(op.When) ? $" (When: {op.When})" : "";
                        sb.AppendLine($"• {op.Operation}: {hours} hrs{when}");
                    }
                    sb.AppendLine();
                }

                if (manualOps.AdditionalOperations != null && manualOps.AdditionalOperations.Count > 0)
                {
                    sb.AppendLine("**Other Manual Entries:**");
                    foreach (var op in manualOps.AdditionalOperations)
                    {
                        var hours = !string.IsNullOrEmpty(op.TypicalHours) ? $": {op.TypicalHours} hrs" : "";
                        var when = !string.IsNullOrEmpty(op.When) ? $" (When: {op.When})" : "";
                        sb.AppendLine($"• {op.Operation}{hours}{when}");
                    }
                }

                return new ChatResponse
                {
                    Message = sb.ToString(),
                    Confidence = 0.95,
                    Category = "manual-entries",
                    RelatedTopics = new List<string> { "not included operations", "feather prime block", "corrosion" }
                };
            }
        }

        // Check for specific manual entry operation queries
        if (inputLower.Contains("feather") || inputLower.Contains("prime") || inputLower.Contains("block") ||
            inputLower.Contains("denib") || inputLower.Contains("de-nib") || inputLower.Contains("buff"))
        {
            var calculations = _includedNotIncludedData.Calculations;
            if (calculations?.ManualEntryOperations?.RefinishOperations != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**Refinish Manual Entry Operations:**\n");
                sb.AppendLine("These are NOT INCLUDED - add as manual line items (#):\n");

                foreach (var op in calculations.ManualEntryOperations.RefinishOperations)
                {
                    var searchTerms = op.Operation?.ToLowerInvariant() ?? "";
                    bool matches = (inputLower.Contains("feather") && searchTerms.Contains("feather")) ||
                                   (inputLower.Contains("prime") && searchTerms.Contains("prime")) ||
                                   (inputLower.Contains("block") && searchTerms.Contains("block")) ||
                                   (inputLower.Contains("denib") && (searchTerms.Contains("denib") || searchTerms.Contains("de-nib"))) ||
                                   (inputLower.Contains("buff") && searchTerms.Contains("buff"));

                    if (matches || (!inputLower.Contains("feather") && !inputLower.Contains("denib")))
                    {
                        var hours = op.TypicalHours ?? "";
                        var when = !string.IsNullOrEmpty(op.When) ? $"\n  When: {op.When}" : "";
                        sb.AppendLine($"• **{op.Operation}**: {hours} hrs{when}");
                    }
                }

                return new ChatResponse
                {
                    Message = sb.ToString(),
                    Confidence = 0.9,
                    Category = "manual-entries",
                    RelatedTopics = new List<string> { "manual entries", "corrosion protection" }
                };
            }
        }

        // Check for corrosion-related manual entry queries
        if (inputLower.Contains("corrosion") || inputLower.Contains("weld-through") || inputLower.Contains("weld thru") ||
            inputLower.Contains("cavity wax") || inputLower.Contains("anti-corrosion"))
        {
            var calculations = _includedNotIncludedData.Calculations;
            if (calculations?.ManualEntryOperations?.CorrosionOperations != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**Corrosion Protection Manual Entries:**\n");
                sb.AppendLine("These are NOT INCLUDED - add as manual line items (#):\n");

                foreach (var op in calculations.ManualEntryOperations.CorrosionOperations)
                {
                    var hours = op.TypicalHours ?? "";
                    var when = !string.IsNullOrEmpty(op.When) ? $"\n  When: {op.When}" : "";
                    sb.AppendLine($"• **{op.Operation}**: {hours} hrs{when}");
                }

                return new ChatResponse
                {
                    Message = sb.ToString(),
                    Confidence = 0.95,
                    Category = "manual-entries",
                    RelatedTopics = new List<string> { "manual entries", "welded panel replacement" }
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Search for included/not-included questions about specific operations
    /// Handles: "what's included with replacing a quarter panel", "what's not included with bumper replace"
    /// </summary>
    private ChatResponse? SearchIncludedNotIncluded(string input)
    {
        if (_includedNotIncludedData?.Operations == null || _includedNotIncludedData.Operations.Count == 0)
            return null;

        var inputLower = input.ToLowerInvariant();

        // Check if this is an included/not-included question OR an operation search
        var isIncludedQuery = inputLower.Contains("included") ||
                              inputLower.Contains("not included") ||
                              inputLower.Contains("what's included") ||
                              inputLower.Contains("whats included") ||
                              inputLower.Contains("what is included") ||
                              inputLower.Contains("what comes with") ||
                              inputLower.Contains("what do i get") ||
                              inputLower.Contains("what's covered") ||
                              inputLower.Contains("what is covered") ||
                              inputLower.Contains("manual line") ||
                              inputLower.Contains("p-page") ||
                              inputLower.Contains("ppage") ||
                              inputLower.Contains("motor guide");

        // Also trigger on operation-related queries (for MET lookup)
        var isOperationQuery = inputLower.Contains("operations for") ||
                               inputLower.Contains("operation for") ||
                               inputLower.Contains("i need operations") ||
                               inputLower.Contains("need operations") ||
                               inputLower.Contains("replacing") ||
                               inputLower.Contains("replace") ||
                               inputLower.Contains("repair") ||
                               inputLower.Contains("what operations") ||
                               inputLower.Contains("met operations") ||
                               inputLower.Contains("manual lines for") ||
                               inputLower.Contains("lines for");

        if (!isIncludedQuery && !isOperationQuery)
            return null;

        // Determine if asking specifically about not-included or included
        var askingNotIncluded = inputLower.Contains("not included") ||
                                inputLower.Contains("not covered") ||
                                inputLower.Contains("manual line") ||
                                inputLower.Contains("what else") ||
                                inputLower.Contains("what's missing");

        var askingIncluded = inputLower.Contains("what's included") ||
                             inputLower.Contains("whats included") ||
                             inputLower.Contains("what is included") ||
                             inputLower.Contains("what comes with") ||
                             inputLower.Contains("what's covered");

        // Find the best matching operation
        IncludedNotIncludedOperation? bestMatch = null;
        int bestScore = 0;

        foreach (var op in _includedNotIncludedData.Operations)
        {
            int score = 0;

            // Check part name
            if (!string.IsNullOrEmpty(op.PartName) && inputLower.Contains(op.PartName.ToLower()))
                score += 10;

            // Check aliases
            if (op.Aliases != null)
            {
                foreach (var alias in op.Aliases)
                {
                    if (inputLower.Contains(alias.ToLower()))
                        score += 8;
                }
            }

            // Check operation type
            if (!string.IsNullOrEmpty(op.OperationType))
            {
                if (inputLower.Contains(op.OperationType.ToLower()))
                    score += 5;
            }

            // Boost for welded if mentioned
            if (op.AttachmentType == "Welded" &&
                (inputLower.Contains("weld") || inputLower.Contains("structural")))
                score += 3;

            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = op;
            }
        }

        if (bestMatch == null || bestScore < 5)
            return null;

        // Build the response
        var response = new System.Text.StringBuilder();

        // Header
        response.AppendLine($"📋 **{bestMatch.PartName} - {bestMatch.OperationType}**");
        if (!string.IsNullOrEmpty(bestMatch.AttachmentType))
            response.AppendLine($"*Attachment: {bestMatch.AttachmentType}*");

        // Show source references
        if (bestMatch.SourceRefs != null)
        {
            var sources = new List<string>();
            if (!string.IsNullOrEmpty(bestMatch.SourceRefs.CccMotor))
                sources.Add($"CCC/MOTOR: {bestMatch.SourceRefs.CccMotor}");
            if (!string.IsNullOrEmpty(bestMatch.SourceRefs.Mitchell))
                sources.Add($"Mitchell: {bestMatch.SourceRefs.Mitchell}");
            if (bestMatch.SourceRefs.DegInquiries?.Count > 0)
                sources.Add($"DEG Inquiries: {string.Join(", ", bestMatch.SourceRefs.DegInquiries)}");
            if (sources.Count > 0)
                response.AppendLine($"📚 *Sources: {string.Join(" | ", sources)}*");
        }
        else if (!string.IsNullOrEmpty(bestMatch.PPageRef))
        {
            response.AppendLine($"*P-Page Ref: {bestMatch.PPageRef}*");
        }
        response.AppendLine();

        // Show what's included (if not specifically asking only for not-included)
        if (!askingNotIncluded || askingIncluded || (!askingIncluded && !askingNotIncluded))
        {
            if (bestMatch.Included != null && bestMatch.Included.Count > 0)
            {
                response.AppendLine("✅ **INCLUDED in labor time:**");
                foreach (var item in bestMatch.Included.Take(8))
                {
                    response.AppendLine($"  • {item}");
                }
                response.AppendLine();
            }
        }

        // Show what's NOT included
        if (bestMatch.NotIncluded != null && bestMatch.NotIncluded.Count > 0)
        {
            response.AppendLine("❌ **NOT INCLUDED (add as manual lines):**");
            foreach (var item in bestMatch.NotIncluded.Take(12))
            {
                response.AppendLine($"  • {item}");
            }
            response.AppendLine();
        }

        // Show MET suggested operations with hours and Excel references
        if (bestMatch.MetOperations != null && bestMatch.MetOperations.Count > 0)
        {
            response.AppendLine("🔧 **MET Suggested Manual Lines:**");
            foreach (var metOp in bestMatch.MetOperations)
            {
                var excelRef = "";
                if (!string.IsNullOrEmpty(metOp.MetRef))
                {
                    // Look up the actual cell reference from ExcelMappings
                    if (ExcelMappings.TryGetInput(metOp.MetRef, out var mapping))
                    {
                        excelRef = $" → [{mapping.Sheet}: {mapping.Cell}]";
                    }
                    else
                    {
                        // Fall back to showing the metRef key
                        excelRef = $" → [{metOp.MetRef}]";
                    }
                }
                else if (!string.IsNullOrEmpty(metOp.Source))
                {
                    // Show just the source sheet if no specific cell ref
                    excelRef = $" → [{metOp.Source}]";
                }
                response.AppendLine($"  • {metOp.Description} - {metOp.Hours:F1} hrs ({metOp.LaborType}){excelRef}");
            }
            response.AppendLine();
        }

        // Add triggers if available (for ADAS calibration, etc.)
        if (bestMatch.Triggers != null && bestMatch.Triggers.Count > 0)
        {
            response.AppendLine("⚡ **Calibration Triggers:**");
            foreach (var trigger in bestMatch.Triggers.Take(7))
            {
                response.AppendLine($"  • {trigger}");
            }
            response.AppendLine();
        }

        // Add SCRS notes if available
        if (!string.IsNullOrEmpty(bestMatch.ScrsNotes))
        {
            response.AppendLine($"📌 **SCRS Note:** {bestMatch.ScrsNotes}");
        }

        // Add related topics
        var relatedTopics = new List<string>();
        if (bestMatch.AttachmentType == "Welded")
        {
            relatedTopics.Add("Corrosion protection");
            relatedTopics.Add("Seam sealer");
            relatedTopics.Add("E-coat removal");
        }
        if (bestMatch.PartName?.ToLower().Contains("bumper") == true)
        {
            relatedTopics.Add("ADAS calibration");
            relatedTopics.Add("Parking sensors");
        }

        return new ChatResponse
        {
            Message = response.ToString().Trim(),
            Confidence = bestScore >= 10 ? 0.95 : 0.8,
            Category = "included_not_included",
            RelatedTopics = relatedTopics.Count > 0 ? relatedTopics : null
        };
    }

    /// <summary>
    /// Search SCRS operations for suggestions based on user query.
    /// Now combines standard operations with learned patterns from uploaded estimates.
    /// </summary>
    private ChatResponse? SearchOperations(string input)
    {
        var inputLower = input.ToLowerInvariant();

        // Extract part name for combined query
        string? partName = ExtractPartFromQuery(inputLower);

        // Keywords that indicate an operations/suggestions query
        var isOperationsQuery = inputLower.Contains("what operations") ||
                                inputLower.Contains("operations for") ||
                                inputLower.Contains("suggest") ||
                                inputLower.Contains("recommendation") ||
                                inputLower.Contains("don't forget") ||
                                inputLower.Contains("what else") ||
                                inputLower.Contains("anything else") ||
                                inputLower.Contains("commonly missed") ||
                                inputLower.Contains("not included") ||
                                inputLower.Contains("manual line") ||
                                (inputLower.Contains("replace") && ContainsPartName(inputLower)) ||
                                (inputLower.Contains("repair") && ContainsPartName(inputLower)) ||
                                (inputLower.Contains("refinish") && ContainsPartName(inputLower));

        // Extract operation type
        string operationType = "Replace";
        if (inputLower.Contains("repair")) operationType = "Repair";
        else if (inputLower.Contains("r&i") || inputLower.Contains("remove")) operationType = "R&I";
        else if (inputLower.Contains("refinish") && !inputLower.Contains("replace")) operationType = "Refinish";

        // Get learned operations from estimates
        var learnedOps = new List<LearnedOperationSuggestion>();

        // Extract the FULL part name as user typed it (e.g., "LT Fender" not just "fender")
        string displayPartName = ExtractFullPartName(input) ?? partName ?? "Part";

        // Extract body/labor and refinish hours from input
        decimal? bodyUnits = ExtractUnits(input, "body") ?? ExtractUnits(input, "labor");
        decimal? refinishUnits = ExtractUnits(input, "refinish") ?? ExtractUnits(input, "paint");

        // If no specific units found, try to extract numbers with context
        if (!bodyUnits.HasValue && !refinishUnits.HasValue)
        {
            // Look for patterns like "2.0 body" or "1.8 refinish" or just numbers
            var matches = System.Text.RegularExpressions.Regex.Matches(input, @"(\d+\.?\d*)\s*(body|labor|refinish|paint|ref)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (decimal.TryParse(m.Groups[1].Value, out var num) && num > 0 && num < 50)
                {
                    var type = m.Groups[2].Value.ToLowerInvariant();
                    if (type == "body" || type == "labor")
                        bodyUnits = num;
                    else if (type == "refinish" || type == "paint" || type == "ref")
                        refinishUnits = num;
                    else if (!bodyUnits.HasValue && !refinishUnits.HasValue)
                    {
                        // First number without type - guess based on context
                        if (inputLower.Contains("refinish") || inputLower.Contains("paint"))
                            refinishUnits = num;
                        else
                            bodyUnits = num;
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(partName))
        {
            var learnedResult = _learningService.QueryOperationsForPart(partName, operationType);
            if (learnedResult.SuggestedOperations.Count > 0)
            {
                foreach (var op in learnedResult.SuggestedOperations.Take(8))
                {
                    learnedOps.Add(new LearnedOperationSuggestion
                    {
                        Description = op.Description,
                        OperationType = op.OperationType,
                        Category = op.Category,
                        Confidence = op.Confidence,
                        ExampleCount = op.ExampleCount,
                        TypicalLaborHours = op.TypicalLaborHours,
                        TypicalRefinishHours = op.TypicalRefinishHours
                    });
                }
            }

            // Smart calculation: Generate manual lines based on part type and hours
            if (refinishUnits.HasValue || bodyUnits.HasValue)
            {
                var calculatedOps = CalculateManualLinesForPart(partName, operationType, refinishUnits ?? 0, bodyUnits ?? 0);
                learnedOps.AddRange(calculatedOps);
            }
        }

        // Get standard SCRS operations
        var standardOps = new List<OperationItem>();

        if (_operationsData?.Operations != null && _operationsData.Operations.Count > 0)
        {
            // Check for suggestion group matches first
            if (_operationsData.SuggestionGroups != null)
            {
                foreach (var group in _operationsData.SuggestionGroups)
                {
                    var groupKey = group.Key.ToLowerInvariant().Replace("-", " ").Replace("_", " ");
                    var groupName = group.Value.Name?.ToLowerInvariant() ?? "";

                    // Check if input matches a suggestion group
                    if (inputLower.Contains(groupKey) ||
                        inputLower.Contains(groupName) ||
                        (groupKey.Contains("bumper") && inputLower.Contains("bumper")) ||
                        (groupKey.Contains("quarter") && inputLower.Contains("quarter")) ||
                        (groupKey.Contains("hood") && inputLower.Contains("hood")) ||
                        (groupKey.Contains("fender") && inputLower.Contains("fender")) ||
                        (groupKey.Contains("structural") && inputLower.Contains("structural")) ||
                        (groupKey.Contains("adas") && inputLower.Contains("adas")))
                    {
                        // Found a matching group - get the operations
                        foreach (var opId in group.Value.Operations ?? new List<string>())
                        {
                            var op = _operationsData.Operations.FirstOrDefault(o => o.Id == opId);
                            if (op != null) standardOps.Add(op);
                        }
                        break;
                    }
                }
            }

            // If no group match, search for individual operations by keyword
            if (standardOps.Count == 0)
            {
                var matchingOps = new List<(OperationItem op, int score)>();

                foreach (var op in _operationsData.Operations)
                {
                    int score = 0;
                    var nameLower = op.Name?.ToLowerInvariant() ?? "";
                    var category = GetCategoryName(op.Category);

                    // Check name match
                    var inputWords = inputLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length >= 3 && !_stopWords.Contains(w))
                        .ToArray();

                    foreach (var word in inputWords)
                    {
                        if (nameLower.Contains(word))
                            score += 3;
                        if (category.ToLowerInvariant().Contains(word))
                            score += 2;
                    }

                    // Boost for specific part matches
                    if (inputLower.Contains("bumper") && (nameLower.Contains("bumper") || op.Category == "01"))
                        score += 5;
                    if (inputLower.Contains("hood") && (nameLower.Contains("hood") || op.Category == "05"))
                        score += 5;
                    if (inputLower.Contains("fender") && (nameLower.Contains("fender") || op.Category == "10"))
                        score += 5;
                    if (inputLower.Contains("quarter") && (nameLower.Contains("quarter") || op.Category == "75"))
                        score += 5;
                    if (inputLower.Contains("door") && (nameLower.Contains("door") || op.Category == "55"))
                        score += 5;
                    if (inputLower.Contains("roof") && (nameLower.Contains("roof") || op.Category == "60"))
                        score += 5;
                    if (inputLower.Contains("pillar") && (nameLower.Contains("pillar") || op.Category == "45"))
                        score += 5;
                    if (inputLower.Contains("rocker") && (nameLower.Contains("rocker") || op.Category == "50"))
                        score += 5;

                    // Boost for refinish queries
                    if (inputLower.Contains("refinish") && op.LaborType == "paint")
                        score += 3;
                    if (inputLower.Contains("structural") && op.LaborType == "structural")
                        score += 3;
                    if (inputLower.Contains("mechanical") && op.LaborType == "mechanical")
                        score += 3;

                    if (score >= 3)
                    {
                        matchingOps.Add((op, score));
                    }
                }

                standardOps = matchingOps
                    .OrderByDescending(x => x.score)
                    .Take(12)
                    .Select(x => x.op)
                    .ToList();
            }
        }

        // If we have either learned ops or standard ops, build combined response
        if (learnedOps.Count > 0 || standardOps.Count > 0)
        {
            // Determine a title based on what was searched
            string title = "Related Operations";
            if (inputLower.Contains("bumper")) title = "Bumper Operations";
            else if (inputLower.Contains("hood")) title = "Hood Operations";
            else if (inputLower.Contains("fender")) title = "Fender Operations";
            else if (inputLower.Contains("quarter")) title = "Quarter Panel Operations";
            else if (inputLower.Contains("door")) title = "Door Operations";
            else if (inputLower.Contains("pillar")) title = "Pillar Operations";
            else if (inputLower.Contains("rocker")) title = "Rocker Operations";
            else if (inputLower.Contains("roof")) title = "Roof Operations";

            return BuildCombinedOperationsResponse(title, displayPartName, operationType, standardOps, learnedOps);
        }

        return null;
    }

    /// <summary>
    /// Build a combined response with both standard SCRS ops and learned patterns
    /// </summary>
    private ChatResponse BuildCombinedOperationsResponse(
        string title,
        string? partName,
        string operationType,
        List<OperationItem> standardOps,
        List<LearnedOperationSuggestion> learnedOps)
    {
        var response = $"🔧 **{title}**\n";

        // Show learned patterns first if available (from real estimates)
        if (learnedOps.Count > 0)
        {
            var regularLearned = learnedOps.Where(o => !o.IsManualLine).ToList();
            var manualLearned = learnedOps.Where(o => o.IsManualLine).ToList();

            if (regularLearned.Count > 0)
            {
                response += $"\n📊 **From Uploaded Estimates** *(learned patterns)*\n";
                foreach (var op in regularLearned.Take(6))
                {
                    var hours = op.TypicalLaborHours > 0 ? $"{op.TypicalLaborHours:F1}L" : "";
                    if (op.TypicalRefinishHours > 0)
                        hours += (hours.Length > 0 ? " / " : "") + $"{op.TypicalRefinishHours:F1}P";

                    var confidence = op.Confidence >= 0.8 ? "✅" : op.Confidence >= 0.5 ? "🟡" : "❓";
                    response += $"{confidence} {op.Description}";
                    if (!string.IsNullOrEmpty(hours))
                        response += $" ({hours})";
                    if (op.ExampleCount > 1)
                        response += $" [seen {op.ExampleCount}x]";
                    response += "\n";
                }
            }

            if (manualLearned.Count > 0)
            {
                // partName here is the full display part name passed from SearchOperations
                var fullPartName = partName ?? "Part";
                if (fullPartName == "Part" || fullPartName.Length < 3)
                {
                    fullPartName = "Part";
                }

                // Group by category (Body vs Paint)
                var bodyOps = manualLearned.Where(m => m.Category == "Body").ToList();
                var paintOps = manualLearned.Where(m => m.Category == "Paint" || string.IsNullOrEmpty(m.Category)).ToList();

                response += $"\n📝 **Calculated Manual Lines for {fullPartName}**\n";

                if (bodyOps.Count > 0)
                {
                    response += "**🟢 Body:**\n";
                    foreach (var ml in bodyOps)
                    {
                        var hours = ml.TypicalLaborHours > 0 ? $"{ml.TypicalLaborHours:F2}" : "";
                        var confidence = ml.Confidence >= 0.9 ? "✅" : ml.Confidence >= 0.8 ? "🟡" : "❓";
                        response += $"{confidence} {fullPartName} {ml.Description}";
                        if (!string.IsNullOrEmpty(hours))
                            response += $" - {hours} units";
                        response += "\n";
                    }
                }

                if (paintOps.Count > 0)
                {
                    response += "**🔵 Paint:**\n";
                    foreach (var ml in paintOps)
                    {
                        var hours = ml.TypicalLaborHours > 0 ? $"{ml.TypicalLaborHours:F2}" : "";
                        var confidence = ml.Confidence >= 0.9 ? "✅" : ml.Confidence >= 0.8 ? "🟡" : "❓";
                        response += $"{confidence} {fullPartName} {ml.Description}";
                        if (!string.IsNullOrEmpty(hours))
                            response += $" - {hours} units";
                        response += "\n";
                    }
                }
            }
        }

        // Show standard SCRS operations
        if (standardOps.Count > 0)
        {
            response += $"\n📋 **Standard Operations** *(industry reference)*\n";

            // Group by labor type
            var laborGroups = standardOps.GroupBy(o => o.LaborType ?? "other").ToList();

            foreach (var group in laborGroups.OrderBy(g => GetLaborTypeOrder(g.Key)))
            {
                var laborName = GetLaborTypeName(group.Key);
                var laborIcon = GetLaborTypeIcon(group.Key);

                response += $"**{laborIcon} {laborName}:**\n";
                foreach (var op in group.Take(4))
                {
                    response += $"• {op.Name}";
                    if (op.Materials == true)
                        response += " 📦";
                    if (!string.IsNullOrEmpty(op.PPageRef))
                        response += $" ({op.PPageRef})";
                    response += "\n";
                }
                if (group.Count() > 4)
                    response += $"  *...and {group.Count() - 4} more*\n";
            }
        }

        // Legend and disclaimer
        response += "\n---\n";
        response += "📦 = Materials may be needed\n";
        if (learnedOps.Count > 0)
            response += "✅ = High confidence | 🟡 = Medium | ❓ = Low\n";

        // Encourage uploading estimates if no learned data
        if (learnedOps.Count == 0)
        {
            response += "\n💡 **Tip:** Upload your estimates to build personalized operation chains! ";
            response += "The more estimates you upload, the smarter the suggestions become - ";
            response += "showing exactly which operations you use together based on YOUR real repairs.\n";
        }

        response += "\n⚠️ *Verify all information against current P-pages and OEM repair procedures for accuracy.*";

        return new ChatResponse
        {
            Message = response,
            Confidence = learnedOps.Count > 0 ? 0.9 : 0.85,
            Category = "operations"
        };
    }

    /// <summary>
    /// Calculate manual lines for a part based on McStud Excel tool formulas
    /// Formulas extracted from master.xlsx Part Operations, Body Operations, and Refinish Operations sheets
    /// </summary>
    private List<LearnedOperationSuggestion> CalculateManualLinesForPart(string partName, string operationType, decimal refinishHours, decimal bodyHours = 0)
    {
        var ops = new List<LearnedOperationSuggestion>();
        var partLower = partName.ToLowerInvariant();

        // === PART TYPE DETECTION (from McStud Excel Part Types) ===

        // Plastic Parts (bumper, fascia, valance, spoiler, etc.)
        bool isPlasticPart = partLower.Contains("bumper") || partLower.Contains("fascia") ||
                             partLower.Contains("valance") || partLower.Contains("spoiler") ||
                             partLower.Contains("air dam") || partLower.Contains("ground effects") ||
                             partLower.Contains("rocker cover") || partLower.Contains("wheel lip") ||
                             partLower.Contains("fender flare") || partLower.Contains("plastic");

        // Welded Metal Parts (quarter, rocker, pillar, rail, etc.)
        bool isWeldedPart = partLower.Contains("quarter") || partLower.Contains("rocker panel") ||
                           partLower.Contains("pillar") || partLower.Contains("rail") ||
                           partLower.Contains("floor") || partLower.Contains("wheelhouse") ||
                           partLower.Contains("apron") || partLower.Contains("inner fender");

        // Bolted Metal Parts (fender, hood, door, trunk, etc.)
        bool isBoltedMetal = !isPlasticPart && !isWeldedPart &&
                            (partLower.Contains("fender") || partLower.Contains("hood") ||
                             partLower.Contains("door") || partLower.Contains("trunk") ||
                             partLower.Contains("deck lid") || partLower.Contains("tailgate") ||
                             partLower.Contains("tail gate") || partLower.Contains("liftgate") ||
                             partLower.Contains("lift gate") || partLower.Contains("mirror"));

        // Wheel/Tire Parts (mechanical - different operations)
        bool isWheelTire = partLower.Contains("wheel") || partLower.Contains("tire") ||
                          partLower.Contains("rim") || partLower.Contains("tpms") ||
                          partLower.Contains("hub cap") || partLower.Contains("hubcap");

        // === OPERATION TYPE DETECTION ===
        bool isReplacement = operationType.Equals("Replace", StringComparison.OrdinalIgnoreCase) ||
                            operationType.Equals("Repl", StringComparison.OrdinalIgnoreCase);
        bool isRepair = operationType.Equals("Repair", StringComparison.OrdinalIgnoreCase) ||
                       operationType.Equals("Rpr", StringComparison.OrdinalIgnoreCase);
        bool isBlend = operationType.Equals("Blend", StringComparison.OrdinalIgnoreCase);

        // === LABOR OPERATIONS (from Body Operations sheet) ===

        // Prime & Block - for repairs (15% of body hours, min 0.3, max 1.0)
        if (isRepair && bodyHours >= 0.5m)
        {
            var primeBlockHours = Math.Max(0.3m, Math.Min(1.0m, bodyHours * 0.15m));
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Prime & Block Repair Area",
                TypicalLaborHours = Math.Round(primeBlockHours, 1),
                IsManualLine = true,
                Confidence = 0.95,
                Category = "Body"
            });
        }

        // Welded Panel Operations (from Body Operations - Welded Metal Part section)
        if (isWeldedPart && isReplacement)
        {
            // Remove Factory E-Coat
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Remove Factory E-Coat",
                TypicalLaborHours = 0.3m,
                IsManualLine = true,
                Confidence = 0.90,
                Category = "Body"
            });

            // Weld-Through Primer
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Weld-Through Primer",
                TypicalLaborHours = 0.2m,
                IsManualLine = true,
                Confidence = 0.95,
                Category = "Body"
            });

            // Seam Sealer - welded panels always need it
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Seam Sealer",
                TypicalLaborHours = 0.4m,
                IsManualLine = true,
                Confidence = 0.95,
                Category = "Body"
            });

            // Cavity Wax - for closed sections
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Cavity Wax",
                TypicalLaborHours = 0.3m,
                IsManualLine = true,
                Confidence = 0.90,
                Category = "Body"
            });

            // Sound Deadener - typically needed
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Sound Deadener",
                TypicalLaborHours = 0.2m,
                IsManualLine = true,
                Confidence = 0.85,
                Category = "Body"
            });
        }

        // Wheel/Tire Operations (mechanical - NOT INCLUDED in database times)
        if (isWheelTire && isReplacement)
        {
            // TPMS Relearn/Reset - required when sensors are replaced or relearned
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "TPMS Sensor Relearn/Reset",
                TypicalLaborHours = 0.3m,
                IsManualLine = true,
                Confidence = 0.95,
                Category = "Body"
            });

            // Mount and Balance - for new tire/wheel
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Mount & Balance",
                TypicalLaborHours = 0.4m,
                IsManualLine = true,
                Confidence = 0.95,
                Category = "Body"
            });

            // Alignment Check - often needed after wheel/tire work
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Alignment Check (may be needed)",
                TypicalLaborHours = 0.5m,
                IsManualLine = true,
                Confidence = 0.80,
                Category = "Body"
            });

            // Torque Wheels - proper torque specification
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Torque to Spec",
                TypicalLaborHours = 0.1m,
                IsManualLine = true,
                Confidence = 0.90,
                Category = "Body"
            });
        }

        // Corrosion Protection - for significant body work or metal panels
        if ((bodyHours >= 1.0m || isBoltedMetal || isWeldedPart) && isReplacement)
        {
            if (!isWeldedPart) // Already added for welded parts
            {
                ops.Add(new LearnedOperationSuggestion
                {
                    Description = "Corrosion Protection",
                    TypicalLaborHours = 0.3m,
                    IsManualLine = true,
                    Confidence = 0.90,
                    Category = "Body"
                });
            }
        }

        // === MATERIAL OPERATIONS (from Part Operations - Material column) ===

        if (refinishHours > 0)
        {
            // Plastic Part operations (Adhesion Promoter + Flex Additive)
            if (isPlasticPart)
            {
                // Adhesion Promoter - 10% of refinish, min 0.2, max 0.5
                var adhesionHours = Math.Max(0.2m, Math.Min(0.5m, refinishHours * 0.10m));
                ops.Add(new LearnedOperationSuggestion
                {
                    Description = "Adhesion Promoter",
                    TypicalLaborHours = Math.Round(adhesionHours, 1),
                    IsManualLine = true,
                    Confidence = 0.95,
                    Category = "Paint"
                });

                // Flex Additive - flat 0.2 for first large plastic part
                ops.Add(new LearnedOperationSuggestion
                {
                    Description = "Flex Additive (First Large Part)",
                    TypicalLaborHours = 0.2m,
                    IsManualLine = true,
                    Confidence = 0.95,
                    Category = "Paint"
                });
            }

            // DE-NIB - all refinish jobs (from Labor column)
            if (refinishHours >= 0.5m)
            {
                ops.Add(new LearnedOperationSuggestion
                {
                    Description = "DE-NIB",
                    TypicalLaborHours = 0.2m,
                    IsManualLine = true,
                    Confidence = 0.90,
                    Category = "Paint"
                });
            }
        }

        // === ADDITIONAL PARTS OPERATIONS (from Part Operations - Additional Parts column) ===

        // License Plate operations - for rear bumper/trunk
        if ((partLower.Contains("rear bumper") || partLower.Contains("trunk") ||
             partLower.Contains("tailgate") || partLower.Contains("tail gate") ||
             partLower.Contains("liftgate") || partLower.Contains("lift gate")) && isReplacement)
        {
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "R&I License Plate",
                TypicalLaborHours = 0.1m,
                IsManualLine = true,
                Confidence = 0.85,
                Category = "Body"
            });

            // Check if drilling needed
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Drill for License Plate (if needed)",
                TypicalLaborHours = 0.2m,
                IsManualLine = true,
                Confidence = 0.70,
                Category = "Body"
            });
        }

        // Nameplate/Emblem transfer - for replacements
        if (isReplacement && (isBoltedMetal || isPlasticPart))
        {
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Transfer Nameplates/Emblems",
                TypicalLaborHours = 0.2m,
                IsManualLine = true,
                Confidence = 0.80,
                Category = "Body"
            });

            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Adhesive Cleanup",
                TypicalLaborHours = 0.2m,
                IsManualLine = true,
                Confidence = 0.75,
                Category = "Body"
            });
        }

        // === EQUIPMENT OPERATIONS (from Part Operations - Equipment column) ===

        // Park Sensors - for bumpers typically
        if (partLower.Contains("bumper") && (isReplacement || refinishHours > 0))
        {
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "R&I Park Sensors (if equipped)",
                TypicalLaborHours = 0.3m,
                IsManualLine = true,
                Confidence = 0.70,
                Category = "Body"
            });
        }

        // Radar/ADAS - for front bumper/grille area
        if ((partLower.Contains("front bumper") || partLower.Contains("grille")) && isReplacement)
        {
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Radar Behind Painted Portion (if equipped)",
                TypicalLaborHours = 0.3m,
                IsManualLine = true,
                Confidence = 0.65,
                Category = "Paint"
            });
        }

        // === REFINISH OPERATIONS (from Refinish Operations sheet) ===

        if (refinishHours > 0)
        {
            // Wet/Dry Sand - 10% of refinish, min 0.2, max 0.8, when refinish >= 1.5
            if (refinishHours >= 1.5m)
            {
                var wetSandHours = Math.Max(0.2m, Math.Min(0.8m, refinishHours * 0.10m));
                ops.Add(new LearnedOperationSuggestion
                {
                    Description = "Wet/Dry Sand & Buff",
                    TypicalLaborHours = Math.Round(wetSandHours, 1),
                    IsManualLine = true,
                    Confidence = 0.85,
                    Category = "Paint"
                });
            }

            // Cover Car (from Cover Car Operations sheet)
            if (refinishHours >= 1.5m)
            {
                ops.Add(new LearnedOperationSuggestion
                {
                    Description = "Cover Car",
                    TypicalLaborHours = 0.5m,
                    IsManualLine = true,
                    Confidence = 0.90,
                    Category = "Paint"
                });
            }
            else if (refinishHours >= 1.0m)
            {
                ops.Add(new LearnedOperationSuggestion
                {
                    Description = "Cover Car (Basic)",
                    TypicalLaborHours = 0.3m,
                    IsManualLine = true,
                    Confidence = 0.85,
                    Category = "Paint"
                });
            }

            // Mask for Overspray - 10% of refinish, min 0.2, max 0.5
            if (refinishHours >= 0.5m)
            {
                var maskHours = Math.Max(0.2m, Math.Min(0.5m, refinishHours * 0.10m));
                ops.Add(new LearnedOperationSuggestion
                {
                    Description = "Mask for Overspray",
                    TypicalLaborHours = Math.Round(maskHours, 1),
                    IsManualLine = true,
                    Confidence = 0.90,
                    Category = "Paint"
                });
            }
        }

        // === REPLACEMENT-SPECIFIC OPERATIONS ===

        if (isReplacement)
        {
            // Prep New Part - all replacements
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Prep New/Unprimed Part",
                TypicalLaborHours = 0.4m,
                IsManualLine = true,
                Confidence = 0.95,
                Category = "Body"
            });

            // Trial Fit - aftermarket/LKQ parts especially
            ops.Add(new LearnedOperationSuggestion
            {
                Description = "Trial Fit",
                TypicalLaborHours = 0.3m,
                IsManualLine = true,
                Confidence = 0.85,
                Category = "Body"
            });
        }

        return ops;
    }

    /// <summary>
    /// Extract the full part name as user typed it (e.g., "LT Fender" not just "fender")
    /// </summary>
    private string? ExtractFullPartName(string input)
    {
        // Common prefixes
        var prefixes = new[] { "lt", "rt", "lh", "rh", "left", "right", "front", "rear", "frt", "rr", "inner", "outer", "upper", "lower" };

        // Known part names
        var partNames = new[] { "fender", "bumper cover", "bumper", "door", "hood", "quarter panel", "quarter",
                                "rocker", "pillar", "roof", "trunk", "tailgate", "liftgate", "grille", "mirror" };

        var words = input.Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        bool foundPart = false;

        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i].ToLowerInvariant();

            // Check if this word is a prefix
            if (prefixes.Contains(word))
            {
                result.Add(words[i]);
                continue;
            }

            // Check if this word (or word + next word) is a part name
            foreach (var part in partNames.OrderByDescending(p => p.Length))
            {
                var partWords = part.Split(' ');
                if (partWords[0] == word)
                {
                    // Check if multi-word part matches
                    bool fullMatch = true;
                    for (int j = 0; j < partWords.Length && i + j < words.Length; j++)
                    {
                        if (words[i + j].ToLowerInvariant() != partWords[j])
                        {
                            fullMatch = false;
                            break;
                        }
                    }
                    if (fullMatch)
                    {
                        for (int j = 0; j < partWords.Length && i + j < words.Length; j++)
                        {
                            result.Add(words[i + j]);
                        }
                        foundPart = true;
                        break;
                    }
                }
            }

            if (foundPart) break;
        }

        if (result.Count > 0)
        {
            // Capitalize properly
            return string.Join(" ", result.Select(w =>
                w.Length <= 2 ? w.ToUpperInvariant() : // LT, RT stay uppercase
                char.ToUpper(w[0]) + w.Substring(1).ToLower()));
        }

        return null;
    }

    /// <summary>
    /// Extract the actual operation keyword from a possibly corrupted ManualLineType
    /// e.g., "Applique dark gunmetal Adhesion" -> "Adhesion Promoter"
    /// e.g., "Some Part Name Flex" -> "Flex Additive"
    /// </summary>
    private string? ExtractOperationKeyword(string manualLineType)
    {
        if (string.IsNullOrWhiteSpace(manualLineType))
            return null;

        var lower = manualLineType.ToLowerInvariant();

        // Known operation keywords and their proper names
        var operationMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "adhesion", "Adhesion Promoter" },
            { "adhesion promoter", "Adhesion Promoter" },
            { "flex", "Flex Additive" },
            { "flex additive", "Flex Additive" },
            { "flex agent", "Flex Additive" },
            { "de-nib", "DE-NIB" },
            { "denib", "DE-NIB" },
            { "wet/dry", "Wet/Dry Sand" },
            { "wet dry", "Wet/Dry Sand" },
            { "color sand", "Color Sand" },
            { "rub out", "Rub Out & Buff" },
            { "rub-out", "Rub Out & Buff" },
            { "buff", "Rub Out & Buff" },
            { "backtape", "Backtape Jambs" },
            { "back tape", "Backtape Jambs" },
            { "clear coat", "Clear Coat" },
            { "clearcoat", "Clear Coat" },
            { "stage", "Stage and Secure" },
            { "mask", "Mask for Overspray" },
            { "seam sealer", "Seam Sealer" },
            { "corrosion", "Corrosion Protection" },
            { "primer", "Primer Surfacer" },
            { "pre-scan", "Pre-Scan" },
            { "post-scan", "Post-Scan" },
            { "scan", "Diagnostic Scan" },
            { "trial fit", "Trial Fit" },
            { "cover car", "Cover Car" },
            { "cover and protect", "Cover and Protect" }
        };

        // Check if any known operation keyword is at the END of the string
        foreach (var kvp in operationMappings)
        {
            if (lower.EndsWith(kvp.Key) || lower.Contains(kvp.Key))
            {
                return kvp.Value;
            }
        }

        // If no match, return the original but only if it's short (probably a real operation name)
        if (manualLineType.Length <= 25)
            return manualLineType;

        return null; // Skip long garbage entries
    }

    /// <summary>
    /// Helper class for learned operation suggestions
    /// </summary>
    private class LearnedOperationSuggestion
    {
        public string Description { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string Category { get; set; } = "";
        public double Confidence { get; set; }
        public int ExampleCount { get; set; }
        public decimal TypicalLaborHours { get; set; }
        public decimal TypicalRefinishHours { get; set; }
        public bool IsManualLine { get; set; }
    }

    private bool ContainsPartName(string input)
    {
        var parts = new[] { "bumper", "fender", "hood", "door", "quarter", "pillar", "rocker", "roof",
                           "deck lid", "trunk", "tailgate", "liftgate", "bed", "cab", "panel" };
        return parts.Any(p => input.Contains(p));
    }

    private string GetCategoryName(string? categoryCode)
    {
        if (string.IsNullOrEmpty(categoryCode) || _operationsData?.Categories == null)
            return "Unknown";

        var cat = _operationsData.Categories.FirstOrDefault(c => c.Code == categoryCode);
        return cat?.Name ?? categoryCode;
    }

    private ChatResponse BuildOperationsResponse(string title, string? description, List<OperationItem> ops)
    {
        var response = $"🔧 **{title}**\n";
        if (!string.IsNullOrEmpty(description))
            response += $"*{description}*\n";
        response += "\n";

        // Group by labor type
        var laborGroups = ops.GroupBy(o => o.LaborType ?? "other").ToList();

        foreach (var group in laborGroups.OrderBy(g => GetLaborTypeOrder(g.Key)))
        {
            var laborName = GetLaborTypeName(group.Key);
            var laborIcon = GetLaborTypeIcon(group.Key);

            response += $"**{laborIcon} {laborName}:**\n";
            foreach (var op in group.Take(5))
            {
                response += $"• {op.Name}";
                if (op.Materials == true)
                    response += " 📦";
                if (!string.IsNullOrEmpty(op.PPageRef))
                    response += $" ({op.PPageRef})";
                response += "\n";
            }
            if (group.Count() > 5)
                response += $"  *...and {group.Count() - 5} more*\n";
            response += "\n";
        }

        response += "📦 = Materials may be needed\n\n";
        response += "⚠️ *Verify all information against current P-pages and OEM repair procedures for accuracy.*";

        return new ChatResponse
        {
            Message = response,
            Confidence = 0.85,
            Category = "operations"
        };
    }

    private int GetLaborTypeOrder(string laborType) => laborType switch
    {
        "body" => 1,
        "structural" => 2,
        "paint" => 3,
        "mechanical" => 4,
        "detail" => 5,
        _ => 6
    };

    private string GetLaborTypeName(string laborType) => laborType switch
    {
        "body" => "Body",
        "structural" => "Structural",
        "paint" => "Paint/Refinish",
        "mechanical" => "Mechanical",
        "detail" => "Detail",
        "other" => "Other/Materials",
        _ => laborType
    };

    private string GetLaborTypeIcon(string laborType) => laborType switch
    {
        "body" => "🟢",
        "structural" => "🔴",
        "paint" => "🔵",
        "mechanical" => "🟠",
        "detail" => "🟣",
        _ => "⚪"
    };

    /// <summary>
    /// Search definitions for a matching term or topic
    /// </summary>
    private ChatResponse? SearchDefinitions(string input)
    {
        if (_definitionsData?.Definitions == null || _definitionsData.Definitions.Count == 0)
            return null;

        var inputLower = input.ToLowerInvariant();

        // Keywords that indicate a definition query
        var isDefinitionQuery = inputLower.Contains("what is") ||
                                inputLower.Contains("what's") ||
                                inputLower.Contains("define") ||
                                inputLower.Contains("definition") ||
                                inputLower.Contains("explain") ||
                                inputLower.Contains("p-page") ||
                                inputLower.Contains("ppage") ||
                                inputLower.Contains("not included") ||
                                inputLower.Contains("deg inquiry") ||
                                inputLower.Contains("deg ");

        DefinitionItem? bestMatch = null;
        int bestScore = 0;

        foreach (var def in _definitionsData.Definitions)
        {
            int score = 0;
            var termLower = def.Term?.ToLowerInvariant() ?? "";
            var definitionLower = def.Definition?.ToLowerInvariant() ?? "";

            // Exact term match
            if (inputLower.Contains(termLower) || termLower.Contains(inputLower.Replace("what is ", "").Replace("what's ", "").Trim()))
            {
                score += 10;
            }

            // Check individual words from input against term
            var inputWords = inputLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !_stopWords.Contains(w))
                .ToArray();

            foreach (var word in inputWords)
            {
                if (termLower.Contains(word))
                    score += 3;
                if (definitionLower.Contains(word))
                    score += 1;
                if (def.Details?.ToLowerInvariant().Contains(word) == true)
                    score += 1;
            }

            // Boost for specific searches
            if (inputLower.Contains("seam sealer") && termLower.Contains("seam sealer"))
                score += 5;
            if (inputLower.Contains("adas") && termLower.Contains("adas"))
                score += 5;
            if (inputLower.Contains("scan") && termLower.Contains("scan"))
                score += 5;
            if (inputLower.Contains("corrosion") && termLower.Contains("corrosion"))
                score += 5;

            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = def;
            }
        }

        // Only return if we have a decent match
        if (bestMatch != null && bestScore >= 3)
        {
            var response = $"📖 **{bestMatch.Term}**\n\n";
            response += $"{bestMatch.Definition}\n";

            if (!string.IsNullOrEmpty(bestMatch.Details))
            {
                response += $"\n📝 **Details:** {bestMatch.Details}\n";
            }

            if (!string.IsNullOrEmpty(bestMatch.PPageRef))
            {
                response += $"\n📍 **P-Page:** {bestMatch.PPageRef}";
                if (!string.IsNullOrEmpty(bestMatch.PPageLocation))
                    response += $" ({bestMatch.PPageLocation})";
                response += "\n";
            }

            if (!string.IsNullOrEmpty(bestMatch.PPageSystem))
            {
                response += $"📊 **Systems:** {bestMatch.PPageSystem}\n";
            }

            if (!string.IsNullOrEmpty(bestMatch.DegInquiry))
            {
                response += $"\n🔍 **DEG Inquiry:** {bestMatch.DegInquiry}\n";
                if (!string.IsNullOrEmpty(bestMatch.DegResponse))
                {
                    response += $"{bestMatch.DegResponse}\n";
                }
            }

            response += $"\n⚠️ **Status:** {bestMatch.Status ?? "Unknown"}";

            return new ChatResponse
            {
                Message = response,
                Confidence = bestScore >= 8 ? 0.95 : (bestScore >= 5 ? 0.8 : 0.6),
                Category = "definitions"
            };
        }

        return null;
    }

    /// <summary>
    /// Search for app navigation and feature questions
    /// </summary>
    private ChatResponse? SearchAppNavigation(string input)
    {
        var inputLower = input.ToLowerInvariant();

        // App navigation knowledge
        var navTopics = new Dictionary<string, (string title, string content)>
        {
            // Main tabs
            ["export tab"] = ("Export Tab",
                "📤 **Export Tab** - Send Excel data to CCC/Mitchell\n\n" +
                "**How to use:**\n" +
                "1. Copy operations from your Excel spreadsheet (Ctrl+C)\n" +
                "2. McStud auto-detects the clipboard data\n" +
                "3. Click in CCC where you want lines inserted\n" +
                "4. Click the 'CCC Desktop' button\n\n" +
                "**Features:**\n" +
                "• Auto-monitoring clipboard for Excel data\n" +
                "• Speed settings (Slow/Normal/Fast/Turbo)\n" +
                "• Clipboard summary showing totals\n" +
                "• Works with CCC ONE and Mitchell\n\n" +
                "**Hotkey:** Press Ctrl+Alt+V while in CCC to paste instantly!"),

            ["chat tab"] = ("Chat Tab",
                "💬 **Chat Tab** - Help Assistant\n\n" +
                "This is where you are now! The Chat tab provides:\n\n" +
                "• Definitions and P-page lookups\n" +
                "• OEM Position Statements with documentation\n" +
                "• Excel spreadsheet navigation help\n" +
                "• Operations knowledge (Repair, Replace, R&I)\n" +
                "• App navigation help\n\n" +
                "Just type your question and I'll help find the answer!"),

            ["import tab"] = ("Import Tab",
                "📥 **Import Tab** - Estimate PDF Upload\n\n" +
                "Upload estimate PDFs to extract data:\n\n" +
                "**Supported formats:**\n" +
                "• CCC ONE estimates\n" +
                "• Mitchell estimates\n" +
                "• Audatex estimates\n\n" +
                "**How it works:**\n" +
                "1. Click 'Select PDF' or drag-and-drop\n" +
                "2. PDF is parsed for line items\n" +
                "3. Data populates your Excel template\n" +
                "4. Review and adjust as needed"),

            ["damage tab"] = ("Damage Estimator Tab",
                "🔧 **Damage Estimator Tab** - Interview-Based Estimation\n\n" +
                "Create estimates by answering questions about damage:\n\n" +
                "**How to use:**\n" +
                "1. Select vehicle type and year\n" +
                "2. Answer questions about damage location\n" +
                "3. Specify repair type for each area\n" +
                "4. System generates recommended operations\n\n" +
                "Great for quick estimates when you don't have a PDF!"),

            ["stats tab"] = ("Statistics Tab",
                "📊 **Statistics Tab** - Export/Import Tracking\n\n" +
                "Track your estimating activity:\n\n" +
                "• Total exports by destination (CCC, Mitchell)\n" +
                "• Total imports from PDFs\n" +
                "• Operations count over time\n" +
                "• Average estimate size\n" +
                "• Session history"),

            ["defs tab"] = ("Definitions Tab",
                "📖 **Definitions Tab** - Reference Library\n\n" +
                "Two main sections:\n\n" +
                "**1. Definitions & P-Pages**\n" +
                "• Operation definitions\n" +
                "• P-page references (CCC/Mitchell)\n" +
                "• DEG inquiry responses\n" +
                "• Not-included items\n\n" +
                "**2. OEM Position Statements**\n" +
                "• Filter by manufacturer\n" +
                "• Quick scenario buttons\n" +
                "• Official documentation links (PDF, I-CAR, OEM)\n" +
                "• Copy to clipboard for estimates"),

            // Features
            ["hotkey"] = ("Hotkey - Quick Export",
                "⌨️ **Hotkey: Ctrl+Alt+V**\n\n" +
                "The fastest way to export!\n\n" +
                "**How to use:**\n" +
                "1. Copy operations from Excel (Ctrl+C)\n" +
                "2. Click in CCC where you want to paste\n" +
                "3. Press Ctrl+Alt+V\n" +
                "4. Data is typed directly into CCC!\n\n" +
                "**Advantages:**\n" +
                "• No need to click the Export button\n" +
                "• Works even when McStud is minimized\n" +
                "• Faster than manual paste"),

            ["export to ccc"] = ("Exporting to CCC",
                "📤 **How to Export to CCC ONE**\n\n" +
                "**Method 1 - Button:**\n" +
                "1. Copy operations from Excel\n" +
                "2. Click in CCC estimate where you want lines\n" +
                "3. Click 'CCC Desktop' button in Export tab\n\n" +
                "**Method 2 - Hotkey (faster):**\n" +
                "1. Copy operations from Excel\n" +
                "2. Click in CCC estimate\n" +
                "3. Press Ctrl+Alt+V\n\n" +
                "**Tips:**\n" +
                "• Use 'Fast' or 'Turbo' speed for quicker paste\n" +
                "• Make sure CCC is visible on screen\n" +
                "• Don't move mouse during paste"),

            ["learning center"] = ("Learning Center",
                "🎓 **Learning Center** (Admin Mode)\n\n" +
                "Advanced feature for training the system:\n\n" +
                "**Access:** Run McStud with --learning flag\n\n" +
                "**Features:**\n" +
                "• Query Operations - search learned patterns\n" +
                "• Import Estimates - train from PDFs\n" +
                "• Statistics - view learned data\n" +
                "• Patterns Database - manage patterns\n\n" +
                "The Learning Center builds intelligence from real estimates."),

            ["speed settings"] = ("Speed Settings",
                "⚡ **Speed Settings**\n\n" +
                "Adjust paste speed in the Export tab:\n\n" +
                "• **Slow (50ms)** - For older/slower computers\n" +
                "• **Normal (25ms)** - Balanced speed\n" +
                "• **Fast (15ms)** - Recommended for most\n" +
                "• **Turbo (8ms)** - For fast computers\n\n" +
                "If pasting skips characters, try a slower setting."),

            ["oem statements"] = ("OEM Position Statements",
                "📋 **OEM Position Statements**\n\n" +
                "Found in the Definitions (Defs) tab:\n\n" +
                "**What they are:**\n" +
                "Official manufacturer statements about repair procedures.\n\n" +
                "**How to use:**\n" +
                "1. Go to Defs tab\n" +
                "2. Select OEM from dropdown (Honda, Toyota, etc.)\n" +
                "3. Click scenario buttons or search\n" +
                "4. Click statement to expand details\n" +
                "5. Use 'Copy' or 'Use in Estimate' buttons\n\n" +
                "**Documentation includes:**\n" +
                "• Official PDF links\n" +
                "• I-CAR RTS references\n" +
                "• OEM portal links")
        };

        // Check for navigation keywords
        var isNavQuery = inputLower.Contains("how do i") ||
                         inputLower.Contains("how to") ||
                         inputLower.Contains("navigate") ||
                         inputLower.Contains("tab") ||
                         inputLower.Contains("feature") ||
                         inputLower.Contains("use the") ||
                         inputLower.Contains("where is") ||
                         inputLower.Contains("hotkey") ||
                         inputLower.Contains("ctrl+alt") ||
                         inputLower.Contains("export") ||
                         inputLower.Contains("import") ||
                         inputLower.Contains("chat") ||
                         inputLower.Contains("stats") ||
                         inputLower.Contains("statistics") ||
                         inputLower.Contains("damage est") ||
                         inputLower.Contains("interview") ||
                         inputLower.Contains("defs") ||
                         inputLower.Contains("definitions tab") ||
                         inputLower.Contains("speed") ||
                         inputLower.Contains("learning center") ||
                         inputLower.Contains("oem statement");

        if (!isNavQuery)
            return null;

        // Find best matching navigation topic
        string? bestKey = null;
        int bestScore = 0;

        foreach (var key in navTopics.Keys)
        {
            int score = 0;
            var keyWords = key.Split(' ');

            foreach (var word in keyWords)
            {
                if (inputLower.Contains(word))
                    score += word.Length >= 4 ? 3 : 1;
            }

            // Boost specific matches
            if (inputLower.Contains("export") && key.Contains("export"))
                score += 5;
            if (inputLower.Contains("import") && key.Contains("import"))
                score += 5;
            if (inputLower.Contains("chat") && key.Contains("chat"))
                score += 5;
            if (inputLower.Contains("stat") && key.Contains("stat"))
                score += 5;
            if (inputLower.Contains("damage") && key.Contains("damage"))
                score += 5;
            if (inputLower.Contains("interview") && key.Contains("damage"))
                score += 5;
            if (inputLower.Contains("def") && key.Contains("def"))
                score += 5;
            if (inputLower.Contains("hotkey") && key.Contains("hotkey"))
                score += 10;
            if (inputLower.Contains("ctrl") && key.Contains("hotkey"))
                score += 10;
            if (inputLower.Contains("speed") && key.Contains("speed"))
                score += 5;
            if (inputLower.Contains("ccc") && key.Contains("ccc"))
                score += 5;
            if (inputLower.Contains("oem") && key.Contains("oem"))
                score += 5;
            if (inputLower.Contains("position statement") && key.Contains("oem"))
                score += 5;
            if (inputLower.Contains("learning") && key.Contains("learning"))
                score += 5;

            if (score > bestScore)
            {
                bestScore = score;
                bestKey = key;
            }
        }

        if (bestKey != null && bestScore >= 3)
        {
            var (title, content) = navTopics[bestKey];
            return new ChatResponse
            {
                Message = content,
                Confidence = bestScore >= 8 ? 0.95 : (bestScore >= 5 ? 0.8 : 0.65),
                Category = "navigation"
            };
        }

        // Generic navigation help if no specific match
        if (isNavQuery && bestScore < 3)
        {
            return new ChatResponse
            {
                Message = "🧭 **McStud App Navigation**\n\n" +
                         "**Main Tabs:**\n" +
                         "• **Export** - Send Excel data to CCC/Mitchell\n" +
                         "• **Chat** - Help assistant (you're here!)\n" +
                         "• **Import** - Upload estimate PDFs\n" +
                         "• **Damage Est** - Interview-based estimation\n" +
                         "• **Stats** - View export/import history\n" +
                         "• **Defs** - Definitions & OEM statements\n\n" +
                         "**Quick Tips:**\n" +
                         "• Ctrl+Alt+V = Quick export to CCC\n" +
                         "• Copy from Excel, click in CCC, press button\n" +
                         "• Use Defs tab for P-page lookups\n\n" +
                         "Ask about any specific tab for more details!",
                Confidence = 0.7,
                Category = "navigation"
            };
        }

        return null;
    }

    /// <summary>
    /// Search for operation tab navigation - directs users to correct tab based on operation type
    /// Handles queries like "where can I find replace bumper" or "operations for fender"
    /// </summary>
    private ChatResponse? SearchOperationTabNavigation(string input)
    {
        var inputLower = input.ToLowerInvariant();

        // Check if this is a "where to find" or "operations for" query
        var isNavigationQuery = inputLower.Contains("where") ||
                                inputLower.Contains("find") ||
                                inputLower.Contains("locate") ||
                                inputLower.Contains("tab for") ||
                                inputLower.Contains("which tab") ||
                                inputLower.Contains("what tab") ||
                                inputLower.Contains("go to") ||
                                inputLower.Contains("navigate") ||
                                (inputLower.Contains("operations for") && !inputLower.Contains("what operations"));

        if (!isNavigationQuery)
            return null;

        // Part Operations keywords (plastic parts, exterior panels, lights, mirrors)
        var partKeywords = new[] {
            "bumper", "fascia", "fender", "door", "hood", "trunk", "liftgate", "tailgate",
            "mirror", "headlight", "headlamp", "tail light", "taillamp", "fog light", "grille",
            "molding", "trim", "spoiler", "valance", "air dam", "splash shield", "fender liner",
            "wheel opening", "rocker molding", "side skirt", "license plate", "emblem", "nameplate",
            "antenna", "wiper", "glass", "windshield", "back glass", "quarter glass",
            "handle", "lock", "latch", "hinge", "weatherstrip", "run channel"
        };

        // Body Operations keywords (welding, structural, corrosion)
        var bodyKeywords = new[] {
            "weld", "welding", "spot weld", "mig weld", "plug weld",
            "seam sealer", "e-coat", "ecoat", "corrosion", "rust", "cavity wax",
            "body plug", "drain hole", "access hole", "structure", "structural",
            "quarter panel replace", "rocker replace", "roof replace", "pillar",
            "rail", "apron", "radiator support", "uni-body", "unibody",
            "sectioning", "section", "cut", "overlap"
        };

        // Refinish Operations keywords (paint, clear, primer)
        var refinishKeywords = new[] {
            "paint", "refinish", "clear coat", "clearcoat", "base coat", "basecoat",
            "primer", "sealer", "blend", "color match", "tri-coat", "pearl",
            "metallic", "solid color", "two tone", "2 tone", "jamb", "edge",
            "cut in", "de-nib", "denib", "sand", "buff", "polish", "tint",
            "flex additive", "adhesion promoter", "plastic primer"
        };

        // Mechanical Operations keywords
        var mechanicalKeywords = new[] {
            "mechanical", "suspension", "alignment", "wheel", "tire", "brake",
            "a/c", "ac ", "air conditioning", "refrigerant", "freon",
            "radiator", "coolant", "cooling", "engine", "transmission",
            "steering", "rack", "pump", "compressor", "condenser", "evaporator",
            "exhaust", "muffler", "catalytic", "sensor", "module", "computer",
            "battery", "hybrid", "electric vehicle", "ev ", "charging"
        };

        // Cover Car / Setup keywords
        var coverCarKeywords = new[] {
            "cover car", "covercar", "masking", "tape", "paper", "mask off",
            "setup", "set up", "tear down", "teardown", "disassembly",
            "removal for access", "r&i for", "remove for"
        };

        // SOP List keywords
        var sopKeywords = new[] {
            "sop", "standard operating", "procedure", "labor rate", "material rate",
            "shop rate", "discount", "markup", "betterment", "deductible",
            "customer info", "vehicle info", "vin", "mileage", "claim"
        };

        // Determine which tab to recommend
        string? recommendedTab = null;
        string? tabDescription = null;
        string? matchedKeyword = null;

        // Check Part Operations
        foreach (var keyword in partKeywords)
        {
            if (inputLower.Contains(keyword))
            {
                recommendedTab = "Part Operations";
                tabDescription = "This tab covers exterior panels, bumpers, lights, mirrors, glass, trim, and plastic parts.";
                matchedKeyword = keyword;
                break;
            }
        }

        // Check Body Operations (higher priority for structural)
        if (recommendedTab == null || bodyKeywords.Any(k => inputLower.Contains(k)))
        {
            foreach (var keyword in bodyKeywords)
            {
                if (inputLower.Contains(keyword))
                {
                    recommendedTab = "Body Operations";
                    tabDescription = "This tab covers welding, corrosion protection, seam sealer, E-coat removal, structural repairs, and sectioning.";
                    matchedKeyword = keyword;
                    break;
                }
            }
        }

        // Check Refinish Operations
        if (recommendedTab == null)
        {
            foreach (var keyword in refinishKeywords)
            {
                if (inputLower.Contains(keyword))
                {
                    recommendedTab = "Refinish Operations";
                    tabDescription = "This tab covers paint operations, clear coat, primer, blending, color matching, and paint materials.";
                    matchedKeyword = keyword;
                    break;
                }
            }
        }

        // Check Mechanical Operations
        if (recommendedTab == null)
        {
            foreach (var keyword in mechanicalKeywords)
            {
                if (inputLower.Contains(keyword))
                {
                    recommendedTab = "Mechanical Operations";
                    tabDescription = "This tab covers suspension, A/C, brakes, steering, electrical, and drivetrain operations.";
                    matchedKeyword = keyword;
                    break;
                }
            }
        }

        // Check Cover Car
        if (recommendedTab == null)
        {
            foreach (var keyword in coverCarKeywords)
            {
                if (inputLower.Contains(keyword))
                {
                    recommendedTab = "Cover Car Operations";
                    tabDescription = "This tab covers masking, cover car, setup/teardown, and preparation operations.";
                    matchedKeyword = keyword;
                    break;
                }
            }
        }

        // Check SOP List
        if (recommendedTab == null)
        {
            foreach (var keyword in sopKeywords)
            {
                if (inputLower.Contains(keyword))
                {
                    recommendedTab = "SOP List";
                    tabDescription = "This tab contains shop info, labor rates, material rates, customer/vehicle data, and claim information.";
                    matchedKeyword = keyword;
                    break;
                }
            }
        }

        if (recommendedTab != null)
        {
            return new ChatResponse
            {
                Message = $"📍 **Go to: {recommendedTab} Tab**\n\n" +
                          $"{tabDescription}\n\n" +
                          $"You mentioned \"{matchedKeyword}\" - you'll find operations for this in the **{recommendedTab}** tab.\n\n" +
                          $"**Quick Navigation:**\n" +
                          $"• Look for the '{recommendedTab}' tab in the left navigation\n" +
                          $"• Use the dropdowns to select specific operations\n" +
                          $"• Values auto-calculate based on your inputs",
                Confidence = 0.9,
                Category = "navigation",
                RelatedTopics = new List<string> {
                    "Part Operations", "Body Operations", "Refinish Operations",
                    "Mechanical Operations", "SOP List"
                }
            };
        }

        return null;
    }

    /// <summary>
    /// Search OEM Position Statements for matching topics
    /// </summary>
    private ChatResponse? SearchOEMStatements(string input)
    {
        if (_oemStatementsData?.Statements == null || _oemStatementsData.Statements.Count == 0)
            return null;

        var inputLower = input.ToLowerInvariant();

        // Keywords that indicate an OEM position statement query
        var isOemQuery = inputLower.Contains("oem") ||
                         inputLower.Contains("position statement") ||
                         inputLower.Contains("manufacturer") ||
                         inputLower.Contains("honda") ||
                         inputLower.Contains("toyota") ||
                         inputLower.Contains("ford") ||
                         inputLower.Contains("gm") ||
                         inputLower.Contains("general motors") ||
                         inputLower.Contains("chevrolet") ||
                         inputLower.Contains("chrysler") ||
                         inputLower.Contains("dodge") ||
                         inputLower.Contains("jeep") ||
                         inputLower.Contains("stellantis") ||
                         inputLower.Contains("nissan") ||
                         inputLower.Contains("subaru") ||
                         inputLower.Contains("eyesight") ||
                         inputLower.Contains("hyundai") ||
                         inputLower.Contains("kia") ||
                         inputLower.Contains("volkswagen") ||
                         inputLower.Contains("vw") ||
                         inputLower.Contains("audi") ||
                         inputLower.Contains("bmw") ||
                         inputLower.Contains("mercedes") ||
                         inputLower.Contains("tesla") ||
                         inputLower.Contains("lexus") ||
                         inputLower.Contains("acura") ||
                         inputLower.Contains("infiniti") ||
                         inputLower.Contains("scanning") ||
                         inputLower.Contains("calibration") ||
                         inputLower.Contains("adas") ||
                         inputLower.Contains("pre-repair") ||
                         inputLower.Contains("post-repair") ||
                         inputLower.Contains("pre scan") ||
                         inputLower.Contains("post scan");

        // Only search OEM statements if the query is actually about OEMs
        if (!isOemQuery)
            return null;

        OEMPositionStatementItem? bestMatch = null;
        int bestScore = 0;

        foreach (var stmt in _oemStatementsData.Statements)
        {
            int score = 0;
            var oemLower = stmt.Oem?.ToLowerInvariant() ?? "";
            var titleLower = stmt.Title?.ToLowerInvariant() ?? "";
            var summaryLower = stmt.Summary?.ToLowerInvariant() ?? "";
            var categoryLower = stmt.Category?.ToLowerInvariant() ?? "";

            // OEM name match - high score
            if (inputLower.Contains(oemLower.Split('/')[0].Trim().ToLower()))
                score += 8;

            // Check individual words from input
            var inputWords = inputLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !_stopWords.Contains(w))
                .ToArray();

            foreach (var word in inputWords)
            {
                if (oemLower.Contains(word))
                    score += 5;
                if (titleLower.Contains(word))
                    score += 3;
                if (categoryLower.Contains(word))
                    score += 2;
                if (summaryLower.Contains(word))
                    score += 1;
            }

            // Boost for specific scenarios
            if (inputLower.Contains("scan") && (categoryLower.Contains("scan") || titleLower.Contains("scan")))
                score += 5;
            if (inputLower.Contains("calibrat") && (categoryLower.Contains("calibrat") || titleLower.Contains("calibrat")))
                score += 5;
            if (inputLower.Contains("adas") && (titleLower.Contains("adas") || summaryLower.Contains("adas")))
                score += 5;
            if (inputLower.Contains("windshield") && summaryLower.Contains("windshield"))
                score += 4;
            if (inputLower.Contains("radar") && summaryLower.Contains("radar"))
                score += 4;
            if (inputLower.Contains("bumper") && summaryLower.Contains("bumper"))
                score += 4;

            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = stmt;
            }
        }

        // Only return if we have a decent match
        if (bestMatch != null && bestScore >= 4)
        {
            var response = $"📋 **{bestMatch.Oem} Position Statement**\n";
            response += $"**{bestMatch.Title}**\n";
            response += $"*({bestMatch.DateIssued})*\n\n";

            response += $"{bestMatch.Summary}\n";

            if (bestMatch.KeyPoints != null && bestMatch.KeyPoints.Count > 0)
            {
                response += "\n**Key Points:**\n";
                foreach (var point in bestMatch.KeyPoints.Take(4))
                {
                    response += $"• {point}\n";
                }
            }

            // Add documentation links
            response += "\n**📄 Documentation:**\n";
            if (!string.IsNullOrEmpty(bestMatch.PdfLink))
            {
                response += $"• PDF: {bestMatch.PdfLink}\n";
            }
            if (!string.IsNullOrEmpty(bestMatch.IcarLink))
            {
                response += $"• I-CAR: {bestMatch.IcarLink}\n";
            }
            if (!string.IsNullOrEmpty(bestMatch.Link))
            {
                response += $"• OEM Portal: {bestMatch.Link}\n";
            }

            return new ChatResponse
            {
                Message = response,
                Confidence = bestScore >= 10 ? 0.95 : (bestScore >= 6 ? 0.8 : 0.65),
                Category = "oem_statements"
            };
        }

        return null;
    }

    /// <summary>
    /// Search scanning/calibration knowledge for queries about health checks, battery support,
    /// OEM vs aftermarket tools, and billing questions.
    /// </summary>
    private ChatResponse? SearchScanningKnowledge(string input)
    {
        var inputLower = input.ToLowerInvariant();

        // Keywords that indicate a scanning/calibration query
        var isScanningQuery = inputLower.Contains("health check") ||
                              inputLower.Contains("battery support") ||
                              inputLower.Contains("jump box") ||
                              inputLower.Contains("trickle charger") ||
                              inputLower.Contains("midtronics") ||
                              inputLower.Contains("dca8000") ||
                              inputLower.Contains("dca-8000") ||
                              inputLower.Contains("oem vs aftermarket") ||
                              inputLower.Contains("aftermarket scan") ||
                              inputLower.Contains("oem scan tool") ||
                              inputLower.Contains("scan tool") ||
                              inputLower.Contains("dtc color") ||
                              inputLower.Contains("code color") ||
                              inputLower.Contains("four questions") ||
                              inputLower.Contains("4 questions") ||
                              inputLower.Contains("billing question") ||
                              inputLower.Contains("insurance only") ||
                              inputLower.Contains("$50 scan") ||
                              inputLower.Contains("50 dollar scan") ||
                              (inputLower.Contains("what") && inputLower.Contains("included") && inputLower.Contains("scan")) ||
                              (inputLower.Contains("steps") && inputLower.Contains("scan")) ||
                              (inputLower.Contains("calibration") && (inputLower.Contains("types") || inputLower.Contains("list")));

        if (!isScanningQuery)
            return null;

        // Battery support queries
        if (inputLower.Contains("battery support") || inputLower.Contains("jump box") ||
            inputLower.Contains("trickle charger") || inputLower.Contains("midtronics"))
        {
            var batteryInfo = _scanningKnowledge.GetBatterySupportInfo();
            var response = $"🔋 **{batteryInfo.Title}**\n\n";

            response += "**NOT Battery Support:**\n";
            foreach (var item in batteryInfo.CriticalInfo?.NotBatterySupport ?? new List<string>())
            {
                response += $"❌ {item}\n";
            }

            response += "\n**Why NOT:**\n";
            foreach (var reason in batteryInfo.CriticalInfo?.Reasons ?? new List<string>())
            {
                response += $"• {reason}\n";
            }

            response += $"\n**Best Choice:** {batteryInfo.CriticalInfo?.BestChoice}\n";
            response += $"**OEM Recommended:** {batteryInfo.CriticalInfo?.OemRecommended}\n\n";

            response += "**Why Battery Support Matters:**\n";
            foreach (var why in batteryInfo.WhyBatterySupportMatters ?? new List<string>())
            {
                response += $"• {why}\n";
            }

            return new ChatResponse
            {
                Message = response,
                Confidence = 0.95,
                Category = "scanning_knowledge"
            };
        }

        // Health check / scanning steps query
        if (inputLower.Contains("health check") || inputLower.Contains("steps") && inputLower.Contains("scan") ||
            (inputLower.Contains("what") && inputLower.Contains("included") && inputLower.Contains("scan")))
        {
            var procedure = _scanningKnowledge.GetHealthCheckProcedure();
            var response = $"📋 **{procedure.Title}**\n\n";
            response += $"**Base Labor:** {procedure.BaseLabor} - {procedure.BaseLaborDescription}\n\n";

            var includedSteps = _scanningKnowledge.GetIncludedSteps();
            var notIncludedSteps = _scanningKnowledge.GetNotIncludedSteps();

            response += "**INCLUDED in base 0.5m:**\n";
            foreach (var step in includedSteps.Take(8))
            {
                response += $"✅ {step.Step}. {step.Description}\n";
            }

            response += "\n**NOT INCLUDED (bill separately):**\n";
            foreach (var step in notIncludedSteps.Take(8))
            {
                response += $"➕ {step.Step}. {step.Description}\n";
            }

            response += $"\n*{notIncludedSteps.Count - 8} more steps... Full procedure has {procedure.Steps?.Count ?? 0} steps total.*";

            return new ChatResponse
            {
                Message = response,
                Confidence = 0.95,
                Category = "scanning_knowledge"
            };
        }

        // OEM vs Aftermarket query
        if (inputLower.Contains("oem vs aftermarket") || inputLower.Contains("aftermarket scan") || inputLower.Contains("oem scan tool"))
        {
            var comparison = _scanningKnowledge.GetOemVsAftermarketComparison();
            var response = "🔧 **Factory vs Third-Party Diagnostic Tools**\n\n";

            foreach (var item in comparison.Take(6))
            {
                response += $"**{item.Category}:**\n";
                response += $"  • OEM: {item.Oem}\n";
                response += $"  • Aftermarket: {item.Aftermarket}\n\n";
            }

            response += "\n**Questions to Ask Aftermarket Vendors:**\n";
            var questions = _scanningKnowledge.GetAftermarketQuestions();
            foreach (var q in questions.Take(3))
            {
                response += $"• {q}\n";
            }

            return new ChatResponse
            {
                Message = response,
                Confidence = 0.9,
                Category = "scanning_knowledge"
            };
        }

        // Four key questions / billing
        if (inputLower.Contains("four questions") || inputLower.Contains("4 questions") ||
            inputLower.Contains("billing question") || inputLower.Contains("insurance only") ||
            inputLower.Contains("$50 scan") || inputLower.Contains("50 dollar scan"))
        {
            var questions = _scanningKnowledge.GetFourKeyQuestions();
            var myths = _scanningKnowledge.GetScanningMyths();

            var response = "💡 **Determining Billable Operations**\n\n";
            response += "*Constantly ask yourself these questions:*\n\n";

            foreach (var q in questions)
            {
                response += $"**{q.Number}.** {q.Question}\n";
            }

            response += $"\n---\n\n**Myth:** \"{myths.Myth}\"\n\n";
            response += "**How to Respond:**\n";
            foreach (var r in myths.Responses ?? new List<MythResponse>())
            {
                response += $"{r.Step}. {r.Action}\n";
                if (r.Examples != null && r.Examples.Count > 0)
                {
                    foreach (var ex in r.Examples)
                    {
                        response += $"   • {ex}\n";
                    }
                }
            }

            response += "\n📖 For OEM position statements, visit oemonestop.com";

            return new ChatResponse
            {
                Message = response,
                Confidence = 0.95,
                Category = "scanning_knowledge"
            };
        }

        // DTC code colors
        if (inputLower.Contains("dtc color") || inputLower.Contains("code color"))
        {
            var colors = _scanningKnowledge.GetDtcCodeColors();
            var response = "🚦 **DTC Code Status Colors (OEM Tools)**\n\n";

            foreach (var c in colors)
            {
                var emoji = c.Color switch
                {
                    "Red" => "🔴",
                    "Yellow" => "🟡",
                    "Green" => "🟢",
                    _ => "⚪"
                };
                response += $"{emoji} **{c.Color}**: {c.Meaning}\n   → {c.Action}\n\n";
            }

            return new ChatResponse
            {
                Message = response,
                Confidence = 0.95,
                Category = "scanning_knowledge"
            };
        }

        // Calibration types
        if (inputLower.Contains("calibration") && (inputLower.Contains("types") || inputLower.Contains("list")))
        {
            var types = _scanningKnowledge.GetCalibrationTypes();
            var response = "🎯 **Common Calibration Types** (NOT INCLUDED - bill separately)\n\n";

            foreach (var t in types)
            {
                response += $"• **{t.Type}**: {t.Description}\n";
            }

            return new ChatResponse
            {
                Message = response,
                Confidence = 0.9,
                Category = "scanning_knowledge"
            };
        }

        return null;
    }

    /// <summary>
    /// Search for teardown/disassembly checklist queries.
    /// Generates inspection checklists for specific parts during disassembly.
    /// Example: "what should I check when pulling the bumper?"
    /// </summary>
    private ChatResponse? SearchTeardownChecklist(string input)
    {
        var inputLower = input.ToLowerInvariant();

        // Keywords that indicate a teardown/disassembly query
        var isTeardownQuery = inputLower.Contains("teardown") ||
                              inputLower.Contains("disassembl") ||
                              inputLower.Contains("pulling") ||
                              inputLower.Contains("pull the") ||
                              inputLower.Contains("remove the") ||
                              inputLower.Contains("what to check") ||
                              inputLower.Contains("what should i check") ||
                              inputLower.Contains("look for when") ||
                              inputLower.Contains("inspect when") ||
                              inputLower.Contains("checklist for") ||
                              (inputLower.Contains("check") && inputLower.Contains("when"));

        if (!isTeardownQuery)
            return null;

        // Identify which part/area they're asking about
        var partChecks = new Dictionary<string, (string title, List<string> items)>
        {
            ["bumper"] = ("Front/Rear Bumper Teardown", new List<string>
            {
                "Check bumper reinforcement for bends/kinks [PHOTO]",
                "Check absorber/foam for compression damage [PHOTO]",
                "Inspect mounting brackets for cracks/bends [PHOTO]",
                "Check for pushed sensors (parking, radar) [PHOTO]",
                "Verify bumper cover tabs are intact",
                "Check air dam/lower valance for damage",
                "Inspect fog light housings if equipped",
                "Check tow hook cover/trim pieces"
            }),
            ["headlight"] = ("Headlight Assembly Teardown", new List<string>
            {
                "Check headlight bracket/mounts for cracks [PHOTO]",
                "Inspect lens for internal moisture/cracks [PHOTO]",
                "Verify all bulb sockets are intact",
                "Check adjuster mechanisms function",
                "Inspect wiring harness connectors"
            }),
            ["fender"] = ("Fender Teardown", new List<string>
            {
                "Check inner fender apron for damage [PHOTO]",
                "Inspect fender mounting points/bolts",
                "Check for pushed A-pillar [PHOTO]",
                "Inspect fender liner for tears/missing clips",
                "Check door gap alignment",
                "Verify antenna mount if equipped"
            }),
            ["door"] = ("Door Assembly Teardown", new List<string>
            {
                "Check door intrusion beam condition [PHOTO]",
                "Inspect hinge mounts for damage",
                "Verify door latch striker alignment",
                "Check inner door panel clips/mounts",
                "Inspect window regulator operation",
                "Check side impact airbag module [PHOTO]",
                "Verify door wiring harness",
                "Check door seal condition"
            }),
            ["hood"] = ("Hood Teardown", new List<string>
            {
                "Check hood inner structure [PHOTO]",
                "Inspect hood hinges for bends",
                "Verify latch mechanism operation",
                "Check hood struts/props",
                "Inspect insulator pad condition"
            }),
            ["radiator"] = ("Radiator/Cooling Teardown", new List<string>
            {
                "Check radiator support for bends/damage [PHOTO]",
                "Inspect radiator fins for damage",
                "Check for coolant leaks",
                "Inspect A/C condenser condition [PHOTO]",
                "Check fan shroud/assembly",
                "Verify all hose connections"
            }),
            ["condenser"] = ("A/C System Teardown", new List<string>
            {
                "Check condenser for leaks/damage [PHOTO]",
                "Inspect A/C lines for damage",
                "NOTE: Document refrigerant type (R134a/R1234yf) - important for supplement",
                "Check compressor mounting"
            }),
            ["quarter"] = ("Quarter Panel Teardown", new List<string>
            {
                "Check inner wheelhouse for damage [PHOTO]",
                "Inspect floor pan for buckles [PHOTO]",
                "Check rear body mounts",
                "Inspect trunk floor/spare tire well",
                "Verify fuel filler neck clearance",
                "Check quarter glass/trim"
            }),
            ["suspension"] = ("Suspension Teardown", new List<string>
            {
                "Check strut/shock for leaks/damage [PHOTO]",
                "Inspect strut tower for damage [PHOTO]",
                "Check control arms for bends",
                "Inspect ball joints/tie rods",
                "Verify wheel bearing condition",
                "Check subframe/cradle mounting [PHOTO]"
            }),
            ["airbag"] = ("Airbag/SRS Inspection", new List<string>
            {
                "Document all deployed airbags [PHOTO each]",
                "Check seatbelt pretensioners [PHOTO]",
                "Inspect clockspring/steering column",
                "Verify all airbag modules present",
                "Document SRS warning light status"
            }),
            ["frame"] = ("Frame/Structural Inspection", new List<string>
            {
                "Check frame rails for damage [PHOTO]",
                "Inspect unibody for buckles/kinks [PHOTO]",
                "Check crush zones/crumple areas",
                "Measure key reference points - document measurements",
                "Inspect floor pan for deformation [PHOTO]",
                "Check rocker panels"
            }),
            ["grille"] = ("Grille/Front End Teardown", new List<string>
            {
                "Check grille mounting tabs",
                "Inspect active grille shutters if equipped",
                "Check radar/sensor mounting behind grille [PHOTO]"
            }),
            ["taillight"] = ("Taillight Assembly Teardown", new List<string>
            {
                "Check taillight bracket/mounts",
                "Inspect lens for cracks/moisture [PHOTO]",
                "Verify all bulb sockets work",
                "Check backup camera housing if equipped"
            }),
            ["trunk"] = ("Trunk/Rear Teardown", new List<string>
            {
                "Check trunk floor for damage [PHOTO]",
                "Inspect trunk hinges",
                "Verify latch/striker alignment",
                "Check trunk weatherstrip",
                "Inspect spare tire well"
            }),
            ["roof"] = ("Roof Inspection", new List<string>
            {
                "Check for roof panel deformation [PHOTO]",
                "Inspect roof rails/drip moldings",
                "Check sunroof mechanism if equipped",
                "Verify headliner condition",
                "Check A/B/C pillar connections"
            })
        };

        // Find matching part
        string? matchedPart = null;
        foreach (var part in partChecks.Keys)
        {
            if (inputLower.Contains(part))
            {
                matchedPart = part;
                break;
            }
        }

        // If no specific part matched, provide general teardown guidance
        if (matchedPart == null)
        {
            var generalResponse = "**General Teardown Checklist:**\n\n" +
                "Ask about a specific part for detailed inspection items:\n" +
                "• \"What should I check when pulling the **bumper**?\"\n" +
                "• \"Teardown checklist for **fender**\"\n" +
                "• \"What to look for when removing the **door**?\"\n\n" +
                "**Available parts:** bumper, headlight, fender, door, hood, radiator, condenser, quarter panel, suspension, airbag/SRS, frame, grille, taillight, trunk, roof\n\n" +
                "**General tips:**\n" +
                "• Take 4-corner photos before teardown\n" +
                "• Document odometer and VIN\n" +
                "• Photo all damage areas before disassembly\n" +
                "• Mark hidden damage with tape for later photos";

            return new ChatResponse
            {
                Message = generalResponse,
                Confidence = 0.85,
                Category = "teardown_checklist",
                RelatedTopics = new List<string> { "bumper teardown", "fender inspection", "door checklist" }
            };
        }

        // Build response for matched part
        var (title, items) = partChecks[matchedPart];
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"**{title}**\n");
        sb.AppendLine("When you pull this part, check these items:\n");

        int itemNum = 1;
        foreach (var item in items)
        {
            var isPhoto = item.Contains("[PHOTO");
            var displayItem = item.Replace("[PHOTO]", "").Replace("[PHOTO each]", "").Trim();
            var photoIcon = isPhoto ? " 📷" : "";
            sb.AppendLine($"{itemNum}. {displayItem}{photoIcon}");
            itemNum++;
        }

        sb.AppendLine("\n📷 = Photo recommended for documentation");
        sb.AppendLine("\n**Tip:** Document any hidden damage found during disassembly for supplement.");

        return new ChatResponse
        {
            Message = sb.ToString(),
            Confidence = 0.95,
            Category = "teardown_checklist",
            RelatedTopics = new List<string> { "supplement items", "hidden damage", "photo documentation" }
        };
    }

    /// <summary>
    /// Search for estimate-related queries like "what operations do I need for a bumper with 3.0 refinish?"
    /// Uses learned patterns from imported estimates to provide scaled suggestions.
    /// </summary>
    private ChatResponse? SearchEstimateQuery(string input)
    {
        var inputLower = input.ToLowerInvariant();

        // Keywords that indicate an estimate operation query
        var isEstimateQuery = inputLower.Contains("what operations") ||
                              inputLower.Contains("what do i need") ||
                              inputLower.Contains("operations do i need") ||
                              inputLower.Contains("replacing a") ||
                              inputLower.Contains("repairing a") ||
                              inputLower.Contains("refinish") ||
                              inputLower.Contains("labor units") ||
                              inputLower.Contains("refinish units") ||
                              (inputLower.Contains("bumper") && (inputLower.Contains("replace") || inputLower.Contains("repair"))) ||
                              (inputLower.Contains("fender") && (inputLower.Contains("replace") || inputLower.Contains("repair"))) ||
                              (inputLower.Contains("door") && (inputLower.Contains("replace") || inputLower.Contains("repair"))) ||
                              (inputLower.Contains("hood") && (inputLower.Contains("replace") || inputLower.Contains("repair"))) ||
                              (inputLower.Contains("quarter") && (inputLower.Contains("replace") || inputLower.Contains("repair")));

        if (!isEstimateQuery)
            return null;

        // Extract part name
        string? partName = ExtractPartFromQuery(inputLower);
        if (string.IsNullOrEmpty(partName))
            return null;

        // Extract operation type
        string operationType = "Replace";
        if (inputLower.Contains("repair"))
            operationType = "Repair";
        else if (inputLower.Contains("r&i") || inputLower.Contains("remove"))
            operationType = "R&I";
        else if (inputLower.Contains("refinish") && !inputLower.Contains("replace"))
            operationType = "Refinish";

        // Extract labor/refinish units from query
        decimal? laborUnits = ExtractUnits(input, "labor");
        decimal? refinishUnits = ExtractUnits(input, "refinish");

        // If no specific units mentioned but has numbers, try to extract them
        if (!laborUnits.HasValue && !refinishUnits.HasValue)
        {
            var numbersInQuery = Regex.Matches(input, @"(\d+\.?\d*)");
            if (numbersInQuery.Count > 0)
            {
                // Assume first number is refinish if query mentions refinish, otherwise labor
                if (decimal.TryParse(numbersInQuery[0].Value, out var firstNum))
                {
                    if (inputLower.Contains("refinish") || inputLower.Contains("paint"))
                        refinishUnits = firstNum;
                    else
                        laborUnits = firstNum;
                }
            }
        }

        // Query the learning service
        var result = _learningService.QueryOperationsWithUnits(partName, operationType, laborUnits, refinishUnits);

        if (result.Operations.Count == 0)
        {
            // No learned data - provide helpful response
            return new ChatResponse
            {
                Message = $"🔍 **No learned patterns found for '{partName}' ({operationType})**\n\n" +
                         "The AI hasn't been trained on this part yet.\n\n" +
                         "**To train the system:**\n" +
                         "1. Go to Import tab\n" +
                         "2. Select LEARN mode\n" +
                         "3. Paste an estimate with this part\n" +
                         "4. Click Train to teach the system\n\n" +
                         "Once trained, I can suggest operations with scaled calculations!",
                Confidence = 0.7,
                Category = "estimate_query"
            };
        }

        // Build response with operations
        var response = $"🔧 **Operations for {partName} ({operationType})**\n";

        if (refinishUnits.HasValue)
            response += $"*Input: {refinishUnits:F1} refinish units*\n";
        if (laborUnits.HasValue)
            response += $"*Input: {laborUnits:F1} labor units*\n";

        if (result.ScaleFactor != 1.0m)
        {
            response += $"*Scaled {result.ScaleFactor:F2}x from typical {result.TypicalRefinishUnits:F1} ref / {result.TypicalLaborUnits:F1} labor*\n";
        }

        response += "\n**Suggested Operations:**\n";

        // Group by category
        var regularOps = result.Operations.Where(o => !o.IsManualLine).ToList();
        var manualLines = result.Operations.Where(o => o.IsManualLine).ToList();

        foreach (var op in regularOps.Take(8))
        {
            var hours = op.ScaledLaborHours > 0 ? $"{op.ScaledLaborHours:F2} labor" :
                       op.ScaledRefinishHours > 0 ? $"{op.ScaledRefinishHours:F2} refinish" : "";
            var confidence = op.Confidence >= 0.8 ? "✅" : op.Confidence >= 0.5 ? "🟡" : "❓";
            response += $"{confidence} **{op.OperationType}** - {op.Description}";
            if (!string.IsNullOrEmpty(hours))
                response += $" ({hours})";
            response += "\n";
        }

        if (manualLines.Count > 0)
        {
            response += "\n**Manual Lines (typically included):**\n";
            foreach (var ml in manualLines.Take(5))
            {
                var hours = ml.ScaledLaborHours > 0 ? $"{ml.ScaledLaborHours:F2}" : "";
                response += $"• {ml.Description}";
                if (!string.IsNullOrEmpty(hours))
                    response += $" - {hours} units";
                response += "\n";
            }
        }

        response += $"\n*Based on {result.Operations.FirstOrDefault()?.ExampleCount ?? 0} learned examples*";

        return new ChatResponse
        {
            Message = response,
            Confidence = result.Operations.Any() ? result.Operations.Max(o => o.Confidence) : 0.5,
            Category = "estimate_query"
        };
    }

    /// <summary>
    /// Extract part name from a query like "replacing a front bumper"
    /// </summary>
    private string? ExtractPartFromQuery(string input)
    {
        var knownParts = new[]
        {
            "front bumper cover", "rear bumper cover", "bumper cover",
            "front bumper", "rear bumper", "bumper",
            "front fender", "rear fender", "fender",
            "hood", "trunk lid", "trunk", "liftgate", "tailgate",
            "front door", "rear door", "door",
            "quarter panel", "quarter",
            "rocker panel", "rocker",
            "grille", "radiator support",
            "headlight", "taillight", "fog light",
            "mirror", "door mirror", "side mirror",
            "windshield", "back glass",
            "roof", "a-pillar", "b-pillar", "c-pillar"
        };

        // Check from longest to shortest (more specific first)
        foreach (var part in knownParts.OrderByDescending(p => p.Length))
        {
            if (input.Contains(part))
                return part;
        }

        return null;
    }

    /// <summary>
    /// Extract labor or refinish units from query
    /// e.g., "3.0 refinish units" → 3.0 for refinish
    /// </summary>
    private decimal? ExtractUnits(string input, string unitType)
    {
        // Patterns like "3.0 refinish", "2.5 labor units", "refinish 3.0"
        var patterns = new[]
        {
            $@"(\d+\.?\d*)\s*{unitType}",
            $@"{unitType}\s*(\d+\.?\d*)",
            $@"(\d+\.?\d*)\s*{unitType}\s*(?:units?|hrs?|hours?)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, out var units))
            {
                return units;
            }
        }

        return null;
    }

    /// <summary>
    /// Search for accuracy/heatmap queries - "where am I off?", "my accuracy", "leaving money"
    /// Uses the EstimateAccuracyService to provide personalized insights.
    /// </summary>
    private ChatResponse? SearchAccuracyQuery(string input)
    {
        var inputLower = input.ToLowerInvariant();

        // Keywords that indicate an accuracy/heatmap query
        var accuracyKeywords = new[]
        {
            "accuracy", "accurate", "where am i off", "where i'm off", "am i off",
            "heatmap", "heat map", "supplement pattern", "supplement rate",
            "leaving money", "left on table", "money on the table", "underbilling",
            "under billing", "overbilling", "over billing", "underestimate", "overestimate",
            "how accurate", "my accuracy", "accuracy report", "accuracy stats",
            "where do i miss", "what do i miss", "commonly miss", "keep missing",
            "supplement history", "my supplements", "supplement trends",
            "retirement fund", "retirement", "roi", "potential revenue", "yearly gain",
            "how much money", "find money", "find revenue", "missed revenue"
        };

        var isAccuracyQuery = accuracyKeywords.Any(kw => inputLower.Contains(kw));

        if (!isAccuracyQuery)
            return null;

        // Determine what type of response they want
        string message;

        // "Retirement Fund Finder" - comprehensive analysis with ROI
        if (inputLower.Contains("retirement") || inputLower.Contains("roi") ||
            inputLower.Contains("yearly") || inputLower.Contains("potential") ||
            inputLower.Contains("find money") || inputLower.Contains("find revenue") ||
            inputLower.Contains("how much money"))
        {
            message = _accuracyService.FormatRetirementFundFinder();
        }
        else if (inputLower.Contains("money") || inputLower.Contains("table") || inputLower.Contains("billing"))
        {
            // Money-focused response
            message = _accuracyService.FormatMoneyLeftOnTable();
        }
        else if (inputLower.Contains("tip") || inputLower.Contains("improve") || inputLower.Contains("suggestion"))
        {
            // Tips-focused response
            var tips = _accuracyService.GetPersonalizedTips();
            if (tips.Count == 0)
            {
                message = "💡 **No tips yet.**\n\nLog more estimate feedback to get personalized improvement suggestions!";
            }
            else
            {
                message = "💡 **Personalized Tips Based on Your Patterns:**\n\n";
                foreach (var tip in tips)
                {
                    var icon = tip.Priority switch
                    {
                        "high" => "🔴",
                        "medium" => "🟡",
                        "positive" => "🟢",
                        _ => "💡"
                    };
                    message += $"{icon} **{tip.Category}**: {tip.Tip}\n\n";
                }
            }
        }
        else if (inputLower.Contains("door") || inputLower.Contains("fender") || inputLower.Contains("bumper") ||
                 inputLower.Contains("quarter") || inputLower.Contains("hood"))
        {
            // Category-specific query
            var category = ExtractPartFromQuery(inputLower) ?? "Unknown";
            var catAccuracy = _accuracyService.GetCategoryAccuracy(category);

            if (catAccuracy == null || catAccuracy.SupplementCount == 0)
            {
                message = $"📊 **{category} Accuracy**\n\nNo supplement data for {category} yet.\n\n" +
                         "Log some estimate outcomes to track your accuracy on this part!";
            }
            else
            {
                message = $"📊 **{category} Accuracy**\n\n";
                message += $"Supplemented: **{catAccuracy.SupplementCount}** times\n";
                if (catAccuracy.AverageHoursAdded > 0)
                    message += $"Average hours added: **+{catAccuracy.AverageHoursAdded:F1} hrs**\n";
                if (catAccuracy.TotalMaterialsAdded > 0)
                    message += $"Total materials added: **${catAccuracy.TotalMaterialsAdded:N0}**\n";
                if (catAccuracy.CommonMissedItems.Any())
                    message += $"\nCommon misses: {string.Join(", ", catAccuracy.CommonMissedItems)}";
            }
        }
        else
        {
            // General accuracy overview
            message = _accuracyService.FormatForChat();
        }

        return new ChatResponse
        {
            Message = message,
            Confidence = 0.95,
            Category = "accuracy",
            RelatedTopics = new List<string>
            {
                "Retirement Fund Finder",
                "Where am I leaving money?",
                "My door accuracy",
                "Accuracy tips"
            }
        };
    }

    /// <summary>
    /// Search estimate history database for insurance payment patterns, similar estimates, etc.
    /// This powers the "Estimate DNA" feature - asking questions about past estimates.
    /// Examples:
    /// - "What estimate did Allstate pay corrosion protection?"
    /// - "How many times did State Farm pay for pre-scan?"
    /// - "Which insurers pay for cavity wax?"
    /// </summary>
    private ChatResponse? SearchEstimateHistory(string input)
    {
        var result = _estimateQueryService.ProcessQuery(input);

        if (result == null)
            return null;

        return new ChatResponse
        {
            Message = result.Message,
            Confidence = result.Success ? 0.9 : 0.5,
            Category = "estimate-history",
            RelatedTopics = new List<string>
            {
                "My estimate statistics",
                "Who pays for corrosion protection?",
                "What does Allstate pay for?",
                "Find similar estimates"
            }
        };
    }

    /// <summary>
    /// Get a response for the user's question
    /// </summary>
    public ChatResponse GetResponse(string userInput)
    {
        var input = userInput.Trim().ToLowerInvariant();

        // Handle greetings
        if (IsGreeting(input))
        {
            return new ChatResponse
            {
                Message = "Hello! I'm the McStud Assistant. I can help you with:\n\n" +
                         "🧬 **Estimate DNA / Insurance Payments**\n" +
                         "• 'Did Allstate pay for corrosion protection?'\n" +
                         "• 'How many times did State Farm pay for pre-scan?'\n" +
                         "• 'Who pays for cavity wax?'\n" +
                         "• 'Show my estimate statistics'\n\n" +
                         "🧭 **App Navigation**\n" +
                         "• How to use the Export tab (Excel to CCC/Mitchell)\n" +
                         "• How to use Damage Estimator interview\n" +
                         "• Hotkeys (Ctrl+Alt+V for quick export)\n\n" +
                         "📖 **Definitions & P-Pages**\n" +
                         "• Ask about operations (e.g., 'what is seam sealer?')\n" +
                         "• P-page info (e.g., 'corrosion protection')\n" +
                         "• DEG inquiries (e.g., 'scans DEG')\n\n" +
                         "📋 **OEM Position Statements**\n" +
                         "• Ask by OEM (e.g., 'Honda scanning', 'Toyota ADAS')\n" +
                         "• Get documentation links for insurance proof\n\n" +
                         "📊 **Your Accuracy Heatmap**\n" +
                         "• 'Where am I off?' - See your supplement patterns\n" +
                         "• 'Where am I leaving money?' - Find missed revenue\n\n" +
                         "What would you like to know?",
                Confidence = 1.0,
                Category = "greeting"
            };
        }

        // Check for founder/creator questions
        if (IsFounderQuery(input))
        {
            return GetFounderResponse();
        }

        // Check for methodology/philosophy questions
        if (IsMethodologyQuery(input))
        {
            return GetMethodologyResponse();
        }

        // Check for accuracy/heatmap/supplement pattern queries
        var accuracyResponse = SearchAccuracyQuery(input);
        if (accuracyResponse != null)
            return accuracyResponse;

        // Check for estimate history/DNA queries (insurance payment patterns, similar estimates)
        var historyResponse = SearchEstimateHistory(input);
        if (historyResponse != null)
            return historyResponse;

        // Extract the main topic from the question
        var topic = ExtractTopic(input);

        // Check terminology, calculations, blend formulas, order of operations queries first
        var terminologyResponse = SearchTerminologyAndCalculations(input);
        if (terminologyResponse != null)
            return terminologyResponse;

        // Check included/not-included queries FIRST - these have priority for operation details
        // ("what's included with quarter panel replace", "operations for bumper", "i need operations for fender")
        var includedResponse = SearchIncludedNotIncluded(input);
        if (includedResponse != null)
            return includedResponse;

        // Check operation tab navigation ("where can I find bumper operations")
        // Only runs if IncludedNotIncluded didn't match
        var tabNavResponse = SearchOperationTabNavigation(input);
        if (tabNavResponse != null)
            return tabNavResponse;

        // Check SCRS operations for suggestions first (general part queries)
        var operationsResponse = SearchOperations(input);
        if (operationsResponse != null)
            return operationsResponse;

        // Check estimate operation queries (uses learned patterns for specific scaling)
        var estimateResponse = SearchEstimateQuery(input);
        if (estimateResponse != null)
            return estimateResponse;

        // Check app navigation queries first
        var navResponse = SearchAppNavigation(input);
        if (navResponse != null)
            return navResponse;

        // Check OEM Position Statements for OEM/manufacturer queries
        var oemResponse = SearchOEMStatements(input);
        if (oemResponse != null)
            return oemResponse;

        // Check scanning/calibration knowledge (battery support, health check, OEM vs aftermarket)
        var scanningResponse = SearchScanningKnowledge(input);
        if (scanningResponse != null)
            return scanningResponse;

        // Check teardown/disassembly checklist queries
        var teardownResponse = SearchTeardownChecklist(input);
        if (teardownResponse != null)
            return teardownResponse;

        // Check definitions for operation/P-page queries
        var definitionResponse = SearchDefinitions(input);
        if (definitionResponse != null)
            return definitionResponse;

        // Check estimating notes (your comprehensive notes from training)
        var notesResponse = SearchEstimatingNotes(input);
        if (notesResponse != null)
            return notesResponse;

        // Check if this is an Excel/location query first
        if (IsExcelQuery(input))
        {
            var excelResponse = GetExcelResponse(input);
            if (excelResponse != null)
                return excelResponse;
        }

        // Try the knowledge base if loaded
        if (_isLoaded && _knowledgeBase != null)
        {
            // Pass the extracted topic to improve matching
            var bestMatch = FindBestMatch(input, topic);

            if (bestMatch.entry != null && bestMatch.score > 0.3)
            {
                var message = bestMatch.entry.Answer;

                // For location questions, ALWAYS ensure we show the tab/sheet clearly
                if (IsLocationQuestion(input))
                {
                    // Add location header based on category
                    if (bestMatch.entry.Category == "tabs")
                    {
                        var tabName = bestMatch.entry.Question.Replace("What is the ", "").Replace(" tab?", "");
                        if (!message.StartsWith("📍") && !message.StartsWith("**Location"))
                        {
                            message = $"📍 **Tab:** {tabName}\n\n{message}";
                        }
                    }
                    else if (bestMatch.entry.Category == "inputs")
                    {
                        // Extract tab from answer if mentioned, otherwise add it
                        var tabHint = GetTabFromKeywords(bestMatch.entry.Keywords);
                        if (!string.IsNullOrEmpty(tabHint) && !message.Contains(tabHint))
                        {
                            message = $"📍 **Tab:** {tabHint}\n\n{message}";
                        }
                    }
                }

                return new ChatResponse
                {
                    Message = message,
                    Confidence = bestMatch.score,
                    Category = bestMatch.entry.Category,
                    RelatedTopics = GetRelatedTopics(bestMatch.entry)
                };
            }
        }

        // Try Excel knowledge as fallback
        var fallbackExcel = GetExcelResponse(input);
        if (fallbackExcel != null)
            return fallbackExcel;

        // No good match found
        return new ChatResponse
        {
            Message = "I don't have information on that topic.\n\n" +
                     "**I can help with:**\n" +
                     "• Operation details (bumper, fender, quarter panel, etc.)\n" +
                     "• P-Page included/not included info\n" +
                     "• OEM position statements (Honda, Toyota, etc.)\n" +
                     "• Excel sheet navigation\n" +
                     "• DEG inquiries\n\n" +
                     "**Need help with something else?**\n" +
                     "Email: Mcstudestimating@gmail.com",
            Confidence = 0,
            Category = null,
            RelatedTopics = _knowledgeBase?.QuickReplies.Take(5).ToList()
        };
    }

    /// <summary>
    /// Check if question is asking for location
    /// </summary>
    private bool IsLocationQuestion(string input)
    {
        return input.Contains("where") || input.Contains("find") || input.Contains("located") || input.Contains("location");
    }

    /// <summary>
    /// Get the tab name from keywords (used to ensure we always show the tab)
    /// </summary>
    private string GetTabFromKeywords(List<string> keywords)
    {
        // Map common keywords to their tabs
        var keywordToTab = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // SOP List keywords
            ["a29"] = "SOP List", ["a31"] = "SOP List", ["a33"] = "SOP List", ["a35"] = "SOP List",
            ["a79"] = "SOP List", ["a81"] = "SOP List", ["a87"] = "SOP List", ["c29"] = "SOP List",
            ["battery"] = "SOP List", ["adas"] = "SOP List", ["scan"] = "SOP List", ["diagnostic"] = "SOP List",
            ["gateway"] = "SOP List", ["vehicle type"] = "SOP List", ["pre wash"] = "SOP List",
            ["bio hazard"] = "SOP List", ["drive cycle"] = "SOP List",

            // Part Operations keywords
            ["part operations"] = "Part Operations", ["panel"] = "Part Operations", ["first panel"] = "Part Operations",
            ["additional panel"] = "Part Operations", ["bumper"] = "Part Operations", ["fender"] = "Part Operations",
            ["door"] = "Part Operations", ["hood"] = "Part Operations", ["quarter"] = "Part Operations",

            // Body Operations keywords
            ["body operations"] = "Body Operations", ["welding"] = "Body Operations", ["weld"] = "Body Operations",
            ["structural"] = "Body Operations", ["frame"] = "Body Operations", ["dent"] = "Body Operations",

            // Refinish Operations keywords
            ["refinish"] = "Refinish Operations", ["paint"] = "Refinish Operations", ["blend"] = "Refinish Operations",
            ["clear coat"] = "Refinish Operations", ["color match"] = "Refinish Operations",

            // Mechanical Operations keywords
            ["mechanical"] = "Mechanical Operations", ["alignment"] = "Mechanical Operations",
            ["ac"] = "Mechanical Operations", ["suspension"] = "Mechanical Operations", ["calibration"] = "Mechanical Operations",

            // SRS Operations keywords
            ["srs"] = "SRS Operations", ["airbag"] = "SRS Operations", ["seatbelt"] = "SRS Operations",

            // Cover Car keywords
            ["cover car"] = "Cover Car Operations", ["masking"] = "Cover Car Operations",

            // Total Loss keywords
            ["total loss"] = "Total Loss Charges", ["storage"] = "Total Loss Charges",

            // Other tabs
            ["body on frame"] = "Body On Frame", ["stolen"] = "Stolen Recovery"
        };

        foreach (var keyword in keywords)
        {
            var lower = keyword.ToLowerInvariant();
            if (keywordToTab.TryGetValue(lower, out var tab))
                return tab;

            // Also check if any key is contained in the keyword
            foreach (var kvp in keywordToTab)
            {
                if (lower.Contains(kvp.Key) || kvp.Key.Contains(lower))
                    return kvp.Value;
            }
        }

        return "";
    }

    /// <summary>
    /// Extract the main topic/subject from a question
    /// "where can I find welding" -> "welding"
    /// "what is ADAS" -> "adas"
    /// </summary>
    private string ExtractTopic(string input)
    {
        // Remove question words and filler words to get the core topic
        var fillerWords = new[] {
            "where", "what", "how", "when", "why", "which", "who",
            "can", "could", "would", "should", "do", "does", "did",
            "is", "are", "was", "were", "be", "been", "being",
            "i", "me", "my", "you", "your", "we", "the", "a", "an",
            "find", "located", "location", "enter", "input", "go",
            "to", "in", "on", "at", "for", "about", "tell", "explain"
        };

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2 && !fillerWords.Contains(w))
            .ToArray();

        return string.Join(" ", words);
    }

    /// <summary>
    /// Check if query is likely about Excel locations/sheets
    /// </summary>
    private bool IsExcelQuery(string input)
    {
        var excelKeywords = new[] {
            "where", "find", "located", "location", "enter", "input",
            "sheet", "cell", "excel", "spreadsheet",
            "sop list", "part operations", "body operations", "refinish",
            "mechanical", "cover car", "total loss", "srs", "adas"
        };
        return excelKeywords.Any(k => input.Contains(k));
    }

    /// <summary>
    /// Get response from Excel knowledge service
    /// </summary>
    private ChatResponse? GetExcelResponse(string input)
    {
        // Try answer question first
        var result = _excelKnowledge.AnswerQuestion(input);
        if (result.Success)
        {
            var message = result.Answer;
            if (result.Locations.Count > 0)
            {
                message += "\n\n📍 Locations:";
                foreach (var loc in result.Locations.Take(5))
                {
                    message += $"\n• {loc.Label} → {loc.Sheet}!{loc.Cell}";
                }
            }
            return new ChatResponse
            {
                Message = message,
                Confidence = 0.8,
                Category = "excel"
            };
        }

        // Try location search
        var locResult = _excelKnowledge.FindLocation(input);
        if (locResult.Success && locResult.Locations.Count > 0)
        {
            var message = locResult.Answer;
            message += "\n\n📍 Found:";
            foreach (var loc in locResult.Locations.Take(5))
            {
                message += $"\n• {loc.Label} → {loc.Sheet}!{loc.Cell}";
                if (!string.IsNullOrEmpty(loc.Description))
                    message += $"\n   ({loc.Description})";
            }
            return new ChatResponse
            {
                Message = message,
                Confidence = 0.7,
                Category = "excel"
            };
        }

        // Try search inputs
        var searchResult = _excelKnowledge.SearchInputs(input);
        if (searchResult.Success && searchResult.Locations.Count > 0)
        {
            var message = $"🔍 Found {searchResult.Locations.Count} related inputs:";
            foreach (var loc in searchResult.Locations.Take(6))
            {
                message += $"\n• {loc.Label} → {loc.Sheet}!{loc.Cell}";
            }
            if (searchResult.Locations.Count > 6)
                message += $"\n... and {searchResult.Locations.Count - 6} more";
            return new ChatResponse
            {
                Message = message,
                Confidence = 0.6,
                Category = "excel"
            };
        }

        return null;
    }

    private bool IsGreeting(string input)
    {
        var greetings = new[] { "hi", "hello", "hey", "help", "start", "?" };

        // Check if it's a pure greeting (just the greeting word alone)
        if (greetings.Contains(input))
            return true;

        // If message starts with greeting but has substantive content, it's NOT just a greeting
        // Check for part names, operations, or numbers which indicate a real query
        var substantiveKeywords = new[] {
            "bumper", "fender", "door", "hood", "quarter", "panel", "roof", "rocker",
            "replace", "repair", "refinish", "paint", "body", "labor",
            "operations", "what", "how", "missing", "include", "calculate"
        };

        if (substantiveKeywords.Any(k => input.Contains(k)))
            return false; // Has real content, not just a greeting

        // Check for numbers (like "3.0 refinish")
        if (System.Text.RegularExpressions.Regex.IsMatch(input, @"\d+\.?\d*"))
            return false; // Has numbers, likely a real query

        // Only treat as greeting if it's very short (just greeting + maybe 1-2 words)
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 3 && greetings.Any(g => input.StartsWith(g + " "));
    }

    private bool IsFounderQuery(string input)
    {
        var founderKeywords = new[] {
            "founder", "creator", "who made", "who created", "who built", "who developed",
            "who is anthony", "anthony mcnee", "mcnee", "who are you made by",
            "who wrote", "who designed", "about the developer", "about the creator",
            "blueprinter of the year", "best estimator", "best repair planner"
        };

        return founderKeywords.Any(k => input.Contains(k));
    }

    private bool IsMethodologyQuery(string input)
    {
        var methodologyKeywords = new[] {
            "systematic", "methodology", "philosophy", "approach", "why mcstud",
            "how does mcstud work", "what is mcstud", "about mcstud", "met system",
            "set it and forget it", "why this tool", "why use this", "purpose",
            "how it works", "the mcstud way", "estimating method", "why systematic",
            "importance of", "why accurate", "why consistent", "mission"
        };

        return methodologyKeywords.Any(k => input.Contains(k));
    }

    private ChatResponse GetMethodologyResponse()
    {
        return new ChatResponse
        {
            Message = "**THE MCSTUD SYSTEMATIC APPROACH**\n" +
                     "════════════════════════════════════════\n\n" +

                     "**🎯 THE PROBLEM WE SOLVE**\n" +
                     "Modern collision repair estimating faces critical challenges:\n" +
                     "• Overlooked operations = Lost revenue\n" +
                     "• Inconsistency between estimators\n" +
                     "• Time-consuming manual calculations\n" +
                     "• Decision fatigue and burnout\n" +
                     "• Lengthy training for new writers\n\n" +

                     "**💡 THE MCSTUD PHILOSOPHY**\n" +
                     "_\"The writer should determine what needs repair or replacement - the tool provides the corresponding operations.\"_\n\n" +

                     "**SET IT AND FORGET IT**\n" +
                     "McStud uses pre-determined operations so estimators focus on DECISIONS, not calculations:\n" +
                     "• You decide: Repair or Replace?\n" +
                     "• McStud provides: Complete operation list with labor times\n" +
                     "• Result: Consistent, accurate estimates every time\n\n" +

                     "**📋 THE SYSTEMATIC METHOD**\n" +
                     "1. **Categorized Operations** - Organized tabs for Body, Refinish, Mechanical, ADAS\n" +
                     "2. **Pre-Built Formulas** - Standardized calculations eliminate manual errors\n" +
                     "3. **Checklist-Based Workflow** - Nothing gets missed\n" +
                     "4. **Rapid Onboarding** - New writers reach proficiency in months, not years\n\n" +

                     "**🏆 PROVEN RESULTS**\n" +
                     "• Used by the 2024 Best Repair Planner/Estimator of the Year\n" +
                     "• Winning estimate: $20,895 | 21 pages | 366 lines | Most complete in competition\n" +
                     "• Shop metrics: $8,000 average RO, 5-day cycle time\n\n" +

                     "**🔑 KEY PRINCIPLES**\n" +
                     "• **Accuracy** - Every operation accounted for\n" +
                     "• **Consistency** - Same quality regardless of who writes\n" +
                     "• **Speed** - Copy/paste beats manual entry\n" +
                     "• **Education** - Learn while you estimate\n" +
                     "• **Adaptability** - Customizable to your shop's needs\n\n" +

                     "_\"All I'm doing is just copying and pasting, and it saves me time. It's very accurate; it's very efficient.\"_\n" +
                     "— Anthony McNee, Creator of McStud",
            Confidence = 1.0,
            Category = "methodology"
        };
    }

    private ChatResponse GetFounderResponse()
    {
        return new ChatResponse
        {
            Message = "**ANTHONY MCNEE - CREATOR OF MCSTUD**\n" +
                     "════════════════════════════════════════\n\n" +

                     "🏆 **2024 Best Repair Planner/Estimator of the Year**\n" +
                     "_(Inaugural award by FenderBender/ABRN - selected from 6 finalists nationwide)_\n\n" +

                     "🏆 **2020 March Taylor Award** - Verifacts Vehicle Damage Repair Planner\n\n" +

                     "**Position:** Blueprinter at Ultimate Collision and Repair, Edison, NJ\n" +
                     "**Experience:** Over a decade in collision repair\n\n" +

                     "**Credentials:**\n" +
                     "• I-CAR Platinum Estimator\n" +
                     "• Verifacts Outstanding Craftsmanship Award (Vehicle Damage Assessor)\n" +
                     "• Rivian-certified\n" +
                     "• Regular OEM training attendance\n\n" +

                     "**The Award-Winning Estimate:**\n" +
                     "Anthony won the national competition with a 2022 Toyota Camry XSE:\n" +
                     "• **$20,895** total repair cost\n" +
                     "• **21 pages, 366 lines** - most thorough submission\n" +
                     "• Judged by Collision Advice as most complete and accurate\n\n" +

                     "**Shop Performance:**\n" +
                     "• $8,000 average repair order\n" +
                     "• 5-day cycle time\n" +
                     "• Uses Goliath ADAS Cart with dual monitors for OEM procedures\n\n" +

                     "**Family Legacy:**\n" +
                     "Son of Jerry McNee, owner of Ultimate Collision and AASP/NJ Hall of Fame inductee (2024)\n\n" +

                     "**Why He Created McStud:**\n" +
                     "_\"Managing the number of required repair procedures is today's biggest estimating challenge.\"_\n\n" +
                     "Anthony built McStud to address gaps in existing workflows - eliminating human error, enhancing consistency, and accelerating the estimate-writing process.\n\n" +

                     "📰 **Featured In:**\n" +
                     "• FenderBender Magazine\n" +
                     "• New Jersey Automotive (Greco Publishing)\n" +
                     "• mcstudestimating.com",
            Confidence = 1.0,
            Category = "about"
        };
    }

    private ChatResponse GetRefrigerantResponse(string input)
    {
        var sb = new System.Text.StringBuilder();

        // Check if asking about specific type
        if (input.Contains("r-134") || input.Contains("r134"))
        {
            sb.AppendLine("**R-134a Refrigerant**\n");
            sb.AppendLine("• **Type:** HFC (Hydrofluorocarbon)");
            sb.AppendLine("• **Used in:** Vehicles pre-2013, some 2013-2020");
            sb.AppendLine("• **GWP:** 1,430 (Global Warming Potential)");
            sb.AppendLine("• **Container:** Light blue");
            sb.AppendLine("• **Oil type:** PAG oil (Polyalkylene Glycol)");
            sb.AppendLine("• **Status:** Being phased out - EPA banned in new vehicles as of Jan 2021\n");
            sb.AppendLine("**Note:** Different fittings than R-1234yf to prevent mixing. Replacing with R-1234yf is illegal under Clean Air Act Section 203.");
        }
        else if (input.Contains("r-1234") || input.Contains("r1234"))
        {
            sb.AppendLine("**R-1234yf Refrigerant**\n");
            sb.AppendLine("• **Type:** HFO (Hydrofluoroolefin)");
            sb.AppendLine("• **Used in:** Required in all new vehicles since Jan 2021");
            sb.AppendLine("• **GWP:** <1 (breaks down in atmosphere within weeks)");
            sb.AppendLine("• **Container:** White/gray");
            sb.AppendLine("• **Oil type:** PAG oil (1234yf-specific)");
            sb.AppendLine("• **Cost:** Significantly more expensive than R-134a");
            sb.AppendLine("• **Flammability:** Mildly flammable (flame speed <10 cm/sec)\n");
            sb.AppendLine("**Important:** Requires dedicated R-1234yf recovery machine. Unique fittings prevent cross-contamination. 95-98% efficiency of R-134a systems.");
        }
        else
        {
            // General refrigerant info
            sb.AppendLine("**A/C Refrigerant Types (Automotive)**\n");
            sb.AppendLine("**R-134a**");
            sb.AppendLine("• HFC refrigerant, light blue containers");
            sb.AppendLine("• Pre-2013 vehicles, some 2013-2020");
            sb.AppendLine("• Uses PAG oil");
            sb.AppendLine("• High GWP (1,430) - phased out Jan 2021\n");
            sb.AppendLine("**R-1234yf**");
            sb.AppendLine("• HFO refrigerant, white/gray containers");
            sb.AppendLine("• Required in all new vehicles since 2021");
            sb.AppendLine("• Environmentally friendly (GWP <1)");
            sb.AppendLine("• More expensive ($$$)");
            sb.AppendLine("• Mildly flammable - requires certified equipment\n");
            sb.AppendLine("**R-12 \"Freon\" (Pre-1994)**");
            sb.AppendLine("• CFC refrigerant (banned 1994)");
            sb.AppendLine("• Must be retrofitted to R-134a\n");
            sb.AppendLine("**Key Points for Estimating:**");
            sb.AppendLine("• Check underhood label for refrigerant type");
            sb.AppendLine("• R-1234yf service costs more (machine + refrigerant)");
            sb.AppendLine("• NEVER mix types - different fittings prevent this");
            sb.AppendLine("• Evacuate & recharge required when A/C system opened");
            sb.AppendLine("• Illegal to substitute R-134a for R-1234yf (Clean Air Act)");
        }

        return new ChatResponse
        {
            Message = sb.ToString(),
            Confidence = 0.95,
            Category = "mechanical",
            RelatedTopics = new List<string> { "coolant", "A/C operations", "mechanical" }
        };
    }

    private ChatResponse GetCoolantResponse()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("**Engine Coolant / Antifreeze**\n");
        sb.AppendLine("Coolant circulates through the engine and radiator to regulate temperature. Prevents freezing in winter and boiling in summer.\n");

        sb.AppendLine("**Coolant Technologies:**\n");
        sb.AppendLine("**IAT (Inorganic Acid Technology) - Green**");
        sb.AppendLine("• Traditional formula with silicates/phosphates");
        sb.AppendLine("• Service: 2 years / 30,000 miles");
        sb.AppendLine("• Vehicles: Pre-2000 domestics\n");

        sb.AppendLine("**OAT (Organic Acid Technology) - Orange**");
        sb.AppendLine("• Long-life formula (GM Dex-Cool)");
        sb.AppendLine("• Service: 5 years / 150,000 miles");
        sb.AppendLine("• Vehicles: GM 1996+, some VW\n");

        sb.AppendLine("**HOAT (Hybrid OAT) - Yellow/Turquoise/Purple**");
        sb.AppendLine("• Silicate + OAT combination");
        sb.AppendLine("• Service: 5 years / 150,000 miles");
        sb.AppendLine("• Vehicles: Ford, Chrysler, European (BMW, Mercedes, VW/Audi)\n");

        sb.AppendLine("**P-OAT (Phosphate OAT) - Pink/Blue/Green**");
        sb.AppendLine("• Phosphate-based, silicate-free (protects water pump)");
        sb.AppendLine("• Toyota: Pink/Red");
        sb.AppendLine("• Honda: Blue");
        sb.AppendLine("• Nissan: Green or Blue long-life\n");

        sb.AppendLine("**⚠️ Important:**");
        sb.AppendLine("• Color alone does NOT determine type - check owner's manual");
        sb.AppendLine("• NEVER mix coolant types - causes sludge/gelling");
        sb.AppendLine("• Use OEM-approved coolant or \"Asian Vehicle\" for Japanese cars");
        sb.AppendLine("• European cars need silicate-containing HOAT\n");

        sb.AppendLine("**Key Points for Estimating:**");
        sb.AppendLine("• Include coolant cost in radiator/hose/water pump repairs");
        sb.AppendLine("• Flush if contaminated or mixed");
        sb.AppendLine("• Check capacity in service manual");
        sb.AppendLine("• Pressure test cooling system after repairs");

        return new ChatResponse
        {
            Message = sb.ToString(),
            Confidence = 0.95,
            Category = "mechanical",
            RelatedTopics = new List<string> { "refrigerant", "cooling system", "radiator" }
        };
    }

    private ChatResponse GetADASResponse(string input)
    {
        var sb = new System.Text.StringBuilder();

        // Check for specific ADAS topics
        if (input.Contains("static") && input.Contains("calibration"))
        {
            sb.AppendLine("**Static Calibration**\n");
            sb.AppendLine("Performed in-shop using targets/fixtures positioned at specific distances.\n");
            sb.AppendLine("**Requirements:**");
            sb.AppendLine("• Level floor (within spec)");
            sb.AppendLine("• Proper lighting (no direct sunlight)");
            sb.AppendLine("• OEM or OEM-equivalent targets");
            sb.AppendLine("• Scan tool with calibration capability");
            sb.AppendLine("• Vehicle centered on targets\n");
            sb.AppendLine("**Typical Time:** 30-60 minutes");
            sb.AppendLine("**Used For:** Forward camera, surround view cameras, some radar systems");
        }
        else if (input.Contains("dynamic") && input.Contains("calibration"))
        {
            sb.AppendLine("**Dynamic Calibration**\n");
            sb.AppendLine("Performed via road test at specific speeds with visible lane markings.\n");
            sb.AppendLine("**Requirements:**");
            sb.AppendLine("• Straight road with clear lane markings");
            sb.AppendLine("• Specific speed range (usually 25-60 mph)");
            sb.AppendLine("• Good weather (no rain/fog)");
            sb.AppendLine("• Scan tool connected during drive\n");
            sb.AppendLine("**Typical Time:** 15-30 minutes");
            sb.AppendLine("**Used For:** Blind spot monitors, rear radar, some forward systems");
        }
        else if (input.Contains("blind spot") || input.Contains("bsm") || input.Contains("rear radar"))
        {
            sb.AppendLine("**Blind Spot Monitoring (BSM)**\n");
            sb.AppendLine("Radar sensors in rear bumper corners detect vehicles in adjacent lanes.\n");
            sb.AppendLine("**Also Called:** Blind Spot Warning, BSW, Rear Cross Traffic Alert (RCTA)\n");
            sb.AppendLine("**Location:** Rear bumper corners\n");
            sb.AppendLine("**Calibration Type:** Dynamic (road test)\n");
            sb.AppendLine("**Calibration Triggers:**");
            sb.AppendLine("• Rear bumper replace");
            sb.AppendLine("• Quarter panel repair/replace");
            sb.AppendLine("• Rear body structural work");
            sb.AppendLine("• Wheel alignment");
        }
        else if (input.Contains("forward") || input.Contains("front camera") || input.Contains("windshield"))
        {
            sb.AppendLine("**Forward Camera System**\n");
            sb.AppendLine("Camera mounted to windshield, usually behind rearview mirror.\n");
            sb.AppendLine("**Features:** Lane Departure Warning, Lane Keep Assist, Traffic Sign Recognition, Forward Collision Warning\n");
            sb.AppendLine("**Calibration Type:** Static (targets)\n");
            sb.AppendLine("**Calibration Triggers:**");
            sb.AppendLine("• Windshield replace (ALWAYS)");
            sb.AppendLine("• Camera R&I");
            sb.AppendLine("• Wheel alignment");
            sb.AppendLine("• Structural repairs\n");
            sb.AppendLine("**⚠️ ALL manufacturers require recalibration after windshield work**");
        }
        else if (input.Contains("radar") || input.Contains("adaptive cruise") || input.Contains("acc"))
        {
            sb.AppendLine("**Front Radar System**\n");
            sb.AppendLine("Radar module mounted behind front bumper or grille.\n");
            sb.AppendLine("**Features:** Adaptive Cruise Control (ACC), Forward Collision Warning, Automatic Emergency Braking (AEB)\n");
            sb.AppendLine("**Calibration Type:** Static or Dynamic (varies by OEM)\n");
            sb.AppendLine("**Calibration Triggers:**");
            sb.AppendLine("• Front bumper replace");
            sb.AppendLine("• Grille replace");
            sb.AppendLine("• Radar module R&I");
            sb.AppendLine("• Wheel alignment");
            sb.AppendLine("• Structural repairs\n");
            sb.AppendLine("**Paint Note:** No metallic/metal flake paint in radar zone. Max 6 mils film thickness.");
        }
        else
        {
            // General ADAS overview
            sb.AppendLine("**ADAS - Advanced Driver Assistance Systems**\n");
            sb.AppendLine("Safety systems using cameras, radar, and sensors to assist the driver.\n");

            sb.AppendLine("**Common ADAS Features:**");
            sb.AppendLine("• **Forward Collision Warning (FCW)** - Alerts to front obstacles");
            sb.AppendLine("• **Automatic Emergency Braking (AEB)** - Brakes if driver doesn't");
            sb.AppendLine("• **Adaptive Cruise Control (ACC)** - Maintains distance to car ahead");
            sb.AppendLine("• **Lane Departure Warning (LDW)** - Alerts when leaving lane");
            sb.AppendLine("• **Lane Keep Assist (LKA)** - Steers back into lane");
            sb.AppendLine("• **Blind Spot Monitoring (BSM)** - Detects vehicles in blind spots");
            sb.AppendLine("• **Rear Cross Traffic Alert (RCTA)** - Warns when backing up");
            sb.AppendLine("• **Surround View/360 Camera** - Bird's eye view for parking\n");

            sb.AppendLine("**Sensor Types:**");
            sb.AppendLine("• **Front Camera** - Windshield mounted (static calibration)");
            sb.AppendLine("• **Front Radar** - Behind bumper/grille (static or dynamic)");
            sb.AppendLine("• **Rear Radar** - Bumper corners (dynamic calibration)");
            sb.AppendLine("• **Ultrasonic** - Parking sensors (learn procedure)");
            sb.AppendLine("• **LiDAR** - Some luxury/EVs (static calibration)\n");

            sb.AppendLine("**Key Estimating Points:**");
            sb.AppendLine("• Calibration is NOT INCLUDED - bill separately");
            sb.AppendLine("• Always check if vehicle has ADAS before writing");
            sb.AppendLine("• Windshield = camera calibration ALWAYS");
            sb.AppendLine("• Bumpers = check for radar/sensors");
            sb.AppendLine("• Alignment triggers most ADAS recalibration");
            sb.AppendLine("• Document calibration on estimate");
        }

        return new ChatResponse
        {
            Message = sb.ToString(),
            Confidence = 0.95,
            Category = "adas",
            RelatedTopics = new List<string> { "calibration", "scanning", "OEM position statements" }
        };
    }

    private (KnowledgeEntry? entry, double score) FindBestMatch(string input, string topic = "")
    {
        if (_knowledgeBase?.Entries == null)
            return (null, 0);

        var inputWords = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var topicWords = string.IsNullOrEmpty(topic)
            ? inputWords
            : topic.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        KnowledgeEntry? bestEntry = null;
        double bestScore = 0;

        foreach (var entry in _knowledgeBase.Entries)
        {
            double score = CalculateMatchScore(input, inputWords, topicWords, entry);
            if (score > bestScore)
            {
                bestScore = score;
                bestEntry = entry;
            }
        }

        return (bestEntry, bestScore);
    }

    // Common words to ignore or reduce weight for
    private static readonly HashSet<string> _stopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "where", "when", "how", "why", "which", "who",
        "is", "are", "was", "were", "be", "been", "being",
        "the", "a", "an", "and", "or", "but", "if", "then",
        "do", "does", "did", "can", "could", "should", "would",
        "for", "to", "from", "with", "about", "into", "through",
        "i", "me", "my", "you", "your", "we", "our", "they", "their",
        "this", "that", "these", "those", "it", "its",
        "go", "find", "get", "use", "need", "want", "like",
        "replace", "replacing", "repair", "repairing" // These are too generic
    };

    private double CalculateMatchScore(string input, string[] inputWords, string[] topicWords, KnowledgeEntry entry)
    {
        double score = 0;

        // Filter out stop words from input for better matching
        var significantWords = inputWords
            .Where(w => w.Length >= 3 && !_stopWords.Contains(w))
            .ToArray();

        // Topic words get HIGHEST priority - these are the core subject
        foreach (var word in topicWords)
        {
            if (word.Length < 2) continue;

            foreach (var keyword in entry.Keywords)
            {
                var keywordLower = keyword.ToLowerInvariant();

                // Exact topic match - very high weight
                if (keywordLower == word)
                {
                    score += 5.0;
                }
                else if (keywordLower.Split(' ').Contains(word))
                {
                    score += 4.0;
                }
                else if (keywordLower.Contains(word) && word.Length >= 4)
                {
                    score += 2.0;
                }
            }

            // Check answer text for topic - helps find relevant content
            if (entry.Answer.ToLowerInvariant().Contains(word) && word.Length >= 4)
            {
                score += 1.0;
            }
        }

        // Check exact phrase match in keywords
        foreach (var keyword in entry.Keywords)
        {
            var keywordLower = keyword.ToLowerInvariant();
            if (input.Contains(keywordLower))
            {
                int wordCount = keyword.Split(' ').Length;
                score += 3.0 * wordCount;
            }
        }

        // Check if significant words match keywords exactly
        foreach (var word in significantWords)
        {
            foreach (var keyword in entry.Keywords)
            {
                var keywordLower = keyword.ToLowerInvariant();

                if (keywordLower == word || keywordLower.Split(' ').Contains(word))
                {
                    score += 2.0;
                }
                else if (keywordLower.Contains(word))
                {
                    score += 0.5;
                }
            }

            if (entry.Question.ToLowerInvariant().Contains(word))
            {
                score += 0.3;
            }
        }

        return score;
    }

    private bool FuzzyMatch(string input, string target)
    {
        // Simple Levenshtein-like check for similar words
        if (input.Length < 3 || target.Length < 3) return false;

        // Check if one contains most of the other
        int commonChars = input.Count(c => target.Contains(c));
        double similarity = (double)commonChars / Math.Max(input.Length, target.Length);

        return similarity > 0.7;
    }

    private List<string> GetRelatedTopics(KnowledgeEntry entry)
    {
        if (_knowledgeBase?.Entries == null)
            return new List<string>();

        // Find entries in same category or with overlapping keywords
        return _knowledgeBase.Entries
            .Where(e => e.Id != entry.Id &&
                       (e.Category == entry.Category ||
                        e.Keywords.Any(k => entry.Keywords.Contains(k))))
            .Take(3)
            .Select(e => e.Question)
            .ToList();
    }
}

public class ChatResponse
{
    public string Message { get; set; } = "";
    public double Confidence { get; set; }
    public string? Category { get; set; }
    public List<string>? RelatedTopics { get; set; }
}

#region Knowledge Base Models

public class KnowledgeBase
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("lastUpdated")]
    public string LastUpdated { get; set; } = "";

    [JsonPropertyName("categories")]
    public Dictionary<string, CategoryInfo>? Categories { get; set; }

    [JsonPropertyName("entries")]
    public List<KnowledgeEntry>? Entries { get; set; }

    [JsonPropertyName("quickReplies")]
    public List<string> QuickReplies { get; set; } = new();
}

public class CategoryInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public class KnowledgeEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = new();

    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("answer")]
    public string Answer { get; set; } = "";
}

#endregion

#region Definitions Data Models

public class DefinitionsData
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("definitions")]
    public List<DefinitionItem>? Definitions { get; set; }
}

public class DefinitionItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("term")]
    public string? Term { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("pPageLocation")]
    public string? PPageLocation { get; set; }

    [JsonPropertyName("pPageRef")]
    public string? PPageRef { get; set; }

    [JsonPropertyName("pPageSystem")]
    public string? PPageSystem { get; set; }

    [JsonPropertyName("definition")]
    public string? Definition { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("degInquiry")]
    public string? DegInquiry { get; set; }

    [JsonPropertyName("degResponse")]
    public string? DegResponse { get; set; }

    [JsonPropertyName("links")]
    public Dictionary<string, string>? Links { get; set; }
}

#endregion

#region OEM Position Statements Data Models

public class OEMStatementsData
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("statements")]
    public List<OEMPositionStatementItem>? Statements { get; set; }
}

public class OEMPositionStatementItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("oem")]
    public string? Oem { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("dateIssued")]
    public string? DateIssued { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("fullText")]
    public string? FullText { get; set; }

    [JsonPropertyName("keyPoints")]
    public List<string>? KeyPoints { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("pdfLink")]
    public string? PdfLink { get; set; }

    [JsonPropertyName("icarLink")]
    public string? IcarLink { get; set; }

    [JsonPropertyName("scrsLink")]
    public string? ScrsLink { get; set; }
}

#endregion

#region SCRS Operations Data Models

public class OperationsData
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("laborTypes")]
    public Dictionary<string, LaborTypeInfo>? LaborTypes { get; set; }

    [JsonPropertyName("categories")]
    public List<OperationCategory>? Categories { get; set; }

    [JsonPropertyName("operations")]
    public List<OperationItem>? Operations { get; set; }

    [JsonPropertyName("suggestionGroups")]
    public Dictionary<string, SuggestionGroup>? SuggestionGroups { get; set; }
}

public class LaborTypeInfo
{
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class OperationCategory
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}

public class OperationItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("laborType")]
    public string? LaborType { get; set; }

    [JsonPropertyName("suggestWhen")]
    public List<string>? SuggestWhen { get; set; }

    [JsonPropertyName("suggestWith")]
    public List<string>? SuggestWith { get; set; }

    [JsonPropertyName("materials")]
    public bool? Materials { get; set; }

    [JsonPropertyName("pPageRef")]
    public string? PPageRef { get; set; }

    [JsonPropertyName("relatedDefinition")]
    public string? RelatedDefinition { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class SuggestionGroup
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("operations")]
    public List<string>? Operations { get; set; }
}

// Included/Not Included Data Classes
public class IncludedNotIncludedData
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("terminology")]
    public TerminologyData? Terminology { get; set; }

    [JsonPropertyName("calculations")]
    public CalculationsData? Calculations { get; set; }

    [JsonPropertyName("orderOfOperations")]
    public OrderOfOperationsData? OrderOfOperations { get; set; }

    [JsonPropertyName("operations")]
    public List<IncludedNotIncludedOperation>? Operations { get; set; }

    [JsonPropertyName("commonNotIncludedByCategory")]
    public Dictionary<string, List<string>>? CommonNotIncludedByCategory { get; set; }
}

public class TerminologyData
{
    [JsonPropertyName("operationTypes")]
    public Dictionary<string, string>? OperationTypes { get; set; }

    [JsonPropertyName("sideDesignations")]
    public Dictionary<string, string>? SideDesignations { get; set; }

    [JsonPropertyName("laborTypes")]
    public Dictionary<string, string>? LaborTypes { get; set; }

    [JsonPropertyName("commonAbbreviations")]
    public Dictionary<string, string>? CommonAbbreviations { get; set; }
}

public class CalculationsData
{
    [JsonPropertyName("blendFormulas")]
    public BlendFormulasData? BlendFormulas { get; set; }

    [JsonPropertyName("overlapDeductions")]
    public OverlapDeductionsData? OverlapDeductions { get; set; }

    [JsonPropertyName("maskingFormulas")]
    public MaskingFormulasData? MaskingFormulas { get; set; }

    [JsonPropertyName("commonLaborTimes")]
    public CommonLaborTimesData? CommonLaborTimes { get; set; }

    [JsonPropertyName("triCoatAdditive")]
    public TriCoatData? TriCoatAdditive { get; set; }

    [JsonPropertyName("manualEntryOperations")]
    public ManualEntryOperationsData? ManualEntryOperations { get; set; }
}

public class ManualEntryOperationsData
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("refinishOperations")]
    public List<ManualEntryOperation>? RefinishOperations { get; set; }

    [JsonPropertyName("corrosionOperations")]
    public List<ManualEntryOperation>? CorrosionOperations { get; set; }

    [JsonPropertyName("additionalOperations")]
    public List<ManualEntryOperation>? AdditionalOperations { get; set; }
}

public class ManualEntryOperation
{
    [JsonPropertyName("operation")]
    public string? Operation { get; set; }

    [JsonPropertyName("typicalHours")]
    public string? TypicalHours { get; set; }

    [JsonPropertyName("laborType")]
    public string? LaborType { get; set; }

    [JsonPropertyName("when")]
    public string? When { get; set; }
}

public class BlendFormulasData
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("largePanels")]
    public BlendPanelGroup? LargePanels { get; set; }

    [JsonPropertyName("standardPanels")]
    public BlendPanelGroup? StandardPanels { get; set; }

    [JsonPropertyName("bumperCover")]
    public BlendPanelGroup? BumperCover { get; set; }
}

public class BlendPanelGroup
{
    [JsonPropertyName("panels")]
    public List<string>? Panels { get; set; }

    [JsonPropertyName("firstPanel")]
    public string? FirstPanel { get; set; }

    [JsonPropertyName("additionalPanels")]
    public string? AdditionalPanels { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class OverlapDeductionsData
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("majorAdjacentPanel")]
    public OverlapDeduction? MajorAdjacentPanel { get; set; }
}

public class OverlapDeduction
{
    [JsonPropertyName("deduction")]
    public decimal Deduction { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class MaskingFormulasData
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("openings")]
    public List<MaskingOperation>? Openings { get; set; }
}

public class MaskingOperation
{
    [JsonPropertyName("area")]
    public string? Area { get; set; }

    [JsonPropertyName("hours")]
    public decimal Hours { get; set; }

    [JsonPropertyName("typicalCost")]
    public string? TypicalCost { get; set; }
}

public class CommonLaborTimesData
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("bodyOperations")]
    public List<LaborTimeEntry>? BodyOperations { get; set; }

    [JsonPropertyName("refinishOperations")]
    public List<LaborTimeEntry>? RefinishOperations { get; set; }
}

public class LaborTimeEntry
{
    [JsonPropertyName("operation")]
    public string? Operation { get; set; }

    [JsonPropertyName("hours")]
    public decimal Hours { get; set; }

    [JsonPropertyName("bodyHours")]
    public decimal BodyHours { get; set; }

    [JsonPropertyName("refinishHours")]
    public decimal RefinishHours { get; set; }

    [JsonPropertyName("laborType")]
    public string? LaborType { get; set; }

    [JsonPropertyName("typicalCost")]
    public string? TypicalCost { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class TriCoatData
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("formula")]
    public string? Formula { get; set; }

    [JsonPropertyName("example")]
    public string? Example { get; set; }
}

public class OrderOfOperationsData
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("phases")]
    public List<OperationPhase>? Phases { get; set; }
}

public class OperationPhase
{
    [JsonPropertyName("phase")]
    public int Phase { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("operations")]
    public List<string>? Operations { get; set; }
}

public class IncludedNotIncludedOperation
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("partName")]
    public string? PartName { get; set; }

    [JsonPropertyName("operationType")]
    public string? OperationType { get; set; }

    [JsonPropertyName("attachmentType")]
    public string? AttachmentType { get; set; }

    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    [JsonPropertyName("pPageRef")]
    public string? PPageRef { get; set; }

    [JsonPropertyName("sourceRefs")]
    public SourceReferences? SourceRefs { get; set; }

    [JsonPropertyName("included")]
    public List<string>? Included { get; set; }

    [JsonPropertyName("notIncluded")]
    public List<string>? NotIncluded { get; set; }

    [JsonPropertyName("triggers")]
    public List<string>? Triggers { get; set; }

    [JsonPropertyName("metOperations")]
    public List<MetOperationSuggestion>? MetOperations { get; set; }

    [JsonPropertyName("scrsNotes")]
    public string? ScrsNotes { get; set; }
}

public class SourceReferences
{
    [JsonPropertyName("cccMotor")]
    public string? CccMotor { get; set; }

    [JsonPropertyName("mitchell")]
    public string? Mitchell { get; set; }

    [JsonPropertyName("degInquiries")]
    public List<string>? DegInquiries { get; set; }
}

public class MetOperationSuggestion
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("hours")]
    public decimal Hours { get; set; }

    [JsonPropertyName("laborType")]
    public string? LaborType { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("metRef")]
    public string? MetRef { get; set; }
}

public class EstimatingNote
{
    [JsonPropertyName("term")]
    public string? Term { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

#endregion
