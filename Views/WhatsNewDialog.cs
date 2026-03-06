#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Linq;
using System.Threading.Tasks;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    public static class WhatsNewDialog
    {
        public static async Task ShowAsync(XamlRoot xamlRoot, string lastSeenVersion)
        {
            Version lastSeen;
            try
            {
                lastSeen = new Version(lastSeenVersion);
            }
            catch
            {
                lastSeen = new Version(0, 0, 0);
            }

            var changelog = ChangelogData.GetChangelog();
            var newEntries = changelog.Where(e =>
            {
                var vStr = e.Version.TrimStart('v');
                try { return new Version(vStr) > lastSeen; }
                catch { return false; }
            }).ToList();

            if (newEntries.Count == 0)
                return;

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 400
            };

            var stack = new StackPanel { Spacing = 20, Padding = new Thickness(0, 8, 0, 0) };

            foreach (var entry in newEntries)
            {
                var entryPanel = new StackPanel { Spacing = 8 };

                var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                headerStack.Children.Add(new TextBlock
                {
                    Text = entry.Version,
                    FontSize = 15,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255))
                });
                headerStack.Children.Add(new TextBlock
                {
                    Text = entry.Date,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                entryPanel.Children.Add(headerStack);

                var changeStack = new StackPanel { Spacing = 4, Margin = new Thickness(8, 0, 0, 0) };
                foreach (var change in entry.Changes)
                {
                    var changeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    changeRow.Children.Add(new TextBlock
                    {
                        Text = "•",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
                    });
                    changeRow.Children.Add(new TextBlock
                    {
                        Text = change,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                        TextWrapping = TextWrapping.Wrap
                    });
                    changeStack.Children.Add(changeRow);
                }
                entryPanel.Children.Add(changeStack);
                stack.Children.Add(entryPanel);
            }

            scrollViewer.Content = stack;

            var dialog = new ContentDialog
            {
                Title = $"What's New in {UpdateService.GetVersionString()}",
                Content = scrollViewer,
                CloseButtonText = "Got it",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            await dialog.ShowAsync();
        }
    }
}
