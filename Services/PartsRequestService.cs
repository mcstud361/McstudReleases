#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace McStudDesktop.Services
{
    public enum PartsRequestStatus
    {
        Needed,
        Ordered,
        Received,
        Backordered
    }

    public class PartsRequestItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Description { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public PartsRequestStatus Status { get; set; } = PartsRequestStatus.Needed;
        public string Notes { get; set; } = "";
    }

    public class PartsRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string RoNumber { get; set; } = "";
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;
        public List<PartsRequestItem> Items { get; set; } = new();
    }

    public class PartsRequestData
    {
        public int Version { get; set; } = 1;
        public List<PartsRequest> Requests { get; set; } = new();
    }

    public class PartsRequestService
    {
        private static PartsRequestService? _instance;
        public static PartsRequestService Instance => _instance ??= new PartsRequestService();

        private readonly string _dataFolder;
        private readonly string _dataFile;
        private PartsRequestData _data;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private PartsRequestService()
        {
            _dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop");
            _dataFile = Path.Combine(_dataFolder, "PartsRequests.json");
            _data = LoadData();
        }

        public List<PartsRequest> GetAllRequests() => _data.Requests;

        public PartsRequest? GetRequest(string id) =>
            _data.Requests.FirstOrDefault(r => r.Id == id);

        public PartsRequest CreateNew()
        {
            var request = new PartsRequest();
            _data.Requests.Insert(0, request);
            Save();
            return request;
        }

        public void SaveRequest(PartsRequest request)
        {
            request.ModifiedDate = DateTime.Now;
            var idx = _data.Requests.FindIndex(r => r.Id == request.Id);
            if (idx >= 0)
                _data.Requests[idx] = request;
            else
                _data.Requests.Insert(0, request);
            Save();
        }

        public void DeleteRequest(string id)
        {
            _data.Requests.RemoveAll(r => r.Id == id);
            Save();
        }

        private void Save()
        {
            try
            {
                if (!Directory.Exists(_dataFolder))
                    Directory.CreateDirectory(_dataFolder);
                var json = JsonSerializer.Serialize(_data, _jsonOptions);
                File.WriteAllText(_dataFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PartsRequest] Save error: {ex.Message}");
            }
        }

        private PartsRequestData LoadData()
        {
            try
            {
                if (File.Exists(_dataFile))
                {
                    var json = File.ReadAllText(_dataFile);
                    var data = JsonSerializer.Deserialize<PartsRequestData>(json, _jsonOptions);
                    if (data != null)
                        return data;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PartsRequest] Load error: {ex.Message}");
            }
            return new PartsRequestData();
        }

        public string GeneratePdf(PartsRequest request)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"parts_request_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(0.5f, Unit.Inch);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Element(c => ComposeHeader(c, request));
                    page.Content().Element(c => ComposeContent(c, request));
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Generated ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        text.Span(DateTime.Now.ToString("MM/dd/yyyy h:mm tt")).FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            }).GeneratePdf(tempPath);

            return tempPath;
        }

        private void ComposeHeader(IContainer container, PartsRequest request)
        {
            container.Column(column =>
            {
                column.Item().Padding(8).Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text("Parts Request").FontSize(16).Bold().FontColor(Colors.Black);
                    });

                    row.ConstantItem(180).AlignRight().Column(right =>
                    {
                        right.Item().Text($"Created: {request.CreatedDate:MM/dd/yyyy}").FontSize(9).FontColor(Colors.Grey.Darken2);
                        right.Item().Text($"Modified: {request.ModifiedDate:MM/dd/yyyy}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                });
                column.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken1);

                // RO # line — writable area for pen/pencil
                column.Item().PaddingTop(8).PaddingLeft(8).Row(row =>
                {
                    row.ConstantItem(40).AlignBottom().Text("RO #:").FontSize(11).Bold();
                    row.RelativeItem().AlignBottom().BorderBottom(1).BorderColor(Colors.Grey.Medium)
                        .MinHeight(18).Padding(2).Text(request.RoNumber ?? "").FontSize(11);
                });
                column.Item().PaddingTop(8);
            });
        }

        private void ComposeContent(IContainer container, PartsRequest request)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(30);   // #
                    columns.RelativeColumn(3);     // Description
                    columns.RelativeColumn(1.5f);  // Part Number
                    columns.ConstantColumn(40);    // Qty
                    columns.ConstantColumn(90);    // Status
                    columns.RelativeColumn(2);     // Notes
                });

                // Header
                table.Header(header =>
                {
                    var style = TextStyle.Default.Bold().FontColor(Colors.White).FontSize(10);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(6).Text("#").Style(style);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(6).Text("Description").Style(style);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(6).Text("Part #").Style(style);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(6).AlignCenter().Text("Qty").Style(style);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(6).Text("Status").Style(style);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(6).Text("Notes").Style(style);
                });

                // Rows
                for (int i = 0; i < request.Items.Count; i++)
                {
                    var item = request.Items[i];
                    var bgColor = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    var statusColor = item.Status switch
                    {
                        PartsRequestStatus.Needed => Colors.Red.Darken1,
                        PartsRequestStatus.Ordered => Colors.Orange.Darken1,
                        PartsRequestStatus.Backordered => Colors.Orange.Darken3,
                        PartsRequestStatus.Received => Colors.Green.Darken1,
                        _ => Colors.Black
                    };

                    table.Cell().Background(bgColor).Padding(4).Text($"{i + 1}");
                    table.Cell().Background(bgColor).Padding(4).Text(item.Description);
                    table.Cell().Background(bgColor).Padding(4).Text(item.PartNumber);
                    table.Cell().Background(bgColor).Padding(4).AlignCenter().Text($"{item.Quantity}");
                    table.Cell().Background(bgColor).Padding(4).Text(item.Status.ToString()).FontColor(statusColor).Bold();
                    table.Cell().Background(bgColor).Padding(4).Text(item.Notes);
                }

                if (request.Items.Count == 0)
                {
                    table.Cell().ColumnSpan(6).Padding(20).AlignCenter()
                        .Text("No parts listed").FontColor(Colors.Grey.Medium).Italic();
                }
            });
        }
    }
}
