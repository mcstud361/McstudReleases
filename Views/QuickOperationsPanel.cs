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
using Microsoft.UI.Dispatching;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Quick Operations Chat Panel
    /// Type "I need operations for [X]" and get a quick box with checkboxes to select operations
    /// Pulls from Excel knowledge base via ExcelFormulaService (actual labor/prices from Excel tool)
    /// </summary>
    public sealed class QuickOperationsPanel : UserControl
    {
        private TextBox? _searchBox;
        private StackPanel? _resultsPanel;
        private Border? _resultsContainer;
        private StackPanel? _selectedPanel;
        private DropDownButton? _copyButton;
        private TextBlock? _statusText;
        private Border? _positionIndicator;
        private TextBlock? _positionText;

        // Selected operations
        private List<OperationItem> _selectedOperations = new();

        // All operation categories with their items
        private Dictionary<string, List<OperationItem>> _operationCategories = new();

        // Excel formula service for actual labor/price data
        private readonly ExcelFormulaService _excelService = ExcelFormulaService.Instance;

        // Smart export service for CCC/Mitchell automation
        private readonly SmartExportService _exportService = new();

        public QuickOperationsPanel()
        {
            LoadOperationData();
            LoadFromExcelDatabase(); // Load actual data from Excel
            BuildUI();
        }

        /// <summary>
        /// Load operations from the Excel database with actual labor hours and prices
        /// </summary>
        private void LoadFromExcelDatabase()
        {
            if (!_excelService.IsLoaded) return;

            // Enhance our operation items with actual Excel data
            foreach (var category in _operationCategories)
            {
                foreach (var op in category.Value)
                {
                    // Try to find matching operation in Excel database
                    var excelOps = _excelService.SearchOperations(op.Name);
                    if (excelOps.Count > 0)
                    {
                        var match = excelOps.First();
                        op.LaborHours = match.LaborHours;
                        op.Price = match.Price;
                        op.RefinishHours = match.RefinishHours;
                        op.ExcelRow = match.Row;
                    }
                }
            }
        }

        private void LoadOperationData()
        {
            // A/C Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["A/C Operations"] = new List<OperationItem>
            {
                new("A/C Leak Check", "Mechanical", ""),
                new("A/C System Dye/Electronic Test", "Mechanical", ""),
                new("A/C Reset", "Mechanical", ""),
                new("Bring Vehicle to Operating Temp", "Mechanical", ""),
                new("Climate Control System Check", "Mechanical", ""),
                new("Clamp Cooling Hoses", "Body", ""),
                new("Cooling Fan Operation Check", "Mechanical", ""),
                new("Cover and Protect A/C Lines", "Body", ""),
                new("Fluid Drain/Fill/Purge Coolant", "Mechanical", ""),
                new("Replace O-Rings", "Mechanical", ""),
                new("Refrigerant Leak Test", "Mechanical", ""),
                new("R134a Refrigerant + Oil", "Mechanical", "MechanicalOp_A29") { IsMaterial = true },
                new("R1234yf Refrigerant + Oil", "Mechanical", "MechanicalOp_A29") { IsMaterial = true },
                new("R744 (CO2) Refrigerant + Oil", "Mechanical", "MechanicalOp_A29") { IsMaterial = true },
                new("Evacuate A/C System", "Mechanical", "MechanicalOp_B29"),
                new("Recharge A/C System", "Mechanical", "MechanicalOp_C29")
            };

            // Alignment Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["Alignment"] = new List<OperationItem>
            {
                new("4-Wheel Alignment", "Mechanical", "MechanicalOp_B79"),
                new("2-Wheel Alignment", "Mechanical", ""),
                new("Wheel Balance (each)", "Mechanical", "MechanicalOp_B81"),
                new("Check and Adjust Tire Pressure", "Mechanical", "SOPList_B83"),
                new("TPMS Reset", "Mechanical", ""),
                new("TPMS Sensor (each)", "Mechanical", ""),
                new("Steering Angle Sensor Recalibration", "Mechanical", ""),
                new("Torque Wheels", "Mechanical", "")
            };

            // ADAS Calibrations - Reference list (prices/labor from Excel tool)
            _operationCategories["ADAS Calibration"] = new List<OperationItem>
            {
                new("ADAS Diagnostic Report", "Diagnostic", "SOPList_B79"),
                new("Setup ADAS Equipment", "Mechanical", ""),
                new("Static Calibration (Camera/Radar)", "Mechanical", ""),
                new("Dynamic Calibration (Camera/Radar)", "Mechanical", ""),
                new("Static + Dynamic Calibration", "Mechanical", ""),
                new("Forward Camera Calibration", "Mechanical", "MechanicalOp_C179"),
                new("Front Radar Calibration", "Mechanical", "MechanicalOp_D179"),
                new("Check Radar Angle", "Mechanical", ""),
                new("Check BSI Installation Angle", "Body", ""),
                new("Blind Spot Sensor Calibration", "Mechanical", ""),
                new("360 Camera Calibration", "Mechanical", ""),
                new("Programming Radar Prior to Calibrating", "Mechanical", ""),
                new("Charge/Maintain Battery for ADAS", "Mechanical", ""),
                new("Remove Customer Belongings for ADAS", "Body", "SOPList_B85"),
                new("Simulate Full Fluids for ADAS", "Body", "SOPList_B81"),
                new("Secured Gateway Access", "Mechanical", "SOPList_A87")
            };

            // Diagnostic Scan Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["Scanning"] = new List<OperationItem>
            {
                new("Pre-Repair Scan", "Diagnostic", "SOPList_A79"),
                new("In-Process Scan", "Diagnostic", ""),
                new("Post-Repair Scan", "Diagnostic", ""),
                new("Setup Scan Tool", "Mechanical", "SOPList_A81"),
                new("Gateway Unlock", "Mechanical", "SOPList_A87"),
                new("Clear Codes", "Mechanical", ""),
                new("Drive Cycle Operational Verification", "Mechanical", "SOPList_B87"),
                new("Dynamic Systems Verification (Road Test)", "Mechanical", ""),
                new("Test Battery Condition", "Mechanical", ""),
                new("Electronic Reset", "Mechanical", ""),
                new("Provide Support Assist Functions", "Mechanical", ""),
                new("Service Mode", "Mechanical", ""),
                new("Tesla Toolbox Scan", "Mechanical", ""),
                new("Tesla Software Script Programming", "Mechanical", "")
            };

            // Battery Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["Battery/Electrical"] = new List<OperationItem>
            {
                new("Battery Support", "Mechanical", "SOPList_A29"),
                new("Test Battery Condition", "Mechanical", ""),
                new("Disconnect & Reconnect Battery", "Mechanical", ""),
                new("Diesel/Dually/Dual Battery D&R", "Mechanical", ""),
                new("Charge and Maintain Battery", "Mechanical", ""),
                new("Jump Start Dead Vehicle", "Mechanical", ""),
                new("Electronic Reset", "Mechanical", ""),
                new("Cover/Protect Electrical Connections", "Body", ""),
                new("High Voltage Disable/Enable", "Mechanical", ""),
                new("Mobile Cart for EV", "Body", ""),
                new("Verify No High Voltage", "Mechanical", ""),
                new("Wire Repair", "Mechanical", ""),
                new("Surge Protector", "Mechanical", "")
            };

            // Frame/Structural Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["Frame/Structural"] = new List<OperationItem>
            {
                new("Frame Setup", "Body", "BodyOp_A29"),
                new("Frame Pull", "Body", "BodyOp_B29"),
                new("Frame Measure", "Body", "BodyOp_C29"),
                new("Pre-Repair Measure", "Body", "BodyOp_D81"),
                new("In-Process Measure", "Body", "BodyOp_D82"),
                new("Post-Repair Measure", "Body", "BodyOp_D83"),
                new("Measure & Mark Gap Dimensions", "Body", ""),
                new("Fixture/Anchor Setup", "Body", "BodyOp_A31"),
                new("Jig Setup", "Body", ""),
                new("Frame Clamps", "Frame", "BodyOp_D87"),
                new("Destructive Test Weld", "Body", ""),
                new("Welder Setup (Power Up/Down)", "Body", ""),
                new("Spot Welder Setup", "Body", ""),
                new("Plug Welder Setup", "Body", ""),
                new("Welding Consumables", "Body", "") { IsMaterial = true },
                new("Welding Shield", "Body", ""),
                new("Weld Blankets", "Body", ""),
                new("Spark Paper", "Body", ""),
                new("Test Structural Adhesive", "Body", "")
            };

            // Refinish Additionals - Reference list (prices/labor from Excel tool)
            _operationCategories["Refinish Additionals"] = new List<OperationItem>
            {
                new("Color Tint - 2 Stage", "Refinish", "RefinishOp_B31"),
                new("Color Tint - 3 Stage", "Refinish", "RefinishOp_B35"),
                new("Color Tint - 4 Stage", "Refinish", ""),
                new("Color Check", "Refinish", "RefinishOp_C29"),
                new("Spray Out Card", "Refinish", ""),
                new("Flex Additive", "Refinish", "") { IsMaterial = true },
                new("Flex Mixing Time", "Refinish", ""),
                new("Adhesion Promoter", "Refinish", ""),
                new("Raw Plastic Prep", "Refinish", ""),
                new("Feather Edge & Block Sand", "Refinish", ""),
                new("DE-NIB First Panel (sky-facing)", "Refinish", ""),
                new("DE-NIB First Panel (other)", "Refinish", ""),
                new("DE-NIB Additional Panel", "Refinish", ""),
                new("Wet/Dry Sand, Rub-out & Buff", "Refinish", ""),
                new("Wash and Tack after Blocking", "Body", ""),
                new("Wash and Tack after Repairs", "Body", ""),
                new("Static Gun", "Refinish", ""),
                new("Add for Inside (50% of base)", "Refinish", ""),
                new("Mask Jams - Small (Fender/A-pillar)", "Refinish", ""),
                new("Mask Jams - Large (Door/Quarter/Hood)", "Refinish", ""),
                new("Stage and Secure for Refinish", "Refinish", "")
            };

            // Corrosion/Sealer Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["Corrosion/Sealer"] = new List<OperationItem>
            {
                new("Weld-Through Primer (Zinc)", "Body", "") { IsMaterial = true },
                new("Corrosion Protection - Repaired Panel", "Refinish", ""),
                new("Corrosion Protection - Small Part", "Refinish", ""),
                new("Corrosion Protection - Large Part", "Refinish", ""),
                new("Cavity Wax Injection", "Body", ""),
                new("Seam Sealer - Cleanup/Mask/Replicate OEM", "Body", ""),
                new("Seam Sealer - Refinish Replicate OEM", "Refinish", ""),
                new("Undercoat", "Refinish", ""),
                new("Sound Deadener (Aftermarket)", "Body", ""),
                new("Foam Removal, Cleanup and Apply", "Body", ""),
                new("E-Coat Removal", "Body", ""),
                new("Remove & Cleanup Debris from Grinding", "Body", ""),
                new("Prepare Part for Installation", "Body", ""),
                new("Chip Guard - Replicate OEM", "Body", "")
            };

            // SRS/Airbag Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["SRS/Airbag"] = new List<OperationItem>
            {
                new("Driver Airbag R&I", "SRS", "SRSOp_A29"),
                new("Passenger Airbag R&I", "SRS", "SRSOp_A31"),
                new("Side Airbag R&I", "SRS", "SRSOp_A33"),
                new("Knee Airbag R&I", "SRS", "SRSOp_A35"),
                new("Curtain Airbag R&I", "SRS", ""),
                new("Seatbelt Pretensioner", "SRS", "SRSOp_A79"),
                new("SRS System Reset", "SRS", ""),
                new("Airbag Module Replace", "SRS", ""),
                new("Replace Door Hole Tapes/Grommets (for SRS)", "Body", "")
            };

            // Cover Car Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["Cover Car"] = new List<OperationItem>
            {
                new("Back Tape Jamb", "Refinish", ""),
                new("Mask Backside of Bumper for Refinish", "Refinish", ""),
                new("Clean and Cover Car for Primer", "Refinish", ""),
                new("Collision Wrap (per panel)", "Body", ""),
                new("Cover and Protect Electrical Connections", "Body", ""),
                new("Cover and Protect Fuel Tank Opening", "Body", ""),
                new("Cover Car for Edging", "Refinish", "CoverCarOp_C29"),
                new("Cover Car for Storage", "Refinish", ""),
                new("Cover Car for Overspray", "Refinish", ""),
                new("Cover Engine/Front Compartment", "Refinish", ""),
                new("Cover Glass Opening for Refinish", "Refinish", ""),
                new("Cover Interior and Jambs - Refinish", "Refinish", ""),
                new("Cover Interior and Jambs - Repairs", "Body", "CoverCarOp_C30"),
                new("Cover Pickup Bed Interior", "Refinish", ""),
                new("Cover Trunk Compartment", "Refinish", ""),
                new("Cover Suspension Components", "Body", ""),
                new("Mask for Buffing", "Refinish", ""),
                new("Mask for Cavity Wax Injection", "Refinish", ""),
                new("Mask for Undercoat", "Refinish", ""),
                new("Mask Seamless Roof Panel", "Refinish", ""),
                new("Seat and Steering Wheel Cover", "Body", "")
            };

            // Body Welded Panel Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["Welded Panel Ops"] = new List<OperationItem>
            {
                new("Trial Fit Welded Panel", "Body", ""),
                new("Weld Zone Repair - Small", "Body", ""),
                new("Weld Zone Repair - Large", "Body", ""),
                new("Refinish Backside of Panel (100%)", "Refinish", ""),
                new("Add for Masking Front Side", "Refinish", ""),
                new("Collision Damage Access", "Body", ""),
                new("Relieve Pressure", "Body", ""),
                new("R&I Electronics Within Weld Zone", "Mechanical", ""),
                new("Dent Puller", "Body", "BodyOp_D35"),
                new("Glue Pull", "Body", "BodyOp_D36"),
                new("Welding Plates/Studs", "Body", ""),
                new("Stud/Nutsert Gun Setup", "Body", ""),
                new("Install Studs/Nutserts", "Body", ""),
                new("Destructive Stud Test", "Body", ""),
                new("Self Tapping Screw Set", "Body", ""),
                new("Nutsert Setup and Test", "Structural", ""),
                new("Rivet Gun Setup and Test", "Structural", ""),
                new("Sacrificial Panel Removal", "Body", ""),
                new("Backing Plates - Fabricate & Install", "Body", ""),
                new("Straighten Rear Wheel Arch Flange", "Body", "")
            };

            // Glass Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["Glass"] = new List<OperationItem>
            {
                new("Broken Glass Cleanup", "Body", ""),
                new("Urethane Kit - Large Glass", "Body", "") { IsMaterial = true },
                new("Urethane Kit - Small Glass", "Body", "") { IsMaterial = true },
                new("Glass Primer - Large", "Body", "") { IsMaterial = true },
                new("Glass Primer - Small", "Body", "") { IsMaterial = true },
                new("Body Primer for Glass Opening", "Body", ""),
                new("Mask Glass Opening - Large", "Refinish", ""),
                new("Mask Glass Opening - Small", "Refinish", ""),
                new("Check Defrost Function", "Body", ""),
                new("Gel Pack Replace (Rain Sensor)", "Body", ""),
                new("R&I Inspection Sticker", "Body", ""),
                new("R&I Dash Cam", "Body", ""),
                new("Glass Cleaner", "Body", "") { IsMaterial = true }
            };

            // Bolted-On Body Parts - Reference list (prices/labor from Excel tool)
            _operationCategories["Bolted Body Parts"] = new List<OperationItem>
            {
                new("Trial Fit Door/Hood/Liftgate", "Body", ""),
                new("Trial Fit Latch", "Body", ""),
                new("Align Hinge", "Body", ""),
                new("Align Striker", "Body", ""),
                new("R&I Door Check", "Body", ""),
                new("R&I Lift Cylinder", "Body", ""),
                new("Stage and Secure for Refinish", "Refinish", ""),
                new("Replicate Seam Sealer OEM Appearance", "Refinish", ""),
                new("Cavity Wax Injection", "Body", ""),
                new("Touch Up Painted Bolts", "Refinish", ""),
                new("Replace Emission Label", "Body", ""),
                new("Replace A/C Label", "Body", ""),
                new("IPA Wipe (for labels)", "Body", "")
            };

            // Misc/Common Operations - Reference list (prices/labor from Excel tool)
            _operationCategories["Miscellaneous"] = new List<OperationItem>
            {
                new("Pre-Wash and Degrease", "Body", "SOPList_A129"),
                new("Clean for Delivery - Standard", "Body", ""),
                new("Clean for Delivery - Large Vehicle", "Body", ""),
                new("Remove Customer Belongings", "Body", "SOPList_B85"),
                new("Biohazard Cleanup", "Body", "SOPList_A133"),
                new("OEM Research", "Mechanical", ""),
                new("Parts Disposal Fee", "Body", "") { IsMaterial = true },
                new("Hazardous Waste Removal", "Body", "") { IsMaterial = true },
                new("Miscellaneous Hardware", "Body", "") { IsMaterial = true },
                new("Transfer Hardware", "Body", ""),
                new("Clean and Retape", "Body", ""),
                new("Adhesive Cleanup", "Body", ""),
                new("Storage Charges (per day)", "Body", "") { IsMaterial = true },
                new("Drill Holes for License Plate", "Body", ""),
                new("Anti-Theft Label (ECSVin)", "Body", "") { IsMaterial = true }
            };
        }

        private void BuildUI()
        {
            var mainGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                Padding = new Thickness(16)
            };

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Results
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Selected
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Position Indicator
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Actions

            // Header
            var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(new TextBlock
            {
                Text = "Quick Operations",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            header.Children.Add(new TextBlock
            {
                Text = "Type what you need (e.g., \"AC operations\", \"ADAS\", \"scan\", \"alignment\")",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            });
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Search box
            var searchBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var searchIcon = new FontIcon
            {
                Glyph = "\uE8BB", // Chat icon
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(searchIcon, 0);

            _searchBox = new TextBox
            {
                PlaceholderText = "\"Hey, I need operations for...\"",
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 14
            };
            _searchBox.TextChanged += SearchBox_TextChanged;
            Grid.SetColumn(_searchBox, 1);

            searchGrid.Children.Add(searchIcon);
            searchGrid.Children.Add(_searchBox);
            searchBorder.Child = searchGrid;
            Grid.SetRow(searchBorder, 1);
            mainGrid.Children.Add(searchBorder);

            // Results container with tabs
            _resultsContainer = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var resultsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12)
            };
            _resultsPanel = new StackPanel { Spacing = 8 };
            resultsScroll.Content = _resultsPanel;
            _resultsContainer.Child = resultsScroll;
            Grid.SetRow(_resultsContainer, 2);
            mainGrid.Children.Add(_resultsContainer);

            // Show initial categories
            ShowAllCategories();

            // Selected operations panel
            var selectedBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 50, 40)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var selectedStack = new StackPanel();
            selectedStack.Children.Add(new TextBlock
            {
                Text = "Selected Operations:",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            _selectedPanel = new StackPanel { Spacing = 2 };
            selectedStack.Children.Add(_selectedPanel);
            selectedBorder.Child = selectedStack;
            Grid.SetRow(selectedBorder, 3);
            mainGrid.Children.Add(selectedBorder);

            // Position Indicator for CCC export
            _positionIndicator = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 35, 30)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var positionStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var positionIcon = new FontIcon
            {
                Glyph = "\uE81C", // Crosshairs/target icon
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 80)),
                VerticalAlignment = VerticalAlignment.Center
            };
            _positionText = new TextBlock
            {
                Text = "Click in CCC to set export position",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 80)),
                VerticalAlignment = VerticalAlignment.Center
            };
            positionStack.Children.Add(positionIcon);
            positionStack.Children.Add(_positionText);
            _positionIndicator.Child = positionStack;
            Grid.SetRow(_positionIndicator, 4);
            mainGrid.Children.Add(_positionIndicator);

            // Update position indicator periodically
            var positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            positionTimer.Tick += (s, e) => UpdatePositionIndicator();
            positionTimer.Start();

            // Actions
            var actionsPanel = new Grid();
            actionsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "0 operations selected",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statusText, 0);

            var clearButton = new Button
            {
                Content = "Clear",
                FontSize = 12,
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(8, 0, 0, 0)
            };
            clearButton.Click += ClearButton_Click;
            Grid.SetColumn(clearButton, 1);

            // Copy dropdown for system-specific formats
            var copyFlyout = new MenuFlyout();

            var cccDesktopItem = new MenuFlyoutItem { Text = "CCC Desktop" };
            cccDesktopItem.Click += (s, e) => CopyForSystem("CCC Desktop");
            copyFlyout.Items.Add(cccDesktopItem);

            var cccWebItem = new MenuFlyoutItem { Text = "CCC Web" };
            cccWebItem.Click += (s, e) => CopyForSystem("CCC Web");
            copyFlyout.Items.Add(cccWebItem);

            var mitchellItem = new MenuFlyoutItem { Text = "Mitchell" };
            mitchellItem.Click += (s, e) => CopyForSystem("Mitchell");
            copyFlyout.Items.Add(mitchellItem);

            copyFlyout.Items.Add(new MenuFlyoutSeparator());

            var plainItem = new MenuFlyoutItem { Text = "Plain Text" };
            plainItem.Click += (s, e) => CopyForSystem("Plain");
            copyFlyout.Items.Add(plainItem);

            _copyButton = new DropDownButton
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
                FontSize = 12,
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 180)),
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(8, 0, 0, 0),
                Flyout = copyFlyout
            };
            Grid.SetColumn(_copyButton, 2);

            // Export dropdown for direct insert into estimating systems
            var exportFlyout = new MenuFlyout();

            var exportCccDesktopItem = new MenuFlyoutItem { Text = "CCC Desktop" };
            exportCccDesktopItem.Click += (s, e) => ExportToSystem(SmartExportService.ExportTarget.CCCDesktop);
            exportFlyout.Items.Add(exportCccDesktopItem);

            var exportCccWebItem = new MenuFlyoutItem { Text = "CCC Web" };
            exportCccWebItem.Click += (s, e) => ExportToSystem(SmartExportService.ExportTarget.CCCWeb);
            exportFlyout.Items.Add(exportCccWebItem);

            var exportMitchellItem = new MenuFlyoutItem { Text = "Mitchell" };
            exportMitchellItem.Click += (s, e) => ExportToSystem(SmartExportService.ExportTarget.Mitchell);
            exportFlyout.Items.Add(exportMitchellItem);

            exportFlyout.Items.Add(new MenuFlyoutSeparator());

            var detectItem = new MenuFlyoutItem { Text = "Auto-Detect" };
            detectItem.Click += (s, e) => ExportToSystem(SmartExportService.ExportTarget.None); // Auto-detect
            exportFlyout.Items.Add(detectItem);

            var exportButton = new DropDownButton
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
                FontSize = 12,
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 150, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(8, 0, 0, 0),
                Flyout = exportFlyout
            };
            Grid.SetColumn(exportButton, 3);

            actionsPanel.Children.Add(_statusText);
            actionsPanel.Children.Add(clearButton);
            actionsPanel.Children.Add(_copyButton);
            actionsPanel.Children.Add(exportButton);
            Grid.SetRow(actionsPanel, 5);
            mainGrid.Children.Add(actionsPanel);

            Content = mainGrid;
        }

        private void ShowAllCategories()
        {
            _resultsPanel?.Children.Clear();

            foreach (var category in _operationCategories)
            {
                var expander = CreateCategoryExpander(category.Key, category.Value);
                _resultsPanel?.Children.Add(expander);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var search = _searchBox?.Text?.ToLowerInvariant() ?? "";

            // Remove common filler words
            search = search.Replace("hey", "").Replace("i need", "").Replace("operations for", "")
                          .Replace("operations", "").Replace("give me", "").Replace("show me", "")
                          .Replace("what about", "").Trim();

            if (string.IsNullOrWhiteSpace(search))
            {
                ShowAllCategories();
                return;
            }

            FilterOperations(search);
        }

        private void FilterOperations(string search)
        {
            _resultsPanel?.Children.Clear();

            // Search aliases - comprehensive keyword mapping
            var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // A/C
                ["ac"] = new[] { "A/C Operations" },
                ["a/c"] = new[] { "A/C Operations" },
                ["air conditioning"] = new[] { "A/C Operations" },
                ["refrigerant"] = new[] { "A/C Operations" },
                ["r134"] = new[] { "A/C Operations" },
                ["r1234"] = new[] { "A/C Operations" },
                ["coolant"] = new[] { "A/C Operations" },
                ["condenser"] = new[] { "A/C Operations" },
                ["evacuate"] = new[] { "A/C Operations" },
                ["recharge"] = new[] { "A/C Operations" },

                // ADAS
                ["adas"] = new[] { "ADAS Calibration" },
                ["calibration"] = new[] { "ADAS Calibration" },
                ["calibrate"] = new[] { "ADAS Calibration" },
                ["camera"] = new[] { "ADAS Calibration" },
                ["radar"] = new[] { "ADAS Calibration" },
                ["blind spot"] = new[] { "ADAS Calibration" },
                ["bsi"] = new[] { "ADAS Calibration" },
                ["lane departure"] = new[] { "ADAS Calibration" },
                ["forward camera"] = new[] { "ADAS Calibration" },

                // Scanning
                ["scan"] = new[] { "Scanning" },
                ["diagnostic"] = new[] { "Scanning" },
                ["pre scan"] = new[] { "Scanning" },
                ["post scan"] = new[] { "Scanning" },
                ["drive cycle"] = new[] { "Scanning" },
                ["dtc"] = new[] { "Scanning" },
                ["codes"] = new[] { "Scanning" },
                ["gateway"] = new[] { "Scanning" },
                ["tesla toolbox"] = new[] { "Scanning" },

                // Alignment
                ["alignment"] = new[] { "Alignment" },
                ["wheel"] = new[] { "Alignment" },
                ["tire"] = new[] { "Alignment" },
                ["tpms"] = new[] { "Alignment" },
                ["steering angle"] = new[] { "Alignment" },
                ["torque"] = new[] { "Alignment" },

                // Battery/Electrical
                ["battery"] = new[] { "Battery/Electrical" },
                ["electrical"] = new[] { "Battery/Electrical" },
                ["ev"] = new[] { "Battery/Electrical" },
                ["electric"] = new[] { "Battery/Electrical" },
                ["high voltage"] = new[] { "Battery/Electrical" },
                ["hv"] = new[] { "Battery/Electrical" },
                ["wire"] = new[] { "Battery/Electrical" },
                ["disconnect"] = new[] { "Battery/Electrical" },
                ["reset"] = new[] { "Battery/Electrical", "Scanning" },

                // Frame/Structural
                ["frame"] = new[] { "Frame/Structural" },
                ["structural"] = new[] { "Frame/Structural" },
                ["measure"] = new[] { "Frame/Structural" },
                ["jig"] = new[] { "Frame/Structural" },
                ["fixture"] = new[] { "Frame/Structural" },
                ["pull"] = new[] { "Frame/Structural" },
                ["welder"] = new[] { "Frame/Structural" },
                ["spot weld"] = new[] { "Frame/Structural", "Welded Panel Ops" },

                // Refinish
                ["paint"] = new[] { "Refinish Additionals" },
                ["refinish"] = new[] { "Refinish Additionals" },
                ["flex"] = new[] { "Refinish Additionals" },
                ["blend"] = new[] { "Refinish Additionals" },
                ["denib"] = new[] { "Refinish Additionals" },
                ["de-nib"] = new[] { "Refinish Additionals" },
                ["buff"] = new[] { "Refinish Additionals" },
                ["color tint"] = new[] { "Refinish Additionals" },
                ["spray out"] = new[] { "Refinish Additionals" },
                ["adhesion"] = new[] { "Refinish Additionals" },
                ["feather"] = new[] { "Refinish Additionals" },
                ["block sand"] = new[] { "Refinish Additionals" },
                ["3 stage"] = new[] { "Refinish Additionals" },
                ["4 stage"] = new[] { "Refinish Additionals" },

                // Corrosion/Sealer
                ["corrosion"] = new[] { "Corrosion/Sealer" },
                ["sealer"] = new[] { "Corrosion/Sealer" },
                ["seam sealer"] = new[] { "Corrosion/Sealer" },
                ["cavity wax"] = new[] { "Corrosion/Sealer" },
                ["weld through"] = new[] { "Corrosion/Sealer" },
                ["undercoat"] = new[] { "Corrosion/Sealer" },
                ["rust"] = new[] { "Corrosion/Sealer" },
                ["e-coat"] = new[] { "Corrosion/Sealer" },

                // Cover Car
                ["cover"] = new[] { "Cover Car" },
                ["mask"] = new[] { "Cover Car" },
                ["masking"] = new[] { "Cover Car" },
                ["protect"] = new[] { "Cover Car" },
                ["collision wrap"] = new[] { "Cover Car" },
                ["backtape"] = new[] { "Cover Car" },
                ["jamb"] = new[] { "Cover Car" },

                // Welded Panel
                ["weld"] = new[] { "Welded Panel Ops", "Corrosion/Sealer" },
                ["welded"] = new[] { "Welded Panel Ops" },
                ["quarter panel"] = new[] { "Welded Panel Ops" },
                ["weld zone"] = new[] { "Welded Panel Ops" },
                ["dent puller"] = new[] { "Welded Panel Ops" },
                ["glue pull"] = new[] { "Welded Panel Ops" },
                ["stud"] = new[] { "Welded Panel Ops" },
                ["nutsert"] = new[] { "Welded Panel Ops" },
                ["rivet"] = new[] { "Welded Panel Ops" },

                // Glass
                ["glass"] = new[] { "Glass" },
                ["windshield"] = new[] { "Glass" },
                ["urethane"] = new[] { "Glass" },
                ["defrost"] = new[] { "Glass" },

                // Bolted Body Parts
                ["door"] = new[] { "Bolted Body Parts" },
                ["hood"] = new[] { "Bolted Body Parts" },
                ["liftgate"] = new[] { "Bolted Body Parts" },
                ["trunk"] = new[] { "Bolted Body Parts" },
                ["hinge"] = new[] { "Bolted Body Parts" },
                ["striker"] = new[] { "Bolted Body Parts" },
                ["latch"] = new[] { "Bolted Body Parts" },

                // SRS
                ["airbag"] = new[] { "SRS/Airbag" },
                ["srs"] = new[] { "SRS/Airbag" },
                ["seatbelt"] = new[] { "SRS/Airbag" },
                ["restraint"] = new[] { "SRS/Airbag" },

                // Misc
                ["wash"] = new[] { "Miscellaneous" },
                ["detail"] = new[] { "Miscellaneous" },
                ["clean"] = new[] { "Miscellaneous" },
                ["disposal"] = new[] { "Miscellaneous" },
                ["hazmat"] = new[] { "Miscellaneous" },
                ["biohazard"] = new[] { "Miscellaneous" },
                ["storage"] = new[] { "Miscellaneous" },
                ["oem research"] = new[] { "Miscellaneous" }
            };

            // Find matching categories
            var matchedCategories = new HashSet<string>();
            foreach (var alias in aliases)
            {
                if (search.Contains(alias.Key))
                {
                    foreach (var cat in alias.Value)
                        matchedCategories.Add(cat);
                }
            }

            // Also search operation names directly
            var directMatches = new List<(string Category, OperationItem Item)>();
            foreach (var cat in _operationCategories)
            {
                foreach (var item in cat.Value)
                {
                    if (item.Name.ToLowerInvariant().Contains(search))
                    {
                        directMatches.Add((cat.Key, item));
                        matchedCategories.Add(cat.Key);
                    }
                }
            }

            // Show matched categories
            if (matchedCategories.Count > 0)
            {
                foreach (var catName in matchedCategories)
                {
                    if (_operationCategories.TryGetValue(catName, out var items))
                    {
                        var expander = CreateCategoryExpander(catName, items, isExpanded: true);
                        _resultsPanel?.Children.Add(expander);
                    }
                }
            }
            else
            {
                // No matches - show hint
                _resultsPanel?.Children.Add(new TextBlock
                {
                    Text = $"No operations found for \"{search}\"\n\nTry: AC, ADAS, scan, alignment, battery, frame, paint, corrosion, airbag",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8)
                });
            }
        }

        private Expander CreateCategoryExpander(string categoryName, List<OperationItem> items, bool isExpanded = false)
        {
            var expander = new Expander
            {
                IsExpanded = isExpanded,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            // Header
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            headerStack.Children.Add(new TextBlock
            {
                Text = categoryName,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"({items.Count})",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center
            });
            expander.Header = headerStack;

            // Content - checkboxes for each operation
            var contentStack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };

            foreach (var item in items)
            {
                var itemGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var checkbox = new CheckBox
                {
                    Tag = item,
                    IsChecked = _selectedOperations.Contains(item),
                    MinWidth = 0
                };
                checkbox.Checked += (s, e) => AddSelectedOperation(item);
                checkbox.Unchecked += (s, e) => RemoveSelectedOperation(item);
                Grid.SetColumn(checkbox, 0);

                var nameText = new TextBlock
                {
                    Text = item.Name,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                Grid.SetColumn(nameText, 1);

                var detailsText = new TextBlock
                {
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 100)),
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Show actual labor/price data from Excel database
                var details = new List<string>();
                if (item.LaborHours > 0) details.Add($"{item.LaborHours:F1} hr");
                if (item.RefinishHours > 0) details.Add($"{item.RefinishHours:F1} rfn");
                if (item.Price > 0) details.Add($"${item.Price:N0}");
                if (item.IsMaterial) details.Add("Material");
                detailsText.Text = string.Join(" | ", details);
                Grid.SetColumn(detailsText, 2);

                itemGrid.Children.Add(checkbox);
                itemGrid.Children.Add(nameText);
                itemGrid.Children.Add(detailsText);

                contentStack.Children.Add(itemGrid);
            }

            // Select All button
            var selectAllBtn = new Button
            {
                Content = "Select All",
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(0, 8, 0, 0)
            };
            selectAllBtn.Click += (s, e) =>
            {
                foreach (var item in items)
                {
                    if (!_selectedOperations.Contains(item))
                        AddSelectedOperation(item);
                }
                // Refresh the expander
                expander.IsExpanded = false;
                expander.IsExpanded = true;
            };
            contentStack.Children.Add(selectAllBtn);

            expander.Content = contentStack;
            return expander;
        }

        private void AddSelectedOperation(OperationItem item)
        {
            if (!_selectedOperations.Contains(item))
            {
                _selectedOperations.Add(item);
                UpdateSelectedPanel();
            }
        }

        private void RemoveSelectedOperation(OperationItem item)
        {
            _selectedOperations.Remove(item);
            UpdateSelectedPanel();
        }

        private void UpdateSelectedPanel()
        {
            _selectedPanel?.Children.Clear();

            if (_selectedOperations.Count == 0)
            {
                _selectedPanel?.Children.Add(new TextBlock
                {
                    Text = "(none)",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }
            else
            {
                foreach (var item in _selectedOperations)
                {
                    var itemText = item.Name;
                    if (item.LaborHours > 0) itemText += $" ({item.LaborHours:F1} hr)";
                    if (item.Price > 0) itemText += $" (${item.Price:N0})";
                    if (item.IsMaterial) itemText += " [Material]";

                    _selectedPanel?.Children.Add(new TextBlock
                    {
                        Text = $"+ {itemText}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 220, 180))
                    });
                }
            }

            // Update status with totals from Excel data
            var totalLabor = _selectedOperations.Sum(o => o.LaborHours);
            var totalRefinish = _selectedOperations.Sum(o => o.RefinishHours);
            var totalPrice = _selectedOperations.Sum(o => o.Price);
            if (_statusText != null)
            {
                var status = $"{_selectedOperations.Count} operation(s)";
                if (totalLabor > 0) status += $" | {totalLabor:F1} hr labor";
                if (totalRefinish > 0) status += $" | {totalRefinish:F1} hr refinish";
                if (totalPrice > 0) status += $" | ${totalPrice:N0}";
                _statusText.Text = status;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedOperations.Clear();
            UpdateSelectedPanel();

            // Refresh categories to uncheck all
            if (!string.IsNullOrWhiteSpace(_searchBox?.Text))
            {
                FilterOperations(_searchBox.Text.ToLowerInvariant());
            }
            else
            {
                ShowAllCategories();
            }
        }

        private async void CopyForSystem(string system)
        {
            if (_selectedOperations.Count == 0)
            {
                if (_statusText != null) _statusText.Text = "No operations selected";
                return;
            }

            var lines = new List<string>();

            foreach (var item in _selectedOperations)
            {
                // Determine operation type based on category
                string opType = GetOperationType(item);
                string qty = "1";
                string price = item.Price > 0 ? item.Price.ToString("F2") : "";
                string labor = item.LaborHours > 0 ? item.LaborHours.ToString("F1") : "";
                string paint = item.RefinishHours > 0 ? item.RefinishHours.ToString("F1") : "";

                string line;
                switch (system)
                {
                    case "CCC Desktop":
                        // CCC Desktop Full Format with placeholder zeros (Excel-like for paste)
                        // 0  0  0  0  0  0  OpType  0  Description  0  Qty  Price  0  0  0  Labor  0  Refinish
                        line = string.Join("\t",
                            "0", "0", "0", "0", "0", "0",
                            opType,
                            "0",
                            item.Name,
                            "0",
                            qty,
                            string.IsNullOrEmpty(price) ? "0" : price,
                            "0", "0", "0",
                            string.IsNullOrEmpty(labor) ? "0" : labor,
                            "0",
                            string.IsNullOrEmpty(paint) ? "0" : paint
                        );
                        break;

                    case "CCC Web":
                        // CCC Web - simpler format
                        line = $"{opType}\t{item.Name}\t{qty}\t{price}\t{labor}\t{paint}";
                        break;

                    case "Mitchell":
                        // Mitchell Format: Operation, Description, Qty, Labor, Paint, Price
                        line = $"{opType}\t{item.Name}\t{qty}\t{labor}\t{paint}\t{price}";
                        break;

                    default: // Plain
                        line = $"{item.Name}\t{labor}\t{price}\t{item.Category}";
                        break;
                }
                lines.Add(line);
            }

            var text = string.Join("\r\n", lines);
            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);

            // Show feedback
            if (_statusText != null)
            {
                _statusText.Text = $"Copied {_selectedOperations.Count} ops for {system}";
            }

            if (_copyButton != null)
            {
                var origContent = _copyButton.Content;
                _copyButton.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE73E", FontSize = 12 },
                        new TextBlock { Text = $"Copied for {system}!", FontSize = 12 }
                    }
                };
                _copyButton.Background = new SolidColorBrush(Color.FromArgb(255, 0, 150, 80));

                await System.Threading.Tasks.Task.Delay(1500);

                _copyButton.Content = origContent;
                _copyButton.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 180));
            }
        }

        /// <summary>
        /// Export operations directly to the estimating system using automation
        /// </summary>
        private async void ExportToSystem(SmartExportService.ExportTarget target)
        {
            if (_selectedOperations.Count == 0)
            {
                if (_statusText != null) _statusText.Text = "No operations selected";
                return;
            }

            // Convert selected operations to SmartExportOp format
            var exportOps = _selectedOperations.Select(item => new SmartExportOp
            {
                OperationType = GetOperationType(item),
                Description = item.Name,
                Quantity = 1,
                Price = item.Price,
                LaborHours = item.LaborHours,
                RefinishHours = item.RefinishHours,
                Category = item.Category
            }).ToList();

            // Auto-detect if no target specified
            if (target == SmartExportService.ExportTarget.None)
            {
                var detected = _exportService.GetBestTarget();
                if (detected == null)
                {
                    if (_statusText != null)
                        _statusText.Text = "No estimating system detected. Open CCC or Mitchell first.";
                    return;
                }
                target = detected.Target;
                if (_statusText != null)
                    _statusText.Text = $"Detected: {detected.Title}";
            }

            if (_statusText != null)
                _statusText.Text = $"Exporting {_selectedOperations.Count} ops to {target}...";

            // Subscribe to progress updates
            _exportService.ProgressChanged += (s, e) =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (_statusText != null)
                        _statusText.Text = e.Message;
                });
            };

            var result = await _exportService.ExportAsync(exportOps, target);

            DispatcherQueue?.TryEnqueue(() =>
            {
                if (_statusText != null)
                {
                    _statusText.Text = result.Success
                        ? $"Exported {result.ExportedCount} operations"
                        : $"Error: {result.Message}";
                }
            });
        }

        /// <summary>
        /// Determine the operation type (Rpr, Add, R&I, Refinish, etc.) based on category
        /// </summary>
        private string GetOperationType(OperationItem item)
        {
            // Materials are typically "Add" type
            if (item.IsMaterial) return "Add";

            // Determine based on category and name
            var name = item.Name.ToLowerInvariant();
            var category = item.Category.ToLowerInvariant();

            if (category == "refinish") return "Refinish";
            if (category == "frame") return "F"; // Frame labor type
            if (category == "diagnostic") return "Scan";

            // Check name for clues
            if (name.Contains("r&i") || name.Contains("r+i") || name.Contains("remove and install")) return "R&I";
            if (name.Contains("replace")) return "Replace";
            if (name.Contains("setup")) return "Rpr";
            if (name.Contains("calibrat")) return "Sublet";
            if (name.Contains("scan") || name.Contains("diagnostic")) return "Scan";
            if (name.Contains("measure")) return "F";

            // Default based on category
            return category switch
            {
                "body" => "Rpr",
                "mechanical" => "M",
                "frame" => "F",
                _ => "Rpr"
            };
        }

        /// <summary>
        /// Update the position indicator to show current export position status
        /// </summary>
        private void UpdatePositionIndicator()
        {
            if (_positionIndicator == null || _positionText == null) return;

            var cccService = McstudDesktop.Services.CCCInsertService.Instance;

            if (cccService.HasClickPosition)
            {
                // Position is set - show green indicator
                _positionIndicator.Background = new SolidColorBrush(Color.FromArgb(255, 30, 50, 40));
                _positionText.Text = $"Export position set: {cccService.TargetWindowTitle}";
                _positionText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150));

                // Update icon color too
                if (_positionIndicator.Child is StackPanel stack && stack.Children.Count > 0 && stack.Children[0] is FontIcon icon)
                {
                    icon.Glyph = "\uE73E"; // Checkmark
                    icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150));
                }
            }
            else
            {
                // No position set - show amber indicator
                _positionIndicator.Background = new SolidColorBrush(Color.FromArgb(255, 50, 40, 30));
                _positionText.Text = "Click in CCC where you want to insert, then come back here";
                _positionText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 170, 90));

                // Update icon
                if (_positionIndicator.Child is StackPanel stack && stack.Children.Count > 0 && stack.Children[0] is FontIcon icon)
                {
                    icon.Glyph = "\uE81C"; // Target crosshairs
                    icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 170, 90));
                }
            }
        }

        // Data class for operations - loaded from Excel database
        private class OperationItem
        {
            public string Name { get; set; }
            public string Category { get; set; }
            public bool IsMaterial { get; set; }
            public string ExcelKey { get; set; } // Reference to Excel mapping key

            // Data loaded from Excel database
            public decimal LaborHours { get; set; }
            public decimal Price { get; set; }
            public decimal RefinishHours { get; set; }
            public int ExcelRow { get; set; }

            public OperationItem(string name, string category, string excelKey)
            {
                Name = name;
                Category = category;
                ExcelKey = excelKey;
            }
        }
    }
}
