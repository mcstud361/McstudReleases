#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Excel Knowledge Service - Complete awareness of the McStud Estimating Tool Excel structure.
    ///
    /// Provides:
    /// - Natural language queries about data locations
    /// - Explanations of what each input/output does
    /// - Navigation help (where to find things)
    /// - Contextual help for operations
    /// </summary>
    public class ExcelKnowledgeService
    {
        private static ExcelKnowledgeService? _instance;
        public static ExcelKnowledgeService Instance => _instance ??= new ExcelKnowledgeService();

        // Human-readable descriptions for all sheets
        private readonly Dictionary<string, SheetInfo> _sheetInfo;

        // Human-readable labels for input controls
        private readonly Dictionary<string, InputInfo> _inputInfo;

        // Search index for quick lookups
        private readonly Dictionary<string, List<string>> _searchIndex;

        public ExcelKnowledgeService()
        {
            _sheetInfo = BuildSheetInfo();
            _inputInfo = BuildInputInfo();
            _searchIndex = BuildSearchIndex();
        }

        #region Sheet Information

        private Dictionary<string, SheetInfo> BuildSheetInfo()
        {
            return new Dictionary<string, SheetInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["Home Page"] = new SheetInfo
                {
                    Name = "Home Page",
                    Description = "Main navigation and getting started guide for the McStud Estimating Tool (MET)",
                    Purpose = "Provides links to all other sheets, documentation, and support information",
                    Sections = new[] { "Getting Started", "MET Tabs", "Support", "Contact" }
                },
                ["SOP List"] = new SheetInfo
                {
                    Name = "SOP List",
                    Description = "Standard Operating Procedures - Electrical, Vehicle Diagnostics, and Miscellaneous operations",
                    Purpose = "Configure standard operations that apply to most repairs: battery config, ADAS, scanning, pre-wash, bio hazard",
                    Sections = new[] { "Electrical (rows 25-50)", "Vehicle Diagnostics (rows 75-120)", "Misc (rows 125-150)" },
                    KeyInputs = new[]
                    {
                        "Vehicle Type (Gas/Hybrid/EV) - A35",
                        "Battery Config (Single/Dual) - A29",
                        "ADAS - C29",
                        "Scan Type - A79",
                        "Pre Wash - A129"
                    }
                },
                ["Part Operations"] = new SheetInfo
                {
                    Name = "Part Operations",
                    Description = "Blend, Repair, and Replace operations for body parts",
                    Purpose = "Calculate labor for part-related operations: panels, bumpers, doors, etc.",
                    Sections = new[]
                    {
                        "Blend Operations (rows 30-80)",
                        "Repair Operations (rows 130-180)",
                        "Replace Operations (rows 230-280)",
                        "Door Operations (rows 330-430)",
                        "Glass Operations (rows 480-530)"
                    },
                    KeyInputs = new[]
                    {
                        "First/Additional Panel - A33, A83, A133, etc.",
                        "Large/Small Part - B85, B35, B135",
                        "Equipped Status - C35, C85, C135",
                        "Punch Holes, Install Brackets - D columns"
                    }
                },
                ["Cover Car Operations"] = new SheetInfo
                {
                    Name = "Cover Car Operations",
                    Description = "Cover Car, Masking, and Protecting operations",
                    Purpose = "Calculate labor for protecting vehicle during repair: masking, covering components",
                    Sections = new[] { "Cover Car Config (rows 25-35)" },
                    KeyInputs = new[]
                    {
                        "Vehicle Type (Gas/EV) - A29",
                        "Position (Front/Side/Rear) - B29-B31",
                        "Operation Type (Refinish/Repair) - C29-C30",
                        "Labor Type - D29"
                    }
                },
                ["Body Operations"] = new SheetInfo
                {
                    Name = "Body Operations",
                    Description = "Body Equipment, Tooling, and Measurements",
                    Purpose = "Frame work, measurements, structural repairs, pulling operations",
                    Sections = new[] { "Equipment Setup (rows 25-45)", "Frame Operations (rows 75-95)" },
                    KeyInputs = new[]
                    {
                        "Setup/Pull/Measure - A29-D29",
                        "Fixture/Anchor - A31-D31",
                        "Dent Puller/Glue Puller - D35-D36",
                        "Pre/In-Process/Post - D81-D83",
                        "Clamp Type - D87"
                    }
                },
                ["Refinish Operations"] = new SheetInfo
                {
                    Name = "Refinish Operations",
                    Description = "Paint and refinish operations",
                    Purpose = "Calculate refinish labor: 2-stage, 3-stage, 4-stage paint, clear coat, color match",
                    Sections = new[] { "Paint Type Selection (rows 25-50)" },
                    KeyInputs = new[]
                    {
                        "Paint Stage - A29, B31, B35, B39, B43 (2-Stage/3-Stage/4-Stage)",
                        "Color Match - C29, D29",
                        "Clear Coat - D31, D33",
                        "Special Finishes - A31, A33"
                    }
                },
                ["Mechanical Operations"] = new SheetInfo
                {
                    Name = "Mechanical Operations",
                    Description = "Mechanical repair operations",
                    Purpose = "A/C work, suspension, alignment, wheel operations",
                    Sections = new[]
                    {
                        "A/C Operations (rows 25-40)",
                        "Suspension (rows 75-90)",
                        "Wheel/Alignment (rows 125-150)",
                        "ADAS Calibration (rows 175-195)"
                    },
                    KeyInputs = new[]
                    {
                        "Refrigerant Type - A29 (R134a/R1234yf/R744)",
                        "Evacuate/Recharge - B29, C29",
                        "Alignment - B79, B81",
                        "ADAS Calibration - C179, D179, A185-A191"
                    }
                },
                ["SRS Operations"] = new SheetInfo
                {
                    Name = "SRS Operations",
                    Description = "Supplemental Restraint System (airbag/seatbelt) operations",
                    Purpose = "Safety restraint handling, airbag deployment, seatbelt operations",
                    Sections = new[] { "Airbag Operations (rows 25-40)", "Seatbelt Operations (rows 75-90)" },
                    KeyInputs = new[]
                    {
                        "Driver Airbag - A29",
                        "Passenger Airbag - A31",
                        "Side Airbags - A33, A35",
                        "Seatbelts - A79-A87"
                    }
                },
                ["Total Loss Charges"] = new SheetInfo
                {
                    Name = "Total Loss Charges",
                    Description = "Fees and charges for total loss vehicles",
                    Purpose = "Administration fees, storage, parts handling, hazardous waste for total loss claims",
                    Sections = new[]
                    {
                        "Standard Fees (A column)",
                        "Additional Fees (B column)",
                        "Parts (C column)",
                        "Storage (D-E columns)"
                    },
                    KeyInputs = new[]
                    {
                        "Admin Fee - A29",
                        "Yard Fee - A31",
                        "Storage Location/Dates - D29-E40",
                        "Parts Handling - C31",
                        "Parts Restocking - C33"
                    }
                },
                ["Body On Frame"] = new SheetInfo
                {
                    Name = "Body On Frame",
                    Description = "Operations specific to body-on-frame vehicles (trucks, SUVs)",
                    Purpose = "Frame-specific repairs for trucks and body-on-frame vehicles",
                    Sections = new[] { "Frame Operations (rows 25-40)" },
                    KeyInputs = new[] { "Frame Damage - A31, B31", "Cab Damage - A33" }
                },
                ["Stolen Recovery "] = new SheetInfo
                {
                    Name = "Stolen Recovery ",
                    Description = "Operations for recovered stolen vehicles",
                    Purpose = "Special operations for stolen vehicle recovery: key replacement, ignition, assessment",
                    Sections = new[] { "Recovery Operations (rows 25-45)" },
                    KeyInputs = new[]
                    {
                        "Key Replacement - B29, C29",
                        "Ignition - A31-C31",
                        "Assessment - A33-C33",
                        "Special Items - A37-A43"
                    }
                }
            };
        }

        #endregion

        #region Input Information

        private Dictionary<string, InputInfo> BuildInputInfo()
        {
            var info = new Dictionary<string, InputInfo>(StringComparer.OrdinalIgnoreCase);

            // SOP List inputs with human-readable descriptions
            AddInputInfo(info, "SOPList_A35", "Vehicle Type", "Select Gas, Hybrid, or EV. Affects diagnostic and electrical operations.", "SOP List", "A35");
            AddInputInfo(info, "SOPList_A29", "Battery Configuration", "Single or Dual battery setup. Affects battery-related operations.", "SOP List", "A29");
            AddInputInfo(info, "SOPList_C29", "ADAS Equipped", "Is vehicle equipped with ADAS? Adds calibration requirements.", "SOP List", "C29");
            AddInputInfo(info, "SOPList_A79", "Scan Type", "Diagnostic scan billing: Dollar Amount, Labor Unit, or Tesla.", "SOP List", "A79");
            AddInputInfo(info, "SOPList_A81", "Setup Scan Tool", "Include scan tool setup time.", "SOP List", "A81");
            AddInputInfo(info, "SOPList_A87", "Gateway Unlock", "Gateway/module unlocking required.", "SOP List", "A87");
            AddInputInfo(info, "SOPList_B79", "ADAS Diagnostics", "Include ADAS in diagnostic scan.", "SOP List", "B79");
            AddInputInfo(info, "SOPList_B81", "Simulate Full Fluids", "Simulate full fluid levels for diagnostics.", "SOP List", "B81");
            AddInputInfo(info, "SOPList_B83", "Adjust Tire Pressure", "Adjust tire pressure for diagnostics.", "SOP List", "B83");
            AddInputInfo(info, "SOPList_B85", "Remove Customer Belongings", "Remove customer items from vehicle.", "SOP List", "B85");
            AddInputInfo(info, "SOPList_B87", "Drive Cycle", "Perform drive cycle after repairs.", "SOP List", "B87");
            AddInputInfo(info, "SOPList_A129", "Pre Wash", "Pre-wash the vehicle before repair.", "SOP List", "A129");
            AddInputInfo(info, "SOPList_A133", "Bio Hazard", "Bio hazard cleanup required.", "SOP List", "A133");
            AddInputInfo(info, "SOPList_C129", "Shipping/Parts Labels", "Apply shipping or parts labels.", "SOP List", "C129");
            AddInputInfo(info, "SOPList_D129", "Scaffolding", "Scaffolding equipment required.", "SOP List", "D129");

            // Part Operations - First/Additional Panel
            AddInputInfo(info, "PartOp_A33", "Blend Panel Type", "First Panel or Additional Panel for blend operations.", "Part Operations", "A33");
            AddInputInfo(info, "PartOp_A83", "Repair Panel Type", "First Panel or Additional Panel for repair operations.", "Part Operations", "A83");
            AddInputInfo(info, "PartOp_A133", "Replace Panel Type", "First Panel or Additional Panel for replace operations.", "Part Operations", "A133");
            AddInputInfo(info, "PartOp_A183", "Door Panel Type", "First Panel or Additional Panel for door operations.", "Part Operations", "A183");

            // Part Operations - Large/Small Part
            AddInputInfo(info, "PartOp_B35", "Blend Part Size", "First Large Part, Additional Large Part, or Additional Small Part.", "Part Operations", "B35");
            AddInputInfo(info, "PartOp_B85", "Repair Part Size", "First Large Part, Additional Large Part, or Additional Small Part.", "Part Operations", "B85");
            AddInputInfo(info, "PartOp_B135", "Replace Part Size", "First Large Part, Additional Large Part, or Additional Small Part.", "Part Operations", "B135");

            // Refinish Operations
            AddInputInfo(info, "RefinishOp_A29", "Paint Stage", "2-Stage, 3-Stage, or 4-Stage paint process.", "Refinish Operations", "A29");
            AddInputInfo(info, "RefinishOp_B31", "Base Coat Stage", "Stage for base coat application.", "Refinish Operations", "B31");
            AddInputInfo(info, "RefinishOp_B35", "Mid Coat Stage", "Stage for mid coat (3-stage) application.", "Refinish Operations", "B35");
            AddInputInfo(info, "RefinishOp_C29", "Color Match", "Color matching/tinting required.", "Refinish Operations", "C29");
            AddInputInfo(info, "RefinishOp_D29", "Clear Coat", "Clear coat application.", "Refinish Operations", "D29");

            // Mechanical Operations
            AddInputInfo(info, "MechanicalOp_A29", "Refrigerant Type", "A/C refrigerant: R134a, R1234yf, or R744.", "Mechanical Operations", "A29");
            AddInputInfo(info, "MechanicalOp_B29", "Evacuate A/C", "Evacuate A/C system.", "Mechanical Operations", "B29");
            AddInputInfo(info, "MechanicalOp_C29", "Recharge A/C", "Recharge A/C system.", "Mechanical Operations", "C29");
            AddInputInfo(info, "MechanicalOp_B79", "Wheel Alignment", "4-wheel alignment required.", "Mechanical Operations", "B79");
            AddInputInfo(info, "MechanicalOp_B81", "Wheel Balance", "Wheel balancing required.", "Mechanical Operations", "B81");

            // Body Operations
            AddInputInfo(info, "BodyOp_A29", "Frame Setup", "Frame machine setup.", "Body Operations", "A29");
            AddInputInfo(info, "BodyOp_B29", "Frame Pull", "Frame pulling operation.", "Body Operations", "B29");
            AddInputInfo(info, "BodyOp_C29", "Frame Measure", "Frame measurement.", "Body Operations", "C29");
            AddInputInfo(info, "BodyOp_D79", "Frame Time", "Frame repair time included.", "Body Operations", "D79");
            AddInputInfo(info, "BodyOp_D87", "Clamp Type", "Pinch Welds or Truck Clamps.", "Body Operations", "D87");

            // SRS Operations
            AddInputInfo(info, "SRSOp_A29", "Driver Airbag", "Driver side airbag operation.", "SRS Operations", "A29");
            AddInputInfo(info, "SRSOp_A31", "Passenger Airbag", "Passenger side airbag operation.", "SRS Operations", "A31");
            AddInputInfo(info, "SRSOp_A79", "Driver Seatbelt", "Driver seatbelt operation.", "SRS Operations", "A79");

            return info;
        }

        private void AddInputInfo(Dictionary<string, InputInfo> dict, string key, string label, string description, string sheet, string cell)
        {
            dict[key] = new InputInfo
            {
                Key = key,
                Label = label,
                Description = description,
                Sheet = sheet,
                Cell = cell
            };
        }

        #endregion

        #region Search Index

        private Dictionary<string, List<string>> BuildSearchIndex()
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // Index keywords to input keys
            var keywords = new Dictionary<string, string[]>
            {
                // Vehicle/General
                ["vehicle"] = new[] { "SOPList_A35" },
                ["gas"] = new[] { "SOPList_A35", "CoverCarOp_A29" },
                ["hybrid"] = new[] { "SOPList_A35" },
                ["ev"] = new[] { "SOPList_A35", "CoverCarOp_A29" },
                ["electric"] = new[] { "SOPList_A35" },

                // Battery
                ["battery"] = new[] { "SOPList_A29", "SOPList_A31", "SOPList_A33" },
                ["dual battery"] = new[] { "SOPList_A29" },

                // ADAS
                ["adas"] = new[] { "SOPList_C29", "SOPList_B79", "MechanicalOp_C179", "MechanicalOp_D179", "MechanicalOp_A185" },
                ["calibration"] = new[] { "MechanicalOp_A138", "MechanicalOp_C179", "MechanicalOp_D179" },
                ["camera"] = new[] { "SOPList_C29", "MechanicalOp_C179" },
                ["radar"] = new[] { "MechanicalOp_D179" },

                // Scanning/Diagnostics
                ["scan"] = new[] { "SOPList_A79", "SOPList_A81" },
                ["diagnostic"] = new[] { "SOPList_A79", "SOPList_B79" },
                ["gateway"] = new[] { "SOPList_A87" },
                ["drive cycle"] = new[] { "SOPList_B87" },

                // Paint/Refinish
                ["paint"] = new[] { "RefinishOp_A29", "RefinishOp_B31", "RefinishOp_B35", "RefinishOp_B39", "RefinishOp_B43" },
                ["refinish"] = new[] { "RefinishOp_A29", "RefinishOp_B31", "CoverCarOp_C29" },
                ["2-stage"] = new[] { "RefinishOp_A29", "RefinishOp_B31" },
                ["3-stage"] = new[] { "RefinishOp_B35", "RefinishOp_B39", "RefinishOp_B43" },
                ["4-stage"] = new[] { "RefinishOp_A29" },
                ["clear coat"] = new[] { "RefinishOp_D29", "RefinishOp_D31", "RefinishOp_D33" },
                ["color match"] = new[] { "RefinishOp_C29" },
                ["blend"] = new[] { "PartOp_A33", "PartOp_B33", "PartOp_C33" },

                // Panel operations
                ["panel"] = new[] { "PartOp_A33", "PartOp_A83", "PartOp_A133", "PartOp_A183", "PartOp_A233", "PartOp_A283", "PartOp_A333", "PartOp_A383" },
                ["first panel"] = new[] { "PartOp_A33", "PartOp_A83", "PartOp_A133" },
                ["additional panel"] = new[] { "PartOp_A33", "PartOp_A83", "PartOp_A133" },

                // Parts
                ["bumper"] = new[] { "PartOp_A33", "PartOp_A83", "PartOp_A133" },
                ["fender"] = new[] { "PartOp_A233", "PartOp_A283" },
                ["door"] = new[] { "PartOp_A333", "PartOp_A383", "PartOp_A183" },
                ["hood"] = new[] { "PartOp_A233" },
                ["quarter"] = new[] { "PartOp_A283", "PartOp_A333" },
                ["roof"] = new[] { "PartOp_A333" },
                ["trunk"] = new[] { "PartOp_A383" },
                ["liftgate"] = new[] { "PartOp_A383" },
                ["glass"] = new[] { "PartOp_A483", "PartOp_A485", "PartOp_A487" },

                // A/C
                ["ac"] = new[] { "MechanicalOp_A29", "MechanicalOp_B29", "MechanicalOp_C29" },
                ["air conditioning"] = new[] { "MechanicalOp_A29", "MechanicalOp_B29", "MechanicalOp_C29" },
                ["refrigerant"] = new[] { "MechanicalOp_A29" },
                ["r134a"] = new[] { "MechanicalOp_A29" },
                ["r1234yf"] = new[] { "MechanicalOp_A29" },
                ["evacuate"] = new[] { "MechanicalOp_B29" },
                ["recharge"] = new[] { "MechanicalOp_C29" },

                // Alignment/Wheels
                ["alignment"] = new[] { "MechanicalOp_B79" },
                ["wheel"] = new[] { "MechanicalOp_B79", "MechanicalOp_B81" },
                ["balance"] = new[] { "MechanicalOp_B81" },
                ["tire"] = new[] { "SOPList_B83", "MechanicalOp_B79" },

                // Frame/Body
                ["frame"] = new[] { "BodyOp_A29", "BodyOp_B29", "BodyOp_C29", "BodyOp_D79", "BodyOnFrame_A31" },
                ["pull"] = new[] { "BodyOp_B29" },
                ["measure"] = new[] { "BodyOp_C29", "MechanicalOp_A137" },
                ["fixture"] = new[] { "BodyOp_A31", "BodyOp_B31" },
                ["anchor"] = new[] { "BodyOp_C31" },

                // SRS/Airbag
                ["airbag"] = new[] { "SRSOp_A29", "SRSOp_A31", "SRSOp_A33", "SRSOp_A35" },
                ["srs"] = new[] { "SRSOp_A29", "SRSOp_A31", "SRSOp_A79" },
                ["seatbelt"] = new[] { "SRSOp_A79", "SRSOp_A81", "SRSOp_A83", "SRSOp_A85", "SRSOp_A87" },

                // Misc
                ["wash"] = new[] { "SOPList_A129" },
                ["pre wash"] = new[] { "SOPList_A129" },
                ["bio hazard"] = new[] { "SOPList_A133" },
                ["storage"] = new[] { "TotalLossCharges_D29", "TotalLossCharges_D34", "TotalLossCharges_D39" },
                ["admin fee"] = new[] { "TotalLossCharges_AdminFeePrice" },
                ["total loss"] = new[] { "TotalLossCharges_AdminFeePrice", "TotalLossCharges_YardFeePrice" },
                ["stolen"] = new[] { "StolenRecovery_A33", "StolenRecovery_B29" }
            };

            foreach (var kvp in keywords)
            {
                index[kvp.Key] = kvp.Value.ToList();
            }

            return index;
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// Answer a natural language question about the Excel structure
        /// </summary>
        public ExcelQueryResult AnswerQuestion(string question)
        {
            var result = new ExcelQueryResult { Question = question };
            var lower = question.ToLowerInvariant();

            // FIRST: Check for direct item queries (e.g., "adas", "labor rate", "blend")
            // This gives immediate, direct answers
            var directResult = GetDirectAnswer(lower);
            if (directResult.Success)
                return directResult;

            // Check for location questions
            if (lower.Contains("where") || lower.Contains("find") || lower.Contains("location") ||
                lower.Contains("which sheet") || lower.Contains("which tab") || lower.Contains("what tab") ||
                lower.Contains("what sheet"))
            {
                result = FindLocation(lower);
            }
            // Check for "what is" questions
            else if (lower.Contains("what is") || lower.Contains("what does") || lower.Contains("explain"))
            {
                result = ExplainConcept(lower);
            }
            // Check for "how to" questions
            else if (lower.Contains("how to") || lower.Contains("how do i"))
            {
                result = HowToGuide(lower);
            }
            // Check for sheet questions
            else if (lower.Contains("sheet") || lower.Contains("tab"))
            {
                result = DescribeSheet(lower);
            }
            // Default: search for relevant inputs
            else
            {
                result = SearchInputs(lower);
            }

            return result;
        }

        /// <summary>
        /// Get a direct, concise answer for common queries
        /// </summary>
        private ExcelQueryResult GetDirectAnswer(string query)
        {
            var result = new ExcelQueryResult { Question = query, Success = false };

            // ADAS - most common question
            if (query.Contains("adas"))
            {
                result.Success = true;
                result.Answer = "**ADAS is on the SOP List sheet**\n\n" +
                    "Main ADAS toggle: Cell C29 (Yes/No)\n" +
                    "ADAS Diagnostics: Cell B79\n\n" +
                    "Additional ADAS calibration options are on Mechanical Operations sheet (rows 175-195)";
                result.Locations = new List<LocationMatch>
                {
                    new LocationMatch { Label = "ADAS Equipped", Sheet = "SOP List", Cell = "C29", Description = "Main ADAS toggle" },
                    new LocationMatch { Label = "ADAS Diagnostics", Sheet = "SOP List", Cell = "B79", Description = "Include ADAS in scan" },
                    new LocationMatch { Label = "ADAS Calibration", Sheet = "Mechanical Operations", Cell = "C179", Description = "Calibration options" }
                };
                return result;
            }

            // Labor rate
            if (query.Contains("labor") && query.Contains("rate"))
            {
                result.Success = true;
                result.Answer = "**Labor Rate is on the SOP List sheet**\n\n" +
                    "Cell: C9\n\n" +
                    "This sets the base labor rate used for all hour calculations.";
                result.Locations = new List<LocationMatch>
                {
                    new LocationMatch { Label = "Labor Rate", Sheet = "SOP List", Cell = "C9", Description = "Base labor rate per hour" }
                };
                return result;
            }

            // Blend
            if (query.Contains("blend"))
            {
                result.Success = true;
                result.Answer = "**Blend operations are on the Part Operations sheet**\n\n" +
                    "Blend panel toggles are in the Blend section.\n" +
                    "Example: Hood blend at B17, Fender blend at B23";
                result.Locations = new List<LocationMatch>
                {
                    new LocationMatch { Label = "Hood Blend", Sheet = "Part Operations", Cell = "B17", Description = "Blend hood panel" },
                    new LocationMatch { Label = "Fender Blend", Sheet = "Part Operations", Cell = "B23", Description = "Blend fender panel" }
                };
                return result;
            }

            // Refinish / Paint
            if (query.Contains("refinish") || query.Contains("paint"))
            {
                result.Success = true;
                result.Answer = "**Refinish operations are on the Refinish Operations sheet**\n\n" +
                    "Contains paint materials, clear coat, and refinish labor.\n" +
                    "Also: Panel refinish toggles are on Part Operations sheet (column D).";
                result.Locations = new List<LocationMatch>
                {
                    new LocationMatch { Label = "Refinish Operations", Sheet = "Refinish Operations", Cell = "A1", Description = "Main refinish sheet" },
                    new LocationMatch { Label = "Panel Refinish Toggle", Sheet = "Part Operations", Cell = "D17", Description = "Panel refinish options" }
                };
                return result;
            }

            // Battery
            if (query.Contains("battery"))
            {
                result.Success = true;
                result.Answer = "**Battery options are on the SOP List sheet**\n\n" +
                    "12V Battery Type: Cell C23 (Single/Dual)\n" +
                    "Test Battery: Cell C24 (Yes/No)\n" +
                    "Battery Support: Cell C25 (Yes/No)";
                result.Locations = new List<LocationMatch>
                {
                    new LocationMatch { Label = "12V Battery Type", Sheet = "SOP List", Cell = "C23", Description = "Single or Dual battery" },
                    new LocationMatch { Label = "Test Battery", Sheet = "SOP List", Cell = "C24", Description = "Include battery test" },
                    new LocationMatch { Label = "Battery Support", Sheet = "SOP List", Cell = "C25", Description = "Memory saver required" }
                };
                return result;
            }

            // SRS / Airbag
            if (query.Contains("srs") || query.Contains("airbag") || query.Contains("air bag"))
            {
                result.Success = true;
                result.Answer = "**SRS/Airbag operations are on the SRS Operations sheet**\n\n" +
                    "Contains airbag, seatbelt, and restraint system operations.\n" +
                    "Toggle in SOP List at B89 (Include SRS).";
                result.Locations = new List<LocationMatch>
                {
                    new LocationMatch { Label = "SRS Operations", Sheet = "SRS Operations", Cell = "A1", Description = "SRS operations sheet" },
                    new LocationMatch { Label = "Include SRS", Sheet = "SOP List", Cell = "B89", Description = "Enable SRS operations" }
                };
                return result;
            }

            // Scan / Diagnostic
            if (query.Contains("scan") || query.Contains("diagnostic"))
            {
                result.Success = true;
                result.Answer = "**Scan/Diagnostic operations are on the SOP List sheet**\n\n" +
                    "Vehicle Diagnostics section starting at row 67.\n" +
                    "Pre-Scan: B67, Post-Scan: B68\n" +
                    "Scan Type: B71 (Dollar Amount/Labor Unit)";
                result.Locations = new List<LocationMatch>
                {
                    new LocationMatch { Label = "Pre-Scan", Sheet = "SOP List", Cell = "B67", Description = "Pre-repair diagnostic scan" },
                    new LocationMatch { Label = "Post-Scan", Sheet = "SOP List", Cell = "B68", Description = "Post-repair diagnostic scan" },
                    new LocationMatch { Label = "Scan Type", Sheet = "SOP List", Cell = "B71", Description = "Dollar amount or labor units" }
                };
                return result;
            }

            // Cover car / Masking
            if (query.Contains("cover") || query.Contains("mask"))
            {
                result.Success = true;
                result.Answer = "**Cover Car/Masking operations are on the Cover Car Operations sheet**\n\n" +
                    "Contains cover car percentage, masking, and protection operations.";
                result.Locations = new List<LocationMatch>
                {
                    new LocationMatch { Label = "Cover Car Operations", Sheet = "Cover Car Operations", Cell = "A1", Description = "Cover car and masking" },
                    new LocationMatch { Label = "Cover Car %", Sheet = "Cover Car Operations", Cell = "C5", Description = "Cover car percentage" }
                };
                return result;
            }

            // Mechanical
            if (query.Contains("mechanical") || query.Contains("alignment") || query.Contains("a/c") || query.Contains("ac "))
            {
                result.Success = true;
                result.Answer = "**Mechanical operations are on the Mechanical Operations sheet**\n\n" +
                    "Contains alignment, A/C, frame, and mechanical repair operations.\n" +
                    "ADAS calibrations are also here (rows 175-195).";
                result.Locations = new List<LocationMatch>
                {
                    new LocationMatch { Label = "Mechanical Operations", Sheet = "Mechanical Operations", Cell = "A1", Description = "Mechanical repairs" },
                    new LocationMatch { Label = "Alignment", Sheet = "Mechanical Operations", Cell = "C5", Description = "Wheel alignment" }
                };
                return result;
            }

            // Total Loss
            if (query.Contains("total loss"))
            {
                result.Success = true;
                result.Answer = "**Total Loss charges are on the Total Loss Charges sheet**\n\n" +
                    "Contains admin fees, storage, and total loss processing charges.";
                result.Locations = new List<LocationMatch>
                {
                    new LocationMatch { Label = "Total Loss Charges", Sheet = "Total Loss Charges", Cell = "A1", Description = "Total loss fees and charges" }
                };
                return result;
            }

            return result;
        }

        /// <summary>
        /// Find where something is located
        /// </summary>
        public ExcelQueryResult FindLocation(string query)
        {
            var result = new ExcelQueryResult { Question = query };
            var matches = new List<LocationMatch>();

            // Search for matching inputs
            foreach (var kvp in _searchIndex)
            {
                if (query.Contains(kvp.Key))
                {
                    foreach (var inputKey in kvp.Value)
                    {
                        if (ExcelMappings.TryGetInput(inputKey, out var mapping))
                        {
                            var inputInfo = _inputInfo.GetValueOrDefault(inputKey);
                            matches.Add(new LocationMatch
                            {
                                Key = inputKey,
                                Label = inputInfo?.Label ?? inputKey,
                                Sheet = mapping.Sheet,
                                Cell = mapping.Cell,
                                Description = inputInfo?.Description ?? ""
                            });
                        }
                    }
                }
            }

            if (matches.Count > 0)
            {
                // Remove duplicates
                matches = matches.DistinctBy(m => m.Key).ToList();

                result.Answer = $"Found {matches.Count} related location(s):";
                result.Locations = matches;
                result.Success = true;
            }
            else
            {
                result.Answer = "I couldn't find that specific item. Try searching for:\n" +
                    "- Part names (bumper, fender, door, hood, quarter panel)\n" +
                    "- Operations (paint, refinish, blend, repair, replace)\n" +
                    "- Systems (ADAS, A/C, alignment, frame, airbag)";
                result.Success = false;
            }

            return result;
        }

        /// <summary>
        /// Explain what something is/does
        /// </summary>
        public ExcelQueryResult ExplainConcept(string query)
        {
            var result = new ExcelQueryResult { Question = query };

            // Check for sheet explanations
            foreach (var kvp in _sheetInfo)
            {
                if (query.Contains(kvp.Key.ToLowerInvariant()))
                {
                    var sheet = kvp.Value;
                    result.Answer = $"**{sheet.Name}**\n\n" +
                        $"{sheet.Description}\n\n" +
                        $"**Purpose:** {sheet.Purpose}\n\n" +
                        $"**Sections:** {string.Join(", ", sheet.Sections)}\n\n" +
                        (sheet.KeyInputs != null ? $"**Key Inputs:**\n- {string.Join("\n- ", sheet.KeyInputs)}" : "");
                    result.Success = true;
                    return result;
                }
            }

            // Check for input explanations
            foreach (var kvp in _inputInfo)
            {
                var info = kvp.Value;
                if (query.Contains(info.Label.ToLowerInvariant()) ||
                    query.Contains(kvp.Key.ToLowerInvariant()))
                {
                    result.Answer = $"**{info.Label}**\n\n" +
                        $"{info.Description}\n\n" +
                        $"**Location:** {info.Sheet} → Cell {info.Cell}";
                    result.Success = true;
                    return result;
                }
            }

            // Fall back to search
            return SearchInputs(query);
        }

        /// <summary>
        /// Provide a how-to guide
        /// </summary>
        public ExcelQueryResult HowToGuide(string query)
        {
            var result = new ExcelQueryResult { Question = query };

            if (query.Contains("blend") || query.Contains("paint") || query.Contains("refinish"))
            {
                result.Answer = "**How to Add Refinish/Blend Operations:**\n\n" +
                    "1. Go to **Part Operations** sheet\n" +
                    "2. Find the panel type (rows 30-80 for blend)\n" +
                    "3. Select First Panel or Additional Panel (Column A)\n" +
                    "4. Choose part size in Column B\n" +
                    "5. Set equipped status in Column C\n\n" +
                    "For paint stage:\n" +
                    "1. Go to **Refinish Operations** sheet\n" +
                    "2. Select 2-Stage, 3-Stage, or 4-Stage (Cell A29)";
                result.Success = true;
            }
            else if (query.Contains("scan") || query.Contains("diagnostic"))
            {
                result.Answer = "**How to Add Scanning/Diagnostics:**\n\n" +
                    "1. Go to **SOP List** sheet\n" +
                    "2. In Vehicle Diagnostics section (row 75+):\n" +
                    "   - Set Scan Type (A79): Dollar Amount, Labor Unit, or Tesla\n" +
                    "   - Enable Setup Scan Tool (A81): Yes/No\n" +
                    "   - Enable ADAS diagnostics (B79): Yes/No\n" +
                    "3. For additional options:\n" +
                    "   - Simulate Full Fluids (B81)\n" +
                    "   - Adjust Tire Pressure (B83)\n" +
                    "   - Drive Cycle (B87)";
                result.Success = true;
            }
            else if (query.Contains("adas") || query.Contains("calibration"))
            {
                result.Answer = "**How to Add ADAS Calibration:**\n\n" +
                    "1. In **SOP List** sheet:\n" +
                    "   - Enable ADAS (C29): Yes\n" +
                    "   - Enable ADAS diagnostics (B79): Yes\n\n" +
                    "2. In **Mechanical Operations** sheet:\n" +
                    "   - Camera calibration (C179, C185)\n" +
                    "   - Radar calibration (D179, D181, D183)\n" +
                    "   - Set Measure (A137) and Calibrate (A138)";
                result.Success = true;
            }
            else if (query.Contains("frame") || query.Contains("structural"))
            {
                result.Answer = "**How to Add Frame/Structural Work:**\n\n" +
                    "1. Go to **Body Operations** sheet\n" +
                    "2. Equipment Setup section (rows 25-45):\n" +
                    "   - Frame Setup (A29): Yes/No\n" +
                    "   - Frame Pull (B29): Yes/No\n" +
                    "   - Frame Measure (C29): Yes/No\n" +
                    "3. Frame section (rows 75-95):\n" +
                    "   - Enable frame time (D79)\n" +
                    "   - Select Pre/In-Process/Post (D81-D83)\n" +
                    "   - Choose clamp type (D87)";
                result.Success = true;
            }
            else
            {
                result = SearchInputs(query);
            }

            return result;
        }

        /// <summary>
        /// Describe a sheet
        /// </summary>
        public ExcelQueryResult DescribeSheet(string query)
        {
            var result = new ExcelQueryResult { Question = query };

            foreach (var kvp in _sheetInfo)
            {
                if (query.Contains(kvp.Key.ToLowerInvariant()) ||
                    (query.Contains("sop") && kvp.Key.Contains("SOP")) ||
                    (query.Contains("part") && kvp.Key.Contains("Part")) ||
                    (query.Contains("body") && kvp.Key.Contains("Body")) ||
                    (query.Contains("refinish") && kvp.Key.Contains("Refinish")) ||
                    (query.Contains("mechanical") && kvp.Key.Contains("Mechanical")) ||
                    (query.Contains("srs") && kvp.Key.Contains("SRS")) ||
                    (query.Contains("cover") && kvp.Key.Contains("Cover")))
                {
                    var sheet = kvp.Value;
                    result.Answer = $"**{sheet.Name}**\n\n" +
                        $"{sheet.Description}\n\n" +
                        $"**Purpose:** {sheet.Purpose}\n\n" +
                        $"**Sections:**\n- {string.Join("\n- ", sheet.Sections)}";

                    if (sheet.KeyInputs != null && sheet.KeyInputs.Length > 0)
                    {
                        result.Answer += $"\n\n**Key Inputs:**\n- {string.Join("\n- ", sheet.KeyInputs)}";
                    }

                    result.Success = true;
                    return result;
                }
            }

            // List all sheets
            result.Answer = "**Available Sheets:**\n\n" +
                string.Join("\n", _sheetInfo.Values.Select(s => $"- **{s.Name}**: {s.Description}"));
            result.Success = true;

            return result;
        }

        /// <summary>
        /// Search for inputs matching a query
        /// </summary>
        public ExcelQueryResult SearchInputs(string query)
        {
            var result = new ExcelQueryResult { Question = query };
            var matches = new List<LocationMatch>();

            // Search through index
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (word.Length < 3) continue;

                foreach (var kvp in _searchIndex)
                {
                    if (kvp.Key.Contains(word) || word.Contains(kvp.Key))
                    {
                        foreach (var inputKey in kvp.Value)
                        {
                            if (ExcelMappings.TryGetInput(inputKey, out var mapping))
                            {
                                var inputInfo = _inputInfo.GetValueOrDefault(inputKey);
                                matches.Add(new LocationMatch
                                {
                                    Key = inputKey,
                                    Label = inputInfo?.Label ?? inputKey,
                                    Sheet = mapping.Sheet,
                                    Cell = mapping.Cell,
                                    Description = inputInfo?.Description ?? ""
                                });
                            }
                        }
                    }
                }
            }

            if (matches.Count > 0)
            {
                matches = matches.DistinctBy(m => m.Key).Take(10).ToList();
                result.Answer = $"Found {matches.Count} related input(s):";
                result.Locations = matches;
                result.Success = true;
            }
            else
            {
                result.Answer = "No matching inputs found. Try asking about:\n" +
                    "- Sheets: SOP List, Part Operations, Refinish Operations, etc.\n" +
                    "- Operations: blend, repair, replace, refinish, paint\n" +
                    "- Parts: bumper, fender, door, hood, quarter panel\n" +
                    "- Systems: ADAS, A/C, alignment, frame, airbag";
                result.Success = false;
            }

            return result;
        }

        /// <summary>
        /// Get all sheets info
        /// </summary>
        public IReadOnlyDictionary<string, SheetInfo> GetAllSheets() => _sheetInfo;

        /// <summary>
        /// Get input info by key
        /// </summary>
        public InputInfo? GetInputInfo(string key) => _inputInfo.GetValueOrDefault(key);

        /// <summary>
        /// Get all inputs for a sheet with human-readable info
        /// </summary>
        public List<InputInfo> GetInputsForSheet(string sheetName)
        {
            var inputs = ExcelMappings.GetInputsForSheet(sheetName);
            var result = new List<InputInfo>();

            foreach (var mapping in inputs)
            {
                var key = $"{sheetName.Replace(" ", "")}_{mapping.Cell}";
                var info = _inputInfo.GetValueOrDefault(key) ?? new InputInfo
                {
                    Key = key,
                    Label = $"Cell {mapping.Cell}",
                    Description = string.Join(", ", mapping.Options),
                    Sheet = mapping.Sheet,
                    Cell = mapping.Cell
                };
                result.Add(info);
            }

            return result;
        }

        #endregion
    }

    #region Data Models

    public class SheetInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string[] Sections { get; set; } = Array.Empty<string>();
        public string[]? KeyInputs { get; set; }
    }

    public class InputInfo
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public string Sheet { get; set; } = "";
        public string Cell { get; set; } = "";
    }

    public class ExcelQueryResult
    {
        public string Question { get; set; } = "";
        public string Answer { get; set; } = "";
        public bool Success { get; set; }
        public List<LocationMatch> Locations { get; set; } = new();
    }

    public class LocationMatch
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Sheet { get; set; } = "";
        public string Cell { get; set; } = "";
        public string Description { get; set; } = "";
    }

    #endregion
}
