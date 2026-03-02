#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Knowledge base for vehicle parts - materials, attachment types, and related operations
    /// This enables "smart" interview that knows bumper = plastic, quarter panel = welded metal, etc.
    /// </summary>
    public static class PartKnowledgeBase
    {
        #region Enums

        public enum PartMaterial
        {
            Steel,
            Aluminum,
            Plastic,           // TPO, PP, ABS plastics
            SMC,               // Sheet Molded Compound (fiberglass)
            CFRP,              // Carbon Fiber Reinforced Plastic
            Glass,
            Rubber,
            Chrome,
            Composite
        }

        public enum AttachmentType
        {
            Bolted,            // Bolts on, easy R&R
            Welded,            // Spot/MIG welded, requires cut/weld
            Adhesive,          // Bonded with adhesive
            ClipOn,            // Plastic clips, snaps
            Hinged,            // Doors, hood, trunk
            Threaded,          // Screws
            RivetBonded,       // Rivets + adhesive (aluminum)
            Integral           // Part of structure, cannot be removed
        }

        public enum RepairCategory
        {
            ExteriorPanel,
            StructuralPanel,
            Bumper,
            Lighting,
            Glass,
            Mirror,
            Trim,
            Mechanical,
            Interior,
            Electrical
        }

        public enum DamageSeverity
        {
            None,
            Light,      // Scratches, scuffs, minor dents
            Moderate,   // Dents, creases repairable
            Heavy,      // Major damage, may need sectioning
            Severe      // Replace only
        }

        #endregion

        #region Part Definition

        public class PartDefinition
        {
            public string PartName { get; set; } = "";
            public string CCCPartName { get; set; } = "";  // How CCC/Mitchell names it
            public PartMaterial Material { get; set; }
            public AttachmentType Attachment { get; set; }
            public RepairCategory Category { get; set; }
            public bool CanPDR { get; set; }              // Can use PDR on metal
            public bool CanRepair { get; set; }           // Can repair vs replace only
            public bool NeedsRefinish { get; set; }       // Typically needs paint
            public string[] RelatedParts { get; set; } = Array.Empty<string>();  // Parts that need R&I when working on this
            public string[] AdjacentPanels { get; set; } = Array.Empty<string>(); // Panels that may need blend
            public decimal BaseRepairHours { get; set; }
            public decimal BaseRefinishHours { get; set; }
            public decimal BaseRIHours { get; set; }      // R&I time
        }

        #endregion

        #region Knowledge Base Data

        private static readonly Dictionary<string, PartDefinition> _parts = new()
        {
            // === BUMPERS (Plastic) - From Excel Part Operations ===
            // Excel: Plastic Part Repair (Bumper cover) Row 75-100
            // Excel: Plastic Part Replace (Bumper cover) Row 125-148
            ["front_bumper_cover"] = new PartDefinition
            {
                PartName = "Front Bumper Cover",
                CCCPartName = "Bumper Cover Front",
                Material = PartMaterial.Plastic,
                Attachment = AttachmentType.ClipOn,
                Category = RepairCategory.Bumper,
                CanPDR = false,
                CanRepair = true,  // Plastic repair possible
                NeedsRefinish = true,
                RelatedParts = new[] { "front_bumper_reinforcement", "front_bumper_absorber", "fog_lights", "front_parking_sensors" },
                AdjacentPanels = new[] { "hood", "left_fender", "right_fender" },
                BaseRepairHours = 2.0m,      // Excel: Repair Unit of Part = 2.0
                BaseRefinishHours = 2.8m,    // Excel: Exterior Refinish Unit = 2.8
                BaseRIHours = 1.0m,          // Excel: Trial Fit Labor Unit = 1.0 (Replace)
                // Additional from Excel:
                // Trial Fit (Repair) = 1.9
                // DE-NIB, Adhesion Promoter, Flex Additive options
            },
            ["rear_bumper_cover"] = new PartDefinition
            {
                PartName = "Rear Bumper Cover",
                CCCPartName = "Bumper Cover Rear",
                Material = PartMaterial.Plastic,
                Attachment = AttachmentType.ClipOn,
                Category = RepairCategory.Bumper,
                CanPDR = false,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "rear_bumper_reinforcement", "rear_bumper_absorber", "rear_parking_sensors", "backup_camera" },
                AdjacentPanels = new[] { "trunk_lid", "left_quarter_panel", "right_quarter_panel" },
                BaseRepairHours = 2.0m,      // Excel: Repair Unit of Part = 2.0
                BaseRefinishHours = 2.8m,    // Excel: Exterior Refinish Unit = 2.8
                BaseRIHours = 1.0m           // Excel: Trial Fit Labor Unit = 1.0 (Replace)
            },

            // === HOOD (Steel/Aluminum) ===
            ["hood"] = new PartDefinition
            {
                PartName = "Hood",
                CCCPartName = "Hood",
                Material = PartMaterial.Steel,  // Could be aluminum on some vehicles
                Attachment = AttachmentType.Hinged,
                Category = RepairCategory.ExteriorPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "hood_hinge_lh", "hood_hinge_rh", "hood_latch", "hood_struts", "hood_insulator" },
                AdjacentPanels = new[] { "left_fender", "right_fender", "cowl" },
                BaseRepairHours = 3.0m,
                BaseRefinishHours = 3.5m,
                BaseRIHours = 0.5m
            },

            // === FENDERS (Steel/Aluminum - Bolted) ===
            ["left_fender"] = new PartDefinition
            {
                PartName = "Left Fender",
                CCCPartName = "Fender LH",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Bolted,
                Category = RepairCategory.ExteriorPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "fender_liner_lh", "fender_molding_lh", "antenna" },
                AdjacentPanels = new[] { "hood", "left_front_door", "left_a_pillar" },
                BaseRepairHours = 2.5m,
                BaseRefinishHours = 3.0m,
                BaseRIHours = 0.8m
            },
            ["right_fender"] = new PartDefinition
            {
                PartName = "Right Fender",
                CCCPartName = "Fender RH",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Bolted,
                Category = RepairCategory.ExteriorPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "fender_liner_rh", "fender_molding_rh" },
                AdjacentPanels = new[] { "hood", "right_front_door", "right_a_pillar" },
                BaseRepairHours = 2.5m,
                BaseRefinishHours = 3.0m,
                BaseRIHours = 0.8m
            },

            // === DOORS (Steel - Hinged) ===
            ["left_front_door"] = new PartDefinition
            {
                PartName = "Left Front Door",
                CCCPartName = "Front Door LH",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Hinged,
                Category = RepairCategory.ExteriorPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "door_handle_lh_front", "door_mirror_lh", "door_molding_lh_front", "door_glass_lh_front", "door_regulator_lh_front" },
                AdjacentPanels = new[] { "left_fender", "left_rear_door", "left_rocker" },
                BaseRepairHours = 3.0m,
                BaseRefinishHours = 3.5m,
                BaseRIHours = 1.0m
            },
            ["left_rear_door"] = new PartDefinition
            {
                PartName = "Left Rear Door",
                CCCPartName = "Rear Door LH",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Hinged,
                Category = RepairCategory.ExteriorPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "door_handle_lh_rear", "door_molding_lh_rear", "door_glass_lh_rear", "door_regulator_lh_rear" },
                AdjacentPanels = new[] { "left_front_door", "left_quarter_panel", "left_rocker" },
                BaseRepairHours = 3.0m,
                BaseRefinishHours = 3.5m,
                BaseRIHours = 1.0m
            },
            ["right_front_door"] = new PartDefinition
            {
                PartName = "Right Front Door",
                CCCPartName = "Front Door RH",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Hinged,
                Category = RepairCategory.ExteriorPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "door_handle_rh_front", "door_mirror_rh", "door_molding_rh_front", "door_glass_rh_front", "door_regulator_rh_front" },
                AdjacentPanels = new[] { "right_fender", "right_rear_door", "right_rocker" },
                BaseRepairHours = 3.0m,
                BaseRefinishHours = 3.5m,
                BaseRIHours = 1.0m
            },
            ["right_rear_door"] = new PartDefinition
            {
                PartName = "Right Rear Door",
                CCCPartName = "Rear Door RH",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Hinged,
                Category = RepairCategory.ExteriorPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "door_handle_rh_rear", "door_molding_rh_rear", "door_glass_rh_rear", "door_regulator_rh_rear" },
                AdjacentPanels = new[] { "right_front_door", "right_quarter_panel", "right_rocker" },
                BaseRepairHours = 3.0m,
                BaseRefinishHours = 3.5m,
                BaseRIHours = 1.0m
            },

            // === QUARTER PANELS (Steel - WELDED) ===
            ["left_quarter_panel"] = new PartDefinition
            {
                PartName = "Left Quarter Panel",
                CCCPartName = "Quarter Panel LH",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Welded,  // This is the key difference!
                Category = RepairCategory.StructuralPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "quarter_glass_lh", "fuel_door", "tail_light_lh", "quarter_molding_lh", "wheel_opening_molding_lh" },
                AdjacentPanels = new[] { "left_rear_door", "trunk_lid", "roof", "rear_bumper_cover" },
                BaseRepairHours = 4.0m,
                BaseRefinishHours = 4.5m,
                BaseRIHours = 0m  // Welded - no simple R&I
            },
            ["right_quarter_panel"] = new PartDefinition
            {
                PartName = "Right Quarter Panel",
                CCCPartName = "Quarter Panel RH",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Welded,
                Category = RepairCategory.StructuralPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "quarter_glass_rh", "tail_light_rh", "quarter_molding_rh", "wheel_opening_molding_rh" },
                AdjacentPanels = new[] { "right_rear_door", "trunk_lid", "roof", "rear_bumper_cover" },
                BaseRepairHours = 4.0m,
                BaseRefinishHours = 4.5m,
                BaseRIHours = 0m
            },

            // === ROOF (Steel - WELDED) ===
            ["roof"] = new PartDefinition
            {
                PartName = "Roof",
                CCCPartName = "Roof Panel",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Welded,
                Category = RepairCategory.StructuralPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "headliner", "roof_moldings", "sunroof_assembly", "roof_rack" },
                AdjacentPanels = new[] { "left_quarter_panel", "right_quarter_panel", "windshield", "back_glass" },
                BaseRepairHours = 4.0m,
                BaseRefinishHours = 4.0m,
                BaseRIHours = 0m
            },

            // === TRUNK/TAILGATE (Steel - Hinged) ===
            ["trunk_lid"] = new PartDefinition
            {
                PartName = "Trunk Lid",
                CCCPartName = "Trunk Lid",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Hinged,
                Category = RepairCategory.ExteriorPanel,
                CanPDR = true,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "trunk_hinge_lh", "trunk_hinge_rh", "trunk_struts", "trunk_latch", "trunk_weatherstrip", "license_plate_lights" },
                AdjacentPanels = new[] { "left_quarter_panel", "right_quarter_panel", "rear_bumper_cover" },
                BaseRepairHours = 2.5m,
                BaseRefinishHours = 3.0m,
                BaseRIHours = 0.5m
            },

            // === ROCKER PANELS (Steel - WELDED) ===
            ["left_rocker"] = new PartDefinition
            {
                PartName = "Left Rocker Panel",
                CCCPartName = "Rocker Panel LH",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Welded,
                Category = RepairCategory.StructuralPanel,
                CanPDR = false,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "rocker_molding_lh" },
                AdjacentPanels = new[] { "left_front_door", "left_rear_door", "left_quarter_panel" },
                BaseRepairHours = 3.0m,
                BaseRefinishHours = 2.0m,
                BaseRIHours = 0m
            },
            ["right_rocker"] = new PartDefinition
            {
                PartName = "Right Rocker Panel",
                CCCPartName = "Rocker Panel RH",
                Material = PartMaterial.Steel,
                Attachment = AttachmentType.Welded,
                Category = RepairCategory.StructuralPanel,
                CanPDR = false,
                CanRepair = true,
                NeedsRefinish = true,
                RelatedParts = new[] { "rocker_molding_rh" },
                AdjacentPanels = new[] { "right_front_door", "right_rear_door", "right_quarter_panel" },
                BaseRepairHours = 3.0m,
                BaseRefinishHours = 2.0m,
                BaseRIHours = 0m
            },

            // === GRILLE (Plastic/Chrome) ===
            ["grille"] = new PartDefinition
            {
                PartName = "Grille",
                CCCPartName = "Grille",
                Material = PartMaterial.Plastic,
                Attachment = AttachmentType.ClipOn,
                Category = RepairCategory.Trim,
                CanPDR = false,
                CanRepair = false,  // Grilles typically replace only
                NeedsRefinish = false, // Often comes finished
                RelatedParts = new[] { "grille_molding", "active_grille_shutter" },
                AdjacentPanels = Array.Empty<string>(),
                BaseRepairHours = 0m,
                BaseRefinishHours = 0m,
                BaseRIHours = 0.3m
            },

            // === HEADLIGHTS (Plastic lens) ===
            ["headlight_lh"] = new PartDefinition
            {
                PartName = "Left Headlight",
                CCCPartName = "Headlamp Assembly LH",
                Material = PartMaterial.Plastic,
                Attachment = AttachmentType.Bolted,
                Category = RepairCategory.Lighting,
                CanPDR = false,
                CanRepair = false,
                NeedsRefinish = false,
                RelatedParts = new[] { "headlight_bracket_lh", "headlight_ballast_lh" },
                AdjacentPanels = Array.Empty<string>(),
                BaseRepairHours = 0m,
                BaseRefinishHours = 0m,
                BaseRIHours = 0.5m
            },
            ["headlight_rh"] = new PartDefinition
            {
                PartName = "Right Headlight",
                CCCPartName = "Headlamp Assembly RH",
                Material = PartMaterial.Plastic,
                Attachment = AttachmentType.Bolted,
                Category = RepairCategory.Lighting,
                CanPDR = false,
                CanRepair = false,
                NeedsRefinish = false,
                RelatedParts = new[] { "headlight_bracket_rh", "headlight_ballast_rh" },
                AdjacentPanels = Array.Empty<string>(),
                BaseRepairHours = 0m,
                BaseRefinishHours = 0m,
                BaseRIHours = 0.5m
            },

            // === TAILLIGHTS ===
            ["tail_light_lh"] = new PartDefinition
            {
                PartName = "Left Tail Light",
                CCCPartName = "Tail Lamp Assembly LH",
                Material = PartMaterial.Plastic,
                Attachment = AttachmentType.Bolted,
                Category = RepairCategory.Lighting,
                CanPDR = false,
                CanRepair = false,
                NeedsRefinish = false,
                RelatedParts = Array.Empty<string>(),
                AdjacentPanels = Array.Empty<string>(),
                BaseRepairHours = 0m,
                BaseRefinishHours = 0m,
                BaseRIHours = 0.3m
            },
            ["tail_light_rh"] = new PartDefinition
            {
                PartName = "Right Tail Light",
                CCCPartName = "Tail Lamp Assembly RH",
                Material = PartMaterial.Plastic,
                Attachment = AttachmentType.Bolted,
                Category = RepairCategory.Lighting,
                CanPDR = false,
                CanRepair = false,
                NeedsRefinish = false,
                RelatedParts = Array.Empty<string>(),
                AdjacentPanels = Array.Empty<string>(),
                BaseRepairHours = 0m,
                BaseRefinishHours = 0m,
                BaseRIHours = 0.3m
            },

            // === MIRRORS ===
            ["door_mirror_lh"] = new PartDefinition
            {
                PartName = "Left Door Mirror",
                CCCPartName = "Door Mirror LH",
                Material = PartMaterial.Plastic,
                Attachment = AttachmentType.Bolted,
                Category = RepairCategory.Mirror,
                CanPDR = false,
                CanRepair = false,
                NeedsRefinish = true,  // Mirror caps often painted
                RelatedParts = Array.Empty<string>(),
                AdjacentPanels = Array.Empty<string>(),
                BaseRepairHours = 0m,
                BaseRefinishHours = 0.5m,
                BaseRIHours = 0.3m
            },
            ["door_mirror_rh"] = new PartDefinition
            {
                PartName = "Right Door Mirror",
                CCCPartName = "Door Mirror RH",
                Material = PartMaterial.Plastic,
                Attachment = AttachmentType.Bolted,
                Category = RepairCategory.Mirror,
                CanPDR = false,
                CanRepair = false,
                NeedsRefinish = true,
                RelatedParts = Array.Empty<string>(),
                AdjacentPanels = Array.Empty<string>(),
                BaseRepairHours = 0m,
                BaseRefinishHours = 0.5m,
                BaseRIHours = 0.3m
            },

            // === GLASS ===
            ["windshield"] = new PartDefinition
            {
                PartName = "Windshield",
                CCCPartName = "Windshield",
                Material = PartMaterial.Glass,
                Attachment = AttachmentType.Adhesive,
                Category = RepairCategory.Glass,
                CanPDR = false,
                CanRepair = false,  // Chip repair possible but typically replace
                NeedsRefinish = false,
                RelatedParts = new[] { "windshield_molding", "rain_sensor", "adas_camera", "rearview_mirror" },
                AdjacentPanels = Array.Empty<string>(),
                BaseRepairHours = 0m,
                BaseRefinishHours = 0m,
                BaseRIHours = 1.5m
            },
            ["back_glass"] = new PartDefinition
            {
                PartName = "Back Glass",
                CCCPartName = "Back Window Glass",
                Material = PartMaterial.Glass,
                Attachment = AttachmentType.Adhesive,
                Category = RepairCategory.Glass,
                CanPDR = false,
                CanRepair = false,
                NeedsRefinish = false,
                RelatedParts = new[] { "back_glass_molding", "defroster_grid" },
                AdjacentPanels = Array.Empty<string>(),
                BaseRepairHours = 0m,
                BaseRefinishHours = 0m,
                BaseRIHours = 1.0m
            }
        };

        #endregion

        #region Public API

        /// <summary>
        /// Get part definition by ID
        /// </summary>
        public static PartDefinition? GetPart(string partId)
        {
            return _parts.TryGetValue(partId, out var part) ? part : null;
        }

        /// <summary>
        /// Get all parts in the knowledge base
        /// </summary>
        public static IEnumerable<KeyValuePair<string, PartDefinition>> GetAllParts() => _parts;

        /// <summary>
        /// Determine if a part is welded (affects repair vs replace decision)
        /// </summary>
        public static bool IsWeldedPart(string partId)
        {
            var part = GetPart(partId);
            return part?.Attachment == AttachmentType.Welded;
        }

        /// <summary>
        /// Determine if a part is plastic (affects repair method)
        /// </summary>
        public static bool IsPlasticPart(string partId)
        {
            var part = GetPart(partId);
            return part?.Material == PartMaterial.Plastic || part?.Material == PartMaterial.SMC;
        }

        /// <summary>
        /// Determine if a part can use PDR
        /// </summary>
        public static bool CanUsePDR(string partId)
        {
            var part = GetPart(partId);
            return part?.CanPDR == true;
        }

        /// <summary>
        /// Generate operations for a part based on damage severity
        /// This is the "smart" part - it knows what operations apply to what parts
        /// </summary>
        public static List<PartKnowledgeOperation> GenerateOperations(string partId, DamageSeverity severity, bool includeRefinish = true)
        {
            var operations = new List<PartKnowledgeOperation>();
            var part = GetPart(partId);
            if (part == null || severity == DamageSeverity.None) return operations;

            // Determine primary operation based on material, attachment, and severity
            if (severity == DamageSeverity.Light && part.CanPDR && part.Material == PartMaterial.Steel)
            {
                // PDR for light steel damage
                operations.Add(new PartKnowledgeOperation
                {
                    OperationType = "PDR",
                    PartName = part.CCCPartName,
                    LaborHours = 2.0m,
                    IsStructural = false,
                    Notes = "Paintless Dent Repair - metal panel"
                });
            }
            else if (severity == DamageSeverity.Light && part.Material == PartMaterial.Plastic)
            {
                // Plastic repair for light damage
                operations.Add(new PartKnowledgeOperation
                {
                    OperationType = "Repair",
                    PartName = part.CCCPartName,
                    LaborHours = part.BaseRepairHours,
                    IsStructural = false,
                    Notes = "Plastic repair - scuff/scratch repair"
                });
                if (includeRefinish && part.NeedsRefinish)
                {
                    operations.Add(new PartKnowledgeOperation
                    {
                        OperationType = "Refinish",
                        PartName = part.CCCPartName,
                        PaintHours = part.BaseRefinishHours,
                        Notes = "Refinish - plastic part"
                    });
                }
            }
            else if ((severity == DamageSeverity.Moderate || severity == DamageSeverity.Light) && part.CanRepair)
            {
                // Conventional repair
                operations.Add(new PartKnowledgeOperation
                {
                    OperationType = "Repair",
                    PartName = part.CCCPartName,
                    LaborHours = part.BaseRepairHours,
                    IsStructural = part.Category == RepairCategory.StructuralPanel,
                    Notes = part.Material == PartMaterial.Plastic ? "Plastic repair" : "Metal repair"
                });
                if (includeRefinish && part.NeedsRefinish)
                {
                    operations.Add(new PartKnowledgeOperation
                    {
                        OperationType = "Refinish",
                        PartName = part.CCCPartName,
                        PaintHours = part.BaseRefinishHours,
                        Notes = ""
                    });
                }
            }
            else if (severity == DamageSeverity.Heavy && part.Attachment == AttachmentType.Welded)
            {
                // Welded panel with heavy damage - sectioning
                operations.Add(new PartKnowledgeOperation
                {
                    OperationType = "Section",
                    PartName = part.CCCPartName,
                    LaborHours = 8.0m,
                    IsStructural = true,
                    Notes = "Welded panel - section repair"
                });
                if (includeRefinish && part.NeedsRefinish)
                {
                    operations.Add(new PartKnowledgeOperation
                    {
                        OperationType = "Refinish",
                        PartName = part.CCCPartName,
                        PaintHours = part.BaseRefinishHours,
                        Notes = ""
                    });
                }
            }
            else // Severe damage or replace-only parts
            {
                // Replace operation
                if (part.Attachment == AttachmentType.Welded)
                {
                    // Welded replacement
                    operations.Add(new PartKnowledgeOperation
                    {
                        OperationType = "Replace (Welded)",
                        PartName = part.CCCPartName,
                        LaborHours = 0m, // Per Mitchell/CCC database
                        IsStructural = true,
                        Notes = "Exterior welded panel replacement"
                    });
                }
                else
                {
                    // Bolted/hinged replacement
                    operations.Add(new PartKnowledgeOperation
                    {
                        OperationType = "Replace",
                        PartName = part.CCCPartName,
                        LaborHours = part.BaseRIHours,
                        IsStructural = false,
                        Notes = ""
                    });
                }

                if (includeRefinish && part.NeedsRefinish)
                {
                    operations.Add(new PartKnowledgeOperation
                    {
                        OperationType = "Refinish",
                        PartName = part.CCCPartName,
                        PaintHours = part.BaseRefinishHours,
                        Notes = ""
                    });
                }
            }

            return operations;
        }

        /// <summary>
        /// Get R&I operations for related parts that need to be removed
        /// </summary>
        public static List<PartKnowledgeOperation> GetRelatedRIOperations(string partId)
        {
            var operations = new List<PartKnowledgeOperation>();
            var part = GetPart(partId);
            if (part == null) return operations;

            foreach (var relatedPartId in part.RelatedParts)
            {
                var relatedPart = GetPart(relatedPartId);
                var partName = relatedPart?.CCCPartName ?? FormatPartName(relatedPartId);

                operations.Add(new PartKnowledgeOperation
                {
                    OperationType = "R&I",
                    PartName = partName,
                    LaborHours = relatedPart?.BaseRIHours ?? 0.3m,
                    Notes = "Required for access"
                });
            }

            return operations;
        }

        /// <summary>
        /// Get blend operations for adjacent panels
        /// </summary>
        public static List<PartKnowledgeOperation> GetBlendOperations(string partId)
        {
            var operations = new List<PartKnowledgeOperation>();
            var part = GetPart(partId);
            if (part == null) return operations;

            foreach (var adjacentId in part.AdjacentPanels)
            {
                var adjacentPart = GetPart(adjacentId);
                var partName = adjacentPart?.CCCPartName ?? FormatPartName(adjacentId);

                operations.Add(new PartKnowledgeOperation
                {
                    OperationType = "Blend",
                    PartName = partName,
                    PaintHours = 0.5m,
                    Notes = "Color match blend"
                });
            }

            return operations;
        }

        /// <summary>
        /// Get material description for display
        /// </summary>
        public static string GetMaterialDescription(string partId)
        {
            var part = GetPart(partId);
            if (part == null) return "Unknown";

            return part.Material switch
            {
                PartMaterial.Steel => "Steel",
                PartMaterial.Aluminum => "Aluminum",
                PartMaterial.Plastic => "Plastic (TPO/PP)",
                PartMaterial.SMC => "SMC (Fiberglass)",
                PartMaterial.CFRP => "Carbon Fiber",
                PartMaterial.Glass => "Glass",
                _ => part.Material.ToString()
            };
        }

        /// <summary>
        /// Get attachment description for display
        /// </summary>
        public static string GetAttachmentDescription(string partId)
        {
            var part = GetPart(partId);
            if (part == null) return "Unknown";

            return part.Attachment switch
            {
                AttachmentType.Bolted => "Bolted (R&R)",
                AttachmentType.Welded => "Welded (Cut & Weld)",
                AttachmentType.Adhesive => "Adhesive Bonded",
                AttachmentType.ClipOn => "Clip-On",
                AttachmentType.Hinged => "Hinged (Bolted)",
                AttachmentType.RivetBonded => "Rivet-Bonded",
                _ => part.Attachment.ToString()
            };
        }

        private static string FormatPartName(string partId)
        {
            return string.Join(" ", partId.Split('_').Select(s =>
                char.ToUpper(s[0]) + s.Substring(1)));
        }

        #endregion
    }

    #region Smart Operation Class

    /// <summary>
    /// Represents an operation generated by the smart PartKnowledgeBase
    /// (Named PartKnowledgeOperation to avoid conflict with GeneratedOperation in EstimateLearningService)
    /// </summary>
    public class PartKnowledgeOperation
    {
        public string OperationType { get; set; } = "";
        public string PartName { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal PaintHours { get; set; }
        public decimal Price { get; set; }
        public bool IsStructural { get; set; }
        public string Notes { get; set; } = "";
        public int Quantity { get; set; } = 1;
    }

    #endregion
}
