#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Smart Estimate Engine - Advanced learning and calculation system
    ///
    /// Capabilities:
    /// 1. Learns operation sequences (what operations follow others)
    /// 2. Calculates hours based on learned ratios
    /// 3. Understands part relationships (if X is replaced, Y needs R&I)
    /// 4. Tracks refinish panel counts and adjacency
    /// 5. Learns pricing patterns
    /// </summary>
    public class SmartEstimateEngine
    {
        private readonly string _dataFilePath;
        private SmartKnowledgeBase _knowledge;

        private static SmartEstimateEngine? _instance;
        public static SmartEstimateEngine Instance => _instance ??= new SmartEstimateEngine();

        public SmartEstimateEngine()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(appDataPath);
            _dataFilePath = Path.Combine(appDataPath, "smart_knowledge.json");

            _knowledge = LoadKnowledge();
            InitializeBaseKnowledge();
        }

        #region Knowledge Base

        private SmartKnowledgeBase LoadKnowledge()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var kb = JsonSerializer.Deserialize<SmartKnowledgeBase>(json);
                    if (kb != null) return kb;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartEngine] Load error: {ex.Message}");
            }
            return new SmartKnowledgeBase();
        }

        public void SaveKnowledge()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_knowledge, options);
                File.WriteAllText(_dataFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartEngine] Save error: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize with base collision repair knowledge
        /// </summary>
        private void InitializeBaseKnowledge()
        {
            if (_knowledge.Initialized) return;

            // Part hierarchy - what parts contain or connect to others
            _knowledge.PartHierarchy = new Dictionary<string, PartRelationship>
            {
                ["front bumper cover"] = new PartRelationship
                {
                    Part = "front bumper cover",
                    Category = "Part Operations",
                    RelatedParts = new[] { "front bumper reinforcement", "front bumper absorber", "grille", "fog light", "headlight" },
                    RequiresRefinish = true,
                    AdjacentPanels = new[] { "hood", "fender" },
                    TypicalOperations = new[] { "R&I", "Replace", "Repair", "Refinish" }
                },
                ["rear bumper cover"] = new PartRelationship
                {
                    Part = "rear bumper cover",
                    Category = "Part Operations",
                    RelatedParts = new[] { "rear bumper reinforcement", "rear bumper absorber", "tail light", "backup sensor" },
                    RequiresRefinish = true,
                    AdjacentPanels = new[] { "quarter panel", "trunk lid", "decklid" },
                    TypicalOperations = new[] { "R&I", "Replace", "Repair", "Refinish" }
                },
                ["fender"] = new PartRelationship
                {
                    Part = "fender",
                    Category = "Part Operations",
                    RelatedParts = new[] { "fender liner", "fender flare", "side marker", "antenna" },
                    RequiresRefinish = true,
                    AdjacentPanels = new[] { "hood", "front door", "front bumper cover", "a-pillar" },
                    TypicalOperations = new[] { "R&I", "Replace", "Repair", "Refinish", "Blend" }
                },
                ["hood"] = new PartRelationship
                {
                    Part = "hood",
                    Category = "Part Operations",
                    RelatedParts = new[] { "hood hinge", "hood latch", "hood strut", "hood insulator" },
                    RequiresRefinish = true,
                    AdjacentPanels = new[] { "fender", "cowl", "windshield" },
                    TypicalOperations = new[] { "R&I", "Replace", "Repair", "Refinish" }
                },
                ["front door"] = new PartRelationship
                {
                    Part = "front door",
                    Category = "Part Operations",
                    RelatedParts = new[] { "door shell", "door skin", "door handle", "door mirror", "door glass", "door trim panel", "door wiring" },
                    RequiresRefinish = true,
                    AdjacentPanels = new[] { "fender", "rear door", "rocker panel", "a-pillar", "b-pillar" },
                    TypicalOperations = new[] { "R&I", "Replace", "Repair", "Refinish", "Blend" }
                },
                ["rear door"] = new PartRelationship
                {
                    Part = "rear door",
                    Category = "Part Operations",
                    RelatedParts = new[] { "door shell", "door skin", "door handle", "door glass", "door trim panel" },
                    RequiresRefinish = true,
                    AdjacentPanels = new[] { "front door", "quarter panel", "rocker panel", "b-pillar", "c-pillar" },
                    TypicalOperations = new[] { "R&I", "Replace", "Repair", "Refinish", "Blend" }
                },
                ["quarter panel"] = new PartRelationship
                {
                    Part = "quarter panel",
                    Category = "Part Operations",
                    RelatedParts = new[] { "quarter glass", "fuel door", "tail light" },
                    RequiresRefinish = true,
                    AdjacentPanels = new[] { "rear door", "rear bumper cover", "trunk lid", "roof", "c-pillar" },
                    TypicalOperations = new[] { "Repair", "Sectioning", "Replace", "Refinish", "Blend" },
                    IsStructural = true
                },
                ["roof"] = new PartRelationship
                {
                    Part = "roof",
                    Category = "Part Operations",
                    RelatedParts = new[] { "headliner", "sunroof", "roof rail", "roof rack", "antenna" },
                    RequiresRefinish = true,
                    AdjacentPanels = new[] { "a-pillar", "b-pillar", "c-pillar", "quarter panel", "windshield", "back glass" },
                    TypicalOperations = new[] { "Repair", "Replace", "Refinish" },
                    IsStructural = true
                },
                ["trunk lid"] = new PartRelationship
                {
                    Part = "trunk lid",
                    Category = "Part Operations",
                    RelatedParts = new[] { "trunk hinge", "trunk latch", "trunk strut", "spoiler", "license plate light" },
                    RequiresRefinish = true,
                    AdjacentPanels = new[] { "quarter panel", "rear bumper cover" },
                    TypicalOperations = new[] { "R&I", "Replace", "Repair", "Refinish" }
                },
                ["liftgate"] = new PartRelationship
                {
                    Part = "liftgate",
                    Category = "Part Operations",
                    RelatedParts = new[] { "liftgate glass", "liftgate strut", "liftgate wiring", "rear wiper" },
                    RequiresRefinish = true,
                    AdjacentPanels = new[] { "quarter panel", "rear bumper cover", "roof" },
                    TypicalOperations = new[] { "R&I", "Replace", "Repair", "Refinish" }
                }
            };

            // Operation sequences - what typically follows what
            _knowledge.OperationSequences = new Dictionary<string, OperationSequence>
            {
                ["replace_panel"] = new OperationSequence
                {
                    TriggerOperation = "Replace",
                    FollowingOperations = new[]
                    {
                        new SequencedOp { Operation = "Refinish", Probability = 0.95, Notes = "New panel needs paint" },
                        new SequencedOp { Operation = "Corrosion Protection", Probability = 0.90, Notes = "Weld-through primer" },
                        new SequencedOp { Operation = "Seam Sealer", Probability = 0.85, Notes = "Match factory seams" }
                    }
                },
                ["repair_panel"] = new OperationSequence
                {
                    TriggerOperation = "Repair",
                    FollowingOperations = new[]
                    {
                        new SequencedOp { Operation = "Refinish", Probability = 0.90, Notes = "Repaired area needs paint" },
                        new SequencedOp { Operation = "Featheredge", Probability = 0.80, Notes = "Prep damaged edges" }
                    }
                },
                ["refinish_panel"] = new OperationSequence
                {
                    TriggerOperation = "Refinish",
                    FollowingOperations = new[]
                    {
                        new SequencedOp { Operation = "Clear Coat", Probability = 0.95, Notes = "2-stage or 3-stage" },
                        new SequencedOp { Operation = "Blend Adjacent", Probability = 0.60, Notes = "Color match" }
                    }
                }
            };

            // Calculation rules
            _knowledge.CalculationRules = new List<CalculationRule>
            {
                new CalculationRule
                {
                    Name = "Refinish Hours from Repair",
                    Description = "Refinish hours typically 1.5-2x repair hours",
                    InputField = "RepairHours",
                    OutputField = "RefinishHours",
                    Multiplier = 1.5m,
                    MinValue = 0.5m
                },
                new CalculationRule
                {
                    Name = "Blend Hours",
                    Description = "Blend is typically 50% of full refinish",
                    InputField = "RefinishHours",
                    OutputField = "BlendHours",
                    Multiplier = 0.5m,
                    MinValue = 0.3m
                },
                new CalculationRule
                {
                    Name = "Clear Coat Hours",
                    Description = "Clear coat adds ~0.4 per panel for 2-stage",
                    InputField = "PanelCount",
                    OutputField = "ClearCoatHours",
                    Multiplier = 0.4m,
                    MinValue = 0.4m
                },
                new CalculationRule
                {
                    Name = "3-Stage Additional",
                    Description = "3-stage adds 50% more refinish time",
                    InputField = "RefinishHours",
                    OutputField = "ThreeStageAdditional",
                    Multiplier = 0.5m,
                    Condition = "Is3Stage"
                }
            };

            // Labor rate categories
            _knowledge.LaborCategories = new Dictionary<string, LaborCategory>
            {
                ["Body"] = new LaborCategory { Name = "Body", DefaultRate = 60m, Operations = new[] { "Repair", "Replace", "R&I", "Sectioning" } },
                ["Refinish"] = new LaborCategory { Name = "Refinish", DefaultRate = 60m, Operations = new[] { "Refinish", "Blend", "Clear Coat" } },
                ["Mechanical"] = new LaborCategory { Name = "Mechanical", DefaultRate = 100m, Operations = new[] { "Alignment", "AC", "Suspension" } },
                ["Frame"] = new LaborCategory { Name = "Frame", DefaultRate = 80m, Operations = new[] { "Frame", "Measure", "Pull" } }
            };

            _knowledge.Initialized = true;
            _knowledge.LastUpdated = DateTime.Now;
            SaveKnowledge();
        }

        #endregion

        #region Smart Analysis

        /// <summary>
        /// Analyze an estimate and generate smart operations
        /// </summary>
        public SmartAnalysisResult AnalyzeEstimate(string estimateText)
        {
            var result = new SmartAnalysisResult();
            var lines = ParseEstimateLines(estimateText);

            // First pass: identify all parts and operations
            foreach (var line in lines)
            {
                var parsed = ParseLine(line);
                if (parsed != null)
                {
                    result.ParsedLines.Add(parsed);
                }
            }

            // Second pass: apply relationships and sequences
            foreach (var parsed in result.ParsedLines)
            {
                var operations = GenerateOperationsForLine(parsed, result.ParsedLines);
                result.GeneratedOperations.AddRange(operations);
            }

            // Third pass: calculate refinish panel counts and blending
            CalculateRefinishPanels(result);

            // Fourth pass: apply calculation rules
            ApplyCalculations(result);

            // Fifth pass: determine operation order
            OrderOperations(result);

            result.Summary = GenerateSummary(result);

            return result;
        }

        /// <summary>
        /// Parse individual lines from estimate text
        /// </summary>
        private List<string> ParseEstimateLines(string text)
        {
            var lines = new List<string>();
            var rawLines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in rawLines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (trimmed.Length < 5) continue;
                if (trimmed.StartsWith("---") || trimmed.StartsWith("===")) continue;
                if (trimmed.All(c => char.IsDigit(c) || c == '.' || c == '$' || c == ' ')) continue;

                lines.Add(trimmed);
            }

            return lines;
        }

        /// <summary>
        /// Parse a single estimate line into structured data
        /// </summary>
        public ParsedEstimateLine? ParseLine(string line)
        {
            var result = new ParsedEstimateLine { RawLine = line };
            var lower = line.ToLowerInvariant();

            // Extract operation type
            result.OperationType = ExtractOperationType(lower);

            // Extract part name
            result.PartName = ExtractPartName(lower);

            // Extract position (front/rear/left/right)
            result.Position = ExtractPosition(lower);
            result.Side = ExtractSide(lower);

            // Extract numeric values
            ExtractNumericValues(line, result);

            // Determine category
            result.Category = DetermineCategory(result.PartName, result.OperationType);

            // Skip if no meaningful data
            if (string.IsNullOrEmpty(result.PartName) && string.IsNullOrEmpty(result.OperationType))
            {
                return null;
            }

            // Build description
            result.Description = BuildDescription(result);

            return result;
        }

        private string ExtractOperationType(string lower)
        {
            var operationPatterns = new Dictionary<string, string[]>
            {
                ["Replace"] = new[] { "replace", "repl", "rplc", "new" },
                ["Repair"] = new[] { "repair", "rpr", "fix" },
                ["R&I"] = new[] { "r&i", "r+i", "r & i", "remove and install", "remove & install", "remove/install" },
                ["Refinish"] = new[] { "refinish", "rfn", "paint", "respray" },
                ["Blend"] = new[] { "blend", "bld" },
                ["Overhaul"] = new[] { "o/h", "overhaul", "ovhl" },
                ["Sublet"] = new[] { "sublet", "sub" },
                ["Add"] = new[] { "add labor", "add time" }
            };

            foreach (var kvp in operationPatterns)
            {
                foreach (var pattern in kvp.Value)
                {
                    if (lower.Contains(pattern))
                        return kvp.Key;
                }
            }

            // Check for single letter codes at start
            if (Regex.IsMatch(lower, @"^\s*r\s+"))
                return "Repair";
            if (Regex.IsMatch(lower, @"^\s*rp\s+"))
                return "Replace";

            return "";
        }

        private string ExtractPartName(string lower)
        {
            // Ordered by specificity (longer matches first)
            var knownParts = new[]
            {
                "front bumper cover", "rear bumper cover", "bumper cover",
                "front bumper reinforcement", "rear bumper reinforcement", "bumper reinforcement",
                "front bumper absorber", "rear bumper absorber", "bumper absorber",
                "front bumper", "rear bumper", "bumper",
                "inner fender", "fender liner", "fender flare", "fender",
                "hood hinge", "hood latch", "hood strut", "hood insulator", "hood",
                "front door shell", "rear door shell", "door shell",
                "front door skin", "rear door skin", "door skin",
                "front door handle", "rear door handle", "door handle",
                "front door mirror", "door mirror", "mirror",
                "front door glass", "rear door glass", "door glass",
                "front door trim", "rear door trim", "door trim",
                "front door", "rear door", "door",
                "quarter panel glass", "quarter glass",
                "quarter panel", "quarter",
                "roof panel", "roof rail", "sunroof", "headliner", "roof",
                "trunk lid", "decklid", "trunk floor", "trunk",
                "liftgate glass", "liftgate", "tailgate",
                "grille", "radiator grille",
                "headlight", "headlamp", "head light",
                "tail light", "taillight", "taillamp", "brake light",
                "fog light", "fog lamp",
                "windshield", "front glass",
                "back glass", "rear glass",
                "a-pillar", "b-pillar", "c-pillar", "d-pillar", "pillar",
                "rocker panel", "rocker", "side sill",
                "radiator support", "core support", "radiator",
                "frame rail", "subframe", "frame",
                "control arm", "ball joint", "tie rod", "strut", "shock",
                "condenser", "evaporator", "compressor",
                "airbag", "air bag", "srs",
                "seat belt", "seatbelt"
            };

            foreach (var part in knownParts)
            {
                if (lower.Contains(part))
                    return part;
            }

            return "";
        }

        private string ExtractPosition(string lower)
        {
            if (lower.Contains("front") || lower.Contains("frt") || lower.Contains("fr "))
                return "Front";
            if (lower.Contains("rear") || lower.Contains("rr ") || lower.Contains("back"))
                return "Rear";
            if (lower.Contains("center") || lower.Contains("ctr"))
                return "Center";
            return "";
        }

        private string ExtractSide(string lower)
        {
            if (lower.Contains("left") || lower.Contains("lh ") || lower.Contains(" lh") || lower.Contains("driver"))
                return "Left";
            if (lower.Contains("right") || lower.Contains("rh ") || lower.Contains(" rh") || lower.Contains("passenger"))
                return "Right";
            if (lower.Contains("both"))
                return "Both";
            return "";
        }

        private void ExtractNumericValues(string line, ParsedEstimateLine result)
        {
            // Extract hours patterns: "1.5 hrs", "2.0 labor", "0.5 ref"
            var hoursMatch = Regex.Match(line, @"(\d+\.?\d*)\s*(hrs?|hours?|labor|lab)", RegexOptions.IgnoreCase);
            if (hoursMatch.Success && decimal.TryParse(hoursMatch.Groups[1].Value, out var hours))
            {
                result.LaborHours = hours;
            }

            // Refinish hours
            var refMatch = Regex.Match(line, @"(\d+\.?\d*)\s*(ref|rfn|refinish|paint)", RegexOptions.IgnoreCase);
            if (refMatch.Success && decimal.TryParse(refMatch.Groups[1].Value, out var refHours))
            {
                result.RefinishHours = refHours;
            }

            // Price: $125.00 or 125.00
            var priceMatch = Regex.Match(line, @"\$?\s*([\d,]+\.?\d{0,2})\s*(?:$|\s|each|ea)");
            if (priceMatch.Success)
            {
                var priceStr = priceMatch.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(priceStr, out var price) && price > 10) // Ignore small numbers
                {
                    result.Price = price;
                }
            }

            // Quantity
            var qtyMatch = Regex.Match(line, @"(?:qty|quantity|x)\s*(\d+)", RegexOptions.IgnoreCase);
            if (qtyMatch.Success && int.TryParse(qtyMatch.Groups[1].Value, out var qty))
            {
                result.Quantity = qty;
            }
            else
            {
                result.Quantity = 1;
            }
        }

        private string DetermineCategory(string partName, string operationType)
        {
            if (_knowledge.PartHierarchy.TryGetValue(partName, out var rel))
            {
                return rel.Category;
            }

            // Default categorization
            if (operationType == "Refinish" || operationType == "Blend")
                return "Refinish Operations";
            if (partName.Contains("frame") || partName.Contains("pillar") || partName.Contains("rocker"))
                return "Body Operations";
            if (partName.Contains("ac") || partName.Contains("suspension") || partName.Contains("wheel"))
                return "Mechanical Operations";
            if (partName.Contains("airbag") || partName.Contains("seatbelt"))
                return "SRS Operations";

            return "Part Operations";
        }

        private string BuildDescription(ParsedEstimateLine parsed)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(parsed.Position))
                parts.Add(parsed.Position);
            if (!string.IsNullOrEmpty(parsed.Side))
                parts.Add(parsed.Side);
            if (!string.IsNullOrEmpty(parsed.PartName))
                parts.Add(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(parsed.PartName));

            return string.Join(" ", parts);
        }

        #endregion

        #region Operation Generation

        /// <summary>
        /// Generate all operations for a parsed line, including related operations
        /// </summary>
        private List<SmartOperation> GenerateOperationsForLine(ParsedEstimateLine parsed, List<ParsedEstimateLine> allLines)
        {
            var operations = new List<SmartOperation>();

            // Primary operation from the line
            var primaryOp = new SmartOperation
            {
                OperationType = parsed.OperationType,
                Description = parsed.Description,
                PartName = parsed.PartName,
                Category = parsed.Category,
                LaborHours = parsed.LaborHours,
                RefinishHours = parsed.RefinishHours,
                Price = parsed.Price,
                Quantity = parsed.Quantity,
                Position = parsed.Position,
                Side = parsed.Side,
                IsPrimary = true,
                Confidence = 1.0,
                Source = "Parsed from estimate"
            };
            operations.Add(primaryOp);

            // Get part relationship
            if (_knowledge.PartHierarchy.TryGetValue(parsed.PartName, out var partRel))
            {
                // Add sequence-based operations
                if (_knowledge.OperationSequences.TryGetValue($"{parsed.OperationType.ToLower()}_panel", out var sequence))
                {
                    foreach (var seqOp in sequence.FollowingOperations)
                    {
                        // Check if this operation already exists
                        var existsInEstimate = allLines.Any(l =>
                            l.PartName == parsed.PartName &&
                            l.OperationType.Equals(seqOp.Operation, StringComparison.OrdinalIgnoreCase));

                        if (!existsInEstimate && seqOp.Probability > 0.5)
                        {
                            var followOp = new SmartOperation
                            {
                                OperationType = seqOp.Operation,
                                Description = $"{parsed.Description} - {seqOp.Operation}",
                                PartName = parsed.PartName,
                                Category = seqOp.Operation == "Refinish" || seqOp.Operation == "Blend" ? "Refinish Operations" : parsed.Category,
                                Position = parsed.Position,
                                Side = parsed.Side,
                                IsPrimary = false,
                                Confidence = seqOp.Probability,
                                Source = $"Auto-added: {seqOp.Notes}",
                                DependsOn = primaryOp.Description
                            };

                            // Calculate hours based on rules
                            if (seqOp.Operation == "Refinish" && parsed.LaborHours > 0)
                            {
                                followOp.RefinishHours = parsed.LaborHours * 1.5m;
                            }
                            else if (seqOp.Operation == "Blend")
                            {
                                followOp.RefinishHours = parsed.RefinishHours > 0 ? parsed.RefinishHours * 0.5m : 0.5m;
                            }

                            operations.Add(followOp);
                        }
                    }
                }

                // Check for adjacent panel blending
                if (parsed.OperationType == "Refinish" || parsed.OperationType == "Replace")
                {
                    foreach (var adjacent in partRel.AdjacentPanels)
                    {
                        // Check if adjacent is being refinished already
                        var adjacentRefinished = allLines.Any(l =>
                            l.PartName.Contains(adjacent) &&
                            (l.OperationType == "Refinish" || l.OperationType == "Blend"));

                        if (!adjacentRefinished)
                        {
                            var blendOp = new SmartOperation
                            {
                                OperationType = "Blend",
                                Description = $"{System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(adjacent)} - Blend for color match",
                                PartName = adjacent,
                                Category = "Refinish Operations",
                                Position = parsed.Position,
                                Side = parsed.Side,
                                IsPrimary = false,
                                Confidence = 0.6,
                                Source = $"Blend adjacent to {parsed.Description}",
                                DependsOn = primaryOp.Description,
                                RefinishHours = 0.5m // Default blend time
                            };
                            operations.Add(blendOp);
                        }
                    }
                }
            }

            return operations;
        }

        #endregion

        #region Calculations

        /// <summary>
        /// Calculate refinish panel counts for proper pricing
        /// </summary>
        private void CalculateRefinishPanels(SmartAnalysisResult result)
        {
            var refinishOps = result.GeneratedOperations
                .Where(o => o.OperationType == "Refinish" || o.OperationType == "Blend")
                .ToList();

            int majorPanels = 0;
            int blendPanels = 0;

            foreach (var op in refinishOps)
            {
                if (op.OperationType == "Refinish")
                {
                    majorPanels++;
                    op.PanelNumber = majorPanels;
                    op.IsFirstPanel = majorPanels == 1;
                }
                else if (op.OperationType == "Blend")
                {
                    blendPanels++;
                }
            }

            result.TotalRefinishPanels = majorPanels;
            result.TotalBlendPanels = blendPanels;
        }

        /// <summary>
        /// Apply calculation rules to operations
        /// </summary>
        private void ApplyCalculations(SmartAnalysisResult result)
        {
            decimal totalLaborHours = 0;
            decimal totalRefinishHours = 0;
            decimal totalPrice = 0;

            foreach (var op in result.GeneratedOperations)
            {
                // Apply learned ratios if hours are missing
                if (op.LaborHours == 0 && op.OperationType == "Repair")
                {
                    // Use learned average for this part
                    op.LaborHours = GetLearnedAverage(op.PartName, "LaborHours", 1.0m);
                }

                if (op.RefinishHours == 0 && (op.OperationType == "Refinish" || op.OperationType == "Blend"))
                {
                    if (op.OperationType == "Refinish")
                    {
                        op.RefinishHours = GetLearnedAverage(op.PartName, "RefinishHours", 2.0m);
                    }
                    else
                    {
                        op.RefinishHours = GetLearnedAverage(op.PartName, "BlendHours", 0.5m);
                    }
                }

                // Calculate total price
                if (_knowledge.LaborCategories.TryGetValue(op.Category.Replace(" Operations", ""), out var laborCat))
                {
                    var rate = laborCat.DefaultRate;
                    op.CalculatedPrice = (op.LaborHours * rate) + (op.RefinishHours * rate);
                }

                totalLaborHours += op.LaborHours;
                totalRefinishHours += op.RefinishHours;
                totalPrice += op.Price > 0 ? op.Price : op.CalculatedPrice;
            }

            result.TotalLaborHours = totalLaborHours;
            result.TotalRefinishHours = totalRefinishHours;
            result.TotalEstimatedPrice = totalPrice;
        }

        private decimal GetLearnedAverage(string partName, string field, decimal defaultValue)
        {
            // Check learned data
            if (_knowledge.LearnedAverages.TryGetValue($"{partName}|{field}", out var learned))
            {
                return learned.Average;
            }
            return defaultValue;
        }

        /// <summary>
        /// Order operations in logical sequence
        /// </summary>
        private void OrderOperations(SmartAnalysisResult result)
        {
            // Order: Disassembly → Structural → Body → Parts → Refinish → Reassembly
            var orderPriority = new Dictionary<string, int>
            {
                ["R&I"] = 1,
                ["Disassembly"] = 1,
                ["Frame"] = 2,
                ["Structural"] = 2,
                ["Body"] = 3,
                ["Repair"] = 3,
                ["Replace"] = 4,
                ["Refinish"] = 5,
                ["Blend"] = 6,
                ["Clear Coat"] = 7,
                ["Reassembly"] = 8
            };

            result.GeneratedOperations = result.GeneratedOperations
                .OrderBy(o => orderPriority.GetValueOrDefault(o.OperationType, 5))
                .ThenBy(o => o.DependsOn ?? "")
                .ThenBy(o => o.Description)
                .ToList();

            // Assign sequence numbers
            int seq = 1;
            foreach (var op in result.GeneratedOperations)
            {
                op.SequenceNumber = seq++;
            }
        }

        private string GenerateSummary(SmartAnalysisResult result)
        {
            var primary = result.GeneratedOperations.Count(o => o.IsPrimary);
            var added = result.GeneratedOperations.Count(o => !o.IsPrimary);

            return $"{primary} operations from estimate + {added} auto-added | " +
                   $"{result.TotalLaborHours:F1}h labor, {result.TotalRefinishHours:F1}h refinish | " +
                   $"{result.TotalRefinishPanels} refinish + {result.TotalBlendPanels} blend panels";
        }

        #endregion

        #region Learning

        /// <summary>
        /// Learn from a completed estimate
        /// </summary>
        public void LearnFromCompletedEstimate(SmartAnalysisResult result, List<SmartOperation> actualOperations)
        {
            foreach (var actual in actualOperations)
            {
                // Learn average hours for this part
                var key = $"{actual.PartName}|LaborHours";
                if (actual.LaborHours > 0)
                {
                    UpdateLearnedAverage(key, actual.LaborHours);
                }

                key = $"{actual.PartName}|RefinishHours";
                if (actual.RefinishHours > 0)
                {
                    UpdateLearnedAverage(key, actual.RefinishHours);
                }

                // Learn operation sequences
                LearnOperationSequence(actual, actualOperations);
            }

            _knowledge.TotalEstimatesLearned++;
            _knowledge.LastUpdated = DateTime.Now;
            SaveKnowledge();
        }

        private void UpdateLearnedAverage(string key, decimal value)
        {
            if (_knowledge.LearnedAverages.TryGetValue(key, out var existing))
            {
                // Running average
                existing.Count++;
                existing.Total += value;
                existing.Average = existing.Total / existing.Count;
            }
            else
            {
                _knowledge.LearnedAverages[key] = new LearnedAverage
                {
                    Key = key,
                    Count = 1,
                    Total = value,
                    Average = value
                };
            }
        }

        private void LearnOperationSequence(SmartOperation op, List<SmartOperation> allOps)
        {
            // Find operations that followed this one
            var index = allOps.IndexOf(op);
            if (index < 0 || index >= allOps.Count - 1) return;

            var followers = allOps.Skip(index + 1)
                .Where(o => o.PartName == op.PartName)
                .Take(3)
                .ToList();

            foreach (var follower in followers)
            {
                var seqKey = $"{op.OperationType}|{op.PartName}|{follower.OperationType}";
                if (_knowledge.LearnedSequences.TryGetValue(seqKey, out var existing))
                {
                    existing.Count++;
                }
                else
                {
                    _knowledge.LearnedSequences[seqKey] = new LearnedSequence
                    {
                        TriggerOp = op.OperationType,
                        PartName = op.PartName,
                        FollowingOp = follower.OperationType,
                        Count = 1
                    };
                }
            }
        }

        #endregion

        #region Statistics

        public SmartEngineStats GetStats()
        {
            return new SmartEngineStats
            {
                TotalEstimatesLearned = _knowledge.TotalEstimatesLearned,
                LearnedAveragesCount = _knowledge.LearnedAverages.Count,
                LearnedSequencesCount = _knowledge.LearnedSequences.Count,
                KnownPartsCount = _knowledge.PartHierarchy.Count,
                LastUpdated = _knowledge.LastUpdated
            };
        }

        #endregion
    }

    #region Data Models

    public class SmartKnowledgeBase
    {
        public bool Initialized { get; set; }
        public DateTime LastUpdated { get; set; }
        public int TotalEstimatesLearned { get; set; }

        public Dictionary<string, PartRelationship> PartHierarchy { get; set; } = new();
        public Dictionary<string, OperationSequence> OperationSequences { get; set; } = new();
        public List<CalculationRule> CalculationRules { get; set; } = new();
        public Dictionary<string, LaborCategory> LaborCategories { get; set; } = new();

        // Learned data
        public Dictionary<string, LearnedAverage> LearnedAverages { get; set; } = new();
        public Dictionary<string, LearnedSequence> LearnedSequences { get; set; } = new();
    }

    public class PartRelationship
    {
        public string Part { get; set; } = "";
        public string Category { get; set; } = "";
        public string[] RelatedParts { get; set; } = Array.Empty<string>();
        public string[] AdjacentPanels { get; set; } = Array.Empty<string>();
        public string[] TypicalOperations { get; set; } = Array.Empty<string>();
        public bool RequiresRefinish { get; set; }
        public bool IsStructural { get; set; }
    }

    public class OperationSequence
    {
        public string TriggerOperation { get; set; } = "";
        public SequencedOp[] FollowingOperations { get; set; } = Array.Empty<SequencedOp>();
    }

    public class SequencedOp
    {
        public string Operation { get; set; } = "";
        public double Probability { get; set; }
        public string Notes { get; set; } = "";
    }

    public class CalculationRule
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string InputField { get; set; } = "";
        public string OutputField { get; set; } = "";
        public decimal Multiplier { get; set; }
        public decimal MinValue { get; set; }
        public string? Condition { get; set; }
    }

    public class LaborCategory
    {
        public string Name { get; set; } = "";
        public decimal DefaultRate { get; set; }
        public string[] Operations { get; set; } = Array.Empty<string>();
    }

    public class LearnedAverage
    {
        public string Key { get; set; } = "";
        public int Count { get; set; }
        public decimal Total { get; set; }
        public decimal Average { get; set; }
    }

    public class LearnedSequence
    {
        public string TriggerOp { get; set; } = "";
        public string PartName { get; set; } = "";
        public string FollowingOp { get; set; } = "";
        public int Count { get; set; }
    }

    public class SmartAnalysisResult
    {
        public List<ParsedEstimateLine> ParsedLines { get; set; } = new();
        public List<SmartOperation> GeneratedOperations { get; set; } = new();

        public int TotalRefinishPanels { get; set; }
        public int TotalBlendPanels { get; set; }
        public decimal TotalLaborHours { get; set; }
        public decimal TotalRefinishHours { get; set; }
        public decimal TotalEstimatedPrice { get; set; }

        public string Summary { get; set; } = "";
    }

    public class SmartOperation
    {
        public int SequenceNumber { get; set; }
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public string PartName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Position { get; set; } = "";
        public string Side { get; set; } = "";

        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public decimal CalculatedPrice { get; set; }
        public int Quantity { get; set; } = 1;

        public bool IsPrimary { get; set; }
        public double Confidence { get; set; }
        public string Source { get; set; } = "";
        public string? DependsOn { get; set; }

        // Refinish-specific
        public int PanelNumber { get; set; }
        public bool IsFirstPanel { get; set; }
    }

    public class SmartEngineStats
    {
        public int TotalEstimatesLearned { get; set; }
        public int LearnedAveragesCount { get; set; }
        public int LearnedSequencesCount { get; set; }
        public int KnownPartsCount { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    #endregion
}
