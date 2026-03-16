#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using McStudDesktop.Services;
using McstudDesktop.Services;
using Velopack;
using Velopack.Sources;

namespace McStudDesktop.Views
{
    /// <summary>
    /// McStud Tool - Ultra-fast clipboard export to CCC/Mitchell
    ///
    /// OPTIMIZED FLOW:
    /// 1. Copy operations from Excel (Ctrl+C) - tool auto-detects
    /// 2. Click CCC or Mitchell button - done!
    ///
    /// Auto-monitoring clipboard means no "Read" step needed.
    /// </summary>
    public sealed class McStudToolView : UserControl
    {
        // Win32 API for window activation
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // For forcing window activation (bypass Windows restrictions)
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;
        private const byte VK_MENU = 0x12; // ALT key
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // Main status display
        private Border? _statusBorder;
        private TextBlock? _statusIcon;
        private TextBlock? _statusText;
        private TextBlock? _countText;

        // Export buttons
        private Button? _cccButton;
        private Button? _cccWebButton;
        private Button? _mitchellButton;

        // Clipboard Summary (merged preview + totals)
        private Expander? _clipboardSummaryExpander;
        private TextBlock? _clipboardSummaryHeaderText;
        private ListView? _operationsList;
        private TextBlock? _clipboardCountText;
        private TextBlock? _clipboardPriceText;
        private TextBlock? _clipboardLaborText;
        private TextBlock? _clipboardRefinishText;

        // Settings panel
        private ComboBox? _speedCombo;

        // Services
        private ClipboardExportService _exportService;
        private ExportStatisticsService _statsService;
        private GlobalHotkeyService _hotkeyService;
        private ClipboardPasteService _clipboardPasteService;
        private CCCAutomationService _cccAutomationService;
        private SmartPasteService _smartPasteService;
        private CCCInsertService _cccInsertService;
        private SmartInsertService _smartInsertService;
        private VirtualClipboardService _virtualClipboard;
        private TypeItService? _typeItService;
        private AutoHotkeyPasteService? _pasteService;
        private Button? _resumeButton;
        private DispatcherTimer? _clipboardTimer;
        private string _lastClipboardHash = "";

        // Track the window user was in BEFORE McStud got focus
        private IntPtr _previousActiveWindow = IntPtr.Zero;
        private IntPtr _lastKnownForeground = IntPtr.Zero;
        private DispatcherTimer? _windowTracker;

        // Estimate Upload feature (now in Import tab)
        private EstimateUploadView? _estimateUploadView;

        // Chatbot feature (now in Chat tab)
        private ChatbotView? _chatbotView;

        // Tab navigation
        private Grid? _tabHeader;
        private Border? _exportTabButton;
        private Border? _chatTabButton;
        private Border? _importTabButton;
        private Border? _statsTabButton;
        private Border? _referenceTabButton;
        private Border? _settingsTabButton;
        private Border? _guideTabButton;
        private Border? _shopDocsTabButton;
        private Grid? _tabContent;
        private int _selectedTabIndex = 0;

        // MET Guide feature
        private METGuideView? _metGuideView;

        // Shop Docs feature
        private ShopDocsView? _shopDocsView;


        // Statistics feature
        private StatisticsView? _statisticsView;

        // Reference feature (Definitions, DEG, P-Pages, Procedures combined)
        private ReferenceView? _referenceView;

        // PDF queue badge on Reference tab
        private Border? _pdfQueueBadge;
        private TextBlock? _pdfQueueBadgeText;

        // Update feature
        private UpdateService? _updateService;
        private Border? _updateBanner;
        private UpdateInfo? _pendingUpdateInfo;

        public McStudToolView()
        {
            _exportService = new ClipboardExportService();
            _statsService = new ExportStatisticsService();
            _hotkeyService = new GlobalHotkeyService();
            _clipboardPasteService = new ClipboardPasteService();
            _cccAutomationService = new CCCAutomationService();
            _smartPasteService = new SmartPasteService();
            _cccInsertService = new CCCInsertService();
            _smartInsertService = new SmartInsertService();
            _updateService = new UpdateService();
            _virtualClipboard = VirtualClipboardService.Instance;

            // Subscribe to virtual clipboard changes
            _virtualClipboard.OperationsChanged += (s, e) => DispatcherQueue?.TryEnqueue(() => UpdateClipboardSummary());

            // Subscribe to smart paste events
            _smartPasteService.StatusChanged += (s, msg) => DispatcherQueue?.TryEnqueue(() => UpdateStatusMessage(msg));
            _smartPasteService.ProgressChanged += (s, args) => DispatcherQueue?.TryEnqueue(() => UpdateStatusProgress(args.Current, args.Total));
            _smartPasteService.PasteCompleted += (s, success) => DispatcherQueue?.TryEnqueue(() => OnSmartPasteCompleted(success));

            // Subscribe to CCC insert events
            _cccInsertService.StatusChanged += (s, msg) => DispatcherQueue?.TryEnqueue(() => UpdateStatusMessage(msg));
            _cccInsertService.ProgressChanged += (s, args) => DispatcherQueue?.TryEnqueue(() => UpdateStatusProgress(args.Current, args.Total));
            _cccInsertService.InsertCompleted += (s, success) => DispatcherQueue?.TryEnqueue(() => OnCCCInsertCompleted(success));

            // Subscribe to Smart Insert events (uses UI Automation to find menu items)
            _smartInsertService.StatusChanged += (s, msg) => DispatcherQueue?.TryEnqueue(() => UpdateStatusMessage(msg));
            _smartInsertService.ProgressChanged += (s, args) => DispatcherQueue?.TryEnqueue(() => UpdateStatusProgress(args.Current, args.Total));
            _smartInsertService.InsertCompleted += (s, success) => DispatcherQueue?.TryEnqueue(() => OnCCCInsertCompleted(success));

            _hotkeyService.OnExportHotkeyPressed += OnHotkeyExport;
            _hotkeyService.Start();

            BuildCompactUI();
            StartClipboardMonitoring();
            StartWindowTracking();
            CheckForUpdatesAsync();

            this.Loaded += OnFirstLoaded;
        }

        private async void OnFirstLoaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= OnFirstLoaded;

            // Small delay to let UI finish rendering
            await Task.Delay(500);

            try
            {
                var onboarding = Services.OnboardingStateService.Instance;

                if (onboarding.IsFirstLaunch)
                {
                    var result = await WelcomeDialog.ShowAsync(this.XamlRoot);
                    if (result == ContentDialogResult.Primary)
                    {
                        StartSpotlightTour();
                    }
                    onboarding.MarkFirstLaunchComplete();
                    onboarding.MarkVersionSeen();
                }
                else if (onboarding.HasNewVersionSinceLastSeen())
                {
                    await WhatsNewDialog.ShowAsync(this.XamlRoot, onboarding.GetLastSeenVersion());
                    onboarding.MarkVersionSeen();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Onboarding] Error: {ex.Message}");
            }
        }

        public void StartSpotlightTour()
        {
            var steps = new System.Collections.Generic.List<TourStep>
            {
                new TourStep { Target = _exportTabButton!, Icon = "\uE8C8", Title = "Export", Description = "Your virtual clipboard. Copy lines from Excel and McStud reads them automatically. Hit the CCC or Mitchell button and it types everything into the estimating platform for you.\n\nTip: Look for the ? button in the top-right corner for detailed help on any tab.", TabIndex = 0 },
                new TourStep { Target = _chatTabButton!, Icon = "\uE8BD", Title = "Chat", Description = "Ask the AI assistant any estimating questions. Get help with procedures, guidelines, and best practices.", TabIndex = 1 },
                new TourStep { Target = _importTabButton!, Icon = "\uE8E5", Title = "Import", Description = "Upload PDF estimates for analysis. The tool will parse operations, identify references, and help you review the estimate. Hit the ? for more details.", TabIndex = 2 },
                new TourStep { Target = _statsTabButton!, Icon = "\uE9D9", Title = "Stats", Description = "Track your export statistics — see how many operations you've exported, which platforms you use most, and your activity over time. Hit the ? for more details.", TabIndex = 3 },
                new TourStep { Target = _referenceTabButton!, Icon = "\uE82D", Title = "Reference", Description = "Access Definitions, DEG Inquiries, P-Pages, and Procedures all in one place. Search and queue references for PDF export. Hit the ? for more details.", TabIndex = 4 },
                new TourStep { Target = _settingsTabButton!, Icon = "\uE713", Title = "Settings", Description = "Manage app settings, check for updates, view version info, and configure text-to-speech voice.", TabIndex = 5 },
                new TourStep { Target = _guideTabButton!, Icon = "\uE82D", Title = "Guide", Description = "Step-by-step MET guides to help you through common estimating tasks and procedures. Hit the ? for more details.", TabIndex = 6 },
                new TourStep { Target = _shopDocsTabButton!, Icon = "\uE8A5", Title = "Shop Docs", Description = "Access checklists, invoices, and shop documents. Create custom checklists and export them to PDF. Hit the ? for more details.", TabIndex = 7 }
            };

            var overlay = new SpotlightTourOverlay(steps, SelectTab);
            overlay.TourCompleted += (s, e) => System.Diagnostics.Debug.WriteLine("[Tour] Completed");
            overlay.TourSkipped += (s, e) => System.Diagnostics.Debug.WriteLine("[Tour] Skipped");

            var rootGrid = McstudDesktop.App.MainWindow?.Content as Grid;
            if (rootGrid != null)
            {
                rootGrid.Children.Add(overlay);
            }
        }

