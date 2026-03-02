using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using McStudDesktop.Services;

namespace McStudDesktop.ViewModels
{
    /// <summary>
    /// ViewModel for SOP List page - handles all inputs and outputs from the SOP List Excel sheet
    /// </summary>
    public partial class SOPListViewModel : ObservableObject
    {
        private readonly ExcelEngineService _excelEngine;
        private CancellationTokenSource? _debounceTokenSource;
        private const int DebounceDelayMs = 300; // Wait 300ms after last change before calculating
        private bool _isLoadingDefaults = false;

        [ObservableProperty]
        private bool _isLoading = true;

        [ObservableProperty]
        private string _loadingMessage = "Initializing Excel engine...";

        public SOPListViewModel(ExcelEngineService excelEngine)
        {
            _excelEngine = excelEngine;
            // Don't block constructor - start async initialization
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                LoadingMessage = "Loading Excel workbook...";

                // Initialize Excel engine asynchronously
                await _excelEngine.InitializeAsync();

                LoadingMessage = "Calculating initial values...";

                // Load defaults after initialization
                _isLoadingDefaults = true;
                await LoadDefaultsAsync();
                _isLoadingDefaults = false;

                IsLoading = false;
                System.Diagnostics.Debug.WriteLine("[SOPListViewModel] Initialization complete");
            }
            catch (Exception ex)
            {
                LoadingMessage = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[SOPListViewModel] Init error: {ex.Message}");
            }
        }

        #region Input Properties (17 inputs from SOP List sheet)

        // Battery Type (A29): Single or Dual
        [ObservableProperty]
        private string _batteryType = "Single";

        // Test Battery Condition (A31): Yes or No
        [ObservableProperty]
        private bool _testBattery = false;

        // Battery Support Required (A33): Yes or No
        [ObservableProperty]
        private bool _batterySupport = false;

        // ADAS System (C29): Yes or No
        [ObservableProperty]
        private bool _adasEnabled = true;

        // Vehicle Type (A35): Gas, Hybrid, or EV
        [ObservableProperty]
        private string _vehicleType = "Gas";

        // Labor Rate Type (A79): Dollar Amount, Labor Unit, or Tesla
        [ObservableProperty]
        private string _laborRateType = "Dollar Amount";

        // Vehicle Diagnostics - OEM Section
        [ObservableProperty]
        private bool _setupScanTool = true; // SOPList_A81

        [ObservableProperty]
        private bool _gatewayUnlock = false; // SOPList_A87

        // Vehicle Diagnostics - Additional Section
        [ObservableProperty]
        private bool _adasDiagnostics = true; // SOPList_B79

        [ObservableProperty]
        private bool _simulateFullFluids = true; // SOPList_B81

        [ObservableProperty]
        private bool _adjustTirePressure = true; // SOPList_B83

        [ObservableProperty]
        private bool _removeCustomerBelongings = false; // SOPList_B85

        [ObservableProperty]
        private bool _driveCycle = false; // SOPList_B87

        // ADAS Overview - Calibration Input Rows (6 rows for user input)
        // Row 1
        [ObservableProperty]
        private string _adasCalibration1Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration1Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration1Labor = double.NaN;

        // Row 2
        [ObservableProperty]
        private string _adasCalibration2Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration2Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration2Labor = double.NaN;

        // Row 3
        [ObservableProperty]
        private string _adasCalibration3Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration3Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration3Labor = double.NaN;

        // Row 4
        [ObservableProperty]
        private string _adasCalibration4Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration4Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration4Labor = double.NaN;

        // Row 5
        [ObservableProperty]
        private string _adasCalibration5Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration5Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration5Labor = double.NaN;

        // Row 6
        [ObservableProperty]
        private string _adasCalibration6Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration6Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration6Labor = double.NaN;

        // Row 7
        [ObservableProperty]
        private string _adasCalibration7Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration7Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration7Labor = double.NaN;

        // Row 8
        [ObservableProperty]
        private string _adasCalibration8Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration8Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration8Labor = double.NaN;

        // Row 9
        [ObservableProperty]
        private string _adasCalibration9Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration9Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration9Labor = double.NaN;

        // Row 10
        [ObservableProperty]
        private string _adasCalibration10Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration10Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration10Labor = double.NaN;

