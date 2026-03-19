#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using System.Threading.Tasks;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    public static class WelcomeDialog
    {
        private static readonly (string Icon, string Name, string Description)[] TabInfo = new[]
        {
            ("\uE8C8", "Export", "Virtual clipboard that reads Excel and types into CCC/Mitchell for you"),
            ("\uE8BD", "Chat", "AI assistant for estimating questions"),
            ("\uE8E5", "Import", "Upload PDF estimates for analysis"),
            ("\uE9D9", "Stats", "Track your export statistics"),
            ("\uE82D", "Reference", "Definitions, DEG, P-Pages, Procedures"),
            ("\uE713", "Settings", "App settings, updates, version info"),
            ("\uE82D", "Guide", "Step-by-step MET guides"),
            ("\uE8A5", "Shop Docs", "Checklists and shop documents")
        };

        private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 215);
        private static readonly Color AccentTeal = Color.FromArgb(255, 0, 200, 180);

        public static async Task<ContentDialogResult> ShowAsync(XamlRoot xamlRoot)
        {
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 450
            };

            var outerStack = new StackPanel { Spacing = 14, Padding = new Thickness(0, 4, 0, 0) };

            // Styled header
            var headerPanel = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var userName = ShopDocsSettingsService.Instance.GetSettings().UserName ?? "";
            headerPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(userName) ? "McStud Tool" : $"Welcome, {userName}!",
                FontSize = 26,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentBlue)
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(userName) ? "Your estimating co-pilot" : "McStud Tool — Your estimating co-pilot",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 150, 170)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
            outerStack.Children.Add(headerPanel);

            outerStack.Children.Add(new TextBlock
            {
                Text = "Here's what you can do. Look for the ? button on each tab for detailed help anytime.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 180)),
                Margin = new Thickness(0, 0, 0, 2),
                TextWrapping = TextWrapping.Wrap
            });

            var cardsStack = new StackPanel { Spacing = 6 };

            foreach (var (icon, name, description) in TabInfo)
            {
                // Card with left accent bar
                var card = new Grid();
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Accent bar
                var accentBar = new Border
                {
                    CornerRadius = new CornerRadius(6, 0, 0, 6),
                    Background = CreateAccentGradient()
                };
                Grid.SetColumn(accentBar, 0);
                card.Children.Add(accentBar);

                // Content
                var cardContent = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 32, 34, 40)),
                    CornerRadius = new CornerRadius(0, 6, 6, 0),
                    Padding = new Thickness(12, 9, 12, 9)
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 11 };

                row.Children.Add(new FontIcon
                {
                    Glyph = icon,
                    FontSize = 18,
                    Foreground = new SolidColorBrush(AccentBlue)
                });

                var textStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
                textStack.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                });
                textStack.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 145, 145, 155)),
                    TextWrapping = TextWrapping.Wrap
                });
                row.Children.Add(textStack);

                cardContent.Child = row;
                Grid.SetColumn(cardContent, 1);
                card.Children.Add(cardContent);

                cardsStack.Children.Add(card);
            }

            outerStack.Children.Add(cardsStack);
            scrollViewer.Content = outerStack;

            var dialog = new ContentDialog
            {
                Title = "Welcome!",
                Content = scrollViewer,
                PrimaryButtonText = "\uE8BE  Take the Tour",
                SecondaryButtonText = "Skip for now",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            return await dialog.ShowAsync();
        }

        private static LinearGradientBrush CreateAccentGradient()
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
    }
}
