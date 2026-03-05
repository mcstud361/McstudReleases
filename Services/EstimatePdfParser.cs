#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Service for parsing estimate PDFs (CCC, Mitchell, Audatex) to extract line items.
    /// The extracted data is fed into EstimateLearningService for pattern learning.
    ///
    /// SMART PARSING FEATURES:
    /// 1. Auto-detects estimate source (CCC ONE, Mitchell, Audatex)
    /// 2. Handles fixed-width column formats common in CCC PDFs
    /// 3. Normalizes part names and abbreviations
    /// 4. Links manual lines (#) to parent parts
    /// 5. Extracts vehicle info, VIN, and totals
    /// </summary>
    public class EstimatePdfParser
    {
        // Singleton instance
        private static EstimatePdfParser? _instance;
        public static EstimatePdfParser Instance => _instance ??= new EstimatePdfParser();

        // Part name normalization mappings
        private static readonly Dictionary<string, string> _partNameNormalizations = new(StringComparer.OrdinalIgnoreCase)
        {
            // Position abbreviations
            { "frt", "Front" }, { "rr", "Rear" }, { "lh", "Left" }, { "rh", "Right" },
            { "lt", "Left" }, { "rt", "Right" }, { "lf", "Left Front" }, { "rf", "Right Front" },
            { "lr", "Left Rear" }, { "ctr", "Center" }, { "upr", "Upper" }, { "lwr", "Lower" },
            // Part abbreviations
            { "bpr", "Bumper" }, { "cvr", "Cover" }, { "pnl", "Panel" }, { "qtr", "Quarter" },
            { "assy", "Assembly" }, { "brkt", "Bracket" }, { "reinf", "Reinforcement" },
            { "mldg", "Molding" }, { "hndl", "Handle" }, { "mirr", "Mirror" },
            { "hdlmp", "Headlamp" }, { "tllmp", "Taillamp" }, { "flr", "Floor" },
            { "whl", "Wheel" }, { "wndsld", "Windshield" }, { "w/s", "Windshield" },
            // Operation abbreviations
            { "w/", "with" }, { "w/o", "without" }, { "incl", "Included" },
        };

        // Common additional operation keywords (manual lines)
        // These are operations that estimators manually add (# lines in CCC)
        private static readonly HashSet<string> _additionalOperationKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            // Surface prep
            "adhesion promoter", "flex additive", "flex agent", "plastic prep", "primer surfacer",
            "seam sealer", "corrosion protection", "cavity wax", "undercoating", "rust preventative",
            "sound deadener", "anti-corrosion", "weld-thru primer", "weld thru", "e-coat",
            "chip guard", "stone guard", "bedliner", "undercoat", "rubberize",

            // Sanding/finishing
            "wet sand", "color sand", "wet/dry", "dry sand", "block sand", "final sand",
            "de-nib", "denib", "rub-out", "rub out", "buff", "polish", "compound",
            "cut and buff", "color match", "color verify", "spray card", "let down panel",

            // Masking/protection
            "backtape", "back tape", "mask", "tape for", "mask for", "masking",
            "cover car", "cover interior", "cover trunk", "cover engine", "cover wheels",
            "bagging", "bag for", "plastic cover", "wheel cover", "tire cover",

            // Setup/handling
            "stage and secure", "trial fit", "test fit", "dry fit", "mock up",
            "transfer parts", "swap parts", "parts transfer", "r&i for access",
            "remove for access", "access time", "additional labor",

            // Scanning/calibration
            "pre-scan", "post-scan", "pre scan", "post scan", "diagnostic scan",
            "in-process scan", "calibration", "calibrate", "reset", "initialize",
            "adas calibration", "camera calibration", "radar calibration", "sensor calibration",
            "static calibration", "dynamic calibration", "target setup", "relearn",

            // Painting operations
            "clear coat", "clearcoat", "basecoat", "base coat", "single stage",
            "tri-coat", "tricoat", "pearl", "metallic", "three stage", "four stage",
            "color tint", "tint", "toner", "spray out", "touch up", "blend into",
            "edge to edge", "partial", "spot repair", "feather edge",

            // Cleanup/misc
            "hazardous waste", "disposal", "environmental", "shop supplies",
            "clean for delivery", "detail", "decontaminate", "clean area",
            "solvent wipe", "prep wipe", "tack cloth", "final prep",

            // Welding/structural
            "plug weld", "spot weld", "mig weld", "mig braze", "resistance weld",
            "weld prep", "grind weld", "dress weld", "seam weld",
            "structural adhesive", "panel bond", "rivet", "flow drill",

            // Glass/trim
            "prime glass", "urethane", "butyl tape", "dam tape",
            "molding tape", "emblem tape", "badge tape", "retape",

            // Mechanical
            "alignment check", "road test", "steering angle reset", "tpms reset",
            "battery disconnect", "battery reconnect", "reprogram", "flash",
            "module setup", "aim headlamp", "aim headlamps"
        };

        /// <summary>
        /// Parse a PDF file and extract estimate data
        /// </summary>
        public ParsedEstimate ParsePdf(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDF file not found", filePath);

            var bytes = File.ReadAllBytes(filePath);
            var estimate = ParsePdfFromBytes(bytes);
            estimate.SourceFile = Path.GetFileName(filePath);
            return estimate;
        }

        /// <summary>
        /// Parse a PDF from bytes (for use with StorageFile)
        /// </summary>
        public ParsedEstimate ParsePdfFromBytes(byte[] pdfBytes)
        {
            var estimate = new ParsedEstimate
            {
                ParsedDate = DateTime.Now
            };

            try
            {
                using var ms = new MemoryStream(pdfBytes);
                using var reader = new PdfReader(ms);
                using var pdfDoc = new PdfDocument(reader);

                var allText = new StringBuilder();
                int numberOfPages = pdfDoc.GetNumberOfPages();

                System.Diagnostics.Debug.WriteLine($"[PDF Parser] Processing {numberOfPages} pages");

                // Extract text from all pages using iText7
                for (int i = 1; i <= numberOfPages; i++)
                {
                    var page = pdfDoc.GetPage(i);
                    var strategy = new LocationTextExtractionStrategy();
                    var pageText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page, strategy);

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        allText.AppendLine(pageText);
                        estimate.RawText += pageText + "\n";
                    }
                }

                var text = allText.ToString();

                // Detect estimate source (CCC, Mitchell, Audatex)
                estimate.Source = DetectSource(text);
                System.Diagnostics.Debug.WriteLine($"[PDF Parser] Detected source: {estimate.Source}");

                // Extract vehicle info
                estimate.VehicleInfo = ExtractVehicleInfo(text);

                // Extract VIN
                estimate.VIN = ExtractVIN(text);

                // Extract line items based on source with SMART parsing
                estimate.LineItems = ExtractLineItemsSmart(text, estimate.Source);

                // Extract totals
                estimate.Totals = ExtractTotals(text);

                System.Diagnostics.Debug.WriteLine($"[PDF Parser] Parsed {estimate.LineItems.Count} line items from {estimate.Source} estimate");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PDF Parser] Error parsing PDF: {ex.Message}");
                estimate.ParseErrors.Add($"Error parsing PDF: {ex.Message}");
            }

            return estimate;
        }

        /// <summary>
        /// Parse text directly (for pasted content)
        /// </summary>
        public ParsedEstimate ParseText(string text)
        {
            var estimate = new ParsedEstimate
            {
                ParsedDate = DateTime.Now,
                RawText = text,
                Source = DetectSource(text)
            };

            estimate.VehicleInfo = ExtractVehicleInfo(text);
            estimate.VIN = ExtractVIN(text);
            estimate.LineItems = ExtractLineItemsSmart(text, estimate.Source);
            estimate.Totals = ExtractTotals(text);

            return estimate;
        }

        /// <summary>
        /// Detect which estimating system generated the PDF
        /// </summary>
        private string DetectSource(string text)
        {
            var upper = text.ToUpperInvariant();

            if (upper.Contains("CCC ONE") || upper.Contains("CCC INFORMATION") || upper.Contains("CCCIS"))
                return "CCC";
            if (upper.Contains("MITCHELL") || upper.Contains("ULTRAMATE"))
                return "Mitchell";
            if (upper.Contains("AUDATEX") || upper.Contains("SOLERA"))
                return "Audatex";
            if (upper.Contains("ESTIMATE") || upper.Contains("REPAIR ORDER"))
                return "Generic";

            return "Unknown";
        }

        /// <summary>
        /// Extract vehicle information (year, make, model)
        /// </summary>
        private string ExtractVehicleInfo(string text)
        {
            // Common patterns for vehicle info
            var patterns = new[]
            {
                @"(\d{4})\s+([\w-]+)\s+([\w-]+)",  // "2019 Honda Accord"
                @"Vehicle:\s*(.+?)(?:\r?\n|VIN)",
                @"Year/Make/Model:\s*(.+?)(?:\r?\n|$)",
                @"(\d{4})\s+(\w+)\s+(\w+)",
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var vehicle = match.Value.Trim();
                    // Clean up the result
                    vehicle = Regex.Replace(vehicle, @"(Vehicle:|Year/Make/Model:)", "", RegexOptions.IgnoreCase).Trim();
                    if (vehicle.Length > 5 && vehicle.Length < 100)
                        return vehicle;
                }
            }

            return "";
        }

        /// <summary>
        /// Extract VIN from estimate
        /// </summary>
        private string ExtractVIN(string text)
        {
            // VIN is always 17 characters, alphanumeric (no I, O, Q)
            var vinPattern = @"\b([A-HJ-NPR-Z0-9]{17})\b";
            var match = Regex.Match(text, vinPattern);
            if (match.Success)
                return match.Groups[1].Value;

            // Also try labeled VIN
            var labeledPattern = @"VIN[:\s]*([A-HJ-NPR-Z0-9]{17})";
            match = Regex.Match(text, labeledPattern, RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;

            return "";
        }

        /// <summary>
        /// SMART line item extraction - handles CCC, Mitchell, Audatex formats
        /// </summary>
        private List<PdfEstimateLineItem> ExtractLineItemsSmart(string text, string source)
        {
            var items = new List<PdfEstimateLineItem>();
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            string currentSection = "";
            PdfEstimateLineItem? currentParentPart = null;

            System.Diagnostics.Debug.WriteLine($"[PDF Parser] Smart parsing {lines.Length} lines for {source}");

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.Length < 3)
                    continue;

                // Skip headers/footers
                if (IsHeaderOrFooter(line))
                    continue;

                // Detect section headers (ELECTRICAL, FRONT DOOR, etc.)
                var sectionHeader = DetectSectionHeader(line);
                if (!string.IsNullOrEmpty(sectionHeader))
                {
                    currentSection = sectionHeader;
                    System.Diagnostics.Debug.WriteLine($"[PDF Parser] Section: {currentSection}");
                    continue;
                }

                // Parse based on source format
                PdfEstimateLineItem? item = source switch
                {
                    "CCC" => ParseCCCLineItem(line, currentSection),
                    "Mitchell" => ParseMitchellLineItem(line, currentSection),
                    "Audatex" => ParseAudatexLineItem(line, currentSection),
                    _ => ParseGenericLineItem(line, currentSection)
                };

                if (item != null && IsValidLineItem(item))
                {
                    // Normalize the part name
                    item.PartName = NormalizePartName(item.PartName);
                    item.Description = NormalizePartName(item.Description);

                    // Detect if this is an additional operation (manual line)
                    item.IsAdditionalOperation = IsAdditionalOperation(item);

                    // Link additional operations to parent parts
                    if (item.IsAdditionalOperation && currentParentPart != null)
                    {
                        item.ParentPartName = currentParentPart.PartName;
                        item.ParentLineNumber = currentParentPart.LineNumber;
                    }
                    else if (!item.IsAdditionalOperation && !string.IsNullOrEmpty(item.PartName))
                    {
                        // This is a main part - track it as current parent
                        currentParentPart = item;
                    }

                    // Debug output for tracking
                    if (item.IsManualMarker || item.IsAdditionalOperation)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PDF Parser] # LINE: {item.Description} | Manual: {item.IsManualMarker} | AddOp: {item.IsAdditionalOperation} | Parent: {item.ParentPartName ?? "NONE"} | L:{item.LaborHours} R:{item.RefinishHours} $:{item.Price}");
                    }

                    items.Add(item);
                }
            }

            // Post-process: link orphaned additional ops to nearest parent
            LinkOrphanedAdditionalOperations(items);

            int manualMarkerCount = items.Count(i => i.IsManualMarker);
            int additionalOpCount = items.Count(i => i.IsAdditionalOperation);
            int parentCount = items.Count(i => !i.IsAdditionalOperation && !string.IsNullOrEmpty(i.PartName));
            System.Diagnostics.Debug.WriteLine($"[PDF Parser] Extracted {items.Count} items: {parentCount} parents, {manualMarkerCount} # markers, {additionalOpCount} additional ops");

            return items;
        }

        /// <summary>
        /// Parse CCC ONE format line items
        /// CCC format: LineNum | #/* | Oper | Description | PartNum | Qty | Price | Labor | Paint
        ///
        /// IMPORTANT DISTINCTIONS:
        /// - # = Manual entry (additional operations like DE-NIB, Backtape, Cavity Wax)
        /// - * = Modified data (NOT manual - these are often parent parts with adjusted labor)
        /// - ** = Aftermarket part with modified data
        /// </summary>
        private PdfEstimateLineItem? ParseCCCLineItem(string line, string section)
        {
            var item = new PdfEstimateLineItem
            {
                RawLine = line,
                Section = section
            };

            // Split into tokens (handle multiple spaces/tabs as column separators)
            var tokens = Regex.Split(line, @"\s{2,}|\t").Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (tokens.Count < 2) return null;

            int idx = 0;

            // Check for line number at start
            if (int.TryParse(tokens[idx], out var lineNum))
            {
                item.LineNumber = lineNum;
                idx++;
            }

            if (idx >= tokens.Count) return null;

            // Check for marker column: # (manual), * (modified), ** (aftermarket modified)
            // CRITICAL: Only # means MANUAL entry. * and ** are modified database lines (often parent parts)
            if (tokens[idx] == "#")
            {
                item.IsManualMarker = true; // True manual entry
                idx++;
            }
            else if (tokens[idx] == "*" || tokens[idx] == "**")
            {
                // * or ** means MODIFIED data, NOT manual entry
                // These are typically parent parts with adjusted labor times
                item.IsManualMarker = false;
                idx++;
            }

            if (idx >= tokens.Count) return null;

            // Check for operation type
            var operationCodes = new[] { "Repl", "Rpr", "Refn", "R&I", "Blnd", "Algn", "Subl", "Add", "O/H", "New" };
            if (operationCodes.Any(op => tokens[idx].Equals(op, StringComparison.OrdinalIgnoreCase)))
            {
                item.OperationType = tokens[idx];
                idx++;
            }

            if (idx >= tokens.Count) return null;

            // Rest is description + numbers
            var descParts = new List<string>();
            for (int i = idx; i < tokens.Count; i++)
            {
                var token = tokens[i];

                // Part number detection (alphanumeric, 6+ chars, has both letters and digits)
                if (string.IsNullOrEmpty(item.PartNumber) && token.Length >= 6 &&
                    token.Any(char.IsDigit) && token.Any(char.IsLetter) && !token.Contains("."))
                {
                    item.PartNumber = token;
                    continue;
                }

                // Price detection (contains comma or large decimal)
                if (item.Price == 0 && TryParsePrice(token, out var price))
                {
                    item.Price = price;
                    continue;
                }

                // Hours detection (small decimal number)
                if (decimal.TryParse(token, out var hours) && hours > 0 && hours < 100)
                {
                    if (item.LaborHours == 0)
                        item.LaborHours = hours;
                    else if (item.RefinishHours == 0)
                        item.RefinishHours = hours;
                    continue;
                }

                // Quantity detection
                if (item.Quantity == 1 && int.TryParse(token, out var qty) && qty >= 1 && qty <= 20)
                {
                    item.Quantity = qty;
                    continue;
                }

                // Detect labor type suffix (M = Mechanical)
                if (token == "M" || token == "m")
                {
                    item.LaborType = "Mechanical";
                    continue;
                }

                // Skip common non-description tokens
                if (token.Equals("Incl", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Incl.", StringComparison.OrdinalIgnoreCase))
                    continue;

                descParts.Add(token);
            }

            item.Description = string.Join(" ", descParts);
            item.PartName = ExtractPartName(item.Description);

            return string.IsNullOrWhiteSpace(item.Description) ? null : item;
        }

        /// <summary>
        /// Parse Mitchell format line items
        /// Mitchell format: LineNum | LineCode | Description | Operation | Type | Total Units | CEG | Part Type | Part Number | Qty | Total Price | Tax
        /// LineCode: "AUTO" = system line, "900500" = manual/judgment entry
        /// </summary>
        private PdfEstimateLineItem? ParseMitchellLineItem(string line, string section)
        {
            var item = new PdfEstimateLineItem
            {
                RawLine = line,
                Section = section
            };

            // Split into tokens
            var tokens = Regex.Split(line, @"\s{2,}|\t").Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (tokens.Count < 3) return null;

            int idx = 0;

            // Check for line number at start
            if (int.TryParse(tokens[idx], out var lineNum))
            {
                item.LineNumber = lineNum;
                idx++;
            }

            if (idx >= tokens.Count) return null;

            // Check for Mitchell line code (AUTO = system, 900500 = manual entry)
            if (tokens[idx] == "AUTO" || tokens[idx] == "900500" || Regex.IsMatch(tokens[idx], @"^\d{6}$"))
            {
                if (tokens[idx] == "900500")
                {
                    item.IsManualMarker = true; // This is a manual/judgment line
                }
                idx++;
            }

            if (idx >= tokens.Count) return null;

            // Mitchell operations
            var mitchellOperations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Overhaul", "O/H" },
                { "Remove / Replace", "Replace" },
                { "Remove/ Replace", "Replace" },
                { "Remove /Replace", "Replace" },
                { "Remove/Replace", "Replace" },
                { "Remove / Install", "R&I" },
                { "Remove/ Install", "R&I" },
                { "Remove /Install", "R&I" },
                { "Remove/Install", "R&I" },
                { "Refinish Only", "Refinish" },
                { "RefinishOnly", "Refinish" },
                { "Repair", "Repair" },
                { "Check / Adjust", "Adjust" },
                { "Check/Adjust", "Adjust" },
                { "Additional Operation", "Add" },
                { "Additional Cost", "Cost" }
            };

            // Build description from remaining tokens
            var descParts = new List<string>();
            string? foundOperation = null;

            for (int i = idx; i < tokens.Count; i++)
            {
                var token = tokens[i];

                // Check for operation type
                if (foundOperation == null)
                {
                    foreach (var kvp in mitchellOperations)
                    {
                        if (token.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                            token.Replace(" ", "").Equals(kvp.Key.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                        {
                            foundOperation = kvp.Value;
                            item.OperationType = kvp.Value;
                            break;
                        }
                    }
                    if (foundOperation != null) continue;
                }

                // Labor type detection (Body, Refinish, Mechanical)
                if (token == "Body" || token == "Body*")
                {
                    item.LaborType = "Body";
                    continue;
                }
                if (token == "Refinish" || token == "Refinish*")
                {
                    item.LaborType = "Refinish";
                    continue;
                }
                if (token == "Mechanical" || token == "Mechanical*")
                {
                    item.LaborType = "Mechanical";
                    continue;
                }

                // Part type (New, Existing, Aftermarket, etc.)
                if (token == "New" || token == "Existing" || token == "Aftermarket" ||
                    token.Contains("Recycled") || token.Contains("OE Discount") || token.Contains("Qual"))
                {
                    continue;
                }

                // Part number detection
                if (string.IsNullOrEmpty(item.PartNumber) && token.Length >= 6 &&
                    token.Any(char.IsDigit) && !token.Contains("$") && !token.Contains("."))
                {
                    item.PartNumber = token;
                    continue;
                }

                // Price detection
                if (item.Price == 0 && TryParsePrice(token, out var price))
                {
                    item.Price = price;
                    continue;
                }

                // Hours detection (look for patterns like "3.1#", "2.8 C", "0.4")
                var cleanedToken = token.TrimEnd('#', 'C', 'r', '*');
                if (decimal.TryParse(cleanedToken, out var hours) && hours > 0 && hours < 100)
                {
                    if (item.LaborHours == 0)
                        item.LaborHours = hours;
                    else if (item.RefinishHours == 0)
                        item.RefinishHours = hours;
                    continue;
                }

                // CEG reference (like "2.9", "0.4")
                if (decimal.TryParse(token, out _) && token.Contains("."))
                    continue;

                // Skip common noise
                if (token == "Yes" || token == "No" || token == "INC" || token == "INC#" || token == "INCr")
                    continue;

                // Quantity
                if (item.Quantity == 1 && int.TryParse(token, out var qty) && qty >= 1 && qty <= 50)
                {
                    item.Quantity = qty;
                    continue;
                }

                descParts.Add(token);
            }

            item.Description = string.Join(" ", descParts);
            item.PartName = ExtractPartName(item.Description);

            // Mark 900500 lines as manual entries (judgment items)
            if (item.IsManualMarker || line.Contains("*"))
            {
                item.IsManualMarker = true;
            }

            return string.IsNullOrWhiteSpace(item.Description) ? null : item;
        }

        /// <summary>
        /// Parse Audatex format line items
        /// </summary>
        private PdfEstimateLineItem? ParseAudatexLineItem(string line, string section)
        {
            // Audatex format
            return ParseGenericLineItem(line, section);
        }

        /// <summary>
        /// Generic line item parser for unknown formats
        /// </summary>
        private PdfEstimateLineItem? ParseGenericLineItem(string line, string section)
        {
            var item = new PdfEstimateLineItem
            {
                RawLine = line,
                Section = section
            };

            // Extract operation type
            item.OperationType = ExtractOperationType(line);

            // Extract part name
            item.PartName = ExtractPartName(line);

            // Extract hours
            ExtractHours(line, item);

            // Extract price
            item.Price = ExtractPrice(line);

            // Extract part number
            item.PartNumber = ExtractPartNumber(line);

            // Set description
            item.Description = CleanDescription(line, item);

            return item;
        }

        /// <summary>
        /// Try to parse a token as a price
        /// </summary>
        private bool TryParsePrice(string token, out decimal price)
        {
            price = 0;
            // Remove $ and commas
            var cleaned = token.Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(cleaned, out var val))
            {
                // Prices are typically > $10 and have 2 decimal places
                if (val > 10 && (token.Contains(".") || token.Contains(",")))
                {
                    price = val;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Normalize part name by expanding abbreviations
        /// </summary>
        public static string NormalizePartName(string partName)
        {
            if (string.IsNullOrWhiteSpace(partName))
                return partName;

            var normalized = partName;

            // Expand abbreviations
            foreach (var kvp in _partNameNormalizations)
            {
                // Use word boundary matching to avoid partial replacements
                var pattern = $@"\b{Regex.Escape(kvp.Key)}\b";
                normalized = Regex.Replace(normalized, pattern, kvp.Value, RegexOptions.IgnoreCase);
            }

            // Clean up extra spaces
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        /// <summary>
        /// Check if this line is an additional operation (not a main part)
        ///
        /// CCC Manual Line Logic:
        /// - # marker = ALWAYS an additional operation (manual entry)
        /// - * marker = modified data, NOT manual (often parent parts with adjusted labor)
        /// - No marker + Repl/Blnd + part name = parent part
        /// - No marker + Refn + additional keyword = additional operation
        /// </summary>
        private bool IsAdditionalOperation(PdfEstimateLineItem item)
        {
            // Has manual marker (#) = ALWAYS an additional operation
            if (item.IsManualMarker)
                return true;

            // Has significant price or part number = main part (not additional)
            if (item.Price > 50 || !string.IsNullOrEmpty(item.PartNumber))
                return false;

            // Major operations (Repl, Blnd) are typically parent parts, not additional ops
            // Even if they don't have a price (like "Blnd LT Fender" with just labor hours)
            if (item.OperationType == "Repl" || item.OperationType == "Blnd")
            {
                // But check if description suggests it's actually an additional op
                var desc = item.Description?.ToLowerInvariant() ?? "";
                if (!_additionalOperationKeywords.Any(k => desc.Contains(k.ToLowerInvariant())))
                    return false; // It's a parent part (Replace/Blend without additional op keywords)
            }

            // Check for additional operation keywords in description
            var descLower = item.Description?.ToLowerInvariant() ?? "";
            foreach (var keyword in _additionalOperationKeywords)
            {
                if (descLower.Contains(keyword.ToLowerInvariant()))
                    return true;
            }

            // Refinish operations (Refn) with low price and no part number are usually additional
            // But only if they have additional op keywords or are clearly not a panel refinish
            if (item.OperationType == "Refn" && item.Price < 10)
            {
                // Check if this looks like a panel refinish (parent) or additional refinish op
                var panelKeywords = new[] { "fender", "door", "bumper", "hood", "roof", "quarter", "panel" };
                bool hasPanelKeyword = panelKeywords.Any(p => descLower.Contains(p));
                bool hasAdditionalKeyword = _additionalOperationKeywords.Any(k => descLower.Contains(k.ToLowerInvariant()));

                // If it mentions a panel but ALSO has additional keywords (like "Fender DE-NIB"), it's additional
                if (hasAdditionalKeyword)
                    return true;

                // If it just mentions a panel without additional keywords, it might be a standalone refinish
                // which could be a parent operation
                if (hasPanelKeyword && !hasAdditionalKeyword)
                    return false;
            }

            // R&I operations are usually not additional ops (they're standalone or parent)
            if (item.OperationType == "R&I")
                return false;

            // Repair operations without # marker are often parent operations
            if (item.OperationType == "Rpr" && !item.IsManualMarker)
            {
                // Unless they have additional op keywords
                if (!_additionalOperationKeywords.Any(k => descLower.Contains(k.ToLowerInvariant())))
                    return false;
            }

            return false;
        }

        /// <summary>
        /// Link orphaned additional operations to nearest parent part
        /// Uses smart matching to link based on part name mentioned in the description
        /// e.g., "LT Fender DE-NIB" → parent "LT Fender w/F-Sport"
        /// </summary>
        private void LinkOrphanedAdditionalOperations(List<PdfEstimateLineItem> items)
        {
            // Build a list of all parent parts for matching
            var parentParts = items
                .Where(i => !i.IsAdditionalOperation && !string.IsNullOrEmpty(i.PartName))
                .ToList();

            PdfEstimateLineItem? lastParent = null;

            foreach (var item in items)
            {
                if (!item.IsAdditionalOperation && !string.IsNullOrEmpty(item.PartName))
                {
                    lastParent = item;
                }
                else if (item.IsAdditionalOperation && string.IsNullOrEmpty(item.ParentPartName))
                {
                    // Try to find the best matching parent based on description
                    var matchedParent = FindBestMatchingParent(item, parentParts, lastParent);
                    if (matchedParent != null)
                    {
                        item.ParentPartName = matchedParent.PartName;
                        item.ParentLineNumber = matchedParent.LineNumber;
                    }
                }
            }
        }

        /// <summary>
        /// Find the best matching parent part for an additional operation
        /// Looks for part name matches in the description (e.g., "LT Door shell DE-NIB" → "LT Door shell")
        /// </summary>
        private PdfEstimateLineItem? FindBestMatchingParent(
            PdfEstimateLineItem additionalOp,
            List<PdfEstimateLineItem> parentParts,
            PdfEstimateLineItem? lastParent)
        {
            var descLower = additionalOp.Description?.ToLowerInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(descLower))
                return lastParent;

            // Score each parent part based on how well it matches the additional op description
            PdfEstimateLineItem? bestMatch = null;
            int bestScore = 0;

            foreach (var parent in parentParts)
            {
                var parentPartLower = parent.PartName?.ToLowerInvariant() ?? "";
                var parentDescLower = parent.Description?.ToLowerInvariant() ?? "";

                if (string.IsNullOrWhiteSpace(parentPartLower))
                    continue;

                int score = 0;

                // Check if the parent part name appears in the additional op description
                // e.g., "lt fender de-nib" contains "fender"
                if (descLower.Contains(parentPartLower))
                {
                    score += 100; // Strong match
                }

                // Check for partial matches (e.g., "door shell" in "lt door shell de-nib")
                var parentWords = parentPartLower.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                var matchedWords = parentWords.Count(w => w.Length > 2 && descLower.Contains(w));
                score += matchedWords * 20;

                // Check position indicators (LT, RT, Front, Rear)
                var positionIndicators = new[] { "lt", "rt", "left", "right", "lf", "rf", "lr", "rr", "front", "rear" };
                foreach (var pos in positionIndicators)
                {
                    bool parentHasPos = parentPartLower.Contains(pos) || parentDescLower.Contains(pos);
                    bool opHasPos = descLower.Contains(pos);

                    if (parentHasPos && opHasPos)
                        score += 30; // Same side/position
                    else if (parentHasPos != opHasPos)
                        score -= 20; // Different sides (penalty)
                }

                // Bonus for being close to this line (recency)
                if (parent == lastParent)
                    score += 10;

                // Bonus for same section
                if (!string.IsNullOrEmpty(parent.Section) &&
                    parent.Section.Equals(additionalOp.Section, StringComparison.OrdinalIgnoreCase))
                    score += 15;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = parent;
                }
            }

            // If we found a good match (score > threshold), use it; otherwise fall back to last parent
            if (bestScore >= 20)
                return bestMatch;

            return lastParent;
        }

        /// <summary>
        /// Extract line items from the estimate text (legacy method)
        /// </summary>
        private List<PdfEstimateLineItem> ExtractLineItems(string text, string source)
        {
            return ExtractLineItemsSmart(text, source);
        }

        /// <summary>
        /// Detect if a line is a section header
        /// </summary>
        // CCC section headers (ALL CAPS in the PDF)
        private static readonly HashSet<string> _cccSectionHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            // Bumper/Front sections
            "FRONT BUMPER", "FRONT BUMPER & GRILLE", "FRONT BUMPER AND GRILLE",
            "REAR BUMPER", "REAR BUMPER & GRILLE", "REAR BUMPER AND GRILLE",
            // Lamps
            "FRONT LAMPS", "REAR LAMPS", "LAMPS", "HEADLAMPS", "TAILLAMPS",
            // Structure
            "RADIATOR SUPPORT", "FRONT INNER STRUCTURE", "REAR INNER STRUCTURE",
            "FRAME", "UNIBODY", "STRUCTURAL",
            // Body panels
            "FENDER", "FRONT FENDER", "REAR FENDER",
            "HOOD", "TRUNK", "DECKLID", "LIFTGATE", "TAILGATE",
            "ROOF", "ROOF PANEL", "SUNROOF",
            // Doors
            "FRONT DOOR", "REAR DOOR", "DOOR", "SLIDING DOOR",
            // Pillars/Rockers
            "PILLARS, ROCKER & FLOOR", "PILLARS ROCKER AND FLOOR",
            "ROCKER", "ROCKER PANEL", "PILLARS", "A PILLAR", "B PILLAR", "C PILLAR",
            // Glass
            "WINDSHIELD", "GLASS", "REAR GLASS", "SIDE GLASS",
            // Side panels
            "SIDE PANEL", "QUARTER PANEL", "QUARTER",
            // Electrical/Electronic
            "ELECTRICAL", "ELECTRONICS", "WIRING",
            // Interior
            "INSTRUMENT PANEL", "DASH", "DASHBOARD", "INTERIOR",
            // Safety/Restraints
            "RESTRAINT SYSTEMS", "RESTRAINTS", "AIR BAG", "AIRBAG", "SRS",
            // Suspension/Mechanical
            "SUSPENSION", "STEERING", "BRAKES", "DRIVETRAIN",
            "WHEELS", "TIRES", "ALIGNMENT",
            // Cooling/Engine
            "COOLING", "ENGINE", "EXHAUST", "A/C", "AIR CONDITIONING",
            // Diagnostics (CCC specific)
            "VEHICLE DIAGNOSTICS", "DIAGNOSTICS", "SCANNING", "CALIBRATION",
            // Misc
            "MISCELLANEOUS OPERATIONS", "MISCELLANEOUS", "ADDITIONAL OPERATIONS",
            // Cargo/Loading (for vans/trucks)
            "REAR BODY & FLOOR", "REAR LOADING DOOR", "CARGO",
            // Bed (trucks)
            "BED", "TRUCK BED", "BED SIDES"
        };

        // Mitchell section headers (Title Case)
        private static readonly HashSet<string> _mitchellSectionHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Front Bumper", "Rear Bumper", "Grille", "Front Lamps", "Rear Lamps",
            "Hood", "Front Fender", "Rear Fender", "Front Door", "Rear Door",
            "Quarter Panel", "Roof", "Trunk", "Deck Lid", "Liftgate", "Tailgate",
            "Front Inner Structure", "Rear Inner Structure", "Cowl & Dash",
            "Windshield", "Rear Glass", "Side Glass",
            "Suspension", "Steering", "Brakes", "Wheels & Tires",
            "Cooling System", "Engine", "Exhaust", "A/C System",
            "Electrical", "Air Bags", "Interior",
            "Additional Costs & Materials", "Additional Operations",
            "Special / Manual Entry"
        };

        private string DetectSectionHeader(string line)
        {
            var trimmed = line.Trim();
            var upper = trimmed.ToUpperInvariant();

            // Check if this is a pure section header line (no numbers, short length)
            // CCC headers are typically ALL CAPS, no digits, and standalone
            if (trimmed.Length >= 3 && trimmed.Length <= 50)
            {
                // Check exact match in CCC headers
                if (_cccSectionHeaders.Contains(trimmed))
                    return trimmed;

                // Check exact match in Mitchell headers
                if (_mitchellSectionHeaders.Contains(trimmed))
                    return trimmed;

                // Check if line IS the section header (all caps, no digits except maybe line number prefix)
                var withoutLineNum = Regex.Replace(trimmed, @"^\d+\s+", "").Trim();
                if (_cccSectionHeaders.Contains(withoutLineNum))
                    return withoutLineNum;

                // Check for section header patterns (CCC: number + section name)
                var sectionMatch = Regex.Match(trimmed, @"^(\d+)\s+([A-Z][A-Z\s&,]+)$");
                if (sectionMatch.Success)
                {
                    var potentialSection = sectionMatch.Groups[2].Value.Trim();
                    if (_cccSectionHeaders.Contains(potentialSection))
                        return potentialSection;
                }
            }

            // Legacy fallback detection for partial matches
            if (upper.Contains("LABOR OPERATIONS") || upper.Contains("LABOR OPS"))
                return "Labor";
            if (upper.Contains("PARTS") && (upper.Contains("LIST") || upper.Contains("SECTION")))
                return "Parts";
            if (upper.Contains("BODY") && upper.Contains("OPERATION"))
                return "Body Operations";
            if (upper.Contains("SUBLET") || upper.Contains("OUTSIDE"))
                return "Sublet";
            if (upper.Contains("SUPPLEMENT"))
                return "Supplement";

            return "";
        }

        /// <summary>
        /// Parse a single line into an estimate line item
        /// </summary>
        private PdfEstimateLineItem? ParseLineItem(string line, string section, string source)
        {
            var item = new PdfEstimateLineItem
            {
                RawLine = line,
                Section = section
            };

            // Skip obvious non-items
            if (IsHeaderOrFooter(line))
                return null;

            // Extract operation type
            item.OperationType = ExtractOperationType(line);

            // Extract part name
            item.PartName = ExtractPartName(line);

            // Extract hours
            ExtractHours(line, item);

            // Extract price/amount
            item.Price = ExtractPrice(line);

            // Extract part number if present
            item.PartNumber = ExtractPartNumber(line);

            // Set description
            item.Description = CleanDescription(line, item);

            return item;
        }

        /// <summary>
        /// Check if line is a header, footer, or non-item line
        /// Aggressively filters out shop info, boilerplate, disclaimers, etc.
        /// </summary>
        public static bool IsHeaderOrFooter(string line)
        {
            var trimmed = line.Trim();
            var upper = trimmed.ToUpperInvariant();

            // Skip very short or very long lines (unlikely to be estimate items)
            if (trimmed.Length < 5 || trimmed.Length > 300)
                return true;

            // === SHOP/BUSINESS INFO ===
            // Shop names, addresses, phone numbers, IDs
            var shopPatterns = new[]
            {
                "COLLISION REPAIR", "COLLISION CENTER", "AUTO BODY", "BODY SHOP",
                "ULTIMATE COLLISION", "OEM FACTORY REPAIRS", "THE RIGHT WAY",
                "HIGHWAY", "STREET", "AVENUE", "ROAD", "BLVD", "DRIVE", "LANE", // Address
                "PHONE:", "FAX:", "OFFICE:", "BUSINESS", "MOBILE", "EVENING",
                "WORKFILE ID", "FEDERAL ID", "STATE ID", "RESALE NUMBER",
                "FEDERAL EPA", "STATE EPA", "LICENSE NUMBER",
                "@WORLDNET", "@GMAIL", "@YAHOO", "@AOL", "@OUTLOOK", ".COM", ".NET", // Email
                "WWW.", "HTTP", "CARWISE.COM", // URLs
                // Dealership/shop mechanical rate lines
                "MECHANICAL RATE", "LEXUS MECHANICAL", "MERCEDES MECHANICAL", "TOYOTA MECHANICAL",
                "BMW MECHANICAL", "HONDA MECHANICAL", "AUDI MECHANICAL", "DEALER RATE",
                "PRESTIGE LEXUS", "RAY CATENA", "DCH TOYOTA", "MALOUF GM",
            };

            // === CUSTOMER/CLAIM INFO ===
            var customerPatterns = new[]
            {
                "CUSTOMER:", "JOB NUMBER:", "WRITTEN BY:", "INSURED:", "POLICY #:",
                "CLAIM #:", "CLAIM NUMBER", "DATE OF LOSS:", "DAYS TO REPAIR:",
                "POINT OF IMPACT:", "TYPE OF LOSS:",
                "OWNER:", "INSPECTION LOCATION:", "INSURANCE COMPANY:",
                "PRELIMINARY ESTIMATE", "ORIGINAL ESTIMATE", "SUPPLEMENT",
                "APPRAISER", "ADJUSTER", "CLASSIFICATION",
                "LOSS TYPE", "DEDUCTIBLE", "NOT WAIVED",
            };

            // === VEHICLE FEATURE LISTS (not line items) ===
            // These are the vehicle options section, not repair items
            var vehicleFeaturePatterns = new[]
            {
                "AUTOMATIC TRANSMISSION", "MANUAL TRANSMISSION", "4 WHEEL DRIVE", "ALL WHEEL DRIVE",
                "POWER STEERING", "POWER BRAKES", "POWER WINDOWS", "POWER LOCKS", "POWER MIRRORS",
                "POWER DRIVER SEAT", "POWER PASSENGER SEAT", "HEATED MIRRORS", "HEATED SEATS",
                "DUAL MIRRORS", "TINTED GLASS", "CONSOLE/STORAGE", "OVERHEAD CONSOLE",
                "AIR CONDITIONING", "CLIMATE CONTROL", "INTERMITTENT WIPERS", "TILT WHEEL",
                "CRUISE CONTROL", "REAR DEFOGGER", "KEYLESS ENTRY", "ALARM", "MESSAGE CENTER",
                "STEERING WHEEL TOUCH", "TELESCOPIC WHEEL", "BACKUP CAMERA", "INTELLIGENT CRUISE",
                "HOME LINK", "AM RADIO", "FM RADIO", "STEREO", "SEARCH/SEEK", "CD PLAYER",
                "AUXILIARY AUDIO", "SATELLITE RADIO", "MP3 PLAYER", "BLUETOOTH",
                "DRIVERS SIDE AIR BAG", "PASSENGER AIR BAG", "ANTI-LOCK BRAKES", "DISC BRAKES",
                "FRONT SIDE IMPACT", "HEAD/CURTAIN AIR BAG", "REAR SIDE IMPACT",
                "COMMUNICATIONS SYSTEM", "HANDS FREE DEVICE", "BLIND SPOT DETECTION",
                "LANE DEPARTURE WARNING", "TELEMATIC SYSTEMS", "THEFT DETERRENT",
                "BUCKET SEATS", "BENCH SEAT", "CLOTH SEAT", "LEATHER SEAT", "VENTILATED SEATS",
                "ALUMINUM/ALLOY WHEELS", "STYLED STEEL WHEELS", "CHROME WHEELS",
                "METALLIC PAINT", "THREE STAGE PAINT", "TWO STAGE PAINT", "PEARL PAINT",
                "FOG LAMPS", "TRACTION CONTROL", "STABILITY CONTROL", "SIGNAL INTEGRATED",
                "XENON", "L.E.D. HEADLAMPS", "LED HEADLAMPS", "ELECTRIC GLASS SUNROOF",
                "TIRE PRESSURE MONITORING", "TRIP COMPUTER", "DAYTIME RUNNING",
            };

            // === COLUMN HEADERS ===
            var columnHeaders = new[]
            {
                "LINE OPER DESCRIPTION", "PART NUMBER QTY", "EXTENDED PRICE",
                "LABOR PAINT", "TOTAL UNITS", "CEG TYPE",
                "LINE #", "LINE#", "OPERATION TYPE", "QTY TOTAL PRICE",
            };

            // === TOTALS/SUMMARY ===
            var totalPatterns = new[]
            {
                "SUBTOTAL", "GRAND TOTAL", "ESTIMATE TOTAL", "NET ESTIMATE",
                "PARTS TOTAL", "LABOR TOTAL", "PAINT SUPPLIES", "BODY SUPPLIES", "SHOP MATERIALS",
                "SALES TAX", "TAXABLE", "NON-TAXABLE", "PRE-TAX DISCOUNT",
                "BODY LABOR", "PAINT LABOR", "MECHANICAL LABOR", "REFINISH LABOR",
                "GROSS TOTAL", "ADJUSTMENTS", "CUSTOMER RESPONSIBILITY",
                "CATEGORY BASIS RATE COST",
                // Rate column headers
                "PRICE $", "RATE $", "COST $", "AMOUNT $", "TOTAL $",
                "LABOR RATE", "PAINT RATE", "MATERIAL RATE", "FRAME RATE",
            };

            // === LEGAL/BOILERPLATE ===
            var legalPatterns = new[]
            {
                "STATEMENT OF ACTUAL REPAIRS", "ANY PERSON WHO KNOWINGLY",
                "THIS ESTIMATE HAS BEEN PREPARED", "PARTS USED IN THE REPAIR",
                "ORIGINAL MANUFACTURER", "LIKE, KIND AND QUALITY",
                "SUBJECT TO CRIMINAL", "CIVIL PENALTIES", "FALSE OR MISLEADING",
                "COPYRIGHT", "PROPRIETARY", "CONFIDENTIAL", "ALL RIGHTS RESERVED",
                "CCC ONE ESTIMATING", "CCC INTELLIGENT SOLUTIONS", "CCCIS",
                "MITCHELL CLOUD ESTIMATING", "MITCHELL INTERNATIONAL",
                "MOTOR CRASH ESTIMATING GUIDE", "MOTOR SUGGESTED",
                "SYMBOLS FOLLOWING", "OTHER SYMBOLS AND ABBREVIATIONS",
                "JUDGMENT ITEM", "LABOR NOTE APPLIES", "DISCONTINUED BY MANUFACTURER",
                "VERIFY THE PART NUMBER", "PARTS VENDORS", "RECYCLED PART VENDORS",
                "SUPPLIER NOTES", "DISCLAIMER",
                "COMMITTED ON", "PRINTED ON", "PROFILE VERSION", "ESTIMATE EVENT LOG",
                "JOB CREATED", "ESTIMATE STARTED", "ESTIMATE PRINTED", "ESTIMATE COMMITTED",
                "ESTIMATE RETRIEVAL ID",
            };

            // === PAGE INFO ===
            var pagePatterns = new[]
            {
                "PAGE ", "PAGE:", "OF 6", "OF 7", "OF 8", "OF 5", "OF 4", "OF 3",
                "GET LIVE UPDATES AT",
            };

            // Check all patterns
            if (shopPatterns.Any(p => upper.Contains(p))) return true;
            if (customerPatterns.Any(p => upper.Contains(p))) return true;
            if (vehicleFeaturePatterns.Any(p => upper.Contains(p))) return true;
            if (columnHeaders.Any(p => upper.Contains(p))) return true;
            if (totalPatterns.Any(p => upper.Contains(p))) return true;
            if (legalPatterns.Any(p => upper.Contains(p))) return true;
            if (pagePatterns.Any(p => upper.Contains(p))) return true;

            // === SPECIFIC FORMAT CHECKS ===

            // Phone number pattern: (xxx) xxx-xxxx
            if (Regex.IsMatch(trimmed, @"\(\d{3}\)\s*\d{3}-\d{4}"))
                return true;

            // Date pattern at start: MM/DD/YYYY
            if (Regex.IsMatch(trimmed, @"^\d{1,2}/\d{1,2}/\d{4}"))
                return true;

            // Just a date and time (footer)
            if (Regex.IsMatch(trimmed, @"^\d{1,2}/\d{1,2}/\d{4}\s+\d{1,2}:\d{2}"))
                return true;

            // Standalone numbers (page numbers, IDs)
            if (Regex.IsMatch(trimmed, @"^\d{5,}$"))
                return true;

            // Lines that are just section labels with colons (ID fields)
            if (Regex.IsMatch(trimmed, @"^[A-Za-z\s]+:\s*$"))
                return true;

            // VIN line by itself
            if (upper.StartsWith("VIN:") || upper.StartsWith("VIN ") || Regex.IsMatch(upper, @"^VIN\s+[A-HJ-NPR-Z0-9]{17}"))
                return true;

            // License plate line
            if (upper.StartsWith("LICENSE:") || upper.StartsWith("LICENSE "))
                return true;

            // Lines starting with Price/Rate (rate headers, not parts)
            if (upper.StartsWith("PRICE") || upper.StartsWith("RATE") || upper.StartsWith("COST"))
                return true;

            // Lines that end with $ followed by digits (summary/total lines)
            if (Regex.IsMatch(trimmed, @"\$\s*$") || Regex.IsMatch(trimmed, @"\$\d"))
                return true;

            // Production date, mileage, condition lines
            if (upper.StartsWith("PRODUCTION DATE") || upper.StartsWith("MILEAGE") ||
                upper.StartsWith("CONDITION:") || upper.StartsWith("EXTERIOR COLOR") ||
                upper.StartsWith("INTERIOR COLOR"))
                return true;

            // State abbreviations with dash (NJ-X32SUH license plates)
            if (Regex.IsMatch(trimmed, @"^[A-Z]{2}-[A-Z0-9]+$"))
                return true;

            // Lines that are ALL CAPS and short (likely headers, not items)
            // But exclude section headers which we want to detect separately
            if (trimmed.Length < 30 && trimmed == upper && !trimmed.Any(char.IsDigit) &&
                !_cccSectionHeaders.Contains(trimmed))
            {
                // Check if it's a standalone word like "TRANSMISSION", "POWER", etc.
                var singleWords = new[] { "TRANSMISSION", "POWER", "DECOR", "CONVENIENCE",
                    "RADIO", "SAFETY", "SEATS", "WHEELS", "PAINT", "OTHER", "ROOF", "VEHICLE",
                    "OPTIONS", "FEATURES", "EQUIPMENT" };
                if (singleWords.Contains(trimmed))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Extract operation type from line
        /// </summary>
        private string ExtractOperationType(string line)
        {
            var upper = line.ToUpperInvariant();

            // Check for explicit operation codes first
            if (Regex.IsMatch(line, @"\bREPL\b|\bREPLACE\b", RegexOptions.IgnoreCase))
                return "Replace";
            if (Regex.IsMatch(line, @"\bRPR\b|\bREPAIR\b", RegexOptions.IgnoreCase))
                return "Repair";
            if (Regex.IsMatch(line, @"\bR\s*[&+]\s*I\b|\bREMOVE\s*(AND|&)?\s*INSTALL\b|\bR/I\b", RegexOptions.IgnoreCase))
                return "R&I";
            if (Regex.IsMatch(line, @"\bREF\b|\bRFN\b|\bREFINISH\b", RegexOptions.IgnoreCase))
                return "Refinish";
            if (Regex.IsMatch(line, @"\bBLEND\b|\bBLND\b", RegexOptions.IgnoreCase))
                return "Blend";
            if (Regex.IsMatch(line, @"\bO/H\b|\bOVERHAUL\b|\bOH\b", RegexOptions.IgnoreCase))
                return "Overhaul";
            if (Regex.IsMatch(line, @"\bALIGN\b|\bALIGNMENT\b", RegexOptions.IgnoreCase))
                return "Alignment";
            if (Regex.IsMatch(line, @"\bSUBLET\b", RegexOptions.IgnoreCase))
                return "Sublet";

            return "";
        }

        /// <summary>
        /// Extract part name from line
        /// </summary>
        private string ExtractPartName(string line)
        {
            // Known part keywords to look for
            var partKeywords = new[]
            {
                "bumper cover", "bumper fascia", "bumper reinforcement", "bumper absorber", "bumper bracket",
                "fender liner", "fender flare", "fender", "inner fender", "apron",
                "hood hinge", "hood latch", "hood strut", "hood insulator", "hood",
                "door shell", "door skin", "door handle", "door mirror", "door glass", "door trim", "door",
                "quarter panel", "quarter glass", "quarter",
                "roof rail", "roof panel", "sunroof", "moonroof", "headliner", "roof",
                "trunk lid", "decklid", "trunk floor", "liftgate", "tailgate", "trunk",
                "grille", "radiator grille", "lower grille", "upper grille",
                "headlight", "headlamp", "head light", "fog light", "fog lamp",
                "taillight", "taillamp", "tail light", "brake light", "stop light",
                "mirror", "side mirror", "outside mirror",
                "windshield", "front glass", "back glass", "rear glass", "side glass",
                "a-pillar", "b-pillar", "c-pillar", "d-pillar", "pillar",
                "rocker panel", "rocker", "side sill", "sill plate",
                "radiator support", "core support", "radiator", "condenser",
                "frame rail", "subframe", "frame", "unibody",
                "control arm", "ball joint", "tie rod", "strut", "shock", "spring",
                "wheel", "rim", "tire", "hub", "bearing",
                "airbag", "air bag", "srs", "seatbelt", "seat belt", "pretensioner",
                "sensor", "camera", "radar", "module", "ecu", "bcm"
            };

            var lower = line.ToLowerInvariant();

            // Find the longest matching part
            string? bestMatch = null;
            foreach (var part in partKeywords)
            {
                if (lower.Contains(part))
                {
                    if (bestMatch == null || part.Length > bestMatch.Length)
                        bestMatch = part;
                }
            }

            return bestMatch ?? "";
        }

        /// <summary>
        /// Extract labor and refinish hours from line
        /// </summary>
        private void ExtractHours(string line, PdfEstimateLineItem item)
        {
            // Common patterns for hours
            // "2.5 hrs" or "2.5 labor" or just decimal followed by context
            var laborMatch = Regex.Match(line, @"(\d+\.?\d*)\s*(hrs?|hours?|labor|lab)", RegexOptions.IgnoreCase);
            if (laborMatch.Success && decimal.TryParse(laborMatch.Groups[1].Value, out var laborHours))
            {
                item.LaborHours = laborHours;
            }

            // Refinish hours
            var refinishMatch = Regex.Match(line, @"(\d+\.?\d*)\s*(ref|rfn|refinish|paint)", RegexOptions.IgnoreCase);
            if (refinishMatch.Success && decimal.TryParse(refinishMatch.Groups[1].Value, out var refHours))
            {
                item.RefinishHours = refHours;
            }

            // CCC format often has hours as standalone decimals in columns
            // Look for pattern like "1.5" that could be hours
            if (item.LaborHours == 0)
            {
                var decimalMatches = Regex.Matches(line, @"\b(\d+\.\d{1,2})\b");
                foreach (Match match in decimalMatches)
                {
                    if (decimal.TryParse(match.Groups[1].Value, out var val))
                    {
                        // Hours are typically between 0.1 and 50
                        if (val >= 0.1m && val <= 50m && item.LaborHours == 0)
                        {
                            // Check if this looks like hours (not a price)
                            var pos = match.Index;
                            var beforeMatch = pos > 0 ? line.Substring(Math.Max(0, pos - 5), Math.Min(5, pos)) : "";
                            if (!beforeMatch.Contains("$"))
                            {
                                item.LaborHours = val;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Extract price/amount from line
        /// </summary>
        private decimal ExtractPrice(string line)
        {
            // Look for dollar amounts
            var priceMatch = Regex.Match(line, @"\$\s*([\d,]+\.?\d*)");
            if (priceMatch.Success)
            {
                var priceStr = priceMatch.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(priceStr, out var price))
                    return price;
            }

            // Also look for amounts without $ that are clearly prices (large numbers)
            var amountMatch = Regex.Match(line, @"\b([\d,]+\.\d{2})\b");
            if (amountMatch.Success)
            {
                var amountStr = amountMatch.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(amountStr, out var amount) && amount > 10)
                    return amount;
            }

            return 0;
        }

        /// <summary>
        /// Extract part number from line
        /// </summary>
        private string ExtractPartNumber(string line)
        {
            // Part numbers are typically alphanumeric, 5-15 chars
            var partNumMatch = Regex.Match(line, @"\b([A-Z0-9]{5,15})\b");
            if (partNumMatch.Success)
            {
                var partNum = partNumMatch.Groups[1].Value;
                // Exclude things that look like VINs or common codes
                if (partNum.Length != 17 && !Regex.IsMatch(partNum, @"^\d+$"))
                    return partNum;
            }
            return "";
        }

        /// <summary>
        /// Clean up the description by removing extracted data
        /// </summary>
        private string CleanDescription(string line, PdfEstimateLineItem item)
        {
            var desc = line;

            // Remove prices
            desc = Regex.Replace(desc, @"\$[\d,]+\.?\d*", "");

            // Remove standalone numbers that are likely hours or quantities
            desc = Regex.Replace(desc, @"\b\d+\.?\d*\s*(hrs?|hours?|labor|ref|rfn)\b", "", RegexOptions.IgnoreCase);

            // Remove part numbers
            if (!string.IsNullOrEmpty(item.PartNumber))
                desc = desc.Replace(item.PartNumber, "");

            // Clean up extra whitespace
            desc = Regex.Replace(desc, @"\s+", " ").Trim();

            return desc;
        }

        /// <summary>
        /// Check if a parsed line item is valid (has enough data)
        /// </summary>
        private bool IsValidLineItem(PdfEstimateLineItem item)
        {
            // Must have at least a part name or meaningful description
            if (string.IsNullOrEmpty(item.PartName) && item.Description.Length < 5)
                return false;

            // Must have either hours or price
            if (item.LaborHours == 0 && item.RefinishHours == 0 && item.Price == 0)
                return false;

            return true;
        }

        /// <summary>
        /// Merge multi-line items that might have been split
        /// </summary>
        private List<PdfEstimateLineItem> MergeMultiLineItems(List<PdfEstimateLineItem> items)
        {
            // For now, return as-is. Could add logic to merge continuation lines.
            return items;
        }

        /// <summary>
        /// Extract totals from the estimate
        /// </summary>
        private EstimateTotals ExtractTotals(string text)
        {
            var totals = new EstimateTotals();

            // Look for labeled totals
            var patterns = new Dictionary<string, Action<decimal>>
            {
                { @"Labor\s*(?:Total)?[:\s]*\$?\s*([\d,]+\.?\d*)", val => totals.LaborTotal = val },
                { @"Parts?\s*(?:Total)?[:\s]*\$?\s*([\d,]+\.?\d*)", val => totals.PartsTotal = val },
                { @"Paint\s*(?:Material)?[:\s]*\$?\s*([\d,]+\.?\d*)", val => totals.PaintMaterial = val },
                { @"Refinish\s*(?:Total)?[:\s]*\$?\s*([\d,]+\.?\d*)", val => totals.RefinishTotal = val },
                { @"(?:Sub)?Total[:\s]*\$?\s*([\d,]+\.?\d*)", val => totals.Subtotal = val },
                { @"Tax[:\s]*\$?\s*([\d,]+\.?\d*)", val => totals.Tax = val },
                { @"Grand\s*Total[:\s]*\$?\s*([\d,]+\.?\d*)", val => totals.GrandTotal = val },
            };

            foreach (var kvp in patterns)
            {
                var match = Regex.Match(text, kvp.Key, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var valStr = match.Groups[1].Value.Replace(",", "");
                    if (decimal.TryParse(valStr, out var val))
                        kvp.Value(val);
                }
            }

            return totals;
        }

        /// <summary>
        /// Convert parsed estimate to training data for the learning service
        /// </summary>
        public EstimateTrainingData ConvertToTrainingData(ParsedEstimate estimate)
        {
            var trainingData = new EstimateTrainingData
            {
                Id = Guid.NewGuid().ToString(),
                Source = estimate.Source,
                VehicleInfo = estimate.VehicleInfo,
                VIN = estimate.VIN,
                DateTrained = DateTime.Now
            };

            foreach (var item in estimate.LineItems)
            {
                var mapping = new LineMapping
                {
                    RawLine = item.RawLine,
                    PartName = item.PartName,
                    OperationType = item.OperationType,
                    RepairHours = item.LaborHours,
                    RefinishHours = item.RefinishHours,
                    Price = item.Price,
                    GeneratedOperations = new List<GeneratedOperation>
                    {
                        new GeneratedOperation
                        {
                            OperationType = item.OperationType,
                            Description = item.Description,
                            Category = item.Section,
                            LaborHours = item.LaborHours,
                            RefinishHours = item.RefinishHours,
                            Price = item.Price,
                            Confidence = 1.0, // Direct from estimate
                            Source = $"Imported from {estimate.Source} PDF"
                        }
                    }
                };

                trainingData.LineMappings.Add(mapping);
            }

            return trainingData;
        }
    }

    #region Data Models

    /// <summary>
    /// Represents a fully parsed estimate from a PDF
    /// </summary>
    public class ParsedEstimate
    {
        public string SourceFile { get; set; } = "";
        public string Source { get; set; } = ""; // CCC, Mitchell, Audatex, etc.
        public string VehicleInfo { get; set; } = "";
        public string VIN { get; set; } = "";
        public string RawText { get; set; } = "";
        public List<PdfEstimateLineItem> LineItems { get; set; } = new();
        public EstimateTotals Totals { get; set; } = new();
        public List<string> ParseErrors { get; set; } = new();
        public DateTime ParsedDate { get; set; }
    }

    /// <summary>
    /// A single line item from a parsed PDF estimate
    /// </summary>
    public class PdfEstimateLineItem
    {
        public string RawLine { get; set; } = "";
        public string Description { get; set; } = "";
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string Section { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; } = 1;

        /// <summary>
        /// Labor type: Body, Refinish, Mechanical, Structural, etc.
        /// </summary>
        public string LaborType { get; set; } = "";

        /// <summary>
        /// Line number from the estimate (if present)
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// True if this line had a # or * marker (CCC manual line indicator)
        /// </summary>
        public bool IsManualMarker { get; set; }

        /// <summary>
        /// True if this is an additional operation (not a main part replacement)
        /// Examples: Adhesion Promoter, Flex Additive, De-Nib, Clear Coat, etc.
        /// </summary>
        public bool IsAdditionalOperation { get; set; }

        /// <summary>
        /// Name of the parent part this additional operation belongs to
        /// </summary>
        public string? ParentPartName { get; set; }

        /// <summary>
        /// Line number of the parent part
        /// </summary>
        public int ParentLineNumber { get; set; }

        /// <summary>
        /// Confidence score for parsing accuracy (0-1)
        /// </summary>
        public double ParseConfidence { get; set; } = 1.0;
    }

    /// <summary>
    /// Totals extracted from the estimate
    /// </summary>
    public class EstimateTotals
    {
        public decimal LaborTotal { get; set; }
        public decimal PartsTotal { get; set; }
        public decimal PaintMaterial { get; set; }
        public decimal RefinishTotal { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal GrandTotal { get; set; }
    }

    #endregion
}
