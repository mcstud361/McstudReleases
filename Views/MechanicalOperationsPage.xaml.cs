using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using McstudDesktop.Models;
using McStudDesktop.Services;
using System;
using System.Collections.Generic;
using Windows.UI;

// Disambiguate OperationType
using OperationType = McstudDesktop.Models.OperationType;

namespace McStudDesktop.Views
{
    public sealed partial class MechanicalOperationsPage : Page
    {
        private string _currentCategory = "ac";
        private List<Operation> _operations = new List<Operation>();

        // Custom operation panels for each category
        private Dictionary<string, CustomOperationEditorPanel> _customPanels = new();

        public MechanicalOperationsPage()
        {
            this.InitializeComponent();

            // Wire up export panel events
            ExportPanel.ClipItClicked += ExportPanel_ClipItClicked;
            ExportPanel.TypeItClicked += ExportPanel_TypeItClicked;

            // Initialize custom operations panels
            this.Loaded += MechanicalOperationsPage_Loaded;
        }

        private void MechanicalOperationsPage_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeCustomOperationPanels();
            UpdateOperations();
        }

        private void InitializeCustomOperationPanels()
        {
            // Create custom operation panels for each category
            var categories = new[]
            {
                ("ac", "AC", ACPanel),
                ("cooling", "Cooling", CoolingPanel),
                ("suspension", "Suspension", SuspensionPanel),
                ("wheeltire", "Wheel & Tire", WheelTirePanel)
            };

            foreach (var (categoryId, categoryName, panel) in categories)
            {
                var customPanel = new CustomOperationEditorPanel($"MechanicalOperations_{categoryId}");
                customPanel.OperationsChanged += (s, ops) =>
                {
                    if (_currentCategory == categoryId)
                    {
                        UpdateOperations();
                    }
                };

                // Add a header before the custom panel
                var headerBorder = new Border
                {
                    Background = new SolidColorBrush(ColorHelper.FromArgb(255, 60, 60, 60)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 16, 0, 8)
                };
                headerBorder.Child = new TextBlock
                {
                    Text = "Custom Operations",
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 120, 212)),
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold
                };

                panel.Children.Add(headerBorder);
                panel.Children.Add(customPanel);

