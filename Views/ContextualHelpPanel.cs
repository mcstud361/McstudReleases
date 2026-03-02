#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Contextual Help Panel - Shows help specific to the current view/tab.
    /// Can be added as a flyout or inline help section.
    /// </summary>
    public sealed class ContextualHelpPanel : UserControl
    {
        private readonly METGuideService _guideService;
        private readonly string _viewId;

        private static readonly Color CardBg = Color.FromArgb(255, 35, 35, 35);
        private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 215);
        private static readonly Color AccentGreen = Color.FromArgb(255, 0, 150, 80);
        private static readonly Color TextGray = Color.FromArgb(255, 160, 160, 160);

        public ContextualHelpPanel(string viewId)
        {
            _guideService = METGuideService.Instance;
            _viewId = viewId;
            BuildUI();
        }

        private void BuildUI()
        {
            var help = _guideService.GetContextualHelp(_viewId);

            if (help == null)
            {
                Content = new TextBlock
                {
                    Text = "No help available for this view.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(TextGray)
                };
                return;
            }

            var mainStack = new StackPanel
            {
                Spacing = 12,
                Padding = new Thickness(4)
            };

            // Title
            mainStack.Children.Add(new TextBlock
            {
                Text = help.Title ?? "Help",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Description
            if (!string.IsNullOrEmpty(help.Description))
            {
                mainStack.Children.Add(new TextBlock
                {
                    Text = help.Description,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(TextGray),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            // Steps
            if (help.Steps != null && help.Steps.Count > 0)
            {
                var stepsHeader = new TextBlock
                {
                    Text = "How to use:",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(AccentBlue),
                    Margin = new Thickness(0, 8, 0, 4)
                };
                mainStack.Children.Add(stepsHeader);

                foreach (var step in help.Steps)
                {
                    mainStack.Children.Add(new TextBlock
                    {
                        Text = step,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.White),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(8, 2, 0, 2)
                    });
                }
            }

            // Tips
            if (help.Tips != null && help.Tips.Count > 0)
            {
                var tipsHeader = new TextBlock
                {
                    Text = "Tips:",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(AccentGreen),
                    Margin = new Thickness(0, 8, 0, 4)
                };
                mainStack.Children.Add(tipsHeader);

                foreach (var tip in help.Tips)
                {
                    mainStack.Children.Add(new TextBlock
                    {
                        Text = "✓ " + tip,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(AccentGreen),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(8, 2, 0, 2)
                    });
                }
            }

            Content = mainStack;
        }
    }

    /// <summary>
    /// Helper class to create a help button with flyout for any view
    /// </summary>
    public static class ContextualHelpButton
    {
        /// <summary>
        /// Creates a help button (?) that shows contextual help in a flyout
        /// </summary>
        public static Button Create(string viewId, XamlRoot? xamlRoot = null)
        {
            var button = new Button
            {
                Content = "?",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CornerRadius = new CornerRadius(14),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            // Create flyout with help content
            var flyout = new Flyout
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
            };

            var helpPanel = new ContextualHelpPanel(viewId)
            {
                MinWidth = 250,
                MaxWidth = 300
            };

            flyout.Content = helpPanel;
            button.Flyout = flyout;

            // Hover effect
            button.PointerEntered += (s, e) =>
            {
                button.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
                button.Foreground = new SolidColorBrush(Colors.White);
            };

            button.PointerExited += (s, e) =>
            {
                button.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50));
                button.Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
            };

            return button;
        }

        /// <summary>
        /// Creates a compact inline help hint
        /// </summary>
        public static Border CreateInlineHint(string text)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 60, 80)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };

            stack.Children.Add(new FontIcon
            {
                Glyph = "\uE946", // Light bulb
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100))
            });

            stack.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 220, 240)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });

            border.Child = stack;
            return border;
        }
    }
}
