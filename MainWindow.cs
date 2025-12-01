#nullable enable
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.UI;
using McStudDesktop.Views;

namespace McStudDesktop;

public sealed class MainWindow : Window
{
    public Grid RootGrid { get; private set; }
    private Grid? _contentGrid;
    private Views.LoginPage? _loginPage;
    private Views.RegisterPage? _registerPage;
    private Views.AdminPanel? _adminPanel;
    private Grid? _mainAppContent;
    private bool _isAuthenticated = false;
    private string? _currentUsername;

    public MainWindow()
    {
        Title = "McStud Desktop";

        // Set up the window
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Set window icon
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "appicon.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }

        // Set window size to 1080x768 and center on screen
        int width = 1080*2;
        int height = 768*2;

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
        _contentGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Canvas.SetZIndex(_contentGrid, 1);

        RootGrid.Children.Add(_contentGrid);
        Content = RootGrid;

        // Show login page on startup
        ShowLoginPage();
    }

    private void ShowLoginPage()
    {
        if (_contentGrid == null) return;

        _contentGrid.Children.Clear();

        _loginPage = new Views.LoginPage();
        _loginPage.LoginSuccessful += LoginPage_LoginSuccessful;
        _loginPage.NavigateToRegister += LoginPage_NavigateToRegister;

        _contentGrid.Children.Add(_loginPage);
        System.Diagnostics.Debug.WriteLine("[MainWindow] Showing login page");
    }

    private void ShowRegisterPage()
    {
        if (_contentGrid == null) return;

        _contentGrid.Children.Clear();

        _registerPage = new Views.RegisterPage();
        _registerPage.RegistrationSuccessful += RegisterPage_RegistrationSuccessful;
        _registerPage.NavigateToLogin += RegisterPage_NavigateToLogin;

        _contentGrid.Children.Add(_registerPage);
        System.Diagnostics.Debug.WriteLine("[MainWindow] Showing register page");
    }

    private void ShowMainApp()
    {
        if (_contentGrid == null) return;

        _contentGrid.Children.Clear();

        _mainAppContent = CreateMainAppContent();
        _contentGrid.Children.Add(_mainAppContent);

        System.Diagnostics.Debug.WriteLine("[MainWindow] Showing main app content");
    }

    private Grid CreateMainAppContent()
    {
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

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
            Text = "Estimating Tool • Professional Dark Mode",
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
            Content = "Start Estimating",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0),
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };
        button.Click += EstimatingButton_Click;
        button.PointerEntered += (s, e) =>
        {
            primaryButtonBorder.Scale = new System.Numerics.Vector3(1.05f, 1.05f, 1);
        };
        button.PointerExited += (s, e) =>
        {
            primaryButtonBorder.Scale = new System.Numerics.Vector3(1, 1, 1);
        };
        primaryButtonBorder.Child = button;

        // Create admin panel button
        var adminButtonBorder = new Border
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

        var adminButton = new Button
        {
            Content = "Admin Panel",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            BorderThickness = new Thickness(0)
        };
        adminButton.Click += AdminButton_Click;
        adminButton.PointerEntered += (s, e) =>
        {
            adminButtonBorder.Scale = new System.Numerics.Vector3(1.05f, 1.05f, 1);
            adminButton.Foreground = new SolidColorBrush(Colors.White);
        };
        adminButton.PointerExited += (s, e) =>
        {
            adminButtonBorder.Scale = new System.Numerics.Vector3(1, 1, 1);
            adminButton.Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
        };
        adminButtonBorder.Child = adminButton;

        // Create logout button
        var logoutButtonBorder = new Border
        {
            Width = 280,
            Height = 50,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1)
        };

        var logoutButton = new Button
        {
            Content = "Logout",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0),
            FontSize = 16,
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            BorderThickness = new Thickness(0)
        };
        logoutButton.Click += LogoutButton_Click;
        logoutButton.PointerEntered += (s, e) =>
        {
            logoutButtonBorder.Scale = new System.Numerics.Vector3(1.02f, 1.02f, 1);
            logoutButton.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100));
        };
        logoutButton.PointerExited += (s, e) =>
        {
            logoutButtonBorder.Scale = new System.Numerics.Vector3(1, 1, 1);
            logoutButton.Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
        };
        logoutButtonBorder.Child = logoutButton;

        // Add all controls to the StackPanel
        stackPanel.Children.Add(titleText);
        stackPanel.Children.Add(subtitleText);
        stackPanel.Children.Add(primaryButtonBorder);
        stackPanel.Children.Add(adminButtonBorder);
        stackPanel.Children.Add(logoutButtonBorder);

        mainGrid.Children.Add(stackPanel);
        return mainGrid;
    }

    private void LoginPage_LoginSuccessful(object? sender, EventArgs e)
    {
        _isAuthenticated = true;
        ShowMainApp();
    }

    private void LoginPage_NavigateToRegister(object? sender, EventArgs e)
    {
        ShowRegisterPage();
    }

    private void RegisterPage_RegistrationSuccessful(object? sender, EventArgs e)
    {
        ShowLoginPage();
    }

    private void RegisterPage_NavigateToLogin(object? sender, EventArgs e)
    {
        ShowLoginPage();
    }

    private void EstimatingButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[MainWindow] Estimating tool button clicked");
        ShowEstimatingTool();
    }

    private void ShowEstimatingTool()
    {
        if (_contentGrid == null) return;

        _contentGrid.Children.Clear();

        var estimatingTool = new Views.EstimatingToolView_NEW();
        estimatingTool.BackToMenu += (s, e) => ShowMainApp();

        _contentGrid.Children.Add(estimatingTool);
        System.Diagnostics.Debug.WriteLine("[MainWindow] Estimating tool opened");
    }

    private void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (_contentGrid == null) return;

        // Show admin panel overlay
        _adminPanel = new Views.AdminPanel();
        _adminPanel.ClosePanel += (s, e) =>
        {
            if (_contentGrid != null && _adminPanel != null)
            {
                _contentGrid.Children.Remove(_adminPanel);
            }
        };

        Canvas.SetZIndex(_adminPanel, 100);
        _contentGrid.Children.Add(_adminPanel);

        System.Diagnostics.Debug.WriteLine("[MainWindow] Admin panel opened");
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _isAuthenticated = false;
        _currentUsername = null;
        ShowLoginPage();
        System.Diagnostics.Debug.WriteLine("[MainWindow] User logged out");
    }
}
