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

        /// <summary>
        /// Static helper to raise the OnItemAddedToPdfQueue event from other views
        /// </summary>
        public static void RaiseItemAddedToPdfQueue(PdfQueueItem item)
        {
            OnItemAddedToPdfQueue?.Invoke(null, item);
        }

        private TextBox? _searchBox;
        private StackPanel? _mainContentPanel;
        private ScrollViewer? _scrollViewer;
        private TextBlock? _resultsCountText;

        // PDF Export panel (integrated)
        private Border? _pdfPanel;
        private StackPanel? _pdfSelectedItemsPanel;
        private TextBlock? _pdfCountText;
        private Button? _generatePdfButton;
        private PdfExportService? _pdfService;

        // Definitions data
        private List<DefinitionItem> _allDefinitions = new();
        private List<DefinitionItem> _filteredDefinitions = new();

        // OEM Position Statements data
        private List<OEMPositionStatement> _allStatements = new();
        private List<OEMPositionStatement> _filteredStatements = new();

        // P-Pages and DEG data (for cross-referencing)
        private List<PPagesView.PPageSection> _allPPages = new();
        private List<DEGInquiriesView.DEGInquiry> _allDEGInquiries = new();

        // Section expanders for P-Pages and DEG
        private Expander? _ppagesExpander;
        private Expander? _degExpander;
        private StackPanel? _ppagesContent;
        private StackPanel? _degContent;

        // Section expanders
        private Expander? _definitionsExpander;
        private Expander? _oemStatementsExpander;
        private StackPanel? _definitionsContent;
        private StackPanel? _oemStatementsContent;

        // Filter controls
        private ComboBox? _oemFilterCombo;
        private string _selectedOemFilter = "All OEMs";

        public DefinitionsView()
        {
            _pdfService = new PdfExportService();
            LoadDefinitions();
            LoadOEMStatements();
            LoadPPagesData();
            LoadDEGData();
            BuildUI();
            FilterContent("");
        }

        private void LoadPPagesData()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Data", "PPages.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("sections", out var sectionsArray))
                    {
                        foreach (var section in sectionsArray.EnumerateArray())
                        {
                            var ppage = new PPagesView.PPageSection
                            {
                                Id = section.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Section = section.TryGetProperty("section", out var sec) ? sec.GetString() : "",
                                Title = section.TryGetProperty("title", out var title) ? title.GetString() : "",
                                Category = section.TryGetProperty("category", out var cat) ? cat.GetString() : "",
                                Summary = section.TryGetProperty("summary", out var sum) ? sum.GetString() : "",
                                Notes = section.TryGetProperty("notes", out var notes) ? notes.GetString() : ""
                            };

                            // Load included list
                            if (section.TryGetProperty("included", out var included))
                            {
                                ppage.Included = new List<string>();
                                foreach (var item in included.EnumerateArray())
                                    ppage.Included.Add(item.GetString() ?? "");
                            }

                            // Load notIncluded list
                            if (section.TryGetProperty("notIncluded", out var notIncluded))
                            {
                                ppage.NotIncluded = new List<string>();
                                foreach (var item in notIncluded.EnumerateArray())
                                    ppage.NotIncluded.Add(item.GetString() ?? "");
                            }

                            // Load tags
                            if (section.TryGetProperty("tags", out var tags))
                            {
                                ppage.Tags = new List<string>();
                                foreach (var tag in tags.EnumerateArray())
                                    ppage.Tags.Add(tag.GetString() ?? "");
                            }

                            _allPPages.Add(ppage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefinitionsView] Error loading P-Pages: {ex.Message}");
            }
        }

        private void LoadDEGData()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Data", "DEGInquiries.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("inquiries", out var inquiriesArray))
                    {
                        foreach (var inquiry in inquiriesArray.EnumerateArray())
                        {
                            var deg = new DEGInquiriesView.DEGInquiry
                            {
                                Id = inquiry.TryGetProperty("id", out var id) ? id.GetString() : "",
                                InquiryNumber = inquiry.TryGetProperty("inquiryNumber", out var num) ? num.GetString() : "",
                                Title = inquiry.TryGetProperty("title", out var title) ? title.GetString() : "",
                                Category = inquiry.TryGetProperty("category", out var cat) ? cat.GetString() : "",
                                Question = inquiry.TryGetProperty("question", out var q) ? q.GetString() : "",
                                Response = inquiry.TryGetProperty("response", out var r) ? r.GetString() : "",
                                PPageRef = inquiry.TryGetProperty("pPageRef", out var pref) ? pref.GetString() : ""
                            };

                            // Load key points
                            if (inquiry.TryGetProperty("keyPoints", out var keyPoints))
                            {
                                deg.KeyPoints = new List<string>();
                                foreach (var kp in keyPoints.EnumerateArray())
                                    deg.KeyPoints.Add(kp.GetString() ?? "");
                            }

                            // Load tags
                            if (inquiry.TryGetProperty("tags", out var tags))
                            {
                                deg.Tags = new List<string>();
                                foreach (var tag in tags.EnumerateArray())
                                    deg.Tags.Add(tag.GetString() ?? "");
                            }

                            _allDEGInquiries.Add(deg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefinitionsView] Error loading DEG: {ex.Message}");
            }
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

        private void LoadOEMStatements()
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
                        _allStatements = data.Statements
                            .OrderBy(s => s.Oem)
                            .ThenBy(s => s.Category)
                            .ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefinitionsView] Error loading OEM statements: {ex.Message}");
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
                PlaceholderText = "Search definitions, OEM statements, P-pages...",
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

            // OEM Filter dropdown
            var filterPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 0, 0, 8)
            };

            filterPanel.Children.Add(new TextBlock
            {
                Text = "Filter by OEM:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _oemFilterCombo = new ComboBox
            {
                Width = 180,
                FontSize = 12
            };
            _oemFilterCombo.Items.Add("All OEMs");

            // Add unique OEMs from statements
            var uniqueOems = _allStatements.Select(s => s.Oem).Distinct().OrderBy(o => o);
            foreach (var oem in uniqueOems)
            {
                _oemFilterCombo.Items.Add(oem);
            }
            _oemFilterCombo.SelectedIndex = 0;
            _oemFilterCombo.SelectionChanged += (s, e) =>
            {
                _selectedOemFilter = _oemFilterCombo.SelectedItem?.ToString() ?? "All OEMs";
                FilterContent(_searchBox?.Text ?? "");
            };
            filterPanel.Children.Add(_oemFilterCombo);

            Grid.SetRow(filterPanel, 1);
            mainGrid.Children.Add(filterPanel);

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
            CreatePPagesSection();
            CreateDEGSection();
            CreateOEMStatementsSection();

            _scrollViewer.Content = _mainContentPanel;
            Grid.SetRow(_scrollViewer, 3);
            mainGrid.Children.Add(_scrollViewer);

            Content = mainGrid;
        }

        /// <summary>
        /// Creates the PDF export panel that shows at the top of the Definitions view
        /// </summary>
        private Border CreatePdfPanel()
        {
            var panel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 25, 25)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 120, 60, 60)),
                BorderThickness = new Thickness(1)
            };

            var mainStack = new StackPanel();

            // Header row with title and buttons
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Title with count
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleStack.Children.Add(new FontIcon
            {
                Glyph = "\uE8A5", // PDF icon
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100))
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "PDF Export Queue",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            _pdfCountText = new TextBlock
            {
                Text = "(0 items)",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(_pdfCountText);
            Grid.SetColumn(titleStack, 0);
            headerRow.Children.Add(titleStack);

            // Buttons
            var buttonStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            // Generate PDF button
            _generatePdfButton = new Button
            {
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 150, 50, 50)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4)
            };
            var genContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            genContent.Children.Add(new FontIcon { Glyph = "\uE8A5", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
            genContent.Children.Add(new TextBlock { Text = "Generate PDF", FontSize = 11, Foreground = new SolidColorBrush(Colors.White) });
            _generatePdfButton.Content = genContent;
            _generatePdfButton.Click += GeneratePdf_Click;
            buttonStack.Children.Add(_generatePdfButton);

            // Clear button
            var clearBtn = new Button
            {
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4)
            };
            var clearContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            clearContent.Children.Add(new FontIcon { Glyph = "\uE894", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
            clearContent.Children.Add(new TextBlock { Text = "Clear", FontSize = 11, Foreground = new SolidColorBrush(Colors.White) });
            clearBtn.Content = clearContent;
            clearBtn.Click += ClearPdfQueue_Click;
            buttonStack.Children.Add(clearBtn);

            Grid.SetColumn(buttonStack, 1);
            headerRow.Children.Add(buttonStack);
            mainStack.Children.Add(headerRow);

            // Selected items panel (horizontal wrap)
            _pdfSelectedItemsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 8, 0, 0)
            };
            mainStack.Children.Add(_pdfSelectedItemsPanel);

            // Empty state text
            RefreshPdfPanel();

            panel.Child = mainStack;
            return panel;
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
                Text = "Definitions & P-Pages",
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

        private void CreateOEMStatementsSection()
        {
            _oemStatementsExpander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = false
            };

            // Header with icon and count
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon
            {
                Glyph = "\uE8A7", // Document icon
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100))
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "OEM Position Statements",
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"({_allStatements.Count})",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _oemStatementsExpander.Header = headerStack;

            _oemStatementsContent = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            _oemStatementsExpander.Content = _oemStatementsContent;

            _mainContentPanel!.Children.Add(_oemStatementsExpander);
        }

        private void CreatePPagesSection()
        {
            _ppagesExpander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = false
            };

            // Header with icon and count
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon
            {
                Glyph = "\uE8A5", // Document icon
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100))
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "P-Pages (Database)",
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"({_allPPages.Count})",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _ppagesExpander.Header = headerStack;

            _ppagesContent = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            _ppagesExpander.Content = _ppagesContent;

            _mainContentPanel!.Children.Add(_ppagesExpander);
        }

        private void CreateDEGSection()
        {
            _degExpander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = false
            };

            // Header with icon and count
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon
            {
                Glyph = "\uE8D2", // Question icon
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "DEG Inquiries",
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"({_allDEGInquiries.Count})",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _degExpander.Header = headerStack;

            _degContent = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            _degExpander.Content = _degContent;

            _mainContentPanel!.Children.Add(_degExpander);
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
                    d.PPageRef?.ToLower().Contains(searchLower) == true ||
                    d.DegInquiry?.ToLower().Contains(searchLower) == true
                ).ToList();

            // Filter OEM statements with OEM dropdown
            _filteredStatements = _allStatements
                .Where(s =>
                {
                    // OEM filter
                    if (_selectedOemFilter != "All OEMs" && s.Oem != _selectedOemFilter)
                        return false;

                    // Text search filter
                    if (!string.IsNullOrEmpty(searchLower))
                    {
                        return s.Oem?.ToLower().Contains(searchLower) == true ||
                               s.Title?.ToLower().Contains(searchLower) == true ||
                               s.Category?.ToLower().Contains(searchLower) == true ||
                               s.Summary?.ToLower().Contains(searchLower) == true ||
                               s.FullText?.ToLower().Contains(searchLower) == true;
                    }

                    return true;
                })
                .ToList();

            // Update count
            int totalCount = _filteredDefinitions.Count + _filteredStatements.Count;
            if (_resultsCountText != null)
                _resultsCountText.Text = $"{totalCount} item{(totalCount != 1 ? "s" : "")} ({_filteredDefinitions.Count} definitions, {_filteredStatements.Count} OEM statements)";

            // Rebuild definitions content
            _definitionsContent?.Children.Clear();
            if (_filteredDefinitions.Count == 0)
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

            // Rebuild OEM statements content
            _oemStatementsContent?.Children.Clear();
            if (_filteredStatements.Count == 0)
            {
                _oemStatementsContent?.Children.Add(new TextBlock
                {
                    Text = "No OEM statements match your search.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    Margin = new Thickness(8, 8, 0, 8)
                });
            }
            else
            {
                // Group by category
                var groupedStatements = _filteredStatements.GroupBy(s => s.Category ?? "Other");
                foreach (var group in groupedStatements)
                {
                    // Category header
                    _oemStatementsContent?.Children.Add(new TextBlock
                    {
                        Text = group.Key.ToUpper(),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100)),
                        Margin = new Thickness(8, 12, 0, 4)
                    });

                    foreach (var statement in group)
                    {
                        _oemStatementsContent?.Children.Add(CreateOEMStatementExpander(statement));
                    }
                }
            }

            // Filter P-Pages
            var filteredPPages = string.IsNullOrEmpty(searchLower)
                ? new List<PPagesView.PPageSection>()
                : _allPPages.Where(p =>
                    p.Section?.ToLower().Contains(searchLower) == true ||
                    p.Title?.ToLower().Contains(searchLower) == true ||
                    p.Summary?.ToLower().Contains(searchLower) == true ||
                    p.Notes?.ToLower().Contains(searchLower) == true ||
                    p.Tags?.Any(t => t.ToLower().Contains(searchLower)) == true ||
                    p.Included?.Any(i => i.ToLower().Contains(searchLower)) == true ||
                    p.NotIncluded?.Any(n => n.ToLower().Contains(searchLower)) == true
                ).ToList();

            // Filter DEG Inquiries
            var filteredDEG = string.IsNullOrEmpty(searchLower)
                ? new List<DEGInquiriesView.DEGInquiry>()
                : _allDEGInquiries.Where(d =>
                    d.InquiryNumber?.ToLower().Contains(searchLower) == true ||
                    d.Title?.ToLower().Contains(searchLower) == true ||
                    d.Question?.ToLower().Contains(searchLower) == true ||
                    d.Response?.ToLower().Contains(searchLower) == true ||
                    d.Tags?.Any(t => t.ToLower().Contains(searchLower)) == true
                ).ToList();

            // Rebuild P-Pages content
            _ppagesContent?.Children.Clear();
            if (filteredPPages.Count == 0)
            {
                _ppagesContent?.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(searchLower) ? "Search for a term to see related P-Pages." : "No P-Pages match your search.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    Margin = new Thickness(8, 8, 0, 8)
                });
            }
            else
            {
                foreach (var ppage in filteredPPages)
                {
                    _ppagesContent?.Children.Add(CreatePPageExpander(ppage));
                }
            }

            // Rebuild DEG content
            _degContent?.Children.Clear();
            if (filteredDEG.Count == 0)
            {
                _degContent?.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(searchLower) ? "Search for a term to see related DEG inquiries." : "No DEG inquiries match your search.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    Margin = new Thickness(8, 8, 0, 8)
                });
            }
            else
            {
                foreach (var deg in filteredDEG)
                {
                    _degContent?.Children.Add(CreateDEGExpander(deg));
                }
            }

            // Update total count
            totalCount = _filteredDefinitions.Count + _filteredStatements.Count + filteredPPages.Count + filteredDEG.Count;
            if (_resultsCountText != null)
                _resultsCountText.Text = $"{totalCount} item{(totalCount != 1 ? "s" : "")} ({_filteredDefinitions.Count} definitions, {filteredPPages.Count} P-Pages, {filteredDEG.Count} DEG, {_filteredStatements.Count} OEM)";

            // Auto-expand sections if searching and has results
            if (!string.IsNullOrEmpty(searchLower))
            {
                if (_definitionsExpander != null && _filteredDefinitions.Count > 0)
                    _definitionsExpander.IsExpanded = true;
                if (_ppagesExpander != null && filteredPPages.Count > 0)
                    _ppagesExpander.IsExpanded = true;
                if (_degExpander != null && filteredDEG.Count > 0)
                    _degExpander.IsExpanded = true;
                if (_oemStatementsExpander != null && _filteredStatements.Count > 0)
                    _oemStatementsExpander.IsExpanded = true;
            }
        }

        private Expander CreatePPageExpander(PPagesView.PPageSection ppage)
        {
            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45))
            };

            // Header
            var headerPanel = new StackPanel();
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleRow.Children.Add(new TextBlock
            {
                Text = $"P-Page {ppage.Section}:",
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100))
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = ppage.Title ?? "",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220))
            });
            headerPanel.Children.Add(titleRow);
            headerPanel.Children.Add(new TextBlock
            {
                Text = ppage.Category ?? "",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            expander.Header = headerPanel;

            // Content
            var contentStack = new StackPanel { Spacing = 8, Margin = new Thickness(8) };

            // Summary
            if (!string.IsNullOrEmpty(ppage.Summary))
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = ppage.Summary,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
                });
            }

            // Two columns: Included and Not Included
            var columnsGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // INCLUDED
            var includedPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 60, 30)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 4, 0)
            };
            var includedStack = new StackPanel();
            includedStack.Children.Add(new TextBlock { Text = "INCLUDED", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 100)) });
            if (ppage.Included != null)
            {
                foreach (var item in ppage.Included.Take(5))
                    includedStack.Children.Add(new TextBlock { Text = $"+ {item}", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 255, 180)), TextWrapping = TextWrapping.Wrap });
                if (ppage.Included.Count > 5)
                    includedStack.Children.Add(new TextBlock { Text = $"... and {ppage.Included.Count - 5} more", FontSize = 9, FontStyle = Windows.UI.Text.FontStyle.Italic, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 150)) });
            }
            includedPanel.Child = includedStack;
            Grid.SetColumn(includedPanel, 0);
            columnsGrid.Children.Add(includedPanel);

            // NOT INCLUDED
            var notIncludedPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 30, 30)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(4, 0, 0, 0)
            };
            var notIncludedStack = new StackPanel();
            notIncludedStack.Children.Add(new TextBlock { Text = "NOT INCLUDED", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100)) });
            if (ppage.NotIncluded != null)
            {
                foreach (var item in ppage.NotIncluded.Take(5))
                    notIncludedStack.Children.Add(new TextBlock { Text = $"- {item}", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 180)), TextWrapping = TextWrapping.Wrap });
                if (ppage.NotIncluded.Count > 5)
                    notIncludedStack.Children.Add(new TextBlock { Text = $"... and {ppage.NotIncluded.Count - 5} more", FontSize = 9, FontStyle = Windows.UI.Text.FontStyle.Italic, Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 150)) });
            }
            notIncludedPanel.Child = notIncludedStack;
            Grid.SetColumn(notIncludedPanel, 1);
            columnsGrid.Children.Add(notIncludedPanel);

            contentStack.Children.Add(columnsGrid);

            // Button to open P-Page on SCRS website
            var navButton = new HyperlinkButton
            {
                Content = $"Open P-Page {ppage.Section} on SCRS website →",
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0)
            };
            navButton.Click += (s, e) =>
            {
                try
                {
                    // Open SCRS P-Pages website
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://scrs.com/procedure-pages/",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error opening P-Page URL: {ex.Message}");
                }
            };
            contentStack.Children.Add(navButton);

            expander.Content = contentStack;
            return expander;
        }

        private Expander CreateDEGExpander(DEGInquiriesView.DEGInquiry deg)
        {
            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 45, 55))
            };

            // Header
            var headerPanel = new StackPanel();
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            // DEG number as clickable link to degweb.org
            var degLink = new HyperlinkButton
            {
                Content = $"DEG #{deg.InquiryNumber}:",
                NavigateUri = new Uri($"https://degweb.org/inquiry/{deg.InquiryNumber}"),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                Padding = new Thickness(0)
            };
            ToolTipService.SetToolTip(degLink, $"Open DEG Inquiry #{deg.InquiryNumber} on degweb.org");
            titleRow.Children.Add(degLink);

            titleRow.Children.Add(new TextBlock
            {
                Text = deg.Title ?? "",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerPanel.Children.Add(titleRow);
            headerPanel.Children.Add(new TextBlock
            {
                Text = deg.Category ?? "",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            expander.Header = headerPanel;

            // Content
            var contentStack = new StackPanel { Spacing = 8, Margin = new Thickness(8) };

            // Question
            contentStack.Children.Add(new TextBlock
            {
                Text = "QUESTION:",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            contentStack.Children.Add(new TextBlock
            {
                Text = deg.Question ?? "",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
            });

            // Response (truncated)
            contentStack.Children.Add(new TextBlock
            {
                Text = "DEG RESPONSE:",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(0, 8, 0, 0)
            });
            var response = deg.Response ?? "";
            var truncatedResponse = response.Length > 300 ? response.Substring(0, 300) + "..." : response;
            contentStack.Children.Add(new TextBlock
            {
                Text = truncatedResponse,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 220, 255))
            });

            // Key Points (if any)
            if (deg.KeyPoints != null && deg.KeyPoints.Count > 0)
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = "KEY POINTS:",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                    Margin = new Thickness(0, 8, 0, 0)
                });
                foreach (var point in deg.KeyPoints.Take(3))
                {
                    contentStack.Children.Add(new TextBlock
                    {
                        Text = $"• {point}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 100)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
            }

            // P-Page reference link - opens SCRS website
            if (!string.IsNullOrEmpty(deg.PPageRef))
            {
                var ppageLink = new HyperlinkButton
                {
                    Content = $"Related P-Page: {deg.PPageRef} (SCRS website) →",
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                ppageLink.Click += (s, e) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://scrs.com/procedure-pages/",
                            UseShellExecute = true
                        });
                    }
                    catch { }
                };
                contentStack.Children.Add(ppageLink);
            }

            // Links row
            var linksRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };

            // View on DEGWeb button
            var degWebLink = new HyperlinkButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE774", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)) },
                        new TextBlock { Text = $"View DEG #{deg.InquiryNumber} on degweb.org", FontSize = 11 }
                    }
                },
                NavigateUri = new Uri($"https://degweb.org/inquiry/{deg.InquiryNumber}"),
                Padding = new Thickness(0)
            };
            ToolTipService.SetToolTip(degWebLink, "Open the official DEG inquiry page");
            linksRow.Children.Add(degWebLink);

            contentStack.Children.Add(linksRow);

            // Button to navigate within app to full DEG section
            var navButton = new HyperlinkButton
            {
                Content = $"View in DEG Inquiries tab →",
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0)
            };
            navButton.Click += (s, e) => ReferenceView.Instance?.NavigateToDEGInquiry(deg.InquiryNumber ?? "");
            contentStack.Children.Add(navButton);

            expander.Content = contentStack;
            return expander;
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
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Status indicator
            var statusColor = def.Status == "NOT INCLUDED"
                ? Color.FromArgb(255, 255, 100, 100)
                : Color.FromArgb(255, 100, 255, 100);

            var statusDot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(statusColor),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
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

            // DEG badge if applicable
            if (!string.IsNullOrEmpty(def.DegInquiry))
            {
                var degBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                degBadge.Child = new TextBlock
                {
                    Text = $"DEG {def.DegInquiry.Split(',')[0].Trim()}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                Grid.SetColumn(degBadge, 2);
                headerGrid.Children.Add(degBadge);
            }

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

                    // Make P-Page clickable - opens SCRS website
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
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "https://scrs.com/procedure-pages/",
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    };
                    ToolTipService.SetToolTip(pPageLink, "Click to open SCRS P-Pages website");
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

            // DEG Inquiry section
            if (!string.IsNullOrEmpty(def.DegInquiry))
            {
                var degSection = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 25, 40, 60)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 8, 0, 0)
                };

                var degStack = new StackPanel { Spacing = 6 };

                degStack.Children.Add(new TextBlock
                {
                    Text = $"DEG INQUIRY: {def.DegInquiry}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    IsTextSelectionEnabled = true
                });

                if (!string.IsNullOrEmpty(def.DegResponse))
                {
                    degStack.Children.Add(new TextBlock
                    {
                        Text = def.DegResponse,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true
                    });
                }

                degSection.Child = degStack;
                contentStack.Children.Add(degSection);
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

            contentStack.Children.Add(buttonRow);

            expander.Content = contentStack;
            return expander;
        }

        private Expander CreateOEMStatementExpander(OEMPositionStatement statement)
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
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // OEM badge
            var oemBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            oemBadge.Child = new TextBlock
            {
                Text = statement.Oem ?? "OEM",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            Grid.SetColumn(oemBadge, 0);

            // Title - NO IsTextSelectionEnabled here, it breaks clicking to expand
            var titleText = new TextBlock
            {
                Text = statement.Title ?? "Unknown",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(titleText, 1);

            // Date badge
            if (!string.IsNullOrEmpty(statement.DateIssued))
            {
                var dateBadge = new TextBlock
                {
                    Text = statement.DateIssued,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(dateBadge, 2);
                headerGrid.Children.Add(dateBadge);
            }

            headerGrid.Children.Add(oemBadge);
            headerGrid.Children.Add(titleText);
            expander.Header = headerGrid;

            // Content
            var contentStack = new StackPanel { Spacing = 10, Padding = new Thickness(12) };

            // Summary
            contentStack.Children.Add(new TextBlock
            {
                Text = statement.Summary ?? "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            // Full text
            if (!string.IsNullOrEmpty(statement.FullText))
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = statement.FullText,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                    IsTextSelectionEnabled = true
                });
            }

            // Key Points
            if (statement.KeyPoints != null && statement.KeyPoints.Count > 0)
            {
                var keyPointsSection = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 45, 30)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 8, 0, 0)
                };

                var keyPointsStack = new StackPanel { Spacing = 6 };
                keyPointsStack.Children.Add(new TextBlock
                {
                    Text = "KEY POINTS",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });

                foreach (var point in statement.KeyPoints)
                {
                    var pointStack = new StackPanel { Orientation = Orientation.Horizontal };
                    pointStack.Children.Add(new TextBlock
                    {
                        Text = "\u2022 ",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
                    });
                    pointStack.Children.Add(new TextBlock
                    {
                        Text = point,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true
                    });
                    keyPointsStack.Children.Add(pointStack);
                }

                keyPointsSection.Child = keyPointsStack;
                contentStack.Children.Add(keyPointsSection);
            }

            // Documentation Links Section - Critical for estimators
            var docsSection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 35, 50)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var docsStack = new StackPanel { Spacing = 8 };
            docsStack.Children.Add(new TextBlock
            {
                Text = "OFFICIAL DOCUMENTATION",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 150, 255)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            var linksStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            // PDF Link (most important - direct official document)
            if (!string.IsNullOrEmpty(statement.PdfLink))
            {
                var pdfBtn = new HyperlinkButton
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE8A5", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100)) },
                            new TextBlock { Text = "PDF DOCUMENT", FontSize = 10 }
                        }
                    },
                    NavigateUri = new Uri(statement.PdfLink),
                    Padding = new Thickness(8, 4, 8, 4)
                };
                linksStack.Children.Add(pdfBtn);
            }

            // I-CAR Link
            if (!string.IsNullOrEmpty(statement.IcarLink))
            {
                var icarBtn = new HyperlinkButton
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE7BE", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)) },
                            new TextBlock { Text = "I-CAR RTS", FontSize = 10 }
                        }
                    },
                    NavigateUri = new Uri(statement.IcarLink),
                    Padding = new Thickness(8, 4, 8, 4)
                };
                linksStack.Children.Add(icarBtn);
            }

            // OEM Portal Link
            if (!string.IsNullOrEmpty(statement.Link))
            {
                var oemLinkBtn = new HyperlinkButton
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE774", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)) },
                            new TextBlock { Text = "OEM PORTAL", FontSize = 10 }
                        }
                    },
                    NavigateUri = new Uri(statement.Link),
                    Padding = new Thickness(8, 4, 8, 4)
                };
                linksStack.Children.Add(oemLinkBtn);
            }

            if (linksStack.Children.Count > 0)
            {
                docsStack.Children.Add(linksStack);
                contentStack.Children.Add(docsSection);
                docsSection.Child = docsStack;
            }

            // Button row
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0)
            };

            // Use in Estimate button - formats for professional documentation
            var useInEstimateBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8F3", FontSize = 12 }, // Document icon
                        new TextBlock { Text = "Use in Estimate", FontSize = 11 }
                    }
                },
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 60)),
                BorderThickness = new Thickness(0)
            };
            useInEstimateBtn.Click += (s, e) => CopyForEstimate(statement);
            buttonRow.Children.Add(useInEstimateBtn);

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
            copyBtn.Click += (s, e) => CopyOEMStatementToClipboard(statement);
            buttonRow.Children.Add(copyBtn);

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

            if (!string.IsNullOrEmpty(def.DegInquiry))
            {
                sb.AppendLine();
                sb.AppendLine($"DEG Inquiry: {def.DegInquiry}");
                if (!string.IsNullOrEmpty(def.DegResponse))
                {
                    sb.AppendLine(def.DegResponse);
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Status: {def.Status}");

            var dataPackage = new DataPackage();
            dataPackage.SetText(sb.ToString());
            Clipboard.SetContent(dataPackage);
        }

        /// <summary>
        /// Formats OEM position statement for professional estimate documentation.
        /// Concise, formal format suitable for pasting into estimate notes.
        /// </summary>
        private void CopyForEstimate(OEMPositionStatement statement)
        {
            var sb = new StringBuilder();

            // Header line with OEM and title
            sb.AppendLine($"Per {statement.Oem} Position Statement: \"{statement.Title}\" ({statement.DateIssued})");
            sb.AppendLine();

            // Summary as the main point
            sb.AppendLine(statement.Summary);

            // Add key points as bullet items
            if (statement.KeyPoints != null && statement.KeyPoints.Count > 0)
            {
                sb.AppendLine();
                foreach (var point in statement.KeyPoints)
                {
                    sb.AppendLine($"  - {point}");
                }
            }

            // Documentation references (most important for insurance)
            sb.AppendLine();
            sb.AppendLine("DOCUMENTATION:");

            // PDF link is most valuable - direct proof
            if (!string.IsNullOrEmpty(statement.PdfLink))
            {
                sb.AppendLine($"  Official PDF: {statement.PdfLink}");
            }

            // I-CAR link for industry standard reference
            if (!string.IsNullOrEmpty(statement.IcarLink))
            {
                sb.AppendLine($"  I-CAR RTS: {statement.IcarLink}");
            }

            // OEM Portal for full procedures
            if (!string.IsNullOrEmpty(statement.Link))
            {
                sb.AppendLine($"  OEM Portal: {statement.Link}");
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(sb.ToString().Trim());
            Clipboard.SetContent(dataPackage);

            // Visual feedback - could add a toast notification here
            System.Diagnostics.Debug.WriteLine($"[DefinitionsView] Copied OEM statement for estimate: {statement.Title}");
        }

        private void CopyOEMStatementToClipboard(OEMPositionStatement statement)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {statement.Oem}: {statement.Title} ===");
            sb.AppendLine($"Date Issued: {statement.DateIssued}");
            sb.AppendLine();
            sb.AppendLine("SUMMARY:");
            sb.AppendLine(statement.Summary);
            sb.AppendLine();
            sb.AppendLine("FULL TEXT:");
            sb.AppendLine(statement.FullText);

            if (statement.KeyPoints != null && statement.KeyPoints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("KEY POINTS:");
                foreach (var point in statement.KeyPoints)
                {
                    sb.AppendLine($"  - {point}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("OFFICIAL DOCUMENTATION:");

            if (!string.IsNullOrEmpty(statement.PdfLink))
            {
                sb.AppendLine($"  PDF Document: {statement.PdfLink}");
            }

            if (!string.IsNullOrEmpty(statement.IcarLink))
            {
                sb.AppendLine($"  I-CAR RTS: {statement.IcarLink}");
            }

            if (!string.IsNullOrEmpty(statement.Link))
            {
                sb.AppendLine($"  OEM Portal: {statement.Link}");
            }

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

        private class OEMStatementsData
        {
            public string? Version { get; set; }
            public List<OEMPositionStatement>? Statements { get; set; }
        }

        private class OEMPositionStatement
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
            public string? ScrsLink { get; set; }
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
            OnItemAddedToPdfQueue?.Invoke(this, item);

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
