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
        private readonly EstimateAIAdvisorService _advisorService;

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

        // State
        private ScreenOcrResult? _latestResult;

        // Events
        public event EventHandler<ScreenOcrResult>? OnFeedToChat;
        public event EventHandler<ScreenOcrResult>? OnLoadToBuilder;

        public ScreenMonitorPanel()
        {
            _monitorService = ScreenMonitorService.Instance;
            _referenceMatcher = EstimateReferenceMatcherService.Instance;
            _advisorService = EstimateAIAdvisorService.Instance;
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

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            titleRow.Children.Add(new FontIcon
            {
                Glyph = "\uE7B3", // Screen/monitor icon
                FontSize = 28,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "Screen OCR Monitor",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
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
            });

            // Auto-match against reference data when changes detected
            if (result.HasChanges && result.DetectedOperations.Count > 0)
            {
                _ = RunReferenceMatchingAsync(result);
            }

            // Auto-compare against learned patterns
            if (result.DetectedOperations.Count > 0)
            {
                DispatcherQueue?.TryEnqueue(() => RunAdvisorAnalysis(result));
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

        private void RunAdvisorAnalysis(ScreenOcrResult result)
        {
            if (_aiAnalysisPanel == null) return;
            _aiAnalysisPanel.Children.Clear();

            try
            {
                // Set context from detected operations
                var opTypes = result.DetectedOperations.Select(o => o.OperationType).ToList();
                foreach (var op in result.DetectedOperations)
                {
                    _advisorService.TrackEnteredOperation(op.PartName, op.OperationType, op.LaborHours);
                }

                var response = _advisorService.ProcessAdvisorQuery("what am I missing");
                if (response == null || response.Sections.Count == 0)
                {
                    _aiAnalysisPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                // Filter out items already in the OCR result
                var existingDescs = result.DetectedOperations
                    .Select(o => o.Description?.ToLowerInvariant() ?? "")
                    .ToHashSet();

                var missingItems = response.Sections
                    .SelectMany(s => s.Items)
                    .Where(item => !existingDescs.Any(d => d.Contains(item.Description?.ToLowerInvariant() ?? "~")))
                    .ToList();

                if (missingItems.Count == 0)
                {
                    _aiAnalysisPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                // Header
                var headerBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 40)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                headerBorder.Child = new TextBlock
                {
                    Text = $"\uD83E\uDDE0 AI Analysis: {missingItems.Count} potentially missing operations",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 140))
                };
                _aiAnalysisPanel.Children.Add(headerBorder);

                // Missing items
                foreach (var item in missingItems.Take(10))
                {
                    var confColor = item.Confidence >= 0.7
                        ? Color.FromArgb(255, 80, 190, 80)
                        : item.Confidence >= 0.4
                            ? Color.FromArgb(255, 220, 180, 60)
                            : Color.FromArgb(255, 220, 130, 60);
                    var confText = item.Confidence >= 0.7 ? "HIGH" : item.Confidence >= 0.4 ? "MED" : "LOW";

                    var row = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 42, 47, 56)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 1, 0, 1)
                    };

                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var desc = new TextBlock
                    {
                        Text = item.Description,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Grid.SetColumn(desc, 0);
                    grid.Children.Add(desc);

                    var badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(40, confColor.R, confColor.G, confColor.B)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    badge.Child = new TextBlock
                    {
                        Text = confText,
                        FontSize = 9,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(confColor)
                    };
                    Grid.SetColumn(badge, 1);
                    grid.Children.Add(badge);

                    if (item.Hours > 0)
                    {
                        var hours = new TextBlock
                        {
                            Text = $"{item.Hours:F1}h",
                            FontSize = 10,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(6, 0, 0, 0)
                        };
                        Grid.SetColumn(hours, 2);
                        grid.Children.Add(hours);
                    }

                    row.Child = grid;
                    _aiAnalysisPanel.Children.Add(row);
                }

                // Summary
                if (!string.IsNullOrWhiteSpace(response.Summary))
                {
                    _aiAnalysisPanel.Children.Add(new TextBlock
                    {
                        Text = response.Summary,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 135, 140)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                }

                _aiAnalysisPanel.Visibility = Visibility.Visible;
            }
            catch
            {
                _aiAnalysisPanel.Visibility = Visibility.Collapsed;
            }
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
                _operationsPanel?.Children.Add(new TextBlock
                {
                    Text = result.LineCount > 0
                        ? $"No operations detected in {result.LineCount} OCR lines. Text may not contain estimate data."
                        : "No text recognized in the captured image.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 140, 100)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap
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
