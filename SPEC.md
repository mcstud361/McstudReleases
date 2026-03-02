# McstudDesktop - Product Specification

## 1. Introduction & Vision

### What is McstudDesktop?

McstudDesktop is a modern Windows desktop productivity and launcher application built with WinUI 3 and .NET 10.0. Designed for Windows 10/11, it provides a clean, professional interface that stays accessible via the system tray, allowing users to quickly access productivity features without cluttering their desktop.

### Vision Statement

McstudDesktop aims to become a **comprehensive productivity launcher** that helps users efficiently manage their workflow, access important content, and launch applications quickly. The application combines elegant design with practical functionality, serving as a central hub for daily productivity tasks.

### Design Philosophy

- **Always Accessible**: System tray integration ensures the app is always one click away
- **Professional Aesthetics**: Dark mode UI with modern design principles for reduced eye strain
- **Lightweight & Fast**: Quick startup, minimal resource usage, single-instance enforcement
- **User-Centric**: Customizable preferences and persistent settings across sessions
- **Windows-Native**: Leverages WinUI 3 for native performance and modern Windows 11 aesthetics

### Target Platform

- **Operating System**: Windows 10 (version 2004+) and Windows 11
- **Framework**: .NET 10.0 with WinUI 3
- **Deployment**: Self-contained, unpackaged desktop application
- **Architecture**: x64 (with ARM64 support available)

---

## 2. Current Features (v1.0)

### 2.1 System Tray Integration

**Description**: McstudDesktop lives in the Windows system tray, providing persistent access without occupying taskbar space.

**Features**:
- Persistent tray icon with custom application branding
- Left-click to toggle main window visibility
- Right-click context menu with:
  - "Show/Hide Window" option
  - "Exit Application" option
- Tooltip displays "Mcstud Desktop" on hover

**User Benefit**: Quick access to the application without keeping a window open continuously.

### 2.2 Window Management

**Description**: Flexible window display options that integrate seamlessly with the system tray.

**Features**:
- Main window dimensions: 1080×768 pixels
- Centered window positioning on launch
- Hide/show window via tray icon
- Single-instance enforcement (prevents multiple copies from running)
- Clean window recreation on show

**User Benefit**: Users can minimize clutter by hiding the window while keeping the app accessible via tray icon.

### 2.3 Dark Mode UI

**Description**: Professional dark-themed interface optimized for low-light environments and reduced eye strain.

**UI Elements**:
- **Window Background**: Subtle gradient from black to dark gray
- **Title Text**: "Mcstud Desktop" in large, bold white text
- **Subtitle**: "Professional Dark Mode • Premium Effects" in gray
- **Primary Action Button**: "Get to Work" with gradient background and hover animations
- **Secondary Action Button**: "Settings & Options" with darker styling
- **Visual Effects**: Smooth scale animations on button hover (1.05× scale)

