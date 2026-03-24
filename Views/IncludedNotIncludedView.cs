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
    /// View for browsing Included/Not Included operations reference with P-Pages style UI
    /// </summary>
    public sealed class IncludedNotIncludedView : UserControl
    {
        private TextBox? _searchBox;
        private StackPanel? _contentPanel;
        private ScrollViewer? _scrollViewer;
        private TextBlock? _resultsCountText;
        private ComboBox? _categoryFilter;

        private List<OperationEntry> _allOperations = new();
        private List<OperationEntry> _filteredOperations = new();
        private string _selectedCategory = "All Categories";
        private IncludedNotIncludedData? _data;

        public IncludedNotIncludedView()
        {
            LoadData();
            BuildUI();
            FilterContent("");
        }

        /// <summary>
        /// Public method to search for a specific operation (called from navigation)
        /// </summary>
        public void SearchFor(string searchTerm)
        {
            if (_searchBox != null)
            {
                _searchBox.Text = searchTerm;
            }
            FilterContent(searchTerm);
        }

        private void LoadData()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Data", "IncludedNotIncluded.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    _data = JsonSerializer.Deserialize<IncludedNotIncludedData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (_data?.Operations != null)
                    {
                        _allOperations = _data.Operations.OrderBy(o => o.PartName).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IncludedNotIncludedView] Error loading: {ex.Message}");
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
                Background = new SolidColorBrush(Color.FromArgb(255, 70, 130, 180)), // Steel blue
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(6)
            };
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon { Glyph = "\uE9D9", FontSize = 20, Foreground = new SolidColorBrush(Colors.White) });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Included / Not Included",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Operations Reference - What's In Labor Time vs What Needs to be Added",
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
                PlaceholderText = "Search operations (e.g., 'quarter panel', 'bumper', 'windshield', 'scan', 'ADAS'...)",
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

            // Get unique attachment types as categories
            var categories = _allOperations
                .Select(o => o.AttachmentType ?? "Other")
                .Distinct()
                .OrderBy(c => c);
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

            // Source info
            filterPanel.Children.Add(new TextBlock
            {
                Text = "Sources: CCC/MOTOR P-Pages, Mitchell CEG, DEG Inquiries, SCRS Guide",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0)
            });

            Grid.SetRow(filterPanel, 2);
            mainGrid.Children.Add(filterPanel);

            // Results count
            _resultsCountText = new TextBlock
            {
                Text = "0 operations",
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

            _filteredOperations = _allOperations.Where(o =>
            {
                if (_selectedCategory != "All Categories" && o.AttachmentType != _selectedCategory)
                    return false;

                if (string.IsNullOrEmpty(search)) return true;

                return o.PartName?.ToLower().Contains(search) == true ||
                       o.OperationType?.ToLower().Contains(search) == true ||
                       o.Aliases?.Any(a => a.ToLower().Contains(search)) == true ||
                       o.Included?.Any(i => i.ToLower().Contains(search)) == true ||
                       o.NotIncluded?.Any(n => n.ToLower().Contains(search)) == true ||
                       o.ScrsNotes?.ToLower().Contains(search) == true;
            }).ToList();

            RefreshContent();
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
            foreach (var term in new[] { "quarter panel", "bumper", "door", "refinish" })
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

        private void RefreshContent()
        {
            if (_contentPanel == null) return;
            _contentPanel.Children.Clear();

            if (_resultsCountText != null)
                _resultsCountText.Text = $"{_filteredOperations.Count} operations";

            if (_filteredOperations.Count == 0 && !string.IsNullOrWhiteSpace(_searchBox?.Text))
            {
                _contentPanel.Children.Add(CreateNoResultsPanel(_searchBox!.Text, term =>
                {
                    if (_searchBox != null) _searchBox.Text = term;
                    FilterContent(term);
                }));
                return;
            }

            foreach (var operation in _filteredOperations)
            {
                var card = CreateOperationCard(operation);
                _contentPanel.Children.Add(card);
            }
        }

        private Expander CreateOperationCard(OperationEntry operation)
        {
            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            // Header
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            // Operation type badge
            var typeBadge = new Border
            {
                Background = new SolidColorBrush(GetOperationColor(operation.AttachmentType)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4)
            };
            typeBadge.Child = new TextBlock
            {
                Text = operation.OperationType ?? "Operation",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            headerStack.Children.Add(typeBadge);

            // Part name and attachment type
            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = operation.PartName ?? "Unknown",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = operation.AttachmentType ?? "",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            headerStack.Children.Add(titleStack);

            expander.Header = headerStack;

            // Content
            var contentStack = new StackPanel { Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };

            // Source references
            if (operation.SourceRefs != null)
            {
                var sourcesText = new StringBuilder();
                if (!string.IsNullOrEmpty(operation.SourceRefs.CccMotor))
                    sourcesText.Append($"CCC/MOTOR: {operation.SourceRefs.CccMotor}  ");
                if (!string.IsNullOrEmpty(operation.SourceRefs.Mitchell))
                    sourcesText.Append($"Mitchell: {operation.SourceRefs.Mitchell}  ");
                if (operation.SourceRefs.DegInquiries?.Count > 0)
                    sourcesText.Append($"DEG: {string.Join(", ", operation.SourceRefs.DegInquiries)}");

                if (sourcesText.Length > 0)
                {
                    contentStack.Children.Add(new TextBlock
                    {
                        Text = sourcesText.ToString().Trim(),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
            }

            // Two columns: Included and Not Included (same as P-Pages)
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
            if (operation.Included != null)
            {
                foreach (var item in operation.Included)
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
            if (operation.NotIncluded != null)
            {
                foreach (var item in operation.NotIncluded)
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

            // MET Operations (suggested labor times)
            if (operation.MetOperations != null && operation.MetOperations.Count > 0)
            {
                var metBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 50, 50)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 8, 0, 0)
                };
                var metStack = new StackPanel();
                metStack.Children.Add(new TextBlock
                {
                    Text = "SUGGESTED LABOR (MET Reference):",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 200))
                });
                foreach (var met in operation.MetOperations)
                {
                    var laborType = met.LaborType ?? "B";
                    var laborText = $"{met.Description}: {met.Hours:F1} hrs ({laborType})";
                    metStack.Children.Add(new TextBlock
                    {
                        Text = laborText,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 220, 220)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }
                metBorder.Child = metStack;
                contentStack.Children.Add(metBorder);
            }

            // Triggers (when operation is required)
            if (operation.Triggers != null && operation.Triggers.Count > 0)
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
                foreach (var trigger in operation.Triggers)
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

            // SCRS Notes
            if (!string.IsNullOrEmpty(operation.ScrsNotes))
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
                    Text = "SCRS / IMPORTANT NOTE:",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 255))
                });
                notesStack.Children.Add(new TextBlock
                {
                    Text = operation.ScrsNotes,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 220)),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                });
                notesBorder.Child = notesStack;
                contentStack.Children.Add(notesBorder);
            }

            // Full Article Expander (for PDF)
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
                Text = BuildFullArticle(operation),
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
            copyBtn.Click += (s, e) => CopyOperation(operation);
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
            addPdfBtn.Click += (s, e) => AddToPdfQueue(operation, addPdfBtn);
            buttonRow.Children.Add(addPdfBtn);

            var readAloudBtn = Services.TextToSpeechService.CreateReadAloudButton(() =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{operation.PartName} - {operation.OperationType}");
                if (operation.Included?.Count > 0) { sb.AppendLine("Included:"); foreach (var item in operation.Included) sb.AppendLine(item); }
                if (operation.NotIncluded?.Count > 0) { sb.AppendLine("Not Included:"); foreach (var item in operation.NotIncluded) sb.AppendLine(item); }
                if (operation.MetOperations?.Count > 0) { sb.AppendLine("Labor:"); foreach (var op in operation.MetOperations) sb.AppendLine($"{op.Description}: {op.Hours} hours"); }
                if (!string.IsNullOrEmpty(operation.ScrsNotes)) sb.AppendLine("Notes: " + operation.ScrsNotes);
                return sb.ToString();
            });
            buttonRow.Children.Add(readAloudBtn);

            contentStack.Children.Add(buttonRow);

            expander.Content = contentStack;
            return expander;
        }

        private static Color GetOperationColor(string? attachmentType)
        {
            return attachmentType switch
            {
                "Welded" => Color.FromArgb(255, 180, 80, 40),    // Orange-brown
                "Bolted" => Color.FromArgb(255, 70, 130, 180),   // Steel blue
                "Hinged/Bolted" => Color.FromArgb(255, 100, 100, 180), // Purple-blue
                "Clip/Bolt-on" => Color.FromArgb(255, 80, 150, 80),   // Green
                "Adhesive" => Color.FromArgb(255, 150, 80, 150),      // Purple
                "N/A" => Color.FromArgb(255, 100, 100, 100),          // Gray
                _ => Color.FromArgb(255, 100, 100, 100)               // Default gray
            };
        }

        private static string BuildFullArticle(OperationEntry operation)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{operation.PartName?.ToUpper()} - {operation.OperationType}");
            sb.AppendLine($"Attachment Type: {operation.AttachmentType}");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine();

            if (operation.SourceRefs != null)
            {
                sb.AppendLine("SOURCE REFERENCES:");
                if (!string.IsNullOrEmpty(operation.SourceRefs.CccMotor))
                    sb.AppendLine($"  CCC/MOTOR: {operation.SourceRefs.CccMotor}");
                if (!string.IsNullOrEmpty(operation.SourceRefs.Mitchell))
                    sb.AppendLine($"  Mitchell: {operation.SourceRefs.Mitchell}");
                if (operation.SourceRefs.DegInquiries?.Count > 0)
                    sb.AppendLine($"  DEG Inquiries: {string.Join(", ", operation.SourceRefs.DegInquiries)}");
                sb.AppendLine();
            }

            if (operation.Included != null && operation.Included.Count > 0)
            {
                sb.AppendLine("WHAT IS INCLUDED IN LABOR TIME:");
                foreach (var item in operation.Included)
                    sb.AppendLine($"  + {item}");
                sb.AppendLine();
            }

            if (operation.NotIncluded != null && operation.NotIncluded.Count > 0)
            {
                sb.AppendLine("WHAT IS NOT INCLUDED (Requires Additional Time/Line Items):");
                foreach (var item in operation.NotIncluded)
                    sb.AppendLine($"  - {item}");
                sb.AppendLine();
            }

            if (operation.MetOperations != null && operation.MetOperations.Count > 0)
            {
                sb.AppendLine("SUGGESTED LABOR TIMES (MET Reference):");
                foreach (var met in operation.MetOperations)
                {
                    sb.AppendLine($"  * {met.Description}: {met.Hours:F1} hrs ({met.LaborType})");
                }
                sb.AppendLine();
            }

            if (operation.Triggers != null && operation.Triggers.Count > 0)
            {
                sb.AppendLine("WHEN THIS OPERATION IS REQUIRED:");
                foreach (var trigger in operation.Triggers)
                    sb.AppendLine($"  * {trigger}");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(operation.ScrsNotes))
            {
                sb.AppendLine("SCRS / IMPORTANT NOTE:");
                sb.AppendLine(operation.ScrsNotes);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private void CopyOperation(OperationEntry operation)
        {
            var text = BuildFullArticle(operation);
            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
        }

        private async void AddToPdfQueue(OperationEntry operation, Button btn)
        {
            var fullArticle = BuildFullArticle(operation);

            var item = new PdfQueueItem
            {
                Id = $"incl-{operation.Id}",
                Term = $"{operation.PartName} - {operation.OperationType}",
                Category = "Included/Not Included Reference",
                Definition = operation.AttachmentType ?? "",
                Details = fullArticle,
                Status = "Operations Reference"
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
        private class IncludedNotIncludedData
        {
            public string? Version { get; set; }
            public List<OperationEntry>? Operations { get; set; }
        }

        private class OperationEntry
        {
            public string? Id { get; set; }
            public string? PartName { get; set; }
            public string? OperationType { get; set; }
            public string? AttachmentType { get; set; }
            public List<string>? Aliases { get; set; }
            public SourceReferences? SourceRefs { get; set; }
            public List<string>? Included { get; set; }
            public List<string>? NotIncluded { get; set; }
            public List<MetOperation>? MetOperations { get; set; }
            public List<string>? Triggers { get; set; }
            public string? ScrsNotes { get; set; }
        }

        private class SourceReferences
        {
            public string? CccMotor { get; set; }
            public string? Mitchell { get; set; }
            public List<string>? DegInquiries { get; set; }
        }

        private class MetOperation
        {
            public string? Description { get; set; }
            public double Hours { get; set; }
            public string? LaborType { get; set; }
            public string? Source { get; set; }
            public string? MetRef { get; set; }
        }
    }
}
