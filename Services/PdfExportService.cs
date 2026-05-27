#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using McStudDesktop.Views;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Public class representing an item that can be exported to PDF
    /// </summary>
    public class PdfExportItem
    {
        public string? Id { get; set; }
        public string? Term { get; set; }
        public string? Category { get; set; }
        public string? Status { get; set; }
        public string? PPageLocation { get; set; }
        public string? PPageRef { get; set; }
        public string? PPageSystem { get; set; }
        public string? Definition { get; set; }
        public string? Details { get; set; }
        public string? DegInquiry { get; set; }
        public string? DegResponse { get; set; }
        public string? ImagePath { get; set; } // For future photo support
        public Dictionary<string, string>? Links { get; set; }
    }

    /// <summary>
    /// Service for generating PDF documents with selected definitions, P-pages, and DEG inquiries
    /// </summary>
    public class PdfExportService
    {
        private List<PdfExportItem> _allItems = new();

        public PdfExportService()
        {
            // Set QuestPDF license type (Community is free for most uses)
            QuestPDF.Settings.License = LicenseType.Community;
            LoadDefinitions();
        }

        private void LoadDefinitions()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Data", "Definitions.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<DefinitionsData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (data?.Definitions != null)
                    {
                        _allItems = data.Definitions
                            .Select(d => new PdfExportItem
                            {
                                Id = d.Id,
                                Term = d.Term,
                                Category = d.Category,
                                Status = d.Status,
                                PPageLocation = d.PPageLocation,
                                PPageRef = d.PPageRef,
                                PPageSystem = d.PPageSystem,
                                Definition = d.Definition,
                                Details = d.Details,
                                DegInquiry = d.DegInquiry,
                                DegResponse = d.DegResponse,
                                Links = d.Links
                            })
                            .OrderBy(d => d.Term)
                            .ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdfExportService] Error loading definitions: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all available items for selection
        /// </summary>
        public List<PdfExportItem> GetAllItems()
        {
            return _allItems.ToList();
        }

        /// <summary>
        /// Get available counts for each category
        /// </summary>
        public (int Operations, int Definitions, int PPages, int DegInquiries) GetAvailableCounts()
        {
            int operations = _allItems.Count(d => d.Category == "Labor Operations");
            int definitions = _allItems.Count;
            int pPages = _allItems.Count(d => !string.IsNullOrEmpty(d.PPageRef));
            int degInquiries = _allItems.Count(d => !string.IsNullOrEmpty(d.DegInquiry));

            return (operations, definitions, pPages, degInquiries);
        }

        /// <summary>
        /// Generate a PDF with the specified selected items
        /// </summary>
        public string GeneratePdfFromItems(List<PdfExportItem> selectedItems, string? outputPath = null)
        {
            // Generate output path if not specified
            if (string.IsNullOrEmpty(outputPath))
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var fileName = $"McStud_Export_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                outputPath = Path.Combine(documentsPath, fileName);
            }

            // Group items by category, then alphabetical within each group
            var groupedItems = selectedItems
                .GroupBy(i => i.Category ?? "Uncategorized")
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Create the PDF document
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(ComposeHeader);

                    page.Content().Element(content =>
                    {
                        content.PaddingVertical(10).Column(column =>
                        {
                            column.Spacing(12);

                            // Glossary (after header, before TOC)
                            if (ReferenceExportConfigService.Instance.Config.ShowGlossary)
                            {
                                column.Item().Element(e => ComposeGlossary(e, groupedItems));
                            }

                            // Table of Contents (conditionally shown)
                            if (ReferenceExportConfigService.Instance.Config.ShowTableOfContents)
                            {
                                column.Item().Element(e => ComposeTableOfContents(e, groupedItems));
                            }

                            // Items grouped by category
                            foreach (var group in groupedItems)
                            {
                                // Category divider
                                column.Item().Element(e => ComposeCategoryDivider(e, group.Key, group.Count()));

                                // Items within this category
                                foreach (var item in group.OrderBy(i => i.Term ?? "", StringComparer.OrdinalIgnoreCase))
                                {
                                    column.Item().Element(e => ComposeItemDetail(e, item));
                                }
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf(outputPath);

            return outputPath;
        }

        /// <summary>
        /// Generate a PDF with the specified counts (legacy method)
        /// </summary>
        public string GeneratePdf(int operationsCount, int definitionsCount, int pPagesCount, int degInquiriesCount, string? outputPath = null)
        {
            // Select items based on counts
            var operations = _allItems
                .Where(d => d.Category == "Labor Operations")
                .Take(operationsCount)
                .ToList();

            var definitions = _allItems
                .Take(definitionsCount)
                .ToList();

            var pPages = _allItems
                .Where(d => !string.IsNullOrEmpty(d.PPageRef))
                .Take(pPagesCount)
                .ToList();

            var degInquiries = _allItems
                .Where(d => !string.IsNullOrEmpty(d.DegInquiry))
                .Take(degInquiriesCount)
                .ToList();

            // Combine all unique items
            var allSelected = operations
                .Union(definitions)
                .Union(pPages)
                .Union(degInquiries)
                .DistinctBy(x => x.Id)
                .ToList();

            return GeneratePdfFromItems(allSelected, outputPath);
        }

        private void ComposeHeader(IContainer container)
        {
            var config = ReferenceExportConfigService.Instance.Config;
            container.Column(col =>
            {
                // Shop name large at top (if set)
                if (!string.IsNullOrWhiteSpace(config.ShopName))
                {
                    col.Item().Text(config.ShopName)
                        .FontSize(22)
                        .Bold()
                        .FontColor(Colors.Blue.Darken3);
                    col.Item().PaddingBottom(4);
                }

                // Title + subtitle
                col.Item().Text(config.HeaderTitle)
                    .FontSize(16)
                    .Bold()
                    .FontColor(Colors.Blue.Darken2);

                col.Item().Text(config.HeaderSubtitle)
                    .FontSize(10)
                    .FontColor(Colors.Blue.Darken1);

                // Vehicle info block
                if (config.ShowVehicleInfo && HasVehicleInfo(config))
                {
                    col.Item().PaddingTop(6).Background(Colors.Grey.Lighten4).Padding(8).Row(row =>
                    {
                        row.RelativeItem().Column(vCol =>
                        {
                            if (!string.IsNullOrWhiteSpace(config.VehicleInfo))
                            {
                                vCol.Item().Text(text =>
                                {
                                    text.Span("Vehicle: ").FontSize(10).Bold();
                                    text.Span(config.VehicleInfo).FontSize(10);
                                });
                            }
                            if (!string.IsNullOrWhiteSpace(config.VIN))
                            {
                                vCol.Item().Text(text =>
                                {
                                    text.Span("VIN: ").FontSize(10).Bold();
                                    text.Span(config.VIN).FontSize(10);
                                });
                            }
                            if (!string.IsNullOrWhiteSpace(config.RONumber))
                            {
                                vCol.Item().Text(text =>
                                {
                                    text.Span("RO#: ").FontSize(10).Bold();
                                    text.Span(config.RONumber).FontSize(10);
                                });
                            }
                        });
                    });
                }

                // Date line
                if (config.ShowDate)
                {
                    string dateStr;
                    try { dateStr = DateTime.Now.ToString(config.DateFormat); }
                    catch { dateStr = DateTime.Now.ToString("MMMM dd, yyyy 'at' h:mm tt"); }

                    col.Item().PaddingTop(4).Text($"Generated: {dateStr}")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken1);
                }
            });
        }

        private static bool HasVehicleInfo(ReferenceExportConfig config)
        {
            return !string.IsNullOrWhiteSpace(config.VehicleInfo)
                || !string.IsNullOrWhiteSpace(config.VIN)
                || !string.IsNullOrWhiteSpace(config.RONumber);
        }

        private void ComposeTableOfContents(IContainer container, List<IGrouping<string, PdfExportItem>> groupedItems)
        {
            container.Background(Colors.Grey.Lighten3).Padding(12).Column(col =>
            {
                col.Item().Text("TABLE OF CONTENTS")
                    .Bold()
                    .FontSize(12)
                    .FontColor(Colors.Blue.Darken2);

                col.Item().PaddingTop(6);

                foreach (var group in groupedItems)
                {
                    col.Item().PaddingTop(4).Text($"{group.Key} ({group.Count()})")
                        .Bold()
                        .FontSize(10)
                        .FontColor(Colors.Blue.Darken3);

                    foreach (var item in group.OrderBy(i => i.Term ?? "", StringComparer.OrdinalIgnoreCase))
                    {
                        var anchor = item.Id ?? item.Term ?? "Unknown";
                        col.Item().PaddingLeft(12).PaddingTop(1).Text(text =>
                        {
                            text.SectionLink(item.Term ?? "Unknown", anchor)
                                .FontSize(9)
                                .FontColor(Colors.Blue.Medium);
                        });
                    }
                }
            });
        }

        private void ComposeCategoryDivider(IContainer container, string categoryName, int count)
        {
            container.Background(Colors.Blue.Darken3).Padding(10).Row(row =>
            {
                row.RelativeItem().Text($"{categoryName}")
                    .Bold()
                    .FontSize(13)
                    .FontColor(Colors.White);

                row.AutoItem().AlignRight().AlignMiddle().Text($"{count} item{(count != 1 ? "s" : "")}")
                    .FontSize(10)
                    .FontColor(Colors.Blue.Lighten3);
            });
        }

        private void ComposeItemDetail(IContainer container, PdfExportItem item)
        {
            var anchor = item.Id ?? item.Term ?? "Unknown";
            container.Section(anchor).Column(col =>
            {
                // Item header with term
                col.Item().Background(Colors.Blue.Darken2).Padding(10).Row(row =>
                {
                    row.RelativeItem().Text(item.Term ?? "Unknown")
                        .Bold()
                        .FontSize(14)
                        .FontColor(Colors.White);

                    // Status badge
                    if (!string.IsNullOrEmpty(item.Status))
                    {
                        var statusColor = item.Status == "NOT INCLUDED" ? Colors.Red.Lighten2 : Colors.Green.Lighten2;
                        row.AutoItem().AlignRight().Text(item.Status)
                            .FontSize(10)
                            .Bold()
                            .FontColor(statusColor);
                    }
                });

                // Content area
                col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(contentCol =>
                {
                    // Category (smaller — TOC already groups by category)
                    if (!string.IsNullOrEmpty(item.Category))
                    {
                        contentCol.Item().Text(item.Category)
                            .FontSize(8)
                            .FontColor(Colors.Grey.Darken1);
                        contentCol.Item().PaddingBottom(6);
                    }

                    // Definition
                    if (!string.IsNullOrEmpty(item.Definition))
                    {
                        contentCol.Item().Text("DEFINITION").FontSize(9).Bold().FontColor(Colors.Blue.Darken3);
                        contentCol.Item().PaddingTop(4).Text(item.Definition).FontSize(10);
                        contentCol.Item().PaddingBottom(10);
                    }

                    // Details
                    if (!string.IsNullOrEmpty(item.Details))
                    {
                        contentCol.Item().Text("DETAILS").FontSize(9).Bold().FontColor(Colors.Blue.Darken3);
                        contentCol.Item().PaddingTop(4).Text(item.Details)
                            .FontSize(10)
                            .FontColor(Colors.Grey.Darken2);
                        contentCol.Item().PaddingBottom(10);
                    }

                    // P-Page Reference
                    if (!string.IsNullOrEmpty(item.PPageRef))
                    {
                        contentCol.Item().Background(Colors.Orange.Lighten4).Padding(8).Column(pPageCol =>
                        {
                            pPageCol.Item().Text("P-PAGE REFERENCE").FontSize(9).Bold().FontColor(Colors.Orange.Darken3);
                            pPageCol.Item().PaddingTop(4).Row(row =>
                            {
                                row.AutoItem().Text($"Reference: ").FontSize(10).Bold();
                                row.AutoItem().Text(item.PPageRef).FontSize(10);
                            });
                            if (!string.IsNullOrEmpty(item.PPageLocation))
                            {
                                pPageCol.Item().PaddingTop(2).Row(row =>
                                {
                                    row.AutoItem().Text($"Location: ").FontSize(10).Bold();
                                    row.AutoItem().Text(item.PPageLocation).FontSize(10);
                                });
                            }
                            if (!string.IsNullOrEmpty(item.PPageSystem))
                            {
                                pPageCol.Item().PaddingTop(2).Row(row =>
                                {
                                    row.AutoItem().Text($"Systems: ").FontSize(10).Bold();
                                    row.AutoItem().Text(item.PPageSystem).FontSize(10);
                                });
                            }

                            // Clickable CCC MOTOR Guide link
                            var cccUrl = DefinitionsView.GetPPageUrl(item.PPageRef ?? "");
                            if (cccUrl != null)
                            {
                                pPageCol.Item().PaddingTop(6).Text(text =>
                                {
                                    text.Hyperlink(cccUrl, "View in CCC MOTOR Guide \u2192")
                                        .FontSize(9).Bold().FontColor(Colors.Blue.Medium);
                                });
                            }
                        });
                        contentCol.Item().PaddingBottom(10);
                    }
                    else if (string.IsNullOrEmpty(item.PPageRef))
                    {
                        contentCol.Item().Background(Colors.Grey.Lighten3).Padding(8).Text("Shop/Industry Defined \u2014 No P-Page reference available")
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                        contentCol.Item().PaddingBottom(10);
                    }

                    // DEG Inquiry / Source Reference — only show if there is a response OR
                    // if the inquiry number is a real DEG number (numeric, not a GTE reference label)
                    if (!string.IsNullOrEmpty(item.DegInquiry) && !string.IsNullOrEmpty(item.DegResponse))
                    {
                        var isRealDegNumber = item.DegInquiry.Any(char.IsDigit) &&
                                              !item.DegInquiry.Contains("GTE", StringComparison.OrdinalIgnoreCase) &&
                                              !item.DegInquiry.Contains("P-Page", StringComparison.OrdinalIgnoreCase) &&
                                              !item.DegInquiry.Contains("MOTOR", StringComparison.OrdinalIgnoreCase);
                        var labelText = isRealDegNumber ? $"DEG INQUIRY #{item.DegInquiry}" : $"SOURCE: {item.DegInquiry}";
                        contentCol.Item().Background(Colors.Blue.Lighten4).Padding(8).Column(degCol =>
                        {
                            degCol.Item().Text(labelText)
                                .FontSize(10)
                                .Bold()
                                .FontColor(Colors.Blue.Darken3);

                            degCol.Item().PaddingTop(6).Text("Response:")
                                .FontSize(9)
                                .Bold()
                                .FontColor(Colors.Grey.Darken2);
                            degCol.Item().PaddingTop(2).Text(item.DegResponse)
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken1);
                        });
                        contentCol.Item().PaddingBottom(10);
                    }

                    // Links
                    if (item.Links != null && item.Links.Count > 0)
                    {
                        contentCol.Item().Text("REFERENCE LINKS").FontSize(9).Bold().FontColor(Colors.Blue.Darken3);
                        foreach (var link in item.Links)
                        {
                            contentCol.Item().PaddingTop(2).Text(text =>
                            {
                                text.Span($"{link.Key}: ").FontSize(9).Bold();
                                text.Hyperlink(link.Value, link.Value).FontSize(9).FontColor(Colors.Blue.Medium);
                            });
                        }
                    }

                    // Placeholder for future photo support
                    if (!string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                    {
                        contentCol.Item().PaddingTop(10).Text("PHOTO").FontSize(9).Bold().FontColor(Colors.Blue.Darken3);
                        contentCol.Item().PaddingTop(4).Image(item.ImagePath).FitWidth();
                    }
                });
            });
        }

        private void ComposeGlossary(IContainer container, List<IGrouping<string, PdfExportItem>> groupedItems)
        {
            // Build glossary entries from common abbreviations used in the selected items
            var glossaryEntries = new List<(string Term, string Description)>
            {
                ("NOT INCLUDED", "Operation is not included in the labor allowance and must be added separately"),
                ("INCLUDED", "Operation is already covered within the base labor allowance"),
                ("P-Page", "Procedure page in the estimating system's MOTOR guide — defines labor operations"),
                ("DEG Inquiry", "Database Enhancement Gateway — formal inquiry to correct estimating system data"),
                ("R&I", "Remove & Install — removing a part, then reinstalling the same part"),
                ("R&R", "Remove & Replace — removing a part and replacing it with a new one"),
                ("MET", "Mechanical Estimating Techniques — standard repair procedures reference"),
                ("OEM", "Original Equipment Manufacturer — the vehicle's maker"),
                ("LKQ", "Like Kind and Quality — used/recycled parts of comparable condition"),
                ("A/M", "Aftermarket — non-OEM replacement parts"),
                ("PDR", "Paintless Dent Repair"),
                ("SOP", "Standard Operating Procedure")
            };

            // Filter to entries relevant to the selected items' content
            var categories = groupedItems.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hasNotIncluded = groupedItems.SelectMany(g => g).Any(i => i.Status == "NOT INCLUDED");
            var hasIncluded = groupedItems.SelectMany(g => g).Any(i => i.Status == "INCLUDED");
            var hasPPage = groupedItems.SelectMany(g => g).Any(i => !string.IsNullOrEmpty(i.PPageRef));
            var hasDeg = groupedItems.SelectMany(g => g).Any(i => !string.IsNullOrEmpty(i.DegInquiry));

            var filtered = glossaryEntries.Where(e =>
            {
                if (e.Term == "NOT INCLUDED") return hasNotIncluded;
                if (e.Term == "INCLUDED") return hasIncluded;
                if (e.Term == "P-Page") return hasPPage;
                if (e.Term == "DEG Inquiry") return hasDeg;
                return true; // always show general terms
            }).ToList();

            if (filtered.Count == 0) return;

            container.Background(Colors.Blue.Lighten5).Padding(10).Column(col =>
            {
                col.Item().Text("QUICK REFERENCE GLOSSARY")
                    .Bold()
                    .FontSize(11)
                    .FontColor(Colors.Blue.Darken3);

                col.Item().PaddingTop(6);

                // 2-column layout
                var midpoint = (filtered.Count + 1) / 2;
                var leftCol = filtered.Take(midpoint).ToList();
                var rightCol = filtered.Skip(midpoint).ToList();

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        foreach (var entry in leftCol)
                        {
                            left.Item().PaddingBottom(3).Text(text =>
                            {
                                text.Span($"{entry.Term}: ").FontSize(8).Bold();
                                text.Span(entry.Description).FontSize(8).FontColor(Colors.Grey.Darken2);
                            });
                        }
                    });

                    row.ConstantItem(10); // gap

                    row.RelativeItem().Column(right =>
                    {
                        foreach (var entry in rightCol)
                        {
                            right.Item().PaddingBottom(3).Text(text =>
                            {
                                text.Span($"{entry.Term}: ").FontSize(8).Bold();
                                text.Span(entry.Description).FontSize(8).FontColor(Colors.Grey.Darken2);
                            });
                        }
                    });
                });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            var config = ReferenceExportConfigService.Instance.Config;
            container.AlignCenter().Text(text =>
            {
                if (config.ShowPageNumbers)
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();

                    var footerParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(config.ShopName))
                        footerParts.Add(config.ShopName);
                    if (!string.IsNullOrEmpty(config.FooterText))
                        footerParts.Add(config.FooterText);

                    if (footerParts.Count > 0)
                    {
                        text.Span($" | {string.Join(" | ", footerParts)}")
                            .FontSize(8)
                            .FontColor(Colors.Grey.Darken1);
                    }
                }
                else
                {
                    var footerParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(config.ShopName))
                        footerParts.Add(config.ShopName);
                    if (!string.IsNullOrEmpty(config.FooterText))
                        footerParts.Add(config.FooterText);

                    if (footerParts.Count > 0)
                    {
                        text.Span(string.Join(" | ", footerParts))
                            .FontSize(8)
                            .FontColor(Colors.Grey.Darken1);
                    }
                }
            });
        }

        // Internal data class for JSON deserialization
        private class DefinitionsData
        {
            public string? Version { get; set; }
            public List<DefinitionItemInternal>? Definitions { get; set; }
        }

        private class DefinitionItemInternal
        {
            public string? Id { get; set; }
            public string? Term { get; set; }
            public string? Category { get; set; }
            public string? Status { get; set; }
            public string? PPageLocation { get; set; }
            public string? PPageRef { get; set; }
            public string? PPageSystem { get; set; }
            public string? Definition { get; set; }
            public string? Details { get; set; }
            public string? DegInquiry { get; set; }
            public string? DegResponse { get; set; }
            public Dictionary<string, string>? Links { get; set; }
        }
    }
}
