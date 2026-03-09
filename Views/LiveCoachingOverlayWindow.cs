#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using McstudDesktop.Models;
using McstudDesktop.Services;

namespace McStudDesktop.Views
{
    public sealed class LiveCoachingOverlayWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private readonly LiveCoachingService _coachingService;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

        private bool _isExpanded;
        private Grid _rootGrid = null!;
        private StackPanel _compactPanel = null!;
        private ScrollViewer _expandedScroller = null!;
        private StackPanel _suggestionsStack = null!;
        private Button _chevronButton = null!;

        // Compact mode elements
        private TextBlock _scoreText = null!;
        private TextBlock _gradeText = null!;
        private TextBlock _summaryText = null!;
        private TextBlock _potentialText = null!;

        private CoachingSnapshot? _lastSnapshot;

        // Window sizing
        private const int CompactWidth = 320;
        private const int CompactHeight = 80;
        private const int ExpandedWidth = 380;
        private const int ExpandedHeight = 500;

        public LiveCoachingOverlayWindow()
        {
            _coachingService = LiveCoachingService.Instance;
            _dispatcherQueue = this.DispatcherQueue;

            Title = "McStud Live Coach";

            // Configure window
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // Remove title bar, set size
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.SetBorderAndTitleBar(true, false);
            }

            // Position top-right
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            appWindow.MoveAndResize(new RectInt32(
                workArea.Width - CompactWidth - 20,
                workArea.Y + 20,
                CompactWidth,
                CompactHeight));

            // Force TOPMOST via Win32
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            BuildUI();

