#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views;

/// <summary>
/// Dynamic form builder that renders ShopDocTemplates as interactive UI.
/// Supports:
/// - Template selection (Original vs My Templates)
/// - Make a Copy functionality
/// - Edit mode for user templates (add/remove/modify fields)
/// - Fill-out mode for all templates
/// - PDF export
/// </summary>
public class TemplateFormBuilder : UserControl
{
    // Colors
    private static readonly Color CardBg = Color.FromArgb(255, 40, 40, 40);
    private static readonly Color SectionBg = Color.FromArgb(255, 50, 50, 50);
    private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 212);
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);
    private static readonly Color TextWhite = Color.FromArgb(255, 240, 240, 240);
    private static readonly Color TextGray = Color.FromArgb(255, 160, 160, 160);

    private readonly ShopDocTemplateService _templateService;
    private readonly ShopDocType _docType;

    private ShopDocTemplate? _currentTemplate;
    private bool _isEditMode;

    // UI Elements
    private ComboBox? _templateSelector;
    private Button? _makeCopyButton;
    private Button? _editButton;
    private Button? _saveButton;
    private Button? _deleteButton;
    private StackPanel? _formContent;
    private Border? _editModeIndicator;

    // Field value storage (fieldId -> value)
    private Dictionary<string, object> _fieldValues = new();
    private Dictionary<string, UIElement> _fieldControls = new();

    // Charge items state (itemId -> (isSelected, amount, quantity))
    private Dictionary<string, (bool Selected, decimal Amount, decimal Quantity)> _chargeStates = new();

    // Events
    public event EventHandler<ShopDocTemplate>? TemplateChanged;
    public event EventHandler<Dictionary<string, object>>? ExportRequested;

    public TemplateFormBuilder(ShopDocType docType)
    {
        _docType = docType;
        _templateService = ShopDocTemplateService.Instance;
        BuildUI();
        LoadTemplates();
    }

    public ShopDocTemplate? CurrentTemplate => _currentTemplate;
    public Dictionary<string, object> FieldValues => _fieldValues;
    public Dictionary<string, (bool Selected, decimal Amount, decimal Quantity)> ChargeStates => _chargeStates;

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Header/template selector
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) } // Form content
            }
        };

        // Header with template selector
        var header = (FrameworkElement)BuildHeader();
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // Scrollable form content
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16)
        };

        _formContent = new StackPanel { Spacing = 12 };
        scroll.Content = _formContent;

        Grid.SetRow(scroll, 1);
        mainGrid.Children.Add(scroll);

        Content = mainGrid;
    }

    private UIElement BuildHeader()
    {
        var headerCard = new Border
        {
            Background = new SolidColorBrush(CardBg),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var headerStack = new StackPanel { Spacing = 12 };

        // Template selection row
        var selectorRow = new Grid();
        selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Template dropdown
        var selectorStack = new StackPanel { Spacing = 4 };
        selectorStack.Children.Add(new TextBlock
        {
            Text = "Template",
            FontSize = 11,
            Foreground = new SolidColorBrush(TextGray)
        });

        _templateSelector = new ComboBox
        {
            MinWidth = 300,
            PlaceholderText = "Select a template..."
        };
        _templateSelector.SelectionChanged += OnTemplateSelectionChanged;
        selectorStack.Children.Add(_templateSelector);

        Grid.SetColumn(selectorStack, 0);
        selectorRow.Children.Add(selectorStack);

        // Action buttons
        var buttonsStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var makeCopyContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        makeCopyContent.Children.Add(new FontIcon { Glyph = "\uE8C8", FontSize = 14 });
        makeCopyContent.Children.Add(new TextBlock { Text = "Make a Copy", VerticalAlignment = VerticalAlignment.Center });

        _makeCopyButton = new Button
        {
            Content = makeCopyContent,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _makeCopyButton.Click += OnMakeCopyClick;
        buttonsStack.Children.Add(_makeCopyButton);

        var editContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        editContent.Children.Add(new FontIcon { Glyph = "\uE70F", FontSize = 14 });
        editContent.Children.Add(new TextBlock { Text = "Edit Template", VerticalAlignment = VerticalAlignment.Center });

        _editButton = new Button
        {
            Content = editContent,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false,
            Visibility = Visibility.Collapsed
        };
        _editButton.Click += OnEditClick;
        buttonsStack.Children.Add(_editButton);

        var saveContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        saveContent.Children.Add(new FontIcon { Glyph = "\uE74E", FontSize = 14 });
        saveContent.Children.Add(new TextBlock { Text = "Save Changes", VerticalAlignment = VerticalAlignment.Center });

        _saveButton = new Button
        {
            Content = saveContent,
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(AccentGreen),
            Foreground = new SolidColorBrush(Colors.White),
            Visibility = Visibility.Collapsed
        };
        _saveButton.Click += OnSaveClick;
        buttonsStack.Children.Add(_saveButton);

        var deleteContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        deleteContent.Children.Add(new FontIcon { Glyph = "\uE74D", FontSize = 14 });
        deleteContent.Children.Add(new TextBlock { Text = "Delete", VerticalAlignment = VerticalAlignment.Center });

        _deleteButton = new Button
        {
            Content = deleteContent,
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 200, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White),
            Visibility = Visibility.Collapsed
        };
        _deleteButton.Click += OnDeleteClick;
        buttonsStack.Children.Add(_deleteButton);

        Grid.SetColumn(buttonsStack, 1);
        selectorRow.Children.Add(buttonsStack);

        headerStack.Children.Add(selectorRow);

        // Edit mode indicator
        var editModeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        editModeContent.Children.Add(new FontIcon { Glyph = "\uE70F", FontSize = 14, Foreground = new SolidColorBrush(Colors.Black) });
        editModeContent.Children.Add(new TextBlock
        {
            Text = "EDIT MODE - You can modify field labels, add/remove fields, and customize this template",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.Black),
            FontWeight = FontWeights.SemiBold
        });

        _editModeIndicator = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 255, 200, 50)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Visibility = Visibility.Collapsed,
            Child = editModeContent
        };
        headerStack.Children.Add(_editModeIndicator);

        headerCard.Child = headerStack;
        return headerCard;
    }

    private void LoadTemplates()
    {
        if (_templateSelector == null) return;

        _templateSelector.Items.Clear();

        // Add originals
        var originals = _templateService.GetOriginalTemplates(_docType);
        if (originals.Any())
        {
            _templateSelector.Items.Add(new ComboBoxItem
            {
                Content = "— ORIGINAL TEMPLATES —",
                IsEnabled = false,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentBlue)
            });

            foreach (var template in originals)
            {
                _templateSelector.Items.Add(new ComboBoxItem
                {
                    Content = $"  {template.Name}",
                    Tag = template
                });
            }
        }

        // Add user templates
        var userTemplates = _templateService.GetUserTemplates(_docType);
        if (userTemplates.Any())
        {
            _templateSelector.Items.Add(new ComboBoxItem
            {
                Content = "— MY TEMPLATES —",
                IsEnabled = false,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentGreen)
            });

            foreach (var template in userTemplates)
            {
                _templateSelector.Items.Add(new ComboBoxItem
                {
                    Content = $"  {template.Name}",
                    Tag = template
                });
            }
        }

        // Auto-select first template
        if (_templateSelector.Items.Count > 1)
        {
            _templateSelector.SelectedIndex = 1;
        }
    }

    private void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_templateSelector?.SelectedItem is ComboBoxItem item && item.Tag is ShopDocTemplate template)
        {
            LoadTemplate(template);
        }
    }

    private void LoadTemplate(ShopDocTemplate template)
    {
        _currentTemplate = template;
        _isEditMode = false;
        _fieldValues.Clear();
        _fieldControls.Clear();
        _chargeStates.Clear();

        // Update button states
        _makeCopyButton!.IsEnabled = true;
        _editButton!.Visibility = template.IsReadOnly ? Visibility.Collapsed : Visibility.Visible;
        _editButton.IsEnabled = !template.IsReadOnly;
        _saveButton!.Visibility = Visibility.Collapsed;
        _deleteButton!.Visibility = template.IsReadOnly ? Visibility.Collapsed : Visibility.Visible;
        _editModeIndicator!.Visibility = Visibility.Collapsed;

        // Render the form
        RenderForm();

        TemplateChanged?.Invoke(this, template);
    }

    private void RenderForm()
    {
        if (_formContent == null || _currentTemplate == null) return;

        _formContent.Children.Clear();

        // Template info card
        var infoCard = new Border
        {
            Background = new SolidColorBrush(CardBg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var infoStack = new StackPanel { Spacing = 4 };
        infoStack.Children.Add(new TextBlock
        {
            Text = _currentTemplate.Name,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(TextWhite)
        });

        if (!string.IsNullOrEmpty(_currentTemplate.Description))
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = _currentTemplate.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray)
            });
        }

        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        if (_currentTemplate.IsOriginal)
        {
            badgeRow.Children.Add(CreateBadge("Original", AccentBlue));
            badgeRow.Children.Add(CreateBadge("Read-Only Structure", Color.FromArgb(255, 100, 100, 100)));
        }
        else
        {
            badgeRow.Children.Add(CreateBadge("My Template", AccentGreen));
            badgeRow.Children.Add(CreateBadge("Editable", AccentGreen));
        }

        infoStack.Children.Add(badgeRow);
        infoCard.Child = infoStack;
        _formContent.Children.Add(infoCard);

        // Render each section
        foreach (var section in _currentTemplate.Sections.OrderBy(s => s.Order))
        {
            var sectionUI = RenderSection(section);
            _formContent.Children.Add(sectionUI);
        }
    }

    private Border CreateBadge(string text, Color bgColor)
    {
        return new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 4, 8, 4),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            }
        };
    }

    private UIElement RenderSection(TemplateSection section)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(CardBg),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var stack = new StackPanel();

        // Section header
        var header = new Border
        {
            Background = new SolidColorBrush(SectionBg),
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(6, 6, 0, 0)
        };

        var headerContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        if (!string.IsNullOrEmpty(section.Icon))
        {
            headerContent.Children.Add(new FontIcon
            {
                Glyph = section.Icon,
                FontSize = 16,
                Foreground = new SolidColorBrush(AccentBlue)
            });
        }

        headerContent.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(TextWhite)
        });

        // Add field button in edit mode
        if (_isEditMode && !section.IsChargeSection)
        {
            var addFieldBtn = new Button
            {
                Content = "+ Add Field",
                FontSize = 11,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(16, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60))
            };
            addFieldBtn.Click += (s, e) => AddFieldToSection(section);
            headerContent.Children.Add(addFieldBtn);
        }

        header.Child = headerContent;
        stack.Children.Add(header);

        // Section content
        var content = new StackPanel
        {
            Padding = new Thickness(16),
            Spacing = 10
        };

        if (section.IsChargeSection)
        {
            // Render charge items
            foreach (var item in section.ChargeItems.OrderBy(i => i.Order))
            {
                var chargeRow = RenderChargeItem(item, section.IsCheckboxSection);
                content.Children.Add(chargeRow);
            }

            if (section.AllowAddItems || _isEditMode)
            {
                var addChargeBtn = new Button
                {
                    Content = "+ Add Item",
                    Margin = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(12, 6, 12, 6)
                };
                addChargeBtn.Click += (s, e) => AddChargeItem(section);
                content.Children.Add(addChargeBtn);
            }
        }
        else
        {
            // Render fields in a grid layout (2 columns for compact fields)
            var fieldsGrid = new Grid();
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = 0;
            int col = 0;

            foreach (var field in section.Fields.OrderBy(f => f.Order))
            {
                if (field.FieldType == FieldType.MultilineText)
                {
                    // Full width for multiline
                    if (col > 0) { row++; col = 0; }
                    fieldsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var fieldUI = (FrameworkElement)RenderField(field);
                    Grid.SetRow(fieldUI, row);
                    Grid.SetColumn(fieldUI, 0);
                    Grid.SetColumnSpan(fieldUI, 3);
                    fieldsGrid.Children.Add(fieldUI);
                    row++;
                }
                else
                {
                    if (col == 0)
                        fieldsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var fieldUI = (FrameworkElement)RenderField(field);
                    Grid.SetRow(fieldUI, row);
                    Grid.SetColumn(fieldUI, col == 0 ? 0 : 2);
                    fieldsGrid.Children.Add(fieldUI);

                    col++;
                    if (col > 1) { col = 0; row++; }
                }
            }

            content.Children.Add(fieldsGrid);
        }

        stack.Children.Add(content);
        card.Child = stack;
        return card;
    }

    private UIElement RenderField(TemplateField field)
    {
        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };

        // Label row (editable in edit mode)
        var labelRow = new Grid();
        labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (_isEditMode)
        {
            var labelBox = new TextBox
            {
                Text = field.Label,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(0)
            };
            labelBox.TextChanged += (s, e) => field.Label = labelBox.Text;
            labelRow.Children.Add(labelBox);

            var deleteBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 10 },
                Padding = new Thickness(4),
                Background = new SolidColorBrush(Colors.Transparent)
            };
            deleteBtn.Click += (s, e) => RemoveField(field);
            Grid.SetColumn(deleteBtn, 1);
            labelRow.Children.Add(deleteBtn);
        }
        else
        {
            var label = new TextBlock
            {
                Text = field.Label + (field.IsRequired ? " *" : ""),
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray)
            };
            labelRow.Children.Add(label);
        }

        stack.Children.Add(labelRow);

        // Field control based on type
        UIElement control = field.FieldType switch
        {
            FieldType.MultilineText => new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 80,
                Text = field.DefaultValue ?? ""
            },
            FieldType.Number or FieldType.Currency => new NumberBox
            {
                Value = double.TryParse(field.DefaultValue, out var v) ? v : 0,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Minimum = 0
            },
            FieldType.Date => new CalendarDatePicker
            {
                Date = field.DefaultValue == "TODAY" ? DateTimeOffset.Now : null,
                MinWidth = 150
            },
            FieldType.Checkbox => new CheckBox
            {
                IsChecked = field.DefaultValue == "true"
            },
            _ => new TextBox
            {
                Text = field.DefaultValue ?? "",
                PlaceholderText = field.Placeholder ?? ""
            }
        };

        // Store reference and track changes
        _fieldControls[field.Id] = control;
        _fieldValues[field.Id] = field.DefaultValue ?? "";

        // Wire up value change tracking
        if (control is TextBox tb)
        {
            tb.TextChanged += (s, e) => _fieldValues[field.Id] = tb.Text;
        }
        else if (control is NumberBox nb)
        {
            nb.ValueChanged += (s, e) => _fieldValues[field.Id] = nb.Value;
        }
        else if (control is CalendarDatePicker cdp)
        {
            cdp.DateChanged += (s, e) => _fieldValues[field.Id] = cdp.Date?.DateTime ?? DateTime.Now;
        }
        else if (control is CheckBox cb)
        {
            cb.Checked += (s, e) => _fieldValues[field.Id] = true;
            cb.Unchecked += (s, e) => _fieldValues[field.Id] = false;
        }

        stack.Children.Add(control);
        return stack;
    }

    private UIElement RenderChargeItem(TemplateChargeItem item, bool isCheckbox)
    {
        // Initialize state
        if (!_chargeStates.ContainsKey(item.Id))
        {
            _chargeStates[item.Id] = (item.IsEnabled, item.DefaultAmount, 1);
        }

        var row = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 2, 0, 2)
        };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); // Checkbox
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Amount
        if (item.ShowQuantity || item.IsPerMile || item.IsPerDay)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Quantity
        }
        if (_isEditMode)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); // Delete
        }

        // Checkbox for selection
        var checkbox = new CheckBox
        {
            IsChecked = _chargeStates[item.Id].Selected,
            VerticalAlignment = VerticalAlignment.Center
        };
        checkbox.Checked += (s, e) =>
        {
            var state = _chargeStates[item.Id];
            _chargeStates[item.Id] = (true, state.Amount, state.Quantity);
        };
        checkbox.Unchecked += (s, e) =>
        {
            var state = _chargeStates[item.Id];
            _chargeStates[item.Id] = (false, state.Amount, state.Quantity);
        };
        Grid.SetColumn(checkbox, 0);
        row.Children.Add(checkbox);

        // Name (editable in edit mode)
        FrameworkElement nameControl;
        if (_isEditMode)
        {
            var nameBox = new TextBox
            {
                Text = item.Name,
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Center
            };
            nameBox.TextChanged += (s, e) => item.Name = nameBox.Text;
            nameControl = nameBox;
        }
        else
        {
            var suffix = "";
            if (item.IsPerMile) suffix = " (per mile)";
            else if (item.IsPerDay) suffix = " (per day)";

            nameControl = new TextBlock
            {
                Text = item.Name + suffix,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(TextWhite)
            };
        }
        Grid.SetColumn(nameControl, 1);
        row.Children.Add(nameControl);

        // Amount
        var amountBox = new NumberBox
        {
            Value = (double)_chargeStates[item.Id].Amount,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Header = _isEditMode ? "Default $" : "$"
        };
        amountBox.ValueChanged += (s, e) =>
        {
            var state = _chargeStates[item.Id];
            _chargeStates[item.Id] = (state.Selected, (decimal)amountBox.Value, state.Quantity);
            if (_isEditMode) item.DefaultAmount = (decimal)amountBox.Value;
        };
        Grid.SetColumn(amountBox, 2);
        row.Children.Add(amountBox);

        // Quantity (if needed)
        int colIndex = 3;
        if (item.ShowQuantity || item.IsPerMile || item.IsPerDay)
        {
            var qtyLabel = item.QuantityLabel ?? (item.IsPerMile ? "Miles" : item.IsPerDay ? "Days" : "Qty");
            var qtyBox = new NumberBox
            {
                Value = (double)_chargeStates[item.Id].Quantity,
                Minimum = 0,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Header = qtyLabel
            };
            qtyBox.ValueChanged += (s, e) =>
            {
                var state = _chargeStates[item.Id];
                _chargeStates[item.Id] = (state.Selected, state.Amount, (decimal)qtyBox.Value);
            };
            Grid.SetColumn(qtyBox, colIndex);
            row.Children.Add(qtyBox);
            colIndex++;
        }

        // Delete button in edit mode
        if (_isEditMode)
        {
            var deleteBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Center
            };
            deleteBtn.Click += (s, e) => RemoveChargeItem(item);
            Grid.SetColumn(deleteBtn, colIndex);
            row.Children.Add(deleteBtn);
        }

        return row;
    }

    #region Edit Mode Actions

    private void OnMakeCopyClick(object sender, RoutedEventArgs e)
    {
        if (_currentTemplate == null) return;

        var dialog = new ContentDialog
        {
            Title = "Make a Copy",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Enter a name for your copy:", FontSize = 14 },
                    new TextBox
                    {
                        Name = "NameBox",
                        Text = $"My {_currentTemplate.Name}",
                        PlaceholderText = "Template name"
                    }
                }
            },
            PrimaryButtonText = "Create Copy",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        dialog.PrimaryButtonClick += async (s, args) =>
        {
            var content = dialog.Content as StackPanel;
            var nameBox = content?.Children.OfType<TextBox>().FirstOrDefault();
            var name = nameBox?.Text ?? "My Template";

            if (!string.IsNullOrWhiteSpace(name))
            {
                var copy = _templateService.MakeCopy(_currentTemplate, name);
                LoadTemplates(); // Refresh dropdown
                // Select the new copy
                foreach (ComboBoxItem item in _templateSelector!.Items)
                {
                    if (item.Tag is ShopDocTemplate t && t.Id == copy.Id)
                    {
                        _templateSelector.SelectedItem = item;
                        break;
                    }
                }
            }
        };

        _ = dialog.ShowAsync();
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_currentTemplate == null || _currentTemplate.IsReadOnly) return;

        _isEditMode = true;
        _editModeIndicator!.Visibility = Visibility.Visible;
        _editButton!.Visibility = Visibility.Collapsed;
        _saveButton!.Visibility = Visibility.Visible;
        _makeCopyButton!.IsEnabled = false;

        RenderForm();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_currentTemplate == null || _currentTemplate.IsReadOnly) return;

        _templateService.SaveUserTemplate(_currentTemplate);

        _isEditMode = false;
        _editModeIndicator!.Visibility = Visibility.Collapsed;
        _editButton!.Visibility = Visibility.Visible;
        _saveButton!.Visibility = Visibility.Collapsed;
        _makeCopyButton!.IsEnabled = true;

        RenderForm();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_currentTemplate == null || _currentTemplate.IsOriginal) return;

        var dialog = new ContentDialog
        {
            Title = "Delete Template",
            Content = $"Are you sure you want to delete \"{_currentTemplate.Name}\"? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _templateService.DeleteUserTemplate(_currentTemplate);
            LoadTemplates();
        }
    }

    private void AddFieldToSection(TemplateSection section)
    {
        var newField = new TemplateField
        {
            Id = $"field_{DateTime.Now.Ticks}",
            Label = "New Field",
            FieldType = FieldType.Text,
            Order = section.Fields.Count
        };
        section.Fields.Add(newField);
        RenderForm();
    }

    private void RemoveField(TemplateField field)
    {
        if (_currentTemplate == null) return;

        foreach (var section in _currentTemplate.Sections)
        {
            if (section.Fields.Remove(field))
            {
                RenderForm();
                return;
            }
        }
    }

    private void AddChargeItem(TemplateSection section)
    {
        var newItem = new TemplateChargeItem
        {
            Id = $"charge_{DateTime.Now.Ticks}",
            Name = "New Charge",
            DefaultAmount = 0,
            IsEnabled = true,
            Order = section.ChargeItems.Count
        };
        section.ChargeItems.Add(newItem);
        RenderForm();
    }

    private void RemoveChargeItem(TemplateChargeItem item)
    {
        if (_currentTemplate == null) return;

        foreach (var section in _currentTemplate.Sections.Where(s => s.IsChargeSection))
        {
            if (section.ChargeItems.Remove(item))
            {
                RenderForm();
                return;
            }
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Clear all field values (reset form)
    /// </summary>
    public void ClearForm()
    {
        _fieldValues.Clear();
        foreach (var (itemId, _) in _chargeStates.ToList())
        {
            _chargeStates[itemId] = (false, _chargeStates[itemId].Amount, 0);
        }
        RenderForm();
    }

    /// <summary>
    /// Get all data for export
    /// </summary>
    public Dictionary<string, object> GetAllData()
    {
        var data = new Dictionary<string, object>(_fieldValues);

        // Add charge totals
        decimal subtotal = 0;
        var charges = new List<(string Name, decimal Amount)>();

        foreach (var (itemId, state) in _chargeStates)
        {
            if (state.Selected)
            {
                var amount = state.Amount * state.Quantity;
                subtotal += amount;

                // Find the item name
                var item = _currentTemplate?.Sections
                    .SelectMany(s => s.ChargeItems)
                    .FirstOrDefault(i => i.Id == itemId);

                if (item != null)
                {
                    charges.Add((item.Name, amount));
                }
            }
        }

        data["charges"] = charges;
        data["subtotal"] = subtotal;

        if (_currentTemplate?.Settings.IncludeTax == true)
        {
            var tax = subtotal * (_currentTemplate.Settings.TaxRate / 100);
            data["tax"] = tax;
            data["total"] = subtotal + tax;
        }
        else
        {
            data["total"] = subtotal;
        }

        return data;
    }

    /// <summary>
    /// Refresh templates list
    /// </summary>
    public void RefreshTemplates()
    {
        LoadTemplates();
    }

    #endregion
}
