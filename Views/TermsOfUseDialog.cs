#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Threading.Tasks;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    public static class TermsOfUseDialog
    {
        /// <summary>
        /// First-launch / new-version notice. Shows the Terms with a single "Continue" button.
        /// By continuing, the user accepts (browsewrap). Caller records acceptance afterward.
        /// </summary>
        public static Task ShowForAcceptanceAsync(XamlRoot xamlRoot)
            => ShowAsync(xamlRoot, forAcceptance: true);

        /// <summary>
        /// Read-only view of the Terms, opened from Settings. Dismissed with "Close".
        /// </summary>
        public static Task ShowReadOnlyAsync(XamlRoot xamlRoot)
            => ShowAsync(xamlRoot, forAcceptance: false);

        private static async Task ShowAsync(XamlRoot xamlRoot, bool forAcceptance)
        {
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 420
            };

            var stack = new StackPanel { Spacing = 16, Padding = new Thickness(0, 4, 0, 0) };

            if (forAcceptance)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "Please review the Terms of Use. By continuing to use McStud, you agree to them. If you do not agree, do not use the software.",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 180, 230)),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            foreach (var clause in TermsOfUseData.GetClauses())
            {
                var clausePanel = new StackPanel { Spacing = 4 };
                clausePanel.Children.Add(new TextBlock
                {
                    Text = clause.Heading,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                    TextWrapping = TextWrapping.Wrap
                });
                clausePanel.Children.Add(new TextBlock
                {
                    Text = clause.Body,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    TextWrapping = TextWrapping.Wrap
                });
                stack.Children.Add(clausePanel);
            }

            scrollViewer.Content = stack;

            var dialog = new ContentDialog
            {
                Title = $"McStud — Terms of Use (v{TermsOfUseData.Version})",
                Content = scrollViewer,
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            if (forAcceptance)
            {
                dialog.PrimaryButtonText = "Continue";
                dialog.DefaultButton = ContentDialogButton.Primary;
            }
            else
            {
                dialog.CloseButtonText = "Close";
                dialog.DefaultButton = ContentDialogButton.Close;
            }

            await dialog.ShowAsync();
        }
    }
}
