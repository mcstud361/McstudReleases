#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    /// <summary>
    /// Learned Patterns Panel - Enhanced navigation for learned estimate patterns
    /// Features: Quick filters, category tabs, improved organization
    /// </summary>
    public sealed class LearnedPatternsPanel : UserControl
    {
        private readonly EstimateLearningService _learningService;
        private readonly TypeItService _typeItService;

        private TextBox? _searchBox;
        private StackPanel? _resultsPanel;
        private TextBlock? _statusText;
        private TextBlock? _statsText;
        private Button? _insertButton;
        private Button? _copyButton;
        private ScrollViewer? _resultsScroller;

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

        // Colors
        private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 215);
        private static readonly Color AccentGreen = Color.FromArgb(255, 0, 150, 80);
        private static readonly Color AccentPurple = Color.FromArgb(255, 140, 100, 200);
        private static readonly Color AccentOrange = Color.FromArgb(255, 220, 150, 50);
        private static readonly Color DarkBg = Color.FromArgb(255, 25, 25, 28);
        private static readonly Color CardBg = Color.FromArgb(255, 35, 35, 38);
        private static readonly Color TextGray = Color.FromArgb(255, 150, 150, 150);

        public LearnedPatternsPanel()
        {
            _learningService = EstimateLearningService.Instance;
            _typeItService = new TypeItService();
            _typeItService.SetSpeedLevel(4);
            _typeItService.BlockUserInput = true;

            BuildUI();
            UpdateStats();
            ShowCategoryContent(0); // Show "All" by default
        }

        private void BuildUI()
        {
            var mainGrid = new Grid
            {
                Background = new SolidColorBrush(DarkBg)
            };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Stats cards
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Quick filters
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Category tabs
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Results
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Actions

            // === HEADER ===
            var headerPanel = new Border
            {
                Padding = new Thickness(16, 12, 16, 8)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Learned Patterns",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Browse and search patterns from your uploaded estimates",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray),
                Margin = new Thickness(0, 2, 0, 0)
            });
            headerPanel.Child = headerStack;
            Grid.SetRow(headerPanel, 0);
            mainGrid.Children.Add(headerPanel);

            // === STATS CARDS ===
            var statsPanel = BuildStatsCards();
            Grid.SetRow(statsPanel, 1);
            mainGrid.Children.Add(statsPanel);

            // === QUICK FILTERS ===
            var quickFiltersSection = BuildQuickFilters();
            Grid.SetRow(quickFiltersSection, 2);
            mainGrid.Children.Add(quickFiltersSection);

            // === CATEGORY TABS ===
            var categoryTabs = BuildCategoryTabs();
            Grid.SetRow(categoryTabs, 3);
            mainGrid.Children.Add(categoryTabs);

            // === SEARCH BAR ===
            var searchSection = BuildSearchBar();
            Grid.SetRow(searchSection, 4);
            mainGrid.Children.Add(searchSection);

            // === RESULTS AREA ===
            _resultsScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(12, 0, 12, 0)
            };

            _resultsPanel = new StackPanel { Spacing = 6 };
            _resultsScroller.Content = _resultsPanel;

            Grid.SetRow(_resultsScroller, 5);
            mainGrid.Children.Add(_resultsScroller);

            // === ACTION BUTTONS ===
            var actionsPanel = BuildActionButtons();
            Grid.SetRow(actionsPanel, 6);
            mainGrid.Children.Add(actionsPanel);

            Content = mainGrid;
        }

        private Border BuildStatsCards()
        {
            var border = new Border
            {
                Padding = new Thickness(12, 4, 12, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var estimates = _learningService.EstimatesImported;
            var patterns = _learningService.ManualLinePatternCount;
            var allPatterns = _learningService.GetAllManualLinePatterns();
            var uniqueOps = allPatterns.SelectMany(p => p.ManualLines).Select(m => m.ManualLineType).Distinct().Count();

            var estimatesCard = CreateMiniStatCard("Estimates", estimates.ToString(), AccentBlue);
            var patternsCard = CreateMiniStatCard("Part Patterns", patterns.ToString(), AccentGreen);
            var opsCard = CreateMiniStatCard("Unique Ops", uniqueOps.ToString(), AccentPurple);

            Grid.SetColumn(estimatesCard, 0);
            Grid.SetColumn(patternsCard, 2);
            Grid.SetColumn(opsCard, 4);

            grid.Children.Add(estimatesCard);
            grid.Children.Add(patternsCard);
            grid.Children.Add(opsCard);

            border.Child = grid;
            return border;
        }

        private Border CreateMiniStatCard(string label, string value, Color accentColor)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8)
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(accentColor),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(TextGray),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            card.Child = stack;
            return card;
        }

        private Border BuildQuickFilters()
        {
            var border = new Border
            {
                Padding = new Thickness(12, 4, 12, 8)
            };

            var stack = new StackPanel { Spacing = 6 };

            stack.Children.Add(new TextBlock
            {
                Text = "QUICK SEARCH",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextGray)
            });

            _quickFiltersPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };

            // Common operation quick filters
            var quickFilters = new[]
            {
                ("Scan", "\uE8B5"),
                ("Weld", "\uE945"),
                ("Cavity Wax", "\uE7F6"),
                ("DE-NIB", "\uE8CB"),
                ("Seam Seal", "\uE8EE"),
                ("Corrosion", "\uE72E"),
                ("Calibrate", "\uE9A1"),
                ("R&I", "\uE8BB")
            };

            foreach (var (name, glyph) in quickFilters)
            {
                var chip = CreateQuickFilterChip(name, glyph);
                _quickFiltersPanel.Children.Add(chip);
            }

            stack.Children.Add(_quickFiltersPanel);
            border.Child = stack;
            return border;
        }

        private Border CreateQuickFilterChip(string name, string glyph)
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 5, 10, 5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(1)
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };

            content.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 11,
                Foreground = new SolidColorBrush(AccentBlue)
            });

            content.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White)
            });

            chip.Child = content;
            chip.Tag = name;

            chip.PointerPressed += (s, e) =>
            {
                _activeQuickFilter = name;
                _searchBox!.Text = name;
                DoSearch();
                HighlightActiveQuickFilter(name);
            };

            chip.PointerEntered += (s, e) =>
            {
                if (_activeQuickFilter != name)
                    chip.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55));
            };

            chip.PointerExited += (s, e) =>
            {
                if (_activeQuickFilter != name)
                    chip.Background = new SolidColorBrush(CardBg);
            };

            return chip;
        }

        private void HighlightActiveQuickFilter(string? activeName)
        {
            if (_quickFiltersPanel == null) return;

            foreach (var child in _quickFiltersPanel.Children)
            {
                if (child is Border chip)
                {
                    var isActive = chip.Tag?.ToString() == activeName;
                    chip.Background = new SolidColorBrush(isActive ? AccentBlue : CardBg);
                    chip.BorderBrush = new SolidColorBrush(isActive ? AccentBlue : Color.FromArgb(255, 60, 60, 60));
                }
            }
        }

        private Border BuildCategoryTabs()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                Margin = new Thickness(12, 0, 12, 8),
                CornerRadius = new CornerRadius(6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _allTab = CreateCategoryTab("All", 0, true);
            _byPartTab = CreateCategoryTab("By Part", 1, false);
            _byOpTab = CreateCategoryTab("By Op", 2, false);
            _recentTab = CreateCategoryTab("Top Used", 3, false);

            Grid.SetColumn(_allTab, 0);
            Grid.SetColumn(_byPartTab, 1);
            Grid.SetColumn(_byOpTab, 2);
            Grid.SetColumn(_recentTab, 3);

            grid.Children.Add(_allTab);
            grid.Children.Add(_byPartTab);
            grid.Children.Add(_byOpTab);
            grid.Children.Add(_recentTab);

            border.Child = grid;
            return border;
        }

        private Border CreateCategoryTab(string text, int index, bool isSelected)
        {
            var tab = new Border
            {
                Background = new SolidColorBrush(isSelected ? AccentBlue : Colors.Transparent),
                Padding = new Thickness(8, 10, 8, 10),
                CornerRadius = new CornerRadius(6)
            };

            tab.Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = new SolidColorBrush(isSelected ? Colors.White : TextGray),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            tab.Tag = index;
            tab.PointerPressed += (s, e) => SelectCategory(index);
            tab.PointerEntered += (s, e) =>
            {
                if (_selectedCategory != index)
                    tab.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55));
            };
            tab.PointerExited += (s, e) =>
            {
                if (_selectedCategory != index)
                    tab.Background = new SolidColorBrush(Colors.Transparent);
            };

            return tab;
        }

        private void SelectCategory(int index)
        {
            _selectedCategory = index;
            _activeQuickFilter = null;
            HighlightActiveQuickFilter(null);

            UpdateCategoryTabStyle(_allTab, 0);
            UpdateCategoryTabStyle(_byPartTab, 1);
            UpdateCategoryTabStyle(_byOpTab, 2);
            UpdateCategoryTabStyle(_recentTab, 3);

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
                text.Foreground = new SolidColorBrush(isSelected ? Colors.White : TextGray);
            }
        }

        private void ShowCategoryContent(int category)
        {
            _resultsPanel!.Children.Clear();
            _selectedOperations.Clear();
            _selectedOpResults.Clear();

            var allPatterns = _learningService.GetAllManualLinePatterns()
                .Where(p => p.ManualLines.Count > 0 && !string.IsNullOrEmpty(p.ParentPartName))
                .ToList();

            if (allPatterns.Count == 0)
            {
                ShowEmptyState();
                return;
            }

            switch (category)
            {
                case 0: // All - show search prompt
                    ShowAllOverview(allPatterns);
                    break;
                case 1: // By Part
                    ShowByPartView(allPatterns);
                    break;
                case 2: // By Operation
                    ShowByOperationView(allPatterns);
                    break;
                case 3: // Most Used
                    ShowMostUsedView(allPatterns);
                    break;
            }

            UpdateButtonStates();
        }

        private void ShowEmptyState()
        {
            var emptyCard = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(24),
                Margin = new Thickness(0, 20, 0, 0)
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12 };

            stack.Children.Add(new FontIcon
            {
                Glyph = "\uE8B7",
                FontSize = 48,
                Foreground = new SolidColorBrush(TextGray)
            });

            stack.Children.Add(new TextBlock
            {
                Text = "No Patterns Learned Yet",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Upload estimates in the Import tab to start learning patterns",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            emptyCard.Child = stack;
            _resultsPanel!.Children.Add(emptyCard);
        }

        private void ShowAllOverview(List<ManualLinePattern> patterns)
        {
            // Status text
            _resultsPanel!.Children.Add(new TextBlock
            {
                Text = $"You have {patterns.Count} part patterns learned. Use search or click a quick filter above.",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray),
                Margin = new Thickness(4, 8, 0, 12),
                TextWrapping = TextWrapping.Wrap
            });

            // Show top 10 most used patterns
            _resultsPanel.Children.Add(new TextBlock
            {
                Text = "TOP PATTERNS",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextGray),
                Margin = new Thickness(4, 0, 0, 8)
            });

            foreach (var pattern in patterns.OrderByDescending(p => p.ExampleCount).Take(10))
            {
                var card = CreatePatternCard(pattern);
                _resultsPanel.Children.Add(card);
            }
        }

        private void ShowByPartView(List<ManualLinePattern> patterns)
        {
            // Group by part name
            var grouped = patterns
                .GroupBy(p => NormalizePartName(p.ParentPartName ?? "Other"))
                .OrderByDescending(g => g.Sum(p => p.ExampleCount))
                .ToList();

            foreach (var group in grouped.Take(15))
            {
                var expander = CreateExpanderCard(
                    group.Key,
                    $"{group.Count()} pattern(s), {group.Sum(p => p.ExampleCount)} estimates",
                    AccentBlue,
                    () => group.ToList()
                );
                _resultsPanel!.Children.Add(expander);
            }
        }

        private void ShowByOperationView(List<ManualLinePattern> patterns)
        {
            // Get all unique operations across all patterns
            var allOps = patterns
                .SelectMany(p => p.ManualLines)
                .GroupBy(m => m.ManualLineType)
                .OrderByDescending(g => g.Sum(m => m.TimesUsed))
                .Take(20)
                .ToList();

            foreach (var opGroup in allOps)
            {
                var opCard = CreateOperationGroupCard(opGroup.Key, opGroup.Sum(m => m.TimesUsed), opGroup.ToList());
                _resultsPanel!.Children.Add(opCard);
            }
        }

        private void ShowMostUsedView(List<ManualLinePattern> patterns)
        {
            var sorted = patterns
                .OrderByDescending(p => p.ManualLines.Sum(m => m.TimesUsed))
                .Take(15)
                .ToList();

            int rank = 1;
            foreach (var pattern in sorted)
            {
                var card = CreateRankedPatternCard(pattern, rank++);
                _resultsPanel!.Children.Add(card);
            }
        }

        private string NormalizePartName(string partName)
        {
            var lower = partName.ToLowerInvariant();
            if (lower.Contains("door")) return "Door";
            if (lower.Contains("fender")) return "Fender";
            if (lower.Contains("bumper")) return "Bumper";
            if (lower.Contains("hood")) return "Hood";
            if (lower.Contains("quarter")) return "Quarter Panel";
            if (lower.Contains("roof")) return "Roof";
            if (lower.Contains("trunk") || lower.Contains("decklid")) return "Trunk/Decklid";
            if (lower.Contains("mirror")) return "Mirror";
            if (lower.Contains("headl")) return "Headlamp";
            if (lower.Contains("taill")) return "Taillamp";
            return partName;
        }

        private Border CreatePatternCard(ManualLinePattern pattern)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{pattern.ParentPartName} - {pattern.ParentOperationType}",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{pattern.ManualLines.Count} operations • {pattern.ExampleCount} estimates",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray)
            });
            Grid.SetColumn(infoStack, 0);
            grid.Children.Add(infoStack);

            // Hours badge
            var totalHours = pattern.ManualLines.Sum(m => m.LaborUnits + m.RefinishUnits);
            var hoursBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 80, 60)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            hoursBadge.Child = new TextBlock
            {
                Text = $"{totalHours:N1}h",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentGreen)
            };
            Grid.SetColumn(hoursBadge, 1);
            grid.Children.Add(hoursBadge);

            // Chevron indicator (shows it's clickable)
            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 14,
                Foreground = new SolidColorBrush(TextGray),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(chevron, 2);
            grid.Children.Add(chevron);

            card.Child = grid;

            // Make entire card clickable
            card.PointerPressed += (s, e) =>
            {
                _searchBox!.Text = $"{pattern.ParentPartName} {pattern.ParentOperationType}";
                _currentPattern = pattern;
                ShowPatternDetails(pattern);
            };

            // Hover effects
            card.PointerEntered += (s, e) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55));
                chevron.Foreground = new SolidColorBrush(AccentBlue);
            };
            card.PointerExited += (s, e) =>
            {
                card.Background = new SolidColorBrush(CardBg);
                chevron.Foreground = new SolidColorBrush(TextGray);
            };

            return card;
        }

        private Border CreateRankedPatternCard(ManualLinePattern pattern, int rank)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Rank badge
            var rankColor = rank <= 3 ? (rank == 1 ? Color.FromArgb(255, 255, 215, 0) :
                rank == 2 ? Color.FromArgb(255, 192, 192, 192) : Color.FromArgb(255, 205, 127, 50))
                : Color.FromArgb(255, 60, 60, 60);

            var rankBadge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(rankColor)
            };
            rankBadge.Child = new TextBlock
            {
                Text = rank.ToString(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(rank <= 3 ? Colors.Black : Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(rankBadge, 0);
            grid.Children.Add(rankBadge);

            var infoStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{pattern.ParentPartName} - {pattern.ParentOperationType}",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            var totalUses = pattern.ManualLines.Sum(m => m.TimesUsed);
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{pattern.ManualLines.Count} ops • {totalUses} total uses",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray)
            });
            Grid.SetColumn(infoStack, 1);
            grid.Children.Add(infoStack);

            // Chevron indicator
            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 14,
                Foreground = new SolidColorBrush(TextGray),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(chevron, 2);
            grid.Children.Add(chevron);

            card.Child = grid;

            // Make entire card clickable
            card.PointerPressed += (s, e) =>
            {
                _currentPattern = pattern;
                ShowPatternDetails(pattern);
            };

            // Hover effects
            card.PointerEntered += (s, e) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55));
                chevron.Foreground = new SolidColorBrush(AccentBlue);
            };
            card.PointerExited += (s, e) =>
            {
                card.Background = new SolidColorBrush(CardBg);
                chevron.Foreground = new SolidColorBrush(TextGray);
            };

            return card;
        }

        private Border CreateExpanderCard(string title, string subtitle, Color accentColor, Func<List<ManualLinePattern>> getPatternsFunc)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var stack = new StackPanel();

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = "\uE76C", // Chevron right
                FontSize = 12,
                Foreground = new SolidColorBrush(accentColor),
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(icon, 0);
            headerGrid.Children.Add(icon);

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            textStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray)
            });
            Grid.SetColumn(textStack, 1);
            headerGrid.Children.Add(textStack);

            var expandBtn = new Button
            {
                Content = "Expand",
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(4)
            };

            var contentPanel = new StackPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(20, 8, 0, 0),
                Spacing = 4
            };

            expandBtn.Click += (s, e) =>
            {
                if (contentPanel.Visibility == Visibility.Collapsed)
                {
                    contentPanel.Visibility = Visibility.Visible;
                    expandBtn.Content = "Collapse";
                    icon.Glyph = "\uE70D"; // Chevron down

                    // Populate content if empty
                    if (contentPanel.Children.Count == 0)
                    {
                        foreach (var p in getPatternsFunc())
                        {
                            var miniCard = CreateMiniPatternCard(p);
                            contentPanel.Children.Add(miniCard);
                        }
                    }
                }
                else
                {
                    contentPanel.Visibility = Visibility.Collapsed;
                    expandBtn.Content = "Expand";
                    icon.Glyph = "\uE76C";
                }
            };

            Grid.SetColumn(expandBtn, 2);
            headerGrid.Children.Add(expandBtn);

            stack.Children.Add(headerGrid);
            stack.Children.Add(contentPanel);
            card.Child = stack;

            return card;
        }

        private Border CreateMiniPatternCard(ManualLinePattern pattern)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 48)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = $"{pattern.ParentOperationType} • {pattern.ManualLines.Count} ops",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(chevron, 1);
            grid.Children.Add(chevron);

            card.Child = grid;

            // Make entire card clickable
            card.PointerPressed += (s, e) =>
            {
                _currentPattern = pattern;
                ShowPatternDetails(pattern);
            };

            card.PointerEntered += (s, e) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 60));
                chevron.Foreground = new SolidColorBrush(AccentBlue);
            };
            card.PointerExited += (s, e) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 48));
                chevron.Foreground = new SolidColorBrush(TextGray);
            };

            return card;
        }

        private Border CreateOperationGroupCard(string opName, int totalUses, List<ManualLineEntry> entries)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(opName) ? "(unnamed)" : opName,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            var avgLabor = entries.Where(e => e.LaborUnits > 0).Select(e => e.LaborUnits).DefaultIfEmpty(0).Average();
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Used {totalUses}x • Avg: {avgLabor:N2}h",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray)
            });
            Grid.SetColumn(infoStack, 0);
            grid.Children.Add(infoStack);

            // Uses badge
            var usesBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 50, 80)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            usesBadge.Child = new TextBlock
            {
                Text = $"{totalUses}x",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentPurple)
            };
            Grid.SetColumn(usesBadge, 1);
            grid.Children.Add(usesBadge);

            // Chevron indicator
            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 14,
                Foreground = new SolidColorBrush(TextGray),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(chevron, 2);
            grid.Children.Add(chevron);

            card.Child = grid;

            // Make entire card clickable
            card.PointerPressed += (s, e) =>
            {
                _searchBox!.Text = opName;
                DoSearch();
            };

            // Hover effects
            card.PointerEntered += (s, e) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55));
                chevron.Foreground = new SolidColorBrush(AccentBlue);
            };
            card.PointerExited += (s, e) =>
            {
                card.Background = new SolidColorBrush(CardBg);
                chevron.Foreground = new SolidColorBrush(TextGray);
            };

            return card;
        }

        private Border BuildSearchBar()
        {
            var border = new Border
            {
                Padding = new Thickness(12, 0, 12, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _searchBox = new TextBox
            {
                PlaceholderText = "Search operations or parts...",
                FontSize = 13,
                Padding = new Thickness(12, 10, 12, 10),
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                CornerRadius = new CornerRadius(6)
            };
            _searchBox.KeyDown += SearchBox_KeyDown;
            _searchBox.TextChanged += SearchBox_TextChanged;
            Grid.SetColumn(_searchBox, 0);
            grid.Children.Add(_searchBox);

            var searchBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE721", FontSize = 14 },
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 10, 12, 10),
                Background = new SolidColorBrush(AccentBlue),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6)
            };
            searchBtn.Click += (s, e) => DoSearch();
            Grid.SetColumn(searchBtn, 1);
            grid.Children.Add(searchBtn);

            border.Child = grid;
            return border;
        }

        private Border BuildActionButtons()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                Padding = new Thickness(12),
                Margin = new Thickness(12, 8, 12, 12),
                CornerRadius = new CornerRadius(6)
            };

            var stack = new StackPanel { Spacing = 8 };

            // Status text
            _statusText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray),
                TextWrapping = TextWrapping.Wrap,
                Text = "Select operations to copy or insert into CCC"
            };
            stack.Children.Add(_statusText);

            // Buttons row
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _copyButton = new Button
            {
                Padding = new Thickness(16, 10, 16, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                IsEnabled = false
            };
            var copyContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            copyContent.Children.Add(new FontIcon { Glyph = "\uE8C8", FontSize = 14 });
            copyContent.Children.Add(new TextBlock { Text = "Copy", FontSize = 12 });
            _copyButton.Content = copyContent;
            _copyButton.Click += CopyButton_Click;
            buttonPanel.Children.Add(_copyButton);

            _insertButton = new Button
            {
                Padding = new Thickness(16, 10, 16, 10),
                Background = new SolidColorBrush(AccentGreen),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                IsEnabled = false
            };
            var insertContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            insertContent.Children.Add(new FontIcon { Glyph = "\uE768", FontSize = 14 });
            insertContent.Children.Add(new TextBlock { Text = "Insert into CCC", FontSize = 12 });
            _insertButton.Content = insertContent;
            _insertButton.Click += InsertButton_Click;
            buttonPanel.Children.Add(_insertButton);

            stack.Children.Add(buttonPanel);
            border.Child = stack;
            return border;
        }

        private void UpdateStats()
        {
            // Stats are now shown in the cards at the top
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            _resultsPanel!.Children.Clear();
            _selectedOperations.Clear();

            var allPatterns = _learningService.GetAllManualLinePatterns()
                .Where(p => p.ManualLines.Count > 0 && !string.IsNullOrEmpty(p.ParentPartName))
                .OrderByDescending(p => p.ExampleCount)
                .Take(20)
                .ToList();

            if (allPatterns.Count == 0)
            {
                _statusText!.Text = "No patterns learned yet. Upload estimates in the Import tab.";
                _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 100));
                return;
            }

            _statusText!.Text = $"Top {allPatterns.Count} patterns by frequency (click to view details)";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255));

            foreach (var pattern in allPatterns)
            {
                var patternCard = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var cardGrid = new Grid();
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameStack = new StackPanel();
                nameStack.Children.Add(new TextBlock
                {
                    Text = $"{pattern.ParentPartName} - {pattern.ParentOperationType}",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                });
                nameStack.Children.Add(new TextBlock
                {
                    Text = $"{pattern.ManualLines.Count} operations • {pattern.ExampleCount} estimates",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
                });
                Grid.SetColumn(nameStack, 0);
                cardGrid.Children.Add(nameStack);

                var viewButton = new Button
                {
                    Content = "View",
                    FontSize = 10,
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
                    Foreground = new SolidColorBrush(Colors.White),
                    CornerRadius = new CornerRadius(3),
                    VerticalAlignment = VerticalAlignment.Center
                };
                viewButton.Tag = pattern;
                viewButton.Click += (s, args) =>
                {
                    if (s is Button btn && btn.Tag is ManualLinePattern p)
                    {
                        _searchBox!.Text = $"{p.ParentPartName} {p.ParentOperationType}";
                        _currentPattern = p;
                        ShowPatternDetails(p);
                    }
                };
                Grid.SetColumn(viewButton, 1);
                cardGrid.Children.Add(viewButton);

                patternCard.Child = cardGrid;
                _resultsPanel.Children.Add(patternCard);
            }

            UpdateButtonStates();
        }

        private void ShowPatternDetails(ManualLinePattern pattern)
        {
            _resultsPanel!.Children.Clear();
            _selectedOperations.Clear();
            _currentPattern = pattern;

            var exampleCount = pattern.ExampleCount;
            var totalHours = pattern.ManualLines.Sum(m => m.LaborUnits + m.RefinishUnits);
            _statusText!.Text = $"Found {pattern.ManualLines.Count} operations for {pattern.ParentPartName} {pattern.ParentOperationType}";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));

            // Summary card
            var summaryCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 45, 55)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 8)
            };
            var summaryStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            summaryStack.Children.Add(CreateStatItem("Estimates", exampleCount.ToString(), Color.FromArgb(255, 100, 180, 255)));
            summaryStack.Children.Add(CreateStatItem("Total Hrs", totalHours.ToString("N1"), Color.FromArgb(255, 100, 200, 100)));
            summaryStack.Children.Add(CreateStatItem("Avg/Est", (totalHours / Math.Max(1, exampleCount)).ToString("N1"), Color.FromArgb(255, 200, 180, 100)));
            summaryCard.Child = summaryStack;
            _resultsPanel.Children.Add(summaryCard);

            // Header row
            var headerRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

            var headerStyle = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));
            AddTextToGrid(headerRow, "", 0, headerStyle, 10);
            AddTextToGrid(headerRow, "Operation", 1, headerStyle, 10);
            AddTextToGrid(headerRow, "Labor", 2, headerStyle, 10);
            AddTextToGrid(headerRow, "Refn", 3, headerStyle, 10);
            AddTextToGrid(headerRow, "Freq", 4, headerStyle, 10);
            _resultsPanel.Children.Add(headerRow);

            // Separator
            _resultsPanel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Margin = new Thickness(0, 4, 0, 4)
            });

            // Operation rows
            foreach (var manualLine in pattern.ManualLines.OrderByDescending(m => m.TimesUsed))
            {
                var row = CreateOperationRow(manualLine, exampleCount);
                _resultsPanel.Children.Add(row);
            }

            UpdateButtonStates();
        }

        private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                DoSearch();
                e.Handled = true;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Auto-search after typing (with debounce would be better, but simple for now)
            if (_searchBox?.Text.Length >= 3)
            {
                DoSearch();
            }
        }

        private void DoSearch()
        {
            var query = _searchBox?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(query))
            {
                _statusText!.Text = "Search by operation (scan, weld, DE-NIB) or part (door replace)";
                _resultsPanel!.Children.Clear();
                _currentPattern = null;
                _currentOpStats = null;
                UpdateButtonStates();
                return;
            }

            _resultsPanel!.Children.Clear();
            _selectedOperations.Clear();
            _selectedOpResults.Clear();

            // First try operation keyword search (scan, weld, DE-NIB, cavity wax, etc.)
            _currentOpStats = _learningService.GetOperationStats(query);

            if (_currentOpStats.Occurrences.Count > 0)
            {
                ShowOperationKeywordResults(_currentOpStats);
                return;
            }

            // Fall back to part name search
            var (partName, opType) = ParseQuery(query);
            _currentPattern = _learningService.GetManualLinesForPart(partName, opType);

            if (_currentPattern == null || _currentPattern.ManualLines.Count == 0)
            {
                _statusText!.Text = $"No results for \"{query}\"\nTry: scan, weld, DE-NIB, cavity wax, door replace";
                _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 100));
                UpdateButtonStates();
                return;
            }

            ShowPatternDetails(_currentPattern);
        }

        private void ShowOperationKeywordResults(OperationAggregateStats stats)
        {
            _statusText!.Text = $"Found \"{stats.OperationName}\" in {stats.UniqueParentParts} different parts ({stats.TotalOccurrences} occurrences)";
            _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));

            // Summary card with detailed stats
            var summaryCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 45, 55)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 8)
            };
            var summaryStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            summaryStack.Children.Add(CreateStatItem("Times Used", stats.TotalOccurrences.ToString(), Color.FromArgb(255, 100, 180, 255)));
            summaryStack.Children.Add(CreateStatItem("Parts", stats.UniqueParentParts.ToString(), Color.FromArgb(255, 180, 150, 255)));
            summaryStack.Children.Add(CreateStatItem("Avg Labor", stats.AvgLaborHours.ToString("N2"), Color.FromArgb(255, 100, 200, 100)));
            summaryStack.Children.Add(CreateStatItem("Range", $"{stats.MinLaborHours:N1}-{stats.MaxLaborHours:N1}", Color.FromArgb(255, 200, 180, 100)));
            // Add price stats if available
            if (stats.AvgPrice > 0)
            {
                summaryStack.Children.Add(CreateStatItem("Avg $", stats.AvgPrice.ToString("C0"), Color.FromArgb(255, 100, 255, 180)));
                summaryStack.Children.Add(CreateStatItem("$ Range", $"{stats.MinPrice:C0}-{stats.MaxPrice:C0}", Color.FromArgb(255, 255, 200, 100)));
            }
            summaryCard.Child = summaryStack;
            _resultsPanel!.Children.Add(summaryCard);

            // Section header
            _resultsPanel.Children.Add(new TextBlock
            {
                Text = "INDIVIDUAL OCCURRENCES (click for details)",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                Margin = new Thickness(0, 8, 0, 4)
            });

            // Header row
            var headerRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); // Checkbox
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Part
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Labor
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Refn
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Price
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) }); // Count

            var headerStyle = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));
            AddTextToGrid(headerRow, "☑", 0, headerStyle, 10);
            AddTextToGrid(headerRow, "Parent Part", 1, headerStyle, 10);
            AddTextToGrid(headerRow, "Labor", 2, headerStyle, 10);
            AddTextToGrid(headerRow, "Refn", 3, headerStyle, 10);
            AddTextToGrid(headerRow, "$", 4, headerStyle, 10);
            AddTextToGrid(headerRow, "Ct", 5, headerStyle, 10);
            _resultsPanel.Children.Add(headerRow);

            // Separator
            _resultsPanel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Margin = new Thickness(0, 4, 0, 4)
            });

            // Show each occurrence grouped by parent part
            foreach (var occ in stats.Occurrences.OrderByDescending(o => o.TimesUsed).Take(25))
            {
                var row = CreateOccurrenceRow(occ);
                _resultsPanel.Children.Add(row);
            }

            if (stats.Occurrences.Count > 25)
            {
                _resultsPanel.Children.Add(new TextBlock
                {
                    Text = $"... and {stats.Occurrences.Count - 25} more",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    Margin = new Thickness(0, 8, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            UpdateButtonStates();
        }

        private Grid CreateOccurrenceRow(OperationSearchResult occ)
        {
            var row = new Grid
            {
                Padding = new Thickness(8, 8, 8, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 2, 0, 2)
            };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); // Checkbox
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Price
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });

            // Checkbox for selection
            var checkbox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0)
            };
            checkbox.Checked += (s, e) => { _selectedOpResults.Add(occ); UpdateButtonStates(); };
            checkbox.Unchecked += (s, e) => { _selectedOpResults.Remove(occ); UpdateButtonStates(); };
            Grid.SetColumn(checkbox, 0);
            row.Children.Add(checkbox);

            // Parent part and operation
            var partStack = new StackPanel();
            partStack.Children.Add(new TextBlock
            {
                Text = $"{occ.ParentPartName}",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            partStack.Children.Add(new TextBlock
            {
                Text = $"{occ.ParentOperationType} • {occ.Description}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(partStack, 1);
            row.Children.Add(partStack);

            // Labor hours
            var laborText = new TextBlock
            {
                Text = occ.LaborHours > 0 ? $"{occ.LaborHours:N2}" : "-",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 150)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(laborText, 2);
            row.Children.Add(laborText);

            // Refinish hours
            var refnText = new TextBlock
            {
                Text = occ.RefinishHours > 0 ? $"{occ.RefinishHours:N2}" : "-",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(refnText, 3);
            row.Children.Add(refnText);

            // Price
            var priceText = new TextBlock
            {
                Text = occ.AvgPrice > 0 ? $"${occ.AvgPrice:N0}" : "-",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(priceText, 4);
            row.Children.Add(priceText);

            // Times used
            var countText = new TextBlock
            {
                Text = $"{occ.TimesUsed}x",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(countText, 5);
            row.Children.Add(countText);

            // Make row clickable to show detailed stats
            row.PointerPressed += (s, e) =>
            {
                ShowOperationDetails(occ);
            };
            row.PointerEntered += (s, e) => row.Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 55));
            row.PointerExited += (s, e) => row.Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40));

            return row;
        }

        /// <summary>
        /// Show detailed stats for a specific operation occurrence
        /// </summary>
        private void ShowOperationDetails(OperationSearchResult occ)
        {
            _resultsPanel!.Children.Clear();
            _selectedOpResults.Clear();

            // Header with back button
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var backButton = new Button
            {
                Content = "← Back",
                FontSize = 11,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50))
            };
            backButton.Click += (s, e) =>
            {
                // Re-run the search
                DoSearch();
            };
            headerPanel.Children.Add(backButton);
            headerPanel.Children.Add(new TextBlock
            {
                Text = occ.OperationName,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            _resultsPanel.Children.Add(headerPanel);

            // Parent info
            _resultsPanel.Children.Add(new TextBlock
            {
                Text = $"For: {occ.ParentPartName} ({occ.ParentOperationType})",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(0, 8, 0, 12)
            });

            // Stats grid
            var statsGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.RowDefinitions.Add(new RowDefinition());
            statsGrid.RowDefinitions.Add(new RowDefinition());
            statsGrid.RowDefinitions.Add(new RowDefinition());
            statsGrid.RowDefinitions.Add(new RowDefinition());
            statsGrid.RowDefinitions.Add(new RowDefinition());
            statsGrid.RowDefinitions.Add(new RowDefinition());

            var labelStyle = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));
            var valueStyle = new SolidColorBrush(Colors.White);

            AddStatRow(statsGrid, 0, "Times Used:", $"{occ.TimesUsed}x", labelStyle, valueStyle);
            AddStatRow(statsGrid, 1, "Labor Hours:", occ.LaborHours > 0 ? $"{occ.LaborHours:N2} (range: {occ.MinLaborHours:N2}-{occ.MaxLaborHours:N2})" : "N/A", labelStyle, valueStyle);
            AddStatRow(statsGrid, 2, "Refinish Hours:", occ.RefinishHours > 0 ? $"{occ.RefinishHours:N2} (range: {occ.MinRefinishHours:N2}-{occ.MaxRefinishHours:N2})" : "N/A", labelStyle, valueStyle);
            AddStatRow(statsGrid, 3, "Price:", occ.AvgPrice > 0 ? $"${occ.AvgPrice:N0} avg (${occ.MinPrice:N0}-${occ.MaxPrice:N0})" : "N/A", labelStyle, valueStyle);
            AddStatRow(statsGrid, 4, "Labor Type:", !string.IsNullOrEmpty(occ.LaborType) ? occ.LaborType : "Body", labelStyle, valueStyle);
            AddStatRow(statsGrid, 5, "Last Seen:", occ.LastSeen != DateTime.MinValue ? occ.LastSeen.ToString("MMM d, yyyy") : "Unknown", labelStyle, valueStyle);

            _resultsPanel.Children.Add(statsGrid);

            // Wording variations
            if (occ.WordingVariations != null && occ.WordingVariations.Count > 0)
            {
                _resultsPanel.Children.Add(new TextBlock
                {
                    Text = "WORDING VARIATIONS USED:",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                    Margin = new Thickness(0, 8, 0, 4)
                });

                foreach (var wording in occ.WordingVariations.Take(5))
                {
                    var wordingBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    wordingBorder.Child = new TextBlock
                    {
                        Text = $"\"{wording}\"",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                        TextWrapping = TextWrapping.Wrap
                    };
                    _resultsPanel.Children.Add(wordingBorder);
                }
            }

            // Use this button
            var useButton = new Button
            {
                Content = "✓ Select for Insert",
                FontSize = 12,
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            useButton.Click += (s, e) =>
            {
                _selectedOpResults.Add(occ);
                UpdateButtonStates();
                _statusText!.Text = $"Selected: {occ.OperationName} - click Insert to add to CCC";
                _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));
            };
            _resultsPanel.Children.Add(useButton);

            UpdateButtonStates();
        }

        private void AddStatRow(Grid grid, int row, string label, string value, SolidColorBrush labelStyle, SolidColorBrush valueStyle)
        {
            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = labelStyle,
                Margin = new Thickness(0, 4, 16, 4)
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                FontSize = 11,
                Foreground = valueStyle,
                Margin = new Thickness(0, 4, 0, 4)
            };
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);
        }

        private (string partName, string? opType) ParseQuery(string query)
        {
            var lower = query.ToLowerInvariant();

            // Extract operation type
            string? opType = null;
            if (lower.Contains("replace") || lower.Contains("repl")) opType = "replace";
            else if (lower.Contains("repair") || lower.Contains("rpr")) opType = "repair";
            else if (lower.Contains("blend") || lower.Contains("blnd")) opType = "blend";
            else if (lower.Contains("refinish") || lower.Contains("refn")) opType = "refinish";
            else if (lower.Contains("r&i") || lower.Contains("r+i")) opType = "r&i";

            // Extract part name (remove operation words)
            var partName = lower
                .Replace("replace", "").Replace("repl", "")
                .Replace("repair", "").Replace("rpr", "")
                .Replace("blend", "").Replace("blnd", "")
                .Replace("refinish", "").Replace("refn", "")
                .Replace("r&i", "").Replace("r+i", "")
                .Trim();

            // Normalize common part names
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

        private Grid CreateOperationRow(ManualLineEntry manualLine, int totalExamples)
        {
            var row = new Grid
            {
                Padding = new Thickness(4, 6, 4, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 2, 0, 2)
            };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); // Checkbox
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Description
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Labor
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Refinish
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) }); // Frequency

            // Checkbox
            var checkbox = new CheckBox
            {
                IsChecked = true,
                Margin = new Thickness(4, 0, 0, 0),
                MinWidth = 20
            };
            checkbox.Tag = manualLine;
            checkbox.Checked += (s, e) => { _selectedOperations.Add(manualLine); UpdateButtonStates(); };
            checkbox.Unchecked += (s, e) => { _selectedOperations.Remove(manualLine); UpdateButtonStates(); };
            _selectedOperations.Add(manualLine); // Initially selected
            Grid.SetColumn(checkbox, 0);
            row.Children.Add(checkbox);

            // Description
            var descText = new TextBlock
            {
                Text = manualLine.ManualLineType.Length > 0 ? manualLine.ManualLineType : manualLine.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(descText, 1);
            row.Children.Add(descText);

            // Labor hours
            var laborText = new TextBlock
            {
                Text = manualLine.LaborUnits > 0 ? $"{manualLine.LaborUnits:N1}" : "-",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 150)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(laborText, 2);
            row.Children.Add(laborText);

            // Refinish hours
            var refinishText = new TextBlock
            {
                Text = manualLine.RefinishUnits > 0 ? $"{manualLine.RefinishUnits:N1}" : "-",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(refinishText, 3);
            row.Children.Add(refinishText);

            // Frequency percentage (how often this op appears)
            var frequency = totalExamples > 0 ? (manualLine.TimesUsed * 100.0 / totalExamples) : 0;
            var freqColor = frequency >= 80 ? Color.FromArgb(255, 100, 200, 100) :
                           frequency >= 50 ? Color.FromArgb(255, 200, 180, 100) :
                           Color.FromArgb(255, 150, 150, 150);
            var freqText = new TextBlock
            {
                Text = $"{frequency:N0}%",
                FontSize = 11,
                FontWeight = frequency >= 80 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = new SolidColorBrush(freqColor),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(freqText, 4);
            row.Children.Add(freqText);

            return row;
        }

        private void AddTextToGrid(Grid grid, string text, int column, SolidColorBrush foreground, int fontSize)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            if (column >= 2) tb.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(tb, column);
            grid.Children.Add(tb);
        }

        private StackPanel CreateStatItem(string label, string value, Color valueColor)
        {
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(valueColor)
            });
            return stack;
        }

        private void UpdateButtonStates()
        {
            var hasSelection = _selectedOperations.Count > 0 || _selectedOpResults.Count > 0;
            _copyButton!.IsEnabled = hasSelection;
            _insertButton!.IsEnabled = hasSelection;
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedOperations.Count == 0 && _selectedOpResults.Count == 0) return;

            var lines = new System.Text.StringBuilder();

            // From pattern detail selections
            foreach (var op in _selectedOperations)
            {
                var desc = op.ManualLineType.Length > 0 ? op.ManualLineType : op.Description;
                var hours = op.LaborUnits > 0 ? op.LaborUnits : op.RefinishUnits;
                var laborType = op.RefinishUnits > 0 ? "Refn" : "Rpr";
                var price = op.AvgPrice > 0 ? $"\t${op.AvgPrice:N0}" : "";
                lines.AppendLine($"#\t{laborType}\t{desc}\t{hours:N1}{price}");
            }

            // From operation search selections
            foreach (var op in _selectedOpResults)
            {
                var desc = op.OperationName.Length > 0 ? op.OperationName : op.Description;
                var hours = op.LaborHours > 0 ? op.LaborHours : op.RefinishHours;
                var laborType = op.RefinishHours > 0 ? "Refn" : "Rpr";
                var price = op.AvgPrice > 0 ? $"\t${op.AvgPrice:N0}" : "";
                lines.AppendLine($"#\t{laborType}\t{desc}\t{hours:N1}{price}");
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(lines.ToString());
            Clipboard.SetContent(dataPackage);

            if (sender is Button btn)
            {
                var original = btn.Content;
                btn.Content = "✓ Copied!";
                await System.Threading.Tasks.Task.Delay(1500);
                btn.Content = original;
            }
        }

        private async void InsertButton_Click(object sender, RoutedEventArgs e)
        {
            // Build rows from either ManualLineEntry or OperationSearchResult selections
            var rows = new List<string[]>();

            // From pattern detail selections
            foreach (var op in _selectedOperations)
            {
                var desc = op.ManualLineType.Length > 0 ? op.ManualLineType : op.Description;
                var hours = op.LaborUnits > 0 ? op.LaborUnits : op.RefinishUnits;
                var laborType = op.RefinishUnits > 0 ? "Refn" : "Rpr";
                rows.Add(new[] { "#", laborType, desc, hours.ToString("N1") });
            }

            // From operation search selections
            foreach (var op in _selectedOpResults)
            {
                var desc = op.OperationName.Length > 0 ? op.OperationName : op.Description;
                var hours = op.LaborHours > 0 ? op.LaborHours : op.RefinishHours;
                var laborType = op.RefinishHours > 0 ? "Refn" : "Rpr";
                rows.Add(new[] { "#", laborType, desc, hours.ToString("N1") });
            }

            if (rows.Count == 0) return;

            if (sender is Button btn)
            {
                btn.Content = "🎯 Click in CCC, press ENTER...";
                btn.IsEnabled = false;

                try
                {
                    _statusText!.Text = $"🎯 Position cursor in CCC, press ENTER to paste {rows.Count} lines. ESC to cancel.";

                    var result = await _typeItService.AimAndTypeAsync(rows.ToArray());

                    if (result.Completed)
                    {
                        _statusText.Text = $"✅ Inserted {result.RowsCompleted} lines into CCC!";
                        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));
                    }
                    else if (result.WasCancelled)
                    {
                        _statusText.Text = "Insert cancelled.";
                        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 100));
                    }
                    else
                    {
                        _statusText.Text = $"Insert incomplete: {result.RowsCompleted}/{result.TotalRows} lines.";
                        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 100, 100));
                    }
                }
                catch (Exception ex)
                {
                    _statusText!.Text = $"Error: {ex.Message}";
                    _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 100, 100));
                }
                finally
                {
                    btn.Content = "▶ Insert into CCC";
                    btn.IsEnabled = true;
                }
            }
        }
    }
}
