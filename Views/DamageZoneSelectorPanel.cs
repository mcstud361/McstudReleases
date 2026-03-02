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
    /// Damage zone selector panel for building estimates.
    /// Uses VehicleDiagramControl for panel selection and suggests operations.
    /// </summary>
    public sealed class DamageZoneSelectorPanel : UserControl
    {
        private VehicleDiagramControl? _vehicleDiagram;
        private ComboBox? _vehicleTypeCombo;
        private ComboBox? _damageTypeCombo;
        private StackPanel? _selectedPanelsDisplay;
        private StackPanel? _suggestedOpsContainer;
        private TextBlock? _summaryText;
        private Button? _generateButton;
        private Button? _clearButton;

        // Track selections
        private readonly Dictionary<string, string> _panelDamageTypes = new(); // panelId -> damageType
        private readonly Dictionary<string, List<SuggestedOperation>> _suggestedOperations = new();

        /// <summary>
        /// Fired when user wants to generate an estimate
        /// </summary>
        public event EventHandler<EstimateGeneratedEventArgs>? OnGenerateEstimate;

        /// <summary>
        /// Fired when selections change
        /// </summary>
        public event EventHandler<SelectionsChangedEventArgs>? OnSelectionsChanged;

        public DamageZoneSelectorPanel()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 32, 38)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) }); // Vehicle diagram
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Right panel

            // === LEFT: Vehicle Diagram ===
            var leftPanel = new StackPanel { Spacing = 12 };

            // Header
            var header = new StackPanel { Spacing = 4 };
            header.Children.Add(new TextBlock
            {
                Text = "🚗 Visual Estimate Builder",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            header.Children.Add(new TextBlock
            {
                Text = "Click panels to select damage, or use quick presets below",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 145, 150))
            });
            leftPanel.Children.Add(header);

            // Quick Preset Buttons - common damage scenarios
            var quickPresetsPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 45, 55)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var presetsStack = new StackPanel { Spacing = 6 };
            presetsStack.Children.Add(new TextBlock
            {
                Text = "⚡ QUICK PRESETS",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });

            var presetsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            // Front End Hit preset
            var frontEndBtn = CreatePresetButton("Front End", "\uE80F", new[] { "front_bumper", "hood", "lf_fender", "rf_fender" });
            presetsRow.Children.Add(frontEndBtn);

            // Rear End Hit preset
            var rearEndBtn = CreatePresetButton("Rear End", "\uE80F", new[] { "rear_bumper", "decklid", "lr_quarter", "rr_quarter" });
            presetsRow.Children.Add(rearEndBtn);

            // Driver Side preset
            var driverSideBtn = CreatePresetButton("Driver Side", "\uE80F", new[] { "lf_fender", "lf_door", "lr_door", "lr_quarter" });
            presetsRow.Children.Add(driverSideBtn);

            // Passenger Side preset
            var passSideBtn = CreatePresetButton("Pass. Side", "\uE80F", new[] { "rf_fender", "rf_door", "rr_door", "rr_quarter" });
            presetsRow.Children.Add(passSideBtn);

            presetsStack.Children.Add(presetsRow);
            quickPresetsPanel.Child = presetsStack;
            leftPanel.Children.Add(quickPresetsPanel);

            // Vehicle type selector
            var vehicleTypePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            vehicleTypePanel.Children.Add(new TextBlock
            {
                Text = "Vehicle:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _vehicleTypeCombo = new ComboBox
            {
                Width = 140,
                FontSize = 12,
                Items = { "Sedan", "Coupe", "SUV", "Truck", "Van" },
                SelectedIndex = 0
            };
            _vehicleTypeCombo.SelectionChanged += VehicleTypeCombo_SelectionChanged;
            vehicleTypePanel.Children.Add(_vehicleTypeCombo);
            leftPanel.Children.Add(vehicleTypePanel);

            // Vehicle diagram
            _vehicleDiagram = new VehicleDiagramControl
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _vehicleDiagram.PanelSelectionChanged += VehicleDiagram_PanelSelectionChanged;
            leftPanel.Children.Add(_vehicleDiagram);

            // Legend
            var legendPanel = CreateLegend();
            leftPanel.Children.Add(legendPanel);

            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            // === RIGHT: Selections & Operations ===
            var rightPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 22, 26, 32)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(16, 0, 0, 0)
            };

            var rightStack = new StackPanel { Spacing = 16 };

            // Selected panels section
            var selectedSection = new StackPanel { Spacing = 8 };
            selectedSection.Children.Add(new TextBlock
            {
                Text = "SELECTED PANELS",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 125, 130))
            });

            _selectedPanelsDisplay = new StackPanel { Spacing = 4 };
            _selectedPanelsDisplay.Children.Add(new TextBlock
            {
                Text = "Click panels on the vehicle to select",
                FontSize = 12,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 105, 110))
            });
            selectedSection.Children.Add(_selectedPanelsDisplay);
            rightStack.Children.Add(selectedSection);

            // Damage type selector (for selected panels)
            var damageTypeSection = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
            damageTypeSection.Children.Add(new TextBlock
            {
                Text = "DAMAGE TYPE",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 125, 130))
            });

            _damageTypeCombo = new ComboBox
            {
                Width = 200,
                FontSize = 12,
                Items =
                {
                    "Replace (New)",
                    "Replace (LKQ/Used)",
                    "Replace (Aftermarket)",
                    "Repair (Minor)",
                    "Repair (Major)",
                    "Refinish Only",
                    "R&I Only",
                    "Blend Only"
                },
                SelectedIndex = 0
            };
            _damageTypeCombo.SelectionChanged += DamageTypeCombo_SelectionChanged;
            damageTypeSection.Children.Add(_damageTypeCombo);
            damageTypeSection.Children.Add(new TextBlock
            {
                Text = "Tip: Select different damage types for each panel",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 130, 160)),
                TextWrapping = TextWrapping.Wrap
            });
            rightStack.Children.Add(damageTypeSection);

            // Suggested operations section
            var opsSection = new StackPanel { Spacing = 8 };
            opsSection.Children.Add(new TextBlock
            {
                Text = "SUGGESTED OPERATIONS",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 125, 130))
            });

            var opsScroll = new ScrollViewer
            {
                MaxHeight = 250,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _suggestedOpsContainer = new StackPanel { Spacing = 4 };
            opsScroll.Content = _suggestedOpsContainer;
            opsSection.Children.Add(opsScroll);
            rightStack.Children.Add(opsSection);

            // Summary
            _summaryText = new TextBlock
            {
                Text = "Select panels to see suggested operations",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 180, 140)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            rightStack.Children.Add(_summaryText);

            // Action buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };

            _clearButton = new Button
            {
                Content = "Clear All",
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 50, 50)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 8, 16, 8),
                CornerRadius = new CornerRadius(4)
            };
            _clearButton.Click += ClearButton_Click;

            _generateButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8B5", FontSize = 14 },
                        new TextBlock { Text = "Generate Estimate" }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(20, 10, 20, 10),
                CornerRadius = new CornerRadius(4),
                IsEnabled = false
            };
            _generateButton.Click += GenerateButton_Click;

            buttonPanel.Children.Add(_clearButton);
            buttonPanel.Children.Add(_generateButton);
            rightStack.Children.Add(buttonPanel);

            rightPanel.Child = rightStack;
            Grid.SetColumn(rightPanel, 1);
            mainGrid.Children.Add(rightPanel);

            mainBorder.Child = mainGrid;
            Content = mainBorder;
        }

        private StackPanel CreateLegend()
        {
            var legend = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };

            legend.Children.Add(CreateLegendItem("Unselected", Color.FromArgb(255, 60, 65, 70)));
            legend.Children.Add(CreateLegendItem("Selected", Color.FromArgb(255, 200, 80, 60)));
            legend.Children.Add(CreateLegendItem("Glass", Color.FromArgb(180, 100, 140, 180)));

            return legend;
        }

        private StackPanel CreateLegendItem(string label, Color color)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            item.Children.Add(new Border
            {
                Width = 16,
                Height = 12,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(color)
            });
            item.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 145, 150)),
                VerticalAlignment = VerticalAlignment.Center
            });
            return item;
        }

        private Button CreatePresetButton(string text, string icon, string[] panelIds)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 10,
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 60, 75)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 85, 100))
            };

            btn.PointerEntered += (s, e) =>
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(255, 70, 90, 120));
            };
            btn.PointerExited += (s, e) =>
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(255, 50, 60, 75));
            };

            btn.Click += (s, e) =>
            {
                // Clear existing selections and apply preset
                _panelDamageTypes.Clear();
                _vehicleDiagram?.ClearSelections();

                var damageType = _damageTypeCombo?.SelectedItem?.ToString() ?? "Replace (New)";
                foreach (var panelId in panelIds)
                {
                    _panelDamageTypes[panelId] = damageType;
                }
                _vehicleDiagram?.SelectPanels(panelIds);

                UpdateSelectedPanelsDisplay();
                UpdateSuggestedOperations();

                OnSelectionsChanged?.Invoke(this, new SelectionsChangedEventArgs(
                    _panelDamageTypes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                ));
            };

            return btn;
        }

        #region Event Handlers

        private void VehicleTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vehicleDiagram == null || _vehicleTypeCombo?.SelectedItem == null) return;

            var vehicleType = _vehicleTypeCombo.SelectedItem.ToString()?.ToLowerInvariant() ?? "sedan";
            _vehicleDiagram.SetVehicleType(vehicleType);
            _panelDamageTypes.Clear();
            UpdateSelectedPanelsDisplay();
            UpdateSuggestedOperations();
        }

        private void VehicleDiagram_PanelSelectionChanged(object? sender, PanelSelectionChangedEventArgs e)
        {
            // Update damage types dictionary
            var currentSelection = e.SelectedPanelIds;

            // Remove deselected panels
            var toRemove = _panelDamageTypes.Keys.Where(k => !currentSelection.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                _panelDamageTypes.Remove(key);
            }

            // Add newly selected panels with default damage type
            foreach (var panelId in currentSelection)
            {
                if (!_panelDamageTypes.ContainsKey(panelId))
                {
                    _panelDamageTypes[panelId] = _damageTypeCombo?.SelectedItem?.ToString() ?? "Replace (New)";
                }
            }

            UpdateSelectedPanelsDisplay();
            UpdateSuggestedOperations();

            OnSelectionsChanged?.Invoke(this, new SelectionsChangedEventArgs(
                _panelDamageTypes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ));
        }

        private void DamageTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update displayed damage types for selected panels
            // This changes the default for newly selected panels
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _vehicleDiagram?.ClearSelections();
            _panelDamageTypes.Clear();
            UpdateSelectedPanelsDisplay();
            UpdateSuggestedOperations();
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var operations = new List<DamageZoneOperation>();

            foreach (var kvp in _suggestedOperations)
            {
                foreach (var op in kvp.Value.Where(o => o.IsSelected))
                {
                    operations.Add(new DamageZoneOperation
                    {
                        PanelId = kvp.Key,
                        PanelName = GetPanelDisplayName(kvp.Key),
                        OperationType = op.OperationType,
                        Description = op.Description,
                        LaborHours = op.LaborHours,
                        Category = op.Category,
                        DamageType = _panelDamageTypes.GetValueOrDefault(kvp.Key, "Replace")
                    });
                }
            }

            OnGenerateEstimate?.Invoke(this, new EstimateGeneratedEventArgs(operations));
        }

        #endregion

        #region UI Updates

        private void UpdateSelectedPanelsDisplay()
        {
            if (_selectedPanelsDisplay == null) return;

            _selectedPanelsDisplay.Children.Clear();

            if (_panelDamageTypes.Count == 0)
            {
                _selectedPanelsDisplay.Children.Add(new TextBlock
                {
                    Text = "Click panels on the vehicle to select",
                    FontSize = 12,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 105, 110))
                });
                _generateButton!.IsEnabled = false;
                return;
            }

            foreach (var kvp in _panelDamageTypes)
            {
                var panelItem = CreateSelectedPanelItem(kvp.Key, kvp.Value);
                _selectedPanelsDisplay.Children.Add(panelItem);
            }

            _generateButton!.IsEnabled = true;
        }

        private Border CreateSelectedPanelItem(string panelId, string damageType)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 45, 52)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Tag = panelId
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Panel name
            var nameText = new TextBlock
            {
                Text = GetPanelDisplayName(panelId),
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 0);
            grid.Children.Add(nameText);

            // Damage type dropdown
            var typeCombo = new ComboBox
            {
                FontSize = 10,
                Width = 120,
                Padding = new Thickness(4, 2, 4, 2),
                Items =
                {
                    "Replace (New)",
                    "Replace (LKQ)",
                    "Replace (AM)",
                    "Repair",
                    "Refinish",
                    "R&I",
                    "Blend"
                },
                Tag = panelId
            };
            typeCombo.SelectedItem = GetShortDamageType(damageType);
            typeCombo.SelectionChanged += (s, e) =>
            {
                if (typeCombo.SelectedItem != null)
                {
                    _panelDamageTypes[panelId] = typeCombo.SelectedItem.ToString()!;
                    UpdateSuggestedOperations();
                }
            };
            Grid.SetColumn(typeCombo, 1);
            grid.Children.Add(typeCombo);

            // Remove button
            var removeBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(4),
                Margin = new Thickness(4, 0, 0, 0),
                Tag = panelId
            };
            removeBtn.Click += (s, e) =>
            {
                _panelDamageTypes.Remove(panelId);
                _vehicleDiagram?.ClearSelections();
                _vehicleDiagram?.SelectPanels(_panelDamageTypes.Keys);
                UpdateSelectedPanelsDisplay();
                UpdateSuggestedOperations();
            };
            Grid.SetColumn(removeBtn, 2);
            grid.Children.Add(removeBtn);

            border.Child = grid;
            return border;
        }

        private void UpdateSuggestedOperations()
        {
            if (_suggestedOpsContainer == null || _summaryText == null) return;

            _suggestedOpsContainer.Children.Clear();
            _suggestedOperations.Clear();

            if (_panelDamageTypes.Count == 0)
            {
                _summaryText.Text = "Select panels to see suggested operations";
                return;
            }

            int totalOps = 0;
            decimal totalHours = 0;

            foreach (var kvp in _panelDamageTypes)
            {
                var panelOps = GetSuggestedOperationsForPanel(kvp.Key, kvp.Value);
                _suggestedOperations[kvp.Key] = panelOps;

                // Panel header
                var panelHeader = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                    Padding = new Thickness(8, 4, 8, 4),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 4, 0, 2)
                };
                panelHeader.Child = new TextBlock
                {
                    Text = $"{GetPanelDisplayName(kvp.Key)} - {kvp.Value}",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 140, 100))
                };
                _suggestedOpsContainer.Children.Add(panelHeader);

                // Operations for this panel
                foreach (var op in panelOps)
                {
                    var opItem = CreateOperationItem(op);
                    _suggestedOpsContainer.Children.Add(opItem);

                    if (op.IsSelected)
                    {
                        totalOps++;
                        totalHours += op.LaborHours;
                    }
                }
            }

            _summaryText.Text = $"Selected: {totalOps} operations, {totalHours:F1} labor hours";
        }

        private Border CreateOperationItem(SuggestedOperation op)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 42)),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(8, 1, 0, 1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); // Checkbox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Hours

            var checkbox = new CheckBox
            {
                IsChecked = op.IsSelected,
                MinWidth = 0,
                Padding = new Thickness(0)
            };
            checkbox.Checked += (s, e) => { op.IsSelected = true; UpdateSummary(); };
            checkbox.Unchecked += (s, e) => { op.IsSelected = false; UpdateSummary(); };
            Grid.SetColumn(checkbox, 0);
            grid.Children.Add(checkbox);

            var descStack = new StackPanel { Spacing = 1 };
            descStack.Children.Add(new TextBlock
            {
                Text = op.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White)
            });
            if (!string.IsNullOrEmpty(op.Category))
            {
                descStack.Children.Add(new TextBlock
                {
                    Text = op.Category,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 140, 180))
                });
            }
            Grid.SetColumn(descStack, 1);
            grid.Children.Add(descStack);

            if (op.LaborHours > 0)
            {
                var hoursText = new TextBlock
                {
                    Text = $"{op.LaborHours:F1} hr",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 150)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(hoursText, 2);
                grid.Children.Add(hoursText);
            }

            border.Child = grid;
            return border;
        }

        private void UpdateSummary()
        {
            if (_summaryText == null) return;

            int totalOps = 0;
            decimal totalHours = 0;

            foreach (var kvp in _suggestedOperations)
            {
                foreach (var op in kvp.Value.Where(o => o.IsSelected))
                {
                    totalOps++;
                    totalHours += op.LaborHours;
                }
            }

            _summaryText.Text = $"Selected: {totalOps} operations, {totalHours:F1} labor hours";
        }

        #endregion

        #region Operation Suggestions

        private List<SuggestedOperation> GetSuggestedOperationsForPanel(string panelId, string damageType)
        {
            var ops = new List<SuggestedOperation>();
            var panelName = GetPanelDisplayName(panelId);
            var isReplace = damageType.ToLowerInvariant().Contains("replace");
            var isRepair = damageType.ToLowerInvariant().Contains("repair");
            var isRefinish = damageType.ToLowerInvariant().Contains("refinish");
            var isBlend = damageType.ToLowerInvariant().Contains("blend");
            var isRI = damageType.ToLowerInvariant().Contains("r&i");
            var isLKQ = damageType.ToLowerInvariant().Contains("lkq") || damageType.ToLowerInvariant().Contains("used");
            var isAM = damageType.ToLowerInvariant().Contains("am") || damageType.ToLowerInvariant().Contains("aftermarket");

            // Main operation
            if (isReplace)
            {
                ops.Add(new SuggestedOperation($"R&R {panelName}", "Body", 0, true, "Replace"));
            }
            else if (isRepair)
            {
                ops.Add(new SuggestedOperation($"Repair {panelName}", "Body", 0, true, "Repair"));
            }
            else if (isRI)
            {
                ops.Add(new SuggestedOperation($"R&I {panelName}", "Body", 0, true, "R&I"));
            }
            else if (isBlend)
            {
                ops.Add(new SuggestedOperation($"Blend {panelName}", "Refinish", 0, true, "Blend"));
            }

            // Refinish (for replace/repair)
            if (isReplace || isRepair)
            {
                ops.Add(new SuggestedOperation($"Refinish {panelName}", "Refinish", 0, true, "Refinish"));
            }

            // Panel-specific operations
            if (panelId.Contains("bumper") || panelId.Contains("front_bumper") || panelId.Contains("rear_bumper"))
            {
                AddBumperOperations(ops, panelId, isReplace, isRepair, isLKQ);
            }
            else if (panelId.Contains("hood"))
            {
                AddHoodOperations(ops, isReplace, isRepair);
            }
            else if (panelId.Contains("fender"))
            {
                AddFenderOperations(ops, panelId, isReplace, isRepair);
            }
            else if (panelId.Contains("door"))
            {
                AddDoorOperations(ops, panelId, isReplace, isRepair);
            }
            else if (panelId.Contains("quarter") || panelId.Contains("bedside"))
            {
                AddQuarterOperations(ops, panelId, isReplace, isRepair);
            }
            else if (panelId.Contains("decklid") || panelId.Contains("liftgate") || panelId.Contains("tailgate"))
            {
                AddRearOperations(ops, panelId, isReplace, isRepair);
            }
            else if (panelId.Contains("roof"))
            {
                AddRoofOperations(ops, isReplace, isRepair);
            }
            else if (panelId.Contains("rocker"))
            {
                AddRockerOperations(ops, isReplace, isRepair);
            }

            // LKQ/Used part operations
            if (isLKQ)
            {
                ops.Add(new SuggestedOperation("Prep & Clean Used Part", "Body", 0.5m, true, "Labor"));
                ops.Add(new SuggestedOperation("Test Fit Used Part", "Body", 0.3m, true, "Labor"));
            }

            // Aftermarket part operations
            if (isAM)
            {
                ops.Add(new SuggestedOperation("Test Fit Aftermarket Part", "Body", 0.3m, true, "Labor"));
            }

            return ops;
        }

        private void AddBumperOperations(List<SuggestedOperation> ops, string panelId, bool isReplace, bool isRepair, bool isLKQ)
        {
            if (isReplace || isRepair)
            {
                ops.Add(new SuggestedOperation("Flex Additive", "Materials", 0, true, "Materials"));
                ops.Add(new SuggestedOperation("Adhesion Promoter", "Materials", 0.2m, true, "Materials"));
            }

            if (isReplace)
            {
                ops.Add(new SuggestedOperation("Transfer Parts from Old Bumper", "Body", 0.5m, true, "Labor"));
            }

            if (isRepair)
            {
                ops.Add(new SuggestedOperation("Plastic Repair Materials", "Materials", 0, true, "Materials"));
            }

            // R&I items
            ops.Add(new SuggestedOperation("R&I Grille", "Body", 0.3m, false, "R&I"));
            ops.Add(new SuggestedOperation("R&I Fog Lamps", "Body", 0.3m, false, "R&I"));
            ops.Add(new SuggestedOperation("R&I Parking Sensors", "Body", 0.2m, false, "R&I"));

            if (panelId.Contains("front"))
            {
                ops.Add(new SuggestedOperation("R&I Front Camera", "Body", 0.3m, false, "R&I"));
            }
        }

        private void AddHoodOperations(List<SuggestedOperation> ops, bool isReplace, bool isRepair)
        {
            if (isReplace)
            {
                ops.Add(new SuggestedOperation("R&I Hood Insulator", "Body", 0.3m, true, "R&I"));
                ops.Add(new SuggestedOperation("Blend LF Fender", "Refinish", 0, true, "Blend"));
                ops.Add(new SuggestedOperation("Blend RF Fender", "Refinish", 0, true, "Blend"));
                ops.Add(new SuggestedOperation("Adjust Hood Hinges", "Body", 0.3m, false, "Labor"));
            }

            if (isRepair)
            {
                ops.Add(new SuggestedOperation("Feather, Prime & Block", "Refinish", 0.5m, true, "Refinish"));
                ops.Add(new SuggestedOperation("Blend LF Fender", "Refinish", 0, true, "Blend"));
                ops.Add(new SuggestedOperation("Blend RF Fender", "Refinish", 0, true, "Blend"));
            }

            ops.Add(new SuggestedOperation("R&I Washer Nozzles", "Body", 0.2m, false, "R&I"));
        }

        private void AddFenderOperations(List<SuggestedOperation> ops, string panelId, bool isReplace, bool isRepair)
        {
            ops.Add(new SuggestedOperation("R&I Fender Liner", "Body", 0.3m, true, "R&I"));

            if (isReplace || isRepair)
            {
                ops.Add(new SuggestedOperation("Blend Hood", "Refinish", 0, true, "Blend"));
                ops.Add(new SuggestedOperation(panelId.Contains("lf") ? "Blend LF Door" : "Blend RF Door", "Refinish", 0, true, "Blend"));
            }

            ops.Add(new SuggestedOperation("R&I Wheel/Tire", "Body", 0.2m, false, "R&I"));
            ops.Add(new SuggestedOperation("R&I Headlamp", "Body", 0.3m, false, "R&I"));
        }

        private void AddDoorOperations(List<SuggestedOperation> ops, string panelId, bool isReplace, bool isRepair)
        {
            if (isReplace)
            {
                ops.Add(new SuggestedOperation("Transfer Door Hardware", "Body", 0.5m, true, "Labor"));
                ops.Add(new SuggestedOperation("Seam Sealer Door Hem", "Materials", 0.2m, true, "Materials"));
            }

            ops.Add(new SuggestedOperation("R&I Door Trim Panel", "Body", 0.3m, true, "R&I"));
            ops.Add(new SuggestedOperation("R&I Mirror", "Body", 0.3m, false, "R&I"));
            ops.Add(new SuggestedOperation("R&I Door Handle", "Body", 0.3m, false, "R&I"));

            // Blend adjacent panels
            if (isReplace || isRepair)
            {
                if (panelId.Contains("lf") || panelId.Contains("l_door"))
                {
                    ops.Add(new SuggestedOperation("Blend LF Fender", "Refinish", 0, false, "Blend"));
                }
                else if (panelId.Contains("rf") || panelId.Contains("r_door"))
                {
                    ops.Add(new SuggestedOperation("Blend RF Fender", "Refinish", 0, false, "Blend"));
                }
            }
        }

        private void AddQuarterOperations(List<SuggestedOperation> ops, string panelId, bool isReplace, bool isRepair)
        {
            if (isReplace)
            {
                // Structural panel - add required items
                ops.Add(new SuggestedOperation("Corrosion Protection", "Materials", 0.5m, true, "Materials"));
                ops.Add(new SuggestedOperation("Weld-Thru Primer", "Materials", 0.3m, true, "Materials"));
                ops.Add(new SuggestedOperation("Seam Sealer", "Materials", 0.8m, true, "Materials"));
                ops.Add(new SuggestedOperation("Sound Deadener", "Materials", 0.3m, false, "Materials"));
                ops.Add(new SuggestedOperation("Pre/Post Measurements", "Body", 0.5m, true, "Labor"));
                ops.Add(new SuggestedOperation("Battery Disconnect/Reconnect", "Electrical", 0.6m, true, "Labor"));
            }

            // R&I items
            ops.Add(new SuggestedOperation("R&I Tail Light", "Body", 0.3m, true, "R&I"));
            ops.Add(new SuggestedOperation("R&I Fuel Door", "Body", 0.2m, false, "R&I"));
            ops.Add(new SuggestedOperation("R&I Interior Trim", "Body", 0.5m, false, "R&I"));

            // Blend
            if (isReplace || isRepair)
            {
                ops.Add(new SuggestedOperation(panelId.Contains("l") ? "Blend LR Door" : "Blend RR Door", "Refinish", 0, true, "Blend"));
                ops.Add(new SuggestedOperation("Blend Rear Bumper", "Refinish", 0, false, "Blend"));
            }
        }

        private void AddRearOperations(List<SuggestedOperation> ops, string panelId, bool isReplace, bool isRepair)
        {
            if (isReplace)
            {
                ops.Add(new SuggestedOperation("Transfer Hardware", "Body", 0.3m, true, "Labor"));
            }

            ops.Add(new SuggestedOperation("R&I License Plate", "Body", 0.1m, false, "R&I"));
            ops.Add(new SuggestedOperation("R&I Spoiler", "Body", 0.3m, false, "R&I"));

            if (panelId.Contains("liftgate") || panelId.Contains("tailgate"))
            {
                ops.Add(new SuggestedOperation("R&I Rear Camera", "Body", 0.3m, false, "R&I"));
            }

            if (isReplace || isRepair)
            {
                ops.Add(new SuggestedOperation("Blend LR Quarter", "Refinish", 0, false, "Blend"));
                ops.Add(new SuggestedOperation("Blend RR Quarter", "Refinish", 0, false, "Blend"));
            }
        }

        private void AddRoofOperations(List<SuggestedOperation> ops, bool isReplace, bool isRepair)
        {
            if (isReplace)
            {
                ops.Add(new SuggestedOperation("R&I Headliner", "Body", 2.0m, true, "R&I"));
                ops.Add(new SuggestedOperation("Corrosion Protection", "Materials", 0.8m, true, "Materials"));
                ops.Add(new SuggestedOperation("Weld-Thru Primer", "Materials", 0.4m, true, "Materials"));
                ops.Add(new SuggestedOperation("Seam Sealer", "Materials", 1.0m, true, "Materials"));
                ops.Add(new SuggestedOperation("Pre/Post Scan", "Diagnostic", 0.5m, true, "Diagnostic"));
            }

            ops.Add(new SuggestedOperation("R&I Roof Moldings", "Body", 0.5m, false, "R&I"));
            ops.Add(new SuggestedOperation("R&I Sunroof (if equipped)", "Body", 1.5m, false, "R&I"));
            ops.Add(new SuggestedOperation("R&I Antenna", "Body", 0.2m, false, "R&I"));
        }

        private void AddRockerOperations(List<SuggestedOperation> ops, bool isReplace, bool isRepair)
        {
            if (isReplace)
            {
                ops.Add(new SuggestedOperation("Corrosion Protection", "Materials", 0.5m, true, "Materials"));
                ops.Add(new SuggestedOperation("Weld-Thru Primer", "Materials", 0.3m, true, "Materials"));
                ops.Add(new SuggestedOperation("Seam Sealer", "Materials", 0.5m, true, "Materials"));
            }

            ops.Add(new SuggestedOperation("R&I Rocker Molding", "Body", 0.2m, false, "R&I"));
        }

        #endregion

        #region Helpers

        private string GetPanelDisplayName(string panelId)
        {
            return panelId switch
            {
                "hood" => "Hood",
                "front_bumper" => "Front Bumper",
                "rear_bumper" => "Rear Bumper",
                "lf_fender" => "LF Fender",
                "rf_fender" => "RF Fender",
                "lf_door" => "LF Door",
                "rf_door" => "RF Door",
                "lr_door" => "LR Door",
                "rr_door" => "RR Door",
                "l_door" => "L Door",
                "r_door" => "R Door",
                "lr_quarter" => "LR Quarter",
                "rr_quarter" => "RR Quarter",
                "l_bedside" => "L Bedside",
                "r_bedside" => "R Bedside",
                "decklid" => "Decklid",
                "liftgate" => "Liftgate",
                "tailgate" => "Tailgate",
                "roof" => "Roof",
                "l_rocker" => "L Rocker",
                "r_rocker" => "R Rocker",
                "lf_mirror" => "LF Mirror",
                "rf_mirror" => "RF Mirror",
                "lf_headlight" => "LF Headlight",
                "rf_headlight" => "RF Headlight",
                "lr_taillight" => "LR Taillight",
                "rr_taillight" => "RR Taillight",
                "sliding_door" => "Sliding Door",
                "r_side" => "R Side",
                _ => panelId.Replace("_", " ").ToUpper()
            };
        }

        private string GetShortDamageType(string damageType)
        {
            if (damageType.Contains("LKQ") || damageType.Contains("Used")) return "Replace (LKQ)";
            if (damageType.Contains("Aftermarket") || damageType.Contains("AM")) return "Replace (AM)";
            if (damageType.Contains("New")) return "Replace (New)";
            if (damageType.Contains("Repair")) return "Repair";
            if (damageType.Contains("Refinish")) return "Refinish";
            if (damageType.Contains("R&I")) return "R&I";
            if (damageType.Contains("Blend")) return "Blend";
            return damageType;
        }

        #endregion
    }

    #region Data Classes

    public class SuggestedOperation
    {
        public string Description { get; set; }
        public string Category { get; set; }
        public decimal LaborHours { get; set; }
        public bool IsSelected { get; set; }
        public string OperationType { get; set; }

        public SuggestedOperation(string description, string category, decimal laborHours, bool isSelected, string operationType)
        {
            Description = description;
            Category = category;
            LaborHours = laborHours;
            IsSelected = isSelected;
            OperationType = operationType;
        }
    }

    public class DamageZoneOperation
    {
        public string PanelId { get; set; } = "";
        public string PanelName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal LaborHours { get; set; }
        public string Category { get; set; } = "";
        public string DamageType { get; set; } = "";
    }

    public class EstimateGeneratedEventArgs : EventArgs
    {
        public List<DamageZoneOperation> Operations { get; }

        public EstimateGeneratedEventArgs(List<DamageZoneOperation> operations)
        {
            Operations = operations;
        }
    }

    public class SelectionsChangedEventArgs : EventArgs
    {
        public Dictionary<string, string> PanelDamageTypes { get; }

        public SelectionsChangedEventArgs(Dictionary<string, string> panelDamageTypes)
        {
            PanelDamageTypes = panelDamageTypes;
        }
    }

    #endregion
}
