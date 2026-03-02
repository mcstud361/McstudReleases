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
    private Button? _exportButton;
    private Button? _clearButton;
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
        Grid.SetRow(_formBuilder, 0);
        mainGrid.Children.Add(_formBuilder);

        // Footer with export/clear buttons
        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var footerContent = new Grid();
        footerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Totals display
        var totalsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 24,
            VerticalAlignment = VerticalAlignment.Center
        };

        var totalText = new TextBlock
        {
            Text = "Total: $0.00",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };
        totalsPanel.Children.Add(totalText);

        Grid.SetColumn(totalsPanel, 0);
        footerContent.Children.Add(totalsPanel);

        // Clear button
        _clearButton = new Button
        {
            Content = "Clear Form",
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 12, 0)
        };
        _clearButton.Click += (s, e) => _formBuilder?.ClearForm();
        Grid.SetColumn(_clearButton, 1);
        footerContent.Children.Add(_clearButton);

        // Export button
        var exportContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        exportContent.Children.Add(new FontIcon { Glyph = "\uE749", FontSize = 16 });
        exportContent.Children.Add(new TextBlock { Text = "Export to PDF", VerticalAlignment = VerticalAlignment.Center });

        _exportButton = new Button
        {
            Content = exportContent,
            Padding = new Thickness(20, 10, 20, 10),
            Background = new SolidColorBrush(AccentGreen),
            Foreground = new SolidColorBrush(Colors.White)
        };
        _exportButton.Click += OnExportClick;
        Grid.SetColumn(_exportButton, 2);
        footerContent.Children.Add(_exportButton);

        footer.Child = footerContent;
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

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_formBuilder?.CurrentTemplate == null)
        {
            ShowNotification("Please select a template first", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var data = _formBuilder.GetAllData();

            // Build PDF data from form
            var pdfData = new ColorTintInvoicePdfData
            {
                InvoiceNumber = GetStringValue(data, "invoiceNumber"),
                Date = DateTime.Now,
                BillTo = GetStringValue(data, "customerName"),
                JobNumber = GetStringValue(data, "roNumber"),
                Vehicle = GetStringValue(data, "vehicleYMM")
            };

            // Add items from charges if available
            if (data.ContainsKey("charges") && data["charges"] is List<(string Name, decimal Amount)> charges)
            {
                foreach (var charge in charges)
                {
                    pdfData.Items.Add(new ColorTintInvoicePdfItem
                    {
                        Description = charge.Name,
                        UnitPrice = charge.Amount,
                        Quantity = 1,
                        LineTotal = charge.Amount
                    });
                }
            }

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
