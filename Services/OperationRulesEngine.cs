#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Services
{
    public enum MaterialType
    {
        Plastic,
        BoltedMetal,
        WeldedMetal,
        Glass,
        CarbonFiber,
        Unknown
    }

    public class RulesSuggestedOperation
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal DefaultHours { get; set; }
        public string OperationType { get; set; } = "Body"; // Body, Paint, Mech, Sublet
        public string WhyNeeded { get; set; } = "";
        public string Source { get; set; } = "Rules Engine";
        public bool ReplaceOnly { get; set; }
    }

    public class EstimateContext
    {
        public bool HasPaintWork { get; set; }
        public bool HasWeldedPanels { get; set; }
        public bool HasPlasticParts { get; set; }
        public bool HasGlass { get; set; }
        public bool HasStructural { get; set; }
        public Dictionary<MaterialType, int> MaterialBreakdown { get; set; } = new();
    }

    public class OperationRulesEngine
    {
        private static OperationRulesEngine? _instance;
        public static OperationRulesEngine Instance => _instance ??= new OperationRulesEngine();

        private static readonly string[] PlasticKeywords =
            { "bumper", "fascia", "valance", "spoiler", "air dam", "deflector", "filler panel", "garnish" };

        private static readonly string[] BoltedMetalKeywords =
            { "fender", "hood", "door", "deck lid", "trunk lid", "tailgate", "liftgate" };

        private static readonly string[] WeldedMetalKeywords =
            { "quarter panel", "quarter pnl", "rocker", "pillar", "rail", "apron", "wheelhouse", "floor pan", "cowl" };

        private static readonly string[] GlassKeywords =
            { "windshield", "back glass", "rear glass", "door glass", "quarter glass", "vent glass" };

        private static readonly string[] StructuralKeywords =
            { "pillar", "rail", "rocker", "apron" };

        private static readonly string[] PaintOpTypes =
            { "replace", "repair", "blend", "refinish", "repl", "rpr", "blnd", "refn", "rfn" };

        private static readonly string[] NonPaintOpTypes =
            { "r&i", "ri", "r+i", "overhaul", "o/h", "remove", "sublet", "mech" };

        public MaterialType ClassifyMaterial(string partName)
        {
            var lower = partName.ToLowerInvariant();

            // Check carbon fiber first (keyword in description)
            if (lower.Contains("carbon fiber") || lower.Contains("cfrp"))
                return MaterialType.CarbonFiber;

            // Glass
            if (GlassKeywords.Any(kw => lower.Contains(kw)))
                return MaterialType.Glass;

            // Welded metal — check before bolted since "quarter" alone should match
            if (WeldedMetalKeywords.Any(kw => lower.Contains(kw)))
                return MaterialType.WeldedMetal;

            // Check for "quarter" alone (common shorthand for quarter panel)
            if (lower.Contains("quarter") && !lower.Contains("glass"))
                return MaterialType.WeldedMetal;

            // Check "roof" — welded if quarter-connected, otherwise bolted
            if (lower.Contains("roof"))
            {
                // If the description suggests welded (quarter-connected roof, roof panel)
                if (lower.Contains("roof panel") || lower.Contains("roof skin"))
                    return MaterialType.WeldedMetal;
                return MaterialType.BoltedMetal;
            }

            // Bolted metal
            if (BoltedMetalKeywords.Any(kw => lower.Contains(kw)))
                return MaterialType.BoltedMetal;

            // Plastic
            if (PlasticKeywords.Any(kw => lower.Contains(kw)))
                return MaterialType.Plastic;

            // "extension" near bumper context
            if (lower.Contains("extension") && lower.Contains("bumper"))
                return MaterialType.Plastic;

            return MaterialType.Unknown;
        }

        public bool InvolvesPaint(string operationType)
        {
            if (string.IsNullOrWhiteSpace(operationType)) return false;
            var lower = operationType.ToLowerInvariant().Trim();

            if (PaintOpTypes.Any(pt => lower == pt || lower.StartsWith(pt)))
                return true;

            // Explicitly non-paint
            if (NonPaintOpTypes.Any(npt => lower == npt || lower.StartsWith(npt)))
                return false;

            return false;
        }

        private static bool IsReplaceOp(string operationType)
        {
            var lower = operationType.ToLowerInvariant().Trim();
            return lower == "replace" || lower == "repl" || lower.StartsWith("replac");
        }

        public List<RulesSuggestedOperation> GetSuggestedOperations(string partName, string operationType)
        {
            var suggestions = new List<RulesSuggestedOperation>();
            var material = ClassifyMaterial(partName);
            var paintInvolved = InvolvesPaint(operationType);

            if (!paintInvolved)
                return suggestions; // No additional operations for R&I/Overhaul

            var isReplace = IsReplaceOp(operationType);

            switch (material)
            {
                case MaterialType.Plastic:
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "adhesion promoter",
                        Description = "Adhesion Promoter (Plastic Parts)",
                        Category = "Refinish Operations",
                        DefaultHours = 0.3m,
                        OperationType = "Paint",
                        WhyNeeded = "Required for paint adhesion on plastic substrates — NOT INCLUDED in refinish times",
                        Source = "Mitchell CEG Section 22 - plastic NOT INCLUDED"
                    });
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "flex additive",
                        Description = "Flex Additive (Plastic Parts)",
                        Category = "Refinish Operations",
                        DefaultHours = 0.2m,
                        OperationType = "Paint",
                        WhyNeeded = "Required for flexible paint on plastic parts — NOT INCLUDED in refinish times",
                        Source = "MET: Part Operations - flexible paint NOT INCLUDED"
                    });
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "cover car",
                        Description = "Cover Vehicle for Paint Overspray Protection",
                        Category = "Body Operations",
                        DefaultHours = 0.5m,
                        OperationType = "Body",
                        WhyNeeded = "Protect vehicle from paint overspray during refinish — NOT INCLUDED",
                        Source = "DEG Inquiry 13434 - NOT INCLUDED"
                    });
                    break;

                case MaterialType.BoltedMetal:
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "backtape jambs",
                        Description = "Backtape Jambs / Mask Openings",
                        Category = "Body Operations",
                        DefaultHours = 0.3m,
                        OperationType = "Body",
                        WhyNeeded = "Mask jambs and openings for refinish — NOT INCLUDED in refinish times",
                        Source = "DEG Inquiry - backtaping NOT INCLUDED"
                    });
                    if (isReplace)
                    {
                        suggestions.Add(new RulesSuggestedOperation
                        {
                            Name = "corrosion protection",
                            Description = "Corrosion Protection - Hem Flanges",
                            Category = "Body Operations",
                            DefaultHours = 0.2m,
                            OperationType = "Body",
                            WhyNeeded = "Protect hem flanges and bare metal from corrosion on replacement panels",
                            Source = "Mitchell CEG G31 - hem flange protection NOT INCLUDED",
                            ReplaceOnly = true
                        });
                    }
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "cover car",
                        Description = "Cover Vehicle for Paint Overspray Protection",
                        Category = "Body Operations",
                        DefaultHours = 0.5m,
                        OperationType = "Body",
                        WhyNeeded = "Protect vehicle from paint overspray during refinish — NOT INCLUDED",
                        Source = "DEG Inquiry 13434 - NOT INCLUDED"
                    });
                    break;

                case MaterialType.WeldedMetal:
                    if (isReplace)
                    {
                        suggestions.Add(new RulesSuggestedOperation
                        {
                            Name = "e-coat removal",
                            Description = "Remove Factory E-Coat at Weld Flanges",
                            Category = "Body Operations",
                            DefaultHours = 0.3m,
                            OperationType = "Body",
                            WhyNeeded = "Remove factory e-coat at weld flanges for proper weld penetration",
                            Source = "MET: Body Operations - DEG: protective coatings NOT INCLUDED",
                            ReplaceOnly = true
                        });
                        suggestions.Add(new RulesSuggestedOperation
                        {
                            Name = "weld-through primer",
                            Description = "Weld-Through Primer Application",
                            Category = "Body Operations",
                            DefaultHours = 0.2m,
                            OperationType = "Body",
                            WhyNeeded = "Apply weld-through primer to bare metal before welding for corrosion protection",
                            Source = "MET: Body Operations - Mitchell CEG: NOT INCLUDED",
                            ReplaceOnly = true
                        });
                        suggestions.Add(new RulesSuggestedOperation
                        {
                            Name = "seam sealer",
                            Description = "Seam Sealer Application (Beyond Attachment Points)",
                            Category = "Body Operations",
                            DefaultHours = 0.3m,
                            OperationType = "Body",
                            WhyNeeded = "Restore factory seam sealer after panel replacement — NOT INCLUDED",
                            Source = "DEG Inquiry 23010 - additional seam sealer NOT INCLUDED",
                            ReplaceOnly = true
                        });
                        suggestions.Add(new RulesSuggestedOperation
                        {
                            Name = "cavity wax",
                            Description = "Cavity Wax / Anti-Corrosion Treatment",
                            Category = "Body Operations",
                            DefaultHours = 0.5m,
                            OperationType = "Body",
                            WhyNeeded = "Restore factory anti-corrosion treatment in enclosed cavities",
                            Source = "Mitchell CEG G31 - corrosion protection NOT INCLUDED",
                            ReplaceOnly = true
                        });
                        suggestions.Add(new RulesSuggestedOperation
                        {
                            Name = "corrosion protection",
                            Description = "Corrosion Protection - Weld Joints",
                            Category = "Body Operations",
                            DefaultHours = 0.2m,
                            OperationType = "Body",
                            WhyNeeded = "Protect weld joints and bare metal from corrosion on welded replacement panels",
                            Source = "Mitchell CEG G31 - corrosion protection NOT INCLUDED",
                            ReplaceOnly = true
                        });
                    }
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "backtape jambs",
                        Description = "Backtape Jambs / Mask Openings",
                        Category = "Body Operations",
                        DefaultHours = 0.3m,
                        OperationType = "Body",
                        WhyNeeded = "Mask jambs and openings for refinish — NOT INCLUDED in refinish times",
                        Source = "DEG Inquiry - backtaping NOT INCLUDED"
                    });
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "cover car",
                        Description = "Cover Vehicle for Paint Overspray Protection",
                        Category = "Body Operations",
                        DefaultHours = 0.5m,
                        OperationType = "Body",
                        WhyNeeded = "Protect vehicle from paint overspray and weld sparks — NOT INCLUDED",
                        Source = "DEG Inquiry 13434 - NOT INCLUDED"
                    });
                    break;

                case MaterialType.Glass:
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "urethane kit",
                        Description = "Urethane Adhesive Kit for Glass Installation",
                        Category = "Glass Operations",
                        DefaultHours = 0m,
                        OperationType = "Sublet",
                        WhyNeeded = "Urethane adhesive kit required for glass installation — typically sublet",
                        Source = "Glass installation materials"
                    });
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "primer application",
                        Description = "Glass Primer Application",
                        Category = "Body Operations",
                        DefaultHours = 0.2m,
                        OperationType = "Body",
                        WhyNeeded = "Primer application for glass bonding surface preparation",
                        Source = "Glass installation procedures"
                    });
                    break;

                case MaterialType.CarbonFiber:
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "adhesion promoter",
                        Description = "Adhesion Promoter (Carbon Fiber)",
                        Category = "Refinish Operations",
                        DefaultHours = 0.3m,
                        OperationType = "Paint",
                        WhyNeeded = "Required for paint adhesion on carbon fiber substrates",
                        Source = "Carbon fiber refinish procedures"
                    });
                    suggestions.Add(new RulesSuggestedOperation
                    {
                        Name = "cover car",
                        Description = "Cover Vehicle for Paint Overspray Protection",
                        Category = "Body Operations",
                        DefaultHours = 0.5m,
                        OperationType = "Body",
                        WhyNeeded = "Protect vehicle from paint overspray during refinish — NOT INCLUDED",
                        Source = "DEG Inquiry 13434 - NOT INCLUDED"
                    });
                    break;
            }

            return suggestions;
        }

        /// <summary>
        /// Calculates Buff, DE-NIB, and Feather Edge hours using percentage formulas
        /// based on each panel's actual refinish/repair hours.
        /// </summary>
        public List<RulesSuggestedOperation> GetRefinishSuggestedOperations(
            string partName, string operationType, decimal refinishHours, decimal repairHours, bool isFirstPanel)
        {
            var suggestions = new List<RulesSuggestedOperation>();
            var material = ClassifyMaterial(partName);

            if (!InvolvesPaint(operationType))
                return suggestions;

            var isReplace = IsReplaceOp(operationType);
            var isRepair = !isReplace && operationType.ToLowerInvariant().Trim() is "repair" or "rpr";

            // Buff / Wet Dry Sand, Rub-Out & Buff — all materials when refinish > 0
            if (refinishHours > 0)
            {
                var buffHours = Math.Max(refinishHours * 0.3m, 0.1m);
                suggestions.Add(new RulesSuggestedOperation
                {
                    Name = "buff",
                    Description = "Wet Dry Sand, Rub-Out & Buff",
                    Category = "Refinish Operations",
                    DefaultHours = buffHours,
                    OperationType = "Paint",
                    WhyNeeded = "Required after refinish for final finish quality — NOT INCLUDED in refinish times",
                    Source = "Mitchell CEG Section 22 - buff NOT INCLUDED"
                });
            }

            // DE-NIB — varies by material and first/additional panel
            if (refinishHours > 0)
            {
                decimal denibHours;
                switch (material)
                {
                    case MaterialType.Plastic:
                        denibHours = isFirstPanel
                            ? Math.Max(refinishHours * 0.1m, 0.1m)
                            : Math.Max(refinishHours * 0.05m, 0.1m);
                        break;
                    case MaterialType.BoltedMetal:
                        if (isReplace)
                            denibHours = isFirstPanel ? 0.15m : 0.11m;
                        else
                            denibHours = Math.Max(refinishHours * 0.2m, 0.1m);
                        break;
                    case MaterialType.WeldedMetal:
                        if (isReplace)
                            denibHours = isFirstPanel ? 0.3m : 0.24m;
                        else
                            denibHours = Math.Max(refinishHours * 0.2m, 0.1m);
                        break;
                    default:
                        denibHours = isFirstPanel
                            ? Math.Max(refinishHours * 0.2m, 0.1m)
                            : Math.Max(refinishHours * 0.2m, 0.1m);
                        break;
                }

                suggestions.Add(new RulesSuggestedOperation
                {
                    Name = "denib",
                    Description = "DE-NIB & Polish",
                    Category = "Refinish Operations",
                    DefaultHours = denibHours,
                    OperationType = "Paint",
                    WhyNeeded = "Required to remove dust nibs after refinish — NOT INCLUDED in refinish times",
                    Source = "MET: Part Operations - denib NOT INCLUDED"
                });
            }

            // Feather Edge & Block Sand — repair operations only, metal parts
            if (repairHours > 0 && isRepair &&
                material is MaterialType.BoltedMetal or MaterialType.WeldedMetal or MaterialType.Unknown)
            {
                var featherHours = repairHours * 0.25m;
                suggestions.Add(new RulesSuggestedOperation
                {
                    Name = "feather prime block",
                    Description = "Feather, Prime & Block (Repair Panels)",
                    Category = "Refinish Operations",
                    DefaultHours = featherHours,
                    OperationType = "Paint",
                    WhyNeeded = "Feather edges and block sand repair area before refinish — NOT INCLUDED",
                    Source = "Mitchell CEG Section 28 - NOT INCLUDED"
                });
            }

            return suggestions;
        }

        public EstimateContext AnalyzeEstimateContext(List<(string partName, string opType)> operations)
        {
            var context = new EstimateContext();
            var breakdown = new Dictionary<MaterialType, int>();

            foreach (var (partName, opType) in operations)
            {
                var material = ClassifyMaterial(partName);
                breakdown[material] = breakdown.GetValueOrDefault(material) + 1;

                if (InvolvesPaint(opType))
                    context.HasPaintWork = true;

                if (material == MaterialType.WeldedMetal && IsReplaceOp(opType))
                    context.HasWeldedPanels = true;

                if (material == MaterialType.Plastic && InvolvesPaint(opType))
                    context.HasPlasticParts = true;

                if (material == MaterialType.Glass)
                    context.HasGlass = true;

                var lower = partName.ToLowerInvariant();
                if (StructuralKeywords.Any(kw => lower.Contains(kw)))
                    context.HasStructural = true;
            }

            context.MaterialBreakdown = breakdown;
            return context;
        }
    }
}
