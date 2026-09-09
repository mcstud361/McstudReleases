#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views;

/// <summary>
/// Shared on-screen title-bar card for Shop Documents, matching the blueprint
/// checklist header: a blue card with the document title on top and the shop name
/// (or a supplied subtitle) beneath it. Use this everywhere so every Shop Doc
/// presents a consistent header.
/// </summary>
public static class ShopDocHeader
{
    private static readonly Color CardBlue = Color.FromArgb(255, 0, 80, 160);
    private static readonly Color SubtitleBlue = Color.FromArgb(255, 180, 210, 255);

    /// <summary>
    /// Build the blue header card. The second line shows the shop name from settings;
    /// if no shop name is set, it falls back to <paramref name="fallbackSubtitle"/> (if any).
    /// </summary>
    public static Border Build(string title, string? fallbackSubtitle = null)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(CardBlue),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });

        var shopName = ShopDocsSettingsService.Instance.GetSettings().ShopName;
        var subtitle = !string.IsNullOrEmpty(shopName) ? shopName : fallbackSubtitle;
        if (!string.IsNullOrEmpty(subtitle))
        {
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = new SolidColorBrush(SubtitleBlue),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        card.Child = stack;
        return card;
    }
}
