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
        {(string.IsNullOrEmpty(roNumber) ? "<div class='ro-number'>RO # ________________</div>" : $"<div class='ro-number'>RO # {roNumber}</div>")}
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

            // Determine if we need compact mode (many sections = fit on one page)
            var sectionCount = checklist.Sections?.Count ?? 0;
            var totalItems = checklist.Sections?.Sum(s => s.Items?.Count ?? 0) ?? 0;
            var isCompact = sectionCount > 6 || totalItems > 30;

            // Store checked items for use in content generation
            _currentCheckedItems = checkedItems ?? new HashSet<string>();

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

            return tempPath;
        }

        // Store checked items during PDF generation
        private HashSet<string> _currentCheckedItems = new();

        private void ComposeHeader(IContainer container, Checklist checklist, string? roNumber, bool isCompact = false)
        {
            container.Column(column =>
            {
                if (isCompact)
                {
                    // Compact single-line header
                    column.Item().Background(Colors.Grey.Darken3).Padding(6).Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text(text =>
                        {
                            text.Span(checklist.ShopName ?? "Shop").FontSize(11).Bold().FontColor(Colors.White);
                            text.Span("  |  ").FontSize(9).FontColor(Colors.Grey.Lighten1);
                            text.Span(checklist.Title ?? "Checklist").FontSize(11).Bold().FontColor(Colors.White);
                        });

                        row.ConstantItem(150).AlignRight().Text(text =>
                        {
                            text.Span("RO# ").FontSize(9).FontColor(Colors.Grey.Lighten2);
                            text.Span(string.IsNullOrEmpty(roNumber) ? "_________" : roNumber)
                                .FontSize(9).Bold().FontColor(Colors.White);
                            text.Span("  ").FontSize(9);
                            text.Span(DateTime.Now.ToString("MM/dd/yy")).FontSize(8).FontColor(Colors.Grey.Lighten2);
                        });
                    });
                    column.Item().PaddingTop(4);
                }
                else
                {
                    // Top banner with shop name
                    column.Item().Background(Colors.Grey.Darken3).Padding(12).Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text(checklist.ShopName ?? "Shop Name")
                            .FontSize(16).Bold().FontColor(Colors.White);

                        row.RelativeItem().AlignRight().Text(DateTime.Now.ToString("MM/dd/yyyy"))
                            .FontSize(10).FontColor(Colors.Grey.Lighten2);
                    });

                    // Title and RO number
                    column.Item().Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text(checklist.Title ?? "Checklist")
                            .FontSize(14).Bold();

                        row.RelativeItem().AlignRight().Text(text =>
                        {
                            text.Span("RO # ").FontSize(11);
                            text.Span(string.IsNullOrEmpty(roNumber) ? "________________" : roNumber)
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
                        // Signature
                        row.RelativeItem().Row(sig =>
                        {
                            sig.ConstantItem(80).AlignBottom().BorderBottom(0.5f).BorderColor(Colors.Black);
                            sig.ConstantItem(3);
                            sig.ConstantItem(50).AlignBottom().Text("Technician").FontSize(6).FontColor(Colors.Grey.Darken1);
                        });

                        row.ConstantItem(15);

                        // Date
                        row.ConstantItem(70).Row(sig =>
                        {
                            sig.ConstantItem(40).AlignBottom().BorderBottom(0.5f).BorderColor(Colors.Black);
                            sig.ConstantItem(3);
                            sig.ConstantItem(25).AlignBottom().Text("Date").FontSize(6).FontColor(Colors.Grey.Darken1);
                        });

                        row.ConstantItem(15);

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
                            sig.Item().PaddingTop(2).Text("Technician Signature").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });

                        row.ConstantItem(30);

                        row.ConstantItem(100).Column(sig =>
                        {
                            sig.Item().Height(20).BorderBottom(1).BorderColor(Colors.Black);
                            sig.Item().PaddingTop(2).Text("Date").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });

                        row.ConstantItem(30);

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
            text += $"RO # ________________\n";
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
            text += "Technician: ________________  Date: ________\n";

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
