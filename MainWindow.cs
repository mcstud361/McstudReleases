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
    private bool _isAlwaysOnTop = LoadPinState();

    private static string PinStatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "McStudDesktop", "PinState.txt");

    private static bool LoadPinState()
    {
        try
        {
            if (File.Exists(PinStatePath))
                return File.ReadAllText(PinStatePath).Trim().Equals("false", StringComparison.OrdinalIgnoreCase) ? false : true;
        }
        catch { }
        return true; // Default: pinned
    }

    private void SavePinState()
    {
        try
        {
            var dir = Path.GetDirectoryName(PinStatePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(PinStatePath, _isAlwaysOnTop ? "true" : "false");
        }
        catch { }
    }

    /// <summary>
    /// Whether the window is currently pinned (always on top).
    /// </summary>
    public bool IsAlwaysOnTop => _isAlwaysOnTop;

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

        // Apply persisted pin state (defaults to pinned/always-on-top)
        // Use BOTH WinUI 3 presenter AND Win32 API for reliable always-on-top behavior
        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = _isAlwaysOnTop;
        }
        if (_isAlwaysOnTop)
            SetWindowAlwaysOnTop(hWnd);

        // Reapply after activation to ensure it sticks (only when pinned)
        this.Activated += (s, e) =>
        {
            if (_isAlwaysOnTop)
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
        else if (LoginAuthService.LoadSession() != null)
        {
            // Session exists — validate it online before showing the app
            _ = ValidateSessionAndRouteAsync();
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

    private async Task ValidateSessionAndRouteAsync()
    {
        if (_contentGrid == null) return;

        // Show a brief loading state while validating
        _contentGrid.Children.Clear();
        var loadingText = new TextBlock
        {
            Text = "Validating license...",
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _contentGrid.Children.Add(loadingText);

        var result = await LoginAuthService.ValidateSessionAsync();

        if (result.Success)
        {
            ShowUserTool();
        }
        else
        {
            ShowLoginPage(result.Message);
        }
    }

    private void ShowLoginPage(string? errorMessage = null)
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

        var loginView = new LoginView { InitialError = errorMessage };
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
    /// Toggle always-on-top (pin/unpin). Returns the new state.
    /// </summary>
    public bool ToggleAlwaysOnTop()
    {
        _isAlwaysOnTop = !_isAlwaysOnTop;
        ApplyAlwaysOnTop();
        SavePinState();
        return _isAlwaysOnTop;
    }

    /// <summary>
    /// Apply the current always-on-top state to the window.
    /// </summary>
    private void ApplyAlwaysOnTop()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var presenter = appWindow.Presenter as OverlappedPresenter;

        if (_isAlwaysOnTop)
        {
            if (presenter != null) presenter.IsAlwaysOnTop = true;
            SetWindowAlwaysOnTop(hWnd);
        }
        else
        {
            if (presenter != null) presenter.IsAlwaysOnTop = false;
            SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
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
        ApplyAlwaysOnTop();
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

        int width = (int)(workArea.Width * 0.45);
        int height = workArea.Height;
        int x = workArea.X + workArea.Width - width;
        int y = workArea.Y;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
        ApplyAlwaysOnTop();
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
        ApplyAlwaysOnTop();
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
