#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Supplement Checker View - Analyzes estimates and suggests missing items
    ///
    /// Features:
    /// - Paste estimate text or enter operations manually
    /// - Auto-detect commonly missed items
    /// - ADAS calibration requirements
    /// - NOT INCLUDED items reference
    /// - Training explanations (Why?)
    /// - Quick wins: Blend calc, 3-stage toggle, scan reminder
    /// </summary>
    public sealed class SupplementCheckerView : UserControl
    {
        // Services
        private readonly SupplementDetectorService _supplementService;
        private readonly ADASMatrixService _adasService;
        private readonly TrainingExplanationService _trainingService;

        // UI Elements
        private StackPanel? _mainStack;
        private TextBox? _estimateInput;
        private StackPanel? _operationsStack;
        private StackPanel? _suggestionsStack;
        private StackPanel? _adasStack;
        private TextBlock? _statusText;
        private TextBlock? _totalSuggestedText;
        private CheckBox? _is3StageCheckbox;
        private NumberBox? _estimateTotalBox;
        private ScrollViewer? _scrollViewer;

        // State
        private List<EstimateOperation> _currentOperations = new();
        private SupplementAnalysisResult? _currentAnalysis;
        private ADASAnalysisResult? _currentAdasAnalysis;
        private bool _showExplanations = true;

        public SupplementCheckerView()
        {
            _supplementService = SupplementDetectorService.Instance;
            _adasService = ADASMatrixService.Instance;
            _trainingService = TrainingExplanationService.Instance;

            BuildUI();
        }

        private void BuildUI()
        {
            var rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25))
            };

            _scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            _mainStack = new StackPanel
            {
                Spacing = 12,
                Padding = new Thickness(16)
            };

            // Header
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Supplement Checker",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Grid.SetColumn(header, 0);
            headerRow.Children.Add(header);

            var helpButton = new Button
            {
                Content = "?",
                Width = 32,
                Height = 32,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(16)
            };
            helpButton.Click += ShowHelpDialog;
            Grid.SetColumn(helpButton, 1);
            headerRow.Children.Add(helpButton);

            _mainStack.Children.Add(headerRow);

            // Description
            var desc = new TextBlock
            {
                Text = "Paste estimate text or add operations to find commonly missed supplements, ADAS calibrations, and NOT INCLUDED items.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _mainStack.Children.Add(desc);

            // Quick Settings Row
            var settingsRow = CreateSettingsRow();
            _mainStack.Children.Add(settingsRow);

            // Estimate Input Section
            var inputSection = CreateInputSection();
            _mainStack.Children.Add(inputSection);

            // Current Operations
            var opsSection = CreateOperationsSection();
            _mainStack.Children.Add(opsSection);

            // Analysis Button
            var analyzeButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE9D9", FontSize = 16 },
                        new TextBlock { Text = "Analyze for Missing Items", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(20, 12, 20, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 8)
            };
            analyzeButton.Click += AnalyzeButton_Click;
            _mainStack.Children.Add(analyzeButton);

            // Status
            _statusText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
            };
            _mainStack.Children.Add(_statusText);

            // Suggestions Section
            var suggestSection = CreateSuggestionsSection();
            _mainStack.Children.Add(suggestSection);

            // ADAS Section
            var adasSection = CreateADASSection();
            _mainStack.Children.Add(adasSection);

            // Total Suggested
            _totalSuggestedText = new TextBlock
            {
                Text = "",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                Margin = new Thickness(0, 12, 0, 0)
            };
            _mainStack.Children.Add(_totalSuggestedText);

            // Quick Reference
            var quickRef = CreateQuickReferenceSection();
            _mainStack.Children.Add(quickRef);

            _scrollViewer.Content = _mainStack;
            rootBorder.Child = _scrollViewer;
            Content = rootBorder;
        }

        private Grid CreateSettingsRow()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 3-Stage Paint Toggle
            _is3StageCheckbox = new CheckBox
            {
                Content = "3-Stage/Tri-Coat Paint (+50% refinish)",
                Foreground = new SolidColorBrush(Colors.White),
                IsChecked = false
            };
            Grid.SetColumn(_is3StageCheckbox, 0);
            grid.Children.Add(_is3StageCheckbox);

            // Estimate Total Input
            var totalLabel = new TextBlock
            {
                Text = "Estimate Total: $",
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 8, 0)
            };
            Grid.SetColumn(totalLabel, 1);
            grid.Children.Add(totalLabel);

            _estimateTotalBox = new NumberBox
            {
                Value = 0,
                Minimum = 0,
                Maximum = 999999,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                Width = 120,
                PlaceholderText = "0.00"
            };
            Grid.SetColumn(_estimateTotalBox, 3);
            grid.Children.Add(_estimateTotalBox);

            return grid;
        }

        private Border CreateInputSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var stack = new StackPanel { Spacing = 8 };

            var header = new TextBlock
            {
                Text = "Paste Estimate Text",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            stack.Children.Add(header);

            _estimateInput = new TextBox
            {
                PlaceholderText = "Paste estimate lines here...\nExample:\nReplace Front Bumper Cover\nRefinish Front Bumper Cover\nR&I Grille",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 120,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            stack.Children.Add(_estimateInput);

            var parseButton = new Button
            {
                Content = "Parse Operations",
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 8, 12, 8)
            };
            parseButton.Click += ParseButton_Click;
            stack.Children.Add(parseButton);

            border.Child = stack;
            return border;
        }

        private Border CreateOperationsSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var stack = new StackPanel { Spacing = 8 };

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Current Operations (0)",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                Name = "OpsHeader"
            };
            Grid.SetColumn(header, 0);
            headerRow.Children.Add(header);

            var clearButton = new Button
            {
                Content = "Clear",
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 50, 50)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 11
            };
            clearButton.Click += ClearOperations_Click;
            Grid.SetColumn(clearButton, 1);
            headerRow.Children.Add(clearButton);

            stack.Children.Add(headerRow);

            _operationsStack = new StackPanel { Spacing = 4 };
            stack.Children.Add(_operationsStack);

            // Add operation manually
            var addRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var opTypeBox = new ComboBox
            {
                PlaceholderText = "Operation",
                Width = 95,
                Items = { "Replace", "Repair", "Refinish", "Blend", "R&I", "Section", "O/H" },
                Name = "OpTypeBox"
            };
            Grid.SetColumn(opTypeBox, 0);
            addRow.Children.Add(opTypeBox);

            var partNameBox = new TextBox
            {
                PlaceholderText = "Part name (e.g., Front Bumper Cover)",
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(8, 0, 8, 0),
                Name = "PartNameBox"
            };
            Grid.SetColumn(partNameBox, 1);
            addRow.Children.Add(partNameBox);

            var addButton = new Button
            {
                Content = "+",
                Width = 32,
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            addButton.Click += (s, e) => AddManualOperation(opTypeBox, partNameBox);
            Grid.SetColumn(addButton, 2);
            addRow.Children.Add(addButton);

            stack.Children.Add(addRow);

            border.Child = stack;
            return border;
        }

        private Border CreateSuggestionsSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 45, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed,
                Name = "SuggestionsSection"
            };

            var stack = new StackPanel { Spacing = 8 };

            var header = new TextBlock
            {
                Text = "Suggested Supplements",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
            };
            stack.Children.Add(header);

            _suggestionsStack = new StackPanel { Spacing = 6 };
            stack.Children.Add(_suggestionsStack);

            border.Child = stack;
            return border;
        }

        private Border CreateADASSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 35, 50)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed,
                Name = "ADASSection"
            };

            var stack = new StackPanel { Spacing = 8 };

            var header = new TextBlock
            {
                Text = "ADAS Calibrations Required",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 130, 220))
            };
            stack.Children.Add(header);

            _adasStack = new StackPanel { Spacing = 6 };
            stack.Children.Add(_adasStack);

            border.Child = stack;
            return border;
        }

        private Border CreateQuickReferenceSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 45)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 16, 0, 0)
            };

            var expander = new Expander
            {
                Header = new TextBlock
                {
                    Text = "Quick Reference: Always Consider These Items",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 200))
                },
                IsExpanded = false
            };

            var stack = new StackPanel { Spacing = 4 };

            var tips = _trainingService.GetQuickTips();
            foreach (var tip in tips)
            {
                var tipText = new TextBlock
                {
                    Text = $"• {tip}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    TextWrapping = TextWrapping.Wrap
                };
                stack.Children.Add(tipText);
            }

            expander.Content = stack;
            border.Child = expander;
            return border;
        }

        #region Event Handlers

        private void ParseButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_estimateInput?.Text)) return;

            var lines = _estimateInput.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var op = ParseOperationLine(trimmed);
                if (op != null && !string.IsNullOrEmpty(op.PartName))
                {
                    _currentOperations.Add(op);
                }
            }

            RefreshOperationsList();
            _statusText!.Text = $"Parsed {_currentOperations.Count} operations";
        }

        private EstimateOperation? ParseOperationLine(string line)
        {
            var lineLower = line.ToLowerInvariant();
            var op = new EstimateOperation { Description = line };

            // Detect operation type
            if (lineLower.Contains("replace") || lineLower.Contains("rpl"))
                op.OperationType = "Replace";
            else if (lineLower.Contains("repair") || lineLower.Contains("rpr"))
                op.OperationType = "Repair";
            else if (lineLower.Contains("refinish") || lineLower.Contains("rfn") || lineLower.Contains("paint"))
                op.OperationType = "Refinish";
            else if (lineLower.Contains("blend") || lineLower.Contains("bld"))
                op.OperationType = "Blend";
            else if (lineLower.Contains("r&i") || lineLower.Contains("r+i") || lineLower.Contains("remove"))
                op.OperationType = "R&I";
            else if (lineLower.Contains("o/h") || lineLower.Contains("overhaul"))
                op.OperationType = "O/H";
            else if (lineLower.Contains("section"))
                op.OperationType = "Section";
            else
                op.OperationType = "Unknown";

            // Extract part name (remove operation keywords)
            var partName = line;
            var removeWords = new[] { "replace", "repair", "refinish", "blend", "r&i", "r+i", "o/h", "overhaul", "section", "remove", "install" };
            foreach (var word in removeWords)
            {
                partName = System.Text.RegularExpressions.Regex.Replace(partName, word, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            op.PartName = partName.Trim().Trim('-', ':', ',', '.').Trim();

            return op;
        }

        private void AddManualOperation(ComboBox opTypeBox, TextBox partNameBox)
        {
            if (opTypeBox.SelectedItem == null || string.IsNullOrWhiteSpace(partNameBox.Text)) return;

            var op = new EstimateOperation
            {
                OperationType = opTypeBox.SelectedItem.ToString()!,
                PartName = partNameBox.Text.Trim(),
                Description = $"{opTypeBox.SelectedItem} {partNameBox.Text.Trim()}"
            };

            _currentOperations.Add(op);
            RefreshOperationsList();

            // Clear inputs
            opTypeBox.SelectedIndex = -1;
            partNameBox.Text = "";
        }

        private void ClearOperations_Click(object sender, RoutedEventArgs e)
        {
            _currentOperations.Clear();
            RefreshOperationsList();
            _currentAnalysis = null;
            _currentAdasAnalysis = null;
            ClearResults();
        }

        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentOperations.Count == 0)
            {
                _statusText!.Text = "Add operations first";
                return;
            }

            _statusText!.Text = "Analyzing...";

            await Task.Delay(100); // Let UI update

            // Run analysis
            var input = new EstimateAnalysisInput
            {
                Operations = _currentOperations,
                EstimateTotal = (decimal)(_estimateTotalBox?.Value ?? 0),
                Is3StagePaint = _is3StageCheckbox?.IsChecked == true
            };

            _currentAnalysis = _supplementService.AnalyzeEstimate(input);
            _currentAdasAnalysis = _adasService.AnalyzeForADAS(_currentOperations);

            DisplayResults();
        }

        private void ShowHelpDialog(object sender, RoutedEventArgs e)
        {
            var tips = new List<string>
            {
                "1. Add operations by pasting estimate text or entering manually",
                "2. Set estimate total for scan threshold checks ($2500+)",
                "3. Check '3-Stage Paint' if vehicle has tri-coat for +50% refinish",
                "4. Click 'Analyze' to find missing supplements and ADAS calibrations",
                "5. Click 'Why?' on any suggestion to learn more",
                "",
                "Common triggers:",
                "• Bumper work → Flex additive, adhesion promoter, ADAS calibration",
                "• Quarter panel → Corrosion protection, seam sealer, weld-thru primer",
                "• Windshield → Camera calibration required",
                "• Alignment → Steering angle reset, ADAS verification"
            };

            _statusText!.Text = string.Join("\n", tips);
        }

        #endregion

        #region Display Methods

        private void RefreshOperationsList()
        {
            if (_operationsStack == null) return;

            _operationsStack.Children.Clear();

            // Update header count
            var header = FindDescendantByName(_mainStack!, "OpsHeader") as TextBlock;
            if (header != null)
            {
                header.Text = $"Current Operations ({_currentOperations.Count})";
            }

            foreach (var op in _currentOperations)
            {
                var row = CreateOperationRow(op);
                _operationsStack.Children.Add(row);
            }
        }

        private Grid CreateOperationRow(EstimateOperation op)
        {
            var grid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(4)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var opType = new TextBlock
            {
                Text = op.OperationType,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = GetOperationColor(op.OperationType),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(opType, 0);
            grid.Children.Add(opType);

            var partName = new TextBlock
            {
                Text = op.PartName,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(partName, 1);
            grid.Children.Add(partName);

            var removeBtn = new Button
            {
                Content = "X",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 80, 80)),
                FontSize = 10
            };
            removeBtn.Click += (s, e) =>
            {
                _currentOperations.Remove(op);
                RefreshOperationsList();
            };
            Grid.SetColumn(removeBtn, 2);
            grid.Children.Add(removeBtn);

            return grid;
        }

        private void DisplayResults()
        {
            if (_suggestionsStack == null || _adasStack == null) return;

            ClearResults();

            // Display supplement suggestions
            if (_currentAnalysis?.Suggestions.Any() == true)
            {
                var suggestSection = FindDescendantByName(_mainStack!, "SuggestionsSection") as Border;
                if (suggestSection != null) suggestSection.Visibility = Visibility.Visible;

                // Critical items first
                foreach (var item in _currentAnalysis.CriticalItems)
                {
                    var card = CreateSuggestionCard(item, "critical");
                    _suggestionsStack.Children.Add(card);
                }

                // High priority
                foreach (var item in _currentAnalysis.HighPriorityItems)
                {
                    var card = CreateSuggestionCard(item, "high");
                    _suggestionsStack.Children.Add(card);
                }

                // Medium priority
                foreach (var item in _currentAnalysis.MediumPriorityItems)
                {
                    var card = CreateSuggestionCard(item, "medium");
                    _suggestionsStack.Children.Add(card);
                }

                // Low priority (collapsed by default)
                if (_currentAnalysis.LowPriorityItems.Any())
                {
                    var lowExpander = new Expander
                    {
                        Header = $"Low Priority Items ({_currentAnalysis.LowPriorityItems.Count})",
                        IsExpanded = false
                    };
                    var lowStack = new StackPanel { Spacing = 4 };
                    foreach (var item in _currentAnalysis.LowPriorityItems)
                    {
                        var card = CreateSuggestionCard(item, "low");
                        lowStack.Children.Add(card);
                    }
                    lowExpander.Content = lowStack;
                    _suggestionsStack.Children.Add(lowExpander);
                }
            }

            // Display ADAS calibrations
            if (_currentAdasAnalysis?.RequiredCalibrations.Any() == true)
            {
                var adasSection = FindDescendantByName(_mainStack!, "ADASSection") as Border;
                if (adasSection != null) adasSection.Visibility = Visibility.Visible;

                foreach (var cal in _currentAdasAnalysis.RequiredCalibrations)
                {
                    var card = CreateADASCard(cal);
                    _adasStack.Children.Add(card);
                }

                // Painting considerations
                if (_currentAdasAnalysis.PaintingConsiderations.Any())
                {
                    var paintWarning = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 80, 60, 20)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8),
                        Margin = new Thickness(0, 8, 0, 0)
                    };
                    var paintStack = new StackPanel { Spacing = 4 };
                    foreach (var consideration in _currentAdasAnalysis.PaintingConsiderations)
                    {
                        paintStack.Children.Add(new TextBlock
                        {
                            Text = consideration,
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                    paintWarning.Child = paintStack;
                    _adasStack.Children.Add(paintWarning);
                }
            }

            // Update totals
            var totalSuggested = (_currentAnalysis?.TotalSuggestedValue ?? 0) + (_currentAdasAnalysis?.TotalCalibrationCost ?? 0);
            var totalLabor = (_currentAnalysis?.TotalSuggestedLabor ?? 0) + (_currentAdasAnalysis?.TotalCalibrationTime ?? 0);

            _totalSuggestedText!.Text = $"Total Suggested Supplements: ${totalSuggested:N2} ({totalLabor:N1} hours)";
            _statusText!.Text = $"Found {_currentAnalysis?.Suggestions.Count ?? 0} supplement items and {_currentAdasAnalysis?.CalibrationsRequired ?? 0} ADAS calibrations";
        }

        private Border CreateSuggestionCard(SupplementSuggestion item, string priority)
        {
            var bgColor = priority switch
            {
                "critical" => Color.FromArgb(255, 80, 30, 30),
                "high" => Color.FromArgb(255, 60, 50, 30),
                "medium" => Color.FromArgb(255, 40, 50, 40),
                _ => Color.FromArgb(255, 40, 40, 45)
            };

            var border = new Border
            {
                Background = new SolidColorBrush(bgColor),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoStack = new StackPanel { Spacing = 2 };

            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var priorityBadge = new Border
            {
                Background = new SolidColorBrush(GetPriorityColor(priority)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1)
            };
            priorityBadge.Child = new TextBlock
            {
                Text = priority.ToUpper(),
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            headerRow.Children.Add(priorityBadge);

            headerRow.Children.Add(new TextBlock
            {
                Text = item.Item,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            infoStack.Children.Add(headerRow);

            infoStack.Children.Add(new TextBlock
            {
                Text = item.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                TextWrapping = TextWrapping.Wrap
            });

            infoStack.Children.Add(new TextBlock
            {
                Text = $"Because: {item.TriggerOperation}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });

            Grid.SetColumn(infoStack, 0);
            grid.Children.Add(infoStack);

            // Value
            var valueText = new TextBlock
            {
                Text = item.EstimatedValue > 0 ? $"~${item.EstimatedValue:N0}" : "",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(valueText);

            // Why button
            var whyButton = new Button
            {
                Content = "Why?",
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 10
            };
            whyButton.Click += (s, e) => ShowExplanation(item);
            Grid.SetColumn(whyButton, 2);
            grid.Children.Add(whyButton);

            border.Child = grid;
            return border;
        }

        private Border CreateADASCard(ADASCalibration cal)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 40, 60)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10)
            };

            var stack = new StackPanel { Spacing = 4 };

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            stack.Children.Add(new TextBlock
            {
                Text = cal.SensorName,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 130, 220))
            });

            stack.Children.Add(new TextBlock
            {
                Text = $"Calibration Type: {cal.CalibrationType}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 180))
            });

            stack.Children.Add(new TextBlock
            {
                Text = cal.Explanation,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 160)),
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrEmpty(cal.OEMNotes))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"OEM: {cal.OEMNotes}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 180, 100)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var costRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 4, 0, 0) };
            if (cal.EstimatedCost > 0)
            {
                costRow.Children.Add(new TextBlock
                {
                    Text = $"~${cal.EstimatedCost:N0}",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
                });
            }
            if (cal.EstimatedTime > 0)
            {
                costRow.Children.Add(new TextBlock
                {
                    Text = $"{cal.EstimatedTime:N1} hr",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 180))
                });
            }
            stack.Children.Add(costRow);

            border.Child = stack;
            return border;
        }

        private void ShowExplanation(SupplementSuggestion item)
        {
            var explanation = _trainingService.GetSupplementExplanation(item.Item, item.TriggerOperation);

            // Show in status or dialog
            _statusText!.Text = explanation;
        }

        private void ClearResults()
        {
            _suggestionsStack?.Children.Clear();
            _adasStack?.Children.Clear();

            var suggestSection = FindDescendantByName(_mainStack!, "SuggestionsSection") as Border;
            if (suggestSection != null) suggestSection.Visibility = Visibility.Collapsed;

            var adasSection = FindDescendantByName(_mainStack!, "ADASSection") as Border;
            if (adasSection != null) adasSection.Visibility = Visibility.Collapsed;

            _totalSuggestedText!.Text = "";
        }

        #endregion

        #region Helpers

        private SolidColorBrush GetOperationColor(string opType)
        {
            return opType.ToLower() switch
            {
                "replace" => new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                "repair" => new SolidColorBrush(Color.FromArgb(255, 200, 180, 100)),
                "refinish" => new SolidColorBrush(Color.FromArgb(255, 100, 180, 220)),
                "blend" => new SolidColorBrush(Color.FromArgb(255, 180, 130, 220)),
                "r&i" => new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                "section" => new SolidColorBrush(Color.FromArgb(255, 220, 100, 100)),
                _ => new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            };
        }

        private Color GetPriorityColor(string priority)
        {
            return priority switch
            {
                "critical" => Color.FromArgb(255, 200, 50, 50),
                "high" => Color.FromArgb(255, 200, 150, 50),
                "medium" => Color.FromArgb(255, 50, 150, 100),
                _ => Color.FromArgb(255, 100, 100, 120)
            };
        }

        private FrameworkElement? FindDescendantByName(DependencyObject parent, string name)
        {
            if (parent == null) return null;

            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement fe && fe.Name == name)
                    return fe;

                var result = FindDescendantByName(child, name);
                if (result != null) return result;
            }

            return null;
        }

        #endregion
    }
}
