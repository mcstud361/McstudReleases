#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Services;

/// <summary>
/// Service that suggests materials and consumables based on repair operations.
/// Helps estimators remember drill bits, sander belts, welding supplies, etc.
/// </summary>
public class MaterialSuggestionService
{
    private static MaterialSuggestionService? _instance;
    public static MaterialSuggestionService Instance => _instance ??= new MaterialSuggestionService();

    // Material categories
    public static class Categories
    {
        public const string Cutting = "Cutting Tools";
        public const string Welding = "Welding Supplies";
        public const string Sanding = "Sanding/Abrasives";
        public const string Corrosion = "Corrosion Protection";
        public const string Sealing = "Sealers & Adhesives";
        public const string Masking = "Masking Materials";
        public const string Refinish = "Refinish Materials";
        public const string Hardware = "Hardware/Fasteners";
        public const string Safety = "Safety Equipment";
        public const string Electrical = "Electrical";
        public const string Glass = "Glass/Trim";
        public const string Structural = "Structural";
    }

    /// <summary>
    /// Represents a suggested material with details
    /// </summary>
    public class MaterialSuggestion
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string? PartNumber { get; set; }
        public bool IsConsumable { get; set; } = true;
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Operation type to material mappings
    /// </summary>
    private readonly Dictionary<string, List<MaterialSuggestion>> _operationMaterials;

    private MaterialSuggestionService()
    {
        _operationMaterials = BuildMaterialDatabase();
    }

