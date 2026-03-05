#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views;

/// <summary>
/// Interactive vehicle protection pricing view.
/// Users pick a service type (PPF/Vinyl/Ceramic), select a vehicle style,
/// click panels on a visual diagram, and see a live-priced panel list with totals.
/// </summary>
public class PPFPricingView : UserControl
{
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);
    private static readonly Color DarkBg = Color.FromArgb(255, 30, 30, 30);
    private static readonly Color MedBg = Color.FromArgb(255, 40, 40, 40);
    private static readonly Color BorderColor = Color.FromArgb(255, 60, 60, 60);
    private static readonly Color DimText = Color.FromArgb(255, 150, 150, 150);

    private readonly PPFPricingService _ppfService = PPFPricingService.Instance;

    // State
    private string _activeServiceType = "ppf";
    private VehicleStyle? _activeVehicleStyle;
    private readonly Dictionary<string, decimal> _panelPrices = new();
    private readonly Dictionary<string, string> _panelDisplayNames = new();
    private bool _updatingDiagram;

    // UI references
    private VehicleDiagramControl? _diagram;
    private StackPanel? _panelListContainer;
    private TextBlock? _subtotalText;
    private TextBlock? _discountText;
    private TextBlock? _totalText;
    private TextBlock? _panelCountText;
    private Button _ppfButton = null!;
    private Button _vinylButton = null!;
    private Button _ceramicButton = null!;
    private ComboBox? _vehicleCombo;
    private InfoBar? _infoBar;

    // Diagram panel ID -> pricing service panel ID
    private static readonly Dictionary<string, string> DiagramToPricingMap = new()
    {
        ["hood"] = "hood_full",
        ["front_bumper"] = "bumper_front",
        ["rear_bumper"] = "bumper_rear",
        ["lf_fender"] = "fender_front_left",
        ["rf_fender"] = "fender_front_right",
        ["lf_door"] = "door_front_left",
        ["rf_door"] = "door_front_right",
        ["lr_door"] = "door_rear_left",
        ["rr_door"] = "door_rear_right",
        ["l_door"] = "door_front_left",
        ["r_door"] = "door_front_right",
        ["lr_quarter"] = "quarter_left",
        ["rr_quarter"] = "quarter_right",
        ["decklid"] = "trunk_lid",
        ["liftgate"] = "trunk_lid",
        ["tailgate"] = "trunk_lid",
        ["roof"] = "roof",
        ["l_rocker"] = "rocker_left",
        ["r_rocker"] = "rocker_right",
        ["lf_mirror"] = "mirror_left",
        ["rf_mirror"] = "mirror_right",
        ["lf_headlight"] = "headlights",
        ["rf_headlight"] = "headlights",
        ["lr_taillight"] = "taillights",
        ["rr_taillight"] = "taillights",
        ["l_bedside"] = "rocker_left",
        ["r_bedside"] = "rocker_right",
        ["sliding_door"] = "door_rear_left",
        ["r_side"] = "door_rear_right",
    };

    // Panels priced as pairs - halve the pair price for individual selection
    private static readonly HashSet<string> PairPricedPanels = new()
    {
        "lf_headlight", "rf_headlight",
        "lr_taillight", "rr_taillight",
        "lf_mirror", "rf_mirror"
    };

    public PPFPricingView()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(DarkBg),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        BuildTopBar(mainGrid);
        BuildMainContent(mainGrid);
        BuildFooter(mainGrid);

        _infoBar = new InfoBar
        {
            IsOpen = false,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 60, 0, 0)
        };
        mainGrid.Children.Add(_infoBar);

        Content = mainGrid;

        // Default to Sedan
        if (_vehicleCombo != null && _vehicleCombo.Items.Count > 1)
            _vehicleCombo.SelectedIndex = 1;
    }

    #region Top Bar

    private void BuildTopBar(Grid mainGrid)
    {
        var topBar = new Border
        {
            Background = new SolidColorBrush(MedBg),
            Padding = new Thickness(16, 10, 16, 10),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Service type toggles
        var togglePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        _ppfButton = CreateServiceToggle("PPF", "ppf", true);
        _vinylButton = CreateServiceToggle("Vinyl Wrap", "vinyl", false);
        _ceramicButton = CreateServiceToggle("Ceramic Coat", "ceramic", false);

        togglePanel.Children.Add(_ppfButton);
        togglePanel.Children.Add(_vinylButton);
        togglePanel.Children.Add(_ceramicButton);

        Grid.SetColumn(togglePanel, 0);
        topGrid.Children.Add(togglePanel);

        // Vehicle label
        var vehicleLabel = new TextBlock
        {
            Text = "Vehicle:",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(vehicleLabel, 2);
        topGrid.Children.Add(vehicleLabel);

        // Vehicle ComboBox
        _vehicleCombo = new ComboBox
        {
            Width = 220,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (var style in _ppfService.GetVehicleStyles())
            _vehicleCombo.Items.Add(new ComboBoxItem { Content = style.Name, Tag = style });

        _vehicleCombo.SelectionChanged += OnVehicleStyleChanged;
        Grid.SetColumn(_vehicleCombo, 3);
        topGrid.Children.Add(_vehicleCombo);

        topBar.Child = topGrid;
        Grid.SetRow(topBar, 0);
        mainGrid.Children.Add(topBar);
    }

    private Button CreateServiceToggle(string label, string serviceType, bool isActive)
    {
        var btn = new Button
        {
            Content = label,
            Tag = serviceType,
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(isActive ? AccentGreen : Color.FromArgb(255, 55, 55, 55)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };
        btn.Click += OnServiceTypeToggle;
        return btn;
    }

    #endregion

    #region Main Content

    private void BuildMainContent(Grid mainGrid)
    {
        var splitGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(350) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        // LEFT: Diagram
        var leftStack = new StackPanel { Spacing = 8, Margin = new Thickness(16) };

        leftStack.Children.Add(new TextBlock
        {
            Text = "Click panels to select",
            FontSize = 13,
            Foreground = new SolidColorBrush(DimText),
            Margin = new Thickness(4, 0, 0, 0)
        });

        _diagram = new VehicleDiagramControl
        {
            Height = 420,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _diagram.PanelSelectionChanged += OnDiagramSelectionChanged;
        leftStack.Children.Add(_diagram);

        // Legend
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Margin = new Thickness(4, 4, 0, 0)
        };
        legend.Children.Add(CreateLegendItem(Color.FromArgb(255, 60, 65, 70), "Available"));
        legend.Children.Add(CreateLegendItem(Color.FromArgb(255, 200, 80, 60), "Selected"));
        legend.Children.Add(CreateLegendItem(Color.FromArgb(180, 100, 140, 180), "Glass"));
        leftStack.Children.Add(legend);

        Grid.SetColumn(leftStack, 0);
        splitGrid.Children.Add(leftStack);

        // RIGHT: Panel list
        var rightBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(16)
        };

        var rightGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            }
        };

        _panelCountText = new TextBlock
        {
            Text = "SELECTED PANELS (0)",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(_panelCountText, 0);
        rightGrid.Children.Add(_panelCountText);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _panelListContainer = new StackPanel { Spacing = 2 };
        scrollViewer.Content = _panelListContainer;

        Grid.SetRow(scrollViewer, 1);
        rightGrid.Children.Add(scrollViewer);

        rightBorder.Child = rightGrid;
        Grid.SetColumn(rightBorder, 1);
        splitGrid.Children.Add(rightBorder);

        Grid.SetRow(splitGrid, 1);
        mainGrid.Children.Add(splitGrid);
    }

    private static StackPanel CreateLegendItem(Color color, string label)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        item.Children.Add(new Border
        {
            Width = 14, Height = 14,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center
        });
        item.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
            VerticalAlignment = VerticalAlignment.Center
        });
        return item;
    }

    #endregion

    #region Footer

    private void BuildFooter(Grid mainGrid)
    {
        var footer = new Border
        {
            Background = new SolidColorBrush(MedBg),
            Padding = new Thickness(16, 12, 16, 12),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Totals
        var totalsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 20,
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

        _discountText = new TextBlock
        {
            Text = "",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 80)),
            VerticalAlignment = VerticalAlignment.Center
        };
        totalsPanel.Children.Add(_discountText);

        _totalText = new TextBlock
        {
            Text = "Total: $0.00",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };
        totalsPanel.Children.Add(_totalText);

        Grid.SetColumn(totalsPanel, 0);
        footerGrid.Children.Add(totalsPanel);

        // Clear button
        var clearBtn = new Button
        {
            Content = "Clear",
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 12, 0)
        };
        clearBtn.Click += OnClearClick;
        Grid.SetColumn(clearBtn, 1);
        footerGrid.Children.Add(clearBtn);

        // Export button
        var exportContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        exportContent.Children.Add(new FontIcon { Glyph = "\uE749", FontSize = 16 });
        exportContent.Children.Add(new TextBlock
        {
            Text = "Generate Quote PDF",
            VerticalAlignment = VerticalAlignment.Center
        });

        var exportBtn = new Button
        {
            Content = exportContent,
            Padding = new Thickness(20, 10, 20, 10),
            Background = new SolidColorBrush(AccentGreen),
            Foreground = new SolidColorBrush(Colors.White)
        };
        exportBtn.Click += OnExportClick;
        Grid.SetColumn(exportBtn, 2);
        footerGrid.Children.Add(exportBtn);

        footer.Child = footerGrid;
        Grid.SetRow(footer, 2);
        mainGrid.Children.Add(footer);
    }

    #endregion

    #region Event Handlers

    private void OnServiceTypeToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string serviceType) return;
        if (serviceType == _activeServiceType) return;

        _activeServiceType = serviceType;

        _ppfButton.Background = new SolidColorBrush(_activeServiceType == "ppf" ? AccentGreen : Color.FromArgb(255, 55, 55, 55));
        _vinylButton.Background = new SolidColorBrush(_activeServiceType == "vinyl" ? AccentGreen : Color.FromArgb(255, 55, 55, 55));
        _ceramicButton.Background = new SolidColorBrush(_activeServiceType == "ceramic" ? AccentGreen : Color.FromArgb(255, 55, 55, 55));

        RecalculateAllPrices();
    }

    private void OnVehicleStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vehicleCombo?.SelectedItem is not ComboBoxItem item || item.Tag is not VehicleStyle style) return;

        _activeVehicleStyle = style;
        var diagramType = GetDiagramType(style);

        _panelPrices.Clear();
        _panelDisplayNames.Clear();

        _updatingDiagram = true;
        _diagram?.SetVehicleType(diagramType);
        _updatingDiagram = false;

        RebuildPanelList();
        UpdateTotals();
    }

    private void OnDiagramSelectionChanged(object? sender, PanelSelectionChangedEventArgs e)
    {
        if (_updatingDiagram) return;

        var selectedIds = new HashSet<string>(e.SelectedPanelIds);

        // Remove deselected panels
        foreach (var id in _panelPrices.Keys.Except(selectedIds).ToList())
        {
            _panelPrices.Remove(id);
            _panelDisplayNames.Remove(id);
        }

        // Add newly selected panels
        if (_diagram != null)
        {
            var allPanels = _diagram.GetAllPanels();
            foreach (var id in selectedIds.Except(_panelPrices.Keys))
            {
                _panelPrices[id] = GetPriceForDiagramPanel(id);
                var info = allPanels.FirstOrDefault(p => p.Id == id);
                _panelDisplayNames[id] = info?.DisplayName ?? id;
            }
        }

        RebuildPanelList();
        UpdateTotals();
    }

    private void OnRemovePanel(string diagramPanelId)
    {
        _panelPrices.Remove(diagramPanelId);
        _panelDisplayNames.Remove(diagramPanelId);

        // Sync diagram
        _updatingDiagram = true;
        _diagram?.ClearSelections();
        if (_panelPrices.Count > 0)
            _diagram?.SelectPanels(_panelPrices.Keys);
        _updatingDiagram = false;

        RebuildPanelList();
        UpdateTotals();
    }

    private void OnPriceEdited(string diagramPanelId, string newText)
    {
        if (!decimal.TryParse(newText.TrimStart('$'), out var newPrice) || newPrice < 0) return;
        if (!_panelPrices.ContainsKey(diagramPanelId)) return;

        _panelPrices[diagramPanelId] = newPrice;

        // Persist custom price
        if (_activeVehicleStyle != null)
        {
            var sizeCategory = _activeVehicleStyle.SizeCategory ?? "medium";
            var pricingId = DiagramToPricingMap.GetValueOrDefault(diagramPanelId, diagramPanelId);
            var storePrice = PairPricedPanels.Contains(diagramPanelId) ? newPrice * 2 : newPrice;
            _ppfService.SetServicePanelPrice(_activeServiceType, pricingId, sizeCategory, storePrice);
        }

        UpdateTotals();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _panelPrices.Clear();
        _panelDisplayNames.Clear();
        _diagram?.ClearSelections();
        RebuildPanelList();
        UpdateTotals();
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_panelPrices.Count == 0)
        {
            ShowNotification("Select at least one panel first", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var serviceTypeName = _activeServiceType switch
            {
                "vinyl" => "Vinyl Wrap",
                "ceramic" => "Ceramic Coating",
                _ => "Paint Protection Film"
            };

            var subtotal = _panelPrices.Values.Sum();
            var (discountPct, discountAmt) = CalculateDiscount(subtotal, _panelPrices.Count);

            var pdfData = new VehicleProtectionPdfData
            {
                Date = DateTime.Now,
                ServiceType = _activeServiceType,
                ServiceTypeName = serviceTypeName,
                VehicleStyle = _activeVehicleStyle?.Name ?? "Unknown",
                PanelCount = _panelPrices.Count,
                Subtotal = subtotal,
                DiscountPercent = discountPct,
                DiscountAmount = discountAmt,
                Total = subtotal - discountAmt
            };

            foreach (var kvp in _panelPrices)
            {
                pdfData.Panels.Add(new VehicleProtectionPdfPanel
                {
                    Name = _panelDisplayNames.GetValueOrDefault(kvp.Key, kvp.Key),
                    Price = kvp.Value
                });
            }

            var pdfPath = ShopDocsPdfService.Instance.GenerateVehicleProtectionPdf(pdfData);
            DocumentUsageTrackingService.Instance.RecordPdfExport("VehicleProtection", Path.GetFileName(pdfPath), 1);

            Process.Start(new ProcessStartInfo
            {
                FileName = pdfPath,
                UseShellExecute = true
            });

            ShowNotification("Quote exported successfully!", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowNotification($"Export failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    #endregion

    #region Pricing

    private decimal GetPriceForDiagramPanel(string diagramPanelId)
    {
        if (_activeVehicleStyle == null) return 0;

        var sizeCategory = _activeVehicleStyle.SizeCategory ?? "medium";
        var pricingId = DiagramToPricingMap.GetValueOrDefault(diagramPanelId, diagramPanelId);
        var price = _ppfService.GetServicePanelPrice(_activeServiceType, pricingId, sizeCategory);

        if (PairPricedPanels.Contains(diagramPanelId))
            price = Math.Round(price / 2, 2);

        return price;
    }

    private void RecalculateAllPrices()
    {
        foreach (var id in _panelPrices.Keys.ToList())
            _panelPrices[id] = GetPriceForDiagramPanel(id);

        RebuildPanelList();
        UpdateTotals();
    }

    private static (decimal percent, decimal amount) CalculateDiscount(decimal subtotal, int panelCount)
    {
        decimal pct = panelCount >= 12 ? 15 : panelCount >= 8 ? 10 : panelCount >= 5 ? 5 : 0;
        return (pct, Math.Round(subtotal * pct / 100, 2));
    }

    private void UpdateTotals()
    {
        var subtotal = _panelPrices.Values.Sum();
        var (discountPct, discountAmt) = CalculateDiscount(subtotal, _panelPrices.Count);
        var total = subtotal - discountAmt;

        if (_subtotalText != null) _subtotalText.Text = $"Subtotal: {subtotal:C2}";
        if (_discountText != null) _discountText.Text = discountPct > 0 ? $"Discount ({discountPct}%): -{discountAmt:C2}" : "";
        if (_totalText != null) _totalText.Text = $"Total: {total:C2}";
    }

    #endregion

    #region Panel List UI

    private void RebuildPanelList()
    {
        if (_panelListContainer == null) return;
        _panelListContainer.Children.Clear();

        if (_panelCountText != null)
            _panelCountText.Text = $"SELECTED PANELS ({_panelPrices.Count})";

        if (_panelPrices.Count == 0)
        {
            _panelListContainer.Children.Add(new TextBlock
            {
                Text = "Click panels on the diagram to add them",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                Margin = new Thickness(0, 20, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (var kvp in _panelPrices)
        {
            var panelId = kvp.Key;
            var price = kvp.Value;
            var displayName = _panelDisplayNames.GetValueOrDefault(panelId, panelId);

            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 8, 8),
                Margin = new Thickness(0, 0, 0, 2)
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Panel name
            var nameText = new TextBlock
            {
                Text = displayName,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 0);
            rowGrid.Children.Add(nameText);

            // Dollar sign
            var dollarSign = new TextBlock
            {
                Text = "$",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 2, 0)
            };
            Grid.SetColumn(dollarSign, 1);
            rowGrid.Children.Add(dollarSign);

            // Editable price
            var priceBox = new TextBox
            {
                Text = price.ToString("F2"),
                Width = 100,
                TextAlignment = TextAlignment.Right,
                FontSize = 13,
                Tag = panelId,
                Padding = new Thickness(6, 4, 6, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            priceBox.LostFocus += (s, _) =>
            {
                if (s is TextBox tb && tb.Tag is string id)
                    OnPriceEdited(id, tb.Text);
            };
            Grid.SetColumn(priceBox, 2);
            rowGrid.Children.Add(priceBox);

            // Remove button
            var capturedId = panelId;
            var removeBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            removeBtn.Click += (_, _) => OnRemovePanel(capturedId);
            Grid.SetColumn(removeBtn, 3);
            rowGrid.Children.Add(removeBtn);

            row.Child = rowGrid;
            _panelListContainer.Children.Add(row);
        }
    }

    #endregion

    #region Helpers

    private static string GetDiagramType(VehicleStyle style) => style.Icon switch
    {
        "Truck" => "truck",
        "Van" => "van",
        "SUV" => "suv",
        "SportsCar" => "coupe",
        _ => style.Id?.Contains("coupe") == true ? "coupe" : "sedan"
    };

    private void ShowNotification(string message, InfoBarSeverity severity)
    {
        if (_infoBar == null) return;

        _infoBar.Message = message;
        _infoBar.Severity = severity;
        _infoBar.IsOpen = true;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _infoBar.IsOpen = false;
        };
        timer.Start();
    }

    #endregion
}
