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
    public sealed class OEMPositionStatementsView : UserControl
    {
        private TextBox? _searchBox;
        private StackPanel? _contentPanel;
        private ScrollViewer? _scrollViewer;
        private TextBlock? _resultsCountText;
        private ComboBox? _categoryFilter;
        private ComboBox? _oemFilter;

        private List<OEMStatement> _allStatements = new();
        private List<OEMStatement> _filteredStatements = new();
        private string _selectedCategory = "All Categories";
        private string _selectedOem = "All OEMs";

        public OEMPositionStatementsView()
        {
            LoadStatements();
            BuildUI();
            FilterContent("");
        }

        public void SearchFor(string searchTerm)
        {
            if (_searchBox != null)
                _searchBox.Text = searchTerm;
            FilterContent(searchTerm);
        }

        private void LoadStatements()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Data", "OEMPositionStatements.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<OEMStatementsData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (data?.Statements != null)
                    {
                        _allStatements = data.Statements.OrderBy(s => s.Oem).ThenBy(s => s.Category).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OEMPositionStatementsView] Error loading: {ex.Message}");
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
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filters
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Results count
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

            // Header
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 100, 160)),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(6)
            };
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon { Glyph = "\uE8D7", FontSize = 20, Foreground = new SolidColorBrush(Colors.White) });
            headerStack.Children.Add(new TextBlock
            {
                Text = "OEM Position Statements",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Official manufacturer requirements for collision repair",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Search
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
                PlaceholderText = "Search OEM position statements (e.g., 'scanning', 'calibration', 'Honda', 'structural'...)",
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

            // Filters row
            var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 8) };

            // Category filter
            filterPanel.Children.Add(new TextBlock
            {
                Text = "Category:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _categoryFilter = new ComboBox { Width = 180, FontSize = 12 };
            _categoryFilter.Items.Add("All Categories");
            var categories = _allStatements.Select(s => s.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c);
            foreach (var cat in categories)
                _categoryFilter.Items.Add(cat);
            _categoryFilter.SelectedIndex = 0;
            _categoryFilter.SelectionChanged += (s, e) =>
            {
                _selectedCategory = _categoryFilter.SelectedItem?.ToString() ?? "All Categories";
                FilterContent(_searchBox?.Text ?? "");
            };
            filterPanel.Children.Add(_categoryFilter);

            // OEM filter
            filterPanel.Children.Add(new TextBlock
            {
                Text = "OEM:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });

            _oemFilter = new ComboBox { Width = 220, FontSize = 12 };
            _oemFilter.Items.Add("All OEMs");
            var oems = _allStatements.Select(s => s.Oem).Where(o => !string.IsNullOrEmpty(o)).Distinct().OrderBy(o => o);
            foreach (var oem in oems)
                _oemFilter.Items.Add(oem);
            _oemFilter.SelectedIndex = 0;
            _oemFilter.SelectionChanged += (s, e) =>
            {
                _selectedOem = _oemFilter.SelectedItem?.ToString() ?? "All OEMs";
                FilterContent(_searchBox?.Text ?? "");
            };
            filterPanel.Children.Add(_oemFilter);

            // Resource links
            var oem1stopLink = new HyperlinkButton
            {
                Content = "OEM1Stop.com",
                NavigateUri = new Uri("https://www.oem1stop.com"),
                FontSize = 11,
                Margin = new Thickness(20, 0, 0, 0)
            };
            ToolTipService.SetToolTip(oem1stopLink, "Central source for OEM position statements");
            filterPanel.Children.Add(oem1stopLink);

            var icarLink = new HyperlinkButton
            {
                Content = "I-CAR RTS",
                NavigateUri = new Uri("https://rts.i-car.com"),
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0)
            };
            ToolTipService.SetToolTip(icarLink, "I-CAR Repairability Technical Support");
            filterPanel.Children.Add(icarLink);

            Grid.SetRow(filterPanel, 2);
            mainGrid.Children.Add(filterPanel);

            // Results count
            _resultsCountText = new TextBlock
            {
                Text = "0 statements",
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

            _filteredStatements = _allStatements.Where(s =>
            {
                if (_selectedCategory != "All Categories" && s.Category != _selectedCategory)
                    return false;
                if (_selectedOem != "All OEMs" && s.Oem != _selectedOem)
                    return false;

                if (string.IsNullOrEmpty(search)) return true;

                return s.Oem?.ToLower().Contains(search) == true ||
                       s.Title?.ToLower().Contains(search) == true ||
                       s.Summary?.ToLower().Contains(search) == true ||
                       s.Category?.ToLower().Contains(search) == true ||
                       s.FullText?.ToLower().Contains(search) == true ||
                       s.KeyPoints?.Any(k => k.ToLower().Contains(search)) == true;
            }).ToList();

            RefreshContent();
        }

        private void RefreshContent()
        {
            if (_contentPanel == null) return;
            _contentPanel.Children.Clear();

            if (_resultsCountText != null)
                _resultsCountText.Text = $"{_filteredStatements.Count} statements";

            if (_filteredStatements.Count == 0 && !string.IsNullOrWhiteSpace(_searchBox?.Text))
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = $"No results for '{_searchBox!.Text}'",
                    FontSize = 13,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                    Margin = new Thickness(8, 16, 8, 8)
                });
                return;
            }

            // Group by OEM
            var groups = _filteredStatements.GroupBy(s => s.Oem ?? "Unknown").OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                // OEM group header
                var groupHeader = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 70)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 8, 0, 4)
                };
                var groupHeaderStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                groupHeaderStack.Children.Add(new FontIcon
                {
                    Glyph = "\uE804",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
                });
                groupHeaderStack.Children.Add(new TextBlock
                {
                    Text = group.Key,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center
                });
                groupHeaderStack.Children.Add(new TextBlock
                {
                    Text = $"({group.Count()} statement{(group.Count() != 1 ? "s" : "")})",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                groupHeader.Child = groupHeaderStack;
                _contentPanel.Children.Add(groupHeader);

                foreach (var statement in group.OrderBy(s => s.Category))
                {
                    _contentPanel.Children.Add(CreateStatementCard(statement));
                }
            }
        }

        private Expander CreateStatementCard(OEMStatement stmt)
        {
            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            // Header
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            // Category badge
            var catColor = GetCategoryColor(stmt.Category ?? "");
            var catBadge = new Border
            {
                Background = new SolidColorBrush(catColor),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4)
            };
            catBadge.Child = new TextBlock
            {
                Text = stmt.Category ?? "General",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            headerStack.Children.Add(catBadge);

            // Title and date
            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = stmt.Title ?? "Unknown",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            var subInfo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            subInfo.Children.Add(new TextBlock
            {
                Text = stmt.Oem ?? "",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });
            if (!string.IsNullOrEmpty(stmt.DateIssued))
            {
                subInfo.Children.Add(new TextBlock
                {
                    Text = $"Issued: {stmt.DateIssued}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
                });
            }
            titleStack.Children.Add(subInfo);
            headerStack.Children.Add(titleStack);

            expander.Header = headerStack;

            // Content
            var contentStack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };

            // Summary
            if (!string.IsNullOrEmpty(stmt.Summary))
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = stmt.Summary,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                });
            }

            // Key Points
            if (stmt.KeyPoints != null && stmt.KeyPoints.Count > 0)
            {
                var keyPointsBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 50, 40)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 4, 0, 0)
                };
                var kpStack = new StackPanel();
                kpStack.Children.Add(new TextBlock
                {
                    Text = "KEY REQUIREMENTS:",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 150)),
                    Margin = new Thickness(0, 0, 0, 6)
                });
                foreach (var point in stmt.KeyPoints)
                {
                    kpStack.Children.Add(new TextBlock
                    {
                        Text = $"  {point}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 255, 200)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }
                keyPointsBorder.Child = kpStack;
                contentStack.Children.Add(keyPointsBorder);
            }

            // Full text (expandable)
            if (!string.IsNullOrEmpty(stmt.FullText))
            {
                var fullTextExpander = new Expander
                {
                    Header = new TextBlock
                    {
                        Text = "Full Position Statement Text",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
                    },
                    Margin = new Thickness(0, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                fullTextExpander.Content = new TextBlock
                {
                    Text = stmt.FullText,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                };
                contentStack.Children.Add(fullTextExpander);
            }

            // Links section
            var linksBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 50)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 4, 0, 0)
            };
            var linksStack = new StackPanel { Spacing = 4 };
            linksStack.Children.Add(new TextBlock
            {
                Text = "DOCUMENTATION & RESOURCES:",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 160, 255)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var linksRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            // OEM tech portal
            if (!string.IsNullOrEmpty(stmt.Link))
            {
                AddLinkButton(linksRow, "OEM Tech Portal", stmt.Link, "\uE774", Color.FromArgb(255, 100, 180, 255));
            }

            // PDF link
            if (!string.IsNullOrEmpty(stmt.PdfLink))
            {
                AddLinkButton(linksRow, "Position Statement PDF", stmt.PdfLink, "\uE8A5", Color.FromArgb(255, 255, 140, 100));
            }

            // I-CAR link
            if (!string.IsNullOrEmpty(stmt.IcarLink))
            {
                AddLinkButton(linksRow, "I-CAR RTS", stmt.IcarLink, "\uE82D", Color.FromArgb(255, 100, 200, 150));
            }

            linksStack.Children.Add(linksRow);

            // Additional links row
            var extraLinksRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
            bool hasExtra = false;

            if (!string.IsNullOrEmpty(stmt.CollisionLink))
            {
                AddLinkButton(extraLinksRow, "Collision Info", stmt.CollisionLink, "\uE774", Color.FromArgb(255, 180, 150, 255));
                hasExtra = true;
            }
            if (!string.IsNullOrEmpty(stmt.AdasLink))
            {
                AddLinkButton(extraLinksRow, "ADAS Info", stmt.AdasLink, "\uE774", Color.FromArgb(255, 255, 200, 100));
                hasExtra = true;
            }
            if (!string.IsNullOrEmpty(stmt.CalibrationLink))
            {
                AddLinkButton(extraLinksRow, "Calibration Guide", stmt.CalibrationLink, "\uE774", Color.FromArgb(255, 200, 200, 100));
                hasExtra = true;
            }
            if (!string.IsNullOrEmpty(stmt.AdasJobAid))
            {
                AddLinkButton(extraLinksRow, "ADAS Job Aid", stmt.AdasJobAid, "\uE8A5", Color.FromArgb(255, 255, 180, 100));
                hasExtra = true;
            }
            if (!string.IsNullOrEmpty(stmt.FreeResources))
            {
                AddLinkButton(extraLinksRow, "Free Resources", stmt.FreeResources, "\uE774", Color.FromArgb(255, 100, 200, 100));
                hasExtra = true;
            }
            if (!string.IsNullOrEmpty(stmt.PositionStatementsPage))
            {
                AddLinkButton(extraLinksRow, "All Statements", stmt.PositionStatementsPage, "\uE774", Color.FromArgb(255, 180, 180, 255));
                hasExtra = true;
            }
            if (!string.IsNullOrEmpty(stmt.AllOemStatements))
            {
                AddLinkButton(extraLinksRow, "All OEM Statements", stmt.AllOemStatements, "\uE774", Color.FromArgb(255, 200, 150, 255));
                hasExtra = true;
            }
            if (!string.IsNullOrEmpty(stmt.AudiStructuralPdf))
            {
                AddLinkButton(extraLinksRow, "Structural PDF", stmt.AudiStructuralPdf, "\uE8A5", Color.FromArgb(255, 255, 160, 100));
                hasExtra = true;
            }
            if (!string.IsNullOrEmpty(stmt.KiaLink))
            {
                AddLinkButton(extraLinksRow, "Kia Tech Info", stmt.KiaLink, "\uE774", Color.FromArgb(255, 100, 180, 255));
                hasExtra = true;
            }

            if (hasExtra)
                linksStack.Children.Add(extraLinksRow);

            linksBorder.Child = linksStack;
            contentStack.Children.Add(linksBorder);

            // Action buttons
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

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
            copyBtn.Click += (s, e) => CopyStatement(stmt);
            buttonRow.Children.Add(copyBtn);

            var copyForEstBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8C8", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)) },
                        new TextBlock { Text = "Copy for Estimate", FontSize = 11 }
                    }
                },
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 55, 75)),
                BorderThickness = new Thickness(0)
            };
            ToolTipService.SetToolTip(copyForEstBtn, "Copy a condensed version suitable for estimate notes");
            copyForEstBtn.Click += (s, e) => CopyForEstimate(stmt);
            buttonRow.Children.Add(copyForEstBtn);

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
            addPdfBtn.Click += (s, e) => AddToPdfQueue(stmt, addPdfBtn);
            buttonRow.Children.Add(addPdfBtn);

            var readAloudBtn = Services.TextToSpeechService.CreateReadAloudButton(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{stmt.Oem} - {stmt.Title}");
                if (!string.IsNullOrEmpty(stmt.Summary)) sb.AppendLine(stmt.Summary);
                if (stmt.KeyPoints?.Count > 0)
                {
                    sb.AppendLine("Key requirements:");
                    foreach (var kp in stmt.KeyPoints) sb.AppendLine(kp);
                }
                return sb.ToString();
            });
            buttonRow.Children.Add(readAloudBtn);

            contentStack.Children.Add(buttonRow);

            expander.Content = contentStack;
            return expander;
        }

        private static void AddLinkButton(StackPanel parent, string label, string url, string glyph, Color iconColor)
        {
            try
            {
                var link = new HyperlinkButton
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new FontIcon { Glyph = glyph, FontSize = 11, Foreground = new SolidColorBrush(iconColor) },
                            new TextBlock { Text = label, FontSize = 11 }
                        }
                    },
                    NavigateUri = new Uri(url),
                    Padding = new Thickness(0)
                };
                ToolTipService.SetToolTip(link, url);
                parent.Children.Add(link);
            }
            catch { /* Invalid URI - skip */ }
        }

        private static Color GetCategoryColor(string category) => category switch
        {
            "Scanning" => Color.FromArgb(255, 60, 130, 180),
            "ADAS Calibration" => Color.FromArgb(255, 180, 120, 50),
            "Repair Procedures" => Color.FromArgb(255, 60, 140, 80),
            _ => Color.FromArgb(255, 100, 100, 100)
        };

        private void CopyStatement(OEMStatement stmt)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {stmt.Oem}: {stmt.Title} ===");
            sb.AppendLine($"Category: {stmt.Category}");
            if (!string.IsNullOrEmpty(stmt.DateIssued))
                sb.AppendLine($"Date Issued: {stmt.DateIssued}");
            sb.AppendLine();
            sb.AppendLine(stmt.Summary);
            sb.AppendLine();

            if (stmt.KeyPoints?.Count > 0)
            {
                sb.AppendLine("KEY REQUIREMENTS:");
                foreach (var kp in stmt.KeyPoints)
                    sb.AppendLine($"  {kp}");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(stmt.FullText))
            {
                sb.AppendLine("FULL TEXT:");
                sb.AppendLine(stmt.FullText);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(stmt.Link))
                sb.AppendLine($"OEM Portal: {stmt.Link}");
            if (!string.IsNullOrEmpty(stmt.PdfLink))
                sb.AppendLine($"PDF: {stmt.PdfLink}");
            if (!string.IsNullOrEmpty(stmt.IcarLink))
                sb.AppendLine($"I-CAR: {stmt.IcarLink}");

            var dataPackage = new DataPackage();
            dataPackage.SetText(sb.ToString());
            Clipboard.SetContent(dataPackage);
        }

        private void CopyForEstimate(OEMStatement stmt)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Per {stmt.Oem} Position Statement ({stmt.DateIssued ?? "current"}):");
            sb.AppendLine(stmt.Summary);

            if (stmt.KeyPoints?.Count > 0)
            {
                sb.AppendLine();
                foreach (var kp in stmt.KeyPoints)
                    sb.AppendLine($"- {kp}");
            }

            if (!string.IsNullOrEmpty(stmt.Link))
                sb.AppendLine($"\nSource: {stmt.Link}");

            var dataPackage = new DataPackage();
            dataPackage.SetText(sb.ToString());
            Clipboard.SetContent(dataPackage);
        }

        private async void AddToPdfQueue(OEMStatement stmt, Button btn)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"OEM: {stmt.Oem}");
            sb.AppendLine($"Category: {stmt.Category}");
            if (!string.IsNullOrEmpty(stmt.DateIssued))
                sb.AppendLine($"Date: {stmt.DateIssued}");
            sb.AppendLine();
            sb.AppendLine(stmt.FullText ?? stmt.Summary ?? "");

            if (stmt.KeyPoints?.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Key Requirements:");
                foreach (var kp in stmt.KeyPoints)
                    sb.AppendLine($"  {kp}");
            }

            if (!string.IsNullOrEmpty(stmt.Link))
                sb.AppendLine($"\nOEM Portal: {stmt.Link}");

            var item = new PdfQueueItem
            {
                Id = $"oem-{stmt.Id}",
                Term = $"{stmt.Oem}: {stmt.Title}",
                Category = "OEM Position Statement",
                Definition = stmt.Summary ?? "",
                Details = sb.ToString(),
                Status = stmt.Category ?? "Position Statement"
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
        private class OEMStatementsData
        {
            public string? Version { get; set; }
            public string? LastUpdated { get; set; }
            public List<OEMStatement>? Statements { get; set; }
        }

        public class OEMStatement
        {
            public string? Id { get; set; }
            public string? Oem { get; set; }
            public string? Title { get; set; }
            public string? Category { get; set; }
            public string? DateIssued { get; set; }
            public string? Summary { get; set; }
            public string? FullText { get; set; }
            public List<string>? KeyPoints { get; set; }
            public string? Link { get; set; }
            public string? PdfLink { get; set; }
            public string? IcarLink { get; set; }
            public string? CollisionLink { get; set; }
            public string? AdasLink { get; set; }
            public string? CalibrationLink { get; set; }
            public string? AdasJobAid { get; set; }
            public string? FreeResources { get; set; }
            public string? PositionStatementsPage { get; set; }
            public string? AllOemStatements { get; set; }
            public string? AudiStructuralPdf { get; set; }
            public string? KiaLink { get; set; }
        }
    }
}
