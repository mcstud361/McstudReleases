#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views;

/// <summary>
/// Color Tint Invoice view with template-based customization.
/// - Original templates are read-only
/// - Users can "Make a Copy" to create editable versions
/// - Edit mode allows changing labels, adding/removing fields
/// </summary>
public sealed class ColorTintInvoiceView : UserControl
{
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);

    private readonly ColorTintService _tintService = ColorTintService.Instance;
    private TemplateFormBuilder? _formBuilder;
    private InfoBar? _infoBar;

    public ColorTintInvoiceView()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        // Template form builder
        _formBuilder = new TemplateFormBuilder(ShopDocType.ColorTintInvoice);
        _formBuilder.ExportRequested += OnExportRequested;
        Grid.SetRow(_formBuilder, 0);
        mainGrid.Children.Add(_formBuilder);

        // Footer — totals only (action buttons are in TemplateFormBuilder header)
        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var totalText = new TextBlock
        {
            Text = "Total: $0.00",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };

        footer.Child = totalText;
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

            // Build PDF data from form
            var pdfData = new ColorTintInvoicePdfData
            {
                InvoiceNumber = GetStringValue(data, "invoiceNumber"),
                Date = DateTime.Now,
                BillTo = GetStringValue(data, "customerName"),
                JobNumber = GetStringValue(data, "roNumber"),
                Vehicle = GetStringValue(data, "vehicleYMM")
            };

            // Add items from charges (supports new ChargeExportItem format)
            var showCost = data.ContainsKey("showCostColumn") && (bool)data["showCostColumn"];
            var showList = data.ContainsKey("showListPriceColumn") && (bool)data["showListPriceColumn"];
            pdfData.ShowCostColumn = showCost;
            pdfData.ShowListPriceColumn = showList;

            if (data.ContainsKey("charges") && data["charges"] is List<ChargeExportItem> charges)
            {
                foreach (var charge in charges)
                {
                    pdfData.Items.Add(new ColorTintInvoicePdfItem
                    {
                        Description = charge.Name,
                        UnitPrice = charge.UnitAmount,
                        Quantity = (int)charge.Quantity,
                        LineTotal = charge.Amount,
                        CostPrice = charge.UnitCostPrice,
                        ListPrice = charge.UnitListPrice,
                        PartNumber = charge.PartNumber
                    });
                }
            }

            if (data.ContainsKey("costSubtotal"))
                pdfData.CostSubtotal = Convert.ToDecimal(data["costSubtotal"]);
            if (data.ContainsKey("listSubtotal"))
                pdfData.ListSubtotal = Convert.ToDecimal(data["listSubtotal"]);

            pdfData.Total = data.ContainsKey("total") ? Convert.ToDecimal(data["total"]) : 0;
            pdfData.Subtotal = pdfData.Total;

            // Generate PDF using ShopDocsPdfService
            var pdfPath = ShopDocsPdfService.Instance.GenerateColorTintInvoicePdf(pdfData);

            // Track usage
            DocumentUsageTrackingService.Instance.RecordPdfExport("ColorTintInvoice", Path.GetFileName(pdfPath), 1);

            // Open PDF
            Process.Start(new ProcessStartInfo
            {
                FileName = pdfPath,
                UseShellExecute = true
            });

            ShowNotification("Color Tint Invoice exported successfully!", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowNotification($"Export failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private string GetStringValue(Dictionary<string, object> data, string key)
    {
        if (data.ContainsKey(key))
            return data[key]?.ToString() ?? "";
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
