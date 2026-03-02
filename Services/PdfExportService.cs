#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

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
                            column.Spacing(15);

                            // Summary
                            column.Item().Element(e => ComposeSummaryFromItems(e, selectedItems));

                            // Each selected item
                            foreach (var item in selectedItems)
                            {
                                column.Item().Element(e => ComposeItemDetail(e, item));
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
            container.Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("McStud Tool - Reference Documentation")
                        .FontSize(18)
                        .Bold()
                        .FontColor(Colors.Blue.Darken2);

                    col.Item().Text($"Generated: {DateTime.Now:MMMM dd, yyyy 'at' h:mm tt}")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken1);
                });
            });
        }

        private void ComposeSummaryFromItems(IContainer container, List<PdfExportItem> items)
        {
            int pPages = items.Count(i => !string.IsNullOrEmpty(i.PPageRef));
            int degInquiries = items.Count(i => !string.IsNullOrEmpty(i.DegInquiry));

            container.Background(Colors.Grey.Lighten3).Padding(10).Column(col =>
            {
                col.Item().Text("Document Summary").Bold().FontSize(12);
                col.Item().PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Text($"Total Items: {items.Count}");
                    row.RelativeItem().Text($"With P-Pages: {pPages}");
                    row.RelativeItem().Text($"With DEG: {degInquiries}");
                });

                // List item names
                col.Item().PaddingTop(8).Text("Included Items:").Bold().FontSize(10);
                col.Item().PaddingTop(4).Text(string.Join(", ", items.Select(i => i.Term)))
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken2);
            });
        }

        private void ComposeItemDetail(IContainer container, PdfExportItem item)
        {
            container.Column(col =>
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
                col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).Column(contentCol =>
                {
                    // Category
                    if (!string.IsNullOrEmpty(item.Category))
                    {
                        contentCol.Item().Text($"Category: {item.Category}")
                            .FontSize(10)
                            .FontColor(Colors.Grey.Darken1)
                            .Bold();
                        contentCol.Item().PaddingBottom(8);
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
                        });
                        contentCol.Item().PaddingBottom(10);
                    }

                    // DEG Inquiry
                    if (!string.IsNullOrEmpty(item.DegInquiry))
                    {
                        contentCol.Item().Background(Colors.Blue.Lighten4).Padding(8).Column(degCol =>
                        {
                            degCol.Item().Text($"DEG INQUIRY: {item.DegInquiry}")
                                .FontSize(10)
                                .Bold()
                                .FontColor(Colors.Blue.Darken3);

                            if (!string.IsNullOrEmpty(item.DegResponse))
                            {
                                degCol.Item().PaddingTop(6).Text("Response:")
                                    .FontSize(9)
                                    .Bold()
                                    .FontColor(Colors.Grey.Darken2);
                                degCol.Item().PaddingTop(2).Text(item.DegResponse)
                                    .FontSize(10)
                                    .FontColor(Colors.Grey.Darken1);
                            }
                        });
                        contentCol.Item().PaddingBottom(10);
                    }

                    // Links
                    if (item.Links != null && item.Links.Count > 0)
                    {
                        contentCol.Item().Text("REFERENCE LINKS").FontSize(9).Bold().FontColor(Colors.Blue.Darken3);
                        foreach (var link in item.Links)
                        {
                            contentCol.Item().PaddingTop(2).Text($"{link.Key}: {link.Value}")
                                .FontSize(9)
                                .FontColor(Colors.Blue.Medium);
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

        private void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(text =>
            {
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
                text.Span(" | McStud Tool - Professional Estimating Reference")
                    .FontSize(8)
                    .FontColor(Colors.Grey.Darken1);
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
