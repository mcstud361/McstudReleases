#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Windows.UI;
using McStudDesktop.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace McStudDesktop.Views;

/// <summary>
/// Excel-like spreadsheet view for the McStud Estimating Tool
/// Matches the master.xlsx structure exactly with input cells and output cells
/// </summary>
public class EstimatingToolView_NEW : Grid
{
    private Estimate _currentEstimate;
    private TabView? _tabView;
    private TextBlock? _totalsSummary;
    private ScrollViewer? _currentScrollViewer;
    private Dictionary<string, int> _sectionRowIndices = new Dictionary<string, int>();
    private Grid? _currentGrid;
    private string _currentSOPSection = "Electrical"; // Default to Electrical
    private Grid? _contentContainer;

    // Cache for tab content to prevent data loss when switching tabs
    private Dictionary<string, UIElement> _tabContentCache = new Dictionary<string, UIElement>();
    // Store the grids for SOP sections to toggle visibility
    private Dictionary<string, Grid> _sopSectionGrids = new Dictionary<string, Grid>();

    // SOP List input controls (stored for cross-row formulas)
    private ComboBox? _batteryTypeCombo;      // A29
    private ComboBox? _adasCombo;             // C29
    private ComboBox? _testBatteryCombo;      // A31
    private ComboBox? _batterySupportCombo;   // A33
    private ComboBox? _vehicleTypeCombo;      // A35
    private ComboBox? _scanToolTypeCombo;     // A79
    private ComboBox? _setupScanToolCombo;    // A81
    private TextBox? _customPriceText;        // A83
    private TextBox? _customLaborText;        // A85
    private ComboBox? _gatewayUnlockCombo;    // A87
    private ComboBox? _adasDiagnosticCombo;   // B79
    private ComboBox? _simulateFluidsCombo;   // B81
    private ComboBox? _checkTirePressureCombo;// B83
    private ComboBox? _removesBelongingsCombo;// B85
    private ComboBox? _driveCycleCombo;       // B87
    private ComboBox? _preWashCombo;          // A129
    private TextBox? _collisionWrapText;      // A131
    private ComboBox? _bioHazardCombo;        // A133
    private TextBox? _ipaWipeText;            // B129
    private ComboBox? _shippingLabelsCombo;   // C129
    private ComboBox? _scaffoldingCombo;      // D129

    // Operation output cells (for totals calculation)
    private List<OperationRow> _operationRows = new List<OperationRow>();

    private class OperationRow
    {
        public TextBlock? OperationType { get; set; }
        public TextBlock? Description { get; set; }
        public TextBlock? Quantity { get; set; }
        public TextBlock? Price { get; set; }
        public TextBlock? Labor { get; set; }
        public TextBlock? Category { get; set; }
        public TextBlock? Refinish { get; set; }
    }

    public event EventHandler? BackToMenu;

