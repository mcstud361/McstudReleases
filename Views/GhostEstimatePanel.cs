#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Ghost Estimate Comparison Panel
    ///
    /// Before you write your estimate:
    /// - AI writes one based on the damage description
    /// - You write yours independently
    /// - Compare side-by-side
    /// - "AI found 4 operations you missed"
    /// - "You caught 2 things AI missed"
    ///
    /// Learn from AI, but AI learns from YOU too.
    /// </summary>
    public sealed class GhostEstimatePanel : UserControl
    {
        private readonly GhostEstimateService _ghostService;
        private readonly VehicleDiagramControl _vehicleDiagram;

        // Input fields
        private TextBox? _vehicleInfoBox;
        private TextBox? _damageDescriptionBox;
        private ComboBox? _severityCombo;
        private Button? _generateGhostButton;

        // Your estimate input
        private TextBox? _userEstimateBox;
        private Button? _compareButton;

        // Results display
        private StackPanel? _ghostOperationsPanel;
        private StackPanel? _userOperationsPanel;
        private StackPanel? _comparisonResultsPanel;
        private TextBlock? _summaryText;

        // State
        private GhostEstimateResult? _currentGhostEstimate;
        private List<GhostOperation> _userOperations = new();
        private GhostComparisonResult? _comparisonResult;

        // Events
        public event EventHandler<GhostOperation>? OnOperationAccepted;
        public event EventHandler<GhostComparisonResult>? OnComparisonComplete;

        public GhostEstimatePanel()
        {
            _ghostService = GhostEstimateService.Instance;
            _vehicleDiagram = new VehicleDiagramControl();
            _vehicleDiagram.PanelSelectionChanged += VehicleDiagram_SelectionChanged;

            BuildUI();
        }

        private void BuildUI()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 32, 38)),
                Padding = new Thickness(16)
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var mainStack = new StackPanel { Spacing = 20 };

            // === HEADER ===
            var headerPanel = CreateHeader();
            mainStack.Children.Add(headerPanel);

            // === INPUT SECTION ===
            var inputSection = CreateInputSection();
            mainStack.Children.Add(inputSection);

            // === COMPARISON SECTION ===
            var comparisonSection = CreateComparisonSection();
            mainStack.Children.Add(comparisonSection);

            // === RESULTS SECTION ===
            var resultsSection = CreateResultsSection();
            mainStack.Children.Add(resultsSection);

            scrollViewer.Content = mainStack;
            mainBorder.Child = scrollViewer;
            Content = mainBorder;
        }

        private StackPanel CreateHeader()
        {
            var panel = new StackPanel { Spacing = 8 };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            titleRow.Children.Add(new FontIcon
            {
                Glyph = "\uE8F4", // Ghost icon
                FontSize = 28,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "Ghost Estimate - AI Training Tool",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            panel.Children.Add(titleRow);

            // Better explanation of what this feature does
            var descriptionPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 65)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 4, 0, 0)
            };

            var descStack = new StackPanel { Spacing = 6 };
            descStack.Children.Add(new TextBlock
            {
                Text = "How It Works:",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });

            var steps = new[]
            {
                "1. Describe the damage (vehicle info + damage description)",
                "2. AI generates a \"ghost\" estimate based on your description",
                "3. Write YOUR estimate independently in the comparison section",
                "4. Compare: See what AI found that you missed, and what you caught that AI didn't",
                "5. Learn from each other - AI improves from your expertise!"
            };

            foreach (var step in steps)
            {
                descStack.Children.Add(new TextBlock
                {
                    Text = step,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190)),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            descriptionPanel.Child = descStack;
            panel.Children.Add(descriptionPanel);

            return panel;
        }

        private Border CreateInputSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            // Left: Input fields
            var inputStack = new StackPanel { Spacing = 16 };

            // Section header
            inputStack.Children.Add(new TextBlock
            {
                Text = "STEP 1: Describe the Damage",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });

            // Vehicle info
            var vehicleStack = new StackPanel { Spacing = 4 };
            vehicleStack.Children.Add(new TextBlock
            {
                Text = "Vehicle (Year Make Model)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });
            _vehicleInfoBox = new TextBox
            {
                PlaceholderText = "e.g., 2022 Toyota Camry LE",
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 65, 70, 80))
            };
            vehicleStack.Children.Add(_vehicleInfoBox);
            inputStack.Children.Add(vehicleStack);

            // Damage description
            var damageStack = new StackPanel { Spacing = 4 };
            damageStack.Children.Add(new TextBlock
            {
                Text = "Damage Description",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });
            _damageDescriptionBox = new TextBox
            {
                PlaceholderText = "e.g., Front end collision, bumper pushed in, hood buckled at front edge, headlight broken...",
                FontSize = 13,
                Height = 80,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 65, 70, 80))
            };
            damageStack.Children.Add(_damageDescriptionBox);
            inputStack.Children.Add(damageStack);

            // Severity selector
            var severityStack = new StackPanel { Spacing = 4 };
            severityStack.Children.Add(new TextBlock
            {
                Text = "Damage Severity",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });
            _severityCombo = new ComboBox
            {
                Width = 200,
                FontSize = 13,
                Items = { "Light (scuffs, minor dents)", "Moderate (dents, creases)", "Heavy (buckled, kinked)", "Severe (structural)" },
                SelectedIndex = 1
            };
            severityStack.Children.Add(_severityCombo);
            inputStack.Children.Add(severityStack);

            // Generate button
            _generateGhostButton = new Button
            {
                Content = "Generate AI Ghost Estimate",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(20, 10, 20, 10),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 130, 200)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6)
            };
            _generateGhostButton.Click += GenerateGhostButton_Click;
            inputStack.Children.Add(_generateGhostButton);

            Grid.SetColumn(inputStack, 0);
            grid.Children.Add(inputStack);

            // Right: Vehicle diagram
            var diagramStack = new StackPanel { Spacing = 8, Margin = new Thickness(16, 0, 0, 0) };
            diagramStack.Children.Add(new TextBlock
            {
                Text = "Click Damaged Areas (Optional)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            _vehicleDiagram.HorizontalAlignment = HorizontalAlignment.Center;
            diagramStack.Children.Add(_vehicleDiagram);

            Grid.SetColumn(diagramStack, 1);
            grid.Children.Add(diagramStack);

            border.Child = grid;
            return border;
        }

        private Border CreateComparisonSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); // Divider
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: Ghost estimate
            var ghostPanel = new StackPanel { Spacing = 12 };
            var ghostHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            ghostHeader.Children.Add(new FontIcon
            {
                Glyph = "\uE8F4",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });
            ghostHeader.Children.Add(new TextBlock
            {
                Text = "AI GHOST ESTIMATE",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });
            ghostPanel.Children.Add(ghostHeader);

            _ghostOperationsPanel = new StackPanel { Spacing = 4 };
            _ghostOperationsPanel.Children.Add(new TextBlock
            {
                Text = "Generate a ghost estimate to see AI suggestions",
                FontSize = 12,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 105, 110))
            });
            ghostPanel.Children.Add(_ghostOperationsPanel);

            Grid.SetColumn(ghostPanel, 0);
            grid.Children.Add(ghostPanel);

            // Divider
            var divider = new Border
            {
                Width = 1,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 65, 75)),
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(divider, 1);
            grid.Children.Add(divider);

            // Right: Your estimate
            var userPanel = new StackPanel { Spacing = 12 };
            var userHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            userHeader.Children.Add(new FontIcon
            {
                Glyph = "\uE77B",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 100))
            });
            userHeader.Children.Add(new TextBlock
            {
                Text = "YOUR ESTIMATE",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 100))
            });
            userPanel.Children.Add(userHeader);

            // User estimate input
            _userEstimateBox = new TextBox
            {
                PlaceholderText = "Enter your estimate operations (one per line):\ne.g., Replace front bumper cover\nR&I headlight\nRepair hood\nRefinish bumper, hood, fenders",
                FontSize = 12,
                Height = 150,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 65, 70, 80))
            };
            userPanel.Children.Add(_userEstimateBox);

            _compareButton = new Button
            {
                Content = "Compare Estimates",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(16, 8, 16, 8),
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 160, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6)
            };
            _compareButton.Click += CompareButton_Click;
            userPanel.Children.Add(_compareButton);

            _userOperationsPanel = new StackPanel { Spacing = 4 };
            userPanel.Children.Add(_userOperationsPanel);

            Grid.SetColumn(userPanel, 2);
            grid.Children.Add(userPanel);

            border.Child = grid;
            return border;
        }

        private Border CreateResultsSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed
            };

            var stack = new StackPanel { Spacing = 16 };

            // Summary header
            var summaryHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            summaryHeader.Children.Add(new FontIcon
            {
                Glyph = "\uE9D9",
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100))
            });
            summaryHeader.Children.Add(new TextBlock
            {
                Text = "COMPARISON RESULTS",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100))
            });
            stack.Children.Add(summaryHeader);

            _summaryText = new TextBlock
            {
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(_summaryText);

            _comparisonResultsPanel = new StackPanel { Spacing = 12 };
            stack.Children.Add(_comparisonResultsPanel);

            border.Child = stack;
            return border;
        }

        private void VehicleDiagram_SelectionChanged(object? sender, PanelSelectionChangedEventArgs e)
        {
            // Optional: Update damage description based on selected panels
            System.Diagnostics.Debug.WriteLine($"[Ghost] Panels selected: {string.Join(", ", e.SelectedPanelIds)}");
        }

        private void GenerateGhostButton_Click(object sender, RoutedEventArgs e)
        {
            var vehicleInfo = _vehicleInfoBox?.Text?.Trim() ?? "";
            var damageDesc = _damageDescriptionBox?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(damageDesc))
            {
                ShowMessage("Please enter a damage description");
                return;
            }

            var severity = _severityCombo?.SelectedIndex switch
            {
                0 => "light",
                1 => "moderate",
                2 => "heavy",
                3 => "severe",
                _ => "moderate"
            };

            // Get selected panels from diagram
            var selectedPanels = _vehicleDiagram.SelectedPanels.ToList();
            var impactZones = new List<string>();
            foreach (var panel in selectedPanels)
            {
                impactZones.Add(panel);
            }

            var input = new GhostEstimateInput
            {
                VehicleInfo = vehicleInfo,
                DamageDescription = damageDesc,
                Severity = severity,
                ImpactZones = impactZones,
                SelectedPanels = selectedPanels
            };

            // Generate ghost estimate
            _currentGhostEstimate = _ghostService.GenerateGhostEstimate(input);

            // Display ghost operations
            DisplayGhostOperations(_currentGhostEstimate);
        }

        private void DisplayGhostOperations(GhostEstimateResult estimate)
        {
            _ghostOperationsPanel?.Children.Clear();

            // Group by category
            var grouped = estimate.Operations.GroupBy(o => o.Category);

            foreach (var group in grouped)
            {
                // Category header
                var catHeader = new TextBlock
                {
                    Text = group.Key.ToUpper(),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 125, 130)),
                    Margin = new Thickness(0, 8, 0, 4)
                };
                _ghostOperationsPanel?.Children.Add(catHeader);

                foreach (var op in group)
                {
                    var opRow = CreateOperationRow(op, true);
                    _ghostOperationsPanel?.Children.Add(opRow);
                }
            }

            // Totals - ACTUAL DOLLARS like real estimates
            var totalsPanel = new StackPanel
            {
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 45, 55))
            };

            // Header
            totalsPanel.Children.Add(new TextBlock
            {
                Text = "LABOR SUMMARY",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Body Labor
            totalsPanel.Children.Add(new TextBlock
            {
                Text = $"Body Labor: {estimate.TotalBodyHours:F1}h @ $55/hr = ${estimate.TotalBodyLaborDollars:F2}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 205, 210))
            });

            // Refinish Labor
            totalsPanel.Children.Add(new TextBlock
            {
                Text = $"Refinish Labor: {estimate.TotalRefinishHours:F1}h @ $55/hr = ${estimate.TotalRefinishLaborDollars:F2}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 205, 210))
            });

            // Mech Labor (if any)
            if (estimate.TotalMechLaborDollars > 0)
            {
                var mechHours = estimate.TotalMechLaborDollars / 95m;
                totalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Mechanical Labor: {mechHours:F1}h @ $95/hr = ${estimate.TotalMechLaborDollars:F2}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 205, 210))
                });
            }

            // Frame Labor (if any)
            if (estimate.TotalFrameLaborDollars > 0)
            {
                var frameHours = estimate.TotalFrameLaborDollars / 75m;
                totalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Frame Labor: {frameHours:F1}h @ $75/hr = ${estimate.TotalFrameLaborDollars:F2}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 205, 210))
                });
            }

            // Sublet
            if (estimate.TotalSubletAmount > 0)
            {
                totalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Sublet/Other: ${estimate.TotalSubletAmount:F2}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 205, 210))
                });
            }

            // Separator
            totalsPanel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 85, 95)),
                Margin = new Thickness(0, 8, 0, 8)
            });

            // Grand Total
            totalsPanel.Children.Add(new TextBlock
            {
                Text = $"LABOR TOTAL: ${estimate.GrandTotalLaborDollars:F2}",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 100))
            });

            // Panel count info
            totalsPanel.Children.Add(new TextBlock
            {
                Text = $"Refinish: {estimate.RefinishPanelCount} panels | Blend: {estimate.BlendPanelCount} panels",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            _ghostOperationsPanel?.Children.Add(totalsPanel);

            // Notes
            if (estimate.Notes.Any())
            {
                var notesPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var note in estimate.Notes)
                {
                    notesPanel.Children.Add(new TextBlock
                    {
                        Text = $"Note: {note}",
                        FontSize = 11,
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                _ghostOperationsPanel?.Children.Add(notesPanel);
            }
        }

        private Border CreateOperationRow(GhostOperation op, bool isGhost)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var descStack = new StackPanel();
            descStack.Children.Add(new TextBlock
            {
                Text = op.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Hours/price info
            var infoText = "";
            if (op.LaborHours > 0) infoText += $"{op.LaborHours:F1}h body";
            if (op.RefinishHours > 0) infoText += (infoText.Length > 0 ? " | " : "") + $"{op.RefinishHours:F1}h ref";
            if (op.Price > 0) infoText += (infoText.Length > 0 ? " | " : "") + $"${op.Price:F2}";

            if (!string.IsNullOrEmpty(infoText))
            {
                descStack.Children.Add(new TextBlock
                {
                    Text = infoText,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140))
                });
            }

            Grid.SetColumn(descStack, 0);
            grid.Children.Add(descStack);

            // Confidence indicator for ghost operations
            if (isGhost && op.Confidence < 1.0)
            {
                var confIcon = new FontIcon
                {
                    Glyph = op.Confidence >= 0.8 ? "\uE73E" : (op.Confidence >= 0.6 ? "\uE8E5" : "\uE783"),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(op.Confidence >= 0.8
                        ? Color.FromArgb(255, 100, 220, 100)
                        : (op.Confidence >= 0.6 ? Color.FromArgb(255, 255, 200, 100) : Color.FromArgb(255, 255, 150, 150)))
                };
                ToolTipService.SetToolTip(confIcon, $"Confidence: {op.Confidence:P0}");
                Grid.SetColumn(confIcon, 1);
                grid.Children.Add(confIcon);
            }

            border.Child = grid;
            return border;
        }

        private void CompareButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGhostEstimate == null)
            {
                ShowMessage("Generate a ghost estimate first");
                return;
            }

            var userText = _userEstimateBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(userText))
            {
                ShowMessage("Enter your estimate operations to compare");
                return;
            }

            // Parse user operations from text
            _userOperations = ParseUserOperations(userText);

            // Display parsed user operations
            DisplayUserOperations(_userOperations);

            // Compare
            _comparisonResult = _ghostService.CompareEstimates(_userOperations, _currentGhostEstimate);

            // Display results
            DisplayComparisonResults(_comparisonResult);

            OnComparisonComplete?.Invoke(this, _comparisonResult);
        }

        private List<GhostOperation> ParseUserOperations(string text)
        {
            var operations = new List<GhostOperation>();
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var op = ParseOperationLine(trimmed);
                if (op != null)
                {
                    operations.Add(op);
                }
            }

            return operations;
        }

        private GhostOperation? ParseOperationLine(string line)
        {
            var lower = line.ToLower();

            // Detect operation type
            var opType = "Repair"; // Default
            if (lower.StartsWith("replace") || lower.Contains("rplc") || lower.Contains(" rp "))
                opType = "Replace";
            else if (lower.StartsWith("r&i") || lower.Contains("r & i") || lower.Contains("remove and install"))
                opType = "R&I";
            else if (lower.StartsWith("refinish") || lower.Contains("rfn") || lower.StartsWith("paint"))
                opType = "Refinish";
            else if (lower.StartsWith("blend"))
                opType = "Blend";
            else if (lower.StartsWith("repair") || lower.Contains("rpr"))
                opType = "Repair";
            else if (lower.Contains("sublet") || lower.Contains("scan") || lower.Contains("calibrat"))
                opType = "Sublet";

            // Extract part name (remove operation keywords)
            var partName = line
                .Replace("Replace", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Repair", "", StringComparison.OrdinalIgnoreCase)
                .Replace("R&I", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Refinish", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Blend", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Sublet", "", StringComparison.OrdinalIgnoreCase)
                .Trim()
                .Trim('-', ':', ' ');

            if (string.IsNullOrEmpty(partName))
                partName = line;

            return new GhostOperation
            {
                OperationType = opType,
                PartName = partName.ToLower(),
                Description = line,
                Category = opType == "Refinish" || opType == "Blend" ? "Refinish Operations" : "Part Operations"
            };
        }

        private void DisplayUserOperations(List<GhostOperation> operations)
        {
            _userOperationsPanel?.Children.Clear();

            if (!operations.Any())
            {
                _userOperationsPanel?.Children.Add(new TextBlock
                {
                    Text = "No operations parsed",
                    FontSize = 12,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160))
                });
                return;
            }

            _userOperationsPanel?.Children.Add(new TextBlock
            {
                Text = $"Parsed {operations.Count} operations:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 155, 160)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            foreach (var op in operations)
            {
                var row = CreateOperationRow(op, false);
                _userOperationsPanel?.Children.Add(row);
            }
        }

        private void DisplayComparisonResults(GhostComparisonResult result)
        {
            // Show results section
            var resultsSection = (_comparisonResultsPanel?.Parent as StackPanel)?.Parent as Border;
            if (resultsSection != null)
                resultsSection.Visibility = Visibility.Visible;

            // Summary
            if (_summaryText != null)
                _summaryText.Text = result.Summary;

            _comparisonResultsPanel?.Children.Clear();

            // Add visual score card at top
            var scoreCard = CreateScoreCard(result);
            _comparisonResultsPanel?.Children.Add(scoreCard);

            // AI found missing
            if (result.GhostFoundMissing.Any())
            {
                var missingSection = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
                var missingHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                missingHeader.Children.Add(new FontIcon
                {
                    Glyph = "\uE7BA",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100))
                });
                missingHeader.Children.Add(new TextBlock
                {
                    Text = $"AI Found {result.GhostFoundMissing.Count} Operations You Might Have Missed:",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100))
                });
                missingSection.Children.Add(missingHeader);

                foreach (var missing in result.GhostFoundMissing)
                {
                    var row = CreateMissingOperationRow(missing);
                    missingSection.Children.Add(row);
                }

                _comparisonResultsPanel?.Children.Add(missingSection);
            }

            // User found extra
            if (result.UserFoundExtra.Any())
            {
                var extraSection = new StackPanel { Spacing = 4, Margin = new Thickness(0, 12, 0, 0) };
                var extraHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                extraHeader.Children.Add(new FontIcon
                {
                    Glyph = "\uE8FB",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 100))
                });
                extraHeader.Children.Add(new TextBlock
                {
                    Text = $"You Caught {result.UserFoundExtra.Count} Things AI Didn't:",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 100))
                });
                extraSection.Children.Add(extraHeader);

                foreach (var extra in result.UserFoundExtra)
                {
                    var row = CreateExtraOperationRow(extra);
                    extraSection.Children.Add(row);
                }

                _comparisonResultsPanel?.Children.Add(extraSection);
            }

            // Both have
            if (result.BothHave.Any())
            {
                var matchSection = new StackPanel { Spacing = 4, Margin = new Thickness(0, 12, 0, 0) };
                var matchHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                matchHeader.Children.Add(new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
                });
                matchHeader.Children.Add(new TextBlock
                {
                    Text = $"{result.BothHave.Count} Operations Match ({result.MatchPercentage:F0}% Agreement)",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
                });
                matchSection.Children.Add(matchHeader);

                _comparisonResultsPanel?.Children.Add(matchSection);
            }

            // Action buttons
            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 0)
            };

            if (result.GhostFoundMissing.Any())
            {
                var acceptAllBtn = new Button
                {
                    Content = "Accept All AI Suggestions",
                    Padding = new Thickness(12, 8, 12, 8),
                    Background = new SolidColorBrush(Color.FromArgb(255, 60, 130, 200)),
                    Foreground = new SolidColorBrush(Colors.White),
                    CornerRadius = new CornerRadius(4)
                };
                acceptAllBtn.Click += (s, e) => AcceptAllSuggestions();
                actionsPanel.Children.Add(acceptAllBtn);
            }

            if (result.UserFoundExtra.Any())
            {
                var teachAIBtn = new Button
                {
                    Content = "Teach AI Your Patterns",
                    Padding = new Thickness(12, 8, 12, 8),
                    Background = new SolidColorBrush(Color.FromArgb(255, 60, 160, 60)),
                    Foreground = new SolidColorBrush(Colors.White),
                    CornerRadius = new CornerRadius(4)
                };
                teachAIBtn.Click += (s, e) => TeachAIUserPatterns();
                actionsPanel.Children.Add(teachAIBtn);
            }

            _comparisonResultsPanel?.Children.Add(actionsPanel);
        }

        private Border CreateScoreCard(GhostComparisonResult result)
        {
            // Calculate scores
            int totalGhostOps = result.BothHave.Count + result.GhostFoundMissing.Count;
            int totalUserOps = result.BothHave.Count + result.UserFoundExtra.Count;
            int matched = result.BothHave.Count;

            double userAccuracy = totalGhostOps > 0 ? (matched * 100.0 / totalGhostOps) : 0;
            double aiMissRate = totalUserOps > 0 ? (result.UserFoundExtra.Count * 100.0 / totalUserOps) : 0;

            var scoreCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 40, 50)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Your Accuracy Score
            var accuracyPanel = CreateScoreItem(
                "📊 Your Accuracy",
                $"{userAccuracy:F0}%",
                userAccuracy >= 80 ? Color.FromArgb(255, 100, 220, 100) :
                userAccuracy >= 60 ? Color.FromArgb(255, 255, 200, 100) :
                Color.FromArgb(255, 255, 130, 100),
                $"You matched {matched} of {totalGhostOps} AI operations"
            );
            Grid.SetColumn(accuracyPanel, 0);
            grid.Children.Add(accuracyPanel);

            // Things You Caught
            var extraPanel = CreateScoreItem(
                "🎯 Extras Found",
                $"{result.UserFoundExtra.Count}",
                Color.FromArgb(255, 100, 180, 255),
                "Operations you found that AI missed"
            );
            Grid.SetColumn(extraPanel, 1);
            grid.Children.Add(extraPanel);

            // Things to Review
            var reviewPanel = CreateScoreItem(
                "⚠️ To Review",
                $"{result.GhostFoundMissing.Count}",
                result.GhostFoundMissing.Count == 0 ? Color.FromArgb(255, 100, 220, 100) :
                Color.FromArgb(255, 255, 180, 100),
                "AI suggestions to consider adding"
            );
            Grid.SetColumn(reviewPanel, 2);
            grid.Children.Add(reviewPanel);

            scoreCard.Child = grid;
            return scoreCard;
        }

        private StackPanel CreateScoreItem(string title, string value, Color color, string subtitle)
        {
            var panel = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 145, 150)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 105, 110)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 140
            });

            return panel;
        }

        private Border CreateMissingOperationRow(MissingOperation missing)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 50, 40)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = missing.Operation.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"Reason: {missing.Reason} ({missing.Confidence:P0} confidence)",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 180, 120))
            });

            Grid.SetColumn(stack, 0);
            grid.Children.Add(stack);

            var acceptBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE710", FontSize = 12 },
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Colors.Transparent)
            };
            ToolTipService.SetToolTip(acceptBtn, "Accept this suggestion");
            acceptBtn.Click += (s, e) => AcceptSuggestion(missing.Operation);
            Grid.SetColumn(acceptBtn, 1);
            grid.Children.Add(acceptBtn);

            border.Child = grid;
            return border;
        }

        private Border CreateExtraOperationRow(ExtraOperation extra)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 60, 40)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = extra.Operation.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "AI will learn from this",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 200, 120))
            });

            border.Child = stack;
            return border;
        }

        private void AcceptSuggestion(GhostOperation op)
        {
            OnOperationAccepted?.Invoke(this, op);
            ShowMessage($"Added: {op.Description}");
        }

        private void AcceptAllSuggestions()
        {
            if (_comparisonResult == null) return;

            foreach (var missing in _comparisonResult.GhostFoundMissing)
            {
                OnOperationAccepted?.Invoke(this, missing.Operation);
            }

            ShowMessage($"Added {_comparisonResult.GhostFoundMissing.Count} operations to your estimate");
        }

        private void TeachAIUserPatterns()
        {
            if (_comparisonResult == null) return;

            _ghostService.LearnFromComparison(
                _comparisonResult,
                new List<GhostOperation>(), // No suggestions were accepted in this case
                new List<GhostOperation>()  // No rejections
            );

            ShowMessage($"AI is learning from your {_comparisonResult.UserFoundExtra.Count} additional operations");
        }

        private void ShowMessage(string message)
        {
            // Simple feedback - could be enhanced with a proper notification system
            System.Diagnostics.Debug.WriteLine($"[Ghost] {message}");
        }
    }
}
