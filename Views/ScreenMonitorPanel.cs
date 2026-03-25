#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using McstudDesktop.Models;
using McstudDesktop.Services;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Screen OCR Monitor panel.
    /// Captures screenshots of estimating windows (CCC ONE / Mitchell / Audatex),
    /// runs OCR to extract text, and parses estimate operations.
    /// </summary>
    public sealed class ScreenMonitorPanel : UserControl
    {
        private readonly ScreenMonitorService _monitorService;
        private readonly EstimateReferenceMatcherService _referenceMatcher;

        // Vehicle banner
        private Border? _vehicleBanner;
        private TextBlock? _vehicleInfoText;
        private TextBlock? _vinText;
        private Button? _resetVehicleButton;

        // Overview section
        private Border? _overviewSection;
        private StackPanel? _overviewContent;

        // Controls
        private ToggleSwitch? _monitorToggle;
        private ComboBox? _intervalCombo;
        private Button? _captureOnceButton;
        private Button? _clearButton;
        private Button? _feedToChatButton;
        private Button? _copyButton;
        private Button? _loadToBuilderButton;
        private Button? _transcriptButton;

        // Status display
        private TextBlock? _statusText;
        private TextBlock? _sourceText;
        private TextBlock? _captureCountText;
        private TextBlock? _changeCountText;
        private TextBlock? _lastChangeText;

        // Results display
        private TextBox? _rawTextBox;
        private Border? _rawTextSection;
        private bool _rawTextExpanded = false;
        private TextBlock? _refMatchStatusText;

        // Estimate Analysis (unified operations + coaching)
        private Border? _analysisSection;
        private StackPanel? _analysisContentStack;
        private TextBlock? _coachingScoreText;
        private TextBlock? _coachingGradeText;
        private TextBlock? _coachingPotentialText;
        private bool _sectionsCollapsed = false;
        private readonly List<(Border header, StackPanel content)> _collapsibleSections = new();

        // Diagnostics
        private StackPanel? _diagnosticStack;
        private Border? _diagnosticSection;

        // State
        private ScreenOcrResult? _latestResult;
        private CoachingSnapshot? _latestSnapshot;

        // Events
        public event EventHandler<ScreenOcrResult>? OnFeedToChat;
        public event EventHandler<ScreenOcrResult>? OnLoadToBuilder;
        public event EventHandler? OnNavigateToExport;
        public event EventHandler? OnNavigateToReference;

        public ScreenMonitorPanel()
        {
            _monitorService = ScreenMonitorService.Instance;
            _referenceMatcher = EstimateReferenceMatcherService.Instance;
            _monitorService.OcrResultReady += MonitorService_OcrResultReady;
            _monitorService.StatusChanged += MonitorService_StatusChanged;

            BuildUI();
        }

        private void BuildUI()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 32, 38)),
                Padding = new Thickness(16)
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var mainStack = new StackPanel { Spacing = 20 };

            mainStack.Children.Add(CreateHeader());
            mainStack.Children.Add(CreateReferenceTabBanner());
            mainStack.Children.Add(CreateControlsSection());
            mainStack.Children.Add(CreateStatusSection());
            mainStack.Children.Add(CreateEstimateAnalysisSection());
            mainStack.Children.Add(CreateDiagnosticSection());
            mainStack.Children.Add(CreateRawTextSection());
            mainStack.Children.Add(CreateActionsSection());

            // Subscribe to live coaching updates
            LiveCoachingService.Instance.SuggestionsUpdated += OnCoachingSuggestionsUpdated;

            scrollViewer.Content = mainStack;
            mainBorder.Child = scrollViewer;
            Content = mainBorder;
        }

        // === HEADER ===
        private StackPanel CreateHeader()
        {
            var headerPanel = new StackPanel { Spacing = 4 };

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            titleContent.Children.Add(new FontIcon
            {
                Glyph = "\uE7B3", // Screen/monitor icon
                FontSize = 28,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });
            titleContent.Children.Add(new TextBlock
            {
                Text = "Screen OCR Monitor",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            Grid.SetColumn(titleContent, 0);
            titleRow.Children.Add(titleContent);

            headerPanel.Children.Add(titleRow);

            headerPanel.Children.Add(new TextBlock
            {
                Text = "Captures screenshots of CCC ONE / Mitchell windows and extracts estimate data via OCR.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(40, 0, 0, 0)
            });

            return headerPanel;
        }

        private Border CreateReferenceTabBanner()
        {
            var banner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 45, 60)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 90, 120)),
                BorderThickness = new Thickness(1)
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

            row.Children.Add(new FontIcon
            {
                Glyph = "\uE82D",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 160, 220))
            });

            row.Children.Add(new TextBlock
            {
                Text = "P-Pages, DEG inquiries, and supporting documents for detected operations are available on the Reference tab.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 200, 220)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            var goBtn = new Button
            {
                Content = "Open Reference Tab",
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 70, 90)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 190, 240)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 100, 140)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };
            goBtn.Click += (s, e) => OnNavigateToReference?.Invoke(this, EventArgs.Empty);
            row.Children.Add(goBtn);

            banner.Child = row;
            return banner;
        }

        // === CONTROLS ===
        private Border CreateControlsSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Spacing = 14 };

            // Section title
            stack.Children.Add(new TextBlock
            {
                Text = "Controls",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Row: Toggle + Interval + Capture Once
            var controlsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

            // Monitor toggle
            var toggleStack = new StackPanel { Spacing = 4 };
            toggleStack.Children.Add(new TextBlock
            {
                Text = "Monitor",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            });
            _monitorToggle = new ToggleSwitch
            {
                IsOn = false,
                OnContent = "On",
                OffContent = "Off"
            };
            _monitorToggle.Toggled += MonitorToggle_Toggled;
            toggleStack.Children.Add(_monitorToggle);
            controlsRow.Children.Add(toggleStack);

            // Interval dropdown
            var intervalStack = new StackPanel { Spacing = 4 };
            intervalStack.Children.Add(new TextBlock
            {
                Text = "Interval",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            });
            _intervalCombo = new ComboBox
            {
                Width = 100,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                Foreground = new SolidColorBrush(Colors.White)
            };
            _intervalCombo.Items.Add("1 sec");
            _intervalCombo.Items.Add("2 sec");
            _intervalCombo.Items.Add("5 sec");
            _intervalCombo.Items.Add("10 sec");
            _intervalCombo.SelectedIndex = 1; // Default: 2 sec
            _intervalCombo.SelectionChanged += IntervalCombo_SelectionChanged;
            intervalStack.Children.Add(_intervalCombo);
            controlsRow.Children.Add(intervalStack);

            // Capture Once button
            _captureOnceButton = new Button
            {
                Content = "Capture Once",
                Padding = new Thickness(16, 8, 16, 8),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 130, 200)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2)
            };
            _captureOnceButton.Click += CaptureOnceButton_Click;
            controlsRow.Children.Add(_captureOnceButton);

            // Clear button
            _clearButton = new Button
            {
                Content = "Clear",
                Padding = new Thickness(12, 8, 12, 8),
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 65)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2)
            };
            _clearButton.Click += ClearButton_Click;
            controlsRow.Children.Add(_clearButton);

            stack.Children.Add(controlsRow);

            // Usage guidance (collapsible)
            var guidanceOuter = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 38, 50)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 6),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 70, 90)),
                BorderThickness = new Thickness(1)
            };
            var guidanceOuterStack = new StackPanel { Spacing = 4 };

            // Header row with title + minimize/expand button
            var guidanceHeaderRow = new Grid();
            guidanceHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            guidanceHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var guidanceTitle = new TextBlock
            {
                Text = "How to use",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 180, 230)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(guidanceTitle, 0);
            guidanceHeaderRow.Children.Add(guidanceTitle);

            var guidanceToggleBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE010", FontSize = 10 }, // minimize icon
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(4, 2, 4, 2),
                MinWidth = 24,
                MinHeight = 20,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(guidanceToggleBtn, 1);
            guidanceHeaderRow.Children.Add(guidanceToggleBtn);

            guidanceOuterStack.Children.Add(guidanceHeaderRow);

            // Collapsible content
            var guidanceContent = new StackPanel { Spacing = 6 };
            guidanceContent.Children.Add(new TextBlock
            {
                Text = "Reviewing an existing estimate?",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 180, 230)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            guidanceContent.Children.Add(new TextBlock
            {
                Text = "Turn on the monitor and scroll through your estimate in CCC/Mitchell. Operations will be detected as you go and missing items will be suggested.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 165, 175)),
                TextWrapping = TextWrapping.Wrap
            });
            guidanceContent.Children.Add(new TextBlock
            {
                Text = "Starting from scratch?",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 180, 230)),
                Margin = new Thickness(0, 4, 0, 0)
            });
            guidanceContent.Children.Add(new TextBlock
            {
                Text = "Begin writing your estimate — suggestions will populate based on the parts and operations you add.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 165, 175)),
                TextWrapping = TextWrapping.Wrap
            });
            guidanceOuterStack.Children.Add(guidanceContent);

            // Toggle visibility on button click
            guidanceToggleBtn.Click += (s, e) =>
            {
                if (guidanceContent.Visibility == Visibility.Visible)
                {
                    guidanceContent.Visibility = Visibility.Collapsed;
                    ((FontIcon)guidanceToggleBtn.Content).Glyph = "\uE011"; // expand icon
                }
                else
                {
                    guidanceContent.Visibility = Visibility.Visible;
                    ((FontIcon)guidanceToggleBtn.Content).Glyph = "\uE010"; // minimize icon
                }
            };

            guidanceOuter.Child = guidanceOuterStack;
            stack.Children.Add(guidanceOuter);

            // OCR availability warning
            if (!_monitorService.IsOcrAvailable)
            {
                var warningBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 80, 60, 20)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                warningBorder.Child = new TextBlock
                {
                    Text = "OCR engine is not available. Ensure a Windows language pack with OCR support is installed.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                    TextWrapping = TextWrapping.Wrap
                };
                stack.Children.Add(warningBorder);
            }

            border.Child = stack;
            return border;
        }

        // === STATUS BAR ===
        private Border CreateStatusSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Source
            var sourceStack = new StackPanel { Spacing = 2 };
            sourceStack.Children.Add(new TextBlock
            {
                Text = "SOURCE",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            _sourceText = new TextBlock
            {
                Text = "--",
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White)
            };
            sourceStack.Children.Add(_sourceText);
            Grid.SetColumn(sourceStack, 0);
            grid.Children.Add(sourceStack);

            // Captures
            var captureStack = new StackPanel { Spacing = 2 };
            captureStack.Children.Add(new TextBlock
            {
                Text = "CAPTURES",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            _captureCountText = new TextBlock
            {
                Text = "0",
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White)
            };
            captureStack.Children.Add(_captureCountText);
            Grid.SetColumn(captureStack, 1);
            grid.Children.Add(captureStack);

            // Changes
            var changeStack = new StackPanel { Spacing = 2 };
            changeStack.Children.Add(new TextBlock
            {
                Text = "CHANGES",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            _changeCountText = new TextBlock
            {
                Text = "0",
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White)
            };
            changeStack.Children.Add(_changeCountText);
            Grid.SetColumn(changeStack, 2);
            grid.Children.Add(changeStack);

            // Last Change
            var lastChangeStack = new StackPanel { Spacing = 2 };
            lastChangeStack.Children.Add(new TextBlock
            {
                Text = "LAST CHANGE",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            _lastChangeText = new TextBlock
            {
                Text = "--",
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White)
            };
            lastChangeStack.Children.Add(_lastChangeText);
            Grid.SetColumn(lastChangeStack, 3);
            grid.Children.Add(lastChangeStack);

            // Status text below
            var outerStack = new StackPanel { Spacing = 8 };
            outerStack.Children.Add(grid);

            _statusText = new TextBlock
            {
                Text = "Ready - click 'Capture Once' or toggle monitoring on",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 180, 130)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            };
            outerStack.Children.Add(_statusText);

            border.Child = outerStack;
            return border;
        }

        // === ESTIMATE ANALYSIS (unified operations + coaching) ===
        private Border CreateEstimateAnalysisSection()
        {
            _analysisSection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 45)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };

            var outerStack = new StackPanel { Spacing = 10 };

            // --- Vehicle Banner ---
            _vehicleBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 40, 55)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 80, 110)),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed
            };
            var vehicleBannerGrid = new Grid();
            vehicleBannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            vehicleBannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var vehicleInfoStack = new StackPanel { Spacing = 2 };
            _vehicleInfoText = new TextBlock
            {
                Text = "",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 200, 255))
            };
            vehicleInfoStack.Children.Add(_vehicleInfoText);
            _vinText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 140, 180)),
                Visibility = Visibility.Collapsed
            };
            vehicleInfoStack.Children.Add(_vinText);
            Grid.SetColumn(vehicleInfoStack, 0);
            vehicleBannerGrid.Children.Add(vehicleInfoStack);

            _resetVehicleButton = new Button
            {
                Content = "Clear Vehicle",
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 65)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center
            };
            _resetVehicleButton.Click += (s, e) =>
            {
                LiveCoachingService.Instance.ResetVehicle();
                _latestSnapshot = null;
                RebuildAnalysisDisplay();
            };
            Grid.SetColumn(_resetVehicleButton, 1);
            vehicleBannerGrid.Children.Add(_resetVehicleButton);

            _vehicleBanner.Child = vehicleBannerGrid;
            outerStack.Children.Add(_vehicleBanner);

            // --- Score banner row ---
            var scoreBanner = new Grid();
            scoreBanner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            scoreBanner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            scoreBanner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scoreBanner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            scoreBanner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _coachingGradeText = new TextBlock
            {
                Text = "--",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(_coachingGradeText, 0);

            _coachingScoreText = new TextBlock
            {
                Text = "",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(_coachingScoreText, 1);

            var sectionTitle = new TextBlock
            {
                Text = "Estimate Analysis",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(sectionTitle, 2);

            _coachingPotentialText = new TextBlock
            {
                Text = "",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 214, 0)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_coachingPotentialText, 3);

            var sopSettingsButton = new Button
            {
                Content = new FontIcon
                {
                    Glyph = "\uE713",
                    FontSize = 14
                },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(6),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            ToolTipService.SetToolTip(sopSettingsButton, "SOP Baseline Settings");
            sopSettingsButton.Click += async (s, e) => await ShowSOPBaselineEditor();
            Grid.SetColumn(sopSettingsButton, 4);

            scoreBanner.Children.Add(_coachingGradeText);
            scoreBanner.Children.Add(_coachingScoreText);
            scoreBanner.Children.Add(sectionTitle);
            scoreBanner.Children.Add(_coachingPotentialText);
            scoreBanner.Children.Add(sopSettingsButton);
            outerStack.Children.Add(scoreBanner);

            // --- Overview section (collapsible) ---
            _overviewSection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 40, 52)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 45, 65, 85)),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed
            };
            _overviewContent = new StackPanel { Spacing = 6 };
            _overviewSection.Child = _overviewContent;
            outerStack.Children.Add(_overviewSection);

            // --- Content area ---
            _analysisContentStack = new StackPanel { Spacing = 4 };
            _analysisContentStack.Children.Add(new TextBlock
            {
                Text = "No operations detected yet. Capture a screenshot to begin.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
            outerStack.Children.Add(_analysisContentStack);

            // Reference match status
            _refMatchStatusText = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0)
            };
            outerStack.Children.Add(_refMatchStatusText);

            _analysisSection.Child = outerStack;
            return _analysisSection;
        }

        // === RAW TEXT (expandable) ===
        private Border CreateRawTextSection()
        {
            _rawTextSection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Spacing = 10 };

            // Expandable header
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var expandButton = new Button
            {
                Content = "\u25B6 Raw OCR Text",
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            expandButton.Click += (s, e) =>
            {
                _rawTextExpanded = !_rawTextExpanded;
                expandButton.Content = _rawTextExpanded ? "\u25BC Raw OCR Text" : "\u25B6 Raw OCR Text";
                if (_rawTextBox != null)
                    _rawTextBox.Visibility = _rawTextExpanded ? Visibility.Visible : Visibility.Collapsed;
            };
            headerRow.Children.Add(expandButton);
            stack.Children.Add(headerRow);

            _rawTextBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 120,
                MaxHeight = 400,
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 28, 34)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                FontSize = 11,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                Visibility = Visibility.Collapsed,
                Text = "(no OCR text captured yet)"
            };
            stack.Children.Add(_rawTextBox);

            _rawTextSection.Child = stack;
            return _rawTextSection;
        }

        // === ACTION BUTTONS ===
        private Border CreateActionsSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            _feedToChatButton = new Button
            {
                Content = "Feed to Chat",
                Padding = new Thickness(20, 10, 20, 10),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 130, 200)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                IsEnabled = false
            };
            _feedToChatButton.Click += FeedToChatButton_Click;
            row.Children.Add(_feedToChatButton);

            _copyButton = new Button
            {
                Content = "Copy to Clipboard",
                Padding = new Thickness(20, 10, 20, 10),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 65)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                IsEnabled = false
            };
            _copyButton.Click += CopyButton_Click;
            row.Children.Add(_copyButton);

            _loadToBuilderButton = new Button
            {
                Content = "Load to Builder",
                Padding = new Thickness(20, 10, 20, 10),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromArgb(255, 100, 160, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                IsEnabled = false
            };
            _loadToBuilderButton.Click += LoadToBuilderButton_Click;
            row.Children.Add(_loadToBuilderButton);

            var transcriptButton = new Button
            {
                Content = "Copy Transcript",
                Padding = new Thickness(20, 10, 20, 10),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromArgb(255, 130, 90, 160)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                IsEnabled = false
            };
            transcriptButton.Click += TranscriptButton_Click;
            _transcriptButton = transcriptButton;
            row.Children.Add(transcriptButton);

            border.Child = row;
            return border;
        }

        private async Task ShowSOPBaselineEditor()
        {
            var service = SOPBaselineService.Instance;
            var allItems = service.GetAllItems();

            var dialogContent = new StackPanel { Spacing = 12, MinWidth = 450 };

            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 400,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var itemsStack = new StackPanel { Spacing = 4 };

            // Group by section
            var sections = new[] { "Electrical", "Vehicle Diagnostics", "Miscellaneous", "Custom" };
            foreach (var section in sections)
            {
                var sectionItems = allItems.Where(i =>
                    section == "Custom" ? i.IsCustom : (!i.IsCustom && i.Section == section)).ToList();
                if (sectionItems.Count == 0) continue;

                itemsStack.Children.Add(new TextBlock
                {
                    Text = section,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                    Margin = new Thickness(0, 8, 0, 2)
                });

                foreach (var item in sectionItems)
                {
                    var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var capturedId = item.Id;
                    var cb = new CheckBox
                    {
                        IsChecked = item.IsEnabled,
                        MinWidth = 0,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    cb.Checked += (s, e) => service.SetItemEnabled(capturedId, true);
                    cb.Unchecked += (s, e) => service.SetItemEnabled(capturedId, false);
                    Grid.SetColumn(cb, 0);

                    var nameBlock = new TextBlock
                    {
                        Text = item.Name,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Grid.SetColumn(nameBlock, 1);

                    var costBlock = new TextBlock
                    {
                        Text = item.EstimatedCost > 0 ? $"${item.EstimatedCost:F0}" : "",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    Grid.SetColumn(costBlock, 2);

                    row.Children.Add(cb);
                    row.Children.Add(nameBlock);
                    row.Children.Add(costBlock);

                    if (item.IsCustom)
                    {
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        var removeBtn = new Button
                        {
                            Content = new FontIcon { Glyph = "\uE74D", FontSize = 11 },
                            Background = new SolidColorBrush(Colors.Transparent),
                            BorderBrush = new SolidColorBrush(Colors.Transparent),
                            Padding = new Thickness(4),
                            Margin = new Thickness(4, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        ToolTipService.SetToolTip(removeBtn, "Remove custom item");
                        var capturedRow = row;
                        removeBtn.Click += (s, e) =>
                        {
                            service.RemoveCustomItem(capturedId);
                            itemsStack.Children.Remove(capturedRow);
                        };
                        Grid.SetColumn(removeBtn, 3);
                        row.Children.Add(removeBtn);
                    }

                    itemsStack.Children.Add(row);
                }
            }

            scrollViewer.Content = itemsStack;
            dialogContent.Children.Add(scrollViewer);

            // Add custom item section
            var addSection = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            addSection.Children.Add(new TextBlock
            {
                Text = "Add Custom Item",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            });

            var addRow = new Grid();
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameInput = new TextBox
            {
                PlaceholderText = "Item name...",
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(nameInput, 0);

            var sectionCombo = new ComboBox
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0),
                MinWidth = 130
            };
            sectionCombo.Items.Add("Electrical");
            sectionCombo.Items.Add("Vehicle Diagnostics");
            sectionCombo.Items.Add("Miscellaneous");
            sectionCombo.SelectedIndex = 2;
            Grid.SetColumn(sectionCombo, 1);

            var addButton = new Button
            {
                Content = "Add",
                FontSize = 12,
                Padding = new Thickness(12, 4, 12, 4)
            };
            addButton.Click += (s, e) =>
            {
                var name = nameInput.Text?.Trim();
                if (string.IsNullOrEmpty(name)) return;

                var newItem = new SOPBaselineItem
                {
                    Name = name,
                    Section = sectionCombo.SelectedItem?.ToString() ?? "Miscellaneous",
                    Description = name,
                    WhyNeeded = "Custom SOP item added by user.",
                    Severity = "Medium",
                    IsCustom = true
                };
                service.AddCustomItem(newItem);

                // Add to the UI list
                var newRow = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                newRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                newRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var newCb = new CheckBox { IsChecked = true, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
                var capturedNewId = newItem.Id;
                newCb.Checked += (s2, e2) => service.SetItemEnabled(capturedNewId, true);
                newCb.Unchecked += (s2, e2) => service.SetItemEnabled(capturedNewId, false);
                Grid.SetColumn(newCb, 0);

                var newNameBlock = new TextBlock
                {
                    Text = name,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(newNameBlock, 1);

                newRow.Children.Add(newCb);
                newRow.Children.Add(newNameBlock);
                itemsStack.Children.Add(newRow);

                nameInput.Text = "";
            };
            Grid.SetColumn(addButton, 2);

            addRow.Children.Add(nameInput);
            addRow.Children.Add(sectionCombo);
            addRow.Children.Add(addButton);
            addSection.Children.Add(addRow);
            dialogContent.Children.Add(addSection);

            var dialog = new ContentDialog
            {
                Title = "SOP Baseline Settings",
                Content = dialogContent,
                PrimaryButtonText = "Done",
                SecondaryButtonText = "Reset Defaults",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                service.ResetToDefaults();
            }
        }

        private void OnCoachingSuggestionsUpdated(object? sender, McstudDesktop.Models.CoachingSnapshot snapshot)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    _latestSnapshot = snapshot;
                    RebuildAnalysisDisplay();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ScreenMonitor] Analysis display error: {ex.Message}");
                }
            });
        }

        private void RebuildAnalysisDisplay()
        {
            if (_analysisContentStack == null) return;

            var snapshot = _latestSnapshot;
            var result = _latestResult;

            // Update score banner from coaching snapshot
            if (snapshot != null)
            {
                _coachingGradeText!.Text = string.IsNullOrEmpty(snapshot.Grade) ? "--" : snapshot.Grade;
                _coachingGradeText.Foreground = new SolidColorBrush(GetGradeColor(snapshot.Grade));
                _coachingScoreText!.Text = $"{snapshot.Score}/100";
                _coachingPotentialText!.Text = snapshot.PotentialRecovery > 0 ? $"+${snapshot.PotentialRecovery:F0} potential" : "";
            }

            // --- Vehicle Banner ---
            if (_vehicleBanner != null)
            {
                var hasVehicle = snapshot != null && (!string.IsNullOrEmpty(snapshot.VehicleInfo) || !string.IsNullOrEmpty(snapshot.VIN));
                _vehicleBanner.Visibility = hasVehicle ? Visibility.Visible : Visibility.Collapsed;
                if (hasVehicle)
                {
                    var infoText = snapshot!.VehicleInfo ?? "";
                    if (!string.IsNullOrEmpty(snapshot.CustomerName))
                        infoText += (infoText.Length > 0 ? "  |  " : "") + snapshot.CustomerName;
                    _vehicleInfoText!.Text = infoText;

                    if (!string.IsNullOrEmpty(snapshot.VIN))
                    {
                        _vinText!.Text = $"VIN: {snapshot.VIN}";
                        _vinText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _vinText!.Visibility = Visibility.Collapsed;
                    }
                }
            }

            _analysisContentStack.Children.Clear();

            var accumulatedOps = LiveCoachingService.Instance.AccumulatedOperations;
            var hasOps = accumulatedOps.Count > 0;
            var hasCoaching = snapshot != null && snapshot.Suggestions.Count > 0;

            // If no data at all, show placeholder
            if (!hasOps && !hasCoaching)
            {
                _overviewSection!.Visibility = Visibility.Collapsed;
                if (result != null && result.LineCount > 0)
                {
                    var noOpsMessage = result.SourceWindow == "Full Screen"
                        ? $"Read {result.LineCount} lines but no estimate operations found. Make sure your estimating software (CCC ONE, Mitchell) is open and showing line items."
                        : $"Read {result.LineCount} lines from \"{result.SourceWindow}\" but no estimate operations detected. Navigate to the estimate line items / operations page.";
                    _analysisContentStack.Children.Add(new TextBlock
                    {
                        Text = noOpsMessage,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 140, 100)),
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    _analysisContentStack.Children.Add(new TextBlock
                    {
                        Text = "No operations detected yet. Capture a screenshot to begin.",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                        FontStyle = Windows.UI.Text.FontStyle.Italic
                    });
                }
                return;
            }

            // --- Overview Section ---
            _overviewSection!.Visibility = Visibility.Visible;
            _overviewContent!.Children.Clear();

            // Summary line
            if (hasCoaching)
            {
                var allActive = snapshot!.Suggestions.Where(s => !s.IsDismissed).ToList();
                var missing = allActive.Where(s => !s.IsConfirmedOnEstimate).ToList();
                var confirmed = allActive.Where(s => s.IsConfirmedOnEstimate).ToList();

                if (missing.Count > 0)
                {
                    var critical = missing.Count(s => s.Severity == CoachingSeverity.Critical);
                    var high = missing.Count(s => s.Severity == CoachingSeverity.High);
                    var summaryParts = new List<string>();
                    if (critical > 0) summaryParts.Add($"{critical} critical");
                    if (high > 0) summaryParts.Add($"{high} high priority");
                    var other = missing.Count - critical - high;
                    if (other > 0) summaryParts.Add($"{other} other");

                    _overviewContent.Children.Add(new TextBlock
                    {
                        Text = $"Missing: {string.Join(", ", summaryParts)}    |    On estimate: {accumulatedOps.Count}",
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 240, 190, 80))
                    });
                }
                else if (confirmed.Count > 0)
                {
                    _overviewContent.Children.Add(new TextBlock
                    {
                        Text = $"All {confirmed.Count} suggested operations found on estimate!",
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 200, 120))
                    });
                }
            }

            // Focused part card
            if (snapshot?.FocusedPart != null)
            {
                var focusedCard = BuildFocusedPartCard(snapshot.FocusedPart, snapshot.VehicleInfo);
                if (focusedCard != null)
                    _overviewContent.Children.Add(focusedCard);
            }

            _collapsibleSections.Clear();

            // === MUST-HAVE OPERATIONS CHECKLIST ===
            var mustHaveChecklist = new List<MustHaveChecklistItem>();
            try
            {
                mustHaveChecklist = BuildScreenMustHaveChecklist(accumulatedOps);

                if (mustHaveChecklist.Count > 0)
                {
                    var presentCount = mustHaveChecklist.Count(m => m.IsPresent);
                    var missingCount = mustHaveChecklist.Count(m => !m.IsPresent);

                    var mustHaveHeader = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 28, 35, 45)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 4, 0, 2),
                        BorderBrush = new SolidColorBrush(missingCount > 0
                            ? Color.FromArgb(255, 180, 120, 40)
                            : Color.FromArgb(255, 50, 140, 70)),
                        BorderThickness = new Thickness(1)
                    };

                    var mustHaveStack = new StackPanel { Spacing = 4 };

                    // Title row with score
                    var titleRow = new Grid();
                    titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var titleText = new TextBlock
                    {
                        Text = "MUST-HAVE OPERATIONS",
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 200, 140))
                    };
                    Grid.SetColumn(titleText, 0);
                    titleRow.Children.Add(titleText);

                    var scoreText = new TextBlock
                    {
                        Text = $"{presentCount}/{mustHaveChecklist.Count}",
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(missingCount > 0
                            ? Color.FromArgb(255, 240, 180, 80)
                            : Color.FromArgb(255, 80, 200, 120))
                    };
                    Grid.SetColumn(scoreText, 1);
                    titleRow.Children.Add(scoreText);
                    mustHaveStack.Children.Add(titleRow);

                    // Group by category
                    var grouped = mustHaveChecklist.GroupBy(c => c.Category).ToList();
                    foreach (var group in grouped)
                    {
                        mustHaveStack.Children.Add(new TextBlock
                        {
                            Text = group.Key,
                            FontSize = 10,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 160)),
                            Margin = new Thickness(0, 4, 0, 1)
                        });

                        foreach (var item in group)
                        {
                            var itemRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

                            itemRow.Children.Add(new TextBlock
                            {
                                Text = item.IsPresent ? "\u2705" : "\u274C",
                                FontSize = 11,
                                VerticalAlignment = VerticalAlignment.Center
                            });

                            itemRow.Children.Add(new TextBlock
                            {
                                Text = item.Description,
                                FontSize = 11,
                                Foreground = new SolidColorBrush(item.IsPresent
                                    ? Color.FromArgb(255, 100, 200, 120)
                                    : Color.FromArgb(255, 220, 140, 80)),
                                TextDecorations = item.IsPresent ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
                                VerticalAlignment = VerticalAlignment.Center
                            });

                            var detail = item.LaborHours > 0 ? $"{item.LaborHours:N1}h" : $"${item.Price:N2}";
                            itemRow.Children.Add(new TextBlock
                            {
                                Text = $"({item.OperationType}, {detail})",
                                FontSize = 9,
                                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 110)),
                                VerticalAlignment = VerticalAlignment.Center
                            });

                            mustHaveStack.Children.Add(itemRow);
                        }
                    }

                    // Recovery potential
                    if (missingCount > 0)
                    {
                        var missingItems = mustHaveChecklist.Where(m => !m.IsPresent).ToList();
                        var missingHours = missingItems.Sum(m => m.LaborHours);
                        var missingDollars = missingItems.Sum(m => m.Price) + missingItems.Sum(m => m.MaterialsCost);

                        var recoveryParts = new List<string>();
                        if (missingHours > 0) recoveryParts.Add($"{missingHours:N1}h labor");
                        if (missingDollars > 0) recoveryParts.Add($"${missingDollars:N2}");

                        if (recoveryParts.Count > 0)
                        {
                            mustHaveStack.Children.Add(new TextBlock
                            {
                                Text = $"Potential recovery: {string.Join(" + ", recoveryParts)}",
                                FontSize = 10,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                Foreground = new SolidColorBrush(Color.FromArgb(255, 240, 180, 80)),
                                Margin = new Thickness(0, 4, 0, 0)
                            });
                        }
                    }

                    mustHaveHeader.Child = mustHaveStack;
                    _analysisContentStack.Children.Add(mustHaveHeader);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenMonitor] Must-have checklist error: {ex.Message}");
            }

            // Prepare data for all 3 sections
            var missingSuggestions = hasCoaching
                ? snapshot!.Suggestions.Where(s => !s.IsDismissed && !s.IsConfirmedOnEstimate).ToList()
                : new List<CoachingSuggestion>();
            var addedSuggestions = hasCoaching
                ? snapshot!.Suggestions.Where(s => !s.IsDismissed && s.IsConfirmedOnEstimate).ToList()
                : new List<CoachingSuggestion>();
            var filteredOps = accumulatedOps.Where(op => IsValidEstimateOperation(op)).ToList();

            // Filter out coaching suggestions that are already covered by the must-have checklist
            // to avoid showing the same item in both places
            if (mustHaveChecklist.Count > 0)
            {
                var mustHaveNames = mustHaveChecklist
                    .Select(m => m.Description.ToLowerInvariant())
                    .ToHashSet();

                bool IsCoveredByMustHave(string title)
                {
                    var titleLower = title.ToLowerInvariant().Trim();
                    foreach (var mh in mustHaveNames)
                    {
                        if (titleLower.Contains(mh) || mh.Contains(titleLower))
                            return true;
                        // Check significant word overlap
                        var titleWords = titleLower.Split(' ', '/', '-').Where(w => w.Length > 3).ToArray();
                        var mhWords = mh.Split(' ', '/', '-').Where(w => w.Length > 3).ToArray();
                        if (titleWords.Length > 0 && mhWords.Length > 0)
                        {
                            var overlap = titleWords.Count(tw => mhWords.Any(mw => mw.Contains(tw) || tw.Contains(mw)));
                            if (overlap >= Math.Min(2, titleWords.Length))
                                return true;
                        }
                    }
                    return false;
                }

                missingSuggestions = missingSuggestions.Where(s => !IsCoveredByMustHave(s.Title)).ToList();
                addedSuggestions = addedSuggestions.Where(s => !IsCoveredByMustHave(s.Title)).ToList();
            }

            // Confirmed suggestions lookup for inline checkmarks on estimate ops
            var confirmedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in addedSuggestions)
                confirmedTitles.Add(s.Title);

            // Dedup missing suggestions by base title
            var dedupedMissing = new List<CoachingSuggestion>();
            var seenBaseOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in missingSuggestions.OrderByDescending(s => s.Severity).ThenByDescending(s => s.EstimatedCost))
            {
                if (seenBaseOps.Add(s.Title.Trim()))
                    dedupedMissing.Add(s);
            }

            // For door suggestions triggered by both front/rear parts, duplicate into both sections
            var expandedMissing = new List<(string Section, CoachingSuggestion Suggestion)>();
            foreach (var s in dedupedMissing)
            {
                var sections = GetAllCccSections(s);
                foreach (var sec in sections)
                    expandedMissing.Add((sec, s));
            }

            // Similarly expand added suggestions
            var expandedAdded = new List<(string Section, CoachingSuggestion Suggestion)>();
            var seenAddedOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in addedSuggestions.OrderByDescending(s => s.Severity).ThenByDescending(s => s.EstimatedCost))
            {
                if (!seenAddedOps.Add(s.Title.Trim())) continue;
                var sections = GetAllCccSections(s);
                foreach (var sec in sections)
                    expandedAdded.Add((sec, s));
            }

            // Group estimate ops by section
            var opsGrouped = filteredOps
                .GroupBy(op => GetCccSectionForOp(op))
                .ToDictionary(g => g.Key, g => g.ToList());

            // Group missing/added by section
            var missingGrouped = expandedMissing
                .GroupBy(x => x.Section)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Suggestion).ToList());
            var addedGrouped = expandedAdded
                .GroupBy(x => x.Section)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Suggestion).ToList());

            // Always-show sections (even when empty)
            var alwaysShowSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ELECTRICAL", "VEHICLE DIAGNOSTICS", "MISCELLANEOUS OPERATIONS"
            };

            // Determine which sections have any content
            var activeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in missingGrouped.Keys) activeSections.Add(key);
            foreach (var key in addedGrouped.Keys) activeSections.Add(key);
            foreach (var key in opsGrouped.Keys) activeSections.Add(key);
            foreach (var s in alwaysShowSections) activeSections.Add(s);

            // === RENDER ALL SECTIONS IN CCC ORDER ===
            var orderedSections = _cccSectionOrder
                .Where(s => activeSections.Contains(s))
                .ToList();

            // Section title for the CCC breakdown area
            if (orderedSections.Count > 0)
            {
                var totalMissing = missingSuggestions.Count;
                var sectionTitleRow = new Grid { Margin = new Thickness(0, 8, 0, 2) };
                sectionTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                sectionTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var sectionTitle = new TextBlock
                {
                    Text = "COACHING SUGGESTIONS & ESTIMATE OPERATIONS",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 200)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(sectionTitle, 0);
                sectionTitleRow.Children.Add(sectionTitle);

                if (totalMissing > 0)
                {
                    var missingBadge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 180, 80, 30)),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(8, 2, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    missingBadge.Child = new TextBlock
                    {
                        Text = $"{totalMissing} missing",
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.White)
                    };
                    Grid.SetColumn(missingBadge, 1);
                    sectionTitleRow.Children.Add(missingBadge);
                }

                _analysisContentStack.Children.Add(sectionTitleRow);
            }

            foreach (var sectionName in orderedSections)
            {
                var sectionColor = _cccSectionColors.TryGetValue(sectionName, out var c) ? c : Color.FromArgb(255, 150, 150, 150);
                var hasMissing = missingGrouped.TryGetValue(sectionName, out var sectionMissing) && sectionMissing.Count > 0;
                var hasAdded = addedGrouped.TryGetValue(sectionName, out var sectionAdded) && sectionAdded.Count > 0;
                var hasEstOps = opsGrouped.TryGetValue(sectionName, out var sectionOps) && sectionOps.Count > 0;
                var totalCount = (sectionMissing?.Count ?? 0) + (sectionAdded?.Count ?? 0) + (sectionOps?.Count ?? 0);

                // Section header (clickable to collapse/expand)
                var contentPanel = new StackPanel { Spacing = 1 };
                var headerBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 6, 0, 0)
                };

                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var headerLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                headerLeft.Children.Add(new Border
                {
                    Background = new SolidColorBrush(sectionColor),
                    CornerRadius = new CornerRadius(2),
                    Width = 4,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center
                });
                headerLeft.Children.Add(new TextBlock
                {
                    Text = sectionName,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(sectionColor),
                    VerticalAlignment = VerticalAlignment.Center
                });

                // Missing count badge
                if (hasMissing)
                {
                    var missingBadge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 180, 80, 30)),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(6, 1, 6, 1),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    missingBadge.Child = new TextBlock
                    {
                        Text = $"{sectionMissing!.Count} missing",
                        FontSize = 9,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.White)
                    };
                    headerLeft.Children.Add(missingBadge);
                }

                Grid.SetColumn(headerLeft, 0);
                headerGrid.Children.Add(headerLeft);

                // Right side: total count
                var countText = new TextBlock
                {
                    Text = totalCount > 0 ? $"{totalCount}" : "",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 130)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(countText, 1);
                headerGrid.Children.Add(countText);

                headerBorder.Child = headerGrid;

                // Make header clickable to toggle section
                var capturedContent = contentPanel;
                headerBorder.PointerPressed += (s, e) =>
                {
                    capturedContent.Visibility = capturedContent.Visibility == Visibility.Visible
                        ? Visibility.Collapsed : Visibility.Visible;
                };

                _analysisContentStack.Children.Add(headerBorder);
                _collapsibleSections.Add((headerBorder, contentPanel));

                // --- Section content ---

                // Missing suggestions for this section
                if (hasMissing)
                {
                    foreach (var suggestion in sectionMissing!.OrderByDescending(s => s.Severity).ThenByDescending(s => s.EstimatedCost))
                        contentPanel.Children.Add(BuildCoachingSuggestionCard(suggestion));
                }

                // Added (confirmed on estimate) suggestions for this section
                if (hasAdded)
                {
                    foreach (var suggestion in sectionAdded!.OrderByDescending(s => s.Severity).ThenByDescending(s => s.EstimatedCost))
                        contentPanel.Children.Add(BuildCoachingSuggestionCard(suggestion));
                }

                // Estimate operations for this section
                if (hasEstOps)
                {
                    foreach (var op in sectionOps!)
                    {
                        var row = CreateAccumulatedOperationRow(op, confirmedTitles);
                        contentPanel.Children.Add(row);
                    }
                }

                // Empty section placeholder
                if (!hasMissing && !hasAdded && !hasEstOps)
                {
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = "No operations detected",
                        FontSize = 10,
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 90, 90, 100)),
                        Margin = new Thickness(16, 3, 0, 3)
                    });
                }

                contentPanel.Visibility = _sectionsCollapsed ? Visibility.Collapsed : Visibility.Visible;
                _analysisContentStack.Children.Add(contentPanel);
            }

            // Dismiss All button (at bottom if any missing suggestions exist)
            if (dedupedMissing.Count > 0)
            {
                var dismissAllBtn = new Button
                {
                    Content = "Dismiss All Suggestions",
                    Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 65)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                    Padding = new Thickness(12, 6, 12, 6),
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    FontSize = 11,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                var allForDismiss = missingSuggestions;
                dismissAllBtn.Click += (s, e) =>
                {
                    foreach (var suggestion in allForDismiss)
                        LiveCoachingService.Instance.DismissSuggestion(suggestion.Id);
                    RebuildAnalysisDisplay();
                };
                _analysisContentStack.Children.Add(dismissAllBtn);
            }
        }

        private Border? BuildFocusedPartCard(FocusedPartContext focusedPart, string? vehicleInfo)
        {
            try
            {
                var smartService = SmartSuggestionService.Instance;
                var suggestions = smartService.GetSuggestionsForPart(focusedPart.PartName, focusedPart.OperationType, vehicleInfo);

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 45, 38)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 80, 60)),
                    BorderThickness = new Thickness(1)
                };

                var cardStack = new StackPanel { Spacing = 4 };

                var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                headerRow.Children.Add(new FontIcon
                {
                    Glyph = "\uE7B3",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 140))
                });
                headerRow.Children.Add(new TextBlock
                {
                    Text = $"Currently viewing: {focusedPart.PartName}",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 140)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                if (suggestions.HasData)
                {
                    headerRow.Children.Add(new TextBlock
                    {
                        Text = $"based on {suggestions.MatchCount} estimates",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 140)),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
                cardStack.Children.Add(headerRow);

                // Show top suggestions for this part
                if (suggestions.HasData)
                {
                    foreach (var sugOp in suggestions.ManualOperations.Take(5))
                    {
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(20, 1, 0, 1) };
                        row.Children.Add(new TextBlock
                        {
                            Text = "\u2022",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 140)),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        row.Children.Add(new TextBlock
                        {
                            Text = sugOp.Description,
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });
                        if (sugOp.TimesUsed > 0)
                        {
                            row.Children.Add(new TextBlock
                            {
                                Text = $"{sugOp.TimesUsed}/{suggestions.MatchCount}",
                                FontSize = 9,
                                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 160, 220)),
                                VerticalAlignment = VerticalAlignment.Center
                            });
                        }
                        cardStack.Children.Add(row);
                    }
                }

                // Also check Excel tool for focused part
                var excelProvider = ExcelGhostDataProvider.Instance;
                var excelLookup = excelProvider.LookupForGhost(focusedPart.PartName, focusedPart.OperationType);
                if (excelLookup.Found)
                {
                    var excelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(20, 1, 0, 1) };
                    excelRow.Children.Add(new TextBlock
                    {
                        Text = "\u2022",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    var excelDesc = $"[Excel] {focusedPart.OperationType} {focusedPart.PartName}";
                    if (excelLookup.LaborHours > 0) excelDesc += $" — {excelLookup.LaborHours:G}h";
                    excelRow.Children.Add(new TextBlock
                    {
                        Text = excelDesc,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    cardStack.Children.Add(excelRow);
                }

                card.Child = cardStack;
                return card;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenMonitor] Focused part card error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Normalize text for matching: lowercase, replace & with and, / with space, collapse whitespace.
        /// This handles canonical name differences like "battery disconnect/reconnect" vs "disconnect and reconnect battery".
        /// </summary>
        private static string NormalizeForMatch(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return System.Text.RegularExpressions.Regex.Replace(
                text.ToLowerInvariant()
                    .Replace("&", " and ")
                    .Replace("/", " ")
                    .Replace("-", " "),
                @"\s+", " ").Trim();
        }

        /// <summary>
        /// Extract significant words (> 3 chars) from normalized text for word-overlap matching.
        /// </summary>
        private static string[] ExtractSignificantWords(string normalizedText)
        {
            return normalizedText.Split(' ')
                .Where(w => w.Length > 3)
                .Distinct()
                .ToArray();
        }

        /// <summary>
        /// Cross-references accumulated screen operations against must-have operations.
        /// Returns a checklist with IsPresent set based on fuzzy matching.
        /// </summary>
        private List<MustHaveChecklistItem> BuildScreenMustHaveChecklist(IReadOnlyList<ParsedEstimateLine> accumulatedOps)
        {
            var checklist = new List<MustHaveChecklistItem>();
            var categories = EstimateContextService.Instance.GetMustHaveCategories();
            if (categories.Count == 0) return checklist;

            // Build normalized searchable strings from PartName and Description.
            // Do NOT use RawLine — it contains full OCR lines with too much noise.
            var allTexts = new List<string>();
            foreach (var o in accumulatedOps)
            {
                var partNorm = NormalizeForMatch(o.PartName);
                var descNorm = NormalizeForMatch(o.Description);
                if (!string.IsNullOrEmpty(partNorm) && !allTexts.Contains(partNorm))
                    allTexts.Add(partNorm);
                if (!string.IsNullOrEmpty(descNorm) && descNorm != partNorm && !allTexts.Contains(descNorm))
                    allTexts.Add(descNorm);
            }

            // Debug: log all texts being matched against
            System.Diagnostics.Debug.WriteLine($"[MustHave] accumulatedOps count: {accumulatedOps.Count}");
            System.Diagnostics.Debug.WriteLine($"[MustHave] allTexts ({allTexts.Count}): {string.Join(" | ", allTexts)}");

            // Build combined text for condition evaluation
            var combinedTextLower = string.Join(" ", allTexts);

            foreach (var category in categories)
            {
                foreach (var op in category.Operations)
                {
                    // Skip operations whose condition is not met by the estimate context
                    if (!EstimateConditionEvaluator.Evaluate(op.Conditions, combinedTextLower))
                        continue;

                    var opNorm = NormalizeForMatch(op.Description);
                    var opWords = ExtractSignificantWords(opNorm);

                    var isPresent = allTexts.Any(d =>
                    {
                        // Check 1: Exact match
                        if (d == opNorm) return true;

                        // Check 2: Detected text contains full must-have (detected is longer/equal)
                        if (d.Contains(opNorm)) return true;

                        // Check 3: Must-have contains detected text, BUT only if detected text
                        // is at least 45% of the must-have length. Prevents short detected texts
                        // like "adas calibration" from matching long must-haves like
                        // "simulate full fluids for adas calibrations".
                        if (opNorm.Contains(d) && d.Length >= opNorm.Length * 0.45)
                            return true;

                        // Check 4: Proportional word overlap — must-have words found in detected text
                        // Requires 60% of must-have significant words to be present (prevents
                        // "adas calibration" from matching 5-word ADAS must-haves with only 2 words)
                        if (opWords.Length > 0)
                        {
                            var matchCount = opWords.Count(w => d.Contains(w));
                            int threshold;
                            if (opWords.Length <= 2)
                                threshold = opWords.Length; // Short must-haves: ALL words must match
                            else
                                threshold = (int)Math.Ceiling(opWords.Length * 0.6); // 60% for longer

                            if (matchCount >= threshold)
                                return true;
                        }

                        return false;
                    });

                    System.Diagnostics.Debug.WriteLine($"[MustHave] {(isPresent ? "MATCH" : "miss ")}: '{op.Description}' (words: {string.Join(",", opWords)})");

                    checklist.Add(new MustHaveChecklistItem
                    {
                        Description = op.Description,
                        Category = category.Name,
                        OperationType = op.OperationType,
                        LaborHours = op.LaborHours,
                        Price = op.Price,
                        MaterialsCost = op.MaterialsCost,
                        WhyNeeded = op.WhyNeeded,
                        Conditions = op.Conditions,
                        IsPresent = isPresent
                    });
                }
            }

            return checklist;
        }

        private Border CreateAccumulatedOperationRow(ParsedEstimateLine op, HashSet<string> confirmedTitles)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: type badge + part name, possibly with confirmed checkmark
            var leftStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            if (!string.IsNullOrEmpty(op.OperationType))
            {
                var typeBadge = new Border
                {
                    Background = new SolidColorBrush(GetOperationTypeColor(op.OperationType)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                typeBadge.Child = new TextBlock
                {
                    Text = op.OperationType,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                leftStack.Children.Add(typeBadge);
            }

            var partDisplay = op.PartName ?? op.Description;
            // Strip part numbers (e.g., "Door shell 6700253130" → "Door shell")
            partDisplay = System.Text.RegularExpressions.Regex.Replace(partDisplay, @"\s+\d{4,}[A-Z0-9]*\s*$", "").Trim();
            if (partDisplay.Length > 60) partDisplay = partDisplay[..60] + "...";
            leftStack.Children.Add(new TextBlock
            {
                Text = partDisplay,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            // Check if any confirmed suggestion matches this op
            var opDesc = $"{op.OperationType} {op.PartName}".Trim();
            if (confirmedTitles.Contains(opDesc) || confirmedTitles.Any(t =>
                t.Contains(op.PartName ?? "", StringComparison.OrdinalIgnoreCase) &&
                t.Contains(op.OperationType ?? "", StringComparison.OrdinalIgnoreCase)))
            {
                leftStack.Children.Add(new TextBlock
                {
                    Text = "\u2713",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // Right: hours / price
            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            if (op.LaborHours > 0)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"{op.LaborHours:0.0}h",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            if (op.RefinishHours > 0)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"{op.RefinishHours:0.0}r",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 150, 255)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            if (op.Price > 0)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"${op.Price:N2}",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(rightStack);

            border.Child = grid;
            return border;
        }

        private string NormalizePartName(string partName)
        {
            if (string.IsNullOrEmpty(partName)) return "Unknown";
            var lower = partName.ToLowerInvariant();
            foreach (var (pattern, canonical) in _scanParts)
            {
                if (lower.Contains(pattern))
                    return canonical;
            }
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(partName.ToLowerInvariant());
        }

        private Border? BuildSimilarEstimateBar(ScreenOcrResult result, CoachingSnapshot? snapshot)
        {
            try
            {
                var db = EstimateHistoryDatabase.Instance;
                var allEstimates = db.GetAllEstimates();
                if (allEstimates.Count == 0) return null;

                // Collect detected part names as damage zones
                var detectedZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var op in result.DetectedOperations)
                {
                    if (string.IsNullOrEmpty(op.PartName)) continue;
                    var normalized = NormalizePartName(op.PartName);
                    if (normalized != "Unknown") detectedZones.Add(normalized);
                }
                if (detectedZones.Count == 0) return null;

                // Find estimates with overlapping damage zones
                var matches = allEstimates.Where(e =>
                {
                    if (e.DNA.DamageZones.Count == 0) return false;
                    var overlap = e.DNA.DamageZones.Count(z =>
                        detectedZones.Any(d =>
                            z.Contains(d, StringComparison.OrdinalIgnoreCase) ||
                            d.Contains(z, StringComparison.OrdinalIgnoreCase)));
                    return overlap >= Math.Min(2, detectedZones.Count);
                }).ToList();

                // Also try vehicle keyword matching if few zone matches
                if (matches.Count < 2 && !string.IsNullOrEmpty(snapshot?.VehicleInfo))
                {
                    var vehicleWords = snapshot.VehicleInfo.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length > 2).ToArray();
                    if (vehicleWords.Length >= 2)
                    {
                        var vehicleMatches = allEstimates.Where(e =>
                        {
                            if (string.IsNullOrEmpty(e.VehicleInfo)) return false;
                            var wordHits = vehicleWords.Count(w =>
                                e.VehicleInfo.Contains(w, StringComparison.OrdinalIgnoreCase));
                            return wordHits >= 2;
                        }).ToList();
                        // Merge without duplicates
                        var existingIds = matches.Select(m => m.Id).ToHashSet();
                        matches.AddRange(vehicleMatches.Where(m => !existingIds.Contains(m.Id)));
                    }
                }

                if (matches.Count == 0) return null;

                var avgTotal = (decimal)matches.Average(e => (double)e.GrandTotal);
                var avgLines = (int)matches.Average(e => e.LineItems.Count);
                var vehicleLabel = !string.IsNullOrEmpty(snapshot?.VehicleInfo) ? snapshot.VehicleInfo : "";
                var damageLabel = string.Join(", ", detectedZones.Take(3));
                var contextLabel = !string.IsNullOrEmpty(vehicleLabel)
                    ? $"{vehicleLabel}, {damageLabel}"
                    : damageLabel;

                var bar = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 28, 40, 52)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 45, 70, 95)),
                    BorderThickness = new Thickness(1)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var icon = new TextBlock
                {
                    Text = "\u2261",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 160, 230)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);

                var text = new TextBlock
                {
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 200, 235)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                var run1 = new Microsoft.UI.Xaml.Documents.Run { Text = $"{matches.Count} similar estimate{(matches.Count != 1 ? "s" : "")}" };
                var run2 = new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = !string.IsNullOrEmpty(contextLabel) ? $" ({contextLabel})" : "",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 150, 180))
                };
                var run3 = new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = $" — avg ${avgTotal:N0} | avg {avgLines} lines",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                text.Inlines.Add(run1);
                text.Inlines.Add(run2);
                text.Inlines.Add(run3);

                Grid.SetColumn(text, 1);
                grid.Children.Add(text);

                bar.Child = grid;

                var tooltip = new ToolTip { Content = "Search the History tab in Estimate Builder for details" };
                ToolTipService.SetToolTip(bar, tooltip);

                return bar;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenMonitor] Similar estimate bar error: {ex.Message}");
                return null;
            }
        }

        private Border BuildCoachingSuggestionCard(McstudDesktop.Models.CoachingSuggestion suggestion)
        {
            var isConfirmed = suggestion.IsConfirmedOnEstimate;

            var card = new Border
            {
                Background = new SolidColorBrush(isConfirmed
                    ? Color.FromArgb(255, 30, 40, 32)   // Greenish tint for confirmed
                    : Color.FromArgb(255, 38, 42, 52)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 1, 0, 1),
                Opacity = isConfirmed ? 0.7 : 1.0
            };

            var cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: severity badge / checkmark + title + triggered by
            var leftStack = new StackPanel { Spacing = 2 };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            if (isConfirmed)
            {
                // Green checkmark badge instead of severity
                var checkBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 46, 125, 50)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkBadge.Child = new TextBlock
                {
                    Text = "\u2713 ON EST",
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                titleRow.Children.Add(checkBadge);
            }
            else
            {
                var severityColor = suggestion.Severity switch
                {
                    McstudDesktop.Models.CoachingSeverity.Critical => Color.FromArgb(255, 220, 50, 50),
                    McstudDesktop.Models.CoachingSeverity.High => Color.FromArgb(255, 230, 140, 30),
                    McstudDesktop.Models.CoachingSeverity.Medium => Color.FromArgb(255, 60, 130, 220),
                    _ => Color.FromArgb(255, 120, 120, 120)
                };

                var severityBadge = new Border
                {
                    Background = new SolidColorBrush(severityColor),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                severityBadge.Child = new TextBlock
                {
                    Text = suggestion.Severity.ToString().ToUpper(),
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                titleRow.Children.Add(severityBadge);
            }

            var titleText = new TextBlock
            {
                Text = suggestion.Title,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(isConfirmed
                    ? Color.FromArgb(255, 120, 120, 120)
                    : Color.FromArgb(255, 255, 255, 255)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                TextDecorations = isConfirmed ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None
            };

            titleRow.Children.Add(titleText);
            leftStack.Children.Add(titleRow);

            if (!string.IsNullOrEmpty(suggestion.TriggeredBy))
            {
                leftStack.Children.Add(new TextBlock
                {
                    Text = $"For: {suggestion.TriggeredBy}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextDecorations = isConfirmed ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None
                });
            }

            Grid.SetColumn(leftStack, 0);
            cardGrid.Children.Add(leftStack);

            // Right: cost + dismiss
            var rightStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (suggestion.EstimatedCost > 0)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"${suggestion.EstimatedCost:F0}",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(isConfirmed
                        ? Color.FromArgb(255, 100, 100, 100)
                        : Color.FromArgb(255, 255, 214, 0)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextDecorations = isConfirmed ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None
                });
            }

            if (!isConfirmed)
            {
                // Export button
                var addBtn = new Button
                {
                    Content = new TextBlock
                    {
                        Text = "Export",
                        FontSize = 9,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
                    Foreground = new SolidColorBrush(Colors.White),
                    Padding = new Thickness(6, 2, 6, 2),
                    MinWidth = 0,
                    MinHeight = 24,
                    CornerRadius = new CornerRadius(3)
                };
                var suggestionForAdd = suggestion;
                addBtn.Click += (s, e) =>
                {
                    ExportSuggestion(suggestionForAdd);
                    ((TextBlock)addBtn.Content).Text = "\u2713 Exported";
                    addBtn.IsEnabled = false;
                };
                rightStack.Children.Add(addBtn);

                // Dismiss button
                var dismissBtn = new Button
                {
                    Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
                    Background = new SolidColorBrush(Colors.Transparent),
                    Padding = new Thickness(4, 2, 4, 2),
                    MinWidth = 24,
                    MinHeight = 24
                };
                var suggestionId = suggestion.Id;
                dismissBtn.Click += (s, e) =>
                {
                    LiveCoachingService.Instance.DismissSuggestion(suggestionId);
                    if (card.Parent is Panel parent)
                        parent.Children.Remove(card);
                };
                rightStack.Children.Add(dismissBtn);
            }

            Grid.SetColumn(rightStack, 1);
            cardGrid.Children.Add(rightStack);

            card.Child = cardGrid;
            return card;
        }

        /// <summary>
        /// Exports a coaching suggestion to the virtual clipboard and navigates to the Export tab.
        /// </summary>
        private void ExportSuggestion(McstudDesktop.Models.CoachingSuggestion suggestion)
        {
            var op = MapSuggestionToVirtualClipboardOp(suggestion);
            VirtualClipboardService.Instance.AddOperation(op, "Live Coach");
            OnNavigateToExport?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Converts a CoachingSuggestion into a VirtualClipboardOp for the Export tab.
        /// </summary>
        private static VirtualClipboardOp MapSuggestionToVirtualClipboardOp(McstudDesktop.Models.CoachingSuggestion suggestion)
        {
            var category = suggestion.Category.ToLowerInvariant();
            var title = suggestion.Title.ToLowerInvariant();

            // Map category to CCC operation type
            string operationType;
            if (category.Contains("refinish") || category.Contains("paint") || category.Contains("blend") || title.Contains("blend"))
                operationType = "Refinish";
            else if (title.StartsWith("replace ") || title.Contains("cover and protect") || title.Contains("mobile cart"))
                operationType = "Replace";
            else if (title.Contains("r&i") || title.Contains("remove and install"))
                operationType = "R&I";
            else
                operationType = "Rpr";

            // Build description: include the triggering part if available
            var description = suggestion.Title;
            if (!string.IsNullOrEmpty(suggestion.TriggeredBy) && !description.Contains(suggestion.TriggeredBy, StringComparison.OrdinalIgnoreCase))
                description = $"{suggestion.TriggeredBy} {description}";

            // Determine if labor hours go to Labor or Refinish
            bool isRefinish = operationType == "Refinish" ||
                              title.Contains("blend") ||
                              title.Contains("refinish") ||
                              title.Contains("paint") ||
                              title.Contains("flex additive");

            return new VirtualClipboardOp
            {
                OperationType = operationType,
                Description = description,
                Quantity = 1,
                Price = suggestion.EstimatedCost,
                LaborHours = isRefinish ? 0 : suggestion.LaborHours,
                RefinishHours = isRefinish ? suggestion.LaborHours : 0,
                Category = suggestion.Category
            };
        }

        private static Color GetGradeColor(string? grade)
        {
            if (string.IsNullOrEmpty(grade)) return Color.FromArgb(255, 150, 150, 150);
            return grade[0] switch
            {
                'A' => Color.FromArgb(255, 76, 175, 80),
                'B' => Color.FromArgb(255, 139, 195, 74),
                'C' => Color.FromArgb(255, 255, 193, 7),
                'D' => Color.FromArgb(255, 255, 152, 0),
                'F' => Color.FromArgb(255, 244, 67, 54),
                _ => Color.FromArgb(255, 150, 150, 150)
            };
        }

        // === DIAGNOSTIC TRACE ===
        private Border CreateDiagnosticSection()
        {
            _diagnosticSection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 40)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 55, 70)),
                BorderThickness = new Thickness(1)
            };

            var outerStack = new StackPanel { Spacing = 6 };
            outerStack.Children.Add(new TextBlock
            {
                Text = "Pipeline Diagnostic",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 150, 255))
            });

            _diagnosticStack = new StackPanel { Spacing = 3 };
            _diagnosticStack.Children.Add(new TextBlock
            {
                Text = "Click 'Capture Once' or toggle monitoring to see pipeline status",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 110, 110, 110)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });

            outerStack.Children.Add(_diagnosticStack);
            _diagnosticSection.Child = outerStack;
            return _diagnosticSection;
        }

        private void UpdateDiagnosticTrace(ScreenOcrResult result)
        {
            if (_diagnosticStack == null) return;
            _diagnosticStack.Children.Clear();

            // Step 1: Window capture
            var windowOk = !string.IsNullOrEmpty(result.SourceWindow);
            AddDiagLine(windowOk,
                windowOk ? $"Window captured: \"{result.SourceWindow}\"" : "No window captured");

            // Step 2: OCR text
            var rawLen = result.RawText?.Length ?? 0;
            var lineCount = result.LineCount;
            var ocrOk = rawLen > 0;
            AddDiagLine(ocrOk,
                ocrOk ? $"OCR returned {lineCount} lines ({rawLen} chars)" : "OCR returned no text");

            if (!string.IsNullOrEmpty(result.ErrorMessage))
                AddDiagLine(false, $"Error: {result.ErrorMessage}");

            // Step 3: Source detection
            AddDiagLine(result.EstimateSource != McstudDesktop.Models.OcrEstimateSource.Unknown,
                $"Source detected: {result.EstimateSource}");

            // Step 4: Structured operations
            var opCount = result.DetectedOperations?.Count ?? 0;
            AddDiagLine(opCount > 0,
                opCount > 0 ? $"Parsed {opCount} structured operations" : "0 structured operations parsed (raw text will be scanned for part names)");

            // Step 5: Part scanning from raw text
            if (ocrOk)
            {
                var parts = ScanPartsFromRawText(result.RawText!);
                AddDiagLine(parts.Count > 0,
                    parts.Count > 0 ? $"Parts found in text: {string.Join(", ", parts.Take(8))}{(parts.Count > 8 ? $" +{parts.Count - 8} more" : "")}" : "No part names found in raw text");
            }

            // Step 6: Coaching status
            var coachActive = LiveCoachingService.Instance.IsRunning;
            AddDiagLine(coachActive,
                coachActive ? "Live coaching is active — scoring results above" : "Live coaching is OFF — toggle screen monitor on to enable");

            // Step 7: HasChanges
            AddDiagLine(result.HasChanges,
                result.HasChanges ? "Content changed since last capture" : "No changes since last capture (duplicate)");
        }

        private void AddDiagLine(bool ok, string text)
        {
            var icon = ok ? "\u2705" : "\u274C";
            _diagnosticStack!.Children.Add(new TextBlock
            {
                Text = $"{icon}  {text}",
                FontSize = 11,
                Foreground = new SolidColorBrush(ok
                    ? Color.FromArgb(255, 130, 200, 130)
                    : Color.FromArgb(255, 200, 130, 130)),
                TextWrapping = TextWrapping.Wrap
            });
        }

        private static List<string> ScanPartsFromRawText(string rawText)
        {
            var lowerText = rawText.ToLowerInvariant();
            var parts = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Reuse the same part patterns from the class
            foreach (var (pattern, canonical) in _scanParts)
            {
                if (seen.Contains(canonical)) continue;
                var idx = lowerText.IndexOf(pattern);
                if (idx < 0) continue;
                if (idx > 0 && char.IsLetter(lowerText[idx - 1])) continue;
                var endIdx = idx + pattern.Length;
                if (endIdx < lowerText.Length && char.IsLetter(lowerText[endIdx])) continue;
                seen.Add(canonical);
                parts.Add(canonical);
            }
            return parts;
        }

        // === EVENT HANDLERS ===

        private void MonitorToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_monitorToggle!.IsOn)
            {
                _monitorService.Start();
                // Auto-start live coaching with screen monitor
                if (!LiveCoachingService.Instance.IsRunning)
                    LiveCoachingService.Instance.Start();
            }
            else
            {
                _monitorService.Stop();
                // Auto-stop live coaching with screen monitor
                if (LiveCoachingService.Instance.IsRunning)
                    LiveCoachingService.Instance.Stop();
            }
        }

        private void IntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var seconds = _intervalCombo!.SelectedIndex switch
            {
                0 => 1,
                1 => 2,
                2 => 5,
                3 => 10,
                _ => 2
            };

            _monitorService.CaptureInterval = TimeSpan.FromSeconds(seconds);
        }

        private async void CaptureOnceButton_Click(object sender, RoutedEventArgs e)
        {
            _captureOnceButton!.IsEnabled = false;
            _captureOnceButton.Content = "Capturing...";

            // Auto-start live coaching so suggestions generate even on one-shot captures
            if (!LiveCoachingService.Instance.IsRunning)
                LiveCoachingService.Instance.Start();

            try
            {
                await _monitorService.CaptureOnceAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Capture failed: {ex.Message}");
            }
            finally
            {
                _captureOnceButton.IsEnabled = true;
                _captureOnceButton.Content = "Capture Once";
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _monitorService.ClearHistory();
            LiveCoachingService.Instance.ResetVehicle();
            _latestResult = null;
            _latestSnapshot = null;
            _analysisContentStack?.Children.Clear();
            _analysisContentStack?.Children.Add(new TextBlock
            {
                Text = "No operations detected yet. Capture a screenshot to begin.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
            // Reset score banner
            if (_coachingGradeText != null) _coachingGradeText.Text = "--";
            if (_coachingScoreText != null) _coachingScoreText.Text = "";
            if (_coachingPotentialText != null) _coachingPotentialText.Text = "";
            if (_vehicleBanner != null) _vehicleBanner.Visibility = Visibility.Collapsed;
            if (_overviewSection != null) _overviewSection.Visibility = Visibility.Collapsed;
            if (_rawTextBox != null) _rawTextBox.Text = "(no OCR text captured yet)";
            UpdateStatusDisplay();
            _feedToChatButton!.IsEnabled = false;
            _copyButton!.IsEnabled = false;
            _loadToBuilderButton!.IsEnabled = false;
        }

        private void FeedToChatButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latestResult == null || string.IsNullOrEmpty(_latestResult.RawText)) return;

            OnFeedToChat?.Invoke(this, _latestResult);
        }

        private void LoadToBuilderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latestResult == null || _latestResult.DetectedOperations.Count == 0) return;

            OnLoadToBuilder?.Invoke(this, _latestResult);
            UpdateStatus($"Loaded {_latestResult.DetectedOperations.Count} operations to Builder");
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latestResult == null || string.IsNullOrEmpty(_latestResult.RawText)) return;

            try
            {
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(_latestResult.RawText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                _copyButton!.Content = "Copied!";
                await Task.Delay(1500);
                _copyButton.Content = "Copy to Clipboard";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenMonitor] Copy failed: {ex.Message}");
            }
        }

        private async void TranscriptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latestResult == null) return;

            try
            {
                var transcript = GenerateTranscript();
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(transcript);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                _transcriptButton!.Content = "Copied!";
                await Task.Delay(1500);
                _transcriptButton.Content = "Copy Transcript";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenMonitor] Transcript copy failed: {ex.Message}");
            }
        }

        private string GenerateTranscript()
        {
            var lines = new List<string>();
            var result = _latestResult!;
            var snapshot = _latestSnapshot;

            lines.Add("=== SCREEN READ TRANSCRIPT ===");
            lines.Add($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"Source Window: {result.SourceWindow ?? "Unknown"}");
            lines.Add($"OCR Capture Time: {result.Timestamp:HH:mm:ss}");
            lines.Add("");

            // Detected operations
            lines.Add($"--- DETECTED OPERATIONS ({result.DetectedOperations.Count}) ---");
            if (result.DetectedOperations.Count > 0)
            {
                foreach (var op in result.DetectedOperations)
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(op.OperationType)) parts.Add(op.OperationType);
                    parts.Add(op.PartName ?? op.Description);
                    if (op.LaborHours > 0) parts.Add($"Labor: {op.LaborHours:0.0}h");
                    if (op.RefinishHours > 0) parts.Add($"Refinish: {op.RefinishHours:0.0}h");
                    if (op.Price > 0) parts.Add($"${op.Price:N2}");
                    lines.Add($"  {string.Join(" | ", parts)}");
                }
            }
            else
            {
                lines.Add("  (none detected)");
            }
            lines.Add("");

            // Parts detected from accumulated ops
            var detectedParts = result.DetectedOperations
                .Where(op => !string.IsNullOrEmpty(op.PartName))
                .Select(op => NormalizePartName(op.PartName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (detectedParts.Count > 0)
            {
                lines.Add($"--- DETECTED PARTS ({detectedParts.Count}) ---");
                foreach (var part in detectedParts)
                    lines.Add($"  - {part}");
                lines.Add("");
            }

            // Live coaching
            if (snapshot != null)
            {
                lines.Add("--- LIVE COACHING ---");
                lines.Add($"Score: {snapshot.Score}/100 (Grade: {snapshot.Grade})");
                lines.Add($"Potential Recovery: ${snapshot.PotentialRecovery:N2}");
                if (snapshot.VehicleInfo != null)
                    lines.Add($"Vehicle: {snapshot.VehicleInfo}");
                lines.Add($"Total Detected: {snapshot.TotalOperationsDetected} | Confirmed: {snapshot.ConfirmedCount}");
                lines.Add("");

                var missing = snapshot.Suggestions.Where(s => !s.IsConfirmedOnEstimate && !s.IsDismissed).ToList();
                var confirmed = snapshot.Suggestions.Where(s => s.IsConfirmedOnEstimate && !s.IsDismissed).ToList();
                var dismissed = snapshot.Suggestions.Where(s => s.IsDismissed).ToList();

                if (missing.Count > 0)
                {
                    lines.Add($"  MISSING ({missing.Count}):");
                    foreach (var s in missing.OrderByDescending(x => x.Severity))
                    {
                        var cost = s.EstimatedCost > 0 ? $" (${s.EstimatedCost:N2})" : "";
                        var hours = s.LaborHours > 0 ? $" [{s.LaborHours:0.0}h]" : "";
                        lines.Add($"    [{s.Severity}] {s.Title}{cost}{hours} — {s.Source}");
                        if (!string.IsNullOrEmpty(s.TriggeredBy))
                            lines.Add($"           Triggered by: {s.TriggeredBy}");
                    }
                    lines.Add("");
                }

                if (confirmed.Count > 0)
                {
                    lines.Add($"  ON ESTIMATE ({confirmed.Count}):");
                    foreach (var s in confirmed)
                        lines.Add($"    [OK] {s.Title} — {s.Source}");
                    lines.Add("");
                }

                if (dismissed.Count > 0)
                {
                    lines.Add($"  DISMISSED ({dismissed.Count}):");
                    foreach (var s in dismissed)
                        lines.Add($"    [X] {s.Title}");
                    lines.Add("");
                }
            }

            // Raw OCR text
            lines.Add("--- RAW OCR TEXT ---");
            lines.Add(result.RawText ?? "(empty)");
            lines.Add("");
            lines.Add("=== END TRANSCRIPT ===");

            return string.Join("\n", lines);
        }

        private void MonitorService_OcrResultReady(object? sender, ScreenOcrResult result)
        {
            // Marshal to UI thread
            DispatcherQueue?.TryEnqueue(() =>
            {
                _latestResult = result;
                UpdateResultDisplay(result);
                UpdateStatusDisplay();

                // Update pipeline diagnostic trace
                UpdateDiagnosticTrace(result);
            });

            // Auto-match against reference data when changes detected
            if (result.HasChanges && result.DetectedOperations.Count > 0)
            {
                _ = RunReferenceMatchingAsync(result);
            }
        }

        private async Task RunReferenceMatchingAsync(ScreenOcrResult result)
        {
            try
            {
                var matchResult = await _referenceMatcher.MatchOcrResultAsync(result);
                if (matchResult.Items.Count > 0)
                {
                    ReferenceView.Instance?.ShowStagedItems(matchResult.Items);

                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        if (_refMatchStatusText != null)
                        {
                            _refMatchStatusText.Text = $"Found {matchResult.Items.Count} reference matches — review in Reference tab";
                            _refMatchStatusText.Visibility = Visibility.Visible;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenMonitor] Reference matching error: {ex.Message}");
            }
        }

        // Known parts to scan for in raw OCR text.
        // IMPORTANT: Longer/more-specific patterns MUST come before shorter ones
        // so "front bumper" matches before "bumper", "fender liner" before "fender", etc.
        private static readonly (string Pattern, string CanonicalName)[] _scanParts = new[]
        {
            // Bumper — specific first, then generic
            ("front bumper", "Front Bumper"), ("rear bumper", "Rear Bumper"),
            ("bumper reinforcement", "Bumper Reinforcement"), ("bumper absorber", "Bumper Absorber"),
            ("bumper fascia", "Bumper Cover"), ("bumper cover", "Bumper Cover"),
            // Fender — liner before fender
            ("fender liner", "Fender Liner"), ("inner fender", "Fender Liner"),
            ("front fender", "Fender"), ("fender", "Fender"),
            // Doors
            ("front door", "Front Door"), ("rear door", "Rear Door"),
            ("door handle", "Door Handle"), ("door trim", "Door Trim Panel"),
            // Panels
            ("quarter panel", "Quarter Panel"), ("qtr panel", "Quarter Panel"), ("quarter", "Quarter Panel"),
            ("rocker panel", "Rocker Panel"),
            ("side panel", "Side Panel"),
            // Roof / trunk / liftgate
            ("roof panel", "Roof"),
            ("trunk lid", "Trunk Lid"), ("decklid", "Trunk Lid"),
            ("liftgate", "Liftgate"), ("tailgate", "Tailgate"),
            // Hood
            ("hood", "Hood"),
            // Lights
            ("headlamp", "Headlight"), ("headlight", "Headlight"), ("head lamp", "Headlight"),
            ("tail lamp", "Tail Light"), ("taillight", "Tail Light"), ("tail light", "Tail Light"),
            ("fog lamp", "Fog Light"), ("fog light", "Fog Light"),
            // Front end
            ("grille", "Grille"), ("grill", "Grille"),
            ("radiator support", "Radiator Support"), ("rad support", "Radiator Support"),
            ("energy absorber", "Bumper Absorber"),
            // Glass
            ("windshield", "Windshield"), ("back glass", "Rear Glass"),
            // Exterior trim / parts
            ("side mirror", "Mirror"), ("outside mirror", "Mirror"),
            ("parking sensor", "Parking Sensor"), ("backup camera", "Backup Camera"),
            ("splash shield", "Splash Shield"), ("wheel opening", "Wheel Opening Molding"),
            ("molding", "Molding"), ("spoiler", "Spoiler"), ("valance", "Valance"),
            // Pillars
            ("a pillar", "A-Pillar"), ("b pillar", "B-Pillar"), ("c pillar", "C-Pillar"),
            // Mechanical
            ("radiator", "Radiator"), ("condenser", "Condenser"),
            ("control arm", "Control Arm"), ("strut", "Strut"), ("suspension", "Suspension"),
            ("wheel", "Wheel"), ("tire", "Tire"),
            // Diagnostics / ADAS
            ("adas", "ADAS Calibration"), ("calibration", "Calibration"),
            ("pre scan", "Pre-Repair Scan"), ("post scan", "Post-Repair Scan"),
            ("diagnostic", "Diagnostic Scan")
        };


        private void MonitorService_StatusChanged(object? sender, string status)
        {
            DispatcherQueue?.TryEnqueue(() => UpdateStatus(status));
        }

        // === DISPLAY UPDATES ===

        private void UpdateResultDisplay(ScreenOcrResult result)
        {
            if (result.ErrorMessage != null)
            {
                UpdateStatus(result.ErrorMessage);
            }

            // Update raw text
            if (_rawTextBox != null)
            {
                _rawTextBox.Text = string.IsNullOrEmpty(result.RawText) ? "(empty)" : result.RawText;
            }

            // Enable action buttons
            var hasContent = !string.IsNullOrEmpty(result.RawText);
            var hasOps = result.DetectedOperations.Count > 0;
            _feedToChatButton!.IsEnabled = hasContent;
            _copyButton!.IsEnabled = hasContent;
            _loadToBuilderButton!.IsEnabled = hasOps;
            if (_transcriptButton != null) _transcriptButton.IsEnabled = hasContent;

            // Rebuild unified analysis display
            RebuildAnalysisDisplay();
        }

        private Border CreateOperationRow(OcrDetectedOperation op)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 58)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: description + type badge
            var leftStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            if (!string.IsNullOrEmpty(op.OperationType))
            {
                var typeBadge = new Border
                {
                    Background = new SolidColorBrush(GetOperationTypeColor(op.OperationType)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                typeBadge.Child = new TextBlock
                {
                    Text = op.OperationType,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                leftStack.Children.Add(typeBadge);
            }

            leftStack.Children.Add(new TextBlock
            {
                Text = op.PartName.Length > 60 ? op.PartName[..60] + "..." : op.PartName,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // Right: hours / price
            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            if (op.LaborHours > 0)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"{op.LaborHours:0.0}h",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            if (op.RefinishHours > 0)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"{op.RefinishHours:0.0}r",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 150, 255)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            if (op.Price > 0)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"${op.Price:N2}",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(rightStack);

            border.Child = grid;
            return border;
        }

        private void UpdateStatusDisplay()
        {
            _captureCountText!.Text = _monitorService.CaptureCount.ToString();
            _changeCountText!.Text = _monitorService.ChangeCount.ToString();

            if (_latestResult != null)
            {
                _sourceText!.Text = _latestResult.EstimateSource == OcrEstimateSource.Unknown
                    ? (_latestResult.SourceWindow.Length > 30 ? _latestResult.SourceWindow[..30] + "..." : _latestResult.SourceWindow)
                    : _latestResult.EstimateSource.ToString();

                if (_latestResult.HasChanges)
                {
                    _lastChangeText!.Text = _latestResult.Timestamp.ToString("HH:mm:ss");
                }
            }
        }

        private void UpdateStatus(string message)
        {
            if (_statusText != null)
                _statusText.Text = message;
        }

        internal static string FormatResultForChat(ScreenOcrResult result)
        {
            var lines = new List<string>();
            lines.Add($"[Screen OCR - {result.SourceWindow} - {result.Timestamp:HH:mm:ss}]");
            lines.Add("");

            if (result.DetectedOperations.Count > 0)
            {
                lines.Add($"Detected {result.DetectedOperations.Count} operations:");
                foreach (var op in result.DetectedOperations)
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(op.OperationType)) parts.Add(op.OperationType);
                    parts.Add(op.PartName);
                    if (op.LaborHours > 0) parts.Add($"{op.LaborHours:0.0}h labor");
                    if (op.RefinishHours > 0) parts.Add($"{op.RefinishHours:0.0}h refinish");
                    if (op.Price > 0) parts.Add($"${op.Price:N2}");
                    lines.Add($"  - {string.Join(" | ", parts)}");
                }
                lines.Add("");
            }

            lines.Add("Raw OCR Text:");
            lines.Add(result.RawText);

            return string.Join("\n", lines);
        }

        // CCC section display order (matches GhostEstimatePanel)
        private static readonly string[] _cccSectionOrder = new[]
        {
            "FRONT BUMPER & GRILLE", "REAR BUMPER",
            "FRONT LAMPS", "REAR LAMPS",
            "RADIATOR SUPPORT",
            "HOOD",
            "FENDER",
            "FRONT DOOR", "REAR DOOR",
            "QUARTER PANEL",
            "PILLARS, ROCKER & FLOOR",
            "ROOF",
            "TRUNK / DECKLID",
            "GLASS",
            "FRAME",
            "RESTRAINT SYSTEMS",
            "ELECTRICAL",
            "INSTRUMENT PANEL",
            "VEHICLE DIAGNOSTICS",
            "MECHANICAL",
            "MISCELLANEOUS OPERATIONS"
        };

        private static readonly Dictionary<string, Color> _cccSectionColors = new()
        {
            ["FRONT BUMPER & GRILLE"] = Color.FromArgb(255, 100, 180, 255),
            ["REAR BUMPER"] = Color.FromArgb(255, 100, 180, 255),
            ["FRONT LAMPS"] = Color.FromArgb(255, 130, 190, 255),
            ["REAR LAMPS"] = Color.FromArgb(255, 130, 190, 255),
            ["RADIATOR SUPPORT"] = Color.FromArgb(255, 100, 170, 240),
            ["HOOD"] = Color.FromArgb(255, 100, 180, 255),
            ["FENDER"] = Color.FromArgb(255, 100, 180, 255),
            ["FRONT DOOR"] = Color.FromArgb(255, 100, 180, 255),
            ["REAR DOOR"] = Color.FromArgb(255, 100, 180, 255),
            ["QUARTER PANEL"] = Color.FromArgb(255, 100, 180, 255),
            ["ROOF"] = Color.FromArgb(255, 100, 180, 255),
            ["TRUNK / DECKLID"] = Color.FromArgb(255, 100, 180, 255),
            ["GLASS"] = Color.FromArgb(255, 130, 200, 240),
            ["INSTRUMENT PANEL"] = Color.FromArgb(255, 130, 190, 255),
            ["PILLARS, ROCKER & FLOOR"] = Color.FromArgb(255, 255, 130, 130),
            ["FRAME"] = Color.FromArgb(255, 255, 130, 130),
            ["RESTRAINT SYSTEMS"] = Color.FromArgb(255, 255, 100, 100),
            ["VEHICLE DIAGNOSTICS"] = Color.FromArgb(255, 100, 220, 180),
            ["ELECTRICAL"] = Color.FromArgb(255, 255, 200, 80),
            ["MECHANICAL"] = Color.FromArgb(255, 130, 200, 255),
            ["MISCELLANEOUS OPERATIONS"] = Color.FromArgb(255, 180, 185, 190),
        };

        /// <summary>
        /// Maps a coaching suggestion to the correct CCC estimate section
        /// based on its Category and TriggeredBy (part name).
        /// </summary>
        private static string GetCccSection(CoachingSuggestion suggestion)
        {
            var cat = suggestion.Category ?? "";

            // Category-based direct mappings (non-part-specific)
            if (cat.Contains("Electrical", StringComparison.OrdinalIgnoreCase) ||
                cat.Equals("SOP - Electrical", StringComparison.OrdinalIgnoreCase))
                return "ELECTRICAL";
            if (cat.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase) ||
                cat.Contains("Calibration", StringComparison.OrdinalIgnoreCase) ||
                cat.Equals("SOP - Vehicle Diagnostics", StringComparison.OrdinalIgnoreCase))
                return "VEHICLE DIAGNOSTICS";
            if (cat.Contains("Materials", StringComparison.OrdinalIgnoreCase) ||
                cat.Equals("SOP - Miscellaneous", StringComparison.OrdinalIgnoreCase) ||
                cat.Equals("Miscellaneous", StringComparison.OrdinalIgnoreCase))
                return "MISCELLANEOUS OPERATIONS";
            if (cat.Contains("Mechanical", StringComparison.OrdinalIgnoreCase))
                return "MECHANICAL";
            if (cat.Contains("Restraint", StringComparison.OrdinalIgnoreCase))
                return "RESTRAINT SYSTEMS";

            // Part-based mapping from TriggeredBy
            var part = (suggestion.TriggeredBy ?? "").ToLowerInvariant();
            var title = (suggestion.Title ?? "").ToLowerInvariant();
            var combined = part + " " + title;

            // Bumper
            if (combined.Contains("rear bumper")) return "REAR BUMPER";
            if (combined.Contains("front bumper") || combined.Contains("bumper")) return "FRONT BUMPER & GRILLE";
            if (combined.Contains("grille") || combined.Contains("grill")) return "FRONT BUMPER & GRILLE";

            // Lamps
            if (combined.Contains("headlamp") || combined.Contains("headlight") || combined.Contains("head lamp") || combined.Contains("fog l"))
                return "FRONT LAMPS";
            if (combined.Contains("tail lamp") || combined.Contains("taillight") || combined.Contains("tail light") || combined.Contains("rear lamp"))
                return "REAR LAMPS";

            // Radiator support / sight shield
            if (combined.Contains("radiator support") || combined.Contains("rad support") || combined.Contains("sight shield"))
                return "RADIATOR SUPPORT";

            // Hood
            if (combined.Contains("hood")) return "HOOD";

            // Fender
            if (combined.Contains("fender")) return "FENDER";

            // Doors
            if (combined.Contains("rear door")) return "REAR DOOR";
            if (combined.Contains("front door") || combined.Contains("door")) return "FRONT DOOR";

            // Quarter panel
            if (combined.Contains("quarter") || combined.Contains("qtr panel")) return "QUARTER PANEL";

            // Pillars / rocker
            if (combined.Contains("pillar") || combined.Contains("rocker")) return "PILLARS, ROCKER & FLOOR";

            // Roof
            if (combined.Contains("roof")) return "ROOF";

            // Trunk / liftgate
            if (combined.Contains("trunk") || combined.Contains("decklid") || combined.Contains("liftgate") || combined.Contains("tailgate"))
                return "TRUNK / DECKLID";

            // Glass
            if (combined.Contains("windshield") || combined.Contains("back glass") || combined.Contains("glass"))
                return "GLASS";

            // Frame
            if (combined.Contains("frame") || combined.Contains("unibody")) return "FRAME";

            // Refinish items without a specific part → Miscellaneous
            if (cat.Contains("Refinish", StringComparison.OrdinalIgnoreCase) ||
                cat.Contains("Labor", StringComparison.OrdinalIgnoreCase))
            {
                // If the part name tells us where it goes, we'd have matched above.
                // Otherwise, misc.
                return "MISCELLANEOUS OPERATIONS";
            }

            // Mirror, sensor, molding — map by location if possible, otherwise misc
            if (combined.Contains("mirror")) return "FRONT DOOR"; // Most mirrors are door-mounted
            if (combined.Contains("sensor") || combined.Contains("camera")) return "VEHICLE DIAGNOSTICS";

            return "MISCELLANEOUS OPERATIONS";
        }

        /// <summary>
        /// Returns ALL CCC sections a suggestion belongs to.
        /// Most suggestions return 1 section, but door-related suggestions triggered by
        /// both front and rear door parts return both FRONT DOOR and REAR DOOR.
        /// </summary>
        private static List<string> GetAllCccSections(CoachingSuggestion suggestion)
        {
            var primary = GetCccSection(suggestion);
            var part = (suggestion.TriggeredBy ?? "").ToLowerInvariant();
            var title = (suggestion.Title ?? "").ToLowerInvariant();
            var combined = part + " " + title;

            // If this is a door-related suggestion, check if it applies to both front and rear
            if (primary == "FRONT DOOR" || primary == "REAR DOOR")
            {
                var hasFront = combined.Contains("front door");
                var hasRear = combined.Contains("rear door");
                // Generic "door" without front/rear qualifier → show in both
                var isGeneric = combined.Contains("door") && !hasFront && !hasRear;

                if (isGeneric || (hasFront && hasRear))
                    return new List<string> { "FRONT DOOR", "REAR DOOR" };
            }

            return new List<string> { primary };
        }

        /// <summary>
        /// Maps an accumulated estimate operation to the correct CCC section
        /// based on its part name and operation type.
        /// </summary>
        private static string GetCccSectionForOp(ParsedEstimateLine op)
        {
            var part = (op.PartName ?? "").ToLowerInvariant();
            var desc = (op.Description ?? "").ToLowerInvariant();
            var combined = part + " " + desc;

            // Diagnostics / scans / calibration
            if (combined.Contains("scan") || combined.Contains("calibrat") || combined.Contains("adas") ||
                combined.Contains("diagnostic") || combined.Contains("oem research"))
                return "VEHICLE DIAGNOSTICS";

            // Electrical
            if (combined.Contains("battery") || combined.Contains("electronic reset") ||
                combined.Contains("electrical") || combined.Contains("wiring"))
                return "ELECTRICAL";

            // Restraint
            if (combined.Contains("air bag") || combined.Contains("airbag") || combined.Contains("restraint") ||
                combined.Contains("seatbelt") || combined.Contains("impact sens"))
                return "RESTRAINT SYSTEMS";

            // Bumper
            if (combined.Contains("rear bumper")) return "REAR BUMPER";
            if (combined.Contains("front bumper") || combined.Contains("bumper")) return "FRONT BUMPER & GRILLE";
            if (combined.Contains("grille") || combined.Contains("grill")) return "FRONT BUMPER & GRILLE";

            // Lamps
            if (combined.Contains("headlamp") || combined.Contains("headlight") || combined.Contains("head lamp") || combined.Contains("fog l"))
                return "FRONT LAMPS";
            if (combined.Contains("tail lamp") || combined.Contains("taillight") || combined.Contains("tail light") || combined.Contains("rear lamp"))
                return "REAR LAMPS";

            // Radiator support
            if (combined.Contains("radiator support") || combined.Contains("rad support") || combined.Contains("sight shield"))
                return "RADIATOR SUPPORT";

            // Hood
            if (combined.Contains("hood")) return "HOOD";

            // Fender
            if (combined.Contains("fender")) return "FENDER";

            // Doors
            if (combined.Contains("rear door")) return "REAR DOOR";
            if (combined.Contains("front door") || combined.Contains("door")) return "FRONT DOOR";

            // Quarter
            if (combined.Contains("quarter") || combined.Contains("qtr panel")) return "QUARTER PANEL";

            // Pillars / rocker
            if (combined.Contains("pillar") || combined.Contains("rocker")) return "PILLARS, ROCKER & FLOOR";

            // Roof
            if (combined.Contains("roof")) return "ROOF";

            // Trunk / liftgate
            if (combined.Contains("trunk") || combined.Contains("decklid") || combined.Contains("liftgate") || combined.Contains("tailgate"))
                return "TRUNK / DECKLID";

            // Glass
            if (combined.Contains("windshield") || combined.Contains("back glass") || combined.Contains("glass"))
                return "GLASS";

            // Frame
            if (combined.Contains("frame") || combined.Contains("unibody")) return "FRAME";

            // Mirror
            if (combined.Contains("mirror")) return "FRONT DOOR";

            // Instrument panel / speaker / dash
            if (combined.Contains("speaker") || combined.Contains("instrument") || combined.Contains("dash"))
                return "INSTRUMENT PANEL";

            // Mechanical
            if (combined.Contains("radiator") || combined.Contains("condenser") || combined.Contains("strut") ||
                combined.Contains("control arm") || combined.Contains("suspension"))
                return "MECHANICAL";

            // Materials / misc
            if (combined.Contains("cover car") || combined.Contains("clean") || combined.Contains("mask") ||
                combined.Contains("grease") || combined.Contains("adhesive") || combined.Contains("hazard") ||
                combined.Contains("disposal") || combined.Contains("hardware") || combined.Contains("refinish material") ||
                combined.Contains("spray out") || combined.Contains("color tint") || combined.Contains("sealer") ||
                combined.Contains("paint") || combined.Contains("primer"))
                return "MISCELLANEOUS OPERATIONS";

            return "MISCELLANEOUS OPERATIONS";
        }

        // Valid CCC/Mitchell operation type codes
        private static readonly HashSet<string> _validOpTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Replace", "Repl", "R&I", "Rpr", "Repair", "Refinish", "Refn", "Blend", "Blnd",
            "O/H", "Sublet", "Subl", "Add", "New", "Algn"
        };

        // Garbage patterns in part names — UI chrome, part numbers, navigation text
        private static readonly string[] _garbagePartPatterns = new[]
        {
            "app.xaml", "desktop", ".exe", ".dll", ".cs", ".json", ".xml",
            "workfile", "checkout", "diagnostics", "actions", "filter",
            "section", "operations", "groups", "description", "recond",
            "compare", "guide", "motor", "tire", "partcodes", "advisor",
            "frame", "notes", "hnotes", "save", "print", "edit", "view",
            "estimate properties", "rates and rules", "delete estimate",
            "preliminary", "source parts", "line operation", "other charg",
            "totals", "ext. price", "front view", "rear view", "side view",
            "exploded view", "top view", "bottom view"
        };

        private static bool IsValidEstimateOperation(ParsedEstimateLine op)
        {
            // Must have a recognized operation type
            if (string.IsNullOrWhiteSpace(op.OperationType)) return false;
            if (!_validOpTypes.Contains(op.OperationType.Trim())) return false;

            var partName = op.PartName ?? "";
            if (partName.Length < 3) return false;

            // Reject garbage with no real letters (dots, bullets, symbols like "• •")
            if (!System.Text.RegularExpressions.Regex.IsMatch(partName, @"[a-zA-Z]{2,}")) return false;

            var partLower = partName.ToLowerInvariant();

            // Reject purely numeric/price strings
            if (System.Text.RegularExpressions.Regex.IsMatch(partName, @"^\$?\d[\d,.\s]*$")) return false;

            // Reject part numbers (alphanumeric codes like "24...", "530...", "20...")
            if (System.Text.RegularExpressions.Regex.IsMatch(partName.Trim(), @"^\d{2,}")) return false;

            // Reject UI chrome and navigation garbage
            foreach (var pattern in _garbagePartPatterns)
            {
                if (partLower.Contains(pattern)) return false;
            }

            // Reject single-word entries that are common component sub-parts (bolt, nut, rivet, plug, bracket, screw)
            // These come from CCC parts catalog, not estimate line items
            var singleWordGarbage = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bolt", "nut", "rivet", "plug", "screw", "clip", "retainer", "bracket",
                "fastener", "washer", "spacer", "stud", "pin", "grommet"
            };
            var trimmedPart = partName.Trim();
            if (!trimmedPart.Contains(' ') && singleWordGarbage.Contains(trimmedPart)) return false;

            return true;
        }

        private static Color GetOperationTypeColor(string opType)
        {
            return opType.ToUpperInvariant() switch
            {
                "REPLACE" => Color.FromArgb(255, 60, 130, 200),
                "R&I" => Color.FromArgb(255, 100, 160, 80),
                "RPR" or "REPAIR" => Color.FromArgb(255, 200, 150, 50),
                "REFINISH" => Color.FromArgb(255, 150, 80, 200),
                "BLEND" => Color.FromArgb(255, 180, 100, 180),
                "O/H" => Color.FromArgb(255, 200, 80, 80),
                "SUBLET" => Color.FromArgb(255, 100, 100, 160),
                _ => Color.FromArgb(255, 100, 100, 100)
            };
        }
    }
}
