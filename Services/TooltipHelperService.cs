using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;

namespace McStudDesktop.Services;

/// <summary>
/// Helper service to add contextual tooltips to UI elements using ExcelKnowledgeService
/// </summary>
public class TooltipHelperService
{
    private static ExcelKnowledgeService? _knowledgeService;

    public static ExcelKnowledgeService Knowledge => _knowledgeService ??= new ExcelKnowledgeService();

    /// <summary>
    /// Add a rich tooltip to a framework element for a specific Excel input
    /// </summary>
    public static void AddInputTooltip(FrameworkElement element, string inputKey)
    {
        var info = Knowledge.GetInputInfo(inputKey);
        if (info != null)
        {
            var tooltip = CreateRichTooltip(info.Label, info.Description, info.Sheet, info.Cell);
            ToolTipService.SetToolTip(element, tooltip);
        }
    }

    /// <summary>
    /// Add a rich tooltip for a sheet
    /// </summary>
    public static void AddSheetTooltip(FrameworkElement element, string sheetName)
    {
        var sheets = Knowledge.GetAllSheets();
        if (sheets.TryGetValue(sheetName, out var sheetInfo))
        {
            var tooltip = CreateSheetTooltip(sheetInfo);
            ToolTipService.SetToolTip(element, tooltip);
        }
    }

    /// <summary>
    /// Add simple text tooltip
    /// </summary>
    public static void AddSimpleTooltip(FrameworkElement element, string text)
    {
        ToolTipService.SetToolTip(element, text);
    }

    /// <summary>
    /// Add tooltip for an operation type
    /// </summary>
    public static void AddOperationTooltip(FrameworkElement element, string operation, string description)
    {
        var tooltip = CreateOperationTooltip(operation, description);
        ToolTipService.SetToolTip(element, tooltip);
    }

    /// <summary>
    /// Create a rich tooltip panel for an Excel input
    /// </summary>
    private static ToolTip CreateRichTooltip(string label, string description, string sheet, string cell)
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            MaxWidth = 300
        };

        // Label header
        var headerText = new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(headerText);

        // Description
        if (!string.IsNullOrEmpty(description))
        {
            var descText = new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(descText);
        }

        // Location
        var locationPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var locationIcon = new FontIcon
        {
            Glyph = "\uE81D", // Map pin
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
        };

        var locationText = new TextBlock
        {
            Text = $"{sheet} → {cell}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
            FontFamily = new FontFamily("Consolas")
        };

        locationPanel.Children.Add(locationIcon);
        locationPanel.Children.Add(locationText);
        panel.Children.Add(locationPanel);

        return new ToolTip
        {
            Content = panel,
            Background = new SolidColorBrush(Color.FromArgb(240, 35, 35, 35)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            Padding = new Thickness(10)
        };
    }

    /// <summary>
    /// Create a tooltip for a sheet
    /// </summary>
    private static ToolTip CreateSheetTooltip(SheetInfo info)
    {
        var panel = new StackPanel
        {
            Spacing = 6,
            MaxWidth = 350
        };

        // Sheet name header
        var headerText = new TextBlock
        {
            Text = $"📊 {info.Name}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Colors.White)
        };
        panel.Children.Add(headerText);

        // Description
        var descText = new TextBlock
        {
            Text = info.Description,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(descText);

        // Purpose
        if (!string.IsNullOrEmpty(info.Purpose))
        {
            var purposeText = new TextBlock
            {
                Text = $"Purpose: {info.Purpose}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 150)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(purposeText);
        }

        // Key inputs if available
        if (info.KeyInputs?.Length > 0)
        {
            var keyInputsText = new TextBlock
            {
                Text = "Key inputs: " + string.Join(", ", info.KeyInputs.Take(4)),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(keyInputsText);
        }

        return new ToolTip
        {
            Content = panel,
            Background = new SolidColorBrush(Color.FromArgb(240, 35, 35, 35)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            Padding = new Thickness(12)
        };
    }

    /// <summary>
    /// Create a tooltip for an operation
    /// </summary>
    private static ToolTip CreateOperationTooltip(string operation, string description)
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            MaxWidth = 280
        };

        // Operation name
        var headerText = new TextBlock
        {
            Text = $"🔧 {operation}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Colors.White)
        };
        panel.Children.Add(headerText);

        // Description
        var descText = new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(descText);

        return new ToolTip
        {
            Content = panel,
            Background = new SolidColorBrush(Color.FromArgb(240, 35, 35, 35)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            Padding = new Thickness(10)
        };
    }

    /// <summary>
    /// Add a help button with click action
    /// </summary>
    public static Button CreateHelpButton(string inputKey, Action? onClick = null)
    {
        var btn = new Button
        {
            Content = new FontIcon
            {
                Glyph = "\uE897", // Help circle
                FontSize = 12
            },
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Add tooltip
        var info = Knowledge.GetInputInfo(inputKey);
        if (info != null)
        {
            var tooltip = CreateRichTooltip(info.Label, info.Description, info.Sheet, info.Cell);
            ToolTipService.SetToolTip(btn, tooltip);
        }

        if (onClick != null)
        {
            btn.Click += (s, e) => onClick();
        }

        return btn;
    }

    /// <summary>
    /// Get description text for an input (for use in labels, etc.)
    /// </summary>
    public static string GetInputDescription(string inputKey)
    {
        var info = Knowledge.GetInputInfo(inputKey);
        return info?.Description ?? "";
    }

    /// <summary>
    /// Get formatted location string for an input
    /// </summary>
    public static string GetInputLocation(string inputKey)
    {
        var info = Knowledge.GetInputInfo(inputKey);
        return info != null ? $"{info.Sheet}!{info.Cell}" : "";
    }
}
