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

        // Browse column UI
        private TextBox? _searchBox;
        private Button? _clearSearchButton;
        private StackPanel? _resultsPanel;
        private TextBlock? _statusText;
        private TextBlock? _selectionCountText;
        private Button? _insertButton;
        private Button? _copyButton;
        private ScrollViewer? _resultsScroller;

        // Stats card refs
        private TextBlock? _statEstimates;
        private TextBlock? _statPatterns;
        private TextBlock? _statUniqueOps;
        private TextBlock? _statTotalHours;
        private TextBlock? _statAvgOps;

        // Category navigation
        private Border? _allTab;
        private Border? _byPartTab;
        private Border? _byOpTab;
        private Border? _recentTab;
        private int _selectedCategory = 0;

        // Quick filter chips
        private StackPanel? _quickFiltersPanel;
        private string? _activeQuickFilter;

        // Current search results
        private ManualLinePattern? _currentPattern;
        private OperationAggregateStats? _currentOpStats;
        private List<ManualLineEntry> _selectedOperations = new();
        private List<OperationSearchResult> _selectedOpResults = new();

        // Cart column UI
        private ListView? _estimateView;
        private TextBlock? _totalLabor;
        private TextBlock? _totalRefinish;
        private TextBlock? _totalPrice;
        private TextBlock? _lineCount;
        private StackPanel? _relatedOpsPanel;
        private StackPanel? _advisorSuggestionsPanel;

        // Cart data
        private List<EstimateLine> _estimateLines = new();

        // Mode indicator
        private TextBlock? _modeBadgeText;
        private Border? _modeBadge;

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

            try
            {
                BuildUI();
                RefreshStatsValues();
                ShowCategoryContent(0);
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
            ShowCategoryContent(_selectedCategory);
        }

        private void RefreshStatsValues()
        {
            var allPatterns = _learningService.GetAllManualLinePatterns()
                .Where(p => p.ManualLines.Count > 0).ToList();
            if (_statEstimates != null) _statEstimates.Text = _learningService.EstimatesImported.ToString();
            if (_statPatterns != null) _statPatterns.Text = _learningService.ManualLinePatternCount.ToString();
            if (_statUniqueOps != null) _statUniqueOps.Text = allPatterns.SelectMany(p => p.ManualLines).Select(m => m.ManualLineType).Distinct().Count().ToString();
            if (_statTotalHours != null) _statTotalHours.Text = $"{allPatterns.SelectMany(p => p.ManualLines).Sum(m => m.LaborUnits + m.RefinishUnits):N1}";
            if (_statAvgOps != null)
            {
                var patterns = _learningService.ManualLinePatternCount;
                var avgOps = patterns > 0 ? (double)allPatterns.Sum(p => p.ManualLines.Count) / patterns : 0;
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

            var stats = BuildStatsCards();
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
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            titleRow.Children.Add(new TextBlock
            {
                Text = "Estimate Builder",
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
            stack.Children.Add(titleRow);
            stack.Children.Add(new TextBlock
            {
                Text = "Browse patterns and build your estimate",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextSecondary)
            });
            border.Child = stack;
            return border;
        }

        // ═══════════════════════════════════════════
        //  STATS CARDS
        // ═══════════════════════════════════════════

        private Border BuildStatsCards()
        {
            var border = new Border { Padding = new Thickness(16, 4, 16, 12) };
            var grid = new Grid { ColumnSpacing = 10 };
            for (int i = 0; i < 5; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var allPatterns = _learningService.GetAllManualLinePatterns().Where(p => p.ManualLines.Count > 0).ToList();
            var estimates = _learningService.EstimatesImported;
            var patterns = _learningService.ManualLinePatternCount;
            var uniqueOps = allPatterns.SelectMany(p => p.ManualLines).Select(m => m.ManualLineType).Distinct().Count();
            var totalHours = allPatterns.SelectMany(p => p.ManualLines).Sum(m => m.LaborUnits + m.RefinishUnits);
            var avgOps = patterns > 0 ? (double)allPatterns.Sum(p => p.ManualLines.Count) / patterns : 0;
            var c1 = BuildStatCard("\uE8A5", "Estimates", estimates.ToString(), AccentBlue, out _statEstimates);
            var c2 = BuildStatCard("\uE80A", "Part Patterns", patterns.ToString(), AccentGreen, out _statPatterns);
            var c3 = BuildStatCard("\uE945", "Unique Ops", uniqueOps.ToString(), AccentPurple, out _statUniqueOps);
            var c4 = BuildStatCard("\uE823", "Total Hours", $"{totalHours:N1}", AccentOrange, out _statTotalHours);
            var c5 = BuildStatCard("\uE9D9", "Avg Ops/Part", $"{avgOps:N1}", AccentTeal, out _statAvgOps);
            Grid.SetColumn(c1, 0); Grid.SetColumn(c2, 1); Grid.SetColumn(c3, 2);
            Grid.SetColumn(c4, 3); Grid.SetColumn(c5, 4);
            grid.Children.Add(c1); grid.Children.Add(c2); grid.Children.Add(c3);
            grid.Children.Add(c4); grid.Children.Add(c5);
            border.Child = grid;
            return border;
        }

        private Border BuildStatCard(string glyph, string label, string value, Color accent, out TextBlock valueRef)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1)
            };
            var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18, Foreground = new SolidColorBrush(accent) });
            var valText = new TextBlock
            {
                Text = value, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(valText);
            valueRef = valText;
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10, Foreground = new SolidColorBrush(TextMuted),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            card.Child = stack;
            return card;
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
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // filters
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // tabs
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // search
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // results

            var filters = BuildQuickFilters();
            Grid.SetRow(filters, 0);
            grid.Children.Add(filters);

            var tabs = BuildCategoryTabs();
            Grid.SetRow(tabs, 1);
            grid.Children.Add(tabs);

            var search = BuildSearchBar();
            Grid.SetRow(search, 2);
            grid.Children.Add(search);

            _resultsScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 0, 16, 0)
            };
            _resultsPanel = new StackPanel { Spacing = 8 };
            _resultsScroller.Content = _resultsPanel;
            Grid.SetRow(_resultsScroller, 3);
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

            // Related ops
            _relatedOpsPanel = new StackPanel { Orientation = Orientation.Vertical, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            stack.Children.Add(_relatedOpsPanel);

            // AI advisor
            _advisorSuggestionsPanel = new StackPanel { Orientation = Orientation.Vertical, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            stack.Children.Add(_advisorSuggestionsPanel);

            border.Child = stack;
            return border;
        }

        // ═══════════════════════════════════════════
        //  QUICK FILTERS
        // ═══════════════════════════════════════════

        private Border BuildQuickFilters()
        {
            var border = new Border { Padding = new Thickness(16, 12, 16, 10) };
            var outer = new StackPanel { Spacing = 6 };
            outer.Children.Add(new TextBlock
            {
                Text = "QUICK SEARCH", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextMuted), CharacterSpacing = 60
            });
            _quickFiltersPanel = new StackPanel { Spacing = 6 };
            var quickFilters = new[]
            {
                ("Scan", "\uE8B5"), ("Weld", "\uE945"), ("Cavity Wax", "\uE7F6"),
                ("DE-NIB", "\uE8CB"), ("Seam Seal", "\uE8EE"), ("Corrosion", "\uE72E"),
                ("Calibrate", "\uE9A1"), ("R&I", "\uE8BB")
            };
            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            for (int i = 0; i < quickFilters.Length; i++)
            {
                var chip = CreateQuickFilterChip(quickFilters[i].Item1, quickFilters[i].Item2);
                if (i < 4) row1.Children.Add(chip); else row2.Children.Add(chip);
            }
            _quickFiltersPanel.Children.Add(row1);
            _quickFiltersPanel.Children.Add(row2);
            outer.Children.Add(_quickFiltersPanel);
            border.Child = outer;
            return border;
        }

        private Border CreateQuickFilterChip(string name, string glyph)
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 7, 14, 7), BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1)
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, Foreground = new SolidColorBrush(AccentBlue) });
            content.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = new SolidColorBrush(TextPrimary), VerticalAlignment = VerticalAlignment.Center });
            chip.Child = content;
            chip.Tag = name;
            chip.PointerPressed += (s, e) =>
            {
                if (_activeQuickFilter == name) { _activeQuickFilter = null; _searchBox!.Text = ""; HighlightActiveQuickFilter(null); ShowCategoryContent(_selectedCategory); }
                else { _activeQuickFilter = name; _searchBox!.Text = name; DoSearch(); HighlightActiveQuickFilter(name); }
            };
            chip.PointerEntered += (s, e) => { if (_activeQuickFilter != name) chip.Background = new SolidColorBrush(CardHover); };
            chip.PointerExited += (s, e) => { if (_activeQuickFilter != name) chip.Background = new SolidColorBrush(CardBg); };
            return chip;
        }

        private void HighlightActiveQuickFilter(string? activeName)
        {
            if (_quickFiltersPanel == null) return;
            foreach (var rowChild in _quickFiltersPanel.Children)
            {
                if (rowChild is StackPanel row)
                {
                    foreach (var child in row.Children)
                    {
                        if (child is Border chip)
                        {
                            var isActive = chip.Tag?.ToString() == activeName;
                            chip.Background = new SolidColorBrush(isActive ? Color.FromArgb(255, 30, 60, 110) : CardBg);
                            chip.BorderBrush = new SolidColorBrush(isActive ? AccentBlue : BorderSubtle);
                            if (chip.Child is StackPanel sp)
                            {
                                var existing = sp.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text == "\u00D7");
                                if (existing != null) sp.Children.Remove(existing);
                                if (isActive) sp.Children.Add(new TextBlock { Text = "\u00D7", FontSize = 14, Foreground = new SolidColorBrush(TextSecondary), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                            }
                        }
                    }
                }
            }
        }

        // ═══════════════════════════════════════════
        //  CATEGORY TABS
        // ═══════════════════════════════════════════

        private Border BuildCategoryTabs()
        {
            var outer = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 32)),
                Margin = new Thickness(16, 0, 16, 10), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4), BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1)
            };
            var grid = new Grid { ColumnSpacing = 4 };
            for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _allTab = CreateCategoryTab("All", 0, true);
            _byPartTab = CreateCategoryTab("By Part", 1, false);
            _byOpTab = CreateCategoryTab("By Op", 2, false);
            _recentTab = CreateCategoryTab("Top Used", 3, false);
            Grid.SetColumn(_allTab, 0); Grid.SetColumn(_byPartTab, 1); Grid.SetColumn(_byOpTab, 2); Grid.SetColumn(_recentTab, 3);
            grid.Children.Add(_allTab); grid.Children.Add(_byPartTab); grid.Children.Add(_byOpTab); grid.Children.Add(_recentTab);
            outer.Child = grid;
            return outer;
        }

        private Border CreateCategoryTab(string text, int index, bool isSelected)
        {
            var tab = new Border
            {
                Background = new SolidColorBrush(isSelected ? AccentBlue : Colors.Transparent),
                Padding = new Thickness(8, 10, 8, 10), CornerRadius = new CornerRadius(8)
            };
            tab.Child = new TextBlock
            {
                Text = text, FontSize = 13,
                FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = new SolidColorBrush(isSelected ? Colors.White : TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            tab.Tag = index;
            tab.PointerPressed += (s, e) => SelectCategory(index);
            tab.PointerEntered += (s, e) => { if (_selectedCategory != index) tab.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 50)); };
            tab.PointerExited += (s, e) => { if (_selectedCategory != index) tab.Background = new SolidColorBrush(Colors.Transparent); };
            return tab;
        }

        private void SelectCategory(int index)
        {
            _selectedCategory = index;
            _activeQuickFilter = null;
            HighlightActiveQuickFilter(null);
            UpdateCategoryTabStyle(_allTab, 0); UpdateCategoryTabStyle(_byPartTab, 1);
            UpdateCategoryTabStyle(_byOpTab, 2); UpdateCategoryTabStyle(_recentTab, 3);
            ShowCategoryContent(index);
        }

        private void UpdateCategoryTabStyle(Border? tab, int index)
        {
            if (tab == null) return;
            var isSelected = _selectedCategory == index;
            tab.Background = new SolidColorBrush(isSelected ? AccentBlue : Colors.Transparent);
            if (tab.Child is TextBlock text)
            {
                text.FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                text.Foreground = new SolidColorBrush(isSelected ? Colors.White : TextSecondary);
            }
        }

        // ═══════════════════════════════════════════
        //  SEARCH BAR
        // ═══════════════════════════════════════════

        private Border BuildSearchBar()
        {
            var border = new Border { Padding = new Thickness(16, 0, 16, 10) };
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _searchBox = new TextBox
            {
                PlaceholderText = "Search operations or parts...", FontSize = 13,
                Padding = new Thickness(14, 11, 14, 11), Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(TextPrimary), BorderBrush = new SolidColorBrush(BorderSubtle), CornerRadius = new CornerRadius(8)
            };
            _searchBox.KeyDown += SearchBox_KeyDown;
            _searchBox.TextChanged += SearchBox_TextChanged;
            Grid.SetColumn(_searchBox, 0);
            grid.Children.Add(_searchBox);
            _clearSearchButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
                Padding = new Thickness(10), Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 60)),
                Foreground = new SolidColorBrush(TextSecondary), CornerRadius = new CornerRadius(8), Visibility = Visibility.Collapsed
            };
            _clearSearchButton.Click += (s, e) => { _searchBox.Text = ""; _activeQuickFilter = null; HighlightActiveQuickFilter(null); ShowCategoryContent(_selectedCategory); };
            Grid.SetColumn(_clearSearchButton, 1);
            grid.Children.Add(_clearSearchButton);
            var searchBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE721", FontSize = 14 },
                Padding = new Thickness(12, 10, 12, 10), Background = new SolidColorBrush(AccentBlue),
                Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(8)
            };
            searchBtn.Click += (s, e) => DoSearch();
            Grid.SetColumn(searchBtn, 2);
            grid.Children.Add(searchBtn);
            border.Child = grid;
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
            _statusText = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(TextSecondary), TextWrapping = TextWrapping.Wrap, Text = "Browse patterns or search to build your estimate" };
            leftStack.Children.Add(_statusText);
            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

            var clearBtn = new Button { Padding = new Thickness(12, 8, 12, 8), Background = new SolidColorBrush(Color.FromArgb(255, 80, 40, 40)), Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(8) };
            clearBtn.Content = new TextBlock { Text = "Clear", FontSize = 12 };
            clearBtn.Click += ClearAll_Click;
            btnPanel.Children.Add(clearBtn);

            _copyButton = new Button { Padding = new Thickness(14, 8, 14, 8), Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 60)), Foreground = new SolidColorBrush(TextPrimary), CornerRadius = new CornerRadius(8), IsEnabled = false };
            var copyContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            copyContent.Children.Add(new FontIcon { Glyph = "\uE8C8", FontSize = 14 });
            copyContent.Children.Add(new TextBlock { Text = "Copy", FontSize = 12 });
            _copyButton.Content = copyContent;
            _copyButton.Click += CopyButton_Click;
            btnPanel.Children.Add(_copyButton);

            var clipBtn = new Button { Padding = new Thickness(14, 8, 14, 8), Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)), Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(8) };
            clipBtn.Content = new TextBlock { Text = "Clip It", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            clipBtn.Click += ClipIt_Click;
            btnPanel.Children.Add(clipBtn);

            var typeBtn = new Button { Padding = new Thickness(14, 8, 14, 8), Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)), Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(8) };
            typeBtn.Content = new TextBlock { Text = "Type It", FontSize = 12 };
            typeBtn.Click += TypeIt_Click;
            btnPanel.Children.Add(typeBtn);

            _insertButton = new Button { Padding = new Thickness(14, 8, 14, 8), Background = new SolidColorBrush(AccentGreen), Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(8), IsEnabled = false };
            var insertContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            insertContent.Children.Add(new FontIcon { Glyph = "\uE768", FontSize = 14 });
            insertContent.Children.Add(new TextBlock { Text = "Insert CCC", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            _insertButton.Content = insertContent;
            _insertButton.Click += InsertButton_Click;
            btnPanel.Children.Add(_insertButton);

            var sendBtn = new Button { Padding = new Thickness(14, 8, 14, 8), Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)), Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(8) };
            sendBtn.Content = new TextBlock { Text = "Send to Chat", FontSize = 12 };
            sendBtn.Click += SendToChat_Click;
            btnPanel.Children.Add(sendBtn);

            Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(btnPanel);
            border.Child = grid;
            return border;
        }

        // ═══════════════════════════════════════════
        //  CATEGORY CONTENT DISPATCH
        // ═══════════════════════════════════════════

        private void ShowCategoryContent(int category)
        {
            _resultsPanel!.Children.Clear();
            _selectedOperations.Clear();
            _selectedOpResults.Clear();
            var allPatterns = _learningService.GetAllManualLinePatterns()
                .Where(p => p.ManualLines.Count > 0 && !string.IsNullOrEmpty(p.ParentPartName)).ToList();
            if (allPatterns.Count == 0) { ShowEmptyState(); return; }
            switch (category)
            {
                case 0: ShowAllOverview(allPatterns); break;
                case 1: ShowByPartView(allPatterns); break;
                case 2: ShowByOperationView(allPatterns); break;
                case 3: ShowMostUsedView(allPatterns); break;
            }
            UpdateButtonStates();
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
            stack.Children.Add(new TextBlock { Text = "No Patterns Learned Yet", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = "Upload estimates in the Import tab to start building\nyour pattern library.", FontSize = 13, Foreground = new SolidColorBrush(TextSecondary), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
            card.Child = stack;
            _resultsPanel!.Children.Add(card);
        }

        private void ShowAllOverview(List<ManualLinePattern> patterns)
        {
            _resultsPanel!.Children.Add(new TextBlock { Text = $"You have {patterns.Count} part patterns learned. Use search or click a quick filter above.", FontSize = 12, Foreground = new SolidColorBrush(TextSecondary), Margin = new Thickness(4, 8, 0, 14), TextWrapping = TextWrapping.Wrap });
            AddSectionHeader("TOP PATTERNS");
            foreach (var pattern in patterns.OrderByDescending(p => p.ExampleCount).Take(10))
                _resultsPanel.Children.Add(CreatePatternCard(pattern));
        }

        private void ShowByPartView(List<ManualLinePattern> patterns)
        {
            var grouped = patterns.GroupBy(p => NormalizePartName(p.ParentPartName ?? "Other")).OrderByDescending(g => g.Sum(p => p.ExampleCount)).ToList();
            foreach (var group in grouped.Take(15))
                _resultsPanel!.Children.Add(CreateAccordionCard(group.Key, group.Count(), group.Sum(p => p.ExampleCount), () => group.ToList()));
        }

        private void ShowByOperationView(List<ManualLinePattern> patterns)
        {
            var allOps = patterns.SelectMany(p => p.ManualLines).GroupBy(m => m.ManualLineType).OrderByDescending(g => g.Sum(m => m.TimesUsed)).Take(20).ToList();
            foreach (var opGroup in allOps)
                _resultsPanel!.Children.Add(CreateOperationGroupCard(opGroup.Key, opGroup.Sum(m => m.TimesUsed), opGroup.ToList()));
        }

        private void ShowMostUsedView(List<ManualLinePattern> patterns)
        {
            var sorted = patterns.OrderByDescending(p => p.ManualLines.Sum(m => m.TimesUsed)).Take(15).ToList();
            int rank = 1;
            foreach (var pattern in sorted)
                _resultsPanel!.Children.Add(CreateRankedPatternCard(pattern, rank++));
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
            card.PointerPressed += (s, e) => { _searchBox!.Text = $"{pattern.ParentPartName} {pattern.ParentOperationType}"; _currentPattern = pattern; ShowPatternDetails(pattern); };
            card.PointerEntered += (s, e) => { card.Background = new SolidColorBrush(CardHover); chevron.Foreground = new SolidColorBrush(AccentBlue); };
            card.PointerExited += (s, e) => { card.Background = new SolidColorBrush(CardBg); chevron.Foreground = new SolidColorBrush(TextMuted); };
            return card;
        }

        private Border CreateRankedPatternCard(ManualLinePattern pattern, int rank)
        {
            var card = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 2), BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1) };
            var innerGrid = new Grid();
            var accentColor = rank == 1 ? Gold : rank == 2 ? Silver : rank == 3 ? Bronze : BorderSubtle;
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var accentBar = new Border { Background = new SolidColorBrush(accentColor), CornerRadius = new CornerRadius(8, 0, 0, 8) };
            Grid.SetColumn(accentBar, 0);
            innerGrid.Children.Add(accentBar);
            var contentGrid = new Grid { Padding = new Thickness(12, 12, 14, 12) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var medalText = rank <= 3 ? (rank == 1 ? "\uD83E\uDD47" : rank == 2 ? "\uD83E\uDD48" : "\uD83E\uDD49") : rank.ToString();
            if (rank <= 3)
            {
                var medalBlock = new TextBlock { Text = medalText, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(medalBlock, 0); contentGrid.Children.Add(medalBlock);
            }
            else
            {
                var rankBadge = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                rankBadge.Child = new TextBlock { Text = rank.ToString(), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(TextSecondary), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(rankBadge, 0); contentGrid.Children.Add(rankBadge);
            }
            var infoStack = new StackPanel { Spacing = 3, Margin = new Thickness(6, 0, 0, 0) };
            infoStack.Children.Add(new TextBlock { Text = $"{pattern.ParentPartName} - {pattern.ParentOperationType}", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary) });
            var totalUses = pattern.ManualLines.Sum(m => m.TimesUsed);
            infoStack.Children.Add(new TextBlock { Text = $"{pattern.ManualLines.Count} ops  \u2022  {totalUses} total uses", FontSize = 11, Foreground = new SolidColorBrush(TextSecondary) });
            Grid.SetColumn(infoStack, 1); contentGrid.Children.Add(infoStack);
            var totalHours = pattern.ManualLines.Sum(m => m.LaborUnits + m.RefinishUnits);
            var hoursPill = CreatePill($"{totalHours:N1}h", AccentGreen, Color.FromArgb(255, 25, 60, 40));
            hoursPill.Margin = new Thickness(8, 0, 0, 0); hoursPill.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hoursPill, 2); contentGrid.Children.Add(hoursPill);
            // + button
            var addBtn = new Button { Content = new FontIcon { Glyph = "\uE710", FontSize = 12 }, Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromArgb(255, 30, 80, 50)), Foreground = new SolidColorBrush(AccentGreen), CornerRadius = new CornerRadius(6), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            addBtn.Click += (s, e) => { AddPatternToCart(pattern); };
            Grid.SetColumn(addBtn, 3); contentGrid.Children.Add(addBtn);
            var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 14, Foreground = new SolidColorBrush(TextMuted), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chevron, 4); contentGrid.Children.Add(chevron);
            Grid.SetColumn(contentGrid, 1); innerGrid.Children.Add(contentGrid);
            card.Child = innerGrid;
            card.PointerPressed += (s, e) => { _currentPattern = pattern; ShowPatternDetails(pattern); };
            card.PointerEntered += (s, e) => { card.Background = new SolidColorBrush(CardHover); chevron.Foreground = new SolidColorBrush(AccentBlue); };
            card.PointerExited += (s, e) => { card.Background = new SolidColorBrush(CardBg); chevron.Foreground = new SolidColorBrush(TextMuted); };
            return card;
        }

        private Border CreateAccordionCard(string title, int patternCount, int estimateCount, Func<List<ManualLinePattern>> getPatternsFunc)
        {
            var card = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 4), BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1) };
            var stack = new StackPanel();
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chevronIcon = new FontIcon { Glyph = "\uE76C", FontSize = 12, Foreground = new SolidColorBrush(AccentBlue), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chevronIcon, 0); headerGrid.Children.Add(chevronIcon);
            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary) });
            titleStack.Children.Add(new TextBlock { Text = $"{estimateCount} estimates", FontSize = 11, Foreground = new SolidColorBrush(TextSecondary) });
            Grid.SetColumn(titleStack, 1); headerGrid.Children.Add(titleStack);
            var countBadge = CreatePill($"{patternCount}", AccentBlue, Color.FromArgb(255, 30, 55, 90));
            countBadge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(countBadge, 2); headerGrid.Children.Add(countBadge);
            stack.Children.Add(headerGrid);
            var contentPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(22, 10, 0, 0), Spacing = 4 };
            stack.Children.Add(contentPanel);
            card.Child = stack;
            bool isExpanded = false;
            card.PointerPressed += (s, e) =>
            {
                isExpanded = !isExpanded;
                contentPanel.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
                chevronIcon.Glyph = isExpanded ? "\uE70D" : "\uE76C";
                if (isExpanded && contentPanel.Children.Count == 0)
                    foreach (var p in getPatternsFunc()) contentPanel.Children.Add(CreateMiniPatternCard(p));
            };
            card.PointerEntered += (s, e) => card.Background = new SolidColorBrush(CardHover);
            card.PointerExited += (s, e) => card.Background = new SolidColorBrush(CardBg);
            return card;
        }

        private Border CreateMiniPatternCard(ManualLinePattern pattern)
        {
            var card = new Border { Background = new SolidColorBrush(SurfaceBg), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = $"{pattern.ParentOperationType}  \u2022  {pattern.ManualLines.Count} ops", FontSize = 12, Foreground = new SolidColorBrush(TextPrimary), VerticalAlignment = VerticalAlignment.Center });
            var totalHours = pattern.ManualLines.Sum(m => m.LaborUnits + m.RefinishUnits);
            var hoursPill = CreatePill($"{totalHours:N1}h", AccentGreen, Color.FromArgb(255, 25, 60, 40));
            hoursPill.VerticalAlignment = VerticalAlignment.Center; hoursPill.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(hoursPill, 1); grid.Children.Add(hoursPill);
            var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 12, Foreground = new SolidColorBrush(TextMuted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(chevron, 2); grid.Children.Add(chevron);
            card.Child = grid;
            card.PointerPressed += (s, e) => { _currentPattern = pattern; ShowPatternDetails(pattern); };
            card.PointerEntered += (s, e) => { card.Background = new SolidColorBrush(CardHover); chevron.Foreground = new SolidColorBrush(AccentBlue); };
            card.PointerExited += (s, e) => { card.Background = new SolidColorBrush(SurfaceBg); chevron.Foreground = new SolidColorBrush(TextMuted); };
            return card;
        }

        private Border CreateOperationGroupCard(string opName, int totalUses, List<ManualLineEntry> entries)
        {
            var card = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4), BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1) };
            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var accentBar = new Border { Background = new SolidColorBrush(AccentPurple), CornerRadius = new CornerRadius(8, 0, 0, 8) };
            Grid.SetColumn(accentBar, 0); innerGrid.Children.Add(accentBar);
            var contentGrid = new Grid { Padding = new Thickness(14, 12, 14, 12) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var opIcon = new FontIcon { Glyph = "\uE945", FontSize = 16, Foreground = new SolidColorBrush(AccentPurple), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(opIcon, 0); contentGrid.Children.Add(opIcon);
            var infoStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(opName) ? "(unnamed)" : opName, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary) });
            var avgLabor = entries.Where(e => e.LaborUnits > 0).Select(e => e.LaborUnits).DefaultIfEmpty(0).Average();
            infoStack.Children.Add(new TextBlock { Text = $"Avg: {avgLabor:N2}h labor", FontSize = 11, Foreground = new SolidColorBrush(TextSecondary) });
            Grid.SetColumn(infoStack, 1); contentGrid.Children.Add(infoStack);
            var usesPill = CreatePill($"{totalUses}x", AccentPurple, Color.FromArgb(255, 50, 35, 70));
            usesPill.Margin = new Thickness(8, 0, 0, 0); usesPill.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(usesPill, 2); contentGrid.Children.Add(usesPill);
            var avgHoursPill = CreatePill($"{avgLabor:N1}h", AccentGreen, Color.FromArgb(255, 25, 60, 40));
            avgHoursPill.Margin = new Thickness(6, 0, 0, 0); avgHoursPill.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(avgHoursPill, 3); contentGrid.Children.Add(avgHoursPill);
            var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 14, Foreground = new SolidColorBrush(TextMuted), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chevron, 4); contentGrid.Children.Add(chevron);
            Grid.SetColumn(contentGrid, 1); innerGrid.Children.Add(contentGrid);
            card.Child = innerGrid;
            card.PointerPressed += (s, e) => { _searchBox!.Text = opName; DoSearch(); };
            card.PointerEntered += (s, e) => { card.Background = new SolidColorBrush(CardHover); chevron.Foreground = new SolidColorBrush(AccentBlue); };
            card.PointerExited += (s, e) => { card.Background = new SolidColorBrush(CardBg); chevron.Foreground = new SolidColorBrush(TextMuted); };
            return card;
        }

        // ═══════════════════════════════════════════
        //  PATTERN DETAILS VIEW (with Add Selected)
        // ═══════════════════════════════════════════

        private void ShowPatternDetails(ManualLinePattern pattern)
        {
            _resultsPanel!.Children.Clear();
            _selectedOperations.Clear();
            _currentPattern = pattern;
            var exampleCount = pattern.ExampleCount;
            var totalHours = pattern.ManualLines.Sum(m => m.LaborUnits + m.RefinishUnits);
            _statusText!.Text = $"Found {pattern.ManualLines.Count} operations for {pattern.ParentPartName} {pattern.ParentOperationType}";
            _statusText.Foreground = new SolidColorBrush(AccentGreen);
            _resultsPanel.Children.Add(CreateBackButton(() => ShowCategoryContent(_selectedCategory)));
            var summaryGrid = new Grid { Margin = new Thickness(0, 8, 0, 12), ColumnSpacing = 10 };
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var s1 = CreateDetailStatCard("Estimates", exampleCount.ToString(), AccentBlue, Color.FromArgb(255, 25, 40, 65));
            var s2 = CreateDetailStatCard("Total Hours", totalHours.ToString("N1"), AccentGreen, Color.FromArgb(255, 20, 50, 35));
            var s3 = CreateDetailStatCard("Avg/Estimate", (totalHours / Math.Max(1, exampleCount)).ToString("N1"), AccentOrange, Color.FromArgb(255, 55, 40, 20));
            Grid.SetColumn(s1, 0); Grid.SetColumn(s2, 1); Grid.SetColumn(s3, 2);
            summaryGrid.Children.Add(s1); summaryGrid.Children.Add(s2); summaryGrid.Children.Add(s3);
            _resultsPanel.Children.Add(summaryGrid);

            var togglePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 8) };
            var selectAllBtn = new Button { Content = "Select All", FontSize = 11, Padding = new Thickness(12, 5, 12, 5), Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55)), Foreground = new SolidColorBrush(TextPrimary), CornerRadius = new CornerRadius(6) };
            var deselectAllBtn = new Button { Content = "Deselect All", FontSize = 11, Padding = new Thickness(12, 5, 12, 5), Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55)), Foreground = new SolidColorBrush(TextSecondary), CornerRadius = new CornerRadius(6) };
            // Add Selected to Estimate button
            var addSelectedBtn = new Button { FontSize = 11, Padding = new Thickness(14, 5, 14, 5), Background = new SolidColorBrush(AccentGreen), Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(6) };
            var addSelContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            addSelContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 11 });
            addSelContent.Children.Add(new TextBlock { Text = "Add Selected to Estimate", FontSize = 11 });
            addSelectedBtn.Content = addSelContent;
            addSelectedBtn.Click += (s, e) => { AddSelectedToCart(pattern); };
            togglePanel.Children.Add(selectAllBtn);
            togglePanel.Children.Add(deselectAllBtn);
            togglePanel.Children.Add(addSelectedBtn);
            _resultsPanel.Children.Add(togglePanel);

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
            var checkboxes = new List<CheckBox>();
            int rowIndex = 0;
            foreach (var manualLine in pattern.ManualLines.OrderByDescending(m => m.TimesUsed))
            {
                var row = CreateOperationRow(manualLine, exampleCount, rowIndex % 2 == 1, out var cb);
                checkboxes.Add(cb);
                _resultsPanel.Children.Add(row);
                rowIndex++;
            }
            selectAllBtn.Click += (s, e) => { foreach (var cb in checkboxes) cb.IsChecked = true; };
            deselectAllBtn.Click += (s, e) => { foreach (var cb in checkboxes) cb.IsChecked = false; };
            UpdateButtonStates();
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
        //  SEARCH LOGIC
        // ═══════════════════════════════════════════

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == Windows.System.VirtualKey.Enter) { DoSearch(); e.Handled = true; } }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_clearSearchButton != null) _clearSearchButton.Visibility = string.IsNullOrEmpty(_searchBox?.Text) ? Visibility.Collapsed : Visibility.Visible;
            if (_searchBox?.Text.Length >= 3) DoSearch();
        }

        private void DoSearch()
        {
            var query = _searchBox?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(query)) { _statusText!.Text = "Search by part name or operation"; _statusText.Foreground = new SolidColorBrush(TextSecondary); _resultsPanel!.Children.Clear(); _currentPattern = null; _currentOpStats = null; UpdateButtonStates(); return; }
            _resultsPanel!.Children.Clear(); _selectedOperations.Clear(); _selectedOpResults.Clear();
            var (partName, opType) = ParseQuery(query);
            _currentPattern = _learningService.GetManualLinesForPart(partName, opType);
            if (_currentPattern != null && _currentPattern.ManualLines.Count > 0) { ShowPatternDetails(_currentPattern); return; }
            var patternMatches = _learningService.SearchPatterns(query, 30);
            if (patternMatches.Count > 0) { ShowPartSearchResults(query, patternMatches); return; }
            _currentOpStats = _learningService.GetOperationStats(query);
            if (_currentOpStats.Occurrences.Count > 0) { ShowOperationKeywordResults(_currentOpStats); return; }
            _statusText!.Text = $"No results for \"{query}\""; _statusText.Foreground = new SolidColorBrush(AccentOrange); UpdateButtonStates();
        }

        private void ShowPartSearchResults(string query, List<LearnedPattern> matches)
        {
            var grouped = matches.GroupBy(m => m.PartName?.ToLowerInvariant() ?? "").Where(g => !string.IsNullOrEmpty(g.Key))
                .Select(g => new { PartName = g.First().PartName, Operations = g.ToList(), TotalExamples = g.Sum(p => p.ExampleCount), BestConfidence = g.Max(p => p.Confidence) })
                .OrderByDescending(g => g.BestConfidence).ThenByDescending(g => g.TotalExamples).ToList();
            _statusText!.Text = $"Found {grouped.Count} parts matching \"{query}\""; _statusText.Foreground = new SolidColorBrush(AccentGreen);
            _resultsPanel!.Children.Add(CreateBackButton(() => ShowCategoryContent(_selectedCategory)));
            foreach (var group in grouped)
            {
                var card = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 6), BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1) };
                var stack = new StackPanel { Spacing = 6 };
                stack.Children.Add(new TextBlock { Text = group.PartName, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary) });
                var opsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var op in group.Operations.Take(6))
                {
                    var opLabel = !string.IsNullOrEmpty(op.OperationType) ? op.OperationType : "—";
                    var firstOp = op.Operations?.FirstOrDefault();
                    var chipText = opLabel;
                    if (firstOp != null) { var parts = new List<string>(); if (firstOp.LaborHours > 0) parts.Add($"{firstOp.LaborHours:N1}h"); if (firstOp.RefinishHours > 0) parts.Add($"R:{firstOp.RefinishHours:N1}h"); if (firstOp.Price > 0) parts.Add($"${firstOp.Price:N0}"); if (parts.Count > 0) chipText += $" ({string.Join(", ", parts)})"; }
                    opsRow.Children.Add(CreatePill(chipText, AccentBlue, Color.FromArgb(255, 25, 40, 65)));
                }
                if (group.Operations.Count > 6) opsRow.Children.Add(CreatePill($"+{group.Operations.Count - 6} more", TextMuted, Color.FromArgb(255, 40, 40, 45)));
                stack.Children.Add(opsRow);
                var footerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                footerRow.Children.Add(new TextBlock { Text = $"{group.TotalExamples} example{(group.TotalExamples != 1 ? "s" : "")}", FontSize = 11, Foreground = new SolidColorBrush(TextMuted) });
                footerRow.Children.Add(new TextBlock { Text = $"{group.BestConfidence:P0} confidence", FontSize = 11, Foreground = new SolidColorBrush(group.BestConfidence >= 0.7 ? AccentGreen : AccentOrange) });
                stack.Children.Add(footerRow);
                card.Child = stack;
                var capturedPartName = group.PartName;
                card.PointerPressed += (s, e) => { _searchBox!.Text = capturedPartName; var pattern = _learningService.GetManualLinesForPart(capturedPartName.ToLowerInvariant(), null); if (pattern != null && pattern.ManualLines.Count > 0) ShowPatternDetails(pattern); };
                card.PointerEntered += (s, e) => card.Background = new SolidColorBrush(CardHover);
                card.PointerExited += (s, e) => card.Background = new SolidColorBrush(CardBg);
                _resultsPanel.Children.Add(card);
            }
            UpdateButtonStates();
        }

        private void ShowOperationKeywordResults(OperationAggregateStats stats)
        {
            _statusText!.Text = $"Found \"{stats.OperationName}\" in {stats.UniqueParentParts} parts ({stats.TotalOccurrences} occurrences)";
            _statusText.Foreground = new SolidColorBrush(AccentGreen);
            _resultsPanel!.Children.Add(CreateBackButton(() => ShowCategoryContent(_selectedCategory)));
            var summaryGrid = new Grid { Margin = new Thickness(0, 8, 0, 12), ColumnSpacing = 10 };
            int colCount = stats.AvgPrice > 0 ? 4 : 3;
            for (int i = 0; i < colCount; i++) summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var s1 = CreateDetailStatCard("Times Used", stats.TotalOccurrences.ToString(), AccentBlue, Color.FromArgb(255, 25, 40, 65));
            var s2 = CreateDetailStatCard("Unique Parts", stats.UniqueParentParts.ToString(), AccentPurple, Color.FromArgb(255, 40, 30, 60));
            var s3 = CreateDetailStatCard("Avg Labor", stats.AvgLaborHours.ToString("N2") + "h", AccentGreen, Color.FromArgb(255, 20, 50, 35));
            Grid.SetColumn(s1, 0); Grid.SetColumn(s2, 1); Grid.SetColumn(s3, 2);
            summaryGrid.Children.Add(s1); summaryGrid.Children.Add(s2); summaryGrid.Children.Add(s3);
            if (stats.AvgPrice > 0) { var s4 = CreateDetailStatCard("Avg Price", stats.AvgPrice.ToString("C0"), AccentOrange, Color.FromArgb(255, 55, 40, 20)); Grid.SetColumn(s4, 3); summaryGrid.Children.Add(s4); }
            _resultsPanel.Children.Add(summaryGrid);
            AddSectionHeader("INDIVIDUAL OCCURRENCES");
            foreach (var occ in stats.Occurrences.OrderByDescending(o => o.TimesUsed).Take(25))
                _resultsPanel.Children.Add(CreateOccurrenceCard(occ));
            UpdateButtonStates();
        }

        private Border CreateOccurrenceCard(OperationSearchResult occ)
        {
            var card = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4), BorderBrush = new SolidColorBrush(BorderSubtle), BorderThickness = new Thickness(1) };
            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var checkbox = new CheckBox { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            checkbox.Checked += (s, e) => { _selectedOpResults.Add(occ); UpdateButtonStates(); };
            checkbox.Unchecked += (s, e) => { _selectedOpResults.Remove(occ); UpdateButtonStates(); };
            Grid.SetColumn(checkbox, 0); innerGrid.Children.Add(checkbox);
            var contentStack = new StackPanel { Padding = new Thickness(8, 12, 14, 12), Spacing = 6 };
            contentStack.Children.Add(new TextBlock { Text = occ.ParentPartName, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary), TextTrimming = TextTrimming.CharacterEllipsis });
            contentStack.Children.Add(new TextBlock { Text = $"{occ.ParentOperationType}  \u2022  {occ.Description}", FontSize = 11, Foreground = new SolidColorBrush(TextSecondary), TextTrimming = TextTrimming.CharacterEllipsis });
            var pillRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (occ.LaborHours > 0) pillRow.Children.Add(CreatePill($"Labor: {occ.LaborHours:N2}h", Color.FromArgb(255, 130, 200, 140), Color.FromArgb(255, 25, 60, 40)));
            if (occ.RefinishHours > 0) pillRow.Children.Add(CreatePill($"Refn: {occ.RefinishHours:N2}h", Color.FromArgb(255, 140, 170, 255), Color.FromArgb(255, 30, 35, 70)));
            if (occ.AvgPrice > 0) pillRow.Children.Add(CreatePill($"${occ.AvgPrice:N0}", AccentOrange, Color.FromArgb(255, 55, 40, 20)));
            pillRow.Children.Add(CreatePill($"{occ.TimesUsed}x", AccentPurple, Color.FromArgb(255, 50, 35, 70)));
            contentStack.Children.Add(pillRow);
            Grid.SetColumn(contentStack, 1); innerGrid.Children.Add(contentStack);
            card.Child = innerGrid;
            card.PointerEntered += (s, e) => card.Background = new SolidColorBrush(CardHover);
            card.PointerExited += (s, e) => card.Background = new SolidColorBrush(CardBg);
            return card;
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
                    LaborHours = entry.LaborUnits,
                    RefinishHours = entry.RefinishUnits,
                    Price = entry.AvgPrice
                });
            }
            UpdateEstimateDisplay();
            if (pattern.ManualLines.Count > 0)
            {
                var first = pattern.ManualLines[0];
                var partName = first.ManualLineType.Length > 0 ? first.ManualLineType : first.Description;
                ShowRelatedOperations(partName, pattern.ParentOperationType ?? "");
                ShowAdvisorSuggestions(pattern.ParentPartName ?? partName, pattern.ParentOperationType ?? "");
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
                _estimateView.Items.Add(new TextBlock { Text = "Browse or search to add lines", FontSize = 12, FontStyle = Windows.UI.Text.FontStyle.Italic, Foreground = new SolidColorBrush(TextMuted), Margin = new Thickness(0, 8, 0, 8) });
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

        private void ShowRelatedOperations(string partName, string operationType)
        {
            if (_relatedOpsPanel == null) return;
            var related = _learningService.GetRelatedOperations(partName, operationType, 8);
            var existingKeys = _estimateLines.Select(l => $"{l.PartName}|{l.OperationType}".ToLowerInvariant()).ToHashSet();
            related = related.Where(r => !existingKeys.Contains($"{r.PartName}|{r.OperationType}".ToLowerInvariant())).ToList();
            _relatedOpsPanel.Children.Clear();
            if (related.Count == 0) { _relatedOpsPanel.Visibility = Visibility.Collapsed; return; }
            _relatedOpsPanel.Children.Add(new TextBlock { Text = "Also typically on this estimate:", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)), Margin = new Thickness(0, 0, 0, 6) });
            var wrapGrid = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var entry in related) wrapGrid.Children.Add(CreateRelatedOpChip(entry));
            _relatedOpsPanel.Children.Add(new ScrollViewer { Content = wrapGrid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 50 });
            _relatedOpsPanel.Visibility = Visibility.Visible;
        }

        private Border CreateRelatedOpChip(CoOccurrenceEntry entry)
        {
            var rateText = $"{entry.CoOccurrenceRate:P0}";
            var hoursText = entry.AvgLaborHours > 0 ? $" {entry.AvgLaborHours:F1}h" : "";
            var chip = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 10, 4), Child = new TextBlock { Text = $"{entry.PartName} {entry.OperationType} ({rateText}{hoursText})", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)) } };
            chip.PointerEntered += (s, e) => { chip.Background = new SolidColorBrush(Color.FromArgb(255, 65, 65, 65)); chip.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)); };
            chip.PointerExited += (s, e) => { chip.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)); chip.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)); };
            chip.Tapped += (s, e) => { _estimateLines.Add(new EstimateLine { PartName = entry.PartName, OperationType = entry.OperationType, LaborHours = entry.AvgLaborHours, RefinishHours = entry.AvgRefinishHours, Price = entry.AvgPrice }); UpdateEstimateDisplay(); };
            return chip;
        }

        private void ShowAdvisorSuggestions(string partName, string operationType)
        {
            if (_advisorSuggestionsPanel == null) return;
            _advisorSuggestionsPanel.Children.Clear();
            try
            {
                var enteredTypes = _estimateLines.Select(l => l.OperationType).ToList();
                var suggestion = _advisorService.GetProactiveSuggestions(partName, operationType, enteredTypes, null);
                if (suggestion == null || suggestion.PatternSuggestions.Count == 0) { _advisorSuggestionsPanel.Visibility = Visibility.Collapsed; return; }
                var existingKeys = _estimateLines.Select(l => $"{l.PartName}|{l.OperationType}".ToLowerInvariant()).ToHashSet();
                var newSuggestions = suggestion.PatternSuggestions.Where(s => !existingKeys.Contains($"{s.Description}|{s.OperationType}".ToLowerInvariant())).ToList();
                if (newSuggestions.Count == 0) { _advisorSuggestionsPanel.Visibility = Visibility.Collapsed; return; }
                _advisorSuggestionsPanel.Children.Add(new TextBlock { Text = $"\uD83E\uDDE0 Others who did {partName} {operationType} also added:", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 220, 140)), Margin = new Thickness(0, 0, 0, 4) });
                var wrapPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var sug in newSuggestions.Take(6))
                {
                    var confText = sug.Confidence >= 0.7 ? "HIGH" : sug.Confidence >= 0.4 ? "MED" : "LOW";
                    var hoursText = sug.Hours > 0 ? $" {sug.Hours:F1}h" : "";
                    var chip = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 40)), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 120, 70)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 10, 4), Child = new TextBlock { Text = $"{sug.Description} ({confText}{hoursText})", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 220, 200)) } };
                    var captured = sug;
                    chip.PointerEntered += (s, e) => { chip.Background = new SolidColorBrush(Color.FromArgb(255, 45, 70, 50)); };
                    chip.PointerExited += (s, e) => { chip.Background = new SolidColorBrush(Color.FromArgb(255, 35, 50, 40)); };
                    chip.Tapped += (s, e) => { _estimateLines.Add(new EstimateLine { PartName = captured.Description.Split(' ').FirstOrDefault() ?? captured.Description, OperationType = captured.OperationType, LaborHours = captured.Hours }); UpdateEstimateDisplay(); };
                    wrapPanel.Children.Add(chip);
                }
                _advisorSuggestionsPanel.Children.Add(new ScrollViewer { Content = wrapPanel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 50 });
                if (!string.IsNullOrWhiteSpace(suggestion.AccuracyWarning)) _advisorSuggestionsPanel.Children.Add(new TextBlock { Text = $"\u26A0 {suggestion.AccuracyWarning}", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
                _advisorSuggestionsPanel.Visibility = Visibility.Visible;
            }
            catch { _advisorSuggestionsPanel.Visibility = Visibility.Collapsed; }
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
            foreach (var op in _selectedOperations) { var desc = op.ManualLineType.Length > 0 ? op.ManualLineType : op.Description; var hours = op.LaborUnits > 0 ? op.LaborUnits : op.RefinishUnits; var laborType = op.RefinishUnits > 0 ? "Refn" : "Rpr"; var price = op.AvgPrice > 0 ? $"\t${op.AvgPrice:N0}" : ""; lines.AppendLine($"#\t{laborType}\t{desc}\t{hours:N1}{price}"); }
            foreach (var op in _selectedOpResults) { var desc = op.OperationName.Length > 0 ? op.OperationName : op.Description; var hours = op.LaborHours > 0 ? op.LaborHours : op.RefinishHours; var laborType = op.RefinishHours > 0 ? "Refn" : "Rpr"; var price = op.AvgPrice > 0 ? $"\t${op.AvgPrice:N0}" : ""; lines.AppendLine($"#\t{laborType}\t{desc}\t{hours:N1}{price}"); }
            foreach (var line in _estimateLines) { var val = line.LaborHours > 0 ? $"{line.LaborHours:F1}" : line.RefinishHours > 0 ? $"{line.RefinishHours:F1}" : line.Price > 0 ? $"${line.Price:F0}" : ""; var laborType = line.RefinishHours > 0 ? "Refn" : "Rpr"; lines.AppendLine($"#\t{laborType}\t{line.PartName}\t{val}"); }
            if (lines.Length == 0) return;
            var dataPackage = new DataPackage(); dataPackage.SetText(lines.ToString()); Clipboard.SetContent(dataPackage);
            if (sender is Button btn) { var original = btn.Content; var cc = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; cc.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 14 }); cc.Children.Add(new TextBlock { Text = "Copied!", FontSize = 12 }); btn.Content = cc; await System.Threading.Tasks.Task.Delay(1500); btn.Content = original; }
        }

        private async void InsertButton_Click(object sender, RoutedEventArgs e)
        {
            var rows = new List<string[]>();
            foreach (var op in _selectedOperations) { var desc = op.ManualLineType.Length > 0 ? op.ManualLineType : op.Description; var hours = op.LaborUnits > 0 ? op.LaborUnits : op.RefinishUnits; var laborType = op.RefinishUnits > 0 ? "Refn" : "Rpr"; rows.Add(new[] { "#", laborType, desc, hours.ToString("N1") }); }
            foreach (var op in _selectedOpResults) { var desc = op.OperationName.Length > 0 ? op.OperationName : op.Description; var hours = op.LaborHours > 0 ? op.LaborHours : op.RefinishHours; var laborType = op.RefinishHours > 0 ? "Refn" : "Rpr"; rows.Add(new[] { "#", laborType, desc, hours.ToString("N1") }); }
            foreach (var line in _estimateLines) { var val = line.LaborHours > 0 ? line.LaborHours : line.RefinishHours; var laborType = line.RefinishHours > 0 ? "Refn" : "Rpr"; rows.Add(new[] { "#", laborType, line.PartName, val.ToString("N1") }); }
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

        private void UpdateButtonStates()
        {
            var totalSelected = _selectedOperations.Count + _selectedOpResults.Count + _estimateLines.Count;
            var hasSelection = totalSelected > 0;
            if (_copyButton != null) _copyButton.IsEnabled = hasSelection;
            if (_insertButton != null) _insertButton.IsEnabled = hasSelection;
            if (_selectionCountText != null) { _selectionCountText.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed; _selectionCountText.Text = $"{totalSelected} selected"; }
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

        private string NormalizePartName(string partName)
        {
            var lower = partName.ToLowerInvariant();
            if (lower.Contains("door")) return "Door"; if (lower.Contains("fender")) return "Fender";
            if (lower.Contains("bumper")) return "Bumper"; if (lower.Contains("hood")) return "Hood";
            if (lower.Contains("quarter")) return "Quarter Panel"; if (lower.Contains("roof")) return "Roof";
            if (lower.Contains("trunk") || lower.Contains("decklid")) return "Trunk/Decklid";
            if (lower.Contains("mirror")) return "Mirror"; if (lower.Contains("headl")) return "Headlamp";
            if (lower.Contains("taill")) return "Taillamp"; return partName;
        }

        private (string partName, string? opType) ParseQuery(string query)
        {
            var lower = query.ToLowerInvariant();
            string? opType = null;
            if (lower.Contains("replace") || lower.Contains("repl")) opType = "replace";
            else if (lower.Contains("repair") || lower.Contains("rpr")) opType = "repair";
            else if (lower.Contains("blend") || lower.Contains("blnd")) opType = "blend";
            else if (lower.Contains("refinish") || lower.Contains("refn")) opType = "refinish";
            else if (lower.Contains("r&i") || lower.Contains("r+i")) opType = "r&i";
            var partName = lower.Replace("replace", "").Replace("repl", "").Replace("repair", "").Replace("rpr", "").Replace("blend", "").Replace("blnd", "").Replace("refinish", "").Replace("refn", "").Replace("r&i", "").Replace("r+i", "").Trim();
            if (partName.Contains("door shell")) partName = "door shell";
            else if (partName.Contains("door")) partName = "door";
            else if (partName.Contains("fender")) partName = "fender";
            else if (partName.Contains("bumper")) partName = "bumper cover";
            else if (partName.Contains("hood")) partName = "hood";
            else if (partName.Contains("quarter")) partName = "quarter panel";
            else if (partName.Contains("roof")) partName = "roof";
            else if (partName.Contains("trunk") || partName.Contains("decklid")) partName = "trunk lid";
            return (partName, opType);
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
            public decimal LaborHours { get; set; }
            public decimal RefinishHours { get; set; }
            public decimal Price { get; set; }
        }
    }
}