    private Dictionary<string, List<MaterialSuggestion>> BuildMaterialDatabase()
    {
        return new Dictionary<string, List<MaterialSuggestion>>(StringComparer.OrdinalIgnoreCase)
        {
            // ========== WELDED PANEL REPLACEMENT ==========
            ["welded panel replace"] = new List<MaterialSuggestion>
            {
                new() { Name = "Spot Weld Cutter Bits", Category = Categories.Cutting, Description = "8mm spot weld drill bits for removing factory welds", IsConsumable = true },
                new() { Name = "Drill Bits (Cobalt)", Category = Categories.Cutting, Description = "1/4\" cobalt drill bits for panel access", IsConsumable = true },
                new() { Name = "Cut-Off Wheels", Category = Categories.Cutting, Description = "3\" cut-off wheels for panel sectioning", IsConsumable = true },
                new() { Name = "Saw Blades", Category = Categories.Cutting, Description = "Reciprocating saw blades (18 TPI metal)", IsConsumable = true },
                new() { Name = "MIG Welding Wire", Category = Categories.Welding, Description = ".023\" or .030\" ER70S-6 welding wire", IsConsumable = true },
                new() { Name = "Welding Gas (C25)", Category = Categories.Welding, Description = "75% Argon / 25% CO2 shielding gas", IsConsumable = true },
                new() { Name = "Contact Tips", Category = Categories.Welding, Description = "MIG gun contact tips (.023\" or .030\")", IsConsumable = true },
                new() { Name = "Welding Nozzles", Category = Categories.Welding, Description = "MIG gun nozzles", IsConsumable = true },
                new() { Name = "Weld-Thru Primer", Category = Categories.Corrosion, Description = "Zinc-rich weld-through primer for flanges", IsConsumable = true },
                new() { Name = "Cavity Wax", Category = Categories.Corrosion, Description = "Internal cavity corrosion protection", IsConsumable = true },
                new() { Name = "Self-Etch Primer", Category = Categories.Corrosion, Description = "For bare metal preparation", IsConsumable = true },
                new() { Name = "Seam Sealer", Category = Categories.Sealing, Description = "OEM-style seam sealer for panel joints", IsConsumable = true },
                new() { Name = "Panel Adhesive", Category = Categories.Sealing, Description = "Structural panel bonding adhesive", IsConsumable = true },
                new() { Name = "36 Grit Sanding Discs", Category = Categories.Sanding, Description = "For weld grinding and metal prep", IsConsumable = true },
                new() { Name = "80 Grit Sanding Discs", Category = Categories.Sanding, Description = "For feathering and final prep", IsConsumable = true },
                new() { Name = "Grinding Discs", Category = Categories.Sanding, Description = "4.5\" grinding wheels for weld cleanup", IsConsumable = true },
                new() { Name = "Flap Discs", Category = Categories.Sanding, Description = "For finish grinding welds", IsConsumable = true },
                new() { Name = "Cleco Fasteners", Category = Categories.Hardware, Description = "Temporary panel alignment fasteners", IsConsumable = false, Notes = "Reusable" },
                new() { Name = "Welding Blanket", Category = Categories.Safety, Description = "Fire protection blanket", IsConsumable = false },
            },

            ["welded"] = new List<MaterialSuggestion>
            {
                new() { Name = "Spot Weld Cutter Bits", Category = Categories.Cutting, Description = "8mm spot weld drill bits", IsConsumable = true },
                new() { Name = "MIG Welding Wire", Category = Categories.Welding, Description = ".023\" or .030\" ER70S-6", IsConsumable = true },
                new() { Name = "Welding Gas", Category = Categories.Welding, Description = "75/25 Argon/CO2 mix", IsConsumable = true },
                new() { Name = "Contact Tips", Category = Categories.Welding, Description = "MIG gun contact tips", IsConsumable = true },
                new() { Name = "Weld-Thru Primer", Category = Categories.Corrosion, Description = "Zinc-rich primer for flanges", IsConsumable = true },
                new() { Name = "Grinding Discs", Category = Categories.Sanding, Description = "For weld cleanup", IsConsumable = true },
            },

            // ========== SECTIONING ==========
            ["sectioning"] = new List<MaterialSuggestion>
            {
                new() { Name = "Spot Weld Cutter Bits", Category = Categories.Cutting, Description = "8mm spot weld drill bits", IsConsumable = true },
                new() { Name = "Cut-Off Wheels", Category = Categories.Cutting, Description = "3\" cut-off wheels", IsConsumable = true },
                new() { Name = "Reciprocating Saw Blades", Category = Categories.Cutting, Description = "Bi-metal 18 TPI blades", IsConsumable = true },
                new() { Name = "Plasma Cutter Tips", Category = Categories.Cutting, Description = "Consumable tips/electrodes", IsConsumable = true },
                new() { Name = "MIG Welding Wire", Category = Categories.Welding, Description = ".023\" ER70S-6", IsConsumable = true },
                new() { Name = "Welding Gas (C25)", Category = Categories.Welding, Description = "Shielding gas", IsConsumable = true },
                new() { Name = "Weld-Thru Primer", Category = Categories.Corrosion, Description = "For all weld flanges", IsConsumable = true },
                new() { Name = "Cavity Wax", Category = Categories.Corrosion, Description = "Internal protection", IsConsumable = true },
                new() { Name = "Seam Sealer", Category = Categories.Sealing, Description = "Match OEM appearance", IsConsumable = true },
                new() { Name = "36 Grit Discs", Category = Categories.Sanding, Description = "Heavy material removal", IsConsumable = true },
                new() { Name = "Flap Discs", Category = Categories.Sanding, Description = "Weld finishing", IsConsumable = true },
            },

            // ========== STRUCTURAL REPAIR ==========
            ["structural"] = new List<MaterialSuggestion>
            {
                new() { Name = "Measuring Targets", Category = Categories.Structural, Description = "For 3D measuring system", IsConsumable = true },
                new() { Name = "Pull Plates/Tabs", Category = Categories.Structural, Description = "Weld-on pull attachments", IsConsumable = true },
                new() { Name = "Chain Links", Category = Categories.Structural, Description = "Frame rack pulling chain", IsConsumable = false },
                new() { Name = "MIG Welding Wire", Category = Categories.Welding, Description = "For structural welds", IsConsumable = true },
                new() { Name = "Welding Gas", Category = Categories.Welding, Description = "75/25 or Tri-mix", IsConsumable = true },
                new() { Name = "Grinding Discs", Category = Categories.Sanding, Description = "Weld cleanup", IsConsumable = true },
            },

            // ========== BOLTED PANEL REPLACEMENT ==========
            ["bolted panel replace"] = new List<MaterialSuggestion>
            {
                new() { Name = "Replacement Hardware", Category = Categories.Hardware, Description = "OEM bolts, nuts, clips as needed", IsConsumable = true },
                new() { Name = "Thread Locker", Category = Categories.Hardware, Description = "Medium strength (blue)", IsConsumable = true },
                new() { Name = "Anti-Seize Compound", Category = Categories.Hardware, Description = "For dissimilar metals", IsConsumable = true },
                new() { Name = "Trim Clips", Category = Categories.Hardware, Description = "Body side molding clips", IsConsumable = true },
            },

            // ========== REFINISH / PAINT ==========
            ["refinish"] = new List<MaterialSuggestion>
            {
                new() { Name = "Sandpaper (180 Grit)", Category = Categories.Sanding, Description = "Featheredge sanding", IsConsumable = true },
                new() { Name = "Sandpaper (320 Grit)", Category = Categories.Sanding, Description = "Primer sanding", IsConsumable = true },
                new() { Name = "Sandpaper (400 Grit)", Category = Categories.Sanding, Description = "Final sanding before paint", IsConsumable = true },
                new() { Name = "Sandpaper (600 Grit)", Category = Categories.Sanding, Description = "Wet sanding clear", IsConsumable = true },
                new() { Name = "Sandpaper (1500 Grit)", Category = Categories.Sanding, Description = "Color sanding", IsConsumable = true },
                new() { Name = "Sandpaper (2000 Grit)", Category = Categories.Sanding, Description = "Final color sanding", IsConsumable = true },
                new() { Name = "Scuff Pads (Gray)", Category = Categories.Sanding, Description = "Panel prep scuffing", IsConsumable = true },
                new() { Name = "Tack Cloths", Category = Categories.Refinish, Description = "Dust removal before paint", IsConsumable = true },
                new() { Name = "Masking Paper", Category = Categories.Masking, Description = "12\" and 18\" rolls", IsConsumable = true },
                new() { Name = "Masking Tape", Category = Categories.Masking, Description = "3/4\" and 1.5\" automotive tape", IsConsumable = true },
                new() { Name = "Fine Line Tape", Category = Categories.Masking, Description = "For two-tone and detail work", IsConsumable = true },
                new() { Name = "Plastic Sheeting", Category = Categories.Masking, Description = "Vehicle cover/overspray protection", IsConsumable = true },
                new() { Name = "Primer/Sealer", Category = Categories.Refinish, Description = "2K urethane primer", IsConsumable = true },
                new() { Name = "Basecoat", Category = Categories.Refinish, Description = "Color matched paint", IsConsumable = true },
                new() { Name = "Clearcoat", Category = Categories.Refinish, Description = "2K urethane clear", IsConsumable = true },
                new() { Name = "Hardener/Activator", Category = Categories.Refinish, Description = "For 2K products", IsConsumable = true },
                new() { Name = "Reducer/Thinner", Category = Categories.Refinish, Description = "Temperature appropriate", IsConsumable = true },
                new() { Name = "Gun Cleaner", Category = Categories.Refinish, Description = "Spray gun cleaning solvent", IsConsumable = true },
                new() { Name = "Paint Strainers", Category = Categories.Refinish, Description = "190 micron cone strainers", IsConsumable = true },
                new() { Name = "Mixing Cups", Category = Categories.Refinish, Description = "Graduated mixing cups", IsConsumable = true },
                new() { Name = "Stir Sticks", Category = Categories.Refinish, Description = "Paint mixing sticks", IsConsumable = true },
            },

            ["paint"] = new List<MaterialSuggestion>
            {
                new() { Name = "Tack Cloths", Category = Categories.Refinish, Description = "Pre-paint dust removal", IsConsumable = true },
                new() { Name = "Masking Materials", Category = Categories.Masking, Description = "Paper and tape", IsConsumable = true },
                new() { Name = "Basecoat", Category = Categories.Refinish, Description = "Color matched", IsConsumable = true },
                new() { Name = "Clearcoat", Category = Categories.Refinish, Description = "2K urethane", IsConsumable = true },
                new() { Name = "Hardener", Category = Categories.Refinish, Description = "Activator for 2K", IsConsumable = true },
                new() { Name = "Paint Strainers", Category = Categories.Refinish, Description = "Cone strainers", IsConsumable = true },
            },

            ["blend"] = new List<MaterialSuggestion>
            {
                new() { Name = "Scuff Pads", Category = Categories.Sanding, Description = "Gray scuff pads", IsConsumable = true },
                new() { Name = "Tack Cloths", Category = Categories.Refinish, Description = "Dust removal", IsConsumable = true },
                new() { Name = "Masking Materials", Category = Categories.Masking, Description = "Paper and tape", IsConsumable = true },
                new() { Name = "Blender/Reducer", Category = Categories.Refinish, Description = "Edge blending solvent", IsConsumable = true },
                new() { Name = "Basecoat", Category = Categories.Refinish, Description = "Reduced for blending", IsConsumable = true },
                new() { Name = "Clearcoat", Category = Categories.Refinish, Description = "2K clear", IsConsumable = true },
            },

            // ========== BODY FILLER / REPAIR ==========
            ["body filler"] = new List<MaterialSuggestion>
            {
                new() { Name = "Body Filler", Category = Categories.Refinish, Description = "Lightweight or standard filler", IsConsumable = true },
                new() { Name = "Hardener (Red Cream)", Category = Categories.Refinish, Description = "Filler catalyst", IsConsumable = true },
                new() { Name = "Glazing Putty", Category = Categories.Refinish, Description = "For pinholes and fine imperfections", IsConsumable = true },
                new() { Name = "36 Grit Discs", Category = Categories.Sanding, Description = "Initial filler shaping", IsConsumable = true },
                new() { Name = "80 Grit Paper", Category = Categories.Sanding, Description = "Filler shaping", IsConsumable = true },
                new() { Name = "180 Grit Paper", Category = Categories.Sanding, Description = "Final filler sanding", IsConsumable = true },
                new() { Name = "Mixing Boards", Category = Categories.Refinish, Description = "Filler mixing surface", IsConsumable = true },
                new() { Name = "Spreaders", Category = Categories.Refinish, Description = "Plastic body filler spreaders", IsConsumable = true },
            },

            ["repair"] = new List<MaterialSuggestion>
            {
                new() { Name = "Body Filler", Category = Categories.Refinish, Description = "Lightweight filler", IsConsumable = true },
                new() { Name = "Hardener", Category = Categories.Refinish, Description = "Red cream hardener", IsConsumable = true },
                new() { Name = "Sanding Discs", Category = Categories.Sanding, Description = "36, 80, 180 grit", IsConsumable = true },
                new() { Name = "Glazing Putty", Category = Categories.Refinish, Description = "For pinholes", IsConsumable = true },
            },

            // ========== ALUMINUM REPAIR ==========
            ["aluminum"] = new List<MaterialSuggestion>
            {
                new() { Name = "Aluminum MIG Wire", Category = Categories.Welding, Description = "4043 or 5356 aluminum wire", IsConsumable = true },
                new() { Name = "Pure Argon Gas", Category = Categories.Welding, Description = "100% Argon shielding", IsConsumable = true },
                new() { Name = "Aluminum Contact Tips", Category = Categories.Welding, Description = "U-groove tips for aluminum", IsConsumable = true },
                new() { Name = "Aluminum Sanding Discs", Category = Categories.Sanding, Description = "Non-loading discs", IsConsumable = true },
                new() { Name = "Aluminum Body Filler", Category = Categories.Refinish, Description = "Aluminum-specific filler", IsConsumable = true },
                new() { Name = "Aluminum Cleaner", Category = Categories.Corrosion, Description = "Conversion coating/prep", IsConsumable = true },
                new() { Name = "Aluminum Primer", Category = Categories.Corrosion, Description = "Self-etch or epoxy", IsConsumable = true },
                new() { Name = "Dedicated Tools", Category = Categories.Hardware, Description = "Prevent steel contamination", IsConsumable = false, Notes = "Use separate tools for aluminum" },
            },

            // ========== PLASTIC REPAIR ==========
            ["plastic repair"] = new List<MaterialSuggestion>
            {
                new() { Name = "Plastic Welder Rod", Category = Categories.Welding, Description = "Match plastic type (PP, ABS, etc.)", IsConsumable = true },
                new() { Name = "Plastic Repair Adhesive", Category = Categories.Sealing, Description = "2-part plastic adhesive", IsConsumable = true },
                new() { Name = "Reinforcement Mesh", Category = Categories.Structural, Description = "Fiberglass mesh for backing", IsConsumable = true },
                new() { Name = "Plastic Filler", Category = Categories.Refinish, Description = "Flexible plastic filler", IsConsumable = true },
                new() { Name = "Adhesion Promoter", Category = Categories.Refinish, Description = "For paint adhesion to plastic", IsConsumable = true },
                new() { Name = "Plastic Prep Solvent", Category = Categories.Refinish, Description = "Removes mold release", IsConsumable = true },
                new() { Name = "180 Grit Paper", Category = Categories.Sanding, Description = "Plastic sanding", IsConsumable = true },
            },

            ["bumper"] = new List<MaterialSuggestion>
            {
                new() { Name = "Adhesion Promoter", Category = Categories.Refinish, Description = "Required for plastic bumpers", IsConsumable = true },
                new() { Name = "Flexible Primer", Category = Categories.Refinish, Description = "Bumper primer", IsConsumable = true },
                new() { Name = "Flex Additive", Category = Categories.Refinish, Description = "Add to clear for flexibility", IsConsumable = true },
                new() { Name = "Scuff Pads", Category = Categories.Sanding, Description = "Gray pads for prep", IsConsumable = true },
            },

            // ========== GLASS ==========
            ["glass"] = new List<MaterialSuggestion>
            {
                new() { Name = "Windshield Urethane", Category = Categories.Sealing, Description = "OEM-approved urethane adhesive", IsConsumable = true },
                new() { Name = "Glass Primer", Category = Categories.Sealing, Description = "Pinchweld and glass primer", IsConsumable = true },
                new() { Name = "Cut-Out Wire", Category = Categories.Glass, Description = "Piano wire for removal", IsConsumable = true },
                new() { Name = "Cut-Out Knife Blades", Category = Categories.Glass, Description = "Cold knife blades", IsConsumable = true },
                new() { Name = "Suction Cups", Category = Categories.Glass, Description = "Glass handling cups", IsConsumable = false },
                new() { Name = "Molding Clips", Category = Categories.Hardware, Description = "Windshield reveal molding clips", IsConsumable = true },
                new() { Name = "Dam Tape", Category = Categories.Masking, Description = "Water dam tape", IsConsumable = true },
            },

            ["windshield"] = new List<MaterialSuggestion>
            {
                new() { Name = "Windshield Urethane", Category = Categories.Sealing, Description = "1-hour or fast-cure adhesive", IsConsumable = true },
                new() { Name = "Glass Primer", Category = Categories.Sealing, Description = "Black primer for edges", IsConsumable = true },
                new() { Name = "Pinchweld Primer", Category = Categories.Sealing, Description = "For bare metal pinchweld", IsConsumable = true },
                new() { Name = "Cut-Out Wire/Blades", Category = Categories.Glass, Description = "Removal tools", IsConsumable = true },
                new() { Name = "Setting Blocks", Category = Categories.Glass, Description = "Positioning spacers", IsConsumable = true },
            },

            // ========== ELECTRICAL ==========
            ["electrical"] = new List<MaterialSuggestion>
            {
                new() { Name = "Butt Connectors", Category = Categories.Electrical, Description = "Heat shrink butt connectors", IsConsumable = true },
                new() { Name = "Wire (Various Gauge)", Category = Categories.Electrical, Description = "Automotive primary wire", IsConsumable = true },
                new() { Name = "Heat Shrink Tubing", Category = Categories.Electrical, Description = "Assorted sizes", IsConsumable = true },
                new() { Name = "Electrical Tape", Category = Categories.Electrical, Description = "Quality vinyl tape", IsConsumable = true },
                new() { Name = "Dielectric Grease", Category = Categories.Electrical, Description = "Connector protection", IsConsumable = true },
                new() { Name = "Wire Loom", Category = Categories.Electrical, Description = "Harness protection", IsConsumable = true },
                new() { Name = "Zip Ties", Category = Categories.Electrical, Description = "Harness securing", IsConsumable = true },
                new() { Name = "Fuses", Category = Categories.Electrical, Description = "Replacement fuses", IsConsumable = true },
            },

            // ========== MECHANICAL ==========
            ["suspension"] = new List<MaterialSuggestion>
            {
                new() { Name = "Thread Locker", Category = Categories.Hardware, Description = "Blue medium strength", IsConsumable = true },
                new() { Name = "Anti-Seize", Category = Categories.Hardware, Description = "For steel/aluminum contact", IsConsumable = true },
                new() { Name = "Cotter Pins", Category = Categories.Hardware, Description = "Castle nut pins", IsConsumable = true },
                new() { Name = "Alignment Specs", Category = Categories.Structural, Description = "Alignment verification", IsConsumable = false },
            },

            ["a/c"] = new List<MaterialSuggestion>
            {
                new() { Name = "Refrigerant (R-134a/R-1234yf)", Category = Categories.Hardware, Description = "System recharge", IsConsumable = true },
                new() { Name = "PAG Oil", Category = Categories.Hardware, Description = "Compressor oil", IsConsumable = true },
                new() { Name = "O-Rings", Category = Categories.Hardware, Description = "A/C fitting seals", IsConsumable = true },
                new() { Name = "UV Dye", Category = Categories.Hardware, Description = "Leak detection", IsConsumable = true },
            },

            ["cooling system"] = new List<MaterialSuggestion>
            {
                new() { Name = "Coolant", Category = Categories.Hardware, Description = "OEM-spec antifreeze", IsConsumable = true },
                new() { Name = "Hose Clamps", Category = Categories.Hardware, Description = "Worm gear or spring clamps", IsConsumable = true },
                new() { Name = "Thermostat Gasket", Category = Categories.Hardware, Description = "If T-stat disturbed", IsConsumable = true },
            },

            // ========== CORROSION PROTECTION ==========
            ["corrosion protection"] = new List<MaterialSuggestion>
            {
                new() { Name = "Weld-Thru Primer", Category = Categories.Corrosion, Description = "Zinc-rich for weld areas", IsConsumable = true },
                new() { Name = "Self-Etch Primer", Category = Categories.Corrosion, Description = "Bare metal primer", IsConsumable = true },
                new() { Name = "Epoxy Primer", Category = Categories.Corrosion, Description = "Maximum corrosion protection", IsConsumable = true },
                new() { Name = "Cavity Wax", Category = Categories.Corrosion, Description = "Internal panel protection", IsConsumable = true },
                new() { Name = "Undercoating", Category = Categories.Corrosion, Description = "Underbody protection", IsConsumable = true },
                new() { Name = "Rubberized Undercoating", Category = Categories.Corrosion, Description = "Sound deadening/protection", IsConsumable = true },
                new() { Name = "Seam Sealer", Category = Categories.Sealing, Description = "Match OEM appearance", IsConsumable = true },
            },

            // ========== TRIM/MOLDING ==========
            ["trim"] = new List<MaterialSuggestion>
            {
                new() { Name = "Trim Adhesive", Category = Categories.Sealing, Description = "3M or equivalent", IsConsumable = true },
                new() { Name = "Double-Sided Tape", Category = Categories.Sealing, Description = "Molding attachment tape", IsConsumable = true },
                new() { Name = "Trim Clips", Category = Categories.Hardware, Description = "Various push-in clips", IsConsumable = true },
                new() { Name = "Adhesive Remover", Category = Categories.Refinish, Description = "For old tape removal", IsConsumable = true },
            },

            ["molding"] = new List<MaterialSuggestion>
            {
                new() { Name = "Double-Sided Tape", Category = Categories.Sealing, Description = "OEM-style attachment", IsConsumable = true },
                new() { Name = "Molding Clips", Category = Categories.Hardware, Description = "Replacement clips", IsConsumable = true },
                new() { Name = "Trim Tool Set", Category = Categories.Hardware, Description = "Non-marring pry tools", IsConsumable = false },
            },
        };
    }

