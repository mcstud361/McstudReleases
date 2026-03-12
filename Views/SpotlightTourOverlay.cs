#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
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

        // Spotlight overlay rects
        private Border _topRect = null!;
        private Border _bottomRect = null!;
        private Border _leftRect = null!;
        private Border _rightRect = null!;

        // Glow ring borders (outer glow + inner crisp)
        private Border _glowOuter = null!;
        private Border _glowInner = null!;

        // Tooltip card elements
        private Border _tooltipCard = null!;
        private Grid _tooltipRoot = null!;
        private Border _accentBar = null!;
        private Grid _iconBadge = null!;
        private FontIcon _tooltipIcon = null!;
        private TextBlock _tooltipTitle = null!;
        private TextBlock _tooltipDescription = null!;
        private StackPanel _progressDotsPanel = null!;
        private Button _backButton = null!;
        private Button _nextButton = null!;
        private Button _skipButton = null!;

        // Welcome/finish flourish
        private TextBlock _welcomeHeader = null!;
        private FontIcon _finishCheckmark = null!;

        // Tab switching
        private readonly Action<int> _selectTabAction;

        // Animation state
        private DispatcherTimer? _animTimer;
        private double _currentX, _currentY, _currentW, _currentH;
        private double _targetX, _targetY, _targetW, _targetH;
        private double _tooltipCurrentX, _tooltipCurrentY;
        private double _tooltipTargetX, _tooltipTargetY;
        private bool _isFirstPosition = true;
        private double _animElapsed;
        private double _windowWidth, _windowHeight;

        // Fade animation
        private DispatcherTimer? _fadeTimer;
        private double _fadeFrom, _fadeTo;
        private double _fadeDuration;
        private double _fadeElapsed;
        private Action? _fadeCallback;

        // Colors
        private static readonly Color OverlayColor = Color.FromArgb(210, 10, 12, 18);
        private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 215);
        private static readonly Color AccentTeal = Color.FromArgb(255, 0, 200, 180);
        private static readonly Color CardBg = Color.FromArgb(255, 28, 30, 36);
        private static readonly Color CardBorder = Color.FromArgb(255, 50, 55, 65);
        private static readonly Color ButtonDarkBg = Color.FromArgb(255, 42, 44, 52);

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

            // Start with opacity 0 for fade-in
            this.Opacity = 0;

            this.Loaded += (s, e) =>
            {
                this.Focus(FocusState.Programmatic);
                ShowStep(0);
                // Fade in
                AnimateFade(0, 1, 300, null);
            };

            // Start the animation timer
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _animTimer.Tick += OnAnimTick;
        }

        private void BuildOverlay()
        {
            var overlayBrush = new SolidColorBrush(OverlayColor);

            _topRect = CreateOverlayRect(overlayBrush);
            _bottomRect = CreateOverlayRect(overlayBrush);
            _leftRect = CreateOverlayRect(overlayBrush);
            _rightRect = CreateOverlayRect(overlayBrush);

            _rootGrid.Children.Add(_topRect);
            _rootGrid.Children.Add(_bottomRect);
            _rootGrid.Children.Add(_leftRect);
            _rootGrid.Children.Add(_rightRect);

            // Outer glow border (pulsing)
            _glowOuter = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 0, 120, 215)),
                BorderThickness = new Thickness(4),
                CornerRadius = new CornerRadius(6),
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0.7
            };
            _rootGrid.Children.Add(_glowOuter);

            // Inner crisp border
            _glowInner = new Border
            {
                BorderBrush = new SolidColorBrush(AccentBlue),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            _rootGrid.Children.Add(_glowInner);

            // Welcome header (shown on first step briefly)
            _welcomeHeader = new TextBlock
            {
                Text = "Let's explore!",
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 20, 0, 0),
                Opacity = 0
            };
            _rootGrid.Children.Add(_welcomeHeader);

            BuildTooltipCard();
            _rootGrid.Children.Add(_tooltipCard);
        }

        private Border CreateOverlayRect(SolidColorBrush brush)
        {
            return new Border
            {
                Background = brush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = true
            };
        }

        private void BuildTooltipCard()
        {
            // Outer card border
            _tooltipCard = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                MinWidth = 310,
                MaxWidth = 400
            };

            // Root grid: accent bar | content
            _tooltipRoot = new Grid();
            _tooltipRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            _tooltipRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left accent bar with gradient
            _accentBar = new Border
            {
                CornerRadius = new CornerRadius(10, 0, 0, 10),
                Background = CreateAccentGradient()
            };
            Grid.SetColumn(_accentBar, 0);
            _tooltipRoot.Children.Add(_accentBar);

            // Content area
            var contentStack = new StackPanel
            {
                Spacing = 12,
                Padding = new Thickness(18, 16, 18, 16)
            };
            Grid.SetColumn(contentStack, 1);

            // Header: icon badge + title
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            // Circular icon badge
            _iconBadge = new Grid
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(AccentBlue)
            };
            _tooltipIcon = new FontIcon
            {
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _iconBadge.Children.Add(_tooltipIcon);
            headerRow.Children.Add(_iconBadge);

            // Title + finish checkmark
            var titlePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            _tooltipTitle = new TextBlock
            {
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            _finishCheckmark = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 200, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            titlePanel.Children.Add(_tooltipTitle);
            titlePanel.Children.Add(_finishCheckmark);
            headerRow.Children.Add(titlePanel);

            contentStack.Children.Add(headerRow);

            // Description
            _tooltipDescription = new TextBlock
            {
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 185, 185, 195)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            };
            contentStack.Children.Add(_tooltipDescription);

            // Progress dots
            _progressDotsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Margin = new Thickness(0, 2, 0, 2)
            };
            contentStack.Children.Add(_progressDotsPanel);

            // Button row
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 2, 0, 0)
            };

            _backButton = new Button
            {
                Content = "Back",
                Padding = new Thickness(18, 7, 18, 7),
                Background = new SolidColorBrush(ButtonDarkBg),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 210)),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 62, 72))
            };
            _backButton.Click += (s, e) => NavigateStep(-1);

            _nextButton = new Button
            {
                Content = "Next",
                Padding = new Thickness(20, 7, 20, 7),
                Background = new SolidColorBrush(AccentBlue),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(0)
            };
            _nextButton.Click += (s, e) => NavigateStep(1);

            _skipButton = new Button
            {
                Padding = new Thickness(12, 7, 12, 7),
                Background = new SolidColorBrush(Colors.Transparent),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(0)
            };
            // Underlined "Skip" text
            var skipText = new TextBlock
            {
                Text = "Skip",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 130)),
                TextDecorations = Windows.UI.Text.TextDecorations.Underline
            };
            _skipButton.Content = skipText;
            _skipButton.Click += (s, e) => EndTour(skipped: true);

            buttonRow.Children.Add(_backButton);
            buttonRow.Children.Add(_nextButton);
            buttonRow.Children.Add(_skipButton);
            contentStack.Children.Add(buttonRow);

            _tooltipRoot.Children.Add(contentStack);
            _tooltipCard.Child = _tooltipRoot;
        }

        private LinearGradientBrush CreateAccentGradient()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };
            brush.GradientStops.Add(new GradientStop { Color = AccentBlue, Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = AccentTeal, Offset = 1 });
            return brush;
        }

        private void UpdateProgressDots()
        {
            _progressDotsPanel.Children.Clear();

            for (int i = 0; i < _steps.Count; i++)
            {
                if (i == _currentStepIndex)
                {
                    // Current step: elongated pill
                    var pill = new Border
                    {
                        Width = 20,
                        Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(AccentBlue)
                    };
                    _progressDotsPanel.Children.Add(pill);
                }
                else if (i < _currentStepIndex)
                {
                    // Completed: filled circle
                    var dot = new Border
                    {
                        Width = 8,
                        Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180))
                    };
                    _progressDotsPanel.Children.Add(dot);
                }
                else
                {
                    // Upcoming: dim hollow circle
                    var dot = new Border
                    {
                        Width = 8,
                        Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Colors.Transparent),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 72, 82)),
                        BorderThickness = new Thickness(1.5)
                    };
                    _progressDotsPanel.Children.Add(dot);
                }
            }
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

            // Update progress dots
            UpdateProgressDots();

            // Update button states
            _backButton.Visibility = index > 0 ? Visibility.Visible : Visibility.Collapsed;

            bool isLast = index == _steps.Count - 1;
            if (isLast)
            {
                _nextButton.Content = "You're all set!";
                _finishCheckmark.Visibility = Visibility.Visible;
                _skipButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                _nextButton.Content = "Next";
                _finishCheckmark.Visibility = Visibility.Collapsed;
                _skipButton.Visibility = Visibility.Visible;
            }

            // Welcome flourish on first step
            if (index == 0)
            {
                ShowWelcomeFlourish();
            }

            // Calculate target spotlight position
            CalculateTargetPosition(step.Target);

            // If first position, snap instantly; otherwise animate
            if (_isFirstPosition)
            {
                _isFirstPosition = false;
                _currentX = _targetX;
                _currentY = _targetY;
                _currentW = _targetW;
                _currentH = _targetH;
                _tooltipCurrentX = _tooltipTargetX;
                _tooltipCurrentY = _tooltipTargetY;
                ApplyPositions();
            }
            else
            {
                // Start animation
                _animTimer?.Start();
            }
        }

        private void ShowWelcomeFlourish()
        {
            _welcomeHeader.Opacity = 1;

            var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            double elapsed = 0;
            double holdDuration = 800; // hold visible for 800ms
            double fadeDuration = 400; // then fade over 400ms

            fadeTimer.Tick += (s, e) =>
            {
                elapsed += 16;
                if (elapsed < holdDuration)
                {
                    // Holding visible
                }
                else if (elapsed < holdDuration + fadeDuration)
                {
                    double t = (elapsed - holdDuration) / fadeDuration;
                    _welcomeHeader.Opacity = 1.0 - t;
                }
                else
                {
                    _welcomeHeader.Opacity = 0;
                    fadeTimer.Stop();
                }
            };
            fadeTimer.Start();
        }

        private void CalculateTargetPosition(FrameworkElement target)
        {
            try
            {
                var windowContent = McstudDesktop.App.MainWindow?.Content as FrameworkElement;
                if (windowContent == null) return;

                var transform = target.TransformToVisual(windowContent);
                var targetPos = transform.TransformPoint(new Point(0, 0));
                var targetWidth = target.ActualWidth;
                var targetHeight = target.ActualHeight;

                _windowWidth = windowContent.ActualWidth;
                _windowHeight = windowContent.ActualHeight;

                double pad = 6;
                _targetX = targetPos.X - pad;
                _targetY = targetPos.Y - pad;
                _targetW = targetWidth + pad * 2;
                _targetH = targetHeight + pad * 2;

                // Calculate tooltip position
                _tooltipTargetX = Math.Max(8, Math.Min(_targetX, _windowWidth - 330));
                _tooltipTargetY = _targetY + _targetH + 14;

                // If tooltip would go off bottom, show above
                if (_tooltipTargetY + 200 > _windowHeight)
                {
                    _tooltipTargetY = Math.Max(8, _targetY - 220);
                }

                // If first position, also set tooltip current
                if (_isFirstPosition)
                {
                    _tooltipCurrentX = _tooltipTargetX;
                    _tooltipCurrentY = _tooltipTargetY;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Tour] Error calculating position: {ex.Message}");
            }
        }

        private void OnAnimTick(object? sender, object e)
        {
            const double lerpFactor = 0.15;
            const double snapThreshold = 0.5;

            _currentX += (_targetX - _currentX) * lerpFactor;
            _currentY += (_targetY - _currentY) * lerpFactor;
            _currentW += (_targetW - _currentW) * lerpFactor;
            _currentH += (_targetH - _currentH) * lerpFactor;
            _tooltipCurrentX += (_tooltipTargetX - _tooltipCurrentX) * lerpFactor;
            _tooltipCurrentY += (_tooltipTargetY - _tooltipCurrentY) * lerpFactor;

            // Track elapsed for glow pulse
            _animElapsed += 16;

            bool done = Math.Abs(_currentX - _targetX) < snapThreshold
                     && Math.Abs(_currentY - _targetY) < snapThreshold
                     && Math.Abs(_currentW - _targetW) < snapThreshold
                     && Math.Abs(_currentH - _targetH) < snapThreshold;

            if (done)
            {
                _currentX = _targetX;
                _currentY = _targetY;
                _currentW = _targetW;
                _currentH = _targetH;
                _tooltipCurrentX = _tooltipTargetX;
                _tooltipCurrentY = _tooltipTargetY;
                // Don't stop timer — keep it running for glow pulse
            }

            ApplyPositions();

            // Glow pulse
            double glowOpacity = 0.4 + 0.6 * (0.5 + 0.5 * Math.Sin(_animElapsed * 0.003 * 3.0));
            _glowOuter.Opacity = glowOpacity;
        }

        private void ApplyPositions()
        {
            double x = _currentX;
            double y = _currentY;
            double w = _currentW;
            double h = _currentH;

            // Top rect
            _topRect.Width = _windowWidth;
            _topRect.Height = Math.Max(0, y);
            _topRect.Margin = new Thickness(0, 0, 0, 0);

            // Bottom rect
            double bottomY = y + h;
            _bottomRect.Width = _windowWidth;
            _bottomRect.Height = Math.Max(0, _windowHeight - bottomY);
            _bottomRect.Margin = new Thickness(0, bottomY, 0, 0);

            // Left rect
            _leftRect.Width = Math.Max(0, x);
            _leftRect.Height = h;
            _leftRect.Margin = new Thickness(0, y, 0, 0);

            // Right rect
            double rightX = x + w;
            _rightRect.Width = Math.Max(0, _windowWidth - rightX);
            _rightRect.Height = h;
            _rightRect.Margin = new Thickness(rightX, y, 0, 0);

            // Glow borders (outer is slightly larger)
            _glowOuter.Width = w + 4;
            _glowOuter.Height = h + 4;
            _glowOuter.Margin = new Thickness(x - 2, y - 2, 0, 0);

            _glowInner.Width = w;
            _glowInner.Height = h;
            _glowInner.Margin = new Thickness(x, y, 0, 0);

            // Tooltip card
            _tooltipCard.Margin = new Thickness(_tooltipCurrentX, _tooltipCurrentY, 0, 0);
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
            // Stop animation timer
            _animTimer?.Stop();
            _animTimer = null;

            // Fade out then remove
            AnimateFade(1, 0, 200, () =>
            {
                if (Parent is Grid parentGrid)
                {
                    parentGrid.Children.Remove(this);
                }

                if (skipped)
                    TourSkipped?.Invoke(this, EventArgs.Empty);
                else
                    TourCompleted?.Invoke(this, EventArgs.Empty);
            });
        }

        private void AnimateFade(double from, double to, double durationMs, Action? callback)
        {
            _fadeFrom = from;
            _fadeTo = to;
            _fadeDuration = durationMs;
            _fadeElapsed = 0;
            _fadeCallback = callback;
            this.Opacity = from;

            _fadeTimer?.Stop();
            _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _fadeTimer.Tick += OnFadeTick;
            _fadeTimer.Start();
        }

        private void OnFadeTick(object? sender, object e)
        {
            _fadeElapsed += 16;
            double t = Math.Min(1.0, _fadeElapsed / _fadeDuration);
            // Ease-out quad
            double eased = 1.0 - (1.0 - t) * (1.0 - t);
            this.Opacity = _fadeFrom + (_fadeTo - _fadeFrom) * eased;

            if (t >= 1.0)
            {
                this.Opacity = _fadeTo;
                _fadeTimer?.Stop();
                _fadeTimer = null;
                _fadeCallback?.Invoke();
            }
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
