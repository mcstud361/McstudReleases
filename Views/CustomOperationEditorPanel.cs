using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Models;
using McStudDesktop.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Views;

/// <summary>
/// A reusable panel for managing custom operations within a category section.
/// Displays a + button to add new operations and shows existing custom operations with edit/delete.
/// </summary>
public class CustomOperationEditorPanel : UserControl
{
    private readonly string _category;
    private readonly string _subCategory;
    private readonly StackPanel _mainPanel;
    private readonly StackPanel _operationsContainer;
    private readonly Grid _addEditFormGrid;
    private CustomOperation? _editingOperation;

    // Form fields
    private TextBox _descriptionBox = null!;
    private ComboBox _calcTypeCombo = null!;
    private NumberBox _valueBox = null!;
    private NumberBox _materialCostBox = null!;
    private ComboBox _opTypeCombo = null!;
    private CheckBox _enabledByDefaultCheck = null!;
    private TextBox _notesBox = null!;

    /// <summary>
    /// Event fired when custom operations change (add/edit/delete)
    /// </summary>
    public event EventHandler<List<CustomOperation>>? OperationsChanged;

    /// <summary>
    /// Current repair hours for percentage calculation preview
    /// </summary>
    public decimal RepairHours { get; set; }

    /// <summary>
    /// Current refinish hours for percentage calculation preview
    /// </summary>
    public decimal RefinishHours { get; set; }

    public CustomOperationEditorPanel(string category, string subCategory = "")
    {
        _category = category;
        _subCategory = subCategory;

        _mainPanel = new StackPanel { Spacing = 8 };
        _operationsContainer = new StackPanel { Spacing = 4 };
        _addEditFormGrid = new Grid { Visibility = Visibility.Collapsed };

        BuildUI();
        Content = _mainPanel;

        // Load existing operations
        RefreshOperations();
    }

