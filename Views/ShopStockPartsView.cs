#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views;

public class ShopStockPartsView : UserControl
{
    private static readonly Color CardBg = Color.FromArgb(255, 40, 40, 40);
    private static readonly Color RowAlt = Color.FromArgb(255, 35, 35, 35);
    private static readonly Color RowNormal = Color.FromArgb(255, 28, 28, 28);
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);
    private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 212);
    private static readonly Color HeaderBg = Color.FromArgb(255, 50, 50, 50);
    private static readonly Color BorderColor = Color.FromArgb(255, 60, 60, 60);

    private readonly ShopStockService _stockService = ShopStockService.Instance;

    private TextBox? _searchBox;
    private ComboBox? _categoryCombo;
    private ComboBox? _makeCombo;
    private StackPanel? _partsListPanel;
    private TextBlock? _partCountText;

    public event EventHandler<StockPart>? AddToInvoiceRequested;

    public ShopStockPartsView()
    {
        BuildUI();
        RefreshPartsList();
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },  // Header
                new RowDefinition { Height = GridLength.Auto },  // Search/filter bar
                new RowDefinition { Height = GridLength.Auto },  // Table header
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) } // Table
            }
        };

        // Header bar
        var header = new Border
        {
            Background = new SolidColorBrush(CardBg),
            Padding = new Thickness(16, 12, 16, 12),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var headerStack = new Grid();
        headerStack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerStack.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new FontIcon { Glyph = "\uE773", FontSize = 20, Foreground = new SolidColorBrush(AccentGreen) });
        titleStack.Children.Add(new TextBlock { Text = "Shop Stock Parts", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });
        _partCountText = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        titleStack.Children.Add(_partCountText);
        Grid.SetColumn(titleStack, 0);
        headerStack.Children.Add(titleStack);

        var addBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new FontIcon { Glyph = "\uE710", FontSize = 14 }, new TextBlock { Text = "Add Part" } }
            },
            Background = new SolidColorBrush(AccentGreen),
            Foreground = new SolidColorBrush(Colors.White),
            Padding = new Thickness(14, 8, 14, 8),
            CornerRadius = new CornerRadius(4)
        };
        addBtn.Click += OnAddPartClick;
        Grid.SetColumn(addBtn, 1);
        headerStack.Children.Add(addBtn);

        header.Child = headerStack;
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // Search/filter bar
        var filterBar = new Border
        {
            Background = new SolidColorBrush(CardBg),
            Padding = new Thickness(16, 8, 16, 8),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var filterStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        _searchBox = new TextBox
        {
            PlaceholderText = "Search parts...",
            Width = 250,
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchBox.TextChanged += (s, e) => RefreshPartsList();
        filterStack.Children.Add(_searchBox);

        filterStack.Children.Add(new TextBlock { Text = "Category:", VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)) });
        _categoryCombo = new ComboBox { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
        _categoryCombo.SelectionChanged += (s, e) => RefreshPartsList();
        filterStack.Children.Add(_categoryCombo);

        filterStack.Children.Add(new TextBlock { Text = "Make:", VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)) });
        _makeCombo = new ComboBox { MinWidth = 140, VerticalAlignment = VerticalAlignment.Center };
        _makeCombo.SelectionChanged += (s, e) => RefreshPartsList();
        filterStack.Children.Add(_makeCombo);

        filterBar.Child = filterStack;
        Grid.SetRow(filterBar, 1);
        mainGrid.Children.Add(filterBar);

        // Table header row
        var tableHeader = BuildTableHeaderRow();
        Grid.SetRow(tableHeader, 2);
        mainGrid.Children.Add(tableHeader);

        // Scrollable parts list
        _partsListPanel = new StackPanel();
        var scroll = new ScrollViewer
        {
            Content = _partsListPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(scroll, 3);
        mainGrid.Children.Add(scroll);

        Content = mainGrid;

        PopulateFilterCombos();
    }

    private void PopulateFilterCombos()
    {
        if (_categoryCombo == null || _makeCombo == null) return;

        _categoryCombo.SelectionChanged -= OnFilterChanged;
        _makeCombo.SelectionChanged -= OnFilterChanged;

        _categoryCombo.Items.Clear();
        foreach (var cat in _stockService.GetCategories())
            _categoryCombo.Items.Add(new ComboBoxItem { Content = cat, Tag = cat });
        _categoryCombo.SelectedIndex = 0;

        _makeCombo.Items.Clear();
        foreach (var make in _stockService.GetVehicleMakes())
            _makeCombo.Items.Add(new ComboBoxItem { Content = make, Tag = make });
        _makeCombo.SelectedIndex = 0;

        _categoryCombo.SelectionChanged += OnFilterChanged;
        _makeCombo.SelectionChanged += OnFilterChanged;
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => RefreshPartsList();

    private Border BuildTableHeaderRow()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(HeaderBg),
            Padding = new Thickness(16, 8, 16, 8),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var grid = new Grid();
        // Part# | Name | Category | Make | Cost | List | Sell | Qty | Notes | Actions
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });  // Part#
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });  // Category
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // Make
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });  // Cost
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });  // List
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });  // Sell
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });  // Qty
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // Notes
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); // Actions

        string[] headers = { "Part #", "Name", "Category", "Make", "Cost", "List", "Sell", "Qty", "Notes", "" };
        for (int i = 0; i < headers.Length; i++)
        {
            var tb = new TextBlock
            {
                Text = headers[i],
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(tb, i);
            grid.Children.Add(tb);
        }

        border.Child = grid;
        return border;
    }

    private void RefreshPartsList()
    {
        if (_partsListPanel == null) return;
        _partsListPanel.Children.Clear();

        var searchTerm = _searchBox?.Text ?? "";
        var selectedCategory = (_categoryCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
        var selectedMake = (_makeCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";

        var parts = _stockService.SearchParts(searchTerm);

        if (selectedCategory != "All")
            parts = parts.Where(p => p.Category == selectedCategory).ToList();

        if (selectedMake != "All")
            parts = parts.Where(p => p.VehicleMake == selectedMake).ToList();

        if (_partCountText != null)
            _partCountText.Text = $"({parts.Count} part{(parts.Count != 1 ? "s" : "")})";

        for (int i = 0; i < parts.Count; i++)
        {
            _partsListPanel.Children.Add(BuildPartRow(parts[i], i));
        }

        if (parts.Count == 0)
        {
            _partsListPanel.Children.Add(new TextBlock
            {
                Text = "No parts found. Try adjusting your search or filters, or add a new part.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                FontSize = 13,
                Margin = new Thickness(16, 24, 16, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
    }

    private Border BuildPartRow(StockPart part, int index)
    {
        var rowBg = index % 2 == 0 ? RowNormal : RowAlt;
        var border = new Border
        {
            Background = new SolidColorBrush(rowBg),
            Padding = new Thickness(16, 6, 16, 6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

        var cellStyle = new Func<string, int, TextBlock>((text, col) =>
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(tb, col);
            return tb;
        });

        grid.Children.Add(cellStyle(part.PartNumber ?? "", 0));
        grid.Children.Add(cellStyle(part.Description ?? "", 1));
        grid.Children.Add(cellStyle(part.Category ?? "", 2));
        grid.Children.Add(cellStyle(part.VehicleMake ?? "", 3));
        grid.Children.Add(cellStyle($"${part.CostPrice:F2}", 4));
        grid.Children.Add(cellStyle(part.ListPrice > 0 ? $"${part.ListPrice:F2}" : "-", 5));
        grid.Children.Add(cellStyle($"${part.SellPrice:F2}", 6));
        grid.Children.Add(cellStyle(part.QuantityInStock.ToString(), 7));

        var notesText = cellStyle(part.Notes ?? "", 8);
        notesText.Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160));
        notesText.FontSize = 11;
        grid.Children.Add(notesText);

        // Action buttons
        var actionStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        var editBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 12 },
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80))
        };
        ToolTipService.SetToolTip(editBtn, "Edit");
        editBtn.Click += (s, e) => OnEditPartClick(part);
        actionStack.Children.Add(editBtn);

        var deleteBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80))
        };
        ToolTipService.SetToolTip(deleteBtn, "Delete");
        deleteBtn.Click += (s, e) => OnDeletePartClick(part);
        actionStack.Children.Add(deleteBtn);

        var invoiceBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE9F9", FontSize = 12 },
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(AccentBlue),
            Foreground = new SolidColorBrush(Colors.White)
        };
        ToolTipService.SetToolTip(invoiceBtn, "Add to Invoice");
        invoiceBtn.Click += (s, e) => AddToInvoiceRequested?.Invoke(this, part);
        actionStack.Children.Add(invoiceBtn);

        Grid.SetColumn(actionStack, 9);
        grid.Children.Add(actionStack);

        border.Child = grid;
        return border;
    }

    private async void OnAddPartClick(object sender, RoutedEventArgs e)
    {
        var part = new StockPart();
        var saved = await ShowPartDialog("Add New Part", part);
        if (saved)
        {
            _stockService.AddPart(part);
            PopulateFilterCombos();
            RefreshPartsList();
        }
    }

    private async void OnEditPartClick(StockPart part)
    {
        var editCopy = new StockPart
        {
            PartNumber = part.PartNumber,
            Description = part.Description,
            Category = part.Category,
            CostPrice = part.CostPrice,
            SellPrice = part.SellPrice,
            QuantityInStock = part.QuantityInStock,
            Notes = part.Notes,
            OriginalInvoiceNumber = part.OriginalInvoiceNumber,
            ListPrice = part.ListPrice,
            VehicleMake = part.VehicleMake
        };

        var saved = await ShowPartDialog("Edit Part", editCopy, isEdit: true);
        if (saved)
        {
            _stockService.UpdatePart(editCopy);
            PopulateFilterCombos();
            RefreshPartsList();
        }
    }

    private async void OnDeletePartClick(StockPart part)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete Part",
            Content = $"Delete \"{part.Description}\" ({part.PartNumber})?\nThis cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _stockService.DeletePart(part.PartNumber ?? "");
            PopulateFilterCombos();
            RefreshPartsList();
        }
    }

    private async System.Threading.Tasks.Task<bool> ShowPartDialog(string title, StockPart part, bool isEdit = false)
    {
        var formGrid = new Grid { RowSpacing = 8, ColumnSpacing = 12, MinWidth = 480 };
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int row = 0;
        void AddRow(string label, FrameworkElement control)
        {
            formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)) };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            formGrid.Children.Add(lbl);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            formGrid.Children.Add(control);
            row++;
        }

        var partNumberBox = new TextBox { Text = part.PartNumber ?? "", PlaceholderText = "e.g. CLIP-001", IsEnabled = !isEdit };
        AddRow("Part Number:", partNumberBox);

        var descBox = new TextBox { Text = part.Description ?? "", PlaceholderText = "Part description" };
        AddRow("Name:", descBox);

        var categoryBox = new TextBox { Text = part.Category ?? "", PlaceholderText = "e.g. Fasteners, Moldings" };
        AddRow("Category:", categoryBox);

        var makeBox = new TextBox { Text = part.VehicleMake ?? "", PlaceholderText = "e.g. Toyota, Honda (optional)" };
        AddRow("Vehicle Make:", makeBox);

        var costBox = new NumberBox { Value = (double)part.CostPrice, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        AddRow("Cost Price:", costBox);

        var listBox = new NumberBox { Value = (double)part.ListPrice, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        AddRow("List Price:", listBox);

        var sellBox = new NumberBox { Value = (double)part.SellPrice, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        AddRow("Sell Price:", sellBox);

        var qtyBox = new NumberBox { Value = part.QuantityInStock, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        AddRow("Quantity:", qtyBox);

        var invoiceNumBox = new TextBox { Text = part.OriginalInvoiceNumber ?? "", PlaceholderText = "Original invoice # (optional)" };
        AddRow("Invoice #:", invoiceNumBox);

        var notesBox = new TextBox { Text = part.Notes ?? "", PlaceholderText = "Notes (optional)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 60 };
        AddRow("Notes:", notesBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = formGrid, MaxHeight = 450 },
            PrimaryButtonText = isEdit ? "Save" : "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            part.PartNumber = partNumberBox.Text.Trim();
            part.Description = descBox.Text.Trim();
            part.Category = categoryBox.Text.Trim();
            part.VehicleMake = string.IsNullOrWhiteSpace(makeBox.Text) ? null : makeBox.Text.Trim();
            part.CostPrice = (decimal)costBox.Value;
            part.ListPrice = (decimal)listBox.Value;
            part.SellPrice = (decimal)sellBox.Value;
            part.QuantityInStock = (int)qtyBox.Value;
            part.OriginalInvoiceNumber = string.IsNullOrWhiteSpace(invoiceNumBox.Text) ? null : invoiceNumBox.Text.Trim();
            part.Notes = string.IsNullOrWhiteSpace(notesBox.Text) ? null : notesBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(part.PartNumber))
                return false;

            return true;
        }

        return false;
    }
}
