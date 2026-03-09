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

        // Status display
        private TextBlock? _statusText;
        private TextBlock? _sourceText;
        private TextBlock? _captureCountText;
        private TextBlock? _changeCountText;
        private TextBlock? _lastChangeText;

        // Results display
        private StackPanel? _operationsPanel;
        private TextBox? _rawTextBox;
        private Border? _rawTextSection;
        private bool _rawTextExpanded = false;
        private TextBlock? _refMatchStatusText;

        // Live Coach
        private Button? _liveCoachButton;

        // State
        private ScreenOcrResult? _latestResult;

        // Events
        public event EventHandler<ScreenOcrResult>? OnFeedToChat;
        public event EventHandler<ScreenOcrResult>? OnLoadToBuilder;

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
            mainStack.Children.Add(CreateOperationsSection());
            mainStack.Children.Add(CreateRawTextSection());
            mainStack.Children.Add(CreateActionsSection());

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

            // Live Coach button
            _liveCoachButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE768", FontSize = 12 },
                        new TextBlock { Text = "Live Coach", FontSize = 11, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                Background = new SolidColorBrush(Color.FromArgb(255, 46, 125, 50)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center
            };
            _liveCoachButton.Click += (s, e) => McstudDesktop.App.ToggleLiveCoaching();
            Grid.SetColumn(_liveCoachButton, 1);
            titleRow.Children.Add(_liveCoachButton);

            McstudDesktop.Services.LiveCoachingService.Instance.CoachingStateChanged += (s, isActive) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_liveCoachButton?.Content is StackPanel sp && sp.Children.Count >= 2)
                    {
                        var icon = (FontIcon)sp.Children[0];
                        var text = (TextBlock)sp.Children[1];
                        if (isActive)
                        {
                            icon.Glyph = "\uE71A"; // Stop icon
                            text.Text = "Stop Coach";
                            _liveCoachButton.Background = new SolidColorBrush(Color.FromArgb(255, 180, 50, 50));
                        }
                        else
                        {
                            icon.Glyph = "\uE768"; // Play icon
                            text.Text = "Live Coach";
                            _liveCoachButton.Background = new SolidColorBrush(Color.FromArgb(255, 46, 125, 50));
                        }
                    }
                });
            };

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

        // === DETECTED OPERATIONS ===
        private Border CreateOperationsSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 55, 60, 70)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(new TextBlock
            {
                Text = "Detected Operations",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            _operationsPanel = new StackPanel { Spacing = 4 };
            _operationsPanel.Children.Add(new TextBlock
            {
                Text = "No operations detected yet. Capture a screenshot to begin.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });

            stack.Children.Add(_operationsPanel);

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
            stack.Children.Add(_refMatchStatusText);

            // AI pattern analysis panel (populated after OCR)
            _aiAnalysisPanel = new StackPanel
            {
                Spacing = 4,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };
            stack.Children.Add(_aiAnalysisPanel);

            border.Child = stack;
            return border;
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

            border.Child = row;
            return border;
        }

        // === EVENT HANDLERS ===

        private void MonitorToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_monitorToggle!.IsOn)
            {
                _monitorService.Start();
            }
            else
            {
                _monitorService.Stop();
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
            _operationsPanel?.Children.Clear();
            _operationsPanel?.Children.Add(new TextBlock
            {
                Text = "No operations detected yet. Capture a screenshot to begin.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
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
            ("quarter panel", "Quarter Panel"), ("qtr panel", "Quarter Panel"),
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

                // Step 4: SOP missing operations check
                var sopSection = BuildSopMissingSection(rawText);
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
            var lowerText = rawText.ToLowerInvariant();
            var detectedParts = new List<string>();
            var seenParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matchedPatterns = new HashSet<string>();

            foreach (var (pattern, canonical) in _scanParts)
            {
                if (seenParts.Contains(canonical)) continue;
                if (matchedPatterns.Any(mp => mp.Contains(pattern))) continue;

                var idx = lowerText.IndexOf(pattern);
                if (idx < 0) continue;

                // Word boundary check
                if (idx > 0 && char.IsLetter(lowerText[idx - 1])) continue;
                var endIdx = idx + pattern.Length;
                if (endIdx < lowerText.Length && char.IsLetter(lowerText[endIdx])) continue;

                seenParts.Add(canonical);
                detectedParts.Add(canonical);
                matchedPatterns.Add(pattern);
            }

            // Also include any parts from the structured parser
            foreach (var op in result.DetectedOperations)
            {
                if (!string.IsNullOrEmpty(op.PartName) && seenParts.Add(op.PartName))
                    detectedParts.Add(op.PartName);
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

        private UIElement? BuildSopMissingSection(string rawText)
        {
            var lowerText = rawText.ToLowerInvariant();

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
                return;
            }

            // Update operations panel
            _operationsPanel?.Children.Clear();

            if (result.DetectedOperations.Count == 0)
            {
                var noOpsMessage = "No text recognized in the captured image.";
                if (result.LineCount > 0)
                {
                    noOpsMessage = result.SourceWindow == "Full Screen"
                        ? $"Read {result.LineCount} lines but no estimate operations found. Make sure your estimating software (CCC ONE, Mitchell) is open and showing line items."
                        : $"Read {result.LineCount} lines from \"{result.SourceWindow}\" but no estimate operations detected. Navigate to the estimate line items / operations page.";
                }

                _operationsPanel?.Children.Add(new TextBlock
                {
                    Text = noOpsMessage,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 140, 100)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap
                });

                // Show tips for getting it working
                _operationsPanel?.Children.Add(new TextBlock
                {
                    Text = "Tips: Open CCC ONE or Mitchell to the estimate line items page. Make sure the window is not minimized. If using a browser, the window title must contain \"CCC\" or \"Mitchell\".",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0)
                });
            }
            else
            {
                foreach (var op in result.DetectedOperations)
                {
                    _operationsPanel?.Children.Add(CreateOperationRow(op));
                }
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
