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

        // UI Elements
        private TextBlock? _statsText;
        private TextBlock? _statusText;
        private Button? _uploadButton;
        private Button? _learnButton;
        private Button? _buildLinesButton;
        private Button? _publishButton;
        private Button? _exportBaselineButton;
        private TextBox? _pasteArea;
        private ListView? _parsedItemsList;
        private ProgressRing? _progressRing;
        private Border? _resultsSection;
        private TextBlock? _resultsTitle;
        private Border? _clientModeBanner;
        private TextBlock? _clientModeText;

        // Quality Assessment UI
        private Border? _qualitySection;
        private TextBlock? _qualityScoreText;
        private TextBlock? _qualityGradeText;
        private TextBlock? _qualityWeightText;
        private StackPanel? _qualityFlagsPanel;
        private CheckBox? _includeOutliersCheckbox;
        private EstimateQualityRecord? _currentQualityRecord;

        // Estimate Completeness Scoring
        private EstimateScoringPanel? _scoringPanel;
        private readonly EstimateScoringService _scoringService;

        // Smart Suggestions UI
        private Border? _suggestionsSection;
        private TextBlock? _suggestionsTitle;
        private TextBlock? _suggestionsSummary;
        private StackPanel? _suggestionsContainer;
        private Button? _addSelectedButton;
        private Button? _addAllHighPriorityButton;
        private readonly SmartEstimateAnalyzerService _analyzerService;
        private AnalysisResult? _currentAnalysis;

        // Reference matching
        private readonly EstimateReferenceMatcherService _referenceMatcher;
        private TextBlock? _refMatchStatusText;

        // Events
        public event EventHandler<TrainingCompletedEventArgs>? OnTrainingCompleted;

        // Parsed data
        private List<ParsedEstimateLine> _parsedLines = new();

        // Drag and drop
        private Border? _uploadDropZone;
        private TextBlock? _dropZoneText;

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

            // Show/hide client mode banner
            if (_clientModeBanner != null)
            {
                _clientModeBanner.Visibility = canTrain ? Visibility.Collapsed : Visibility.Visible;
            }

            // Update learn button state
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
            }

            // Show/hide publish button (only for trainers)
            if (_publishButton != null)
            {
                _publishButton.Visibility = canTrain ? Visibility.Visible : Visibility.Collapsed;
            }

            // Show/hide publish knowledge button (Shop/Admin)
            if (_exportBaselineButton != null)
            {
                _exportBaselineButton.Visibility = _learningService.CurrentTier != LicenseTier.Client
                    ? Visibility.Visible : Visibility.Collapsed;
            }
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
                Text = "Import & Learn",
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

            // Stats and Publish section
            var statsSection = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                VerticalAlignment = VerticalAlignment.Center
            };

            _statsText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            statsSection.Children.Add(_statsText);

            // Publish button (Shop/Admin only) - Bakes learning into app for distribution
            _publishButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE898", FontSize = 12 }, // Share/Export icon
                        new TextBlock { Text = "Publish Learning", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 100, 80, 140)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4)
            };
            _publishButton.Click += PublishButton_Click;
            ToolTipService.SetToolTip(_publishButton, "Bake learned knowledge into app for distribution. Other users will get your learning when they receive the app.");
            statsSection.Children.Add(_publishButton);

            // Publish Knowledge button (Shop/Admin only) - Exports sanitized data for distribution to all users
            _exportBaselineButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE72D", FontSize = 12 }, // Upload/cloud icon
                        new TextBlock { Text = "Publish Knowledge to Users", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4),
                Visibility = _learningService.CurrentTier != LicenseTier.Client ? Visibility.Visible : Visibility.Collapsed
            };
            _exportBaselineButton.Click += ExportBaselineButton_Click;
            ToolTipService.SetToolTip(_exportBaselineButton, "Sanitize and export all learning data, bump version, then push to GitHub so all users receive your knowledge.");
            statsSection.Children.Add(_exportBaselineButton);

            Grid.SetColumn(statsSection, 1);
            headerGrid.Children.Add(statsSection);

            mainStack.Children.Add(headerGrid);

            // === CLIENT MODE BANNER (hidden for shop/admin) ===
            _clientModeBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 40, 20)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 180, 140, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed // Hidden by default, shown for clients
            };
            var clientBannerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            clientBannerStack.Children.Add(new FontIcon
            {
                Glyph = "\uE8D7", // Info icon
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100))
            });
            _clientModeText = new TextBlock
            {
                Text = "Client Mode: You can parse estimates and build operation lines, but learning is disabled. Contact your shop for full access.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 200, 150)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            clientBannerStack.Children.Add(_clientModeText);
            _clientModeBanner.Child = clientBannerStack;
            mainStack.Children.Add(_clientModeBanner);

            // === UPLOAD SECTION (with drag and drop) ===
            _uploadDropZone = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 40)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 70, 80)),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(24),
                MinHeight = 150,
                AllowDrop = true
            };

            // Wire up drag and drop events
            _uploadDropZone.DragOver += UploadDropZone_DragOver;
            _uploadDropZone.DragLeave += UploadDropZone_DragLeave;
            _uploadDropZone.Drop += UploadDropZone_Drop;

            var uploadStack = new StackPanel
            {
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var uploadIcon = new FontIcon
            {
                Glyph = "\uE898", // Upload icon
                FontSize = 40,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 150, 200))
            };
            uploadStack.Children.Add(uploadIcon);

            _uploadButton = new Button
            {
                Content = "Upload Estimate Files",
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(24, 12, 24, 12),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _uploadButton.Click += UploadButton_Click;
            uploadStack.Children.Add(_uploadButton);

            _dropZoneText = new TextBlock
            {
                Text = "Drag & drop files here, or click to browse\nSupports PDF, TXT, CSV - Multiple files for batch learning",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            uploadStack.Children.Add(_dropZoneText);

            _uploadDropZone.Child = uploadStack;
            mainStack.Children.Add(_uploadDropZone);

            // === OR PASTE ===
            var orText = new TextBlock
            {
                Text = "— or paste estimate text —",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            mainStack.Children.Add(orText);

            _pasteArea = new TextBox
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
            mainStack.Children.Add(_pasteArea);

            // Parse button for pasted text
            var parseButton = new Button
            {
                Content = "Parse Pasted Text",
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 8, 16, 8),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            parseButton.Click += ParsePastedText_Click;
            mainStack.Children.Add(parseButton);

            // === RESULTS SECTION (hidden until data parsed) ===
            _resultsSection = new Border
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

            _resultsTitle = new TextBlock
            {
                Text = "Parsed Lines",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_resultsTitle, 0);
            resultsHeader.Children.Add(_resultsTitle);

            _progressRing = new ProgressRing
            {
                Width = 20,
                Height = 20,
                IsActive = false,
                Visibility = Visibility.Collapsed
            };
            Grid.SetColumn(_progressRing, 1);
            resultsHeader.Children.Add(_progressRing);

            // Build Lines button (works for all tiers)
            _buildLinesButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8A5", FontSize = 14 }, // List icon
                        new TextBlock { Text = "Build Operation Lines", FontSize = 12 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 180)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(8, 0, 0, 0)
            };
            _buildLinesButton.Click += BuildLinesButton_Click;
            ToolTipService.SetToolTip(_buildLinesButton,
                "BUILD: Generate operation lines for THIS estimate right now.\n" +
                "Uses knowledge base to suggest operations you can copy/paste into CCC.\n" +
                "Does NOT save anything - just creates output for immediate use.");

            // Learn button (Shop/Admin only)
            _learnButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE7BE", FontSize = 14 }, // Brain icon
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

            // Add both buttons to a panel
            var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            buttonsPanel.Children.Add(_buildLinesButton);
            buttonsPanel.Children.Add(_learnButton);
            Grid.SetColumn(buttonsPanel, 2);
            resultsHeader.Children.Add(buttonsPanel);

            resultsStack.Children.Add(resultsHeader);

            // Parsed items list
            _parsedItemsList = new ListView
            {
                MaxHeight = 350,
                Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)),
                SelectionMode = ListViewSelectionMode.None,
                Padding = new Thickness(4)
            };
            resultsStack.Children.Add(_parsedItemsList);

            _resultsSection.Child = resultsStack;
            mainStack.Children.Add(_resultsSection);

            // === QUALITY ASSESSMENT SECTION (hidden until parse runs) ===
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

            // Quality header
            var qualityHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            qualityHeader.Children.Add(new FontIcon
            {
                Glyph = "\uE9D9", // Shield/check icon
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

            // Score and grade row
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

            // Flags panel
            _qualityFlagsPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            qualityStack.Children.Add(_qualityFlagsPanel);

            // Include outliers checkbox
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
            mainStack.Children.Add(_qualitySection);

            // === ESTIMATE COMPLETENESS SCORING PANEL ===
            _scoringPanel = new EstimateScoringPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 12, 0, 0)
            };
            _scoringPanel.OnAddItems += ScoringPanel_OnAddItems;
            mainStack.Children.Add(_scoringPanel);

            // === SMART SUGGESTIONS SECTION (hidden until analysis runs) ===
            _suggestionsSection = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 25, 35)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 12, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 100, 80, 140)),
                BorderThickness = new Thickness(1)
            };

            var suggestionsStack = new StackPanel { Spacing = 12 };

            // Suggestions header with icon
            var suggestionsHeader = new Grid();
            suggestionsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            suggestionsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            suggestionsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headerStack.Children.Add(new FontIcon
            {
                Glyph = "\uE946", // Lightbulb icon
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 50))
            });
            var suggestionTitleStack = new StackPanel { Spacing = 2 };
            _suggestionsTitle = new TextBlock
            {
                Text = "SMART Suggestions",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            suggestionTitleStack.Children.Add(_suggestionsTitle);
            _suggestionsSummary = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            };
            suggestionTitleStack.Children.Add(_suggestionsSummary);
            headerStack.Children.Add(suggestionTitleStack);
            Grid.SetColumn(headerStack, 0);
            Grid.SetColumnSpan(headerStack, 2);
            suggestionsHeader.Children.Add(headerStack);

            // Action buttons
            var actionButtonsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            _addAllHighPriorityButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE73E", FontSize = 12 }, // Check icon
                        new TextBlock { Text = "Add High Priority", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 180, 80, 50)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6)
            };
            _addAllHighPriorityButton.Click += AddAllHighPriority_Click;
            ToolTipService.SetToolTip(_addAllHighPriorityButton, "Add all critical and high priority items");
            actionButtonsPanel.Children.Add(_addAllHighPriorityButton);

            _addSelectedButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE710", FontSize = 12 }, // Add icon
                        new TextBlock { Text = "Add Selected", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6)
            };
            _addSelectedButton.Click += AddSelected_Click;
            ToolTipService.SetToolTip(_addSelectedButton, "Add selected suggestions to the estimate");
            actionButtonsPanel.Children.Add(_addSelectedButton);

            Grid.SetColumn(actionButtonsPanel, 2);
            suggestionsHeader.Children.Add(actionButtonsPanel);

            suggestionsStack.Children.Add(suggestionsHeader);

            // Suggestions container (scrollable)
            var suggestionsScroll = new ScrollViewer
            {
                MaxHeight = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _suggestionsContainer = new StackPanel { Spacing = 4 };
            suggestionsScroll.Content = _suggestionsContainer;
            suggestionsStack.Children.Add(suggestionsScroll);

            _suggestionsSection.Child = suggestionsStack;
            mainStack.Children.Add(_suggestionsSection);

            // === REFERENCE MATCH STATUS ===
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
            mainStack.Children.Add(_refMatchStatusText);

            // === STATUS ===
            _statusText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            mainStack.Children.Add(_statusText);


            // === HELP TEXT ===
            var helpBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 35)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var helpText = new TextBlock
            {
                Text = "SMART Import Features:\n" +
                       "• Auto-detects estimate source (CCC ONE, Mitchell, Audatex)\n" +
                       "• Normalizes abbreviations (LT→Left, Frt→Front, Bpr→Bumper)\n" +
                       "• Identifies additional operations (#): Adhesion Promoter, Flex Additive, De-Nib, etc.\n" +
                       "• Links additional ops to their parent parts automatically\n" +
                       "• Learns patterns: \"Front Bumper + Replace\" → typical operations\n\n" +
                       "How to use:\n" +
                       "1. Upload PDF or paste estimate text from CCC (Ctrl+A → Ctrl+C → Ctrl+V)\n" +
                       "2. Review parsed parts and operations\n" +
                       "3. Click 'Learn from This' to train the system\n" +
                       "4. Future estimates will auto-suggest based on learned patterns",
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
        }

        private void UpdateStats()
        {
            var estimatesImported = _learningService.EstimatesImported;
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

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
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
                    await ProcessFilesAsync(files.ToList());
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Error: {ex.Message}", isError: true);
            }
        }

        #region Drag and Drop

        private void UploadDropZone_DragOver(object sender, DragEventArgs e)
        {
            // Check if files are being dragged
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Drop to import";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
                e.DragUIOverride.IsContentVisible = true;

                // Visual feedback - highlight the drop zone
                if (_uploadDropZone != null)
                {
                    _uploadDropZone.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 150, 255));
                    _uploadDropZone.BorderThickness = new Thickness(3);
                    _uploadDropZone.Background = new SolidColorBrush(Color.FromArgb(255, 35, 45, 55));
                }

                if (_dropZoneText != null)
                {
                    _dropZoneText.Text = "Drop files to import...";
                    _dropZoneText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255));
                }
            }
            else
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            }
        }

        private void UploadDropZone_DragLeave(object sender, DragEventArgs e)
        {
            // Reset visual appearance
            ResetDropZoneAppearance();
        }

        private async void UploadDropZone_Drop(object sender, DragEventArgs e)
        {
            // Reset visual appearance
            ResetDropZoneAppearance();

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
                            // Filter to supported file types
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
                            // Recursively get files from folder
                            var folderFiles = await GetFilesFromFolderAsync(folder);
                            files.AddRange(folderFiles);
                        }
                    }

                    if (files.Count > 0)
                    {
                        ShowStatus($"Processing {files.Count} dropped file(s)...");
                        await ProcessFilesAsync(files);
                    }
                    else
                    {
                        ShowStatus("No supported files found. Drop PDF, TXT, or CSV files.", isError: true);
                    }
                }
                catch (Exception ex)
                {
                    ShowStatus($"Error processing dropped files: {ex.Message}", isError: true);
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

                // Also check subfolders (one level deep)
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

        private void ResetDropZoneAppearance()
        {
            if (_uploadDropZone != null)
            {
                _uploadDropZone.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 70, 80));
                _uploadDropZone.BorderThickness = new Thickness(2);
                _uploadDropZone.Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 40));
            }

            if (_dropZoneText != null)
            {
                _dropZoneText.Text = "Drag & drop files here, or click to browse\nSupports PDF, TXT, CSV - Multiple files for batch learning";
                _dropZoneText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100));
            }
        }

        #endregion

        private async Task ProcessFilesAsync(List<StorageFile> files)
        {
            ShowProgress(true);
            _parsedLines.Clear();
            _parsedItemsList!.Items.Clear();

            int totalLines = 0;
            int totalParts = 0;
            int totalManualLines = 0;
            string? detectedSource = null;
            string? vehicleInfo = null;

            foreach (var file in files)
            {
                try
                {
                    ShowStatus($"Processing: {file.Name}...");

                    if (file.FileType.ToLower() == ".pdf")
                    {
                        // Use SMART PDF parser for structured extraction
                        var estimate = await ReadPdfSmartAsync(file);
                        if (estimate != null && estimate.LineItems.Count > 0)
                        {
                            detectedSource = estimate.Source;
                            vehicleInfo = estimate.VehicleInfo;

                            // Convert to ParsedEstimateLine format
                            foreach (var item in estimate.LineItems)
                            {
                                var parsed = ConvertToParsedLine(item);
                                _parsedLines.Add(parsed);
                            }

                            totalLines += estimate.LineItems.Count;
                            totalParts += estimate.LineItems.Count(i => !i.IsAdditionalOperation && !string.IsNullOrEmpty(i.PartName));
                            totalManualLines += estimate.LineItems.Count(i => i.IsAdditionalOperation);

                            System.Diagnostics.Debug.WriteLine($"[Import] {file.Name}: {estimate.Source} estimate, {estimate.LineItems.Count} items, Vehicle: {estimate.VehicleInfo}");

                            // Auto-save to Estimate History Database and mine for patterns
                            EstimatePersistenceHelper.PersistAndMine(estimate);
                        }
                        else
                        {
                            // Fallback to text extraction if structured parsing fails
                            var text = await ReadPdfAsTextAsync(file);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                var parsed = _learningService.ParseWithManualLineDetection(text);
                                _parsedLines.AddRange(parsed);
                                totalLines += parsed.Count;
                                totalParts += parsed.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
                                totalManualLines += parsed.Count(p => p.IsManualLine);

                                // Persist fallback-parsed PDF to history
                                var fallbackEstimate = EstimatePersistenceHelper.ConvertFromParsedLines(parsed, text, file.Name);
                                EstimatePersistenceHelper.PersistAndMine(fallbackEstimate);
                            }
                        }
                    }
                    else
                    {
                        // Text/CSV files - use learning service parser
                        var text = await FileIO.ReadTextAsync(file);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var parsed = _learningService.ParseWithManualLineDetection(text);
                            _parsedLines.AddRange(parsed);
                            totalLines += parsed.Count;
                            totalParts += parsed.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
                            totalManualLines += parsed.Count(p => p.IsManualLine);

                            // Persist text/CSV file to history
                            var csvEstimate = EstimatePersistenceHelper.ConvertFromParsedLines(parsed, text, file.Name);
                            EstimatePersistenceHelper.PersistAndMine(csvEstimate);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing {file.Name}: {ex.Message}");
                    ShowStatus($"Error processing {file.Name}: {ex.Message}", isError: true);
                }
            }

            // Display parsed lines
            DisplayParsedLines();

            ShowProgress(false);
            _resultsSection!.Visibility = Visibility.Visible;

            // Update results title with source info
            var sourceInfo = !string.IsNullOrEmpty(detectedSource) ? $" ({detectedSource})" : "";
            var vehicleInfoText = !string.IsNullOrEmpty(vehicleInfo) ? $" - {vehicleInfo}" : "";
            _resultsTitle!.Text = $"Parsed: {totalParts} parts, {totalManualLines} additional ops{sourceInfo}{vehicleInfoText}";

            ShowStatus($"Processed {files.Count} file(s): {totalParts} parts, {totalManualLines} additional operations ready to learn");

            // Run SMART analysis for suggestions
            RunSmartAnalysis();
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

        private void ParsePastedText_Click(object sender, RoutedEventArgs e)
        {
            var text = _pasteArea?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowStatus("Please paste estimate text first", isError: true);
                return;
            }

            _parsedLines.Clear();

            // Use SMART parser for pasted text too
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

                DisplayParsedLines();

                var parts = _parsedLines.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
                var additionalOps = _parsedLines.Count(p => p.IsManualLine);

                _resultsSection!.Visibility = Visibility.Visible;
                _resultsTitle!.Text = $"Parsed: {parts} parts, {additionalOps} additional ops{sourceInfo}{vehicleInfo}";

                ShowStatus($"SMART Parse: {parts} parts, {additionalOps} additional operations detected", isSuccess: true);

                // Persist pasted text estimate to history
                estimate.SourceFile = "TextPaste";
                EstimatePersistenceHelper.PersistAndMine(estimate);

                // Run SMART analysis for suggestions
                RunSmartAnalysis();
            }
            else
            {
                // Fallback to old parser
                _parsedLines = _learningService.ParseWithManualLineDetection(text);

                DisplayParsedLines();

                var parts = _parsedLines.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
                var manualLines = _parsedLines.Count(p => p.IsManualLine);

                _resultsSection!.Visibility = Visibility.Visible;
                _resultsTitle!.Text = $"Parsed: {parts} parts, {manualLines} manual lines (#)";

                ShowStatus($"Parsed {_parsedLines.Count} lines: {parts} parts, {manualLines} manual lines");

                // Persist fallback-parsed text paste to history
                var fallbackEstimate = EstimatePersistenceHelper.ConvertFromParsedLines(_parsedLines, text, "TextPaste");
                EstimatePersistenceHelper.PersistAndMine(fallbackEstimate);

                // Run SMART analysis for suggestions
                RunSmartAnalysis();
            }
        }

        private void DisplayParsedLines()
        {
            _parsedItemsList!.Items.Clear();

            string? currentSection = null;

            foreach (var line in _parsedLines.Take(100)) // Limit display
            {
                // Section header detection
                if (!line.IsManualLine && string.IsNullOrEmpty(line.OperationType) &&
                    !string.IsNullOrEmpty(line.PartName) && line.PartName == line.PartName.ToUpper())
                {
                    currentSection = line.PartName;
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
                    _parsedItemsList.Items.Add(sectionHeader);
                    continue;
                }

                var item = CreateParsedLineItem(line);
                _parsedItemsList.Items.Add(item);
            }

            if (_parsedLines.Count > 100)
            {
                _parsedItemsList.Items.Add(new TextBlock
                {
                    Text = $"... and {_parsedLines.Count - 100} more lines",
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    Margin = new Thickness(8)
                });
            }
        }

        private Border CreateParsedLineItem(ParsedEstimateLine line)
        {
            var border = new Border
            {
                Background = line.IsManualLine
                    ? new SolidColorBrush(Color.FromArgb(255, 35, 45, 40))  // Greenish tint for additional ops
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

            // Description (show normalized part name for main parts)
            var desc = !string.IsNullOrEmpty(line.PartName) ? line.PartName :
                       !string.IsNullOrEmpty(line.Description) ? line.Description : line.RawLine;
            if (line.IsManualLine && !string.IsNullOrEmpty(line.Description))
            {
                // For additional ops, show the operation description, not part name
                desc = line.Description;
            }
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
        /// Build operation lines from parsed estimate.
        /// This is available to ALL license tiers.
        /// </summary>
        private async void BuildLinesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_parsedLines.Count == 0)
            {
                ShowStatus("No data to build lines from", isError: true);
                return;
            }

            ShowProgress(true);
            _buildLinesButton!.IsEnabled = false;

            try
            {
                // Generate operation lines using the learning service's pattern matching
                // This works without training - it uses existing learned patterns
                var operationLines = new List<string>();
                var patternsUsed = new List<string>();  // Track for feedback
                int suggestionsFound = 0;

                await Task.Run(() =>
                {
                    foreach (var line in _parsedLines.Where(p => !string.IsNullOrEmpty(p.PartName)))
                    {
                        // Get operations for this part using learned patterns
                        var operations = _learningService.GenerateOperations(line);

                        // Generate pattern key for feedback tracking
                        var patternKey = $"{line.PartName.ToLowerInvariant()}|{line.OperationType?.ToLowerInvariant() ?? "unknown"}";

                        if (operations.Count > 0)
                        {
                            patternsUsed.Add(patternKey);
                        }

                        foreach (var op in operations)
                        {
                            var lineText = $"{op.OperationType}: {op.Description}";
                            if (op.LaborHours > 0)
                                lineText += $" | Labor: {op.LaborHours:F1}";
                            if (op.RefinishHours > 0)
                                lineText += $" | Refinish: {op.RefinishHours:F1}";
                            if (op.Confidence > 0)
                                lineText += $" | Conf: {op.Confidence:P0}";

                            operationLines.Add(lineText);
                            suggestionsFound++;
                        }

                        // Also check for manual line patterns (available to all tiers for reading)
                        var manualPattern = _learningService.GetManualLinesForPart(line.PartName, line.OperationType);
                        if (manualPattern != null)
                        {
                            foreach (var manual in manualPattern.ManualLines)
                            {
                                var manualLine = $"  # {manual.ManualLineType}";
                                if (manual.LaborUnits > 0)
                                    manualLine += $" | Labor: {manual.LaborUnits:F1}";
                                if (manual.RefinishUnits > 0)
                                    manualLine += $" | Refinish: {manual.RefinishUnits:F1}";
                                if (manual.AvgPrice > 0)
                                    manualLine += $" | ~${manual.AvgPrice:F0}";
                                if (manual.TimesUsed > 1)
                                    manualLine += $" ({manual.TimesUsed}x)";
                                operationLines.Add(manualLine);
                                suggestionsFound++;
                            }
                        }
                    }
                });

                // Track feedback for patterns used - they're being "accepted" by copying
                foreach (var patternKey in patternsUsed.Distinct())
                {
                    _feedbackService.RecordOperationGenerated(patternKey, "build_lines");
                    // Count as accepted since user is copying to clipboard to use
                    _feedbackService.RecordOperationAccepted(patternKey, "build_lines");
                }

                // Copy to clipboard
                var clipboardText = string.Join("\n", operationLines);
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(clipboardText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                // Notify health service of feedback
                _healthService.OnFeedbackRecorded();

                ShowStatus($"Built {suggestionsFound} operation lines and copied to clipboard!", isSuccess: true);

                if (suggestionsFound == 0)
                {
                    ShowStatus("No matching patterns found. Import more estimates (with Shop license) to build the knowledge base.", isError: false);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Error building lines: {ex.Message}", isError: true);
            }
            finally
            {
                ShowProgress(false);
                _buildLinesButton!.IsEnabled = true;
            }
        }

        private async void LearnButton_Click(object sender, RoutedEventArgs e)
        {
            // Check license tier
            if (!_learningService.CanTrain)
            {
                ShowStatus("Learning is locked for Client licenses. Contact your shop admin for Shop/Admin access.", isError: true);
                return;
            }

            if (_parsedLines.Count == 0)
            {
                ShowStatus("No data to learn from", isError: true);
                return;
            }

            // Assess quality if not already done
            if (_currentQualityRecord == null)
            {
                _currentQualityRecord = _qualityService.AssessQuality(_parsedLines);
                UpdateQualityPanel(_currentQualityRecord);
            }

            // Check if quality is too low (and not in bootstrap mode)
            if (!_qualityService.IsBootstrapMode() &&
                _currentQualityRecord.Grade == QualityGrade.Rejected &&
                _includeOutliersCheckbox?.IsChecked != true)
            {
                ShowStatus($"Quality too low ({_currentQualityRecord.QualityScore}/100). Check 'Include outliers' to override.", isError: true);
                return;
            }

            ShowProgress(true);
            _learnButton!.IsEnabled = false;

            try
            {
                // Calculate estimate total from all prices
                var estimateTotal = _parsedLines.Sum(p => p.Price);
                var learningWeight = _currentQualityRecord.LearningWeight;

                await Task.Run(() =>
                {
                    // Learn manual line patterns
                    _learningService.LearnManualLinePatterns(_parsedLines);

                    // Build and learn operation patterns
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

                    // Record this estimate for stats tracking
                    _learningService.RecordEstimateImport(estimateTotal);
                });

                // Mark quality record as used for training
                _currentQualityRecord.WasUsedForTraining = true;

                // Notify health service
                _healthService.OnTrainingCompleted(
                    _parsedLines.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName)),
                    1
                );

                UpdateStats();

                var parts = _parsedLines.Count(p => !p.IsManualLine && !string.IsNullOrEmpty(p.PartName));
                var manual = _parsedLines.Count(p => p.IsManualLine);

                var qualityNote = _qualityService.IsBootstrapMode()
                    ? " (Bootstrap mode)"
                    : $" (Quality: {_currentQualityRecord.QualityScore}/100)";

                ShowStatus($"Learned from estimate (${estimateTotal:N0}): {parts} parts, {manual} operations{qualityNote}", isSuccess: true);

                // Notify listeners about training completion
                OnTrainingCompleted?.Invoke(this, new TrainingCompletedEventArgs
                {
                    PartsCount = parts,
                    OperationsCount = manual,
                    EstimateTotal = estimateTotal,
                    QualityScore = _currentQualityRecord?.QualityScore ?? 0
                });

                // Post AI summary to chat feed
                PostTrainingSummaryToChat(parts, manual, estimateTotal);

                // Clear for next batch
                _parsedLines.Clear();
                _parsedItemsList!.Items.Clear();
                _pasteArea!.Text = "";
                _resultsSection!.Visibility = Visibility.Collapsed;
                _qualitySection!.Visibility = Visibility.Collapsed;
                _currentQualityRecord = null;
            }
            catch (Exception ex)
            {
                ShowStatus($"Error learning: {ex.Message}", isError: true);
            }
            finally
            {
                ShowProgress(false);
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
            if (_qualityService.IsBootstrapMode())
            {
                var (current, required) = _qualityService.GetBootstrapProgress();
                _qualityWeightText!.Text = $"Bootstrap Mode ({current}/{required} estimates)";
            }
            else
            {
                _qualityWeightText!.Text = $"Learning Weight: {quality.LearningWeight:F2}x";
            }

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
            // Check license tier
            if (!_learningService.CanTrain)
            {
                ShowStatus("Publishing is only available for Shop/Admin users.", isError: true);
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
                    ShowStatus("✓ Learning published! Ready for distribution.", isSuccess: true);

                    // Show success dialog with path
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
                    ShowStatus($"Error publishing: {message}", isError: true);
                }
            }
        }

        private async void ExportBaselineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_learningService.CurrentTier == LicenseTier.Client)
            {
                ShowStatus("Publishing knowledge is only available for Shop/Admin users.", isError: true);
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
                ShowStatus("Publishing knowledge...");

                var exportResult = BaselineExportTool.ExportAndSanitize();

                if (exportResult.Success)
                {
                    ShowStatus($"Knowledge published as v{exportResult.NewVersion}!", isSuccess: true);

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
                    ShowStatus($"Publish failed: {exportResult.Message}", isError: true);
                }
            }
        }

        private void ShowStatus(string message, bool isError = false, bool isSuccess = false)
        {
            _statusText!.Text = message;
            _statusText.Foreground = new SolidColorBrush(
                isError ? Color.FromArgb(255, 255, 120, 120) :
                isSuccess ? Color.FromArgb(255, 100, 220, 150) :
                Color.FromArgb(255, 180, 180, 180)
            );
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
                sb.AppendLine($"   AI now trained on {stats.TotalEstimatesTrained} estimates | {stats.TotalPatterns} patterns");

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
            _progressRing!.IsActive = show;
            _progressRing.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _uploadButton!.IsEnabled = !show;
        }

        #region Smart Analysis

        /// <summary>
        /// Run SMART analysis on parsed lines and display suggestions
        /// </summary>
        private void RunSmartAnalysis()
        {
            if (_parsedLines.Count == 0)
            {
                _suggestionsSection!.Visibility = Visibility.Collapsed;
                _qualitySection!.Visibility = Visibility.Collapsed;
                _scoringPanel!.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                // Run quality assessment
                _currentQualityRecord = _qualityService.AssessQuality(_parsedLines);
                UpdateQualityPanel(_currentQualityRecord);

                // Run estimate completeness scoring
                RunEstimateScoring();

                // Run SMART suggestions analysis
                _currentAnalysis = _analyzerService.AnalyzeEstimate(_parsedLines);

                if (_currentAnalysis.Suggestions.Count > 0)
                {
                    DisplaySuggestions(_currentAnalysis);
                    _suggestionsSection!.Visibility = Visibility.Visible;

                    System.Diagnostics.Debug.WriteLine($"[SmartAnalysis] Found {_currentAnalysis.Suggestions.Count} suggestions");
                }
                else
                {
                    _suggestionsSection!.Visibility = Visibility.Collapsed;
                }

                // Run reference matching to auto-populate PDF queue
                _ = RunReferenceMatchingAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartAnalysis] Error: {ex.Message}");
                _suggestionsSection!.Visibility = Visibility.Collapsed;
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
        /// Run estimate completeness scoring and update the scoring panel
        /// </summary>
        private void RunEstimateScoring()
        {
            if (_parsedLines.Count == 0 || _scoringPanel == null)
            {
                if (_scoringPanel != null)
                {
                    _scoringPanel.Visibility = Visibility.Collapsed;
                }
                return;
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
                var scoringResult = _scoringService.ScoreEstimate(_parsedLines, vehicleInfo);

                // Update the panel
                _scoringPanel.UpdateScore(scoringResult);
                _scoringPanel.Visibility = Visibility.Visible;

                System.Diagnostics.Debug.WriteLine($"[Scoring] Score: {scoringResult.OverallScore}%, Issues: {scoringResult.Issues.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scoring] Error: {ex.Message}");
                _scoringPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Handle adding items from the scoring panel
        /// </summary>
        private void ScoringPanel_OnAddItems(object? sender, List<ScoringIssue> items)
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
                        Description = $"{item.SuggestedFix.Description} - {item.WhyNeeded}",
                        PartName = item.Title,
                        OperationType = item.SuggestedFix.OperationType,
                        LaborHours = item.SuggestedFix.LaborHours,
                        Price = item.SuggestedFix.EstimatedCost,
                        Category = GetCategoryCode(item.Category)
                    };

                    // Add to parsed lines if not already present
                    bool alreadyExists = _parsedLines.Any(l =>
                        l.Description?.ToLowerInvariant() == newLine.Description?.ToLowerInvariant() ||
                        l.PartName?.ToLowerInvariant() == newLine.PartName?.ToLowerInvariant());

                    if (!alreadyExists)
                    {
                        _parsedLines.Add(newLine);
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    // Refresh the display
                    DisplayParsedLines();

                    // Re-run scoring to update
                    RunEstimateScoring();

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

        /// <summary>
        /// Display suggestions in the UI
        /// </summary>
        private void DisplaySuggestions(AnalysisResult analysis)
        {
            _suggestionsContainer!.Children.Clear();

            // Update summary
            var criticalText = analysis.CriticalCount > 0 ? $"{analysis.CriticalCount} CRITICAL, " : "";
            var highText = analysis.HighPriorityCount > 0 ? $"{analysis.HighPriorityCount} high priority, " : "";
            _suggestionsSummary!.Text = $"Found {analysis.Suggestions.Count} commonly missed items: {criticalText}{highText}based on industry knowledge";

            // Group suggestions by category
            var grouped = analysis.Suggestions
                .GroupBy(s => s.Category)
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                // Category header
                var categoryHeader = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 45, 40, 55)),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 8, 0, 4),
                    CornerRadius = new CornerRadius(4)
                };
                var categoryStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                categoryStack.Children.Add(new FontIcon
                {
                    Glyph = GetCategoryIcon(group.Key),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(GetCategoryColor(group.Key))
                });
                categoryStack.Children.Add(new TextBlock
                {
                    Text = $"{group.Key.ToString().Replace("RAndI", "R&I")} ({group.Count()})",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                });
                categoryHeader.Child = categoryStack;
                _suggestionsContainer.Children.Add(categoryHeader);

                // Items in category
                foreach (var suggestion in group)
                {
                    var itemUI = CreateSuggestionItem(suggestion);
                    _suggestionsContainer.Children.Add(itemUI);
                }
            }
        }

        /// <summary>
        /// Create a suggestion item UI with checkbox
        /// </summary>
        private Border CreateSuggestionItem(SmartSuggestedOperation suggestion)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 32, 42)),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 2, 0, 2),
                CornerRadius = new CornerRadius(4),
                Tag = suggestion
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });   // Checkbox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });   // Priority badge
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Item + description
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });   // Hours
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });   // Cost
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });  // Source

            // Checkbox
            var checkbox = new CheckBox
            {
                IsChecked = suggestion.Priority == "critical" || suggestion.Priority == "high",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0
            };
            checkbox.Checked += (s, e) => suggestion.IsSelected = true;
            checkbox.Unchecked += (s, e) => suggestion.IsSelected = false;
            suggestion.IsSelected = checkbox.IsChecked == true;
            Grid.SetColumn(checkbox, 0);
            grid.Children.Add(checkbox);

            // Priority badge
            var priorityBadge = new Border
            {
                Background = new SolidColorBrush(GetPriorityColor(suggestion.Priority)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            priorityBadge.Child = new TextBlock
            {
                Text = suggestion.Priority.ToUpperInvariant(),
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Grid.SetColumn(priorityBadge, 1);
            grid.Children.Add(priorityBadge);

            // Item name and description
            var itemStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var itemName = new TextBlock
            {
                Text = suggestion.Item,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            itemStack.Children.Add(itemName);

            if (!string.IsNullOrEmpty(suggestion.WhyNeeded))
            {
                var whyText = new TextBlock
                {
                    Text = suggestion.WhyNeeded,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                itemStack.Children.Add(whyText);
            }
            Grid.SetColumn(itemStack, 2);
            grid.Children.Add(itemStack);

            // Labor hours
            if (suggestion.LaborHours > 0)
            {
                var hoursText = new TextBlock
                {
                    Text = $"{suggestion.LaborHours:F1} hrs",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(hoursText, 3);
                grid.Children.Add(hoursText);
            }

            // Cost
            if (suggestion.TypicalCost > 0)
            {
                var costText = new TextBlock
                {
                    Text = $"${suggestion.TypicalCost:N0}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 100)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(costText, 4);
                grid.Children.Add(costText);
            }

            // Source (what triggered this)
            var sourceText = new TextBlock
            {
                Text = suggestion.SourcePart.Length > 18 ? suggestion.SourcePart.Substring(0, 15) + "..." : suggestion.SourcePart,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 150, 200)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            ToolTipService.SetToolTip(sourceText, $"Triggered by: {suggestion.SourcePart}");
            Grid.SetColumn(sourceText, 5);
            grid.Children.Add(sourceText);

            // Tooltip with full details
            var tooltipText = $"{suggestion.Item}\n\n";
            if (!string.IsNullOrEmpty(suggestion.Description)) tooltipText += $"{suggestion.Description}\n\n";
            if (!string.IsNullOrEmpty(suggestion.WhyNeeded)) tooltipText += $"Why: {suggestion.WhyNeeded}\n";
            if (!string.IsNullOrEmpty(suggestion.DegReference)) tooltipText += $"DEG: {suggestion.DegReference}\n";
            if (!string.IsNullOrEmpty(suggestion.AffectedSensors)) tooltipText += $"Sensors: {suggestion.AffectedSensors}\n";
            ToolTipService.SetToolTip(border, tooltipText.Trim());

            border.Child = grid;
            return border;
        }

        private string GetCategoryIcon(SuggestionCategory category)
        {
            return category switch
            {
                SuggestionCategory.Calibration => "\uE945",  // Settings icon
                SuggestionCategory.Diagnostic => "\uE9D9",   // Scan icon
                SuggestionCategory.Electrical => "\uE945",   // Lightning bolt
                SuggestionCategory.Materials => "\uE8B1",    // Box/package
                SuggestionCategory.RAndI => "\uE895",        // Refresh/cycle
                SuggestionCategory.Labor => "\uE902",        // Clock
                SuggestionCategory.Refinish => "\uE790",     // Paint brush
                SuggestionCategory.Mechanical => "\uE90F",   // Wrench
                _ => "\uE8FD"                                 // List
            };
        }

        private Color GetCategoryColor(SuggestionCategory category)
        {
            return category switch
            {
                SuggestionCategory.Calibration => Color.FromArgb(255, 255, 100, 100),
                SuggestionCategory.Diagnostic => Color.FromArgb(255, 200, 150, 255),
                SuggestionCategory.Electrical => Color.FromArgb(255, 255, 200, 50),
                SuggestionCategory.Materials => Color.FromArgb(255, 150, 200, 255),
                SuggestionCategory.RAndI => Color.FromArgb(255, 100, 200, 150),
                SuggestionCategory.Labor => Color.FromArgb(255, 255, 180, 100),
                SuggestionCategory.Refinish => Color.FromArgb(255, 100, 180, 255),
                SuggestionCategory.Mechanical => Color.FromArgb(255, 200, 200, 200),
                _ => Color.FromArgb(255, 180, 180, 180)
            };
        }

        private Color GetPriorityColor(string? priority)
        {
            return priority?.ToLowerInvariant() switch
            {
                "critical" => Color.FromArgb(255, 200, 50, 50),
                "high" => Color.FromArgb(255, 200, 100, 50),
                "medium" => Color.FromArgb(255, 150, 150, 80),
                "low" => Color.FromArgb(255, 80, 120, 80),
                _ => Color.FromArgb(255, 100, 100, 100)
            };
        }

        private void AddSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnalysis == null) return;

            var selected = _currentAnalysis.Suggestions.Where(s => s.IsSelected).ToList();
            if (selected.Count == 0)
            {
                ShowStatus("No items selected. Check the boxes next to items you want to add.", isError: true);
                return;
            }

            AddSuggestionsToEstimate(selected);
        }

        private void AddAllHighPriority_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnalysis == null) return;

            var highPriority = _currentAnalysis.Suggestions
                .Where(s => s.Priority == "critical" || s.Priority == "high")
                .ToList();

            if (highPriority.Count == 0)
            {
                ShowStatus("No critical or high priority items found.", isError: true);
                return;
            }

            AddSuggestionsToEstimate(highPriority);
        }

        private void AddSuggestionsToEstimate(List<SmartSuggestedOperation> suggestions)
        {
            // Add suggestions to parsed lines so they can be learned or exported
            foreach (var suggestion in suggestions)
            {
                var newLine = new ParsedEstimateLine
                {
                    RawLine = $"[SUGGESTED] {suggestion.Item}",
                    Description = suggestion.Description ?? suggestion.Item,
                    PartName = suggestion.Item,
                    OperationType = "Add",
                    LaborHours = suggestion.LaborHours,
                    RepairHours = suggestion.LaborHours,
                    RefinishHours = 0,
                    Price = suggestion.TypicalCost,
                    Category = suggestion.Category.ToString(),
                    IsManualLine = true,
                    ParentPartName = suggestion.SourcePart
                };
                _parsedLines.Add(newLine);
            }

            // Refresh display
            DisplayParsedLines();

            // Update suggestion checkboxes to reflect they were added
            foreach (var suggestion in suggestions)
            {
                suggestion.IsSelected = false;
            }

            // Refresh suggestions UI
            if (_currentAnalysis != null)
            {
                // Remove added items from suggestions
                foreach (var suggestion in suggestions)
                {
                    _currentAnalysis.Suggestions.Remove(suggestion);
                }
                DisplaySuggestions(_currentAnalysis);

                if (_currentAnalysis.Suggestions.Count == 0)
                {
                    _suggestionsSection!.Visibility = Visibility.Collapsed;
                }
            }

            ShowStatus($"Added {suggestions.Count} suggested operations to the estimate. Click 'Learn from This' to train the system.", isSuccess: true);
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