**Design Details**:
- Modern rounded corners (12px border radius)
- Light gray borders (#646464) for depth
- Gradient button backgrounds for premium feel
- Responsive hover states with visual feedback

**User Benefit**: Comfortable viewing experience that looks professional and modern.

### 2.4 Single-Instance Behavior

**Description**: Ensures only one instance of McstudDesktop runs at any time.

**Implementation**: Automatic detection and termination of existing instances on launch.

**User Benefit**: Prevents confusion and resource waste from accidentally launching multiple copies.

### 2.5 Interactive UI Elements

**Current Buttons**:
- **"Get to Work"**: Primary call-to-action button (ready for productivity feature integration)
- **"Settings & Options"**: Secondary button (prepared for settings panel)

**Interaction Design**:
- Hover effects with smooth scaling animation
- Visual state feedback on click
- Professional button styling with gradients

---

## 3. Technical Overview

### 3.1 Application Architecture

```
Program.cs (Entry Point)
    ├─ Initializes WinUI 3 environment
    ├─ Sets up DispatcherQueue synchronization
    └─ Creates App instance

App.cs (Application Lifecycle)
    ├─ OnLaunched: Single-instance check
    ├─ MainWindow initialization
    ├─ TrayIconView integration
    └─ Static accessors for global components

MainWindow.cs (UI Window)
    ├─ 1080×768 centered window
    ├─ Dark mode UI with gradient styling
    ├─ Interactive buttons
    └─ Container for TrayIconView

TrayIconView.cs (System Tray)
    ├─ H.NotifyIcon integration
    ├─ Context menu commands
    └─ MVVM pattern with CommunityToolkit
```

### 3.2 Component Responsibilities

| Component | Responsibility |
|-----------|----------------|
| **Program.cs** | Application entry point, WinUI 3 initialization |
| **App.cs** | Lifecycle management, static accessors, window state control |
| **MainWindow.cs** | Primary UI window, dark mode styling, button interactions |
| **TrayIconView.cs** | System tray icon, context menu, show/hide commands |
| **GlobalUsings.cs** | Centralized namespace imports for WinUI and NotifyIcon |

### 3.3 Key Dependencies

| Package | Purpose |
|---------|---------|
| **Microsoft.WindowsAppSDK** | WinUI 3 framework and Windows 11 controls |
| **CommunityToolkit.Mvvm** | MVVM source generators (ObservableObject, RelayCommand) |
| **H.NotifyIcon.WinUI** | System tray icon support for WinUI 3 applications |
| **Microsoft.WindowsDesktop.App.WindowsForms** | System.Drawing.Icon support |

### 3.4 MVVM Pattern

McstudDesktop uses **CommunityToolkit.Mvvm** source generators to implement MVVM:

- `[ObservableObject]` generates INotifyPropertyChanged implementation
- `[ObservableProperty]` auto-generates properties with change notification
- `[RelayCommand]` creates ICommand properties for data binding

This approach reduces boilerplate code while maintaining clean separation of concerns.

### 3.5 Initialization Sequence

1. `Program.Main()` starts the application
2. `App.OnLaunched()` checks for existing instances
3. `MainWindow` is created but not yet shown
4. `TrayIconView` is instantiated and added to window
5. `MainWindow.Activate()` brings window to life
6. `TrayIconView.Loaded` event calls `ForceCreate()` on tray icon
7. Main window displays with full functionality

**Critical Note**: The tray icon must be added to the visual tree **before** window activation to ensure proper rendering on x64 platforms.

### 3.6 Known Issues & Workarounds

**Issue**: On x64 builds, TrayIconView in the same Grid as UI content can cause rendering problems.

**Workaround**: Use separate child Grids with explicit Z-index layering:
- Content elements at Z-index 1 (foreground)
- TrayIconView at Z-index -1 (background)

**Status**: This issue is not observed on ARM64 builds and appears to be platform-specific.

**Issue**: Single-instance Mutex check is temporarily disabled due to UI thread deadlock on x64.

**Workaround**: Process-based termination of existing instances.

**Status**: Acceptable for current version, may be revisited in future updates.

---

## 4. Future Roadmap

### 4.1 Vision: Productivity Launcher

McstudDesktop is evolving into a comprehensive **productivity and launcher application** that serves as a central hub for:
- Quick access to frequently used applications
- Content management (notes, files, recent items)
- Productivity features (tasks, timers, quick captures)
- Deep system integration with hotkeys and startup behavior

### 4.2 Version 1.1 (Near-Term) - Settings & Persistence

**Target**: Foundation for user customization and state management

**Features**:
- **Settings Panel**
  - User preferences UI (accessible from "Settings & Options" button)
  - Appearance customization (theme selection, window size preferences)
  - Behavior settings (startup options, tray icon behavior)
  - Application about/version information

- **Window & Preference Persistence**
  - Remember window size and position across sessions
  - Save user preferences to local configuration file
  - Restore previous state on application launch
  - Reset to defaults option

**Technical Considerations**:
- JSON-based configuration file in user's AppData folder
- Settings service/manager for centralized configuration access
- Window position validation (ensure window stays on-screen)

### 4.3 Version 1.2 (Short-Term) - Content & System Integration

**Target**: Basic productivity features and deeper OS integration

**Features**:
- **Content Management**
  - Quick notes panel (simple text capture)
  - Recent files list with quick access
  - Clipboard history viewer
  - Favorites/bookmarks section

- **System Integration**
  - Global hotkey support (e.g., Win+Space to show/hide)
  - Run at startup option (Windows registry integration)
  - Desktop notifications for reminders
  - Jump list integration for quick actions

**Technical Considerations**:
- Windows API integration for hotkeys and startup
- File system watcher for recent files
- Notification API for Windows 10/11 toast notifications

### 4.4 Version 2.0 (Medium-Term) - Enhanced Productivity

**Target**: Full-featured productivity suite

**Features**:
- **Launcher Capabilities**
  - Application launcher with fuzzy search
  - File/folder quick open
  - Web search integration
  - Command palette for power users

- **Productivity Tools**
  - Pomodoro timer with notifications
  - Simple task list with checkboxes
  - Quick capture for ideas/notes
  - Screenshot capture and annotation

- **Enhanced UI**
  - Multiple view modes (compact, expanded, dashboard)
  - Customizable layout and widgets
  - Theme system with color customization
  - Transition animations and smooth UX

**Technical Considerations**:
- Plugin architecture for extensibility
- Search indexing for fast application/file lookup
- Database for task/note persistence (SQLite consideration)

### 4.5 Version 3.0+ (Long-Term) - Advanced Features

**Target**: Professional-grade productivity platform

**Potential Features**:
- Multi-window support for simultaneous views
- Cloud sync for settings and content (OneDrive integration)
- Plugin/extension marketplace
- Automation and scripting support
- Calendar and schedule integration
- Team collaboration features
- Advanced theming and customization engine

**Exploration Areas**:
- AI-powered search and suggestions
- Integration with Microsoft 365 services
- Voice command support
- Mobile companion app (Windows Phone/Android)

### 4.6 Technical Debt & Improvements

**Items to Address**:
- Resolve single-instance Mutex deadlock issue on x64
- Optimize x64 rendering for TrayIconView (remove Z-index workaround if possible)
- Implement proper error handling and logging system
- Add unit tests for core functionality
- Create installer (MSI or MSIX packaging)
- Improve startup performance
- Add analytics/telemetry (privacy-respecting)

---

## 5. Development Guide

### 5.1 Quick Start

**Prerequisites**:
- .NET 10.0 SDK
- Windows 10 SDK (10.0.19041.0 or later)
- Visual Studio 2022 or JetBrains Rider (optional but recommended)

**Build Commands**:
```bash
# Build the project
dotnet build

# Run the application
dotnet run

# Build for release
dotnet build -c Release

# Clean build artifacts
dotnet clean

# Restore dependencies
dotnet restore
```

### 5.2 Project Structure

```
McstudDesktop/
├── Program.cs              # Application entry point
├── App.cs                  # Application lifecycle & state management
├── MainWindow.cs           # Main UI window implementation
├── TrayIconView.cs         # System tray icon & context menu
├── GlobalUsings.cs         # Global namespace imports
├── App.xaml                # Minimal XAML resources
├── McstudDesktop.csproj    # .NET project configuration
├── Assets/
│   └── appicon.ico        # Application icon
├── SPEC.md                # This file - product specification
└── CLAUDE.md              # Technical implementation guide
```

### 5.3 Key Files

| File | Purpose |
|------|---------|
| **Program.cs** | Contains `[STAThread] Main()` method, initializes WinUI 3 |
| **App.cs** | Application lifecycle, single-instance logic, static accessors |
| **MainWindow.cs** | Main window UI, dark mode styling, button interactions |
| **TrayIconView.cs** | System tray integration using H.NotifyIcon with MVVM |
| **GlobalUsings.cs** | Common namespace imports (WinUI, NotifyIcon, MVVM) |

### 5.4 Documentation

- **SPEC.md** (this file): Product specification, features, and roadmap
- **CLAUDE.md**: Detailed technical implementation guide, architecture patterns, and platform-specific considerations

For deep technical details, initialization sequences, and implementation patterns, refer to **CLAUDE.md**.

### 5.5 Contributing

**Areas Open for Contribution**:
- Implementing roadmap features (v1.1+)
- UI/UX enhancements and design improvements
- Bug fixes and performance optimizations
- Documentation improvements
- Testing and quality assurance

**Development Workflow**:
1. Review SPEC.md for feature roadmap
2. Check CLAUDE.md for technical implementation details
3. Build and run the application locally
4. Make changes in a feature branch
5. Test on both x64 and ARM64 if possible
6. Submit changes with clear descriptions

**Code Style**:
- Follow existing patterns (MVVM with source generators)
- Use meaningful variable and method names
- Add comments for complex logic
- Maintain dark mode aesthetic for UI additions

---

## 6. Version History

| Version | Release Date | Highlights |
|---------|--------------|------------|
| **1.0** | 2025-01-25 | Initial release: System tray integration, dark mode UI, window management |

---

## 7. References

- **Technical Documentation**: See CLAUDE.md for detailed implementation guidance
- **WinUI 3 Documentation**: [Microsoft Learn - WinUI 3](https://learn.microsoft.com/windows/apps/winui/winui3/)
- **CommunityToolkit.Mvvm**: [MVVM Toolkit Documentation](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- **H.NotifyIcon**: [GitHub - H.NotifyIcon.WinUI](https://github.com/HavenDV/H.NotifyIcon)

---

**Document Version**: 1.0
**Last Updated**: January 2025
**Maintained By**: McstudDesktop Development Team
