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
    public sealed partial class CoverCarPage : Page
    {
        private List<Operation> _operations = new List<Operation>();
        private TypeItService? _typeService;

        public CoverCarPage()
        {
            this.InitializeComponent();

            // Wire up export panel events
            ExportPanel.ClipItClicked += ExportPanel_ClipItClicked;
            ExportPanel.TypeItClicked += ExportPanel_TypeItClicked;
            ExportPanel.ResumeClicked += ExportPanel_ResumeClicked;
            ExportPanel.RestartClicked += ExportPanel_RestartClicked;
            ExportPanel.CancelExportClicked += ExportPanel_CancelClicked;
            ExportPanel.SpeedChanged += ExportPanel_SpeedChanged;

            UpdateOperations();
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
            VirtualClipboardService.Instance.SetOperations(_operations, "Cover Car Operations");

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

            // Apply speed settings based on ExportPanel speed selection
            // 0=Slow, 1=Normal, 2=Fast (default), 3=Turbo
            switch (ExportPanel.SelectedSpeedLevel)
            {
                case 0: // Slow
                    pasteService.KeyDelay = 50;
                    pasteService.TabDelay = 80;
                    pasteService.EnterDelay = 150;
                    break;
                case 1: // Normal
                    pasteService.KeyDelay = 35;
                    pasteService.TabDelay = 50;
                    pasteService.EnterDelay = 100;
                    break;
                case 2: // Fast (current defaults)
                    pasteService.KeyDelay = 20;
                    pasteService.TabDelay = 30;
                    pasteService.EnterDelay = 50;
                    break;
                case 3: // Turbo
                    pasteService.KeyDelay = 5;
                    pasteService.TabDelay = 10;
                    pasteService.EnterDelay = 20;
                    break;
            }

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
            if (_typeService == null || !_typeService.CanResume)
            {
                ExportPanel.SetError("Nothing to resume");
                return;
            }

            // Resume needs aiming again - user clicks where to continue
            ExportPanel.SetAiming();
            await RunAimAndTypeAsync(resume: true);
        }

        private async void ExportPanel_RestartClicked(object? sender, EventArgs e)
        {
            if (_typeService == null)
            {
                ExportPanel.SetError("Nothing to restart");
                return;
            }

            // Clear state and start fresh with aiming
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

        private void ExportPanel_SpeedChanged(object? sender, int speedLevel)
        {
            _typeService?.SetSpeedLevel(speedLevel);
        }

        private void OnInputChanged(object sender, RoutedEventArgs e)
        {
            UpdateOperations();
        }

        private void OnInputChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateOperations();
        }

        private void UpdateOperations()
        {
            _operations.Clear();

            // Get input values
            var vehicleType = (VehicleTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var laborType = (LaborTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var twoToneValue = (TwoToneCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "No";
            var twoTone = twoToneValue == "Yes";
            var front = FrontCheck?.IsChecked == true;
            var side = SideCheck?.IsChecked == true;
            var rear = RearCheck?.IsChecked == true;
            var refinish = RefinishCheck?.IsChecked == true;
            var repair = RepairCheck?.IsChecked == true;

            string twoToneSuffix = twoTone ? " for Two Tone Paint" : "";

            // Only generate operations if labor type is selected
            if (!string.IsNullOrWhiteSpace(laborType))
            {
                // Cover Car for Overspray (always when labor type selected)
                AddOperation("Refinish", "Cover Car for Overspray" + twoToneSuffix, 0, 0.2m, twoTone);

                // Cover for Edging (always when labor type selected)
                AddOperation("Refinish", "Cover for Edging" + twoToneSuffix, 0, 0.3m, twoTone);

                // Mask for Buffing (always when labor type selected)
                AddOperation("Refinish", "Mask for Buffing", 0, 0.5m, false);

                // Front position operations
                if (front)
                {
                    string frontDesc = vehicleType == "EV"
                        ? "Cover Front Trunk Compartment" + twoToneSuffix
                        : "Cover Engine Compartment" + twoToneSuffix;
                    AddOperation("Refinish", frontDesc, 0, 0.2m, twoTone);
                }

                // Side position operations (Refinish)
                if (side && refinish)
                {
                    AddOperation("Refinish", "Cover Interior and Jambs for Refinish" + twoToneSuffix, 0, 0.2m, twoTone);
                }

                // Rear position operations
                if (rear)
                {
                    AddOperation("Refinish", "Cover Trunk Compartment for Refinish" + twoToneSuffix, 0, 0.2m, twoTone);
                }

                // Repair operations
                if (repair)
                {
                    AddOperation("Refinish", "Clean and Cover Car for Primer" + twoToneSuffix, 0, 0.3m, twoTone);
                }

                // Side position operations (Repair)
                if (side && repair)
                {
                    AddOperation("Refinish", "Cover Interior and Jambs for Repairs" + twoToneSuffix, 0, 0.2m, twoTone);
                }
            }

            RenderOperations();
            UpdateSummary();
            UpdateExportPanel();
        }

        private void AddOperation(string opType, string description, decimal price, decimal refinishHrs, bool twoTone)
        {
            // Two-tone doubles the refinish hours
            decimal multiplier = twoTone ? 2m : 1m;

            _operations.Add(new Operation
            {
                Description = description,
                OperationType = OperationType.Refinish,
                Quantity = 1,
                Price = price,
                LaborHours = 0,
                RefinishHours = refinishHrs * multiplier,
                Category = "Cover"
            });
        }

        private void RenderOperations()
        {
            OperationsList.Children.Clear();

            if (_operations.Count == 0)
            {
                OperationsList.Children.Add(new TextBlock
                {
                    Text = "Select a Labor Type to generate operations.",
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

            // Row number
            var rowNumText = new TextBlock
            {
                Text = rowNum.ToString(),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                FontSize = 10,
                Padding = new Thickness(2, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Operation Type
            var typeText = new TextBlock
            {
                Text = "Refinish",
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
                Text = "0",
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
            SummaryLabor.Text = $"{totalLabor:F0} Labor";
            SummaryRefinish.Text = $"{totalRefinish:F1} Refinish";
        }

        private void UpdateExportPanel()
        {
            ExportPanel.LineCount = _operations.Count;
            ExportPanel.ResetStatus();
        }
    }
}
