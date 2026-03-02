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

        // Data
        private List<EstimateLine> _estimateLines = new();
        private List<PatternSuggestion> _suggestions = new();

        // Services
        private readonly EstimateLearningService _learningService;

        // Event for ChatbotView integration
        public event EventHandler<List<GeneratedOperation>>? OnOperationsGenerated;

        public ExcelEstimateBuilder()
        {
            _learningService = EstimateLearningService.Instance;
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
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // Header
            var header = new TextBlock
            {
                Text = "Build Estimate",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Search box
            var searchPanel = CreateSearchPanel();
            Grid.SetRow(searchPanel, 1);
            root.Children.Add(searchPanel);

            // Content: Suggestions | Estimate
            var content = CreateContentPanel();
            Grid.SetRow(content, 2);
            root.Children.Add(content);

            // Footer: Totals + Export
            var footer = CreateFooter();
            Grid.SetRow(footer, 3);
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

            _totalLabor!.Text = $"Labor: {totalLabor:F1}";
            _totalRefinish!.Text = $"Refinish: {totalRefinish:F1}";
            _totalPrice!.Text = $"${totalPrice:F0}";
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
