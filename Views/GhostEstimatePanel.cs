#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using McStudDesktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Estimate Assistant - Damage Guidance Panel
    ///
    /// Describe the damage, generate a comprehensive list of operations
    /// pulled from all data sources (MET/CCC database, operation knowledge base,
    /// learned patterns from uploaded estimates).
    /// </summary>
    public sealed class GhostEstimatePanel : UserControl
    {
        private readonly GhostEstimateService _ghostService;
        private readonly EstimateAIAdvisorService _advisorService;
        private readonly VehicleDiagramControl _vehicleDiagram;

        // Input fields
        private TextBox? _vehicleInfoBox;
        private TextBox? _damageDescriptionBox;
        private ComboBox? _severityCombo;
        private Button? _generateGhostButton;


        // Toolbar controls
        private ComboBox? _categoryFilter;
        private ComboBox? _confidenceFilter;
        private Button? _copyAllButton;
        private Button? _copySelectedButton;
        private Button? _exportButton;

        // Results display
        private StackPanel? _resultsPanel;
        private StackPanel? _summaryCardsPanel;
        private TextBlock? _statusText;

        // State
        private GuidanceEstimateResult? _currentResult;
        private readonly Dictionary<string, bool> _collapsedCategories = new();
        private readonly Dictionary<GuidanceOperation, CheckBox> _operationCheckboxes = new();

        // Events
        public event EventHandler<GhostOperation>? OnOperationAccepted;
        public event EventHandler? OnNavigateToReference;

        // CCC section display order
        private static readonly string[] SectionOrder = new[]
        {
            "FRONT BUMPER & GRILLE", "REAR BUMPER",
            "FRONT LAMPS", "REAR LAMPS",
            "RADIATOR SUPPORT",
            "HOOD",
            "FENDER",
            "FRONT DOOR", "REAR DOOR",
            "QUARTER PANEL",
            "PILLARS, ROCKER & FLOOR",
            "ROOF",
            "TRUNK / DECKLID",
            "GLASS",
            "FRAME",
            "RESTRAINT SYSTEMS",
            "ELECTRICAL",
            "INSTRUMENT PANEL",
            "VEHICLE DIAGNOSTICS",
            "MECHANICAL",
            "MISCELLANEOUS OPERATIONS"
        };

        // Section colors — part sections (blue), structural (red), diagnostics (green), electrical (yellow), misc (gray)
        private static readonly Dictionary<string, Color> SectionColors = new()
        {
            // Part sections — blue tones
            ["FRONT BUMPER & GRILLE"] = Color.FromArgb(255, 100, 180, 255),
            ["REAR BUMPER"] = Color.FromArgb(255, 100, 180, 255),
            ["FRONT LAMPS"] = Color.FromArgb(255, 130, 190, 255),
            ["REAR LAMPS"] = Color.FromArgb(255, 130, 190, 255),
            ["RADIATOR SUPPORT"] = Color.FromArgb(255, 100, 170, 240),
            ["HOOD"] = Color.FromArgb(255, 100, 180, 255),
            ["FENDER"] = Color.FromArgb(255, 100, 180, 255),
            ["FRONT DOOR"] = Color.FromArgb(255, 100, 180, 255),
            ["REAR DOOR"] = Color.FromArgb(255, 100, 180, 255),
            ["QUARTER PANEL"] = Color.FromArgb(255, 100, 180, 255),
            ["ROOF"] = Color.FromArgb(255, 100, 180, 255),
            ["TRUNK / DECKLID"] = Color.FromArgb(255, 100, 180, 255),
            ["GLASS"] = Color.FromArgb(255, 130, 200, 240),
            ["INSTRUMENT PANEL"] = Color.FromArgb(255, 130, 190, 255),
            // Structural — red tones
            ["PILLARS, ROCKER & FLOOR"] = Color.FromArgb(255, 255, 130, 130),
            ["FRAME"] = Color.FromArgb(255, 255, 130, 130),
            ["RESTRAINT SYSTEMS"] = Color.FromArgb(255, 255, 100, 100),
            // Diagnostics — green
            ["VEHICLE DIAGNOSTICS"] = Color.FromArgb(255, 100, 220, 180),
            // Electrical — yellow
            ["ELECTRICAL"] = Color.FromArgb(255, 255, 200, 80),
            // Mechanical — blue-green
            ["MECHANICAL"] = Color.FromArgb(255, 130, 200, 255),
            // Misc — gray
            ["MISCELLANEOUS OPERATIONS"] = Color.FromArgb(255, 180, 185, 190),
        };

        public GhostEstimatePanel()
        {
            _ghostService = GhostEstimateService.Instance;
            _advisorService = EstimateAIAdvisorService.Instance;
            _vehicleDiagram = new VehicleDiagramControl();
            _vehicleDiagram.PanelSelectionChanged += VehicleDiagram_SelectionChanged;

            BuildUI();
        }

        private void BuildUI()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 32, 38)),
                Padding = new Thickness(16)
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var mainStack = new StackPanel { Spacing = 20 };

            // 1. Header
            mainStack.Children.Add(CreateHeader());

            // Reference tab banner
            mainStack.Children.Add(CreateReferenceTabBanner());

            // 2. Input Section (kept as-is: vehicle, damage desc, severity, diagram)
            mainStack.Children.Add(CreateInputSection());

            // 3. Toolbar (filters + action buttons)
            mainStack.Children.Add(CreateToolbar());

            // 4. Results Output (categorized operations)
            mainStack.Children.Add(CreateResultsOutputSection());

            scrollViewer.Content = mainStack;
            mainBorder.Child = scrollViewer;
            Content = mainBorder;
        }

        private StackPanel CreateHeader()
        {
            var panel = new StackPanel { Spacing = 8 };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            titleRow.Children.Add(new FontIcon
            {
                Glyph = "\uE8F4",
                FontSize = 28,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "Estimate Assistant - Damage Guidance",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Settings gear button
            var settingsButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE713", FontSize = 18 },
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            ToolTipService.SetToolTip(settingsButton, "Ghost Estimate Settings");
            settingsButton.Click += SettingsButton_Click;
            titleRow.Children.Add(settingsButton);

            panel.Children.Add(titleRow);

            var descriptionPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 65)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 4, 0, 0)
            };

            var descStack = new StackPanel { Spacing = 6 };
            descStack.Children.Add(new TextBlock
            {
                Text = "How It Works:",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });

            var steps = new[]
            {
                "1. Describe the damage (vehicle info + damage description + click diagram)",
                "2. Click \"Generate Estimate Guide\" to pull operations from all data sources",
                "3. Review categorized operations with confidence levels and justifications",
                "4. Copy or export the operations you need to your estimate"
            };

            foreach (var step in steps)
            {
                descStack.Children.Add(new TextBlock
                {
                    Text = step,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190)),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            descriptionPanel.Child = descStack;
            panel.Children.Add(descriptionPanel);

            return panel;
        }

        private Border CreateReferenceTabBanner()
        {
            var banner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 45, 60)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 90, 120)),
                BorderThickness = new Thickness(1)
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new FontIcon
            {
                Glyph = "\uE82D",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });

            var linkText = new TextBlock
            {
                Text = "P-Pages, DEG inquiries, and supporting documents for these operations are available on the Reference tab",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 180, 220)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(linkText);

            var goBtn = new Button
            {
                Content = "Open Reference Tab",
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 70, 100)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center
            };
            goBtn.Click += (s, e) => OnNavigateToReference?.Invoke(this, EventArgs.Empty);
            row.Children.Add(goBtn);

            banner.Child = row;
            return banner;
        }

        private Border CreateInputSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            // Left: Input fields
            var inputStack = new StackPanel { Spacing = 16 };

            inputStack.Children.Add(new TextBlock
            {
                Text = "STEP 1: Describe the Damage",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });

            // Vehicle info
            var vehicleStack = new StackPanel { Spacing = 4 };
            vehicleStack.Children.Add(new TextBlock
            {
                Text = "Vehicle (Year Make Model)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });
            _vehicleInfoBox = new TextBox
            {
                PlaceholderText = "e.g., 2022 Toyota Camry LE",
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 65, 70, 80))
            };
            vehicleStack.Children.Add(_vehicleInfoBox);
            inputStack.Children.Add(vehicleStack);

            // Damage description
            var damageStack = new StackPanel { Spacing = 4 };
            damageStack.Children.Add(new TextBlock
            {
                Text = "Damage Description",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });
            _damageDescriptionBox = new TextBox
            {
                PlaceholderText = "e.g., Front end collision, bumper pushed in, hood buckled at front edge, headlight broken...",
                FontSize = 13,
                Height = 80,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 65, 70, 80))
            };
            damageStack.Children.Add(_damageDescriptionBox);
            inputStack.Children.Add(damageStack);

            // Severity selector
            var severityStack = new StackPanel { Spacing = 4 };
            severityStack.Children.Add(new TextBlock
            {
                Text = "Damage Severity",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });
            _severityCombo = new ComboBox
            {
                Width = 200,
                FontSize = 13,
                Items = { "Light (scuffs, minor dents)", "Moderate (dents, creases)", "Heavy (buckled, kinked)", "Severe (structural)" },
                SelectedIndex = 1
            };
            severityStack.Children.Add(_severityCombo);
            inputStack.Children.Add(severityStack);

            // Generate button
            _generateGhostButton = new Button
            {
                Content = "Generate Estimate Guide",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(20, 10, 20, 10),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 130, 200)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6)
            };
            _generateGhostButton.Click += GenerateGhostButton_Click;
            inputStack.Children.Add(_generateGhostButton);

            Grid.SetColumn(inputStack, 0);
            grid.Children.Add(inputStack);

            // Right: Vehicle diagram
            var diagramStack = new StackPanel { Spacing = 8, Margin = new Thickness(16, 0, 0, 0) };
            diagramStack.Children.Add(new TextBlock
            {
                Text = "Click Damaged Areas (Optional)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            _vehicleDiagram.HorizontalAlignment = HorizontalAlignment.Center;
            diagramStack.Children.Add(_vehicleDiagram);

            Grid.SetColumn(diagramStack, 1);
            grid.Children.Add(diagramStack);

            border.Child = grid;
            return border;
        }

        private Border CreateToolbar()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed
            };
            border.Name = "ToolbarBorder";

            var toolbarGrid = new Grid();
            toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Filters
            var filtersPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            filtersPanel.Children.Add(new TextBlock
            {
                Text = "Filter:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _categoryFilter = new ComboBox
            {
                Width = 200,
                FontSize = 12,
                Items = { "All Sections", "Body Panels", "Structural / Frame",
                          "Diagnostics", "Electrical", "Mechanical", "Misc Operations" },
                SelectedIndex = 0
            };
            _categoryFilter.SelectionChanged += FilterChanged;
            filtersPanel.Children.Add(_categoryFilter);

            _confidenceFilter = new ComboBox
            {
                Width = 150,
                FontSize = 12,
                Items = { "All Confidence", "High Only", "Medium & High" },
                SelectedIndex = 0
            };
            _confidenceFilter.SelectionChanged += FilterChanged;
            filtersPanel.Children.Add(_confidenceFilter);

            Grid.SetColumn(filtersPanel, 0);
            toolbarGrid.Children.Add(filtersPanel);

            // Right: Action buttons
            var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            _copySelectedButton = new Button
            {
                Content = "Copy Selected",
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            _copySelectedButton.Click += CopySelectedButton_Click;
            actionsPanel.Children.Add(_copySelectedButton);

            _copyAllButton = new Button
            {
                Content = "Copy All",
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 130, 200)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            _copyAllButton.Click += CopyAllButton_Click;
            actionsPanel.Children.Add(_copyAllButton);

            _exportButton = new Button
            {
                Content = "Export",
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            _exportButton.Click += ExportButton_Click;
            actionsPanel.Children.Add(_exportButton);

            Grid.SetColumn(actionsPanel, 1);
            toolbarGrid.Children.Add(actionsPanel);

            border.Child = toolbarGrid;
            return border;
        }

        private Border CreateResultsOutputSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed
            };
            border.Name = "ResultsBorder";

            var stack = new StackPanel { Spacing = 16 };

            // Summary cards row
            _summaryCardsPanel = new StackPanel { Spacing = 0 };
            stack.Children.Add(_summaryCardsPanel);

            // Status text
            _statusText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(_statusText);

            // Operations output
            _resultsPanel = new StackPanel { Spacing = 12 };
            stack.Children.Add(_resultsPanel);

            border.Child = stack;
            return border;
        }

        #region Event Handlers

        private void VehicleDiagram_SelectionChanged(object? sender, PanelSelectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[Guidance] Panels selected: {string.Join(", ", e.SelectedPanelIds)}");
        }

        private void GenerateGhostButton_Click(object sender, RoutedEventArgs e)
        {
            var vehicleInfo = _vehicleInfoBox?.Text?.Trim() ?? "";
            var damageDesc = _damageDescriptionBox?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(damageDesc))
            {
                ShowMessage("Please enter a damage description");
                return;
            }

            var severity = _severityCombo?.SelectedIndex switch
            {
                0 => "light",
                1 => "moderate",
                2 => "heavy",
                3 => "severe",
                _ => "moderate"
            };

            var selectedPanels = _vehicleDiagram.SelectedPanels.ToList();
            var impactZones = new List<string>();
            foreach (var panel in selectedPanels)
            {
                impactZones.Add(panel);
            }

            var input = new GhostEstimateInput
            {
                VehicleInfo = vehicleInfo,
                DamageDescription = damageDesc,
                Severity = severity,
                ImpactZones = impactZones,
                SelectedPanels = selectedPanels
            };

            // Generate guidance estimate
            _currentResult = _ghostService.GenerateGuidanceEstimate(input);

            // Show toolbar and results
            ShowToolbarAndResults();

            // Display
            DisplayGuidanceResults(_currentResult);
        }

        private void FilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentResult != null)
                DisplayGuidanceResults(_currentResult);
        }

        private void CopyAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResult == null) return;
            var ops = GetFilteredOperations(_currentResult.GuidanceOperations);
            CopyOperationsToClipboard(ops);
            ShowMessage($"Copied {ops.Count} operations to clipboard");
        }

        private void CopySelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResult == null) return;
            var selected = _operationCheckboxes
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key)
                .ToList();

            if (!selected.Any())
            {
                ShowMessage("No operations selected. Check the boxes next to operations to select them.");
                return;
            }

            CopyOperationsToClipboard(selected);
            ShowMessage($"Copied {selected.Count} selected operations to clipboard");
        }

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var configView = new GhostConfigView();
            var dialog = new ContentDialog
            {
                Title = "Ghost Estimate Settings",
                Content = configView,
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            };
            await dialog.ShowAsync();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResult == null) return;
            var ops = GetFilteredOperations(_currentResult.GuidanceOperations);
            CopyOperationsToClipboard(ops, includeDetails: true);
            ShowMessage($"Exported {ops.Count} operations with details to clipboard");
        }

        #endregion

        #region Display Methods

        private void ShowToolbarAndResults()
        {
            // Find toolbar and results borders by walking the visual tree
            var mainBorder = Content as Border;
            var scrollViewer = mainBorder?.Child as ScrollViewer;
            var mainStack = scrollViewer?.Content as StackPanel;
            if (mainStack == null) return;

            foreach (var child in mainStack.Children)
            {
                if (child is Border b)
                {
                    if (b.Name == "ToolbarBorder" || b.Name == "ResultsBorder")
                        b.Visibility = Visibility.Visible;
                }
            }
        }

        private void DisplayGuidanceResults(GuidanceEstimateResult result)
        {
            _operationCheckboxes.Clear();
            _resultsPanel?.Children.Clear();
            _summaryCardsPanel?.Children.Clear();

            var filteredOps = GetFilteredOperations(result.GuidanceOperations);

            // Build summary cards
            BuildSummaryCards(result, filteredOps);

            // Build warnings/tips
            BuildWarningsAndTips(result);

            // Group operations by CCC section and display
            var grouped = filteredOps
                .GroupBy(o => !string.IsNullOrEmpty(o.Section) ? o.Section : o.Category)
                .OrderBy(g => Array.IndexOf(SectionOrder, g.Key) is int idx && idx >= 0 ? idx : 99);

            foreach (var group in grouped)
            {
                var categorySection = CreateCategorySection(group.Key, group.ToList());
                _resultsPanel?.Children.Add(categorySection);
            }

            // Notes
            if (result.Notes.Any())
            {
                var notesPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var note in result.Notes)
                {
                    notesPanel.Children.Add(new TextBlock
                    {
                        Text = $"Note: {note}",
                        FontSize = 11,
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                _resultsPanel?.Children.Add(notesPanel);
            }

            // Learned Suggested Additions
            AppendAdvisorSuggestions(result);
        }

        private void AppendAdvisorSuggestions(GuidanceEstimateResult result)
        {
            try
            {
                var vehicleInfo = _vehicleInfoBox?.Text?.Trim();
                _advisorService.SetSessionContext(vehicleInfo, null);

                // Track what's already in the guide so advisor can find gaps
                foreach (var op in result.GuidanceOperations)
                {
                    _advisorService.TrackEnteredOperation(op.PartName, op.OperationType, op.LaborHours);
                }

                var response = _advisorService.ProcessAdvisorQuery("what am I missing");
                if (response == null || response.Sections.Count == 0) return;

                // Filter out items already in the guide
                var existingKeys = result.GuidanceOperations
                    .Select(o => o.Description?.ToLowerInvariant() ?? "")
                    .ToHashSet();

                var newItems = response.Sections
                    .SelectMany(s => s.Items)
                    .Where(item => !existingKeys.Contains(item.Description?.ToLowerInvariant() ?? ""))
                    .ToList();

                if (newItems.Count == 0) return;

                // Build the section
                var sectionBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 42)),
                    CornerRadius = new CornerRadius(8),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 130, 80)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 4, 0, 0)
                };

                var stack = new StackPanel();

                // Header
                var headerBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 40)),
                    CornerRadius = new CornerRadius(8, 8, 0, 0),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                headerRow.Children.Add(new TextBlock
                {
                    Text = "\uD83E\uDDE0 Learned Suggested Additions",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 140)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                headerRow.Children.Add(new TextBlock
                {
                    Text = $"({newItems.Count} from learned patterns)",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                headerBorder.Child = headerRow;
                stack.Children.Add(headerBorder);

                // Items
                var itemsPanel = new StackPanel { Spacing = 2, Padding = new Thickness(4) };
                foreach (var item in newItems.Take(15))
                {
                    itemsPanel.Children.Add(CreateAdvisorSuggestionRow(item));
                }
                stack.Children.Add(itemsPanel);

                // Summary
                if (!string.IsNullOrWhiteSpace(response.Summary))
                {
                    var summaryBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 30, 50, 50)),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(4, 0, 4, 4),
                        CornerRadius = new CornerRadius(4)
                    };
                    summaryBorder.Child = new TextBlock
                    {
                        Text = response.Summary,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 180)),
                        TextWrapping = TextWrapping.Wrap
                    };
                    stack.Children.Add(summaryBorder);
                }

                sectionBorder.Child = stack;
                _resultsPanel?.Children.Add(sectionBorder);
            }
            catch { }
        }

        private Border CreateAdvisorSuggestionRow(AdvisorItem item)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 42, 47, 56)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 1, 0, 1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Description
            var desc = new TextBlock
            {
                Text = item.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(desc, 0);
            grid.Children.Add(desc);

            // Confidence badge
            var (confColor, confText) = item.Confidence switch
            {
                >= 0.7 => (Color.FromArgb(255, 80, 190, 80), "HIGH"),
                >= 0.4 => (Color.FromArgb(255, 220, 180, 60), "MED"),
                _ => (Color.FromArgb(255, 220, 130, 60), "LOW")
            };
            var confBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, confColor.R, confColor.G, confColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            confBadge.Child = new TextBlock
            {
                Text = confText,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(confColor)
            };
            Grid.SetColumn(confBadge, 1);
            grid.Children.Add(confBadge);

            // Source badge
            var srcNorm = (item.Source ?? "").ToLowerInvariant();
            var (srcColor, srcLabel) = srcNorm switch
            {
                var s when s.Contains("learn") => (Color.FromArgb(255, 180, 130, 255), "LRN"),
                var s when s.Contains("database") || s.Contains("history") => (Color.FromArgb(255, 100, 180, 255), "DB"),
                var s when s.Contains("knowledge") => (Color.FromArgb(255, 100, 220, 180), "KB"),
                _ => (Color.FromArgb(255, 150, 155, 160), "AI")
            };
            var srcBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, srcColor.R, srcColor.G, srcColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            srcBadge.Child = new TextBlock
            {
                Text = srcLabel,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(srcColor)
            };
            Grid.SetColumn(srcBadge, 2);
            grid.Children.Add(srcBadge);

            // Hours
            if (item.Hours > 0)
            {
                var hours = new TextBlock
                {
                    Text = $"{item.Hours:F1}h",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                Grid.SetColumn(hours, 3);
                grid.Children.Add(hours);
            }

            border.Child = grid;
            return border;
        }

        private void BuildSummaryCards(GuidanceEstimateResult result, List<GuidanceOperation> filteredOps)
        {
            var cardsGrid = new Grid();
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Card 1: Total Operations
            var totalCard = CreateSummaryCard("Total Ops",
                filteredOps.Count.ToString(),
                Color.FromArgb(255, 100, 200, 255));
            Grid.SetColumn(totalCard, 0);
            cardsGrid.Children.Add(totalCard);

            // Card 2: Body Hours
            var bodyHours = filteredOps.Sum(o => o.LaborHours);
            var bodyCard = CreateSummaryCard("Body Hours",
                $"{bodyHours:F1}h",
                Color.FromArgb(255, 255, 180, 100));
            Grid.SetColumn(bodyCard, 1);
            cardsGrid.Children.Add(bodyCard);

            // Card 3: Refinish Hours
            var refinishHours = filteredOps.Sum(o => o.RefinishHours);
            var refinishCard = CreateSummaryCard("Refinish Hours",
                $"{refinishHours:F1}h",
                Color.FromArgb(255, 180, 130, 255));
            Grid.SetColumn(refinishCard, 2);
            cardsGrid.Children.Add(refinishCard);

            // Card 4: Labor Total (rate-based) — also show learned dollar total if available
            var laborTotal = result.GrandTotalLaborDollars;
            var laborLabel = "Rate-Based";
            var laborValue = $"${laborTotal:F0}";
            var laborCard = CreateSummaryCard(laborLabel, laborValue,
                Color.FromArgb(255, 100, 220, 100));
            Grid.SetColumn(laborCard, 3);
            cardsGrid.Children.Add(laborCard);

            // Card 5: Learned Dollar Total (from uploaded estimates) if >50% of ops have learned amounts
            var opsWithDollars = filteredOps.Count(o => o.LearnedDollarAmount.HasValue && o.LearnedDollarAmount > 0);
            if (opsWithDollars > 0 && result.LearnedDollarTotal.HasValue)
            {
                var learnedText = $"${result.LearnedDollarTotal:F0}";
                if (result.MinDollarTotal.HasValue && result.MaxDollarTotal.HasValue && result.MinDollarTotal != result.MaxDollarTotal)
                    learnedText += $"\n${result.MinDollarTotal:F0}–${result.MaxDollarTotal:F0}";
                var learnedCard = CreateSummaryCard("From Estimates", learnedText,
                    Color.FromArgb(255, 100, 220, 180));
                Grid.SetColumn(learnedCard, 4);
                cardsGrid.Children.Add(learnedCard);
            }

            // Card 6: Data Quality — % of operations with learned data
            var totalOps = filteredOps.Count;
            var learnedOps = filteredOps.Count(o => o.LaborSource != "fallback" && o.LaborSource != "met_data" && !string.IsNullOrEmpty(o.LaborSource));
            var qualityPercent = totalOps > 0 ? (int)(100.0 * learnedOps / totalOps) : 0;
            var qualityColor = qualityPercent > 70
                ? Color.FromArgb(255, 80, 190, 80)   // Green
                : qualityPercent > 30
                    ? Color.FromArgb(255, 220, 180, 60)   // Yellow
                    : Color.FromArgb(255, 220, 130, 60);  // Orange
            var qualityCard = CreateSummaryCard("Data Quality",
                $"{qualityPercent}% learned",
                qualityColor);
            Grid.SetColumn(qualityCard, 5);
            cardsGrid.Children.Add(qualityCard);

            _summaryCardsPanel?.Children.Add(cardsGrid);
        }

        private Border CreateSummaryCard(string label, string value, Color color)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 38, 48)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(4)
            };

            var stack = new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            border.Child = stack;
            return border;
        }

        private void BuildWarningsAndTips(GuidanceEstimateResult result)
        {
            if (!result.Warnings.Any() && !result.ProTips.Any()) return;

            var infoPanel = new StackPanel { Spacing = 4 };

            foreach (var warning in result.Warnings)
            {
                var warningBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 60, 50, 30)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                warningBorder.Child = new TextBlock
                {
                    Text = $"Warning: {warning}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                    TextWrapping = TextWrapping.Wrap
                };
                infoPanel.Children.Add(warningBorder);
            }

            foreach (var tip in result.ProTips)
            {
                var tipBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 50, 50)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                tipBorder.Child = new TextBlock
                {
                    Text = $"Tip: {tip}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 180)),
                    TextWrapping = TextWrapping.Wrap
                };
                infoPanel.Children.Add(tipBorder);
            }

            _resultsPanel?.Children.Add(infoPanel);
        }

        private Border CreateCategorySection(string category, List<GuidanceOperation> operations)
        {
            var catColor = SectionColors.GetValueOrDefault(category, Color.FromArgb(255, 150, 155, 160));
            var isCollapsed = _collapsedCategories.GetValueOrDefault(category, false);

            var sectionBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 42)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                BorderThickness = new Thickness(1)
            };

            var sectionStack = new StackPanel();

            // Category header (clickable to collapse)
            var headerButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8, 12, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 38, 43, 52)),
                CornerRadius = new CornerRadius(8, 8, isCollapsed ? 8 : 0, isCollapsed ? 8 : 0),
                BorderThickness = new Thickness(0)
            };

            var headerContent = new Grid();
            headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chevron = new FontIcon
            {
                Glyph = isCollapsed ? "\uE76C" : "\uE76D",
                FontSize = 12,
                Foreground = new SolidColorBrush(catColor),
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(chevron, 0);
            headerContent.Children.Add(chevron);

            var catTitle = new TextBlock
            {
                Text = $"{category.ToUpper()} ({operations.Count})",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(catColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(catTitle, 1);
            headerContent.Children.Add(catTitle);

            var catHours = operations.Sum(o => o.LaborHours + o.RefinishHours);
            var hoursText = new TextBlock
            {
                Text = catHours > 0 ? $"{catHours:F1}h" : "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hoursText, 2);
            headerContent.Children.Add(hoursText);

            headerButton.Content = headerContent;

            // Toggle collapse
            var opsPanel = new StackPanel { Spacing = 2, Padding = new Thickness(8, 4, 8, 8) };
            opsPanel.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;

            headerButton.Click += (s, e) =>
            {
                var collapsed = opsPanel.Visibility == Visibility.Visible;
                opsPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                chevron.Glyph = collapsed ? "\uE76C" : "\uE76D";
                _collapsedCategories[category] = collapsed;
            };

            sectionStack.Children.Add(headerButton);

            // Operation rows
            foreach (var op in operations)
            {
                var opRow = CreateGuidanceOperationRow(op);
                opsPanel.Children.Add(opRow);
            }

            sectionStack.Children.Add(opsPanel);
            sectionBorder.Child = sectionStack;
            return sectionBorder;
        }

        private Border CreateGuidanceOperationRow(GuidanceOperation op)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 42, 47, 56)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 1, 0, 1)
            };

            var mainStack = new StackPanel { Spacing = 2 };

            // Top row: checkbox + description + hours + badges
            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Checkbox
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Hours
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Confidence badge
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Source tag

            // Checkbox
            var checkbox = new CheckBox
            {
                MinWidth = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _operationCheckboxes[op] = checkbox;
            Grid.SetColumn(checkbox, 0);
            topRow.Children.Add(checkbox);

            // Description
            var descText = new TextBlock
            {
                Text = op.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(descText, 1);
            topRow.Children.Add(descText);

            // Hours — with range display when learned data has multiple samples
            var hoursInfo = "";
            if (op.LaborHours > 0)
            {
                hoursInfo += $"{op.LaborHours:F1}h";
                if (op.SampleCount >= 2 && op.MinLaborHours != op.MaxLaborHours)
                    hoursInfo += $" ({op.MinLaborHours:F1}–{op.MaxLaborHours:F1})";
                else if (op.SampleCount == 1 && op.LaborSource != "fallback" && op.LaborSource != "met_data")
                    hoursInfo += " (1 est)";
            }
            if (op.RefinishHours > 0)
            {
                var rfnPart = $"{op.RefinishHours:F1}h rfn";
                if (op.SampleCount >= 2 && op.MinRefinishHours != op.MaxRefinishHours && op.MinRefinishHours > 0)
                    rfnPart += $" ({op.MinRefinishHours:F1}–{op.MaxRefinishHours:F1})";
                hoursInfo += (hoursInfo.Length > 0 ? " | " : "") + rfnPart;
            }
            if (op.Price > 0)
            {
                var pricePart = $"${op.Price:F2}";
                if (op.SampleCount >= 2 && op.MinDollarAmount.HasValue && op.MaxDollarAmount.HasValue && op.MinDollarAmount != op.MaxDollarAmount)
                    pricePart += $" (${op.MinDollarAmount:F0}–${op.MaxDollarAmount:F0})";
                hoursInfo += (hoursInfo.Length > 0 ? " | " : "") + pricePart;
            }
            if (op.LearnedDollarAmount.HasValue && op.LearnedDollarAmount > 0 && op.Price == 0)
            {
                var dollarPart = $"~${op.LearnedDollarAmount:F0}";
                if (op.SampleCount >= 2 && op.MinDollarAmount.HasValue && op.MaxDollarAmount.HasValue && op.MinDollarAmount != op.MaxDollarAmount)
                    dollarPart += $" (${op.MinDollarAmount:F0}–${op.MaxDollarAmount:F0})";
                hoursInfo += (hoursInfo.Length > 0 ? " | " : "") + dollarPart;
            }

            if (!string.IsNullOrEmpty(hoursInfo))
            {
                var hoursText = new TextBlock
                {
                    Text = hoursInfo,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 165, 170)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0)
                };
                Grid.SetColumn(hoursText, 2);
                topRow.Children.Add(hoursText);
            }

            // Confidence badge
            var (confColor, confText) = op.ConfidenceLabel switch
            {
                "High" => (Color.FromArgb(255, 80, 190, 80), "HIGH"),
                "Medium" => (Color.FromArgb(255, 220, 180, 60), "MED"),
                "Low" => (Color.FromArgb(255, 220, 130, 60), "LOW"),
                _ => (Color.FromArgb(255, 150, 155, 160), "?")
            };

            var confBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, confColor.R, confColor.G, confColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            confBadge.Child = new TextBlock
            {
                Text = confText,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(confColor)
            };
            Grid.SetColumn(confBadge, 3);
            topRow.Children.Add(confBadge);

            // Source tag
            var (srcColor, srcLabel) = op.DataSource switch
            {
                "Database" => (Color.FromArgb(255, 100, 180, 255), "DB"),
                "Knowledge Base" => (Color.FromArgb(255, 180, 130, 255), "KB"),
                "Learned" => (Color.FromArgb(255, 100, 220, 180), "LRN"),
                "Excel Tool" => (Color.FromArgb(255, 255, 160, 60), "XLS"),
                _ => (Color.FromArgb(255, 150, 155, 160), "?")
            };

            var srcBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, srcColor.R, srcColor.G, srcColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            srcBadge.Child = new TextBlock
            {
                Text = srcLabel,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(srcColor)
            };
            ToolTipService.SetToolTip(srcBadge, op.DataSource);
            Grid.SetColumn(srcBadge, 4);
            topRow.Children.Add(srcBadge);

            mainStack.Children.Add(topRow);

            // Justification row (expandable) - show if justification or references exist
            var hasJustification = !string.IsNullOrEmpty(op.Justification) ||
                                   !string.IsNullOrEmpty(op.PPageReference) ||
                                   !string.IsNullOrEmpty(op.DEGReference) ||
                                   !string.IsNullOrEmpty(op.Source);

            if (hasJustification)
            {
                var justPanel = new StackPanel
                {
                    Spacing = 1,
                    Margin = new Thickness(28, 2, 0, 0),
                    Visibility = Visibility.Collapsed
                };

                if (!string.IsNullOrEmpty(op.Source))
                {
                    justPanel.Children.Add(new TextBlock
                    {
                        Text = $"Source: {op.Source}",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                if (!string.IsNullOrEmpty(op.Justification) && op.Justification != op.Source)
                {
                    justPanel.Children.Add(new TextBlock
                    {
                        Text = $"Why: {op.Justification}",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 160, 180)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                if (!string.IsNullOrEmpty(op.PPageReference))
                {
                    justPanel.Children.Add(new TextBlock
                    {
                        Text = $"P-Page: {op.PPageReference}",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 160, 130)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                if (!string.IsNullOrEmpty(op.DEGReference))
                {
                    justPanel.Children.Add(new TextBlock
                    {
                        Text = $"DEG: {op.DEGReference}",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 160, 130)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }


                if (op.LearnedFrequency > 0)
                {
                    justPanel.Children.Add(new TextBlock
                    {
                        Text = $"Seen in {op.LearnedFrequency} uploaded estimate(s)",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 160)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                // Show sample count for learned data
                if (op.SampleCount > 0 && op.LaborSource != "fallback" && op.LaborSource != "met_data")
                {
                    justPanel.Children.Add(new TextBlock
                    {
                        Text = $"Based on {op.SampleCount} uploaded estimate(s)",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 160)),
                        TextWrapping = TextWrapping.Wrap
                    });

                    // Show range details
                    if (!string.IsNullOrEmpty(op.RangeDisplayText))
                    {
                        justPanel.Children.Add(new TextBlock
                        {
                            Text = op.RangeDisplayText,
                            FontSize = 10,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 180, 200)),
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                }

                mainStack.Children.Add(justPanel);

                // Make the row clickable to expand/collapse justification
                border.PointerPressed += (s, e) =>
                {
                    justPanel.Visibility = justPanel.Visibility == Visibility.Visible
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                };
                ToolTipService.SetToolTip(border, "Click to show/hide details");
            }

            border.Child = mainStack;
            return border;
        }

        #endregion

        #region Comparison Display

        /// <summary>
        /// Display comparison results with color-coded diff borders and action buttons.
        /// Green=match, Yellow/Amber=ghost-only (missing), Blue=user-extra, Orange=value-differs.
        /// </summary>
        public void DisplayComparisonResults(GhostComparisonResult comparison)
        {
            _resultsPanel?.Children.Clear();
            _summaryCardsPanel?.Children.Clear();

            ShowToolbarAndResults();

            // Summary stats bar
            BuildComparisonStatsBar(comparison);

            // Coverage gaps warning
            if (comparison.CoverageGaps.Count > 0)
            {
                var gapBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 60, 50, 30)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                gapBorder.Child = new TextBlock
                {
                    Text = $"Coverage gaps: Your estimate is missing entire categories: {string.Join(", ", comparison.CoverageGaps)}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                    TextWrapping = TextWrapping.Wrap
                };
                _resultsPanel?.Children.Add(gapBorder);
            }

            // Matching operations (green)
            if (comparison.BothHave.Count > 0)
            {
                _resultsPanel?.Children.Add(CreateComparisonSection(
                    "Matching Operations", comparison.BothHave, ComparisonDiffType.Match,
                    comparison.ValueDifferences));
            }

            // Ghost found missing (yellow/amber) - with Accept/Dismiss buttons
            if (comparison.GhostFoundMissing.Count > 0)
            {
                var missingOps = comparison.GhostFoundMissing.Select(m => m.Operation).ToList();
                _resultsPanel?.Children.Add(CreateComparisonSection(
                    "Ghost Suggests Adding", missingOps, ComparisonDiffType.GhostOnly, null));
            }

            // User extras (blue) - with "Teach Ghost" button
            if (comparison.UserFoundExtra.Count > 0)
            {
                var extraOps = comparison.UserFoundExtra.Select(e => e.Operation).ToList();
                _resultsPanel?.Children.Add(CreateComparisonSection(
                    "Your Additions (Ghost Didn't Include)", extraOps, ComparisonDiffType.UserOnly, null));
            }

            // Value differences (orange)
            if (comparison.ValueDifferences.Count > 0)
            {
                _resultsPanel?.Children.Add(CreateValueDifferencesSection(comparison.ValueDifferences));
            }
        }

        private void BuildComparisonStatsBar(GhostComparisonResult comparison)
        {
            var statsGrid = new Grid();
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var matchCard = CreateSummaryCard("Match",
                $"{comparison.MatchPercentage:F0}%",
                Color.FromArgb(255, 80, 200, 80));
            Grid.SetColumn(matchCard, 0);
            statsGrid.Children.Add(matchCard);

            var missingCard = CreateSummaryCard("Ghost Missing",
                comparison.GhostFoundMissing.Count.ToString(),
                Color.FromArgb(255, 255, 200, 80));
            Grid.SetColumn(missingCard, 1);
            statsGrid.Children.Add(missingCard);

            var extraCard = CreateSummaryCard("Your Extras",
                comparison.UserFoundExtra.Count.ToString(),
                Color.FromArgb(255, 100, 180, 255));
            Grid.SetColumn(extraCard, 2);
            statsGrid.Children.Add(extraCard);

            var diffLabel = comparison.LaborDollarDifference >= 0 ? $"+${comparison.LaborDollarDifference:F0}" : $"-${Math.Abs(comparison.LaborDollarDifference):F0}";
            var diffColor = comparison.LaborDollarDifference >= 0
                ? Color.FromArgb(255, 255, 200, 80)
                : Color.FromArgb(255, 100, 200, 255);
            var dollarCard = CreateSummaryCard("Labor $ Diff",
                diffLabel,
                diffColor);
            Grid.SetColumn(dollarCard, 3);
            statsGrid.Children.Add(dollarCard);

            _summaryCardsPanel?.Children.Add(statsGrid);
        }

        private Border CreateComparisonSection(string title, List<GhostOperation> operations,
            ComparisonDiffType diffType, List<ValueDifference>? valueDiffs)
        {
            var (accentColor, headerBg) = diffType switch
            {
                ComparisonDiffType.Match => (Color.FromArgb(255, 80, 200, 80), Color.FromArgb(255, 30, 50, 35)),
                ComparisonDiffType.GhostOnly => (Color.FromArgb(255, 255, 200, 80), Color.FromArgb(255, 50, 45, 25)),
                ComparisonDiffType.UserOnly => (Color.FromArgb(255, 100, 180, 255), Color.FromArgb(255, 30, 40, 55)),
                _ => (Color.FromArgb(255, 150, 155, 160), Color.FromArgb(255, 35, 40, 48))
            };

            var sectionBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 42)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var stack = new StackPanel();

            // Header
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(headerBg),
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            headerRow.Children.Add(new TextBlock
            {
                Text = $"{title} ({operations.Count})",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accentColor)
            });
            headerBorder.Child = headerRow;
            stack.Children.Add(headerBorder);

            // Operations
            var opsPanel = new StackPanel { Spacing = 2, Padding = new Thickness(4) };
            foreach (var op in operations)
            {
                var valueDiff = valueDiffs?.FirstOrDefault(v =>
                    v.GhostOp.PartName == op.PartName && v.GhostOp.OperationType == op.OperationType);
                var rowType = valueDiff != null ? ComparisonDiffType.ValueDiffers : diffType;
                opsPanel.Children.Add(CreateComparisonOperationRow(op, rowType, diffType, valueDiff));
            }
            stack.Children.Add(opsPanel);

            sectionBorder.Child = stack;
            return sectionBorder;
        }

        private Border CreateComparisonOperationRow(GhostOperation op, ComparisonDiffType rowType,
            ComparisonDiffType sectionType, ValueDifference? valueDiff)
        {
            var accentColor = rowType switch
            {
                ComparisonDiffType.Match => Color.FromArgb(255, 80, 200, 80),
                ComparisonDiffType.GhostOnly => Color.FromArgb(255, 255, 200, 80),
                ComparisonDiffType.UserOnly => Color.FromArgb(255, 100, 180, 255),
                ComparisonDiffType.ValueDiffers => Color.FromArgb(255, 255, 160, 60),
                _ => Color.FromArgb(255, 150, 155, 160)
            };

            var rowBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 42, 47, 56)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 1, 0, 1),
                BorderBrush = new SolidColorBrush(accentColor),
                BorderThickness = new Thickness(4, 0, 0, 0) // 4px left accent border
            };

            var mainStack = new StackPanel { Spacing = 2 };

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Hours
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Action button

            // Description
            var descText = new TextBlock
            {
                Text = op.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(descText, 0);
            topRow.Children.Add(descText);

            // Hours info
            var hoursInfo = "";
            if (op.LaborHours > 0) hoursInfo += $"{op.LaborHours:F1}h";
            if (op.RefinishHours > 0) hoursInfo += (hoursInfo.Length > 0 ? " | " : "") + $"{op.RefinishHours:F1}h rfn";
            if (op.Price > 0) hoursInfo += (hoursInfo.Length > 0 ? " | " : "") + $"${op.Price:F2}";

            // Show diff info for value differences
            if (valueDiff != null)
            {
                var diffParts = new List<string>();
                if (Math.Abs(valueDiff.LaborHoursDiff) > 0.1m)
                    diffParts.Add($"labor {(valueDiff.LaborHoursDiff > 0 ? "+" : "")}{valueDiff.LaborHoursDiff:F1}h");
                if (Math.Abs(valueDiff.RefinishHoursDiff) > 0.1m)
                    diffParts.Add($"rfn {(valueDiff.RefinishHoursDiff > 0 ? "+" : "")}{valueDiff.RefinishHoursDiff:F1}h");
                if (diffParts.Count > 0)
                    hoursInfo += $" ({string.Join(", ", diffParts)})";
            }

            if (!string.IsNullOrEmpty(hoursInfo))
            {
                var hoursText = new TextBlock
                {
                    Text = hoursInfo,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 165, 170)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0)
                };
                Grid.SetColumn(hoursText, 1);
                topRow.Children.Add(hoursText);
            }

            // Action buttons based on section type
            if (sectionType == ComparisonDiffType.GhostOnly)
            {
                var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

                var acceptBtn = new Button
                {
                    Content = "Accept",
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 10,
                    Background = new SolidColorBrush(Color.FromArgb(255, 40, 80, 50)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 120)),
                    CornerRadius = new CornerRadius(3)
                };
                var capturedOp = op;
                acceptBtn.Click += (s, e) =>
                {
                    OnOperationAccepted?.Invoke(this, capturedOp);
                    rowBorder.Opacity = 0.4;
                    acceptBtn.IsEnabled = false;
                };
                actionPanel.Children.Add(acceptBtn);

                var dismissBtn = new Button
                {
                    Content = "Dismiss",
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 10,
                    Background = new SolidColorBrush(Color.FromArgb(255, 60, 40, 40)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 130, 130)),
                    CornerRadius = new CornerRadius(3)
                };
                dismissBtn.Click += (s, e) =>
                {
                    rowBorder.Visibility = Visibility.Collapsed;
                };
                actionPanel.Children.Add(dismissBtn);

                Grid.SetColumn(actionPanel, 2);
                topRow.Children.Add(actionPanel);
            }
            else if (sectionType == ComparisonDiffType.UserOnly)
            {
                var teachBtn = new Button
                {
                    Content = "Teach Ghost",
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 10,
                    Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 70)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                    CornerRadius = new CornerRadius(3)
                };
                teachBtn.Click += (s, e) =>
                {
                    teachBtn.Content = "Learned!";
                    teachBtn.IsEnabled = false;
                    System.Diagnostics.Debug.WriteLine($"[Ghost] User teaching ghost about: {op.Description}");
                };
                Grid.SetColumn(teachBtn, 2);
                topRow.Children.Add(teachBtn);
            }

            mainStack.Children.Add(topRow);

            // Source info
            if (!string.IsNullOrEmpty(op.Source))
            {
                mainStack.Children.Add(new TextBlock
                {
                    Text = op.Source,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 110, 115, 120)),
                    Margin = new Thickness(4, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            rowBorder.Child = mainStack;
            return rowBorder;
        }

        private Border CreateValueDifferencesSection(List<ValueDifference> diffs)
        {
            var accentColor = Color.FromArgb(255, 255, 160, 60);

            var sectionBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 42)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var stack = new StackPanel();

            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 40, 25)),
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            headerBorder.Child = new TextBlock
            {
                Text = $"Value Differences ({diffs.Count})",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accentColor)
            };
            stack.Children.Add(headerBorder);

            var opsPanel = new StackPanel { Spacing = 2, Padding = new Thickness(4) };
            foreach (var diff in diffs)
            {
                var rowBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 42, 47, 56)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 1, 0, 1),
                    BorderBrush = new SolidColorBrush(accentColor),
                    BorderThickness = new Thickness(4, 0, 0, 0)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                grid.Children.Add(new TextBlock
                {
                    Text = diff.GhostOp.Description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var ghostLabel = new TextBlock
                {
                    Text = $"Ghost: {diff.GhostOp.LaborHours:F1}h",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 80)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(ghostLabel, 1);
                grid.Children.Add(ghostLabel);

                var userLabel = new TextBlock
                {
                    Text = $"You: {diff.UserOp.LaborHours:F1}h",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(userLabel, 2);
                grid.Children.Add(userLabel);

                rowBorder.Child = grid;
                opsPanel.Children.Add(rowBorder);
            }
            stack.Children.Add(opsPanel);

            sectionBorder.Child = stack;
            return sectionBorder;
        }

        private enum ComparisonDiffType
        {
            Match,
            GhostOnly,
            UserOnly,
            ValueDiffers
        }

        #endregion

        #region Filtering

        private List<GuidanceOperation> GetFilteredOperations(List<GuidanceOperation> allOps)
        {
            var ops = allOps.AsEnumerable();

            // Section filter
            var catIndex = _categoryFilter?.SelectedIndex ?? 0;
            if (catIndex > 0)
            {
                var bodyPanelSections = new HashSet<string>
                {
                    "FRONT BUMPER & GRILLE", "REAR BUMPER", "FRONT LAMPS", "REAR LAMPS",
                    "RADIATOR SUPPORT", "HOOD", "FENDER", "FRONT DOOR", "REAR DOOR",
                    "QUARTER PANEL", "ROOF", "TRUNK / DECKLID", "GLASS", "INSTRUMENT PANEL"
                };

                var section = !string.IsNullOrEmpty(ops.FirstOrDefault()?.Section) ? "Section" : "Category";

                ops = catIndex switch
                {
                    1 => ops.Where(o => bodyPanelSections.Contains(o.Section)),                          // Body Panels
                    2 => ops.Where(o => o.Section == "PILLARS, ROCKER & FLOOR" || o.Section == "FRAME" || o.Section == "RESTRAINT SYSTEMS"), // Structural / Frame
                    3 => ops.Where(o => o.Section == "VEHICLE DIAGNOSTICS"),                             // Diagnostics
                    4 => ops.Where(o => o.Section == "ELECTRICAL"),                                      // Electrical
                    5 => ops.Where(o => o.Section == "MECHANICAL"),                                      // Mechanical
                    6 => ops.Where(o => o.Section == "MISCELLANEOUS OPERATIONS"),                        // Misc
                    _ => ops
                };
            }

            // Confidence filter
            var confIndex = _confidenceFilter?.SelectedIndex ?? 0;
            if (confIndex > 0)
            {
                ops = confIndex switch
                {
                    1 => ops.Where(o => o.ConfidenceLabel == "High"),
                    2 => ops.Where(o => o.ConfidenceLabel == "High" || o.ConfidenceLabel == "Medium"),
                    _ => ops
                };
            }

            return ops.ToList();
        }

        #endregion

        #region Copy/Export

        private void CopyOperationsToClipboard(List<GuidanceOperation> operations, bool includeDetails = false)
        {
            var sb = new StringBuilder();

            var grouped = operations
                .GroupBy(o => !string.IsNullOrEmpty(o.Section) ? o.Section : o.Category)
                .OrderBy(g => Array.IndexOf(SectionOrder, g.Key) is int idx && idx >= 0 ? idx : 99);

            foreach (var group in grouped)
            {
                sb.AppendLine($"=== {group.Key.ToUpper()} ===");

                foreach (var op in group)
                {
                    var hours = "";
                    if (op.LaborHours > 0) hours += $"{op.LaborHours:F1}h";
                    if (op.RefinishHours > 0) hours += (hours.Length > 0 ? "\t" : "") + $"{op.RefinishHours:F1}h rfn";
                    if (op.Price > 0) hours += (hours.Length > 0 ? "\t" : "") + $"${op.Price:F2}";

                    sb.AppendLine($"{op.Description}\t{hours}");

                    if (includeDetails)
                    {
                        if (!string.IsNullOrEmpty(op.Source))
                            sb.AppendLine($"  Source: {op.Source}");
                        if (!string.IsNullOrEmpty(op.Justification) && op.Justification != op.Source)
                            sb.AppendLine($"  Why: {op.Justification}");
                        if (!string.IsNullOrEmpty(op.PPageReference))
                            sb.AppendLine($"  P-Page: {op.PPageReference}");
                        if (!string.IsNullOrEmpty(op.DEGReference))
                            sb.AppendLine($"  DEG: {op.DEGReference}");
                    }
                }

                sb.AppendLine();
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(sb.ToString());
            Clipboard.SetContent(dataPackage);
        }

        #endregion

        private void ShowMessage(string message)
        {
            if (_statusText != null)
                _statusText.Text = message;
            System.Diagnostics.Debug.WriteLine($"[Guidance] {message}");
        }
    }
}
