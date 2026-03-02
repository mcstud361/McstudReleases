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
    /// Damage Estimate Start - Direct row input for operations
    /// Users type directly into rows - same format as display
    /// </summary>
    public sealed class InterviewView : UserControl
    {
        // UI Elements
        private StackPanel? _mainStack;
        private StackPanel? _operationsStack;
        private Button? _clearButton;
        private Button? _copyButton;
        private TextBlock? _statusText;
        private TextBlock? _totalText;
        private ScrollViewer? _scrollViewer;

        // Operation rows
        private List<DamageEstimateRow> _operationRows = new();

        // Event for when lines are ready to export
        public event Action<List<GeneratedEstimateLine>>? OnLinesGenerated;

        public InterviewView()
        {
            BuildUI();
            // Start with one empty row
            AddOperationRow();
        }

        private void BuildUI()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                Padding = new Thickness(0)
            };

            _scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            _mainStack = new StackPanel
            {
                Spacing = 8,
                Padding = new Thickness(16)
            };

            // Header row
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Damage Estimate Start",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(header, 0);
            headerRow.Children.Add(header);

            // Totals display
            _totalText = new TextBlock
            {
                Text = "0 operations",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_totalText, 1);
            headerRow.Children.Add(_totalText);

            _mainStack.Children.Add(headerRow);

            // Instructions
            var instructions = new TextBlock
            {
                Text = "Type directly into the rows below. Press Tab to move between fields, Enter to add a new row.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            _mainStack.Children.Add(instructions);

            // Column headers
            var headerGrid = CreateHeaderRow();
            _mainStack.Children.Add(headerGrid);

            // Operations container
            _operationsStack = new StackPanel
            {
                Spacing = 2
            };
            _mainStack.Children.Add(_operationsStack);

            // Action buttons row
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Thickness(0, 16, 0, 0)
            };

            _copyButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon
                        {
                            Glyph = "\uE8C8", // Copy icon
                            FontSize = 14,
                            Foreground = new SolidColorBrush(Colors.White)
                        },
                        new TextBlock
                        {
                            Text = "Copy to Clipboard",
                            FontSize = 12,
                            Foreground = new SolidColorBrush(Colors.White)
                        }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 10, 16, 10),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            _copyButton.Click += CopyButton_Click;
            buttonRow.Children.Add(_copyButton);

            _clearButton = new Button
            {
                Content = "Clear All",
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 50, 50)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 10, 16, 10)
            };
            _clearButton.Click += ClearButton_Click;
            buttonRow.Children.Add(_clearButton);

            _mainStack.Children.Add(buttonRow);

            // Status text
            _statusText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                Margin = new Thickness(0, 8, 0, 0)
            };
            _mainStack.Children.Add(_statusText);

            _scrollViewer.Content = _mainStack;
            mainBorder.Child = _scrollViewer;
            Content = mainBorder;
        }

        private Grid CreateHeaderRow()
        {
            var headerGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Padding = new Thickness(8, 10, 8, 10),
                Margin = new Thickness(0, 0, 0, 4)
            };

            // Define columns matching input fields
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });  // Operation
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // Qty
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Price
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // Labor
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Category
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // Refinish
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });  // Delete button

            AddHeaderCell(headerGrid, "Operation", 0);
            AddHeaderCell(headerGrid, "Description", 1);
            AddHeaderCell(headerGrid, "Qty", 2);
            AddHeaderCell(headerGrid, "Price", 3);
            AddHeaderCell(headerGrid, "Labor", 4);
            AddHeaderCell(headerGrid, "Category", 5);
            AddHeaderCell(headerGrid, "Refinish", 6);

            return headerGrid;
        }

        private void AddHeaderCell(Grid grid, string text, int column)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(tb, column);
            grid.Children.Add(tb);
        }

        private void AddOperationRow(GeneratedEstimateLine? prefill = null, bool isAutoGenerated = false)
        {
            var row = new DamageEstimateRow(prefill, isAutoGenerated);
            row.OnDeleteRequested += () => RemoveOperationRow(row);
            row.OnEnterPressed += () => HandleRowEnterPressed(row);
            row.OnDataChanged += UpdateTotals;
            row.OnSuggestOperations += HandleSuggestedOperations;

            _operationRows.Add(row);
            _operationsStack?.Children.Add(row.Container);

            if (prefill == null)
            {
                row.FocusOperation();
            }

            UpdateTotals();
        }

        /// <summary>
        /// Handle suggested operations from learned data - add them as new rows
        /// </summary>
        private void HandleSuggestedOperations(List<GeneratedEstimateLine> suggestedOps)
        {
            if (suggestedOps == null || suggestedOps.Count == 0) return;

            foreach (var op in suggestedOps)
            {
                AddOperationRow(op, isAutoGenerated: true);
            }

            _statusText!.Text = $"Added {suggestedOps.Count} suggested operation(s) from learned data";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255));

            UpdateTotals();
        }

        private void HandleRowEnterPressed(DamageEstimateRow row)
        {
            // If this is the last row and it has data, add a new row
            var index = _operationRows.IndexOf(row);
            if (index == _operationRows.Count - 1)
            {
                var op = row.GetOperation();
                if (!string.IsNullOrWhiteSpace(op.Description) || !string.IsNullOrWhiteSpace(op.Operation))
                {
                    AddOperationRow();
                }
            }
            else
            {
                // Move to next row
                if (index + 1 < _operationRows.Count)
                {
                    _operationRows[index + 1].FocusOperation();
                }
            }
        }

        private void RemoveOperationRow(DamageEstimateRow row)
        {
            // Always keep at least one row
            if (_operationRows.Count <= 1)
            {
                row.Clear();
                return;
            }

            _operationRows.Remove(row);
            _operationsStack?.Children.Remove(row.Container);
            UpdateTotals();
        }

        private void UpdateTotals()
        {
            var ops = GetOperations();
            var count = ops.Count;

            decimal totalLabor = 0;
            decimal totalRefinish = 0;

            foreach (var op in ops)
            {
                if (decimal.TryParse(op.Labor, out var labor))
                    totalLabor += labor;
                if (decimal.TryParse(op.Paint, out var paint))
                    totalRefinish += paint;
            }

            var text = $"{count} operation{(count != 1 ? "s" : "")}";
            if (totalLabor > 0 || totalRefinish > 0)
            {
                text += $" | {totalLabor:F1} labor | {totalRefinish:F1} refinish";
            }
            _totalText!.Text = text;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _operationRows.Clear();
            _operationsStack?.Children.Clear();

            // Add one empty row back
            AddOperationRow();

            _statusText!.Text = "Cleared all operations";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var operations = GetOperations();
            if (operations.Count == 0)
            {
                _statusText!.Text = "No operations to copy";
                _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 150, 100));
                return;
            }

            // Build tab-separated text for Excel/CCC paste
            var lines = new List<string>();

            foreach (var op in operations)
            {
                lines.Add($"{op.Operation}\t{op.Description}\t{op.Qty}\t{op.Price}\t{op.Labor}\t{op.Category}\t{op.Paint}");
            }

            var text = string.Join("\n", lines);

            try
            {
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                _statusText!.Text = $"Copied {operations.Count} operation(s) to clipboard";
                _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));

                // Fire event
                OnLinesGenerated?.Invoke(operations);

                // Visual feedback on button
                var originalContent = _copyButton!.Content;
                _copyButton.Content = new TextBlock
                {
                    Text = "Copied!",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White)
                };

                await Task.Delay(1500);
                _copyButton.Content = originalContent;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copy error: {ex.Message}");
                _statusText!.Text = "Failed to copy to clipboard";
                _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100));
            }
        }

        private List<GeneratedEstimateLine> GetOperations()
        {
            var operations = new List<GeneratedEstimateLine>();

            foreach (var row in _operationRows)
            {
                var op = row.GetOperation();
                // Only include rows that have at least operation or description filled
                if (!string.IsNullOrWhiteSpace(op.Operation) || !string.IsNullOrWhiteSpace(op.Description))
                {
                    operations.Add(op);
                }
            }

            return operations;
        }

        public List<GeneratedEstimateLine> GetGeneratedLines()
        {
            return GetOperations();
        }
    }

    /// <summary>
    /// A single row of damage estimate input fields
    /// Auto-fills labor/refinish from learned data and PartKnowledgeBase
    /// Queries EstimateLearningService for manual line suggestions
    /// </summary>
    public class DamageEstimateRow
    {
        public Grid Container { get; private set; }
        public event Action? OnDeleteRequested;
        public event Action? OnEnterPressed;
        public event Action? OnDataChanged;
        public event Action<List<GeneratedEstimateLine>>? OnSuggestOperations;

        private TextBox _operationBox;
        private TextBox _descriptionBox;
        private TextBox _qtyBox;
        private TextBox _priceBox;
        private TextBox _laborBox;
        private TextBox _categoryBox;
        private TextBox _refinishBox;
        private Button _deleteButton;
        private Button? _suggestButton;
        private bool _isAutoFilling = false;
        private bool _isAutoGenerated = false;
        private bool _hasSuggested = false;
        private readonly EstimateLearningService _learningService;

        public DamageEstimateRow(GeneratedEstimateLine? prefill = null, bool isAutoGenerated = false)
        {
            _learningService = EstimateLearningService.Instance;
            _isAutoGenerated = isAutoGenerated;

            Container = new Grid
            {
                Background = isAutoGenerated
                    ? new SolidColorBrush(Color.FromArgb(255, 35, 45, 40)) // Greenish tint for auto-generated
                    : new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 2)
            };

            // Match header columns + suggest button column
            Container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });  // Operation
            Container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            Container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // Qty
            Container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Price
            Container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // Labor
            Container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Category
            Container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // Refinish
            Container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });  // Delete/Suggest button

            // Create input fields with prefilled values
            _operationBox = CreateInputBox(prefill?.Operation ?? "", "Rpl", 0);
            _descriptionBox = CreateInputBox(prefill?.Description ?? "", "Part name", 1);
            _qtyBox = CreateInputBox(prefill?.Qty ?? "1", "1", 2);
            _priceBox = CreateInputBox(prefill?.Price ?? "", "$", 3);
            _laborBox = CreateInputBox(prefill?.Labor ?? "", "0.0", 4);
            _categoryBox = CreateInputBox(prefill?.Category ?? "B", "B", 5);
            _refinishBox = CreateInputBox(prefill?.Paint ?? "", "0.0", 6);

            // Auto-fill and suggest when operation or description changes
            _operationBox.LostFocus += (s, e) => TryAutoFillAndSuggest();
            _descriptionBox.LostFocus += (s, e) => TryAutoFillAndSuggest();

            // Notify data changed
            _operationBox.TextChanged += (s, e) => OnDataChanged?.Invoke();
            _descriptionBox.TextChanged += (s, e) => OnDataChanged?.Invoke();
            _laborBox.TextChanged += (s, e) => OnDataChanged?.Invoke();
            _refinishBox.TextChanged += (s, e) => OnDataChanged?.Invoke();

            // Handle Enter key on any field
            _operationBox.KeyDown += HandleKeyDown;
            _descriptionBox.KeyDown += HandleKeyDown;
            _qtyBox.KeyDown += HandleKeyDown;
            _priceBox.KeyDown += HandleKeyDown;
            _laborBox.KeyDown += HandleKeyDown;
            _categoryBox.KeyDown += HandleKeyDown;
            _refinishBox.KeyDown += HandleKeyDown;

            // Delete button (or auto-gen indicator)
            _deleteButton = new Button
            {
                Content = new FontIcon
                {
                    Glyph = isAutoGenerated ? "\uE945" : "\uE74D", // Robot for auto-gen, trash for manual
                    FontSize = 12,
                    Foreground = new SolidColorBrush(isAutoGenerated
                        ? Color.FromArgb(255, 100, 180, 100)
                        : Color.FromArgb(255, 200, 100, 100))
                },
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(4),
                MinWidth = 28,
                MinHeight = 28,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ToolTipService.SetToolTip(_deleteButton, isAutoGenerated ? "Auto-generated from learned data" : "Delete row");
            _deleteButton.Click += (s, e) => OnDeleteRequested?.Invoke();
            Grid.SetColumn(_deleteButton, 7);
            Container.Children.Add(_deleteButton);
        }

        private void HandleKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                OnEnterPressed?.Invoke();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Auto-fill labor/refinish and suggest related operations from learned data
        /// </summary>
        private void TryAutoFillAndSuggest()
        {
            if (_isAutoFilling) return;
            _isAutoFilling = true;

            try
            {
                var operation = _operationBox.Text?.Trim() ?? "";
                var description = _descriptionBox.Text?.Trim() ?? "";
                var operationLower = operation.ToLower();
                var descriptionLower = description.ToLower();

                if (string.IsNullOrEmpty(description)) return;

                // Determine operation type
                string opType = "Replace";
                if (operationLower.Contains("r&i") || operationLower.Contains("r i") || operationLower.Contains("remove"))
                    opType = "R&I";
                else if (operationLower.Contains("repair") || operationLower.Contains("rpr"))
                    opType = "Repair";
                else if (operationLower.Contains("refinish") || operationLower.Contains("rfn") || operationLower.Contains("paint"))
                    opType = "Refinish";
                else if (operationLower.Contains("blend"))
                    opType = "Blend";
                else if (operationLower.Contains("replace") || operationLower.Contains("rpl"))
                    opType = "Replace";

                // Get refinish units from the refinish box if entered
                decimal? refinishUnits = null;
                if (decimal.TryParse(_refinishBox.Text, out var refVal) && refVal > 0)
                    refinishUnits = refVal;

                // Get labor units if entered
                decimal? laborUnits = null;
                if (decimal.TryParse(_laborBox.Text, out var labVal) && labVal > 0)
                    laborUnits = labVal;

                // Query learned data for this part+operation
                var learnedResult = _learningService.QueryOperationsWithUnits(description, opType, laborUnits, refinishUnits);

                if (learnedResult.Operations.Count > 0)
                {
                    // Auto-fill from learned data if fields are empty
                    if (string.IsNullOrWhiteSpace(_laborBox.Text) || _laborBox.Text == "0" || _laborBox.Text == "0.0")
                    {
                        var laborOp = learnedResult.Operations.FirstOrDefault(o => !o.IsManualLine && o.ScaledLaborHours > 0);
                        if (laborOp != null)
                        {
                            _laborBox.Text = laborOp.ScaledLaborHours.ToString("F1");
                        }
                    }

                    if (string.IsNullOrWhiteSpace(_refinishBox.Text) || _refinishBox.Text == "0" || _refinishBox.Text == "0.0")
                    {
                        var refinishOp = learnedResult.Operations.FirstOrDefault(o => !o.IsManualLine && o.ScaledRefinishHours > 0);
                        if (refinishOp != null)
                        {
                            _refinishBox.Text = refinishOp.ScaledRefinishHours.ToString("F1");
                        }
                    }

                    // Suggest manual lines (# operations) - only once per row
                    if (!_hasSuggested && !_isAutoGenerated)
                    {
                        var manualLines = learnedResult.Operations
                            .Where(o => o.IsManualLine)
                            .ToList();

                        if (manualLines.Count > 0)
                        {
                            _hasSuggested = true;

                            var suggestedOps = manualLines.Select(ml => new GeneratedEstimateLine(
                                "Add",
                                ml.Description,
                                "1",
                                "",
                                ml.ScaledLaborHours > 0 ? ml.ScaledLaborHours.ToString("F2") : "",
                                ml.ScaledRefinishHours > 0 ? ml.ScaledRefinishHours.ToString("F2") : ""
                            )
                            {
                                Category = "#" // Mark as manual line
                            }).ToList();

                            OnSuggestOperations?.Invoke(suggestedOps);
                        }
                    }
                }
                else
                {
                    // Fall back to PartKnowledgeBase if no learned data
                    TryAutoFillFromKnowledgeBase(descriptionLower, operationLower);
                }
            }
            finally
            {
                _isAutoFilling = false;
            }
        }

        /// <summary>
        /// Fallback: Auto-fill from static PartKnowledgeBase
        /// </summary>
        private void TryAutoFillFromKnowledgeBase(string description, string operation)
        {
            var partKey = GetPartKeyFromDescription(description);
            if (string.IsNullOrEmpty(partKey)) return;

            var partInfo = PartKnowledgeBase.GetPart(partKey);
            if (partInfo == null) return;

            bool isRI = operation.Contains("r&i") || operation.Contains("r i") || operation.Contains("remove");
            bool isRefinishOnly = operation.Contains("refinish") || operation.Contains("rfn") || operation.Contains("paint");
            bool isBlend = operation.Contains("blend");

            // Auto-fill Labor if empty
            if (string.IsNullOrWhiteSpace(_laborBox.Text) || _laborBox.Text == "0" || _laborBox.Text == "0.0")
            {
                if (isRI)
                {
                    _laborBox.Text = partInfo.BaseRIHours.ToString("F1");
                }
                else if (!isRefinishOnly && !isBlend)
                {
                    _laborBox.Text = partInfo.BaseRepairHours.ToString("F1");
                }
            }

            // Auto-fill Refinish if empty
            if (string.IsNullOrWhiteSpace(_refinishBox.Text) || _refinishBox.Text == "0" || _refinishBox.Text == "0.0")
            {
                bool needsRefinish = operation.Contains("replace") || operation.Contains("rpl") ||
                                     operation.Contains("repair") || operation.Contains("rpr") ||
                                     isRefinishOnly || isBlend;
                if (needsRefinish && partInfo.BaseRefinishHours > 0)
                {
                    _refinishBox.Text = partInfo.BaseRefinishHours.ToString("F1");
                }
            }

            // Auto-set category
            if (string.IsNullOrWhiteSpace(_categoryBox.Text) || _categoryBox.Text == "B")
            {
                if (isRefinishOnly || isBlend)
                {
                    _categoryBox.Text = "P";
                }
            }
        }

        /// <summary>
        /// Map description text to PartKnowledgeBase key
        /// </summary>
        private string GetPartKeyFromDescription(string description)
        {
            var lower = description.ToLower();

            // Bumpers
            if (lower.Contains("front") && lower.Contains("bumper")) return "front_bumper_cover";
            if (lower.Contains("rear") && lower.Contains("bumper")) return "rear_bumper_cover";
            if (lower.Contains("bumper")) return "front_bumper_cover"; // Default to front

            // Hood
            if (lower.Contains("hood")) return "hood";

            // Fenders
            if (lower.Contains("fender") && (lower.Contains("lh") || lower.Contains("left") || lower.Contains("l/"))) return "left_fender";
            if (lower.Contains("fender") && (lower.Contains("rh") || lower.Contains("right") || lower.Contains("r/"))) return "right_fender";
            if (lower.Contains("fender")) return "left_fender"; // Default

            // Doors
            if (lower.Contains("front") && lower.Contains("door") && (lower.Contains("lh") || lower.Contains("left"))) return "left_front_door";
            if (lower.Contains("front") && lower.Contains("door") && (lower.Contains("rh") || lower.Contains("right"))) return "right_front_door";
            if (lower.Contains("rear") && lower.Contains("door") && (lower.Contains("lh") || lower.Contains("left"))) return "left_rear_door";
            if (lower.Contains("rear") && lower.Contains("door") && (lower.Contains("rh") || lower.Contains("right"))) return "right_rear_door";
            if (lower.Contains("door") && (lower.Contains("lh") || lower.Contains("left"))) return "left_front_door";
            if (lower.Contains("door") && (lower.Contains("rh") || lower.Contains("right"))) return "right_front_door";
            if (lower.Contains("door")) return "left_front_door"; // Default

            // Quarter panels
            if (lower.Contains("quarter") && (lower.Contains("lh") || lower.Contains("left"))) return "left_quarter_panel";
            if (lower.Contains("quarter") && (lower.Contains("rh") || lower.Contains("right"))) return "right_quarter_panel";
            if (lower.Contains("quarter")) return "left_quarter_panel"; // Default

            // Trunk
            if (lower.Contains("trunk") || lower.Contains("decklid")) return "trunk_lid";

            // Roof
            if (lower.Contains("roof")) return "roof_panel";

            return "";
        }

        private TextBox CreateInputBox(string value, string placeholder, int column)
        {
            var box = new TextBox
            {
                Text = value,
                PlaceholderText = placeholder,
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 6, 6, 6),
                FontSize = 12,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Add focus styling
            box.GotFocus += (s, e) =>
            {
                box.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
            };
            box.LostFocus += (s, e) =>
            {
                box.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70));
            };

            Grid.SetColumn(box, column);
            Container.Children.Add(box);

            return box;
        }

        public void FocusOperation()
        {
            _operationBox.Focus(FocusState.Programmatic);
        }

        public void Clear()
        {
            _operationBox.Text = "";
            _descriptionBox.Text = "";
            _qtyBox.Text = "1";
            _priceBox.Text = "";
            _laborBox.Text = "";
            _categoryBox.Text = "B";
            _refinishBox.Text = "";
        }

        public GeneratedEstimateLine GetOperation()
        {
            return new GeneratedEstimateLine(
                _operationBox.Text?.Trim() ?? "",
                _descriptionBox.Text?.Trim() ?? "",
                string.IsNullOrWhiteSpace(_qtyBox.Text) ? "1" : _qtyBox.Text.Trim(),
                _priceBox.Text?.Trim() ?? "",
                _laborBox.Text?.Trim() ?? "",
                _refinishBox.Text?.Trim() ?? ""
            )
            {
                Category = _categoryBox.Text?.Trim() ?? "B"
            };
        }
    }

    #region Data Classes

    public class IntakeOperation
    {
        public string Operation { get; set; } = "";
        public string PartName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Material { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
    }

    public class EstimateLine
    {
        public string Operation { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; } = 1;
        public decimal Price { get; set; }
        public decimal Labor { get; set; }
        public string Category { get; set; } = "";
        public decimal Refinish { get; set; }
    }

    public class SmartEstimateLine
    {
        public string Operation { get; set; } = "";
        public string PartName { get; set; } = "";
        public string PartId { get; set; } = "";
        public decimal BodyLabor { get; set; }
        public decimal RefinishTime { get; set; }
        public decimal RITime { get; set; }
    }

    public enum QuestionType
    {
        SingleChoice,
        MultiChoice,
        Text
    }

    public class InterviewQuestion
    {
        public string Id { get; set; } = "";
        public string Question { get; set; } = "";
        public QuestionType QuestionType { get; set; } = QuestionType.SingleChoice;
        public string[] Options { get; set; } = Array.Empty<string>();
        public Func<Dictionary<string, string>, bool>? ShowWhen { get; set; }
    }

    public class GeneratedEstimateLine
    {
        public string Operation { get; set; }
        public string Description { get; set; }
        public string Qty { get; set; }
        public string Price { get; set; }
        public string Labor { get; set; }
        public string Paint { get; set; }
        public string Category { get; set; } = "B";

        public GeneratedEstimateLine(string operation, string description, string qty = "1", string price = "", string labor = "", string paint = "")
        {
            Operation = operation;
            Description = description;
            Qty = qty;
            Price = price;
            Labor = labor;
            Paint = paint;
        }
    }

    #endregion
}
