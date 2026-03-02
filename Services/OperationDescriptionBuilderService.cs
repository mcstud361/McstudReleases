#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services;

/// <summary>
/// Service for building professional operation descriptions from natural language inputs.
/// "fix the door dent, it has a few dings and a crease" → "PDR repair, door panel, multiple dings with crease line"
/// "bumper repair tear" → "Plastic repair, bumper cover, tear, adhesive bond method"
/// </summary>
public class OperationDescriptionBuilderService
{
    private static OperationDescriptionBuilderService? _instance;
    public static OperationDescriptionBuilderService Instance => _instance ??= new OperationDescriptionBuilderService();

    #region Damage Type Definitions

    private readonly Dictionary<string, DamageTypeInfo> _damageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Dent-related
        ["dent"] = new DamageTypeInfo("dent", "PDR repair", "Body", new[] { "ding", "dented", "pushed in", "indentation" }),
        ["ding"] = new DamageTypeInfo("ding", "PDR repair", "Body", new[] { "door ding", "parking lot" }),
        ["crease"] = new DamageTypeInfo("crease", "crease line", "Body", new[] { "creased", "crease line", "sharp line" }),
        ["hail"] = new DamageTypeInfo("hail damage", "PDR repair", "Body", new[] { "hail", "storm damage", "dimples" }),

        // Surface damage
        ["scratch"] = new DamageTypeInfo("scratch", "Scratch repair", "Refinish", new[] { "scratched", "key scratch", "keyed", "scuff" }),
        ["scuff"] = new DamageTypeInfo("scuff", "Buff and polish", "Refinish", new[] { "scuffed", "rub mark" }),
        ["chip"] = new DamageTypeInfo("paint chip", "Touch-up repair", "Refinish", new[] { "chipped", "rock chip", "stone chip" }),
        ["fade"] = new DamageTypeInfo("paint fade", "Color sand and buff", "Refinish", new[] { "faded", "oxidized", "oxidation", "chalky" }),
        ["peel"] = new DamageTypeInfo("clear coat peel", "Refinish required", "Refinish", new[] { "peeling", "flaking", "delaminating" }),

        // Plastic/bumper damage
        ["tear"] = new DamageTypeInfo("tear", "Plastic repair", "Body", new[] { "torn", "ripped", "rip" }),
        ["crack"] = new DamageTypeInfo("crack", "Plastic repair", "Body", new[] { "cracked", "split", "broken" }),
        ["hole"] = new DamageTypeInfo("hole", "Plastic repair", "Body", new[] { "puncture", "punctured", "punched through" }),
        ["gouge"] = new DamageTypeInfo("gouge", "Fill and refinish", "Body", new[] { "gouged", "deep scratch", "deep gouge" }),
        ["tab"] = new DamageTypeInfo("broken tab", "Tab repair", "Body", new[] { "tab broken", "mounting tab", "clip broken" }),

        // Structural
        ["bent"] = new DamageTypeInfo("bent", "Straighten and align", "Structural", new[] { "tweaked", "pushed", "out of alignment" }),
        ["buckle"] = new DamageTypeInfo("buckle", "Metal work repair", "Structural", new[] { "buckled", "kinked", "wrinkled" }),
        ["crush"] = new DamageTypeInfo("crush damage", "Pull and repair", "Structural", new[] { "crushed", "collapsed", "smashed" }),

