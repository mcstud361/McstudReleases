#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Visual scoring panel for estimate completeness.
    /// Shows score, grade, category breakdown, and one-click fix suggestions.
    /// </summary>
    public sealed class EstimateScoringPanel : UserControl
    {
        // Score display
        private TextBlock? _scoreText;
        private TextBlock? _gradeText;
        private Border? _gradeBadge;
        private ProgressBar? _scoreBar;
        private TextBlock? _summaryText;

        // Stats row
        private TextBlock? _criticalCountText;
        private TextBlock? _highCountText;
        private TextBlock? _mediumCountText;
        private TextBlock? _recoveryText;

        // Category breakdown
        private Grid? _categoryGrid;

        // Issues list
        private StackPanel? _issuesContainer;
        private Border? _issuesSection;
        private TextBlock? _issuesTitleText;

        // Action buttons
        private Button? _addAllCriticalButton;
        private Button? _addSelectedButton;
        private Button? _copyToClipboardButton;

        // Current result
        private EstimateScoringResult? _currentResult;

        // Event for when items are selected to add
        public event EventHandler<List<ScoringIssue>>? OnAddItems;

        public EstimateScoringPanel()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 22, 27, 34)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 48, 54, 61)),
                BorderThickness = new Thickness(1)
            };

            var mainStack = new StackPanel { Spacing = 16 };

            // === HEADER WITH SCORE ===
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Score
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Grade badge
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Progress bar
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Actions

            // Score display
            var scoreStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            _scoreText = new TextBlock
            {
                Text = "100",
                FontSize = 36,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            scoreStack.Children.Add(_scoreText);
            scoreStack.Children.Add(new TextBlock
            {
                Text = "%",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 6)
            });
            Grid.SetColumn(scoreStack, 0);
            headerGrid.Children.Add(scoreStack);

            // Grade badge
            _gradeBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 46, 160, 67)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _gradeText = new TextBlock
            {
                Text = "A+",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            _gradeBadge.Child = _gradeText;
            Grid.SetColumn(_gradeBadge, 1);
            headerGrid.Children.Add(_gradeBadge);

            // Progress bar section
            var progressStack = new StackPanel
            {
                Margin = new Thickness(20, 0, 20, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _scoreBar = new ProgressBar
            {
                Value = 100,
                Maximum = 100,
                Height = 12,
                CornerRadius = new CornerRadius(6)
            };
            _summaryText = new TextBlock
            {
                Text = "Estimate completeness score",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                Margin = new Thickness(0, 4, 0, 0)
            };
            progressStack.Children.Add(_scoreBar);
            progressStack.Children.Add(_summaryText);
            Grid.SetColumn(progressStack, 2);
            headerGrid.Children.Add(progressStack);

            // Action buttons
            var actionStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            _addAllCriticalButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE7BA", FontSize = 12 },
                        new TextBlock { Text = "Fix All Critical", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 200, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4)
            };
            _addAllCriticalButton.Click += AddAllCritical_Click;
            ToolTipService.SetToolTip(_addAllCriticalButton, "Add all critical and high priority missing items");

            _copyToClipboardButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8C8", FontSize = 12 },
                        new TextBlock { Text = "Copy Report", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4)
            };
            _copyToClipboardButton.Click += CopyToClipboard_Click;
            ToolTipService.SetToolTip(_copyToClipboardButton, "Copy scoring report to clipboard");

            actionStack.Children.Add(_addAllCriticalButton);
            actionStack.Children.Add(_copyToClipboardButton);
            Grid.SetColumn(actionStack, 3);
            headerGrid.Children.Add(actionStack);

            mainStack.Children.Add(headerGrid);

            // === STATS ROW ===
            var statsGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 33, 40)),
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(6)
            };
            for (int i = 0; i < 5; i++)
            {
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            _criticalCountText = CreateStatItem("CRITICAL", "0", Color.FromArgb(255, 248, 81, 73), 0, statsGrid);
            _highCountText = CreateStatItem("HIGH", "0", Color.FromArgb(255, 255, 166, 87), 1, statsGrid);
            _mediumCountText = CreateStatItem("MEDIUM", "0", Color.FromArgb(255, 210, 168, 63), 2, statsGrid);
            CreateStatItem("LOW", "0", Color.FromArgb(255, 140, 140, 140), 3, statsGrid);
            _recoveryText = CreateStatItem("POTENTIAL", "$0", Color.FromArgb(255, 87, 171, 90), 4, statsGrid);

            mainStack.Children.Add(statsGrid);

            // === CATEGORY BREAKDOWN ===
            var categorySection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 33, 40)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };
            var categoryStack = new StackPanel { Spacing = 8 };

            categoryStack.Children.Add(new TextBlock
            {
                Text = "CATEGORY BREAKDOWN",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            _categoryGrid = new Grid();
            _categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Label
            _categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Bar
            _categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); // Score

            categoryStack.Children.Add(_categoryGrid);
            categorySection.Child = categoryStack;
            mainStack.Children.Add(categorySection);

            // === ISSUES LIST ===
            _issuesSection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 33, 40)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };
            var issuesStack = new StackPanel { Spacing = 8 };

            var issuesHeader = new Grid();
            issuesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            issuesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _issuesTitleText = new TextBlock
            {
                Text = "MISSING ITEMS (0)",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            };
            Grid.SetColumn(_issuesTitleText, 0);
            issuesHeader.Children.Add(_issuesTitleText);

            _addSelectedButton = new Button
            {
                Content = "Add Selected",
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(255, 46, 160, 67)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 4, 12, 4),
                CornerRadius = new CornerRadius(4)
            };
            _addSelectedButton.Click += AddSelected_Click;
            Grid.SetColumn(_addSelectedButton, 1);
            issuesHeader.Children.Add(_addSelectedButton);

            issuesStack.Children.Add(issuesHeader);

            var issuesScroll = new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _issuesContainer = new StackPanel { Spacing = 4 };
            issuesScroll.Content = _issuesContainer;
            issuesStack.Children.Add(issuesScroll);

            _issuesSection.Child = issuesStack;
            mainStack.Children.Add(_issuesSection);

            mainBorder.Child = mainStack;
            Content = mainBorder;
        }

        private TextBlock CreateStatItem(string label, string value, Color color, int column, Grid grid)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 2
            };

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            stack.Children.Add(labelText);
            stack.Children.Add(valueText);

            Grid.SetColumn(stack, column);
            grid.Children.Add(stack);

            return valueText;
        }

        /// <summary>
        /// Update the panel with scoring results.
        /// </summary>
        public void UpdateScore(EstimateScoringResult result)
        {
            _currentResult = result;

            // Update score display
            _scoreText!.Text = result.OverallScore.ToString();
            _scoreBar!.Value = result.OverallScore;

            // Update grade
            _gradeText!.Text = result.Grade;
            _gradeBadge!.Background = new SolidColorBrush(GetGradeColor(result.Grade));

            // Update progress bar color
            _scoreBar.Foreground = new SolidColorBrush(GetScoreColor(result.OverallScore));

            // Update summary
            _summaryText!.Text = result.Summary;

            // Update stats
            _criticalCountText!.Text = result.CriticalCount.ToString();
            _highCountText!.Text = result.HighCount.ToString();
            _mediumCountText!.Text = result.MediumCount.ToString();

            var totalRecovery = result.PotentialCostRecovery + (result.PotentialLaborRecovery * 60); // Assume $60/hr
            _recoveryText!.Text = totalRecovery > 0 ? $"${totalRecovery:N0}" : "$0";

            // Update category breakdown
            UpdateCategoryBreakdown(result.CategoryScores);

            // Update issues list
            UpdateIssuesList(result.Issues);

            // Show/hide fix button based on critical items
            _addAllCriticalButton!.Visibility = (result.CriticalCount + result.HighCount) > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateCategoryBreakdown(Dictionary<string, int> categoryScores)
        {
            _categoryGrid!.Children.Clear();
            _categoryGrid.RowDefinitions.Clear();

            int row = 0;
            foreach (var category in categoryScores.OrderBy(c => c.Value))
            {
                _categoryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Label
                var label = new TextBlock
                {
                    Text = category.Key,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                Grid.SetRow(label, row);
                Grid.SetColumn(label, 0);
                _categoryGrid.Children.Add(label);

                // Progress bar
                var bar = new ProgressBar
                {
                    Value = category.Value,
                    Maximum = 100,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(8, 0, 8, 0),
                    Foreground = new SolidColorBrush(GetScoreColor(category.Value))
                };
                Grid.SetRow(bar, row);
                Grid.SetColumn(bar, 1);
                _categoryGrid.Children.Add(bar);

                // Score
                var scoreText = new TextBlock
                {
                    Text = $"{category.Value}%",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(GetScoreColor(category.Value)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(scoreText, row);
                Grid.SetColumn(scoreText, 2);
                _categoryGrid.Children.Add(scoreText);

                row++;
            }
        }

        private void UpdateIssuesList(List<ScoringIssue> issues)
        {
            _issuesContainer!.Children.Clear();
            _issuesTitleText!.Text = $"MISSING ITEMS ({issues.Count})";

            // Group by category
            var grouped = issues
                .OrderByDescending(i => i.Severity)
                .ThenBy(i => i.Category)
                .GroupBy(i => i.Category);

            foreach (var group in grouped)
            {
                // Category header
                var categoryHeader = new TextBlock
                {
                    Text = GetCategoryDisplayName(group.Key),
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(GetCategoryColor(group.Key)),
                    Margin = new Thickness(0, 8, 0, 4)
                };
                _issuesContainer.Children.Add(categoryHeader);

                // Issues
                foreach (var issue in group)
                {
                    var issueItem = CreateIssueItem(issue);
                    _issuesContainer.Children.Add(issueItem);
                }
            }

            // Hide section if no issues
            _issuesSection!.Visibility = issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Border CreateIssueItem(ScoringIssue issue)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Tag = issue
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); // Checkbox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) }); // Severity badge
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Hours
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Cost

            // Checkbox
            var checkbox = new CheckBox
            {
                IsChecked = issue.Severity == IssueSeverity.Critical || issue.Severity == IssueSeverity.High,
                MinWidth = 0,
                Padding = new Thickness(0)
            };
            checkbox.Checked += (s, e) => issue.IsSelected = true;
            checkbox.Unchecked += (s, e) => issue.IsSelected = false;
            issue.IsSelected = checkbox.IsChecked == true;
            Grid.SetColumn(checkbox, 0);
            grid.Children.Add(checkbox);

            // Severity badge
            var severityBadge = new Border
            {
                Background = new SolidColorBrush(GetSeverityColor(issue.Severity)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            severityBadge.Child = new TextBlock
            {
                Text = issue.Severity.ToString().ToUpper(),
                FontSize = 8,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Grid.SetColumn(severityBadge, 1);
            grid.Children.Add(severityBadge);

            // Content
            var contentStack = new StackPanel { Spacing = 2 };
            var titleText = new TextBlock
            {
                Text = issue.Title,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            };
            contentStack.Children.Add(titleText);

            if (!string.IsNullOrEmpty(issue.WhyNeeded))
            {
                var whyText = new TextBlock
                {
                    Text = issue.WhyNeeded,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                contentStack.Children.Add(whyText);
            }

            if (!string.IsNullOrEmpty(issue.TriggeredBy))
            {
                var triggeredText = new TextBlock
                {
                    Text = $"→ {issue.TriggeredBy}",
                    FontSize = 9,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 150, 200))
                };
                contentStack.Children.Add(triggeredText);
            }

            Grid.SetColumn(contentStack, 2);
            grid.Children.Add(contentStack);

            // Hours
            if (issue.SuggestedFix?.LaborHours > 0)
            {
                var hoursText = new TextBlock
                {
                    Text = $"{issue.SuggestedFix.LaborHours:F1} hr",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(hoursText, 3);
                grid.Children.Add(hoursText);
            }

            // Cost
            if (issue.SuggestedFix?.EstimatedCost > 0)
            {
                var costText = new TextBlock
                {
                    Text = $"${issue.SuggestedFix.EstimatedCost:N0}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(costText, 4);
                grid.Children.Add(costText);
            }

            // Tooltip with full details
            var tooltip = $"{issue.Title}\n\n{issue.Description}";
            if (!string.IsNullOrEmpty(issue.WhyNeeded))
                tooltip += $"\n\nWhy: {issue.WhyNeeded}";
            if (!string.IsNullOrEmpty(issue.SuggestedFix?.DegReference))
                tooltip += $"\n\nDEG: {issue.SuggestedFix.DegReference}";
            ToolTipService.SetToolTip(border, tooltip);

            border.Child = grid;
            return border;
        }

        #region Event Handlers

        private void AddAllCritical_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResult == null) return;

            var criticalItems = _currentResult.Issues
                .Where(i => i.Severity == IssueSeverity.Critical || i.Severity == IssueSeverity.High)
                .ToList();

            if (criticalItems.Count > 0)
            {
                OnAddItems?.Invoke(this, criticalItems);
            }
        }

        private void AddSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResult == null) return;

            var selectedItems = _currentResult.Issues.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count > 0)
            {
                OnAddItems?.Invoke(this, selectedItems);
            }
        }

        private void CopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResult == null) return;

            var report = GenerateReport(_currentResult);

            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(report);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }

        #endregion

        #region Helper Methods

        private string GenerateReport(EstimateScoringResult result)
        {
            var lines = new List<string>
            {
                "═══════════════════════════════════════════",
                "       ESTIMATE COMPLETENESS REPORT",
                "═══════════════════════════════════════════",
                "",
                $"Score: {result.OverallScore}% ({result.Grade})",
                $"Vehicle: {result.VehicleInfo ?? "N/A"}",
                $"Total: ${result.EstimateTotal:N2}",
                $"Assessed: {result.AssessedAt:g}",
                "",
                "─────────────────────────────────────────────",
                "SUMMARY",
                "─────────────────────────────────────────────",
                result.Summary,
                "",
                $"Issues Found: {result.Issues.Count}",
                $"  • Critical: {result.CriticalCount}",
                $"  • High: {result.HighCount}",
                $"  • Medium: {result.MediumCount}",
                $"  • Low: {result.LowCount}",
                "",
                $"Potential Labor Recovery: {result.PotentialLaborRecovery:F1} hours",
                $"Potential Cost Recovery: ${result.PotentialCostRecovery:N0}",
                ""
            };

            if (result.Issues.Count > 0)
            {
                lines.Add("─────────────────────────────────────────────");
                lines.Add("MISSING ITEMS");
                lines.Add("─────────────────────────────────────────────");

                foreach (var group in result.Issues.GroupBy(i => i.Category))
                {
                    lines.Add("");
                    lines.Add($"[{GetCategoryDisplayName(group.Key)}]");

                    foreach (var issue in group.OrderByDescending(i => i.Severity))
                    {
                        var hours = issue.SuggestedFix?.LaborHours > 0
                            ? $" | {issue.SuggestedFix.LaborHours:F1} hr"
                            : "";
                        var cost = issue.SuggestedFix?.EstimatedCost > 0
                            ? $" | ${issue.SuggestedFix.EstimatedCost:N0}"
                            : "";

                        lines.Add($"  [{issue.Severity}] {issue.Title}{hours}{cost}");
                        if (!string.IsNullOrEmpty(issue.WhyNeeded))
                            lines.Add($"     Why: {issue.WhyNeeded}");
                        if (!string.IsNullOrEmpty(issue.TriggeredBy))
                            lines.Add($"     Triggered by: {issue.TriggeredBy}");
                    }
                }
            }

            lines.Add("");
            lines.Add("═══════════════════════════════════════════");
            lines.Add("Generated by McStud Tool");
            lines.Add("═══════════════════════════════════════════");

            return string.Join("\n", lines);
        }

        private Color GetGradeColor(string grade)
        {
            return grade switch
            {
                "A+" or "A" => Color.FromArgb(255, 46, 160, 67),
                "B+" or "B" => Color.FromArgb(255, 87, 171, 90),
                "C+" or "C" => Color.FromArgb(255, 210, 168, 63),
                "D" => Color.FromArgb(255, 255, 166, 87),
                _ => Color.FromArgb(255, 248, 81, 73)
            };
        }

        private Color GetScoreColor(int score)
        {
            return score switch
            {
                >= 90 => Color.FromArgb(255, 46, 160, 67),
                >= 75 => Color.FromArgb(255, 87, 171, 90),
                >= 60 => Color.FromArgb(255, 210, 168, 63),
                >= 40 => Color.FromArgb(255, 255, 166, 87),
                _ => Color.FromArgb(255, 248, 81, 73)
            };
        }

        private Color GetSeverityColor(IssueSeverity severity)
        {
            return severity switch
            {
                IssueSeverity.Critical => Color.FromArgb(255, 200, 50, 50),
                IssueSeverity.High => Color.FromArgb(255, 200, 100, 50),
                IssueSeverity.Medium => Color.FromArgb(255, 180, 150, 50),
                IssueSeverity.Low => Color.FromArgb(255, 80, 100, 80),
                _ => Color.FromArgb(255, 100, 100, 100)
            };
        }

        private Color GetCategoryColor(IssueCategoryType category)
        {
            return category switch
            {
                IssueCategoryType.Calibration => Color.FromArgb(255, 248, 81, 73),
                IssueCategoryType.Diagnostic => Color.FromArgb(255, 200, 150, 255),
                IssueCategoryType.Materials => Color.FromArgb(255, 150, 200, 255),
                IssueCategoryType.Blend => Color.FromArgb(255, 255, 200, 100),
                IssueCategoryType.RandI => Color.FromArgb(255, 100, 200, 150),
                IssueCategoryType.Refinish => Color.FromArgb(255, 100, 180, 255),
                IssueCategoryType.Electrical => Color.FromArgb(255, 255, 200, 50),
                IssueCategoryType.Mechanical => Color.FromArgb(255, 200, 200, 200),
                _ => Color.FromArgb(255, 150, 150, 150)
            };
        }

        private string GetCategoryDisplayName(IssueCategoryType category)
        {
            return category switch
            {
                IssueCategoryType.RandI => "R&I Operations",
                IssueCategoryType.Calibration => "Calibrations",
                IssueCategoryType.Diagnostic => "Diagnostics",
                IssueCategoryType.Materials => "Materials",
                IssueCategoryType.Blend => "Blend Operations",
                IssueCategoryType.Refinish => "Refinish",
                IssueCategoryType.Electrical => "Electrical",
                IssueCategoryType.Mechanical => "Mechanical",
                IssueCategoryType.Labor => "Labor",
                _ => "Other"
            };
        }

        #endregion

        /// <summary>
        /// Reset the panel to empty state.
        /// </summary>
        public void Reset()
        {
            _currentResult = null;
            _scoreText!.Text = "--";
            _gradeText!.Text = "--";
            _scoreBar!.Value = 0;
            _summaryText!.Text = "Upload an estimate to see completeness score";
            _criticalCountText!.Text = "0";
            _highCountText!.Text = "0";
            _mediumCountText!.Text = "0";
            _recoveryText!.Text = "$0";
            _categoryGrid!.Children.Clear();
            _issuesContainer!.Children.Clear();
            _issuesSection!.Visibility = Visibility.Collapsed;
            _addAllCriticalButton!.Visibility = Visibility.Collapsed;
        }
    }
}
