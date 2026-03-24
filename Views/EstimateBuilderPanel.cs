#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using McStudDesktop.Models;
using McStudDesktop.Services;
using McstudDesktop.Services;

namespace McStudDesktop.Views
{
    public sealed class EstimateBuilderPanel : UserControl
    {
        // Services
        private readonly EstimateLearningService _learningService;
        private readonly EstimateAIAdvisorService _advisorService;
        private readonly TypeItService _typeItService;
        private readonly EstimatePresetService _presetService;

        // Browse column UI
        private AutoSuggestBox? _partNameInput;
        private ComboBox? _operationTypeCombo;
        private Button? _lookupButton;
        private Button? _historyToggleButton;
        private bool _showingHistory = false;
        private string? _currentLookupPart;
        private string? _currentLookupOp;
        private StackPanel? _resultsPanel;
        private TextBlock? _statusText;
        private TextBlock? _selectionCountText;
        private Button? _insertButton;
        private ScrollViewer? _resultsScroller;

        // Stats card refs
        private TextBlock? _statEstimates;
        private TextBlock? _statPatterns;
        private TextBlock? _statUniqueOps;
        private TextBlock? _statTotalHours;
        private TextBlock? _statAvgOps;

        // Current search results
        private ManualLinePattern? _currentPattern;
        private List<ManualLineEntry> _selectedOperations = new();
        private List<OperationSearchResult> _selectedOpResults = new();

        // Cart column UI
        private ListView? _estimateView;
        private TextBlock? _totalLabor;
        private TextBlock? _totalRefinish;
        private TextBlock? _totalPrice;
        private TextBlock? _lineCount;
        private StackPanel? _suggestionsPanel;
        private StackPanel? _presetsPanel;

        // Cart data
        private List<EstimateLine> _estimateLines = new();

        // Mode indicator
        private TextBlock? _modeBadgeText;
        private Border? _modeBadge;

        // Personal imports toggle
        private bool _viewPersonalOnly = false;
        private Button? _personalToggleButton;
        private TextBlock? _personalToggleIcon;
        private LearnedPatternDatabase? _personalViewDb;

        // --- Color Palette ---
        private static readonly Color AccentBlue = Color.FromArgb(255, 56, 132, 244);
        private static readonly Color AccentGreen = Color.FromArgb(255, 40, 167, 96);
        private static readonly Color AccentPurple = Color.FromArgb(255, 140, 100, 210);
        private static readonly Color AccentOrange = Color.FromArgb(255, 230, 160, 50);
        private static readonly Color AccentTeal = Color.FromArgb(255, 50, 180, 170);
        private static readonly Color DarkBg = Color.FromArgb(255, 22, 22, 26);
        private static readonly Color CardBg = Color.FromArgb(255, 32, 32, 36);
        private static readonly Color CardHover = Color.FromArgb(255, 42, 42, 48);
        private static readonly Color SurfaceBg = Color.FromArgb(255, 38, 38, 42);
        private static readonly Color BorderSubtle = Color.FromArgb(255, 52, 52, 58);
        private static readonly Color TextPrimary = Color.FromArgb(255, 240, 240, 245);
        private static readonly Color TextSecondary = Color.FromArgb(255, 155, 155, 165);
        private static readonly Color TextMuted = Color.FromArgb(255, 105, 105, 115);
        private static readonly Color Gold = Color.FromArgb(255, 255, 200, 50);
        private static readonly Color Silver = Color.FromArgb(255, 200, 200, 210);
        private static readonly Color Bronze = Color.FromArgb(255, 210, 140, 70);

        // Public API
        public event EventHandler<List<GeneratedOperation>>? OnOperationsGenerated;

        public EstimateBuilderPanel()
        {
            _learningService = EstimateLearningService.Instance;
            _advisorService = EstimateAIAdvisorService.Instance;
            _typeItService = new TypeItService();
            _typeItService.SetSpeedLevel(4);
            _typeItService.BlockUserInput = true;
            _presetService = EstimatePresetService.Instance;

            // One-time migration: sanitize all existing learned patterns
            _learningService.ResanitizeAllPatterns();

            try
            {
                BuildUI();
                RefreshStatsValues();
                _presetService.GenerateAutoPresets();
                RefreshPresetChips();
                ShowLookupHint();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EstimateBuilderPanel] Constructor error: {ex}");
                // Fallback: show error message
                Content = new TextBlock { Text = $"EstimateBuilder init error: {ex.Message}", Foreground = new SolidColorBrush(Colors.Red), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) };
            }

