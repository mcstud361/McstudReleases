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
using McStudDesktop.Services;
using McstudDesktop;
using System.Security.Cryptography;
using System.Text;
using Windows.Storage.Pickers;
using Windows.Storage;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Clean, focused Import & Learn view
    /// Upload estimates → System learns patterns → Use in Damage Estimate
    ///
    /// License tiers:
    /// - Client: Parse estimates and get suggestions (read-only)
    /// - Shop/Admin: Full access including training/learning
    /// </summary>
    public sealed class EstimateUploadView : UserControl
    {
        private readonly EstimateLearningService _learningService;
        private readonly EstimateStatisticsService _statsService;
        private readonly EstimateQualityService _qualityService;
        private readonly LearningFeedbackService _feedbackService;
        private readonly LearningHealthService _healthService;

        // Header
        private TextBlock? _statsText;

        // Inner tab infrastructure
        private Border? _scrubberTabButton;
        private Border? _learningTabButton;
        private StackPanel? _scrubberTabContent;
        private StackPanel? _learningTabContent;
        private int _activeSubTab = 0; // 0=Scrubber, 1=Learning

        // Scrubber tab UI
        private Border? _scrubUploadDropZone;
        private TextBlock? _scrubDropZoneText;
        private Button? _scrubUploadButton;
        private TextBox? _scrubPasteArea;
        private Grid? _scrubSideBySideGrid;
        private Border? _scrubLeftPanel;
        private ScrollViewer? _scrubRightScroll;
        private Button? _layoutToggleButton;
        private bool _isSideBySideLayout = true;
        private TextBlock? _scrubResultsTitle;
        private ListView? _scrubParsedItemsList;
        private ProgressRing? _scrubProgressRing;
        private Button? _scrubClearButton;
        private Button? _scrubCopyButton;
        private Button? _scrubMustHavesButton;
        private TextBlock? _scrubStatusText;
        private Border? _scrubClientModeBanner;

        // Scrubber-only: Quality Assessment UI
        private Border? _qualitySection;
        private TextBlock? _qualityScoreText;
        private TextBlock? _qualityGradeText;
        private TextBlock? _qualityWeightText;
        private StackPanel? _qualityFlagsPanel;
        private CheckBox? _includeOutliersCheckbox;
        private EstimateQualityRecord? _currentQualityRecord;

        // Scrubber-only: Estimate Completeness Scoring
        private EstimateScoringPanel? _scoringPanel;
        private readonly EstimateScoringService _scoringService;
        private TextBlock? _refMatchStatusText;

        // Smart analysis (data only — UI merged into scoring panel)
        private readonly SmartEstimateAnalyzerService _analyzerService;
        private AnalysisResult? _currentAnalysis;

        // Reference matching
        private readonly EstimateReferenceMatcherService _referenceMatcher;

        // Learning tab UI
        private Border? _learnUploadDropZone;
        private TextBlock? _learnDropZoneText;
        private Button? _learnUploadButton;
        private TextBox? _learnPasteArea;
        private Border? _learnResultsSection;
        private TextBlock? _learnResultsTitle;
        private ListView? _learnParsedItemsList;
        private ProgressRing? _learnProgressRing;
        private Button? _learnClearButton;
        private Button? _learnButton;
        private TextBlock? _learnStatusText;
        private Border? _learnClientModeBanner;
        private Button? _publishButton;
        private Button? _exportBaselineButton;
        private Border? _learningSummarySection;
        private StackPanel? _learningSummaryContent;

        // Events
        public event EventHandler<TrainingCompletedEventArgs>? OnTrainingCompleted;

        // Parsed data (shared between tabs)
        private List<ParsedEstimateLine> _parsedLines = new();

        // Parse context
        private enum ParseContext { Scrubber, Learning }

        public EstimateUploadView()
        {
            _learningService = EstimateLearningService.Instance;
            _statsService = EstimateStatisticsService.Instance;
            _analyzerService = SmartEstimateAnalyzerService.Instance;
            _qualityService = EstimateQualityService.Instance;
            _feedbackService = LearningFeedbackService.Instance;
            _healthService = LearningHealthService.Instance;
            _scoringService = EstimateScoringService.Instance;
            _referenceMatcher = EstimateReferenceMatcherService.Instance;
            BuildUI();
            UpdateStats();
            UpdateUIForLicenseTier();
        }

        /// <summary>
        /// Update UI elements based on license tier
        /// </summary>
        private void UpdateUIForLicenseTier()
        {
            bool canTrain = _learningService.CanTrain;
            bool canTrainStandard = _learningService.CanTrainStandard;

            // Show/hide client mode banners (both tabs)
            if (_scrubClientModeBanner != null)
                _scrubClientModeBanner.Visibility = canTrain ? Visibility.Collapsed : Visibility.Visible;
            if (_learnClientModeBanner != null)
                _learnClientModeBanner.Visibility = canTrain ? Visibility.Collapsed : Visibility.Visible;

            // Update learn button state and label based on tier
            if (_learnButton != null)
            {
                _learnButton.IsEnabled = canTrain;
                if (!canTrain)
                {
                    _learnButton.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE72E", FontSize = 14 }, // Lock icon
                            new TextBlock { Text = "Learning Locked (Client)", FontSize = 12 }
                        }
                    };
                    _learnButton.Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60));
                    ToolTipService.SetToolTip(_learnButton, "Client licenses cannot train the learning system. Contact admin for shop license.");
                }
                else if (canTrainStandard)
                {
                    ToolTipService.SetToolTip(_learnButton, "Admin: Imported estimates train the STANDARD knowledge base for all users.");
                }
                else
                {
                    ToolTipService.SetToolTip(_learnButton, "Your imported estimates are saved to your personal learning data.");
                }
            }

            // Show/hide publish button (Admin only)
            if (_publishButton != null)
                _publishButton.Visibility = canTrainStandard ? Visibility.Visible : Visibility.Collapsed;

            // Show/hide publish knowledge button (Admin only)
            if (_exportBaselineButton != null)
                _exportBaselineButton.Visibility = canTrainStandard ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildUI()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)),
                Padding = new Thickness(0)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var mainStack = new StackPanel
            {
                Spacing = 16,
                Padding = new Thickness(20)
            };

            // === HEADER ===
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Spacing = 4 };
            titleStack.Children.Add(new TextBlock
            {
                Text = "Estimate Upload",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "SMART parsing: Auto-detects CCC/Mitchell/Audatex, normalizes part names, links operations",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            _statsText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statsText, 1);
            headerGrid.Children.Add(_statsText);

            mainStack.Children.Add(headerGrid);

            // === INNER TAB BAR (styled like main app tabs) ===
            var innerTabBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _scrubberTabButton = CreateInnerTabButton("\uE7BA", "Scrubber", 0);
            _learningTabButton = CreateInnerTabButton("\uE7BE", "Estimate Learning", 1);
            innerTabBar.Children.Add(_scrubberTabButton);
            innerTabBar.Children.Add(_learningTabButton);
            mainStack.Children.Add(innerTabBar);

            // === TAB CONTENTS ===
            _scrubberTabContent = BuildScrubberTab();
            _learningTabContent = BuildLearningTab();
            mainStack.Children.Add(_scrubberTabContent);
            mainStack.Children.Add(_learningTabContent);

            // === HELP TEXT (shared) ===
            var helpBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 35)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var helpText = new TextBlock
            {
                Text = "WHAT THIS TAB DOES\n" +
                       "Feed it an estimate (PDF or pasted text) and it reads every line \u2014 parts, labor,\n" +
                       "refinish, additional ops \u2014 so you can analyze it, learn from it, or scrub for missing ops.\n\n" +
                       "SCRUBBER TAB\n" +
                       "Parse an estimate and it automatically scores for missing operations.\n" +
                       "Use \u201cCopy to Clipboard\u201d to grab the missing items list for CCC.\n\n" +
                       "ESTIMATE LEARNING TAB\n" +
                       "Parse an estimate and hit \u201cLearn from This\u201d to save the part\u2192operation patterns.\n" +
                       "Next time you see that part, McStud remembers what ops usually go with it.\n\n" +
                       "GETTING ESTIMATES IN\n" +
                       "   \u2022 PDF: Click \"Upload\" or drag-drop the file onto the upload area.\n" +
                       "   \u2022 Paste: Ctrl+A / Ctrl+C from CCC ONE, paste into the text box, hit Parse.\n" +
                       "   \u2022 Batch: Drop multiple PDFs at once for bulk processing.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            };
            helpBorder.Child = helpText;
            mainStack.Children.Add(helpBorder);

            scroll.Content = mainStack;
            mainBorder.Child = scroll;
            Content = mainBorder;

            // Default to Scrubber tab
            SelectInnerTab(0);
        }

        #region Inner Tab Infrastructure

        private Border CreateInnerTabButton(string glyph, string text, int tabIndex)
        {
            bool isSelected = tabIndex == _activeSubTab;

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

            border.PointerPressed += (s, e) => SelectInnerTab(tabIndex);
            border.PointerEntered += (s, e) =>
            {
                if (tabIndex != _activeSubTab)
                    border.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
            };
            border.PointerExited += (s, e) =>
            {
                if (tabIndex != _activeSubTab)
                    border.Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35));
            };

            return border;
        }

        private void UpdateInnerTabButtonStyle(Border? button, int tabIndex)
        {
            if (button == null) return;

            bool isSelected = tabIndex == _activeSubTab;

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
                    else if (child is TextBlock lbl)
                    {
                        lbl.FontWeight = isSelected
                            ? Microsoft.UI.Text.FontWeights.SemiBold
                            : Microsoft.UI.Text.FontWeights.Normal;
                        lbl.Foreground = new SolidColorBrush(isSelected
                            ? Colors.White
                            : Color.FromArgb(255, 150, 150, 150));
                    }
                }
            }
        }

        private void SelectInnerTab(int index)
        {
            _activeSubTab = index;

            if (_scrubberTabContent != null)
                _scrubberTabContent.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_learningTabContent != null)
                _learningTabContent.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;

            UpdateInnerTabButtonStyle(_scrubberTabButton, 0);
            UpdateInnerTabButtonStyle(_learningTabButton, 1);
        }

        #endregion

        #region Tab Content Builders

        private StackPanel BuildScrubberTab()
        {
            var content = new StackPanel { Spacing = 16 };

            // Top bar: description + Must-Haves gear button (prominent, right-aligned)
            var topBar = new Grid();
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var descText = new TextBlock
            {
                Text = "Scrub an estimate for missing operations. Parse a PDF or pasted text \u2014 quality + scoring appear automatically.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 165, 200)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(descText, 0);
            topBar.Children.Add(descText);

            _scrubMustHavesButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE713", FontSize = 16 },
                        new TextBlock { Text = "Must-Haves", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 90)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 10, 16, 10),
                CornerRadius = new CornerRadius(6)
            };
            _scrubMustHavesButton.Click += MustHavesButton_Click;
            ToolTipService.SetToolTip(_scrubMustHavesButton,
                "Configure which operations must appear on every estimate.\n" +
                "The Scrubber checks for these plus commonly missed items.");
            Grid.SetColumn(_scrubMustHavesButton, 1);
            topBar.Children.Add(_scrubMustHavesButton);

            content.Children.Add(topBar);

            // Upload section
            var uploadSection = BuildUploadSection(
                out _scrubUploadDropZone, out _scrubDropZoneText, out _scrubUploadButton,
                out _scrubPasteArea, out var scrubParseBtn);

            _scrubUploadButton.Click += (s, e) => HandleUpload(ParseContext.Scrubber);
            _scrubUploadDropZone.DragOver += (s, e) => HandleDragOver(s, e, _scrubUploadDropZone, _scrubDropZoneText);
            _scrubUploadDropZone.DragLeave += (s, e) => ResetDropZoneAppearance(_scrubUploadDropZone, _scrubDropZoneText);
            _scrubUploadDropZone.Drop += async (s, e) => await HandleDrop(e, ParseContext.Scrubber);
            scrubParseBtn.Click += (s, e) => HandleParsePaste(ParseContext.Scrubber);

            content.Children.Add(uploadSection);

            // Client mode banner
            _scrubClientModeBanner = BuildClientModeBanner();
            content.Children.Add(_scrubClientModeBanner);

            // Action buttons: Copy to Clipboard + Clear
            var scrubActions = new StackPanel { Orientation = Orientation.Horizontal };

            _scrubCopyButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8C8", FontSize = 14 },
                        new TextBlock { Text = "Copy to Clipboard", FontSize = 12 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 180)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(8, 0, 0, 0)
            };
            _scrubCopyButton.Click += CopyToClipboard_Click;
            ToolTipService.SetToolTip(_scrubCopyButton,
                "Copy all missing items to clipboard.\n" +
                "Includes must-haves, commonly missed, and learned pattern suggestions.");
            scrubActions.Children.Add(_scrubCopyButton);

            _scrubClearButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                        new TextBlock { Text = "Clear", FontSize = 12 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 100, 40, 40)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(8, 0, 0, 0)
            };
            _scrubClearButton.Click += (s, e) => ClearForTab(ParseContext.Scrubber);
            scrubActions.Children.Add(_scrubClearButton);

            // Layout toggle button
            _layoutToggleButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8A4", FontSize = 12 },
                        new TextBlock { Text = "Stacked", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(8, 0, 0, 0),
                CornerRadius = new CornerRadius(4)
            };
            _layoutToggleButton.Click += (s, e) => ToggleScrubLayout();
            ToolTipService.SetToolTip(_layoutToggleButton, "Switch between side-by-side and stacked layout");
            scrubActions.Children.Add(_layoutToggleButton);

            // === Results container grid ===
            _scrubSideBySideGrid = new Grid
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0),
                ColumnSpacing = 12
            };
            _scrubSideBySideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            _scrubSideBySideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // -- Left panel: "Your Estimate" --
            _scrubLeftPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 30, 25)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };
            var leftStack = new StackPanel { Spacing = 8 };

            leftStack.Children.Add(new TextBlock
            {
                Text = "\uE8A5  Your Estimate",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var resultsHeader = new Grid();
            resultsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            resultsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _scrubResultsTitle = new TextBlock
            {
                Text = "Parsed Lines",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_scrubResultsTitle, 0);
            resultsHeader.Children.Add(_scrubResultsTitle);

            _scrubProgressRing = new ProgressRing
            {
                Width = 20,
                Height = 20,
                IsActive = false,
                Visibility = Visibility.Collapsed
            };
            Grid.SetColumn(_scrubProgressRing, 1);
            resultsHeader.Children.Add(_scrubProgressRing);

            Grid.SetColumn(scrubActions, 2);
            resultsHeader.Children.Add(scrubActions);

            leftStack.Children.Add(resultsHeader);
            leftStack.Children.Add(BuildLegendPanel());

            _scrubParsedItemsList = new ListView
            {
                MaxHeight = 550,
                Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)),
                SelectionMode = ListViewSelectionMode.None,
                Padding = new Thickness(4)
            };
            leftStack.Children.Add(_scrubParsedItemsList);

            _scrubLeftPanel.Child = leftStack;
            Grid.SetColumn(_scrubLeftPanel, 0);
            _scrubSideBySideGrid.Children.Add(_scrubLeftPanel);

            // -- Right panel: "Scrubber" --
            var rightBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 25, 30)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };
            var rightStack = new StackPanel { Spacing = 8 };

            rightStack.Children.Add(new TextBlock
            {
                Text = "\uE7BA  Scrubber Results",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            BuildQualitySection();
            rightStack.Children.Add(_qualitySection!);

            _scoringPanel = new EstimateScoringPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _scoringPanel.OnAddItems += ScoringPanel_OnAddItems;
            rightStack.Children.Add(_scoringPanel);

            _refMatchStatusText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };
            rightStack.Children.Add(_refMatchStatusText);

            rightBorder.Child = rightStack;

            _scrubRightScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 600,
                Content = rightBorder
            };
            Grid.SetColumn(_scrubRightScroll, 1);
            _scrubSideBySideGrid.Children.Add(_scrubRightScroll);

            content.Children.Add(_scrubSideBySideGrid);

            // Status text
            _scrubStatusText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            content.Children.Add(_scrubStatusText);

            return content;
        }

        private StackPanel BuildLearningTab()
        {
            var content = new StackPanel { Spacing = 16 };

            // Top bar: description
            content.Children.Add(new TextBlock
            {
                Text = "Teach McStud from your estimates. Parse an estimate and hit Learn to save the part\u2192operation patterns for future use. Next time you see that part, McStud remembers what ops usually go with it.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 165, 200)),
                TextWrapping = TextWrapping.Wrap
            });

            // Upload section
            var uploadSection = BuildUploadSection(
                out _learnUploadDropZone, out _learnDropZoneText, out _learnUploadButton,
                out _learnPasteArea, out var learnParseBtn);

            _learnUploadButton.Click += (s, e) => HandleUpload(ParseContext.Learning);
            _learnUploadDropZone.DragOver += (s, e) => HandleDragOver(s, e, _learnUploadDropZone, _learnDropZoneText);
            _learnUploadDropZone.DragLeave += (s, e) => ResetDropZoneAppearance(_learnUploadDropZone, _learnDropZoneText);
            _learnUploadDropZone.Drop += async (s, e) => await HandleDrop(e, ParseContext.Learning);
            learnParseBtn.Click += (s, e) => HandleParsePaste(ParseContext.Learning);

            content.Children.Add(uploadSection);

            // Client mode banner
            _learnClientModeBanner = BuildClientModeBanner();
            content.Children.Add(_learnClientModeBanner);

            // Action buttons: Learn + Clear + Publish + Publish KB
            var learnActions = new StackPanel { Orientation = Orientation.Horizontal };

            _learnButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE7BE", FontSize = 14 },
                        new TextBlock { Text = "Learn from This", FontSize = 12 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 150, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(8, 0, 0, 0)
            };
            _learnButton.Click += LearnButton_Click;
            ToolTipService.SetToolTip(_learnButton,
                "LEARN: Save patterns from this estimate for FUTURE reference.\n" +
                "Remembers what manual ops you added for each part type.\n" +
                "Data appears in the 'Learned' tab for searching later.");
            learnActions.Children.Add(_learnButton);

            _learnClearButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                        new TextBlock { Text = "Clear", FontSize = 12 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 100, 40, 40)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(8, 0, 0, 0)
            };
            _learnClearButton.Click += (s, e) => ClearForTab(ParseContext.Learning);
            learnActions.Children.Add(_learnClearButton);

            _publishButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE898", FontSize = 12 },
                        new TextBlock { Text = "Publish Learning", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 100, 80, 140)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(8, 0, 0, 0)
            };
            _publishButton.Click += PublishButton_Click;
            ToolTipService.SetToolTip(_publishButton, "Bake learned knowledge into app for distribution.");
            learnActions.Children.Add(_publishButton);

            _exportBaselineButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE72D", FontSize = 12 },
                        new TextBlock { Text = "Publish KB", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(8, 0, 0, 0),
                Visibility = _learningService.CurrentTier != LicenseTier.Client ? Visibility.Visible : Visibility.Collapsed
            };
            _exportBaselineButton.Click += ExportBaselineButton_Click;
            ToolTipService.SetToolTip(_exportBaselineButton, "Sanitize and export all learning data, bump version, then push to GitHub.");
            learnActions.Children.Add(_exportBaselineButton);

            // Results section
            BuildResultsSection(learnActions,
                out _learnResultsSection, out _learnResultsTitle,
                out _learnParsedItemsList, out _learnProgressRing);
            content.Children.Add(_learnResultsSection);

            // Learning summary (hidden until learn completes)
            _learningSummarySection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 20, 35, 25)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 12, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 140, 80)),
                BorderThickness = new Thickness(1)
            };
            _learningSummaryContent = new StackPanel { Spacing = 8 };
            _learningSummarySection.Child = _learningSummaryContent;
            content.Children.Add(_learningSummarySection);

            // Status text
            _learnStatusText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            content.Children.Add(_learnStatusText);

            return content;
        }

        #endregion

        #region UI Builder Helpers

        private StackPanel BuildUploadSection(
            out Border dropZone, out TextBlock dropText, out Button uploadBtn,
            out TextBox pasteArea, out Button parseBtn)
        {
            var section = new StackPanel { Spacing = 12 };

            // Drop zone
            dropZone = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 40)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 70, 80)),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(24),
                MinHeight = 150,
                AllowDrop = true
            };

            var uploadStack = new StackPanel
            {
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            uploadStack.Children.Add(new FontIcon
            {
                Glyph = "\uE898",
                FontSize = 40,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 150, 200))
            });

            uploadBtn = new Button
            {
                Content = "Upload Estimate Files",
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(24, 12, 24, 12),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            uploadStack.Children.Add(uploadBtn);

            dropText = new TextBlock
            {
                Text = "Drag & drop files here, or click to browse\nSupports PDF, TXT, CSV - Multiple files for batch processing",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            uploadStack.Children.Add(dropText);

            dropZone.Child = uploadStack;
            section.Children.Add(dropZone);

            // "or" text
            section.Children.Add(new TextBlock
            {
                Text = "\u2014 or paste estimate text \u2014",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            // Paste area
            pasteArea = new TextBox
            {
                PlaceholderText = "Paste estimate text here...",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 120,
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11
            };
            section.Children.Add(pasteArea);

            // Parse button
            parseBtn = new Button
            {
                Content = "Parse Pasted Text",
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 8, 16, 8),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            section.Children.Add(parseBtn);

            return section;
        }

        private void BuildResultsSection(
            StackPanel actionButtons,
            out Border section, out TextBlock title,
            out ListView list, out ProgressRing ring)
        {
            section = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 30, 25)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var resultsStack = new StackPanel { Spacing = 12 };

            // Results header
            var resultsHeader = new Grid();
            resultsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            resultsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            title = new TextBlock
            {
                Text = "Parsed Lines",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            resultsHeader.Children.Add(title);

            ring = new ProgressRing
            {
                Width = 20,
                Height = 20,
                IsActive = false,
                Visibility = Visibility.Collapsed
            };
            Grid.SetColumn(ring, 1);
            resultsHeader.Children.Add(ring);

            Grid.SetColumn(actionButtons, 2);
            resultsHeader.Children.Add(actionButtons);

            resultsStack.Children.Add(resultsHeader);

            // Legend
            resultsStack.Children.Add(BuildLegendPanel());

            // Parsed items list
            list = new ListView
            {
                MaxHeight = 350,
                Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)),
                SelectionMode = ListViewSelectionMode.None,
                Padding = new Thickness(4)
            };
            resultsStack.Children.Add(list);

            section.Child = resultsStack;
        }

        private StackPanel BuildLegendPanel()
        {
            var legendPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Margin = new Thickness(4, 4, 4, 2)
            };

            var partLegend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            partLegend.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 100, 160)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock { Text = "PART", FontSize = 9, Foreground = new SolidColorBrush(Colors.White), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
            });
            partLegend.Children.Add(new TextBlock
            {
                Text = "Estimate line items",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                VerticalAlignment = VerticalAlignment.Center
            });
            legendPanel.Children.Add(partLegend);

            var addLegend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            addLegend.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 150, 100)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock { Text = "ADD", FontSize = 9, Foreground = new SolidColorBrush(Colors.White), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
            });
            addLegend.Children.Add(new TextBlock
            {
                Text = "Manual operations (#)",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 220, 180)),
                VerticalAlignment = VerticalAlignment.Center
            });
            legendPanel.Children.Add(addLegend);

            legendPanel.Children.Add(new TextBlock
            {
                Text = "\u21b3 indented = belongs to part above",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 110, 110, 110)),
                VerticalAlignment = VerticalAlignment.Center
            });

            return legendPanel;
        }

        private Border BuildClientModeBanner()
        {
            var banner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 40, 20)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 180, 140, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            stack.Children.Add(new FontIcon
            {
                Glyph = "\uE8D7",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100))
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Client Mode: You can parse estimates and scrub for missing ops, but learning is disabled. Contact your shop for full access.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 200, 150)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            banner.Child = stack;
            return banner;
        }

        private void BuildQualitySection()
        {
            _qualitySection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 30, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 12, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 100, 120)),
                BorderThickness = new Thickness(1)
            };

            var qualityStack = new StackPanel { Spacing = 8 };

            var qualityHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            qualityHeader.Children.Add(new FontIcon
            {
                Glyph = "\uE9D9",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });
            qualityHeader.Children.Add(new TextBlock
            {
                Text = "QUALITY ASSESSMENT",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });
            qualityStack.Children.Add(qualityHeader);

            qualityStack.Children.Add(new TextBlock
            {
                Text = "Measures data cleanliness \u2014 how well the estimate parsed and whether the numbers " +
                       "make sense. Checks for missing fields, unusual values, formatting issues, and " +
                       "outliers. A high score means the data is clean and reliable for learning; a low " +
                       "score means some lines may have parsing errors or suspicious values. This does " +
                       "NOT judge whether the estimate is complete \u2014 that\u2019s what the Completeness Score below does.",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 140, 155)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var scoreRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 4, 0, 0) };

            _qualityScoreText = new TextBlock
            {
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            scoreRow.Children.Add(_qualityScoreText);

            _qualityGradeText = new TextBlock
            {
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 2, 8, 2)
            };
            scoreRow.Children.Add(_qualityGradeText);

            _qualityWeightText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            scoreRow.Children.Add(_qualityWeightText);
            qualityStack.Children.Add(scoreRow);

            _qualityFlagsPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            qualityStack.Children.Add(_qualityFlagsPanel);

            _includeOutliersCheckbox = new CheckBox
            {
                Content = "Include outliers in training anyway",
                IsChecked = false,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };
            qualityStack.Children.Add(_includeOutliersCheckbox);

            _qualitySection.Child = qualityStack;
        }

        #endregion

        private void UpdateStats()
        {
            var estimatesImported = EstimateHistoryDatabase.Instance.EstimateCount;
            var avgValue = _learningService.AverageEstimateValue;
            var patterns = _learningService.PatternCount;
            var manualPatterns = _learningService.ManualLinePatternCount;

            if (estimatesImported == 0)
            {
                _statsText!.Text = "No estimates learned yet";
                _statsText.Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));
            }
            else
            {
                _statsText!.Text = $"{estimatesImported} estimates | Avg ${avgValue:N0} | {patterns} parts | {manualPatterns} operations";
                _statsText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150));
            }
        }

        #region Upload, Drag/Drop, Parse Handlers

        private async void HandleUpload(ParseContext ctx)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add(".pdf");
                picker.FileTypeFilter.Add(".txt");
                picker.FileTypeFilter.Add(".csv");
                picker.FileTypeFilter.Add("*");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var files = await picker.PickMultipleFilesAsync();
                if (files != null && files.Count > 0)
                {
                    await ProcessFilesAsync(files.ToList(), ctx);
                }
            }
            catch (Exception ex)
            {
                ShowStatusForContext($"Error: {ex.Message}", ctx, isError: true);
            }
        }

        private void HandleDragOver(object sender, DragEventArgs e, Border? dropZone, TextBlock? dropText)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Drop to import";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
                e.DragUIOverride.IsContentVisible = true;

                if (dropZone != null)
                {
                    dropZone.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255));
                    dropZone.BorderThickness = new Thickness(3);
                    dropZone.Background = new SolidColorBrush(Color.FromArgb(255, 35, 45, 55));
                }

                if (dropText != null)
                {
                    dropText.Text = "Drop files to import...";
                    dropText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255));
                }
            }
            else
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            }
        }

        private async Task HandleDrop(DragEventArgs e, ParseContext ctx)
        {
            var dropZone = ctx == ParseContext.Scrubber ? _scrubUploadDropZone : _learnUploadDropZone;
            var dropText = ctx == ParseContext.Scrubber ? _scrubDropZoneText : _learnDropZoneText;
            ResetDropZoneAppearance(dropZone, dropText);

            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                try
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    var files = new List<StorageFile>();

                    foreach (var item in items)
                    {
                        if (item is StorageFile file)
                        {
                            var ext = file.FileType.ToLowerInvariant();
                            if (ext == ".pdf" || ext == ".txt" || ext == ".csv")
                            {
                                files.Add(file);
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[DragDrop] Skipped unsupported file: {file.Name}");
                            }
                        }
                        else if (item is StorageFolder folder)
                        {
                            var folderFiles = await GetFilesFromFolderAsync(folder);
                            files.AddRange(folderFiles);
                        }
                    }

                    if (files.Count > 0)
                    {
                        ShowStatusForContext($"Processing {files.Count} dropped file(s)...", ctx);
                        await ProcessFilesAsync(files, ctx);
                    }
                    else
                    {
                        ShowStatusForContext("No supported files found. Drop PDF, TXT, or CSV files.", ctx, isError: true);
                    }
                }
                catch (Exception ex)
                {
                    ShowStatusForContext($"Error processing dropped files: {ex.Message}", ctx, isError: true);
                }
            }
        }

        private async Task<List<StorageFile>> GetFilesFromFolderAsync(StorageFolder folder)
        {
            var result = new List<StorageFile>();

            try
            {
                var files = await folder.GetFilesAsync();
                foreach (var file in files)
                {
                    var ext = file.FileType.ToLowerInvariant();
                    if (ext == ".pdf" || ext == ".txt" || ext == ".csv")
                    {
                        result.Add(file);
                    }
                }

                var subfolders = await folder.GetFoldersAsync();
                foreach (var subfolder in subfolders)
                {
                    var subFiles = await subfolder.GetFilesAsync();
                    foreach (var file in subFiles)
                    {
                        var ext = file.FileType.ToLowerInvariant();
                        if (ext == ".pdf" || ext == ".txt" || ext == ".csv")
                        {
                            result.Add(file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DragDrop] Error reading folder {folder.Name}: {ex.Message}");
            }

            return result;
        }

        private void ResetDropZoneAppearance(Border? dropZone, TextBlock? dropText)
        {
            if (dropZone != null)
            {
                dropZone.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 70, 80));
                dropZone.BorderThickness = new Thickness(2);
                dropZone.Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 40));
            }

            if (dropText != null)
            {
                dropText.Text = "Drag & drop files here, or click to browse\nSupports PDF, TXT, CSV - Multiple files for batch processing";
                dropText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100));
            }
        }

        #endregion

        private async Task ProcessFilesAsync(List<StorageFile> files, ParseContext ctx)
        {
            var targetList = ctx == ParseContext.Scrubber ? _scrubParsedItemsList! : _learnParsedItemsList!;
            ShowProgress(true, ctx);
            _parsedLines.Clear();
            targetList.Items.Clear();

            int totalLines = 0;
            int totalParts = 0;
            int totalManualLines = 0;
            string? detectedSource = null;
            string? vehicleInfo = null;

            foreach (var file in files)
            {
                try
                {
                    ShowStatusForContext($"Processing: {file.Name}...", ctx);

                    if (file.FileType.ToLower() == ".pdf")
                    {
                        var estimate = await ReadPdfSmartAsync(file);
                        if (estimate != null && estimate.LineItems.Count > 0)
                        {
                            detectedSource = estimate.Source;
                            vehicleInfo = estimate.VehicleInfo;

                            foreach (var item in estimate.LineItems)
                            {
                                var parsed = ConvertToParsedLine(item);
                                _parsedLines.Add(parsed);
                            }

                            totalLines += estimate.LineItems.Count;
                            totalParts += estimate.LineItems.Count(i => !i.IsAdditionalOperation && !string.IsNullOrEmpty(i.PartName));
                            totalManualLines += estimate.LineItems.Count(i => i.IsAdditionalOperation);

                            System.Diagnostics.Debug.WriteLine($"[Import] {file.Name}: {estimate.Source} estimate, {estimate.LineItems.Count} items, Vehicle: {estimate.VehicleInfo}");

                            var est = estimate;
                            _ = Task.Run(() => EstimatePersistenceHelper.PersistAndMine(est));
                        }
                        else
                        {
                            var text = await ReadPdfAsTextAsync(file);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                var parsed = _learningService.ParseWithManualLineDetection(text);
                                _parsedLines.AddRange(parsed);
                                totalLines += parsed.Count;
                                totalParts += parsed.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
                                totalManualLines += parsed.Count(p => p.IsManualLine);

                                var fallbackEstimate = EstimatePersistenceHelper.ConvertFromParsedLines(parsed, text, file.Name);
                                _ = Task.Run(() => EstimatePersistenceHelper.PersistAndMine(fallbackEstimate));
                            }
                        }
                    }
                    else
                    {
                        var text = await FileIO.ReadTextAsync(file);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var parsed = _learningService.ParseWithManualLineDetection(text);
                            _parsedLines.AddRange(parsed);
                            totalLines += parsed.Count;
                            totalParts += parsed.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
                            totalManualLines += parsed.Count(p => p.IsManualLine);

                            var csvEstimate = EstimatePersistenceHelper.ConvertFromParsedLines(parsed, text, file.Name);
                            _ = Task.Run(() => EstimatePersistenceHelper.PersistAndMine(csvEstimate));
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing {file.Name}: {ex.Message}");
                    ShowStatusForContext($"Error processing {file.Name}: {ex.Message}", ctx, isError: true);
                }
            }

            DisplayParsedLines(targetList);

            ShowProgress(false, ctx);
            FrameworkElement resultsSection = ctx == ParseContext.Scrubber ? (FrameworkElement)_scrubSideBySideGrid! : _learnResultsSection!;
            var resultsTitle = ctx == ParseContext.Scrubber ? _scrubResultsTitle! : _learnResultsTitle!;
            resultsSection.Visibility = Visibility.Visible;

            var sourceInfo = !string.IsNullOrEmpty(detectedSource) ? $" ({detectedSource})" : "";
            var vehicleInfoText = !string.IsNullOrEmpty(vehicleInfo) ? $" - {vehicleInfo}" : "";
            resultsTitle.Text = $"Parsed: {totalParts} parts, {totalManualLines} additional ops{sourceInfo}{vehicleInfoText}";

            ShowStatusForContext($"Processed {files.Count} file(s): {totalParts} parts, {totalManualLines} additional operations", ctx);

            // Auto-scrub only in Scrubber tab
            if (ctx == ParseContext.Scrubber)
                await RunSmartAnalysisAsync();
        }

        /// <summary>
        /// Read and parse PDF using SMART structured parser
        /// </summary>
        private async Task<ParsedEstimate?> ReadPdfSmartAsync(StorageFile file)
        {
            try
            {
                // Read PDF bytes
                var buffer = await FileIO.ReadBufferAsync(file);
                var bytes = new byte[buffer.Length];
                using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer))
                {
                    reader.ReadBytes(bytes);
                }

                // Use SMART PDF parser with structured extraction
                var estimate = await Task.Run(() => EstimatePdfParser.Instance.ParsePdfFromBytes(bytes));
                estimate.SourceFile = file.Name;

                System.Diagnostics.Debug.WriteLine($"[PDF Smart] Parsed {estimate.LineItems.Count} items from {file.Name} ({estimate.Source})");
                return estimate;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PDF Smart] Error parsing {file.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Legacy: Read PDF as plain text (fallback)
        /// </summary>
        private async Task<string> ReadPdfAsTextAsync(StorageFile file)
        {
            try
            {
                var buffer = await FileIO.ReadBufferAsync(file);
                var bytes = new byte[buffer.Length];
                using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer))
                {
                    reader.ReadBytes(bytes);
                }

                var text = await Task.Run(() => PdfTextExtractorService.ExtractTextFromBytes(bytes));
                System.Diagnostics.Debug.WriteLine($"[PDF] Extracted {text.Length} characters from {file.Name}");
                return text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PDF] Error reading {file.Name}: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Convert PdfEstimateLineItem to ParsedEstimateLine for learning service
        /// </summary>
        // Store current source file for tracking
        private string? _currentSourceFile;

        private ParsedEstimateLine ConvertToParsedLine(PdfEstimateLineItem item)
        {
            return new ParsedEstimateLine
            {
                RawLine = item.RawLine,
                Description = item.Description,
                OriginalDescription = item.OriginalDescription,
                PartName = item.PartName,
                OperationType = item.OperationType,
                Category = item.Section,
                LaborHours = item.LaborHours,
                RepairHours = item.LaborHours,
                RefinishHours = item.RefinishHours,
                Price = item.Price,
                Quantity = item.Quantity,
                LaborType = item.LaborType,
                IsManualLine = item.IsAdditionalOperation,
                ParentPartName = item.ParentPartName,
                SourceFile = _currentSourceFile
            };
        }

        private async void HandleParsePaste(ParseContext ctx)
        {
            var pasteArea = ctx == ParseContext.Scrubber ? _scrubPasteArea : _learnPasteArea;
            var text = pasteArea?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowStatusForContext("Please paste estimate text first", ctx, isError: true);
                return;
            }

            _parsedLines.Clear();
            var targetList = ctx == ParseContext.Scrubber ? _scrubParsedItemsList! : _learnParsedItemsList!;
            FrameworkElement resultsSection = ctx == ParseContext.Scrubber ? (FrameworkElement)_scrubSideBySideGrid! : _learnResultsSection!;
            var resultsTitle = ctx == ParseContext.Scrubber ? _scrubResultsTitle! : _learnResultsTitle!;

            var estimate = EstimatePdfParser.Instance.ParseText(text);
            if (estimate.LineItems.Count > 0)
            {
                foreach (var item in estimate.LineItems)
                {
                    _parsedLines.Add(ConvertToParsedLine(item));
                }

                var sourceInfo = !string.IsNullOrEmpty(estimate.Source) && estimate.Source != "Unknown"
                    ? $" ({estimate.Source})"
                    : "";
                var vehicleInfo = !string.IsNullOrEmpty(estimate.VehicleInfo)
                    ? $" - {estimate.VehicleInfo}"
                    : "";

                DisplayParsedLines(targetList);

                var parts = _parsedLines.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
                var additionalOps = _parsedLines.Count(p => p.IsManualLine);

                resultsSection.Visibility = Visibility.Visible;
                resultsTitle.Text = $"Parsed: {parts} parts, {additionalOps} additional ops{sourceInfo}{vehicleInfo}";

                ShowStatusForContext($"SMART Parse: {parts} parts, {additionalOps} additional operations detected", ctx, isSuccess: true);

                estimate.SourceFile = "TextPaste";
                _ = Task.Run(() => EstimatePersistenceHelper.PersistAndMine(estimate));

                if (ctx == ParseContext.Scrubber)
                    await RunSmartAnalysisAsync();
            }
            else
            {
                _parsedLines = _learningService.ParseWithManualLineDetection(text);

                DisplayParsedLines(targetList);

                var parts = _parsedLines.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
                var manualLines = _parsedLines.Count(p => p.IsManualLine);

                resultsSection.Visibility = Visibility.Visible;
                resultsTitle.Text = $"Parsed: {parts} parts, {manualLines} manual lines (#)";

                ShowStatusForContext($"Parsed {_parsedLines.Count} lines: {parts} parts, {manualLines} manual lines", ctx);

                var fallbackEstimate = EstimatePersistenceHelper.ConvertFromParsedLines(_parsedLines, text, "TextPaste");
                _ = Task.Run(() => EstimatePersistenceHelper.PersistAndMine(fallbackEstimate));

                if (ctx == ParseContext.Scrubber)
                    await RunSmartAnalysisAsync();
            }
        }

        private void DisplayParsedLines(ListView targetList)
        {
            targetList.Items.Clear();

            string? currentSection = null;

            foreach (var line in _parsedLines.Take(100)) // Limit display
            {
                // Section header detection
                var sectionText = !string.IsNullOrEmpty(line.OriginalDescription) ? line.OriginalDescription : line.PartName;
                if (!line.IsManualLine && string.IsNullOrEmpty(line.OperationType) &&
                    !string.IsNullOrEmpty(sectionText) && sectionText == sectionText.ToUpper())
                {
                    currentSection = sectionText;
                    // Add section header
                    var sectionHeader = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 40, 50, 60)),
                        Padding = new Thickness(8, 6, 8, 6),
                        Margin = new Thickness(0, 8, 0, 2),
                        CornerRadius = new CornerRadius(4)
                    };
                    sectionHeader.Child = new TextBlock
                    {
                        Text = currentSection,
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 220))
                    };
                    targetList.Items.Add(sectionHeader);
                    continue;
                }

                var item = CreateParsedLineItem(line);
                targetList.Items.Add(item);
            }

            if (_parsedLines.Count > 100)
            {
                targetList.Items.Add(new TextBlock
                {
                    Text = $"... and {_parsedLines.Count - 100} more lines",
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    Margin = new Thickness(8)
                });
            }
        }

        private bool IsSuspectLine(ParsedEstimateLine line)
        {
            if (line.IsManualLine) return false;
            // Missing both PartName and Description
            if (string.IsNullOrEmpty(line.PartName) && string.IsNullOrEmpty(line.Description)) return true;
            // Has part but all values zero
            if (!string.IsNullOrEmpty(line.PartName) && line.LaborHours == 0 && line.RefinishHours == 0 && line.Price == 0) return true;
            // Outlier price
            if (line.Price < 0 || line.Price > 5000) return true;
            // Outlier hours
            if (line.LaborHours > 20 || line.RefinishHours > 20) return true;
            return false;
        }

        private string GetSuspectReason(ParsedEstimateLine line)
        {
            var reasons = new List<string>();
            if (string.IsNullOrEmpty(line.PartName) && string.IsNullOrEmpty(line.Description))
                reasons.Add("Missing part name and description");
            if (!string.IsNullOrEmpty(line.PartName) && line.LaborHours == 0 && line.RefinishHours == 0 && line.Price == 0)
                reasons.Add("Part has no labor, refinish, or price");
            if (line.Price < 0) reasons.Add($"Negative price: ${line.Price:N2}");
            if (line.Price > 5000) reasons.Add($"High price: ${line.Price:N2}");
            if (line.LaborHours > 20) reasons.Add($"High labor hours: {line.LaborHours:F1}");
            if (line.RefinishHours > 20) reasons.Add($"High refinish hours: {line.RefinishHours:F1}");
            return string.Join("\n", reasons);
        }

        private Border CreateParsedLineItem(ParsedEstimateLine line)
        {
            bool isSuspect = IsSuspectLine(line);

            var border = new Border
            {
                Background = line.IsManualLine
                    ? new SolidColorBrush(Color.FromArgb(255, 35, 45, 40))  // Greenish tint for additional ops
                    : isSuspect
                        ? new SolidColorBrush(Color.FromArgb(255, 60, 50, 35))  // Amber tint for suspect lines
                        : new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(line.IsManualLine ? 20 : 0, 1, 0, 1),  // Indent additional ops more
                CornerRadius = new CornerRadius(3)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) }); // Badge/indicator
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Operation
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Labor
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Paint
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Price

            // Badge indicator
            if (line.IsManualLine)
            {
                var hashBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 80, 150, 100)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                hashBadge.Child = new TextBlock
                {
                    Text = "ADD",
                    FontSize = 8,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                ToolTipService.SetToolTip(hashBadge, $"Additional operation{(string.IsNullOrEmpty(line.ParentPartName) ? "" : $" for: {line.ParentPartName}")}");
                Grid.SetColumn(hashBadge, 0);
                grid.Children.Add(hashBadge);
            }
            else if (!string.IsNullOrEmpty(line.PartName))
            {
                // Main part indicator
                var partBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 60, 100, 160)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                partBadge.Child = new TextBlock
                {
                    Text = "PART",
                    FontSize = 8,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                Grid.SetColumn(partBadge, 0);
                grid.Children.Add(partBadge);
            }
            else if (isSuspect)
            {
                var warnBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 180, 140, 50)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                warnBadge.Child = new TextBlock
                {
                    Text = "\u26A0",
                    FontSize = 8,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                ToolTipService.SetToolTip(warnBadge, GetSuspectReason(line));
                Grid.SetColumn(warnBadge, 0);
                grid.Children.Add(warnBadge);
            }

            // Operation type
            if (!string.IsNullOrEmpty(line.OperationType))
            {
                var opText = new TextBlock
                {
                    Text = line.OperationType,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(opText, 1);
                grid.Children.Add(opText);
            }

            // Description — show original estimate wording to match the estimate format
            var desc = !string.IsNullOrEmpty(line.OriginalDescription) ? line.OriginalDescription :
                       !string.IsNullOrEmpty(line.Description) ? line.Description :
                       !string.IsNullOrEmpty(line.PartName) ? line.PartName : line.RawLine;
            if (desc.Length > 55) desc = desc.Substring(0, 55) + "...";

            var descText = new TextBlock
            {
                Text = desc,
                FontSize = 11,
                Foreground = new SolidColorBrush(line.IsManualLine
                    ? Color.FromArgb(255, 180, 220, 180)  // Light green for additional ops
                    : Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTipService.SetToolTip(descText, line.RawLine);  // Show full raw line on hover
            Grid.SetColumn(descText, 2);
            grid.Children.Add(descText);

            // Labor hours
            if (line.LaborHours > 0)
            {
                var laborText = new TextBlock
                {
                    Text = $"{line.LaborHours:F1}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                ToolTipService.SetToolTip(laborText, "Labor Hours");
                Grid.SetColumn(laborText, 3);
                grid.Children.Add(laborText);
            }

            // Refinish hours
            if (line.RefinishHours > 0)
            {
                var paintText = new TextBlock
                {
                    Text = $"{line.RefinishHours:F1}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 255)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                ToolTipService.SetToolTip(paintText, "Refinish Hours");
                Grid.SetColumn(paintText, 4);
                grid.Children.Add(paintText);
            }

            // Price
            if (line.Price > 0)
            {
                var priceText = new TextBlock
                {
                    Text = $"${line.Price:N0}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 100)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(priceText, 5);
                grid.Children.Add(priceText);
            }

            border.Child = grid;
            return border;
        }

        /// <summary>
        /// Copy scoring results to clipboard.
        /// Reads from the scoring panel's current result (which already includes
        /// must-haves, commonly missed, smart suggestions, and learned patterns).
        /// </summary>
        private void CopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            var result = _scoringPanel?.CurrentResult;

            // Fallback: if scoring result is missing (e.g., background scoring failed silently),
            // run scoring on-demand so the scrubber always works when there's parsed data.
            if (result == null && _parsedLines.Count > 0)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("[Scrubber] No cached scoring result — running on-demand scoring");
                    result = RunEstimateScoringAndReturn();
                    if (result != null)
                    {
                        // Tag sources and store for future use
                        foreach (var issue in result.Issues)
                            issue.Source ??= "Scoring";
                        _scoringPanel?.UpdateScore(result);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Scrubber] On-demand scoring failed: {ex.Message}");
                }
            }

            if (result == null || result.Issues.Count == 0)
            {
                if (_parsedLines.Count == 0)
                    ShowStatus("No data to scrub — upload or paste an estimate first", isError: true);
                else
                    ShowStatus("Scrubber found no missing items. This estimate looks complete!", isError: false);
                return;
            }

            try
            {
                var outputLines = new List<string>();

                // Section A — Must-have / Scoring issues
                var scoringIssues = result.Issues
                    .Where(i => i.Source == "Scoring" || i.Source == null)
                    .ToList();
                if (scoringIssues.Count > 0)
                {
                    outputLines.Add($"\u2550\u2550\u2550 MUST-HAVE / SCORING ISSUES ({scoringIssues.Count} items) \u2550\u2550\u2550");
                    foreach (var issue in scoringIssues)
                    {
                        var line = $"\u2717 {issue.Title}";
                        if (issue.SuggestedFix?.LaborHours > 0)
                            line += $" | Labor: {issue.SuggestedFix.LaborHours:F1}";
                        if (issue.SuggestedFix?.EstimatedCost > 0)
                            line += $" | ~${issue.SuggestedFix.EstimatedCost:N2}";
                        if (issue.Severity == IssueSeverity.Critical)
                            line += " | CRITICAL";
                        else if (!string.IsNullOrEmpty(issue.WhyNeeded))
                            line += $" | {issue.WhyNeeded}";
                        outputLines.Add(line);
                    }
                    outputLines.Add("");
                }

                // Section B — Smart / commonly missed
                var smartIssues = result.Issues
                    .Where(i => i.Source == "Smart")
                    .ToList();
                if (smartIssues.Count > 0)
                {
                    outputLines.Add($"\u2550\u2550\u2550 COMMONLY MISSED FOR THIS ESTIMATE ({smartIssues.Count} items) \u2550\u2550\u2550");
                    foreach (var issue in smartIssues)
                    {
                        var line = $"\u25ba {issue.TriggeredBy} \u2192 {issue.Title}";
                        if (issue.SuggestedFix?.LaborHours > 0)
                            line += $" | Labor: {issue.SuggestedFix.LaborHours:F1}";
                        if (issue.SuggestedFix?.EstimatedCost > 0)
                            line += $" | ~${issue.SuggestedFix.EstimatedCost:N2}";
                        if (!string.IsNullOrEmpty(issue.WhyNeeded))
                            line += $" | {issue.WhyNeeded}";
                        outputLines.Add(line);
                    }
                    outputLines.Add("");
                }

                // Section C — Learned pattern suggestions
                var learnedIssues = result.Issues
                    .Where(i => i.Source == "Learned")
                    .ToList();
                if (learnedIssues.Count > 0)
                {
                    outputLines.Add($"\u2550\u2550\u2550 LEARNED PATTERN SUGGESTIONS ({learnedIssues.Count} items) \u2550\u2550\u2550");
                    foreach (var issue in learnedIssues)
                    {
                        var line = $"{issue.Description}";
                        if (issue.SuggestedFix?.LaborHours > 0)
                            line += $" | Labor: {issue.SuggestedFix.LaborHours:F1}";
                        if (issue.SuggestedFix?.EstimatedCost > 0)
                            line += $" | ~${issue.SuggestedFix.EstimatedCost:F0}";
                        if (!string.IsNullOrEmpty(issue.WhyNeeded))
                            line += $" | {issue.WhyNeeded}";
                        outputLines.Add(line);
                    }
                    outputLines.Add("");
                }

                // Section D — Not-included operations from P-Pages/CEG/DEG
                var pPageIssues = result.Issues
                    .Where(i => i.Source == "PPage")
                    .ToList();
                if (pPageIssues.Count > 0)
                {
                    outputLines.Add($"\u2550\u2550\u2550 NOT-INCLUDED OPERATIONS (P-Pages/CEG/DEG) ({pPageIssues.Count} items) \u2550\u2550\u2550");
                    foreach (var issue in pPageIssues)
                    {
                        var line = $"- {issue.Title}: {issue.Description}";
                        if (!string.IsNullOrEmpty(issue.WhyNeeded))
                            line += $" [Source: {issue.WhyNeeded}]";
                        outputLines.Add(line);
                    }
                }

                var totalSuggestions = result.Issues.Count;
                var clipboardText = string.Join("\n", outputLines);
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(clipboardText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                var parts = new List<string>();
                if (scoringIssues.Count > 0) parts.Add($"{scoringIssues.Count} scoring");
                if (smartIssues.Count > 0) parts.Add($"{smartIssues.Count} commonly missed");
                if (learnedIssues.Count > 0) parts.Add($"{learnedIssues.Count} learned");
                if (pPageIssues.Count > 0) parts.Add($"{pPageIssues.Count} P-Page");
                ShowStatus($"Scrubber: {totalSuggestions} items ({string.Join(", ", parts)}) — copied to clipboard!", isSuccess: true);
            }
            catch (Exception ex)
            {
                ShowStatus($"Error copying scrubber results: {ex.Message}", isError: true);
            }
        }

        private void ClearForTab(ParseContext ctx)
        {
            _parsedLines.Clear();
            _currentAnalysis = null;

            if (ctx == ParseContext.Scrubber)
            {
                _scrubParsedItemsList!.Items.Clear();
                _currentQualityRecord = null;
                _scrubSideBySideGrid!.Visibility = Visibility.Collapsed;
                _qualitySection!.Visibility = Visibility.Collapsed;
                _scoringPanel!.Reset();
                _scoringPanel.Visibility = Visibility.Collapsed;
                if (_scrubPasteArea != null) _scrubPasteArea.Text = "";
                ShowProgress(false, ParseContext.Scrubber);
                ShowStatusForContext("Cleared. Drop or paste a new estimate to start over.", ParseContext.Scrubber);
            }
            else
            {
                _learnParsedItemsList!.Items.Clear();
                _learnResultsSection!.Visibility = Visibility.Collapsed;
                if (_learningSummarySection != null)
                    _learningSummarySection.Visibility = Visibility.Collapsed;
                if (_learnPasteArea != null) _learnPasteArea.Text = "";
                ShowProgress(false, ParseContext.Learning);
                ShowStatusForContext("Cleared. Drop or paste a new estimate to start over.", ParseContext.Learning);
            }
        }

        private void ToggleScrubLayout()
        {
            if (_scrubSideBySideGrid == null || _scrubLeftPanel == null || _scrubRightScroll == null) return;

            _isSideBySideLayout = !_isSideBySideLayout;

            _scrubSideBySideGrid.ColumnDefinitions.Clear();
            _scrubSideBySideGrid.RowDefinitions.Clear();

            if (_isSideBySideLayout)
            {
                // Side-by-side: two columns
                _scrubSideBySideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                _scrubSideBySideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _scrubSideBySideGrid.ColumnSpacing = 12;
                _scrubSideBySideGrid.RowSpacing = 0;

                Grid.SetRow(_scrubLeftPanel, 0);
                Grid.SetColumn(_scrubLeftPanel, 0);
                Grid.SetColumnSpan(_scrubLeftPanel, 1);

                Grid.SetRow(_scrubRightScroll, 0);
                Grid.SetColumn(_scrubRightScroll, 1);
                Grid.SetColumnSpan(_scrubRightScroll, 1);

                _scrubRightScroll.MaxHeight = 600;
            }
            else
            {
                // Stacked: single column, two rows
                _scrubSideBySideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _scrubSideBySideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _scrubSideBySideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _scrubSideBySideGrid.ColumnSpacing = 0;
                _scrubSideBySideGrid.RowSpacing = 16;

                Grid.SetRow(_scrubLeftPanel, 0);
                Grid.SetColumn(_scrubLeftPanel, 0);
                Grid.SetColumnSpan(_scrubLeftPanel, 1);

                Grid.SetRow(_scrubRightScroll, 1);
                Grid.SetColumn(_scrubRightScroll, 0);
                Grid.SetColumnSpan(_scrubRightScroll, 1);

                _scrubRightScroll.MaxHeight = double.PositiveInfinity;
            }

            // Update button label
            if (_layoutToggleButton?.Content is StackPanel btnStack)
            {
                foreach (var child in btnStack.Children)
                {
                    if (child is FontIcon icon)
                        icon.Glyph = _isSideBySideLayout ? "\uE8A4" : "\uE89F";
                    else if (child is TextBlock lbl)
                        lbl.Text = _isSideBySideLayout ? "Stacked" : "Side by Side";
                }
            }
        }

        private async void MustHavesButton_Click(object sender, RoutedEventArgs e)
        {
            var saved = await MustHavesDialog.ShowAsync(this.XamlRoot);
            if (saved)
            {
                var enabledCount = GhostConfigService.Instance.GetMustHaves().Count(m => m.Enabled);
                if (_parsedLines.Count > 0)
                {
                    ShowStatus($"Must-haves updated: {enabledCount} items enabled. Re-analyzing...", isSuccess: true);
                    await RunSmartAnalysisAsync();
                    ShowStatus($"Must-haves updated: {enabledCount} items enabled. Results updated.", isSuccess: true);
                }
                else
                {
                    ShowStatus($"Must-haves updated: {enabledCount} items enabled. Parse an estimate to see results.", isSuccess: true);
                }
            }
        }

        private async void LearnButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_learningService.CanTrain)
            {
                ShowStatusForContext("Learning is locked for Client licenses. Contact your shop admin for Shop/Admin access.", ParseContext.Learning, isError: true);
                return;
            }

            if (_parsedLines.Count == 0)
            {
                ShowStatusForContext("No data to learn from", ParseContext.Learning, isError: true);
                return;
            }

            // Assess quality if not already done
            if (_currentQualityRecord == null)
            {
                _currentQualityRecord = _qualityService.AssessQuality(_parsedLines);
                UpdateQualityPanel(_currentQualityRecord);
            }

            // Check if quality is too low (skip during initial import phase)
            if (!_qualityService.IsBootstrapMode() &&
                _currentQualityRecord.Grade == QualityGrade.Rejected &&
                _includeOutliersCheckbox?.IsChecked != true)
            {
                ShowStatusForContext($"Quality too low ({_currentQualityRecord.QualityScore}/100). Switch to Scrubber tab and check 'Include outliers' to override.", ParseContext.Learning, isError: true);
                return;
            }

            ShowProgress(true, ParseContext.Learning);
            _learnButton!.IsEnabled = false;

            try
            {
                var estimateTotal = _parsedLines.Sum(p => p.Price);
                var learningWeight = _currentQualityRecord.LearningWeight;

                await Task.Run(() =>
                {
                    _learningService.LearnManualLinePatterns(_parsedLines, fromImport: true);

                    var trainingData = new EstimateTrainingData
                    {
                        Source = "Import",
                        LineMappings = new List<LineMapping>()
                    };

                    foreach (var line in _parsedLines.Where(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName)))
                    {
                        var mapping = new LineMapping
                        {
                            RawLine = line.RawLine,
                            PartName = line.PartName,
                            OperationType = line.OperationType,
                            RepairHours = line.LaborHours,
                            RefinishHours = line.RefinishHours,
                            Price = line.Price,
                            GeneratedOperations = new List<GeneratedOperation>
                            {
                                new GeneratedOperation
                                {
                                    OperationType = line.OperationType,
                                    Description = line.PartName,
                                    LaborHours = line.LaborHours,
                                    RefinishHours = line.RefinishHours
                                }
                            }
                        };
                        trainingData.LineMappings.Add(mapping);
                    }

                    if (trainingData.LineMappings.Count > 0)
                    {
                        _learningService.LearnFromEstimate(trainingData);
                    }

                    _learningService.RecordEstimateImport(estimateTotal);
                });

                _currentQualityRecord.WasUsedForTraining = true;

                var parts = _parsedLines.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
                var manual = _parsedLines.Count(p => p.IsManualLine);

                var fingerprint = ComputeEstimateFingerprint(_parsedLines, estimateTotal);
                var occurrence = new ExportStatisticsService().RecordLearn(parts, manual, estimateTotal, fingerprint);

                _healthService.OnTrainingCompleted(parts, 1);

                BuildLearningSummary(_parsedLines, estimateTotal, _currentQualityRecord);

                UpdateStats();

                var qualityNote = $" (Quality: {_currentQualityRecord.QualityScore}/100)";

                var destination = _learningService.CanTrainStandard ? "standard" : "personal";
                var repeatNote = occurrence > 1 ? $" (seen {occurrence}x)" : "";
                ShowStatusForContext($"Learned to {destination} (${estimateTotal:N0}): {parts} parts, {manual} operations{qualityNote}{repeatNote}", ParseContext.Learning, isSuccess: true);

                OnTrainingCompleted?.Invoke(this, new TrainingCompletedEventArgs
                {
                    PartsCount = parts,
                    OperationsCount = manual,
                    EstimateTotal = estimateTotal,
                    QualityScore = _currentQualityRecord?.QualityScore ?? 0
                });

                PostTrainingSummaryToChat(parts, manual, estimateTotal);

                // Clear parsed data but keep UI visible so Clear button stays accessible
                _parsedLines.Clear();
                _learnParsedItemsList!.Items.Clear();
                if (_learnPasteArea != null)
                    _learnPasteArea.Text = "";
                _currentQualityRecord = null;
            }
            catch (Exception ex)
            {
                ShowStatusForContext($"Error learning: {ex.Message}", ParseContext.Learning, isError: true);
            }
            finally
            {
                ShowProgress(false, ParseContext.Learning);
                _learnButton!.IsEnabled = true;
            }
        }

        /// <summary>
        /// Update the quality assessment panel with results.
        /// </summary>
        private void UpdateQualityPanel(EstimateQualityRecord quality)
        {
            if (_qualitySection == null) return;

            _qualitySection.Visibility = Visibility.Visible;

            // Score
            _qualityScoreText!.Text = $"{quality.QualityScore}/100";

            // Grade with color
            _qualityGradeText!.Text = quality.Grade.ToString().ToUpper();
            var gradeColor = quality.Grade switch
            {
                QualityGrade.Excellent => Color.FromArgb(255, 76, 175, 80),   // Green
                QualityGrade.Good => Color.FromArgb(255, 139, 195, 74),       // Light green
                QualityGrade.Fair => Color.FromArgb(255, 255, 193, 7),        // Yellow
                QualityGrade.Poor => Color.FromArgb(255, 255, 152, 0),        // Orange
                _ => Color.FromArgb(255, 244, 67, 54)                          // Red
            };
            _qualityGradeText.Foreground = new SolidColorBrush(gradeColor);

            // Learning weight
            _qualityWeightText!.Text = $"Learning Weight: {quality.LearningWeight:F2}x";

            // Flags
            _qualityFlagsPanel!.Children.Clear();

            foreach (var flag in quality.Flags.Take(5))
            {
                var flagIcon = flag.Severity switch
                {
                    QualitySeverity.Error => "\uE783",   // Warning icon
                    QualitySeverity.Warning => "\uE7BA", // Info icon
                    _ => "\uE946"                         // Light bulb
                };
                var flagColor = flag.Severity switch
                {
                    QualitySeverity.Error => Color.FromArgb(255, 244, 67, 54),
                    QualitySeverity.Warning => Color.FromArgb(255, 255, 193, 7),
                    _ => Color.FromArgb(255, 100, 180, 255)
                };

                var flagPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                flagPanel.Children.Add(new FontIcon
                {
                    Glyph = flagIcon,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(flagColor)
                });
                flagPanel.Children.Add(new TextBlock
                {
                    Text = flag.Description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    TextWrapping = TextWrapping.Wrap
                });
                _qualityFlagsPanel.Children.Add(flagPanel);
            }

            // Outliers
            foreach (var outlier in quality.Outliers.Take(3))
            {
                var desc = _qualityService.GetOutlierDescription(outlier);
                var outlierPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                outlierPanel.Children.Add(new FontIcon
                {
                    Glyph = "\uE814", // Graph icon
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0))
                });
                outlierPanel.Children.Add(new TextBlock
                {
                    Text = desc,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    TextWrapping = TextWrapping.Wrap
                });
                _qualityFlagsPanel.Children.Add(outlierPanel);
            }

            // Show outlier checkbox if there are outliers
            _includeOutliersCheckbox!.Visibility = quality.Outliers.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }


        private async void PublishButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_learningService.CanTrain)
            {
                ShowStatusForContext("Publishing is only available for Shop/Admin users.", ParseContext.Learning, isError: true);
                return;
            }

            var stats = _learningService.GetStatistics();

            // Show confirmation dialog with stats
            var dialog = new ContentDialog
            {
                Title = "Publish Learning to App",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "This will bake your current learning into the app's base knowledge.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = $"Current Learning:\n• {stats.TotalPatterns} patterns\n• {stats.TotalTrainingExamples} training examples\n• {stats.TotalTrainedEstimates} trained estimates",
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150))
                        },
                        new TextBlock
                        {
                            Text = "When you distribute the app, new users will automatically have this knowledge.",
                            TextWrapping = TextWrapping.Wrap,
                            FontStyle = Windows.UI.Text.FontStyle.Italic,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
                        }
                    }
                },
                PrimaryButtonText = "Publish",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var (success, message) = _learningService.PublishLearning();
                if (success)
                {
                    ShowStatusForContext("Learning published! Ready for distribution.", ParseContext.Learning, isSuccess: true);

                    var successDialog = new ContentDialog
                    {
                        Title = "Published Successfully",
                        Content = message,
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
                else
                {
                    ShowStatusForContext($"Error publishing: {message}", ParseContext.Learning, isError: true);
                }
            }
        }

        private async void ExportBaselineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_learningService.CurrentTier == LicenseTier.Client)
            {
                ShowStatusForContext("Publishing knowledge is only available for Shop/Admin users.", ParseContext.Learning, isError: true);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Publish Knowledge to Users",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "This will sanitize and export your learning data so all users receive your knowledge on their next update check.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = "Sanitization:\n• RO numbers, claim numbers, VINs — stripped\n• Source file paths, user IDs — stripped\n• Estimate IDs — regenerated\n• Patterns, hours, insurers, vehicle info — kept",
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 100))
                        },
                        new TextBlock
                        {
                            Text = "Version will auto-increment. After export, push the Data/ files to GitHub.",
                            TextWrapping = TextWrapping.Wrap,
                            FontStyle = Windows.UI.Text.FontStyle.Italic,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
                        }
                    }
                },
                PrimaryButtonText = "Publish",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ShowStatusForContext("Publishing knowledge...", ParseContext.Learning);

                var exportResult = BaselineExportTool.ExportAndSanitize();

                if (exportResult.Success)
                {
                    ShowStatusForContext($"Knowledge published as v{exportResult.NewVersion}!", ParseContext.Learning, isSuccess: true);

                    var successDialog = new ContentDialog
                    {
                        Title = $"Knowledge Published (v{exportResult.NewVersion})",
                        Content = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = exportResult.FormattedSummary,
                                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                                    FontSize = 12
                                },
                                new TextBlock
                                {
                                    Text = $"\nOutput: {exportResult.OutputPath}",
                                    FontSize = 11,
                                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                                    IsTextSelectionEnabled = true
                                },
                                new TextBlock
                                {
                                    Text = "\nNext: Push the Data/ folder to GitHub (mcstud/mcstud-data).\nUsers will receive this knowledge on their next update check.",
                                    TextWrapping = TextWrapping.Wrap,
                                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150))
                                }
                            }
                        },
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
                else
                {
                    ShowStatusForContext($"Publish failed: {exportResult.Message}", ParseContext.Learning, isError: true);
                }
            }
        }

        private void ShowStatus(string message, bool isError = false, bool isSuccess = false)
        {
            ShowStatusForContext(message, ParseContext.Scrubber, isError, isSuccess);
        }

        private void ShowStatusForContext(string message, ParseContext ctx, bool isError = false, bool isSuccess = false)
        {
            var statusText = ctx == ParseContext.Scrubber ? _scrubStatusText! : _learnStatusText!;
            statusText.Text = message;
            statusText.Foreground = new SolidColorBrush(
                isError ? Color.FromArgb(255, 255, 120, 120) :
                isSuccess ? Color.FromArgb(255, 100, 220, 150) :
                Color.FromArgb(255, 180, 180, 180)
            );
        }

        /// <summary>
        /// Generates a short hash from the parsed estimate lines so we can detect duplicate learns.
        /// Uses sorted part names + operation types + prices + total to create a deterministic fingerprint.
        /// </summary>
        private static string ComputeEstimateFingerprint(List<ParsedEstimateLine> lines, decimal total)
        {
            var sb = new StringBuilder();
            // Sort by part name then op type for deterministic ordering
            foreach (var line in lines.OrderBy(l => l.PartName, StringComparer.OrdinalIgnoreCase)
                                      .ThenBy(l => l.OperationType, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(line.PartName?.ToLowerInvariant().Trim() ?? "");
                sb.Append('|');
                sb.Append(line.OperationType?.ToLowerInvariant().Trim() ?? "");
                sb.Append('|');
                sb.Append(line.Price.ToString("F2"));
                sb.Append(';');
            }
            sb.Append(total.ToString("F2"));

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            // Use first 8 bytes for a compact fingerprint (16 hex chars)
            return Convert.ToHexString(bytes, 0, 8);
        }

        private void BuildLearningSummary(List<ParsedEstimateLine> parsedLines, decimal estimateTotal, EstimateQualityRecord? quality)
        {
            if (_learningSummaryContent == null || _learningSummarySection == null) return;
            _learningSummaryContent.Children.Clear();

            var stats = _learningService.GetStatistics();
            var accentGreen = Color.FromArgb(255, 100, 200, 130);
            var dimText = Color.FromArgb(255, 160, 160, 160);
            var brightText = Color.FromArgb(255, 230, 230, 230);

            // Header
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 18, Foreground = new SolidColorBrush(accentGreen) });
            headerStack.Children.Add(new TextBlock
            {
                Text = "LEARNING COMPLETE",
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (quality != null)
            {
                headerStack.Children.Add(new TextBlock
                {
                    Text = $"Quality: {quality.QualityScore}/100",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(dimText),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                });
            }

            // Dismiss button
            var dismissBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
                Width = 24, Height = 24, Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            dismissBtn.Click += (s, e) => { _learningSummarySection.Visibility = Visibility.Collapsed; };

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(headerStack, 0);
            Grid.SetColumn(dismissBtn, 1);
            headerRow.Children.Add(headerStack);
            headerRow.Children.Add(dismissBtn);
            _learningSummaryContent.Children.Add(headerRow);

            // Estimate value
            _learningSummaryContent.Children.Add(new TextBlock
            {
                Text = $"Estimate value: {estimateTotal:C0}",
                FontSize = 12,
                Foreground = new SolidColorBrush(accentGreen),
                Margin = new Thickness(0, 4, 0, 0)
            });

            // === PARTS LEARNED ===
            var partLines = parsedLines.Where(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName)).ToList();
            if (partLines.Count > 0)
            {
                _learningSummaryContent.Children.Add(CreateSummarySubheader($"Parts Learned ({partLines.Count})"));

                var partsGrid = new Grid { Margin = new Thickness(8, 0, 0, 0) };
                partsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                partsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                partsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                partsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                int row = 0;
                // Column headers
                partsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var colHeaders = new[] { "Part", "Operation", "Labor", "Price" };
                for (int c = 0; c < colHeaders.Length; c++)
                {
                    var hdr = new TextBlock
                    {
                        Text = colHeaders[c],
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(dimText),
                        Margin = new Thickness(0, 0, 12, 4)
                    };
                    Grid.SetColumn(hdr, c);
                    Grid.SetRow(hdr, 0);
                    partsGrid.Children.Add(hdr);
                }
                row++;

                foreach (var part in partLines.Take(20))
                {
                    partsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var nameText = new TextBlock { Text = part.PartName, FontSize = 11, Foreground = new SolidColorBrush(brightText), Margin = new Thickness(0, 0, 12, 2), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200 };
                    Grid.SetColumn(nameText, 0); Grid.SetRow(nameText, row);
                    partsGrid.Children.Add(nameText);

                    var opText = new TextBlock { Text = part.OperationType, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 180, 255)), Margin = new Thickness(0, 0, 12, 2) };
                    Grid.SetColumn(opText, 1); Grid.SetRow(opText, row);
                    partsGrid.Children.Add(opText);

                    var laborStr = part.LaborHours > 0 ? $"{part.LaborHours:N1}h" : "-";
                    if (part.RefinishHours > 0) laborStr += $" +{part.RefinishHours:N1}r";
                    var laborText = new TextBlock { Text = laborStr, FontSize = 11, Foreground = new SolidColorBrush(dimText), Margin = new Thickness(0, 0, 12, 2) };
                    Grid.SetColumn(laborText, 2); Grid.SetRow(laborText, row);
                    partsGrid.Children.Add(laborText);

                    var priceText = new TextBlock { Text = part.Price > 0 ? part.Price.ToString("C0") : "-", FontSize = 11, Foreground = new SolidColorBrush(accentGreen), Margin = new Thickness(0, 0, 0, 2) };
                    Grid.SetColumn(priceText, 3); Grid.SetRow(priceText, row);
                    partsGrid.Children.Add(priceText);

                    row++;
                }

                if (partLines.Count > 20)
                {
                    partsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var moreText = new TextBlock { Text = $"+{partLines.Count - 20} more parts", FontSize = 10, Foreground = new SolidColorBrush(dimText), FontStyle = Windows.UI.Text.FontStyle.Italic, Margin = new Thickness(0, 4, 0, 0) };
                    Grid.SetRow(moreText, row);
                    partsGrid.Children.Add(moreText);
                }

                _learningSummaryContent.Children.Add(partsGrid);
            }

            // === MANUAL OPS LEARNED ===
            var manualLines = parsedLines.Where(p => p.IsManualLine).ToList();
            if (manualLines.Count > 0)
            {
                _learningSummaryContent.Children.Add(CreateSummarySubheader($"Additional Operations Learned ({manualLines.Count})"));

                // Group manual lines by their parent part
                ParsedEstimateLine? currentParent = null;
                var grouped = new List<(string parent, List<ParsedEstimateLine> ops)>();
                var currentOps = new List<ParsedEstimateLine>();

                foreach (var line in parsedLines)
                {
                    if (!line.IsManualLine && !string.IsNullOrEmpty(line.PartName))
                    {
                        if (currentParent != null && currentOps.Count > 0)
                            grouped.Add(($"{currentParent.PartName} ({currentParent.OperationType})", new List<ParsedEstimateLine>(currentOps)));
                        currentParent = line;
                        currentOps.Clear();
                    }
                    else if (line.IsManualLine && currentParent != null)
                    {
                        currentOps.Add(line);
                    }
                }
                if (currentParent != null && currentOps.Count > 0)
                    grouped.Add(($"{currentParent.PartName} ({currentParent.OperationType})", new List<ParsedEstimateLine>(currentOps)));

                var opsStack = new StackPanel { Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
                foreach (var (parent, ops) in grouped.Take(15))
                {
                    var parentText = new TextBlock
                    {
                        Text = parent,
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(brightText)
                    };
                    opsStack.Children.Add(parentText);

                    foreach (var op in ops)
                    {
                        var opRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 0, 0, 0) };
                        opRow.Children.Add(new TextBlock { Text = "#", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 80)), VerticalAlignment = VerticalAlignment.Center });
                        opRow.Children.Add(new TextBlock { Text = op.Description ?? op.RawLine, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 120)), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 });

                        if (op.LaborHours > 0)
                            opRow.Children.Add(new TextBlock { Text = $"{op.LaborHours:N1}h", FontSize = 10, Foreground = new SolidColorBrush(dimText) });
                        if (op.Price > 0)
                            opRow.Children.Add(new TextBlock { Text = op.Price.ToString("C0"), FontSize = 10, Foreground = new SolidColorBrush(accentGreen) });

                        opsStack.Children.Add(opRow);
                    }
                }

                if (grouped.Count > 15)
                    opsStack.Children.Add(new TextBlock { Text = $"+{grouped.Count - 15} more groups", FontSize = 10, Foreground = new SolidColorBrush(dimText), FontStyle = Windows.UI.Text.FontStyle.Italic });

                _learningSummaryContent.Children.Add(opsStack);
            }

            // === TOTALS BAR ===
            var totalsBar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 45, 30)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var totalsStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
            totalsStack.Children.Add(CreateTotalsStat("Estimates", stats.EstimatesImported.ToString()));
            totalsStack.Children.Add(CreateTotalsStat("Parts", stats.TotalPatterns.ToString()));
            totalsStack.Children.Add(CreateTotalsStat("Operations", stats.TotalManualLinePatterns.ToString()));
            totalsStack.Children.Add(CreateTotalsStat("Avg Value", stats.AverageEstimateValue.ToString("C0")));
            totalsStack.Children.Add(CreateTotalsStat("Saved To", "Personal File"));
            totalsBar.Child = totalsStack;
            _learningSummaryContent.Children.Add(totalsBar);

            _learningSummarySection.Visibility = Visibility.Visible;
        }

        private TextBlock CreateSummarySubheader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 130)),
                Margin = new Thickness(0, 8, 0, 4)
            };
        }

        private StackPanel CreateTotalsStat(string label, string value)
        {
            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130))
            });
            return stack;
        }

        private void PostTrainingSummaryToChat(int parts, int manual, decimal estimateTotal)
        {
            try
            {
                var learning = EstimateLearningService.Instance;
                var history = EstimateHistoryDatabase.Instance;
                var stats = learning.GetStatistics();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"\u2705 Learned from estimate (${estimateTotal:N0})");
                sb.AppendLine($"   {parts} parts, {manual} operations absorbed");
                sb.AppendLine($"   Now trained on {stats.TotalEstimatesTrained} estimates | {stats.TotalPatterns} patterns");

                var insurers = history.KnownInsurers;
                if (insurers?.Count > 0)
                {
                    sb.AppendLine($"   Insurer playbooks: {string.Join(", ", insurers.Take(5))}{(insurers.Count > 5 ? $" +{insurers.Count - 5} more" : "")}");
                }

                var followUps = new List<string>
                {
                    "What am I missing?",
                    "Show my accuracy stats",
                    "Review learned patterns"
                };

                ChatbotView.Instance?.PostAISummary(sb.ToString(), followUps);
            }
            catch { }
        }

        private void ShowProgress(bool show)
        {
            ShowProgress(show, ParseContext.Scrubber);
        }

        private void ShowProgress(bool show, ParseContext ctx)
        {
            var ring = ctx == ParseContext.Scrubber ? _scrubProgressRing! : _learnProgressRing!;
            ring.IsActive = show;
            ring.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            var uploadBtn = ctx == ParseContext.Scrubber ? _scrubUploadButton! : _learnUploadButton!;
            uploadBtn.IsEnabled = !show;
        }

        #region Smart Analysis

        /// <summary>
        /// Run unified analysis: quality + scoring + smart suggestions merged into one panel.
        /// Heavy computation runs on background thread; UI updates marshal back to dispatcher.
        /// </summary>
        private async Task RunSmartAnalysisAsync()
        {
            if (_parsedLines.Count == 0)
            {
                _qualitySection!.Visibility = Visibility.Collapsed;
                _scoringPanel!.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                // Snapshot the lines for background processing
                var linesSnapshot = _parsedLines.ToList();

                // Run heavy computation on background thread
                var (qualityRecord, scoringResult, analysis) = await Task.Run(() =>
                {
                    // Run quality assessment
                    var qr = _qualityService.AssessQuality(linesSnapshot);

                    // Run estimate completeness scoring
                    EstimateScoringResult? sr = null;
                    try
                    {
                        string? vehicleInfo = null;
                        var vehicleLine = linesSnapshot.FirstOrDefault(l =>
                            !string.IsNullOrEmpty(l.PartName) &&
                            (l.PartName.ToLowerInvariant().Contains("vehicle") ||
                             l.PartName.Contains(" VIN") ||
                             System.Text.RegularExpressions.Regex.IsMatch(l.PartName, @"\d{4}\s+\w+")));
                        if (vehicleLine != null)
                            vehicleInfo = vehicleLine.PartName;

                        sr = _scoringService.ScoreEstimate(linesSnapshot, vehicleInfo);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Scoring] Error: {ex.Message}");
                    }

                    // Run smart suggestions analysis
                    AnalysisResult? sa = null;
                    if (sr != null)
                    {
                        foreach (var issue in sr.Issues)
                            issue.Source ??= "Scoring";

                        sa = _analyzerService.AnalyzeEstimate(linesSnapshot);

                        if (sa.Suggestions.Count > 0)
                        {
                            var smartIssues = EstimateScoringService.ConvertFromSuggestions(sa.Suggestions);
                            MergeIssuesWithDedup(sr, smartIssues);
                            System.Diagnostics.Debug.WriteLine($"[SmartAnalysis] Merged {smartIssues.Count} smart suggestions into scoring panel");
                        }

                        // Merge learned patterns into scoring
                        var learnedIssues = new List<ScoringIssue>();
                        foreach (var line in linesSnapshot.Where(l => !string.IsNullOrEmpty(l.PartName)))
                        {
                            var ops = _learningService.GenerateOperations(line);
                            var manualPattern = _learningService.GetManualLinesForPart(line.PartName, line.OperationType);
                            if (ops.Count > 0 || manualPattern != null)
                            {
                                var converted = EstimateScoringService.ConvertFromLearnedOperations(ops, manualPattern, line.PartName);
                                learnedIssues.AddRange(converted);
                            }
                        }
                        if (learnedIssues.Count > 0)
                        {
                            MergeIssuesWithDedup(sr, learnedIssues);
                            System.Diagnostics.Debug.WriteLine($"[SmartAnalysis] Merged {learnedIssues.Count} learned patterns into scoring panel");
                        }

                        _scoringService.RecalculateScore(sr);
                    }

                    return (qr, sr, sa);
                });

                // Update UI on main thread
                _currentQualityRecord = qualityRecord;
                UpdateQualityPanel(qualityRecord);

                if (scoringResult != null)
                {
                    _scoringPanel!.UpdateScore(scoringResult);
                    _scoringPanel.Visibility = Visibility.Visible;
                    ShowStatusForContext($"Scrubber: {scoringResult.OverallScore}% score, {scoringResult.Issues.Count} items found", ParseContext.Scrubber, isSuccess: scoringResult.Issues.Count > 0);
                    System.Diagnostics.Debug.WriteLine($"[Scoring] Unified score: {scoringResult.OverallScore}%, Issues: {scoringResult.Issues.Count}");
                }
                else
                {
                    ShowStatusForContext("Scrubber: scoring returned no results", ParseContext.Scrubber);
                }

                _currentAnalysis = analysis;

                // Run reference matching to auto-populate PDF queue
                _ = RunReferenceMatchingAsync();
            }
            catch (Exception ex)
            {
                ShowStatusForContext($"Scrubber error: {ex.Message}", ParseContext.Scrubber, isError: true);
                System.Diagnostics.Debug.WriteLine($"[SmartAnalysis] Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Normalize a dedup key by stripping "Missing: " and "Need N more: " prefixes
        /// so that the same underlying item doesn't appear multiple times.
        /// </summary>
        private static string NormalizeDedupKey(string title)
        {
            var t = title.ToLowerInvariant().Trim();
            if (t.StartsWith("missing: "))
                t = t.Substring(9);
            else if (t.StartsWith("need "))
            {
                var colonIdx = t.IndexOf(':');
                if (colonIdx > 0 && colonIdx < t.Length - 1)
                    t = t.Substring(colonIdx + 1).TrimStart();
            }
            return t;
        }

        /// <summary>
        /// Merge smart-sourced issues into the scoring result, deduplicating by normalized title+category.
        /// Scoring issues win on collision (they have full-weight point deductions).
        /// </summary>
        private static void MergeIssuesWithDedup(EstimateScoringResult result, List<ScoringIssue> smartIssues)
        {
            var existingKeys = new HashSet<string>(
                result.Issues.Select(i => $"{NormalizeDedupKey(i.Title)}|{i.Category}"));

            foreach (var smart in smartIssues)
            {
                var key = $"{NormalizeDedupKey(smart.Title)}|{smart.Category}";
                if (!existingKeys.Contains(key))
                {
                    result.Issues.Add(smart);
                    existingKeys.Add(key);
                }
            }

            // Safety cap after merge
            const int MaxIssues = 500;
            if (result.Issues.Count > MaxIssues)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EstimateUpload] WARNING: {result.Issues.Count} issues after merge exceeded cap of {MaxIssues}, truncating.");
                result.Issues = result.Issues
                    .OrderByDescending(i => i.Severity)
                    .ThenByDescending(i => i.PointDeduction)
                    .Take(MaxIssues).ToList();
            }
        }

        /// <summary>
        /// Auto-match parsed lines against reference data and add to PDF queue
        /// </summary>
        private async Task RunReferenceMatchingAsync()
        {
            if (_parsedLines.Count == 0) return;

            try
            {
                var matchResult = await _referenceMatcher.MatchEstimateLinesAsync(_parsedLines);
                if (matchResult.Items.Count > 0)
                {
                    ReferenceView.Instance?.ShowStagedItems(matchResult.Items);

                    if (_refMatchStatusText != null)
                    {
                        _refMatchStatusText.Text = $"Found {matchResult.Items.Count} reference matches — review in Reference tab";
                        _refMatchStatusText.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EstimateUpload] Reference matching error: {ex.Message}");
            }
        }

        /// <summary>
        /// Run estimate completeness scoring and return the result (does not update panel — caller does that after merging).
        /// </summary>
        private EstimateScoringResult? RunEstimateScoringAndReturn()
        {
            if (_parsedLines.Count == 0 || _scoringPanel == null)
            {
                if (_scoringPanel != null)
                {
                    _scoringPanel.Visibility = Visibility.Collapsed;
                }
                return null;
            }

            try
            {
                // Get vehicle info if available from parsed data
                string? vehicleInfo = null;
                var vehicleLine = _parsedLines.FirstOrDefault(l =>
                    !string.IsNullOrEmpty(l.PartName) &&
                    (l.PartName.ToLowerInvariant().Contains("vehicle") ||
                     l.PartName.Contains(" VIN") ||
                     System.Text.RegularExpressions.Regex.IsMatch(l.PartName, @"\d{4}\s+\w+")));
                if (vehicleLine != null)
                {
                    vehicleInfo = vehicleLine.PartName;
                }

                // Run scoring
                return _scoringService.ScoreEstimate(_parsedLines, vehicleInfo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scoring] Error: {ex.Message}");
                _scoringPanel.Visibility = Visibility.Collapsed;
                return null;
            }
        }

        /// <summary>
        /// Handle adding items from the scoring panel (works for both scoring and smart-sourced items)
        /// </summary>
        private async void ScoringPanel_OnAddItems(object? sender, List<ScoringIssue> items)
        {
            if (items.Count == 0) return;

            try
            {
                int addedCount = 0;
                foreach (var item in items)
                {
                    if (item.SuggestedFix == null) continue;

                    // Create a new parsed line for this item
                    var newLine = new ParsedEstimateLine
                    {
                        IsManualLine = true,
                        RawLine = item.Source == "Smart" ? $"[SUGGESTED] {item.Title}" : $"[SCORING] {item.Title}",
                        Description = $"{item.SuggestedFix.Description} - {item.WhyNeeded}",
                        PartName = item.Title,
                        OperationType = item.SuggestedFix.OperationType,
                        LaborHours = item.SuggestedFix.LaborHours,
                        Price = item.SuggestedFix.EstimatedCost,
                        Category = GetCategoryCode(item.Category),
                        ParentPartName = item.SourceDetail
                    };

                    // Add to parsed lines if not already present
                    bool alreadyExists = _parsedLines.Any(l =>
                        l.Description?.ToLowerInvariant() == newLine.Description?.ToLowerInvariant() ||
                        l.PartName?.ToLowerInvariant() == newLine.PartName?.ToLowerInvariant());

                    if (!alreadyExists)
                    {
                        _parsedLines.Add(newLine);
                        addedCount++;

                        // Remove from smart suggestions so they don't reappear on re-analysis
                        if (item.Source == "Smart" && _currentAnalysis != null)
                        {
                            _currentAnalysis.Suggestions.RemoveAll(s =>
                                s.Item.Equals(item.Title, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }

                if (addedCount > 0)
                {
                    ShowStatus($"Adding {addedCount} items...", isSuccess: true);

                    // Refresh the display
                    DisplayParsedLines(_scrubParsedItemsList!);

                    // Re-run unified scoring + smart merge flow (async - won't freeze UI)
                    await RunSmartAnalysisAsync();

                    ShowStatus($"Added {addedCount} items to estimate", isSuccess: true);
                }
                else
                {
                    ShowStatus("All selected items already in estimate", isSuccess: false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scoring] Error adding items: {ex.Message}");
                ShowStatus($"Error adding items: {ex.Message}", isSuccess: false);
            }
        }

        /// <summary>
        /// Get category code from scoring category
        /// </summary>
        private string GetCategoryCode(IssueCategoryType category)
        {
            return category switch
            {
                IssueCategoryType.Materials => "MAT",
                IssueCategoryType.Blend => "RFN",
                IssueCategoryType.RandI => "R&I",
                IssueCategoryType.Diagnostic => "DIAG",
                IssueCategoryType.Calibration => "CAL",
                IssueCategoryType.Refinish => "RFN",
                IssueCategoryType.Electrical => "ELEC",
                IssueCategoryType.Mechanical => "MECH",
                IssueCategoryType.Labor => "BODY",
                _ => "ADD"
            };
        }


        #endregion
    }

    public class TrainingCompletedEventArgs : EventArgs
    {
        public int PartsCount { get; set; }
        public int OperationsCount { get; set; }
        public decimal EstimateTotal { get; set; }
        public int QualityScore { get; set; }
    }
}
