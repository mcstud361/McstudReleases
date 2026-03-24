#nullable enable
using System;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace McStudDesktop.Services
{
    public class LearningReportPdfService
    {
        private static LearningReportPdfService? _instance;
        public static LearningReportPdfService Instance => _instance ??= new LearningReportPdfService();

        private LearningReportPdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public string GenerateReportPdf(LearningAnalysisReport report, string? outputPath = null)
        {
            outputPath ??= GetDefaultOutputPath();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(c => ComposeHeader(c, report));
                    page.Content().Element(c => ComposeContent(c, report));
                    page.Footer().Element(c => ComposeFooter(c));
                });
            }).GeneratePdf(outputPath);

            return outputPath;
        }

        private void ComposeHeader(IContainer container, LearningAnalysisReport report)
        {
            container.Column(col =>
            {
                col.Item().Text("McStud Learning Analysis Report")
                    .FontSize(18).Bold().FontColor(Colors.Blue.Darken2);

                col.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    row.AutoItem().Text($"Estimates: {report.EstimatesAnalyzed} | Patterns: {report.PatternsAnalyzed}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });
        }

        private void ComposeContent(IContainer container, LearningAnalysisReport report)
        {
            container.PaddingVertical(10).Column(column =>
            {
                column.Spacing(8);

                // Summary
                column.Item().Text("Summary").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                column.Item().PaddingBottom(4).Text(report.Summary).FontSize(10).LineHeight(1.4f);

                if (report.Recommendations.Count == 0)
                {
                    column.Item().PaddingTop(10).Text("No recommendations generated.")
                        .FontSize(11).Italic().FontColor(Colors.Grey.Darken1);
                    return;
                }

                // Group recommendations by category
                var grouped = report.Recommendations
                    .GroupBy(r => r.Category)
                    .OrderBy(g => g.Key);

                foreach (var group in grouped)
                {
                    var categoryLabel = group.Key switch
                    {
                        "missing_item" => "Missing Items (CommonlyMissedItems.json)",
                        "must_have" => "Must-Have Operations (MustHaveOperations.json)",
                        "labor_update" => "Labor Hour Updates",
                        "new_rule" => "New Rules (High-Confidence Patterns)",
                        "stale_pattern" => "Stale Patterns (Review/Remove)",
                        _ => group.Key
                    };

                    column.Item().PaddingTop(8).Text(categoryLabel)
                        .FontSize(12).Bold().FontColor(Colors.Blue.Darken1);

                    column.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

                    // Table for recommendations
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);  // Title
                            columns.RelativeColumn(6);  // Detail
                            columns.ConstantColumn(50);  // Confidence
                        });

                        // Header
                        table.Header(header =>
                        {
                            header.Cell().Padding(4).Text("Title").Bold().FontSize(9).FontColor(Colors.White);
                            header.Cell().Padding(4).Text("Detail").Bold().FontSize(9).FontColor(Colors.White);
                            header.Cell().Padding(4).Text("Conf.").Bold().FontSize(9).FontColor(Colors.White);

                            header.Cell().ColumnSpan(3).Background(Colors.Grey.Darken3).PaddingBottom(1);
                        });

                        int idx = 0;
                        foreach (var rec in group.OrderByDescending(r => r.Confidence))
                        {
                            var bgColor = idx % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White;

                            table.Cell().Background(bgColor).Padding(4)
                                .Text(rec.Title).FontSize(9).Bold();

                            table.Cell().Background(bgColor).Padding(4).Column(detailCol =>
                            {
                                detailCol.Item().Text(rec.Detail).FontSize(9).LineHeight(1.3f);
                                if (!string.IsNullOrWhiteSpace(rec.SourceData))
                                    detailCol.Item().PaddingTop(2).Text($"Source: {rec.SourceData}")
                                        .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                            });

                            table.Cell().Background(bgColor).Padding(4).AlignCenter()
                                .Text($"{rec.Confidence:F0}%").FontSize(9);

                            idx++;
                        }
                    });
                }
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
                text.Span(" | McStud Tool - Learning Analysis")
                    .FontSize(8)
                    .FontColor(Colors.Grey.Darken1);
            });
        }

        private string GetDefaultOutputPath()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var fileName = $"LearningAnalysis_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            return Path.Combine(documentsPath, fileName);
        }
    }
}
