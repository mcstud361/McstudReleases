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

        // Common OCR misreads — applied to raw text before pattern matching
        private static readonly (string Wrong, string Right)[] _ocrCorrections = new[]
        {
            ("batten,'", "battery"), ("batten,\"", "battery"), ("batten'", "battery"),
            ("batten,", "battery"), ("batteiy", "battery"), ("batter,'", "battery"),
            ("battey", "battery"), ("batt ery", "battery"),
            ("bumpe r", "bumper"), ("bumpe,", "bumper"), ("bumper,", "bumper"),
            ("electr onic", "electronic"), ("electr0nic", "electronic"),
            ("reinf orcement", "reinforcement"), ("reinf0rcement", "reinforcement"),
            ("calibrat ion", "calibration"), ("calibrat1on", "calibration"),
            ("disconnect and reconnect batten", "disconnect and reconnect battery"),
            ("disconnect and reconnect batte", "disconnect and reconnect battery"),
            ("test batten", "test battery"), ("charge and maintain batten", "charge and maintain battery"),
        };

        // Hardware / part-number keywords — these are parts, not estimating operations
        private static readonly string[] _hardwareKeywords = new[]
        {
            "bolt", "nut ", "clip #", "clip#", "grommet", "rivet ", "screw",
            "fastener", "felt strip", "retainer clip", "liner clip",
            "impact bar bolt", "seal retainer", "side seal",
            "side retainer", "lower cover retainer",
            "pin ", "washer ", "stud ", "bracket bolt",
        };

        // Regex patterns (shared with EstimateParserService)
        private static readonly Regex _pricePattern = new(@"\$\s*(\d{1,3}(?:,\d{3})*(?:\.\d{1,2})?)", RegexOptions.Compiled);
        private static readonly Regex _hoursPattern = new(@"(\d+\.?\d*)\s*(?:hrs?|hours?|labor)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _refinishPattern = new(@"(\d+\.?\d*)\s*(?:ref|refinish|paint)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _qtyPattern = new(@"qty[:\s]*(\d+)|(\d+)\s*(?:ea|each|x\s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _vinPattern = new(@"\b([A-HJ-NPR-Z0-9]{17})\b", RegexOptions.Compiled);

        // Diagnostic/scan canonical names — these should always get op type "Sublet", not context-guessed
        private static readonly HashSet<string> _diagnosticCanonicalNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "diagnostic scan", "pre-repair scan", "post-repair scan", "in-process scan",
            "adas calibration", "adas diagnostic report", "module programming",
            "gateway unlock", "drive cycle", "dynamic systems verification",
            "setup scan tool", "steering angle reset", "aim headlamps", "TPMS reset",
            "OEM research", "electronic reset"
        };

        // Must-have / shop-operation canonical names — these get op type "Rpr" when context detection
        // can't determine one (misc charges sections often lack standard op-type keywords nearby)
        private static readonly HashSet<string> _mustHaveCanonicalNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "cover and protect electrical connections",
            "charge and maintain battery during ADAS",
            "simulate full fluids for ADAS calibrations",
            "check and adjust tire pressure for ADAS calibrations",
            "remove customer belongings for ADAS calibrations",
            "mask and protect removed components",
            "steering wheel cover, seat cover, and floor mat",
            "clean for delivery", "glass cleaner", "parts disposal",
            "hazardous waste", "misc hardware", "test battery condition",
            "battery support"
        };

        // CCC modifier/add-on sub-line patterns — these are NOT standalone operations.
        // They modify the preceding parent panel line (e.g., "Add for Clear Coat" under a Quarter Panel Replace).
        private static readonly Regex _cccModifierPattern = new(
            @"(?i)^\s*\d*\s*(?:Add\s+for\s+|Overlap|Major\s+Adj|Minor\s+Adj|Adjacent\s+Adj|Deduct\s+Overlap|Add\s+Clear\s*Coat|Add\s+Underside|Add\s+Two\s*Tone|Add\s+Tri[\s-]*Coat|Anti[\s-]*Corrosion|Edging)",
            RegexOptions.Compiled);

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

                // Optional AI-powered OCR cleanup for better accuracy
                var aiCleanedOps = await TryAiOcrCleanupAsync(result.RawText, result.EstimateSource);
                if (aiCleanedOps != null && aiCleanedOps.Count > 0)
                {
                    Debug.WriteLine($"[ScreenOCR] AI cleanup returned {aiCleanedOps.Count} ops (was {result.DetectedOperations.Count})");
                    result.DetectedOperations = aiCleanedOps;
                }

                // Detect changes from previous capture
                result.HasChanges = _previousRawText == null || !string.Equals(_previousRawText, result.RawText, StringComparison.Ordinal);
                _previousRawText = result.RawText;

                // Extract VIN if present
                var vinMatch = _vinPattern.Match(result.RawText);
                if (vinMatch.Success)
                    result.DetectedVin = vinMatch.Groups[1].Value;

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

                // Wrap in IRandomAccessStream (using ensures COM stream is released each OCR cycle)
                using var randomAccessStream = new InMemoryRandomAccessStream();
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

        // UI chrome strings — any OCR line containing one of these is skipped.
        // Covers CCC ONE desktop, CCC Web (browser), Mitchell, email clients, and common apps.
        private static readonly HashSet<string> _uiChromePatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            // ── CCC ONE Desktop ──
            "WORKFILE", "Save Filter", "Frame Save and Print", "Advisor", "ECIT",
            "Estimate Properties", "Rates and Rules", "Delete Estimate",
            "Repairable Total Loss", "Repairable", "Total Loss", "threshold",
            "Preliminary Estimate", "Lines MOTOR", "Other Charg", "Ext. Price",
            "Part Cod", "Search for Parts", "Checkout", "Diagnostics Checkout",
            // CCC ONE parts catalog sidebar (right panel — NOT estimate data)
            "Add to Estimate", "Select multiple part codes", "Advisa-",
            "Rivian Pre-Scan", "Rivian Post-Scan", "Rivian In-Process",
            "Tesla Toolbox", "Tesla Softvvare", "Tesla Software",
            "RiDE", "Flex part T",
            "Ins ur ance", "Es tin", "Rental Estim", "Attachments",
            "Ev ents", "Fo rms", "Recy opt", "OEM Recond", "compare PDR",
            "Section • Operations", "Tire Part", "Re finish", "• Search",
            // CCC ONE legend/abbreviation definitions
            "D&R=", "HSS=", "HSLA=", "UHSS=", "SMC=", "TPO=", "ABS=", "PPO=",
            "Disconnect and Reconnect", "High Strength Steel",
            // CCC ONE diagram/parts views
            "Front View", "Rear View", "Side View", "Exploded View", "Top View", "Bottom View",
            "Undercarriage View", "Parts Diagram", "Parts List", "Interactive Diagram",
            "Diagram Database", "Select Part", "Part Catalog", "Illustration",
            // CCC ONE additional UI
            "Photo Gallery", "Photos & Documents", "Assignment Sheet", "Supplement",
            "Profile Menu", "User Settings", "Log Out", "Sign Out",

            // ── CCC Web (browser-based) ──
            "New Tab", "Bookmarks", "Extensions", "Downloads", "History",
            "Ctrl+", "Alt+", "Address bar", "Search or type", "google.com",
            "Chrome Web Store", "Edge Add-ons",
            "caborneone.com", "Welcome to CCC", "My Assignments", "Dashboard",
            "Open Assignment", "Close Assignment", "Refresh", "Navigation Menu",
            "Notifications", "Help Center", "Support Chat", "Release Notes",
            "Print Preview", "Page Setup", "Zoom",

            // ── Mitchell Desktop/Cloud UI ──
            "Mitchell Cloud Estimating", "Mitchell International", "Mitchell WorkCenter",
            "UltraMate", "Repair Center",
            "Estimate Summary", "Estimate Manager", "Create New Estimate",
            "Open Estimate", "Close Estimate", "Print Estimate",
            "Parts Source", "Part Number Lookup", "Collision Estimating Guide",
            "CEG", "Guide Page", "Procedure Page", "P-Pages",
            "Rate Lookup", "Labor Rate", "Material Rate",
            "Database Version", "Vehicle Selection", "Select Vehicle",
            "Vehicle ID", "Decode VIN", "VIN Decode",
            "Quick Add", "Paste from Clipboard", "Import from",
            "Total Loss Evaluation", "Salvage", "Prior Damage",
            "Assignment Manager", "Claim Manager",
            "Mitchell Diagnostics", "asTech", "Opus IVS",

            // ── Email / communication apps ──
            "Inbox", "Sent Items", "Drafts", "Compose", "Reply All", "Forward",
            "Junk Email", "Deleted Items", "Archive", "Flagged",
            "Subject:", "From:", "To:", "Cc:", "Bcc:",
            "@gmail.com", "@outlook.com", "@yahoo.com", "@hotmail.com",
            "@aol.com", "@icloud.com",
            "Microsoft Teams", "Slack", "Zoom Meeting"
        };

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
            // ── Bumper (specific before generic) ──
            ("front bumper cover", "front bumper cover"), ("front bumper", "front bumper cover"),
            ("frt bmpr cvr", "front bumper cover"), ("frt bumper", "front bumper cover"),
            ("rear bumper cover", "rear bumper cover"), ("rear bumper", "rear bumper cover"),
            ("rr bmpr cvr", "rear bumper cover"), ("rr bumper", "rear bumper cover"),
            ("bumper reinforcement", "bumper reinforcement"), ("bumper rebar", "bumper reinforcement"),
            ("bumper reinf", "bumper reinforcement"),
            ("bumper absorber", "bumper absorber"), ("energy absorber", "bumper absorber"),
            ("bumper cover", "bumper cover"), ("bumper fascia", "bumper cover"),
            ("bmpr cvr", "bumper cover"),

            // ── Fender (liner before fender) ──
            ("fender liner", "fender liner"), ("inner fender", "fender liner"),
            ("fndr liner", "fender liner"),
            ("front fender", "fender"), ("frt fndr", "fender"), ("fender", "fender"),

            // ── Hood ──
            ("hood panel", "hood"), ("hood", "hood"),

            // ── Doors (specific before generic) ──
            ("front door", "front door"), ("frt door", "front door"),
            ("rear door", "rear door"),
            ("door shell", "door"), ("door skin", "door"),
            ("door handle", "door handle"), ("door hndl", "door handle"),
            ("door trim panel", "door trim panel"), ("door trim", "door trim panel"),

            // ── Panels ──
            ("quarter panel", "quarter panel"), ("qtr panel", "quarter panel"),
            ("q/p", "quarter panel"), ("quarter", "quarter panel"),
            ("rocker panel", "rocker panel"), ("rckr panel", "rocker panel"),
            ("roof panel", "roof"), ("roof", "roof"),

            // ── Trunk / liftgate ──
            ("trunk lid", "trunk lid"), ("decklid", "trunk lid"), ("deck lid", "trunk lid"),
            ("dklid", "trunk lid"),
            ("liftgate", "liftgate"), ("lift gate", "liftgate"),
            ("tailgate", "tailgate"), ("tail gate", "tailgate"),

            // ── Lights ──
            ("headlamp", "headlight"), ("headlight", "headlight"), ("head lamp", "headlight"),
            ("head light", "headlight"), ("hdlmp", "headlight"), ("hdlt", "headlight"),
            ("tail lamp", "tail light"), ("taillight", "tail light"), ("tail light", "tail light"),
            ("tllmp", "tail light"),
            ("fog lamp", "fog light"), ("fog light", "fog light"),
            ("turn signal", "turn signal"), ("marker lamp", "marker lamp"),

            // ── Front end ──
            ("grille", "grille"), ("grill", "grille"),
            ("radiator support", "radiator support"), ("rad support", "radiator support"),
            ("radtr support", "radiator support"),

            // ── Glass ──
            ("windshield", "windshield"), ("wndshld", "windshield"), ("w/s", "windshield"),
            ("back glass", "rear glass"), ("rear glass", "rear glass"),
            ("side glass", "side glass"), ("door glass", "door glass"),
            ("prime glass", "prime glass"),

            // ── Exterior ──
            ("side mirror", "mirror"), ("outside mirror", "mirror"),
            ("door mirror", "mirror"), ("mirr", "mirror"),
            ("parking sensor", "parking sensor"), ("park sensor", "parking sensor"),
            ("backup camera", "backup camera"), ("rear camera", "backup camera"),
            ("rearview camera", "backup camera"),
            ("splash shield", "splash shield"), ("wheel opening", "wheel opening molding"),
            ("molding", "molding"), ("moulding", "molding"), ("mldg", "molding"),
            ("emblem", "emblem"), ("badge", "emblem"), ("nameplate", "emblem"),
            ("spoiler", "spoiler"), ("valance", "valance"),

            // ── Pillars ──
            ("a pillar", "a-pillar"), ("a-pillar", "a-pillar"),
            ("b pillar", "b-pillar"), ("b-pillar", "b-pillar"),
            ("c pillar", "c-pillar"), ("c-pillar", "c-pillar"),

            // ── Truck ──
            ("running board", "running board"), ("step bar", "running board"),
            ("bed side", "bed side"), ("truck bed", "truck bed"),

            // ── Restraints ──
            ("air bag", "air bag"), ("airbag", "air bag"),
            ("seat belt", "seat belt"), ("seatbelt", "seat belt"),
            ("pretensioner", "seat belt pretensioner"),

            // ── Interior ──
            ("instrument panel", "instrument panel"), ("dash panel", "instrument panel"),
            ("dashboard", "instrument panel"), ("dash pad", "instrument panel"),

            // ── Diagnostics — scans ──
            ("diagnostic scan", "diagnostic scan"),
            ("pre-repair scan", "pre-repair scan"), ("pre repair scan", "pre-repair scan"),
            ("pre-scan", "pre-repair scan"), ("pre scan", "pre-repair scan"), ("prescan", "pre-repair scan"),
            ("post-repair scan", "post-repair scan"), ("post repair scan", "post-repair scan"),
            ("post-scan", "post-repair scan"), ("post scan", "post-repair scan"), ("postscan", "post-repair scan"),
            ("in-process scan", "in-process scan"), ("in process scan", "in-process scan"),
            ("mid-repair scan", "in-process scan"),

            // ── Diagnostics — calibration & modules ──
            ("adas calibration", "adas calibration"), ("camera calibration", "adas calibration"),
            ("radar calibration", "adas calibration"), ("sensor calibration", "adas calibration"),
            ("static calibration", "adas calibration"), ("dynamic calibration", "adas calibration"),
            ("target setup", "adas calibration"),
            ("adas diagnostic", "adas diagnostic report"), ("adas report", "adas diagnostic report"),
            ("module programming", "module programming"), ("reprogram", "module programming"),
            ("initialization", "module programming"), ("initialize", "module programming"),
            ("relearn", "module programming"), ("idle relearn", "module programming"),
            ("module setup", "module programming"), ("flash module", "module programming"),
            ("gateway unlock", "gateway unlock"), ("security gateway", "gateway unlock"),
            ("drive cycle", "drive cycle"), ("test drive", "drive cycle"), ("road test", "drive cycle"),
            ("dynamic systems verification", "dynamic systems verification"),
            ("dynamic systems", "dynamic systems verification"),
            ("systems verification", "dynamic systems verification"),
            ("setup scan tool", "setup scan tool"), ("scan tool setup", "setup scan tool"),
            ("simulate full fluids", "simulate full fluids for ADAS calibrations"),
            ("full fluids adas", "simulate full fluids for ADAS calibrations"),
            ("simulate fluids adas", "simulate full fluids for ADAS calibrations"),
            ("check and adjust tire pressure", "check and adjust tire pressure for ADAS calibrations"),
            ("adjust tire pressure", "check and adjust tire pressure for ADAS calibrations"),
            ("tire pressure adas", "check and adjust tire pressure for ADAS calibrations"),
            ("check tire pressure", "check and adjust tire pressure for ADAS calibrations"),
            ("remove customer belongings", "remove customer belongings for ADAS calibrations"),
            ("customer belongings", "remove customer belongings for ADAS calibrations"),
            ("steering angle reset", "steering angle reset"),
            ("aim headlamp", "aim headlamps"), ("aim headlamps", "aim headlamps"),
            ("headlamp aiming", "aim headlamps"),
            ("tpms reset", "TPMS reset"), ("tpms relearn", "TPMS reset"),

            // ── Battery ──
            ("disconnect/reconnect battery", "battery disconnect/reconnect"),
            ("disconnect battery", "battery disconnect/reconnect"),
            ("reconnect battery", "battery disconnect/reconnect"),
            ("d/c battery", "battery disconnect/reconnect"),
            ("battery disconnect", "battery disconnect/reconnect"),
            ("battery reconnect", "battery disconnect/reconnect"),
            ("battery initialize", "battery disconnect/reconnect"),
            ("battery init", "battery disconnect/reconnect"),
            ("test battery", "test battery condition"),
            ("battery condition", "test battery condition"),
            ("battery test", "test battery condition"),
            ("memory saver", "memory saver"), ("ks-100", "memory saver"),
            ("keep alive", "memory saver"), ("keep-alive", "memory saver"),
            ("battery support", "battery support"), ("battery maintainer", "battery support"),
            ("battery tender", "battery support"),
            ("electronic reset", "electronic reset"), ("electronic module reset", "electronic reset"),
            ("ecm reset", "electronic reset"), ("ecu reset", "electronic reset"),
            ("cover and protect electrical", "cover and protect electrical connections"),
            ("cover electrical connections", "cover and protect electrical connections"),
            ("protect electrical connections", "cover and protect electrical connections"),
            ("protect electrical", "cover and protect electrical connections"),
            ("charge and maintain battery", "charge and maintain battery during ADAS"),
            ("charge maintain battery", "charge and maintain battery during ADAS"),
            ("maintain battery adas", "charge and maintain battery during ADAS"),
            ("charge battery adas", "charge and maintain battery during ADAS"),

            // ── Surface prep & paint materials ──
            ("adhesion promoter", "adhesion promoter"), ("ad pro", "adhesion promoter"),
            ("adpro", "adhesion promoter"),
            ("flex additive", "flex additive"), ("flex add", "flex additive"),
            ("flex agent", "flex additive"),
            ("plastic prep", "adhesion promoter"),
            ("primer surfacer", "primer surfacer"), ("primer sealer", "primer surfacer"),
            ("color tint", "color tint"), ("color match", "color tint"), ("tinting", "color tint"),
            ("color verify", "color tint"),
            ("spray out card", "spray out cards"), ("spray out cards", "spray out cards"),
            ("spray card", "spray out cards"), ("let down panel", "spray out cards"),
            ("spray out panel", "spray out cards"),
            ("clear coat", "clear coat"), ("clearcoat", "clear coat"),
            ("basecoat", "basecoat"), ("base coat", "basecoat"),
            ("single stage", "single stage"),
            ("tri-coat", "tri-coat"), ("tricoat", "tri-coat"), ("three stage", "tri-coat"),
            ("seam sealer", "seam sealer"), ("seam seal", "seam sealer"),
            ("weld-thru primer", "weld-thru primer"), ("weld thru primer", "weld-thru primer"),
            ("weld through primer", "weld-thru primer"), ("welding primer", "weld-thru primer"),
            ("weld thru", "weld-thru primer"),
            ("e-coat", "e-coat"), ("ecoat", "e-coat"),
            ("corrosion protection", "corrosion protection"), ("cavity wax", "corrosion protection"),
            ("rust protection", "corrosion protection"), ("anti-corrosion", "corrosion protection"),
            ("rust preventative", "corrosion protection"), ("undercoating", "corrosion protection"),
            ("undercoat", "corrosion protection"),
            ("sound deadener", "sound deadener"),
            ("chip guard", "chip guard"), ("stone guard", "chip guard"),
            ("rubberize", "chip guard"),
            ("bedliner", "bedliner"), ("bed liner", "bedliner"),

            // ── Sanding & finishing ──
            ("denib", "denib"), ("de-nib", "denib"), ("de nib", "denib"), ("nib sand", "denib"),
            ("wet sand", "wet sand"), ("color sand", "wet sand"),
            ("block sand", "block sand"), ("final sand", "block sand"),
            ("dry sand", "block sand"),
            ("rub-out", "cut & buff"), ("rub out", "cut & buff"),
            ("cut and buff", "cut & buff"), ("cut & buff", "cut & buff"),
            ("buff and polish", "cut & buff"), ("compound", "cut & buff"),
            ("feather edge", "feather edge"),
            ("feather prime and block", "feather prime & block"),
            ("feather, prime & block", "feather prime & block"),
            ("feather prime block", "feather prime & block"),
            ("feather prime & block", "feather prime & block"),
            ("fpb", "feather prime & block"),
            ("spot repair", "spot repair"),

            // ── Masking & protection ──
            ("mask and protect removed", "mask and protect removed components"),
            ("mask protect removed", "mask and protect removed components"),
            ("protect removed components", "mask and protect removed components"),
            ("mask and protect", "mask & protect"), ("mask & protect", "mask & protect"),
            ("masking", "mask & protect"), ("mask for", "mask & protect"),
            ("tape for", "mask & protect"),
            ("backtape", "back tape"), ("back tape", "back tape"),
            ("cover car for overspray", "cover car for overspray"),
            ("cover for overspray", "cover car for overspray"),
            ("cover for edging", "cover for edging"),
            ("mask for buffing", "mask for buffing"),
            ("cover engine compartment", "cover engine compartment"),
            ("cover interior and jambs", "cover interior and jambs"),
            ("cover jambs", "cover interior and jambs"),
            ("clean and cover car for primer", "clean and cover car for primer"),
            ("clean and cover for primer", "clean and cover car for primer"),
            ("cover car for primer", "clean and cover car for primer"),
            ("cover car", "cover vehicle"), ("cover interior", "cover vehicle"),
            ("cover engine", "cover vehicle"), ("cover trunk", "cover vehicle"),
            ("bagging", "cover vehicle"),
            ("wheel cover", "wheel/tire cover"), ("tire cover", "wheel/tire cover"),
            ("cover wheels", "wheel/tire cover"),
            ("plastic cover", "protective covers"),

            // ── Pre-wash & degrease ──
            ("pre wash and degrease", "pre-wash & degrease"),
            ("pre-wash and degrease", "pre-wash & degrease"),
            ("pre wash", "pre-wash & degrease"), ("pre-wash", "pre-wash & degrease"),
            ("degrease", "pre-wash & degrease"), ("decontaminate", "pre-wash & degrease"),
            ("solvent wipe", "pre-wash & degrease"), ("prep wipe", "pre-wash & degrease"),
            ("tack cloth", "pre-wash & degrease"),

            // ── Setup & handling ──
            ("stage and secure", "stage & secure"), ("stage & secure", "stage & secure"),
            ("trial fit", "test fit"), ("test fit", "test fit"),
            ("dry fit", "test fit"), ("mock up", "test fit"),
            ("transfer parts", "parts transfer"), ("swap parts", "parts transfer"),
            ("parts transfer", "parts transfer"),
            ("r&i for access", "R&I for access"), ("remove for access", "R&I for access"),
            ("access time", "R&I for access"),
            ("additional labor", "additional labor"),

            // ── Welding & structural ──
            ("plug weld", "plug weld"), ("spot weld", "spot weld"),
            ("mig weld", "MIG weld"), ("mig braze", "MIG braze"),
            ("resistance weld", "resistance weld"), ("squeeze type resistance", "resistance weld"),
            ("weld prep", "weld prep"), ("grind weld", "grind weld"),
            ("dress weld", "grind weld"), ("seam weld", "seam weld"),
            ("structural adhesive", "structural adhesive"), ("panel bond", "structural adhesive"),
            ("rivet", "rivet"), ("flow drill", "rivet"),

            // ── Glass materials ──
            ("urethane", "urethane"), ("butyl tape", "butyl tape"),
            ("dam tape", "dam tape"),
            ("molding tape", "molding tape"), ("emblem tape", "molding tape"),
            ("badge tape", "molding tape"), ("retape", "molding tape"),

            // ── Alignment ──
            ("4-wheel alignment", "4-wheel alignment"), ("4 wheel alignment", "4-wheel alignment"),
            ("four wheel alignment", "4-wheel alignment"), ("wheel alignment", "4-wheel alignment"),
            ("alignment check", "4-wheel alignment"), ("alignment", "4-wheel alignment"),

            // ── Shop operations — cleanup ──
            ("clean for delivery", "clean for delivery"), ("final clean", "clean for delivery"),
            ("detail clean", "clean for delivery"), ("detail vehicle", "clean for delivery"),
            ("vehicle cleanup", "clean for delivery"), ("final wash", "clean for delivery"),
            ("final detail", "clean for delivery"), ("interior clean", "clean for delivery"),
            ("exterior wash", "clean for delivery"), ("wash vehicle", "clean for delivery"),
            ("clean area", "clean for delivery"),

            // ── Shop operations — waste & disposal ──
            ("hazardous waste", "hazardous waste"), ("haz waste", "hazardous waste"),
            ("hazmat", "hazardous waste"), ("waste disposal", "hazardous waste"),
            ("environmental fee", "hazardous waste"),
            ("parts disposal", "parts disposal"), ("disposal fee", "parts disposal"),
            ("shop supplies", "shop supplies"),

            // ── Shop operations — misc ──
            ("collision wrap", "collision wrap"), ("crash wrap", "collision wrap"),
            ("oem research", "OEM research"), ("oem procedure", "OEM research"),
            ("oem repair procedure", "OEM research"), ("repair procedure", "OEM research"),
            ("oem position", "OEM research"), ("oem position statement", "OEM research"),
            ("misc hardware", "misc hardware"), ("miscellaneous hardware", "misc hardware"),
            ("misc. hardware", "misc hardware"),
            ("steering wheel cover, seat cover, and floor mat", "steering wheel cover, seat cover, and floor mat"),
            ("steering wheel cover, seat cover", "steering wheel cover, seat cover, and floor mat"),
            ("seat cover, and floor mat", "steering wheel cover, seat cover, and floor mat"),
            ("seat cover and floor mat", "steering wheel cover, seat cover, and floor mat"),
            ("seat cover", "protective covers"), ("floor mat cover", "protective covers"),
            ("steering wheel cover", "protective covers"), ("protective cover", "protective covers"),
            ("glass cleaner", "glass cleaner"), ("windshield cleaner", "glass cleaner"),
            ("refinish material invoice", "refinish material invoice"),
            ("refinish materials invoice", "refinish material invoice"),
            ("paint material invoice", "refinish material invoice"),
            ("material invoice", "refinish material invoice"),

            // ── Mechanical ──
            ("tpms sensor", "TPMS sensor"), ("tpms", "TPMS sensor"),
            ("exhaust assembly", "exhaust assembly"), ("exhaust assy", "exhaust assembly"),
            ("catalytic converter", "catalytic converter"), ("c/cnvrtr", "catalytic converter"),
            ("muffler", "muffler"), ("resonator", "resonator"),
            ("radiator", "radiator"), ("condenser", "condenser"),
            ("a/c compressor", "A/C compressor"), ("ac compressor", "A/C compressor"),
            ("evaporator", "evaporator"),
            ("control arm", "control arm"),

            // ── Hybrid / EV ──
            ("high voltage system", "high voltage system"),
            ("high voltage", "high voltage system"), ("hv system", "high voltage system"),
            ("ev safe", "high voltage system"), ("hybrid safe", "high voltage system"),
            ("hybrid system", "hybrid system"), ("hybrid", "hybrid system"),
            ("mobile cart", "mobile cart"), ("ev battery", "EV battery"),

            // ── Suspension ──
            ("strut", "strut"), ("strut assembly", "strut"),
            ("shock absorber", "shock absorber"), ("shock assy", "shock absorber"),
            ("wheel bearing", "wheel bearing"), ("hub assembly", "hub assembly"),
            ("hub assy", "hub assembly"),
            ("tie rod", "tie rod"), ("tie rod end", "tie rod"),
            ("ball joint", "ball joint"),

            // ── Brakes ──
            ("brake rotor", "brake rotor"), ("brake disc", "brake rotor"),
            ("brake pad", "brake pad"), ("brake shoe", "brake pad"),
            ("brake caliper", "brake caliper"),

            // ── Other ──
            ("door latch", "door latch"), ("window regulator", "window regulator"),
            ("window motor", "window regulator"),
            ("wiper motor", "wiper motor"), ("wiper arm", "wiper arm"),
            ("antenna", "antenna"), ("antenna mast", "antenna"),
            ("connecting support", "connecting support")
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
                var text = ApplyOcrCorrections(line.Text.Trim());
                if (string.IsNullOrWhiteSpace(text) || text.Length < 5)
                    continue;

                // Skip UI chrome / toolbar text (CCC, Mitchell, browser, email)
                if (_uiChromePatterns.Any(chrome => text.Contains(chrome, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Skip CCC parts catalog sidebar entries (DIA codes, group codes, standalone op-type lists)
                if (Regex.IsMatch(text, @"\bDIA\d{1,2}\b"))
                    continue;
                if (Regex.IsMatch(text, @"\b(VEHI|TRON|MISC)[_.\s]{2,}"))
                    continue;
                // Skip lines that are just a list of operation types from the catalog column
                // e.g. "Repair Repair Repair Repair Repair Replace Replace..."
                if (Regex.IsMatch(text, @"^(Repair|Replace|Refinish|R&I|Blend)(\s+(Repair|Replace|Refinish|R&I|Blend)){2,}",
                    RegexOptions.IgnoreCase))
                    continue;

                // Skip lines that are purely section headers (no operation codes or numerics mixed in).
                // Strip leading digits/whitespace before checking so "3 HOOD" style sidebar items are caught,
                // but "Repl Hood 2.5 $250.00" is NOT skipped.
                var headerCandidate = Regex.Replace(text, @"^\d+\s*", "").Trim();
                if (_cccSectionHeaders.Contains(headerCandidate) && !_cccOperationCodes.Any(op =>
                    text.Contains(op, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Skip CCC/Mitchell legend text (e.g., "D&R=Disconnect and Reconnect. HSS=High Strength Steel.")
                if (Regex.IsMatch(text, @"\b\w{1,6}=\w{2,}.*\b\w{1,6}=\w{2,}"))
                    continue;

                // Skip lines containing email addresses — not estimate data
                if (Regex.IsMatch(text, @"\w+@\w+\.\w{2,}"))
                    continue;

                // Skip lines containing URLs
                if (Regex.IsMatch(text, @"https?://|www\.\w+\.\w+"))
                    continue;

                // Skip lines >150 chars — estimate lines are typically short; long lines are OCR paragraph noise
                if (text.Length > 150)
                    continue;

                // Skip CCC modifier/add-on sub-lines — these belong to the preceding parent panel operation,
                // not standalone operations (e.g., "Add for Clear Coat", "Overlap", "Major Adj. Panel",
                // "Add for Underside(Complete)", "Add for Two Tone", "Add for Tri-Coat")
                if (_cccModifierPattern.IsMatch(text))
                    continue;

                OcrDetectedOperation? operation = source switch
                {
                    OcrEstimateSource.CCCOne => ParseCCCOperation(text),
                    OcrEstimateSource.Mitchell => ParseMitchellOperation(text),
                    _ => ParseGenericOperation(text)
                };

                if (operation != null)
                {
                    // A2: Validate Pass 1 output — reject garbage fragments
                    var desc = CleanPartName(operation.Description);
                    // Empty description with only a price → garbage
                    if (string.IsNullOrWhiteSpace(desc) && operation.Price > 0 && string.IsNullOrEmpty(operation.OperationType))
                        continue;
                    // Description too short (<3 chars) → garbage
                    if (desc.Length > 0 && desc.Length < 3)
                        continue;
                    // Description is purely numeric or a price (e.g., "21.68", "$4,972.77") → garbage
                    if (Regex.IsMatch(desc, @"^\$?\d[\d,.\s]*$"))
                        continue;
                    // Description equals an operation type keyword (e.g., "Replace", "Repair") → UI label, not a part
                    if (_cccOperationCodes.Any(op => desc.Equals(op, StringComparison.OrdinalIgnoreCase)) ||
                        desc.Equals("Replace", StringComparison.OrdinalIgnoreCase) ||
                        desc.Equals("Repair", StringComparison.OrdinalIgnoreCase) ||
                        desc.Equals("Refinish", StringComparison.OrdinalIgnoreCase) ||
                        desc.Equals("Blend", StringComparison.OrdinalIgnoreCase) ||
                        desc.Equals("R&I", StringComparison.OrdinalIgnoreCase) ||
                        desc.Equals("Overhaul", StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Contains UI text that slipped through
                    if (desc.Contains("threshold", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("preliminary estimate", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("repairable", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("total loss", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("diagnostics", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("checkout", StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Description contains abbreviation definition (single ABBR=Something)
                    if (Regex.IsMatch(desc, @"\b\w{1,5}=\w{3,}"))
                        continue;
                    // Description looks like a file path or URL
                    if (desc.Contains("\\") || desc.Contains("://") || desc.Contains("www."))
                        continue;
                    // Description contains navigation text
                    if (desc.Contains("click here", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("log in", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("password", StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Single short word (<5 chars) that isn't a known part → garbage (e.g., "able", "Rpr")
                    if (!desc.Contains(' ') && desc.Length < 5 &&
                        !_knownParts.Any(kp => kp.CanonicalName.Equals(desc, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    // Hardware/part number items (clips, bolts, retainers, felt strips) → not operations
                    if (IsHardwareItem(desc))
                        continue;

                    operations.Add(operation);
                }
            }

            // Pass 2: Always run fuzzy part-name scan and merge with Pass 1 (dedup by canonical name)
            {
                // Filter out section header / diagram label lines before joining — these cause
                // false positives when CCC ONE's vehicle diagram labels (Hood, Fender, etc.) are in view.
                var filteredLines = lines.Where(l =>
                {
                    var candidate = Regex.Replace(l.Text.Trim(), @"^\d+\s*", "").Trim();
                    return !_cccSectionHeaders.Contains(candidate);
                });
                var fullTextRaw = ApplyOcrCorrections(string.Join(" ", filteredLines.Select(l => l.Text))).ToLowerInvariant();
                // Truncate at CCC parts catalog sidebar markers — everything after these is
                // available-to-add operations, NOT what's on the estimate.
                var fullText = fullTextRaw;
                var catalogMarkers = new[] { "add to estimate", "select multiple part codes", "part codes advisa" };
                foreach (var marker in catalogMarkers)
                {
                    var markerIdx = fullText.IndexOf(marker);
                    if (markerIdx > 50) // only truncate if marker isn't at the very start
                    {
                        fullText = fullText.Substring(0, markerIdx);
                        break;
                    }
                }
                var foundParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Dedup against Pass 1 results by canonical name
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

                    // Skip CCC modifier context — "add for clear coat", "add for underside" etc.
                    // These are sub-lines of the preceding panel, not standalone operations.
                    {
                        var prefixStart = Math.Max(0, patternIdx - 15);
                        var prefix = fullText.Substring(prefixStart, patternIdx - prefixStart);
                        if (Regex.IsMatch(prefix, @"(?i)add\s+(?:for\s+)?$"))
                            continue;
                    }

                    foundParts.Add(canonical);
                    matchedPatterns.Add(pattern);

                    // Try to detect operation type from nearby context
                    // Use wider window (±80 chars) because CCC grid columns put op types far from part names
                    var opType = "";
                    {
                        var start = Math.Max(0, patternIdx - 80);
                        var end = Math.Min(fullText.Length, patternIdx + pattern.Length + 50);
                        var context = fullText.Substring(start, end - start);

                        // Check "repair" before "repl" — "repair" contains substring "repl" and would false-positive
                        if (context.Contains("rpr") || context.Contains("repair"))
                            opType = "Repair";
                        else if (context.Contains("repl") || context.Contains("new") || context.Contains("r/r"))
                            opType = "Replace";
                        else if (context.Contains("r&i") || context.Contains("r+i") || context.Contains("remove"))
                            opType = "R&I";
                        else if (context.Contains("refn") || context.Contains("refinish") || context.Contains("paint"))
                            opType = "Refinish";
                        else if (context.Contains("blend") || context.Contains("blnd"))
                            opType = "Blend";
                    }

                    // A4: Extract nearby price and labor hours from ~80 chars after the mention
                    decimal nearbyPrice = 0;
                    decimal nearbyHours = 0;
                    {
                        var scanStart = patternIdx;
                        var scanEnd = Math.Min(fullText.Length, patternIdx + pattern.Length + 80);
                        var nearby = fullText.Substring(scanStart, scanEnd - scanStart);
                        // Price: $xxx.xx or xxx.xx preceded by $ context
                        var priceMatch = _pricePattern.Match(nearby);
                        if (priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value.Replace(",", ""), out var pv))
                            nearbyPrice = pv;
                        // Hours: bare decimal 0.1–50
                        foreach (Match hm in _bareDecimalPattern.Matches(nearby))
                        {
                            if (decimal.TryParse(hm.Groups[1].Value, out var hv) && hv >= 0.1m && hv <= 50m && hv != nearbyPrice)
                            {
                                nearbyHours = hv;
                                break;
                            }
                        }
                    }

                    // Override operation type for diagnostic/scan items — context-based guessing
                    // often misclassifies these (e.g., "pre-repair scan" → "Repair" due to "repair" substring)
                    if (_diagnosticCanonicalNames.Contains(canonical))
                        opType = "Sublet";
                    // Default op type for must-have/shop operations that appear in misc-charges sections
                    // without standard op-type keywords nearby — prevents LiveCoachingService filtering
                    else if (string.IsNullOrEmpty(opType) && _mustHaveCanonicalNames.Contains(canonical))
                        opType = "Rpr";

                    operations.Add(new OcrDetectedOperation
                    {
                        Description = canonical,
                        PartName = canonical,
                        OperationType = opType,
                        RawLine = $"[detected from screen: {canonical}]",
                        LaborHours = nearbyHours,
                        Price = nearbyPrice
                    });
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

                // Part number: 3+ chars, has both letters and digits, no dot (CCC codes like FR1, LS1, AB2)
                if (partNumber == null && token.Length >= 3 &&
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

                // Skip labor type markers, "Incl", and status words
                if (token == "M" || token == "m" ||
                    token.Equals("Incl", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Incl.", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("New", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Existing", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("No", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip pure-digit tokens that didn't match hours/price/qty rules (OCR noise)
                if (Regex.IsMatch(token, @"^\d+$"))
                    continue;

                descParts.Add(token);
            }

            var description = CleanPartName(string.Join(" ", descParts));
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

                // Part number: 3+ chars (CCC codes like FR1, LS1, AB2)
                if (partNumber == null && token.Length >= 3 &&
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

                // Skip labor type markers and status words
                if (token == "Body" || token == "Body*" || token == "Refinish" || token == "Mechanical" ||
                    token == "Refinish*" || token == "Mechanical*" ||
                    token.Equals("New", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Existing", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("No", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip pure-digit tokens that didn't match hours/price/qty rules (OCR noise)
                if (Regex.IsMatch(token, @"^\d+$"))
                    continue;

                descParts.Add(token);
            }

            var description = CleanPartName(string.Join(" ", descParts));
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
            cleaned = CleanPartName(cleaned);
            return cleaned.Length > 2 ? cleaned : text.Trim();
        }

        /// <summary>
        /// Strips leading/trailing digits, stray punctuation, and excess whitespace from a part name.
        /// </summary>
        private static string CleanPartName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            // Strip leading digits and whitespace (e.g., "4 Front Bumper" → "Front Bumper")
            var cleaned = Regex.Replace(name, @"^\s*\d+\s+", "");
            // Strip trailing standalone digits (e.g., "Hood 1" → "Hood")
            cleaned = Regex.Replace(cleaned, @"\s+\d+\s*$", "");
            // Strip stray asterisks and trailing punctuation from OCR
            cleaned = cleaned.Replace("*", "").Trim().TrimEnd(',', '.', ';', ':');
            // Collapse whitespace
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
            return cleaned;
        }

        /// <summary>
        /// Applies common OCR misread corrections to raw text before pattern matching.
        /// </summary>
        internal static string ApplyOcrCorrections(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var lower = text.ToLowerInvariant();
            foreach (var (wrong, right) in _ocrCorrections)
            {
                if (lower.Contains(wrong))
                {
                    // Case-insensitive replace
                    var idx = lower.IndexOf(wrong);
                    while (idx >= 0)
                    {
                        text = text.Remove(idx, wrong.Length).Insert(idx, right);
                        lower = text.ToLowerInvariant();
                        idx = lower.IndexOf(wrong, idx + right.Length);
                    }
                }
            }
            return text;
        }

        /// <summary>
        /// Returns true if the description looks like a hardware/part number, not an operation.
        /// </summary>
        private static bool IsHardwareItem(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return false;
            var lower = description.ToLowerInvariant() + " "; // trailing space for boundary matching
            return _hardwareKeywords.Any(kw => lower.Contains(kw));
        }

        /// <summary>
        /// Resets the change detection baseline.
        /// </summary>
        public void ResetChangeTracking()
        {
            _previousRawText = null;
        }

        /// <summary>
        /// Attempt AI-powered OCR text cleanup for better structured parsing.
        /// Returns null on failure — caller should use fallback regex results.
        /// </summary>
        private async Task<List<OcrDetectedOperation>?> TryAiOcrCleanupAsync(string rawText, OcrEstimateSource source)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawText) || rawText.Length < 20)
                    return null;

                var apiService = McStudDesktop.Services.ClaudeApiService.Instance;
                var systemPrompt = @"You are a collision repair estimate parser. Extract structured operations from raw OCR text captured from estimating software (CCC ONE, Mitchell, Audatex).

Return a JSON array of operations. Each operation object has:
- ""description"": full operation description
- ""operationType"": one of: Repl, Rpr, Refn, R&I, Blnd, Add, O/H, Subl, Mech, Body, New
- ""partName"": the part/panel name (e.g. ""front bumper cover"", ""hood"", ""fender"")
- ""laborHours"": decimal labor hours (0 if not found)
- ""refinishHours"": decimal refinish hours (0 if not found)
- ""price"": decimal price (0 if not found)

Rules:
- Skip UI chrome, headers, footers, and navigation text
- Skip empty or purely numeric lines
- Normalize part names to lowercase
- Return ONLY the JSON array, no markdown or explanation";

                var userMessage = $"Source: {source}\n\nRaw OCR text:\n{rawText}";

                // Limit input to avoid excessive token usage
                if (userMessage.Length > 4000)
                    userMessage = userMessage.Substring(0, 4000);

                var response = await apiService.SendAsync(systemPrompt, userMessage, McStudDesktop.Services.AiFeature.OcrCleanup, 2048);
                if (response == null) return null;

                // Parse the JSON response
                var text = McStudDesktop.Services.ClaudeApiService.StripCodeFences(response.Text);

                var ops = System.Text.Json.JsonSerializer.Deserialize<List<AiOcrOperation>>(text, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (ops == null || ops.Count == 0) return null;

                var result = new List<OcrDetectedOperation>();
                foreach (var op in ops)
                {
                    if (string.IsNullOrWhiteSpace(op.Description) && string.IsNullOrWhiteSpace(op.PartName))
                        continue;

                    result.Add(new OcrDetectedOperation
                    {
                        Description = op.Description ?? "",
                        OperationType = op.OperationType ?? "",
                        PartName = op.PartName ?? "",
                        LaborHours = op.LaborHours,
                        RefinishHours = op.RefinishHours,
                        Price = op.Price,
                        RawLine = op.Description ?? ""
                    });
                }

                return result.Count > 0 ? result : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenOCR] AI cleanup failed (using fallback): {ex.Message}");
                return null;
            }
        }

        private class AiOcrOperation
        {
            public string? Description { get; set; }
            public string? OperationType { get; set; }
            public string? PartName { get; set; }
            public decimal LaborHours { get; set; }
            public decimal RefinishHours { get; set; }
            public decimal Price { get; set; }
        }
    }
}
