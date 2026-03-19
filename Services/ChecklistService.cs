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
    /// Service for managing shop checklists
    /// </summary>
    public class ChecklistService
    {
        private static ChecklistService? _instance;
        public static ChecklistService Instance => _instance ??= new ChecklistService();

        private List<Checklist> _checklists = new();
        private readonly string _checklistsFolder;

        /// <summary>
        /// Number of underscores for the RO# blank line on printed PDFs.
        /// </summary>
        public int RoLineLength { get; set; } = 30;

        private ChecklistService()
        {
            _checklistsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Checklists");
            LoadChecklists();
        }

        private void LoadChecklists()
        {
            try
            {
                if (!Directory.Exists(_checklistsFolder))
                {
                    System.Diagnostics.Debug.WriteLine($"[Checklist] Folder not found: {_checklistsFolder}");
                    return;
                }

                var files = Directory.GetFiles(_checklistsFolder, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var checklist = JsonSerializer.Deserialize<Checklist>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (checklist != null)
                        {
                            _checklists.Add(checklist);
                            System.Diagnostics.Debug.WriteLine($"[Checklist] Loaded: {checklist.Title}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Checklist] Error loading {file}: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[Checklist] Loaded {_checklists.Count} checklists");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Checklist] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all available checklists
        /// </summary>
        public List<Checklist> GetChecklists()
        {
            return _checklists.ToList();
        }

        /// <summary>
        /// Get a specific checklist by ID
        /// </summary>
        public Checklist? GetChecklist(string id)
        {
            return _checklists.FirstOrDefault(c =>
                c.Id?.Equals(id, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// Generate printable HTML for a checklist
        /// </summary>
        public string GeneratePrintableHtml(Checklist checklist, string? roNumber = null)
        {
            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>{checklist.Title}</title>
    <style>
        body {{
            font-family: Arial, sans-serif;
            font-size: 11pt;
            margin: 0.5in;
            color: #000;
        }}
        .header {{
            text-align: center;
            margin-bottom: 20px;
            border-bottom: 2px solid #000;
            padding-bottom: 10px;
        }}
        .header h1 {{
            margin: 0;
            font-size: 18pt;
        }}
        .header h2 {{
            margin: 5px 0 0 0;
            font-size: 14pt;
            font-weight: normal;
        }}
        .ro-number {{
            margin-top: 10px;
            font-size: 12pt;
        }}
        .section {{
            margin-bottom: 15px;
            break-inside: avoid;
        }}
        .section-title {{
            font-weight: bold;
            font-size: 12pt;
            background: #e0e0e0;
            padding: 5px 10px;
            margin-bottom: 5px;
            border-left: 4px solid #333;
        }}
        .items {{
            margin-left: 10px;
        }}
        .item {{
            display: flex;
            align-items: center;
            padding: 3px 0;
            border-bottom: 1px dotted #ccc;
        }}
        .checkbox {{
            width: 14px;
            height: 14px;
            border: 1.5px solid #000;
            margin-right: 10px;
            flex-shrink: 0;
        }}
        .item-text {{
            flex-grow: 1;
        }}
        .required {{
            color: #c00;
            font-size: 10pt;
            margin-left: 5px;
        }}
        .notes-line {{
            border-bottom: 1px solid #000;
            height: 20px;
            margin-left: 30px;
        }}
        .footer {{
            margin-top: 30px;
            border-top: 1px solid #000;
            padding-top: 15px;
        }}
        .signature-line {{
            display: flex;
            justify-content: space-between;
            margin-top: 20px;
        }}
        .signature-box {{
            width: 45%;
        }}
        .signature-box .line {{
            border-bottom: 1px solid #000;
            height: 30px;
        }}
        .signature-box .label {{
            font-size: 9pt;
            color: #666;
            margin-top: 3px;
        }}
        @media print {{
            body {{ margin: 0.3in; }}
            .section {{ break-inside: avoid; }}
        }}
    </style>
</head>
<body>
    <div class='header'>
        <h1>{checklist.ShopName ?? "Shop Name"}</h1>
        <h2>{checklist.Title}</h2>
        {(string.IsNullOrEmpty(roNumber) ? $"<div class='ro-number'>RO # {new string('_', RoLineLength)}</div>" : $"<div class='ro-number'>RO # {roNumber}</div>")}
    </div>
";

            foreach (var section in checklist.Sections ?? new List<ChecklistSection>())
            {
                html += $@"
    <div class='section'>
        <div class='section-title'>{section.Title}</div>
        <div class='items'>
";
                foreach (var item in section.Items ?? new List<ChecklistItem>())
                {
                    var requiredMark = item.Required ? "<span class='required'>*</span>" : "";
                    html += $@"
            <div class='item'>
                <div class='checkbox'></div>
                <span class='item-text'>{item.Text}{requiredMark}</span>
            </div>
";
                }
                html += @"
        </div>
    </div>
";
            }

            html += @"
    <div class='footer'>
        <div class='signature-line'>
            <div class='signature-box'>
                <div class='line'></div>
                <div class='label'>Technician Signature</div>
            </div>
            <div class='signature-box'>
                <div class='line'></div>
                <div class='label'>Date</div>
            </div>
        </div>
    </div>
</body>
</html>";

            return html;
        }

        /// <summary>
        /// Generate PDF for a checklist and return the file path
        /// </summary>
        public string GeneratePdf(Checklist checklist, string? roNumber = null, HashSet<string>? checkedItems = null)
        {
            // Set QuestPDF license (Community license for open source)
            QuestPDF.Settings.License = LicenseType.Community;

            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"checklist_{checklist.Id}_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            // Store checked items for use in content generation
            _currentCheckedItems = checkedItems ?? new HashSet<string>();

            if (checklist.Id == "in-process-quality-control")
            {
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.Letter);
                        page.Margin(0.3f, Unit.Inch);
                        page.DefaultTextStyle(x => x.FontSize(7).FontFamily("Arial"));
                        page.Content().Element(c => ComposeQCLayout(c, checklist, roNumber));
                    });
                }).GeneratePdf(tempPath);
            }
            else
            {
                // Determine if we need compact mode (many sections = fit on one page)
                var sectionCount = checklist.Sections?.Count ?? 0;
                var totalItems = checklist.Sections?.Sum(s => s.Items?.Count ?? 0) ?? 0;
                var isCompact = sectionCount > 6 || totalItems > 30;

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.Letter);
                        page.Margin(isCompact ? 0.25f : 0.4f, Unit.Inch);
                        page.DefaultTextStyle(x => x.FontSize(isCompact ? 8 : 10).FontFamily("Arial"));

                        page.Header().Element(c => ComposeHeader(c, checklist, roNumber, isCompact));
                        page.Content().Element(c => ComposeContent(c, checklist, isCompact));
                        page.Footer().Element(c => ComposeFooter(c, checklist, isCompact));
                    });
                }).GeneratePdf(tempPath);
            }

            return tempPath;
        }

        // Store checked items during PDF generation
        private HashSet<string> _currentCheckedItems = new();

        private void ComposeQCLayout(IContainer container, Checklist checklist, string? roNumber)
        {
            var sections = checklist.Sections ?? new List<ChecklistSection>();
            int SectionIndex(string id) => sections.FindIndex(s => s.Id == id);
            ChecklistSection? Section(string id) => sections.FirstOrDefault(s => s.Id == id);

            const float fontSize = 7f;
            const float headerFontSize = 8f;
            const float titleFontSize = 11f;

            container.Border(1).BorderColor(Colors.Black).Column(mainCol =>
            {
                // === TITLE ROW ===
                mainCol.Item().BorderBottom(1).BorderColor(Colors.Black)
                    .Background(Colors.Grey.Lighten3).Padding(6).AlignCenter()
                    .Text(text =>
                    {
                        text.Span(string.IsNullOrEmpty(checklist.ShopName) ? "Quality Control" : $"{checklist.ShopName} Quality Control")
                            .FontSize(titleFontSize).Bold();
                    });

                // === INFO ROW 1: RO#, Ins Co, Self-Pay ===
                mainCol.Item().BorderBottom(1).BorderColor(Colors.Black).Padding(4).Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("RO#: ").FontSize(fontSize).Bold();
                        text.Span(string.IsNullOrEmpty(roNumber) ? new string('_', RoLineLength) : roNumber).FontSize(fontSize);
                    });
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("Ins Co: ").FontSize(fontSize).Bold();
                        text.Span("____________").FontSize(fontSize);
                    });
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("Self-Pay: ").FontSize(fontSize).Bold();
                        text.Span("Y / N").FontSize(fontSize);
                    });
                });

                // === INFO ROW 2: DFR Date, Blueprinter, Supplement Date, Parts Ordered ===
                mainCol.Item().BorderBottom(1).BorderColor(Colors.Black).Padding(4).Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("DFR Date: ").FontSize(fontSize).Bold();
                        text.Span("________").FontSize(fontSize);
                    });
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("Blueprinter: ").FontSize(fontSize).Bold();
                        text.Span("________").FontSize(fontSize);
                    });
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("Supp Date: ").FontSize(fontSize).Bold();
                        text.Span("________").FontSize(fontSize);
                    });
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("Parts Ordered: ").FontSize(fontSize).Bold();
                        text.Span("________").FontSize(fontSize);
                    });
                });

                // === 4-COLUMN BODY ===
                var columnDefs = new (string Title, ChecklistSection? Section, int Index)[]
                {
                    ("Body/Structural", Section("body-structural"), SectionIndex("body-structural")),
                    ("Mechanical/Electrical", Section("mechanical-electrical"), SectionIndex("mechanical-electrical")),
                    ("Refinish", Section("refinish"), SectionIndex("refinish")),
                    ("Detail", Section("detail"), SectionIndex("detail"))
                };

                mainCol.Item().BorderBottom(1).BorderColor(Colors.Black).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn();
                        cols.RelativeColumn();
                        cols.RelativeColumn();
                        cols.RelativeColumn();
                    });

                    // Column headers
                    foreach (var colDef in columnDefs)
                    {
                        table.Cell()
                            .Border(0.5f).BorderColor(Colors.Black)
                            .Background(Colors.Grey.Lighten2)
                            .PaddingVertical(3).PaddingHorizontal(2)
                            .AlignCenter()
                            .Text(colDef.Title).FontSize(headerFontSize).Bold();
                    }

                    // Column items
                    foreach (var colDef in columnDefs)
                    {
                        table.Cell()
                            .BorderVertical(0.5f).BorderColor(Colors.Black)
                            .Padding(2).Column(col =>
                            {
                                var items = colDef.Section?.Items ?? new List<ChecklistItem>();
                                for (int i = 0; i < items.Count; i++)
                                {
                                    var item = items[i];
                                    var itemKey = $"{colDef.Index}_{i}";
                                    var isChecked = _currentCheckedItems.Contains(itemKey);

                                    col.Item().PaddingVertical(0.5f).Text(text =>
                                    {
                                        if (isChecked)
                                            text.Span("\u2713 ").FontSize(fontSize).Bold().FontColor(Colors.Green.Darken2);
                                        else
                                            text.Span("__ ").FontSize(6);
                                        text.Span(item.Text ?? "").FontSize(fontSize);
                                    });
                                }
                            });
                    }
                });

                // === QC INSPECTION ===
                mainCol.Item().BorderBottom(1).BorderColor(Colors.Black).Padding(4).Column(qcCol =>
                {
                    qcCol.Item().PaddingBottom(3).AlignCenter()
                        .Text("QC Inspection").FontSize(headerFontSize).Bold();

                    var qcItems = Section("qc-inspection")?.Items ?? new List<ChecklistItem>();
                    var qcIdx = SectionIndex("qc-inspection");
                    var depts = new[] { "Body/Structural", "Mech/Electrical", "Refinish", "Detail" };

                    qcCol.Item().Table(qcTable =>
                    {
                        qcTable.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(85);   // department name
                            cols.ConstantColumn(12);   // issue checkbox
                            cols.ConstantColumn(35);   // "Issues"
                            cols.RelativeColumn();     // "Describe: ___"
                            cols.ConstantColumn(12);   // corrected checkbox
                            cols.ConstantColumn(52);   // "Corrected"
                        });

                        for (int d = 0; d < depts.Length && d * 2 + 1 < qcItems.Count; d++)
                        {
                            var issueChecked = _currentCheckedItems.Contains($"{qcIdx}_{d * 2}");
                            var correctedChecked = _currentCheckedItems.Contains($"{qcIdx}_{d * 2 + 1}");
                            var dept = depts[d];
                            var ic = issueChecked;
                            var cc = correctedChecked;

                            qcTable.Cell().PaddingVertical(1).Text($"{dept}:").FontSize(fontSize).Bold();
                            qcTable.Cell().PaddingVertical(1).Text(text =>
                            {
                                if (ic) text.Span("\u2713").Bold().FontColor(Colors.Green.Darken2).FontSize(fontSize);
                                else text.Span("__").FontSize(6);
                            });
                            qcTable.Cell().PaddingVertical(1).Text(" Issues").FontSize(fontSize);
                            qcTable.Cell().PaddingVertical(1).Text("Describe: _______________").FontSize(fontSize);
                            qcTable.Cell().PaddingVertical(1).Text(text =>
                            {
                                if (cc) text.Span("\u2713").Bold().FontColor(Colors.Green.Darken2).FontSize(fontSize);
                                else text.Span("__").FontSize(6);
                            });
                            qcTable.Cell().PaddingVertical(1).Text(" Corrected").FontSize(fontSize);
                        }
                    });
                });

                // === ADAS ===
                mainCol.Item().BorderBottom(1).BorderColor(Colors.Black).Padding(4).Column(adasCol =>
                {
                    var adasItems = Section("adas")?.Items ?? new List<ChecklistItem>();
                    var adasIdx = SectionIndex("adas");

                    // Header: ADAS Equipped + ADAS Think
                    adasCol.Item().PaddingBottom(2).Text(text =>
                    {
                        text.Span("ADAS Systems").FontSize(headerFontSize).Bold();
                    });

                    adasCol.Item().PaddingBottom(2).Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("ADAS Equipped: ").FontSize(fontSize).Bold();
                            text.Span("Yes ___ No ___").FontSize(fontSize);
                        });
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("ADAS Think: ").FontSize(fontSize).Bold();
                            text.Span("____________").FontSize(fontSize);
                        });
                    });

                    // Calibration items in 4-column grid
                    // Row 1: Camera(Front)[2], Lane Departure[5], Camera(Rear)[3], Collision Avoid[7]
                    // Row 2: Blind Spot[4], LKA[6], Radar[8], All Calibrated[9]
                    var adasGridOrder = new[] { 2, 5, 3, 7, 4, 6, 8, 9 };

                    adasCol.Item().Table(gridTable =>
                    {
                        gridTable.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn();
                            cols.RelativeColumn();
                            cols.RelativeColumn();
                            cols.RelativeColumn();
                        });

                        foreach (var idx in adasGridOrder)
                        {
                            if (idx < adasItems.Count)
                            {
                                var itemKey = $"{adasIdx}_{idx}";
                                var isChecked = _currentCheckedItems.Contains(itemKey);
                                var itemText = (adasItems[idx].Text ?? "").Replace(" - Calibration Required", "");

                                gridTable.Cell().PaddingVertical(0.5f).Text(text =>
                                {
                                    if (isChecked)
                                        text.Span("\u2713 ").Bold().FontColor(Colors.Green.Darken2).FontSize(fontSize);
                                    else
                                        text.Span("__ ").FontSize(6);
                                    text.Span(itemText).FontSize(fontSize);
                                });
                            }
                        }
                    });
                });

                // === EV/HYBRID ===
                mainCol.Item().Padding(4).Column(evCol =>
                {
                    var evItems = Section("ev-hybrid")?.Items ?? new List<ChecklistItem>();
                    var evIdx = SectionIndex("ev-hybrid");

                    evCol.Item().PaddingBottom(2)
                        .Text(Section("ev-hybrid")?.Title ?? "EV/Hybrid")
                        .FontSize(headerFontSize).Bold();

                    // 3-column grid for EV items
                    evCol.Item().Table(gridTable =>
                    {
                        gridTable.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn();
                            cols.RelativeColumn();
                            cols.RelativeColumn();
                        });

                        for (int i = 0; i < evItems.Count; i++)
                        {
                            var itemKey = $"{evIdx}_{i}";
                            var isChecked = _currentCheckedItems.Contains(itemKey);
                            var itemText = evItems[i].Text ?? "";

                            gridTable.Cell().PaddingVertical(0.5f).Text(text =>
                            {
                                if (isChecked)
                                    text.Span("\u2713 ").Bold().FontColor(Colors.Green.Darken2).FontSize(fontSize);
                                else
                                    text.Span("__ ").FontSize(6);
                                text.Span(itemText).FontSize(fontSize);
                            });
                        }
                    });
                });

                // === VERSION ===
                mainCol.Item().PaddingHorizontal(4).PaddingBottom(2).AlignRight()
                    .Text($"v{checklist.Version ?? "1.0"}").FontSize(5).FontColor(Colors.Grey.Medium);
            });
        }

        private void ComposeHeader(IContainer container, Checklist checklist, string? roNumber, bool isCompact = false)
        {
            container.Column(column =>
            {
                if (isCompact)
                {
                    // Compact single-line header (white background for pen visibility)
                    column.Item().Background(Colors.White).BorderBottom(1).BorderColor(Colors.Grey.Medium).Padding(6).Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text(text =>
                        {
                            text.Span(checklist.ShopName ?? "Shop").FontSize(11).Bold().FontColor(Colors.Black);
                            text.Span("  |  ").FontSize(9).FontColor(Colors.Grey.Medium);
                            text.Span(checklist.Title ?? "Checklist").FontSize(11).Bold().FontColor(Colors.Black);
                        });

                        row.ConstantItem(150).AlignRight().Text(text =>
                        {
                            text.Span("RO# ").FontSize(9).FontColor(Colors.Grey.Darken2);
                            text.Span(string.IsNullOrEmpty(roNumber) ? new string('_', RoLineLength) : roNumber)
                                .FontSize(9).Bold().FontColor(Colors.Black);
                        });
                    });
                    column.Item().PaddingTop(4);
                }
                else
                {
                    // Top banner with shop name (white background for pen visibility)
                    column.Item().Background(Colors.White).BorderBottom(1).BorderColor(Colors.Grey.Medium).Padding(12).Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text(checklist.ShopName ?? "Shop Name")
                            .FontSize(16).Bold().FontColor(Colors.Black);
                    });

                    // Title and RO number
                    column.Item().Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
                    {
                        row.ConstantItem(200).AlignLeft().Text(checklist.Title ?? "Checklist")
                            .FontSize(14).Bold();

                        row.RelativeItem().AlignRight().Text(text =>
                        {
                            text.Span("RO # ").FontSize(11);
                            text.Span(string.IsNullOrEmpty(roNumber) ? new string('_', RoLineLength) : roNumber)
                                .FontSize(11).Bold().Underline();
                        });
                    });

                    column.Item().PaddingTop(8);
                }
            });
        }

        private void ComposeContent(IContainer container, Checklist checklist, bool isCompact = false)
        {
            var sections = checklist.Sections ?? new List<ChecklistSection>();

            container.Column(mainColumn =>
            {
                if (isCompact && sections.Count > 6)
                {
                    // 3-column layout for compact mode with many sections
                    var colSize = (sections.Count + 2) / 3;
                    var col1 = sections.Take(colSize).Select((s, i) => (s, i)).ToList();
                    var col2 = sections.Skip(colSize).Take(colSize).Select((s, i) => (s, i + colSize)).ToList();
                    var col3 = sections.Skip(colSize * 2).Select((s, i) => (s, i + colSize * 2)).ToList();

                    mainColumn.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            foreach (var (section, idx) in col1)
                            {
                                var sectionIdx = idx;
                                c.Item().Element(x => ComposeSection(x, section, isCompact, sectionIdx));
                                c.Item().PaddingBottom(3);
                            }
                        });

                        row.ConstantItem(6);

                        row.RelativeItem().Column(c =>
                        {
                            foreach (var (section, idx) in col2)
                            {
                                var sectionIdx = idx;
                                c.Item().Element(x => ComposeSection(x, section, isCompact, sectionIdx));
                                c.Item().PaddingBottom(3);
                            }
                        });

                        row.ConstantItem(6);

                        row.RelativeItem().Column(c =>
                        {
                            foreach (var (section, idx) in col3)
                            {
                                var sectionIdx = idx;
                                c.Item().Element(x => ComposeSection(x, section, isCompact, sectionIdx));
                                c.Item().PaddingBottom(3);
                            }
                        });
                    });
                }
                else if (sections.Count > 4)
                {
                    // Two-column layout
                    var midPoint = (sections.Count + 1) / 2;
                    var leftSections = sections.Take(midPoint).Select((s, i) => (s, i)).ToList();
                    var rightSections = sections.Skip(midPoint).Select((s, i) => (s, i + midPoint)).ToList();

                    mainColumn.Item().Row(row =>
                    {
                        row.RelativeItem().Column(leftCol =>
                        {
                            foreach (var (section, idx) in leftSections)
                            {
                                var sectionIdx = idx;
                                leftCol.Item().Element(c => ComposeSection(c, section, isCompact, sectionIdx));
                                leftCol.Item().PaddingBottom(isCompact ? 3 : 6);
                            }
                        });

                        row.ConstantItem(isCompact ? 6 : 12);

                        row.RelativeItem().Column(rightCol =>
                        {
                            foreach (var (section, idx) in rightSections)
                            {
                                var sectionIdx = idx;
                                rightCol.Item().Element(c => ComposeSection(c, section, isCompact, sectionIdx));
                                rightCol.Item().PaddingBottom(isCompact ? 3 : 6);
                            }
                        });
                    });
                }
                else
                {
                    // Single column for fewer sections
                    for (int i = 0; i < sections.Count; i++)
                    {
                        var sectionIdx = i;
                        mainColumn.Item().Element(c => ComposeSection(c, sections[sectionIdx], isCompact, sectionIdx));
                        mainColumn.Item().PaddingBottom(isCompact ? 3 : 6);
                    }
                }
            });
        }

        private void ComposeSection(IContainer container, ChecklistSection section, bool isCompact = false, int sectionIndex = 0)
        {
            var headerPadding = isCompact ? 3 : 6;
            var itemPadding = isCompact ? 2 : 4;
            var headerFontSize = isCompact ? 7 : 10;
            var itemFontSize = isCompact ? 6.5f : 9;
            var checkboxSize = isCompact ? 8 : 12;

            container.Border(isCompact ? 0.5f : 1).BorderColor(Colors.Grey.Lighten1).Column(column =>
            {
                // Section header with icon-like styling
                column.Item().Background(Colors.Blue.Darken2).Padding(headerPadding).Row(headerRow =>
                {
                    headerRow.RelativeItem().Text(section.Title ?? "Section")
                        .FontSize(headerFontSize).Bold().FontColor(Colors.White);
                });

                // Items
                column.Item().Padding(isCompact ? 2 : 4).Column(items =>
                {
                    var itemList = section.Items ?? new List<ChecklistItem>();
                    for (int i = 0; i < itemList.Count; i++)
                    {
                        var item = itemList[i];
                        var bgColor = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                        var itemKey = $"{sectionIndex}_{i}";
                        var isChecked = _currentCheckedItems.Contains(itemKey);

                        items.Item().Background(bgColor).Padding(itemPadding).Row(row =>
                        {
                            // Checkbox with checkmark if checked
                            if (isChecked)
                            {
                                row.ConstantItem(checkboxSize + 2).Height(checkboxSize).Width(checkboxSize)
                                    .Border(isCompact ? 1f : 1.5f).BorderColor(Colors.Green.Darken2)
                                    .Background(Colors.Green.Lighten4)
                                    .AlignCenter().AlignMiddle()
                                    .Text("✓").FontSize(isCompact ? 6 : 9).Bold().FontColor(Colors.Green.Darken3);
                            }
                            else
                            {
                                row.ConstantItem(checkboxSize + 2).Height(checkboxSize).Width(checkboxSize)
                                    .Border(isCompact ? 1f : 1.5f).BorderColor(Colors.Grey.Darken1);
                            }

                            row.ConstantItem(isCompact ? 3 : 6); // Spacing

                            // Item text (strikethrough if checked)
                            row.RelativeItem().AlignMiddle().Text(text =>
                            {
                                if (isChecked)
                                {
                                    text.Span(item.Text ?? "").FontSize(itemFontSize).FontColor(Colors.Grey.Darken1);
                                }
                                else
                                {
                                    text.Span(item.Text ?? "").FontSize(itemFontSize);
                                }
                                if (item.Required)
                                {
                                    text.Span(" *").FontSize(itemFontSize).FontColor(Colors.Red.Medium).Bold();
                                }
                            });
                        });
                    }
                });
            });
        }

        private void ComposeFooter(IContainer container, Checklist checklist, bool isCompact = false)
        {
            if (isCompact)
            {
                // Minimal footer for compact mode - just legend and signature line
                container.Column(column =>
                {
                    column.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingTop(4);

                    column.Item().Row(row =>
                    {
                        // Legend
                        row.RelativeItem().AlignRight().Text(text =>
                        {
                            text.Span("* ").FontSize(6).FontColor(Colors.Red.Medium).Bold();
                            text.Span("Required").FontSize(6).FontColor(Colors.Grey.Darken1);
                            text.Span("  v").FontSize(5).FontColor(Colors.Grey.Medium);
                            text.Span(checklist.Version ?? "1.0").FontSize(5).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            }
            else
            {
                container.Column(column =>
                {
                    column.Item().PaddingTop(12).BorderTop(2).BorderColor(Colors.Grey.Darken3).PaddingTop(12);

                    // Notes section
                    column.Item().Text("Notes:").FontSize(9).Bold();
                    column.Item().PaddingTop(4).Height(40).Border(1).BorderColor(Colors.Grey.Lighten1);

                    column.Item().PaddingTop(12);

                    // Signature area
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(sig =>
                        {
                            sig.Item().Height(20).BorderBottom(1).BorderColor(Colors.Black);
                            sig.Item().PaddingTop(2).Text("Verified By").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    // Legend
                    column.Item().PaddingTop(10).Row(legend =>
                    {
                        legend.RelativeItem().Text(text =>
                        {
                            text.Span("* ").FontColor(Colors.Red.Medium).Bold();
                            text.Span("= Required Item").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });

                        legend.RelativeItem().AlignRight().Text($"v{checklist.Version ?? "1.0"}")
                            .FontSize(7).FontColor(Colors.Grey.Medium);
                    });
                });
            }
        }

        /// <summary>
        /// Generate plain text version for clipboard
        /// </summary>
        public string GeneratePlainText(Checklist checklist)
        {
            var text = $"{checklist.ShopName ?? "Shop"}\n";
            text += $"{checklist.Title}\n";
            text += $"RO # {new string('_', RoLineLength)}\n";
            text += new string('=', 50) + "\n\n";

            foreach (var section in checklist.Sections ?? new List<ChecklistSection>())
            {
                text += $"[ {section.Title} ]\n";
                foreach (var item in section.Items ?? new List<ChecklistItem>())
                {
                    var req = item.Required ? " *" : "";
                    text += $"  [ ] {item.Text}{req}\n";
                }
                text += "\n";
            }

            text += new string('=', 50) + "\n";
            return text;
        }
    }

    #region Data Models

    public class Checklist
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? ShopName { get; set; }
        public List<ChecklistSection>? Sections { get; set; }
    }

    public class ChecklistSection
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Icon { get; set; }
        public List<ChecklistItem>? Items { get; set; }
    }

    public class ChecklistItem
    {
        public string? Id { get; set; }
        public string? Text { get; set; }
        public bool Required { get; set; }
    }

    #endregion
}