    /// <summary>
    /// Get material suggestions for a specific operation type
    /// </summary>
    public List<MaterialSuggestion> GetSuggestionsForOperation(string operation)
    {
        var suggestions = new List<MaterialSuggestion>();
        var opLower = operation.ToLower();

        // Direct match
        if (_operationMaterials.TryGetValue(opLower, out var directMatch))
        {
            suggestions.AddRange(directMatch);
        }

        // Partial matches - check if operation contains key terms
        foreach (var kvp in _operationMaterials)
        {
            if (opLower.Contains(kvp.Key) && kvp.Key != opLower)
            {
                foreach (var mat in kvp.Value)
                {
                    if (!suggestions.Any(s => s.Name == mat.Name))
                    {
                        suggestions.Add(mat);
                    }
                }
            }
        }

        return suggestions;
    }

    /// <summary>
    /// Get material suggestions based on multiple keywords
    /// </summary>
    public List<MaterialSuggestion> GetSuggestionsForKeywords(params string[] keywords)
    {
        var suggestions = new List<MaterialSuggestion>();

        foreach (var keyword in keywords)
        {
            var keywordSuggestions = GetSuggestionsForOperation(keyword);
            foreach (var suggestion in keywordSuggestions)
            {
                if (!suggestions.Any(s => s.Name == suggestion.Name))
                {
                    suggestions.Add(suggestion);
                }
            }
        }

        return suggestions;
    }

    /// <summary>
    /// Get suggestions grouped by category
    /// </summary>
    public Dictionary<string, List<MaterialSuggestion>> GetSuggestionsGroupedByCategory(string operation)
    {
        var suggestions = GetSuggestionsForOperation(operation);
        return suggestions
            .GroupBy(s => s.Category)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Get all available operation types
    /// </summary>
    public List<string> GetAvailableOperationTypes()
    {
        return _operationMaterials.Keys.OrderBy(k => k).ToList();
    }

    /// <summary>
    /// Search materials by name
    /// </summary>
    public List<MaterialSuggestion> SearchMaterials(string searchTerm)
    {
        var results = new List<MaterialSuggestion>();
        var searchLower = searchTerm.ToLower();

        foreach (var kvp in _operationMaterials)
        {
            foreach (var material in kvp.Value)
            {
                if (material.Name.ToLower().Contains(searchLower) ||
                    material.Description.ToLower().Contains(searchLower))
                {
                    if (!results.Any(r => r.Name == material.Name))
                    {
                        results.Add(material);
                    }
                }
            }
        }

        return results;
    }
}
