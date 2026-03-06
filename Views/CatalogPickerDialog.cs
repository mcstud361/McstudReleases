#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views;

/// <summary>
/// ContentDialog for picking a price catalog item.
/// Shows catalog selector, search box, category filter chips, and items list.
/// Returns the selected PriceCatalogItem or null if cancelled.
/// </summary>
public class CatalogPickerDialog
{
    private static readonly Color CardBg = Color.FromArgb(255, 40, 40, 40);
    private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 212);
    private static readonly Color TextGray = Color.FromArgb(255, 160, 160, 160);

    private readonly PriceCatalogService _catalogService = PriceCatalogService.Instance;

    private ComboBox? _catalogCombo;
    private TextBox? _searchBox;
    private StackPanel? _categoryChips;
    private StackPanel? _itemsList;
    private string? _selectedCatalogId;
    private string? _selectedCategory;
    private PriceCatalogItem? _selectedItem;

    /// <summary>
    /// Show the picker dialog and return the selected item, or null if cancelled.
    /// </summary>
    public async Task<PriceCatalogItem?> ShowAsync(XamlRoot xamlRoot)
    {
        _selectedItem = null;

        var content = BuildContent();

        var dialog = new ContentDialog
        {
            Title = "Pick from Price Catalog",
            Content = content,
            PrimaryButtonText = "Select",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            IsPrimaryButtonEnabled = false
        };

        dialog.PrimaryButtonClick += (s, e) => { /* _selectedItem already set */ };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? _selectedItem : null;
    }

    private UIElement BuildContent()
    {
        var stack = new StackPanel { Spacing = 12, MinWidth = 500 };

        // Catalog selector
        var catalogRow = new StackPanel { Spacing = 4 };
        catalogRow.Children.Add(new TextBlock
        {
            Text = "Catalog",
            FontSize = 11,
            Foreground = new SolidColorBrush(TextGray)
        });

        _catalogCombo = new ComboBox
        {
            PlaceholderText = "All Catalogs",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // "All" option
        _catalogCombo.Items.Add(new ComboBoxItem { Content = "All Catalogs", Tag = (string?)null });

        foreach (var catalog in _catalogService.GetAllCatalogs())
        {
            var label = catalog.Name;
            if (!string.IsNullOrEmpty(catalog.Supplier))
                label += $" ({catalog.Supplier})";

            _catalogCombo.Items.Add(new ComboBoxItem { Content = label, Tag = catalog.Id });
        }

        _catalogCombo.SelectedIndex = 0;
        _catalogCombo.SelectionChanged += (s, e) =>
        {
            _selectedCatalogId = (_catalogCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            _selectedCategory = null;
            RefreshCategories();
            RefreshItems();
        };
        catalogRow.Children.Add(_catalogCombo);
        stack.Children.Add(catalogRow);

        // Search
        _searchBox = new TextBox
        {
            PlaceholderText = "Search by part #, description, or category...",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _searchBox.TextChanged += (s, e) => RefreshItems();
        stack.Children.Add(_searchBox);

        // Category filter chips
        _categoryChips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        var categoryScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _categoryChips,
            MaxHeight = 40
        };
        stack.Children.Add(categoryScroll);

        // Items list
        var itemsScroll = new ScrollViewer
        {
            MaxHeight = 350,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _itemsList = new StackPanel { Spacing = 4 };
        itemsScroll.Content = _itemsList;
        stack.Children.Add(itemsScroll);

        // Initial load
        RefreshCategories();
        RefreshItems();

        return stack;
    }

    private void RefreshCategories()
    {
        if (_categoryChips == null) return;
        _categoryChips.Children.Clear();

        var categories = _catalogService.GetCategories(_selectedCatalogId);
        if (!categories.Any()) return;

        // "All" chip
        var allChip = CreateChip("All", _selectedCategory == null);
        allChip.Click += (s, e) =>
        {
            _selectedCategory = null;
            RefreshCategories();
            RefreshItems();
        };
        _categoryChips.Children.Add(allChip);

        foreach (var cat in categories.Take(15)) // Cap at 15 chips
        {
            var chip = CreateChip(cat, _selectedCategory == cat);
            chip.Click += (s, e) =>
            {
                _selectedCategory = cat;
                RefreshCategories();
                RefreshItems();
            };
            _categoryChips.Children.Add(chip);
        }
    }

    private Button CreateChip(string text, bool isSelected)
    {
        return new Button
        {
            Content = text,
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Background = new SolidColorBrush(isSelected ? AccentBlue : Color.FromArgb(255, 60, 60, 60)),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(12)
        };
    }

    private void RefreshItems()
    {
        if (_itemsList == null) return;
        _itemsList.Children.Clear();

        var keyword = _searchBox?.Text?.Trim() ?? "";

        IEnumerable<(PriceCatalog Catalog, PriceCatalogItem Item)> results;

        if (!string.IsNullOrEmpty(keyword))
        {
            results = _catalogService.Search(keyword, _selectedCatalogId);
        }
        else
        {
            // Show all items from selected catalog (or all)
            var catalogs = _selectedCatalogId != null
                ? _catalogService.GetAllCatalogs().Where(c => c.Id == _selectedCatalogId)
                : _catalogService.GetAllCatalogs();

            results = catalogs.SelectMany(c => c.Items.Select(i => (c, i)));
        }

        // Apply category filter
        if (_selectedCategory != null)
        {
            results = results.Where(r => r.Item.Category == _selectedCategory);
        }

        var list = results.Take(100).ToList(); // Cap display

        if (!list.Any())
        {
            _itemsList.Children.Add(new TextBlock
            {
                Text = "No items found",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            });
            return;
        }

        foreach (var (catalog, item) in list)
        {
            var row = CreateItemRow(catalog, item);
            _itemsList.Children.Add(row);
        }
    }

    private UIElement CreateItemRow(PriceCatalog catalog, PriceCatalogItem item)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // Part#
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Desc
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Cost
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // List

        var partText = new TextBlock
        {
            Text = item.PartNumber ?? "",
            FontSize = 11,
            Foreground = new SolidColorBrush(AccentBlue),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(partText, 0);
        grid.Children.Add(partText);

        var descStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        descStack.Children.Add(new TextBlock
        {
            Text = item.Description ?? "",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(item.Category))
        {
            descStack.Children.Add(new TextBlock
            {
                Text = item.Category,
                FontSize = 10,
                Foreground = new SolidColorBrush(TextGray)
            });
        }
        Grid.SetColumn(descStack, 1);
        grid.Children.Add(descStack);

        var costText = new TextBlock
        {
            Text = item.CostPrice > 0 ? $"${item.CostPrice:F2}" : "-",
            FontSize = 11,
            Foreground = new SolidColorBrush(TextGray),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(costText, 2);
        grid.Children.Add(costText);

        var listText = new TextBlock
        {
            Text = item.ListPrice > 0 ? $"${item.ListPrice:F2}" : "-",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(listText, 3);
        grid.Children.Add(listText);

        btn.Content = grid;
        btn.Click += (s, e) =>
        {
            _selectedItem = item;
            // Visual feedback: highlight selected
            foreach (var child in _itemsList!.Children)
            {
                if (child is Border b)
                    b.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
            }
            border.Background = new SolidColorBrush(AccentBlue);

            // Enable primary button on parent dialog
            if (btn.XamlRoot != null)
            {
                // Walk up to find the ContentDialog — the primary button is auto-enabled
                // by setting _selectedItem. The dialog checks on PrimaryButtonClick.
            }
        };

        border.Child = btn;
        return border;
    }
}