    private void BuildUI()
    {
        // Custom operations list
        _mainPanel.Children.Add(_operationsContainer);

        // Add button
        var addButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE710", FontSize = 14 }, // + icon
                    new TextBlock { Text = "Add Custom Operation", VerticalAlignment = VerticalAlignment.Center }
                }
            },
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Margin = new Thickness(0, 4, 0, 0)
        };
        addButton.Click += (s, e) => ShowAddForm();
        _mainPanel.Children.Add(addButton);

        // Add/Edit form (hidden by default)
        BuildAddEditForm();
        _mainPanel.Children.Add(_addEditFormGrid);
    }

    private void BuildAddEditForm()
    {
        _addEditFormGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var formBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 8, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1)
        };

        var formStack = new StackPanel { Spacing = 12 };

        // Header
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        headerStack.Children.Add(new TextBlock
        {
            Text = "Custom Operation",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        formStack.Children.Add(headerStack);

        // Description
        formStack.Children.Add(new TextBlock { Text = "Description:", Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)) });
        _descriptionBox = new TextBox
        {
            PlaceholderText = "e.g., Adhesion Promoter Application",
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45))
        };
        formStack.Children.Add(_descriptionBox);

        // Two-column layout for calc type and value
        var calcGrid = new Grid { ColumnSpacing = 16 };
        calcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        calcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Calculation type
        var calcTypeStack = new StackPanel { Spacing = 4 };
        calcTypeStack.Children.Add(new TextBlock { Text = "Calculation Type:", Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)) });
        _calcTypeCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            Items =
            {
                new ComboBoxItem { Content = "Fixed Hours", Tag = CalculationType.FixedHours },
                new ComboBoxItem { Content = "% of Repair/Body Time", Tag = CalculationType.PercentOfRepairTime },
                new ComboBoxItem { Content = "% of Refinish/Paint Time", Tag = CalculationType.PercentOfRefinishTime },
                new ComboBoxItem { Content = "% of R&I Time", Tag = CalculationType.PercentOfRITime },
                new ComboBoxItem { Content = "% of Total Time", Tag = CalculationType.PercentOfTotalTime }
            },
            SelectedIndex = 0
        };
        _calcTypeCombo.SelectionChanged += CalcTypeChanged;
        calcTypeStack.Children.Add(_calcTypeCombo);
        Grid.SetColumn(calcTypeStack, 0);
        calcGrid.Children.Add(calcTypeStack);

        // Value
        var valueStack = new StackPanel { Spacing = 4 };
        valueStack.Children.Add(new TextBlock { Text = "Value (hours or %):", Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)), Name = "ValueLabel" });
        _valueBox = new NumberBox
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0.5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            SmallChange = 0.1,
            LargeChange = 1,
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45))
        };
        valueStack.Children.Add(_valueBox);
        Grid.SetColumn(valueStack, 1);
        calcGrid.Children.Add(valueStack);

        formStack.Children.Add(calcGrid);

        // Material cost and Op type row
        var row2Grid = new Grid { ColumnSpacing = 16 };
        row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Material cost
        var materialStack = new StackPanel { Spacing = 4 };
        materialStack.Children.Add(new TextBlock { Text = "Material Cost ($):", Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)) });
        _materialCostBox = new NumberBox
        {
            Minimum = 0,
            Maximum = 9999,
            Value = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            SmallChange = 1,
            LargeChange = 10,
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45))
        };
        materialStack.Children.Add(_materialCostBox);
        Grid.SetColumn(materialStack, 0);
        row2Grid.Children.Add(materialStack);

        // Operation type code
        var opTypeStack = new StackPanel { Spacing = 4 };
        opTypeStack.Children.Add(new TextBlock { Text = "Operation Type:", Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)) });
        _opTypeCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            Items =
            {
                new ComboBoxItem { Content = "Material (Mat)", Tag = "Mat" },
                new ComboBoxItem { Content = "Repair (Rpr)", Tag = "Rpr" },
                new ComboBoxItem { Content = "R&I", Tag = "R&I" },
                new ComboBoxItem { Content = "Refinish", Tag = "Refinish" },
                new ComboBoxItem { Content = "Body (B)", Tag = "B" },
                new ComboBoxItem { Content = "Frame (F)", Tag = "F" }
            },
            SelectedIndex = 0
        };
        opTypeStack.Children.Add(_opTypeCombo);
        Grid.SetColumn(opTypeStack, 1);
        row2Grid.Children.Add(opTypeStack);

        formStack.Children.Add(row2Grid);

        // Notes (optional)
        formStack.Children.Add(new TextBlock { Text = "Notes (optional):", Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)) });
        _notesBox = new TextBox
        {
            PlaceholderText = "P-Page reference, justification, etc.",
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 60
        };
        formStack.Children.Add(_notesBox);

        // Enabled by default
        _enabledByDefaultCheck = new CheckBox
        {
            Content = "Enabled by default when category is selected",
            IsChecked = true,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
        };
        formStack.Children.Add(_enabledByDefaultCheck);

        // Buttons
        var buttonStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        var saveButton = new Button
        {
            Content = "Save",
            Style = Application.Current.Resources["AccentButtonStyle"] as Style
        };
        saveButton.Click += SaveOperation;
        buttonStack.Children.Add(saveButton);

        var cancelButton = new Button { Content = "Cancel" };
        cancelButton.Click += (s, e) => HideForm();
        buttonStack.Children.Add(cancelButton);

        formStack.Children.Add(buttonStack);

        formBorder.Child = formStack;
        _addEditFormGrid.Children.Add(formBorder);
    }

    private void CalcTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_calcTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is CalculationType calcType)
        {
            // Update the value box range and label based on calculation type
            if (calcType == CalculationType.FixedHours)
            {
                _valueBox.Maximum = 99;
                _valueBox.SmallChange = 0.1;
            }
            else
            {
                _valueBox.Maximum = 100;
                _valueBox.SmallChange = 5;
            }
        }
    }

    private void ShowAddForm()
    {
        _editingOperation = null;
        ClearForm();
        _addEditFormGrid.Visibility = Visibility.Visible;
    }

    private void ShowEditForm(CustomOperation operation)
    {
        _editingOperation = operation;
        PopulateForm(operation);
        _addEditFormGrid.Visibility = Visibility.Visible;
    }

    private void HideForm()
    {
        _addEditFormGrid.Visibility = Visibility.Collapsed;
        _editingOperation = null;
        ClearForm();
    }

    private void ClearForm()
    {
        _descriptionBox.Text = string.Empty;
        _calcTypeCombo.SelectedIndex = 0;
        _valueBox.Value = 0.5;
        _materialCostBox.Value = 0;
        _opTypeCombo.SelectedIndex = 0;
        _notesBox.Text = string.Empty;
        _enabledByDefaultCheck.IsChecked = true;
    }

    private void PopulateForm(CustomOperation op)
    {
        _descriptionBox.Text = op.Description;

        // Find and select the matching calculation type
        for (int i = 0; i < _calcTypeCombo.Items.Count; i++)
        {
            if (_calcTypeCombo.Items[i] is ComboBoxItem item && item.Tag is CalculationType ct && ct == op.CalculationType)
            {
                _calcTypeCombo.SelectedIndex = i;
                break;
            }
        }

        _valueBox.Value = (double)op.Value;
        _materialCostBox.Value = (double)op.MaterialCost;

        // Find and select the matching operation type
        for (int i = 0; i < _opTypeCombo.Items.Count; i++)
        {
            if (_opTypeCombo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == op.OperationTypeCode)
            {
                _opTypeCombo.SelectedIndex = i;
                break;
            }
        }

        _notesBox.Text = op.Notes;
        _enabledByDefaultCheck.IsChecked = op.EnabledByDefault;
    }

    private async void SaveOperation(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_descriptionBox.Text))
        {
            // Show validation error
            _descriptionBox.Focus(FocusState.Programmatic);
            return;
        }

        var calcType = (_calcTypeCombo.SelectedItem as ComboBoxItem)?.Tag as CalculationType? ?? CalculationType.FixedHours;
        var opType = (_opTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Mat";

        if (_editingOperation != null)
        {
            // Update existing
            _editingOperation.Description = _descriptionBox.Text.Trim();
            _editingOperation.CalculationType = calcType;
            _editingOperation.Value = (decimal)_valueBox.Value;
            _editingOperation.MaterialCost = (decimal)_materialCostBox.Value;
            _editingOperation.OperationTypeCode = opType;
            _editingOperation.Notes = _notesBox.Text.Trim();
            _editingOperation.EnabledByDefault = _enabledByDefaultCheck.IsChecked ?? true;

            await CustomOperationsService.Instance.UpdateOperationAsync(_editingOperation);
        }
        else
        {
            // Create new
            var operation = new CustomOperation
            {
                Description = _descriptionBox.Text.Trim(),
                Category = _category,
                SubCategory = _subCategory,
                CalculationType = calcType,
                Value = (decimal)_valueBox.Value,
                MaterialCost = (decimal)_materialCostBox.Value,
                OperationTypeCode = opType,
                Notes = _notesBox.Text.Trim(),
                EnabledByDefault = _enabledByDefaultCheck.IsChecked ?? true
            };

            await CustomOperationsService.Instance.AddOperationAsync(_category, operation);
        }

        HideForm();
        RefreshOperations();
    }

    private void RefreshOperations()
    {
        _operationsContainer.Children.Clear();

        var operations = CustomOperationsService.Instance.GetOperations(_category)
            .Where(o => string.IsNullOrEmpty(_subCategory) || o.SubCategory == _subCategory)
            .ToList();

        if (operations.Count == 0)
        {
            _operationsContainer.Children.Add(new TextBlock
            {
                Text = "No custom operations yet. Click + to add one.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Margin = new Thickness(0, 4, 0, 4)
            });
        }
        else
        {
            foreach (var op in operations)
            {
                _operationsContainer.Children.Add(CreateOperationRow(op));
            }
        }

        OperationsChanged?.Invoke(this, operations);
    }

    private UIElement CreateOperationRow(CustomOperation op)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 2, 0, 2)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Description and calculation info
        var infoStack = new StackPanel { Spacing = 2 };
        infoStack.Children.Add(new TextBlock
        {
            Text = op.Description,
            Foreground = new SolidColorBrush(Colors.White),
            FontWeight = Microsoft.UI.Text.FontWeights.Medium
        });

        var detailsText = op.GetCalculationDisplay();
        if (op.MaterialCost > 0)
            detailsText += $" + ${op.MaterialCost} material";
        detailsText += $" [{op.OperationTypeCode}]";

        infoStack.Children.Add(new TextBlock
        {
            Text = detailsText,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
            FontSize = 12
        });

        Grid.SetColumn(infoStack, 0);
        grid.Children.Add(infoStack);

        // Preview calculated hours (if percentage-based)
        if (op.CalculationType != CalculationType.FixedHours)
        {
            var previewHours = op.CalculateHours(RepairHours, RefinishHours, 0, RepairHours + RefinishHours);
            var previewText = new TextBlock
            {
                Text = $"= {previewHours:F1} hrs",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            };
            Grid.SetColumn(previewText, 1);
            grid.Children.Add(previewText);
        }

        // Edit button
        var editButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 12 }, // Edit icon
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        editButton.Click += (s, e) => ShowEditForm(op);
        Grid.SetColumn(editButton, 2);
        grid.Children.Add(editButton);

        // Delete button
        var deleteButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 }, // Delete icon
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        deleteButton.Click += async (s, e) =>
        {
            await CustomOperationsService.Instance.DeleteOperationAsync(_category, op.Id);
            RefreshOperations();
        };
        Grid.SetColumn(deleteButton, 3);
        grid.Children.Add(deleteButton);

        border.Child = grid;
        return border;
    }

    /// <summary>
    /// Update the preview calculations when repair/refinish hours change
    /// </summary>
    public void UpdateCalculationPreview(decimal repairHours, decimal refinishHours)
    {
        RepairHours = repairHours;
        RefinishHours = refinishHours;
        RefreshOperations();
    }

    /// <summary>
    /// Get all enabled custom operations with calculated hours
    /// </summary>
    public List<(string Description, decimal Hours, decimal MaterialCost, string OpType)> GetCalculatedOperations(
        decimal repairHours, decimal refinishHours, decimal riHours = 0)
    {
        var totalHours = repairHours + refinishHours + riHours;

        return CustomOperationsService.Instance.GetOperations(_category)
            .Where(o => string.IsNullOrEmpty(_subCategory) || o.SubCategory == _subCategory)
            .Where(o => o.EnabledByDefault)
            .Select(o => (
                o.Description,
                o.CalculateHours(repairHours, refinishHours, riHours, totalHours),
                o.MaterialCost,
                o.OperationTypeCode
            ))
            .ToList();
    }

    /// <summary>
    /// Get all enabled custom operations as OperationRow objects for export
    /// </summary>
    public List<McStudDesktop.Services.OperationRow> GetOperationRows(
        decimal repairHours, decimal refinishHours, decimal riHours = 0, string partName = "")
    {
        var totalHours = repairHours + refinishHours + riHours;

        return CustomOperationsService.Instance.GetOperations(_category)
            .Where(o => string.IsNullOrEmpty(_subCategory) || o.SubCategory == _subCategory)
            .Where(o => o.EnabledByDefault)
            .Select(o =>
            {
                var hours = o.CalculateHours(repairHours, refinishHours, riHours, totalHours);
                var description = string.IsNullOrEmpty(partName) ? o.Description : $"{partName} {o.Description}";

                // Determine if this is a labor or refinish operation based on calc type
                var isRefinishOp = o.CalculationType == CalculationType.PercentOfRefinishTime ||
                                   o.OperationTypeCode == "Refinish" || o.OperationTypeCode == "Ref";

                return new McStudDesktop.Services.OperationRow
                {
                    OperationType = o.OperationTypeCode,
                    Name = description,
                    Quantity = 1,
                    Price = (double)o.MaterialCost,
                    Labor = isRefinishOp ? 0 : (double)hours,
                    Category = o.OperationTypeCode == "Mat" ? "M" : (o.OperationTypeCode == "Refinish" || o.OperationTypeCode == "Ref" ? "R" : "B"),
                    Refinish = isRefinishOp ? (double)hours : 0
                };
            })
            .ToList();
    }

    /// <summary>
    /// Get all enabled custom operations as Operation objects (for BodyOperationsPage, etc.)
    /// </summary>
    public List<McstudDesktop.Models.Operation> GetOperationModels(
        decimal repairHours, decimal refinishHours, decimal riHours = 0, string partName = "")
    {
        var totalHours = repairHours + refinishHours + riHours;

        return CustomOperationsService.Instance.GetOperations(_category)
            .Where(o => string.IsNullOrEmpty(_subCategory) || o.SubCategory == _subCategory)
            .Where(o => o.EnabledByDefault)
            .Select(o =>
            {
                var hours = o.CalculateHours(repairHours, refinishHours, riHours, totalHours);
                var description = string.IsNullOrEmpty(partName) ? o.Description : $"{partName} {o.Description}";

                // Determine if this is a labor or refinish operation based on calc type
                var isRefinishOp = o.CalculationType == CalculationType.PercentOfRefinishTime ||
                                   o.OperationTypeCode == "Refinish" || o.OperationTypeCode == "Ref";

                // Map operation type code to enum
                var opType = o.OperationTypeCode switch
                {
                    "Mat" => McstudDesktop.Models.OperationType.Refinish,
                    "Rpr" => McstudDesktop.Models.OperationType.Repair,
                    "R&I" => McstudDesktop.Models.OperationType.RemoveAndInstall,
                    "Replace" => McstudDesktop.Models.OperationType.Replace,
                    "Refinish" or "Ref" => McstudDesktop.Models.OperationType.Refinish,
                    "Blend" => McstudDesktop.Models.OperationType.Blend,
                    _ => McstudDesktop.Models.OperationType.Repair
                };

                return new McstudDesktop.Models.Operation
                {
                    Description = description,
                    OperationType = opType,
                    Quantity = 1,
                    Price = o.MaterialCost,
                    LaborHours = isRefinishOp ? 0 : hours,
                    RefinishHours = isRefinishOp ? hours : 0,
                    Category = o.OperationTypeCode == "Mat" ? "M" : (isRefinishOp ? "R" : "B")
                };
            })
            .ToList();
    }
}