        // Row 11
        [ObservableProperty]
        private string _adasCalibration11Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration11Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration11Labor = double.NaN;

        // Row 12
        [ObservableProperty]
        private string _adasCalibration12Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration12Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration12Labor = double.NaN;

        // Row 13
        [ObservableProperty]
        private string _adasCalibration13Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration13Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration13Labor = double.NaN;

        // Row 14
        [ObservableProperty]
        private string _adasCalibration14Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration14Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration14Labor = double.NaN;

        // Row 15
        [ObservableProperty]
        private string _adasCalibration15Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration15Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration15Labor = double.NaN;

        // Row 16
        [ObservableProperty]
        private string _adasCalibration16Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration16Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration16Labor = double.NaN;

        // Row 17
        [ObservableProperty]
        private string _adasCalibration17Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration17Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration17Labor = double.NaN;

        // Row 18
        [ObservableProperty]
        private string _adasCalibration18Name = string.Empty;
        [ObservableProperty]
        private double _adasCalibration18Price = double.NaN;
        [ObservableProperty]
        private double _adasCalibration18Labor = double.NaN;

        // Misc - Labor Section
        [ObservableProperty]
        private bool _preWash = false; // SOPList_A129

        [ObservableProperty]
        private bool _bioHazard = false; // SOPList_A133

        // Misc - Additional Section
        [ObservableProperty]
        private bool _shippingPartsLabels = false; // SOPList_C129

        // Misc - Equipment Section
        [ObservableProperty]
        private bool _scaffolding = false; // SOPList_D129

        #endregion

        #region Output Properties (Summary and Operations)

        [ObservableProperty]
        private int _totalOperations;

        [ObservableProperty]
        private double _totalPrice;

        [ObservableProperty]
        private double _totalLabor;

        [ObservableProperty]
        private double _totalRefinish;

        // Formatted string properties for display (avoid floating point precision issues)
        public string TotalPriceFormatted => TotalPrice.ToString("F2");
        public string TotalLaborFormatted => TotalLabor.ToString("F1");
        public string TotalRefinishFormatted => TotalRefinish.ToString("F1");

        // Current category totals (shown in footer based on selected tab)
        [ObservableProperty]
        private string _currentCategoryName = "Electrical";

        [ObservableProperty]
        private int _currentCategoryOperations;

        [ObservableProperty]
        private double _currentCategoryPrice;

        [ObservableProperty]
        private double _currentCategoryLabor;

        [ObservableProperty]
        private double _currentCategoryRefinish;

        // Formatted current category properties
        public string CurrentCategoryPriceFormatted => CurrentCategoryPrice.ToString("F2");
        public string CurrentCategoryLaborFormatted => CurrentCategoryLabor.ToString("F1");
        public string CurrentCategoryRefinishFormatted => CurrentCategoryRefinish.ToString("F1");

        [ObservableProperty]
        private string _summaryText = string.Empty;

        [ObservableProperty]
        private bool _isCalculating;

        [ObservableProperty]
        private ObservableCollection<OperationRow> _operations = new();

        // Category-specific operations
        [ObservableProperty]
        private ObservableCollection<OperationRow> _electricalOperations = new();

        [ObservableProperty]
        private ObservableCollection<OperationRow> _vehicleDiagnosticsOperations = new();

        [ObservableProperty]
        private ObservableCollection<OperationRow> _miscOperations = new();

        #endregion

        #region Dropdown Options (for UI binding)

        public string[] BatteryTypeOptions { get; } = { "Single", "Dual" };
        public string[] VehicleTypeOptions { get; } = { "Gas", "Hybrid", "EV" };
        public string[] LaborRateTypeOptions { get; } = { "Dollar Amount", "Labor Unit", "Tesla" };

        #endregion

        #region Commands