            _coachingService.SuggestionsUpdated += OnSuggestionsUpdated;
            this.Closed += OnWindowClosed;
        }

        private void BuildUI()
        {
            _rootGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 28, 28, 28)),
                Padding = new Thickness(0)
            };

            // Row 0: Compact header (always visible)
            // Row 1: Expanded content (toggled)
            _rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            BuildCompactPanel();
            BuildExpandedPanel();

            Content = _rootGrid;
        }

        private void BuildCompactPanel()
        {
            _compactPanel = new StackPanel
            {
                Padding = new Thickness(12, 8, 12, 8)
            };
            Grid.SetRow(_compactPanel, 0);

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Grade
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Score
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Summary
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Potential
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Chevron

            _gradeText = new TextBlock
            {
                Text = "--",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(_gradeText, 0);

            _scoreText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(_scoreText, 1);

            _summaryText = new TextBlock
            {
                Text = "Waiting for screen data...",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_summaryText, 2);

            _potentialText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 214, 0)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0)
            };
            Grid.SetColumn(_potentialText, 3);

            _chevronButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE70D", FontSize = 12 }, // ChevronDown
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = 30,
                VerticalAlignment = VerticalAlignment.Center
            };
            _chevronButton.Click += ChevronButton_Click;
            Grid.SetColumn(_chevronButton, 4);

            topRow.Children.Add(_gradeText);
            topRow.Children.Add(_scoreText);
            topRow.Children.Add(_summaryText);
            topRow.Children.Add(_potentialText);
            topRow.Children.Add(_chevronButton);

            _compactPanel.Children.Add(topRow);
            _rootGrid.Children.Add(_compactPanel);
        }

        private void BuildExpandedPanel()
        {
            _suggestionsStack = new StackPanel { Spacing = 4, Padding = new Thickness(8, 0, 8, 0) };

            _expandedScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        _suggestionsStack,
                        BuildBottomActions()
                    }
                },
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_expandedScroller, 1);
            _rootGrid.Children.Add(_expandedScroller);
        }

        private StackPanel BuildBottomActions()
        {
            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10,
                Padding = new Thickness(0, 6, 0, 8)
            };

            var dismissAllBtn = new Button
            {
                Content = new TextBlock { Text = "Dismiss All", FontSize = 11 },
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Padding = new Thickness(12, 4, 12, 4),
                CornerRadius = new CornerRadius(4)
            };
            dismissAllBtn.Click += (s, e) =>
            {
                if (_lastSnapshot != null)
                {
                    foreach (var suggestion in _lastSnapshot.Suggestions)
                        _coachingService.DismissSuggestion(suggestion.Id);
                }
                _suggestionsStack.Children.Clear();
            };

            var stopBtn = new Button
            {
                Content = new TextBlock { Text = "Stop Coaching", FontSize = 11 },
                Background = new SolidColorBrush(Color.FromArgb(255, 180, 50, 50)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 4, 12, 4),
                CornerRadius = new CornerRadius(4)
            };
            stopBtn.Click += (s, e) => this.Close();

            actionsPanel.Children.Add(dismissAllBtn);
            actionsPanel.Children.Add(stopBtn);
            return actionsPanel;
        }

        private void ChevronButton_Click(object sender, RoutedEventArgs e)
        {
            _isExpanded = !_isExpanded;

            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (_isExpanded)
            {
                _expandedScroller.Visibility = Visibility.Visible;
                ((FontIcon)_chevronButton.Content).Glyph = "\uE70E"; // ChevronUp

                var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                var workArea = displayArea.WorkArea;
                appWindow.MoveAndResize(new RectInt32(
                    workArea.Width - ExpandedWidth - 20,
                    workArea.Y + 20,
                    ExpandedWidth,
                    ExpandedHeight));
            }
            else
            {
                _expandedScroller.Visibility = Visibility.Collapsed;
                ((FontIcon)_chevronButton.Content).Glyph = "\uE70D"; // ChevronDown

                var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                var workArea = displayArea.WorkArea;
                appWindow.MoveAndResize(new RectInt32(
                    workArea.Width - CompactWidth - 20,
                    workArea.Y + 20,
                    CompactWidth,
                    CompactHeight));
            }

            // Re-apply topmost
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private void OnSuggestionsUpdated(object? sender, CoachingSnapshot snapshot)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _lastSnapshot = snapshot;
                    UpdateCompactView(snapshot);
                    UpdateExpandedView(snapshot);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LiveCoachingOverlay] UI update error: {ex.Message}");
                }
            });
        }

        private void UpdateCompactView(CoachingSnapshot snapshot)
        {
            _gradeText.Text = string.IsNullOrEmpty(snapshot.Grade) ? "--" : snapshot.Grade;
            _scoreText.Text = $"{snapshot.Score}/100";

            // Color the grade
            _gradeText.Foreground = new SolidColorBrush(GetGradeColor(snapshot.Grade));

            var active = snapshot.Suggestions.Where(s => !s.IsDismissed).ToList();
            var critical = active.Count(s => s.Severity == CoachingSeverity.Critical);
            var high = active.Count(s => s.Severity == CoachingSeverity.High);

            if (active.Count == 0)
                _summaryText.Text = "Looking good! No issues found.";
            else
            {
                var parts = new List<string>();
                if (critical > 0) parts.Add($"{critical} critical");
                if (high > 0) parts.Add($"{high} high");
                var other = active.Count - critical - high;
                if (other > 0) parts.Add($"{other} other");
                _summaryText.Text = string.Join(", ", parts);
            }

            if (snapshot.PotentialRecovery > 0)
                _potentialText.Text = $"+${snapshot.PotentialRecovery:F0}";
            else
                _potentialText.Text = "";
        }

        private void UpdateExpandedView(CoachingSnapshot snapshot)
        {
            _suggestionsStack.Children.Clear();

            var activeSuggestions = snapshot.Suggestions
                .Where(s => !s.IsDismissed)
                .OrderByDescending(s => s.Severity)
                .ThenByDescending(s => s.EstimatedCost)
                .Take(15)
                .ToList();

            if (activeSuggestions.Count == 0)
            {
                _suggestionsStack.Children.Add(new TextBlock
                {
                    Text = "No suggestions — estimate looks complete!",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
                return;
            }

            foreach (var suggestion in activeSuggestions)
            {
                _suggestionsStack.Children.Add(BuildSuggestionCard(suggestion));
            }
        }

        private Border BuildSuggestionCard(CoachingSuggestion suggestion)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 55, 55)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8)
            };

            var cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Cost + dismiss

            // Left side: severity badge + title + triggered by
            var leftStack = new StackPanel { Spacing = 3 };

            // Title row with severity badge
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            var severityBadge = new Border
            {
                Background = new SolidColorBrush(GetSeverityColor(suggestion.Severity)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            severityBadge.Child = new TextBlock
            {
                Text = suggestion.Severity.ToString().ToUpper(),
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };

            var titleText = new TextBlock
            {
                Text = suggestion.Title,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            titleRow.Children.Add(severityBadge);
            titleRow.Children.Add(titleText);
            leftStack.Children.Add(titleRow);

            // "For: [part name]"
            if (!string.IsNullOrEmpty(suggestion.TriggeredBy))
            {
                leftStack.Children.Add(new TextBlock
                {
                    Text = $"For: {suggestion.TriggeredBy}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            Grid.SetColumn(leftStack, 0);
            cardGrid.Children.Add(leftStack);

            // Right side: cost + dismiss
            var rightStack = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (suggestion.EstimatedCost > 0)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"${suggestion.EstimatedCost:F0}",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 214, 0)),
                    HorizontalAlignment = HorizontalAlignment.Right
                });
            }

            var dismissBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10 }, // Cancel/X
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(4, 2, 4, 2),
                MinWidth = 24,
                MinHeight = 24,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var suggestionId = suggestion.Id;
            dismissBtn.Click += (s, e) =>
            {
                _coachingService.DismissSuggestion(suggestionId);
                // Remove card from UI immediately
                if (card.Parent is Panel parent)
                    parent.Children.Remove(card);
            };

            rightStack.Children.Add(dismissBtn);
            Grid.SetColumn(rightStack, 1);
            cardGrid.Children.Add(rightStack);

            card.Child = cardGrid;
            return card;
        }

        private static Color GetSeverityColor(CoachingSeverity severity)
        {
            return severity switch
            {
                CoachingSeverity.Critical => Color.FromArgb(255, 220, 50, 50),   // Red
                CoachingSeverity.High => Color.FromArgb(255, 230, 140, 30),      // Orange
                CoachingSeverity.Medium => Color.FromArgb(255, 60, 130, 220),    // Blue
                CoachingSeverity.Low => Color.FromArgb(255, 120, 120, 120),      // Gray
                _ => Color.FromArgb(255, 120, 120, 120)
            };
        }

        private static Color GetGradeColor(string grade)
        {
            if (string.IsNullOrEmpty(grade)) return Color.FromArgb(255, 150, 150, 150);
            return grade[0] switch
            {
                'A' => Color.FromArgb(255, 76, 175, 80),    // Green
                'B' => Color.FromArgb(255, 139, 195, 74),   // Light green
                'C' => Color.FromArgb(255, 255, 193, 7),    // Yellow
                'D' => Color.FromArgb(255, 255, 152, 0),    // Orange
                'F' => Color.FromArgb(255, 244, 67, 54),    // Red
                _ => Color.FromArgb(255, 150, 150, 150)
            };
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _coachingService.SuggestionsUpdated -= OnSuggestionsUpdated;
        }
    }
}
