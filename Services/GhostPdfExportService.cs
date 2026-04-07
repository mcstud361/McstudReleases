#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace McStudDesktop.Services
{
    public class GhostPdfExportService
    {
        // CCC section display order (mirrors GhostEstimatePanel.SectionOrder)
        private static readonly string[] SectionOrder = new[]
        {
            "FRONT BUMPER & GRILLE", "REAR BUMPER",
            "FRONT LAMPS", "REAR LAMPS",
            "RADIATOR SUPPORT",
            "HOOD",
            "FENDER",
            "FRONT DOOR", "REAR DOOR",
            "QUARTER PANEL",
            "PILLARS, ROCKER & FLOOR",
            "ROOF",
            "TRUNK / DECKLID",
            "GLASS",
            "FRAME",
            "RESTRAINT SYSTEMS",
            "ELECTRICAL",
            "INSTRUMENT PANEL",
            "VEHICLE DIAGNOSTICS",
            "MECHANICAL",
            "MISCELLANEOUS OPERATIONS"
        };

        // Section accent colors (hex strings for QuestPDF)
        private static readonly Dictionary<string, string> SectionHexColors = new()
        {
            ["FRONT BUMPER & GRILLE"] = "#64B4FF",
            ["REAR BUMPER"] = "#64B4FF",
            ["FRONT LAMPS"] = "#82BEFF",
            ["REAR LAMPS"] = "#82BEFF",
            ["RADIATOR SUPPORT"] = "#64AAF0",
            ["HOOD"] = "#64B4FF",
            ["FENDER"] = "#64B4FF",
            ["FRONT DOOR"] = "#64B4FF",
            ["REAR DOOR"] = "#64B4FF",
            ["QUARTER PANEL"] = "#64B4FF",
            ["ROOF"] = "#64B4FF",
            ["TRUNK / DECKLID"] = "#64B4FF",
            ["GLASS"] = "#82C8F0",
            ["INSTRUMENT PANEL"] = "#82BEFF",
            ["PILLARS, ROCKER & FLOOR"] = "#FF8282",
            ["FRAME"] = "#FF8282",
            ["RESTRAINT SYSTEMS"] = "#FF6464",
            ["VEHICLE DIAGNOSTICS"] = "#64DCB4",
            ["ELECTRICAL"] = "#FFC850",
            ["MECHANICAL"] = "#82C8FF",
            ["MISCELLANEOUS OPERATIONS"] = "#B4B9BE",
        };

        public GhostPdfExportService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public string GenerateGhostPdf(GuidanceEstimateResult result, List<GuidanceOperation> filteredOps, string? outputPath = null)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var fileName = $"McStud_GhostExport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                outputPath = Path.Combine(documentsPath, fileName);
            }

            var config = GhostExportConfigService.Instance.Config;

            var grouped = filteredOps
                .GroupBy(o => !string.IsNullOrEmpty(o.Section) ? o.Section : o.Category)
                .OrderBy(g => Array.IndexOf(SectionOrder, g.Key) is int idx && idx >= 0 ? idx : 99)
                .ToList();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(c => ComposeHeader(c, result, config));

                    page.Content().Element(content =>
                    {
                        content.PaddingVertical(10).Column(column =>
                        {
                            column.Spacing(10);

                            // Summary cards row
                            if (config.ShowSummaryCards)
                                column.Item().Element(e => ComposeSummaryCards(e, result, filteredOps));

                            // Warnings
                            if (config.ShowWarnings && result.Warnings.Any())
                                column.Item().Element(e => ComposeWarnings(e, result.Warnings));

                            // Pro Tips
                            if (config.ShowProTips && result.ProTips.Any())
                                column.Item().Element(e => ComposeProTips(e, result.ProTips));

                            // Operations grouped by CCC section
                            foreach (var group in grouped)
                            {
                                column.Item().Element(e => ComposeSection(e, group.Key, group.ToList(), config));
                            }

                            // Grand totals
                            column.Item().Element(e => ComposeGrandTotals(e, filteredOps, result));
                        });
                    });

                    page.Footer().Element(c => ComposeFooter(c, config));
                });
            }).GeneratePdf(outputPath);

            // Auto-open the PDF
            try
            {
                Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GhostPdfExport] Could not open PDF: {ex.Message}");
            }

            return outputPath;
        }

        private void ComposeHeader(IContainer container, GuidanceEstimateResult result, GhostExportConfig config)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(titleCol =>
                    {
                        titleCol.Item().Text(config.HeaderTitle)
                            .FontSize(18)
                            .Bold()
                            .FontColor(Colors.Blue.Darken2);

                        titleCol.Item().Text(config.HeaderSubtitle)
                            .FontSize(11)
                            .FontColor(Colors.Blue.Darken1);

                        if (config.ShowDate)
                        {
                            string dateStr;
                            try { dateStr = DateTime.Now.ToString(config.DateFormat); }
                            catch { dateStr = DateTime.Now.ToString("MMMM dd, yyyy 'at' h:mm tt"); }

                            titleCol.Item().PaddingTop(2).Text($"Generated: {dateStr}")
                                .FontSize(9)
                                .FontColor(Colors.Grey.Darken1);
                        }
                    });
                });

                // Vehicle info + damage description
                if (config.ShowVehicleInfo)
                {
                    col.Item().PaddingTop(8).Background(Colors.Grey.Lighten3).Padding(10).Column(infoCol =>
                    {
                        if (!string.IsNullOrEmpty(result.VehicleInfo))
                        {
                            infoCol.Item().Row(row =>
                            {
                                row.AutoItem().Text("Vehicle: ").FontSize(10).Bold();
                                row.AutoItem().Text(result.VehicleInfo).FontSize(10);
                            });
                        }
                        if (!string.IsNullOrEmpty(result.DamageDescription))
                        {
                            infoCol.Item().PaddingTop(2).Row(row =>
                            {
                                row.AutoItem().Text("Damage: ").FontSize(10).Bold();
                                row.RelativeItem().Text(result.DamageDescription).FontSize(10);
                            });
                        }
                    });
                }
            });
        }

        private void ComposeSummaryCards(IContainer container, GuidanceEstimateResult result, List<GuidanceOperation> filteredOps)
        {
            var totalOps = filteredOps.Count;
            var bodyHours = filteredOps.Sum(o => o.LaborHours);
            var refinishHours = filteredOps.Sum(o => o.RefinishHours);
            var learnedOps = filteredOps.Count(o => o.LaborSource != "fallback" && o.LaborSource != "met_data" && !string.IsNullOrEmpty(o.LaborSource));
            var qualityPercent = totalOps > 0 ? (int)(100.0 * learnedOps / totalOps) : 0;

            container.Background(Colors.Grey.Lighten3).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Total Ops").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text(totalOps.ToString()).FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Body Hours").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text($"{bodyHours:F1}h").FontSize(14).Bold().FontColor("#B87333");
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Refinish Hours").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text($"{refinishHours:F1}h").FontSize(14).Bold().FontColor(Colors.Purple.Medium);
                });
                if (result.LearnedDollarTotal.HasValue && result.LearnedDollarTotal > 0)
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("From Estimates").FontSize(8).FontColor(Colors.Grey.Darken1);
                        c.Item().Text($"${result.LearnedDollarTotal:F0}").FontSize(14).Bold().FontColor(Colors.Green.Darken2);
                    });
                }
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Data Quality").FontSize(8).FontColor(Colors.Grey.Darken1);
                    var qualColor = qualityPercent > 70 ? "#2E7D32" :
                                    qualityPercent > 30 ? "#DAB43C" : "#E65100";
                    c.Item().Text($"{qualityPercent}% learned").FontSize(14).Bold().FontColor(qualColor);
                });
            });
        }

        private void ComposeWarnings(IContainer container, List<string> warnings)
        {
            container.Background("#FFF3E0").Padding(10).Column(col =>
            {
                col.Item().Text("WARNINGS").FontSize(9).Bold().FontColor(Colors.Orange.Darken3);
                foreach (var warning in warnings)
                {
                    col.Item().PaddingTop(3).Text($"• {warning}").FontSize(10).FontColor(Colors.Orange.Darken2);
                }
            });
        }

        private void ComposeProTips(IContainer container, List<string> tips)
        {
            container.Background("#E8F5E9").Padding(10).Column(col =>
            {
                col.Item().Text("PRO TIPS").FontSize(9).Bold().FontColor(Colors.Green.Darken3);
                foreach (var tip in tips)
                {
                    col.Item().PaddingTop(3).Text($"• {tip}").FontSize(10).FontColor(Colors.Green.Darken2);
                }
            });
        }

        private void ComposeSection(IContainer container, string sectionName, List<GuidanceOperation> operations, GhostExportConfig config)
        {
            var accentHex = SectionHexColors.GetValueOrDefault(sectionName, "#969BA0");

            container.Column(col =>
            {
                // Section header
                col.Item().Background(accentHex).Padding(8).Row(row =>
                {
                    row.RelativeItem().Text(sectionName.ToUpper())
                        .Bold()
                        .FontSize(11)
                        .FontColor(Colors.White);

                    if (config.ShowSectionSubtotals)
                    {
                        var bodyHrs = operations.Sum(o => o.LaborHours);
                        var rfnHrs = operations.Sum(o => o.RefinishHours);
                        var parts = operations.Sum(o => o.Price);
                        var totals = new List<string>();
                        if (bodyHrs > 0) totals.Add($"Body: {bodyHrs:F1}");
                        if (rfnHrs > 0) totals.Add($"Rfn: {rfnHrs:F1}");
                        if (parts > 0) totals.Add($"Parts: ${parts:F2}");

                        row.AutoItem().AlignRight().AlignMiddle().Text(string.Join("  |  ", totals))
                            .FontSize(9)
                            .FontColor(Colors.White);
                    }
                });

                // Column headers
                col.Item().Background(Colors.Grey.Lighten3).Padding(4).Row(hdr =>
                {
                    hdr.ConstantItem(25).Text("#").FontSize(8).Bold().FontColor(Colors.Grey.Darken2);
                    hdr.RelativeItem(4).Text("Description").FontSize(8).Bold().FontColor(Colors.Grey.Darken2);
                    hdr.ConstantItem(40).Text("Type").FontSize(8).Bold().FontColor(Colors.Grey.Darken2);
                    hdr.ConstantItem(45).AlignRight().Text("Body").FontSize(8).Bold().FontColor(Colors.Grey.Darken2);
                    hdr.ConstantItem(45).AlignRight().Text("Paint").FontSize(8).Bold().FontColor(Colors.Grey.Darken2);
                    hdr.ConstantItem(55).AlignRight().Text("Parts").FontSize(8).Bold().FontColor(Colors.Grey.Darken2);
                });

                // Operation rows
                int lineNum = 1;
                foreach (var op in operations)
                {
                    var isEven = lineNum % 2 == 0;
                    var bgColor = isEven ? Colors.Grey.Lighten4 : Colors.White;

                    col.Item().Background(bgColor).Padding(4).Column(opCol =>
                    {
                        // Main row
                        opCol.Item().Row(opRow =>
                        {
                            opRow.ConstantItem(25).Text(lineNum.ToString()).FontSize(9).FontColor(Colors.Grey.Darken1);

                            // Description + optional confidence label
                            opRow.RelativeItem(4).Text(text =>
                            {
                                text.Span(op.Description).FontSize(9);
                                if (config.ShowConfidenceLabels && !string.IsNullOrEmpty(op.ConfidenceLabel))
                                {
                                    var confColor = op.ConfidenceLabel switch
                                    {
                                        "High" => "#2E7D32",
                                        "Medium" => "#DAB43C",
                                        _ => "#E65100"
                                    };
                                    text.Span($"  [{op.ConfidenceLabel}]").FontSize(7).FontColor(confColor);
                                }
                            });

                            var typeCode = op.OperationType switch
                            {
                                "Replace" or "Repl" => "REPL",
                                "Repair" or "Rpr" => "RPR",
                                "Refinish" or "Rfn" => "RFN",
                                "Blend" => "BLD",
                                "Body" => "BODY",
                                "Mech" => "MECH",
                                "Frame" => "FRM",
                                "r&i" or "R&I" => "R&I",
                                _ => op.OperationType?.ToUpper() ?? ""
                            };
                            opRow.ConstantItem(40).Text(typeCode).FontSize(8).FontColor(Colors.Grey.Darken2);

                            opRow.ConstantItem(45).AlignRight().Text(op.LaborHours > 0 ? $"{op.LaborHours:F1}" : "")
                                .FontSize(9);
                            opRow.ConstantItem(45).AlignRight().Text(op.RefinishHours > 0 ? $"{op.RefinishHours:F1}" : "")
                                .FontSize(9).FontColor(Colors.Blue.Darken1);
                            opRow.ConstantItem(55).AlignRight().Text(op.Price > 0 ? $"${op.Price:F2}" : "")
                                .FontSize(9).FontColor(Colors.Green.Darken2);
                        });

                        // Detail sub-rows
                        if (config.ShowOperationDetails)
                        {
                            var hasDetails = !string.IsNullOrEmpty(op.Source) ||
                                             !string.IsNullOrEmpty(op.Justification) ||
                                             !string.IsNullOrEmpty(op.PPageReference) ||
                                             !string.IsNullOrEmpty(op.DEGReference);

                            if (hasDetails)
                            {
                                opCol.Item().PaddingLeft(25).Column(detailCol =>
                                {
                                    if (!string.IsNullOrEmpty(op.Source))
                                        detailCol.Item().Text($"Source: {op.Source}").FontSize(7).FontColor(Colors.Grey.Darken1);
                                    if (!string.IsNullOrEmpty(op.Justification) && op.Justification != op.Source)
                                        detailCol.Item().Text($"Why: {op.Justification}").FontSize(7).FontColor(Colors.Blue.Darken1);
                                    if (!string.IsNullOrEmpty(op.PPageReference))
                                        detailCol.Item().Text($"P-Page: {op.PPageReference}").FontSize(7).FontColor(Colors.Orange.Darken2);
                                    if (!string.IsNullOrEmpty(op.DEGReference))
                                        detailCol.Item().Text($"DEG: {op.DEGReference}").FontSize(7).FontColor(Colors.Orange.Darken2);
                                });
                            }
                        }
                    });

                    lineNum++;
                }

                // Section subtotals row
                if (config.ShowSectionSubtotals)
                {
                    var bodyTotal = operations.Sum(o => o.LaborHours);
                    var paintTotal = operations.Sum(o => o.RefinishHours);
                    var partsTotal = operations.Sum(o => o.Price);

                    col.Item().Background(Colors.Grey.Lighten2).Padding(4).Row(subRow =>
                    {
                        subRow.ConstantItem(25).Text("");
                        subRow.RelativeItem(4).Text($"Section Total ({operations.Count} ops)")
                            .FontSize(9).Bold().FontColor(Colors.Grey.Darken2);
                        subRow.ConstantItem(40).Text("");
                        subRow.ConstantItem(45).AlignRight().Text(bodyTotal > 0 ? $"{bodyTotal:F1}" : "")
                            .FontSize(9).Bold();
                        subRow.ConstantItem(45).AlignRight().Text(paintTotal > 0 ? $"{paintTotal:F1}" : "")
                            .FontSize(9).Bold().FontColor(Colors.Blue.Darken1);
                        subRow.ConstantItem(55).AlignRight().Text(partsTotal > 0 ? $"${partsTotal:F2}" : "")
                            .FontSize(9).Bold().FontColor(Colors.Green.Darken2);
                    });
                }
            });
        }

        private void ComposeGrandTotals(IContainer container, List<GuidanceOperation> allOps, GuidanceEstimateResult result)
        {
            var totalBody = allOps.Sum(o => o.LaborHours);
            var totalPaint = allOps.Sum(o => o.RefinishHours);
            var totalParts = allOps.Sum(o => o.Price);

            container.Background(Colors.Blue.Darken3).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("GRAND TOTALS")
                        .FontSize(12).Bold().FontColor(Colors.White);
                    col.Item().PaddingTop(2).Text($"{allOps.Count} operations")
                        .FontSize(9).FontColor(Colors.Blue.Lighten3);
                });

                row.AutoItem().AlignRight().AlignMiddle().Column(col =>
                {
                    var parts = new List<string>();
                    if (totalBody > 0) parts.Add($"Body: {totalBody:F1}h");
                    if (totalPaint > 0) parts.Add($"Refinish: {totalPaint:F1}h");
                    if (totalParts > 0) parts.Add($"Parts: ${totalParts:F2}");

                    col.Item().Text(string.Join("   |   ", parts))
                        .FontSize(11).Bold().FontColor(Colors.White);

                    if (result.GrandTotalLaborDollars > 0)
                    {
                        col.Item().AlignRight().Text($"Est. Labor Total: ${result.GrandTotalLaborDollars:F0}")
                            .FontSize(9).FontColor(Colors.Blue.Lighten3);
                    }
                });
            });
        }

        private void ComposeFooter(IContainer container, GhostExportConfig config)
        {
            container.AlignCenter().Text(text =>
            {
                if (config.ShowPageNumbers)
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                    if (!string.IsNullOrEmpty(config.FooterText))
                    {
                        text.Span($" | {config.FooterText}")
                            .FontSize(8)
                            .FontColor(Colors.Grey.Darken1);
                    }
                }
                else if (!string.IsNullOrEmpty(config.FooterText))
                {
                    text.Span(config.FooterText)
                        .FontSize(8)
                        .FontColor(Colors.Grey.Darken1);
                }
            });
        }
    }
}
