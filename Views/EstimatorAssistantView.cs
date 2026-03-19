#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using McStudDesktop.Services;
using PartInput = McStudDesktop.Services.PartInput;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Estimator Assistant - Comprehensive tool for finding operations
    ///
    /// Features:
    /// 1. Build Ops - Type anything, set labor times, get operation list
    /// 2. Unified Search - Search across Excel, P-Pages, DEG, definitions
    /// 3. Part Lookup - "What operations do I need for [part]?"
    /// 4. Missing Ops - Paste estimate, find potentially missing operations
    /// 5. Justifications - Why should I add this operation?
    /// </summary>
    public sealed class EstimatorAssistantView : UserControl
    {
        private readonly OperationKnowledgeService _knowledgeService = OperationKnowledgeService.Instance;
        private readonly ExcelFormulaService _excelService = ExcelFormulaService.Instance;
        private readonly ExcelPartKnowledgeService _partKnowledgeService = ExcelPartKnowledgeService.Instance;
        private readonly SmartExportService _exportService = new();

        // UI Elements
        private TextBox? _searchBox;
        private StackPanel? _resultsPanel;
        private ScrollViewer? _resultsScroll;

        // Tab buttons
        private Button? _buildTabBtn;
        private Button? _searchTabBtn;
        private Button? _partTabBtn;
        private Button? _missingTabBtn;
        private int _currentTab = 0;

        // Build operations tab
        private ComboBox? _sheetCombo;
        private StackPanel? _categoryPanel;
        private ComboBox? _partTypeCombo;
        private TextBox? _partNameBox;
        private NumberBox? _refinishUnitsBox;
        private NumberBox? _riTimeBox;
        private NumberBox? _repairTimeBox;
        private StackPanel? _buildResultsPanel;
        private StackPanel? _additionalOptionsPanel;
        private List<CheckBox> _optionCheckboxes = new();
        private List<CheckBox> _categoryCheckboxes = new();
        private string _currentSheet = "";
        private OperationBuild? _lastBuildResult;

        // Part lookup
        private AutoSuggestBox? _partSearchBox;
        private StackPanel? _partResultsPanel;

        // Damage area (moved to part lookup)
        private ComboBox? _damageAreaCombo;
        private StackPanel? _damageResultsPanel;

        // Missing ops
        private TextBox? _estimateInput;
        private StackPanel? _missingOpsPanel;

        // Selected operations for export
        private List<OperationRecommendation> _selectedOps = new();

        public EstimatorAssistantView()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            var mainGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                Padding = new Thickness(0)
            };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Tabs
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

            // Tab bar (no separate header - the tab already says "Assistant")
            var tabBar = CreateTabBar();
            Grid.SetRow(tabBar, 0);
            mainGrid.Children.Add(tabBar);

            // Content area - will switch based on tab
            var contentArea = new Grid();
            Grid.SetRow(contentArea, 1);
            mainGrid.Children.Add(contentArea);

            // Create all tab contents
            var buildContent = CreateBuildOpsTab();
            var searchContent = CreateSearchTab();
            var partContent = CreatePartLookupTab();
            var missingContent = CreateMissingOpsTab();

            contentArea.Children.Add(buildContent);
            contentArea.Children.Add(searchContent);
            contentArea.Children.Add(partContent);
            contentArea.Children.Add(missingContent);

            // Set initial visibility - Build Ops is now first
            buildContent.Visibility = Visibility.Visible;
            searchContent.Visibility = Visibility.Collapsed;
            partContent.Visibility = Visibility.Collapsed;
            missingContent.Visibility = Visibility.Collapsed;

            this.Content = mainGrid;
        }

        private Border CreateTabBar()
        {
            var tabPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Padding = new Thickness(8, 6, 8, 6)
            };

            _buildTabBtn = CreateTabButton("Build Ops", 0, true);
            _searchTabBtn = CreateTabButton("Search", 1, false);
            _partTabBtn = CreateTabButton("Parts", 2, false);
            _missingTabBtn = CreateTabButton("Missing", 3, false);

            tabPanel.Children.Add(_buildTabBtn);
            tabPanel.Children.Add(_searchTabBtn);
            tabPanel.Children.Add(_partTabBtn);
            tabPanel.Children.Add(_missingTabBtn);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
                Child = tabPanel
            };
        }

        private Button CreateTabButton(string text, int tabIndex, bool isActive)
        {
            var btn = new Button
            {
                Content = text,
                Tag = tabIndex,
                FontSize = 13,
                Padding = new Thickness(14, 6, 14, 6),
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(255, 60, 120, 80))
                    : new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };
            btn.Click += TabButton_Click;
            return btn;
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int tabIndex)
            {
                SwitchToTab(tabIndex);
            }
        }

        private void SwitchToTab(int tabIndex)
        {
            _currentTab = tabIndex;

            // Update button styles
            var buttons = new[] { _buildTabBtn, _searchTabBtn, _partTabBtn, _missingTabBtn };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null)
                {
                    buttons[i]!.Background = i == tabIndex
                        ? new SolidColorBrush(Color.FromArgb(255, 60, 120, 80))
                        : new SolidColorBrush(Colors.Transparent);
                }
            }

            // Update content visibility
            var contentGrid = (this.Content as Grid)?.Children[1] as Grid;
            if (contentGrid != null)
            {
                for (int i = 0; i < contentGrid.Children.Count; i++)
                {
                    if (contentGrid.Children[i] is UIElement element)
                    {
                        element.Visibility = i == tabIndex ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
        }

        #region Tab 0: Build Operations

        private Grid CreateBuildOpsTab()
        {
            var grid = new Grid { Padding = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sheet selector
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Category options
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Part type & name (for Part Ops)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Labor inputs
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Additional options
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Build button
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Results

            // Row 0: Sheet/Tab selector
            var sheetPanel = new StackPanel { Spacing = 5 };
            sheetPanel.Children.Add(new TextBlock
            {
                Text = "What are you working on?",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 150))
            });

            _sheetCombo = new ComboBox
            {
                PlaceholderText = "Select operation category...",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                Margin = new Thickness(0, 5, 0, 0)
            };

            // Add all Excel sheet tabs
            // Excel tab names - match exactly
            _sheetCombo.Items.Add("SOP List");
            _sheetCombo.Items.Add("Part Operations");
            _sheetCombo.Items.Add("Cover Car Operations");
            _sheetCombo.Items.Add("Body Operations");
            _sheetCombo.Items.Add("Refinish Operations");
            _sheetCombo.Items.Add("Mechanical Operations");
            _sheetCombo.Items.Add("SRS Operations");
            _sheetCombo.Items.Add("Total Loss Charges");
            _sheetCombo.Items.Add("Body On Frame");
            _sheetCombo.Items.Add("Stolen Recovery");
            _sheetCombo.Items.Add("Post Repair Inspection");

            _sheetCombo.SelectionChanged += SheetCombo_SelectionChanged;
            sheetPanel.Children.Add(_sheetCombo);
            Grid.SetRow(sheetPanel, 0);
            grid.Children.Add(sheetPanel);

            // Row 1: Category-specific options (dynamically populated)
            _categoryPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(_categoryPanel, 1);
            grid.Children.Add(_categoryPanel);

            // Row 2: Part Type and Part Name (shown only for Part Operations)
            var partPanel = new Grid { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            partPanel.Name = "PartPanel";
            partPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            partPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var partTypeStack = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 5, 0) };
            partTypeStack.Children.Add(new TextBlock
            {
                Text = "Part Type",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            _partTypeCombo = new ComboBox
            {
                PlaceholderText = "Select...",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40))
            };
            // Populate part types
            foreach (var partType in _partKnowledgeService.GetAllPartTypes())
            {
                _partTypeCombo.Items.Add(partType);
            }
            _partTypeCombo.SelectionChanged += PartTypeCombo_SelectionChanged;
            partTypeStack.Children.Add(_partTypeCombo);
            Grid.SetColumn(partTypeStack, 0);
            partPanel.Children.Add(partTypeStack);

            var partNameStack = new StackPanel { Spacing = 3, Margin = new Thickness(5, 0, 0, 0) };
            partNameStack.Children.Add(new TextBlock
            {
                Text = "Part Name",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            _partNameBox = new TextBox
            {
                PlaceholderText = "e.g., LT Quarter Panel",
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            partNameStack.Children.Add(_partNameBox);
            Grid.SetColumn(partNameStack, 1);
            partPanel.Children.Add(partNameStack);

            Grid.SetRow(partPanel, 2);
            grid.Children.Add(partPanel);

            // Row 3: Labor Time Inputs (shown only for Part Operations)
            var laborPanel = new Grid { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            laborPanel.Name = "LaborPanel";
            laborPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            laborPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            laborPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var refinishStack = CreateLaborInput("Refinish Units", out _refinishUnitsBox);
            Grid.SetColumn(refinishStack, 0);
            laborPanel.Children.Add(refinishStack);

            var riStack = CreateLaborInput("R&I Time", out _riTimeBox);
            Grid.SetColumn(riStack, 1);
            laborPanel.Children.Add(riStack);

            var repairStack = CreateLaborInput("Repair Time", out _repairTimeBox);
            Grid.SetColumn(repairStack, 2);
            laborPanel.Children.Add(repairStack);

            Grid.SetRow(laborPanel, 3);
            grid.Children.Add(laborPanel);

            // Row 4: Additional Options (populated dynamically based on selection)
            var optionsHeader = new TextBlock
            {
                Text = "Additional Options:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(0, 10, 0, 5)
            };

            _additionalOptionsPanel = new StackPanel { Spacing = 3 };
            var optionsScroll = new ScrollViewer
            {
                Content = _additionalOptionsPanel,
                MaxHeight = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var optionsContainer = new StackPanel();
            optionsContainer.Children.Add(optionsHeader);
            optionsContainer.Children.Add(optionsScroll);
            Grid.SetRow(optionsContainer, 4);
            grid.Children.Add(optionsContainer);

            // Row 5: Build Button
            var buildBtn = new Button
            {
                Content = "Build Operation List",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(15, 10, 15, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            buildBtn.Click += BuildButton_Click;
            Grid.SetRow(buildBtn, 5);
            grid.Children.Add(buildBtn);

            // Row 6: Results
            _buildResultsPanel = new StackPanel { Spacing = 5, Margin = new Thickness(0, 10, 0, 0) };
            var resultsScroll = new ScrollViewer
            {
                Content = _buildResultsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(resultsScroll, 6);
            grid.Children.Add(resultsScroll);

            return grid;
        }

        private void SheetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_sheetCombo?.SelectedItem is string selection)
            {
                _currentSheet = selection;
                UpdateCategoryOptions(selection);

                // Find the part panel and labor panel
                var contentGrid = (this.Content as Grid)?.Children[1] as Grid;
                if (contentGrid?.Children[0] is Grid buildGrid)
                {
                    // Show/hide part-specific controls based on selection
                    bool isPartOps = selection == "Part Operations";
                    foreach (var child in buildGrid.Children)
                    {
                        if (child is Grid panel)
                        {
                            if (panel.Name == "PartPanel" || panel.Name == "LaborPanel")
                            {
                                panel.Visibility = isPartOps ? Visibility.Visible : Visibility.Collapsed;
                            }
                        }
                    }
                }
            }
        }

        private void UpdateCategoryOptions(string sheetSelection)
        {
            _categoryPanel?.Children.Clear();
            _categoryCheckboxes.Clear();
            _additionalOptionsPanel?.Children.Clear();
            _optionCheckboxes.Clear();

            // Get grouped options for the sheet (organized like Excel)
            var sections = GetSectionsForSheet(sheetSelection);

            if (sections.Count == 0) return;

            foreach (var section in sections)
            {
                // Section header
                _categoryPanel?.Children.Add(new TextBlock
                {
                    Text = section.Key,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 130)),
                    Margin = new Thickness(0, 8, 0, 4)
                });

                var wrapPanel = new StackPanel { Orientation = Orientation.Horizontal };
                int count = 0;

                foreach (var option in section.Value)
                {
                    var cb = new CheckBox
                    {
                        Content = option.Name,
                        Tag = option,
                        Foreground = new SolidColorBrush(Colors.White),
                        Margin = new Thickness(0, 0, 12, 4),
                        MinWidth = 150,
                        IsChecked = option.IsDefault
                    };
                    _categoryCheckboxes.Add(cb);

                    if (count % 3 == 0 && count > 0)
                    {
                        _categoryPanel?.Children.Add(wrapPanel);
                        wrapPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    }
                    wrapPanel.Children.Add(cb);
                    count++;
                }

                if (wrapPanel.Children.Count > 0)
                {
                    _categoryPanel?.Children.Add(wrapPanel);
                }
            }
        }

        private Dictionary<string, List<SheetOption>> GetSectionsForSheet(string sheetSelection)
        {
            var sections = new Dictionary<string, List<SheetOption>>();
            var options = GetOptionsForSheet(sheetSelection);

            if (options.Count == 0) return sections;

            // Group options by category type (matching Excel sections)
            foreach (var option in options)
            {
                var sectionName = DetermineSectionName(option.Name, option.Category, sheetSelection);

                if (!sections.ContainsKey(sectionName))
                {
                    sections[sectionName] = new List<SheetOption>();
                }
                sections[sectionName].Add(option);
            }

            return sections;
        }

        private string DetermineSectionName(string name, string category, string sheet)
        {
            var lower = name.ToLowerInvariant();

            // SOP List sections (matching Excel)
            if (sheet == "SOP List")
            {
                if (lower.Contains("battery") || lower.Contains("electric")) return "Electrical";
                if (lower.Contains("scan") || lower.Contains("diagnos") || lower.Contains("adas")) return "Vehicle Diagnostics";
                if (lower.Contains("clean") || lower.Contains("delivery") || lower.Contains("disposal") ||
                    lower.Contains("hardware") || lower.Contains("cover") || lower.Contains("mask")) return "Misc/Delivery";
                return "Standard Operations";
            }

            // Body Operations sections
            if (sheet == "Body Operations")
            {
                if (lower.Contains("weld") || lower.Contains("drill")) return "Welding";
                if (lower.Contains("frame") || lower.Contains("measure")) return "Frame/Structural";
                return "Body";
            }

            // Mechanical Operations sections
            if (sheet == "Mechanical Operations")
            {
                if (lower.Contains("refrigerant") || lower.Contains("a/c") || lower.Contains("ac ")) return "A/C System";
                if (lower.Contains("coolant")) return "Cooling System";
                if (lower.Contains("steer") || lower.Contains("align")) return "Steering/Alignment";
                if (lower.Contains("tire") || lower.Contains("wheel") || lower.Contains("tpms") ||
                    lower.Contains("torque") || lower.Contains("mount") || lower.Contains("balance")) return "Wheels/Tires";
                return "Mechanical";
            }

            // Refinish Operations sections
            if (sheet == "Refinish Operations")
            {
                if (lower.Contains("tint") || lower.Contains("color")) return "Color Matching";
                if (lower.Contains("material") || lower.Contains("invoice")) return "Materials";
                return "Refinish";
            }

            // Cover Car sections
            if (sheet == "Cover Car Operations")
            {
                return "Masking/Cover";
            }

            // Default to category
            return string.IsNullOrEmpty(category) ? "Operations" : category;
        }

        private List<SheetOption> GetOptionsForSheet(string sheetSelection)
        {
            var options = new List<SheetOption>();

            switch (sheetSelection)
            {
                case "SOP List":
                    // Battery & Scanning operations
                    options.Add(new SheetOption("Disconnect/Reconnect Battery", "M", 0.4m, 0, true));
                    options.Add(new SheetOption("Test Battery Condition", "M", 0.2m, 0, false));
                    options.Add(new SheetOption("Electronic Reset", "M", 0.5m, 0, true));
                    options.Add(new SheetOption("Cover/Protect Electrical", "M", 0.3m, 5, false));
                    options.Add(new SheetOption("Battery Support", "M", 0.2m, 0, false));
                    options.Add(new SheetOption("Pre-Scan", "M", 0, 150, true));
                    options.Add(new SheetOption("In-Process Scan", "M", 0, 150, false));
                    options.Add(new SheetOption("Post Scan", "M", 0, 150, true));
                    options.Add(new SheetOption("Setup Scan Tool", "M", 0.2m, 0, true));
                    options.Add(new SheetOption("OEM Research", "M", 1.0m, 50, false));
                    options.Add(new SheetOption("ADAS Diagnostic Report", "M", 0, 25, false));
                    options.Add(new SheetOption("Dynamic Systems Verification", "M", 1.0m, 0, false));
                    // Cleaning & Delivery operations
                    options.Add(new SheetOption("Clean for Delivery", "Rpr", 1.0m, 0, true));
                    options.Add(new SheetOption("Glass Cleaner", "", 0, 2, true));
                    options.Add(new SheetOption("Mask/Protect Removed Parts", "Rpr", 0.5m, 10, true));
                    options.Add(new SheetOption("Parts Disposal", "", 0, 25, true));
                    options.Add(new SheetOption("Hazardous Waste Disposal", "", 0, 7.5m, false));
                    options.Add(new SheetOption("Misc Hardware", "", 0, 15, true));
                    options.Add(new SheetOption("Steering Wheel/Seat Covers", "Rpr", 0.2m, 5, true));
                    break;

                case "Part Operations":
                    // Part operations uses the part type combo instead
                    break;

                case "Cover Car Operations":
                    options.Add(new SheetOption("Cover Car for Overspray", "Refinish", 0, 0.2m, true, true));
                    options.Add(new SheetOption("Cover for Edging", "Refinish", 0, 0.3m, false, true));
                    options.Add(new SheetOption("Mask for Buffing", "Refinish", 0, 0.5m, false, true));
                    options.Add(new SheetOption("Cover Engine Compartment", "Refinish", 0, 0.2m, false, true));
                    options.Add(new SheetOption("Cover Interior/Jambs for Refinish", "Refinish", 0, 0.2m, false, true));
                    options.Add(new SheetOption("Clean/Cover Car for Primer", "Refinish", 0, 0.3m, false, true));
                    options.Add(new SheetOption("Cover Interior/Jambs for Repairs", "Refinish", 0, 0.2m, false, true));
                    break;

                case "Body Operations":
                    // Welding operations
                    options.Add(new SheetOption("Weld Blankets", "Rpr", 0.3m, 0, true));
                    options.Add(new SheetOption("Welder Setup", "Rpr", 0.5m, 25, true));
                    options.Add(new SheetOption("Welding Consumables", "", 0, 25, true));
                    options.Add(new SheetOption("Weld Through Primer", "", 0, 10, true));
                    options.Add(new SheetOption("Destructive Test Weld", "Rpr", 0.5m, 0, false));
                    options.Add(new SheetOption("Remove Debris from Drilling", "Rpr", 0.2m, 0, true));
                    options.Add(new SheetOption("R&I Electronics in Weld Zone", "M", 1.0m, 0, false));
                    // Frame operations
                    options.Add(new SheetOption("Frame Setup", "F", 2.0m, 0, true));
                    options.Add(new SheetOption("Pre/Post Measure", "F", 2.5m, 0, true));
                    options.Add(new SheetOption("Measure Gap Dimensions", "Rpr", 0.3m, 0, false));
                    options.Add(new SheetOption("Body Plugs", "Rpr", 0.2m, 5, false));
                    options.Add(new SheetOption("Remove Factory E-Coat", "Rpr", 0.5m, 0, false));
                    break;

                case "Refinish Operations":
                    options.Add(new SheetOption("Refinish Material Invoice", "", 0, 1, true));
                    options.Add(new SheetOption("Color Tint (2-Stage)", "Refinish", 0, 0.5m, false, true));
                    options.Add(new SheetOption("Color Tint (3-Stage)", "Refinish", 0, 0.7m, false, true));
                    options.Add(new SheetOption("Spray Out Cards", "Refinish", 0, 0.5m, true, true));
                    options.Add(new SheetOption("Static Gun", "Rpr", 0.1m, 14, false));
                    options.Add(new SheetOption("Monitor Flash/Cure Time", "Refinish", 0, 0.2m, false, true));
                    break;

                case "Mechanical Operations":
                    // A/C operations
                    options.Add(new SheetOption("R1234yf Refrigerant + Oil", "", 0, 485, true));
                    options.Add(new SheetOption("R134a Refrigerant + Oil", "", 0, 285, false));
                    options.Add(new SheetOption("R744 Refrigerant + Oil", "", 0, 585, false));
                    options.Add(new SheetOption("Cover/Protect AC Lines", "Rpr", 0.2m, 3, true));
                    options.Add(new SheetOption("Climate Control Check", "M", 0.2m, 0, true));
                    options.Add(new SheetOption("Operating Temperature", "M", 0.2m, 0, true));
                    // Coolant operations
                    options.Add(new SheetOption("Coolant", "M", 1.0m, 25, true));
                    // Steering operations
                    options.Add(new SheetOption("Steering Angle Sensor", "M", 1.0m, 0, false));
                    options.Add(new SheetOption("Steering Wheel Lock", "M", 0.1m, 0, false));
                    options.Add(new SheetOption("Alignment Check", "M", 0.5m, 0, false));
                    options.Add(new SheetOption("Power Steering Check", "M", 0.2m, 0, false));
                    // Wheels/Tires operations
                    options.Add(new SheetOption("Torque Wheels", "M", 0.2m, 0, true));
                    options.Add(new SheetOption("Mount and Balance", "M", 0.5m, 0, false));
                    options.Add(new SheetOption("Wheel Weights", "M", 0, 5, false));
                    options.Add(new SheetOption("Tire Mounting Paste", "M", 0, 2, false));
                    options.Add(new SheetOption("Tire Disposal", "M", 0, 5, false));
                    options.Add(new SheetOption("Tire Tax", "M", 0, 1.5m, false));
                    options.Add(new SheetOption("TPMS Programming", "M", 0.3m, 0, false));
                    break;

                case "SRS Operations":
                    options.Add(new SheetOption("Safety Inspections", "M", 4.0m, 0, false));
                    options.Add(new SheetOption("Seatbelts Inspection", "M", 1.0m, 0, false));
                    options.Add(new SheetOption("Steering Column Inspection", "M", 1.0m, 0, false));
                    options.Add(new SheetOption("Steering Gear Inspection", "M", 0.5m, 0, false));
                    options.Add(new SheetOption("Disable/Enable SRS", "M", 0.3m, 0, true));
                    options.Add(new SheetOption("Airbag Residue Cleanup", "Rpr", 1.5m, 0, false));
                    options.Add(new SheetOption("Restraint Module Program", "", 0, 200, false));
                    break;

                case "Total Loss Charges":
                    options.Add(new SheetOption("Teardown/Inspection", "Rpr", 2.0m, 0, true));
                    options.Add(new SheetOption("Reassembly", "Rpr", 1.5m, 0, true));
                    options.Add(new SheetOption("Storage Fee", "", 0, 50, false));
                    options.Add(new SheetOption("Administration Fee", "", 0, 75, false));
                    break;

                case "Body On Frame":
                    options.Add(new SheetOption("Frame Inspection", "F", 1.0m, 0, true));
                    options.Add(new SheetOption("Bed Removal", "Rpr", 2.0m, 0, false));
                    options.Add(new SheetOption("Cab to Frame Alignment", "F", 1.5m, 0, false));
                    options.Add(new SheetOption("Body Mount Bushings", "", 0, 50, false));
                    break;

                case "Stolen Recovery":
                    options.Add(new SheetOption("Vehicle Inspection", "Rpr", 1.0m, 0, true));
                    options.Add(new SheetOption("Key/Lock Cylinder", "", 0, 150, false));
                    options.Add(new SheetOption("Ignition Module", "", 0, 200, false));
                    options.Add(new SheetOption("Security System Reset", "M", 0.5m, 0, false));
                    break;

                case "Post Repair Inspection":
                    options.Add(new SheetOption("Final Inspection", "Rpr", 0.5m, 0, true));
                    options.Add(new SheetOption("Road Test", "M", 0.5m, 0, true));
                    options.Add(new SheetOption("QC Checklist", "Rpr", 0.3m, 0, true));
                    options.Add(new SheetOption("Customer Walkthrough", "Rpr", 0.3m, 0, false));
                    break;
            }

            return options;
        }

        private StackPanel CreateLaborInput(string label, out NumberBox numberBox)
        {
            var stack = new StackPanel { Spacing = 3, Margin = new Thickness(2, 0, 2, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            numberBox = new NumberBox
            {
                Value = 0,
                Minimum = 0,
                Maximum = 100,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                SmallChange = 0.1,
                LargeChange = 1,
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40))
            };
            stack.Children.Add(numberBox);
            return stack;
        }

        private void PartTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_partTypeCombo?.SelectedItem is string partType)
            {
                UpdateAdditionalOptions(partType);
            }
        }

        private void UpdateAdditionalOptions(string partType)
        {
            _additionalOptionsPanel?.Children.Clear();
            _optionCheckboxes.Clear();

            var inputs = _partKnowledgeService.GetInputsForPartType(partType);

            if (inputs.Count == 0) return;

            // Group inputs by category (based on common prefixes/types)
            var refinishOps = new List<PartInput>();
            var materialOps = new List<PartInput>();
            var otherOps = new List<PartInput>();

            foreach (var input in inputs)
            {
                var name = input.Name?.ToLowerInvariant() ?? "";
                if (name.Contains("de-nib") || name.Contains("blend") || name.Contains("clear") ||
                    name.Contains("buff") || name.Contains("sand") || name.Contains("prime") ||
                    name.Contains("ceramic") || name.Contains("tint"))
                {
                    refinishOps.Add(input);
                }
                else if (name.Contains("adhesion") || name.Contains("flex") || name.Contains("sealer") ||
                         name.Contains("wax") || name.Contains("corrosion") || name.Contains("undercoat"))
                {
                    materialOps.Add(input);
                }
                else
                {
                    otherOps.Add(input);
                }
            }

            // Add Refinish section
            if (refinishOps.Any())
            {
                AddOptionSection("Refinish Operations", refinishOps);
            }

            // Add Materials section
            if (materialOps.Any())
            {
                AddOptionSection("Materials", materialOps);
            }

            // Add Other section
            if (otherOps.Any())
            {
                AddOptionSection("Other Options", otherOps);
            }
        }

        private void AddOptionSection(string sectionName, List<PartInput> inputs)
        {
            // Section header
            _additionalOptionsPanel?.Children.Add(new TextBlock
            {
                Text = sectionName,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 130)),
                Margin = new Thickness(0, 5, 0, 3)
            });

            // Checkboxes in a wrap panel
            var wrapPanel = new StackPanel { Orientation = Orientation.Horizontal };
            int count = 0;

            foreach (var input in inputs)
            {
                var cb = new CheckBox
                {
                    Content = input.Name,
                    Tag = input.Name,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 12, 3),
                    MinWidth = 130
                };
                _optionCheckboxes.Add(cb);

                if (count % 3 == 0 && count > 0)
                {
                    _additionalOptionsPanel?.Children.Add(wrapPanel);
                    wrapPanel = new StackPanel { Orientation = Orientation.Horizontal };
                }
                wrapPanel.Children.Add(cb);
                count++;
            }

            if (wrapPanel.Children.Count > 0)
            {
                _additionalOptionsPanel?.Children.Add(wrapPanel);
            }
        }

        private void BuildButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSheet == "Part Operations")
            {
                // Use part-specific build
                var request = new OperationBuildRequest
                {
                    PartType = _partTypeCombo?.SelectedItem as string,
                    PartName = _partNameBox?.Text?.Trim(),
                    RefinishUnits = (decimal)(_refinishUnitsBox?.Value ?? 0),
                    RITime = (decimal)(_riTimeBox?.Value ?? 0),
                    RepairTime = (decimal)(_repairTimeBox?.Value ?? 0),
                    SelectedOptions = _optionCheckboxes
                        .Where(cb => cb.IsChecked == true)
                        .Select(cb => cb.Tag?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList()
                };

                var result = _partKnowledgeService.BuildOperations(request);
                DisplayBuildResults(result);
            }
            else
            {
                // Build from sheet category checkboxes
                var result = BuildFromSheetOptions();
                DisplayBuildResults(result);
            }
        }

        private OperationBuild BuildFromSheetOptions()
        {
            var result = new OperationBuild
            {
                PartType = _currentSheet,
                PartName = _currentSheet,
                Operations = new List<BuiltOperation>()
            };

            foreach (var cb in _categoryCheckboxes)
            {
                if (cb.IsChecked == true && cb.Tag is SheetOption option)
                {
                    var op = new BuiltOperation
                    {
                        Description = option.Name,
                        Category = option.Category,
                        LaborHours = option.LaborHours,
                        RefinishHours = option.RefinishHours,
                        Price = option.Price,
                        Source = _currentSheet
                    };
                    result.Operations.Add(op);
                    result.TotalLaborHours += op.LaborHours;
                    result.TotalRefinishHours += op.RefinishHours;
                    result.TotalPrice += op.Price;
                }
            }

            return result;
        }

        private void DisplayBuildResults(OperationBuild result)
        {
            _buildResultsPanel?.Children.Clear();
            _lastBuildResult = result;

            if (result.Operations.Count == 0)
            {
                _buildResultsPanel?.Children.Add(new TextBlock
                {
                    Text = "No operations generated. Select a category and check the operations you need.",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 150, 150)),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            // Summary header
            var summary = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 60, 50)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var summaryParts = new List<string> { $"{result.Operations.Count} Operations" };
            if (result.TotalLaborHours > 0) summaryParts.Add($"Labor: {result.TotalLaborHours:F1} hr");
            if (result.TotalRefinishHours > 0) summaryParts.Add($"Refinish: {result.TotalRefinishHours:F1} hr");
            if (result.TotalPrice > 0) summaryParts.Add($"${result.TotalPrice:N0}");
            var summaryText = new TextBlock
            {
                Text = string.Join(" | ", summaryParts),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 220, 150))
            };
            summary.Child = summaryText;
            _buildResultsPanel?.Children.Add(summary);

            // Copy dropdown for system-specific formats
            var copyFlyout = new MenuFlyout();

            var cccDesktopItem = new MenuFlyoutItem { Text = "CCC Desktop" };
            cccDesktopItem.Click += (s, e) => CopyBuildResultsForSystem("CCC Desktop");
            copyFlyout.Items.Add(cccDesktopItem);

            var cccWebItem = new MenuFlyoutItem { Text = "CCC Web" };
            cccWebItem.Click += (s, e) => CopyBuildResultsForSystem("CCC Web");
            copyFlyout.Items.Add(cccWebItem);

            var mitchellItem = new MenuFlyoutItem { Text = "Mitchell" };
            mitchellItem.Click += (s, e) => CopyBuildResultsForSystem("Mitchell");
            copyFlyout.Items.Add(mitchellItem);

            copyFlyout.Items.Add(new MenuFlyoutSeparator());

            var plainItem = new MenuFlyoutItem { Text = "Plain Text" };
            plainItem.Click += (s, e) => CopyBuildResultsForSystem("Plain");
            copyFlyout.Items.Add(plainItem);

            var copyBtn = new DropDownButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8C8", FontSize = 12 },
                        new TextBlock { Text = "Copy for...", FontSize = 12 }
                    }
                },
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Flyout = copyFlyout
            };

            // Export dropdown for direct insert into estimating systems
            var exportFlyout = new MenuFlyout();

            var exportCccDesktopItem = new MenuFlyoutItem { Text = "CCC Desktop" };
            exportCccDesktopItem.Click += (s, e) => ExportBuildResultsToSystem(SmartExportService.ExportTarget.CCCDesktop);
            exportFlyout.Items.Add(exportCccDesktopItem);

            var exportCccWebItem = new MenuFlyoutItem { Text = "CCC Web" };
            exportCccWebItem.Click += (s, e) => ExportBuildResultsToSystem(SmartExportService.ExportTarget.CCCWeb);
            exportFlyout.Items.Add(exportCccWebItem);

            var exportMitchellItem = new MenuFlyoutItem { Text = "Mitchell" };
            exportMitchellItem.Click += (s, e) => ExportBuildResultsToSystem(SmartExportService.ExportTarget.Mitchell);
            exportFlyout.Items.Add(exportMitchellItem);

            exportFlyout.Items.Add(new MenuFlyoutSeparator());

            var detectItem = new MenuFlyoutItem { Text = "Auto-Detect" };
            detectItem.Click += (s, e) => ExportBuildResultsToSystem(SmartExportService.ExportTarget.None);
            exportFlyout.Items.Add(detectItem);

            var exportBtn = new DropDownButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE898", FontSize = 12 }, // Send icon
                        new TextBlock { Text = "Export to...", FontSize = 12 }
                    }
                },
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 150, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(8, 0, 0, 0),
                Flyout = exportFlyout
            };

            // Position Indicator for CCC export
            var cccService = McstudDesktop.Services.CCCInsertService.Instance;
            var positionIndicator = new Border
            {
                Background = cccService.HasClickPosition
                    ? new SolidColorBrush(Color.FromArgb(255, 30, 50, 40))
                    : new SolidColorBrush(Color.FromArgb(255, 50, 40, 30)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var positionStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            positionStack.Children.Add(new FontIcon
            {
                Glyph = cccService.HasClickPosition ? "\uE73E" : "\uE81C",
                FontSize = 12,
                Foreground = cccService.HasClickPosition
                    ? new SolidColorBrush(Color.FromArgb(255, 100, 200, 150))
                    : new SolidColorBrush(Color.FromArgb(255, 220, 170, 90)),
                VerticalAlignment = VerticalAlignment.Center
            });
            positionStack.Children.Add(new TextBlock
            {
                Text = cccService.HasClickPosition
                    ? $"Position set: {cccService.TargetWindowTitle}"
                    : "Click in CCC where you want to insert, then come back here",
                FontSize = 10,
                Foreground = cccService.HasClickPosition
                    ? new SolidColorBrush(Color.FromArgb(255, 100, 200, 150))
                    : new SolidColorBrush(Color.FromArgb(255, 220, 170, 90)),
                VerticalAlignment = VerticalAlignment.Center
            });
            positionIndicator.Child = positionStack;
            _buildResultsPanel?.Children.Add(positionIndicator);

            // Button panel for copy and export
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            buttonPanel.Children.Add(copyBtn);
            buttonPanel.Children.Add(exportBtn);
            _buildResultsPanel?.Children.Add(buttonPanel);

            // Operations list
            foreach (var op in result.Operations)
            {
                var opCard = CreateBuildOperationCard(op);
                _buildResultsPanel?.Children.Add(opCard);
            }
        }

        private Border CreateBuildOperationCard(BuiltOperation op)
        {
            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftPanel = new StackPanel { Spacing = 2 };
            leftPanel.Children.Add(new TextBlock
            {
                Text = op.Description,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap
            });
            leftPanel.Children.Add(new TextBlock
            {
                Text = $"{op.Category} • {op.Source}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 130, 120))
            });
            Grid.SetColumn(leftPanel, 0);
            panel.Children.Add(leftPanel);

            var rightPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (op.LaborHours > 0)
            {
                rightPanel.Children.Add(new TextBlock
                {
                    Text = $"{op.LaborHours:F1} hr",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150))
                });
            }
            if (op.RefinishHours > 0)
            {
                rightPanel.Children.Add(new TextBlock
                {
                    Text = $"{op.RefinishHours:F1} rfn",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 150, 200))
                });
            }
            if (op.Price > 0)
            {
                rightPanel.Children.Add(new TextBlock
                {
                    Text = $"${op.Price:N0}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 180, 100))
                });
            }
            Grid.SetColumn(rightPanel, 1);
            panel.Children.Add(rightPanel);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 38)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 2, 0, 2),
                Child = panel
            };
        }

        private void CopyBuildResultsForSystem(string system)
        {
            if (_lastBuildResult == null || _lastBuildResult.Operations.Count == 0) return;

            var lines = new List<string>();

            // Convert to SmartExportOp for VirtualClipboardService
            var exportOps = _lastBuildResult.Operations.Select(op => new SmartExportOp
            {
                OperationType = GetBuildOperationType(op),
                Description = op.Description,
                Quantity = 1,
                Price = op.Price,
                LaborHours = op.LaborHours,
                RefinishHours = op.RefinishHours,
                Category = op.Category
            }).ToList();

            // Update virtual clipboard so Export tab shows accurate summary
            VirtualClipboardService.Instance.SetOperations(exportOps, $"Estimator Assistant - {_lastBuildResult.PartType}");

            foreach (var op in _lastBuildResult.Operations)
            {
                // Determine operation type based on category
                string opType = GetBuildOperationType(op);
                string qty = "1";
                string price = op.Price > 0 ? op.Price.ToString("F2") : "";
                string labor = op.LaborHours > 0 ? op.LaborHours.ToString("F1") : "";
                string paint = op.RefinishHours > 0 ? op.RefinishHours.ToString("F1") : "";

                string line;
                switch (system)
                {
                    case "CCC Desktop":
                    case "CCC Web":
                        line = $"{opType}\t{op.Description}\t{qty}\t{price}\t{labor}\t{paint}";
                        break;

                    case "Mitchell":
                        // Mitchell Format: Operation, Description, Qty, Labor, Paint, Price
                        line = $"{opType}\t{op.Description}\t{qty}\t{labor}\t{paint}\t{price}";
                        break;

                    default: // Plain
                        line = $"{op.Description}\t{labor}\t{paint}\t{price}\t{op.Category}";
                        break;
                }
                lines.Add(line);
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(string.Join("\r\n", lines));
            Clipboard.SetContent(dataPackage);
        }

        /// <summary>
        /// Export build results directly to the estimating system using automation
        /// Same as Export Tab - uses AutoHotkeyPasteService for reliable CCC paste
        /// </summary>
        private async void ExportBuildResultsToSystem(SmartExportService.ExportTarget target)
        {
            if (_lastBuildResult == null || _lastBuildResult.Operations.Count == 0) return;

            // Convert build operations to SmartExportOp format
            var exportOps = _lastBuildResult.Operations.Select(op => new SmartExportOp
            {
                OperationType = GetBuildOperationType(op),
                Description = op.Description,
                Quantity = 1,
                Price = op.Price,
                LaborHours = op.LaborHours,
                RefinishHours = op.RefinishHours,
                Category = op.Category
            }).ToList();

            // Update virtual clipboard so Export tab shows accurate summary
            VirtualClipboardService.Instance.SetOperations(exportOps, $"Estimator Assistant - {_lastBuildResult.PartType}");

            // Use AutoHotkeyPasteService - same as Export Tab for consistent behavior
            using var pasteService = new AutoHotkeyPasteService();

            // Convert to row format for paste service
            var rows = new List<string[]>();
            foreach (var op in _lastBuildResult.Operations)
            {
                string opType = GetBuildOperationType(op);
                string labor = op.LaborHours > 0 ? op.LaborHours.ToString("F1") : "";
                string refinish = op.RefinishHours > 0 ? op.RefinishHours.ToString("F1") : "";

                // Format: OpType, Description, Labor, Refinish (what CCC needs)
                rows.Add(new[] { opType, op.Description, labor, refinish });
            }

            try
            {
                // Alt+Tab back to CCC and paste - same as Export Tab
                bool success = await pasteService.PasteToApp(rows.ToArray(), switchWindow: true);

                // Could show feedback here if needed
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Export error: {ex.Message}");
            }
        }

        /// <summary>
        /// Determine the operation type (Rpr, Add, R&I, Refinish, etc.) based on category
        /// </summary>
        private string GetBuildOperationType(BuiltOperation op)
        {
            var desc = op.Description.ToLowerInvariant();
            var category = op.Category.ToLowerInvariant();

            // Check description for specific operation types
            if (desc.Contains("r&i") || desc.Contains("r+i")) return "R&I";
            if (desc.Contains("replace")) return "Replace";
            if (desc.Contains("scan") || desc.Contains("diagnostic")) return "Scan";
            if (desc.Contains("calibrat")) return "Sublet";
            if (desc.Contains("measure") || desc.Contains("frame")) return "F";
            if (desc.Contains("refrigerant") || desc.Contains("coolant") || desc.Contains("consumable")) return "Add";

            // Check category
            if (category.Contains("refinish") || op.RefinishHours > 0 && op.LaborHours == 0) return "Refinish";
            if (category == "f" || category == "frame") return "F";
            if (category == "m" || category.Contains("mechanical")) return "M";

            // Default to Rpr for body/repair operations
            return "Rpr";
        }

        #endregion

        #region Tab 1: Unified Search

        private Grid CreateSearchTab()
        {
            var grid = new Grid { Padding = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Search box
            _searchBox = new TextBox
            {
                PlaceholderText = "Search operations, parts, P-Pages, definitions...",
                Padding = new Thickness(12),
                FontSize = 14,
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60))
            };
            _searchBox.TextChanged += SearchBox_TextChanged;
            Grid.SetRow(_searchBox, 0);
            grid.Children.Add(_searchBox);

            // Results
            _resultsPanel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 15, 0, 0) };
            _resultsScroll = new ScrollViewer
            {
                Content = _resultsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(_resultsScroll, 1);
            grid.Children.Add(_resultsScroll);

            // Initial hint
            _resultsPanel.Children.Add(new TextBlock
            {
                Text = "Type to search across all data sources:\n\n" +
                       "• Excel operations (labor hours, prices)\n" +
                       "• Part operations (quarter panel, hood, etc.)\n" +
                       "• Operation justifications (why to add)\n\n" +
                       "Examples: \"corrosion\", \"scan\", \"quarter panel\", \"calibration\"",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });

            return grid;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = _searchBox?.Text?.Trim() ?? "";
            if (query.Length < 2)
            {
                _resultsPanel?.Children.Clear();
                _resultsPanel?.Children.Add(new TextBlock
                {
                    Text = "Type at least 2 characters to search...",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
                });
                return;
            }

            PerformUnifiedSearch(query);
        }

        private void PerformUnifiedSearch(string query)
        {
            _resultsPanel?.Children.Clear();

            var results = _knowledgeService.SearchAll(query);

            // Excel Operations section
            if (results.ExcelOperations.Any())
            {
                _resultsPanel?.Children.Add(CreateSectionHeader($"Excel Operations ({results.ExcelOperations.Count})"));
                foreach (var item in results.ExcelOperations.Take(10))
                {
                    _resultsPanel?.Children.Add(CreateSearchResultCard(item, "Excel"));
                }
            }

            // Parts section
            if (results.Parts.Any())
            {
                _resultsPanel?.Children.Add(CreateSectionHeader($"Parts ({results.Parts.Count})"));
                foreach (var item in results.Parts.Take(10))
                {
                    var card = CreateSearchResultCard(item, "Part");
                    // Add click handler to switch to part lookup
                    if (card is Border border)
                    {
                        border.Tag = item.Title;
                        border.PointerPressed += (s, e) =>
                        {
                            if (s is Border b && b.Tag is string partName)
                            {
                                SwitchToTab(1);
                                if (_partSearchBox != null)
                                {
                                    _partSearchBox.Text = partName;
                                    LoadPartOperations(partName);
                                }
                            }
                        };
                    }
                    _resultsPanel?.Children.Add(card);
                }
            }

            // Justifications section
            if (results.Justifications.Any())
            {
                _resultsPanel?.Children.Add(CreateSectionHeader($"Justifications ({results.Justifications.Count})"));
                foreach (var item in results.Justifications.Take(10))
                {
                    _resultsPanel?.Children.Add(CreateSearchResultCard(item, "Justification"));
                }
            }

            if (!results.ExcelOperations.Any() && !results.Parts.Any() && !results.Justifications.Any())
            {
                _resultsPanel?.Children.Add(new TextBlock
                {
                    Text = $"No results found for \"{query}\"",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 150))
                });
            }
        }

        private TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 130)),
                Margin = new Thickness(0, 10, 0, 5)
            };
        }

        private Border CreateSearchResultCard(SearchResultItem item, string type)
        {
            var panel = new StackPanel { Spacing = 4 };

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = item.Title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(title, 0);
            titleRow.Children.Add(title);

            if (item.LaborHours > 0 || item.Price > 0)
            {
                var values = new TextBlock
                {
                    Text = $"{(item.LaborHours > 0 ? $"{item.LaborHours:F1} hr" : "")} {(item.Price > 0 ? $"${item.Price:N0}" : "")}".Trim(),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                    FontSize = 12
                };
                Grid.SetColumn(values, 1);
                titleRow.Children.Add(values);
            }

            panel.Children.Add(titleRow);

            if (!string.IsNullOrEmpty(item.Details))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = item.Details,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var sourceTag = new TextBlock
            {
                Text = $"{item.Source} • {item.Category}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            };
            panel.Children.Add(sourceTag);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 2, 0, 2),
                Child = panel
            };
        }

        #endregion

        #region Tab 2: Part Lookup

        private Grid CreatePartLookupTab()
        {
            var grid = new Grid { Padding = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Title
            var title = new TextBlock
            {
                Text = "What operations do I need for this part?",
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Part search
            _partSearchBox = new AutoSuggestBox
            {
                PlaceholderText = "Type a part name (quarter panel, hood, door...)",
                QueryIcon = new SymbolIcon(Symbol.Find),
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            _partSearchBox.TextChanged += PartSearchBox_TextChanged;
            _partSearchBox.SuggestionChosen += PartSearchBox_SuggestionChosen;
            Grid.SetRow(_partSearchBox, 1);
            grid.Children.Add(_partSearchBox);

            // Results
            _partResultsPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 15, 0, 0) };
            var scroll = new ScrollViewer
            {
                Content = _partResultsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scroll, 2);
            grid.Children.Add(scroll);

            // Initial content
            ShowPartLookupHint();

            return grid;
        }

        private void ShowPartLookupHint()
        {
            _partResultsPanel?.Children.Clear();

            var hint = new TextBlock
            {
                Text = "Popular parts:\n\n" +
                       "Structural: Quarter Panel, Rocker Panel, A-Pillar, B-Pillar, Roof Panel\n\n" +
                       "Bolted: Front Fender, Hood, Door, Deck Lid, Liftgate\n\n" +
                       "Bumpers: Front Bumper Cover, Rear Bumper Cover\n\n" +
                       "Glass: Windshield, Back Glass\n\n" +
                       "Mechanical: Radiator Support, Condenser",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22
            };
            _partResultsPanel?.Children.Add(hint);
        }

        private void PartSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = sender.Text?.Trim() ?? "";
                if (query.Length >= 2)
                {
                    var suggestions = _knowledgeService.SearchParts(query);
                    sender.ItemsSource = suggestions;
                }
            }
        }

        private void PartSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string partName)
            {
                LoadPartOperations(partName);
            }
        }

        private void LoadPartOperations(string partName)
        {
            _partResultsPanel?.Children.Clear();

            var partOps = _knowledgeService.GetOperationsForPart(partName);

            // Header
            var header = new StackPanel { Spacing = 5 };
            header.Children.Add(new TextBlock
            {
                Text = partOps.PartName,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 130))
            });
            header.Children.Add(new TextBlock
            {
                Text = $"Type: {partOps.PartType} • {partOps.Operations.Count} operations",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            _partResultsPanel?.Children.Add(header);

            // Copy all button
            var copyBtn = new Button
            {
                Content = "Copy All Operations",
                Margin = new Thickness(0, 10, 0, 10),
                Padding = new Thickness(15, 8, 15, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 100, 70))
            };
            copyBtn.Click += (s, e) => CopyPartOperations(partOps);
            _partResultsPanel?.Children.Add(copyBtn);

            // Common operations
            var commonOps = partOps.Operations.Where(o => o.IsCommon).ToList();
            if (commonOps.Any())
            {
                _partResultsPanel?.Children.Add(CreateSectionHeader("Common Operations (Usually Needed)"));
                foreach (var op in commonOps)
                {
                    _partResultsPanel?.Children.Add(CreateOperationCard(op, true));
                }
            }

            // Other operations
            var otherOps = partOps.Operations.Where(o => !o.IsCommon).ToList();
            if (otherOps.Any())
            {
                _partResultsPanel?.Children.Add(CreateSectionHeader("Additional Operations (If Applicable)"));
                foreach (var op in otherOps)
                {
                    _partResultsPanel?.Children.Add(CreateOperationCard(op, false));
                }
            }
        }

        private Border CreateOperationCard(OperationRecommendation op, bool isCommon)
        {
            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftPanel = new StackPanel { Spacing = 2 };

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            nameRow.Children.Add(new TextBlock
            {
                Text = op.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            if (op.IsMaterial)
            {
                nameRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 80, 80, 120)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 2, 5, 2),
                    Child = new TextBlock
                    {
                        Text = "Material",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Colors.White)
                    }
                });
            }
            leftPanel.Children.Add(nameRow);

            if (!string.IsNullOrEmpty(op.Notes))
            {
                leftPanel.Children.Add(new TextBlock
                {
                    Text = op.Notes,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
                });
            }

            // Category tag
            leftPanel.Children.Add(new TextBlock
            {
                Text = op.Category,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 150, 130))
            });

            Grid.SetColumn(leftPanel, 0);
            panel.Children.Add(leftPanel);

            // Labor/Price
            var valuePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            if (op.LaborHours > 0)
            {
                valuePanel.Children.Add(new TextBlock
                {
                    Text = $"{op.LaborHours:F1} hr",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150))
                });
            }
            if (op.TypicalCost > 0)
            {
                valuePanel.Children.Add(new TextBlock
                {
                    Text = $"${op.TypicalCost:N0}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 180, 100))
                });
            }
            Grid.SetColumn(valuePanel, 1);
            panel.Children.Add(valuePanel);

            // Info button for justification
            var infoBtn = new Button
            {
                Content = "?",
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 60)),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = op.Name
            };
            infoBtn.Click += InfoButton_Click;
            Grid.SetColumn(infoBtn, 2);
            panel.Children.Add(infoBtn);

            return new Border
            {
                Background = isCommon
                    ? new SolidColorBrush(Color.FromArgb(255, 40, 55, 45))
                    : new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 2, 0, 2),
                BorderBrush = isCommon
                    ? new SolidColorBrush(Color.FromArgb(255, 60, 100, 70))
                    : null,
                BorderThickness = isCommon ? new Thickness(1) : new Thickness(0),
                Child = panel
            };
        }

        private async void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string opName)
            {
                var justification = _knowledgeService.GetJustification(opName);

                var dialog = new ContentDialog
                {
                    Title = opName,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };

                var content = new StackPanel { Spacing = 10, MaxWidth = 400 };

                if (justification != null)
                {
                    content.Children.Add(CreateJustificationSection("Why Needed", justification.WhyNeeded));
                    if (!string.IsNullOrEmpty(justification.PPageReference))
                        content.Children.Add(CreateJustificationSection("P-Page Reference", justification.PPageReference));
                    if (!string.IsNullOrEmpty(justification.DEGReference))
                        content.Children.Add(CreateJustificationSection("DEG Reference", justification.DEGReference));
                    if (!string.IsNullOrEmpty(justification.OEMReference))
                        content.Children.Add(CreateJustificationSection("OEM Reference", justification.OEMReference));
                }
                else
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = "No specific justification available for this operation.\n\nConsult P-Pages, DEG inquiries, or OEM repair procedures for guidance.",
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                dialog.Content = content;
                await dialog.ShowAsync();
            }
        }

        private StackPanel CreateJustificationSection(string title, string content)
        {
            var panel = new StackPanel { Spacing = 3 };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 130))
            });
            panel.Children.Add(new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
            });
            return panel;
        }

        private void CopyPartOperations(PartOperationSet partOps)
        {
            var lines = new List<string>();
            lines.Add($"Operations for {partOps.PartName} ({partOps.PartType})");
            lines.Add("Operation\tCategory\tLabor\tCost\tNotes");

            // Convert to SmartExportOp for VirtualClipboardService
            var exportOps = partOps.Operations.Where(o => o.IsCommon).Select(op => new SmartExportOp
            {
                OperationType = op.Category.Contains("Refinish") ? "Refinish" : "Rpr",
                Description = op.Name,
                Quantity = 1,
                Price = op.TypicalCost,
                LaborHours = op.LaborHours,
                RefinishHours = 0,
                Category = op.Category
            }).ToList();

            // Update virtual clipboard so Export tab shows accurate summary
            VirtualClipboardService.Instance.SetOperations(exportOps, $"Part Lookup - {partOps.PartName}");

            foreach (var op in partOps.Operations.Where(o => o.IsCommon))
            {
                lines.Add($"{op.Name}\t{op.Category}\t{(op.LaborHours > 0 ? $"{op.LaborHours:F1}" : "")}\t{(op.TypicalCost > 0 ? $"${op.TypicalCost:N0}" : "")}\t{op.Notes}");
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(string.Join("\n", lines));
            Clipboard.SetContent(dataPackage);
        }

        #endregion

        #region Tab 3: Damage Area

        private Grid CreateDamageAreaTab()
        {
            var grid = new Grid { Padding = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Title
            var title = new TextBlock
            {
                Text = "Select damage area to see related parts and operations",
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Damage area combo
            _damageAreaCombo = new ComboBox
            {
                PlaceholderText = "Select damage area...",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40))
            };

            foreach (var area in _knowledgeService.GetDamageAreas())
            {
                _damageAreaCombo.Items.Add(area);
            }
            _damageAreaCombo.SelectionChanged += DamageAreaCombo_SelectionChanged;
            Grid.SetRow(_damageAreaCombo, 1);
            grid.Children.Add(_damageAreaCombo);

            // Results
            _damageResultsPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 15, 0, 0) };
            var scroll = new ScrollViewer
            {
                Content = _damageResultsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scroll, 2);
            grid.Children.Add(scroll);

            return grid;
        }

        private void DamageAreaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_damageAreaCombo?.SelectedItem is string area)
            {
                LoadDamageAreaOperations(area);
            }
        }

        private void LoadDamageAreaOperations(string area)
        {
            _damageResultsPanel?.Children.Clear();

            // Parts affected
            var parts = _knowledgeService.GetPartsForDamageArea(area);
            _damageResultsPanel?.Children.Add(CreateSectionHeader($"Parts Typically Affected ({parts.Count})"));

            var partsWrap = new ItemsWrapGrid();
            var partsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var part in parts)
            {
                var partBtn = new Button
                {
                    Content = part,
                    Padding = new Thickness(10, 5, 10, 5),
                    Background = new SolidColorBrush(Color.FromArgb(255, 50, 70, 60)),
                    Tag = part
                };
                partBtn.Click += (s, e) =>
                {
                    if (s is Button btn && btn.Tag is string partName)
                    {
                        SwitchToTab(1);
                        if (_partSearchBox != null)
                        {
                            _partSearchBox.Text = partName;
                            LoadPartOperations(partName);
                        }
                    }
                };
                partsPanel.Children.Add(partBtn);
            }
            _damageResultsPanel?.Children.Add(new ScrollViewer
            {
                Content = partsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 50
            });

            // Common operations across all parts in this area
            var allOps = _knowledgeService.GetOperationsForDamageArea(area);
            _damageResultsPanel?.Children.Add(CreateSectionHeader($"Common Operations for {area} ({allOps.Count(o => o.IsCommon)})"));

            foreach (var op in allOps.Where(o => o.IsCommon).Take(20))
            {
                _damageResultsPanel?.Children.Add(CreateOperationCard(op, true));
            }
        }

        #endregion

        #region Tab 4: Missing Operations Detector

        private Grid CreateMissingOpsTab()
        {
            var grid = new Grid { Padding = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Title
            var title = new TextBlock
            {
                Text = "Paste estimate lines to check for missing operations",
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Estimate input
            _estimateInput = new TextBox
            {
                PlaceholderText = "Paste estimate operation lines here...\n\nExample:\nR&R Quarter Panel\nR&R Rear Bumper Cover\nR&R Windshield",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            Grid.SetRow(_estimateInput, 1);
            grid.Children.Add(_estimateInput);

            // Analyze button
            var analyzeBtn = new Button
            {
                Content = "Analyze for Missing Operations",
                Margin = new Thickness(0, 10, 0, 10),
                Padding = new Thickness(15, 10, 15, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 100, 80)),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            analyzeBtn.Click += AnalyzeButton_Click;
            Grid.SetRow(analyzeBtn, 2);
            grid.Children.Add(analyzeBtn);

            // Results
            _missingOpsPanel = new StackPanel { Spacing = 8 };
            var scroll = new ScrollViewer
            {
                Content = _missingOpsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scroll, 3);
            grid.Children.Add(scroll);

            return grid;
        }

        private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            var estimateText = _estimateInput?.Text ?? "";
            var lines = estimateText.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

            if (lines.Count == 0)
            {
                _missingOpsPanel?.Children.Clear();
                _missingOpsPanel?.Children.Add(new TextBlock
                {
                    Text = "Please paste some estimate lines to analyze.",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 150))
                });
                return;
            }

            var suggestions = _knowledgeService.DetectMissingOperations(lines);
            DisplayMissingSuggestions(suggestions);
        }

        private void DisplayMissingSuggestions(List<MissingOperationSuggestion> suggestions)
        {
            _missingOpsPanel?.Children.Clear();

            if (!suggestions.Any())
            {
                _missingOpsPanel?.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 40, 60, 45)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(15),
                    Child = new TextBlock
                    {
                        Text = "No obvious missing operations detected.\n\nNote: This is a basic check. Always verify against P-Pages, DEG, and OEM procedures.",
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 220, 150)),
                        TextWrapping = TextWrapping.Wrap
                    }
                });
                return;
            }

            _missingOpsPanel?.Children.Add(CreateSectionHeader($"Potentially Missing Operations ({suggestions.Count})"));

            // High priority
            var highPriority = suggestions.Where(s => s.Priority == "High").ToList();
            if (highPriority.Any())
            {
                _missingOpsPanel?.Children.Add(new TextBlock
                {
                    Text = "HIGH PRIORITY",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 100, 100)),
                    Margin = new Thickness(0, 10, 0, 5)
                });
                foreach (var sug in highPriority)
                {
                    _missingOpsPanel?.Children.Add(CreateMissingSuggestionCard(sug));
                }
            }

            // Medium priority
            var mediumPriority = suggestions.Where(s => s.Priority == "Medium").ToList();
            if (mediumPriority.Any())
            {
                _missingOpsPanel?.Children.Add(new TextBlock
                {
                    Text = "MEDIUM PRIORITY",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 180, 100)),
                    Margin = new Thickness(0, 10, 0, 5)
                });
                foreach (var sug in mediumPriority)
                {
                    _missingOpsPanel?.Children.Add(CreateMissingSuggestionCard(sug));
                }
            }
        }

        private Border CreateMissingSuggestionCard(MissingOperationSuggestion sug)
        {
            var panel = new StackPanel { Spacing = 5 };

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            panel.Children.Add(new TextBlock
            {
                Text = sug.Operation,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White)
            });

            panel.Children.Add(new TextBlock
            {
                Text = sug.Reason,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                TextWrapping = TextWrapping.Wrap
            });

            if (sug.Justification != null && !string.IsNullOrEmpty(sug.Justification.PPageReference))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"P-Page: {sug.Justification.PPageReference}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 130)),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return new Border
            {
                Background = sug.Priority == "High"
                    ? new SolidColorBrush(Color.FromArgb(255, 60, 40, 40))
                    : new SolidColorBrush(Color.FromArgb(255, 55, 50, 40)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 2, 0, 2),
                BorderBrush = sug.Priority == "High"
                    ? new SolidColorBrush(Color.FromArgb(255, 150, 80, 80))
                    : new SolidColorBrush(Color.FromArgb(255, 150, 130, 80)),
                BorderThickness = new Thickness(1),
                Child = panel
            };
        }

        #endregion
    }

    /// <summary>
    /// Represents an option within a sheet/tab category
    /// </summary>
    public class SheetOption
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public bool IsDefault { get; set; }

        public SheetOption(string name, string category, decimal laborHours, decimal price, bool isDefault, bool isRefinish = false)
        {
            Name = name;
            Category = category;
            if (isRefinish)
            {
                RefinishHours = laborHours > 0 ? laborHours : price; // Use price param for refinish hours
                LaborHours = 0;
                Price = 0;
            }
            else
            {
                LaborHours = laborHours;
                RefinishHours = 0;
                Price = price;
            }
            IsDefault = isDefault;
        }
    }
}
