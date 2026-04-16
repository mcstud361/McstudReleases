#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace McStudDesktop.Services
{
    /// <summary>
    /// A single matching line item paired with enough parent-estimate context
    /// to render a row in the Insurance Payments Report.
    /// </summary>
    public class PaymentRow
    {
        public DateTime Date { get; set; }
        public string InsuranceCompany { get; set; } = "";
        public string ClaimNumber { get; set; } = "";
        public string RONumber { get; set; } = "";
        public string VehicleInfo { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public bool WasPaid { get; set; } = true;
    }

    /// <summary>
    /// Report model capturing a query run on the local estimate history.
    /// Data is sourced entirely from EstimateHistoryDatabase and never leaves the machine.
    /// </summary>
    public class InsurancePaymentReport
    {
        public string Insurer { get; set; } = "";
        public string OperationKeyword { get; set; } = "";
        public OperationPaymentSummary Summary { get; set; } = new();
        public List<PaymentRow> Rows { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        public int UniqueEstimateCount => Rows
            .Select(r => r.ClaimNumber + "|" + r.RONumber + "|" + r.Date.Ticks)
            .Distinct()
            .Count();

        public DateTime? EarliestDate => Rows.Count == 0 ? null : Rows.Min(r => r.Date);
        public DateTime? LatestDate => Rows.Count == 0 ? null : Rows.Max(r => r.Date);
    }

    /// <summary>
    /// Builds insurance payment reports from local estimate history.
    /// Produces PDF and plain-text clipboard output for pasting into estimate notes.
    /// </summary>
    public class InsurancePaymentReportService
    {
        private static InsurancePaymentReportService? _instance;
        public static InsurancePaymentReportService Instance =>
            _instance ??= new InsurancePaymentReportService();

        private InsurancePaymentReportService() { }

        /// <summary>
        /// Build a report for a specific insurer (or "(All insurers)") and operation keyword.
        /// includeDenied=false limits rows to lines where WasPaid==true.
        /// </summary>
        public InsurancePaymentReport Build(string insurer, string operationKeyword, bool includeDenied)
        {
            var keyword = operationKeyword ?? "";
            var report = new InsurancePaymentReport
            {
                Insurer = string.IsNullOrWhiteSpace(insurer) ? "All Insurers" : insurer,
                OperationKeyword = keyword,
            };

            var db = EstimateHistoryDatabase.Instance;
            var isAll = string.IsNullOrWhiteSpace(insurer) ||
                        insurer.Equals("All Insurers", StringComparison.OrdinalIgnoreCase) ||
                        insurer.Equals("(All insurers)", StringComparison.OrdinalIgnoreCase);

            List<EstimateSearchResult> matches;
            if (isAll)
            {
                matches = new List<EstimateSearchResult>();
                foreach (var ins in db.KnownInsurers)
                {
                    matches.AddRange(db.FindEstimatesWithOperation(ins, keyword));
                }

                // Aggregate summary across insurers
                var aggregate = new OperationPaymentSummary
                {
                    InsuranceCompany = "All Insurers",
                    OperationKeyword = keyword,
                };
                foreach (var ins in db.KnownInsurers)
                {
                    var s = db.GetOperationPaymentStats(ins, keyword);
                    aggregate.TimesPaid += s.TimesPaid;
                    aggregate.TotalAmountPaid += s.TotalAmountPaid;
                    aggregate.TotalHoursPaid += s.TotalHoursPaid;
                    aggregate.EstimateIds.AddRange(s.EstimateIds);
                }
                if (aggregate.TimesPaid > 0)
                    aggregate.AverageHours = aggregate.TotalHoursPaid / aggregate.TimesPaid;
                report.Summary = aggregate;
            }
            else
            {
                matches = db.FindEstimatesWithOperation(insurer, keyword);
                report.Summary = db.GetOperationPaymentStats(insurer, keyword);
            }

            // Flatten matching line items into payment rows
            foreach (var match in matches)
            {
                foreach (var li in match.MatchingLineItems)
                {
                    if (!includeDenied && !li.WasPaid)
                        continue;

                    report.Rows.Add(new PaymentRow
                    {
                        Date = match.Estimate.ImportedDate,
                        InsuranceCompany = match.Estimate.InsuranceCompany,
                        ClaimNumber = match.Estimate.ClaimNumber,
                        RONumber = match.Estimate.RONumber,
                        VehicleInfo = match.Estimate.VehicleInfo,
                        OperationType = li.OperationType,
                        Description = li.Description,
                        LaborHours = li.LaborHours,
                        RefinishHours = li.RefinishHours,
                        Price = li.Price,
                        WasPaid = li.WasPaid,
                    });
                }
            }

            report.Rows = report.Rows.OrderByDescending(r => r.Date).ToList();
            return report;
        }

        /// <summary>
        /// Generate a plain-text block ready to paste at the top of an estimate.
        /// </summary>
        public string GenerateClipboardText(InsurancePaymentReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"INSURER PAYMENT HISTORY — \"{report.OperationKeyword}\"");

            var insurerLabel = string.IsNullOrWhiteSpace(report.Insurer) ? "All insurers" : report.Insurer;
            var paidRows = report.Rows.Where(r => r.WasPaid).ToList();
            var timesPaid = Math.Max(report.Summary.TimesPaid, paidRows.Count);

            sb.AppendLine(
                $"{insurerLabel} has paid for this operation {timesPaid} time{(timesPaid == 1 ? "" : "s")} " +
                $"in our shop's history.");

            if (report.Summary.AverageHours > 0)
                sb.AppendLine($"Average: {report.Summary.AverageHours:0.##} hr labor per occurrence.");

            if (report.EarliestDate.HasValue && report.LatestDate.HasValue)
                sb.AppendLine($"Date range: {report.EarliestDate:MM/dd/yy} – {report.LatestDate:MM/dd/yy}.");

            sb.AppendLine();

            if (paidRows.Count > 0)
            {
                sb.AppendLine("Recent examples:");
                foreach (var row in paidRows.Take(8))
                {
                    var hours = row.LaborHours + row.RefinishHours;
                    var claim = string.IsNullOrWhiteSpace(row.ClaimNumber) ? "(no claim #)" : row.ClaimNumber;
                    var ro = string.IsNullOrWhiteSpace(row.RONumber) ? "" : $"/ RO {row.RONumber} ";
                    var vehicle = string.IsNullOrWhiteSpace(row.VehicleInfo) ? "" : $" — {row.VehicleInfo}";
                    sb.AppendLine(
                        $"  • Claim {claim} {ro}({row.Date:MM/dd/yy}) — {hours:0.##} hr{vehicle}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Produce an ink-friendly PDF of the report. Returns the temp file path.
        /// </summary>
        public string GeneratePdf(InsurancePaymentReport report)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var safeInsurer = System.Text.RegularExpressions.Regex
                .Replace(report.Insurer, @"[^A-Za-z0-9]+", "_")
                .Trim('_');
            var safeOp = System.Text.RegularExpressions.Regex
                .Replace(report.OperationKeyword, @"[^A-Za-z0-9]+", "_")
                .Trim('_');
            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"payment_history_{safeInsurer}_{safeOp}_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(0.5f, Unit.Inch);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Header().Element(c => ComposeHeader(c, report));
                    page.Content().Element(c => ComposeContent(c, report));
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span($"Generated {DateTime.Now:MM/dd/yyyy h:mm tt}")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                        text.Span(" — McStudDesktop Learned History — Confidential / Internal Use Only")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            }).GeneratePdf(tempPath);

            return tempPath;
        }

        private void ComposeHeader(IContainer container, InsurancePaymentReport report)
        {
            container.Column(column =>
            {
                column.Item().Padding(8).Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text($"Insurance Payment History — {report.Insurer}")
                            .FontSize(16).Bold().FontColor(Colors.Black);
                        left.Item().Text($"Operation: \"{report.OperationKeyword}\"")
                            .FontSize(11).FontColor(Colors.Grey.Darken2);
                    });

                    row.ConstantItem(160).AlignRight().Column(right =>
                    {
                        right.Item().Text($"Generated: {report.GeneratedAt:MM/dd/yyyy}")
                            .FontSize(9).FontColor(Colors.Grey.Darken2);
                        right.Item().Text($"Matches: {report.Rows.Count}")
                            .FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                });
                column.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                column.Item().PaddingTop(6);
            });
        }

        private void ComposeContent(IContainer container, InsurancePaymentReport report)
        {
            container.Column(column =>
            {
                // Summary block
                column.Item().PaddingBottom(10).Column(summary =>
                {
                    var paidCount = Math.Max(report.Summary.TimesPaid, report.Rows.Count(r => r.WasPaid));
                    summary.Item().Text($"{report.Insurer} paid for \"{report.OperationKeyword}\" " +
                                        $"{paidCount} time{(paidCount == 1 ? "" : "s")} " +
                                        $"across {report.UniqueEstimateCount} estimate{(report.UniqueEstimateCount == 1 ? "" : "s")}.")
                        .FontSize(10).Bold().FontColor(Colors.Black);

                    if (report.Summary.AverageHours > 0)
                        summary.Item().Text($"Average: {report.Summary.AverageHours:0.##} hr labor per occurrence.")
                            .FontSize(10).FontColor(Colors.Grey.Darken3);

                    if (report.Summary.TotalAmountPaid > 0)
                        summary.Item().Text($"Total paid: ${report.Summary.TotalAmountPaid:N2}")
                            .FontSize(10).FontColor(Colors.Grey.Darken3);

                    if (report.EarliestDate.HasValue && report.LatestDate.HasValue)
                        summary.Item().Text($"Date range: {report.EarliestDate:MM/dd/yy} – {report.LatestDate:MM/dd/yy}")
                            .FontSize(10).FontColor(Colors.Grey.Darken3);
                });

                // Table
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(55);    // Date
                        columns.RelativeColumn(1.1f);  // Claim #
                        columns.ConstantColumn(50);    // RO #
                        columns.RelativeColumn(1.6f);  // Vehicle
                        columns.RelativeColumn(2.4f);  // Operation / Description
                        columns.ConstantColumn(32);    // Op type
                        columns.ConstantColumn(38);    // Hrs
                        columns.ConstantColumn(42);    // Refin
                        columns.ConstantColumn(52);    // $
                    });

                    table.Header(header =>
                    {
                        var style = TextStyle.Default.Bold().FontColor(Colors.Black).FontSize(9);
                        void H(string label, bool center = false)
                        {
                            var cell = header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken1)
                                .Padding(4);
                            if (center) cell.AlignCenter().Text(label).Style(style);
                            else cell.Text(label).Style(style);
                        }

                        H("Date");
                        H("Claim #");
                        H("RO #");
                        H("Vehicle");
                        H("Operation");
                        H("Op", center: true);
                        H("Hrs", center: true);
                        H("Refin", center: true);
                        H("$", center: true);
                    });

                    for (int i = 0; i < report.Rows.Count; i++)
                    {
                        var row = report.Rows[i];
                        var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten5;
                        var textColor = row.WasPaid ? Colors.Black : Colors.Red.Darken2;

                        void Cell(string text, bool center = false)
                        {
                            var cell = table.Cell().Background(bg).Padding(3);
                            var placed = center ? cell.AlignCenter() : cell;
                            placed.Text(text).FontSize(8).FontColor(textColor);
                        }

                        var desc = string.IsNullOrWhiteSpace(row.Description) ? row.OperationType : row.Description;
                        if (desc.Length > 80) desc = desc.Substring(0, 77) + "...";

                        Cell(row.Date.ToString("MM/dd/yy"));
                        Cell(string.IsNullOrWhiteSpace(row.ClaimNumber) ? "—" : row.ClaimNumber);
                        Cell(string.IsNullOrWhiteSpace(row.RONumber) ? "—" : row.RONumber);
                        Cell(row.VehicleInfo ?? "");
                        Cell(desc);
                        Cell(row.OperationType ?? "", center: true);
                        Cell(row.LaborHours > 0 ? row.LaborHours.ToString("0.##") : "—", center: true);
                        Cell(row.RefinishHours > 0 ? row.RefinishHours.ToString("0.##") : "—", center: true);
                        Cell(row.Price > 0 ? $"${row.Price:0.##}" : "—", center: true);
                    }

                    if (report.Rows.Count == 0)
                    {
                        table.Cell().ColumnSpan(9).Padding(20).AlignCenter()
                            .Text("No matching payment history found.")
                            .FontColor(Colors.Grey.Medium).Italic();
                    }
                });
            });
        }
    }
}