        // Corrosion
        ["rust"] = new DamageTypeInfo("rust", "Rust repair", "Body", new[] { "rusted", "corrosion", "corroded", "rusty" }),
        ["bubble"] = new DamageTypeInfo("rust bubble", "Cut out and patch", "Body", new[] { "bubbling", "blistering" })
    };

    private readonly Dictionary<string, string> _panelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Doors
        ["door"] = "door panel",
        ["driver door"] = "LF door panel",
        ["passenger door"] = "RF door panel",
        ["rear door"] = "rear door panel",
        ["lf door"] = "LF door panel",
        ["rf door"] = "RF door panel",
        ["lr door"] = "LR door panel",
        ["rr door"] = "RR door panel",

        // Fenders/Quarters
        ["fender"] = "fender",
        ["front fender"] = "front fender",
        ["quarter"] = "quarter panel",
        ["quarter panel"] = "quarter panel",
        ["rear quarter"] = "rear quarter panel",

        // Front end
        ["hood"] = "hood",
        ["bumper"] = "bumper cover",
        ["front bumper"] = "front bumper cover",
        ["rear bumper"] = "rear bumper cover",
        ["grille"] = "grille",
        ["header"] = "header panel",

        // Rear
        ["trunk"] = "trunk lid",
        ["decklid"] = "deck lid",
        ["tailgate"] = "tailgate",
        ["liftgate"] = "liftgate",

        // Top/sides
        ["roof"] = "roof panel",
        ["rocker"] = "rocker panel",
        ["pillar"] = "pillar",
        ["a-pillar"] = "A-pillar",
        ["b-pillar"] = "B-pillar",
        ["c-pillar"] = "C-pillar",

        // Truck
        ["bed"] = "bed side",
        ["bedside"] = "bed side",
        ["taillight"] = "taillight panel",

        // Misc
        ["mirror"] = "mirror cover",
        ["spoiler"] = "spoiler",
        ["valance"] = "valance",
        ["air dam"] = "air dam",
        ["splash shield"] = "splash shield",
        ["wheel opening"] = "wheel opening molding"
    };

    private readonly Dictionary<string, string> _quantityWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a few"] = "multiple",
        ["few"] = "multiple",
        ["several"] = "multiple",
        ["many"] = "multiple",
        ["multiple"] = "multiple",
        ["couple"] = "2",
        ["some"] = "multiple",
        ["one"] = "single",
        ["single"] = "single",
        ["two"] = "2",
        ["three"] = "3",
        ["four"] = "4",
        ["five"] = "5"
    };

    private readonly Dictionary<string, string> _sizeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["small"] = "small",
        ["tiny"] = "minor",
        ["little"] = "minor",
        ["minor"] = "minor",
        ["medium"] = "moderate",
        ["moderate"] = "moderate",
        ["large"] = "large",
        ["big"] = "large",
        ["major"] = "major",
        ["huge"] = "extensive",
        ["extensive"] = "extensive",
        ["deep"] = "deep",
        ["shallow"] = "surface",
        ["light"] = "light",
        ["heavy"] = "heavy"
    };

    private readonly Dictionary<string, string> _locationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = "LH",
        ["right"] = "RH",
        ["front"] = "front",
        ["rear"] = "rear",
        ["driver"] = "LH",
        ["passenger"] = "RH",
        ["upper"] = "upper",
        ["lower"] = "lower",
        ["center"] = "center",
        ["edge"] = "edge area",
        ["corner"] = "corner",
        ["top"] = "upper",
        ["bottom"] = "lower"
    };

    private readonly Dictionary<string, string> _repairMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        // PDR methods
        ["pdr"] = "PDR method",
        ["paintless"] = "paintless dent repair",
        ["glue pull"] = "glue pull method",
        ["push"] = "conventional push access",

        // Conventional body
        ["filler"] = "with body filler",
        ["bondo"] = "with body filler",
        ["weld"] = "weld repair",
        ["patch"] = "cut and patch",

        // Plastic
        ["adhesive"] = "adhesive bond method",
        ["glue"] = "adhesive bond method",
        ["hot air"] = "hot air welding",
        ["plastic weld"] = "plastic welding method",
        ["staple"] = "staple and fill method",

        // Refinish
        ["spot"] = "spot refinish",
        ["blend"] = "blend refinish",
        ["touch up"] = "touch-up",
        ["buff"] = "buff and polish",
        ["wet sand"] = "wet sand and buff"
    };

    #endregion

    /// <summary>
    /// Parse natural language and build professional description
    /// </summary>
    public DescriptionBuilderResult BuildDescriptions(string input)
    {
        var result = new DescriptionBuilderResult { OriginalInput = input };
        var inputLower = input.ToLowerInvariant();

        // Parse all components from input
        var parsed = ParseInput(inputLower);
        result.DetectedPanel = parsed.Panel;
        result.DetectedOperation = parsed.PrimaryDamage;
        result.DetectedSize = parsed.Size;
        result.ParsedDetails = parsed;

        // Generate the primary professional description
        var primaryDesc = BuildPrimaryDescription(parsed);

        // Generate variations
        var suggestions = new List<DescriptionSuggestion>();

        // Add primary description
        suggestions.Add(new DescriptionSuggestion
        {
            Description = primaryDesc,
            Category = parsed.RepairCategory,
            RelevanceScore = 100,
            IsGenerated = true
        });

        // Add method variations based on damage type
        var variations = GenerateVariations(parsed);
        suggestions.AddRange(variations);

        result.Suggestions = suggestions.Take(5).ToList();
        return result;
    }

    private ParsedDamageInfo ParseInput(string input)
    {
        var info = new ParsedDamageInfo();

        // Find panel
        foreach (var kvp in _panelNames.OrderByDescending(k => k.Key.Length))
        {
            if (input.Contains(kvp.Key))
            {
                info.Panel = kvp.Value;
                break;
            }
        }
        if (string.IsNullOrEmpty(info.Panel)) info.Panel = "panel";

        // Find all damage types mentioned
        foreach (var kvp in _damageTypes)
        {
            if (input.Contains(kvp.Key) || kvp.Value.Aliases.Any(a => input.Contains(a)))
            {
                info.DamageTypes.Add(kvp.Value);
            }
        }

        // Set primary damage
        if (info.DamageTypes.Any())
        {
            info.PrimaryDamage = info.DamageTypes[0].Name;
            info.RepairCategory = info.DamageTypes[0].Category;
            info.BaseRepairMethod = info.DamageTypes[0].DefaultRepair;
        }

        // Find quantity
        foreach (var kvp in _quantityWords.OrderByDescending(k => k.Key.Length))
        {
            if (input.Contains(kvp.Key))
            {
                info.Quantity = kvp.Value;
                break;
            }
        }

        // Find size
        foreach (var kvp in _sizeWords)
        {
            if (input.Contains(kvp.Key))
            {
                info.Size = kvp.Value;
                break;
            }
        }

        // Extract numeric size (e.g., "3 inch", "2\"", "4 in")
        var sizeMatch = Regex.Match(input, @"(\d+(?:\.\d+)?)\s*(?:inch|in|\""|inches|centimeter|cm|mm)?");
        if (sizeMatch.Success)
        {
            info.NumericSize = sizeMatch.Groups[1].Value + "\"";
        }

        // Find location
        foreach (var kvp in _locationWords)
        {
            if (Regex.IsMatch(input, $@"\b{kvp.Key}\b"))
            {
                info.Locations.Add(kvp.Value);
            }
        }

        // Find repair method hints
        foreach (var kvp in _repairMethods.OrderByDescending(k => k.Key.Length))
        {
            if (input.Contains(kvp.Key))
            {
                info.RepairMethodHint = kvp.Value;
                break;
            }
        }

        // Check for plastic/bumper specific context
        if (info.Panel.Contains("bumper") || input.Contains("plastic"))
        {
            info.IsPlasiticPanel = true;
        }

        return info;
    }

    private string BuildPrimaryDescription(ParsedDamageInfo parsed)
    {
        var parts = new List<string>();

        // Start with repair type
        if (!string.IsNullOrEmpty(parsed.BaseRepairMethod))
        {
            parts.Add(parsed.BaseRepairMethod);
        }

        // Add panel with location
        var panelDesc = parsed.Panel;
        if (parsed.Locations.Any())
        {
            panelDesc = $"{string.Join(" ", parsed.Locations)} {panelDesc}";
        }
        parts.Add(panelDesc);

        // Add damage details
        var damageDetails = new List<string>();

        // Quantity + size + primary damage
        var damageDesc = "";
        if (!string.IsNullOrEmpty(parsed.Quantity))
            damageDesc += parsed.Quantity + " ";
        if (!string.IsNullOrEmpty(parsed.NumericSize))
            damageDesc += parsed.NumericSize + " ";
        else if (!string.IsNullOrEmpty(parsed.Size))
            damageDesc += parsed.Size + " ";

        damageDesc += parsed.PrimaryDamage;
        damageDetails.Add(damageDesc.Trim());

        // Add secondary damage types
        foreach (var damage in parsed.DamageTypes.Skip(1).Take(2))
        {
            damageDetails.Add($"with {damage.Name}");
        }

        if (damageDetails.Any())
        {
            parts.Add(string.Join(" ", damageDetails));
        }

        // Add repair method if specified
        if (!string.IsNullOrEmpty(parsed.RepairMethodHint))
        {
            parts.Add(parsed.RepairMethodHint);
        }

        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private List<DescriptionSuggestion> GenerateVariations(ParsedDamageInfo parsed)
    {
        var variations = new List<DescriptionSuggestion>();
        var panel = parsed.Panel;

        // Based on damage type, generate appropriate variations
        if (parsed.DamageTypes.Any(d => d.Name == "dent" || d.Name == "ding" || d.Name == "crease"))
        {
            // PDR variations
            var sizeDesc = !string.IsNullOrEmpty(parsed.NumericSize) ? parsed.NumericSize :
                          !string.IsNullOrEmpty(parsed.Size) ? parsed.Size : "moderate";
            var qtyDesc = !string.IsNullOrEmpty(parsed.Quantity) ? parsed.Quantity + " " : "";

            variations.Add(new DescriptionSuggestion
            {
                Description = $"PDR repair, {panel}, {qtyDesc}{sizeDesc} dent(s), glue pull method",
                Category = "PDR",
                RelevanceScore = 90
            });

            variations.Add(new DescriptionSuggestion
            {
                Description = $"PDR repair, {panel}, {qtyDesc}{sizeDesc} dent(s), conventional push access",
                Category = "PDR",
                RelevanceScore = 85
            });

            // If has crease, add crease-specific
            if (parsed.DamageTypes.Any(d => d.Name == "crease"))
            {
                variations.Add(new DescriptionSuggestion
                {
                    Description = $"PDR repair, {panel}, crease line with {qtyDesc}dings, inner panel access",
                    Category = "PDR",
                    RelevanceScore = 88
                });
            }

            // Conventional option
            variations.Add(new DescriptionSuggestion
            {
                Description = $"Conventional dent repair, {panel}, with body filler, refinish required",
                Category = "Body",
                RelevanceScore = 70
            });
        }
        else if (parsed.IsPlasiticPanel || parsed.DamageTypes.Any(d => d.Name == "tear" || d.Name == "crack" || d.Name == "hole"))
        {
            // Plastic repair variations
            var damageType = parsed.DamageTypes.FirstOrDefault()?.Name ?? "damage";

            variations.Add(new DescriptionSuggestion
            {
                Description = $"Plastic repair, {panel}, {damageType}, adhesive bond method",
                Category = "Plastic",
                RelevanceScore = 90
            });

            variations.Add(new DescriptionSuggestion
            {
                Description = $"Plastic repair, {panel}, {damageType}, hot air welding method",
                Category = "Plastic",
                RelevanceScore = 85
            });

            variations.Add(new DescriptionSuggestion
            {
                Description = $"Plastic repair, {panel}, {damageType}, staple and fill method",
                Category = "Plastic",
                RelevanceScore = 80
            });

            // If severe, suggest replace
            if (parsed.Size == "large" || parsed.Size == "extensive" || parsed.Size == "major")
            {
                variations.Add(new DescriptionSuggestion
                {
                    Description = $"Replace {panel}, damage exceeds repair threshold",
                    Category = "Replace",
                    RelevanceScore = 75
                });
            }
        }
        else if (parsed.DamageTypes.Any(d => d.Name == "scratch" || d.Name == "scuff" || d.Name == "chip"))
        {
            // Surface/refinish variations
            var damageType = parsed.DamageTypes.FirstOrDefault()?.Name ?? "damage";
            var sizeDesc = !string.IsNullOrEmpty(parsed.Size) ? parsed.Size + " " : "";

            variations.Add(new DescriptionSuggestion
            {
                Description = $"Buff and polish, {panel}, {sizeDesc}{damageType}, clear coat only",
                Category = "Refinish",
                RelevanceScore = 90
            });

            variations.Add(new DescriptionSuggestion
            {
                Description = $"Wet sand and buff, {panel}, {sizeDesc}{damageType}",
                Category = "Refinish",
                RelevanceScore = 85
            });

            variations.Add(new DescriptionSuggestion
            {
                Description = $"Spot refinish, {panel}, {sizeDesc}{damageType}, blend edges",
                Category = "Refinish",
                RelevanceScore = 80
            });

            if (damageType == "chip")
            {
                variations.Add(new DescriptionSuggestion
                {
                    Description = $"Touch-up repair, {panel}, paint chip(s)",
                    Category = "Refinish",
                    RelevanceScore = 88
                });
            }
        }
        else if (parsed.DamageTypes.Any(d => d.Name == "rust" || d.Name == "rust bubble"))
        {
            // Rust repair variations
            variations.Add(new DescriptionSuggestion
            {
                Description = $"Rust repair, {panel}, grind, treat, and prime",
                Category = "Body",
                RelevanceScore = 90
            });

            variations.Add(new DescriptionSuggestion
            {
                Description = $"Rust repair, {panel}, cut out and patch weld",
                Category = "Body",
                RelevanceScore = 85
            });

            variations.Add(new DescriptionSuggestion
            {
                Description = $"Surface rust removal, {panel}, sand, treat, and refinish",
                Category = "Body",
                RelevanceScore = 80
            });
        }

        return variations;
    }

    /// <summary>
    /// Quick check if input looks like a description builder request
    /// </summary>
    public bool IsDescriptionRequest(string input)
    {
        var lower = input.ToLowerInvariant();

        // Explicit trigger phrases
        var triggers = new[]
        {
            "describe", "description", "write up", "writeup", "how to write",
            "word it", "phrase it", "say it", "put it", "professional",
            "clean up", "cleanup", "format"
        };

        if (triggers.Any(t => lower.Contains(t)))
            return true;

        // Check for damage description patterns (informal repair language)
        // "fix the door dent" / "door has a dent" / "dent on door" / "bumper tear"
        bool hasDamageWord = _damageTypes.Keys.Any(d => lower.Contains(d)) ||
                            _damageTypes.Values.Any(v => v.Aliases.Any(a => lower.Contains(a)));

        bool hasPanelWord = _panelNames.Keys.Any(p => lower.Contains(p));

        // If it mentions damage + panel, it's likely a description request
        if (hasDamageWord && hasPanelWord)
        {
            // But not if it's asking a question about the damage
            var questionWords = new[] { "what is", "why", "how much", "how long", "cost", "price", "?" };
            if (!questionWords.Any(q => lower.Contains(q)))
            {
                return true;
            }
        }

        // Short informal repair phrases
        var informalPatterns = new[]
        {
            @"^fix\s+(the\s+)?\w+",           // "fix the dent" / "fix door"
            @"\bhas\s+(a\s+)?(few\s+|some\s+)?\w+\s*(dent|ding|scratch|crack|tear)",  // "has a dent"
            @"\b(dent|ding|scratch|crack|tear|rust)\s+on\s+",  // "dent on door"
            @"\brepair\s+(the\s+)?(bumper|door|fender|hood)\s+(tear|crack|dent|scratch)", // "repair bumper tear"
        };

        return informalPatterns.Any(p => Regex.IsMatch(lower, p));
    }
}

