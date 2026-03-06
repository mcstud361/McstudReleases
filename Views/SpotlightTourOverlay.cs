#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using System;
using System.Collections.Generic;

namespace McStudDesktop.Views
{
    public class TourStep
    {
        public FrameworkElement Target { get; set; } = null!;
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int TabIndex { get; set; }
    }

    public sealed class SpotlightTourOverlay : UserControl
    {
        public event EventHandler? TourCompleted;
        public event EventHandler? TourSkipped;

        private readonly List<TourStep> _steps;
        private int _currentStepIndex;
        private readonly Grid _rootGrid;

        // Spotlight pieces
        private Border _topRect = null!;
        private Border _bottomRect = null!;
        private Border _leftRect = null!;
        private Border _rightRect = null!;
        private Border _highlightBorder = null!;

        // Tooltip card
        private Border _tooltipCard = null!;
        private FontIcon _tooltipIcon = null!;
        private TextBlock _tooltipTitle = null!;
        private TextBlock _tooltipDescription = null!;
        private TextBlock _stepIndicator = null!;
        private Button _backButton = null!;
        private Button _nextButton = null!;
        private Button _skipButton = null!;

        // Reference to McStudToolView for tab switching
        private readonly Action<int> _selectTabAction;

        private static readonly SolidColorBrush OverlayBrush = new(Color.FromArgb(200, 0, 0, 0));
        private static readonly SolidColorBrush HighlightBrush = new(Color.FromArgb(255, 0, 120, 215));

        public SpotlightTourOverlay(List<TourStep> steps, Action<int> selectTabAction)
        {
            _steps = steps;
            _currentStepIndex = 0;
            _selectTabAction = selectTabAction;

            _rootGrid = new Grid();
            BuildOverlay();
            Content = _rootGrid;

            Canvas.SetZIndex(this, 100);
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;

            this.KeyDown += OnKeyDown;
            this.IsTabStop = true;

            this.Loaded += (s, e) =>
            {
                this.Focus(FocusState.Programmatic);
                ShowStep(0);
            };
        }

        private void BuildOverlay()
        {
            // Four dark overlay rectangles
            _topRect = CreateOverlayRect();
            _bottomRect = CreateOverlayRect();
            _leftRect = CreateOverlayRect();
            _rightRect = CreateOverlayRect();

            _rootGrid.Children.Add(_topRect);
            _rootGrid.Children.Add(_bottomRect);
            _rootGrid.Children.Add(_leftRect);
            _rootGrid.Children.Add(_rightRect);

            // Highlight border around target
            _highlightBorder = new Border
            {
                BorderBrush = HighlightBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                IsHitTestVisible = false
            };
            _rootGrid.Children.Add(_highlightBorder);

            // Tooltip card
            BuildTooltipCard();
            _rootGrid.Children.Add(_tooltipCard);
        }

        private Border CreateOverlayRect()
        {
            return new Border
            {
                Background = OverlayBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = true
            };
        }

        private void BuildTooltipCard()
        {
            _tooltipCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                BorderBrush = HighlightBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20, 16, 20, 16),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                MinWidth = 300,
                MaxWidth = 400
            };

            var stack = new StackPanel { Spacing = 12 };

