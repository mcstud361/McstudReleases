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
    public sealed partial class BodyOperationsPage : Page
    {
        private string _currentSection = "bodyops";
        private List<Operation> _operations = new List<Operation>();

        // Custom operation panels for each section
        private Dictionary<string, CustomOperationEditorPanel> _customPanels = new();

        public BodyOperationsPage()
        {
            this.InitializeComponent();

            // Wire up export panel events
            ExportPanel.ClipItClicked += ExportPanel_ClipItClicked;
            ExportPanel.TypeItClicked += ExportPanel_TypeItClicked;

            // Initialize custom operation panels
            this.Loaded += BodyOperationsPage_Loaded;
        }

        private void BodyOperationsPage_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeCustomOperationPanels();
            UpdateOperations();
        }

        private void InitializeCustomOperationPanels()
        {
            // Create custom operation panels for each section
            var sections = new[]
            {
                ("bodyops", "Body Operations", BodyOpsSection),
                ("measurements", "Measurements", MeasurementsSection)
            };

            foreach (var (sectionId, sectionName, panel) in sections)
            {
                var customPanel = new CustomOperationEditorPanel($"BodyOperations_{sectionId}");
                customPanel.OperationsChanged += (s, ops) =>
                {
                    if (_currentSection == sectionId)
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

                _customPanels[sectionId] = customPanel;
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

            // Copy to clipboard using the static method
            var targetSystem = e.Target switch
            {
                ExportPanel.ExportTarget.CCCDesktop => "CCC Desktop",
                ExportPanel.ExportTarget.CCCWeb => "CCC Web",
                ExportPanel.ExportTarget.Mitchell => "Mitchell",
                _ => "CCC Desktop"
            };

            // Update virtual clipboard first (so Export tab shows accurate summary)
            VirtualClipboardService.Instance.SetOperations(_operations, $"Body Operations - {_currentSection}");

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

            // Simple AutoHotkey-style paste
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
                if (success)
                    ExportPanel.SetComplete(_operations.Count);
                else
                    ExportPanel.SetError("Paste failed");
            }
            catch (Exception ex)
            {
                ExportPanel.SetError(ex.Message);
            }
        }

        private void SectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string section)
            {
                ShowSection(section);
            }
        }

        private void ShowSection(string section)
        {
            _currentSection = section;

            // Reset button styles
            BodyOpsButton.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
            MeasurementsButton.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));

            // Hide all sections
            BodyOpsSection.Visibility = Visibility.Collapsed;
            MeasurementsSection.Visibility = Visibility.Collapsed;

            // Show selected section
            switch (section)
            {
                case "bodyops":
                    BodyOpsButton.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
                    HeaderText.Text = "Body Operations";
                    BodyOpsSection.Visibility = Visibility.Visible;
                    break;
                case "measurements":
                    MeasurementsButton.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
                    HeaderText.Text = "Measurements";
                    MeasurementsSection.Visibility = Visibility.Visible;
                    break;
            }

            UpdateOperations();
        }

        private void OnInputChanged(object sender, SelectionChangedEventArgs e)
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
            _operations.Clear();

            // Body Operations Section
            bool welding = GetComboValue(Welding) == "Yes";
            int numWelders = (int)(NumberOfWelders?.Value ?? 1);

            // Welding-related operations
            if (welding)
            {
                // R&I: Weld Blankets
                AddOperation("R&I", "Weld Blankets", 1, 0, 0.3m, "0", 0);

                // Replace: Welder Setup (based on number of welders)
                if (numWelders > 0)
                {
                    AddOperation("Replace", $"{numWelders}x Welder Setup, Power Up, Power Down", 1, 25, 0.5m, "0", 0);
                }

                // Replace: Welding Consumables
                AddOperation("Replace", "Welding Consumables", 1, 25, 0, "0", 0);

                // Replace: Weld Through Primer
                AddOperation("Replace", "Weld Through Primer", 1, 10, 0, "0", 0);

                // Rpr: Destructive Test Weld
                AddOperation("Rpr", "Destructive Test Weld", 1, 0, 0.5m, "0", 0);

                // Rpr: Remove and Cleanup Debris from Drilling and Grinding
                AddOperation("Rpr", "Remove and Cleanup Debris from Drilling and Grinding", 1, 0, 0.2m, "0", 0);

                // R&I: R&I All Electronics Within Weld Zones
                AddOperation("R&I", "R&I All Electronics Within Weld Zones", 1, 0, 1m, "M", 0);
            }

            // Collision Damage Access
            if (GetComboValue(CollisionDamageAccess) == "Yes")
            {
                AddOperation("Rpr", "Collision Damage Access", 1, 0, 0.5m, "0", 0);
            }

            // Body Plugs
            if (GetComboValue(BodyPlugs) == "Yes")
            {
                AddOperation("Mat", "Body Plugs", 1, 15, 0.2m, "0", 0);
            }

            // Remove Factory E-Coat
            if (GetComboValue(RemoveFactoryECoat) == "Yes")
            {
                AddOperation("Rpr", "Remove Factory E-Coat", 1, 0, 0.3m, "0", 0);
            }

            // Structural Adhesive
            if (GetComboValue(StructuralAdhesive) == "Yes")
            {
                AddOperation("Mat", "Structural Adhesive", 1, 35, 0.3m, "0", 0);
            }

            // Cure Time
            if (GetComboValue(CureTime) == "Yes")
            {
                AddOperation("Rpr", "Adhesive Cure Time", 1, 0, 0.5m, "0", 0);
            }

            // Spark Paper
            if (GetComboValue(SparkPaper) == "Yes")
            {
                AddOperation("Mat", "Spark Paper", 1, 10, 0, "0", 0);
            }

            // Undercoat
            if (GetComboValue(Undercoat) == "Yes")
            {
                AddOperation("Mat", "Undercoat Application", 1, 20, 0.3m, "0", 0);
            }

            // Sound Deadener Pads
            int soundDeadenerPads = (int)(SoundDeadenerPads?.Value ?? 0);
            if (soundDeadenerPads > 0)
            {
                AddOperation("Mat", $"Sound Deadener Pads x{soundDeadenerPads}", soundDeadenerPads, 5 * soundDeadenerPads, 0.1m * soundDeadenerPads, "0", 0);
            }

            // Rivet Gun
            if (GetComboValue(RivetGun) == "Yes")
            {
                AddOperation("Replace", "Rivet Gun Setup", 1, 0, 0.2m, "0", 0);
            }

            // Nutsert Gun
            if (GetComboValue(NutsertGun) == "Yes")
            {
                AddOperation("Replace", "Nutsert Gun Setup", 1, 0, 0.2m, "0", 0);
            }

            // Crack Dye Test
            if (GetComboValue(CrackDyeTest) == "Yes")
            {
                AddOperation("Rpr", "Crack Dye Test", 1, 15, 0.3m, "0", 0);
            }

            // Tap and Die Set
            if (GetComboValue(TapAndDieSet) == "Yes")
            {
                AddOperation("Replace", "Tap and Die Set Usage", 1, 0, 0.2m, "0", 0);
            }

            // Plastic Welder
            if (GetComboValue(PlasticWelder) == "Yes")
            {
                AddOperation("Replace", "Plastic Welder Setup", 1, 15, 0.3m, "0", 0);
            }

            // Puller
            if (GetComboValue(Puller) == "Yes")
            {
                AddOperation("Replace", "Puller Setup", 1, 0, 0.2m, "0", 0);
            }

            // Weld Pull Tabs
            if (GetComboValue(WeldPullTabs) == "Yes")
            {
                AddOperation("Mat", "Weld Pull Tabs", 1, 10, 0.2m, "0", 0);
            }

            // Measurements Section operations
            // Measure Gap Dimensions
            if (GetComboValue(MeasureGapDimensions) == "Yes")
            {
                AddOperation("Rpr", "Measure Gap Dimensions", 1, 0, 0.3m, "0", 0);
            }

            // Frame Bench - generates Setup operation
            if (GetComboValue(FrameBench) == "Yes")
            {
                AddOperation("Rpr", "Setup", 1, 0, 2m, "F", 0);
            }

            // Measure type (Pre, In-Process, Post)
            string measureType = GetComboValue(MeasureType, "");
            if (!string.IsNullOrEmpty(measureType))
            {
                AddOperation("Rpr", $"{measureType} Measure", 1, 0, 3m, "F", 0);
            }

            // Pull Labor Unit
            double pullLabor = PullLaborUnit?.Value ?? 0;
            if (pullLabor > 0)
            {
                AddOperation("Rpr", "Pull Damage and Restore Dimensions", 1, 0, (decimal)pullLabor, "F", 0);
            }

            // Securing Method (Pinch Welds / Truck Clamps)
            string securingMethod = GetComboValue(SecuringMethod, "");
            if (!string.IsNullOrEmpty(securingMethod))
            {
                decimal securingRefinish = securingMethod == "Pinch Welds" ? 0.2m : 0.3m;
                AddOperation("Rpr", securingMethod, 1, 0, 0.2m, "F", securingRefinish);
            }

            // Tram Gauge
            if (GetComboValue(TramGauge) == "Yes")
            {
                AddOperation("Rpr", "Tram Gauge Setup", 1, 0, 0.3m, "F", 0);
            }

            // Jig Setup
            if (GetComboValue(JigSetup) == "Yes")
            {
                AddOperation("Rpr", "Jig Setup", 1, 0, 0.5m, "F", 0);
            }

            // Extra Fixtures
            if (GetComboValue(ExtraFixtures) == "Yes")
            {
                AddOperation("Rpr", "Extra Fixtures Setup", 1, 0, 0.3m, "F", 0);
            }

            // Add custom operations from the panel
            AddCustomOperations();

            RenderOperations();
            UpdateSummary();
            UpdateExportPanel();
        }

        private void AddCustomOperations()
        {
            if (_customPanels.TryGetValue(_currentSection, out var customPanel))
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); // Operation
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

            // Check if this is an R&I operation based on description
            if (op.Description.Contains("R&I") || op.Description.Contains("Weld Blankets") || op.Description.Contains("Electronics"))
            {
                opTypeStr = "R&I";
            }

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
                Text = op.Price > 0 ? $"{op.Price:F0}" : "-",
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
                Text = op.LaborHours > 0 ? $"{op.LaborHours:F1}" : "-",
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
                Text = op.Category ?? "-",
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
                Text = op.RefinishHours > 0 ? $"{op.RefinishHours:F1}" : "-",
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
