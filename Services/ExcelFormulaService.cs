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
    /// Excel Formula Service - Complete awareness of all Excel formulas, calculations, and operations.
    /// Loaded from ExcelOperationsDB.json which was extracted from the McStud Estimating Tool Excel.
    ///
    /// This service knows:
    /// - All operation descriptions and their conditional logic
    /// - Labor hours for each operation (and how they change based on inputs)
    /// - Prices/costs for materials and services
    /// - How input selections affect output calculations
    /// - The complete formula logic from the Excel tool
    /// </summary>
    public class ExcelFormulaService
    {
        private static ExcelFormulaService? _instance;
        public static ExcelFormulaService Instance => _instance ??= new ExcelFormulaService();

        private ExcelOperationsDatabase? _database;
        private bool _isLoaded = false;

        // Category code mappings
        public static readonly Dictionary<string, string> CategoryNames = new()
        {
            ["M"] = "Mechanical",
            ["B"] = "Body",
            ["R"] = "Refinish",
            ["F"] = "Frame",
            ["D"] = "Diagnostic"
        };

        // Operation type mappings
        public static readonly Dictionary<string, string> OperationTypeNames = new()
        {
            ["Rpr"] = "Repair",
            ["Rpl"] = "Replace",
            ["Rfn"] = "Refinish",
            ["Add"] = "Additional",
            ["Blnd"] = "Blend"
        };

        public ExcelFormulaService()
        {
            LoadDatabase();
        }

        private void LoadDatabase()
        {
            try
            {
                var basePath = AppContext.BaseDirectory;
                var jsonPath = Path.Combine(basePath, "Data", "ExcelOperationsDB.json");

                if (!File.Exists(jsonPath))
                {
                    // Try relative path for development
                    jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "ExcelOperationsDB.json");
                }

                if (File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    _database = JsonSerializer.Deserialize<ExcelOperationsDatabase>(json);
                    _isLoaded = _database != null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load Excel operations database: {ex.Message}");
            }
        }

        public bool IsLoaded => _isLoaded;

        /// <summary>
        /// Get all operations for a specific sheet
        /// </summary>
        public List<ExcelOperation> GetOperationsForSheet(string sheetName)
        {
            if (_database?.Sheets == null) return new List<ExcelOperation>();
            return _database.Sheets.GetValueOrDefault(sheetName) ?? new List<ExcelOperation>();
        }

        /// <summary>
        /// Get all sheets available
        /// </summary>
        public IEnumerable<string> GetSheetNames()
        {
            return _database?.Sheets?.Keys ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Search for operations by description keyword
        /// </summary>
        public List<ExcelOperation> SearchOperations(string keyword)
        {
            if (_database?.Sheets == null || string.IsNullOrWhiteSpace(keyword))
                return new List<ExcelOperation>();

            var results = new List<ExcelOperation>();
            var searchLower = keyword.ToLowerInvariant();

            foreach (var sheet in _database.Sheets.Values)
            {
                results.AddRange(sheet.Where(op =>
                    op.Description?.ToLowerInvariant().Contains(searchLower) == true));
            }

            return results;
        }

        /// <summary>
        /// Get labor hours for an operation based on input conditions
        /// </summary>
        public decimal GetLaborHours(string sheetName, string description, Dictionary<string, string>? inputValues = null)
        {
            var operation = FindOperation(sheetName, description);
            if (operation == null) return 0;

            // If there are labor conditions and input values provided, evaluate them
            if (operation.LaborConditions != null && inputValues != null)
            {
                foreach (var condition in operation.LaborConditions)
                {
                    if (inputValues.TryGetValue(condition.InputCell, out var value) &&
                        value.Equals(condition.When, StringComparison.OrdinalIgnoreCase))
                    {
                        if (decimal.TryParse(condition.Then, out var hours))
                            return hours;
                    }
                }
            }

            return operation.LaborHours;
        }

        /// <summary>
        /// Get price for an operation based on input conditions
        /// </summary>
        public decimal GetPrice(string sheetName, string description, Dictionary<string, string>? inputValues = null)
        {
            var operation = FindOperation(sheetName, description);
            if (operation == null) return 0;

            // If there are price conditions and input values provided, evaluate them
            if (operation.PriceConditions != null && inputValues != null)
            {
                foreach (var condition in operation.PriceConditions)
                {
                    if (inputValues.TryGetValue(condition.InputCell, out var value) &&
                        value.Equals(condition.When, StringComparison.OrdinalIgnoreCase))
                    {
                        if (decimal.TryParse(condition.Then, out var price))
                            return price;
                    }
                }
            }

            return operation.Price;
        }

        /// <summary>
        /// Get the description text based on input conditions
        /// </summary>
        public string GetDescription(string sheetName, int row, Dictionary<string, string>? inputValues = null)
        {
            var operations = GetOperationsForSheet(sheetName);
            var operation = operations.FirstOrDefault(op => op.Row == row);
            if (operation == null) return "";

            // If there are description conditions and input values provided, evaluate them
            if (operation.DescriptionConditions != null && inputValues != null)
            {
                foreach (var condition in operation.DescriptionConditions)
                {
                    if (inputValues.TryGetValue(condition.InputCell, out var value) &&
                        value.Equals(condition.When, StringComparison.OrdinalIgnoreCase))
                    {
                        return condition.Then;
                    }
                }
            }

            return operation.Description ?? "";
        }

        /// <summary>
        /// Get all available labor hour variations for an operation
        /// </summary>
        public Dictionary<string, decimal> GetLaborVariations(string sheetName, string description)
        {
            var operation = FindOperation(sheetName, description);
            var variations = new Dictionary<string, decimal>();

            if (operation == null) return variations;

            // Default value
            variations["Default"] = operation.LaborHours;

            // Add conditional variations
            if (operation.LaborConditions != null)
            {
                foreach (var condition in operation.LaborConditions)
                {
                    var key = $"When {condition.InputCell}={condition.When}";
                    if (decimal.TryParse(condition.Then, out var hours))
                        variations[key] = hours;
                }
            }

            return variations;
        }

        /// <summary>
        /// Get complete operation details including all formulas and conditions
        /// </summary>
        public OperationDetails? GetOperationDetails(string sheetName, string description)
        {
            var operation = FindOperation(sheetName, description);
            if (operation == null) return null;

            return new OperationDetails
            {
                Sheet = sheetName,
                Row = operation.Row,
                Description = operation.Description ?? "",
                OperationType = operation.OperationType ?? "",
                OperationTypeName = OperationTypeNames.GetValueOrDefault(operation.OperationType ?? "", ""),
                Category = operation.Category ?? "",
                CategoryName = CategoryNames.GetValueOrDefault(operation.Category ?? "", ""),
                LaborHours = operation.LaborHours,
                RefinishHours = operation.RefinishHours,
                Price = operation.Price,
                Quantity = operation.Quantity,
                LaborFormula = operation.LaborFormula,
                PriceFormula = operation.PriceFormula,
                DescriptionFormula = operation.DescriptionFormula,
                InputCell = operation.InputCell,
                InputValue = operation.InputValue,
                LaborConditions = operation.LaborConditions?.Select(c => $"IF {c.InputCell}=\"{c.When}\" THEN {c.Then}").ToList(),
                PriceConditions = operation.PriceConditions?.Select(c => $"IF {c.InputCell}=\"{c.When}\" THEN {c.Then}").ToList()
            };
        }

        /// <summary>
        /// Get operations by category
        /// </summary>
        public List<ExcelOperation> GetOperationsByCategory(string categoryCode)
        {
            if (_database?.Sheets == null) return new List<ExcelOperation>();

            var results = new List<ExcelOperation>();
            foreach (var sheet in _database.Sheets.Values)
            {
                results.AddRange(sheet.Where(op =>
                    op.Category?.Equals(categoryCode, StringComparison.OrdinalIgnoreCase) == true));
            }

            return results;
        }

        /// <summary>
        /// Get all operations that have a specific input cell
        /// </summary>
        public List<ExcelOperation> GetOperationsByInputCell(string inputCell)
        {
            if (_database?.Sheets == null) return new List<ExcelOperation>();

            var results = new List<ExcelOperation>();
            foreach (var sheet in _database.Sheets.Values)
            {
                results.AddRange(sheet.Where(op =>
                    op.InputCell?.Equals(inputCell, StringComparison.OrdinalIgnoreCase) == true ||
                    op.LaborConditions?.Any(c => c.InputCell.Equals(inputCell, StringComparison.OrdinalIgnoreCase)) == true ||
                    op.PriceConditions?.Any(c => c.InputCell.Equals(inputCell, StringComparison.OrdinalIgnoreCase)) == true));
            }

            return results;
        }

        /// <summary>
        /// Calculate total labor for a list of operations
        /// </summary>
        public decimal CalculateTotalLabor(List<(string Sheet, string Description)> operations, Dictionary<string, string>? inputValues = null)
        {
            decimal total = 0;
            foreach (var (sheet, desc) in operations)
            {
                total += GetLaborHours(sheet, desc, inputValues);
            }
            return total;
        }

        /// <summary>
        /// Calculate total price for a list of operations
        /// </summary>
        public decimal CalculateTotalPrice(List<(string Sheet, string Description)> operations, Dictionary<string, string>? inputValues = null)
        {
            decimal total = 0;
            foreach (var (sheet, desc) in operations)
            {
                total += GetPrice(sheet, desc, inputValues);
            }
            return total;
        }

        /// <summary>
        /// Get a summary of all operations counts by sheet and category
        /// </summary>
        public Dictionary<string, OperationSummary> GetSummary()
        {
            var summary = new Dictionary<string, OperationSummary>();

            if (_database?.Sheets == null) return summary;

            foreach (var (sheetName, operations) in _database.Sheets)
            {
                summary[sheetName] = new OperationSummary
                {
                    TotalOperations = operations.Count,
                    TotalLaborHours = operations.Sum(o => o.LaborHours),
                    TotalRefinishHours = operations.Sum(o => o.RefinishHours),
                    TotalPrice = operations.Sum(o => o.Price),
                    ByCategory = operations.GroupBy(o => o.Category ?? "")
                        .ToDictionary(g => g.Key, g => g.Count()),
                    ByOperationType = operations.GroupBy(o => o.OperationType ?? "")
                        .ToDictionary(g => g.Key, g => g.Count())
                };
            }

            return summary;
        }

        private ExcelOperation? FindOperation(string sheetName, string description)
        {
            var operations = GetOperationsForSheet(sheetName);
            return operations.FirstOrDefault(op =>
                op.Description?.Equals(description, StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    #region Data Models

    public class ExcelOperationsDatabase
    {
        [JsonPropertyName("sheets")]
        public Dictionary<string, List<ExcelOperation>>? Sheets { get; set; }

        [JsonPropertyName("operation_types")]
        public Dictionary<string, string>? OperationTypes { get; set; }

        [JsonPropertyName("categories")]
        public Dictionary<string, string>? Categories { get; set; }
    }

    public class ExcelOperation
    {
        [JsonPropertyName("row")]
        public int Row { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("labor_hours")]
        public decimal LaborHours { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("refinish_hours")]
        public decimal RefinishHours { get; set; }

        [JsonPropertyName("operation_type")]
        public string? OperationType { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;

        [JsonPropertyName("description_formula")]
        public string? DescriptionFormula { get; set; }

        [JsonPropertyName("description_conditions")]
        public List<FormulaCondition>? DescriptionConditions { get; set; }

        [JsonPropertyName("labor_formula")]
        public string? LaborFormula { get; set; }

        [JsonPropertyName("labor_conditions")]
        public List<FormulaCondition>? LaborConditions { get; set; }

        [JsonPropertyName("price_formula")]
        public string? PriceFormula { get; set; }

        [JsonPropertyName("price_conditions")]
        public List<FormulaCondition>? PriceConditions { get; set; }

        [JsonPropertyName("input_cell")]
        public string? InputCell { get; set; }

        [JsonPropertyName("input_value")]
        public string? InputValue { get; set; }
    }

    public class FormulaCondition
    {
        [JsonPropertyName("input_cell")]
        public string InputCell { get; set; } = "";

        [JsonPropertyName("when")]
        public string When { get; set; } = "";

        [JsonPropertyName("then")]
        public string Then { get; set; } = "";
    }

    public class OperationDetails
    {
        public string Sheet { get; set; } = "";
        public int Row { get; set; }
        public string Description { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string OperationTypeName { get; set; } = "";
        public string Category { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string? LaborFormula { get; set; }
        public string? PriceFormula { get; set; }
        public string? DescriptionFormula { get; set; }
        public string? InputCell { get; set; }
        public string? InputValue { get; set; }
        public List<string>? LaborConditions { get; set; }
        public List<string>? PriceConditions { get; set; }
    }

    public class OperationSummary
    {
        public int TotalOperations { get; set; }
        public decimal TotalLaborHours { get; set; }
        public decimal TotalRefinishHours { get; set; }
        public decimal TotalPrice { get; set; }
        public Dictionary<string, int> ByCategory { get; set; } = new();
        public Dictionary<string, int> ByOperationType { get; set; } = new();
    }

    #endregion
}