            // Header row: icon + title
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            _tooltipIcon = new FontIcon
            {
                FontSize = 22,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255))
            };
            _tooltipTitle = new TextBlock
            {
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerRow.Children.Add(_tooltipIcon);
            headerRow.Children.Add(_tooltipTitle);
            stack.Children.Add(headerRow);

            // Description
            _tooltipDescription = new TextBlock
            {
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 190, 190, 190)),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(_tooltipDescription);

            // Step indicator
            _stepIndicator = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            };
            stack.Children.Add(_stepIndicator);

            // Button row
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            _backButton = new Button
            {
                Content = "Back",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            _backButton.Click += (s, e) => NavigateStep(-1);

            _nextButton = new Button
            {
                Content = "Next",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 200)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            _nextButton.Click += (s, e) => NavigateStep(1);

            _skipButton = new Button
            {
                Content = "Skip Tour",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                CornerRadius = new CornerRadius(4)
            };
            _skipButton.Click += (s, e) => EndTour(skipped: true);

            buttonRow.Children.Add(_backButton);
            buttonRow.Children.Add(_nextButton);
            buttonRow.Children.Add(_skipButton);
            stack.Children.Add(buttonRow);

            _tooltipCard.Child = stack;
        }

        private void ShowStep(int index)
        {
            if (index < 0 || index >= _steps.Count) return;

            _currentStepIndex = index;
            var step = _steps[index];

            // Switch to the tab
            _selectTabAction(step.TabIndex);

            // Update tooltip content
            _tooltipIcon.Glyph = step.Icon;
            _tooltipTitle.Text = step.Title;
            _tooltipDescription.Text = step.Description;
            _stepIndicator.Text = $"Step {index + 1} of {_steps.Count}";

            // Update button states
            _backButton.Visibility = index > 0 ? Visibility.Visible : Visibility.Collapsed;
            _nextButton.Content = index == _steps.Count - 1 ? "Finish" : "Next";

            // Position spotlight around target element
            PositionSpotlight(step.Target);
        }

        private void PositionSpotlight(FrameworkElement target)
        {
            try
            {
                // Get target position relative to the main window content
                var windowContent = McstudDesktop.App.MainWindow?.Content as FrameworkElement;
                if (windowContent == null) return;

                var transform = target.TransformToVisual(windowContent);
                var targetPos = transform.TransformPoint(new Point(0, 0));
                var targetWidth = target.ActualWidth;
                var targetHeight = target.ActualHeight;

                var windowWidth = windowContent.ActualWidth;
                var windowHeight = windowContent.ActualHeight;

                // Add padding around the target
                double pad = 4;
                double x = targetPos.X - pad;
                double y = targetPos.Y - pad;
                double w = targetWidth + pad * 2;
                double h = targetHeight + pad * 2;

                // Top rect: full width, from top to target top
                _topRect.Width = windowWidth;
                _topRect.Height = Math.Max(0, y);
                _topRect.Margin = new Thickness(0, 0, 0, 0);

                // Bottom rect: full width, from target bottom to window bottom
                double bottomY = y + h;
                _bottomRect.Width = windowWidth;
                _bottomRect.Height = Math.Max(0, windowHeight - bottomY);
                _bottomRect.Margin = new Thickness(0, bottomY, 0, 0);

                // Left rect: from left edge to target left, height = target height
                _leftRect.Width = Math.Max(0, x);
                _leftRect.Height = h;
                _leftRect.Margin = new Thickness(0, y, 0, 0);

                // Right rect: from target right to window right, height = target height
                double rightX = x + w;
                _rightRect.Width = Math.Max(0, windowWidth - rightX);
                _rightRect.Height = h;
                _rightRect.Margin = new Thickness(rightX, y, 0, 0);

                // Highlight border
                _highlightBorder.Width = w;
                _highlightBorder.Height = h;
                _highlightBorder.Margin = new Thickness(x, y, 0, 0);
                _highlightBorder.HorizontalAlignment = HorizontalAlignment.Left;
                _highlightBorder.VerticalAlignment = VerticalAlignment.Top;

                // Position tooltip card below the target
                double tooltipX = Math.Max(8, Math.Min(x, windowWidth - 320));
                double tooltipY = y + h + 12;

                // If tooltip would go off bottom, show above instead
                if (tooltipY + 200 > windowHeight)
                {
                    tooltipY = Math.Max(8, y - 200);
                }

                _tooltipCard.Margin = new Thickness(tooltipX, tooltipY, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Tour] Error positioning spotlight: {ex.Message}");
            }
        }

        private void NavigateStep(int direction)
        {
            int newIndex = _currentStepIndex + direction;

            if (newIndex >= _steps.Count)
            {
                EndTour(skipped: false);
                return;
            }

            if (newIndex >= 0)
            {
                ShowStep(newIndex);
            }
        }

        private void EndTour(bool skipped)
        {
            // Remove self from parent
            if (Parent is Grid parentGrid)
            {
                parentGrid.Children.Remove(this);
            }

            if (skipped)
                TourSkipped?.Invoke(this, EventArgs.Empty);
            else
                TourCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                EndTour(skipped: true);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Right || e.Key == Windows.System.VirtualKey.Enter)
            {
                NavigateStep(1);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Left)
            {
                NavigateStep(-1);
                e.Handled = true;
            }
        }
    }
}
