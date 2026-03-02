#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Estimating Helper Service - Quick calculation utilities
    ///
    /// Quick Wins:
    /// - Blend Calculator: Auto-calculate adjacent blend panels
    /// - 3-Stage Paint Multiplier: Add 50% to refinish for tri-coat
    /// - Pre/Post Scan Reminder: Trigger when estimate > threshold
    /// - Panel Count Calculator: Track refinish panel count for pricing
    /// - NOT INCLUDED Quick Reference
    /// </summary>
    public class EstimatingHelperService
    {
        private static EstimatingHelperService? _instance;
        public static EstimatingHelperService Instance => _instance ??= new EstimatingHelperService();

        // Adjacent panel definitions for blend calculations
        private readonly Dictionary<string, string[]> _adjacentPanels = new()
        {
            ["front bumper cover"] = new[] { "hood", "left fender", "right fender" },
            ["rear bumper cover"] = new[] { "trunk lid", "liftgate", "left quarter panel", "right quarter panel" },
            ["hood"] = new[] { "left fender", "right fender", "cowl" },
            ["left fender"] = new[] { "hood", "left front door", "left a-pillar" },
            ["right fender"] = new[] { "hood", "right front door", "right a-pillar" },
            ["left front door"] = new[] { "left fender", "left rear door", "left rocker" },
            ["right front door"] = new[] { "right fender", "right rear door", "right rocker" },
            ["left rear door"] = new[] { "left front door", "left quarter panel", "left rocker" },
            ["right rear door"] = new[] { "right front door", "right quarter panel", "right rocker" },
            ["left quarter panel"] = new[] { "left rear door", "trunk lid", "liftgate", "rear bumper cover", "roof" },
            ["right quarter panel"] = new[] { "right rear door", "trunk lid", "liftgate", "rear bumper cover", "roof" },
            ["trunk lid"] = new[] { "left quarter panel", "right quarter panel", "rear bumper cover" },
            ["liftgate"] = new[] { "left quarter panel", "right quarter panel", "rear bumper cover", "roof" },
            ["roof"] = new[] { "left quarter panel", "right quarter panel", "windshield header", "back glass" }
        };

        // Scan threshold
        public decimal ScanThreshold { get; set; } = 2500m;

        #region Blend Calculator

        /// <summary>
        /// Calculate which panels need blending based on refinished panels
        /// </summary>
        public BlendCalculationResult CalculateBlendPanels(List<string> refinishedPanels)
        {
            var result = new BlendCalculationResult();
            var blendCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var panel in refinishedPanels)
            {
                var panelLower = panel.ToLowerInvariant();

                // Find matching adjacent definition
                foreach (var kvp in _adjacentPanels)
                {
                    if (panelLower.Contains(kvp.Key) || kvp.Key.Contains(panelLower))
                    {
                        foreach (var adjacent in kvp.Value)
                        {
                            // Don't add if already being refinished
                            if (!refinishedPanels.Any(p =>
                                p.ToLowerInvariant().Contains(adjacent) ||
                                adjacent.Contains(p.ToLowerInvariant())))
                            {
                                blendCandidates.Add(adjacent);
                            }
                        }
                    }
                }
            }

            result.BlendPanels = blendCandidates.ToList();
            result.BlendCount = result.BlendPanels.Count;
            result.EstimatedBlendHours = result.BlendCount * 0.5m; // 0.5 hours per blend panel typical
            result.RefinishPanelCount = refinishedPanels.Count;

            return result;
        }

        /// <summary>
        /// Get adjacent panels for a specific panel
        /// </summary>
        public List<string> GetAdjacentPanels(string panelName)
        {
            var panelLower = panelName.ToLowerInvariant();

            foreach (var kvp in _adjacentPanels)
            {
                if (panelLower.Contains(kvp.Key) || kvp.Key.Contains(panelLower))
                {
                    return kvp.Value.ToList();
                }
            }

            return new List<string>();
        }

        #endregion

        #region 3-Stage Paint

        /// <summary>
        /// Calculate 3-stage paint additional time
        /// </summary>
        public ThreeStageResult Calculate3StagePaint(decimal baseRefinishHours, bool is3Stage)
        {
            var result = new ThreeStageResult
            {
                BaseHours = baseRefinishHours,
                Is3Stage = is3Stage
            };

            if (is3Stage)
            {
                result.AdditionalHours = baseRefinishHours * 0.5m; // 50% additional
                result.TotalHours = baseRefinishHours + result.AdditionalHours;
                result.Explanation = "3-stage/tri-coat paint requires additional mid-coat application (+50%)";
            }
            else
            {
                result.AdditionalHours = 0;
                result.TotalHours = baseRefinishHours;
                result.Explanation = "Standard 2-stage basecoat/clearcoat";
            }

            return result;
        }

        /// <summary>
        /// Detect if paint code indicates 3-stage
        /// Common 3-stage indicators: Pearl, Tri-coat, Mica, certain color names
        /// </summary>
        public bool Is3StagePaintCode(string paintCode, string? colorName = null)
        {
            var combined = $"{paintCode} {colorName ?? ""}".ToLowerInvariant();

            var threeStageIndicators = new[]
            {
                "pearl", "tri", "mica", "3-stage", "three stage", "3 stage",
                "crystal", "metallic pearl", "white pearl", "diamond"
            };

            return threeStageIndicators.Any(indicator => combined.Contains(indicator));
        }

        #endregion

        #region Scan Reminder

        /// <summary>
        /// Check if estimate total triggers scan reminder
        /// </summary>
        public ScanReminderResult CheckScanRequired(decimal estimateTotal, List<string>? existingOperations = null)
        {
            var result = new ScanReminderResult
            {
                EstimateTotal = estimateTotal,
                Threshold = ScanThreshold
            };

            result.PreScanRequired = estimateTotal >= ScanThreshold;
            result.PostScanRequired = estimateTotal >= ScanThreshold;

            // Check if scans already in estimate
            if (existingOperations != null)
            {
                var opsLower = existingOperations.Select(o => o.ToLowerInvariant()).ToList();
                result.PreScanIncluded = opsLower.Any(o => o.Contains("pre") && o.Contains("scan"));
                result.PostScanIncluded = opsLower.Any(o => o.Contains("post") && o.Contains("scan"));
            }

            if (result.PreScanRequired)
            {
                result.Reminders.Add(result.PreScanIncluded
                    ? "Pre-repair scan IS included"
                    : "ADD Pre-repair scan (estimate over threshold)");

                result.Reminders.Add(result.PostScanIncluded
                    ? "Post-repair scan IS included"
                    : "ADD Post-repair scan (estimate over threshold)");
            }

            return result;
        }

        #endregion

        #region Panel Count Calculator

        /// <summary>
        /// Calculate refinish panel count for pricing tiers
        /// Many shops price: 1st panel full rate, additional panels at reduced rate
        /// </summary>
        public PanelCountResult CalculatePanelCount(List<RefinishOperation> operations)
        {
            var result = new PanelCountResult();

            // Separate full refinish from blend
            var fullPanels = operations.Where(o =>
                o.OperationType.Equals("Refinish", StringComparison.OrdinalIgnoreCase) ||
                o.OperationType.Equals("Paint", StringComparison.OrdinalIgnoreCase)).ToList();

            var blendPanels = operations.Where(o =>
                o.OperationType.Equals("Blend", StringComparison.OrdinalIgnoreCase)).ToList();

            result.FullRefinishCount = fullPanels.Count;
            result.BlendCount = blendPanels.Count;
            result.TotalPanelCount = result.FullRefinishCount + result.BlendCount;

            // Calculate clear coat (typically ~0.4 per panel)
            result.ClearCoatHours = result.FullRefinishCount * 0.4m;

            // Sum hours
            result.TotalRefinishHours = fullPanels.Sum(p => p.Hours);
            result.TotalBlendHours = blendPanels.Sum(p => p.Hours);

            // Panel pricing breakdown
            if (result.FullRefinishCount > 0)
            {
                result.FirstPanelHours = fullPanels.First().Hours;
                result.AdditionalPanelHours = fullPanels.Skip(1).Sum(p => p.Hours);
            }

            return result;
        }

        #endregion

        #region Quick NOT INCLUDED Reference

        /// <summary>
        /// Get quick NOT INCLUDED items for an operation
        /// </summary>
        public List<string> GetQuickNotIncluded(string operationType, string partName)
        {
            var items = new List<string>();
            var opLower = operationType.ToLowerInvariant();
            var partLower = partName.ToLowerInvariant();

            // Refinish operations
            if (opLower.Contains("refinish") || opLower.Contains("paint"))
            {
                items.Add("Color tinting/matching");
                items.Add("Denib/final sand");
                items.Add("Color sand and buff");

                if (partLower.Contains("bumper") || partLower.Contains("plastic"))
                {
                    items.Add("Flex additive");
                    items.Add("Adhesion promoter");
                }
            }

            // Structural replace
            if ((opLower.Contains("replace") || opLower.Contains("section")) &&
                IsStructuralPart(partLower))
            {
                items.Add("Weld-thru primer");
                items.Add("Corrosion protection (cavities)");
                items.Add("Seam sealer (beyond attachment points)");
                items.Add("Sound deadener");
                items.Add("Pre/post measurements");
            }

            // Windshield
            if (partLower.Contains("windshield") || partLower.Contains("glass"))
            {
                items.Add("ADAS camera calibration");
                items.Add("Urethane/adhesive materials");
                items.Add("Molding kit");
            }

            // All repairs over threshold
            items.Add("Pre-repair diagnostic scan");
            items.Add("Post-repair diagnostic scan");

            return items;
        }

        private bool IsStructuralPart(string partName)
        {
            var structural = new[] { "quarter", "roof", "rocker", "pillar", "rail", "floor", "apron", "strut tower" };
            return structural.Any(s => partName.Contains(s));
        }

        #endregion

        #region Printable Checklist Generator

        /// <summary>
        /// Generate a printable checklist for technicians
        /// </summary>
        public string GenerateTechnicianChecklist(List<string> operations, string? roNumber = null)
        {
            var checklist = new System.Text.StringBuilder();

            checklist.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            checklist.AppendLine("║           TECHNICIAN OPERATIONS CHECKLIST                   ║");
            if (!string.IsNullOrEmpty(roNumber))
            {
                checklist.AppendLine($"║  RO#: {roNumber,-52}  ║");
            }
            checklist.AppendLine($"║  Date: {DateTime.Now:MM/dd/yyyy,-50}  ║");
            checklist.AppendLine("╠══════════════════════════════════════════════════════════════╣");

            // Pre-repair
            checklist.AppendLine("║ PRE-REPAIR                                                   ║");
            checklist.AppendLine("║ [ ] Pre-repair diagnostic scan completed                     ║");
            checklist.AppendLine("║ [ ] Pre-repair photos taken (damage, VIN, odometer)         ║");
            checklist.AppendLine("║ [ ] Parts verified and ready                                 ║");
            checklist.AppendLine("╠══════════════════════════════════════════════════════════════╣");

            // Operations
            checklist.AppendLine("║ OPERATIONS                                                   ║");
            foreach (var op in operations)
            {
                var opTruncated = op.Length > 54 ? op.Substring(0, 54) + "..." : op;
                checklist.AppendLine($"║ [ ] {opTruncated,-56} ║");
            }
            checklist.AppendLine("╠══════════════════════════════════════════════════════════════╣");

            // Post-repair
            checklist.AppendLine("║ POST-REPAIR                                                  ║");
            checklist.AppendLine("║ [ ] Post-repair diagnostic scan completed                   ║");
            checklist.AppendLine("║ [ ] All codes cleared                                        ║");
            checklist.AppendLine("║ [ ] ADAS calibrations completed (if applicable)             ║");
            checklist.AppendLine("║ [ ] Test drive completed                                     ║");
            checklist.AppendLine("║ [ ] Final QC inspection                                      ║");
            checklist.AppendLine("║ [ ] Vehicle cleaned                                          ║");
            checklist.AppendLine("╠══════════════════════════════════════════════════════════════╣");

            checklist.AppendLine("║ TECH SIGNATURE: _____________________  DATE: ___________   ║");
            checklist.AppendLine("╚══════════════════════════════════════════════════════════════╝");

            return checklist.ToString();
        }

        #endregion
    }

    #region Result Models

    public class BlendCalculationResult
    {
        public List<string> BlendPanels { get; set; } = new();
        public int BlendCount { get; set; }
        public int RefinishPanelCount { get; set; }
        public decimal EstimatedBlendHours { get; set; }
    }

    public class ThreeStageResult
    {
        public decimal BaseHours { get; set; }
        public decimal AdditionalHours { get; set; }
        public decimal TotalHours { get; set; }
        public bool Is3Stage { get; set; }
        public string Explanation { get; set; } = "";
    }

    public class ScanReminderResult
    {
        public decimal EstimateTotal { get; set; }
        public decimal Threshold { get; set; }
        public bool PreScanRequired { get; set; }
        public bool PostScanRequired { get; set; }
        public bool PreScanIncluded { get; set; }
        public bool PostScanIncluded { get; set; }
        public List<string> Reminders { get; set; } = new();
    }

    public class PanelCountResult
    {
        public int FullRefinishCount { get; set; }
        public int BlendCount { get; set; }
        public int TotalPanelCount { get; set; }
        public decimal ClearCoatHours { get; set; }
        public decimal TotalRefinishHours { get; set; }
        public decimal TotalBlendHours { get; set; }
        public decimal FirstPanelHours { get; set; }
        public decimal AdditionalPanelHours { get; set; }
    }

    public class RefinishOperation
    {
        public string OperationType { get; set; } = "";
        public string PartName { get; set; } = "";
        public decimal Hours { get; set; }
    }

    #endregion
}
