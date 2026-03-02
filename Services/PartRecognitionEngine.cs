#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// PART RECOGNITION ENGINE
    ///
    /// Normalizes part names from various formats:
    /// - "LT FRT BMPR CVR" → "front_bumper_cover" (side: left)
    /// - "Left Front Bumper Cover" → "front_bumper_cover" (side: left)
    /// - "L/F Fascia" → "front_bumper_cover" (side: left)
    /// - "FENDER ASSY LH" → "fender" (side: left)
    ///
    /// LEARNS from every estimate - builds alias dictionary automatically.
    /// The more estimates you feed it, the smarter it gets.
    /// </summary>
    public class PartRecognitionEngine
    {
        #region Singleton

        private static PartRecognitionEngine? _instance;
        public static PartRecognitionEngine Instance => _instance ??= new PartRecognitionEngine();

        #endregion

        private readonly LearnedKnowledgeBase _knowledge;

        // Common abbreviation mappings (bootstrap knowledge)
        private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            // Position
            ["lt"] = "left", ["lh"] = "left", ["l/h"] = "left", ["lf"] = "left front", ["l/f"] = "left front",
            ["rt"] = "right", ["rh"] = "right", ["r/h"] = "right", ["rf"] = "right front", ["r/f"] = "right front",
            ["lr"] = "left rear", ["l/r"] = "left rear", ["rr"] = "right rear", ["r/r"] = "right rear",
            ["frt"] = "front", ["fr"] = "front", ["rr"] = "rear", ["ctr"] = "center",

            // Parts
            ["bmpr"] = "bumper", ["bpr"] = "bumper", ["bmp"] = "bumper",
            ["cvr"] = "cover", ["cov"] = "cover",
            ["fndr"] = "fender", ["fdr"] = "fender",
            ["dr"] = "door", ["dor"] = "door",
            ["qtr"] = "quarter", ["qp"] = "quarter panel", ["q/p"] = "quarter panel",
            ["hd"] = "hood", ["hod"] = "hood",
            ["trnk"] = "trunk", ["trk"] = "trunk", ["dklid"] = "decklid",
            ["rckr"] = "rocker", ["rkr"] = "rocker",
            ["hse"] = "housing", ["hsng"] = "housing",
            ["mtg"] = "mounting", ["mntg"] = "mounting", ["brkt"] = "bracket",
            ["reinf"] = "reinforcement", ["rebar"] = "reinforcement", ["bar"] = "reinforcement",
            ["assy"] = "assembly", ["assm"] = "assembly", ["asm"] = "assembly",
            ["hlamp"] = "headlamp", ["hdlt"] = "headlight", ["hdlp"] = "headlamp",
            ["tllmp"] = "taillamp", ["tllt"] = "taillight", ["tllp"] = "taillamp",
            ["wndshld"] = "windshield", ["w/s"] = "windshield", ["ws"] = "windshield",
            ["mirr"] = "mirror", ["mir"] = "mirror",
            ["mldg"] = "molding", ["mld"] = "molding",
            ["grll"] = "grille", ["grl"] = "grille",
            ["cond"] = "condenser", ["cndsr"] = "condenser",
            ["rad"] = "radiator", ["radtr"] = "radiator",
            ["abs"] = "absorber", ["absr"] = "absorber",
            ["supp"] = "support", ["spt"] = "support",
            ["pnl"] = "panel", ["pan"] = "panel",
            ["inr"] = "inner", ["otr"] = "outer",
            ["upr"] = "upper", ["lwr"] = "lower",
            ["ext"] = "extension", ["extn"] = "extension",

            // Operations (sometimes in part names)
            ["repl"] = "replace", ["rpl"] = "replace",
            ["rpr"] = "repair", ["rep"] = "repair",
            ["rfn"] = "refinish", ["ref"] = "refinish",
            ["r&i"] = "r&i", ["ri"] = "r&i", ["r+i"] = "r&i",
            ["o/h"] = "overhaul", ["oh"] = "overhaul",
        };

        // Side detection patterns
        private static readonly Regex SidePattern = new(
            @"\b(left|right|lt|rt|lh|rh|l/h|r/h|lf|rf|l/f|r/f|lr|rr|l/r|r/r|driver|passenger)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Position detection patterns
        private static readonly Regex PositionPattern = new(
            @"\b(front|rear|frt|rr|fr|center|ctr|upper|lower|upr|lwr|inner|outer|inr|otr)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Known canonical part names (bootstrap, grows from learning)
        private static readonly HashSet<string> CanonicalParts = new(StringComparer.OrdinalIgnoreCase)
        {
            // Bumper area
            "front_bumper_cover", "rear_bumper_cover", "front_bumper_reinforcement", "rear_bumper_reinforcement",
            "front_bumper_absorber", "rear_bumper_absorber", "bumper_bracket", "license_plate_bracket",

            // Fenders & related
            "fender", "fender_liner", "fender_support", "fender_extension", "inner_fender",

            // Hood & front
            "hood", "hood_hinge", "hood_latch", "hood_insulator", "grille", "grille_surround",
            "radiator_support", "radiator", "condenser", "intercooler",

            // Doors
            "front_door", "rear_door", "front_door_shell", "rear_door_shell",
            "door_trim_panel", "door_glass", "door_regulator", "door_handle", "door_hinge",

            // Quarter & rear
            "quarter_panel", "rocker_panel", "rear_body_panel", "trunk_lid", "decklid", "trunk_floor",
            "liftgate", "tailgate",

            // Roof & pillars
            "roof", "roof_panel", "sunroof", "a_pillar", "b_pillar", "c_pillar", "d_pillar",

            // Lights
            "headlight", "headlamp", "fog_light", "fog_lamp", "turn_signal",
            "tail_light", "taillamp", "backup_light", "third_brake_light",

            // Glass
            "windshield", "back_glass", "rear_glass", "door_glass", "quarter_glass", "vent_glass",

            // Mirrors
            "mirror", "side_mirror", "door_mirror",

            // Trim & moldings
            "body_side_molding", "wheel_opening_molding", "door_molding", "rocker_molding",
            "window_molding", "belt_molding",

            // Structural
            "frame", "subframe", "apron", "rail", "crossmember", "floor_pan",
            "wheelhouse", "shock_tower",

            // Mechanical
            "strut", "shock", "control_arm", "tie_rod", "ball_joint", "spindle",
            "brake_caliper", "brake_rotor", "wheel", "tire",

            // Electrical
            "battery", "alternator", "starter", "wiring_harness", "sensor",

            // HVAC
            "compressor", "evaporator", "blower_motor", "heater_core",

            // Safety
            "airbag", "seatbelt", "pretensioner", "airbag_module"
        };

        public PartRecognitionEngine()
        {
            _knowledge = LearnedKnowledgeBase.Instance;
        }

        /// <summary>
        /// Recognize and normalize a part name from an estimate line
        /// </summary>
        public PartRecognitionResult Recognize(string rawPartName)
        {
            if (string.IsNullOrWhiteSpace(rawPartName))
                return new PartRecognitionResult { Success = false };

            var result = new PartRecognitionResult
            {
                OriginalText = rawPartName,
                Success = true
            };

            // Step 1: Extract side information
            result.Side = ExtractSide(rawPartName);

            // Step 2: Extract position (front/rear)
            result.Position = ExtractPosition(rawPartName);

            // Step 3: Normalize the text
            var normalized = NormalizeText(rawPartName);

            // Step 4: Try to resolve from learned aliases first
            var resolvedCanonical = _knowledge.ResolveAlias(normalized);
            if (resolvedCanonical != null)
            {
                result.CanonicalName = resolvedCanonical;
                result.Confidence = 0.95;
                result.Source = "learned_alias";
                return result;
            }

            // Step 5: Try to match to known canonical parts
            var matchedCanonical = MatchToCanonical(normalized, result.Position);
            if (matchedCanonical != null)
            {
                result.CanonicalName = matchedCanonical;
                result.Confidence = 0.85;
                result.Source = "pattern_match";

                // Learn this alias for future
                _knowledge.RecordPartAlias(matchedCanonical, normalized);
                return result;
            }

            // Step 6: Generate a canonical name from the normalized text
            result.CanonicalName = GenerateCanonicalName(normalized, result.Position);
            result.Confidence = 0.60;
            result.Source = "generated";

            // Learn this new part
            _knowledge.RecordPartAlias(result.CanonicalName, normalized);

            return result;
        }

        /// <summary>
        /// Extract side (left/right) from part description
        /// </summary>
        private string ExtractSide(string text)
        {
            var match = SidePattern.Match(text);
            if (!match.Success) return "";

            var side = match.Value.ToLowerInvariant();

            // Normalize to left/right
            if (side.Contains("left") || side.StartsWith("l") || side == "driver")
                return "left";
            if (side.Contains("right") || side.StartsWith("r") || side == "passenger")
                return "right";

            return "";
        }

        /// <summary>
        /// Extract position (front/rear/etc) from part description
        /// </summary>
        private string ExtractPosition(string text)
        {
            var match = PositionPattern.Match(text);
            if (!match.Success) return "";

            var pos = match.Value.ToLowerInvariant();

            // Normalize
            if (pos == "frt" || pos == "fr") return "front";
            if (pos == "rr") return "rear";
            if (pos == "ctr") return "center";
            if (pos == "upr") return "upper";
            if (pos == "lwr") return "lower";
            if (pos == "inr") return "inner";
            if (pos == "otr") return "outer";

            return pos;
        }

        /// <summary>
        /// Normalize text by expanding abbreviations
        /// </summary>
        private string NormalizeText(string text)
        {
            // Lowercase
            var normalized = text.ToLowerInvariant();

            // Remove common suffixes that don't add meaning
            normalized = Regex.Replace(normalized, @"\s*(assy|assembly|asm|assm)\s*", " ");

            // Split into tokens
            var tokens = Regex.Split(normalized, @"[\s\-_/\\]+")
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            // Expand abbreviations
            for (int i = 0; i < tokens.Count; i++)
            {
                if (Abbreviations.TryGetValue(tokens[i], out var expanded))
                {
                    tokens[i] = expanded;
                }
            }

            // Remove side/position words (we extract these separately)
            var sideWords = new[] { "left", "right", "lh", "rh", "lt", "rt", "driver", "passenger" };
            var posWords = new[] { "front", "rear", "center", "upper", "lower", "inner", "outer" };
            tokens = tokens.Where(t => !sideWords.Contains(t) && !posWords.Contains(t)).ToList();

            // Remove empty/short tokens
            tokens = tokens.Where(t => t.Length > 1).ToList();

            return string.Join(" ", tokens).Trim();
        }

        /// <summary>
        /// Try to match normalized text to a known canonical part
        /// </summary>
        private string? MatchToCanonical(string normalizedText, string position)
        {
            // Direct match check
            var directKey = normalizedText.Replace(" ", "_");
            if (CanonicalParts.Contains(directKey))
                return directKey;

            // With position prefix
            if (!string.IsNullOrEmpty(position))
            {
                var withPosition = $"{position}_{directKey}";
                if (CanonicalParts.Contains(withPosition))
                    return withPosition;
            }

            // Fuzzy matching - find best match
            var tokens = normalizedText.Split(' ').ToHashSet();
            string? bestMatch = null;
            int bestScore = 0;

            foreach (var canonical in CanonicalParts)
            {
                var canonicalTokens = canonical.Split('_').ToHashSet();
                var intersection = tokens.Intersect(canonicalTokens).Count();

                if (intersection > bestScore && intersection >= Math.Min(tokens.Count, canonicalTokens.Count) * 0.6)
                {
                    bestScore = intersection;
                    bestMatch = canonical;
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Generate a canonical name from normalized text
        /// </summary>
        private string GenerateCanonicalName(string normalizedText, string position)
        {
            var tokens = normalizedText.Split(' ')
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            // Add position if relevant
            if (!string.IsNullOrEmpty(position) && !tokens.Contains(position))
            {
                tokens.Insert(0, position);
            }

            // Join with underscores
            var canonical = string.Join("_", tokens);

            // Clean up
            canonical = Regex.Replace(canonical, @"_+", "_").Trim('_');

            return canonical;
        }

        /// <summary>
        /// Batch process multiple part names
        /// </summary>
        public List<PartRecognitionResult> RecognizeMany(IEnumerable<string> rawPartNames)
        {
            return rawPartNames.Select(Recognize).ToList();
        }

        /// <summary>
        /// Learn from a confirmed part name mapping
        /// </summary>
        public void LearnMapping(string rawPartName, string canonicalName, string side = "")
        {
            var normalized = NormalizeText(rawPartName);
            _knowledge.RecordPartAlias(canonicalName, normalized);
            _knowledge.RecordPartAlias(canonicalName, rawPartName.ToLowerInvariant());

            System.Diagnostics.Debug.WriteLine($"[PartRecog] Learned: '{rawPartName}' → '{canonicalName}'");
        }

        /// <summary>
        /// Get suggestions for a partial part name
        /// </summary>
        public List<string> GetSuggestions(string partialName, int maxResults = 10)
        {
            var normalized = partialName.ToLowerInvariant();
            var results = new List<string>();

            // Check canonical parts
            results.AddRange(CanonicalParts
                .Where(p => p.Replace("_", " ").Contains(normalized) || normalized.Contains(p.Replace("_", " ")))
                .Take(maxResults));

            // Check learned parts
            var learnedParts = _knowledge.GetAllParts();
            results.AddRange(learnedParts.Keys
                .Where(p => p.Contains(normalized) || normalized.Contains(p))
                .Take(maxResults - results.Count));

            return results.Distinct().Take(maxResults).ToList();
        }

        /// <summary>
        /// Get all known aliases for a canonical part
        /// </summary>
        public List<string> GetAliases(string canonicalName)
        {
            var parts = _knowledge.GetAllParts();
            if (parts.TryGetValue(canonicalName.ToLowerInvariant(), out var part))
            {
                return part.KnownAliases;
            }
            return new List<string>();
        }

        /// <summary>
        /// Statistics about recognition
        /// </summary>
        public PartRecognitionStats GetStats()
        {
            var parts = _knowledge.GetAllParts();
            return new PartRecognitionStats
            {
                TotalCanonicalParts = parts.Count,
                TotalAliasesLearned = parts.Values.Sum(p => p.KnownAliases.Count),
                BootstrapPartsCount = CanonicalParts.Count,
                AbbreviationsCount = Abbreviations.Count
            };
        }
    }

    #region Result Models

    public class PartRecognitionResult
    {
        public bool Success { get; set; }
        public string OriginalText { get; set; } = "";
        public string CanonicalName { get; set; } = "";
        public string Side { get; set; } = "";
        public string Position { get; set; } = "";
        public double Confidence { get; set; }
        public string Source { get; set; } = "";

        /// <summary>
        /// Full description with side/position
        /// </summary>
        public string FullDescription
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(Side)) parts.Add(Side);
                if (!string.IsNullOrEmpty(Position) && !CanonicalName.StartsWith(Position))
                    parts.Add(Position);
                parts.Add(CanonicalName.Replace("_", " "));
                return string.Join(" ", parts);
            }
        }
    }

    public class PartRecognitionStats
    {
        public int TotalCanonicalParts { get; set; }
        public int TotalAliasesLearned { get; set; }
        public int BootstrapPartsCount { get; set; }
        public int AbbreviationsCount { get; set; }
    }

    #endregion
}