            LearningModeService.Instance.ModeChanged += OnLearningModeChanged;
        }

        private void OnLearningModeChanged(LearningMode newMode)
        {
            if (_modeBadgeText != null)
                _modeBadgeText.Text = newMode == LearningMode.Shop ? "Standard" : "Personal";
            if (_modeBadge != null)
                _modeBadge.Background = new SolidColorBrush(newMode == LearningMode.Shop
                    ? Color.FromArgb(255, 0, 100, 180)
                    : Color.FromArgb(255, 140, 80, 200));
            RefreshStatsValues();
            if (_currentLookupPart != null) DoLookup();
            else ShowLookupHint();
        }

        private void RefreshStatsValues()
        {
            IReadOnlyList<ManualLinePattern> allPatterns;
            int estimatesImported;
            int patternCount;

            if (_viewPersonalOnly && _personalViewDb != null)
            {
                allPatterns = _personalViewDb.ManualLinePatterns.Values
                    .Where(p => p.ManualLines.Count > 0).ToList();
                estimatesImported = _personalViewDb.EstimatesImported;
                patternCount = _personalViewDb.ManualLinePatterns.Count;
            }
            else
            {
                allPatterns = _learningService.GetAllManualLinePatterns()
                    .Where(p => p.ManualLines.Count > 0).ToList();
                estimatesImported = _learningService.EstimatesImported;
                patternCount = _learningService.ManualLinePatternCount;
            }

            if (_statEstimates != null) _statEstimates.Text = estimatesImported.ToString();
            if (_statPatterns != null) _statPatterns.Text = patternCount.ToString();
            if (_statUniqueOps != null) _statUniqueOps.Text = allPatterns.SelectMany(p => p.ManualLines).Select(m => m.ManualLineType).Distinct().Count().ToString();
            if (_statTotalHours != null) _statTotalHours.Text = $"{allPatterns.SelectMany(p => p.ManualLines).Sum(m => m.LaborUnits + m.RefinishUnits):N1}";
            if (_statAvgOps != null)
            {
                var nonEmptyCount = allPatterns.Count;
                var avgOps = nonEmptyCount > 0 ? (double)allPatterns.Sum(p => p.ManualLines.Count) / nonEmptyCount : 0;
                _statAvgOps.Text = $"{avgOps:N1}";
            }
        }

        // ═══════════════════════════════════════════
        //  BUILD UI - Two Column Layout
        // ═══════════════════════════════════════════

        private void BuildUI()
        {
            var mainGrid = new Grid { Background = new SolidColorBrush(DarkBg) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0 Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1 Stats
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2 Main content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3 Actions

            var header = BuildHeader();
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            var stats = BuildStatsBar();
            Grid.SetRow(stats, 1);
            mainGrid.Children.Add(stats);

            var contentGrid = new Grid { Margin = new Thickness(16, 0, 16, 0), ColumnSpacing = 12 };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var browseColumn = BuildBrowseColumn();
            Grid.SetColumn(browseColumn, 0);
            contentGrid.Children.Add(browseColumn);

            var cartColumn = BuildCartColumn();
            Grid.SetColumn(cartColumn, 1);
            contentGrid.Children.Add(cartColumn);

            Grid.SetRow(contentGrid, 2);
            mainGrid.Children.Add(contentGrid);

            var actions = BuildActionBar();
            Grid.SetRow(actions, 3);
            mainGrid.Children.Add(actions);

            Content = mainGrid;
        }

        // ═══════════════════════════════════════════
        //  HEADER
        // ═══════════════════════════════════════════

        private Border BuildHeader()
        {
            var border = new Border { Padding = new Thickness(20, 16, 20, 10) };
            var stack = new StackPanel { Spacing = 2 };

            // Top row: title + badges on left, toggle on right
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            titleRow.Children.Add(new TextBlock
            {
                Text = "Learned Operations",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            });
            var currentMode = LearningModeService.Instance.CurrentMode;
            _modeBadge = new Border
            {
                Background = new SolidColorBrush(currentMode == LearningMode.Shop
                    ? Color.FromArgb(255, 0, 100, 180) : Color.FromArgb(255, 140, 80, 200)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            _modeBadgeText = new TextBlock
            {
                Text = currentMode == LearningMode.Shop ? "Standard" : "Personal",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White)
            };
            _modeBadge.Child = _modeBadgeText;
            titleRow.Children.Add(_modeBadge);
            Grid.SetColumn(titleRow, 0);
            headerGrid.Children.Add(titleRow);

            // "My Imports" toggle button
            _personalToggleIcon = new TextBlock
            {
                Text = "\uE8AB", // Contact icon
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            var toggleContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            toggleContent.Children.Add(_personalToggleIcon);
            toggleContent.Children.Add(new TextBlock
            {
                Text = "My Imports",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            });
            _personalToggleButton = new Button
            {
                Content = toggleContent,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            _personalToggleButton.Click += OnPersonalToggleClicked;
            Grid.SetColumn(_personalToggleButton, 1);
            headerGrid.Children.Add(_personalToggleButton);

            stack.Children.Add(headerGrid);
            stack.Children.Add(new TextBlock
            {
                Text = "Look up part operations from imported estimates",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextSecondary)
            });
            border.Child = stack;
            return border;
        }

        private void OnPersonalToggleClicked(object sender, RoutedEventArgs e)
        {
            _viewPersonalOnly = !_viewPersonalOnly;

            if (_viewPersonalOnly)
            {
                // Load personal-only database
                _personalViewDb = _learningService.GetPersonalOnlyDatabase();
                _personalToggleButton!.Background = new SolidColorBrush(Color.FromArgb(255, 140, 80, 200));
                _personalToggleButton.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 160, 100, 220));
                _personalToggleIcon!.Foreground = new SolidColorBrush(Colors.White);
                ((StackPanel)_personalToggleButton.Content).Children.OfType<TextBlock>().Last().Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                _personalViewDb = null;
                _personalToggleButton!.Background = new SolidColorBrush(Colors.Transparent);
                _personalToggleButton.BorderBrush = new SolidColorBrush(BorderSubtle);
                _personalToggleIcon!.Foreground = new SolidColorBrush(TextSecondary);
                ((StackPanel)_personalToggleButton.Content).Children.OfType<TextBlock>().Last().Foreground = new SolidColorBrush(TextSecondary);
            }

            RefreshStatsValues();
            if (_currentLookupPart != null) DoLookup();
            else ShowLookupHint();
        }

        // ═══════════════════════════════════════════
        //  COMPACT STATS BAR
        // ═══════════════════════════════════════════

        private Border BuildStatsBar()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(SurfaceBg),
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(16, 0, 16, 8),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1)
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
            var allPatterns = _learningService.GetAllManualLinePatterns().Where(p => p.ManualLines.Count > 0).ToList();
            var estimates = _learningService.EstimatesImported;
            var patterns = _learningService.ManualLinePatternCount;
            var uniqueOps = allPatterns.SelectMany(p => p.ManualLines).Select(m => m.ManualLineType).Distinct().Count();
            var totalHours = allPatterns.SelectMany(p => p.ManualLines).Sum(m => m.LaborUnits + m.RefinishUnits);
            var avgOps = patterns > 0 ? (double)allPatterns.Sum(p => p.ManualLines.Count) / patterns : 0;
            row.Children.Add(BuildStatItem(AccentBlue, estimates.ToString(), "Estimates", out _statEstimates));
            row.Children.Add(BuildStatItem(AccentGreen, patterns.ToString(), "Patterns", out _statPatterns));
            row.Children.Add(BuildStatItem(AccentPurple, uniqueOps.ToString(), "Unique Ops", out _statUniqueOps));
            row.Children.Add(BuildStatItem(AccentOrange, $"{totalHours:N1}", "Total Hours", out _statTotalHours));
            row.Children.Add(BuildStatItem(AccentTeal, $"{avgOps:N1}", "Avg Ops/Part", out _statAvgOps));
            border.Child = row;
            return border;
        }

        private StackPanel BuildStatItem(Color dotColor, string value, string label, out TextBlock valueRef)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(dotColor), VerticalAlignment = VerticalAlignment.Center });
            valueRef = new TextBlock { Text = value, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(TextPrimary), VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(valueRef);
            item.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(TextMuted), VerticalAlignment = VerticalAlignment.Center });
            return item;
        }

        // ═══════════════════════════════════════════
        //  BROWSE COLUMN (Left)
        // ═══════════════════════════════════════════

        private Border BuildBrowseColumn()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 32)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(0),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1)
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // lookup inputs
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // results

            var lookup = BuildLookupInputSection();
            Grid.SetRow(lookup, 0);
            grid.Children.Add(lookup);

            _resultsScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 0, 16, 0)
            };
            _resultsPanel = new StackPanel { Spacing = 8 };
            _resultsScroller.Content = _resultsPanel;
            Grid.SetRow(_resultsScroller, 1);
            grid.Children.Add(_resultsScroller);

            border.Child = grid;
            return border;
        }

        // ═══════════════════════════════════════════
        //  CART COLUMN (Right)
        // ═══════════════════════════════════════════

        private Border BuildCartColumn()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 32)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1)
            };
            var stack = new StackPanel { Spacing = 8 };

            // Presets row
            stack.Children.Add(BuildPresetsRow());

            // Header
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.Children.Add(new TextBlock
            {
                Text = "Your Estimate",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentGreen)
            });
            _lineCount = new TextBlock
            {
                Text = "0 lines", FontSize = 11,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_lineCount, 1);
            headerRow.Children.Add(_lineCount);
            stack.Children.Add(headerRow);

            // Estimate list
            _estimateView = new ListView
            {
                SelectionMode = ListViewSelectionMode.None,
                Background = new SolidColorBrush(Colors.Transparent),
                MaxHeight = 300
            };
            stack.Children.Add(_estimateView);

            // Totals
            var totalsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 8, 0, 0) };
            _totalLabor = new TextBlock
            {
                Text = "Labor: 0.0", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100))
            };
            totalsPanel.Children.Add(_totalLabor);
            _totalRefinish = new TextBlock
            {
                Text = "Refinish: 0.0", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 255))
            };
            totalsPanel.Children.Add(_totalRefinish);
            _totalPrice = new TextBlock
            {
                Text = "$0", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentGreen)
            };
            totalsPanel.Children.Add(_totalPrice);
            stack.Children.Add(totalsPanel);

            // Unified suggestions
            _suggestionsPanel = new StackPanel { Orientation = Orientation.Vertical, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            stack.Children.Add(_suggestionsPanel);

            border.Child = stack;
            return border;
        }

        // ═══════════════════════════════════════════
        //  LOOKUP INPUT SECTION
        // ═══════════════════════════════════════════

        private Border BuildLookupInputSection()
        {
            var border = new Border { Padding = new Thickness(16, 12, 16, 10) };
            var outer = new StackPanel { Spacing = 8 };

            // Part name row
            var partLabel = new TextBlock { Text = "Part:", FontSize = 12, Foreground = new SolidColorBrush(TextSecondary), Margin = new Thickness(2, 0, 0, 2) };
            outer.Children.Add(partLabel);
            _partNameInput = new AutoSuggestBox
            {
                PlaceholderText = "Part or operation (e.g. Fender, scan, ADAS...)",
                FontSize = 13,
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(TextPrimary),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                CornerRadius = new CornerRadius(8)
            };
            _partNameInput.TextChanged += (s, e) =>
            {
                if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                    s.ItemsSource = GetKnownPartNames(s.Text);
            };
            _partNameInput.QuerySubmitted += (s, e) =>
            {
                if (e.ChosenSuggestion != null)
                    _partNameInput.Text = e.ChosenSuggestion.ToString();
                DoLookup();
            };
            outer.Children.Add(_partNameInput);

            // Operation + buttons row
            var opRow = new Grid { ColumnSpacing = 8 };
            opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _operationTypeCombo = new ComboBox
            {
                FontSize = 13,
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(TextPrimary),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new[] { "(Any)", "Replace", "Repair", "Blend", "Refinish", "R&I" },
                SelectedIndex = 0
            };
            Grid.SetColumn(_operationTypeCombo, 0);
            opRow.Children.Add(_operationTypeCombo);

            _lookupButton = new Button
            {
                Padding = new Thickness(14, 8, 14, 8),
                Background = new SolidColorBrush(AccentBlue),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(8)
            };
            var lookupContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            lookupContent.Children.Add(new FontIcon { Glyph = "\uE721", FontSize = 14 });
            lookupContent.Children.Add(new TextBlock { Text = "Lookup", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            _lookupButton.Content = lookupContent;
            _lookupButton.Click += (s, e) => DoLookup();
            Grid.SetColumn(_lookupButton, 1);
            opRow.Children.Add(_lookupButton);

            _historyToggleButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE81C", FontSize = 14 },
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55)),
                Foreground = new SolidColorBrush(TextSecondary),
                CornerRadius = new CornerRadius(8)
            };
            ToolTipService.SetToolTip(_historyToggleButton, "Toggle estimate history");
            _historyToggleButton.Click += (s, e) =>
            {
                _showingHistory = !_showingHistory;
                _historyToggleButton.Background = new SolidColorBrush(_showingHistory ? Color.FromArgb(255, 30, 60, 80) : Color.FromArgb(255, 50, 50, 55));
                _historyToggleButton.Foreground = new SolidColorBrush(_showingHistory ? AccentTeal : TextSecondary);
                if (_showingHistory)
                {
                    _resultsPanel!.Children.Clear();
                    ShowHistoryView();
                    UpdateButtonStates();
                }
                else
                {
                    if (_currentLookupPart != null) DoLookup();
                    else ShowLookupHint();
                }
            };
            Grid.SetColumn(_historyToggleButton, 2);
            opRow.Children.Add(_historyToggleButton);

            outer.Children.Add(opRow);
            border.Child = outer;
            return border;
        }

        // ═══════════════════════════════════════════
        //  ACTION BAR (merged)
        // ═══════════════════════════════════════════

        private Border BuildActionBar()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(SurfaceBg), Padding = new Thickness(16, 12, 16, 14),
                Margin = new Thickness(16, 8, 16, 14), CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            _selectionCountText = new TextBlock { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary), Visibility = Visibility.Collapsed };
            leftStack.Children.Add(_selectionCountText);
            _statusText = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(TextSecondary), TextWrapping = TextWrapping.Wrap, Text = "Look up a part and operation to build your estimate" };
            leftStack.Children.Add(_statusText);
            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

            var clearBtn = new Button { Padding = new Thickness(12, 8, 12, 8), Background = new SolidColorBrush(Color.FromArgb(255, 80, 40, 40)), Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(8) };
            clearBtn.Content = new TextBlock { Text = "Clear", FontSize = 12 };
            clearBtn.Click += ClearAll_Click;
            btnPanel.Children.Add(clearBtn);

            // Export dropdown: primary action = Insert CCC, flyout has the rest
            var exportFlyout = new MenuFlyout();
            var copyItem = new MenuFlyoutItem { Text = "Copy to Clipboard" };
            copyItem.Click += CopyButton_Click;
            exportFlyout.Items.Add(copyItem);
            var clipItem = new MenuFlyoutItem { Text = "Clip It" };
            clipItem.Click += ClipIt_Click;
            exportFlyout.Items.Add(clipItem);
            var typeItem = new MenuFlyoutItem { Text = "Type It" };
            typeItem.Click += TypeIt_Click;
            exportFlyout.Items.Add(typeItem);
            exportFlyout.Items.Add(new MenuFlyoutSeparator());
            var sendItem = new MenuFlyoutItem { Text = "Send to Chat" };
            sendItem.Click += SendToChat_Click;
            exportFlyout.Items.Add(sendItem);

            _insertButton = new DropDownButton
            {
                Padding = new Thickness(14, 8, 14, 8),
                Background = new SolidColorBrush(AccentGreen),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(8),
                IsEnabled = false,
                Flyout = exportFlyout,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE768", FontSize = 14 },
                        new TextBlock { Text = "Insert CCC", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                    }
                }
            };
            _insertButton.Click += InsertButton_Click;
            btnPanel.Children.Add(_insertButton);

            Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(btnPanel);
            border.Child = grid;
            return border;
        }

        // ═══════════════════════════════════════════
        //  LOOKUP LOGIC
        // ═══════════════════════════════════════════

        private void ShowLookupHint()
        {
            _resultsPanel!.Children.Clear();
            _selectedOperations.Clear();
            _selectedOpResults.Clear();
            _currentLookupPart = null;
            _currentLookupOp = null;
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(40, 48, 40, 48), Margin = new Thickness(0, 40, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1)
            };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 16 };
            var iconCircle = new Border { Width = 80, Height = 80, CornerRadius = new CornerRadius(40), Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 50)), HorizontalAlignment = HorizontalAlignment.Center };
            iconCircle.Child = new FontIcon { Glyph = "\uE721", FontSize = 36, Foreground = new SolidColorBrush(TextMuted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(iconCircle);
            stack.Children.Add(new TextBlock { Text = "Look Up Operations", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = "Enter a part name and operation type above\nto see learned data from imported estimates.", FontSize = 13, Foreground = new SolidColorBrush(TextSecondary), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
            card.Child = stack;
            _resultsPanel.Children.Add(card);
            UpdateButtonStates();
        }

        private void DoLookup()
        {
            var partName = _partNameInput?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(partName))
            {
                ShowLookupHint();
                return;
            }

            var opSelection = _operationTypeCombo?.SelectedItem?.ToString();
            string? opType = opSelection == "(Any)" || opSelection == null ? null : opSelection.ToLowerInvariant();

            _currentLookupPart = partName;
            _currentLookupOp = opType;
            _resultsPanel!.Children.Clear();
            _selectedOperations.Clear();
            _selectedOpResults.Clear();

            // Get the right pattern source depending on toggle
            IReadOnlyList<ManualLinePattern> sourcePatterns;
            if (_viewPersonalOnly && _personalViewDb != null)
                sourcePatterns = _personalViewDb.ManualLinePatterns.Values.ToList();
            else
                sourcePatterns = _learningService.GetAllManualLinePatterns();

            // Try exact/partial pattern match
            ManualLinePattern? pattern = null;
            if (_viewPersonalOnly && _personalViewDb != null)
            {
                // Search personal DB directly
                pattern = FindPatternInDb(_personalViewDb.ManualLinePatterns, partName, opType);
            }
            else
            {
                pattern = _learningService.GetManualLinesForPart(partName, opType);
            }

            if (pattern != null && pattern.ManualLines.Count > 0
                && (pattern.ParentPartName ?? "").Contains(partName, StringComparison.OrdinalIgnoreCase))
            {
                ShowLookupResults(pattern);
                return;
            }

            // Broader search: find all patterns whose part name contains the search term
            // Also match side-neutral: "RT Fender" should find "LT Fender" patterns
            var sideNeutralSearch = System.Text.RegularExpressions.Regex.Replace(
                partName, @"\b(LT|RT|LH|RH|Left|Right)\b\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            var allPatterns = sourcePatterns
                .Where(p => p.ManualLines.Count > 0
                    && !string.IsNullOrEmpty(p.ParentPartName)
                    && (p.ParentPartName.Contains(partName, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty(sideNeutralSearch)
                            && System.Text.RegularExpressions.Regex.Replace(
                                p.ParentPartName, @"\b(LT|RT|LH|RH|Left|Right)\b\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                                .Trim()
                                .Contains(sideNeutralSearch, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(p => p.ExampleCount)
                .Take(15)
                .ToList();

            if (allPatterns.Count == 1)
            {
                // Single match — show it directly
                ShowLookupResults(allPatterns[0]);
                return;
            }
            if (allPatterns.Count > 1)
            {
                _statusText!.Text = $"Found {allPatterns.Count} patterns matching \"{partName}\"";
                _statusText.Foreground = new SolidColorBrush(AccentBlue);
                AddSectionHeader("SELECT A PATTERN");
                foreach (var p in allPatterns)
                    _resultsPanel.Children.Add(CreatePatternCard(p));
                UpdateButtonStates();
                return;
            }

            // Strategy 3: Reverse search — search across manual line descriptions
            // This handles cases where user types an operation name like "scan", "ADAS", "corrosion", etc.
            var opResults = SearchManualLineDescriptions(partName, sourcePatterns);
            if (opResults.Count > 0)
            {
                _statusText!.Text = $"Found {opResults.Count} operations matching \"{partName}\"";
                _statusText.Foreground = new SolidColorBrush(AccentTeal);
                AddSectionHeader("MATCHING OPERATIONS");

                // Group by description so we show unique operations with their parent parts
                var grouped = opResults
                    .GroupBy(r => (r.Line.ManualLineType.Length > 0 ? r.Line.ManualLineType : r.Line.Description).ToLowerInvariant())
                    .OrderByDescending(g => g.Sum(r => r.Line.TimesUsed))
                    .Take(20);

                bool alt = false;
                foreach (var group in grouped)
                {
                    var best = group.OrderByDescending(r => r.Line.TimesUsed).First();
                    var parentParts = group.Select(r => r.SourcePattern.ParentPartName ?? "").Distinct().Take(3);
                    var parentInfo = string.Join(", ", parentParts);
                    _resultsPanel.Children.Add(CreateOpSearchResultRow(best.Line, best.SourcePattern, parentInfo, alt));
                    alt = !alt;
                }

                UpdateButtonStates();
                return;
            }

            // Nothing found
            var source = _viewPersonalOnly ? "your imports" : "learned data";
            _statusText!.Text = $"No results for \"{partName}\" in {source}";
            _statusText.Foreground = new SolidColorBrush(AccentOrange);
            ShowEmptyState();
            UpdateButtonStates();
        }

        /// <summary>
        /// Search across all manual line descriptions for a query term.
        /// Returns matching lines with their parent pattern context.
        /// </summary>
        private List<(ManualLineEntry Line, ManualLinePattern SourcePattern)> SearchManualLineDescriptions(
            string query, IReadOnlyList<ManualLinePattern> patterns)
        {
            var queryLower = query.ToLowerInvariant().Trim();
            if (queryLower.Length < 2) return new();

            // Expand common shorthand/aliases
            var searchTerms = new List<string> { queryLower };
            var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["scan"] = new[] { "scan", "diagnostic", "pre-scan", "post-scan", "pre scan", "post scan" },
                ["adas"] = new[] { "adas", "calibrat", "recalibrat", "camera", "sensor aim" },
                ["corrosion"] = new[] { "corrosion", "anti-corrosion", "cavity wax", "rust" },
                ["blend"] = new[] { "blend" },
                ["seam"] = new[] { "seam seal", "seam sealer" },
                ["weld"] = new[] { "weld", "weld-thru", "weld thru" },
                ["prime"] = new[] { "primer", "weld-thru primer", "weld thru primer" },
                ["mask"] = new[] { "mask", "cover", "tape" },
                ["cover"] = new[] { "cover", "mask" },
                ["r&i"] = new[] { "r&i", "remove and install", "remove & install", "r+i" },
                ["refinish"] = new[] { "refinish", "paint", "clear coat", "clearcoat", "basecoat", "base coat" },
            };

            if (aliases.TryGetValue(queryLower, out var expanded))
                searchTerms = expanded.ToList();

            var results = new List<(ManualLineEntry Line, ManualLinePattern SourcePattern)>();
            var seenDescs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in patterns)
            {
                if (pattern.ManualLines.Count == 0) continue;
                foreach (var line in pattern.ManualLines)
                {
                    var desc = line.ManualLineType.Length > 0 ? line.ManualLineType : line.Description;
                    if (string.IsNullOrEmpty(desc)) continue;
                    var descLower = desc.ToLowerInvariant();

                    if (searchTerms.Any(t => descLower.Contains(t)) && seenDescs.Add(desc))
                        results.Add((line, pattern));
                }
            }

            return results.OrderByDescending(r => r.Line.TimesUsed).ToList();
        }

        /// <summary>
        /// Create a UI row for an operation search result (reverse search by description).
        /// </summary>
        private Grid CreateOpSearchResultRow(ManualLineEntry line, ManualLinePattern sourcePattern, string parentPartsInfo, bool alternate)
        {
            var row = new Grid
            {
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(alternate ? SurfaceBg : CardBg),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 1, 0, 1)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

            var nameStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var desc = line.ManualLineType.Length > 0 ? line.ManualLineType : line.Description;
            nameStack.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = $"Found in: {parentPartsInfo}",
                FontSize = 10,
                Foreground = new SolidColorBrush(TextMuted),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(nameStack, 0);
            row.Children.Add(nameStack);

            var laborText = new TextBlock
            {
                Text = line.LaborUnits > 0 ? $"{line.LaborUnits:N1}" : "-",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 200, 140)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(laborText, 1);
            row.Children.Add(laborText);

            var refinishText = new TextBlock
            {
                Text = line.RefinishUnits > 0 ? $"{line.RefinishUnits:N1}" : "-",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 170, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(refinishText, 2);
            row.Children.Add(refinishText);

            var usedPill = CreatePill($"{line.TimesUsed}x", AccentPurple, Color.FromArgb(255, 50, 35, 70));
            usedPill.HorizontalAlignment = HorizontalAlignment.Center;
            usedPill.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(usedPill, 3);
            row.Children.Add(usedPill);

            // Click to navigate to the parent pattern
            row.PointerPressed += (s, e) =>
            {
                if (_partNameInput != null)
                    _partNameInput.Text = sourcePattern.ParentPartName ?? "";
                _currentPattern = sourcePattern;
                ShowLookupResults(sourcePattern);
            };
            row.PointerEntered += (s, e) => row.Background = new SolidColorBrush(CardHover);
            row.PointerExited += (s, e) => row.Background = new SolidColorBrush(alternate ? SurfaceBg : CardBg);

            return row;
        }

        /// <summary>
        /// Search for a pattern by part name in a specific ManualLinePatterns dictionary.
        /// Simplified version of the service's GetManualLinesForPart for personal DB queries.
        /// </summary>
        private ManualLinePattern? FindPatternInDb(Dictionary<string, ManualLinePattern> patterns, string partName, string? opType)
        {
            // Normalize to match GenerateManualLinePatternKey format: spaces → underscores, lowered
            var normalizedPart = partName.ToLowerInvariant().Trim().Replace(" ", "_");
            var normalizedOp = (opType ?? "any").ToLowerInvariant().Trim().Replace(" ", "_");

            // Try exact key match
            var key = $"{normalizedPart}|{normalizedOp}";
            if (patterns.TryGetValue(key, out var match)) return match;

            key = $"{normalizedPart}|";
            if (patterns.TryGetValue(key, out match)) return match;

            if (opType != null)
            {
                key = $"{normalizedPart}|any";
                if (patterns.TryGetValue(key, out match)) return match;
            }

            // Partial name match — filter by operation type when specified
            ManualLinePattern? bestPartial = null;
            foreach (var kvp in patterns)
            {
                if (kvp.Value.ManualLines.Count == 0) continue;
                var pp = kvp.Value.ParentPartName ?? "";
                if (!pp.Contains(partName, StringComparison.OrdinalIgnoreCase)) continue;

                // If opType specified, prefer patterns with matching operation type
                if (opType != null && !string.IsNullOrEmpty(kvp.Value.ParentOperationType) &&
                    kvp.Value.ParentOperationType.Equals(opType, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;

                bestPartial ??= kvp.Value;
            }
            return bestPartial;
        }

        private void ShowLookupResults(ManualLinePattern pattern)
        {
            _resultsPanel!.Children.Clear();
            _selectedOperations.Clear();
            _currentPattern = pattern;
            var exampleCount = pattern.ExampleCount;
            var totalHours = pattern.ManualLines.Sum(m => m.LaborUnits + m.RefinishUnits);

            _statusText!.Text = $"Found {pattern.ManualLines.Count} operations for {pattern.ParentPartName} {pattern.ParentOperationType}";
            _statusText.Foreground = new SolidColorBrush(AccentGreen);

            // Title
            _resultsPanel.Children.Add(new TextBlock
            {
                Text = $"{pattern.ParentPartName} \u2014 {pattern.ParentOperationType}",
                FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(TextPrimary),
                Margin = new Thickness(0, 8, 0, 4)
            });
            _resultsPanel.Children.Add(new TextBlock
            {
                Text = $"from {exampleCount} estimate{(exampleCount != 1 ? "s" : "")}",
                FontSize = 12, Foreground = new SolidColorBrush(TextSecondary),
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Summary stat cards
            var summaryGrid = new Grid { Margin = new Thickness(0, 0, 0, 12), ColumnSpacing = 10 };
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var s1 = CreateDetailStatCard("Estimates", exampleCount.ToString(), AccentBlue, Color.FromArgb(255, 25, 40, 65));
            var s2 = CreateDetailStatCard("Total Hours", totalHours.ToString("N1"), AccentGreen, Color.FromArgb(255, 20, 50, 35));
            var s3 = CreateDetailStatCard("Avg/Estimate", (totalHours / Math.Max(1, exampleCount)).ToString("N1"), AccentOrange, Color.FromArgb(255, 55, 40, 20));
            Grid.SetColumn(s1, 0); Grid.SetColumn(s2, 1); Grid.SetColumn(s3, 2);
            summaryGrid.Children.Add(s1); summaryGrid.Children.Add(s2); summaryGrid.Children.Add(s3);
            _resultsPanel.Children.Add(summaryGrid);

            // Operations header with Add All button
            var opsHeaderRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            opsHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opsHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opsHeaderRow.Children.Add(new TextBlock { Text = $"OPERATIONS ({pattern.ManualLines.Count})", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextMuted), CharacterSpacing = 60, VerticalAlignment = VerticalAlignment.Center });
            var addAllBtn = new Button
            {
                FontSize = 11, Padding = new Thickness(12, 5, 12, 5),
                Background = new SolidColorBrush(AccentGreen), Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(6)
            };
            var addAllContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            addAllContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 11 });
            addAllContent.Children.Add(new TextBlock { Text = "Add All", FontSize = 11 });
            addAllBtn.Content = addAllContent;
            addAllBtn.Click += (s, e) => { AddPatternToCart(pattern); };
            Grid.SetColumn(addAllBtn, 1);
            opsHeaderRow.Children.Add(addAllBtn);
            _resultsPanel.Children.Add(opsHeaderRow);

            // Column headers
            var headerRow = new Grid { Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(4, 0, 4, 0) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            var hdrBrush = new SolidColorBrush(TextMuted);
            AddHeaderText(headerRow, "", 0, hdrBrush); AddHeaderText(headerRow, "Operation", 1, hdrBrush);
            AddHeaderText(headerRow, "Labor", 2, hdrBrush); AddHeaderText(headerRow, "Refn", 3, hdrBrush); AddHeaderText(headerRow, "Freq", 4, hdrBrush);
            _resultsPanel.Children.Add(headerRow);
            _resultsPanel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(BorderSubtle), Margin = new Thickness(0, 2, 0, 6) });

            // Operation rows
            var checkboxes = new List<CheckBox>();
            int rowIndex = 0;
            foreach (var manualLine in pattern.ManualLines.OrderByDescending(m => m.TimesUsed))
            {
                var row = CreateOperationRow(manualLine, exampleCount, rowIndex % 2 == 1, out var cb);
                checkboxes.Add(cb);
                _resultsPanel.Children.Add(row);
                rowIndex++;
            }

            // Select/Deselect/Add Selected buttons
            var togglePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 8, 0, 8) };
            var selectAllBtn = new Button { Content = "Select All", FontSize = 11, Padding = new Thickness(12, 5, 12, 5), Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55)), Foreground = new SolidColorBrush(TextPrimary), CornerRadius = new CornerRadius(6) };
            var deselectAllBtn = new Button { Content = "Deselect All", FontSize = 11, Padding = new Thickness(12, 5, 12, 5), Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55)), Foreground = new SolidColorBrush(TextSecondary), CornerRadius = new CornerRadius(6) };
            var addSelectedBtn = new Button { FontSize = 11, Padding = new Thickness(14, 5, 14, 5), Background = new SolidColorBrush(AccentGreen), Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(6) };
            var addSelContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            addSelContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 11 });
            addSelContent.Children.Add(new TextBlock { Text = "Add Selected", FontSize = 11 });
            addSelectedBtn.Content = addSelContent;
            addSelectedBtn.Click += (s, e) => { AddSelectedToCart(pattern); };
            selectAllBtn.Click += (s, e) => { foreach (var cb in checkboxes) cb.IsChecked = true; };
            deselectAllBtn.Click += (s, e) => { foreach (var cb in checkboxes) cb.IsChecked = false; };
            togglePanel.Children.Add(selectAllBtn);
            togglePanel.Children.Add(deselectAllBtn);
            togglePanel.Children.Add(addSelectedBtn);
            _resultsPanel.Children.Add(togglePanel);

            // Related operations
            try
            {
                var related = _learningService.GetRelatedOperations(pattern.ParentPartName ?? "", pattern.ParentOperationType ?? "", 8);
                if (related.Count > 0)
                {
                    AddSectionHeader($"RELATED OPERATIONS ({related.Count})");
                    foreach (var entry in related)
                        _resultsPanel.Children.Add(CreateRelatedOperationCard(entry));
                }
            }
            catch { /* ignore */ }

            UpdateButtonStates();
        }

        private Border CreateRelatedOperationCard(CoOccurrenceEntry entry)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4),
                BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1)
            };
            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var accentBar = new Border { Background = new SolidColorBrush(AccentTeal), CornerRadius = new CornerRadius(8, 0, 0, 8) };
            Grid.SetColumn(accentBar, 0); innerGrid.Children.Add(accentBar);
            var contentGrid = new Grid { Padding = new Thickness(14, 10, 14, 10) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var infoStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock { Text = $"{entry.PartName} {entry.OperationType}", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary), TextTrimming = TextTrimming.CharacterEllipsis });
            var detailParts = new List<string>();
            if (entry.AvgLaborHours > 0) detailParts.Add($"{entry.AvgLaborHours:N1}h labor");
            if (entry.AvgRefinishHours > 0) detailParts.Add($"{entry.AvgRefinishHours:N1}h refn");
            if (detailParts.Count > 0)
                infoStack.Children.Add(new TextBlock { Text = string.Join("  \u2022  ", detailParts), FontSize = 11, Foreground = new SolidColorBrush(TextSecondary) });
            Grid.SetColumn(infoStack, 0); contentGrid.Children.Add(infoStack);
            var coRate = entry.CoOccurrenceRate;
            var rateColor = coRate >= 0.7 ? AccentGreen : coRate >= 0.4 ? AccentOrange : TextMuted;
            var rateBg = coRate >= 0.7 ? Color.FromArgb(255, 25, 60, 40) : coRate >= 0.4 ? Color.FromArgb(255, 55, 40, 20) : Color.FromArgb(255, 45, 45, 50);
            var ratePill = CreatePill($"{coRate:P0}", rateColor, rateBg);
            ratePill.Margin = new Thickness(8, 0, 0, 0); ratePill.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ratePill, 1); contentGrid.Children.Add(ratePill);
            if (entry.AvgLaborHours > 0 || entry.AvgRefinishHours > 0)
            {
                var hours = entry.AvgLaborHours + entry.AvgRefinishHours;
                var hoursPill = CreatePill($"{hours:N1}h", AccentGreen, Color.FromArgb(255, 25, 60, 40));
                hoursPill.Margin = new Thickness(6, 0, 0, 0); hoursPill.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(hoursPill, 2); contentGrid.Children.Add(hoursPill);
            }
            var addBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE710", FontSize = 12 },
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 80, 50)),
                Foreground = new SolidColorBrush(AccentGreen),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var capturedEntry = entry;
            addBtn.Click += (s, e) =>
            {
                _estimateLines.Add(new EstimateLine
                {
                    PartName = capturedEntry.PartName,
                    OperationType = capturedEntry.OperationType,
                    LaborHours = capturedEntry.AvgLaborHours,
                    RefinishHours = capturedEntry.AvgRefinishHours,
                    Price = capturedEntry.AvgPrice
                });
                UpdateEstimateDisplay();
                _statusText!.Text = $"Added {capturedEntry.PartName} {capturedEntry.OperationType}";
                _statusText.Foreground = new SolidColorBrush(AccentGreen);
            };
            Grid.SetColumn(addBtn, 3); contentGrid.Children.Add(addBtn);
            Grid.SetColumn(contentGrid, 1); innerGrid.Children.Add(contentGrid);
            card.Child = innerGrid;
            card.PointerEntered += (s, e) => card.Background = new SolidColorBrush(CardHover);
            card.PointerExited += (s, e) => card.Background = new SolidColorBrush(CardBg);
            return card;
        }

        private List<string> GetKnownPartNames(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return new List<string>();
            IEnumerable<ManualLinePattern> allPatterns;
            if (_viewPersonalOnly && _personalViewDb != null)
                allPatterns = _personalViewDb.ManualLinePatterns.Values;
            else
                allPatterns = _learningService.GetAllManualLinePatterns();

            var patternList = allPatterns.ToList();

            // Part name matches (existing)
            var partNames = patternList
                .Where(p => !string.IsNullOrEmpty(p.ParentPartName))
                .Select(p => p.ParentPartName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => n.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n)
                .Take(10)
                .ToList();

            // Also search manual line descriptions — so typing "scan" suggests "Diagnostic Scan"
            var opNames = patternList
                .SelectMany(p => p.ManualLines)
                .Select(m => m.ManualLineType.Length > 0 ? m.ManualLineType : m.Description)
                .Where(d => !string.IsNullOrEmpty(d) && d.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(d => !partNames.Any(p => p.Equals(d, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => d)
                .Take(5)
                .ToList();

            partNames.AddRange(opNames);
            return partNames.Take(15).ToList();
        }

        private void ShowEmptyState()
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(40, 48, 40, 48), Margin = new Thickness(0, 40, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1)
            };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 16 };
            var iconCircle = new Border { Width = 80, Height = 80, CornerRadius = new CornerRadius(40), Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 50)), HorizontalAlignment = HorizontalAlignment.Center };
            iconCircle.Child = new FontIcon { Glyph = "\uE8B7", FontSize = 36, Foreground = new SolidColorBrush(TextMuted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(iconCircle);
            stack.Children.Add(new TextBlock { Text = "No Patterns Found", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = "No matching patterns found. Upload estimates\nin the Import tab to build your pattern library.", FontSize = 13, Foreground = new SolidColorBrush(TextSecondary), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
            card.Child = stack;
            _resultsPanel!.Children.Add(card);
        }

        // ═══════════════════════════════════════════
        //  PATTERN CARD (with + button)
        // ═══════════════════════════════════════════

        private Border CreatePatternCard(ManualLinePattern pattern)
        {
            var card = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 2), BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1) };
            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var accentBar = new Border { Background = new SolidColorBrush(AccentBlue), CornerRadius = new CornerRadius(8, 0, 0, 8) };
            Grid.SetColumn(accentBar, 0);
            innerGrid.Children.Add(accentBar);
            var contentGrid = new Grid { Padding = new Thickness(14, 12, 14, 12) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var infoStack = new StackPanel { Spacing = 3 };
            infoStack.Children.Add(new TextBlock { Text = $"{pattern.ParentPartName} - {pattern.ParentOperationType}", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary) });
            infoStack.Children.Add(new TextBlock { Text = $"{pattern.ManualLines.Count} operations  \u2022  {pattern.ExampleCount} estimates", FontSize = 11, Foreground = new SolidColorBrush(TextSecondary) });
            Grid.SetColumn(infoStack, 0);
            contentGrid.Children.Add(infoStack);
            var totalHours = pattern.ManualLines.Sum(m => m.LaborUnits + m.RefinishUnits);
            var hoursPill = CreatePill($"{totalHours:N1}h", AccentGreen, Color.FromArgb(255, 25, 60, 40));
            hoursPill.Margin = new Thickness(10, 0, 0, 0); hoursPill.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hoursPill, 1);
            contentGrid.Children.Add(hoursPill);
            var usagePill = CreatePill($"{pattern.ExampleCount}x", AccentPurple, Color.FromArgb(255, 50, 35, 70));
            usagePill.Margin = new Thickness(6, 0, 0, 0); usagePill.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(usagePill, 2);
            contentGrid.Children.Add(usagePill);
            // + button to add all ops to cart
            var addBtn = new Button { Content = new FontIcon { Glyph = "\uE710", FontSize = 12 }, Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromArgb(255, 30, 80, 50)), Foreground = new SolidColorBrush(AccentGreen), CornerRadius = new CornerRadius(6), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            addBtn.Click += (s, e) => { AddPatternToCart(pattern); };
            Grid.SetColumn(addBtn, 3);
            contentGrid.Children.Add(addBtn);
            var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 14, Foreground = new SolidColorBrush(TextMuted), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chevron, 4);
            contentGrid.Children.Add(chevron);
            Grid.SetColumn(contentGrid, 1);
            innerGrid.Children.Add(contentGrid);
            card.Child = innerGrid;
            card.PointerPressed += (s, e) => { if (_partNameInput != null) _partNameInput.Text = pattern.ParentPartName ?? ""; _currentPattern = pattern; ShowLookupResults(pattern); };
            card.PointerEntered += (s, e) => { card.Background = new SolidColorBrush(CardHover); chevron.Foreground = new SolidColorBrush(AccentBlue); };
            card.PointerExited += (s, e) => { card.Background = new SolidColorBrush(CardBg); chevron.Foreground = new SolidColorBrush(TextMuted); };
            return card;
        }



        private Grid CreateOperationRow(ManualLineEntry manualLine, int totalExamples, bool alternateRow, out CheckBox checkbox)
        {
            var row = new Grid { Padding = new Thickness(8), Background = new SolidColorBrush(alternateRow ? SurfaceBg : CardBg), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            checkbox = new CheckBox { IsChecked = true, Margin = new Thickness(4, 0, 0, 0), MinWidth = 20, VerticalAlignment = VerticalAlignment.Center };
            checkbox.Tag = manualLine;
            checkbox.Checked += (s, e) => { _selectedOperations.Add(manualLine); UpdateButtonStates(); };
            checkbox.Unchecked += (s, e) => { _selectedOperations.Remove(manualLine); UpdateButtonStates(); };
            _selectedOperations.Add(manualLine);
            Grid.SetColumn(checkbox, 0); row.Children.Add(checkbox);
            var descText = new TextBlock { Text = manualLine.ManualLineType.Length > 0 ? manualLine.ManualLineType : manualLine.Description, FontSize = 12, Foreground = new SolidColorBrush(TextPrimary), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 8, 0) };
            Grid.SetColumn(descText, 1); row.Children.Add(descText);
            var laborText = new TextBlock { Text = manualLine.LaborUnits > 0 ? $"{manualLine.LaborUnits:N1}" : "-", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 200, 140)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(laborText, 2); row.Children.Add(laborText);
            var refinishText = new TextBlock { Text = manualLine.RefinishUnits > 0 ? $"{manualLine.RefinishUnits:N1}" : "-", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 170, 255)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(refinishText, 3); row.Children.Add(refinishText);
            var frequency = totalExamples > 0 ? (manualLine.TimesUsed * 100.0 / totalExamples) : 0;
            var freqColor = frequency >= 80 ? AccentGreen : frequency >= 50 ? AccentOrange : TextMuted;
            var freqBg = frequency >= 80 ? Color.FromArgb(255, 25, 60, 40) : frequency >= 50 ? Color.FromArgb(255, 55, 40, 20) : Color.FromArgb(255, 45, 45, 50);
            var freqPill = CreatePill($"{frequency:N0}%", freqColor, freqBg);
            freqPill.HorizontalAlignment = HorizontalAlignment.Center; freqPill.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(freqPill, 4); row.Children.Add(freqPill);
            row.PointerEntered += (s, e) => row.Background = new SolidColorBrush(CardHover);
            row.PointerExited += (s, e) => row.Background = new SolidColorBrush(alternateRow ? SurfaceBg : CardBg);
            return row;
        }


        // ═══════════════════════════════════════════
        //  BRIDGE: Add patterns to cart
        // ═══════════════════════════════════════════

        private void AddPatternToCart(ManualLinePattern pattern)
        {
            foreach (var entry in pattern.ManualLines)
            {
                _estimateLines.Add(new EstimateLine
                {
                    PartName = entry.ManualLineType.Length > 0 ? entry.ManualLineType : entry.Description,
                    OperationType = pattern.ParentOperationType ?? "",
                    LaborType = entry.LaborType,
                    LaborHours = entry.LaborUnits,
                    RefinishHours = entry.RefinishUnits,
                    Price = entry.AvgPrice
                });
            }
            UpdateEstimateDisplay();
            if (pattern.ManualLines.Count > 0)
            {
                ShowUnifiedSuggestions(pattern.ParentPartName ?? pattern.ManualLines[0].ManualLineType, pattern.ParentOperationType ?? "");
            }
            _statusText!.Text = $"Added {pattern.ManualLines.Count} ops from {pattern.ParentPartName}";
            _statusText.Foreground = new SolidColorBrush(AccentGreen);
        }

        private void AddSelectedToCart(ManualLinePattern pattern)
        {
            if (_selectedOperations.Count == 0) return;
            foreach (var entry in _selectedOperations)
            {
                _estimateLines.Add(new EstimateLine
                {
                    PartName = entry.ManualLineType.Length > 0 ? entry.ManualLineType : entry.Description,
                    OperationType = pattern.ParentOperationType ?? "",
                    LaborType = entry.LaborType,
                    LaborHours = entry.LaborUnits,
                    RefinishHours = entry.RefinishUnits,
                    Price = entry.AvgPrice
                });
            }
            UpdateEstimateDisplay();
            _statusText!.Text = $"Added {_selectedOperations.Count} selected ops to estimate";
            _statusText.Foreground = new SolidColorBrush(AccentGreen);
        }

        // ═══════════════════════════════════════════
        //  CART: Estimate display + management
        // ═══════════════════════════════════════════

        private void UpdateEstimateDisplay()
        {
            if (_estimateView == null) return;
            _estimateView.Items.Clear();
            if (_estimateLines.Count == 0)
            {
                _estimateView.Items.Add(new TextBlock { Text = "Look up a part to add lines", FontSize = 12, FontStyle = Windows.UI.Text.FontStyle.Italic, Foreground = new SolidColorBrush(TextMuted), Margin = new Thickness(0, 8, 0, 8) });
            }
            else
            {
                for (int i = 0; i < _estimateLines.Count; i++)
                    _estimateView.Items.Add(CreateEstimateLineItem(_estimateLines[i], i));
            }
            var totalLabor = _estimateLines.Sum(l => l.LaborHours);
            var totalRefinish = _estimateLines.Sum(l => l.RefinishHours);
            var totalPrice = _estimateLines.Sum(l => l.Price);
            if (_totalLabor != null) _totalLabor.Text = totalLabor > 0 ? $"Labor: {totalLabor:F1}" : "Labor: -";
            if (_totalRefinish != null) _totalRefinish.Text = totalRefinish > 0 ? $"Refinish: {totalRefinish:F1}" : "Refinish: -";
            if (_totalPrice != null) _totalPrice.Text = totalPrice > 0 ? $"${totalPrice:F0}" : "-";
            if (_lineCount != null) _lineCount.Text = $"{_estimateLines.Count} line{(_estimateLines.Count == 1 ? "" : "s")}";
            UpdateButtonStates();
        }

        private Border CreateEstimateLineItem(EstimateLine line, int index)
        {
            var border = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 50, 60, 50)), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 2, 0, 2) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var desc = line.PartName;
            if (!string.IsNullOrEmpty(line.OperationType)) desc += $" ({line.OperationType})";
            grid.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var valueText = line.LaborHours > 0 ? $"{line.LaborHours:F1}h" : line.RefinishHours > 0 ? $"{line.RefinishHours:F1}r" : line.Price > 0 ? $"${line.Price:F0}" : "";
            var valueBlock = new TextBlock { Text = valueText, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            Grid.SetColumn(valueBlock, 1); grid.Children.Add(valueBlock);
            var removeBtn = new Button { Content = "\u00D7", FontSize = 14, Padding = new Thickness(6, 2, 6, 2), Background = new SolidColorBrush(Colors.Transparent), Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 100, 100)), Tag = index };
            removeBtn.Click += RemoveLine_Click;
            Grid.SetColumn(removeBtn, 2); grid.Children.Add(removeBtn);
            border.Child = grid;
            return border;
        }

        private void ShowUnifiedSuggestions(string partName, string operationType)
        {
            if (_suggestionsPanel == null) return;
            _suggestionsPanel.Children.Clear();

            var existingKeys = _estimateLines.Select(l => $"{l.PartName}|{l.OperationType}".ToLowerInvariant()).ToHashSet();

            // Collect suggestions from both sources, keyed by name+opType for dedup
            var merged = new Dictionary<string, (string Name, string OpType, decimal LaborHours, decimal RefinishHours, decimal Price, double Confidence)>(StringComparer.OrdinalIgnoreCase);

            // Related operations from learning service
            try
            {
                var related = _learningService.GetRelatedOperations(partName, operationType, 8);
                foreach (var entry in related)
                {
                    var key = $"{entry.PartName}|{entry.OperationType}";
                    if (!existingKeys.Contains(key.ToLowerInvariant()) && !merged.ContainsKey(key))
                        merged[key] = (entry.PartName, entry.OperationType, entry.AvgLaborHours, entry.AvgRefinishHours, entry.AvgPrice, entry.CoOccurrenceRate);
                }
            }
            catch { /* ignore */ }

            // AI advisor suggestions
            try
            {
                var enteredTypes = _estimateLines.Select(l => l.OperationType).ToList();
                var suggestion = _advisorService.GetProactiveSuggestions(partName, operationType, enteredTypes, null);
                if (suggestion?.PatternSuggestions != null)
                {
                    foreach (var sug in suggestion.PatternSuggestions)
                    {
                        var key = $"{sug.Description}|{sug.OperationType}";
                        if (existingKeys.Contains(key.ToLowerInvariant())) continue;
                        if (merged.TryGetValue(key, out var existing))
                        {
                            // Keep higher confidence
                            if (sug.Confidence > existing.Confidence)
                                merged[key] = (sug.Description, sug.OperationType, sug.Hours, 0, 0, sug.Confidence);
                        }
                        else
                        {
                            merged[key] = (sug.Description, sug.OperationType, sug.Hours, 0, 0, sug.Confidence);
                        }
                    }
                }
            }
            catch { /* ignore */ }

            if (merged.Count == 0) { _suggestionsPanel.Visibility = Visibility.Collapsed; return; }

            _suggestionsPanel.Children.Add(new TextBlock
            {
                Text = "SUGGESTIONS", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextMuted), CharacterSpacing = 60
            });
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var item in merged.Values.OrderByDescending(v => v.Confidence).Take(8))
            {
                var hoursText = item.LaborHours > 0 ? $" {item.LaborHours:F1}h" : "";
                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 40)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 120, 70)),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 4, 10, 4),
                    Child = new TextBlock { Text = $"{item.Name} {item.OpType}{hoursText}", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 220, 200)) }
                };
                var captured = item;
                chip.PointerEntered += (s, e) => { chip.Background = new SolidColorBrush(Color.FromArgb(255, 45, 70, 50)); };
                chip.PointerExited += (s, e) => { chip.Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 40)); };
                chip.Tapped += (s, e) => { _estimateLines.Add(new EstimateLine { PartName = captured.Name, OperationType = captured.OpType, LaborHours = captured.LaborHours, RefinishHours = captured.RefinishHours, Price = captured.Price }); UpdateEstimateDisplay(); };
                chipRow.Children.Add(chip);
            }
            _suggestionsPanel.Children.Add(new ScrollViewer { Content = chipRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 50 });
            _suggestionsPanel.Visibility = Visibility.Visible;
        }

        // ═══════════════════════════════════════════
        //  ACTION HANDLERS
        // ═══════════════════════════════════════════

        private void RemoveLine_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.Tag is int index && index >= 0 && index < _estimateLines.Count) { _estimateLines.RemoveAt(index); UpdateEstimateDisplay(); } }
        private void ClearAll_Click(object sender, RoutedEventArgs e) { _estimateLines.Clear(); _selectedOperations.Clear(); _selectedOpResults.Clear(); UpdateEstimateDisplay(); }

        private void ClipIt_Click(object sender, RoutedEventArgs e)
        {
            if (_estimateLines.Count == 0) return;
            try
            {
                VirtualClipboardService.Instance.Clear();
                foreach (var line in _estimateLines)
                    VirtualClipboardService.Instance.AddOperation(new VirtualClipboardOp { Description = line.PartName, OperationType = line.OperationType, LaborHours = line.LaborHours, RefinishHours = line.RefinishHours, Price = line.Price }, "EstimateBuilder");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EstimateBuilder] Clip error: {ex.Message}"); }
        }

        private void TypeIt_Click(object sender, RoutedEventArgs e)
        {
            if (_estimateLines.Count == 0) return;
            try
            {
                var lines = _estimateLines.Select(l => { var val = l.LaborHours > 0 ? $"{l.LaborHours:F1}h" : l.RefinishHours > 0 ? $"{l.RefinishHours:F1}r" : l.Price > 0 ? $"${l.Price:F0}" : ""; return $"{l.PartName} {l.OperationType} - {val}"; });
                var text = string.Join("\n", lines);
                var dataPackage = new DataPackage(); dataPackage.SetText(text); Clipboard.SetContent(dataPackage);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EstimateBuilder] Type error: {ex.Message}"); }
        }

        private void SendToChat_Click(object sender, RoutedEventArgs e)
        {
            if (_estimateLines.Count == 0) return;
            var operations = _estimateLines.Select(l => new GeneratedOperation { Description = l.PartName, OperationType = l.OperationType, LaborHours = l.LaborHours, RefinishHours = l.RefinishHours, Price = l.Price }).ToList();
            OnOperationsGenerated?.Invoke(this, operations);
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var lines = new System.Text.StringBuilder();
            foreach (var op in _selectedOperations) { var desc = op.ManualLineType.Length > 0 ? op.ManualLineType : op.Description; var hours = op.LaborUnits > 0 ? op.LaborUnits : op.RefinishUnits; var laborType = ResolveCCCLaborType(op.LaborType, op.RefinishUnits > 0); var price = op.AvgPrice > 0 ? $"\t${op.AvgPrice:N0}" : ""; lines.AppendLine($"#\t{laborType}\t{desc}\t{hours:N1}{price}"); }
            foreach (var op in _selectedOpResults) { var desc = op.OperationName.Length > 0 ? op.OperationName : op.Description; var hours = op.LaborHours > 0 ? op.LaborHours : op.RefinishHours; var laborType = ResolveCCCLaborType(op.LaborType, op.RefinishHours > 0); var price = op.AvgPrice > 0 ? $"\t${op.AvgPrice:N0}" : ""; lines.AppendLine($"#\t{laborType}\t{desc}\t{hours:N1}{price}"); }
            foreach (var line in _estimateLines) { var val = line.LaborHours > 0 ? $"{line.LaborHours:F1}" : line.RefinishHours > 0 ? $"{line.RefinishHours:F1}" : line.Price > 0 ? $"${line.Price:F0}" : ""; var laborType = ResolveCCCLaborType(line.LaborType, line.RefinishHours > 0); var price = line.Price > 0 ? $"\t${line.Price:N0}" : ""; lines.AppendLine($"#\t{laborType}\t{line.PartName}\t{val}{price}"); }
            if (lines.Length == 0) return;
            var dataPackage = new DataPackage(); dataPackage.SetText(lines.ToString()); Clipboard.SetContent(dataPackage);
            if (sender is Button btn) { var original = btn.Content; var cc = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; cc.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 14 }); cc.Children.Add(new TextBlock { Text = "Copied!", FontSize = 12 }); btn.Content = cc; await System.Threading.Tasks.Task.Delay(1500); btn.Content = original; }
        }

        private async void InsertButton_Click(object sender, RoutedEventArgs e)
        {
            var rows = new List<string[]>();
            foreach (var op in _selectedOperations) { var desc = op.ManualLineType.Length > 0 ? op.ManualLineType : op.Description; var hours = op.LaborUnits > 0 ? op.LaborUnits : op.RefinishUnits; var laborType = ResolveCCCLaborType(op.LaborType, op.RefinishUnits > 0); var row = op.AvgPrice > 0 ? new[] { "#", laborType, desc, hours.ToString("N1"), $"${op.AvgPrice:N0}" } : new[] { "#", laborType, desc, hours.ToString("N1") }; rows.Add(row); }
            foreach (var op in _selectedOpResults) { var desc = op.OperationName.Length > 0 ? op.OperationName : op.Description; var hours = op.LaborHours > 0 ? op.LaborHours : op.RefinishHours; var laborType = ResolveCCCLaborType(op.LaborType, op.RefinishHours > 0); var row = op.AvgPrice > 0 ? new[] { "#", laborType, desc, hours.ToString("N1"), $"${op.AvgPrice:N0}" } : new[] { "#", laborType, desc, hours.ToString("N1") }; rows.Add(row); }
            foreach (var line in _estimateLines) { var val = line.LaborHours > 0 ? line.LaborHours : line.RefinishHours; var laborType = ResolveCCCLaborType(line.LaborType, line.RefinishHours > 0); var row = line.Price > 0 ? new[] { "#", laborType, line.PartName, val.ToString("N1"), $"${line.Price:N0}" } : new[] { "#", laborType, line.PartName, val.ToString("N1") }; rows.Add(row); }
            if (rows.Count == 0) return;
            if (sender is Button btn)
            {
                var aimContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; aimContent.Children.Add(new FontIcon { Glyph = "\uE7B3", FontSize = 14 }); aimContent.Children.Add(new TextBlock { Text = "Click in CCC, press ENTER...", FontSize = 12 }); btn.Content = aimContent; btn.IsEnabled = false;
                try
                {
                    _statusText!.Text = $"Position cursor in CCC, press ENTER to paste {rows.Count} lines. ESC to cancel."; _statusText.Foreground = new SolidColorBrush(AccentBlue);
                    var result = await _typeItService.AimAndTypeAsync(rows.ToArray());
                    if (result.Completed) { _statusText.Text = $"Inserted {result.RowsCompleted} lines into CCC!"; _statusText.Foreground = new SolidColorBrush(AccentGreen); }
                    else if (result.WasCancelled) { _statusText.Text = "Insert cancelled."; _statusText.Foreground = new SolidColorBrush(AccentOrange); }
                    else { _statusText.Text = $"Insert incomplete: {result.RowsCompleted}/{result.TotalRows} lines."; _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 100, 100)); }
                }
                catch (Exception ex) { _statusText!.Text = $"Error: {ex.Message}"; _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 100, 100)); }
                finally
                {
                    var ic = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; ic.Children.Add(new FontIcon { Glyph = "\uE768", FontSize = 14 }); ic.Children.Add(new TextBlock { Text = "Insert CCC", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }); btn.Content = ic; btn.IsEnabled = true;
                }
            }
        }

        // ═══════════════════════════════════════════
        //  STATE MANAGEMENT
        // ═══════════════════════════════════════════

        /// <summary>
        /// Resolve the CCC labor type code from stored LaborType string.
        /// Falls back to refinish heuristic only when no stored type exists.
        /// </summary>
        private static string ResolveCCCLaborType(string storedLaborType, bool hasRefinish)
        {
            if (!string.IsNullOrWhiteSpace(storedLaborType))
            {
                var lt = storedLaborType.Trim().ToLowerInvariant();
                if (lt.Contains("body") || lt == "bdy") return "Bdy";
                if (lt.Contains("refin") || lt == "refn" || lt == "rfn" || lt == "paint") return "Refn";
                if (lt.Contains("mech")) return "Mech";
                if (lt.Contains("struct") || lt == "frame" || lt == "frm") return "Struc";
                // If the stored type is already a short code, use it directly
                if (lt.Length <= 5) return storedLaborType.Trim();
            }
            // Fallback heuristic
            return hasRefinish ? "Refn" : "Rpr";
        }

        private void UpdateButtonStates()
        {
            var totalSelected = _selectedOperations.Count + _selectedOpResults.Count + _estimateLines.Count;
            var hasSelection = totalSelected > 0;
            if (_insertButton != null) _insertButton.IsEnabled = hasSelection;
            if (_selectionCountText != null) { _selectionCountText.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed; _selectionCountText.Text = $"{totalSelected} selected"; }
        }

        // ═══════════════════════════════════════════
        //  PRESETS
        // ═══════════════════════════════════════════

        private StackPanel BuildPresetsRow()
        {
            var outer = new StackPanel { Spacing = 6 };
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            headerRow.Children.Add(new TextBlock
            {
                Text = "PRESETS", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextMuted), CharacterSpacing = 60,
                VerticalAlignment = VerticalAlignment.Center
            });
            var saveBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74E", FontSize = 11 },
                Padding = new Thickness(6, 3, 6, 3),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55)),
                Foreground = new SolidColorBrush(TextSecondary),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(saveBtn, "Save current estimate as preset");
            saveBtn.Click += SaveAsPreset_Click;
            headerRow.Children.Add(saveBtn);
            outer.Children.Add(headerRow);

            _presetsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var scroller = new ScrollViewer
            {
                Content = _presetsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 36
            };
            outer.Children.Add(scroller);
            return outer;
        }

        private async void SaveAsPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_estimateLines.Count == 0) return;
            var dialog = new ContentDialog
            {
                Title = "Save as Preset",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };
            var nameBox = new TextBox { PlaceholderText = "Preset name...", FontSize = 13 };
            dialog.Content = nameBox;
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
            {
                var lines = _estimateLines.Select(l => new PresetLine
                {
                    PartName = l.PartName,
                    OperationType = l.OperationType,
                    LaborHours = l.LaborHours,
                    RefinishHours = l.RefinishHours,
                    Price = l.Price
                }).ToList();
                _presetService.SaveCurrentAsPreset(nameBox.Text.Trim(), lines);
                RefreshPresetChips();
            }
        }

        private void RefreshPresetChips()
        {
            if (_presetsPanel == null) return;
            _presetsPanel.Children.Clear();
            var presets = _presetService.GetAllPresets();
            if (presets.Count == 0)
            {
                _presetsPanel.Children.Add(new TextBlock { Text = "No presets yet", FontSize = 11, Foreground = new SolidColorBrush(TextMuted), VerticalAlignment = VerticalAlignment.Center, FontStyle = Windows.UI.Text.FontStyle.Italic });
                return;
            }
            foreach (var preset in presets)
            {
                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 40, 50, 65)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 80, 110)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 4, 10, 4)
                };
                var chipContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                chipContent.Children.Add(new TextBlock { Text = preset.Name, FontSize = 11, Foreground = new SolidColorBrush(TextPrimary), VerticalAlignment = VerticalAlignment.Center });
                chipContent.Children.Add(new TextBlock { Text = $"{preset.Lines.Count}L", FontSize = 10, Foreground = new SolidColorBrush(TextMuted), VerticalAlignment = VerticalAlignment.Center });
                chip.Child = chipContent;
                var captured = preset;
                chip.PointerEntered += (s, e) => { chip.Background = new SolidColorBrush(Color.FromArgb(255, 55, 70, 90)); };
                chip.PointerExited += (s, e) => { chip.Background = new SolidColorBrush(Color.FromArgb(255, 40, 50, 65)); };
                chip.Tapped += (s, e) =>
                {
                    _presetService.RecordUsage(captured.Id);
                    foreach (var line in captured.Lines)
                        _estimateLines.Add(new EstimateLine { PartName = line.PartName, OperationType = line.OperationType, LaborHours = line.LaborHours, RefinishHours = line.RefinishHours, Price = line.Price });
                    UpdateEstimateDisplay();
                    _statusText!.Text = $"Loaded preset \"{captured.Name}\" ({captured.Lines.Count} lines)";
                    _statusText.Foreground = new SolidColorBrush(AccentGreen);
                };
                _presetsPanel.Children.Add(chip);
            }
        }

        // ═══════════════════════════════════════════
        //  HISTORY VIEW
        // ═══════════════════════════════════════════

        private void ShowHistoryView()
        {
            var estimates = EstimateHistoryDatabase.Instance.GetAllEstimates()
                .OrderByDescending(e => e.ImportedDate).ToList();
            if (estimates.Count == 0)
            {
                _statusText!.Text = "No imported estimates yet";
                _statusText.Foreground = new SolidColorBrush(TextSecondary);
                var emptyCard = new Border
                {
                    Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(40, 48, 40, 48), Margin = new Thickness(0, 40, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center, BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1)
                };
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 16 };
                var iconCircle = new Border { Width = 80, Height = 80, CornerRadius = new CornerRadius(40), Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 50)), HorizontalAlignment = HorizontalAlignment.Center };
                iconCircle.Child = new FontIcon { Glyph = "\uE81C", FontSize = 36, Foreground = new SolidColorBrush(TextMuted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(iconCircle);
                stack.Children.Add(new TextBlock { Text = "No Estimate History", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary), HorizontalAlignment = HorizontalAlignment.Center });
                stack.Children.Add(new TextBlock { Text = "Import estimates in the Upload tab to build\nyour searchable history.", FontSize = 13, Foreground = new SolidColorBrush(TextSecondary), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
                emptyCard.Child = stack;
                _resultsPanel!.Children.Add(emptyCard);
                return;
            }
            _statusText!.Text = $"{estimates.Count} estimate{(estimates.Count != 1 ? "s" : "")} in history";
            _statusText.Foreground = new SolidColorBrush(AccentBlue);
            AddSectionHeader($"{estimates.Count} ESTIMATES");
            foreach (var est in estimates)
                _resultsPanel!.Children.Add(BuildEstimateHistoryCard(est));
        }

        private void ShowHistorySearchResults(string query)
        {
            // Search across insurance, vehicle, claim number, RO, and line item descriptions
            var allEstimates = EstimateHistoryDatabase.Instance.GetAllEstimates();
            var queryLower = query.ToLowerInvariant();
            var matches = allEstimates.Where(e =>
                e.InsuranceCompany.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.VehicleInfo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.ClaimNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.RONumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.VIN.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.LineItems.Any(i => i.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                e.LineItems.Any(i => i.PartName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            ).OrderByDescending(e => e.ImportedDate).ToList();

            if (matches.Count == 0)
            {
                _statusText!.Text = $"No estimates matching \"{query}\"";
                _statusText.Foreground = new SolidColorBrush(AccentOrange);
                return;
            }
            _statusText!.Text = $"{matches.Count} estimate{(matches.Count != 1 ? "s" : "")} matching \"{query}\"";
            _statusText.Foreground = new SolidColorBrush(AccentGreen);
            _resultsPanel!.Children.Add(CreateBackButton(() => { _resultsPanel!.Children.Clear(); ShowHistoryView(); UpdateButtonStates(); }));
            foreach (var est in matches)
                _resultsPanel.Children.Add(BuildEstimateHistoryCard(est));
            UpdateButtonStates();
        }

        private Border BuildEstimateHistoryCard(StoredEstimate est)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4),
                BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1)
            };
            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var accentBar = new Border { Background = new SolidColorBrush(AccentTeal), CornerRadius = new CornerRadius(8, 0, 0, 8) };
            Grid.SetColumn(accentBar, 0);
            innerGrid.Children.Add(accentBar);

            var contentStack = new StackPanel { Padding = new Thickness(14, 12, 14, 12), Spacing = 5 };

            // Row 1: Vehicle info + grand total
            var row1 = new Grid();
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.Children.Add(new TextBlock
            {
                Text = !string.IsNullOrEmpty(est.VehicleInfo) ? est.VehicleInfo : "Unknown Vehicle",
                FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary), TextTrimming = TextTrimming.CharacterEllipsis
            });
            var totalBlock = new TextBlock
            {
                Text = est.GrandTotal > 0 ? $"${est.GrandTotal:N0}" : "",
                FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentGreen)
            };
            Grid.SetColumn(totalBlock, 1);
            row1.Children.Add(totalBlock);
            contentStack.Children.Add(row1);

            // Row 2: Insurance | Claim | Source
            var metaParts = new List<string>();
            if (!string.IsNullOrEmpty(est.InsuranceCompany)) metaParts.Add(est.InsuranceCompany);
            if (!string.IsNullOrEmpty(est.ClaimNumber)) metaParts.Add(est.ClaimNumber);
            if (!string.IsNullOrEmpty(est.EstimateSource)) metaParts.Add(est.EstimateSource);
            if (metaParts.Count > 0)
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = string.Join("  \u2022  ", metaParts),
                    FontSize = 11, Foreground = new SolidColorBrush(TextSecondary),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            // Row 3: RO | Import date
            var row3Parts = new List<string>();
            if (!string.IsNullOrEmpty(est.RONumber)) row3Parts.Add($"RO: {est.RONumber}");
            row3Parts.Add($"Imported: {est.ImportedDate:MM/dd/yyyy}");
            contentStack.Children.Add(new TextBlock
            {
                Text = string.Join("  \u2022  ", row3Parts),
                FontSize = 11, Foreground = new SolidColorBrush(TextMuted)
            });

            // Row 4: Expandable line items
            var lineItemCount = est.LineItems.Count;
            if (lineItemCount > 0)
            {
                var expandPanel = new StackPanel { Spacing = 0 };
                var expandHeader = new Border
                {
                    Background = new SolidColorBrush(Colors.Transparent),
                    Padding = new Thickness(0, 4, 0, 0)
                };
                var expandText = new TextBlock
                {
                    Text = $"\u25B6  {lineItemCount} line item{(lineItemCount != 1 ? "s" : "")}",
                    FontSize = 11, Foreground = new SolidColorBrush(AccentTeal)
                };
                expandHeader.Child = expandText;
                var lineItemsContainer = new StackPanel { Visibility = Visibility.Collapsed, Spacing = 2, Margin = new Thickness(0, 6, 0, 0) };

                foreach (var item in est.LineItems)
                {
                    var itemRow = new Grid { Margin = new Thickness(0, 0, 0, 1) };
                    itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var desc = !string.IsNullOrEmpty(item.Description) ? item.Description : item.PartName;
                    if (!string.IsNullOrEmpty(item.OperationType)) desc += $" ({item.OperationType})";
                    itemRow.Children.Add(new TextBlock
                    {
                        Text = desc, FontSize = 11, Foreground = new SolidColorBrush(TextSecondary),
                        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
                    });

                    var valueParts = new List<string>();
                    if (item.LaborHours > 0) valueParts.Add($"{item.LaborHours:N1}h");
                    if (item.RefinishHours > 0) valueParts.Add($"R:{item.RefinishHours:N1}h");
                    if (item.Price > 0) valueParts.Add($"${item.Price:N0}");
                    var valueBlock = new TextBlock
                    {
                        Text = string.Join(" ", valueParts), FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                        Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(valueBlock, 1);
                    itemRow.Children.Add(valueBlock);

                    if (item.WasDenied)
                    {
                        var deniedPill = CreatePill("Denied", Color.FromArgb(255, 230, 100, 100), Color.FromArgb(255, 60, 25, 25));
                        deniedPill.Margin = new Thickness(6, 0, 0, 0);
                        deniedPill.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetColumn(deniedPill, 2);
                        itemRow.Children.Add(deniedPill);
                    }

                    lineItemsContainer.Children.Add(itemRow);
                }

                bool isExpanded = false;
                expandHeader.PointerPressed += (s, e) =>
                {
                    isExpanded = !isExpanded;
                    lineItemsContainer.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
                    expandText.Text = isExpanded
                        ? $"\u25BC  {lineItemCount} line item{(lineItemCount != 1 ? "s" : "")}"
                        : $"\u25B6  {lineItemCount} line item{(lineItemCount != 1 ? "s" : "")}";
                    e.Handled = true;
                };

                expandPanel.Children.Add(expandHeader);
                expandPanel.Children.Add(lineItemsContainer);
                contentStack.Children.Add(expandPanel);
            }

            // Load to Cart button
            var loadBtn = new Button
            {
                Content = "Load to Cart", FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 55, 45)),
                Foreground = new SolidColorBrush(AccentGreen),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 4, 0, 0)
            };
            var capturedEst = est;
            loadBtn.Click += (s, e) =>
            {
                foreach (var item in capturedEst.LineItems)
                {
                    _estimateLines.Add(new EstimateLine
                    {
                        PartName = !string.IsNullOrEmpty(item.Description) ? item.Description : item.PartName,
                        OperationType = item.OperationType,
                        LaborHours = item.LaborHours,
                        RefinishHours = item.RefinishHours,
                        Price = item.Price
                    });
                }
                UpdateEstimateDisplay();
                _statusText!.Text = $"Loaded {capturedEst.LineItems.Count} lines from {capturedEst.VehicleInfo}";
                _statusText.Foreground = new SolidColorBrush(AccentGreen);
            };
            contentStack.Children.Add(loadBtn);

            Grid.SetColumn(contentStack, 1);
            innerGrid.Children.Add(contentStack);
            card.Child = innerGrid;

            card.PointerEntered += (s, e) => card.Background = new SolidColorBrush(CardHover);
            card.PointerExited += (s, e) => card.Background = new SolidColorBrush(CardBg);
            return card;
        }

        // ═══════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════

        private Border CreatePill(string text, Color foreground, Color background)
        {
            var pill = new Border { Background = new SolidColorBrush(background), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 8, 3) };
            pill.Child = new TextBlock { Text = text, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(foreground) };
            return pill;
        }

        private Border CreateDetailStatCard(string label, string value, Color accent, Color bgTint)
        {
            var card = new Border { Background = new SolidColorBrush(bgTint), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1) };
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = new SolidColorBrush(TextMuted) });
            stack.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(accent) });
            card.Child = stack;
            return card;
        }

        private Border CreateBackButton(Action onClick)
        {
            var backBorder = new Border { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            var backBtn = new Button { Padding = new Thickness(12, 6, 14, 6), Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 50)), Foreground = new SolidColorBrush(TextPrimary), CornerRadius = new CornerRadius(6) };
            var backContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            backContent.Children.Add(new FontIcon { Glyph = "\uE72B", FontSize = 12 });
            backContent.Children.Add(new TextBlock { Text = "Back", FontSize = 12 });
            backBtn.Content = backContent;
            backBtn.Click += (s, e) => onClick();
            backBorder.Child = backBtn;
            return backBorder;
        }

        private void AddSectionHeader(string text) { _resultsPanel!.Children.Add(new TextBlock { Text = text, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextMuted), Margin = new Thickness(0, 8, 0, 6), CharacterSpacing = 60 }); }

        private void AddHeaderText(Grid grid, string text, int column, SolidColorBrush foreground)
        {
            var tb = new TextBlock { Text = text, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            if (column >= 2) tb.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(tb, column); grid.Children.Add(tb);
        }


        // ═══════════════════════════════════════════
        //  PUBLIC API
        // ═══════════════════════════════════════════

        public void AddOperationsFromOcr(List<McstudDesktop.Models.OcrDetectedOperation> operations)
        {
            foreach (var op in operations)
            {
                _estimateLines.Add(new EstimateLine { PartName = op.PartName, OperationType = op.OperationType, LaborHours = op.LaborHours, RefinishHours = op.RefinishHours, Price = op.Price });
            }
            UpdateEstimateDisplay();
        }

        // ═══════════════════════════════════════════
        //  DATA CLASSES
        // ═══════════════════════════════════════════

        private class EstimateLine
        {
            public string PartName { get; set; } = "";
            public string OperationType { get; set; } = "";
            public string LaborType { get; set; } = "";
            public decimal LaborHours { get; set; }
            public decimal RefinishHours { get; set; }
            public decimal Price { get; set; }
        }
    }
}
