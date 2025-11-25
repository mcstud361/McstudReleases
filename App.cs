#nullable enable
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace McStudDesktop;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }
    public static TrayIconView? TrayIconView { get; private set; }
    public static Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcherQueue { get; private set; }

    private const string MutexName = "McStudDesktop";
    private Mutex? _mutex;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // TEMPORARILY DISABLED: Single instance logic causing UI thread deadlock on x64
        // bool isNewInstance;
        // _mutex = new Mutex(true, MutexName, out isNewInstance);
        //
        // if (!isNewInstance)
        // {
        //     // Another instance is running; terminate it
        //     TerminatePreviousInstance();
        // }

        Debug.WriteLine("[App] OnLaunched - single instance check bypassed for testing");

        // Create main window first (don't activate yet)
        MainWindow = new MainWindow();
        MainDispatcherQueue = MainWindow.DispatcherQueue;

        // Create TrayIconView and add to the main window's root grid
        // This must be done BEFORE activating the window
        TrayIconView = new TrayIconView
        {
            Name = "TrayIconView"
        };

        // Set the TrayIcon to be behind other content (Z-index = -1)
        Canvas.SetZIndex(TrayIconView, -1);
        MainWindow.RootGrid.Children.Add(TrayIconView);

        // Now activate the main window - this brings the visual tree to life
        MainWindow.Activate();

        Debug.WriteLine("[App] MainWindow activated with TrayIconView");

        // Ensure window is visible and brought to foreground
        var hWnd = WindowNative.GetWindowHandle(MainWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
        }
        Debug.WriteLine("[App] Window presenter state set to Restore");
    }

    public static void ShowMainWindow()
    {
        try
        {
            // If window doesn't exist or was closed, recreate it
            if (MainWindow == null)
            {
                Debug.WriteLine("[App] Recreating MainWindow");
                MainWindow = new MainWindow();
                MainDispatcherQueue = MainWindow.DispatcherQueue;

                // Re-add the TrayIconView to the new window
                if (TrayIconView != null)
                {
                    Canvas.SetZIndex(TrayIconView, -1);
                    MainWindow.RootGrid.Children.Add(TrayIconView);
                }
            }

            // Activate the window
            MainWindow.Activate();

            // Restore if minimized
            var hWnd = WindowNative.GetWindowHandle(MainWindow);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow != null && appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore();
            }

            Debug.WriteLine("[App] MainWindow shown");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Error showing window: {ex.Message}");
        }
    }

    public static void HideMainWindow()
    {
        // Close the window so it can be recreated fresh next time
        if (MainWindow != null)
        {
            try
            {
                Debug.WriteLine("[App] Closing MainWindow");

                // Remove TrayIconView from the window before closing
                if (TrayIconView != null && MainWindow.RootGrid.Children.Contains(TrayIconView))
                {
                    MainWindow.RootGrid.Children.Remove(TrayIconView);
                }

                MainWindow.Close();
                MainWindow = null;
                Debug.WriteLine("[App] MainWindow closed and set to null");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Error hiding window: {ex.Message}");
            }
        }
    }

    public static void ExitApplication()
    {
        TrayIconView?.Dispose();
        MainWindow?.Close();
        Environment.Exit(0);
    }

    private void TerminatePreviousInstance()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var processName = currentProcess.ProcessName;

            foreach (var process in Process.GetProcessesByName(processName))
            {
                if (process.Id != currentProcess.Id)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to terminate process {process.Id}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error while terminating previous instances: {ex.Message}");
        }
    }
}
