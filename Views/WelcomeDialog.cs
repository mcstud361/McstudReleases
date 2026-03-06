#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Threading.Tasks;

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

        public static async Task<ContentDialogResult> ShowAsync(XamlRoot xamlRoot)
        {
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 420
            };

            var stack = new StackPanel { Spacing = 8, Padding = new Thickness(0, 8, 0, 0) };

            stack.Children.Add(new TextBlock
            {
                Text = "Here's what you can do with McStud Tool. Look for the ? button on each tab for detailed help anytime.",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });

            foreach (var (icon, name, description) in TabInfo)
            {
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(14, 10, 14, 10)
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

                row.Children.Add(new FontIcon
                {
                    Glyph = icon,
                    FontSize = 20,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255))
                });

                var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                textStack.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                });
                textStack.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                    TextWrapping = TextWrapping.Wrap
                });
                row.Children.Add(textStack);

                card.Child = row;
                stack.Children.Add(card);
            }

            scrollViewer.Content = stack;

            var dialog = new ContentDialog
            {
                Title = "Welcome to McStud Tool!",
                Content = scrollViewer,
                PrimaryButtonText = "Take the Tour",
                SecondaryButtonText = "Skip",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            return await dialog.ShowAsync();
        }
    }
}
