#nullable enable
using Microsoft.UI;
using McStudDesktop.Models;
using Windows.UI;

namespace McStudDesktop.Views;

public class EstimatingToolView : Grid
{
    private Estimate _currentEstimate;
    private TabView? _tabView;
    private TextBlock? _totalsSummary;

    // Current tab state
    private string _currentTab = "SOP List";
    private string _currentSection = "Electrical";

    // Main layout grids
    private Grid? _sectionButtonsGrid;
    private Grid? _inputsGrid;
    private Grid? _operationsGrid;

    // Initialization flag
    private bool _controlsInitialized = false;

    // ============================================================
    // SOP LIST CONTROL REFERENCES
    // ============================================================
    // Electrical section
    private ComboBox? _sopBatteryTypeCombo;
    private CheckBox? _sopTestBatteryCheck;
    private CheckBox? _sopBatterySupportCheck;
    private ComboBox? _sopVehicleTypeCombo;
    private CheckBox? _sopAdasCheck;

    // Diagnostics section
    private ComboBox? _sopScanToolCombo;
    private CheckBox? _sopSetupScanToolCheck;
    private CheckBox? _sopAdasDiagnosticCheck;
    private CheckBox? _sopSimulateFluidsCheck;
    private CheckBox? _sopTirePressureCheck;
    private CheckBox? _sopRemoveItemsCheck;
    private CheckBox? _sopDriveCycleCheck;
    private CheckBox? _sopGatewayCheck;

    // Misc section
    private TextBox? _sopCustomPriceBox;
    private TextBox? _sopCustomLaborBox;

    // ============================================================
    // PART OPERATIONS CONTROL REFERENCES
    // ============================================================
    private TextBox? _partNameBox;
    private ComboBox? _partSizeCombo;
    private CheckBox? _partAdhesionCheck;

    // ============================================================
    // COVER CAR CONTROL REFERENCES
    // ============================================================
    private ComboBox? _coverLaborTypeCombo;
    private CheckBox? _coverTwoToneCheck;

    // ============================================================
    // BODY OPERATIONS CONTROL REFERENCES
    // ============================================================
    private CheckBox? _bodyCollisionAccessCheck;

    // ============================================================
    // REFINISH OPERATIONS CONTROL REFERENCES
    // ============================================================
    private ComboBox? _refinishPaintStageCombo;
    private CheckBox? _refinishRadarFormulaCheck;
    private CheckBox? _refinishAdditionalCheck;

    // ============================================================
    // MECHANICAL OPERATIONS CONTROL REFERENCES
    // ============================================================
    private ComboBox? _mechRefrigerantCombo;
    private CheckBox? _mechCoverACLinesCheck;

    // ============================================================
    // SRS OPERATIONS CONTROL REFERENCES
    // ============================================================
    private CheckBox? _srsSafetyInspectionCheck;

    // ============================================================
    // TOTAL LOSS CONTROL REFERENCES
    // ============================================================
    private TextBox? _totalLossAdminFeeBox;
    private TextBox? _totalLossCoordinationBox;

    // ============================================================
    // BODY ON FRAME CONTROL REFERENCES
    // ============================================================
    private CheckBox? _bodyOnFrameDisposalCheck;

    // ============================================================
    // STOLEN RECOVERY CONTROL REFERENCES
    // ============================================================
    private CheckBox? _stolenRecoveryInspectionCheck;

    public event EventHandler? BackToMenu;

