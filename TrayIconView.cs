#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Drawing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using H.NotifyIcon;

namespace McStudDesktop;

[ObservableObject]
public sealed partial class TrayIconView : UserControl, IDisposable
{
    [ObservableProperty]
    private bool _isWindowVisible = true;

    public TaskbarIcon TrayIcon { get; private set; }
    private Icon? _trayIcon;

    public TrayIconView()
    {
        InitializeUI();

        TrayIcon!.ToolTipText = "McStud Desktop";

        // Load the icon
        LoadIcon();

        // Register for Loaded event to call ForceCreate after we're in the visual tree
        this.Loaded += TrayIconView_Loaded;
    }

    private void TrayIconView_Loaded(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[TrayIcon] TrayIconView_Loaded fired");
        System.Diagnostics.Debug.WriteLine($"[TrayIcon] TrayIcon.Icon is null: {TrayIcon.Icon == null}");
        System.Diagnostics.Debug.WriteLine($"[TrayIcon] TrayIcon.Visibility: {TrayIcon.Visibility}");
        System.Diagnostics.Debug.WriteLine($"[TrayIcon] XamlRoot: {this.XamlRoot}");

        // Force the TaskbarIcon to show - must be called after control is loaded
        try
        {
            TrayIcon.ForceCreate();
            System.Diagnostics.Debug.WriteLine("[TrayIcon] ForceCreate called successfully in Loaded event");
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] After ForceCreate - IsCreated: {TrayIcon.IsCreated}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] ForceCreate failed: {ex.Message}");
        }
    }

    private void LoadIcon()
    {
        try
        {
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var iconPath = Path.Combine(appPath, "Assets", "appicon.ico");

            System.Diagnostics.Debug.WriteLine($"[TrayIcon] Looking for icon at: {iconPath}");
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] File exists: {File.Exists(iconPath)}");

            if (File.Exists(iconPath))
            {
                _trayIcon = new Icon(iconPath);
                TrayIcon.Icon = _trayIcon;
                System.Diagnostics.Debug.WriteLine($"[TrayIcon] Icon loaded successfully, Size: {_trayIcon.Width}x{_trayIcon.Height}");
            }
            else
            {
                // Try relative path
                iconPath = Path.Combine("Assets", "appicon.ico");
                System.Diagnostics.Debug.WriteLine($"[TrayIcon] Trying relative path: {iconPath}");
                if (File.Exists(iconPath))
                {
                    _trayIcon = new Icon(iconPath);
                    TrayIcon.Icon = _trayIcon;
                    System.Diagnostics.Debug.WriteLine($"[TrayIcon] Icon loaded from relative path, Size: {_trayIcon.Width}x{_trayIcon.Height}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[TrayIcon] No icon file found!");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] Icon loading failed: {ex.Message}");
        }
    }

    private void InitializeUI()
    {
        // Create the TaskbarIcon
        TrayIcon = new TaskbarIcon
        {
            Name = "TrayIcon",
            ContextMenuMode = ContextMenuMode.SecondWindow,
            NoLeftClickDelay = true,
            ToolTipText = "McStud Desktop",
            LeftClickCommand = ShowHideWindowCommand,
            Visibility = Visibility.Visible
        };

        // Create context menu
        var contextMenu = new MenuFlyout
        {
            AreOpenCloseAnimationsEnabled = false,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft,
            AllowFocusOnInteraction = true
        };

        // Style for menu presenter
        var menuStyle = new Style(typeof(MenuFlyoutPresenter));
        menuStyle.Setters.Add(new Setter(MenuFlyoutPresenter.MinWidthProperty, 180));
        menuStyle.Setters.Add(new Setter(MenuFlyoutPresenter.PaddingProperty, new Thickness(2)));
        contextMenu.MenuFlyoutPresenterStyle = menuStyle;

        // Style for menu items
        var itemStyle = new Style(typeof(MenuFlyoutItem));
        itemStyle.Setters.Add(new Setter(MenuFlyoutItem.MinHeightProperty, 32));
        itemStyle.Setters.Add(new Setter(MenuFlyoutItem.HeightProperty, 32));
        itemStyle.Setters.Add(new Setter(MenuFlyoutItem.PaddingProperty, new Thickness(12, 4, 12, 4)));

        var showHideItem = new MenuFlyoutItem
        {
            Text = "Show/Hide Window",
            Command = ShowHideWindowCommand,
            MinWidth = 160,
            Style = itemStyle
        };

        var exitItem = new MenuFlyoutItem
        {
            Text = "Exit",
            Command = ExitApplicationCommand,
            MinWidth = 160,
            Style = itemStyle
        };

        contextMenu.Items.Add(showHideItem);
        contextMenu.Items.Add(new MenuFlyoutSeparator());
        contextMenu.Items.Add(exitItem);

        TrayIcon.ContextFlyout = contextMenu;

        // TaskbarIcon MUST be the Content
        Content = TrayIcon;

        System.Diagnostics.Debug.WriteLine("[TrayIcon] InitializeUI completed");
    }

    [RelayCommand]
    public void ShowHideWindow()
    {
        try
        {
            // Check if window exists (null means it's been closed/hidden)
            if (App.MainWindow == null)
            {
                System.Diagnostics.Debug.WriteLine("[TrayIcon] MainWindow is null, showing (recreating)");
                App.ShowMainWindow();
                IsWindowVisible = true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[TrayIcon] MainWindow exists, hiding (closing)");
                App.HideMainWindow();
                IsWindowVisible = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] Error toggling window: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ExitApplication()
    {
        App.ExitApplication();
    }

    public void Dispose()
    {
        TrayIcon?.Dispose();
        _trayIcon?.Dispose();
    }
}
