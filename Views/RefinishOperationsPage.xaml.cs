using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using McstudDesktop.Models;
using McStudDesktop.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI;

// Disambiguate OperationType
using OperationType = McstudDesktop.Models.OperationType;

namespace McStudDesktop.Views
{
    public sealed partial class RefinishOperationsPage : Page
    {
        private List<Operation> _operations = new List<Operation>();
        private TypeItService? _typeService;
        private CustomOperationEditorPanel? _customPanel;

        public RefinishOperationsPage()
        {
            this.InitializeComponent();

            // Wire up export panel events
            ExportPanel.ClipItClicked += ExportPanel_ClipItClicked;
            ExportPanel.TypeItClicked += ExportPanel_TypeItClicked;
            ExportPanel.ResumeClicked += ExportPanel_ResumeClicked;
            ExportPanel.RestartClicked += ExportPanel_RestartClicked;
            ExportPanel.CancelExportClicked += ExportPanel_CancelClicked;

            // Initialize custom operations panel
            this.Loaded += RefinishOperationsPage_Loaded;
        }

        private void RefinishOperationsPage_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeCustomOperationPanel();
            UpdateOperations();
        }

        private void InitializeCustomOperationPanel()
        {
            _customPanel = new CustomOperationEditorPanel("RefinishOperations");
            _customPanel.OperationsChanged += (s, ops) => UpdateOperations();

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
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 212, 165, 116)), // Match refinish accent color
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            };

            InputsPanel.Children.Add(headerBorder);
            InputsPanel.Children.Add(_customPanel);
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
            VirtualClipboardService.Instance.SetOperations(_operations, "Refinish Operations");

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

        // Stub for compatibility - not used with new service
        private async Task RunAimAndTypeAsync(bool resume = false)
        {
            await Task.CompletedTask;
        }

        private async void ExportPanel_ResumeClicked(object? sender, EventArgs e)
        {
            if (_typeService == null || !_typeService.CanResume) { ExportPanel.SetError("Nothing to resume"); return; }
            ExportPanel.SetAiming();
            await RunAimAndTypeAsync(resume: true);
        }

        private async void ExportPanel_RestartClicked(object? sender, EventArgs e)
        {
            if (_typeService == null) { ExportPanel.SetError("Nothing to restart"); return; }
            _typeService.ClearState();
            ExportPanel.SetAiming();
            await RunAimAndTypeAsync(resume: false);
        }

        private void ExportPanel_CancelClicked(object? sender, EventArgs e)
        {
            _typeService?.CancelAimingMode();
            ExportPanel.Status = "Export cancelled";
            ExportPanel.HidePauseButtons();
            _typeService?.Dispose();
            _typeService = null;
        }

        private void OnInputChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateOperations();
        }

        private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            UpdateOperations();
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateOperations();
        }

        private string GetComboValue(ComboBox combo, string defaultValue = "")
        {
            return (combo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? defaultValue;
        }

        private void UpdateOperations()
        {
            _operations.Clear();

            // Get input values
            string paintStage = GetComboValue(PaintStage);
            bool fourStageRefinish = GetComboValue(FourStageRefinish) == "Yes";
            bool touchUpPaintedBolts = GetComboValue(TouchUpPaintedBolts) == "Yes";
            bool specialColorTint = GetComboValue(SpecialColorTint) == "Yes";

            // Get paint stage suffix
            string paintStageSuffix = !string.IsNullOrEmpty(paintStage) ? $" ({paintStage})" : "";

            // Refinish Material Invoice - always add when paint stage is selected
            if (!string.IsNullOrEmpty(paintStage))
            {
                AddOperation("Replace", "Refinish Material Invoice", 1, 1, 0, "0", 0);
            }

            // Color Tint based on paint stage
            if (!string.IsNullOrEmpty(paintStage))
            {
                AddOperation("Refinish", $"Color Tint{paintStageSuffix}", 1, 0, 0, "0", 0.5m);
            }

            // Spray Out Cards based on paint stage
            if (!string.IsNullOrEmpty(paintStage))
            {
                AddOperation("Refinish", $"Spray Out Cards{paintStageSuffix}", 1, 0, 0, "0", 0.5m);
            }

            // Touch Up Painted Bolts
            if (touchUpPaintedBolts)
            {
                AddOperation("Refinish", "Touch Up Painted Bolts", 1, 0, 0, "0", 0.5m);
            }

            // Special Color Tint
            if (specialColorTint)
            {
                AddOperation("Refinish", "Special Color Tint", 1, 0, 0, "0", 0.3m);
            }

            // Second Color operations
            string secondColor = SecondColor?.Text?.Trim() ?? "";
            string secondColorStage = GetComboValue(SecondColorPaintStage);
            if (!string.IsNullOrEmpty(secondColor) && !string.IsNullOrEmpty(secondColorStage))
            {
                AddOperation("Refinish", $"Second Color: {secondColor} ({secondColorStage})", 1, 0, 0, "0", 0.5m);
            }

            // Third Color operations
            string thirdColor = ThirdColor?.Text?.Trim() ?? "";
            string thirdColorStage = GetComboValue(ThirdColorPaintStage);
            if (!string.IsNullOrEmpty(thirdColor) && !string.IsNullOrEmpty(thirdColorStage))
            {
                AddOperation("Refinish", $"Third Color: {thirdColor} ({thirdColorStage})", 1, 0, 0, "0", 0.5m);
            }

            // Fourth Color operations
            string fourthColor = FourthColor?.Text?.Trim() ?? "";
            string fourthColorStage = GetComboValue(FourthColorPaintStage);
            if (!string.IsNullOrEmpty(fourthColor) && !string.IsNullOrEmpty(fourthColorStage))
            {
                AddOperation("Refinish", $"Fourth Color: {fourthColor} ({fourthColorStage})", 1, 0, 0, "0", 0.5m);
            }

            // Fifth Color operations
            string fifthColor = FifthColor?.Text?.Trim() ?? "";
            string fifthColorStage = GetComboValue(FifthColorPaintStage);
            if (!string.IsNullOrEmpty(fifthColor) && !string.IsNullOrEmpty(fifthColorStage))
            {
                AddOperation("Refinish", $"Fifth Color: {fifthColor} ({fifthColorStage})", 1, 0, 0, "0", 0.5m);
            }

            // Matte Clear Price
            double matteClearPrice = MatteClearPrice?.Value ?? 0;
            if (matteClearPrice > 0)
            {
                AddOperation("Mat", "Matte Clear", 1, (decimal)matteClearPrice, 0, "0", 0);
            }

            // Static Gun
            if (GetComboValue(StaticGun) == "Yes")
            {
                AddOperation("Refinish", "Static Gun Setup", 1, 0, 0, "0", 0.2m);
            }

            // Monitor Time
            if (GetComboValue(MonitorTime) == "Yes")
            {
                AddOperation("Refinish", "Monitor Time", 1, 0, 0, "0", 0.5m);
            }

            // Four Stage Refinish operations
            if (fourStageRefinish)
            {
                AddOperation("Refinish", "Four Stage Refinish Process", 1, 0, 0, "0", 1.0m);
            }

            // Add custom operations from the panel
            AddCustomOperations();

            RenderOperations();
            UpdateSummary();
            UpdateExportPanel();
        }

        private void AddCustomOperations()
        {
            if (_customPanel != null)
            {
                // Calculate current totals for percentage calculations
                decimal repairHours = 0, refinishHours = 0;
                foreach (var op in _operations)
                {
                    repairHours += op.LaborHours;
                    refinishHours += op.RefinishHours;
                }

                // Get custom operations and add them
                var customOps = _customPanel.GetOperationModels(repairHours, refinishHours);
                _operations.AddRange(customOps);
            }
        }

        private void AddOperation(string opType, string description, int quantity, decimal price, decimal labor, string category, decimal refinish)
        {
            _operations.Add(new Operation
            {
                Description = description,
                OperationType = opType == "Replace" ? OperationType.Replace :
                               opType == "Refinish" ? OperationType.Refinish :
                               opType == "Mat" ? OperationType.Refinish : OperationType.Refinish,
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
                OperationType.Refinish => "Refinish",
                _ => "Refinish"
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