        private async void CheckForUpdatesAsync()
        {
            try
            {
                var source = new GithubSource("https://github.com/mcstud361/McstudReleases", null, false);
                var mgr = new UpdateManager(source);

                if (!mgr.IsInstalled)
                {
                    Debug.WriteLine("[Update] Not installed via Velopack - skipping startup update check");
                    return;
                }

                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion != null && _updateBanner != null)
                {
                    _pendingUpdateInfo = newVersion;
                    ShowUpdateBanner(newVersion.TargetFullRelease.Version.ToString(), "");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] Check failed: {ex.Message}");
            }
        }

        private void ShowUpdateBanner(string version, string notes)
        {
            if (_updateBanner == null) return;

            DispatcherQueue.TryEnqueue(() =>
            {
                _updateBanner.Visibility = Visibility.Visible;
                if (_updateBanner.Child is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is TextBlock tb && tb.Name == "UpdateText")
                        {
                            tb.Text = $"Update {version} available!";
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Called when Ctrl+Alt+V is pressed anywhere - export to current window
        /// Uses SMART paste that targets CCC ONE directly (won't mess up if you click elsewhere!)
        /// </summary>
        private void OnHotkeyExport()
        {
            // Run on UI thread
            DispatcherQueue.TryEnqueue(async () =>
            {
                // First try: use parsed operations from export service (if available)
                if (_exportService.GetParsedOperations().Count > 0)
                {
                    await StartHotkeyExport();
                    return;
                }

                // Second try: SMART clipboard paste using UI Automation
                // This targets CCC ONE directly - won't break if you click elsewhere!
                System.Diagnostics.Debug.WriteLine("[Hotkey] Using smart CCC automation paste...");
                await _cccAutomationService.SmartPasteAsync();
            });
        }

        private void BuildCompactUI()
        {
            // Main container
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18)),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(0)
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Update banner
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Tab header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Tab content

            // === UPDATE BANNER (hidden by default) ===
            _updateBanner = CreateUpdateBanner();
            Grid.SetRow(_updateBanner, 0);
            mainGrid.Children.Add(_updateBanner);

            // === TAB HEADER ===
            _tabHeader = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                Padding = new Thickness(8, 8, 8, 0)
            };
            // 9 tabs
            for (int i = 0; i < 10; i++)
            {
                _tabHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            _exportTabButton = CreateTabButton("Export", "\uE8C8", 0);
            _chatTabButton = CreateTabButton("Chat", "\uE8BD", 1);
            _importTabButton = CreateTabButton("Import", "\uE8E5", 2);
            _statsTabButton = CreateTabButton("Stats", "\uE9D9", 3);
            _referenceTabButton = CreateTabButton("Reference", "\uE82D", 4);
            AttachPdfQueueBadge(_referenceTabButton);
            _settingsTabButton = CreateTabButton("Settings", "\uE713", 5); // Gear icon
            _guideTabButton = CreateTabButton("Guide", "\uE82D", 6); // Book icon
            _shopDocsTabButton = CreateTabButton("Shop Docs", "\uE8A5", 7); // Document icon

            Grid.SetColumn(_exportTabButton, 0);
            Grid.SetColumn(_chatTabButton, 1);
            Grid.SetColumn(_importTabButton, 2);
            Grid.SetColumn(_statsTabButton, 3);
            Grid.SetColumn(_referenceTabButton, 4);
            Grid.SetColumn(_settingsTabButton, 5);
            Grid.SetColumn(_guideTabButton, 6);
            Grid.SetColumn(_shopDocsTabButton, 7);

            _tabHeader.Children.Add(_exportTabButton);
            _tabHeader.Children.Add(_chatTabButton);
            _tabHeader.Children.Add(_importTabButton);
            _tabHeader.Children.Add(_statsTabButton);
            _tabHeader.Children.Add(_referenceTabButton);
            _tabHeader.Children.Add(_settingsTabButton);
            _tabHeader.Children.Add(_guideTabButton);
            _tabHeader.Children.Add(_shopDocsTabButton);

            // Window snap buttons (half / quarter / compact)
            var snapStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center
            };

            var halfBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE740", FontSize = 10 },
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(4, 2, 4, 2),
                MinWidth = 24, MinHeight = 24
            };
            ToolTipService.SetToolTip(halfBtn, "Half Screen");
            halfBtn.Click += (s, e) => (McstudDesktop.App.MainWindow as McstudDesktop.MainWindow)?.SnapHalfScreen();
            snapStack.Children.Add(halfBtn);

            var quarterBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE744", FontSize = 10 },
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(4, 2, 4, 2),
                MinWidth = 24, MinHeight = 24
            };
            ToolTipService.SetToolTip(quarterBtn, "Quarter Screen");
            quarterBtn.Click += (s, e) => (McstudDesktop.App.MainWindow as McstudDesktop.MainWindow)?.SnapQuarterScreen();
            snapStack.Children.Add(quarterBtn);

            var compactBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE73F", FontSize = 10 },
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(4, 2, 4, 2),
                MinWidth = 24, MinHeight = 24
            };
            ToolTipService.SetToolTip(compactBtn, "Compact (Default)");
            compactBtn.Click += (s, e) => (McstudDesktop.App.MainWindow as McstudDesktop.MainWindow)?.SnapCompact();
            snapStack.Children.Add(compactBtn);

            Grid.SetColumn(snapStack, 9);
            _tabHeader.Children.Add(snapStack);

            Grid.SetRow(_tabHeader, 1);
            mainGrid.Children.Add(_tabHeader);

            // === TAB CONTENT AREA ===
            _tabContent = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18)),
                Padding = new Thickness(12)
            };
            Grid.SetRow(_tabContent, 2);
            mainGrid.Children.Add(_tabContent);

            // Build all tab contents
            BuildExportTab();
            BuildChatTab();
            BuildImportTab();
            BuildStatsTab();
            BuildReferenceTab();
            BuildSettingsTab();
            BuildGuideTab();
            BuildShopDocsTab();

            // Show first tab by default
            SelectTab(0);

            mainBorder.Child = mainGrid;
            Content = mainBorder;
        }

        private Border CreateUpdateBanner()
        {
            var banner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
                Padding = new Thickness(10, 6, 10, 6),
                Visibility = Visibility.Collapsed
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBlock = new TextBlock
            {
                Name = "UpdateText",
                Text = "Update available!",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(textBlock, 0);

            var updateButton = new Button
            {
                Content = "Update Now",
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 150, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(8, 0, 0, 0)
            };
            updateButton.Click += async (s, e) =>
            {
                if (_pendingUpdateInfo == null) return;
                updateButton.IsEnabled = false;
                updateButton.Content = "Downloading...";
                try
                {
                    var source = new GithubSource("https://github.com/mcstud361/McstudReleases", null, false);
                    var mgr = new UpdateManager(source);
                    await mgr.DownloadUpdatesAsync(_pendingUpdateInfo);
                    McstudDesktop.App.ReleaseMutex();
                    mgr.ApplyUpdatesAndRestart(_pendingUpdateInfo);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Update] Banner install failed: {ex.Message}");
                    updateButton.Content = "Update Failed";
                    updateButton.IsEnabled = true;
                }
            };
            Grid.SetColumn(updateButton, 1);

            var dismissButton = new Button
            {
                Content = "\uE711", // X icon
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(4, 0, 0, 0)
            };
            dismissButton.Click += (s, e) => banner.Visibility = Visibility.Collapsed;
            Grid.SetColumn(dismissButton, 2);

            grid.Children.Add(textBlock);
            grid.Children.Add(updateButton);
            grid.Children.Add(dismissButton);

            banner.Child = grid;
            return banner;
        }

        private Border CreateTabButton(string text, string glyph, int tabIndex)
        {
            bool isSelected = tabIndex == _selectedTabIndex;

            var border = new Border
            {
                Background = new SolidColorBrush(isSelected
                    ? Color.FromArgb(255, 18, 18, 18)
                    : Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                Padding = new Thickness(8, 8, 8, 8),
                Margin = new Thickness(2, 0, 2, 0),
                BorderBrush = new SolidColorBrush(isSelected
                    ? Color.FromArgb(255, 0, 120, 215)
                    : Color.FromArgb(255, 50, 50, 50)),
                BorderThickness = new Thickness(1, 1, 1, 0)
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6
            };

            var icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
                Foreground = new SolidColorBrush(isSelected
                    ? Color.FromArgb(255, 0, 150, 255)
                    : Color.FromArgb(255, 120, 120, 120))
            };

            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = isSelected
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = new SolidColorBrush(isSelected
                    ? Colors.White
                    : Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };

            stack.Children.Add(icon);
            stack.Children.Add(label);
            border.Child = stack;

            // Handle click
            border.PointerPressed += (s, e) => SelectTab(tabIndex);
            border.PointerEntered += (s, e) =>
            {
                if (tabIndex != _selectedTabIndex)
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
                }
            };
            border.PointerExited += (s, e) =>
            {
                if (tabIndex != _selectedTabIndex)
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35));
                }
            };

            return border;
        }

        private void AttachPdfQueueBadge(Border? tabButton)
        {
            if (tabButton == null) return;

            // Replace the border's child with a Grid that overlays the badge
            var existingChild = tabButton.Child;
            tabButton.Child = null;

            var overlayGrid = new Grid();
            overlayGrid.Children.Add(existingChild!);

            _pdfQueueBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 200, 60, 60)),
                CornerRadius = new CornerRadius(8),
                MinWidth = 16,
                Height = 16,
                Padding = new Thickness(4, 0, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -4, 0),
                Visibility = Visibility.Collapsed
            };
            _pdfQueueBadgeText = new TextBlock
            {
                Text = "0",
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _pdfQueueBadge.Child = _pdfQueueBadgeText;
            overlayGrid.Children.Add(_pdfQueueBadge);

            tabButton.Child = overlayGrid;

            // Subscribe to queue changes (add and clear/remove)
            DefinitionsView.OnPdfQueueChanged += (s, e) =>
            {
                DispatcherQueue?.TryEnqueue(UpdatePdfQueueBadge);
            };
        }

        private void UpdatePdfQueueBadge()
        {
            if (_pdfQueueBadge == null || _pdfQueueBadgeText == null) return;

            var count = DefinitionsView.PdfQueue.Count;
            if (count > 0)
            {
                _pdfQueueBadgeText.Text = count.ToString();
                _pdfQueueBadge.Visibility = Visibility.Visible;
            }
            else
            {
                _pdfQueueBadge.Visibility = Visibility.Collapsed;
            }
        }

        private async void OpenSupportEmail(string name, string subject, string message)
        {
            var version = UpdateService.GetVersionString();
            var emailSubject = string.IsNullOrEmpty(subject) ? "McStud Support Request" : subject;
            var bodyParts = new List<string>();
            if (!string.IsNullOrEmpty(message)) bodyParts.Add(message);
            bodyParts.Add("\n---");
            if (!string.IsNullOrEmpty(name)) bodyParts.Add($"From: {name}");
            bodyParts.Add($"App Version: {version}");
            var body = string.Join("\n", bodyParts);

            var encodedSubject = Uri.EscapeDataString(emailSubject);
            var encodedBody = Uri.EscapeDataString(body);

            // Show picker dialog
            var dialog = new ContentDialog
            {
                Title = "Open with...",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.None
            };

            var options = new StackPanel { Spacing = 6 };

            void AddOption(string label, string icon, Action action)
            {
                var btn = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(16, 12, 16, 12),
                    Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55)),
                    CornerRadius = new CornerRadius(6)
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                row.Children.Add(new FontIcon { Glyph = icon, FontSize = 18, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)) });
                row.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Colors.White) });
                btn.Content = row;
                btn.Click += (s, e) => { dialog.Hide(); action(); };
                options.Children.Add(btn);
            }

            AddOption("Gmail (opens in browser)", "\uE774", () =>
            {
                var url = $"https://mail.google.com/mail/?view=cm&to=Mcstudestimating@gmail.com&su={encodedSubject}&body={encodedBody}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            });

            AddOption("Outlook (opens in browser)", "\uE724", () =>
            {
                var url = $"https://outlook.live.com/mail/0/deeplink/compose?to=Mcstudestimating@gmail.com&subject={encodedSubject}&body={encodedBody}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            });

            AddOption("Yahoo Mail (opens in browser)", "\uE774", () =>
            {
                var url = $"https://compose.mail.yahoo.com/?to=Mcstudestimating@gmail.com&subject={encodedSubject}&body={encodedBody}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            });

            AddOption("Default Email App (Outlook desktop, etc.)", "\uE8A7", () =>
            {
                var mailto = $"mailto:Mcstudestimating@gmail.com?subject={encodedSubject}&body={encodedBody}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = mailto, UseShellExecute = true });
            });

            dialog.Content = options;

            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Support] Error: {ex.Message}");
            }
        }

        private async void ShowAboutDialog()
        {
            var dialog = new ContentDialog
            {
                Title = "About McStud Tool",
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var stack = new StackPanel { Spacing = 8 };

            stack.Children.Add(new TextBlock
            {
                Text = $"McStud Tool v{UpdateService.GetVersionString()}",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Clipboard export tool for collision estimating",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });

            stack.Children.Add(new Border { Height = 16 });

            stack.Children.Add(new TextBlock
            {
                Text = "Support: Mcstudestimating@gmail.com",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 150, 255)),
                IsTextSelectionEnabled = true
            });

            stack.Children.Add(new TextBlock
            {
                Text = $"\n© {DateTime.Now.Year} McStud Estimating",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            });

            dialog.Content = stack;
            await dialog.ShowAsync();
        }

        private async Task ShowUpdateCheckDialog()
        {
            // Show "checking" dialog
            var checkingDialog = new ContentDialog
            {
                Title = "Checking for Updates",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var checkingStack = new StackPanel { Spacing = 12 };
            var progressRing = new ProgressRing { IsActive = true, Width = 40, Height = 40 };
            var statusText = new TextBlock
            {
                Text = "Checking for updates...",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            checkingStack.Children.Add(progressRing);
            checkingStack.Children.Add(statusText);
            checkingDialog.Content = checkingStack;

            // Show checking dialog (non-blocking)
            _ = checkingDialog.ShowAsync();

            try
            {
                // Use Velopack to check for updates
                var source = new GithubSource("https://github.com/mcstud361/McstudReleases", null, false);
                var mgr = new UpdateManager(source);

                // Check if running from Velopack install
                if (!mgr.IsInstalled)
                {
                    checkingDialog.Hide();
                    var devDialog = new ContentDialog
                    {
                        Title = "Update Check",
                        Content = "Updates are only available when running the installed version.\n\nTo get automatic updates, install using the Setup.exe from GitHub releases.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await devDialog.ShowAsync();
                    return;
                }

                var currentVersion = mgr.CurrentVersion?.ToString() ?? "1.0.0";
                Debug.WriteLine($"[Update] Velopack installed version: {currentVersion}, AppId: {mgr.AppId}");
                var newVersion = await mgr.CheckForUpdatesAsync();
                Debug.WriteLine($"[Update] CheckForUpdatesAsync result: {(newVersion != null ? $"v{newVersion.TargetFullRelease.Version}" : "null (up to date)")}");
                checkingDialog.Hide();

                // Show result dialog
                var resultDialog = new ContentDialog
                {
                    Title = "Update Check",
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var resultStack = new StackPanel { Spacing = 12 };

                // Current version
                resultStack.Children.Add(new TextBlock
                {
                    Text = $"Current Version: v{currentVersion}",
                    FontSize = 14
                });

                if (newVersion != null)
                {
                    // Update available
                    resultStack.Children.Add(new TextBlock
                    {
                        Text = $"New Version Available: v{newVersion.TargetFullRelease.Version}",
                        FontSize = 16,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
                    });

                    resultDialog.PrimaryButtonText = "Install Update";
                    resultDialog.PrimaryButtonClick += async (s, e) =>
                    {
                        e.Cancel = true; // Prevent dialog from closing
                        await InstallUpdateWithProgress(mgr, newVersion);
                    };
                }
                else
                {
                    // Up to date
                    resultStack.Children.Add(new TextBlock
                    {
                        Text = "You're up to date!",
                        FontSize = 16,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
                    });

                    resultStack.Children.Add(new TextBlock
                    {
                        Text = "You have the latest version of McStud Tool.",
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
                    });
                }

                resultDialog.Content = resultStack;
                await resultDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                checkingDialog.Hide();
                Debug.WriteLine($"[Update] Error checking for updates: {ex.Message}");

                var errorDialog = new ContentDialog
                {
                    Title = "Update Check Failed",
                    Content = $"Could not check for updates.\n\nError: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private async Task InstallUpdateWithProgress(UpdateManager mgr, UpdateInfo updateInfo)
        {
            var installDialog = new ContentDialog
            {
                Title = "Installing Update",
                XamlRoot = this.XamlRoot
            };

            var installStack = new StackPanel { Spacing = 12 };
            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Width = 300
            };
            var statusText = new TextBlock
            {
                Text = "Downloading update...",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            installStack.Children.Add(progressBar);
            installStack.Children.Add(statusText);
            installDialog.Content = installStack;

            _ = installDialog.ShowAsync();

            try
            {
                // Download with progress
                await mgr.DownloadUpdatesAsync(updateInfo, progress =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        progressBar.Value = progress;
                        statusText.Text = $"Downloading... {progress}%";
                    });
                });

                statusText.Text = "Installing update...";

                // Release mutex so the restarted instance doesn't exit
                McstudDesktop.App.ReleaseMutex();
                mgr.ApplyUpdatesAndRestart(updateInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] Install failed: {ex.Message}");
                installDialog.CloseButtonText = "Close";
                statusText.Text = $"Update failed: {ex.Message}";
            }
        }

        internal void SelectTab(int tabIndex)
        {
            _selectedTabIndex = tabIndex;

            // Update tab button styles
            UpdateTabButtonStyle(_exportTabButton, 0);
            UpdateTabButtonStyle(_chatTabButton, 1);
            UpdateTabButtonStyle(_importTabButton, 2);
            UpdateTabButtonStyle(_statsTabButton, 3);
            UpdateTabButtonStyle(_referenceTabButton, 4);
            UpdateTabButtonStyle(_settingsTabButton, 5);
            UpdateTabButtonStyle(_guideTabButton, 6);
            UpdateTabButtonStyle(_shopDocsTabButton, 7);

            // Refresh stats view when selected
            if (tabIndex == 3 && _statisticsView != null)
            {
                _statisticsView.Refresh();
            }

            // Show selected content
            if (_tabContent == null) return;

            foreach (var child in _tabContent.Children)
            {
                if (child is FrameworkElement element)
                {
                    int index = (int)(element.Tag ?? -1);
                    element.Visibility = index == tabIndex ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void UpdateTabButtonStyle(Border? button, int tabIndex)
        {
            if (button == null) return;

            bool isSelected = tabIndex == _selectedTabIndex;

            button.Background = new SolidColorBrush(isSelected
                ? Color.FromArgb(255, 18, 18, 18)
                : Color.FromArgb(255, 35, 35, 35));
            button.BorderBrush = new SolidColorBrush(isSelected
                ? Color.FromArgb(255, 0, 120, 215)
                : Color.FromArgb(255, 50, 50, 50));

            if (button.Child is StackPanel stack)
            {
                foreach (var child in stack.Children)
                {
                    if (child is FontIcon icon)
                    {
                        icon.Foreground = new SolidColorBrush(isSelected
                            ? Color.FromArgb(255, 0, 150, 255)
                            : Color.FromArgb(255, 120, 120, 120));
                    }
                    else if (child is TextBlock label)
                    {
                        label.FontWeight = isSelected
                            ? Microsoft.UI.Text.FontWeights.SemiBold
                            : Microsoft.UI.Text.FontWeights.Normal;
                        label.Foreground = new SolidColorBrush(isSelected
                            ? Colors.White
                            : Color.FromArgb(255, 150, 150, 150));
                    }
                }
            }
        }

        private void BuildExportTab()
        {
            var exportContent = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Tag = 0
            };

            var mainStack = new StackPanel { Spacing = 10 };

            // === HEADER ROW ===
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 28)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Excel Export",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);

            // Speed selector
            _speedCombo = new ComboBox
            {
                Width = 85,
                SelectedIndex = 1,
                FontSize = 12,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45))
            };
            _speedCombo.Items.Add("Slow (50ms)");
            _speedCombo.Items.Add("Normal (25ms)");
            _speedCombo.Items.Add("Fast (15ms)");
            _speedCombo.Items.Add("Turbo (8ms)");
            _speedCombo.SelectedIndex = 2; // Default to Fast (15ms) - what we were running
            _speedCombo.SelectionChanged += SpeedCombo_SelectionChanged;
            Grid.SetColumn(_speedCombo, 1);

            // Help button
            var exportHelpButton = ContextualHelpButton.Create("export-tab");
            exportHelpButton.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(exportHelpButton, 2);

            headerGrid.Children.Add(title);
            headerGrid.Children.Add(_speedCombo);
            headerGrid.Children.Add(exportHelpButton);
            headerBorder.Child = headerGrid;
            mainStack.Children.Add(headerBorder);

            // === STATUS INDICATOR ===
            _statusBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 55, 55)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var statusStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _statusIcon = new TextBlock
            {
                Text = "...",
                FontSize = 22,
                VerticalAlignment = VerticalAlignment.Center
            };

            var statusTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };

            _statusText = new TextBlock
            {
                Text = "Waiting for Excel data...",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170))
            };

            _countText = new TextBlock
            {
                Text = "Copy operations with Ctrl+C",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 110, 110, 110))
            };

            statusTextStack.Children.Add(_statusText);
            statusTextStack.Children.Add(_countText);
            statusStack.Children.Add(_statusIcon);
            statusStack.Children.Add(statusTextStack);
            _statusBorder.Child = statusStack;
            mainStack.Children.Add(_statusBorder);

            // === EXPORT BUTTONS ===
            var buttonStack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 0) };

            _cccButton = CreateExportButton("CCC Desktop", "\uE8C8", Color.FromArgb(255, 0, 120, 215));
            _cccButton.Click += CCCDesktopButton_Click;
            _cccButton.IsEnabled = false;
            ToolTipService.SetToolTip(_cccButton, "1. Copy from Excel  2. Select the A column in CCC where you want to paste  3. Click this button\nInput blocked during paste - you can't mess it up!");

            _cccWebButton = CreateExportButton("CCC Web", "\uE774", Color.FromArgb(255, 0, 160, 200), comingSoon: true);
            _cccWebButton.Click += CCCWebButton_Click;
            _cccWebButton.IsEnabled = false;

            _mitchellButton = CreateExportButton("Mitchell", "\uE8C8", Color.FromArgb(255, 130, 80, 180), comingSoon: true);
            _mitchellButton.Click += MitchellButton_Click;
            _mitchellButton.IsEnabled = false;

            // Resume button (hidden until interrupted)
            _resumeButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE768", FontSize = 14, Margin = new Thickness(0, 0, 6, 0) },
                        new TextBlock { Text = "Resume", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 180, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Visibility = Visibility.Collapsed
            };
            _resumeButton.Click += ResumeButton_Click;

            buttonStack.Children.Add(_cccButton);
            buttonStack.Children.Add(_cccWebButton);
            buttonStack.Children.Add(_mitchellButton);
            buttonStack.Children.Add(_resumeButton);
            mainStack.Children.Add(buttonStack);

            // === CLIPBOARD SUMMARY (merged preview + totals) ===
            _clipboardSummaryExpander = new Expander
            {
                Header = CreateClipboardSummaryHeader(),
                IsExpanded = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 0)
            };
            _clipboardSummaryExpander.Content = CreateClipboardSummaryPanel();
            mainStack.Children.Add(_clipboardSummaryExpander);

            // === HINT ===
            var hint = new TextBlock
            {
                Text = "1. Copy from Excel  2. Select the A column in CCC where you want to paste  3. Click button above",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            mainStack.Children.Add(hint);

            exportContent.Content = mainStack;
            _tabContent?.Children.Add(exportContent);
        }

        private void BuildChatTab()
        {
            var chatContent = new Grid
            {
                Tag = 1,
                Visibility = Visibility.Collapsed
            };

            // Create header
            var mainStack = new StackPanel { Spacing = 10 };

            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 28)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var headerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            var icon = new FontIcon
            {
                Glyph = "\uE8BD",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255))
            };

            var title = new TextBlock
            {
                Text = "Help Assistant",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var subtitle = new TextBlock
            {
                Text = "Ask about operations",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            headerStack.Children.Add(icon);
            headerStack.Children.Add(title);
            headerStack.Children.Add(subtitle);
            headerBorder.Child = headerStack;

            chatContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chatContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(headerBorder, 0);
            chatContent.Children.Add(headerBorder);

            // Chat view takes remaining space
            _chatbotView = new ChatbotView();
            _chatbotView.OnNavigateToExport += (s, e) => SelectTab(0); // Navigate to Export tab
            Grid.SetRow(_chatbotView, 1);
            chatContent.Children.Add(_chatbotView);

            _tabContent?.Children.Add(chatContent);
        }

        private void BuildImportTab()
        {
            var importContent = new Grid
            {
                Tag = 2,
                Visibility = Visibility.Collapsed
            };

            // Create header with help button
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 28)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            var icon = new FontIcon
            {
                Glyph = "\uE8E5",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 180, 100))
            };

            var title = new TextBlock
            {
                Text = "Estimate Upload",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 150)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = "NEW",
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };

            headerStack.Children.Add(icon);
            headerStack.Children.Add(title);
            headerStack.Children.Add(badge);
            Grid.SetColumn(headerStack, 0);

            // Help button
            var importHelpButton = ContextualHelpButton.Create("import-tab");
            Grid.SetColumn(importHelpButton, 1);

            headerGrid.Children.Add(headerStack);
            headerGrid.Children.Add(importHelpButton);
            headerBorder.Child = headerGrid;

            importContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            importContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(headerBorder, 0);
            importContent.Children.Add(headerBorder);

            // Estimate upload view takes remaining space
            _estimateUploadView = new EstimateUploadView();
            Grid.SetRow(_estimateUploadView, 1);
            importContent.Children.Add(_estimateUploadView);

            _tabContent?.Children.Add(importContent);
        }

        private void BuildStatsTab()
        {
            var statsContent = new Grid
            {
                Tag = 3,
                Visibility = Visibility.Collapsed
            };
            statsContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            statsContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header with help button
            var statsHeader = new Grid { Margin = new Thickness(16, 8, 16, 4) };
            statsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var statsHelpButton = ContextualHelpButton.Create("stats-tab");
            Grid.SetColumn(statsHelpButton, 1);
            statsHeader.Children.Add(statsHelpButton);
            Grid.SetRow(statsHeader, 0);
            statsContent.Children.Add(statsHeader);

            // Create the StatisticsView and add it to the content
            _statisticsView = new StatisticsView();
            Grid.SetRow(_statisticsView, 1);

            statsContent.Children.Add(_statisticsView);
            _tabContent?.Children.Add(statsContent);
        }

        private void BuildReferenceTab()
        {
            var referenceContent = new Grid
            {
                Tag = 4,
                Visibility = Visibility.Collapsed
            };
            referenceContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            referenceContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header with help button
            var refHeader = new Grid { Margin = new Thickness(16, 8, 16, 4) };
            refHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            refHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var refHelpButton = ContextualHelpButton.Create("reference-tab");
            Grid.SetColumn(refHelpButton, 1);
            refHeader.Children.Add(refHelpButton);
            Grid.SetRow(refHeader, 0);
            referenceContent.Children.Add(refHeader);

            // Create the ReferenceView (combined: Definitions, DEG, P-Pages, Procedures)
            _referenceView = new ReferenceView();
            Grid.SetRow(_referenceView, 1);

            referenceContent.Children.Add(_referenceView);
            _tabContent?.Children.Add(referenceContent);
        }

        private void BuildSettingsTab()
        {
            var settingsContent = new Grid
            {
                Tag = 5,
                Visibility = Visibility.Collapsed
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var mainStack = new StackPanel
            {
                Spacing = 16,
                Padding = new Thickness(20)
            };

            // === HEADER ===
            var headerStack = new StackPanel { Spacing = 4 };
            headerStack.Children.Add(new TextBlock
            {
                Text = "Settings",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Application settings and updates",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            mainStack.Children.Add(headerStack);

            // === VERSION SECTION ===
            var versionCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var versionStack = new StackPanel { Spacing = 12 };

            // Version header
            versionStack.Children.Add(new TextBlock
            {
                Text = "Version Information",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Current version with big display
            var versionDisplay = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            versionDisplay.Children.Add(new FontIcon
            {
                Glyph = "\uE895",
                FontSize = 32,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
            });
            var versionTextStack = new StackPanel();
            versionTextStack.Children.Add(new TextBlock
            {
                Text = $"McStud Tool {UpdateService.GetVersionString()}",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            versionTextStack.Children.Add(new TextBlock
            {
                Text = "You are running the latest version",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            versionDisplay.Children.Add(versionTextStack);
            versionStack.Children.Add(versionDisplay);

            // Check for updates button
            var updateButton = new Button
            {
                Content = "Check for Updates",
                Padding = new Thickness(20, 10, 20, 10),
                Margin = new Thickness(0, 8, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 200)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            updateButton.Click += async (s, e) => await ShowUpdateCheckDialog();
            versionStack.Children.Add(updateButton);

            versionCard.Child = versionStack;
            mainStack.Children.Add(versionCard);

            // === SHOP NAME SECTION ===
            var shopNameCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var shopNameStack = new StackPanel { Spacing = 10 };
            shopNameStack.Children.Add(new TextBlock
            {
                Text = "Shop Info",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            shopNameStack.Children.Add(new TextBlock
            {
                Text = "Your shop name appears on checklists and shop documents",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });

            var shopDocsSettings = ShopDocsSettingsService.Instance.GetSettings();
            var shopNameBox = new TextBox
            {
                PlaceholderText = "Enter your body shop name",
                Text = shopDocsSettings.ShopName ?? "",
                FontSize = 13,
                Width = 350,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            shopNameBox.LostFocus += (s, e) =>
            {
                var settings = ShopDocsSettingsService.Instance.GetSettings();
                settings.ShopName = shopNameBox.Text?.Trim() ?? "";
                ShopDocsSettingsService.Instance.SaveSettings(settings);
            };
            shopNameStack.Children.Add(shopNameBox);

            shopNameCard.Child = shopNameStack;
            mainStack.Children.Add(shopNameCard);

            // === LEARNING MODE SECTION ===
            var learningModeCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var learningModeStack = new StackPanel { Spacing = 10 };
            learningModeStack.Children.Add(new TextBlock
            {
                Text = "Learning Mode",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            learningModeStack.Children.Add(new TextBlock
            {
                Text = "Choose whether to use the standard pre-trained knowledge base or build your own from scratch.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                TextWrapping = TextWrapping.Wrap
            });

            var currentMode = LearningModeService.Instance.CurrentMode;

            // Standard radio with ? tooltip
            var standardContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            standardContent.Children.Add(new TextBlock { Text = "Standard (Recommended)", VerticalAlignment = VerticalAlignment.Center });
            var standardHelp = new FontIcon { Glyph = "\uE897", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(standardHelp, "Uses the pre-trained learned data that ships with the app.\nBest for consistent results across the team.\nUploaded estimates use this data but do not modify it.");
            standardContent.Children.Add(standardHelp);

            var shopRadio = new RadioButton
            {
                Content = standardContent,
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White),
                IsChecked = currentMode == LearningMode.Shop,
                GroupName = "LearningMode",
                Margin = new Thickness(0, 4, 0, 0)
            };

            var shopDescription = new TextBlock
            {
                Text = "Uses the pre-trained learned data. Best for consistent results across the team. Uploaded estimates use the standard knowledge base but do not modify it.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(32, 0, 0, 4)
            };

            // Personal radio with ? tooltip
            var personalContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            personalContent.Children.Add(new TextBlock { Text = "Personal", VerticalAlignment = VerticalAlignment.Center });
            var personalHelp = new FontIcon { Glyph = "\uE897", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(personalHelp, "Start fresh with no baseline data.\nYour learning builds entirely from your own trained estimates.\nUploaded estimates are saved to your personal data file.");
            personalContent.Children.Add(personalHelp);

            var personalRadio = new RadioButton
            {
                Content = personalContent,
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White),
                IsChecked = currentMode == LearningMode.Personal,
                GroupName = "LearningMode",
                Margin = new Thickness(0, 2, 0, 0)
            };

            var personalDescription = new TextBlock
            {
                Text = "Start fresh with no baseline data. Uploaded estimates are saved to your personal data file and build your own knowledge base.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(32, 0, 0, 4)
            };

            var warningText = new TextBlock
            {
                Text = "Note: Personal mode starts with an empty knowledge base if you haven't trained any estimates yet.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 160, 60)),
                TextWrapping = TextWrapping.Wrap,
                Visibility = currentMode == LearningMode.Personal ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0)
            };

            shopRadio.Checked += async (s, e) =>
            {
                if (LearningModeService.Instance.CurrentMode == LearningMode.Shop) return;

                var dialog = new ContentDialog
                {
                    Title = "Switch to Standard?",
                    Content = "This will load the standard pre-trained knowledge base. Your personal data will remain saved on disk.",
                    PrimaryButtonText = "Switch",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    LearningModeService.Instance.SetMode(LearningMode.Shop);
                    EstimateLearningService.Instance.ReloadForMode(LearningMode.Shop);
                    warningText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    personalRadio.IsChecked = true;
                }
            };

            personalRadio.Checked += async (s, e) =>
            {
                if (LearningModeService.Instance.CurrentMode == LearningMode.Personal) return;

                var dialog = new ContentDialog
                {
                    Title = "Switch to Personal?",
                    Content = "This will unload the standard baseline data. You'll start with only your own previously trained estimates (if any). No data will be deleted.",
                    PrimaryButtonText = "Switch",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    LearningModeService.Instance.SetMode(LearningMode.Personal);
                    EstimateLearningService.Instance.ReloadForMode(LearningMode.Personal);
                    warningText.Visibility = Visibility.Visible;
                }
                else
                {
                    shopRadio.IsChecked = true;
                }
            };

            learningModeStack.Children.Add(shopRadio);
            learningModeStack.Children.Add(shopDescription);
            learningModeStack.Children.Add(personalRadio);
            learningModeStack.Children.Add(personalDescription);
            learningModeStack.Children.Add(warningText);

            learningModeCard.Child = learningModeStack;
            mainStack.Children.Add(learningModeCard);

            // === APP TOUR SECTION ===
            var tourCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var tourStack = new StackPanel { Spacing = 12 };
            tourStack.Children.Add(new TextBlock
            {
                Text = "App Tour",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            tourStack.Children.Add(new TextBlock
            {
                Text = "Take a guided tour of all the tabs and features in McStud Tool.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                TextWrapping = TextWrapping.Wrap
            });

            var replayTourButton = new Button
            {
                Padding = new Thickness(20, 10, 20, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 200)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            var replayContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            replayContent.Children.Add(new FontIcon { Glyph = "\uE8B0", FontSize = 16 });
            replayContent.Children.Add(new TextBlock { Text = "Replay Tour" });
            replayTourButton.Content = replayContent;
            replayTourButton.Click += (s, e) => StartSpotlightTour();
            tourStack.Children.Add(replayTourButton);

            tourCard.Child = tourStack;
            mainStack.Children.Add(tourCard);

            // === WHAT'S NEW SECTION ===
            var whatsNewCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var whatsNewStack = new StackPanel { Spacing = 12 };

            var changelog = Services.ChangelogData.GetChangelog();
            var latest = changelog.FirstOrDefault();

            // Header row: "What's New" + current version + date
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            headerRow.Children.Add(new TextBlock
            {
                Text = "What's New",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            if (latest != null)
            {
                headerRow.Children.Add(new TextBlock
                {
                    Text = latest.Version,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                headerRow.Children.Add(new TextBlock
                {
                    Text = $"— {latest.Date}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            whatsNewStack.Children.Add(headerRow);

            // Version history as collapsible expanders
            foreach (var entry in changelog)
            {
                var expander = new Expander
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsExpanded = entry == latest,
                    Padding = new Thickness(0)
                };

                // Expander header: version + date
                var expanderHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                expanderHeader.Children.Add(new TextBlock
                {
                    Text = entry.Version,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255))
                });
                expanderHeader.Children.Add(new TextBlock
                {
                    Text = entry.Date,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                expanderHeader.Children.Add(new TextBlock
                {
                    Text = $"({entry.Changes.Length} changes)",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 90, 90, 90)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                expander.Header = expanderHeader;

                // Expander content: bullet list of changes
                var changeStack = new StackPanel { Spacing = 4, Margin = new Thickness(4, 4, 0, 4) };
                foreach (var change in entry.Changes)
                {
                    var changeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    changeRow.Children.Add(new TextBlock
                    {
                        Text = "•",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
                    });
                    changeRow.Children.Add(new TextBlock
                    {
                        Text = change,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                        TextWrapping = TextWrapping.Wrap
                    });
                    changeStack.Children.Add(changeRow);
                }
                expander.Content = changeStack;

                whatsNewStack.Children.Add(expander);
            }

            whatsNewCard.Child = whatsNewStack;
            mainStack.Children.Add(whatsNewCard);

            // === CONTACT SUPPORT SECTION ===
            var supportCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var supportStack = new StackPanel { Spacing = 14 };

            // Header row with icon
            var supportHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            supportHeaderRow.Children.Add(new FontIcon
            {
                Glyph = "\uE715", // Contact icon
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });
            supportHeaderRow.Children.Add(new TextBlock
            {
                Text = "Contact Support",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            supportStack.Children.Add(supportHeaderRow);

            supportStack.Children.Add(new TextBlock
            {
                Text = "Have an issue, feedback, or feature request? Send us an email and we'll get back to you.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                TextWrapping = TextWrapping.Wrap
            });

            // Email button — opens default mail client
            var emailButton = new Button
            {
                Padding = new Thickness(24, 12, 24, 12),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var emailContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            emailContent.Children.Add(new FontIcon { Glyph = "\uE724", FontSize = 16 }); // Mail icon
            emailContent.Children.Add(new TextBlock
            {
                Text = "Email Mcstudestimating@gmail.com",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            emailButton.Content = emailContent;
            emailButton.Click += (s, e) =>
            {
                OpenSupportEmail("", "McStud Support Request", "");
            };
            supportStack.Children.Add(emailButton);

            supportCard.Child = supportStack;
            mainStack.Children.Add(supportCard);

            // === TEXT-TO-SPEECH SECTION ===
            var ttsCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var ttsStack = new StackPanel { Spacing = 12 };
            ttsStack.Children.Add(new TextBlock
            {
                Text = "Text-to-Speech",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            ttsStack.Children.Add(new TextBlock
            {
                Text = "Choose the voice used for Read Aloud buttons throughout the app.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                TextWrapping = TextWrapping.Wrap
            });

            var voiceCombo = new ComboBox
            {
                MinWidth = 280,
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50))
            };

            var tts = Services.TextToSpeechService.Instance;
            var voices = Services.TextToSpeechService.GetAvailableVoices();
            int selectedIndex = 0;
            for (int i = 0; i < voices.Count; i++)
            {
                var v = voices[i];
                voiceCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{v.DisplayName} ({v.Language})",
                    Tag = v.DisplayName
                });
                if (v.DisplayName == tts.CurrentVoiceName)
                    selectedIndex = i;
            }
            voiceCombo.SelectedIndex = selectedIndex;

            voiceCombo.SelectionChanged += (s, e) =>
            {
                if (voiceCombo.SelectedItem is ComboBoxItem item && item.Tag is string name)
                    tts.SetVoice(name);
            };

            ttsStack.Children.Add(voiceCombo);

            // Preview button
            var previewBtn = new Button
            {
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                CornerRadius = new CornerRadius(4)
            };
            var previewContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            previewContent.Children.Add(new FontIcon { Glyph = "\uE767", FontSize = 14 });
            previewContent.Children.Add(new TextBlock { Text = "Preview Voice" });
            previewBtn.Content = previewContent;
            previewBtn.Click += (s, e) =>
            {
                tts.SpeakAsync("This is a preview of the selected text-to-speech voice.");
            };
            ttsStack.Children.Add(previewBtn);

            ttsCard.Child = ttsStack;
            mainStack.Children.Add(ttsCard);

            // === ABOUT SECTION ===
            var aboutCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var aboutStack = new StackPanel { Spacing = 8 };
            aboutStack.Children.Add(new TextBlock
            {
                Text = "About",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            aboutStack.Children.Add(new TextBlock
            {
                Text = "McStud Tool - Clipboard export tool for collision estimating",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                TextWrapping = TextWrapping.Wrap
            });
            aboutStack.Children.Add(new TextBlock
            {
                Text = $"© {DateTime.Now.Year} McStud Estimating",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                Margin = new Thickness(0, 8, 0, 0)
            });

            aboutCard.Child = aboutStack;
            mainStack.Children.Add(aboutCard);

            scrollViewer.Content = mainStack;
            settingsContent.Children.Add(scrollViewer);
            _tabContent?.Children.Add(settingsContent);
        }

        private void BuildGuideTab()
        {
            var guideContent = new Grid
            {
                Tag = 6,
                Visibility = Visibility.Collapsed
            };
            guideContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            guideContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header with help button
            var guideHeader = new Grid { Margin = new Thickness(16, 8, 16, 4) };
            guideHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            guideHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var guideHelpButton = ContextualHelpButton.Create("guide-tab");
            Grid.SetColumn(guideHelpButton, 1);
            guideHeader.Children.Add(guideHelpButton);
            Grid.SetRow(guideHeader, 0);
            guideContent.Children.Add(guideHeader);

            // Create the METGuideView
            _metGuideView = new METGuideView();
            Grid.SetRow(_metGuideView, 1);

            guideContent.Children.Add(_metGuideView);
            _tabContent?.Children.Add(guideContent);
        }

        private void BuildShopDocsTab()
        {
            var shopDocsContent = new Grid
            {
                Tag = 7,
                Visibility = Visibility.Collapsed
            };
            shopDocsContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shopDocsContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header with help button
            var shopDocsHeader = new Grid { Margin = new Thickness(16, 8, 16, 4) };
            shopDocsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shopDocsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var shopDocsHelpButton = ContextualHelpButton.Create("shop-docs-tab");
            Grid.SetColumn(shopDocsHelpButton, 1);
            shopDocsHeader.Children.Add(shopDocsHelpButton);
            Grid.SetRow(shopDocsHeader, 0);
            shopDocsContent.Children.Add(shopDocsHeader);

            // Create the ShopDocsView
            _shopDocsView = new ShopDocsView();
            Grid.SetRow(_shopDocsView, 1);

            shopDocsContent.Children.Add(_shopDocsView);
            _tabContent?.Children.Add(shopDocsContent);
        }

        private StackPanel CreateVersionEntry(string version, string date, string[] changes)
        {
            var entry = new StackPanel { Spacing = 8 };

            // Version header with date
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            headerStack.Children.Add(new TextBlock
            {
                Text = version,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255))
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = date,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center
            });
            entry.Children.Add(headerStack);

            // Changes list
            var changeStack = new StackPanel { Spacing = 4, Margin = new Thickness(8, 0, 0, 0) };
            foreach (var change in changes)
            {
                var changeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                changeRow.Children.Add(new TextBlock
                {
                    Text = "•",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
                });
                changeRow.Children.Add(new TextBlock
                {
                    Text = change,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    TextWrapping = TextWrapping.Wrap
                });
                changeStack.Children.Add(changeRow);
            }
            entry.Children.Add(changeStack);

            return entry;
        }

        /// <summary>
        /// Creates the merged Clipboard Summary panel showing totals and operations list
        /// </summary>
        private StackPanel CreateClipboardSummaryPanel()
        {
            var panel = new StackPanel
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
                Padding = new Thickness(12),
                Spacing = 8
            };

            // === TOTALS ROW ===
            var totalsGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(4)
            };
            totalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Operations count
            var countStack = CreateTotalItem("Operations", out _clipboardCountText);
            Grid.SetColumn(countStack, 0);
            totalsGrid.Children.Add(countStack);

            // Total Price
            var priceStack = CreateTotalItem("Price", out _clipboardPriceText);
            Grid.SetColumn(priceStack, 1);
            totalsGrid.Children.Add(priceStack);

            // Total Labor
            var laborStack = CreateTotalItem("Labor", out _clipboardLaborText);
            Grid.SetColumn(laborStack, 2);
            totalsGrid.Children.Add(laborStack);

            // Total Refinish
            var refinishStack = CreateTotalItem("Refinish", out _clipboardRefinishText);
            Grid.SetColumn(refinishStack, 3);
            totalsGrid.Children.Add(refinishStack);

            panel.Children.Add(totalsGrid);

            // === OPERATIONS LIST ===
            var listLabel = new TextBlock
            {
                Text = "Operations:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                Margin = new Thickness(0, 4, 0, 2)
            };
            panel.Children.Add(listLabel);

            _operationsList = new ListView
            {
                MaxHeight = 450,
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                SelectionMode = ListViewSelectionMode.None,
                Padding = new Thickness(6)
            };
            panel.Children.Add(_operationsList);

            return panel;
        }

        private StackPanel CreateTotalItem(string label, out TextBlock valueText)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 2
            };

            var labelTb = new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            valueText = new TextBlock
            {
                Text = "0",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            stack.Children.Add(labelTb);
            stack.Children.Add(valueText);

            return stack;
        }

        private StackPanel CreateExpanderHeader(string text, string glyph)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            var icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            };
            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(icon);
            stack.Children.Add(label);
            return stack;
        }

        /// <summary>
        /// Creates the clipboard summary header with a dynamic text that shows the source
        /// </summary>
        private StackPanel CreateClipboardSummaryHeader()
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            var icon = new FontIcon
            {
                Glyph = "\uE8A5", // Clipboard icon
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            };
            _clipboardSummaryHeaderText = new TextBlock
            {
                Text = "Clipboard Summary",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(icon);
            stack.Children.Add(_clipboardSummaryHeaderText);
            return stack;
        }

        private Button CreateExportButton(string text, string iconGlyph, Color accentColor, bool comingSoon = false)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(12, 10, 12, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(6)
            };

            // Create a grid for better layout control
            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = iconGlyph,
                FontSize = 16,
                Foreground = new SolidColorBrush(comingSoon ? Color.FromArgb(255, 100, 100, 100) : accentColor),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(icon, 0);

            var buttonText = new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(comingSoon ? Color.FromArgb(255, 120, 120, 120) : Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(buttonText, 1);

            // Coming Soon badge (red)
            if (comingSoon)
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 180, 50, 50)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badge.Child = new TextBlock
                {
                    Text = "SOON",
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                Grid.SetColumn(badge, 2);
                contentGrid.Children.Add(badge);
            }

            // Arrow indicator
            var arrow = new FontIcon
            {
                Glyph = "\uE76C", // ChevronRight
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(arrow, 3);

            contentGrid.Children.Add(icon);
            contentGrid.Children.Add(buttonText);
            contentGrid.Children.Add(arrow);
            button.Content = contentGrid;

            return button;
        }

        private Button CreateBigButton(string text, string iconGlyph, Color accentColor)
        {
            return CreateExportButton(text, iconGlyph, accentColor);
        }

        private void StartClipboardMonitoring()
        {
            _clipboardTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Check every 500ms
            };
            _clipboardTimer.Tick += ClipboardTimer_Tick;
            _clipboardTimer.Start();
        }

        /// <summary>
        /// Track foreground window changes - saves the window user was in BEFORE McStud
        /// Also continuously tracks cursor position when user is in CCC
        /// </summary>
        private void StartWindowTracking()
        {
            _windowTracker = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _windowTracker.Tick += (s, e) =>
            {
                var current = GetForegroundWindow();
                var sb = new System.Text.StringBuilder(256);
                GetWindowText(current, sb, 256);
                string currentTitle = sb.ToString();
                bool isOurWindow = currentTitle.Contains("McStud", StringComparison.OrdinalIgnoreCase);

                // If user is in an external window (like CCC), continuously save cursor position
                // This way we always have the LAST position where the user was in CCC
                // When they switch to McStud, we stop updating (so we keep their last CCC position)
                if (!isOurWindow)
                {
                    _previousActiveWindow = current;
                    // Continuously save cursor position while in external window
                    // This captures where the user is clicking in CCC
                    _cccInsertService.SaveCursorPosition();
                    // Also track which window they're in
                    _cccInsertService.SetTargetWindowOnly(current);
                }

                if (current != _lastKnownForeground)
                {
                    // Window changed
                    if (isOurWindow && _lastKnownForeground != IntPtr.Zero)
                    {
                        // McStud became active - LOCK the position now!
                        // This is the moment user clicked from CCC to McStud
                        // The cursor position from CCC is frozen here
                        GetWindowText(_lastKnownForeground, sb, 256);
                        string lastTitle = sb.ToString();
                        if (!lastTitle.Contains("McStud", StringComparison.OrdinalIgnoreCase))
                        {
                            _previousActiveWindow = _lastKnownForeground;
                            // LOCK the click position - this is where user last was in CCC
                            _cccInsertService.LockClickPosition();
                            System.Diagnostics.Debug.WriteLine($"[McStudToolView] User switched to McStud - click position locked");
                        }
                    }
                    _lastKnownForeground = current;
                }
            };
            _windowTracker.Start();
        }

        private void ClipboardTimer_Tick(object? sender, object e)
        {
            try
            {
                // Check clipboard for new Excel data
                string? clipboardText = null;
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        if (System.Windows.Forms.Clipboard.ContainsText())
                        {
                            clipboardText = System.Windows.Forms.Clipboard.GetText();
                        }
                    }
                    catch { }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join(200);

                if (string.IsNullOrWhiteSpace(clipboardText)) return;

                // Calculate hash to detect changes
                var hash = clipboardText.GetHashCode().ToString();
                if (hash == _lastClipboardHash) return;

                // STRICT FILTERING: Only accept data that looks like estimate operations
                // Must have tabs (Excel format)
                if (!clipboardText.Contains('\t')) return;

                // Must contain at least one operation keyword to be considered estimate data
                var opKeywords = new[] { "Replace", "Rpr", "R&I", "R+I", "Blend", "Refinish", "O/H", "Sublet", "Add", "Remove", "Install", "Repair" };
                bool hasOpKeyword = false;
                foreach (var keyword in opKeywords)
                {
                    if (clipboardText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        hasOpKeyword = true;
                        break;
                    }
                }
                if (!hasOpKeyword) return; // Not estimate data, ignore

                _lastClipboardHash = hash;

                // Auto-read the clipboard
                var rawCount = _exportService.ReadFromClipboard();
                if (rawCount > 0)
                {
                    // Record this as an IMPORT (data coming into McStud)
                    var operations = _exportService.GetParsedOperations();
                    _statsService.RecordImport(operations);

                    // Use actual parsed operations count, not raw line count
                    UpdateStatusReady(operations.Count);
                    UpdatePreview();
                }
            }
            catch { }
        }

        private int _lastLoadedCount = 0;

        private void UpdateStatusReady(int count)
        {
            if (_statusBorder == null || _statusIcon == null || _statusText == null || _countText == null) return;

            _statusBorder.Background = new SolidColorBrush(Color.FromArgb(255, 0, 60, 30));
            _statusBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 200, 100));
            _statusIcon.Text = "✓";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 150));

            // Show if count changed (updated data)
            if (_lastLoadedCount > 0 && count != _lastLoadedCount)
            {
                _statusText.Text = "UPDATED - Ready to Export";
                _countText.Text = $"{count} rows (was {_lastLoadedCount})";
            }
            else
            {
                _statusText.Text = "Ready to Export";
                _countText.Text = $"{count} rows loaded";
            }
            _lastLoadedCount = count;

            _countText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));

            if (_cccButton != null) _cccButton.IsEnabled = true;
            if (_cccWebButton != null) _cccWebButton.IsEnabled = true;
            if (_mitchellButton != null) _mitchellButton.IsEnabled = true;
        }

        private void UpdateStatusWaiting()
        {
            if (_statusBorder == null || _statusIcon == null || _statusText == null || _countText == null) return;

            _statusBorder.Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40));
            _statusBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60));
            _statusIcon.Text = "⏳";
            _statusText.Text = "Waiting for data...";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
            _countText.Text = "Copy from Excel (Ctrl+C) or add from Live Coach (+CCC)";
            _countText.Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120));

            if (_cccButton != null) _cccButton.IsEnabled = false;
            if (_cccWebButton != null) _cccWebButton.IsEnabled = false;
            if (_mitchellButton != null) _mitchellButton.IsEnabled = false;
        }

        private void UpdateStatusCopied(int count, string target)
        {
            if (_statusBorder == null || _statusIcon == null || _statusText == null || _countText == null) return;

            _statusBorder.Background = new SolidColorBrush(Color.FromArgb(255, 0, 80, 150));
            _statusBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255));
            _statusIcon.Text = "📋";
            _statusText.Text = $"Copied for {target}!";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255));
            _countText.Text = $"Paste with Ctrl+V in {target}";
            _countText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
        }

        private void UpdatePreview()
        {
            if (_operationsList == null) return;

            _operationsList.Items.Clear();

            // Check if virtual clipboard has operations (preferred source)
            if (_virtualClipboard.Count > 0)
            {
                UpdateClipboardSummary();
                return;
            }

            // Fallback to parsing Windows clipboard
            var parsedOps = _exportService.GetParsedOperations();

            // Sync to virtual clipboard
            if (parsedOps.Count > 0)
            {
                _virtualClipboard.SetOperations(parsedOps, "Windows Clipboard");
                return; // UpdateClipboardSummary will be called by the event
            }

            // No operations - show empty state
            if (_clipboardCountText != null) _clipboardCountText.Text = "0";
            if (_clipboardPriceText != null) _clipboardPriceText.Text = "$0";
            if (_clipboardLaborText != null) _clipboardLaborText.Text = "0";
            if (_clipboardRefinishText != null) _clipboardRefinishText.Text = "0";
        }

        /// <summary>
        /// Updates the clipboard summary using VirtualClipboardService as the source of truth
        /// </summary>
        private void UpdateClipboardSummary()
        {
            if (_operationsList == null) return;

            _operationsList.Items.Clear();

            var operations = _virtualClipboard.Operations;
            int count = _virtualClipboard.Count;
            decimal totalPrice = _virtualClipboard.TotalPrice;
            decimal totalLabor = _virtualClipboard.TotalLaborHours;
            decimal totalRefinish = _virtualClipboard.TotalRefinishHours;
            string source = _virtualClipboard.Source;
            bool hasExcelTotals = _virtualClipboard.HasExcelTotals;

            // Determine if source is reliable (app pages) or parsed (Windows clipboard)
            bool isFromClipboardParsing = source.Contains("Windows Clipboard", StringComparison.OrdinalIgnoreCase);

            // === UPDATE HEADER TO SHOW SOURCE ===
            if (_clipboardSummaryHeaderText != null)
            {
                if (count > 0 && !string.IsNullOrEmpty(source))
                {
                    if (hasExcelTotals)
                    {
                        // Excel totals - most accurate
                        _clipboardSummaryHeaderText.Text = $"Clipboard Summary ({count} from {source}) ✓";
                        _clipboardSummaryHeaderText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));
                    }
                    else if (isFromClipboardParsing)
                    {
                        // Windows clipboard parsing - may be inaccurate
                        _clipboardSummaryHeaderText.Text = $"Clipboard Summary ({count} from clipboard - verify totals)";
                        _clipboardSummaryHeaderText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)); // Yellow warning
                    }
                    else
                    {
                        // From app page but without Excel totals
                        _clipboardSummaryHeaderText.Text = $"Clipboard Summary ({count} from {source})";
                        _clipboardSummaryHeaderText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));
                    }
                }
                else if (count > 0)
                {
                    _clipboardSummaryHeaderText.Text = $"Clipboard Summary ({count} operations)";
                    _clipboardSummaryHeaderText.Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160));
                }
                else
                {
                    _clipboardSummaryHeaderText.Text = "Clipboard Summary (empty)";
                    _clipboardSummaryHeaderText.Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120));
                }
            }

            // === UPDATE TOTALS ===
            // For clipboard parsing, don't show labor/refinish totals as they're unreliable
            // Only show accurate totals when we have Excel source or app-generated data
            if (_clipboardCountText != null) _clipboardCountText.Text = count.ToString();

            if (isFromClipboardParsing && !hasExcelTotals)
            {
                // Clipboard parsing - totals are unreliable, show N/A or warning
                if (_clipboardPriceText != null)
                {
                    _clipboardPriceText.Text = totalPrice > 0 ? $"${totalPrice:F2}" : "N/A";
                    _clipboardPriceText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100));
                }
                if (_clipboardLaborText != null)
                {
                    _clipboardLaborText.Text = "N/A"; // Don't trust parsed labor
                    _clipboardLaborText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100));
                }
                if (_clipboardRefinishText != null)
                {
                    _clipboardRefinishText.Text = "N/A"; // Don't trust parsed refinish
                    _clipboardRefinishText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100));
                }
            }
            else
            {
                // Accurate totals from Excel or app pages
                if (_clipboardPriceText != null)
                {
                    _clipboardPriceText.Text = totalPrice > 0 ? $"${totalPrice:F2}" : "-";
                    _clipboardPriceText.Foreground = new SolidColorBrush(Color.FromArgb(255, 78, 201, 176));
                }
                if (_clipboardLaborText != null)
                {
                    _clipboardLaborText.Text = totalLabor > 0 ? totalLabor.ToString("F1") : "-";
                    _clipboardLaborText.Foreground = new SolidColorBrush(Color.FromArgb(255, 156, 220, 254));
                }
                if (_clipboardRefinishText != null)
                {
                    _clipboardRefinishText.Text = totalRefinish > 0 ? totalRefinish.ToString("F1") : "-";
                    _clipboardRefinishText.Foreground = new SolidColorBrush(Color.FromArgb(255, 206, 145, 120));
                }
            }

            // === UPDATE OPERATIONS LIST ===
            int index = 1;
            foreach (var op in operations)
            {
                if (index > 10) // Limit to 10 for compact view
                {
                    _operationsList.Items.Add(new TextBlock
                    {
                        Text = $"... +{count - 10} more",
                        FontSize = 11,
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
                    });
                    break;
                }

                var displayText = $"{index}. {op.OperationType} - {op.Description}";
                if (op.Price > 0)
                    displayText += $" (${op.Price:F2})";
                if (op.LaborHours > 0)
                    displayText += $" [{op.LaborHours:F1}h]";

                _operationsList.Items.Add(new TextBlock
                {
                    Text = displayText,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                index++;
            }

            // Update status to show source
            if (count > 0 && !string.IsNullOrEmpty(source))
            {
                UpdateStatusReady(count);
            }
        }

        private bool _isTyping = false;

        private async void CCCDesktopButton_Click(object sender, RoutedEventArgs e)
        {
            // Use aiming mode - user clicks in CCC, presses ENTER to paste
            await StartTypeItExport();
        }

        /// <summary>
        /// OLD Export to CCC Desktop - kept for reference but no longer used
        /// </summary>
        private async Task ExportToCCCDesktop_OLD()
        {
            if (_isTyping) return;
            _isTyping = true;

            try
            {
                // Disable buttons during insert
                if (_cccButton != null) _cccButton.IsEnabled = false;
                if (_cccWebButton != null) _cccWebButton.IsEnabled = false;
                if (_mitchellButton != null) _mitchellButton.IsEnabled = false;

                // Get the RAW clipboard rows (not parsed)
                var rawRows = _exportService.Operations;
                if (rawRows.Count == 0)
                {
                    UpdateStatusError("No data found. Add items with + CCC or copy from Excel.");
                    _isTyping = false;
                    if (_cccButton != null) _cccButton.IsEnabled = true;
                    if (_cccWebButton != null) _cccWebButton.IsEnabled = true;
                    if (_mitchellButton != null) _mitchellButton.IsEnabled = true;
                    return;
                }

                // Check if we have a target window
                if (_previousActiveWindow == IntPtr.Zero)
                {
                    UpdateStatusError("Select the A column in CCC first, then click here.");
                    _isTyping = false;
                    if (_cccButton != null) _cccButton.IsEnabled = true;
                    if (_cccWebButton != null) _cccWebButton.IsEnabled = true;
                    if (_mitchellButton != null) _mitchellButton.IsEnabled = true;
                    return;
                }

                UpdateStatusTyping("CCC Desktop");

                // Use CCCInsertService - simplified approach:
                // Right-click > Type 'I' > Enter > Type fields with Tab > Enter
                await _cccInsertService.InsertRawRowsAsync(rawRows.ToList());

                // Record stats (done in OnCCCInsertCompleted)
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CCC export error: {ex.Message}");
                UpdateStatusError(ex.Message);
                _isTyping = false;
                if (_cccButton != null) _cccButton.IsEnabled = true;
                if (_cccWebButton != null) _cccWebButton.IsEnabled = true;
                if (_mitchellButton != null) _mitchellButton.IsEnabled = true;
            }
            // Note: _isTyping and button state are reset in OnCCCInsertCompleted
        }

        /// <summary>
        /// Called when CCC insert operation completes
        /// </summary>
        private void OnCCCInsertCompleted(bool success)
        {
            _isTyping = false;
            if (_cccButton != null) _cccButton.IsEnabled = true;
            if (_cccWebButton != null) _cccWebButton.IsEnabled = true;
            if (_mitchellButton != null) _mitchellButton.IsEnabled = true;

            if (success)
            {
                var parsedOps = _exportService.GetParsedOperations();
                _statsService.RecordExport(parsedOps, "CCC Desktop");
                UpdateStatusDone(parsedOps.Count, "CCC Desktop");
            }
        }

        private async void CCCWebButton_Click(object sender, RoutedEventArgs e)
        {
            await StartAutoType("CCC Web");
        }

        private async void MitchellButton_Click(object sender, RoutedEventArgs e)
        {
            await StartAutoType("Mitchell");
        }

        /// <summary>
        /// Type It button click - enters aiming mode where user can click freely in CCC
        /// and press ENTER to start typing at current cursor position
        /// </summary>
        private async void TypeItButton_Click(object sender, RoutedEventArgs e)
        {
            await StartTypeItExport();
        }

        /// <summary>
        /// Export to CCC Desktop - AutoHotkey style:
        /// 1. Click button - Alt+Tab to CCC
        /// 2. BlockInput - user can't interfere
        /// 3. Type FAST
        /// 4. Done (or paused if interrupted - can resume)
        /// </summary>
        private async Task StartTypeItExport()
        {
            System.Diagnostics.Debug.WriteLine("[McStudToolView] StartTypeItExport called");
            if (_isTyping)
            {
                return;
            }
            _isTyping = true;

            try
            {
                // Disable buttons
                if (_cccButton != null) _cccButton.IsEnabled = false;
                if (_cccWebButton != null) _cccWebButton.IsEnabled = false;
                if (_mitchellButton != null) _mitchellButton.IsEnabled = false;
                if (_resumeButton != null) _resumeButton.Visibility = Visibility.Collapsed;

                // Get clipboard data — prefer raw Excel rows, fall back to Virtual Clipboard
                var rawRows = _exportService.Operations;
                var rows = new List<string[]>();

                if (rawRows.Count > 0)
                {
                    // Excel/clipboard source — use raw tab-separated rows
                    foreach (var row in rawRows)
                    {
                        var fields = row.Split('\t');
                        rows.Add(fields);
                    }
                }
                else if (_virtualClipboard.Count > 0)
                {
                    // Virtual clipboard source (Live Coach "+ CCC" button, etc.)
                    // Full CCC Desktop 18-column format:
                    // 0  0  0  0  0  0  OpType  0  Description  0  Qty  Price  0  0  0  Labor  M  Refinish
                    foreach (var op in _virtualClipboard.Operations)
                    {
                        var opType = op.OperationType;
                        var desc = op.Description;
                        var qty = op.Quantity > 0 ? op.Quantity.ToString() : "1";
                        var labor = op.LaborHours > 0 ? op.LaborHours.ToString("0.0") : "0";
                        var refinish = op.RefinishHours > 0 ? op.RefinishHours.ToString("0.0") : "0";
                        var price = op.Price > 0 ? op.Price.ToString("0.00") : "0";
                        rows.Add(new[] {
                            "0", "0", "0", "0", "0", "0",
                            opType,
                            "0",
                            desc,
                            "0",
                            qty,
                            price,
                            "0", "0", "0",
                            labor,
                            "M",
                            refinish
                        });
                    }
                }
                else
                {
                    UpdateStatusError("No data found. Add items with + CCC or copy from Excel.");
                    _isTyping = false;
                    EnableAllButtons();
                    return;
                }

                // Create or reuse paste service (keep for resume)
                _pasteService?.Dispose();
                _pasteService = new AutoHotkeyPasteService();

                // Apply speed settings based on speed combo selection
                ApplySpeedSettings(_pasteService);

                // Wire up events for UI updates
                _pasteService.StatusChanged += (s, status) =>
                    DispatcherQueue?.TryEnqueue(() => { if (_statusText != null) _statusText.Text = status; });
                _pasteService.ProgressChanged += (s, p) =>
                    DispatcherQueue?.TryEnqueue(() => { if (_countText != null) _countText.Text = $"{p.current}/{p.total}"; });
                _pasteService.Interrupted += (s, e) =>
                    DispatcherQueue?.TryEnqueue(() => ShowResumeButton());

                // DO IT - Alt+Tab back to where user was (CCC), detect interrupts
                bool success = await _pasteService.PasteToApp(rows.ToArray(), switchWindow: true);

                if (success)
                {
                    var parsedOps = _exportService.GetParsedOperations();
                    _statsService.RecordExport(parsedOps, "CCC Desktop");
                    UpdateStatusDone(rows.Count, "CCC Desktop");
                    _pasteService?.Dispose();
                    _pasteService = null;
                }
                // If interrupted, the service stays alive for resume
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Export error: {ex.Message}");
                UpdateStatusError(ex.Message);
            }
            finally
            {
                _isTyping = false;
                EnableAllButtons();
            }
        }

        private void ApplySpeedSettings(AutoHotkeyPasteService pasteService)
        {
            // Current defaults (KeyDelay=20, TabDelay=30, EnterDelay=50) = Fast
            switch (_speedCombo?.SelectedIndex ?? 2)
            {
                case 0: // Slow
                    pasteService.KeyDelay = 50;
                    pasteService.TabDelay = 80;
                    pasteService.EnterDelay = 150;
                    break;
                case 1: // Normal
                    pasteService.KeyDelay = 35;
                    pasteService.TabDelay = 50;
                    pasteService.EnterDelay = 100;
                    break;
                case 2: // Fast (current defaults)
                    pasteService.KeyDelay = 20;
                    pasteService.TabDelay = 30;
                    pasteService.EnterDelay = 50;
                    break;
                case 3: // Turbo
                    pasteService.KeyDelay = 5;
                    pasteService.TabDelay = 10;
                    pasteService.EnterDelay = 20;
                    break;
            }
        }

        private void ShowResumeButton()
        {
            if (_resumeButton != null)
            {
                _resumeButton.Visibility = Visibility.Visible;
            }
        }

        private async void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pasteService == null || !_pasteService.CanResume)
            {
                if (_statusText != null) _statusText.Text = "Nothing to resume";
                return;
            }

            if (_isTyping) return;
            _isTyping = true;

            try
            {
                if (_resumeButton != null) _resumeButton.Visibility = Visibility.Collapsed;
                if (_cccButton != null) _cccButton.IsEnabled = false;

                bool success = await _pasteService.Resume(switchWindow: true);

                if (success)
                {
                    var parsedOps = _exportService.GetParsedOperations();
                    _statsService.RecordExport(parsedOps, "CCC Desktop");
                    UpdateStatusDone(parsedOps.Count, "CCC Desktop");
                    _pasteService?.Dispose();
                    _pasteService = null;
                }
                // If interrupted again, stays alive for another resume
            }
            catch (Exception ex)
            {
                UpdateStatusError(ex.Message);
            }
            finally
            {
                _isTyping = false;
                EnableAllButtons();
            }
        }

        private async Task RunTypeItAimAndTypeAsync(string[][] rows, bool resume = false)
        {
            if (_typeItService == null) return;

            var progress = new Progress<TypeItProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_statusText != null) _statusText.Text = $"Typing {p.CurrentRow}/{p.TotalRows}...";
                    if (_countText != null) _countText.Text = $"{(p.CurrentRow * 100 / p.TotalRows)}% complete";
                });
            });

            try
            {
                TypeItResult result;
                if (resume)
                {
                    result = await _typeItService.ResumeAsync(progress);
                }
                else
                {
                    result = await _typeItService.AimAndTypeAsync(rows, progress);
                }

                if (result.Completed)
                {
                    var parsedOps = _exportService.GetParsedOperations();
                    _statsService.RecordExport(parsedOps, "CCC Desktop (Type It)");
                    UpdateStatusDone(result.RowsCompleted, "CCC Desktop");
                    _typeItService?.Dispose();
                    _typeItService = null;
                }
                else if (result.NotCCCWindow)
                {
                    UpdateStatusError("Select the A column inside CCC Desktop, not other windows. Try again.");
                    _typeItService?.Dispose();
                    _typeItService = null;
                }
                else if (result.WasInterrupted)
                {
                    // User input detected - show pause state
                    UpdateStatusPaused(result.RowsCompleted, result.TotalRows);
                }
                else if (result.WasCancelled)
                {
                    UpdateStatusMessage("Export cancelled");
                    _typeItService?.Dispose();
                    _typeItService = null;
                }
            }
            catch (Exception ex)
            {
                UpdateStatusError(ex.Message);
                _typeItService?.Dispose();
                _typeItService = null;
            }
            finally
            {
                _isTyping = false;
                EnableAllButtons();
            }
        }

        private void EnableAllButtons()
        {
            if (_cccButton != null) _cccButton.IsEnabled = true;
            if (_cccWebButton != null) _cccWebButton.IsEnabled = true;
            if (_mitchellButton != null) _mitchellButton.IsEnabled = true;
        }

        private void UpdateStatusAiming()
        {
            if (_statusBorder == null || _statusIcon == null || _statusText == null || _countText == null) return;

            _statusBorder.Background = new SolidColorBrush(Color.FromArgb(255, 0, 60, 120));
            _statusBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255));
            _statusIcon.Text = "🎯";
            _statusText.Text = "Select the A column in CCC, press ENTER to paste";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255));
            _countText.Text = "ESC to cancel";
            _countText.Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
        }

        private void UpdateStatusPaused(int completed, int total)
        {
            if (_statusBorder == null || _statusIcon == null || _statusText == null || _countText == null) return;

            _statusBorder.Background = new SolidColorBrush(Color.FromArgb(255, 80, 60, 0));
            _statusBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100));
            _statusIcon.Text = "⏸";
            _statusText.Text = $"Paused at {completed}/{total} - User input detected";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100));
            _countText.Text = "Click Type It again to resume";
            _countText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
        }

        // Called when pointer enters the button - not used anymore but kept for reference
        private void ExportButton_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Window tracking now handled by _windowTracker timer
        }

        private async Task StartAutoType(string target)
        {
            if (_isTyping) return;
            _isTyping = true;

            try
            {
                // Disable buttons during paste
                if (_cccButton != null) _cccButton.IsEnabled = false;
                if (_cccWebButton != null) _cccWebButton.IsEnabled = false;
                if (_mitchellButton != null) _mitchellButton.IsEnabled = false;

                // Get raw clipboard data (full tab-separated rows from Excel)
                string? clipboardText = null;
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        if (System.Windows.Forms.Clipboard.ContainsText())
                        {
                            clipboardText = System.Windows.Forms.Clipboard.GetText();
                        }
                    }
                    catch { }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (string.IsNullOrEmpty(clipboardText))
                {
                    UpdateStatusError("No data in clipboard");
                    return;
                }

                // Parse into rows/columns
                var rows = ParseClipboardToRows(clipboardText);

                if (rows.Count == 0)
                {
                    UpdateStatusError("No valid rows found");
                    return;
                }

                // Use the window that was active BEFORE McStud got focus
                if (_previousActiveWindow == IntPtr.Zero)
                {
                    UpdateStatusError("No target window - select the A column in CCC first, then click here");
                    return;
                }

                // Update status once
                UpdateStatusTyping(target);

                // Use SmartPasteService to type each row with proper Tab/Enter
                _smartPasteService.SetTargetWindow(_previousActiveWindow);
                await _smartPasteService.PasteToTargetAsync(rows);

                // Record stats
                var operations = _exportService.GetParsedOperations();
                _statsService.RecordExport(operations, target);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-type error: {ex.Message}");
                UpdateStatusError(ex.Message);
            }
            finally
            {
                _isTyping = false;
                if (_cccButton != null) _cccButton.IsEnabled = true;
                    if (_cccWebButton != null) _cccWebButton.IsEnabled = true;
                if (_mitchellButton != null) _mitchellButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Parse clipboard text into rows and columns (tab-separated)
        /// </summary>
        private List<List<string>> ParseClipboardToRows(string text)
        {
            var rows = new List<List<string>>();
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Split by tab - DON'T trim, preserve exact values
                // Empty cells from Excel come as empty strings between tabs
                var fields = line.Split('\t').ToList();
                if (fields.Count > 0)
                {
                    rows.Add(fields);
                }
            }

            // Normalize rows - ensure all rows have the same number of fields as the longest row
            if (rows.Count > 1)
            {
                int maxFields = rows.Max(r => r.Count);

                foreach (var row in rows)
                {
                    // If this row has fewer fields, find where the operation type is
                    // and insert a placeholder before it to align with other rows
                    if (row.Count < maxFields)
                    {
                        int missing = maxFields - row.Count;

                        // Find the operation type column (Replace, Rpr, Refinish, etc.)
                        int opIndex = -1;
                        for (int i = 0; i < row.Count; i++)
                        {
                            var val = row[i].Trim().ToLower();
                            if (val == "replace" || val == "rpr" || val == "refinish" ||
                                val == "repair" || val == "r&i" || val == "blend" ||
                                val == "o/h" || val == "sublet")
                            {
                                opIndex = i;
                                break;
                            }
                        }

                        // Insert empty placeholders before the operation type
                        if (opIndex > 0)
                        {
                            for (int i = 0; i < missing; i++)
                            {
                                row.Insert(opIndex, "0");
                            }
                            System.Diagnostics.Debug.WriteLine($"[Parse] Added {missing} placeholder(s) before column {opIndex}");
                        }
                        else
                        {
                            // No operation type found, just pad at the end
                            while (row.Count < maxFields)
                            {
                                row.Add("");
                            }
                        }
                    }
                }
            }

            return rows;
        }

        /// <summary>
        /// Update status with a message
        /// </summary>
        private void UpdateStatusMessage(string message)
        {
            if (_statusText != null)
            {
                _statusText.Text = message;
            }
        }

        /// <summary>
        /// Update status with progress
        /// </summary>
        private void UpdateStatusProgress(int current, int total)
        {
            if (_statusText != null)
            {
                _statusText.Text = $"Pasting row {current} of {total}...";
            }
            if (_countText != null)
            {
                _countText.Text = $"{(current * 100 / total)}% complete";
            }
        }

        /// <summary>
        /// Called when smart paste completes
        /// </summary>
        private void OnSmartPasteCompleted(bool success)
        {
            _isTyping = false;
            if (_cccButton != null) _cccButton.IsEnabled = true;
            if (_cccWebButton != null) _cccWebButton.IsEnabled = true;
            if (_mitchellButton != null) _mitchellButton.IsEnabled = true;

            if (success)
            {
                UpdateStatusDone(_exportService.GetParsedOperations().Count, "CCC Desktop");
            }
        }

        /// <summary>
        /// Called by hotkey (Ctrl+Alt+V) - exports directly to current window
        /// No window switching needed since user pressed the hotkey while in CCC
        /// </summary>
        private async Task StartHotkeyExport()
        {
            if (_isTyping) return;
            _isTyping = true;
            _hotkeyService.IsExporting = true;

            try
            {
                // Disable buttons during typing
                if (_cccButton != null) _cccButton.IsEnabled = false;
                if (_cccWebButton != null) _cccWebButton.IsEnabled = false;
                if (_mitchellButton != null) _mitchellButton.IsEnabled = false;

                // Update status
                UpdateStatusTyping("CCC (hotkey)");

                // Get parsed operations
                var operations = _exportService.GetParsedOperations();
                int total = operations.Count;

                // Run paste-based export on STA thread
                // NO window switching - user is already in CCC when they pressed Ctrl+Alt+V
                await Task.Run(() =>
                {
                    var thread = new System.Threading.Thread(() =>
                    {
                        // Small delay to let Ctrl+Alt keys release
                        System.Threading.Thread.Sleep(50);

                        foreach (var op in operations)
                        {
                            // Paste each field with Tab between them
                            PasteField(op.Operation);
                            System.Windows.Forms.SendKeys.SendWait("{TAB}");

                            PasteField(op.Description);
                            System.Windows.Forms.SendKeys.SendWait("{TAB}");

                            PasteField(op.Qty);
                            System.Windows.Forms.SendKeys.SendWait("{TAB}");

                            PasteField(op.Price);
                            System.Windows.Forms.SendKeys.SendWait("{TAB}");

                            PasteField(op.Labor);
                            System.Windows.Forms.SendKeys.SendWait("{TAB}");

                            PasteField(op.Paint);

                            System.Windows.Forms.SendKeys.SendWait("{ENTER}");
                        }
                    });
                    thread.SetApartmentState(System.Threading.ApartmentState.STA);
                    thread.Start();
                    thread.Join();
                });

                // Record stats - assume CCC Desktop for hotkey
                _statsService.RecordExport(operations, "CCC Desktop");

                // Done!
                UpdateStatusDone(total, "CCC (hotkey)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkey export error: {ex.Message}");
                UpdateStatusError(ex.Message);
            }
            finally
            {
                _isTyping = false;
                _hotkeyService.IsExporting = false;
                if (_cccButton != null) _cccButton.IsEnabled = true;
                    if (_cccWebButton != null) _cccWebButton.IsEnabled = true;
                if (_mitchellButton != null) _mitchellButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Paste a field value using clipboard (much faster than typing)
        /// </summary>
        private void PasteField(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                System.Windows.Forms.Clipboard.SetText(value);
                System.Windows.Forms.SendKeys.SendWait("^v"); // Ctrl+V
            }
        }

        /// <summary>
        /// Force a window to foreground - bypasses Windows restrictions
        /// Uses ALT key trick that AutoHotkey and other automation tools use
        /// </summary>
        private static void ForceForegroundWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;

            uint currentThreadId = GetCurrentThreadId();
            uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);

            // Attach thread input to bypass Windows restrictions
            if (currentThreadId != targetThreadId)
            {
                AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            // Press and release ALT to unlock SetForegroundWindow
            keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);

            // Now activate the window
            ShowWindow(hWnd, SW_RESTORE);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);

            // Detach thread input
            if (currentThreadId != targetThreadId)
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }

        /// <summary>
        /// Get window title from handle
        /// </summary>
        private static string GetWindowTitle(IntPtr hWnd)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            return sb.ToString();
        }

        /// <summary>
        /// Find the target application window (CCC or Mitchell)
        /// </summary>
        private IntPtr FindTargetWindow(string target)
        {
            IntPtr foundWindow = IntPtr.Zero;
            var sb = new System.Text.StringBuilder(256);

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                GetWindowText(hWnd, sb, 256);
                string title = sb.ToString();

                if (target == "CCC")
                {
                    // Look for CCC ONE windows - adjust these patterns based on actual window titles
                    if (title.Contains("CCC ONE", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("CCC Estimating", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("CCC Intelligent", StringComparison.OrdinalIgnoreCase))
                    {
                        foundWindow = hWnd;
                        return false; // Stop enumeration
                    }
                }
                else if (target == "Mitchell")
                {
                    if (title.Contains("Mitchell", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("Ultramate", StringComparison.OrdinalIgnoreCase))
                    {
                        foundWindow = hWnd;
                        return false;
                    }
                }

                return true; // Continue enumeration
            }, IntPtr.Zero);

            return foundWindow;
        }

        private void UpdateStatusTyping(string target)
        {
            if (_statusBorder == null || _statusIcon == null || _statusText == null || _countText == null) return;

            _statusBorder.Background = new SolidColorBrush(Color.FromArgb(255, 0, 80, 150));
            _statusBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255));
            _statusIcon.Text = "⌨";
            _statusText.Text = $"Typing into {target}...";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255));
            _countText.Text = "Don't touch keyboard/mouse!";
            _countText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100));
        }

        private void UpdateStatusDone(int count, string target)
        {
            if (_statusBorder == null || _statusIcon == null || _statusText == null || _countText == null) return;

            _statusBorder.Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 50));
            _statusBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 220, 100));
            _statusIcon.Text = "✓";
            _statusText.Text = "Export Complete!";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 150));
            _countText.Text = $"{count} operations typed into {target}";
            _countText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
        }

        private void UpdateStatusError(string message)
        {
            if (_statusBorder == null || _statusIcon == null || _statusText == null || _countText == null) return;

            _statusBorder.Background = new SolidColorBrush(Color.FromArgb(255, 100, 30, 30));
            _statusBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 80, 80));
            _statusIcon.Text = "✗";
            _statusText.Text = "Error!";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100));
            _countText.Text = message;
            _countText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 150, 150));
        }

        private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_speedCombo == null) return;

            int tabDelay, enterDelay, initialDelay;
            switch (_speedCombo.SelectedIndex)
            {
                case 0: // Slow - for high CPU/memory usage
                    tabDelay = 50; enterDelay = 150; initialDelay = 500;
                    break;
                case 1: // Normal
                    tabDelay = 25; enterDelay = 75; initialDelay = 300;
                    break;
                case 2: // Fast
                    tabDelay = 15; enterDelay = 50; initialDelay = 200;
                    break;
                case 3: // Turbo - for fast machines
                    tabDelay = 8; enterDelay = 30; initialDelay = 150;
                    break;
                default:
                    tabDelay = 15; enterDelay = 50; initialDelay = 200;
                    break;
            }

            // Set delays on all services
            _exportService.SetDelays(tabDelay, enterDelay, initialDelay);
            _smartPasteService.SetDelays(tabDelay, enterDelay, initialDelay);
            _cccInsertService.SetDelays(tabDelay, enterDelay, initialDelay);
        }
    }
}
