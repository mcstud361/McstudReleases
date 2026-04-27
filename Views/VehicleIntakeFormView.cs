#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using McStudDesktop.Services;
using QuestPDF.Fluent;

namespace McStudDesktop.Views;

public class VehicleIntakeFormView : UserControl
{
    private static readonly Windows.UI.Color AccentGreen = Windows.UI.Color.FromArgb(255, 0, 180, 80);

    private TemplateFormBuilder? _formBuilder;
    private InfoBar? _infoBar;

    public VehicleIntakeFormView()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 30)),
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        // Template form builder
        _formBuilder = new TemplateFormBuilder(ShopDocType.VehicleIntakeForm);
        _formBuilder.ExportRequested += OnExportRequested;
        Grid.SetRow(_formBuilder, 0);
        mainGrid.Children.Add(_formBuilder);

        // Footer — status label only (action buttons are in TemplateFormBuilder header)
        var footer = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 40, 40)),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var statusText = new TextBlock
        {
            Text = "Vehicle Check-In Report",
            FontSize = 14,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center
        };

        footer.Child = statusText;
        Grid.SetRow(footer, 1);
        mainGrid.Children.Add(footer);

        // InfoBar for notifications
        _infoBar = new InfoBar
        {
            IsOpen = false,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 60, 0, 0)
        };
        mainGrid.Children.Add(_infoBar);

        Content = mainGrid;
    }

    private void OnExportRequested(object? sender, Dictionary<string, object> data)
    {
        if (_formBuilder?.CurrentTemplate == null)
        {
            ShowNotification("Please select a template first", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var template = _formBuilder.CurrentTemplate;
            var pdfPath = GeneratePdf(data, template);

            // Track usage
            DocumentUsageTrackingService.Instance.RecordPdfExport("VehicleIntakeForm", Path.GetFileName(pdfPath), 1);

            // Open PDF
            Process.Start(new ProcessStartInfo
            {
                FileName = pdfPath,
                UseShellExecute = true
            });

            ShowNotification("Vehicle Check-In Report exported!", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowNotification($"Export failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    #region PDF Generation

    private string GeneratePdf(Dictionary<string, object> data, ShopDocTemplate template)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"vehicle_checkin_{DateTime.Now:yyyyMMddHHmmss}.pdf");

        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(QuestPDF.Helpers.PageSizes.Letter);
                page.Margin(0.5f, QuestPDF.Infrastructure.Unit.Inch);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                page.Header().Element(c => ComposeHeader(c, data));
                page.Content().Element(c => ComposeContent(c, data, template));
                page.Footer().Element(c => ComposeFooter(c, data));
            });
        }).GeneratePdf(tempPath);

        return tempPath;
    }

    private void ComposeHeader(QuestPDF.Infrastructure.IContainer container, Dictionary<string, object> data)
    {
        container.Column(column =>
        {
            column.Item().Background(QuestPDF.Helpers.Colors.Grey.Darken3).Padding(12).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("VEHICLE CHECK-IN REPORT").FontSize(20).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                });

                row.ConstantItem(200).AlignRight().Column(col =>
                {
                    var dateStr = GetString(data, "date");
                    if (!string.IsNullOrEmpty(dateStr))
                        col.Item().Text($"Date: {dateStr}").FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Lighten2);

                    var writtenBy = GetString(data, "writtenBy");
                    if (!string.IsNullOrEmpty(writtenBy))
                        col.Item().Text($"Written By: {writtenBy}").FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Lighten2);
                });
            });

            column.Item().PaddingVertical(4);
        });
    }

    private void ComposeContent(QuestPDF.Infrastructure.IContainer container, Dictionary<string, object> data, ShopDocTemplate template)
    {
        container.Column(column =>
        {
            // Customer & Vehicle Info side by side
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => ComposeInfoSection(c, "Customer Information", new[]
                {
                    ("Name", GetString(data, "customerName")),
                    ("Address", GetString(data, "address")),
                    ("City/State/ZIP", GetString(data, "city")),
                    ("Business Phone", GetString(data, "businessPhone")),
                    ("Home Phone", GetString(data, "homePhone"))
                }));

                row.ConstantItem(16);

                row.RelativeItem().Element(c => ComposeInfoSection(c, "Vehicle Information", new[]
                {
                    ("Year", GetString(data, "year")),
                    ("Make", GetString(data, "make")),
                    ("Model", GetString(data, "model")),
                    ("Color", GetString(data, "color")),
                    ("Trim", GetString(data, "trim")),
                    ("VIN", GetString(data, "vin")),
                    ("Odometer In", GetString(data, "odometerIn")),
                    ("Odometer Out", GetString(data, "odometerOut"))
                }));
            });

            column.Item().PaddingVertical(4);

            // Insurance & Schedule side by side
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => ComposeInfoSection(c, "Insurance Information", new[]
                {
                    ("Insurance Co.", GetString(data, "insuranceCompany")),
                    ("Ins. Phone", GetString(data, "insurancePhone")),
                    ("Claim #", GetString(data, "claimNumber")),
                    ("Adjuster", GetString(data, "adjuster"))
                }));

                row.ConstantItem(16);

                row.RelativeItem().Element(c => ComposeInfoSection(c, "Schedule", new[]
                {
                    ("Received", GetString(data, "received")),
                    ("Promised", GetString(data, "promised"))
                }));
            });

            column.Item().PaddingVertical(6);

            // Checklist sections
            var checklistSections = new[] { "interiorCondition", "miscEquipment", "paintCondition" };
            foreach (var section in template.Sections.Where(s => checklistSections.Contains(s.Id)))
            {
                column.Item().Element(c => ComposeChecklistSection(c, section, data));
                column.Item().PaddingVertical(3);
            }

            // Exterior notes
            var extNotes = GetString(data, "exteriorNotes");
            if (!string.IsNullOrEmpty(extNotes))
            {
                column.Item().Element(c => ComposeNotesSection(c, "Exterior Condition Notes", extNotes));
                column.Item().PaddingVertical(3);
            }

            // Additional notes
            var notes = GetString(data, "notes");
            if (!string.IsNullOrEmpty(notes))
            {
                column.Item().Element(c => ComposeNotesSection(c, "Additional Notes", notes));
                column.Item().PaddingVertical(3);
            }
        });
    }

    private void ComposeInfoSection(QuestPDF.Infrastructure.IContainer container, string title, (string Label, string Value)[] fields)
    {
        container.Border(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten1).Column(column =>
        {
            column.Item().Background(QuestPDF.Helpers.Colors.Grey.Darken2).Padding(6)
                .Text(title).FontSize(11).Bold().FontColor(QuestPDF.Helpers.Colors.White);

            foreach (var (label, value) in fields)
            {
                column.Item().BorderBottom(0.25f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2)
                    .Padding(4).Row(row =>
                    {
                        row.ConstantItem(110).Text(label + ":").FontSize(9).Bold();
                        row.RelativeItem().Text(string.IsNullOrEmpty(value) ? " " : value).FontSize(9);
                    });
            }
        });
    }

    private void ComposeChecklistSection(QuestPDF.Infrastructure.IContainer container, TemplateSection section, Dictionary<string, object> data)
    {
        container.Border(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten1).Column(column =>
        {
            column.Item().Background(QuestPDF.Helpers.Colors.Grey.Darken2).Padding(6)
                .Text(section.Title).FontSize(11).Bold().FontColor(QuestPDF.Helpers.Colors.White);

            // Render checkbox fields in a multi-column grid
            var checkboxFields = section.Fields.Where(f => f.FieldType == FieldType.Checkbox).ToList();
            var otherFields = section.Fields.Where(f => f.FieldType != FieldType.Checkbox).ToList();

            // Checkboxes in 3-column layout
            if (checkboxFields.Count > 0)
            {
                var rows = (int)Math.Ceiling(checkboxFields.Count / 3.0);
                for (int r = 0; r < rows; r++)
                {
                    column.Item().BorderBottom(0.25f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2)
                        .Padding(3).Row(row =>
                        {
                            for (int c = 0; c < 3; c++)
                            {
                                var idx = r * 3 + c;
                                if (idx < checkboxFields.Count)
                                {
                                    var field = checkboxFields[idx];
                                    var isChecked = GetString(data, field.Id) == "true";
                                    row.RelativeItem().Row(inner =>
                                    {
                                        inner.ConstantItem(14).Text(isChecked ? "\u2611" : "\u2610").FontSize(11);
                                        inner.RelativeItem().Text(field.Label).FontSize(9);
                                    });
                                }
                                else
                                {
                                    row.RelativeItem();
                                }
                            }
                        });
                }
            }

            // Other fields (like alarm code, floormat count)
            foreach (var field in otherFields)
            {
                var value = GetString(data, field.Id);
                if (!string.IsNullOrEmpty(value) && value != "0")
                {
                    column.Item().BorderBottom(0.25f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2)
                        .Padding(4).Row(row =>
                        {
                            row.ConstantItem(150).Text(field.Label + ":").FontSize(9).Bold();
                            row.RelativeItem().Text(value).FontSize(9);
                        });
                }
            }
        });
    }

    private void ComposeNotesSection(QuestPDF.Infrastructure.IContainer container, string title, string notes)
    {
        container.Border(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten1).Column(column =>
        {
            column.Item().Background(QuestPDF.Helpers.Colors.Grey.Darken2).Padding(6)
                .Text(title).FontSize(11).Bold().FontColor(QuestPDF.Helpers.Colors.White);
            column.Item().Padding(8).Text(notes).FontSize(9);
        });
    }

    private void ComposeFooter(QuestPDF.Infrastructure.IContainer container, Dictionary<string, object> data)
    {
        container.Column(column =>
        {
            column.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    var inspBy = GetString(data, "inspectedBy");
                    var inspDate = GetString(data, "inspectedDate");
                    col.Item().Text($"Inspected By: {inspBy}   Date: {inspDate}").FontSize(9);
                    col.Item().PaddingTop(16).LineHorizontal(0.5f).LineColor(QuestPDF.Helpers.Colors.Grey.Medium);
                });

                row.ConstantItem(40);

                row.RelativeItem().Column(col =>
                {
                    var custSig = GetString(data, "customerSignature");
                    var custDate = GetString(data, "customerSignDate");
                    col.Item().Text($"Customer: {custSig}   Date: {custDate}").FontSize(9);
                    col.Item().PaddingTop(16).LineHorizontal(0.5f).LineColor(QuestPDF.Helpers.Colors.Grey.Medium);
                });
            });

            column.Item().PaddingTop(8).AlignCenter()
                .Text("Customer acknowledges this Vehicle Check-In Report as a true representation of the vehicle's current physical condition.")
                .FontSize(8).Italic().FontColor(QuestPDF.Helpers.Colors.Grey.Medium);
        });
    }

    #endregion

    private string GetString(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var val))
            return val?.ToString() ?? "";
        return "";
    }

    private void ShowNotification(string message, InfoBarSeverity severity)
    {
        if (_infoBar == null) return;

        _infoBar.Message = message;
        _infoBar.Severity = severity;
        _infoBar.IsOpen = true;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            _infoBar.IsOpen = false;
        };
        timer.Start();
    }
}
