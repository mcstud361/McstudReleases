#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Pattern Explorer view for browsing, searching, and managing learned patterns.
    /// Features:
    /// - Search/filter patterns
    /// - View pattern details and history
    /// - Rollback to previous versions
    /// - Delete patterns
    /// - View confidence and feedback stats
    /// </summary>
    public sealed class PatternExplorerView : UserControl
    {
        private readonly EstimateLearningService _learningService;
        private readonly LearningFeedbackService _feedbackService;
        private readonly PatternIntelligenceService _intelligenceService;

        // UI Elements
        private TextBox? _searchBox;
        private ComboBox? _filterCombo;
        private ListView? _patternsList;
        private TextBlock? _patternCountText;
        private Border? _detailsPanel;

        // Details panel elements
        private TextBlock? _detailPatternKey;
        private TextBlock? _detailPartName;
        private TextBlock? _detailOperationType;
        private TextBlock? _detailConfidence;
        private TextBlock? _detailExampleCount;
        private TextBlock? _detailLastUpdated;
        private TextBlock? _detailVehicleType;
        private TextBlock? _detailAcceptanceRate;
        private StackPanel? _detailOperationsPanel;
        private StackPanel? _detailHistoryPanel;
        private Button? _deleteButton;

        // Current selection
        private string? _selectedPatternKey;
        private List<PatternListItem> _allPatterns = new();

        public PatternExplorerView()
        {
            _learningService = EstimateLearningService.Instance;
            _feedbackService = LearningFeedbackService.Instance;
            _intelligenceService = PatternIntelligenceService.Instance;

            BuildUI();
            LoadPatterns();
        }

        private void BuildUI()
        {
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });

            // === LEFT PANEL - Pattern List ===
            var leftPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 30)),
                Padding = new Thickness(16)
            };

            var leftStack = new StackPanel { Spacing = 12 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE8FD",  // Database icon
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });
            header.Children.Add(new TextBlock
            {
                Text = "Pattern Explorer",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            _patternCountText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            header.Children.Add(_patternCountText);
            leftStack.Children.Add(header);

            // Search and filter row
            var searchRow = new Grid();
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _searchBox = new TextBox
            {
                PlaceholderText = "Search patterns...",
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 40)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            _searchBox.TextChanged += (s, e) => FilterPatterns();
            searchRow.Children.Add(_searchBox);

            _filterCombo = new ComboBox
            {
                Margin = new Thickness(8, 0, 0, 0),
                Width = 120,
                SelectedIndex = 0
            };
            _filterCombo.Items.Add("All");
            _filterCombo.Items.Add("High Confidence");
            _filterCombo.Items.Add("Low Confidence");
            _filterCombo.Items.Add("Stale");
            _filterCombo.Items.Add("Trucks");
            _filterCombo.Items.Add("SUVs");
            _filterCombo.Items.Add("Cars");
            _filterCombo.SelectionChanged += (s, e) => FilterPatterns();
            Grid.SetColumn(_filterCombo, 1);
            searchRow.Children.Add(_filterCombo);

            leftStack.Children.Add(searchRow);

            // Patterns list
            _patternsList = new ListView
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 25)),
                SelectionMode = ListViewSelectionMode.Single
            };
            _patternsList.SelectionChanged += PatternsList_SelectionChanged;
            leftStack.Children.Add(_patternsList);

            leftPanel.Child = leftStack;
            mainGrid.Children.Add(leftPanel);

            // === RIGHT PANEL - Pattern Details ===
            _detailsPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 35)),
                Padding = new Thickness(16),
                Visibility = Visibility.Collapsed
            };
            Grid.SetColumn(_detailsPanel, 1);

            var detailsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var detailsStack = new StackPanel { Spacing = 16 };

            // Pattern key header
            _detailPatternKey = new TextBlock
            {
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap
            };
            detailsStack.Children.Add(_detailPatternKey);

            // Info grid
            var infoGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoGrid.RowDefinitions.Add(new RowDefinition());
            infoGrid.RowDefinitions.Add(new RowDefinition());
            infoGrid.RowDefinitions.Add(new RowDefinition());
            infoGrid.RowDefinitions.Add(new RowDefinition());
            infoGrid.RowDefinitions.Add(new RowDefinition());
            infoGrid.RowDefinitions.Add(new RowDefinition());
            infoGrid.RowDefinitions.Add(new RowDefinition());

            AddInfoRow(infoGrid, 0, "Part Name:", out _detailPartName);
            AddInfoRow(infoGrid, 1, "Operation:", out _detailOperationType);
            AddInfoRow(infoGrid, 2, "Confidence:", out _detailConfidence);
            AddInfoRow(infoGrid, 3, "Examples:", out _detailExampleCount);
            AddInfoRow(infoGrid, 4, "Last Updated:", out _detailLastUpdated);
            AddInfoRow(infoGrid, 5, "Vehicle Type:", out _detailVehicleType);
            AddInfoRow(infoGrid, 6, "Acceptance:", out _detailAcceptanceRate);

            detailsStack.Children.Add(infoGrid);

            // Operations section
            var opsHeader = new TextBlock
            {
                Text = "OPERATIONS",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                Margin = new Thickness(0, 8, 0, 4)
            };
            detailsStack.Children.Add(opsHeader);

            _detailOperationsPanel = new StackPanel
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 30)),
                Padding = new Thickness(8)
            };
            detailsStack.Children.Add(_detailOperationsPanel);

            // History section
            var historyHeader = new TextBlock
            {
                Text = "VERSION HISTORY",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(0, 8, 0, 4)
            };
            detailsStack.Children.Add(historyHeader);

            _detailHistoryPanel = new StackPanel
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 30)),
                Padding = new Thickness(8)
            };
            detailsStack.Children.Add(_detailHistoryPanel);

            // Action buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 0)
            };

            _deleteButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                        new TextBlock { Text = "Delete Pattern", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 20, 20)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 244, 100, 100)),
                Padding = new Thickness(12, 6, 12, 6)
            };
            _deleteButton.Click += DeleteButton_Click;
            buttonPanel.Children.Add(_deleteButton);

            detailsStack.Children.Add(buttonPanel);

            detailsScroll.Content = detailsStack;
            _detailsPanel.Child = detailsScroll;
            mainGrid.Children.Add(_detailsPanel);

            Content = mainGrid;
        }

        private void AddInfoRow(Grid grid, int row, string label, out TextBlock valueText)
        {
            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            };
            Grid.SetRow(labelBlock, row);
            grid.Children.Add(labelBlock);

            valueText = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(valueText, row);
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(valueText);
        }

        private void LoadPatterns()
        {
            _allPatterns.Clear();

            var stats = _learningService.GetStatistics();

            // Get pattern data (would need to extend learning service to expose patterns)
            // For now, create items from available stats
            // This would iterate through _learningService patterns
            System.Diagnostics.Debug.WriteLine($"[PatternExplorer] Loading patterns... Total: {stats.TotalPatterns}");

            FilterPatterns();
        }

        private void FilterPatterns()
        {
            _patternsList!.Items.Clear();

            var searchText = _searchBox?.Text?.ToLowerInvariant() ?? "";
            var filter = _filterCombo?.SelectedItem?.ToString() ?? "All";

            var filtered = _allPatterns.Where(p =>
            {
                // Search filter
                if (!string.IsNullOrEmpty(searchText) &&
                    !p.PatternKey.ToLowerInvariant().Contains(searchText))
                    return false;

                // Type filter
                return filter switch
                {
                    "High Confidence" => p.Confidence >= 0.8,
                    "Low Confidence" => p.Confidence < 0.5,
                    "Stale" => p.IsStale,
                    "Trucks" => p.VehicleType == "truck",
                    "SUVs" => p.VehicleType == "suv",
                    "Cars" => p.VehicleType == "car",
                    _ => true
                };
            }).ToList();

            foreach (var pattern in filtered)
            {
                var item = CreatePatternListItem(pattern);
                _patternsList.Items.Add(item);
            }

            _patternCountText!.Text = $"({filtered.Count} of {_allPatterns.Count})";

            // Show message if no patterns
            if (_allPatterns.Count == 0)
            {
                _patternsList.Items.Add(new TextBlock
                {
                    Text = "No patterns yet. Import estimates to build patterns.",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                    Padding = new Thickness(16)
                });
            }
        }

        private Border CreatePatternListItem(PatternListItem pattern)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 35)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 2, 0, 2),
                Tag = pattern.PatternKey
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftStack = new StackPanel { Spacing = 2 };

            leftStack.Children.Add(new TextBlock
            {
                Text = pattern.PatternKey,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            });

            var infoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            // Confidence badge
            var confColor = pattern.Confidence >= 0.8 ? Color.FromArgb(255, 76, 175, 80) :
                            pattern.Confidence >= 0.5 ? Color.FromArgb(255, 255, 193, 7) :
                            Color.FromArgb(255, 244, 67, 54);
            var confBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, confColor.R, confColor.G, confColor.B)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1)
            };
            confBadge.Child = new TextBlock
            {
                Text = $"{pattern.Confidence:P0}",
                FontSize = 10,
                Foreground = new SolidColorBrush(confColor)
            };
            infoRow.Children.Add(confBadge);

            // Example count
            infoRow.Children.Add(new TextBlock
            {
                Text = $"{pattern.ExampleCount} examples",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });

            // Stale indicator
            if (pattern.IsStale)
            {
                infoRow.Children.Add(new TextBlock
                {
                    Text = "STALE",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
            }

            // Vehicle type
            if (!string.IsNullOrEmpty(pattern.VehicleType))
            {
                infoRow.Children.Add(new TextBlock
                {
                    Text = pattern.VehicleType.ToUpper(),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
                });
            }

            leftStack.Children.Add(infoRow);
            grid.Children.Add(leftStack);

            // Chevron
            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            };
            Grid.SetColumn(chevron, 1);
            grid.Children.Add(chevron);

            border.Child = grid;
            return border;
        }

        private void PatternsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_patternsList!.SelectedItem is Border border && border.Tag is string patternKey)
            {
                _selectedPatternKey = patternKey;
                ShowPatternDetails(patternKey);
            }
        }

        private void ShowPatternDetails(string patternKey)
        {
            _detailsPanel!.Visibility = Visibility.Visible;

            // Find pattern in our list
            var pattern = _allPatterns.FirstOrDefault(p => p.PatternKey == patternKey);
            if (pattern == null) return;

            _detailPatternKey!.Text = pattern.PatternKey;
            _detailPartName!.Text = pattern.PartName;
            _detailOperationType!.Text = pattern.OperationType;
            _detailConfidence!.Text = $"{pattern.Confidence:P0} (effective: {pattern.EffectiveConfidence:P0})";
            _detailExampleCount!.Text = pattern.ExampleCount.ToString();
            _detailLastUpdated!.Text = pattern.LastUpdated.ToString("yyyy-MM-dd HH:mm");
            _detailVehicleType!.Text = pattern.VehicleType ?? "All vehicles";

            // Feedback
            var feedback = _feedbackService.GetFeedbackForPattern(patternKey);
            if (feedback != null && feedback.TimesGenerated > 0)
            {
                _detailAcceptanceRate!.Text = $"{feedback.AcceptanceRate:P0} ({feedback.TimesAccepted}/{feedback.TimesGenerated})";
            }
            else
            {
                _detailAcceptanceRate!.Text = "No feedback data";
            }

            // Operations
            _detailOperationsPanel!.Children.Clear();
            // Would list operations from the pattern

            _detailOperationsPanel.Children.Add(new TextBlock
            {
                Text = "Operations would be listed here",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });

            // History
            _detailHistoryPanel!.Children.Clear();
            var history = _intelligenceService.GetPatternHistory(patternKey);

            if (history.Count > 0)
            {
                foreach (var snapshot in history.Take(5))
                {
                    var historyItem = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    historyItem.Children.Add(new TextBlock
                    {
                        Text = $"v{snapshot.Version}",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
                    });
                    historyItem.Children.Add(new TextBlock
                    {
                        Text = snapshot.SnapshotDate.ToString("MM/dd HH:mm"),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
                    });
                    historyItem.Children.Add(new TextBlock
                    {
                        Text = snapshot.Reason,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
                    });
                    _detailHistoryPanel.Children.Add(historyItem);
                }
            }
            else
            {
                _detailHistoryPanel.Children.Add(new TextBlock
                {
                    Text = "No version history",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedPatternKey)) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Pattern?",
                Content = $"Are you sure you want to delete pattern '{_selectedPatternKey}'? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // Would call learning service to delete pattern
                System.Diagnostics.Debug.WriteLine($"[PatternExplorer] Delete pattern: {_selectedPatternKey}");

                _detailsPanel!.Visibility = Visibility.Collapsed;
                _selectedPatternKey = null;
                LoadPatterns();
            }
        }

        public void RefreshPatterns()
        {
            LoadPatterns();
        }
    }

    /// <summary>
    /// Display item for pattern list.
    /// </summary>
    internal class PatternListItem
    {
        public string PatternKey { get; set; } = "";
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public double Confidence { get; set; }
        public double EffectiveConfidence { get; set; }
        public int ExampleCount { get; set; }
        public DateTime LastUpdated { get; set; }
        public bool IsStale { get; set; }
        public string? VehicleType { get; set; }
    }
}