#region Data Classes

public class DamageTypeInfo
{
    public string Name { get; }
    public string DefaultRepair { get; }
    public string Category { get; }
    public string[] Aliases { get; }

    public DamageTypeInfo(string name, string defaultRepair, string category, string[] aliases)
    {
        Name = name;
        DefaultRepair = defaultRepair;
        Category = category;
        Aliases = aliases;
    }
}

public class ParsedDamageInfo
{
    public string Panel { get; set; } = "";
    public string PrimaryDamage { get; set; } = "";
    public string RepairCategory { get; set; } = "";
    public string BaseRepairMethod { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Size { get; set; } = "";
    public string NumericSize { get; set; } = "";
    public string RepairMethodHint { get; set; } = "";
    public bool IsPlasiticPanel { get; set; }
    public List<string> Locations { get; set; } = new();
    public List<DamageTypeInfo> DamageTypes { get; set; } = new();
}

public class DescriptionBuilderResult
{
    public string OriginalInput { get; set; } = "";
    public string DetectedOperation { get; set; } = "";
    public string DetectedPanel { get; set; } = "";
    public string DetectedSize { get; set; } = "";
    public ParsedDamageInfo? ParsedDetails { get; set; }
    public List<DescriptionSuggestion> Suggestions { get; set; } = new();
}

public class DescriptionSuggestion
{
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public int RelevanceScore { get; set; }
    public bool IsGenerated { get; set; }
}

#endregion
