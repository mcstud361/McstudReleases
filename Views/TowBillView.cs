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
/// Includes a mileage/rate/tax calculation panel below the template form.
/// </summary>
public class TowBillView : UserControl
{
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);

    private readonly TowBillService _towBillService = TowBillService.Instance;
    private TemplateFormBuilder? _formBuilder;
    private InfoBar? _infoBar;

    // Calculation fields
    private NumberBox? _milesBox;
    private NumberBox? _ratePerMileBox;
    private NumberBox? _taxPercentBox;
    private TextBlock? _mileageTotalText;
    private TextBlock? _subtotalText;
    private TextBlock? _taxText;
    private TextBlock? _totalText;

    public TowBillView()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        var settings = _towBillService.GetSettings();

        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // Form
                new RowDefinition { Height = GridLength.Auto }, // Calculation panel
                new RowDefinition { Height = GridLength.Auto }  // Footer totals
            }
        };

        // Template form builder
        _formBuilder = new TemplateFormBuilder(ShopDocType.TowBill);
        _formBuilder.ExportRequested += OnExportRequested;
        Grid.SetRow(_formBuilder, 0);
        mainGrid.Children.Add(_formBuilder);

        // Calculation panel — Miles, $/mile, Tax %
        var calcBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
            Padding = new Thickness(16, 12, 16, 12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var calcGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            }
        };

        // Left side: input fields
        var inputPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Miles
        inputPanel.Children.Add(new TextBlock
        {
            Text = "Miles",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center
        });
        _milesBox = new NumberBox
        {
            Value = 0,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 100,
            SmallChange = 1,
            LargeChange = 10
        };
        _milesBox.ValueChanged += (s, e) => RecalculateTotals();
        inputPanel.Children.Add(_milesBox);

        // $/mile
        inputPanel.Children.Add(new TextBlock
        {
            Text = "$ / mile",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center
        });
        _ratePerMileBox = new NumberBox
        {
            Value = (double)settings.DefaultMileageRate,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 100,
            SmallChange = 0.25,
            LargeChange = 1
        };
        _ratePerMileBox.ValueChanged += (s, e) =>
        {
            RecalculateTotals();
            SaveRateSettings();
        };
        inputPanel.Children.Add(_ratePerMileBox);

        // = mileage total
        inputPanel.Children.Add(new TextBlock
        {
            Text = "=",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
            VerticalAlignment = VerticalAlignment.Center
        });
        _mileageTotalText = new TextBlock
        {
            Text = "$0.00",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };
        inputPanel.Children.Add(_mileageTotalText);

        // Separator
        inputPanel.Children.Add(new Border
        {
            Width = 1,
            Height = 20,
            Background = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            Margin = new Thickness(4, 0, 4, 0)
        });

        // Tax %
        inputPanel.Children.Add(new TextBlock
        {
            Text = "Tax %",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center
        });
        _taxPercentBox = new NumberBox
        {
            Value = (double)settings.TaxRate,
            Minimum = 0,
            Maximum = 100,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 90,
            SmallChange = 0.25,
            LargeChange = 1
        };
        _taxPercentBox.ValueChanged += (s, e) =>
        {
            RecalculateTotals();
            SaveRateSettings();
        };
        inputPanel.Children.Add(_taxPercentBox);

        Grid.SetColumn(inputPanel, 0);
        calcGrid.Children.Add(inputPanel);

        calcBorder.Child = calcGrid;
        Grid.SetRow(calcBorder, 1);
        mainGrid.Children.Add(calcBorder);

        // Footer — totals
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

        _subtotalText = new TextBlock
        {
            Text = "Subtotal: $0.00",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center
        };
        totalsPanel.Children.Add(_subtotalText);

        _taxText = new TextBlock
        {
            Text = "",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center
        };
        totalsPanel.Children.Add(_taxText);

        _totalText = new TextBlock
        {
            Text = "Total: $0.00",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };
        totalsPanel.Children.Add(_totalText);

        // Live totals: update footer whenever charges change
        _formBuilder.ChargeTotalsChanged += (s, e) => RecalculateTotals();

        footer.Child = totalsPanel;
        Grid.SetRow(footer, 2);
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

    private void RecalculateTotals()
    {
        if (_formBuilder == null || _milesBox == null || _ratePerMileBox == null ||
            _taxPercentBox == null || _mileageTotalText == null ||
            _subtotalText == null || _taxText == null || _totalText == null)
            return;

        var miles = double.IsNaN(_milesBox.Value) ? 0 : (decimal)_milesBox.Value;
        var ratePerMile = double.IsNaN(_ratePerMileBox.Value) ? 0 : (decimal)_ratePerMileBox.Value;
        var taxPercent = double.IsNaN(_taxPercentBox.Value) ? 0 : (decimal)_taxPercentBox.Value;

        var mileageTotal = miles * ratePerMile;
        _mileageTotalText.Text = $"{mileageTotal:C2}";

        // Equipment subtotal from template charges (subtotal only, ignore template tax)
        var (equipmentSubtotal, _, _) = _formBuilder.GetLiveTotals();

        var subtotal = equipmentSubtotal + mileageTotal;
        var tax = taxPercent > 0 ? subtotal * (taxPercent / 100) : 0;
        var total = subtotal + tax;

        _subtotalText.Text = $"Subtotal: {subtotal:C2}";
        _taxText.Text = tax > 0 ? $"Tax: {tax:C2}" : "";
        _totalText.Text = $"Total: {total:C2}";
    }

    private void SaveRateSettings()
    {
        if (_ratePerMileBox == null || _taxPercentBox == null) return;

        var settings = _towBillService.GetSettings();
        settings.DefaultMileageRate = double.IsNaN(_ratePerMileBox.Value) ? 0 : (decimal)_ratePerMileBox.Value;
        settings.TaxRate = double.IsNaN(_taxPercentBox.Value) ? 0 : (decimal)_taxPercentBox.Value;
        _towBillService.SaveSettings(settings);
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
            var miles = _milesBox != null && !double.IsNaN(_milesBox.Value) ? (decimal)_milesBox.Value : 0;
            var ratePerMile = _ratePerMileBox != null && !double.IsNaN(_ratePerMileBox.Value) ? (decimal)_ratePerMileBox.Value : 0;
            var taxPercent = _taxPercentBox != null && !double.IsNaN(_taxPercentBox.Value) ? (decimal)_taxPercentBox.Value : 0;

            var billData = new TowBillData
            {
                ShopName = GetStringValue(data, "shopName"),
                RoNumber = GetStringValue(data, "roNumber"),
                VehicleYMM = GetStringValue(data, "vehicleYMM"),
                TowDate = data.ContainsKey("towDate") && data["towDate"] is DateTime dt ? dt : DateTime.Today,
                Notes = GetStringValue(data, "notes"),
                Miles = miles,
                RatePerMile = ratePerMile,
                TaxPercent = taxPercent
            };

            // Add charges from template (only checked equipment)
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

            // Generate PDF
            var pdfPath = _towBillService.GeneratePdf(billData);

            // Track usage
            var total = billData.ComputedTotal;
            DocumentUsageTrackingService.Instance.RecordTowBill(
                billData.RoNumber,
                total,
                billData.ShopName
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
            System.Diagnostics.Debug.WriteLine($"[TowBill] Export error: {ex}");
            ShowNotification($"Export failed: {ex.GetType().Name}: {ex.Message}", InfoBarSeverity.Error);
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
