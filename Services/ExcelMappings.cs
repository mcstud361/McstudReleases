// Complete Excel Mappings for Mcstud Estimating Tool
// Optimized for faster lookups with FrozenDictionary and cached sheet references
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Maps UI controls to Excel cells for the Mcstud Estimating Tool
    /// Auto-generated from: Unlocked Mcstud Estimating Tool Master.xlsx
    /// Optimized with FrozenDictionary for O(1) lookups and reduced memory allocations
    /// </summary>
    public static class ExcelMappings
    {
        // Common options arrays (shared to reduce memory allocations)
        private static readonly string[] YesNoOptions = ["Yes", "No"];
        private static readonly string[] EmptyOptions = [];
        private static readonly string[] FirstAdditionalPanelOptions = ["First Panel", "Additional Panel"];
        private static readonly string[] EquippedNotEquippedOptions = ["Equipped", "Not Equipped"];
        private static readonly string[] StorageLocationOptions = ["Inside", "Outside", "Upstairs"];
        private static readonly string[] StageOptions = ["2-Stage", "3-Stage", "4-Stage"];
        private static readonly string[] LargeSmallOptions = ["Large", "Small"];

        // Sheet name constants (interned for faster comparison)
        public static class Sheets
        {
            public const string SOPList = "SOP List";
            public const string PartOperations = "Part Operations";
            public const string CoverCarOperations = "Cover Car Operations";
            public const string BodyOperations = "Body Operations";
            public const string RefinishOperations = "Refinish Operations";
            public const string MechanicalOperations = "Mechanical Operations";
            public const string SRSOperations = "SRS Operations";
            public const string TotalLossCharges = "Total Loss Charges";
            public const string BodyOnFrame = "Body On Frame";
            public const string StolenRecovery = "Stolen Recovery ";
        }

        #region Input Controls (Dropdowns/Toggles)

        /// <summary>
        /// UI Input Name -> (Sheet, Cell, Options)
        /// Uses FrozenDictionary for O(1) lookups after initialization
        /// </summary>
        private static readonly Dictionary<string, InputMapping> _inputControlsSource = new()
        {
            // ===== SOP List Inputs =====
            ["SOPList_A35"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "A35",
                Options: new[] { "Gas", "Hybrid", "EV" },
                DefaultValue: "Gas"
            ),
            ["SOPList_C29"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "C29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["SOPList_A29"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "A29",
                Options: new[] { "Single", "Dual" },
                DefaultValue: "Single"
            ),
            ["SOPList_A79"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "A79",
                Options: new[] { "Dollar Amount", "Labor Unit", "Tesla" },
                DefaultValue: "Dollar Amount"
            ),
            ["SOPList_A31"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "A31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["SOPList_B83"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "B83",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["SOPList_A87"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "A87",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["SOPList_B87"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "B87",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["SOPList_A81"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "A81",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["SOPList_A129"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "A129",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["SOPList_B81"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "B81",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["SOPList_B85"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "B85",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["SOPList_A33"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "A33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["SOPList_C129"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "C129",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["SOPList_D129"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "D129",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["SOPList_A133"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "A133",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["SOPList_B79"] = new InputMapping(
                Sheet: "SOP List",
                Cell: "B79",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),

            // ===== Part Operations Inputs =====
            ["PartOp_A33"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A33",
                Options: new[] { "First Panel", "Additional Panel" },
                DefaultValue: "Additional Panel"
            ),
            ["PartOp_A83"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A83",
                Options: new[] { "First Panel", "Additional Panel" },
                DefaultValue: "First Panel"
            ),
            ["PartOp_A183"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A183",
                Options: new[] { "First Panel", "Additional Panel" },
                DefaultValue: "First Panel"
            ),
            ["PartOp_A133"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A133",
                Options: new[] { "First Panel", "Additional Panel" },
                DefaultValue: "Additional Panel"
            ),
            ["PartOp_A437"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A437",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_B437"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B437",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_A35"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A35",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_C233"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C233",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_C91"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C91",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_B335"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B335",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_D240"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D240",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_D38"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D38",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_C237"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C237",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_C37"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C37",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_D290"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D290",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_C87"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C87",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_D88"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D88",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_C391"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C391",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_B433"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B433",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_B287"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B287",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_C287"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C287",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_D138"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D138",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_B439"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B439",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_D90"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D90",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_D388"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D388",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_B435"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B435",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_C485"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C485",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_A285"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A285",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_B285"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B285",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_D140"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D140",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_B333"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B333",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_C333"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C333",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_D40"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D40",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_E337"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E337",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_D338"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D338",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_B385"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B385",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_A237"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A237",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_B283"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B283",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_C283"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C283",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_A187"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A187",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_E333"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E333",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_A485"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A485",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_D390"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D390",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_D340"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D340",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_C137"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C137",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_D288"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D288",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_E137"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E137",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_C41"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C41",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_A337"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A337",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_B337"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B337",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_C337"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C337",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_B83"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B83",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_C83"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C83",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_A387"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A387",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_B387"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B387",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_C387"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C387",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_A487"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A487",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_C291"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C291",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_B483"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B483",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_C483"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C483",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_D483"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D483",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_B33"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_C33"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_C139"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C139",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_B133"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B133",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_C133"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C133",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_D238"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D238",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_A235"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A235",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_B383"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B383",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_C383"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C383",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["PartOp_E133"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E133",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_A85"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A85",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_A185"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A185",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["PartOp_C241"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C241",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["PartOp_B85"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B85",
                Options: new[] { "First Large Part", "Additional Large Part", "Additional Small Part" },
                DefaultValue: "First Large Part"
            ),
            ["PartOp_B35"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B35",
                Options: new[] { "First Large Part", "Additional Large Part", "Additional Small Part" },
                DefaultValue: "First Large Part"
            ),
            ["PartOp_B135"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B135",
                Options: new[] { "First Large Part", "Additional Large Part", "Additional Small Part" },
                DefaultValue: "Additional Large Part"
            ),
            ["PartOp_C35"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C35",
                Options: new[] { "Equipped", "Not Equipped" },
                DefaultValue: "Not Equipped"
            ),
            ["PartOp_C285"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C285",
                Options: new[] { "Equipped", "Not Equipped" },
                DefaultValue: ""
            ),
            ["PartOp_C135"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C135",
                Options: new[] { "Equipped", "Not Equipped" },
                DefaultValue: "Not Equipped"
            ),
            ["PartOp_C385"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C385",
                Options: new[] { "Equipped", "Not Equipped" },
                DefaultValue: "Not Equipped"
            ),
            ["PartOp_C235"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C235",
                Options: new[] { "Equipped", "Not Equipped" },
                DefaultValue: "Not Equipped"
            ),
            ["PartOp_C85"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C85",
                Options: new[] { "Equipped", "Not Equipped" },
                DefaultValue: "Not Equipped"
            ),
            ["PartOp_C335"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "C335",
                Options: new[] { "Equipped", "Not Equipped" },
                DefaultValue: "Not Equipped"
            ),
            ["PartOp_D333"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D333",
                Options: new[] { "Punch Holes" },
                DefaultValue: ""
            ),
            ["PartOp_D83"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D83",
                Options: new[] { "Punch Holes" },
                DefaultValue: ""
            ),
            ["PartOp_D33"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D33",
                Options: new[] { "Punch Holes" },
                DefaultValue: ""
            ),
            ["PartOp_D283"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D283",
                Options: new[] { "Punch Holes" },
                DefaultValue: ""
            ),
            ["PartOp_D133"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D133",
                Options: new[] { "Punch Holes" },
                DefaultValue: ""
            ),
            ["PartOp_D383"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D383",
                Options: new[] { "Punch Holes" },
                DefaultValue: ""
            ),
            ["PartOp_D233"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D233",
                Options: new[] { "Punch Holes" },
                DefaultValue: ""
            ),
            ["PartOp_D234"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D234",
                Options: new[] { "Install Brackets" },
                DefaultValue: ""
            ),
            ["PartOp_D284"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D284",
                Options: new[] { "Install Brackets" },
                DefaultValue: ""
            ),
            ["PartOp_D134"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D134",
                Options: new[] { "Install Brackets" },
                DefaultValue: ""
            ),
            ["PartOp_D384"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D384",
                Options: new[] { "Install Brackets" },
                DefaultValue: ""
            ),
            ["PartOp_D34"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D34",
                Options: new[] { "Install Brackets" },
                DefaultValue: ""
            ),
            ["PartOp_D84"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D84",
                Options: new[] { "Install Brackets" },
                DefaultValue: ""
            ),
            ["PartOp_D334"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "D334",
                Options: new[] { "Install Brackets" },
                DefaultValue: ""
            ),
            ["PartOp_A333"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A333",
                Options: new[] { "First Panel", "Additional Panel", "First Panel Facing Sky" },
                DefaultValue: "First Panel Facing Sky"
            ),
            ["PartOp_A383"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A383",
                Options: new[] { "First Panel", "Additional Panel", "First Panel Facing Sky" },
                DefaultValue: "First Panel"
            ),
            ["PartOp_A233"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A233",
                Options: new[] { "First Panel", "Additional Panel", "First Panel Facing Sky" },
                DefaultValue: "Additional Panel"
            ),
            ["PartOp_A283"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A283",
                Options: new[] { "First Panel", "Additional Panel", "First Panel Facing Sky" },
                DefaultValue: "Additional Panel"
            ),
            ["PartOp_E283"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E283",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E243"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E243",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E233"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E233",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E238"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E238",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_B233"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B233",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E483"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E483",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E297"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E297",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_B389"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B389",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E33"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E33",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E344"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E344",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_B292"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B292",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E247"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E247",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E83"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E83",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_B236"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B236",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E293"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E293",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E88"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E88",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E38"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E38",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E383"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E383",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E288"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E288",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_B392"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B392",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_B342"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B342",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_B289"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B289",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E388"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E388",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E397"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E397",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_B339"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B339",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_E393"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E393",
                Options: new[] { "Remove" },
                DefaultValue: ""
            ),
            ["PartOp_B343"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B343",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_E234"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E234",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_B390"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B390",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_B290"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B290",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_E34"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E34",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_E244"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E244",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_B237"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B237",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_E341"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E341",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_E394"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E394",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_E294"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E294",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_B234"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B234",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_B293"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B293",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_E384"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E384",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_E284"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E284",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_B393"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B393",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_B340"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "B340",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_E84"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E84",
                Options: new[] { "Apply" },
                DefaultValue: ""
            ),
            ["PartOp_E39"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E39",
                Options: new[] { "Install" },
                DefaultValue: ""
            ),
            ["PartOp_E298"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E298",
                Options: new[] { "Install" },
                DefaultValue: ""
            ),
            ["PartOp_E484"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E484",
                Options: new[] { "Install" },
                DefaultValue: ""
            ),
            ["PartOp_E289"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E289",
                Options: new[] { "Install" },
                DefaultValue: ""
            ),
            ["PartOp_E89"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E89",
                Options: new[] { "Install" },
                DefaultValue: ""
            ),
            ["PartOp_E248"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E248",
                Options: new[] { "Install" },
                DefaultValue: ""
            ),
            ["PartOp_E389"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E389",
                Options: new[] { "Install" },
                DefaultValue: ""
            ),
            ["PartOp_E398"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E398",
                Options: new[] { "Install" },
                DefaultValue: ""
            ),
            ["PartOp_E345"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E345",
                Options: new[] { "Install" },
                DefaultValue: ""
            ),
            ["PartOp_E239"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E239",
                Options: new[] { "Install" },
                DefaultValue: ""
            ),
            ["PartOp_E245"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E245",
                Options: new[] { "Dual Pinstripes" },
                DefaultValue: ""
            ),
            ["PartOp_E342"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E342",
                Options: new[] { "Dual Pinstripes" },
                DefaultValue: ""
            ),
            ["PartOp_E295"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E295",
                Options: new[] { "Dual Pinstripes" },
                DefaultValue: ""
            ),
            ["PartOp_E395"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "E395",
                Options: new[] { "Dual Pinstripes" },
                DefaultValue: ""
            ),
            ["PartOp_A483"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A483",
                Options: new[] { "Large", "Small" },
                DefaultValue: "Large"
            ),
            ["PartOp_A433"] = new InputMapping(
                Sheet: "Part Operations",
                Cell: "A433",
                Options: new[] { "Large", "Small" },
                DefaultValue: "Large"
            ),

            // ===== Cover Car Operations Inputs =====
            ["CoverCarOp_C32"] = new InputMapping(
                Sheet: "Cover Car Operations",
                Cell: "C32",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["CoverCarOp_A29"] = new InputMapping(
                Sheet: "Cover Car Operations",
                Cell: "A29",
                Options: new[] { "Gas", "EV" },
                DefaultValue: "EV"
            ),
            ["CoverCarOp_B29"] = new InputMapping(
                Sheet: "Cover Car Operations",
                Cell: "B29",
                Options: new[] { "Front" },
                DefaultValue: "Front"
            ),
            ["CoverCarOp_B30"] = new InputMapping(
                Sheet: "Cover Car Operations",
                Cell: "B30",
                Options: new[] { "Side" },
                DefaultValue: "Side"
            ),
            ["CoverCarOp_B31"] = new InputMapping(
                Sheet: "Cover Car Operations",
                Cell: "B31",
                Options: new[] { "Rear" },
                DefaultValue: "Rear"
            ),
            ["CoverCarOp_C29"] = new InputMapping(
                Sheet: "Cover Car Operations",
                Cell: "C29",
                Options: new[] { "Refinish" },
                DefaultValue: "Refinish"
            ),
            ["CoverCarOp_C30"] = new InputMapping(
                Sheet: "Cover Car Operations",
                Cell: "C30",
                Options: new[] { "Repair" },
                DefaultValue: "Repair"
            ),
            ["CoverCarOp_D29"] = new InputMapping(
                Sheet: "Cover Car Operations",
                Cell: "D29",
                Options: new[] { "$ and Body Labor", "Refinish Labor" },
                DefaultValue: "Refinish Labor"
            ),

            // ===== Body Operations Inputs =====
            ["BodyOp_A79"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "A79",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["BodyOp_D79"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D79",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["BodyOp_A31"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "A31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["BodyOp_B31"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "B31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["BodyOp_C31"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "C31",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["BodyOp_D91"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D91",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["BodyOp_A81"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "A81",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["BodyOp_A29"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "A29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["BodyOp_B29"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "B29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["BodyOp_C29"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "C29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["BodyOp_D29"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["BodyOp_A33"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "A33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["BodyOp_B33"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "B33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["BodyOp_C33"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "C33",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["BodyOp_D33"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D33",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["BodyOp_D38"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D38",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["BodyOp_D89"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D89",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["BodyOp_B35"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "B35",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["BodyOp_C35"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "C35",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["BodyOp_D35"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D35",
                Options: new[] { "Dent Puller" },
                DefaultValue: ""
            ),
            ["BodyOp_D36"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D36",
                Options: new[] { "Glue Puller" },
                DefaultValue: ""
            ),
            ["BodyOp_D81"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D81",
                Options: new[] { "Pre" },
                DefaultValue: "Pre"
            ),
            ["BodyOp_D82"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D82",
                Options: new[] { "In-Process" },
                DefaultValue: "In-Process"
            ),
            ["BodyOp_D83"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D83",
                Options: new[] { "Post" },
                DefaultValue: "Post"
            ),
            ["BodyOp_D87"] = new InputMapping(
                Sheet: "Body Operations",
                Cell: "D87",
                Options: new[] { "Pinch Welds", "Truck Clamps" },
                DefaultValue: "Pinch Welds"
            ),

            // ===== Refinish Operations Inputs =====
            ["RefinishOp_B35"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "B35",
                Options: new[] { "2-Stage", "3-Stage", "4-Stage" },
                DefaultValue: "2-Stage"
            ),
            ["RefinishOp_B43"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "B43",
                Options: new[] { "2-Stage", "3-Stage", "4-Stage" },
                DefaultValue: "3-Stage"
            ),
            ["RefinishOp_A29"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "A29",
                Options: new[] { "2-Stage", "3-Stage", "4-Stage" },
                DefaultValue: "2-Stage"
            ),
            ["RefinishOp_B39"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "B39",
                Options: new[] { "2-Stage", "3-Stage", "4-Stage" },
                DefaultValue: "3-Stage"
            ),
            ["RefinishOp_B31"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "B31",
                Options: new[] { "2-Stage", "3-Stage", "4-Stage" },
                DefaultValue: "2-Stage"
            ),
            ["RefinishOp_D31"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "D31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["RefinishOp_A31"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "A31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["RefinishOp_C29"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "C29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["RefinishOp_D29"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "D29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["RefinishOp_D33"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "D33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["RefinishOp_A33"] = new InputMapping(
                Sheet: "Refinish Operations",
                Cell: "A33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),

            // ===== Mechanical Operations Inputs =====
            ["MechanicalOp_A29"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A29",
                Options: new[] { "R134a", "R1234yf", "R744" },
                DefaultValue: "R1234yf"
            ),
            ["MechanicalOp_C179"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "C179",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_D179"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "D179",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_A140"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A140",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_B31"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "B31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_C31"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "C31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_B79"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "B79",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["MechanicalOp_A133"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A133",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_B133"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "B133",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_A131"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A131",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["MechanicalOp_B131"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "B131",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_C131"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "C131",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_B81"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "B81",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["MechanicalOp_A191"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A191",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_A142"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A142",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_C185"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "C185",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["MechanicalOp_B29"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "B29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["MechanicalOp_C29"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "C29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_D183"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "D183",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_A187"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A187",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_A135"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A135",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_B135"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "B135",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_C181"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "C181",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_D181"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "D181",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            ["MechanicalOp_A189"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A189",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_B33"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "B33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_C33"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "C33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_A129"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A129",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_B129"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "B129",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_C129"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "C129",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_D129"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "D129",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_A185"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A185",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["MechanicalOp_A137"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A137",
                Options: new[] { "Measure" },
                DefaultValue: ""
            ),
            ["MechanicalOp_A138"] = new InputMapping(
                Sheet: "Mechanical Operations",
                Cell: "A138",
                Options: new[] { "Calibrate" },
                DefaultValue: ""
            ),

            // ===== SRS Operations Inputs =====
            ["SRSOp_A79"] = new InputMapping(
                Sheet: "SRS Operations",
                Cell: "A79",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["SRSOp_A31"] = new InputMapping(
                Sheet: "SRS Operations",
                Cell: "A31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["SRSOp_A35"] = new InputMapping(
                Sheet: "SRS Operations",
                Cell: "A35",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["SRSOp_A83"] = new InputMapping(
                Sheet: "SRS Operations",
                Cell: "A83",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["SRSOp_A29"] = new InputMapping(
                Sheet: "SRS Operations",
                Cell: "A29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["SRSOp_A87"] = new InputMapping(
                Sheet: "SRS Operations",
                Cell: "A87",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["SRSOp_A81"] = new InputMapping(
                Sheet: "SRS Operations",
                Cell: "A81",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["SRSOp_A85"] = new InputMapping(
                Sheet: "SRS Operations",
                Cell: "A85",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["SRSOp_A33"] = new InputMapping(
                Sheet: "SRS Operations",
                Cell: "A33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),

            // ===== Total Loss Charges Inputs =====

            // --- Standard Fees ---
            // Administration Fee Price (numeric input - A29)
            ["TotalLossCharges_AdminFeePrice"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "A29",
                Options: Array.Empty<string>(), // Numeric input
                DefaultValue: "200"
            ),
            // Yard Fee Price (dropdown - A31)
            ["TotalLossCharges_YardFeePrice"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "A31",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            // Yard Fee Removal Coordination Fee (A33)
            ["TotalLossCharges_YardFeeRemovalCoord"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "A33",
                Options: Array.Empty<string>(), // Numeric input
                DefaultValue: ""
            ),

            // --- Additional ---
            // Blueprint Labor Expense (B29)
            ["TotalLossCharges_BlueprintLaborExpense"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "B29",
                Options: Array.Empty<string>(), // Text/numeric input
                DefaultValue: ""
            ),
            // Collision Damage Inspection (B31 dropdown)
            ["TotalLossCharges_B31"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "B31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            // # of Collision Wrap Areas (B33 numeric)
            ["TotalLossCharges_CollisionWrapAreas"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "B33",
                Options: Array.Empty<string>(), // Numeric input
                DefaultValue: "2"
            ),
            // Cover Electrical Connections (B35 dropdown)
            ["TotalLossCharges_B35"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "B35",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            // Customer Assistance Fee (B37)
            ["TotalLossCharges_CustomerAssistanceFee"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "B37",
                Options: Array.Empty<string>(), // Text/numeric input
                DefaultValue: ""
            ),
            // Delete Customer Info (B39 dropdown)
            ["TotalLossCharges_B39"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "B39",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            // Hazardous Waste Disposal (B41 dropdown)
            ["TotalLossCharges_B41"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "B41",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            // Seat, Wheel Cover, Floor Mat (B43 dropdown)
            ["TotalLossCharges_B43"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "B43",
                Options: new[] { "Yes", "No" },
                DefaultValue: ""
            ),
            // Spray Out Card (B45 dropdown)
            ["TotalLossCharges_B45"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "B45",
                Options: new[] { "2-Stage", "3-Stage", "4-Stage" },
                DefaultValue: ""
            ),

            // --- Parts ---
            // Broken Glass Cleanup Labor (C29)
            ["TotalLossCharges_BrokenGlassCleanup"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "C29",
                Options: Array.Empty<string>(), // Text input
                DefaultValue: ""
            ),
            // Parts Handling Fee (C31 dropdown)
            ["TotalLossCharges_C31"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "C31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            // Parts Restocking Fee (C33 numeric)
            ["TotalLossCharges_PartsRestockingFee"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "C33",
                Options: Array.Empty<string>(), // Numeric input
                DefaultValue: "1000"
            ),

            // --- Storage ---
            // Storage 1 Location (D29 dropdown)
            ["TotalLossCharges_D29"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "D29",
                Options: new[] { "Inside", "Outside", "Upstairs" },
                DefaultValue: "Upstairs"
            ),
            // Storage 1 Start Date (E29)
            ["TotalLossCharges_Storage1StartDate"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "E29",
                Options: Array.Empty<string>(), // Date input
                DefaultValue: ""
            ),
            // Storage 1 End Date (E30)
            ["TotalLossCharges_Storage1EndDate"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "E30",
                Options: Array.Empty<string>(), // Date input
                DefaultValue: ""
            ),
            // Storage 2 Location (D34 dropdown)
            ["TotalLossCharges_D34"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "D34",
                Options: new[] { "Inside", "Outside", "Upstairs" },
                DefaultValue: ""
            ),
            // Storage 2 Start Date (E34)
            ["TotalLossCharges_Storage2StartDate"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "E34",
                Options: Array.Empty<string>(), // Date input
                DefaultValue: ""
            ),
            // Storage 2 End Date (E35)
            ["TotalLossCharges_Storage2EndDate"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "E35",
                Options: Array.Empty<string>(), // Date input
                DefaultValue: ""
            ),
            // Storage 3 Location (D39 dropdown)
            ["TotalLossCharges_D39"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "D39",
                Options: new[] { "Inside", "Outside", "Upstairs" },
                DefaultValue: ""
            ),
            // Storage 3 Start Date (E39)
            ["TotalLossCharges_Storage3StartDate"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "E39",
                Options: Array.Empty<string>(), // Date input
                DefaultValue: ""
            ),
            // Storage 3 End Date (E40)
            ["TotalLossCharges_Storage3EndDate"] = new InputMapping(
                Sheet: "Total Loss Charges",
                Cell: "E40",
                Options: Array.Empty<string>(), // Date input
                DefaultValue: ""
            ),

            // ===== Body On Frame Inputs =====
            ["BodyOnFrame_A33"] = new InputMapping(
                Sheet: "Body On Frame",
                Cell: "A33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["BodyOnFrame_A31"] = new InputMapping(
                Sheet: "Body On Frame",
                Cell: "A31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["BodyOnFrame_B31"] = new InputMapping(
                Sheet: "Body On Frame",
                Cell: "B31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["BodyOnFrame_B29"] = new InputMapping(
                Sheet: "Body On Frame",
                Cell: "B29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),

            // ===== Stolen Recovery  Inputs =====
            ["StolenRecovery_A33"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "A33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["StolenRecovery_B33"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "B33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["StolenRecovery_C33"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "C33",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["StolenRecovery_B29"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "B29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["StolenRecovery_C29"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "C29",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["StolenRecovery_A31"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "A31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["StolenRecovery_B31"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "B31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["StolenRecovery_C31"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "C31",
                Options: new[] { "Yes", "No" },
                DefaultValue: "Yes"
            ),
            ["StolenRecovery_A43"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "A43",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["StolenRecovery_A41"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "A41",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),
            ["StolenRecovery_A37"] = new InputMapping(
                Sheet: "Stolen Recovery ",
                Cell: "A37",
                Options: new[] { "Yes", "No" },
                DefaultValue: "No"
            ),

        };

        // FrozenDictionary provides faster lookups than Dictionary after initialization
        public static readonly FrozenDictionary<string, InputMapping> InputControls =
            _inputControlsSource.ToFrozenDictionary(StringComparer.Ordinal);

        // Cached lookup by sheet name for batch operations
        private static readonly Lazy<FrozenDictionary<string, List<InputMapping>>> _inputsBySheet =
            new(() => _inputControlsSource.Values
                .GroupBy(m => m.Sheet)
                .ToFrozenDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal));

        public static FrozenDictionary<string, List<InputMapping>> InputsBySheet => _inputsBySheet.Value;

        #endregion

        #region Output Cells (Calculated Results)

        /// <summary>
        /// Output field name -> (Sheet, Cell)
        /// </summary>
        private static readonly Dictionary<string, (string Sheet, string Cell)> _outputCellsSource = new()
        {
            // ===== SOP List Outputs =====
            ["SOPList_Summary1"] = ("SOP List", "O26"),
            ["SOPList_Summary2"] = ("SOP List", "O76"),
            ["SOPList_Summary3"] = ("SOP List", "M79"),
            ["SOPList_Summary4"] = ("SOP List", "O79"),
            ["SOPList_Summary5"] = ("SOP List", "R79"),
            ["SOPList_Summary6"] = ("SOP List", "V79"),
            ["SOPList_Summary7"] = ("SOP List", "M80"),
            ["SOPList_Summary8"] = ("SOP List", "O80"),
            ["SOPList_Summary9"] = ("SOP List", "R80"),
            ["SOPList_Summary10"] = ("SOP List", "V80"),

            // ===== Part Operations Outputs =====
            ["PartOp_Summary1"] = ("Part Operations", "O30"),
            ["PartOp_Summary2"] = ("Part Operations", "M33"),
            ["PartOp_Summary3"] = ("Part Operations", "N33"),
            ["PartOp_Summary4"] = ("Part Operations", "M35"),
            ["PartOp_Summary5"] = ("Part Operations", "N35"),
            ["PartOp_Summary6"] = ("Part Operations", "M38"),
            ["PartOp_Summary7"] = ("Part Operations", "N38"),
            ["PartOp_Summary8"] = ("Part Operations", "O38"),
            ["PartOp_Summary9"] = ("Part Operations", "P38"),
            ["PartOp_Summary10"] = ("Part Operations", "M40"),

            // ===== Cover Car Operations Outputs =====
            ["CoverCarOp_Summary1"] = ("Cover Car Operations", "O26"),
            ["CoverCarOp_Summary2"] = ("Cover Car Operations", "M29"),
            ["CoverCarOp_Summary3"] = ("Cover Car Operations", "R29"),
            ["CoverCarOp_Summary4"] = ("Cover Car Operations", "V29"),
            ["CoverCarOp_Summary5"] = ("Cover Car Operations", "X29"),
            ["CoverCarOp_Summary6"] = ("Cover Car Operations", "M30"),
            ["CoverCarOp_Summary7"] = ("Cover Car Operations", "R30"),
            ["CoverCarOp_Summary8"] = ("Cover Car Operations", "V30"),
            ["CoverCarOp_Summary9"] = ("Cover Car Operations", "X30"),
            ["CoverCarOp_Summary10"] = ("Cover Car Operations", "M31"),

            // ===== Body Operations Outputs =====
            ["BodyOp_Summary1"] = ("Body Operations", "O26"),
            ["BodyOp_Summary2"] = ("Body Operations", "M35"),
            ["BodyOp_Summary3"] = ("Body Operations", "N35"),
            ["BodyOp_Summary4"] = ("Body Operations", "O43"),
            ["BodyOp_Summary5"] = ("Body Operations", "P43"),
            ["BodyOp_Summary6"] = ("Body Operations", "O76"),

            // ===== Refinish Operations Outputs =====
            ["RefinishOp_Summary1"] = ("Refinish Operations", "O26"),
            ["RefinishOp_Summary2"] = ("Refinish Operations", "M32"),
            ["RefinishOp_Summary3"] = ("Refinish Operations", "N32"),
            ["RefinishOp_Summary4"] = ("Refinish Operations", "M33"),
            ["RefinishOp_Summary5"] = ("Refinish Operations", "N33"),
            ["RefinishOp_Summary6"] = ("Refinish Operations", "M34"),
            ["RefinishOp_Summary7"] = ("Refinish Operations", "N34"),
            ["RefinishOp_Summary8"] = ("Refinish Operations", "O34"),
            ["RefinishOp_Summary9"] = ("Refinish Operations", "P34"),
            ["RefinishOp_Summary10"] = ("Refinish Operations", "X34"),

            // ===== Mechanical Operations Outputs =====
            ["MechanicalOp_Summary1"] = ("Mechanical Operations", "O26"),
            ["MechanicalOp_Summary2"] = ("Mechanical Operations", "O76"),

            // ===== SRS Operations Outputs =====
            ["SRSOp_Summary1"] = ("SRS Operations", "O26"),
            ["SRSOp_Summary2"] = ("SRS Operations", "O76"),

            // ===== Total Loss Charges Outputs =====
            ["TotalLossCharges_Summary1"] = ("Total Loss Charges", "O26"),
            ["TotalLossCharges_Summary2"] = ("Total Loss Charges", "O32"),
            ["TotalLossCharges_Summary3"] = ("Total Loss Charges", "P32"),
            ["TotalLossCharges_Summary4"] = ("Total Loss Charges", "M40"),
            ["TotalLossCharges_Summary5"] = ("Total Loss Charges", "N40"),

            // ===== Body On Frame Outputs =====
            ["BodyOnFrame_Summary1"] = ("Body On Frame", "O26"),

            // ===== Stolen Recovery  Outputs =====
            ["StolenRecovery_Summary1"] = ("Stolen Recovery ", "O26"),

        };

        // FrozenDictionary for faster output lookups
        public static readonly FrozenDictionary<string, (string Sheet, string Cell)> OutputCells =
            _outputCellsSource.ToFrozenDictionary(StringComparer.Ordinal);

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get all input mappings for a specific sheet (optimized batch lookup)
        /// </summary>
        public static IReadOnlyList<InputMapping> GetInputsForSheet(string sheetName)
        {
            return InputsBySheet.TryGetValue(sheetName, out var mappings)
                ? mappings
                : Array.Empty<InputMapping>();
        }

        /// <summary>
        /// Try to get an input mapping by key (avoids exception on missing key)
        /// </summary>
        public static bool TryGetInput(string key, out InputMapping mapping)
        {
            return InputControls.TryGetValue(key, out mapping);
        }

        /// <summary>
        /// Try to get an output cell location by key
        /// </summary>
        public static bool TryGetOutput(string key, out (string Sheet, string Cell) location)
        {
            return OutputCells.TryGetValue(key, out location);
        }

        #endregion
    }

    /// <summary>
    /// Input mapping definition - optimized as readonly record struct for better performance
    /// </summary>
    public readonly record struct InputMapping(
        string Sheet,
        string Cell,
        string[] Options,
        string DefaultValue
    )
    {
        /// <summary>
        /// Returns true if this is a dropdown/selection input with defined options
        /// </summary>
        public bool HasOptions => Options.Length > 0;

        /// <summary>
        /// Returns true if this is a free-form input (text/numeric)
        /// </summary>
        public bool IsFreeForm => Options.Length == 0;
    }
}