                _customPanels[categoryId] = customPanel;
            }
        }

        private void ExportPanel_ClipItClicked(object? sender, ExportEventArgs e)
        {
            if (_operations.Count == 0)
            {
                ExportPanel.Status = "No lines to export";
                return;
            }

            ExportPanel.SetTransferring();

            var targetSystem = e.Target switch
            {
                ExportPanel.ExportTarget.CCCDesktop => "CCC Desktop",
                ExportPanel.ExportTarget.CCCWeb => "CCC Web",
                ExportPanel.ExportTarget.Mitchell => "Mitchell",
                _ => "CCC Desktop"
            };

            // Update virtual clipboard first (so Export tab shows accurate summary)
            VirtualClipboardService.Instance.SetOperations(_operations, "Mechanical Operations");

            // Copy to Windows clipboard
            ClipboardExportService.CopyToClipboard(_operations, targetSystem);

            ExportPanel.SetComplete(_operations.Count);
        }

        private async void ExportPanel_TypeItClicked(object? sender, ExportEventArgs e)
        {
            if (_operations.Count == 0)
            {
                ExportPanel.Status = "No lines to export";
                return;
            }

            using var pasteService = new AutoHotkeyPasteService();
            pasteService.StatusChanged += (s, status) =>
                ExportPanel.DispatcherQueue.TryEnqueue(() => ExportPanel.Status = status);
            pasteService.ProgressChanged += (s, p) =>
                ExportPanel.DispatcherQueue.TryEnqueue(() => ExportPanel.SetTyping(p.current, p.total));

            var rows = new List<string[]>();
            foreach (var op in _operations)
            {
                string labor = op.LaborHours > 0 ? op.LaborHours.ToString("F1") : "";
                string refinish = op.RefinishHours > 0 ? op.RefinishHours.ToString("F1") : "";
                rows.Add(new[] { op.OperationTypeString, op.Description, labor, refinish });
            }

            try
            {
                ExportPanel.SetTyping(0, _operations.Count);
                bool success = await pasteService.PasteToApp(rows.ToArray());
                if (success) ExportPanel.SetComplete(_operations.Count);
                else ExportPanel.SetError("Paste failed");
            }
            catch (Exception ex)
            {
                ExportPanel.SetError(ex.Message);
            }
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string category)
            {
                ShowCategory(category);
            }
        }

        private void ShowCategory(string category)
        {
            _currentCategory = category;

            // Reset button styles
            ACBtn.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
            CoolingBtn.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
            SuspensionBtn.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
            WheelTireBtn.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));

            // Hide all panels
            ACPanel.Visibility = Visibility.Collapsed;
            CoolingPanel.Visibility = Visibility.Collapsed;
            SuspensionPanel.Visibility = Visibility.Collapsed;
            WheelTirePanel.Visibility = Visibility.Collapsed;

            // Show selected category
            switch (category)
            {
                case "ac":
                    ACBtn.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
                    ACPanel.Visibility = Visibility.Visible;
                    break;
                case "cooling":
                    CoolingBtn.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
                    CoolingPanel.Visibility = Visibility.Visible;
                    break;
                case "suspension":
                    SuspensionBtn.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
                    SuspensionPanel.Visibility = Visibility.Visible;
                    break;
                case "wheeltire":
                    WheelTireBtn.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
                    WheelTirePanel.Visibility = Visibility.Visible;
                    break;
            }

            UpdateOperations();
        }

        private void OnInputChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateOperations();
        }

        private void OnInputChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            UpdateOperations();
        }

        private string GetComboValue(ComboBox combo, string defaultValue = "No")
        {
            return (combo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? defaultValue;
        }

        private void UpdateOperations()
        {
            _operations.Clear();

            // AC Operations
            string refrigerantType = GetComboValue(AC_RefrigerantType, "R1234yf");
            bool coverProtect = GetComboValue(AC_CoverProtect) == "Yes";
            bool climateControl = GetComboValue(AC_ClimateControl) == "Yes";
            bool bringToTemp = GetComboValue(AC_BringToTemp) == "Yes";
            bool leakCheck = GetComboValue(AC_LeakCheck) == "Yes";
            bool dyeTest = GetComboValue(AC_DyeTest) == "Yes";
            bool acReset = GetComboValue(AC_Reset) == "Yes";

            // AC Refrigerant
            decimal refrigerantPrice = refrigerantType == "R1234yf" ? 120m : 75m;
            AddOperation("Mat", $"AC Refrigerant ({refrigerantType})", 1, refrigerantPrice, 0, "M", 0);

            if (coverProtect)
            {
                AddOperation("Rpr", "Cover and Protect AC Lines", 1, 0, 0.2m, "M", 0);
            }

            if (climateControl)
            {
                AddOperation("Rpr", "Climate Control System Check", 1, 0, 0.3m, "M", 0);
            }

            if (bringToTemp)
            {
                AddOperation("Rpr", "Bring Vehicle to Operating Temperature", 1, 0, 0.5m, "M", 0);
            }

            if (leakCheck)
            {
                AddOperation("Rpr", "AC Leak Check", 1, 0, 0.5m, "M", 0);
            }

            if (dyeTest)
            {
                AddOperation("Mat", "AC System Dye/Electronic Test", 1, 25, 0.5m, "M", 0);
            }

            if (acReset)
            {
                AddOperation("Rpr", "AC Reset", 1, 0, 0.3m, "M", 0);
            }

            // Cooling Operations
            int coolantQty = (int)(COOL_CoolantQty?.Value ?? 2);
            bool clampHoses = GetComboValue(COOL_ClampHoses) == "Yes";
            bool fanCheck = GetComboValue(COOL_FanCheck) == "Yes";

            if (coolantQty > 0)
            {
                AddOperation("Mat", $"Coolant x{coolantQty}", coolantQty, 15 * coolantQty, 0.3m, "M", 0);
            }

            if (clampHoses)
            {
                AddOperation("Rpr", "Clamp Cooling Hoses", 1, 0, 0.2m, "M", 0);
            }

            if (fanCheck)
            {
                AddOperation("Rpr", "Cooling Fan Operation Check", 1, 0, 0.3m, "M", 0);
            }

            // Suspension Operations
            bool alignmentCheck = GetComboValue(SUSP_AlignmentCheck) == "Yes";
            bool addWeight = GetComboValue(SUSP_AddWeight) == "Yes";
            bool parkingBrake = GetComboValue(SUSP_ParkingBrake) == "Yes";
            bool airSuspension = GetComboValue(SUSP_AirSuspension) == "Yes";
            double rideHeight = SUSP_RideHeight?.Value ?? 0;
            bool rideHeightMode = GetComboValue(SUSP_RideHeightMode) == "Yes";
            bool powerSteering = GetComboValue(SUSP_PowerSteering) == "Yes";
            bool brakeFluid = GetComboValue(SUSP_BrakeFluid) == "Yes";
            bool hydraulicFluid = GetComboValue(SUSP_HydraulicFluid) == "Yes";
            bool transFluid = GetComboValue(SUSP_TransFluid) == "Yes";
            bool powerSteeringFluid = GetComboValue(SUSP_PowerSteeringFluid) == "Yes";
            bool hubPress = GetComboValue(SUSP_HubPress) == "Yes";
            bool springCompressor = GetComboValue(SUSP_SpringCompressor) == "Yes";
            bool subframeFixture = GetComboValue(SUSP_SubframeFixture) == "Yes";

            if (alignmentCheck)
            {
                AddOperation("Rpr", "Alignment Check", 1, 0, 1m, "M", 0);
            }

            if (addWeight)
            {
                AddOperation("Rpr", "Add Weight to Vehicle", 1, 0, 0.2m, "M", 0);
            }

            if (parkingBrake)
            {
                AddOperation("Rpr", "Parking Brake Release", 1, 0, 0.2m, "M", 0);
            }

            if (airSuspension)
            {
                AddOperation("Rpr", "Air Suspension Service", 1, 0, 0.5m, "M", 0);
            }

            if (rideHeight > 0)
            {
                AddOperation("Rpr", "Ride Height Adjustment", 1, 0, (decimal)rideHeight * 0.1m, "M", 0);
            }

            if (rideHeightMode)
            {
                AddOperation("Rpr", "Ride Height Mode Setting", 1, 0, 0.3m, "M", 0);
            }

            if (powerSteering)
            {
                AddOperation("Rpr", "Power Steering Assist Check", 1, 0, 0.3m, "M", 0);
            }

            if (brakeFluid)
            {
                AddOperation("Mat", "Brake Fluid", 1, 20, 0.3m, "M", 0);
            }

            if (hydraulicFluid)
            {
                AddOperation("Mat", "Hydraulic Fluid", 1, 25, 0.3m, "M", 0);
            }

            if (transFluid)
            {
                AddOperation("Mat", "Transmission Fluid", 1, 30, 0.3m, "M", 0);
            }

            if (powerSteeringFluid)
            {
                AddOperation("Mat", "Power Steering Fluid", 1, 15, 0.2m, "M", 0);
            }

            if (hubPress)
            {
                AddOperation("Replace", "Hub and Bearing Press", 1, 0, 0.5m, "M", 0);
            }

            if (springCompressor)
            {
                AddOperation("Replace", "Spring Compressor Setup", 1, 0, 0.3m, "M", 0);
            }

            if (subframeFixture)
            {
                AddOperation("Replace", "Subframe Fixture Setup", 1, 0, 1m, "M", 0);
            }

            // Wheel and Tire Operations
            double torqueSpec = WT_TorqueSpec?.Value ?? 0;
            int mountBalance = (int)(WT_MountBalance?.Value ?? 1);
            int tireReplacements = (int)(WT_TireReplacements?.Value ?? 0);
            bool wtBringToTemp = GetComboValue(WT_BringToTemp) == "Yes";
            bool checkBalance = GetComboValue(WT_CheckBalance) == "Yes";
            bool deBeadTPMS = GetComboValue(WT_DeBeadTPMS) == "Yes";
            bool tpmsDisposal = GetComboValue(WT_TPMSDisposal) == "Yes";
            bool lugNutCovers = GetComboValue(WT_LugNutCovers) == "Yes";
            bool universalSpare = GetComboValue(WT_UniversalSpare) == "Yes";
            int numUniversalSpare = (int)(WT_NumUniversalSpare?.Value ?? 0);
            bool removeLockingLug = GetComboValue(WT_RemoveLockingLug) == "Yes";
            bool tireRepairKit = GetComboValue(WT_TireRepairKit) == "Yes";
            bool jackStand = GetComboValue(WT_JackStand) == "Yes";
            bool wheelDolly = GetComboValue(WT_WheelDolly) == "Yes";

            if (torqueSpec > 0)
            {
                AddOperation("Rpr", $"Torque Wheels to {torqueSpec} ft-lbs", 1, 0, 0.2m, "M", 0);
            }

            if (mountBalance > 0)
            {
                AddOperation("Rpr", $"Mount and Balance x{mountBalance}", mountBalance, 25 * mountBalance, 0.3m * mountBalance, "M", 0);
            }

            if (tireReplacements > 0)
            {
                AddOperation("Replace", $"Tire Replacement x{tireReplacements}", tireReplacements, 0, 0.3m * tireReplacements, "M", 0);
            }

            if (wtBringToTemp)
            {
                AddOperation("Rpr", "Bring Tire to Operating Temperature", 1, 0, 0.3m, "M", 0);
            }

            if (checkBalance)
            {
                AddOperation("Rpr", "Check Wheel Balance", 1, 0, 0.3m, "M", 0);
            }

            if (deBeadTPMS)
            {
                AddOperation("Rpr", "De-Bead Wheel/Tire for TPMS", 1, 0, 0.3m, "M", 0);
            }

            if (tpmsDisposal)
            {
                AddOperation("Mat", "TPMS Disposal", 1, 15, 0.2m, "M", 0);
            }

            if (lugNutCovers)
            {
                AddOperation("Mat", "Lug Nut Covers", 1, 10, 0.1m, "M", 0);
            }

            if (universalSpare && numUniversalSpare > 0)
            {
                AddOperation("Mat", $"Universal Spare Wheel x{numUniversalSpare}", numUniversalSpare, 50 * numUniversalSpare, 0.2m, "M", 0);
            }

            if (removeLockingLug)
            {
                AddOperation("Rpr", "Remove Locking Lug Nut", 1, 0, 0.2m, "M", 0);
            }

            if (tireRepairKit)
            {
                AddOperation("Mat", "Tire Repair Kit", 1, 25, 0.2m, "M", 0);
            }

            if (jackStand)
            {
                AddOperation("Replace", "Jack Stand Setup", 1, 0, 0.2m, "M", 0);
            }

            if (wheelDolly)
            {
                AddOperation("Replace", "Wheel Dolly Usage", 1, 0, 0.2m, "M", 0);
            }

            // Add custom operations from the panel
            AddCustomOperations();

            RenderOperations();
            UpdateSummary();
            UpdateExportPanel();
        }

        private void AddCustomOperations()
        {
            if (_customPanels.TryGetValue(_currentCategory, out var customPanel))
            {
                // Calculate current totals for percentage calculations
                decimal repairHours = 0, refinishHours = 0;
                foreach (var op in _operations)
                {
                    repairHours += op.LaborHours;
                    refinishHours += op.RefinishHours;
                }

                // Get custom operations and add them
                var customOps = customPanel.GetOperationModels(repairHours, refinishHours);
                _operations.AddRange(customOps);
            }
        }

        private void AddOperation(string opType, string description, int quantity, decimal price, decimal labor, string category, decimal refinish)
        {
            _operations.Add(new Operation
            {
                Description = description,
                OperationType = opType == "R&I" ? OperationType.Replace :
                               opType == "Replace" ? OperationType.Replace :
                               opType == "Rpr" ? OperationType.Repair :
                               opType == "Mat" ? OperationType.Refinish : OperationType.Repair,
                Quantity = quantity,
                Price = price,
                LaborHours = labor,
                RefinishHours = refinish,
                Category = category
            });
        }

        private void RenderOperations()
        {
            OperationsList.Children.Clear();

            if (_operations.Count == 0)
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
                int rowNum = 0;
                foreach (var op in _operations)
                {
                    OperationsList.Children.Add(CreateOperationRow(op, rowNum++));
                }
            }
        }

        private Border CreateOperationRow(Operation op, int rowNum)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Operation
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Quantity
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Price
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Labor
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); // Category
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Refinish

            // Determine operation type string
            string opTypeStr = op.OperationType switch
            {
                OperationType.Replace => "Replace",
                OperationType.Repair => "Rpr",
                OperationType.Refinish => "Mat",
                _ => "Rpr"
            };

            // Operation Type
            var typeText = new TextBlock
            {
                Text = opTypeStr,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(typeText, 0);
            grid.Children.Add(typeText);

            // Description
            var descText = new TextBlock
            {
                Text = op.Description,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(descText, 1);
            grid.Children.Add(descText);

            // Quantity
            var qtyText = new TextBlock
            {
                Text = op.Quantity.ToString(),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(qtyText, 2);
            grid.Children.Add(qtyText);

            // Price
            var priceText = new TextBlock
            {
                Text = op.Price > 0 ? $"{op.Price:F0}" : "0",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(priceText, 3);
            grid.Children.Add(priceText);

            // Labor
            var laborText = new TextBlock
            {
                Text = op.LaborHours > 0 ? $"{op.LaborHours:F1}" : "0",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(laborText, 4);
            grid.Children.Add(laborText);

            // Category
            var categoryText = new TextBlock
            {
                Text = op.Category ?? "0",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(categoryText, 5);
            grid.Children.Add(categoryText);

            // Refinish
            var refinishText = new TextBlock
            {
                Text = op.RefinishHours > 0 ? $"{op.RefinishHours:F1}" : "0",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100)),
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(refinishText, 6);
            grid.Children.Add(refinishText);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 1, 0, 1),
                CornerRadius = new CornerRadius(2),
                Child = grid
            };
        }

        private void UpdateSummary()
        {
            decimal totalPrice = 0;
            decimal totalLabor = 0;
            decimal totalRefinish = 0;

            foreach (var op in _operations)
            {
                totalPrice += op.Price;
                totalLabor += op.LaborHours;
                totalRefinish += op.RefinishHours;
            }

            SummaryOps.Text = $"{_operations.Count} Ops";
            SummaryPrice.Text = $"$ {totalPrice:F0}";
            SummaryLabor.Text = $"{totalLabor:F1} Labor";
            SummaryRefinish.Text = $"{totalRefinish:F1} Refinish";
        }

        private void UpdateExportPanel()
        {
            ExportPanel.LineCount = _operations.Count;
            ExportPanel.ResetStatus();
        }
    }
}
