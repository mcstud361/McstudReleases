#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Comprehensive Estimate Scoring Service
    /// Analyzes estimates for completeness and identifies missing operations.
    ///
    /// Scoring Categories:
    /// - Operation Chains: Missing related operations (e.g., bumper replace missing flex additive)
    /// - Blend Operations: Missing blend on adjacent panels
    /// - Materials: Missing corrosion protection, seam sealer, etc.
    /// - R&I Operations: Missing R&I for access
    /// - Diagnostics: Missing pre/post scans for ADAS vehicles
    /// - Calibrations: Missing ADAS calibrations after glass/sensor work
    /// </summary>
    public class EstimateScoringService
    {
        private static EstimateScoringService? _instance;
        public static EstimateScoringService Instance => _instance ??= new EstimateScoringService();

        private readonly CommonlyMissedData? _missedItemsData;
        private readonly Dictionary<string, List<BlendRule>> _blendRules;
        private readonly Dictionary<string, List<string>> _operationChains;

        public EstimateScoringService()
        {
            _missedItemsData = LoadCommonlyMissedItems();
            _blendRules = InitializeBlendRules();
            _operationChains = InitializeOperationChains();
        }

        #region Main Scoring Method

        /// <summary>
        /// Score an estimate for completeness.
        /// Returns a detailed scoring result with suggestions.
        /// </summary>
        public EstimateScoringResult ScoreEstimate(List<ParsedEstimateLine> lines, string? vehicleInfo = null)
        {
            var result = new EstimateScoringResult
            {
                VehicleInfo = vehicleInfo,
                TotalLineItems = lines.Count,
                AssessedAt = DateTime.Now
            };

            if (lines.Count == 0)
            {
                result.OverallScore = 0;
                result.Grade = "N/A";
                result.Summary = "No operations to score";
                return result;
            }

            // Calculate estimate total
            result.EstimateTotal = lines.Sum(l => l.Price);

            // Run all scoring checks
            CheckOperationChains(lines, result);
            CheckBlendOperations(lines, result);
            CheckMaterialOperations(lines, result);
            CheckRIOperations(lines, result);
            CheckDiagnosticScans(lines, result);
            CheckADASCalibrations(lines, result);
            CheckGlobalRules(lines, result);

            // Calculate final score
            CalculateFinalScore(result);

            return result;
        }

        #endregion

        #region Operation Chain Checks

        private void CheckOperationChains(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            if (_missedItemsData?.OperationChecks == null) return;

            foreach (var line in lines.Where(l => !l.IsManualLine && !string.IsNullOrEmpty(l.PartName)))
            {
                var partLower = line.PartName.ToLowerInvariant();
                var opLower = line.OperationType?.ToLowerInvariant() ?? "";

                foreach (var check in _missedItemsData.OperationChecks)
                {
                    var checkData = check.Value;

                    // Check if this line triggers this check
                    bool matchesPart = checkData.TriggerKeywords?.Any(k =>
                        partLower.Contains(k.ToLowerInvariant())) == true;

                    bool matchesOp = checkData.TriggerOperations == null ||
                                     checkData.TriggerOperations.Count == 0 ||
                                     checkData.TriggerOperations.Any(o =>
                                         opLower.Contains(o.ToLowerInvariant()));

                    if (matchesPart && matchesOp && checkData.MissedItems != null)
                    {
                        foreach (var missedItem in checkData.MissedItems)
                        {
                            // Check if this item is already in the estimate
                            bool isPresent = lines.Any(l =>
                                l.PartName?.ToLowerInvariant().Contains(missedItem.Item.ToLowerInvariant()) == true ||
                                l.Description?.ToLowerInvariant().Contains(missedItem.Item.ToLowerInvariant()) == true);

                            if (!isPresent)
                            {
                                var issue = new ScoringIssue
                                {
                                    Category = GetIssueCategory(missedItem.Category ?? "Other"),
                                    Severity = GetSeverity(missedItem.Priority ?? "medium"),
                                    Title = missedItem.Item,
                                    Description = missedItem.Description ?? "",
                                    WhyNeeded = missedItem.WhyNeeded ?? "",
                                    TriggeredBy = line.PartName,
                                    SuggestedFix = new SuggestedFix
                                    {
                                        OperationType = GetOperationType(missedItem.Category ?? "Labor"),
                                        Description = missedItem.Item,
                                        LaborHours = missedItem.LaborHours,
                                        EstimatedCost = missedItem.TypicalCost,
                                        DegReference = missedItem.DegReference
                                    },
                                    PointDeduction = GetPointDeduction(missedItem.Priority ?? "medium")
                                };

                                // Avoid duplicates
                                if (!result.Issues.Any(i => i.Title == issue.Title && i.TriggeredBy == issue.TriggeredBy))
                                {
                                    result.Issues.Add(issue);
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Blend Operation Checks

        private void CheckBlendOperations(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            var refinishPanels = lines.Where(l =>
                !l.IsManualLine &&
                (l.OperationType?.ToLowerInvariant().Contains("repl") == true ||
                 l.OperationType?.ToLowerInvariant().Contains("rfn") == true ||
                 l.OperationType?.ToLowerInvariant().Contains("refinish") == true ||
                 l.RefinishHours > 0)).ToList();

            foreach (var panel in refinishPanels)
            {
                var partLower = panel.PartName?.ToLowerInvariant() ?? "";

                foreach (var rule in _blendRules)
                {
                    if (partLower.Contains(rule.Key))
                    {
                        foreach (var blendRule in rule.Value)
                        {
                            // Check if blend panel is already in estimate
                            bool hasBlend = lines.Any(l =>
                                l.PartName?.ToLowerInvariant().Contains(blendRule.AdjacentPanel.ToLowerInvariant()) == true &&
                                (l.OperationType?.ToLowerInvariant().Contains("blend") == true ||
                                 l.Description?.ToLowerInvariant().Contains("blend") == true));

                            if (!hasBlend)
                            {
                                var issue = new ScoringIssue
                                {
                                    Category = IssueCategoryType.Blend,
                                    Severity = blendRule.IsRequired ? IssueSeverity.High : IssueSeverity.Medium,
                                    Title = $"Blend {blendRule.AdjacentPanel}",
                                    Description = $"Adjacent panel blend for color match",
                                    WhyNeeded = $"New paint on {panel.PartName} won't match aged paint on {blendRule.AdjacentPanel}",
                                    TriggeredBy = panel.PartName ?? "",
                                    SuggestedFix = new SuggestedFix
                                    {
                                        OperationType = "Blend",
                                        Description = $"Blend {blendRule.AdjacentPanel}",
                                        LaborHours = blendRule.TypicalHours
                                    },
                                    PointDeduction = blendRule.IsRequired ? 5 : 3
                                };

                                if (!result.Issues.Any(i => i.Title == issue.Title))
                                {
                                    result.Issues.Add(issue);
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Material Checks

        private void CheckMaterialOperations(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            // Check for structural panels missing corrosion protection
            var structuralPanels = lines.Where(l =>
                !l.IsManualLine &&
                (l.PartName?.ToLowerInvariant().Contains("quarter") == true ||
                 l.PartName?.ToLowerInvariant().Contains("rocker") == true ||
                 l.PartName?.ToLowerInvariant().Contains("rail") == true ||
                 l.PartName?.ToLowerInvariant().Contains("pillar") == true ||
                 l.PartName?.ToLowerInvariant().Contains("roof") == true ||
                 l.PartName?.ToLowerInvariant().Contains("floor") == true) &&
                (l.OperationType?.ToLowerInvariant().Contains("repl") == true ||
                 l.OperationType?.ToLowerInvariant().Contains("section") == true)).ToList();

            foreach (var panel in structuralPanels)
            {
                // Check for corrosion protection
                bool hasCorrosion = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("corrosion") == true ||
                    l.PartName?.ToLowerInvariant().Contains("corrosion") == true ||
                    l.Description?.ToLowerInvariant().Contains("cavity wax") == true);

                if (!hasCorrosion)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Materials,
                        Severity = IssueSeverity.Critical,
                        Title = "Corrosion Protection",
                        Description = "Cavity wax and corrosion preventive compounds",
                        WhyNeeded = "OEM requires corrosion protection on structural panel replacement",
                        TriggeredBy = panel.PartName ?? "",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Corrosion Protection - Cavities",
                            LaborHours = 0.5m,
                            EstimatedCost = 45
                        },
                        PointDeduction = 8
                    });
                }

                // Check for weld-thru primer
                bool hasWeldThru = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("weld") == true &&
                    l.Description?.ToLowerInvariant().Contains("primer") == true);

                if (!hasWeldThru)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Materials,
                        Severity = IssueSeverity.High,
                        Title = "Weld-Thru Primer",
                        Description = "Zinc-rich primer for weld joints",
                        WhyNeeded = "Prevents corrosion at weld areas - OEM requirement",
                        TriggeredBy = panel.PartName ?? "",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Weld-Thru Primer Application",
                            LaborHours = 0.3m,
                            EstimatedCost = 18
                        },
                        PointDeduction = 5
                    });
                }

                // Check for seam sealer
                bool hasSeamSealer = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("seam seal") == true ||
                    l.PartName?.ToLowerInvariant().Contains("seam seal") == true);

                if (!hasSeamSealer)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Materials,
                        Severity = IssueSeverity.High,
                        Title = "Seam Sealer",
                        Description = "OEM-style seam sealer application",
                        WhyNeeded = "Prevents water intrusion at panel joints",
                        TriggeredBy = panel.PartName ?? "",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Seam Sealer Application",
                            LaborHours = 0.8m,
                            EstimatedCost = 35
                        },
                        PointDeduction = 5
                    });
                }
            }

            // Check plastic panels for flex additive
            var plasticPanels = lines.Where(l =>
                !l.IsManualLine &&
                (l.PartName?.ToLowerInvariant().Contains("bumper") == true ||
                 l.PartName?.ToLowerInvariant().Contains("fascia") == true ||
                 l.PartName?.ToLowerInvariant().Contains("cover") == true) &&
                l.RefinishHours > 0).ToList();

            if (plasticPanels.Count > 0)
            {
                bool hasFlexAdditive = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("flex") == true ||
                    l.PartName?.ToLowerInvariant().Contains("flex") == true);

                if (!hasFlexAdditive)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Materials,
                        Severity = IssueSeverity.High,
                        Title = "Flex Additive",
                        Description = "Flexible paint additive for plastic panels",
                        WhyNeeded = "Paint will crack on plastic without flex additive",
                        TriggeredBy = plasticPanels.First().PartName ?? "Bumper",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Flex Additive",
                            EstimatedCost = 15
                        },
                        PointDeduction = 4
                    });
                }

                bool hasAdhesionPromoter = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("adhesion") == true ||
                    l.PartName?.ToLowerInvariant().Contains("adhesion") == true);

                if (!hasAdhesionPromoter)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Materials,
                        Severity = IssueSeverity.High,
                        Title = "Adhesion Promoter",
                        Description = "Promotes paint adhesion to plastic",
                        WhyNeeded = "Paint won't properly adhere to bare plastic",
                        TriggeredBy = plasticPanels.First().PartName ?? "Bumper",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Adhesion Promoter",
                            LaborHours = 0.2m,
                            EstimatedCost = 12
                        },
                        PointDeduction = 4
                    });
                }
            }
        }

        #endregion

        #region R&I Checks

        private void CheckRIOperations(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            // Check bumper work for R&I items
            var bumperWork = lines.Where(l =>
                !l.IsManualLine &&
                (l.PartName?.ToLowerInvariant().Contains("bumper") == true ||
                 l.PartName?.ToLowerInvariant().Contains("fascia") == true)).ToList();

            if (bumperWork.Count > 0)
            {
                CheckForMissingRI(lines, result, bumperWork.First().PartName ?? "Bumper",
                    new[] { "fog", "sensor", "camera", "grille" },
                    new[] { ("R&I Fog Lamps", 0.3m), ("R&I Parking Sensors", 0.2m), ("R&I Camera", 0.3m) });
            }

            // Check door work for R&I items
            var doorWork = lines.Where(l =>
                !l.IsManualLine &&
                l.PartName?.ToLowerInvariant().Contains("door") == true &&
                !l.PartName.ToLowerInvariant().Contains("fuel")).ToList();

            if (doorWork.Count > 0)
            {
                CheckForMissingRI(lines, result, doorWork.First().PartName ?? "Door",
                    new[] { "mirror", "handle", "molding", "trim" },
                    new[] { ("R&I Mirror", 0.3m), ("R&I Door Handle", 0.3m), ("R&I Door Trim Panel", 0.3m) });
            }

            // Check quarter panel for R&I items
            var quarterWork = lines.Where(l =>
                !l.IsManualLine &&
                (l.PartName?.ToLowerInvariant().Contains("quarter") == true ||
                 l.PartName?.ToLowerInvariant().Contains("qtr") == true)).ToList();

            if (quarterWork.Count > 0)
            {
                CheckForMissingRI(lines, result, quarterWork.First().PartName ?? "Quarter Panel",
                    new[] { "tail", "fuel door", "molding" },
                    new[] { ("R&I Tail Light", 0.3m), ("R&I Fuel Door", 0.2m), ("R&I Quarter Moldings", 0.2m) });
            }

            // Check hood for R&I items
            var hoodWork = lines.Where(l =>
                !l.IsManualLine &&
                l.PartName?.ToLowerInvariant().Contains("hood") == true).ToList();

            if (hoodWork.Count > 0)
            {
                CheckForMissingRI(lines, result, "Hood",
                    new[] { "insulator", "insulation", "strut" },
                    new[] { ("R&I Hood Insulator", 0.3m), ("R&I Hood Struts", 0.2m) });
            }

            // Check fender for R&I items
            var fenderWork = lines.Where(l =>
                !l.IsManualLine &&
                l.PartName?.ToLowerInvariant().Contains("fender") == true).ToList();

            if (fenderWork.Count > 0)
            {
                CheckForMissingRI(lines, result, fenderWork.First().PartName ?? "Fender",
                    new[] { "liner", "splash", "wheel", "tire" },
                    new[] { ("R&I Fender Liner", 0.3m), ("R&I Tire/Wheel", 0.2m) });
            }
        }

        private void CheckForMissingRI(List<ParsedEstimateLine> lines, EstimateScoringResult result,
            string triggeredBy, string[] checkKeywords, (string Name, decimal Hours)[] riItems)
        {
            foreach (var item in riItems)
            {
                // Check if any keyword for this item exists in estimate
                bool hasItem = lines.Any(l =>
                    checkKeywords.Any(k =>
                        l.Description?.ToLowerInvariant().Contains(k) == true ||
                        l.PartName?.ToLowerInvariant().Contains(k) == true));

                // Also check if the R&I itself is already there
                bool hasRI = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains(item.Name.ToLowerInvariant()) == true ||
                    l.PartName?.ToLowerInvariant().Contains(item.Name.ToLowerInvariant()) == true);

                // If the component might exist but R&I isn't listed
                if (!hasRI)
                {
                    var existing = result.Issues.FirstOrDefault(i => i.Title == item.Name);
                    if (existing == null)
                    {
                        result.Issues.Add(new ScoringIssue
                        {
                            Category = IssueCategoryType.RandI,
                            Severity = IssueSeverity.Medium,
                            Title = item.Name,
                            Description = $"Remove and install {item.Name.Replace("R&I ", "")}",
                            WhyNeeded = $"Required for access to {triggeredBy}",
                            TriggeredBy = triggeredBy,
                            SuggestedFix = new SuggestedFix
                            {
                                OperationType = "R&I",
                                Description = item.Name,
                                LaborHours = item.Hours
                            },
                            PointDeduction = 2
                        });
                    }
                }
            }
        }

        #endregion

        #region Diagnostic Scan Checks

        private void CheckDiagnosticScans(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            // Any body panel work should have pre/post scans per OEM position statements
            bool needsScans = result.EstimateTotal > 2500 ||
                lines.Any(l =>
                    l.PartName?.ToLowerInvariant().Contains("bumper") == true ||
                    l.PartName?.ToLowerInvariant().Contains("fender") == true ||
                    l.PartName?.ToLowerInvariant().Contains("hood") == true ||
                    l.PartName?.ToLowerInvariant().Contains("door") == true ||
                    l.PartName?.ToLowerInvariant().Contains("quarter") == true ||
                    l.PartName?.ToLowerInvariant().Contains("roof") == true ||
                    l.PartName?.ToLowerInvariant().Contains("windshield") == true ||
                    l.PartName?.ToLowerInvariant().Contains("frame") == true ||
                    l.PartName?.ToLowerInvariant().Contains("rail") == true ||
                    l.PartName?.ToLowerInvariant().Contains("strut tower") == true ||
                    l.PartName?.ToLowerInvariant().Contains("structural") == true ||
                    l.PartName?.ToLowerInvariant().Contains("airbag") == true ||
                    l.PartName?.ToLowerInvariant().Contains("srs") == true);

            if (needsScans)
            {
                // Check both Description AND PartName — CCC estimates often put scan text in PartName
                bool hasPreScan = lines.Any(l =>
                {
                    var desc = l.Description?.ToLowerInvariant() ?? "";
                    var part = l.PartName?.ToLowerInvariant() ?? "";
                    var combined = $"{desc} {part}";
                    return (combined.Contains("pre") || combined.Contains("before")) &&
                           combined.Contains("scan");
                });

                bool hasPostScan = lines.Any(l =>
                {
                    var desc = l.Description?.ToLowerInvariant() ?? "";
                    var part = l.PartName?.ToLowerInvariant() ?? "";
                    var combined = $"{desc} {part}";
                    return (combined.Contains("post") || combined.Contains("after")) &&
                           combined.Contains("scan");
                });

                // Also check for generic scan operations
                bool hasAnyScan = lines.Any(l =>
                {
                    var desc = l.Description?.ToLowerInvariant() ?? "";
                    var part = l.PartName?.ToLowerInvariant() ?? "";
                    var combined = $"{desc} {part}";
                    return combined.Contains("diagnostic") || combined.Contains("scan");
                });

                if (!hasPreScan && !hasAnyScan)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Diagnostic,
                        Severity = IssueSeverity.Critical,
                        Title = "Pre-Repair Scan",
                        Description = "Diagnostic scan before any work",
                        WhyNeeded = "Document existing codes and system status - OEM requirement",
                        TriggeredBy = result.EstimateTotal > 2500 ? $"Estimate over $2,500" : "Structural work",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Pre-Repair Diagnostic Scan",
                            LaborHours = 0.5m
                        },
                        PointDeduction = 8
                    });
                }

                if (!hasPostScan && !hasAnyScan)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Diagnostic,
                        Severity = IssueSeverity.Critical,
                        Title = "Post-Repair Scan",
                        Description = "Diagnostic scan after all work complete",
                        WhyNeeded = "Verify no new codes, clear repair-related DTCs",
                        TriggeredBy = result.EstimateTotal > 2500 ? $"Estimate over $2,500" : "Structural work",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Post-Repair Diagnostic Scan",
                            LaborHours = 0.5m
                        },
                        PointDeduction = 8
                    });
                }
            }
        }

        #endregion

        #region ADAS Calibration Checks

        private void CheckADASCalibrations(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            // Check windshield work
            bool hasWindshield = lines.Any(l =>
                l.PartName?.ToLowerInvariant().Contains("windshield") == true ||
                l.PartName?.ToLowerInvariant().Contains("w/s") == true);

            if (hasWindshield)
            {
                bool hasCalibration = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("calibrat") == true ||
                    l.PartName?.ToLowerInvariant().Contains("calibrat") == true ||
                    l.Description?.ToLowerInvariant().Contains("adas") == true);

                if (!hasCalibration)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Calibration,
                        Severity = IssueSeverity.Critical,
                        Title = "ADAS Camera Calibration",
                        Description = "Recalibrate forward-facing camera",
                        WhyNeeded = "Camera mounted to windshield - OEM requires calibration after R&R",
                        TriggeredBy = "Windshield",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Sublet",
                            Description = "ADAS Camera Calibration",
                            EstimatedCost = 350
                        },
                        PointDeduction = 10
                    });
                }
            }

            // Check for alignment with ADAS implications
            bool hasAlignment = lines.Any(l =>
                l.Description?.ToLowerInvariant().Contains("align") == true);

            bool hasSuspensionWork = lines.Any(l =>
                l.PartName?.ToLowerInvariant().Contains("strut") == true ||
                l.PartName?.ToLowerInvariant().Contains("control arm") == true ||
                l.PartName?.ToLowerInvariant().Contains("tie rod") == true ||
                l.PartName?.ToLowerInvariant().Contains("suspension") == true);

            if (hasSuspensionWork && !hasAlignment)
            {
                result.Issues.Add(new ScoringIssue
                {
                    Category = IssueCategoryType.Calibration,
                    Severity = IssueSeverity.High,
                    Title = "4-Wheel Alignment",
                    Description = "Check/adjust wheel alignment",
                    WhyNeeded = "Suspension work affects alignment settings",
                    TriggeredBy = "Suspension components",
                    SuggestedFix = new SuggestedFix
                    {
                        OperationType = "Add",
                        Description = "4-Wheel Alignment",
                        LaborHours = 1.0m
                    },
                    PointDeduction = 5
                });
            }

            // Check radar/sensor work
            bool hasRadarSensor = lines.Any(l =>
                l.PartName?.ToLowerInvariant().Contains("radar") == true ||
                l.PartName?.ToLowerInvariant().Contains("sensor") == true ||
                l.PartName?.ToLowerInvariant().Contains("camera") == true);

            if (hasRadarSensor)
            {
                bool hasSensorCalibration = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("calibrat") == true);

                if (!hasSensorCalibration)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Calibration,
                        Severity = IssueSeverity.High,
                        Title = "Sensor Calibration",
                        Description = "Calibrate radar/camera sensor after R&R",
                        WhyNeeded = "ADAS sensors require calibration after removal",
                        TriggeredBy = "Radar/Camera Sensor",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Sublet",
                            Description = "ADAS Sensor Calibration",
                            EstimatedCost = 250
                        },
                        PointDeduction = 6
                    });
                }
            }
        }

        #endregion

        #region Global Rules

        private void CheckGlobalRules(List<ParsedEstimateLine> lines, EstimateScoringResult result)
        {
            // Check for clear coat on refinish operations
            bool hasRefinish = lines.Any(l => l.RefinishHours > 0);
            if (hasRefinish)
            {
                bool hasClearCoat = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("clear") == true ||
                    l.PartName?.ToLowerInvariant().Contains("clear") == true);

                // Clear coat is usually included, but check for 2-stage/3-stage
                bool hasTriCoat = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("tri-coat") == true ||
                    l.Description?.ToLowerInvariant().Contains("3-stage") == true ||
                    l.Description?.ToLowerInvariant().Contains("three stage") == true ||
                    l.Description?.ToLowerInvariant().Contains("pearl") == true);

                if (hasTriCoat)
                {
                    bool hasTriCoatTime = lines.Any(l =>
                        l.Description?.ToLowerInvariant().Contains("tri-coat") == true ||
                        l.Description?.ToLowerInvariant().Contains("additional") == true);

                    if (!hasTriCoatTime)
                    {
                        result.Issues.Add(new ScoringIssue
                        {
                            Category = IssueCategoryType.Refinish,
                            Severity = IssueSeverity.High,
                            Title = "3-Stage/Tri-Coat Time",
                            Description = "Additional refinish time for 3-stage paint",
                            WhyNeeded = "3-stage paint requires extra color application steps",
                            TriggeredBy = "Tri-coat/Pearl paint",
                            SuggestedFix = new SuggestedFix
                            {
                                OperationType = "Add",
                                Description = "3-Stage Paint Additional Time",
                                LaborHours = 0.5m
                            },
                            PointDeduction = 4
                        });
                    }
                }
            }

            // Check for battery disconnect on structural work
            bool hasStructuralWelding = lines.Any(l =>
                (l.PartName?.ToLowerInvariant().Contains("quarter") == true ||
                 l.PartName?.ToLowerInvariant().Contains("rocker") == true ||
                 l.PartName?.ToLowerInvariant().Contains("rail") == true ||
                 l.PartName?.ToLowerInvariant().Contains("pillar") == true ||
                 l.PartName?.ToLowerInvariant().Contains("roof") == true) &&
                l.OperationType?.ToLowerInvariant().Contains("repl") == true);

            if (hasStructuralWelding)
            {
                bool hasBatteryDisconnect = lines.Any(l =>
                    l.Description?.ToLowerInvariant().Contains("battery") == true &&
                    l.Description?.ToLowerInvariant().Contains("disconnect") == true);

                if (!hasBatteryDisconnect)
                {
                    result.Issues.Add(new ScoringIssue
                    {
                        Category = IssueCategoryType.Electrical,
                        Severity = IssueSeverity.High,
                        Title = "Battery Disconnect/Reconnect",
                        Description = "Disconnect battery before welding, reconnect and initialize after",
                        WhyNeeded = "Protect vehicle electronics from welding current",
                        TriggeredBy = "Structural panel replacement",
                        SuggestedFix = new SuggestedFix
                        {
                            OperationType = "Add",
                            Description = "Battery Disconnect/Reconnect & Initialize",
                            LaborHours = 0.6m
                        },
                        PointDeduction = 4
                    });
                }
            }
        }

        #endregion

        #region Score Calculation

        private void CalculateFinalScore(EstimateScoringResult result)
        {
            // Start at 100
            int baseScore = 100;

            // Calculate total deductions (cap at 100)
            int totalDeductions = Math.Min(100, result.Issues.Sum(i => i.PointDeduction));

            result.OverallScore = Math.Max(0, baseScore - totalDeductions);

            // Calculate category breakdown
            result.CategoryScores = new Dictionary<string, int>
            {
                ["Materials"] = CalculateCategoryScore(result.Issues, IssueCategoryType.Materials),
                ["Blend"] = CalculateCategoryScore(result.Issues, IssueCategoryType.Blend),
                ["R&I"] = CalculateCategoryScore(result.Issues, IssueCategoryType.RandI),
                ["Diagnostics"] = CalculateCategoryScore(result.Issues, IssueCategoryType.Diagnostic),
                ["Calibrations"] = CalculateCategoryScore(result.Issues, IssueCategoryType.Calibration),
                ["Refinish"] = CalculateCategoryScore(result.Issues, IssueCategoryType.Refinish)
            };

            // Set grade
            result.Grade = result.OverallScore switch
            {
                >= 95 => "A+",
                >= 90 => "A",
                >= 85 => "B+",
                >= 80 => "B",
                >= 75 => "C+",
                >= 70 => "C",
                >= 60 => "D",
                _ => "F"
            };

            // Count issues by severity
            result.CriticalCount = result.Issues.Count(i => i.Severity == IssueSeverity.Critical);
            result.HighCount = result.Issues.Count(i => i.Severity == IssueSeverity.High);
            result.MediumCount = result.Issues.Count(i => i.Severity == IssueSeverity.Medium);
            result.LowCount = result.Issues.Count(i => i.Severity == IssueSeverity.Low);

            // Calculate potential recovery
            result.PotentialLaborRecovery = result.Issues.Sum(i => i.SuggestedFix?.LaborHours ?? 0);
            result.PotentialCostRecovery = result.Issues.Sum(i => i.SuggestedFix?.EstimatedCost ?? 0);

            // Generate summary
            if (result.OverallScore >= 90)
            {
                result.Summary = "Excellent! This estimate is very complete.";
            }
            else if (result.OverallScore >= 75)
            {
                result.Summary = $"Good estimate. Found {result.Issues.Count} potential improvements.";
            }
            else if (result.OverallScore >= 60)
            {
                result.Summary = $"Fair estimate. Missing {result.CriticalCount + result.HighCount} important items.";
            }
            else
            {
                result.Summary = $"Needs work. {result.CriticalCount} critical items missing.";
            }
        }

        private int CalculateCategoryScore(List<ScoringIssue> issues, IssueCategoryType category)
        {
            var categoryIssues = issues.Where(i => i.Category == category).ToList();
            if (categoryIssues.Count == 0) return 100;

            int deductions = categoryIssues.Sum(i => i.PointDeduction);
            return Math.Max(0, 100 - (deductions * 3)); // Amplify for category score
        }

        #endregion

        #region Helper Methods

        private CommonlyMissedData? LoadCommonlyMissedItems()
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = Path.Combine(appDir, "Data", "CommonlyMissedItems.json");

                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<CommonlyMissedData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scoring] Error loading commonly missed items: {ex.Message}");
            }
            return null;
        }

        private Dictionary<string, List<BlendRule>> InitializeBlendRules()
        {
            return new Dictionary<string, List<BlendRule>>
            {
                ["hood"] = new() { new("Fender", 0.5m, true), new("Fender", 0.5m, true) },
                ["fender"] = new() { new("Hood", 0.5m, true), new("Door", 0.5m, true) },
                ["door"] = new() { new("Fender", 0.5m, false), new("Quarter Panel", 0.5m, false) },
                ["quarter"] = new() { new("Door", 0.5m, true), new("Bumper", 0.3m, false) },
                ["bumper"] = new() { new("Fender", 0.3m, false) },
                ["roof"] = new() { new("Quarter Panel", 0.5m, false) },
                ["deck"] = new() { new("Quarter Panel", 0.5m, true) },
                ["trunk"] = new() { new("Quarter Panel", 0.5m, true) }
            };
        }

        private Dictionary<string, List<string>> InitializeOperationChains()
        {
            return new Dictionary<string, List<string>>
            {
                ["bumper_replace"] = new() { "Flex Additive", "Adhesion Promoter", "Transfer Parts" },
                ["quarter_replace"] = new() { "Corrosion Protection", "Weld-Thru Primer", "Seam Sealer", "Adjacent Blend" },
                ["door_replace"] = new() { "Transfer Hardware", "Seam Sealer", "Adjacent Blend" },
                ["hood_replace"] = new() { "Transfer Insulator", "Blend Fenders" },
                ["windshield_replace"] = new() { "ADAS Calibration", "Transfer Sensors" }
            };
        }

        private IssueCategoryType GetIssueCategory(string category)
        {
            return category.ToLowerInvariant() switch
            {
                "materials" => IssueCategoryType.Materials,
                "refinish" => IssueCategoryType.Refinish,
                "labor" => IssueCategoryType.Labor,
                "r&i" or "r & i" or "randi" => IssueCategoryType.RandI,
                "electrical" => IssueCategoryType.Electrical,
                "diagnostic" => IssueCategoryType.Diagnostic,
                "calibration" => IssueCategoryType.Calibration,
                "mechanical" => IssueCategoryType.Mechanical,
                _ => IssueCategoryType.Other
            };
        }

        private IssueSeverity GetSeverity(string priority)
        {
            return priority.ToLowerInvariant() switch
            {
                "critical" => IssueSeverity.Critical,
                "high" => IssueSeverity.High,
                "medium" => IssueSeverity.Medium,
                "low" => IssueSeverity.Low,
                _ => IssueSeverity.Medium
            };
        }

        private string GetOperationType(string category)
        {
            return category.ToLowerInvariant() switch
            {
                "materials" => "Add",
                "refinish" => "Refinish",
                "labor" => "Add",
                "r&i" => "R&I",
                "electrical" => "Add",
                "diagnostic" => "Add",
                "calibration" => "Sublet",
                "mechanical" => "Add",
                _ => "Add"
            };
        }

        private int GetPointDeduction(string priority)
        {
            return priority.ToLowerInvariant() switch
            {
                "critical" => 8,
                "high" => 5,
                "medium" => 3,
                "low" => 1,
                _ => 2
            };
        }

        #endregion
    }

    #region Data Models

    public class EstimateScoringResult
    {
        public int OverallScore { get; set; }
        public string Grade { get; set; } = "";
        public string Summary { get; set; } = "";
        public string? VehicleInfo { get; set; }
        public int TotalLineItems { get; set; }
        public decimal EstimateTotal { get; set; }
        public DateTime AssessedAt { get; set; }

        public List<ScoringIssue> Issues { get; set; } = new();
        public Dictionary<string, int> CategoryScores { get; set; } = new();

        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }

        public decimal PotentialLaborRecovery { get; set; }
        public decimal PotentialCostRecovery { get; set; }
    }

    public class ScoringIssue
    {
        public IssueCategoryType Category { get; set; }
        public IssueSeverity Severity { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string WhyNeeded { get; set; } = "";
        public string TriggeredBy { get; set; } = "";
        public SuggestedFix? SuggestedFix { get; set; }
        public int PointDeduction { get; set; }
        public bool IsSelected { get; set; } = false;
    }

    public class SuggestedFix
    {
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal EstimatedCost { get; set; }
        public string? DegReference { get; set; }
    }

    public enum IssueCategoryType
    {
        Materials,
        Blend,
        RandI,
        Diagnostic,
        Calibration,
        Refinish,
        Labor,
        Electrical,
        Mechanical,
        Other
    }

    public enum IssueSeverity
    {
        Critical,
        High,
        Medium,
        Low
    }

    internal class BlendRule
    {
        public string AdjacentPanel { get; }
        public decimal TypicalHours { get; }
        public bool IsRequired { get; }

        public BlendRule(string panel, decimal hours, bool required)
        {
            AdjacentPanel = panel;
            TypicalHours = hours;
            IsRequired = required;
        }
    }

    internal class CommonlyMissedData
    {
        public string? Version { get; set; }
        public Dictionary<string, OperationCheckData>? OperationChecks { get; set; }
        public Dictionary<string, GlobalCheckData>? GlobalChecks { get; set; }
    }

    internal class OperationCheckData
    {
        public string? Operation { get; set; }
        public List<string>? TriggerKeywords { get; set; }
        public List<string>? TriggerOperations { get; set; }
        public List<MissedItemData>? MissedItems { get; set; }
    }

    internal class MissedItemData
    {
        public string Item { get; set; } = "";
        public string? Category { get; set; }
        public string? Description { get; set; }
        public string? WhyNeeded { get; set; }
        public string? DegReference { get; set; }
        public decimal TypicalCost { get; set; }
        public decimal LaborHours { get; set; }
        public string? Priority { get; set; }
    }

    internal class GlobalCheckData
    {
        public decimal? Threshold { get; set; }
        public string? Condition { get; set; }
        public string? Description { get; set; }
        public List<MissedItemData>? Checks { get; set; }
    }

    #endregion
}
