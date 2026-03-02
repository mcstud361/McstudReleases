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

/// <summary>
/// Labor Rates View - Searchable dealer rates database
/// - Add/Edit/Delete dealers with their mechanical labor rates
/// - Search by dealer name or manufacturer (Ford, Mercedes, etc.)
/// - Filter by manufacturer dropdown
/// </summary>
public sealed class LaborRatesView : UserControl
{
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);
    private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 212);

    private readonly LaborRatesService _laborService = LaborRatesService.Instance;
    private TextBox? _searchBox;
    private ComboBox? _manufacturerFilter;
    private StackPanel? _dealerList;
    private ScrollViewer? _scrollViewer;
    private InfoBar? _infoBar;
    private string _currentSearch = "";
    private string _currentManufacturer = "";
    private bool _isUpdatingFilter = false; // Prevent reentrancy

    public LaborRatesView()
    {
        _laborService.DataChanged += (s, e) => RefreshList();
        BuildUI();
        RefreshList();
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Header + Search
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // List
                new RowDefinition { Height = GridLength.Auto } // Footer
            }
        };

        // Header with search and filter
        var header = CreateHeader();
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // Scrollable dealer list
        _scrollViewer = new ScrollViewer
        {
            Padding = new Thickness(16)
        };
        _dealerList = new StackPanel { Spacing = 8 };
        _scrollViewer.Content = _dealerList;
        Grid.SetRow(_scrollViewer, 1);
        mainGrid.Children.Add(_scrollViewer);

        // Footer with Add button
        var footer = CreateFooter();
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

    private Border CreateHeader()
    {
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var headerContent = new StackPanel { Spacing = 12 };

        // Title
        var title = new TextBlock
        {
            Text = "Dealer Labor Rates",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        headerContent.Children.Add(title);

        var subtitle = new TextBlock
        {
            Text = "Track mechanical labor rates from dealerships. Search by manufacturer to find rates quickly.",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
            TextWrapping = TextWrapping.Wrap
        };
        headerContent.Children.Add(subtitle);

        // Search row
        var searchRow = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        // Search box
        _searchBox = new TextBox
        {
            PlaceholderText = "Search dealers... (e.g., Ford, Mercedes, AutoNation)",
            Margin = new Thickness(0, 0, 12, 0)
        };
        _searchBox.TextChanged += (s, e) =>
        {
            _currentSearch = _searchBox.Text;
            RefreshList();
        };
        Grid.SetColumn(_searchBox, 0);
        searchRow.Children.Add(_searchBox);

        // Manufacturer filter dropdown
        _manufacturerFilter = new ComboBox
        {
            PlaceholderText = "All Manufacturers",
            MinWidth = 180
        };
        _manufacturerFilter.SelectionChanged += (s, e) =>
        {
            if (_manufacturerFilter.SelectedItem is ComboBoxItem item)
            {
                _currentManufacturer = item.Tag?.ToString() ?? "";
            }
            else
            {
                _currentManufacturer = "";
            }
            RefreshList();
        };
        Grid.SetColumn(_manufacturerFilter, 1);
        searchRow.Children.Add(_manufacturerFilter);

        headerContent.Children.Add(searchRow);
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

        // Count
        var countText = new TextBlock
        {
            Text = "0 dealers",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
            VerticalAlignment = VerticalAlignment.Center
        };
        countText.Name = "CountText";
        Grid.SetColumn(countText, 0);
        footerContent.Children.Add(countText);

        // Add button
        var addContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        addContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 16 });
        addContent.Children.Add(new TextBlock { Text = "Add Dealer", VerticalAlignment = VerticalAlignment.Center });

        var addButton = new Button
        {
            Content = addContent,
            Padding = new Thickness(20, 10, 20, 10),
            Background = new SolidColorBrush(AccentGreen),
            Foreground = new SolidColorBrush(Colors.White)
        };
        addButton.Click += OnAddDealerClick;
        Grid.SetColumn(addButton, 1);
        footerContent.Children.Add(addButton);

        footer.Child = footerContent;
        return footer;
    }

    private void RefreshList()
    {
        if (_dealerList == null) return;
        _dealerList.Children.Clear();

        // Update manufacturer filter dropdown
        UpdateManufacturerFilter();

        // Get filtered dealers
        List<DealerLaborRate> dealers;
        if (!string.IsNullOrWhiteSpace(_currentSearch))
        {
            dealers = _laborService.Search(_currentSearch);
        }
        else if (!string.IsNullOrWhiteSpace(_currentManufacturer))
        {
            dealers = _laborService.GetByManufacturer(_currentManufacturer);
        }
        else
        {
            dealers = _laborService.GetAllDealers();
        }

        // Apply manufacturer filter if both search and filter active
        if (!string.IsNullOrWhiteSpace(_currentSearch) && !string.IsNullOrWhiteSpace(_currentManufacturer))
        {
            dealers = dealers.Where(d =>
                d.Manufacturer?.Equals(_currentManufacturer, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        if (dealers.Count == 0)
        {
            var emptyMessage = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_currentSearch) && string.IsNullOrWhiteSpace(_currentManufacturer)
                    ? "No dealers added yet. Click 'Add Dealer' to get started."
                    : "No dealers match your search.",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            };
            _dealerList.Children.Add(emptyMessage);
        }
        else
        {
            foreach (var dealer in dealers)
            {
                _dealerList.Children.Add(CreateDealerCard(dealer));
            }
        }

        // Update count
        UpdateCount(dealers.Count);
    }

    private void UpdateManufacturerFilter()
    {
        if (_manufacturerFilter == null || _isUpdatingFilter) return;
        _isUpdatingFilter = true;

        try
        {
            var currentSelection = _currentManufacturer;
            _manufacturerFilter.Items.Clear();

            // Add "All" option
            var allItem = new ComboBoxItem { Content = "All Manufacturers", Tag = "" };
            _manufacturerFilter.Items.Add(allItem);

            // Add each manufacturer
            foreach (var mfr in _laborService.GetManufacturers())
            {
                var item = new ComboBoxItem { Content = mfr, Tag = mfr };
                _manufacturerFilter.Items.Add(item);

                if (mfr == currentSelection)
                {
                    _manufacturerFilter.SelectedItem = item;
                }
            }

            if (string.IsNullOrEmpty(currentSelection))
            {
                _manufacturerFilter.SelectedIndex = 0;
            }
        }
        finally
        {
            _isUpdatingFilter = false;
        }
    }

    private void UpdateCount(int count)
    {
        // Find the count text in the footer
        if (Content is Grid mainGrid)
        {
            foreach (var child in mainGrid.Children)
            {
                if (child is Border border && Grid.GetRow(border) == 2)
                {
                    if (border.Child is Grid footerGrid)
                    {
                        foreach (var footerChild in footerGrid.Children)
                        {
                            if (footerChild is TextBlock tb && tb.Name == "CountText")
                            {
                                tb.Text = $"{count} dealer{(count != 1 ? "s" : "")}";
                                return;
                            }
                        }
                    }
                }
            }
        }
    }

    private Border CreateDealerCard(DealerLaborRate dealer)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };

        var cardContent = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        // Left side - dealer info
        var infoPanel = new StackPanel { Spacing = 6 };

        // Dealer name and manufacturer
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        nameRow.Children.Add(new TextBlock
        {
            Text = dealer.DealerName ?? "Unknown Dealer",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        });

        if (!string.IsNullOrEmpty(dealer.Manufacturer))
        {
            nameRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(AccentBlue),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Child = new TextBlock
                {
                    Text = dealer.Manufacturer,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White)
                }
            });
        }
        infoPanel.Children.Add(nameRow);

        // Rates row
        var ratesPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

        if (dealer.MechLaborRate > 0)
            AddRateBadge(ratesPanel, "Mech", dealer.MechLaborRate);
        if (dealer.BodyLaborRate > 0)
            AddRateBadge(ratesPanel, "Body", dealer.BodyLaborRate);
        if (dealer.PaintLaborRate > 0)
            AddRateBadge(ratesPanel, "Paint", dealer.PaintLaborRate);
        if (dealer.FrameLaborRate > 0)
            AddRateBadge(ratesPanel, "Frame", dealer.FrameLaborRate);
        if (dealer.GlassLaborRate > 0)
            AddRateBadge(ratesPanel, "Glass", dealer.GlassLaborRate);

        infoPanel.Children.Add(ratesPanel);

        // Phone and notes
        if (!string.IsNullOrEmpty(dealer.Phone) || !string.IsNullOrEmpty(dealer.Notes))
        {
            var detailsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

            if (!string.IsNullOrEmpty(dealer.Phone))
            {
                detailsRow.Children.Add(new TextBlock
                {
                    Text = $"\uE717 {dealer.Phone}",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
                });
            }

            if (!string.IsNullOrEmpty(dealer.Notes))
            {
                detailsRow.Children.Add(new TextBlock
                {
                    Text = dealer.Notes,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 400
                });
            }

            infoPanel.Children.Add(detailsRow);
        }

        Grid.SetColumn(infoPanel, 0);
        cardContent.Children.Add(infoPanel);

        // Right side - actions
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var editBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 },
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        editBtn.Click += (s, e) => ShowEditDialog(dealer);
        actionsPanel.Children.Add(editBtn);

        var deleteBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        deleteBtn.Click += async (s, e) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Dealer",
                Content = $"Are you sure you want to delete {dealer.DealerName}?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _laborService.DeleteDealer(dealer.Id!);
                ShowNotification($"Deleted {dealer.DealerName}", InfoBarSeverity.Success);
            }
        };
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(actionsPanel, 1);
        cardContent.Children.Add(actionsPanel);

        card.Child = cardContent;
        return card;
    }

    private void AddRateBadge(StackPanel panel, string label, decimal rate)
    {
        var badge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        badge.Children.Add(new TextBlock
        {
            Text = label + ":",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
        });
        badge.Children.Add(new TextBlock
        {
            Text = $"${rate:N2}",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(AccentGreen)
        });
        panel.Children.Add(badge);
    }

    private async void OnAddDealerClick(object sender, RoutedEventArgs e)
    {
        await ShowDealerDialog(null);
    }

    private async void ShowEditDialog(DealerLaborRate dealer)
    {
        await ShowDealerDialog(dealer);
    }

    private async System.Threading.Tasks.Task ShowDealerDialog(DealerLaborRate? existing)
    {
        bool isEdit = existing != null;

        var dialogContent = new StackPanel { Spacing = 12, Width = 400 };

        // Dealer Name
        var nameBox = new TextBox
        {
            Header = "Dealer Name",
            PlaceholderText = "e.g., AutoNation Ford",
            Text = existing?.DealerName ?? ""
        };
        dialogContent.Children.Add(nameBox);

        // Manufacturer
        var mfrBox = new TextBox
        {
            Header = "Manufacturer (for searching)",
            PlaceholderText = "e.g., Ford, Mercedes, BMW",
            Text = existing?.Manufacturer ?? ""
        };
        dialogContent.Children.Add(mfrBox);

        // Phone
        var phoneBox = new TextBox
        {
            Header = "Phone (optional)",
            PlaceholderText = "(555) 123-4567",
            Text = existing?.Phone ?? ""
        };
        dialogContent.Children.Add(phoneBox);

        // Rates section
        dialogContent.Children.Add(new TextBlock
        {
            Text = "Labor Rates ($/hr)",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var ratesGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            ColumnSpacing = 12,
            RowSpacing = 8
        };

        var mechRate = new NumberBox { Header = "Mechanical", Value = (double)(existing?.MechLaborRate ?? 0), Minimum = 0 };
        Grid.SetRow(mechRate, 0); Grid.SetColumn(mechRate, 0);
        ratesGrid.Children.Add(mechRate);

        var bodyRate = new NumberBox { Header = "Body", Value = (double)(existing?.BodyLaborRate ?? 0), Minimum = 0 };
        Grid.SetRow(bodyRate, 0); Grid.SetColumn(bodyRate, 1);
        ratesGrid.Children.Add(bodyRate);

        var paintRate = new NumberBox { Header = "Paint", Value = (double)(existing?.PaintLaborRate ?? 0), Minimum = 0 };
        Grid.SetRow(paintRate, 1); Grid.SetColumn(paintRate, 0);
        ratesGrid.Children.Add(paintRate);

        var frameRate = new NumberBox { Header = "Frame", Value = (double)(existing?.FrameLaborRate ?? 0), Minimum = 0 };
        Grid.SetRow(frameRate, 1); Grid.SetColumn(frameRate, 1);
        ratesGrid.Children.Add(frameRate);

        var glassRate = new NumberBox { Header = "Glass", Value = (double)(existing?.GlassLaborRate ?? 0), Minimum = 0 };
        Grid.SetRow(glassRate, 2); Grid.SetColumn(glassRate, 0);
        ratesGrid.Children.Add(glassRate);

        dialogContent.Children.Add(ratesGrid);

        // Notes
        var notesBox = new TextBox
        {
            Header = "Notes (optional)",
            PlaceholderText = "Any additional info...",
            Text = existing?.Notes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 60
        };
        dialogContent.Children.Add(notesBox);

        var dialog = new ContentDialog
        {
            Title = isEdit ? "Edit Dealer" : "Add New Dealer",
            Content = dialogContent,
            PrimaryButtonText = isEdit ? "Save" : "Add",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                ShowNotification("Please enter a dealer name", InfoBarSeverity.Warning);
                return;
            }

            var dealer = existing ?? new DealerLaborRate();
            dealer.DealerName = nameBox.Text.Trim();
            dealer.Manufacturer = mfrBox.Text.Trim();
            dealer.Phone = phoneBox.Text.Trim();
            dealer.MechLaborRate = (decimal)mechRate.Value;
            dealer.BodyLaborRate = (decimal)bodyRate.Value;
            dealer.PaintLaborRate = (decimal)paintRate.Value;
            dealer.FrameLaborRate = (decimal)frameRate.Value;
            dealer.GlassLaborRate = (decimal)glassRate.Value;
            dealer.Notes = notesBox.Text.Trim();

            if (isEdit)
            {
                _laborService.UpdateDealer(dealer);
                ShowNotification($"Updated {dealer.DealerName}", InfoBarSeverity.Success);
            }
            else
            {
                _laborService.AddDealer(dealer);
                ShowNotification($"Added {dealer.DealerName}", InfoBarSeverity.Success);
            }
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
