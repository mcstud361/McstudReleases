#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// View for generating PDF exports - search and select specific items to include
    /// </summary>
    public sealed class PdfExportView : UserControl
    {
        private PdfExportService _pdfService;

        // Search and selection UI
        private TextBox? _searchBox;
        private ListView? _searchResultsList;
        private ListView? _selectedItemsList;
        private TextBlock? _searchResultsCount;
        private TextBlock? _selectedCount;

        // Status display
        private TextBlock? _statusText;
        private Button? _generateButton;
        private Button? _clearButton;

        // Selected items for PDF
        private List<PdfExportItem> _allItems = new();
        private List<PdfExportItem> _filteredItems = new();
        private List<PdfExportItem> _selectedItems = new();

        public PdfExportView()
        {
            _pdfService = new PdfExportService();
            LoadAllItems();
            BuildUI();

            // Subscribe to items added from DefinitionsView
            DefinitionsView.OnItemAddedToPdfQueue += OnDefinitionItemAdded;

            // Load any items already in the queue
            foreach (var queueItem in DefinitionsView.PdfQueue)
            {
                AddQueueItemToSelection(queueItem);
            }
        }

        private void OnDefinitionItemAdded(object? sender, PdfQueueItem queueItem)
        {
            // Run on UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                AddQueueItemToSelection(queueItem);
            });
        }

        private void AddQueueItemToSelection(PdfQueueItem queueItem)
        {
            // Convert PdfQueueItem to PdfExportItem
            var item = new PdfExportItem
            {
                Id = queueItem.Id,
                Term = queueItem.Term,
                Category = queueItem.Category,
                Definition = queueItem.Definition,
                Details = queueItem.Details,
                PPageRef = queueItem.PPageRef,
                PPageLocation = queueItem.PPageLocation,
                DegInquiry = queueItem.DegInquiry,
                DegResponse = queueItem.DegResponse,
                Status = queueItem.Status
            };

            // Add to selected if not already there
            if (!_selectedItems.Any(x => x.Id == item.Id))
            {
                _selectedItems.Add(item);
                RefreshSearchResults();
                RefreshSelectedItems();
                UpdateStatus($"Added from Definitions: {item.Term}", true);
            }
        }

        private void LoadAllItems()
        {
            _allItems = _pdfService.GetAllItems();
            _filteredItems = _allItems.ToList();
        }

        private void BuildUI()
        {
            var mainGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18))
            };

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // === HEADER ===
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 28)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon
            {
                Glyph = "\uE8A5",
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100))
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "PDF Export",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerBorder.Child = headerStack;
            Grid.SetRow(headerBorder, 0);
            mainGrid.Children.Add(headerBorder);

            // === SEARCH BOX ===
            var searchBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var searchIcon = new FontIcon
            {
                Glyph = "\uE721",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(searchIcon, 0);

            _searchBox = new TextBox
            {
                PlaceholderText = "Search for items (e.g., de-nib, corrosion, ADAS...)",
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 13
            };
            _searchBox.TextChanged += SearchBox_TextChanged;
            Grid.SetColumn(_searchBox, 1);

            searchGrid.Children.Add(searchIcon);
            searchGrid.Children.Add(_searchBox);
            searchBorder.Child = searchGrid;
            Grid.SetRow(searchBorder, 1);
            mainGrid.Children.Add(searchBorder);

            // === CONTENT - Two columns: Search Results and Selected Items ===
            var contentGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left column - Search Results
            var leftPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 28)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 4, 0)
            };

            var leftStack = new StackPanel();

            var leftHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            leftHeader.Children.Add(new TextBlock
            {
                Text = "Available Items",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            _searchResultsCount = new TextBlock
            {
                Text = $" ({_allItems.Count})",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center
            };
            leftHeader.Children.Add(_searchResultsCount);
            leftStack.Children.Add(leftHeader);

            leftStack.Children.Add(new TextBlock
            {
                Text = "Click to add to PDF",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            _searchResultsList = new ListView
            {
                MaxHeight = 200,
                Background = new SolidColorBrush(Color.FromArgb(255, 22, 22, 22)),
                SelectionMode = ListViewSelectionMode.None,
                Padding = new Thickness(4)
            };
            leftStack.Children.Add(_searchResultsList);
            leftPanel.Child = leftStack;
            Grid.SetColumn(leftPanel, 0);
            contentGrid.Children.Add(leftPanel);

            // Right column - Selected Items
            var rightPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 28)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Margin = new Thickness(4, 0, 0, 0)
            };

            var rightStack = new StackPanel();

            var rightHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            rightHeader.Children.Add(new TextBlock
            {
                Text = "Selected for PDF",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 150))
            });
            _selectedCount = new TextBlock
            {
                Text = " (0)",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center
            };
            rightHeader.Children.Add(_selectedCount);
            rightStack.Children.Add(rightHeader);

            rightStack.Children.Add(new TextBlock
            {
                Text = "Click to remove",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            _selectedItemsList = new ListView
            {
                MaxHeight = 200,
                Background = new SolidColorBrush(Color.FromArgb(255, 22, 22, 22)),
                SelectionMode = ListViewSelectionMode.None,
                Padding = new Thickness(4)
            };
            rightStack.Children.Add(_selectedItemsList);
            rightPanel.Child = rightStack;
            Grid.SetColumn(rightPanel, 1);
            contentGrid.Children.Add(rightPanel);

            Grid.SetRow(contentGrid, 2);
            mainGrid.Children.Add(contentGrid);

            // === BUTTONS AND STATUS ===
            var bottomStack = new StackPanel { Spacing = 8 };

            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Generate PDF button
            _generateButton = new Button
            {
                Padding = new Thickness(20, 10, 20, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 180, 50, 50)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6)
            };

            var generateContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            generateContent.Children.Add(new FontIcon
            {
                Glyph = "\uE8A5",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White)
            });
            generateContent.Children.Add(new TextBlock
            {
                Text = "Generate PDF",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            _generateButton.Content = generateContent;
            _generateButton.Click += GenerateButton_Click;

            // Clear button
            _clearButton = new Button
            {
                Padding = new Thickness(20, 10, 20, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6)
            };

            var clearContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            clearContent.Children.Add(new FontIcon
            {
                Glyph = "\uE894",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White)
            });
            clearContent.Children.Add(new TextBlock
            {
                Text = "Clear",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White)
            });
            _clearButton.Content = clearContent;
            _clearButton.Click += ClearButton_Click;

            buttonStack.Children.Add(_generateButton);
            buttonStack.Children.Add(_clearButton);
            bottomStack.Children.Add(buttonStack);

            // Status text
            _statusText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };
            bottomStack.Children.Add(_statusText);

            Grid.SetRow(bottomStack, 3);
            mainGrid.Children.Add(bottomStack);

            Content = mainGrid;

            // Initial population
            RefreshSearchResults();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var search = _searchBox?.Text?.ToLower().Trim() ?? "";

            if (string.IsNullOrEmpty(search))
            {
                _filteredItems = _allItems.ToList();
            }
            else
            {
                _filteredItems = _allItems.Where(item =>
                    item.Term?.ToLower().Contains(search) == true ||
                    item.Category?.ToLower().Contains(search) == true ||
                    item.Definition?.ToLower().Contains(search) == true ||
                    item.PPageRef?.ToLower().Contains(search) == true ||
                    item.DegInquiry?.ToLower().Contains(search) == true
                ).ToList();
            }

            RefreshSearchResults();
        }

        private void RefreshSearchResults()
        {
            if (_searchResultsList == null) return;

            _searchResultsList.Items.Clear();

            // Filter out already selected items
            var availableItems = _filteredItems
                .Where(item => !_selectedItems.Any(s => s.Id == item.Id))
                .ToList();

            if (_searchResultsCount != null)
                _searchResultsCount.Text = $" ({availableItems.Count})";

            foreach (var item in availableItems.Take(50)) // Limit to 50 for performance
            {
                var itemBorder = CreateItemButton(item, true);
                _searchResultsList.Items.Add(itemBorder);
            }

            if (availableItems.Count > 50)
            {
                _searchResultsList.Items.Add(new TextBlock
                {
                    Text = $"... and {availableItems.Count - 50} more (refine your search)",
                    FontSize = 10,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    Margin = new Thickness(4)
                });
            }
        }

        private void RefreshSelectedItems()
        {
            if (_selectedItemsList == null) return;

            _selectedItemsList.Items.Clear();

            if (_selectedCount != null)
                _selectedCount.Text = $" ({_selectedItems.Count})";

            foreach (var item in _selectedItems)
            {
                var itemBorder = CreateItemButton(item, false);
                _selectedItemsList.Items.Add(itemBorder);
            }

            if (_selectedItems.Count == 0)
            {
                _selectedItemsList.Items.Add(new TextBlock
                {
                    Text = "No items selected yet",
                    FontSize = 11,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                    Margin = new Thickness(4)
                });
            }
        }

        private Border CreateItemButton(PdfExportItem item, bool isForAdd)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(isForAdd
                    ? Color.FromArgb(255, 35, 35, 35)
                    : Color.FromArgb(255, 30, 50, 40)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var stack = new StackPanel();

            // Term with badges
            var termRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            termRow.Children.Add(new TextBlock
            {
                Text = item.Term ?? "Unknown",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 140
            });

            // P-Page badge
            if (!string.IsNullOrEmpty(item.PPageRef))
            {
                var pPageBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 180, 120, 50)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1)
                };
                pPageBadge.Child = new TextBlock
                {
                    Text = item.PPageRef,
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                termRow.Children.Add(pPageBadge);
            }

            // DEG badge
            if (!string.IsNullOrEmpty(item.DegInquiry))
            {
                var degBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1)
                };
                degBadge.Child = new TextBlock
                {
                    Text = "DEG",
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                termRow.Children.Add(degBadge);
            }

            stack.Children.Add(termRow);

            // Category
            stack.Children.Add(new TextBlock
            {
                Text = item.Category ?? "",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            });

            border.Child = stack;

            // Click handler
            border.PointerPressed += (s, e) =>
            {
                if (isForAdd)
                {
                    // Add to selected
                    if (!_selectedItems.Any(x => x.Id == item.Id))
                    {
                        _selectedItems.Add(item);
                        RefreshSearchResults();
                        RefreshSelectedItems();
                        UpdateStatus($"Added: {item.Term}", true);
                    }
                }
                else
                {
                    // Remove from selected
                    _selectedItems.RemoveAll(x => x.Id == item.Id);
                    RefreshSearchResults();
                    RefreshSelectedItems();
                    UpdateStatus($"Removed: {item.Term}", true);
                }
            };

            // Hover effect
            border.PointerEntered += (s, e) =>
            {
                border.Background = new SolidColorBrush(isForAdd
                    ? Color.FromArgb(255, 50, 70, 50)
                    : Color.FromArgb(255, 70, 40, 40));
            };
            border.PointerExited += (s, e) =>
            {
                border.Background = new SolidColorBrush(isForAdd
                    ? Color.FromArgb(255, 35, 35, 35)
                    : Color.FromArgb(255, 30, 50, 40));
            };

            return border;
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedItems.Count == 0)
                {
                    UpdateStatus("Please select at least one item to export.", false);
                    return;
                }

                if (_generateButton != null) _generateButton.IsEnabled = false;
                UpdateStatus("Generating PDF...", true);

                // Generate PDF with selected items
                var outputPath = _pdfService.GeneratePdfFromItems(_selectedItems);

                if (_generateButton != null) _generateButton.IsEnabled = true;

                UpdateStatus($"PDF saved! ({_selectedItems.Count} items)", true);

                // Open the PDF file
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = outputPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PdfExportView] Error opening PDF: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                if (_generateButton != null) _generateButton.IsEnabled = true;
                UpdateStatus($"Error: {ex.Message}", false);
                Debug.WriteLine($"[PdfExportView] PDF generation error: {ex.Message}");
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedItems.Clear();
            if (_searchBox != null) _searchBox.Text = "";
            _filteredItems = _allItems.ToList();
            RefreshSearchResults();
            RefreshSelectedItems();
            UpdateStatus("Selection cleared.", true);
        }

        private void UpdateStatus(string message, bool isSuccess)
        {
            if (_statusText == null) return;

            _statusText.Text = message;
            _statusText.Foreground = new SolidColorBrush(isSuccess
                ? Color.FromArgb(255, 100, 200, 100)
                : Color.FromArgb(255, 255, 100, 100));
        }
    }
}
