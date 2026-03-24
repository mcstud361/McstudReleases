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
using System.Threading.Tasks;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Visual scoring panel for estimate completeness.
    /// Shows dollar hero, action-based issue groups, collapsible detail sections.
    /// </summary>
    public sealed class EstimateScoringPanel : UserControl
    {
        #region Color Constants

        private static readonly Color BgDark = Color.FromArgb(255, 22, 27, 34);
        private static readonly Color BgMedium = Color.FromArgb(255, 28, 33, 40);
        private static readonly Color BgCard = Color.FromArgb(255, 35, 40, 48);
        private static readonly Color BorderDefault = Color.FromArgb(255, 48, 54, 61);
        private static readonly Color TextWhite = Color.FromArgb(255, 255, 255, 255);
        private static readonly Color TextMuted = Color.FromArgb(255, 140, 140, 140);
        private static readonly Color TextDim = Color.FromArgb(255, 120, 120, 120);
        private static readonly Color TextSubtle = Color.FromArgb(255, 130, 140, 155);
        private static readonly Color TextLight = Color.FromArgb(255, 180, 180, 180);
        private static readonly Color TextBright = Color.FromArgb(255, 220, 220, 220);

        private static readonly Color GreenBright = Color.FromArgb(255, 46, 160, 67);
        private static readonly Color GreenMedium = Color.FromArgb(255, 87, 171, 90);
        private static readonly Color GreenMoney = Color.FromArgb(255, 100, 200, 100);
        private static readonly Color GoldBright = Color.FromArgb(255, 255, 200, 100);
        private static readonly Color GoldMedium = Color.FromArgb(255, 210, 168, 63);
        private static readonly Color OrangeWarn = Color.FromArgb(255, 255, 166, 87);
        private static readonly Color RedCritical = Color.FromArgb(255, 248, 81, 73);
        private static readonly Color RedButton = Color.FromArgb(255, 200, 60, 60);
        private static readonly Color BlueInfo = Color.FromArgb(255, 100, 150, 200);
        private static readonly Color BlueAccent = Color.FromArgb(255, 100, 180, 255);
        private static readonly Color PurpleDiag = Color.FromArgb(255, 200, 150, 255);
        private static readonly Color PPageGold = Color.FromArgb(255, 220, 170, 80);
        private static readonly Color ButtonDark = Color.FromArgb(255, 60, 60, 60);
        private static readonly Color MustHavesBg = Color.FromArgb(255, 120, 90, 40);

        // Severity dot colors
        private static readonly Color SevCritical = Color.FromArgb(255, 248, 81, 73);
        private static readonly Color SevHigh = Color.FromArgb(255, 255, 166, 87);
        private static readonly Color SevMedium = Color.FromArgb(255, 210, 168, 63);
        private static readonly Color SevLow = Color.FromArgb(255, 140, 140, 140);

        // Category colors
        private static readonly Color CatCalibration = Color.FromArgb(255, 248, 81, 73);
        private static readonly Color CatDiagnostic = Color.FromArgb(255, 200, 150, 255);
        private static readonly Color CatMaterials = Color.FromArgb(255, 150, 200, 255);
        private static readonly Color CatBlend = Color.FromArgb(255, 255, 200, 100);
        private static readonly Color CatRandI = Color.FromArgb(255, 100, 200, 150);
        private static readonly Color CatRefinish = Color.FromArgb(255, 100, 180, 255);
        private static readonly Color CatElectrical = Color.FromArgb(255, 255, 200, 50);
        private static readonly Color CatMechanical = Color.FromArgb(255, 200, 200, 200);
        private static readonly Color CatOther = Color.FromArgb(255, 150, 150, 150);

        // Action group colors
        private static readonly Color GroupAdd = Color.FromArgb(255, 70, 160, 110);
        private static readonly Color GroupOEM = Color.FromArgb(255, 100, 150, 220);
        private static readonly Color GroupVerify = Color.FromArgb(255, 160, 160, 160);

        #endregion

        // === Hero section ===
        private TextBlock? _dollarHeroText;
        private TextBlock? _laborHeroText;
        private TextBlock? _gradeText;
        private Border? _gradeBadge;
        private TextBlock? _scorePercentText;
        private ProgressBar? _scoreBar;

        // === Action-based issue groups ===
        private StackPanel? _issueGroupsContainer;
        private Border? _issuesSection;

        // === Collapsible detail sections ===
        private Border? _categorySection;
        private StackPanel? _categoryContent;
        private Grid? _categoryGrid;
        private TextBlock? _categorySummaryText;

        private Border? _breakdownSection;
        private StackPanel? _breakdownContent;
        private StackPanel? _breakdownContainer;
        private TextBlock? _breakdownSummaryText;

        private Border? _criteriaSection;
        private StackPanel? _criteriaContent;
        private TextBlock? _criteriaSummaryText;

        // === Action buttons ===
        private Button? _addAllCriticalButton;
        private Button? _copyToClipboardButton;

        // Current result
        private EstimateScoringResult? _currentResult;
        public EstimateScoringResult? CurrentResult => _currentResult;

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
                Background = new SolidColorBrush(BgDark),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(BorderDefault),
                BorderThickness = new Thickness(1)
            };

            var mainStack = new StackPanel { Spacing = 12 };

            // === HERO SUMMARY ===
            mainStack.Children.Add(BuildHeroSection());

            // === ACTION-BASED ISSUE GROUPS ===
            _issuesSection = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Visibility = Visibility.Collapsed
            };
            _issueGroupsContainer = new StackPanel { Spacing = 8 };
            _issuesSection.Child = _issueGroupsContainer;
            mainStack.Children.Add(_issuesSection);

            // === COLLAPSIBLE: CATEGORY BREAKDOWN ===
            mainStack.Children.Add(BuildCollapsibleCategorySection());

            // === COLLAPSIBLE: ESTIMATE BREAKDOWN ===
            mainStack.Children.Add(BuildCollapsibleBreakdownSection());

            // === COLLAPSIBLE: GRADING CRITERIA ===
            mainStack.Children.Add(BuildCollapsibleCriteriaSection());

            mainBorder.Child = mainStack;
            Content = mainBorder;
        }

        #region Hero Section

        private Border BuildHeroSection()
        {
            var heroBorder = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var heroGrid = new Grid();
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Grade badge
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Dollar + labor
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Score + bar
            heroGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            heroGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Grade badge (left)
            _gradeBadge = new Border
            {
                Background = new SolidColorBrush(GreenBright),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            var gradeStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            _gradeText = new TextBlock
            {
                Text = "A+",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            gradeStack.Children.Add(_gradeText);
            gradeStack.Children.Add(new TextBlock
            {
                Text = "grade",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            _gradeBadge.Child = gradeStack;
            Grid.SetColumn(_gradeBadge, 0);
            Grid.SetRowSpan(_gradeBadge, 2);
            heroGrid.Children.Add(_gradeBadge);

            // Dollar hero (center top)
            _dollarHeroText = new TextBlock
            {
                Text = "$0 left on the table",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(GreenMoney),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetColumn(_dollarHeroText, 1);
            Grid.SetRow(_dollarHeroText, 0);
            heroGrid.Children.Add(_dollarHeroText);

            // Labor hours (center bottom)
            _laborHeroText = new TextBlock
            {
                Text = "0.0 hrs uncaptured labor",
                FontSize = 13,
                Foreground = new SolidColorBrush(GoldBright),
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetColumn(_laborHeroText, 1);
            Grid.SetRow(_laborHeroText, 1);
            heroGrid.Children.Add(_laborHeroText);

            // Score + progress bar (right)
            var scoreStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 100
            };
            _scorePercentText = new TextBlock
            {
                Text = "100%",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _scoreBar = new ProgressBar
            {
                Value = 100,
                Maximum = 100,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 4, 0, 0)
            };
            scoreStack.Children.Add(_scorePercentText);
            scoreStack.Children.Add(_scoreBar);
            Grid.SetColumn(scoreStack, 2);
            Grid.SetRowSpan(scoreStack, 2);
            heroGrid.Children.Add(scoreStack);

            var heroStack = new StackPanel { Spacing = 10 };
            heroStack.Children.Add(heroGrid);

            // Action buttons row
            var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

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
                Background = new SolidColorBrush(RedButton),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4),
                Visibility = Visibility.Collapsed
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
                Background = new SolidColorBrush(ButtonDark),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4)
            };
            _copyToClipboardButton.Click += CopyToClipboard_Click;
            ToolTipService.SetToolTip(_copyToClipboardButton, "Copy scoring report to clipboard");

            var mustHavesButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE73E", FontSize = 12 },
                        new TextBlock { Text = "Must-Haves", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(MustHavesBg),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4)
            };
            mustHavesButton.Click += MustHavesButton_Click;
            ToolTipService.SetToolTip(mustHavesButton, "Configure operations that must always be on every estimate");

            actionRow.Children.Add(_addAllCriticalButton);
            actionRow.Children.Add(_copyToClipboardButton);
            actionRow.Children.Add(mustHavesButton);
            heroStack.Children.Add(actionRow);

            heroBorder.Child = heroStack;
            return heroBorder;
        }

        #endregion

        #region Collapsible Detail Sections

        private Border BuildCollapsibleCategorySection()
        {
            _categorySection = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };

            var stack = new StackPanel { Spacing = 6 };

            // Collapsible header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chevron = new TextBlock
            {
                Text = "\uE76C", // collapsed chevron
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = new SolidColorBrush(TextMuted),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var headerBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { chevron, new TextBlock
                    {
                        Text = "CATEGORY BREAKDOWN",
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(TextMuted)
                    }}
                },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            _categorySummaryText = new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(TextDim),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_categorySummaryText, 2);

            headerGrid.Children.Add(headerBtn);
            headerGrid.Children.Add(_categorySummaryText);
            stack.Children.Add(headerGrid);

            _categoryContent = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
            _categoryGrid = new Grid();
            _categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            _categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            _categoryContent.Children.Add(_categoryGrid);

            headerBtn.Click += (s, e) =>
            {
                bool show = _categoryContent.Visibility != Visibility.Visible;
                _categoryContent.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = show ? "\uE70D" : "\uE76C";
            };

            stack.Children.Add(_categoryContent);
            _categorySection.Child = stack;
            return _categorySection;
        }

        private Border BuildCollapsibleBreakdownSection()
        {
            _breakdownSection = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Visibility = Visibility.Collapsed
            };

            var stack = new StackPanel { Spacing = 6 };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chevron = new TextBlock
            {
                Text = "\uE76C",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = new SolidColorBrush(TextMuted),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var headerBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { chevron, new TextBlock
                    {
                        Text = "YOUR ESTIMATE",
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(TextMuted)
                    }}
                },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            _breakdownSummaryText = new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(TextDim),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_breakdownSummaryText, 2);

            headerGrid.Children.Add(headerBtn);
            headerGrid.Children.Add(_breakdownSummaryText);
            stack.Children.Add(headerGrid);

            _breakdownContent = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
            var breakdownScroll = new ScrollViewer
            {
                MaxHeight = 350,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _breakdownContainer = new StackPanel { Spacing = 6 };
            breakdownScroll.Content = _breakdownContainer;
            _breakdownContent.Children.Add(breakdownScroll);

            headerBtn.Click += (s, e) =>
            {
                bool show = _breakdownContent.Visibility != Visibility.Visible;
                _breakdownContent.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = show ? "\uE70D" : "\uE76C";
            };

            stack.Children.Add(_breakdownContent);
            _breakdownSection.Child = stack;
            return _breakdownSection;
        }

        private Border BuildCollapsibleCriteriaSection()
        {
            _criteriaSection = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };

            var stack = new StackPanel { Spacing = 6 };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chevron = new TextBlock
            {
                Text = "\uE76C",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = new SolidColorBrush(TextMuted),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var headerBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { chevron, new TextBlock
                    {
                        Text = "GRADING CRITERIA",
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(TextMuted)
                    }}
                },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            _criteriaSummaryText = new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(TextDim),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_criteriaSummaryText, 2);

            var adjustWeightsButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE713", FontSize = 10 },
                        new TextBlock { Text = "Adjust Weights", FontSize = 10 }
                    }
                },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            adjustWeightsButton.Click += AdjustWeights_Click;
            Grid.SetColumn(adjustWeightsButton, 3);

            headerGrid.Children.Add(headerBtn);
            headerGrid.Children.Add(_criteriaSummaryText);
            headerGrid.Children.Add(adjustWeightsButton);
            stack.Children.Add(headerGrid);

            _criteriaContent = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
            _criteriaContent.Children.Add(new TextBlock
            {
                Text = "Upload an estimate to see pass/fail criteria",
                FontSize = 10,
                Foreground = new SolidColorBrush(TextDim),
                Margin = new Thickness(4)
            });

            headerBtn.Click += (s, e) =>
            {
                bool show = _criteriaContent.Visibility != Visibility.Visible;
                _criteriaContent.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = show ? "\uE70D" : "\uE76C";
            };

            stack.Children.Add(_criteriaContent);
            _criteriaSection.Child = stack;
            return _criteriaSection;
        }

        #endregion

        #region Update Methods

        /// <summary>
        /// Update the panel with scoring results.
        /// </summary>
        public void UpdateScore(EstimateScoringResult result)
        {
            _currentResult = result;

            // Hero: dollar amount
            var totalRecovery = result.PotentialCostRecovery + (result.PotentialLaborRecovery * 60);
            _dollarHeroText!.Text = totalRecovery > 0 ? $"${totalRecovery:N0} left on the table" : "Estimate looks complete";
            _dollarHeroText.Foreground = new SolidColorBrush(totalRecovery > 0 ? GreenMoney : GreenBright);

            // Hero: labor hours
            _laborHeroText!.Text = result.PotentialLaborRecovery > 0
                ? $"{result.PotentialLaborRecovery:F1} hrs uncaptured labor"
                : "";

            // Grade badge
            _gradeText!.Text = result.Grade;
            _gradeBadge!.Background = new SolidColorBrush(GetGradeColor(result.Grade));

            // Score + progress bar
            _scorePercentText!.Text = $"{result.OverallScore}%";
            _scoreBar!.Value = result.OverallScore;
            _scoreBar.Foreground = new SolidColorBrush(GetScoreColor(result.OverallScore));

            // Fix All Critical button — show count
            int critHighCount = result.CriticalCount + result.HighCount;
            if (critHighCount > 0)
            {
                _addAllCriticalButton!.Visibility = Visibility.Visible;
                // Update button text with count
                _addAllCriticalButton.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE7BA", FontSize = 12 },
                        new TextBlock { Text = $"Fix All Critical ({critHighCount})", FontSize = 11 }
                    }
                };
            }
            else
            {
                _addAllCriticalButton!.Visibility = Visibility.Collapsed;
            }

            // Update action-based issue groups
            UpdateIssueGroups(result.Issues);

            // Update category breakdown
            UpdateCategoryBreakdown(result.CategoryScores);

            // Update estimate breakdown
            UpdateEstimateBreakdown(result.CategorizedLines);

            // Update criteria
            UpdateCriteriaContent(result);
        }

        private void UpdateIssueGroups(List<ScoringIssue> issues)
        {
            _issueGroupsContainer!.Children.Clear();

            if (issues.Count == 0)
            {
                _issuesSection!.Visibility = Visibility.Collapsed;
                return;
            }

            _issuesSection!.Visibility = Visibility.Visible;

            // Group by action type
            var groups = new[]
            {
                (Type: IssueActionType.AddToEstimate, Label: "Add to Estimate", GroupColor: GroupAdd, DefaultExpanded: true),
                (Type: IssueActionType.CheckWithOEM, Label: "Check with OEM", GroupColor: GroupOEM, DefaultExpanded: false),
                (Type: IssueActionType.VerifyOptional, Label: "Verify / Optional", GroupColor: GroupVerify, DefaultExpanded: false)
            };

            foreach (var (actionType, label, groupColor, defaultExpanded) in groups)
            {
                var groupIssues = issues
                    .Where(i => i.ActionType == actionType)
                    .OrderByDescending(i => i.Severity)
                    .ToList();

                if (groupIssues.Count == 0) continue;

                var groupDollars = groupIssues.Sum(i => i.SuggestedFix?.EstimatedCost ?? 0);
                var groupHours = groupIssues.Sum(i => i.SuggestedFix?.LaborHours ?? 0);

                // Group wrapper
                var groupStack = new StackPanel { Spacing = 4 };

                // Group header
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Chevron
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Label + count
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Dollar/hours
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Add Selected

                var chevron = new TextBlock
                {
                    Text = defaultExpanded ? "\uE70D" : "\uE76C",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(groupColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };

                var labelBlock = new TextBlock
                {
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(groupColor),
                    VerticalAlignment = VerticalAlignment.Center
                };
                labelBlock.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $"{label} " });
                labelBlock.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = $"({groupIssues.Count} items)",
                    FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = new SolidColorBrush(TextMuted)
                });

                var statsText = new TextBlock
                {
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextLight),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                var statParts = new List<string>();
                if (groupDollars > 0) statParts.Add($"${groupDollars:N0}");
                if (groupHours > 0) statParts.Add($"{groupHours:F1} hrs");
                statsText.Text = string.Join("  |  ", statParts);

                var addSelectedBtn = new Button
                {
                    Content = "Add Selected",
                    FontSize = 10,
                    Background = new SolidColorBrush(GreenBright),
                    Foreground = new SolidColorBrush(Colors.White),
                    Padding = new Thickness(8, 3, 8, 3),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                addSelectedBtn.Click += (s, e) =>
                {
                    var selected = groupIssues.Where(i => i.IsSelected).ToList();
                    if (selected.Count > 0) OnAddItems?.Invoke(this, selected);
                };

                Grid.SetColumn(chevron, 0);
                Grid.SetColumn(labelBlock, 1);
                Grid.SetColumn(statsText, 2);
                Grid.SetColumn(addSelectedBtn, 3);
                headerGrid.Children.Add(chevron);
                headerGrid.Children.Add(labelBlock);
                headerGrid.Children.Add(statsText);
                headerGrid.Children.Add(addSelectedBtn);

                var headerBorder = new Border
                {
                    Background = new SolidColorBrush(BgCard),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 6, 8, 6),
                    Child = headerGrid
                };

                // Issue items panel
                var itemsPanel = new StackPanel { Spacing = 3, Visibility = defaultExpanded ? Visibility.Visible : Visibility.Collapsed };

                headerBorder.Tapped += (s, e) =>
                {
                    bool show = itemsPanel.Visibility != Visibility.Visible;
                    itemsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    chevron.Text = show ? "\uE70D" : "\uE76C";
                };

                groupStack.Children.Add(headerBorder);

                foreach (var issue in groupIssues)
                {
                    itemsPanel.Children.Add(CreateIssueRow(issue));
                }

                groupStack.Children.Add(itemsPanel);
                _issueGroupsContainer.Children.Add(groupStack);
            }
        }

        /// <summary>
        /// Simplified 4-column issue row: checkbox, severity dot, title+reason, cost+hours
        /// </summary>
        private Border CreateIssueRow(ScoringIssue issue)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 42)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 5, 8, 5),
                Tag = issue
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });  // Checkbox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });  // Severity dot
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Title
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Cost + hours

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

            // Severity dot (8px colored circle)
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(GetSeverityColor(issue.Severity)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ToolTipService.SetToolTip(dot, issue.Severity.ToString());
            Grid.SetColumn(dot, 1);
            grid.Children.Add(dot);

            // Title + reason
            var titleStack = new StackPanel { Spacing = 1 };
            var titleText = new TextBlock
            {
                Text = issue.Title,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            titleStack.Children.Add(titleText);

            if (!string.IsNullOrEmpty(issue.WhyNeeded))
            {
                titleStack.Children.Add(new TextBlock
                {
                    Text = issue.WhyNeeded,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(TextDim),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            Grid.SetColumn(titleStack, 2);
            grid.Children.Add(titleStack);

            // Cost + hours (right-aligned)
            var costStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            if (issue.SuggestedFix?.EstimatedCost > 0)
            {
                costStack.Children.Add(new TextBlock
                {
                    Text = $"${issue.SuggestedFix.EstimatedCost:N0}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(GreenMoney),
                    HorizontalAlignment = HorizontalAlignment.Right
                });
            }
            if (issue.SuggestedFix?.LaborHours > 0)
            {
                costStack.Children.Add(new TextBlock
                {
                    Text = $"{issue.SuggestedFix.LaborHours:F1}h",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(GoldBright),
                    HorizontalAlignment = HorizontalAlignment.Right
                });
            }
            Grid.SetColumn(costStack, 3);
            grid.Children.Add(costStack);

            // Tooltip with full details (source detail moved here)
            var tooltip = $"{issue.Title}\n\n{issue.Description}";
            if (!string.IsNullOrEmpty(issue.WhyNeeded))
                tooltip += $"\n\nWhy: {issue.WhyNeeded}";
            if (!string.IsNullOrEmpty(issue.SuggestedFix?.DegReference))
                tooltip += $"\n\nDEG: {issue.SuggestedFix.DegReference}";
            if (!string.IsNullOrEmpty(issue.SourceDetail))
                tooltip += $"\n\nTriggered by: {issue.SourceDetail}";
            if (!string.IsNullOrEmpty(issue.TriggeredBy))
                tooltip += $"\n\nTriggered by: {issue.TriggeredBy}";
            ToolTipService.SetToolTip(border, tooltip);

            border.Child = grid;
            return border;
        }

        private void UpdateCategoryBreakdown(Dictionary<string, int> categoryScores)
        {
            _categoryGrid!.Children.Clear();
            _categoryGrid.RowDefinitions.Clear();

            if (categoryScores.Count == 0)
            {
                _categorySummaryText!.Text = "";
                return;
            }

            _categorySummaryText!.Text = $"[{categoryScores.Count} categories scored]";

            int row = 0;
            foreach (var category in categoryScores.OrderBy(c => c.Value))
            {
                _categoryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = category.Key,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextLight),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                Grid.SetRow(label, row);
                Grid.SetColumn(label, 0);
                _categoryGrid.Children.Add(label);

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

        private void UpdateEstimateBreakdown(List<CategorizedEstimateLine> categorizedLines)
        {
            if (_breakdownContainer == null || _breakdownSection == null) return;

            _breakdownContainer.Children.Clear();

            if (categorizedLines.Count == 0)
            {
                _breakdownSection.Visibility = Visibility.Collapsed;
                return;
            }

            _breakdownSection.Visibility = Visibility.Visible;
            var total = categorizedLines.Sum(l => l.Price);
            _breakdownSummaryText!.Text = $"[{categorizedLines.Count} ops  |  ${total:N0} total]";

            var grouped = categorizedLines
                .GroupBy(l => l.Category)
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                var catColor = GetBreakdownCategoryColor(group.Key);

                var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                headerStack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(catColor),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Child = new TextBlock
                    {
                        Text = group.Key.ToUpperInvariant(),
                        FontSize = 9,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.White)
                    }
                });
                headerStack.Children.Add(new TextBlock
                {
                    Text = $"{group.Count()} operation{(group.Count() != 1 ? "s" : "")}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextDim),
                    VerticalAlignment = VerticalAlignment.Center
                });
                _breakdownContainer.Children.Add(headerStack);

                foreach (var line in group)
                {
                    var lineGrid = new Grid { Margin = new Thickness(12, 1, 0, 1) };
                    lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

                    lineGrid.Children.Add(SetCol(new TextBlock
                    {
                        Text = line.OperationType,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(CatOther)
                    }, 0));

                    lineGrid.Children.Add(SetCol(new TextBlock
                    {
                        Text = !string.IsNullOrEmpty(line.PartName) ? line.PartName : line.Description,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }, 1));

                    var totalHours = line.LaborHours + line.RefinishHours;
                    if (totalHours > 0)
                    {
                        lineGrid.Children.Add(SetCol(new TextBlock
                        {
                            Text = $"{totalHours:F1}h",
                            FontSize = 10,
                            Foreground = new SolidColorBrush(GoldBright),
                            HorizontalAlignment = HorizontalAlignment.Right
                        }, 2));
                    }

                    if (line.Price > 0)
                    {
                        lineGrid.Children.Add(SetCol(new TextBlock
                        {
                            Text = $"${line.Price:N0}",
                            FontSize = 10,
                            Foreground = new SolidColorBrush(GreenMoney),
                            HorizontalAlignment = HorizontalAlignment.Right
                        }, 3));
                    }

                    _breakdownContainer.Children.Add(lineGrid);
                }
            }
        }

        /// <summary>
        /// Dynamic criteria display: shows pass/fail status per check against the current estimate.
        /// </summary>
        private void UpdateCriteriaContent(EstimateScoringResult result)
        {
            if (_criteriaContent == null) return;
            _criteriaContent.Children.Clear();

            var allCategories = new (IssueCategoryType Type, string Name, Color Color, string[] DefaultChecks)[]
            {
                (IssueCategoryType.Diagnostic, "Diagnostics", CatDiagnostic, new[]
                {
                    "Pre-repair diagnostic scan",
                    "Post-repair diagnostic scan",
                    "In-process scan"
                }),
                (IssueCategoryType.Calibration, "Calibrations", CatCalibration, new[]
                {
                    "ADAS camera calibration",
                    "Sensor calibration",
                    "4-wheel alignment"
                }),
                (IssueCategoryType.Materials, "Materials", CatMaterials, new[]
                {
                    "Corrosion protection",
                    "Weld-thru primer",
                    "Seam sealer",
                    "Flex additive",
                    "Adhesion promoter"
                }),
                (IssueCategoryType.Blend, "Blend", CatBlend, new[] { "Adjacent panel blend" }),
                (IssueCategoryType.RandI, "R&I", CatRandI, new[] { "R&I operations" }),
                (IssueCategoryType.Refinish, "Refinish", CatRefinish, new[] { "3-stage/tri-coat paint" }),
                (IssueCategoryType.Electrical, "Electrical", CatElectrical, new[] { "Battery disconnect/reconnect" }),
                (IssueCategoryType.Other, "Learned Patterns", CatOther, Array.Empty<string>())
            };

            int totalPassed = 0;
            int totalChecks = 0;

            foreach (var (type, name, color, defaultChecks) in allCategories)
            {
                var categoryIssues = result.Issues.Where(i => i.Category == type).ToList();

                if (defaultChecks.Length == 0 && categoryIssues.Count == 0)
                    continue;

                var catBorder = new Border
                {
                    Background = new SolidColorBrush(BgCard),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 2, 0, 0)
                };

                var catStack = new StackPanel { Spacing = 2 };

                int catDeductions = categoryIssues.Sum(i => i.PointDeduction);
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                headerGrid.Children.Add(new TextBlock
                {
                    Text = name.ToUpperInvariant(),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(color)
                });

                var scoreLabel = new TextBlock
                {
                    Text = catDeductions == 0 ? "PASS" : $"-{catDeductions} pts",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(catDeductions == 0 ? GreenBright : RedCritical),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(scoreLabel, 1);
                headerGrid.Children.Add(scoreLabel);
                catStack.Children.Add(headerGrid);

                var failedTitles = new HashSet<string>(
                    categoryIssues.Select(i => i.Title.ToLowerInvariant()));

                foreach (var check in defaultChecks)
                {
                    totalChecks++;
                    bool isFailing = categoryIssues.Any(i =>
                        i.Title.ToLowerInvariant().Contains(check.ToLowerInvariant()) ||
                        check.ToLowerInvariant().Contains(i.Title.ToLowerInvariant().Split(' ')[0]));

                    if (!isFailing) totalPassed++;

                    var matchingIssue = isFailing
                        ? categoryIssues.FirstOrDefault(i =>
                            i.Title.ToLowerInvariant().Contains(check.ToLowerInvariant()) ||
                            check.ToLowerInvariant().Contains(i.Title.ToLowerInvariant().Split(' ')[0]))
                        : null;

                    var checkGrid = new Grid { Margin = new Thickness(4, 1, 0, 1) };
                    checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                    checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    checkGrid.Children.Add(new TextBlock
                    {
                        Text = isFailing ? "\u2717" : "\u2713",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(isFailing ? RedCritical : GreenBright),
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    var desc = new TextBlock
                    {
                        Text = check,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(isFailing ? TextBright : TextDim),
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(desc, 1);
                    checkGrid.Children.Add(desc);

                    if (isFailing && matchingIssue != null)
                    {
                        var pts = new TextBlock
                        {
                            Text = $"-{matchingIssue.PointDeduction} pts",
                            FontSize = 9,
                            Foreground = new SolidColorBrush(RedCritical),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(6, 0, 0, 0)
                        };
                        Grid.SetColumn(pts, 2);
                        checkGrid.Children.Add(pts);
                    }

                    catStack.Children.Add(checkGrid);
                }

                // Additional issues not covered by default checks
                var coveredTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var check in defaultChecks)
                {
                    var matched = categoryIssues.FirstOrDefault(i =>
                        i.Title.ToLowerInvariant().Contains(check.ToLowerInvariant()) ||
                        check.ToLowerInvariant().Contains(i.Title.ToLowerInvariant().Split(' ')[0]));
                    if (matched != null) coveredTitles.Add(matched.Title);
                }

                foreach (var issue in categoryIssues.Where(i => !coveredTitles.Contains(i.Title)))
                {
                    totalChecks++;
                    var checkGrid = new Grid { Margin = new Thickness(4, 1, 0, 1) };
                    checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                    checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    checkGrid.Children.Add(new TextBlock
                    {
                        Text = "\u2717",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(RedCritical),
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    var desc = new TextBlock
                    {
                        Text = issue.Title,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(TextBright),
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(desc, 1);
                    checkGrid.Children.Add(desc);

                    var pts = new TextBlock
                    {
                        Text = $"-{issue.PointDeduction} pts",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(RedCritical),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0)
                    };
                    Grid.SetColumn(pts, 2);
                    checkGrid.Children.Add(pts);

                    catStack.Children.Add(checkGrid);
                }

                catBorder.Child = catStack;
                _criteriaContent.Children.Add(catBorder);
            }

            _criteriaSummaryText!.Text = totalChecks > 0 ? $"[{totalPassed}/{totalChecks} passed]" : "";

            // Show criteria when results are available
            _criteriaContent.Visibility = Visibility.Visible;
        }

        #endregion

        #region Event Handlers

        private async void MustHavesButton_Click(object sender, RoutedEventArgs e)
        {
            await MustHavesDialog.ShowAsync(this.XamlRoot);
        }

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

        private async void AdjustWeights_Click(object sender, RoutedEventArgs e)
        {
            var config = GhostConfigService.Instance.Config.ScoringWeights;

            var criticalBox = new NumberBox
            {
                Header = "Critical",
                Value = config.CriticalPoints,
                Minimum = 0,
                Maximum = 20,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var highBox = new NumberBox
            {
                Header = "High",
                Value = config.HighPoints,
                Minimum = 0,
                Maximum = 20,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var mediumBox = new NumberBox
            {
                Header = "Medium",
                Value = config.MediumPoints,
                Minimum = 0,
                Maximum = 20,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var lowBox = new NumberBox
            {
                Header = "Low",
                Value = config.LowPoints,
                Minimum = 0,
                Maximum = 20,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };

            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = "Points deducted per severity level:",
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            });
            stack.Children.Add(criticalBox);
            stack.Children.Add(highBox);
            stack.Children.Add(mediumBox);
            stack.Children.Add(lowBox);

            var dialog = new ContentDialog
            {
                Title = "Adjust Scoring Weights",
                Content = stack,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                config.CriticalPoints = (int)criticalBox.Value;
                config.HighPoints = (int)highBox.Value;
                config.MediumPoints = (int)mediumBox.Value;
                config.LowPoints = (int)lowBox.Value;
                GhostConfigService.Instance.SaveConfig();

                if (_currentResult != null)
                {
                    foreach (var issue in _currentResult.Issues)
                    {
                        if (issue.Source == "Learned")
                        {
                            issue.PointDeduction = 1;
                            continue;
                        }
                        issue.PointDeduction = issue.Severity switch
                        {
                            IssueSeverity.Critical => config.CriticalPoints,
                            IssueSeverity.High => config.HighPoints,
                            IssueSeverity.Medium => config.MediumPoints,
                            IssueSeverity.Low => config.LowPoints,
                            _ => 2
                        };
                        if (issue.Source == "Smart")
                        {
                            issue.PointDeduction = issue.Severity switch
                            {
                                IssueSeverity.Critical => Math.Max(1, config.CriticalPoints / 2),
                                IssueSeverity.High => Math.Max(1, config.HighPoints / 2),
                                IssueSeverity.Medium => Math.Max(1, config.MediumPoints / 2),
                                _ => 0
                            };
                        }
                    }
                    EstimateScoringService.Instance.RecalculateScore(_currentResult);
                    UpdateScore(_currentResult);
                }
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

        #region Report Generation

        private string GenerateReport(EstimateScoringResult result)
        {
            var lines = new List<string>
            {
                "\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550",
                "       ESTIMATE COMPLETENESS REPORT",
                "\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550",
                "",
                $"Score: {result.OverallScore}% ({result.Grade})",
                $"Vehicle: {result.VehicleInfo ?? "N/A"}",
                $"Total: ${result.EstimateTotal:N2}",
                $"Assessed: {result.AssessedAt:g}",
                "",
                "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500",
                "SUMMARY",
                "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500",
                result.Summary,
                "",
                $"Issues Found: {result.Issues.Count}",
                $"  \u2022 Critical: {result.CriticalCount}",
                $"  \u2022 High: {result.HighCount}",
                $"  \u2022 Medium: {result.MediumCount}",
                $"  \u2022 Low: {result.LowCount}",
                "",
                $"Potential Labor Recovery: {result.PotentialLaborRecovery:F1} hours",
                $"Potential Cost Recovery: ${result.PotentialCostRecovery:N0}",
                ""
            };

            if (result.Issues.Count > 0)
            {
                lines.Add("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
                lines.Add("MISSING ITEMS");
                lines.Add("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");

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

            if (result.CategorizedLines.Count > 0)
            {
                lines.Add("");
                lines.Add("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
                lines.Add("ESTIMATE BREAKDOWN BY CATEGORY");
                lines.Add("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");

                foreach (var catGroup in result.CategorizedLines.GroupBy(l => l.Category).OrderBy(g => g.Key))
                {
                    lines.Add("");
                    lines.Add($"[{catGroup.Key}] ({catGroup.Count()} ops)");
                    foreach (var op in catGroup)
                    {
                        var hours = (op.LaborHours + op.RefinishHours) > 0
                            ? $" | {op.LaborHours + op.RefinishHours:F1} hr"
                            : "";
                        var price = op.Price > 0 ? $" | ${op.Price:N0}" : "";
                        lines.Add($"  {op.OperationType,-8} {op.PartName}{hours}{price}");
                    }
                }
            }

            lines.Add("");
            lines.Add("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
            lines.Add("Generated by McStud Tool");
            lines.Add("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");

            return string.Join("\n", lines);
        }

        #endregion

        #region Helper Methods

        private static FrameworkElement SetCol(FrameworkElement element, int col)
        {
            Grid.SetColumn(element, col);
            return element;
        }

        private Color GetGradeColor(string grade)
        {
            return grade switch
            {
                "A+" or "A" => GreenBright,
                "B+" or "B" => GreenMedium,
                "C+" or "C" => GoldMedium,
                "D" => OrangeWarn,
                _ => RedCritical
            };
        }

        private Color GetScoreColor(int score)
        {
            return score switch
            {
                >= 90 => GreenBright,
                >= 75 => GreenMedium,
                >= 60 => GoldMedium,
                >= 40 => OrangeWarn,
                _ => RedCritical
            };
        }

        private Color GetSeverityColor(IssueSeverity severity)
        {
            return severity switch
            {
                IssueSeverity.Critical => SevCritical,
                IssueSeverity.High => SevHigh,
                IssueSeverity.Medium => SevMedium,
                IssueSeverity.Low => SevLow,
                _ => SevLow
            };
        }

        private Color GetCategoryColor(IssueCategoryType category)
        {
            return category switch
            {
                IssueCategoryType.Calibration => CatCalibration,
                IssueCategoryType.Diagnostic => CatDiagnostic,
                IssueCategoryType.Materials => CatMaterials,
                IssueCategoryType.Blend => CatBlend,
                IssueCategoryType.RandI => CatRandI,
                IssueCategoryType.Refinish => CatRefinish,
                IssueCategoryType.Electrical => CatElectrical,
                IssueCategoryType.Mechanical => CatMechanical,
                _ => CatOther
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

        private Color GetBreakdownCategoryColor(string category)
        {
            return category switch
            {
                "Diagnostics" => Color.FromArgb(255, 140, 100, 200),
                "Calibrations" => CatCalibration,
                "Materials" => Color.FromArgb(255, 100, 150, 200),
                "Blend" => Color.FromArgb(255, 200, 160, 60),
                "R&I" => Color.FromArgb(255, 70, 160, 110),
                "Refinish" => Color.FromArgb(255, 70, 140, 210),
                "Electrical" => Color.FromArgb(255, 200, 160, 40),
                "Mechanical" => Color.FromArgb(255, 160, 160, 160),
                "Body/Structural" => Color.FromArgb(255, 100, 160, 220),
                _ => TextDim
            };
        }

        #endregion

        /// <summary>
        /// Reset the panel to empty state.
        /// </summary>
        public void Reset()
        {
            _currentResult = null;
            _dollarHeroText!.Text = "$0 left on the table";
            _laborHeroText!.Text = "";
            _gradeText!.Text = "--";
            _scorePercentText!.Text = "--%";
            _scoreBar!.Value = 0;
            _categoryGrid!.Children.Clear();
            _categorySummaryText!.Text = "";
            _issueGroupsContainer!.Children.Clear();
            _issuesSection!.Visibility = Visibility.Collapsed;
            _breakdownContainer!.Children.Clear();
            _breakdownSection!.Visibility = Visibility.Collapsed;
            _breakdownSummaryText!.Text = "";
            _addAllCriticalButton!.Visibility = Visibility.Collapsed;
        }
    }
}
