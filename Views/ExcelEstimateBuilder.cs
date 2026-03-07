#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Simple estimate builder - search learned patterns, add to estimate, export.
    /// </summary>
    public sealed class ExcelEstimateBuilder : UserControl
    {
        // UI Elements
        private TextBox? _searchBox;
        private ListView? _suggestionsView;
        private ListView? _estimateView;
        private TextBlock? _totalLabor;
        private TextBlock? _totalRefinish;
        private TextBlock? _totalPrice;
        private TextBlock? _lineCount;
        private StackPanel? _relatedOpsPanel;

        // Data
        private List<EstimateLine> _estimateLines = new();
        private List<PatternSuggestion> _suggestions = new();

        // Services
        private readonly EstimateLearningService _learningService;
        private readonly EstimateAIAdvisorService _advisorService;

        // AI advisor suggestions
        private StackPanel? _advisorSuggestionsPanel;

        // Event for ChatbotView integration
        public event EventHandler<List<GeneratedOperation>>? OnOperationsGenerated;

        public ExcelEstimateBuilder()
        {
            _learningService = EstimateLearningService.Instance;
            _advisorService = EstimateAIAdvisorService.Instance;
            BuildUI();
        }

        private void BuildUI()
        {
            var root = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                Padding = new Thickness(16)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Related ops
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // AI suggestions
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // Header with help button
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(new TextBlock
            {
                Text = "Build Estimate",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            var buildHelpButton = ContextualHelpButton.Create("build-estimate-subtab");
            Grid.SetColumn(buildHelpButton, 1);
            headerGrid.Children.Add(buildHelpButton);
            Grid.SetRow(headerGrid, 0);
            root.Children.Add(headerGrid);

            // Search box
            var searchPanel = CreateSearchPanel();
            Grid.SetRow(searchPanel, 1);
            root.Children.Add(searchPanel);

            // Content: Suggestions | Estimate
            var content = CreateContentPanel();
            Grid.SetRow(content, 2);
            root.Children.Add(content);

            // Related operations panel (collapsed by default)
            _relatedOpsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(_relatedOpsPanel, 3);
            root.Children.Add(_relatedOpsPanel);

            // AI advisor suggestions (collapsed by default)
            _advisorSuggestionsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(_advisorSuggestionsPanel, 4);
            root.Children.Add(_advisorSuggestionsPanel);

            // Footer: Totals + Export
            var footer = CreateFooter();
            Grid.SetRow(footer, 5);
            root.Children.Add(footer);

            Content = root;
        }

        private Border CreateSearchPanel()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var stack = new StackPanel { Spacing = 8 };

            stack.Children.Add(new TextBlock
            {
                Text = "Search parts from your uploaded estimates:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });

            _searchBox = new TextBox
            {
                PlaceholderText = "Type part name (e.g., bumper, fender, door...)",
                FontSize = 14,
                Padding = new Thickness(12, 10, 12, 10)
            };
            _searchBox.TextChanged += SearchBox_TextChanged;
            _searchBox.KeyDown += SearchBox_KeyDown;
            stack.Children.Add(_searchBox);

            border.Child = stack;
            return border;
        }

        private Grid CreateContentPanel()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); // Spacer
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: Suggestions
            var suggestionsPanel = CreateSuggestionsPanel();
            Grid.SetColumn(suggestionsPanel, 0);
            grid.Children.Add(suggestionsPanel);

            // Right: Your Estimate
            var estimatePanel = CreateEstimatePanel();
            Grid.SetColumn(estimatePanel, 2);
            grid.Children.Add(estimatePanel);

            return grid;
        }

        private Border CreateSuggestionsPanel()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };

            var stack = new StackPanel { Spacing = 8 };

            stack.Children.Add(new TextBlock
            {
                Text = "Suggestions (click to add)",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });

            _suggestionsView = new ListView
            {
                SelectionMode = ListViewSelectionMode.None,
                Background = new SolidColorBrush(Colors.Transparent),
                MaxHeight = 350
            };
            stack.Children.Add(_suggestionsView);

            border.Child = stack;
            return border;
        }

        private Border CreateEstimatePanel()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 50, 40)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };

            var stack = new StackPanel { Spacing = 8 };

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            headerRow.Children.Add(new TextBlock
            {
                Text = "Your Estimate",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
            });

            _lineCount = new TextBlock
            {
                Text = "0 lines",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_lineCount, 1);
            headerRow.Children.Add(_lineCount);

            stack.Children.Add(headerRow);

            _estimateView = new ListView
            {
                SelectionMode = ListViewSelectionMode.None,
                Background = new SolidColorBrush(Colors.Transparent),
                MaxHeight = 350
            };
            stack.Children.Add(_estimateView);

            // Totals
            var totalsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Margin = new Thickness(0, 8, 0, 0)
            };

            _totalLabor = new TextBlock
            {
                Text = "Labor: 0.0",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100))
            };
            totalsPanel.Children.Add(_totalLabor);

            _totalRefinish = new TextBlock
            {
                Text = "Refinish: 0.0",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 255))
            };
            totalsPanel.Children.Add(_totalRefinish);

            _totalPrice = new TextBlock
            {
                Text = "$0",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
            };
            totalsPanel.Children.Add(_totalPrice);

            stack.Children.Add(totalsPanel);

            border.Child = stack;
            return border;
        }

        private Border CreateFooter()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Clear button
            var clearBtn = new Button
            {
                Content = "Clear All",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 40, 40)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            clearBtn.Click += ClearAll_Click;
            grid.Children.Add(clearBtn);

            // Right: Export buttons
            var exportPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var clipBtn = new Button
            {
                Content = "Clip It",
                Padding = new Thickness(20, 10, 20, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            clipBtn.Click += ClipIt_Click;
            exportPanel.Children.Add(clipBtn);

            var typeBtn = new Button
            {
                Content = "Type It",
                Padding = new Thickness(20, 10, 20, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            typeBtn.Click += TypeIt_Click;
            exportPanel.Children.Add(typeBtn);

            var sendToChatBtn = new Button
            {
                Content = "Send to Chat",
                Padding = new Thickness(16, 10, 16, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            sendToChatBtn.Click += SendToChat_Click;
            exportPanel.Children.Add(sendToChatBtn);

            Grid.SetColumn(exportPanel, 1);
            grid.Children.Add(exportPanel);

            border.Child = grid;
            return border;
        }

        #region Search Logic

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = _searchBox?.Text?.Trim() ?? "";
            if (query.Length < 2)
            {
                _suggestions.Clear();
                UpdateSuggestionsDisplay();
                return;
            }

            // Search learned patterns
            _suggestions = SearchPatterns(query);
            UpdateSuggestionsDisplay();
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter && _suggestions.Count > 0)
            {
                AddToEstimate(_suggestions[0]);
            }
        }

        private List<PatternSuggestion> SearchPatterns(string query)
        {
            var results = new List<PatternSuggestion>();
            var queryLower = query.ToLowerInvariant();

            try
            {
                // Get matching patterns from learning service
                var matches = _learningService.SearchPatterns(queryLower, 20);

                foreach (var match in matches)
                {
                    // Skip patterns without meaningful data
                    if (string.IsNullOrEmpty(match.PartName)) continue;

                    var firstOp = match.Operations?.FirstOrDefault();
                    var suggestion = new PatternSuggestion
                    {
                        PartName = match.PartName,
                        OperationType = match.OperationType ?? "",
                        LaborHours = firstOp?.LaborHours ?? 0m,
                        RefinishHours = firstOp?.RefinishHours ?? 0m,
                        Price = firstOp?.Price ?? 0m,
                        Confidence = match.Confidence,
                        TimesUsed = match.ExampleCount
                    };

                    // Only include if it has a real part name and some value
                    if (suggestion.PartName.Length > 2 &&
                        !suggestion.PartName.Contains(":") &&
                        !suggestion.PartName.All(char.IsUpper))
                    {
                        results.Add(suggestion);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BuildEstimate] Search error: {ex.Message}");
            }

            // Sort by confidence/usage
            return results
                .OrderByDescending(r => r.Confidence)
                .ThenByDescending(r => r.TimesUsed)
                .Take(15)
                .ToList();
        }

        #endregion

        #region Display Updates

        private void UpdateSuggestionsDisplay()
        {
            if (_suggestionsView == null) return;

            _suggestionsView.Items.Clear();

            if (_suggestions.Count == 0)
            {
                _suggestionsView.Items.Add(new TextBlock
                {
                    Text = "Type to search...",
                    FontSize = 12,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    Margin = new Thickness(0, 8, 0, 8)
                });
                return;
            }

            foreach (var suggestion in _suggestions)
            {
                var item = CreateSuggestionItem(suggestion);
                _suggestionsView.Items.Add(item);
            }
        }

        private Border CreateSuggestionItem(PatternSuggestion suggestion)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 2, 0, 2),
                Tag = suggestion
            };

            // Make clickable
            border.PointerPressed += (s, e) =>
            {
                if (border.Tag is PatternSuggestion sug)
                    AddToEstimate(sug);
            };
            border.PointerEntered += (s, e) =>
                border.Background = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70));
            border.PointerExited += (s, e) =>
                border.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Part name + operation type
            var nameStack = new StackPanel();
            nameStack.Children.Add(new TextBlock
            {
                Text = suggestion.PartName,
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            if (!string.IsNullOrEmpty(suggestion.OperationType))
            {
                nameStack.Children.Add(new TextBlock
                {
                    Text = suggestion.OperationType,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
                });
            }
            grid.Children.Add(nameStack);

            // Right: Hours/price
            var valueText = suggestion.LaborHours > 0 ? $"{suggestion.LaborHours:F1}h" :
                           suggestion.RefinishHours > 0 ? $"{suggestion.RefinishHours:F1}r" :
                           suggestion.Price > 0 ? $"${suggestion.Price:F0}" : "";

            var valueBlock = new TextBlock
            {
                Text = valueText,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);

            border.Child = grid;
            return border;
        }

        private void UpdateEstimateDisplay()
        {
            if (_estimateView == null) return;

            _estimateView.Items.Clear();

            if (_estimateLines.Count == 0)
            {
                _estimateView.Items.Add(new TextBlock
                {
                    Text = "Click suggestions to add lines",
                    FontSize = 12,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    Margin = new Thickness(0, 8, 0, 8)
                });
            }
            else
            {
                for (int i = 0; i < _estimateLines.Count; i++)
                {
                    var item = CreateEstimateLineItem(_estimateLines[i], i);
                    _estimateView.Items.Add(item);
                }
            }

            // Update totals
            var totalLabor = _estimateLines.Sum(l => l.LaborHours);
            var totalRefinish = _estimateLines.Sum(l => l.RefinishHours);
            var totalPrice = _estimateLines.Sum(l => l.Price);

            _totalLabor!.Text = totalLabor > 0 ? $"Labor: {totalLabor:F1}" : "Labor: -";
            _totalRefinish!.Text = totalRefinish > 0 ? $"Refinish: {totalRefinish:F1}" : "Refinish: -";
            _totalPrice!.Text = totalPrice > 0 ? $"${totalPrice:F0}" : "-";
            _lineCount!.Text = $"{_estimateLines.Count} line{(_estimateLines.Count == 1 ? "" : "s")}";
        }

        private Border CreateEstimateLineItem(EstimateLine line, int index)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 60, 50)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Description
            var desc = $"{line.PartName}";
            if (!string.IsNullOrEmpty(line.OperationType))
                desc += $" ({line.OperationType})";

            grid.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            // Value
            var valueText = line.LaborHours > 0 ? $"{line.LaborHours:F1}h" :
                           line.RefinishHours > 0 ? $"{line.RefinishHours:F1}r" :
                           line.Price > 0 ? $"${line.Price:F0}" : "";

            var valueBlock = new TextBlock
            {
                Text = valueText,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 8, 0)
            };
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);

            // Remove button
            var removeBtn = new Button
            {
                Content = "×",
                FontSize = 14,
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 100, 100)),
                Tag = index
            };
            removeBtn.Click += RemoveLine_Click;
            Grid.SetColumn(removeBtn, 2);
            grid.Children.Add(removeBtn);

            border.Child = grid;
            return border;
        }

        #endregion

        #region Actions

        private void AddToEstimate(PatternSuggestion suggestion)
        {
            var line = new EstimateLine
            {
                PartName = suggestion.PartName,
                OperationType = suggestion.OperationType,
                LaborHours = suggestion.LaborHours,
                RefinishHours = suggestion.RefinishHours,
                Price = suggestion.Price
            };

            _estimateLines.Add(line);
            UpdateEstimateDisplay();
            ShowRelatedOperations(suggestion.PartName, suggestion.OperationType);
            ShowAdvisorSuggestions(suggestion.PartName, suggestion.OperationType);
        }

        private void ShowRelatedOperations(string partName, string operationType)
        {
            if (_relatedOpsPanel == null) return;

            var related = _learningService.GetRelatedOperations(partName, operationType, 8);

            // Filter out operations already on the estimate
            var existingKeys = _estimateLines
                .Select(l => $"{l.PartName}|{l.OperationType}".ToLowerInvariant())
                .ToHashSet();
            related = related
                .Where(r => !existingKeys.Contains($"{r.PartName}|{r.OperationType}".ToLowerInvariant()))
                .ToList();

            _relatedOpsPanel.Children.Clear();
            if (related.Count == 0)
            {
                _relatedOpsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // Header
            _relatedOpsPanel.Children.Add(new TextBlock
            {
                Text = "Also typically on this estimate:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Chips wrap panel
            var wrapGrid = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };

            foreach (var entry in related)
            {
                wrapGrid.Children.Add(CreateRelatedOpChip(entry));
            }

            _relatedOpsPanel.Children.Add(new ScrollViewer
            {
                Content = wrapGrid,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 50
            });

            _relatedOpsPanel.Visibility = Visibility.Visible;
        }

        private Border CreateRelatedOpChip(CoOccurrenceEntry entry)
        {
            var rateText = $"{entry.CoOccurrenceRate:P0}";
            var hoursText = entry.AvgLaborHours > 0 ? $" {entry.AvgLaborHours:F1}h" : "";
            var refinText = entry.AvgRefinishHours > 0 ? $" R:{entry.AvgRefinishHours:F1}h" : "";

            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                Child = new TextBlock
                {
                    Text = $"{entry.PartName} {entry.OperationType} ({rateText}{hoursText}{refinText})",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
                }
            };

            chip.PointerEntered += (s, e) =>
            {
                chip.Background = new SolidColorBrush(Color.FromArgb(255, 65, 65, 65));
                chip.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
            };
            chip.PointerExited += (s, e) =>
            {
                chip.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
                chip.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80));
            };
            chip.Tapped += (s, e) =>
            {
                AddToEstimate(new PatternSuggestion
                {
                    PartName = entry.PartName,
                    OperationType = entry.OperationType,
                    LaborHours = entry.AvgLaborHours,
                    RefinishHours = entry.AvgRefinishHours,
                    Price = entry.AvgPrice,
                    Confidence = entry.CoOccurrenceRate
                });
            };

            return chip;
        }

        private void ShowAdvisorSuggestions(string partName, string operationType)
        {
            if (_advisorSuggestionsPanel == null) return;
            _advisorSuggestionsPanel.Children.Clear();

            try
            {
                // Track all entered operations for context
                var enteredTypes = _estimateLines.Select(l => l.OperationType).ToList();
                var suggestion = _advisorService.GetProactiveSuggestions(partName, operationType, enteredTypes, null);
                if (suggestion == null || suggestion.PatternSuggestions.Count == 0)
                {
                    _advisorSuggestionsPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                // Filter out ops already on the estimate
                var existingKeys = _estimateLines
                    .Select(l => $"{l.PartName}|{l.OperationType}".ToLowerInvariant())
                    .ToHashSet();
                var newSuggestions = suggestion.PatternSuggestions
                    .Where(s => !existingKeys.Contains($"{s.Description}|{s.OperationType}".ToLowerInvariant()))
                    .ToList();

                if (newSuggestions.Count == 0)
                {
                    _advisorSuggestionsPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                // Header
                _advisorSuggestionsPanel.Children.Add(new TextBlock
                {
                    Text = $"\uD83E\uDDE0 Others who did {partName} {operationType} also added:",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 140)),
                    Margin = new Thickness(0, 0, 0, 4)
                });

                // Suggestion chips
                var wrapPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var sug in newSuggestions.Take(6))
                {
                    var confText = sug.Confidence >= 0.7 ? "HIGH" : sug.Confidence >= 0.4 ? "MED" : "LOW";
                    var hoursText = sug.Hours > 0 ? $" {sug.Hours:F1}h" : "";
                    var timesText = sug.TimesUsed > 0 ? $" x{sug.TimesUsed}" : "";

                    var chip = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 40)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 120, 70)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(10, 4, 10, 4),
                        Child = new TextBlock
                        {
                            Text = $"{sug.Description} ({confText}{hoursText}{timesText})",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 220, 200))
                        }
                    };

                    var captured = sug;
                    chip.PointerEntered += (s, e) =>
                    {
                        chip.Background = new SolidColorBrush(Color.FromArgb(255, 45, 70, 50));
                        chip.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 190, 80));
                    };
                    chip.PointerExited += (s, e) =>
                    {
                        chip.Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 40));
                        chip.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 120, 70));
                    };
                    chip.Tapped += (s, e) =>
                    {
                        AddToEstimate(new PatternSuggestion
                        {
                            PartName = captured.Description.Split(' ').FirstOrDefault() ?? captured.Description,
                            OperationType = captured.OperationType,
                            LaborHours = captured.Hours,
                            Confidence = captured.Confidence
                        });
                    };

                    wrapPanel.Children.Add(chip);
                }

                _advisorSuggestionsPanel.Children.Add(new ScrollViewer
                {
                    Content = wrapPanel,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxHeight = 50
                });

                // Accuracy warning
                if (!string.IsNullOrWhiteSpace(suggestion.AccuracyWarning))
                {
                    _advisorSuggestionsPanel.Children.Add(new TextBlock
                    {
                        Text = $"\u26A0 {suggestion.AccuracyWarning}",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                        Margin = new Thickness(0, 4, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                // Insurer note
                if (!string.IsNullOrWhiteSpace(suggestion.InsurerNote))
                {
                    _advisorSuggestionsPanel.Children.Add(new TextBlock
                    {
                        Text = $"\uD83C\uDFE2 {suggestion.InsurerNote}",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 210)),
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                _advisorSuggestionsPanel.Visibility = Visibility.Visible;
            }
            catch
            {
                _advisorSuggestionsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void RemoveLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int index && index >= 0 && index < _estimateLines.Count)
            {
                _estimateLines.RemoveAt(index);
                UpdateEstimateDisplay();
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            _estimateLines.Clear();
            UpdateEstimateDisplay();
        }

        private void ClipIt_Click(object sender, RoutedEventArgs e)
        {
            if (_estimateLines.Count == 0) return;

            try
            {
                // Add to virtual clipboard
                VirtualClipboardService.Instance.Clear();
                foreach (var line in _estimateLines)
                {
                    VirtualClipboardService.Instance.AddOperation(new VirtualClipboardOp
                    {
                        Description = line.PartName,
                        OperationType = line.OperationType,
                        LaborHours = line.LaborHours,
                        RefinishHours = line.RefinishHours,
                        Price = line.Price
                    }, "BuildEstimate");
                }

                System.Diagnostics.Debug.WriteLine($"[BuildEstimate] Clipped {_estimateLines.Count} lines");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BuildEstimate] Clip error: {ex.Message}");
            }
        }

        private void TypeIt_Click(object sender, RoutedEventArgs e)
        {
            if (_estimateLines.Count == 0) return;

            try
            {
                var lines = _estimateLines.Select(l =>
                {
                    var val = l.LaborHours > 0 ? $"{l.LaborHours:F1}h" :
                              l.RefinishHours > 0 ? $"{l.RefinishHours:F1}r" :
                              l.Price > 0 ? $"${l.Price:F0}" : "";
                    return $"{l.PartName} {l.OperationType} - {val}";
                });

                var text = string.Join("\n", lines);

                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                System.Diagnostics.Debug.WriteLine($"[BuildEstimate] Copied {_estimateLines.Count} lines to clipboard");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BuildEstimate] Type error: {ex.Message}");
            }
        }

        private void SendToChat_Click(object sender, RoutedEventArgs e)
        {
            if (_estimateLines.Count == 0) return;

            // Convert to GeneratedOperation list for chat integration
            var operations = _estimateLines.Select(l => new GeneratedOperation
            {
                Description = l.PartName,
                OperationType = l.OperationType,
                LaborHours = l.LaborHours,
                RefinishHours = l.RefinishHours,
                Price = l.Price
            }).ToList();

            OnOperationsGenerated?.Invoke(this, operations);
        }

        #endregion

        #region Data Classes

        private class PatternSuggestion
        {
            public string PartName { get; set; } = "";
            public string OperationType { get; set; } = "";
            public decimal LaborHours { get; set; }
            public decimal RefinishHours { get; set; }
            public decimal Price { get; set; }
            public double Confidence { get; set; }
            public int TimesUsed { get; set; }
        }

        /// <summary>
        /// Adds operations detected by Screen OCR into the estimate builder.
        /// </summary>
        public void AddOperationsFromOcr(List<McstudDesktop.Models.OcrDetectedOperation> operations)
        {
            foreach (var op in operations)
            {
                _estimateLines.Add(new EstimateLine
                {
                    PartName = op.PartName,
                    OperationType = op.OperationType,
                    LaborHours = op.LaborHours,
                    RefinishHours = op.RefinishHours,
                    Price = op.Price
                });
            }
            UpdateEstimateDisplay();
        }

        private class EstimateLine
        {
            public string PartName { get; set; } = "";
            public string OperationType { get; set; } = "";
            public decimal LaborHours { get; set; }
            public decimal RefinishHours { get; set; }
            public decimal Price { get; set; }
        }

        #endregion
    }
}
