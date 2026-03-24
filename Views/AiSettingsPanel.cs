#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;
using System;
using System.Diagnostics;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Admin-only settings panel for Claude AI integration.
    /// Shows status, configuration, budget controls, feature toggles, and usage dashboard.
    /// </summary>
    public class AiSettingsPanel : UserControl
    {
        private TextBlock? _statusText;
        private FontIcon? _statusIcon;
        private ProgressBar? _budgetProgress;
        private TextBlock? _budgetLabel;
        private TextBlock? _todayCallsText;
        private TextBlock? _todayCostText;
        private TextBlock? _allTimeCallsText;
        private TextBlock? _allTimeCostText;
        private TextBlock? _featureBreakdownText;
        private ToggleSwitch? _enabledToggle;
        private Border? _configSectionBorder;
        private bool _adminUnlocked;
        private TextBlock? _analysisResultText;
        private Button? _copyReportButton;
        private Button? _exportPdfButton;
        private ProgressRing? _analysisProgress;
        private LearningAnalysisReport? _lastReport;
        private CancellationTokenSource? _analysisCts;

        private static AiSettingsPanel? _instance;

        public AiSettingsPanel()
        {
            _instance = this;
            BuildUI();
            RefreshUsageDisplay();
        }

        /// <summary>
        /// Called from McStudToolView after successful PIN entry to reveal the config section.
        /// </summary>
        public static void ShowConfigSection()
        {
            if (_instance?._configSectionBorder != null)
            {
                _instance._adminUnlocked = true;
                _instance._configSectionBorder.Visibility = Visibility.Visible;
                _instance._configSectionBorder.Child = _instance.BuildUnlockedConfigView();
            }
        }

        private void BuildUI()
        {
            var mainStack = new StackPanel { Spacing = 12 };

            // === HEADER ===
            var headerStack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 4) };
            headerStack.Children.Add(new TextBlock
            {
                Text = "AI Assistant (Claude)",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "AI-powered chat, estimate review, OCR cleanup, panel detection, and more",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            mainStack.Children.Add(headerStack);

            // === STATUS SECTION ===
            mainStack.Children.Add(BuildStatusSection());

            // === CONFIGURATION SECTION (hidden — revealed via About 5-tap + PIN) ===
            mainStack.Children.Add(BuildConfigSection());
            if (_configSectionBorder != null)
                _configSectionBorder.Visibility = Visibility.Collapsed;

            // === BUDGET SECTION ===
            mainStack.Children.Add(BuildBudgetSection());

            // === FEATURE TOGGLES ===
            mainStack.Children.Add(BuildFeatureTogglesSection());

            // === USAGE DASHBOARD ===
            mainStack.Children.Add(BuildUsageDashboard());

            Content = mainStack;
        }

        private Border BuildStatusSection()
        {
            var border = CreateSectionBorder();
            var stack = new StackPanel { Spacing = 10 };

            stack.Children.Add(CreateSectionHeader("Status", "\uE946"));

            // Enabled toggle
            var toggleRow = new Grid();
            toggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var config = AiConfigService.Instance;

            toggleRow.Children.Add(new TextBlock
            {
                Text = "Enable AI features",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _enabledToggle = new ToggleSwitch
            {
                IsOn = config.Config.Enabled,
                OnContent = "On",
                OffContent = "Off"
            };
            _enabledToggle.Toggled += (s, e) =>
            {
                AiConfigService.Instance.SetEnabled(_enabledToggle.IsOn);
                UpdateStatusIndicator();
            };
            Grid.SetColumn(_enabledToggle, 1);
            toggleRow.Children.Add(_enabledToggle);
            stack.Children.Add(toggleRow);

            // Status indicator
            var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _statusIcon = new FontIcon
            {
                Glyph = "\uEA8C",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            };
            statusRow.Children.Add(_statusIcon);
            _statusText = new TextBlock
            {
                Text = "Checking...",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusRow.Children.Add(_statusText);
            stack.Children.Add(statusRow);

            // Test connection button
            var testButton = new Button
            {
                Content = "Test Connection",
                Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            testButton.Click += async (s, e) =>
            {
                testButton.IsEnabled = false;
                _statusText.Text = "Testing...";
                _statusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 100));

                var (success, message) = await ClaudeApiService.Instance.TestConnectionAsync();

                _statusText.Text = message;
                if (success)
                {
                    _statusIcon.Glyph = "\uE73E"; // checkmark
                    _statusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));
                }
                else
                {
                    _statusIcon.Glyph = "\uEA39"; // error
                    _statusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 100, 100));
                }
                testButton.IsEnabled = true;
            };
            stack.Children.Add(testButton);

            UpdateStatusIndicator();

            border.Child = stack;
            return border;
        }

        private Border BuildConfigSection()
        {
            _configSectionBorder = CreateSectionBorder();
            _configSectionBorder.Child = BuildUnlockedConfigView();
            return _configSectionBorder;
        }

        private StackPanel BuildUnlockedConfigView()
        {
            var stack = new StackPanel { Spacing = 10 };

            var config = AiConfigService.Instance;
            stack.Children.Add(CreateSectionHeader("Configuration", "\uE713"));

            // API key override
            stack.Children.Add(new TextBlock
            {
                Text = "API Key (leave blank to use embedded default)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190))
            });
            var keyBox = new PasswordBox
            {
                PlaceholderText = "sk-ant-...",
                Password = config.Config.ApiKeyOverride ?? "",
                Width = 350,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            keyBox.LostFocus += (s, e) =>
            {
                var key = keyBox.Password?.Trim();
                AiConfigService.Instance.SetApiKeyOverride(string.IsNullOrWhiteSpace(key) ? null : key);
                UpdateStatusIndicator();
            };
            stack.Children.Add(keyBox);

            // Model selection
            stack.Children.Add(new TextBlock
            {
                Text = "Model",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190)),
                Margin = new Thickness(0, 4, 0, 0)
            });
            var modelCombo = new ComboBox
            {
                Width = 350,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            modelCombo.Items.Add("claude-3-haiku-20240307");
            modelCombo.Items.Add("claude-haiku-4-5-20251001");
            modelCombo.Items.Add("claude-sonnet-4-20250514");
            modelCombo.SelectedItem = config.GetModelName();
            if (modelCombo.SelectedItem == null) modelCombo.SelectedIndex = 0;
            modelCombo.SelectionChanged += (s, e) =>
            {
                if (modelCombo.SelectedItem is string model)
                    AiConfigService.Instance.SetModelName(model);
            };
            stack.Children.Add(modelCombo);

            // === Admin PIN section ===
            var pinSection = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 0) };
            pinSection.Children.Add(new TextBlock
            {
                Text = config.HasAdminPin ? "Admin PIN (set)" : "Admin PIN (not set)",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190))
            });
            pinSection.Children.Add(new TextBlock
            {
                Text = "Set a PIN to hide the API key and model from other users",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130))
            });

            var pinInputRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var newPinBox = new PasswordBox
            {
                PlaceholderText = config.HasAdminPin ? "New PIN (or blank to remove)" : "Set a PIN",
                Width = 200
            };
            var setPinButton = new Button
            {
                Content = config.HasAdminPin ? "Update PIN" : "Set PIN",
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            var pinStatus = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                VerticalAlignment = VerticalAlignment.Center
            };

            setPinButton.Click += (s, e) =>
            {
                var pin = newPinBox.Password?.Trim();
                if (string.IsNullOrWhiteSpace(pin))
                {
                    AiConfigService.Instance.SetAdminPin(null);
                    pinStatus.Text = "PIN removed";
                    setPinButton.Content = "Set PIN";
                }
                else
                {
                    AiConfigService.Instance.SetAdminPin(pin);
                    pinStatus.Text = "PIN set!";
                    setPinButton.Content = "Update PIN";
                }
                newPinBox.Password = "";
            };

            pinInputRow.Children.Add(newPinBox);
            pinInputRow.Children.Add(setPinButton);
            pinInputRow.Children.Add(pinStatus);
            pinSection.Children.Add(pinInputRow);
            stack.Children.Add(pinSection);

            // === LEARNING ANALYSIS SECTION ===
            stack.Children.Add(BuildLearningAnalysisSection());

            return stack;
        }

        private Border BuildBudgetSection()
        {
            var border = CreateSectionBorder();
            var stack = new StackPanel { Spacing = 10 };

            stack.Children.Add(CreateSectionHeader("Budget Controls", "\uE8CB"));

            var config = AiConfigService.Instance;

            // Daily budget cap
            var budgetRow = new Grid();
            budgetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            budgetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            budgetRow.Children.Add(new TextBlock
            {
                Text = "Daily budget cap ($)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190)),
                VerticalAlignment = VerticalAlignment.Center
            });

            var budgetBox = new NumberBox
            {
                Value = (double)config.GetDailyBudgetCap(),
                Minimum = 0.01,
                Maximum = 100,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 120,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            budgetBox.ValueChanged += (s, e) =>
            {
                if (!double.IsNaN(budgetBox.Value))
                    AiConfigService.Instance.SetDailyBudgetCap((decimal)budgetBox.Value);
            };
            Grid.SetColumn(budgetBox, 1);
            budgetRow.Children.Add(budgetBox);
            stack.Children.Add(budgetRow);

            // Max calls per day
            var callsRow = new Grid();
            callsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            callsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            callsRow.Children.Add(new TextBlock
            {
                Text = "Max calls per day",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 185, 190)),
                VerticalAlignment = VerticalAlignment.Center
            });

            var callsBox = new NumberBox
            {
                Value = config.GetMaxCallsPerDay(),
                Minimum = 1,
                Maximum = 10000,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 120,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            callsBox.ValueChanged += (s, e) =>
            {
                if (!double.IsNaN(callsBox.Value))
                    AiConfigService.Instance.SetMaxCallsPerDay((int)callsBox.Value);
            };
            Grid.SetColumn(callsBox, 1);
            callsRow.Children.Add(callsBox);
            stack.Children.Add(callsRow);

            // Budget progress bar
            _budgetLabel = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(0, 4, 0, 0)
            };
            stack.Children.Add(_budgetLabel);

            _budgetProgress = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 6,
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(_budgetProgress);

            border.Child = stack;
            return border;
        }

        private Border BuildFeatureTogglesSection()
        {
            var border = CreateSectionBorder();
            var stack = new StackPanel { Spacing = 8 };

            stack.Children.Add(CreateSectionHeader("Feature Toggles", "\uE9E9"));

            var config = AiConfigService.Instance;

            stack.Children.Add(CreateFeatureToggle("OCR Text Cleanup", "Clean up raw OCR text using AI for better parsing", AiFeature.OcrCleanup, config.Config.FeatureToggles.OcrCleanup));
            stack.Children.Add(CreateFeatureToggle("Ghost Panel Detection", "Use AI to parse damage descriptions into affected panels", AiFeature.GhostPanelDetection, config.Config.FeatureToggles.GhostPanelDetection));
            stack.Children.Add(CreateFeatureToggle("Part Name Matching", "Resolve unknown part names to canonical names using AI", AiFeature.PartMatching, config.Config.FeatureToggles.PartMatching));
            stack.Children.Add(CreateFeatureToggle("Suggestion Dedup", "Use AI to detect duplicate coaching suggestions", AiFeature.SuggestionDedup, config.Config.FeatureToggles.SuggestionDedup));
            stack.Children.Add(CreateFeatureToggle("Ghost Explanation", "Generate plain-English summaries of ghost estimates", AiFeature.GhostExplanation, config.Config.FeatureToggles.GhostExplanation));
            stack.Children.Add(CreateFeatureToggle("AI Chat Assistant", "Context-aware AI responses in the chat tab with full estimate knowledge", AiFeature.Chat, config.Config.FeatureToggles.ChatAssistant));
            stack.Children.Add(CreateFeatureToggle("AI Estimate Review", "AI-powered estimate review checking must-haves and commonly missed items", AiFeature.EstimateReview, config.Config.FeatureToggles.EstimateReview));
            stack.Children.Add(CreateFeatureToggle("Automation", "AI-driven automation to enter operations in CCC/Mitchell from chat commands", AiFeature.Automation, config.Config.FeatureToggles.Automation));

            border.Child = stack;
            return border;
        }

        private Border BuildUsageDashboard()
        {
            var border = CreateSectionBorder();
            var stack = new StackPanel { Spacing = 10 };

            stack.Children.Add(CreateSectionHeader("Usage Dashboard", "\uE9D2"));

            // Today stats
            var todayGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            todayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            todayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var todayCallsStack = new StackPanel();
            todayCallsStack.Children.Add(new TextBlock
            {
                Text = "Today's Calls",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130))
            });
            _todayCallsText = new TextBlock
            {
                Text = "0",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            };
            todayCallsStack.Children.Add(_todayCallsText);
            todayGrid.Children.Add(todayCallsStack);

            var todayCostStack = new StackPanel();
            todayCostStack.Children.Add(new TextBlock
            {
                Text = "Today's Cost",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130))
            });
            _todayCostText = new TextBlock
            {
                Text = "$0.0000",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
            };
            todayCostStack.Children.Add(_todayCostText);
            Grid.SetColumn(todayCostStack, 1);
            todayGrid.Children.Add(todayCostStack);
            stack.Children.Add(todayGrid);

            // All-time stats
            var allTimeGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            allTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            allTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var allTimeCallsStack = new StackPanel();
            allTimeCallsStack.Children.Add(new TextBlock
            {
                Text = "All-Time Calls",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130))
            });
            _allTimeCallsText = new TextBlock
            {
                Text = "0",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
            };
            allTimeCallsStack.Children.Add(_allTimeCallsText);
            allTimeGrid.Children.Add(allTimeCallsStack);

            var allTimeCostStack = new StackPanel();
            allTimeCostStack.Children.Add(new TextBlock
            {
                Text = "All-Time Cost",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130))
            });
            _allTimeCostText = new TextBlock
            {
                Text = "$0.0000",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
            };
            allTimeCostStack.Children.Add(_allTimeCostText);
            Grid.SetColumn(allTimeCostStack, 1);
            allTimeGrid.Children.Add(allTimeCostStack);
            stack.Children.Add(allTimeGrid);

            // Per-feature breakdown
            _featureBreakdownText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            stack.Children.Add(_featureBreakdownText);

            // Refresh button
            var refreshButton = new Button
            {
                Content = "Refresh",
                Padding = new Thickness(12, 4, 12, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };
            refreshButton.Click += (s, e) => RefreshUsageDisplay();
            stack.Children.Add(refreshButton);

            border.Child = stack;
            return border;
        }

        private StackPanel BuildLearningAnalysisSection()
        {
            var section = new StackPanel { Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };

            section.Children.Add(CreateSectionHeader("Learning Analysis", "\uE9D5"));
            section.Children.Add(new TextBlock
            {
                Text = "Analyze learned patterns from uploaded estimates and compare against static reference data to find gaps and improvements.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                TextWrapping = TextWrapping.Wrap
            });

            // Button row
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };

            var analyzeButton = new Button
            {
                Content = "Analyze Learning Data",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };

            _analysisProgress = new ProgressRing
            {
                IsActive = false,
                Width = 20,
                Height = 20,
                Visibility = Visibility.Collapsed
            };

            analyzeButton.Click += async (s, e) =>
            {
                analyzeButton.IsEnabled = false;
                _analysisProgress.IsActive = true;
                _analysisProgress.Visibility = Visibility.Visible;
                if (_analysisResultText != null)
                    _analysisResultText.Text = "Analyzing learned patterns... This may take a moment.";
                if (_copyReportButton != null) _copyReportButton.Visibility = Visibility.Collapsed;
                if (_exportPdfButton != null) _exportPdfButton.Visibility = Visibility.Collapsed;

                _analysisCts?.Cancel();
                _analysisCts = new CancellationTokenSource();

                try
                {
                    var report = await AiLearningAnalysisService.Instance.AnalyzeAndRecommendAsync(_analysisCts.Token);
                    _lastReport = report;

                    if (report != null && _analysisResultText != null)
                    {
                        _analysisResultText.Text = report.RawReportText;
                        if (_copyReportButton != null) _copyReportButton.Visibility = Visibility.Visible;
                        if (_exportPdfButton != null) _exportPdfButton.Visibility = Visibility.Visible;
                    }
                    else if (_analysisResultText != null)
                    {
                        _analysisResultText.Text = "Analysis failed. Ensure AI is enabled with the Automation feature toggle on, and that you have an API key configured.";
                    }
                }
                catch (OperationCanceledException)
                {
                    if (_analysisResultText != null)
                        _analysisResultText.Text = "Analysis cancelled.";
                }
                catch (Exception ex)
                {
                    if (_analysisResultText != null)
                        _analysisResultText.Text = $"Error: {ex.Message}";
                }
                finally
                {
                    analyzeButton.IsEnabled = true;
                    _analysisProgress.IsActive = false;
                    _analysisProgress.Visibility = Visibility.Collapsed;
                }
            };

            buttonRow.Children.Add(analyzeButton);
            buttonRow.Children.Add(_analysisProgress);
            section.Children.Add(buttonRow);

            // Results area
            var resultsScroll = new ScrollViewer
            {
                MaxHeight = 400,
                Margin = new Thickness(0, 4, 0, 0)
            };
            _analysisResultText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                FontFamily = new FontFamily("Consolas")
            };
            resultsScroll.Content = _analysisResultText;
            section.Children.Add(resultsScroll);

            // Copy and Export buttons
            var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            _copyReportButton = new Button
            {
                Content = "Copy Report",
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4),
                Visibility = Visibility.Collapsed
            };
            _copyReportButton.Click += (s, e) =>
            {
                if (_lastReport?.RawReportText != null)
                {
                    var dataPackage = new DataPackage();
                    dataPackage.SetText(_lastReport.RawReportText);
                    Clipboard.SetContent(dataPackage);
                    if (_copyReportButton.Content is string)
                    {
                        _copyReportButton.Content = "Copied!";
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                        timer.Tick += (t, te) => { _copyReportButton.Content = "Copy Report"; timer.Stop(); };
                        timer.Start();
                    }
                }
            };

            _exportPdfButton = new Button
            {
                Content = "Export PDF",
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4),
                Visibility = Visibility.Collapsed
            };
            _exportPdfButton.Click += (s, e) =>
            {
                if (_lastReport == null) return;
                try
                {
                    var pdfPath = LearningReportPdfService.Instance.GenerateReportPdf(_lastReport);
                    Process.Start(new ProcessStartInfo { FileName = pdfPath, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    if (_analysisResultText != null)
                        _analysisResultText.Text += $"\n\nPDF export error: {ex.Message}";
                }
            };

            actionRow.Children.Add(_copyReportButton);
            actionRow.Children.Add(_exportPdfButton);
            section.Children.Add(actionRow);

            return section;
        }

        #region Helpers

        private Border CreateSectionBorder()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 38, 43, 52)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 4, 0, 4)
            };
        }

        private StackPanel CreateSectionHeader(string title, string iconGlyph)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 4) };
            panel.Children.Add(new FontIcon
            {
                Glyph = iconGlyph,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255))
            });
            return panel;
        }

        private Grid CreateFeatureToggle(string label, string description, AiFeature feature, bool isOn)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
            });
            textStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                TextWrapping = TextWrapping.Wrap
            });
            grid.Children.Add(textStack);

            var toggle = new ToggleSwitch
            {
                IsOn = isOn,
                OnContent = "",
                OffContent = ""
            };
            toggle.Toggled += (s, e) =>
            {
                AiConfigService.Instance.SetFeatureToggle(feature, toggle.IsOn);
            };
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);

            return grid;
        }

        private void UpdateStatusIndicator()
        {
            if (_statusText == null || _statusIcon == null) return;

            var config = AiConfigService.Instance;
            if (!config.Config.Enabled)
            {
                _statusText.Text = "AI features disabled";
                _statusIcon.Glyph = "\uEA8C"; // circle
                _statusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));
            }
            else if (string.IsNullOrWhiteSpace(config.GetApiKey()))
            {
                _statusText.Text = "No API key configured";
                _statusIcon.Glyph = "\uE7BA"; // warning
                _statusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 100));
            }
            else
            {
                _statusText.Text = "Ready — use Test Connection to verify";
                _statusIcon.Glyph = "\uE73E"; // checkmark
                _statusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));
            }
        }

        public void RefreshUsageDisplay()
        {
            var tracker = AiUsageTracker.Instance;
            var todayStats = tracker.GetTodayStats();
            var allTimeSummary = tracker.GetSummary(AiUsagePeriod.AllTime);
            var config = AiConfigService.Instance;

            if (_todayCallsText != null) _todayCallsText.Text = todayStats.TotalCalls.ToString();
            if (_todayCostText != null) _todayCostText.Text = $"${todayStats.TotalCost:F4}";
            if (_allTimeCallsText != null) _allTimeCallsText.Text = allTimeSummary.TotalCalls.ToString();
            if (_allTimeCostText != null) _allTimeCostText.Text = $"${allTimeSummary.TotalCost:F4}";

            // Budget progress
            if (_budgetProgress != null && _budgetLabel != null)
            {
                var cap = config.GetDailyBudgetCap();
                var used = todayStats.TotalCost;
                var pct = cap > 0 ? Math.Min(100, (double)(used / cap) * 100) : 0;
                _budgetProgress.Value = pct;
                _budgetLabel.Text = $"${used:F4} / ${cap:F2} today ({todayStats.TotalCalls}/{config.GetMaxCallsPerDay()} calls)";
            }

            // Feature breakdown
            if (_featureBreakdownText != null && allTimeSummary.PerFeature.Count > 0)
            {
                var lines = new System.Text.StringBuilder();
                foreach (var kvp in allTimeSummary.PerFeature)
                    lines.AppendLine($"  {kvp.Key}: {kvp.Value.Calls} calls, ${kvp.Value.Cost:F4}");
                _featureBreakdownText.Text = lines.ToString().TrimEnd();
            }
        }

        #endregion
    }
}
