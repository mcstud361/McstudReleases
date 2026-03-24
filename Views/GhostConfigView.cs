#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Ghost Estimate configuration settings UI.
    /// Sections: Shop Labor Rates, Scanning Config, Operation Overrides, Category Toggles.
    /// </summary>
    public sealed class GhostConfigView : UserControl
    {
        private readonly GhostConfigService _configService;
        private readonly ExcelGhostDataProvider _excelProvider;

        // Rate inputs
        private NumberBox? _bodyRateBox;
        private NumberBox? _paintRateBox;
        private NumberBox? _mechRateBox;
        private NumberBox? _frameRateBox;
        private NumberBox? _glassRateBox;

        // Scanning inputs
        private RadioButton? _flatRateRadio;
        private RadioButton? _laborHoursRadio;
        private NumberBox? _flatRateAmountBox;
        private NumberBox? _scanLaborHoursBox;

        // Operation overrides
        private TextBox? _overrideSearchBox;
        private StackPanel? _overrideListPanel;

        // Custom operations
        private StackPanel? _customOpsListPanel;

        // Must-haves
        private StackPanel? _mustHavesListPanel;

        // Category toggles
        private StackPanel? _categoryTogglesPanel;

        public GhostConfigView()
        {
            _configService = GhostConfigService.Instance;
            _excelProvider = ExcelGhostDataProvider.Instance;
            BuildUI();
        }

        private void BuildUI()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 32, 38)),
                Padding = new Thickness(0)
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 800
            };

            this.MinWidth = 700;

            var mainStack = new StackPanel { Spacing = 16, Padding = new Thickness(16) };

            // Section 1: Shop Labor Rates
            mainStack.Children.Add(BuildLaborRatesSection());

            // Section 2: Scanning Config
            mainStack.Children.Add(BuildScanningSection());

            // Section 3: Standard Operations (read-only, with override/disable)
            mainStack.Children.Add(BuildOperationOverridesSection());

            // Section 4: Your Custom Operations
            mainStack.Children.Add(BuildCustomOperationsSection());

            // Section 5: Must-Haves (required on every estimate)
            mainStack.Children.Add(BuildMustHavesSection());

            // Section 6: Category Toggles
            mainStack.Children.Add(BuildCategoryTogglesSection());

            // Bottom buttons
            mainStack.Children.Add(BuildBottomButtons());

            scrollViewer.Content = mainStack;
            mainBorder.Child = scrollViewer;
            Content = mainBorder;
        }

        #region Section Builders

        private Border BuildLaborRatesSection()
        {
            var border = CreateSectionBorder();
            var stack = new StackPanel { Spacing = 10 };

            stack.Children.Add(CreateSectionHeader("Shop Labor Rates", "\uE825"));

            var config = _configService.Config;

            var ratesGrid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
            ratesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            ratesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ratesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            ratesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Row 0: Body + Paint
            ratesGrid.RowDefinitions.Add(new RowDefinition());
            AddRateRow(ratesGrid, 0, 0, "Body ($/hr)", config.LaborRates.BodyRate, GhostConfigService.DEFAULT_BODY_RATE, out _bodyRateBox);
            AddRateRow(ratesGrid, 0, 2, "Paint ($/hr)", config.LaborRates.PaintRate, GhostConfigService.DEFAULT_PAINT_RATE, out _paintRateBox);

            // Row 1: Mech + Frame
            ratesGrid.RowDefinitions.Add(new RowDefinition());
            AddRateRow(ratesGrid, 1, 0, "Mechanical ($/hr)", config.LaborRates.MechRate, GhostConfigService.DEFAULT_MECH_RATE, out _mechRateBox);
            AddRateRow(ratesGrid, 1, 2, "Frame ($/hr)", config.LaborRates.FrameRate, GhostConfigService.DEFAULT_FRAME_RATE, out _frameRateBox);

            // Row 2: Glass
            ratesGrid.RowDefinitions.Add(new RowDefinition());
            AddRateRow(ratesGrid, 2, 0, "Glass ($/hr)", config.LaborRates.GlassRate, GhostConfigService.DEFAULT_GLASS_RATE, out _glassRateBox);

            stack.Children.Add(ratesGrid);

            stack.Children.Add(new TextBlock
            {
                Text = "Leave blank to use defaults. Changes take effect on next estimate generation.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });

            border.Child = stack;
            return border;
        }

        private void AddRateRow(Grid grid, int row, int col, string label, decimal? currentValue, decimal defaultValue, out NumberBox numberBox)
        {
            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, col);
            grid.Children.Add(lbl);

            numberBox = new NumberBox
            {
                Value = currentValue.HasValue ? (double)currentValue.Value : double.NaN,
                PlaceholderText = $"${defaultValue:F0}",
                Minimum = 0,
                Maximum = 500,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 120,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetRow(numberBox, row);
            Grid.SetColumn(numberBox, col + 1);
            grid.Children.Add(numberBox);
        }

        private Border BuildScanningSection()
        {
            var border = CreateSectionBorder();
            var stack = new StackPanel { Spacing = 10 };

            stack.Children.Add(CreateSectionHeader("Scanning Configuration", "\uE9D9"));

            var config = _configService.GetScanningConfig();

            // Radio buttons
            _flatRateRadio = new RadioButton
            {
                Content = "Flat Rate (per scan)",
                IsChecked = config.BillingMethod == ScanBillingMethod.FlatRate,
                GroupName = "ScanMethod",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12
            };

            _laborHoursRadio = new RadioButton
            {
                Content = "Labor Hours (mechanical rate)",
                IsChecked = config.BillingMethod == ScanBillingMethod.LaborHours,
                GroupName = "ScanMethod",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12
            };

            stack.Children.Add(_flatRateRadio);

            // Flat rate amount
            var flatRatePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(24, 0, 0, 0) };
            flatRatePanel.Children.Add(new TextBlock
            {
                Text = "Amount: $",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190)),
                VerticalAlignment = VerticalAlignment.Center
            });
            _flatRateAmountBox = new NumberBox
            {
                Value = (double)config.FlatRateAmount,
                Minimum = 0,
                Maximum = 1000,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 100,
                FontSize = 12
            };
            flatRatePanel.Children.Add(_flatRateAmountBox);
            stack.Children.Add(flatRatePanel);

            stack.Children.Add(_laborHoursRadio);

            // Labor hours
            var laborHoursPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(24, 0, 0, 0) };
            laborHoursPanel.Children.Add(new TextBlock
            {
                Text = "Hours per scan:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190)),
                VerticalAlignment = VerticalAlignment.Center
            });
            _scanLaborHoursBox = new NumberBox
            {
                Value = (double)config.LaborHours,
                Minimum = 0,
                Maximum = 10,
                SmallChange = 0.1,
                LargeChange = 0.5,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 100,
                FontSize = 12
            };
            laborHoursPanel.Children.Add(_scanLaborHoursBox);
            stack.Children.Add(laborHoursPanel);

            // Toggle visibility based on radio selection
            _flatRateRadio.Checked += (s, e) =>
            {
                flatRatePanel.Opacity = 1.0;
                laborHoursPanel.Opacity = 0.4;
            };
            _laborHoursRadio.Checked += (s, e) =>
            {
                flatRatePanel.Opacity = 0.4;
                laborHoursPanel.Opacity = 1.0;
            };

            // Set initial opacity
            flatRatePanel.Opacity = config.BillingMethod == ScanBillingMethod.FlatRate ? 1.0 : 0.4;
            laborHoursPanel.Opacity = config.BillingMethod == ScanBillingMethod.LaborHours ? 1.0 : 0.4;

            border.Child = stack;
            return border;
        }

        private Border BuildOperationOverridesSection()
        {
            var border = CreateSectionBorder();
            var stack = new StackPanel { Spacing = 10 };

            stack.Children.Add(CreateSectionHeader("Customize Operations", "\uE70F"));

            // Search box
            _overrideSearchBox = new TextBox
            {
                PlaceholderText = "Search operations...",
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 65, 70, 80))
            };
            _overrideSearchBox.TextChanged += OverrideSearch_TextChanged;
            stack.Children.Add(_overrideSearchBox);

            // Operation list (scrollable)
            var listScroll = new ScrollViewer
            {
                MaxHeight = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _overrideListPanel = new StackPanel { Spacing = 2 };
            listScroll.Content = _overrideListPanel;
            stack.Children.Add(listScroll);

            // Populate with Excel operations
            PopulateOverrideList("");

            stack.Children.Add(new TextBlock
            {
                Text = "Adjust labor hours or turn off operations you don't want in the ghost estimate.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });

            border.Child = stack;
            return border;
        }

        private void OverrideSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            PopulateOverrideList(_overrideSearchBox?.Text ?? "");
        }

        private static bool IsJunkExcelRow(ExcelOperation op)
        {
            if (string.IsNullOrWhiteSpace(op.Description)) return true;
            var desc = op.Description.Trim();
            // Navigation links, stat bars, emoji headers from Excel
            if (desc.Contains("\U0001f517") || desc.Contains("\U0001f4ca") || desc.Contains("\U0001f4b2") ||
                desc.Contains("\U0001f6e0") || desc.Contains("\U0001f3a8"))
                return true;
            if (desc.StartsWith("Back to top", StringComparison.OrdinalIgnoreCase)) return true;
            if (desc.StartsWith("\u25bc") || desc.StartsWith("\u25b2")) return true; // ▼▲ dropdown markers
            // All zeros placeholder — no hours, no price, not a real operation
            if (op.LaborHours == 0 && op.Price == 0 && op.RefinishHours == 0 && desc.Length < 3) return true;
            return false;
        }

        private void PopulateOverrideList(string searchText)
        {
            if (_overrideListPanel == null) return;
            _overrideListPanel.Children.Clear();

            var searchLower = searchText.ToLowerInvariant();
            var count = 0;

            foreach (var sheetName in _excelProvider.GetSheetNames())
            {
                var ops = _excelProvider.GetSheetOperations(sheetName);

                // Filter out Excel junk rows first
                var cleanOps = ops.Where(o => !IsJunkExcelRow(o));

                var filtered = string.IsNullOrEmpty(searchLower)
                    ? cleanOps.Take(15).ToList()
                    : cleanOps.Where(o => o.Description?.ToLowerInvariant().Contains(searchLower) == true).ToList();

                if (!filtered.Any()) continue;

                // Sheet header
                _overrideListPanel.Children.Add(new TextBlock
                {
                    Text = sheetName,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
                    Margin = new Thickness(0, 6, 0, 2)
                });

                foreach (var op in filtered.Take(25))
                {
                    _overrideListPanel.Children.Add(CreateOverrideRow(op));
                    count++;
                    if (count >= 75) break;
                }
                if (count >= 75) break;
            }

            if (count == 0)
            {
                _overrideListPanel.Children.Add(new TextBlock
                {
                    Text = "No operations found. Try a different search.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }
        }

        private Border CreateOverrideRow(ExcelOperation op)
        {
            var key = op.Description?.ToLowerInvariant().Trim().Replace("-", " ").Replace("  ", " ") ?? "";
            var existingOverride = _configService.GetOperationOverride(key);

            var rowBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 38, 43, 52)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 1, 0, 1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Default hours
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); // Override hours
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Toggle

            // Description
            var desc = new TextBlock
            {
                Text = op.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(desc, 0);
            grid.Children.Add(desc);

            // Default hours
            var defaultText = op.LaborHours > 0 ? $"{op.LaborHours:F1}h" : (op.Price > 0 ? $"${op.Price:F0}" : "—");
            var defaultLabel = new TextBlock
            {
                Text = defaultText,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(defaultLabel, 1);
            grid.Children.Add(defaultLabel);

            // Override input
            var overrideBox = new NumberBox
            {
                Value = existingOverride?.LaborHours.HasValue == true ? (double)existingOverride.LaborHours.Value : double.NaN,
                PlaceholderText = "Override",
                Minimum = 0,
                Maximum = 100,
                SmallChange = 0.1,
                Width = 80,
                FontSize = 10,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden
            };
            var capturedKey = key;
            overrideBox.ValueChanged += (s, e) =>
            {
                if (double.IsNaN(overrideBox.Value))
                {
                    // Clear override
                    var existing = _configService.GetOperationOverride(capturedKey);
                    if (existing != null)
                    {
                        existing.LaborHours = null;
                        if (existing.RefinishHours == null && existing.Price == null && !existing.Disabled)
                            _configService.SetOperationOverride(capturedKey, null);
                        else
                            _configService.SetOperationOverride(capturedKey, existing);
                    }
                }
                else
                {
                    var existing = _configService.GetOperationOverride(capturedKey) ?? new OperationOverride();
                    existing.LaborHours = (decimal)overrideBox.Value;
                    _configService.SetOperationOverride(capturedKey, existing);
                }
            };
            Grid.SetColumn(overrideBox, 2);
            grid.Children.Add(overrideBox);

            // Disable toggle
            var toggle = new ToggleSwitch
            {
                IsOn = existingOverride?.Disabled != true,
                OnContent = "",
                OffContent = "",
                MinWidth = 0
            };
            toggle.Toggled += (s, e) =>
            {
                var existing = _configService.GetOperationOverride(capturedKey) ?? new OperationOverride();
                existing.Disabled = !toggle.IsOn;
                _configService.SetOperationOverride(capturedKey, existing);
                rowBorder.Opacity = toggle.IsOn ? 1.0 : 0.5;
            };
            rowBorder.Opacity = existingOverride?.Disabled == true ? 0.5 : 1.0;
            Grid.SetColumn(toggle, 3);
            grid.Children.Add(toggle);

            rowBorder.Child = grid;
            return rowBorder;
        }

        private Border BuildCustomOperationsSection()
        {
            var border = CreateSectionBorder();
            var stack = new StackPanel { Spacing = 10 };

            stack.Children.Add(CreateSectionHeader("Your Custom Operations", "\uE710"));

            // Add button
            var addButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE710", FontSize = 12 },
                        new TextBlock { Text = "Add Operation", FontSize = 12 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 46, 120, 67)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            addButton.Click += AddCustomOp_Click;
            stack.Children.Add(addButton);

            // Custom operations list
            _customOpsListPanel = new StackPanel { Spacing = 4 };
            stack.Children.Add(_customOpsListPanel);

            PopulateCustomOpsList();

            stack.Children.Add(new TextBlock
            {
                Text = "Your custom operations are included in ghost estimates alongside the standard ones.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });

            border.Child = stack;
            return border;
        }

        private void PopulateCustomOpsList()
        {
            if (_customOpsListPanel == null) return;
            _customOpsListPanel.Children.Clear();

            var customOps = _configService.GetCustomOperations();

            if (customOps.Count == 0)
            {
                _customOpsListPanel.Children.Add(new TextBlock
                {
                    Text = "No custom operations yet. Click \"Add Operation\" to create one.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 105, 110)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Margin = new Thickness(0, 4, 0, 4)
                });
                return;
            }

            foreach (var op in customOps)
            {
                _customOpsListPanel.Children.Add(CreateCustomOpRow(op));
            }
        }

        private Border CreateCustomOpRow(GhostCustomOperation op)
        {
            var rowBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 38, 43, 52)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 1, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 130, 80)),
                BorderThickness = new Thickness(3, 0, 0, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); // Category
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Hours
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Price
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); // Edit/Delete

            // Description
            var desc = new TextBlock
            {
                Text = op.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(desc, 0);
            grid.Children.Add(desc);

            // Category
            var catLabel = op.Category switch
            {
                "B" => "Body",
                "M" => "Mech",
                "R" => "Refn",
                "F" => "Frame",
                _ => op.Category
            };
            var catText = new TextBlock
            {
                Text = catLabel,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(catText, 1);
            grid.Children.Add(catText);

            // Hours
            var totalHours = op.LaborHours + op.RefinishHours;
            var hoursText = new TextBlock
            {
                Text = totalHours > 0 ? $"{totalHours:F1}h" : "—",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(hoursText, 2);
            grid.Children.Add(hoursText);

            // Price
            var priceText = new TextBlock
            {
                Text = op.Price > 0 ? $"${op.Price:F0}" : "—",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(priceText, 3);
            grid.Children.Add(priceText);

            // Edit + Delete buttons
            var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };

            var editBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE70F", FontSize = 11 },
                Padding = new Thickness(6, 3, 6, 3),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 60, 75)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 220)),
                CornerRadius = new CornerRadius(3),
                MinWidth = 0
            };
            ToolTipService.SetToolTip(editBtn, "Edit");
            var capturedOp = op;
            editBtn.Click += (s, e) => EditCustomOp(capturedOp);
            actionPanel.Children.Add(editBtn);

            var deleteBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 11 },
                Padding = new Thickness(6, 3, 6, 3),
                Background = new SolidColorBrush(Color.FromArgb(255, 75, 40, 40)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 120, 120)),
                CornerRadius = new CornerRadius(3),
                MinWidth = 0
            };
            ToolTipService.SetToolTip(deleteBtn, "Delete");
            deleteBtn.Click += (s, e) =>
            {
                _configService.RemoveCustomOperation(capturedOp.Id);
                PopulateCustomOpsList();
            };
            actionPanel.Children.Add(deleteBtn);

            Grid.SetColumn(actionPanel, 4);
            grid.Children.Add(actionPanel);

            rowBorder.Opacity = op.Enabled ? 1.0 : 0.5;
            rowBorder.Child = grid;
            return rowBorder;
        }

        private async void AddCustomOp_Click(object sender, RoutedEventArgs e)
        {
            await ShowCustomOpDialog(null);
        }

        private async void EditCustomOp(GhostCustomOperation op)
        {
            await ShowCustomOpDialog(op);
        }

        private async Task ShowCustomOpDialog(GhostCustomOperation? existing)
        {
            bool isNew = existing == null;
            var op = existing ?? new GhostCustomOperation();

            var dialogStack = new StackPanel { Spacing = 12, MinWidth = 400 };

            // Description
            var descBox = new TextBox
            {
                Text = op.Description,
                PlaceholderText = "Operation name (e.g., R&I Headliner)",
                FontSize = 12,
                Header = "Description"
            };
            dialogStack.Children.Add(descBox);

            // Category
            var catCombo = new ComboBox
            {
                Header = "Category",
                FontSize = 12,
                Width = 200,
                Items = { "Body", "Mechanical", "Refinish", "Frame" },
                SelectedIndex = op.Category switch
                {
                    "M" => 1,
                    "R" => 2,
                    "F" => 3,
                    _ => 0
                }
            };
            dialogStack.Children.Add(catCombo);

            // Hours row
            var hoursRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            var laborBox = new NumberBox
            {
                Header = "Labor Hours",
                Value = op.LaborHours > 0 ? (double)op.LaborHours : double.NaN,
                PlaceholderText = "0.0",
                Minimum = 0, Maximum = 100,
                SmallChange = 0.1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 130, FontSize = 12
            };
            hoursRow.Children.Add(laborBox);

            var refinishBox = new NumberBox
            {
                Header = "Refinish Hours",
                Value = op.RefinishHours > 0 ? (double)op.RefinishHours : double.NaN,
                PlaceholderText = "0.0",
                Minimum = 0, Maximum = 100,
                SmallChange = 0.1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 130, FontSize = 12
            };
            hoursRow.Children.Add(refinishBox);

            var priceBox = new NumberBox
            {
                Header = "Price ($)",
                Value = op.Price > 0 ? (double)op.Price : double.NaN,
                PlaceholderText = "0.00",
                Minimum = 0, Maximum = 10000,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 130, FontSize = 12
            };
            hoursRow.Children.Add(priceBox);
            dialogStack.Children.Add(hoursRow);

            var dialog = new ContentDialog
            {
                Title = isNew ? "Add Custom Operation" : "Edit Custom Operation",
                Content = dialogStack,
                PrimaryButtonText = isNew ? "Add" : "Save",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (string.IsNullOrWhiteSpace(descBox.Text)) return;

                op.Description = descBox.Text.Trim();
                op.Category = catCombo.SelectedIndex switch
                {
                    1 => "M",
                    2 => "R",
                    3 => "F",
                    _ => "B"
                };
                op.OperationType = op.Category switch
                {
                    "M" => "Mech",
                    "R" => "Rfn",
                    "F" => "Frame",
                    _ => "Body"
                };
                op.LaborHours = !double.IsNaN(laborBox.Value) ? (decimal)laborBox.Value : 0;
                op.RefinishHours = !double.IsNaN(refinishBox.Value) ? (decimal)refinishBox.Value : 0;
                op.Price = !double.IsNaN(priceBox.Value) ? (decimal)priceBox.Value : 0;

                if (isNew)
                    _configService.AddCustomOperation(op);
                else
                    _configService.UpdateCustomOperation(op);

                PopulateCustomOpsList();
            }
        }

        private Border BuildMustHavesSection()
        {
            var border = CreateSectionBorder();
            var stack = new StackPanel { Spacing = 10 };

            stack.Children.Add(CreateSectionHeader("Must-Haves (Scoring Criteria)", "\uE73E"));

            stack.Children.Add(new TextBlock
            {
                Text = "Operations that should always be on every estimate. Configure these from the Must-Haves button on the Estimate Import scoring panel, or manage them here.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190)),
                TextWrapping = TextWrapping.Wrap
            });

            // Must-haves list (read-only display)
            _mustHavesListPanel = new StackPanel { Spacing = 4 };
            stack.Children.Add(_mustHavesListPanel);
            PopulateMustHavesList();

            border.Child = stack;
            return border;
        }

        private void PopulateMustHavesList()
        {
            if (_mustHavesListPanel == null) return;
            _mustHavesListPanel.Children.Clear();

            var mustHaves = _configService.GetMustHaves();

            if (mustHaves.Count == 0)
            {
                _mustHavesListPanel.Children.Add(new TextBlock
                {
                    Text = "No must-haves configured. Use the Must-Haves button on the Estimate Import scoring panel to select from SOP operations.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 105, 110)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 4)
                });
                return;
            }

            foreach (var mh in mustHaves.Where(m => m.Enabled))
            {
                var rowBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 38, 43, 52)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 1, 0, 1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 180, 140, 50)),
                    BorderThickness = new Thickness(3, 0, 0, 0)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var desc = new TextBlock
                {
                    Text = mh.Description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(desc, 0);
                grid.Children.Add(desc);

                var detail = "";
                if (mh.ExpectedPrice > 0) detail += $"${mh.ExpectedPrice:N0}  ";
                if (mh.ExpectedHours > 0) detail += $"{mh.ExpectedHours:F1}h";
                if (detail.Length > 0)
                {
                    var detailText = new TextBlock
                    {
                        Text = detail.Trim(),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 180, 140)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    Grid.SetColumn(detailText, 1);
                    grid.Children.Add(detailText);
                }

                var capturedMh = mh;
                var deleteBtn = new Button
                {
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 10 },
                    Padding = new Thickness(4, 2, 4, 2),
                    Background = new SolidColorBrush(Color.FromArgb(255, 75, 40, 40)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 120, 120)),
                    CornerRadius = new CornerRadius(3),
                    MinWidth = 0,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                deleteBtn.Click += (s, e) =>
                {
                    _configService.RemoveMustHave(capturedMh.Id);
                    PopulateMustHavesList();
                };
                Grid.SetColumn(deleteBtn, 2);
                grid.Children.Add(deleteBtn);

                rowBorder.Child = grid;
                _mustHavesListPanel.Children.Add(rowBorder);
            }
        }

        private Border BuildCategoryTogglesSection()
        {
            var border = CreateSectionBorder();
            var stack = new StackPanel { Spacing = 10 };

            stack.Children.Add(CreateSectionHeader("Category Toggles", "\uE71D"));

            _categoryTogglesPanel = new StackPanel { Spacing = 4 };

            var categories = new[]
            {
                "Part Operations", "Body Operations", "Refinish Operations",
                "Scanning", "Calibration", "Structural", "Frame Operations",
                "SRS Operations", "Cover Car Operations", "Mechanical Operations", "SOP Operations"
            };

            foreach (var cat in categories)
            {
                var isEnabled = _configService.IsCategoryEnabled(cat);

                var checkBox = new CheckBox
                {
                    Content = cat,
                    IsChecked = isEnabled,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 12
                };

                var capturedCat = cat;
                checkBox.Checked += (s, e) => _configService.SetCategoryEnabled(capturedCat, true);
                checkBox.Unchecked += (s, e) => _configService.SetCategoryEnabled(capturedCat, false);

                _categoryTogglesPanel.Children.Add(checkBox);
            }

            stack.Children.Add(_categoryTogglesPanel);

            border.Child = stack;
            return border;
        }

        private StackPanel BuildBottomButtons()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };

            var resetButton = new Button
            {
                Content = "Reset to Defaults",
                Padding = new Thickness(16, 8, 16, 8),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 40, 40)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            resetButton.Click += (s, e) =>
            {
                _configService.ResetToDefaults();
                // Refresh the UI by rebuilding
                Content = null;
                BuildUI();
            };
            panel.Children.Add(resetButton);

            var saveButton = new Button
            {
                Content = "Save & Close",
                Padding = new Thickness(16, 8, 16, 8),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 130, 200)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            saveButton.Click += SaveButton_Click;
            panel.Children.Add(saveButton);

            return panel;
        }

        #endregion

        #region Save Logic

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Save labor rates
            _configService.SetLaborRate("body", GetNullableDecimal(_bodyRateBox));
            _configService.SetLaborRate("paint", GetNullableDecimal(_paintRateBox));
            _configService.SetLaborRate("mech", GetNullableDecimal(_mechRateBox));
            _configService.SetLaborRate("frame", GetNullableDecimal(_frameRateBox));
            _configService.SetLaborRate("glass", GetNullableDecimal(_glassRateBox));

            // Save scanning config
            var scanMethod = _flatRateRadio?.IsChecked == true ? ScanBillingMethod.FlatRate : ScanBillingMethod.LaborHours;
            var flatRateAmount = !double.IsNaN(_flatRateAmountBox?.Value ?? double.NaN) ? (decimal?)_flatRateAmountBox!.Value : null;
            var scanLabor = !double.IsNaN(_scanLaborHoursBox?.Value ?? double.NaN) ? (decimal?)_scanLaborHoursBox!.Value : null;
            _configService.SetScanningConfig(scanMethod, flatRateAmount, scanLabor);

            _configService.SaveConfig();

            // Close the dialog by finding parent ContentDialog
            var parent = this.Parent;
            while (parent != null)
            {
                if (parent is ContentDialog dialog)
                {
                    dialog.Hide();
                    return;
                }
                parent = (parent as FrameworkElement)?.Parent as DependencyObject;
            }
        }

        private decimal? GetNullableDecimal(NumberBox? box)
        {
            if (box == null || double.IsNaN(box.Value))
                return null;
            return (decimal)box.Value;
        }

        #endregion

        #region Helpers

        private Border CreateSectionBorder()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };
        }

        private StackPanel CreateSectionHeader(string title, string glyph)
        {
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
            headerPanel.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            return headerPanel;
        }

        #endregion
    }
}
