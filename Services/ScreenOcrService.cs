#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using McstudDesktop.Models;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace McstudDesktop.Services
{
    /// <summary>
    /// Performs OCR on captured screenshots using the built-in Windows.Media.Ocr engine.
    /// Parses recognized text into structured estimate operations.
    /// </summary>
    public class ScreenOcrService
    {
        private static ScreenOcrService? _instance;
        public static ScreenOcrService Instance => _instance ??= new ScreenOcrService();

        private OcrEngine? _ocrEngine;
        private string? _previousRawText;

        // Regex patterns (shared with EstimateParserService)
        private static readonly Regex _pricePattern = new(@"\$?\s*(\d{1,3}(?:,\d{3})*(?:\.\d{2}))", RegexOptions.Compiled);
        private static readonly Regex _hoursPattern = new(@"(\d+\.?\d*)\s*(?:hrs?|hours?|labor)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _refinishPattern = new(@"(\d+\.?\d*)\s*(?:ref|refinish|paint)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _qtyPattern = new(@"qty[:\s]*(\d+)|(\d+)\s*(?:ea|each|x\s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public event EventHandler<string>? StatusChanged;

        public ScreenOcrService()
        {
            InitializeEngine();
        }

        private void InitializeEngine()
        {
            try
            {
                // Try to create engine with user's preferred language, fallback to first available
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_ocrEngine == null)
                {
                    // Try English specifically
                    var english = new Windows.Globalization.Language("en-US");
                    if (OcrEngine.IsLanguageSupported(english))
                    {
                        _ocrEngine = OcrEngine.TryCreateFromLanguage(english);
                    }
                }

                if (_ocrEngine != null)
                {
                    StatusChanged?.Invoke(this, $"OCR engine ready ({_ocrEngine.RecognizerLanguage.DisplayName})");
                }
                else
                {
                    StatusChanged?.Invoke(this, "OCR engine unavailable - no supported language found");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenOCR] Engine init failed: {ex.Message}");
                StatusChanged?.Invoke(this, $"OCR engine init failed: {ex.Message}");
            }
        }

        public bool IsAvailable => _ocrEngine != null;

        /// <summary>
        /// Runs OCR on a bitmap and returns structured results.
        /// </summary>
        public async Task<ScreenOcrResult> ProcessBitmapAsync(Bitmap bitmap, string sourceWindow)
        {
            var result = new ScreenOcrResult
            {
                SourceWindow = sourceWindow,
                EstimateSource = ScreenCaptureService.DetectSource(sourceWindow),
                Timestamp = DateTime.Now
            };

            if (_ocrEngine == null)
            {
                result.ErrorMessage = "OCR engine is not available. Ensure a supported language pack is installed.";
                return result;
            }

            try
            {
                // Convert System.Drawing.Bitmap to SoftwareBitmap via memory stream
                var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmap);
                if (softwareBitmap == null)
                {
                    result.ErrorMessage = "Failed to convert bitmap for OCR processing.";
                    return result;
                }

                // Run OCR
                var ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);

                // Extract text and lines
                result.RawText = ocrResult.Text ?? string.Empty;

                int lineNumber = 0;
                foreach (var line in ocrResult.Lines)
                {
                    result.Lines.Add(new OcrTextLine
                    {
                        Text = line.Text,
                        LineNumber = lineNumber++,
                        Confidence = 1.0 // Windows OCR doesn't expose per-line confidence
                    });
                }

                // Parse operations from recognized text
                result.DetectedOperations = ParseOperations(result.Lines, result.EstimateSource);

                // Detect changes from previous capture
                result.HasChanges = _previousRawText == null || !string.Equals(_previousRawText, result.RawText, StringComparison.Ordinal);
                _previousRawText = result.RawText;

                StatusChanged?.Invoke(this, $"OCR complete: {result.LineCount} lines, {result.OperationCount} operations");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenOCR] Processing failed: {ex.Message}");
                result.ErrorMessage = $"OCR processing failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Converts a System.Drawing.Bitmap to a Windows.Graphics.Imaging.SoftwareBitmap for OCR.
        /// </summary>
        private async Task<SoftwareBitmap?> ConvertToSoftwareBitmapAsync(Bitmap bitmap)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                bitmap.Save(memoryStream, ImageFormat.Bmp);
                memoryStream.Position = 0;

                // Wrap in IRandomAccessStream
                var randomAccessStream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(memoryStream.ToArray());
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                }

                // Decode to SoftwareBitmap
                randomAccessStream.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);

                return softwareBitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenOCR] Bitmap conversion failed: {ex.Message}");
                return null;
            }
        }

        // CCC operation codes
        private static readonly string[] _cccOperationCodes = { "Repl", "Rpr", "Refn", "R&I", "Blnd", "Algn", "Subl", "Add", "O/H", "New" };

        // Bare decimal pattern for hours detection (0.1–50, not preceded by $)
        private static readonly Regex _bareDecimalPattern = new(@"(?<!\$)\b(\d{1,2}\.\d{1,2})\b", RegexOptions.Compiled);

        // CCC section headers — lines matching these are skipped during operation parsing
        private static readonly HashSet<string> _cccSectionHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "FRONT BUMPER", "FRONT BUMPER & GRILLE", "FRONT BUMPER AND GRILLE",
            "REAR BUMPER", "REAR BUMPER & GRILLE", "REAR BUMPER AND GRILLE",
            "FRONT LAMPS", "REAR LAMPS", "LAMPS", "HEADLAMPS", "TAILLAMPS",
            "RADIATOR SUPPORT", "FRONT INNER STRUCTURE", "REAR INNER STRUCTURE",
            "FRAME", "UNIBODY", "STRUCTURAL",
            "FENDER", "FRONT FENDER", "REAR FENDER",
            "HOOD", "TRUNK", "DECKLID", "LIFTGATE", "TAILGATE",
            "ROOF", "ROOF PANEL", "SUNROOF",
            "FRONT DOOR", "REAR DOOR", "DOOR", "SLIDING DOOR",
            "PILLARS, ROCKER & FLOOR", "PILLARS ROCKER AND FLOOR",
            "ROCKER", "ROCKER PANEL", "PILLARS", "A PILLAR", "B PILLAR", "C PILLAR",
            "WINDSHIELD", "GLASS", "REAR GLASS", "SIDE GLASS",
            "SIDE PANEL", "QUARTER PANEL", "QUARTER",
            "ELECTRICAL", "ELECTRONICS", "WIRING",
            "INSTRUMENT PANEL", "DASH", "DASHBOARD", "INTERIOR",
            "RESTRAINT SYSTEMS", "RESTRAINTS", "AIR BAG", "AIRBAG", "SRS",
            "SUSPENSION", "STEERING", "BRAKES", "DRIVETRAIN",
            "WHEELS", "TIRES", "ALIGNMENT",
            "COOLING", "ENGINE", "EXHAUST", "A/C", "AIR CONDITIONING",
            "VEHICLE DIAGNOSTICS", "DIAGNOSTICS", "SCANNING", "CALIBRATION",
            "MISCELLANEOUS OPERATIONS", "MISCELLANEOUS", "ADDITIONAL OPERATIONS",
            "REAR BODY & FLOOR", "REAR LOADING DOOR", "CARGO",
            "BED", "TRUCK BED", "BED SIDES"
        };

        // Mitchell operations dictionary
        private static readonly Dictionary<string, string> _mitchellOperations = new(StringComparer.OrdinalIgnoreCase)
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

        // Known body panel / part names for fuzzy OCR detection
        // Known part names for fuzzy OCR detection.
        // Longer/more-specific patterns first so they match before shorter generic ones.
        // Avoid single generic words that cause false positives.
        private static readonly (string Pattern, string CanonicalName)[] _knownParts = new[]
        {
            // Bumper — specific before generic
            ("front bumper", "front bumper cover"), ("rear bumper", "rear bumper cover"),
            ("bumper reinforcement", "bumper reinforcement"), ("bumper absorber", "bumper absorber"),
            ("bumper cover", "bumper cover"), ("bumper fascia", "bumper cover"),
            ("energy absorber", "bumper absorber"),
            // Fender — liner before fender
            ("fender liner", "fender liner"), ("inner fender", "fender liner"),
            ("front fender", "fender"), ("fender", "fender"),
            // Hood
            ("hood panel", "hood"), ("hood", "hood"),
            // Doors — specific before generic
            ("front door", "front door"), ("rear door", "rear door"),
            ("door shell", "door"), ("door skin", "door"),
            ("door handle", "door handle"), ("door trim", "door trim panel"),
            // Panels
            ("quarter panel", "quarter panel"), ("qtr panel", "quarter panel"),
            ("rocker panel", "rocker panel"),
            ("roof panel", "roof"),
            // Trunk / liftgate
            ("trunk lid", "trunk lid"), ("decklid", "trunk lid"),
            ("liftgate", "liftgate"), ("tailgate", "tailgate"),
            // Lights — multi-word only
            ("headlamp", "headlight"), ("headlight", "headlight"), ("head lamp", "headlight"),
            ("tail lamp", "tail light"), ("taillight", "tail light"), ("tail light", "tail light"),
            ("fog lamp", "fog light"), ("fog light", "fog light"),
            // Front end
            ("grille", "grille"),
            ("radiator support", "radiator support"), ("rad support", "radiator support"),
            // Glass
            ("windshield", "windshield"), ("back glass", "rear glass"),
            // Exterior
            ("side mirror", "mirror"), ("outside mirror", "mirror"),
            ("parking sensor", "parking sensor"), ("park sensor", "parking sensor"),
            ("backup camera", "backup camera"),
            ("splash shield", "splash shield"), ("wheel opening", "wheel opening molding"),
            ("molding", "molding"), ("moulding", "molding"),
            ("spoiler", "spoiler"), ("valance", "valance"),
            // Pillars
            ("a pillar", "a-pillar"), ("b pillar", "b-pillar"), ("c pillar", "c-pillar"),
            // Truck
            ("running board", "running board"), ("step bar", "running board"),
            ("bed side", "bed side"), ("truck bed", "truck bed"),
            // Mechanical — multi-word only
            ("radiator", "radiator"), ("condenser", "condenser"),
            ("control arm", "control arm"),
            // Diagnostics — multi-word only
            ("adas calibration", "adas calibration"), ("diagnostic scan", "diagnostic scan"),
            ("pre scan", "diagnostic scan"), ("post scan", "diagnostic scan")
        };

        /// <summary>
        /// Parses OCR text lines into structured estimate operations.
        /// Uses source-aware parsing first, then falls back to fuzzy part-name detection.
        /// </summary>
        private List<OcrDetectedOperation> ParseOperations(List<OcrTextLine> lines, OcrEstimateSource source)
        {
            var operations = new List<OcrDetectedOperation>();

            // Pass 1: Try structured parsing (existing logic)
            foreach (var line in lines)
            {
                var text = line.Text.Trim();
                if (string.IsNullOrWhiteSpace(text) || text.Length < 5)
                    continue;

                if (_cccSectionHeaders.Contains(text))
                    continue;

                OcrDetectedOperation? operation = source switch
                {
                    OcrEstimateSource.CCCOne => ParseCCCOperation(text),
                    OcrEstimateSource.Mitchell => ParseMitchellOperation(text),
                    _ => ParseGenericOperation(text)
                };

                if (operation != null)
                    operations.Add(operation);
            }

            // Pass 2: If structured parsing found very little, do fuzzy part-name scan
            // This catches CCC ONE web UI, browser-rendered estimates, etc.
            if (operations.Count < 3)
            {
                var fullText = string.Join(" ", lines.Select(l => l.Text)).ToLowerInvariant();
                var foundParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var existingParts = new HashSet<string>(
                    operations.Select(o => o.PartName?.ToLower() ?? ""),
                    StringComparer.OrdinalIgnoreCase);

                var matchedPatterns = new HashSet<string>();
                foreach (var (pattern, canonical) in _knownParts)
                {
                    if (foundParts.Contains(canonical) || existingParts.Contains(canonical)) continue;
                    // Skip if a longer pattern already covered this text
                    if (matchedPatterns.Any(mp => mp.Contains(pattern))) continue;

                    var patternIdx = fullText.IndexOf(pattern);
                    if (patternIdx < 0) continue;

                    // Word boundary check — avoid "waterproof" matching "roof" etc.
                    if (patternIdx > 0 && char.IsLetter(fullText[patternIdx - 1])) continue;
                    var endIdx = patternIdx + pattern.Length;
                    if (endIdx < fullText.Length && char.IsLetter(fullText[endIdx])) continue;

                    foundParts.Add(canonical);
                    matchedPatterns.Add(pattern);
                    {
                        // Try to detect operation type from nearby context
                        var opType = "";
                        if (patternIdx >= 0)
                        {
                            // Look at ~60 chars around the mention for operation clues
                            var start = Math.Max(0, patternIdx - 30);
                            var end = Math.Min(fullText.Length, patternIdx + pattern.Length + 30);
                            var context = fullText.Substring(start, end - start);

                            if (context.Contains("repl") || context.Contains("new") || context.Contains("r/r"))
                                opType = "Replace";
                            else if (context.Contains("r&i") || context.Contains("r+i") || context.Contains("remove"))
                                opType = "R&I";
                            else if (context.Contains("rpr") || context.Contains("repair"))
                                opType = "Repair";
                            else if (context.Contains("refn") || context.Contains("refinish") || context.Contains("paint"))
                                opType = "Refinish";
                            else if (context.Contains("blend") || context.Contains("blnd"))
                                opType = "Blend";
                        }

                        operations.Add(new OcrDetectedOperation
                        {
                            Description = canonical,
                            PartName = canonical,
                            OperationType = opType,
                            RawLine = $"[detected from screen: {canonical}]",
                            LaborHours = 0,
                            Price = 0
                        });
                    }
                }

                if (foundParts.Count > 0)
                {
                    Debug.WriteLine($"[ScreenOCR] Fuzzy scan found {foundParts.Count} parts: {string.Join(", ", foundParts)}");
                }
            }

            return operations;
        }

        /// <summary>
        /// Token-based parser for CCC ONE lines. Adapted from EstimatePdfParser.ParseCCCLineItem.
        /// </summary>
        private OcrDetectedOperation? ParseCCCOperation(string text)
        {
            var tokens = Regex.Split(text, @"\s{2,}|\t").Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (tokens.Count < 2) return null;

            int idx = 0;

            // Consume leading line number
            if (int.TryParse(tokens[idx], out _))
                idx++;

            if (idx >= tokens.Count) return null;

            // Consume marker tokens: # (manual), * or ** (modified)
            if (tokens[idx] == "#" || tokens[idx] == "*" || tokens[idx] == "**")
                idx++;

            if (idx >= tokens.Count) return null;

            // Check for operation type
            string opType = "";
            if (_cccOperationCodes.Any(op => tokens[idx].Equals(op, StringComparison.OrdinalIgnoreCase)))
            {
                opType = tokens[idx];
                idx++;
            }

            if (idx >= tokens.Count) return null;

            // Classify remaining tokens
            var descParts = new List<string>();
            string? partNumber = null;
            decimal price = 0;
            decimal laborHours = 0;
            decimal refinishHours = 0;
            int quantity = 1;

            for (int i = idx; i < tokens.Count; i++)
            {
                var token = tokens[i];

                // Part number: 6+ chars, has both letters and digits, no dot
                if (partNumber == null && token.Length >= 6 &&
                    token.Any(char.IsDigit) && token.Any(char.IsLetter) && !token.Contains("."))
                {
                    partNumber = token;
                    continue;
                }

                // Price detection
                if (price == 0 && TryParsePrice(token, out var p))
                {
                    price = p;
                    continue;
                }

                // Hours detection: bare decimal 0.1–50
                if (decimal.TryParse(token, out var hrs) && hrs >= 0.1m && hrs <= 50m)
                {
                    if (laborHours == 0)
                        laborHours = hrs;
                    else if (refinishHours == 0)
                        refinishHours = hrs;
                    continue;
                }

                // Quantity: integer 1–20
                if (quantity == 1 && int.TryParse(token, out var qty) && qty >= 1 && qty <= 20)
                {
                    quantity = qty;
                    continue;
                }

                // Skip labor type markers and "Incl"
                if (token == "M" || token == "m" ||
                    token.Equals("Incl", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Incl.", StringComparison.OrdinalIgnoreCase))
                    continue;

                descParts.Add(token);
            }

            var description = string.Join(" ", descParts);
            if (string.IsNullOrWhiteSpace(description) && string.IsNullOrEmpty(opType))
                return null;

            return new OcrDetectedOperation
            {
                Description = string.IsNullOrWhiteSpace(description) ? text : description,
                PartName = string.IsNullOrWhiteSpace(description) ? text : description,
                OperationType = opType,
                PartNumber = partNumber,
                Price = price,
                LaborHours = laborHours,
                RefinishHours = refinishHours,
                Quantity = quantity > 1 ? quantity : 0,
                RawLine = text
            };
        }

        /// <summary>
        /// Token-based parser for Mitchell lines. Adapted from EstimatePdfParser.ParseMitchellLineItem.
        /// </summary>
        private OcrDetectedOperation? ParseMitchellOperation(string text)
        {
            var tokens = Regex.Split(text, @"\s{2,}|\t").Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (tokens.Count < 3) return null;

            int idx = 0;

            // Consume leading line number
            if (int.TryParse(tokens[idx], out _))
                idx++;

            if (idx >= tokens.Count) return null;

            // Consume Mitchell line code (AUTO, 900500, 6-digit codes)
            if (tokens[idx] == "AUTO" || tokens[idx] == "900500" || Regex.IsMatch(tokens[idx], @"^\d{6}$"))
                idx++;

            if (idx >= tokens.Count) return null;

            // Try to match Mitchell operation names (may span tokens due to OCR spacing)
            string opType = "";
            var descParts = new List<string>();
            string? partNumber = null;
            decimal price = 0;
            decimal laborHours = 0;
            decimal refinishHours = 0;
            int quantity = 1;

            for (int i = idx; i < tokens.Count; i++)
            {
                var token = tokens[i];

                // Check for Mitchell operation type
                if (string.IsNullOrEmpty(opType))
                {
                    bool foundOp = false;
                    foreach (var kvp in _mitchellOperations)
                    {
                        if (token.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                            token.Replace(" ", "").Equals(kvp.Key.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                        {
                            opType = kvp.Value;
                            foundOp = true;
                            break;
                        }
                    }
                    if (foundOp) continue;
                }

                // Part number
                if (partNumber == null && token.Length >= 6 &&
                    token.Any(char.IsDigit) && token.Any(char.IsLetter) && !token.Contains("."))
                {
                    partNumber = token;
                    continue;
                }

                // Price
                if (price == 0 && TryParsePrice(token, out var p))
                {
                    price = p;
                    continue;
                }

                // Hours
                if (decimal.TryParse(token, out var hrs) && hrs >= 0.1m && hrs <= 50m)
                {
                    if (laborHours == 0)
                        laborHours = hrs;
                    else if (refinishHours == 0)
                        refinishHours = hrs;
                    continue;
                }

                // Quantity
                if (quantity == 1 && int.TryParse(token, out var qty) && qty >= 1 && qty <= 20)
                {
                    quantity = qty;
                    continue;
                }

                // Skip labor type markers
                if (token == "Body" || token == "Body*" || token == "Refinish" || token == "Mechanical")
                    continue;

                descParts.Add(token);
            }

            var description = string.Join(" ", descParts);
            if (string.IsNullOrWhiteSpace(description))
                return null;

            return new OcrDetectedOperation
            {
                Description = description,
                PartName = description,
                OperationType = opType,
                PartNumber = partNumber,
                Price = price,
                LaborHours = laborHours,
                RefinishHours = refinishHours,
                Quantity = quantity > 1 ? quantity : 0,
                RawLine = text
            };
        }

        /// <summary>
        /// Enhanced regex-based parser for unknown/Audatex sources.
        /// Includes CCC abbreviation support and bare decimal hours detection.
        /// </summary>
        private OcrDetectedOperation? ParseGenericOperation(string text)
        {
            var opType = DetermineOperationType(text.ToLowerInvariant());
            if (string.IsNullOrEmpty(opType) && !HasPriceOrHours(text))
                return null;

            var operation = new OcrDetectedOperation
            {
                Description = text,
                OperationType = opType,
                RawLine = text
            };

            // Extract part name
            operation.PartName = ExtractPartName(text);

            // Extract price
            var priceMatch = _pricePattern.Match(text);
            if (priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value.Replace(",", ""), out var price))
            {
                operation.Price = price;
            }

            // Extract labor hours (labeled pattern first)
            var hoursMatch = _hoursPattern.Match(text);
            if (hoursMatch.Success && decimal.TryParse(hoursMatch.Groups[1].Value, out var hours))
            {
                operation.LaborHours = hours;
            }

            // Extract refinish hours
            var refinishMatch = _refinishPattern.Match(text);
            if (refinishMatch.Success && decimal.TryParse(refinishMatch.Groups[1].Value, out var refHours))
            {
                operation.RefinishHours = refHours;
            }

            // Bare decimal hours fallback: if no labeled hours found, look for standalone decimals
            if (operation.LaborHours == 0)
            {
                foreach (Match m in _bareDecimalPattern.Matches(text))
                {
                    if (decimal.TryParse(m.Groups[1].Value, out var bareHrs) && bareHrs >= 0.1m && bareHrs <= 50m)
                    {
                        // Make sure this isn't already captured as a price
                        if (operation.Price == 0 || bareHrs != operation.Price)
                        {
                            if (operation.LaborHours == 0)
                                operation.LaborHours = bareHrs;
                            else if (operation.RefinishHours == 0)
                                operation.RefinishHours = bareHrs;
                            break;
                        }
                    }
                }
            }

            // Extract quantity
            var qtyMatch = _qtyPattern.Match(text);
            if (qtyMatch.Success)
            {
                var qtyStr = qtyMatch.Groups[1].Success ? qtyMatch.Groups[1].Value : qtyMatch.Groups[2].Value;
                if (int.TryParse(qtyStr, out var qty) && qty > 0 && qty < 100)
                {
                    operation.Quantity = qty;
                }
            }

            return operation;
        }

        private bool TryParsePrice(string token, out decimal price)
        {
            price = 0;
            var cleaned = token.Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(cleaned, out var val))
            {
                if (val > 10 && (token.Contains(".") || token.Contains(",")))
                {
                    price = val;
                    return true;
                }
            }
            return false;
        }

        private string DetermineOperationType(string lowerLine)
        {
            // CCC abbreviations (standalone word match)
            if (Regex.IsMatch(lowerLine, @"\brepl\b"))
                return "Replace";
            if (Regex.IsMatch(lowerLine, @"\brefn\b"))
                return "Refinish";
            if (Regex.IsMatch(lowerLine, @"\bblnd\b"))
                return "Blend";
            if (Regex.IsMatch(lowerLine, @"\balgn\b"))
                return "Align";

            // Full words and other patterns
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
            if (lowerLine.Contains("sublet") || Regex.IsMatch(lowerLine, @"\bsubl\b"))
                return "Sublet";
            return "";
        }

        private bool HasPriceOrHours(string text)
        {
            if (_pricePattern.IsMatch(text) || _hoursPattern.IsMatch(text))
                return true;

            // Bare decimal detection: any standalone decimal 0.1–50 that isn't a price
            foreach (Match m in _bareDecimalPattern.Matches(text))
            {
                if (decimal.TryParse(m.Groups[1].Value, out var val) && val >= 0.1m && val <= 50m)
                    return true;
            }

            return false;
        }

        private string ExtractPartName(string text)
        {
            // Remove numeric data, prices, and operation keywords to isolate the part name
            var cleaned = Regex.Replace(text, @"\$[\d,\.]+", "");
            cleaned = Regex.Replace(cleaned, @"\d+\.?\d*\s*(?:hrs?|hours?|labor|ref|refinish|paint|ea|each)", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"(?:replace|r/?r|r&i|r\+i|repair|rpr|blend|refinish|o/?h|sublet|qty[:\s]*\d+)", "", RegexOptions.IgnoreCase);
            // Remove CCC abbreviations
            cleaned = Regex.Replace(cleaned, @"\b(?:Repl|Rpr|Refn|Blnd|Algn|Subl|Add|O/H|New)\b", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
            return cleaned.Length > 2 ? cleaned : text.Trim();
        }

        /// <summary>
        /// Resets the change detection baseline.
        /// </summary>
        public void ResetChangeTracking()
        {
            _previousRawText = null;
        }
    }
}
