#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Linq;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Dashboard showing learning system health, statistics, and recommendations.
    /// Displays health score, acceptance rates, pattern counts, and actionable insights.
    /// </summary>
    public sealed class LearningHealthDashboard : UserControl
    {
        private readonly LearningHealthService _healthService;
        private readonly LearningFeedbackService _feedbackService;
        private readonly EstimateLearningService _learningService;

        // UI Elements
        private TextBlock? _healthScoreText;
        private TextBlock? _healthGradeText;
        private Border? _healthScoreBorder;
        private ProgressRing? _healthRing;
        private TextBlock? _bootstrapText;
        private ProgressBar? _bootstrapProgress;

        // Stats cards
        private TextBlock? _patternsCountText;
        private TextBlock? _estimatesCountText;
        private TextBlock? _acceptanceRateText;
        private TextBlock? _rejectionRateText;

        // Recommendations
        private StackPanel? _recommendationsPanel;

        // Top patterns lists
        private StackPanel? _topRejectedPanel;
        private StackPanel? _stalePatternspanel;

        // Alerts
        private StackPanel? _alertsPanel;

        public LearningHealthDashboard()
        {
            _healthService = LearningHealthService.Instance;
            _feedbackService = LearningFeedbackService.Instance;
            _learningService = EstimateLearningService.Instance;

            BuildUI();
            RefreshDashboard();
        }

        private void BuildUI()
        {
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var mainStack = new StackPanel
            {
                Spacing = 16,
                Padding = new Thickness(24)
            };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE9D9",  // Health shield
                FontSize = 28,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });
            header.Children.Add(new TextBlock
            {
                Text = "Learning Health Dashboard",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            var refreshButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE72C", FontSize = 14 },
                Margin = new Thickness(16, 0, 0, 0),
                Padding = new Thickness(8)
            };
            refreshButton.Click += (s, e) => RefreshDashboard();
            ToolTipService.SetToolTip(refreshButton, "Refresh dashboard");
            header.Children.Add(refreshButton);

            mainStack.Children.Add(header);

            // === ALERTS SECTION ===
            _alertsPanel = new StackPanel { Spacing = 8 };
            mainStack.Children.Add(_alertsPanel);

            // === IMPORT PROGRESS BANNER ===
            var bootstrapBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 35, 50)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 100, 80, 140)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Visibility = Visibility.Collapsed
            };

            var bootstrapStack = new StackPanel { Spacing = 8 };
            var bootstrapHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            bootstrapHeader.Children.Add(new FontIcon
            {
                Glyph = "\uE946",  // Lightbulb
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 255))
            });
            _bootstrapText = new TextBlock
            {
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 255))
            };
            bootstrapHeader.Children.Add(_bootstrapText);
            bootstrapStack.Children.Add(bootstrapHeader);

            _bootstrapProgress = new ProgressBar
            {
                Maximum = 20,
                Height = 6,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 100, 200))
            };
            bootstrapStack.Children.Add(_bootstrapProgress);

            bootstrapStack.Children.Add(new TextBlock
            {
                Text = "Keep importing estimates to improve accuracy!",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                TextWrapping = TextWrapping.Wrap
            });

            bootstrapBorder.Child = bootstrapStack;
            bootstrapBorder.Tag = "bootstrap";  // For finding later
            mainStack.Children.Add(bootstrapBorder);

            // === HEALTH SCORE SECTION ===
            var healthSection = new Grid();
            healthSection.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            healthSection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Score card
            _healthScoreBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 40)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(24),
                MinWidth = 180
            };

            var scoreStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            _healthScoreText = new TextBlock
            {
                FontSize = 48,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            scoreStack.Children.Add(_healthScoreText);

            _healthGradeText = new TextBlock
            {
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            scoreStack.Children.Add(_healthGradeText);

            scoreStack.Children.Add(new TextBlock
            {
                Text = "Health Score",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            });

            _healthScoreBorder.Child = scoreStack;
            healthSection.Children.Add(_healthScoreBorder);

            // Stats grid
            var statsGrid = new Grid { Margin = new Thickness(24, 0, 0, 0) };
            statsGrid.RowDefinitions.Add(new RowDefinition());
            statsGrid.RowDefinitions.Add(new RowDefinition());
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition());

            // Patterns count
            var patternsCard = CreateStatCard("\uE8FD", "Patterns", out _patternsCountText);
            Grid.SetRow(patternsCard, 0);
            Grid.SetColumn(patternsCard, 0);
            statsGrid.Children.Add(patternsCard);

            // Estimates count
            var estimatesCard = CreateStatCard("\uE8A5", "Estimates", out _estimatesCountText);
            Grid.SetRow(estimatesCard, 0);
            Grid.SetColumn(estimatesCard, 1);
            statsGrid.Children.Add(estimatesCard);

            // Acceptance rate
            var acceptCard = CreateStatCard("\uE8FB", "Acceptance", out _acceptanceRateText);
            Grid.SetRow(acceptCard, 1);
            Grid.SetColumn(acceptCard, 0);
            statsGrid.Children.Add(acceptCard);

            // Rejection rate
            var rejectCard = CreateStatCard("\uE711", "Rejection", out _rejectionRateText);
            Grid.SetRow(rejectCard, 1);
            Grid.SetColumn(rejectCard, 1);
            statsGrid.Children.Add(rejectCard);

            Grid.SetColumn(statsGrid, 1);
            healthSection.Children.Add(statsGrid);

            mainStack.Children.Add(healthSection);

            // === RECOMMENDATIONS SECTION ===
            var recsSection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 30, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var recsStack = new StackPanel { Spacing = 12 };

            var recsHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            recsHeader.Children.Add(new FontIcon
            {
                Glyph = "\uE946",  // Lightbulb
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 193, 7))
            });
            recsHeader.Children.Add(new TextBlock
            {
                Text = "RECOMMENDATIONS",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 193, 7))
            });
            recsStack.Children.Add(recsHeader);

            _recommendationsPanel = new StackPanel { Spacing = 8 };
            recsStack.Children.Add(_recommendationsPanel);

            recsSection.Child = recsStack;
            mainStack.Children.Add(recsSection);

            // === TOP REJECTED PATTERNS ===
            var rejectedSection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 25, 25)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var rejectedStack = new StackPanel { Spacing = 12 };

            var rejectedHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            rejectedHeader.Children.Add(new FontIcon
            {
                Glyph = "\uE783",  // Warning
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 244, 67, 54))
            });
            rejectedHeader.Children.Add(new TextBlock
            {
                Text = "FREQUENTLY REJECTED PATTERNS",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 244, 67, 54))
            });
            rejectedStack.Children.Add(rejectedHeader);

            _topRejectedPanel = new StackPanel { Spacing = 4 };
            rejectedStack.Children.Add(_topRejectedPanel);

            rejectedSection.Child = rejectedStack;
            mainStack.Children.Add(rejectedSection);

            // === STALE PATTERNS ===
            var staleSection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 30, 25)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var staleStack = new StackPanel { Spacing = 12 };

            var staleHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            staleHeader.Children.Add(new FontIcon
            {
                Glyph = "\uE823",  // Clock
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0))
            });
            staleHeader.Children.Add(new TextBlock
            {
                Text = "STALE PATTERNS (180+ days)",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0))
            });
            staleStack.Children.Add(staleHeader);

            _stalePatternspanel = new StackPanel { Spacing = 4 };
            staleStack.Children.Add(_stalePatternspanel);

            staleSection.Child = staleStack;
            mainStack.Children.Add(staleSection);

            scrollViewer.Content = mainStack;
            Content = scrollViewer;
        }

        private Border CreateStatCard(string glyph, string label, out TextBlock valueText)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 45)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(4)
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            header.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            stack.Children.Add(header);

            valueText = new TextBlock
            {
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            stack.Children.Add(valueText);

            border.Child = stack;
            return border;
        }

        public void RefreshDashboard()
        {
            try
            {
                var summary = _healthService.GetSummary();
                var metrics = _healthService.CalculateHealthMetrics();
                var stats = _learningService.GetStatistics();
                var feedbackSummary = _feedbackService.GetSummary();

                // Update health score
                _healthScoreText!.Text = summary.HealthScore.ToString();
                _healthGradeText!.Text = summary.HealthGrade;

                // Color based on score
                var scoreColor = summary.HealthScore switch
                {
                    >= 75 => Color.FromArgb(255, 76, 175, 80),
                    >= 60 => Color.FromArgb(255, 139, 195, 74),
                    >= 40 => Color.FromArgb(255, 255, 193, 7),
                    >= 20 => Color.FromArgb(255, 255, 152, 0),
                    _ => Color.FromArgb(255, 244, 67, 54)
                };
                _healthScoreText.Foreground = new SolidColorBrush(scoreColor);
                _healthGradeText.Foreground = new SolidColorBrush(scoreColor);

                // Stats
                _patternsCountText!.Text = stats.TotalPatterns.ToString();
                _estimatesCountText!.Text = stats.EstimatesImported.ToString();
                _acceptanceRateText!.Text = $"{metrics.OverallAcceptanceRate:P0}";
                _rejectionRateText!.Text = $"{metrics.OverallRejectionRate:P0}";

                // Import progress
                var bootstrapBorder = (Border)((StackPanel)((ScrollViewer)Content).Content).Children
                    .First(c => c is Border b && b.Tag?.ToString() == "bootstrap");

                if (summary.IsBootstrapMode)
                {
                    bootstrapBorder.Visibility = Visibility.Visible;
                    _bootstrapText!.Text = $"{summary.BootstrapProgress}/20 estimates imported";
                    _bootstrapProgress!.Value = summary.BootstrapProgress;
                }
                else
                {
                    bootstrapBorder.Visibility = Visibility.Collapsed;
                }

                // Alerts
                _alertsPanel!.Children.Clear();
                var alerts = _healthService.GetCriticalAlerts();
                foreach (var alert in alerts)
                {
                    var alertBorder = new Border
                    {
                        Background = alert.Severity == AlertSeverity.Critical
                            ? new SolidColorBrush(Color.FromArgb(255, 60, 20, 20))
                            : new SolidColorBrush(Color.FromArgb(255, 50, 40, 20)),
                        BorderBrush = alert.Severity == AlertSeverity.Critical
                            ? new SolidColorBrush(Color.FromArgb(255, 244, 67, 54))
                            : new SolidColorBrush(Color.FromArgb(255, 255, 152, 0)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(12)
                    };

                    var alertStack = new StackPanel { Spacing = 4 };
                    alertStack.Children.Add(new TextBlock
                    {
                        Text = alert.Title,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Colors.White)
                    });
                    alertStack.Children.Add(new TextBlock
                    {
                        Text = alert.Message,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                        TextWrapping = TextWrapping.Wrap
                    });
                    alertStack.Children.Add(new TextBlock
                    {
                        Text = $"Recommended: {alert.Action}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 193, 7)),
                        FontStyle = Windows.UI.Text.FontStyle.Italic
                    });

                    alertBorder.Child = alertStack;
                    _alertsPanel.Children.Add(alertBorder);
                }

                // Recommendations
                _recommendationsPanel!.Children.Clear();
                foreach (var rec in metrics.Recommendations.Take(5))
                {
                    var recPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    recPanel.Children.Add(new TextBlock
                    {
                        Text = "•",
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 193, 7))
                    });
                    recPanel.Children.Add(new TextBlock
                    {
                        Text = rec,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                        TextWrapping = TextWrapping.Wrap
                    });
                    _recommendationsPanel.Children.Add(recPanel);
                }

                if (metrics.Recommendations.Count == 0)
                {
                    _recommendationsPanel.Children.Add(new TextBlock
                    {
                        Text = "No recommendations - system is healthy!",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 150))
                    });
                }

                // Top rejected patterns
                _topRejectedPanel!.Children.Clear();
                var rejectedPatterns = _feedbackService.GetMostRejectedPatterns(5);

                if (rejectedPatterns.Count > 0)
                {
                    foreach (var pattern in rejectedPatterns)
                    {
                        var patternPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        patternPanel.Children.Add(new TextBlock
                        {
                            Text = pattern.PatternKey,
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Colors.White)
                        });
                        patternPanel.Children.Add(new TextBlock
                        {
                            Text = $"({pattern.RejectionRate:P0} rejected)",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 244, 67, 54))
                        });
                        _topRejectedPanel.Children.Add(patternPanel);
                    }
                }
                else
                {
                    _topRejectedPanel.Children.Add(new TextBlock
                    {
                        Text = "No rejected patterns yet",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
                    });
                }

                // Stale patterns
                _stalePatternspanel!.Children.Clear();
                if (metrics.StalePatternKeys.Count > 0)
                {
                    foreach (var key in metrics.StalePatternKeys.Take(5))
                    {
                        _stalePatternspanel.Children.Add(new TextBlock
                        {
                            Text = key,
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 150))
                        });
                    }

                    if (metrics.StalePatternKeys.Count > 5)
                    {
                        _stalePatternspanel.Children.Add(new TextBlock
                        {
                            Text = $"...and {metrics.StalePatternKeys.Count - 5} more",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                            FontStyle = Windows.UI.Text.FontStyle.Italic
                        });
                    }
                }
                else
                {
                    _stalePatternspanel.Children.Add(new TextBlock
                    {
                        Text = "No stale patterns - all patterns are fresh!",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 150))
                    });
                }

                System.Diagnostics.Debug.WriteLine($"[Dashboard] Refreshed: Score={summary.HealthScore}, Patterns={stats.TotalPatterns}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] Error refreshing: {ex.Message}");
            }
        }
    }
}