    public EstimatingToolView_NEW()
    {
        _currentEstimate = new Estimate();
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20));

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) }); // Header
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) }); // Footer

        CreateHeader();
        CreateTabView();
        CreateFooter();
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
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            Margin = new Thickness(0)
        };

        string[] tabs = {
            "SOP List", "Part Operations", "Cover Car", "Body",
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

        _tabView.SelectedIndex = 0; // Start with SOP List
        _tabView.SelectionChanged += TabView_SelectionChanged;

        Grid.SetRow(_tabView, 1);
        Children.Add(_tabView);

        RenderCurrentTab();
    }

    private void TabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderCurrentTab();
    }

    private void RenderCurrentTab()
    {
        if (_tabView?.SelectedItem is not TabViewItem selectedTab) return;

        var tabName = selectedTab.Header?.ToString() ?? "";

        // Check cache first - if we already built this tab, use it to preserve inputs!
        if (_tabContentCache.TryGetValue(tabName, out var cachedContent))
        {
            selectedTab.Content = cachedContent;
            return;
        }

        // Reset to default section when switching to SOP List tab
        if (tabName == "SOP List" && _currentSOPSection != "Electrical")
        {
            _currentSOPSection = "Electrical";
        }

        // Create main container with sidebar
        var mainContainer = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30))
        };

        mainContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); // Sidebar
        mainContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content

        // Get sections for this tab
        string[]? sections = tabName switch
        {
            "SOP List" => new[] { "Electrical", "Vehicle Diagnostics", "Misc" },
            "Part Operations" => new[] { "Blend Operations", "Repair Operations", "R&I Operations" },
            "Cover Car" => new[] { "Masking", "Covering", "Protection" },
            "Body" => new[] { "Structural", "Equipment", "Measurements" },
            "Refinish" => new[] { "Paint", "Clear Coat", "Color Matching" },
            "Mechanical" => new[] { "AC & Cooling", "Suspension", "Wheel & Tire" },
            "SRS" => new[] { "Safety Inspections", "Airbag Systems" },
            "Total Loss" => new[] { "Fees", "Coordination", "Storage" },
            "Body on Frame" => new[] { "Frame Operations", "Checklist" },
            "Stolen Recovery" => new[] { "Recovery Operations", "Inspection" },
            "Post Repair" => new[] { "Final Inspection" },
            "Summary" => null, // No sidebar for summary
            _ => null
        };

        // Create sidebar if sections exist
        if (sections != null && sections.Length > 0)
        {
            var sidebar = CreateSidebar(sections, tabName);
            Grid.SetColumn(sidebar, 0);
            mainContainer.Children.Add(sidebar);
        }

        // Create scrollable grid container for content
        _currentScrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Padding = new Thickness(0)
        };

        Grid? contentGrid = tabName switch
        {
            "SOP List" => CreateSOPListGrid(),
            "Part Operations" => CreatePartOperationsGrid(),
            "Cover Car" => CreateCoverCarGrid(),
            "Body" => CreateBodyGrid(),
            "Refinish" => CreateRefinishGrid(),
            "Mechanical" => CreateMechanicalGrid(),
            "SRS" => CreateSRSGrid(),
            "Total Loss" => CreateTotalLossGrid(),
            "Body on Frame" => CreateBodyOnFrameGrid(),
            "Stolen Recovery" => CreateStolenRecoveryGrid(),
            "Post Repair" => CreatePostRepairGrid(),
            "Summary" => CreateSummaryGrid(),
            _ => null
        };

        if (contentGrid != null)
        {
            _currentScrollViewer.Content = contentGrid;
            _currentGrid = contentGrid;
        }

        Grid.SetColumn(_currentScrollViewer, sections != null && sections.Length > 0 ? 1 : 0);
        if (sections == null || sections.Length == 0)
        {
            Grid.SetColumnSpan(_currentScrollViewer, 2);
        }
        mainContainer.Children.Add(_currentScrollViewer);

        // Cache the created content
        _tabContentCache[tabName] = mainContainer;

        selectedTab.Content = mainContainer;
    }

    private Grid CreateSidebar(string[] sections, string tabName)
    {
        var sidebar = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(10, 20, 10, 20)
        };

        var stackPanel = new StackPanel
        {
            Spacing = 8
        };

        // Add title
        var titleText = new TextBlock
        {
            Text = "Sections",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            Margin = new Thickness(0, 0, 0, 15)
        };
        stackPanel.Children.Add(titleText);

        // Add section buttons
        foreach (var section in sections)
        {
            var sectionName = section; // Capture correctly
            var button = new Button
            {
                Content = sectionName,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(15, 10, 15, 10),
                FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            button.Click += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Sidebar] Button clicked: {sectionName}");
                ScrollToSection(sectionName);
            };

            button.PointerEntered += (s, e) =>
            {
                button.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
            };

            button.PointerExited += (s, e) =>
            {
                button.Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35));
            };

            stackPanel.Children.Add(button);
        }

        sidebar.Children.Add(stackPanel);
        return sidebar;
    }

    private void ScrollToSection(string sectionName)
    {
        // Update the current section
        _currentSOPSection = sectionName;

        // Toggle visibility of the grids
        if (_sopSectionGrids.Count > 0)
        {
            foreach (var kvp in _sopSectionGrids)
            {
                kvp.Value.Visibility = kvp.Key == sectionName ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        else
        {
            // Fallback for tabs that don't use the new pre-build system yet
            RenderCurrentTab();
        }
    }

    private Grid CreatePartOperationsGrid()
    {
        return CreatePlaceholderGrid("Part Operations", "Part-specific operations including blend, repair, and replacement procedures.");
    }

    private Grid CreateCoverCarGrid()
    {
        return CreatePlaceholderGrid("Cover Car", "Masking and covering procedures for overspray protection.");
    }

    private Grid CreateBodyGrid()
    {
        return CreatePlaceholderGrid("Body", "Body equipment and structural repair operations.");
    }

    private Grid CreateRefinishGrid()
    {
        return CreatePlaceholderGrid("Refinish", "Paint and refinish procedures with paint stage configuration.");
    }

    private Grid CreateMechanicalGrid()
    {
        return CreatePlaceholderGrid("Mechanical", "AC, cooling, suspension, and general mechanical work.");
    }

    private Grid CreateSRSGrid()
    {
        return CreatePlaceholderGrid("SRS", "Safety restraint system (airbag) operations and inspections.");
    }

    private Grid CreateTotalLossGrid()
    {
        return CreatePlaceholderGrid("Total Loss", "Administrative fees, coordination charges, and handling fees.");
    }

    private Grid CreateBodyOnFrameGrid()
    {
        return CreatePlaceholderGrid("Body on Frame", "Body-on-frame specific operations for truck/SUV frames.");
    }

    private Grid CreateStolenRecoveryGrid()
    {
        return CreatePlaceholderGrid("Stolen Recovery", "Stolen vehicle recovery operations and inspections.");
    }

    private Grid CreatePostRepairGrid()
    {
        return CreatePlaceholderGrid("Post Repair", "Final inspection checklist before vehicle delivery.");
    }

    private Grid CreatePlaceholderGrid(string tabName, string description)
    {
        var grid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Padding = new Thickness(60)
        };

        var stackPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 20
        };

        var titleText = new TextBlock
        {
            Text = tabName,
            FontSize = 32,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var descText = new TextBlock
        {
            Text = description,
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 600
        };

        var statusText = new TextBlock
        {
            Text = "🔨 This tab is being built with an Excel-like grid structure matching your master.xlsx file.",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 600,
            Margin = new Thickness(0, 10, 0, 0)
        };

        stackPanel.Children.Add(titleText);
        stackPanel.Children.Add(descText);
        stackPanel.Children.Add(statusText);
        grid.Children.Add(stackPanel);

        return grid;
    }

    private Grid CreateSummaryGrid()
    {
        var grid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Padding = new Thickness(60)
        };

        var summaryText = new TextBlock
        {
            Text = $"📊 Total Operations: {_currentEstimate.TotalOperationsCount}\n" +
                   $"💲 Total Price: ${_currentEstimate.TotalPrice:F2}\n" +
                   $"🛠 Total Labor Hours: {_currentEstimate.TotalLaborHours:F1}\n" +
                   $"🎨 Total Refinish Hours: {_currentEstimate.TotalRefinishHours:F1}",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            LineHeight = 45,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        grid.Children.Add(summaryText);
        return grid;
    }

    private Grid CreateSOPListGrid()
    {
        // Removed debug logs for performance
        _operationRows.Clear();
        _sopSectionGrids.Clear();

        // Master container that holds headers and the swappable content
        var masterGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Padding = new Thickness(20, 10, 20, 10)
        };

        // Define columns for the master grid
        // INPUTS (Cols 0-3) | SPACER (Col 4) | OUTPUTS (Cols 5-11)
        // Adjusted for better fit and alignment
        double[] columnWidths = {
            160, // [0] Input A: Primary Selection
            140, // [1] Input B: Options/Labor
            110, // [2] Input C: ADAS/Notes
            110, // [3] Input D: Equipment
            10,  // [4] Spacer (Minimal gap)
            70,  // [5] Operation Type
            320, // [6] Description
            70,  // [7] Quantity
            80,  // [8] Price
            80,  // [9] Labor
            70,  // [10] Category
            80   // [11] Refinish
        };

        for (int i = 0; i < columnWidths.Length; i++)
        {
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnWidths[i]) });
        }

        // Rows for Master Grid:
        // 0: Summary
        // 1: Headers
        // 2: Content Area (Where we swap sections)
        masterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        masterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        masterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        int masterRowIndex = 0;

        // Row 0: Summary header
        AddSummaryRow(masterGrid, masterRowIndex++);

        // Row 1: Column headers
        AddColumnHeaders(masterGrid, masterRowIndex++);

        // --- BUILD ALL SECTIONS NOW ---
        // We build them all into separate Grids and stack them in Row 2.

        // Helper to setup a section grid with same columns
        Grid CreateSectionGrid()
        {
            var g = new Grid();
            for (int i = 0; i < columnWidths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnWidths[i]) });

            // Add enough rows definition
            for (int i = 0; i < 30; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(g, masterRowIndex); // Place in the content row of master grid
            Grid.SetColumn(g, 0);
            Grid.SetColumnSpan(g, 12); // Span all new columns
            return g;
        }

        // 1. Electrical
        var electricalGrid = CreateSectionGrid();
        int row = 0;
        AddElectricalSection(electricalGrid, ref row);
        _sopSectionGrids["Electrical"] = electricalGrid;
        masterGrid.Children.Add(electricalGrid);

        // 2. Vehicle Diagnostics
        var diagGrid = CreateSectionGrid();
        row = 0; // Reset row counter for new grid
        AddVehicleDiagnosticsSection(diagGrid, ref row);
        _sopSectionGrids["Vehicle Diagnostics"] = diagGrid;
        masterGrid.Children.Add(diagGrid);

        // 3. Misc
        var miscGrid = CreateSectionGrid();
        row = 0;
        AddMiscSection(miscGrid, ref row);
        _sopSectionGrids["Misc"] = miscGrid;
        masterGrid.Children.Add(miscGrid);

        // Set initial visibility
        foreach (var kvp in _sopSectionGrids)
        {
            kvp.Value.Visibility = kvp.Key == _currentSOPSection ? Visibility.Visible : Visibility.Collapsed;
        }

        return masterGrid;
    }

    private void AddElectricalSection(Grid grid, ref int rowIndex)
    {
        // Section header
        AddSectionHeader(grid, rowIndex++, "▼ ELECTRICAL OPERATIONS");

        // Battery Disconnect (Row 29 in Excel)
        AddRowTitle(grid, rowIndex++, "Battery Disconnect & Reconnect");
        AddRow_BatteryDisconnect(grid, rowIndex++);

        // Row 4: Test Battery header
        AddSectionHeader(grid, rowIndex++, "▼ Test Battery");

        // Row 5: Test Battery Condition (Row 30)
        AddRowTitle(grid, rowIndex++, "Test Battery Condition");
        AddRow_TestBatteryCondition(grid, rowIndex++);

        // Row 6: Electronic Reset (Row 31)
        AddRow_ElectronicReset(grid, rowIndex++);

        // Row 7: Cover and Protect Electrical (Row 32)
        AddRow_CoverElectrical(grid, rowIndex++);

        // Row 8: Battery Support header
        AddSectionHeader(grid, rowIndex++, "▼ Battery Support");

        // Row 9: Battery Support (Row 33)
        AddRowTitle(grid, rowIndex++, "Battery Support");
        AddRow_BatterySupport(grid, rowIndex++);

        // Row 10: Vehicle Type header
        AddSectionHeader(grid, rowIndex++, "▼ Vehicle Type");

        // Row 11: Vehicle Type input
        AddRowTitle(grid, rowIndex++, "Vehicle Type Selection");
        AddRow_VehicleTypeInput(grid, rowIndex++);

        // Row 12: Charge and Maintain Battery (Row 34)
        AddRow_ChargeMaintainBattery(grid, rowIndex++);

        // Row 13: Mobile Cart (Row 35)
        AddRow_MobileCart(grid, rowIndex++);

        // Row 14: Verify No High Voltage (Row 36)
        AddRow_VerifyNoHighVoltage(grid, rowIndex++);

        // Service Mode (Row 37)
        AddRow_ServiceMode(grid, rowIndex++);
    }

    private void AddVehicleDiagnosticsSection(Grid grid, ref int rowIndex)
    {
        // Section header
        AddSectionHeader(grid, rowIndex++, "▼ VEHICLE DIAGNOSTICS");

        // Scan tool operations (Rows 79-81)
        AddRowTitle(grid, rowIndex++, "Pre/Post Scan Tool Selection");
        AddRow_PreScan(grid, rowIndex++);
        AddRow_InProcessScan(grid, rowIndex++);
        AddRow_PostScan(grid, rowIndex++);

        // Setup and diagnostic operations (Rows 82-86)
        AddRowTitle(grid, rowIndex++, "Setup Scan Tool");
        AddRow_SetupScanTool(grid, rowIndex++);

        // Dynamic systems (Output only)
        AddRow_DynamicSystemsVerification(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "OEM Research & Procedures (Manual Entry)");
        AddRow_OEMResearch(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "Gateway Unlock");
        AddRow_GatewayUnlock(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "ADAS Diagnostic Report");
        AddRow_ADASDiagnosticReport(grid, rowIndex++);

        // ADAS support operations (Rows 88-91)
        AddRowTitle(grid, rowIndex++, "Simulate Fluids");
        AddRow_SimulateFluids(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "Check Tire Pressure");
        AddRow_CheckTirePressure(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "Remove Belongings");
        AddRow_RemoveBelongings(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "Drive Cycle Verification");
        AddRow_DriveCycle(grid, rowIndex++);
    }

    private void AddMiscSection(Grid grid, ref int rowIndex)
    {
        // Section header
        AddSectionHeader(grid, rowIndex++, "▼ MISCELLANEOUS");

        // Static items (Cleaning, etc)
        AddRow_CleanForDelivery(grid, rowIndex++);
        AddRow_GlassCleaner(grid, rowIndex++);
        AddRow_MaskProtectComponents(grid, rowIndex++);
        AddRow_PartsDisposal(grid, rowIndex++);
        AddRow_HazardousWaste(grid, rowIndex++);
        AddRow_MiscHardware(grid, rowIndex++);
        AddRow_SteeringWheelCover(grid, rowIndex++);

        // Input items with titles
        AddRowTitle(grid, rowIndex++, "Pre Wash");
        AddRow_PreWash(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "Collision Wrap");
        AddRow_CollisionWrap(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "Bio Hazard");
        AddRow_BioHazard(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "IPA Wipe");
        AddRow_IPAWipe(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "Remove Shipping Labels");
        AddRow_RemoveShippingLabels(grid, rowIndex++);

        AddRowTitle(grid, rowIndex++, "Scaffolding");
        AddRow_Scaffolding(grid, rowIndex++);
    }

    private void AddSummaryRow(Grid grid, int rowIndex)
    {
        var summaryCell = CreateCell("📊 0 Ops  |  💲 $0.00  |  🛠 0.0 Labor  |  🎨 0.0 Refinish",
            isBold: true, isHeader: false);
        Grid.SetRow(summaryCell, rowIndex);
        Grid.SetColumn(summaryCell, 5); // Start at Operation Type column
        Grid.SetColumnSpan(summaryCell, 7); // Span across all output columns
        grid.Children.Add(summaryCell);
    }

    private void AddSectionHeader(Grid grid, int rowIndex, string title)
    {
        var headerCell = CreateCell(title, isBold: true, isHeader: false);
        Grid.SetRow(headerCell, rowIndex);
        Grid.SetColumn(headerCell, 0); // Column A
        Grid.SetColumnSpan(headerCell, 4); // Span all 4 input columns
        grid.Children.Add(headerCell);
    }

    private void AddColumnHeaders(Grid grid, int rowIndex)
    {
        // Updated headers to be generic across all sections
        var headers = new[]
        {
            (0, "Selection / Input"),    // Column A
            (1, "Options / Details"),    // Column B
            (2, "ADAS / Notes"),         // Column C
            (3, "Equipment"),            // Column D
            (5, "Operation"),            // Column M
            (6, "Description"),          // Column O
            (7, "Quantity"),             // Column Q
            (8, "Price"),                // Column R
            (9, "Labor"),                // Column V
            (10, "Category"),            // Column W
            (11, "Refinish")             // Column X
        };

        foreach (var (col, text) in headers)
        {
            var cell = CreateCell(text, isBold: true, isHeader: true);
            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
        }
    }

    // Helper to add a simple title row above an input
    private void AddRowTitle(Grid grid, int rowIndex, string title)
    {
        var textBlock = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2, 5, 0, 2)
        };
        Grid.SetRow(textBlock, rowIndex);
        Grid.SetColumn(textBlock, 0);
        Grid.SetColumnSpan(textBlock, 12); // Span across all columns for consistent vertical layout
        grid.Children.Add(textBlock);
    }

    // BATTERY/ELECTRICAL OPERATIONS

    private void AddRow_BatteryDisconnect(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Battery Type
        _batteryTypeCombo = CreateComboBox(new[] { "", "Single", "Dual" }, isInputCell: true, placeholder: "Battery Type");
        _batteryTypeCombo.SelectedIndex = 0;
        Grid.SetRow(_batteryTypeCombo, rowIndex);
        Grid.SetColumn(_batteryTypeCombo, 0);
        grid.Children.Add(_batteryTypeCombo);

        // INPUT: Column C - ADAS
        _adasCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "ADAS Required?");
        _adasCombo.SelectedIndex = 0;
        Grid.SetRow(_adasCombo, rowIndex);
        Grid.SetColumn(_adasCombo, 2);
        grid.Children.Add(_adasCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(A29="Single", ..., IF(A29="Dual", ..., ""))
        _batteryTypeCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_batteryTypeCombo);
            System.Diagnostics.Debug.WriteLine($"[Battery Disconnect] Selected: '{selected}'");
            if (selected == "Single")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Disconnect and Reconnect Battery";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.4";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else if (selected == "Dual")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Disconnect and Reconnect 2x Battery";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.8";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

