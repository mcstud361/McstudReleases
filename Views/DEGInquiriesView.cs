#nullable enable
using System;
using System.Collections.Generic;
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

namespace McStudDesktop.Views
{
    /// <summary>
    /// View for browsing and searching DEG (Database Enhancement Gateway) inquiries
    /// </summary>
    public sealed class DEGInquiriesView : UserControl
    {
        private TextBox? _searchBox;
        private StackPanel? _contentPanel;
        private ScrollViewer? _scrollViewer;
        private TextBlock? _resultsCountText;
        private ComboBox? _categoryFilter;

        private List<DEGInquiry> _allInquiries = new();
        private List<DEGInquiry> _filteredInquiries = new();
        private string _selectedCategory = "All Categories";

        public DEGInquiriesView()
        {
            LoadInquiries();
            BuildUI();
            FilterContent("");
        }

        /// <summary>
        /// Public method to search for a specific inquiry (called from navigation)
        /// </summary>
        public void SearchFor(string searchTerm)
        {
            if (_searchBox != null)
            {
                _searchBox.Text = searchTerm;
            }
            FilterContent(searchTerm);
        }

        /// <summary>
        /// Get DEG inquiry by inquiry number
        /// </summary>
        public DEGInquiry? GetInquiry(string inquiryNumber)
        {
            return _allInquiries.FirstOrDefault(i =>
                i.InquiryNumber?.Equals(inquiryNumber, StringComparison.OrdinalIgnoreCase) == true ||
                i.Id?.Equals(inquiryNumber, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// Get all DEG inquiries (for cross-referencing)
        /// </summary>
        public IReadOnlyList<DEGInquiry> GetAllInquiries() => _allInquiries.AsReadOnly();

        /// <summary>
        /// Find DEG inquiries related to a search term
        /// </summary>
        public List<DEGInquiry> FindRelatedInquiries(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return new List<DEGInquiry>();

            var term = searchTerm.ToLowerInvariant();
            return _allInquiries.Where(i =>
                i.Title?.ToLowerInvariant().Contains(term) == true ||
                i.Question?.ToLowerInvariant().Contains(term) == true ||
                i.Response?.ToLowerInvariant().Contains(term) == true ||
                i.Tags?.Any(t => t.ToLowerInvariant().Contains(term)) == true
            ).ToList();
        }

        private void LoadInquiries()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Data", "DEGInquiries.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<DEGData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (data?.Inquiries != null)
                    {
                        _allInquiries = data.Inquiries.OrderBy(i => i.Title).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEGInquiriesView] Error loading: {ex.Message}");
            }
        }

        private void BuildUI()
        {
            var mainGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18))
            };

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filter
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Results count
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

