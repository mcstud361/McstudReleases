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
    /// Visual editor for customizing Build Estimate inputs and operation rules.
    /// No code editing required - all done through forms.
    /// </summary>
    public sealed class EstimateBuilderConfigEditor : UserControl
    {
        private readonly EstimateBuilderConfigService _configService;
        private EstimateBuilderConfig _config => _configService.Config;

        // Tab navigation
        private int _selectedTab = 0;
        private Border? _inputsTabBtn;
        private Border? _rulesTabBtn;
        private Grid? _inputsPanel;
        private Grid? _rulesPanel;

        // Inputs editor
        private ComboBox? _sheetSelector;
        private ListView? _inputsList;
        private SheetConfig? _selectedSheet;
        private InputConfig? _selectedInput;

        // Rules editor
        private ListView? _rulesList;
        private OperationRule? _selectedRule;

        // Events
        public event EventHandler? ConfigChanged;

        public EstimateBuilderConfigEditor()
        {
            _configService = EstimateBuilderConfigService.Instance;
            BuildUI();
        }

        private void BuildUI()
        {
            var mainGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30))
            };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Tabs
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // Header
            var header = CreateHeader();
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Tabs
            var tabs = CreateTabs();
            Grid.SetRow(tabs, 1);
            mainGrid.Children.Add(tabs);

            // Content
            var content = new Grid();
            _inputsPanel = CreateInputsPanel();
            _rulesPanel = CreateRulesPanel();
            _rulesPanel.Visibility = Visibility.Collapsed;
            content.Children.Add(_inputsPanel);
            content.Children.Add(_rulesPanel);
            Grid.SetRow(content, 2);
            mainGrid.Children.Add(content);

            // Footer with save button
            var footer = CreateFooter();
            Grid.SetRow(footer, 3);
            mainGrid.Children.Add(footer);

            Content = mainGrid;
        }

        private TextBlock? _statusText;
        private Button? _createCopyBtn;
        private Button? _resetBtn;

        private Border CreateHeader()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftStack = new StackPanel { Spacing = 4 };
            leftStack.Children.Add(new TextBlock
            {
                Text = "Customize Build Estimate",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Status indicator
            var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _statusText = new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            UpdateStatusText();
            statusRow.Children.Add(_statusText);
            leftStack.Children.Add(statusRow);

            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // Action buttons on right
            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            _createCopyBtn = new Button
            {
                Content = "Create My Copy",
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 150)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11
            };
            _createCopyBtn.Click += CreateCopy_Click;
            rightStack.Children.Add(_createCopyBtn);

            _resetBtn = new Button
            {
                Content = "Reset to Default",
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 120, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11
            };
            _resetBtn.Click += ResetToDefault_Click;
            rightStack.Children.Add(_resetBtn);

            UpdateButtonVisibility();

            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(rightStack);

            border.Child = grid;
            return border;
        }

        private void UpdateStatusText()
        {
            if (_statusText == null) return;

            if (_configService.IsUsingCustomConfig)
            {
                _statusText.Text = "Using: YOUR CUSTOM CONFIG (editable)";
                _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));
            }
            else
            {
                _statusText.Text = "Using: DEFAULT CONFIG (read-only) - Click 'Create My Copy' to customize";
                _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100));
            }
        }

        private void UpdateButtonVisibility()
        {
            if (_createCopyBtn != null)
                _createCopyBtn.Visibility = _configService.IsUsingCustomConfig ? Visibility.Collapsed : Visibility.Visible;
            if (_resetBtn != null)
                _resetBtn.Visibility = _configService.IsUsingCustomConfig ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CreateCopy_Click(object sender, RoutedEventArgs e)
        {
            _configService.CreateCustomCopy();
            UpdateStatusText();
            UpdateButtonVisibility();
            RefreshAll();

            // Show confirmation
            var dialog = new ContentDialog
            {
                Title = "Custom Copy Created",
                Content = "Your custom config has been created. You can now edit inputs and operation rules freely!",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async void ResetToDefault_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Reset to Default?",
                Content = "This will delete your custom config and restore the original default settings. Are you sure?",
                PrimaryButtonText = "Yes, Reset",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _configService.ResetToDefault();
                UpdateStatusText();
                UpdateButtonVisibility();
                RefreshAll();
            }
        }

        private void RefreshAll()
        {
            // Refresh sheet selector
            _sheetSelector?.Items.Clear();
            if (_config.Sheets != null)
            {
                foreach (var sheet in _config.Sheets)
                {
                    _sheetSelector?.Items.Add(sheet.Name);
                }
            }
            if (_sheetSelector?.Items.Count > 0)
            {
                _sheetSelector.SelectedIndex = 0;
            }
            RefreshRulesList();
        }

        private StackPanel CreateTabs()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                Padding = new Thickness(8, 4, 8, 4),
                Spacing = 4
            };

            _inputsTabBtn = CreateTabButton("Inputs", 0);
            _rulesTabBtn = CreateTabButton("Operation Rules", 1);

            panel.Children.Add(_inputsTabBtn);
            panel.Children.Add(_rulesTabBtn);

            SelectTab(0);
            return panel;
        }

        private Border CreateTabButton(string text, int index)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(16, 8, 16, 8)
            };

            border.Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            };

            border.PointerPressed += (s, e) => SelectTab(index);
            border.PointerEntered += (s, e) =>
            {
                if (_selectedTab != index)
                    border.Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 55));
            };
            border.PointerExited += (s, e) =>
            {
                if (_selectedTab != index)
                    border.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
            };

            return border;
        }

        private void SelectTab(int index)
        {
            _selectedTab = index;

            _inputsTabBtn!.Background = new SolidColorBrush(
                index == 0 ? Color.FromArgb(255, 0, 100, 150) : Color.FromArgb(255, 45, 45, 45));
            _rulesTabBtn!.Background = new SolidColorBrush(
                index == 1 ? Color.FromArgb(255, 0, 100, 150) : Color.FromArgb(255, 45, 45, 45));

            _inputsPanel!.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            _rulesPanel!.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        #region Inputs Panel

        private Grid CreateInputsPanel()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); // List
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Editor

            // Left: Sheet selector + inputs list
            var leftPanel = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 16, 0) };

            // Sheet selector
            var sheetLabel = new TextBlock
            {
                Text = "Select Sheet:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            leftPanel.Children.Add(sheetLabel);

            _sheetSelector = new ComboBox
            {
                Width = 280,
                FontSize = 12
            };
            if (_config.Sheets != null)
            {
                foreach (var sheet in _config.Sheets)
                {
                    _sheetSelector.Items.Add(sheet.Name);
                }
            }
            _sheetSelector.SelectionChanged += SheetSelector_Changed;
            leftPanel.Children.Add(_sheetSelector);

            // Add input button
            var addInputBtn = new Button
            {
                Content = "+ Add Input",
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0)
            };
            addInputBtn.Click += AddInput_Click;
            leftPanel.Children.Add(addInputBtn);

            // Inputs list
            var listLabel = new TextBlock
            {
                Text = "Inputs in this sheet:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(0, 12, 0, 4)
            };
            leftPanel.Children.Add(listLabel);

            _inputsList = new ListView
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                MaxHeight = 400
            };
            _inputsList.SelectionChanged += InputsList_SelectionChanged;
            leftPanel.Children.Add(_inputsList);

            Grid.SetColumn(leftPanel, 0);
            grid.Children.Add(leftPanel);

            // Right: Input editor (will be populated when input selected)
            var rightPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };
            rightPanel.Child = new TextBlock
            {
                Text = "Select an input to edit, or click '+ Add Input' to create a new one",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(rightPanel, 1);
            grid.Children.Add(rightPanel);

            // Select first sheet
            if (_sheetSelector.Items.Count > 0)
            {
                _sheetSelector.SelectedIndex = 0;
            }

            return grid;
        }

        private void SheetSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_sheetSelector?.SelectedIndex >= 0 && _config.Sheets != null)
            {
                _selectedSheet = _config.Sheets[_sheetSelector.SelectedIndex];
                RefreshInputsList();
            }
        }

        private void RefreshInputsList()
        {
            _inputsList?.Items.Clear();
            if (_selectedSheet?.Inputs == null) return;

            foreach (var input in _selectedSheet.Inputs)
            {
                var item = new Grid { Padding = new Thickness(8, 6, 8, 6) };
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = input.Label,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White)
                });
                stack.Children.Add(new TextBlock
                {
                    Text = $"{input.Type} | {input.Id}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
                });
                Grid.SetColumn(stack, 0);
                item.Children.Add(stack);

                var deleteBtn = new Button
                {
                    Content = "\uE74D",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Padding = new Thickness(6),
                    Background = new SolidColorBrush(Colors.Transparent),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 80, 80)),
                    FontSize = 12,
                    Tag = input.Id
                };
                deleteBtn.Click += DeleteInput_Click;
                Grid.SetColumn(deleteBtn, 1);
                item.Children.Add(deleteBtn);

                item.Tag = input;
                _inputsList?.Items.Add(item);
            }
        }

        private void InputsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_inputsList?.SelectedItem is Grid grid && grid.Tag is InputConfig input)
            {
                _selectedInput = input;
                ShowInputEditor(input, false);
            }
        }

        private void AddInput_Click(object sender, RoutedEventArgs e)
        {
            var newInput = new InputConfig
            {
                Id = $"New_Input_{DateTime.Now.Ticks}",
                Label = "New Input",
                Type = "dropdown",
                Options = new[] { "Option 1", "Option 2" },
                Default = "Option 1",
                Category = "General"
            };
            _selectedInput = newInput;
            ShowInputEditor(newInput, true);
        }

        private void DeleteInput_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string inputId && _selectedSheet != null)
            {
                _selectedSheet.Inputs = _selectedSheet.Inputs?.Where(i => i.Id != inputId).ToList();
                RefreshInputsList();
            }
        }

        private void ShowInputEditor(InputConfig input, bool isNew)
        {
            var grid = _inputsPanel?.Children.OfType<Border>().FirstOrDefault();
            if (grid == null) return;

            var editor = new StackPanel { Spacing = 12 };

            // Header
            editor.Children.Add(new TextBlock
            {
                Text = isNew ? "Add New Input" : "Edit Input",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // ID
            editor.Children.Add(CreateFormField("ID (unique identifier):", input.Id, (val) => input.Id = val));

            // Label
            editor.Children.Add(CreateFormField("Label (display name):", input.Label, (val) => input.Label = val));

            // Type dropdown
            var typePanel = new StackPanel { Spacing = 4 };
            typePanel.Children.Add(new TextBlock
            {
                Text = "Type:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });
            var typeCombo = new ComboBox { Width = 200, FontSize = 12 };
            typeCombo.Items.Add("dropdown");
            typeCombo.Items.Add("checkbox");
            typeCombo.Items.Add("number");
            typeCombo.Items.Add("text");
            typeCombo.SelectedItem = input.Type;
            typeCombo.SelectionChanged += (s, e) => input.Type = typeCombo.SelectedItem?.ToString() ?? "text";
            typePanel.Children.Add(typeCombo);
            editor.Children.Add(typePanel);

            // Category
            editor.Children.Add(CreateFormField("Category:", input.Category ?? "General", (val) => input.Category = val));

            // Options (for dropdown)
            var optionsPanel = new StackPanel { Spacing = 4 };
            optionsPanel.Children.Add(new TextBlock
            {
                Text = "Options (comma-separated, for dropdown):",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });
            var optionsBox = new TextBox
            {
                Text = input.Options != null ? string.Join(", ", input.Options) : "",
                FontSize = 12,
                Width = 300
            };
            optionsBox.TextChanged += (s, e) =>
            {
                input.Options = optionsBox.Text.Split(',').Select(o => o.Trim()).Where(o => !string.IsNullOrEmpty(o)).ToArray();
            };
            optionsPanel.Children.Add(optionsBox);
            editor.Children.Add(optionsPanel);

            // Default value
            editor.Children.Add(CreateFormField("Default value:", input.Default?.ToString() ?? "", (val) => input.Default = val));

            // For number type: min, max, step
            var numberPanel = new StackPanel { Spacing = 4 };
            numberPanel.Children.Add(new TextBlock
            {
                Text = "For number inputs:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(0, 8, 0, 0)
            });
            var numRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            numRow.Children.Add(CreateSmallNumberField("Min:", input.Min ?? 0, (val) => input.Min = val));
            numRow.Children.Add(CreateSmallNumberField("Max:", input.Max ?? 100, (val) => input.Max = val));
            numRow.Children.Add(CreateSmallNumberField("Step:", input.Step ?? 1, (val) => input.Step = val));
            numberPanel.Children.Add(numRow);
            editor.Children.Add(numberPanel);

            // Save button
            var saveBtn = new Button
            {
                Content = isNew ? "Add Input" : "Save Changes",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                Margin = new Thickness(0, 16, 0, 0)
            };
            saveBtn.Click += (s, e) =>
            {
                if (isNew && _selectedSheet != null)
                {
                    _selectedSheet.Inputs ??= new List<InputConfig>();
                    _selectedSheet.Inputs.Add(input);
                }
                RefreshInputsList();
            };
            editor.Children.Add(saveBtn);

            grid.Child = editor;
        }

        private StackPanel CreateFormField(string label, string value, Action<string> onChange)
        {
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });
            var textBox = new TextBox
            {
                Text = value,
                FontSize = 12,
                Width = 300
            };
            textBox.TextChanged += (s, e) => onChange(textBox.Text);
            panel.Children.Add(textBox);
            return panel;
        }

        private StackPanel CreateSmallNumberField(string label, double value, Action<double> onChange)
        {
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            var numBox = new NumberBox
            {
                Value = value,
                Width = 80,
                FontSize = 11,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };
            numBox.ValueChanged += (s, e) => onChange(numBox.Value);
            panel.Children.Add(numBox);
            return panel;
        }

        #endregion

        #region Rules Panel

        private Grid CreateRulesPanel()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) }); // List
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Editor

            // Left: Rules list
            var leftPanel = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 16, 0) };

            // Add rule button
            var addRuleBtn = new Button
            {
                Content = "+ Add Operation Rule",
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12
            };
            addRuleBtn.Click += AddRule_Click;
            leftPanel.Children.Add(addRuleBtn);

            // Explanation
            leftPanel.Children.Add(new TextBlock
            {
                Text = "Operation rules define what operations get generated when you select certain inputs.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Rules list
            _rulesList = new ListView
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                MaxHeight = 450
            };
            _rulesList.SelectionChanged += RulesList_SelectionChanged;
            RefreshRulesList();
            leftPanel.Children.Add(_rulesList);

            Grid.SetColumn(leftPanel, 0);
            grid.Children.Add(leftPanel);

            // Right: Rule editor
            var rightPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };
            rightPanel.Child = new TextBlock
            {
                Text = "Select a rule to edit, or click '+ Add Operation Rule' to create a new one",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(rightPanel, 1);
            grid.Children.Add(rightPanel);

            return grid;
        }

        private void RefreshRulesList()
        {
            _rulesList?.Items.Clear();
            if (_config.OperationRules == null) return;

            foreach (var rule in _config.OperationRules)
            {
                var item = new Grid { Padding = new Thickness(8, 6, 8, 6) };
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var stack = new StackPanel();

                var triggerText = rule.Trigger != null
                    ? $"When {rule.Trigger.InputId} = {rule.Trigger.Equals ?? rule.Trigger.GreaterThan?.ToString() ?? "?"}"
                    : "No trigger";

                stack.Children.Add(new TextBlock
                {
                    Text = triggerText,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var opsText = rule.Operations.Count > 0
                    ? string.Join(", ", rule.Operations.Take(2).Select(o => o.Description ?? o.Type))
                    : "No operations";
                stack.Children.Add(new TextBlock
                {
                    Text = $"→ {opsText}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 100)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(stack, 0);
                item.Children.Add(stack);

                var deleteBtn = new Button
                {
                    Content = "\uE74D",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Padding = new Thickness(6),
                    Background = new SolidColorBrush(Colors.Transparent),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 80, 80)),
                    FontSize = 12,
                    Tag = rule.Id
                };
                deleteBtn.Click += DeleteRule_Click;
                Grid.SetColumn(deleteBtn, 1);
                item.Children.Add(deleteBtn);

                item.Tag = rule;
                _rulesList?.Items.Add(item);
            }
        }

        private void RulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rulesList?.SelectedItem is Grid grid && grid.Tag is OperationRule rule)
            {
                _selectedRule = rule;
                ShowRuleEditor(rule, false);
            }
        }

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            var newRule = new OperationRule
            {
                Id = $"rule_{DateTime.Now.Ticks}",
                Trigger = new OperationTrigger { InputId = "", Equals = "" },
                Operations = new List<OperationDefinition>
                {
                    new OperationDefinition
                    {
                        Type = "Rpr",
                        Description = "New Operation",
                        LaborHours = 0.5m,
                        Category = "Body"
                    }
                }
            };
            _selectedRule = newRule;
            ShowRuleEditor(newRule, true);
        }

        private void DeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string ruleId)
            {
                _configService.RemoveOperationRule(ruleId);
                RefreshRulesList();
            }
        }

        private void ShowRuleEditor(OperationRule rule, bool isNew)
        {
            var border = _rulesPanel?.Children.OfType<Border>().FirstOrDefault();
            if (border == null) return;

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var editor = new StackPanel { Spacing = 12 };

            // Header
            editor.Children.Add(new TextBlock
            {
                Text = isNew ? "Add New Rule" : "Edit Rule",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Trigger section
            editor.Children.Add(new TextBlock
            {
                Text = "TRIGGER - When should this rule fire?",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                Margin = new Thickness(0, 8, 0, 0)
            });

            // Input selector for trigger
            var inputPanel = new StackPanel { Spacing = 4 };
            inputPanel.Children.Add(new TextBlock
            {
                Text = "When this input:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });
            var inputCombo = new ComboBox { Width = 280, FontSize = 12 };
            // Populate with all inputs from all sheets
            if (_config.Sheets != null)
            {
                foreach (var sheet in _config.Sheets)
                {
                    if (sheet.Inputs != null)
                    {
                        foreach (var input in sheet.Inputs)
                        {
                            inputCombo.Items.Add($"{input.Id} ({input.Label})");
                        }
                    }
                    if (sheet.Subcategories != null)
                    {
                        foreach (var sub in sheet.Subcategories)
                        {
                            if (sub.Inputs != null)
                            {
                                foreach (var input in sub.Inputs)
                                {
                                    inputCombo.Items.Add($"{input.Id} ({input.Label})");
                                }
                            }
                        }
                    }
                }
            }
            // Select current
            if (rule.Trigger != null)
            {
                for (int i = 0; i < inputCombo.Items.Count; i++)
                {
                    if (inputCombo.Items[i]?.ToString()?.StartsWith(rule.Trigger.InputId) == true)
                    {
                        inputCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
            inputCombo.SelectionChanged += (s, e) =>
            {
                var selected = inputCombo.SelectedItem?.ToString();
                if (selected != null)
                {
                    var id = selected.Split('(')[0].Trim();
                    rule.Trigger ??= new OperationTrigger();
                    rule.Trigger.InputId = id;
                }
            };
            inputPanel.Children.Add(inputCombo);
            editor.Children.Add(inputPanel);

            // Condition
            editor.Children.Add(CreateFormField("Equals this value:", rule.Trigger?.Equals?.ToString() ?? "", (val) =>
            {
                rule.Trigger ??= new OperationTrigger();
                // Handle boolean values
                if (val.ToLower() == "true") rule.Trigger.Equals = true;
                else if (val.ToLower() == "false") rule.Trigger.Equals = false;
                else rule.Trigger.Equals = val;
            }));

            // Operations section
            editor.Children.Add(new TextBlock
            {
                Text = "OPERATIONS - What operations to generate?",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 150)),
                Margin = new Thickness(0, 16, 0, 0)
            });

            // Show each operation
            for (int i = 0; i < rule.Operations.Count; i++)
            {
                var op = rule.Operations[i];
                var opPanel = CreateOperationEditor(op, i, rule);
                editor.Children.Add(opPanel);
            }

            // Add operation button
            var addOpBtn = new Button
            {
                Content = "+ Add Another Operation",
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11
            };
            addOpBtn.Click += (s, e) =>
            {
                rule.Operations.Add(new OperationDefinition
                {
                    Type = "Rpr",
                    Description = "New Operation",
                    Category = "Body"
                });
                ShowRuleEditor(rule, isNew);
            };
            editor.Children.Add(addOpBtn);

            // Save button
            var saveBtn = new Button
            {
                Content = isNew ? "Add Rule" : "Save Changes",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                Margin = new Thickness(0, 16, 0, 0)
            };
            saveBtn.Click += (s, e) =>
            {
                if (isNew)
                {
                    _configService.AddOperationRule(rule);
                }
                RefreshRulesList();
            };
            editor.Children.Add(saveBtn);

            scroll.Content = editor;
            border.Child = scroll;
        }

        private Border CreateOperationEditor(OperationDefinition op, int index, OperationRule rule)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var stack = new StackPanel { Spacing = 8 };

            // Header with delete
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = $"Operation {index + 1}",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            if (rule.Operations.Count > 1)
            {
                var delBtn = new Button
                {
                    Content = "Remove",
                    Padding = new Thickness(6, 2, 6, 2),
                    FontSize = 10,
                    Background = new SolidColorBrush(Color.FromArgb(255, 120, 50, 50)),
                    Foreground = new SolidColorBrush(Colors.White)
                };
                delBtn.Click += (s, e) =>
                {
                    rule.Operations.RemoveAt(index);
                    ShowRuleEditor(rule, false);
                };
                Grid.SetColumn(delBtn, 1);
                header.Children.Add(delBtn);
            }
            stack.Children.Add(header);

            // Type
            var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            var typePanel = new StackPanel { Spacing = 2 };
            typePanel.Children.Add(new TextBlock { Text = "Type:", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)) });
            var typeCombo = new ComboBox { Width = 100, FontSize = 11 };
            typeCombo.Items.Add("Rpr");
            typeCombo.Items.Add("Rfn");
            typeCombo.Items.Add("Mat");
            typeCombo.Items.Add("Sub");
            typeCombo.Items.Add("M");
            typeCombo.SelectedItem = op.Type ?? "Rpr";
            typeCombo.SelectionChanged += (s, e) => op.Type = typeCombo.SelectedItem?.ToString();
            typePanel.Children.Add(typeCombo);
            typeRow.Children.Add(typePanel);

            var catPanel = new StackPanel { Spacing = 2 };
            catPanel.Children.Add(new TextBlock { Text = "Category:", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)) });
            var catBox = new TextBox { Text = op.Category ?? "", Width = 120, FontSize = 11 };
            catBox.TextChanged += (s, e) => op.Category = catBox.Text;
            catPanel.Children.Add(catBox);
            typeRow.Children.Add(catPanel);
            stack.Children.Add(typeRow);

            // Description
            var descPanel = new StackPanel { Spacing = 2 };
            descPanel.Children.Add(new TextBlock { Text = "Description:", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)) });
            var descBox = new TextBox { Text = op.Description ?? "", Width = 300, FontSize = 11 };
            descBox.TextChanged += (s, e) => op.Description = descBox.Text;
            descPanel.Children.Add(descBox);
            stack.Children.Add(descPanel);

            // Hours and Price
            var valuesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

            var laborPanel = new StackPanel { Spacing = 2 };
            laborPanel.Children.Add(new TextBlock { Text = "Labor Hours:", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)) });
            var laborBox = new NumberBox { Value = (double)(op.LaborHours ?? 0), Width = 80, FontSize = 11, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            laborBox.ValueChanged += (s, e) => op.LaborHours = (decimal)laborBox.Value;
            laborPanel.Children.Add(laborBox);
            valuesRow.Children.Add(laborPanel);

            var refinishPanel = new StackPanel { Spacing = 2 };
            refinishPanel.Children.Add(new TextBlock { Text = "Refinish Hours:", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)) });
            var refinishBox = new NumberBox { Value = (double)(op.RefinishHours ?? 0), Width = 80, FontSize = 11, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            refinishBox.ValueChanged += (s, e) => op.RefinishHours = (decimal)refinishBox.Value;
            refinishPanel.Children.Add(refinishBox);
            valuesRow.Children.Add(refinishPanel);

            var pricePanel = new StackPanel { Spacing = 2 };
            pricePanel.Children.Add(new TextBlock { Text = "Price $:", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)) });
            var priceBox = new NumberBox { Value = (double)(op.Price ?? 0), Width = 80, FontSize = 11, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            priceBox.ValueChanged += (s, e) => op.Price = (decimal)priceBox.Value;
            pricePanel.Children.Add(priceBox);
            valuesRow.Children.Add(pricePanel);

            stack.Children.Add(valuesRow);

            border.Child = stack;
            return border;
        }

        #endregion

        #region Footer

        private Button? _saveBtn;
        private TextBlock? _footerInfoText;

        private Border CreateFooter()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _footerInfoText = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            UpdateFooterText();
            Grid.SetColumn(_footerInfoText, 0);
            grid.Children.Add(_footerInfoText);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            _saveBtn = new Button
            {
                Content = "Save All & Apply",
                Padding = new Thickness(20, 10, 20, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            _saveBtn.Click += SaveConfig_Click;
            btnPanel.Children.Add(_saveBtn);

            UpdateSaveButtonState();

            Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(btnPanel);

            border.Child = grid;
            return border;
        }

        private void UpdateFooterText()
        {
            if (_footerInfoText == null) return;

            if (_configService.IsUsingCustomConfig)
            {
                _footerInfoText.Text = "Click 'Save All & Apply' to save your changes.";
                _footerInfoText.Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120));
            }
            else
            {
                _footerInfoText.Text = "Default config is READ-ONLY. Click 'Create My Copy' above to make changes.";
                _footerInfoText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100));
            }
        }

        private void UpdateSaveButtonState()
        {
            if (_saveBtn == null) return;

            _saveBtn.IsEnabled = _configService.IsUsingCustomConfig;
            _saveBtn.Background = new SolidColorBrush(
                _configService.IsUsingCustomConfig
                    ? Color.FromArgb(255, 0, 120, 80)
                    : Color.FromArgb(255, 60, 60, 60));
        }

        private async void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!_configService.IsUsingCustomConfig)
            {
                var dialog = new ContentDialog
                {
                    Title = "Cannot Save",
                    Content = "The default config is read-only. Click 'Create My Copy' first to make your own editable version.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            _configService.SaveConfig();
            ConfigChanged?.Invoke(this, EventArgs.Empty);

            // Show confirmation
            var confirmDialog = new ContentDialog
            {
                Title = "Saved!",
                Content = "Your custom config has been saved and applied.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await confirmDialog.ShowAsync();
        }

        #endregion
    }
}
