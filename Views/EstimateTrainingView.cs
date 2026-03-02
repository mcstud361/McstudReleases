#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Training view for teaching the estimate learning system.
    ///
    /// Workflow:
    /// 1. User pastes an estimate (or individual lines)
    /// 2. System parses and shows each line
    /// 3. User maps each line to operations it should generate
    /// 4. System learns the pattern
    /// </summary>
    public class EstimateTrainingView : UserControl
    {
        private readonly EstimateLearningService _learningService;

        // UI Elements
        private TextBox _estimateInput = null!;
        private ListView _parsedLinesList = null!;
        private StackPanel _mappingPanel = null!;
        private TextBlock _statsText = null!;
        private Button _parseButton = null!;
        private Button _trainButton = null!;

        // State
        private List<ExtractedLineData> _parsedLines = new();
        private Dictionary<int, List<GeneratedOperation>> _lineMappings = new();
        private int _selectedLineIndex = -1;

        public EstimateTrainingView()
        {
            _learningService = EstimateLearningService.Instance;
            BuildUI();
            UpdateStats();
        }

        private void BuildUI()
        {
            var mainStack = new StackPanel
            {
                Spacing = 12,
                Padding = new Thickness(12)
            };

            // Header with stats
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Train Estimate Recognition",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Grid.SetColumn(header, 0);

            _statsText = new TextBlock
            {
                Text = "0 patterns learned",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statsText, 1);

            headerGrid.Children.Add(header);
            headerGrid.Children.Add(_statsText);
            mainStack.Children.Add(headerGrid);

            // Instructions
            var instructions = new TextBlock
            {
                Text = "Paste estimate lines below. The system will learn what operations each line should generate.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                TextWrapping = TextWrapping.Wrap
            };
            mainStack.Children.Add(instructions);

            // Input area
            _estimateInput = new TextBox
            {
                PlaceholderText = "Paste estimate text here...\nExample:\nR Front Bumper Cover 1.5 hrs $125.00\nRefinish Front Bumper 2.0 hrs",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 100,
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            mainStack.Children.Add(_estimateInput);

            // Parse button
            _parseButton = new Button
            {
                Content = "Parse Lines",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8, 12, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            _parseButton.Click += ParseButton_Click;
            mainStack.Children.Add(_parseButton);

            // Parsed lines list
            var linesHeader = new TextBlock
            {
                Text = "Parsed Lines (click to map operations)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                Margin = new Thickness(0, 8, 0, 4)
            };
            mainStack.Children.Add(linesHeader);

            _parsedLinesList = new ListView
            {
                MaxHeight = 150,
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                SelectionMode = ListViewSelectionMode.Single
            };
            _parsedLinesList.SelectionChanged += ParsedLinesList_SelectionChanged;
            mainStack.Children.Add(_parsedLinesList);

            // Mapping panel (shown when a line is selected)
            _mappingPanel = new StackPanel
            {
                Spacing = 8,
                Visibility = Visibility.Collapsed,
                Padding = new Thickness(0, 8, 0, 0)
            };
            mainStack.Children.Add(_mappingPanel);

            // Train button
            _trainButton = new Button
            {
                Content = "Save Training Data",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8, 12, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 150, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                IsEnabled = false
            };
            _trainButton.Click += TrainButton_Click;
            mainStack.Children.Add(_trainButton);

            Content = mainStack;
        }

        private void ParseButton_Click(object sender, RoutedEventArgs e)
        {
            var text = _estimateInput.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            _parsedLines.Clear();
            _lineMappings.Clear();
            _parsedLinesList.Items.Clear();

            // Use the new manual line detection parser
            var parsedEstimateLines = _learningService.ParseWithManualLineDetection(text);

            int index = 0;
            string? currentParentPart = null;

            foreach (var parsedLine in parsedEstimateLines)
            {
                // Convert ParsedEstimateLine to ExtractedLineData for compatibility
                var extracted = new ExtractedLineData
                {
                    RawLine = parsedLine.RawLine,
                    PartName = parsedLine.PartName,
                    OperationType = parsedLine.OperationType,
                    Position = parsedLine.Position,
                    Side = parsedLine.Side,
                    LaborHours = parsedLine.LaborHours,
                    RefinishHours = parsedLine.RefinishHours,
                    Price = parsedLine.Price
                };

                _parsedLines.Add(extracted);
                _lineMappings[index] = new List<GeneratedOperation>();

                // Track parent part for manual lines
                if (!parsedLine.IsManualLine && !string.IsNullOrEmpty(parsedLine.PartName))
                {
                    currentParentPart = parsedLine.PartName;
                }

                // Check for existing pattern matches
                var matches = _learningService.FindMatches(parsedLine.RawLine);

                // Create list item
                var itemGrid = new Grid { Padding = new Thickness(8, 6, 8, 6) };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Manual line indicator (# badge)
                if (parsedLine.IsManualLine)
                {
                    var manualBadge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 180, 100, 40)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(4, 1, 4, 1),
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    manualBadge.Child = new TextBlock
                    {
                        Text = "#",
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.White)
                    };
                    Grid.SetColumn(manualBadge, 0);
                    itemGrid.Children.Add(manualBadge);
                }

                var itemStack = new StackPanel();

                // Main text
                var displayText = parsedLine.RawLine.Length > 55 ? parsedLine.RawLine.Substring(0, 55) + "..." : parsedLine.RawLine;
                var itemText = new TextBlock
                {
                    Text = displayText,
                    FontSize = 12,
                    Foreground = parsedLine.IsManualLine
                        ? new SolidColorBrush(Color.FromArgb(255, 255, 200, 140))
                        : new SolidColorBrush(Colors.White)
                };
                itemStack.Children.Add(itemText);

                // Extracted info
                var infoText = new TextBlock
                {
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
                };

                var infoParts = new List<string>();

                if (parsedLine.IsManualLine)
                {
                    infoParts.Add($"→ linked to: {parsedLine.ParentPartName ?? currentParentPart ?? "unknown"}");
                }
                else
                {
                    if (!string.IsNullOrEmpty(extracted.PartName))
                        infoParts.Add(extracted.PartName);
                }

                if (!string.IsNullOrEmpty(extracted.OperationType))
                    infoParts.Add(extracted.OperationType);
                if (extracted.LaborHours > 0)
                    infoParts.Add($"{extracted.LaborHours}h labor");
                if (extracted.RefinishHours > 0)
                    infoParts.Add($"{extracted.RefinishHours}h refinish");

                infoText.Text = infoParts.Count > 0 ? string.Join(" | ", infoParts) : "No data extracted";
                itemStack.Children.Add(infoText);

                Grid.SetColumn(itemStack, 1);
                itemGrid.Children.Add(itemStack);

                // Match indicator / badge column
                Border badge;
                if (parsedLine.IsManualLine)
                {
                    badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 100, 80, 40)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    badge.Child = new TextBlock
                    {
                        Text = "Manual",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.White)
                    };
                }
                else if (matches.Count > 0)
                {
                    badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    badge.Child = new TextBlock
                    {
                        Text = $"{(int)(matches[0].MatchScore * 100)}% match",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.White)
                    };

                    // Pre-fill with matched operations
                    _lineMappings[index] = matches[0].Pattern.Operations.ToList();
                }
                else
                {
                    badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    badge.Child = new TextBlock
                    {
                        Text = "New",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.White)
                    };
                }
                Grid.SetColumn(badge, 2);
                itemGrid.Children.Add(badge);

                _parsedLinesList.Items.Add(itemGrid);
                index++;
            }

            _trainButton.IsEnabled = _parsedLines.Count > 0;

            // Show summary
            var manualCount = parsedEstimateLines.Count(p => p.IsManualLine);
            var partCount = parsedEstimateLines.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
            System.Diagnostics.Debug.WriteLine($"[Training] Parsed {parsedEstimateLines.Count} lines: {partCount} parts, {manualCount} manual lines (#)");
        }

        private void ParsedLinesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedLineIndex = _parsedLinesList.SelectedIndex;

            if (_selectedLineIndex < 0 || _selectedLineIndex >= _parsedLines.Count)
            {
                _mappingPanel.Visibility = Visibility.Collapsed;
                return;
            }

            ShowMappingPanel(_selectedLineIndex);
        }

        private void ShowMappingPanel(int lineIndex)
        {
            _mappingPanel.Children.Clear();
            _mappingPanel.Visibility = Visibility.Visible;

            var line = _parsedLines[lineIndex];
            var operations = _lineMappings[lineIndex];

            // Header
            var headerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            var headerText = new TextBlock
            {
                Text = $"Operations for: {line.PartName} {line.OperationType}",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerStack.Children.Add(headerText);

            var addButton = new Button
            {
                Content = "+ Add Operation",
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50))
            };
            addButton.Click += (s, e) => AddOperation(lineIndex);
            headerStack.Children.Add(addButton);

            _mappingPanel.Children.Add(headerStack);

            // Operation list
            if (operations.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "No operations mapped. Click 'Add Operation' to add one.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                };
                _mappingPanel.Children.Add(emptyText);
            }
            else
            {
                foreach (var op in operations.ToList())
                {
                    var opPanel = CreateOperationEditor(lineIndex, op);
                    _mappingPanel.Children.Add(opPanel);
                }
            }

            // Quick add buttons for common operations
            var quickAddPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var quickLabel = new TextBlock
            {
                Text = "Quick add:",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            quickAddPanel.Children.Add(quickLabel);

            var quickOps = new[] { "R&I", "Repair", "Replace", "Refinish", "Blend", "Clear Coat" };
            foreach (var quickOp in quickOps)
            {
                var btn = new Button
                {
                    Content = quickOp,
                    Padding = new Thickness(6, 2, 6, 2),
                    FontSize = 10,
                    Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                    MinWidth = 0
                };
                var opName = quickOp;
                btn.Click += (s, e) => QuickAddOperation(lineIndex, opName);
                quickAddPanel.Children.Add(btn);
            }

            _mappingPanel.Children.Add(quickAddPanel);
        }

        private Border CreateOperationEditor(int lineIndex, GeneratedOperation op)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 4, 0, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel { Spacing = 4 };

            // Type and Description row
            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var typeCombo = new ComboBox
            {
                Width = 80,
                FontSize = 11,
                SelectedItem = op.OperationType
            };
            typeCombo.Items.Add("R&I");
            typeCombo.Items.Add("Repair");
            typeCombo.Items.Add("Replace");
            typeCombo.Items.Add("Refinish");
            typeCombo.Items.Add("Blend");
            typeCombo.Items.Add("O/H");
            typeCombo.Items.Add("Add");
            typeCombo.SelectedItem = string.IsNullOrEmpty(op.OperationType) ? "R&I" : op.OperationType;
            typeCombo.SelectionChanged += (s, e) =>
            {
                if (typeCombo.SelectedItem != null)
                    op.OperationType = typeCombo.SelectedItem.ToString()!;
            };
            row1.Children.Add(typeCombo);

            var descBox = new TextBox
            {
                Text = op.Description,
                PlaceholderText = "Description",
                Width = 150,
                FontSize = 11
            };
            descBox.TextChanged += (s, e) => op.Description = descBox.Text;
            row1.Children.Add(descBox);

            stack.Children.Add(row1);

            // Hours row
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var laborLabel = new TextBlock { Text = "Labor:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            row2.Children.Add(laborLabel);

            var laborBox = new NumberBox
            {
                Value = (double)op.LaborHours,
                Minimum = 0,
                Maximum = 100,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 70,
                FontSize = 11
            };
            laborBox.ValueChanged += (s, e) => op.LaborHours = (decimal)laborBox.Value;
            row2.Children.Add(laborBox);

            var refLabel = new TextBlock { Text = "Refinish:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            row2.Children.Add(refLabel);

            var refBox = new NumberBox
            {
                Value = (double)op.RefinishHours,
                Minimum = 0,
                Maximum = 100,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 70,
                FontSize = 11
            };
            refBox.ValueChanged += (s, e) => op.RefinishHours = (decimal)refBox.Value;
            row2.Children.Add(refBox);

            stack.Children.Add(row2);

            Grid.SetColumn(stack, 0);
            grid.Children.Add(stack);

            // Delete button
            var deleteBtn = new Button
            {
                Content = "X",
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 10,
                Background = new SolidColorBrush(Color.FromArgb(255, 100, 40, 40)),
                VerticalAlignment = VerticalAlignment.Top
            };
            deleteBtn.Click += (s, e) =>
            {
                _lineMappings[lineIndex].Remove(op);
                ShowMappingPanel(lineIndex);
            };
            Grid.SetColumn(deleteBtn, 1);
            grid.Children.Add(deleteBtn);

            border.Child = grid;
            return border;
        }

        private void AddOperation(int lineIndex)
        {
            var line = _parsedLines[lineIndex];
            var newOp = new GeneratedOperation
            {
                OperationType = line.OperationType ?? "R&I",
                Description = line.PartName ?? "",
                Category = "Part Operations",
                LaborHours = line.LaborHours,
                RefinishHours = line.RefinishHours
            };

            _lineMappings[lineIndex].Add(newOp);
            ShowMappingPanel(lineIndex);
        }

        private void QuickAddOperation(int lineIndex, string opType)
        {
            var line = _parsedLines[lineIndex];
            var newOp = new GeneratedOperation
            {
                OperationType = opType,
                Description = line.PartName ?? "",
                Category = opType == "Refinish" || opType == "Blend" || opType == "Clear Coat"
                    ? "Refinish Operations"
                    : "Part Operations",
                LaborHours = opType == "Refinish" || opType == "Blend" ? 0 : line.LaborHours,
                RefinishHours = opType == "Refinish" || opType == "Blend" ? line.RefinishHours : 0
            };

            _lineMappings[lineIndex].Add(newOp);
            ShowMappingPanel(lineIndex);
        }

        private void TrainButton_Click(object sender, RoutedEventArgs e)
        {
            if (_parsedLines.Count == 0) return;

            // Re-parse with manual line detection to get proper structure
            var text = _estimateInput.Text;
            var parsedEstimateLines = _learningService.ParseWithManualLineDetection(text);

            // Build training data
            var trainingData = new EstimateTrainingData
            {
                Source = "Manual",
                LineMappings = new List<LineMapping>()
            };

            for (int i = 0; i < _parsedLines.Count; i++)
            {
                var line = _parsedLines[i];
                var ops = _lineMappings.GetValueOrDefault(i, new List<GeneratedOperation>());

                if (ops.Count == 0) continue; // Skip unmapped lines

                var mapping = new LineMapping
                {
                    RawLine = line.RawLine,
                    PartName = line.PartName,
                    OperationType = line.OperationType ?? "",
                    RepairHours = line.LaborHours,
                    RefinishHours = line.RefinishHours,
                    Price = line.Price,
                    GeneratedOperations = ops
                };

                trainingData.LineMappings.Add(mapping);
            }

            int patternsLearned = 0;
            int manualPatternsLearned = 0;

            if (trainingData.LineMappings.Count > 0)
            {
                _learningService.LearnFromEstimate(trainingData);
                patternsLearned = trainingData.LineMappings.Count;
            }

            // Also learn manual line patterns (# lines linked to parts)
            if (parsedEstimateLines.Any(p => p.IsManualLine))
            {
                _learningService.LearnManualLinePatterns(parsedEstimateLines);
                manualPatternsLearned = parsedEstimateLines.Count(p => p.IsManualLine);
            }

            UpdateStats();

            // Clear and show success
            _estimateInput.Text = "";
            _parsedLines.Clear();
            _lineMappings.Clear();
            _parsedLinesList.Items.Clear();
            _mappingPanel.Visibility = Visibility.Collapsed;
            _trainButton.IsEnabled = false;

            // Show success message
            var message = $"Trained {patternsLearned} operation patterns";
            if (manualPatternsLearned > 0)
            {
                message += $" and {manualPatternsLearned} manual line patterns (#)";
            }
            message += " successfully!";
            ShowMessage(message);
        }

        private void UpdateStats()
        {
            var stats = _learningService.GetStatistics();
            var manualPatterns = _learningService.ManualLinePatternCount;
            var statsText = $"{stats.TotalPatterns} patterns | {stats.TotalExamples} examples";
            if (manualPatterns > 0)
            {
                statsText += $" | {manualPatterns} manual line patterns";
            }
            _statsText.Text = statsText;
        }

        private async void ShowMessage(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Training Complete",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}
