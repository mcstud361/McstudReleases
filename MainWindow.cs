#nullable enable
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.UI;
using McStudDesktop.Views;
using McStudDesktop.Services;
using System.Runtime.InteropServices;

namespace McstudDesktop;

public sealed class MainWindow : Window
{
    // Win32 API for always on top
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private static void SetWindowAlwaysOnTop(IntPtr hWnd)
    {
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public Grid RootGrid { get; private set; }
    private Grid? _contentGrid;
    private bool _isAdminMode = false;
    private bool _isLearningMode = false;

    public MainWindow()
    {
        var shopName = ShopDocsSettingsService.Instance.GetSettings().ShopName ?? "";
        Title = string.IsNullOrWhiteSpace(shopName) ? "McStud Tool" : $"{shopName} — McStud Tool";

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

        // Set window size - compact for the tool
        int width = 380;
        int height = 600;

        // Get display area
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        // Position in bottom-right corner (like a tool window)
        int x = workArea.Width - width - 20 + workArea.X;
        int y = workArea.Height - height - 60 + workArea.Y;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        // Make window always on top so it stays visible when switching to CCC
        // Use BOTH WinUI 3 presenter AND Win32 API for reliable always-on-top behavior
        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
        }
        SetWindowAlwaysOnTop(hWnd);

        // Reapply after activation to ensure it sticks
        this.Activated += (s, e) =>
        {
            SetWindowAlwaysOnTop(hWnd);
        };

        // Create the main content grid with dark background
        RootGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // Create content grid
        _contentGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0)
        };

        RootGrid.Children.Add(_contentGrid);
        Content = RootGrid;

        // Check for admin mode (hold Shift on launch or command line arg)
        CheckAdminMode();

        // Show appropriate view
        if (_isLearningMode)
        {
            ShowLearningCenter();
        }
        else if (_isAdminMode)
        {
            ShowAdminTool();
        }
        else if (LoginAuthService.IsSessionValid())
        {
            // Auto-login: session exists, skip login page
            ShowUserTool();
        }
        else
        {
            ShowLoginPage();
        }
    }

    private void CheckAdminMode()
    {
        // Check command line args for --admin or --learning
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args)
        {
            if (arg.Equals("--admin", StringComparison.OrdinalIgnoreCase))
            {
                _isAdminMode = true;
            }
            else if (arg.Equals("--learning", StringComparison.OrdinalIgnoreCase))
            {
                _isLearningMode = true;
            }
        }
    }

    private void ShowLoginPage()
    {
        if (_contentGrid == null) return;

        Title = "McStud - Login";

        // Resize for login (centered, larger)
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Remove always on top for login
        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = false;
        }

        int width = 500;
        int height = 650;

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        int x = (workArea.Width - width) / 2 + workArea.X;
        int y = (workArea.Height - height) / 2 + workArea.Y;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        _contentGrid.Children.Clear();

        var loginView = new LoginView();
        loginView.LoginSuccessful += (s, e) =>
        {
            ShowUserTool();
        };
        _contentGrid.Children.Add(loginView);

        System.Diagnostics.Debug.WriteLine("[MainWindow] Showing Login Page");
    }

    private void ShowUserTool()
    {
        if (_contentGrid == null) return;

        var shopName = ShopDocsSettingsService.Instance.GetSettings().ShopName ?? "";
        Title = string.IsNullOrWhiteSpace(shopName) ? "McStud Tool" : $"{shopName} — McStud Tool";

        // Resize back to compact tool window in bottom-right
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        int width = 380;
        int height = 600;

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        int x = workArea.Width - width - 20 + workArea.X;
        int y = workArea.Height - height - 60 + workArea.Y;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        // Re-enable always on top for tool mode
        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
        }
        SetWindowAlwaysOnTop(hWnd);

        _contentGrid.Children.Clear();

        var toolView = new McStudToolView();
        _contentGrid.Children.Add(toolView);

        System.Diagnostics.Debug.WriteLine("[MainWindow] Showing User Tool");
    }

    private void ShowAdminTool()
    {
        if (_contentGrid == null) return;

        Title = "McStud Admin";

        // Resize for admin view (needs more space)
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        int width = 500;
        int height = 750;

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        int x = (workArea.Width - width) / 2 + workArea.X;
        int y = (workArea.Height - height) / 2 + workArea.Y;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        // Keep admin mode always on top too
        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
        }
        SetWindowAlwaysOnTop(hWnd);

        _contentGrid.Children.Clear();

        var adminView = new McStudAdminView();
        _contentGrid.Children.Add(adminView);

        System.Diagnostics.Debug.WriteLine("[MainWindow] Showing Admin Tool");
    }

    private void ShowLearningCenter()
    {
        if (_contentGrid == null) return;

        Title = "McStud Learning Center";

        // Resize for learning center (needs more space for tabs and data)
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Remove always on top for learning center
        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = false;
        }

        int width = 900;
        int height = 700;

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        int x = (workArea.Width - width) / 2 + workArea.X;
        int y = (workArea.Height - height) / 2 + workArea.Y;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        _contentGrid.Children.Clear();

        var learningView = new LearningCenterView();
        _contentGrid.Children.Add(learningView);

        System.Diagnostics.Debug.WriteLine("[MainWindow] Showing Learning Center");
    }

    /// <summary>
    /// Snap window to quarter screen (bottom-right corner).
    /// </summary>
    public void SnapQuarterScreen()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        int width = workArea.Width / 2;
        int height = workArea.Height / 2;
        int x = workArea.X + workArea.Width - width;
        int y = workArea.Y + workArea.Height - height;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
        }
        SetWindowAlwaysOnTop(hWnd);
    }

    /// <summary>
    /// Snap window to half screen (right side).
    /// </summary>
    public void SnapHalfScreen()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        int width = workArea.Width / 2;
        int height = workArea.Height;
        int x = workArea.X + workArea.Width - width;
        int y = workArea.Y;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
        }
        SetWindowAlwaysOnTop(hWnd);
    }

    /// <summary>
    /// Snap back to compact tool window (default position).
    /// </summary>
    public void SnapCompact()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        int width = 380;
        int height = 600;
        int x = workArea.Width - width - 20 + workArea.X;
        int y = workArea.Height - height - 60 + workArea.Y;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
        }
        SetWindowAlwaysOnTop(hWnd);
    }

    /// <summary>
    /// Switch between user and admin mode
    /// </summary>
    public void ToggleMode()
    {
        _isAdminMode = !_isAdminMode;

        if (_isAdminMode)
        {
            ShowAdminTool();
        }
        else
        {
            ShowUserTool();
        }
    }
}
