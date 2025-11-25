#nullable enable
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.UI;

namespace McStudDesktop;

public sealed class MainWindow : Window
{
    public Grid RootGrid { get; private set; }

    public MainWindow()
    {
        Title = "McStud Desktop";

        // Set up the window
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Set window size to 1080x768 and center on screen
        int width = 1080;
        int height = 768;

        // Get display area
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        // Calculate center position
        int x = (workArea.Width - width) / 2 + workArea.X;
        int y = (workArea.Height - height) / 2 + workArea.Y;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        // Create the main content grid with black gradient background
        RootGrid = new Grid
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 0, 0, 0), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(255, 20, 20, 20), Offset = 1.0 }
                }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // Create content grid for UI elements (separate from tray icon)
        var contentGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Canvas.SetZIndex(contentGrid, 1);

        // Create a StackPanel to hold multiple test controls
        var stackPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 20
        };

        // Add a title TextBlock
        var titleText = new TextBlock
        {
            Text = "McStud Desktop",
            FontSize = 48,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
        };

        // Add a subtitle TextBlock
        var subtitleText = new TextBlock
        {
            Text = "Professional Dark Mode • Premium Effects",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            Margin = new Thickness(0, 10, 0, 30)
        };

        // Create primary button with black/gray gradient
        var primaryButtonBorder = new Border
        {
            Width = 280,
            Height = 60,
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 40, 40, 40), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(255, 60, 60, 60), Offset = 1.0 }
                }
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
            BorderThickness = new Thickness(2)
        };

        var button = new Button
        {
            Content = "Get to Work",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0),
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };
        button.Click += Button_Click;
        button.PointerEntered += (s, e) =>
        {
            primaryButtonBorder.Scale = new System.Numerics.Vector3(1.05f, 1.05f, 1);
        };
        button.PointerExited += (s, e) =>
        {
            primaryButtonBorder.Scale = new System.Numerics.Vector3(1, 1, 1);
        };
        primaryButtonBorder.Child = button;

        // Create secondary button with darker gray gradient
        var secondaryButtonBorder = new Border
        {
            Width = 280,
            Height = 60,
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 25, 25, 25), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(255, 40, 40, 40), Offset = 1.0 }
                }
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            BorderThickness = new Thickness(2)
        };

        var testButton = new Button
        {
            Content = "Settings & Options",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            BorderThickness = new Thickness(0)
        };
        testButton.Click += TestButton_Click;
        testButton.PointerEntered += (s, e) =>
        {
            secondaryButtonBorder.Scale = new System.Numerics.Vector3(1.05f, 1.05f, 1);
            testButton.Foreground = new SolidColorBrush(Colors.White);
        };
        testButton.PointerExited += (s, e) =>
        {
            secondaryButtonBorder.Scale = new System.Numerics.Vector3(1, 1, 1);
            testButton.Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
        };
        secondaryButtonBorder.Child = testButton;

        // Add all controls to the StackPanel
        stackPanel.Children.Add(titleText);
        stackPanel.Children.Add(subtitleText);
        stackPanel.Children.Add(primaryButtonBorder);
        stackPanel.Children.Add(secondaryButtonBorder);

        contentGrid.Children.Add(stackPanel);
        RootGrid.Children.Add(contentGrid);
        Content = RootGrid;
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.Content = "Let's Go! 🚀";
            System.Diagnostics.Debug.WriteLine("[MainWindow] Primary button clicked - Get to Work!");
        }
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.Content = "Settings Opened ⚙️";
            System.Diagnostics.Debug.WriteLine("[MainWindow] Secondary button clicked - Settings & Options!");
        }
    }
}
