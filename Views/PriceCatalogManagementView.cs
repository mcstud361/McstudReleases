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
/// Management view for price catalogs.
/// - Import from Excel, CSV, PDF
/// - Browse catalogs with item counts and dates
/// - Search/filter items within a catalog
/// - Edit/delete capabilities
/// </summary>
public class PriceCatalogManagementView : UserControl
{
    private static readonly Color CardBg = Color.FromArgb(255, 40, 40, 40);
    private static readonly Color SectionBg = Color.FromArgb(255, 50, 50, 50);
    private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 212);
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);
    private static readonly Color TextGray = Color.FromArgb(255, 160, 160, 160);

    private readonly PriceCatalogService _catalogService = PriceCatalogService.Instance;

    private StackPanel? _catalogList;
    private Grid? _detailPanel;
    private StackPanel? _itemsList;
    private TextBox? _searchBox;
    private TextBlock? _detailTitle;
    private TextBlock? _detailInfo;
    private InfoBar? _infoBar;

    private PriceCatalog? _selectedCatalog;

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
                new RowDefinition { Height = GridLength.Auto },  // Header + Import
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) } // Content
            }
        };

        // Header bar
        var header = new Border
        {
            Background = new SolidColorBrush(CardBg),
            Padding = new Thickness(16, 12, 16, 12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Spacing = 2 };
        titleStack.Children.Add(new TextBlock
        {
            Text = "Price Catalogs",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Import supplier price sheets to auto-fill charge items",
            FontSize = 11,
            Foreground = new SolidColorBrush(TextGray)
        });
        headerRow.Children.Add(titleStack);

        // Import button
        var importContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        importContent.Children.Add(new FontIcon { Glyph = "\uE8B5", FontSize = 14 });
        importContent.Children.Add(new TextBlock { Text = "Import Price Sheet", VerticalAlignment = VerticalAlignment.Center });

        var importBtn = new Button
        {
            Content = importContent,
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(AccentGreen),
            Foreground = new SolidColorBrush(Colors.White)
        };
        importBtn.Click += OnImportClick;
        Grid.SetColumn(importBtn, 1);
        headerRow.Children.Add(importBtn);

        header.Child = headerRow;
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // Content: Catalog list (left) | Detail (right)
        var contentGrid = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(280) }, // Catalog list
                new ColumnDefinition { Width = new GridLength(12) },  // Gap
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } // Detail
            }
        };

        // Left: Catalog list
        var listPanel = new Border
        {
            Background = new SolidColorBrush(CardBg),
            CornerRadius = new CornerRadius(6)
        };
        var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
        _catalogList = new StackPanel { Spacing = 6 };
        listScroll.Content = _catalogList;
        listPanel.Child = listScroll;
        Grid.SetColumn(listPanel, 0);
        contentGrid.Children.Add(listPanel);

        // Right: Detail panel
        _detailPanel = new Grid
        {
            Visibility = Visibility.Collapsed
        };
        var detailBorder = new Border
        {
            Background = new SolidColorBrush(CardBg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16)
        };

        var detailStack = new StackPanel { Spacing = 12 };

        // Detail header
        var detailHeaderRow = new Grid();
        detailHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detailHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var detailTitleStack = new StackPanel { Spacing = 2 };
        _detailTitle = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        detailTitleStack.Children.Add(_detailTitle);

        _detailInfo = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(TextGray)
        };
        detailTitleStack.Children.Add(_detailInfo);
        detailHeaderRow.Children.Add(detailTitleStack);

        // Delete catalog button
        var deleteCatBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Color.FromArgb(255, 200, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Top
        };
        ToolTipService.SetToolTip(deleteCatBtn, "Delete this catalog");
        deleteCatBtn.Click += OnDeleteCatalogClick;
        Grid.SetColumn(deleteCatBtn, 1);
        detailHeaderRow.Children.Add(deleteCatBtn);

        detailStack.Children.Add(detailHeaderRow);

        // Search within catalog
        _searchBox = new TextBox
        {
            PlaceholderText = "Search items...",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _searchBox.TextChanged += (s, e) => RefreshItemsList();
        detailStack.Children.Add(_searchBox);

        // Items list
        var itemsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 600
        };
        _itemsList = new StackPanel { Spacing = 4 };
        itemsScroll.Content = _itemsList;
        detailStack.Children.Add(itemsScroll);

        detailBorder.Child = detailStack;
        _detailPanel.Children.Add(detailBorder);
        Grid.SetColumn(_detailPanel, 2);
        contentGrid.Children.Add(_detailPanel);

        Grid.SetRow(contentGrid, 1);
        mainGrid.Children.Add(contentGrid);

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

    private void RefreshCatalogList()
    {
        if (_catalogList == null) return;
        _catalogList.Children.Clear();

        var catalogs = _catalogService.GetAllCatalogs();

        if (!catalogs.Any())
        {
            _catalogList.Children.Add(new TextBlock
            {
                Text = "No catalogs yet.\nClick 'Import Price Sheet' to get started.",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 20, 8, 0)
            });
            return;
        }

        foreach (var catalog in catalogs.OrderByDescending(c => c.ModifiedDate))
        {
            var isSelected = _selectedCatalog?.Id == catalog.Id;
            var card = new Border
            {
                Background = new SolidColorBrush(isSelected ? AccentBlue : SectionBg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8)
            };

            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };

            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = catalog.Name,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            var metaText = $"{catalog.Items.Count} items";
            if (!string.IsNullOrEmpty(catalog.Supplier))
                metaText = $"{catalog.Supplier} - {metaText}";
            metaText += $" | {catalog.ModifiedDate:MMM dd, yyyy}";

            stack.Children.Add(new TextBlock
            {
                Text = metaText,
                FontSize = 10,
                Foreground = new SolidColorBrush(TextGray)
            });

            btn.Content = stack;
            btn.Click += (s, e) => SelectCatalog(catalog);

            card.Child = btn;
            _catalogList.Children.Add(card);
        }
    }

    private void SelectCatalog(PriceCatalog catalog)
    {
        _selectedCatalog = catalog;
        _detailPanel!.Visibility = Visibility.Visible;
        _detailTitle!.Text = catalog.Name;

        var info = $"{catalog.Items.Count} items";
        if (!string.IsNullOrEmpty(catalog.Supplier))
            info += $" | Supplier: {catalog.Supplier}";
        info += $" | Imported: {catalog.CreatedDate:MMM dd, yyyy}";
        _detailInfo!.Text = info;

        _searchBox!.Text = "";
        RefreshItemsList();
        RefreshCatalogList(); // Update highlight
    }

    private void RefreshItemsList()
    {
        if (_itemsList == null || _selectedCatalog == null) return;
        _itemsList.Children.Clear();

        var keyword = _searchBox?.Text?.Trim() ?? "";
        var items = _selectedCatalog.Items.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.ToLowerInvariant();
            items = items.Where(i =>
                (i.PartNumber?.ToLowerInvariant().Contains(kw) == true) ||
                (i.Description?.ToLowerInvariant().Contains(kw) == true) ||
                (i.Category?.ToLowerInvariant().Contains(kw) == true));
        }

        // Column header
        var headerGrid = new Grid
        {
            Padding = new Thickness(8, 4, 8, 4)
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        AddHeaderCell(headerGrid, "Part #", 0);
        AddHeaderCell(headerGrid, "Description", 1);
        AddHeaderCell(headerGrid, "Category", 2);
        AddHeaderCell(headerGrid, "Cost", 3, HorizontalAlignment.Right);
        AddHeaderCell(headerGrid, "List", 4, HorizontalAlignment.Right);

        _itemsList.Children.Add(headerGrid);

        var list = items.Take(200).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            var rowBg = i % 2 == 0 ? Color.FromArgb(255, 38, 38, 38) : Color.FromArgb(255, 45, 45, 45);

            var rowGrid = new Grid
            {
                Background = new SolidColorBrush(rowBg),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(2)
            };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            AddCell(rowGrid, item.PartNumber ?? "", 0, AccentBlue);
            AddCell(rowGrid, item.Description ?? "", 1);
            AddCell(rowGrid, item.Category ?? "", 2, TextGray);
            AddCell(rowGrid, item.CostPrice > 0 ? $"${item.CostPrice:F2}" : "-", 3, TextGray, HorizontalAlignment.Right);
            AddCell(rowGrid, item.ListPrice > 0 ? $"${item.ListPrice:F2}" : "-", 4, Colors.White, HorizontalAlignment.Right);

            _itemsList.Children.Add(rowGrid);
        }

        if (list.Count == 200)
        {
            _itemsList.Children.Add(new TextBlock
            {
                Text = "Showing first 200 items. Use search to narrow results.",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray),
                Margin = new Thickness(8, 8, 0, 0)
            });
        }
    }

    private void AddHeaderCell(Grid grid, string text, int col, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(TextGray),
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private void AddCell(Grid grid, string text, int col, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = new SolidColorBrush(color ?? Colors.White),
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".xlsx");
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".pdf");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;

            // Initialize with window handle for WinUI 3
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(McstudDesktop.App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            // Show import dialog with name, supplier, preview
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
        var nameBox = new TextBox
        {
            Text = Path.GetFileNameWithoutExtension(fileName),
            PlaceholderText = "Catalog name",
            Header = "Catalog Name"
        };
        var supplierBox = new TextBox
        {
            PlaceholderText = "e.g. PPG, 3M, SEM",
            Header = "Supplier (optional)"
        };

        var previewText = new TextBlock
        {
            Text = "Importing...",
            FontSize = 11,
            Foreground = new SolidColorBrush(TextGray),
            TextWrapping = TextWrapping.Wrap
        };

        var dialogContent = new StackPanel
        {
            Spacing = 12,
            MinWidth = 400,
            Children =
            {
                new TextBlock { Text = $"File: {fileName}", FontSize = 12, Foreground = new SolidColorBrush(TextGray) },
                nameBox,
                supplierBox,
                new Border
                {
                    Background = new SolidColorBrush(SectionBg),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 8, 12, 8),
                    Child = previewText
                }
            }
        };

        var dialog = new ContentDialog
        {
            Title = "Import Price Sheet",
            Content = dialogContent,
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        // Preview: try importing in background
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
                var previewItems = previewCatalog.Items.Take(10).ToList();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Found {previewCatalog.Items.Count} items. Preview:");
                foreach (var item in previewItems)
                {
                    var line = item.PartNumber ?? "";
                    if (!string.IsNullOrEmpty(item.Description))
                        line += $" - {item.Description}";
                    if (item.CostPrice > 0)
                        line += $" | Cost: ${item.CostPrice:F2}";
                    if (item.ListPrice > 0)
                        line += $" | List: ${item.ListPrice:F2}";
                    sb.AppendLine(line);
                }
                if (previewCatalog.Items.Count > 10)
                    sb.AppendLine($"... and {previewCatalog.Items.Count - 10} more");

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
            _catalogService.DeleteCatalog(_selectedCatalog.Id);
            _selectedCatalog = null;
            _detailPanel!.Visibility = Visibility.Collapsed;
            RefreshCatalogList();
            ShowNotification("Catalog deleted", InfoBarSeverity.Informational);
        }
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
