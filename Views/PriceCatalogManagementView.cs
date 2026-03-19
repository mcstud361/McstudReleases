#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views;

/// <summary>
/// Price Catalog Management View
/// - Import from Excel, CSV, PDF
/// - Browse catalogs with item counts and dates
/// - Search/filter items within a catalog
/// - Consistent UI matching other Shop Docs views
/// </summary>
public class PriceCatalogManagementView : UserControl
{
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);
    private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 212);
    private static readonly Color AccentOrange = Color.FromArgb(255, 220, 140, 30);
    private static readonly Color TextDim = Color.FromArgb(255, 140, 140, 140);
    private static readonly Color TextMuted = Color.FromArgb(255, 160, 160, 160);
    private static readonly Color CardBg = Color.FromArgb(255, 45, 45, 45);
    private static readonly Color SectionBg = Color.FromArgb(255, 50, 50, 50);

    private readonly PriceCatalogService _catalogService = PriceCatalogService.Instance;

    // Catalog list (left panel)
    private StackPanel? _catalogList;

    // Detail panel (right)
    private Grid? _detailPanel;
    private StackPanel? _itemsList;
    private TextBox? _searchBox;
    private ComboBox? _categoryFilter;
    private TextBlock? _detailTitle;
    private TextBlock? _detailInfo;
    private TextBlock? _itemCountText;
    private InfoBar? _infoBar;
    private TextBlock? _footerCountText;

    private PriceCatalog? _selectedCatalog;
    private bool _isUpdatingCategoryFilter;

    public PriceCatalogManagementView()
    {
        _catalogService.CatalogsChanged += (s, e) => RefreshCatalogList();
        BuildUI();
        RefreshCatalogList();
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },                    // Header
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // Content
                new RowDefinition { Height = GridLength.Auto }                     // Footer
            }
        };

        // Header
        var header = CreateHeader();
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // Content: Catalog list (left) + Detail (right)
        var contentGrid = new Grid
        {
            Margin = new Thickness(16, 12, 16, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(280) },
                new ColumnDefinition { Width = new GridLength(12) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        // Left: Catalog list
        var listBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0)
        };

        var listStack = new StackPanel();

        // List header
        var listHeader = new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        listHeader.Child = new TextBlock
        {
            Text = "My Catalogs",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        listStack.Children.Add(listHeader);

        var listScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8)
        };
        _catalogList = new StackPanel { Spacing = 4 };
        listScroll.Content = _catalogList;
        listStack.Children.Add(listScroll);

        listBorder.Child = listStack;
        Grid.SetColumn(listBorder, 0);
        contentGrid.Children.Add(listBorder);

        // Right: Detail panel
        _detailPanel = new Grid();

        // Placeholder when no catalog selected
        var placeholder = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8
        };
        placeholder.Children.Add(new FontIcon
        {
            Glyph = "\uE8B5",
            FontSize = 48,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80))
        });
        placeholder.Children.Add(new TextBlock
        {
            Text = "Select a catalog to view items",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        placeholder.Children.Add(new TextBlock
        {
            Text = "or import a new price sheet",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        placeholder.Name = "Placeholder";
        _detailPanel.Children.Add(placeholder);

        // Detail content (hidden until catalog selected)
        var detailBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Visibility = Visibility.Collapsed
        };
        detailBorder.Name = "DetailContent";

        var detailStack = new StackPanel { Spacing = 10 };

        // Detail header row
        var detailHeaderRow = new Grid();
        detailHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detailHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var detailTitleStack = new StackPanel { Spacing = 2 };
        _detailTitle = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        detailTitleStack.Children.Add(_detailTitle);

        _detailInfo = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(TextMuted)
        };
        detailTitleStack.Children.Add(_detailInfo);
        detailHeaderRow.Children.Add(detailTitleStack);

        // Delete button
        var deleteCatBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Top
        };
        ToolTipService.SetToolTip(deleteCatBtn, "Delete this catalog");
        deleteCatBtn.Click += OnDeleteCatalogClick;
        Grid.SetColumn(deleteCatBtn, 1);
        detailHeaderRow.Children.Add(deleteCatBtn);

        detailStack.Children.Add(detailHeaderRow);

        // Search + Category filter row
        var filterRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        _searchBox = new TextBox
        {
            PlaceholderText = "Search by part #, description, or category...",
            Margin = new Thickness(0, 0, 10, 0)
        };
        _searchBox.TextChanged += (s, e) => RefreshItemsList();
        Grid.SetColumn(_searchBox, 0);
        filterRow.Children.Add(_searchBox);

        _categoryFilter = new ComboBox
        {
            PlaceholderText = "All Categories",
            MinWidth = 200
        };
        _categoryFilter.SelectionChanged += (s, e) =>
        {
            if (!_isUpdatingCategoryFilter) RefreshItemsList();
        };
        Grid.SetColumn(_categoryFilter, 1);
        filterRow.Children.Add(_categoryFilter);

        detailStack.Children.Add(filterRow);

        // Item count
        _itemCountText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(TextMuted),
            Margin = new Thickness(0, 2, 0, 0)
        };
        detailStack.Children.Add(_itemCountText);

        // Column header
        var colHeader = CreateColumnHeader();
        detailStack.Children.Add(colHeader);

        // Items list
        var itemsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _itemsList = new StackPanel { Spacing = 0 };
        itemsScroll.Content = _itemsList;
        detailStack.Children.Add(itemsScroll);

        detailBorder.Child = detailStack;
        _detailPanel.Children.Add(detailBorder);

        Grid.SetColumn(_detailPanel, 2);
        contentGrid.Children.Add(_detailPanel);

        Grid.SetRow(contentGrid, 1);
        mainGrid.Children.Add(contentGrid);

        // Footer
        var footer = CreateFooter();
        Grid.SetRow(footer, 2);
        mainGrid.Children.Add(footer);

        // InfoBar
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

    private Border CreateHeader()
    {
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var headerContent = new StackPanel { Spacing = 4 };

        headerContent.Children.Add(new TextBlock
        {
            Text = "Price Catalogs",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });

        headerContent.Children.Add(new TextBlock
        {
            Text = "Import supplier price sheets for quick lookup. Search by part number, description, or category across all catalogs.",
            FontSize = 13,
            Foreground = new SolidColorBrush(TextMuted),
            TextWrapping = TextWrapping.Wrap
        });

        header.Child = headerContent;
        return header;
    }

    private Border CreateFooter()
    {
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

        _footerCountText = new TextBlock
        {
            Text = "0 catalogs",
            FontSize = 14,
            Foreground = new SolidColorBrush(TextMuted),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_footerCountText, 0);
        footerContent.Children.Add(_footerCountText);

        var importContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        importContent.Children.Add(new FontIcon { Glyph = "\uE8B5", FontSize = 16 });
        importContent.Children.Add(new TextBlock { Text = "Import Price Sheet", VerticalAlignment = VerticalAlignment.Center });

        var importBtn = new Button
        {
            Content = importContent,
            Padding = new Thickness(20, 10, 20, 10),
            Background = new SolidColorBrush(AccentGreen),
            Foreground = new SolidColorBrush(Colors.White)
        };
        importBtn.Click += OnImportClick;
        Grid.SetColumn(importBtn, 1);
        footerContent.Children.Add(importBtn);

        footer.Child = footerContent;
        return footer;
    }

    private static Grid CreateColumnHeader()
    {
        var headerGrid = new Grid
        {
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 38)),
            CornerRadius = new CornerRadius(4)
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        AddHeaderText(headerGrid, "Part #", 0);
        AddHeaderText(headerGrid, "Description", 1);
        AddHeaderText(headerGrid, "Category", 2);
        AddHeaderText(headerGrid, "Cost", 3, HorizontalAlignment.Right);
        AddHeaderText(headerGrid, "List Price", 4, HorizontalAlignment.Right);

        return headerGrid;
    }

    private static void AddHeaderText(Grid grid, string text, int col, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    #region Catalog List

    private void RefreshCatalogList()
    {
        if (_catalogList == null) return;
        _catalogList.Children.Clear();

        var catalogs = _catalogService.GetAllCatalogs();

        if (_footerCountText != null)
        {
            var totalItems = catalogs.Sum(c => c.Items.Count);
            _footerCountText.Text = $"{catalogs.Count} catalog{(catalogs.Count != 1 ? "s" : "")} - {totalItems} total items";
        }

        if (!catalogs.Any())
        {
            _catalogList.Children.Add(new TextBlock
            {
                Text = "No catalogs yet.\nImport a price sheet to get started.",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextMuted),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 20, 8, 0)
            });
            return;
        }

        foreach (var catalog in catalogs.OrderByDescending(c => c.ModifiedDate))
        {
            _catalogList.Children.Add(CreateCatalogCard(catalog));
        }

        // Auto-select first catalog if none selected
        if (_selectedCatalog == null && catalogs.Count > 0)
        {
            SelectCatalog(catalogs.OrderByDescending(c => c.ModifiedDate).First());
        }
    }

    private Border CreateCatalogCard(PriceCatalog catalog)
    {
        var isSelected = _selectedCatalog?.Id == catalog.Id;

        var card = new Border
        {
            Background = new SolidColorBrush(isSelected
                ? Color.FromArgb(255, 0, 90, 160)
                : SectionBg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            BorderBrush = isSelected
                ? new SolidColorBrush(AccentBlue)
                : new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(isSelected ? 1 : 0)
        };

        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var stack = new StackPanel { Spacing = 4 };

        // Name row with supplier badge
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(new TextBlock
        {
            Text = catalog.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        });

        if (!string.IsNullOrEmpty(catalog.Supplier))
        {
            nameRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(60, AccentBlue.R, AccentBlue.G, AccentBlue.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = catalog.Supplier,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 190, 255))
                }
            });
        }
        stack.Children.Add(nameRow);

        // Meta info
        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        metaRow.Children.Add(new TextBlock
        {
            Text = $"{catalog.Items.Count} items",
            FontSize = 11,
            Foreground = new SolidColorBrush(TextDim)
        });
        metaRow.Children.Add(new TextBlock
        {
            Text = $"{catalog.ModifiedDate:MMM dd, yyyy}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
        });

        // Category count
        var catCount = catalog.Items
            .Where(i => !string.IsNullOrEmpty(i.Category))
            .Select(i => i.Category!)
            .Distinct()
            .Count();
        if (catCount > 0)
        {
            metaRow.Children.Add(new TextBlock
            {
                Text = $"{catCount} categories",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            });
        }
        stack.Children.Add(metaRow);

        btn.Content = stack;
        btn.Click += (s, e) => SelectCatalog(catalog);

        card.Child = btn;
        return card;
    }

    #endregion

    #region Detail Panel

    private void SelectCatalog(PriceCatalog catalog)
    {
        _selectedCatalog = catalog;

        // Show detail content, hide placeholder
        if (_detailPanel != null)
        {
            foreach (var child in _detailPanel.Children)
            {
                if (child is FrameworkElement fe)
                {
                    if (fe.Name == "Placeholder")
                        fe.Visibility = Visibility.Collapsed;
                    else if (fe.Name == "DetailContent")
                        fe.Visibility = Visibility.Visible;
                }
            }
        }

        _detailTitle!.Text = catalog.Name;

        var info = $"{catalog.Items.Count} items";
        if (!string.IsNullOrEmpty(catalog.Supplier))
            info += $"  |  Supplier: {catalog.Supplier}";
        info += $"  |  Imported: {catalog.CreatedDate:MMM dd, yyyy}";
        _detailInfo!.Text = info;

        _searchBox!.Text = "";

        // Populate category filter
        _isUpdatingCategoryFilter = true;
        if (_categoryFilter != null)
        {
            _categoryFilter.Items.Clear();
            _categoryFilter.Items.Add(new ComboBoxItem { Content = "All Categories", Tag = "" });

            var categories = catalog.Items
                .Where(i => !string.IsNullOrEmpty(i.Category))
                .Select(i => i.Category!)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            foreach (var cat in categories)
            {
                var count = catalog.Items.Count(i => i.Category == cat);
                _categoryFilter.Items.Add(new ComboBoxItem
                {
                    Content = $"{cat} ({count})",
                    Tag = cat
                });
            }
            _categoryFilter.SelectedIndex = 0;
        }
        _isUpdatingCategoryFilter = false;

        RefreshItemsList();
        RefreshCatalogList(); // Update selection highlight
    }

    private void RefreshItemsList()
    {
        if (_itemsList == null || _selectedCatalog == null) return;
        _itemsList.Children.Clear();

        var keyword = _searchBox?.Text?.Trim() ?? "";
        var selectedCategory = "";
        if (_categoryFilter?.SelectedItem is ComboBoxItem catItem)
            selectedCategory = catItem.Tag as string ?? "";

        var items = _selectedCatalog.Items.AsEnumerable();

        if (!string.IsNullOrEmpty(selectedCategory))
            items = items.Where(i => i.Category == selectedCategory);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.ToLowerInvariant();
            items = items.Where(i =>
                (i.PartNumber?.ToLowerInvariant().Contains(kw) == true) ||
                (i.Description?.ToLowerInvariant().Contains(kw) == true) ||
                (i.Category?.ToLowerInvariant().Contains(kw) == true));
        }

        var filteredList = items.ToList();

        if (_itemCountText != null)
        {
            _itemCountText.Text = filteredList.Count == _selectedCatalog.Items.Count
                ? $"{filteredList.Count} items"
                : $"{filteredList.Count} of {_selectedCatalog.Items.Count} items";
        }

        var displayList = filteredList.Take(200).ToList();
        for (int i = 0; i < displayList.Count; i++)
        {
            _itemsList.Children.Add(CreateItemRow(displayList[i], i));
        }

        if (displayList.Count == 200 && filteredList.Count > 200)
        {
            _itemsList.Children.Add(new TextBlock
            {
                Text = $"Showing first 200 of {filteredList.Count} items. Use search or filter to narrow results.",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextDim),
                Margin = new Thickness(10, 10, 0, 0),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
        }

        if (filteredList.Count == 0)
        {
            _itemsList.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(keyword) && string.IsNullOrEmpty(selectedCategory)
                    ? "This catalog has no items."
                    : "No items match your search.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 0)
            });
        }
    }

    private static Border CreateItemRow(PriceCatalogItem item, int index)
    {
        var rowBg = index % 2 == 0
            ? Color.FromArgb(255, 38, 38, 38)
            : Color.FromArgb(255, 48, 48, 48);

        var row = new Border
        {
            Background = new SolidColorBrush(rowBg),
            Padding = new Thickness(10, 7, 10, 7),
            CornerRadius = new CornerRadius(2)
        };

        var rowGrid = new Grid();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        // Part #
        var partText = new TextBlock
        {
            Text = item.PartNumber ?? "",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(partText, 0);
        rowGrid.Children.Add(partText);

        // Description
        var descText = new TextBlock
        {
            Text = item.Description ?? "",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(descText, 1);
        rowGrid.Children.Add(descText);

        // Category chip
        if (!string.IsNullOrEmpty(item.Category))
        {
            var catBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 0, 180, 80)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = item.Category,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 130))
                }
            };
            Grid.SetColumn(catBorder, 2);
            rowGrid.Children.Add(catBorder);
        }

        // Cost
        var costText = new TextBlock
        {
            Text = item.CostPrice > 0 ? $"${item.CostPrice:F2}" : "-",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(costText, 3);
        rowGrid.Children.Add(costText);

        // List Price
        var listText = new TextBlock
        {
            Text = item.ListPrice > 0 ? $"${item.ListPrice:F2}" : "-",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 200, 100)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(listText, 4);
        rowGrid.Children.Add(listText);

        row.Child = rowGrid;
        return row;
    }

    #endregion

    #region Import

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".xlsx");
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".pdf");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(McstudDesktop.App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            await ShowImportDialog(file.Path, file.Name);
        }
        catch (Exception ex)
        {
            ShowNotification($"Import failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async System.Threading.Tasks.Task ShowImportDialog(string filePath, string fileName)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var dialogContent = new StackPanel { Spacing = 12, Width = 450 };

        // File info
        var fileRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        fileRow.Children.Add(new FontIcon
        {
            Glyph = "\uE8A5",
            FontSize = 14,
            Foreground = new SolidColorBrush(TextDim)
        });
        fileRow.Children.Add(new TextBlock
        {
            Text = fileName,
            FontSize = 12,
            Foreground = new SolidColorBrush(TextMuted)
        });
        dialogContent.Children.Add(fileRow);

        var nameBox = new TextBox
        {
            Text = Path.GetFileNameWithoutExtension(fileName),
            PlaceholderText = "Catalog name",
            Header = "Catalog Name"
        };
        dialogContent.Children.Add(nameBox);

        var supplierBox = new TextBox
        {
            PlaceholderText = "e.g., PPG, 3M, SEM, BASF",
            Header = "Supplier (optional)"
        };
        dialogContent.Children.Add(supplierBox);

        var previewText = new TextBlock
        {
            Text = "Reading file...",
            FontSize = 11,
            Foreground = new SolidColorBrush(TextDim),
            TextWrapping = TextWrapping.Wrap
        };

        dialogContent.Children.Add(new Border
        {
            Background = new SolidColorBrush(SectionBg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Child = previewText
        });

        var dialog = new ContentDialog
        {
            Title = "Import Price Sheet",
            Content = dialogContent,
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        // Preview
        PriceCatalog? previewCatalog = null;
        try
        {
            previewCatalog = ext switch
            {
                ".xlsx" => await _catalogService.ImportFromExcelAsync(filePath, "preview"),
                ".csv" => await _catalogService.ImportFromCsvAsync(filePath, "preview"),
                ".pdf" => await _catalogService.ImportFromPdfAsync(filePath, "preview"),
                _ => null
            };

            if (previewCatalog != null)
            {
                var previewItems = previewCatalog.Items.Take(8).ToList();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Found {previewCatalog.Items.Count} items. Preview:");
                sb.AppendLine();
                foreach (var item in previewItems)
                {
                    var line = item.PartNumber ?? "";
                    if (!string.IsNullOrEmpty(item.Description))
                        line += $"  {item.Description}";
                    if (item.ListPrice > 0)
                        line += $"  ${item.ListPrice:F2}";
                    sb.AppendLine(line);
                }
                if (previewCatalog.Items.Count > 8)
                    sb.AppendLine($"... and {previewCatalog.Items.Count - 8} more items");

                previewText.Text = sb.ToString();
            }
            else
            {
                previewText.Text = "Unsupported file format.";
                dialog.IsPrimaryButtonEnabled = false;
            }
        }
        catch (Exception ex)
        {
            previewText.Text = $"Preview failed: {ex.Message}";
        }

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && previewCatalog != null)
        {
            previewCatalog.Name = string.IsNullOrWhiteSpace(nameBox.Text)
                ? Path.GetFileNameWithoutExtension(fileName)
                : nameBox.Text;
            previewCatalog.Supplier = string.IsNullOrWhiteSpace(supplierBox.Text) ? null : supplierBox.Text;
            previewCatalog.Id = Guid.NewGuid().ToString();
            previewCatalog.CreatedDate = DateTime.Now;

            _catalogService.AddCatalog(previewCatalog);
            SelectCatalog(previewCatalog);

            ShowNotification($"Imported {previewCatalog.Items.Count} items into \"{previewCatalog.Name}\"", InfoBarSeverity.Success);
        }
    }

    #endregion

    #region Actions

    private async void OnDeleteCatalogClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCatalog == null) return;

        var dialog = new ContentDialog
        {
            Title = "Delete Catalog",
            Content = $"Delete \"{_selectedCatalog.Name}\" and all {_selectedCatalog.Items.Count} items? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var name = _selectedCatalog.Name;
            _catalogService.DeleteCatalog(_selectedCatalog.Id);
            _selectedCatalog = null;

            // Hide detail, show placeholder
            if (_detailPanel != null)
            {
                foreach (var child in _detailPanel.Children)
                {
                    if (child is FrameworkElement fe)
                    {
                        if (fe.Name == "Placeholder")
                            fe.Visibility = Visibility.Visible;
                        else if (fe.Name == "DetailContent")
                            fe.Visibility = Visibility.Collapsed;
                    }
                }
            }

            RefreshCatalogList();
            ShowNotification($"Deleted \"{name}\"", InfoBarSeverity.Success);
        }
    }

    #endregion

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
