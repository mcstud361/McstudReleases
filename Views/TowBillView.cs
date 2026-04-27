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
/// Tow Bill view with template-based customization.
/// - Original templates are read-only
/// - Users can "Make a Copy" to create editable versions
/// - Edit mode allows changing labels, adding/removing fields
/// - All templates can be filled out and exported to PDF
/// </summary>
public class TowBillView : UserControl
{
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);

    private readonly TowBillService _towBillService = TowBillService.Instance;
    private TemplateFormBuilder? _formBuilder;
    private InfoBar? _infoBar;

    public TowBillView()
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
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // Form
                new RowDefinition { Height = GridLength.Auto } // Footer buttons
            }
        };

        // Template form builder
        _formBuilder = new TemplateFormBuilder(ShopDocType.TowBill);
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

        var totalsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 24,
            VerticalAlignment = VerticalAlignment.Center
        };

        var subtotalText = new TextBlock
        {
            Text = "Subtotal: $0.00",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center
        };
        totalsPanel.Children.Add(subtotalText);

        var totalText = new TextBlock
        {
            Text = "Total: $0.00",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };
        totalsPanel.Children.Add(totalText);

        // Live totals: update footer whenever charges change
        _formBuilder.ChargeTotalsChanged += (s, e) =>
        {
            var (subtotal, tax, total) = _formBuilder.GetLiveTotals();
            subtotalText.Text = $"Subtotal: {subtotal:C2}";
            totalText.Text = tax > 0
                ? $"Total: {(subtotal + tax):C2}  (tax: {tax:C2})"
                : $"Total: {total:C2}";
        };

        footer.Child = totalsPanel;
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

            // Build TowBillData from form data
            var billData = new TowBillData
            {
                InvoiceNumber = GetStringValue(data, "invoiceNumber"),
                TowDate = data.ContainsKey("towDate") && data["towDate"] is DateTime dt ? dt : DateTime.Today,
                VehicleYMM = GetStringValue(data, "vehicleYMM"),
                VehicleColor = GetStringValue(data, "vehicleColor"),
                VehicleVin = GetStringValue(data, "vehicleVin"),
                LicensePlate = GetStringValue(data, "licensePlate"),
                RoNumber = GetStringValue(data, "roNumber"),
                CustomerName = GetStringValue(data, "customerName"),
                CustomerAddress = GetStringValue(data, "customerAddress"),
                CustomerCityStateZip = GetStringValue(data, "customerCityStateZip"),
                CustomerPhone = GetStringValue(data, "customerPhone"),
                InsuranceCompany = GetStringValue(data, "insuranceCompany"),
                ClaimNumber = GetStringValue(data, "claimNumber"),
                PickupLocation = GetStringValue(data, "pickupLocation"),
                DeliveryLocation = GetStringValue(data, "deliveryLocation"),
                TowMileage = data.ContainsKey("mileage") ? Convert.ToInt32(data["mileage"]) : 0,
                Notes = GetStringValue(data, "notes")
            };

            // Add charges from template (supports new ChargeExportItem format)
            if (data.ContainsKey("charges") && data["charges"] is List<ChargeExportItem> charges)
            {
                foreach (var charge in charges)
                {
                    billData.Charges.Add(new TowBillCharge
                    {
                        Description = charge.Name,
                        Amount = charge.Amount
                    });
                }
            }

            // Tax
            if (data.ContainsKey("tax"))
            {
                billData.TaxAmount = Convert.ToDecimal(data["tax"]);
            }

            // Generate PDF
            var pdfPath = _towBillService.GeneratePdf(billData);

            // Track usage
            var total = data.ContainsKey("total") ? Convert.ToDecimal(data["total"]) : 0;
            DocumentUsageTrackingService.Instance.RecordTowBill(
                billData.InvoiceNumber,
                total,
                billData.CustomerName
            );
            DocumentUsageTrackingService.Instance.RecordPdfExport("TowBill", Path.GetFileName(pdfPath), 1);

            // Open PDF
            Process.Start(new ProcessStartInfo
            {
                FileName = pdfPath,
                UseShellExecute = true
            });

            ShowNotification("Tow Bill exported successfully!", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowNotification($"Export failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private string GetStringValue(Dictionary<string, object> data, string key)
    {
        if (data.ContainsKey(key))
        {
            return data[key]?.ToString() ?? "";
        }
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
