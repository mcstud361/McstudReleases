#nullable enable
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using McstudDesktop.Services;

namespace McstudDesktop;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }
    public static Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcherQueue { get; private set; }

    private const string MutexName = "McstudTool";
    private Mutex? _mutex;

    public App()
    {
        this.InitializeComponent();

        // Global safety nets so a single stray exception can't silently kill the app.
        // Each path logs to crash.log; the UI-thread path keeps the app alive.
        this.UnhandledException += OnUiUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUiUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash("UI thread", e.Exception);
        // Keep the app running instead of terminating on a non-fatal UI error.
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        LogCrash("Background thread", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("Unobserved task", e.Exception);
        e.SetObserved();
    }

    /// <summary>
    /// Appends an error (with timestamp and stack trace) to
    /// %LocalAppData%\McStudDesktop\crash.log. This file is the diagnostic
    /// report a user can send in when something goes wrong.
    /// </summary>
    public static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop",
                "crash.log");

            var dir = System.IO.Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}:\n{ex}\n\n";
            System.IO.File.AppendAllText(logPath, entry);
            Debug.WriteLine($"[App] Logged {source} exception: {ex?.Message}");
        }
        catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Single instance check
            bool isNewInstance;
            _mutex = new Mutex(true, MutexName, out isNewInstance);

            if (!isNewInstance)
            {
                // Another instance is running - just exit
                Debug.WriteLine("[App] Another instance already running");
                Environment.Exit(0);
                return;
            }

            Debug.WriteLine("[App] Starting McStud Tool");

            // Apply baseline data before any services are accessed
            try
            {
                McStudDesktop.Services.BaselineDataService.Instance.ApplyBaselineIfNeeded();
            }
            catch (Exception baselineEx)
            {
                Debug.WriteLine($"[App] Baseline apply error (non-fatal): {baselineEx.Message}");
            }

            // Create and show main window
            MainWindow = new MainWindow();
            MainDispatcherQueue = MainWindow.DispatcherQueue;

            MainWindow.Activate();

            // Ensure window is visible
            var hWnd = WindowNative.GetWindowHandle(MainWindow);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore();
            }

            Debug.WriteLine("[App] MainWindow activated");
        }
        catch (Exception ex)
        {
            // Startup failures are logged, then rethrown — the app can't run without a window.
            LogCrash("Startup", ex);
            Debug.WriteLine($"[App] CRASH: {ex}");
            throw;
        }
    }

    public static void ExitApplication()
    {
        MainWindow?.Close();
        Environment.Exit(0);
    }

    /// <summary>
    /// Releases the single-instance mutex so a Velopack restart
    /// doesn't race against it and immediately exit.
    /// Call this before ApplyUpdatesAndRestart.
    /// </summary>
    public static void ReleaseMutex()
    {
        try
        {
            ((App)Current)._mutex?.ReleaseMutex();
            ((App)Current)._mutex?.Dispose();
            ((App)Current)._mutex = null;
            Debug.WriteLine("[App] Mutex released for update restart");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Mutex release error (non-fatal): {ex.Message}");
        }
    }
}
