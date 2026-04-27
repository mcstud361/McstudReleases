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

        // Learned tab view mode (Operations vs Estimates vs Components)
        private enum LearnedViewMode { Operations, Estimates, Components }
        private LearnedViewMode _viewMode = LearnedViewMode.Operations;
        private Button? _modeOperationsButton;
        private Button? _modeEstimatesButton;
        private Button? _modeComponentsButton;
        private Grid? _opsContentGrid;
        private Border? _actionsBar;
        private Grid? _estimatesContentGrid;
        private Border? _descriptionBar;
        private FontIcon? _descriptionIcon;
        private TextBlock? _descriptionText;
        // Estimates browser state
        private TextBox? _estimatesSearchBox;
        private StackPanel? _estimatesListPanel;
        private TextBlock? _estimatesCountText;
        private List<StoredEstimate> _estimatesCache = new();

        // Estimate detail panel (inline, replaces list when viewing an estimate)
        private Border? _estimateListCard;
        private Grid? _estimateDetailPanel;

        // Component search state
        private Grid? _componentsContentGrid;
        private TextBox? _componentSearchBox;
        private ComboBox? _componentCategoryFilter;
        private TextBlock? _componentResultsCount;
        private StackPanel? _componentResultsPanel;
        private System.Threading.CancellationTokenSource? _componentSearchCts;

        // Sort state for the Estimates table (0=Date, 1=Vehicle, 2=Customer, 3=Insurance, 4=Total)
        private int _estimatesSortColumn = 0;
        private bool _estimatesSortDescending = true;

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

        // Cached brushes — used in per-row hot paths so we don't allocate a new
        // SolidColorBrush per cell on every search-box keystroke.
        private static readonly SolidColorBrush BorderSubtleBrush = new(BorderSubtle);
        private static readonly SolidColorBrush TextPrimaryBrush = new(TextPrimary);
        private static readonly SolidColorBrush TextSecondaryBrush = new(TextSecondary);
        private static readonly SolidColorBrush TextMutedBrush = new(TextMuted);
        private static readonly SolidColorBrush AccentGreenBrush = new(AccentGreen);
        private static readonly SolidColorBrush CardHoverBrush = new(CardHover);
        private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

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

            _opsContentGrid = new Grid { Margin = new Thickness(16, 0, 16, 0), ColumnSpacing = 12 };
            _opsContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            _opsContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var browseColumn = BuildBrowseColumn();
            Grid.SetColumn(browseColumn, 0);
            _opsContentGrid.Children.Add(browseColumn);

            var cartColumn = BuildCartColumn();
            Grid.SetColumn(cartColumn, 1);
            _opsContentGrid.Children.Add(cartColumn);

            Grid.SetRow(_opsContentGrid, 2);
            mainGrid.Children.Add(_opsContentGrid);

            _actionsBar = BuildActionBar();
            Grid.SetRow(_actionsBar, 3);
            mainGrid.Children.Add(_actionsBar);

            // Estimates view occupies the same row 2 as ops content; hidden by default.
            // Spans row 3 too so it gets full vertical space when active.
            _estimatesContentGrid = new Grid { Visibility = Visibility.Collapsed };
            Grid.SetRow(_estimatesContentGrid, 2);
            Grid.SetRowSpan(_estimatesContentGrid, 2);
            mainGrid.Children.Add(_estimatesContentGrid);

            // Components view — same row, also hidden by default.
            // Constructed lazily in BuildComponentSearchView().

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

            // Right-side controls: view mode pills + "My Imports" toggle
            var rightControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

            // View mode pill group
            _modeOperationsButton = BuildModePill("Operations", true);
            _modeOperationsButton.Click += (s, e) => SetViewMode(LearnedViewMode.Operations);
            _modeEstimatesButton = BuildModePill("Estimates", false);
            _modeEstimatesButton.Click += (s, e) => SetViewMode(LearnedViewMode.Estimates);
            _modeComponentsButton = BuildModePill("Components", false);
            _modeComponentsButton.Click += (s, e) => SetViewMode(LearnedViewMode.Components);

            var modePillGroup = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            };
            var modeStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
            modeStack.Children.Add(_modeOperationsButton);
            modeStack.Children.Add(_modeEstimatesButton);
            modeStack.Children.Add(_modeComponentsButton);
            modePillGroup.Child = modeStack;
            rightControls.Children.Add(modePillGroup);

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
            rightControls.Children.Add(_personalToggleButton);

            Grid.SetColumn(rightControls, 1);
            headerGrid.Children.Add(rightControls);

            stack.Children.Add(headerGrid);

            // Description bar — shows a brief explanation of the active view
            _descriptionBar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 34, 44)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 6, 0, 0)
            };
            var descStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _descriptionIcon = new FontIcon
            {
                FontSize = 13,
                Foreground = new SolidColorBrush(AccentTeal),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 0, 0)
            };
            descStack.Children.Add(_descriptionIcon);
            _descriptionText = new TextBlock
            {
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 190, 205)),
                TextWrapping = TextWrapping.Wrap
            };
            descStack.Children.Add(_descriptionText);
            _descriptionBar.Child = descStack;
            stack.Children.Add(_descriptionBar);

            UpdateDescriptionBar();

            border.Child = stack;
            return border;
        }

        private Button BuildModePill(string label, bool active)
        {
            var btn = new Button
            {
                Content = new TextBlock { Text = label, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                Background = new SolidColorBrush(active ? AccentBlue : Colors.Transparent),
                Foreground = new SolidColorBrush(active ? Colors.White : TextSecondary),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 5, 12, 5),
                MinWidth = 0
            };
            return btn;
        }

        private void SetViewMode(LearnedViewMode mode)
        {
            if (_viewMode == mode) return;
            _viewMode = mode;

            // Update pill visuals
            void UpdatePill(Button? btn, bool active)
            {
                if (btn == null) return;
                btn.Background = new SolidColorBrush(active ? AccentBlue : Colors.Transparent);
                btn.Foreground = new SolidColorBrush(active ? Colors.White : TextSecondary);
            }
            UpdatePill(_modeOperationsButton, mode == LearnedViewMode.Operations);
            UpdatePill(_modeEstimatesButton, mode == LearnedViewMode.Estimates);
            UpdatePill(_modeComponentsButton, mode == LearnedViewMode.Components);

            // Swap content visibility
            if (_opsContentGrid != null) _opsContentGrid.Visibility = mode == LearnedViewMode.Operations ? Visibility.Visible : Visibility.Collapsed;
            if (_actionsBar != null) _actionsBar.Visibility = mode == LearnedViewMode.Operations ? Visibility.Visible : Visibility.Collapsed;

            if (_estimatesContentGrid != null)
            {
                if (mode == LearnedViewMode.Estimates)
                {
                    if (_estimatesContentGrid.Children.Count == 0)
                        BuildEstimatesView(_estimatesContentGrid);
                    _estimatesContentGrid.Visibility = Visibility.Visible;
                    HideEstimateDetail(); // ensure list view is shown
                    ReloadAndRenderEstimates();
                }
                else
                {
                    _estimatesContentGrid.Visibility = Visibility.Collapsed;
                }
            }

            if (_componentsContentGrid != null)
            {
                _componentsContentGrid.Visibility = mode == LearnedViewMode.Components ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (mode == LearnedViewMode.Components)
            {
                BuildComponentSearchView();
            }

            UpdateDescriptionBar();
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
            UpdateDescriptionBar();
            if (_currentLookupPart != null) DoLookup();
            else ShowLookupHint();
        }

        private void UpdateDescriptionBar()
        {
            if (_descriptionText == null || _descriptionIcon == null) return;

            string icon;
            string text;

            if (_viewMode == LearnedViewMode.Operations)
            {
                icon = "\uE773"; // Search icon
                text = _viewPersonalOnly
                    ? "Showing only your personally imported operations. Search for a part to see the # manual-line operations learned from your uploads. Select operations to add to your estimate cart."
                    : "Search for a part to see all learned # manual-line operations that typically go with it — built from every estimate you've imported. Select operations to add to your estimate cart.";
            }
            else if (_viewMode == LearnedViewMode.Components)
            {
                icon = "\uE71E"; // Component/search icon
                text = "Search for specific parts, services, or supplies across all imported estimates. Filter by category to find OEM parts by number, service calibrations, or material costs.";
            }
            else
            {
                icon = "\uE8A5"; // Document icon
                text = _viewPersonalOnly
                    ? "Showing only your personally imported estimates. Click a row for full details, open the original PDF, or delete. Data is stored locally and never leaves your machine."
                    : "Browse all uploaded PDF estimates stored on this device. Click a row for full details, open the original PDF, or delete. Insurance and claim data never leave your machine.";
            }

            _descriptionIcon.Glyph = icon;
            _descriptionText.Text = text;
        }

        // ═══════════════════════════════════════════
        //  ESTIMATES VIEW (Imported PDF browser)
        // ═══════════════════════════════════════════

        private void BuildEstimatesView(Grid host)
        {
            host.RowDefinitions.Clear();
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });        // toolbar
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });        // privacy notice
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list
            host.Margin = new Thickness(16, 0, 16, 14);

            // Toolbar: search + cleanup button + count
            var toolbar = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 8) };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _estimatesSearchBox = new TextBox
            {
                PlaceholderText = "Search vehicle, customer, claim, RO, VIN, insurance...",
                FontSize = 13,
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(TextPrimary),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                CornerRadius = new CornerRadius(8)
            };
            _estimatesSearchBox.TextChanged += (s, e) => RenderEstimatesList();
            Grid.SetColumn(_estimatesSearchBox, 0);
            toolbar.Children.Add(_estimatesSearchBox);

            var cleanupContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            cleanupContent.Children.Add(new FontIcon { Glyph = "\uE74D", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 120, 120)) });
            cleanupContent.Children.Add(new TextBlock { Text = "Clean Up Duplicates", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var cleanupBtn = new Button
            {
                Content = cleanupContent,
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                Foreground = new SolidColorBrush(TextPrimary)
            };
            ToolTipService.SetToolTip(cleanupBtn, "Remove duplicate imports, keeping the newest of each group");
            cleanupBtn.Click += async (s, e) => await CleanupDuplicateEstimatesAsync();
            Grid.SetColumn(cleanupBtn, 1);
            toolbar.Children.Add(cleanupBtn);

            _estimatesCountText = new TextBlock
            {
                Text = "0 estimates",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0)
            };
            Grid.SetColumn(_estimatesCountText, 2);
            toolbar.Children.Add(_estimatesCountText);

            Grid.SetRow(toolbar, 0);
            host.Children.Add(toolbar);

            // Privacy notice
            var privacyBar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 38, 50)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 80, 110)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var privacyStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            privacyStack.Children.Add(new FontIcon { Glyph = "\uE72E", FontSize = 12, Foreground = new SolidColorBrush(AccentTeal), VerticalAlignment = VerticalAlignment.Center }); // lock icon
            var privacyText = new TextBlock
            {
                Text = "Visible only to you on this device. Insurance and claim data never leave your machine.",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            privacyStack.Children.Add(privacyText);
            privacyBar.Child = privacyStack;
            Grid.SetRow(privacyBar, 1);
            host.Children.Add(privacyBar);

            // List card
            _estimateListCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 32)),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1)
            };
            var listScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0)
            };
            _estimatesListPanel = new StackPanel { Spacing = 0 };
            listScroller.Content = _estimatesListPanel;
            _estimateListCard.Child = listScroller;
            Grid.SetRow(_estimateListCard, 2);
            host.Children.Add(_estimateListCard);

            // Detail panel — hidden by default, shown when clicking an estimate row
            _estimateDetailPanel = new Grid { Visibility = Visibility.Collapsed };
            Grid.SetRow(_estimateDetailPanel, 2);
            host.Children.Add(_estimateDetailPanel);
        }

        // Shared column layout for the Estimates table — referenced by both header and rows
        // so the columns can never drift.
        private static void AddEstimateColumnDefinitions(Grid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });                          // date
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });      // vehicle
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });      // customer
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });      // insurance
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });                         // total
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });                          // actions
        }

        /// <summary>
        /// Reload the per-user estimates cache from the database, then render. Call this on
        /// view activation, after deletes, and after new imports — NOT on every keystroke.
        /// </summary>
        private void ReloadAndRenderEstimates()
        {
            _estimatesCache = EstimateHistoryDatabase.Instance.GetEstimatesForCurrentUser();
            RenderEstimatesList();
        }

        /// <summary>
        /// Filter the in-memory cache by the current search text and rebuild the list rows.
        /// Does NOT touch the database — safe to call on every keystroke.
        /// </summary>
        private void RenderEstimatesList()
        {
            if (_estimatesListPanel == null) return;

            var query = (_estimatesSearchBox?.Text ?? "").Trim();
            IEnumerable<StoredEstimate> filtered = _estimatesCache;
            if (!string.IsNullOrEmpty(query))
            {
                var q = query;
                filtered = _estimatesCache.Where(e =>
                    Contains(e.VehicleInfo, q) ||
                    Contains(e.CustomerName, q) ||
                    Contains(e.InsuranceCompany, q) ||
                    Contains(e.ClaimNumber, q) ||
                    Contains(e.RONumber, q) ||
                    Contains(e.VIN, q));
            }

            // Apply sort
            filtered = _estimatesSortColumn switch
            {
                1 => _estimatesSortDescending
                    ? filtered.OrderByDescending(e => e.VehicleInfo ?? "", StringComparer.OrdinalIgnoreCase)
                    : filtered.OrderBy(e => e.VehicleInfo ?? "", StringComparer.OrdinalIgnoreCase),
                2 => _estimatesSortDescending
                    ? filtered.OrderByDescending(e => e.CustomerName ?? "", StringComparer.OrdinalIgnoreCase)
                    : filtered.OrderBy(e => e.CustomerName ?? "", StringComparer.OrdinalIgnoreCase),
                3 => _estimatesSortDescending
                    ? filtered.OrderByDescending(e => e.InsuranceCompany ?? "", StringComparer.OrdinalIgnoreCase)
                    : filtered.OrderBy(e => e.InsuranceCompany ?? "", StringComparer.OrdinalIgnoreCase),
                4 => _estimatesSortDescending
                    ? filtered.OrderByDescending(e => ComputeGrandTotal(e))
                    : filtered.OrderBy(e => ComputeGrandTotal(e)),
                _ => _estimatesSortDescending
                    ? filtered.OrderByDescending(e => e.ImportedDate)
                    : filtered.OrderBy(e => e.ImportedDate)
            };

            var list = filtered.ToList();
            _estimatesListPanel.Children.Clear();

            // Header row (built fresh per render — cheap, only 5 TextBlocks)
            _estimatesListPanel.Children.Add(BuildEstimatesHeaderRow());

            if (list.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = string.IsNullOrEmpty(query)
                        ? "No estimates imported yet. Drop a PDF in the Estimate Upload tab to get started."
                        : $"No estimates match \"{query}\".",
                    FontSize = 12,
                    Foreground = TextMutedBrush,
                    Margin = new Thickness(16, 24, 16, 24),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                _estimatesListPanel.Children.Add(empty);
            }
            else
            {
                foreach (var est in list)
                    _estimatesListPanel.Children.Add(BuildEstimateRow(est));
            }

            if (_estimatesCountText != null)
                _estimatesCountText.Text = $"{list.Count} estimate{(list.Count == 1 ? "" : "s")}";
        }

        private static bool Contains(string? haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private Grid BuildEstimatesHeaderRow()
        {
            var header = new Grid
            {
                Padding = new Thickness(14, 10, 14, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 24, 24, 28))
            };
            AddEstimateColumnDefinitions(header);

            void AddHeader(string text, int col, HorizontalAlignment align = HorizontalAlignment.Left)
            {
                var showArrow = _estimatesSortColumn == col;
                var arrow = showArrow ? (_estimatesSortDescending ? " \u2193" : " \u2191") : "";
                var t = new TextBlock
                {
                    Text = text + arrow,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = showArrow ? new SolidColorBrush(AccentBlue) : TextMutedBrush,
                    HorizontalAlignment = align
                };
                // Make the cell itself the click target so the hitbox is larger than the text.
                var cellBorder = new Border
                {
                    Background = TransparentBrush,
                    Padding = new Thickness(0),
                    Child = t
                };
                cellBorder.PointerEntered += (s, e) => cellBorder.Background = CardHoverBrush;
                cellBorder.PointerExited += (s, e) => cellBorder.Background = TransparentBrush;
                cellBorder.Tapped += (s, e) =>
                {
                    if (_estimatesSortColumn == col)
                        _estimatesSortDescending = !_estimatesSortDescending;
                    else
                    {
                        _estimatesSortColumn = col;
                        _estimatesSortDescending = true; // new column defaults to desc
                    }
                    RenderEstimatesList();
                };
                Grid.SetColumn(cellBorder, col);
                header.Children.Add(cellBorder);
            }
            AddHeader("DATE", 0);
            AddHeader("VEHICLE", 1);
            AddHeader("CUSTOMER", 2);
            AddHeader("INSURANCE", 3);
            AddHeader("TOTAL", 4, HorizontalAlignment.Right);

            // Actions column header (no sort)
            var actionsLabel = new TextBlock
            {
                Text = "",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = TextMutedBrush,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(actionsLabel, 5);
            header.Children.Add(actionsLabel);

            return header;
        }

        private Border BuildEstimateRow(StoredEstimate est)
        {
            var border = new Border
            {
                BorderBrush = BorderSubtleBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Background = TransparentBrush,
                Padding = new Thickness(14, 10, 14, 10)
            };

            var grid = new Grid();
            AddEstimateColumnDefinitions(grid);

            void Cell(string text, int col, SolidColorBrush fg, HorizontalAlignment align = HorizontalAlignment.Left)
            {
                var t = new TextBlock
                {
                    Text = string.IsNullOrEmpty(text) ? "—" : text,
                    FontSize = 12,
                    Foreground = string.IsNullOrEmpty(text) ? TextMutedBrush : fg,
                    HorizontalAlignment = align,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(t, col);
                grid.Children.Add(t);
            }

            Cell(est.ImportedDate.ToString("M/d/yy"), 0, TextSecondaryBrush);
            var vehicleLabel = est.VehicleInfo ?? "";
            if (est.Version > 1) vehicleLabel = $"{vehicleLabel} (v{est.Version})".TrimStart();
            Cell(vehicleLabel, 1, TextPrimaryBrush);
            Cell(est.CustomerName, 2, TextPrimaryBrush);
            Cell(est.InsuranceCompany, 3, TextSecondaryBrush);
            var displayTotal = ComputeGrandTotal(est);
            Cell(displayTotal > 0 ? $"${displayTotal:N0}" : "—", 4, AccentGreenBrush, HorizontalAlignment.Right);

            // Actions cell: [Open PDF] [Delete]
            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Open PDF button — shown only when we have a path on disk
            bool hasPdfPath = !string.IsNullOrEmpty(est.SourcePdfPath) && System.IO.File.Exists(est.SourcePdfPath);
            var openBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE8A7", FontSize = 13 }, // "OpenFile"
                Background = TransparentBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = 0,
                MinHeight = 0,
                IsEnabled = hasPdfPath,
                Opacity = hasPdfPath ? 1.0 : 0.35
            };
            ToolTipService.SetToolTip(openBtn, hasPdfPath ? "Open original PDF" : "Original PDF not found");
            openBtn.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = est.SourcePdfPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Learned] Failed to open PDF: {ex.Message}");
                }
            };
            // Don't bubble row tap when clicking buttons
            openBtn.AddHandler(UIElement.TappedEvent, new TappedEventHandler((s, e) => e.Handled = true), true);
            actionsPanel.Children.Add(openBtn);

            // Delete button
            var deleteBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 100, 100)) }, // "Delete"
                Background = TransparentBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = 0,
                MinHeight = 0
            };
            ToolTipService.SetToolTip(deleteBtn, "Delete this estimate");
            deleteBtn.Click += async (s, e) => await ConfirmDeleteEstimateAsync(est);
            deleteBtn.AddHandler(UIElement.TappedEvent, new TappedEventHandler((s, e) => e.Handled = true), true);
            actionsPanel.Children.Add(deleteBtn);

            Grid.SetColumn(actionsPanel, 5);
            grid.Children.Add(actionsPanel);

            border.Child = grid;

            // Hover + click — reuse cached brushes
            border.PointerEntered += (s, e) => border.Background = CardHoverBrush;
            border.PointerExited += (s, e) => border.Background = TransparentBrush;
            border.Tapped += (s, e) => ShowEstimateDetails(est, border);

            return border;
        }

        private async System.Threading.Tasks.Task CleanupDuplicateEstimatesAsync()
        {
            var db = EstimateHistoryDatabase.Instance;
            var dupIds = db.FindDuplicateIds();

            if (dupIds.Count == 0)
            {
                var none = new ContentDialog
                {
                    Title = "No duplicates found",
                    Content = "Nothing to clean up — every saved estimate has a unique signature (file + total + VIN).",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await none.ShowAsync();
                return;
            }

            var confirm = new ContentDialog
            {
                Title = $"Remove {dupIds.Count} duplicate estimate{(dupIds.Count == 1 ? "" : "s")}?",
                Content = $"Found {dupIds.Count} duplicate import{(dupIds.Count == 1 ? "" : "s")} " +
                          "(same file + grand total + VIN). The most recent version of each will be kept. " +
                          "This also removes the duplicate rows' operations from the learned index. " +
                          "This cannot be undone.",
                PrimaryButtonText = "Clean Up",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            int removed = db.DeleteDuplicates();
            ReloadAndRenderEstimates();
            RefreshStatsValues();

            var done = new ContentDialog
            {
                Title = "Cleanup complete",
                Content = $"Removed {removed} duplicate estimate{(removed == 1 ? "" : "s")}.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await done.ShowAsync();
        }

        private async System.Threading.Tasks.Task ConfirmDeleteEstimateAsync(StoredEstimate est)
        {
            var label = !string.IsNullOrEmpty(est.VehicleInfo)
                ? est.VehicleInfo
                : (!string.IsNullOrEmpty(est.CustomerName) ? est.CustomerName : est.SourceFile);
            var dlg = new ContentDialog
            {
                Title = "Delete estimate?",
                Content = $"Remove \"{label}\" from your learned estimates?\n\nThis also removes its operations from the learned operations index. This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            var result = await dlg.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var ok = EstimateHistoryDatabase.Instance.DeleteEstimate(est.Id);
            if (ok)
            {
                ReloadAndRenderEstimates();
                RefreshStatsValues();
            }
        }

        private void ShowEstimateDetails(StoredEstimate est, FrameworkElement anchor)
        {
            if (_estimateDetailPanel == null || _estimateListCard == null) return;

            // Hide list, show detail panel
            _estimateListCard.Visibility = Visibility.Collapsed;
            _estimateDetailPanel.Children.Clear();
            _estimateDetailPanel.Visibility = Visibility.Visible;

            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var stack = new StackPanel { Spacing = 10, Padding = new Thickness(4) };

            // ── Top bar: Back + title + action buttons ──
            var topBar = new Grid();
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var backBtn = new Button
            {
                Padding = new Thickness(8, 5, 10, 5),
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 50)),
                Foreground = new SolidColorBrush(TextPrimary),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Center
            };
            var backContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            backContent.Children.Add(new FontIcon { Glyph = "\uE72B", FontSize = 12 });
            backContent.Children.Add(new TextBlock { Text = "Back", FontSize = 12 });
            backBtn.Content = backContent;
            backBtn.Click += (s, e) => HideEstimateDetail();
            Grid.SetColumn(backBtn, 0);
            topBar.Children.Add(backBtn);

            var titleText = string.IsNullOrEmpty(est.VehicleInfo) ? "(Unknown vehicle)" : est.VehicleInfo;
            if (est.Version > 1) titleText = $"{titleText}  v{est.Version}";
            var titleBlock = new TextBlock
            {
                Text = titleText,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(12, 0, 12, 0)
            };
            Grid.SetColumn(titleBlock, 1);
            topBar.Children.Add(titleBlock);

            var topActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

            // Open PDF button
            bool hasPdfPath = !string.IsNullOrEmpty(est.SourcePdfPath) && System.IO.File.Exists(est.SourcePdfPath);
            var pdfBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { new FontIcon { Glyph = "\uE8A7", FontSize = 12 }, new TextBlock { Text = "PDF", FontSize = 11 } }
                },
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(TextPrimary),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                CornerRadius = new CornerRadius(6),
                IsEnabled = hasPdfPath,
                Opacity = hasPdfPath ? 1.0 : 0.35
            };
            pdfBtn.Click += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = est.SourcePdfPath, UseShellExecute = true }); }
                catch { }
            };
            topActions.Children.Add(pdfBtn);

            // Copy button
            var copyBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { new FontIcon { Glyph = "\uE8C8", FontSize = 12 }, new TextBlock { Text = "Copy", FontSize = 11 } }
                },
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(AccentBlue),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6)
            };
            copyBtn.Click += (s, e) => CopyEstimateDetailsToClipboard(est);
            topActions.Children.Add(copyBtn);

            // Delete button
            var deleteBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { new FontIcon { Glyph = "\uE74D", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 100, 100)) }, new TextBlock { Text = "Delete", FontSize = 11 } }
                },
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 35, 35)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6)
            };
            deleteBtn.Click += async (s, e) =>
            {
                await ConfirmDeleteEstimateAsync(est);
                if (!EstimateHistoryDatabase.Instance.AllEstimates.Any(x => x.Id == est.Id))
                    HideEstimateDetail();
            };
            topActions.Children.Add(deleteBtn);

            Grid.SetColumn(topActions, 2);
            topBar.Children.Add(topActions);
            stack.Children.Add(topBar);

            // VIN
            if (!string.IsNullOrEmpty(est.VIN))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"VIN: {est.VIN}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextMuted),
                    Margin = new Thickness(0, -6, 0, 0)
                });
            }

            stack.Children.Add(BuildSectionDivider());

            // ── Customer + Insurance two-column ──
            var topGrid = new Grid { ColumnSpacing = 16 };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var custCol = BuildDetailColumn("CUSTOMER", new (string, string)[]
            {
                ("Name", est.CustomerName),
                ("Phone", est.CustomerPhone),
            });
            Grid.SetColumn(custCol, 0);
            topGrid.Children.Add(custCol);

            var insCol = BuildDetailColumn("INSURANCE", new (string, string)[]
            {
                ("Carrier", est.InsuranceCompany),
                ("Claim #", est.ClaimNumber),
                ("RO #", est.RONumber),
                ("Adjuster", est.AdjusterName),
                ("Adj. Phone", est.AdjusterPhone),
                ("Loss Date", est.LossDate?.ToString("M/d/yyyy") ?? ""),
                ("Deductible", est.DeductibleAmount > 0 ? $"${est.DeductibleAmount:N2}" : "")
            });
            Grid.SetColumn(insCol, 1);
            topGrid.Children.Add(insCol);
            stack.Children.Add(topGrid);

            stack.Children.Add(BuildSectionDivider());

            // ── Financial Breakdown ──
            stack.Children.Add(BuildFinancialBreakdownTable(est));

            stack.Children.Add(BuildSectionDivider());

            // ── Categorized Line Items ──
            if (est.LineItems.Count > 0)
            {
                var cats = CategorizeLineItems(est.LineItems);

                if (cats.OemParts.Count > 0)
                {
                    var oemTotal = cats.OemParts.Sum(li => li.Price);
                    stack.Children.Add(BuildCategoryExpander(
                        $"OEM PARTS ({cats.OemParts.Count} items)",
                        oemTotal > 0 ? $"${oemTotal:N0}" : null,
                        cats.OemParts, showPartNumber: true));
                }
                if (cats.Services.Count > 0)
                {
                    var svcTotal = cats.Services.Sum(li => li.Price);
                    stack.Children.Add(BuildCategoryExpander(
                        $"SERVICES & CALIBRATIONS ({cats.Services.Count} items)",
                        svcTotal > 0 ? $"${svcTotal:N0}" : null,
                        cats.Services, showPartNumber: false));
                }
                if (cats.Supplies.Count > 0)
                {
                    var supTotal = cats.Supplies.Sum(li => li.Price);
                    stack.Children.Add(BuildCategoryExpander(
                        $"SUPPLIES & MATERIALS ({cats.Supplies.Count} items)",
                        supTotal > 0 ? $"${supTotal:N0}" : null,
                        cats.Supplies, showPartNumber: false));
                }
                if (cats.LaborOps.Count > 0)
                {
                    // Group labor by category (Body, Refinish, Mechanical, Structural, Frame)
                    var laborByCategory = cats.LaborOps
                        .GroupBy(li => ClassifyLaborCategory(li))
                        .OrderBy(g => LaborCategoryOrder(g.Key))
                        .ToList();

                    // Container for the labor expanders (rebuilt when filter changes)
                    var laborContainer = new StackPanel { Spacing = 2 };

                    // Populate all categories initially
                    void PopulateLaborExpanders(string? filter)
                    {
                        laborContainer.Children.Clear();
                        foreach (var group in laborByCategory)
                        {
                            if (filter != null && group.Key != filter)
                                continue;
                            var groupItems = group.ToList();
                            var groupHrs = groupItems.Sum(li => li.LaborHours + li.RefinishHours);
                            var categoryLabel = group.Key.ToUpper();
                            var exp = BuildCategoryExpander(
                                $"{categoryLabel} ({groupItems.Count} items)",
                                groupHrs > 0 ? $"{groupHrs:N1} hrs" : null,
                                groupItems, showPartNumber: false);
                            if (filter != null) exp.IsExpanded = true;
                            laborContainer.Children.Add(exp);
                        }
                    }

                    // Build filter buttons if there are 2+ categories
                    if (laborByCategory.Count >= 2)
                    {
                        var filterBar = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 4,
                            Margin = new Thickness(0, 0, 0, 6)
                        };

                        string? activeFilter = null; // null = "All"
                        var filterButtons = new List<Button>();

                        void UpdateFilterButtonStyles()
                        {
                            foreach (var btn in filterButtons)
                            {
                                var tag = btn.Tag as string;
                                bool isActive = tag == activeFilter;
                                btn.Background = new SolidColorBrush(isActive ? AccentBlue : Color.FromArgb(255, 45, 45, 50));
                                btn.Foreground = new SolidColorBrush(isActive ? Colors.White : TextSecondary);
                            }
                        }

                        // "All" button
                        var allBtn = new Button
                        {
                            Content = new TextBlock { Text = "All", FontSize = 11 },
                            Padding = new Thickness(10, 4, 10, 4),
                            CornerRadius = new CornerRadius(4),
                            Tag = (string?)null
                        };
                        allBtn.Click += (s, e) => { activeFilter = null; UpdateFilterButtonStyles(); PopulateLaborExpanders(null); };
                        filterButtons.Add(allBtn);
                        filterBar.Children.Add(allBtn);

                        // Category buttons
                        foreach (var group in laborByCategory)
                        {
                            var catName = group.Key;
                            var shortLabel = catName.Replace(" Labor", "");
                            var catBtn = new Button
                            {
                                Content = new TextBlock { Text = shortLabel, FontSize = 11 },
                                Padding = new Thickness(10, 4, 10, 4),
                                CornerRadius = new CornerRadius(4),
                                Tag = catName
                            };
                            catBtn.Click += (s, e) =>
                            {
                                activeFilter = activeFilter == catName ? null : catName;
                                UpdateFilterButtonStyles();
                                PopulateLaborExpanders(activeFilter);
                            };
                            filterButtons.Add(catBtn);
                            filterBar.Children.Add(catBtn);
                        }

                        UpdateFilterButtonStyles();
                        stack.Children.Add(filterBar);
                    }

                    PopulateLaborExpanders(null);
                    stack.Children.Add(laborContainer);
                }
            }

            stack.Children.Add(BuildSectionDivider());

            // ── Footer ──
            stack.Children.Add(new TextBlock
            {
                Text = $"Source: {(string.IsNullOrEmpty(est.SourceFile) ? "(unknown)" : est.SourceFile)}  •  Imported {est.ImportedDate:M/d/yyyy}  •  Quality: {est.QualityGrade}",
                FontSize = 10,
                Foreground = new SolidColorBrush(TextMuted),
                TextWrapping = TextWrapping.Wrap
            });

            scroller.Content = stack;
            _estimateDetailPanel.Children.Add(scroller);
        }

        private void HideEstimateDetail()
        {
            if (_estimateDetailPanel != null)
            {
                _estimateDetailPanel.Children.Clear();
                _estimateDetailPanel.Visibility = Visibility.Collapsed;
            }
            if (_estimateListCard != null)
                _estimateListCard.Visibility = Visibility.Visible;
        }

        /// <summary>Financial breakdown table: Parts, Body Labor, Paint Labor, Mechanical, Grand Total</summary>
        private StackPanel BuildFinancialBreakdownTable(StoredEstimate est)
        {
            var col = new StackPanel { Spacing = 4 };
            col.Children.Add(new TextBlock
            {
                Text = "FINANCIAL BREAKDOWN",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextMuted),
                Margin = new Thickness(0, 0, 0, 4)
            });

            void AddFinRow(string label, string value, bool bold = false, Color? fg = null)
            {
                if (string.IsNullOrEmpty(value) || value == "$0.00") return;
                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var l = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = new SolidColorBrush(fg ?? TextPrimary)
                };
                rowGrid.Children.Add(l);
                var v = new TextBlock
                {
                    Text = value,
                    FontSize = 12,
                    FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = new SolidColorBrush(fg ?? TextPrimary),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(v, 1);
                rowGrid.Children.Add(v);
                col.Children.Add(rowGrid);
            }

            if (est.PartsTotal > 0)
                AddFinRow("Parts", $"${est.PartsTotal:N2}");

            // Calculate hours per labor category using the same classifier as the expanders
            decimal bodyHrs = 0, refinishHrs = 0, mechHrs = 0, frameHrs = 0, structHrs = 0;
            foreach (var li in est.LineItems)
            {
                var cat = ClassifyLaborCategory(li);
                var hrs = li.LaborHours + li.RefinishHours;
                switch (cat)
                {
                    case "Body Labor": bodyHrs += hrs; break;
                    case "Refinish Labor": refinishHrs += hrs; break;
                    case "Mechanical Labor": mechHrs += hrs; break;
                    case "Frame Labor": frameHrs += hrs; break;
                    case "Structural Labor": structHrs += hrs; break;
                }
            }

            if (bodyHrs > 0 || est.BodyHourlyRate > 0)
            {
                var rate = est.BodyHourlyRate > 0 ? est.BodyHourlyRate : est.LaborHourlyRate;
                var amount = bodyHrs * rate;
                var detail = rate > 0 ? $"{bodyHrs:N1} hrs @ ${rate:N2}/hr" : $"{bodyHrs:N1} hrs";
                AddFinRow($"Body Labor        {detail}", amount > 0 ? $"${amount:N2}" : "");
            }
            if (refinishHrs > 0 || est.RefinishHourlyRate > 0)
            {
                var rate = est.RefinishHourlyRate;
                var amount = refinishHrs * rate;
                var detail = rate > 0 ? $"{refinishHrs:N1} hrs @ ${rate:N2}/hr" : $"{refinishHrs:N1} hrs";
                AddFinRow($"Paint Labor       {detail}", amount > 0 ? $"${amount:N2}" : "");
            }
            if (mechHrs > 0 || est.MechanicalHourlyRate > 0)
            {
                var rate = est.MechanicalHourlyRate;
                var amount = mechHrs * rate;
                var detail = rate > 0 ? $"{mechHrs:N1} hrs @ ${rate:N2}/hr" : $"{mechHrs:N1} hrs";
                AddFinRow($"Mechanical Labor  {detail}", amount > 0 ? $"${amount:N2}" : "");
            }
            if (frameHrs > 0 || est.FrameHourlyRate > 0)
            {
                var rate = est.FrameHourlyRate;
                var amount = frameHrs * rate;
                var detail = rate > 0 ? $"{frameHrs:N1} hrs @ ${rate:N2}/hr" : $"{frameHrs:N1} hrs";
                AddFinRow($"Frame Labor       {detail}", amount > 0 ? $"${amount:N2}" : "");
            }
            if (structHrs > 0)
            {
                var rate = est.BodyHourlyRate > 0 ? est.BodyHourlyRate : est.LaborHourlyRate;
                var amount = structHrs * rate;
                var detail = rate > 0 ? $"{structHrs:N1} hrs @ ${rate:N2}/hr" : $"{structHrs:N1} hrs";
                AddFinRow($"Structural Labor  {detail}", amount > 0 ? $"${amount:N2}" : "");
            }

            // Divider line
            col.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(BorderSubtle), Margin = new Thickness(0, 2, 0, 2) });

            var grandTotal = ComputeGrandTotal(est);
            AddFinRow("Grand Total", $"${grandTotal:N2}", bold: true, fg: AccentGreen);

            return col;
        }

        /// <summary>Build a categorized line-items expander with a table inside</summary>
        private Expander BuildCategoryExpander(string headerText, string? rightValue, List<StoredLineItem> items, bool showPartNumber)
        {
            // Header: label on left, value on right
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            headerGrid.Children.Add(new TextBlock
            {
                Text = headerText,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextMuted)
            });
            if (!string.IsNullOrEmpty(rightValue))
            {
                var rv = new TextBlock
                {
                    Text = rightValue,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(AccentGreen),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(rv, 1);
                headerGrid.Children.Add(rv);
            }

            var expander = new Expander
            {
                Header = headerGrid,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            expander.Content = BuildLineItemsTable(items, showPartNumber);
            return expander;
        }

        /// <summary>Build a table grid of line items for use inside an expander</summary>
        private StackPanel BuildLineItemsTable(List<StoredLineItem> items, bool showPartNumber)
        {
            var panel = new StackPanel { Spacing = 2 };

            // Header row
            var hdr = new Grid { Padding = new Thickness(4, 4, 4, 4) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });  // OP
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // DESC
            if (showPartNumber)
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // PART#
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });  // QTY
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // PRICE
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });  // HOURS

            int c = 0;
            void HdrCell(string text, int col) { var t = new TextBlock { Text = text, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = TextMutedBrush }; Grid.SetColumn(t, col); hdr.Children.Add(t); }
            HdrCell("OP", c++);
            HdrCell("DESCRIPTION", c++);
            if (showPartNumber) HdrCell("PART #", c++);
            HdrCell("QTY", c++);
            HdrCell("PRICE", c++);
            HdrCell("HOURS", c++);
            panel.Children.Add(hdr);

            bool alt = false;
            foreach (var li in items)
            {
                var row = new Grid
                {
                    Padding = new Thickness(4, 3, 4, 3),
                    Background = new SolidColorBrush(alt ? Color.FromArgb(255, 30, 30, 34) : Colors.Transparent)
                };
                // Same column defs
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (showPartNumber)
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

                c = 0;
                void RowCell(string text, int col, SolidColorBrush fg)
                {
                    var t = new TextBlock { Text = text, FontSize = 11, Foreground = fg, TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(t, col);
                    row.Children.Add(t);
                }

                // OP type abbreviation
                var opAbbr = (li.OperationType ?? "").Length > 5 ? li.OperationType.Substring(0, 5) : (li.OperationType ?? "");
                RowCell(opAbbr, c++, TextSecondaryBrush);
                RowCell(li.Description ?? li.PartName ?? "", c++, TextPrimaryBrush);
                if (showPartNumber)
                    RowCell(li.PartNumber ?? "", c++, TextMutedBrush);
                RowCell(li.Quantity > 1 ? li.Quantity.ToString() : "", c++, TextSecondaryBrush);
                RowCell(li.Price > 0 ? $"${li.Price:N0}" : "", c++, AccentGreenBrush);
                var hours = li.LaborHours + li.RefinishHours;
                RowCell(hours > 0 ? $"{hours:N1}" : (li.Price > 0 ? "Incl." : ""), c++, TextSecondaryBrush);

                panel.Children.Add(row);
                alt = !alt;
            }

            return panel;
        }

        private Border BuildSectionDivider() => new Border
        {
            Height = 1,
            Background = new SolidColorBrush(BorderSubtle),
            Margin = new Thickness(0, 4, 0, 4)
        };

        private StackPanel BuildDetailColumn(string title, IEnumerable<(string Label, string Value)> rows)
        {
            var col = new StackPanel { Spacing = 4 };
            col.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextMuted),
                Margin = new Thickness(0, 0, 0, 2)
            });
            foreach (var (label, value) in rows)
            {
                if (string.IsNullOrEmpty(value)) continue;
                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var l = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(TextMuted) };
                Grid.SetColumn(l, 0); rowGrid.Children.Add(l);
                var v = new TextBlock { Text = value, FontSize = 12, Foreground = new SolidColorBrush(TextPrimary), TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(v, 1); rowGrid.Children.Add(v);
                col.Children.Add(rowGrid);
            }
            return col;
        }

        private void CopyEstimateDetailsToClipboard(StoredEstimate est)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(est.VehicleInfo);
                if (!string.IsNullOrEmpty(est.VIN)) sb.AppendLine($"VIN: {est.VIN}");
                sb.AppendLine();
                if (!string.IsNullOrEmpty(est.CustomerName)) sb.AppendLine($"Customer: {est.CustomerName}");
                if (!string.IsNullOrEmpty(est.CustomerPhone)) sb.AppendLine($"Phone: {est.CustomerPhone}");
                if (!string.IsNullOrEmpty(est.InsuranceCompany)) sb.AppendLine($"Insurance: {est.InsuranceCompany}");
                if (!string.IsNullOrEmpty(est.ClaimNumber)) sb.AppendLine($"Claim #: {est.ClaimNumber}");
                if (!string.IsNullOrEmpty(est.RONumber)) sb.AppendLine($"RO #: {est.RONumber}");
                if (!string.IsNullOrEmpty(est.AdjusterName)) sb.AppendLine($"Adjuster: {est.AdjusterName}");
                if (!string.IsNullOrEmpty(est.AdjusterPhone)) sb.AppendLine($"Adjuster Phone: {est.AdjusterPhone}");
                if (est.LossDate.HasValue) sb.AppendLine($"Loss Date: {est.LossDate:M/d/yyyy}");
                if (est.DeductibleAmount > 0) sb.AppendLine($"Deductible: ${est.DeductibleAmount:N2}");

                // Financial breakdown
                sb.AppendLine();
                sb.AppendLine("── Financial Breakdown ──");
                if (est.PartsTotal > 0) sb.AppendLine($"Parts:              ${est.PartsTotal:N2}");

                decimal bodyHrs = 0, refinishHrs = 0, mechHrs = 0;
                foreach (var li in est.LineItems)
                {
                    bodyHrs += li.LaborHours;
                    refinishHrs += li.RefinishHours;
                    if (li.LaborType.IndexOf("mech", StringComparison.OrdinalIgnoreCase) >= 0)
                        mechHrs += li.LaborHours;
                }
                bodyHrs -= mechHrs;
                if (bodyHrs < 0) bodyHrs = 0;

                if (bodyHrs > 0)
                {
                    var rate = est.BodyHourlyRate > 0 ? est.BodyHourlyRate : est.LaborHourlyRate;
                    sb.AppendLine($"Body Labor:         {bodyHrs:N1} hrs{(rate > 0 ? $" @ ${rate:N2}/hr = ${bodyHrs * rate:N2}" : "")}");
                }
                if (refinishHrs > 0)
                {
                    var rate = est.RefinishHourlyRate;
                    sb.AppendLine($"Paint Labor:        {refinishHrs:N1} hrs{(rate > 0 ? $" @ ${rate:N2}/hr = ${refinishHrs * rate:N2}" : "")}");
                }
                if (mechHrs > 0)
                {
                    var rate = est.MechanicalHourlyRate;
                    sb.AppendLine($"Mechanical Labor:   {mechHrs:N1} hrs{(rate > 0 ? $" @ ${rate:N2}/hr = ${mechHrs * rate:N2}" : "")}");
                }

                var grandTotal = ComputeGrandTotal(est);
                sb.AppendLine($"Grand Total:        ${grandTotal:N2}");

                // Categorized line items
                if (est.LineItems.Count > 0)
                {
                    var cats = CategorizeLineItems(est.LineItems);
                    void AppendCategory(string title, List<StoredLineItem> items)
                    {
                        if (items.Count == 0) return;
                        sb.AppendLine();
                        sb.AppendLine($"── {title} ({items.Count}) ──");
                        foreach (var li in items)
                        {
                            var hours = li.LaborHours + li.RefinishHours;
                            var parts = new List<string>();
                            if (!string.IsNullOrEmpty(li.OperationType)) parts.Add(li.OperationType.PadRight(6));
                            parts.Add(li.Description ?? li.PartName ?? "");
                            if (!string.IsNullOrEmpty(li.PartNumber)) parts.Add(li.PartNumber);
                            if (li.Quantity > 1) parts.Add($"x{li.Quantity}");
                            if (li.Price > 0) parts.Add($"${li.Price:N0}");
                            if (hours > 0) parts.Add($"{hours:N1}h");
                            sb.AppendLine("  " + string.Join("  ", parts));
                        }
                    }
                    AppendCategory("OEM Parts", cats.OemParts);
                    AppendCategory("Services & Calibrations", cats.Services);
                    AppendCategory("Supplies & Materials", cats.Supplies);
                    AppendCategory("Labor Operations", cats.LaborOps);
                }

                var pkg = new DataPackage();
                pkg.SetText(sb.ToString());
                Clipboard.SetContent(pkg);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EstimateBuilderPanel] Copy details failed: {ex.Message}");
            }
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

            bool foundDirectResults = false;

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
                foundDirectResults = true;
            }

            if (!foundDirectResults)
            {
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
                    foundDirectResults = true;
                }
                else if (allPatterns.Count > 1)
                {
                    _statusText!.Text = $"Found {allPatterns.Count} patterns matching \"{partName}\"";
                    _statusText.Foreground = new SolidColorBrush(AccentBlue);
                    AddSectionHeader("SELECT A PATTERN");
                    foreach (var p in allPatterns)
                        _resultsPanel.Children.Add(CreatePatternCard(p));
                    foundDirectResults = true;
                }
            }

            if (!foundDirectResults)
            {
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
                    foundDirectResults = true;
                }
            }

            // Always show co-occurring operations from learned estimates
            // "What else typically appears on estimates that include [partName]?"
            AppendCoOccurringOperations(partName, opType);

            if (!foundDirectResults && _resultsPanel.Children.Count == 0)
            {
                var source = _viewPersonalOnly ? "your imports" : "learned data";
                _statusText!.Text = $"No results for \"{partName}\" in {source}";
                _statusText.Foreground = new SolidColorBrush(AccentOrange);
                ShowEmptyState();
            }

            UpdateButtonStates();
        }

        // Section colors for co-occurring operations (matches GhostEstimatePanel)
        private static readonly Dictionary<string, Color> CoOccSectionColors = new()
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

        // CCC section display order
        private static readonly string[] CoOccSectionOrder = new[]
        {
            "FRONT BUMPER & GRILLE", "REAR BUMPER",
            "FRONT LAMPS", "REAR LAMPS",
            "RADIATOR SUPPORT", "HOOD", "FENDER",
            "FRONT DOOR", "REAR DOOR",
            "QUARTER PANEL", "PILLARS, ROCKER & FLOOR", "ROOF",
            "TRUNK / DECKLID", "GLASS", "FRAME",
            "RESTRAINT SYSTEMS", "ELECTRICAL", "INSTRUMENT PANEL",
            "VEHICLE DIAGNOSTICS", "MECHANICAL", "MISCELLANEOUS OPERATIONS"
        };

        /// <summary>
        /// Appends co-occurring operations from learned estimates to the results panel,
        /// grouped by CCC section with colored headers.
        /// </summary>
        private void AppendCoOccurringOperations(string partName, string? opType)
        {
            try
            {
                // Get co-occurring operations (what else appears on estimates with this part)
                var related = _learningService.GetRelatedOperations(partName, opType ?? "", 30);

                // Also try without the operation type filter for broader results
                if (related.Count < 5 && !string.IsNullOrEmpty(opType))
                {
                    var broaderRelated = _learningService.GetRelatedOperations(partName, "", 30);
                    var existingKeys = related.Select(r => $"{r.PartName}|{r.OperationType}".ToLowerInvariant()).ToHashSet();
                    foreach (var entry in broaderRelated)
                    {
                        if (!existingKeys.Contains($"{entry.PartName}|{entry.OperationType}".ToLowerInvariant()))
                            related.Add(entry);
                    }
                }

                if (related.Count == 0) return;

                // Separator
                _resultsPanel!.Children.Add(new Border
                {
                    Height = 1, Background = new SolidColorBrush(BorderSubtle),
                    Margin = new Thickness(0, 12, 0, 4)
                });

                // Main header
                var headerRow = new Grid { Margin = new Thickness(0, 4, 0, 8) };
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var headerStack = new StackPanel { Spacing = 2 };
                headerStack.Children.Add(new TextBlock
                {
                    Text = $"FROM ESTIMATES WITH {partName.ToUpper()}",
                    FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(AccentTeal), CharacterSpacing = 60
                });
                headerStack.Children.Add(new TextBlock
                {
                    Text = $"Operations that commonly appear alongside {partName} on learned estimates",
                    FontSize = 11, Foreground = new SolidColorBrush(TextSecondary)
                });
                Grid.SetColumn(headerStack, 0);
                headerRow.Children.Add(headerStack);

                // Add All button
                var addAllCoBtn = new Button
                {
                    FontSize = 11, Padding = new Thickness(12, 5, 12, 5),
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 80, 50)),
                    Foreground = new SolidColorBrush(AccentGreen), CornerRadius = new CornerRadius(6),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var addAllCoContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                addAllCoContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 11 });
                addAllCoContent.Children.Add(new TextBlock { Text = "Add All", FontSize = 11 });
                addAllCoBtn.Content = addAllCoContent;
                var capturedRelated = related;
                addAllCoBtn.Click += (s, e) =>
                {
                    foreach (var entry in capturedRelated)
                    {
                        _estimateLines.Add(new EstimateLine
                        {
                            PartName = entry.PartName,
                            OperationType = entry.OperationType,
                            LaborHours = entry.AvgLaborHours,
                            RefinishHours = entry.AvgRefinishHours,
                            Price = entry.AvgPrice
                        });
                    }
                    UpdateEstimateDisplay();
                    _statusText!.Text = $"Added {capturedRelated.Count} co-occurring operations";
                    _statusText.Foreground = new SolidColorBrush(AccentGreen);
                };
                Grid.SetColumn(addAllCoBtn, 1);
                headerRow.Children.Add(addAllCoBtn);
                _resultsPanel.Children.Add(headerRow);

                // Group operations by CCC section
                var grouped = related
                    .GroupBy(e => GhostEstimateService.MapToCCCSection(e.PartName))
                    .OrderBy(g => Array.IndexOf(CoOccSectionOrder, g.Key) is int idx && idx >= 0 ? idx : 99)
                    .ToList();

                foreach (var sectionGroup in grouped)
                {
                    var sectionName = sectionGroup.Key;
                    var sectionOps = sectionGroup.OrderByDescending(e => e.CoOccurrenceRate).ToList();
                    var sectionColor = CoOccSectionColors.GetValueOrDefault(sectionName, Color.FromArgb(255, 150, 155, 160));

                    // Section header bar
                    var sectionHeader = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 48)),
                        BorderBrush = new SolidColorBrush(sectionColor),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 6, 0, 2)
                    };

                    var sectionHeaderGrid = new Grid();
                    sectionHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    sectionHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    sectionHeaderGrid.Children.Add(new TextBlock
                    {
                        Text = sectionName,
                        FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(sectionColor),
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    // Section summary (ops count + total hours)
                    var sectionHours = sectionOps.Sum(o => o.AvgLaborHours + o.AvgRefinishHours);
                    var summaryText = new TextBlock
                    {
                        Text = $"{sectionOps.Count} ops  •  {sectionHours:N1}h",
                        FontSize = 10, Foreground = new SolidColorBrush(TextMuted),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(summaryText, 1);
                    sectionHeaderGrid.Children.Add(summaryText);

                    sectionHeader.Child = sectionHeaderGrid;
                    _resultsPanel.Children.Add(sectionHeader);

                    // Operation cards for this section
                    foreach (var entry in sectionOps)
                    {
                        _resultsPanel.Children.Add(CreateRelatedOperationCard(entry));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EstimateBuilder] Error loading co-occurring operations: {ex.Message}");
            }
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

            // Co-occurring operations are now appended by DoLookup() via AppendCoOccurringOperations()

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
        //  COMPONENT SEARCH VIEW
        // ═══════════════════════════════════════════

        private void BuildComponentSearchView()
        {
            _componentsContentGrid = new Grid { Visibility = Visibility.Visible, Margin = new Thickness(16, 0, 16, 14) };
            _componentsContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // toolbar
            _componentsContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // results

            // Toolbar: search + category filter + results count
            var toolbar = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 8) };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _componentSearchBox = new TextBox
            {
                PlaceholderText = "Search parts, services, supplies by name or part #...",
                FontSize = 13,
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(TextPrimary),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                CornerRadius = new CornerRadius(8)
            };
            _componentSearchBox.TextChanged += (s, e) => DebounceComponentSearch();
            Grid.SetColumn(_componentSearchBox, 0);
            toolbar.Children.Add(_componentSearchBox);

            _componentCategoryFilter = new ComboBox
            {
                FontSize = 13,
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(TextPrimary),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new[] { "All Categories", "OEM Parts", "Services", "Supplies", "Labor" },
                SelectedIndex = 0
            };
            _componentCategoryFilter.SelectionChanged += (s, e) => RunComponentSearch();
            Grid.SetColumn(_componentCategoryFilter, 1);
            toolbar.Children.Add(_componentCategoryFilter);

            _componentResultsCount = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0)
            };
            Grid.SetColumn(_componentResultsCount, 2);
            toolbar.Children.Add(_componentResultsCount);

            Grid.SetRow(toolbar, 0);
            _componentsContentGrid.Children.Add(toolbar);

            // Results area
            var resultsCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 32)),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1)
            };
            var resultsScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _componentResultsPanel = new StackPanel { Spacing = 0, Padding = new Thickness(4) };

            // Initial empty state
            _componentResultsPanel.Children.Add(new TextBlock
            {
                Text = "Type at least 2 characters to search across all imported estimates.",
                FontSize = 12,
                Foreground = TextMutedBrush,
                Margin = new Thickness(16, 24, 16, 24),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            resultsScroller.Content = _componentResultsPanel;
            resultsCard.Child = resultsScroller;
            Grid.SetRow(resultsCard, 1);
            _componentsContentGrid.Children.Add(resultsCard);

            // Add to parent grid (same row as estimates)
            var mainGrid = (Grid)Content;
            Grid.SetRow(_componentsContentGrid, 2);
            Grid.SetRowSpan(_componentsContentGrid, 2);
            mainGrid.Children.Add(_componentsContentGrid);
        }

        private void DebounceComponentSearch()
        {
            _componentSearchCts?.Cancel();
            _componentSearchCts = new System.Threading.CancellationTokenSource();
            var token = _componentSearchCts.Token;

            _ = System.Threading.Tasks.Task.Delay(300, token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                McstudDesktop.App.MainDispatcherQueue?.TryEnqueue(() =>
                {
                    if (!token.IsCancellationRequested)
                        RunComponentSearch();
                });
            });
        }

        private void RunComponentSearch()
        {
            if (_componentResultsPanel == null || _componentSearchBox == null || _componentCategoryFilter == null) return;

            var query = _componentSearchBox.Text?.Trim() ?? "";
            var categoryIdx = _componentCategoryFilter.SelectedIndex;

            _componentResultsPanel.Children.Clear();

            if (query.Length < 2)
            {
                _componentResultsPanel.Children.Add(new TextBlock
                {
                    Text = "Type at least 2 characters to search across all imported estimates.",
                    FontSize = 12,
                    Foreground = TextMutedBrush,
                    Margin = new Thickness(16, 24, 16, 24),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                if (_componentResultsCount != null) _componentResultsCount.Text = "";
                return;
            }

            var allEstimates = EstimateHistoryDatabase.Instance.GetEstimatesForCurrentUser();
            var matchGroups = new List<(StoredEstimate Est, List<StoredLineItem> Matches)>();
            int totalMatches = 0;

            foreach (var est in allEstimates)
            {
                var cats = CategorizeLineItems(est.LineItems);

                // Filter by category
                IEnumerable<StoredLineItem> pool = categoryIdx switch
                {
                    1 => cats.OemParts,
                    2 => cats.Services,
                    3 => cats.Supplies,
                    4 => cats.LaborOps,
                    _ => est.LineItems
                };

                var matches = pool.Where(li =>
                    Contains(li.Description, query) ||
                    Contains(li.PartName, query) ||
                    Contains(li.PartNumber, query)
                ).ToList();

                if (matches.Count > 0)
                {
                    matchGroups.Add((est, matches));
                    totalMatches += matches.Count;
                }
            }

            if (_componentResultsCount != null)
                _componentResultsCount.Text = $"{totalMatches} match{(totalMatches == 1 ? "" : "es")} in {matchGroups.Count} estimate{(matchGroups.Count == 1 ? "" : "s")}";

            if (matchGroups.Count == 0)
            {
                _componentResultsPanel.Children.Add(new TextBlock
                {
                    Text = $"No components match \"{query}\".",
                    FontSize = 12,
                    Foreground = TextMutedBrush,
                    Margin = new Thickness(16, 24, 16, 24),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            // Render grouped results
            foreach (var (est, matches) in matchGroups)
            {
                RenderComponentResultGroup(est, matches);
            }
        }

        private void RenderComponentResultGroup(StoredEstimate est, List<StoredLineItem> matches)
        {
            if (_componentResultsPanel == null) return;

            var groupBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 34)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2, 2, 2, 4),
                Padding = new Thickness(10, 8, 10, 8)
            };

            var groupStack = new StackPanel { Spacing = 4 };

            // Estimate header — clickable to navigate to detail
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var vehicleLabel = est.VehicleInfo ?? "(Unknown)";
            var headerParts = new List<string> { vehicleLabel };
            if (est.ImportedDate != default) headerParts.Add(est.ImportedDate.ToString("M/d/yy"));
            if (!string.IsNullOrEmpty(est.InsuranceCompany)) headerParts.Add(est.InsuranceCompany);

            var headerText = new TextBlock
            {
                Text = string.Join("  \u2022  ", headerParts),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentBlue),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            headerRow.Children.Add(headerText);

            var viewBtn = new TextBlock
            {
                Text = "\uE76C", // ChevronRight
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = new SolidColorBrush(TextMuted),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(viewBtn, 1);
            headerRow.Children.Add(viewBtn);

            // Make header clickable — navigate to estimates tab + show detail
            var headerBorder = new Border { Background = TransparentBrush, Padding = new Thickness(0, 2, 0, 4) };
            headerBorder.Child = headerRow;
            headerBorder.PointerEntered += (s, e) => headerText.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
            headerBorder.PointerExited += (s, e) => headerText.TextDecorations = Windows.UI.Text.TextDecorations.None;
            headerBorder.Tapped += (s, e) =>
            {
                // Switch to Estimates tab and show this estimate's detail
                _viewMode = LearnedViewMode.Operations; // force re-entry
                SetViewMode(LearnedViewMode.Estimates);
                ShowEstimateDetails(est, _estimateListCard ?? (FrameworkElement)this);
            };
            groupStack.Children.Add(headerBorder);

            // Matching line items
            bool alt = false;
            foreach (var li in matches)
            {
                var row = new Grid
                {
                    Padding = new Thickness(8, 2, 8, 2),
                    Background = new SolidColorBrush(alt ? Color.FromArgb(255, 36, 36, 40) : Colors.Transparent),
                    Margin = new Thickness(8, 0, 0, 0)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); // OP
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // DESC
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); // PART#
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) }); // QTY
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) }); // PRICE/HOURS

                int c = 0;
                void Cell(string text, int col, SolidColorBrush fg)
                {
                    var t = new TextBlock { Text = text, FontSize = 11, Foreground = fg, TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(t, col);
                    row.Children.Add(t);
                }

                var opAbbr = (li.OperationType ?? "").Length > 5 ? li.OperationType.Substring(0, 5) : (li.OperationType ?? "");
                Cell(opAbbr, c++, TextMutedBrush);
                Cell(li.Description ?? li.PartName ?? "", c++, TextPrimaryBrush);
                Cell(li.PartNumber ?? "", c++, TextMutedBrush);
                Cell(li.Quantity > 1 ? li.Quantity.ToString() : "", c++, TextSecondaryBrush);

                var hours = li.LaborHours + li.RefinishHours;
                var priceOrHours = li.Price > 0 ? $"${li.Price:N0}" : (hours > 0 ? $"{hours:N1} hrs" : "");
                Cell(priceOrHours, c++, li.Price > 0 ? AccentGreenBrush : TextSecondaryBrush);

                groupStack.Children.Add(row);
                alt = !alt;
            }

            groupBorder.Child = groupStack;
            _componentResultsPanel.Children.Add(groupBorder);
        }

        // ═══════════════════════════════════════════
        //  STATIC HELPERS: Grand Total & Line Item Categorization
        // ═══════════════════════════════════════════

        /// <summary>
        /// Compute a reliable grand total for display. Falls back to summing components
        /// when the stored GrandTotal looks like it's actually just PartsTotal.
        /// </summary>
        private static decimal ComputeGrandTotal(StoredEstimate est)
        {
            // If GrandTotal is set, non-zero, and different from PartsTotal, trust it
            if (est.GrandTotal > 0 && est.GrandTotal != est.PartsTotal)
                return est.GrandTotal;

            // Compute from components
            decimal total = est.PartsTotal;

            // Sum labor from rates × hours if we have them
            decimal bodyHrs = 0, refinishHrs = 0, mechHrs = 0;
            foreach (var li in est.LineItems)
            {
                bodyHrs += li.LaborHours;
                refinishHrs += li.RefinishHours;
            }
            // Mechanical hours are lines with LaborType containing "mech"
            mechHrs = est.LineItems
                .Where(li => li.LaborType.IndexOf("mech", StringComparison.OrdinalIgnoreCase) >= 0)
                .Sum(li => li.LaborHours);
            // Body hours = total labor hours minus mechanical
            bodyHrs -= mechHrs;
            if (bodyHrs < 0) bodyHrs = 0;

            if (est.BodyHourlyRate > 0)
                total += bodyHrs * est.BodyHourlyRate;
            else if (est.LaborHourlyRate > 0)
                total += bodyHrs * est.LaborHourlyRate;

            if (est.RefinishHourlyRate > 0)
                total += refinishHrs * est.RefinishHourlyRate;

            if (est.MechanicalHourlyRate > 0)
                total += mechHrs * est.MechanicalHourlyRate;

            // Add manual line prices (services, supplies) that aren't already in PartsTotal
            decimal manualPrices = est.LineItems
                .Where(li => li.IsManualLine && li.Price > 0)
                .Sum(li => li.Price);
            total += manualPrices;

            return total > 0 ? total : est.GrandTotal;
        }

        private record CategorizedLineItems(
            List<StoredLineItem> OemParts,
            List<StoredLineItem> Services,
            List<StoredLineItem> Supplies,
            List<StoredLineItem> LaborOps);

        private static CategorizedLineItems CategorizeLineItems(IList<StoredLineItem> items)
        {
            var oem = new List<StoredLineItem>();
            var services = new List<StoredLineItem>();
            var supplies = new List<StoredLineItem>();
            var labor = new List<StoredLineItem>();

            foreach (var li in items)
            {
                if (!string.IsNullOrEmpty(li.PartNumber) && !li.IsManualLine)
                    oem.Add(li);
                else if (IsServiceLine(li))
                    services.Add(li);
                else if (IsSupplyLine(li))
                    supplies.Add(li);
                else
                    labor.Add(li);
            }
            return new CategorizedLineItems(oem, services, supplies, labor);
        }

        private static readonly string[] ServiceKeywords = {
            "scan", "calibrat", "adas", "research", "diagnostic",
            "alignment", "measure", "aim", "recalibrat", "pre-scan",
            "post-scan", "inspection", "blueprint"
        };

        private static bool IsServiceLine(StoredLineItem li)
        {
            if (!li.IsManualLine) return false;
            var desc = (li.Description ?? li.PartName ?? "").ToLowerInvariant();
            return ServiceKeywords.Any(kw => desc.Contains(kw));
        }

        private static readonly string[] SupplyKeywords = {
            "flex add", "paint material", "wax", "disposal", "adhesion",
            "corrosion", "primer", "cavity wax", "inventory", "ipa",
            "static gun", "glass cleaner", "hardware", "cover car",
            "mask", "buff", "sand", "tint", "spray card", "de-nib",
            "seam seal", "weld-thru", "weld thru", "anti-corrosion",
            "sound deadener", "undercoat", "body filler", "pinch weld"
        };

        private static bool IsSupplyLine(StoredLineItem li)
        {
            if (!li.IsManualLine) return false;
            if (IsServiceLine(li)) return false; // services take priority
            var desc = (li.Description ?? li.PartName ?? "").ToLowerInvariant();
            if (SupplyKeywords.Any(kw => desc.Contains(kw)))
                return true;
            // Manual lines with price but no hours are typically supplies
            if (li.Price > 0 && li.LaborHours == 0 && li.RefinishHours == 0)
                return true;
            return false;
        }

        /// <summary>Classify a labor line item into its labor category for display grouping</summary>
        private static string ClassifyLaborCategory(StoredLineItem li)
        {
            // Explicit LaborType from parser takes priority
            var lt = (li.LaborType ?? "").Trim();
            if (lt.Equals("Mechanical", StringComparison.OrdinalIgnoreCase) ||
                lt.IndexOf("mech", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Mechanical Labor";
            if (lt.Equals("Structural", StringComparison.OrdinalIgnoreCase))
                return "Structural Labor";
            if (lt.Equals("Frame", StringComparison.OrdinalIgnoreCase))
                return "Frame Labor";
            if (lt.Equals("Refinish", StringComparison.OrdinalIgnoreCase))
                return "Refinish Labor";

            // Infer from operation type or hours
            var op = (li.OperationType ?? "").ToLowerInvariant();
            if (op == "refn" || op == "refinish" || op == "blnd" || op == "blend")
                return "Refinish Labor";
            if (li.RefinishHours > 0 && li.LaborHours == 0)
                return "Refinish Labor";

            // Default to Body Labor
            if (lt.Equals("Body", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(lt))
                return "Body Labor";

            return "Body Labor";
        }

        /// <summary>Sort order for labor category groups</summary>
        private static int LaborCategoryOrder(string category) => category switch
        {
            "Body Labor" => 0,
            "Refinish Labor" => 1,
            "Mechanical Labor" => 2,
            "Frame Labor" => 3,
            "Structural Labor" => 4,
            _ => 5
        };

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