    public EstimatingToolView()
    {
        _currentEstimate = new Estimate();
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        Background = new SolidColorBrush(Color.FromArgb(255, 10, 10, 10));

        // Initialize controls FIRST, so they are ready when CreateTabView/RenderCurrentTab is called
        InitializeAllControls();

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) }); // Header
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) }); // Footer

        CreateHeader();
        CreateTabView();
        CreateFooter();

        SubscribeToEstimateChanges();
    }

    private void InitializeAllControls()
    {
        if (_controlsInitialized) return;
        _controlsInitialized = true;

        InitializeSOPControls();
        InitializePartControls();
        InitializeCoverCarControls();
        InitializeBodyControls();
        InitializeRefinishControls();
        InitializeMechanicalControls();
        InitializeSRSControls();
        InitializeTotalLossControls();
        InitializeBodyOnFrameControls();
        InitializeStolenRecoveryControls();
    }

    private void InitializePartControls()
    {
        _partNameBox = CreateTextBox("Enter part name");
        _partNameBox.TextChanged += (s, e) => { UpdatePartOperations(); RenderPartOperations(); };

        _partSizeCombo = CreateComboBox(new[] { "", "First Large Part", "Additional Large Part", "Additional Small Part" });
        _partSizeCombo.SelectionChanged += (s, e) => { UpdatePartOperations(); RenderPartOperations(); };

        _partAdhesionCheck = CreateCheckBox("Adhesion Promoter");
        _partAdhesionCheck.Checked += (s, e) => { UpdatePartOperations(); RenderPartOperations(); };
        _partAdhesionCheck.Unchecked += (s, e) => { UpdatePartOperations(); RenderPartOperations(); };
    }

    private void InitializeCoverCarControls()
    {
        _coverLaborTypeCombo = CreateComboBox(new[] { "", "Refinish Labor", "$ and Body Labor" });
        _coverLaborTypeCombo.SelectionChanged += (s, e) => { UpdateCoverCarOperations(); RenderCoverCarOperations(); };

        _coverTwoToneCheck = CreateCheckBox("Two-Tone Paint");
        _coverTwoToneCheck.Checked += (s, e) => { UpdateCoverCarOperations(); RenderCoverCarOperations(); };
        _coverTwoToneCheck.Unchecked += (s, e) => { UpdateCoverCarOperations(); RenderCoverCarOperations(); };
    }

    private void InitializeBodyControls()
    {
        _bodyCollisionAccessCheck = CreateCheckBox("Collision Damage Access");
        _bodyCollisionAccessCheck.Checked += (s, e) => { UpdateBodyOperations(); RenderBodyOperations(); };
        _bodyCollisionAccessCheck.Unchecked += (s, e) => { UpdateBodyOperations(); RenderBodyOperations(); };
    }

    private void InitializeRefinishControls()
    {
        _refinishPaintStageCombo = CreateComboBox(new[] { "", "2-Stage", "3-Stage", "4-Stage" });
        _refinishPaintStageCombo.SelectionChanged += (s, e) => { UpdateRefinishOperations(); RenderRefinishOperations(); };

        _refinishRadarFormulaCheck = CreateCheckBox("Radar Formula Color Tint");
        _refinishRadarFormulaCheck.Checked += (s, e) => { UpdateRefinishOperations(); RenderRefinishOperations(); };
        _refinishRadarFormulaCheck.Unchecked += (s, e) => { UpdateRefinishOperations(); RenderRefinishOperations(); };
    }

    private void InitializeMechanicalControls()
    {
        _mechRefrigerantCombo = CreateComboBox(new[] { "", "R134a", "R1234yf", "R744" });
        _mechRefrigerantCombo.SelectionChanged += (s, e) => { UpdateMechanicalOperations(); RenderMechanicalOperations(); };

        _mechCoverACLinesCheck = CreateCheckBox("Cover and Protect AC Lines");
        _mechCoverACLinesCheck.Checked += (s, e) => { UpdateMechanicalOperations(); RenderMechanicalOperations(); };
        _mechCoverACLinesCheck.Unchecked += (s, e) => { UpdateMechanicalOperations(); RenderMechanicalOperations(); };
    }

    private void InitializeSRSControls()
    {
        _srsSafetyInspectionCheck = CreateCheckBox("Safety Inspections");
        _srsSafetyInspectionCheck.Checked += (s, e) => { UpdateSRSOperations(); RenderSRSOperations(); };
        _srsSafetyInspectionCheck.Unchecked += (s, e) => { UpdateSRSOperations(); RenderSRSOperations(); };
    }

    private void InitializeTotalLossControls()
    {
        _totalLossAdminFeeBox = CreateTextBox("Enter fee amount");
        _totalLossAdminFeeBox.TextChanged += (s, e) => { UpdateTotalLossOperations(); RenderTotalLossOperations(); };

        _totalLossCoordinationBox = CreateTextBox("Enter charge amount");
        _totalLossCoordinationBox.TextChanged += (s, e) => { UpdateTotalLossOperations(); RenderTotalLossOperations(); };
    }

    private void InitializeBodyOnFrameControls()
    {
        _bodyOnFrameDisposalCheck = CreateCheckBox("Frame Disposal");
        _bodyOnFrameDisposalCheck.Checked += (s, e) => { UpdateBodyOnFrameOperations(); RenderBodyOnFrameOperations(); };
        _bodyOnFrameDisposalCheck.Unchecked += (s, e) => { UpdateBodyOnFrameOperations(); RenderBodyOnFrameOperations(); };
    }

    private void InitializeStolenRecoveryControls()
    {
        _stolenRecoveryInspectionCheck = CreateCheckBox("Vehicle Inspection");
        _stolenRecoveryInspectionCheck.Checked += (s, e) => { UpdateStolenRecoveryOperations(); RenderStolenRecoveryOperations(); };
        _stolenRecoveryInspectionCheck.Unchecked += (s, e) => { UpdateStolenRecoveryOperations(); RenderStolenRecoveryOperations(); };
    }

    private void CreateHeader()
    {
        var headerGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            Padding = new Thickness(20, 10, 20, 10)
        };

        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var backButton = new Button
        {
            Content = "← Back",
            Height = 40,
            Width = 100,
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        backButton.Click += (s, e) => BackToMenu?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(backButton, 0);

        var titleStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var titleText = new TextBlock
        {
            Text = "McStud Estimating Tool (MET)",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        };

        var subtitleText = new TextBlock
        {
            Text = "Auto Body Shop Estimating System",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        titleStack.Children.Add(titleText);
        titleStack.Children.Add(subtitleText);
        Grid.SetColumn(titleStack, 1);

        var exportButton = new Button
        {
            Content = "📤 Export",
            Height = 40,
            Width = 110,
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        Grid.SetColumn(exportButton, 2);

        headerGrid.Children.Add(backButton);
        headerGrid.Children.Add(titleStack);
        headerGrid.Children.Add(exportButton);

        Grid.SetRow(headerGrid, 0);
        Children.Add(headerGrid);
    }

    private void CreateTabView()
    {
        _tabView = new TabView
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 15, 15, 15)),
            Margin = new Thickness(10, 0, 10, 0)
        };

        string[] tabs = {
            "Import Estimate", "SOP List", "Part Operations", "Cover Car", "Body",
            "Refinish", "Mechanical", "SRS", "Total Loss", "Body on Frame",
            "Stolen Recovery", "Post Repair", "Summary"
        };

        foreach (var tabName in tabs)
        {
            var tabItem = new TabViewItem
            {
                Header = tabName,
                IconSource = new SymbolIconSource { Symbol = Symbol.Document }
            };

            _tabView.TabItems.Add(tabItem);
        }

        _tabView.SelectedIndex = 1; // Start with SOP List
        _tabView.SelectionChanged += TabView_SelectionChanged;

        Grid.SetRow(_tabView, 1);
        Children.Add(_tabView);

        // Render the initial tab now that controls are initialized
        RenderCurrentTab();
    }

    private void TabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tabView?.SelectedItem is TabViewItem selectedTab)
        {
            _currentTab = selectedTab.Header?.ToString() ?? "";

            // Set default section for each tab
            _currentSection = _currentTab switch
            {
                "SOP List" => "Electrical",
                "Part Operations" => "Blend Operations",
                "Cover Car" => "Masking",
                "Body" => "Structural",
                "Refinish" => "Paint",
                "Mechanical" => "AC & Cooling",
                "SRS" => "Safety",
                "Total Loss" => "Fees",
                "Body on Frame" => "Frame",
                "Stolen Recovery" => "Recovery",
                _ => ""
            };

            RenderCurrentTab();
        }
    }

    private void CreateFooter()
    {
        var footerGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            Padding = new Thickness(20, 10, 20, 10)
        };

        _totalsSummary = new TextBlock
        {
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        UpdateTotalsSummary();

        footerGrid.Children.Add(_totalsSummary);
        Grid.SetRow(footerGrid, 2);
        Children.Add(footerGrid);
    }

    private void SubscribeToEstimateChanges()
    {
        _currentEstimate.PropertyChanged += (s, e) => UpdateTotalsSummary();
    }

    private void UpdateTotalsSummary()
    {
        if (_totalsSummary != null)
        {
            _totalsSummary.Text = $"📊 {_currentEstimate.TotalOperationsCount} Operations  |  " +
                                 $"💲 ${_currentEstimate.TotalPrice:F2}  |  " +
                                 $"🛠 {_currentEstimate.TotalLaborHours:F1} Labor Hrs  |  " +
                                 $"🎨 {_currentEstimate.TotalRefinishHours:F1} Refinish Hrs";
        }
    }

    private void RenderCurrentTab()
    {
        if (_tabView?.SelectedItem is not TabViewItem selectedTab) return;

        var contentGrid = new Grid
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1)
        };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); // Section buttons
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 400 }); // Inputs
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 500 }); // Operations

        _sectionButtonsGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)),
            Padding = new Thickness(10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1)
        };
        Grid.SetColumn(_sectionButtonsGrid, 0);

        var inputScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(15),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1)
        };
        _inputsGrid = new Grid
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1)
        };
        inputScrollViewer.Content = _inputsGrid;
        Grid.SetColumn(inputScrollViewer, 1);

        var operationsScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(15),
            Background = new SolidColorBrush(Color.FromArgb(255, 12, 12, 12)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1)
        };
        _operationsGrid = new Grid
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1)
        };
        operationsScrollViewer.Content = _operationsGrid;
        Grid.SetColumn(operationsScrollViewer, 2);

        contentGrid.Children.Add(_sectionButtonsGrid);
        contentGrid.Children.Add(inputScrollViewer);
        contentGrid.Children.Add(operationsScrollViewer);

        selectedTab.Content = contentGrid;

        // Render appropriate tab content
        switch (_currentTab)
        {
            case "Import Estimate": RenderImportEstimateTab(); break;
            case "SOP List": RenderSOPListTab(); break;
            case "Part Operations": RenderPartOperationsTab(); break;
            case "Cover Car": RenderCoverCarTab(); break;
            case "Body": RenderBodyTab(); break;
            case "Refinish": RenderRefinishTab(); break;
            case "Mechanical": RenderMechanicalTab(); break;
            case "SRS": RenderSRSTab(); break;
            case "Total Loss": RenderTotalLossTab(); break;
            case "Body on Frame": RenderBodyOnFrameTab(); break;
            case "Stolen Recovery": RenderStolenRecoveryTab(); break;
            case "Post Repair": RenderPostRepairTab(); break;
            case "Summary": RenderSummaryTab(); break;
        }
    }

    // ============================================================
    // IMPORT ESTIMATE TAB
    // ============================================================
    private void RenderImportEstimateTab()
    {
        var inputStack = new StackPanel { Spacing = 20 };
        inputStack.Children.Add(CreateSectionTitle("Import Estimate from PDF"));
        inputStack.Children.Add(new TextBlock
        {
            Text = "PDF import functionality coming soon...",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            FontSize = 14
        });

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }
    }

    // ============================================================
    // SOP LIST TAB
    // ============================================================
    private void RenderSOPListTab()
    {
        RenderSectionButtons(new[] { "Electrical", "Vehicle Diagnostics", "Misc" });

        var inputStack = new StackPanel { Spacing = 15 };

        if (_currentSection == "Electrical")
            RenderSOPElectricalInputs(inputStack);
        else if (_currentSection == "Vehicle Diagnostics")
            RenderSOPDiagnosticsInputs(inputStack);
        else if (_currentSection == "Misc")
            RenderSOPMiscInputs(inputStack);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear(); // Clear previous content first!
            _inputsGrid.Children.Add(inputStack);
        }

        RenderSOPOperations();
    }

    private void InitializeSOPControls()
    {
        // Electrical controls
        _sopBatteryTypeCombo = CreateComboBox(new[] { "", "Single", "Dual" });
        _sopBatteryTypeCombo.SelectionChanged += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopTestBatteryCheck = CreateCheckBox("Test Battery Condition");
        _sopTestBatteryCheck.Checked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
        _sopTestBatteryCheck.Unchecked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopBatterySupportCheck = CreateCheckBox("Battery Support");
        _sopBatterySupportCheck.Checked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
        _sopBatterySupportCheck.Unchecked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopVehicleTypeCombo = CreateComboBox(new[] { "", "Gas", "Hybrid", "EV" });
        _sopVehicleTypeCombo.SelectionChanged += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopAdasCheck = CreateCheckBox("ADAS System Present");
        _sopAdasCheck.Checked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
        _sopAdasCheck.Unchecked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        // Diagnostics controls
        _sopScanToolCombo = CreateComboBox(new[] { "", "Gas", "Rivian", "Tesla" });
        _sopScanToolCombo.SelectionChanged += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopSetupScanToolCheck = CreateCheckBox("Setup Scan Tool");
        _sopSetupScanToolCheck.Checked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
        _sopSetupScanToolCheck.Unchecked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopAdasDiagnosticCheck = CreateCheckBox("ADAS Diagnostic Report");
        _sopAdasDiagnosticCheck.Checked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
        _sopAdasDiagnosticCheck.Unchecked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopSimulateFluidsCheck = CreateCheckBox("Simulate Full Fluids");
        _sopSimulateFluidsCheck.Checked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
        _sopSimulateFluidsCheck.Unchecked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopTirePressureCheck = CreateCheckBox("Check Tire Pressure");
        _sopTirePressureCheck.Checked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
        _sopTirePressureCheck.Unchecked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopRemoveItemsCheck = CreateCheckBox("Remove Customer Items");
        _sopRemoveItemsCheck.Checked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
        _sopRemoveItemsCheck.Unchecked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopDriveCycleCheck = CreateCheckBox("Drive Cycle Verification");
        _sopDriveCycleCheck.Checked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
        _sopDriveCycleCheck.Unchecked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopGatewayCheck = CreateCheckBox("Gateway Unlock");
        _sopGatewayCheck.Checked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
        _sopGatewayCheck.Unchecked += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        // Misc controls
        _sopCustomPriceBox = CreateTextBox("Enter price (min $50)");
        _sopCustomPriceBox.TextChanged += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };

        _sopCustomLaborBox = CreateTextBox("Enter hours (min 1.0)");
        _sopCustomLaborBox.TextChanged += (s, e) => { UpdateSOPOperations(); RenderSOPOperations(); };
    }

    private void RenderSOPElectricalInputs(StackPanel stack)
    {
        stack.Children.Add(CreateSectionTitle("Electrical Operations"));

        stack.Children.Add(CreateLabel("12V Battery Type:"));
        AddControl(stack, _sopBatteryTypeCombo);

        AddControl(stack, _sopTestBatteryCheck);
        AddControl(stack, _sopBatterySupportCheck);

        stack.Children.Add(CreateSeparator());

        stack.Children.Add(CreateLabel("Vehicle Type:"));
        AddControl(stack, _sopVehicleTypeCombo);

        AddControl(stack, _sopAdasCheck);
    }

    private void RenderSOPDiagnosticsInputs(StackPanel stack)
    {
        stack.Children.Add(CreateSectionTitle("Vehicle Diagnostics"));

        stack.Children.Add(CreateLabel("Scan Tool Type:"));
        AddControl(stack, _sopScanToolCombo);

        AddControl(stack, _sopSetupScanToolCheck);
        AddControl(stack, _sopAdasDiagnosticCheck);

        stack.Children.Add(CreateSeparator());
        stack.Children.Add(CreateLabel("ADAS Preparations:"));

        AddControl(stack, _sopSimulateFluidsCheck);
        AddControl(stack, _sopTirePressureCheck);
        AddControl(stack, _sopRemoveItemsCheck);

        stack.Children.Add(CreateSeparator());

        AddControl(stack, _sopDriveCycleCheck);
        AddControl(stack, _sopGatewayCheck);
    }

    private void RenderSOPMiscInputs(StackPanel stack)
    {
        stack.Children.Add(CreateSectionTitle("Misc Operations"));

        stack.Children.Add(CreateLabel("Custom Price Operation ($):"));
        AddControl(stack, _sopCustomPriceBox);

        stack.Children.Add(CreateLabel("Custom Labor Hours:"));
        AddControl(stack, _sopCustomLaborBox);
    }

    private void UpdateSOPOperations()
    {
        _currentEstimate.SOPOperations.Clear();

        var batteryType = (_sopBatteryTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var testBattery = _sopTestBatteryCheck?.IsChecked == true;
        var batterySupport = _sopBatterySupportCheck?.IsChecked == true;
        var vehicleType = (_sopVehicleTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var hasAdas = _sopAdasCheck?.IsChecked == true;
        var scanTool = (_sopScanToolCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var setupScanTool = _sopSetupScanToolCheck?.IsChecked == true;
        var adasDiagnostic = _sopAdasDiagnosticCheck?.IsChecked == true;
        var simulateFluids = _sopSimulateFluidsCheck?.IsChecked == true;
        var tirePressure = _sopTirePressureCheck?.IsChecked == true;
        var removeItems = _sopRemoveItemsCheck?.IsChecked == true;
        var driveCycle = _sopDriveCycleCheck?.IsChecked == true;
        var gateway = _sopGatewayCheck?.IsChecked == true;

        // Disconnect and Reconnect Battery
        if (batteryType == "Single")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Disconnect and Reconnect Battery",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.4m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }
        else if (batteryType == "Dual")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Disconnect and Reconnect 2x Battery",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.8m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // Test Battery Condition
        if (testBattery)
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Test Battery Condition",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.2m,
                RefinishHours = 0,
                Category = "SOP"
            });

            // Electronic Reset (comes with test battery)
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Electronic Reset",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "SOP"
            });

            // Cover and Protect Electrical Connections
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Cover and Protect Electrical Connections",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 5,
                LaborHours = 0.3m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // Battery Support
        if (batterySupport)
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Battery Support",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.2m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // Charge and Maintain Battery
        if (vehicleType == "EV")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Charge and Maintain Battery",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.6m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }
        else if (hasAdas && (vehicleType == "Gas" || vehicleType == "Hybrid"))
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Charge and Maintain Battery during ADAS",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.6m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // Mobile Cart for EV/Hybrid
        if (vehicleType == "Hybrid")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Mobile Cart for Hybrid",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = 50,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "SOP"
            });

            // Verify No High Voltage
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Verify No High Voltage",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.2m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }
        else if (vehicleType == "EV")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Mobile Cart for EV",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = 50,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "SOP"
            });

            // Verify No High Voltage
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Verify No High Voltage",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.2m,
                RefinishHours = 0,
                Category = "SOP"
            });

            // Service Mode
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Service Mode",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.1m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // Pre-Scan
        if (scanTool == "Gas")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Pre-Scan",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 150,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "SOP"
            });
        }
        else if (scanTool == "Rivian")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Pre-Scan",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 1.0m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }
        else if (scanTool == "Tesla")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Trim to Access Scanner",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.2m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // In-Process Scan
        if (scanTool == "Gas")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "In-Process Scan",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 150,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "SOP"
            });
        }
        else if (scanTool == "Rivian")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "In-Process Scan",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 1.0m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }
        else if (scanTool == "Tesla")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Tesla Toolbox Scan",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 1.0m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // Post Scan
        if (scanTool == "Gas")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Post Scan",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 150,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "SOP"
            });
        }
        else if (scanTool == "Rivian")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Post Scan",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 1.0m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }
        else if (scanTool == "Tesla")
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Tesla Software Script Programming",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // Setup Scan Tool
        if (setupScanTool)
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Setup Scan Tool",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.2m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // ADAS Preparations (only if ADAS diagnostic is enabled)
        if (adasDiagnostic)
        {
            if (simulateFluids)
            {
                _currentEstimate.SOPOperations.Add(new Operation
                {
                    Description = "Simulate Full Fluids for ADAS Calibrations",
                    OperationType = OperationType.Repair,
                    Quantity = 1,
                    Price = 0,
                    LaborHours = 0.2m,
                    RefinishHours = 0,
                    Category = "SOP"
                });
            }

            if (tirePressure)
            {
                _currentEstimate.SOPOperations.Add(new Operation
                {
                    Description = "Check and Adjust Tire Pressure for ADAS Calibrations",
                    OperationType = OperationType.Repair,
                    Quantity = 1,
                    Price = 0,
                    LaborHours = 0.2m,
                    RefinishHours = 0,
                    Category = "SOP"
                });
            }

            if (removeItems)
            {
                _currentEstimate.SOPOperations.Add(new Operation
                {
                    Description = "Remove Customer Belongings for ADAS Calibrations",
                    OperationType = OperationType.Repair,
                    Quantity = 1,
                    Price = 0,
                    LaborHours = 0.2m,
                    RefinishHours = 0,
                    Category = "SOP"
                });
            }
        }

        // Drive Cycle
        if (driveCycle)
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Drive Cycle Operational Verification",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.7m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // Gateway Unlock
        if (gateway)
        {
            _currentEstimate.SOPOperations.Add(new Operation
            {
                Description = "Gateway (Unlock)",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.1m,
                RefinishHours = 0,
                Category = "SOP"
            });
        }

        // Custom Price Operation
        if (_sopCustomPriceBox != null && !string.IsNullOrWhiteSpace(_sopCustomPriceBox.Text))
        {
            if (decimal.TryParse(_sopCustomPriceBox.Text, out decimal customPrice))
            {
                var finalPrice = Math.Max(customPrice, 50); // Minimum $50
                _currentEstimate.SOPOperations.Add(new Operation
                {
                    Description = "Custom Price Operation",
                    OperationType = OperationType.Replace,
                    Quantity = 1,
                    Price = finalPrice,
                    LaborHours = 0,
                    RefinishHours = 0,
                    Category = "SOP"
                });
            }
        }

        // Custom Labor Operation
        if (_sopCustomLaborBox != null && !string.IsNullOrWhiteSpace(_sopCustomLaborBox.Text))
        {
            if (decimal.TryParse(_sopCustomLaborBox.Text, out decimal customLabor))
            {
                var finalLabor = Math.Max(customLabor, 1.0m); // Minimum 1.0 hour
                _currentEstimate.SOPOperations.Add(new Operation
                {
                    Description = "Custom Labor Operation",
                    OperationType = OperationType.Repair,
                    Quantity = 1,
                    Price = 0,
                    LaborHours = finalLabor,
                    RefinishHours = 0,
                    Category = "SOP"
                });
            }
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderSOPOperations()
    {
        if (_operationsGrid == null) return;

        // Make sure we can modify the grid
        try
        {
            _operationsGrid.Children.Clear();
        }
        catch
        {
            return; // Grid not ready yet
        }

        var operationsStack = new StackPanel { Spacing = 10 };

        operationsStack.Children.Add(CreateSectionTitle("Operations List"));

        if (_currentEstimate.SOPOperations.Count == 0)
        {
            operationsStack.Children.Add(new TextBlock
            {
                Text = "No operations selected. Use the inputs on the left to add operations.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var op in _currentEstimate.SOPOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreateOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    // ============================================================
    // PART OPERATIONS TAB
    // ============================================================
    private void RenderPartOperationsTab()
    {
        RenderSectionButtons(new[] { "Blend Operations", "Repair Operations", "R&I Operations" });

        var inputStack = new StackPanel { Spacing = 15 };
        inputStack.Children.Add(CreateSectionTitle("Part Operations"));

        inputStack.Children.Add(CreateLabel("Part Name:"));
        AddControl(inputStack, _partNameBox);

        inputStack.Children.Add(CreateLabel("Part Size:"));
        AddControl(inputStack, _partSizeCombo);

        AddControl(inputStack, _partAdhesionCheck);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderPartOperations();
    }

    private void UpdatePartOperations()
    {
        _currentEstimate.PartOperations.Clear();

        var partName = _partNameBox?.Text ?? "";
        var partSize = (_partSizeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var adhesion = _partAdhesionCheck?.IsChecked == true;

        // Adhesion Promoter
        if (adhesion && !string.IsNullOrWhiteSpace(partName))
        {
            decimal refinishHours = 0;
            if (partSize == "First Large Part" || partSize == "Additional Large Part")
                refinishHours = 0.3m;
            else if (partSize == "Additional Small Part")
                refinishHours = 0.2m;

            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Adhesion Promoter",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = refinishHours,
                Category = "Part"
            });
        }

        // Part-based pricing
        if (!string.IsNullOrWhiteSpace(partSize) && !string.IsNullOrWhiteSpace(partName))
        {
            decimal price = partSize switch
            {
                "First Large Part" => 15,
                "Additional Large Part" => 10,
                "Additional Small Part" => 5,
                _ => 15
            };

            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} ({partSize})",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = price,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "Part"
            });
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderPartOperations()
    {
        if (_operationsGrid == null) return;

        _operationsGrid.Children.Clear();

        var operationsStack = new StackPanel { Spacing = 10 };
        operationsStack.Children.Add(CreateSectionTitle("Part Operations List"));

        if (_currentEstimate.PartOperations.Count == 0)
        {
            operationsStack.Children.Add(new TextBlock
            {
                Text = "No operations selected. Use the inputs on the left to add operations.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var op in _currentEstimate.PartOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreateOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    // ============================================================
    // COVER CAR TAB
    // ============================================================
    private void RenderCoverCarTab()
    {
        RenderSectionButtons(new[] { "Masking", "Protection" });

        var inputStack = new StackPanel { Spacing = 15 };
        inputStack.Children.Add(CreateSectionTitle("Cover Car Operations"));

        inputStack.Children.Add(CreateLabel("Labor Type:"));
        AddControl(inputStack, _coverLaborTypeCombo);

        AddControl(inputStack, _coverTwoToneCheck);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderCoverCarOperations();
    }

    private void UpdateCoverCarOperations()
    {
        _currentEstimate.CoverCarOperations.Clear();

        var laborType = (_coverLaborTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var twoTone = _coverTwoToneCheck?.IsChecked == true;

        if (!string.IsNullOrWhiteSpace(laborType))
        {
            var description = twoTone ? "Cover Car for Overspray for Two Tone Paint" : "Cover Car for Overspray";
            var opType = laborType == "Refinish Labor" ? OperationType.Refinish : OperationType.Replace;

            decimal price = 0;
            decimal laborHours = 0;
            decimal refinishHours = 0;

            if (laborType == "$ and Body Labor")
            {
                price = twoTone ? 10 : 5;
                laborHours = twoTone ? 0.4m : 0.2m;
            }
            else if (laborType == "Refinish Labor")
            {
                refinishHours = twoTone ? 0.4m : 0.2m;
            }

            _currentEstimate.CoverCarOperations.Add(new Operation
            {
                Description = description,
                OperationType = opType,
                Quantity = 1,
                Price = price,
                LaborHours = laborHours,
                RefinishHours = refinishHours,
                Category = "Cover"
            });
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderCoverCarOperations()
    {
        if (_operationsGrid == null) return;

        _operationsGrid.Children.Clear();

        var operationsStack = new StackPanel { Spacing = 10 };
        operationsStack.Children.Add(CreateSectionTitle("Cover Car Operations List"));

        if (_currentEstimate.CoverCarOperations.Count == 0)
        {
            operationsStack.Children.Add(new TextBlock
            {
                Text = "No operations selected. Use the inputs on the left to add operations.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var op in _currentEstimate.CoverCarOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreateOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    // ============================================================
    // BODY TAB
    // ============================================================
    private void RenderBodyTab()
    {
        RenderSectionButtons(new[] { "Structural", "Equipment" });

        var inputStack = new StackPanel { Spacing = 15 };
        inputStack.Children.Add(CreateSectionTitle("Body Operations"));

        AddControl(inputStack, _bodyCollisionAccessCheck);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderBodyOperations();
    }

    private void UpdateBodyOperations()
    {
        _currentEstimate.BodyOperations.Clear();

        var collisionAccess = _bodyCollisionAccessCheck?.IsChecked == true;

        if (collisionAccess)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Collision Damage Access",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderBodyOperations()
    {
        if (_operationsGrid == null) return;

        _operationsGrid.Children.Clear();

        var operationsStack = new StackPanel { Spacing = 10 };
        operationsStack.Children.Add(CreateSectionTitle("Body Operations List"));

        if (_currentEstimate.BodyOperations.Count == 0)
        {
            operationsStack.Children.Add(new TextBlock
            {
                Text = "No operations selected. Use the inputs on the left to add operations.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var op in _currentEstimate.BodyOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreateOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    // ============================================================
    // REFINISH TAB
    // ============================================================
    private void RenderRefinishTab()
    {
        RenderSectionButtons(new[] { "Paint", "Clear Coat" });

        var inputStack = new StackPanel { Spacing = 15 };
        inputStack.Children.Add(CreateSectionTitle("Refinish Operations"));

        inputStack.Children.Add(CreateLabel("Paint Stage:"));
        AddControl(inputStack, _refinishPaintStageCombo);

        AddControl(inputStack, _refinishRadarFormulaCheck);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderRefinishOperations();
    }

    private void UpdateRefinishOperations()
    {
        _currentEstimate.RefinishOperations.Clear();

        var paintStage = (_refinishPaintStageCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var radarFormula = _refinishRadarFormulaCheck?.IsChecked == true;

        // Color Tint
        if (paintStage == "2-Stage")
        {
            _currentEstimate.RefinishOperations.Add(new Operation
            {
                Description = "Color Tint (2-Stage)",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.5m,
                Category = "Refinish"
            });

            _currentEstimate.RefinishOperations.Add(new Operation
            {
                Description = "Spray Out Cards (2-Stage)",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.5m,
                Category = "Refinish"
            });
        }
        else if (paintStage == "3-Stage")
        {
            _currentEstimate.RefinishOperations.Add(new Operation
            {
                Description = "Color Tint (3-Stage)",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 1.0m,
                Category = "Refinish"
            });

            _currentEstimate.RefinishOperations.Add(new Operation
            {
                Description = "Spray Out Cards (3-Stage)",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 1.0m,
                Category = "Refinish"
            });
        }
        else if (paintStage == "4-Stage")
        {
            _currentEstimate.RefinishOperations.Add(new Operation
            {
                Description = "Color Tint (4-Stage)",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 1.5m,
                Category = "Refinish"
            });

            _currentEstimate.RefinishOperations.Add(new Operation
            {
                Description = "Spray Out Cards (4-Stage)",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 1.5m,
                Category = "Refinish"
            });
        }

        // Radar Formula Color Tint
        if (radarFormula && !string.IsNullOrWhiteSpace(paintStage))
        {
            var description = paintStage switch
            {
                "2-Stage" => "Color Tint (2-Stage) Radar Formula",
                "3-Stage" => "Color Tint (3-Stage) Radar Formula",
                "4-Stage" => "Color Tint (4-Stage) Radar Formula",
                _ => ""
            };

            if (!string.IsNullOrWhiteSpace(description))
            {
                _currentEstimate.RefinishOperations.Add(new Operation
                {
                    Description = description,
                    OperationType = OperationType.Refinish,
                    Quantity = 1,
                    Price = 0,
                    LaborHours = 0,
                    RefinishHours = 0,
                    Category = "Refinish"
                });
            }
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderRefinishOperations()
    {
        if (_operationsGrid == null) return;

        _operationsGrid.Children.Clear();

        var operationsStack = new StackPanel { Spacing = 10 };
        operationsStack.Children.Add(CreateSectionTitle("Refinish Operations List"));

        if (_currentEstimate.RefinishOperations.Count == 0)
        {
            operationsStack.Children.Add(new TextBlock
            {
                Text = "No operations selected. Use the inputs on the left to add operations.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var op in _currentEstimate.RefinishOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreateOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    // ============================================================
    // MECHANICAL TAB
    // ============================================================
    private void RenderMechanicalTab()
    {
        RenderSectionButtons(new[] { "AC & Cooling", "Suspension" });

        var inputStack = new StackPanel { Spacing = 15 };
        inputStack.Children.Add(CreateSectionTitle("Mechanical Operations"));

        inputStack.Children.Add(CreateLabel("Refrigerant Type:"));
        AddControl(inputStack, _mechRefrigerantCombo);

        AddControl(inputStack, _mechCoverACLinesCheck);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderMechanicalOperations();
    }

    private void UpdateMechanicalOperations()
    {
        _currentEstimate.MechanicalOperations.Clear();

        var refrigerantType = (_mechRefrigerantCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var coverACLines = _mechCoverACLinesCheck?.IsChecked == true;

        // Refrigerant and Oil
        if (refrigerantType == "R134a")
        {
            _currentEstimate.MechanicalOperations.Add(new Operation
            {
                Description = "R134a and Refrigerant Oil",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = 85,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "Mechanical"
            });
        }
        else if (refrigerantType == "R1234yf")
        {
            _currentEstimate.MechanicalOperations.Add(new Operation
            {
                Description = "R1234yf and Refrigerant Oil",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = 485,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "Mechanical"
            });
        }
        else if (refrigerantType == "R744")
        {
            _currentEstimate.MechanicalOperations.Add(new Operation
            {
                Description = "R744 and Refrigerant Oil",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = 600,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "Mechanical"
            });
        }

        // Cover and Protect AC Lines
        if (coverACLines)
        {
            _currentEstimate.MechanicalOperations.Add(new Operation
            {
                Description = "Cover and Protect AC Lines",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = 3,
                LaborHours = 0.2m,
                RefinishHours = 0,
                Category = "Mechanical"
            });
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderMechanicalOperations()
    {
        if (_operationsGrid == null) return;

        _operationsGrid.Children.Clear();

        var operationsStack = new StackPanel { Spacing = 10 };
        operationsStack.Children.Add(CreateSectionTitle("Mechanical Operations List"));

        if (_currentEstimate.MechanicalOperations.Count == 0)
        {
            operationsStack.Children.Add(new TextBlock
            {
                Text = "No operations selected. Use the inputs on the left to add operations.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var op in _currentEstimate.MechanicalOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreateOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    // ============================================================
    // SRS TAB
    // ============================================================
    private void RenderSRSTab()
    {
        RenderSectionButtons(new[] { "Safety", "Airbags" });

        var inputStack = new StackPanel { Spacing = 15 };
        inputStack.Children.Add(CreateSectionTitle("SRS Operations"));

        AddControl(inputStack, _srsSafetyInspectionCheck);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderSRSOperations();
    }

    private void UpdateSRSOperations()
    {
        _currentEstimate.SRSOperations.Clear();

        var safetyInspection = _srsSafetyInspectionCheck?.IsChecked == true;

        if (safetyInspection)
        {
            _currentEstimate.SRSOperations.Add(new Operation
            {
                Description = "Safety Inspections",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 4.0m,
                RefinishHours = 0,
                Category = "M"
            });
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderSRSOperations()
    {
        if (_operationsGrid == null) return;

        _operationsGrid.Children.Clear();

        var operationsStack = new StackPanel { Spacing = 10 };
        operationsStack.Children.Add(CreateSectionTitle("SRS Operations List"));

        if (_currentEstimate.SRSOperations.Count == 0)
        {
            operationsStack.Children.Add(new TextBlock
            {
                Text = "No operations selected. Use the inputs on the left to add operations.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var op in _currentEstimate.SRSOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreateOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    // ============================================================
    // TOTAL LOSS TAB
    // ============================================================
    private void RenderTotalLossTab()
    {
        RenderSectionButtons(new[] { "Fees", "Charges" });

        var inputStack = new StackPanel { Spacing = 15 };
        inputStack.Children.Add(CreateSectionTitle("Total Loss Charges"));

        inputStack.Children.Add(CreateLabel("Administration Fee ($):"));
        AddControl(inputStack, _totalLossAdminFeeBox);

        inputStack.Children.Add(CreateLabel("Coordination Charge ($):"));
        AddControl(inputStack, _totalLossCoordinationBox);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderTotalLossOperations();
    }

    private void UpdateTotalLossOperations()
    {
        _currentEstimate.TotalLossOperations.Clear();

        // Administration Fee
        if (_totalLossAdminFeeBox != null && !string.IsNullOrWhiteSpace(_totalLossAdminFeeBox.Text))
        {
            if (decimal.TryParse(_totalLossAdminFeeBox.Text, out decimal adminFee))
            {
                _currentEstimate.TotalLossOperations.Add(new Operation
                {
                    Description = "Administration Fee",
                    OperationType = OperationType.Replace,
                    Quantity = 1,
                    Price = adminFee,
                    LaborHours = 0,
                    RefinishHours = 0,
                    Category = "TotalLoss"
                });
            }
        }

        // Coordination Charge
        if (_totalLossCoordinationBox != null && !string.IsNullOrWhiteSpace(_totalLossCoordinationBox.Text))
        {
            if (decimal.TryParse(_totalLossCoordinationBox.Text, out decimal coordination))
            {
                _currentEstimate.TotalLossOperations.Add(new Operation
                {
                    Description = "Coordination Charge",
                    OperationType = OperationType.Replace,
                    Quantity = 1,
                    Price = coordination,
                    LaborHours = 0,
                    RefinishHours = 0,
                    Category = "TotalLoss"
                });
            }
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderTotalLossOperations()
    {
        if (_operationsGrid == null) return;

        _operationsGrid.Children.Clear();

        var operationsStack = new StackPanel { Spacing = 10 };
        operationsStack.Children.Add(CreateSectionTitle("Total Loss Operations List"));

        if (_currentEstimate.TotalLossOperations.Count == 0)
        {
            operationsStack.Children.Add(new TextBlock
            {
                Text = "No operations selected. Use the inputs on the left to add operations.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var op in _currentEstimate.TotalLossOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreateOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    // ============================================================
    // BODY ON FRAME TAB
    // ============================================================
    private void RenderBodyOnFrameTab()
    {
        RenderSectionButtons(new[] { "Frame", "Disposal" });

        var inputStack = new StackPanel { Spacing = 15 };
        inputStack.Children.Add(CreateSectionTitle("Body on Frame Operations"));

        AddControl(inputStack, _bodyOnFrameDisposalCheck);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderBodyOnFrameOperations();
    }

    private void UpdateBodyOnFrameOperations()
    {
        _currentEstimate.BodyOnFrameOperations.Clear();

        var frameDisposal = _bodyOnFrameDisposalCheck?.IsChecked == true;

        if (frameDisposal)
        {
            _currentEstimate.BodyOnFrameOperations.Add(new Operation
            {
                Description = "Frame Disposal",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "BodyOnFrame"
            });
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderBodyOnFrameOperations()
    {
        if (_operationsGrid == null) return;

        _operationsGrid.Children.Clear();

        var operationsStack = new StackPanel { Spacing = 10 };
        operationsStack.Children.Add(CreateSectionTitle("Body on Frame Operations List"));

        if (_currentEstimate.BodyOnFrameOperations.Count == 0)
        {
            operationsStack.Children.Add(new TextBlock
            {
                Text = "No operations selected. Use the inputs on the left to add operations.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var op in _currentEstimate.BodyOnFrameOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreateOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    // ============================================================
    // STOLEN RECOVERY TAB
    // ============================================================
    private void RenderStolenRecoveryTab()
    {
        RenderSectionButtons(new[] { "Recovery", "Inspection" });

        var inputStack = new StackPanel { Spacing = 15 };
        inputStack.Children.Add(CreateSectionTitle("Stolen Recovery Operations"));

        AddControl(inputStack, _stolenRecoveryInspectionCheck);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderStolenRecoveryOperations();
    }

    private void UpdateStolenRecoveryOperations()
    {
        _currentEstimate.StolenRecoveryOperations.Clear();

        var inspection = _stolenRecoveryInspectionCheck?.IsChecked == true;

        if (inspection)
        {
            _currentEstimate.StolenRecoveryOperations.Add(new Operation
            {
                Description = "Vehicle Inspection",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "StolenRecovery"
            });
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderStolenRecoveryOperations()
    {
        if (_operationsGrid == null) return;

        _operationsGrid.Children.Clear();

        var operationsStack = new StackPanel { Spacing = 10 };
        operationsStack.Children.Add(CreateSectionTitle("Stolen Recovery Operations List"));

        if (_currentEstimate.StolenRecoveryOperations.Count == 0)
        {
            operationsStack.Children.Add(new TextBlock
            {
                Text = "No operations selected. Use the inputs on the left to add operations.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var op in _currentEstimate.StolenRecoveryOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreateOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    // ============================================================
    // POST REPAIR TAB
    // ============================================================
    private void RenderPostRepairTab()
    {
        var inputStack = new StackPanel { Spacing = 20 };
        inputStack.Children.Add(CreateSectionTitle("Post Repair Inspection"));
        inputStack.Children.Add(new TextBlock
        {
            Text = "Post-repair inspection checklist coming soon...",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            FontSize = 14
        });

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }
    }

    // ============================================================
    // SUMMARY TAB
    // ============================================================
    private void RenderSummaryTab()
    {
        var summaryStack = new StackPanel { Spacing = 20 };
        summaryStack.Children.Add(CreateSectionTitle("Estimate Summary"));

        var summaryText = new TextBlock
        {
            Text = $"📊 Total Operations: {_currentEstimate.TotalOperationsCount}\n" +
                   $"💲 Total Price: ${_currentEstimate.TotalPrice:F2}\n" +
                   $"🛠 Total Labor Hours: {_currentEstimate.TotalLaborHours:F1}\n" +
                   $"🎨 Total Refinish Hours: {_currentEstimate.TotalRefinishHours:F1}",
            FontSize = 18,
            Foreground = new SolidColorBrush(Colors.White),
            LineHeight = 32
        };

        summaryStack.Children.Add(summaryText);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(summaryStack);
        }
    }

    // ============================================================
    // HELPER METHODS
    // ============================================================
    private void RenderSectionButtons(string[] sections)
    {
        if (_sectionButtonsGrid == null) return;

        _sectionButtonsGrid.Children.Clear();

        var buttonStack = new StackPanel { Spacing = 5 };

        foreach (var section in sections)
        {
            var button = new Button
            {
                Content = section,
                Width = 160,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(_currentSection == section
                    ? Color.FromArgb(255, 60, 100, 140)
                    : Color.FromArgb(255, 35, 35, 35)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                FontSize = 13
            };

            button.Click += (s, e) =>
            {
                _currentSection = section;
                RenderCurrentTab();
            };

            buttonStack.Children.Add(button);
        }

        _sectionButtonsGrid.Children.Add(buttonStack);
    }

    private Border CreateOperationRow(Operation op)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(15, 10, 15, 10),
            Margin = new Thickness(0, 5, 0, 5)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Description
        var descText = new TextBlock
        {
            Text = $"{op.OperationTypeString} - {op.Description}",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(descText, 0);

        // Price
        var priceText = new TextBlock
        {
            Text = $"${op.TotalPrice:F2}",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 100)),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(priceText, 1);

        // Labor Hours
        var laborText = new TextBlock
        {
            Text = $"{op.TotalLaborHours:F1} hrs",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 150, 255)),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(laborText, 2);

        // Refinish Hours
        var refinishText = new TextBlock
        {
            Text = $"{op.TotalRefinishHours:F1} hrs",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100)),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(refinishText, 3);

        grid.Children.Add(descText);
        grid.Children.Add(priceText);
        grid.Children.Add(laborText);
        grid.Children.Add(refinishText);

        border.Child = grid;
        return border;
    }

    private TextBlock CreateSectionTitle(string title)
    {
        return new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 15)
        };
    }

    private TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            Margin = new Thickness(0, 5, 0, 5)
        };
    }

    private ComboBox CreateComboBox(string[] items)
    {
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 40,
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1)
        };

        foreach (var item in items)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = item,
                Foreground = new SolidColorBrush(Colors.White)
            });
        }

        combo.SelectedIndex = 0;
        return combo;
    }

    private TextBox CreateTextBox(string placeholder)
    {
        return new TextBox
        {
            PlaceholderText = placeholder,
            Height = 40,
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
    }

    private CheckBox CreateCheckBox(string content)
    {
        return new CheckBox
        {
            Content = content,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            FontSize = 14,
            Margin = new Thickness(0, 5, 0, 5)
        };
    }

    private Border CreateSeparator()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            Margin = new Thickness(0, 10, 0, 10)
        };
    }

    private void AddControl(Panel panel, FrameworkElement? control)
    {
        if (control == null) return;

        // Don't try to move controls between parents - this causes rendering issues
        // Instead, only add if not already in this panel
        if (!panel.Children.Contains(control))
        {
            // If control has a parent, it means it's already in use elsewhere
            // We should not move it - this is likely a design issue
            if (control.Parent != null && control.Parent != panel)
            {
                System.Diagnostics.Debug.WriteLine($"[EstimatingToolView] Warning: Trying to add control that already has a parent: {control.GetType().Name}");
                return;
            }

            panel.Children.Add(control);
        }
    }
}
