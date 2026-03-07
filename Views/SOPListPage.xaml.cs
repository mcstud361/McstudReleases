using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using McstudDesktop.Models;
using McStudDesktop.Services;
using System;
using System.Collections.Generic;
using Windows.UI;

// Disambiguate OperationType from the two namespaces
using OperationType = McstudDesktop.Models.OperationType;

namespace McStudDesktop.Views
{
    public sealed partial class SOPListPage : Page
    {
        private string _currentCategory = "electrical";
        private List<Operation> _electricalOps = new List<Operation>();
        private List<Operation> _diagnosticsOps = new List<Operation>();
        private List<Operation> _miscOps = new List<Operation>();

        public SOPListPage()
        {
            this.InitializeComponent();

            // Hide loading overlay immediately - no Excel loading needed
            LoadingOverlay.Visibility = Visibility.Collapsed;

            // Wire up export panel
            ExportPanel.ClipItClicked += ExportPanel_ClipItClicked;
            ExportPanel.TypeItClicked += ExportPanel_TypeItClicked;

            // Set up contextual tooltips for input fields
            SetupTooltips();

            // Set Electrical as default active category
            ShowSubCategory("electrical");
            UpdateOperations();
        }

        /// <summary>
        /// Set up contextual tooltips using the Excel knowledge service
        /// </summary>
        private void SetupTooltips()
        {
            // Electrical section tooltips
            TooltipHelperService.AddOperationTooltip(BatteryTypeCombo, "12V Battery Type",
                "Select Single or Dual battery configuration. Dual batteries require additional disconnect procedures.");

            TooltipHelperService.AddOperationTooltip(TestBatteryCombo, "Test Battery",
                "Indicates whether battery testing should be included as an operation.");

            TooltipHelperService.AddOperationTooltip(BatterySupportCombo, "Battery Support",
                "Memory saver or support unit during repair. Required for vehicles with extensive module programming.");

            TooltipHelperService.AddOperationTooltip(VehicleTypeCombo, "Vehicle Type",
                "Gas, Hybrid, or Electric - affects battery disconnect procedures and safety requirements.");

            TooltipHelperService.AddOperationTooltip(AdasCombo, "ADAS System",
                "Advanced Driver Assistance Systems - when Yes, includes static/dynamic calibration requirements.");

            // Vehicle Diagnostics section
            TooltipHelperService.AddOperationTooltip(ScanTypeCombo, "Scan Tool Type",
                "Dollar Amount for sublet scans, Labor Unit for in-house, or Tesla OEM diagnostics.");

            // Category buttons
            TooltipHelperService.AddSheetTooltip(ElectricalButton, "SOP List");
            TooltipHelperService.AddSheetTooltip(VehicleDiagnosticsButton, "SOP List");
            TooltipHelperService.AddSheetTooltip(MiscButton, "SOP List");
        }

        private void ExportPanel_ClipItClicked(object? sender, ExportEventArgs e)
        {
            var currentOps = GetCurrentCategoryOperations();
            int lineCount = currentOps.Count;

            if (lineCount == 0)
            {
                ExportPanel.Status = "No lines to export";
                return;
            }

            ExportPanel.SetTransferring();

            var targetName = e.Target switch
            {
                ExportPanel.ExportTarget.CCCDesktop => "CCC Desktop",
                ExportPanel.ExportTarget.CCCWeb => "CCC Web",
                ExportPanel.ExportTarget.Mitchell => "Mitchell",
                _ => "Unknown"
            };

            // Update virtual clipboard first (so Export tab shows accurate summary)
            VirtualClipboardService.Instance.SetOperations(currentOps, $"SOP List - {_currentCategory}");

            // Actually copy to Windows clipboard with proper formatting
            McStudDesktop.Services.ClipboardExportService.CopyToClipboard(currentOps, targetName);

            System.Diagnostics.Debug.WriteLine($"[ExportPanel] Clipped {lineCount} lines from {_currentCategory} to {targetName}");
            ExportPanel.SetComplete(lineCount);
        }

