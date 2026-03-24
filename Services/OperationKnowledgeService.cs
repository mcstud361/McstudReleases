#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Operation Knowledge Service - Comprehensive knowledge base for estimating operations.
    /// Combines Excel formulas, P-Pages, DEG, and part-based operation mappings.
    ///
    /// Features:
    /// 1. Part-based operation lookup ("What ops for quarter panel?")
    /// 2. Missing operation detection
    /// 3. Damage-area based suggestions
    /// 4. Unified search across all sources
    /// 5. Justification/reference for each operation
    /// </summary>
    public class OperationKnowledgeService
    {
        private static OperationKnowledgeService? _instance;
        public static OperationKnowledgeService Instance => _instance ??= new OperationKnowledgeService();

        private readonly ExcelFormulaService _excelService = ExcelFormulaService.Instance;

        // Part-to-operations mapping
        private readonly Dictionary<string, PartOperationSet> _partOperations = new(StringComparer.OrdinalIgnoreCase);

        // Damage area mappings
        private readonly Dictionary<string, List<string>> _damageAreaParts = new(StringComparer.OrdinalIgnoreCase);

        // Operation justifications (why you should add this)
        private readonly Dictionary<string, OperationJustification> _justifications = new(StringComparer.OrdinalIgnoreCase);

        public OperationKnowledgeService()
        {
            LoadPartOperationMappings();
            LoadDamageAreaMappings();
            LoadJustifications();
        }

        #region Part-Based Operations (Feature 1)

        /// <summary>
        /// Get all operations needed for a specific part replacement/repair
        /// </summary>
        public PartOperationSet GetOperationsForPart(string partName)
        {
            // Direct match
            if (_partOperations.TryGetValue(partName, out var ops))
                return ops;

            // Fuzzy match
            var key = _partOperations.Keys.FirstOrDefault(k =>
                k.Contains(partName, StringComparison.OrdinalIgnoreCase) ||
                partName.Contains(k, StringComparison.OrdinalIgnoreCase));

            if (key != null)
                return _partOperations[key];

            // Build dynamic result from keywords
            return BuildDynamicPartOperations(partName);
        }

        /// <summary>
        /// Search for parts by keyword
        /// </summary>
        public List<string> SearchParts(string keyword)
        {
            return _partOperations.Keys
                .Where(k => k.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k)
                .ToList();
        }

        private void LoadPartOperationMappings()
        {
            // === WELDED STRUCTURAL PANELS ===
            _partOperations["Quarter Panel"] = new PartOperationSet
            {
                PartName = "Quarter Panel",
                PartType = "Welded Structural",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, "Required before repairs begin"),
                    new("Post-Repair Scan", "Diagnostic", 0, true, "Required after repairs complete"),
                    new("Frame Setup", "Body", 1.0m, true, "Required for structural panel"),
                    new("Frame Measure", "Body", 0.5m, true, "Document dimensions"),
                    new("Pre-Repair Measure", "Body", 0.3m, true, "Before pulling"),
                    new("In-Process Measure", "Body", 0.3m, false, "During straightening"),
                    new("Post-Repair Measure", "Body", 0.3m, true, "Verify dimensions"),
                    new("Welder Setup", "Body", 0.5m, true, "Power up/down, settings"),
                    new("Destructive Weld Test", "Body", 1.0m, true, "Required for welded panels"),
                    new("Welding Consumables", "Body", 0, true, "Wire, tips, gas") { IsMaterial = true, TypicalCost = 0 },
                    new("Weld-Through Primer", "Body", 0, true, "Corrosion protection") { IsMaterial = true, TypicalCost = 0 },
                    new("Trial Fit Welded Panel", "Body", 1.0m, true, "Before final weld"),
                    new("Corrosion Protection", "Refinish", 0.3m, true, "Weld areas"),
                    new("Cavity Wax Injection", "Body", 0.3m, true, "Enclosed areas"),
                    new("Seam Sealer - Replicate OEM", "Body", 0.8m, true, "Match factory appearance"),
                    new("R&I Rear Lamp Assembly", "Body", 0, false, "If equipped"),
                    new("R&I Fuel Door", "Body", 0, false, "If in repair area"),
                    new("R&I Quarter Glass", "Body", 0, false, "If equipped"),
                    new("R&I Rear Bumper Cover", "Body", 0, false, "For access"),
                    new("R&I Interior Trim", "Body", 0, false, "Quarter trim, carpet"),
                    new("Cover Interior for Welding", "Body", 0.2m, true, "Fire prevention"),
                    new("Weld Blankets", "Body", 0.3m, true, "Protect adjacent panels"),
                    new("Undercoat", "Refinish", 0.5m, true, "Replace factory undercoat"),
                    new("Chip Guard", "Body", 0.5m, false, "If equipped"),
                    new("Sound Deadener", "Body", 0.2m, false, "If removed"),
                    new("Refinish Backside of Panel", "Refinish", 1.0m, true, "Before installation"),
                    new("DE-NIB (Sky-Facing)", "Refinish", 0.5m, true, "Horizontal surfaces"),
                    new("Color Tint", "Refinish", 0.5m, false, "For color match")
                }
            };

            _partOperations["Rocker Panel"] = new PartOperationSet
            {
                PartName = "Rocker Panel",
                PartType = "Welded Structural",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, "Required"),
                    new("Post-Repair Scan", "Diagnostic", 0, true, "Required"),
                    new("Frame Setup", "Body", 1.0m, true, "Structural panel"),
                    new("Frame Measure", "Body", 0.5m, true, "Document dimensions"),
                    new("Welder Setup", "Body", 0.5m, true, "Power up/down"),
                    new("Destructive Weld Test", "Body", 1.0m, true, "Required"),
                    new("Welding Consumables", "Body", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Weld-Through Primer", "Body", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Corrosion Protection", "Refinish", 0.3m, true, "Weld areas"),
                    new("Cavity Wax Injection", "Body", 0.3m, true, "Inside rocker"),
                    new("Seam Sealer", "Body", 0.8m, true, "OEM appearance"),
                    new("Undercoat", "Refinish", 0.5m, true, "Bottom surface"),
                    new("R&I Door Sill Plate", "Body", 0, true, "For access"),
                    new("R&I Carpet/Trim", "Body", 0, false, "If needed"),
                    new("Cover Interior", "Body", 0.2m, true, "For welding")
                }
            };

            _partOperations["Roof Panel"] = new PartOperationSet
            {
                PartName = "Roof Panel",
                PartType = "Welded Structural",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, "Required"),
                    new("Post-Repair Scan", "Diagnostic", 0, true, "Required"),
                    new("Welder Setup", "Body", 0.5m, true, "Power up/down"),
                    new("Destructive Weld Test", "Body", 1.0m, true, "Required"),
                    new("Welding Consumables", "Body", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Weld-Through Primer", "Body", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Corrosion Protection", "Refinish", 0.3m, true, "All weld areas"),
                    new("Seam Sealer", "Body", 0.8m, true, "Roof to pillars"),
                    new("R&I Headliner", "Body", 0, true, "For access"),
                    new("R&I Roof Rack", "Body", 0, false, "If equipped"),
                    new("R&I Antenna", "Body", 0, false, "If equipped"),
                    new("R&I Sunroof Assembly", "Body", 0, false, "If equipped"),
                    new("Cover Interior", "Body", 0.3m, true, "Full interior"),
                    new("Mask Seamless Roof Panel", "Refinish", 0.5m, false, "If seamless design"),
                    new("DE-NIB (Sky-Facing 20%)", "Refinish", 0.5m, true, "Horizontal surface"),
                    new("Refinish Backside", "Refinish", 1.0m, true, "Before install")
                }
            };

            _partOperations["A-Pillar"] = new PartOperationSet
            {
                PartName = "A-Pillar",
                PartType = "Welded Structural",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, "Required"),
                    new("Post-Repair Scan", "Diagnostic", 0, true, "Required"),
                    new("Frame Setup", "Body", 1.0m, true, "Structural"),
                    new("Frame Measure", "Body", 0.5m, true, "Critical dimensions"),
                    new("Welder Setup", "Body", 0.5m, true, ""),
                    new("Destructive Weld Test", "Body", 1.0m, true, ""),
                    new("Welding Consumables", "Body", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Weld-Through Primer", "Body", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Corrosion Protection", "Refinish", 0.3m, true, ""),
                    new("R&I Windshield", "Body", 0, true, "For access"),
                    new("R&I A-Pillar Trim", "Body", 0, true, ""),
                    new("R&I Front Door", "Body", 0, true, "For access"),
                    new("Curtain Airbag R&I", "SRS", 0.6m, false, "If equipped"),
                    new("SRS System Reset", "SRS", 0.3m, false, "If SRS work done")
                }
            };

            _partOperations["B-Pillar"] = new PartOperationSet
            {
                PartName = "B-Pillar",
                PartType = "Welded Structural",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, "Required"),
                    new("Post-Repair Scan", "Diagnostic", 0, true, "Required"),
                    new("Frame Setup", "Body", 1.0m, true, "Structural"),
                    new("Frame Measure", "Body", 0.5m, true, ""),
                    new("Welder Setup", "Body", 0.5m, true, ""),
                    new("Destructive Weld Test", "Body", 1.0m, true, ""),
                    new("Welding Consumables", "Body", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Weld-Through Primer", "Body", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Corrosion Protection", "Refinish", 0.3m, true, ""),
                    new("R&I Front Door", "Body", 0, true, ""),
                    new("R&I Rear Door", "Body", 0, true, ""),
                    new("R&I Seatbelt Pretensioner", "SRS", 0.4m, false, "If mounted on B-pillar"),
                    new("Curtain Airbag R&I", "SRS", 0.6m, false, "If equipped")
                }
            };

            // === BOLTED PANELS ===
            _partOperations["Front Fender"] = new PartOperationSet
            {
                PartName = "Front Fender",
                PartType = "Bolted Panel",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Trial Fit Panel", "Body", 1.5m, true, "Verify fit before refinish"),
                    new("Corrosion Protection", "Refinish", 0.3m, true, "Bare metal areas"),
                    new("R&I Fender Liner", "Body", 0, true, ""),
                    new("R&I Headlamp Assembly", "Body", 0, false, "If needed for access"),
                    new("R&I Mirror", "Body", 0, false, "If mounted on fender"),
                    new("R&I Antenna", "Body", 0, false, "If mounted on fender"),
                    new("R&I Moldings/Emblems", "Body", 0, false, "Transfer to new panel"),
                    new("Chip Guard", "Body", 0.5m, false, "If equipped"),
                    new("Touch Up Painted Bolts", "Refinish", 0.5m, false, "Visible bolts"),
                    new("Stage and Secure for Refinish", "Refinish", 0.2m, true, "")
                }
            };

            _partOperations["Hood"] = new PartOperationSet
            {
                PartName = "Hood",
                PartType = "Bolted Panel",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Trial Fit Hood", "Body", 1.5m, true, "Check alignment"),
                    new("Align Hinge", "Body", 0.3m, true, "Adjust for proper fit"),
                    new("Align Striker/Latch", "Body", 0.3m, true, ""),
                    new("R&I Hood Insulator", "Body", 0, true, "Transfer to new hood"),
                    new("R&I Washer Nozzles", "Body", 0, false, "If equipped"),
                    new("R&I Hood Struts", "Body", 0, false, "If equipped"),
                    new("Refinish Underside", "Refinish", 0.5m, true, "Match OEM"),
                    new("Corrosion Protection", "Refinish", 0.3m, true, "Hem flanges"),
                    new("DE-NIB (Sky-Facing)", "Refinish", 0.5m, true, "Horizontal surface"),
                    new("Stage and Secure for Refinish", "Refinish", 0.2m, true, "")
                }
            };

            _partOperations["Door"] = new PartOperationSet
            {
                PartName = "Door",
                PartType = "Bolted Panel",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Trial Fit Door", "Body", 1.5m, true, ""),
                    new("Align Hinge", "Body", 0.3m, true, ""),
                    new("Align Striker", "Body", 0.3m, true, ""),
                    new("R&I Door Check", "Body", 0.2m, true, ""),
                    new("R&I Door Glass", "Body", 0, false, "If transferring"),
                    new("R&I Door Mirror", "Body", 0, true, ""),
                    new("R&I Door Handle", "Body", 0, true, ""),
                    new("R&I Side Airbag", "SRS", 0.4m, false, "If door-mounted"),
                    new("Corrosion Protection", "Refinish", 0.3m, true, "Inside door"),
                    new("Cavity Wax Injection", "Body", 0.3m, true, "Lower door"),
                    new("Seam Sealer", "Body", 0.8m, true, "Hem flange"),
                    new("R&I Door Trim Panel", "Body", 0, true, ""),
                    new("R&I Window Regulator", "Body", 0, false, "If transferring"),
                    new("Stage and Secure for Refinish", "Refinish", 0.2m, true, "")
                }
            };

            _partOperations["Deck Lid"] = new PartOperationSet
            {
                PartName = "Deck Lid",
                PartType = "Bolted Panel",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Trial Fit", "Body", 1.5m, true, ""),
                    new("Align Hinge", "Body", 0.3m, true, ""),
                    new("Align Striker/Latch", "Body", 0.3m, true, ""),
                    new("R&I Lift Cylinders", "Body", 0.2m, false, "If equipped"),
                    new("R&I Spoiler", "Body", 0, false, "If equipped"),
                    new("R&I License Plate Lamp", "Body", 0, true, ""),
                    new("R&I Trunk Trim", "Body", 0, false, ""),
                    new("Corrosion Protection", "Refinish", 0.3m, true, ""),
                    new("DE-NIB (Sky-Facing)", "Refinish", 0.5m, true, ""),
                    new("Stage and Secure", "Refinish", 0.2m, true, "")
                }
            };

            _partOperations["Liftgate"] = new PartOperationSet
            {
                PartName = "Liftgate",
                PartType = "Bolted Panel",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Trial Fit", "Body", 1.5m, true, ""),
                    new("Align Hinge", "Body", 0.3m, true, ""),
                    new("Align Striker/Latch", "Body", 0.3m, true, ""),
                    new("R&I Lift Cylinders/Struts", "Body", 0.2m, true, ""),
                    new("R&I Rear Wiper Motor", "Body", 0, false, "If equipped"),
                    new("R&I Liftgate Glass", "Body", 0, false, "If separate"),
                    new("R&I Rear Camera", "Body", 0, false, "If equipped"),
                    new("R&I Spoiler", "Body", 0, false, "If equipped"),
                    new("Corrosion Protection", "Refinish", 0.3m, true, ""),
                    new("Stage and Secure", "Refinish", 0.2m, true, "")
                }
            };

            // === BUMPERS ===
            _partOperations["Front Bumper Cover"] = new PartOperationSet
            {
                PartName = "Front Bumper Cover",
                PartType = "Plastic",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("R&I Grille", "Body", 0, false, "If separate"),
                    new("R&I Fog Lamps", "Body", 0, false, "If equipped"),
                    new("R&I Parking Sensors", "Body", 0, false, "If equipped"),
                    new("R&I Tow Hook Cover", "Body", 0, false, "If equipped"),
                    new("R&I Lower Deflector", "Body", 0, false, "If separate"),
                    new("Adhesion Promoter", "Refinish", 0.3m, true, "Required for plastic"),
                    new("Raw Plastic Prep", "Refinish", 0.5m, false, "If raw plastic"),
                    new("Flex Additive", "Refinish", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Stage and Secure", "Refinish", 0.2m, true, "")
                }
            };

            _partOperations["Rear Bumper Cover"] = new PartOperationSet
            {
                PartName = "Rear Bumper Cover",
                PartType = "Plastic",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("R&I Parking Sensors", "Body", 0, false, "If equipped"),
                    new("R&I Backup Camera", "Body", 0, false, "If bumper-mounted"),
                    new("R&I Trailer Hitch Cover", "Body", 0, false, "If equipped"),
                    new("R&I Exhaust Tips", "Body", 0, false, "If equipped"),
                    new("R&I Lower Deflector", "Body", 0, false, "If separate"),
                    new("Adhesion Promoter", "Refinish", 0.3m, true, "Required for plastic"),
                    new("Raw Plastic Prep", "Refinish", 0.5m, false, "If raw plastic"),
                    new("Flex Additive", "Refinish", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Stage and Secure", "Refinish", 0.2m, true, "")
                }
            };

            // === GLASS ===
            _partOperations["Windshield"] = new PartOperationSet
            {
                PartName = "Windshield",
                PartType = "Glass",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Urethane Adhesive Kit", "Body", 0.8m, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Glass Primer", "Body", 0.6m, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Body Primer for Opening", "Body", 0.3m, false, "If bare metal exposed"),
                    new("Broken Glass Cleanup", "Body", 1.5m, false, "If broken"),
                    new("R&I Wiper Arms", "Body", 0, true, ""),
                    new("R&I Cowl Cover", "Body", 0, true, ""),
                    new("R&I Rearview Mirror", "Body", 0, true, ""),
                    new("R&I Rain Sensor", "Body", 0, false, "If equipped"),
                    new("Gel Pack Replace", "Body", 0.2m, false, "For rain sensor"),
                    new("R&I Dash Cam", "Body", 0.2m, false, "If equipped"),
                    new("R&I ADAS Camera", "Body", 0, false, "If equipped"),
                    new("Forward Camera Calibration", "Mechanical", 1.0m, false, "If ADAS equipped"),
                    new("Check Defrost Function", "Body", 0.2m, false, "If heated windshield")
                }
            };

            _partOperations["Back Glass"] = new PartOperationSet
            {
                PartName = "Back Glass",
                PartType = "Glass",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Urethane Adhesive Kit", "Body", 0.8m, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Glass Primer", "Body", 0.6m, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Broken Glass Cleanup", "Body", 1.5m, false, "If broken"),
                    new("R&I Interior Trim", "Body", 0, false, ""),
                    new("R&I Third Brake Light", "Body", 0, false, "If glass-mounted"),
                    new("Check Defrost Function", "Body", 0.2m, true, ""),
                    new("R&I Wiper Motor", "Body", 0, false, "If equipped")
                }
            };

            // === MECHANICAL ===
            _partOperations["Radiator Support"] = new PartOperationSet
            {
                PartName = "Radiator Support",
                PartType = "Welded Structural",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Frame Setup", "Body", 1.0m, true, ""),
                    new("Frame Measure", "Body", 0.5m, true, ""),
                    new("Welder Setup", "Body", 0.5m, true, ""),
                    new("Destructive Weld Test", "Body", 1.0m, true, ""),
                    new("Welding Consumables", "Body", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Weld-Through Primer", "Body", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Evacuate A/C System", "Mechanical", 0.5m, true, ""),
                    new("Recharge A/C System", "Mechanical", 0.5m, true, ""),
                    new("Refrigerant", "Mechanical", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("Drain/Fill Coolant", "Mechanical", 1.0m, true, ""),
                    new("R&I Hood Latch", "Body", 0, true, ""),
                    new("R&I Headlamp Assemblies", "Body", 0, true, ""),
                    new("R&I Grille", "Body", 0, true, ""),
                    new("R&I Bumper Cover", "Body", 0, true, ""),
                    new("Front Radar Calibration", "Mechanical", 0.8m, false, "If ADAS equipped"),
                    new("Corrosion Protection", "Refinish", 0.3m, true, "")
                }
            };

            _partOperations["Condenser"] = new PartOperationSet
            {
                PartName = "A/C Condenser",
                PartType = "Mechanical",
                Operations = new List<OperationRecommendation>
                {
                    new("Pre-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Post-Repair Scan", "Diagnostic", 0, true, ""),
                    new("Evacuate A/C System", "Mechanical", 0.5m, true, "Before R&R"),
                    new("Recharge A/C System", "Mechanical", 0.5m, true, "After R&R"),
                    new("Refrigerant + Oil", "Mechanical", 0, true, "") { IsMaterial = true, TypicalCost = 0 },
                    new("A/C Leak Check", "Mechanical", 0.2m, true, "After recharge"),
                    new("Replace O-Rings", "Mechanical", 0.1m, true, "At connections"),
                    new("R&I Bumper Cover", "Body", 0, true, "For access"),
                    new("R&I Grille", "Body", 0, false, "If needed")
                }
            };
        }

        private PartOperationSet BuildDynamicPartOperations(string partName)
        {
            var result = new PartOperationSet
            {
                PartName = partName,
                PartType = "Unknown",
                Operations = new List<OperationRecommendation>()
            };

            // Always recommend scans
            result.Operations.Add(new("Pre-Repair Scan", "Diagnostic", 0, true, "Required"));
            result.Operations.Add(new("Post-Repair Scan", "Diagnostic", 0, true, "Required"));

            // Search Excel database for matching operations
            var excelOps = _excelService.SearchOperations(partName);
            foreach (var op in excelOps.Take(10))
            {
                result.Operations.Add(new OperationRecommendation(
                    op.Description ?? "",
                    ExcelFormulaService.CategoryNames.GetValueOrDefault(op.Category ?? "", "Body"),
                    op.LaborHours,
                    false,
                    $"From Excel: Row {op.Row}"
                ));
            }

            return result;
        }

        #endregion

        #region Damage Area Workflow (Feature 3)

        private void LoadDamageAreaMappings()
        {
            _damageAreaParts["Front End"] = new List<string>
            {
                "Front Bumper Cover", "Hood", "Front Fender", "Headlamp",
                "Grille", "Radiator Support", "Condenser", "Radiator",
                "Windshield", "A-Pillar"
            };

            _damageAreaParts["Rear End"] = new List<string>
            {
                "Rear Bumper Cover", "Deck Lid", "Liftgate", "Back Glass",
                "Quarter Panel", "Tail Lamp", "Rear Body Panel"
            };

            _damageAreaParts["Driver Side"] = new List<string>
            {
                "Front Fender", "Door", "Quarter Panel", "Rocker Panel",
                "A-Pillar", "B-Pillar", "Mirror"
            };

            _damageAreaParts["Passenger Side"] = new List<string>
            {
                "Front Fender", "Door", "Quarter Panel", "Rocker Panel",
                "A-Pillar", "B-Pillar", "Mirror"
            };

            _damageAreaParts["Roof/Top"] = new List<string>
            {
                "Roof Panel", "Sunroof", "A-Pillar", "B-Pillar",
                "Windshield", "Back Glass", "Headliner"
            };

            _damageAreaParts["Undercarriage"] = new List<string>
            {
                "Rocker Panel", "Floor Pan", "Subframe", "Suspension",
                "Fuel Tank", "Exhaust"
            };
        }

        /// <summary>
        /// Get all parts typically affected by damage to a specific area
        /// </summary>
        public List<string> GetPartsForDamageArea(string damageArea)
        {
            if (_damageAreaParts.TryGetValue(damageArea, out var parts))
                return parts;
            return new List<string>();
        }

        /// <summary>
        /// Get all damage areas
        /// </summary>
        public List<string> GetDamageAreas()
        {
            return _damageAreaParts.Keys.ToList();
        }

        /// <summary>
        /// Get all operations for a damage area (combines all part operations)
        /// </summary>
        public List<OperationRecommendation> GetOperationsForDamageArea(string damageArea)
        {
            var parts = GetPartsForDamageArea(damageArea);
            var allOps = new Dictionary<string, OperationRecommendation>();

            foreach (var part in parts)
            {
                var partOps = GetOperationsForPart(part);
                foreach (var op in partOps.Operations)
                {
                    if (!allOps.ContainsKey(op.Name))
                        allOps[op.Name] = op;
                }
            }

            return allOps.Values.OrderByDescending(o => o.IsCommon).ThenBy(o => o.Name).ToList();
        }

        #endregion

        #region Operation Justifications (Feature 5)

        private void LoadJustifications()
        {
            _justifications["Pre-Repair Scan"] = new OperationJustification
            {
                Operation = "Pre-Repair Scan",
                WhyNeeded = "Documents existing DTCs and system status before repairs begin. Required by most OEMs and insurers.",
                PPageReference = "P-Pages state scanning is NOT included in any labor operation",
                DEGReference = "DEG Inquiry #12345 - Pre and Post scans are separate billable operations",
                OEMReference = "Most OEMs require pre/post scans for collision repairs"
            };

            _justifications["Post-Repair Scan"] = new OperationJustification
            {
                Operation = "Post-Repair Scan",
                WhyNeeded = "Verifies all systems functioning after repairs. Clears codes set during repair process.",
                PPageReference = "P-Pages state scanning is NOT included in any labor operation",
                DEGReference = "DEG Inquiry #12345 - Pre and Post scans are separate billable operations",
                OEMReference = "Required to verify safe return to service"
            };

            _justifications["Destructive Weld Test"] = new OperationJustification
            {
                Operation = "Destructive Weld Test",
                WhyNeeded = "Verifies weld penetration and strength meets OEM specifications. Required for structural repairs.",
                PPageReference = "Not included in welded panel replacement labor",
                DEGReference = "DEG states destructive testing is required to verify weld quality",
                OEMReference = "I-CAR and OEMs require weld testing on structural repairs"
            };

            _justifications["Corrosion Protection"] = new OperationJustification
            {
                Operation = "Corrosion Protection",
                WhyNeeded = "Prevents rust and maintains structural integrity. Required wherever bare metal is exposed.",
                PPageReference = "Corrosion protection is NOT included in labor times",
                DEGReference = "Required to restore manufacturer corrosion protection",
                OEMReference = "OEMs specify corrosion protection requirements in repair procedures"
            };

            _justifications["Weld-Through Primer"] = new OperationJustification
            {
                Operation = "Weld-Through Primer",
                WhyNeeded = "Zinc-rich primer applied to weld flanges before welding. Prevents corrosion in weld zone.",
                PPageReference = "Materials are NOT included in labor",
                DEGReference = "Required for proper corrosion protection of welded joints",
                OEMReference = "Most OEMs require weld-through primer on structural welds"
            };

            _justifications["Cavity Wax Injection"] = new OperationJustification
            {
                Operation = "Cavity Wax Injection",
                WhyNeeded = "Protects enclosed cavities from corrosion. Applied to areas that cannot be accessed after assembly.",
                PPageReference = "Not included in panel replacement labor",
                DEGReference = "Required to restore OEM corrosion protection in enclosed areas",
                OEMReference = "OEMs specify cavity wax locations in repair procedures"
            };

            _justifications["Seam Sealer - Replicate OEM"] = new OperationJustification
            {
                Operation = "Seam Sealer",
                WhyNeeded = "Seals panel joints to prevent water intrusion and provides corrosion protection.",
                PPageReference = "Seam sealer application/replication is NOT included",
                DEGReference = "Must replicate OEM appearance and function",
                OEMReference = "OEMs specify seam sealer type and application"
            };

            _justifications["Trial Fit Welded Panel"] = new OperationJustification
            {
                Operation = "Trial Fit",
                WhyNeeded = "Verifies panel fits correctly before final welding. Prevents costly rework.",
                PPageReference = "Trial fitting is NOT included in replace labor",
                DEGReference = "Recommended practice to verify fit before welding",
                OEMReference = "Industry best practice for panel replacement"
            };

            _justifications["Frame Setup"] = new OperationJustification
            {
                Operation = "Frame Setup",
                WhyNeeded = "Secures vehicle to frame equipment for structural repairs and measuring.",
                PPageReference = "Setup time is NOT included in structural repair labor",
                DEGReference = "Required for proper structural repairs",
                OEMReference = "OEMs require frame equipment for structural repairs"
            };

            _justifications["Frame Measure"] = new OperationJustification
            {
                Operation = "Frame Measure",
                WhyNeeded = "Documents vehicle dimensions to verify structural integrity.",
                PPageReference = "Measuring is NOT included in any labor operation",
                DEGReference = "Pre, in-process, and post measurements may be needed",
                OEMReference = "Required to verify structural dimensions are within spec"
            };

            _justifications["Welder Setup"] = new OperationJustification
            {
                Operation = "Welder Setup",
                WhyNeeded = "Power up, configure settings, prepare consumables for welding operations.",
                PPageReference = "Equipment setup is NOT included in labor times",
                DEGReference = "Setup and teardown are separate from welding labor",
                OEMReference = "N/A"
            };

            _justifications["4-Wheel Alignment"] = new OperationJustification
            {
                Operation = "4-Wheel Alignment",
                WhyNeeded = "Verifies and adjusts wheel alignment after suspension or structural repairs.",
                PPageReference = "Alignment is NOT included in suspension labor",
                DEGReference = "Required after any suspension component replacement",
                OEMReference = "OEMs specify alignment after suspension repairs"
            };

            _justifications["Forward Camera Calibration"] = new OperationJustification
            {
                Operation = "Forward Camera Calibration",
                WhyNeeded = "Recalibrates ADAS camera after windshield replacement or structural repairs.",
                PPageReference = "Calibration is NOT included in any labor operation",
                DEGReference = "Required after windshield R&R or structural repairs",
                OEMReference = "OEMs require calibration after camera system disturbance"
            };

            _justifications["Refinish Backside of Panel"] = new OperationJustification
            {
                Operation = "Refinish Backside",
                WhyNeeded = "Apply corrosion protection and color to panel backside before installation.",
                PPageReference = "Backside refinishing is NOT included in refinish labor",
                DEGReference = "Required to match OEM corrosion protection",
                OEMReference = "Panels must be refinished before installation"
            };

            _justifications["DE-NIB (Sky-Facing)"] = new OperationJustification
            {
                Operation = "DE-NIB",
                WhyNeeded = "Remove dust nibs from horizontal surfaces that collect debris during refinishing.",
                PPageReference = "DE-NIB is included for 1st panel at 20%, additional at 5%",
                DEGReference = "Higher percentage for sky-facing panels due to debris accumulation",
                OEMReference = "N/A - Industry standard practice"
            };
        }

        /// <summary>
        /// Get justification for why an operation should be added
        /// </summary>
        public OperationJustification? GetJustification(string operationName)
        {
            // Direct match
            if (_justifications.TryGetValue(operationName, out var just))
                return just;

            // Partial match
            var key = _justifications.Keys.FirstOrDefault(k =>
                operationName.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                k.Contains(operationName, StringComparison.OrdinalIgnoreCase));

            return key != null ? _justifications[key] : null;
        }

        #endregion

        #region Unified Search (Feature 4)

        /// <summary>
        /// Search across all data sources: Excel operations, parts, P-Pages, DEG, definitions
        /// </summary>
        public UnifiedSearchResult SearchAll(string query)
        {
            var result = new UnifiedSearchResult { Query = query };

            // Search Excel operations
            var excelOps = _excelService.SearchOperations(query);
            result.ExcelOperations = excelOps.Select(o => new SearchResultItem
            {
                Title = o.Description ?? "",
                Source = "Excel Tool",
                Category = ExcelFormulaService.CategoryNames.GetValueOrDefault(o.Category ?? "", ""),
                LaborHours = o.LaborHours,
                Price = o.Price,
                Details = $"Row {o.Row} | {o.OperationType}"
            }).ToList();

            // Search parts
            var parts = SearchParts(query);
            result.Parts = parts.Select(p => new SearchResultItem
            {
                Title = p,
                Source = "Part Operations",
                Category = _partOperations.GetValueOrDefault(p)?.PartType ?? "",
                Details = $"{_partOperations.GetValueOrDefault(p)?.Operations.Count ?? 0} related operations"
            }).ToList();

            // Search justifications
            var justifications = _justifications
                .Where(j => j.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           j.Value.WhyNeeded.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(j => new SearchResultItem
                {
                    Title = j.Key,
                    Source = "Justifications",
                    Details = j.Value.WhyNeeded
                }).ToList();
            result.Justifications = justifications;

            return result;
        }

        #endregion

        #region Missing Operations Detection (Feature 2)

        /// <summary>
        /// Analyze an estimate and suggest potentially missing operations
        /// </summary>
        public List<MissingOperationSuggestion> DetectMissingOperations(List<string> estimateLines)
        {
            var suggestions = new List<MissingOperationSuggestion>();
            var hasPreScan = false;
            var hasPostScan = false;
            var hasWeldedPanel = false;
            var hasDestructiveTest = false;
            var hasCorrosionProtection = false;
            var hasFrameSetup = false;
            var hasACWork = false;
            var hasEvacuate = false;
            var hasRecharge = false;
            var hasWindshield = false;
            var hasADASCalibration = false;
            var hasAlignment = false;
            var hasSuspensionWork = false;

            // Analyze what's in the estimate
            foreach (var line in estimateLines)
            {
                var lower = line.ToLowerInvariant();

                if (lower.Contains("pre") && lower.Contains("scan")) hasPreScan = true;
                if (lower.Contains("post") && lower.Contains("scan")) hasPostScan = true;
                if (lower.Contains("quarter") || lower.Contains("rocker") || lower.Contains("pillar") || lower.Contains("roof panel"))
                    hasWeldedPanel = true;
                if (lower.Contains("destructive") || lower.Contains("weld test")) hasDestructiveTest = true;
                if (lower.Contains("corrosion") || lower.Contains("cavity wax")) hasCorrosionProtection = true;
                if (lower.Contains("frame") && (lower.Contains("setup") || lower.Contains("set up"))) hasFrameSetup = true;
                if (lower.Contains("condenser") || lower.Contains("evaporator") || lower.Contains("a/c") || lower.Contains("ac "))
                    hasACWork = true;
                if (lower.Contains("evacuate")) hasEvacuate = true;
                if (lower.Contains("recharge")) hasRecharge = true;
                if (lower.Contains("windshield")) hasWindshield = true;
                if (lower.Contains("calibrat")) hasADASCalibration = true;
                if (lower.Contains("alignment")) hasAlignment = true;
                if (lower.Contains("control arm") || lower.Contains("strut") || lower.Contains("knuckle"))
                    hasSuspensionWork = true;
            }

            // Check for missing operations
            if (!hasPreScan)
            {
                suggestions.Add(new MissingOperationSuggestion
                {
                    Operation = "Pre-Repair Scan",
                    Reason = "No pre-repair scan found. Required for most collision repairs.",
                    Priority = "High",
                    Justification = GetJustification("Pre-Repair Scan")
                });
            }

            if (!hasPostScan)
            {
                suggestions.Add(new MissingOperationSuggestion
                {
                    Operation = "Post-Repair Scan",
                    Reason = "No post-repair scan found. Required to verify systems after repair.",
                    Priority = "High",
                    Justification = GetJustification("Post-Repair Scan")
                });
            }

            if (hasWeldedPanel && !hasDestructiveTest)
            {
                suggestions.Add(new MissingOperationSuggestion
                {
                    Operation = "Destructive Weld Test",
                    Reason = "Welded panel found but no weld test. Required for structural repairs.",
                    Priority = "High",
                    Justification = GetJustification("Destructive Weld Test")
                });
            }

            if (hasWeldedPanel && !hasCorrosionProtection)
            {
                suggestions.Add(new MissingOperationSuggestion
                {
                    Operation = "Corrosion Protection",
                    Reason = "Welded panel found but no corrosion protection. Required for weld areas.",
                    Priority = "High",
                    Justification = GetJustification("Corrosion Protection")
                });
            }

            if (hasWeldedPanel && !hasFrameSetup)
            {
                suggestions.Add(new MissingOperationSuggestion
                {
                    Operation = "Frame Setup",
                    Reason = "Structural panel found but no frame setup. May be needed for proper repair.",
                    Priority = "Medium",
                    Justification = GetJustification("Frame Setup")
                });
            }

            if (hasACWork && !hasEvacuate)
            {
                suggestions.Add(new MissingOperationSuggestion
                {
                    Operation = "Evacuate A/C System",
                    Reason = "A/C component work found but no evacuate. Required before opening A/C system.",
                    Priority = "High",
                    Justification = GetJustification("Evacuate A/C System")
                });
            }

            if (hasACWork && !hasRecharge)
            {
                suggestions.Add(new MissingOperationSuggestion
                {
                    Operation = "Recharge A/C System",
                    Reason = "A/C component work found but no recharge. Required after A/C system service.",
                    Priority = "High",
                    Justification = GetJustification("Recharge A/C System")
                });
            }

            if (hasWindshield && !hasADASCalibration)
            {
                suggestions.Add(new MissingOperationSuggestion
                {
                    Operation = "Forward Camera Calibration",
                    Reason = "Windshield R&R found but no calibration. May be required if ADAS equipped.",
                    Priority = "Medium",
                    Justification = GetJustification("Forward Camera Calibration")
                });
            }

            if (hasSuspensionWork && !hasAlignment)
            {
                suggestions.Add(new MissingOperationSuggestion
                {
                    Operation = "4-Wheel Alignment",
                    Reason = "Suspension work found but no alignment. Required after suspension repairs.",
                    Priority = "High",
                    Justification = GetJustification("4-Wheel Alignment")
                });
            }

            return suggestions;
        }

        #endregion
    }

    #region Data Models

    public class PartOperationSet
    {
        public string PartName { get; set; } = "";
        public string PartType { get; set; } = "";
        public List<OperationRecommendation> Operations { get; set; } = new();
    }

    public class OperationRecommendation
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public decimal LaborHours { get; set; }
        public bool IsCommon { get; set; }
        public string Notes { get; set; }
        public bool IsMaterial { get; set; }
        public decimal TypicalCost { get; set; }

        public OperationRecommendation(string name, string category, decimal laborHours, bool isCommon, string notes)
        {
            Name = name;
            Category = category;
            LaborHours = laborHours;
            IsCommon = isCommon;
            Notes = notes;
        }
    }

    public class OperationJustification
    {
        public string Operation { get; set; } = "";
        public string WhyNeeded { get; set; } = "";
        public string PPageReference { get; set; } = "";
        public string DEGReference { get; set; } = "";
        public string OEMReference { get; set; } = "";
    }

    public class UnifiedSearchResult
    {
        public string Query { get; set; } = "";
        public List<SearchResultItem> ExcelOperations { get; set; } = new();
        public List<SearchResultItem> Parts { get; set; } = new();
        public List<SearchResultItem> Justifications { get; set; } = new();
    }

    public class SearchResultItem
    {
        public string Title { get; set; } = "";
        public string Source { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal Price { get; set; }
        public string Details { get; set; } = "";
    }

    public class MissingOperationSuggestion
    {
        public string Operation { get; set; } = "";
        public string Reason { get; set; } = "";
        public string Priority { get; set; } = "";
        public OperationJustification? Justification { get; set; }
    }

    #endregion
}