﻿
    // BATTERY/ELECTRICAL OPERATIONS (continued)

    private void AddRow_TestBatteryCondition(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Test Battery Yes/No
        _testBatteryCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Test Condition?");
        _testBatteryCombo.SelectedIndex = 0;
        Grid.SetRow(_testBatteryCombo, rowIndex);
        Grid.SetColumn(_testBatteryCombo, 0);
        grid.Children.Add(_testBatteryCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(A31="Yes", ...)
        _testBatteryCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_testBatteryCombo);
            if (selected == "Yes")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Test Battery Condition";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.2";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_ElectronicReset(Grid grid, int rowIndex)
    {
        // Static outputs (always shown)
        var row = CreateOperationRow("Rpr", "Electronic Reset", "1", "0", "0.5", "M", "0");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);
        // No update needed - static values
    }

    private void AddRow_CoverElectrical(Grid grid, int rowIndex)
    {
        // Static outputs (always shown)
        var row = CreateOperationRow("Rpr", "Cover and Protect Electrical Connections", "1", "5", "0.3", "M", "0");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);
        // No update needed - static values
    }

    private void AddRow_BatterySupport(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Battery Support Yes/No
        _batterySupportCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Battery Support?");
        _batterySupportCombo.SelectedIndex = 0;
        Grid.SetRow(_batterySupportCombo, rowIndex);
        Grid.SetColumn(_batterySupportCombo, 0);
        grid.Children.Add(_batterySupportCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(A33="Yes", ...)
        _batterySupportCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_batterySupportCombo);
            if (selected == "Yes")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Battery Support";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.2";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_VehicleTypeInput(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Vehicle Type
        _vehicleTypeCombo = CreateComboBox(new[] { "", "Gas", "Hybrid", "EV" }, isInputCell: true, placeholder: "Vehicle Type");
        _vehicleTypeCombo.SelectedIndex = 0;
        Grid.SetRow(_vehicleTypeCombo, rowIndex);
        Grid.SetColumn(_vehicleTypeCombo, 0);
        grid.Children.Add(_vehicleTypeCombo);

        // This is an input-only row - no direct outputs
        // But we need to trigger updates when it changes
        _vehicleTypeCombo.SelectionChanged += (s, e) =>
        {
            // This will trigger updates in dependent rows
            UpdateTotals();
        };
    }

    private void AddRow_ChargeMaintainBattery(Grid grid, int rowIndex)
    {
        // OUTPUTS (depend on vehicle type and ADAS)
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // Update function to be called when inputs change
        Action updateRow = () =>
        {
            var vehicleType = GetComboValue(_vehicleTypeCombo);
            var adas = GetComboValue(_adasCombo);

            // FORMULA: IF(OR(A35="EV", C29="Yes"), ...)
            if (vehicleType == "EV" || adas == "Yes")
            {
                row.OperationType!.Text = "Rpr";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.6";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";

                // Description varies
                if (vehicleType == "EV")
                {
                    row.Description!.Text = "Charge and Maintain Battery";
                }
                else if (adas == "Yes" && (vehicleType == "Gas" || vehicleType == "Hybrid"))
                {
                    row.Description!.Text = "Charge and Maintain Battery during ADAS";
                }
                else
                {
                    ClearOperationRow(row);
                }
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };

        // Attach to input changes
        if (_vehicleTypeCombo != null) _vehicleTypeCombo.SelectionChanged += (s, e) => updateRow();
        if (_adasCombo != null) _adasCombo.SelectionChanged += (s, e) => updateRow();
    }

    private void AddRow_MobileCart(Grid grid, int rowIndex)
    {
        // OUTPUTS (depend on vehicle type)
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // Update function
        Action updateRow = () =>
        {
            var vehicleType = GetComboValue(_vehicleTypeCombo);

            // FORMULA: IF(A35="Hybrid", ..., IF(A35="EV", ...))
            if (vehicleType == "Hybrid" || vehicleType == "EV")
            {
                row.OperationType!.Text = "Replace";
                row.Quantity!.Text = "1";
                row.Price!.Text = "50";
                row.Labor!.Text = "0.5";
                row.Category!.Text = "0";
                row.Refinish!.Text = "0";

                if (vehicleType == "Hybrid")
                    row.Description!.Text = "Mobile Cart for Hybrid";
                else
                    row.Description!.Text = "Mobile Cart for EV";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };

        // Attach to input changes
        if (_vehicleTypeCombo != null) _vehicleTypeCombo.SelectionChanged += (s, e) => updateRow();
    }

    private void AddRow_VerifyNoHighVoltage(Grid grid, int rowIndex)
    {
        // OUTPUTS (depend on vehicle type)
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // Update function
        Action updateRow = () =>
        {
            var vehicleType = GetComboValue(_vehicleTypeCombo);

            // FORMULA: IF(OR(A35="Hybrid", A35="EV"), ...)
            if (vehicleType == "Hybrid" || vehicleType == "EV")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Verify No High Voltage";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.2";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };

        // Attach to input changes
        if (_vehicleTypeCombo != null) _vehicleTypeCombo.SelectionChanged += (s, e) => updateRow();
    }

    private void AddRow_ServiceMode(Grid grid, int rowIndex)
    {
        // OUTPUTS (depend on vehicle type - EV only)
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // Update function
        Action updateRow = () =>
        {
            var vehicleType = GetComboValue(_vehicleTypeCombo);

            // FORMULA: IF(A35="EV", ...)
            if (vehicleType == "EV")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Service Mode";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.1";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };

        // Attach to input changes
        if (_vehicleTypeCombo != null) _vehicleTypeCombo.SelectionChanged += (s, e) => updateRow();
    }

    // VEHICLE DIAGNOSTICS OPERATIONS

    private void AddRow_PreScan(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Scan Tool Type
        _scanToolTypeCombo = CreateComboBox(new[] { "", "Gas", "Rivian", "Tesla" }, isInputCell: true, placeholder: "Scan Tool Type");
        _scanToolTypeCombo.SelectedIndex = 0;
        Grid.SetRow(_scanToolTypeCombo, rowIndex);
        Grid.SetColumn(_scanToolTypeCombo, 0);
        grid.Children.Add(_scanToolTypeCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: Different values based on scan tool type
        _scanToolTypeCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_scanToolTypeCombo);
            row.Quantity!.Text = "1";
            row.Refinish!.Text = "0";
            row.Category!.Text = "M";

            if (selected == "Gas")
            {
                row.OperationType!.Text = "Replace";
                row.Description!.Text = "Pre-Scan";
                row.Price!.Text = "150";
                row.Labor!.Text = "0";
            }
            else if (selected == "Rivian")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Pre-Scan";
                row.Price!.Text = "0";
                row.Labor!.Text = "1";
            }
            else if (selected == "Tesla")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Trim to Access Scanner";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.2";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_InProcessScan(Grid grid, int rowIndex)
    {
        // OUTPUTS (depend on scan tool type)
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // Update function
        Action updateRow = () =>
        {
            var selected = GetComboValue(_scanToolTypeCombo);
            row.Quantity!.Text = "1";
            row.Refinish!.Text = "0";
            row.Category!.Text = "M";

            if (selected == "Gas")
            {
                row.OperationType!.Text = "Replace";
                row.Description!.Text = "In-Process Scan";
                row.Price!.Text = "150";
                row.Labor!.Text = "0";
            }
            else if (selected == "Rivian")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "In-Process Scan";
                row.Price!.Text = "0";
                row.Labor!.Text = "1";
            }
            else if (selected == "Tesla")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Tesla Toolbox Scan";
                row.Price!.Text = "0";
                row.Labor!.Text = "1";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };

        // Attach to input changes
        if (_scanToolTypeCombo != null) _scanToolTypeCombo.SelectionChanged += (s, e) => updateRow();
    }

    private void AddRow_PostScan(Grid grid, int rowIndex)
    {
        // OUTPUTS (depend on scan tool type)
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // Update function
        Action updateRow = () =>
        {
            var selected = GetComboValue(_scanToolTypeCombo);
            row.Quantity!.Text = "1";
            row.Refinish!.Text = "0";
            row.Category!.Text = "M";

            if (selected == "Gas")
            {
                row.OperationType!.Text = "Replace";
                row.Description!.Text = "Post Scan";
                row.Price!.Text = "150";
                row.Labor!.Text = "0";
            }
            else if (selected == "Rivian")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Post Scan";
                row.Price!.Text = "0";
                row.Labor!.Text = "1";
            }
            else if (selected == "Tesla")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Tesla Software Script Programming";
                row.Price!.Text = "0";
                row.Labor!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };

        // Attach to input changes
        if (_scanToolTypeCombo != null) _scanToolTypeCombo.SelectionChanged += (s, e) => updateRow();
    }

    private void AddRow_SetupScanTool(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Setup Scan Tool Yes/No
        _setupScanToolCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Setup Tool?");
        _setupScanToolCombo.SelectedIndex = 0;
        Grid.SetRow(_setupScanToolCombo, rowIndex);
        Grid.SetColumn(_setupScanToolCombo, 0);
        grid.Children.Add(_setupScanToolCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(A81="Yes", ...)
        _setupScanToolCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_setupScanToolCombo);
            if (selected == "Yes")
            {
                row.OperationType!.Text = "R&I";
                row.Description!.Text = "Setup Scan Tool";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.2";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_DynamicSystemsVerification(Grid grid, int rowIndex)
    {
        // Static outputs (always shown)
        var row = CreateOperationRow("Rpr", "Dynamic Systems Verification", "1", "0", "0", "M", "0");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);
        // No update needed - static values
    }

    private void AddRow_OEMResearch(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Custom price, Column B - Custom labor
        _customPriceText = new TextBox
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 60, 80)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            Height = 32,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Price $",
            Padding = new Thickness(8, 4, 8, 4)
        };
        Grid.SetRow(_customPriceText, rowIndex);
        Grid.SetColumn(_customPriceText, 0);
        grid.Children.Add(_customPriceText);

        _customLaborText = new TextBox
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 60, 80)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            Height = 32,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Labor Hrs",
            Padding = new Thickness(8, 4, 8, 4)
        };
        Grid.SetRow(_customLaborText, rowIndex);
        Grid.SetColumn(_customLaborText, 1);
        grid.Children.Add(_customLaborText);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: R84=MAX(A83, 50), V84=MAX(A85, 1)
        Action updateRow = () =>
        {
            double price = 0;
            double labor = 0;

            if (double.TryParse(_customPriceText?.Text ?? "", out price) ||
                double.TryParse(_customLaborText?.Text ?? "", out labor))
            {
                row.OperationType!.Text = "Replace";
                row.Description!.Text = "OEM Research and Repair Procedures";
                row.Quantity!.Text = "1";
                row.Price!.Text = Math.Max(price, 50).ToString("F0");
                row.Labor!.Text = Math.Max(labor, 1).ToString("F1");
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };

        _customPriceText.TextChanged += (s, e) => updateRow();
        _customLaborText.TextChanged += (s, e) => updateRow();
    }

    private void AddRow_GatewayUnlock(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Gateway Unlock Yes/No
        _gatewayUnlockCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Gateway Unlock?");
        _gatewayUnlockCombo.SelectedIndex = 0;
        Grid.SetRow(_gatewayUnlockCombo, rowIndex);
        Grid.SetColumn(_gatewayUnlockCombo, 0);
        grid.Children.Add(_gatewayUnlockCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(A87="Yes", ...)
        _gatewayUnlockCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_gatewayUnlockCombo);
            if (selected == "Yes")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Gateway (Unlock)";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.1";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_ADASDiagnosticReport(Grid grid, int rowIndex)
    {
        // INPUT: Column B - ADAS Diagnostic Yes/No
        _adasDiagnosticCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "ADAS Diag?");
        _adasDiagnosticCombo.SelectedIndex = 0;
        Grid.SetRow(_adasDiagnosticCombo, rowIndex);
        Grid.SetColumn(_adasDiagnosticCombo, 1);
        grid.Children.Add(_adasDiagnosticCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(B79="Yes", ...)
        _adasDiagnosticCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_adasDiagnosticCombo);
            if (selected == "Yes")
            {
                row.OperationType!.Text = "Replace";
                row.Description!.Text = "ADAS Diagnostic Report";
                row.Quantity!.Text = "1";
                row.Price!.Text = "25";
                row.Labor!.Text = "0";
                row.Category!.Text = "0";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_SimulateFluids(Grid grid, int rowIndex)
    {
        // INPUT: Column B - Simulate Fluids Yes/No
        _simulateFluidsCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Sim Fluids?");
        _simulateFluidsCombo.SelectedIndex = 0;
        Grid.SetRow(_simulateFluidsCombo, rowIndex);
        Grid.SetColumn(_simulateFluidsCombo, 1);
        grid.Children.Add(_simulateFluidsCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(OR(B79="No",B79=""), "", IF(B81="Yes", ...))
        Action updateRow = () =>
        {
            var adasDiag = GetComboValue(_adasDiagnosticCombo);
            var simulate = GetComboValue(_simulateFluidsCombo);

            if (adasDiag != "No" && adasDiag != "" && simulate == "Yes")
            {
                row.OperationType!.Text = "R&I";
                row.Description!.Text = "Simulate Full Fluids for ADAS Calibrations";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.2";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };

        _simulateFluidsCombo.SelectionChanged += (s, e) => updateRow();
        if (_adasDiagnosticCombo != null) _adasDiagnosticCombo.SelectionChanged += (s, e) => updateRow();
    }

    private void AddRow_CheckTirePressure(Grid grid, int rowIndex)
    {
        // INPUT: Column B - Check Tire Pressure Yes/No
        _checkTirePressureCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Check Tire Press?");
        _checkTirePressureCombo.SelectedIndex = 0;
        Grid.SetRow(_checkTirePressureCombo, rowIndex);
        Grid.SetColumn(_checkTirePressureCombo, 1);
        grid.Children.Add(_checkTirePressureCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(OR(B79="No",B79=""), "", IF(B83="Yes", ...))
        Action updateRow = () =>
        {
            var adasDiag = GetComboValue(_adasDiagnosticCombo);
            var checkTire = GetComboValue(_checkTirePressureCombo);

            if (adasDiag != "No" && adasDiag != "" && checkTire == "Yes")
            {
                row.OperationType!.Text = "R&I";
                row.Description!.Text = "Check and Adjust Tire Pressure for ADAS Calibrations";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.2";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };

        _checkTirePressureCombo.SelectionChanged += (s, e) => updateRow();
        if (_adasDiagnosticCombo != null) _adasDiagnosticCombo.SelectionChanged += (s, e) => updateRow();
    }

    private void AddRow_RemoveBelongings(Grid grid, int rowIndex)
    {
        // INPUT: Column B - Remove Belongings Yes/No
        _removesBelongingsCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Remv Items?");
        _removesBelongingsCombo.SelectedIndex = 0;
        Grid.SetRow(_removesBelongingsCombo, rowIndex);
        Grid.SetColumn(_removesBelongingsCombo, 1);
        grid.Children.Add(_removesBelongingsCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(OR(B79="No",B79=""), "", IF(B85="Yes", ...))
        Action updateRow = () =>
        {
            var adasDiag = GetComboValue(_adasDiagnosticCombo);
            var remove = GetComboValue(_removesBelongingsCombo);

            if (adasDiag != "No" && adasDiag != "" && remove == "Yes")
            {
                row.OperationType!.Text = "R&I";
                row.Description!.Text = "Remove Customer Belongings for ADAS Calibrations";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.2";
                row.Category!.Text = "0";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };

        _removesBelongingsCombo.SelectionChanged += (s, e) => updateRow();
        if (_adasDiagnosticCombo != null) _adasDiagnosticCombo.SelectionChanged += (s, e) => updateRow();
    }

    private void AddRow_DriveCycle(Grid grid, int rowIndex)
    {
        // INPUT: Column B - Drive Cycle Yes/No
        _driveCycleCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Drive Cycle?");
        _driveCycleCombo.SelectedIndex = 0;
        Grid.SetRow(_driveCycleCombo, rowIndex);
        Grid.SetColumn(_driveCycleCombo, 1);
        grid.Children.Add(_driveCycleCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(B87="Yes", ...)
        _driveCycleCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_driveCycleCombo);
            if (selected == "Yes")
            {
                row.OperationType!.Text = "Rpr";
                row.Description!.Text = "Drive Cycle Operational Verification";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.7";
                row.Category!.Text = "M";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    // MISCELLANEOUS OPERATIONS

    private void AddRow_CleanForDelivery(Grid grid, int rowIndex)
    {
        // Static outputs (always shown)
        var row = CreateOperationRow("Replace", "Clean for Delivery", "1", "15", "0.5", "0", "0");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);
    }

    private void AddRow_GlassCleaner(Grid grid, int rowIndex)
    {
        // Static outputs (always shown)
        var row = CreateOperationRow("Replace", "Glass Cleaner", "1", "5", "0.1", "0", "0");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);
    }

    private void AddRow_MaskProtectComponents(Grid grid, int rowIndex)
    {
        // Static outputs (always shown)
        var row = CreateOperationRow("Replace", "Mask and Protect Components", "1", "3", "0.2", "0", "0");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);
    }

    private void AddRow_PartsDisposal(Grid grid, int rowIndex)
    {
        // Static outputs (always shown)
        var row = CreateOperationRow("Replace", "Parts Disposal", "1", "10", "0.3", "0", "0");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);
    }

    private void AddRow_HazardousWaste(Grid grid, int rowIndex)
    {
        // Static outputs (always shown)
        var row = CreateOperationRow("Replace", "Hazardous Waste Disposal", "1", "5", "0.2", "0", "0");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);
    }

    private void AddRow_MiscHardware(Grid grid, int rowIndex)
    {
        // Static outputs (always shown)
        var row = CreateOperationRow("Replace", "Misc Hardware", "1", "10", "0.3", "0", "0");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);
    }

    private void AddRow_SteeringWheelCover(Grid grid, int rowIndex)
    {
        // Static outputs (always shown)
        var row = CreateOperationRow("Replace", "Steering Wheel Cover, Seat Cover, and Floor Mat", "1", "5", "0.1", "0", "0");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);
    }

    private void AddRow_PreWash(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Pre Wash Yes/No
        _preWashCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Pre Wash?");
        _preWashCombo.SelectedIndex = 0;
        Grid.SetRow(_preWashCombo, rowIndex);
        Grid.SetColumn(_preWashCombo, 0);
        grid.Children.Add(_preWashCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(A129="Yes", ...)
        _preWashCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_preWashCombo);
            if (selected == "Yes")
            {
                row.OperationType!.Text = "Replace";
                row.Description!.Text = "Pre Wash and Degrease";
                row.Quantity!.Text = "1";
                row.Price!.Text = "10";
                row.Labor!.Text = "0.5";
                row.Category!.Text = "0";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_CollisionWrap(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Collision Wrap quantity (text input)
        _collisionWrapText = new TextBox
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 60, 80)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            Height = 32,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Wrap Qty",
            Padding = new Thickness(8, 4, 8, 4)
        };
        Grid.SetRow(_collisionWrapText, rowIndex);
        Grid.SetColumn(_collisionWrapText, 0);
        grid.Children.Add(_collisionWrapText);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: Description="Collision Wrap " & A131 & "x", Price=A131*25, Labor=A131*0.3
        _collisionWrapText.TextChanged += (s, e) =>
        {
            var text = _collisionWrapText.Text;
            if (!string.IsNullOrEmpty(text) && double.TryParse(text, out double value))
            {
                row.OperationType!.Text = "Replace";
                row.Description!.Text = $"Collision Wrap {value}x";
                row.Quantity!.Text = "1";
                row.Price!.Text = (value * 25).ToString("F0");
                row.Labor!.Text = (value * 0.3).ToString("F1");
                row.Category!.Text = "0";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_BioHazard(Grid grid, int rowIndex)
    {
        // INPUT: Column A - Bio Hazard Yes/No
        _bioHazardCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Bio Hazard?");
        _bioHazardCombo.SelectedIndex = 0;
        Grid.SetRow(_bioHazardCombo, rowIndex);
        Grid.SetColumn(_bioHazardCombo, 0);
        grid.Children.Add(_bioHazardCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(A133="Yes", ...)
        _bioHazardCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_bioHazardCombo);
            if (selected == "Yes")
            {
                row.OperationType!.Text = "Replace";
                row.Description!.Text = "Bio Hazard Cleanup";
                row.Quantity!.Text = "1";
                row.Price!.Text = "25";
                row.Labor!.Text = "1";
                row.Category!.Text = "0";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_IPAWipe(Grid grid, int rowIndex)
    {
        // INPUT: Column B - IPA Wipe quantity (text input)
        _ipaWipeText = new TextBox
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 60, 80)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            Height = 32,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Wipe Qty",
            Padding = new Thickness(8, 4, 8, 4)
        };
        Grid.SetRow(_ipaWipeText, rowIndex);
        Grid.SetColumn(_ipaWipeText, 1);
        grid.Children.Add(_ipaWipeText);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: Description="IPA Wipe " & B129 & "x", Price=B129*2, Labor=0.1
        _ipaWipeText.TextChanged += (s, e) =>
        {
            var text = _ipaWipeText.Text;
            if (!string.IsNullOrEmpty(text) && double.TryParse(text, out double value))
            {
                row.OperationType!.Text = "Replace";
                row.Description!.Text = $"IPA Wipe {value}x";
                row.Quantity!.Text = "1";
                row.Price!.Text = (value * 2).ToString("F0");
                row.Labor!.Text = "0.1";
                row.Category!.Text = "0";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_RemoveShippingLabels(Grid grid, int rowIndex)
    {
        // INPUT: Column C - Remove Shipping Labels Yes/No
        _shippingLabelsCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Labels?");
        _shippingLabelsCombo.SelectedIndex = 0;
        Grid.SetRow(_shippingLabelsCombo, rowIndex);
        Grid.SetColumn(_shippingLabelsCombo, 2);
        grid.Children.Add(_shippingLabelsCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(C129="Yes", ...)
        _shippingLabelsCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_shippingLabelsCombo);
            if (selected == "Yes")
            {
                row.OperationType!.Text = "Replace";
                row.Description!.Text = "Remove Shipping/Part Labels";
                row.Quantity!.Text = "1";
                row.Price!.Text = "2";
                row.Labor!.Text = "0.1";
                row.Category!.Text = "0";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    private void AddRow_Scaffolding(Grid grid, int rowIndex)
    {
        // INPUT: Column D - Scaffolding Yes/No
        _scaffoldingCombo = CreateComboBox(new[] { "", "Yes", "No" }, isInputCell: true, placeholder: "Scaffolding?");
        _scaffoldingCombo.SelectedIndex = 0;
        Grid.SetRow(_scaffoldingCombo, rowIndex);
        Grid.SetColumn(_scaffoldingCombo, 3);
        grid.Children.Add(_scaffoldingCombo);

        // OUTPUTS
        var row = CreateOperationRow("", "", "", "", "", "", "");
        AddOutputCellsToGrid(grid, rowIndex, row);
        _operationRows.Add(row);

        // FORMULA: IF(D129="Yes", ...)
        _scaffoldingCombo.SelectionChanged += (s, e) =>
        {
            var selected = GetComboValue(_scaffoldingCombo);
            if (selected == "Yes")
            {
                row.OperationType!.Text = "R&I";
                row.Description!.Text = "Scaffolding Equipment";
                row.Quantity!.Text = "1";
                row.Price!.Text = "0";
                row.Labor!.Text = "0.2";
                row.Category!.Text = "0";
                row.Refinish!.Text = "0";
            }
            else ClearOperationRow(row);
            UpdateTotals();
        };
    }

    // Helper methods for creating and managing operation rows

    private OperationRow CreateOperationRow(string opType, string desc, string qty, string price, string labor, string category, string refinish)
    {
        return new OperationRow
        {
            OperationType = new TextBlock { Text = opType },
            Description = new TextBlock { Text = desc },
            Quantity = new TextBlock { Text = qty },
            Price = new TextBlock { Text = price },
            Labor = new TextBlock { Text = labor },
            Category = new TextBlock { Text = category },
            Refinish = new TextBlock { Text = refinish }
        };
    }

    private void AddOutputCellsToGrid(Grid grid, int rowIndex, OperationRow row)
    {
        var colM = CreateCellWithTextBlock(row.OperationType!);
        var colO = CreateCellWithTextBlock(row.Description!);
        var colQ = CreateCellWithTextBlock(row.Quantity!);
        var colR = CreateCellWithTextBlock(row.Price!);
        var colV = CreateCellWithTextBlock(row.Labor!);
        var colW = CreateCellWithTextBlock(row.Category!);
        var colX = CreateCellWithTextBlock(row.Refinish!);

        // Updated column indices to match new grid structure
        Grid.SetRow(colM, rowIndex); Grid.SetColumn(colM, 5);  // Operation
        Grid.SetRow(colO, rowIndex); Grid.SetColumn(colO, 6);  // Description
        Grid.SetRow(colQ, rowIndex); Grid.SetColumn(colQ, 7);  // Quantity
        Grid.SetRow(colR, rowIndex); Grid.SetColumn(colR, 8);  // Price
        Grid.SetRow(colV, rowIndex); Grid.SetColumn(colV, 9);  // Labor
        Grid.SetRow(colW, rowIndex); Grid.SetColumn(colW, 10); // Category
        Grid.SetRow(colX, rowIndex); Grid.SetColumn(colX, 11); // Refinish

        grid.Children.Add(colM);
        grid.Children.Add(colO);
        grid.Children.Add(colQ);
        grid.Children.Add(colR);
        grid.Children.Add(colV);
        grid.Children.Add(colW);
        grid.Children.Add(colX);
    }

    private Border CreateCellWithTextBlock(TextBlock textBlock)
    {
        textBlock.Padding = new Thickness(8, 6, 8, 6);
        textBlock.FontSize = 13;
        textBlock.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
        textBlock.VerticalAlignment = VerticalAlignment.Center;

        return new Border
        {
            Child = textBlock,
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(0.5),
            Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
            MinHeight = 32,
            MinWidth = 40,
            Margin = new Thickness(0)
        };
    }

    private string GetComboValue(ComboBox? combo)
    {
        if (combo == null || combo.SelectedItem == null) return "";
        return combo.SelectedItem.ToString() ?? "";
    }

    private void ClearOperationRow(OperationRow row)
    {
        row.OperationType!.Text = "";
        row.Description!.Text = "";
        row.Quantity!.Text = "";
        row.Price!.Text = "";
        row.Labor!.Text = "";
        row.Category!.Text = "";
        row.Refinish!.Text = "";
    }

    private Border CreateCell(string text, bool isBold = false, bool isHeader = false, bool isOutput = false)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 13,
            Foreground = isHeader
                ? new SolidColorBrush(Color.FromArgb(255, 220, 220, 220))
                : new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isBold)
        {
            textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }

        var border = new Border
        {
            Child = textBlock,
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(0.5),
            Background = isHeader
                ? new SolidColorBrush(Color.FromArgb(255, 45, 45, 45))
                : (isOutput
                    ? new SolidColorBrush(Color.FromArgb(255, 35, 35, 35))
                    : new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)))
        };

        return border;
    }

    private ComboBox CreateComboBox(string[] items, bool isInputCell = false, string placeholder = "")
    {
        var combo = new ComboBox
        {
            Background = isInputCell
                ? new SolidColorBrush(Color.FromArgb(255, 45, 60, 80)) // Dark blue for input cells
                : new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            Height = 32,
            MinHeight = 32,
            MinWidth = 80,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4)
        };

        if (!string.IsNullOrEmpty(placeholder))
        {
            combo.PlaceholderText = placeholder;
        }

        foreach (var item in items)
        {
            combo.Items.Add(item); // Add string directly
        }

        return combo;
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

    private void UpdateTotals()
    {
        // This will recalculate totals from all visible operations
        // For now, just update the display
        UpdateTotalsSummary();
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
}