        private async void ExportPanel_TypeItClicked(object? sender, ExportEventArgs e)
        {
            var currentOps = GetCurrentCategoryOperations();
            int lineCount = currentOps.Count;

            if (lineCount == 0)
            {
                ExportPanel.Status = "No lines to export";
                return;
            }

            // Use simple AutoHotkey-style paste service
            using var pasteService = new AutoHotkeyPasteService();

            // Wire up events
            pasteService.StatusChanged += (s, status) =>
            {
                ExportPanel.DispatcherQueue.TryEnqueue(() => ExportPanel.Status = status);
            };
            pasteService.ProgressChanged += (s, p) =>
            {
                ExportPanel.DispatcherQueue.TryEnqueue(() => ExportPanel.SetTyping(p.current, p.total));
            };

            // Convert operations to simple row format for CCC Desktop
            // Just: OpType, Description, Labor, Refinish (what CCC actually needs)
            var rows = new List<string[]>();
            foreach (var op in currentOps)
            {
                string opType = op.OperationType switch
                {
                    OperationType.Replace => "Replace",
                    OperationType.Repair => "Rpr",
                    OperationType.Refinish => "Mat",
                    _ => "Rpr"
                };

                if (op.Description.Contains("R&I") || op.Description.StartsWith("12V Battery"))
                {
                    opType = "R&I";
                }

                string labor = op.LaborHours > 0 ? op.LaborHours.ToString("F1") : "";
                string refinish = op.RefinishHours > 0 ? op.RefinishHours.ToString("F1") : "";

                // Simple format: OpType, Description, Labor, Refinish
                rows.Add(new[] { opType, op.Description, labor, refinish });
            }

            try
            {
                ExportPanel.SetTyping(0, lineCount);

                // Alt+Tab to CCC, BlockInput, type fast, done!
                bool success = await pasteService.PasteToApp(rows.ToArray());

                if (success)
                    ExportPanel.SetComplete(lineCount);
                else
                    ExportPanel.SetError("Paste cancelled or failed");
            }
            catch (Exception ex)
            {
                ExportPanel.SetError(ex.Message);
            }
        }

