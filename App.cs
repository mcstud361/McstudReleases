#nullable enable
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Velopack;
using Velopack.Sources;
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

            // Check for updates in background
            _ = CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            // Write to a log file for debugging
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop",
                "crash.log");

            try
            {
                var dir = System.IO.Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                System.IO.File.WriteAllText(logPath,
                    $"Crash at {DateTime.Now}:\n{ex}\n\nStack trace:\n{ex.StackTrace}");
            }
            catch { }

            Debug.WriteLine($"[App] CRASH: {ex}");
            throw;
        }
    }

    public static void ExitApplication()
    {
        MainWindow?.Close();
        Environment.Exit(0);
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            // GitHub releases URL for McStud updates
            var source = new GithubSource("https://github.com/mcstud361/McstudReleases", null, false);
            var mgr = new UpdateManager(source);

            // Check if app was installed via Velopack (won't work in dev/debug mode)
            if (!mgr.IsInstalled)
            {
                Debug.WriteLine("[App] Not installed via Velopack - skipping update check");
                return;
            }

            Debug.WriteLine("[App] Checking for updates...");
            var newVersion = await mgr.CheckForUpdatesAsync();

            if (newVersion == null)
            {
                Debug.WriteLine("[App] No updates available");
                return;
            }

            Debug.WriteLine($"[App] Update available: {newVersion.TargetFullRelease.Version}");

            // Show update dialog on UI thread
            MainDispatcherQueue?.TryEnqueue(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = "Update Available",
                    Content = $"A new version ({newVersion.TargetFullRelease.Version}) is available.\n\nWould you like to download and install it now?",
                    PrimaryButtonText = "Update Now",
                    CloseButtonText = "Later",
                    XamlRoot = MainWindow?.Content?.XamlRoot
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        // Download and apply update
                        await mgr.DownloadUpdatesAsync(newVersion);
                        mgr.ApplyUpdatesAndRestart(newVersion);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[App] Update failed: {ex.Message}");
                        var errorDialog = new ContentDialog
                        {
                            Title = "Update Failed",
                            Content = $"Failed to install update: {ex.Message}",
                            CloseButtonText = "OK",
                            XamlRoot = MainWindow?.Content?.XamlRoot
                        };
                        await errorDialog.ShowAsync();
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Update check failed: {ex.Message}");
        }
    }
}
