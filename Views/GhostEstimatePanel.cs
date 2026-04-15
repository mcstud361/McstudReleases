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
        private ComboBox? _vehicleCombo;
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

            // Must-Haves button
            var mustHavesButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE73E", FontSize = 14 },
                        new TextBlock { Text = "Must-Haves", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 90)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            ToolTipService.SetToolTip(mustHavesButton,
                "Configure which operations must appear on every estimate.\n" +
                "These are automatically included in generated guidance.");
            mustHavesButton.Click += async (s, e) =>
            {
                var saved = await MustHavesDialog.ShowAsync(this.XamlRoot);
                if (saved)
                {
                    var enabledCount = GhostConfigService.Instance.GetMustHaves().Count(m => m.Enabled);
                    System.Diagnostics.Debug.WriteLine($"[Ghost] Must-haves updated: {enabledCount} enabled");
                }
            };
            titleRow.Children.Add(mustHavesButton);

            // Settings gear button
            var settingsButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE713", FontSize = 18 },
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
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

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new FontIcon
            {
                Glyph = "\uE82D",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 160, 220))
            });

            var linkText = new TextBlock
            {
                Text = "P-Pages, DEG inquiries, and supporting documents for detected operations are available on the Reference tab.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 200, 220)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(linkText);

            var goBtn = new Button
            {
                Content = "Open Reference Tab",
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 70, 90)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 190, 240)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 100, 140)),
                BorderThickness = new Thickness(1),
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
            _vehicleCombo = new ComboBox
            {
                PlaceholderText = "Select vehicle type",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 65, 70, 80))
            };
            foreach (var style in PPFPricingService.Instance.GetVehicleStyles())
                _vehicleCombo.Items.Add(new ComboBoxItem { Content = style.Name, Tag = style });
            _vehicleCombo.SelectionChanged += OnVehicleComboChanged;
            vehicleStack.Children.Add(_vehicleCombo);
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

            var pdfExportButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8A5", FontSize = 12 },
                        new TextBlock { Text = "Export PDF", FontSize = 12 }
                    }
                },
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 120, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            ToolTipService.SetToolTip(pdfExportButton, "Export to PDF document");
            pdfExportButton.Click += PdfExportButton_Click;
            actionsPanel.Children.Add(pdfExportButton);

            var pdfSettingsButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE713", FontSize = 12 },
                Padding = new Thickness(6, 6, 6, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                CornerRadius = new CornerRadius(4)
            };
            ToolTipService.SetToolTip(pdfSettingsButton, "PDF Export Settings");
            pdfSettingsButton.Click += OpenGhostPdfSettings_Click;
            actionsPanel.Children.Add(pdfSettingsButton);

            var clearButton = new Button
            {
                Content = "Clear",
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 40, 40)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 140, 140)),
                CornerRadius = new CornerRadius(4)
            };
            clearButton.Click += ClearButton_Click;
            actionsPanel.Children.Add(clearButton);

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

        private void OnVehicleComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vehicleCombo?.SelectedItem is not ComboBoxItem item || item.Tag is not VehicleStyle style) return;
            _vehicleDiagram.SetVehicleType(style.DiagramType);
        }

        private string _lastAutoPanelText = "";

        private void VehicleDiagram_SelectionChanged(object? sender, PanelSelectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[Guidance] Panels selected: {string.Join(", ", e.SelectedPanelIds)}");

            if (_damageDescriptionBox == null) return;

            // Build new auto-text from selected panel display names
            var panelNames = e.SelectedPanelIds.Select(GetPanelDisplayName).ToList();
            var newAutoText = panelNames.Count > 0
                ? $"Damaged: {string.Join(", ", panelNames)}. "
                : "";

            var current = _damageDescriptionBox.Text ?? "";

            // If we previously injected auto-text, remove it (preserve any user-typed text)
            if (!string.IsNullOrEmpty(_lastAutoPanelText) && current.StartsWith(_lastAutoPanelText))
            {
                current = current.Substring(_lastAutoPanelText.Length);
            }

            _damageDescriptionBox.Text = newAutoText + current;
            _lastAutoPanelText = newAutoText;
        }

        private static string GetPanelDisplayName(string panelId)
        {
            // Spell out sides so the ghost parser can detect Left/Right/Front/Rear
            return panelId switch
            {
                "hood" => "Hood",
                "front_bumper" => "Front Bumper",
                "rear_bumper" => "Rear Bumper",
                "lf_fender" => "Left Front Fender",
                "rf_fender" => "Right Front Fender",
                "lf_door" => "Left Front Door",
                "rf_door" => "Right Front Door",
                "lr_door" => "Left Rear Door",
                "rr_door" => "Right Rear Door",
                "l_door" => "Left Door",
                "r_door" => "Right Door",
                "lr_quarter" => "Left Rear Quarter Panel",
                "rr_quarter" => "Right Rear Quarter Panel",
                "l_bedside" => "Left Bedside",
                "r_bedside" => "Right Bedside",
                "decklid" => "Decklid",
                "liftgate" => "Liftgate",
                "tailgate" => "Tailgate",
                "roof" => "Roof",
                "l_rocker" => "Left Rocker Panel",
                "r_rocker" => "Right Rocker Panel",
                "lf_mirror" => "Left Front Mirror",
                "rf_mirror" => "Right Front Mirror",
                "lf_headlight" => "Left Front Headlight",
                "rf_headlight" => "Right Front Headlight",
                "lr_taillight" => "Left Rear Tail Light",
                "rr_taillight" => "Right Rear Tail Light",
                "sliding_door" => "Sliding Door",
                "r_side" => "Right Side",
                _ => panelId.Replace("_", " ")
            };
        }

        private async void GenerateGhostButton_Click(object sender, RoutedEventArgs e)
        {
            var vehicleInfo = (_vehicleCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var damageDesc = _damageDescriptionBox?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(damageDesc))
            {
                ShowMessage("Please enter a damage description");
                return;
            }

            // Disable button and show loading state during generation
            if (_generateGhostButton != null)
            {
                _generateGhostButton.IsEnabled = false;
                _generateGhostButton.Content = "Generating...";
            }

            try
            {
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

                // Generate guidance estimate (async with AI when available, sync fallback)
                _currentResult = await _ghostService.GenerateGuidanceEstimateAsync(input);

                // Show toolbar and results
                ShowToolbarAndResults();

                // Display
                DisplayGuidanceResults(_currentResult);
            }
            finally
            {
                // Restore button state
                if (_generateGhostButton != null)
                {
                    _generateGhostButton.IsEnabled = true;
                    _generateGhostButton.Content = "Generate Estimate";
                }
            }
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

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _currentResult = null;
            _operationCheckboxes.Clear();
            _resultsPanel?.Children.Clear();
            _summaryCardsPanel?.Children.Clear();

            // Clear input fields
            if (_vehicleCombo != null) _vehicleCombo.SelectedIndex = -1;
            if (_damageDescriptionBox != null) _damageDescriptionBox.Text = "";
            _lastAutoPanelText = "";

            // Hide toolbar and results
            var mainBorder = Content as Border;
            var scrollViewer = mainBorder?.Child as ScrollViewer;
            var mainStack = scrollViewer?.Content as StackPanel;
            if (mainStack != null)
            {
                foreach (var child in mainStack.Children)
                {
                    if (child is Border b && (b.Name == "ToolbarBorder" || b.Name == "ResultsBorder"))
                        b.Visibility = Visibility.Collapsed;
                }
            }

            _vehicleDiagram.ClearSelections();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResult == null) return;
            var ops = GetFilteredOperations(_currentResult.GuidanceOperations);
            CopyOperationsToClipboard(ops, includeDetails: true);
            ShowMessage($"Exported {ops.Count} operations with details to clipboard");
        }

        private void PdfExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResult == null)
            {
                ShowMessage("Generate an estimate first before exporting to PDF.");
                return;
            }

            try
            {
                // Honor user selection — checked ops only — and recompute totals to match
                var activeOps = GetActiveOperations();
                RecomputeTotalsFromOps(_currentResult, activeOps);
                var pdfService = new GhostPdfExportService();
                var path = pdfService.GenerateGhostPdf(_currentResult, activeOps);
                ShowMessage($"PDF exported: {path}");
            }
            catch (Exception ex)
            {
                ShowMessage($"PDF export failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[GhostPDF] Export error: {ex}");
            }
        }

        private async void OpenGhostPdfSettings_Click(object sender, RoutedEventArgs e)
        {
            var configService = GhostExportConfigService.Instance;
            var config = configService.Config;

            var settingsPanel = new StackPanel { Spacing = 12, MinWidth = 400 };

            // Header/Footer settings
            settingsPanel.Children.Add(new TextBlock
            {
                Text = "Header & Footer",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });

            var titleBox = new TextBox
            {
                Header = "Header Title",
                Text = config.HeaderTitle,
                FontSize = 13
            };
            settingsPanel.Children.Add(titleBox);

            var subtitleBox = new TextBox
            {
                Header = "Header Subtitle",
                Text = config.HeaderSubtitle,
                FontSize = 13
            };
            settingsPanel.Children.Add(subtitleBox);

            var footerBox = new TextBox
            {
                Header = "Footer Text",
                Text = config.FooterText,
                FontSize = 13
            };
            settingsPanel.Children.Add(footerBox);

            var dateFormatBox = new TextBox
            {
                Header = "Date Format",
                Text = config.DateFormat,
                FontSize = 13
            };
            settingsPanel.Children.Add(dateFormatBox);

            // Toggles
            settingsPanel.Children.Add(new TextBlock
            {
                Text = "Display Options",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
                Margin = new Thickness(0, 8, 0, 0)
            });

            var showDateToggle = new ToggleSwitch { Header = "Show Date", IsOn = config.ShowDate };
            var showPageNumToggle = new ToggleSwitch { Header = "Show Page Numbers", IsOn = config.ShowPageNumbers };
            var showVehicleToggle = new ToggleSwitch { Header = "Show Vehicle Info", IsOn = config.ShowVehicleInfo };
            var showSummaryToggle = new ToggleSwitch { Header = "Show Summary Cards", IsOn = config.ShowSummaryCards };
            var showWarningsToggle = new ToggleSwitch { Header = "Show Warnings", IsOn = config.ShowWarnings };
            var showTipsToggle = new ToggleSwitch { Header = "Show Pro Tips", IsOn = config.ShowProTips };
            var showDetailsToggle = new ToggleSwitch { Header = "Show Operation Details", IsOn = config.ShowOperationDetails };
            var showSubtotalsToggle = new ToggleSwitch { Header = "Show Section Subtotals", IsOn = config.ShowSectionSubtotals };
            var showConfidenceToggle = new ToggleSwitch { Header = "Show Confidence Labels", IsOn = config.ShowConfidenceLabels };

            settingsPanel.Children.Add(showDateToggle);
            settingsPanel.Children.Add(showPageNumToggle);
            settingsPanel.Children.Add(showVehicleToggle);
            settingsPanel.Children.Add(showSummaryToggle);
            settingsPanel.Children.Add(showWarningsToggle);
            settingsPanel.Children.Add(showTipsToggle);
            settingsPanel.Children.Add(showDetailsToggle);
            settingsPanel.Children.Add(showSubtotalsToggle);
            settingsPanel.Children.Add(showConfidenceToggle);

            // Reset button
            var resetButton = new Button
            {
                Content = "Reset to Defaults",
                Margin = new Thickness(0, 8, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 40, 40)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 140, 140)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 6, 12, 6)
            };
            resetButton.Click += (s, args) =>
            {
                configService.ResetToDefaults();
                var defaults = configService.Config;
                titleBox.Text = defaults.HeaderTitle;
                subtitleBox.Text = defaults.HeaderSubtitle;
                footerBox.Text = defaults.FooterText;
                dateFormatBox.Text = defaults.DateFormat;
                showDateToggle.IsOn = defaults.ShowDate;
                showPageNumToggle.IsOn = defaults.ShowPageNumbers;
                showVehicleToggle.IsOn = defaults.ShowVehicleInfo;
                showSummaryToggle.IsOn = defaults.ShowSummaryCards;
                showWarningsToggle.IsOn = defaults.ShowWarnings;
                showTipsToggle.IsOn = defaults.ShowProTips;
                showDetailsToggle.IsOn = defaults.ShowOperationDetails;
                showSubtotalsToggle.IsOn = defaults.ShowSectionSubtotals;
                showConfidenceToggle.IsOn = defaults.ShowConfidenceLabels;
            };
            settingsPanel.Children.Add(resetButton);

            var scrollViewer = new ScrollViewer
            {
                Content = settingsPanel,
                MaxHeight = 500,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var dialog = new ContentDialog
            {
                Title = "PDF Export Settings",
                Content = scrollViewer,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                config.HeaderTitle = titleBox.Text;
                config.HeaderSubtitle = subtitleBox.Text;
                config.FooterText = footerBox.Text;
                config.DateFormat = dateFormatBox.Text;
                config.ShowDate = showDateToggle.IsOn;
                config.ShowPageNumbers = showPageNumToggle.IsOn;
                config.ShowVehicleInfo = showVehicleToggle.IsOn;
                config.ShowSummaryCards = showSummaryToggle.IsOn;
                config.ShowWarnings = showWarningsToggle.IsOn;
                config.ShowProTips = showTipsToggle.IsOn;
                config.ShowOperationDetails = showDetailsToggle.IsOn;
                config.ShowSectionSubtotals = showSubtotalsToggle.IsOn;
                config.ShowConfidenceLabels = showConfidenceToggle.IsOn;
                configService.SaveConfig();
                ShowMessage("PDF export settings saved.");
            }
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

            // Recompute totals against the visible (filtered) ops so the breakdown
            // panel matches what's actually shown — and reflects current shop rates.
            RecomputeTotalsFromOps(result, filteredOps);

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
                var vehicleInfo = (_vehicleCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
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
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true
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
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsTextSelectionEnabled = true
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
                _ => (Color.FromArgb(255, 150, 155, 160), "SYS")
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

        /// <summary>
        /// Returns the operations the user has actively included: passes the category filter
        /// AND has a checked checkbox. Defaults to all when checkboxes haven't been built yet.
        /// </summary>
        private List<GuidanceOperation> GetActiveOperations()
        {
            if (_currentResult == null) return new List<GuidanceOperation>();

            var filtered = GetFilteredOperations(_currentResult.GuidanceOperations);

            // No checkboxes registered yet (first display) — return all filtered
            if (_operationCheckboxes.Count == 0) return filtered;

            return filtered
                .Where(op => !_operationCheckboxes.TryGetValue(op, out var cb) || cb.IsChecked == true)
                .ToList();
        }

        /// <summary>
        /// Recompute the totals on the result object using the given operations
        /// and the current shop labor rates. Mirrors the engine's CalculateTotals logic.
        /// </summary>
        private void RecomputeTotalsFromOps(GuidanceEstimateResult result, List<GuidanceOperation> ops)
        {
            var ghostCfg = GhostConfigService.Instance;
            var bodyRate = ghostCfg.GetEffectiveBodyRate();
            var paintRate = ghostCfg.GetEffectivePaintRate();
            var mechRate = ghostCfg.GetEffectiveMechRate();
            var frameRate = ghostCfg.GetEffectiveFrameRate();
            var matRate = ghostCfg.GetEffectiveMaterialRate();

            result.TotalBodyHours = ops
                .Where(o => o.Category == "Body Operations" || o.Category == "Part Operations" || o.Category == "Structural")
                .Sum(o => o.LaborHours);

            result.TotalRefinishHours = ops
                .Where(o => o.Category == "Refinish Operations")
                .Sum(o => o.RefinishHours);

            var mechHours = ops
                .Where(o => o.Category == "Scanning" || o.OperationType == "Mech")
                .Sum(o => o.LaborHours);

            var frameHours = ops
                .Where(o => o.Category == "Frame Operations")
                .Sum(o => o.LaborHours);

            result.TotalSubletAmount = ops
                .Where(o => o.OperationType == "Sublet")
                .Sum(o => o.Price);

            result.TotalBodyLaborDollars = result.TotalBodyHours * bodyRate;
            result.TotalRefinishLaborDollars = result.TotalRefinishHours * paintRate;
            result.TotalMechLaborDollars = mechHours * mechRate;
            result.TotalFrameLaborDollars = frameHours * frameRate;
            result.TotalMaterialDollars = result.TotalRefinishHours * matRate;

            result.GrandTotalLaborDollars =
                result.TotalBodyLaborDollars +
                result.TotalRefinishLaborDollars +
                result.TotalMechLaborDollars +
                result.TotalFrameLaborDollars +
                result.TotalMaterialDollars +
                result.TotalSubletAmount;

            result.BodyRateUsed = bodyRate;
            result.PaintRateUsed = paintRate;
            result.MechRateUsed = mechRate;
            result.FrameRateUsed = frameRate;
            result.MaterialRateUsed = matRate;
            result.MechHoursUsed = mechHours;
            result.FrameHoursUsed = frameHours;
        }

        /// <summary>
        /// Fired when an operation checkbox is toggled. Recomputes totals + cards live
        /// without rebuilding the operation rows (which would reset all checkbox states).
        /// </summary>
        private void OperationCheckbox_Toggled(object sender, RoutedEventArgs e)
        {
            if (_currentResult == null) return;

            var activeOps = GetActiveOperations();
            RecomputeTotalsFromOps(_currentResult, activeOps);

            _summaryCardsPanel?.Children.Clear();
            BuildSummaryCards(_currentResult, activeOps);
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

            // Card 4: Dollar Total from real data (uploaded estimates + Excel tool)
            var opsWithDollars = filteredOps.Count(o => o.LearnedDollarAmount.HasValue && o.LearnedDollarAmount > 0);
            if (opsWithDollars > 0 && result.LearnedDollarTotal.HasValue)
            {
                var learnedText = $"${result.LearnedDollarTotal:F0}";
                if (result.MinDollarTotal.HasValue && result.MaxDollarTotal.HasValue && result.MinDollarTotal != result.MaxDollarTotal)
                    learnedText += $"\n${result.MinDollarTotal:F0}–${result.MaxDollarTotal:F0}";
                var learnedCard = CreateSummaryCard("From Estimates", learnedText,
                    Color.FromArgb(255, 100, 220, 180));
                Grid.SetColumn(learnedCard, 3);
                cardsGrid.Children.Add(learnedCard);
            }

            // Card 5: Data Quality — % of operations with learned data
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
            Grid.SetColumn(qualityCard, 4);
            cardsGrid.Children.Add(qualityCard);

            _summaryCardsPanel?.Children.Add(cardsGrid);

            // Estimate-style totals breakdown (Tractable-style)
            _summaryCardsPanel?.Children.Add(BuildTotalsBreakdown(result));
        }

        private Border BuildTotalsBreakdown(GuidanceEstimateResult result)
        {
            var ghostCfg = GhostConfigService.Instance;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 34, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 70, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(4, 12, 4, 4)
            };

            var stack = new StackPanel { Spacing = 8 };

            // Heading row (editable)
            var headingRow = new Grid();
            headingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headingText = new TextBlock
            {
                Text = ghostCfg.GetEffectiveEstimateHeading(),
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 235, 240)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headingText, 0);
            headingRow.Children.Add(headingText);

            var settingsBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE713", FontSize = 14 },
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 52, 62)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 205, 210)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(settingsBtn, "Edit heading & labor rates");
            settingsBtn.Click += async (_, _) => await OpenTotalsSettings_Click();
            Grid.SetColumn(settingsBtn, 1);
            headingRow.Children.Add(settingsBtn);

            stack.Children.Add(headingRow);

            // Divider
            stack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(255, 55, 65, 78)),
                Margin = new Thickness(0, 2, 0, 6)
            });

            // Line items
            void AddLine(string label, string detail, decimal amount, bool dim = false)
            {
                var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var labelTb = new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(dim
                        ? Color.FromArgb(255, 130, 135, 140)
                        : Color.FromArgb(255, 200, 205, 215))
                };
                Grid.SetColumn(labelTb, 0);
                grid.Children.Add(labelTb);

                var detailTb = new TextBlock
                {
                    Text = detail,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 145))
                };
                Grid.SetColumn(detailTb, 1);
                grid.Children.Add(detailTb);

                var amtTb = new TextBlock
                {
                    Text = $"${amount:N2}",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(dim
                        ? Color.FromArgb(255, 130, 135, 140)
                        : Color.FromArgb(255, 220, 225, 232)),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(amtTb, 2);
                grid.Children.Add(amtTb);

                stack.Children.Add(grid);
            }

            AddLine("Body Labor",
                $"{result.TotalBodyHours:F1} hrs × ${result.BodyRateUsed:F2}/hr",
                result.TotalBodyLaborDollars,
                dim: result.TotalBodyHours == 0);

            AddLine("Refinish Labor",
                $"{result.TotalRefinishHours:F1} hrs × ${result.PaintRateUsed:F2}/hr",
                result.TotalRefinishLaborDollars,
                dim: result.TotalRefinishHours == 0);

            AddLine("Paint Materials",
                $"{result.TotalRefinishHours:F1} hrs × ${result.MaterialRateUsed:F2}/hr",
                result.TotalMaterialDollars,
                dim: result.TotalRefinishHours == 0);

            AddLine("Mechanical Labor",
                $"{result.MechHoursUsed:F1} hrs × ${result.MechRateUsed:F2}/hr",
                result.TotalMechLaborDollars,
                dim: result.MechHoursUsed == 0);

            AddLine("Frame Labor",
                $"{result.FrameHoursUsed:F1} hrs × ${result.FrameRateUsed:F2}/hr",
                result.TotalFrameLaborDollars,
                dim: result.FrameHoursUsed == 0);

            AddLine("Sublet",
                "Pre/Post scan, calibrations, etc.",
                result.TotalSubletAmount,
                dim: result.TotalSubletAmount == 0);

            // Divider
            stack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(255, 55, 65, 78)),
                Margin = new Thickness(0, 6, 0, 6)
            });

            // Grand total row
            var totalGrid = new Grid();
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var totalLabel = new TextBlock
            {
                Text = "TOTAL",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 235, 240)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(totalLabel, 0);
            totalGrid.Children.Add(totalLabel);

            var totalAmt = new TextBlock
            {
                Text = $"${result.GrandTotalLaborDollars:N2}",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 230, 140)),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(totalAmt, 1);
            totalGrid.Children.Add(totalAmt);

            stack.Children.Add(totalGrid);

            // Footer note
            stack.Children.Add(new TextBlock
            {
                Text = "Calculated from labor hours × your shop rates. Parts not included.",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 110, 115, 125)),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Margin = new Thickness(0, 4, 0, 0)
            });

            border.Child = stack;
            return border;
        }

        private async System.Threading.Tasks.Task OpenTotalsSettings_Click()
        {
            var ghostCfg = GhostConfigService.Instance;

            var panel = new StackPanel { Spacing = 12, MinWidth = 380 };

            panel.Children.Add(new TextBlock
            {
                Text = "Estimate Heading",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });

            var headingBox = new TextBox
            {
                Header = "Heading text shown above the totals",
                Text = ghostCfg.GetEffectiveEstimateHeading(),
                FontSize = 13
            };
            panel.Children.Add(headingBox);

            panel.Children.Add(new TextBlock
            {
                Text = "Labor Rates ($/hr)",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
                Margin = new Thickness(0, 8, 0, 0)
            });

            NumberBox MakeRateBox(string header, decimal current)
            {
                return new NumberBox
                {
                    Header = header,
                    Value = (double)current,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                    SmallChange = 1,
                    LargeChange = 10,
                    Minimum = 0
                };
            }

            var bodyBox = MakeRateBox("Body Labor", ghostCfg.GetEffectiveBodyRate());
            var paintBox = MakeRateBox("Refinish Labor", ghostCfg.GetEffectivePaintRate());
            var matBox = MakeRateBox("Paint Materials", ghostCfg.GetEffectiveMaterialRate());
            var mechBox = MakeRateBox("Mechanical Labor", ghostCfg.GetEffectiveMechRate());
            var frameBox = MakeRateBox("Frame Labor", ghostCfg.GetEffectiveFrameRate());

            panel.Children.Add(bodyBox);
            panel.Children.Add(paintBox);
            panel.Children.Add(matBox);
            panel.Children.Add(mechBox);
            panel.Children.Add(frameBox);

            var dialog = new ContentDialog
            {
                Title = "Estimate Totals Settings",
                Content = new ScrollViewer { Content = panel, MaxHeight = 500 },
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            ghostCfg.SetEstimateHeading(headingBox.Text);
            ghostCfg.SetLaborRate("body", (decimal)bodyBox.Value);
            ghostCfg.SetLaborRate("paint", (decimal)paintBox.Value);
            ghostCfg.SetLaborRate("material", (decimal)matBox.Value);
            ghostCfg.SetLaborRate("mech", (decimal)mechBox.Value);
            ghostCfg.SetLaborRate("frame", (decimal)frameBox.Value);

            // Recompute totals on the current result with the new rates and refresh the UI
            if (_currentResult != null)
            {
                var bodyR = (decimal)bodyBox.Value;
                var paintR = (decimal)paintBox.Value;
                var matR = (decimal)matBox.Value;
                var mechR = (decimal)mechBox.Value;
                var frameR = (decimal)frameBox.Value;

                _currentResult.TotalBodyLaborDollars = _currentResult.TotalBodyHours * bodyR;
                _currentResult.TotalRefinishLaborDollars = _currentResult.TotalRefinishHours * paintR;
                _currentResult.TotalMaterialDollars = _currentResult.TotalRefinishHours * matR;
                _currentResult.TotalMechLaborDollars = _currentResult.MechHoursUsed * mechR;
                _currentResult.TotalFrameLaborDollars = _currentResult.FrameHoursUsed * frameR;

                _currentResult.GrandTotalLaborDollars =
                    _currentResult.TotalBodyLaborDollars +
                    _currentResult.TotalRefinishLaborDollars +
                    _currentResult.TotalMaterialDollars +
                    _currentResult.TotalMechLaborDollars +
                    _currentResult.TotalFrameLaborDollars +
                    _currentResult.TotalSubletAmount;

                _currentResult.BodyRateUsed = bodyR;
                _currentResult.PaintRateUsed = paintR;
                _currentResult.MaterialRateUsed = matR;
                _currentResult.MechRateUsed = mechR;
                _currentResult.FrameRateUsed = frameR;

                DisplayGuidanceResults(_currentResult);
            }
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
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 32, 38)),
                Margin = new Thickness(0, 2, 0, 2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var sectionStack = new StackPanel();

            // Section header — CCC style: bold section name with left accent
            var headerButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(0),
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = new SolidColorBrush(catColor)
            };

            var headerContent = new Grid();
            headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chevron = new FontIcon
            {
                Glyph = isCollapsed ? "\uE76C" : "\uE76D",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 125, 130)),
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(chevron, 0);
            headerContent.Children.Add(chevron);

            var catTitle = new TextBlock
            {
                Text = category.ToUpper(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(catColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(catTitle, 1);
            headerContent.Children.Add(catTitle);

            // Section totals
            var bodyHrs = operations.Sum(o => o.LaborHours);
            var rfnHrs = operations.Sum(o => o.RefinishHours);
            var partsDollars = operations.Sum(o => o.Price);
            var totals = new List<string>();
            if (bodyHrs > 0) totals.Add($"Body: {bodyHrs:F1}");
            if (rfnHrs > 0) totals.Add($"Rfn: {rfnHrs:F1}");
            if (partsDollars > 0) totals.Add($"Parts: ${partsDollars:F2}");

            var hoursText = new TextBlock
            {
                Text = string.Join("  ", totals),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 145, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hoursText, 2);
            headerContent.Children.Add(hoursText);

            headerButton.Content = headerContent;

            // Operations panel
            var opsPanel = new StackPanel { Spacing = 0, Padding = new Thickness(0) };
            opsPanel.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;

            headerButton.Click += (s, e) =>
            {
                var collapsed = opsPanel.Visibility == Visibility.Visible;
                opsPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                chevron.Glyph = collapsed ? "\uE76C" : "\uE76D";
                _collapsedCategories[category] = collapsed;
            };

            sectionStack.Children.Add(headerButton);

            // Column header row
            var colHeader = CreateColumnHeaderRow();
            opsPanel.Children.Add(colHeader);

            // Operation rows
            int lineNum = 1;
            foreach (var op in operations)
            {
                var opRow = CreateGuidanceOperationRow(op, lineNum++);
                opsPanel.Children.Add(opRow);
            }

            sectionStack.Children.Add(opsPanel);
            sectionBorder.Child = sectionStack;
            return sectionBorder;
        }

        private Grid CreateColumnHeaderRow()
        {
            var grid = new Grid
            {
                Padding = new Thickness(8, 3, 8, 3),
                Background = new SolidColorBrush(Color.FromArgb(255, 32, 36, 44))
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });  // Checkbox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });  // Line#
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });  // Type
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // Body
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // Paint
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });  // Parts

            var dimColor = new SolidColorBrush(Color.FromArgb(255, 100, 105, 115));
            var headers = new[] { "", "#", "Description", "Type", "Body", "Paint", "Parts" };
            for (int i = 1; i < headers.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = headers[i],
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = dimColor,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = i >= 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };
                Grid.SetColumn(tb, i);
                grid.Children.Add(tb);
            }
            return grid;
        }

        private Border CreateGuidanceOperationRow(GuidanceOperation op, int lineNum)
        {
            var isEvenRow = lineNum % 2 == 0;
            var bgColor = isEvenRow
                ? Color.FromArgb(255, 36, 40, 48)
                : Color.FromArgb(255, 32, 36, 44);

            var border = new Border
            {
                Background = new SolidColorBrush(bgColor),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0)
            };

            var mainStack = new StackPanel { Spacing = 0 };

            // Main row grid — CCC column layout
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });  // Checkbox
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });  // Line#
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });  // Type
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // Body
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // Paint
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });  // Parts

            // Checkbox — default ON. Unchecking excludes this op from totals + PDF export.
            var checkbox = new CheckBox
            {
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = true
            };
            ToolTipService.SetToolTip(checkbox, "Uncheck to exclude this operation from totals & PDF");
            checkbox.Checked += OperationCheckbox_Toggled;
            checkbox.Unchecked += OperationCheckbox_Toggled;
            _operationCheckboxes[op] = checkbox;
            Grid.SetColumn(checkbox, 0);
            row.Children.Add(checkbox);

            // Line number
            var lineText = new TextBlock
            {
                Text = lineNum.ToString(),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 90, 95, 105)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lineText, 1);
            row.Children.Add(lineText);

            // Description
            var descText = new TextBlock
            {
                Text = op.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsTextSelectionEnabled = true
            };
            Grid.SetColumn(descText, 2);
            row.Children.Add(descText);

            // Type code
            var typeCode = op.OperationType switch
            {
                "Replace" or "Repl" => "REPL",
                "Repair" or "Rpr" => "RPR",
                "Refinish" or "Rfn" => "RFN",
                "Blend" => "BLD",
                "Body" => "BODY",
                "Mech" => "MECH",
                "Frame" => "FRM",
                "r&i" or "R&I" => "R&I",
                _ => op.OperationType?.ToUpper() ?? ""
            };
            var typeText = new TextBlock
            {
                Text = typeCode,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 160, 180)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(typeText, 3);
            row.Children.Add(typeText);

            // Body hours
            if (op.LaborHours > 0)
            {
                var bodyText = new TextBlock
                {
                    Text = $"{op.LaborHours:F1}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 210, 220)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(bodyText, 4);
                row.Children.Add(bodyText);
            }

            // Paint/Refinish hours
            if (op.RefinishHours > 0)
            {
                var paintText = new TextBlock
                {
                    Text = $"{op.RefinishHours:F1}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 200, 255)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(paintText, 5);
                row.Children.Add(paintText);
            }

            // Parts price
            if (op.Price > 0)
            {
                var partsText = new TextBlock
                {
                    Text = $"${op.Price:F2}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 220, 180)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(partsText, 6);
                row.Children.Add(partsText);
            }

            mainStack.Children.Add(row);

            // Expandable detail row (click to show source/justification)
            var hasDetails = !string.IsNullOrEmpty(op.Source) ||
                             !string.IsNullOrEmpty(op.Justification) ||
                             !string.IsNullOrEmpty(op.PPageReference) ||
                             !string.IsNullOrEmpty(op.DEGReference);

            if (hasDetails)
            {
                var detailPanel = new StackPanel
                {
                    Spacing = 1,
                    Margin = new Thickness(52, 2, 0, 2),
                    Visibility = Visibility.Collapsed
                };

                if (!string.IsNullOrEmpty(op.Source))
                    detailPanel.Children.Add(CreateDetailText($"Source: {op.Source}", Color.FromArgb(255, 130, 135, 140)));
                if (!string.IsNullOrEmpty(op.Justification) && op.Justification != op.Source)
                    detailPanel.Children.Add(CreateDetailText($"Why: {op.Justification}", Color.FromArgb(255, 130, 160, 180)));
                if (!string.IsNullOrEmpty(op.PPageReference))
                    detailPanel.Children.Add(CreateDetailText($"P-Page: {op.PPageReference}", Color.FromArgb(255, 180, 160, 130)));
                if (!string.IsNullOrEmpty(op.DEGReference))
                    detailPanel.Children.Add(CreateDetailText($"DEG: {op.DEGReference}", Color.FromArgb(255, 180, 160, 130)));

                mainStack.Children.Add(detailPanel);

                border.PointerPressed += (s, e) =>
                {
                    detailPanel.Visibility = detailPanel.Visibility == Visibility.Visible
                        ? Visibility.Collapsed : Visibility.Visible;
                };
            }

            border.Child = mainStack;
            return border;
        }

        private static TextBlock CreateDetailText(string text, Color color)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = new SolidColorBrush(color),
                TextWrapping = TextWrapping.Wrap
            };
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
