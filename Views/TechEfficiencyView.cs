#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    public sealed class TechEfficiencyView : UserControl
    {
        private readonly TechEfficiencyService _service = TechEfficiencyService.Instance;

        // Hours Calculator
        private ComboBox? _calcTechSelector;
        private TextBox? _calcEfficiencyInput;
        private TextBox? _calcBookHoursInput;
        private TextBlock? _calcExpectedResult;
        private TextBlock? _calcDeltaText;

        // Technician roster
        private TextBox? _techNameInput;
        private ComboBox? _techTierSelector;
        private TextBox? _techEfficiencyInput;
        private StackPanel? _techRosterList;

        // Job entry
        private ComboBox? _techSelector;
        private TextBox? _roNumberInput;
        private TextBox? _billedHoursInput;
        private TextBox? _actualHoursInput;
        private TextBlock? _efficiencyResult;
        private Border? _efficiencyBadge;
        private TextBlock? _efficiencyLabel;

        // Summary
        private ComboBox? _summaryTechSelector;
        private DatePicker? _summaryFromDate;
        private DatePicker? _summaryToDate;
        private TextBlock? _summaryBilledText;
        private TextBlock? _summaryActualText;
        private TextBlock? _summaryEfficiencyText;
        private TextBlock? _summaryProductivityText;
        private TextBlock? _summaryEntryCountText;
        private Border? _summaryBenchmarkBadge;
        private TextBlock? _summaryBenchmarkLabel;
        private Grid? _summaryResultsGrid;

        // Benchmark bar
        private Border? _benchmarkIndicator;
        private Grid? _benchmarkBarContainer;

        // History
        private StackPanel? _historyList;

        public TechEfficiencyView()
        {
            BuildUI();
            RefreshTechnicianLists();
            RefreshHistory();
        }

        private void BuildUI()
        {
            var root = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var mainStack = new StackPanel
            {
                Spacing = 16,
                Padding = new Thickness(20)
            };

            // 1. Header
            mainStack.Children.Add(BuildHeader());

            // 2. Hours Calculator (top priority)
            mainStack.Children.Add(BuildHoursCalculator());

            // 3. Technician Roster (enhanced with tiers)
            mainStack.Children.Add(BuildTechnicianRoster());

            // 4. Benchmark reference bar
            mainStack.Children.Add(BuildBenchmarkBar());

            // 5. Log a Job (secondary)
            mainStack.Children.Add(BuildJobCalculator());

            // 6. Period Summary
            mainStack.Children.Add(BuildSummarySection());

            // 7. History log
            mainStack.Children.Add(BuildHistorySection());

            root.Content = mainStack;
            Content = root;
        }

        // ============================================================
        //  HEADER
        // ============================================================

        private FrameworkElement BuildHeader()
        {
            var stack = new StackPanel { Spacing = 4 };

            stack.Children.Add(new TextBlock
            {
                Text = "Technician Efficiency & Planning",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Assign techs a tier (A/B/C) with efficiency %, then use the Hours Calculator to predict job duration. Log jobs to track actual performance over time.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                TextWrapping = TextWrapping.Wrap
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Expected Hours = Book Hours \u00f7 (Efficiency% / 100)   |   A Tech 150% beats book time, B Tech 100% matches it, C Tech 75% takes longer",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });

            return stack;
        }

        // ============================================================
        //  HOURS CALCULATOR
        // ============================================================

        private FrameworkElement BuildHoursCalculator()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 45, 65)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 100, 160)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20, 16, 20, 16)
            };

            var outer = new StackPanel { Spacing = 12 };

            // Title row
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleRow.Children.Add(new TextBlock
            {
                Text = "Hours Calculator",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 200, 255)),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "How long will this job take?",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 160, 190)),
                VerticalAlignment = VerticalAlignment.Center
            });
            outer.Children.Add(titleRow);

            // Controls row
            var controlsRow = new Grid();
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); // Tech selector
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) }); // Efficiency %
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) }); // Book hours

            _calcTechSelector = new ComboBox
            {
                PlaceholderText = "Pick a technician (or use manual %)",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 55, 75)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            };
            _calcTechSelector.SelectionChanged += OnCalcTechChanged;
            var techLabeled = CreateLabeledControl("Technician", _calcTechSelector);
            Grid.SetColumn(techLabeled, 0);
            controlsRow.Children.Add(techLabeled);

            _calcEfficiencyInput = new TextBox
            {
                PlaceholderText = "100",
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 55, 75)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                Text = "100"
            };
            _calcEfficiencyInput.TextChanged += OnCalcInputChanged;
            var effLabeled = CreateLabeledControl("Efficiency %", _calcEfficiencyInput);
            Grid.SetColumn(effLabeled, 2);
            controlsRow.Children.Add(effLabeled);

            _calcBookHoursInput = new TextBox
            {
                PlaceholderText = "0.0",
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 55, 75)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            };
            _calcBookHoursInput.TextChanged += OnCalcInputChanged;
            var bookLabeled = CreateLabeledControl("Book Hours", _calcBookHoursInput);
            Grid.SetColumn(bookLabeled, 4);
            controlsRow.Children.Add(bookLabeled);

            outer.Children.Add(controlsRow);

            // Result display
            var resultRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };

            resultRow.Children.Add(new TextBlock
            {
                Text = "Expected Actual Hours:",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 200, 220)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _calcExpectedResult = new TextBlock
            {
                Text = "\u2014",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            resultRow.Children.Add(_calcExpectedResult);

            _calcDeltaText = new TextBlock
            {
                Text = "",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 4, 0, 0)
            };
            resultRow.Children.Add(_calcDeltaText);

            outer.Children.Add(resultRow);

            border.Child = outer;
            return border;
        }

        // ============================================================
        //  TECHNICIAN ROSTER (with Tiers)
        // ============================================================

        private FrameworkElement BuildTechnicianRoster()
        {
            var card = CreateCard("Technician Roster");

            var inner = new StackPanel { Spacing = 8 };

            // Add tech row: Name | Tier dropdown | Efficiency % | Add button
            var addRow = new Grid();
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // Name
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90, GridUnitType.Pixel) }); // Tier
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90, GridUnitType.Pixel) }); // Eff%
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Button

            _techNameInput = new TextBox
            {
                PlaceholderText = "Technician name",
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            };
            Grid.SetColumn(_techNameInput, 0);
            addRow.Children.Add(_techNameInput);

            _techTierSelector = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _techTierSelector.Items.Add(new ComboBoxItem { Content = "A Tech", Tag = "A" });
            _techTierSelector.Items.Add(new ComboBoxItem { Content = "B Tech", Tag = "B" });
            _techTierSelector.Items.Add(new ComboBoxItem { Content = "C Tech", Tag = "C" });
            _techTierSelector.SelectedIndex = 1; // Default B
            _techTierSelector.SelectionChanged += OnTierSelectionChanged;
            Grid.SetColumn(_techTierSelector, 2);
            addRow.Children.Add(_techTierSelector);

            _techEfficiencyInput = new TextBox
            {
                PlaceholderText = "100",
                Text = "100",
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumn(_techEfficiencyInput, 4);
            addRow.Children.Add(_techEfficiencyInput);

            var addButton = new Button
            {
                Content = "Add",
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                Padding = new Thickness(16, 6, 16, 6)
            };
            addButton.Click += OnAddTechnician;
            Grid.SetColumn(addButton, 6);
            addRow.Children.Add(addButton);

            inner.Children.Add(addRow);

            // Column headers for roster
            var headerRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80, GridUnitType.Pixel) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Pixel) });

            AddHeaderCell(headerRow, "Name", 0);
            AddHeaderCell(headerRow, "Tier", 1);
            AddHeaderCell(headerRow, "Eff %", 2);
            inner.Children.Add(headerRow);

            _techRosterList = new StackPanel { Spacing = 2 };
            inner.Children.Add(_techRosterList);

            ((StackPanel)((Border)card).Child!).Children.Add(inner);
            return card;
        }

        // ============================================================
        //  PER-JOB CALCULATOR (Log a Job)
        // ============================================================

        private FrameworkElement BuildJobCalculator()
        {
            var card = CreateCard("Log a Job");

            var inner = new StackPanel { Spacing = 10 };

            // Tech selector
            _techSelector = new ComboBox
            {
                PlaceholderText = "Select technician",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            };
            inner.Children.Add(CreateLabeledControl("Technician", _techSelector));

            // RO Number
            _roNumberInput = new TextBox
            {
                PlaceholderText = "Optional",
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            };
            inner.Children.Add(CreateLabeledControl("RO Number", _roNumberInput));

            // Hours row
            var hoursRow = new Grid();
            hoursRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hoursRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
            hoursRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _billedHoursInput = new TextBox
            {
                PlaceholderText = "0.0",
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            };
            _billedHoursInput.TextChanged += OnHoursChanged;
            var billedLabeled = CreateLabeledControl("Billed Hours", _billedHoursInput);
            Grid.SetColumn(billedLabeled, 0);
            hoursRow.Children.Add(billedLabeled);

            _actualHoursInput = new TextBox
            {
                PlaceholderText = "0.0",
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            };
            _actualHoursInput.TextChanged += OnHoursChanged;
            var actualLabeled = CreateLabeledControl("Actual Hours", _actualHoursInput);
            Grid.SetColumn(actualLabeled, 2);
            hoursRow.Children.Add(actualLabeled);

            inner.Children.Add(hoursRow);

            // Efficiency result display
            var resultRow = new Grid();
            resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var resultStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            resultStack.Children.Add(new TextBlock
            {
                Text = "Efficiency:",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center
            });
            _efficiencyResult = new TextBlock
            {
                Text = "\u2014",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            resultStack.Children.Add(_efficiencyResult);

            _efficiencyLabel = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            _efficiencyBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Child = _efficiencyLabel,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center
            };
            resultStack.Children.Add(_efficiencyBadge);

            Grid.SetColumn(resultStack, 0);
            resultRow.Children.Add(resultStack);

            var saveButton = new Button
            {
                Content = "Save Entry",
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                Padding = new Thickness(16, 6, 16, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            saveButton.Click += OnSaveEntry;
            Grid.SetColumn(saveButton, 1);
            resultRow.Children.Add(saveButton);

            inner.Children.Add(resultRow);

            ((StackPanel)((Border)card).Child!).Children.Add(inner);
            return card;
        }

        // ============================================================
        //  BENCHMARK BAR
        // ============================================================

        private FrameworkElement BuildBenchmarkBar()
        {
            var card = CreateCard("Benchmark Reference");

            var inner = new StackPanel { Spacing = 6 };

            // Color bar
            var barOuter = new Grid { Height = 28, Margin = new Thickness(0, 4, 0, 0) };
            barOuter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80, GridUnitType.Star) });   // <80 red
            barOuter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20, GridUnitType.Star) });   // 80-99 yellow
            barOuter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25, GridUnitType.Star) });   // 100-124 green
            barOuter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25, GridUnitType.Star) });   // 125+ gold

            var redZone = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 180, 60, 60)), CornerRadius = new CornerRadius(4, 0, 0, 4) };
            Grid.SetColumn(redZone, 0);
            barOuter.Children.Add(redZone);

            var yellowZone = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 200, 180, 60)) };
            Grid.SetColumn(yellowZone, 1);
            barOuter.Children.Add(yellowZone);

            var greenZone = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 60, 160, 60)) };
            Grid.SetColumn(greenZone, 2);
            barOuter.Children.Add(greenZone);

            var goldZone = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 50, 130, 200)), CornerRadius = new CornerRadius(0, 4, 4, 0) };
            Grid.SetColumn(goldZone, 3);
            barOuter.Children.Add(goldZone);

            // Indicator (positioned over the bar)
            _benchmarkBarContainer = barOuter;
            _benchmarkIndicator = new Border
            {
                Width = 3,
                Background = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };
            barOuter.Children.Add(_benchmarkIndicator);
            Grid.SetColumnSpan(_benchmarkIndicator, 4);

            inner.Children.Add(barOuter);

            // Labels
            var labelRow = new Grid();
            labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80, GridUnitType.Star) });
            labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20, GridUnitType.Star) });
            labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25, GridUnitType.Star) });
            labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25, GridUnitType.Star) });

            var redLabel = new TextBlock { Text = "< 80% Needs Improvement", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 120, 120)), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(redLabel, 0);
            labelRow.Children.Add(redLabel);

            var yellowLabel = new TextBlock { Text = "80-99%", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 120)), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(yellowLabel, 1);
            labelRow.Children.Add(yellowLabel);

            var greenLabel = new TextBlock { Text = "100-124%", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 200, 120)), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(greenLabel, 2);
            labelRow.Children.Add(greenLabel);

            var goldLabel = new TextBlock { Text = "125%+ Top Tier", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 180, 220)), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(goldLabel, 3);
            labelRow.Children.Add(goldLabel);

            inner.Children.Add(labelRow);

            ((StackPanel)((Border)card).Child!).Children.Add(inner);
            return card;
        }

        // ============================================================
        //  SUMMARY SECTION
        // ============================================================

        private FrameworkElement BuildSummarySection()
        {
            var card = CreateCard("Period Summary");

            var inner = new StackPanel { Spacing = 10 };

            // Controls row
            var controlsRow = new Grid();
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlsRow.ColumnSpacing = 10;

            _summaryTechSelector = new ComboBox
            {
                PlaceholderText = "Select technician",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45))
            };
            var techLabeled = CreateLabeledControl("Technician", _summaryTechSelector);
            Grid.SetColumn(techLabeled, 0);
            controlsRow.Children.Add(techLabeled);

            _summaryFromDate = new DatePicker
            {
                Date = DateTimeOffset.Now.AddDays(-(int)DateTime.Today.DayOfWeek + 1),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var fromLabeled = CreateLabeledControl("From", _summaryFromDate);
            Grid.SetColumn(fromLabeled, 1);
            controlsRow.Children.Add(fromLabeled);

            _summaryToDate = new DatePicker
            {
                Date = DateTimeOffset.Now,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var toLabeled = CreateLabeledControl("To", _summaryToDate);
            Grid.SetColumn(toLabeled, 2);
            controlsRow.Children.Add(toLabeled);

            var calcButton = new Button
            {
                Content = "Calculate",
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                Padding = new Thickness(16, 6, 16, 6),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 1)
            };
            calcButton.Click += OnCalculateSummary;
            Grid.SetColumn(calcButton, 3);
            controlsRow.Children.Add(calcButton);

            inner.Children.Add(controlsRow);

            // Results grid
            var resultsGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 45, 60)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 12, 16, 12),
                Visibility = Visibility.Collapsed
            };
            resultsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _summaryEntryCountText = CreateStatBlock("Jobs", "\u2014", out var entryCountBlock);
            Grid.SetColumn(entryCountBlock, 0);
            resultsGrid.Children.Add(entryCountBlock);

            _summaryBilledText = CreateStatBlock("Billed Hrs", "\u2014", out var billedBlock);
            Grid.SetColumn(billedBlock, 1);
            resultsGrid.Children.Add(billedBlock);

            _summaryActualText = CreateStatBlock("Actual Hrs", "\u2014", out var actualBlock);
            Grid.SetColumn(actualBlock, 2);
            resultsGrid.Children.Add(actualBlock);

            _summaryEfficiencyText = CreateStatBlock("Efficiency", "\u2014", out var effBlock);
            Grid.SetColumn(effBlock, 3);
            resultsGrid.Children.Add(effBlock);

            _summaryProductivityText = CreateStatBlock("Productivity", "\u2014", out var prodBlock);
            Grid.SetColumn(prodBlock, 4);
            resultsGrid.Children.Add(prodBlock);

            _summaryBenchmarkBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _summaryBenchmarkLabel = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
            _summaryBenchmarkBadge.Child = _summaryBenchmarkLabel;

            var resultsStack = new StackPanel { Spacing = 4 };
            resultsStack.Children.Add(resultsGrid);
            resultsStack.Children.Add(_summaryBenchmarkBadge);

            inner.Children.Add(resultsStack);
            _summaryResultsGrid = resultsGrid;

            ((StackPanel)((Border)card).Child!).Children.Add(inner);
            return card;
        }

        // ============================================================
        //  HISTORY LOG
        // ============================================================

        private FrameworkElement BuildHistorySection()
        {
            var card = CreateCard("History Log");

            var inner = new StackPanel { Spacing = 6 };

            // Column headers
            var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90, GridUnitType.Pixel) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90, GridUnitType.Pixel) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80, GridUnitType.Pixel) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Pixel) });

            AddHeaderCell(headerRow, "Date", 0);
            AddHeaderCell(headerRow, "Technician", 1);
            AddHeaderCell(headerRow, "RO #", 2);
            AddHeaderCell(headerRow, "Billed", 3);
            AddHeaderCell(headerRow, "Actual", 4);
            AddHeaderCell(headerRow, "Eff %", 5);

            inner.Children.Add(headerRow);

            _historyList = new StackPanel { Spacing = 2 };
            inner.Children.Add(_historyList);

            ((StackPanel)((Border)card).Child!).Children.Add(inner);
            return card;
        }

        // ============================================================
        //  UI HELPERS
        // ============================================================

        private Border CreateCard(string title)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 38, 42, 48)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 65)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            });

            border.Child = stack;
            return border;
        }

        private StackPanel CreateLabeledControl(string label, UIElement control)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            stack.Children.Add(control);
            return stack;
        }

        private TextBlock CreateStatBlock(string label, string value, out StackPanel container)
        {
            container = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
            container.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            container.Children.Add(valueText);
            return valueText;
        }

        private void AddHeaderCell(Grid row, string text, int column)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            };
            Grid.SetColumn(tb, column);
            row.Children.Add(tb);
        }

        private (SolidColorBrush bg, SolidColorBrush fg) GetBenchmarkColors(decimal efficiency)
        {
            if (efficiency >= 125)
                return (new SolidColorBrush(Color.FromArgb(255, 30, 80, 140)), new SolidColorBrush(Color.FromArgb(255, 140, 200, 255)));
            if (efficiency >= 100)
                return (new SolidColorBrush(Color.FromArgb(255, 30, 80, 30)), new SolidColorBrush(Color.FromArgb(255, 120, 220, 120)));
            if (efficiency >= 80)
                return (new SolidColorBrush(Color.FromArgb(255, 90, 80, 20)), new SolidColorBrush(Color.FromArgb(255, 220, 200, 80)));
            return (new SolidColorBrush(Color.FromArgb(255, 90, 30, 30)), new SolidColorBrush(Color.FromArgb(255, 220, 120, 120)));
        }

        private static Border CreateTierBadge(TechTier tier)
        {
            var (bg, fg, label) = tier switch
            {
                TechTier.A => (Color.FromArgb(255, 30, 80, 140), Color.FromArgb(255, 140, 200, 255), "A"),
                TechTier.C => (Color.FromArgb(255, 120, 80, 20), Color.FromArgb(255, 220, 180, 80), "C"),
                _ => (Color.FromArgb(255, 30, 80, 30), Color.FromArgb(255, 120, 220, 120), "B")
            };

            return new Border
            {
                Background = new SolidColorBrush(bg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(fg),
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };
        }

        private static Color GetCalcResultColor(decimal efficiencyPercent)
        {
            if (efficiencyPercent >= 125) return Color.FromArgb(255, 140, 200, 255);
            if (efficiencyPercent >= 100) return Color.FromArgb(255, 120, 220, 120);
            if (efficiencyPercent >= 80) return Color.FromArgb(255, 220, 200, 80);
            return Color.FromArgb(255, 220, 120, 120);
        }

        // ============================================================
        //  DATA REFRESH
        // ============================================================

        private void RefreshTechnicianLists()
        {
            var techs = _service.GetTechnicians();

            // Roster list
            _techRosterList?.Children.Clear();
            foreach (var tech in techs)
            {
                var row = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)),
                    Padding = new Thickness(8, 6, 8, 6),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 1, 0, 0)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80, GridUnitType.Pixel) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Pixel) });

                row.Children.Add(new TextBlock
                {
                    Text = tech.Name,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var tierBadge = CreateTierBadge(tech.Tier);
                tierBadge.HorizontalAlignment = HorizontalAlignment.Center;
                tierBadge.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(tierBadge, 1);
                row.Children.Add(tierBadge);

                var effText = new TextBlock
                {
                    Text = $"{tech.EfficiencyPercent:F0}%",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(GetCalcResultColor(tech.EfficiencyPercent)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(effText, 2);
                row.Children.Add(effText);

                var removeBtn = new Button
                {
                    Content = "\u2715",
                    FontSize = 10,
                    Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                    Padding = new Thickness(6, 2, 6, 2),
                    Tag = tech.Id
                };
                removeBtn.Click += OnRemoveTechnician;
                Grid.SetColumn(removeBtn, 3);
                row.Children.Add(removeBtn);

                _techRosterList?.Children.Add(row);
            }

            if (techs.Count == 0)
            {
                _techRosterList?.Children.Add(new TextBlock
                {
                    Text = "No technicians added yet",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }

            // Combo boxes (job logging + summary)
            RefreshComboBox(_techSelector, techs, showTier: true);
            RefreshComboBox(_summaryTechSelector, techs, showTier: true);

            // Hours Calculator combo
            RefreshCalcComboBox(techs);
        }

        private void RefreshComboBox(ComboBox? combo, List<Technician> techs, bool showTier = false)
        {
            if (combo == null) return;
            var selectedId = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
            combo.Items.Clear();
            foreach (var tech in techs)
            {
                var label = showTier
                    ? $"{tech.Name} \u2014 {tech.Tier} Tech {tech.EfficiencyPercent:F0}%"
                    : tech.Name;
                var item = new ComboBoxItem { Content = label, Tag = tech.Id };
                combo.Items.Add(item);
                if (tech.Id == selectedId) combo.SelectedItem = item;
            }
        }

        private void RefreshCalcComboBox(List<Technician> techs)
        {
            if (_calcTechSelector == null) return;
            var selectedId = (_calcTechSelector.SelectedItem as ComboBoxItem)?.Tag as string;
            _calcTechSelector.Items.Clear();

            // First option: manual entry
            var manualItem = new ComboBoxItem { Content = "(Manual efficiency %)", Tag = "manual" };
            _calcTechSelector.Items.Add(manualItem);

            foreach (var tech in techs)
            {
                var label = $"{tech.Name} \u2014 {tech.Tier} Tech {tech.EfficiencyPercent:F0}%";
                var item = new ComboBoxItem { Content = label, Tag = tech.Id };
                _calcTechSelector.Items.Add(item);
                if (tech.Id == selectedId) _calcTechSelector.SelectedItem = item;
            }

            if (_calcTechSelector.SelectedItem == null)
                _calcTechSelector.SelectedItem = manualItem;
        }

        private void RefreshHistory()
        {
            if (_historyList == null) return;
            _historyList.Children.Clear();

            var entries = _service.GetEntries();
            var techs = _service.GetTechnicians().ToDictionary(t => t.Id, t => t.Name);

            if (entries.Count == 0)
            {
                _historyList.Children.Add(new TextBlock
                {
                    Text = "No entries yet. Log a job above to get started.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            foreach (var entry in entries.Take(50))
            {
                var eff = TechEfficiencyService.CalculateEfficiency(entry.BilledHours, entry.ActualHours);
                var (_, effFg) = GetBenchmarkColors(eff);
                var techName = techs.GetValueOrDefault(entry.TechnicianId, "Unknown");

                var row = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)),
                    Padding = new Thickness(4, 4, 4, 4),
                    CornerRadius = new CornerRadius(3)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90, GridUnitType.Pixel) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90, GridUnitType.Pixel) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80, GridUnitType.Pixel) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Pixel) });

                AddCell(row, entry.Date.ToString("MM/dd/yyyy"), 0);
                AddCell(row, techName, 1);
                AddCell(row, entry.RONumber ?? "\u2014", 2);
                AddCell(row, entry.BilledHours.ToString("F1"), 3);
                AddCell(row, entry.ActualHours.ToString("F1"), 4);

                var effText = new TextBlock
                {
                    Text = $"{eff:F1}%",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = effFg,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(effText, 5);
                row.Children.Add(effText);

                var deleteBtn = new Button
                {
                    Content = "\u2715",
                    FontSize = 9,
                    Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                    Padding = new Thickness(4, 1, 4, 1),
                    Tag = entry.Id
                };
                deleteBtn.Click += OnDeleteEntry;
                Grid.SetColumn(deleteBtn, 6);
                row.Children.Add(deleteBtn);

                _historyList.Children.Add(row);
            }
        }

        private void AddCell(Grid row, string text, int column)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tb, column);
            row.Children.Add(tb);
        }

        private void UpdateBenchmarkIndicator(decimal efficiency)
        {
            if (_benchmarkIndicator == null || _benchmarkBarContainer == null) return;

            var clamped = Math.Clamp(efficiency, 0, 150);
            var fraction = (double)clamped / 150.0;

            _benchmarkIndicator.Visibility = Visibility.Visible;
            _benchmarkIndicator.Margin = new Thickness(_benchmarkBarContainer.ActualWidth * fraction - 1, 0, 0, 0);
        }

        private void UpdateCalcResult()
        {
            if (_calcEfficiencyInput == null || _calcBookHoursInput == null || _calcExpectedResult == null || _calcDeltaText == null) return;

            if (decimal.TryParse(_calcEfficiencyInput.Text, out var eff) && eff > 0 &&
                decimal.TryParse(_calcBookHoursInput.Text, out var bookHours) && bookHours > 0)
            {
                var expected = TechEfficiencyService.CalculateExpectedHours(bookHours, eff);
                _calcExpectedResult.Text = $"{expected:F1} hrs";
                _calcExpectedResult.Foreground = new SolidColorBrush(GetCalcResultColor(eff));

                var delta = expected - bookHours;
                if (delta < 0)
                {
                    _calcDeltaText.Text = $"{Math.Abs(delta):F1} hrs faster than book";
                    _calcDeltaText.Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 220, 120));
                }
                else if (delta > 0)
                {
                    _calcDeltaText.Text = $"{delta:F1} hrs over book";
                    _calcDeltaText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 120, 120));
                }
                else
                {
                    _calcDeltaText.Text = "matches book time";
                    _calcDeltaText.Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
                }
            }
            else
            {
                _calcExpectedResult.Text = "\u2014";
                _calcExpectedResult.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                _calcDeltaText.Text = "";
            }
        }

        // ============================================================
        //  EVENT HANDLERS
        // ============================================================

        private void OnCalcTechChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_calcTechSelector?.SelectedItem is not ComboBoxItem item) return;
            var tagStr = item.Tag as string;

            if (tagStr == "manual" || string.IsNullOrEmpty(tagStr))
            {
                // Manual mode — leave efficiency input editable
                if (_calcEfficiencyInput != null) _calcEfficiencyInput.IsReadOnly = false;
                return;
            }

            // Tech selected — populate efficiency and lock the field
            var tech = _service.GetTechnicians().FirstOrDefault(t => t.Id == tagStr);
            if (tech != null && _calcEfficiencyInput != null)
            {
                _calcEfficiencyInput.Text = tech.EfficiencyPercent.ToString("F0");
                _calcEfficiencyInput.IsReadOnly = true;
            }

            UpdateCalcResult();
        }

        private void OnCalcInputChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCalcResult();
        }

        private void OnTierSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_techTierSelector?.SelectedItem is ComboBoxItem item && item.Tag is string tierTag && _techEfficiencyInput != null)
            {
                var tier = tierTag switch
                {
                    "A" => TechTier.A,
                    "C" => TechTier.C,
                    _ => TechTier.B
                };
                _techEfficiencyInput.Text = TechEfficiencyService.GetDefaultEfficiency(tier).ToString("F0");
            }
        }

        private void OnAddTechnician(object sender, RoutedEventArgs e)
        {
            var name = _techNameInput?.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var tier = TechTier.B;
            if (_techTierSelector?.SelectedItem is ComboBoxItem item && item.Tag is string tierTag)
            {
                tier = tierTag switch
                {
                    "A" => TechTier.A,
                    "C" => TechTier.C,
                    _ => TechTier.B
                };
            }

            decimal? customEff = null;
            if (decimal.TryParse(_techEfficiencyInput?.Text, out var parsedEff) && parsedEff > 0)
                customEff = parsedEff;

            _service.AddTechnician(name, tier, customEff);

            if (_techNameInput != null) _techNameInput.Text = "";
            if (_techTierSelector != null) _techTierSelector.SelectedIndex = 1;
            if (_techEfficiencyInput != null) _techEfficiencyInput.Text = "100";

            RefreshTechnicianLists();
        }

        private async void OnRemoveTechnician(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string techId)
            {
                var tech = _service.GetTechnicians().FirstOrDefault(t => t.Id == techId);
                if (tech == null) return;

                var dialog = new ContentDialog
                {
                    Title = "Remove Technician",
                    Content = $"Remove \"{tech.Name}\" and all their entries?",
                    PrimaryButtonText = "Remove",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    _service.RemoveTechnician(techId);
                    RefreshTechnicianLists();
                    RefreshHistory();
                }
            }
        }

        private void OnHoursChanged(object sender, TextChangedEventArgs e)
        {
            if (_billedHoursInput == null || _actualHoursInput == null || _efficiencyResult == null) return;

            if (decimal.TryParse(_billedHoursInput.Text, out var billed) &&
                decimal.TryParse(_actualHoursInput.Text, out var actual) &&
                actual > 0)
            {
                var eff = TechEfficiencyService.CalculateEfficiency(billed, actual);
                _efficiencyResult.Text = $"{eff:F1}%";
                var (bg, fg) = GetBenchmarkColors(eff);
                _efficiencyResult.Foreground = fg;

                if (_efficiencyBadge != null && _efficiencyLabel != null)
                {
                    _efficiencyBadge.Background = bg;
                    _efficiencyLabel.Text = TechEfficiencyService.GetBenchmarkLabel(eff);
                    _efficiencyLabel.Foreground = fg;
                    _efficiencyBadge.Visibility = Visibility.Visible;
                }

                UpdateBenchmarkIndicator(eff);
            }
            else
            {
                _efficiencyResult.Text = "\u2014";
                _efficiencyResult.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                if (_efficiencyBadge != null) _efficiencyBadge.Visibility = Visibility.Collapsed;
                if (_benchmarkIndicator != null) _benchmarkIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private void OnSaveEntry(object sender, RoutedEventArgs e)
        {
            var selectedItem = _techSelector?.SelectedItem as ComboBoxItem;
            var techId = selectedItem?.Tag as string;
            if (string.IsNullOrEmpty(techId))
            {
                ShowTip("Select a technician first.");
                return;
            }

            if (!decimal.TryParse(_billedHoursInput?.Text, out var billed) || billed <= 0)
            {
                ShowTip("Enter valid billed hours.");
                return;
            }

            if (!decimal.TryParse(_actualHoursInput?.Text, out var actual) || actual <= 0)
            {
                ShowTip("Enter valid actual hours.");
                return;
            }

            _service.AddEntry(techId, billed, actual, roNumber: _roNumberInput?.Text);

            if (_roNumberInput != null) _roNumberInput.Text = "";
            if (_billedHoursInput != null) _billedHoursInput.Text = "";
            if (_actualHoursInput != null) _actualHoursInput.Text = "";

            RefreshHistory();
        }

        private async void ShowTip(string message)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch { /* dialog already open */ }
        }

        private void OnCalculateSummary(object sender, RoutedEventArgs e)
        {
            var selectedItem = _summaryTechSelector?.SelectedItem as ComboBoxItem;
            var techId = selectedItem?.Tag as string;
            if (string.IsNullOrEmpty(techId))
            {
                ShowTip("Select a technician first.");
                return;
            }

            var from = _summaryFromDate?.Date.DateTime ?? DateTime.Today.AddDays(-7);
            var to = _summaryToDate?.Date.DateTime ?? DateTime.Today;

            var summary = _service.GetSummary(techId, from, to);

            if (_summaryResultsGrid != null)
                _summaryResultsGrid.Visibility = Visibility.Visible;

            if (_summaryEntryCountText != null)
                _summaryEntryCountText.Text = summary.EntryCount.ToString();
            if (_summaryBilledText != null)
                _summaryBilledText.Text = summary.TotalBilledHours.ToString("F1");
            if (_summaryActualText != null)
                _summaryActualText.Text = summary.TotalActualHours.ToString("F1");

            if (_summaryEfficiencyText != null)
            {
                _summaryEfficiencyText.Text = summary.EntryCount > 0 ? $"{summary.EfficiencyPercent:F1}%" : "\u2014";
                var (_, effFg) = GetBenchmarkColors(summary.EfficiencyPercent);
                _summaryEfficiencyText.Foreground = effFg;
            }

            if (_summaryProductivityText != null)
            {
                _summaryProductivityText.Text = summary.EntryCount > 0 ? $"{summary.ProductivityPercent:F1}%" : "\u2014";
            }

            if (_summaryBenchmarkBadge != null && _summaryBenchmarkLabel != null && summary.EntryCount > 0)
            {
                var (bg, fg) = GetBenchmarkColors(summary.EfficiencyPercent);
                _summaryBenchmarkBadge.Background = bg;
                _summaryBenchmarkLabel.Text = TechEfficiencyService.GetBenchmarkLabel(summary.EfficiencyPercent);
                _summaryBenchmarkLabel.Foreground = fg;
                _summaryBenchmarkBadge.Visibility = Visibility.Visible;
            }
            else if (_summaryBenchmarkBadge != null)
            {
                _summaryBenchmarkBadge.Visibility = Visibility.Collapsed;
            }
        }

        private async void OnDeleteEntry(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string entryId)
            {
                var dialog = new ContentDialog
                {
                    Title = "Delete Entry",
                    Content = "Delete this entry?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    _service.RemoveEntry(entryId);
                    RefreshHistory();
                }
            }
        }
    }
}
