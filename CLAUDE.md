# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

McStudDesktop is a WinUI 3 desktop application for Windows 10/11 targeting .NET 10.0. The application features a main window with a system tray icon that persists when the window is minimized. The app enforces single-instance behavior by terminating any existing instances on launch.

## Build and Run Commands

**Build the project:**
```bash
dotnet build
```

**Run the application:**
```bash
dotnet run
```

**Build for release:**
```bash
dotnet build -c Release
```

**Clean build artifacts:**
```bash
dotnet clean
```

**Restore dependencies:**
```bash
dotnet restore
```

## Architecture and Key Patterns

### Application Entry Point

The application uses a custom entry point pattern rather than XAML-generated main:
- `Program.cs` contains the `[STAThread] Main()` method
- Sets up `DispatcherQueueSynchronizationContext` for WinUI 3
- `DISABLE_XAML_GENERATED_MAIN` is defined in the .csproj to prevent auto-generation

### Single Instance Management

The app enforces single-instance behavior in `App.cs:OnLaunched()`:
- Uses a named `Mutex` to detect existing instances
- Automatically terminates any running instances using `Process.Kill()`
- Ensures only one copy of the application runs at a time

### Tray Icon Integration

System tray functionality is implemented via a strict initialization sequence:

1. `MainWindow` is created first (but not yet activated)
2. `TrayIconView` UserControl is instantiated
3. `TrayIconView` is added to `MainWindow.RootGrid`
4. `MainWindow.Activate()` is called to bring the visual tree to life
5. `TrayIconView.Loaded` event fires, which calls `TrayIcon.ForceCreate()`

**Critical:** The tray icon MUST be added to the visual tree BEFORE the window is activated. Calling `ForceCreate()` must happen in the `Loaded` event handler, not earlier.

### Component Organization

- **Program.cs**: Application entry point with WinUI 3 initialization
- **App.cs**: Application lifecycle, single-instance enforcement, static accessors for MainWindow/TrayIcon/DispatcherQueue
- **MainWindow.cs**: Main UI window (800x600, centered), contains the root Grid
- **TrayIconView.cs**: System tray icon implementation using H.NotifyIcon with context menu
- **GlobalUsings.cs**: Global using directives for WinUI 3 and H.NotifyIcon namespaces

### Static Access Pattern

`App.cs` provides static properties for global access:
- `App.MainWindow`: Reference to the main window
- `App.TrayIconView`: Reference to the tray icon view
- `App.MainDispatcherQueue`: The main UI thread's DispatcherQueue

Static methods manage window state:
- `ShowMainWindow()`: Activates the main window
- `HideMainWindow()`: Minimizes the window (uses `OverlappedPresenter.Minimize()`)
- `ExitApplication()`: Disposes resources and terminates the app

### MVVM Pattern with CommunityToolkit.Mvvm

`TrayIconView` uses Source Generators from CommunityToolkit.Mvvm:
- `[ObservableObject]` on the class generates `INotifyPropertyChanged` implementation
- `[ObservableProperty]` on fields auto-generates properties with change notification
- `[RelayCommand]` on methods auto-generates `ICommand` properties for data binding

The generated commands (e.g., `ShowHideWindowCommand`, `ExitApplicationCommand`) are used in the tray icon's context menu.

## Key Dependencies

- **Microsoft.WindowsAppSDK**: WinUI 3 framework
- **CommunityToolkit.Mvvm**: MVVM source generators for observable properties and commands
- **H.NotifyIcon.WinUI**: System tray icon support for WinUI 3
- **Microsoft.WindowsDesktop.App.WindowsForms**: Required for `System.Drawing.Icon` support

## Project Configuration Notes

- Target Framework: `net10.0-windows10.0.19041.0` (Windows 10, version 2004 minimum)
- Self-contained deployment with Windows App SDK included
- Unpackaged application (no MSIX packaging)
- Nullable reference types enabled
- C# preview language features enabled
- Debug configuration suppresses warnings: CS0105, CS8618, CS8603, CS8625

## Asset Management

Icons and resources are stored in the `Assets/` directory and copied to output during build. The tray icon searches for `Assets/appicon.ico` relative to the application's base directory.

## Known Platform-Specific Issues

### x64 Window Rendering Issue

On x64 builds, adding the TrayIconView directly to the same Grid as UI content can cause rendering issues where the window is visible but content doesn't draw. **Solution**: Use separate child Grids with explicit Z-index layering:

- Content elements: Z-index 1 (foreground)
- TrayIconView: Z-index -1 (background)

This issue was not observed on ARM64 builds, suggesting a platform-specific rendering behavior in WinUI 3 or the H.NotifyIcon library.
