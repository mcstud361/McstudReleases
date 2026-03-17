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

        // AI analysis
        private StackPanel? _aiAnalysisPanel;

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

            // Score banner row
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

            // Content area (rebuilt dynamically)
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

            // AI pattern analysis panel (populated after OCR)
            _aiAnalysisPanel = new StackPanel
            {
                Spacing = 4,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };
            outerStack.Children.Add(_aiAnalysisPanel);

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

            _analysisContentStack.Children.Clear();

            var hasOps = result != null && result.DetectedOperations.Count > 0;
            var hasCoaching = snapshot != null && snapshot.Suggestions.Count > 0;

            // Show vehicle/customer info if detected
            if (snapshot != null && (!string.IsNullOrEmpty(snapshot.VehicleInfo) || !string.IsNullOrEmpty(snapshot.CustomerName)))
            {
                var infoText = "";
                if (!string.IsNullOrEmpty(snapshot.VehicleInfo))
                    infoText += snapshot.VehicleInfo;
                if (!string.IsNullOrEmpty(snapshot.CustomerName))
                    infoText += (infoText.Length > 0 ? "  |  " : "") + snapshot.CustomerName;

                _analysisContentStack.Children.Add(new TextBlock
                {
                    Text = infoText,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 180, 220)),
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            // Similar estimate comparison bar
            if (hasOps && result != null)
            {
                var similarBar = BuildSimilarEstimateBar(result, snapshot);
                if (similarBar != null)
                    _analysisContentStack.Children.Add(similarBar);
            }

            // If no data at all, show placeholder
            if (!hasOps && !hasCoaching)
            {
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

            // --- Build part groups from detected operations ---
            var partGroups = new Dictionary<string, List<OcrDetectedOperation>>(StringComparer.OrdinalIgnoreCase);
            var partOrder = new List<string>();

            if (hasOps)
            {
                foreach (var op in result!.DetectedOperations)
                {
                    var partName = NormalizePartName(op.PartName);
                    if (!partGroups.ContainsKey(partName))
                    {
                        partGroups[partName] = new List<OcrDetectedOperation>();
                        partOrder.Add(partName);
                    }
                    partGroups[partName].Add(op);
                }
            }

            // --- Categorize coaching suggestions ---
            var matchedSuggestions = new Dictionary<string, List<McstudDesktop.Models.CoachingSuggestion>>(StringComparer.OrdinalIgnoreCase);
            var unmatchedSuggestions = new List<McstudDesktop.Models.CoachingSuggestion>();
            var sopSuggestions = new List<McstudDesktop.Models.CoachingSuggestion>();

            if (hasCoaching)
            {
                var activeSuggestions = snapshot!.Suggestions
                    .Where(s => !s.IsDismissed)
                    .OrderBy(s => s.IsConfirmedOnEstimate ? 1 : 0)
                    .ThenByDescending(s => s.Severity)
                    .ThenByDescending(s => s.EstimatedCost)
                    .ToList();

                foreach (var suggestion in activeSuggestions)
                {
                    if (suggestion.Source == "SOP List" && string.IsNullOrEmpty(suggestion.TriggeredBy))
                    {
                        sopSuggestions.Add(suggestion);
                    }
                    else if (!string.IsNullOrEmpty(suggestion.TriggeredBy))
                    {
                        // Try to match to a part group
                        var matched = false;
                        foreach (var partName in partOrder)
                        {
                            if (partName.Contains(suggestion.TriggeredBy, StringComparison.OrdinalIgnoreCase) ||
                                suggestion.TriggeredBy.Contains(partName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!matchedSuggestions.ContainsKey(partName))
                                    matchedSuggestions[partName] = new List<McstudDesktop.Models.CoachingSuggestion>();
                                matchedSuggestions[partName].Add(suggestion);
                                matched = true;
                                break;
                            }
                        }
                        if (!matched)
                        {
                            // Create a virtual part group for this suggestion's TriggeredBy
                            var triggerPart = NormalizePartName(suggestion.TriggeredBy);
                            if (!matchedSuggestions.ContainsKey(triggerPart))
                            {
                                matchedSuggestions[triggerPart] = new List<McstudDesktop.Models.CoachingSuggestion>();
                                if (!partOrder.Contains(triggerPart, StringComparer.OrdinalIgnoreCase))
                                    partOrder.Add(triggerPart);
                            }
                            matchedSuggestions[triggerPart].Add(suggestion);
                        }
                    }
                    else
                    {
                        unmatchedSuggestions.Add(suggestion);
                    }
                }
            }

            // --- Summary line ---
            if (hasCoaching)
            {
                var allActive = snapshot!.Suggestions.Where(s => !s.IsDismissed).ToList();
                var missing = allActive.Where(s => !s.IsConfirmedOnEstimate).ToList();
                var confirmed = allActive.Where(s => s.IsConfirmedOnEstimate).ToList();

                if (missing.Count > 0)
                {
                    var critical = missing.Count(s => s.Severity == McstudDesktop.Models.CoachingSeverity.Critical);
                    var high = missing.Count(s => s.Severity == McstudDesktop.Models.CoachingSeverity.High);
                    var summaryParts = new List<string>();
                    if (critical > 0) summaryParts.Add($"{critical} critical");
                    if (high > 0) summaryParts.Add($"{high} high priority");
                    var other = missing.Count - critical - high;
                    if (other > 0) summaryParts.Add($"{other} other");

                    _analysisContentStack.Children.Add(new TextBlock
                    {
                        Text = $"Missing: {string.Join(", ", summaryParts)}    |    On estimate: {confirmed.Count}",
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 240, 190, 80)),
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                }
                else if (confirmed.Count > 0)
                {
                    _analysisContentStack.Children.Add(new TextBlock
                    {
                        Text = $"All {confirmed.Count} suggested operations found on estimate!",
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 200, 120)),
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                }
            }

            // --- Render per-part groups ---
            foreach (var partName in partOrder)
            {
                // Part header
                _analysisContentStack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 38, 45, 55)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 8, 0, 2),
                    Child = new TextBlock
                    {
                        Text = $"\u2500\u2500 {partName} \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500",
                        FontSize = 13,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                });

                // Operations in this part group
                if (partGroups.ContainsKey(partName))
                {
                    foreach (var op in partGroups[partName])
                        _analysisContentStack.Children.Add(CreateOperationRow(op));
                }

                // Coaching suggestions matched to this part
                if (matchedSuggestions.ContainsKey(partName))
                {
                    foreach (var suggestion in matchedSuggestions[partName])
                    {
                        var card = BuildCoachingSuggestionCard(suggestion);
                        card.Margin = new Thickness(16, 1, 0, 1);
                        _analysisContentStack.Children.Add(card);
                    }
                }
            }

            // --- Additional Checks (unmatched non-SOP suggestions) ---
            if (unmatchedSuggestions.Count > 0)
            {
                _analysisContentStack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 38, 45, 55)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 8, 0, 2),
                    Child = new TextBlock
                    {
                        Text = "\u2500\u2500 Additional Checks \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500",
                        FontSize = 13,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 140, 170)),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                });

                foreach (var suggestion in unmatchedSuggestions)
                    _analysisContentStack.Children.Add(BuildCoachingSuggestionCard(suggestion));
            }

            // --- SOP Operations ---
            if (sopSuggestions.Count > 0)
            {
                _analysisContentStack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 38, 45, 55)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 8, 0, 2),
                    Child = new TextBlock
                    {
                        Text = "\u2500\u2500 SOP Operations \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500",
                        FontSize = 13,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 150, 255)),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                });

                foreach (var suggestion in sopSuggestions)
                    _analysisContentStack.Children.Add(BuildCoachingSuggestionCard(suggestion));
            }

            // --- Dismiss All button ---
            if (hasCoaching)
            {
                var allActiveDismiss = snapshot!.Suggestions.Where(s => !s.IsDismissed).ToList();
                if (allActiveDismiss.Count > 0)
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
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    dismissAllBtn.Click += (s, e) =>
                    {
                        foreach (var suggestion in allActiveDismiss)
                            LiveCoachingService.Instance.DismissSuggestion(suggestion.Id);
                        RebuildAnalysisDisplay();
                    };
                    _analysisContentStack.Children.Add(dismissAllBtn);
                }
            }
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

            // Parts detected from structured ops
            var detectedParts = DetectParts(result, "");
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

                // Always run advisor analysis when we have OCR text — even with 0 detected ops
                // the fuzzy scanner + scoring engine can find useful suggestions
                if (result.HasChanges && !string.IsNullOrEmpty(result.RawText))
                {
                    RunAdvisorAnalysis(result);
                }

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

        // === SOP DEFINITIONS ===
        private record SopOperation(string Name, string[] Keywords, string Detail);

        private static readonly SopOperation[] _sopElectrical = new[]
        {
            new SopOperation("Disconnect/Reconnect Battery", new[] { "disconnect battery", "reconnect battery", "disconnect/reconnect battery", "d/c battery", "battery disconnect" }, "0.3-0.4 hrs"),
            new SopOperation("Test Battery Condition", new[] { "test battery", "battery condition", "battery test" }, "0.2 hrs"),
            new SopOperation("Battery Support/Memory Saver", new[] { "memory saver", "battery support", "ks-100" }, "$15, 0.2 hrs"),
            new SopOperation("EV/Hybrid High Voltage System Safe", new[] { "high voltage", "ev safe", "hybrid safe", "hv system" }, "0.5 hrs"),
        };

        private static readonly SopOperation[] _sopDiagnostics = new[]
        {
            new SopOperation("Pre-Repair Scan", new[] { "pre-scan", "pre scan", "pre-repair scan", "prescan" }, "$150 or 1.0 hr"),
            new SopOperation("Post-Repair Scan", new[] { "post-scan", "post scan", "post-repair scan", "postscan" }, "$150 or 1.0 hr"),
            new SopOperation("In-Process Scan", new[] { "in-process scan", "in process scan", "mid-repair scan" }, "$150 or 1.0 hr"),
            new SopOperation("Setup Scan Tool", new[] { "setup scan tool", "scan tool setup" }, "0.2 hrs"),
            new SopOperation("Dynamic Systems Verification", new[] { "dynamic systems", "dynamic verification", "systems verification" }, "1.0 hr"),
            new SopOperation("OEM Research", new[] { "oem research", "oem procedure", "oem position" }, "$50, 1.0 hr"),
            new SopOperation("ADAS Diagnostic Report", new[] { "adas diagnostic", "adas report" }, "$25"),
            new SopOperation("Gateway Unlock", new[] { "gateway unlock", "security gateway" }, "0.1 hrs"),
            new SopOperation("Drive Cycle", new[] { "drive cycle", "test drive" }, "0.7 hrs"),
        };

        private static readonly SopOperation[] _sopMisc = new[]
        {
            new SopOperation("Clean for Delivery", new[] { "clean for delivery", "final clean", "detail clean" }, "1.0 hr"),
            new SopOperation("Glass Cleaner", new[] { "glass cleaner" }, "$2"),
            new SopOperation("Mask and Protect", new[] { "mask and protect", "mask & protect", "masking" }, "$10, 0.5 hr"),
            new SopOperation("Parts Disposal", new[] { "parts disposal", "disposal fee" }, "$25"),
            new SopOperation("Hazardous Waste", new[] { "hazardous waste", "haz waste", "hazmat" }, "$7.50"),
            new SopOperation("Misc Hardware", new[] { "misc hardware", "miscellaneous hardware", "misc. hardware" }, "$15"),
            new SopOperation("Steering Wheel/Seat/Floor Mat Cover", new[] { "seat cover", "floor mat cover", "steering wheel cover", "protective cover" }, "$5, 0.2 hr"),
            new SopOperation("Pre Wash and Degrease", new[] { "pre wash", "pre-wash", "degrease" }, "$10, 0.5 hr"),
            new SopOperation("Collision Wrap", new[] { "collision wrap", "crash wrap" }, "$25, 0.3 hr"),
        };

        // === ANALYSIS PIPELINE ===

        private void RunAdvisorAnalysis(ScreenOcrResult result)
        {
            if (_aiAnalysisPanel == null) return;
            _aiAnalysisPanel.Children.Clear();

            try
            {
                var rawText = result.RawText ?? "";
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    _aiAnalysisPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                // Step 1: Detect parts from OCR text + structured ops
                var detectedParts = DetectParts(result, rawText);

                if (detectedParts.Count == 0)
                {
                    _aiAnalysisPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                // Step 2: "Detected on screen" header
                var headerBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 50, 65)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                headerBorder.Child = new TextBlock
                {
                    Text = $"Detected on screen: {string.Join(", ", detectedParts.Take(8))}{(detectedParts.Count > 8 ? $" +{detectedParts.Count - 8} more" : "")}",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                    TextWrapping = TextWrapping.Wrap
                };
                _aiAnalysisPanel.Children.Add(headerBorder);

                // Step 3: Part suggestions from uploaded estimates
                var vehicleInfo = ExtractVehicleInfo(rawText);
                var partSuggestionsSection = BuildPartSuggestionsSection(detectedParts, result, vehicleInfo);
                if (partSuggestionsSection != null)
                    _aiAnalysisPanel.Children.Add(partSuggestionsSection);

                // Step 4: SOP missing operations check (uses structured ops, not raw text)
                var sopSection = BuildSopMissingSection(rawText, result);
                if (sopSection != null)
                    _aiAnalysisPanel.Children.Add(sopSection);

                _aiAnalysisPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenMonitor] Analysis error: {ex.Message}");
                _aiAnalysisPanel.Visibility = Visibility.Collapsed;
            }
        }

        private List<string> DetectParts(ScreenOcrResult result, string rawText)
        {
            // ONLY use structured operations from the parser — NOT raw OCR text.
            // Raw text includes sidebar navigation (FRONT BUMPER & GRILLE, FENDER, etc.)
            // which are section headers, not actual estimate lines.
            var detectedParts = new List<string>();
            var seenParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var op in result.DetectedOperations)
            {
                if (string.IsNullOrEmpty(op.PartName)) continue;

                // Map to canonical name if possible
                var partLower = op.PartName.ToLowerInvariant();
                string canonical = op.PartName;
                foreach (var (pattern, canonicalName) in _scanParts)
                {
                    if (partLower.Contains(pattern))
                    {
                        canonical = canonicalName;
                        break;
                    }
                }

                if (seenParts.Add(canonical))
                    detectedParts.Add(canonical);
            }

            return detectedParts;
        }

        private static string? ExtractVehicleInfo(string rawText)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                rawText,
                @"(20\d{2})\s+(\w+)\s+(\w+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value : null;
        }

        private UIElement? BuildPartSuggestionsSection(List<string> detectedParts, ScreenOcrResult result, string? vehicleInfo)
        {
            var smartService = SmartSuggestionService.Instance;
            var container = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };

            // Build a lookup of detected op types from structured ops
            var opTypeLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var op in result.DetectedOperations)
            {
                if (!string.IsNullOrEmpty(op.PartName) && !string.IsNullOrEmpty(op.OperationType))
                    opTypeLookup.TryAdd(op.PartName, op.OperationType);
            }

            bool anyData = false;

            foreach (var partName in detectedParts)
            {
                var opType = opTypeLookup.GetValueOrDefault(partName, "Replace");
                var suggestions = smartService.GetSuggestionsForPart(partName, opType, vehicleInfo);

                if (!suggestions.HasData) continue;
                anyData = true;

                // Per-part card
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 40)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var cardStack = new StackPanel { Spacing = 4 };

                // Part name header + match count
                var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                headerRow.Children.Add(new TextBlock
                {
                    Text = partName,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 140)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                headerRow.Children.Add(new TextBlock
                {
                    Text = $"Based on {suggestions.MatchCount} estimates",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 140)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                cardStack.Children.Add(headerRow);

                // Each suggestion row
                foreach (var sugOp in suggestions.ManualOperations.Take(10))
                {
                    var row = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 42, 47, 56)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 1, 0, 1)
                    };

                    var rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Left: description
                    var descText = new TextBlock
                    {
                        Text = sugOp.Description,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Grid.SetColumn(descText, 0);
                    rowGrid.Children.Add(descText);

                    // Right: frequency badge + hours/price
                    var rightStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

                    // Frequency badge
                    var freqBadge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(40, 100, 180, 255)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 1, 5, 1),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    freqBadge.Child = new TextBlock
                    {
                        Text = $"{sugOp.TimesUsed}/{suggestions.MatchCount}",
                        FontSize = 9,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
                    };
                    rightStack.Children.Add(freqBadge);

                    if (sugOp.LaborHours > 0)
                    {
                        rightStack.Children.Add(new TextBlock
                        {
                            Text = $"{sugOp.LaborHours:F1}h",
                            FontSize = 10,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    }
                    if (sugOp.Price > 0)
                    {
                        rightStack.Children.Add(new TextBlock
                        {
                            Text = $"${sugOp.Price:N2}",
                            FontSize = 10,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    }

                    Grid.SetColumn(rightStack, 1);
                    rowGrid.Children.Add(rightStack);

                    row.Child = rowGrid;
                    cardStack.Children.Add(row);
                }

                card.Child = cardStack;
                container.Children.Add(card);
            }

            if (!anyData)
            {
                container.Children.Add(new TextBlock
                {
                    Text = "No suggestion data yet — upload more estimates to get part-based recommendations.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }

            return container;
        }

        private UIElement? BuildSopMissingSection(string rawText, ScreenOcrResult? result = null)
        {
            // Build search text from STRUCTURED operations only — not raw OCR text.
            // Raw text includes sidebar/diagram navigation that causes false positives.
            string lowerText;
            if (result != null && result.DetectedOperations.Count > 0)
            {
                lowerText = string.Join(" | ", result.DetectedOperations
                    .Select(op => $"{op.Description} {op.PartName} {op.OperationType} {op.RawLine}"))
                    .ToLowerInvariant();
            }
            else
            {
                lowerText = rawText.ToLowerInvariant();
            }

            var missingElectrical = new List<SopOperation>();
            var missingDiagnostics = new List<SopOperation>();
            var missingMisc = new List<SopOperation>();

            foreach (var sop in _sopElectrical)
                if (!sop.Keywords.Any(k => lowerText.Contains(k)))
                    missingElectrical.Add(sop);

            foreach (var sop in _sopDiagnostics)
                if (!sop.Keywords.Any(k => lowerText.Contains(k)))
                    missingDiagnostics.Add(sop);

            foreach (var sop in _sopMisc)
                if (!sop.Keywords.Any(k => lowerText.Contains(k)))
                    missingMisc.Add(sop);

            int totalMissing = missingElectrical.Count + missingDiagnostics.Count + missingMisc.Count;

            var container = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };

            if (totalMissing == 0)
            {
                // All SOP items present — green banner
                var allGoodBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 60, 40)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8)
                };
                allGoodBorder.Child = new TextBlock
                {
                    Text = "All SOP operations detected",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 200, 120))
                };
                container.Children.Add(allGoodBorder);
                return container;
            }

            // Amber section header
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 65, 50, 25)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6)
            };
            headerBorder.Child = new TextBlock
            {
                Text = $"Missing SOP Operations ({totalMissing})",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 240, 190, 80))
            };
            container.Children.Add(headerBorder);

            // Sub-sections
            if (missingElectrical.Count > 0)
                container.Children.Add(BuildSopSubSection("Electrical", Color.FromArgb(255, 80, 150, 220), missingElectrical));
            if (missingDiagnostics.Count > 0)
                container.Children.Add(BuildSopSubSection("Vehicle Diagnostics", Color.FromArgb(255, 150, 100, 200), missingDiagnostics));
            if (missingMisc.Count > 0)
                container.Children.Add(BuildSopSubSection("Misc", Color.FromArgb(255, 80, 180, 170), missingMisc));

            return container;
        }

        private static UIElement BuildSopSubSection(string title, Color accentColor, List<SopOperation> missingOps)
        {
            var section = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 2) };

            section.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accentColor),
                Margin = new Thickness(4, 2, 0, 2)
            });

            foreach (var sop in missingOps)
            {
                var row = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 42, 47, 56)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 1, 0, 1)
                };

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameText = new TextBlock
                {
                    Text = sop.Name,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(nameText, 0);
                rowGrid.Children.Add(nameText);

                var detailText = new TextBlock
                {
                    Text = sop.Detail,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 170)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(detailText, 1);
                rowGrid.Children.Add(detailText);

                row.Child = rowGrid;
                section.Children.Add(row);
            }

            return section;
        }

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
