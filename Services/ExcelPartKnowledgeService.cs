#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Excel Part Knowledge Service - Complete awareness of ALL Excel operations,
    /// part types, dropdowns, and formulas.
    ///
    /// Features:
    /// - Part type recognition (Plastic Blend, Metal Repair, Welded Replace, etc.)
    /// - All dropdown options for each part type
    /// - Operation matching for any user query
    /// - Labor time calculations (Refinish Units, R&I Time, Repair Time)
    /// </summary>
    public class ExcelPartKnowledgeService
    {
        private static ExcelPartKnowledgeService? _instance;
        public static ExcelPartKnowledgeService Instance => _instance ??= new ExcelPartKnowledgeService();

        private ComprehensiveExcelData? _data;
        private bool _isLoaded = false;

        // Part type aliases for natural language matching
        private readonly Dictionary<string, List<string>> _partTypeAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Plastic Part Blend"] = new() { "plastic blend", "bumper blend", "plastic refinish", "blend plastic" },
            ["Plastic Part Repair"] = new() { "plastic repair", "bumper repair", "repair plastic", "plastic fix" },
            ["Plastic Part Replace"] = new() { "plastic replace", "bumper replace", "new bumper", "replace plastic", "plastic r&r" },
            ["Carbon Fiber / SMC / Composite"] = new() { "carbon fiber", "carbon", "smc", "composite", "fiberglass" },
            ["Metal Part Blend"] = new() { "metal blend", "fender blend", "door blend", "blend metal", "blend fender" },
            ["Metal Part Repair"] = new() { "metal repair", "fender repair", "door repair", "dent repair", "repair metal", "pdr" },
            ["Bolted on Metal Part Replace"] = new() { "bolted replace", "fender replace", "door replace", "hood replace", "bolt on", "bolted panel" },
            ["Welded on Exterior Metal Part Replace"] = new() { "welded replace", "quarter panel", "rocker", "pillar", "structural", "weld panel", "welded panel" },
            ["Inner Panel"] = new() { "inner panel", "inner structure", "inner", "rail", "apron" },
            ["Glass"] = new() { "glass", "windshield", "window", "back glass", "door glass", "quarter glass" }
        };

        // Common operation type aliases
        private readonly Dictionary<string, List<string>> _operationAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DE-NIB"] = new() { "de-nib", "denib", "de nib", "nib", "nibs" },
            ["Adhesion Promoter"] = new() { "adhesion promoter", "adhesion", "promoter", "plastic primer" },
            ["Flex Additive"] = new() { "flex additive", "flex", "flexer", "flex agent" },
            ["Corrosion Protection"] = new() { "corrosion", "rust protection", "anti-rust", "corrosion protection" },
            ["Seam Sealer"] = new() { "seam sealer", "sealer", "seam", "body sealer" },
            ["Cavity Wax"] = new() { "cavity wax", "wax injection", "cavity", "wax" },
            ["Color Tint"] = new() { "color tint", "tint", "color match", "tinting" },
            ["Clear Coat"] = new() { "clear coat", "clear", "clearcoat" },
            ["Prime"] = new() { "prime", "primer", "priming" },
            ["Undercoat"] = new() { "undercoat", "undercoating", "under coat" }
        };

        public ExcelPartKnowledgeService()
        {
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                var basePath = AppContext.BaseDirectory;
                var jsonPath = Path.Combine(basePath, "Data", "ComprehensiveExcelData.json");

                if (!File.Exists(jsonPath))
                {
                    jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "ComprehensiveExcelData.json");
                }

                if (File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    _data = JsonSerializer.Deserialize<ComprehensiveExcelData>(json);
                    _isLoaded = _data != null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load comprehensive Excel data: {ex.Message}");
            }
        }

        public bool IsLoaded => _isLoaded;

        #region Part Type Recognition

        /// <summary>
        /// Get all available part types
        /// </summary>
        public List<string> GetAllPartTypes()
        {
            return _data?.PartTypes?.Keys.ToList() ?? new List<string>();
        }

        /// <summary>
        /// Find the best matching part type for a user query
        /// </summary>
        public string? FindPartType(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            var queryLower = query.ToLowerInvariant();

            // Check direct match first
            if (_data?.PartTypes?.ContainsKey(query) == true)
                return query;

            // Check aliases
            foreach (var (partType, aliases) in _partTypeAliases)
            {
                if (aliases.Any(a => queryLower.Contains(a) || a.Contains(queryLower)))
                    return partType;
            }

            // Fuzzy match on part type names
            foreach (var partType in _data?.PartTypes?.Keys ?? Enumerable.Empty<string>())
            {
                if (partType.ToLowerInvariant().Contains(queryLower) ||
                    queryLower.Contains(partType.ToLowerInvariant()))
                    return partType;
            }

            return null;
        }

        /// <summary>
        /// Get all inputs (dropdowns) for a part type
        /// </summary>
        public List<PartInput> GetInputsForPartType(string partType)
        {
            if (_data?.PartTypes?.TryGetValue(partType, out var data) == true)
            {
                return data.Inputs ?? new List<PartInput>();
            }
            return new List<PartInput>();
        }

        /// <summary>
        /// Get all operations for a part type
        /// </summary>
        public List<PartOperation> GetOperationsForPartType(string partType)
        {
            if (_data?.PartTypes?.TryGetValue(partType, out var data) == true)
            {
                return data.Operations?.Where(o =>
                    !string.IsNullOrWhiteSpace(o.Description) &&
                    o.Description != "Refinish" &&
                    o.Description != "Replace" &&
                    o.Description != "Repair" &&
                    !o.Description.StartsWith("0\t")).ToList() ?? new List<PartOperation>();
            }
            return new List<PartOperation>();
        }

        #endregion

        #region Operation Search

        /// <summary>
        /// Search all operations across all sheets
        /// </summary>
        public List<SheetOperation> SearchAllOperations(string query)
        {
            if (_data?.AllOperations == null || string.IsNullOrWhiteSpace(query))
                return new List<SheetOperation>();

            var queryLower = query.ToLowerInvariant();

            return _data.AllOperations
                .Where(op => op.Description?.ToLowerInvariant().Contains(queryLower) == true)
                .ToList();
        }

        /// <summary>
        /// Get operations by category (Body, Refinish, Mechanical, etc.)
        /// </summary>
        public List<SheetOperation> GetOperationsByCategory(string category)
        {
            if (_data?.AllOperations == null) return new List<SheetOperation>();

            return _data.AllOperations
                .Where(op => op.Category?.Equals(category, StringComparison.OrdinalIgnoreCase) == true ||
                            op.Sheet?.Contains(category, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        #endregion

        #region Smart Query Matching

        /// <summary>
        /// Process a natural language query and return relevant operations
        /// Examples:
        /// - "I'm replacing a quarter panel" → Welded Metal Part Replace operations
        /// - "blending a bumper cover" → Plastic Part Blend operations
        /// - "what's needed for a windshield" → Glass operations
        /// </summary>
        public PartQueryResult ProcessQuery(string query)
        {
            var result = new PartQueryResult { Query = query };

            // Detect part type
            var partType = FindPartType(query);
            if (partType != null)
            {
                result.PartType = partType;
                result.PartTypeInputs = GetInputsForPartType(partType);
                result.PartTypeOperations = GetOperationsForPartType(partType);
            }

            // Search for matching operations
            result.MatchingOperations = SearchAllOperations(query);

            // Detect operation types mentioned
            result.DetectedOperationTypes = DetectOperationTypes(query);

            // Determine if user is asking about specific labor types
            result.IsAskingAboutRefinish = ContainsAny(query, new[] { "refinish", "paint", "blend", "clear", "base", "color" });
            result.IsAskingAboutRepair = ContainsAny(query, new[] { "repair", "fix", "straighten", "pull", "dent" });
            result.IsAskingAboutRI = ContainsAny(query, new[] { "r&i", "r & i", "remove", "install", "r and i" });

            return result;
        }

        private List<string> DetectOperationTypes(string query)
        {
            var detected = new List<string>();
            var queryLower = query.ToLowerInvariant();

            foreach (var (opType, aliases) in _operationAliases)
            {
                if (aliases.Any(a => queryLower.Contains(a)))
                    detected.Add(opType);
            }

            return detected;
        }

        private bool ContainsAny(string text, string[] terms)
        {
            var lower = text.ToLowerInvariant();
            return terms.Any(t => lower.Contains(t));
        }

        #endregion

        #region Labor Calculations

        /// <summary>
        /// Build an operation list with specified labor times
        /// Operations are ONLY added when the user has selected them via checkboxes
        /// </summary>
        public OperationBuild BuildOperations(OperationBuildRequest request)
        {
            var build = new OperationBuild
            {
                PartType = request.PartType,
                PartName = request.PartName
            };

            // NOTE: We do NOT add all part type operations automatically
            // Operations are ONLY added when:
            // 1. User selects them via SelectedOptions checkboxes
            // 2. User specifies labor times (Refinish, R&I, Repair)
            // This matches the Excel behavior where operations only appear
            // when the corresponding dropdown/checkbox is selected

            // Add refinish labor if specified
            if (request.RefinishUnits > 0)
            {
                build.Operations.Add(new BuiltOperation
                {
                    Description = $"{request.PartName ?? "Part"} Refinish",
                    LaborHours = 0,
                    RefinishHours = request.RefinishUnits,
                    Category = "Refinish",
                    Source = "User Input"
                });
                build.TotalRefinishHours += request.RefinishUnits;
            }

            // Add R&I labor if specified
            if (request.RITime > 0)
            {
                build.Operations.Add(new BuiltOperation
                {
                    Description = $"R&I {request.PartName ?? "Part"}",
                    LaborHours = request.RITime,
                    RefinishHours = 0,
                    Category = "Body",
                    Source = "User Input"
                });
                build.TotalLaborHours += request.RITime;
            }

            // Add repair labor if specified
            if (request.RepairTime > 0)
            {
                build.Operations.Add(new BuiltOperation
                {
                    Description = $"Repair {request.PartName ?? "Part"}",
                    LaborHours = request.RepairTime,
                    RefinishHours = 0,
                    Category = "Body",
                    Source = "User Input"
                });
                build.TotalLaborHours += request.RepairTime;
            }

            // Calculate totals
            foreach (var op in build.Operations)
            {
                build.TotalLaborHours += op.LaborHours;
                build.TotalRefinishHours += op.RefinishHours;
                build.TotalPrice += op.Price;
            }

            // Add selected additional operations - ONLY these get added
            if (request.SelectedOptions != null)
            {
                foreach (var option in request.SelectedOptions)
                {
                    // First, try to find matching operation in part type operations
                    var partOps = !string.IsNullOrEmpty(request.PartType)
                        ? GetOperationsForPartType(request.PartType)
                        : new List<PartOperation>();

                    var matchingPartOp = partOps.FirstOrDefault(op =>
                        op.Description?.Contains(option, StringComparison.OrdinalIgnoreCase) == true);

                    if (matchingPartOp != null)
                    {
                        // Use the part-specific operation data
                        build.Operations.Add(new BuiltOperation
                        {
                            Description = $"{request.PartName ?? "Part"} {option}",
                            LaborHours = matchingPartOp.Labor,
                            RefinishHours = matchingPartOp.Refinish,
                            Price = matchingPartOp.Price,
                            Category = DetermineCategory(option),
                            Source = "Part Type"
                        });
                        build.TotalLaborHours += matchingPartOp.Labor;
                        build.TotalRefinishHours += matchingPartOp.Refinish;
                        build.TotalPrice += matchingPartOp.Price;
                    }
                    else
                    {
                        // Fall back to general operation search
                        var matchingOp = SearchAllOperations(option).FirstOrDefault();
                        if (matchingOp != null)
                        {
                            build.Operations.Add(new BuiltOperation
                            {
                                Description = $"{request.PartName ?? "Part"} {option}",
                                LaborHours = matchingOp.Labor,
                                RefinishHours = matchingOp.Refinish,
                                Price = matchingOp.Price,
                                Category = matchingOp.Category ?? "Body",
                                Source = "Selected Option"
                            });
                            build.TotalLaborHours += matchingOp.Labor;
                            build.TotalRefinishHours += matchingOp.Refinish;
                            build.TotalPrice += matchingOp.Price;
                        }
                        else
                        {
                            // Create a placeholder operation for the selected option
                            // User selected it, so include it even without specific data
                            var opData = GetOperationDefaults(option);
                            build.Operations.Add(new BuiltOperation
                            {
                                Description = $"{request.PartName ?? "Part"} {option}",
                                LaborHours = opData.labor,
                                RefinishHours = opData.refinish,
                                Price = opData.price,
                                Category = DetermineCategory(option),
                                Source = "Selected"
                            });
                            build.TotalLaborHours += opData.labor;
                            build.TotalRefinishHours += opData.refinish;
                            build.TotalPrice += opData.price;
                        }
                    }
                }
            }

            return build;
        }

        /// <summary>
        /// Get default values for common operation types
        /// </summary>
        private (decimal labor, decimal refinish, decimal price) GetOperationDefaults(string option)
        {
            var lower = option.ToLowerInvariant();

            // Refinish operations
            if (lower.Contains("de-nib") || lower.Contains("denib")) return (0, 0.1m, 0);
            if (lower.Contains("buff") || lower.Contains("sand")) return (0, 0.3m, 0);
            if (lower.Contains("tint")) return (0, 0.5m, 0);
            if (lower.Contains("prime")) return (0, 0.2m, 0);
            if (lower.Contains("blend")) return (0, 0.5m, 0);

            // Material operations
            if (lower.Contains("adhesion promoter")) return (0, 0.3m, 15);
            if (lower.Contains("flex")) return (0, 0.3m, 15);
            if (lower.Contains("sealer")) return (0.2m, 0, 10);
            if (lower.Contains("wax")) return (0.3m, 0, 15);
            if (lower.Contains("corrosion")) return (0.3m, 0, 20);
            if (lower.Contains("undercoat")) return (0.2m, 0, 15);
            if (lower.Contains("ceramic")) return (0, 0, 50);
            if (lower.Contains("wrap") || lower.Contains("ppf")) return (1.0m, 0, 100);

            // Parts operations
            if (lower.Contains("sensor")) return (0.2m, 0, 0);
            if (lower.Contains("emblem")) return (0.1m, 0, 0);
            if (lower.Contains("plate")) return (0.1m, 0, 0);

            // Default
            return (0.2m, 0, 0);
        }

        private string DetermineCategory(string description)
        {
            var lower = description.ToLowerInvariant();
            if (lower.Contains("refinish") || lower.Contains("paint") || lower.Contains("clear") ||
                lower.Contains("blend") || lower.Contains("prime"))
                return "Refinish";
            if (lower.Contains("mechanical") || lower.Contains("a/c") || lower.Contains("scan") ||
                lower.Contains("calibrat"))
                return "Mechanical";
            return "Body";
        }

        #endregion

        #region Get All Inputs/Dropdowns

        /// <summary>
        /// Get all unique input types across all part types
        /// </summary>
        public List<string> GetAllInputTypes()
        {
            var inputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_data?.PartTypes != null)
            {
                foreach (var partType in _data.PartTypes.Values)
                {
                    if (partType.Inputs != null)
                    {
                        foreach (var input in partType.Inputs)
                        {
                            if (!string.IsNullOrWhiteSpace(input.Name))
                                inputs.Add(input.Name);
                        }
                    }
                }
            }

            if (_data?.InputTypes != null)
            {
                foreach (var sheetInputs in _data.InputTypes.Values)
                {
                    foreach (var input in sheetInputs)
                    {
                        if (!string.IsNullOrWhiteSpace(input.Name))
                            inputs.Add(input.Name);
                    }
                }
            }

            return inputs.OrderBy(x => x).ToList();
        }

        #endregion
    }

    #region Data Models

    public class ComprehensiveExcelData
    {
        [JsonPropertyName("part_types")]
        public Dictionary<string, PartTypeData>? PartTypes { get; set; }

        [JsonPropertyName("all_operations")]
        public List<SheetOperation>? AllOperations { get; set; }

        [JsonPropertyName("input_types")]
        public Dictionary<string, List<SheetInput>>? InputTypes { get; set; }
    }

    public class PartTypeData
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("inputs")]
        public List<PartInput>? Inputs { get; set; }

        [JsonPropertyName("operations")]
        public List<PartOperation>? Operations { get; set; }
    }

    public class PartInput
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("cell")]
        public string? Cell { get; set; }
    }

    public class PartOperation
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("row")]
        public int Row { get; set; }

        [JsonPropertyName("labor")]
        public decimal Labor { get; set; }

        [JsonPropertyName("refinish")]
        public decimal Refinish { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("has_formula")]
        public bool HasFormula { get; set; }
    }

    public class SheetOperation
    {
        [JsonPropertyName("sheet")]
        public string? Sheet { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("row")]
        public int Row { get; set; }

        [JsonPropertyName("labor")]
        public decimal Labor { get; set; }

        [JsonPropertyName("refinish")]
        public decimal Refinish { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }

    public class SheetInput
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("row")]
        public int Row { get; set; }
    }

    public class PartQueryResult
    {
        public string Query { get; set; } = "";
        public string? PartType { get; set; }
        public List<PartInput> PartTypeInputs { get; set; } = new();
        public List<PartOperation> PartTypeOperations { get; set; } = new();
        public List<SheetOperation> MatchingOperations { get; set; } = new();
        public List<string> DetectedOperationTypes { get; set; } = new();
        public bool IsAskingAboutRefinish { get; set; }
        public bool IsAskingAboutRepair { get; set; }
        public bool IsAskingAboutRI { get; set; }
    }

    public class OperationBuildRequest
    {
        public string? PartType { get; set; }
        public string? PartName { get; set; }
        public decimal RefinishUnits { get; set; }
        public decimal RITime { get; set; }
        public decimal RepairTime { get; set; }
        public List<string>? SelectedOptions { get; set; }
    }

    public class OperationBuild
    {
        public string? PartType { get; set; }
        public string? PartName { get; set; }
        public List<BuiltOperation> Operations { get; set; } = new();
        public decimal TotalLaborHours { get; set; }
        public decimal TotalRefinishHours { get; set; }
        public decimal TotalPrice { get; set; }
    }

    public class BuiltOperation
    {
        public string Description { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public string Category { get; set; } = "";
        public string Source { get; set; } = "";
    }

    #endregion
}
