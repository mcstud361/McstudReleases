#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    public sealed class DefinitionsView : UserControl
    {
        // Static queue shared with PdfExportView
        public static List<PdfQueueItem> PdfQueue { get; } = new();
        public static event EventHandler<PdfQueueItem>? OnItemAddedToPdfQueue;
        public static event EventHandler? OnPdfQueueChanged;

        /// <summary>
        /// Static helper to raise the OnItemAddedToPdfQueue event from other views
        /// </summary>
        public static void RaiseItemAddedToPdfQueue(PdfQueueItem item)
        {
            OnItemAddedToPdfQueue?.Invoke(null, item);
            OnPdfQueueChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Static helper to raise OnPdfQueueChanged (e.g., after clear or remove)
        /// </summary>
        public static void RaisePdfQueueChanged()
        {
            OnPdfQueueChanged?.Invoke(null, EventArgs.Empty);
        }

        private TextBox? _searchBox;
        private StackPanel? _mainContentPanel;
        private ScrollViewer? _scrollViewer;
        private TextBlock? _resultsCountText;

        // PDF Export panel (integrated)
        private StackPanel? _pdfSelectedItemsPanel;
        private TextBlock? _pdfCountText;
        private Button? _generatePdfButton;
        private PdfExportService? _pdfService;

        // Definitions data
        private List<DefinitionItem> _allDefinitions = new();
        private List<DefinitionItem> _filteredDefinitions = new();

        // Section expanders
        private Expander? _definitionsExpander;
        private StackPanel? _definitionsContent;

        public DefinitionsView()
        {
            _pdfService = new PdfExportService();
            LoadDefinitions();
            BuildUI();
            FilterContent("");
        }

        private void LoadDefinitions()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Data", "Definitions.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<DefinitionsData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (data?.Definitions != null)
                    {
                        _allDefinitions = data.Definitions
                            .OrderBy(d => d.Term)
                            .ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefinitionsView] Error loading definitions: {ex.Message}");
            }
        }

        private void BuildUI()
        {
            var mainGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18))
            };

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Search
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Filters
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Results count
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: Content

            // Note: PDF Export Panel is handled by parent ReferenceView

            // Search box
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
                PlaceholderText = "Search definitions...",
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 13
            };
            _searchBox.TextChanged += (s, e) => FilterContent(_searchBox.Text ?? "");
            Grid.SetColumn(_searchBox, 1);

            searchGrid.Children.Add(searchIcon);
            searchGrid.Children.Add(_searchBox);
            searchBorder.Child = searchGrid;
            Grid.SetRow(searchBorder, 0);
            mainGrid.Children.Add(searchBorder);

            // Results count
            _resultsCountText = new TextBlock
            {
                Text = "0 items",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                Margin = new Thickness(4, 0, 0, 6)
            };
            Grid.SetRow(_resultsCountText, 2);
            mainGrid.Children.Add(_resultsCountText);

            // Main scrollable content
            _scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            _mainContentPanel = new StackPanel { Spacing = 12 };

            // Create all content sections
            CreateDefinitionsSection();

            _scrollViewer.Content = _mainContentPanel;
            Grid.SetRow(_scrollViewer, 3);
            mainGrid.Children.Add(_scrollViewer);

            Content = mainGrid;
        }

        private void RefreshPdfPanel()
        {
            if (_pdfSelectedItemsPanel == null || _pdfCountText == null) return;

            _pdfSelectedItemsPanel.Children.Clear();
            _pdfCountText.Text = $"({PdfQueue.Count} items)";

            if (PdfQueue.Count == 0)
            {
                _pdfSelectedItemsPanel.Children.Add(new TextBlock
                {
                    Text = "Click 'Add to PDF' on any definition to add it here",
                    FontSize = 11,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
                });
            }
            else
            {
                foreach (var item in PdfQueue)
                {
                    var chip = CreatePdfItemChip(item);
                    _pdfSelectedItemsPanel.Children.Add(chip);
                }
            }
        }

        private Border CreatePdfItemChip(PdfQueueItem item)
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 80, 60)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 4, 8, 4)
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            stack.Children.Add(new TextBlock
            {
                Text = item.Term.Length > 20 ? item.Term.Substring(0, 17) + "..." : item.Term,
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Remove button (X)
            var removeBtn = new Button
            {
                Content = "\u2715",
                FontSize = 10,
                Padding = new Thickness(2),
                MinWidth = 16,
                MinHeight = 16,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
            };
            removeBtn.Click += (s, e) =>
            {
                PdfQueue.RemoveAll(q => q.Id == item.Id);
                RefreshPdfPanel();
            };
            stack.Children.Add(removeBtn);

            chip.Child = stack;
            return chip;
        }

        private void GeneratePdf_Click(object sender, RoutedEventArgs e)
        {
            if (PdfQueue.Count == 0)
            {
                // Show message
                return;
            }

            try
            {
                if (_pdfService == null) _pdfService = new PdfExportService();

                // Convert PdfQueueItem to PdfExportItem
                var exportItems = PdfQueue.Select(q => new PdfExportItem
                {
                    Id = q.Id,
                    Term = q.Term,
                    Category = q.Category,
                    Definition = q.Definition,
                    Details = q.Details,
                    PPageRef = q.PPageRef,
                    PPageLocation = q.PPageLocation,
                    DegInquiry = q.DegInquiry,
                    DegResponse = q.DegResponse,
                    Status = q.Status
                }).ToList();

                var outputPath = _pdfService.GeneratePdfFromItems(exportItems);

                // Open the PDF
                Process.Start(new ProcessStartInfo
                {
                    FileName = outputPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DefinitionsView] PDF generation error: {ex.Message}");
            }
        }

        private void ClearPdfQueue_Click(object sender, RoutedEventArgs e)
        {
            PdfQueue.Clear();
            RefreshPdfPanel();
        }

        private void CreateDefinitionsSection()
        {
            _definitionsExpander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = true
            };

            // Header with icon and count
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon
            {
                Glyph = "\uE82D", // Dictionary icon
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Definitions",
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"({_allDefinitions.Count})",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _definitionsExpander.Header = headerStack;

            _definitionsContent = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            _definitionsExpander.Content = _definitionsContent;

            _mainContentPanel!.Children.Add(_definitionsExpander);
        }

        private void FilterContent(string search)
        {
            var searchLower = search.ToLower().Trim();

            // Filter definitions
            _filteredDefinitions = string.IsNullOrEmpty(searchLower)
                ? _allDefinitions.ToList()
                : _allDefinitions.Where(d =>
                    d.Term?.ToLower().Contains(searchLower) == true ||
                    d.Definition?.ToLower().Contains(searchLower) == true ||
                    d.Details?.ToLower().Contains(searchLower) == true ||
                    d.PPageLocation?.ToLower().Contains(searchLower) == true ||
                    d.PPageRef?.ToLower().Contains(searchLower) == true
                ).ToList();

            // Update count
            if (_resultsCountText != null)
                _resultsCountText.Text = $"{_filteredDefinitions.Count} item{(_filteredDefinitions.Count != 1 ? "s" : "")}";

            // Rebuild definitions content
            _definitionsContent?.Children.Clear();
            if (_filteredDefinitions.Count == 0 && !string.IsNullOrEmpty(searchLower))
            {
                _definitionsContent?.Children.Add(CreateNoResultsPanel(search.Trim(), term =>
                {
                    if (_searchBox != null) _searchBox.Text = term;
                    FilterContent(term);
                }));
            }
            else if (_filteredDefinitions.Count == 0)
            {
                _definitionsContent?.Children.Add(new TextBlock
                {
                    Text = "No definitions match your search.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    Margin = new Thickness(8, 8, 0, 8)
                });
            }
            else
            {
                foreach (var def in _filteredDefinitions)
                {
                    _definitionsContent?.Children.Add(CreateDefinitionExpander(def));
                }
            }

            // Auto-expand section if searching and has results
            if (!string.IsNullOrEmpty(searchLower))
            {
                if (_definitionsExpander != null && _filteredDefinitions.Count > 0)
                    _definitionsExpander.IsExpanded = true;
            }
        }

        private StackPanel CreateNoResultsPanel(string searchTerm, Action<string> onSuggestionClick)
        {
            var panel = new StackPanel { Spacing = 10, Margin = new Thickness(8, 16, 8, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = $"No results for '{searchTerm}'",
                FontSize = 13,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            });
            var tryRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            tryRow.Children.Add(new TextBlock { Text = "Try:", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)), VerticalAlignment = VerticalAlignment.Center });
            foreach (var term in new[] { "blend", "corrosion", "scan", "calibration" })
            {
                var btn = new Button
                {
                    Content = term,
                    FontSize = 11,
                    Padding = new Thickness(10, 4, 10, 4),
                    Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 255)),
                    BorderThickness = new Thickness(0)
                };
                var captured = term;
                btn.Click += (s, e) => onSuggestionClick(captured);
                tryRow.Children.Add(btn);
            }
            panel.Children.Add(tryRow);
            return panel;
        }

        private Expander CreateDefinitionExpander(DefinitionItem def)
        {
            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = false
            };

            // Header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Status indicator
            var statusColor = def.Status == "NOT INCLUDED"
                ? Color.FromArgb(255, 255, 100, 100)
                : Color.FromArgb(255, 100, 255, 100);

            var statusLabel = def.Status == "NOT INCLUDED" ? "Not Included" : "Base Operation";
            var statusDot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(statusColor),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(statusDot, statusLabel);
            Grid.SetColumn(statusDot, 0);

            // Term - NO IsTextSelectionEnabled here, it breaks clicking to expand
            var termText = new TextBlock
            {
                Text = def.Term ?? "Unknown",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(termText, 1);

            headerGrid.Children.Add(statusDot);
            headerGrid.Children.Add(termText);
            expander.Header = headerGrid;

            // Content
            var contentStack = new StackPanel { Spacing = 10, Padding = new Thickness(12) };

            // Definition
            contentStack.Children.Add(new TextBlock
            {
                Text = def.Definition ?? "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            });

            // Details (if any)
            if (!string.IsNullOrEmpty(def.Details))
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = def.Details,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                    IsTextSelectionEnabled = true
                });
            }

            // Info grid (P-Page, System, etc.)
            bool hasPPage = !string.IsNullOrEmpty(def.PPageRef);

            if (hasPPage || !string.IsNullOrEmpty(def.PPageSystem))
            {
                var infoGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                if (hasPPage)
                {
                    var pPageStack = new StackPanel();
                    pPageStack.Children.Add(new TextBlock
                    {
                        Text = "P-PAGE LOCATION",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    });

                    // Make P-Page clickable - opens CCC GTE guide
                    var pPageLink = new HyperlinkButton
                    {
                        Content = $"{def.PPageLocation ?? ""} ({def.PPageRef}) →",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                        Padding = new Thickness(0)
                    };
                    pPageLink.Click += (s, e) =>
                    {
                        try
                        {
                            var url = GetPPageUrl(def.PPageRef ?? "");
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    };
                    ToolTipService.SetToolTip(pPageLink, $"Open {def.PPageRef} in CCC Guide to Estimating");
                    pPageStack.Children.Add(pPageLink);

                    Grid.SetColumn(pPageStack, 0);
                    infoGrid.Children.Add(pPageStack);
                }

                var systemStack = new StackPanel();
                systemStack.Children.Add(new TextBlock
                {
                    Text = "ESTIMATING SYSTEM",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
                systemStack.Children.Add(new TextBlock
                {
                    Text = def.PPageSystem ?? "All Systems",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 255)),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                });
                Grid.SetColumn(systemStack, hasPPage ? 1 : 0);

                infoGrid.Children.Add(systemStack);
                contentStack.Children.Add(infoGrid);
            }

            // Links section
            if (def.Links != null && def.Links.Count > 0)
            {
                var linksStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(0, 8, 0, 0)
                };

                foreach (var link in def.Links)
                {
                    var linkBtn = new HyperlinkButton
                    {
                        Content = link.Key.ToUpper(),
                        NavigateUri = new Uri(link.Value),
                        FontSize = 10,
                        Padding = new Thickness(8, 4, 8, 4)
                    };
                    linksStack.Children.Add(linkBtn);
                }

                contentStack.Children.Add(linksStack);
            }

            // Button row for Copy and Add to PDF
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0)
            };

            // Add to PDF button
            var addToPdfBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8A5", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100)) },
                        new TextBlock { Text = "Add to PDF", FontSize = 11 }
                    }
                },
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 100, 40, 40)),
                BorderThickness = new Thickness(0)
            };
            addToPdfBtn.Click += (s, e) => AddDefinitionToPdfQueue(def, addToPdfBtn);
            ToolTipService.SetToolTip(addToPdfBtn, "Add this definition to your PDF export queue");
            buttonRow.Children.Add(addToPdfBtn);

            // Copy All button
            var copyBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8C8", FontSize = 12 },
                        new TextBlock { Text = "Copy All", FontSize = 11 }
                    }
                },
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                BorderThickness = new Thickness(0)
            };
            copyBtn.Click += (s, e) => CopyDefinitionToClipboard(def);
            buttonRow.Children.Add(copyBtn);

            var readAloudBtn = Services.TextToSpeechService.CreateReadAloudButton(() =>
            {
                var sb = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(def.Term)) sb.AppendLine(def.Term);
                if (!string.IsNullOrEmpty(def.Definition)) sb.AppendLine(def.Definition);
                if (!string.IsNullOrEmpty(def.Details)) sb.AppendLine(def.Details);
                return sb.ToString();
            });
            buttonRow.Children.Add(readAloudBtn);

            contentStack.Children.Add(buttonRow);

            expander.Content = contentStack;
            return expander;
        }

        /// <summary>
        /// Gets the direct URL to a specific CCC P-Page section
        /// </summary>
        private static string GetPPageUrl(string pPageRef)
        {
            // CCC P-Pages are at help.cccis.com with specific page URLs
            // Format: G1, G9, G33, etc.
            if (string.IsNullOrWhiteSpace(pPageRef))
                return "https://help.cccis.com/webhelp/motor/gte/guide.htm";

            // Clean the reference (remove spaces, handle multiple refs)
            var cleanRef = pPageRef.Trim().ToUpperInvariant();

            // CCC ONE guide pages use this URL format
            // The guide.htm page supports hash navigation to specific pages
            return $"https://help.cccis.com/webhelp/motor/gte/guide.htm#{cleanRef.ToLowerInvariant()}";
        }

        private void CopyDefinitionToClipboard(DefinitionItem def)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {def.Term} ===");
            sb.AppendLine();
            sb.AppendLine(def.Definition);

            if (!string.IsNullOrEmpty(def.Details))
            {
                sb.AppendLine();
                sb.AppendLine($"Details: {def.Details}");
            }

            if (!string.IsNullOrEmpty(def.PPageRef))
            {
                sb.AppendLine();
                sb.AppendLine($"P-Page: {def.PPageRef} ({def.PPageLocation})");
            }

            if (!string.IsNullOrEmpty(def.PPageSystem))
            {
                sb.AppendLine($"Systems: {def.PPageSystem}");
            }

            sb.AppendLine();
            sb.AppendLine($"Status: {def.Status}");

            var dataPackage = new DataPackage();
            dataPackage.SetText(sb.ToString());
            Clipboard.SetContent(dataPackage);
        }

        // Data classes
        private class DefinitionsData
        {
            public string? Version { get; set; }
            public List<DefinitionItem>? Definitions { get; set; }
        }

        private class DefinitionItem
        {
            public string? Id { get; set; }
            public string? Term { get; set; }
            public string? Category { get; set; }
            public string? Status { get; set; }
            public string? PPageLocation { get; set; }
            public string? PPageRef { get; set; }
            public string? PPageSystem { get; set; }
            public string? Definition { get; set; }
            public string? Details { get; set; }
            public string? DegInquiry { get; set; }
            public string? DegResponse { get; set; }
            public Dictionary<string, string>? Links { get; set; }
        }

        /// <summary>
        /// Add a definition to the PDF export queue
        /// </summary>
        private void AddDefinitionToPdfQueue(DefinitionItem def, Button? addButton = null)
        {
            var item = new PdfQueueItem
            {
                Id = def.Id ?? Guid.NewGuid().ToString(),
                Term = def.Term ?? "Unknown",
                Category = def.Category ?? "General",
                Definition = def.Definition ?? "",
                Details = def.Details,
                PPageRef = def.PPageRef,
                PPageLocation = def.PPageLocation,
                DegInquiry = def.DegInquiry,
                DegResponse = def.DegResponse,
                Status = def.Status
            };

            // Check if already in queue
            if (PdfQueue.Any(q => q.Id == item.Id))
            {
                // Show already added feedback on button
                if (addButton != null)
                {
                    ShowButtonFeedback(addButton, "Already Added", Color.FromArgb(255, 100, 100, 100));
                }
                return;
            }

            PdfQueue.Add(item);
            RaiseItemAddedToPdfQueue(item);

            // Refresh the PDF panel to show the new item
            RefreshPdfPanel();

            // Show visual feedback on the button
            if (addButton != null)
            {
                ShowButtonFeedback(addButton, "Added!", Color.FromArgb(255, 50, 150, 50));
            }

            Debug.WriteLine($"[DefinitionsView] Added to PDF queue: {item.Term} (queue size: {PdfQueue.Count})");
        }

        private async void ShowButtonFeedback(Button button, string message, Color bgColor)
        {
            var originalContent = button.Content;
            var originalBg = button.Background;

            // Show feedback
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) }, // Checkmark
                    new TextBlock { Text = message, FontSize = 11, Foreground = new SolidColorBrush(Colors.White) }
                }
            };
            button.Background = new SolidColorBrush(bgColor);
            button.IsEnabled = false;

            // Wait 1.5 seconds then restore
            await System.Threading.Tasks.Task.Delay(1500);

            button.Content = originalContent;
            button.Background = originalBg;
            button.IsEnabled = true;
        }
    }

    /// <summary>
    /// Item for PDF export queue - shared between DefinitionsView and PdfExportView
    /// </summary>
    public class PdfQueueItem
    {
        public string Id { get; set; } = "";
        public string Term { get; set; } = "";
        public string Category { get; set; } = "";
        public string Definition { get; set; } = "";
        public string? Details { get; set; }
        public string? PPageRef { get; set; }
        public string? PPageLocation { get; set; }
        public string? DegInquiry { get; set; }
        public string? DegResponse { get; set; }
        public string? Status { get; set; }
    }
}
