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
    /// Service for loading and managing the customizable Build Estimate configuration.
    /// Users can edit the JSON file to add/remove inputs and operation rules.
    /// </summary>
    public class EstimateBuilderConfigService
    {
        private static EstimateBuilderConfigService? _instance;
        public static EstimateBuilderConfigService Instance => _instance ??= new EstimateBuilderConfigService();

        private EstimateBuilderConfig? _config;
        private readonly string _defaultConfigPath;  // Original - never modified
        private readonly string _customConfigPath;   // User's copy - editable
        private DateTime _lastLoadTime;

        public EstimateBuilderConfig Config => _config ?? new EstimateBuilderConfig();

        /// <summary>
        /// True if user is using their custom config, false if using default
        /// </summary>
        public bool IsUsingCustomConfig { get; private set; }

        public EstimateBuilderConfigService()
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            _defaultConfigPath = Path.Combine(basePath, "Data", "EstimateBuilderConfig.json");
            _customConfigPath = Path.Combine(basePath, "Data", "EstimateBuilderConfig_Custom.json");
            LoadConfig();
        }

        public void LoadConfig()
        {
            try
            {
                // Try to load custom config first, fall back to default
                string pathToLoad;
                if (File.Exists(_customConfigPath))
                {
                    pathToLoad = _customConfigPath;
                    IsUsingCustomConfig = true;
                    System.Diagnostics.Debug.WriteLine("[ConfigService] Loading CUSTOM config");
                }
                else if (File.Exists(_defaultConfigPath))
                {
                    pathToLoad = _defaultConfigPath;
                    IsUsingCustomConfig = false;
                    System.Diagnostics.Debug.WriteLine("[ConfigService] Loading DEFAULT config (no custom found)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ConfigService] No config files found, using built-in defaults");
                    _config = CreateDefaultConfig();
                    IsUsingCustomConfig = false;
                    return;
                }

                var json = File.ReadAllText(pathToLoad);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                _config = JsonSerializer.Deserialize<EstimateBuilderConfig>(json, options);
                _lastLoadTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Loaded {_config?.Sheets?.Count ?? 0} sheets, {_config?.OperationRules?.Count ?? 0} rules");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Error loading config: {ex.Message}");
                _config = CreateDefaultConfig();
                IsUsingCustomConfig = false;
            }
        }

        /// <summary>
        /// Create a custom copy from the default config for user editing
        /// </summary>
        public void CreateCustomCopy()
        {
            try
            {
                if (File.Exists(_defaultConfigPath))
                {
                    File.Copy(_defaultConfigPath, _customConfigPath, overwrite: true);
                    IsUsingCustomConfig = true;
                    LoadConfig();
                    System.Diagnostics.Debug.WriteLine("[ConfigService] Created custom config copy");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Error creating custom copy: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete the custom config and revert to default
        /// </summary>
        public void ResetToDefault()
        {
            try
            {
                if (File.Exists(_customConfigPath))
                {
                    File.Delete(_customConfigPath);
                    System.Diagnostics.Debug.WriteLine("[ConfigService] Deleted custom config, reverting to default");
                }
                IsUsingCustomConfig = false;
                LoadConfig();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Error resetting to default: {ex.Message}");
            }
        }

        public void SaveConfig()
        {
            try
            {
                // Always save to custom config - never modify the default
                var dir = Path.GetDirectoryName(_customConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var json = JsonSerializer.Serialize(_config, options);
                File.WriteAllText(_customConfigPath, json);
                IsUsingCustomConfig = true;
                _lastLoadTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine("[ConfigService] Custom config saved successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Error saving config: {ex.Message}");
            }
        }

        /// <summary>
        /// Reload config if file has been modified externally
        /// </summary>
        public void ReloadIfModified()
        {
            try
            {
                // Check custom config first, then default
                var pathToCheck = File.Exists(_customConfigPath) ? _customConfigPath : _defaultConfigPath;
                if (File.Exists(pathToCheck))
                {
                    var lastWrite = File.GetLastWriteTime(pathToCheck);
                    if (lastWrite > _lastLoadTime)
                    {
                        LoadConfig();
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Generate operations based on current input values using the configured rules
        /// </summary>
        public List<GeneratedOperation> GenerateOperations(Dictionary<string, object> inputValues)
        {
            var operations = new List<GeneratedOperation>();
            if (_config?.OperationRules == null) return operations;

            foreach (var rule in _config.OperationRules)
            {
                if (EvaluateTrigger(rule.Trigger, inputValues))
                {
                    foreach (var opDef in rule.Operations)
                    {
                        var op = CreateOperation(opDef, inputValues);
                        if (op != null)
                        {
                            operations.Add(op);
                        }
                    }
                }
            }

            return operations;
        }

        private bool EvaluateTrigger(OperationTrigger? trigger, Dictionary<string, object> inputValues)
        {
            if (trigger == null || string.IsNullOrEmpty(trigger.InputId))
                return false;

            if (!inputValues.TryGetValue(trigger.InputId, out var value))
                return false;

            // Check equals condition
            if (trigger.Equals != null)
            {
                if (trigger.Equals is JsonElement jsonElement)
                {
                    if (jsonElement.ValueKind == JsonValueKind.True)
                        return value is bool b && b;
                    if (jsonElement.ValueKind == JsonValueKind.False)
                        return value is bool b2 && !b2;
                    if (jsonElement.ValueKind == JsonValueKind.String)
                        return string.Equals(value?.ToString(), jsonElement.GetString(), StringComparison.OrdinalIgnoreCase);
                    if (jsonElement.ValueKind == JsonValueKind.Number)
                        return Convert.ToDouble(value) == jsonElement.GetDouble();
                }
                else if (trigger.Equals is bool triggerBool)
                {
                    return value is bool valueBool && valueBool == triggerBool;
                }
                else
                {
                    return string.Equals(value?.ToString(), trigger.Equals?.ToString(), StringComparison.OrdinalIgnoreCase);
                }
            }

            // Check greaterThan condition
            if (trigger.GreaterThan.HasValue)
            {
                var numValue = Convert.ToDouble(value);
                return numValue > trigger.GreaterThan.Value;
            }

            // Check lessThan condition
            if (trigger.LessThan.HasValue)
            {
                var numValue = Convert.ToDouble(value);
                return numValue < trigger.LessThan.Value;
            }

            // Check notEquals condition
            if (trigger.NotEquals != null)
            {
                return !string.Equals(value?.ToString(), trigger.NotEquals?.ToString(), StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private GeneratedOperation? CreateOperation(OperationDefinition def, Dictionary<string, object> inputValues)
        {
            var op = new GeneratedOperation
            {
                OperationType = def.Type ?? "",
                Category = def.Category ?? ""
            };

            // Handle description with template substitution
            if (!string.IsNullOrEmpty(def.DescriptionTemplate))
            {
                op.Description = SubstituteValues(def.DescriptionTemplate, inputValues);
            }
            else
            {
                op.Description = def.Description ?? "";
            }

            // Handle labor hours
            if (!string.IsNullOrEmpty(def.LaborHoursFromInput) && inputValues.TryGetValue(def.LaborHoursFromInput, out var laborValue))
            {
                op.LaborHours = Convert.ToDecimal(laborValue);
                // Apply multiplier if specified
                if (def.LaborHoursMultiplier.HasValue)
                {
                    op.LaborHours *= def.LaborHoursMultiplier.Value;
                }
            }
            else if (def.LaborHours.HasValue)
            {
                op.LaborHours = def.LaborHours.Value;
            }

            // Handle refinish hours
            if (!string.IsNullOrEmpty(def.RefinishHoursFromInput) && inputValues.TryGetValue(def.RefinishHoursFromInput, out var refinishValue))
            {
                op.RefinishHours = Convert.ToDecimal(refinishValue);
                // Apply multiplier if specified
                if (def.RefinishHoursMultiplier.HasValue)
                {
                    op.RefinishHours *= def.RefinishHoursMultiplier.Value;
                }
            }
            else if (def.RefinishHours.HasValue)
            {
                op.RefinishHours = def.RefinishHours.Value;
            }

            // Handle price with formula support
            if (!string.IsNullOrEmpty(def.PriceFormula))
            {
                op.Price = EvaluatePriceFormula(def.PriceFormula, inputValues);
            }
            else if (!string.IsNullOrEmpty(def.PriceFromInput) && inputValues.TryGetValue(def.PriceFromInput, out var priceValue))
            {
                op.Price = Convert.ToDecimal(priceValue);
                // Apply multiplier if specified
                if (def.PriceMultiplier.HasValue)
                {
                    op.Price *= def.PriceMultiplier.Value;
                }
            }
            else if (def.Price.HasValue)
            {
                op.Price = def.Price.Value;
            }

            // Skip operations with no meaningful values
            if (op.LaborHours == 0 && op.RefinishHours == 0 && op.Price == 0 &&
                string.IsNullOrEmpty(op.Description))
            {
                return null;
            }

            return op;
        }

        private string SubstituteValues(string template, Dictionary<string, object> inputValues)
        {
            var result = template;
            foreach (var kvp in inputValues)
            {
                result = result.Replace($"{{{kvp.Key}}}", kvp.Value?.ToString() ?? "");
            }
            return result;
        }

        private decimal EvaluatePriceFormula(string formula, Dictionary<string, object> inputValues)
        {
            try
            {
                // Simple formula evaluation: replace input references and calculate
                var expr = formula;
                foreach (var kvp in inputValues)
                {
                    expr = expr.Replace($"{{{kvp.Key}}}", kvp.Value?.ToString() ?? "0");
                }

                // Very basic evaluation for multiplication
                if (expr.Contains("*"))
                {
                    var parts = expr.Split('*');
                    if (parts.Length == 2)
                    {
                        var a = decimal.Parse(parts[0].Trim());
                        var b = decimal.Parse(parts[1].Trim());
                        return a * b;
                    }
                }

                // Try direct parse
                if (decimal.TryParse(expr, out var directValue))
                    return directValue;

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private EstimateBuilderConfig CreateDefaultConfig()
        {
            return new EstimateBuilderConfig
            {
                Version = "1.0",
                Sheets = new List<SheetConfig>
                {
                    new SheetConfig
                    {
                        Id = "soplist",
                        Name = "SOP List",
                        Icon = "\uE8A5",
                        Inputs = new List<InputConfig>
                        {
                            new InputConfig { Id = "SOPList_BatteryType", Label = "Battery Type", Type = "dropdown", Options = new[] { "None", "Single", "Dual" }, Default = "None" }
                        }
                    }
                },
                OperationRules = new List<OperationRule>()
            };
        }

        #region Add/Edit/Remove Methods

        public void AddSheet(SheetConfig sheet)
        {
            _config ??= new EstimateBuilderConfig();
            _config.Sheets ??= new List<SheetConfig>();
            _config.Sheets.Add(sheet);
        }

        public void AddInputToSheet(string sheetId, InputConfig input)
        {
            var sheet = _config?.Sheets?.FirstOrDefault(s => s.Id == sheetId);
            if (sheet != null)
            {
                sheet.Inputs ??= new List<InputConfig>();
                sheet.Inputs.Add(input);
            }
        }

        public void RemoveInput(string sheetId, string inputId)
        {
            var sheet = _config?.Sheets?.FirstOrDefault(s => s.Id == sheetId);
            if (sheet?.Inputs != null)
            {
                sheet.Inputs = sheet.Inputs.Where(i => i.Id != inputId).ToList();
            }
        }

        public void AddOperationRule(OperationRule rule)
        {
            _config ??= new EstimateBuilderConfig();
            _config.OperationRules ??= new List<OperationRule>();
            _config.OperationRules.Add(rule);
        }

        public void RemoveOperationRule(string ruleId)
        {
            if (_config?.OperationRules != null)
            {
                _config.OperationRules = _config.OperationRules.Where(r => r.Id != ruleId).ToList();
            }
        }

        public void UpdateOperationRule(OperationRule rule)
        {
            if (_config?.OperationRules != null)
            {
                var index = _config.OperationRules.FindIndex(r => r.Id == rule.Id);
                if (index >= 0)
                {
                    _config.OperationRules[index] = rule;
                }
            }
        }

        #endregion
    }

    #region Config Models

    public class EstimateBuilderConfig
    {
        public string Version { get; set; } = "1.0";
        public string? Description { get; set; }
        public List<SheetConfig>? Sheets { get; set; }
        public List<OperationRule>? OperationRules { get; set; }
        public List<OutputCategory>? OutputCategories { get; set; }
    }

    public class SheetConfig
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Icon { get; set; }
        public List<InputConfig>? Inputs { get; set; }
        public List<SubCategoryConfig>? Subcategories { get; set; }
    }

    public class SubCategoryConfig
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<InputConfig>? Inputs { get; set; }
    }

    public class InputConfig
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "text"; // dropdown, checkbox, number, text
        public string[]? Options { get; set; }
        public object? Default { get; set; }
        public string? Placeholder { get; set; }
        public string? Category { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Step { get; set; }
    }

    public class OperationRule
    {
        public string Id { get; set; } = "";
        public OperationTrigger? Trigger { get; set; }
        public List<OperationDefinition> Operations { get; set; } = new();
    }

    public class OperationTrigger
    {
        public string InputId { get; set; } = "";
        public object? Equals { get; set; }
        public object? NotEquals { get; set; }
        public double? GreaterThan { get; set; }
        public double? LessThan { get; set; }
    }

    public class OperationDefinition
    {
        public string? Type { get; set; }
        public string? Description { get; set; }
        public string? DescriptionTemplate { get; set; }
        public string? Category { get; set; }
        public decimal? LaborHours { get; set; }
        public string? LaborHoursFromInput { get; set; }
        public decimal? LaborHoursMultiplier { get; set; }
        public decimal? RefinishHours { get; set; }
        public string? RefinishHoursFromInput { get; set; }
        public decimal? RefinishHoursMultiplier { get; set; }
        public decimal? Price { get; set; }
        public string? PriceFromInput { get; set; }
        public string? PriceFormula { get; set; }
        public decimal? PriceMultiplier { get; set; }
    }

    public class OutputCategory
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Color { get; set; }
    }

    #endregion
}