        [RelayCommand]
        private async Task UpdateEstimate()
        {
            IsCalculating = true;

            try
            {
                await Task.Run(() =>
                {
                    // Send all inputs to Excel
                    _excelEngine.SetInputs(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["SOPList_A29"] = BatteryType,
                        ["SOPList_A31"] = TestBattery,
                        ["SOPList_A33"] = BatterySupport,
                        ["SOPList_C29"] = AdasEnabled,
                        ["SOPList_A35"] = VehicleType,
                        ["SOPList_A79"] = LaborRateType,
                        ["SOPList_A81"] = SetupScanTool,
                        ["SOPList_A87"] = GatewayUnlock,
                        ["SOPList_B79"] = AdasDiagnostics,
                        ["SOPList_B81"] = SimulateFullFluids,
                        ["SOPList_B83"] = AdjustTirePressure,
                        ["SOPList_B85"] = RemoveCustomerBelongings,
                        ["SOPList_B87"] = DriveCycle,
                        ["SOPList_A129"] = PreWash,
                        ["SOPList_C129"] = ShippingPartsLabels,
                        ["SOPList_D129"] = Scaffolding,
                        ["SOPList_A133"] = BioHazard
                    });

                    // Calculate formulas
                    _excelEngine.Calculate();

                    // Read summary outputs
                    var summary = _excelEngine.GetSOPListSummary();

                    // Read operation rows by category (from SOP List Excel sheet)
                    // Electrical: rows 29-75 (battery, ADAS, vehicle type related ops)
                    // Vehicle Diagnostics: rows 79-127 (scan, diagnostics related ops)
                    // Misc: rows 129-171 (pre-wash, equipment, additional ops)
                    var electricalOps = _excelEngine.GetOperations("SOP List", 29, 75, "O", "V", "R");
                    var vehicleDiagOps = _excelEngine.GetOperations("SOP List", 79, 127, "O", "V", "R");
                    var miscOps = _excelEngine.GetOperations("SOP List", 129, 171, "O", "V", "R");

                    // All operations combined
                    var allOps = new List<OperationRow>();
                    allOps.AddRange(electricalOps);
                    allOps.AddRange(vehicleDiagOps);
                    allOps.AddRange(miscOps);

                    // Update UI on main thread
                    McstudDesktop.App.MainDispatcherQueue?.TryEnqueue(() =>
                    {
                        TotalOperations = summary.TotalOperations;
                        TotalPrice = summary.TotalPrice;
                        TotalLabor = summary.TotalLabor;
                        TotalRefinish = summary.TotalRefinish;
                        SummaryText = summary.SummaryText;

                        // Update all operations
                        Operations.Clear();
                        foreach (var op in allOps)
                        {
                            Operations.Add(op);
                        }

                        // Update category-specific operations
                        ElectricalOperations.Clear();
                        foreach (var op in electricalOps)
                        {
                            ElectricalOperations.Add(op);
                        }

                        VehicleDiagnosticsOperations.Clear();
                        foreach (var op in vehicleDiagOps)
                        {
                            VehicleDiagnosticsOperations.Add(op);
                        }

                        // Add custom ADAS calibration entries from user input
                        var adasCalibrations = GetAdasCalibrationOperations();
                        foreach (var op in adasCalibrations)
                        {
                            VehicleDiagnosticsOperations.Add(op);
                            allOps.Add(op);
                        }

                        MiscOperations.Clear();
                        foreach (var op in miscOps)
                        {
                            MiscOperations.Add(op);
                        }

                        // Recalculate totals including custom ADAS calibrations
                        RecalculateTotalsWithAdasCalibrations(summary, adasCalibrations);
                    });
                });
            }
            catch (Exception ex)
            {
                // Log error or show to user
                System.Diagnostics.Debug.WriteLine($"Error updating estimate: {ex.Message}");
            }
            finally
            {
                IsCalculating = false;
            }
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            BatteryType = "Single";
            TestBattery = false;
            BatterySupport = false;
            AdasEnabled = true;
            VehicleType = "Gas";
            LaborRateType = "Dollar Amount";
            SetupScanTool = true;
            GatewayUnlock = false;
            AdasDiagnostics = true;
            SimulateFullFluids = true;
            AdjustTirePressure = true;
            RemoveCustomerBelongings = false;
            DriveCycle = false;
            PreWash = false;
            BioHazard = false;
            ShippingPartsLabels = false;
            Scaffolding = false;

