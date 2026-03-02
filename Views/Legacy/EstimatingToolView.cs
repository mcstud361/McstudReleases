#nullable enable
using Microsoft.UI;
using McstudDesktop.Models;
using Windows.UI;

namespace McstudDesktop.Views.Legacy;

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

    // Cache for tab content to avoid recreation
    private Dictionary<string, Grid> _tabContentCache = new();
    private Dictionary<string, StackPanel> _inputStackCache = new();
    private Dictionary<string, StackPanel> _operationsStackCache = new();

    // Debounce flag to prevent redundant updates
    private bool _isUpdating = false;

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
    // Common controls for all plastic part types
    private ComboBox? _partTypeCombo;  // Plastic Part Blend, Repair, Replace
    private TextBox? _partNameBox;
    private TextBox? _exteriorRefinishUnitBox;
    private TextBox? _repairUnitBox;  // Repair only

    // Labor section
    private CheckBox? _deNibCheck;
    private CheckBox? _additionalPanelCheck;
    private CheckBox? _partBeingRemovedCheck;
    private TextBox? _trialFitLaborUnitBox;

    // Material section
    private ComboBox? _adhesionPromoterCombo;
    private ComboBox? _flexAdditiveCombo;

    // Additional Parts section
    private ComboBox? _texturedPortionCombo;
    private ComboBox? _licensePlateCombo;
    private CheckBox? _licensePlateDamagedCheck;
    private CheckBox? _drillHolesForLicensePlateCheck;  // Replace only
    private TextBox? _numberOfNameplatesBox;
    private CheckBox? _adhesiveCleanupCheck;

    // Equipment section
    private ComboBox? _parkSensorsCombo;
    private TextBox? _numberOfParkSensorsBox;
    private CheckBox? _paintParkSensorBracketsCheck;
    private CheckBox? _radarBehindPaintedPortionCheck;

    // Add Ons section
    private ComboBox? _ceramicCoatCombo;
    private TextBox? _pricePerCeramicCoatBox;
    private CheckBox? _ppfVinylWrapCheck;
    private TextBox? _pricePerPpfVinylWrapBox;

    // ============================================================
    // COVER CAR CONTROL REFERENCES
    // ============================================================
    private ComboBox? _coverVehicleTypeCombo;      // Gas / EV (A29)
    private CheckBox? _coverFrontCheck;             // Front position (B29)
    private CheckBox? _coverSideCheck;              // Side position (B30)
    private CheckBox? _coverRearCheck;              // Rear position (B31)
    private CheckBox? _coverRefinishCheck;          // Refinish operation (C29)
    private CheckBox? _coverRepairCheck;            // Repair operation (C30)
    private CheckBox? _coverTwoToneCheck;           // Two-Tone paint (C32)
    private ComboBox? _coverLaborTypeCombo;         // Labor type (D29)

    // ============================================================
    // BODY OPERATIONS CONTROL REFERENCES
    // ============================================================
    #pragma warning disable CS0169 // Reserved for future UI binding
    private CheckBox? _bodyCollisionAccessCheck;
    #pragma warning restore CS0169

    // Equipment section
    private CheckBox? _bodyFixtureEquipmentCheck;
    private CheckBox? _bodySpecialToolingCheck;
    private CheckBox? _bodyElectronicMeasurementsCheck;
    private CheckBox? _bodyDocumentMeasurementsCheck;

    // Structural section
    private CheckBox? _bodyAccessHolesCheck;
    private CheckBox? _bodyProtectWeldAreaCheck;
    private CheckBox? _bodyAntiCorrosionCheck;

    // Welding section
    private CheckBox? _bodySeamSealerCheck;
    private CheckBox? _bodyCorrosionProtectionCheck;
    private CheckBox? _bodyCavityWaxCheck;
    private CheckBox? _bodySoundDeadenerCheck;

    // PDR section
    private CheckBox? _bodyPDRAccessCheck;
    private CheckBox? _bodyPDRGlueCheck;
    private ComboBox? _bodyPDRToolCombo;

    // Measurements section
    private CheckBox? _bodyPreMeasurementCheck;
    private CheckBox? _bodyInProcessMeasurementCheck;
    private CheckBox? _bodyPostMeasurementCheck;
    private ComboBox? _bodyMeasurementTypeCombo;

    // Frame section
    private ComboBox? _bodyFrameClampCombo;
    private CheckBox? _bodyStructuralPullCheck;
    private CheckBox? _bodyFrameTimeCheck;

    // ============================================================
    // REFINISH OPERATIONS CONTROL REFERENCES
    // ============================================================
    private ComboBox? _refinishPaintStageCombo;
    private CheckBox? _refinishRadarFormulaCheck;
    #pragma warning disable CS0169 // Reserved for future UI binding
    private CheckBox? _refinishAdditionalCheck;
    #pragma warning restore CS0169

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
        // Part type selector (Blend, Repair, Replace)
        _partTypeCombo = CreateComboBox(new[] { "Plastic Part Blend", "Plastic Part Repair", "Plastic Part Replace" });
        _partTypeCombo.SelectionChanged += (s, e) => {
            // Use DispatcherQueue to delay re-render until after selection is committed
            App.MainDispatcherQueue?.TryEnqueue(() => {
                RenderPartOperationsTab(); // Re-render to show/hide repair unit field
                DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
            });
        };

        // Part name input
        _partNameBox = CreateTextBox("Enter part name");
        _partNameBox.TextChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        // Unit fields
        _exteriorRefinishUnitBox = CreateTextBox("0");
        _exteriorRefinishUnitBox.TextChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _repairUnitBox = CreateTextBox("0");
        _repairUnitBox.TextChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        // Labor section
        _deNibCheck = CreateCheckBox("DE-NIB");
        _deNibCheck.Checked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
        _deNibCheck.Unchecked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _additionalPanelCheck = CreateCheckBox("Additional Panel");
        _additionalPanelCheck.Checked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
        _additionalPanelCheck.Unchecked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _partBeingRemovedCheck = CreateCheckBox("Part being Removed");
        _partBeingRemovedCheck.Checked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
        _partBeingRemovedCheck.Unchecked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _trialFitLaborUnitBox = CreateTextBox("0");
        _trialFitLaborUnitBox.TextChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        // Material section
        _adhesionPromoterCombo = CreateComboBox(new[] { "", "Yes", "No" });
        _adhesionPromoterCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _flexAdditiveCombo = CreateComboBox(new[] { "", "Yes", "No" });
        _flexAdditiveCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        // Additional Parts section
        _texturedPortionCombo = CreateComboBox(new[] { "", "Yes", "No" });
        _texturedPortionCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _licensePlateCombo = CreateComboBox(new[] { "", "Equipped", "Not Equipped" });
        _licensePlateCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _licensePlateDamagedCheck = CreateCheckBox("License Plate Damaged");
        _licensePlateDamagedCheck.Checked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
        _licensePlateDamagedCheck.Unchecked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _drillHolesForLicensePlateCheck = CreateCheckBox("Drill Holes for License Plate");
        _drillHolesForLicensePlateCheck.Checked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
        _drillHolesForLicensePlateCheck.Unchecked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _numberOfNameplatesBox = CreateTextBox("0");
        _numberOfNameplatesBox.TextChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _adhesiveCleanupCheck = CreateCheckBox("Adhesive Cleanup");
        _adhesiveCleanupCheck.Checked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
        _adhesiveCleanupCheck.Unchecked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        // Equipment section
        _parkSensorsCombo = CreateComboBox(new[] { "", "Yes", "No" });
        _parkSensorsCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _numberOfParkSensorsBox = CreateTextBox("0");
        _numberOfParkSensorsBox.TextChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _paintParkSensorBracketsCheck = CreateCheckBox("Paint Park Sensor Brackets");
        _paintParkSensorBracketsCheck.Checked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
        _paintParkSensorBracketsCheck.Unchecked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _radarBehindPaintedPortionCheck = CreateCheckBox("Radar behind Painted Portion");
        _radarBehindPaintedPortionCheck.Checked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
        _radarBehindPaintedPortionCheck.Unchecked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        // Add Ons section
        _ceramicCoatCombo = CreateComboBox(new[] { "", "Yes", "No" });
        _ceramicCoatCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _pricePerCeramicCoatBox = CreateTextBox("0");
        _pricePerCeramicCoatBox.TextChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _ppfVinylWrapCheck = CreateCheckBox("PPF / Vinyl Wrap");
        _ppfVinylWrapCheck.Checked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
        _ppfVinylWrapCheck.Unchecked += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);

        _pricePerPpfVinylWrapBox = CreateTextBox("0");
        _pricePerPpfVinylWrapBox.TextChanged += (s, e) => DebouncedUpdate(UpdatePartOperations, RenderPartOperations);
    }

    private void InitializeCoverCarControls()
    {
        // Vehicle Type (Gas / EV)
        _coverVehicleTypeCombo = CreateComboBox(new[] { "", "Gas", "EV" });
        _coverVehicleTypeCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);

        // Position checkboxes (Front, Side, Rear)
        _coverFrontCheck = CreateCheckBox("Front");
        _coverFrontCheck.Checked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);
        _coverFrontCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);

        _coverSideCheck = CreateCheckBox("Side");
        _coverSideCheck.Checked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);
        _coverSideCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);

        _coverRearCheck = CreateCheckBox("Rear");
        _coverRearCheck.Checked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);
        _coverRearCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);

        // Operation Type checkboxes (Refinish, Repair)
        _coverRefinishCheck = CreateCheckBox("Refinish");
        _coverRefinishCheck.Checked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);
        _coverRefinishCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);

        _coverRepairCheck = CreateCheckBox("Repair");
        _coverRepairCheck.Checked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);
        _coverRepairCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);

        // Two-Tone Paint
        _coverTwoToneCheck = CreateCheckBox("Two-Tone Paint");
        _coverTwoToneCheck.Checked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);
        _coverTwoToneCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);

        // Labor Type (Refinish Labor / $ and Body Labor)
        _coverLaborTypeCombo = CreateComboBox(new[] { "", "Refinish Labor", "$ and Body Labor" });
        _coverLaborTypeCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdateCoverCarOperations, RenderCoverCarOperations);
    }

    private void InitializeBodyControls()
    {
        // Equipment section
        _bodyFixtureEquipmentCheck = CreateCheckBox("Setup Fixture/Bench Equipment");
        _bodyFixtureEquipmentCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyFixtureEquipmentCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodySpecialToolingCheck = CreateCheckBox("Special Tooling Required");
        _bodySpecialToolingCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodySpecialToolingCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyElectronicMeasurementsCheck = CreateCheckBox("Electronic Measuring System");
        _bodyElectronicMeasurementsCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyElectronicMeasurementsCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyDocumentMeasurementsCheck = CreateCheckBox("Document Measurements");
        _bodyDocumentMeasurementsCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyDocumentMeasurementsCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        // Structural section
        _bodyAccessHolesCheck = CreateCheckBox("Drill Access Holes");
        _bodyAccessHolesCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyAccessHolesCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyProtectWeldAreaCheck = CreateCheckBox("Protect Weld Area");
        _bodyProtectWeldAreaCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyProtectWeldAreaCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyAntiCorrosionCheck = CreateCheckBox("Apply Anti-Corrosion");
        _bodyAntiCorrosionCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyAntiCorrosionCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        // Welding section
        _bodySeamSealerCheck = CreateCheckBox("Apply Seam Sealer");
        _bodySeamSealerCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodySeamSealerCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyCorrosionProtectionCheck = CreateCheckBox("Corrosion Protection");
        _bodyCorrosionProtectionCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyCorrosionProtectionCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyCavityWaxCheck = CreateCheckBox("Apply Cavity Wax");
        _bodyCavityWaxCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyCavityWaxCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodySoundDeadenerCheck = CreateCheckBox("Apply Sound Deadener");
        _bodySoundDeadenerCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodySoundDeadenerCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        // PDR section
        _bodyPDRAccessCheck = CreateCheckBox("PDR Access Required");
        _bodyPDRAccessCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyPDRAccessCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyPDRGlueCheck = CreateCheckBox("Glue Pull Required");
        _bodyPDRGlueCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyPDRGlueCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyPDRToolCombo = CreateComboBox(new[] { "", "Dent Puller", "Glue Puller" });
        _bodyPDRToolCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        // Measurements section
        _bodyPreMeasurementCheck = CreateCheckBox("Pre-Repair Measurement");
        _bodyPreMeasurementCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyPreMeasurementCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyInProcessMeasurementCheck = CreateCheckBox("In-Process Measurement");
        _bodyInProcessMeasurementCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyInProcessMeasurementCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyPostMeasurementCheck = CreateCheckBox("Post-Repair Measurement");
        _bodyPostMeasurementCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyPostMeasurementCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyMeasurementTypeCombo = CreateComboBox(new[] { "", "Pre", "In-Process", "Post" });
        _bodyMeasurementTypeCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        // Frame section
        _bodyFrameClampCombo = CreateComboBox(new[] { "", "Pinch Welds", "Truck Clamps" });
        _bodyFrameClampCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyStructuralPullCheck = CreateCheckBox("Structural Pull Required");
        _bodyStructuralPullCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyStructuralPullCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);

        _bodyFrameTimeCheck = CreateCheckBox("Setup Frame Time");
        _bodyFrameTimeCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
        _bodyFrameTimeCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOperations, RenderBodyOperations);
    }

    private void InitializeRefinishControls()
    {
        _refinishPaintStageCombo = CreateComboBox(new[] { "", "2-Stage", "3-Stage", "4-Stage" });
        _refinishPaintStageCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdateRefinishOperations, RenderRefinishOperations);

        _refinishRadarFormulaCheck = CreateCheckBox("Radar Formula Color Tint");
        _refinishRadarFormulaCheck.Checked += (s, e) => DebouncedUpdate(UpdateRefinishOperations, RenderRefinishOperations);
        _refinishRadarFormulaCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateRefinishOperations, RenderRefinishOperations);
    }

    private void InitializeMechanicalControls()
    {
        _mechRefrigerantCombo = CreateComboBox(new[] { "", "R134a", "R1234yf", "R744" });
        _mechRefrigerantCombo.SelectionChanged += (s, e) => DebouncedUpdate(UpdateMechanicalOperations, RenderMechanicalOperations);

        _mechCoverACLinesCheck = CreateCheckBox("Cover and Protect AC Lines");
        _mechCoverACLinesCheck.Checked += (s, e) => DebouncedUpdate(UpdateMechanicalOperations, RenderMechanicalOperations);
        _mechCoverACLinesCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateMechanicalOperations, RenderMechanicalOperations);
    }

    private void InitializeSRSControls()
    {
        _srsSafetyInspectionCheck = CreateCheckBox("Safety Inspections");
        _srsSafetyInspectionCheck.Checked += (s, e) => DebouncedUpdate(UpdateSRSOperations, RenderSRSOperations);
        _srsSafetyInspectionCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateSRSOperations, RenderSRSOperations);
    }

    private void InitializeTotalLossControls()
    {
        _totalLossAdminFeeBox = CreateTextBox("Enter fee amount");
        _totalLossAdminFeeBox.TextChanged += (s, e) => DebouncedUpdate(UpdateTotalLossOperations, RenderTotalLossOperations);

        _totalLossCoordinationBox = CreateTextBox("Enter charge amount");
        _totalLossCoordinationBox.TextChanged += (s, e) => DebouncedUpdate(UpdateTotalLossOperations, RenderTotalLossOperations);
    }

    private void InitializeBodyOnFrameControls()
    {
        _bodyOnFrameDisposalCheck = CreateCheckBox("Frame Disposal");
        _bodyOnFrameDisposalCheck.Checked += (s, e) => DebouncedUpdate(UpdateBodyOnFrameOperations, RenderBodyOnFrameOperations);
        _bodyOnFrameDisposalCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateBodyOnFrameOperations, RenderBodyOnFrameOperations);
    }

    private void InitializeStolenRecoveryControls()
    {
        _stolenRecoveryInspectionCheck = CreateCheckBox("Vehicle Inspection");
        _stolenRecoveryInspectionCheck.Checked += (s, e) => DebouncedUpdate(UpdateStolenRecoveryOperations, RenderStolenRecoveryOperations);
        _stolenRecoveryInspectionCheck.Unchecked += (s, e) => DebouncedUpdate(UpdateStolenRecoveryOperations, RenderStolenRecoveryOperations);
    }

    // Generic debounced update helper
    private void DebouncedUpdate(Action updateAction, Action renderAction)
    {
        if (_isUpdating) return;
        _isUpdating = true;
        try
        {
            updateAction();
            renderAction();
        }
        finally
        {
            _isUpdating = false;
        }
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
            Text = "Mcstud Estimating Tool (MET)",
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
                "Body" => "Equipment",
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

        var cacheKey = $"{_currentTab}_{_currentSection}";

        // Check if we have cached content for this tab
        if (!_tabContentCache.TryGetValue(_currentTab, out var contentGrid))
        {
            // Create new content grid - matching Part Operations layout (2 rows: sub-category buttons + main content)
            contentGrid = new Grid();
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sub-category buttons row
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Main content row

            // Sub-category buttons bar at top
            _sectionButtonsGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 37, 37, 38)),
                Padding = new Thickness(12, 8, 12, 8)
            };
            Grid.SetRow(_sectionButtonsGrid, 0);

            // Main content area - 2 columns (inputs + outputs)
            var mainContentGrid = new Grid
            {
                Margin = new Thickness(16, 8, 16, 8)
            };
            mainContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star), MinWidth = 450 }); // Inputs
            mainContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star), MinWidth = 400 }); // Operations

            var inputScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _inputsGrid = new Grid();
            inputScrollViewer.Content = _inputsGrid;
            Grid.SetColumn(inputScrollViewer, 0);

            var operationsScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _operationsGrid = new Grid();
            operationsScrollViewer.Content = _operationsGrid;
            Grid.SetColumn(operationsScrollViewer, 1);

            mainContentGrid.Children.Add(inputScrollViewer);
            mainContentGrid.Children.Add(operationsScrollViewer);
            Grid.SetRow(mainContentGrid, 1);

            contentGrid.Children.Add(_sectionButtonsGrid);
            contentGrid.Children.Add(mainContentGrid);

            _tabContentCache[_currentTab] = contentGrid;
        }
        else
        {
            // Reuse cached grids
            _sectionButtonsGrid = contentGrid.Children[0] as Grid;
            var mainContentGrid = contentGrid.Children[1] as Grid;
            if (mainContentGrid != null)
            {
                _inputsGrid = ((mainContentGrid.Children[0] as ScrollViewer)?.Content as Grid);
                _operationsGrid = ((mainContentGrid.Children[1] as ScrollViewer)?.Content as Grid);
            }
        }

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

        var inputStack = new StackPanel { Spacing = 16 };

        // Colored header - matching Part Operations style
        var headerColor = _currentSection switch
        {
            "Electrical" => Color.FromArgb(255, 45, 90, 39), // Green
            "Vehicle Diagnostics" => Color.FromArgb(255, 90, 66, 39), // Brown/Orange
            "Misc" => Color.FromArgb(255, 39, 66, 90), // Blue
            _ => Color.FromArgb(255, 45, 90, 39)
        };

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(headerColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var headerStack = new StackPanel();
        headerStack.Children.Add(new TextBlock
        {
            Text = _currentSection,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = "SOP List Operations",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 204, 204, 204))
        });
        headerBorder.Child = headerStack;
        inputStack.Children.Add(headerBorder);

        // Render section-specific inputs with 5-column grid layout
        if (_currentSection == "Electrical")
            RenderSOPElectricalInputs(inputStack);
        else if (_currentSection == "Vehicle Diagnostics")
            RenderSOPDiagnosticsInputs(inputStack);
        else if (_currentSection == "Misc")
            RenderSOPMiscInputs(inputStack);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderSOPOperations();
    }

    private void InitializeSOPControls()
    {
        // Electrical controls
        _sopBatteryTypeCombo = CreateComboBox(new[] { "", "Single", "Dual" });
        _sopBatteryTypeCombo.SelectionChanged += (s, e) => DebouncedSOPUpdate();

        _sopTestBatteryCheck = CreateCheckBox("Test Battery Condition");
        _sopTestBatteryCheck.Checked += (s, e) => DebouncedSOPUpdate();
        _sopTestBatteryCheck.Unchecked += (s, e) => DebouncedSOPUpdate();

        _sopBatterySupportCheck = CreateCheckBox("Battery Support");
        _sopBatterySupportCheck.Checked += (s, e) => DebouncedSOPUpdate();
        _sopBatterySupportCheck.Unchecked += (s, e) => DebouncedSOPUpdate();

        _sopVehicleTypeCombo = CreateComboBox(new[] { "", "Gas", "Hybrid", "EV" });
        _sopVehicleTypeCombo.SelectionChanged += (s, e) => DebouncedSOPUpdate();

        _sopAdasCheck = CreateCheckBox("ADAS System Present");
        _sopAdasCheck.Checked += (s, e) => DebouncedSOPUpdate();
        _sopAdasCheck.Unchecked += (s, e) => DebouncedSOPUpdate();

        // Diagnostics controls
        _sopScanToolCombo = CreateComboBox(new[] { "", "Gas", "Rivian", "Tesla" });
        _sopScanToolCombo.SelectionChanged += (s, e) => DebouncedSOPUpdate();

        _sopSetupScanToolCheck = CreateCheckBox("Setup Scan Tool");
        _sopSetupScanToolCheck.Checked += (s, e) => DebouncedSOPUpdate();
        _sopSetupScanToolCheck.Unchecked += (s, e) => DebouncedSOPUpdate();

        _sopAdasDiagnosticCheck = CreateCheckBox("ADAS Diagnostic Report");
        _sopAdasDiagnosticCheck.Checked += (s, e) => DebouncedSOPUpdate();
        _sopAdasDiagnosticCheck.Unchecked += (s, e) => DebouncedSOPUpdate();

        _sopSimulateFluidsCheck = CreateCheckBox("Simulate Full Fluids");
        _sopSimulateFluidsCheck.Checked += (s, e) => DebouncedSOPUpdate();
        _sopSimulateFluidsCheck.Unchecked += (s, e) => DebouncedSOPUpdate();

        _sopTirePressureCheck = CreateCheckBox("Check Tire Pressure");
        _sopTirePressureCheck.Checked += (s, e) => DebouncedSOPUpdate();
        _sopTirePressureCheck.Unchecked += (s, e) => DebouncedSOPUpdate();

        _sopRemoveItemsCheck = CreateCheckBox("Remove Customer Items");
        _sopRemoveItemsCheck.Checked += (s, e) => DebouncedSOPUpdate();
        _sopRemoveItemsCheck.Unchecked += (s, e) => DebouncedSOPUpdate();

        _sopDriveCycleCheck = CreateCheckBox("Drive Cycle Verification");
        _sopDriveCycleCheck.Checked += (s, e) => DebouncedSOPUpdate();
        _sopDriveCycleCheck.Unchecked += (s, e) => DebouncedSOPUpdate();

        _sopGatewayCheck = CreateCheckBox("Gateway Unlock");
        _sopGatewayCheck.Checked += (s, e) => DebouncedSOPUpdate();
        _sopGatewayCheck.Unchecked += (s, e) => DebouncedSOPUpdate();

        // Misc controls
        _sopCustomPriceBox = CreateTextBox("Enter price (min $50)");
        _sopCustomPriceBox.TextChanged += (s, e) => DebouncedSOPUpdate();

        _sopCustomLaborBox = CreateTextBox("Enter hours (min 1.0)");
        _sopCustomLaborBox.TextChanged += (s, e) => DebouncedSOPUpdate();
    }

    private void DebouncedSOPUpdate()
    {
        if (_isUpdating) return;
        _isUpdating = true;
        try
        {
            UpdateSOPOperations();
            RenderSOPOperations();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void RenderSOPElectricalInputs(StackPanel stack)
    {
        // Main Input Grid - 5 Columns like Part Operations
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Headers
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2

        // Column Headers
        var headers = new[] { "Battery", "Testing", "Support", "Vehicle", "ADAS" };
        for (int i = 0; i < headers.Length; i++)
        {
            var header = new TextBlock
            {
                Text = headers[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumn(header, i);
            Grid.SetRow(header, 0);
            inputGrid.Children.Add(header);
        }

        // Row 1: Battery Type, Test Battery, Battery Support, Vehicle Type, ADAS
        AddGridInput(inputGrid, 0, 1, "12V Battery Type", _sopBatteryTypeCombo);
        AddGridInput(inputGrid, 1, 1, "Test Battery", _sopTestBatteryCheck);
        AddGridInput(inputGrid, 2, 1, "Battery Support", _sopBatterySupportCheck);
        AddGridInput(inputGrid, 3, 1, "Vehicle Type", _sopVehicleTypeCombo);
        AddGridInput(inputGrid, 4, 1, "ADAS System", _sopAdasCheck);

        stack.Children.Add(inputGrid);
    }

    private void AddGridInput(Grid grid, int column, int row, string label, FrameworkElement? control)
    {
        if (control == null) return;

        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
            FontSize = 11
        });

        // Remove control from parent if it has one
        if (control.Parent is Panel parentPanel)
        {
            parentPanel.Children.Remove(control);
        }

        stack.Children.Add(control);
        Grid.SetColumn(stack, column);
        Grid.SetRow(stack, row);
        grid.Children.Add(stack);
    }

    private void RenderSOPDiagnosticsInputs(StackPanel stack)
    {
        // Main Input Grid - 5 Columns like Part Operations
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Headers
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 3

        // Column Headers
        var headers = new[] { "Scan Tool", "Setup", "ADAS Prep", "Verification", "Gateway" };
        for (int i = 0; i < headers.Length; i++)
        {
            var header = new TextBlock
            {
                Text = headers[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumn(header, i);
            Grid.SetRow(header, 0);
            inputGrid.Children.Add(header);
        }

        // Row 1
        AddGridInput(inputGrid, 0, 1, "Scan Tool Type", _sopScanToolCombo);
        AddGridInput(inputGrid, 1, 1, "Setup Scan Tool", _sopSetupScanToolCheck);
        AddGridInput(inputGrid, 2, 1, "ADAS Diagnostic", _sopAdasDiagnosticCheck);
        AddGridInput(inputGrid, 3, 1, "Drive Cycle", _sopDriveCycleCheck);
        AddGridInput(inputGrid, 4, 1, "Gateway Unlock", _sopGatewayCheck);

        // Row 2
        AddGridInput(inputGrid, 2, 2, "Simulate Fluids", _sopSimulateFluidsCheck);
        AddGridInput(inputGrid, 3, 2, "Tire Pressure", _sopTirePressureCheck);

        // Row 3
        AddGridInput(inputGrid, 2, 3, "Remove Items", _sopRemoveItemsCheck);

        stack.Children.Add(inputGrid);
    }

    private void RenderSOPMiscInputs(StackPanel stack)
    {
        // Main Input Grid - 5 Columns like Part Operations
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Headers
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1

        // Column Headers
        var headers = new[] { "Custom Price", "Custom Labor", "", "", "" };
        for (int i = 0; i < headers.Length; i++)
        {
            if (string.IsNullOrEmpty(headers[i])) continue;
            var header = new TextBlock
            {
                Text = headers[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumn(header, i);
            Grid.SetRow(header, 0);
            inputGrid.Children.Add(header);
        }

        // Row 1
        AddGridInput(inputGrid, 0, 1, "Price (min $50)", _sopCustomPriceBox);
        AddGridInput(inputGrid, 1, 1, "Hours (min 1.0)", _sopCustomLaborBox);

        stack.Children.Add(inputGrid);
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

        try
        {
            _operationsGrid.Children.Clear();
        }
        catch
        {
            return;
        }

        // Main output container - matching Part Operations style
        var outputBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 37, 37, 38)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            VerticalAlignment = VerticalAlignment.Top
        };

        var outputStack = new StackPanel();

        // Summary Header - matching Part Operations
        var summaryBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var summaryStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        var ops = _currentEstimate.SOPOperations.Where(o => o.IsVisible).ToList();
        var totalPrice = ops.Sum(o => o.TotalPrice);
        var totalLabor = ops.Sum(o => o.TotalLaborHours);
        var totalRefinish = ops.Sum(o => o.TotalRefinishHours);

        summaryStack.Children.Add(new TextBlock { Text = $"{ops.Count} Ops", Foreground = new SolidColorBrush(Color.FromArgb(255, 78, 201, 176)) });
        summaryStack.Children.Add(new TextBlock { Text = "|", Foreground = new SolidColorBrush(Color.FromArgb(255, 85, 85, 85)) });
        summaryStack.Children.Add(new TextBlock { Text = $"${totalPrice:F2}", Foreground = new SolidColorBrush(Color.FromArgb(255, 78, 201, 176)) });
        summaryStack.Children.Add(new TextBlock { Text = "|", Foreground = new SolidColorBrush(Color.FromArgb(255, 85, 85, 85)) });
        summaryStack.Children.Add(new TextBlock { Text = $"{totalLabor:F1} Labor", Foreground = new SolidColorBrush(Color.FromArgb(255, 156, 220, 254)) });
        summaryStack.Children.Add(new TextBlock { Text = "|", Foreground = new SolidColorBrush(Color.FromArgb(255, 85, 85, 85)) });
        summaryStack.Children.Add(new TextBlock { Text = $"{totalRefinish:F2} Refinish", Foreground = new SolidColorBrush(Color.FromArgb(255, 206, 145, 120)) });

        summaryBorder.Child = summaryStack;
        outputStack.Children.Add(summaryBorder);

        // Clip It Button - matching Part Operations
        var clipButton = new Button
        {
            Content = "Clip it",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(Colors.White),
            Padding = new Thickness(16, 8, 16, 8)
        };
        outputStack.Children.Add(clipButton);

        // Operations Table Header - matching Part Operations
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

        var tableHeaders = new[] { "Operation", "Description", "Qty", "Price", "Labor", "Category", "Refinish" };
        for (int i = 0; i < tableHeaders.Length; i++)
        {
            var th = new TextBlock
            {
                Text = tableHeaders[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)),
                FontSize = 11
            };
            Grid.SetColumn(th, i);
            headerGrid.Children.Add(th);
        }
        outputStack.Children.Add(headerGrid);

        // Operations List
        if (ops.Count == 0)
        {
            outputStack.Children.Add(new TextBlock
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
            var opsListStack = new StackPanel { Spacing = 2 };
            foreach (var op in ops)
            {
                opsListStack.Children.Add(CreateOperationTableRow(op));
            }
            outputStack.Children.Add(opsListStack);
        }

        outputBorder.Child = outputStack;
        _operationsGrid.Children.Add(outputBorder);
    }

    private Grid CreateOperationTableRow(Operation op)
    {
        var rowGrid = new Grid { Padding = new Thickness(0, 4, 0, 4) };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

        var cells = new[]
        {
            (op.OperationTypeString, Color.FromArgb(255, 200, 200, 200)),
            (op.Description, Color.FromArgb(255, 255, 255, 255)),
            (op.Quantity.ToString(), Color.FromArgb(255, 200, 200, 200)),
            ($"${op.TotalPrice:F0}", Color.FromArgb(255, 78, 201, 176)),
            ($"{op.TotalLaborHours:F1}", Color.FromArgb(255, 156, 220, 254)),
            (op.Category, Color.FromArgb(255, 200, 200, 200)),
            ($"{op.TotalRefinishHours:F2}", Color.FromArgb(255, 206, 145, 120))
        };

        for (int i = 0; i < cells.Length; i++)
        {
            var cell = new TextBlock
            {
                Text = cells[i].Item1,
                Foreground = new SolidColorBrush(cells[i].Item2),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(cell, i);
            rowGrid.Children.Add(cell);
        }

        return rowGrid;
    }

    // ============================================================
    // PART OPERATIONS TAB
    // ============================================================
    private void RenderPartOperationsTab()
    {
        // Get selected part type
        var partType = (_partTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Plastic Part Blend";

        var inputStack = new StackPanel { Spacing = 12 };

        // Header with part type selector
        inputStack.Children.Add(CreateSectionTitle("Plastic Part Operation"));

        // Part Type dropdown at top
        inputStack.Children.Add(CreateLabel("Operation Type:"));
        AddControl(inputStack, _partTypeCombo);

        // Part Name
        inputStack.Children.Add(CreateLabel("Part Name:"));
        AddControl(inputStack, _partNameBox);

        // Unit fields in a horizontal row (side by side like Excel)
        var unitsRow = new Grid();

        if (partType == "Plastic Part Repair")
        {
            // Two columns for Repair: Repair Unit + Refinish Unit
            unitsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            unitsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var repairStack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 10, 0) };
            repairStack.Children.Add(CreateLabel("Repair Unit of Part:"));
            AddControl(repairStack, _repairUnitBox);
            Grid.SetColumn(repairStack, 0);
            unitsRow.Children.Add(repairStack);

            var refinishStack = new StackPanel { Spacing = 4 };
            refinishStack.Children.Add(CreateLabel("Exterior Refinish Unit of Part:"));
            AddControl(refinishStack, _exteriorRefinishUnitBox);
            Grid.SetColumn(refinishStack, 1);
            unitsRow.Children.Add(refinishStack);
        }
        else
        {
            // Single column for Blend/Replace: just Refinish Unit
            unitsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var refinishStack = new StackPanel { Spacing = 4 };
            refinishStack.Children.Add(CreateLabel("Exterior Refinish Unit of Part:"));
            AddControl(refinishStack, _exteriorRefinishUnitBox);
            Grid.SetColumn(refinishStack, 0);
            unitsRow.Children.Add(refinishStack);
        }

        inputStack.Children.Add(unitsRow);

        // Horizontal separator
        inputStack.Children.Add(CreateSeparator());

        // Create a grid layout for the columns similar to Excel
        var columnsGrid = new Grid();
        columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Column headers
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var laborHeader = CreateColumnHeader("Labor");
        Grid.SetColumn(laborHeader, 0);
        headerRow.Children.Add(laborHeader);

        var materialHeader = CreateColumnHeader("Material");
        Grid.SetColumn(materialHeader, 1);
        headerRow.Children.Add(materialHeader);

        var addPartsHeader = CreateColumnHeader("Additional Parts");
        Grid.SetColumn(addPartsHeader, 2);
        headerRow.Children.Add(addPartsHeader);

        var equipHeader = CreateColumnHeader("Equipment");
        Grid.SetColumn(equipHeader, 3);
        headerRow.Children.Add(equipHeader);

        var addOnsHeader = CreateColumnHeader("Add Ons");
        Grid.SetColumn(addOnsHeader, 4);
        headerRow.Children.Add(addOnsHeader);

        inputStack.Children.Add(headerRow);

        // Content row with columns
        var contentRow = new Grid();
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Labor column
        var laborCol = CreatePartColumn(new UIElement[] {
            _deNibCheck!,
            _additionalPanelCheck!,
            _partBeingRemovedCheck!,
            CreateLabel("Trial Fit Labor Unit:"),
            _trialFitLaborUnitBox!
        });
        Grid.SetColumn(laborCol, 0);
        contentRow.Children.Add(laborCol);

        // Material column
        var materialCol = CreatePartColumn(new UIElement[] {
            CreateLabel("Adhesion Promoter:"),
            _adhesionPromoterCombo!,
            CreateLabel("Flex Additive:"),
            _flexAdditiveCombo!
        });
        Grid.SetColumn(materialCol, 1);
        contentRow.Children.Add(materialCol);

        // Additional Parts column
        var addPartsElements = new List<UIElement> {
            CreateLabel("Textured Portion on Part:"),
            _texturedPortionCombo!,
            CreateLabel("License Plate:"),
            _licensePlateCombo!,
            _licensePlateDamagedCheck!
        };

        // Add Drill Holes checkbox only for Replace
        if (partType == "Plastic Part Replace")
        {
            addPartsElements.Add(_drillHolesForLicensePlateCheck!);
        }

        addPartsElements.Add(CreateLabel("Number of Nameplates:"));
        addPartsElements.Add(_numberOfNameplatesBox!);
        addPartsElements.Add(_adhesiveCleanupCheck!);

        var addPartsCol = CreatePartColumn(addPartsElements.ToArray());
        Grid.SetColumn(addPartsCol, 2);
        contentRow.Children.Add(addPartsCol);

        // Equipment column
        var equipCol = CreatePartColumn(new UIElement[] {
            CreateLabel("Park Sensors:"),
            _parkSensorsCombo!,
            CreateLabel("Number of Park Sensors:"),
            _numberOfParkSensorsBox!,
            _paintParkSensorBracketsCheck!,
            _radarBehindPaintedPortionCheck!
        });
        Grid.SetColumn(equipCol, 3);
        contentRow.Children.Add(equipCol);

        // Add Ons column
        var addOnsCol = CreatePartColumn(new UIElement[] {
            CreateLabel("Ceramic Coat:"),
            _ceramicCoatCombo!,
            CreateLabel("Price per Ceramic Coat:"),
            _pricePerCeramicCoatBox!,
            _ppfVinylWrapCheck!,
            CreateLabel("Price per PPF / Vinyl Wrap:"),
            _pricePerPpfVinylWrapBox!
        });
        Grid.SetColumn(addOnsCol, 4);
        contentRow.Children.Add(addOnsCol);

        inputStack.Children.Add(contentRow);

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderPartOperations();
    }

    private Border CreateColumnHeader(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
    }

    private StackPanel CreatePartColumn(UIElement[] elements)
    {
        var stack = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(4),
            Padding = new Thickness(4)
        };

        foreach (var element in elements)
        {
            // Remove from previous parent if needed
            if (element is FrameworkElement fe && fe.Parent is Panel parent)
            {
                parent.Children.Remove(element);
            }
            stack.Children.Add(element);
        }

        return stack;
    }

    private Border CreateSeparator()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            Margin = new Thickness(0, 10, 0, 10)
        };
    }

    private void UpdatePartOperations()
    {
        _currentEstimate.PartOperations.Clear();

        var partName = _partNameBox?.Text ?? "";
        var partType = (_partTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Plastic Part Blend";

        if (string.IsNullOrWhiteSpace(partName))
        {
            _currentEstimate.RefreshTotals();
            return;
        }

        // Parse numeric values
        decimal.TryParse(_exteriorRefinishUnitBox?.Text ?? "0", out decimal refinishUnit);
        decimal.TryParse(_repairUnitBox?.Text ?? "0", out decimal repairUnit);
        decimal.TryParse(_trialFitLaborUnitBox?.Text ?? "0", out decimal trialFitUnit);
        decimal.TryParse(_numberOfParkSensorsBox?.Text ?? "0", out decimal numParkSensors);
        decimal.TryParse(_numberOfNameplatesBox?.Text ?? "0", out decimal numNameplates);
        decimal.TryParse(_pricePerCeramicCoatBox?.Text ?? "0", out decimal ceramicPrice);
        decimal.TryParse(_pricePerPpfVinylWrapBox?.Text ?? "0", out decimal ppfPrice);

        // Get dropdown values
        var adhesionPromoter = (_adhesionPromoterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var flexAdditive = (_flexAdditiveCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var texturedPortion = (_texturedPortionCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var licensePlate = (_licensePlateCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var parkSensors = (_parkSensorsCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var ceramicCoat = (_ceramicCoatCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

        // Get checkbox values
        var deNib = _deNibCheck?.IsChecked == true;
        var additionalPanel = _additionalPanelCheck?.IsChecked == true;
        var partBeingRemoved = _partBeingRemovedCheck?.IsChecked == true;
        var licensePlateDamaged = _licensePlateDamagedCheck?.IsChecked == true;
        var drillHolesForLicensePlate = _drillHolesForLicensePlateCheck?.IsChecked == true;
        var adhesiveCleanup = _adhesiveCleanupCheck?.IsChecked == true;
        var paintParkSensorBrackets = _paintParkSensorBracketsCheck?.IsChecked == true;
        var radarBehindPaintedPortion = _radarBehindPaintedPortionCheck?.IsChecked == true;
        var ppfVinylWrap = _ppfVinylWrapCheck?.IsChecked == true;

        // Determine base operation type suffix
        var opTypeSuffix = partType switch
        {
            "Plastic Part Blend" => "Blend",
            "Plastic Part Repair" => "Repair",
            "Plastic Part Replace" => "Replace",
            _ => "Blend"
        };

        // === Generate Operations based on inputs ===

        // Adhesion Promoter
        if (adhesionPromoter == "Yes")
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Adhesion Promoter",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.3m,
                Category = "Part"
            });
        }

        // Flex Additive
        if (flexAdditive == "Yes")
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Flex Additive",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = additionalPanel ? 15 : 5,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "Part"
            });
        }

        // DE-NIB
        if (deNib)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} DE-NIB",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = additionalPanel ? 0.15m : 0.1m,
                Category = "Part"
            });
        }

        // Feather Edge & Block Sand (for Repair)
        if (partType == "Plastic Part Repair" && repairUnit > 0)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Feather Edge & Block Sand",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.125m * repairUnit,
                Category = "Part"
            });
        }

        // Raw Plastic Prep (for Repair and Replace)
        if (partType != "Plastic Part Blend" && refinishUnit > 0)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Raw Plastic Prep",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.3m * refinishUnit,
                Category = "Part"
            });
        }

        // Flex Mixing Time
        if (flexAdditive == "Yes")
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Flex Mixing Time",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.3m,
                Category = "Part"
            });
        }

        // Wet/Dry Sand, Rub-Out & Buff
        if (refinishUnit > 0)
        {
            decimal buffHours = partType == "Plastic Part Blend" ? 0.9m : (additionalPanel ? 0.18m : 0.6m);
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Wet/Dry Sand, Rub-Out & Buff",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = buffHours,
                Category = "Part"
            });
        }

        // Stage and Secure for Refinish
        if (refinishUnit > 0)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Stage and Secure for Refinish",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.3m,
                Category = "Part"
            });
        }

        // Trial Fit
        if (trialFitUnit > 0)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Trial Fit",
                OperationType = OperationType.Refinish,  // R&I type
                Quantity = 1,
                Price = 0,
                LaborHours = trialFitUnit * 2,
                RefinishHours = 0,
                Category = "Part"
            });
        }

        // Park Sensors
        if (parkSensors == "Yes" && numParkSensors > 0)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Park Sensors ({numParkSensors})",
                OperationType = OperationType.Refinish,
                Quantity = (int)numParkSensors,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.1m * numParkSensors,
                Category = "Part"
            });
        }

        // Paint Park Sensor Brackets
        if (paintParkSensorBrackets)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Paint Park Sensor Brackets",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.3m,
                Category = "Part"
            });
        }

        // Radar behind Painted Portion
        if (radarBehindPaintedPortion)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Radar behind Painted Portion",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.2m,
                Category = "Part"
            });
        }

        // License Plate handling
        if (licensePlate == "Equipped")
        {
            if (licensePlateDamaged)
            {
                _currentEstimate.PartOperations.Add(new Operation
                {
                    Description = $"{partName} License Plate Damaged",
                    OperationType = OperationType.Refinish,
                    Quantity = 1,
                    Price = 0,
                    LaborHours = 0,
                    RefinishHours = 0.2m,
                    Category = "Part"
                });
            }

            if (drillHolesForLicensePlate && partType == "Plastic Part Replace")
            {
                _currentEstimate.PartOperations.Add(new Operation
                {
                    Description = $"{partName} Drill Holes for License Plate",
                    OperationType = OperationType.Refinish,
                    Quantity = 1,
                    Price = 0,
                    LaborHours = 0,
                    RefinishHours = 0.15m,
                    Category = "Part"
                });
            }
        }

        // Nameplates
        if (numNameplates > 0)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Nameplates ({numNameplates})",
                OperationType = OperationType.Refinish,
                Quantity = (int)numNameplates,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.1m * numNameplates,
                Category = "Part"
            });
        }

        // Adhesive Cleanup
        if (adhesiveCleanup)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Adhesive Cleanup",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.3m,
                Category = "Part"
            });
        }

        // Ceramic Coat
        if (ceramicCoat == "Yes" && ceramicPrice > 0)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Ceramic Coat",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = ceramicPrice,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "Part"
            });
        }

        // PPF / Vinyl Wrap
        if (ppfVinylWrap && ppfPrice > 0)
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} PPF / Vinyl Wrap",
                OperationType = OperationType.Replace,
                Quantity = 1,
                Price = ppfPrice,
                LaborHours = 0,
                RefinishHours = 0,
                Category = "Part"
            });
        }

        // Textured Portion on Part
        if (texturedPortion == "Yes")
        {
            _currentEstimate.PartOperations.Add(new Operation
            {
                Description = $"{partName} Textured Portion on Part",
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = 0,
                LaborHours = 0,
                RefinishHours = 0.2m,
                Category = "Part"
            });
        }

        _currentEstimate.RefreshTotals();
    }

    private void RenderPartOperations()
    {
        if (_operationsGrid == null) return;

        _operationsGrid.Children.Clear();

        var partType = (_partTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Plastic Part Blend";
        var partName = _partNameBox?.Text ?? "";

        var operationsStack = new StackPanel { Spacing = 10 };

        // Header with summary stats
        var headerPanel = new StackPanel { Spacing = 5 };
        headerPanel.Children.Add(CreateSectionTitle($"{partType}: {partName}"));

        // Calculate totals
        int totalOps = _currentEstimate.PartOperations.Count;
        decimal totalPrice = _currentEstimate.PartOperations.Sum(o => o.Price * o.Quantity);
        decimal totalLabor = _currentEstimate.PartOperations.Sum(o => o.LaborHours);
        decimal totalRefinish = _currentEstimate.PartOperations.Sum(o => o.RefinishHours);

        var summaryText = new TextBlock
        {
            Text = $"{totalOps} Ops | ${totalPrice:F0} | {totalLabor:F1} Labor | {totalRefinish:F2} Refinish",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        };
        headerPanel.Children.Add(summaryText);
        operationsStack.Children.Add(headerPanel);

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
            // Create header row for operations table
            var tableHeader = CreateOperationsTableHeader();
            operationsStack.Children.Add(tableHeader);

            foreach (var op in _currentEstimate.PartOperations.Where(o => o.IsVisible))
            {
                operationsStack.Children.Add(CreatePartOperationRow(op));
            }
        }

        _operationsGrid.Children.Add(operationsStack);
    }

    private Grid CreateOperationsTableHeader()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });   // Operation
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });   // Qty
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });   // Price
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });   // Labor
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });   // Category
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });   // Refinish

        var headers = new[] { "Operation", "Description", "Qty", "Price", "Labor", "Category", "Refinish" };
        for (int i = 0; i < headers.Length; i++)
        {
            var text = new TextBlock
            {
                Text = headers[i],
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Padding = new Thickness(4, 2, 4, 2)
            };
            Grid.SetColumn(text, i);
            grid.Children.Add(text);
        }

        return grid;
    }

    private Border CreatePartOperationRow(Operation op)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var opTypeText = new TextBlock
        {
            Text = op.OperationType.ToString(),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Grid.SetColumn(opTypeText, 0);
        grid.Children.Add(opTypeText);

        var descText = new TextBlock
        {
            Text = op.Description,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 230, 230)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Grid.SetColumn(descText, 1);
        grid.Children.Add(descText);

        var qtyText = new TextBlock
        {
            Text = op.Quantity.ToString(),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Grid.SetColumn(qtyText, 2);
        grid.Children.Add(qtyText);

        var priceText = new TextBlock
        {
            Text = op.Price > 0 ? $"${op.Price:F0}" : "-",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Grid.SetColumn(priceText, 3);
        grid.Children.Add(priceText);

        var laborText = new TextBlock
        {
            Text = op.LaborHours > 0 ? $"{op.LaborHours:F1}" : "-",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Grid.SetColumn(laborText, 4);
        grid.Children.Add(laborText);

        var catText = new TextBlock
        {
            Text = op.Category ?? "-",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Grid.SetColumn(catText, 5);
        grid.Children.Add(catText);

        var refinishText = new TextBlock
        {
            Text = op.RefinishHours > 0 ? $"{op.RefinishHours:F2}" : "-",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100)),
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Grid.SetColumn(refinishText, 6);
        grid.Children.Add(refinishText);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(4),
            Child = grid
        };
    }

    // ============================================================
    // COVER CAR TAB
    // ============================================================
    private void RenderCoverCarTab()
    {
        RenderSectionButtons(new[] { "Masking", "Protection" });

        var inputStack = new StackPanel { Spacing = 12 };

        // Header with colored banner matching other tabs
        var headerColor = _currentSection switch
        {
            "Masking" => Color.FromArgb(255, 90, 60, 90), // Purple
            "Protection" => Color.FromArgb(255, 60, 90, 90), // Teal
            _ => Color.FromArgb(255, 90, 60, 90)
        };

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(headerColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var headerStack = new StackPanel();
        headerStack.Children.Add(new TextBlock
        {
            Text = "Cover Car Operations",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = $"{_currentSection} - Masking and overspray protection",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 204, 204, 204))
        });
        headerBorder.Child = headerStack;
        inputStack.Children.Add(headerBorder);

        // Vehicle Type selector
        inputStack.Children.Add(CreateLabel("Vehicle Type:"));
        AddControl(inputStack, _coverVehicleTypeCombo);

        // Separator
        inputStack.Children.Add(CreateSeparator());

        // Create a 4-column grid layout for inputs
        var columnsGrid = new Grid();
        columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Column headers row
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var positionHeader = CreateColumnHeader("Position");
        Grid.SetColumn(positionHeader, 0);
        headerRow.Children.Add(positionHeader);

        var operationHeader = CreateColumnHeader("Operation Type");
        Grid.SetColumn(operationHeader, 1);
        headerRow.Children.Add(operationHeader);

        var optionsHeader = CreateColumnHeader("Options");
        Grid.SetColumn(optionsHeader, 2);
        headerRow.Children.Add(optionsHeader);

        var laborHeader = CreateColumnHeader("Labor Type");
        Grid.SetColumn(laborHeader, 3);
        headerRow.Children.Add(laborHeader);

        inputStack.Children.Add(headerRow);

        // Content row with columns
        var contentRow = new Grid();
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Position column (Front, Side, Rear checkboxes)
        var positionCol = CreatePartColumn(new UIElement[] {
            _coverFrontCheck!,
            _coverSideCheck!,
            _coverRearCheck!
        });
        Grid.SetColumn(positionCol, 0);
        contentRow.Children.Add(positionCol);

        // Operation Type column (Refinish, Repair checkboxes)
        var operationCol = CreatePartColumn(new UIElement[] {
            _coverRefinishCheck!,
            _coverRepairCheck!
        });
        Grid.SetColumn(operationCol, 1);
        contentRow.Children.Add(operationCol);

        // Options column (Two-Tone checkbox)
        var optionsCol = CreatePartColumn(new UIElement[] {
            _coverTwoToneCheck!
        });
        Grid.SetColumn(optionsCol, 2);
        contentRow.Children.Add(optionsCol);

        // Labor Type column
        var laborCol = CreatePartColumn(new UIElement[] {
            CreateLabel("Labor Type:"),
            _coverLaborTypeCombo!
        });
        Grid.SetColumn(laborCol, 3);
        contentRow.Children.Add(laborCol);

        inputStack.Children.Add(contentRow);

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

        // Get input values
        var vehicleType = (_coverVehicleTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var laborType = (_coverLaborTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var twoTone = _coverTwoToneCheck?.IsChecked == true;
        var front = _coverFrontCheck?.IsChecked == true;
        var side = _coverSideCheck?.IsChecked == true;
        var rear = _coverRearCheck?.IsChecked == true;
        var refinish = _coverRefinishCheck?.IsChecked == true;
        var repair = _coverRepairCheck?.IsChecked == true;

        // Build list of positions
        var positions = new List<string>();
        if (front) positions.Add("Front");
        if (side) positions.Add("Side");
        if (rear) positions.Add("Rear");

        // Build list of operation types
        var opTypes = new List<string>();
        if (refinish) opTypes.Add("Refinish");
        if (repair) opTypes.Add("Repair");

        // If we have any selections, create operations
        if (positions.Count > 0 || !string.IsNullOrWhiteSpace(laborType))
        {
            // Base labor values per position
            decimal baseLaborHours = 0.2m;
            decimal baseRefinishHours = 0.2m;
            decimal basePrice = 5m;

            // Two-tone doubles the values
            if (twoTone)
            {
                baseLaborHours *= 2;
                baseRefinishHours *= 2;
                basePrice *= 2;
            }

            // Create operations for each position
            foreach (var position in positions)
            {
                foreach (var opTypeStr in opTypes)
                {
                    var description = $"Cover Car - {position}";
                    if (twoTone) description += " (Two-Tone)";
                    if (!string.IsNullOrWhiteSpace(vehicleType)) description += $" [{vehicleType}]";

                    var opType = opTypeStr == "Refinish" ? OperationType.Refinish : OperationType.Repair;

                    decimal price = 0;
                    decimal laborHours = 0;
                    decimal refinishHours = 0;

                    if (laborType == "$ and Body Labor")
                    {
                        price = basePrice;
                        laborHours = baseLaborHours;
                    }
                    else if (laborType == "Refinish Labor")
                    {
                        refinishHours = baseRefinishHours;
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
            }

            // If no positions but labor type selected, add a general cover car operation
            if (positions.Count == 0 && !string.IsNullOrWhiteSpace(laborType))
            {
                var description = twoTone ? "Cover Car for Overspray (Two-Tone)" : "Cover Car for Overspray";
                if (!string.IsNullOrWhiteSpace(vehicleType)) description += $" [{vehicleType}]";

                var opType = laborType == "Refinish Labor" ? OperationType.Refinish : OperationType.Replace;

                decimal price = 0;
                decimal laborHours = 0;
                decimal refinishHours = 0;

                if (laborType == "$ and Body Labor")
                {
                    price = basePrice;
                    laborHours = baseLaborHours;
                }
                else if (laborType == "Refinish Labor")
                {
                    refinishHours = baseRefinishHours;
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
        RenderSectionButtons(new[] { "Equipment", "Structural", "Welding", "PDR", "Measurements", "Frame" });

        var inputStack = new StackPanel { Spacing = 16 };

        // Colored header - matching other tabs style
        var headerColor = _currentSection switch
        {
            "Equipment" => Color.FromArgb(255, 90, 66, 39), // Brown/Orange
            "Structural" => Color.FromArgb(255, 45, 90, 39), // Green
            "Welding" => Color.FromArgb(255, 90, 39, 66), // Purple
            "PDR" => Color.FromArgb(255, 39, 66, 90), // Blue
            "Measurements" => Color.FromArgb(255, 66, 90, 39), // Yellow-Green
            "Frame" => Color.FromArgb(255, 90, 45, 39), // Red-Brown
            _ => Color.FromArgb(255, 45, 90, 39)
        };

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(headerColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var headerStack = new StackPanel();
        headerStack.Children.Add(new TextBlock
        {
            Text = _currentSection,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = "Body Operations",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 204, 204, 204))
        });
        headerBorder.Child = headerStack;
        inputStack.Children.Add(headerBorder);

        // Render section-specific inputs
        switch (_currentSection)
        {
            case "Equipment":
                RenderBodyEquipmentInputs(inputStack);
                break;
            case "Structural":
                RenderBodyStructuralInputs(inputStack);
                break;
            case "Welding":
                RenderBodyWeldingInputs(inputStack);
                break;
            case "PDR":
                RenderBodyPDRInputs(inputStack);
                break;
            case "Measurements":
                RenderBodyMeasurementsInputs(inputStack);
                break;
            case "Frame":
                RenderBodyFrameInputs(inputStack);
                break;
        }

        if (_inputsGrid != null)
        {
            _inputsGrid.Children.Clear();
            _inputsGrid.Children.Add(inputStack);
        }

        RenderBodyOperations();
    }

    private void RenderBodyEquipmentInputs(StackPanel stack)
    {
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Headers
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1

        // Column Headers
        var headers = new[] { "Fixture/Bench", "Tooling", "Measuring", "Documentation" };
        for (int i = 0; i < headers.Length; i++)
        {
            var header = new TextBlock
            {
                Text = headers[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumn(header, i);
            Grid.SetRow(header, 0);
            inputGrid.Children.Add(header);
        }

        // Row 1
        AddGridInput(inputGrid, 0, 1, "Fixture/Bench Setup", _bodyFixtureEquipmentCheck);
        AddGridInput(inputGrid, 1, 1, "Special Tooling", _bodySpecialToolingCheck);
        AddGridInput(inputGrid, 2, 1, "Electronic Measuring", _bodyElectronicMeasurementsCheck);
        AddGridInput(inputGrid, 3, 1, "Document Measurements", _bodyDocumentMeasurementsCheck);

        stack.Children.Add(inputGrid);
    }

    private void RenderBodyStructuralInputs(StackPanel stack)
    {
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Headers
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1

        // Column Headers
        var headers = new[] { "Access", "Weld Prep", "Anti-Corrosion" };
        for (int i = 0; i < headers.Length; i++)
        {
            var header = new TextBlock
            {
                Text = headers[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumn(header, i);
            Grid.SetRow(header, 0);
            inputGrid.Children.Add(header);
        }

        // Row 1
        AddGridInput(inputGrid, 0, 1, "Drill Access Holes", _bodyAccessHolesCheck);
        AddGridInput(inputGrid, 1, 1, "Protect Weld Area", _bodyProtectWeldAreaCheck);
        AddGridInput(inputGrid, 2, 1, "Anti-Corrosion Treatment", _bodyAntiCorrosionCheck);

        stack.Children.Add(inputGrid);
    }

    private void RenderBodyWeldingInputs(StackPanel stack)
    {
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Headers
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1

        // Column Headers
        var headers = new[] { "Seam Sealer", "Corrosion", "Cavity Wax", "Sound Deadener" };
        for (int i = 0; i < headers.Length; i++)
        {
            var header = new TextBlock
            {
                Text = headers[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumn(header, i);
            Grid.SetRow(header, 0);
            inputGrid.Children.Add(header);
        }

        // Row 1
        AddGridInput(inputGrid, 0, 1, "Apply Seam Sealer", _bodySeamSealerCheck);
        AddGridInput(inputGrid, 1, 1, "Corrosion Protection", _bodyCorrosionProtectionCheck);
        AddGridInput(inputGrid, 2, 1, "Apply Cavity Wax", _bodyCavityWaxCheck);
        AddGridInput(inputGrid, 3, 1, "Sound Deadener", _bodySoundDeadenerCheck);

        stack.Children.Add(inputGrid);
    }

    private void RenderBodyPDRInputs(StackPanel stack)
    {
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Headers
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1

        // Column Headers
        var headers = new[] { "Access", "Glue Pull", "Tool Type" };
        for (int i = 0; i < headers.Length; i++)
        {
            var header = new TextBlock
            {
                Text = headers[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumn(header, i);
            Grid.SetRow(header, 0);
            inputGrid.Children.Add(header);
        }

        // Row 1
        AddGridInput(inputGrid, 0, 1, "PDR Access Required", _bodyPDRAccessCheck);
        AddGridInput(inputGrid, 1, 1, "Glue Pull Required", _bodyPDRGlueCheck);
        AddGridInput(inputGrid, 2, 1, "PDR Tool Type", _bodyPDRToolCombo);

        stack.Children.Add(inputGrid);
    }

    private void RenderBodyMeasurementsInputs(StackPanel stack)
    {
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Headers
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1

        // Column Headers
        var headers = new[] { "Pre-Repair", "In-Process", "Post-Repair", "Type" };
        for (int i = 0; i < headers.Length; i++)
        {
            var header = new TextBlock
            {
                Text = headers[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumn(header, i);
            Grid.SetRow(header, 0);
            inputGrid.Children.Add(header);
        }

        // Row 1
        AddGridInput(inputGrid, 0, 1, "Pre-Repair Measurement", _bodyPreMeasurementCheck);
        AddGridInput(inputGrid, 1, 1, "In-Process Measurement", _bodyInProcessMeasurementCheck);
        AddGridInput(inputGrid, 2, 1, "Post-Repair Measurement", _bodyPostMeasurementCheck);
        AddGridInput(inputGrid, 3, 1, "Measurement Type", _bodyMeasurementTypeCombo);

        stack.Children.Add(inputGrid);
    }

    private void RenderBodyFrameInputs(StackPanel stack)
    {
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Headers
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1

        // Column Headers
        var headers = new[] { "Clamp Type", "Structural Pull", "Frame Time" };
        for (int i = 0; i < headers.Length; i++)
        {
            var header = new TextBlock
            {
                Text = headers[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumn(header, i);
            Grid.SetRow(header, 0);
            inputGrid.Children.Add(header);
        }

        // Row 1
        AddGridInput(inputGrid, 0, 1, "Frame Clamp Type", _bodyFrameClampCombo);
        AddGridInput(inputGrid, 1, 1, "Structural Pull", _bodyStructuralPullCheck);
        AddGridInput(inputGrid, 2, 1, "Setup Frame Time", _bodyFrameTimeCheck);

        stack.Children.Add(inputGrid);
    }

    private void UpdateBodyOperations()
    {
        _currentEstimate.BodyOperations.Clear();

        // Equipment section
        if (_bodyFixtureEquipmentCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Setup Fixture/Bench Equipment",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 1.0m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodySpecialToolingCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Special Tooling Setup",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 25m,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyElectronicMeasurementsCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Electronic Measuring System Setup",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyDocumentMeasurementsCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Document and Print Measurements",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.3m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        // Structural section
        if (_bodyAccessHolesCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Drill Access Holes",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.3m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyProtectWeldAreaCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Protect Adjacent Panels/Weld Area",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.2m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyAntiCorrosionCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Apply Anti-Corrosion Treatment",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 15m,
                LaborHours = 0.3m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        // Welding section
        if (_bodySeamSealerCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Apply Seam Sealer",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 10m,
                LaborHours = 0.4m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyCorrosionProtectionCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Apply Corrosion Protection",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 15m,
                LaborHours = 0.3m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyCavityWaxCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Apply Cavity Wax",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 20m,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodySoundDeadenerCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Apply Sound Deadener",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 25m,
                LaborHours = 0.4m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        // PDR section
        if (_bodyPDRAccessCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "PDR Access - Remove/Reinstall Components",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyPDRGlueCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Glue Pull Access",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.3m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        var pdrTool = (_bodyPDRToolCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        if (!string.IsNullOrEmpty(pdrTool))
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = $"PDR Tool Setup - {pdrTool}",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 15m,
                LaborHours = 0.2m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        // Measurements section
        if (_bodyPreMeasurementCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Pre-Repair Structural Measurement",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyInProcessMeasurementCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "In-Process Structural Measurement",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyPostMeasurementCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Post-Repair Structural Measurement",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 0.5m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        // Frame section
        var frameClamp = (_bodyFrameClampCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        if (!string.IsNullOrEmpty(frameClamp))
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = $"Setup Frame Clamps - {frameClamp}",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = frameClamp == "Truck Clamps" ? 0.8m : 0.5m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyStructuralPullCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Structural Pull Setup",
                OperationType = OperationType.Repair,
                Quantity = 1,
                Price = 0,
                LaborHours = 1.0m,
                RefinishHours = 0,
                Category = "Body"
            });
        }

        if (_bodyFrameTimeCheck?.IsChecked == true)
        {
            _currentEstimate.BodyOperations.Add(new Operation
            {
                Description = "Setup Frame Time",
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

        // Horizontal scrollable button bar - matching Part Operations style
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var buttonStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };

        foreach (var section in sections)
        {
            var button = new Button
            {
                Content = section,
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(_currentSection == section
                    ? Color.FromArgb(255, 0, 120, 212) // Blue highlight for selected (like Part Operations)
                    : Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                FontSize = 13
            };

            button.Click += (s, e) =>
            {
                _currentSection = section;
                RenderCurrentTab();
            };

            buttonStack.Children.Add(button);
        }

        scrollViewer.Content = buttonStack;
        _sectionButtonsGrid.Children.Add(scrollViewer);
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

    private void AddControl(Panel panel, FrameworkElement? control)
    {
        if (control == null) return;

        // If already in this panel, nothing to do
        if (panel.Children.Contains(control))
            return;

        // Remove from previous parent if needed
        if (control.Parent is Panel oldParent)
        {
            oldParent.Children.Remove(control);
        }

        panel.Children.Add(control);
    }
}
