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
    private readonly Dictionary<string, decimal> _panelQuantities = new();
    private readonly List<ManualLine> _manualLines = new();
    private bool _updatingDiagram;

    // A hand-added quote line (product + price + quantity), separate from diagram panels.
    private sealed class ManualLine
    {
        public string Name = "";
        public decimal Price;
        public decimal Qty = 1m;
    }

    private static string FormatQty(decimal qty) => qty.ToString("0.##");

    private decimal QtyOf(string panelId) =>
        _panelQuantities.TryGetValue(panelId, out var q) ? q : 1m;

    // UI references
    private VehicleDiagramControl? _diagram;
    private StackPanel? _panelListContainer;
    private TextBlock? _subtotalText;
    private TextBlock? _discountText;
    private TextBlock? _totalText;
    private TextBlock? _panelCountText;
    private StackPanel? _togglePanel;
    private readonly Dictionary<string, Button> _serviceButtons = new();
    private ComboBox? _vehicleCombo;
    private InfoBar? _infoBar;

    // Job / vehicle info (populates the PDF header)
    private TextBox? _customerBox;
    private TextBox? _vehicleBox;
    private TextBox? _roBox;
    private TextBox? _vinBox;

    public event EventHandler? CustomizeRequested;

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
                new RowDefinition { Height = GridLength.Auto }, // Header card
                new RowDefinition { Height = GridLength.Auto }, // Top bar (toolbar)
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        // Header card — matches the blueprint checklist's on-screen title bar
        var headerCard = ShopDocHeader.Build("Paint Protection Pricing");
        Grid.SetRow(headerCard, 0);
        mainGrid.Children.Add(headerCard);

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
        Grid.SetRow(_infoBar, 2);
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

        // Service type toggles (built dynamically)
        _togglePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        RebuildServiceToggles();

        // Customize gear button
        var gearBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE713", FontSize = 16 },
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        ToolTipService.SetToolTip(gearBtn, "Customize services");
        gearBtn.Click += (_, _) => CustomizeRequested?.Invoke(this, EventArgs.Empty);
        _togglePanel.Children.Add(gearBtn);

        Grid.SetColumn(_togglePanel, 0);
        topGrid.Children.Add(_togglePanel);

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

        // Stack the service/vehicle row above a job-info row (Customer / Vehicle / RO # / VIN)
        var topStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        topStack.Children.Add(topGrid);
        topStack.Children.Add(BuildJobInfoRow());

        topBar.Child = topStack;
        Grid.SetRow(topBar, 1);
        mainGrid.Children.Add(topBar);
    }

    private StackPanel BuildJobInfoRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        _customerBox = CreateInfoBox("Customer", 200);
        _vehicleBox = CreateInfoBox("Vehicle (Year / Make / Model)", 220);
        _roBox = CreateInfoBox("RO #", 120);
        _vinBox = CreateInfoBox("VIN", 200);

        row.Children.Add(_customerBox);
        row.Children.Add(_vehicleBox);
        row.Children.Add(_roBox);
        row.Children.Add(_vinBox);

        return row;
    }

    private static TextBox CreateInfoBox(string placeholder, double width) => new()
    {
        PlaceholderText = placeholder,
        Width = width,
        FontSize = 13,
        Padding = new Thickness(10, 6, 10, 6),
        Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
        Foreground = new SolidColorBrush(Colors.White),
        BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
        CornerRadius = new CornerRadius(4)
    };

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

        Grid.SetRow(splitGrid, 2);
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

        // Clear Form button
        var clearContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        clearContent.Children.Add(new FontIcon { Glyph = "\uE74D", FontSize = 12 });
        clearContent.Children.Add(new TextBlock { Text = "Clear Form", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

        var clearBtn = new Button
        {
            Content = clearContent,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 8, 0)
        };
        clearBtn.Click += OnClearClick;
        Grid.SetColumn(clearBtn, 1);
        footerGrid.Children.Add(clearBtn);

        // Copy button
        var copyContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        copyContent.Children.Add(new FontIcon { Glyph = "\uE8C8", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
        copyContent.Children.Add(new TextBlock { Text = "Copy", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Colors.White) });

        var copyBtn = new Button
        {
            Content = copyContent,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
            Margin = new Thickness(0, 0, 8, 0)
        };
        copyBtn.Click += OnCopyClick;
        Grid.SetColumn(copyBtn, 2);
        footerGrid.Children.Add(copyBtn);

        // Export to PDF button
        var exportContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        exportContent.Children.Add(new FontIcon { Glyph = "\uE749", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
        exportContent.Children.Add(new TextBlock { Text = "Export to PDF", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Colors.White) });

        var exportBtn = new Button
        {
            Content = exportContent,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(AccentGreen)
        };
        exportBtn.Click += OnExportClick;
        Grid.SetColumn(exportBtn, 3);
        footerGrid.Children.Add(exportBtn);

        footer.Child = footerGrid;
        Grid.SetRow(footer, 3);
        mainGrid.Children.Add(footer);
    }

    #endregion

    #region Event Handlers

    private void OnServiceTypeToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string serviceType) return;
        if (serviceType == _activeServiceType) return;

        _activeServiceType = serviceType;

        foreach (var kvp in _serviceButtons)
            kvp.Value.Background = new SolidColorBrush(kvp.Key == _activeServiceType ? AccentGreen : Color.FromArgb(255, 55, 55, 55));

        RecalculateAllPrices();
    }

    private void OnVehicleStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vehicleCombo?.SelectedItem is not ComboBoxItem item || item.Tag is not VehicleStyle style) return;

        _activeVehicleStyle = style;
        var diagramType = style.DiagramType;

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
            _panelQuantities.Remove(id);
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
                _panelQuantities[id] = 1m;
            }
        }

        RebuildPanelList();
        UpdateTotals();
    }

    private void OnRemovePanel(string diagramPanelId)
    {
        _panelPrices.Remove(diagramPanelId);
        _panelDisplayNames.Remove(diagramPanelId);
        _panelQuantities.Remove(diagramPanelId);

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

    private void OnQtyEdited(string diagramPanelId, string newText)
    {
        if (!decimal.TryParse(newText, out var qty) || qty < 0) return;
        if (!_panelPrices.ContainsKey(diagramPanelId)) return;

        _panelQuantities[diagramPanelId] = qty;
        UpdateTotals();
    }

    // Subtotal across diagram panels (price x qty) and manual product lines.
    private decimal ComputeSubtotal() =>
        _panelPrices.Sum(kvp => kvp.Value * QtyOf(kvp.Key))
        + _manualLines.Sum(l => l.Price * l.Qty);

    private int LineCount() => _panelPrices.Count + _manualLines.Count;

    private async void OnAddLineClick(object sender, RoutedEventArgs e)
    {
        var products = _ppfService.GetCustomProducts(_activeServiceType);

        var stack = new StackPanel { Spacing = 8, MinWidth = 280 };

        // Product preset picker (optional) — fills name + price when chosen
        ComboBox? productCombo = null;
        if (products.Count > 0)
        {
            stack.Children.Add(new TextBlock { Text = "Product (optional):", FontSize = 13 });
            productCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            productCombo.Items.Add(new ComboBoxItem { Content = "— Custom —", Tag = null });
            foreach (var p in products)
                productCombo.Items.Add(new ComboBoxItem { Content = $"{p.Name}  (${p.Price:F2})", Tag = p });
            productCombo.SelectedIndex = 0;
            stack.Children.Add(productCombo);
        }

        var nameBox = new TextBox { PlaceholderText = "Line name (e.g., Full Front)" };
        stack.Children.Add(new TextBlock { Text = "Name:", FontSize = 13, Margin = new Thickness(0, 4, 0, 0) });
        stack.Children.Add(nameBox);

        var priceBox = new TextBox { PlaceholderText = "Price per unit (e.g., 200)" };
        stack.Children.Add(new TextBlock { Text = "Price:", FontSize = 13, Margin = new Thickness(0, 4, 0, 0) });
        stack.Children.Add(priceBox);

        var qtyBox = new TextBox { Text = "1" };
        stack.Children.Add(new TextBlock { Text = "Quantity (partials allowed):", FontSize = 13, Margin = new Thickness(0, 4, 0, 0) });
        stack.Children.Add(qtyBox);

        if (productCombo != null)
        {
            productCombo.SelectionChanged += (_, _) =>
            {
                if (productCombo.SelectedItem is ComboBoxItem ci && ci.Tag is CustomProduct p)
                {
                    nameBox.Text = p.Name;
                    priceBox.Text = p.Price.ToString("F2");
                }
            };
        }

        var dialog = new ContentDialog
        {
            Title = "Add Line",
            Content = stack,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text)) return;

        decimal.TryParse(priceBox.Text.TrimStart('$'), out var price);
        if (!decimal.TryParse(qtyBox.Text, out var qty) || qty <= 0) qty = 1m;

        _manualLines.Add(new ManualLine { Name = nameBox.Text.Trim(), Price = price, Qty = qty });
        RebuildPanelList();
        UpdateTotals();
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (LineCount() == 0)
        {
            ShowNotification("Select at least one panel first", InfoBarSeverity.Warning);
            return;
        }

        var sb = new System.Text.StringBuilder();
        var serviceConfig = _ppfService.GetServiceType(_activeServiceType);
        var serviceTypeName = serviceConfig?.Name ?? _activeServiceType;
        sb.AppendLine($"{serviceTypeName} Quote — {_activeVehicleStyle?.Name ?? "Unknown"}");
        sb.AppendLine(new string('-', 40));

        foreach (var kvp in _panelPrices)
        {
            var name = _panelDisplayNames.GetValueOrDefault(kvp.Key, kvp.Key);
            var qty = QtyOf(kvp.Key);
            var lineTotal = kvp.Value * qty;
            sb.AppendLine(qty == 1m
                ? $"{name}: {lineTotal:C2}"
                : $"{name} (x{FormatQty(qty)} @ {kvp.Value:C2}): {lineTotal:C2}");
        }

        foreach (var line in _manualLines)
        {
            var lineTotal = line.Price * line.Qty;
            sb.AppendLine(line.Qty == 1m
                ? $"{line.Name}: {lineTotal:C2}"
                : $"{line.Name} (x{FormatQty(line.Qty)} @ {line.Price:C2}): {lineTotal:C2}");
        }

        var subtotal = ComputeSubtotal();
        var (discountPct, discountAmt) = CalculateDiscount(subtotal, LineCount());
        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"Subtotal: {subtotal:C2}");
        if (discountPct > 0)
            sb.AppendLine($"Discount ({discountPct}%): -{discountAmt:C2}");
        sb.AppendLine($"Total: {(subtotal - discountAmt):C2}");

        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        ShowNotification("Copied to clipboard!", InfoBarSeverity.Success);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _panelPrices.Clear();
        _panelDisplayNames.Clear();
        _panelQuantities.Clear();
        _manualLines.Clear();
        _diagram?.ClearSelections();
        if (_customerBox != null) _customerBox.Text = "";
        if (_vehicleBox != null) _vehicleBox.Text = "";
        if (_roBox != null) _roBox.Text = "";
        if (_vinBox != null) _vinBox.Text = "";
        RebuildPanelList();
        UpdateTotals();
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (LineCount() == 0)
        {
            ShowNotification("Select at least one panel first", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var serviceConfig = _ppfService.GetServiceType(_activeServiceType);
            var serviceTypeName = serviceConfig?.Name ?? _activeServiceType switch
            {
                "vinyl" => "Vinyl Wrap",
                "ceramic" => "Ceramic Coating",
                _ => "Paint Protection Film"
            };

            var subtotal = ComputeSubtotal();
            var (discountPct, discountAmt) = CalculateDiscount(subtotal, LineCount());

            var pdfData = new VehicleProtectionPdfData
            {
                Date = DateTime.Now,
                ServiceType = _activeServiceType,
                ServiceTypeName = serviceTypeName,
                ShopName = ShopDocsSettingsService.Instance.GetSettings().ShopName,
                VehicleStyle = _activeVehicleStyle?.Name ?? "Unknown",
                CustomerName = _customerBox?.Text?.Trim(),
                VehicleDescription = _vehicleBox?.Text?.Trim(),
                RoNumber = _roBox?.Text?.Trim(),
                Vin = _vinBox?.Text?.Trim(),
                PanelCount = LineCount(),
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
                    Price = kvp.Value,
                    Quantity = QtyOf(kvp.Key)
                });
            }

            foreach (var line in _manualLines)
            {
                pdfData.Panels.Add(new VehicleProtectionPdfPanel
                {
                    Name = line.Name,
                    Price = line.Price,
                    Quantity = line.Qty
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
        var subtotal = ComputeSubtotal();
        var (discountPct, discountAmt) = CalculateDiscount(subtotal, LineCount());
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
            _panelCountText.Text = $"LINE ITEMS ({LineCount()})";

        if (LineCount() == 0)
        {
            _panelListContainer.Children.Add(new TextBlock
            {
                Text = "Click panels on the diagram, or add a line below",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                Margin = new Thickness(0, 20, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
        else
        {
            foreach (var kvp in _panelPrices)
                _panelListContainer.Children.Add(BuildPanelRow(kvp.Key, kvp.Value));

            foreach (var line in _manualLines)
                _panelListContainer.Children.Add(BuildManualRow(line));
        }

        _panelListContainer.Children.Add(BuildAddLineButton());
    }

    // Column layout shared by panel + manual rows: Name(*) | Qty | $ | Price | Remove
    private static Grid MakeLineGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    private static Border MakeLineBorder(Color bg)
    {
        return new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 8, 8),
            Margin = new Thickness(0, 0, 0, 2)
        };
    }

    private static TextBox MakeQtyBox(decimal qty)
    {
        var box = new TextBox
        {
            Text = FormatQty(qty),
            Width = 42,
            TextAlignment = TextAlignment.Center,
            FontSize = 13,
            Padding = new Thickness(4, 4, 4, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(box, "Quantity (partials allowed)");
        return box;
    }

    private static TextBlock MakeDollarSign() => new()
    {
        Text = "$",
        FontSize = 13,
        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 2, 0)
    };

    private static Button MakeRemoveButton()
    {
        return new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
            Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private FrameworkElement BuildPanelRow(string panelId, decimal price)
    {
        var displayName = _panelDisplayNames.GetValueOrDefault(panelId, panelId);
        var row = MakeLineBorder(Color.FromArgb(255, 45, 45, 45));
        var grid = MakeLineGrid();

        var nameText = new TextBlock
        {
            Text = displayName,
            FontSize = 14,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 0);
        grid.Children.Add(nameText);

        var qtyBox = MakeQtyBox(QtyOf(panelId));
        qtyBox.LostFocus += (s, _) => { if (s is TextBox tb) OnQtyEdited(panelId, tb.Text); };
        Grid.SetColumn(qtyBox, 1);
        grid.Children.Add(qtyBox);

        var dollarSign = MakeDollarSign();
        Grid.SetColumn(dollarSign, 2);
        grid.Children.Add(dollarSign);

        var priceBox = new TextBox
        {
            Text = price.ToString("F2"),
            Width = 82,
            TextAlignment = TextAlignment.Right,
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        priceBox.LostFocus += (s, _) => { if (s is TextBox tb) OnPriceEdited(panelId, tb.Text); };
        Grid.SetColumn(priceBox, 3);
        grid.Children.Add(priceBox);

        var removeBtn = MakeRemoveButton();
        removeBtn.Click += (_, _) => OnRemovePanel(panelId);
        Grid.SetColumn(removeBtn, 4);
        grid.Children.Add(removeBtn);

        row.Child = grid;
        return row;
    }

    private FrameworkElement BuildManualRow(ManualLine line)
    {
        // Slightly different background + green accent so manual lines read as distinct.
        var row = MakeLineBorder(Color.FromArgb(255, 42, 48, 42));
        row.BorderBrush = new SolidColorBrush(AccentGreen);
        row.BorderThickness = new Thickness(2, 0, 0, 0);
        var grid = MakeLineGrid();

        var nameBox = new TextBox
        {
            Text = line.Name,
            FontSize = 14,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Colors.White),
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        nameBox.LostFocus += (s, _) => { if (s is TextBox tb && tb.Text.Trim().Length > 0) line.Name = tb.Text.Trim(); };
        Grid.SetColumn(nameBox, 0);
        grid.Children.Add(nameBox);

        var qtyBox = MakeQtyBox(line.Qty);
        qtyBox.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && decimal.TryParse(tb.Text, out var q) && q >= 0)
            {
                line.Qty = q;
                tb.Text = FormatQty(q);
                UpdateTotals();
            }
        };
        Grid.SetColumn(qtyBox, 1);
        grid.Children.Add(qtyBox);

        var dollarSign = MakeDollarSign();
        Grid.SetColumn(dollarSign, 2);
        grid.Children.Add(dollarSign);

        var priceBox = new TextBox
        {
            Text = line.Price.ToString("F2"),
            Width = 82,
            TextAlignment = TextAlignment.Right,
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        priceBox.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && decimal.TryParse(tb.Text.TrimStart('$'), out var p) && p >= 0)
            {
                line.Price = p;
                tb.Text = p.ToString("F2");
                UpdateTotals();
            }
        };
        Grid.SetColumn(priceBox, 3);
        grid.Children.Add(priceBox);

        var removeBtn = MakeRemoveButton();
        removeBtn.Click += (_, _) =>
        {
            _manualLines.Remove(line);
            RebuildPanelList();
            UpdateTotals();
        };
        Grid.SetColumn(removeBtn, 4);
        grid.Children.Add(removeBtn);

        row.Child = grid;
        return row;
    }

    private Button BuildAddLineButton()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 12, Foreground = new SolidColorBrush(AccentGreen) });
        content.Children.Add(new TextBlock { Text = "Add Line", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });

        var btn = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 6, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45))
        };
        btn.Click += OnAddLineClick;
        return btn;
    }

    #endregion

    #region Helpers

    private void RebuildServiceToggles()
    {
        if (_togglePanel == null) return;

        // Remove old service buttons (keep the gear button if present)
        foreach (var btn in _serviceButtons.Values)
            _togglePanel.Children.Remove(btn);
        _serviceButtons.Clear();

        var serviceTypes = _ppfService.GetServiceTypes();

        // If active service type was removed, default to first
        if (!serviceTypes.Any(s => s.Id == _activeServiceType))
            _activeServiceType = serviceTypes.FirstOrDefault()?.Id ?? "ppf";

        foreach (var st in serviceTypes)
        {
            var isActive = st.Id == _activeServiceType;
            var btn = CreateServiceToggle(st.Name, st.Id, isActive);
            _serviceButtons[st.Id] = btn;
            // Insert before the gear button (last child)
            var insertIdx = _togglePanel.Children.Count > 0 ? _togglePanel.Children.Count - 1 : 0;
            _togglePanel.Children.Insert(insertIdx, btn);
        }
    }

    public void RefreshAfterCustomization()
    {
        RebuildServiceToggles();
        RecalculateAllPrices();
    }

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