            UpdateEstimateCommand.Execute(null);
        }

        #endregion

        #region Property Change Handlers (Auto-update on change with debounce)

        private void ScheduleDebouncedUpdate()
        {
            // Don't trigger updates during initial load
            if (_isLoadingDefaults || IsLoading) return;

            // Cancel any pending update
            _debounceTokenSource?.Cancel();
            _debounceTokenSource = new CancellationTokenSource();

            var token = _debounceTokenSource.Token;

            // Schedule update after debounce delay
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceDelayMs, token);
                    if (!token.IsCancellationRequested)
                    {
                        // Execute on UI thread via dispatcher
                        McstudDesktop.App.MainDispatcherQueue?.TryEnqueue(() =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                UpdateEstimateCommand.Execute(null);
                            }
                        });
                    }
                }
                catch (TaskCanceledException)
                {
                    // Expected when debounce is cancelled
                }
            });
        }

        partial void OnBatteryTypeChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnTestBatteryChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnBatterySupportChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnAdasEnabledChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnVehicleTypeChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnLaborRateTypeChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnSetupScanToolChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnGatewayUnlockChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnAdasDiagnosticsChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnSimulateFullFluidsChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnAdjustTirePressureChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnRemoveCustomerBelongingsChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnDriveCycleChanged(bool value) => ScheduleDebouncedUpdate();

        // ADAS Calibration input change handlers
        partial void OnAdasCalibration1NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration1PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration1LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration2NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration2PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration2LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration3NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration3PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration3LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration4NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration4PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration4LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration5NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration5PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration5LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration6NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration6PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration6LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration7NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration7PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration7LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration8NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration8PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration8LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration9NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration9PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration9LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration10NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration10PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration10LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration11NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration11PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration11LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration12NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration12PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration12LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration13NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration13PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration13LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration14NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration14PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration14LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration15NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration15PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration15LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration16NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration16PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration16LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration17NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration17PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration17LaborChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration18NameChanged(string value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration18PriceChanged(double value) => ScheduleDebouncedUpdate();
        partial void OnAdasCalibration18LaborChanged(double value) => ScheduleDebouncedUpdate();

        partial void OnPreWashChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnBioHazardChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnShippingPartsLabelsChanged(bool value) => ScheduleDebouncedUpdate();
        partial void OnScaffoldingChanged(bool value) => ScheduleDebouncedUpdate();

        // Notify formatted properties when raw values change
        partial void OnTotalPriceChanged(double value) => OnPropertyChanged(nameof(TotalPriceFormatted));
        partial void OnTotalLaborChanged(double value) => OnPropertyChanged(nameof(TotalLaborFormatted));
        partial void OnTotalRefinishChanged(double value) => OnPropertyChanged(nameof(TotalRefinishFormatted));

        // Notify current category formatted properties
        partial void OnCurrentCategoryPriceChanged(double value) => OnPropertyChanged(nameof(CurrentCategoryPriceFormatted));
        partial void OnCurrentCategoryLaborChanged(double value) => OnPropertyChanged(nameof(CurrentCategoryLaborFormatted));
        partial void OnCurrentCategoryRefinishChanged(double value) => OnPropertyChanged(nameof(CurrentCategoryRefinishFormatted));

        #endregion

        private async Task LoadDefaultsAsync()
        {
            // Initialize with default values and calculate
            await UpdateEstimate();
        }

        /// <summary>
        /// Creates OperationRow objects from the custom ADAS calibration input fields
        /// Only returns rows where a name has been entered
        /// </summary>
        private List<OperationRow> GetAdasCalibrationOperations()
        {
            var operations = new List<OperationRow>();

            // Helper to add a calibration if it has a name
            void AddIfNotEmpty(string name, double price, double labor)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    double actualPrice = double.IsNaN(price) ? 0 : price;
                    double actualLabor = double.IsNaN(labor) ? 0 : labor;

                    // Determine operation type based on Excel logic:
                    // - If there's a price value -> "Replace"
                    // - If there's only labor (no price) -> "Rpr"
                    string operationType;
                    if (actualPrice > 0)
                    {
                        operationType = "Replace";
                    }
                    else if (actualLabor > 0)
                    {
                        operationType = "Rpr";
                    }
                    else
                    {
                        operationType = "Replace"; // Default if both are 0
                    }

                    operations.Add(new OperationRow
                    {
                        OperationType = operationType,
                        Name = name,
                        Quantity = 1,
                        Price = actualPrice,
                        Labor = actualLabor,
                        Category = "M",
                        Refinish = 0
                    });
                }
            }

            // Check all 18 ADAS calibration rows
            AddIfNotEmpty(AdasCalibration1Name, AdasCalibration1Price, AdasCalibration1Labor);
            AddIfNotEmpty(AdasCalibration2Name, AdasCalibration2Price, AdasCalibration2Labor);
            AddIfNotEmpty(AdasCalibration3Name, AdasCalibration3Price, AdasCalibration3Labor);
            AddIfNotEmpty(AdasCalibration4Name, AdasCalibration4Price, AdasCalibration4Labor);
            AddIfNotEmpty(AdasCalibration5Name, AdasCalibration5Price, AdasCalibration5Labor);
            AddIfNotEmpty(AdasCalibration6Name, AdasCalibration6Price, AdasCalibration6Labor);
            AddIfNotEmpty(AdasCalibration7Name, AdasCalibration7Price, AdasCalibration7Labor);
            AddIfNotEmpty(AdasCalibration8Name, AdasCalibration8Price, AdasCalibration8Labor);
            AddIfNotEmpty(AdasCalibration9Name, AdasCalibration9Price, AdasCalibration9Labor);
            AddIfNotEmpty(AdasCalibration10Name, AdasCalibration10Price, AdasCalibration10Labor);
            AddIfNotEmpty(AdasCalibration11Name, AdasCalibration11Price, AdasCalibration11Labor);
            AddIfNotEmpty(AdasCalibration12Name, AdasCalibration12Price, AdasCalibration12Labor);
            AddIfNotEmpty(AdasCalibration13Name, AdasCalibration13Price, AdasCalibration13Labor);
            AddIfNotEmpty(AdasCalibration14Name, AdasCalibration14Price, AdasCalibration14Labor);
            AddIfNotEmpty(AdasCalibration15Name, AdasCalibration15Price, AdasCalibration15Labor);
            AddIfNotEmpty(AdasCalibration16Name, AdasCalibration16Price, AdasCalibration16Labor);
            AddIfNotEmpty(AdasCalibration17Name, AdasCalibration17Price, AdasCalibration17Labor);
            AddIfNotEmpty(AdasCalibration18Name, AdasCalibration18Price, AdasCalibration18Labor);

            return operations;
        }

        /// <summary>
        /// Recalculates totals to include custom ADAS calibrations
        /// </summary>
        private void RecalculateTotalsWithAdasCalibrations(SOPListSummary baseSummary, List<OperationRow> adasCalibrations)
        {
            double additionalPrice = 0;
            double additionalLabor = 0;
            int additionalOps = adasCalibrations.Count;

            foreach (var op in adasCalibrations)
            {
                additionalPrice += op.Price;
                additionalLabor += op.Labor;
            }

            TotalOperations = baseSummary.TotalOperations + additionalOps;
            TotalPrice = baseSummary.TotalPrice + additionalPrice;
            TotalLabor = baseSummary.TotalLabor + additionalLabor;
            // TotalRefinish stays the same - ADAS calibrations don't add refinish

            // Update current category totals (default to Electrical on load)
            UpdateCurrentCategoryTotals(CurrentCategoryName);
        }

        /// <summary>
        /// Updates the footer totals to show the selected category's totals
        /// </summary>
        public void UpdateCurrentCategoryTotals(string categoryName)
        {
            CurrentCategoryName = categoryName;

            ObservableCollection<OperationRow> categoryOps = categoryName switch
            {
                "Electrical" => ElectricalOperations,
                "Vehicle Diagnostics" => VehicleDiagnosticsOperations,
                "Misc" => MiscOperations,
                _ => ElectricalOperations
            };

            CurrentCategoryOperations = categoryOps.Count;
            CurrentCategoryPrice = categoryOps.Sum(op => op.Price);
            CurrentCategoryLabor = categoryOps.Sum(op => op.Labor);
            CurrentCategoryRefinish = categoryOps.Sum(op => op.Refinish);
        }
    }
}