            // Header
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 80, 150)),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(6)
            };
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon { Glyph = "\uE946", FontSize = 20, Foreground = new SolidColorBrush(Colors.White) });
            headerStack.Children.Add(new TextBlock
            {
                Text = "DEG Inquiries",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Database Enhancement Gateway Official Responses",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

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
                PlaceholderText = "Search DEG inquiries (e.g., 'scan', 'calibration', 'blend', inquiry number...)",
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
            Grid.SetRow(searchBorder, 1);
            mainGrid.Children.Add(searchBorder);

            // Category filter
            var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 8) };
            filterPanel.Children.Add(new TextBlock
            {
                Text = "Category:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _categoryFilter = new ComboBox { Width = 200, FontSize = 12 };
            _categoryFilter.Items.Add("All Categories");
            var categories = _allInquiries.Select(i => i.Category).Distinct().OrderBy(c => c);
            foreach (var cat in categories)
            {
                _categoryFilter.Items.Add(cat);
            }
            _categoryFilter.SelectedIndex = 0;
            _categoryFilter.SelectionChanged += (s, e) =>
            {
                _selectedCategory = _categoryFilter.SelectedItem?.ToString() ?? "All Categories";
                FilterContent(_searchBox?.Text ?? "");
            };
            filterPanel.Children.Add(_categoryFilter);

            Grid.SetRow(filterPanel, 2);
            mainGrid.Children.Add(filterPanel);

            // Results count
            _resultsCountText = new TextBlock
            {
                Text = "0 inquiries",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                Margin = new Thickness(4, 0, 0, 6)
            };
            Grid.SetRow(_resultsCountText, 3);
            mainGrid.Children.Add(_resultsCountText);

            // Content
            _scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _contentPanel = new StackPanel { Spacing = 8 };
            _scrollViewer.Content = _contentPanel;
            Grid.SetRow(_scrollViewer, 4);
            mainGrid.Children.Add(_scrollViewer);

            Content = mainGrid;
        }

        private void FilterContent(string search)
        {
            search = search.ToLower().Trim();

            _filteredInquiries = _allInquiries.Where(i =>
            {
                // Category filter
                if (_selectedCategory != "All Categories" && i.Category != _selectedCategory)
                    return false;

                // Text search
                if (string.IsNullOrEmpty(search)) return true;

                return i.Title?.ToLower().Contains(search) == true ||
                       i.InquiryNumber?.ToLower().Contains(search) == true ||
                       i.Question?.ToLower().Contains(search) == true ||
                       i.Response?.ToLower().Contains(search) == true ||
                       i.Category?.ToLower().Contains(search) == true ||
                       i.Tags?.Any(t => t.ToLower().Contains(search)) == true;
            }).ToList();

            RefreshContent();
        }

        private void RefreshContent()
        {
            if (_contentPanel == null) return;
            _contentPanel.Children.Clear();

            if (_resultsCountText != null)
                _resultsCountText.Text = $"{_filteredInquiries.Count} inquiries";

            foreach (var inquiry in _filteredInquiries.Take(50))
            {
                var card = CreateInquiryCard(inquiry);
                _contentPanel.Children.Add(card);
            }

            if (_filteredInquiries.Count > 50)
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = $"... and {_filteredInquiries.Count - 50} more (refine your search)",
                    FontSize = 11,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    Margin = new Thickness(8)
                });
            }
        }

        private Expander CreateInquiryCard(DEGInquiry inquiry)
        {
            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            // Header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Inquiry number badge
            var numBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 10, 0)
            };
            numBadge.Child = new TextBlock
            {
                Text = $"#{inquiry.InquiryNumber}",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Grid.SetColumn(numBadge, 0);
            headerGrid.Children.Add(numBadge);

            // Title
            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = inquiry.Title ?? "Unknown",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = inquiry.Category ?? "",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            // P-Page badge
            if (!string.IsNullOrEmpty(inquiry.PPageRef))
            {
                var pPageBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 180, 120, 50)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                pPageBadge.Child = new TextBlock
                {
                    Text = inquiry.PPageRef,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                Grid.SetColumn(pPageBadge, 2);
                headerGrid.Children.Add(pPageBadge);
            }

            expander.Header = headerGrid;

            // Content
            var contentStack = new StackPanel { Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };

            // Question
            contentStack.Children.Add(new TextBlock
            {
                Text = "QUESTION",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 150, 255))
            });
            contentStack.Children.Add(new TextBlock
            {
                Text = inquiry.Question,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            });

            // Response
            contentStack.Children.Add(new TextBlock
            {
                Text = "DEG RESPONSE",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 150)),
                Margin = new Thickness(0, 8, 0, 0)
            });
            contentStack.Children.Add(new TextBlock
            {
                Text = inquiry.Response,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            });

            // Key Points
            if (inquiry.KeyPoints != null && inquiry.KeyPoints.Count > 0)
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = "KEY POINTS",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                    Margin = new Thickness(0, 8, 0, 0)
                });

                foreach (var point in inquiry.KeyPoints)
                {
                    contentStack.Children.Add(new TextBlock
                    {
                        Text = $"  - {point}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true
                    });
                }
            }

            // Systems and Date
            var infoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Margin = new Thickness(0, 12, 0, 0) };
            if (inquiry.Systems != null && inquiry.Systems.Count > 0)
            {
                infoRow.Children.Add(new TextBlock
                {
                    Text = $"Systems: {string.Join(", ", inquiry.Systems)}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
                });
            }
            if (!string.IsNullOrEmpty(inquiry.DateResponded))
            {
                infoRow.Children.Add(new TextBlock
                {
                    Text = $"Responded: {inquiry.DateResponded}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
                });
            }
            contentStack.Children.Add(infoRow);

            // Full Article Expander
            var articleExpander = new Expander
            {
                Header = new TextBlock
                {
                    Text = "View Full Article (for PDF)",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
                },
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            var articleText = new TextBox
            {
                Text = inquiry.BuildFullArticle(),
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 10,
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                MinHeight = 200
            };
            articleExpander.Content = articleText;
            contentStack.Children.Add(articleExpander);

            // DEG Reference Panel with search link and copy button
            var degRefPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 40, 55)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var degRefStack = new StackPanel { Spacing = 6 };

            degRefStack.Children.Add(new TextBlock
            {
                Text = "DEG DATABASE REFERENCE:",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });

            // Extract primary inquiry number for URL (handle multi-number format)
            var inquiryNum = inquiry.InquiryNumber ?? "";
            var primaryNumber = inquiryNum.Split(' ', ',')[0].Trim();

            // Copyable inquiry reference
            var refRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            refRow.Children.Add(new TextBlock
            {
                Text = $"Inquiry #{inquiryNum}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
                IsTextSelectionEnabled = true
            });

            // Copy inquiry number button
            var copyNumBtn = new Button
            {
                Content = "Copy #",
                FontSize = 10,
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(0)
            };
            copyNumBtn.Click += (s, e) =>
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(primaryNumber);
                Clipboard.SetContent(dataPackage);
            };
            refRow.Children.Add(copyNumBtn);
            degRefStack.Children.Add(refRow);

            // Links row
            var linksRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };

            // Search on DEGWeb button - links to the database search page
            var degWebLink = new HyperlinkButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE774", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)) },
                        new TextBlock { Text = "Search on degweb.org", FontSize = 11 }
                    }
                },
                NavigateUri = new Uri("https://degweb.org/deg-database/"),
                Padding = new Thickness(0)
            };
            ToolTipService.SetToolTip(degWebLink, $"Opens DEG Database - search for inquiry #{primaryNumber}");
            linksRow.Children.Add(degWebLink);

            // Also link to SCRS DEG page
            var scrsLink = new HyperlinkButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE774", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 255, 100)) },
                        new TextBlock { Text = "SCRS DEG Resources", FontSize = 11 }
                    }
                },
                NavigateUri = new Uri("https://scrs.com/database-enhancement-gateway/"),
                Padding = new Thickness(0)
            };
            linksRow.Children.Add(scrsLink);

            degRefStack.Children.Add(linksRow);

            // Helpful tip
            degRefStack.Children.Add(new TextBlock
            {
                Text = $"Tip: Search for \"{primaryNumber}\" on degweb.org to view the full official response",
                FontSize = 10,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            degRefPanel.Child = degRefStack;
            contentStack.Children.Add(degRefPanel);

            // Buttons
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };

            var copyBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8C8", FontSize = 12 },
                        new TextBlock { Text = "Copy", FontSize = 11 }
                    }
                },
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                BorderThickness = new Thickness(0)
            };
            copyBtn.Click += (s, e) => CopyInquiry(inquiry);
            buttonRow.Children.Add(copyBtn);

            var addPdfBtn = new Button
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
            addPdfBtn.Click += (s, e) => AddToPdfQueue(inquiry, addPdfBtn);
            buttonRow.Children.Add(addPdfBtn);

            contentStack.Children.Add(buttonRow);

            expander.Content = contentStack;
            return expander;
        }

        private void CopyInquiry(DEGInquiry inquiry)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== DEG Inquiry #{inquiry.InquiryNumber}: {inquiry.Title} ===");
            sb.AppendLine();
            sb.AppendLine("QUESTION:");
            sb.AppendLine(inquiry.Question);
            sb.AppendLine();
            sb.AppendLine("DEG RESPONSE:");
            sb.AppendLine(inquiry.Response);

            if (inquiry.KeyPoints != null && inquiry.KeyPoints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("KEY POINTS:");
                foreach (var point in inquiry.KeyPoints)
                {
                    sb.AppendLine($"  - {point}");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"P-Page Reference: {inquiry.PPageRef}");
            sb.AppendLine($"Systems: {string.Join(", ", inquiry.Systems ?? new List<string>())}");
            sb.AppendLine($"Source: DEG (degweb.org) - Inquiry #{inquiry.InquiryNumber}");

            var dataPackage = new DataPackage();
            dataPackage.SetText(sb.ToString());
            Clipboard.SetContent(dataPackage);
        }

        private async void AddToPdfQueue(DEGInquiry inquiry, Button btn)
        {
            // Build the full article for the PDF
            var fullArticle = inquiry.BuildFullArticle();

            var item = new PdfQueueItem
            {
                Id = $"deg-{inquiry.InquiryNumber}",
                Term = $"DEG #{inquiry.InquiryNumber}: {inquiry.Title}",
                Category = "DEG Inquiry",
                Definition = inquiry.Question ?? "",
                Details = fullArticle,
                PPageRef = inquiry.PPageRef,
                Status = inquiry.Category ?? "DEG Response"
            };

            if (DefinitionsView.PdfQueue.Any(q => q.Id == item.Id))
            {
                btn.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = "Already Added", FontSize = 11 } } };
                btn.Background = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80));
                return;
            }

            DefinitionsView.PdfQueue.Add(item);
            DefinitionsView.RaiseItemAddedToPdfQueue(item);

            var origContent = btn.Content;
            btn.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) }, new TextBlock { Text = "Added!", FontSize = 11 } } };
            btn.Background = new SolidColorBrush(Color.FromArgb(255, 50, 150, 50));
            btn.IsEnabled = false;

            await System.Threading.Tasks.Task.Delay(1500);
            btn.Content = origContent;
            btn.Background = new SolidColorBrush(Color.FromArgb(255, 100, 40, 40));
            btn.IsEnabled = true;
        }

        // Data classes
        private class DEGData
        {
            public string? Version { get; set; }
            public List<DEGInquiry>? Inquiries { get; set; }
        }

        public class DEGInquiry
        {
            public string? Id { get; set; }
            public string? InquiryNumber { get; set; }
            public string? Title { get; set; }
            public string? Category { get; set; }
            public string? Vehicle { get; set; }
            public string? Question { get; set; }
            public string? Response { get; set; }
            public List<string>? KeyPoints { get; set; }
            public List<string>? Systems { get; set; }
            public string? DateResponded { get; set; }
            public string? PPageRef { get; set; }
            public List<string>? Tags { get; set; }

            /// <summary>
            /// Build a complete article text from all DEG inquiry data
            /// </summary>
            public string BuildFullArticle()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"DEG INQUIRY #{InquiryNumber}");
                sb.AppendLine($"Title: {Title}");
                sb.AppendLine($"Category: {Category}");
                if (!string.IsNullOrEmpty(Vehicle))
                    sb.AppendLine($"Vehicle: {Vehicle}");
                sb.AppendLine(new string('=', 50));
                sb.AppendLine();

                sb.AppendLine("QUESTION:");
                sb.AppendLine(Question);
                sb.AppendLine();

                sb.AppendLine("DEG RESPONSE:");
                sb.AppendLine(Response);
                sb.AppendLine();

                if (KeyPoints != null && KeyPoints.Count > 0)
                {
                    sb.AppendLine("KEY POINTS:");
                    foreach (var point in KeyPoints)
                        sb.AppendLine($"  * {point}");
                    sb.AppendLine();
                }

                if (Systems != null && Systems.Count > 0)
                {
                    sb.AppendLine($"SYSTEMS: {string.Join(", ", Systems)}");
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(PPageRef))
                {
                    sb.AppendLine($"P-PAGE REFERENCE: {PPageRef}");
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(DateResponded))
                {
                    sb.AppendLine($"Date Responded: {DateResponded}");
                }

                sb.AppendLine();
                sb.AppendLine("Source: Database Enhancement Gateway (DEG)");

                return sb.ToString();
            }
        }
    }
}