        private void SubCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string category)
            {
                ShowSubCategory(category);
            }
        }

        private void ShowSubCategory(string category)
        {
            _currentCategory = category;

            // Reset button styles
            ElectricalButton.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
            VehicleDiagnosticsButton.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
            MiscButton.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));

            // Hide all input panels
            ElectricalPanel.Visibility = Visibility.Collapsed;
            VehicleDiagnosticsPanel.Visibility = Visibility.Collapsed;
            MiscPanel.Visibility = Visibility.Collapsed;

            // Show selected panel and update header
            switch (category)
            {
                case "electrical":
                    ElectricalButton.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
                    ElectricalPanel.Visibility = Visibility.Visible;
                    break;
                case "diagnostics":
                    VehicleDiagnosticsButton.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
                    VehicleDiagnosticsPanel.Visibility = Visibility.Visible;
                    break;
                case "misc":
                    MiscButton.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
                    MiscPanel.Visibility = Visibility.Visible;
                    break;
            }

            UpdateOperations();
        }

        private void OnInputChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateOperations();
        }

        private void OnInputChanged(object sender, TextChangedEventArgs e)
        {
            UpdateOperations();
        }

        private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            UpdateOperations();
        }

        private string GetComboValue(ComboBox combo, string defaultValue = "No")
        {
            return (combo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? defaultValue;
        }

        private void UpdateOperations()
        {
            _electricalOps.Clear();
            _diagnosticsOps.Clear();
            _miscOps.Clear();

            // === ELECTRICAL OPERATIONS ===
            string batteryType = GetComboValue(BatteryTypeCombo, "Single");
            bool testBattery = GetComboValue(TestBatteryCombo) == "Yes";
            bool batterySupport = GetComboValue(BatterySupportCombo) == "Yes";
            string vehicleType = GetComboValue(VehicleTypeCombo, "Gas");
            bool adasSystem = GetComboValue(AdasCombo) == "Yes";

            // Battery operations
            if (batteryType == "Single")
            {
                AddElectricalOp("R&I", "12V Battery", 1, 0, 0.3m, "0", 0);
            }
            else if (batteryType == "Dual")
            {
                AddElectricalOp("R&I", "12V Battery (Primary)", 1, 0, 0.3m, "0", 0);
                AddElectricalOp("R&I", "12V Battery (Secondary)", 1, 0, 0.3m, "0", 0);
            }

            if (testBattery)
            {
                AddElectricalOp("Rpr", "Test Battery Condition", 1, 0, 0.2m, "0", 0);
            }

            if (batterySupport)
            {
                AddElectricalOp("Rpr", "Battery Support/Memory Saver", 1, 15, 0.2m, "0", 0);
            }

            // Vehicle type operations
            if (vehicleType == "EV")
            {
                AddElectricalOp("Rpr", "EV High Voltage System Safe", 1, 0, 0.5m, "M", 0);
                AddElectricalOp("Rpr", "EV Battery Disconnect", 1, 0, 0.3m, "M", 0);
            }
            else if (vehicleType == "Hybrid")
            {
                AddElectricalOp("Rpr", "Hybrid High Voltage System Safe", 1, 0, 0.5m, "M", 0);
            }

            // ADAS operations
            if (adasSystem)
            {
                AddElectricalOp("Rpr", "ADAS System Present", 1, 0, 0, "M", 0);
            }

            // === VEHICLE DIAGNOSTICS OPERATIONS ===
            string scanType = GetComboValue(ScanTypeCombo, "Dollar Amount");
            bool setupScanTool = GetComboValue(SetupScanToolCombo) == "Yes";
            bool gatewayUnlock = GetComboValue(GatewayUnlockCombo) == "Yes";
            bool adasDiagnostic = GetComboValue(AdasDiagnosticCombo) == "Yes";
            bool simulateFluids = GetComboValue(SimulateFluidsCombo) == "Yes";
            bool tirePressure = GetComboValue(TirePressureCombo) == "Yes";
            bool removeBelongings = GetComboValue(RemoveBelongingsCombo) == "Yes";
            bool driveCycle = GetComboValue(DriveCycleCombo) == "Yes";

            if (setupScanTool)
            {
                if (scanType == "Dollar Amount")
                {
                    AddDiagnosticsOp("Replace", "Setup Scan Tool", 1, 50, 0, "M", 0);
                }
                else if (scanType == "Labor Unit")
                {
                    AddDiagnosticsOp("Rpr", "Setup Scan Tool", 1, 0, 0.5m, "M", 0);
                }
                else if (scanType == "Tesla")
                {
                    AddDiagnosticsOp("Replace", "Tesla Diagnostic Session", 1, 100, 0, "M", 0);
                }
            }

            if (gatewayUnlock)
            {
                AddDiagnosticsOp("Replace", "Gateway Unlock", 1, 75, 0, "M", 0);
            }

            if (adasDiagnostic)
            {
                AddDiagnosticsOp("Rpr", "ADAS System Diagnostic", 1, 0, 0.5m, "M", 0);
            }

            if (simulateFluids)
            {
                AddDiagnosticsOp("Rpr", "Simulate Full Fluids for Calibration", 1, 0, 0.3m, "M", 0);
            }

            if (tirePressure)
            {
                AddDiagnosticsOp("Rpr", "Adjust Tire Pressure to Spec", 1, 0, 0.2m, "M", 0);
            }

            if (removeBelongings)
            {
                AddDiagnosticsOp("Rpr", "Remove Customer Belongings", 1, 0, 0.3m, "0", 0);
            }

            if (driveCycle)
            {
                AddDiagnosticsOp("Rpr", "Drive Cycle", 1, 0, 0.5m, "M", 0);
            }

            // ADAS Calibrations (from TextBox/NumberBox inputs)
            AddAdasCalibrations();

            // === MISC OPERATIONS ===
            bool preWash = GetComboValue(PreWashCombo) == "Yes";
            bool shippingLabels = GetComboValue(ShippingLabelsCombo) == "Yes";
            bool scaffolding = GetComboValue(ScaffoldingCombo) == "Yes";

            if (preWash)
            {
                AddMiscOp("Rpr", "Pre-Wash Vehicle", 1, 0, 0.5m, "0", 0);
            }

            if (shippingLabels)
            {
                AddMiscOp("Mat", "Shipping/Parts Labels", 1, 10, 0.1m, "0", 0);
            }

            if (scaffolding)
            {
                AddMiscOp("Replace", "Scaffolding Setup", 1, 50, 0.5m, "0", 0);
            }

            RenderOperations();
            UpdateSummary();
            UpdateExportLineCount();
        }

        private void AddAdasCalibrations()
        {
            // Get all ADAS calibration inputs
            var calibrations = new[]
            {
                (Calib1Name, Calib1Price, Calib1Labor),
                (Calib2Name, Calib2Price, Calib2Labor),
                (Calib3Name, Calib3Price, Calib3Labor),
                (Calib4Name, Calib4Price, Calib4Labor),
                (Calib5Name, Calib5Price, Calib5Labor),
                (Calib6Name, Calib6Price, Calib6Labor),
                (Calib7Name, Calib7Price, Calib7Labor),
                (Calib8Name, Calib8Price, Calib8Labor),
                (Calib9Name, Calib9Price, Calib9Labor),
                (Calib10Name, Calib10Price, Calib10Labor),
                (Calib11Name, Calib11Price, Calib11Labor),
                (Calib12Name, Calib12Price, Calib12Labor),
                (Calib13Name, Calib13Price, Calib13Labor),
                (Calib14Name, Calib14Price, Calib14Labor),
                (Calib15Name, Calib15Price, Calib15Labor),
                (Calib16Name, Calib16Price, Calib16Labor),
                (Calib17Name, Calib17Price, Calib17Labor),
                (Calib18Name, Calib18Price, Calib18Labor)
            };

            foreach (var (nameBox, priceBox, laborBox) in calibrations)
            {
                string name = nameBox?.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                double price = priceBox?.Value ?? 0;
                double labor = laborBox?.Value ?? 0;

                if (double.IsNaN(price)) price = 0;
                if (double.IsNaN(labor)) labor = 0;

                string opType = price > 0 ? "Replace" : "Rpr";
                AddDiagnosticsOp(opType, name, 1, (decimal)price, (decimal)labor, "M", 0);
            }
        }

        private void AddElectricalOp(string opType, string description, int qty, decimal price, decimal labor, string category, decimal refinish)
        {
            _electricalOps.Add(new Operation
            {
                Description = description,
                OperationType = opType == "R&I" ? OperationType.Replace :
                               opType == "Replace" ? OperationType.Replace :
                               opType == "Rpr" ? OperationType.Repair : OperationType.Repair,
                Quantity = qty,
                Price = price,
                LaborHours = labor,
                Category = category,
                RefinishHours = refinish
            });
        }

        private void AddDiagnosticsOp(string opType, string description, int qty, decimal price, decimal labor, string category, decimal refinish)
        {
            _diagnosticsOps.Add(new Operation
            {
                Description = description,
                OperationType = opType == "R&I" ? OperationType.Replace :
                               opType == "Replace" ? OperationType.Replace :
                               opType == "Rpr" ? OperationType.Repair : OperationType.Repair,
                Quantity = qty,
                Price = price,
                LaborHours = labor,
                Category = category,
                RefinishHours = refinish
            });
        }

        private void AddMiscOp(string opType, string description, int qty, decimal price, decimal labor, string category, decimal refinish)
        {
            _miscOps.Add(new Operation
            {
                Description = description,
                OperationType = opType == "R&I" ? OperationType.Replace :
                               opType == "Replace" ? OperationType.Replace :
                               opType == "Rpr" ? OperationType.Repair :
                               opType == "Mat" ? OperationType.Refinish : OperationType.Repair,
                Quantity = qty,
                Price = price,
                LaborHours = labor,
                Category = category,
                RefinishHours = refinish
            });
        }

        private List<Operation> GetCurrentCategoryOperations()
        {
            return _currentCategory switch
            {
                "electrical" => _electricalOps,
                "diagnostics" => _diagnosticsOps,
                "misc" => _miscOps,
                _ => _electricalOps
            };
        }

        private void RenderOperations()
        {
            OperationsList.Children.Clear();

            var currentOps = GetCurrentCategoryOperations();

            if (currentOps.Count == 0)
            {
                OperationsList.Children.Add(new TextBlock
                {
                    Text = "Select options to generate operations.",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 20, 0, 0)
                });
            }
            else
            {
                foreach (var op in currentOps)
                {
                    OperationsList.Children.Add(CreateOperationRow(op));
                }
            }
        }

        private Border CreateOperationRow(Operation op)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

            string opTypeStr = op.OperationType switch
            {
                OperationType.Replace => "Replace",
                OperationType.Repair => "Rpr",
                OperationType.Refinish => "Mat",
                _ => "Rpr"
            };

            // Check for R&I in description
            if (op.Description.Contains("R&I") || op.Description.StartsWith("12V Battery"))
            {
                opTypeStr = "R&I";
            }

            AddTextToGrid(grid, 0, opTypeStr, Colors.White);
            AddTextToGrid(grid, 1, op.Description, Colors.White, TextTrimming.CharacterEllipsis);
            AddTextToGrid(grid, 2, op.Quantity.ToString(), Color.FromArgb(255, 204, 204, 204));
            AddTextToGrid(grid, 3, op.Price > 0 ? $"{op.Price:F0}" : "-", Color.FromArgb(255, 78, 201, 176));
            AddTextToGrid(grid, 4, op.LaborHours > 0 ? $"{op.LaborHours:F1}" : "-", Color.FromArgb(255, 156, 220, 254));
            AddTextToGrid(grid, 5, op.Category ?? "-", Color.FromArgb(255, 204, 204, 204));
            AddTextToGrid(grid, 6, op.RefinishHours > 0 ? $"{op.RefinishHours:F1}" : "-", Color.FromArgb(255, 206, 145, 120));

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
                Padding = new Thickness(0, 4, 0, 4),
                Margin = new Thickness(0, 1, 0, 1),
                Child = grid
            };
        }

        private void AddTextToGrid(Grid grid, int column, string text, Color color, TextTrimming trimming = TextTrimming.None)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(color),
                TextTrimming = trimming,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tb, column);
            grid.Children.Add(tb);
        }

        private void UpdateSummary()
        {
            var currentOps = GetCurrentCategoryOperations();

            decimal totalPrice = 0;
            decimal totalLabor = 0;
            decimal totalRefinish = 0;

            foreach (var op in currentOps)
            {
                totalPrice += op.Price;
                totalLabor += op.LaborHours;
                totalRefinish += op.RefinishHours;
            }

            SummaryOps.Text = $"{currentOps.Count}";
            SummaryPrice.Text = $"{totalPrice:F2}";
            SummaryLabor.Text = $"{totalLabor:F1}";
            SummaryRefinish.Text = $"{totalRefinish:F1}";
        }

        private void UpdateExportLineCount()
        {
            var currentOps = GetCurrentCategoryOperations();
            ExportPanel.LineCount = currentOps.Count;
            ExportPanel.ResetStatus();
        }

        private void ClipItButton_Click(object sender, RoutedEventArgs e)
        {
            var currentOps = GetCurrentCategoryOperations();
            int lineCount = currentOps.Count;

            if (lineCount == 0) return;

            System.Diagnostics.Debug.WriteLine($"[ClipIt] Clipping {lineCount} lines from {_currentCategory}");
        }
    }
}
