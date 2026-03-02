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
    /// View for browsing CCC/MOTOR P-Pages with included/not included information
    /// </summary>
    public sealed class PPagesView : UserControl
    {
        private TextBox? _searchBox;
        private StackPanel? _contentPanel;
        private ScrollViewer? _scrollViewer;
        private TextBlock? _resultsCountText;
        private ComboBox? _categoryFilter;

        private List<PPageSection> _allSections = new();
        private List<PPageSection> _filteredSections = new();
        private string _selectedCategory = "All Categories";

        public PPagesView()
        {
            LoadSections();
            BuildUI();
            FilterContent("");
        }

        /// <summary>
        /// Public method to search for a specific P-Page (called from navigation)
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
        /// Get P-Page section by reference (e.g., "G38")
        /// </summary>
        public PPageSection? GetSection(string sectionRef)
        {
            return _allSections.FirstOrDefault(s =>
                s.Section?.Equals(sectionRef, StringComparison.OrdinalIgnoreCase) == true ||
                s.Id?.Equals(sectionRef, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// Get all P-Page sections (for cross-referencing)
        /// </summary>
        public IReadOnlyList<PPageSection> GetAllSections() => _allSections.AsReadOnly();

        private void LoadSections()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Data", "PPages.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<PPagesData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (data?.Sections != null)
                    {
                        _allSections = data.Sections.OrderBy(s => s.Section).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PPagesView] Error loading: {ex.Message}");
            }
        }

        private void BuildUI()
        {
            var mainGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18))
            };

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 180, 100, 40)),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(6)
            };
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon { Glyph = "\uE8A1", FontSize = 20, Foreground = new SolidColorBrush(Colors.White) });
            headerStack.Children.Add(new TextBlock
            {
                Text = "P-Pages",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "CCC/MOTOR Guide to Estimating - What's Included vs Not Included",
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
                PlaceholderText = "Search P-Pages (e.g., 'P-9', 'G9', 'P21', 'scan', 'blend'...)",
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

            // Filter
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
            var categories = _allSections.Select(s => s.Category).Distinct().OrderBy(c => c);
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

            // Link to CCC P-Pages
            var linkBtn = new HyperlinkButton
            {
                Content = "CCC/MOTOR Guide",
                NavigateUri = new Uri("https://help.cccis.com/webhelp/motor/gte/guide.htm"),
                FontSize = 11,
                Margin = new Thickness(20, 0, 0, 0)
            };
            ToolTipService.SetToolTip(linkBtn, "Open CCC ONE / MOTOR Guide to Estimating");
            filterPanel.Children.Add(linkBtn);

            // Link to DEG (Database Enhancement Gateway)
            var degLink = new HyperlinkButton
            {
                Content = "DEG Resources",
                NavigateUri = new Uri("https://degweb.org/ccc-motor-pathways/"),
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0)
            };
            ToolTipService.SetToolTip(degLink, "Database Enhancement Gateway - P-Page updates and resources");
            filterPanel.Children.Add(degLink);

            Grid.SetRow(filterPanel, 2);
            mainGrid.Children.Add(filterPanel);

            // Results count
            _resultsCountText = new TextBlock
            {
                Text = "0 sections",
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

            // Normalize P-Page search (handle "P-21", "P21", "p-21", "p21" etc.)
            var normalizedSearch = search.Replace("-", "").Replace(" ", "");

            _filteredSections = _allSections.Where(s =>
            {
                if (_selectedCategory != "All Categories" && s.Category != _selectedCategory)
                    return false;

                if (string.IsNullOrEmpty(search)) return true;

                // Check section number (G9, G10, etc.)
                if (s.Section?.ToLower().Contains(search) == true) return true;

                // Check P-Page reference (P-9, P-10, etc.)
                if (s.PPageRef?.ToLower().Replace("-", "").Contains(normalizedSearch) == true) return true;

                // Check alternate references (P9, P-9, page 9, etc.)
                if (s.AltRefs?.Any(r => r.ToLower().Replace("-", "").Replace(" ", "").Contains(normalizedSearch)) == true) return true;

                // Check title, summary, included/not included, tags
                return s.Title?.ToLower().Contains(search) == true ||
                       s.Summary?.ToLower().Contains(search) == true ||
                       s.Included?.Any(i => i.ToLower().Contains(search)) == true ||
                       s.NotIncluded?.Any(n => n.ToLower().Contains(search)) == true ||
                       s.Tags?.Any(t => t.ToLower().Replace("-", "").Contains(normalizedSearch)) == true;
            }).ToList();

            RefreshContent();
        }

        private void RefreshContent()
        {
            if (_contentPanel == null) return;
            _contentPanel.Children.Clear();

            if (_resultsCountText != null)
                _resultsCountText.Text = $"{_filteredSections.Count} sections";

            foreach (var section in _filteredSections)
            {
                var card = CreateSectionCard(section);
                _contentPanel.Children.Add(card);
            }
        }

        private Expander CreateSectionCard(PPageSection section)
        {
            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            // Header
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            // Section badge - just a display badge (not clickable since we're already viewing it)
            var sectionBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 180, 120, 50)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4)
            };
            sectionBadge.Child = new TextBlock
            {
                Text = section.Section ?? "??",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            headerStack.Children.Add(sectionBadge);

            // Title and category
            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = section.Title ?? "Unknown",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = section.Category ?? "",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            headerStack.Children.Add(titleStack);

            expander.Header = headerStack;

            // Content
            var contentStack = new StackPanel { Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };

            // Summary
            if (!string.IsNullOrEmpty(section.Summary))
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = section.Summary,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                });
            }

            // Two columns: Included and Not Included
            var columnsGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // INCLUDED column
            var includedPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 60, 30)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 4, 0)
            };
            var includedStack = new StackPanel();
            includedStack.Children.Add(new TextBlock
            {
                Text = "INCLUDED",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 100)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            if (section.Included != null)
            {
                foreach (var item in section.Included)
                {
                    includedStack.Children.Add(new TextBlock
                    {
                        Text = $"+ {item}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 255, 180)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }
            }
            includedPanel.Child = includedStack;
            Grid.SetColumn(includedPanel, 0);
            columnsGrid.Children.Add(includedPanel);

            // NOT INCLUDED column
            var notIncludedPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 30, 30)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(4, 0, 0, 0)
            };
            var notIncludedStack = new StackPanel();
            notIncludedStack.Children.Add(new TextBlock
            {
                Text = "NOT INCLUDED",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            if (section.NotIncluded != null)
            {
                foreach (var item in section.NotIncluded)
                {
                    notIncludedStack.Children.Add(new TextBlock
                    {
                        Text = $"- {item}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 180)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }
            }
            notIncludedPanel.Child = notIncludedStack;
            Grid.SetColumn(notIncludedPanel, 1);
            columnsGrid.Children.Add(notIncludedPanel);

            contentStack.Children.Add(columnsGrid);

            // Triggers (when operation is required)
            if (section.Triggers != null && section.Triggers.Count > 0)
            {
                var triggersBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 30)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 8, 0, 0)
                };
                var triggersStack = new StackPanel();
                triggersStack.Children.Add(new TextBlock
                {
                    Text = "WHEN REQUIRED:",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100))
                });
                foreach (var trigger in section.Triggers)
                {
                    triggersStack.Children.Add(new TextBlock
                    {
                        Text = $"* {trigger}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 200, 150)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }
                triggersBorder.Child = triggersStack;
                contentStack.Children.Add(triggersBorder);
            }

            // Labor Guidelines
            if (section.LaborGuidelines != null && section.LaborGuidelines.Count > 0)
            {
                var laborBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 50, 50)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 8, 0, 0)
                };
                var laborStack = new StackPanel();
                laborStack.Children.Add(new TextBlock
                {
                    Text = "LABOR TIME GUIDELINES:",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 200))
                });
                foreach (var kvp in section.LaborGuidelines)
                {
                    var displayText = "";
                    if (kvp.Value is System.Text.Json.JsonElement elem)
                    {
                        if (elem.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var items = new List<string>();
                            foreach (var item in elem.EnumerateArray())
                                items.Add(item.GetString() ?? "");
                            displayText = $"{FormatKeyForDisplay(kvp.Key)}: {string.Join(", ", items)}";
                        }
                        else
                        {
                            displayText = $"{FormatKeyForDisplay(kvp.Key)}: {elem.GetString()}";
                        }
                    }
                    else
                    {
                        displayText = $"{FormatKeyForDisplay(kvp.Key)}: {kvp.Value}";
                    }
                    laborStack.Children.Add(new TextBlock
                    {
                        Text = displayText,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 220, 220)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }
                laborBorder.Child = laborStack;
                contentStack.Children.Add(laborBorder);
            }

            // Notes
            if (!string.IsNullOrEmpty(section.Notes))
            {
                var notesBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 50)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 8, 0, 0)
                };
                var notesStack = new StackPanel();
                notesStack.Children.Add(new TextBlock
                {
                    Text = "IMPORTANT NOTE:",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 255))
                });
                notesStack.Children.Add(new TextBlock
                {
                    Text = section.Notes,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 220)),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                });
                notesBorder.Child = notesStack;
                contentStack.Children.Add(notesBorder);
            }

            // Full Article Expander
            var articleExpander = new Expander
            {
                Header = new TextBlock
                {
                    Text = "View Full Article (for PDF)",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
                },
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            var articleText = new TextBox
            {
                Text = section.BuildFullArticle(),
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

            // View on CCC link - copies P-Page SECTION NUMBER to clipboard then opens site
            var sectionRef = section.Section ?? "";
            var sectionTitle = section.Title ?? "";

            // P-Page section info panel with copyable reference
            var ppageInfoPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 45, 55)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var ppageInfoStack = new StackPanel { Spacing = 6 };

            ppageInfoStack.Children.Add(new TextBlock
            {
                Text = "CCC/MOTOR GUIDE TO ESTIMATING REFERENCE:",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100))
            });

            // Show both G-number and P-Page reference
            var pPageRefText = section.PPageRef ?? $"P-{sectionRef.Replace("G", "")}";

            // Copyable section reference
            var refRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            refRow.Children.Add(new TextBlock
            {
                Text = $"Section {sectionRef} ({pPageRefText}): {sectionTitle}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
                IsTextSelectionEnabled = true
            });

            // Copy section ref button
            var copySectionBtn = new Button
            {
                Content = "Copy Section #",
                FontSize = 10,
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(0)
            };
            copySectionBtn.Click += (s, e) =>
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(sectionRef);
                Clipboard.SetContent(dataPackage);
            };
            refRow.Children.Add(copySectionBtn);
            ppageInfoStack.Children.Add(refRow);

            // Links row
            var linksRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 6, 0, 0) };

            // CCC Link - Open CCC Online Help
            var cccLink = new HyperlinkButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE774", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100)) },
                        new TextBlock { Text = "Open CCC Guide to Estimating", FontSize = 11 }
                    }
                },
                NavigateUri = new Uri("https://help.cccis.com/webhelp/motor/gte/guide.htm"),
                Padding = new Thickness(0)
            };
            ToolTipService.SetToolTip(cccLink, $"Opens CCC Guide to Estimating - use Ctrl+F to search for '{sectionRef}'");
            linksRow.Children.Add(cccLink);

            // Mitchell Link
            if (!string.IsNullOrEmpty(section.MitchellRef))
            {
                linksRow.Children.Add(new HyperlinkButton
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE774", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)) },
                            new TextBlock { Text = $"Mitchell {section.MitchellRef}", FontSize = 11 }
                        }
                    },
                    NavigateUri = new Uri("https://static.mymitchell.com/estimating/"),
                    Padding = new Thickness(0)
                });
            }

            ppageInfoStack.Children.Add(linksRow);

            // Helpful note
            ppageInfoStack.Children.Add(new TextBlock
            {
                Text = $"Tip: Search for \"{sectionRef}\", \"{pPageRefText}\", or \"{sectionTitle}\" in CCC ONE or this app",
                FontSize = 10,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            ppageInfoPanel.Child = ppageInfoStack;
            contentStack.Children.Add(ppageInfoPanel);

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
            copyBtn.Click += (s, e) => CopySection(section);
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
            addPdfBtn.Click += (s, e) => AddToPdfQueue(section, addPdfBtn);
            buttonRow.Children.Add(addPdfBtn);

            contentStack.Children.Add(buttonRow);

            expander.Content = contentStack;
            return expander;
        }

        private static string FormatKeyForDisplay(string key)
        {
            var result = new System.Text.StringBuilder();
            foreach (char c in key)
            {
                if (char.IsUpper(c) && result.Length > 0)
                    result.Append(' ');
                result.Append(result.Length == 0 ? char.ToUpper(c) : c);
            }
            return result.ToString();
        }

        private void CopySection(PPageSection section)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== P-Page {section.Section}: {section.Title} ===");
            sb.AppendLine();
            sb.AppendLine(section.Summary);
            sb.AppendLine();

            if (section.Included != null && section.Included.Count > 0)
            {
                sb.AppendLine("INCLUDED:");
                foreach (var item in section.Included)
                    sb.AppendLine($"  + {item}");
                sb.AppendLine();
            }

            if (section.NotIncluded != null && section.NotIncluded.Count > 0)
            {
                sb.AppendLine("NOT INCLUDED:");
                foreach (var item in section.NotIncluded)
                    sb.AppendLine($"  - {item}");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(section.Notes))
            {
                sb.AppendLine($"NOTE: {section.Notes}");
            }

            sb.AppendLine();
            sb.AppendLine($"Source: CCC/MOTOR Guide to Estimating - {section.Section}");

            var dataPackage = new DataPackage();
            dataPackage.SetText(sb.ToString());
            Clipboard.SetContent(dataPackage);
        }

        private async void AddToPdfQueue(PPageSection section, Button btn)
        {
            // Build the full article for the PDF
            var fullArticle = section.BuildFullArticle();

            var item = new PdfQueueItem
            {
                Id = $"ppage-{section.Section}",
                Term = $"P-Page {section.Section}: {section.Title}",
                Category = "P-Page Reference",
                Definition = section.Summary ?? "",
                Details = fullArticle,
                PPageRef = section.Section,
                Status = section.Category ?? "Reference"
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
        private class PPagesData
        {
            public string? Version { get; set; }
            public List<PPageSection>? Sections { get; set; }
        }

        public class PPageSection
        {
            public string? Id { get; set; }
            public string? Section { get; set; }
            public string? PPageRef { get; set; }
            public List<string>? AltRefs { get; set; }
            public string? Title { get; set; }
            public string? Category { get; set; }
            public string? Summary { get; set; }
            public List<string>? Included { get; set; }
            public List<string>? NotIncluded { get; set; }
            public string? Notes { get; set; }
            public string? MitchellRef { get; set; }
            public List<string>? Tags { get; set; }
            public List<string>? Triggers { get; set; }
            public Dictionary<string, object>? LaborGuidelines { get; set; }

            /// <summary>
            /// Build a complete article text from all P-Page data
            /// </summary>
            public string BuildFullArticle()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"P-PAGE {Section}: {Title}");
                sb.AppendLine($"Category: {Category}");
                sb.AppendLine(new string('=', 50));
                sb.AppendLine();

                if (!string.IsNullOrEmpty(Summary))
                {
                    sb.AppendLine("OVERVIEW:");
                    sb.AppendLine(Summary);
                    sb.AppendLine();
                }

                if (Included != null && Included.Count > 0)
                {
                    sb.AppendLine("WHAT IS INCLUDED IN LABOR TIME:");
                    foreach (var item in Included)
                        sb.AppendLine($"  + {item}");
                    sb.AppendLine();
                }

                if (NotIncluded != null && NotIncluded.Count > 0)
                {
                    sb.AppendLine("WHAT IS NOT INCLUDED (Requires Additional Time):");
                    foreach (var item in NotIncluded)
                        sb.AppendLine($"  - {item}");
                    sb.AppendLine();
                }

                if (Triggers != null && Triggers.Count > 0)
                {
                    sb.AppendLine("WHEN THIS OPERATION IS REQUIRED:");
                    foreach (var trigger in Triggers)
                        sb.AppendLine($"  * {trigger}");
                    sb.AppendLine();
                }

                if (LaborGuidelines != null)
                {
                    sb.AppendLine("LABOR TIME GUIDELINES:");
                    foreach (var kvp in LaborGuidelines)
                    {
                        if (kvp.Value is System.Text.Json.JsonElement elem)
                        {
                            if (elem.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                sb.AppendLine($"  {FormatKey(kvp.Key)}:");
                                foreach (var item in elem.EnumerateArray())
                                    sb.AppendLine($"    - {item.GetString()}");
                            }
                            else
                            {
                                sb.AppendLine($"  {FormatKey(kvp.Key)}: {elem.GetString()}");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"  {FormatKey(kvp.Key)}: {kvp.Value}");
                        }
                    }
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(Notes))
                {
                    sb.AppendLine("IMPORTANT NOTE:");
                    sb.AppendLine(Notes);
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(MitchellRef))
                {
                    sb.AppendLine($"Mitchell Reference: {MitchellRef}");
                }

                return sb.ToString();
            }

            private static string FormatKey(string key)
            {
                // Convert camelCase to Title Case
                var result = new System.Text.StringBuilder();
                foreach (char c in key)
                {
                    if (char.IsUpper(c) && result.Length > 0)
                        result.Append(' ');
                    result.Append(result.Length == 0 ? char.ToUpper(c) : c);
                }
                return result.ToString();
            }
        }
    }
}
