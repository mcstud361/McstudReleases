#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Service for parsing uploaded estimates and extracting keywords.
    /// Keywords are loaded from KeywordMappings.json for easy customization.
    /// </summary>
    public class EstimateParserService
    {
        // Loaded from JSON file
        private Dictionary<string, ExcelCategory> _excelCategories = new();
        private Dictionary<string, List<string>> _operationTypes = new();

        // Fallback hardcoded categories (used if JSON fails to load)
        private static readonly Dictionary<string, List<string>> _fallbackKeywords = new()
        {
            ["PLASTIC_PART"] = new() { "fascia", "bumper cover", "valance", "air dam", "spoiler", "fender liner", "grille", "molding" },
            ["STEEL_PART"] = new() { "fender", "hood", "door", "quarter panel", "trunk lid", "rocker", "pillar", "roof" },
            ["HEADLIGHT"] = new() { "headlight", "headlamp", "fog light", "drl" },
            ["TAILLIGHT"] = new() { "taillight", "taillamp", "brake light", "backup lamp" },
            ["BUMPER"] = new() { "bumper", "front bumper", "rear bumper", "bumper reinforcement", "absorber" },
            ["MIRROR"] = new() { "mirror", "door mirror", "side mirror" },
            ["GLASS"] = new() { "windshield", "back glass", "door glass", "quarter glass" },
            ["AC_SYSTEM"] = new() { "a/c", "condenser", "evaporator", "compressor", "refrigerant", "r134a", "r1234yf" },
            ["SUSPENSION"] = new() { "strut", "shock", "control arm", "ball joint", "tie rod" },
            ["AIRBAG"] = new() { "airbag", "air bag", "srs", "restraint" },
            ["SEATBELT"] = new() { "seatbelt", "seat belt", "pretensioner", "retractor" },
            ["REFINISH"] = new() { "refinish", "paint", "blend", "clearcoat", "basecoat" },
            ["FRAME"] = new() { "frame", "structural", "unibody", "pull" }
        };

        // Regex patterns for extracting specific data
        private static readonly Regex _pricePattern = new(@"\$?\s*(\d{1,3}(?:,\d{3})*(?:\.\d{2})?)", RegexOptions.Compiled);
        private static readonly Regex _hoursPattern = new(@"(\d+\.?\d*)\s*(?:hrs?|hours?|labor)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _qtyPattern = new(@"qty[:\s]*(\d+)|(\d+)\s*(?:ea|each|x\s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _vinPattern = new(@"[A-HJ-NPR-Z0-9]{17}", RegexOptions.Compiled);
        private static readonly Regex _vehiclePattern = new(@"(\d{4})\s+(\w+)\s+(\w+)", RegexOptions.Compiled);

        public EstimateParserService()
        {
            LoadKeywordMappings();
        }

        /// <summary>
        /// Load keyword mappings from JSON file
        /// </summary>
        private void LoadKeywordMappings()
        {
            try
            {
                // Look for JSON file in app directory
                var appDir = AppContext.BaseDirectory;
                var jsonPath = Path.Combine(appDir, "Services", "KeywordMappings.json");

                // Also check source directory during development
                if (!File.Exists(jsonPath))
                {
                    var devPath = Path.Combine(Directory.GetCurrentDirectory(), "Services", "KeywordMappings.json");
                    if (File.Exists(devPath))
                        jsonPath = devPath;
                }

                // Check parent directories
                if (!File.Exists(jsonPath))
                {
                    var parentDir = Directory.GetParent(appDir)?.FullName;
                    if (parentDir != null)
                    {
                        jsonPath = Path.Combine(parentDir, "Services", "KeywordMappings.json");
                    }
                }

                if (File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };

                    var mappings = JsonSerializer.Deserialize<KeywordMappingsFile>(json, options);
                    if (mappings?.ExcelCategories != null)
                    {
                        _excelCategories = mappings.ExcelCategories;
                        System.Diagnostics.Debug.WriteLine($"[EstimateParser] Loaded {_excelCategories.Count} categories from JSON");
                    }
                    if (mappings?.OperationTypes != null)
                    {
                        _operationTypes = mappings.OperationTypes;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[EstimateParser] KeywordMappings.json not found, using fallback keywords");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EstimateParser] Error loading JSON: {ex.Message}");
            }

            // If no categories loaded, use fallback
            if (_excelCategories.Count == 0)
            {
                foreach (var kvp in _fallbackKeywords)
                {
                    _excelCategories[kvp.Key] = new ExcelCategory
                    {
                        ExcelSheet = "Part Operations",
                        Description = kvp.Key,
                        Keywords = kvp.Value
                    };
                }
            }
        }

        /// <summary>
        /// Reload keywords from JSON file (call after editing the file)
        /// </summary>
        public void ReloadKeywords()
        {
            _excelCategories.Clear();
            _operationTypes.Clear();
            LoadKeywordMappings();
        }

        /// <summary>
        /// Parse estimate text and extract relevant keywords and data
        /// </summary>
        public EstimateAnalysis ParseEstimate(string estimateText)
        {
            if (string.IsNullOrWhiteSpace(estimateText))
                return new EstimateAnalysis();

            var analysis = new EstimateAnalysis
            {
                RawText = estimateText
            };

            // Normalize text for matching
            var normalizedText = estimateText.ToLowerInvariant();
            var lines = estimateText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            // Extract VIN
            var vinMatch = _vinPattern.Match(estimateText);
            if (vinMatch.Success)
            {
                analysis.VIN = vinMatch.Value;
            }

            // Extract vehicle info
            var vehicleMatch = _vehiclePattern.Match(estimateText);
            if (vehicleMatch.Success)
            {
                analysis.VehicleYear = vehicleMatch.Groups[1].Value;
                analysis.VehicleMake = vehicleMatch.Groups[2].Value;
                analysis.VehicleModel = vehicleMatch.Groups[3].Value;
            }

            // Extract line items
            foreach (var line in lines)
            {
                var lineItem = ParseLineItem(line);
                if (lineItem != null)
                {
                    analysis.LineItems.Add(lineItem);
                }
            }

            // Extract keywords and match to categories
            foreach (var category in _excelCategories)
            {
                foreach (var keyword in category.Value.Keywords)
                {
                    if (normalizedText.Contains(keyword.ToLowerInvariant()))
                    {
                        if (!analysis.MatchedKeywords.ContainsKey(category.Key))
                        {
                            analysis.MatchedKeywords[category.Key] = new List<string>();
                        }
                        if (!analysis.MatchedKeywords[category.Key].Contains(keyword))
                        {
                            analysis.MatchedKeywords[category.Key].Add(keyword);
                        }
                    }
                }
            }

            // Generate suggestions based on matched keywords
            analysis.Suggestions = GenerateSuggestions(analysis);

            return analysis;
        }

        /// <summary>
        /// Parse a single line from the estimate
        /// </summary>
        private EstimateLineItem? ParseLineItem(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 5)
                return null;

            var lineItem = new EstimateLineItem
            {
                RawLine = line
            };

            // Try to extract price
            var priceMatch = _pricePattern.Match(line);
            if (priceMatch.Success)
            {
                var priceStr = priceMatch.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(priceStr, out var price))
                {
                    lineItem.Price = price;
                }
            }

            // Try to extract hours
            var hoursMatch = _hoursPattern.Match(line);
            if (hoursMatch.Success)
            {
                if (decimal.TryParse(hoursMatch.Groups[1].Value, out var hours))
                {
                    lineItem.LaborHours = hours;
                }
            }

            // Try to extract quantity
            var qtyMatch = _qtyPattern.Match(line);
            if (qtyMatch.Success)
            {
                var qtyGroup = qtyMatch.Groups[1].Success ? qtyMatch.Groups[1] : qtyMatch.Groups[2];
                if (int.TryParse(qtyGroup.Value, out var qty))
                {
                    lineItem.Quantity = qty;
                }
            }

            // Determine operation type
            var lowerLine = line.ToLowerInvariant();
            lineItem.OperationType = DetermineOperationType(lowerLine);

            // Find matching category for description
            foreach (var category in _excelCategories)
            {
                foreach (var keyword in category.Value.Keywords)
                {
                    if (lowerLine.Contains(keyword.ToLowerInvariant()))
                    {
                        lineItem.Category = category.Key;
                        lineItem.ExcelSheet = category.Value.ExcelSheet;
                        lineItem.Description = ExtractDescription(line, keyword);
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(lineItem.Category))
                    break;
            }

            // If no category found but line has data, still include it
            if (string.IsNullOrEmpty(lineItem.Category) && (lineItem.Price > 0 || lineItem.LaborHours > 0))
            {
                lineItem.Description = line.Trim();
                return lineItem;
            }

            return !string.IsNullOrEmpty(lineItem.Category) ? lineItem : null;
        }

        /// <summary>
        /// Determine operation type from line text
        /// </summary>
        private string DetermineOperationType(string lowerLine)
        {
            // Check loaded operation types first
            foreach (var opType in _operationTypes)
            {
                foreach (var keyword in opType.Value)
                {
                    if (lowerLine.Contains(keyword.ToLowerInvariant()))
                    {
                        return opType.Key;
                    }
                }
            }

            // Fallback to hardcoded patterns
            if (lowerLine.Contains("replace") || lowerLine.Contains("new") || lowerLine.Contains("r/r"))
                return "Replace";
            if (lowerLine.Contains("r&i") || lowerLine.Contains("r+i") || lowerLine.Contains("r/i") || lowerLine.Contains("remove and install"))
                return "R&I";
            if (lowerLine.Contains("repair") || lowerLine.Contains("rpr"))
                return "Rpr";
            if (lowerLine.Contains("blend"))
                return "Blend";
            if (lowerLine.Contains("refinish") || lowerLine.Contains("paint") || lowerLine.Contains(" ref "))
                return "Refinish";
            if (lowerLine.Contains("o/h") || lowerLine.Contains("overhaul"))
                return "O/H";
            if (lowerLine.Contains("sublet"))
                return "Sublet";

            return "";
        }

        /// <summary>
        /// Extract a clean description from the line
        /// </summary>
        private string ExtractDescription(string line, string matchedKeyword)
        {
            // Remove price and hours patterns
            var desc = _pricePattern.Replace(line, "");
            desc = _hoursPattern.Replace(desc, "");
            desc = _qtyPattern.Replace(desc, "");

            // Clean up extra whitespace and common separators
            desc = Regex.Replace(desc, @"[\t]+", " ");
            desc = Regex.Replace(desc, @"\s{2,}", " ");
            desc = desc.Trim(' ', '-', '|', ',', '.');

            // Limit length
            if (desc.Length > 60)
                desc = desc.Substring(0, 57) + "...";

            return desc;
        }

        /// <summary>
        /// Generate operation suggestions based on matched keywords
        /// </summary>
        private List<OperationSuggestion> GenerateSuggestions(EstimateAnalysis analysis)
        {
            var suggestions = new List<OperationSuggestion>();

            // Map keywords to Excel operations
            foreach (var kvp in analysis.MatchedKeywords)
            {
                var categoryKey = kvp.Key;
                var keywords = kvp.Value;

                if (!_excelCategories.TryGetValue(categoryKey, out var category))
                    continue;

                var suggestion = new OperationSuggestion
                {
                    Category = categoryKey,
                    Keywords = keywords,
                    Confidence = CalculateConfidence(keywords),
                    TargetSheet = category.ExcelSheet,
                    Description = $"{category.Description}: {string.Join(", ", keywords.Take(3))}"
                };

                // Generate suggested inputs based on category
                suggestion.SuggestedInputs = GenerateSuggestedInputs(categoryKey, keywords, category);

                if (!string.IsNullOrEmpty(suggestion.TargetSheet))
                {
                    suggestions.Add(suggestion);
                }
            }

            // Sort by confidence
            return suggestions.OrderByDescending(s => s.Confidence).ToList();
        }

        /// <summary>
        /// Generate suggested Excel inputs for a category
        /// Uses excelInputs from JSON when available
        /// </summary>
        private Dictionary<string, object> GenerateSuggestedInputs(string categoryKey, List<string> keywords, ExcelCategory category)
        {
            var inputs = new Dictionary<string, object>();

            // Use JSON-defined Excel inputs if available
            if (category.ExcelInputs != null && category.ExcelInputs.Count > 0)
            {
                foreach (var kvp in category.ExcelInputs)
                {
                    inputs[kvp.Key] = kvp.Value;
                }
            }

            // Add position modifiers for certain categories
            bool isLeft = keywords.Any(k => k.Contains("left") || k.Contains("lh") || k.Contains("driver") || k.Contains("lf"));
            bool isRight = keywords.Any(k => k.Contains("right") || k.Contains("rh") || k.Contains("passenger") || k.Contains("rf"));

            // Add side-specific inputs for applicable categories
            if (isLeft && categoryKey.Contains("HEADLIGHT"))
                inputs["Side"] = "Left";
            else if (isRight && categoryKey.Contains("HEADLIGHT"))
                inputs["Side"] = "Right";

            if (isLeft && categoryKey.Contains("TAILLIGHT"))
                inputs["Side"] = "Left";
            else if (isRight && categoryKey.Contains("TAILLIGHT"))
                inputs["Side"] = "Right";

            if (isLeft && categoryKey.Contains("MIRROR"))
                inputs["Side"] = "Left";
            else if (isRight && categoryKey.Contains("MIRROR"))
                inputs["Side"] = "Right";

            if (isLeft && categoryKey.Contains("FENDER"))
                inputs["Side"] = "Left";
            else if (isRight && categoryKey.Contains("FENDER"))
                inputs["Side"] = "Right";

            if (isLeft && categoryKey.Contains("DOOR"))
                inputs["Side"] = "Left";
            else if (isRight && categoryKey.Contains("DOOR"))
                inputs["Side"] = "Right";

            // AC system refrigerant type detection
            if (categoryKey == "AC_SYSTEM")
            {
                if (keywords.Any(k => k.Contains("r1234yf")))
                    inputs["RefrigerantType"] = "R1234yf";
                else if (keywords.Any(k => k.Contains("r134a")))
                    inputs["RefrigerantType"] = "R134a";
                else if (keywords.Any(k => k.Contains("r744")))
                    inputs["RefrigerantType"] = "R744";
            }

            return inputs;
        }

        private double CalculateConfidence(List<string> matchedKeywords)
        {
            // More matched keywords = higher confidence
            return Math.Min(1.0, matchedKeywords.Count * 0.25);
        }

        /// <summary>
        /// Read estimate from file
        /// </summary>
        public EstimateAnalysis ParseEstimateFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Estimate file not found: {filePath}");

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            string text;

            switch (extension)
            {
                case ".txt":
                case ".csv":
                    text = File.ReadAllText(filePath);
                    break;
                case ".pdf":
                    throw new NotSupportedException("PDF parsing requires additional libraries. Please copy/paste the estimate text or use a .txt file.");
                default:
                    text = File.ReadAllText(filePath);
                    break;
            }

            return ParseEstimate(text);
        }

        /// <summary>
        /// Get all available keyword categories
        /// </summary>
        public IReadOnlyDictionary<string, ExcelCategory> GetCategories()
        {
            return _excelCategories;
        }

        /// <summary>
        /// Add or update a keyword category at runtime
        /// </summary>
        public void AddCategory(string key, ExcelCategory category)
        {
            _excelCategories[key] = category;
        }

        /// <summary>
        /// Add keywords to an existing category
        /// </summary>
        public void AddKeywords(string categoryKey, params string[] keywords)
        {
            if (_excelCategories.TryGetValue(categoryKey, out var category))
            {
                foreach (var keyword in keywords)
                {
                    if (!category.Keywords.Contains(keyword.ToLowerInvariant()))
                    {
                        category.Keywords.Add(keyword.ToLowerInvariant());
                    }
                }
            }
        }
    }

    #region Data Classes

    /// <summary>
    /// JSON file structure
    /// </summary>
    public class KeywordMappingsFile
    {
        public Dictionary<string, ExcelCategory> ExcelCategories { get; set; } = new();
        public Dictionary<string, List<string>> OperationTypes { get; set; } = new();
    }

    /// <summary>
    /// Category definition loaded from JSON
    /// </summary>
    public class ExcelCategory
    {
        public string ExcelSheet { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Keywords { get; set; } = new();
        public Dictionary<string, string> ExcelInputs { get; set; } = new();
    }

    /// <summary>
    /// Result of estimate analysis
    /// </summary>
    public class EstimateAnalysis
    {
        public string RawText { get; set; } = "";
        public string? VIN { get; set; }
        public string? VehicleYear { get; set; }
        public string? VehicleMake { get; set; }
        public string? VehicleModel { get; set; }

        public Dictionary<string, List<string>> MatchedKeywords { get; set; } = new();
        public List<EstimateLineItem> LineItems { get; set; } = new();
        public List<OperationSuggestion> Suggestions { get; set; } = new();

        public string VehicleDescription =>
            !string.IsNullOrEmpty(VehicleYear)
                ? $"{VehicleYear} {VehicleMake} {VehicleModel}".Trim()
                : "";
    }

    /// <summary>
    /// A single line item from the estimate
    /// </summary>
    public class EstimateLineItem
    {
        public string RawLine { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string ExcelSheet { get; set; } = "";
        public string OperationType { get; set; } = "";
        public decimal Price { get; set; }
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public int Quantity { get; set; } = 1;
    }

    /// <summary>
    /// Suggested operation to apply
    /// </summary>
    public class OperationSuggestion
    {
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string TargetSheet { get; set; } = "";
        public List<string> Keywords { get; set; } = new();
        public double Confidence { get; set; }
        public Dictionary<string, object> SuggestedInputs { get; set; } = new();
        public string ConfidenceDisplay => $"{(Confidence * 100):F0}%";
    }

    #endregion
}
