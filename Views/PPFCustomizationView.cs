#nullable enable
using System;
using System.Collections.Generic;
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
/// Slide-over editor for customizing Vehicle Protection service types and panel items.
/// Follows the same pattern as ChecklistEditorView.
/// </summary>
public class PPFCustomizationView : UserControl
{
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);
    private static readonly Color DarkBg = Color.FromArgb(255, 25, 25, 25);
    private static readonly Color MedBg = Color.FromArgb(255, 35, 35, 35);
    private static readonly Color CardBg = Color.FromArgb(255, 45, 45, 45);
    private static readonly Color BorderClr = Color.FromArgb(255, 50, 50, 50);
    private static readonly Color DimText = Color.FromArgb(255, 150, 150, 150);

    private readonly PPFPricingService _ppfService = PPFPricingService.Instance;

    // Working copies
    private List<ServiceTypeConfig> _workingServiceTypes = new();
    private HashSet<string> _workingHiddenPanels = new();
    private Dictionary<string, List<CustomPanelItem>> _workingCustomItems = new();

    // Selected service type
    private string? _selectedServiceTypeId;

    // UI references
    private StackPanel? _serviceTypesPanel;
    private StackPanel? _panelItemsPanel;
    private TextBlock? _itemsHeaderText;
    private StackPanel? _hiddenItemsPanel;
    private Border? _hiddenSection;

    public event EventHandler? SaveRequested;
    public event EventHandler? CloseRequested;

    public PPFCustomizationView()
    {
        BuildUI();
    }

    public void LoadData()
    {
        _workingServiceTypes = _ppfService.GetAllServiceTypesIncludingHidden()
            .Select(s => new ServiceTypeConfig
            {
                Id = s.Id,
                Name = s.Name,
                PriceMultiplier = s.PriceMultiplier,
                IsBuiltIn = s.IsBuiltIn,
                IsHidden = s.IsHidden,
                Order = s.Order
            }).ToList();

        _workingHiddenPanels = new HashSet<string>(_ppfService.GetHiddenPanelIds());

        _workingCustomItems = new Dictionary<string, List<CustomPanelItem>>();
        foreach (var st in _workingServiceTypes)
        {
            var items = _ppfService.GetCustomPanelItems(st.Id);
            if (items.Count > 0)
                _workingCustomItems[st.Id] = items.Select(i => new CustomPanelItem
                {
                    Id = i.Id,
                    Name = i.Name,
                    Description = i.Description,
                    Category = i.Category,
                    DefaultPrices = i.DefaultPrices != null ? new Dictionary<string, decimal>(i.DefaultPrices) : null
                }).ToList();
        }

        _selectedServiceTypeId = _workingServiceTypes.FirstOrDefault(s => !s.IsHidden)?.Id;
        RefreshServiceTypesList();
        RefreshPanelsList();
    }

    private void BuildUI()
    {
        var root = new Grid
        {
            Background = new SolidColorBrush(DarkBg)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0 - Header
        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Row 1 - Two-column content
        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left column - Service Categories
        var leftPanel = BuildLeftColumn();
        Grid.SetColumn(leftPanel, 0);
        contentGrid.Children.Add(leftPanel);

        // Right column - Items for selected service
        var rightPanel = BuildRightColumn();
        Grid.SetColumn(rightPanel, 1);
        contentGrid.Children.Add(rightPanel);

        Grid.SetRow(contentGrid, 1);
        root.Children.Add(contentGrid);

        // Row 2 - Footer
        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
    }

    private FrameworkElement BuildHeader()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(MedBg),
            Padding = new Thickness(20, 15, 20, 15),
            BorderBrush = new SolidColorBrush(BorderClr),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = "Customize Vehicle Protection",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Add or remove service categories and items. Custom items are added to the quote directly.",
            FontSize = 13,
            Foreground = new SolidColorBrush(DimText),
            TextWrapping = TextWrapping.Wrap
        });

        border.Child = stack;
        return border;
    }

    private FrameworkElement BuildLeftColumn()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(DarkBg),
            BorderBrush = new SolidColorBrush(BorderClr),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(16)
        };

        var outerStack = new StackPanel { Spacing = 12 };

        outerStack.Children.Add(new TextBlock
        {
            Text = "SERVICE CATEGORIES",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(DimText)
        });

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 800
        };
        _serviceTypesPanel = new StackPanel { Spacing = 6 };
        scroll.Content = _serviceTypesPanel;
        outerStack.Children.Add(scroll);

        // Add Service button
        var addBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8, 12, 8)
        };
        var addContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        addContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 14, Foreground = new SolidColorBrush(AccentGreen) });
        addContent.Children.Add(new TextBlock { Text = "Add Service Category", VerticalAlignment = VerticalAlignment.Center });
        addBtn.Content = addContent;
        addBtn.Click += OnAddServiceType;
        outerStack.Children.Add(addBtn);

        border.Child = outerStack;
        return border;
    }

    private FrameworkElement BuildRightColumn()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Padding = new Thickness(16)
        };

        var outerStack = new StackPanel { Spacing = 12 };

        _itemsHeaderText = new TextBlock
        {
            Text = "ITEMS",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(DimText)
        };
        outerStack.Children.Add(_itemsHeaderText);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 600
        };
        _panelItemsPanel = new StackPanel { Spacing = 4 };
        scroll.Content = _panelItemsPanel;
        outerStack.Children.Add(scroll);

        // Add Item button
        var addItemBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 8, 12, 8)
        };
        var addItemContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        addItemContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 14, Foreground = new SolidColorBrush(AccentGreen) });
        addItemContent.Children.Add(new TextBlock { Text = "Add Custom Item", VerticalAlignment = VerticalAlignment.Center });
        addItemBtn.Content = addItemContent;
        addItemBtn.Click += OnAddPanelItem;
        outerStack.Children.Add(addItemBtn);

        // Hidden items section
        _hiddenSection = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 38)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        var hiddenStack = new StackPanel { Spacing = 8 };
        hiddenStack.Children.Add(new TextBlock
        {
            Text = "HIDDEN ITEMS",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
        });
        _hiddenItemsPanel = new StackPanel { Spacing = 4 };
        hiddenStack.Children.Add(_hiddenItemsPanel);
        _hiddenSection.Child = hiddenStack;
        outerStack.Children.Add(_hiddenSection);

        border.Child = outerStack;
        return border;
    }

    private FrameworkElement BuildFooter()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(MedBg),
            Padding = new Thickness(20, 12, 20, 12),
            BorderBrush = new SolidColorBrush(BorderClr),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(20, 10, 20, 10)
        };
        cancelBtn.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        panel.Children.Add(cancelBtn);

        var saveBtn = new Button
        {
            Content = "Save Changes",
            Padding = new Thickness(20, 10, 20, 10),
            Background = new SolidColorBrush(AccentGreen),
            Foreground = new SolidColorBrush(Colors.White)
        };
        saveBtn.Click += OnSave;
        panel.Children.Add(saveBtn);

        border.Child = panel;
        return border;
    }

    #region Refresh UI

    private void RefreshServiceTypesList()
    {
        if (_serviceTypesPanel == null) return;
        _serviceTypesPanel.Children.Clear();

        foreach (var st in _workingServiceTypes.OrderBy(s => s.Order))
        {
            _serviceTypesPanel.Children.Add(BuildServiceTypeCard(st));
        }
    }

    private FrameworkElement BuildServiceTypeCard(ServiceTypeConfig config)
    {
        var isSelected = config.Id == _selectedServiceTypeId;
        var card = new Border
        {
            Background = new SolidColorBrush(isSelected ? Color.FromArgb(255, 50, 55, 50) : CardBg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            BorderBrush = new SolidColorBrush(isSelected ? AccentGreen : BorderClr),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            Opacity = config.IsHidden ? 0.5 : 1.0,
            Tag = config.Id
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: Name + multiplier
        var infoStack = new StackPanel { Spacing = 4 };

        var nameBox = new TextBox
        {
            Text = config.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(config.IsHidden ? DimText : Colors.White),
            Padding = new Thickness(0),
            Tag = config.Id,
            IsReadOnly = config.IsHidden
        };
        var capturedId = config.Id;
        nameBox.LostFocus += (s, _) =>
        {
            if (s is TextBox tb)
            {
                var st = _workingServiceTypes.FirstOrDefault(x => x.Id == capturedId);
                if (st != null && tb.Text.Trim().Length > 0)
                    st.Name = tb.Text.Trim();
            }
        };
        infoStack.Children.Add(nameBox);

        if (!config.IsHidden)
        {
            var multRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            multRow.Children.Add(new TextBlock
            {
                Text = "Multiplier:",
                FontSize = 12,
                Foreground = new SolidColorBrush(DimText),
                VerticalAlignment = VerticalAlignment.Center
            });
            var multBox = new TextBox
            {
                Text = config.PriceMultiplier.ToString("F2"),
                Width = 60,
                FontSize = 12,
                Padding = new Thickness(4, 2, 4, 2),
                Tag = config.Id,
                TextAlignment = TextAlignment.Right
            };
            multBox.LostFocus += (s, _) =>
            {
                if (s is TextBox tb && decimal.TryParse(tb.Text, out var val) && val > 0)
                {
                    var st = _workingServiceTypes.FirstOrDefault(x => x.Id == capturedId);
                    if (st != null) st.PriceMultiplier = val;
                }
            };
            multRow.Children.Add(multBox);
            infoStack.Children.Add(multRow);
        }

        Grid.SetColumn(infoStack, 0);
        grid.Children.Add(infoStack);

        // Right: Buttons
        var btnStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Top
        };

        if (config.IsHidden)
        {
            var restoreBtn = CreateSmallButton("\uE777", "Restore");
            restoreBtn.Click += (_, _) =>
            {
                var st = _workingServiceTypes.FirstOrDefault(x => x.Id == capturedId);
                if (st != null) st.IsHidden = false;
                RefreshServiceTypesList();
            };
            btnStack.Children.Add(restoreBtn);
        }
        else
        {
            // Select button (to show items)
            if (!isSelected)
            {
                var selectBtn = CreateSmallButton("\uE76C", "Select");
                selectBtn.Click += (_, _) =>
                {
                    _selectedServiceTypeId = capturedId;
                    RefreshServiceTypesList();
                    RefreshPanelsList();
                };
                btnStack.Children.Add(selectBtn);
            }

            // Move up
            var upBtn = CreateSmallButton("\uE70E", "Move Up");
            upBtn.Click += (_, _) => MoveServiceType(capturedId, -1);
            btnStack.Children.Add(upBtn);

            // Move down
            var downBtn = CreateSmallButton("\uE70D", "Move Down");
            downBtn.Click += (_, _) => MoveServiceType(capturedId, 1);
            btnStack.Children.Add(downBtn);

            // Hide/Delete
            var removeBtn = CreateSmallButton("\uE74D", config.IsBuiltIn ? "Hide" : "Delete");
            removeBtn.Click += (_, _) =>
            {
                var st = _workingServiceTypes.FirstOrDefault(x => x.Id == capturedId);
                if (st == null) return;

                if (st.IsBuiltIn)
                {
                    st.IsHidden = true;
                }
                else
                {
                    _workingServiceTypes.Remove(st);
                }

                if (_selectedServiceTypeId == capturedId)
                    _selectedServiceTypeId = _workingServiceTypes.FirstOrDefault(x => !x.IsHidden)?.Id;

                RefreshServiceTypesList();
                RefreshPanelsList();
            };
            btnStack.Children.Add(removeBtn);
        }

        Grid.SetColumn(btnStack, 1);
        grid.Children.Add(btnStack);

        card.Child = grid;

        // Click to select
        card.PointerPressed += (_, _) =>
        {
            if (!config.IsHidden)
            {
                _selectedServiceTypeId = config.Id;
                RefreshServiceTypesList();
                RefreshPanelsList();
            }
        };

        return card;
    }

    private void RefreshPanelsList()
    {
        if (_panelItemsPanel == null) return;
        _panelItemsPanel.Children.Clear();
        _hiddenItemsPanel?.Children.Clear();

        if (_selectedServiceTypeId == null)
        {
            _itemsHeaderText!.Text = "ITEMS";
            _hiddenSection!.Visibility = Visibility.Collapsed;
            return;
        }

        var st = _workingServiceTypes.FirstOrDefault(s => s.Id == _selectedServiceTypeId);
        _itemsHeaderText!.Text = $"ITEMS FOR {st?.Name?.ToUpperInvariant() ?? "SERVICE"}";

        // Built-in panels (visible)
        var allPanels = _ppfService.GetAllPanels();
        var visiblePanels = allPanels.Where(p => !_workingHiddenPanels.Contains(p.Id ?? "")).ToList();
        var hiddenPanels = allPanels.Where(p => _workingHiddenPanels.Contains(p.Id ?? "")).ToList();

        foreach (var panel in visiblePanels)
        {
            _panelItemsPanel.Children.Add(BuildPanelItemRow(panel.Id ?? "", panel.Name ?? "", false, false));
        }

        // Custom items for this service type
        if (_workingCustomItems.TryGetValue(_selectedServiceTypeId, out var customItems))
        {
            foreach (var item in customItems)
            {
                _panelItemsPanel.Children.Add(BuildPanelItemRow(item.Id, item.Name, true, false));
            }
        }

        // Hidden items section
        if (hiddenPanels.Count > 0)
        {
            _hiddenSection!.Visibility = Visibility.Visible;
            foreach (var panel in hiddenPanels)
            {
                _hiddenItemsPanel!.Children.Add(BuildPanelItemRow(panel.Id ?? "", panel.Name ?? "", false, true));
            }
        }
        else
        {
            _hiddenSection!.Visibility = Visibility.Collapsed;
        }
    }

    private FrameworkElement BuildPanelItemRow(string id, string name, bool isCustom, bool isHidden)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(isHidden ? Color.FromArgb(255, 38, 38, 38) : CardBg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 8, 6),
            Opacity = isHidden ? 0.6 : 1.0
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Name (editable for custom items)
        if (isCustom && !isHidden)
        {
            var nameBox = new TextBox
            {
                Text = name,
                FontSize = 13,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var capturedId = id;
            nameBox.LostFocus += (s, _) =>
            {
                if (s is not TextBox tb || _selectedServiceTypeId == null) return;
                if (_workingCustomItems.TryGetValue(_selectedServiceTypeId, out var items))
                {
                    var item = items.FirstOrDefault(i => i.Id == capturedId);
                    if (item != null && tb.Text.Trim().Length > 0)
                        item.Name = tb.Text.Trim();
                }
            };
            Grid.SetColumn(nameBox, 0);
            grid.Children.Add(nameBox);
        }
        else
        {
            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 13,
                Foreground = new SolidColorBrush(isHidden ? DimText : Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (isCustom)
            {
                nameText.FontStyle = Windows.UI.Text.FontStyle.Italic;
            }
            Grid.SetColumn(nameText, 0);
            grid.Children.Add(nameText);
        }

        // Action buttons
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var capturedItemId = id;

        if (isHidden)
        {
            var restoreBtn = CreateSmallButton("\uE777", "Restore");
            restoreBtn.Click += (_, _) =>
            {
                _workingHiddenPanels.Remove(capturedItemId);
                RefreshPanelsList();
            };
            btnPanel.Children.Add(restoreBtn);
        }
        else
        {
            if (isCustom)
            {
                // Delete custom item
                var deleteBtn = CreateSmallButton("\uE74D", "Delete");
                deleteBtn.Click += (_, _) =>
                {
                    if (_selectedServiceTypeId != null && _workingCustomItems.TryGetValue(_selectedServiceTypeId, out var items))
                    {
                        items.RemoveAll(i => i.Id == capturedItemId);
                    }
                    RefreshPanelsList();
                };
                btnPanel.Children.Add(deleteBtn);
            }
            else
            {
                // Hide built-in panel
                var hideBtn = CreateSmallButton("\uE7B3", "Hide");
                hideBtn.Click += (_, _) =>
                {
                    _workingHiddenPanels.Add(capturedItemId);
                    RefreshPanelsList();
                };
                btnPanel.Children.Add(hideBtn);
            }
        }

        Grid.SetColumn(btnPanel, 1);
        grid.Children.Add(btnPanel);

        row.Child = grid;
        return row;
    }

    #endregion

    #region Actions

    private void MoveServiceType(string id, int direction)
    {
        var ordered = _workingServiceTypes.OrderBy(s => s.Order).ToList();
        var idx = ordered.FindIndex(s => s.Id == id);
        if (idx < 0) return;

        var swapIdx = idx + direction;
        if (swapIdx < 0 || swapIdx >= ordered.Count) return;

        var tempOrder = ordered[idx].Order;
        ordered[idx].Order = ordered[swapIdx].Order;
        ordered[swapIdx].Order = tempOrder;

        RefreshServiceTypesList();
    }

    private async void OnAddServiceType(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "Service name (e.g., Window Tint)",
            Margin = new Thickness(0, 8, 0, 0)
        };

        var dialog = new ContentDialog
        {
            Title = "Add Service Category",
            Content = nameBox,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text)) return;

        var maxOrder = _workingServiceTypes.Count > 0 ? _workingServiceTypes.Max(s => s.Order) : 0;
        _workingServiceTypes.Add(new ServiceTypeConfig
        {
            Id = $"custom_{Guid.NewGuid():N}"[..20],
            Name = nameBox.Text.Trim(),
            PriceMultiplier = 1.0m,
            IsBuiltIn = false,
            IsHidden = false,
            Order = maxOrder + 1
        });

        RefreshServiceTypesList();
    }

    private async void OnAddPanelItem(object sender, RoutedEventArgs e)
    {
        if (_selectedServiceTypeId == null) return;

        var stack = new StackPanel { Spacing = 8 };

        var nameBox = new TextBox { PlaceholderText = "Item name (e.g., Full Vehicle Tint)" };
        stack.Children.Add(new TextBlock { Text = "Name:", FontSize = 13 });
        stack.Children.Add(nameBox);

        var priceBox = new TextBox { PlaceholderText = "Default price (e.g., 400)" };
        stack.Children.Add(new TextBlock { Text = "Default Price:", FontSize = 13, Margin = new Thickness(0, 4, 0, 0) });
        stack.Children.Add(priceBox);

        var dialog = new ContentDialog
        {
            Title = "Add Custom Item",
            Content = stack,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text)) return;

        decimal price = 0;
        if (!string.IsNullOrWhiteSpace(priceBox.Text))
            decimal.TryParse(priceBox.Text.TrimStart('$'), out price);

        if (!_workingCustomItems.ContainsKey(_selectedServiceTypeId))
            _workingCustomItems[_selectedServiceTypeId] = new List<CustomPanelItem>();

        _workingCustomItems[_selectedServiceTypeId].Add(new CustomPanelItem
        {
            Id = $"custom_{Guid.NewGuid():N}"[..20],
            Name = nameBox.Text.Trim(),
            DefaultPrices = new Dictionary<string, decimal>
            {
                ["small"] = price,
                ["medium"] = price,
                ["large"] = price,
                ["xlarge"] = price
            }
        });

        RefreshPanelsList();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var settings = _ppfService.GetUserSettings();

        // Save service types
        settings.ServiceTypes = _workingServiceTypes;

        // Save hidden panels
        settings.HiddenPanels = _workingHiddenPanels.Count > 0 ? _workingHiddenPanels : null;

        // Save custom panel items
        settings.CustomPanelItems = _workingCustomItems.Count > 0 ? _workingCustomItems : null;

        _ppfService.UpdateUserSettings(settings);
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Helpers

    private static Button CreateSmallButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    #endregion
}
